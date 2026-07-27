using System.Text;
using Emke.AiMarker.Core.Abstractions;
using Emke.AiMarker.Core.Contracts;
using Emke.AiMarker.Core.Models;

namespace Emke.AiMarker.Core.Tests.TestSupport;

public enum ExifFailurePoint
{
    None,
    InitialRead,
    Write,
    ReadAfterWrite,
    RawXmp,
    Version,
}

internal sealed class FakeFileTransaction(
    byte[] sourceBytes,
    List<string>? operationLog = null) : IFileTransaction
{
    public byte[] SourceBytes { get; } = sourceBytes.ToArray();

    public bool PrepareCalled { get; private set; }

    public bool CommitCalled { get; private set; }

    public bool RollbackCalled { get; private set; }

    public bool SealVerifiedCalled { get; private set; }

    public bool ThrowOnCommit { get; init; }

    public List<string> Calls { get; } = [];

    public List<CancellationToken> CancellationTokens { get; } = [];

    public Task<PreparedMedia> PrepareAsync(
        OutputPlanItem plan,
        RunMode mode,
        CancellationToken cancellationToken)
    {
        PrepareCalled = true;
        Calls.Add("prepare");
        operationLog?.Add("prepare");
        CancellationTokens.Add(cancellationToken);
        string workingPath = mode == RunMode.MarkCopies
            ? plan.TempPath
            : plan.SourcePath;
        return Task.FromResult(new PreparedMedia(
            plan.SourcePath,
            workingPath,
            plan.FinalPath));
    }

    public Task CommitAsync(PreparedMedia media, CancellationToken cancellationToken)
    {
        CommitCalled = true;
        Calls.Add("commit");
        operationLog?.Add("commit");
        CancellationTokens.Add(cancellationToken);
        if (ThrowOnCommit)
        {
            throw new IOException("commit failed");
        }

        return Task.CompletedTask;
    }

    public Task SealVerifiedAsync(
        PreparedMedia media,
        CancellationToken cancellationToken)
    {
        SealVerifiedCalled = true;
        Calls.Add("verified");
        operationLog?.Add("verified");
        CancellationTokens.Add(cancellationToken);
        return Task.CompletedTask;
    }

    public Task RollbackAsync(PreparedMedia media)
    {
        RollbackCalled = true;
        Calls.Add("rollback");
        operationLog?.Add("rollback");
        return Task.CompletedTask;
    }
}

internal sealed class FakeExifToolClient(
    IReadOnlyList<string>? beforeSubjects = null,
    IReadOnlyList<string>? afterSubjects = null,
    ReadOnlyMemory<byte> rawXmp = default,
    ExifFailurePoint failurePoint = ExifFailurePoint.None,
    List<string>? operationLog = null,
    bool mutateSubjectsInPlace = false) : IExifToolClient
{
    private bool _writeAttempted;
    private readonly List<string>? _sharedSubjects =
        mutateSubjectsInPlace ? (beforeSubjects ?? []).ToList() : null;

    public IReadOnlyList<string> BeforeSubjects { get; } = beforeSubjects ?? [];

    public IReadOnlyList<string> AfterSubjects { get; } = afterSubjects ?? [];

    public ReadOnlyMemory<byte> RawXmp { get; } = rawXmp;

    public int WriteCount { get; private set; }

    public int IdentityPreservingWriteCount { get; private set; }

    public List<string> Calls { get; } = [];

    public List<CancellationToken> CancellationTokens { get; } = [];

    public Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        Record("version", cancellationToken);
        if (failurePoint == ExifFailurePoint.Version)
        {
            throw new IOException("version failed");
        }

        return Task.FromResult("13.59");
    }

    public Task<IReadOnlyList<string>> ReadSubjectsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        Record($"subjects:{path}", cancellationToken);
        if (!_writeAttempted && failurePoint == ExifFailurePoint.InitialRead)
        {
            throw new IOException("initial read failed");
        }

        if (_writeAttempted && failurePoint == ExifFailurePoint.ReadAfterWrite)
        {
            throw new IOException("read after write failed");
        }

        return Task.FromResult<IReadOnlyList<string>>(
            _sharedSubjects
            ?? (_writeAttempted ? AfterSubjects : BeforeSubjects));
    }

    public Task WriteMarkerAsync(string path, CancellationToken cancellationToken)
    {
        return WriteMarkerCore(path, cancellationToken, $"write:{path}");
    }

    public Task WriteMarkerPreservingIdentityAsync(
        string path,
        CancellationToken cancellationToken)
    {
        IdentityPreservingWriteCount++;
        return WriteMarkerCore(
            path,
            cancellationToken,
            $"write-preserving:{path}");
    }

    private Task WriteMarkerCore(
        string path,
        CancellationToken cancellationToken,
        string call)
    {
        _writeAttempted = true;
        WriteCount++;
        Record(call, cancellationToken);
        if (_sharedSubjects is not null)
        {
            _sharedSubjects.Clear();
            _sharedSubjects.AddRange(AfterSubjects);
        }

        if (failurePoint == ExifFailurePoint.Write)
        {
            throw new IOException("write failed");
        }

        return Task.CompletedTask;
    }

    public Task<ReadOnlyMemory<byte>> ReadRawXmpAsync(
        string path,
        CancellationToken cancellationToken)
    {
        Record($"xmp:{path}", cancellationToken);
        if (failurePoint == ExifFailurePoint.RawXmp)
        {
            throw new IOException("raw XMP failed");
        }

        return Task.FromResult(RawXmp);
    }

    public Task<string> ReadImageDataHashAsync(
        string path,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    private void Record(string call, CancellationToken cancellationToken)
    {
        Calls.Add(call);
        operationLog?.Add(call);
        CancellationTokens.Add(cancellationToken);
    }
}

internal sealed class FakeOriginalWriteSafety(List<string>? operationLog = null)
    : IOriginalWriteSafety
{
    public bool ValidateCalled { get; private set; }

    public bool ThrowOnValidate { get; init; }

    public List<string> Calls { get; } = [];

    public void Validate(OutputPlanItem plan)
    {
        ValidateCalled = true;
        Calls.Add("safety");
        operationLog?.Add("safety");
        if (ThrowOnValidate)
        {
            throw new IOException("unsafe original");
        }
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    public static readonly DateTimeOffset FixedUtcNow =
        new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => FixedUtcNow;
}

internal static class TestPlans
{
    public static OutputPlanItem Copy(string relativePath)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "emke-ai-marker-tests",
            Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source", relativePath);
        string final = Path.Combine(root, "output", relativePath);
        string destination = Path.GetDirectoryName(final)!;
        string temp = Path.Combine(
            destination,
            $".emke-ai-marker-{Guid.NewGuid():N}.tmp{Path.GetExtension(final)}");
        return new(source, relativePath, final, temp, 3);
    }
}

internal static class TestXmp
{
    public static ReadOnlyMemory<byte> ValidBag(params string[] subjects) =>
        Container("Bag", subjects);

    public static ReadOnlyMemory<byte> RdfSeq(params string[] subjects) =>
        Container("Seq", subjects);

    private static ReadOnlyMemory<byte> Container(
        string container,
        IEnumerable<string> subjects)
    {
        string items = string.Concat(
            subjects.Select(subject => $"<rdf:li>{subject}</rdf:li>"));
        string xml =
            $"""
             <x:xmpmeta xmlns:x="adobe:ns:meta/">
               <rdf:RDF xmlns:rdf="{MarkerContract.RdfNamespace}">
                 <rdf:Description xmlns:dc="{MarkerContract.DcNamespace}">
                   <dc:subject><rdf:{container}>{items}</rdf:{container}></dc:subject>
                 </rdf:Description>
               </rdf:RDF>
             </x:xmpmeta>
             """;
        return Encoding.UTF8.GetBytes(xml);
    }
}
