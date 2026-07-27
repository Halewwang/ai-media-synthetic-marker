namespace Emke.AiMarker.Core.Models;

public enum PathEntryKind
{
    Missing,
    File,
    Directory,
    ReparseFile,
    ReparseDirectory,
}

public sealed record ScanIssue(string Path, string Error);

public sealed record ScanResult(
    IReadOnlyList<DiscoveredMedia> Media,
    IReadOnlyList<ScanIssue> Issues);
