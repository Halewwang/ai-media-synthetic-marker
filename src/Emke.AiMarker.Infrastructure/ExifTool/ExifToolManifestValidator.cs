using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Emke.AiMarker.Infrastructure.ExifTool;

public static partial class ExifToolManifestValidator
{
    private const string ExpectedVersion = "13.59";
    private const string ManifestName = "exiftool-manifest.json";

    private static readonly string[] RequiredFiles =
    [
        "exiftool.exe",
        "README.txt",
        "exiftool_files/LICENSE",
        "exiftool_files/Licenses_Strawberry_Perl.zip",
        "exiftool_files/perl.exe",
        "exiftool_files/readme_windows.txt",
    ];

    public static void Validate(string runtimeRoot, string lockPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);

        try
        {
            string normalizedRoot = Path.GetFullPath(runtimeRoot);
            EnsureDirectory(normalizedRoot, "ExifTool 运行目录");

            LockMetadata locked = ReadLock(Path.GetFullPath(lockPath));
            Manifest manifest = ReadManifest(
                Path.Combine(normalizedRoot, ManifestName),
                locked);

            EnsureDirectory(
                Path.Combine(normalizedRoot, "exiftool_files"),
                "ExifTool 组件目录 exiftool_files");
            foreach (string relativePath in RequiredFiles)
            {
                EnsureRegularFile(
                    CombineManifestPath(normalizedRoot, relativePath),
                    $"ExifTool 必需组件 {relativePath}");
            }

            Dictionary<string, string> actualFiles = CollectPayloadFiles(normalizedRoot);
            string? unexpected = actualFiles.Keys
                .FirstOrDefault(path => !manifest.Files.ContainsKey(path));
            if (unexpected is not null)
            {
                throw new ExifToolIntegrityException(
                    $"ExifTool 运行目录包含未列入清单的 payload：{unexpected}");
            }

            string? missing = manifest.Files.Keys
                .FirstOrDefault(path => !actualFiles.ContainsKey(path));
            if (missing is not null)
            {
                throw new ExifToolIntegrityException(
                    $"ExifTool 清单中的 payload 缺失：{missing}");
            }

            foreach ((string relativePath, PayloadRecord expected) in manifest.Files)
            {
                string fullPath = actualFiles[relativePath];
                long actualSize = new FileInfo(fullPath).Length;
                if (actualSize != expected.Size)
                {
                    throw new ExifToolIntegrityException(
                        $"ExifTool payload 大小不符：{relativePath}；"
                        + $"期望 {expected.Size}，实际 {actualSize}");
                }

                string actualSha256 = ComputeSha256(fullPath);
                if (!string.Equals(
                        actualSha256,
                        expected.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ExifToolIntegrityException(
                        $"ExifTool payload SHA-256 不符：{relativePath}；"
                        + $"期望 {expected.Sha256}，实际 {actualSha256}");
                }
            }
        }
        catch (ExifToolIntegrityException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or ArgumentException
                or NotSupportedException)
        {
            throw new ExifToolIntegrityException(
                $"无法验证 ExifTool 运行时完整性：{exception.Message}");
        }
    }

    private static LockMetadata ReadLock(string lockPath)
    {
        EnsureRegularFile(lockPath, "ExifTool 锁定文件");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(lockPath));
        JsonElement root = RequireObject(document.RootElement, "ExifTool 锁定文件");

        string version = RequireString(root, "version", "ExifTool 锁定文件");
        string archiveName = RequireString(
            root,
            "archive_name",
            "ExifTool 锁定文件");
        long archiveSize = RequireInt64(root, "size", "ExifTool 锁定文件");
        string archiveSha256 = RequireString(
            root,
            "sha256",
            "ExifTool 锁定文件");

        if (!string.Equals(version, ExpectedVersion, StringComparison.Ordinal))
        {
            throw new ExifToolIntegrityException(
                $"ExifTool 锁定文件版本必须是 {ExpectedVersion}，实际是 {version}。");
        }

        if (archiveName.Length == 0)
        {
            throw new ExifToolIntegrityException(
                "ExifTool 锁定文件 archive_name 不能为空。");
        }

        if (archiveSize <= 0)
        {
            throw new ExifToolIntegrityException(
                "ExifTool 锁定文件 size 必须是正整数。");
        }

        if (!Sha256Pattern().IsMatch(archiveSha256))
        {
            throw new ExifToolIntegrityException(
                "ExifTool 锁定文件 sha256 必须是 64 位十六进制值。");
        }

        return new LockMetadata(
            version,
            archiveName,
            archiveSize,
            archiveSha256);
    }

    private static Manifest ReadManifest(
        string manifestPath,
        LockMetadata locked)
    {
        EnsureRegularFile(manifestPath, "ExifTool runtime manifest");
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(manifestPath));
        JsonElement root = RequireObject(
            document.RootElement,
            "ExifTool runtime manifest");

        long schemaVersion = RequireInt64(
            root,
            "schema_version",
            "ExifTool runtime manifest");
        string exifToolVersion = RequireString(
            root,
            "exiftool_version",
            "ExifTool runtime manifest");
        string archiveName = RequireString(
            root,
            "archive_name",
            "ExifTool runtime manifest");
        long archiveSize = RequireInt64(
            root,
            "archive_size",
            "ExifTool runtime manifest");
        string archiveSha256 = RequireString(
            root,
            "archive_sha256",
            "ExifTool runtime manifest");

        if (schemaVersion != 1)
        {
            throw new ExifToolIntegrityException(
                $"ExifTool runtime manifest schema_version 必须是 1，"
                + $"实际是 {schemaVersion}。");
        }

        if (!string.Equals(
                exifToolVersion,
                locked.Version,
                StringComparison.Ordinal)
            || !string.Equals(
                archiveName,
                locked.ArchiveName,
                StringComparison.Ordinal)
            || archiveSize != locked.ArchiveSize
            || !string.Equals(
                archiveSha256,
                locked.ArchiveSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ExifToolIntegrityException(
                "ExifTool runtime manifest 元数据与锁定文件不一致。");
        }

        if (!root.TryGetProperty("files", out JsonElement files)
            || files.ValueKind != JsonValueKind.Array)
        {
            throw new ExifToolIntegrityException(
                "ExifTool runtime manifest 的 files 必须是数组。");
        }

        var records = new Dictionary<string, PayloadRecord>(
            StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in files.EnumerateArray())
        {
            JsonElement record = RequireObject(
                item,
                "ExifTool runtime manifest payload 记录");
            string relativePath = RequireString(
                record,
                "path",
                "ExifTool runtime manifest payload 记录");
            long size = RequireInt64(
                record,
                "size",
                $"ExifTool runtime manifest payload {relativePath}");
            string sha256 = RequireString(
                record,
                "sha256",
                $"ExifTool runtime manifest payload {relativePath}");

            ValidateManifestPath(relativePath);
            if (size < 0)
            {
                throw new ExifToolIntegrityException(
                    $"ExifTool runtime manifest payload 大小无效：{relativePath}");
            }

            if (!Sha256Pattern().IsMatch(sha256))
            {
                throw new ExifToolIntegrityException(
                    $"ExifTool runtime manifest payload SHA-256 格式无效："
                    + relativePath);
            }

            if (!records.TryAdd(
                    relativePath,
                    new PayloadRecord(size, sha256)))
            {
                throw new ExifToolIntegrityException(
                    $"ExifTool runtime manifest 包含重复路径：{relativePath}");
            }
        }

        return new Manifest(records);
    }

    private static Dictionary<string, string> CollectPayloadFiles(string root)
    {
        var files = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        CollectDirectory(root, root, files);
        return files;
    }

    private static void CollectDirectory(
        string root,
        string directory,
        Dictionary<string, string> files)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(
                     directory,
                     "*",
                     new EnumerationOptions
                     {
                         RecurseSubdirectories = false,
                         IgnoreInaccessible = false,
                         AttributesToSkip = 0,
                     }))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            string relativePath = Path.GetRelativePath(root, entry)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ExifToolIntegrityException(
                    $"ExifTool payload 包含不允许的重解析点：{relativePath}");
            }

            if (attributes.HasFlag(FileAttributes.Directory))
            {
                CollectDirectory(root, entry, files);
                continue;
            }

            if (IsUntrackedRuntimeDocumentation(relativePath))
            {
                continue;
            }

            if (!files.TryAdd(relativePath, entry))
            {
                throw new ExifToolIntegrityException(
                    $"ExifTool 运行目录包含大小写冲突的 payload：{relativePath}");
            }
        }
    }

    private static bool IsUntrackedRuntimeDocumentation(string relativePath) =>
        string.Equals(relativePath, ManifestName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(relativePath, "README.md", StringComparison.OrdinalIgnoreCase);

    private static void EnsureDirectory(string path, string description)
    {
        FileAttributes attributes = GetAttributes(path, description);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ExifToolIntegrityException(
                $"{description}不能是重解析点：{path}");
        }

        if (!attributes.HasFlag(FileAttributes.Directory))
        {
            throw new ExifToolIntegrityException(
                $"{description}不是目录：{path}");
        }
    }

    private static void EnsureRegularFile(string path, string description)
    {
        FileAttributes attributes = GetAttributes(path, description);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ExifToolIntegrityException(
                $"{description}不能是重解析点：{path}");
        }

        if (attributes.HasFlag(FileAttributes.Directory))
        {
            throw new ExifToolIntegrityException(
                $"{description}不是文件：{path}");
        }
    }

    private static FileAttributes GetAttributes(
        string path,
        string description)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            throw new ExifToolIntegrityException(
                $"{description}缺失：{path}");
        }
        catch (DirectoryNotFoundException)
        {
            throw new ExifToolIntegrityException(
                $"{description}缺失：{path}");
        }
    }

    private static string CombineManifestPath(string root, string relativePath)
    {
        ValidateManifestPath(relativePath);
        return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void ValidateManifestPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath.Contains('\\', StringComparison.Ordinal)
            || relativePath.Contains(':', StringComparison.Ordinal)
            || relativePath.StartsWith("/", StringComparison.Ordinal)
            || DrivePrefixPattern().IsMatch(relativePath))
        {
            throw new ExifToolIntegrityException(
                $"ExifTool runtime manifest 包含不安全路径：{relativePath}");
        }

        string[] segments = relativePath.Split('/');
        if (segments.Any(segment =>
                segment.Length == 0
                || segment is "." or ".."))
        {
            throw new ExifToolIntegrityException(
                $"ExifTool runtime manifest 包含不安全路径：{relativePath}");
        }

        if (IsUntrackedRuntimeDocumentation(relativePath))
        {
            throw new ExifToolIntegrityException(
                $"ExifTool runtime manifest 不应把清单或本地说明列为 payload："
                + relativePath);
        }
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static JsonElement RequireObject(
        JsonElement element,
        string description)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ExifToolIntegrityException(
                $"{description}必须是 JSON 对象。");
        }

        return element;
    }

    private static string RequireString(
        JsonElement parent,
        string propertyName,
        string description)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            throw new ExifToolIntegrityException(
                $"{description}的 {propertyName} 必须是字符串。");
        }

        return property.GetString()!;
    }

    private static long RequireInt64(
        JsonElement parent,
        string propertyName,
        string description)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt64(out long value))
        {
            throw new ExifToolIntegrityException(
                $"{description}的 {propertyName} 必须是整数。");
        }

        return value;
    }

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[A-Za-z]:", RegexOptions.CultureInvariant)]
    private static partial Regex DrivePrefixPattern();

    private sealed record LockMetadata(
        string Version,
        string ArchiveName,
        long ArchiveSize,
        string ArchiveSha256);

    private sealed record PayloadRecord(long Size, string Sha256);

    private sealed record Manifest(
        Dictionary<string, PayloadRecord> Files);
}
