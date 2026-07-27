using Emke.AiMarker.App.Tests.TestSupport;
using Emke.AiMarker.App.ViewModels;
using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.App.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void New_workspace_is_empty_and_safe_copy_is_the_default()
    {
        MainWindowHarness harness = MainWindowHarness.Empty();

        Assert.Equal(WorkspaceState.Empty, harness.ViewModel.State);
        Assert.False(harness.ViewModel.IsOverwriteOriginals);
        Assert.False(harness.ViewModel.StartMarkCommand.CanExecute(null));
        Assert.True(harness.ViewModel.AddFilesCommand.CanExecute(null));
    }

    [Fact]
    public async Task Adding_paths_deduplicates_media_and_summarizes_scan_issues()
    {
        MainWindowHarness harness = MainWindowHarness.Empty();
        harness.Paths
            .Directory(@"D:\商品")
            .File(@"D:\商品\a.jpg", 10)
            .File(@"D:\商品\b.MP4", 20)
            .File(@"D:\商品\ignore.txt", 30)
            .ReparseFile(@"D:\商品\linked.png");

        await harness.ViewModel.AddPathsAsync([@"D:\商品", @"d:\商品"]);

        Assert.Equal(WorkspaceState.Ready, harness.ViewModel.State);
        Assert.Equal(2, harness.ViewModel.MediaCount);
        Assert.Equal(2, harness.ViewModel.ProcessableCount);
        Assert.Equal(2, harness.ViewModel.SkippedCount);
        Assert.Equal(30, harness.ViewModel.TotalBytes);
        Assert.Equal(["a.jpg", "b.MP4"], harness.ViewModel.MediaItems.Select(item => item.RelativePath));
        Assert.Single(harness.ViewModel.ScanIssues);
        Assert.Contains("2", harness.ViewModel.SummaryMessage, StringComparison.Ordinal);
        Assert.True(harness.ViewModel.StartMarkCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_selection_without_processable_media_keeps_the_workspace_empty()
    {
        MainWindowHarness harness = MainWindowHarness.Empty();
        harness.Paths.File(@"D:\notes.txt", 10);

        await harness.ViewModel.AddPathsAsync([@"D:\notes.txt"]);

        Assert.Equal(WorkspaceState.Empty, harness.ViewModel.State);
        Assert.Empty(harness.ViewModel.MediaItems);
        Assert.False(harness.ViewModel.StartMarkCommand.CanExecute(null));
    }

    [Fact]
    public async Task Running_locks_inputs_enables_safe_stop_and_finishes_completed()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        harness.Batch.BlockUntilReleased();

        Task run = harness.ViewModel.StartMarkAsync();

        Assert.Equal(WorkspaceState.Running, harness.ViewModel.State);
        Assert.False(harness.ViewModel.AddFilesCommand.CanExecute(null));
        Assert.False(harness.ViewModel.StartMarkCommand.CanExecute(null));
        Assert.True(harness.ViewModel.SafeStopCommand.CanExecute(null));

        harness.Batch.ReleaseSuccess();
        await run;

        Assert.Equal(WorkspaceState.Completed, harness.ViewModel.State);
        Assert.False(harness.ViewModel.SafeStopCommand.CanExecute(null));
        Assert.True(harness.ViewModel.OpenOutputCommand.CanExecute(null));
        Assert.True(harness.ViewModel.OpenLogCommand.CanExecute(null));
        Assert.Equal(2, harness.ViewModel.CompletedCount);
        Assert.Equal(2, harness.ViewModel.TotalCount);
        Assert.Equal(100, harness.ViewModel.ProgressPercent);
    }

    [Fact]
    public async Task Safe_stop_requests_the_active_core_stop_controller()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        harness.Batch.BlockUntilReleased();
        Task run = harness.ViewModel.StartMarkAsync();

        harness.ViewModel.SafeStopCommand.Execute(null);

        Assert.True(harness.Batch.ReceivedStop!.IsStopRequested);
        Assert.Contains("停止", harness.ViewModel.SummaryMessage, StringComparison.Ordinal);
        harness.Batch.ReleaseStopped();
        await run;

        Assert.Contains(
            harness.ViewModel.Results,
            result => result.Status == ProcessStatus.StoppedBeforeProcessing);
        Assert.Equal(100, harness.ViewModel.ProgressPercent);
    }

    [Fact]
    public async Task Verify_only_maps_to_verify_mode_without_storage_preflight()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia(availableBytes: 0);

        await harness.ViewModel.VerifyOnlyCommand.ExecuteAsync();

        Assert.Equal(RunMode.VerifyOnly, harness.Batch.ReceivedMode);
        Assert.Equal(WorkspaceState.Completed, harness.ViewModel.State);
        Assert.False(harness.ViewModel.OpenOutputCommand.CanExecute(null));
        Assert.True(harness.ViewModel.OpenLogCommand.CanExecute(null));
    }

    [Fact]
    public async Task Original_flag_maps_to_original_mode_and_resets_after_completion()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        harness.ViewModel.IsOverwriteOriginals = true;

        await harness.ViewModel.StartMarkAsync();

        Assert.Equal(RunMode.MarkOriginals, harness.Batch.ReceivedMode);
        Assert.False(harness.ViewModel.IsOverwriteOriginals);
        Assert.False(harness.ViewModel.OpenOutputCommand.CanExecute(null));
    }

    [Fact]
    public async Task Failed_safe_copy_preflight_stays_ready_and_explains_the_failure()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia(availableBytes: 0);

        await harness.ViewModel.StartMarkAsync();

        Assert.False(harness.Batch.WasStarted);
        Assert.Equal(WorkspaceState.Ready, harness.ViewModel.State);
        Assert.Single(harness.Prompts.Errors);
        Assert.Contains("空间", harness.Prompts.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reset_clears_run_and_selection_state_and_restores_safe_copy()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        harness.ViewModel.IsOverwriteOriginals = true;

        harness.ViewModel.ResetCommand.Execute(null);

        Assert.Equal(WorkspaceState.Empty, harness.ViewModel.State);
        Assert.False(harness.ViewModel.IsOverwriteOriginals);
        Assert.Empty(harness.ViewModel.MediaItems);
        Assert.Empty(harness.ViewModel.Results);
        Assert.Equal("", harness.ViewModel.OutputPath);
        Assert.Equal(0, harness.ViewModel.CompletedCount);
    }

    [Fact]
    public async Task Completed_actions_open_the_output_log_and_settings_through_shell_ports()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        await harness.ViewModel.StartMarkAsync();

        harness.ViewModel.OpenOutputCommand.Execute(null);
        harness.ViewModel.OpenLogCommand.Execute(null);
        harness.ViewModel.OpenSettingsCommand.Execute(null);
        await Task.Yield();

        Assert.Equal(
            [@"D:\EMKE 已标记\商品", @"D:\运行记录\run.csv"],
            harness.Shell.OpenedPaths);
        Assert.Equal(1, harness.Shell.SettingsOpenCount);
    }

    [Fact]
    public async Task Missing_batch_log_is_visible_and_disables_open_log()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        harness.Batch.LogWritten = false;

        await harness.ViewModel.StartMarkAsync();

        Assert.False(harness.ViewModel.OpenLogCommand.CanExecute(null));
        Assert.Contains("CSV", harness.ViewModel.SummaryMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Batch_exception_reaches_the_command_exception_callback()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        harness.Batch.Exception = new IOException("batch failed");

        harness.ViewModel.StartMarkCommand.Execute(null);
        await harness.Prompts.Prompted.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Contains("batch failed", Assert.Single(harness.Prompts.Errors), StringComparison.Ordinal);
        Assert.Equal(WorkspaceState.Completed, harness.ViewModel.State);
        Assert.False(harness.ViewModel.IsOverwriteOriginals);
    }

    [Fact]
    public async Task Add_file_command_uses_selection_service_and_toggle_details_changes_state()
    {
        MainWindowHarness harness = MainWindowHarness.Empty();
        harness.Paths.File(@"D:\a.jpg", 10);
        harness.Selection.Files = [@"D:\a.jpg"];

        await harness.ViewModel.AddFilesCommand.ExecuteAsync();
        harness.ViewModel.ToggleDetailsCommand.Execute(null);

        Assert.Equal(WorkspaceState.Ready, harness.ViewModel.State);
        Assert.True(harness.ViewModel.IsDetailsExpanded);
    }

    [Fact]
    public async Task Delayed_file_selection_blocks_other_selection_and_run_commands()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        harness.Paths.File(@"D:\new.png", 40);
        harness.Selection.BlockFilesUntilReleased();

        Task selection = harness.ViewModel.AddFilesCommand.ExecuteAsync();
        await harness.Selection.FilesRequested;

        Assert.False(harness.ViewModel.AddFilesCommand.CanExecute(null));
        Assert.False(harness.ViewModel.AddFolderCommand.CanExecute(null));
        Assert.False(harness.ViewModel.StartMarkCommand.CanExecute(null));
        Assert.False(harness.ViewModel.VerifyOnlyCommand.CanExecute(null));

        await harness.ViewModel.StartMarkCommand.ExecuteAsync();
        Assert.False(harness.Batch.WasStarted);

        harness.Selection.ReleaseFiles([@"D:\new.png"]);
        await selection;

        Assert.Equal(WorkspaceState.Ready, harness.ViewModel.State);
        Assert.Equal(3, harness.ViewModel.MediaCount);
    }

    [Fact]
    public async Task Reset_invalidates_a_delayed_file_selection_result()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        harness.Paths.File(@"D:\stale.jpg", 40);
        harness.Selection.BlockFilesUntilReleased();

        Task selection = harness.ViewModel.AddFilesCommand.ExecuteAsync();
        await harness.Selection.FilesRequested;
        harness.ViewModel.ResetCommand.Execute(null);

        harness.Selection.ReleaseFiles([@"D:\stale.jpg"]);
        await selection;

        Assert.Equal(WorkspaceState.Empty, harness.ViewModel.State);
        Assert.Empty(harness.ViewModel.MediaItems);
    }

    [Fact]
    public async Task Multiple_output_roots_are_shown_honestly_and_opened_once_in_stable_order()
    {
        MainWindowHarness harness = MainWindowHarness.Empty();
        harness.Paths
            .Directory(@"E:\营销")
            .File(@"E:\营销\b.png", 20)
            .Directory(@"D:\商品")
            .File(@"D:\商品\a.jpg", 10);
        await harness.ViewModel.AddPathsAsync([@"E:\营销", @"D:\商品"]);

        Assert.Equal("多个输出位置（2）", harness.ViewModel.OutputPath);

        await harness.ViewModel.StartMarkAsync();
        await harness.ViewModel.OpenOutputCommand.ExecuteAsync();

        Assert.Equal(
            [@"D:\EMKE 已标记\商品", @"E:\EMKE 已标记\营销"],
            harness.Shell.OpenedPaths);
    }
}

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task Concurrent_execution_is_rejected_until_the_first_execution_finishes()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var command = new AsyncRelayCommand(async () =>
        {
            calls++;
            await release.Task;
        });

        Task first = command.ExecuteAsync();
        Task second = command.ExecuteAsync();

        Assert.Equal(1, calls);
        Assert.False(command.CanExecute(null));
        Assert.True(second.IsCompletedSuccessfully);

        release.SetResult();
        await first;
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task Execution_exception_is_delivered_to_the_supplied_callback()
    {
        Exception? delivered = null;
        var command = new AsyncRelayCommand(
            () => throw new InvalidOperationException("command failed"),
            onException: exception =>
            {
                delivered = exception;
                return Task.CompletedTask;
            });

        await command.ExecuteAsync();

        Assert.IsType<InvalidOperationException>(delivered);
        Assert.Equal("command failed", delivered!.Message);
    }
}
