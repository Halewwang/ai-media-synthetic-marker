namespace Emke.AiMarker.Core.Contracts;

public static class MarkerContract
{
    public const string Marker = "contains-synthetic-performer";
    public const string VerificationField = "XMP-dc:Subject";
    public const string VerificationStructure = "rdf:Bag/rdf:li";
    public const string DcNamespace = "http://purl.org/dc/elements/1.1/";
    public const string RdfNamespace =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".mp4",
        };
}
