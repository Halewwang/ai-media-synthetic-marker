using System.Windows.Input;

namespace Emke.AiMarker.App.ViewModels;

public sealed class AsyncRelayCommand(
    Func<Task> execute,
    Func<bool>? canExecute = null,
    Func<Exception, Task>? onException = null) : ICommand
{
    private readonly Func<Task> execute =
        execute ?? throw new ArgumentNullException(nameof(execute));
    private readonly Func<bool>? canExecute = canExecute;
    private readonly Func<Exception, Task>? onException = onException;
    private int isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        Volatile.Read(ref isExecuting) == 0 && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null) || Interlocked.CompareExchange(ref isExecuting, 1, 0) != 0)
        {
            return;
        }

        NotifyCanExecuteChanged();
        try
        {
            await execute();
        }
        catch (Exception exception)
        {
            if (onException is null)
            {
                throw;
            }

            await onException(exception);
        }
        finally
        {
            Interlocked.Exchange(ref isExecuting, 0);
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
