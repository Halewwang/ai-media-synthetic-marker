namespace Emke.AiMarker.Infrastructure.ExifTool;

public sealed class MarkerOperationException : Exception
{
    public MarkerOperationException(string message)
        : base(message)
    {
    }
}

public sealed class ExifToolIntegrityException : Exception
{
    public ExifToolIntegrityException(string message)
        : base(message)
    {
    }
}
