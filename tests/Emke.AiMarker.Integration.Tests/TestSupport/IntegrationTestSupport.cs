using System.Security.Cryptography;
using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Core.Processing;
using Emke.AiMarker.Infrastructure.ExifTool;
using Emke.AiMarker.Infrastructure.Files;

namespace Emke.AiMarker.Integration.Tests.TestSupport;

internal static class IntegrationConstants
{
    public const string ExistingSubject = "emke-existing-fixture-subject";
}

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string ControlledFixtures =>
        Path.Combine(Root, "tests", "fixtures", "controlled");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Emke.AiMarker.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate repository root from the integration test output.");
    }
}

internal static class Hashing
{
    public static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();
}

internal static class FixtureCopy
{
    public static string CreatePrivateWorkingCopy(
        string fixtureName,
        string inputDirectory)
    {
        string fixturePath = Path.Combine(
            RepositoryPaths.ControlledFixtures,
            fixtureName);
        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException(
                "Controlled fixture is missing.",
                fixturePath);
        }

        Directory.CreateDirectory(inputDirectory);
        string sourcePath = Path.Combine(inputDirectory, fixtureName);
        File.Copy(fixturePath, sourcePath, overwrite: false);
        return sourcePath;
    }
}

internal static class IntegrationPlans
{
    public static OutputPlanItem For(
        string sourcePath,
        string outputDirectory)
    {
        string name = Path.GetFileName(sourcePath);
        string finalPath = Path.Combine(outputDirectory, name);
        string tempPath = Path.Combine(
            outputDirectory,
            $".emke-ai-marker-{Guid.NewGuid():N}.tmp{Path.GetExtension(name)}");
        return new(
            sourcePath,
            name,
            finalPath,
            tempPath,
            new FileInfo(sourcePath).Length);
    }
}

internal sealed class IntegrationHarness : IAsyncDisposable
{
    private IntegrationHarness(
        string root,
        string sourcePath,
        string finalPath,
        OutputPlanItem plan)
    {
        Root = root;
        SourcePath = sourcePath;
        FinalPath = finalPath;
        Plan = plan;
    }

    public string Root { get; }

    public string SourcePath { get; }

    public string FinalPath { get; }

    public OutputPlanItem Plan { get; }

    public static IntegrationHarness Create(string fixtureName)
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "integration-workspaces",
            $".emke-ai-marker-integration-{Guid.NewGuid():N}");
        string inputDirectory = Path.Combine(root, "input");
        string outputDirectory = Path.Combine(root, "output");
        string sourcePath =
            FixtureCopy.CreatePrivateWorkingCopy(fixtureName, inputDirectory);
        OutputPlanItem plan = IntegrationPlans.For(
            sourcePath,
            outputDirectory);
        return new(root, sourcePath, plan.FinalPath, plan);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed record IntegrationServices(
    MediaProcessor Processor,
    ExifToolClient ExifTool)
{
    public static Task<IntegrationServices> CreateAsync() =>
        CreateAsync(Environment.GetEnvironmentVariable("EMKE_EXIFTOOL"));

    internal static async Task<IntegrationServices> CreateAsync(
        string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException(
                "EMKE_EXIFTOOL is required.");
        }

        string fullPath = Path.GetFullPath(executable);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "ExifTool executable not found.",
                fullPath);
        }

        var exifTool = new ExifToolClient(fullPath, new ProcessRunner());
        string version =
            await exifTool.GetVersionAsync(CancellationToken.None);
        if (!string.Equals(version, "13.59", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"ExifTool 13.59 is required. Actual: {version}");
        }

        var processor = new MediaProcessor(
            new PhysicalCopyTransaction(),
            exifTool,
            new WindowsFileSafety(),
            TimeProvider.System);
        return new(processor, exifTool);
    }
}
