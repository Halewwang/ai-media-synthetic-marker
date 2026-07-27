using System.Diagnostics;
using System.IO;

namespace Emke.AiMarker.App.Services;

public sealed class ShellService : IShellService
{
    private readonly Func<ProcessStartInfo, Process?> start;
    private readonly Func<Task> openSettings;

    public ShellService(
        Func<ProcessStartInfo, Process?>? start = null,
        Func<Task>? openSettings = null)
    {
        this.start = start ?? Process.Start;
        this.openSettings = openSettings ?? (() => Task.CompletedTask);
    }

    public Task OpenPathAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The requested shell path does not exist.",
                fullPath);
        }

        _ = start(new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true,
        });
        return Task.CompletedTask;
    }

    public Task OpenSettingsAsync() => openSettings();
}
