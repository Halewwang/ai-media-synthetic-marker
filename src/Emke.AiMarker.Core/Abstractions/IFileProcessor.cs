using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Core.Abstractions;

public interface IFileProcessor
{
    Task<ProcessResult> ProcessAsync(
        OutputPlanItem plan,
        RunMode mode,
        CancellationToken cancellationToken);
}
