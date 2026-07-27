using Microsoft.Win32;

namespace Emke.AiMarker.App.Services;

public sealed class FileSelectionService(IAppText text) : IFileSelectionService
{
    private readonly IAppText text = text ?? throw new ArgumentNullException(nameof(text));

    public Task<IReadOnlyList<string>> SelectFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = text.Get("SupportedMediaDialogFilter"),
        };

        IReadOnlyList<string> paths = dialog.ShowDialog() == true
            ? dialog.FileNames
            : [];
        return Task.FromResult(paths);
    }

    public Task<IReadOnlyList<string>> SelectFoldersAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Multiselect = true,
        };

        IReadOnlyList<string> paths = dialog.ShowDialog() == true
            ? dialog.FolderNames
            : [];
        return Task.FromResult(paths);
    }
}
