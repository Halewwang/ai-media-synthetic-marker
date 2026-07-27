namespace Emke.AiMarker.Infrastructure.Windows;

public sealed class SingleInstanceGuard : IDisposable
{
    public const string ProductMutexName = @"Local\EMKE.AIMarker.2.x";

    private static readonly object ProcessGate = new();
    private static readonly HashSet<string> ProcessOwnedNames =
        new(StringComparer.Ordinal);

    private Mutex? mutex;
    private bool ownsMutex;
    private bool disposed;

    public SingleInstanceGuard(string name = ProductMutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ownsMutex)
        {
            return true;
        }

        lock (ProcessGate)
        {
            if (ProcessOwnedNames.Contains(Name))
            {
                return false;
            }

            mutex ??= new Mutex(initiallyOwned: false, Name);
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                return false;
            }

            ownsMutex = true;
            ProcessOwnedNames.Add(Name);
            return true;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        lock (ProcessGate)
        {
            if (ownsMutex)
            {
                ProcessOwnedNames.Remove(Name);
                mutex!.ReleaseMutex();
                ownsMutex = false;
            }

            mutex?.Dispose();
            mutex = null;
            disposed = true;
        }
    }
}
