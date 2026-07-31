using Emke.AiMarker.Infrastructure.Files;
using Emke.AiMarker.Infrastructure.Tests.TestSupport;

namespace Emke.AiMarker.Infrastructure.Tests.Files;

public sealed class WindowsStorageProbeTests
{
    [Fact]
    public void GetAvailableBytes_queries_existing_ancestor_when_output_directory_is_missing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("仅在 Windows 上验证真实磁盘可用空间查询。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        string missingOutput = Path.Combine(
            workspace.Root,
            "EMKE 已标记",
            "nested");

        long available = new WindowsStorageProbe()
            .GetAvailableBytes(missingOutput);

        Assert.True(available > 0);
        Assert.False(Directory.Exists(missingOutput));
    }

    [Fact]
    public void GetAvailableBytes_queries_normalized_unc_directory_through_native_seam()
    {
        string? queriedDirectory = null;
        var probe = new WindowsStorageProbe(directory =>
        {
            queriedDirectory = directory;
            return new FreeSpaceQueryResult(true, 4_294_967_296, 0);
        }, new AllowAllPathComponentGuard());

        long available = probe.GetAvailableBytes(@"\\server\share\待标记");

        Assert.Equal(4_294_967_296, available);
        Assert.Equal(@"\\server\share\待标记\", queriedDirectory);
    }

    [Fact]
    public void GetAvailableBytes_surfaces_native_failure_as_actionable_io_exception()
    {
        var probe = new WindowsStorageProbe(
            _ => new FreeSpaceQueryResult(false, 0, 5),
            new AllowAllPathComponentGuard());

        IOException exception = Assert.Throws<IOException>(
            () => probe.GetAvailableBytes(@"\\server\share\待标记"));

        Assert.Contains("Windows 错误 5", exception.Message);
    }

    private sealed class AllowAllPathComponentGuard : IPathComponentGuard
    {
        public string EnsureExistingPath(string path) => path;

        public string EnsurePathAllowsMissing(string path) => path;
    }
}
