using System.Text;
using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Infrastructure.Logging;

public sealed class CsvRunLogWriter(TimeProvider? timeProvider = null) : IRunLogWriter
{
    private static readonly string[] Headers =
    [
        "相对路径",
        "格式",
        "运行模式",
        "处理状态",
        "验证结果",
        "验证字段",
        "实际读取值",
        "XMP结构",
        "验证时间",
        "ExifTool版本",
        "错误原因",
    ];

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<string> WriteAsync(
        string logDirectory,
        RunMode mode,
        IReadOnlyList<ProcessResult> results,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentNullException.ThrowIfNull(results);

        Directory.CreateDirectory(logDirectory);
        string filename = $"{FilePrefix(mode)}_{_timeProvider.GetUtcNow():yyyyMMdd-HHmmss-fffffff}.csv";
        string finalPath = Path.Combine(logDirectory, filename);
        string tempPath = Path.Combine(
            logDirectory,
            $".{filename}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await using var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                    bufferSize: 4096,
                    leaveOpen: true)
                {
                    NewLine = "\r\n",
                };

                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(Row(Headers));
                foreach (ProcessResult result in results)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(Row(Fields(mode, result)));
                }

                await writer.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, finalPath, overwrite: false);
            return finalPath;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static IEnumerable<string> Fields(RunMode mode, ProcessResult result) =>
    [
        result.RelativePath,
        result.MediaFormat,
        ModeText(mode),
        StatusText(result.Status),
        VerificationText(result.Evidence.Result),
        "XMP-dc:Subject",
        result.Evidence.ActualValue,
        result.Evidence.XmpStructure,
        result.Evidence.VerifiedAt.ToString("O"),
        result.Evidence.ExifToolVersion,
        string.IsNullOrWhiteSpace(result.Error) ? result.Evidence.Error : result.Error,
    ];

    private static string Row(IEnumerable<string> fields) =>
        string.Join(",", fields.Select(field => Escape(Neutralize(field))));

    private static string Neutralize(string value)
    {
        int index = 0;
        while (index < value.Length && value[index] == ' ')
        {
            index++;
        }

        return index < value.Length && value[index] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n'
            ? $"'{value}"
            : value;
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string FilePrefix(RunMode mode) =>
        mode == RunMode.VerifyOnly ? "只读验证结果" : "标记与验证结果";

    private static string ModeText(RunMode mode) => mode switch
    {
        RunMode.MarkCopies => "创建副本并验证",
        RunMode.MarkOriginals => "直接修改原件并验证",
        RunMode.VerifyOnly => "只读验证",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static string StatusText(ProcessStatus status) => status switch
    {
        ProcessStatus.Added => "新增",
        ProcessStatus.AlreadyCompliant => "原本已合规",
        ProcessStatus.OutputAlreadyCompliant => "输出已存在且合规",
        ProcessStatus.Unmarked => "未标记",
        ProcessStatus.Failed => "失败",
        ProcessStatus.Skipped => "跳过",
        ProcessStatus.StoppedBeforeProcessing => "用户停止前未处理",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static string VerificationText(VerificationResult result) => result switch
    {
        VerificationResult.Passed => "通过",
        VerificationResult.Unmarked => "未标记",
        VerificationResult.Failed => "失败",
        VerificationResult.NotRun => "未执行",
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };
}
