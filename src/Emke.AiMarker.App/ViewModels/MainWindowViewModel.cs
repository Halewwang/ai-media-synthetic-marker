using Emke.AiMarker.App.Services;
using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Discovery;
using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Core.Planning;
using Emke.AiMarker.Core.Processing;

namespace Emke.AiMarker.App.ViewModels;

public enum WorkspaceState
{
    Empty,
    Ready,
    Running,
    Completed,
}

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly InputScanner scanner;
    private readonly StoragePreflight storagePreflight;
    private readonly IBatchProcessor batchProcessor;
    private readonly IFileSelectionService fileSelection;
    private readonly IUserPromptService prompts;
    private readonly IShellService shell;
    private readonly IAppText text;
    private readonly string logDirectory;
    private readonly List<string> selectedPaths = [];
    private WorkspaceState state;
    private IReadOnlyList<DiscoveredMedia> mediaItems = [];
    private IReadOnlyList<ProcessResult> results = [];
    private IReadOnlyList<ScanIssue> scanIssues = [];
    private IReadOnlyList<string> outputDirectories = [];
    private int skippedUnsupportedCount;
    private string currentRelativePath = "";
    private int completedCount;
    private int totalCount;
    private string outputPath = "";
    private bool isDetailsExpanded;
    private bool isOverwriteOriginals;
    private string summaryMessage;
    private StopController? activeStop;
    private TaskCompletionSource? activeRunCompletion;
    private bool stopRequested;
    private RunMode? completedMode;
    private int operationActive;
    private long workspaceRevision;

    public MainWindowViewModel(
        InputScanner scanner,
        StoragePreflight storagePreflight,
        IBatchProcessor batchProcessor,
        IFileSelectionService fileSelection,
        IUserPromptService prompts,
        IShellService shell,
        IAppText text,
        string logDirectory)
    {
        this.scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        this.storagePreflight = storagePreflight
            ?? throw new ArgumentNullException(nameof(storagePreflight));
        this.batchProcessor = batchProcessor
            ?? throw new ArgumentNullException(nameof(batchProcessor));
        this.fileSelection = fileSelection
            ?? throw new ArgumentNullException(nameof(fileSelection));
        this.prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
        this.shell = shell ?? throw new ArgumentNullException(nameof(shell));
        this.text = text ?? throw new ArgumentNullException(nameof(text));
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        this.logDirectory = logDirectory;
        summaryMessage = text.Get("InitialSelectionPrompt");

        AddFilesCommand = new(
            AddSelectedFilesAsync,
            CanChangeInputs,
            HandleExceptionAsync);
        AddFolderCommand = new(
            AddSelectedFoldersAsync,
            CanChangeInputs,
            HandleExceptionAsync);
        StartMarkCommand = new(
            StartMarkAsync,
            CanStart,
            HandleExceptionAsync);
        VerifyOnlyCommand = new(
            () => ExecuteExclusiveAsync(
                _ => RunCoreAsync(RunMode.VerifyOnly),
                CanStartState),
            CanStart,
            HandleExceptionAsync);
        SafeStopCommand = new(
            RequestSafeStopAsync,
            () => State == WorkspaceState.Running && !stopRequested,
            HandleExceptionAsync);
        ResetCommand = new(
            ResetAsync,
            () => State is not WorkspaceState.Running and not WorkspaceState.Empty,
            HandleExceptionAsync);
        OpenOutputCommand = new(
            OpenOutputAsync,
            () => State == WorkspaceState.Completed
                && completedMode == RunMode.MarkCopies
                && outputDirectories.Count > 0,
            HandleExceptionAsync);
        OpenLogCommand = new(
            OpenLogAsync,
            () => State == WorkspaceState.Completed
                && !string.IsNullOrWhiteSpace(LogPath),
            HandleExceptionAsync);
        OpenSettingsCommand = new(
            shell.OpenSettingsAsync,
            () => State != WorkspaceState.Running,
            HandleExceptionAsync);
        ToggleDetailsCommand = new(
            ToggleDetailsAsync,
            () => State is WorkspaceState.Ready or WorkspaceState.Completed,
            HandleExceptionAsync);
    }

    public WorkspaceState State
    {
        get => state;
        private set
        {
            if (SetProperty(ref state, value))
            {
                NotifyCommands();
            }
        }
    }

    public int MediaCount => MediaItems.Count;

    public long TotalBytes => MediaItems.Sum(item => item.Length);

    public int ProcessableCount => MediaItems.Count;

    public int SkippedCount => ScanIssues.Count + skippedUnsupportedCount;

    public string CurrentRelativePath
    {
        get => currentRelativePath;
        private set => SetProperty(ref currentRelativePath, value);
    }

    public int CompletedCount
    {
        get => completedCount;
        private set
        {
            if (SetProperty(ref completedCount, value))
            {
                OnPropertyChanged(nameof(ProgressPercent));
            }
        }
    }

    public int TotalCount
    {
        get => totalCount;
        private set
        {
            if (SetProperty(ref totalCount, value))
            {
                OnPropertyChanged(nameof(ProgressPercent));
            }
        }
    }

    public int ProgressPercent =>
        TotalCount == 0 ? 0 : (int)Math.Round(CompletedCount * 100d / TotalCount);

    public string OutputPath
    {
        get => outputPath;
        private set
        {
            if (SetProperty(ref outputPath, value))
            {
                OpenOutputCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsDetailsExpanded
    {
        get => isDetailsExpanded;
        set => SetProperty(ref isDetailsExpanded, value);
    }

    public bool IsOverwriteOriginals
    {
        get => isOverwriteOriginals;
        set
        {
            if (State != WorkspaceState.Running)
            {
                SetProperty(ref isOverwriteOriginals, value);
            }
        }
    }

    public IReadOnlyList<DiscoveredMedia> MediaItems
    {
        get => mediaItems;
        private set
        {
            if (SetProperty(ref mediaItems, value))
            {
                OnPropertyChanged(nameof(MediaCount));
                OnPropertyChanged(nameof(TotalBytes));
                OnPropertyChanged(nameof(ProcessableCount));
            }
        }
    }

    public IReadOnlyList<ProcessResult> Results
    {
        get => results;
        private set => SetProperty(ref results, value);
    }

    public IReadOnlyList<ScanIssue> ScanIssues
    {
        get => scanIssues;
        private set
        {
            if (SetProperty(ref scanIssues, value))
            {
                OnPropertyChanged(nameof(SkippedCount));
            }
        }
    }

    public string SummaryMessage
    {
        get => summaryMessage;
        private set => SetProperty(ref summaryMessage, value);
    }

    public AsyncRelayCommand AddFilesCommand { get; }

    public AsyncRelayCommand AddFolderCommand { get; }

    public AsyncRelayCommand StartMarkCommand { get; }

    public AsyncRelayCommand VerifyOnlyCommand { get; }

    public AsyncRelayCommand SafeStopCommand { get; }

    public AsyncRelayCommand ResetCommand { get; }

    public AsyncRelayCommand OpenOutputCommand { get; }

    public AsyncRelayCommand OpenLogCommand { get; }

    public AsyncRelayCommand OpenSettingsCommand { get; }

    public AsyncRelayCommand ToggleDetailsCommand { get; }

    private string LogPath { get; set; } = "";

    public Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return ExecuteExclusiveAsync(
            _ =>
            {
                ApplyPaths(paths);
                return Task.CompletedTask;
            },
            CanChangeInputsState);
    }

    public Task StartMarkAsync() =>
        ExecuteExclusiveAsync(
            async _ =>
            {
                RunMode mode = IsOverwriteOriginals
                    ? RunMode.MarkOriginals
                    : RunMode.MarkCopies;
                if (mode == RunMode.MarkOriginals
                    && !await prompts.ConfirmOriginalWriteAsync(ProcessableCount))
                {
                    return;
                }

                await RunCoreAsync(mode);
            },
            CanStartState);

    public async Task RequestSafeStopAndWaitAsync()
    {
        if (State != WorkspaceState.Running || activeStop is null)
        {
            return;
        }

        Task? completion = activeRunCompletion?.Task;
        await RequestSafeStopAsync();
        if (completion is not null)
        {
            await completion;
        }
    }

    private void ApplyPaths(IReadOnlyList<string> paths)
    {
        foreach (string path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (!selectedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                selectedPaths.Add(path);
            }
        }

        ScanResult scan = scanner.Scan(selectedPaths);
        MediaItems = scan.Media;
        ScanIssues = scan.Issues;
        SetSkippedUnsupportedCount(scan.SkippedUnsupportedCount);
        Results = [];
        completedMode = null;
        LogPath = "";
        CurrentRelativePath = "";
        CompletedCount = 0;
        TotalCount = 0;
        outputDirectories = GetOutputDirectories(MediaItems);
        OutputPath = outputDirectories.Count switch
        {
            0 => "",
            1 => outputDirectories[0],
            _ => text.Format(
                "MultipleOutputLocationsFormat",
                outputDirectories.Count),
        };
        Interlocked.Increment(ref workspaceRevision);

        if (MediaItems.Count == 0)
        {
            State = WorkspaceState.Empty;
            SummaryMessage = ScanIssues.Count == 0
                ? text.Get("NoSupportedMedia")
                : text.Format(
                    "NoProcessableMediaWithIssuesFormat",
                    ScanIssues.Count);
        }
        else
        {
            State = WorkspaceState.Ready;
            SummaryMessage = text.Format(
                "ReadySummaryFormat",
                ProcessableCount,
                FormatBytes(TotalBytes),
                SkippedCount);
        }

        NotifyCommands();
    }

    private async Task RunCoreAsync(RunMode mode)
    {
        if (!CanStartState())
        {
            return;
        }

        IReadOnlyList<OutputPlanItem> plans = OutputPlanner.Plan(MediaItems, null);
        if (mode == RunMode.MarkCopies)
        {
            StorageCheck storage = storagePreflight.Check(plans);
            if (!storage.IsReady)
            {
                string message = string.IsNullOrWhiteSpace(storage.Error)
                    ? text.Get("StorageCheckFailed")
                    : text.Format("SafeCopyStorageFailedFormat", storage.Error);
                SummaryMessage = message;
                await prompts.ShowErrorAsync(message);
                return;
            }
        }

        completedMode = null;
        Results = [];
        LogPath = "";
        CurrentRelativePath = "";
        CompletedCount = 0;
        TotalCount = plans.Count;
        stopRequested = false;
        activeStop = new StopController();
        activeRunCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Increment(ref workspaceRevision);
        State = WorkspaceState.Running;
        SummaryMessage = text.Format("RunningProgressFormat", 0, TotalCount);

        try
        {
            RunSummary summary = await batchProcessor.RunAsync(
                plans,
                mode,
                logDirectory,
                activeStop,
                new InlineProgress<RunProgress>(ApplyProgress),
                CancellationToken.None);

            Results = summary.Results;
            LogPath = summary.LogWritten ? summary.LogPath : "";
            completedMode = summary.Mode;
            if (summary.Results.Count > CompletedCount)
            {
                CompletedCount = summary.Results.Count;
            }

            SummaryMessage = BuildCompletionSummary(summary);
        }
        finally
        {
            activeStop = null;
            stopRequested = false;
            State = WorkspaceState.Completed;
            IsOverwriteOriginals = false;
            activeRunCompletion?.TrySetResult();
            activeRunCompletion = null;
            NotifyCommands();
        }
    }

    private void ApplyProgress(RunProgress progress)
    {
        CompletedCount = progress.Completed;
        TotalCount = progress.Total;
        CurrentRelativePath = progress.CurrentRelativePath;
        SummaryMessage = text.Format(
            "RunningProgressFormat",
            CompletedCount,
            TotalCount);
    }

    private Task AddSelectedFilesAsync() =>
        ExecuteExclusiveAsync(
            async revision =>
            {
                IReadOnlyList<string> paths = await fileSelection.SelectFilesAsync();
                if (revision == Volatile.Read(ref workspaceRevision)
                    && CanChangeInputsState())
                {
                    ApplyPaths(paths);
                }
            },
            CanChangeInputsState);

    private Task AddSelectedFoldersAsync() =>
        ExecuteExclusiveAsync(
            async revision =>
            {
                IReadOnlyList<string> paths = await fileSelection.SelectFoldersAsync();
                if (revision == Volatile.Read(ref workspaceRevision)
                    && CanChangeInputsState())
                {
                    ApplyPaths(paths);
                }
            },
            CanChangeInputsState);

    private Task RequestSafeStopAsync()
    {
        if (activeStop is null)
        {
            return Task.CompletedTask;
        }

        activeStop.RequestStop();
        stopRequested = true;
        SummaryMessage = text.Get("SafeStopRequested");
        SafeStopCommand.NotifyCanExecuteChanged();
        return Task.CompletedTask;
    }

    private Task ResetAsync()
    {
        selectedPaths.Clear();
        MediaItems = [];
        Results = [];
        ScanIssues = [];
        SetSkippedUnsupportedCount(0);
        outputDirectories = [];
        completedMode = null;
        LogPath = "";
        OutputPath = "";
        CurrentRelativePath = "";
        CompletedCount = 0;
        TotalCount = 0;
        IsDetailsExpanded = false;
        IsOverwriteOriginals = false;
        SummaryMessage = text.Get("InitialSelectionPrompt");
        Interlocked.Increment(ref workspaceRevision);
        State = WorkspaceState.Empty;
        NotifyCommands();
        return Task.CompletedTask;
    }

    private async Task OpenOutputAsync()
    {
        foreach (string directory in outputDirectories)
        {
            await shell.OpenPathAsync(directory);
        }
    }

    private Task OpenLogAsync() => shell.OpenPathAsync(LogPath);

    private Task ToggleDetailsAsync()
    {
        IsDetailsExpanded = !IsDetailsExpanded;
        return Task.CompletedTask;
    }

    private async Task HandleExceptionAsync(Exception exception)
    {
        string detail = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
        SummaryMessage = text.Format("OperationFailedFormat", detail);
        await prompts.ShowErrorAsync(SummaryMessage);
    }

    private bool CanChangeInputs() =>
        !IsOperationActive && CanChangeInputsState();

    private bool CanStart() =>
        !IsOperationActive && CanStartState();

    private bool CanChangeInputsState() =>
        State is WorkspaceState.Empty or WorkspaceState.Ready;

    private bool CanStartState() =>
        State == WorkspaceState.Ready && MediaItems.Count > 0;

    private bool IsOperationActive => Volatile.Read(ref operationActive) == 1;

    private async Task ExecuteExclusiveAsync(
        Func<long, Task> operation,
        Func<bool> canBegin)
    {
        if (!canBegin()
            || Interlocked.CompareExchange(ref operationActive, 1, 0) != 0)
        {
            return;
        }

        long revision = Volatile.Read(ref workspaceRevision);
        NotifyCommands();
        try
        {
            await operation(revision);
        }
        finally
        {
            Interlocked.Exchange(ref operationActive, 0);
            NotifyCommands();
        }
    }

    private void SetSkippedUnsupportedCount(int value)
    {
        if (skippedUnsupportedCount == value)
        {
            return;
        }

        skippedUnsupportedCount = value;
        OnPropertyChanged(nameof(SkippedCount));
    }

    private void NotifyCommands()
    {
        AddFilesCommand?.NotifyCanExecuteChanged();
        AddFolderCommand?.NotifyCanExecuteChanged();
        StartMarkCommand?.NotifyCanExecuteChanged();
        VerifyOnlyCommand?.NotifyCanExecuteChanged();
        SafeStopCommand?.NotifyCanExecuteChanged();
        ResetCommand?.NotifyCanExecuteChanged();
        OpenOutputCommand?.NotifyCanExecuteChanged();
        OpenLogCommand?.NotifyCanExecuteChanged();
        OpenSettingsCommand?.NotifyCanExecuteChanged();
        ToggleDetailsCommand?.NotifyCanExecuteChanged();
    }

    private string BuildCompletionSummary(RunSummary summary)
    {
        int failed = summary.Results.Count(result => result.Status == ProcessStatus.Failed);
        string message = text.Format(
            "CompletionSummaryFormat",
            summary.Results.Count,
            failed);
        if (summary.Stopped)
        {
            message += text.Get("CompletionStoppedSuffix");
        }

        if (!summary.LogWritten)
        {
            message += text.Get("CompletionLogFailedSuffix");
        }

        return message;
    }

    private static IReadOnlyList<string> GetOutputDirectories(
        IReadOnlyList<DiscoveredMedia> media)
    {
        if (media.Count == 0)
        {
            return [];
        }

        IReadOnlyList<OutputPlanItem> plans = OutputPlanner.Plan(media, null);
        return media
            .Zip(plans, GetOutputDirectory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetOutputDirectory(
        DiscoveredMedia media,
        OutputPlanItem plan)
    {
        bool folderInput = !string.Equals(
            media.SourcePath,
            media.TopLevelInput,
            StringComparison.OrdinalIgnoreCase);
        if (folderInput && plan.FinalPath.EndsWith(
            media.RelativePath,
            StringComparison.OrdinalIgnoreCase))
        {
            return plan.FinalPath[..^media.RelativePath.Length].TrimEnd('\\', '/');
        }

        return GetDirectoryName(plan.FinalPath);
    }

    private static string GetDirectoryName(string path)
    {
        string normalized = path.TrimEnd('\\', '/');
        int separator = Math.Max(normalized.LastIndexOf('\\'), normalized.LastIndexOf('/'));
        return separator < 0 ? "" : normalized[..separator];
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.#} KB";
        }

        return $"{bytes / (1024d * 1024d):0.#} MB";
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
