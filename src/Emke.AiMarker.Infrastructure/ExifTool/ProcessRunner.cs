using System.Diagnostics;
using System.Text;

namespace Emke.AiMarker.Infrastructure.ExifTool;

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(2);

    public async Task<ProcessExecutionResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> argumentFileLines,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(argumentFileLines);
        ValidateArgumentFileLines(argumentFileLines);
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

        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();
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
            bool callerCancelled = cancellationToken.IsCancellationRequested;
            string? cleanupFailure = await TerminateAndDrainAsync(
                process,
                stdoutCopy,
                stderrCopy);
            if (cleanupFailure is not null)
            {
                string operation = callerCancelled ? "取消" : "超时";
                throw new MarkerOperationException(
                    $"ExifTool 操作{operation}后终止/清理失败：{cleanupFailure}");
            }

            if (callerCancelled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new MarkerOperationException(
                "ExifTool 操作超时。文件可能过大、损坏或正在被其他程序占用。");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            string? cleanupFailure = await TerminateAndDrainAsync(
                process,
                stdoutCopy,
                stderrCopy);
            if (cleanupFailure is not null)
            {
                throw new MarkerOperationException(
                    "与 ExifTool 通信失败，且终止/清理失败："
                    + cleanupFailure);
            }

            throw new MarkerOperationException(
                $"与 ExifTool 通信失败：{exception.Message}");
        }

        await CompleteOutputCaptureAsync(stdoutCopy, stderrCopy);
        return new ProcessExecutionResult(
            process.ExitCode,
            stdout.ToArray(),
            stderr.ToArray());
    }

    private static void ValidateArgumentFileLines(
        IReadOnlyList<string> argumentFileLines)
    {
        for (int index = 0; index < argumentFileLines.Count; index++)
        {
            string? line = argumentFileLines[index];
            if (line is null)
            {
                throw new MarkerOperationException(
                    $"ExifTool 参数文件第 {index + 1} 行不能为空。");
            }

            if (line.Contains('\r', StringComparison.Ordinal)
                || line.Contains('\n', StringComparison.Ordinal))
            {
                throw new MarkerOperationException(
                    $"ExifTool 参数文件第 {index + 1} 行包含不允许的换行符；"
                    + "已拒绝启动以防止参数注入。");
            }
        }
    }

    private static async Task<string?> TerminateAndDrainAsync(
        Process process,
        Task stdoutCopy,
        Task stderrCopy)
    {
        string? killFailure = null;
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
            killFailure = exception.Message;
        }

        Task? cleanup = null;
        try
        {
            cleanup = Task.WhenAll(
                process.WaitForExitAsync(),
                stdoutCopy,
                stderrCopy);
            await cleanup.WaitAsync(CleanupTimeout);
            return null;
        }
        catch (TimeoutException)
        {
            ObserveLater(cleanup ?? Task.WhenAll(stdoutCopy, stderrCopy));
            string killDetail = killFailure is null
                ? string.Empty
                : $"；终止调用错误：{killFailure}";
            return $"未能在 {CleanupTimeout.TotalSeconds:0} 秒内关闭进程和输出管道"
                + killDetail;
        }
        catch (Exception exception)
        {
            string killDetail = killFailure is null
                ? string.Empty
                : $"；终止调用错误：{killFailure}";
            return $"清理进程或输出管道时出错："
                + exception.GetBaseException().Message
                + killDetail;
        }
    }

    private static async Task CompleteOutputCaptureAsync(
        Task stdoutCopy,
        Task stderrCopy)
    {
        Task capture = Task.WhenAll(stdoutCopy, stderrCopy);
        try
        {
            await capture.WaitAsync(CleanupTimeout);
        }
        catch (TimeoutException)
        {
            ObserveLater(capture);
            throw new MarkerOperationException(
                $"ExifTool 已退出，但输出管道未在 "
                + $"{CleanupTimeout.TotalSeconds:0} 秒内关闭。"
                + "请检查是否有子进程继承了输出句柄。");
        }
        catch (Exception exception)
        {
            throw new MarkerOperationException(
                $"读取 ExifTool 输出失败：{exception.GetBaseException().Message}");
        }
    }

    private static void ObserveLater(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted
                | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
