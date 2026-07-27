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
    public async Task Reprocessing_existing_output_reports_compliant_without_duplicate_marker(
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

        ProcessResult second = await services.Processor.ProcessAsync(
            harness.Plan,
            RunMode.MarkCopies,
            CancellationToken.None);

        Assert.Equal(ProcessStatus.OutputAlreadyCompliant, second.Status);
        Assert.Equal(VerificationResult.Passed, second.Evidence.Result);
        IReadOnlyList<string> subjects =
            await services.ExifTool.ReadSubjectsAsync(
                second.OutputPath,
                CancellationToken.None);
        Assert.Contains(IntegrationConstants.ExistingSubject, subjects);
        Assert.Equal(
            1,
            subjects.Count(subject =>
                string.Equals(
                    subject,
                    MarkerContract.Marker,
                    StringComparison.Ordinal)));
    }
}
