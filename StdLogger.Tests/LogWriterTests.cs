using System;
using System.IO;

namespace StdLogger.Tests;

public class LogWriterTests : IDisposable
{
    private readonly string _tempDir;

    public LogWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StdLogger.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static LogWriter NewWriter(string dir, Func<DateTime>? clock = null)
        => new LogWriter(dir, clock ?? (() => DateTime.Now));

    [Theory]
    [InlineData("[ERROR] boom", true, "ERROR")]
    [InlineData("[error] boom", true, "ERROR")]
    [InlineData("[WARN] careful", false, "WARN")]
    [InlineData("[WARNING] careful", false, "WARNING")]
    [InlineData("[INFO] hi", false, "INFO")]
    [InlineData("[DEBUG] x", false, "DEBUG")]
    [InlineData("[TRACE] x", true, "TRACE")]
    [InlineData("[FATAL] x", false, "FATAL")]
    [InlineData("[CRITICAL] x", false, "CRITICAL")]
    [InlineData("Warning: 'dnssync.cs' appears to be a file-based app but was passed as an argument to the project", true, "WARNING")]
    [InlineData("c:\\Scripts\\simpledns2router\\dnssync.csproj : warning NU1903: Package 'SSH.NET' 2025.0.0 has a", true, "WARNING")]
    [InlineData("c:\\Scripts\\simpledns2router\\dnssync.csproj : warning NU1903: Package 'SSH.NET' 2025.0.0 has a", false, "WARNING")]
    public void ParseLine_DetectsKnownLevel_CaseInsensitive(string line, bool isError, string expected)
    {
        var writer = NewWriter(_tempDir);

        (string level, string message) = writer.ParseLine(line, isError);

        Assert.Equal(expected, level);
        Assert.Equal(line, message);
    }

    [Theory]
    [InlineData(false, "INFO")]
    [InlineData(true, "ERROR")]
    public void ParseLine_DefaultsLevel_WhenNoTagFound(bool isError, string expected)
    {
        var writer = NewWriter(_tempDir);

        (string level, string _) = writer.ParseLine("some random output", isError);

        Assert.Equal(expected, level);
    }

    [Fact]
    public void ParseLine_UnknownTagOutsideBrackets_NotTreatedAsLevel()
    {
        var writer = NewWriter(_tempDir);

        (string level, string _) = writer.ParseLine("just [INFO-ish] text", isError: false);

        Assert.Equal("INFO", level);
    }

    [Fact]
    public void Write_CreatesDatedFile_WithFormattedEntry()
    {
        var writer = NewWriter(_tempDir);

        writer.Write("INFO", "hello world");

        var file = Assert.Single(Directory.GetFiles(_tempDir, "log_*.log"));
        string content = File.ReadAllText(file);

        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] \[INFO \] hello world\r?$", content);
    }

    [Fact]
    public void Write_AppendsToSameDateFile()
    {
        var writer = NewWriter(_tempDir);

        writer.Write("INFO", "first");
        writer.Write("ERROR", "second");

        var file = Assert.Single(Directory.GetFiles(_tempDir, "log_*.log"));
        string[] lines = File.ReadAllLines(file);

        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, l => l.EndsWith("[INFO ] first"));
        Assert.Contains(lines, l => l.EndsWith("[ERROR] second"));
    }

    [Fact]
    public void Write_CreatesLogDirectory_IfMissing()
    {
        string nested = Path.Combine(_tempDir, "a", "b");
        var writer = NewWriter(nested);

        writer.Write("WARN", "creates dirs");

        Assert.True(Directory.Exists(nested));
        Assert.NotEmpty(Directory.GetFiles(nested, "log_*.log"));
    }

    [Fact]
    public void Write_UsesInjectedClock_ForTimestampAndFileName()
    {
        var fixedTime = new DateTime(2026, 8, 28, 15, 30, 45);
        var writer = NewWriter(_tempDir, () => fixedTime);

        writer.Write("INFO", "timed message");

        var file = Assert.Single(Directory.GetFiles(_tempDir, "log_2026-08-28.log"));
        string content = File.ReadAllText(file);

        var marker = "2026-08-28 15:30:45";
        Assert.Contains(marker, content);
        Assert.StartsWith($"[{marker}]", content);
    }
}
