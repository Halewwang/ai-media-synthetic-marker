using Emke.AiMarker.Core.Contracts;

namespace Emke.AiMarker.Core.Tests.Contracts;

public sealed class MarkerContractTests
{
    [Fact]
    public void Contract_values_are_exact_and_case_sensitive()
    {
        Assert.Equal("contains-synthetic-performer", MarkerContract.Marker);
        Assert.Equal("XMP-dc:Subject", MarkerContract.VerificationField);
        Assert.Equal("rdf:Bag/rdf:li", MarkerContract.VerificationStructure);
        Assert.Equal("http://purl.org/dc/elements/1.1/", MarkerContract.DcNamespace);
        Assert.Equal(
            "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
            MarkerContract.RdfNamespace);
    }

    [Fact]
    public void Core_assembly_version_is_2_0_0_0()
    {
        Assert.Equal(
            new Version(2, 0, 0, 0),
            typeof(MarkerContract).Assembly.GetName().Version);
    }

    [Fact]
    public void Supported_extensions_are_case_insensitive_and_limited_to_media_contract()
    {
        Assert.Contains(".JPG", MarkerContract.SupportedExtensions);
        Assert.Contains(".jpeg", MarkerContract.SupportedExtensions);
        Assert.Contains(".PnG", MarkerContract.SupportedExtensions);
        Assert.Contains(".MP4", MarkerContract.SupportedExtensions);
        Assert.DoesNotContain(".gif", MarkerContract.SupportedExtensions);
    }
}
