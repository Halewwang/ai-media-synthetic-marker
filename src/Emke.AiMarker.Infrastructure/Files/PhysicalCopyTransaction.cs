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

    public PhysicalCopyTransaction()
        : this(CopySourceToOwnedStream)
    {
    }

    public PhysicalCopyTransaction(
        Action<string, Stream> copyToOwnedStream,
        Action<string>? beforeReserve = null)
    {
        ArgumentNullException.ThrowIfNull(copyToOwnedStream);
        _copyToOwnedStream = copyToOwnedStream;
        _beforeReserve = beforeReserve;
    }

    public Task<PreparedMedia> PrepareAsync(
        OutputPlanItem plan,
        RunMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (mode != RunMode.MarkCopies)
        {
            if (mode is not RunMode.MarkOriginals and not RunMode.VerifyOnly)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            return Task.FromResult(new PreparedMedia(
                plan.SourcePath,
                plan.SourcePath,
                plan.FinalPath));
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureOwnedCopyPath(plan.TempPath, plan.SourcePath, plan.FinalPath);
        if (File.Exists(plan.FinalPath))
        {
            throw new IOException(
                $"目标冲突：输出文件已存在，未创建临时副本：{plan.FinalPath}");
        }

        string destinationDirectory = Path.GetDirectoryName(plan.FinalPath)
            ?? throw new IOException("输出路径缺少目标目录。");
        Directory.CreateDirectory(destinationDirectory);
        _beforeReserve?.Invoke(plan.TempPath);

        OwnedTempFile owned;
        try
        {
            owned = OwnedTempFile.Reserve(plan.TempPath, plan.FinalPath);
        }
        catch (IOException exception)
        {
            throw new IOException(
                $"无法原子预留计划临时文件，未覆盖或删除现有路径：{plan.TempPath}",
                exception);
        }

        try
        {
            _copyToOwnedStream(plan.SourcePath, owned.Destination);
            owned.CompleteCopy();
            lock (_ownershipGate)
            {
                _ownedTemps.Add(owned.Token, owned);
            }
        }
        catch
        {
            owned.DeleteIfStillOwned();
            owned.Dispose();
            throw;
        }

        return Task.FromResult(new PreparedMedia(
            plan.SourcePath,
            plan.TempPath,
            plan.FinalPath,
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

        lock (_ownershipGate)
        {
            OwnedTempFile owned = GetProvenOwnership(media);
            if (!owned.StillOwnsVerifiedPath())
            {
                _ownedTemps.Remove(media.OwnershipToken);
                owned.Dispose();
                throw new IOException(
                    "临时文件缺少严格验证封存或所有权无法证明，可能已被替换；已拒绝提交。");
            }

            if (File.Exists(media.FinalPath))
            {
                throw new IOException(
                    $"目标冲突：输出文件已存在，未覆盖该文件：{media.FinalPath}");
            }

            File.Move(media.WorkingPath, media.FinalPath, overwrite: false);
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
            owned.DeleteIfStillOwned();
            owned.Dispose();
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
}
