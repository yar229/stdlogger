using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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

        var stdoutTask = ReadStreamAsync(process.StandardOutput, isError: false);
        var stderrTask = ReadStreamAsync(process.StandardError, isError: true);

        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private async Task ReadStreamAsync(TextReader reader, bool isError)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line == null)
                break;

            _logger.Write(line, isError);
        }
    }
}