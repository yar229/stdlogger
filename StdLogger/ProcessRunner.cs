using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace StdLogger;

internal sealed class ProcessRunner
{
    private readonly LogWriter _writer;
    private readonly TextWriter _stdoutMirror;

    public ProcessRunner(LogWriter writer, TextWriter? stdoutMirror = null)
    {
        _writer = writer;
        _stdoutMirror = stdoutMirror ?? Console.Out;
    }

    public async Task<int> RunAsync(string[] cmd)
    {
        _writer.Write("INFO", $"StdLogger  for '{string.Join(" ", cmd)}'");

        var psi = new ProcessStartInfo
        {
            FileName = cmd[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        for (int i = 1; i < cmd.Length; i++)
            psi.ArgumentList.Add(cmd[i]);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            _writer.Write("ERROR", $"Failed to start '{cmd[0]}': {ex.Message}");
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

            (string level, string message) = _writer.ParseLine(line, isError);

            if (!isError)
                _stdoutMirror.WriteLine(line);

            _writer.Write(level, message);
        }
    }
}
