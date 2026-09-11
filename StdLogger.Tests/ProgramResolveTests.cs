using Microsoft.Extensions.Configuration;

namespace StdLogger.Tests;

public class ProgramResolveTests
{
    private static IConfiguration BuildConfig(string json)
    {
        return new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();
    }

    [Fact]
    public void ResolveFileSinkPaths_RelativePath_ResolvesAgainstBaseDir()
    {
        var config = BuildConfig("""
        {
          "Serilog": {
            "WriteTo": [
              { "Name": "File", "Args": { "path": "logs/app-.log", "rollingInterval": "Day" } }
            ]
          }
        }
        """);

        var overrides = Program.ResolveFileSinkPaths(config, baseDir: @"C:\configs", logDir: null);

        var overrideEntry = Assert.Single(overrides);
        Assert.Equal("Serilog:WriteTo:0:Args:path", overrideEntry.Key);
        Assert.Equal(@"C:\configs\logs\app-.log", overrideEntry.Value);
    }

    [Fact]
    public void ResolveFileSinkPaths_AbsolutePath_StaysAbsolute()
    {
        var config = BuildConfig("""
        {
          "Serilog": {
            "WriteTo": [
              { "Name": "File", "Args": { "path": "D:\\logs\\fixed.log" } }
            ]
          }
        }
        """);

        var overrides = Program.ResolveFileSinkPaths(config, baseDir: @"C:\configs", logDir: null);

        var overrideEntry = Assert.Single(overrides);
        Assert.Equal(@"D:\logs\fixed.log", overrideEntry.Value);
    }

    [Fact]
    public void OverrideFileSinkPath_RelativeLogDir_ResolvesAgainstBaseDir()
    {
        var config = BuildConfig("""
        {
          "Serilog": {
            "WriteTo": [
              { "Name": "File", "Args": { "path": "log-.log", "rollingInterval": "Day" } }
            ]
          }
        }
        """);

        var overrides = Program.OverrideFileSinkPath(config, logDir: "my-logs", baseDir: @"C:\configs");

        var overrideEntry = Assert.Single(overrides);
        Assert.Equal(@"C:\configs\my-logs\log-.log", overrideEntry.Value);
    }

    [Fact]
    public void OverrideFileSinkPath_AbsoluteLogDir_CombinesWithFileName()
    {
        var config = BuildConfig("""
        {
          "Serilog": {
            "WriteTo": [
              { "Name": "File", "Args": { "path": "D:\\other\\app-.log" } }
            ]
          }
        }
        """);

        var overrides = Program.OverrideFileSinkPath(config, logDir: @"D:\logs", baseDir: @"C:\configs");

        var overrideEntry = Assert.Single(overrides);
        Assert.Equal(@"D:\logs\app-.log", overrideEntry.Value);
    }
}
