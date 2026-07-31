using Emke.AiMarker.Core.Abstractions;
using System.Runtime.InteropServices;

namespace Emke.AiMarker.Infrastructure.Files;

public sealed class WindowsStorageProbe : IStorageProbe
{
    private readonly Func<string, FreeSpaceQueryResult> getFreeSpace;
    private readonly IPathComponentGuard pathGuard;

    public WindowsStorageProbe()
        : this(QueryFreeSpace, new PathComponentGuard())
    {
    }

    public WindowsStorageProbe(Func<string, FreeSpaceQueryResult> getFreeSpace)
        : this(getFreeSpace, new PathComponentGuard())
    {
    }

    public WindowsStorageProbe(
        Func<string, FreeSpaceQueryResult> getFreeSpace,
        IPathComponentGuard pathGuard)
    {
        this.getFreeSpace = getFreeSpace;
        this.pathGuard = pathGuard;
    }

    public long GetAvailableBytes(string directory)
    {
        string safeDirectory = pathGuard.EnsurePathAllowsMissing(directory);
        string queryDirectory = GetExistingQueryDirectory(safeDirectory);
        FreeSpaceQueryResult result =
            getFreeSpace(NormalizeDirectory(queryDirectory));
        if (!result.Succeeded)
        {
            throw new IOException($"无法读取输出目录可用空间（Windows 错误 {result.ErrorCode}）。");
        }

        try
        {
            return checked((long)result.AvailableBytes);
        }
        catch (OverflowException exception)
        {
            throw new IOException("输出目录可用空间超出支持的范围。", exception);
        }
    }

    public void AssertWritable(string directory)
    {
        string safeDirectory = pathGuard.EnsurePathAllowsMissing(directory);
        Directory.CreateDirectory(safeDirectory);
        safeDirectory = pathGuard.EnsureExistingPath(safeDirectory);
        string probePath = Path.Combine(
            safeDirectory,
            $".emke-ai-marker-probe-{Guid.NewGuid():N}.tmp");
        probePath = pathGuard.EnsurePathAllowsMissing(probePath);
        bool created = false;

        try
        {
            using (var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough))
            {
                created = true;
                stream.Flush(flushToDisk: true);
            }

            probePath = pathGuard.EnsureExistingPath(probePath);
            File.Delete(probePath);
            created = false;
        }
        finally
        {
            if (created)
            {
                string safeProbe =
                    pathGuard.EnsurePathAllowsMissing(probePath);
                if (File.Exists(safeProbe))
                {
                    File.Delete(safeProbe);
                }
            }
        }
    }

    private string GetExistingQueryDirectory(string directory)
    {
        string current = directory;
        while (!Directory.Exists(current))
        {
            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null)
            {
                return directory;
            }

            current = parent.FullName;
        }

        return pathGuard.EnsureExistingPath(current);
    }

    private static string NormalizeDirectory(string directory) =>
        $"{directory.Replace('/', '\\').TrimEnd('\\')}\\";

    private static FreeSpaceQueryResult QueryFreeSpace(string directory)
    {
        bool succeeded = GetDiskFreeSpaceEx(
            directory,
            out ulong availableBytes,
            out _,
            out _);
        return new(succeeded, availableBytes, succeeded ? 0 : Marshal.GetLastWin32Error());
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true,
        EntryPoint = "GetDiskFreeSpaceExW")]
    private static extern bool GetDiskFreeSpaceEx(
        string directory,
        out ulong availableBytes,
        out ulong totalBytes,
        out ulong totalFreeBytes);
}

public sealed record FreeSpaceQueryResult(
    bool Succeeded,
    ulong AvailableBytes,
    int ErrorCode);
