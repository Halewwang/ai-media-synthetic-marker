using System.Diagnostics;
using System.Text;

namespace Emke.AiMarker.ProcessRunner.TestHelper;

internal static class Program
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(1);

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        await File.WriteAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "started.marker"),
            Environment.ProcessId.ToString());

        if (args.Length == 2
            && string.Equals(args[0], "--hold-pipes", StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(args[1], Environment.ProcessId.ToString());
            await Task.Delay(HoldDuration);
            return 0;
        }

        if (args.Length == 2
            && string.Equals(
                args[0],
                "--spawn-holder-and-exit",
                StringComparison.Ordinal))
        {
            StartChild("--hold-pipes", args[1]);
            await WaitForFileAsync(args[1]);
            return 0;
        }

        string[] expectedLauncherArguments =
            ["-charset", "filename=UTF8", "-@", "-"];
        if (!args.SequenceEqual(expectedLauncherArguments, StringComparer.Ordinal))
        {
            return 64;
        }

        string? mode = await Console.In.ReadLineAsync();
        if (string.Equals(mode, "emit", StringComparison.Ordinal))
        {
            await Console.Out.WriteAsync("stdout 中文\n");
            await Console.Error.WriteAsync("stderr detail\n");
            return 7;
        }

        if (string.Equals(mode, "hang", StringComparison.Ordinal))
        {
            await Task.Delay(HoldDuration);
            return 0;
        }

        const string orphanTimeoutPrefix = "orphan-timeout:";
        if (mode?.StartsWith(orphanTimeoutPrefix, StringComparison.Ordinal) == true)
        {
            string pidFile = mode[orphanTimeoutPrefix.Length..];
            StartChild("--spawn-holder-and-exit", pidFile);
            await WaitForFileAsync(pidFile);
            await Task.Delay(HoldDuration);
            return 0;
        }

        const string orphanOutputPrefix = "orphan-output:";
        if (mode?.StartsWith(orphanOutputPrefix, StringComparison.Ordinal) == true)
        {
            string pidFile = mode[orphanOutputPrefix.Length..];
            StartChild("--spawn-holder-and-exit", pidFile);
            await WaitForFileAsync(pidFile);
            return 0;
        }

        return 65;
    }

    private static void StartChild(string mode, string pidFile)
    {
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Test helper executable is unavailable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(mode);
        startInfo.ArgumentList.Add(pidFile);

        Process.Start(startInfo)?.Dispose();
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!File.Exists(path))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}
