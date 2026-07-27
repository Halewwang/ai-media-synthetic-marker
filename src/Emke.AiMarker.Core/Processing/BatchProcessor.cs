using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Core.Processing;

public sealed class BatchProcessor(
    IFileProcessor fileProcessor,
    IRunLogWriter logWriter) : IBatchProcessor
{
    public async Task<RunSummary> RunAsync(
        IReadOnlyList<OutputPlanItem> plans,
        RunMode mode,
        string logDirectory,
        StopController stop,
        IProgress<RunProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentNullException.ThrowIfNull(stop);
        _ = cancellationToken;

        var results = new List<ProcessResult>(plans.Count);
        bool stopped = false;
        bool logWritten = false;
        string logPath = "";

        try
        {
            for (int index = 0; index < plans.Count; index++)
            {
                OutputPlanItem plan = plans[index];
                if (stop.IsStopRequested)
                {
                    stopped = true;
                    for (int remaining = index; remaining < plans.Count; remaining++)
                    {
                        ProcessResult stoppedResult = StoppedResult(plans[remaining], mode);
                        results.Add(stoppedResult);
                        ReportProgress(
                            progress,
                            results,
                            plans.Count,
                            stoppedResult.RelativePath);
                    }

                    break;
                }

                ProcessResult result;
                try
                {
                    result = await fileProcessor.ProcessAsync(
                        plan,
                        mode,
                        CancellationToken.None);
                }
                catch (Exception exception)
                {
                    result = FailedResult(plan, mode, exception);
                }

                results.Add(result);
                ReportProgress(progress, results, plans.Count, result.RelativePath);
            }
        }
        finally
        {
            try
            {
                logPath = await logWriter.WriteAsync(
                    logDirectory,
                    mode,
                    results,
                    CancellationToken.None);
                logWritten = true;
            }
            catch (Exception)
            {
                logPath = "";
                logWritten = false;
            }
        }

        return new RunSummary(mode, results.ToArray(), logPath, logWritten, stopped);
    }

    private static ProcessResult StoppedResult(OutputPlanItem plan, RunMode mode)
    {
        const string error = "用户停止前未处理";
        return new ProcessResult(
            plan.RelativePath,
            MediaFormat(plan),
            ProcessStatus.StoppedBeforeProcessing,
            mode,
            new VerificationEvidence(
                VerificationResult.NotRun,
                "（未读取）",
                "未验证",
                DateTimeOffset.UtcNow,
                "",
                error),
            Error: error);
    }

    private static ProcessResult FailedResult(
        OutputPlanItem plan,
        RunMode mode,
        Exception exception)
    {
        string error = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
        return new ProcessResult(
            plan.RelativePath,
            MediaFormat(plan),
            ProcessStatus.Failed,
            mode,
            new VerificationEvidence(
                VerificationResult.Failed,
                "（未读取）",
                "验证未完成",
                DateTimeOffset.UtcNow,
                "",
                error),
            Error: error);
    }

    private static string MediaFormat(OutputPlanItem plan) =>
        Path.GetExtension(plan.SourcePath).TrimStart('.').ToUpperInvariant();

    private static void ReportProgress(
        IProgress<RunProgress>? progress,
        IReadOnlyList<ProcessResult> results,
        int total,
        string currentRelativePath)
    {
        if (progress is null)
        {
            return;
        }

        IReadOnlyDictionary<ProcessStatus, int> counts = results
            .GroupBy(result => result.Status)
            .ToDictionary(group => group.Key, group => group.Count());
        progress.Report(new RunProgress(
            results.Count,
            total,
            currentRelativePath,
            counts));
    }
}
