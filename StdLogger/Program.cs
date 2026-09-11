using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;

namespace StdLogger;

internal static class Program
{
    private const string DefaultConfigName = "appsettings.json";

    static async Task<int> Main(string[] args)
    {
        if (!TryParseArgs(args, out string? logPath, out string? configFile, out string[]? command, out string? error))
        {
            Console.Error.WriteLine(error);
            PrintUsage();
            return 1;
        }

        Logger logger;
        IReadOnlyList<LevelPattern> patterns;
        string sinksInfo;
        try
        {
            (logger, patterns, sinksInfo) = CreateLogger(logPath, configFile);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to configure logger: {ex.Message}");
            return 1;
        }

        using (logger)
        {
            var lineLogger = new LineLogger(logger, patterns);

            if (command is null or { Length: 0 })
            {
                await ReadStdin(Console.In, lineLogger);
                return 0;
            }

            var runner = new ProcessRunner(lineLogger, sinksInfo);
            return await runner.RunAsync(command);
        }
    }

    private static bool TryParseArgs(string[] args, out string? logPath, out string? configFile, out string[]? command, out string? error)
    {
        logPath = null;
        configFile = null;
        command = null;
        error = null;

        var commandArgs = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--path":
                    if (i + 1 >= args.Length)
                    {
                        error = "Missing value for --path.";
                        return false;
                    }
                    logPath = args[++i];
                    break;

                case "--config":
                    if (i + 1 >= args.Length)
                    {
                        error = "Missing value for --config.";
                        return false;
                    }
                    configFile = args[++i];
                    break;

                default:
                    commandArgs.Add(args[i]);
                    break;
            }
        }

        if (logPath == null && configFile == null)
        {
            error = "Missing required --path or --config option.";
            return false;
        }

        if (logPath != null && configFile != null)
        {
            error = "Both --path and --config cannot be used together.";
            return false;
        }

        command = commandArgs.ToArray();
        return true;
    }

    private static (Logger logger, IReadOnlyList<LevelPattern> patterns, string sinksInfo) CreateLogger(string? logPath, string? configFile)
    {
        var builder = new LoggerConfiguration();

        if (configFile != null)
        {
            if (!File.Exists(configFile))
                throw new FileNotFoundException($"Config file not found: {configFile}");

            string configDir = Path.GetDirectoryName(Path.GetFullPath(configFile)) ?? string.Empty;

            var config = new ConfigurationBuilder()
                .AddJsonFile(configFile, optional: false)
                .Build();

            var overrides = ResolveFileSinkPaths(config, configDir, logDir: null);
            if (overrides.Count > 0)
            {
                config = new ConfigurationBuilder()
                    .AddConfiguration(config)
                    .AddInMemoryCollection(overrides)
                    .Build();
            }

            var logger = builder.ReadFrom.Configuration(config).CreateLogger();
            var patterns = LevelPattern.ReadFrom(config);
            var sinksInfo = DescribeSinks(config, configDir);
            return (logger, patterns, sinksInfo);
        }

        string defaultConfig = Path.Combine(AppContext.BaseDirectory, DefaultConfigName);
        if (!File.Exists(defaultConfig))
            throw new FileNotFoundException($"Default config not found next to executable: {defaultConfig}");

        var baseConfig = new ConfigurationBuilder()
            .AddJsonFile(defaultConfig, optional: false)
            .Build();

        var pathOverrides = OverrideFileSinkPath(baseConfig, logPath!, AppContext.BaseDirectory);
        if (pathOverrides.Count > 0)
        {
            baseConfig = new ConfigurationBuilder()
                .AddConfiguration(baseConfig)
                .AddInMemoryCollection(pathOverrides)
                .Build();
        }

        var logger2 = builder.ReadFrom.Configuration(baseConfig).CreateLogger();
        var patterns2 = LevelPattern.ReadFrom(baseConfig);
        var sinksInfo2 = DescribeSinks(baseConfig, AppContext.BaseDirectory);
        return (logger2, patterns2, sinksInfo2);
    }

    private static string DescribeSinks(IConfiguration config, string baseDir)
    {
        var parts = new List<string>();
        var writeTo = config.GetSection("Serilog:WriteTo");

        int index = 0;
        foreach (var sink in writeTo.GetChildren())
        {
            string name = sink["Name"] ?? "";
            switch (name.ToLowerInvariant())
            {
                case "console":
                    parts.Add("Console");
                    break;

                case "file":
                    string? path = sink["Args:path"];
                    if (string.IsNullOrEmpty(path))
                    {
                        parts.Add("File");
                        break;
                    }

                    string? rolling = sink["Args:rollingInterval"];
                    string resolved = ResolveFileSinkPath(path, rolling, baseDir);
                    if (string.IsNullOrEmpty(rolling))
                        parts.Add($"File -> {resolved}");
                    else
                        parts.Add($"File -> {resolved} (rolling: {rolling})");
                    break;

                default:
                    if (!string.IsNullOrEmpty(name))
                        parts.Add(name);
                    break;
            }

            index++;
        }

        string minLevel = config["Serilog:MinimumLevel:Default"] ?? "Information";
        string sinksText = parts.Count > 0 ? string.Join(", ", parts) : "none";
        return $"{sinksText}; min level: {minLevel}";
    }

    private static string ResolveFileSinkPath(string path, string? rollingInterval, string baseDir)
    {
        string fullPath = path;
        if (!Path.IsPathRooted(fullPath))
            fullPath = Path.Combine(baseDir, fullPath);
        fullPath = Path.GetFullPath(fullPath);

        if (string.IsNullOrEmpty(rollingInterval))
            return fullPath;

        string dateStamp = rollingInterval.ToLowerInvariant() switch
        {
            "year" => DateTime.Now.ToString("yyyy"),
            "month" => DateTime.Now.ToString("yyyyMM"),
            "day" => DateTime.Now.ToString("yyyyMMdd"),
            "hour" => DateTime.Now.ToString("yyyyMMddHH"),
            "minute" => DateTime.Now.ToString("yyyyMMddHHmm"),
            _ => "",
        };

        if (dateStamp.Length == 0)
            return fullPath;

        string dir = Path.GetDirectoryName(fullPath) ?? string.Empty;
        string fileName = Path.GetFileName(fullPath);

        string ext = Path.GetExtension(fileName);
        string stem = fileName[..^ext.Length];
        return Path.Combine(dir, $"{stem}{dateStamp}{ext}");
    }

    internal static Dictionary<string, string?> OverrideFileSinkPath(IConfiguration config, string logDir, string baseDir)
    {
        var overrides = new Dictionary<string, string?>();
        var writeTo = config.GetSection("Serilog:WriteTo");

        string resolvedLogDir = Path.IsPathRooted(logDir) ? logDir : Path.Combine(baseDir, logDir);

        int index = 0;
        foreach (var sink in writeTo.GetChildren())
        {
            if (string.Equals(sink["Name"], "File", StringComparison.OrdinalIgnoreCase))
            {
                string? path = sink["Args:path"];
                if (!string.IsNullOrEmpty(path))
                {
                    string fileName = Path.GetFileName(path);
                    string newPath = string.IsNullOrEmpty(fileName) ? "log-.log" : fileName;
                    newPath = Path.Combine(resolvedLogDir, newPath);
                    overrides[$"Serilog:WriteTo:{index}:Args:path"] = Path.GetFullPath(newPath);
                }
            }
            index++;
        }

        return overrides;
    }

    internal static Dictionary<string, string?> ResolveFileSinkPaths(IConfiguration config, string baseDir, string? logDir)
    {
        var overrides = new Dictionary<string, string?>();
        var writeTo = config.GetSection("Serilog:WriteTo");

        int index = 0;
        foreach (var sink in writeTo.GetChildren())
        {
            if (string.Equals(sink["Name"], "File", StringComparison.OrdinalIgnoreCase))
            {
                string? path = sink["Args:path"];
                if (string.IsNullOrEmpty(path))
                    continue;

                string fullPath;
                if (string.IsNullOrEmpty(logDir))
                {
                    fullPath = Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path);
                }
                else
                {
                    string fileName = Path.GetFileName(path);
                    string newPath = string.IsNullOrEmpty(fileName) ? "log-.log" : fileName;
                    string resolvedLogDir = Path.IsPathRooted(logDir) ? logDir : Path.Combine(baseDir, logDir);
                    fullPath = Path.Combine(resolvedLogDir, newPath);
                }

                overrides[$"Serilog:WriteTo:{index}:Args:path"] = Path.GetFullPath(fullPath);
            }
            index++;
        }

        return overrides;
    }

    private static async Task ReadStdin(TextReader reader, LineLogger logger)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line == null)
                break;

            logger.Write(line, isError: false);
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  StdLogger --path <dir> [command args...]");
        Console.Error.WriteLine("  StdLogger --config <config.json> [command args...]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  --path <dir>       Write logs to <dir> using the default appsettings.json");
        Console.Error.WriteLine("                     located next to the executable.");
        Console.Error.WriteLine("  --config <file>    Use all Serilog settings from the given JSON config file.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  If a command is given, it is run and its stdout/stderr are logged;");
        Console.Error.WriteLine("  otherwise stdin is read and each line is logged.");
    }
}