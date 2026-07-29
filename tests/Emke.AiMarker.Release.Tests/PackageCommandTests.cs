using Emke.AiMarker.Release.Commands;
using Emke.AiMarker.Release.Packaging;
using Emke.AiMarker.Release.Tests.TestSupport;

namespace Emke.AiMarker.Release.Tests;

public sealed class PackageCommandTests
{
    public static TheoryData<string, string> SelfTestStagePollution => new()
    {
        { "private.jpg", "media" },
        { "records/验证.csv", "csv" },
        { "使用说明.txt", @"C:\Users\private\source" },
        { "示例输出/EMKE 已标记/unexpected.bin", "pollution" },
    };

    [Fact]
    public async Task Builds_stage_runs_exact_self_test_then_writes_zip_and_checksum()
    {
        using var temp = new TemporaryDirectory();
        FetchFixture fetch = ReleaseFixtures.CreateFetchFixture(temp);
        var acquisition = new FetchExifToolCommand(
            new CopyingDownloader(fetch.ArchivePath),
            new FixedVersionProbe("13.59"));
        await acquisition.ExecuteAsync(
            fetch.LockPath,
            fetch.TargetPath,
            fetch.ArchivePath,
            false,
            CancellationToken.None);
        string root = ReleaseFixtures.CreatePackageRepository(temp, fetch);
        CopyDirectory(fetch.TargetPath, System.IO.Path.Combine(root, "runtime", "exiftool"));
        string manifest = ReleaseFixtures.CreateManifest(temp);
        File.Copy(
            manifest,
            System.IO.Path.Combine(root, "packaging", "release-manifest.json"));
        var process = new RecordingPackageProcess();
        var command = new PackageCommand(
            process,
            new FixedVersionProbe("13.59"));

        PackageResult result = await command.ExecuteAsync(
            root,
            ReleaseFixtures.PublishDirectory(root),
            System.IO.Path.Combine(root, "dist"),
            1_700_000_000,
            CancellationToken.None);

        Assert.True(File.Exists(result.ZipPath));
        Assert.True(File.Exists(result.ChecksumPath));
        Assert.Equal(
            $"{result.Sha256}  {System.IO.Path.GetFileName(result.ZipPath)}\n",
            File.ReadAllText(result.ChecksumPath));
        Assert.Equal(2, process.Calls.Count);
        Assert.EndsWith(
            "EMKE AI Marker.exe",
            process.Calls[0].Executable,
            StringComparison.Ordinal);
        Assert.Equal(
            ["--self-test", "--report"],
            process.Calls[0].Arguments.Take(2));
        Assert.Equal(
            ["--ui-self-test", "--report"],
            process.Calls[1].Arguments.Take(2));
        Assert.All(
            process.Calls,
            call => Assert.True(System.IO.Path.IsPathFullyQualified(
                call.Arguments[2])));
    }

    [Fact]
    public async Task Failed_self_test_leaves_no_publishable_zip_or_checksum()
    {
        using var temp = new TemporaryDirectory();
        FetchFixture fetch = ReleaseFixtures.CreateFetchFixture(temp);
        var acquisition = new FetchExifToolCommand(
            new CopyingDownloader(fetch.ArchivePath),
            new FixedVersionProbe("13.59"));
        await acquisition.ExecuteAsync(
            fetch.LockPath,
            fetch.TargetPath,
            fetch.ArchivePath,
            false,
            CancellationToken.None);
        string root = ReleaseFixtures.CreatePackageRepository(temp, fetch);
        CopyDirectory(fetch.TargetPath, System.IO.Path.Combine(root, "runtime", "exiftool"));
        string manifest = ReleaseFixtures.CreateManifest(temp);
        File.Copy(
            manifest,
            System.IO.Path.Combine(root, "packaging", "release-manifest.json"));
        string dist = System.IO.Path.Combine(root, "dist");
        Directory.CreateDirectory(dist);
        string oldZip = System.IO.Path.Combine(
            dist,
            "emke-ai-marker-v2.0.0-windows-x64.zip");
        File.WriteAllText(oldZip, "old");
        File.WriteAllText(System.IO.Path.Combine(dist, "SHA256SUMS.txt"), "old");
        var process = new RecordingPackageProcess
        {
            ExitCode = 1,
            WriteSuccessfulReport = false,
        };
        var command = new PackageCommand(
            process,
            new FixedVersionProbe("13.59"));

        await Assert.ThrowsAsync<ReleaseToolException>(
            () => command.ExecuteAsync(
                root,
                ReleaseFixtures.PublishDirectory(root),
                dist,
                1_700_000_000,
                CancellationToken.None));

        Assert.False(File.Exists(oldZip));
        Assert.False(File.Exists(System.IO.Path.Combine(dist, "SHA256SUMS.txt")));
    }

    [Fact]
    public async Task Rejects_arbitrary_directory_that_is_not_the_release_publish_output()
    {
        using var temp = new TemporaryDirectory();
        FetchFixture fetch = ReleaseFixtures.CreateFetchFixture(temp);
        var acquisition = new FetchExifToolCommand(
            new CopyingDownloader(fetch.ArchivePath),
            new FixedVersionProbe("13.59"));
        await acquisition.ExecuteAsync(
            fetch.LockPath,
            fetch.TargetPath,
            fetch.ArchivePath,
            false,
            CancellationToken.None);
        string root = ReleaseFixtures.CreatePackageRepository(temp, fetch);
        CopyDirectory(fetch.TargetPath, System.IO.Path.Combine(root, "runtime", "exiftool"));
        string manifest = ReleaseFixtures.CreateManifest(temp);
        File.Copy(
            manifest,
            System.IO.Path.Combine(root, "packaging", "release-manifest.json"));
        string arbitrary = System.IO.Path.Combine(root, "arbitrary");
        CopyDirectory(ReleaseFixtures.PublishDirectory(root), arbitrary);
        var command = new PackageCommand(
            new RecordingPackageProcess(),
            new FixedVersionProbe("13.59"));

        await Assert.ThrowsAsync<ReleaseToolException>(
            () => command.ExecuteAsync(
                root,
                arbitrary,
                System.IO.Path.Combine(root, "dist"),
                1_700_000_000,
                CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_publish_path_with_intermediate_symlink_to_outside()
    {
        using var temp = new TemporaryDirectory();
        (string root, FetchFixture fetch) = await PrepareRepositoryAsync(temp);
        string external = temp.CreateDirectory("external-publish/child");
        CopyDirectory(ReleaseFixtures.PublishDirectory(root), external);
        string link = System.IO.Path.Combine(root, "build", "publish", "link");
        Directory.CreateSymbolicLink(
            link,
            System.IO.Path.GetDirectoryName(external)!);
        var process = new RecordingPackageProcess();
        var command = new PackageCommand(
            process,
            new FixedVersionProbe("13.59"));

        await Assert.ThrowsAsync<ReleaseToolException>(
            () => command.ExecuteAsync(
                root,
                System.IO.Path.Combine(link, "child"),
                System.IO.Path.Combine(root, "dist"),
                1_700_000_000,
                CancellationToken.None));

        Assert.Empty(process.Calls);
        Assert.True(File.Exists(
            System.IO.Path.Combine(external, "EMKE AI Marker.exe")));
    }

    [Fact]
    public async Task Rejects_output_path_with_intermediate_symlink_without_deleting_outside()
    {
        using var temp = new TemporaryDirectory();
        (string root, FetchFixture fetch) = await PrepareRepositoryAsync(temp);
        string external = temp.CreateDirectory("external-output/dist");
        string zip = System.IO.Path.Combine(
            external,
            "emke-ai-marker-v2.0.0-windows-x64.zip");
        string checksum = System.IO.Path.Combine(external, "SHA256SUMS.txt");
        File.WriteAllText(zip, "outside zip");
        File.WriteAllText(checksum, "outside checksum");
        string link = System.IO.Path.Combine(root, "output-link");
        Directory.CreateSymbolicLink(
            link,
            System.IO.Path.GetDirectoryName(external)!);
        var process = new RecordingPackageProcess();
        var command = new PackageCommand(
            process,
            new FixedVersionProbe("13.59"));

        await Assert.ThrowsAsync<ReleaseToolException>(
            () => command.ExecuteAsync(
                root,
                ReleaseFixtures.PublishDirectory(root),
                System.IO.Path.Combine(link, "dist"),
                1_700_000_000,
                CancellationToken.None));

        Assert.Empty(process.Calls);
        Assert.Equal("outside zip", File.ReadAllText(zip));
        Assert.Equal("outside checksum", File.ReadAllText(checksum));
    }

    [Fact]
    public async Task Self_test_cannot_replace_output_with_link_or_delete_external_sentinels()
    {
        using var temp = new TemporaryDirectory();
        (string root, FetchFixture fetch) = await PrepareRepositoryAsync(temp);
        string dist = System.IO.Path.Combine(root, "dist");
        string external = temp.CreateDirectory("external-dist");
        string zip = System.IO.Path.Combine(
            external,
            "emke-ai-marker-v2.0.0-windows-x64.zip");
        string checksum = System.IO.Path.Combine(external, "SHA256SUMS.txt");
        byte[] zipSentinel = [0x45, 0x4d, 0x4b, 0x45, 0x00, 0xff];
        byte[] checksumSentinel = [0x53, 0x48, 0x41, 0x32, 0x35, 0x36];
        File.WriteAllBytes(zip, zipSentinel);
        File.WriteAllBytes(checksum, checksumSentinel);
        var process = new RecordingPackageProcess
        {
            MutateWorkingDirectory = _ =>
            {
                Directory.Delete(dist);
                Directory.CreateSymbolicLink(dist, external);
            },
        };
        var command = new PackageCommand(
            process,
            new FixedVersionProbe("13.59"));

        Exception? error = await Record.ExceptionAsync(
            () => command.ExecuteAsync(
                root,
                ReleaseFixtures.PublishDirectory(root),
                dist,
                1_700_000_000,
                CancellationToken.None));

        Assert.True(File.Exists(zip), "external ZIP sentinel was deleted");
        Assert.True(File.Exists(checksum), "external checksum sentinel was deleted");
        Assert.Equal(zipSentinel, File.ReadAllBytes(zip));
        Assert.Equal(checksumSentinel, File.ReadAllBytes(checksum));
        Assert.Equal(
            ["SHA256SUMS.txt", "emke-ai-marker-v2.0.0-windows-x64.zip"],
            Directory.EnumerateFiles(external)
                .Select(path => System.IO.Path.GetFileName(path)!)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.IsType<ReleaseToolException>(error);
    }

    [Theory]
    [MemberData(nameof(SelfTestStagePollution))]
    public async Task Rejects_stage_pollution_created_by_successful_self_test(
        string relativePath,
        string content)
    {
        using var temp = new TemporaryDirectory();
        (string root, FetchFixture fetch) = await PrepareRepositoryAsync(temp);
        string dist = System.IO.Path.Combine(root, "dist");
        var process = new RecordingPackageProcess
        {
            MutateWorkingDirectory = stage =>
                ReleaseFixtures.Write(stage, relativePath, content),
        };
        var command = new PackageCommand(
            process,
            new FixedVersionProbe("13.59"));

        await Assert.ThrowsAsync<ReleaseToolException>(
            () => command.ExecuteAsync(
                root,
                ReleaseFixtures.PublishDirectory(root),
                dist,
                1_700_000_000,
                CancellationToken.None));

        Assert.False(File.Exists(
            System.IO.Path.Combine(
                dist,
                "emke-ai-marker-v2.0.0-windows-x64.zip")));
        Assert.False(File.Exists(
            System.IO.Path.Combine(dist, "SHA256SUMS.txt")));
    }

    private static async Task<(string Root, FetchFixture Fetch)>
        PrepareRepositoryAsync(TemporaryDirectory temp)
    {
        FetchFixture fetch = ReleaseFixtures.CreateFetchFixture(temp);
        var acquisition = new FetchExifToolCommand(
            new CopyingDownloader(fetch.ArchivePath),
            new FixedVersionProbe("13.59"));
        await acquisition.ExecuteAsync(
            fetch.LockPath,
            fetch.TargetPath,
            fetch.ArchivePath,
            false,
            CancellationToken.None);
        string root = ReleaseFixtures.CreatePackageRepository(temp, fetch);
        CopyDirectory(
            fetch.TargetPath,
            System.IO.Path.Combine(root, "runtime", "exiftool"));
        string manifest = ReleaseFixtures.CreateManifest(temp);
        File.Copy(
            manifest,
            System.IO.Path.Combine(root, "packaging", "release-manifest.json"));
        return (root, fetch);
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(
                System.IO.Path.Combine(
                    destination,
                    System.IO.Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            string output = System.IO.Path.Combine(
                destination,
                System.IO.Path.GetRelativePath(source, file));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
            File.Copy(file, output);
        }
    }
}
