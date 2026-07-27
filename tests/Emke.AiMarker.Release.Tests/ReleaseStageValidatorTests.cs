using Emke.AiMarker.Release.Packaging;
using Emke.AiMarker.Release.Tests.TestSupport;

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
