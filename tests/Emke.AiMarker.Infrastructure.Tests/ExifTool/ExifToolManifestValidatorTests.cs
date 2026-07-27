using System.Security.Cryptography;
using System.Text.Json;
using Emke.AiMarker.Infrastructure.ExifTool;

namespace Emke.AiMarker.Infrastructure.Tests.ExifTool;

public sealed class ExifToolManifestValidatorTests : IDisposable
{
    private readonly string _temporaryRoot =
        Path.Combine(Path.GetTempPath(), $"emke-exiftool-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Validate_accepts_matching_locked_runtime()
    {
        RuntimeFixture fixture = CreateValidFixture();

        ExifToolManifestValidator.Validate(fixture.RuntimeRoot, fixture.LockPath);
    }

    [Fact]
    public void Validate_rejects_tampered_perl_payload()
    {
        RuntimeFixture fixture = CreateValidFixture();
        File.WriteAllBytes(
            Path.Combine(fixture.RuntimeRoot, "exiftool_files", "perl.exe"),
            "perM"u8.ToArray());

        ExifToolIntegrityException exception =
            Assert.Throws<ExifToolIntegrityException>(
                () => ExifToolManifestValidator.Validate(
                    fixture.RuntimeRoot,
                    fixture.LockPath));

        Assert.Contains("perl.exe", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_missing_manifest_payload()
    {
        RuntimeFixture fixture = CreateValidFixture();
        File.Delete(Path.Combine(fixture.RuntimeRoot, "README.txt"));

        ExifToolIntegrityException exception =
            Assert.Throws<ExifToolIntegrityException>(
                () => ExifToolManifestValidator.Validate(
                    fixture.RuntimeRoot,
                    fixture.LockPath));

        Assert.Contains("README.txt", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_rejects_unexpected_payload()
    {
        RuntimeFixture fixture = CreateValidFixture();
        File.WriteAllBytes(
            Path.Combine(fixture.RuntimeRoot, "exiftool_files", "unexpected.dll"),
            [0x01]);

        ExifToolIntegrityException exception =
            Assert.Throws<ExifToolIntegrityException>(
                () => ExifToolManifestValidator.Validate(
                    fixture.RuntimeRoot,
                    fixture.LockPath));

        Assert.Contains("unexpected.dll", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("清单", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_requires_exiftool_license_payloads()
    {
        RuntimeFixture fixture = CreateValidFixture(
            omittedRelativePath: "exiftool_files/Licenses_Strawberry_Perl.zip");

        ExifToolIntegrityException exception =
            Assert.Throws<ExifToolIntegrityException>(
                () => ExifToolManifestValidator.Validate(
                    fixture.RuntimeRoot,
                    fixture.LockPath));

        Assert.Contains(
            "Licenses_Strawberry_Perl.zip",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_rejects_payload_reparse_point()
    {
        RuntimeFixture fixture = CreateValidFixture();
        string target = Path.Combine(_temporaryRoot, "outside-perl.exe");
        File.WriteAllBytes(target, "perl"u8.ToArray());
        string perl = Path.Combine(fixture.RuntimeRoot, "exiftool_files", "perl.exe");
        File.Delete(perl);
        File.CreateSymbolicLink(perl, target);

        ExifToolIntegrityException exception =
            Assert.Throws<ExifToolIntegrityException>(
                () => ExifToolManifestValidator.Validate(
                    fixture.RuntimeRoot,
                    fixture.LockPath));

        Assert.Contains("重解析", exception.Message, StringComparison.Ordinal);
        Assert.Contains("perl.exe", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_rejects_reparse_directory_before_traversal()
    {
        RuntimeFixture fixture = CreateValidFixture();
        string realDirectory = Path.Combine(_temporaryRoot, "outside-files");
        Directory.CreateDirectory(realDirectory);
        string filesDirectory = Path.Combine(fixture.RuntimeRoot, "exiftool_files");
        Directory.Delete(filesDirectory, recursive: true);
        Directory.CreateSymbolicLink(filesDirectory, realDirectory);

        ExifToolIntegrityException exception =
            Assert.Throws<ExifToolIntegrityException>(
                () => ExifToolManifestValidator.Validate(
                    fixture.RuntimeRoot,
                    fixture.LockPath));

        Assert.Contains("重解析", exception.Message, StringComparison.Ordinal);
        Assert.Contains("exiftool_files", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("13.58", 123L, "abababababababababababababababababababababababababababababababab")]
    [InlineData("13.59", 0L, "abababababababababababababababababababababababababababababababab")]
    [InlineData("13.59", 123L, "not-a-sha256")]
    public void Validate_rejects_invalid_lock_contract(
        string version,
        long archiveSize,
        string archiveSha256)
    {
        RuntimeFixture fixture = CreateValidFixture();
        WriteJson(
            fixture.LockPath,
            new
            {
                version,
                platform = "windows-x64",
                archive_name = "exiftool-13.59_64.zip",
                url = "https://example.invalid/exiftool.zip",
                size = archiveSize,
                sha256 = archiveSha256,
            });

        ExifToolIntegrityException exception =
            Assert.Throws<ExifToolIntegrityException>(
                () => ExifToolManifestValidator.Validate(
                    fixture.RuntimeRoot,
                    fixture.LockPath));

        Assert.Contains("锁定文件", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("schema_version")]
    [InlineData("exiftool_version")]
    [InlineData("archive_name")]
    [InlineData("archive_size")]
    [InlineData("archive_sha256")]
    public void Validate_rejects_manifest_metadata_mismatch(string property)
    {
        RuntimeFixture fixture = CreateValidFixture();
        string manifestPath = Path.Combine(
            fixture.RuntimeRoot,
            "exiftool-manifest.json");
        using JsonDocument original = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        Dictionary<string, object?> manifest = original.RootElement
            .EnumerateObject()
            .ToDictionary(
                item => item.Name,
                item => (object?)item.Value.Clone(),
                StringComparer.Ordinal);
        manifest[property] = property switch
        {
            "schema_version" => 2,
            "archive_size" => 124,
            _ => "mismatch",
        };
        WriteJson(manifestPath, manifest);

        ExifToolIntegrityException exception =
            Assert.Throws<ExifToolIntegrityException>(
                () => ExifToolManifestValidator.Validate(
                    fixture.RuntimeRoot,
                    fixture.LockPath));

        Assert.Contains("manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("/absolute.exe")]
    [InlineData("C:/absolute.exe")]
    [InlineData("exiftool_files\\perl.exe")]
    [InlineData("exiftool_files/perl.exe:alternate-stream")]
    public void Validate_rejects_unsafe_manifest_paths(string unsafePath)
    {
        RuntimeFixture fixture = CreateValidFixture();
        string manifestPath = Path.Combine(
            fixture.RuntimeRoot,
            "exiftool-manifest.json");
        using JsonDocument original = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var records = original.RootElement.GetProperty("files")
            .EnumerateArray()
            .Select(item => new Dictionary<string, object?>
            {
                ["path"] = item.GetProperty("path").GetString(),
                ["size"] = item.GetProperty("size").GetInt64(),
                ["sha256"] = item.GetProperty("sha256").GetString(),
            })
            .ToList();
        records[0]["path"] = unsafePath;
        WriteManifest(manifestPath, records);

        ExifToolIntegrityException exception =
            Assert.Throws<ExifToolIntegrityException>(
                () => ExifToolManifestValidator.Validate(
                    fixture.RuntimeRoot,
                    fixture.LockPath));

        Assert.Contains("路径", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }
    }

    private RuntimeFixture CreateValidFixture(string? omittedRelativePath = null)
    {
        string runtimeRoot = Path.Combine(_temporaryRoot, Guid.NewGuid().ToString("N"));
        string lockPath = Path.Combine(runtimeRoot, "..", $"{Guid.NewGuid():N}.lock.json");
        Directory.CreateDirectory(Path.Combine(runtimeRoot, "exiftool_files"));

        var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["exiftool.exe"] = "launcher"u8.ToArray(),
            ["README.txt"] = "readme"u8.ToArray(),
            ["exiftool_files/LICENSE"] = "license"u8.ToArray(),
            ["exiftool_files/Licenses_Strawberry_Perl.zip"] = [0x50, 0x4B],
            ["exiftool_files/perl.exe"] = "perl"u8.ToArray(),
            ["exiftool_files/readme_windows.txt"] = "windows readme"u8.ToArray(),
        };

        foreach ((string relativePath, byte[] content) in payload)
        {
            if (string.Equals(relativePath, omittedRelativePath, StringComparison.Ordinal))
            {
                continue;
            }

            string fullPath = Path.Combine(
                runtimeRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, content);
        }

        WriteJson(
            lockPath,
            new
            {
                version = "13.59",
                platform = "windows-x64",
                archive_name = "exiftool-13.59_64.zip",
                url = "https://example.invalid/exiftool.zip",
                size = 123,
                sha256 = "ab" + new string('0', 62),
            });

        var records = payload
            .Where(item =>
                !string.Equals(item.Key, omittedRelativePath, StringComparison.Ordinal))
            .Select(item => new Dictionary<string, object?>
            {
                ["path"] = item.Key,
                ["size"] = item.Value.LongLength,
                ["sha256"] = Convert.ToHexString(
                    SHA256.HashData(item.Value)).ToLowerInvariant(),
            })
            .OrderBy(item => (string)item["path"]!, StringComparer.OrdinalIgnoreCase)
            .ToList();
        WriteManifest(
            Path.Combine(runtimeRoot, "exiftool-manifest.json"),
            records);

        return new RuntimeFixture(runtimeRoot, Path.GetFullPath(lockPath));
    }

    private static void WriteManifest(
        string manifestPath,
        IReadOnlyList<Dictionary<string, object?>> files) =>
        WriteJson(
            manifestPath,
            new
            {
                schema_version = 1,
                exiftool_version = "13.59",
                archive_name = "exiftool-13.59_64.zip",
                archive_size = 123,
                archive_sha256 = "ab" + new string('0', 62),
                files,
            });

    private static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
    }

    private sealed record RuntimeFixture(string RuntimeRoot, string LockPath);
}
