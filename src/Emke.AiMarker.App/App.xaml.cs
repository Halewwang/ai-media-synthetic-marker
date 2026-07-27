using Emke.AiMarker.App.Services;
using Emke.AiMarker.App.ViewModels;
using Emke.AiMarker.App.Views;
using Emke.AiMarker.Core.Discovery;
using Emke.AiMarker.Core.Planning;
using Emke.AiMarker.Core.Processing;
using Emke.AiMarker.Infrastructure.ExifTool;
using Emke.AiMarker.Infrastructure.Files;
using Emke.AiMarker.Infrastructure.Logging;
using Emke.AiMarker.Infrastructure.Windows;
using System.IO;
using System.Windows;

namespace Emke.AiMarker.App;

public partial class App : Application
{
    private SingleInstanceGuard? singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (SelfTestArguments.IsRequested(e.Args))
        {
            int selfTestExitCode = RunSelfTest(e.Args);
            Shutdown(selfTestExitCode);
            return;
        }

        base.OnStartup(e);

        var text = new ResourceAppText();
        singleInstance = new SingleInstanceGuard();
        if (!singleInstance.TryAcquire())
        {
            MessageBox.Show(
                text.Get("AlreadyRunningMessage"),
                text.Get("AppName"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        var fileSelection = new FileSelectionService(text);
        MainWindowViewModel? viewModel = null;
        var shell = new ShellService(
            openSettings: () =>
            {
                if (viewModel is null)
                {
                    return Task.CompletedTask;
                }

                var dialog = new SettingsDialog
                {
                    DataContext = new SettingsViewModel(viewModel),
                    Owner = MainWindow,
                };
                dialog.ShowDialog();
                return Task.CompletedTask;
            });
        var prompts = new UserPromptService(text, () => MainWindow);
        var exifTool = new ExifToolClient(ExifToolPath(), new ProcessRunner());
        var mediaProcessor = new MediaProcessor(
            new PhysicalCopyTransaction(),
            exifTool,
            new WindowsFileSafety(),
            TimeProvider.System);
        var batchProcessor = new BatchProcessor(
            mediaProcessor,
            new CsvRunLogWriter());
        viewModel = new MainWindowViewModel(
            new InputScanner(new PhysicalPathAccess()),
            new StoragePreflight(new WindowsStorageProbe()),
            batchProcessor,
            fileSelection,
            prompts,
            shell,
            text,
            LogDirectory());
        var window = new MainWindow(prompts)
        {
            DataContext = viewModel,
        };

        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        singleInstance?.Dispose();
        singleInstance = null;
        base.OnExit(e);
    }

    private static string ExifToolPath()
    {
        string? configured = Environment.GetEnvironmentVariable("EMKE_EXIFTOOL");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "exiftool", "exiftool.exe")
            : Path.GetFullPath(configured);
    }

    private static int RunSelfTest(IReadOnlyList<string> arguments)
    {
        if (!SelfTestArguments.TryParse(
                arguments,
                out string reportPath,
                out string error))
        {
            if (SelfTestArguments.TryGetAbsoluteReportPath(
                    arguments,
                    out string candidate))
            {
                Task.Run(() => SelfTestService.TryWriteFailureReportAtRequestedPathAsync(
                        candidate,
                        new ArgumentException(error)))
                    .GetAwaiter()
                    .GetResult();
            }

            return 1;
        }

        try
        {
            string executable = Path.Combine(
                AppContext.BaseDirectory,
                "exiftool",
                "exiftool.exe");
            var service = new SelfTestService(
                typeof(App).Assembly,
                Path.GetDirectoryName(executable)!,
                Path.Combine(AppContext.BaseDirectory, "exiftool.lock.json"),
                new ExifToolClient(executable, new ProcessRunner()),
                resourceExists: resource =>
                    GetResourceStream(new Uri(
                        $"pack://application:,,,/{resource}",
                        UriKind.Absolute)) is not null);
            return Task.Run(() => service.RunAsync(reportPath))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            Task.Run(() => SelfTestService.TryWriteFailureReportAtRequestedPathAsync(
                    reportPath,
                    exception))
                .GetAwaiter()
                .GetResult();
            return 1;
        }
    }

    private static string LogDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EMKE",
            "AI Marker",
            "Logs");

}
