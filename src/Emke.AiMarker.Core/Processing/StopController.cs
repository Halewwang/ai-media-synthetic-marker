namespace Emke.AiMarker.Core.Processing;

public sealed class StopController(Action? beforeAdmissionAttempt = null)
{
    private readonly object _admissionGate = new();
    private readonly Action? _beforeAdmissionAttempt = beforeAdmissionAttempt;
    private int _requested;

    public bool IsStopRequested => Volatile.Read(ref _requested) == 1;

    public void RequestStop()
    {
        lock (_admissionGate)
        {
            Interlocked.Exchange(ref _requested, 1);
        }
    }

    public bool TryAdmit(CancellationToken cancellationToken, Action start)
    {
        ArgumentNullException.ThrowIfNull(start);
        _beforeAdmissionAttempt?.Invoke();

        lock (_admissionGate)
        {
            if (IsStopRequested || cancellationToken.IsCancellationRequested)
            {
                Interlocked.Exchange(ref _requested, 1);
                return false;
            }

            start();
            return true;
        }
    }
}
