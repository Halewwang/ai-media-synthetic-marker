using Emke.AiMarker.Release.Packaging;
using Emke.AiMarker.Release.Tests.TestSupport;
using System.Text;

namespace Emke.AiMarker.Release.Tests;

public sealed class ReleaseStageValidatorTests
{
    public static TheoryData<string> ForbiddenPaths => new()
    {
        "private.jpg",
        "private.JPEG",
        "private.png",
        "private.MP4",
        "验证结果.csv",
        "media.mp4_original",
        "app.py",
        "app.pyc",
        "__pycache__/cache",
        ".gitkeep",
        "unexpected.md",
    };

    [Theory]
    [MemberData(nameof(ForbiddenPaths))]
    public void Rejects_forbidden_release_content(string relativePath)
    {
        using var temp = new TemporaryDirectory();
        string manifest = ReleaseFixtures.CreateManifest(temp);
        string stage = ReleaseFixtures.CreateValidStage(temp);
        ReleaseFixtures.Write(stage, relativePath);

        Assert.Throws<ReleaseToolException>(
            () => ReleaseStageValidator.Validate(stage, manifest));
    }

    [Theory]
    [InlineData(@"C:\Users\maintainer\source")]
    [InlineData(@"\\server\share\private")]
    [InlineData("/Users/maintainer/source")]
    [InlineData("/home/maintainer/source")]
    public void Rejects_absolute_paths_in_text(string absolutePath)
    {
        using var temp = new TemporaryDirectory();
        string manifest = ReleaseFixtures.CreateManifest(temp);
        string stage = ReleaseFixtures.CreateValidStage(temp);
        File.WriteAllText(
            System.IO.Path.Combine(stage, "使用说明.txt"),
            $"local={absolutePath}");

        Assert.Throws<ReleaseToolException>(
            () => ReleaseStageValidator.Validate(stage, manifest));
    }

    [Fact]
    public void Locked_exiftool_vendor_document_may_keep_upstream_path_examples()
    {
        using var temp = new TemporaryDirectory();
        string manifest = ReleaseFixtures.CreateManifest(temp);
        string stage = ReleaseFixtures.CreateValidStage(temp);
        ReleaseFixtures.Write(
            stage,
            "exiftool/exiftool_files/windows_exiftool.txt",
            @"upstream example: C:\WINDOWS\exiftool.exe");

        ReleaseStageValidator.Validate(stage, manifest);
    }

    [Fact]
    public void Adjacent_exiftool_text_is_not_exempt_from_privacy_scanning()
    {
        using var temp = new TemporaryDirectory();
        string manifest = ReleaseFixtures.CreateManifest(temp);
        string stage = ReleaseFixtures.CreateValidStage(temp);
        ReleaseFixtures.Write(
            stage,
            "exiftool/exiftool_files/private.txt",
            @"local=C:\Users\maintainer\source");

        Assert.Throws<ReleaseToolException>(
            () => ReleaseStageValidator.Validate(stage, manifest));
    }

    [Theory]
    [InlineData("utf16le-no-bom")]
    [InlineData("utf16be-no-bom")]
    [InlineData("utf16le-bom")]
    [InlineData("utf16be-bom")]
    [InlineData("utf32le-bom")]
    [InlineData("utf32be-bom")]
    public void Rejects_absolute_paths_in_supported_or_suspicious_unicode_text(
        string encodingName)
    {
        using var temp = new TemporaryDirectory();
        string manifest = ReleaseFixtures.CreateManifest(temp);
        string stage = ReleaseFixtures.CreateValidStage(temp);
        string path = System.IO.Path.Combine(stage, "使用说明.txt");
        const string content = @"local=C:\Users\private\source";

        Encoding encoding = encodingName switch
        {
            "utf16le-no-bom" or "utf16le-bom" =>
                new UnicodeEncoding(false, encodingName is "utf16le-bom", true),
            "utf16be-no-bom" or "utf16be-bom" =>
                new UnicodeEncoding(true, encodingName is "utf16be-bom", true),
            "utf32le-bom" => new UTF32Encoding(false, true, true),
            "utf32be-bom" => new UTF32Encoding(true, true, true),
            _ => throw new InvalidOperationException(),
        };
        byte[] bytes = encoding.GetPreamble().Concat(
            encoding.GetBytes(content)).ToArray();
        File.WriteAllBytes(path, bytes);

        Assert.Throws<ReleaseToolException>(
            () => ReleaseStageValidator.Validate(stage, manifest));
    }

    [Fact]
    public void Rejects_oversized_text_instead_of_skipping_absolute_path_scan()
    {
        using var temp = new TemporaryDirectory();
        string manifest = ReleaseFixtures.CreateManifest(temp);
        string stage = ReleaseFixtures.CreateValidStage(temp);
        string path = System.IO.Path.Combine(stage, "使用说明.txt");
        string content = new('a', (2 * 1024 * 1024) + 1);
        File.WriteAllText(path, content + @" C:\Users\private\source");

        Assert.Throws<ReleaseToolException>(
            () => ReleaseStageValidator.Validate(stage, manifest));
    }

    [Fact]
    public void Requires_every_manifest_path_and_accepts_clean_stage()
    {
        using var temp = new TemporaryDirectory();
        string manifest = ReleaseFixtures.CreateManifest(temp);
        string stage = ReleaseFixtures.CreateValidStage(temp);

        ReleaseStageValidator.Validate(stage, manifest);

        File.Delete(System.IO.Path.Combine(stage, "EMKE AI Marker.exe"));
        Assert.Throws<ReleaseToolException>(
            () => ReleaseStageValidator.Validate(stage, manifest));
    }

    [Fact]
    public void Rejects_case_and_unicode_normalization_collisions()
    {
        Assert.Throws<ReleaseToolException>(
            () => ReleaseStageValidator.ValidatePortablePathSet(
                ["data/A.txt", "data/a.TXT"]));
        Assert.Throws<ReleaseToolException>(
            () => ReleaseStageValidator.ValidatePortablePathSet(
                ["data/café.bin", "data/cafe\u0301.bin"]));
    }

    [Theory]
    [InlineData("file.txt:private")]
    [InlineData("CON.txt")]
    [InlineData("data/aux.json")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("control\u0001.txt")]
    public void Rejects_nonportable_windows_path_segments(string relativePath)
    {
        Assert.Throws<ReleaseToolException>(
            () => ReleaseStageValidator.ValidatePortablePathSet([relativePath]));
    }

    [Fact]
    public void Required_example_output_directory_must_be_empty()
    {
        using var temp = new TemporaryDirectory();
        string manifest = ReleaseFixtures.CreateManifest(temp);
        string stage = ReleaseFixtures.CreateValidStage(temp);
        ReleaseFixtures.Write(stage, "示例输出/EMKE 已标记/unexpected.bin");

        Assert.Throws<ReleaseToolException>(
            () => ReleaseStageValidator.Validate(stage, manifest));
    }

    [Fact]
    public void Rejects_symbolic_links_without_following_them()
    {
        using var temp = new TemporaryDirectory();
        string manifest = ReleaseFixtures.CreateManifest(temp);
        string stage = ReleaseFixtures.CreateValidStage(temp);
        string outside = temp.CreateFile("outside.bin");
        string link = System.IO.Path.Combine(stage, "linked.bin");
        File.CreateSymbolicLink(link, outside);

        Assert.Throws<ReleaseToolException>(
            () => ReleaseStageValidator.Validate(stage, manifest));
        Assert.Equal("fixture", File.ReadAllText(outside));
    }
}
