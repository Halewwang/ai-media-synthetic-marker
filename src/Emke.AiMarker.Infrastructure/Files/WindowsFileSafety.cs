using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Infrastructure.Files;

public sealed class WindowsFileSafety : IOriginalWriteSafety
{
    private const FileAttributes UnsafeAttributes =
        FileAttributes.ReadOnly
        | FileAttributes.Hidden
        | FileAttributes.System
        | FileAttributes.ReparsePoint;

    public void Validate(OutputPlanItem plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!File.Exists(plan.SourcePath))
        {
            throw new IOException(
                $"原文件不存在，已拒绝直接写入：{plan.SourcePath}");
        }

        FileAttributes attributes = File.GetAttributes(plan.SourcePath);
        FileAttributes unsafeAttributes = attributes & UnsafeAttributes;
        if (unsafeAttributes == 0)
        {
            return;
        }

        var reasons = new List<string>();
        AddReason(FileAttributes.ReadOnly, "只读", unsafeAttributes, reasons);
        AddReason(FileAttributes.Hidden, "隐藏", unsafeAttributes, reasons);
        AddReason(FileAttributes.System, "系统", unsafeAttributes, reasons);
        AddReason(
            FileAttributes.ReparsePoint,
            "重解析点",
            unsafeAttributes,
            reasons);
        throw new IOException(
            $"原文件具有不安全属性（{string.Join("、", reasons)}），已拒绝直接写入：{plan.SourcePath}");
    }

    private static void AddReason(
        FileAttributes attribute,
        string reason,
        FileAttributes actual,
        ICollection<string> reasons)
    {
        if ((actual & attribute) != 0)
        {
            reasons.Add(reason);
        }
    }
}
