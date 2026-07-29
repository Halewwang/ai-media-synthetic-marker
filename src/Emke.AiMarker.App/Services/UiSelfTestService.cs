using System.IO;
using System.Text;

namespace Emke.AiMarker.App.Services;

public static class UiSelfTestArguments
{
    public static bool IsRequested(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Contains("--ui-self-test", StringComparer.Ordinal);
    }

    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out string reportPath,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        reportPath = "";
        error = "";
        if (arguments.Count != 3
            || !string.Equals(
                arguments[0],
                "--ui-self-test",
                StringComparison.Ordinal)
            || !string.Equals(arguments[1], "--report", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(arguments[2]))
        {
            error = "Expected exactly: --ui-self-test --report <absolute-path>";
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(arguments[2]))
            {
                error = "The UI self-test report path must be absolute.";
                return false;
            }

            reportPath = Path.GetFullPath(arguments[2]);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            error = exception.Message;
            return false;
        }
    }
}

public static class UiSelfTestReport
{
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static void WriteSuccess(string reportPath) =>
        WriteAtomically(
            reportPath,
            [
                "AppVersion=2.0.1",
                "MainWindow=shown",
                "Result=ok",
            ]);

    public static void TryWriteFailure(string reportPath, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            WriteAtomically(
                reportPath,
                [
                    "Result=failed",
                    $"ErrorType={exception.GetType().Name}",
                    $"ErrorMessage={SingleLine(exception.Message)}",
                ]);
        }
        catch
        {
            // Failure reporting must not replace the primary startup failure.
        }
    }

    private static void WriteAtomically(
        string reportPath,
        IReadOnlyList<string> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        if (!Path.IsPathFullyQualified(reportPath))
        {
            throw new ArgumentException(
                "The UI self-test report path must be absolute.",
                nameof(reportPath));
        }

        string fullPath = Path.GetFullPath(reportPath);
        if (Directory.Exists(fullPath))
        {
            throw new IOException("The UI self-test report path is a directory.");
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                "The UI self-test report directory does not exist.");
        }

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                string.Join('\n', lines) + "\n",
                Utf8);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // Best-effort cleanup must not replace the report result.
            }
        }
    }

    private static string SingleLine(string message) =>
        message.Replace('\r', ' ').Replace('\n', ' ');
}
