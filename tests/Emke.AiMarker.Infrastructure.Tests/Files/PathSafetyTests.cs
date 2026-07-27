using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Discovery;
using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Core.Processing;
using Emke.AiMarker.Infrastructure.Files;
using Emke.AiMarker.Infrastructure.Tests.TestSupport;

namespace Emke.AiMarker.Infrastructure.Tests.Files;

public sealed class PathSafetyTests
{
    [Fact]
    public void Guard_rejects_a_dangling_reparse_leaf()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("该受控符号链接用例仅提供非 Windows 逻辑证据。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        string missingTarget = Path.Combine(workspace.Root, "missing.jpg");
        string link = workspace.CreateFileLink(
            "output/dangling.jpg",
            missingTarget);

        IOException exception = Assert.Throws<IOException>(
            () => new PathComponentGuard().EnsurePathAllowsMissing(link));

        Assert.Contains("重解析点", exception.Message);
    }

    [Fact]
    public void Scanner_rejects_direct_media_beneath_a_reparse_ancestor()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("该受控符号链接用例仅提供非 Windows 逻辑证据。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        string target = workspace.CreateDirectory("real-input");
        string source = workspace.CreateFile("real-input/商品.jpg");
        string link = workspace.CreateDirectoryLink("linked-input", target);
        string linkedSource = Path.Combine(link, Path.GetFileName(source));

        ScanResult result = new InputScanner(new PhysicalPathAccess()).Scan(
            [linkedSource]);

        Assert.Empty(result.Media);
        ScanIssue issue = Assert.Single(result.Issues);
        Assert.Contains("重解析点", issue.Error);
    }

    [Fact]
    public void Scanner_rejects_direct_directory_beneath_a_reparse_ancestor()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("该受控符号链接用例仅提供非 Windows 逻辑证据。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        string target = workspace.CreateDirectory("real-input");
        workspace.CreateFile("real-input/nested/商品.jpg");
        string link = workspace.CreateDirectoryLink("linked-input", target);

        ScanResult result = new InputScanner(new PhysicalPathAccess()).Scan(
            [Path.Combine(link, "nested")]);

        Assert.Empty(result.Media);
        ScanIssue issue = Assert.Single(result.Issues);
        Assert.Contains("重解析点", issue.Error);
    }

    [Fact]
    public void Storage_probe_rejects_output_reparse_before_native_query()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("该受控符号链接用例仅提供非 Windows 逻辑证据。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        string target = workspace.CreateDirectory("real-output");
        string link = workspace.CreateDirectoryLink("linked-output", target);
        int nativeQueries = 0;
        var probe = new WindowsStorageProbe(_ =>
        {
            nativeQueries++;
            return new FreeSpaceQueryResult(true, 1024, 0);
        });

        IOException exception = Assert.Throws<IOException>(
            () => probe.GetAvailableBytes(link));

        Assert.Contains("重解析点", exception.Message);
        Assert.Equal(0, nativeQueries);
    }

    [Fact]
    public void Storage_writability_probe_rejects_output_reparse()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("该受控符号链接用例仅提供非 Windows 逻辑证据。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        string target = workspace.CreateDirectory("real-output");
        string link = workspace.CreateDirectoryLink("linked-output", target);
        var probe = new WindowsStorageProbe(
            _ => new FreeSpaceQueryResult(true, 1024, 0));

        IOException exception = Assert.Throws<IOException>(
            () => probe.AssertWritable(link));

        Assert.Contains("重解析点", exception.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Fact]
    public void Copy_plan_rejects_an_output_reparse_component()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("该受控符号链接用例仅提供非 Windows 逻辑证据。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        string source = workspace.CreateFile("source/商品.jpg");
        string target = workspace.CreateDirectory("real-output");
        string link = workspace.CreateDirectoryLink("linked-output", target);
        string final = Path.Combine(link, "商品.jpg");
        string temp = Path.Combine(
            link,
            $".emke-ai-marker-{Guid.NewGuid():N}.tmp.jpg");
        var plan = new OutputPlanItem(
            source,
            "商品.jpg",
            final,
            temp,
            3);

        IOException exception = Assert.Throws<IOException>(
            () => new PhysicalCopyTransaction().ValidatePlan(
                plan,
                RunMode.MarkCopies));

        Assert.Contains("重解析点", exception.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(source));
    }

    [Fact]
    public void Storage_writability_probe_rechecks_a_just_created_directory()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("该受控符号链接用例仅提供非 Windows 逻辑证据。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        string output = Path.Combine(workspace.Root, "new-output");
        string displaced = Path.Combine(workspace.Root, "displaced-output");
        string foreign = workspace.CreateDirectory("foreign-output");
        var guard = new ReplacingDirectoryGuard(
            output,
            () =>
            {
                Directory.Move(output, displaced);
                Directory.CreateSymbolicLink(output, foreign);
            });
        var probe = new WindowsStorageProbe(
            _ => new FreeSpaceQueryResult(true, 1024, 0),
            guard);

        IOException exception = Assert.Throws<IOException>(
            () => probe.AssertWritable(output));

        Assert.Contains("重解析点", exception.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(foreign));
    }

    [Fact]
    public void Original_write_safety_rejects_a_reparse_ancestor()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("该受控符号链接用例仅提供非 Windows 逻辑证据。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        string target = workspace.CreateDirectory("real-input");
        string source = workspace.CreateFile("real-input/商品.jpg");
        string link = workspace.CreateDirectoryLink("linked-input", target);
        string linkedSource = Path.Combine(link, Path.GetFileName(source));
        var plan = new OutputPlanItem(
            linkedSource,
            "商品.jpg",
            linkedSource,
            linkedSource,
            3);

        IOException exception = Assert.Throws<IOException>(
            () => new WindowsFileSafety().Validate(plan));

        Assert.Contains("重解析点", exception.Message);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData(RunMode.MarkOriginals)]
    [InlineData(RunMode.VerifyOnly)]
    public async Task Original_and_verify_preflight_rejects_reparse_before_ExifTool(
        RunMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("该受控符号链接用例仅提供非 Windows 逻辑证据。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        string target = workspace.CreateDirectory("real-input");
        string source = workspace.CreateFile("real-input/商品.jpg");
        string link = workspace.CreateDirectoryLink("linked-input", target);
        string linkedSource = Path.Combine(link, Path.GetFileName(source));
        string final = Path.Combine(workspace.Root, "output", "商品.jpg");
        string temp = Path.Combine(
            Path.GetDirectoryName(final)!,
            $".emke-ai-marker-{Guid.NewGuid():N}.tmp.jpg");
        var plan = new OutputPlanItem(
            linkedSource,
            "商品.jpg",
            final,
            temp,
            3);
        var exif = new RejectingExifToolClient();
        var processor = new MediaProcessor(
            new PhysicalCopyTransaction(),
            exif,
            new WindowsFileSafety(),
            TimeProvider.System);

        ProcessResult result = await processor.ProcessAsync(
            plan,
            mode,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Failed, result.Status);
        Assert.Contains("重解析点", result.Error);
        Assert.Equal(0, exif.CallCount);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(source));
    }

    [Fact]
    public async Task Existing_final_symlink_is_rejected_before_ExifTool()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("该受控符号链接用例仅提供非 Windows 逻辑证据。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        OutputPlanItem plan = CreatePlan(workspace);
        string foreign = workspace.CreateFile("foreign/existing.jpg", [9]);
        workspace.CreateFileLink(
            Path.GetRelativePath(workspace.Root, plan.FinalPath),
            foreign);
        var exif = new RejectingExifToolClient();
        var processor = new MediaProcessor(
            new PhysicalCopyTransaction(),
            exif,
            new WindowsFileSafety(),
            TimeProvider.System);

        ProcessResult result = await processor.ProcessAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Failed, result.Status);
        Assert.Contains("重解析点", result.Error);
        Assert.Equal(0, exif.CallCount);
        Assert.Equal([9], File.ReadAllBytes(foreign));
    }

    [Fact]
    public async Task Prepare_rechecks_output_components_after_reservation_observer()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("该受控符号链接用例仅提供非 Windows 逻辑证据。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        OutputPlanItem plan = CreatePlan(workspace);
        string output = Path.GetDirectoryName(plan.FinalPath)!;
        string displaced = Path.Combine(workspace.Root, "displaced-output");
        string foreign = workspace.CreateDirectory("foreign-output");
        var transaction = new PhysicalCopyTransaction(
            afterReserve: _ =>
            {
                Directory.Move(output, displaced);
                Directory.CreateSymbolicLink(output, foreign);
            });

        IOException exception = await Assert.ThrowsAsync<IOException>(
            () => transaction.PrepareAsync(
                plan,
                RunMode.MarkCopies,
                TestContext.Current.CancellationToken));

        Assert.Contains("重解析点", exception.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(foreign));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(plan.SourcePath));
    }

    [Fact]
    public async Task Commit_rechecks_output_components_after_boundary_observer()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("该受控符号链接用例仅提供非 Windows 逻辑证据。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        OutputPlanItem plan = CreatePlan(workspace);
        string output = Path.GetDirectoryName(plan.FinalPath)!;
        string displaced = Path.Combine(workspace.Root, "displaced-output");
        string foreign = workspace.CreateDirectory("foreign-output");
        var transaction = new PhysicalCopyTransaction(
            atCommitBoundary: _ =>
            {
                Directory.Move(output, displaced);
                Directory.CreateSymbolicLink(output, foreign);
            });
        PreparedMedia media = await transaction.PrepareAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);
        await transaction.SealVerifiedAsync(
            media,
            TestContext.Current.CancellationToken);

        IOException exception = await Assert.ThrowsAsync<IOException>(
            () => transaction.CommitAsync(
                media,
                TestContext.Current.CancellationToken));

        Assert.Contains("重解析点", exception.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(foreign));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(plan.SourcePath));
        await transaction.RollbackAsync(media);
    }

    [Fact]
    public void Windows_junction_ancestor_is_rejected_by_scanner()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("仅在 Windows 上创建真实 junction。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        string target = workspace.CreateDirectory("real-input");
        string source = workspace.CreateFile("real-input/商品.jpg");
        string junction = workspace.CreateWindowsJunction(
            "junction-input",
            target);

        ScanResult result = new InputScanner(new PhysicalPathAccess()).Scan(
            [Path.Combine(junction, Path.GetFileName(source))]);

        Assert.Empty(result.Media);
        ScanIssue issue = Assert.Single(result.Issues);
        Assert.Contains("重解析点", issue.Error);
    }

    [Fact]
    public void Windows_output_junction_is_rejected_before_storage_query()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("仅在 Windows 上创建真实 junction。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        string target = workspace.CreateDirectory("real-output");
        string junction = workspace.CreateWindowsJunction(
            "junction-output",
            target);
        int nativeQueries = 0;
        var probe = new WindowsStorageProbe(_ =>
        {
            nativeQueries++;
            return new FreeSpaceQueryResult(true, 1024, 0);
        });

        IOException exception = Assert.Throws<IOException>(
            () => probe.GetAvailableBytes(junction));

        Assert.Contains("重解析点", exception.Message);
        Assert.Equal(0, nativeQueries);

        string source = workspace.CreateFile("source/商品.jpg");
        string final = Path.Combine(junction, "商品.jpg");
        string temp = Path.Combine(
            junction,
            $".emke-ai-marker-{Guid.NewGuid():N}.tmp.jpg");
        var plan = new OutputPlanItem(
            source,
            "商品.jpg",
            final,
            temp,
            3);

        IOException transactionException = Assert.Throws<IOException>(
            () => new PhysicalCopyTransaction().ValidatePlan(
                plan,
                RunMode.MarkCopies));

        Assert.Contains("重解析点", transactionException.Message);
    }

    [Fact]
    public async Task Windows_final_file_symlink_is_rejected_before_ExifTool_when_supported()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("仅在 Windows 上验证真实文件符号链接。");
        }

        using var workspace = new PathSafetyTestWorkspace();
        OutputPlanItem plan = CreatePlan(workspace);
        string foreign = workspace.CreateFile("foreign/existing.jpg", [9]);
        workspace.CreateFileLink(
            Path.GetRelativePath(workspace.Root, plan.FinalPath),
            foreign);
        var exif = new RejectingExifToolClient();
        var processor = new MediaProcessor(
            new PhysicalCopyTransaction(),
            exif,
            new WindowsFileSafety(),
            TimeProvider.System);

        ProcessResult result = await processor.ProcessAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Failed, result.Status);
        Assert.Contains("重解析点", result.Error);
        Assert.Equal(0, exif.CallCount);
    }

    private static OutputPlanItem CreatePlan(PathSafetyTestWorkspace workspace)
    {
        string source = workspace.CreateFile("source/商品.jpg");
        string final = Path.Combine(workspace.Root, "output", "商品.jpg");
        string temp = Path.Combine(
            Path.GetDirectoryName(final)!,
            $".emke-ai-marker-{Guid.NewGuid():N}.tmp.jpg");
        return new(source, "商品.jpg", final, temp, 3);
    }

    private sealed class RejectingExifToolClient : IExifToolClient
    {
        public int CallCount { get; private set; }

        public Task<string> GetVersionAsync(CancellationToken cancellationToken) =>
            Reject<string>();

        public Task<IReadOnlyList<string>> ReadSubjectsAsync(
            string path,
            CancellationToken cancellationToken) =>
            Reject<IReadOnlyList<string>>();

        public Task WriteMarkerAsync(
            string path,
            CancellationToken cancellationToken) =>
            Reject<object>();

        public Task WriteMarkerPreservingIdentityAsync(
            string path,
            CancellationToken cancellationToken) =>
            Reject<object>();

        public Task<ReadOnlyMemory<byte>> ReadRawXmpAsync(
            string path,
            CancellationToken cancellationToken) =>
            Reject<ReadOnlyMemory<byte>>();

        public Task<string> ReadImageDataHashAsync(
            string path,
            CancellationToken cancellationToken) =>
            Reject<string>();

        private Task<T> Reject<T>()
        {
            CallCount++;
            throw new IOException("ExifTool must not be called.");
        }
    }

    private sealed class ReplacingDirectoryGuard(
        string pathToReplace,
        Action replacement) : IPathComponentGuard
    {
        private readonly PathComponentGuard inner = new();
        private bool replaced;

        public string EnsureExistingPath(string path)
        {
            if (!replaced
                && string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(pathToReplace),
                    StringComparison.Ordinal))
            {
                replaced = true;
                replacement();
            }

            return inner.EnsureExistingPath(path);
        }

        public string EnsurePathAllowsMissing(string path) =>
            inner.EnsurePathAllowsMissing(path);
    }
}
