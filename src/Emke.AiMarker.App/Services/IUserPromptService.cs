namespace Emke.AiMarker.App.Services;

public interface IUserPromptService
{
    Task ShowErrorAsync(string message);

    Task<bool> ConfirmOriginalWriteAsync(int count);

    Task<bool> ConfirmSafeStopForCloseAsync();
}
