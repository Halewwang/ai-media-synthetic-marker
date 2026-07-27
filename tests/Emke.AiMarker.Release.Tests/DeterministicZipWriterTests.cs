using System.IO.Compression;
using System.Security.Cryptography;
using Emke.AiMarker.Release.Packaging;
using Emke.AiMarker.Release.Tests.TestSupport;

namespace Emke.AiMarker.Release.Tests;

public sealed class DeterministicZipWriterTests(ITestOutputHelper output)
{
    [Fact]
    public void Same_input_and_epoch_produce_identical_bytes_with_one_ascii_root()
    {
        using var temp = new TemporaryDirectory();
        string stage = ReleaseFixtures.CreateValidStage(temp);
        string first = System.IO.Path.Combine(temp.Path, "first.zip");
        string second = System.IO.Path.Combine(temp.Path, "second.zip");

        DeterministicZipWriter.Write(
            stage,
            first,
            "emke-ai-marker-v2.0.0-windows-x64",
            1_700_000_000);
        DeterministicZipWriter.Write(
            stage,
            second,
            "emke-ai-marker-v2.0.0-windows-x64",
            1_700_000_000);

        string firstHash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(first))).ToLowerInvariant();
        string secondHash = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(second))).ToLowerInvariant();
        output.WriteLine($"first={firstHash}");
        output.WriteLine($"second={secondHash}");
        Assert.Equal(firstHash, secondHash);
        using var archive = ZipFile.OpenRead(first);
        string[] names = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.All(
            names,
            name => Assert.StartsWith(
                "emke-ai-marker-v2.0.0-windows-x64/",
                name,
                StringComparison.Ordinal));
        Assert.Contains(
            "emke-ai-marker-v2.0.0-windows-x64/示例输出/EMKE 已标记/",
            names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    [InlineData("999999999999999")]
    public void Invalid_source_date_epoch_fails_explicitly(string value)
    {
        Assert.Throws<ReleaseToolException>(
            () => DeterministicZipWriter.ResolveEpoch(value));
    }

    [Fact]
    public void Unset_source_date_epoch_uses_fixed_fallback()
    {
        Assert.Equal(
            1_700_000_000,
            DeterministicZipWriter.ResolveEpoch(null));
    }

    [Fact]
    public void Rejects_non_ascii_or_traversing_root_name()
    {
        using var temp = new TemporaryDirectory();
        string stage = ReleaseFixtures.CreateValidStage(temp);
        string output = System.IO.Path.Combine(temp.Path, "bad.zip");

        Assert.Throws<ReleaseToolException>(
            () => DeterministicZipWriter.Write(stage, output, "../bad", 1_700_000_000));
        Assert.Throws<ReleaseToolException>(
            () => DeterministicZipWriter.Write(stage, output, "中文", 1_700_000_000));
    }

    [Fact]
    public void Rejects_zip_output_inside_input_stage()
    {
        using var temp = new TemporaryDirectory();
        string stage = ReleaseFixtures.CreateValidStage(temp);
        string output = System.IO.Path.Combine(stage, "release.zip");

        Assert.Throws<ReleaseToolException>(
            () => DeterministicZipWriter.Write(
                stage,
                output,
                "emke-ai-marker-v2.0.0-windows-x64",
                1_700_000_000));
        Assert.False(File.Exists(output));
    }
}
