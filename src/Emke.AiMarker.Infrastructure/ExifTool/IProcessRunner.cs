namespace Emke.AiMarker.Infrastructure.ExifTool;

public interface IProcessRunner
{
    Task<ProcessExecutionResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> argumentFileLines,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed record ProcessExecutionResult(
    int ExitCode,
    byte[] Stdout,
    byte[] Stderr);
