using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Emke.AiMarker.Release.Tests;

public sealed partial class RepositoryPromotionTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string[] LegacyPaths =
    [
        "src/ai_media_marker.py",
        "tests/test_ai_media_marker.py",
        "tests/test_fetch_exiftool.py",
        "tests/test_release_hygiene.py",
        "scripts/fetch_exiftool.py",
        "scripts/build_release.py",
        "packaging/marker_app.spec",
        "packaging/licenses/Tcl-8.6-license.terms",
        "packaging/licenses/Tk-8.6-license.terms",
        "pyproject.toml",
        "requirements-build.lock",
        "开发运行.cmd",
    ];

    [Fact]
    public void Dotnet_v2_is_the_repository_truth_and_python_v1_is_archived()
    {
        foreach (string relativePath in LegacyPaths)
        {
            Assert.False(
                File.Exists(PathInRepository(relativePath)),
                $"Legacy Python path remains at repository root: {relativePath}");
            Assert.True(
                File.Exists(PathInRepository($"legacy/python/{relativePath}")),
                $"Archived Python v1 path is missing: {relativePath}");
        }

        Assert.True(File.Exists(PathInRepository("Emke.AiMarker.sln")));
        Assert.True(Directory.Exists(PathInRepository("src/Emke.AiMarker.App")));
        Assert.True(Directory.Exists(PathInRepository("src/Emke.AiMarker.Core")));
        Assert.True(
            Directory.Exists(PathInRepository("src/Emke.AiMarker.Infrastructure")));

        string legacyReadme = Read("legacy/python/README.md");
        Assert.Contains(
            """
            This directory preserves the v1.0.0 Python/Tkinter implementation for
            one major release cycle as a behavior reference. It is not built,
            packaged, or shipped by the EMKE AI Marker v2 product.
            """,
            legacyReadme,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Production_documentation_uses_only_the_dotnet_v2_toolchain()
    {
        string readme = Read("README.md");
        string building = Read("BUILDING.md");
        string agents = Read("AGENTS.md");
        string contributing = Read("CONTRIBUTING.md");
        string runtimeReadme = Read("runtime/exiftool/README.md");
        string productionDocs = string.Join(
            "\n",
            readme,
            building,
            agents,
            contributing,
            runtimeReadme);

        Assert.Contains("EMKE AI Marker v2", readme, StringComparison.Ordinal);
        Assert.Contains("内部预览", readme, StringComparison.Ordinal);
        Assert.Contains("安全副本", readme, StringComparison.Ordinal);
        Assert.Contains("高级原件模式", readme, StringComparison.Ordinal);
        Assert.Contains("SmartScreen", readme, StringComparison.Ordinal);
        Assert.Contains("Windows x64", building, StringComparison.Ordinal);
        Assert.Contains(".NET SDK 10.0.100", building, StringComparison.Ordinal);
        Assert.Contains(
            "dotnet restore Emke.AiMarker.sln --locked-mode",
            building,
            StringComparison.Ordinal);
        Assert.Contains(
            "scripts\\fetch-exiftool.ps1",
            building,
            StringComparison.Ordinal);
        Assert.Contains(
            "scripts\\build-release.ps1",
            building,
            StringComparison.Ordinal);
        Assert.Contains(
            "-overwrite_original_in_place",
            agents,
            StringComparison.Ordinal);
        Assert.Contains(
            "-overwrite_original",
            agents,
            StringComparison.Ordinal);
        Assert.Contains("FFmpeg 7.1.1", contributing, StringComparison.Ordinal);
        Assert.Contains("ExifTool 13.59", contributing, StringComparison.Ordinal);

        Assert.DoesNotContain("py -3.14", productionDocs, StringComparison.Ordinal);
        Assert.DoesNotContain("setup-python", productionDocs, StringComparison.Ordinal);
        Assert.DoesNotContain("PyInstaller", productionDocs, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "scripts\\build_release.py",
            productionDocs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "scripts\\fetch_exiftool.py",
            productionDocs,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Exact_sdk_and_release_test_helper_are_part_of_the_build_contract()
    {
        using JsonDocument globalJson =
            JsonDocument.Parse(Read("global.json"));
        JsonElement sdk = globalJson.RootElement.GetProperty("sdk");

        Assert.Equal("10.0.100", sdk.GetProperty("version").GetString());
        Assert.Equal("disable", sdk.GetProperty("rollForward").GetString());

        string solution = Read("Emke.AiMarker.sln");
        Assert.Contains(
            "Emke.AiMarker.ProcessRunner.TestHelper",
            solution,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_fetches_locked_exiftool_before_the_complete_solution_test()
    {
        string workflow = Read(".github/workflows/ci.yml");

        Assert.Contains("runs-on: windows-2022", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("global-json-file: global.json", workflow, StringComparison.Ordinal);
        Assert.Contains("cache: true", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "cache-dependency-path: '**/packages.lock.json'",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "dotnet restore Emke.AiMarker.sln --locked-mode",
            workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain("setup-python", workflow, StringComparison.Ordinal);
        AssertAllActionsArePinned(workflow);

        int fetch = workflow.IndexOf(
            "pwsh scripts/fetch-exiftool.ps1",
            StringComparison.Ordinal);
        int environment = workflow.IndexOf(
            "EMKE_EXIFTOOL=",
            StringComparison.Ordinal);
        int test = workflow.IndexOf(
            "dotnet test Emke.AiMarker.sln -c Release --no-restore",
            StringComparison.Ordinal);
        Assert.True(fetch >= 0, "CI must fetch the locked ExifTool runtime.");
        Assert.True(
            environment > fetch,
            "CI must export EMKE_EXIFTOOL after locked acquisition.");
        Assert.True(
            test > environment,
            "The complete solution test must run after EMKE_EXIFTOOL is exported.");
        Assert.Single(
            Regex.Matches(
                    workflow,
                    "dotnet test Emke\\.AiMarker\\.sln",
                    RegexOptions.CultureInvariant)
                .Cast<Match>());
    }

    [Fact]
    public void Release_workflow_builds_manually_but_publishes_only_verified_v2_tags()
    {
        string workflow = Read(".github/workflows/release.yml");

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("- \"v*\"", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "actions/download-artifact@37930b1c2abaa49bbe596cd826c3c89aef350131",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("Directory.Build.props", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "scripts/build-release.ps1",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "if: startsWith(github.ref, 'refs/tags/')",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("gh release create", workflow, StringComparison.Ordinal);
        Assert.Contains("--verify-tag", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "v1.0.0 is immutable and cannot be used for EMKE AI Marker v2",
            workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain("setup-python", workflow, StringComparison.Ordinal);
        AssertAllActionsArePinned(workflow);
    }

    [Fact]
    public void Production_notices_and_package_instructions_exclude_legacy_runtimes()
    {
        string notices = Read("THIRD_PARTY_NOTICES.md");
        string instructions = Read("release_template/使用说明.txt");

        Assert.Contains("Production package", notices, StringComparison.Ordinal);
        Assert.Contains(".NET 10", notices, StringComparison.Ordinal);
        Assert.Contains("ExifTool 13.59", notices, StringComparison.Ordinal);
        Assert.Contains("Legacy source only", notices, StringComparison.Ordinal);
        Assert.Contains("not included in the v2 ZIP", notices, StringComparison.Ordinal);
        Assert.Contains("EMKE AI Marker.exe", instructions, StringComparison.Ordinal);
        Assert.Contains("添加文件", instructions, StringComparison.Ordinal);
        Assert.Contains("拖放", instructions, StringComparison.Ordinal);
        Assert.Contains("安全副本", instructions, StringComparison.Ordinal);
        Assert.Contains("只读验证", instructions, StringComparison.Ordinal);
        Assert.Contains("高级原件模式", instructions, StringComparison.Ordinal);
        Assert.Contains("示例输出", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("bundled Python", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("包含 Python", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("待标记”文件夹", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_template_tracks_only_the_v2_example_output_placeholder()
    {
        Assert.False(Directory.Exists(PathInRepository("release_template/待标记")));
        Assert.False(Directory.Exists(PathInRepository("release_template/运行记录")));

        string exampleOutput = PathInRepository(
            "release_template/示例输出/EMKE 已标记");
        Assert.True(
            Directory.Exists(exampleOutput),
            "The v2 example output directory is missing.");
        string[] entries = Directory
            .EnumerateFileSystemEntries(
                exampleOutput,
                "*",
                SearchOption.AllDirectories)
            .Select(entry => Path.GetRelativePath(exampleOutput, entry))
            .ToArray();
        Assert.Equal([".gitkeep"], entries);
        Assert.DoesNotContain(
            entries,
            entry => new[] { ".jpg", ".jpeg", ".png", ".mp4", ".csv" }
                .Contains(Path.GetExtension(entry), StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Official_dotnet_10_license_bytes_are_retained()
    {
        Assert.Equal(
            "cfc21f5e8bd655ae997eec916138b707b1d290b83272c02a95c9f821b8c87310",
            Sha256("packaging/licenses/dotnet/LICENSE.txt"));
        Assert.Equal(
            "2dc8f8c5a39401e928b5784ab564eb8b3ceb99ead3df8f260e0cab7e0bbecc7a",
            Sha256("packaging/licenses/dotnet/ThirdPartyNotices.txt"));
    }

    [Fact]
    public void Directory_build_props_keeps_exact_v2_release_version()
    {
        XDocument document = XDocument.Load(
            PathInRepository("Directory.Build.props"));
        Assert.Equal(
            "2.0.0",
            document.Descendants("Version").Single().Value);
    }

    private static void AssertAllActionsArePinned(string workflow)
    {
        MatchCollection uses = ActionReference().Matches(workflow);
        Assert.NotEmpty(uses);
        Assert.All(
            uses.Cast<Match>(),
            match => Assert.Matches(
                "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}$",
                match.Groups[1].Value));
    }

    private static string Sha256(string relativePath) =>
        Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(PathInRepository(relativePath))))
            .ToLowerInvariant();

    private static string Read(string relativePath) =>
        File.ReadAllText(PathInRepository(relativePath))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string PathInRepository(string relativePath) =>
        Path.Combine(
            Root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRepositoryRoot()
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

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    [GeneratedRegex(
        @"(?m)^\s*uses:\s*([^\s#]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ActionReference();
}
