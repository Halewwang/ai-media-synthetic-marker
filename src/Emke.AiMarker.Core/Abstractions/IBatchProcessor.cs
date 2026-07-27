using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Core.Processing;

namespace Emke.AiMarker.Core.Abstractions;

public interface IBatchProcessor
{
    Task<RunSummary> RunAsync(
        IReadOnlyList<OutputPlanItem> plans,
        RunMode mode,
        string logDirectory,
        StopController stop,
        IProgress<RunProgress>? progress,
        CancellationToken cancellationToken);
}
