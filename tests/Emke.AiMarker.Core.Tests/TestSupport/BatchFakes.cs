using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Core.Tests.TestSupport;

internal sealed class SequencedProcessor(
    Action? onFirstCompleted = null,
    string? throwingPath = null) : IFileProcessor
{
    public List<string> StartedPaths { get; } = [];

    public List<CancellationToken> ReceivedTokens { get; } = [];

    public Task<ProcessResult> ProcessAsync(
        OutputPlanItem plan,
        RunMode mode,
        CancellationToken cancellationToken)
    {
        StartedPaths.Add(plan.RelativePath);
        ReceivedTokens.Add(cancellationToken);

        if (plan.RelativePath == throwingPath)
        {
            throw new IOException("simulated processing failure");
        }

        if (StartedPaths.Count == 1)
        {
            onFirstCompleted?.Invoke();
        }

        return Task.FromResult(TestResults.Added(plan.RelativePath, mode));
    }
}

internal sealed class InMemoryLogWriter(bool throwOnWrite = false) : IRunLogWriter
{
    public int WriteAttempts { get; private set; }

    public IReadOnlyList<ProcessResult> Results { get; private set; } = [];

    public Task<string> WriteAsync(
        string logDirectory,
        RunMode mode,
        IReadOnlyList<ProcessResult> results,
        CancellationToken cancellationToken)
    {
        WriteAttempts++;
        Results = results.ToArray();
        if (throwOnWrite)
        {
            throw new IOException("simulated log failure");
        }

        return Task.FromResult(Path.Combine(logDirectory, "run.csv"));
    }
}

internal sealed class BlockingProcessor : IFileProcessor
{
    private readonly TaskCompletionSource<ProcessResult> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<string> Started { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public List<string> StartedPaths { get; } = [];

    public List<CancellationToken> ReceivedTokens { get; } = [];

    public Task<ProcessResult> ProcessAsync(
        OutputPlanItem plan,
        RunMode mode,
        CancellationToken cancellationToken)
    {
        StartedPaths.Add(plan.RelativePath);
        ReceivedTokens.Add(cancellationToken);
        Started.TrySetResult(plan.RelativePath);
        return _completion.Task;
    }

    public void Complete(string relativePath, RunMode mode) =>
        _completion.TrySetResult(TestResults.Added(relativePath, mode));
}

internal sealed class CapturingProgress : IProgress<RunProgress>
{
    public List<RunProgress> Values { get; } = [];

    public void Report(RunProgress value) => Values.Add(value);
}

internal static class BatchPlans
{
    public static IReadOnlyList<OutputPlanItem> Many(params string[] relativePaths) =>
        relativePaths.Select(Plan).ToArray();

    public static OutputPlanItem Plan(string relativePath) =>
        new(
            $@"D:\input\{relativePath}",
            relativePath,
            $@"D:\output\{relativePath}",
            $@"D:\output\.{relativePath}.tmp",
            1);
}

internal static class TestResults
{
    public static ProcessResult Added(string relativePath, RunMode mode = RunMode.MarkCopies) =>
        new(
            relativePath,
            Path.GetExtension(relativePath).TrimStart('.').ToUpperInvariant(),
            ProcessStatus.Added,
            mode,
            new VerificationEvidence(
                VerificationResult.Passed,
                "[\"contains-synthetic-performer\"]",
                "已确认 rdf:Bag/rdf:li",
                DateTimeOffset.Parse("2026-07-27T08:00:00+00:00"),
                "13.59"));

    public static ProcessResult Failed(
        string relativePath,
        string error,
        string evidenceError = "") =>
        new(
            relativePath,
            Path.GetExtension(relativePath).TrimStart('.').ToUpperInvariant(),
            ProcessStatus.Failed,
            RunMode.MarkCopies,
            new VerificationEvidence(
                VerificationResult.Failed,
                "（未读取）",
                "验证未完成",
                DateTimeOffset.Parse("2026-07-27T08:00:00+00:00"),
                "13.59",
                evidenceError),
            Error: error);
}
