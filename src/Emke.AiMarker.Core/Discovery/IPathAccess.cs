using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Core.Discovery;

public interface IPathAccess
{
    PathEntryKind GetKind(string path);

    IEnumerable<string> EnumerateChildren(string directory);

    long GetFileLength(string file);

    string GetFullPath(string path);
}
