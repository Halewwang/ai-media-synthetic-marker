namespace Emke.AiMarker.Core.Models;

public enum VerificationResult
{
    Passed,
    Unmarked,
    Failed,
    NotRun,
}

public sealed record VerificationEvidence(
    VerificationResult Result,
    string ActualValue,
    string XmpStructure,
    DateTimeOffset VerifiedAt,
    string ExifToolVersion,
    string Error = "");
