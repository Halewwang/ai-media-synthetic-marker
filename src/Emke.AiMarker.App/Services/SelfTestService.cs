using System.IO;
using System.Reflection;
using System.Text;
using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Infrastructure.ExifTool;

namespace Emke.AiMarker.App.Services;

public static class SelfTestArguments
{
    public static bool IsRequested(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Contains("--self-test", StringComparer.Ordinal);
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
            || !string.Equals(arguments[0], "--self-test", StringComparison.Ordinal)
            || !string.Equals(arguments[1], "--report", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(arguments[2]))
        {
            error = "Expected exactly: --self-test --report <absolute-path>";
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(arguments[2]))
            {
                error = "The self-test report path must be absolute.";
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

    public static bool TryGetAbsoluteReportPath(
        IReadOnlyList<string> arguments,
        out string reportPath)
    {
        reportPath = "";
        for (int index = 0; index + 1 < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], "--report", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(arguments[index + 1])
                || !Path.IsPathFullyQualified(arguments[index + 1]))
            {
                continue;
            }

            try
            {
                reportPath = Path.GetFullPath(arguments[index + 1]);
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or NotSupportedException
                    or PathTooLongException)
            {
                return false;
            }
        }

        return false;
    }
}

public sealed class SelfTestService
{
    private const string ExpectedExifToolVersion = "13.59";
    private static readonly Version ExpectedAssemblyVersion = new(2, 0, 1, 0);
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly string[] RequiredLogoResources =
    [
        "Assets/emke-app-logo-32.png",
        "Assets/emke-app-logo-256.png",
        "Assets/emke-ai-marker.ico",
    ];

    private readonly Assembly appAssembly;
    private readonly string runtimeRoot;
    private readonly string lockPath;
    private readonly IExifToolClient exifTool;
    private readonly Action<string, string> validateRuntime;
    private readonly Func<string, bool> resourceExists;

    public SelfTestService(
        Assembly appAssembly,
        string runtimeRoot,
        string lockPath,
        IExifToolClient exifTool,
        Action<string, string>? validateRuntime = null,
        Func<string, bool>? resourceExists = null)
    {
        this.appAssembly = appAssembly
            ?? throw new ArgumentNullException(nameof(appAssembly));
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        this.runtimeRoot = Path.GetFullPath(runtimeRoot);
        this.lockPath = Path.GetFullPath(lockPath);
        this.exifTool = exifTool
            ?? throw new ArgumentNullException(nameof(exifTool));
        this.validateRuntime = validateRuntime
            ?? ExifToolManifestValidator.Validate;
        this.resourceExists = resourceExists
            ?? throw new ArgumentNullException(nameof(resourceExists));
    }

    public async Task<int> RunAsync(
        string reportPath,
        CancellationToken cancellationToken = default)
    {
        string? normalizedReport = null;
        try
        {
            normalizedReport = ValidateReportTarget(reportPath);
            await ProbeReportDirectoryAsync(normalizedReport, cancellationToken);
            ValidateAssembly();
            validateRuntime(runtimeRoot, lockPath);

            string version = await exifTool.GetVersionAsync(cancellationToken);
            if (!string.Equals(
                    version,
                    ExpectedExifToolVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"ExifTool version must be {ExpectedExifToolVersion}; actual: {version}.");
            }

            foreach (string resource in RequiredLogoResources)
            {
                if (!resourceExists(resource))
                {
                    throw new FileNotFoundException(
                        $"Required Logo resource is missing: {resource}",
                        resource);
                }
            }

            await WriteReportAtomicallyAsync(
                normalizedReport,
                [
                    "AppVersion=2.0.1",
                    "Runtime=.NET 10",
                    "ExifTool=13.59",
                    "Result=ok",
                ],
                cancellationToken);
            return 0;
        }
        catch (Exception exception)
        {
            if (normalizedReport is not null)
            {
                await TryWriteFailureReportAsync(
                    normalizedReport,
                    exception,
                    cancellationToken);
            }
            else
            {
                await TryWriteFailureReportAtRequestedPathAsync(
                    reportPath,
                    exception,
                    cancellationToken);
            }

            return 1;
        }
    }

    public static Task TryWriteFailureReportAtRequestedPathAsync(
        string reportPath,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            string normalized = ValidateReportTarget(reportPath);
            return TryWriteFailureReportAsync(
                normalized,
                exception,
                cancellationToken);
        }
        catch
        {
            return Task.CompletedTask;
        }
    }

    private void ValidateAssembly()
    {
        Version? actual = appAssembly.GetName().Version;
        if (actual != ExpectedAssemblyVersion)
        {
            throw new InvalidDataException(
                $"Application assembly version must be {ExpectedAssemblyVersion}; actual: {actual}.");
        }

        if (Environment.Version.Major != 10)
        {
            throw new InvalidDataException(
                $"Runtime major version must be 10; actual: {Environment.Version.Major}.");
        }
    }

    private static string ValidateReportTarget(string reportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        if (!Path.IsPathFullyQualified(reportPath))
        {
            throw new ArgumentException(
                "The self-test report path must be absolute.",
                nameof(reportPath));
        }

        string fullPath = Path.GetFullPath(reportPath);
        if (Directory.Exists(fullPath))
        {
            throw new IOException("The self-test report path is a directory.");
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                "The self-test report directory does not exist.");
        }

        return fullPath;
    }

    private static async Task ProbeReportDirectoryAsync(
        string reportPath,
        CancellationToken cancellationToken)
    {
        string probe = TemporaryReportPath(reportPath);
        try
        {
            await using var stream = new FileStream(
                probe,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            TryDelete(probe);
        }
    }

    private static async Task TryWriteFailureReportAsync(
        string reportPath,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteReportAtomicallyAsync(
                reportPath,
                [
                    "Result=failed",
                    $"ErrorType={exception.GetType().Name}",
                    $"ErrorMessage={SingleLine(exception.Message)}",
                ],
                cancellationToken);
        }
        catch
        {
            // A report cannot be promised when its destination is not writable.
        }
    }

    private static async Task WriteReportAtomicallyAsync(
        string reportPath,
        IReadOnlyList<string> lines,
        CancellationToken cancellationToken)
    {
        string temporaryPath = TemporaryReportPath(reportPath);
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                string.Join('\n', lines) + "\n",
                Utf8,
                cancellationToken);
            File.Move(temporaryPath, reportPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static string TemporaryReportPath(string reportPath)
    {
        string directory = Path.GetDirectoryName(reportPath)!;
        return Path.Combine(
            directory,
            $".{Path.GetFileName(reportPath)}.{Guid.NewGuid():N}.tmp");
    }

    private static string SingleLine(string message) =>
        message.Replace('\r', ' ').Replace('\n', ' ');

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup must not replace the primary self-test result.
        }
    }
}
