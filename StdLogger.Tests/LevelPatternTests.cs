using Microsoft.Extensions.Configuration;

namespace StdLogger.Tests;

public class LevelPatternTests
{
    private static IConfiguration BuildConfig(string json)
    {
        return new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();
    }

    [Fact]
    public void ReadFrom_ReturnsDefaults_WhenSectionMissing()
    {
        var config = BuildConfig("""{ "Serilog": { "MinimumLevel": "Information" } }""");

        var patterns = LevelPattern.ReadFrom(config);

        Assert.Equal(LevelPattern.Defaults.Count, patterns.Count);
        Assert.Equal("Fatal", patterns[0].Level);
    }

    [Fact]
    public void ReadFrom_ParsesMultiplePatterns()
    {
        var configDir = Path.Combine(Path.GetTempPath(), "StdLogger.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDir);
        var configFile = Path.Combine(configDir, "test.json");
        File.WriteAllText(configFile, "{\"StdLogger\":{\"LevelPatterns\":[" +
            "{\"Level\":\"Error\",\"Pattern\":\"\\\\[ERROR\\\\]\"}," +
            "{\"Level\":\"Fatal\",\"Pattern\":\"FATAL:\"}," +
            "{\"Level\":\"Warning\",\"Pattern\":\"\\\\[WARNING\\\\]\"}" +
            "]}}");

        var config = new ConfigurationBuilder().AddJsonFile(configFile).Build();
        var patterns = LevelPattern.ReadFrom(config);

        Assert.Equal(3, patterns.Count);
        Assert.Equal("Error", patterns[0].Level);
        Assert.True(patterns[0].Match("[ERROR] boom").Success);
        Assert.Equal("Fatal", patterns[1].Level);
        Assert.True(patterns[1].Match("FATAL: something").Success);
        Assert.Equal("Warning", patterns[2].Level);
        Assert.True(patterns[2].Match("[WARNING] careful").Success);

        File.Delete(configFile);
        Directory.Delete(configDir, true);
    }

    [Fact]
    public void ReadFrom_SkipsEntries_WithoutLevelOrPattern()
    {
        var config = BuildConfig("""
        {
          "StdLogger": {
            "LevelPatterns": [
              { "Level": "Error", "Pattern": "\\[ERROR\\]" },
              { "Pattern": "\\[INFO\\]" },
              { "Level": "Warning" }
            ]
          }
        }
        """);

        var patterns = LevelPattern.ReadFrom(config);

        Assert.Single(patterns);
        Assert.Equal("Error", patterns[0].Level);
    }

    [Fact]
    public void LineLogger_UsesConfiguredPatterns()
    {
        var config = BuildConfig("""
        {
          "StdLogger": {
            "LevelPatterns": [
              { "Level": "Error", "Pattern": "\\[ERROR\\]" }
            ]
          }
        }
        """);

        var sink = new CollectingSink();
        var serilog = new Serilog.LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var logger = new LineLogger(serilog, LevelPattern.ReadFrom(config));

        Assert.Equal(Serilog.Events.LogEventLevel.Error, logger.ParseLine("[ERROR] boom", isError: false));
        Assert.Equal(Serilog.Events.LogEventLevel.Information, logger.ParseLine("[WARNING] careful", isError: false));
    }
}