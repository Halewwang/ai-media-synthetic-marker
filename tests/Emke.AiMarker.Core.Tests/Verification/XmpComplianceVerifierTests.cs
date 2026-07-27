using System.Text;
using Emke.AiMarker.Core.Contracts;
using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Core.Verification;

namespace Emke.AiMarker.Core.Tests.Verification;

public sealed class XmpComplianceVerifierTests
{
    private static byte[] MakeXmp(
        IEnumerable<string> values,
        string container = "Bag",
        string? dcNamespace = null)
    {
        string items = string.Concat(
            values.Select(value =>
                $"<rdf:li>{System.Security.SecurityElement.Escape(value)}</rdf:li>"));
        string xml =
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="{MarkerContract.RdfNamespace}">
                <rdf:Description xmlns:dc="{dcNamespace ?? MarkerContract.DcNamespace}">
                  <dc:subject><rdf:{container}>{items}</rdf:{container}></dc:subject>
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            """;
        return Encoding.UTF8.GetBytes(xml);
    }

    [Fact]
    public void Exact_marker_in_formal_bag_li_passes()
    {
        var evidence = XmpComplianceVerifier.Verify(
            ["existing", MarkerContract.Marker],
            MakeXmp(["existing", MarkerContract.Marker]),
            "13.59",
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.FromHours(8)));

        Assert.Equal(VerificationResult.Passed, evidence.Result);
        Assert.Equal("已确认 rdf:Bag/rdf:li", evidence.XmpStructure);
    }

    [Theory]
    [InlineData("CONTAINS-SYNTHETIC-PERFORMER")]
    [InlineData(" contains-synthetic-performer")]
    [InlineData("contains-synthetic-performer ")]
    [InlineData("contains-synthetic-performer-extra")]
    public void Near_matches_are_unmarked(string value)
    {
        var evidence = XmpComplianceVerifier.Verify(
            [value],
            MakeXmp([value]),
            "13.59",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(VerificationResult.Unmarked, evidence.Result);
    }

    [Fact]
    public void Marker_in_rdf_seq_fails_structure_validation()
    {
        var evidence = XmpComplianceVerifier.Verify(
            [MarkerContract.Marker],
            MakeXmp([MarkerContract.Marker], container: "Seq"),
            "13.59",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(VerificationResult.Failed, evidence.Result);
        Assert.Contains("rdf:Bag/rdf:li", evidence.Error);
    }

    [Fact]
    public void Wrong_dc_namespace_fails_structure_validation()
    {
        var evidence = XmpComplianceVerifier.Verify(
            [MarkerContract.Marker],
            MakeXmp(
                [MarkerContract.Marker],
                dcNamespace: "https://example.invalid/dc"),
            "13.59",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(VerificationResult.Failed, evidence.Result);
    }

    [Fact]
    public void Empty_raw_xmp_fails_when_subject_contains_exact_marker()
    {
        var evidence = XmpComplianceVerifier.Verify(
            [MarkerContract.Marker],
            ReadOnlyMemory<byte>.Empty,
            "13.59",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(VerificationResult.Failed, evidence.Result);
        Assert.Equal("未读取到原始 XMP", evidence.XmpStructure);
        Assert.Equal(
            "字段读取到了目标值，但没有读取到可验证的原始 XMP 数据包。",
            evidence.Error);
    }

    [Fact]
    public void Malformed_raw_xmp_fails_when_subject_contains_exact_marker()
    {
        var evidence = XmpComplianceVerifier.Verify(
            [MarkerContract.Marker],
            Encoding.UTF8.GetBytes("<rdf:RDF"),
            "13.59",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(VerificationResult.Failed, evidence.Result);
        Assert.Equal("原始 XMP 解析失败", evidence.XmpStructure);
        Assert.NotEmpty(evidence.Error);
    }

    [Fact]
    public void Missing_exact_subject_is_unmarked_before_raw_xmp_is_checked()
    {
        var evidence = XmpComplianceVerifier.Verify(
            ["contains-synthetic-performer-extra"],
            ReadOnlyMemory<byte>.Empty,
            "13.59",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(VerificationResult.Unmarked, evidence.Result);
        Assert.Equal("未找到目标 rdf:li", evidence.XmpStructure);
        Assert.Equal("[\"contains-synthetic-performer-extra\"]", evidence.ActualValue);
    }
}
