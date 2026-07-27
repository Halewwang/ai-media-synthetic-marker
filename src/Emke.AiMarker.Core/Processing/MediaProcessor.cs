using System.Text.Json;
using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Contracts;
using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Core.Verification;

namespace Emke.AiMarker.Core.Processing;

public sealed class MediaProcessor(
    IFileTransaction files,
    IExifToolClient exifTool,
    IOriginalWriteSafety originalWriteSafety,
    TimeProvider timeProvider) : IFileProcessor
{
    public async Task<ProcessResult> ProcessAsync(
        OutputPlanItem plan,
        RunMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _ = cancellationToken;

        PreparedMedia? media = null;
        IReadOnlyList<string> subjects = [];
        string exifToolVersion = "";

        try
        {
            if (mode == RunMode.MarkCopies && File.Exists(plan.FinalPath))
            {
                return await InspectExistingOutputAsync(plan);
            }

            media = await files.PrepareAsync(
                plan,
                mode,
                CancellationToken.None);
            subjects = await exifTool.ReadSubjectsAsync(
                media.WorkingPath,
                CancellationToken.None);
            exifToolVersion = await exifTool.GetVersionAsync(
                CancellationToken.None);

            if (mode == RunMode.VerifyOnly)
            {
                VerificationEvidence evidence = await VerifyAsync(
                    media.WorkingPath,
                    subjects,
                    exifToolVersion);
                ProcessStatus status = evidence.Result switch
                {
                    VerificationResult.Passed => ProcessStatus.AlreadyCompliant,
                    VerificationResult.Unmarked => ProcessStatus.Unmarked,
                    _ => ProcessStatus.Failed,
                };
                return MakeResult(plan, mode, status, evidence);
            }

            if (subjects.Contains(MarkerContract.Marker, StringComparer.Ordinal))
            {
                VerificationEvidence evidence = await VerifyAsync(
                    media.WorkingPath,
                    subjects,
                    exifToolVersion);
                if (evidence.Result != VerificationResult.Passed)
                {
                    return await FailWithEvidenceAsync(
                        plan,
                        mode,
                        media,
                        evidence,
                        StrictVerificationError(evidence));
                }

                if (mode == RunMode.MarkCopies)
                {
                    await files.CommitAsync(media, CancellationToken.None);
                }

                return MakeResult(
                    plan,
                    mode,
                    ProcessStatus.AlreadyCompliant,
                    evidence,
                    OutputPath(plan, mode));
            }

            if (mode == RunMode.MarkOriginals)
            {
                originalWriteSafety.Validate(plan);
            }

            await exifTool.WriteMarkerAsync(
                media.WorkingPath,
                CancellationToken.None);
            subjects = await exifTool.ReadSubjectsAsync(
                media.WorkingPath,
                CancellationToken.None);
            ReadOnlyMemory<byte> rawXmp = await exifTool.ReadRawXmpAsync(
                media.WorkingPath,
                CancellationToken.None);
            VerificationEvidence afterWrite = XmpComplianceVerifier.Verify(
                subjects,
                rawXmp,
                exifToolVersion,
                timeProvider.GetUtcNow());

            if (afterWrite.Result != VerificationResult.Passed)
            {
                return await FailWithEvidenceAsync(
                    plan,
                    mode,
                    media,
                    afterWrite,
                    StrictVerificationError(afterWrite));
            }

            if (mode == RunMode.MarkCopies)
            {
                await files.CommitAsync(media, CancellationToken.None);
            }

            return MakeResult(
                plan,
                mode,
                ProcessStatus.Added,
                afterWrite,
                OutputPath(plan, mode));
        }
        catch (Exception exception)
        {
            string error = ExceptionDetail(exception);
            if (mode == RunMode.MarkCopies && media is not null)
            {
                error = await RollbackWithErrorAsync(media, error);
            }

            VerificationEvidence evidence = FailureEvidence(
                subjects,
                exifToolVersion,
                error);
            return MakeResult(
                plan,
                mode,
                ProcessStatus.Failed,
                evidence,
                error: error);
        }
    }

    private async Task<ProcessResult> InspectExistingOutputAsync(
        OutputPlanItem plan)
    {
        IReadOnlyList<string> subjects = [];
        string version = "";
        try
        {
            subjects = await exifTool.ReadSubjectsAsync(
                plan.FinalPath,
                CancellationToken.None);
            version = await exifTool.GetVersionAsync(CancellationToken.None);
            VerificationEvidence evidence = await VerifyAsync(
                plan.FinalPath,
                subjects,
                version);
            if (evidence.Result == VerificationResult.Passed)
            {
                return MakeResult(
                    plan,
                    RunMode.MarkCopies,
                    ProcessStatus.OutputAlreadyCompliant,
                    evidence,
                    plan.FinalPath);
            }

            string error =
                $"目标冲突：输出文件已存在但未通过严格验证，未覆盖该文件。{StrictVerificationError(evidence)}";
            return MakeResult(
                plan,
                RunMode.MarkCopies,
                ProcessStatus.Failed,
                evidence,
                plan.FinalPath,
                error);
        }
        catch (Exception exception)
        {
            string error =
                $"目标冲突：输出文件已存在但无法完成严格验证，未覆盖该文件。{ExceptionDetail(exception)}";
            return MakeResult(
                plan,
                RunMode.MarkCopies,
                ProcessStatus.Failed,
                FailureEvidence(subjects, version, error),
                plan.FinalPath,
                error);
        }
    }

    private async Task<VerificationEvidence> VerifyAsync(
        string path,
        IReadOnlyList<string> subjects,
        string exifToolVersion)
    {
        ReadOnlyMemory<byte> rawXmp = await exifTool.ReadRawXmpAsync(
            path,
            CancellationToken.None);
        return XmpComplianceVerifier.Verify(
            subjects,
            rawXmp,
            exifToolVersion,
            timeProvider.GetUtcNow());
    }

    private async Task<ProcessResult> FailWithEvidenceAsync(
        OutputPlanItem plan,
        RunMode mode,
        PreparedMedia media,
        VerificationEvidence evidence,
        string error)
    {
        if (mode == RunMode.MarkCopies)
        {
            error = await RollbackWithErrorAsync(media, error);
        }

        return MakeResult(
            plan,
            mode,
            ProcessStatus.Failed,
            evidence,
            error: error);
    }

    private async Task<string> RollbackWithErrorAsync(
        PreparedMedia media,
        string originalError)
    {
        try
        {
            await files.RollbackAsync(media);
            return originalError;
        }
        catch (Exception rollbackException)
        {
            return
                $"{originalError} 临时文件回滚失败：{ExceptionDetail(rollbackException)}";
        }
    }

    private VerificationEvidence FailureEvidence(
        IReadOnlyList<string> subjects,
        string exifToolVersion,
        string error) =>
        new(
            VerificationResult.Failed,
            JsonSerializer.Serialize(subjects),
            "验证未完成",
            timeProvider.GetUtcNow(),
            exifToolVersion,
            error);

    private static ProcessResult MakeResult(
        OutputPlanItem plan,
        RunMode mode,
        ProcessStatus status,
        VerificationEvidence evidence,
        string outputPath = "",
        string error = "") =>
        new(
            plan.RelativePath,
            Path.GetExtension(plan.SourcePath).TrimStart('.').ToUpperInvariant(),
            status,
            mode,
            evidence,
            outputPath,
            error);

    private static string OutputPath(OutputPlanItem plan, RunMode mode) =>
        mode == RunMode.MarkCopies ? plan.FinalPath : plan.SourcePath;

    private static string StrictVerificationError(VerificationEvidence evidence) =>
        string.IsNullOrWhiteSpace(evidence.Error)
            ? $"严格验证未通过：{evidence.XmpStructure}"
            : evidence.Error;

    private static string ExceptionDetail(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
}
