using System.Text;
using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Infrastructure.Logging;
using Emke.AiMarker.Infrastructure.Tests.TestSupport;

namespace Emke.AiMarker.Infrastructure.Tests.Logging;

public sealed class CsvRunLogWriterTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public CsvRunLogWriterTests() => Directory.CreateDirectory(_temp);

    [Fact]
    public async Task Csv_has_utf8_bom_eleven_columns_and_neutralized_formulas()
    {
        ProcessResult result = TestResult(
            relativePath: "=HYPERLINK(\"https://example.invalid\")",
            error: "+危险公式");

        string path = await new CsvRunLogWriter().WriteAsync(
            _temp,
            RunMode.MarkCopies,
            [result],
            TestContext.Current.CancellationToken);

        byte[] raw = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal<byte>([0xEF, 0xBB, 0xBF], raw[..3]);
        string[] rows = await File.ReadAllLinesAsync(
            path,
            Encoding.UTF8,
            TestContext.Current.CancellationToken);
        Assert.Equal(11, CsvTestParser.Parse(rows[0]).Count);
        Assert.StartsWith("'=", CsvTestParser.Parse(rows[1])[0]);
        Assert.StartsWith("'+", CsvTestParser.Parse(rows[1])[10]);
    }

    [Fact]
    public async Task Csv_uses_exact_headers_and_maps_mode_status_and_evidence_fields()
    {
        ProcessResult result = new(
            "资料/春季,look.jpg",
            "JPG",
            ProcessStatus.OutputAlreadyCompliant,
            RunMode.MarkCopies,
            new VerificationEvidence(
                VerificationResult.Passed,
                "[\"contains-synthetic-performer\"]",
                "已确认 rdf:Bag/rdf:li",
                DateTimeOffset.Parse("2026-07-27T08:00:00+00:00"),
                "13.59",
                "来自证据的错误"));

        string path = await new CsvRunLogWriter().WriteAsync(
            _temp,
            RunMode.VerifyOnly,
            [result],
            TestContext.Current.CancellationToken);

        string[] rows = await File.ReadAllLinesAsync(
            path,
            Encoding.UTF8,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            ["相对路径", "格式", "运行模式", "处理状态", "验证结果", "验证字段", "实际读取值", "XMP结构", "验证时间", "ExifTool版本", "错误原因"],
            CsvTestParser.Parse(rows[0]));
        Assert.Equal(
            ["资料/春季,look.jpg", "JPG", "只读验证", "输出已存在且合规", "通过", "XMP-dc:Subject", "[\"contains-synthetic-performer\"]", "已确认 rdf:Bag/rdf:li", "2026-07-27T08:00:00.0000000+00:00", "13.59", "来自证据的错误"],
            CsvTestParser.Parse(rows[1]));
    }

    [Theory]
    [InlineData(RunMode.MarkCopies, "创建副本并验证")]
    [InlineData(RunMode.MarkOriginals, "直接修改原件并验证")]
    [InlineData(RunMode.VerifyOnly, "只读验证")]
    public async Task Csv_maps_every_run_mode(RunMode mode, string expected)
    {
        string path = await new CsvRunLogWriter().WriteAsync(
            _temp,
            mode,
            [TestResult("mode.jpg", "")],
            TestContext.Current.CancellationToken);

        string[] rows = await File.ReadAllLinesAsync(
            path,
            Encoding.UTF8,
            TestContext.Current.CancellationToken);
        Assert.Equal(expected, CsvTestParser.Parse(rows[1])[2]);
    }

    [Theory]
    [InlineData(ProcessStatus.Added, VerificationResult.Passed, "新增", "通过")]
    [InlineData(ProcessStatus.AlreadyCompliant, VerificationResult.Passed, "原本已合规", "通过")]
    [InlineData(ProcessStatus.OutputAlreadyCompliant, VerificationResult.Passed, "输出已存在且合规", "通过")]
    [InlineData(ProcessStatus.Unmarked, VerificationResult.Unmarked, "未标记", "未标记")]
    [InlineData(ProcessStatus.Failed, VerificationResult.Failed, "失败", "失败")]
    [InlineData(ProcessStatus.Skipped, VerificationResult.NotRun, "跳过", "未执行")]
    [InlineData(ProcessStatus.StoppedBeforeProcessing, VerificationResult.NotRun, "用户停止前未处理", "未执行")]
    public async Task Csv_maps_every_processing_status_and_verification_result(
        ProcessStatus status,
        VerificationResult verification,
        string expectedStatus,
        string expectedVerification)
    {
        ProcessResult result = new(
            "mapping.jpg",
            "JPG",
            status,
            RunMode.MarkCopies,
            new VerificationEvidence(
                verification,
                "value",
                "structure",
                DateTimeOffset.Parse("2026-07-27T08:00:00+00:00"),
                "13.59"));
        string path = await new CsvRunLogWriter().WriteAsync(
            _temp,
            RunMode.MarkCopies,
            [result],
            TestContext.Current.CancellationToken);

        string[] rows = await File.ReadAllLinesAsync(
            path,
            Encoding.UTF8,
            TestContext.Current.CancellationToken);
        IReadOnlyList<string> fields = CsvTestParser.Parse(rows[1]);
        Assert.Equal(expectedStatus, fields[3]);
        Assert.Equal(expectedVerification, fields[4]);
    }

    [Fact]
    public async Task Csv_escapes_rfc4180_quotes_and_newlines_and_neutralizes_after_leading_spaces()
    {
        ProcessResult result = TestResult(
            relativePath: "  =SUM(1,1)\r\n\"quoted\".jpg",
            error: " \tunsafe\nsecond line");

        string path = await new CsvRunLogWriter().WriteAsync(
            _temp,
            RunMode.MarkCopies,
            [result],
            TestContext.Current.CancellationToken);

        string csv = await File.ReadAllTextAsync(
            path,
            Encoding.UTF8,
            TestContext.Current.CancellationToken);
        int firstNewline = csv.IndexOf("\r\n", StringComparison.Ordinal);
        IReadOnlyList<string> fields = CsvTestParser.Parse(csv[(firstNewline + 2)..]);
        Assert.Equal("'  =SUM(1,1)\r\n\"quoted\".jpg", fields[0]);
        Assert.Equal("' \tunsafe\nsecond line", fields[10]);
    }

    [Fact]
    public async Task Existing_final_log_is_not_overwritten_and_failed_move_cleans_its_temp_file()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-07-27T08:00:00+00:00"));
        var writer = new CsvRunLogWriter(clock);

        string original = await writer.WriteAsync(
            _temp,
            RunMode.MarkCopies,
            [TestResult("one.jpg", "")],
            TestContext.Current.CancellationToken);
        string originalText = await File.ReadAllTextAsync(
            original,
            Encoding.UTF8,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(
            _temp,
            RunMode.MarkCopies,
            [TestResult("two.jpg", "")],
            TestContext.Current.CancellationToken));

        Assert.Equal(originalText, await File.ReadAllTextAsync(
            original,
            Encoding.UTF8,
            TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(_temp, ".*.tmp"));
    }

    [Fact]
    public async Task Failure_while_constructing_a_row_cleans_the_created_temp_file()
    {
        var writer = new CsvRunLogWriter();
        var results = new ThrowingResults();

        await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(
            _temp,
            RunMode.MarkCopies,
            results,
            TestContext.Current.CancellationToken));

        Assert.Empty(Directory.EnumerateFiles(_temp, ".*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private static ProcessResult TestResult(string relativePath, string error) =>
        new(
            relativePath,
            "JPG",
            ProcessStatus.Failed,
            RunMode.MarkCopies,
            new VerificationEvidence(
                VerificationResult.Failed,
                "（未读取）",
                "验证未完成",
                DateTimeOffset.Parse("2026-07-27T08:00:00+00:00"),
                "13.59",
                "来自证据的错误"),
            Error: error);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class ThrowingResults : IReadOnlyList<ProcessResult>
    {
        public int Count => 1;

        public ProcessResult this[int index] => throw new IOException("simulated row write failure");

        public IEnumerator<ProcessResult> GetEnumerator()
        {
            _ = this[0];
            yield break;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
