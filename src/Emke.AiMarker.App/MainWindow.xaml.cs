using Emke.AiMarker.App.Services;
using Emke.AiMarker.App.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Emke.AiMarker.App;

public partial class MainWindow : Window
{
    private readonly IUserPromptService prompts;
    private bool closeWorkflowActive;
    private bool closeAllowed;

    public MainWindow(IUserPromptService prompts)
    {
        this.prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
        InitializeComponent();
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeAllowed
            || DataContext is not MainWindowViewModel
            {
                State: WorkspaceState.Running,
            } viewModel)
        {
            return;
        }

        e.Cancel = true;
        if (closeWorkflowActive)
        {
            return;
        }

        closeWorkflowActive = true;
        _ = HandleRunningCloseAsync(viewModel);
    }

    private async Task HandleRunningCloseAsync(MainWindowViewModel viewModel)
    {
        bool closePosted = false;
        try
        {
            if (!await prompts.ConfirmSafeStopForCloseAsync())
            {
                return;
            }

            await viewModel.RequestSafeStopAndWaitAsync();
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(CompleteDeferredClose));
            closePosted = true;
        }
        catch (Exception exception)
        {
            await ShowCloseErrorAsync(exception);
        }
        finally
        {
            if (!closePosted)
            {
                closeWorkflowActive = false;
            }
        }
    }

    private void CompleteDeferredClose()
    {
        try
        {
            if (!IsLoaded
                || Dispatcher.HasShutdownStarted
                || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            closeAllowed = true;
            Close();
        }
        catch (Exception exception)
        {
            closeAllowed = false;
            _ = ShowCloseErrorAsync(exception);
        }
        finally
        {
            closeWorkflowActive = false;
        }
    }

    private async Task ShowCloseErrorAsync(Exception exception)
    {
        string detail = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
        try
        {
            await prompts.ShowErrorAsync(detail);
        }
        catch
        {
            // Closing remains cancelled if the error surface itself is unavailable.
        }
    }

    private void DropTarget_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanAcceptDrop(e.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void DropTarget_OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!CanAcceptDrop(e.Data)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.AddPathsAsync(paths);
    }

    private bool CanAcceptDrop(IDataObject data) =>
        DataContext is MainWindowViewModel
        {
            State: WorkspaceState.Empty or WorkspaceState.Ready,
        }
        && data.GetDataPresent(DataFormats.FileDrop);
}
