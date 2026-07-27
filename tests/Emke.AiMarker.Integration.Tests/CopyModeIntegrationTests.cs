using Emke.AiMarker.Core.Contracts;
using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Integration.Tests.TestSupport;

namespace Emke.AiMarker.Integration.Tests;

public sealed class CopyModeIntegrationTests
{
    [Theory]
    [InlineData("fixture.jpg")]
    [InlineData("fixture.jpeg")]
    [InlineData("fixture.png")]
    [InlineData("fixture.mp4")]
    public async Task Copy_mode_marks_output_without_changing_source_or_media_stream(
        string name)
    {
        await using IntegrationHarness harness =
            IntegrationHarness.Create(name);
        IntegrationServices services = await IntegrationServices.CreateAsync();
        string sourceHash = Hashing.Sha256(harness.SourcePath);
        string sourceImageDataHash =
            await services.ExifTool.ReadImageDataHashAsync(
                harness.SourcePath,
                CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(sourceImageDataHash));

        ProcessResult result = await services.Processor.ProcessAsync(
            harness.Plan,
            RunMode.MarkCopies,
            CancellationToken.None);

        Assert.True(
            result.Status == ProcessStatus.Added,
            $"Expected Added, got {result.Status}: {result.Error} {result.Evidence.Error}");
        Assert.Equal(sourceHash, Hashing.Sha256(harness.SourcePath));
        Assert.Equal(VerificationResult.Passed, result.Evidence.Result);
        Assert.Equal("已确认 rdf:Bag/rdf:li", result.Evidence.XmpStructure);
        Assert.Equal("13.59", result.Evidence.ExifToolVersion);
        Assert.Equal(harness.FinalPath, result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));

        IReadOnlyList<string> subjects =
            await services.ExifTool.ReadSubjectsAsync(
                result.OutputPath,
                CancellationToken.None);
        Assert.Contains(IntegrationConstants.ExistingSubject, subjects);
        Assert.Equal(
            1,
            subjects.Count(subject =>
                string.Equals(
                    subject,
                    MarkerContract.Marker,
                    StringComparison.Ordinal)));

        string outputImageDataHash =
            await services.ExifTool.ReadImageDataHashAsync(
                result.OutputPath,
                CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(outputImageDataHash));
        Assert.Equal(sourceImageDataHash, outputImageDataHash);
    }

    [Theory]
    [InlineData("fixture.jpg")]
    [InlineData("fixture.jpeg")]
    [InlineData("fixture.png")]
    [InlineData("fixture.mp4")]
    public async Task Compliant_output_is_copied_then_existing_final_is_reported(
        string name)
    {
        await using IntegrationHarness harness =
            IntegrationHarness.Create(name);
        IntegrationServices services = await IntegrationServices.CreateAsync();

        ProcessResult first = await services.Processor.ProcessAsync(
            harness.Plan,
            RunMode.MarkCopies,
            CancellationToken.None);
        Assert.True(
            first.Status == ProcessStatus.Added,
            $"Expected Added, got {first.Status}: {first.Error} {first.Evidence.Error}");
        string firstOutputSha = Hashing.Sha256(first.OutputPath);
        string firstImageDataHash =
            await services.ExifTool.ReadImageDataHashAsync(
                first.OutputPath,
                CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(firstImageDataHash));

        OutputPlanItem secondPlan = IntegrationPlans.For(
            first.OutputPath,
            Path.Combine(harness.Root, "output-2"));
        ProcessResult second = await services.Processor.ProcessAsync(
            secondPlan,
            RunMode.MarkCopies,
            CancellationToken.None);

        Assert.NotEqual(first.OutputPath, second.OutputPath);
        Assert.Equal(ProcessStatus.AlreadyCompliant, second.Status);
        Assert.Equal(VerificationResult.Passed, second.Evidence.Result);
        Assert.Equal(secondPlan.FinalPath, second.OutputPath);
        Assert.True(File.Exists(second.OutputPath));
        Assert.Equal(firstOutputSha, Hashing.Sha256(first.OutputPath));
        IReadOnlyList<string> copiedSubjects =
            await services.ExifTool.ReadSubjectsAsync(
                second.OutputPath,
                CancellationToken.None);
        Assert.Contains(
            IntegrationConstants.ExistingSubject,
            copiedSubjects);
        Assert.Equal(
            1,
            copiedSubjects.Count(subject =>
                string.Equals(
                    subject,
                    MarkerContract.Marker,
                    StringComparison.Ordinal)));
        string secondImageDataHash =
            await services.ExifTool.ReadImageDataHashAsync(
                second.OutputPath,
                CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(secondImageDataHash));
        Assert.Equal(firstImageDataHash, secondImageDataHash);

        ProcessResult existingFinal = await services.Processor.ProcessAsync(
            secondPlan,
            RunMode.MarkCopies,
            CancellationToken.None);

        Assert.Equal(
            ProcessStatus.OutputAlreadyCompliant,
            existingFinal.Status);
        Assert.Equal(
            VerificationResult.Passed,
            existingFinal.Evidence.Result);
        Assert.Equal(second.OutputPath, existingFinal.OutputPath);
        Assert.Equal(firstOutputSha, Hashing.Sha256(first.OutputPath));
        IReadOnlyList<string> existingFinalSubjects =
            await services.ExifTool.ReadSubjectsAsync(
                existingFinal.OutputPath,
                CancellationToken.None);
        Assert.Contains(
            IntegrationConstants.ExistingSubject,
            existingFinalSubjects);
        Assert.Equal(
            1,
            existingFinalSubjects.Count(subject =>
                string.Equals(
                    subject,
                    MarkerContract.Marker,
                    StringComparison.Ordinal)));
    }
}
