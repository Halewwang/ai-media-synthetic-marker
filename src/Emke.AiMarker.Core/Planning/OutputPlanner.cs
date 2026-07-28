using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Core.Planning;

public static class OutputPlanner
{
    private const string DefaultOutputDirectoryName = "EMKE 已标记";

    public static IReadOnlyList<OutputPlanItem> Plan(
        IEnumerable<DiscoveredMedia> media,
        string? customOutputRoot)
    {
        var plans = new List<OutputPlanItem>();
        var finalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (DiscoveredMedia item in media)
        {
            string finalPath = GetFinalPath(item, customOutputRoot);
            if (!finalPaths.Add(finalPath))
            {
                throw new InvalidOperationException($"输出路径冲突：{finalPath}");
            }

            string destinationDirectory = GetDirectoryName(finalPath);
            string tempName =
                $".emke-ai-marker-{Guid.NewGuid():N}.tmp{GetExtension(finalPath)}";
            plans.Add(new(
                item.SourcePath,
                item.RelativePath,
                finalPath,
                Combine(destinationDirectory, tempName),
                item.Length));
        }

        return plans;
    }

    private static string GetFinalPath(DiscoveredMedia item, string? customOutputRoot)
    {
        bool folderInput = !string.Equals(
            item.SourcePath,
            item.TopLevelInput,
            StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(customOutputRoot))
        {
            return folderInput
                ? Combine(customOutputRoot, GetFileName(item.TopLevelInput), item.RelativePath)
                : Combine(customOutputRoot, GetFileName(item.SourcePath));
        }

        if (!folderInput)
        {
            return Combine(
                GetDirectoryName(item.SourcePath),
                DefaultOutputDirectoryName,
                GetFileName(item.SourcePath));
        }

        return Combine(
            GetDirectoryName(item.TopLevelInput),
            DefaultOutputDirectoryName,
            GetFileName(item.TopLevelInput),
            item.RelativePath);
    }

    private static string GetExtension(string path)
    {
        int separator = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        int dot = path.LastIndexOf('.');
        return dot > separator ? path[dot..] : string.Empty;
    }

    private static string GetFileName(string path)
    {
        string normalized = path.TrimEnd('\\', '/');
        int separator = Math.Max(normalized.LastIndexOf('\\'), normalized.LastIndexOf('/'));
        return separator < 0 ? normalized : normalized[(separator + 1)..];
    }

    private static string GetDirectoryName(string path)
    {
        string normalized = path.TrimEnd('\\', '/');
        int separator = Math.Max(normalized.LastIndexOf('\\'), normalized.LastIndexOf('/'));
        if (separator < 0)
        {
            throw new ArgumentException("路径必须包含父目录。", nameof(path));
        }

        return normalized[..separator];
    }

    private static string Combine(params string[] paths)
    {
        bool windowsPath = paths.Any(path => path.Contains('\\'))
            || paths.FirstOrDefault() is { Length: 2 } root
                && char.IsAsciiLetter(root[0])
                && root[1] == ':';
        char separator = windowsPath ? '\\' : '/';
        string[] nonEmptyPaths = paths.Where(path => path.Length > 0).ToArray();
        if (nonEmptyPaths.Length == 0)
        {
            return string.Empty;
        }

        string combined = nonEmptyPaths[0].TrimEnd('\\', '/');
        foreach (string path in nonEmptyPaths.Skip(1))
        {
            string segment = path.Trim('\\', '/');
            if (segment.Length > 0)
            {
                combined = $"{combined}{separator}{segment}";
            }
        }

        return combined;
    }
}
