using Emke.AiMarker.Core.Discovery;
using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Core.Tests.TestSupport;

namespace Emke.AiMarker.Core.Tests.Discovery;

public sealed class InputScannerTests
{
    [Fact]
    public void Scan_is_recursive_deduplicated_stable_and_case_insensitive()
    {
        var paths = new FakePathAccess()
            .Directory(@"D:\商品")
            .File(@"D:\商品\B.MP4", 20)
            .File(@"D:\商品\a.JPG", 10)
            .Directory(@"D:\商品\子目录")
            .File(@"D:\商品\子目录\透明.PNG", 30)
            .File(@"D:\商品\忽略.txt", 2);

        ScanResult result = new InputScanner(paths).Scan(
            [@"D:\商品", @"D:\商品\a.JPG"]);

        Assert.Equal(
            [@"a.JPG", @"B.MP4", @"子目录\透明.PNG"],
            result.Media.Select(item => item.RelativePath));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Scan_rejects_reparse_directories()
    {
        var paths = new FakePathAccess()
            .Directory(@"D:\商品")
            .ReparseDirectory(@"D:\商品\联接")
            .File(@"D:\商品\联接\private.jpg", 10);

        ScanResult result = new InputScanner(paths).Scan([@"D:\商品"]);

        Assert.Empty(result.Media);
        Assert.Single(result.Issues);
        Assert.Contains("重解析点", result.Issues[0].Error);
    }

    [Fact]
    public void Scan_deduplicates_a_repeated_top_level_directory_before_reporting_children()
    {
        var paths = new FakePathAccess()
            .Directory(@"D:\商品")
            .ReparseDirectory(@"D:\商品\联接");

        ScanResult result = new InputScanner(paths).Scan([@"D:\商品", @"D:\商品"]);

        Assert.Single(result.Issues);
        Assert.Contains("重解析点", result.Issues[0].Error);
    }

    [Fact]
    public void Scan_rejects_drive_root_before_traversal()
    {
        var paths = new FakePathAccess().Directory(@"D:\");

        ScanResult result = new InputScanner(paths).Scan([@"D:\"]);

        Assert.Empty(result.Media);
        ScanIssue issue = Assert.Single(result.Issues);
        Assert.Contains("根目录", issue.Error);
    }

    [Fact]
    public void Scan_rejects_unc_share_root_before_traversal()
    {
        var paths = new FakePathAccess().Directory(@"\\server\share\");

        ScanResult result = new InputScanner(paths).Scan([@"\\server\share\"]);

        Assert.Empty(result.Media);
        ScanIssue issue = Assert.Single(result.Issues);
        Assert.Contains("根目录", issue.Error);
    }
}
