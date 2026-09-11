using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace StdLogger;

internal sealed class LevelPattern
{
    private readonly Regex _regex;

    public string Level { get; }

    public LevelPattern(string level, string pattern)
    {
        Level = level;
        _regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);
    }

    public Match Match(string line) => _regex.Match(line);

    public static IReadOnlyList<LevelPattern> ReadFrom(IConfiguration config)
    {
        var patterns = new List<LevelPattern>();

        foreach (var child in config.GetSection("StdLogger:LevelPatterns").GetChildren())
        {
            string? level = child["Level"];
            string? pattern = child["Pattern"];
            if (!string.IsNullOrWhiteSpace(level) && !string.IsNullOrWhiteSpace(pattern))
                patterns.Add(new LevelPattern(level, pattern));
        }

        return patterns.Count > 0 ? patterns : Defaults;
    }

    internal static IReadOnlyList<LevelPattern> Defaults { get; } = new[]
    {
        new LevelPattern("Fatal", @"\]? FATAL (\s*\w{2}\d+)? (\]|\:)"),
        new LevelPattern("Fatal", @"\]? CRITICAL (\s*\w{2}\d+)? (\]|\:)"),
        new LevelPattern("Error", @"\]? ERROR (\s*\w{2}\d+)? (\]|\:)"),
        new LevelPattern("Warning", @"\]? WARN(?:ING)? (\s*\w{2}\d+)? (\]|\:)"),
        new LevelPattern("Information", @"\]? INFO (\s*\w{2}\d+)? (\]|\:)"),
        new LevelPattern("Debug", @"\]? DEBUG (\s*\w{2}\d+)? (\]|\:)"),
        new LevelPattern("Verbose", @"\]? TRACE (\s*\w{2}\d+)? (\]|\:)"),
    };
}