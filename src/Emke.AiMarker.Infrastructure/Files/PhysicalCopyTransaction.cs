using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Infrastructure.Files;

public sealed class PhysicalCopyTransaction : IFileTransaction
{
    private readonly Action<string, string> _copyFile;

    public PhysicalCopyTransaction()
        : this((source, destination) =>
            File.Copy(source, destination, overwrite: false))
    {
    }

    public PhysicalCopyTransaction(Action<string, string> copyFile)
    {
        ArgumentNullException.ThrowIfNull(copyFile);
        _copyFile = copyFile;
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
        if (File.Exists(plan.TempPath))
        {
            throw new IOException(
                $"计划临时文件已存在，未覆盖该文件：{plan.TempPath}");
        }

        try
        {
            _copyFile(plan.SourcePath, plan.TempPath);
        }
        catch
        {
            if (File.Exists(plan.TempPath))
            {
                File.Delete(plan.TempPath);
            }

            throw;
        }

        return Task.FromResult(new PreparedMedia(
            plan.SourcePath,
            plan.TempPath,
            plan.FinalPath));
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

        if (File.Exists(media.FinalPath))
        {
            throw new IOException(
                $"目标冲突：输出文件已存在，未覆盖该文件：{media.FinalPath}");
        }

        File.Move(media.WorkingPath, media.FinalPath, overwrite: false);
        return Task.CompletedTask;
    }

    public Task RollbackAsync(PreparedMedia media)
    {
        ArgumentNullException.ThrowIfNull(media);

        if (!OwnedTempFile.IsSamePath(media.WorkingPath, media.SourcePath)
            && !OwnedTempFile.IsSamePath(media.WorkingPath, media.FinalPath)
            && OwnedTempFile.IsOwned(media.WorkingPath, media.FinalPath)
            && File.Exists(media.WorkingPath))
        {
            File.Delete(media.WorkingPath);
        }

        return Task.CompletedTask;
    }

    private static void EnsureOwnedCopyPath(
        string workingPath,
        string sourcePath,
        string finalPath)
    {
        if (!OwnedTempFile.IsOwned(workingPath, finalPath)
            || OwnedTempFile.IsSamePath(workingPath, sourcePath)
            || OwnedTempFile.IsSamePath(workingPath, finalPath))
        {
            throw new IOException(
                "临时文件不属于计划目标目录，已拒绝复制事务。");
        }
    }
}
