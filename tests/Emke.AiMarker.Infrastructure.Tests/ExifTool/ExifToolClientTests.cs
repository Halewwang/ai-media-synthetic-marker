using System.Text;
using Emke.AiMarker.Infrastructure.ExifTool;
using Emke.AiMarker.Infrastructure.Tests.TestSupport;

namespace Emke.AiMarker.Infrastructure.Tests.ExifTool;

public sealed class ExifToolClientTests
{
    private const string Executable = @"C:\app\exiftool\exiftool.exe";

    [Fact]
    public async Task Write_marker_uses_exact_append_and_no_backup_options()
    {
        var runner = new RecordingProcessRunner();
        var client = new ExifToolClient(Executable, runner);

        await client.WriteMarkerAsync(@"D:\中文 示例.mp4", CancellationToken.None);

        Assert.Equal(
            [
                "-overwrite_original",
                "-P",
                "-XMP-dc:Subject+=contains-synthetic-performer",
                @"D:\中文 示例.mp4",
            ],
            runner.LastArgumentFileLines);
        Assert.Equal(TimeSpan.FromMinutes(5), runner.LastTimeout);
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task Read_subjects_requests_only_explicit_xmp_dc_subject()
    {
        var runner = RecordingProcessRunner.WithStdout(
            """[{"XMP-dc:Subject":["existing","contains-synthetic-performer"]}]""");
        var client = new ExifToolClient(Executable, runner);

        IReadOnlyList<string> result =
            await client.ReadSubjectsAsync(@"D:\image.jpg", CancellationToken.None);

        Assert.Equal(["existing", "contains-synthetic-performer"], result);
        Assert.Equal(
            [
                "-j",
                "-struct",
                "-G1",
                "-s",
                "-XMP-dc:Subject",
                @"D:\image.jpg",
            ],
            runner.LastArgumentFileLines);
        Assert.DoesNotContain(
            "Microsoft:Category",
            string.Join('\n', runner.LastArgumentFileLines),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""[{"XMP-dc:Subject":"existing"}]""", "existing")]
    [InlineData("""[{}]""")]
    [InlineData("""[{"XMP-dc:Subject":null}]""")]
    public async Task Read_subjects_accepts_scalar_or_missing_subject_json(
        string json,
        params string[] expected)
    {
        var runner = RecordingProcessRunner.WithStdout(json);
        var client = new ExifToolClient(Executable, runner);

        IReadOnlyList<string> result =
            await client.ReadSubjectsAsync(@"D:\image.jpg", CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("not-json")]
    public async Task Read_subjects_rejects_invalid_metadata_json(string json)
    {
        var runner = RecordingProcessRunner.WithStdout(json);
        var client = new ExifToolClient(Executable, runner);

        MarkerOperationException exception = await Assert.ThrowsAsync<MarkerOperationException>(
            () => client.ReadSubjectsAsync(@"D:\image.jpg", CancellationToken.None));

        Assert.Contains("元数据", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_raw_xmp_preserves_stdout_bytes_exactly()
    {
        byte[] rawXmp = [0xEF, 0xBB, 0xBF, 0x00, 0x80, 0xFF, 0x0A];
        var runner = RecordingProcessRunner.WithStdout(rawXmp);
        var client = new ExifToolClient(Executable, runner);

        ReadOnlyMemory<byte> result =
            await client.ReadRawXmpAsync(@"D:\中文 图像.png", CancellationToken.None);

        Assert.Equal(rawXmp, result.ToArray());
        Assert.Equal(
            ["-q", "-q", "-b", "-XMP", @"D:\中文 图像.png"],
            runner.LastArgumentFileLines);
        Assert.Equal(TimeSpan.FromMinutes(5), runner.LastTimeout);
    }

    [Fact]
    public async Task Read_image_data_hash_uses_request_all_and_trims_utf8_output()
    {
        var runner = RecordingProcessRunner.WithStdout("\uFEFFabc123\r\n");
        var client = new ExifToolClient(Executable, runner);

        string result = await client.ReadImageDataHashAsync(
            @"D:\中文 视频.mp4",
            CancellationToken.None);

        Assert.Equal("abc123", result);
        Assert.Equal(
            [
                "-q",
                "-q",
                "-api",
                "RequestAll=3",
                "-s3",
                "-ImageDataHash",
                @"D:\中文 视频.mp4",
            ],
            runner.LastArgumentFileLines);
    }

    [Fact]
    public async Task Get_version_uses_short_timeout_and_exact_version_argument()
    {
        var cancellation = new CancellationTokenSource();
        var runner = RecordingProcessRunner.WithStdout("\uFEFF 13.59 \r\n");
        var client = new ExifToolClient(Executable, runner);

        string result = await client.GetVersionAsync(cancellation.Token);

        Assert.Equal("13.59", result);
        Assert.Equal(["-ver"], runner.LastArgumentFileLines);
        Assert.Equal(TimeSpan.FromSeconds(30), runner.LastTimeout);
        Assert.Equal(Executable, runner.LastExecutable);
        Assert.Equal(cancellation.Token, runner.LastCancellationToken);
    }

    [Theory]
    [InlineData("stderr detail", "stdout detail", "stderr detail")]
    [InlineData("  ", "stdout detail", "stdout detail")]
    [InlineData("", "", "退出码 23")]
    public async Task Nonzero_exit_prefers_stderr_then_stdout_then_exit_code(
        string stderr,
        string stdout,
        string expected)
    {
        var runner = RecordingProcessRunner.WithResult(
            new ProcessExecutionResult(
                23,
                Encoding.UTF8.GetBytes(stdout),
                Encoding.UTF8.GetBytes(stderr)));
        var client = new ExifToolClient(Executable, runner);

        MarkerOperationException exception = await Assert.ThrowsAsync<MarkerOperationException>(
            () => client.WriteMarkerAsync(@"D:\image.jpg", CancellationToken.None));

        Assert.Equal(expected, exception.Message);
    }
}
