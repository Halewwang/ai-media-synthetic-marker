using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Core.Abstractions;

public sealed record PreparedMedia(
    string SourcePath,
    string WorkingPath,
    string FinalPath);

public interface IFileTransaction
{
    Task<PreparedMedia> PrepareAsync(
        OutputPlanItem plan,
        RunMode mode,
        CancellationToken cancellationToken);

    Task CommitAsync(
        PreparedMedia media,
        CancellationToken cancellationToken);

    Task RollbackAsync(PreparedMedia media);
}
