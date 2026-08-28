using System;
using System.IO;
using System.Threading.Tasks;

namespace StdLogger;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: StdLogger <logdir> [command args...]");
            Console.Error.WriteLine("  Reads stdin and logs to <logdir>.");
            Console.Error.WriteLine("  If a command is given, runs it and logs its stdout and stderr.");
            return 1;
        }

        string logDir = args[0];
        Directory.CreateDirectory(logDir);

        string[] rest = args[1..];
        var writer = new LogWriter(logDir);

        if (rest.Length == 0)
        {
            await ReadStdin(Console.In, writer);
            return 0;
        }

        var runner = new ProcessRunner(writer);
        return await runner.RunAsync(rest);
    }

    private static async Task ReadStdin(TextReader reader, LogWriter writer)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line == null)
                break;

            (string level, string _) = writer.ParseLine(line, isError: false);
            writer.Write(level, line);
        }
    }
}
