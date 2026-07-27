using System.Diagnostics;
using Emke.AiMarker.App.Services;

namespace Emke.AiMarker.App.Tests.Services;

public sealed class ShellServiceTests
{
    [Fact]
    public async Task Existing_path_is_opened_through_the_native_shell()
    {
        ProcessStartInfo? captured = null;
        var service = new ShellService(
            start: startInfo =>
            {
                captured = startInfo;
                return null;
            });

        await service.OpenPathAsync(Path.GetTempPath());

        Assert.NotNull(captured);
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), captured.FileName);
        Assert.True(captured.UseShellExecute);
    }

    [Fact]
    public async Task Missing_path_is_rejected_before_shell_launch()
    {
        bool launched = false;
        var service = new ShellService(
            start: _ =>
            {
                launched = true;
                return null;
            });
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"emke-missing-{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.OpenPathAsync(missing));

        Assert.False(launched);
    }

    [Fact]
    public async Task Settings_action_is_delegated_without_adding_dialog_behavior()
    {
        int calls = 0;
        var service = new ShellService(
            openSettings: () =>
            {
                calls++;
                return Task.CompletedTask;
            });

        await service.OpenSettingsAsync();

        Assert.Equal(1, calls);
    }
}
