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
        ViewModel = new MainWindowViewModel(
            new InputScanner(paths),
            new StoragePreflight(Storage),
            Batch,
            Selection,
            Prompts,
            Shell,
            @"D:\运行记录");
    }

    public MainWindowViewModel ViewModel { get; }

    public ControllableBatchProcessor Batch { get; }

    public RecordingPromptService Prompts { get; }

    public RecordingShellService Shell { get; }

    public FakeFileSelectionService Selection { get; }

    public FakeStorageProbe Storage { get; }

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

    public RunSummary? NextSummary { get; set; }

    public void BlockUntilReleased() =>
        release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release(RunSummary summary) => release!.TrySetResult(summary);

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
            new Dictionary<ProcessStatus, int> { [ProcessStatus.Added] = 1 }));

        RunSummary summary = release is null
            ? NextSummary ?? TestSummaries.Success(mode)
            : await release.Task;
        return summary;
    }
}

internal sealed class FakeFileSelectionService : IFileSelectionService
{
    public IReadOnlyList<string> Files { get; set; } = [];

    public IReadOnlyList<string> Folders { get; set; } = [];

    public Task<IReadOnlyList<string>> SelectFilesAsync() => Task.FromResult(Files);

    public Task<IReadOnlyList<string>> SelectFoldersAsync() => Task.FromResult(Folders);
}

internal sealed class RecordingPromptService : IUserPromptService
{
    private readonly TaskCompletionSource prompted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<string> Errors { get; } = [];

    public Task Prompted => prompted.Task;

    public Task ShowErrorAsync(string message)
    {
        Errors.Add(message);
        prompted.TrySetResult();
        return Task.CompletedTask;
    }
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
        RunMode mode = RunMode.MarkCopies,
        bool logWritten = true,
        bool stopped = false)
    {
        var evidence = new VerificationEvidence(
            VerificationResult.Passed,
            "[\"contains-synthetic-performer\"]",
            "dc:subject/rdf:Bag/rdf:li",
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
            "13.59");
        ProcessResult result = new(
            "a.jpg",
            "JPG",
            ProcessStatus.Added,
            mode,
            evidence,
            mode == RunMode.MarkCopies ? @"D:\EMKE 已标记\商品\a.jpg" : "");
        return new(
            mode,
            [result],
            logWritten ? @"D:\运行记录\run.csv" : "",
            logWritten,
            stopped);
    }
}
