using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Emke.AiMarker.Integration.Tests.TestSupport;

namespace Emke.AiMarker.Integration.Tests;

public sealed partial class FixtureManifestTests
{
    private static readonly string[] ExpectedFiles =
    [
        "fixture.jpeg",
        "fixture.jpg",
        "fixture.mp4",
        "fixture.png",
    ];

    [Fact]
    public void Manifest_lists_only_the_four_controlled_media_files_in_stable_order()
    {
        string directory = RepositoryPaths.ControlledFixtures;
        using JsonDocument document = LoadManifest(directory);
        JsonElement root = document.RootElement;

        AssertObjectHasExactProperties(
            root,
            "schema_version",
            "generator_versions",
            "files");
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());

        JsonElement generators = root.GetProperty("generator_versions");
        AssertObjectHasExactProperties(generators, "ffmpeg", "exiftool");
        Assert.Equal("7.1.1", generators.GetProperty("ffmpeg").GetString());
        Assert.Equal("13.59", generators.GetProperty("exiftool").GetString());

        JsonElement.ArrayEnumerator records =
            root.GetProperty("files").EnumerateArray();
        string[] listed = records
            .Select(record => record.GetProperty("path").GetString()!)
            .ToArray();

        Assert.Equal(ExpectedFiles, listed);
        Assert.Equal(
            ExpectedFiles.Length,
            listed.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        string[] actualEntries = Directory
            .EnumerateFileSystemEntries(directory)
            .Select(entry => Path.GetFileName(entry)!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ExpectedFiles.Append("fixture-manifest.json")
                .Order(StringComparer.Ordinal),
            actualEntries);
    }

    [Fact]
    public void Manifest_hashes_lengths_commands_and_privacy_fields_match_real_files()
    {
        string directory = RepositoryPaths.ControlledFixtures;
        using JsonDocument document = LoadManifest(directory);

        foreach (JsonElement record in document.RootElement
                     .GetProperty("files")
                     .EnumerateArray())
        {
            AssertObjectHasExactProperties(
                record,
                "path",
                "byte_length",
                "sha256",
                "generation_commands");

            string relativePath = record.GetProperty("path").GetString()!;
            Assert.False(Path.IsPathFullyQualified(relativePath));
            Assert.Equal(Path.GetFileName(relativePath), relativePath);
            Assert.DoesNotContain("..", relativePath, StringComparison.Ordinal);

            string fullPath = Path.Combine(directory, relativePath);
            Assert.True(File.Exists(fullPath), $"Missing fixture: {relativePath}");
            Assert.Equal(
                new FileInfo(fullPath).Length,
                record.GetProperty("byte_length").GetInt64());
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath)))
                    .ToLowerInvariant(),
                record.GetProperty("sha256").GetString());

            string[] commands = record.GetProperty("generation_commands")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray();
            Assert.NotEmpty(commands);
            Assert.All(commands, command =>
            {
                Assert.False(string.IsNullOrWhiteSpace(command));
                Assert.DoesNotMatch(PrivateAbsolutePath(), command);
                Assert.DoesNotContain(
                    RepositoryPaths.Root,
                    command,
                    StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    private static JsonDocument LoadManifest(string directory)
    {
        string manifestPath = Path.Combine(directory, "fixture-manifest.json");
        Assert.True(File.Exists(manifestPath), "Controlled fixture manifest is missing.");
        return JsonDocument.Parse(File.ReadAllBytes(manifestPath));
    }

    private static void AssertObjectHasExactProperties(
        JsonElement element,
        params string[] expectedNames)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        string[] actualNames = element.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        Assert.Equal(
            expectedNames.Order(StringComparer.Ordinal),
            actualNames.Order(StringComparer.Ordinal));
        Assert.Equal(
            actualNames.Length,
            actualNames.Distinct(StringComparer.Ordinal).Count());
    }

    [GeneratedRegex(
        @"(?:^|[\s""'])(?:/Users/|/home/|[A-Za-z]:[\\/]|\\\\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex PrivateAbsolutePath();
}
