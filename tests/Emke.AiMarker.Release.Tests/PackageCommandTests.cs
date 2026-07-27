using Emke.AiMarker.Release.Commands;
using Emke.AiMarker.Release.Packaging;
using Emke.AiMarker.Release.Tests.TestSupport;

namespace Emke.AiMarker.Release.Tests;

public sealed class PackageCommandTests
{
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
        Assert.EndsWith("EMKE AI Marker.exe", process.Executable, StringComparison.Ordinal);
        Assert.Equal("--self-test", process.Arguments![0]);
        Assert.Equal("--report", process.Arguments[1]);
        Assert.True(System.IO.Path.IsPathFullyQualified(process.Arguments[2]));
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
