using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Emke.AiMarker.Release.Packaging;

public static partial class DeterministicZipWriter
{
    public const long DefaultEpoch = 1_700_000_000;
    private static readonly DateTimeOffset MinimumZipTime =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MaximumZipTime =
        new(2107, 12, 31, 23, 59, 58, TimeSpan.Zero);

    public static long ResolveEpoch(string? configured)
    {
        if (configured is null)
        {
            return DefaultEpoch;
        }

        if (!long.TryParse(
                configured,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out long epoch))
        {
            throw new ReleaseToolException(
                "SOURCE_DATE_EPOCH 必须是非负十进制整数。");
        }

        DateTimeOffset timestamp;
        try
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(epoch);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new ReleaseToolException(
                "SOURCE_DATE_EPOCH 超出 ZIP 可表示范围。",
                exception);
        }

        if (timestamp < MinimumZipTime || timestamp > MaximumZipTime)
        {
            throw new ReleaseToolException(
                "SOURCE_DATE_EPOCH 必须位于 1980-01-01 至 2107-12-31 的 ZIP 时间范围内。");
        }

        return epoch;
    }

    public static void Write(
        string stageRoot,
        string outputPath,
        string rootName,
        long epoch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (string.IsNullOrWhiteSpace(rootName)
            || !AsciiRootName().IsMatch(rootName))
        {
            throw new ReleaseToolException(
                "ZIP 根目录名必须是非空 ASCII 字母、数字、点、下划线或连字符。");
        }

        DateTimeOffset timestamp;
        try
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(epoch);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new ReleaseToolException(
                "ZIP 时间戳超出范围。",
                exception);
        }

        if (timestamp < MinimumZipTime || timestamp > MaximumZipTime)
        {
            throw new ReleaseToolException("ZIP 时间戳超出 1980 至 2107 范围。");
        }

        string root = Path.GetFullPath(stageRoot);
        ReleaseStageValidator.EnsureOrdinaryDirectory(root, "ZIP 输入目录");
        string output = Path.GetFullPath(outputPath);
        string outputRelative = Path.GetRelativePath(root, output);
        if (outputRelative is "."
            || outputRelative != ".."
                && !outputRelative.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                && !Path.IsPathFullyQualified(outputRelative))
        {
            throw new ReleaseToolException(
                "ZIP 输出路径不能位于输入暂存目录内。");
        }

        if (File.Exists(output) || Directory.Exists(output))
        {
            throw new ReleaseToolException($"ZIP 输出路径已存在：{output}");
        }

        string? outputDirectory = Path.GetDirectoryName(output);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ReleaseToolException("ZIP 输出路径缺少父目录。");
        }

        Directory.CreateDirectory(outputDirectory);
        IReadOnlyList<ArchiveItem> items = CollectItems(root, rootName);
        try
        {
            using var stream = new FileStream(
                output,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None);
            using var archive = new ZipArchive(
                stream,
                ZipArchiveMode.Create,
                leaveOpen: false,
                entryNameEncoding: System.Text.Encoding.UTF8);
            foreach (ArchiveItem item in items)
            {
                ZipArchiveEntry entry = archive.CreateEntry(
                    item.ArchivePath,
                    item.IsDirectory
                        ? CompressionLevel.NoCompression
                        : CompressionLevel.SmallestSize);
                entry.LastWriteTime = timestamp;
                entry.ExternalAttributes = item.IsDirectory
                    ? (0x41ED << 16) | 0x10
                    : (item.ArchivePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? 0x81ED << 16
                        : 0x81A4 << 16);
                if (item.IsDirectory)
                {
                    continue;
                }

                using FileStream source = new(
                    item.FullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 1024 * 1024,
                    FileOptions.SequentialScan);
                using Stream destination = entry.Open();
                source.CopyTo(destination, 1024 * 1024);
            }
        }
        catch
        {
            File.Delete(output);
            throw;
        }
    }

    private static IReadOnlyList<ArchiveItem> CollectItems(
        string root,
        string rootName)
    {
        var items = new List<ArchiveItem>
        {
            new(root, $"{rootName}/", IsDirectory: true),
        };
        Walk(root, root, rootName, items);
        return items
            .OrderBy(item => item.ArchivePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static void Walk(
        string root,
        string directory,
        string rootName,
        List<ArchiveItem> items)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(
                     directory,
                     "*",
                     new EnumerationOptions
                     {
                         RecurseSubdirectories = false,
                         IgnoreInaccessible = false,
                         AttributesToSkip = 0,
                     }))
        {
            FileAttributes attributes = File.GetAttributes(path);
            string relative = ReleaseStageValidator.NormalizeRelativePath(
                Path.GetRelativePath(root, path));
            if (relative.StartsWith("../", StringComparison.Ordinal)
                || relative is ".."
                || relative.Contains('\\', StringComparison.Ordinal))
            {
                throw new ReleaseToolException(
                    $"ZIP 输入路径越界：{relative}");
            }

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ReleaseToolException(
                    $"ZIP 输入包含链接或重解析点：{relative}");
            }

            bool directoryEntry = attributes.HasFlag(FileAttributes.Directory);
            string archivePath = $"{rootName}/{relative}"
                + (directoryEntry ? "/" : "");
            items.Add(new(path, archivePath, directoryEntry));
            if (directoryEntry)
            {
                Walk(root, path, rootName, items);
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AsciiRootName();

    private sealed record ArchiveItem(
        string FullPath,
        string ArchivePath,
        bool IsDirectory);
}
