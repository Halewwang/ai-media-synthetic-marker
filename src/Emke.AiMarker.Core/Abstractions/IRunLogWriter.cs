using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Core.Abstractions;

public interface IRunLogWriter
{
    Task<string> WriteAsync(
        string logDirectory,
        RunMode mode,
        IReadOnlyList<ProcessResult> results,
        CancellationToken cancellationToken);
}
