namespace Emke.AiMarker.Core.Models;

public enum ProcessStatus
{
    Added,
    AlreadyCompliant,
    OutputAlreadyCompliant,
    Unmarked,
    Failed,
    Skipped,
    StoppedBeforeProcessing,
}

public sealed record ProcessResult(
    string RelativePath,
    string MediaFormat,
    ProcessStatus Status,
    RunMode Mode,
    VerificationEvidence Evidence,
    string OutputPath = "",
    string Error = "");
