namespace Emke.AiMarker.Infrastructure.Files;

public interface IPathComponentGuard
{
    string EnsureExistingPath(string path);

    string EnsurePathAllowsMissing(string path);
}

public sealed class PathComponentGuard : IPathComponentGuard
{
    public string EnsureExistingPath(string path) =>
        EnsureSafePath(path, requireLeaf: true);

    public string EnsurePathAllowsMissing(string path) =>
        EnsureSafePath(path, requireLeaf: false);

    private static string EnsureSafePath(string path, bool requireLeaf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)
            ?? throw new IOException($"路径缺少卷根或共享根：{fullPath}");
        string[] components = fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        string current = root;
        if (!InspectComponent(
                current,
                hasDescendants: components.Length > 0,
                requireLeaf))
        {
            return fullPath;
        }

        for (int index = 0; index < components.Length; index++)
        {
            current = Path.Combine(current, components[index]);
            bool hasDescendants = index < components.Length - 1;
            if (!InspectComponent(current, hasDescendants, requireLeaf))
            {
                return fullPath;
            }
        }

        return fullPath;
    }

    private static bool InspectComponent(
        string component,
        bool hasDescendants,
        bool requireLeaf)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(component);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            if (requireLeaf)
            {
                throw new IOException($"路径不存在：{component}", exception);
            }

            return false;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                $"路径包含重解析点组件，已拒绝访问：{component}");
        }

        if (hasDescendants
            && (attributes & FileAttributes.Directory) == 0)
        {
            throw new IOException(
                $"路径祖先不是目录，已拒绝访问：{component}");
        }

        return true;
    }
}
