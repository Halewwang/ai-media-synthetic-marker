using Emke.AiMarker.App.Views;
using System.Windows;

namespace Emke.AiMarker.App.Services;

public sealed class UserPromptService(
    IAppText text,
    Func<Window?> ownerProvider) : IUserPromptService
{
    private readonly IAppText text =
        text ?? throw new ArgumentNullException(nameof(text));
    private readonly Func<Window?> ownerProvider =
        ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));

    public Task ShowErrorAsync(string message)
    {
        MessageBox.Show(
            ownerProvider(),
            message,
            text.Get("ErrorTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmOriginalWriteAsync(int count) =>
        Task.FromResult(ShowConfirmation(
            text.Get("OriginalWriteConfirmationTitle"),
            text.Format("OriginalWriteConfirmationFormat", count),
            text.Get("ConfirmModifyOriginalsButton"),
            text.Get("CancelButton")));

    public Task<bool> ConfirmSafeStopForCloseAsync() =>
        Task.FromResult(ShowConfirmation(
            text.Get("RunningCloseTitle"),
            text.Get("RunningCloseWarning"),
            text.Get("SafeStopAndWaitButton"),
            text.Get("ContinueProcessingButton")));

    private bool ShowConfirmation(
        string title,
        string message,
        string affirmative,
        string cancel)
    {
        var dialog = new ConfirmationDialog(
            title,
            message,
            affirmative,
            cancel);
        Window? owner = ownerProvider();
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true;
    }
}
