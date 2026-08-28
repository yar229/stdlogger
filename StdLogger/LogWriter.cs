using System;
using System.IO;
using System.Text.RegularExpressions;

namespace StdLogger;

internal sealed partial class LogWriter
{
    private readonly string _logDir;
    private readonly Func<DateTime> _clock;
    private readonly object _lock = new();
    private bool _dirCreated;

    public LogWriter(string logDir, Func<DateTime>? clock = null)
    {
        _logDir = logDir;
        _clock = clock ?? (() => DateTime.Now);
    }

    public (string level, string message) ParseLine(string line, bool isError)
    {
        var match = LevelRegex().Match(line);
        if (match.Success)
            return (match.Groups[1].Value.ToUpperInvariant(), line);

        string level = isError ? "ERROR" : "INFO";
        return (level, line);
    }

    public void Write(string level, string message)
    {
        string now = _clock().ToString("yyyy-MM-dd HH:mm:ss");
        string logFile = Path.Combine(_logDir, $"log_{now[..10]}.log");

        lock (_lock)
        {
            if (!_dirCreated)
            {
                Directory.CreateDirectory(_logDir);
                _dirCreated = true;
            }
            File.AppendAllText(logFile, $"[{now}] [{level,-5}] {message}{Environment.NewLine}");
        }
    }

    [GeneratedRegex(@"\]? (TRACE|DEBUG|INFO|WARN(?:ING)?|ERROR|FATAL|CRITICAL) (\s*\w{2}\d+)? (\]|\:)", RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex LevelRegex();
}
