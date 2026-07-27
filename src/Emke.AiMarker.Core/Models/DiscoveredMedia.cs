namespace Emke.AiMarker.Core.Models;

public sealed record DiscoveredMedia(
    string SourcePath,
    string TopLevelInput,
    string RelativePath,
    string Extension,
    long Length);
