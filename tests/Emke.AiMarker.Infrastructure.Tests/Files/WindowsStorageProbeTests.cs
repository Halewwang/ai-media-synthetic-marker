using Emke.AiMarker.Infrastructure.Files;

namespace Emke.AiMarker.Infrastructure.Tests.Files;

public sealed class WindowsStorageProbeTests
{
    [Fact]
    public void GetAvailableBytes_queries_normalized_unc_directory_through_native_seam()
    {
        string? queriedDirectory = null;
        var probe = new WindowsStorageProbe(directory =>
        {
            queriedDirectory = directory;
            return new FreeSpaceQueryResult(true, 4_294_967_296, 0);
        });

        long available = probe.GetAvailableBytes(@"\\server\share\待标记");

        Assert.Equal(4_294_967_296, available);
        Assert.Equal(@"\\server\share\待标记\", queriedDirectory);
    }

    [Fact]
    public void GetAvailableBytes_surfaces_native_failure_as_actionable_io_exception()
    {
        var probe = new WindowsStorageProbe(
            _ => new FreeSpaceQueryResult(false, 0, 5));

        IOException exception = Assert.Throws<IOException>(
            () => probe.GetAvailableBytes(@"\\server\share\待标记"));

        Assert.Contains("Windows 错误 5", exception.Message);
    }
}
