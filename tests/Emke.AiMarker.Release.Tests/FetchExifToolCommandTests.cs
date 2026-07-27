using System.IO.Compression;
using System.Text.Json;
using Emke.AiMarker.Release.Commands;
using Emke.AiMarker.Release.Packaging;
using Emke.AiMarker.Release.Tests.TestSupport;

namespace Emke.AiMarker.Release.Tests;

public sealed class FetchExifToolCommandTests
{
    [Fact]
    public async Task Installs_verified_archive_and_is_idempotent()
    {
        using var temp = new TemporaryDirectory();
        FetchFixture fixture = ReleaseFixtures.CreateFetchFixture(temp);
        var downloader = new CopyingDownloader(fixture.ArchivePath);
        var command = new FetchExifToolCommand(
            downloader,
            new FixedVersionProbe("13.59"));

        await command.ExecuteAsync(
            fixture.LockPath,
            fixture.TargetPath,
            archivePath: null,
            force: false,
            CancellationToken.None);
        string manifest = System.IO.Path.Combine(
            fixture.TargetPath,
            "exiftool-manifest.json");
        Assert.True(File.Exists(manifest));
        Assert.True(File.Exists(
            System.IO.Path.Combine(fixture.TargetPath, "exiftool.exe")));
        Assert.False(File.Exists(
            System.IO.Path.Combine(fixture.TargetPath, "exiftool(-k).exe")));
        Assert.Equal(
            "https://downloads.example.invalid/exiftool.zip",
            downloader.RequestedUri!.AbsoluteUri);
        using (JsonDocument document = JsonDocument.Parse(
                   File.ReadAllBytes(manifest)))
        {
            Assert.Equal(
                1,
                document.RootElement.GetProperty("schema_version").GetInt32());
            Assert.Equal(
                "13.59",
                document.RootElement.GetProperty("exiftool_version").GetString());
            JsonElement[] files = document.RootElement
                .GetProperty("files")
                .EnumerateArray()
                .ToArray();
            Assert.NotEmpty(files);
            Assert.All(files, record =>
            {
                Assert.True(record.GetProperty("size").GetInt64() >= 0);
                Assert.Matches(
                    "^[0-9a-f]{64}$",
                    record.GetProperty("sha256").GetString()!);
            });
        }

        await command.ExecuteAsync(
            fixture.LockPath,
            fixture.TargetPath,
            fixture.ArchivePath,
            force: false,
            CancellationToken.None);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData(@"C:/drive.txt")]
    [InlineData(@"\\server/share.txt")]
    [InlineData(@"folder\backslash.txt")]
    public async Task Rejects_unsafe_archive_paths_without_polluting_target(
        string unsafePath)
    {
        using var temp = new TemporaryDirectory();
        FetchFixture fixture = ReleaseFixtures.CreateFetchFixture(
            temp,
            archive =>
            {
                ZipArchiveEntry entry = archive.CreateEntry(unsafePath);
                using StreamWriter writer = new(entry.Open());
                writer.Write("bad");
            });
        var command = new FetchExifToolCommand(
            new CopyingDownloader(fixture.ArchivePath),
            new FixedVersionProbe("13.59"));

        await Assert.ThrowsAsync<ReleaseToolException>(
            () => command.ExecuteAsync(
                fixture.LockPath,
                fixture.TargetPath,
                fixture.ArchivePath,
                force: false,
                CancellationToken.None));
        Assert.False(Directory.Exists(fixture.TargetPath));
    }

    [Fact]
    public async Task Rejects_symlink_and_case_collision_entries()
    {
        using var temp = new TemporaryDirectory();
        FetchFixture fixture = ReleaseFixtures.CreateFetchFixture(
            temp,
            archive =>
            {
                ZipArchiveEntry link = archive.CreateEntry("payload/link");
                link.ExternalAttributes = 0xA000 << 16;
                ZipArchiveEntry collision = archive.CreateEntry(
                    "exiftool-13.59_64/readme.TXT");
                using StreamWriter writer = new(collision.Open());
                writer.Write("collision");
            });
        var command = new FetchExifToolCommand(
            new CopyingDownloader(fixture.ArchivePath),
            new FixedVersionProbe("13.59"));

        await Assert.ThrowsAsync<ReleaseToolException>(
            () => command.ExecuteAsync(
                fixture.LockPath,
                fixture.TargetPath,
                fixture.ArchivePath,
                false,
                CancellationToken.None));
        Assert.False(Directory.Exists(fixture.TargetPath));
    }

    [Fact]
    public async Task Invalid_nonempty_runtime_requires_force_and_failed_force_restores_it()
    {
        using var temp = new TemporaryDirectory();
        FetchFixture fixture = ReleaseFixtures.CreateFetchFixture(temp);
        Directory.CreateDirectory(fixture.TargetPath);
        string ownerFile = System.IO.Path.Combine(fixture.TargetPath, "owner.txt");
        File.WriteAllText(ownerFile, "keep");
        var command = new FetchExifToolCommand(
            new CopyingDownloader(fixture.ArchivePath),
            new FixedVersionProbe("wrong"));

        await Assert.ThrowsAsync<ReleaseToolException>(
            () => command.ExecuteAsync(
                fixture.LockPath,
                fixture.TargetPath,
                fixture.ArchivePath,
                false,
                CancellationToken.None));
        Assert.Equal("keep", File.ReadAllText(ownerFile));

        await Assert.ThrowsAsync<ReleaseToolException>(
            () => command.ExecuteAsync(
                fixture.LockPath,
                fixture.TargetPath,
                fixture.ArchivePath,
                true,
                CancellationToken.None));
        Assert.Equal("keep", File.ReadAllText(ownerFile));
    }

    [Fact]
    public async Task Tampered_archive_hash_is_rejected_before_extraction()
    {
        using var temp = new TemporaryDirectory();
        FetchFixture fixture = ReleaseFixtures.CreateFetchFixture(temp);
        File.AppendAllText(fixture.ArchivePath, "tamper");
        var command = new FetchExifToolCommand(
            new CopyingDownloader(fixture.ArchivePath),
            new FixedVersionProbe("13.59"));

        await Assert.ThrowsAsync<ReleaseToolException>(
            () => command.ExecuteAsync(
                fixture.LockPath,
                fixture.TargetPath,
                fixture.ArchivePath,
                false,
                CancellationToken.None));

        Assert.False(Directory.Exists(fixture.TargetPath));
    }

    [Fact]
    public async Task Tracked_runtime_readme_placeholder_is_preserved()
    {
        using var temp = new TemporaryDirectory();
        FetchFixture fixture = ReleaseFixtures.CreateFetchFixture(temp);
        Directory.CreateDirectory(fixture.TargetPath);
        string readme = System.IO.Path.Combine(fixture.TargetPath, "README.md");
        File.WriteAllText(readme, "tracked placeholder");
        var command = new FetchExifToolCommand(
            new CopyingDownloader(fixture.ArchivePath),
            new FixedVersionProbe("13.59"));

        await command.ExecuteAsync(
            fixture.LockPath,
            fixture.TargetPath,
            fixture.ArchivePath,
            false,
            CancellationToken.None);

        Assert.Equal("tracked placeholder", File.ReadAllText(readme));
    }
}
