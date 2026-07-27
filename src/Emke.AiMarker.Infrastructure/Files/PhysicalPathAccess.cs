using Emke.AiMarker.Core.Discovery;
using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Infrastructure.Files;

public sealed class PhysicalPathAccess : IPathAccess
{
    public PathEntryKind GetKind(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return PathEntryKind.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return PathEntryKind.Missing;
        }

        bool isDirectory = attributes.HasFlag(FileAttributes.Directory);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return isDirectory ? PathEntryKind.ReparseDirectory : PathEntryKind.ReparseFile;
        }

        return isDirectory ? PathEntryKind.Directory : PathEntryKind.File;
    }

    public IEnumerable<string> EnumerateChildren(string directory) =>
        Directory.EnumerateFileSystemEntries(
            directory,
            "*",
            new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                AttributesToSkip = 0,
            });

    public long GetFileLength(string file) => new FileInfo(file).Length;

    public string GetFullPath(string path) => Path.GetFullPath(path);
}
