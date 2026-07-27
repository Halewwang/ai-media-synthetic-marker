using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Emke.AiMarker.Release.Tests.TestSupport;

internal sealed record PowerShellBlock(
    int ContentStart,
    int ContentEnd,
    string SectionHeading,
    string Content);

internal sealed record PowerShellAnalysisOptions(
    string AnalyzerScript,
    TimeSpan Timeout,
    IReadOnlyDictionary<string, string>? Environment = null);

internal sealed record PowerShellAstNode(
    char Kind,
    int Start,
    int End,
    string Label,
    string Text,
    int ParentStart,
    int RelatedStart);

internal sealed class PowerShellAst(
    PowerShellBlock block,
    IReadOnlyList<PowerShellAstNode> nodes)
{
    public PowerShellBlock Block { get; } = block;

    public IReadOnlyList<PowerShellAstNode> TopLevelStatements { get; } =
        nodes.Where(node => node.Kind == 'S').ToArray();

    public IReadOnlyList<PowerShellAstNode> Statements { get; } =
        nodes.Where(node => node.Kind == 'D').ToArray();

    public IReadOnlyList<PowerShellAstNode> Commands { get; } =
        nodes.Where(node => node.Kind == 'C').ToArray();

    public IReadOnlyList<PowerShellAstNode> Assignments { get; } =
        nodes.Where(node => node.Kind == 'A').ToArray();

    public IReadOnlyList<PowerShellAstNode> IfStatements { get; } =
        nodes.Where(node => node.Kind == 'I').ToArray();

    public IReadOnlyList<PowerShellAstNode> IfClauses { get; } =
        nodes.Where(node => node.Kind == 'K').ToArray();

    public IReadOnlyList<PowerShellAstNode> Throws { get; } =
        nodes.Where(node => node.Kind == 'T').ToArray();

    public IReadOnlyList<PowerShellAstNode> Hashtables { get; } =
        nodes.Where(node => node.Kind == 'H').ToArray();

    public IReadOnlyList<PowerShellAstNode> BinaryExpressions { get; } =
        nodes.Where(node => node.Kind == 'B').ToArray();

    public bool HasDirectThrow(PowerShellAstNode node)
    {
        PowerShellAstNode? ifStatement = IfStatements.SingleOrDefault(
            statement => statement.Start == node.Start);
        PowerShellAstNode? matchingClause = IfClauses.SingleOrDefault(
            clause =>
                clause.ParentStart == node.Start
                && clause.Label == ifStatement?.Label);
        return ifStatement is not null
            && matchingClause is not null
            && Throws.Any(statement =>
                statement.ParentStart == matchingClause.Start);
    }

    public int GlobalStart(PowerShellAstNode node) =>
        Block.ContentStart + node.Start;
}

internal static class PowerShellDocumentAnalysis
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

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
        $nodes = [System.Collections.Generic.List[object]]::new()
        function Get-StatementBlockStart {
          param([System.Management.Automation.Language.Ast]$Node)
          $parent = $Node.Parent
          while (
            $null -ne $parent -and
            $parent -isnot [System.Management.Automation.Language.StatementBlockAst]) {
            $parent = $parent.Parent
          }
          if ($null -eq $parent) {
            return -1
          }
          return $parent.Extent.StartOffset
        }
        function Add-Node {
          param(
            [string]$Kind,
            [System.Management.Automation.Language.Ast]$Node,
            [string]$Label,
            [int]$ParentStart = -1,
            [int]$RelatedStart = -1
          )
          $item = [System.Collections.Generic.Dictionary[string, object]]::new()
          $item["kind"] = $Kind
          $item["start"] = $Node.Extent.StartOffset
          $item["end"] = $Node.Extent.EndOffset
          $item["label"] = $Label
          $item["text"] = $Node.Extent.Text
          $item["parentStart"] = $ParentStart
          $item["relatedStart"] = $RelatedStart
          $null = $nodes.Add($item)
        }
        foreach ($statement in $ast.EndBlock.Statements) {
          $bodyStart = if (
            $statement -is [System.Management.Automation.Language.ForEachStatementAst]) {
            $statement.Body.Extent.StartOffset
          } else {
            -1
          }
          Add-Node "S" $statement $statement.GetType().Name `
            $ast.EndBlock.Extent.StartOffset $bodyStart
        }
        foreach ($node in $ast.FindAll(
          { param($candidate) $candidate -is [System.Management.Automation.Language.StatementAst] },
          $true)) {
          $bodyStart = if (
            $node -is [System.Management.Automation.Language.ForEachStatementAst]) {
            $node.Body.Extent.StartOffset
          } else {
            -1
          }
          Add-Node "D" $node $node.GetType().Name `
            (Get-StatementBlockStart $node) $bodyStart
        }
        foreach ($node in $ast.FindAll(
          { param($candidate) $candidate -is [System.Management.Automation.Language.CommandAst] },
          $true)) {
          Add-Node "C" $node ([string]$node.GetCommandName()) `
            (Get-StatementBlockStart $node)
        }
        foreach ($node in $ast.FindAll(
          { param($candidate) $candidate -is [System.Management.Automation.Language.AssignmentStatementAst] },
          $true)) {
          Add-Node "A" $node $node.Left.Extent.Text `
            (Get-StatementBlockStart $node)
        }
        foreach ($node in $ast.FindAll(
          { param($candidate) $candidate -is [System.Management.Automation.Language.IfStatementAst] },
          $true)) {
          Add-Node "I" $node $node.Clauses[0].Item1.Extent.Text `
            (Get-StatementBlockStart $node) `
            $node.Clauses[0].Item2.Extent.StartOffset
          foreach ($clause in $node.Clauses) {
            Add-Node "K" $clause.Item2 $clause.Item1.Extent.Text `
              $node.Extent.StartOffset $clause.Item2.Extent.StartOffset
          }
          if ($null -ne $node.ElseClause) {
            Add-Node "E" $node.ElseClause "else" `
              $node.Extent.StartOffset $node.ElseClause.Extent.StartOffset
          }
        }
        foreach ($node in $ast.FindAll(
          { param($candidate) $candidate -is [System.Management.Automation.Language.ThrowStatementAst] },
          $true)) {
          Add-Node "T" $node "throw" (Get-StatementBlockStart $node)
        }
        foreach ($node in $ast.FindAll(
          { param($candidate) $candidate -is [System.Management.Automation.Language.HashtableAst] },
          $true)) {
          $keys = @()
          foreach ($pair in $node.KeyValuePairs) {
            $keys += $pair.Item1.Extent.Text
          }
          Add-Node "H" $node ([string]::Join(",", $keys)) `
            (Get-StatementBlockStart $node)
        }
        foreach ($node in $ast.FindAll(
          { param($candidate) $candidate -is [System.Management.Automation.Language.BinaryExpressionAst] },
          $true)) {
          Add-Node "B" $node ([string]$node.Operator) `
            (Get-StatementBlockStart $node)
        }
        [Console]::WriteLine(
          [System.Text.Json.JsonSerializer]::Serialize(
            [object]$nodes,
            $nodes.GetType(),
            [System.Text.Json.JsonSerializerOptions]::new()))
        """;

    private static readonly Regex PowerShellFence = new(
        @"^```powershell\r?\n(?<code>.*?)^```[ \t]*$",
        RegexOptions.CultureInvariant
        | RegexOptions.Multiline
        | RegexOptions.Singleline);

    private static readonly Regex MarkdownHeading = new(
        @"^#{2,6} .+$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    public static IReadOnlyList<PowerShellBlock> Blocks(string document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return PowerShellFence.Matches(document)
            .Cast<Match>()
            .Select(match =>
            {
                Group code = match.Groups["code"];
                Match heading = MarkdownHeading.Matches(document[..match.Index])[^1];
                return new PowerShellBlock(
                    code.Index,
                    code.Index + code.Length,
                    heading.Value.TrimEnd('\r'),
                    code.Value);
            })
            .ToArray();
    }

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
                    headingPrefix,
                    code.Value);
            })
            .ToArray();
    }

    public static PowerShellAst Analyze(PowerShellBlock block) =>
        Analyze(
            block,
            new PowerShellAnalysisOptions(
                AnalyzerScript,
                TimeSpan.FromSeconds(15)));

    public static PowerShellAst Analyze(
        PowerShellBlock block,
        PowerShellAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AnalyzerScript);
        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Timeout,
                "PowerShell AST timeout must be positive.");
        }

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
            process.StartInfo.ArgumentList.Add(options.AnalyzerScript);
            process.StartInfo.Environment["EMKE_AST_SOURCE_PATH"] = sourcePath;
            if (options.Environment is not null)
            {
                foreach ((string name, string value) in options.Environment)
                {
                    process.StartInfo.Environment[name] = value;
                }
            }

            Assert.True(process.Start(), "Could not start PowerShell AST parser.");
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(ToTimeoutMilliseconds(options.Timeout)))
            {
                string cleanup = TerminateAndDrain(
                    process,
                    standardOutput,
                    standardError);
                string timeoutError = CompletedText(standardError);
                Assert.Fail(
                    $"PowerShell AST parser timed out in section "
                    + $"'{block.SectionHeading}' after "
                    + $"{options.Timeout.TotalSeconds:0.###} seconds. "
                    + $"stderr: {DisplayText(timeoutError)}. "
                    + $"Cleanup: {cleanup}");
            }

            string capture = WaitForCapture(standardOutput, standardError);
            string output = standardOutput.GetAwaiter().GetResult();
            string error = standardError.GetAwaiter().GetResult();
            Assert.True(
                process.ExitCode == 0,
                $"PowerShell AST parser failed in section "
                + $"'{block.SectionHeading}' with exit {process.ExitCode}: "
                + $"{DisplayText(error)}. Capture: {capture}");

            using JsonDocument document = JsonDocument.Parse(output);
            PowerShellAstNode[] nodes = document.RootElement
                .EnumerateArray()
                .Select(ParseNode)
                .ToArray();
            return new PowerShellAst(block, nodes);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    private static string TerminateAndDrain(
        Process process,
        Task<string> standardOutput,
        Task<string> standardError)
    {
        string killResult;
        try
        {
            if (process.HasExited)
            {
                killResult = "process exited before termination";
            }
            else
            {
                process.Kill(entireProcessTree: true);
                killResult = "process-tree termination requested";
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
            killResult = $"process-tree termination failed: {exception.Message}";
        }

        bool exited = process.HasExited
            || process.WaitForExit(ToTimeoutMilliseconds(CleanupTimeout));
        string capture = WaitForCapture(standardOutput, standardError);
        return $"{killResult}; exited={exited}; capture={capture}";
    }

    private static string WaitForCapture(
        Task<string> standardOutput,
        Task<string> standardError)
    {
        Task capture = Task.WhenAll(standardOutput, standardError);
        try
        {
            if (capture.Wait(CleanupTimeout))
            {
                return "complete";
            }

            ObserveLater(capture);
            return "timed out";
        }
        catch (AggregateException exception)
        {
            return $"failed: {exception.GetBaseException().Message}";
        }
    }

    private static string CompletedText(Task<string> task) =>
        task.IsCompletedSuccessfully ? task.Result.Trim() : "<capture unavailable>";

    private static string DisplayText(string value) =>
        string.IsNullOrWhiteSpace(value) ? "<empty>" : value.Trim();

    private static void ObserveLater(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted
                | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static int ToTimeoutMilliseconds(TimeSpan timeout) =>
        (int)Math.Clamp(
            Math.Ceiling(timeout.TotalMilliseconds),
            1,
            int.MaxValue);

    private static PowerShellAstNode ParseNode(JsonElement element)
    {
        string kind = element.GetProperty("kind").GetString()!;
        Assert.Single(kind);
        return new(
            kind[0],
            element.GetProperty("start").GetInt32(),
            element.GetProperty("end").GetInt32(),
            element.GetProperty("label").GetString()!,
            element.GetProperty("text").GetString()!,
            element.GetProperty("parentStart").GetInt32(),
            element.GetProperty("relatedStart").GetInt32());
    }

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
