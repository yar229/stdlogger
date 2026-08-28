using System.IO;

namespace StdLogger.Tests;

public class ProgramTests : IDisposable
{
    private readonly string _tempDir;

    public ProgramTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StdLogger.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

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
        (string level, string message) = Program.ParseLine(line, isError);

        Assert.Equal(expected, level);
        Assert.Equal(line, message);
    }

    [Theory]
    [InlineData(false, "INFO")]
    [InlineData(true, "ERROR")]
    public void ParseLine_DefaultsLevel_WhenNoTagFound(bool isError, string expected)
    {
        (string level, string _) = Program.ParseLine("some random output", isError);

        Assert.Equal(expected, level);
    }

    [Fact]
    public void ParseLine_UnknownTagOutsideBrackets_NotTreatedAsLevel()
    {
        (string level, string _) = Program.ParseLine("just [INFO-ish] text", isError: false);

        Assert.Equal("INFO", level);
    }

    [Fact]
    public void WriteLog_CreatesDatedFile_WithFormattedEntry()
    {
        Program.WriteLog(_tempDir, "INFO", "hello world");

        var file = Assert.Single(Directory.GetFiles(_tempDir, "log_*.log"));
        string content = File.ReadAllText(file);

        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] \[INFO \] hello world\r?$", content);
    }

    [Fact]
    public void WriteLog_AppendsToSameDateFile()
    {
        Program.WriteLog(_tempDir, "INFO", "first");
        Program.WriteLog(_tempDir, "ERROR", "second");

        var file = Assert.Single(Directory.GetFiles(_tempDir, "log_*.log"));
        string[] lines = File.ReadAllLines(file);

        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, l => l.EndsWith("[INFO ] first"));
        Assert.Contains(lines, l => l.EndsWith("[ERROR] second"));
    }

    [Fact]
    public void WriteLog_CreatesLogDirectory_IfMissing()
    {
        string nested = Path.Combine(_tempDir, "a", "b");

        Program.WriteLog(nested, "WARN", "creates dirs");

        Assert.True(Directory.Exists(nested));
        Assert.NotEmpty(Directory.GetFiles(nested, "log_*.log"));
    }
}
