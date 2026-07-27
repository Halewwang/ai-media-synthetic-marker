using Emke.AiMarker.App.Tests.TestSupport;
using Emke.AiMarker.App.ViewModels;
using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.App.Tests.ViewModels;

public sealed class AdvancedModeTests
{
    [Fact]
    public async Task Original_mode_requires_confirmation_every_run()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        harness.ViewModel.IsOverwriteOriginals = true;
        harness.Prompts.NextOriginalWriteConfirmation = false;

        await harness.ViewModel.StartMarkAsync();

        Assert.Equal(1, harness.Prompts.OriginalWriteConfirmationCount);
        Assert.Equal(2, harness.Prompts.LastOriginalWriteCount);
        Assert.False(harness.Batch.WasStarted);
        Assert.Equal(WorkspaceState.Ready, harness.ViewModel.State);

        harness.Prompts.NextOriginalWriteConfirmation = true;
        await harness.ViewModel.StartMarkAsync();
        Assert.Equal(2, harness.Prompts.OriginalWriteConfirmationCount);
        Assert.Equal(1, harness.Batch.StartCount);

        harness.ViewModel.IsOverwriteOriginals = true;
        harness.Prompts.NextOriginalWriteConfirmation = false;
        await harness.ViewModel.StartMarkAsync();

        Assert.Equal(3, harness.Prompts.OriginalWriteConfirmationCount);
        Assert.Equal(1, harness.Batch.StartCount);
    }

    [Fact]
    public async Task Accepted_original_run_uses_original_mode_and_resets_after_completion()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        harness.ViewModel.IsOverwriteOriginals = true;
        harness.Prompts.NextOriginalWriteConfirmation = true;

        await harness.ViewModel.StartMarkAsync();

        Assert.Equal(RunMode.MarkOriginals, harness.Batch.ReceivedMode);
        Assert.False(harness.ViewModel.IsOverwriteOriginals);
    }

    [Fact]
    public async Task Reset_restores_safe_copy_without_persisting_the_advanced_choice()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        harness.ViewModel.IsOverwriteOriginals = true;

        await harness.ViewModel.ResetCommand.ExecuteAsync();

        Assert.False(harness.ViewModel.IsOverwriteOriginals);
        Assert.Equal(WorkspaceState.Empty, harness.ViewModel.State);
    }

    [Fact]
    public void Settings_view_model_updates_the_main_window_per_run_choice()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        var settings = new SettingsViewModel(harness.ViewModel);

        settings.IsOverwriteOriginals = true;

        Assert.True(harness.ViewModel.IsOverwriteOriginals);
        Assert.True(settings.IsOverwriteOriginals);
    }

    [Fact]
    public async Task Safe_stop_and_wait_requests_stop_then_completes_with_the_batch()
    {
        MainWindowHarness harness = MainWindowHarness.ReadyWithMedia();
        harness.Batch.BlockUntilReleased();
        Task run = harness.ViewModel.StartMarkAsync();

        Task wait = harness.ViewModel.RequestSafeStopAndWaitAsync();

        Assert.True(harness.Batch.ReceivedStop!.IsStopRequested);
        Assert.False(wait.IsCompleted);

        harness.Batch.ReleaseStopped();
        await Task.WhenAll(run, wait);

        Assert.Equal(WorkspaceState.Completed, harness.ViewModel.State);
    }
}
