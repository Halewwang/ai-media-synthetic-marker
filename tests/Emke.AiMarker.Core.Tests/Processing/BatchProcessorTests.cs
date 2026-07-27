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
    public async Task Batch_does_not_pass_safe_stop_or_external_cancellation_into_started_file()
    {
        var stop = new StopController();
        var processor = new SequencedProcessor(onFirstCompleted: stop.RequestStop);
        var batch = new BatchProcessor(processor, new InMemoryLogWriter());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await batch.RunAsync(
            BatchPlans.Many("a.jpg", "b.jpg"),
            RunMode.MarkCopies,
            @"D:\logs",
            stop,
            progress: null,
            cancelled.Token);

        Assert.Single(processor.ReceivedTokens);
        Assert.False(processor.ReceivedTokens[0].CanBeCanceled);
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
