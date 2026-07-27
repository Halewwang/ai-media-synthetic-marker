using Emke.AiMarker.Core.Discovery;
using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Core.Abstractions;

namespace Emke.AiMarker.Core.Tests.TestSupport;

internal sealed class FakePathAccess : IPathAccess
{
    private readonly Dictionary<string, FakeEntry> entries =
        new(StringComparer.OrdinalIgnoreCase);

    public FakePathAccess Directory(string path) => Add(path, PathEntryKind.Directory, 0);

    public FakePathAccess ReparseDirectory(string path) =>
        Add(path, PathEntryKind.ReparseDirectory, 0);

    public FakePathAccess File(string path, long length) => Add(path, PathEntryKind.File, length);

    public FakePathAccess ReparseFile(string path, long length) =>
        Add(path, PathEntryKind.ReparseFile, length);

    public PathEntryKind GetKind(string path) =>
        entries.TryGetValue(Normalize(path), out FakeEntry? entry)
            ? entry.Kind
            : PathEntryKind.Missing;

    public IEnumerable<string> EnumerateChildren(string directory)
    {
        string normalizedDirectory = Normalize(directory);
        return entries.Keys
            .Where(path => string.Equals(
                Parent(path),
                normalizedDirectory,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public long GetFileLength(string file) => entries[Normalize(file)].Length;

    public string GetFullPath(string path) => Normalize(path);

    private FakePathAccess Add(string path, PathEntryKind kind, long length)
    {
        entries.Add(Normalize(path), new(kind, length));
        return this;
    }

    private static string Normalize(string path) => path.TrimEnd('\\', '/').Replace('/', '\\');

    private static string Parent(string path)
    {
        int separator = path.LastIndexOf('\\');
        return separator < 0 ? string.Empty : path[..separator];
    }

    private sealed record FakeEntry(PathEntryKind Kind, long Length);
}

internal sealed class FakeStorageProbe(long availableBytes) : IStorageProbe
{
    private readonly long availableBytes = availableBytes;

    public List<string> WritableDirectories { get; } = [];

    public Exception? WritableFailure { get; init; }

    public long GetAvailableBytes(string directory) => availableBytes;

    public void AssertWritable(string directory)
    {
        WritableDirectories.Add(directory);
        if (WritableFailure is not null)
        {
            throw WritableFailure;
        }
    }
}
