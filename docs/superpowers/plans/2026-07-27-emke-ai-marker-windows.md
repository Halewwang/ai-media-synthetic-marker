# EMKE AI Marker Windows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Python/Tkinter runtime with a branded, offline, native Windows x64 EMKE AI Marker that safely creates and strictly verifies marked JPG, JPEG, PNG, and MP4 copies.

**Architecture:** Build a .NET 10 solution with a platform-neutral Core, Windows/ExifTool Infrastructure, a WPF MVVM App, xUnit test projects, and a .NET release tool. Preserve the existing Python implementation under `legacy/python/` for one major release and port its observable behavior into C# contract tests before removing it from the production package.

**Tech Stack:** C# 14, .NET 10, WPF, MVVM without a third-party MVVM framework, `System.Xml.Linq`, `System.Text.Json`, xUnit v3 3.2.2, Microsoft.NET.Test.Sdk 18.8.1, xunit.runner.visualstudio 3.1.5, ExifTool 13.59, PowerShell 7, GitHub Actions Windows 2022.

## Global Constraints

- Product name is `EMKE AI Marker`; the first EMKE release is `2.0.0`.
- Target is Windows x64; publish a `win-x64` self-contained portable ZIP.
- Use C#, .NET 10, WPF, and MVVM. Python must not be part of the production runtime.
- Support `.jpg`, `.jpeg`, `.png`, and `.mp4`, case-insensitively.
- The only marker is the read-only exact string `contains-synthetic-performer`.
- Compliance requires exact `XMP-dc:Subject` membership and the formal `dc:subject/rdf:Bag/rdf:li` structure.
- Default mode creates verified copies and leaves source bytes unchanged.
- Direct source modification is an advanced, per-run choice with a second confirmation.
- Preserve existing Subject values and never append the target marker twice.
- Reject symbolic links, junctions, and other reparse points.
- Process files in stable order; one file failure must not abort the batch.
- Safe stop finishes the current file, starts no new file, and still writes a CSV record.
- Media processing is offline: no sign-in, uploads, telemetry, Amazon API, or network calls.
- First UI language is Simplified Chinese; resource strings must be centralized.
- Use the supplied 202 × 202 SVG with SHA-256 `c98e5a189b344bd5adb6b49848acdf307ce0f12c4e60a428c4c421fe258142a6`; do not redraw it.
- Brand/action color is `#36A39E`; safety background is `#E3F3F1`; app background is `#F7F9F9`; primary text is `#172022`.
- The package is unsigned. Documentation must state that SmartScreen may warn.
- Keep unit, CI/package, and Windows 11 real-machine evidence separate.

---

## Planned File Structure

```text
Emke.AiMarker.sln
global.json
Directory.Build.props
Directory.Packages.props
assets/
  branding/
    emke-app-logo.svg
    brand-assets.json
src/
  Emke.AiMarker.Core/
    Abstractions/
      IBatchProcessor.cs
      IExifToolClient.cs
      IFileProcessor.cs
      IFileTransaction.cs
      IOriginalWriteSafety.cs
      IRunLogWriter.cs
      IStorageProbe.cs
    Contracts/
      MarkerContract.cs
    Discovery/
      IPathAccess.cs
      InputScanner.cs
    Models/
      DiscoveredMedia.cs
      OutputPlanItem.cs
      ProcessResult.cs
      RunModels.cs
      ScanModels.cs
      VerificationEvidence.cs
    Planning/
      OutputPlanner.cs
      StoragePreflight.cs
    Processing/
      BatchProcessor.cs
      MediaProcessor.cs
      StopController.cs
    Verification/
      XmpComplianceVerifier.cs
  Emke.AiMarker.Infrastructure/
    ExifTool/
      ExifToolClient.cs
      ExifToolExceptions.cs
      ExifToolManifestValidator.cs
      ProcessRunner.cs
    Files/
      OwnedTempFile.cs
      PhysicalCopyTransaction.cs
      WindowsStorageProbe.cs
      WindowsFileSafety.cs
    Logging/
      CsvRunLogWriter.cs
    Windows/
      SingleInstanceGuard.cs
  Emke.AiMarker.App/
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    Assets/
      emke-app-logo-32.png
      emke-app-logo-256.png
      emke-ai-marker.ico
    Resources/
      Controls.xaml
      Strings.zh-CN.xaml
      Theme.xaml
    Services/
      FileSelectionService.cs
      IFileSelectionService.cs
      IShellService.cs
      IUserPromptService.cs
      ShellService.cs
      UserPromptService.cs
    ViewModels/
      AsyncRelayCommand.cs
      MainWindowViewModel.cs
      ObservableObject.cs
      SettingsViewModel.cs
    Views/
      ConfirmationDialog.xaml
      ConfirmationDialog.xaml.cs
      SettingsDialog.xaml
      SettingsDialog.xaml.cs
tests/
  Emke.AiMarker.Core.Tests/
  Emke.AiMarker.Infrastructure.Tests/
  Emke.AiMarker.App.Tests/
  Emke.AiMarker.Integration.Tests/
  Emke.AiMarker.Release.Tests/
  fixtures/
    controlled/
      fixture.jpg
      fixture.jpeg
      fixture.png
      fixture.mp4
      fixture-manifest.json
tools/
  Emke.AiMarker.Release/
    Commands/
      FetchExifToolCommand.cs
      PackageCommand.cs
    Packaging/
      DeterministicZipWriter.cs
      ReleaseStageValidator.cs
    Program.cs
  generate-controlled-fixtures.ps1
scripts/
  build-release.ps1
  fetch-exiftool.ps1
packaging/
  exiftool.lock.json
  release-manifest.json
  licenses/
    dotnet/
      LICENSE.txt
      ThirdPartyNotices.txt
legacy/
  python/
docs/
  validation/
    windows-11-x64-smoke.md
    windows-11-x64-smoke-result.md
```

Files are split by responsibility. Core owns behavior and contracts, Infrastructure owns operating-system and ExifTool effects, App owns presentation, and Release owns deterministic acquisition and packaging. No WPF type crosses into Core.

### Task 1: Establish the .NET 10 solution and immutable product contract

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `Emke.AiMarker.sln`
- Create: `src/Emke.AiMarker.Core/Emke.AiMarker.Core.csproj`
- Create: `src/Emke.AiMarker.Core/Contracts/MarkerContract.cs`
- Create: `tests/Emke.AiMarker.Core.Tests/Emke.AiMarker.Core.Tests.csproj`
- Create: `tests/Emke.AiMarker.Core.Tests/Contracts/MarkerContractTests.cs`
- Create: generated `packages.lock.json` files beside each project

**Interfaces:**
- Consumes: none.
- Produces: `MarkerContract.Marker`, `MarkerContract.VerificationField`, `MarkerContract.VerificationStructure`, `MarkerContract.DcNamespace`, `MarkerContract.RdfNamespace`, and solution-wide version `2.0.0`.

- [ ] **Step 1: Create the solution and central configuration**

Run on a machine with .NET 10:

```powershell
dotnet new sln -n Emke.AiMarker
dotnet new classlib -n Emke.AiMarker.Core -o src/Emke.AiMarker.Core -f net10.0
dotnet new xunit -n Emke.AiMarker.Core.Tests -o tests/Emke.AiMarker.Core.Tests -f net10.0
dotnet sln Emke.AiMarker.sln add src/Emke.AiMarker.Core/Emke.AiMarker.Core.csproj
dotnet sln Emke.AiMarker.sln add tests/Emke.AiMarker.Core.Tests/Emke.AiMarker.Core.Tests.csproj
dotnet add tests/Emke.AiMarker.Core.Tests/Emke.AiMarker.Core.Tests.csproj reference src/Emke.AiMarker.Core/Emke.AiMarker.Core.csproj
```

Write `global.json`:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestMinor",
    "allowPrerelease": false
  }
}
```

Write `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Version>2.0.0</Version>
    <AssemblyVersion>2.0.0.0</AssemblyVersion>
    <FileVersion>2.0.0.0</FileVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
</Project>
```

Write `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageVersion Include="xunit.v3" Version="3.2.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
</Project>
```

Replace the generated test package references with:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.NET.Test.Sdk" />
  <PackageReference Include="xunit.v3" />
  <PackageReference Include="xunit.runner.visualstudio">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

- [ ] **Step 2: Write the failing product contract test**

```csharp
using Emke.AiMarker.Core.Contracts;

namespace Emke.AiMarker.Core.Tests.Contracts;

public sealed class MarkerContractTests
{
    [Fact]
    public void Contract_values_are_exact_and_case_sensitive()
    {
        Assert.Equal("contains-synthetic-performer", MarkerContract.Marker);
        Assert.Equal("XMP-dc:Subject", MarkerContract.VerificationField);
        Assert.Equal("rdf:Bag/rdf:li", MarkerContract.VerificationStructure);
        Assert.Equal("http://purl.org/dc/elements/1.1/", MarkerContract.DcNamespace);
        Assert.Equal(
            "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
            MarkerContract.RdfNamespace);
    }

    [Fact]
    public void Core_assembly_version_is_2_0_0_0()
    {
        Assert.Equal(
            new Version(2, 0, 0, 0),
            typeof(MarkerContract).Assembly.GetName().Version);
    }
}
```

- [ ] **Step 3: Run the test and verify that it fails**

Run:

```powershell
dotnet test tests/Emke.AiMarker.Core.Tests/Emke.AiMarker.Core.Tests.csproj
```

Expected: build fails because `MarkerContract` does not exist.

- [ ] **Step 4: Implement the contract**

```csharp
namespace Emke.AiMarker.Core.Contracts;

public static class MarkerContract
{
    public const string Marker = "contains-synthetic-performer";
    public const string VerificationField = "XMP-dc:Subject";
    public const string VerificationStructure = "rdf:Bag/rdf:li";
    public const string DcNamespace = "http://purl.org/dc/elements/1.1/";
    public const string RdfNamespace =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".mp4",
        };
}
```

Delete the generated `Class1.cs` and `UnitTest1.cs`.

- [ ] **Step 5: Restore with lock files and run the solution**

Run:

```powershell
dotnet restore Emke.AiMarker.sln --use-lock-file
dotnet test Emke.AiMarker.sln --no-restore
```

Expected: restore succeeds, lock files are created, and all tests pass.

- [ ] **Step 6: Commit**

```bash
git add global.json Directory.Build.props Directory.Packages.props Emke.AiMarker.sln src/Emke.AiMarker.Core tests/Emke.AiMarker.Core.Tests
git commit -m "build: establish .NET 10 marker solution"
```

### Task 2: Port result models and strict XMP verification

**Files:**
- Create: `src/Emke.AiMarker.Core/Models/VerificationEvidence.cs`
- Create: `src/Emke.AiMarker.Core/Models/ProcessResult.cs`
- Create: `src/Emke.AiMarker.Core/Models/RunModels.cs`
- Create: `src/Emke.AiMarker.Core/Verification/XmpComplianceVerifier.cs`
- Create: `tests/Emke.AiMarker.Core.Tests/Verification/XmpComplianceVerifierTests.cs`

**Interfaces:**
- Consumes: `MarkerContract` constants from Task 1.
- Produces: `VerificationResult`, `VerificationEvidence`, `ProcessStatus`, `ProcessResult`, `RunMode`, and `XmpComplianceVerifier.Verify(...)`.

- [ ] **Step 1: Write failing strict verification tests**

```csharp
using System.Text;
using Emke.AiMarker.Core.Contracts;
using Emke.AiMarker.Core.Models;
using Emke.AiMarker.Core.Verification;

namespace Emke.AiMarker.Core.Tests.Verification;

public sealed class XmpComplianceVerifierTests
{
    private static byte[] MakeXmp(
        IEnumerable<string> values,
        string container = "Bag",
        string? dcNamespace = null)
    {
        string items = string.Concat(
            values.Select(value =>
                $"<rdf:li>{System.Security.SecurityElement.Escape(value)}</rdf:li>"));
        string xml =
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="{MarkerContract.RdfNamespace}">
                <rdf:Description xmlns:dc="{dcNamespace ?? MarkerContract.DcNamespace}">
                  <dc:subject><rdf:{container}>{items}</rdf:{container}></dc:subject>
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            """;
        return Encoding.UTF8.GetBytes(xml);
    }

    [Fact]
    public void Exact_marker_in_formal_bag_li_passes()
    {
        var evidence = XmpComplianceVerifier.Verify(
            ["existing", MarkerContract.Marker],
            MakeXmp(["existing", MarkerContract.Marker]),
            "13.59",
            new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.FromHours(8)));

        Assert.Equal(VerificationResult.Passed, evidence.Result);
        Assert.Equal("已确认 rdf:Bag/rdf:li", evidence.XmpStructure);
    }

    [Theory]
    [InlineData("CONTAINS-SYNTHETIC-PERFORMER")]
    [InlineData(" contains-synthetic-performer")]
    [InlineData("contains-synthetic-performer ")]
    [InlineData("contains-synthetic-performer-extra")]
    public void Near_matches_are_unmarked(string value)
    {
        var evidence = XmpComplianceVerifier.Verify(
            [value],
            MakeXmp([value]),
            "13.59",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(VerificationResult.Unmarked, evidence.Result);
    }

    [Fact]
    public void Marker_in_rdf_seq_fails_structure_validation()
    {
        var evidence = XmpComplianceVerifier.Verify(
            [MarkerContract.Marker],
            MakeXmp([MarkerContract.Marker], container: "Seq"),
            "13.59",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(VerificationResult.Failed, evidence.Result);
        Assert.Contains("rdf:Bag/rdf:li", evidence.Error);
    }

    [Fact]
    public void Wrong_dc_namespace_fails_structure_validation()
    {
        var evidence = XmpComplianceVerifier.Verify(
            [MarkerContract.Marker],
            MakeXmp(
                [MarkerContract.Marker],
                dcNamespace: "https://example.invalid/dc"),
            "13.59",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(VerificationResult.Failed, evidence.Result);
    }
}
```

- [ ] **Step 2: Run the tests and verify that they fail**

Run:

```powershell
dotnet test tests/Emke.AiMarker.Core.Tests/Emke.AiMarker.Core.Tests.csproj --filter XmpComplianceVerifierTests
```

Expected: build fails because the models and verifier do not exist.

- [ ] **Step 3: Implement the exact models**

```csharp
namespace Emke.AiMarker.Core.Models;

public enum VerificationResult
{
    Passed,
    Unmarked,
    Failed,
    NotRun,
}

public sealed record VerificationEvidence(
    VerificationResult Result,
    string ActualValue,
    string XmpStructure,
    DateTimeOffset VerifiedAt,
    string ExifToolVersion,
    string Error = "");

public enum ProcessStatus
{
    Added,
    AlreadyCompliant,
    OutputAlreadyCompliant,
    Unmarked,
    Failed,
    Skipped,
    StoppedBeforeProcessing,
}

public enum RunMode
{
    MarkCopies,
    MarkOriginals,
    VerifyOnly,
}

public sealed record ProcessResult(
    string RelativePath,
    string MediaFormat,
    ProcessStatus Status,
    RunMode Mode,
    VerificationEvidence Evidence,
    string OutputPath = "",
    string Error = "");
```

- [ ] **Step 4: Implement strict XML verification**

Implement `XmpComplianceVerifier.Verify` with this public signature and exact decision order:

```csharp
public static VerificationEvidence Verify(
    IReadOnlyList<string> subjects,
    ReadOnlyMemory<byte> rawXmp,
    string exifToolVersion,
    DateTimeOffset verifiedAt)
```

Use:

```csharp
if (!subjects.Contains(MarkerContract.Marker, StringComparer.Ordinal))
{
    return new(
        VerificationResult.Unmarked,
        JsonSerializer.Serialize(subjects),
        "未找到目标 rdf:li",
        verifiedAt,
        exifToolVersion);
}

if (rawXmp.IsEmpty)
{
    return new(
        VerificationResult.Failed,
        JsonSerializer.Serialize(subjects),
        "未读取到原始 XMP",
        verifiedAt,
        exifToolVersion,
        "字段读取到了目标值，但没有读取到可验证的原始 XMP 数据包。");
}

using var stream = new MemoryStream(rawXmp.ToArray(), writable: false);
XDocument document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
XNamespace dc = MarkerContract.DcNamespace;
XNamespace rdf = MarkerContract.RdfNamespace;
bool found = document
    .Descendants(dc + "subject")
    .Elements(rdf + "Bag")
    .Elements(rdf + "li")
    .Any(item => string.Equals(
        item.Value,
        MarkerContract.Marker,
        StringComparison.Ordinal));
```

Return `Failed` for parse errors or a missing formal Bag/li, and `Passed` only when `found` is true. Format `ActualValue` with `JsonSerializer.Serialize(subjects)`.

- [ ] **Step 5: Run verification tests**

Run:

```powershell
dotnet test tests/Emke.AiMarker.Core.Tests/Emke.AiMarker.Core.Tests.csproj --filter XmpComplianceVerifierTests
```

Expected: all strict verification tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Emke.AiMarker.Core/Models src/Emke.AiMarker.Core/Verification tests/Emke.AiMarker.Core.Tests/Verification
git commit -m "feat: port strict XMP compliance verification"
```

### Task 3: Discover safe media inputs and plan collision-free outputs

**Files:**
- Create: `src/Emke.AiMarker.Core/Models/DiscoveredMedia.cs`
- Create: `src/Emke.AiMarker.Core/Models/ScanModels.cs`
- Create: `src/Emke.AiMarker.Core/Models/OutputPlanItem.cs`
- Create: `src/Emke.AiMarker.Core/Discovery/IPathAccess.cs`
- Create: `src/Emke.AiMarker.Core/Discovery/InputScanner.cs`
- Create: `src/Emke.AiMarker.Infrastructure/Emke.AiMarker.Infrastructure.csproj`
- Create: `src/Emke.AiMarker.Infrastructure/Files/PhysicalPathAccess.cs`
- Create: `src/Emke.AiMarker.Infrastructure/Files/WindowsStorageProbe.cs`
- Create: `src/Emke.AiMarker.Core/Planning/OutputPlanner.cs`
- Create: `src/Emke.AiMarker.Core/Planning/StoragePreflight.cs`
- Create: `src/Emke.AiMarker.Core/Abstractions/IStorageProbe.cs`
- Test: `tests/Emke.AiMarker.Core.Tests/Discovery/InputScannerTests.cs`
- Test: `tests/Emke.AiMarker.Core.Tests/Planning/OutputPlannerTests.cs`
- Create: `tests/Emke.AiMarker.Core.Tests/TestSupport/DiscoveryFakes.cs`

**Interfaces:**
- Consumes: `MarkerContract.SupportedExtensions`.
- Produces: `IPathAccess`, `InputScanner.Scan`, `DiscoveredMedia`, `ScanIssue`, `ScanResult`, `OutputPlanner.Plan`, `OutputPlanItem`, `IStorageProbe`, and `StoragePreflight.Check`.

- [ ] **Step 1: Write failing scanner tests**

Use a fake path tree so reparse behavior does not require elevated permissions:

```csharp
[Fact]
public void Scan_is_recursive_deduplicated_stable_and_case_insensitive()
{
    var paths = new FakePathAccess()
        .Directory(@"D:\商品")
        .File(@"D:\商品\B.MP4", 20)
        .File(@"D:\商品\a.JPG", 10)
        .Directory(@"D:\商品\子目录")
        .File(@"D:\商品\子目录\透明.PNG", 30)
        .File(@"D:\商品\忽略.txt", 2);

    ScanResult result = new InputScanner(paths).Scan(
        [@"D:\商品", @"D:\商品\a.JPG"]);

    Assert.Equal(
        [@"a.JPG", @"B.MP4", @"子目录\透明.PNG"],
        result.Media.Select(item => item.RelativePath));
    Assert.Empty(result.Issues);
}

[Fact]
public void Scan_rejects_reparse_directories()
{
    var paths = new FakePathAccess()
        .Directory(@"D:\商品")
        .ReparseDirectory(@"D:\商品\联接")
        .File(@"D:\商品\联接\private.jpg", 10);

    ScanResult result = new InputScanner(paths).Scan([@"D:\商品"]);

    Assert.Empty(result.Media);
    Assert.Single(result.Issues);
    Assert.Contains("重解析点", result.Issues[0].Error);
}
```

`FakePathAccess` must implement the same `IPathAccess` contract used by production:

```csharp
public interface IPathAccess
{
    PathEntryKind GetKind(string path);
    IEnumerable<string> EnumerateChildren(string directory);
    long GetFileLength(string file);
    string GetFullPath(string path);
}
```

- [ ] **Step 2: Run scanner tests and verify failure**

```powershell
dotnet test tests/Emke.AiMarker.Core.Tests/Emke.AiMarker.Core.Tests.csproj --filter InputScannerTests
```

Expected: build fails because scanner types do not exist.

- [ ] **Step 3: Implement scanner models and behavior**

Define:

```csharp
public enum PathEntryKind
{
    Missing,
    File,
    Directory,
    ReparseFile,
    ReparseDirectory,
}

public sealed record DiscoveredMedia(
    string SourcePath,
    string TopLevelInput,
    string RelativePath,
    string Extension,
    long Length);

public sealed record ScanIssue(string Path, string Error);
public sealed record ScanResult(
    IReadOnlyList<DiscoveredMedia> Media,
    IReadOnlyList<ScanIssue> Issues);
```

`InputScanner` must recurse only through `Directory`, add only supported files,
deduplicate by full path with `StringComparer.OrdinalIgnoreCase`, and sort by
`RelativePath` with `StringComparer.OrdinalIgnoreCase`. Catch access errors
per child, append a `ScanIssue`, and continue scanning other inputs.

- [ ] **Step 4: Write failing output-plan and storage tests**

```csharp
[Fact]
public void Folder_input_preserves_root_name_and_relative_structure()
{
    var media = new DiscoveredMedia(
        @"D:\商品\春季\look.JPG",
        @"D:\商品",
        @"春季\look.JPG",
        ".JPG",
        100);

    OutputPlanItem item = OutputPlanner.Plan([media], customOutputRoot: null).Single();

    Assert.Equal(
        @"D:\EMKE 已标记\商品\春季\look.JPG",
        item.FinalPath);
    Assert.EndsWith(".JPG", item.TempPath, StringComparison.OrdinalIgnoreCase);
    Assert.Contains(".emke-ai-marker-", Path.GetFileName(item.TempPath));
}

[Fact]
public void Storage_preflight_requires_total_plus_larger_margin()
{
    var plans = new[]
    {
        new OutputPlanItem("a", "a", @"D:\out\a.jpg", @"D:\out\.tmp.jpg", 1_000_000_000),
    };
    var storage = new FakeStorageProbe(availableBytes: 1_200_000_000);

    StorageCheck result = new StoragePreflight(storage).Check(plans);

    Assert.False(result.IsReady);
    Assert.Equal(1_268_435_456, result.RequiredBytes);
}
```

The 256 MiB minimum margin is `268_435_456` bytes.

- [ ] **Step 5: Implement output planning and storage preflight**

Define:

```csharp
public sealed record OutputPlanItem(
    string SourcePath,
    string RelativePath,
    string FinalPath,
    string TempPath,
    long Length);

public interface IStorageProbe
{
    long GetAvailableBytes(string directory);
    void AssertWritable(string directory);
}

public sealed record StorageCheck(
    bool IsReady,
    long RequiredBytes,
    long AvailableBytes,
    string Error);
```

Default folder layout is:

```text
<top-level parent>\EMKE 已标记\<top-level folder name>\<relative path>
```

Default single-file layout is:

```text
<source parent>\EMKE 已标记\<file name>
```

For a custom output root, prefix folder inputs with the top-level folder name and place individual file inputs directly under the root. Generate temporary names as:

```csharp
string tempName =
    $".emke-ai-marker-{Guid.NewGuid():N}.tmp{Path.GetExtension(finalPath)}";
```

Calculate required bytes with checked arithmetic:

```csharp
long total = plans.Sum(item => item.Length);
long margin = Math.Max(total / 20, 256L * 1024 * 1024);
long required = checked(total + margin);
```

Before returning a ready result, call `AssertWritable` once for each distinct
destination directory. The physical implementation creates, flushes, and
deletes an application-namespaced zero-byte probe file in that directory. A
failure becomes a `StorageCheck` error and does not start processing.

`DiscoveryFakes.cs` defines the fluent `FakePathAccess` tree and
`FakeStorageProbe` used by the tests. The storage fake records
`AssertWritable` calls and can be configured to throw `UnauthorizedAccessException`.

- [ ] **Step 6: Add Infrastructure to the solution and implement physical path access**

Run:

```powershell
dotnet new classlib -n Emke.AiMarker.Infrastructure -o src/Emke.AiMarker.Infrastructure -f net10.0-windows
dotnet add src/Emke.AiMarker.Infrastructure/Emke.AiMarker.Infrastructure.csproj reference src/Emke.AiMarker.Core/Emke.AiMarker.Core.csproj
dotnet sln Emke.AiMarker.sln add src/Emke.AiMarker.Infrastructure/Emke.AiMarker.Infrastructure.csproj
```

`PhysicalPathAccess.GetKind` must map `FileAttributes.ReparsePoint` to the reparse enum values before returning ordinary file or directory kinds. `EnumerateChildren` must not follow links.

`WindowsStorageProbe.GetAvailableBytes` resolves the destination drive through
`DriveInfo`, and `AssertWritable` creates, flushes, closes, and deletes only a
`.emke-ai-marker-probe-<guid>.tmp` file that it owns.

- [ ] **Step 7: Run tests and commit**

```powershell
dotnet test Emke.AiMarker.sln
git add src/Emke.AiMarker.Core src/Emke.AiMarker.Infrastructure tests/Emke.AiMarker.Core.Tests Emke.AiMarker.sln
git commit -m "feat: add safe media discovery and output planning"
```

### Task 4: Implement the ExifTool client and runtime integrity checks

**Files:**
- Create: `src/Emke.AiMarker.Core/Abstractions/IExifToolClient.cs`
- Create: `src/Emke.AiMarker.Infrastructure/ExifTool/IProcessRunner.cs`
- Create: `src/Emke.AiMarker.Infrastructure/ExifTool/ProcessRunner.cs`
- Create: `src/Emke.AiMarker.Infrastructure/ExifTool/ExifToolClient.cs`
- Create: `src/Emke.AiMarker.Infrastructure/ExifTool/ExifToolExceptions.cs`
- Create: `src/Emke.AiMarker.Infrastructure/ExifTool/ExifToolManifestValidator.cs`
- Create: `tests/Emke.AiMarker.Infrastructure.Tests/Emke.AiMarker.Infrastructure.Tests.csproj`
- Create: `tests/Emke.AiMarker.Infrastructure.Tests/ExifTool/ExifToolClientTests.cs`
- Create: `tests/Emke.AiMarker.Infrastructure.Tests/ExifTool/ExifToolManifestValidatorTests.cs`
- Create: `tests/Emke.AiMarker.Infrastructure.Tests/TestSupport/RecordingProcessRunner.cs`

**Interfaces:**
- Consumes: `MarkerContract.Marker`.
- Produces: `IExifToolClient`, `ExifToolClient`, `ExifToolManifestValidator`, `IProcessRunner`, `ProcessExecutionResult`, `MarkerOperationException`, and `ExifToolIntegrityException`.

- [ ] **Step 1: Define the Core port and write failing argument tests**

```csharp
public interface IExifToolClient
{
    Task<string> GetVersionAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ReadSubjectsAsync(
        string path,
        CancellationToken cancellationToken);
    Task WriteMarkerAsync(string path, CancellationToken cancellationToken);
    Task<ReadOnlyMemory<byte>> ReadRawXmpAsync(
        string path,
        CancellationToken cancellationToken);
    Task<string> ReadImageDataHashAsync(
        string path,
        CancellationToken cancellationToken);
}
```

`RecordingProcessRunner` is a test-only `IProcessRunner`.
`LastArgumentFileLines` records the most recent request, and
`WithStdout(string)` returns UTF-8 stdout with exit code `0`.

Test exact calls with a fake process runner:

```csharp
[Fact]
public async Task Write_marker_uses_exact_append_and_no_backup_options()
{
    var runner = new RecordingProcessRunner();
    var client = new ExifToolClient(@"C:\app\exiftool\exiftool.exe", runner);

    await client.WriteMarkerAsync(@"D:\中文 示例.mp4", CancellationToken.None);

    Assert.Equal(
        [
            "-overwrite_original",
            "-P",
            "-XMP-dc:Subject+=contains-synthetic-performer",
            @"D:\中文 示例.mp4",
        ],
        runner.LastArgumentFileLines);
}

[Fact]
public async Task Read_subjects_requests_only_explicit_xmp_dc_subject()
{
    var runner = RecordingProcessRunner.WithStdout(
        """[{"XMP-dc:Subject":["existing","contains-synthetic-performer"]}]""");
    var client = new ExifToolClient(@"C:\app\exiftool\exiftool.exe", runner);

    IReadOnlyList<string> result =
        await client.ReadSubjectsAsync(@"D:\image.jpg", CancellationToken.None);

    Assert.Equal(["existing", "contains-synthetic-performer"], result);
    Assert.DoesNotContain(
        "Microsoft:Category",
        string.Join('\n', runner.LastArgumentFileLines),
        StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run tests and verify failure**

```powershell
dotnet new xunit -n Emke.AiMarker.Infrastructure.Tests -o tests/Emke.AiMarker.Infrastructure.Tests -f net10.0-windows
dotnet add tests/Emke.AiMarker.Infrastructure.Tests/Emke.AiMarker.Infrastructure.Tests.csproj reference src/Emke.AiMarker.Infrastructure/Emke.AiMarker.Infrastructure.csproj
dotnet sln Emke.AiMarker.sln add tests/Emke.AiMarker.Infrastructure.Tests/Emke.AiMarker.Infrastructure.Tests.csproj
dotnet test tests/Emke.AiMarker.Infrastructure.Tests/Emke.AiMarker.Infrastructure.Tests.csproj
```

Expected: build fails because the client and runner do not exist.

- [ ] **Step 3: Implement process execution and JSON parsing**

Use one ExifTool process per operation with:

```csharp
var startInfo = new ProcessStartInfo
{
    FileName = executable,
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true,
};
startInfo.ArgumentList.Add("-charset");
startInfo.ArgumentList.Add("filename=UTF8");
startInfo.ArgumentList.Add("-@");
startInfo.ArgumentList.Add("-");
```

Write each operation argument as one UTF-8 line to stdin, close stdin, and capture stdout/stderr as bytes. Return:

```csharp
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

public sealed class MarkerOperationException : Exception
{
    public MarkerOperationException(string message) : base(message) { }
}

public sealed class ExifToolIntegrityException : Exception
{
    public ExifToolIntegrityException(string message) : base(message) { }
}
```

Use a five-minute timeout for media operations and a thirty-second timeout for `-ver`. Convert non-zero exits into `MarkerOperationException` containing stderr, stdout, or the exit code in that order.

- [ ] **Step 4: Implement runtime manifest validation with failing tamper tests**

`ExifToolManifestValidator.Validate(runtimeRoot, lockPath)` must verify:

- lock version is `13.59`;
- archive size and SHA-256 formats are valid;
- runtime manifest schema is `1`;
- manifest metadata matches the lock;
- every runtime payload file size and SHA-256 matches;
- `exiftool.exe`, `README.txt`, `exiftool_files`, and required ExifTool license files exist;
- no payload entry is a reparse point.

The tamper test must change one byte in a fake `perl.exe` and assert that validation throws `ExifToolIntegrityException`.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test tests/Emke.AiMarker.Infrastructure.Tests/Emke.AiMarker.Infrastructure.Tests.csproj
git add src/Emke.AiMarker.Core/Abstractions/IExifToolClient.cs src/Emke.AiMarker.Infrastructure/ExifTool tests/Emke.AiMarker.Infrastructure.Tests Emke.AiMarker.sln
git commit -m "feat: add verified ExifTool process client"
```

### Task 5: Implement safe-copy transactions and single-file processing

**Files:**
- Create: `src/Emke.AiMarker.Core/Abstractions/IFileTransaction.cs`
- Create: `src/Emke.AiMarker.Core/Abstractions/IFileProcessor.cs`
- Create: `src/Emke.AiMarker.Core/Abstractions/IOriginalWriteSafety.cs`
- Create: `src/Emke.AiMarker.Infrastructure/Files/OwnedTempFile.cs`
- Create: `src/Emke.AiMarker.Infrastructure/Files/PhysicalCopyTransaction.cs`
- Create: `src/Emke.AiMarker.Infrastructure/Files/WindowsFileSafety.cs`
- Create: `src/Emke.AiMarker.Core/Processing/MediaProcessor.cs`
- Test: `tests/Emke.AiMarker.Core.Tests/Processing/MediaProcessorTests.cs`
- Create: `tests/Emke.AiMarker.Core.Tests/TestSupport/ProcessingFakes.cs`
- Test: `tests/Emke.AiMarker.Infrastructure.Tests/Files/PhysicalCopyTransactionTests.cs`

**Interfaces:**
- Consumes: `OutputPlanItem`, `IExifToolClient`, `XmpComplianceVerifier`, `ProcessResult`.
- Produces: `IFileTransaction`, `IFileProcessor`, `IOriginalWriteSafety`, `PreparedMedia`, `MediaProcessor.ProcessAsync`, and owned temporary-file cleanup.

- [ ] **Step 1: Write failing default-copy behavior tests**

```csharp
[Fact]
public async Task Copy_mode_preserves_source_and_commits_only_after_verification()
{
    var files = new FakeFileTransaction(sourceBytes: [1, 2, 3]);
    var exif = new FakeExifToolClient(
        beforeSubjects: ["existing"],
        afterSubjects: ["existing", MarkerContract.Marker],
        rawXmp: TestXmp.ValidBag("existing", MarkerContract.Marker));
    var processor = new MediaProcessor(
        files,
        exif,
        new FakeOriginalWriteSafety(),
        new FixedTimeProvider());
    var plan = TestPlans.Copy("商品.jpg");

    ProcessResult result = await processor.ProcessAsync(
        plan,
        RunMode.MarkCopies,
        CancellationToken.None);

    Assert.Equal(ProcessStatus.Added, result.Status);
    Assert.Equal([1, 2, 3], files.SourceBytes);
    Assert.True(files.CommitCalled);
    Assert.False(files.RollbackCalled);
}

[Fact]
public async Task Failed_verification_rolls_back_temp_and_keeps_source()
{
    var files = new FakeFileTransaction(sourceBytes: [1, 2, 3]);
    var exif = new FakeExifToolClient(
        beforeSubjects: [],
        afterSubjects: [MarkerContract.Marker],
        rawXmp: TestXmp.RdfSeq(MarkerContract.Marker));
    var processor = new MediaProcessor(
        files,
        exif,
        new FakeOriginalWriteSafety(),
        new FixedTimeProvider());

    ProcessResult result = await processor.ProcessAsync(
        TestPlans.Copy("商品.jpg"),
        RunMode.MarkCopies,
        CancellationToken.None);

    Assert.Equal(ProcessStatus.Failed, result.Status);
    Assert.False(files.CommitCalled);
    Assert.True(files.RollbackCalled);
    Assert.Equal([1, 2, 3], files.SourceBytes);
}
```

- [ ] **Step 2: Run tests and verify failure**

```powershell
dotnet test tests/Emke.AiMarker.Core.Tests/Emke.AiMarker.Core.Tests.csproj --filter MediaProcessorTests
```

Expected: build fails because transaction and processor types do not exist.

- [ ] **Step 3: Implement the transaction port**

```csharp
public sealed record PreparedMedia(
    string SourcePath,
    string WorkingPath,
    string FinalPath);

public interface IFileTransaction
{
    Task<PreparedMedia> PrepareAsync(
        OutputPlanItem plan,
        RunMode mode,
        CancellationToken cancellationToken);
    Task CommitAsync(PreparedMedia media, CancellationToken cancellationToken);
    Task RollbackAsync(PreparedMedia media);
}

public interface IFileProcessor
{
    Task<ProcessResult> ProcessAsync(
        OutputPlanItem plan,
        RunMode mode,
        CancellationToken cancellationToken);
}

public interface IOriginalWriteSafety
{
    void Validate(OutputPlanItem plan);
}
```

`ProcessingFakes.cs` defines the test-only `FakeFileTransaction`,
`FakeExifToolClient`, `FakeOriginalWriteSafety`, `FixedTimeProvider`,
`TestPlans`, and `TestXmp` members used above. `TestXmp.ValidBag` emits the
formal namespaces from `MarkerContract`; `TestXmp.RdfSeq` differs only by
using `rdf:Seq`.

`PhysicalCopyTransaction` rules:

- copy mode creates directories and copies source to `TempPath`;
- original mode uses source as `WorkingPath` and never calls `File.Copy`;
- verify-only mode uses source as `WorkingPath` and never creates output;
- commit uses same-volume `File.Move(temp, final, overwrite: false)`;
- rollback deletes only a path whose filename starts `.emke-ai-marker-` and whose parent equals the planned destination directory;
- source files are never deleted.

`WindowsFileSafety.Validate` reads current Windows attributes immediately
before an original write and rejects missing, read-only, hidden, system, or
reparse-point files with an actionable error.

- [ ] **Step 4: Implement the processor decision order**

`MediaProcessor.ProcessAsync` must:

1. in copy mode, inspect an existing `FinalPath` before creating a temp file;
   return `OutputAlreadyCompliant` only if that existing output passes strict
   verification, otherwise return `Failed` with `目标冲突`;
2. prepare media;
3. read existing subjects;
4. in verify-only mode, verify and return without writing;
5. if the exact marker already exists, strictly verify it; in copy mode commit
   the verified temp copy, then return `AlreadyCompliant`;
6. in original mode, call the injected `IOriginalWriteSafety.Validate` before
   source overwrite; the Infrastructure composition root supplies
   `WindowsFileSafety`;
7. append the marker once;
8. read subjects again and read raw XMP;
9. strictly verify;
10. commit only when copy-mode verification passes;
11. rollback owned temp files on every failure.

Do not pass the batch stop token into an operation after that file starts. The batch coordinator controls whether the next file starts.

- [ ] **Step 5: Add destination conflict tests**

Cover:

- compliant existing output returns `OutputAlreadyCompliant`;
- noncompliant existing output returns `Failed` with “目标冲突”;
- no path silently overwrites an existing output.

- [ ] **Step 6: Run tests and commit**

```powershell
dotnet test Emke.AiMarker.sln
git add src/Emke.AiMarker.Core src/Emke.AiMarker.Infrastructure tests/Emke.AiMarker.Core.Tests tests/Emke.AiMarker.Infrastructure.Tests
git commit -m "feat: add atomic safe-copy media processing"
```

### Task 6: Add batch coordination, safe stop, progress, and CSV evidence

**Files:**
- Create: `src/Emke.AiMarker.Core/Abstractions/IBatchProcessor.cs`
- Create: `src/Emke.AiMarker.Core/Abstractions/IRunLogWriter.cs`
- Create: `src/Emke.AiMarker.Core/Models/RunModels.cs`
- Create: `src/Emke.AiMarker.Core/Processing/StopController.cs`
- Create: `src/Emke.AiMarker.Core/Processing/BatchProcessor.cs`
- Create: `src/Emke.AiMarker.Infrastructure/Logging/CsvRunLogWriter.cs`
- Test: `tests/Emke.AiMarker.Core.Tests/Processing/BatchProcessorTests.cs`
- Create: `tests/Emke.AiMarker.Core.Tests/TestSupport/BatchFakes.cs`
- Test: `tests/Emke.AiMarker.Infrastructure.Tests/Logging/CsvRunLogWriterTests.cs`
- Create: `tests/Emke.AiMarker.Infrastructure.Tests/TestSupport/CsvTestParser.cs`

**Interfaces:**
- Consumes: `MediaProcessor.ProcessAsync` and `ProcessResult`.
- Produces: `StopController`, `RunProgress`, `RunSummary`, `IBatchProcessor`, `BatchProcessor.RunAsync`, `IRunLogWriter`, and `CsvRunLogWriter`.

- [ ] **Step 1: Write failing safe-stop tests**

```csharp
[Fact]
public async Task Stop_finishes_current_file_and_marks_remaining_files_unprocessed()
{
    var stop = new StopController();
    var processor = new SequencedProcessor(onFirstCompleted: stop.RequestStop);
    var batch = new BatchProcessor(processor, new InMemoryLogWriter());

    RunSummary summary = await batch.RunAsync(
        TestPlans.Many("a.jpg", "b.jpg", "c.mp4"),
        RunMode.MarkCopies,
        logDirectory: @"D:\logs",
        stop: stop,
        progress: null,
        cancellationToken: CancellationToken.None);

    Assert.Single(processor.StartedPaths);
    Assert.Equal("a.jpg", processor.StartedPaths[0]);
    Assert.Equal(2, summary.Results.Count(
        result => result.Status == ProcessStatus.StoppedBeforeProcessing));
    Assert.True(summary.LogWritten);
}
```

- [ ] **Step 2: Write failing CSV tests**

```csharp
[Fact]
public async Task Csv_has_utf8_bom_eleven_columns_and_neutralized_formulas()
{
    var result = TestResults.Failed(
        relativePath: "=HYPERLINK(\"https://example.invalid\")",
        error: "+危险公式");

    string path = await new CsvRunLogWriter().WriteAsync(
        _temp,
        RunMode.MarkCopies,
        [result],
        CancellationToken.None);

    byte[] raw = await File.ReadAllBytesAsync(path);
    Assert.Equal([0xEF, 0xBB, 0xBF], raw[..3]);
    string[] rows = await File.ReadAllLinesAsync(path, Encoding.UTF8);
    Assert.Equal(11, CsvTestParser.Parse(rows[0]).Count);
    Assert.StartsWith("'=", CsvTestParser.Parse(rows[1])[0]);
    Assert.StartsWith("'+", CsvTestParser.Parse(rows[1])[10]);
}
```

- [ ] **Step 3: Implement stop and batch models**

```csharp
public sealed class StopController
{
    private int _requested;
    public bool IsStopRequested => Volatile.Read(ref _requested) == 1;
    public void RequestStop() => Interlocked.Exchange(ref _requested, 1);
}

public sealed record RunProgress(
    int Completed,
    int Total,
    string CurrentRelativePath,
    IReadOnlyDictionary<ProcessStatus, int> Counts);

public sealed record RunSummary(
    RunMode Mode,
    IReadOnlyList<ProcessResult> Results,
    string LogPath,
    bool LogWritten,
    bool Stopped);

public interface IBatchProcessor
{
    Task<RunSummary> RunAsync(
        IReadOnlyList<OutputPlanItem> plans,
        RunMode mode,
        string logDirectory,
        StopController stop,
        IProgress<RunProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IRunLogWriter
{
    Task<string> WriteAsync(
        string logDirectory,
        RunMode mode,
        IReadOnlyList<ProcessResult> results,
        CancellationToken cancellationToken);
}
```

`BatchProcessor : IBatchProcessor` depends on `IFileProcessor`. It checks
`IsStopRequested` immediately before each file, appends
`StoppedBeforeProcessing` results for every remaining plan, and always calls
the log writer in `finally`. It catches an exception from one file, converts
it to a `Failed` result with that file's relative path, and continues with the
next plan. `BatchFakes.cs` defines the test-only
`SequencedProcessor`, `InMemoryLogWriter`, `TestPlans`, and `TestResults`.

- [ ] **Step 4: Implement atomic CSV writing**

Use `new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)`. Write to:

```text
.<final-name>.<guid>.tmp
```

in the same log directory, flush the stream, then `File.Move(temp, final, overwrite: false)`. Escape double quotes, commas, and newlines per RFC 4180. Prefix cells whose first non-space character is `=`, `+`, `-`, `@`, tab, carriage return, or newline with `'`.

Use these exact eleven headers:

```text
相对路径,格式,运行模式,处理状态,验证结果,验证字段,实际读取值,XMP结构,验证时间,ExifTool版本,错误原因
```

`CsvTestParser.Parse(string)` is a test-only RFC 4180 row parser used to
assert field boundaries without relying on the production writer.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test Emke.AiMarker.sln
git add src/Emke.AiMarker.Core src/Emke.AiMarker.Infrastructure tests
git commit -m "feat: add safe batch stop and CSV evidence"
```

### Task 7: Build the WPF MVVM state machine

**Files:**
- Create: `src/Emke.AiMarker.App/Emke.AiMarker.App.csproj`
- Create: `src/Emke.AiMarker.App/ViewModels/ObservableObject.cs`
- Create: `src/Emke.AiMarker.App/ViewModels/AsyncRelayCommand.cs`
- Create: `src/Emke.AiMarker.App/ViewModels/MainWindowViewModel.cs`
- Create: `src/Emke.AiMarker.App/Services/IFileSelectionService.cs`
- Create: `src/Emke.AiMarker.App/Services/IUserPromptService.cs`
- Create: `src/Emke.AiMarker.App/Services/IShellService.cs`
- Create: `tests/Emke.AiMarker.App.Tests/Emke.AiMarker.App.Tests.csproj`
- Create: `tests/Emke.AiMarker.App.Tests/ViewModels/MainWindowViewModelTests.cs`
- Create: `tests/Emke.AiMarker.App.Tests/TestSupport/MainWindowHarness.cs`

**Interfaces:**
- Consumes: scanner, output planner, storage preflight, batch processor, and run models.
- Produces: `MainWindowViewModel`, `WorkspaceState`, command implementations, and UI service ports.

- [ ] **Step 1: Create the WPF and App test projects**

```powershell
dotnet new wpf -n Emke.AiMarker.App -o src/Emke.AiMarker.App -f net10.0-windows
dotnet new xunit -n Emke.AiMarker.App.Tests -o tests/Emke.AiMarker.App.Tests -f net10.0-windows
dotnet add src/Emke.AiMarker.App/Emke.AiMarker.App.csproj reference src/Emke.AiMarker.Core/Emke.AiMarker.Core.csproj src/Emke.AiMarker.Infrastructure/Emke.AiMarker.Infrastructure.csproj
dotnet add tests/Emke.AiMarker.App.Tests/Emke.AiMarker.App.Tests.csproj reference src/Emke.AiMarker.App/Emke.AiMarker.App.csproj
dotnet sln Emke.AiMarker.sln add src/Emke.AiMarker.App/Emke.AiMarker.App.csproj tests/Emke.AiMarker.App.Tests/Emke.AiMarker.App.Tests.csproj
```

Set App project properties:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
  <UseWPF>true</UseWPF>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>false</PublishSingleFile>
  <AssemblyName>EMKE AI Marker</AssemblyName>
  <ApplicationIcon>Assets\emke-ai-marker.ico</ApplicationIcon>
</PropertyGroup>
```

- [ ] **Step 2: Write failing state-transition tests**

```csharp
[Fact]
public async Task Selecting_supported_media_moves_empty_to_ready()
{
    var harness = MainWindowHarness.Empty();

    await harness.ViewModel.AddPathsAsync([@"D:\商品"]);

    Assert.Equal(WorkspaceState.Ready, harness.ViewModel.State);
    Assert.Equal(28, harness.ViewModel.MediaCount);
    Assert.True(harness.ViewModel.StartMarkCommand.CanExecute(null));
}

[Fact]
public async Task Run_locks_input_exposes_safe_stop_and_finishes_completed()
{
    var harness = MainWindowHarness.ReadyWithMedia();
    harness.Batch.BlockUntilReleased();

    Task run = harness.ViewModel.StartMarkAsync();

    Assert.Equal(WorkspaceState.Running, harness.ViewModel.State);
    Assert.False(harness.ViewModel.AddFilesCommand.CanExecute(null));
    Assert.True(harness.ViewModel.SafeStopCommand.CanExecute(null));

    harness.Batch.Release(TestSummaries.Success());
    await run;

    Assert.Equal(WorkspaceState.Completed, harness.ViewModel.State);
    Assert.True(harness.ViewModel.OpenOutputCommand.CanExecute(null));
    Assert.True(harness.ViewModel.OpenLogCommand.CanExecute(null));
}
```

- [ ] **Step 3: Implement MVVM primitives without a package**

`ObservableObject` implements `INotifyPropertyChanged` and:

```csharp
protected bool SetProperty<T>(
    ref T field,
    T value,
    [CallerMemberName] string? propertyName = null)
```

`AsyncRelayCommand` implements `ICommand`, prevents concurrent execution, exposes `NotifyCanExecuteChanged`, and surfaces exceptions to a supplied `Func<Exception, Task>` callback rather than swallowing them.

- [ ] **Step 4: Implement the view-model state surface**

```csharp
public enum WorkspaceState
{
    Empty,
    Ready,
    Running,
    Completed,
}
```

`MainWindowViewModel` consumes `IBatchProcessor` and must expose:

```text
State, MediaCount, TotalBytes, ProcessableCount, SkippedCount,
CurrentRelativePath, CompletedCount, TotalCount, ProgressPercent,
OutputPath, IsDetailsExpanded, IsOverwriteOriginals,
MediaItems, Results, ScanIssues, SummaryMessage
```

and commands:

```text
AddFilesCommand, AddFolderCommand, StartMarkCommand, VerifyOnlyCommand,
SafeStopCommand, ResetCommand, OpenOutputCommand, OpenLogCommand,
OpenSettingsCommand, ToggleDetailsCommand
```

It also exposes the event-adapter methods used by selection services and
code-behind:

```csharp
public Task AddPathsAsync(IReadOnlyList<string> paths);
public Task StartMarkAsync();
```

`IsOverwriteOriginals` resets to `false` on every app launch and after every completed or reset run.

`MainWindowHarness` is the shared App-test composition helper. `Empty()` uses
an empty fake path tree, `ReadyWithMedia()` uses controlled discovered media,
its `Batch` property is a controllable `IBatchProcessor`, and its `Prompts`
property records `IUserPromptService` calls. The helper also exposes
`TestSummaries.Success()` with a completed, logged `RunSummary`.

- [ ] **Step 5: Run view-model tests and commit**

```powershell
dotnet test tests/Emke.AiMarker.App.Tests/Emke.AiMarker.App.Tests.csproj
git add src/Emke.AiMarker.App tests/Emke.AiMarker.App.Tests Emke.AiMarker.sln
git commit -m "feat: add WPF marker workspace state machine"
```

### Task 8: Implement the confirmed compact single-page UI and brand assets

**Files:**
- Create: `assets/branding/emke-app-logo.svg`
- Create: `assets/branding/brand-assets.json`
- Create: `src/Emke.AiMarker.App/Assets/emke-app-logo-32.png`
- Create: `src/Emke.AiMarker.App/Assets/emke-app-logo-256.png`
- Create: `src/Emke.AiMarker.App/Assets/emke-ai-marker.ico`
- Create: `src/Emke.AiMarker.App/Resources/Theme.xaml`
- Create: `src/Emke.AiMarker.App/Resources/Controls.xaml`
- Create: `src/Emke.AiMarker.App/Resources/Strings.zh-CN.xaml`
- Modify: `src/Emke.AiMarker.App/App.xaml`
- Modify: `src/Emke.AiMarker.App/MainWindow.xaml`
- Modify: `src/Emke.AiMarker.App/MainWindow.xaml.cs`
- Create: `src/Emke.AiMarker.App/Services/FileSelectionService.cs`
- Create: `src/Emke.AiMarker.App/Services/ShellService.cs`
- Test: `tests/Emke.AiMarker.App.Tests/Resources/BrandResourceTests.cs`
- Create: `tests/Emke.AiMarker.App.Tests/TestSupport/RepositoryRoot.cs`

**Interfaces:**
- Consumes: `MainWindowViewModel` and its commands/properties.
- Produces: the confirmed compact single-page WPF shell, real Logo resources, centralized Chinese strings, and native file/folder selection.

- [ ] **Step 1: Copy and verify the exact SVG**

```bash
mkdir -p assets/branding
cp "/Users/hale/Downloads/Group 335 (1).svg" assets/branding/emke-app-logo.svg
shasum -a 256 assets/branding/emke-app-logo.svg
```

Expected SHA-256:

```text
c98e5a189b344bd5adb6b49848acdf307ce0f12c4e60a428c4c421fe258142a6
```

Create `brand-assets.json`:

```json
{
  "source": "emke-app-logo.svg",
  "source_width": 202,
  "source_height": 202,
  "source_sha256": "c98e5a189b344bd5adb6b49848acdf307ce0f12c4e60a428c4c421fe258142a6",
  "accent": "#36A39E"
}
```

- [ ] **Step 2: Generate PNG and ICO derivatives from the real SVG**

On macOS:

```bash
brand_tmp=$(mktemp -d /tmp/emke-brand.XXXXXX)
qlmanage -t -s 256 -o "$brand_tmp" assets/branding/emke-app-logo.svg
sips -z 32 32 "$brand_tmp/emke-app-logo.svg.png" --out src/Emke.AiMarker.App/Assets/emke-app-logo-32.png
cp "$brand_tmp/emke-app-logo.svg.png" src/Emke.AiMarker.App/Assets/emke-app-logo-256.png
sips -s format ico src/Emke.AiMarker.App/Assets/emke-app-logo-256.png --out src/Emke.AiMarker.App/Assets/emke-ai-marker.ico
```

Open all three rendered assets and confirm they show the black E-shaped form and `#36A39E` wave without cropping, blank output, or a substituted design.

- [ ] **Step 3: Write failing brand resource tests**

```csharp
[Fact]
public void Brand_source_hash_and_dimensions_match_approved_asset()
{
    string root = RepositoryRoot.Find();
    string svg = Path.Combine(root, "assets", "branding", "emke-app-logo.svg");

    Assert.Equal(
        "c98e5a189b344bd5adb6b49848acdf307ce0f12c4e60a428c4c421fe258142a6",
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(svg))).ToLowerInvariant());
    Assert.Contains("width=\"202\"", File.ReadAllText(svg));
    Assert.Contains("height=\"202\"", File.ReadAllText(svg));
    Assert.Contains("#36A39E", File.ReadAllText(svg));
}
```

Add PNG dimension checks with `BitmapDecoder` and assert the ICO is non-empty.
`RepositoryRoot.Find()` walks parent directories from
`AppContext.BaseDirectory` until it finds `Emke.AiMarker.sln`, then returns
that directory or throws `DirectoryNotFoundException`.

- [ ] **Step 4: Implement theme and string resources**

`Theme.xaml` must define:

```xml
<Color x:Key="BrandAccentColor">#36A39E</Color>
<Color x:Key="SafetyBackgroundColor">#E3F3F1</Color>
<Color x:Key="AppBackgroundColor">#F7F9F9</Color>
<Color x:Key="PrimaryTextColor">#172022</Color>
<Color x:Key="DangerColor">#B42318</Color>
<FontFamily x:Key="AppFontFamily">Segoe UI Variable, Segoe UI</FontFamily>
<SolidColorBrush x:Key="BrandAccentBrush" Color="{StaticResource BrandAccentColor}" />
<SolidColorBrush x:Key="SafetyBackgroundBrush" Color="{StaticResource SafetyBackgroundColor}" />
<SolidColorBrush x:Key="AppBackgroundBrush" Color="{StaticResource AppBackgroundColor}" />
<SolidColorBrush x:Key="PrimaryTextBrush" Color="{StaticResource PrimaryTextColor}" />
<SolidColorBrush x:Key="DangerBrush" Color="{StaticResource DangerColor}" />
```

`Strings.zh-CN.xaml` must contain every visible string, including the title, drag/drop copy, supported formats, read-only marker label, safe-copy statement, buttons, result categories, advanced warning, and error actions. Do not hard-code user-visible Chinese text in code-behind or the ViewModel.

- [ ] **Step 5: Implement the compact XAML shell**

Use one `Window` with `FontFamily="{StaticResource AppFontFamily}"` and:

```xml
<Grid Background="{StaticResource AppBackgroundBrush}">
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto" />
    <RowDefinition Height="*" />
  </Grid.RowDefinitions>

  <Border Grid.Row="0" Background="White" BorderBrush="#E0E6E6"
          BorderThickness="0,0,0,1" Padding="16,10">
    <DockPanel>
      <StackPanel Orientation="Horizontal" DockPanel.Dock="Left">
        <Image Source="Assets/emke-app-logo-32.png" Width="27" Height="27" />
        <TextBlock Text="{DynamicResource AppName}" Margin="9,0,0,0"
                   VerticalAlignment="Center" FontWeight="SemiBold" />
      </StackPanel>
      <Button Content="{DynamicResource SettingsButton}"
              Command="{Binding OpenSettingsCommand}" DockPanel.Dock="Right" />
    </DockPanel>
  </Border>

  <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
    <Grid MaxWidth="720" Margin="22">
      <StackPanel>
        <TextBlock Text="{DynamicResource WorkspaceTitle}"
                   FontSize="24" FontWeight="SemiBold" />
        <TextBlock Text="{DynamicResource OfflinePrivacyCopy}"
                   Margin="0,6,0,16" TextWrapping="Wrap" />

        <Border x:Name="DropTarget" AllowDrop="True"
                BorderBrush="{StaticResource BrandAccentBrush}"
                BorderThickness="1" CornerRadius="10" Padding="20">
          <StackPanel HorizontalAlignment="Center">
            <TextBlock Text="{DynamicResource DropTargetTitle}"
                       FontWeight="SemiBold" HorizontalAlignment="Center" />
            <TextBlock Text="{DynamicResource SupportedFormats}"
                       Margin="0,4,0,12" HorizontalAlignment="Center" />
            <StackPanel Orientation="Horizontal">
              <Button Content="{DynamicResource AddFilesButton}"
                      Command="{Binding AddFilesCommand}" />
              <Button Content="{DynamicResource AddFolderButton}"
                      Command="{Binding AddFolderCommand}" Margin="8,0,0,0" />
            </StackPanel>
          </StackPanel>
        </Border>

        <StackPanel Margin="0,16,0,0">
          <StackPanel.Style>
            <Style TargetType="StackPanel">
              <Setter Property="Visibility" Value="Collapsed" />
              <Style.Triggers>
                <DataTrigger Binding="{Binding State}"
                             Value="{x:Static vm:WorkspaceState.Ready}">
                  <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
              </Style.Triggers>
            </Style>
          </StackPanel.Style>
          <TextBlock Text="{Binding SummaryMessage}" FontWeight="SemiBold" />
          <TextBlock Margin="0,8,0,3"
                     Text="{DynamicResource MarkerLabel}" />
          <TextBox Text="contains-synthetic-performer"
                   IsReadOnly="True" IsTabStop="False" />
          <Border Margin="0,12,0,0" Padding="12" CornerRadius="8"
                  Background="{StaticResource SafetyBackgroundBrush}">
            <TextBlock Text="{DynamicResource SafeCopyStatement}"
                       TextWrapping="Wrap" />
          </Border>
          <Expander Header="{DynamicResource FileDetails}"
                    IsExpanded="{Binding IsDetailsExpanded}" Margin="0,12,0,0">
            <DataGrid ItemsSource="{Binding MediaItems}" IsReadOnly="True"
                      AutoGenerateColumns="False">
              <DataGrid.Columns>
                <DataGridTextColumn Header="{DynamicResource FileColumn}"
                                    Binding="{Binding RelativePath}" />
                <DataGridTextColumn Header="{DynamicResource StatusColumn}"
                                    Binding="{Binding Status}" />
              </DataGrid.Columns>
            </DataGrid>
          </Expander>
          <StackPanel Orientation="Horizontal" Margin="0,16,0,0">
            <Button Content="{DynamicResource StartMarkButton}"
                    Command="{Binding StartMarkCommand}" />
            <Button Content="{DynamicResource VerifyOnlyButton}"
                    Command="{Binding VerifyOnlyCommand}" Margin="8,0,0,0" />
          </StackPanel>
        </StackPanel>

        <StackPanel Margin="0,16,0,0">
          <StackPanel.Style>
            <Style TargetType="StackPanel">
              <Setter Property="Visibility" Value="Collapsed" />
              <Style.Triggers>
                <DataTrigger Binding="{Binding State}"
                             Value="{x:Static vm:WorkspaceState.Running}">
                  <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
              </Style.Triggers>
            </Style>
          </StackPanel.Style>
          <TextBlock Text="{Binding CurrentRelativePath}" TextTrimming="CharacterEllipsis" />
          <ProgressBar Minimum="0" Maximum="100"
                       Value="{Binding ProgressPercent}" Margin="0,8,0,4" />
          <TextBlock Text="{Binding SummaryMessage}" />
          <Button Content="{DynamicResource SafeStopButton}"
                  Command="{Binding SafeStopCommand}"
                  HorizontalAlignment="Left" Margin="0,12,0,0" />
        </StackPanel>

        <StackPanel Margin="0,16,0,0">
          <StackPanel.Style>
            <Style TargetType="StackPanel">
              <Setter Property="Visibility" Value="Collapsed" />
              <Style.Triggers>
                <DataTrigger Binding="{Binding State}"
                             Value="{x:Static vm:WorkspaceState.Completed}">
                  <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
              </Style.Triggers>
            </Style>
          </StackPanel.Style>
          <TextBlock Text="{Binding SummaryMessage}"
                     FontWeight="SemiBold" TextWrapping="Wrap" />
          <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
            <Button Content="{DynamicResource OpenOutputButton}"
                    Command="{Binding OpenOutputCommand}" />
            <Button Content="{DynamicResource OpenLogButton}"
                    Command="{Binding OpenLogCommand}" Margin="8,0,0,0" />
            <Button Content="{DynamicResource ResetButton}"
                    Command="{Binding ResetCommand}" Margin="8,0,0,0" />
          </StackPanel>
        </StackPanel>
      </StackPanel>
    </Grid>
  </ScrollViewer>
</Grid>
```

Declare `xmlns:vm="clr-namespace:Emke.AiMarker.App.ViewModels"` on the
`Window`. The drop target is the Empty/Ready selection surface; commands
disable it while Running. The Ready, Running, and Completed sections above
use data triggers on `State` and are mutually exclusive. Keep the file detail
list inside the bound `Expander`.

- [ ] **Step 6: Implement drag/drop and native selection**

Code-behind may only translate WPF drag events into paths and call `AddPathsAsync`; it must not scan or process media.

Use:

```csharp
var fileDialog = new Microsoft.Win32.OpenFileDialog
{
    Multiselect = true,
    Filter = "支持的媒体|*.jpg;*.jpeg;*.png;*.mp4",
};

var folderDialog = new Microsoft.Win32.OpenFolderDialog
{
    Multiselect = true,
};
```

Bind keyboard focus styles to `BrandAccentBrush`, give every interactive control an accessible name, and preserve visible focus.

- [ ] **Step 7: Run tests, build, visually inspect, and commit**

```powershell
dotnet test Emke.AiMarker.sln
dotnet build src/Emke.AiMarker.App/Emke.AiMarker.App.csproj -c Debug
git add assets/branding src/Emke.AiMarker.App tests/Emke.AiMarker.App.Tests
git commit -m "feat: apply EMKE compact desktop design"
```

On Windows, inspect Empty and Ready states at 100%, 150%, and 200% display scaling. Reject cropped Logo, clipped Chinese text, missing focus rings, or horizontal scrolling at the minimum window size.

### Task 9: Add advanced original mode, confirmations, single instance, and self-test

**Files:**
- Create: `src/Emke.AiMarker.App/ViewModels/SettingsViewModel.cs`
- Create: `src/Emke.AiMarker.App/Views/SettingsDialog.xaml`
- Create: `src/Emke.AiMarker.App/Views/SettingsDialog.xaml.cs`
- Create: `src/Emke.AiMarker.App/Views/ConfirmationDialog.xaml`
- Create: `src/Emke.AiMarker.App/Views/ConfirmationDialog.xaml.cs`
- Create: `src/Emke.AiMarker.App/Services/UserPromptService.cs`
- Create: `src/Emke.AiMarker.Infrastructure/Windows/SingleInstanceGuard.cs`
- Create: `src/Emke.AiMarker.App/Services/SelfTestService.cs`
- Modify: `src/Emke.AiMarker.App/App.xaml`
- Modify: `src/Emke.AiMarker.App/App.xaml.cs`
- Modify: `src/Emke.AiMarker.App/MainWindow.xaml.cs`
- Test: `tests/Emke.AiMarker.App.Tests/ViewModels/AdvancedModeTests.cs`
- Test: `tests/Emke.AiMarker.Infrastructure.Tests/Windows/SingleInstanceGuardTests.cs`

**Interfaces:**
- Consumes: `RunMode.MarkOriginals`, `StopController`, ExifTool runtime validation.
- Produces: per-run advanced setting, exact confirmation contract, named mutex, safe close behavior, and `--self-test`.

- [ ] **Step 1: Write failing advanced confirmation tests**

```csharp
[Fact]
public async Task Original_mode_requires_confirmation_every_run()
{
    var harness = MainWindowHarness.ReadyWithMedia();
    harness.ViewModel.IsOverwriteOriginals = true;
    harness.Prompts.NextOriginalWriteConfirmation = false;

    await harness.ViewModel.StartMarkAsync();

    Assert.Equal(1, harness.Prompts.OriginalWriteConfirmationCount);
    Assert.False(harness.Batch.WasStarted);
}

[Fact]
public async Task Completed_run_resets_original_mode()
{
    var harness = MainWindowHarness.ReadyWithMedia();
    harness.ViewModel.IsOverwriteOriginals = true;
    harness.Prompts.NextOriginalWriteConfirmation = true;

    await harness.ViewModel.StartMarkAsync();

    Assert.False(harness.ViewModel.IsOverwriteOriginals);
}
```

- [ ] **Step 2: Implement the settings and confirmation dialogs**

The advanced switch is in `SettingsDialog` under an “高级” heading. It is not persisted. When enabled, the main safety panel uses `DangerBrush`.

The confirmation dialog must show:

```text
即将直接修改 {count} 个原始媒体文件。
本次不会创建备份，文件容器和校验值会改变。
请确认这些媒体已经人工判断需要 contains-synthetic-performer。
```

The affirmative button text is `确认修改原件`; the default focused button is `取消`.

- [ ] **Step 3: Implement single instance**

Use a named mutex:

```csharp
const string MutexName = @"Local\EMKE.AIMarker.2.x";
```

`SingleInstanceGuard.TryAcquire()` returns false when the mutex already exists. The second process shows `EMKE AI Marker 已在运行` and exits `0`.

- [ ] **Step 4: Implement safe close behavior**

When state is Running, window close must show:

```text
任务正在进行。选择“安全停止”后，应用会完成当前文件并停止后续文件。
```

Buttons are `继续处理` and `安全停止并等待`. Do not kill the current ExifTool process.

- [ ] **Step 5: Implement headless self-test**

`App.OnStartup` must detect:

```text
--self-test --report <absolute-path>
```

`SelfTestService` validates:

- assembly version `2.0.0.0`;
- ExifTool manifest and executable;
- ExifTool version `13.59`;
- required Logo resources;
- output report directory is writable.

Write UTF-8 lines:

```text
AppVersion=2.0.0
Runtime=.NET 10
ExifTool=13.59
Result=ok
```

Return exit code `0` only for `Result=ok`; otherwise return `1` and include the exception type and message.

- [ ] **Step 6: Run tests and commit**

```powershell
dotnet test Emke.AiMarker.sln
git add src/Emke.AiMarker.App src/Emke.AiMarker.Infrastructure tests
git commit -m "feat: add guarded original mode and startup safety"
```

### Task 10: Add controlled media fixtures and real ExifTool integration tests

**Files:**
- Create: `tools/generate-controlled-fixtures.ps1`
- Create: `tests/fixtures/controlled/fixture.jpg`
- Create: `tests/fixtures/controlled/fixture.jpeg`
- Create: `tests/fixtures/controlled/fixture.png`
- Create: `tests/fixtures/controlled/fixture.mp4`
- Create: `tests/fixtures/controlled/fixture-manifest.json`
- Create: `tests/Emke.AiMarker.Integration.Tests/Emke.AiMarker.Integration.Tests.csproj`
- Create: `tests/Emke.AiMarker.Integration.Tests/CopyModeIntegrationTests.cs`
- Create: `tests/Emke.AiMarker.Integration.Tests/FixtureManifestTests.cs`
- Create: `tests/Emke.AiMarker.Integration.Tests/TestSupport/IntegrationTestSupport.cs`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: `MediaProcessor`, real `ExifToolClient`, physical transaction, and runtime path from `EMKE_EXIFTOOL`.
- Produces: controlled non-private fixtures and end-to-end proof for all four extensions.

- [ ] **Step 1: Create a deterministic fixture generator**

The script must require FFmpeg `7.1.1` and fail for another version:

```powershell
$version = (& ffmpeg -version | Select-Object -First 1)
if ($version -notmatch '^ffmpeg version 7\.1\.1\b') {
    throw "FFmpeg 7.1.1 is required to regenerate controlled fixtures."
}
```

It must also require `EMKE_EXIFTOOL` to point at locked ExifTool `13.59`.

Generate:

```powershell
ffmpeg -hide_banner -loglevel error -f lavfi -i color=c=white:s=16x16:d=1 `
  -frames:v 1 -y tests/fixtures/controlled/fixture.png
ffmpeg -hide_banner -loglevel error -f lavfi -i color=c=white:s=16x16:d=1 `
  -frames:v 1 -q:v 2 -y tests/fixtures/controlled/fixture.jpg
Copy-Item tests/fixtures/controlled/fixture.jpg tests/fixtures/controlled/fixture.jpeg
ffmpeg -hide_banner -loglevel error -f lavfi -i color=c=black:s=16x16:d=1 `
  -an -c:v libx264 -pix_fmt yuv420p -movflags +faststart `
  -metadata title=EMKE-Controlled-Test-Fixture `
  -y tests/fixtures/controlled/fixture.mp4
& $env:EMKE_EXIFTOOL -overwrite_original `
  -XMP-dc:Subject=emke-existing-fixture-subject `
  tests/fixtures/controlled/fixture.jpg `
  tests/fixtures/controlled/fixture.jpeg `
  tests/fixtures/controlled/fixture.png `
  tests/fixtures/controlled/fixture.mp4
```

The script writes a JSON manifest containing each relative filename, byte length, SHA-256, generator version, and the exact generation command. No fixture may contain people, products, customer data, or external media.

- [ ] **Step 2: Narrowly allow only controlled fixtures in `.gitignore`**

Add after the media privacy guard:

```gitignore
!/tests/fixtures/
!/tests/fixtures/controlled/
!/tests/fixtures/controlled/fixture.jpg
!/tests/fixtures/controlled/fixture.jpeg
!/tests/fixtures/controlled/fixture.png
!/tests/fixtures/controlled/fixture.mp4
!/tests/fixtures/controlled/fixture-manifest.json
```

Do not add a broad exception for arbitrary media.

- [ ] **Step 3: Write failing manifest and copy-mode integration tests**

```csharp
[Theory]
[InlineData("fixture.jpg")]
[InlineData("fixture.jpeg")]
[InlineData("fixture.png")]
[InlineData("fixture.mp4")]
public async Task Copy_mode_marks_output_and_preserves_source_bytes(string name)
{
    string source = FixtureCopy.CreatePrivateWorkingCopy(name);
    string sourceHash = Hashing.Sha256(source);
    var services = IntegrationServices.Create(
        Environment.GetEnvironmentVariable("EMKE_EXIFTOOL")
        ?? throw new InvalidOperationException("EMKE_EXIFTOOL is required."));

    ProcessResult result = await services.Processor.ProcessAsync(
        IntegrationPlans.For(source),
        RunMode.MarkCopies,
        CancellationToken.None);

    Assert.Equal(ProcessStatus.Added, result.Status);
    Assert.Equal(sourceHash, Hashing.Sha256(source));
    Assert.Equal(VerificationResult.Passed, result.Evidence.Result);
    Assert.True(File.Exists(result.OutputPath));
    Assert.Contains(
        "emke-existing-fixture-subject",
        await services.ExifTool.ReadSubjectsAsync(
            result.OutputPath,
            CancellationToken.None));
}
```

`FixtureManifestTests` recalculates length and SHA-256 for every manifest entry and rejects any unlisted file in the controlled directory.

`IntegrationTestSupport.cs` defines `FixtureCopy`, `Hashing`,
`IntegrationPlans`, `IntegrationHarness`, and `IntegrationServices`. The
service factory returns the processor and its ExifTool client, and wires
`PhysicalCopyTransaction`, `ExifToolClient`, `WindowsFileSafety`, and
`MediaProcessor` exactly as the App composition root does.

Add a second integration test that processes an output path again, expects
`OutputAlreadyCompliant`, and asserts the exact marker appears once while
`emke-existing-fixture-subject` is still present.

- [ ] **Step 4: Add media-stream preservation checks**

For each controlled JPG, JPEG, PNG, and MP4 fixture, call
`IExifToolClient.ReadImageDataHashAsync` before and after processing. That
method invokes:

```text
-q
-q
-api
RequestAll=3
-s3
-ImageDataHash
<absolute media path>
```

Assert both values are non-empty and identical. A blank value is an
integration-test failure; do not fall back to a weaker assertion. FFmpeg is
used only to regenerate fixtures and is not added to product runtime or CI.

- [ ] **Step 5: Fetch ExifTool, run integration tests, and commit**

```powershell
py -3 scripts/fetch_exiftool.py
$env:EMKE_EXIFTOOL = (Resolve-Path runtime/exiftool/exiftool.exe)
pwsh tools/generate-controlled-fixtures.ps1
dotnet test tests/Emke.AiMarker.Integration.Tests/Emke.AiMarker.Integration.Tests.csproj
git add .gitignore tools/generate-controlled-fixtures.ps1 tests/fixtures tests/Emke.AiMarker.Integration.Tests Emke.AiMarker.sln
git commit -m "test: add controlled four-format integration coverage"
```

The Python fetch command is a bootstrap-only use of the repository's existing
locked downloader before Task 11 replaces it. It is never packaged or invoked
by the v2 application.

### Task 11: Replace Python packaging with a tested .NET release tool

**Files:**
- Create: `tools/Emke.AiMarker.Release/Emke.AiMarker.Release.csproj`
- Create: `tools/Emke.AiMarker.Release/Program.cs`
- Create: `tools/Emke.AiMarker.Release/Commands/FetchExifToolCommand.cs`
- Create: `tools/Emke.AiMarker.Release/Commands/PackageCommand.cs`
- Create: `tools/Emke.AiMarker.Release/Packaging/ReleaseStageValidator.cs`
- Create: `tools/Emke.AiMarker.Release/Packaging/DeterministicZipWriter.cs`
- Create: `packaging/release-manifest.json`
- Create: `scripts/fetch-exiftool.ps1`
- Create: `scripts/build-release.ps1`
- Create: `tests/Emke.AiMarker.Release.Tests/Emke.AiMarker.Release.Tests.csproj`
- Create: `tests/Emke.AiMarker.Release.Tests/ReleaseStageValidatorTests.cs`
- Create: `tests/Emke.AiMarker.Release.Tests/DeterministicZipWriterTests.cs`

**Interfaces:**
- Consumes: `packaging/exiftool.lock.json`, published App output, licenses, release template.
- Produces: `fetch-exiftool` and `package` commands, a validated deterministic ZIP, `SHA256SUMS.txt`, and release tests.

- [ ] **Step 1: Create the release tool and failing hygiene tests**

```powershell
dotnet new console -n Emke.AiMarker.Release -o tools/Emke.AiMarker.Release -f net10.0
dotnet new xunit -n Emke.AiMarker.Release.Tests -o tests/Emke.AiMarker.Release.Tests -f net10.0
dotnet add tests/Emke.AiMarker.Release.Tests/Emke.AiMarker.Release.Tests.csproj reference tools/Emke.AiMarker.Release/Emke.AiMarker.Release.csproj
dotnet sln Emke.AiMarker.sln add tools/Emke.AiMarker.Release/Emke.AiMarker.Release.csproj tests/Emke.AiMarker.Release.Tests/Emke.AiMarker.Release.Tests.csproj
```

Write tests that reject:

```text
private.jpg
private.JPEG
private.png
private.MP4
验证结果.csv
media.mp4_original
app.py
app.pyc
__pycache__
.gitkeep
unexpected top-level documentation
absolute paths in text files
```

and require:

```text
EMKE AI Marker.exe
使用说明.txt
LICENSE.txt
THIRD_PARTY_NOTICES.txt
exiftool/exiftool.exe
exiftool/exiftool-manifest.json
licenses/dotnet/LICENSE.txt
licenses/dotnet/ThirdPartyNotices.txt
示例输出/EMKE 已标记/
```

- [ ] **Step 2: Implement locked ExifTool acquisition**

Port the current safety behavior into `FetchExifToolCommand`:

- HTTPS download from the exact lock URL;
- exact archive byte length and SHA-256;
- ZIP signature check;
- reject absolute paths, `..`, drive prefixes, symlinks, and reparse points;
- require exactly one `exiftool(-k).exe`;
- rename it to `exiftool.exe`;
- require `exiftool_files` and `README.txt`;
- verify version `13.59`;
- write schema-1 per-file manifest;
- atomically replace the runtime directory;
- `--force` is required to replace nonempty invalid runtime content.

`scripts/fetch-exiftool.ps1` is only:

```powershell
$ErrorActionPreference = 'Stop'
dotnet run --project tools/Emke.AiMarker.Release -- fetch-exiftool @args
```

- [ ] **Step 3: Implement stage validation and deterministic ZIP**

`packaging/release-manifest.json` contains:

```json
{
  "schema_version": 1,
  "product": "EMKE AI Marker",
  "version": "2.0.0",
  "platform": "windows-x64",
  "required_paths": [
    "EMKE AI Marker.exe",
    "使用说明.txt",
    "LICENSE.txt",
    "THIRD_PARTY_NOTICES.txt",
    "exiftool/exiftool.exe",
    "exiftool/exiftool-manifest.json",
    "licenses/dotnet/LICENSE.txt",
    "licenses/dotnet/ThirdPartyNotices.txt",
    "示例输出/EMKE 已标记/"
  ]
}
```

`DeterministicZipWriter` sorts paths ordinally, normalizes separators to `/`, and sets every entry timestamp from `SOURCE_DATE_EPOCH` or `1700000000` when unset. The archive has one ASCII root:

```text
emke-ai-marker-v2.0.0-windows-x64/
```

- [ ] **Step 4: Implement the release build script**

`scripts/build-release.ps1` must:

```powershell
$ErrorActionPreference = 'Stop'
dotnet restore Emke.AiMarker.sln --locked-mode
dotnet test Emke.AiMarker.sln -c Release --no-restore
pwsh scripts/fetch-exiftool.ps1
dotnet publish src/Emke.AiMarker.App/Emke.AiMarker.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -o build/publish/win-x64 --no-restore
dotnet run --project tools/Emke.AiMarker.Release -c Release --no-build -- `
  package --publish-dir build/publish/win-x64 --output-dir dist
```

The package command runs:

```powershell
& "build\stage\emke-ai-marker-v2.0.0-windows-x64\EMKE AI Marker.exe" `
  --self-test --report "build\self-test.txt"
```

and requires `Result=ok` before writing the ZIP.

- [ ] **Step 5: Run release tests and a local Windows package build**

```powershell
dotnet test tests/Emke.AiMarker.Release.Tests/Emke.AiMarker.Release.Tests.csproj
pwsh scripts/build-release.ps1
Get-ChildItem dist
Get-Content dist/SHA256SUMS.txt
```

Expected: one ZIP named `emke-ai-marker-v2.0.0-windows-x64.zip`, one checksum file, no forbidden file in the ZIP, and identical ZIP bytes on two builds with the same `SOURCE_DATE_EPOCH`.

- [ ] **Step 6: Commit**

```bash
git add tools/Emke.AiMarker.Release scripts packaging/release-manifest.json tests/Emke.AiMarker.Release.Tests Emke.AiMarker.sln
git commit -m "build: add deterministic .NET Windows release pipeline"
```

### Task 12: Move Python to legacy, update licenses/docs, and replace CI

**Files:**
- Move: `src/ai_media_marker.py` → `legacy/python/src/ai_media_marker.py`
- Move: `tests/test_ai_media_marker.py` → `legacy/python/tests/test_ai_media_marker.py`
- Move: `tests/test_fetch_exiftool.py` → `legacy/python/tests/test_fetch_exiftool.py`
- Move: `tests/test_release_hygiene.py` → `legacy/python/tests/test_release_hygiene.py`
- Move: `scripts/fetch_exiftool.py` → `legacy/python/scripts/fetch_exiftool.py`
- Move: `scripts/build_release.py` → `legacy/python/scripts/build_release.py`
- Move: `packaging/marker_app.spec` → `legacy/python/packaging/marker_app.spec`
- Move: `packaging/licenses/Tcl-8.6-license.terms` → `legacy/python/packaging/licenses/Tcl-8.6-license.terms`
- Move: `packaging/licenses/Tk-8.6-license.terms` → `legacy/python/packaging/licenses/Tk-8.6-license.terms`
- Move: `pyproject.toml` → `legacy/python/pyproject.toml`
- Move: `requirements-build.lock` → `legacy/python/requirements-build.lock`
- Move: `开发运行.cmd` → `legacy/python/开发运行.cmd`
- Create: `legacy/python/README.md`
- Modify: `README.md`
- Modify: `BUILDING.md`
- Modify: `AGENTS.md`
- Modify: `CONTRIBUTING.md`
- Modify: `THIRD_PARTY_NOTICES.md`
- Modify: `release_template/使用说明.txt`
- Create: `packaging/licenses/dotnet/LICENSE.txt`
- Create: `packaging/licenses/dotnet/ThirdPartyNotices.txt`
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: all completed C# projects, release tool, and version `2.0.0`.
- Produces: an unambiguous .NET repository root, preserved legacy source, current licenses/docs, .NET CI, and tag release behavior.

- [ ] **Step 1: Move legacy files without deleting history**

Use `git mv` for every path listed above. `legacy/python/README.md` must state:

```text
This directory preserves the v1.0.0 Python/Tkinter implementation for
one major release cycle as a behavior reference. It is not built,
packaged, or shipped by the EMKE AI Marker v2 product.
```

Do not move `packaging/exiftool.lock.json`, `LICENSE`, or the new release template.

- [ ] **Step 2: Add official .NET runtime license files**

On the Windows .NET 10 build machine:

```powershell
New-Item -ItemType Directory -Force packaging/licenses/dotnet | Out-Null
Copy-Item "$env:ProgramFiles\dotnet\LICENSE.txt" packaging/licenses/dotnet/LICENSE.txt
Copy-Item "$env:ProgramFiles\dotnet\ThirdPartyNotices.txt" packaging/licenses/dotnet/ThirdPartyNotices.txt
```

Update `THIRD_PARTY_NOTICES.md` so the production package lists .NET and ExifTool. Keep Python, Tcl/Tk, and PyInstaller notices under a clearly labeled legacy-source section and exclude those legacy license files from the v2 ZIP.

- [ ] **Step 3: Rewrite user and maintainer documentation**

`README.md` must cover:

- offline purpose and exact compliance boundary;
- safe-copy default and advanced original mode;
- drag/drop and supported formats;
- strict verification and CSV evidence;
- unsigned SmartScreen warning;
- Windows x64 source/build commands;
- no Amazon affiliation or legal guarantee.

`BUILDING.md` must use only .NET 10, PowerShell, the locked ExifTool command, `dotnet test`, and `scripts/build-release.ps1`. `AGENTS.md` must make the C# projects the production truth and retain the same privacy/proof boundaries. `CONTRIBUTING.md` must prohibit private media and require controlled fixtures.

- [ ] **Step 4: Replace CI with locked .NET restore and tests**

Use:

```yaml
jobs:
  test:
    name: Windows / .NET 10
    runs-on: windows-2022
    timeout-minutes: 20
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1
        with:
          persist-credentials: false
      - uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1
        with:
          dotnet-version: 10.0.x
          cache: true
          cache-dependency-path: '**/packages.lock.json'
      - run: dotnet restore Emke.AiMarker.sln --locked-mode
      - run: dotnet test Emke.AiMarker.sln -c Release --no-restore
      - run: pwsh scripts/fetch-exiftool.ps1
      - run: dotnet test tests/Emke.AiMarker.Integration.Tests/Emke.AiMarker.Integration.Tests.csproj -c Release --no-restore
        env:
          EMKE_EXIFTOOL: ${{ github.workspace }}\runtime\exiftool\exiftool.exe
```

Keep all action references pinned to full commit SHAs.

- [ ] **Step 5: Replace release workflow**

The release workflow must:

- run on `workflow_dispatch` and `v*` tags;
- set up .NET 10;
- verify tag equals `v2.0.0` from `Directory.Build.props`;
- run `scripts/build-release.ps1`;
- upload ZIP and SHA-256 with the already pinned `actions/upload-artifact` SHA;
- publish only for a tag using the pinned `actions/download-artifact` SHA and `gh release create`;
- never move or reuse `v1.0.0`.

- [ ] **Step 6: Run repository-wide checks and commit**

```powershell
dotnet restore Emke.AiMarker.sln --locked-mode
dotnet test Emke.AiMarker.sln -c Release --no-restore
pwsh scripts/build-release.ps1
git diff --check
git status --short
git add -A -- src tests tools scripts packaging legacy .github README.md BUILDING.md AGENTS.md CONTRIBUTING.md THIRD_PARTY_NOTICES.md release_template pyproject.toml requirements-build.lock 开发运行.cmd Emke.AiMarker.sln
git diff --cached --stat
git commit -m "chore: promote EMKE native app and archive Python v1"
```

Before committing, inspect staged files and prove no private media, CSV, runtime binary, build output, or local absolute path is staged. The design document may retain the user-provided Logo source path as design provenance; production docs and package content must not contain it.

### Task 13: Complete Windows 11 x64 real-machine acceptance

**Files:**
- Create: `docs/validation/windows-11-x64-smoke.md`
- Create: `docs/validation/windows-11-x64-smoke-result.md`
- Modify: `README.md` only if acceptance discovers a user-facing limitation

**Interfaces:**
- Consumes: verified release ZIP, controlled fixtures, SHA-256, and spec acceptance criteria.
- Produces: explicit real-machine evidence without conflating it with CI or package evidence.

- [ ] **Step 1: Write the immutable smoke checklist**

The checklist must record:

```text
Windows edition/build:
Architecture:
Display scaling:
ZIP filename:
ZIP SHA-256:
App file version:
ExifTool version:
SmartScreen behavior:
```

and require:

1. extract the entire ZIP;
2. run `Get-FileHash` and compare with `SHA256SUMS.txt`;
3. launch `EMKE AI Marker.exe`;
4. verify the real Logo, `#36A39E`, Chinese layout, and visible focus;
5. drag one controlled JPG, JPEG, PNG, and MP4;
6. run default copy mode;
7. prove every source hash is unchanged;
8. run read-only verification on outputs;
9. inspect the CSV fields and ExifTool version;
10. test target conflict;
11. test safe stop with a multi-file batch;
12. enable original mode, verify the second confirmation, then cancel;
13. close and relaunch to prove original mode reset;
14. run at 100%, 150%, and 200% scaling.

- [ ] **Step 2: Run the headless self-test**

```powershell
& ".\EMKE AI Marker.exe" --self-test --report ".\self-test.txt"
$LASTEXITCODE
Get-Content .\self-test.txt
```

Expected exit code: `0`. Expected final line: `Result=ok`.

- [ ] **Step 3: Execute the GUI/media checklist and record exact evidence**

For each step, record `pass`, `fail`, or `blocked`, with the observed app text and sanitized filenames. Do not convert a blocked or skipped check into a pass.

- [ ] **Step 4: Re-run automated and package checks after any acceptance fix**

```powershell
dotnet test Emke.AiMarker.sln -c Release
pwsh scripts/build-release.ps1
```

Generate a new ZIP and SHA-256 after every code or package change. Do not reuse an earlier acceptance result for changed bytes.

- [ ] **Step 5: Commit the acceptance record**

```bash
git add docs/validation/windows-11-x64-smoke.md docs/validation/windows-11-x64-smoke-result.md
git diff --quiet -- README.md || git add README.md
git commit -m "test: record Windows 11 x64 release acceptance"
```

The result document must end with one of:

```text
Final result: passed
Final result: failed
Final result: blocked
```

Only `passed` satisfies the design acceptance gate.

## Plan Self-Review Coverage Map

- Product/version/platform/network constraints: Tasks 1, 7, 9, 11, 12.
- Exact marker and XMP structure: Tasks 1, 2, 4, 5, 10.
- Safe-copy default and unchanged sources: Tasks 3, 5, 10, 13.
- Advanced original mode and second confirmation: Tasks 5, 7, 9, 13.
- Recursive scanning, stable ordering, formats, and reparse rejection: Task 3.
- Target conflicts, temp ownership, atomic commit, and recovery: Tasks 3, 5, 11.
- Batch failure isolation, safe stop, and CSV evidence: Task 6.
- Confirmed compact UI, Chinese resources, Logo, and `#36A39E`: Tasks 7 and 8.
- ExifTool integrity/version and offline execution: Tasks 4, 9, 11.
- Controlled four-format integration proof: Task 10.
- Deterministic unsigned ZIP, licenses, SmartScreen copy, and hygiene: Tasks 11 and 12.
- Legacy Python retention and removal from production runtime: Task 12.
- Windows 11 real-machine evidence boundary: Task 13.
