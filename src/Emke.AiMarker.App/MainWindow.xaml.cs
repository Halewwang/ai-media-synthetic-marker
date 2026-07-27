using Emke.AiMarker.App.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace Emke.AiMarker.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
