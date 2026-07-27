using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Emke.AiMarker.Release.Tests.TestSupport;

internal sealed record PowerShellBlock(
    int ContentStart,
    int ContentEnd,
    string Content);

internal sealed record PowerShellAstNode(
    char Kind,
    int Start,
    int End,
    string Label,
    string Text);

internal sealed class PowerShellAst(
    PowerShellBlock block,
    IReadOnlyList<PowerShellAstNode> nodes)
{
    public PowerShellBlock Block { get; } = block;

    public IReadOnlyList<PowerShellAstNode> TopLevelStatements { get; } =
        nodes.Where(node => node.Kind == 'S').ToArray();

    public IReadOnlyList<PowerShellAstNode> Commands { get; } =
        nodes.Where(node => node.Kind == 'C').ToArray();

    public IReadOnlyList<PowerShellAstNode> Assignments { get; } =
        nodes.Where(node => node.Kind == 'A').ToArray();

    public IReadOnlyList<PowerShellAstNode> IfStatements { get; } =
        nodes.Where(node => node.Kind == 'I').ToArray();

    public IReadOnlyList<PowerShellAstNode> Throws { get; } =
        nodes.Where(node => node.Kind == 'T').ToArray();

    public IReadOnlyList<PowerShellAstNode> Hashtables { get; } =
        nodes.Where(node => node.Kind == 'H').ToArray();

    public IReadOnlyList<PowerShellAstNode> BinaryExpressions { get; } =
        nodes.Where(node => node.Kind == 'B').ToArray();

    public bool HasDirectThrow(PowerShellAstNode node) =>
        Throws.Any(statement =>
            statement.Label == node.Start.ToString(
                System.Globalization.CultureInfo.InvariantCulture));

    public int GlobalStart(PowerShellAstNode node) =>
        Block.ContentStart + node.Start;
}

internal static class PowerShellDocumentAnalysis
{
    private const string AnalyzerScript =
        """
        $source = [System.IO.File]::ReadAllText($env:EMKE_AST_SOURCE_PATH)
        $tokens = $null
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseInput(
          $source,
          [ref]$tokens,
          [ref]$errors)
        if ($errors.Count -ne 0) {
          foreach ($parseError in $errors) {
            [Console]::Error.WriteLine($parseError.Message)
          }
          exit 2
        }
        function Emit-Node {
          param(
            [string]$Kind,
            [System.Management.Automation.Language.Ast]$Node,
            [string]$Label
          )
          $label64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Label))
          $text64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Node.Extent.Text))
          [Console]::WriteLine(
            "$Kind|$($Node.Extent.StartOffset)|$($Node.Extent.EndOffset)|$label64|$text64")
        }
        foreach ($statement in $ast.EndBlock.Statements) {
          Emit-Node "S" $statement $statement.GetType().Name
        }
        foreach ($node in $ast.FindAll(
          { param($candidate) $candidate -is [System.Management.Automation.Language.CommandAst] },
          $true)) {
          Emit-Node "C" $node ([string]$node.GetCommandName())
        }
        foreach ($node in $ast.FindAll(
          { param($candidate) $candidate -is [System.Management.Automation.Language.AssignmentStatementAst] },
          $true)) {
          Emit-Node "A" $node $node.Left.Extent.Text
        }
        foreach ($node in $ast.FindAll(
          { param($candidate) $candidate -is [System.Management.Automation.Language.IfStatementAst] },
          $true)) {
          Emit-Node "I" $node $node.Clauses[0].Item1.Extent.Text
        }
        foreach ($node in $ast.FindAll(
          { param($candidate) $candidate -is [System.Management.Automation.Language.ThrowStatementAst] },
          $true)) {
          $owner = $node.Parent.Parent
          $ownerStart = if (
            $owner -is [System.Management.Automation.Language.IfStatementAst]) {
            $owner.Extent.StartOffset
          } else {
            -1
          }
          Emit-Node "T" $node ([string]$ownerStart)
        }
        foreach ($node in $ast.FindAll(
          { param($candidate) $candidate -is [System.Management.Automation.Language.HashtableAst] },
          $true)) {
          $keys = @()
          foreach ($pair in $node.KeyValuePairs) {
            $keys += $pair.Item1.Extent.Text
          }
          Emit-Node "H" $node ([string]::Join(",", $keys))
        }
        foreach ($node in $ast.FindAll(
          { param($candidate) $candidate -is [System.Management.Automation.Language.BinaryExpressionAst] },
          $true)) {
          Emit-Node "B" $node ([string]$node.Operator)
        }
        """;

    private static readonly Regex PowerShellFence = new(
        @"^```powershell\r?\n(?<code>.*?)^```[ \t]*$",
        RegexOptions.CultureInvariant
        | RegexOptions.Multiline
        | RegexOptions.Singleline);

    public static IReadOnlyList<PowerShellBlock> BlocksInSection(
        string document,
        string headingPrefix)
    {
        int sectionStart = document.IndexOf(headingPrefix, StringComparison.Ordinal);
        Assert.True(sectionStart >= 0, $"Missing section: {headingPrefix}");
        int sectionEnd = document.IndexOf(
            "\n##",
            sectionStart + headingPrefix.Length,
            StringComparison.Ordinal);
        if (sectionEnd < 0)
        {
            sectionEnd = document.Length;
        }

        string section = document[sectionStart..sectionEnd];
        return PowerShellFence.Matches(section)
            .Cast<Match>()
            .Select(match =>
            {
                Group code = match.Groups["code"];
                int start = sectionStart + code.Index;
                return new PowerShellBlock(
                    start,
                    start + code.Length,
                    code.Value);
            })
            .ToArray();
    }

    public static PowerShellAst Analyze(PowerShellBlock block)
    {
        string sourcePath = Path.Combine(
            Path.GetTempPath(),
            $"emke-ai-marker-doc-ast-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(sourcePath, block.Content, new UTF8Encoding(false));
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = FindPowerShell(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("-NoLogo");
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
            process.StartInfo.ArgumentList.Add("-Command");
            process.StartInfo.ArgumentList.Add(AnalyzerScript);
            process.StartInfo.Environment["EMKE_AST_SOURCE_PATH"] = sourcePath;

            Assert.True(process.Start(), "Could not start PowerShell AST parser.");
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            Assert.True(
                process.WaitForExit(15_000),
                "PowerShell AST parser did not exit within 15 seconds.");
            string output = standardOutput.GetAwaiter().GetResult();
            string error = standardError.GetAwaiter().GetResult();
            Assert.True(
                process.ExitCode == 0,
                $"PowerShell AST parser failed with exit {process.ExitCode}: {error}");

            PowerShellAstNode[] nodes = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseNode)
                .ToArray();
            return new PowerShellAst(block, nodes);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    private static PowerShellAstNode ParseNode(string line)
    {
        string[] fields = line.TrimEnd('\r').Split('|');
        Assert.Equal(5, fields.Length);
        Assert.Single(fields[0]);
        return new(
            fields[0][0],
            int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture),
            Decode(fields[3]),
            Decode(fields[4]));
    }

    private static string Decode(string value) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private static string FindPowerShell()
    {
        string root = FindRepositoryRoot();
        string[] localCandidates = OperatingSystem.IsWindows()
            ?
            [
                Path.Combine(root, ".superpowers", "toolchain", "powershell", "pwsh.exe"),
                Path.Combine(root, ".superpowers", "toolchain", "powershell", "pwsh"),
            ]
            :
            [
                Path.Combine(root, ".superpowers", "toolchain", "powershell", "pwsh"),
            ];
        return localCandidates.FirstOrDefault(File.Exists)
            ?? (OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
    }

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
