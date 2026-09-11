using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using Serilog.Events;

namespace StdLogger;

internal sealed class LineLogger
{
    private readonly ILogger _logger;
    private readonly IReadOnlyList<LevelPattern> _patterns;

    public LineLogger(ILogger logger, IReadOnlyList<LevelPattern>? patterns = null)
    {
        _logger = logger;
        _patterns = (patterns ?? LevelPattern.Defaults)
            .OrderByDescending(p => MapLevel(p.Level))
            .ToList();
    }

    public static LogEventLevel MapLevel(string levelName) => levelName.ToUpperInvariant() switch
    {
        "TRACE" or "VERBOSE" => LogEventLevel.Verbose,
        "DEBUG" => LogEventLevel.Debug,
        "INFO" or "INFORMATION" => LogEventLevel.Information,
        "WARN" or "WARNING" => LogEventLevel.Warning,
        "ERROR" => LogEventLevel.Error,
        "FATAL" or "CRITICAL" => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };

    public LogEventLevel ParseLine(string line, bool isError)
    {
        foreach (var pattern in _patterns)
        {
            if (pattern.Match(line).Success)
                return MapLevel(pattern.Level);
        }

        return isError ? LogEventLevel.Error : LogEventLevel.Information;
    }

    public void Write(string line, bool isError)
    {
        var level = ParseLine(line, isError);
        _logger.Write(level, "{Line}", line);
    }

    public void Header(string command, string? sinksInfo = null)
    {
        if (string.IsNullOrEmpty(sinksInfo))
            _logger.Information("StdLogger for {Command}", command);
        else
            _logger.Information("StdLogger for {Command}; sinks: {Sinks}", command, sinksInfo);
    }

    public void StartError(string command, string error)
    {
        _logger.Error("Failed to start {Command}: {Error}", command, error);
    }
}