# EMKE AI Marker 2.0.1 Windows Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the WPF startup crash and produce verified EMKE AI Marker 2.0.1 Windows x64 portable ZIP and per-user Inno Setup installer artifacts.

**Architecture:** Keep `Directory.Build.props` as the version source and keep the existing validated release stage as the only payload source. Add a real WPF UI self-test to the packaged application, then build both the deterministic ZIP and an Inno Setup installer from that stage and verify a temporary install/run/uninstall cycle before writing final checksums.

**Tech Stack:** .NET SDK 10.0.100, C# 14, WPF, xUnit v3, PowerShell 7, ExifTool 13.59, Inno Setup 6.7.3, GitHub Actions `windows-2022`.

## Global Constraints

- Production platform remains Windows x64.
- .NET SDK must resolve to exactly `10.0.100`; `global.json` roll-forward remains disabled.
- NuGet restore uses committed `packages.lock.json` files and final verification uses `--locked-mode`.
- ExifTool remains exactly `13.59` and must pass the existing archive, manifest, payload and version checks.
- Version becomes `2.0.1`; assembly and file version become `2.0.1.0`.
- Existing tag `v2.0.0` is immutable and must not be moved, deleted or reused.
- No commit, push, tag, GitHub Release or signing action is authorized beyond local implementation commits.
- Installer is per-user, uses `PrivilegesRequired=lowest`, and installs to `{localappdata}\Programs\EMKE AI Marker`.
- Installer creates a Start Menu shortcut, offers an unchecked desktop shortcut task, and creates no file association or startup entry.
- ZIP and Setup consume only `build/stage/emke-ai-marker-v2.0.1-windows-x64`.
- Real or private media, CSV records, logs, runtime downloads and build outputs remain untracked.
- Every production change follows a failing-test-first red/green cycle.

---

### Task 1: Prepare the locked local build toolchain and baseline

**Files:**
- No repository file changes.
- Local tools root: `D:\EMEK DEV\AI Maker\.tools\`

**Interfaces:**
- Consumes: `global.json`, `packaging/exiftool.lock.json`.
- Produces: absolute paths to `dotnet.exe`, `pwsh.exe`, and Inno Setup 6.7.3 `ISCC.exe`.

- [ ] **Step 1: Install .NET SDK 10.0.100 outside the repository**

Use Microsoft's installer script to install into an owned local tool directory:

```powershell
$ToolsRoot = 'D:\EMEK DEV\AI Maker\.tools'
$DotNetRoot = Join-Path $ToolsRoot 'dotnet-10.0.100'
$InstallScript = Join-Path $ToolsRoot 'dotnet-install.ps1'
New-Item -ItemType Directory -Path $ToolsRoot -Force | Out-Null
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $InstallScript
powershell -ExecutionPolicy Bypass -File $InstallScript `
  -Version 10.0.100 -Architecture x64 -InstallDir $DotNetRoot
& (Join-Path $DotNetRoot 'dotnet.exe') --version
```

Expected: exact output `10.0.100`.

- [ ] **Step 2: Install a PowerShell 7 x64 build host outside the repository**

Use the official `Microsoft.PowerShell` winget package, then resolve its executable:

```powershell
winget install --id Microsoft.PowerShell --exact --source winget `
  --accept-package-agreements --accept-source-agreements
$Pwsh = (Get-Command pwsh -ErrorAction Stop).Source
& $Pwsh --version
```

Expected: output begins with `PowerShell 7.`.

- [ ] **Step 3: Download and verify Inno Setup 6.7.3**

```powershell
$InnoInstaller = Join-Path $ToolsRoot 'innosetup-6.7.3.exe'
Invoke-WebRequest `
  'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' `
  -OutFile $InnoInstaller
$InnoHash = (Get-FileHash $InnoInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
if ($InnoHash -cne '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732') {
  throw "Unexpected Inno Setup hash: $InnoHash"
}
$InnoSignature = Get-AuthenticodeSignature $InnoInstaller
if ($InnoSignature.Status -ne 'Valid' -or
    $InnoSignature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') {
  throw "Inno Setup installer signature is not the expected valid publisher signature."
}
Start-Process -FilePath $InnoInstaller -Wait -ArgumentList @(
  '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER'
)
$InnoCompiler = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
(Get-Item $InnoCompiler).VersionInfo.FileVersion
```

Expected: compiler file version begins with `6.7.3`.

- [ ] **Step 4: Run the clean baseline**

```powershell
$env:PATH = "$DotNetRoot;$env:PATH"
& $Pwsh -NoProfile -Command {
  dotnet restore Emke.AiMarker.sln --locked-mode
  pwsh scripts\fetch-exiftool.ps1
  $env:EMKE_EXIFTOOL = (Resolve-Path .\runtime\exiftool\exiftool.exe)
  dotnet test Emke.AiMarker.sln -c Release --no-restore
}
```

Expected: the existing full solution passes with 0 failed tests. This baseline does not launch the normal WPF window and therefore does not contradict the known startup crash.

---

### Task 2: Reproduce and fix the read-only progress binding

**Files:**
- Modify: `tests/Emke.AiMarker.App.Tests/Resources/BrandResourceTests.cs`
- Modify: `src/Emke.AiMarker.App/MainWindow.xaml:249`

**Interfaces:**
- Consumes: `MainWindowViewModel.ProgressPercent : int` getter-only property.
- Produces: a `ProgressBar.Value` binding explicitly configured as `OneWay`.

- [ ] **Step 1: Write the failing XAML regression test**

Add to `BrandResourceTests`:

```csharp
[Fact]
public void Progress_bar_uses_one_way_binding_for_read_only_progress()
{
    XDocument window = XDocument.Load(
        FromRoot("src", "Emke.AiMarker.App", "MainWindow.xaml"));
    XElement progress = Assert.Single(
        window.Descendants(Presentation + "ProgressBar"));

    Assert.Equal(
        "{Binding ProgressPercent, Mode=OneWay}",
        (string?)progress.Attribute("Value"));
}
```

- [ ] **Step 2: Run the focused test and verify RED**

```powershell
dotnet test tests\Emke.AiMarker.App.Tests\Emke.AiMarker.App.Tests.csproj `
  -c Release --no-restore `
  --filter FullyQualifiedName~Progress_bar_uses_one_way_binding_for_read_only_progress
```

Expected: FAIL because the actual value is `{Binding ProgressPercent}`.

- [ ] **Step 3: Apply the minimal production fix**

Change the binding to:

```xml
<ProgressBar Margin="0,10,0,6"
             Minimum="0"
             Maximum="100"
             Value="{Binding ProgressPercent, Mode=OneWay}" />
```

- [ ] **Step 4: Verify GREEN**

Run the focused command from Step 2, then:

```powershell
dotnet test tests\Emke.AiMarker.App.Tests\Emke.AiMarker.App.Tests.csproj `
  -c Release --no-restore
```

Expected: all App tests pass with 0 failures.

- [ ] **Step 5: Commit the binding regression**

```powershell
git add src/Emke.AiMarker.App/MainWindow.xaml `
  tests/Emke.AiMarker.App.Tests/Resources/BrandResourceTests.cs
git commit -m "fix: make progress binding one way"
```

---

### Task 3: Add a real WPF UI self-test and release gate

**Files:**
- Create: `src/Emke.AiMarker.App/Services/UiSelfTestService.cs`
- Create: `tests/Emke.AiMarker.App.Tests/Services/UiSelfTestServiceTests.cs`
- Modify: `src/Emke.AiMarker.App/App.xaml.cs`
- Modify: `tools/Emke.AiMarker.Release/Commands/PackageCommand.cs`
- Modify: `tests/Emke.AiMarker.Release.Tests/PackageCommandTests.cs`
- Modify: `tests/Emke.AiMarker.Release.Tests/TestSupport/ReleaseFixtures.cs`

**Interfaces:**
- Produces: `UiSelfTestArguments.IsRequested(IReadOnlyList<string>)`.
- Produces: `UiSelfTestArguments.TryParse(..., out string reportPath, out string error)`.
- Produces: `UiSelfTestReport.WriteSuccess(string reportPath)`.
- Produces: `UiSelfTestReport.TryWriteFailure(string reportPath, Exception exception)`.
- Package contract invokes both `--self-test --report <absolute>` and `--ui-self-test --report <absolute>`.

- [ ] **Step 1: Write failing UI self-test service tests**

Create tests that require the exact argument and report contracts:

```csharp
[Fact]
public void Ui_arguments_require_the_exact_shape_and_absolute_report()
{
    string report = Path.Combine(root, "ui-report.txt");
    Assert.True(UiSelfTestArguments.TryParse(
        ["--ui-self-test", "--report", report],
        out string parsed,
        out string error));
    Assert.Equal(Path.GetFullPath(report), parsed);
    Assert.Equal("", error);
    Assert.False(UiSelfTestArguments.TryParse(
        ["--ui-self-test", "--report", "relative.txt"],
        out _,
        out _));
}

[Fact]
public void Ui_success_report_is_exact_and_failure_is_sanitized()
{
    string success = Path.Combine(root, "success.txt");
    UiSelfTestReport.WriteSuccess(success);
    Assert.Equal(
        ["AppVersion=2.0.0", "MainWindow=shown", "Result=ok"],
        File.ReadAllLines(success));

    string failure = Path.Combine(root, "failure.txt");
    UiSelfTestReport.TryWriteFailure(
        failure,
        new InvalidOperationException("binding\r\nfailed"));
    Assert.Equal(
        ["Result=failed", "ErrorType=InvalidOperationException",
         "ErrorMessage=binding  failed"],
        File.ReadAllLines(failure));
}
```

- [ ] **Step 2: Verify the service tests fail to compile**

```powershell
dotnet test tests\Emke.AiMarker.App.Tests\Emke.AiMarker.App.Tests.csproj `
  -c Release --no-restore `
  --filter FullyQualifiedName~UiSelfTest
```

Expected: compilation failure because `UiSelfTestArguments` and `UiSelfTestReport` do not exist.

- [ ] **Step 3: Implement the argument parser and atomic report writer**

Implement `UiSelfTestService.cs` with:

```csharp
public static class UiSelfTestArguments
{
    public static bool IsRequested(IReadOnlyList<string> arguments) =>
        arguments.Contains("--ui-self-test", StringComparer.Ordinal);

    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out string reportPath,
        out string error)
    {
        reportPath = "";
        error = "";
        if (arguments.Count != 3
            || !string.Equals(arguments[0], "--ui-self-test", StringComparison.Ordinal)
            || !string.Equals(arguments[1], "--report", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(arguments[2])
            || !Path.IsPathFullyQualified(arguments[2]))
        {
            error = "Expected exactly: --ui-self-test --report <absolute-path>";
            return false;
        }

        reportPath = Path.GetFullPath(arguments[2]);
        return true;
    }
}
```

`UiSelfTestReport.WriteSuccess` atomically writes UTF-8 without BOM:

```text
AppVersion=2.0.0
MainWindow=shown
Result=ok
```

`TryWriteFailure` writes `Result`, exception type and a single-line message, and never throws.

- [ ] **Step 4: Verify the service tests pass**

Run the focused command from Step 2.

Expected: all `UiSelfTest` service tests pass.

- [ ] **Step 5: Write the failing package dual-self-test contract**

Update the first `PackageCommandTests` test to assert:

```csharp
Assert.Equal(2, process.Calls.Count);
Assert.Equal(
    ["--self-test", "--report"],
    process.Calls[0].Arguments.Take(2));
Assert.Equal(
    ["--ui-self-test", "--report"],
    process.Calls[1].Arguments.Take(2));
```

Change `RecordingPackageProcess` to expose:

```csharp
public List<PackageProcessCall> Calls { get; } = [];

internal sealed record PackageProcessCall(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);
```

The recorder writes the corresponding exact headless or UI report based on argument zero.

- [ ] **Step 6: Verify the package test is RED**

```powershell
dotnet test tests\Emke.AiMarker.Release.Tests\Emke.AiMarker.Release.Tests.csproj `
  -c Release --no-restore `
  --filter FullyQualifiedName~Builds_stage_runs_exact_self_test
```

Expected: FAIL because `PackageCommand` performs only the headless self-test.

- [ ] **Step 7: Wire UI mode into the real WPF startup path**

In `App.OnStartup`:

1. Parse `--ui-self-test` before ordinary composition.
2. Call `base.OnStartup(e)`.
3. Create the normal services, real `MainWindowViewModel`, and `MainWindow`.
4. Register `DispatcherUnhandledException` only for UI self-test mode; write a failure report, mark handled, and `Shutdown(1)`.
5. Register `window.ContentRendered` only for UI self-test mode; write the exact success report and `Shutdown(0)`.
6. Wrap `window.Show()` so synchronous construction/binding/layout exceptions write a failure report and shut down with exit code 1.
7. Preserve normal startup behavior when no UI self-test argument is present.

- [ ] **Step 8: Run and validate both package self-tests**

Update `PackageCommand` to create separate absolute report paths:

```csharp
string headlessReportPath = Path.Combine(operationRoot, "self-test.txt");
string uiReportPath = Path.Combine(operationRoot, "ui-self-test.txt");
```

Run and validate headless first, then UI. Expected UI report:

```csharp
string[] expected =
[
    "AppVersion=2.0.0",
    "MainWindow=shown",
    "Result=ok",
];
```

Any nonzero exit or report mismatch must prevent ZIP/checksum creation.

- [ ] **Step 9: Verify GREEN and run App plus Release tests**

```powershell
dotnet test tests\Emke.AiMarker.App.Tests\Emke.AiMarker.App.Tests.csproj `
  -c Release --no-restore
dotnet test tests\Emke.AiMarker.Release.Tests\Emke.AiMarker.Release.Tests.csproj `
  -c Release --no-restore
```

Expected: both projects pass with 0 failures.

- [ ] **Step 10: Commit the UI startup gate**

```powershell
git add src/Emke.AiMarker.App/App.xaml.cs `
  src/Emke.AiMarker.App/Services/UiSelfTestService.cs `
  tests/Emke.AiMarker.App.Tests/Services/UiSelfTestServiceTests.cs `
  tools/Emke.AiMarker.Release/Commands/PackageCommand.cs `
  tests/Emke.AiMarker.Release.Tests/PackageCommandTests.cs `
  tests/Emke.AiMarker.Release.Tests/TestSupport/ReleaseFixtures.cs
git commit -m "test: gate releases on real WPF startup"
```

---

### Task 4: Promote the product contract to version 2.0.1

**Files:**
- Modify: `Directory.Build.props`
- Modify: `packaging/release-manifest.json`
- Modify: `src/Emke.AiMarker.App/Services/SelfTestService.cs`
- Modify: `src/Emke.AiMarker.App/Services/UiSelfTestService.cs`
- Modify: `tools/Emke.AiMarker.Release/Commands/PackageCommand.cs`
- Modify: `tools/Emke.AiMarker.Release/Packaging/ReleaseStageValidator.cs`
- Modify: `.github/workflows/release.yml`
- Modify: all v2 production tests and fixtures found by the scoped `rg` command below
- Refresh: committed `packages.lock.json` files whose project-reference minimum versions change

**Interfaces:**
- Consumes: `Directory.Build.props` product version.
- Produces: ZIP root/name `emke-ai-marker-v2.0.1-windows-x64`.
- Produces: exact self-test `AppVersion=2.0.1`.

- [ ] **Step 1: Change version expectations in tests first**

Update production contract tests from:

```text
2.0.0
2.0.0.0
emke-ai-marker-v2.0.0-windows-x64
```

to:

```text
2.0.1
2.0.1.0
emke-ai-marker-v2.0.1-windows-x64
```

Do not rewrite historical `v2.0.0` release facts in `README.md` or the blocked 2026-07-27 result.

- [ ] **Step 2: Verify version tests are RED**

```powershell
dotnet test tests\Emke.AiMarker.App.Tests\Emke.AiMarker.App.Tests.csproj `
  -c Release --no-restore
dotnet test tests\Emke.AiMarker.Release.Tests\Emke.AiMarker.Release.Tests.csproj `
  -c Release --no-restore
```

Expected: failures report actual production version/package names as 2.0.0.

- [ ] **Step 3: Update production version sources**

Apply:

```xml
<Version>2.0.1</Version>
<AssemblyVersion>2.0.1.0</AssemblyVersion>
<FileVersion>2.0.1.0</FileVersion>
```

Update `release-manifest.json`, self-test reports, release validator and `PackageCommand.RootName` to 2.0.1.

- [ ] **Step 4: Update workflow version and artifact paths**

The release workflow must require exact `2.0.1`, upload:

```text
dist/emke-ai-marker-v2.0.1-windows-x64.zip
dist/emke-ai-marker-v2.0.1-windows-x64-setup.exe
dist/SHA256SUMS.txt
```

and pass all three files to `gh release create`.

- [ ] **Step 5: Refresh project-reference lock metadata**

```powershell
dotnet restore Emke.AiMarker.sln --force-evaluate
dotnet restore Emke.AiMarker.sln --locked-mode
```

Expected: only local project-reference version constraints change; third-party package identities and hashes remain unchanged.

- [ ] **Step 6: Scan for stale production version literals**

```powershell
rg -n "2\.0\.0|v2\.0\.0|emke-ai-marker-v2\.0\.0" `
  -g '!legacy/**' `
  -g '!docs/superpowers/plans/2026-07-27-emke-ai-marker-windows.md' `
  -g '!docs/superpowers/specs/2026-07-27-emke-ai-marker-windows-design.md' `
  -g '!docs/superpowers/specs/2026-07-29-emke-ai-marker-2.0.1-installer-design.md'
```

Expected: only explicitly historical v2.0.0 release statements and immutable-tag protections remain.

- [ ] **Step 7: Verify GREEN and commit**

```powershell
dotnet test Emke.AiMarker.sln -c Release --no-restore
git add Directory.Build.props packaging .github src tools tests
git commit -m "chore: promote Windows release to 2.0.1"
```

Expected: full solution has 0 failures.

---

### Task 5: Define and test the per-user Inno Setup installer

**Files:**
- Create: `packaging/inno-setup.lock.json`
- Create: `packaging/installer/Emke.AiMarker.iss`
- Create: `tests/Emke.AiMarker.Release.Tests/InstallerContractTests.cs`

**Interfaces:**
- Consumes preprocessor defines: `StageDir`, `AppVersion`, `OutputDir`.
- Produces: `emke-ai-marker-v2.0.1-windows-x64-setup.exe`.
- Stable installer AppId: `{9F630913-5706-4142-A1A4-C35B171938C8}`.

- [ ] **Step 1: Write failing installer contract tests**

The test must parse the `.iss` text and require:

```csharp
Assert.Contains("AppId={{9F630913-5706-4142-A1A4-C35B171938C8}", script);
Assert.Contains("PrivilegesRequired=lowest", script);
Assert.Contains(
    @"DefaultDirName={localappdata}\Programs\EMKE AI Marker",
    script);
Assert.Contains("ArchitecturesAllowed=x64compatible", script);
Assert.Contains(@"Source: ""{#StageDir}\*""", script);
Assert.Contains(@"Name: ""{group}\EMKE AI Marker""", script);
Assert.Contains(@"Name: ""{autodesktop}\EMKE AI Marker""", script);
Assert.Contains("Flags: unchecked", script);
Assert.DoesNotContain("[Registry]", script, StringComparison.Ordinal);
Assert.DoesNotContain("{autostartup}", script, StringComparison.Ordinal);
```

The lock test requires exact version `6.7.3`, size `10592232`, and SHA-256
`9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732`.

- [ ] **Step 2: Verify RED**

```powershell
dotnet test tests\Emke.AiMarker.Release.Tests\Emke.AiMarker.Release.Tests.csproj `
  -c Release --no-restore `
  --filter FullyQualifiedName~InstallerContract
```

Expected: FAIL because the lock and `.iss` files do not exist.

- [ ] **Step 3: Add the exact Inno Setup lock**

```json
{
  "version": "6.7.3",
  "platform": "windows-x64-build-tool",
  "archive_name": "innosetup-6.7.3.exe",
  "url": "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe",
  "size": 10592232,
  "sha256": "9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732"
}
```

- [ ] **Step 4: Add the installer script**

Use these production directives:

```iss
#ifndef StageDir
  #error StageDir define is required
#endif
#ifndef AppVersion
  #error AppVersion define is required
#endif
#ifndef OutputDir
  #error OutputDir define is required
#endif

[Setup]
AppId={{9F630913-5706-4142-A1A4-C35B171938C8}
AppName=EMKE AI Marker
AppVersion={#AppVersion}
AppPublisher=EMKE
DefaultDirName={localappdata}\Programs\EMKE AI Marker
DefaultGroupName=EMKE AI Marker
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=emke-ai-marker-v{#AppVersion}-windows-x64-setup
SetupIconFile=..\..\src\Emke.AiMarker.App\Assets\emke-ai-marker.ico
UninstallDisplayIcon={app}\EMKE AI Marker.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\EMKE AI Marker"; Filename: "{app}\EMKE AI Marker.exe"
Name: "{autodesktop}\EMKE AI Marker"; Filename: "{app}\EMKE AI Marker.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\EMKE AI Marker.exe"; Description: "启动 EMKE AI Marker"; Flags: nowait postinstall skipifsilent
```

- [ ] **Step 5: Verify the installer contract is GREEN**

```powershell
dotnet test tests\Emke.AiMarker.Release.Tests\Emke.AiMarker.Release.Tests.csproj `
  -c Release --no-restore `
  --filter FullyQualifiedName~InstallerContract
```

Expected: all installer contract tests pass. The real compiler invocation is part of
Task 8 after `build-release.ps1` has produced the validated stage.

- [ ] **Step 6: Commit the installer definition**

```powershell
git add packaging/inno-setup.lock.json `
  packaging/installer/Emke.AiMarker.iss `
  tests/Emke.AiMarker.Release.Tests/InstallerContractTests.cs
git commit -m "feat: define per-user Windows installer"
```

---

### Task 6: Build, install-test, uninstall-test and checksum the Setup

**Files:**
- Create: `scripts/build-installer.ps1`
- Modify: `scripts/build-release.ps1`
- Modify: `tests/Emke.AiMarker.Release.Tests/BuildReleaseScriptContractTests.cs`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- `build-release.ps1 -InnoCompiler "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"`.
- `build-installer.ps1 -RepositoryRoot <root> -StageDirectory <stage> -OutputDirectory <dist> -InnoCompiler <ISCC.exe>`.
- Produces two final artifacts and a two-line `SHA256SUMS.txt`.

- [ ] **Step 1: Write failing PowerShell orchestration contract tests**

Require `build-release.ps1` to invoke the installer only after `package`:

```csharp
int package = script.IndexOf("package --repo-root", StringComparison.Ordinal);
int installer = script.IndexOf(
    "build-installer.ps1",
    StringComparison.Ordinal);
Assert.True(package >= 0);
Assert.True(installer > package);
Assert.Contains("-InnoCompiler $InnoCompiler", script);
```

Require `build-installer.ps1` to contain:

```csharp
Assert.Contains("PrivilegesRequired=lowest", installerScript);
Assert.Contains("/VERYSILENT", buildScript);
Assert.Contains("/NOICONS", buildScript);
Assert.Contains("--ui-self-test", buildScript);
Assert.Contains("unins*.exe", buildScript);
Assert.Contains("SHA256SUMS.txt", buildScript);
Assert.Contains("Assert-NoReparsePathComponents", buildScript);
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test tests\Emke.AiMarker.Release.Tests\Emke.AiMarker.Release.Tests.csproj `
  -c Release --no-restore `
  --filter FullyQualifiedName~BuildReleaseScriptContract
```

Expected: FAIL because `build-installer.ps1` and the installer invocation do not exist.

- [ ] **Step 3: Implement `build-installer.ps1` with owned paths**

The script must:

1. Resolve and validate repository, stage, output and compiler paths.
2. Verify the stage is the exact ordinary directory
   `build/stage/emke-ai-marker-v2.0.1-windows-x64`.
3. Verify `ISCC.exe` file version begins with `6.7.3`.
4. Compile into an owned candidate directory under `build/.installer-<guid>`.
5. Verify exactly one candidate Setup with the expected name and file version `2.0.1.0`.
6. Install with:

```powershell
@(
  '/VERYSILENT',
  '/SUPPRESSMSGBOXES',
  '/NORESTART',
  '/NOICONS',
  "/DIR=$installRoot"
)
```

7. Compare every stage file SHA-256 to the corresponding installed file.
8. Run installed headless and UI self-tests with reports outside the install root.
9. Resolve exactly one `unins*.exe`, run it with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.
10. Poll for at most 30 seconds until the temporary install directory is absent.
11. Move the verified Setup atomically to `dist`.
12. Recompute ZIP and Setup hashes, sort by filename, and atomically write:

```text
<zip sha256>  emke-ai-marker-v2.0.1-windows-x64.zip
<setup sha256>  emke-ai-marker-v2.0.1-windows-x64-setup.exe
```

13. In `finally`, remove only exact owned candidate, report and temporary install paths after reparse checks.

- [ ] **Step 4: Wire installer construction into `build-release.ps1`**

Add parameter:

```powershell
[string]$InnoCompiler
```

After `package` succeeds, require a fully-qualified existing compiler and call:

```powershell
& (Join-Path $PSScriptRoot 'build-installer.ps1') `
    -RepositoryRoot $repoRoot `
    -StageDirectory (Join-Path $repoRoot 'build/stage/emke-ai-marker-v2.0.1-windows-x64') `
    -OutputDirectory $outputDirectory `
    -InnoCompiler $InnoCompiler
```

- [ ] **Step 5: Update the release workflow with locked compiler acquisition**

Add a workflow step that downloads the exact official 6.7.3 asset, validates size and SHA-256 from `packaging/inno-setup.lock.json`, validates the Authenticode publisher, installs with `/CURRENTUSER`, and exports the resolved `ISCC.exe` path. Call:

```powershell
pwsh scripts/build-release.ps1 -InnoCompiler $env:INNO_COMPILER
```

Upload and publish exactly the ZIP, Setup and checksum.

- [ ] **Step 6: Verify GREEN**

```powershell
dotnet test tests\Emke.AiMarker.Release.Tests\Emke.AiMarker.Release.Tests.csproj `
  -c Release --no-restore
```

Expected: all Release tests pass with 0 failures.

- [ ] **Step 7: Commit installer orchestration**

```powershell
git add scripts/build-installer.ps1 scripts/build-release.ps1 `
  tests/Emke.AiMarker.Release.Tests/BuildReleaseScriptContractTests.cs `
  .github/workflows/release.yml
git commit -m "build: verify Windows installer delivery"
```

---

### Task 7: Update user and maintainer documentation

**Files:**
- Modify: `README.md`
- Modify: `BUILDING.md`
- Modify: `CONTRIBUTING.md`
- Modify: `AGENTS.md`
- Modify: `release_template/使用说明.txt`
- Modify: `docs/validation/windows-11-x64-smoke.md`
- Modify: relevant documentation contract tests in `tests/Emke.AiMarker.Release.Tests`

**Interfaces:**
- Documents both ZIP and Setup delivery.
- Documents exact Inno Setup 6.7.3 prerequisite and `-InnoCompiler`.
- Preserves unsigned-preview and Windows 11 acceptance boundaries.

- [ ] **Step 1: Change documentation tests first**

Require BUILDING to show:

```powershell
pwsh scripts\build-release.ps1 `
  -InnoCompiler "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
```

Require the output tree:

```text
dist/
├─ emke-ai-marker-v2.0.1-windows-x64.zip
├─ emke-ai-marker-v2.0.1-windows-x64-setup.exe
└─ SHA256SUMS.txt
```

Require user instructions to describe current-user install, optional desktop shortcut, uninstall and unsigned SmartScreen behavior.

- [ ] **Step 2: Verify documentation tests are RED**

```powershell
dotnet test tests\Emke.AiMarker.Release.Tests\Emke.AiMarker.Release.Tests.csproj `
  -c Release --no-restore `
  --filter "FullyQualifiedName~Document|FullyQualifiedName~Promotion"
```

Expected: failures identify stale ZIP-only and 2.0.0 text.

- [ ] **Step 3: Update the documentation**

Document:

- Setup is the recommended end-user path.
- ZIP remains available for portable use.
- Setup is per-user and non-elevated.
- Inno Setup is a maintainer build dependency only.
- Both package forms are unsigned.
- UI self-test is automated but Windows 11 DPI, drag-and-drop and SmartScreen acceptance remain separate.

- [ ] **Step 4: Verify GREEN and commit**

```powershell
dotnet test tests\Emke.AiMarker.Release.Tests\Emke.AiMarker.Release.Tests.csproj `
  -c Release --no-restore
git add AGENTS.md README.md BUILDING.md CONTRIBUTING.md `
  release_template docs/validation tests/Emke.AiMarker.Release.Tests
git commit -m "docs: document v2.0.1 installer delivery"
```

---

### Task 8: Execute the complete Windows release and inspect deliverables

**Files:**
- Generated, untracked: `runtime/exiftool/**`
- Generated, untracked: `build/**`
- Generated, untracked: `dist/emke-ai-marker-v2.0.1-windows-x64.zip`
- Generated, untracked: `dist/emke-ai-marker-v2.0.1-windows-x64-setup.exe`
- Generated, untracked: `dist/SHA256SUMS.txt`

**Interfaces:**
- Consumes all prior tasks and locked local tools.
- Produces final local deliverables with verification evidence.

- [ ] **Step 1: Run the full locked gate**

```powershell
$env:PATH = "$DotNetRoot;$env:PATH"
dotnet restore Emke.AiMarker.sln --locked-mode
pwsh scripts\fetch-exiftool.ps1
$env:EMKE_EXIFTOOL = (Resolve-Path .\runtime\exiftool\exiftool.exe)
dotnet test Emke.AiMarker.sln -c Release --no-restore
```

Expected: 0 failed tests and no unexpected warning promoted to an error.

- [ ] **Step 2: Build both deliverables**

```powershell
pwsh scripts\build-release.ps1 -InnoCompiler $InnoCompiler
```

Expected: ZIP, Setup and two-line checksum exist; script has already completed stage self-tests plus temporary install/run/uninstall acceptance.

- [ ] **Step 3: Independently verify checksums and package identity**

```powershell
Get-Content dist\SHA256SUMS.txt
Get-ChildItem dist -File | Select-Object Name,Length
Get-FileHash dist\emke-ai-marker-v2.0.1-windows-x64.zip -Algorithm SHA256
Get-FileHash dist\emke-ai-marker-v2.0.1-windows-x64-setup.exe -Algorithm SHA256
(Get-Item build\stage\emke-ai-marker-v2.0.1-windows-x64\'EMKE AI Marker.exe').VersionInfo |
  Select-Object FileVersion,ProductVersion
Get-AuthenticodeSignature dist\emke-ai-marker-v2.0.1-windows-x64-setup.exe |
  Select-Object Status
```

Expected: hashes match the checksum file; application version is 2.0.1; Setup status is `NotSigned`.

- [ ] **Step 4: Verify ordinary UI startup outside self-test mode**

```powershell
$Stage = Resolve-Path build\stage\emke-ai-marker-v2.0.1-windows-x64
$App = Join-Path $Stage 'EMKE AI Marker.exe'
$Process = Start-Process -FilePath $App -WorkingDirectory $Stage -PassThru
Start-Sleep -Seconds 3
$Process.Refresh()
if ($Process.HasExited -or $Process.MainWindowHandle -eq 0) {
  throw "Normal UI did not remain running with a main window."
}
$Process.CloseMainWindow() | Out-Null
$Process.WaitForExit(10000) | Out-Null
```

Expected: a main window exists and the process closes normally.

- [ ] **Step 5: Run repository hygiene checks**

```powershell
git diff --check
git status --short
git diff --stat HEAD~5..HEAD
git log -8 --oneline --decorate
```

Expected: only intended tracked source, test, script and documentation changes; runtime/build/dist remain ignored.

- [ ] **Step 6: Record final handoff**

Report:

- exact commit;
- test totals and skips;
- ZIP and Setup absolute paths, sizes and SHA-256;
- headless/UI self-test results;
- temporary install/uninstall result;
- normal UI launch result;
- unsigned/SmartScreen and Windows 11 manual-acceptance boundaries.
