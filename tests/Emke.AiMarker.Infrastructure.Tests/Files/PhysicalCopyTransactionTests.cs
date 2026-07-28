using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Infrastructure.Files;

namespace Emke.AiMarker.Infrastructure.Tests.Files;

public sealed class PhysicalCopyTransactionTests
{
    [Fact]
    public async Task Copy_prepare_creates_owned_temp_with_source_bytes_and_keeps_source()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        var transaction = new PhysicalCopyTransaction();

        PreparedMedia media = await transaction.PrepareAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        Assert.Equal(plan.TempPath, media.WorkingPath);
        Assert.Equal(
            [1, 2, 3],
            File.ReadAllBytes(plan.SourcePath));
        Assert.Equal(
            [1, 2, 3],
            File.ReadAllBytes(plan.TempPath));
        Assert.False(File.Exists(plan.FinalPath));

        await transaction.RollbackAsync(media);
        Assert.False(File.Exists(plan.TempPath));
    }

    [Theory]
    [InlineData(RunMode.MarkOriginals)]
    [InlineData(RunMode.VerifyOnly)]
    public async Task Noncopy_prepare_uses_source_without_creating_output(RunMode mode)
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        var transaction = new PhysicalCopyTransaction();

        PreparedMedia media = await transaction.PrepareAsync(
            plan,
            mode,
            TestContext.Current.CancellationToken);

        Assert.Equal(plan.SourcePath, media.WorkingPath);
        Assert.False(Directory.Exists(Path.GetDirectoryName(plan.FinalPath)));
        Assert.False(File.Exists(plan.TempPath));
        Assert.False(File.Exists(plan.FinalPath));
    }

    [Fact]
    public async Task Commit_moves_owned_temp_to_same_directory_without_overwrite()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        var transaction = new PhysicalCopyTransaction();
        PreparedMedia media = await transaction.PrepareAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);
        await using (var writer = new FileStream(
            plan.TempPath,
            new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous,
            }))
        {
            await writer.WriteAsync(
                new byte[] { 4, 5, 6 },
                TestContext.Current.CancellationToken);
        }

        await transaction.SealVerifiedAsync(
            media,
            TestContext.Current.CancellationToken);

        await transaction.CommitAsync(media, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(plan.TempPath));
        Assert.Equal(
            [4, 5, 6],
            await File.ReadAllBytesAsync(
                plan.FinalPath,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            [1, 2, 3],
            await File.ReadAllBytesAsync(
                plan.SourcePath,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Commit_never_overwrites_existing_final_output()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        var transaction = new PhysicalCopyTransaction();
        PreparedMedia media = await transaction.PrepareAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);
        await transaction.SealVerifiedAsync(
            media,
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            plan.FinalPath,
            [9, 8, 7],
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IOException>(
            () => transaction.CommitAsync(
                media,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            [9, 8, 7],
            await File.ReadAllBytesAsync(
                plan.FinalPath,
                TestContext.Current.CancellationToken));
        Assert.True(File.Exists(plan.TempPath));
        Assert.Equal(
            [1, 2, 3],
            await File.ReadAllBytesAsync(
                plan.SourcePath,
                TestContext.Current.CancellationToken));

        await transaction.RollbackAsync(media);
    }

    [Fact]
    public async Task Rollback_deletes_only_owned_temp_in_planned_destination()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        var transaction = new PhysicalCopyTransaction();
        PreparedMedia media = await transaction.PrepareAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        await transaction.RollbackAsync(media);

        Assert.False(File.Exists(plan.TempPath));
        Assert.True(File.Exists(plan.SourcePath));
        Assert.False(File.Exists(plan.FinalPath));
    }

    [Fact]
    public async Task Rollback_refuses_prefixed_file_outside_planned_destination()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        string foreignDirectory = Path.Combine(workspace.Root, "foreign");
        Directory.CreateDirectory(foreignDirectory);
        string foreignPath = Path.Combine(
            foreignDirectory,
            ".emke-ai-marker-foreign.tmp.jpg");
        await File.WriteAllBytesAsync(
            foreignPath,
            [6, 6, 6],
            TestContext.Current.CancellationToken);
        var media = new PreparedMedia(plan.SourcePath, foreignPath, plan.FinalPath);

        await new PhysicalCopyTransaction().RollbackAsync(media);

        Assert.True(File.Exists(foreignPath));
        Assert.True(File.Exists(plan.SourcePath));
    }

    [Fact]
    public async Task Rollback_never_deletes_source_or_final_even_with_owned_prefix_names()
    {
        using var workspace = new TemporaryWorkspace();
        string directory = Path.Combine(workspace.Root, "output");
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, ".emke-ai-marker-source.tmp.jpg");
        string final = Path.Combine(directory, ".emke-ai-marker-final.tmp.jpg");
        await File.WriteAllBytesAsync(
            source,
            [1],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            final,
            [2],
            TestContext.Current.CancellationToken);
        var media = new PreparedMedia(source, source, final);

        await new PhysicalCopyTransaction().RollbackAsync(media);

        Assert.True(File.Exists(source));
        Assert.True(File.Exists(final));
    }

    [Fact]
    public async Task Prepare_never_overwrites_an_existing_owned_temp()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        Directory.CreateDirectory(Path.GetDirectoryName(plan.TempPath)!);
        await File.WriteAllBytesAsync(
            plan.TempPath,
            [9, 8, 7],
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IOException>(
            () => new PhysicalCopyTransaction().PrepareAsync(
                plan,
                RunMode.MarkCopies,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            [9, 8, 7],
            await File.ReadAllBytesAsync(
                plan.TempPath,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            [1, 2, 3],
            await File.ReadAllBytesAsync(
                plan.SourcePath,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Copy_failure_removes_only_the_temp_created_by_that_prepare()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        var transaction = new PhysicalCopyTransaction((_, ownedDestination) =>
        {
            ownedDestination.Write([4, 5]);
            throw new IOException("simulated partial copy");
        });

        IOException exception = await Assert.ThrowsAsync<IOException>(
            () => transaction.PrepareAsync(
                plan,
                RunMode.MarkCopies,
                TestContext.Current.CancellationToken));

        Assert.Contains("simulated partial copy", exception.Message);
        Assert.False(File.Exists(plan.TempPath));
        Assert.False(File.Exists(plan.FinalPath));
        Assert.Equal(
            [1, 2, 3],
            await File.ReadAllBytesAsync(
                plan.SourcePath,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Foreign_file_winning_create_race_is_preserved()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        var transaction = new PhysicalCopyTransaction(
            copyToOwnedStream: (_, _) => throw new InvalidOperationException(
                "copy must not start after reservation loss"),
            beforeReserve: destination => File.WriteAllBytes(
                destination,
                [9, 8, 7]));

        await Assert.ThrowsAsync<IOException>(
            () => transaction.PrepareAsync(
                plan,
                RunMode.MarkCopies,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            [9, 8, 7],
            await File.ReadAllBytesAsync(
                plan.TempPath,
                TestContext.Current.CancellationToken));
        Assert.False(File.Exists(plan.FinalPath));
    }

    [Fact]
    public async Task Commit_substitution_is_blocked_on_Windows_or_refused_elsewhere()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        var substitution = new SubstitutionAttempt();
        var transaction = new PhysicalCopyTransaction();
        PreparedMedia media = await transaction.PrepareAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);
        await transaction.SealVerifiedAsync(
            media,
            TestContext.Current.CancellationToken);
        substitution.ReplaceWithForeignBytes(plan.TempPath);

        if (OperatingSystem.IsWindows())
        {
            Assert.False(substitution.DeleteSucceeded);
            Assert.False(substitution.Succeeded);
            Assert.IsType<IOException>(substitution.DeniedBy);

            await transaction.CommitAsync(
                media,
                TestContext.Current.CancellationToken);

            Assert.False(File.Exists(plan.TempPath));
            Assert.Equal(
                [1, 2, 3],
                await File.ReadAllBytesAsync(
                    plan.FinalPath,
                    TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.True(substitution.DeleteSucceeded);
            Assert.True(substitution.Succeeded);
            Assert.Null(substitution.DeniedBy);

            IOException exception = await Assert.ThrowsAsync<IOException>(
                () => transaction.CommitAsync(
                    media,
                    TestContext.Current.CancellationToken));

            Assert.Contains("所有权", exception.Message);
            Assert.False(File.Exists(plan.FinalPath));
            Assert.Equal(
                [9, 8, 7],
                await File.ReadAllBytesAsync(
                    plan.TempPath,
                    TestContext.Current.CancellationToken));
        }

        await transaction.RollbackAsync(media);
    }

    [Fact]
    public async Task Seal_substitution_is_blocked_on_Windows_or_refused_elsewhere()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        var substitution = new SubstitutionAttempt();
        var transaction = new PhysicalCopyTransaction();
        PreparedMedia media = await transaction.PrepareAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);
        substitution.ReplaceWithForeignBytes(plan.TempPath);

        if (OperatingSystem.IsWindows())
        {
            Assert.False(substitution.DeleteSucceeded);
            Assert.False(substitution.Succeeded);
            Assert.IsType<IOException>(substitution.DeniedBy);

            await transaction.SealVerifiedAsync(
                media,
                TestContext.Current.CancellationToken);

            Assert.True(File.Exists(plan.TempPath));
        }
        else
        {
            Assert.True(substitution.DeleteSucceeded);
            Assert.True(substitution.Succeeded);
            Assert.Null(substitution.DeniedBy);

            IOException exception = await Assert.ThrowsAsync<IOException>(
                () => transaction.SealVerifiedAsync(
                    media,
                    TestContext.Current.CancellationToken));

            Assert.Contains("所有权", exception.Message);
            Assert.Equal(
                [9, 8, 7],
                await File.ReadAllBytesAsync(
                    plan.TempPath,
                    TestContext.Current.CancellationToken));
            Assert.False(File.Exists(plan.FinalPath));
        }

        await transaction.RollbackAsync(media);

        if (OperatingSystem.IsWindows())
        {
            Assert.False(File.Exists(plan.TempPath));
            Assert.False(File.Exists(plan.FinalPath));
        }
    }

    [Fact]
    public async Task Commit_refuses_owned_temp_without_strict_verification_seal()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        var transaction = new PhysicalCopyTransaction();
        PreparedMedia media = await transaction.PrepareAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        IOException exception = await Assert.ThrowsAsync<IOException>(
            () => transaction.CommitAsync(
                media,
                TestContext.Current.CancellationToken));

        Assert.Contains("验证", exception.Message);
        Assert.False(File.Exists(plan.FinalPath));
        Assert.True(File.Exists(plan.TempPath));

        await transaction.RollbackAsync(media);
    }

    [Fact]
    public async Task Commit_boundary_blocks_substitution_on_Windows_and_never_commits_foreign_bytes()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        var substitution = new SubstitutionAttempt();
        var transaction = new PhysicalCopyTransaction(
            atCommitBoundary: substitution.ReplaceWithForeignBytes);
        PreparedMedia media = await transaction.PrepareAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);
        await transaction.SealVerifiedAsync(
            media,
            TestContext.Current.CancellationToken);

        if (OperatingSystem.IsWindows())
        {
            await transaction.CommitAsync(
                media,
                TestContext.Current.CancellationToken);

            Assert.False(substitution.DeleteSucceeded);
            Assert.False(substitution.Succeeded);
            Assert.IsType<IOException>(substitution.DeniedBy);
            Assert.False(File.Exists(plan.TempPath));
            Assert.Equal(
                [1, 2, 3],
                await File.ReadAllBytesAsync(
                    plan.FinalPath,
                    TestContext.Current.CancellationToken));
        }
        else
        {
            IOException exception = await Assert.ThrowsAsync<IOException>(
                () => transaction.CommitAsync(
                    media,
                    TestContext.Current.CancellationToken));

            Assert.True(substitution.DeleteSucceeded);
            Assert.True(substitution.Succeeded);
            Assert.Null(substitution.DeniedBy);
            Assert.Contains("所有权", exception.Message);
            Assert.False(File.Exists(plan.FinalPath));
            Assert.Equal(
                [9, 8, 7],
                await File.ReadAllBytesAsync(
                    plan.TempPath,
                    TestContext.Current.CancellationToken));
        }

        await transaction.RollbackAsync(media);
    }

    [Fact]
    public async Task Rollback_substitution_is_blocked_on_Windows_or_preserved_elsewhere()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        var substitution = new SubstitutionAttempt();
        var transaction = new PhysicalCopyTransaction();
        PreparedMedia media = await transaction.PrepareAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);
        substitution.ReplaceWithForeignBytes(plan.TempPath);

        await transaction.RollbackAsync(media);

        if (OperatingSystem.IsWindows())
        {
            Assert.False(substitution.DeleteSucceeded);
            Assert.False(substitution.Succeeded);
            Assert.IsType<IOException>(substitution.DeniedBy);
            Assert.False(File.Exists(plan.TempPath));
        }
        else
        {
            Assert.True(substitution.DeleteSucceeded);
            Assert.True(substitution.Succeeded);
            Assert.Null(substitution.DeniedBy);
            Assert.Equal(
                [9, 8, 7],
                await File.ReadAllBytesAsync(
                    plan.TempPath,
                    TestContext.Current.CancellationToken));
        }

        Assert.True(File.Exists(plan.SourcePath));
        Assert.False(File.Exists(plan.FinalPath));
    }

    [Fact]
    public async Task Rollback_boundary_blocks_substitution_on_Windows_and_never_deletes_foreign_bytes()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        var substitution = new SubstitutionAttempt();
        var transaction = new PhysicalCopyTransaction(
            atRollbackBoundary: substitution.ReplaceWithForeignBytes);
        PreparedMedia media = await transaction.PrepareAsync(
            plan,
            RunMode.MarkCopies,
            TestContext.Current.CancellationToken);

        await transaction.RollbackAsync(media);

        if (OperatingSystem.IsWindows())
        {
            Assert.False(substitution.DeleteSucceeded);
            Assert.False(substitution.Succeeded);
            Assert.IsType<IOException>(substitution.DeniedBy);
            Assert.False(File.Exists(plan.TempPath));
        }
        else
        {
            Assert.True(substitution.DeleteSucceeded);
            Assert.True(substitution.Succeeded);
            Assert.Null(substitution.DeniedBy);
            Assert.Equal(
                [9, 8, 7],
                await File.ReadAllBytesAsync(
                    plan.TempPath,
                    TestContext.Current.CancellationToken));
        }

        Assert.True(File.Exists(plan.SourcePath));
        Assert.False(File.Exists(plan.FinalPath));
    }

    [Fact]
    public void Windows_file_safety_rejects_missing_original()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        File.Delete(plan.SourcePath);

        IOException exception = Assert.Throws<IOException>(
            () => new WindowsFileSafety().Validate(plan));

        Assert.Contains("不存在", exception.Message);
    }

    [Fact]
    public void Windows_file_safety_rejects_read_only_original()
    {
        using var workspace = new TemporaryWorkspace();
        OutputPlanItem plan = workspace.CreatePlan([1, 2, 3]);
        File.SetAttributes(plan.SourcePath, FileAttributes.ReadOnly);
        try
        {
            IOException exception = Assert.Throws<IOException>(
                () => new WindowsFileSafety().Validate(plan));

            Assert.Contains("只读", exception.Message);
        }
        finally
        {
            File.SetAttributes(plan.SourcePath, FileAttributes.Normal);
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(
            AppContext.BaseDirectory,
            "transaction-workspaces",
            $"emke-transaction-tests-{Guid.NewGuid():N}");

        public OutputPlanItem CreatePlan(byte[] sourceBytes)
        {
            string source = Path.Combine(Root, "source", "商品.jpg");
            string final = Path.Combine(Root, "output", "商品.jpg");
            string temp = Path.Combine(
                Path.GetDirectoryName(final)!,
                $".emke-ai-marker-{Guid.NewGuid():N}.tmp.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllBytes(source, sourceBytes);
            return new(source, "商品.jpg", final, temp, sourceBytes.Length);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class SubstitutionAttempt
    {
        public Exception? DeniedBy { get; private set; }

        public bool DeleteSucceeded { get; private set; }

        public bool Succeeded { get; private set; }

        public void ReplaceWithForeignBytes(string path)
        {
            try
            {
                File.Delete(path);
                DeleteSucceeded = true;
                File.WriteAllBytes(path, [9, 8, 7]);
                Succeeded = true;
            }
            catch (IOException exception)
            {
                DeniedBy = exception;
            }
        }
    }
}
