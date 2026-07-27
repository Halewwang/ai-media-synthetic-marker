using System.Diagnostics;
using System.Text;
using Emke.AiMarker.Infrastructure.ExifTool;

namespace Emke.AiMarker.Infrastructure.Tests.ExifTool;

[Collection("RealProcessRunner")]
public sealed class ProcessRunnerTests
{
    private static readonly TimeSpan TestCompletionLimit = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData("emit\rhang")]
    [InlineData("emit\nhang")]
    public async Task Execute_rejects_argument_file_line_breaks_before_start(
        string unsafeLine)
    {
        string startedMarker = GetHelperFile("started.marker");
        File.Delete(startedMarker);
        var runner = new ProcessRunner();

        MarkerOperationException exception =
            await Assert.ThrowsAsync<MarkerOperationException>(
                () => runner.ExecuteAsync(
                    GetHelperExecutable(),
                    [unsafeLine],
                    TimeSpan.FromSeconds(5),
                    CancellationToken.None));

        Assert.Contains("换行", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(startedMarker));
    }

    [Fact]
    public async Task Execute_captures_completed_stdout_stderr_and_exit_code()
    {
        var runner = new ProcessRunner();

        ProcessExecutionResult result = await runner.ExecuteAsync(
            GetHelperExecutable(),
            ["emit"],
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
        Assert.Equal("stdout 中文\n", Encoding.UTF8.GetString(result.Stdout));
        Assert.Equal("stderr detail\n", Encoding.UTF8.GetString(result.Stderr));
    }

    [Fact]
    public async Task Execute_preserves_caller_cancellation_after_cleanup()
    {
        var runner = new ProcessRunner();
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(150));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.ExecuteAsync(
                GetHelperExecutable(),
                ["hang"],
                TimeSpan.FromSeconds(10),
                cancellation.Token));

        Assert.True(stopwatch.Elapsed < TestCompletionLimit);
    }

    [Fact]
    public async Task Execute_bounds_timeout_cleanup_when_orphan_holds_pipes()
    {
        string pidFile = GetTemporaryPidFile();
        var runner = new ProcessRunner();
        Task<ProcessExecutionResult> execution = runner.ExecuteAsync(
            GetHelperExecutable(),
            [$"orphan-timeout:{pidFile}"],
            TimeSpan.FromMilliseconds(200),
            CancellationToken.None);

        try
        {
            Task completed = await Task.WhenAny(
                execution,
                Task.Delay(
                    TestCompletionLimit,
                    TestContext.Current.CancellationToken));
            Assert.Same(execution, completed);

            MarkerOperationException exception =
                await Assert.ThrowsAsync<MarkerOperationException>(
                    async () => await execution);
            Assert.Contains("超时", exception.Message, StringComparison.Ordinal);
            Assert.Contains("清理", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            KillRecordedProcess(pidFile);
            await ObserveAfterCleanupAsync(execution);
        }
    }

    [Fact]
    public async Task Execute_bounds_output_completion_after_parent_exit()
    {
        string pidFile = GetTemporaryPidFile();
        var runner = new ProcessRunner();
        Task<ProcessExecutionResult> execution = runner.ExecuteAsync(
            GetHelperExecutable(),
            [$"orphan-output:{pidFile}"],
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        try
        {
            Task completed = await Task.WhenAny(
                execution,
                Task.Delay(
                    TestCompletionLimit,
                    TestContext.Current.CancellationToken));
            Assert.Same(execution, completed);

            MarkerOperationException exception =
                await Assert.ThrowsAsync<MarkerOperationException>(
                    async () => await execution);
            Assert.Contains("输出管道", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            KillRecordedProcess(pidFile);
            await ObserveAfterCleanupAsync(execution);
        }
    }

    private static string GetHelperExecutable()
    {
        string executableName = OperatingSystem.IsWindows()
            ? "Emke.AiMarker.ProcessRunner.TestHelper.exe"
            : "Emke.AiMarker.ProcessRunner.TestHelper";
        return GetHelperFile(executableName);
    }

    private static string GetHelperFile(string name) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "ProcessRunnerTestHelper",
            name);

    private static string GetTemporaryPidFile() =>
        Path.Combine(
            Path.GetTempPath(),
            $"emke-process-runner-{Guid.NewGuid():N}.pid");

    private static void KillRecordedProcess(string pidFile)
    {
        try
        {
            if (!File.Exists(pidFile)
                || !int.TryParse(File.ReadAllText(pidFile), out int processId))
            {
                return;
            }

            using Process process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(milliseconds: 2_000);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
        }
        finally
        {
            File.Delete(pidFile);
        }
    }

    private static async Task ObserveAfterCleanupAsync(
        Task<ProcessExecutionResult> execution)
    {
        try
        {
            await execution.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception exception) when (
            exception is MarkerOperationException
                or OperationCanceledException
                or TimeoutException)
        {
        }
    }
}

[CollectionDefinition("RealProcessRunner", DisableParallelization = true)]
public sealed class RealProcessRunnerCollection;
