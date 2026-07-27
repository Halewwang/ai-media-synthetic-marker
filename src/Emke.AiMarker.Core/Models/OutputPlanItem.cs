namespace Emke.AiMarker.Core.Models;

public sealed record OutputPlanItem(
    string SourcePath,
    string RelativePath,
    string FinalPath,
    string TempPath,
    long Length);
