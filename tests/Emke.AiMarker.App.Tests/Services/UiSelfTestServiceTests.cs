using Emke.AiMarker.App.Services;

namespace Emke.AiMarker.App.Tests.Services;

public sealed class UiSelfTestServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"emke-ui-self-test-{Guid.NewGuid():N}");

    public UiSelfTestServiceTests() => Directory.CreateDirectory(root);

    [Fact]
    public void Ui_arguments_require_the_exact_shape_and_absolute_report()
    {
        string report = Path.Combine(root, "ui-report.txt");

        Assert.True(UiSelfTestArguments.TryParse(
            ["--ui-self-test", "--report", report],
            out string parsed,
            out string error));
        Assert.Equal(Path.GetFullPath(report), parsed);
        Assert.Equal("", error);
        Assert.False(UiSelfTestArguments.TryParse(
            ["--ui-self-test", "--report", "relative.txt"],
            out _,
            out _));
        Assert.False(UiSelfTestArguments.TryParse(
            ["--ui-self-test", "--report", report, "--extra"],
            out _,
            out _));
    }

    [Fact]
    public void Ui_success_report_is_exact_and_failure_is_sanitized()
    {
        string success = Path.Combine(root, "success.txt");
        UiSelfTestReport.WriteSuccess(success);
        Assert.Equal(
            ["AppVersion=2.0.0", "MainWindow=shown", "Result=ok"],
            File.ReadAllLines(success));

        string failure = Path.Combine(root, "failure.txt");
        UiSelfTestReport.TryWriteFailure(
            failure,
            new InvalidOperationException("binding\r\nfailed"));
        Assert.Equal(
            [
                "Result=failed",
                "ErrorType=InvalidOperationException",
                "ErrorMessage=binding  failed",
            ],
            File.ReadAllLines(failure));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
