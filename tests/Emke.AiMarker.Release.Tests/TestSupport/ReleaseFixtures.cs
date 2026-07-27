using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Emke.AiMarker.Release.Commands;

namespace Emke.AiMarker.Release.Tests.TestSupport;

internal static class ReleaseFixtures
{
    public static string CreateManifest(TemporaryDirectory temp)
    {
        return temp.CreateFile(
            "release-manifest.json",
            """
            {
              "schema_version": 1,
              "product": "EMKE AI Marker",
              "version": "2.0.0",
              "platform": "windows-x64",
              "required_paths": [
                "EMKE AI Marker.exe",
                "使用说明.txt",
                "LICENSE.txt",
                "THIRD_PARTY_NOTICES.txt",
                "exiftool/exiftool.exe",
                "exiftool/exiftool-manifest.json",
                "licenses/dotnet/LICENSE.txt",
                "licenses/dotnet/ThirdPartyNotices.txt",
                "示例输出/EMKE 已标记/"
              ]
            }
            """);
    }

    public static string CreateValidStage(TemporaryDirectory temp)
    {
        string stage = temp.CreateDirectory("stage");
        Write(stage, "EMKE AI Marker.exe");
        Write(stage, "使用说明.txt");
        Write(stage, "LICENSE.txt");
        Write(stage, "THIRD_PARTY_NOTICES.txt");
        Write(stage, "exiftool/exiftool.exe");
        Write(stage, "exiftool/exiftool-manifest.json", "{}");
        Write(stage, "licenses/dotnet/LICENSE.txt");
        Write(stage, "licenses/dotnet/ThirdPartyNotices.txt");
        Directory.CreateDirectory(System.IO.Path.Combine(stage, "示例输出", "EMKE 已标记"));
        return stage;
    }

    public static FetchFixture CreateFetchFixture(
        TemporaryDirectory temp,
        Action<ZipArchive>? mutate = null)
    {
        string archivePath = System.IO.Path.Combine(temp.Path, "exiftool.zip");
        using (var stream = new FileStream(archivePath, FileMode.CreateNew))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Add(archive, "exiftool-13.59_64/exiftool(-k).exe", "fake-exe");
            Add(archive, "exiftool-13.59_64/README.txt", "readme");
            Add(archive, "exiftool-13.59_64/exiftool_files/LICENSE", "license");
            Add(
                archive,
                "exiftool-13.59_64/exiftool_files/Licenses_Strawberry_Perl.zip",
                "licenses");
            Add(archive, "exiftool-13.59_64/exiftool_files/perl.exe", "perl");
            Add(
                archive,
                "exiftool-13.59_64/exiftool_files/readme_windows.txt",
                "readme");
            mutate?.Invoke(archive);
        }

        byte[] archiveBytes = File.ReadAllBytes(archivePath);
        string sha = Convert.ToHexString(SHA256.HashData(archiveBytes))
            .ToLowerInvariant();
        string lockPath = temp.CreateFile(
            "exiftool.lock.json",
            JsonSerializer.Serialize(
                new
                {
                    version = "13.59",
                    platform = "windows-x64",
                    archive_name = "exiftool.zip",
                    url = "https://downloads.example.invalid/exiftool.zip",
                    size = archiveBytes.LongLength,
                    sha256 = sha,
                }));
        return new(
            archivePath,
            lockPath,
            System.IO.Path.Combine(temp.Path, "runtime", "exiftool"));
    }

    public static string CreatePackageRepository(
        TemporaryDirectory temp,
        FetchFixture fetch)
    {
        string root = temp.CreateDirectory("repo");
        Write(root, "build/publish/win-x64/EMKE AI Marker.exe", "app");
        Write(root, "build/publish/win-x64/EMKE AI Marker.dll", "managed");
        Write(root, "release_template/使用说明.txt", "instructions");
        Directory.CreateDirectory(
            System.IO.Path.Combine(root, "release_template", "示例输出", "EMKE 已标记"));
        Write(root, "LICENSE", "project license");
        Write(root, "THIRD_PARTY_NOTICES.md", "notices");
        Write(root, "packaging/licenses/dotnet/LICENSE.txt", "dotnet license");
        Write(
            root,
            "packaging/licenses/dotnet/ThirdPartyNotices.txt",
            "dotnet notices");
        File.Copy(
            fetch.LockPath,
            System.IO.Path.Combine(root, "packaging", "exiftool.lock.json"),
            overwrite: true);
        File.Copy(
            fetch.LockPath,
            System.IO.Path.Combine(
                root,
                "build",
                "publish",
                "win-x64",
                "exiftool.lock.json"),
            overwrite: true);
        return root;
    }

    public static string PublishDirectory(string root) =>
        System.IO.Path.Combine(root, "build", "publish", "win-x64");

    public static void Write(string root, string relativePath, string content = "fixture")
    {
        string path = System.IO.Path.Combine(
            root,
            relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void Add(ZipArchive archive, string path, string value)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using StreamWriter writer = new(entry.Open());
        writer.Write(value);
    }
}

internal sealed record FetchFixture(
    string ArchivePath,
    string LockPath,
    string TargetPath);

internal sealed class CopyingDownloader(string source) : IArchiveDownloader
{
    public Uri? RequestedUri { get; private set; }

    public Task DownloadAsync(
        Uri uri,
        string destination,
        CancellationToken cancellationToken)
    {
        RequestedUri = uri;
        File.Copy(source, destination, overwrite: false);
        return Task.CompletedTask;
    }
}

internal sealed class FixedVersionProbe(string version) : IVersionProbe
{
    public Task<string> GetVersionAsync(
        string executable,
        CancellationToken cancellationToken) =>
        Task.FromResult(version);
}

internal sealed class RecordingPackageProcess : IPackageProcessRunner
{
    public int ExitCode { get; set; }

    public bool WriteSuccessfulReport { get; set; } = true;

    public string? Executable { get; private set; }

    public IReadOnlyList<string>? Arguments { get; private set; }

    public Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        Executable = executable;
        Arguments = arguments;
        int reportIndex = Array.IndexOf(arguments.ToArray(), "--report");
        if (WriteSuccessfulReport && reportIndex >= 0)
        {
            File.WriteAllText(
                arguments[reportIndex + 1],
                """
                AppVersion=2.0.0
                Runtime=.NET 10
                ExifTool=13.59
                Result=ok
                """);
        }

        return Task.FromResult(ExitCode);
    }
}
