using Emke.AiMarker.Infrastructure.Windows;

namespace Emke.AiMarker.Infrastructure.Tests.Windows;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void Default_guard_uses_the_exact_product_mutex_name()
    {
        using var guard = new SingleInstanceGuard();

        Assert.Equal(@"Local\EMKE.AIMarker.2.x", guard.Name);
    }

    [Fact]
    public void A_second_guard_in_the_same_process_cannot_acquire_the_name()
    {
        string name = $@"Local\EMKE.AIMarker.Tests.{Guid.NewGuid():N}";
        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
    }

    [Fact]
    public void Dispose_releases_the_name_for_a_later_guard()
    {
        string name = $@"Local\EMKE.AIMarker.Tests.{Guid.NewGuid():N}";
        using (var first = new SingleInstanceGuard(name))
        {
            Assert.True(first.TryAcquire());
        }

        using var later = new SingleInstanceGuard(name);
        Assert.True(later.TryAcquire());
    }

    [Fact]
    public void Repeated_acquire_on_the_owning_guard_is_idempotent()
    {
        string name = $@"Local\EMKE.AIMarker.Tests.{Guid.NewGuid():N}";
        using var guard = new SingleInstanceGuard(name);

        Assert.True(guard.TryAcquire());
        Assert.True(guard.TryAcquire());
    }

    [Fact]
    public void An_abandoned_named_mutex_is_acquired_as_the_new_owner()
    {
        string name = $@"Local\EMKE.AIMarker.Tests.{Guid.NewGuid():N}";
        using var keeper = new Mutex(initiallyOwned: false, name);
        using var acquired = new ManualResetEventSlim();
        var abandoningThread = new Thread(() =>
        {
            using var abandoned = new Mutex(initiallyOwned: false, name);
            abandoned.WaitOne();
            acquired.Set();
        });

        abandoningThread.Start();
        Assert.True(acquired.Wait(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken));
        Assert.True(abandoningThread.Join(TimeSpan.FromSeconds(5)));

        using var guard = new SingleInstanceGuard(name);
        Assert.True(guard.TryAcquire());
    }
}
