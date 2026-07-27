using Emke.AiMarker.App.Services;
using Emke.AiMarker.Core.Abstractions;

namespace Emke.AiMarker.App.Tests.Services;

public sealed class SelfTestServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"emke-self-test-{Guid.NewGuid():N}");

    public SelfTestServiceTests() => Directory.CreateDirectory(root);

    [Fact]
    public void Arguments_require_the_exact_headless_shape_and_absolute_report()
    {
        string report = Path.Combine(root, "report.txt");

        Assert.True(SelfTestArguments.TryParse(
            ["--self-test", "--report", report],
            out string parsed,
            out string error));
        Assert.Equal(Path.GetFullPath(report), parsed);
        Assert.Equal("", error);

        Assert.False(SelfTestArguments.TryParse(
            ["--self-test", "--report", "relative.txt"],
            out _,
            out string relativeError));
        Assert.NotEmpty(relativeError);
        Assert.False(SelfTestArguments.TryParse(
            ["--self-test", "--report", report, "--extra"],
            out _,
            out string shapeError));
        Assert.NotEmpty(shapeError);
    }

    [Fact]
    public async Task Successful_self_test_writes_the_exact_success_contract()
    {
        string report = Path.Combine(root, "report.txt");
        var client = new FakeExifToolClient("13.59");
        int validatorCalls = 0;
        var service = new SelfTestService(
            typeof(SelfTestService).Assembly,
            Path.Combine(root, "exiftool"),
            Path.Combine(root, "exiftool.lock.json"),
            client,
            (_, _) => validatorCalls++,
            _ => true);

        int exitCode = await service.RunAsync(
            report,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, validatorCalls);
        Assert.Equal(
            [
                "AppVersion=2.0.0",
                "Runtime=.NET 10",
                "ExifTool=13.59",
                "Result=ok",
            ],
            await File.ReadAllLinesAsync(
                report,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Failed_validation_returns_one_and_reports_exception_details()
    {
        string report = Path.Combine(root, "report.txt");
        var service = new SelfTestService(
            typeof(SelfTestService).Assembly,
            Path.Combine(root, "exiftool"),
            Path.Combine(root, "exiftool.lock.json"),
            new FakeExifToolClient("13.59"),
            (_, _) => throw new InvalidDataException("manifest mismatch"),
            _ => true);

        int exitCode = await service.RunAsync(
            report,
            TestContext.Current.CancellationToken);
        string body = await File.ReadAllTextAsync(
            report,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains("Result=failed", body, StringComparison.Ordinal);
        Assert.Contains("ErrorType=InvalidDataException", body, StringComparison.Ordinal);
        Assert.Contains("ErrorMessage=manifest mismatch", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_exiftool_version_and_missing_logo_are_failures()
    {
        string versionReport = Path.Combine(root, "version.txt");
        var wrongVersion = new SelfTestService(
            typeof(SelfTestService).Assembly,
            Path.Combine(root, "exiftool"),
            Path.Combine(root, "exiftool.lock.json"),
            new FakeExifToolClient("13.58"),
            (_, _) => { },
            _ => true);
        Assert.Equal(
            1,
            await wrongVersion.RunAsync(
                versionReport,
                TestContext.Current.CancellationToken));
        Assert.Contains(
            "13.58",
            await File.ReadAllTextAsync(
                versionReport,
                TestContext.Current.CancellationToken),
            StringComparison.Ordinal);

        string logoReport = Path.Combine(root, "logo.txt");
        var missingLogo = new SelfTestService(
            typeof(SelfTestService).Assembly,
            Path.Combine(root, "exiftool"),
            Path.Combine(root, "exiftool.lock.json"),
            new FakeExifToolClient("13.59"),
            (_, _) => { },
            resource => !resource.EndsWith(
                "emke-app-logo-256.png",
                StringComparison.Ordinal));
        Assert.Equal(
            1,
            await missingLogo.RunAsync(
                logoReport,
                TestContext.Current.CancellationToken));
        Assert.Contains(
            "emke-app-logo-256.png",
            await File.ReadAllTextAsync(
                logoReport,
                TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_target_cannot_be_a_directory_or_have_a_missing_parent()
    {
        var service = new SelfTestService(
            typeof(SelfTestService).Assembly,
            Path.Combine(root, "exiftool"),
            Path.Combine(root, "exiftool.lock.json"),
            new FakeExifToolClient("13.59"),
            (_, _) => { },
            _ => true);

        Assert.Equal(
            1,
            await service.RunAsync(
                root,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await service.RunAsync(
                Path.Combine(root, "missing", "report.txt"),
                TestContext.Current.CancellationToken));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class FakeExifToolClient(string version) : IExifToolClient
    {
        public Task<string> GetVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(version);

        public Task<IReadOnlyList<string>> ReadSubjectsAsync(
            string path,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task WriteMarkerAsync(
            string path,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task WriteMarkerPreservingIdentityAsync(
            string path,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReadOnlyMemory<byte>> ReadRawXmpAsync(
            string path,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> ReadImageDataHashAsync(
            string path,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
