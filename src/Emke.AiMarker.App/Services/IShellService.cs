namespace Emke.AiMarker.App.Services;

public interface IShellService
{
    Task OpenPathAsync(string path);

    Task OpenSettingsAsync();
}
