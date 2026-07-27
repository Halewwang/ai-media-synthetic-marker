using System.Diagnostics;
using System.Text;

namespace Emke.AiMarker.Infrastructure.ExifTool;

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public async Task<ProcessExecutionResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> argumentFileLines,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(argumentFileLines);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-charset");
        startInfo.ArgumentList.Add("filename=UTF8");
        startInfo.ArgumentList.Add("-@");
        startInfo.ArgumentList.Add("-");
        startInfo.StandardInputEncoding = Utf8WithoutBom;

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new MarkerOperationException("无法启动 ExifTool。");
            }
        }
        catch (MarkerOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new MarkerOperationException(
                $"无法启动 ExifTool：{exception.Message}");
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        var stdout = new MemoryStream();
        var stderr = new MemoryStream();
        Task stdoutCopy = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        Task stderrCopy = process.StandardError.BaseStream.CopyToAsync(stderr);
        process.StandardInput.NewLine = "\n";

        try
        {
            foreach (string line in argumentFileLines)
            {
                await process.StandardInput.WriteLineAsync(line.AsMemory(), linkedSource.Token);
            }

            await process.StandardInput.FlushAsync(linkedSource.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await AwaitOutputAsync(stdoutCopy, stderrCopy);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new MarkerOperationException(
                "ExifTool 操作超时。文件可能过大、损坏或正在被其他程序占用。");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            TryKill(process);
            await AwaitOutputAsync(stdoutCopy, stderrCopy);
            throw new MarkerOperationException(
                $"与 ExifTool 通信失败：{exception.Message}");
        }

        await Task.WhenAll(stdoutCopy, stderrCopy);
        return new ProcessExecutionResult(
            process.ExitCode,
            stdout.ToArray(),
            stderr.ToArray());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
        }
    }

    private static async Task AwaitOutputAsync(Task stdoutCopy, Task stderrCopy)
    {
        try
        {
            await Task.WhenAll(stdoutCopy, stderrCopy);
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException)
        {
        }
    }
}
