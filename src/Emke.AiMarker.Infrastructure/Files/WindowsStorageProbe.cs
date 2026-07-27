using Emke.AiMarker.Core.Abstractions;

namespace Emke.AiMarker.Infrastructure.Files;

public sealed class WindowsStorageProbe : IStorageProbe
{
    public long GetAvailableBytes(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        string root = Path.GetPathRoot(fullPath)
            ?? throw new IOException("无法确定输出目录所在的驱动器。");
        return new DriveInfo(root).AvailableFreeSpace;
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
}
