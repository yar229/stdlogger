using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StdLogger;

internal static partial class Program
{
    private static readonly object Lock = new();

    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: StdLogger <logdir> [command args...]");
            Console.Error.WriteLine("  Reads stdin and logs to <logdir>.");
            Console.Error.WriteLine("  If a command is given, runs it and logs its stdout and stderr.");
            return 1;
        }

        string logDir = args[0];
        Directory.CreateDirectory(logDir);

        string[] rest = args[1..];
        if (rest.Length == 0)
        {
            await ReadStream(Console.In, logDir, isError: false);
            return 0;
        }

        WriteLog(logDir, "INFO", $"StdLogger  for '{string.Join(" ", rest)}'");

        return await RunCommand(rest, logDir);
    }

    private static async Task<int> RunCommand(string[] cmd, string logDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cmd[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        for (int i = 1; i < cmd.Length; i++)
            psi.ArgumentList.Add(cmd[i]);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Start();

        var stdoutTask = ReadStreamAsync(process.StandardOutput, logDir, isError: false);
        var stderrTask = ReadStreamAsync(process.StandardError, logDir, isError: true);

        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static async Task ReadStreamAsync(TextReader reader, string logDir, bool isError)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line == null)
                break;

            (string level, string message) = ParseLine(line, isError);

            if (!isError)
                Console.WriteLine(line);

            WriteLog(logDir, level, message);
        }
    }

    private static async Task ReadStream(TextReader reader, string logDir, bool isError)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line == null)
                break;

            (string level, string _) = ParseLine(line, isError);
            WriteLog(logDir, level, line);
        }
    }

    internal static (string level, string message) ParseLine(string line, bool isError)
    {
        var match = LevelRegex().Match(line);
        if (match.Success)
            return (match.Groups[1].Value.ToUpperInvariant(), line);

        string level = isError ? "ERROR" : "INFO";
        return (level, line);
    }

    internal static void WriteLog(string logDir, string level, string message)
    {
        string date = DateTime.Now.ToString("yyyy-MM-dd");
        string time = DateTime.Now.ToString("HH:mm:ss");
        string logFile = Path.Combine(logDir, $"log_{date}.log");

        lock (Lock)
        {
            Directory.CreateDirectory(logDir);
            File.AppendAllText(logFile, $"[{date} {time}] [{level,-5}] {message}{Environment.NewLine}");
        }
    }

    //[GeneratedRegex(@"\[(TRACE|DEBUG|INFO|WARN(?:ING)?|ERROR|FATAL|CRITICAL)\]", RegexOptions.IgnoreCase)]
    [GeneratedRegex(@"\]? (TRACE|DEBUG|INFO|WARN(?:ING)?|ERROR|FATAL|CRITICAL) (\s*\w{2}\d+)? (\]|\:)", RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex LevelRegex();
}
