using System.Text.Json;

namespace Emke.AiMarker.Release.Tests;

public sealed class WindowsAcceptanceDocumentTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string[] MetadataFields =
    [
        "Windows edition/build",
        "Architecture",
        "Display scaling",
        "ZIP filename",
        "ZIP SHA-256",
        "App file version",
        "ExifTool version",
        "SmartScreen behavior",
    ];

    private static readonly string[] AllowedStatuses = ["pass", "fail", "blocked"];

    [Fact]
    public void Checklist_defines_the_complete_immutable_windows_acceptance_contract()
    {
        string checklist = Read("docs/validation/windows-11-x64-smoke.md");

        Assert.Equal(
            MetadataFields,
            TableRows(checklist, "## Required metadata")
                .Select(row => row[0])
                .ToArray());

        IReadOnlyList<string[]> steps = TableRows(checklist, "## Fourteen GUI/media steps");
        Assert.Equal(
            Enumerable.Range(1, 14).Select(value => value.ToString()),
            steps.Select(row => row[0]));
        Assert.All(
            Enumerable.Range(1, 14),
            step =>
            {
                string section = Section(checklist, $"### Step {step} —");
                Assert.Contains("PowerShell / action:", section, StringComparison.Ordinal);
                Assert.Contains("Record:", section, StringComparison.Ordinal);
                Assert.Contains("Pass condition:", section, StringComparison.Ordinal);
            });

        Assert.Contains(
            "& \".\\EMKE AI Marker.exe\" --self-test --report \".\\self-test.txt\"",
            checklist,
            StringComparison.Ordinal);
        Assert.Contains("$LASTEXITCODE", checklist, StringComparison.Ordinal);
        Assert.Contains("Get-Content .\\self-test.txt", checklist, StringComparison.Ordinal);
        Assert.Contains("Expected exit code: `0`", checklist, StringComparison.Ordinal);
        Assert.Contains("Expected final line: `Result=ok`", checklist, StringComparison.Ordinal);
        Assert.Contains(
            "Allowed item statuses: `pass`, `fail`, `blocked`.",
            checklist,
            StringComparison.Ordinal);

        Assert.Equal(
            ["100%", "150%", "200%"],
            TableRows(checklist, "## Required display-scaling matrix")
                .Select(row => row[0])
                .ToArray());

        using JsonDocument manifest = JsonDocument.Parse(
            Read("tests/fixtures/controlled/fixture-manifest.json"));
        Dictionary<string, string> expectedHashes = manifest.RootElement
            .GetProperty("files")
            .EnumerateArray()
            .ToDictionary(
                file => file.GetProperty("path").GetString()!,
                file => file.GetProperty("sha256").GetString()!,
                StringComparer.Ordinal);
        Dictionary<string, string> documentedHashes = TableRows(
                checklist,
                "## Controlled source hashes")
            .ToDictionary(row => row[0], row => row[1], StringComparer.Ordinal);
        Assert.Equal(expectedHashes, documentedHashes);
    }

    [Fact]
    public void Blocked_result_records_every_item_and_cannot_satisfy_the_acceptance_gate()
    {
        string result = Read("docs/validation/windows-11-x64-smoke-result.md");

        Assert.Contains("Acceptance date: `2026-07-27`", result, StringComparison.Ordinal);
        IReadOnlyList<string[]> identity = TableRows(result, "## Artifact and host identity");
        Assert.Equal(MetadataFields, identity.Select(row => row[0]).ToArray());
        Assert.All(identity, row => Assert.False(string.IsNullOrWhiteSpace(row[1])));

        IReadOnlyList<string[]> items = TableRows(result, "## Item results");
        Assert.Equal(15, items.Count);
        Assert.Equal(
            ["Self-test", .. Enumerable.Range(1, 14).Select(value => value.ToString())],
            items.Select(row => row[0]).ToArray());
        Assert.All(
            items,
            row =>
            {
                Assert.Contains(row[1], AllowedStatuses);
                Assert.False(string.IsNullOrWhiteSpace(row[2]));
            });

        Assert.Contains(
            "## Automated evidence (not Windows acceptance)",
            result,
            StringComparison.Ordinal);
        Assert.Contains(
            "## Real-machine evidence boundary",
            result,
            StringComparison.Ordinal);

        string finalLine = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1];
        Assert.Equal("Final result: blocked", finalLine);
        Assert.Contains(items, row => row[1] != "pass");
        Assert.NotEqual("Final result: passed", finalLine);
    }

    private static IReadOnlyList<string[]> TableRows(string document, string heading)
    {
        return Section(document, heading)
            .Split('\n')
            .Where(line => line.StartsWith('|'))
            .Select(line => line.Split('|')[1..^1].Select(cell => cell.Trim()).ToArray())
            .Where(cells => cells.Length >= 2)
            .Where(cells => !cells.All(cell => cell.All(character => character is '-' or ':')))
            .Skip(1)
            .ToArray();
    }

    private static string Section(string document, string headingPrefix)
    {
        int start = document.IndexOf(headingPrefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing section: {headingPrefix}");
        int next = document.IndexOf("\n##", start + headingPrefix.Length, StringComparison.Ordinal);
        return next < 0 ? document[start..] : document[start..next];
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(
                Path.Combine(
                    Root,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

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
}
