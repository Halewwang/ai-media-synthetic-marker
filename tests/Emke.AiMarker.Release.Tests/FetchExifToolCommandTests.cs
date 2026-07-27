using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Emke.AiMarker.Release.Commands;
using Emke.AiMarker.Release.Packaging;
using Emke.AiMarker.Release.Tests.TestSupport;

namespace Emke.AiMarker.Release.Tests;

public sealed class FetchExifToolCommandTests
{
    [Fact]
    public async Task Downloader_follows_only_trusted_https_sourceforge_redirects()
    {
        using var temp = new TemporaryDirectory();
        var initial = new Uri(
            "https://downloads.sourceforge.net/project/exiftool/exiftool-13.59_64.zip");
        var mirror = new Uri(
            "https://zenlayer.dl.sourceforge.net/project/exiftool/exiftool-13.59_64.zip");
        var handler = new SequenceHttpHandler(
            request =>
            {
                Assert.Equal(initial, request.RequestUri);
                return Redirect(HttpStatusCode.Found, mirror);
            },
            request =>
            {
                Assert.Equal(mirror, request.RequestUri);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.ASCII.GetBytes("archive")),
                };
            });
        using var client = new HttpClient(handler);
        using var downloader = new HttpArchiveDownloader(client);
        string destination = System.IO.Path.Combine(temp.Path, "archive.zip");

        await downloader.DownloadAsync(initial, destination, CancellationToken.None);

        Assert.Equal("archive", File.ReadAllText(destination));
        Assert.Equal(2, handler.RequestCount);
    }

    [Theory]
    [InlineData("http://downloads.sourceforge.net/project/exiftool/archive.zip")]
    [InlineData("https://evil.invalid/project/exiftool/archive.zip")]
    [InlineData("https://user@mirror.sourceforge.net/project/exiftool/archive.zip")]
    [InlineData("https://mirror.sourceforge.net:444/project/exiftool/archive.zip")]
    public async Task Downloader_rejects_untrusted_redirect_targets(string redirect)
    {
        using var temp = new TemporaryDirectory();
        var initial = new Uri(
            "https://downloads.sourceforge.net/project/exiftool/exiftool-13.59_64.zip");
        var handler = new SequenceHttpHandler(
            _ => Redirect(HttpStatusCode.Found, new Uri(redirect)));
        using var client = new HttpClient(handler);
        using var downloader = new HttpArchiveDownloader(client);

        await Assert.ThrowsAsync<ReleaseToolException>(
            () => downloader.DownloadAsync(
                initial,
                System.IO.Path.Combine(temp.Path, "archive.zip"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Downloader_rejects_redirect_loops()
    {
        using var temp = new TemporaryDirectory();
        var initial = new Uri(
            "https://downloads.sourceforge.net/project/exiftool/exiftool-13.59_64.zip");
        var handler = new SequenceHttpHandler(
            _ => Redirect(HttpStatusCode.Found, initial),
            _ => Redirect(HttpStatusCode.Found, initial));
        using var client = new HttpClient(handler);
        using var downloader = new HttpArchiveDownloader(client);

        await Assert.ThrowsAsync<ReleaseToolException>(
            () => downloader.DownloadAsync(
                initial,
                System.IO.Path.Combine(temp.Path, "archive.zip"),
                CancellationToken.None));
    }

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
    [InlineData("file.txt:private")]
    [InlineData("CON.txt")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
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

    [Theory]
    [InlineData("extra")]
    [InlineData("duplicate")]
    public async Task Rejects_unknown_or_duplicate_lock_properties(string mutation)
    {
        using var temp = new TemporaryDirectory();
        FetchFixture fixture = ReleaseFixtures.CreateFetchFixture(temp);
        string json = File.ReadAllText(fixture.LockPath).TrimEnd();
        json = json[..^1] + (mutation == "extra"
            ? ",\"unexpected\":true}"
            : ",\"version\":\"13.59\"}");
        File.WriteAllText(fixture.LockPath, json);
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
    }

    [Theory]
    [InlineData("root-extra")]
    [InlineData("root-duplicate")]
    [InlineData("record-extra")]
    [InlineData("record-duplicate")]
    public async Task Rejects_unknown_or_duplicate_runtime_manifest_properties(
        string mutation)
    {
        using var temp = new TemporaryDirectory();
        FetchFixture fixture = ReleaseFixtures.CreateFetchFixture(temp);
        var command = new FetchExifToolCommand(
            new CopyingDownloader(fixture.ArchivePath),
            new FixedVersionProbe("13.59"));
        await command.ExecuteAsync(
            fixture.LockPath,
            fixture.TargetPath,
            fixture.ArchivePath,
            false,
            CancellationToken.None);
        string manifest = System.IO.Path.Combine(
            fixture.TargetPath,
            "exiftool-manifest.json");
        string json = File.ReadAllText(manifest).TrimEnd();
        json = mutation switch
        {
            "root-extra" => json[..^1] + ",\"unexpected\":true}",
            "root-duplicate" => json[..^1] + ",\"schema_version\":1}",
            "record-extra" => json.Replace(
                "\"path\":",
                "\"unexpected\": true,\n      \"path\":",
                StringComparison.Ordinal),
            "record-duplicate" => Regex.Replace(
                json,
                "(\"path\"\\s*:\\s*\"[^\"]+\")",
                "$1,\n      $1",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            _ => throw new InvalidOperationException(),
        };
        File.WriteAllText(manifest, json);

        await Assert.ThrowsAsync<ReleaseToolException>(
            () => command.ExecuteAsync(
                fixture.LockPath,
                fixture.TargetPath,
                fixture.ArchivePath,
                false,
                CancellationToken.None));
    }

    [Theory]
    [InlineData("file.txt:private")]
    [InlineData("CON.txt")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    public async Task Runtime_manifest_rejects_nonportable_windows_paths(
        string unsafePath)
    {
        using var temp = new TemporaryDirectory();
        FetchFixture fixture = ReleaseFixtures.CreateFetchFixture(temp);
        var command = new FetchExifToolCommand(
            new CopyingDownloader(fixture.ArchivePath),
            new FixedVersionProbe("13.59"));
        await command.ExecuteAsync(
            fixture.LockPath,
            fixture.TargetPath,
            fixture.ArchivePath,
            false,
            CancellationToken.None);
        string manifest = System.IO.Path.Combine(
            fixture.TargetPath,
            "exiftool-manifest.json");
        string json = File.ReadAllText(manifest);
        json = Regex.Replace(
            json,
            "(\"path\"\\s*:\\s*\")[^\"]+",
            $"$1{unsafePath}",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        File.WriteAllText(manifest, json);

        MethodInfo validationMethod = typeof(FetchExifToolCommand)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method =>
                method.Name == "ValidateInstallationAsync"
                && method.GetParameters().Length == 4
                && method.GetParameters()[1].ParameterType == typeof(string));
        var validation = (Task)validationMethod.Invoke(
            null,
            [
                fixture.TargetPath,
                fixture.LockPath,
                new FixedVersionProbe("13.59"),
                CancellationToken.None,
            ])!;

        ReleaseToolException error = await Assert.ThrowsAsync<ReleaseToolException>(
            () => validation);
        Assert.Contains("不安全路径", error.Message, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Redirect(
        HttpStatusCode status,
        Uri location)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = location;
        return response;
    }

    private sealed class SequenceHttpHandler(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int index = Interlocked.Increment(ref requestCount) - 1;
            HttpResponseMessage response = responses[
                Math.Min(index, responses.Length - 1)](request);
            return Task.FromResult(response);
        }
    }
}
