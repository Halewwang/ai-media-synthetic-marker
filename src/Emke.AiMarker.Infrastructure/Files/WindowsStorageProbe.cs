using Emke.AiMarker.Core.Abstractions;
using System.Runtime.InteropServices;

namespace Emke.AiMarker.Infrastructure.Files;

public sealed class WindowsStorageProbe : IStorageProbe
{
    private readonly Func<string, FreeSpaceQueryResult> getFreeSpace;

    public WindowsStorageProbe()
        : this(QueryFreeSpace)
    {
    }

    public WindowsStorageProbe(Func<string, FreeSpaceQueryResult> getFreeSpace)
    {
        this.getFreeSpace = getFreeSpace;
    }

    public long GetAvailableBytes(string directory)
    {
        FreeSpaceQueryResult result = getFreeSpace(NormalizeDirectory(directory));
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
        Directory.CreateDirectory(directory);
        string probePath = Path.Combine(
            directory,
            $".emke-ai-marker-probe-{Guid.NewGuid():N}.tmp");

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
                stream.Flush(flushToDisk: true);
            }

            File.Delete(probePath);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
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
