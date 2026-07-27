namespace Emke.AiMarker.App.ViewModels;

public sealed class SettingsViewModel(MainWindowViewModel mainWindow) : ObservableObject
{
    private readonly MainWindowViewModel mainWindow =
        mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));

    public bool IsOverwriteOriginals
    {
        get => mainWindow.IsOverwriteOriginals;
        set
        {
            if (value == mainWindow.IsOverwriteOriginals)
            {
                return;
            }

            mainWindow.IsOverwriteOriginals = value;
            OnPropertyChanged();
        }
    }
}
