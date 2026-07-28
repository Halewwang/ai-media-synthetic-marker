using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Emke.AiMarker.Release.Packaging;

namespace Emke.AiMarker.Release.Commands;

public interface IPackageProcessRunner
{
    Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken);
}

public sealed class PackageProcessRunner : IPackageProcessRunner
{
    public async Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new ReleaseToolException("无法启动发布包自检。");
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            await process.WaitForExitAsync(timeout.Token);
            string output = await stdout;
            string error = await stderr;
            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
            {
                Console.Error.WriteLine(error.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine(output.Trim());
            }

            return process.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new ReleaseToolException("发布包自检在 3 分钟后超时。");
        }
        catch (ReleaseToolException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            TryKill(process);
            throw new ReleaseToolException(
                $"无法执行发布包自检：{exception.Message}",
                exception);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        catch
        {
            // Cleanup failure must not replace the primary process error.
        }
    }
}

public sealed record PackageResult(
    string ZipPath,
    string ChecksumPath,
    string Sha256,
    string StagePath);

public sealed class PackageCommand
{
    public const string RootName = "emke-ai-marker-v2.0.0-windows-x64";
    public const string ZipName = $"{RootName}.zip";
    private const string ChecksumName = "SHA256SUMS.txt";
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IPackageProcessRunner processRunner;
    private readonly IVersionProbe versionProbe;

    public PackageCommand(
        IPackageProcessRunner processRunner,
        IVersionProbe versionProbe)
    {
        this.processRunner = processRunner
            ?? throw new ArgumentNullException(nameof(processRunner));
        this.versionProbe = versionProbe
            ?? throw new ArgumentNullException(nameof(versionProbe));
    }

    public async Task<PackageResult> ExecuteAsync(
        string repositoryRoot,
        string publishDirectory,
        string outputDirectory,
        long epoch,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(repositoryRoot);
        ReleaseStageValidator.EnsureOrdinaryDirectory(root, "仓库根目录");
        EnsureNoReparseComponents(root, root, "仓库根目录");
        string publish = RequirePublishDirectory(
            root,
            publishDirectory);
        string output = RequireSafeOutputDirectory(root, outputDirectory);

        string manifestPath = Path.Combine(
            root,
            "packaging",
            "release-manifest.json");
        string lockPath = Path.Combine(
            root,
            "packaging",
            "exiftool.lock.json");
        string runtime = Path.Combine(root, "runtime", "exiftool");
        EnsureNoReparseComponents(root, runtime, "ExifTool runtime");
        await FetchExifToolCommand.ValidateInstallationAsync(
            runtime,
            lockPath,
            versionProbe,
            cancellationToken);

        string outputZip = Path.Combine(output, ZipName);
        string checksum = Path.Combine(output, ChecksumName);
        DeleteExactOutput(root, outputZip, output);
        DeleteExactOutput(root, checksum, output);

        string build = Path.Combine(root, "build");
        EnsureOwnedBuildDirectory(root, build);
        string operationRoot = Path.Combine(
            build,
            $".package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(operationRoot);
        EnsureNoReparseComponents(root, operationRoot, "package 工作目录");
        string candidateStage = Path.Combine(operationRoot, RootName);
        string reportPath = Path.Combine(operationRoot, "self-test.txt");
        string finalStageParent = Path.Combine(build, "stage");
        string finalStage = Path.Combine(finalStageParent, RootName);

        try
        {
            AssembleStage(
                root,
                publish,
                runtime,
                candidateStage);
            ReleaseStageValidator.Validate(candidateStage, manifestPath);

            string executable = Path.Combine(
                candidateStage,
                "EMKE AI Marker.exe");
            int exitCode = await processRunner.RunAsync(
                executable,
                ["--self-test", "--report", reportPath],
                candidateStage,
                cancellationToken);
            if (exitCode != 0)
            {
                throw new ReleaseToolException(
                    $"发布包自检失败，退出码 {exitCode}。");
            }

            ValidateSelfTestReport(reportPath);
            EnsureExistingOutputDirectorySafe(root, output);
            ReleaseStageValidator.Validate(candidateStage, manifestPath);
            Directory.CreateDirectory(finalStageParent);
            EnsureNoReparseComponents(root, finalStageParent, "stage 父目录");
            ReplaceOwnedStage(
                root,
                candidateStage,
                finalStage,
                finalStageParent);
            ReleaseStageValidator.Validate(finalStage, manifestPath);

            string candidateZip = Path.Combine(
                output,
                $".{ZipName}.{Guid.NewGuid():N}.tmp");
            try
            {
                EnsureExistingOutputDirectorySafe(root, output);
                DeterministicZipWriter.Write(
                    finalStage,
                    candidateZip,
                    RootName,
                    epoch);
                string digest = ComputeSha256(candidateZip);
                EnsureExistingOutputDirectorySafe(root, output);
                File.Move(candidateZip, outputZip, overwrite: false);
                EnsureExistingOutputDirectorySafe(root, output);
                WriteChecksumAtomically(
                    root,
                    output,
                    checksum,
                    $"{digest}  {ZipName}\n");
                return new(outputZip, checksum, digest, finalStage);
            }
            finally
            {
                EnsureExistingOutputDirectorySafe(root, output);
                File.Delete(candidateZip);
            }
        }
        catch
        {
            EnsureExistingOutputDirectorySafe(root, output);
            File.Delete(outputZip);
            File.Delete(checksum);
            throw;
        }
        finally
        {
            DeleteOwnedDirectory(operationRoot, build, ".package-");
        }
    }

    private static void AssembleStage(
        string root,
        string publish,
        string runtime,
        string stage)
    {
        EnsureNoReparseComponents(root, publish, "publish 输出目录");
        EnsureNoReparseComponents(root, runtime, "ExifTool runtime");
        CopyDirectoryWithoutLinks(publish, stage);
        CopyRequiredFile(
            root,
            Path.Combine(root, "release_template", "使用说明.txt"),
            Path.Combine(stage, "使用说明.txt"),
            overwrite: true);
        CopyRequiredFile(
            root,
            Path.Combine(root, "LICENSE"),
            Path.Combine(stage, "LICENSE.txt"),
            overwrite: true);
        CopyRequiredFile(
            root,
            Path.Combine(root, "THIRD_PARTY_NOTICES.md"),
            Path.Combine(stage, "THIRD_PARTY_NOTICES.txt"),
            overwrite: true);
        CopyRequiredFile(
            root,
            Path.Combine(root, "packaging", "licenses", "dotnet", "LICENSE.txt"),
            Path.Combine(stage, "licenses", "dotnet", "LICENSE.txt"),
            overwrite: false);
        CopyRequiredFile(
            root,
            Path.Combine(
                root,
                "packaging",
                "licenses",
                "dotnet",
                "ThirdPartyNotices.txt"),
            Path.Combine(
                stage,
                "licenses",
                "dotnet",
                "ThirdPartyNotices.txt"),
            overwrite: false);
        CopyDirectoryWithoutLinks(
            runtime,
            Path.Combine(stage, "exiftool"));
        Directory.CreateDirectory(
            Path.Combine(stage, "示例输出", "EMKE 已标记"));

        string publishedLock = Path.Combine(stage, "exiftool.lock.json");
        string repositoryLock = Path.Combine(
            root,
            "packaging",
            "exiftool.lock.json");
        ReleaseStageValidator.EnsureOrdinaryFile(
            publishedLock,
            "publish 输出中的 exiftool.lock.json");
        if (!File.ReadAllBytes(publishedLock).AsSpan()
                .SequenceEqual(File.ReadAllBytes(repositoryLock)))
        {
            throw new ReleaseToolException(
                "publish 输出中的 exiftool.lock.json 与仓库锁定文件不一致。");
        }
    }

    private static void ValidateSelfTestReport(string reportPath)
    {
        ReleaseStageValidator.EnsureOrdinaryFile(
            reportPath,
            "应用自检报告");
        string[] lines = File.ReadAllLines(reportPath, Utf8)
            .Where(line => line.Length > 0)
            .ToArray();
        string[] expected =
        [
            "AppVersion=2.0.0",
            "Runtime=.NET 10",
            "ExifTool=13.59",
            "Result=ok",
        ];
        if (!lines.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new ReleaseToolException(
                "应用自检报告未包含精确的 2.0.0/.NET 10/ExifTool 13.59/Result=ok 结果。");
        }
    }

    private static void ReplaceOwnedStage(
        string root,
        string candidate,
        string finalStage,
        string stageParent)
    {
        EnsureNoReparseComponents(root, stageParent, "stage 父目录");
        ReleaseStageValidator.EnsureOrdinaryDirectory(stageParent, "stage 父目录");
        string? backup = null;
        if (Directory.Exists(finalStage))
        {
            EnsureExactChild(finalStage, stageParent, RootName);
            FileAttributes attributes = File.GetAttributes(finalStage);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ReleaseToolException(
                    $"拒绝替换链接或重解析点 stage：{finalStage}");
            }

            backup = Path.Combine(
                stageParent,
                $".{RootName}.backup-{Guid.NewGuid():N}");
            Directory.Move(finalStage, backup);
        }

        try
        {
            Directory.Move(candidate, finalStage);
        }
        catch
        {
            if (backup is not null
                && Directory.Exists(backup)
                && !Directory.Exists(finalStage))
            {
                Directory.Move(backup, finalStage);
            }

            throw;
        }

        if (backup is not null)
        {
            DeleteOwnedDirectory(
                backup,
                stageParent,
                $".{RootName}.backup-");
        }
    }

    private static void CopyDirectoryWithoutLinks(
        string source,
        string destination)
    {
        ReleaseStageValidator.EnsureOrdinaryDirectory(source, "复制源目录");
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new ReleaseToolException($"复制目标已存在：{destination}");
        }

        Directory.CreateDirectory(destination);
        CopyDirectoryContents(source, destination);
    }

    private static void CopyDirectoryContents(
        string source,
        string destination)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ReleaseToolException(
                    $"发布输入包含链接或重解析点：{entry}");
            }

            string output = Path.Combine(destination, Path.GetFileName(entry));
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                Directory.CreateDirectory(output);
                CopyDirectoryContents(entry, output);
            }
            else
            {
                File.Copy(entry, output, overwrite: false);
            }
        }
    }

    private static void CopyRequiredFile(
        string root,
        string source,
        string destination,
        bool overwrite)
    {
        EnsureNoReparseComponents(root, source, "发布输入文件");
        ReleaseStageValidator.EnsureOrdinaryFile(source, "发布输入文件");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite);
    }

    private static string RequireDescendantDirectory(
        string root,
        string candidate,
        string description)
    {
        string path = Path.GetFullPath(candidate);
        EnsureDescendant(root, path, description);
        EnsureNoReparseComponents(root, path, description);
        ReleaseStageValidator.EnsureOrdinaryDirectory(path, description);
        return path;
    }

    private static string RequirePublishDirectory(
        string root,
        string publishDirectory)
    {
        string publishRoot = Path.Combine(root, "build", "publish");
        string publish = RequireDescendantDirectory(
            root,
            publishDirectory,
            "publish 输出目录");
        string relative = Path.GetRelativePath(publishRoot, publish);
        if (relative is "." or ".."
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relative))
        {
            throw new ReleaseToolException(
                $"package 只接受 build/publish 下的具体发布输出目录：{publish}");
        }

        return publish;
    }

    private static string RequireSafeOutputDirectory(
        string root,
        string outputDirectory)
    {
        string output = Path.GetFullPath(outputDirectory);
        EnsureDescendant(root, output, "output 目录");
        EnsureNoReparseComponents(root, output, "output 目录");
        if (File.Exists(output))
        {
            throw new ReleaseToolException($"output 路径是文件：{output}");
        }

        Directory.CreateDirectory(output);
        EnsureNoReparseComponents(root, output, "output 目录");
        ReleaseStageValidator.EnsureOrdinaryDirectory(output, "output 目录");
        return output;
    }

    private static void EnsureExistingOutputDirectorySafe(
        string root,
        string output)
    {
        EnsureNoReparseComponents(root, output, "output 目录");
        ReleaseStageValidator.EnsureOrdinaryDirectory(output, "output 目录");
    }

    private static void EnsureOwnedBuildDirectory(string root, string build)
    {
        EnsureDescendant(root, build, "build 目录");
        EnsureNoReparseComponents(root, build, "build 目录");
        if (File.Exists(build))
        {
            throw new ReleaseToolException($"build 路径是文件：{build}");
        }

        Directory.CreateDirectory(build);
        EnsureNoReparseComponents(root, build, "build 目录");
        ReleaseStageValidator.EnsureOrdinaryDirectory(build, "build 目录");
    }

    private static void EnsureDescendant(
        string root,
        string candidate,
        string description)
    {
        string relative = Path.GetRelativePath(root, candidate);
        if (relative is "." or ".."
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relative))
        {
            throw new ReleaseToolException(
                $"{description}必须位于仓库根目录之下：{candidate}");
        }
    }

    private static void DeleteExactOutput(
        string root,
        string path,
        string outputDirectory)
    {
        EnsureExactChild(path, outputDirectory, Path.GetFileName(path));
        EnsureNoReparseComponents(root, outputDirectory, "output 目录");
        EnsureNoReparseComponents(root, path, "发布输出文件");
        if (Directory.Exists(path))
        {
            throw new ReleaseToolException(
                $"拒绝删除占用发布文件名的目录：{path}");
        }

        File.Delete(path);
    }

    private static void EnsureNoReparseComponents(
        string root,
        string candidate,
        string description)
    {
        string fullRoot = Path.GetFullPath(root);
        string fullCandidate = Path.GetFullPath(candidate);
        string relative = Path.GetRelativePath(fullRoot, fullCandidate);
        if (relative == ".."
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relative))
        {
            throw new ReleaseToolException(
                $"{description}必须位于已验证仓库根目录内：{fullCandidate}");
        }

        FileAttributes rootAttributes = File.GetAttributes(fullRoot);
        if (!rootAttributes.HasFlag(FileAttributes.Directory)
            || rootAttributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ReleaseToolException(
                $"仓库根目录必须是非链接普通目录：{fullRoot}");
        }

        if (relative == ".")
        {
            return;
        }

        string current = fullRoot;
        string[] segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception exception) when (
                exception is FileNotFoundException
                    or DirectoryNotFoundException)
            {
                break;
            }

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ReleaseToolException(
                    $"{description}的仓库内路径组件不能是链接或重解析点：{current}");
            }
        }
    }

    private static void EnsureExactChild(
        string path,
        string parent,
        string name)
    {
        string fullPath = Path.GetFullPath(path);
        string fullParent = Path.GetFullPath(parent);
        if (!string.Equals(
                Path.GetDirectoryName(fullPath),
                fullParent,
                PathComparison)
            || !string.Equals(Path.GetFileName(fullPath), name, StringComparison.Ordinal))
        {
            throw new ReleaseToolException($"拒绝操作范围外路径：{fullPath}");
        }
    }

    private static void DeleteOwnedDirectory(
        string path,
        string expectedParent,
        string expectedPrefix)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return;
        }

        string full = Path.GetFullPath(path);
        string parent = Path.GetFullPath(expectedParent);
        if (!string.Equals(
                Path.GetDirectoryName(full),
                parent,
                PathComparison)
            || !Path.GetFileName(full).StartsWith(
                expectedPrefix,
                StringComparison.Ordinal))
        {
            throw new ReleaseToolException(
                $"拒绝清理不属于 package command 的路径：{full}");
        }

        DeleteTreeNoFollow(full);
    }

    private static void DeleteTreeNoFollow(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint)
            || !attributes.HasFlag(FileAttributes.Directory))
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

    private static void WriteChecksumAtomically(
        string root,
        string output,
        string path,
        string content)
    {
        string directory = Path.GetDirectoryName(path)!;
        string temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            EnsureExistingOutputDirectorySafe(root, output);
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                byte[] bytes = Utf8.GetBytes(content);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            EnsureExistingOutputDirectorySafe(root, output);
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            EnsureExistingOutputDirectorySafe(root, output);
            File.Delete(temporary);
        }
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
