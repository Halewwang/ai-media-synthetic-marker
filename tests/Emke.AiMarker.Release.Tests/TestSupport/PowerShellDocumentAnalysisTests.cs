using System.Diagnostics;

namespace Emke.AiMarker.Release.Tests.TestSupport;

public sealed class PowerShellDocumentAnalysisTests
{
    [Fact]
    public void Timed_out_analyzer_kills_process_tree_and_reports_context()
    {
        string pidPath = Path.Combine(
            Path.GetTempPath(),
            $"emke-ai-marker-ast-pids-{Guid.NewGuid():N}.txt");
        int[] processIds = [];
        try
        {
            var block = new PowerShellBlock(
                0,
                0,
                "### Timeout process-tree fixture",
                "$null");
            const string hangingAnalyzer =
                """
                $startInfo = [Diagnostics.ProcessStartInfo]::new()
                $startInfo.FileName = [Environment]::ProcessPath
                $startInfo.ArgumentList.Add("-NoLogo")
                $startInfo.ArgumentList.Add("-NoProfile")
                $startInfo.ArgumentList.Add("-NonInteractive")
                $startInfo.ArgumentList.Add("-Command")
                $startInfo.ArgumentList.Add("[Threading.Thread]::Sleep(30000)")
                $child = [Diagnostics.Process]::Start($startInfo)
                [IO.File]::WriteAllLines(
                  $env:EMKE_AST_PID_PATH,
                  [string[]]@([string]$PID, [string]$child.Id))
                [Console]::Error.WriteLine("timeout stderr marker")
                while ($true) {
                  [Threading.Thread]::Sleep(100)
                }
                """;
            var options = new PowerShellAnalysisOptions(
                hangingAnalyzer,
                TimeSpan.FromSeconds(2),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["EMKE_AST_PID_PATH"] = pidPath,
                });

            Exception exception = Assert.ThrowsAny<Exception>(
                () => PowerShellDocumentAnalysis.Analyze(block, options));

            Assert.Contains(block.SectionHeading, exception.Message, StringComparison.Ordinal);
            Assert.Contains("timeout stderr marker", exception.Message, StringComparison.Ordinal);
            Assert.True(
                File.Exists(pidPath),
                $"The timeout fixture did not record its process IDs: {exception.Message}");
            processIds = File.ReadAllLines(pidPath)
                .Select(line => int.Parse(
                    line,
                    System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            Assert.Equal(2, processIds.Length);
            Assert.All(
                processIds,
                processId => Assert.True(
                    WaitForProcessExit(processId, TimeSpan.FromSeconds(2)),
                    $"PowerShell AST parser process {processId} survived timeout cleanup."));
        }
        finally
        {
            foreach (int processId in processIds)
            {
                TerminateForTestCleanup(processId);
            }

            File.Delete(pidPath);
        }
    }

    private static bool WaitForProcessExit(int processId, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            Thread.Sleep(25);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private static void TerminateForTestCleanup(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _ = process.WaitForExit(2_000);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException)
        {
        }
    }
}
