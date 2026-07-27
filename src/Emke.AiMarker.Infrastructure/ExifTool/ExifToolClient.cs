using System.Text;
using System.Text.Json;
using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Contracts;

namespace Emke.AiMarker.Infrastructure.ExifTool;

public sealed class ExifToolClient : IExifToolClient
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MediaTimeout = TimeSpan.FromMinutes(5);
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    private readonly string _executable;
    private readonly IProcessRunner _processRunner;

    public ExifToolClient(string executable, IProcessRunner processRunner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(processRunner);

        _executable = executable;
        _processRunner = processRunner;
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await ExecuteAsync(
            ["-ver"],
            VersionTimeout,
            cancellationToken);
        string version = DecodeText(result.Stdout);
        if (version.Length == 0)
        {
            throw new MarkerOperationException(
                "ExifTool 可以启动，但没有返回版本号。");
        }

        return version;
    }

    public async Task<IReadOnlyList<string>> ReadSubjectsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await ExecuteAsync(
            ["-j", "-struct", "-G1", "-s", "-XMP-dc:Subject", path],
            MediaTimeout,
            cancellationToken);

        try
        {
            using JsonDocument document = JsonDocument.Parse(result.Stdout);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array
                || root.GetArrayLength() == 0
                || root[0].ValueKind != JsonValueKind.Object)
            {
                throw new MarkerOperationException(
                    "ExifTool 未返回有效的媒体元数据。");
            }

            if (!root[0].TryGetProperty(MarkerContract.VerificationField, out JsonElement subject)
                || subject.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return [];
            }

            if (subject.ValueKind != JsonValueKind.Array)
            {
                return [GetScalarText(subject)];
            }

            var subjects = new List<string>();
            foreach (JsonElement item in subject.EnumerateArray())
            {
                if (item.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                {
                    subjects.Add(GetScalarText(item));
                }
            }

            return subjects;
        }
        catch (MarkerOperationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new MarkerOperationException(
                $"无法解析 ExifTool 返回的元数据：{exception.Message}");
        }
    }

    public async Task WriteMarkerAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            [
                "-overwrite_original",
                "-P",
                $"-XMP-dc:Subject+={MarkerContract.Marker}",
                path,
            ],
            MediaTimeout,
            cancellationToken);
    }

    public async Task WriteMarkerPreservingIdentityAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            [
                "-overwrite_original_in_place",
                "-P",
                $"-XMP-dc:Subject+={MarkerContract.Marker}",
                path,
            ],
            MediaTimeout,
            cancellationToken);
    }

    public async Task<ReadOnlyMemory<byte>> ReadRawXmpAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await ExecuteAsync(
            ["-q", "-q", "-b", "-XMP", path],
            MediaTimeout,
            cancellationToken);
        return result.Stdout;
    }

    public async Task<string> ReadImageDataHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await ExecuteAsync(
            [
                "-q",
                "-q",
                "-api",
                "RequestAll=3",
                "-s3",
                "-ImageDataHash",
                path,
            ],
            MediaTimeout,
            cancellationToken);
        return DecodeText(result.Stdout);
    }

    private async Task<ProcessExecutionResult> ExecuteAsync(
        IReadOnlyList<string> argumentFileLines,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _processRunner.ExecuteAsync(
            _executable,
            argumentFileLines,
            timeout,
            cancellationToken);

        if (result.ExitCode == 0)
        {
            return result;
        }

        string detail = DecodeText(result.Stderr);
        if (detail.Length == 0)
        {
            detail = DecodeText(result.Stdout);
        }

        if (detail.Length == 0)
        {
            detail = $"退出码 {result.ExitCode}";
        }

        throw new MarkerOperationException(detail);
    }

    private static string DecodeText(byte[] value) =>
        Utf8.GetString(value).Trim().TrimStart('\uFEFF').Trim();

    private static string GetScalarText(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : element.ToString();
}
