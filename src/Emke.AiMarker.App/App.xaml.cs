using Emke.AiMarker.App.Services;
using Emke.AiMarker.App.ViewModels;
using Emke.AiMarker.Core.Discovery;
using Emke.AiMarker.Core.Planning;
using Emke.AiMarker.Core.Processing;
using Emke.AiMarker.Infrastructure.ExifTool;
using Emke.AiMarker.Infrastructure.Files;
using Emke.AiMarker.Infrastructure.Logging;
using System.IO;
using System.Windows;

namespace Emke.AiMarker.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var text = new ResourceAppText();
        var fileSelection = new FileSelectionService(text);
        var shell = new ShellService();
        var prompts = new MessageBoxPromptService(text);
        var exifTool = new ExifToolClient(ExifToolPath(), new ProcessRunner());
        var mediaProcessor = new MediaProcessor(
            new PhysicalCopyTransaction(),
            exifTool,
            new WindowsFileSafety(),
            TimeProvider.System);
        var batchProcessor = new BatchProcessor(
            mediaProcessor,
            new CsvRunLogWriter());
        var viewModel = new MainWindowViewModel(
            new InputScanner(new PhysicalPathAccess()),
            new StoragePreflight(new WindowsStorageProbe()),
            batchProcessor,
            fileSelection,
            prompts,
            shell,
            text,
            LogDirectory());
        var window = new MainWindow
        {
            DataContext = viewModel,
        };

        MainWindow = window;
        window.Show();
    }

    private static string ExifToolPath()
    {
        string? configured = Environment.GetEnvironmentVariable("EMKE_EXIFTOOL");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "exiftool", "exiftool.exe")
            : Path.GetFullPath(configured);
    }

    private static string LogDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EMKE",
            "AI Marker",
            "Logs");

    private sealed class MessageBoxPromptService(IAppText text) : IUserPromptService
    {
        private readonly IAppText text =
            text ?? throw new ArgumentNullException(nameof(text));

        public Task ShowErrorAsync(string message)
        {
            MessageBox.Show(
                message,
                text.Get("ErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return Task.CompletedTask;
        }
    }
}
