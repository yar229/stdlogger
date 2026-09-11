using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace StdLogger.Tests;

public class LineLoggerTests
{
    private static (LineLogger logger, CollectingSink sink) CreateLogger()
    {
        var sink = new CollectingSink();
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();
        return (new LineLogger(serilog), sink);
    }

    [Theory]
    [InlineData("[ERROR] boom", true, LogEventLevel.Error)]
    [InlineData("[error] boom", true, LogEventLevel.Error)]
    [InlineData("[WARN] careful", false, LogEventLevel.Warning)]
    [InlineData("[WARNING] careful", false, LogEventLevel.Warning)]
    [InlineData("[INFO] hi", false, LogEventLevel.Information)]
    [InlineData("[DEBUG] x", false, LogEventLevel.Debug)]
    [InlineData("[TRACE] x", true, LogEventLevel.Verbose)]
    [InlineData("[FATAL] x", false, LogEventLevel.Fatal)]
    [InlineData("[CRITICAL] x", false, LogEventLevel.Fatal)]
    [InlineData("Warning: 'dnssync.cs' appears to be a file-based app but was passed as an argument to the project", true, LogEventLevel.Warning)]
    [InlineData("c:\\Scripts\\simpledns2router\\dnssync.csproj : warning NU1903: Package 'SSH.NET' 2025.0.0 has a", true, LogEventLevel.Warning)]
    [InlineData("c:\\Scripts\\simpledns2router\\dnssync.csproj : warning NU1903: Package 'SSH.NET' 2025.0.0 has a", false, LogEventLevel.Warning)]
    public void ParseLine_DetectsKnownLevel_CaseInsensitive(string line, bool isError, LogEventLevel expected)
    {
        (var logger, _) = CreateLogger();

        var level = logger.ParseLine(line, isError);

        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData(false, LogEventLevel.Information)]
    [InlineData(true, LogEventLevel.Error)]
    public void ParseLine_DefaultsLevel_WhenNoTagFound(bool isError, LogEventLevel expected)
    {
        (var logger, _) = CreateLogger();

        var level = logger.ParseLine("some random output", isError);

        Assert.Equal(expected, level);
    }

    [Fact]
    public void ParseLine_UnknownTagOutsidePattern_NotTreatedAsLevel()
    {
        (var logger, _) = CreateLogger();

        var level = logger.ParseLine("just [INFO-ish] text", isError: false);

        Assert.Equal(LogEventLevel.Information, level);
    }

    [Fact]
    public void ParseLine_PriorityFirstMatch_WinsOverEarlierPosition()
    {
        (var logger, _) = CreateLogger();

        var level = logger.ParseLine("[WARNING] first, but [ERROR] wins", isError: false);

        Assert.Equal(LogEventLevel.Error, level);
    }

    [Fact]
    public void ParseLine_HighestLevelInLine_Wins()
    {
        (var logger, _) = CreateLogger();

        var level = logger.ParseLine("[WARNING] something [ERROR] and [FATAL] here", isError: false);

        Assert.Equal(LogEventLevel.Fatal, level);
    }

    [Theory]
    [InlineData("TRACE", LogEventLevel.Verbose)]
    [InlineData("DEBUG", LogEventLevel.Debug)]
    [InlineData("INFO", LogEventLevel.Information)]
    [InlineData("WARN", LogEventLevel.Warning)]
    [InlineData("WARNING", LogEventLevel.Warning)]
    [InlineData("ERROR", LogEventLevel.Error)]
    [InlineData("FATAL", LogEventLevel.Fatal)]
    [InlineData("CRITICAL", LogEventLevel.Fatal)]
    [InlineData("bogus", LogEventLevel.Information)]
    public void MapLevel_MapsKnownAndUnknownNames(string name, LogEventLevel expected)
    {
        Assert.Equal(expected, LineLogger.MapLevel(name));
    }

    [Fact]
    public void Write_EmitssingleEvent_WithDetectedLevelAndMessage()
    {
        (var logger, var sink) = CreateLogger();

        logger.Write("[WARNING] careful now", isError: false);

        var evt = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Warning, evt.Level);
        Assert.Contains("careful now", evt.RenderMessage());
    }

    [Fact]
    public void Header_EmitssingleInformationEvent_WithCommand()
    {
        (var logger, var sink) = CreateLogger();

        logger.Header("dotnet build");

        var evt = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Information, evt.Level);
        Assert.Contains("dotnet build", evt.RenderMessage());
    }

    [Fact]
    public void Header_WithSinksInfo_IncludesIt()
    {
        var sink = new CollectingSink();
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();
        var logger = new LineLogger(serilog);

        logger.Header("dotnet build", "Console, File -> D:\\logs\\app-20260911.log (rolling: Day); min level: Verbose");

        var evt = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Information, evt.Level);
        Assert.Contains("app-20260911.log", evt.RenderMessage());
        Assert.Contains("rolling: Day", evt.RenderMessage());
        Assert.Contains("min level", evt.RenderMessage());
    }

    [Fact]
    public void StartError_EmitssingleErrorEvent_WithCommandAndError()
    {
        (var logger, var sink) = CreateLogger();

        logger.StartError("app.exe", "The system cannot find the file specified.");

        var evt = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Error, evt.Level);
        Assert.Contains("app.exe", evt.RenderMessage());
        Assert.Contains("cannot find", evt.RenderMessage());
    }
}

internal sealed class CollectingSink : ILogEventSink
{
    public List<LogEvent> Events { get; } = new();

    public void Emit(LogEvent logEvent) => Events.Add(logEvent);
}