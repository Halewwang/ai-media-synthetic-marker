using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Emke.AiMarker.Release.Packaging;

public sealed class ReleaseToolException : Exception
{
    public ReleaseToolException(string message)
        : base(message)
    {
    }

    public ReleaseToolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static partial class ReleaseStageValidator
{
    private const int MaximumTextBytes = 2 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16LittleEndian =
        new(false, false, true);
    private static readonly UnicodeEncoding StrictUtf16BigEndian =
        new(true, false, true);
    private static readonly UTF32Encoding StrictUtf32LittleEndian =
        new(false, false, true);
    private static readonly UTF32Encoding StrictUtf32BigEndian =
        new(true, false, true);

    private static readonly string[] ExactRequiredPaths =
    [
        "EMKE AI Marker.exe",
        "使用说明.txt",
        "LICENSE.txt",
        "THIRD_PARTY_NOTICES.txt",
        "exiftool/exiftool.exe",
        "exiftool/exiftool-manifest.json",
        "licenses/dotnet/LICENSE.txt",
        "licenses/dotnet/ThirdPartyNotices.txt",
        "示例输出/EMKE 已标记/",
    ];

    private static readonly HashSet<string> ForbiddenExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".mp4",
            ".csv",
            ".py",
            ".pyw",
            ".pyc",
            ".pyo",
            ".log",
            ".spec",
        };

    private static readonly HashSet<string> ForbiddenNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".gitkeep",
            "__pycache__",
            "desktop.ini",
            "thumbs.db",
        };

    private static readonly HashSet<string> TextExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt",
            ".md",
            ".json",
            ".toml",
            ".ini",
            ".yaml",
            ".yml",
        };

    private static readonly HashSet<string> AllowedTopLevelDocuments =
        new(StringComparer.Ordinal)
        {
            "使用说明.txt",
            "LICENSE.txt",
            "THIRD_PARTY_NOTICES.txt",
            "exiftool.lock.json",
        };

    private static readonly HashSet<string> LockedVendorTextWithPathExamples =
        new(StringComparer.Ordinal)
        {
            "exiftool/exiftool_files/windows_exiftool.txt",
        };

    public static void Validate(string stageRoot, string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        try
        {
            string root = Path.GetFullPath(stageRoot);
            EnsureOrdinaryDirectory(root, "发布暂存目录");
            ReleaseManifest manifest = ReadManifest(Path.GetFullPath(manifestPath));

            foreach (string requiredPath in manifest.RequiredPaths)
            {
                ValidateRequiredPath(root, requiredPath);
            }

            string requiredEmptyDirectory = Path.Combine(
                root,
                "示例输出",
                "EMKE 已标记");
            if (Directory.EnumerateFileSystemEntries(requiredEmptyDirectory).Any())
            {
                throw new ReleaseToolException(
                    "必需发布目录 示例输出/EMKE 已标记/ 必须为空。");
            }

            var normalizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Walk(root, root, normalizedPaths);
        }
        catch (ReleaseToolException)
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
            throw new ReleaseToolException(
                $"无法验证发布暂存目录：{exception.Message}",
                exception);
        }
    }

    public static void ValidatePortablePathSet(IEnumerable<string> relativePaths)
    {
        PortablePathValidator.ValidatePathSet(relativePaths, "路径集合");
    }

    internal static void EnsureOrdinaryDirectory(string path, string description)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ReleaseToolException($"{description}不存在：{path}", exception);
        }

        if (!attributes.HasFlag(FileAttributes.Directory)
            || attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ReleaseToolException(
                $"{description}必须是普通目录且不能是链接或重解析点：{path}");
        }
    }

    internal static void EnsureOrdinaryFile(string path, string description)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ReleaseToolException($"{description}不存在：{path}", exception);
        }

        if (attributes.HasFlag(FileAttributes.Directory)
            || attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ReleaseToolException(
                $"{description}必须是普通文件且不能是链接或重解析点：{path}");
        }
    }

    internal static string NormalizeRelativePath(string relativePath)
    {
        string normalized = relativePath
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        return normalized.Normalize(NormalizationForm.FormC);
    }

    private static ReleaseManifest ReadManifest(string manifestPath)
    {
        EnsureOrdinaryFile(manifestPath, "release manifest");
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(manifestPath));
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ReleaseToolException("release manifest 根节点必须是对象。");
        }

        string[] actualProperties = root.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedProperties =
        [
            "platform",
            "product",
            "required_paths",
            "schema_version",
            "version",
        ];
        if (!actualProperties.SequenceEqual(expectedProperties, StringComparer.Ordinal))
        {
            throw new ReleaseToolException("release manifest 字段不符合 schema 1。");
        }

        int schemaVersion = RequireInt(root, "schema_version");
        string product = RequireString(root, "product");
        string version = RequireString(root, "version");
        string platform = RequireString(root, "platform");
        if (schemaVersion != 1
            || !string.Equals(product, "EMKE AI Marker", StringComparison.Ordinal)
            || !string.Equals(version, "2.0.0", StringComparison.Ordinal)
            || !string.Equals(platform, "windows-x64", StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                "release manifest 产品、版本或平台不符合 EMKE AI Marker 2.0.0 Windows x64。");
        }

        if (!root.TryGetProperty("required_paths", out JsonElement required)
            || required.ValueKind != JsonValueKind.Array)
        {
            throw new ReleaseToolException("release manifest required_paths 必须是数组。");
        }

        string[] paths = required.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : throw new ReleaseToolException(
                    "release manifest required_paths 只能包含字符串。"))
            .ToArray();
        if (!paths.SequenceEqual(ExactRequiredPaths, StringComparer.Ordinal))
        {
            throw new ReleaseToolException(
                "release manifest required_paths 与发布合同不一致。");
        }

        return new(paths);
    }

    private static void ValidateRequiredPath(string root, string requiredPath)
    {
        bool directory = requiredPath.EndsWith("/", StringComparison.Ordinal);
        string pathWithoutSlash = directory ? requiredPath[..^1] : requiredPath;
        PortablePathValidator.ValidateRelativePath(
            pathWithoutSlash,
            "required path");
        string fullPath = Path.Combine(
            root,
            pathWithoutSlash.Replace('/', Path.DirectorySeparatorChar));
        if (directory)
        {
            EnsureOrdinaryDirectory(fullPath, $"必需发布目录 {requiredPath}");
        }
        else
        {
            EnsureOrdinaryFile(fullPath, $"必需发布文件 {requiredPath}");
            if (new FileInfo(fullPath).Length == 0)
            {
                throw new ReleaseToolException($"必需发布文件不能为空：{requiredPath}");
            }
        }
    }

    private static void Walk(
        string root,
        string directory,
        HashSet<string> normalizedPaths)
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
            string relativePath = NormalizeRelativePath(
                Path.GetRelativePath(root, entry));
            PortablePathValidator.ValidateRelativePath(relativePath, "stage path");

            if (!normalizedPaths.Add(
                    PortablePathValidator.CollisionKey(relativePath)))
            {
                throw new ReleaseToolException(
                    $"发布暂存目录包含大小写或 Unicode 规范化冲突路径：{relativePath}");
            }

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ReleaseToolException(
                    $"发布暂存目录不允许链接或重解析点：{relativePath}");
            }

            string name = Path.GetFileName(entry);
            if (ForbiddenNames.Contains(name))
            {
                throw new ReleaseToolException(
                    $"发布暂存目录包含禁止名称：{relativePath}");
            }

            if (name.EndsWith("_original", StringComparison.OrdinalIgnoreCase))
            {
                throw new ReleaseToolException(
                    $"发布暂存目录包含 ExifTool 备份：{relativePath}");
            }

            bool isDirectory = attributes.HasFlag(FileAttributes.Directory);
            if (isDirectory)
            {
                Walk(root, entry, normalizedPaths);
                continue;
            }

            string extension = Path.GetExtension(name);
            if (ForbiddenExtensions.Contains(extension))
            {
                throw new ReleaseToolException(
                    $"发布暂存目录包含禁止文件类型：{relativePath}");
            }

            if (Path.GetDirectoryName(relativePath)?.Length is null or 0
                && TextExtensions.Contains(extension)
                && !IsAllowedTopLevelText(name))
            {
                throw new ReleaseToolException(
                    $"发布暂存目录包含意外的顶层文档：{relativePath}");
            }

            CheckTextForAbsolutePaths(entry, relativePath);
        }
    }

    private static bool IsAllowedTopLevelText(string name) =>
        AllowedTopLevelDocuments.Contains(name)
        || name.EndsWith(".deps.json", StringComparison.Ordinal)
        || name.EndsWith(".runtimeconfig.json", StringComparison.Ordinal);

    private static void CheckTextForAbsolutePaths(
        string fullPath,
        string relativePath)
    {
        if (!TextExtensions.Contains(Path.GetExtension(fullPath)))
        {
            return;
        }

        var info = new FileInfo(fullPath);
        if (info.Length > MaximumTextBytes)
        {
            throw new ReleaseToolException(
                $"发布文本超过 {MaximumTextBytes} 字节扫描上限：{relativePath}");
        }

        byte[] bytes = File.ReadAllBytes(fullPath);
        string text = DecodeText(bytes, relativePath);
        if (LockedVendorTextWithPathExamples.Contains(relativePath))
        {
            return;
        }

        if (WindowsDrivePath().IsMatch(text)
            || UncPath().IsMatch(text)
            || UnixUserPath().IsMatch(text))
        {
            throw new ReleaseToolException(
                $"发布文本包含本机绝对路径：{relativePath}");
        }
    }

    private static string DecodeText(byte[] bytes, string relativePath)
    {
        ReadOnlySpan<byte> data = bytes;
        Encoding encoding;
        int preambleLength;
        if (data.StartsWith(new byte[] { 0x00, 0x00, 0xfe, 0xff }))
        {
            encoding = StrictUtf32BigEndian;
            preambleLength = 4;
        }
        else if (data.StartsWith(new byte[] { 0xff, 0xfe, 0x00, 0x00 }))
        {
            encoding = StrictUtf32LittleEndian;
            preambleLength = 4;
        }
        else if (data.StartsWith(new byte[] { 0xfe, 0xff }))
        {
            encoding = StrictUtf16BigEndian;
            preambleLength = 2;
        }
        else if (data.StartsWith(new byte[] { 0xff, 0xfe }))
        {
            encoding = StrictUtf16LittleEndian;
            preambleLength = 2;
        }
        else if (data.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
        {
            encoding = StrictUtf8;
            preambleLength = 3;
        }
        else
        {
            if (data.Contains((byte)0))
            {
                throw new ReleaseToolException(
                    $"发布文本包含无 BOM 的可疑 Unicode 编码：{relativePath}");
            }

            encoding = StrictUtf8;
            preambleLength = 0;
        }

        try
        {
            return encoding.GetString(data[preambleLength..]);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ReleaseToolException(
                $"发布文本编码无效：{relativePath}",
                exception);
        }
    }

    private static int RequireInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int result))
        {
            throw new ReleaseToolException($"release manifest 的 {name} 必须是整数。");
        }

        return result;
    }

    private static string RequireString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new ReleaseToolException($"release manifest 的 {name} 必须是字符串。");
        }

        return value.GetString()!;
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsDrivePath();

    [GeneratedRegex(@"\\\\[^\\\r\n]+\\[^\\\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex UncPath();

    [GeneratedRegex(@"/(?:Users|home)/[^/\s]+(?:/|$)", RegexOptions.CultureInvariant)]
    private static partial Regex UnixUserPath();

    private sealed record ReleaseManifest(IReadOnlyList<string> RequiredPaths);
}
