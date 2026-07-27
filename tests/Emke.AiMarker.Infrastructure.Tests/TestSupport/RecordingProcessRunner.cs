using System.Text;
using Emke.AiMarker.Infrastructure.ExifTool;

namespace Emke.AiMarker.Infrastructure.Tests.TestSupport;

internal sealed class RecordingProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessExecutionResult> _results = new();

    public string? LastExecutable { get; private set; }

    public IReadOnlyList<string> LastArgumentFileLines { get; private set; } = [];

    public TimeSpan LastTimeout { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public int CallCount { get; private set; }

    public static RecordingProcessRunner WithStdout(string stdout) =>
        WithResult(new ProcessExecutionResult(0, Encoding.UTF8.GetBytes(stdout), []));

    public static RecordingProcessRunner WithStdout(byte[] stdout) =>
        WithResult(new ProcessExecutionResult(0, stdout, []));

    public static RecordingProcessRunner WithResult(ProcessExecutionResult result)
    {
        var runner = new RecordingProcessRunner();
        runner._results.Enqueue(result);
        return runner;
    }

    public Task<ProcessExecutionResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> argumentFileLines,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        LastExecutable = executable;
        LastArgumentFileLines = argumentFileLines.ToArray();
        LastTimeout = timeout;
        LastCancellationToken = cancellationToken;
        CallCount++;

        return Task.FromResult(
            _results.Count == 0
                ? new ProcessExecutionResult(0, [], [])
                : _results.Dequeue());
    }
}
