using System.Text;

namespace Emke.AiMarker.Release.Packaging;

public static class PortablePathValidator
{
    private static readonly HashSet<char> InvalidWindowsCharacters =
        ['<', '>', ':', '"', '|', '?', '*'];

    private static readonly HashSet<string> ReservedWindowsNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9",
        };

    public static void ValidateRelativePath(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.StartsWith("/", StringComparison.Ordinal)
            || path.StartsWith('\\')
            || path.Contains('\\', StringComparison.Ordinal))
        {
            throw Unsafe(description, path);
        }

        string[] segments = path.Split('/');
        foreach (string segment in segments)
        {
            ValidateSegment(segment, description, path);
        }
    }

    public static string CollisionKey(string path) =>
        path.Normalize(NormalizationForm.FormC);

    public static void ValidatePathSet(
        IEnumerable<string> paths,
        string description)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var collisionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            ValidateRelativePath(path, description);
            if (!collisionKeys.Add(CollisionKey(path)))
            {
                throw new ReleaseToolException(
                    $"{description} 包含大小写或 Unicode 规范化冲突：{path}");
            }
        }
    }

    private static void ValidateSegment(
        string segment,
        string description,
        string path)
    {
        if (segment.Length == 0
            || segment is "." or ".."
            || segment.EndsWith(".", StringComparison.Ordinal)
            || segment.EndsWith(' ')
            || segment.Any(character =>
                char.IsControl(character)
                || InvalidWindowsCharacters.Contains(character)))
        {
            throw Unsafe(description, path);
        }

        string deviceStem = segment.Split('.', 2)[0];
        if (ReservedWindowsNames.Contains(deviceStem))
        {
            throw Unsafe(description, path);
        }
    }

    private static ReleaseToolException Unsafe(
        string description,
        string? path) =>
        new($"{description} 包含不安全路径：{path}");
}
