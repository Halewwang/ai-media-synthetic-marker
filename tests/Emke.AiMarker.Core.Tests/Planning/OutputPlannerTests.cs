using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Core.Planning;
using Emke.AiMarker.Core.Tests.TestSupport;

namespace Emke.AiMarker.Core.Tests.Planning;

public sealed class OutputPlannerTests
{
    [Fact]
    public void Folder_input_preserves_root_name_and_relative_structure()
    {
        var media = new DiscoveredMedia(
            @"D:\商品\春季\look.JPG",
            @"D:\商品",
            @"春季\look.JPG",
            ".JPG",
            100);

        OutputPlanItem item = OutputPlanner.Plan([media], customOutputRoot: null).Single();

        Assert.Equal(
            @"D:\EMKE 已标记\商品\春季\look.JPG",
            item.FinalPath);
        Assert.EndsWith(".JPG", item.TempPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".emke-ai-marker-", Path.GetFileName(item.TempPath));
    }

    [Fact]
    public void Folder_directly_below_drive_root_keeps_windows_separators()
    {
        var media = new DiscoveredMedia(
            @"D:\商品\look.JPG",
            @"D:\商品",
            "look.JPG",
            ".JPG",
            100);

        OutputPlanItem item = OutputPlanner.Plan([media], customOutputRoot: null).Single();

        Assert.Equal(
            @"D:\EMKE 已标记\商品\look.JPG",
            item.FinalPath);
    }

    [Fact]
    public void Single_file_input_keeps_a_unc_parent_when_using_default_output()
    {
        var media = new DiscoveredMedia(
            @"\\server\share\待标记\look.jpg",
            @"\\server\share\待标记\look.jpg",
            "look.jpg",
            ".jpg",
            100);

        OutputPlanItem item = OutputPlanner.Plan([media], customOutputRoot: null).Single();

        Assert.Equal(
            @"\\server\share\待标记\EMKE 已标记\look.jpg",
            item.FinalPath);
    }

    [Fact]
    public void Storage_preflight_requires_total_plus_larger_margin()
    {
        var plans = new[]
        {
            new OutputPlanItem("a", "a", @"D:\out\a.jpg", @"D:\out\.tmp.jpg", 1_000_000_000),
        };
        var storage = new FakeStorageProbe(availableBytes: 1_200_000_000);

        StorageCheck result = new StoragePreflight(storage).Check(plans);

        Assert.False(result.IsReady);
        Assert.Equal(1_268_435_456, result.RequiredBytes);
    }

    [Fact]
    public void Storage_preflight_probes_each_distinct_destination_before_returning_ready()
    {
        var plans = new[]
        {
            new OutputPlanItem("a", "a", @"D:\one\a.jpg", @"D:\one\.tmp.jpg", 1),
            new OutputPlanItem("b", "b", @"D:\two\b.jpg", @"D:\two\.tmp.jpg", 1),
            new OutputPlanItem("c", "c", @"D:\one\c.jpg", @"D:\one\.tmp.jpg", 1),
        };
        var storage = new FakeStorageProbe(availableBytes: 1_000_000_000);

        StorageCheck result = new StoragePreflight(storage).Check(plans);

        Assert.True(result.IsReady);
        Assert.Equal([@"D:\one", @"D:\two"], storage.WritableDirectories);
    }

    [Fact]
    public void Planning_duplicate_final_paths_throws_before_processing_can_overwrite()
    {
        var media = new[]
        {
            new DiscoveredMedia(@"D:\one\look.jpg", @"D:\one\look.jpg", "look.jpg", ".jpg", 1),
            new DiscoveredMedia(@"D:\two\look.jpg", @"D:\two\look.jpg", "look.jpg", ".jpg", 1),
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => OutputPlanner.Plan(media, @"D:\out"));

        Assert.Contains("输出路径冲突", exception.Message);
    }
}
