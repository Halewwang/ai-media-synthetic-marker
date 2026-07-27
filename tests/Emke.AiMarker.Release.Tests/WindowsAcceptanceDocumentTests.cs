using System.Text.Json;
using Emke.AiMarker.Release.Tests.TestSupport;

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
    public void Checklist_reads_package_metadata_only_after_exact_extraction_and_checksum()
    {
        string checklist = Read("docs/validation/windows-11-x64-smoke.md");
        string extraction = Section(checklist, "### Step 1 —");

        using JsonDocument manifest = JsonDocument.Parse(
            Read("packaging/release-manifest.json"));
        string[] requiredPaths = manifest.RootElement
            .GetProperty("required_paths")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        string[] requiredFiles = requiredPaths
            .Where(path => !path.EndsWith('/'))
            .ToArray();
        string[] requiredDirectories = requiredPaths
            .Where(path => path.EndsWith('/'))
            .ToArray();
        Assert.Equal(8, requiredFiles.Length);
        Assert.Single(requiredDirectories);
        Assert.All(
            requiredFiles,
            path => Assert.Contains(
                $"\"{path.Replace('/', '\\')}\"",
                extraction,
                StringComparison.Ordinal));
        Assert.Contains("$RequiredFiles", extraction, StringComparison.Ordinal);
        Assert.Contains(
            "Test-Path -LiteralPath $RequiredPath -PathType Leaf",
            extraction,
            StringComparison.Ordinal);
        Assert.Contains(
            $"\"{requiredDirectories[0].TrimEnd('/').Replace('/', '\\')}\"",
            extraction,
            StringComparison.Ordinal);
        Assert.Contains("$RequiredEmptyDirectories", extraction, StringComparison.Ordinal);
        Assert.Contains(
            "Test-Path -LiteralPath $RequiredDirectory -PathType Container",
            extraction,
            StringComparison.Ordinal);
        Assert.Contains(
            "Get-ChildItem -LiteralPath $RequiredDirectory -Force",
            extraction,
            StringComparison.Ordinal);

        AssertPackageExecutionContract(checklist);
    }

    [Fact]
    public void Read_only_step_proves_every_output_hash_is_unchanged()
    {
        string checklist = Read("docs/validation/windows-11-x64-smoke.md");

        AssertReadOnlyExecutionContract(checklist);
    }

    [Fact]
    public void Execution_contract_rejects_comments_and_mismatch_branch_dead_code()
    {
        string checklist = Read("docs/validation/windows-11-x64-smoke.md");
        const string regexAssignment =
            "$ChecksumMatch = [regex]::Match($ChecksumLines[0], '^(?<hash>[0-9a-f]{64})  (?<filename>[^\\r\\n]+)$')";
        string commentedRegex = ReplaceRequired(
            checklist,
            regexAssignment,
            $"# {regexAssignment}");
        Assert.ThrowsAny<Exception>(
            () => AssertPackageExecutionContract(commentedRegex));

        const string gatedVersions =
            """
            if ($ActualZipHash -ne $ExpectedZipHash) {
              throw "Product ZIP SHA-256 does not match SHA256SUMS.txt."
            }

            $AppFileVersion = (Get-Item -LiteralPath $AppPath).VersionInfo.FileVersion
            $ExifToolVersion = (& $ExifToolPath -ver).Trim()
            """;
        const string versionsInsideMismatchBranch =
            """
            if ($ActualZipHash -ne $ExpectedZipHash) {
              $AppFileVersion = (Get-Item -LiteralPath $AppPath).VersionInfo.FileVersion
              $ExifToolVersion = (& $ExifToolPath -ver).Trim()
              throw "Product ZIP SHA-256 does not match SHA256SUMS.txt."
            }
            """;
        string deadBranchVersions = ReplaceRequired(
            checklist,
            gatedVersions,
            versionsInsideMismatchBranch);
        Assert.ThrowsAny<Exception>(
            () => AssertPackageExecutionContract(deadBranchVersions));

        const string aggregateThrow =
            "throw \"Read-only verification changed output bytes: $($OutputHashMismatches -join ', ')\"";
        string commentedAggregateThrow = ReplaceRequired(
            checklist,
            aggregateThrow,
            $"# {aggregateThrow}");
        Assert.ThrowsAny<Exception>(
            () => AssertReadOnlyExecutionContract(commentedAggregateThrow));
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

    private static void AssertPackageExecutionContract(string checklist)
    {
        PowerShellBlock initializationBlock = Assert.Single(
            PowerShellDocumentAnalysis.BlocksInSection(
                checklist,
                "## Required metadata"));
        PowerShellBlock extractionBlock = Assert.Single(
            PowerShellDocumentAnalysis.BlocksInSection(
                checklist,
                "### Step 1 —"));
        PowerShellBlock checksumBlock = Assert.Single(
            PowerShellDocumentAnalysis.BlocksInSection(
                checklist,
                "### Step 2 —"));
        PowerShellAst initialization = PowerShellDocumentAnalysis.Analyze(
            initializationBlock);
        PowerShellAst extraction = PowerShellDocumentAnalysis.Analyze(
            extractionBlock);
        PowerShellAst checksum = PowerShellDocumentAnalysis.Analyze(checksumBlock);

        Assert.DoesNotContain(
            initialization.Commands,
            command =>
                command.Label.Equals("Get-Item", StringComparison.OrdinalIgnoreCase)
                && command.Text.Contains("$AppPath", StringComparison.Ordinal));
        Assert.DoesNotContain(
            initialization.Commands,
            command => command.Text.TrimStart().StartsWith(
                "& $ExifToolPath",
                StringComparison.Ordinal));

        PowerShellAstNode expand = Assert.Single(
            extraction.Commands,
            command => command.Label.Equals(
                "Expand-Archive",
                StringComparison.OrdinalIgnoreCase));
        PowerShellAstNode lineCount = TopLevelIf(
            checksum,
            "$ChecksumLines.Count -ne 1");
        PowerShellAstNode invalidFormat = TopLevelIf(
            checksum,
            "-not $ChecksumMatch.Success");
        PowerShellAstNode wrongFilename = TopLevelIf(
            checksum,
            "$ChecksumZipFileName -cne $ExpectedZipFileName");
        PowerShellAstNode wrongHash = TopLevelIf(
            checksum,
            "$ActualZipHash -ne $ExpectedZipHash");
        Assert.True(checksum.HasDirectThrow(lineCount));
        Assert.True(checksum.HasDirectThrow(invalidFormat));
        Assert.True(checksum.HasDirectThrow(wrongFilename));
        Assert.True(checksum.HasDirectThrow(wrongHash));

        PowerShellAstNode checksumLines = TopLevelAssignment(
            checksum,
            "$ChecksumLines");
        Assert.Contains(
            "@(Get-Content -LiteralPath $ChecksumPath -Encoding utf8)",
            checksumLines.Text,
            StringComparison.Ordinal);
        PowerShellAstNode checksumMatch = TopLevelAssignment(
            checksum,
            "$ChecksumMatch");
        Assert.Contains(
            "'^(?<hash>[0-9a-f]{64})  (?<filename>[^\\r\\n]+)$'",
            checksumMatch.Text,
            StringComparison.Ordinal);
        PowerShellAstNode expectedFilename = TopLevelAssignment(
            checksum,
            "$ExpectedZipFileName");
        Assert.Contains(
            "Split-Path -Path $ZipPath -Leaf",
            expectedFilename.Text,
            StringComparison.Ordinal);
        PowerShellAstNode checksumFilename = TopLevelAssignment(
            checksum,
            "$ChecksumZipFileName");
        Assert.Contains(
            "$ChecksumMatch.Groups[\"filename\"].Value",
            checksumFilename.Text,
            StringComparison.Ordinal);
        PowerShellAstNode expectedHash = TopLevelAssignment(
            checksum,
            "$ExpectedZipHash");
        Assert.Contains(
            "$ChecksumMatch.Groups[\"hash\"].Value",
            expectedHash.Text,
            StringComparison.Ordinal);
        PowerShellAstNode actualHash = TopLevelAssignment(
            checksum,
            "$ActualZipHash");
        Assert.Contains(
            "Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256",
            actualHash.Text,
            StringComparison.Ordinal);
        PowerShellAstNode appVersion = TopLevelAssignment(
            checksum,
            "$AppFileVersion");
        PowerShellAstNode exifToolVersion = TopLevelAssignment(
            checksum,
            "$ExifToolVersion");

        AssertOrdered(
            checksumLines,
            lineCount,
            checksumMatch,
            invalidFormat,
            expectedFilename,
            checksumFilename,
            wrongFilename,
            expectedHash,
            actualHash,
            wrongHash,
            appVersion,
            exifToolVersion);
        Assert.True(
            extraction.GlobalStart(expand)
            < checksum.GlobalStart(appVersion));
        Assert.True(
            extraction.GlobalStart(expand)
            < checksum.GlobalStart(exifToolVersion));
    }

    private static void AssertReadOnlyExecutionContract(string checklist)
    {
        IReadOnlyList<PowerShellBlock> blocks =
            PowerShellDocumentAnalysis.BlocksInSection(
                checklist,
                "### Step 8 —");
        Assert.Equal(3, blocks.Count);
        PowerShellAst before = PowerShellDocumentAnalysis.Analyze(blocks[0]);
        PowerShellAst after = PowerShellDocumentAnalysis.Analyze(blocks[1]);
        PowerShellAst exifTool = PowerShellDocumentAnalysis.Analyze(blocks[2]);

        PowerShellAstNode beforeHashes = TopLevelAssignment(
            before,
            "$BeforeOutputHashes");
        PowerShellAstNode afterHashes = TopLevelAssignment(
            after,
            "$AfterOutputHashes");
        PowerShellAstNode mismatches = TopLevelAssignment(
            after,
            "$OutputHashMismatches");
        PowerShellAstNode aggregate = TopLevelIf(
            after,
            "$OutputHashMismatches.Count -ne 0");
        Assert.True(after.HasDirectThrow(aggregate));
        PowerShellAstNode beforeLoop = Assert.Single(
            before.TopLevelStatements,
            node =>
                node.Label == "ForEachStatementAst"
                && node.Text.StartsWith(
                    "foreach ($OutputFile in $OutputFiles)",
                    StringComparison.Ordinal));
        Assert.Contains(
            before.Commands,
            command =>
                command.Label.Equals(
                    "Get-FileHash",
                    StringComparison.OrdinalIgnoreCase)
                && command.Start >= beforeLoop.Start
                && command.End <= beforeLoop.End);
        PowerShellAstNode afterLoop = Assert.Single(
            after.TopLevelStatements,
            node =>
                node.Label == "ForEachStatementAst"
                && node.Text.StartsWith(
                    "foreach ($OutputFile in $OutputFiles)",
                    StringComparison.Ordinal));
        Assert.Contains(
            after.Assignments,
            assignment =>
                assignment.Label == "$OutputHashMismatches"
                && assignment.Text.Contains(
                    "+= $OutputFile.Name",
                    StringComparison.Ordinal)
                && assignment.Start >= afterLoop.Start
                && assignment.End <= afterLoop.End);
        PowerShellAstNode evidence = Assert.Single(
            after.Hashtables,
            table =>
                table.Label == "File,Before,After,Equal"
                && table.Start >= afterLoop.Start
                && table.End <= afterLoop.End);
        PowerShellAstNode equality = Assert.Single(
            after.BinaryExpressions,
            expression =>
                expression.Text ==
                    "$AfterOutputHashes[$OutputFile.Name] -eq $BeforeOutputHashes[$OutputFile.Name]"
                && expression.Start >= evidence.Start
                && expression.End <= evidence.End);
        Assert.Contains(
            after.Commands,
            command =>
                command.Label.Equals(
                    "Get-FileHash",
                    StringComparison.OrdinalIgnoreCase)
                && command.Start >= afterLoop.Start
                && command.End <= afterLoop.End);
        PowerShellAstNode independentExifTool = Assert.Single(
            exifTool.Commands,
            command => command.Text.Contains(
                "-XMP-dc:Subject",
                StringComparison.Ordinal));

        int click = checklist.IndexOf(
            "click “只读验证”",
            blocks[0].ContentEnd,
            StringComparison.Ordinal);
        Assert.InRange(click, blocks[0].ContentEnd, blocks[1].ContentStart - 1);
        Assert.True(beforeHashes.Start >= 0);
        Assert.True(equality.Start >= 0);
        AssertOrdered(beforeHashes, beforeLoop);
        AssertOrdered(afterHashes, mismatches, afterLoop, aggregate);
        Assert.True(
            after.GlobalStart(aggregate)
            < exifTool.GlobalStart(independentExifTool));
    }

    private static PowerShellAstNode TopLevelAssignment(
        PowerShellAst ast,
        string variable)
    {
        PowerShellAstNode statement = Assert.Single(
            ast.TopLevelStatements,
            node =>
                node.Label == "AssignmentStatementAst"
                && node.Text.TrimStart().StartsWith(
                    $"{variable} =",
                    StringComparison.Ordinal));
        Assert.Contains(
            ast.Assignments,
            assignment =>
                assignment.Start == statement.Start
                && assignment.Label == variable);
        return statement;
    }

    private static PowerShellAstNode TopLevelIf(
        PowerShellAst ast,
        string condition)
    {
        PowerShellAstNode statement = Assert.Single(
            ast.TopLevelStatements,
            node =>
                node.Label == "IfStatementAst"
                && ast.IfStatements.Any(candidate =>
                    candidate.Start == node.Start
                    && candidate.Label.Contains(
                        condition,
                        StringComparison.Ordinal)));
        return statement;
    }

    private static void AssertOrdered(params PowerShellAstNode[] statements)
    {
        Assert.All(
            statements.Zip(statements.Skip(1)),
            pair => Assert.True(
                pair.First.End <= pair.Second.Start,
                $"Expected '{pair.First.Text}' before '{pair.Second.Text}'."));
    }

    private static string ReplaceRequired(
        string value,
        string oldValue,
        string newValue)
    {
        Assert.Contains(oldValue, value, StringComparison.Ordinal);
        return value.Replace(oldValue, newValue, StringComparison.Ordinal);
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
