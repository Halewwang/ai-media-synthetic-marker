using Emke.AiMarker.Core.Contracts;
using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Core.Processing;
using Emke.AiMarker.Core.Tests.TestSupport;

namespace Emke.AiMarker.Core.Tests.Processing;

public sealed class MediaProcessorTests
{
    [Fact]
    public async Task Copy_mode_preserves_source_and_commits_only_after_verification()
    {
        var operationLog = new List<string>();
        var files = new FakeFileTransaction([1, 2, 3], operationLog);
        var exif = new FakeExifToolClient(
            beforeSubjects: ["existing"],
            afterSubjects: ["existing", MarkerContract.Marker],
            rawXmp: TestXmp.ValidBag("existing", MarkerContract.Marker),
            operationLog: operationLog);
        var processor = CreateProcessor(files, exif);
        OutputPlanItem plan = TestPlans.Copy("商品.jpg");

        ProcessResult result = await processor.ProcessAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Added, result.Status);
        Assert.Equal([1, 2, 3], files.SourceBytes);
        Assert.True(files.CommitCalled);
        Assert.False(files.RollbackCalled);
        Assert.Equal("已确认 rdf:Bag/rdf:li", result.Evidence.XmpStructure);
        int verificationIndex =
            operationLog.FindLastIndex(call => call.StartsWith("xmp:"));
        int sealedIndex = operationLog.IndexOf("verified");
        int commitIndex = operationLog.IndexOf("commit");
        Assert.True(verificationIndex >= 0);
        Assert.True(sealedIndex > verificationIndex);
        Assert.True(commitIndex > sealedIndex);
        Assert.True(files.SealVerifiedCalled);
        Assert.Contains($"write:{plan.TempPath}", exif.Calls);
        Assert.DoesNotContain($"write:{plan.SourcePath}", exif.Calls);
    }

    [Fact]
    public async Task Failed_verification_rolls_back_temp_and_keeps_source()
    {
        var files = new FakeFileTransaction([1, 2, 3]);
        var exif = new FakeExifToolClient(
            beforeSubjects: [],
            afterSubjects: [MarkerContract.Marker],
            rawXmp: TestXmp.RdfSeq(MarkerContract.Marker));
        var processor = CreateProcessor(files, exif);

        ProcessResult result = await processor.ProcessAsync(
            TestPlans.Copy("商品.jpg"),
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Failed, result.Status);
        Assert.False(files.CommitCalled);
        Assert.True(files.RollbackCalled);
        Assert.Equal([1, 2, 3], files.SourceBytes);
    }

    [Fact]
    public async Task Verify_only_returns_unmarked_without_writing_or_creating_output()
    {
        var files = new FakeFileTransaction([1, 2, 3]);
        var exif = new FakeExifToolClient(beforeSubjects: []);
        var safety = new FakeOriginalWriteSafety();
        var processor = CreateProcessor(files, exif, safety);

        ProcessResult result = await processor.ProcessAsync(
            TestPlans.Copy("商品.PNG"),
            RunMode.VerifyOnly,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Unmarked, result.Status);
        Assert.Equal(0, exif.WriteCount);
        Assert.False(files.CommitCalled);
        Assert.False(files.RollbackCalled);
        Assert.False(safety.ValidateCalled);
        Assert.Equal("", result.OutputPath);
    }

    [Fact]
    public async Task Exact_existing_marker_is_strictly_verified_and_copy_is_committed_once()
    {
        var files = new FakeFileTransaction([1, 2, 3]);
        var exif = new FakeExifToolClient(
            beforeSubjects: ["existing", MarkerContract.Marker],
            rawXmp: TestXmp.ValidBag("existing", MarkerContract.Marker));
        var processor = CreateProcessor(files, exif);
        OutputPlanItem plan = TestPlans.Copy("商品.jpeg");

        ProcessResult result = await processor.ProcessAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.AlreadyCompliant, result.Status);
        Assert.Equal(0, exif.WriteCount);
        Assert.True(files.SealVerifiedCalled);
        Assert.True(files.CommitCalled);
        Assert.Equal(plan.FinalPath, result.OutputPath);
    }

    [Fact]
    public async Task Marker_near_match_does_not_suppress_exact_append()
    {
        var files = new FakeFileTransaction([1, 2, 3]);
        var exif = new FakeExifToolClient(
            beforeSubjects: ["Contains-Synthetic-Performer"],
            afterSubjects: ["Contains-Synthetic-Performer", MarkerContract.Marker],
            rawXmp: TestXmp.ValidBag(
                "Contains-Synthetic-Performer",
                MarkerContract.Marker));
        var processor = CreateProcessor(files, exif);

        ProcessResult result = await processor.ProcessAsync(
            TestPlans.Copy("商品.mp4"),
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Added, result.Status);
        Assert.Equal(1, exif.WriteCount);
        Assert.Contains("Contains-Synthetic-Performer", result.Evidence.ActualValue);
        Assert.Contains(MarkerContract.Marker, result.Evidence.ActualValue);
    }

    [Fact]
    public async Task Existing_subjects_are_preserved_in_verified_evidence()
    {
        var files = new FakeFileTransaction([1, 2, 3]);
        var exif = new FakeExifToolClient(
            beforeSubjects: ["existing", "another"],
            afterSubjects: ["existing", "another", MarkerContract.Marker],
            rawXmp: TestXmp.ValidBag(
                "existing",
                "another",
                MarkerContract.Marker));
        var processor = CreateProcessor(files, exif);

        ProcessResult result = await processor.ProcessAsync(
            TestPlans.Copy("商品.jpg"),
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Added, result.Status);
        Assert.Equal(
            """["existing","another","contains-synthetic-performer"]""",
            result.Evidence.ActualValue);
    }

    [Fact]
    public async Task Copy_mode_rolls_back_when_a_duplicate_existing_subject_is_lost()
    {
        var files = new FakeFileTransaction([1, 2, 3]);
        var exif = new FakeExifToolClient(
            beforeSubjects: ["existing", "duplicate", "duplicate"],
            afterSubjects: ["existing", "duplicate", MarkerContract.Marker],
            rawXmp: TestXmp.ValidBag(
                "existing",
                "duplicate",
                MarkerContract.Marker));
        var processor = CreateProcessor(files, exif);

        ProcessResult result = await processor.ProcessAsync(
            TestPlans.Copy("商品.jpg"),
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Failed, result.Status);
        Assert.Contains("Subject", result.Error);
        Assert.True(files.RollbackCalled);
        Assert.False(files.SealVerifiedCalled);
        Assert.False(files.CommitCalled);
    }

    [Fact]
    public async Task Original_mode_fails_when_subject_preservation_changes_case()
    {
        var files = new FakeFileTransaction([1, 2, 3]);
        var exif = new FakeExifToolClient(
            beforeSubjects: ["KeepCase"],
            afterSubjects: ["keepcase", MarkerContract.Marker],
            rawXmp: TestXmp.ValidBag("keepcase", MarkerContract.Marker));
        var safety = new FakeOriginalWriteSafety();
        var processor = CreateProcessor(files, exif, safety);

        ProcessResult result = await processor.ProcessAsync(
            TestPlans.Copy("商品.jpg"),
            RunMode.MarkOriginals,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Failed, result.Status);
        Assert.Contains("Subject", result.Error);
        Assert.True(safety.ValidateCalled);
        Assert.Equal(1, exif.WriteCount);
        Assert.False(files.CommitCalled);
    }

    [Fact]
    public async Task Prewrite_subject_snapshot_survives_a_client_reusing_its_list()
    {
        var files = new FakeFileTransaction([1, 2, 3]);
        var exif = new FakeExifToolClient(
            beforeSubjects: ["existing"],
            afterSubjects: [MarkerContract.Marker],
            rawXmp: TestXmp.ValidBag(MarkerContract.Marker),
            mutateSubjectsInPlace: true);
        var processor = CreateProcessor(files, exif);

        ProcessResult result = await processor.ProcessAsync(
            TestPlans.Copy("商品.jpg"),
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Failed, result.Status);
        Assert.Contains("Subject", result.Error);
        Assert.True(files.RollbackCalled);
        Assert.False(files.CommitCalled);
    }

    [Fact]
    public async Task Original_mode_validates_safety_immediately_before_write()
    {
        var operationLog = new List<string>();
        var files = new FakeFileTransaction([1, 2, 3]);
        var exif = new FakeExifToolClient(
            beforeSubjects: [],
            afterSubjects: [MarkerContract.Marker],
            rawXmp: TestXmp.ValidBag(MarkerContract.Marker),
            operationLog: operationLog);
        var safety = new FakeOriginalWriteSafety(operationLog);
        var processor = CreateProcessor(files, exif, safety);
        OutputPlanItem plan = TestPlans.Copy("商品.jpg");

        ProcessResult result = await processor.ProcessAsync(
            plan,
            RunMode.MarkOriginals,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Added, result.Status);
        Assert.True(safety.ValidateCalled);
        Assert.Equal(1, exif.WriteCount);
        Assert.False(files.CommitCalled);
        Assert.False(files.RollbackCalled);
        int safetyIndex = operationLog.IndexOf("safety");
        Assert.True(safetyIndex >= 0);
        Assert.Equal($"write:{plan.SourcePath}", operationLog[safetyIndex + 1]);
    }

    [Fact]
    public async Task Unsafe_original_returns_failure_without_write()
    {
        var files = new FakeFileTransaction([1, 2, 3]);
        var exif = new FakeExifToolClient(beforeSubjects: []);
        var safety = new FakeOriginalWriteSafety { ThrowOnValidate = true };
        var processor = CreateProcessor(files, exif, safety);

        ProcessResult result = await processor.ProcessAsync(
            TestPlans.Copy("商品.jpg"),
            RunMode.MarkOriginals,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Failed, result.Status);
        Assert.Equal(0, exif.WriteCount);
        Assert.Contains("unsafe original", result.Error);
    }

    [Theory]
    [InlineData(ExifFailurePoint.InitialRead)]
    [InlineData(ExifFailurePoint.Write)]
    [InlineData(ExifFailurePoint.ReadAfterWrite)]
    [InlineData(ExifFailurePoint.RawXmp)]
    [InlineData(ExifFailurePoint.Version)]
    public async Task Every_copy_operation_failure_rolls_back_owned_work(
        ExifFailurePoint failurePoint)
    {
        var files = new FakeFileTransaction([1, 2, 3]);
        var exif = new FakeExifToolClient(
            beforeSubjects: [],
            afterSubjects: [MarkerContract.Marker],
            rawXmp: TestXmp.ValidBag(MarkerContract.Marker),
            failurePoint: failurePoint);
        var processor = CreateProcessor(files, exif);

        ProcessResult result = await processor.ProcessAsync(
            TestPlans.Copy("商品.jpg"),
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Failed, result.Status);
        Assert.True(files.RollbackCalled);
        Assert.False(files.CommitCalled);
    }

    [Fact]
    public async Task Commit_failure_is_a_file_failure_and_triggers_rollback()
    {
        var files = new FakeFileTransaction([1, 2, 3]) { ThrowOnCommit = true };
        var exif = new FakeExifToolClient(
            beforeSubjects: [],
            afterSubjects: [MarkerContract.Marker],
            rawXmp: TestXmp.ValidBag(MarkerContract.Marker));
        var processor = CreateProcessor(files, exif);

        ProcessResult result = await processor.ProcessAsync(
            TestPlans.Copy("商品.jpg"),
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessStatus.Failed, result.Status);
        Assert.True(files.CommitCalled);
        Assert.True(files.RollbackCalled);
        Assert.Contains("commit failed", result.Error);
    }

    [Fact]
    public async Task Compliant_existing_output_is_returned_without_preparing_temp()
    {
        OutputPlanItem plan = TestPlans.Copy("商品.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(plan.FinalPath)!);
        await File.WriteAllBytesAsync(
            plan.FinalPath,
            [9, 8, 7],
            TestContext.Current.CancellationToken);
        try
        {
            var files = new FakeFileTransaction([1, 2, 3]);
            var exif = new FakeExifToolClient(
                beforeSubjects: [MarkerContract.Marker],
                rawXmp: TestXmp.ValidBag(MarkerContract.Marker));
            var processor = CreateProcessor(files, exif);

            ProcessResult result = await processor.ProcessAsync(
                plan,
                RunMode.MarkCopies,
                TestContext.Current.CancellationToken);

            Assert.Equal(ProcessStatus.OutputAlreadyCompliant, result.Status);
            Assert.False(files.PrepareCalled);
            Assert.False(files.CommitCalled);
            Assert.False(files.RollbackCalled);
            Assert.Equal(
                [9, 8, 7],
                await File.ReadAllBytesAsync(
                    plan.FinalPath,
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(
                Directory.GetParent(Path.GetDirectoryName(plan.SourcePath)!)!.FullName,
                recursive: true);
        }
    }

    [Fact]
    public async Task Noncompliant_existing_output_is_target_conflict_without_temp_creation()
    {
        OutputPlanItem plan = TestPlans.Copy("商品.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(plan.FinalPath)!);
        await File.WriteAllBytesAsync(
            plan.FinalPath,
            [9, 8, 7],
            TestContext.Current.CancellationToken);
        try
        {
            var files = new FakeFileTransaction([1, 2, 3]);
            var exif = new FakeExifToolClient(beforeSubjects: []);
            var processor = CreateProcessor(files, exif);

            ProcessResult result = await processor.ProcessAsync(
                plan,
                RunMode.MarkCopies,
                TestContext.Current.CancellationToken);

            Assert.Equal(ProcessStatus.Failed, result.Status);
            Assert.Contains("目标冲突", result.Error);
            Assert.False(files.PrepareCalled);
            Assert.Equal(
                [9, 8, 7],
                await File.ReadAllBytesAsync(
                    plan.FinalPath,
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(
                Directory.GetParent(Path.GetDirectoryName(plan.SourcePath)!)!.FullName,
                recursive: true);
        }
    }

    [Fact]
    public async Task Field_only_existing_output_is_target_conflict_when_bag_structure_fails()
    {
        OutputPlanItem plan = TestPlans.Copy("商品.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(plan.FinalPath)!);
        await File.WriteAllBytesAsync(
            plan.FinalPath,
            [9, 8, 7],
            TestContext.Current.CancellationToken);
        try
        {
            var files = new FakeFileTransaction([1, 2, 3]);
            var exif = new FakeExifToolClient(
                beforeSubjects: [MarkerContract.Marker],
                rawXmp: TestXmp.RdfSeq(MarkerContract.Marker));
            var processor = CreateProcessor(files, exif);

            ProcessResult result = await processor.ProcessAsync(
                plan,
                RunMode.MarkCopies,
                TestContext.Current.CancellationToken);

            Assert.Equal(ProcessStatus.Failed, result.Status);
            Assert.Equal(VerificationResult.Failed, result.Evidence.Result);
            Assert.Contains("目标冲突", result.Error);
            Assert.False(files.PrepareCalled);
        }
        finally
        {
            Directory.Delete(
                Directory.GetParent(Path.GetDirectoryName(plan.SourcePath)!)!.FullName,
                recursive: true);
        }
    }

    [Fact]
    public async Task Started_file_operations_ignore_batch_stop_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var files = new FakeFileTransaction([1, 2, 3]);
        var exif = new FakeExifToolClient(
            beforeSubjects: [],
            afterSubjects: [MarkerContract.Marker],
            rawXmp: TestXmp.ValidBag(MarkerContract.Marker));
        var processor = CreateProcessor(files, exif);

        ProcessResult result = await processor.ProcessAsync(
            TestPlans.Copy("商品.jpg"),
            RunMode.MarkCopies,
            cancellation.Token);

        Assert.Equal(ProcessStatus.Added, result.Status);
        Assert.All(
            exif.CancellationTokens,
            token => Assert.Equal(CancellationToken.None, token));
        Assert.All(
            files.CancellationTokens,
            token => Assert.Equal(CancellationToken.None, token));
    }

    private static MediaProcessor CreateProcessor(
        FakeFileTransaction files,
        FakeExifToolClient exif,
        FakeOriginalWriteSafety? safety = null) =>
        new(files, exif, safety ?? new FakeOriginalWriteSafety(), new FixedTimeProvider());
}
