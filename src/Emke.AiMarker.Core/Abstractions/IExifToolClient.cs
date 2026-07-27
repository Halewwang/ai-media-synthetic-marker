namespace Emke.AiMarker.Core.Abstractions;

public interface IExifToolClient
{
    Task<string> GetVersionAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ReadSubjectsAsync(
        string path,
        CancellationToken cancellationToken);

    Task WriteMarkerAsync(string path, CancellationToken cancellationToken);

    Task<ReadOnlyMemory<byte>> ReadRawXmpAsync(
        string path,
        CancellationToken cancellationToken);

    Task<string> ReadImageDataHashAsync(
        string path,
        CancellationToken cancellationToken);
}
