using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Emke.AiMarker.Release.Packaging;

namespace Emke.AiMarker.Release.Commands;

public interface IArchiveDownloader
{
    Task DownloadAsync(
        Uri uri,
        string destination,
        CancellationToken cancellationToken);
}

public interface IVersionProbe
{
    Task<string> GetVersionAsync(
        string executable,
        CancellationToken cancellationToken);
}

public sealed class HttpArchiveDownloader : IArchiveDownloader, IDisposable
{
    private const int MaximumRedirects = 5;
    private readonly HttpClient client;
    private readonly bool ownsClient;

    public HttpArchiveDownloader(HttpClient? client = null)
    {
        this.client = client ?? new HttpClient(
            new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromMinutes(2),
        };
        ownsClient = client is null;
    }

    public async Task DownloadAsync(
        Uri uri,
        string destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ValidateDownloadUri(uri, uri.Host);
        Uri current = uri;
        var visited = new HashSet<string>(StringComparer.Ordinal)
        {
            current.AbsoluteUri,
        };

        for (int redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("emke-ai-marker-release/2.0");
            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (IsRedirect(response.StatusCode))
            {
                if (redirects >= MaximumRedirects)
                {
                    throw new ReleaseToolException(
                        $"ExifTool 下载重定向超过 {MaximumRedirects} 次。");
                }

                Uri? location = response.Headers.Location;
                if (location is null)
                {
                    throw new ReleaseToolException(
                        "ExifTool 下载重定向缺少 Location。");
                }

                Uri next = location.IsAbsoluteUri
                    ? location
                    : new Uri(current, location);
                ValidateDownloadUri(next, uri.Host);
                if (!visited.Add(next.AbsoluteUri))
                {
                    throw new ReleaseToolException(
                        "ExifTool 下载发生重定向循环。");
                }

                current = next;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ReleaseToolException(
                    $"ExifTool 下载失败，HTTP 状态码 {(int)response.StatusCode}。");
            }

            await using Stream source =
                await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(output, 1024 * 1024, cancellationToken);
            await output.FlushAsync(cancellationToken);
            return;
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static void ValidateDownloadUri(Uri uri, string originalHost)
    {
        bool sameHost = string.Equals(
            uri.Host,
            originalHost,
            StringComparison.OrdinalIgnoreCase);
        bool sourceForgeMirror = uri.Host.EndsWith(
            ".sourceforge.net",
            StringComparison.OrdinalIgnoreCase);
        if (!uri.IsAbsoluteUri
            || !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.Ordinal)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.IsDefaultPort
            || (!sameHost && !sourceForgeMirror))
        {
            throw new ReleaseToolException(
                $"ExifTool 下载 URL 或重定向目标不受信任：{uri}");
        }
    }

    public void Dispose()
    {
        if (ownsClient)
        {
            client.Dispose();
        }
    }
}

public sealed class ProcessVersionProbe : IVersionProbe
{
    public async Task<string> GetVersionAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        startInfo.ArgumentList.Add("-ver");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new ReleaseToolException("无法启动 ExifTool 版本探针。");
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);
            string output = (await stdout).TrimStart('\uFEFF').Trim();
            string error = (await stderr).TrimStart('\uFEFF').Trim();
            if (process.ExitCode != 0)
            {
                throw new ReleaseToolException(
                    error.Length > 0
                        ? error
                        : output.Length > 0
                            ? output
                            : $"ExifTool 版本探针退出码 {process.ExitCode}。");
            }

            return output;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new ReleaseToolException("ExifTool 版本探针在 60 秒后超时。");
        }
        catch (ReleaseToolException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            TryKill(process);
            throw new ReleaseToolException(
                $"无法启动 ExifTool 版本探针：{exception.Message}",
                exception);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        catch
        {
            // Cleanup failure must not replace the primary process error.
        }
    }
}

public sealed partial class FetchExifToolCommand
{
    private const string ExpectedVersion = "13.59";
    private const string ExpectedPlatform = "windows-x64";
    private const string ManifestName = "exiftool-manifest.json";

    private static readonly string[] RequiredPayload =
    [
        "exiftool.exe",
        "README.txt",
        "exiftool_files/LICENSE",
        "exiftool_files/Licenses_Strawberry_Perl.zip",
        "exiftool_files/perl.exe",
        "exiftool_files/readme_windows.txt",
    ];

    private readonly IArchiveDownloader downloader;
    private readonly IVersionProbe versionProbe;

    public FetchExifToolCommand(
        IArchiveDownloader downloader,
        IVersionProbe versionProbe)
    {
        this.downloader = downloader
            ?? throw new ArgumentNullException(nameof(downloader));
        this.versionProbe = versionProbe
            ?? throw new ArgumentNullException(nameof(versionProbe));
    }

    public async Task ExecuteAsync(
        string lockPath,
        string targetPath,
        string? archivePath,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        ExifToolLock locked = ReadLock(Path.GetFullPath(lockPath));
        string target = Path.GetFullPath(targetPath);
        string parent = Path.GetDirectoryName(target)
            ?? throw new ReleaseToolException("ExifTool 目标目录缺少父目录。");
        Directory.CreateDirectory(parent);
        EnsureSafeAncestors(parent);
        ReleaseStageValidator.EnsureOrdinaryDirectory(parent, "ExifTool 目标父目录");

        if (await IsValidInstallationAsync(target, locked, cancellationToken))
        {
            return;
        }

        byte[]? preservedReadme = null;
        if (Directory.Exists(target))
        {
            EnsureTargetIsNotReparse(target);
            string readmePath = Path.Combine(target, "README.md");
            if (File.Exists(readmePath))
            {
                ReleaseStageValidator.EnsureOrdinaryFile(
                    readmePath,
                    "本地 runtime README.md");
                preservedReadme = File.ReadAllBytes(readmePath);
            }

            bool hasNonPlaceholderContent = Directory
                .EnumerateFileSystemEntries(target)
                .Any(path => !string.Equals(
                    Path.GetFileName(path),
                    "README.md",
                    StringComparison.OrdinalIgnoreCase));
            if (hasNonPlaceholderContent && !force)
            {
                throw new ReleaseToolException(
                    $"目标目录已有无效内容：{target}。请确认后使用 --force。");
            }
        }
        else if (File.Exists(target))
        {
            throw new ReleaseToolException($"ExifTool 目标路径不是目录：{target}");
        }

        string operationRoot = Path.Combine(
            parent,
            $".fetch-exiftool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(operationRoot);
        try
        {
            string archive = Path.Combine(operationRoot, locked.ArchiveName);
            if (archivePath is null)
            {
                await downloader.DownloadAsync(
                    locked.Url,
                    archive,
                    cancellationToken);
            }
            else
            {
                string localArchive = Path.GetFullPath(archivePath);
                ReleaseStageValidator.EnsureOrdinaryFile(
                    localArchive,
                    "本地 ExifTool 压缩包");
                File.Copy(localArchive, archive, overwrite: false);
            }

            ValidateArchiveBytes(archive, locked);
            string extracted = Path.Combine(operationRoot, "extracted");
            Directory.CreateDirectory(extracted);
            ExtractArchiveSafely(archive, extracted);

            string launcher = FindSingleLauncher(extracted);
            string payloadRoot = Path.GetDirectoryName(launcher)!;
            EnsureArchivePayload(payloadRoot);

            string installStage = Path.Combine(operationRoot, "install");
            CopyTreeWithoutLinks(payloadRoot, installStage);
            File.Move(
                Path.Combine(installStage, "exiftool(-k).exe"),
                Path.Combine(installStage, "exiftool.exe"));
            if (preservedReadme is not null)
            {
                File.WriteAllBytes(
                    Path.Combine(installStage, "README.md"),
                    preservedReadme);
            }

            WriteManifest(installStage, locked);

            string installedVersion = await versionProbe.GetVersionAsync(
                Path.Combine(installStage, "exiftool.exe"),
                cancellationToken);
            if (!string.Equals(
                    installedVersion,
                    ExpectedVersion,
                    StringComparison.Ordinal))
            {
                throw new ReleaseToolException(
                    $"ExifTool 版本不符：期望 {ExpectedVersion}，实际 {installedVersion}。");
            }

            await ValidateInstallationAsync(
                installStage,
                locked,
                versionProbe,
                cancellationToken);
            ReplaceDirectoryAtomically(installStage, target);
        }
        finally
        {
            DeleteOwnedTree(operationRoot, parent, ".fetch-exiftool-");
        }
    }

    internal static async Task ValidateInstallationAsync(
        string target,
        string lockPath,
        IVersionProbe versionProbe,
        CancellationToken cancellationToken)
    {
        ExifToolLock locked = ReadLock(Path.GetFullPath(lockPath));
        await ValidateInstallationAsync(
            Path.GetFullPath(target),
            locked,
            versionProbe,
            cancellationToken);
    }

    private static ExifToolLock ReadLock(string lockPath)
    {
        ReleaseStageValidator.EnsureOrdinaryFile(lockPath, "ExifTool 锁定文件");
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(lockPath));
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ReleaseToolException("ExifTool 锁定文件根节点必须是对象。");
        }
        RequireExactProperties(
            root,
            "ExifTool 锁定文件",
            "version",
            "platform",
            "archive_name",
            "url",
            "size",
            "sha256");

        string version = RequireString(root, "version");
        string platform = RequireString(root, "platform");
        string archiveName = RequireString(root, "archive_name");
        string urlValue = RequireString(root, "url");
        long size = RequireInt64(root, "size");
        string sha256 = RequireString(root, "sha256");

        if (!string.Equals(version, ExpectedVersion, StringComparison.Ordinal)
            || !string.Equals(platform, ExpectedPlatform, StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                "ExifTool 锁定文件必须锁定 13.59 windows-x64。");
        }

        if (string.IsNullOrWhiteSpace(archiveName)
            || !string.Equals(
                archiveName,
                Path.GetFileName(archiveName),
                StringComparison.Ordinal)
            || archiveName.Contains('\\', StringComparison.Ordinal)
            || archiveName.Contains('/', StringComparison.Ordinal))
        {
            throw new ReleaseToolException("ExifTool archive_name 必须是安全文件名。");
        }

        if (!Uri.TryCreate(urlValue, UriKind.Absolute, out Uri? url)
            || !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(url.UserInfo)
            || !string.IsNullOrEmpty(url.Fragment))
        {
            throw new ReleaseToolException(
                "ExifTool 下载地址必须是无凭据、无 fragment 的绝对 HTTPS URL。");
        }

        if (size <= 0)
        {
            throw new ReleaseToolException("ExifTool 锁定 size 必须是正整数。");
        }

        if (!Sha256Pattern().IsMatch(sha256))
        {
            throw new ReleaseToolException(
                "ExifTool 锁定 sha256 必须是 64 位十六进制值。");
        }

        return new(
            version,
            platform,
            archiveName,
            url,
            size,
            sha256.ToLowerInvariant());
    }

    private static void ValidateArchiveBytes(
        string archivePath,
        ExifToolLock locked)
    {
        ReleaseStageValidator.EnsureOrdinaryFile(
            archivePath,
            "ExifTool 压缩包");
        var info = new FileInfo(archivePath);
        if (info.Length != locked.Size)
        {
            throw new ReleaseToolException(
                $"ExifTool 压缩包大小不符：期望 {locked.Size}，实际 {info.Length}。");
        }

        string actualHash = ComputeSha256(archivePath);
        if (!string.Equals(
                actualHash,
                locked.Sha256,
                StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"ExifTool 压缩包 SHA-256 不符：期望 {locked.Sha256}，实际 {actualHash}。");
        }

        Span<byte> signature = stackalloc byte[4];
        using (var stream = File.OpenRead(archivePath))
        {
            if (stream.Read(signature) != signature.Length
                || signature[0] != 0x50
                || signature[1] != 0x4B
                || (signature[2], signature[3]) is not (
                    (0x03, 0x04) or (0x05, 0x06) or (0x07, 0x08)))
            {
                throw new ReleaseToolException(
                    "ExifTool 下载内容没有有效 ZIP magic。");
            }
        }
    }

    private static void ExtractArchiveSafely(
        string archivePath,
        string destination)
    {
        string root = Path.GetFullPath(destination);
        var explicitNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var logicalPaths = new Dictionary<string, bool>(
            StringComparer.OrdinalIgnoreCase);
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = ValidateArchiveEntryName(entry.FullName);
            string normalized = PortablePathValidator.CollisionKey(path);
            if (!explicitNames.Add(normalized))
            {
                throw new ReleaseToolException(
                    $"ZIP 包含重复、大小写或 Unicode 冲突路径：{entry.FullName}");
            }

            bool isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
            ValidateArchiveEntryType(entry, isDirectory);
            RegisterLogicalPath(logicalPaths, normalized, isDirectory);
        }

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = ValidateArchiveEntryName(entry.FullName)
                .Normalize(NormalizationForm.FormC);
            bool isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
            string output = Path.GetFullPath(
                Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsDescendant(root, output))
            {
                throw new ReleaseToolException(
                    $"ZIP 解压路径越界：{entry.FullName}");
            }

            if (isDirectory)
            {
                Directory.CreateDirectory(output);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using Stream input = entry.Open();
            using var file = new FileStream(
                output,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            input.CopyTo(file);
            file.Flush(flushToDisk: true);
        }
    }

    private static string ValidateArchiveEntryName(string name)
    {
        string withoutTrailingSlash = name.EndsWith("/", StringComparison.Ordinal)
            ? name[..^1]
            : name;
        PortablePathValidator.ValidateRelativePath(
            withoutTrailingSlash,
            "ZIP");
        return withoutTrailingSlash;
    }

    private static void ValidateArchiveEntryType(
        ZipArchiveEntry entry,
        bool isDirectory)
    {
        int unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        int unixType = unixMode & 0xF000;
        bool symlink = unixType == 0xA000;
        bool unexpectedUnixType = unixType != 0
            && unixType != 0x8000
            && unixType != 0x4000;
        bool windowsReparse =
            (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
        if (symlink || unexpectedUnixType || windowsReparse)
        {
            throw new ReleaseToolException(
                $"ZIP 包含链接、重解析点或非常规成员：{entry.FullName}");
        }

        if (isDirectory && unixType == 0x8000)
        {
            throw new ReleaseToolException(
                $"ZIP 目录成员类型不一致：{entry.FullName}");
        }
    }

    private static void RegisterLogicalPath(
        Dictionary<string, bool> logicalPaths,
        string path,
        bool isDirectory)
    {
        string[] segments = path.Split('/');
        for (int index = 1; index <= segments.Length; index++)
        {
            string current = string.Join('/', segments[..index]);
            bool currentDirectory = index < segments.Length || isDirectory;
            if (logicalPaths.TryGetValue(current, out bool existingDirectory))
            {
                if (!existingDirectory || !currentDirectory)
                {
                    throw new ReleaseToolException(
                        $"ZIP 包含文件/目录路径冲突：{path}");
                }

                continue;
            }

            logicalPaths.Add(current, currentDirectory);
        }
    }

    private static string FindSingleLauncher(string extractedRoot)
    {
        string[] launchers = EnumerateFilesWithoutLinks(extractedRoot)
            .Where(path => string.Equals(
                Path.GetFileName(path),
                "exiftool(-k).exe",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (launchers.Length != 1)
        {
            throw new ReleaseToolException(
                $"压缩包中必须恰好有一个 exiftool(-k).exe，实际为 {launchers.Length}。");
        }

        return launchers[0];
    }

    private static void EnsureArchivePayload(string payloadRoot)
    {
        ReleaseStageValidator.EnsureOrdinaryDirectory(
            Path.Combine(payloadRoot, "exiftool_files"),
            "压缩包 exiftool_files");
        ReleaseStageValidator.EnsureOrdinaryFile(
            Path.Combine(payloadRoot, "README.txt"),
            "压缩包 README.txt");
        foreach (string relative in RequiredPayload.Skip(2))
        {
            ReleaseStageValidator.EnsureOrdinaryFile(
                Path.Combine(
                    payloadRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar)),
                $"压缩包必需组件 {relative}");
        }
    }

    private static void CopyTreeWithoutLinks(string source, string destination)
    {
        ReleaseStageValidator.EnsureOrdinaryDirectory(source, "ExifTool payload");
        Directory.CreateDirectory(destination);
        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ReleaseToolException(
                    $"ExifTool payload 包含链接或重解析点：{entry}");
            }

            string output = Path.Combine(destination, Path.GetFileName(entry));
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                CopyTreeWithoutLinks(entry, output);
            }
            else
            {
                File.Copy(entry, output, overwrite: false);
            }
        }
    }

    private static void WriteManifest(
        string installRoot,
        ExifToolLock locked)
    {
        PayloadRecord[] records = CollectPayloadRecords(installRoot);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                schema_version = 1,
                exiftool_version = locked.Version,
                archive_name = locked.ArchiveName,
                archive_size = locked.Size,
                archive_sha256 = locked.Sha256,
                files = records.Select(record => new
                {
                    path = record.Path,
                    size = record.Size,
                    sha256 = record.Sha256,
                }),
            },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllBytes(Path.Combine(installRoot, ManifestName), json);
    }

    private async Task<bool> IsValidInstallationAsync(
        string target,
        ExifToolLock locked,
        CancellationToken cancellationToken)
    {
        try
        {
            await ValidateInstallationAsync(
                target,
                locked,
                versionProbe,
                cancellationToken);
            return true;
        }
        catch (Exception exception) when (
            exception is ReleaseToolException
                or IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return false;
        }
    }

    private static async Task ValidateInstallationAsync(
        string target,
        ExifToolLock locked,
        IVersionProbe versionProbe,
        CancellationToken cancellationToken)
    {
        ReleaseStageValidator.EnsureOrdinaryDirectory(
            target,
            "ExifTool 运行目录");
        foreach (string relative in RequiredPayload)
        {
            ReleaseStageValidator.EnsureOrdinaryFile(
                Path.Combine(
                    target,
                    relative.Replace('/', Path.DirectorySeparatorChar)),
                $"ExifTool 必需组件 {relative}");
        }

        string manifestPath = Path.Combine(target, ManifestName);
        ReleaseStageValidator.EnsureOrdinaryFile(
            manifestPath,
            "ExifTool runtime manifest");
        RuntimeManifest manifest = ReadRuntimeManifest(manifestPath, locked);
        PayloadRecord[] actual = CollectPayloadRecords(target);
        if (!manifest.Files.SequenceEqual(actual))
        {
            throw new ReleaseToolException(
                "ExifTool runtime payload 与逐文件清单不一致。");
        }

        string version = await versionProbe.GetVersionAsync(
            Path.Combine(target, "exiftool.exe"),
            cancellationToken);
        if (!string.Equals(version, ExpectedVersion, StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"ExifTool 运行版本必须为 {ExpectedVersion}，实际为 {version}。");
        }
    }

    private static RuntimeManifest ReadRuntimeManifest(
        string manifestPath,
        ExifToolLock locked)
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(manifestPath));
        JsonElement root = document.RootElement;
        RequireExactProperties(
            root,
            "ExifTool runtime manifest",
            "schema_version",
            "exiftool_version",
            "archive_name",
            "archive_size",
            "archive_sha256",
            "files");
        if (RequireInt64(root, "schema_version") != 1
            || !string.Equals(
                RequireString(root, "exiftool_version"),
                locked.Version,
                StringComparison.Ordinal)
            || !string.Equals(
                RequireString(root, "archive_name"),
                locked.ArchiveName,
                StringComparison.Ordinal)
            || RequireInt64(root, "archive_size") != locked.Size
            || !string.Equals(
                RequireString(root, "archive_sha256"),
                locked.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException(
                "ExifTool runtime manifest 元数据与锁定文件不一致。");
        }

        if (!root.TryGetProperty("files", out JsonElement files)
            || files.ValueKind != JsonValueKind.Array)
        {
            throw new ReleaseToolException(
                "ExifTool runtime manifest files 必须是数组。");
        }

        var records = new List<PayloadRecord>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in files.EnumerateArray())
        {
            RequireExactProperties(
                item,
                "ExifTool runtime manifest payload 记录",
                "path",
                "size",
                "sha256");
            string path = RequireString(item, "path");
            ValidateManifestPath(path);
            string normalizedPath = PortablePathValidator.CollisionKey(path);
            if (!paths.Add(normalizedPath))
            {
                throw new ReleaseToolException(
                    $"ExifTool runtime manifest 包含重复路径：{path}");
            }

            long size = RequireInt64(item, "size");
            string sha256 = RequireString(item, "sha256").ToLowerInvariant();
            if (size < 0 || !Sha256Pattern().IsMatch(sha256))
            {
                throw new ReleaseToolException(
                    $"ExifTool runtime manifest payload 记录无效：{path}");
            }

            records.Add(new(path, size, sha256));
        }

        PayloadRecord[] ordered = records
            .OrderBy(record => record.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Path, StringComparer.Ordinal)
            .ToArray();
        if (!records.SequenceEqual(ordered))
        {
            throw new ReleaseToolException(
                "ExifTool runtime manifest files 必须稳定排序。");
        }

        return new(records.ToArray());
    }

    private static PayloadRecord[] CollectPayloadRecords(string root)
    {
        return EnumerateFilesWithoutLinks(root)
            .Select(path => new
            {
                FullPath = path,
                Relative = ReleaseStageValidator.NormalizeRelativePath(
                    Path.GetRelativePath(root, path)),
            })
            .Where(item => !string.Equals(
                    item.Relative,
                    ManifestName,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    item.Relative,
                    "README.md",
                    StringComparison.OrdinalIgnoreCase))
            .Select(item => new PayloadRecord(
                item.Relative,
                new FileInfo(item.FullPath).Length,
                ComputeSha256(item.FullPath)))
            .OrderBy(record => record.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateFilesWithoutLinks(string root)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ReleaseToolException(
                    $"ExifTool payload 包含链接或重解析点：{entry}");
            }

            if (attributes.HasFlag(FileAttributes.Directory))
            {
                foreach (string nested in EnumerateFilesWithoutLinks(entry))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return entry;
            }
        }
    }

    private static void ReplaceDirectoryAtomically(
        string installStage,
        string target)
    {
        string parent = Path.GetDirectoryName(target)!;
        string? backup = null;
        if (Directory.Exists(target))
        {
            backup = Path.Combine(
                parent,
                $".{Path.GetFileName(target)}.backup-{Guid.NewGuid():N}");
            Directory.Move(target, backup);
        }

        try
        {
            Directory.Move(installStage, target);
        }
        catch
        {
            if (backup is not null
                && Directory.Exists(backup)
                && !Directory.Exists(target))
            {
                Directory.Move(backup, target);
            }

            throw;
        }

        if (backup is not null)
        {
            DeleteOwnedTree(
                backup,
                parent,
                $".{Path.GetFileName(target)}.backup-");
        }
    }

    private static void EnsureTargetIsNotReparse(string target)
    {
        FileAttributes attributes = File.GetAttributes(target);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ReleaseToolException(
                $"ExifTool 目标目录不能是链接或重解析点：{target}");
        }
    }

    private static void EnsureSafeAncestors(string path)
    {
        string fullPath = Path.GetFullPath(path);
        FileAttributes attributes = File.GetAttributes(fullPath);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ReleaseToolException(
                $"ExifTool 目标父目录不能是链接或重解析点：{fullPath}");
        }
    }

    private static void DeleteOwnedTree(
        string path,
        string expectedParent,
        string expectedPrefix)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return;
        }

        string full = Path.GetFullPath(path);
        string parent = Path.GetFullPath(expectedParent);
        if (!string.Equals(
                Path.GetDirectoryName(full),
                parent,
                PathComparison)
            || !Path.GetFileName(full).StartsWith(
                expectedPrefix,
                StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"拒绝清理不属于 release tool 的路径：{full}");
        }

        DeleteTreeNoFollow(full);
    }

    private static void DeleteTreeNoFollow(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint)
            || !attributes.HasFlag(FileAttributes.Directory))
        {
            File.Delete(path);
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            DeleteTreeNoFollow(entry);
        }

        Directory.Delete(path);
    }

    private static bool IsDescendant(string root, string candidate)
    {
        string relative = Path.GetRelativePath(root, candidate);
        return relative != ".."
            && !relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            && !Path.IsPathFullyQualified(relative);
    }

    private static void ValidateManifestPath(string path)
    {
        PortablePathValidator.ValidateRelativePath(
            path,
            "ExifTool runtime manifest");
        if (string.Equals(path, ManifestName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "README.md", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReleaseToolException(
                $"ExifTool runtime manifest 包含不安全路径：{path}");
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

    private static string RequireString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new ReleaseToolException($"JSON 字段 {name} 必须是字符串。");
        }

        return value.GetString()!;
    }

    private static long RequireInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out long result))
        {
            throw new ReleaseToolException($"JSON 字段 {name} 必须是整数。");
        }

        return result;
    }

    private static void RequireExactProperties(
        JsonElement element,
        string description,
        params string[] expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ReleaseToolException($"{description} 必须是对象。");
        }

        string[] actual = element.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected = expectedProperties
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new ReleaseToolException(
                $"{description} 包含未知、缺失或重复字段。");
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    private sealed record ExifToolLock(
        string Version,
        string Platform,
        string ArchiveName,
        Uri Url,
        long Size,
        string Sha256);

    private sealed record PayloadRecord(
        string Path,
        long Size,
        string Sha256);

    private sealed record RuntimeManifest(PayloadRecord[] Files);
}
