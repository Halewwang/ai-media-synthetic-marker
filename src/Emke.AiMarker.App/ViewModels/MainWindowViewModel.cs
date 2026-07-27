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
    private readonly string logDirectory;
    private readonly List<string> selectedPaths = [];
    private WorkspaceState state;
    private IReadOnlyList<DiscoveredMedia> mediaItems = [];
    private IReadOnlyList<ProcessResult> results = [];
    private IReadOnlyList<ScanIssue> scanIssues = [];
    private string currentRelativePath = "";
    private int completedCount;
    private int totalCount;
    private string outputPath = "";
    private bool isDetailsExpanded;
    private bool isOverwriteOriginals;
    private string summaryMessage = "请选择需要处理的媒体文件或文件夹。";
    private StopController? activeStop;
    private bool stopRequested;
    private RunMode? completedMode;

    public MainWindowViewModel(
        InputScanner scanner,
        StoragePreflight storagePreflight,
        IBatchProcessor batchProcessor,
        IFileSelectionService fileSelection,
        IUserPromptService prompts,
        IShellService shell,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        this.logDirectory = logDirectory;

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
            () => RunAsync(RunMode.VerifyOnly),
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
                && !string.IsNullOrWhiteSpace(OutputPath),
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

    public int SkippedCount => ScanIssues.Count;

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
        if (!CanChangeInputs())
        {
            return Task.CompletedTask;
        }

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
        Results = [];
        completedMode = null;
        LogPath = "";
        CurrentRelativePath = "";
        CompletedCount = 0;
        TotalCount = 0;
        OutputPath = GetOutputPath(MediaItems);

        if (MediaItems.Count == 0)
        {
            State = WorkspaceState.Empty;
            SummaryMessage = ScanIssues.Count == 0
                ? "未发现支持的媒体文件。"
                : $"未发现可处理媒体；{ScanIssues.Count} 个路径存在问题。";
        }
        else
        {
            State = WorkspaceState.Ready;
            SummaryMessage =
                $"已选择 {ProcessableCount} 个可处理媒体，共 {FormatBytes(TotalBytes)}；"
                + $"{SkippedCount} 个路径存在问题。";
        }

        NotifyCommands();
        return Task.CompletedTask;
    }

    public Task StartMarkAsync() =>
        RunAsync(IsOverwriteOriginals ? RunMode.MarkOriginals : RunMode.MarkCopies);

    private async Task RunAsync(RunMode mode)
    {
        if (!CanStart())
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
                    ? "输出目录检查失败。"
                    : $"无法开始安全副本处理：{storage.Error}";
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
        State = WorkspaceState.Running;
        SummaryMessage = $"正在处理 0/{TotalCount}。";

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
            NotifyCommands();
        }
    }

    private void ApplyProgress(RunProgress progress)
    {
        CompletedCount = progress.Completed;
        TotalCount = progress.Total;
        CurrentRelativePath = progress.CurrentRelativePath;
        SummaryMessage = $"正在处理 {CompletedCount}/{TotalCount}。";
    }

    private async Task AddSelectedFilesAsync() =>
        await AddPathsAsync(await fileSelection.SelectFilesAsync());

    private async Task AddSelectedFoldersAsync() =>
        await AddPathsAsync(await fileSelection.SelectFoldersAsync());

    private Task RequestSafeStopAsync()
    {
        if (activeStop is null)
        {
            return Task.CompletedTask;
        }

        activeStop.RequestStop();
        stopRequested = true;
        SummaryMessage = "已请求安全停止；当前文件完成后将不再开始新文件。";
        SafeStopCommand.NotifyCanExecuteChanged();
        return Task.CompletedTask;
    }

    private Task ResetAsync()
    {
        selectedPaths.Clear();
        MediaItems = [];
        Results = [];
        ScanIssues = [];
        completedMode = null;
        LogPath = "";
        OutputPath = "";
        CurrentRelativePath = "";
        CompletedCount = 0;
        TotalCount = 0;
        IsDetailsExpanded = false;
        IsOverwriteOriginals = false;
        SummaryMessage = "请选择需要处理的媒体文件或文件夹。";
        State = WorkspaceState.Empty;
        NotifyCommands();
        return Task.CompletedTask;
    }

    private Task OpenOutputAsync() => shell.OpenPathAsync(OutputPath);

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
        SummaryMessage = $"操作失败：{detail}";
        await prompts.ShowErrorAsync(SummaryMessage);
    }

    private bool CanChangeInputs() =>
        State is WorkspaceState.Empty or WorkspaceState.Ready;

    private bool CanStart() =>
        State == WorkspaceState.Ready && MediaItems.Count > 0;

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

    private static string BuildCompletionSummary(RunSummary summary)
    {
        int failed = summary.Results.Count(result => result.Status == ProcessStatus.Failed);
        string message =
            $"处理完成：{summary.Results.Count} 个结果，{failed} 个失败。";
        if (summary.Stopped)
        {
            message += " 已按用户请求安全停止。";
        }

        if (!summary.LogWritten)
        {
            message += " CSV 运行记录写入失败。";
        }

        return message;
    }

    private static string GetOutputPath(IReadOnlyList<DiscoveredMedia> media)
    {
        if (media.Count == 0)
        {
            return "";
        }

        DiscoveredMedia first = media[0];
        OutputPlanItem plan = OutputPlanner.Plan([first], null)[0];
        bool folderInput = !string.Equals(
            first.SourcePath,
            first.TopLevelInput,
            StringComparison.OrdinalIgnoreCase);
        if (folderInput && plan.FinalPath.EndsWith(
            first.RelativePath,
            StringComparison.OrdinalIgnoreCase))
        {
            return plan.FinalPath[..^first.RelativePath.Length].TrimEnd('\\', '/');
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
