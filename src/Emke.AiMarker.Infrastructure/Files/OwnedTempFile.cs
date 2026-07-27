namespace Emke.AiMarker.Infrastructure.Files;

internal static class OwnedTempFile
{
    private const string Prefix = ".emke-ai-marker-";

    public static bool IsOwned(string candidatePath, string finalPath)
    {
        try
        {
            string candidateFullPath = Path.GetFullPath(candidatePath);
            string finalFullPath = Path.GetFullPath(finalPath);
            string? candidateDirectory = Path.GetDirectoryName(candidateFullPath);
            string? destinationDirectory = Path.GetDirectoryName(finalFullPath);
            string candidateName = Path.GetFileName(candidateFullPath);

            return candidateDirectory is not null
                && destinationDirectory is not null
                && string.Equals(
                    candidateDirectory,
                    destinationDirectory,
                    StringComparison.OrdinalIgnoreCase)
                && candidateName.StartsWith(Prefix, StringComparison.Ordinal);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }

    public static bool IsSamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }
}
