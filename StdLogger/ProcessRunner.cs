using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace StdLogger;

internal sealed class ProcessRunner
{
    private readonly LineLogger _logger;
    private readonly string? _sinksInfo;

    public ProcessRunner(LineLogger logger, string? sinksInfo = null)
    {
        _logger = logger;
        _sinksInfo = sinksInfo;
    }

    public async Task<int> RunAsync(string[] cmd)
    {
        _logger.Header(string.Join(" ", cmd), _sinksInfo);

        var psi = new ProcessStartInfo
        {
            FileName = cmd[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        for (int i = 1; i < cmd.Length; i++)
            psi.ArgumentList.Add(cmd[i]);

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            _logger.StartError(cmd[0], ex.Message);
            return 1;
        }

        var channel = Channel.CreateUnbounded<PendingLine>();
        var consumerTask = ConsumeAsync(channel.Reader);
        var stdoutTask = ReadStreamAsync(process.StandardOutput, channel.Writer, isError: false);
        var stderrTask = ReadStreamAsync(process.StandardError, channel.Writer, isError: true);

        await Task.WhenAll(stdoutTask, stderrTask);
        channel.Writer.TryComplete();
        await consumerTask;
        await process.WaitForExitAsync();

        return process.ExitCode;
    }

    private static async Task ReadStreamAsync(StreamReader reader, ChannelWriter<PendingLine> writer, bool isError)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line == null)
                break;

            await writer.WriteAsync(new PendingLine(line, isError));
        }
    }

    private async Task ConsumeAsync(ChannelReader<PendingLine> reader)
    {
        await foreach (var pending in reader.ReadAllAsync())
        {
            _logger.Write(pending.Line, pending.IsError);
        }
    }

    private readonly record struct PendingLine(string Line, bool IsError);
}