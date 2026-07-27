using Emke.AiMarker.Core.Contracts;
using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Core.Discovery;

public sealed class InputScanner(IPathAccess paths)
{
    private readonly IPathAccess paths = paths;

    public ScanResult Scan(IEnumerable<string> inputs)
    {
        var media = new List<DiscoveredMedia>();
        var issues = new List<ScanIssue>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string input in inputs)
        {
            string topLevelInput;
            try
            {
                topLevelInput = paths.GetFullPath(input);
            }
            catch (Exception exception) when (IsAccessException(exception))
            {
                issues.Add(new(input, exception.Message));
                continue;
            }

            ScanInput(topLevelInput, seenPaths, visitedDirectories, media, issues);
        }

        return new(
            media.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            issues);
    }

    private void ScanInput(
        string topLevelInput,
        ISet<string> seenPaths,
        ISet<string> visitedDirectories,
        ICollection<DiscoveredMedia> media,
        ICollection<ScanIssue> issues)
    {
        PathEntryKind kind;
        try
        {
            kind = paths.GetKind(topLevelInput);
        }
        catch (Exception exception) when (IsAccessException(exception))
        {
            issues.Add(new(topLevelInput, exception.Message));
            return;
        }

        switch (kind)
        {
            case PathEntryKind.File:
                AddFile(topLevelInput, topLevelInput, null, seenPaths, media, issues);
                break;
            case PathEntryKind.Directory:
                ScanDirectory(
                    topLevelInput,
                    topLevelInput,
                    seenPaths,
                    visitedDirectories,
                    media,
                    issues);
                break;
            case PathEntryKind.ReparseFile:
            case PathEntryKind.ReparseDirectory:
                issues.Add(new(topLevelInput, "拒绝重解析点路径。"));
                break;
            case PathEntryKind.Missing:
                issues.Add(new(topLevelInput, "路径不存在。"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    private void ScanDirectory(
        string directory,
        string topLevelInput,
        ISet<string> seenPaths,
        ISet<string> visitedDirectories,
        ICollection<DiscoveredMedia> media,
        ICollection<ScanIssue> issues)
    {
        if (!visitedDirectories.Add(directory))
        {
            return;
        }

        IEnumerable<string> children;
        try
        {
            children = paths.EnumerateChildren(directory)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (IsAccessException(exception))
        {
            issues.Add(new(directory, exception.Message));
            return;
        }

        foreach (string child in children)
        {
            PathEntryKind kind;
            try
            {
                kind = paths.GetKind(child);
            }
            catch (Exception exception) when (IsAccessException(exception))
            {
                issues.Add(new(child, exception.Message));
                continue;
            }

            switch (kind)
            {
                case PathEntryKind.File:
                    AddFile(child, topLevelInput, directory, seenPaths, media, issues);
                    break;
                case PathEntryKind.Directory:
                    ScanDirectory(
                        child,
                        topLevelInput,
                        seenPaths,
                        visitedDirectories,
                        media,
                        issues);
                    break;
                case PathEntryKind.ReparseFile:
                case PathEntryKind.ReparseDirectory:
                    issues.Add(new(child, "拒绝重解析点路径。"));
                    break;
                case PathEntryKind.Missing:
                    issues.Add(new(child, "路径不存在。"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }
    }

    private void AddFile(
        string file,
        string topLevelInput,
        string? currentDirectory,
        ISet<string> seenPaths,
        ICollection<DiscoveredMedia> media,
        ICollection<ScanIssue> issues)
    {
        string sourcePath;
        try
        {
            sourcePath = paths.GetFullPath(file);
        }
        catch (Exception exception) when (IsAccessException(exception))
        {
            issues.Add(new(file, exception.Message));
            return;
        }

        if (!seenPaths.Add(sourcePath))
        {
            return;
        }

        string extension = GetExtension(sourcePath);
        if (!MarkerContract.SupportedExtensions.Contains(extension))
        {
            return;
        }

        try
        {
            media.Add(new(
                sourcePath,
                topLevelInput,
                currentDirectory is null ? GetFileName(sourcePath) : GetRelativePath(topLevelInput, sourcePath),
                extension,
                paths.GetFileLength(sourcePath)));
        }
        catch (Exception exception) when (IsAccessException(exception))
        {
            issues.Add(new(sourcePath, exception.Message));
        }
    }

    private static bool IsAccessException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static string GetExtension(string path)
    {
        int separator = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        int dot = path.LastIndexOf('.');
        return dot > separator ? path[dot..] : string.Empty;
    }

    private static string GetFileName(string path)
    {
        int separator = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        return separator < 0 ? path : path[(separator + 1)..];
    }

    private static string GetRelativePath(string root, string path)
    {
        string normalizedRoot = root.TrimEnd('\\', '/');
        if (path.StartsWith(normalizedRoot + "\\", StringComparison.OrdinalIgnoreCase))
        {
            return path[(normalizedRoot.Length + 1)..];
        }

        if (path.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return path[(normalizedRoot.Length + 1)..];
        }

        throw new IOException("发现的文件不在顶层输入目录中。");
    }
}
