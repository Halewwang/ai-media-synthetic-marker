using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Infrastructure.Files;

public sealed class PhysicalCopyTransaction : IFileTransaction
{
    private readonly object _ownershipGate = new();
    private readonly Dictionary<string, OwnedTempFile> _ownedTemps =
        new(StringComparer.Ordinal);
    private readonly Action<string, Stream> _copyToOwnedStream;
    private readonly Action<string>? _beforeReserve;
    private readonly Action<string>? _afterReserve;
    private readonly Action<string>? _atCommitBoundary;
    private readonly Action<string>? _atRollbackBoundary;
    private readonly IPathComponentGuard _pathGuard;

    public PhysicalCopyTransaction(
        Action<string, Stream>? copyToOwnedStream = null,
        Action<string>? beforeReserve = null,
        Action<string>? afterReserve = null,
        Action<string>? atCommitBoundary = null,
        Action<string>? atRollbackBoundary = null,
        IPathComponentGuard? pathGuard = null)
    {
        _copyToOwnedStream = copyToOwnedStream ?? CopySourceToOwnedStream;
        _beforeReserve = beforeReserve;
        _afterReserve = afterReserve;
        _atCommitBoundary = atCommitBoundary;
        _atRollbackBoundary = atRollbackBoundary;
        _pathGuard = pathGuard ?? new PathComponentGuard();
    }

    public void ValidatePlan(OutputPlanItem plan, RunMode mode)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _ = GuardPlan(plan, mode);
    }

    public Task<PreparedMedia> PrepareAsync(
        OutputPlanItem plan,
        RunMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        GuardedPaths paths = GuardPlan(plan, mode);

        if (mode != RunMode.MarkCopies)
        {
            if (mode is not RunMode.MarkOriginals and not RunMode.VerifyOnly)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            return Task.FromResult(new PreparedMedia(
                paths.SourcePath,
                paths.SourcePath,
                paths.FinalPath));
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureOwnedCopyPath(
            paths.TempPath,
            paths.SourcePath,
            paths.FinalPath);
        if (File.Exists(paths.FinalPath))
        {
            throw new IOException(
                $"目标冲突：输出文件已存在，未创建临时副本：{paths.FinalPath}");
        }

        string destinationDirectory = Path.GetDirectoryName(paths.FinalPath)
            ?? throw new IOException("输出路径缺少目标目录。");
        Directory.CreateDirectory(destinationDirectory);
        GuardCopyPaths(paths, requireTemp: false);
        _beforeReserve?.Invoke(paths.TempPath);
        GuardCopyPaths(paths, requireTemp: false);

        OwnedTempFile owned;
        try
        {
            owned = OwnedTempFile.Reserve(
                paths.TempPath,
                paths.FinalPath);
        }
        catch (IOException exception)
        {
            throw new IOException(
                $"无法原子预留计划临时文件，未覆盖或删除现有路径：{paths.TempPath}",
                exception);
        }

        try
        {
            _afterReserve?.Invoke(paths.TempPath);
            GuardCopyPaths(paths, requireTemp: true);
            _copyToOwnedStream(paths.SourcePath, owned.Destination);
            owned.CompleteCopy();
            lock (_ownershipGate)
            {
                _ownedTemps.Add(owned.Token, owned);
            }
        }
        catch
        {
            try
            {
                owned.DeleteOwnedLease();
            }
            finally
            {
                owned.Dispose();
            }

            throw;
        }

        return Task.FromResult(new PreparedMedia(
            paths.SourcePath,
            paths.TempPath,
            paths.FinalPath,
            owned.Token));
    }

    public Task CommitAsync(
        PreparedMedia media,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOwnedCopyPath(
            media.WorkingPath,
            media.SourcePath,
            media.FinalPath);
        GuardCopyPaths(media);

        lock (_ownershipGate)
        {
            OwnedTempFile owned = GetProvenOwnership(media);
            if (!owned.StillOwnsVerifiedPath())
            {
                throw new IOException(
                    "临时文件缺少严格验证封存或所有权无法证明，可能已被替换；已拒绝提交。");
            }

            if (File.Exists(media.FinalPath))
            {
                throw new IOException(
                    $"目标冲突：输出文件已存在，未覆盖该文件：{media.FinalPath}");
            }

            _atCommitBoundary?.Invoke(media.WorkingPath);
            GuardCopyPaths(media);
            owned.RenameVerifiedTo(media.FinalPath);
            _ownedTemps.Remove(media.OwnershipToken);
            owned.Dispose();
        }

        return Task.CompletedTask;
    }

    public Task SealVerifiedAsync(
        PreparedMedia media,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOwnedCopyPath(
            media.WorkingPath,
            media.SourcePath,
            media.FinalPath);
        GuardCopyPaths(media);

        lock (_ownershipGate)
        {
            OwnedTempFile owned = GetProvenOwnership(media);
            owned.SealVerifiedPath();
        }

        return Task.CompletedTask;
    }

    public Task RollbackAsync(PreparedMedia media)
    {
        ArgumentNullException.ThrowIfNull(media);

        lock (_ownershipGate)
        {
            if (!_ownedTemps.TryGetValue(
                    media.OwnershipToken,
                    out OwnedTempFile? owned)
                || !owned.Matches(
                    media.SourcePath,
                    media.WorkingPath,
                    media.FinalPath,
                    media.OwnershipToken))
            {
                return Task.CompletedTask;
            }

            _ownedTemps.Remove(media.OwnershipToken);
            try
            {
                if (!owned.LockOwnedPathForFinalization())
                {
                    return Task.CompletedTask;
                }

                _atRollbackBoundary?.Invoke(media.WorkingPath);
                owned.DeleteOwnedLease();
            }
            finally
            {
                owned.Dispose();
            }
        }

        return Task.CompletedTask;
    }

    private OwnedTempFile GetProvenOwnership(PreparedMedia media)
    {
        if (!_ownedTemps.TryGetValue(
                media.OwnershipToken,
                out OwnedTempFile? owned)
            || !owned.Matches(
                media.SourcePath,
                media.WorkingPath,
                media.FinalPath,
                media.OwnershipToken))
        {
            throw new IOException(
                "临时文件缺少当前事务的所有权证明，已拒绝提交。");
        }

        return owned;
    }

    private GuardedPaths GuardPlan(OutputPlanItem plan, RunMode mode)
    {
        string sourcePath =
            _pathGuard.EnsureExistingPath(plan.SourcePath);
        switch (mode)
        {
            case RunMode.MarkCopies:
                string finalPath =
                    _pathGuard.EnsurePathAllowsMissing(plan.FinalPath);
                string tempPath =
                    _pathGuard.EnsurePathAllowsMissing(plan.TempPath);
                EnsureOwnedCopyPath(tempPath, sourcePath, finalPath);
                return new(sourcePath, finalPath, tempPath);
            case RunMode.MarkOriginals:
            case RunMode.VerifyOnly:
                return new(
                    sourcePath,
                    Path.GetFullPath(plan.FinalPath),
                    Path.GetFullPath(plan.TempPath));
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    private void GuardCopyPaths(
        GuardedPaths paths,
        bool requireTemp)
    {
        _pathGuard.EnsureExistingPath(paths.SourcePath);
        _pathGuard.EnsurePathAllowsMissing(paths.FinalPath);
        if (requireTemp)
        {
            _pathGuard.EnsureExistingPath(paths.TempPath);
        }
        else
        {
            _pathGuard.EnsurePathAllowsMissing(paths.TempPath);
        }
    }

    private void GuardCopyPaths(PreparedMedia media)
    {
        _pathGuard.EnsureExistingPath(media.SourcePath);
        _pathGuard.EnsurePathAllowsMissing(media.FinalPath);
        _pathGuard.EnsureExistingPath(media.WorkingPath);
    }

    private static void EnsureOwnedCopyPath(
        string workingPath,
        string sourcePath,
        string finalPath)
    {
        if (!OwnedTempFile.HasOwnedPathShape(workingPath, finalPath)
            || OwnedTempFile.IsSamePath(workingPath, sourcePath)
            || OwnedTempFile.IsSamePath(workingPath, finalPath))
        {
            throw new IOException(
                "临时文件不属于计划目标目录，已拒绝复制事务。");
        }
    }

    private static void CopySourceToOwnedStream(
        string sourcePath,
        Stream destination)
    {
        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        source.CopyTo(destination);
    }

    private sealed record GuardedPaths(
        string SourcePath,
        string FinalPath,
        string TempPath);
}
