using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Core.Processing;
using Emke.AiMarker.Core.Tests.TestSupport;

namespace Emke.AiMarker.Core.Tests.Processing;

public sealed class BatchProcessorTests
{
    [Fact]
    public async Task Stop_finishes_current_file_and_marks_remaining_files_unprocessed()
    {
        var stop = new StopController();
        var processor = new SequencedProcessor(onFirstCompleted: stop.RequestStop);
        var log = new InMemoryLogWriter();
        var batch = new BatchProcessor(processor, log);

        RunSummary summary = await batch.RunAsync(
            BatchPlans.Many("a.jpg", "b.jpg", "c.mp4"),
            RunMode.MarkCopies,
            logDirectory: @"D:\logs",
            stop: stop,
            progress: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(["a.jpg"], processor.StartedPaths);
        Assert.Equal(2, summary.Results.Count(
            result => result.Status == ProcessStatus.StoppedBeforeProcessing));
        Assert.All(
            summary.Results.Where(result => result.Status == ProcessStatus.StoppedBeforeProcessing),
            result =>
            {
                Assert.Equal(VerificationResult.NotRun, result.Evidence.Result);
                Assert.Equal("用户停止前未处理", result.Error);
            });
        Assert.True(summary.Stopped);
        Assert.True(summary.LogWritten);
        Assert.Equal(summary.Results, log.Results);
    }

    [Fact]
    public async Task Per_file_exception_becomes_failed_result_and_later_files_continue()
    {
        var processor = new SequencedProcessor(throwingPath: "b.jpg");
        var batch = new BatchProcessor(processor, new InMemoryLogWriter());

        RunSummary summary = await batch.RunAsync(
            BatchPlans.Many("a.jpg", "b.jpg", "c.mp4"),
            RunMode.MarkCopies,
            @"D:\logs",
            new StopController(),
            progress: null,
            CancellationToken.None);

        Assert.Equal(["a.jpg", "b.jpg", "c.mp4"], processor.StartedPaths);
        ProcessResult failed = Assert.Single(
            summary.Results,
            result => result.Status == ProcessStatus.Failed);
        Assert.Equal("b.jpg", failed.RelativePath);
        Assert.Equal("simulated processing failure", failed.Error);
        Assert.Equal(VerificationResult.Failed, failed.Evidence.Result);
        Assert.Equal("c.mp4", summary.Results[^1].RelativePath);
    }

    [Fact]
    public async Task Progress_snapshots_keep_completed_count_and_status_counts_consistent()
    {
        var progress = new CapturingProgress();
        var processor = new SequencedProcessor(throwingPath: "b.jpg");
        var batch = new BatchProcessor(processor, new InMemoryLogWriter());

        RunSummary summary = await batch.RunAsync(
            BatchPlans.Many("a.jpg", "b.jpg", "c.mp4"),
            RunMode.MarkCopies,
            @"D:\logs",
            new StopController(),
            progress,
            CancellationToken.None);

        Assert.Equal(3, progress.Values.Count);
        RunProgress last = progress.Values[^1];
        Assert.Equal(3, last.Completed);
        Assert.Equal(3, last.Total);
        Assert.Equal("c.mp4", last.CurrentRelativePath);
        Assert.Equal(summary.Results.Count, last.Counts.Values.Sum());
        Assert.Equal(2, last.Counts[ProcessStatus.Added]);
        Assert.Equal(1, last.Counts[ProcessStatus.Failed]);
    }

    [Fact]
    public async Task Stop_request_winning_admission_starts_no_processor_and_logs_stopped_results()
    {
        using var releaseAdmission = new ManualResetEventSlim();
        var admissionEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = new StopController(
            beforeAdmissionAttempt: () =>
            {
                admissionEntered.TrySetResult();
                releaseAdmission.Wait();
            });
        var processor = new SequencedProcessor();
        var log = new InMemoryLogWriter();
        var batch = new BatchProcessor(processor, log);

        Task<RunSummary> running = Task.Run(() => batch.RunAsync(
            BatchPlans.Many("a.jpg", "b.jpg"),
            RunMode.MarkCopies,
            @"D:\logs",
            stop,
            progress: null,
            CancellationToken.None));
        await admissionEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await Task.Run(stop.RequestStop, TestContext.Current.CancellationToken);
        releaseAdmission.Set();

        RunSummary summary = await running;

        Assert.Empty(processor.StartedPaths);
        Assert.Equal(2, summary.Results.Count(
            result => result.Status == ProcessStatus.StoppedBeforeProcessing));
        Assert.True(summary.LogWritten);
        Assert.Equal(2, log.Results.Count);
    }

    [Fact]
    public async Task Admission_winning_request_finishes_exactly_current_file_and_blocks_next()
    {
        var stop = new StopController();
        var processor = new BlockingProcessor();
        var batch = new BatchProcessor(processor, new InMemoryLogWriter());

        Task<RunSummary> running = batch.RunAsync(
            BatchPlans.Many("a.jpg", "b.jpg"),
            RunMode.MarkCopies,
            @"D:\logs",
            stop,
            progress: null,
            CancellationToken.None);

        await processor.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await Task.Run(stop.RequestStop, TestContext.Current.CancellationToken);
        processor.Complete("a.jpg", RunMode.MarkCopies);

        RunSummary summary = await running;

        Assert.Equal(["a.jpg"], processor.StartedPaths);
        Assert.Single(processor.ReceivedTokens);
        Assert.False(processor.ReceivedTokens[0].CanBeCanceled);
        Assert.Equal(ProcessStatus.Added, summary.Results[0].Status);
        Assert.Equal(ProcessStatus.StoppedBeforeProcessing, summary.Results[1].Status);
    }

    [Fact]
    public async Task Caller_cancellation_before_first_admission_marks_every_plan_stopped_and_still_logs()
    {
        var processor = new SequencedProcessor();
        var log = new InMemoryLogWriter();
        var batch = new BatchProcessor(processor, log);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        RunSummary summary = await batch.RunAsync(
            BatchPlans.Many("a.jpg", "b.jpg"),
            RunMode.MarkCopies,
            @"D:\logs",
            new StopController(),
            progress: null,
            cancelled.Token);

        Assert.Empty(processor.StartedPaths);
        Assert.Equal(2, summary.Results.Count(
            result => result.Status == ProcessStatus.StoppedBeforeProcessing));
        Assert.True(summary.LogWritten);
        Assert.Equal(2, log.Results.Count);
    }

    [Fact]
    public async Task Caller_cancellation_between_admissions_finishes_current_file_and_stops_remaining()
    {
        using var cancelled = new CancellationTokenSource();
        var processor = new SequencedProcessor(onFirstCompleted: cancelled.Cancel);
        var batch = new BatchProcessor(processor, new InMemoryLogWriter());

        RunSummary summary = await batch.RunAsync(
            BatchPlans.Many("a.jpg", "b.jpg"),
            RunMode.MarkCopies,
            @"D:\logs",
            new StopController(),
            progress: null,
            cancelled.Token);

        Assert.Equal(["a.jpg"], processor.StartedPaths);
        Assert.Single(processor.ReceivedTokens);
        Assert.False(processor.ReceivedTokens[0].CanBeCanceled);
        Assert.Equal(ProcessStatus.Added, summary.Results[0].Status);
        Assert.Equal(ProcessStatus.StoppedBeforeProcessing, summary.Results[1].Status);
        Assert.True(summary.LogWritten);
    }

    [Fact]
    public async Task Log_is_attempted_in_finally_after_stop_and_log_failure_is_reported_in_summary()
    {
        var stop = new StopController();
        var processor = new SequencedProcessor(onFirstCompleted: stop.RequestStop);
        var log = new InMemoryLogWriter(throwOnWrite: true);
        var batch = new BatchProcessor(processor, log);

        RunSummary summary = await batch.RunAsync(
            BatchPlans.Many("a.jpg", "b.jpg"),
            RunMode.MarkCopies,
            @"D:\logs",
            stop,
            progress: null,
            CancellationToken.None);

        Assert.Equal(1, log.WriteAttempts);
        Assert.Equal(2, log.Results.Count);
        Assert.False(summary.LogWritten);
        Assert.Equal("", summary.LogPath);
    }
}
