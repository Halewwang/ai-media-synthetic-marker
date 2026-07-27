using Emke.AiMarker.Release.Commands;
using Emke.AiMarker.Release.Packaging;

return await ReleaseProgram.RunAsync(args);

internal static class ReleaseProgram
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        try
        {
            if (arguments.Length == 0)
            {
                throw new ReleaseToolException(
                    "需要命令：fetch-exiftool 或 package。");
            }

            string command = arguments[0];
            CommandLine options = CommandLine.Parse(arguments[1..]);
            string repositoryRoot = options.GetOptional("--repo-root")
                is { } configuredRoot
                ? Path.GetFullPath(configuredRoot)
                : FindRepositoryRoot();
            using var downloader = new HttpArchiveDownloader();
            var versionProbe = new ProcessVersionProbe();

            switch (command)
            {
                case "fetch-exiftool":
                    {
                        options.AssertOnly(
                            "--repo-root",
                            "--target",
                            "--archive",
                            "--force");
                        string target = options.GetOptional("--target")
                            ?? Path.Combine(repositoryRoot, "runtime", "exiftool");
                        string lockPath = Path.Combine(
                            repositoryRoot,
                            "packaging",
                            "exiftool.lock.json");
                        await new FetchExifToolCommand(
                                downloader,
                                versionProbe)
                            .ExecuteAsync(
                                lockPath,
                                target,
                                options.GetOptional("--archive"),
                                options.HasFlag("--force"),
                                CancellationToken.None);
                        Console.WriteLine(
                            $"ExifTool 13.59 已验证：{Path.GetFullPath(target)}");
                        return 0;
                    }

                case "package":
                    {
                        options.AssertOnly(
                            "--repo-root",
                            "--publish-dir",
                            "--output-dir");
                        string publish = options.GetRequired("--publish-dir");
                        string output = options.GetRequired("--output-dir");
                        long epoch = DeterministicZipWriter.ResolveEpoch(
                            Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH"));
                        PackageResult result = await new PackageCommand(
                                new PackageProcessRunner(),
                                versionProbe)
                            .ExecuteAsync(
                                repositoryRoot,
                                publish,
                                output,
                                epoch,
                                CancellationToken.None);
                        Console.WriteLine($"ZIP: {result.ZipPath}");
                        Console.WriteLine($"SHA-256: {result.Sha256}");
                        Console.WriteLine($"Checksums: {result.ChecksumPath}");
                        return 0;
                    }

                default:
                    throw new ReleaseToolException($"未知命令：{command}");
            }
        }
        catch (Exception exception) when (
            exception is ReleaseToolException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or System.Text.Json.JsonException
                or HttpRequestException)
        {
            Console.Error.WriteLine($"错误：{exception.Message}");
            return 1;
        }
    }

    private static string FindRepositoryRoot()
    {
        foreach (string start in new[]
                 {
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory,
                 })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (File.Exists(
                        Path.Combine(directory.FullName, "Emke.AiMarker.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new ReleaseToolException(
            "无法定位仓库根目录；请使用 --repo-root 指定。");
    }

    private sealed class CommandLine
    {
        private readonly Dictionary<string, string?> values;

        private CommandLine(Dictionary<string, string?> values)
        {
            this.values = values;
        }

        public static CommandLine Parse(IReadOnlyList<string> arguments)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (int index = 0; index < arguments.Count; index++)
            {
                string name = arguments[index];
                if (!name.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ReleaseToolException($"无效参数：{name}");
                }

                if (values.ContainsKey(name))
                {
                    throw new ReleaseToolException($"参数不能重复：{name}");
                }

                if (string.Equals(name, "--force", StringComparison.Ordinal))
                {
                    values.Add(name, null);
                    continue;
                }

                if (index + 1 >= arguments.Count
                    || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ReleaseToolException($"参数缺少值：{name}");
                }

                values.Add(name, arguments[++index]);
            }

            return new(values);
        }

        public void AssertOnly(params string[] allowed)
        {
            var allowedSet = new HashSet<string>(
                allowed,
                StringComparer.Ordinal);
            string? unknown = values.Keys.FirstOrDefault(
                key => !allowedSet.Contains(key));
            if (unknown is not null)
            {
                throw new ReleaseToolException($"此命令不支持参数：{unknown}");
            }
        }

        public string GetRequired(string name) =>
            GetOptional(name)
            ?? throw new ReleaseToolException($"缺少必需参数：{name}");

        public string? GetOptional(string name) =>
            values.TryGetValue(name, out string? value) ? value : null;

        public bool HasFlag(string name) => values.ContainsKey(name);
    }
}
