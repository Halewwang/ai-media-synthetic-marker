namespace Emke.AiMarker.Core.Processing;

public sealed class StopController
{
    private int _requested;

    public bool IsStopRequested => Volatile.Read(ref _requested) == 1;

    public void RequestStop() => Interlocked.Exchange(ref _requested, 1);
}
