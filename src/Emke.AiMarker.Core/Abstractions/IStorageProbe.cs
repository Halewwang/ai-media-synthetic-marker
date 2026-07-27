namespace Emke.AiMarker.Core.Abstractions;

public interface IStorageProbe
{
    long GetAvailableBytes(string directory);

    void AssertWritable(string directory);
}
