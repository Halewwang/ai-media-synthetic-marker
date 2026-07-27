namespace Emke.AiMarker.App.Services;

public interface IFileSelectionService
{
    Task<IReadOnlyList<string>> SelectFilesAsync();

    Task<IReadOnlyList<string>> SelectFoldersAsync();
}
