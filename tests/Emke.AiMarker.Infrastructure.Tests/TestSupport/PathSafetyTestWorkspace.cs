using System.Diagnostics;

namespace Emke.AiMarker.Infrastructure.Tests.TestSupport;

internal sealed class PathSafetyTestWorkspace : IDisposable
{
    public PathSafetyTestWorkspace()
    {
        Root = Path.Combine(
            AppContext.BaseDirectory,
            "path-safety-workspaces",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string CreateDirectory(string relativePath)
    {
        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateFile(string relativePath, byte[]? bytes = null)
    {
        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes ?? [1, 2, 3]);
        return path;
    }

    public string CreateDirectoryLink(string relativePath, string target)
    {
        string link = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            Assert.Skip(
                $"受控目录符号链接不可用，环境限制：{exception.Message}");
        }

        return link;
    }

    public string CreateFileLink(string relativePath, string target)
    {
        string link = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            Assert.Skip(
                $"受控文件符号链接不可用，环境限制：{exception.Message}");
        }

        return link;
    }

    public string CreateWindowsJunction(string relativePath, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                "Windows junction helper may only run on Windows.");
        }

        string junction = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(junction)!);
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            ArgumentList =
            {
                "/d",
                "/c",
                "mklink",
                "/J",
                junction,
                target,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException(
            "无法启动 cmd.exe 创建 Windows junction，环境限制。");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            string detail =
                $"{process.StandardOutput.ReadToEnd()} {process.StandardError.ReadToEnd()}".Trim();
            Assert.Fail(
                $"无法创建 Windows junction，环境限制（退出码 {process.ExitCode}）：{detail}");
        }

        return junction;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            DeleteTreeNoFollow(Root);
        }
    }

    private static void DeleteTreeNoFollow(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                Directory.Delete(path);
            }
            else
            {
                File.Delete(path);
            }

            return;
        }

        if (!attributes.HasFlag(FileAttributes.Directory))
        {
            File.Delete(path);
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            DeleteTreeNoFollow(entry);
        }

        Directory.Delete(path);
    }
}
