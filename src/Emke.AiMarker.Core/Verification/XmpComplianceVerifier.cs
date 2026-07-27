using System.Text.Json;
using System.Xml.Linq;
using Emke.AiMarker.Core.Contracts;
using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Core.Verification;

public static class XmpComplianceVerifier
{
    public static VerificationEvidence Verify(
        IReadOnlyList<string> subjects,
        ReadOnlyMemory<byte> rawXmp,
        string exifToolVersion,
        DateTimeOffset verifiedAt)
    {
        string actualValue = JsonSerializer.Serialize(subjects);

        if (!subjects.Contains(MarkerContract.Marker, StringComparer.Ordinal))
        {
            return new(
                VerificationResult.Unmarked,
                actualValue,
                "未找到目标 rdf:li",
                verifiedAt,
                exifToolVersion);
        }

        if (rawXmp.IsEmpty)
        {
            return new(
                VerificationResult.Failed,
                actualValue,
                "未读取到原始 XMP",
                verifiedAt,
                exifToolVersion,
                "字段读取到了目标值，但没有读取到可验证的原始 XMP 数据包。");
        }

        try
        {
            using var stream = new MemoryStream(rawXmp.ToArray(), writable: false);
            XDocument document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            XNamespace dc = MarkerContract.DcNamespace;
            XNamespace rdf = MarkerContract.RdfNamespace;
            bool found = document
                .Descendants(dc + "subject")
                .Elements(rdf + "Bag")
                .Elements(rdf + "li")
                .Any(item => string.Equals(
                    item.Value,
                    MarkerContract.Marker,
                    StringComparison.Ordinal));

            if (found)
            {
                return new(
                    VerificationResult.Passed,
                    actualValue,
                    "已确认 rdf:Bag/rdf:li",
                    verifiedAt,
                    exifToolVersion);
            }

            return new(
                VerificationResult.Failed,
                actualValue,
                "未确认 rdf:Bag/rdf:li",
                verifiedAt,
                exifToolVersion,
                "字段读取到了目标值，但原始 XMP 中未确认 rdf:Bag/rdf:li 结构。");
        }
        catch (System.Xml.XmlException exception)
        {
            return new(
                VerificationResult.Failed,
                actualValue,
                "原始 XMP 解析失败",
                verifiedAt,
                exifToolVersion,
                exception.Message);
        }
    }
}
