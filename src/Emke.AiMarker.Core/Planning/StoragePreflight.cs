using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Core.Planning;

public sealed class StoragePreflight(IStorageProbe storage)
{
    private readonly IStorageProbe storage = storage;

    public StorageCheck Check(IEnumerable<OutputPlanItem> plans)
    {
        OutputPlanItem[] planItems = plans.ToArray();
        long total = planItems.Sum(item => item.Length);
        long margin = Math.Max(total / 20, 256L * 1024 * 1024);
        long required = checked(total + margin);
        string[] destinations = planItems
            .Select(item => GetDirectoryName(item.FinalPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (destinations.Length == 0)
        {
            return new(true, required, long.MaxValue, string.Empty);
        }

        try
        {
            long available = destinations
                .Select(storage.GetAvailableBytes)
                .Min();
            if (available < required)
            {
                return new(false, required, available, "可用空间不足。");
            }

            foreach (string destination in destinations)
            {
                storage.AssertWritable(destination);
            }

            return new(true, required, available, string.Empty);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(false, required, 0, exception.Message);
        }
    }

    private static string GetDirectoryName(string path)
    {
        string normalized = path.TrimEnd('\\', '/');
        int separator = Math.Max(normalized.LastIndexOf('\\'), normalized.LastIndexOf('/'));
        if (separator < 0)
        {
            throw new ArgumentException("输出路径必须包含父目录。", nameof(path));
        }

        return normalized[..separator];
    }
}

public sealed record StorageCheck(
    bool IsReady,
    long RequiredBytes,
    long AvailableBytes,
    string Error);
