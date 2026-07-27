using Emke.AiMarker.App.Services;
using Emke.AiMarker.App.ViewModels;
using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Discovery;
using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Core.Planning;
using Emke.AiMarker.Core.Processing;

namespace Emke.AiMarker.App.Tests.TestSupport;

internal sealed class MainWindowHarness
{
    private MainWindowHarness(FakePathAccess paths, long availableBytes)
    {
        Paths = paths;
        Batch = new ControllableBatchProcessor();
        Prompts = new RecordingPromptService();
        Shell = new RecordingShellService();
        Selection = new FakeFileSelectionService();
        Storage = new FakeStorageProbe(availableBytes);
        Text = new FakeAppText();
        ViewModel = new MainWindowViewModel(
            new InputScanner(paths),
            new StoragePreflight(Storage),
            Batch,
            Selection,
            Prompts,
            Shell,
            Text,
            @"D:\运行记录");
    }

    public MainWindowViewModel ViewModel { get; }

    public ControllableBatchProcessor Batch { get; }

    public RecordingPromptService Prompts { get; }

    public RecordingShellService Shell { get; }

    public FakeFileSelectionService Selection { get; }

    public FakeStorageProbe Storage { get; }

    public FakeAppText Text { get; }

    public FakePathAccess Paths { get; }

    public static MainWindowHarness Empty(long availableBytes = long.MaxValue) =>
        new(new FakePathAccess(), availableBytes);

    public static MainWindowHarness ReadyWithMedia(long availableBytes = long.MaxValue)
    {
        MainWindowHarness harness = Empty(availableBytes);
        harness.Paths
            .Directory(@"D:\商品")
            .File(@"D:\商品\a.jpg", 10)
            .File(@"D:\商品\b.MP4", 20);
        harness.ViewModel.AddPathsAsync([@"D:\商品"]).GetAwaiter().GetResult();
        return harness;
    }
}

internal sealed class FakeAppText : IAppText
{
    private static readonly IReadOnlyDictionary<string, string> Values =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["InitialSelectionPrompt"] = "请选择需要处理的媒体文件或文件夹。",
            ["MultipleOutputLocationsFormat"] = "多个输出位置（{0}）",
            ["NoSupportedMedia"] = "未发现支持的媒体文件。",
            ["NoProcessableMediaWithIssuesFormat"] = "未发现可处理媒体；{0} 个路径存在问题。",
            ["ReadySummaryFormat"] = "已选择 {0} 个可处理媒体，共 {1}；{2} 个项目已跳过或存在问题。",
            ["StorageCheckFailed"] = "输出目录检查失败。",
            ["SafeCopyStorageFailedFormat"] = "无法开始安全副本处理：{0}",
            ["RunningProgressFormat"] = "正在处理 {0}/{1}。",
            ["SafeStopRequested"] = "已请求安全停止；当前文件完成后将不再开始新文件。",
            ["OperationFailedFormat"] = "操作失败：{0}",
            ["CompletionSummaryFormat"] = "处理完成：{0} 个结果，{1} 个失败。",
            ["CompletionStoppedSuffix"] = " 已按用户请求安全停止。",
            ["CompletionLogFailedSuffix"] = " CSV 运行记录写入失败。",
        };

    public string Get(string key) =>
        Values.TryGetValue(key, out string? value)
            ? value
            : throw new KeyNotFoundException(key);

    public string Format(string key, params object[] arguments) =>
        string.Format(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), Get(key), arguments);
}

internal sealed class FakePathAccess : IPathAccess
{
    private readonly Dictionary<string, FakeEntry> entries =
        new(StringComparer.OrdinalIgnoreCase);

    public FakePathAccess Directory(string path) => Add(path, PathEntryKind.Directory, 0);

    public FakePathAccess File(string path, long length) => Add(path, PathEntryKind.File, length);

    public FakePathAccess ReparseFile(string path) => Add(path, PathEntryKind.ReparseFile, 0);

    public PathEntryKind GetKind(string path) =>
        entries.TryGetValue(Normalize(path), out FakeEntry? entry)
            ? entry.Kind
            : PathEntryKind.Missing;

    public IEnumerable<string> EnumerateChildren(string directory)
    {
        string normalized = Normalize(directory);
        return entries.Keys.Where(path => string.Equals(
            Parent(path),
            normalized,
            StringComparison.OrdinalIgnoreCase));
    }

    public long GetFileLength(string file) => entries[Normalize(file)].Length;

    public string GetFullPath(string path) => Normalize(path);

    private FakePathAccess Add(string path, PathEntryKind kind, long length)
    {
        entries[Normalize(path)] = new(kind, length);
        return this;
    }

    private static string Normalize(string path) => path.TrimEnd('\\', '/').Replace('/', '\\');

    private static string Parent(string path)
    {
        int separator = path.LastIndexOf('\\');
        return separator < 0 ? string.Empty : path[..separator];
    }

    private sealed record FakeEntry(PathEntryKind Kind, long Length);
}

internal sealed class FakeStorageProbe(long availableBytes) : IStorageProbe
{
    public long AvailableBytes { get; set; } = availableBytes;

    public Exception? WritableException { get; set; }

    public long GetAvailableBytes(string directory) => AvailableBytes;

    public void AssertWritable(string directory)
    {
        if (WritableException is not null)
        {
            throw WritableException;
        }
    }
}

internal sealed class ControllableBatchProcessor : IBatchProcessor
{
    private TaskCompletionSource<RunSummary>? release;

    public bool WasStarted { get; private set; }

    public int StartCount { get; private set; }

    public RunMode? ReceivedMode { get; private set; }

    public IReadOnlyList<OutputPlanItem> ReceivedPlans { get; private set; } = [];

    public StopController? ReceivedStop { get; private set; }

    public Exception? Exception { get; set; }

    public bool LogWritten { get; set; } = true;

    public void BlockUntilReleased() =>
        release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ReleaseSuccess() => release!.TrySetResult(
        TestSummaries.Success(ReceivedPlans, ReceivedMode!.Value, LogWritten));

    public void ReleaseStopped() => release!.TrySetResult(
        TestSummaries.Stopped(ReceivedPlans, ReceivedMode!.Value, LogWritten));

    public async Task<RunSummary> RunAsync(
        IReadOnlyList<OutputPlanItem> plans,
        RunMode mode,
        string logDirectory,
        StopController stop,
        IProgress<RunProgress>? progress,
        CancellationToken cancellationToken)
    {
        WasStarted = true;
        StartCount++;
        ReceivedMode = mode;
        ReceivedPlans = plans;
        ReceivedStop = stop;

        if (Exception is not null)
        {
            throw Exception;
        }

        progress?.Report(new RunProgress(
            1,
            plans.Count,
            plans[0].RelativePath,
            new Dictionary<ProcessStatus, int> { [TestSummaries.SuccessStatus(mode)] = 1 }));

        RunSummary summary = release is null
            ? TestSummaries.Success(plans, mode, LogWritten)
            : await release.Task;
        progress?.Report(new RunProgress(
            summary.Results.Count,
            plans.Count,
            summary.Results[^1].RelativePath,
            summary.Results
                .GroupBy(result => result.Status)
                .ToDictionary(group => group.Key, group => group.Count())));
        return summary;
    }
}

internal sealed class FakeFileSelectionService : IFileSelectionService
{
    private TaskCompletionSource<IReadOnlyList<string>>? filesRelease;
    private TaskCompletionSource? filesRequested;

    public IReadOnlyList<string> Files { get; set; } = [];

    public IReadOnlyList<string> Folders { get; set; } = [];

    public Task FilesRequested =>
        filesRequested?.Task ?? throw new InvalidOperationException("File selection is not blocked.");

    public void BlockFilesUntilReleased()
    {
        filesRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        filesRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void ReleaseFiles(IReadOnlyList<string> files) =>
        filesRelease!.TrySetResult(files);

    public Task<IReadOnlyList<string>> SelectFilesAsync()
    {
        filesRequested?.TrySetResult();
        return filesRelease?.Task ?? Task.FromResult(Files);
    }

    public Task<IReadOnlyList<string>> SelectFoldersAsync() => Task.FromResult(Folders);
}

internal sealed class RecordingPromptService : IUserPromptService
{
    private readonly TaskCompletionSource prompted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<string> Errors { get; } = [];

    public Task Prompted => prompted.Task;

    public bool NextOriginalWriteConfirmation { get; set; } = true;

    public bool NextSafeCloseConfirmation { get; set; }

    public int OriginalWriteConfirmationCount { get; private set; }

    public int LastOriginalWriteCount { get; private set; }

    public Task ShowErrorAsync(string message)
    {
        Errors.Add(message);
        prompted.TrySetResult();
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmOriginalWriteAsync(int count)
    {
        OriginalWriteConfirmationCount++;
        LastOriginalWriteCount = count;
        return Task.FromResult(NextOriginalWriteConfirmation);
    }

    public Task<bool> ConfirmSafeStopForCloseAsync() =>
        Task.FromResult(NextSafeCloseConfirmation);
}

internal sealed class RecordingShellService : IShellService
{
    public List<string> OpenedPaths { get; } = [];

    public int SettingsOpenCount { get; private set; }

    public Task OpenPathAsync(string path)
    {
        OpenedPaths.Add(path);
        return Task.CompletedTask;
    }

    public Task OpenSettingsAsync()
    {
        SettingsOpenCount++;
        return Task.CompletedTask;
    }
}

internal static class TestSummaries
{
    public static RunSummary Success(
        IReadOnlyList<OutputPlanItem> plans,
        RunMode mode,
        bool logWritten = true) =>
        new(
            mode,
            plans.Select(plan => Result(plan, mode, SuccessStatus(mode))).ToArray(),
            logWritten ? @"D:\运行记录\run.csv" : "",
            logWritten,
            Stopped: false);

    public static RunSummary Stopped(
        IReadOnlyList<OutputPlanItem> plans,
        RunMode mode,
        bool logWritten = true) =>
        new(
            mode,
            plans.Select((plan, index) => Result(
                plan,
                mode,
                index == 0
                    ? SuccessStatus(mode)
                    : ProcessStatus.StoppedBeforeProcessing)).ToArray(),
            logWritten ? @"D:\运行记录\run.csv" : "",
            logWritten,
            Stopped: true);

    private static ProcessResult Result(
        OutputPlanItem plan,
        RunMode mode,
        ProcessStatus status)
    {
        var evidence = new VerificationEvidence(
            status == ProcessStatus.StoppedBeforeProcessing
                ? VerificationResult.NotRun
                : VerificationResult.Passed,
            status == ProcessStatus.StoppedBeforeProcessing
                ? "（未读取）"
                : "[\"contains-synthetic-performer\"]",
            status == ProcessStatus.StoppedBeforeProcessing
                ? "未验证"
                : "dc:subject/rdf:Bag/rdf:li",
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
            status == ProcessStatus.StoppedBeforeProcessing ? "" : "13.59",
            status == ProcessStatus.StoppedBeforeProcessing
                ? "用户停止前未处理"
                : "");
        return new(
            plan.RelativePath,
            Path.GetExtension(plan.SourcePath).TrimStart('.').ToUpperInvariant(),
            status,
            mode,
            evidence,
            status != ProcessStatus.StoppedBeforeProcessing
                && mode == RunMode.MarkCopies
                    ? plan.FinalPath
                    : "",
            status == ProcessStatus.StoppedBeforeProcessing
                ? "用户停止前未处理"
                : "");
    }

    public static ProcessStatus SuccessStatus(RunMode mode) =>
        mode == RunMode.VerifyOnly
            ? ProcessStatus.AlreadyCompliant
            : ProcessStatus.Added;
}
