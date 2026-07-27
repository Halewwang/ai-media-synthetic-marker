namespace Emke.AiMarker.Core.Models;

public enum RunMode
{
    MarkCopies,
    MarkOriginals,
    VerifyOnly,
}

public sealed record RunProgress(
    int Completed,
    int Total,
    string CurrentRelativePath,
    IReadOnlyDictionary<ProcessStatus, int> Counts);

public sealed record RunSummary(
    RunMode Mode,
    IReadOnlyList<ProcessResult> Results,
    string LogPath,
    bool LogWritten,
    bool Stopped);
