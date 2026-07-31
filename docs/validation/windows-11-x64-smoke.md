# EMKE AI Marker v2 Windows 11 x64 immutable smoke checklist

This checklist accepts one exact portable ZIP on a real Windows 11 x64
computer. It is not a build guide and cannot be completed from source,
cross-publish output, CI configuration, screenshots from another platform, or
a test-fixture ZIP.

Copy this file to a new dated result record before execution. Never edit an old
result to cover different package bytes. Any code, package-content, or package
byte change requires a newly generated ZIP and SHA-256 and a complete rerun
from the first prerequisite through step 14.

Allowed item statuses: `pass`, `fail`, `blocked`.

- Use `pass` only when the stated pass condition was observed on the identified
  Windows host and exact ZIP.
- Use `fail` when the item was executed and its pass condition was not met.
- Use `blocked` when a prerequisite prevents execution. A blocked or unexecuted
  item is never a pass.
- The headless self-test and all 14 numbered items must be `pass` before the
  final result can be `passed`.
- Sanitize evidence. Do not record private device names, usernames, absolute
  home paths, or private media names.

## Required metadata

Record these values before assigning any item status. Do not infer or invent a
version, hash, or SmartScreen result.

| Field | Required value |
| --- | --- |
| Windows edition/build | `Get-ComputerInfo` edition, version, and OS build from the acceptance host |
| Architecture | Native x64 evidence from the acceptance host |
| Display scaling | Scaling active when each observation was made |
| ZIP filename | Exact product ZIP filename tested |
| ZIP SHA-256 | Lowercase SHA-256 calculated from the exact ZIP bytes |
| App file version | `VersionInfo.FileVersion` from the extracted executable |
| ExifTool version | Output from the extracted `exiftool.exe -ver` |
| SmartScreen behavior | Exact sanitized prompt/publisher observation, or explicit no-prompt observation |

Initialize a clean acceptance directory in PowerShell 7. Set paths to the
received artifacts and a separate copy of this repository's controlled
fixtures; never point these variables at private media:

```powershell
$AcceptanceRoot = (Get-Location).Path
$ZipPath = Join-Path $AcceptanceRoot "emke-ai-marker-v2.0.1-windows-x64.zip"
$ChecksumPath = Join-Path $AcceptanceRoot "SHA256SUMS.txt"
$ExtractRoot = Join-Path $AcceptanceRoot "extracted"
$PackageRoot = Join-Path $ExtractRoot "emke-ai-marker-v2.0.1-windows-x64"
$FixtureRoot = Join-Path $AcceptanceRoot "controlled-fixtures"
$AppPath = Join-Path $PackageRoot "EMKE AI Marker.exe"
$ExifToolPath = Join-Path $PackageRoot "exiftool\exiftool.exe"

$os = Get-ComputerInfo -Property WindowsProductName, WindowsVersion, OsBuildNumber, OsArchitecture
$os | Format-List
$env:PROCESSOR_ARCHITECTURE
```

This initialization block only defines paths and records host OS/architecture.
It must not read or execute anything under `$PackageRoot` before step 1
extracts it and step 2 verifies the ZIP checksum. Pass the host prerequisite
only when the host is Windows 11 and the native architecture is x64/AMD64.
Otherwise mark every dependent item `blocked`.

## Controlled source hashes

The four source files are repository-generated controlled fixtures. Copy these
exact bytes into `$FixtureRoot`; do not substitute private or public media.

| Fixture | Required SHA-256 |
| --- | --- |
| fixture.jpeg | 989f8ef247d1c70402058975312bdf719192ec81905bb05a9f8d9cb9b2267ec7 |
| fixture.jpg | 989f8ef247d1c70402058975312bdf719192ec81905bb05a9f8d9cb9b2267ec7 |
| fixture.mp4 | 1b51f16ea4e312fb66dfd10a4a2e87ca563462dcdbd11773b8c485785d2feec3 |
| fixture.png | 48b1f3408b05f8b6af30a81ad720e0a8174770ec97b3f4f457b2f975f7f4a41d |

Capture the before-state and reject unexpected fixture bytes:

```powershell
$ExpectedSourceHashes = @{
  "fixture.jpeg" = "989f8ef247d1c70402058975312bdf719192ec81905bb05a9f8d9cb9b2267ec7"
  "fixture.jpg"  = "989f8ef247d1c70402058975312bdf719192ec81905bb05a9f8d9cb9b2267ec7"
  "fixture.mp4"  = "1b51f16ea4e312fb66dfd10a4a2e87ca563462dcdbd11773b8c485785d2feec3"
  "fixture.png"  = "48b1f3408b05f8b6af30a81ad720e0a8174770ec97b3f4f457b2f975f7f4a41d"
}
$SourceFiles = Get-ChildItem -LiteralPath $FixtureRoot -File |
  Where-Object Extension -in ".jpg", ".jpeg", ".png", ".mp4" |
  Sort-Object Name
$BeforeSourceHashes = @{}
foreach ($SourceFile in $SourceFiles) {
  $Hash = (Get-FileHash -LiteralPath $SourceFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($Hash -ne $ExpectedSourceHashes[$SourceFile.Name]) {
    throw "Controlled fixture hash mismatch: $($SourceFile.Name)"
  }
  $BeforeSourceHashes[$SourceFile.Name] = $Hash
}
$BeforeSourceHashes
```

## Fourteen GUI/media steps

| ID | Acceptance item |
| --- | --- |
| 1 | Extract the entire ZIP |
| 2 | Compare the ZIP SHA-256 with `SHA256SUMS.txt` |
| 3 | Launch `EMKE AI Marker.exe` |
| 4 | Verify Logo, `#36A39E`, Chinese layout, and visible keyboard focus |
| 5 | Drag the controlled JPG, JPEG, PNG, and MP4 |
| 6 | Run default safe-copy mode |
| 7 | Prove every controlled source hash is unchanged |
| 8 | Run read-only verification on all outputs |
| 9 | Inspect exact CSV columns and ExifTool version |
| 10 | Exercise a noncompliant target conflict |
| 11 | Exercise safe stop during a multi-file batch |
| 12 | Enable advanced original mode, reach the second confirmation, and cancel |
| 13 | Close and relaunch to prove advanced original mode resets |
| 14 | Repeat visual acceptance at 100%, 150%, and 200% scaling |

### Step 1 — Extract the entire ZIP

PowerShell / action:

```powershell
if (Test-Path -LiteralPath $ExtractRoot) {
  throw "Use a new empty extraction directory; do not merge extractions."
}
Expand-Archive -LiteralPath $ZipPath -DestinationPath $ExtractRoot

$RequiredFiles = @(
  "EMKE AI Marker.exe",
  "使用说明.txt",
  "LICENSE.txt",
  "THIRD_PARTY_NOTICES.txt",
  "exiftool\exiftool.exe",
  "exiftool\exiftool-manifest.json",
  "licenses\dotnet\LICENSE.txt",
  "licenses\dotnet\ThirdPartyNotices.txt"
)
$RequiredEmptyDirectories = @(
  "示例输出\EMKE 已标记"
)

foreach ($RelativePath in $RequiredFiles) {
  $RequiredPath = Join-Path $PackageRoot $RelativePath
  if (-not (Test-Path -LiteralPath $RequiredPath -PathType Leaf)) {
    throw "Required package file is missing or not a file: $RelativePath"
  }
}
foreach ($RelativePath in $RequiredEmptyDirectories) {
  $RequiredDirectory = Join-Path $PackageRoot $RelativePath
  if (-not (Test-Path -LiteralPath $RequiredDirectory -PathType Container)) {
    throw "Required package directory is missing or not a directory: $RelativePath"
  }
  $DirectoryEntries = @(Get-ChildItem -LiteralPath $RequiredDirectory -Force)
  if ($DirectoryEntries.Count -ne 0) {
    throw "Required example-output directory is not empty: $RelativePath"
  }
}
```

Record: the sanitized ZIP filename, extraction root label, package-root name,
the eight exact required file checks, and the exact required empty-directory
check.

Pass condition: extraction completes without error into a new directory and
all eight nested release-manifest file paths are `Leaf` files; the required
`示例输出\EMKE 已标记` path is a `Container` directory and has zero entries.

### Step 2 — Compare ZIP checksum

PowerShell / action:

```powershell
$ChecksumLines = @(Get-Content -LiteralPath $ChecksumPath -Encoding utf8)
if ($ChecksumLines.Count -ne 1) {
  throw "SHA256SUMS.txt must contain exactly one line."
}
$ChecksumMatch = [regex]::Match($ChecksumLines[0], '^(?<hash>[0-9a-f]{64})  (?<filename>[^\r\n]+)$')
if (-not $ChecksumMatch.Success) {
  throw "SHA256SUMS.txt must use: <64 lowercase hex><two spaces><ZIP filename>."
}
$ExpectedZipFileName = Split-Path -Path $ZipPath -Leaf
$ChecksumZipFileName = $ChecksumMatch.Groups["filename"].Value
if ($ChecksumZipFileName -cne $ExpectedZipFileName) {
  throw "SHA256SUMS.txt names a different ZIP."
}
$ExpectedZipHash = $ChecksumMatch.Groups["hash"].Value
$ActualZipHash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$ExpectedZipFileName
$ExpectedZipHash
$ActualZipHash
if ($ActualZipHash -ne $ExpectedZipHash) {
  throw "Product ZIP SHA-256 does not match SHA256SUMS.txt."
}

$AppFileVersion = (Get-Item -LiteralPath $AppPath).VersionInfo.FileVersion
$ExifToolVersion = (& $ExifToolPath -ver).Trim()
$AppFileVersion
$ExifToolVersion
if ([string]::IsNullOrWhiteSpace($AppFileVersion)) {
  throw "Extracted app file version is empty."
}
if ($ExifToolVersion -ne "13.59") {
  throw "Extracted ExifTool version is not 13.59."
}
```

Record: the checksum line count, strict-format result, checksum filename,
actual ZIP filename, expected SHA-256, actual SHA-256, and comparison result.
Only after every gate succeeds, record `$AppFileVersion` and
`$ExifToolVersion` in the required metadata table.

Pass condition: `SHA256SUMS.txt` has exactly one line in the production format
`<64 lowercase hex><two spaces><ZIP filename>`; that filename case-sensitively
equals `Split-Path $ZipPath -Leaf`; both SHA-256 values are identical; the
extracted app has a nonempty file version; and the extracted ExifTool reports
exactly `13.59`. Format, line-count, filename, or hash mismatch must stop before
any package executable runs. A deterministic test-fixture ZIP hash is not a
product artifact identity.

### Headless self-test — after Step 2

Run only after step 1 has validated the fully extracted package structure and
step 2 has matched the ZIP checksum and recorded both package versions. Do not
run the executable from inside the ZIP:

```powershell
Set-Location -LiteralPath $PackageRoot
& ".\EMKE AI Marker.exe" --self-test --report ".\self-test.txt"
$LASTEXITCODE
Get-Content .\self-test.txt
```

Expected exit code: `0`

Expected final line: `Result=ok`

Record the exit code and the complete sanitized report. `pass` requires both
expected values and report versions matching the artifact metadata. A missing
host, missing verified ZIP, nonzero exit, missing report, or wrong final line
cannot be recorded as a pass.

### Step 3 — Launch the extracted app

PowerShell / action:

```powershell
(Get-Item -LiteralPath $AppPath).VersionInfo |
  Select-Object FileName, FileVersion, ProductVersion
Start-Process -FilePath $AppPath -WorkingDirectory $PackageRoot
```

Launch once from Explorer as well so SmartScreen behavior is directly
observed; do not disable security controls for the test.

Record: file/product versions, whether the process opened the single main
window, exact sanitized SmartScreen text, and shown publisher identity. If no
prompt appears, record `no SmartScreen prompt observed` rather than guessing
why.

Pass condition: the extracted x64 app reaches its usable main window and the
recorded SmartScreen observation describes what actually happened.

### Step 4 — Verify branded Chinese UI and focus

PowerShell / action: in the launched app, inspect the real EMKE Logo and the
primary action accent against `#36A39E`; verify the compact Chinese layout.
Use `Tab` and `Shift+Tab` through every interactive control, including opening
and closing Settings, without using the mouse.

Record: Logo rendering, accent comparison, any clipped/overlapped Chinese text,
tab order, and the visible focus cue for each primary and secondary button.

Pass condition: the Logo is the branded asset, the primary accent visibly
matches `#36A39E`, all Chinese text is readable without overlap, and every
keyboard-reachable control has a visible focus indicator.

### Step 5 — Drag all four controlled formats

PowerShell / action:

```powershell
$SourceFiles | Select-Object Name, Length, FullName
```

Select the four files in Explorer and drag them together onto the application
drop target.

Record: sanitized displayed names, displayed formats, count, stable ordering,
and any rejection message.

Pass condition: exactly one controlled `.jpg`, `.jpeg`, `.png`, and `.mp4`
appears, all four are accepted, and no other file is listed.

### Step 6 — Run default safe-copy mode

PowerShell / action: confirm Settings shows default safe-copy mode, click
“开始标记”, and wait for the batch summary. Do not enable advanced original
mode.

After completion:

```powershell
$OutputRoot = Join-Path $FixtureRoot "EMKE 已标记"
$OutputFiles = Get-ChildItem -LiteralPath $OutputRoot -File |
  Where-Object Extension -in ".jpg", ".jpeg", ".png", ".mp4" |
  Sort-Object Name
$OutputFiles | Select-Object Name, Length
Get-ChildItem -LiteralPath $FixtureRoot -Recurse -Force |
  Where-Object Name -Match "_original$|^\.emke-ai-marker-"
```

Record: selected mode, four per-file statuses, summary counts, sanitized output
location, unexpected backup/temp files, and app text.

Pass condition: all four final outputs exist under `EMKE 已标记`, each reports a
successful strict verification, no source is selected for direct modification,
and no `*_original` or owned temporary file remains.

### Step 7 — Prove source hashes stayed unchanged

PowerShell / action:

```powershell
$AfterSourceHashes = @{}
foreach ($SourceFile in $SourceFiles) {
  $Hash = (Get-FileHash -LiteralPath $SourceFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  $AfterSourceHashes[$SourceFile.Name] = $Hash
  [pscustomobject]@{
    File = $SourceFile.Name
    Before = $BeforeSourceHashes[$SourceFile.Name]
    After = $Hash
    Equal = ($Hash -eq $BeforeSourceHashes[$SourceFile.Name])
  }
}
```

Record: the before/after/equal row for every controlled source.

Pass condition: all four exact source SHA-256 values are unchanged. Output
hashes are expected to differ after metadata writes and are not a substitute
for source-hash evidence.

### Step 8 — Verify outputs read-only

PowerShell / action: before adding outputs or clicking “只读验证”, require the
four expected files and capture their exact hashes:

```powershell
$OutputFiles = Get-ChildItem -LiteralPath $OutputRoot -File |
  Where-Object Extension -in ".jpg", ".jpeg", ".png", ".mp4" |
  Sort-Object Name
if ($OutputFiles.Count -ne 4) {
  throw "Read-only verification requires exactly four controlled outputs."
}
$BeforeOutputHashes = @{}
foreach ($OutputFile in $OutputFiles) {
  $BeforeOutputHashes[$OutputFile.Name] =
    (Get-FileHash -LiteralPath $OutputFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
```

Clear the list, add all four `$OutputFiles`, click “只读验证”, and wait for
completion. Before any independent ExifTool inspection, recalculate each hash
and print the equality evidence:

```powershell
$AfterOutputHashes = @{}
$OutputHashMismatches = @()
foreach ($OutputFile in $OutputFiles) {
  $AfterOutputHashes[$OutputFile.Name] =
    (Get-FileHash -LiteralPath $OutputFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  $HashEvidence = [pscustomobject]@{
    File = $OutputFile.Name
    Before = $BeforeOutputHashes[$OutputFile.Name]
    After = $AfterOutputHashes[$OutputFile.Name]
    Equal = ($AfterOutputHashes[$OutputFile.Name] -eq $BeforeOutputHashes[$OutputFile.Name])
  }
  $HashEvidence
  if (-not $HashEvidence.Equal) {
    $OutputHashMismatches += $OutputFile.Name
  }
}
if ($OutputHashMismatches.Count -ne 0) {
  throw "Read-only verification changed output bytes: $($OutputHashMismatches -join ', ')"
}
```

Only after the aggregate mismatch gate passes, independently inspect fields and
raw XMP with the package ExifTool:

```powershell
& $ExifToolPath -G1 -s -XMP-dc:Subject -- $OutputFiles.FullName
foreach ($OutputFile in $OutputFiles) {
  "===== $($OutputFile.Name) ====="
  & $ExifToolPath -XMP -b -- $OutputFile.FullName
}
```

Record: the File/Before/After/Equal row for every output, per-file read-only
status, `XMP-dc:Subject` values, raw-XMP evidence for formal Dublin Core/RDF
namespaces and `dc:subject/rdf:Bag/rdf:li`, and any write-like side effect.

Pass condition: all four output SHA-256 values are unchanged and every equality
row is `True`; any nonempty mismatch aggregate throws before independent
ExifTool inspection; all four report verification success; the exact
case-sensitive value `contains-synthetic-performer` is in the formal
`rdf:Bag/rdf:li`; and the pre-existing subject remains.

### Step 9 — Inspect CSV columns and ExifTool version

PowerShell / action: copy the CSV path displayed by the app into `$CsvPath`,
then run:

```powershell
$ExpectedColumns = @(
  "相对路径", "格式", "运行模式", "处理状态", "验证结果", "验证字段",
  "实际读取值", "XMP结构", "验证时间", "ExifTool版本", "错误原因"
)
$CsvRows = Import-Csv -LiteralPath $CsvPath -Encoding utf8
$ActualColumns = @($CsvRows[0].PSObject.Properties.Name)
[pscustomobject]@{
  Expected = ($ExpectedColumns -join " | ")
  Actual = ($ActualColumns -join " | ")
  Equal = (@(Compare-Object $ExpectedColumns $ActualColumns -SyncWindow 0).Count -eq 0)
}
$CsvRows | Select-Object 相对路径, 运行模式, 处理状态, 验证结果, ExifTool版本
& $ExifToolPath -ver
```

Record: exact ordered header, row count, sanitized relative names, run mode,
verification result, every row's ExifTool version, and executable version.

Pass condition: the ordered 11-column header matches exactly, all four rows
describe read-only verification of the controlled outputs, and both CSV rows
and executable report ExifTool `13.59`.

### Step 10 — Exercise a target conflict

PowerShell / action: preserve one compliant output as evidence, replace only
that output with the matching unmarked controlled source, and rerun default
safe-copy mode for that one source:

```powershell
$ConflictOutput = Join-Path $OutputRoot "fixture.png"
$ConflictEvidence = Join-Path $AcceptanceRoot "fixture-compliant-before-conflict.png"
Copy-Item -LiteralPath $ConflictOutput -Destination $ConflictEvidence
Copy-Item -LiteralPath (Join-Path $FixtureRoot "fixture.png") -Destination $ConflictOutput -Force
$ConflictBefore = (Get-FileHash -LiteralPath $ConflictOutput -Algorithm SHA256).Hash
```

After the app reports the result:

```powershell
$ConflictAfter = (Get-FileHash -LiteralPath $ConflictOutput -Algorithm SHA256).Hash
[pscustomobject]@{ Before = $ConflictBefore; After = $ConflictAfter; Equal = ($ConflictBefore -eq $ConflictAfter) }
```

Record: exact sanitized app error, verification evidence, and conflict-file
hash before/after.

Pass condition: the app reports a target conflict because the existing output
does not pass strict verification, does not overwrite it, and continues to a
stable completed batch state.

### Step 11 — Exercise safe stop

PowerShell / action: create a disposable multi-file batch only from the
controlled fixture bytes:

```powershell
$SafeStopInput = Join-Path $AcceptanceRoot "safe-stop-controlled-batch"
New-Item -ItemType Directory -Path $SafeStopInput -ErrorAction Stop | Out-Null
1..25 | ForEach-Object {
  $Index = $_.ToString("00")
  Copy-Item (Join-Path $FixtureRoot "fixture.jpg")  (Join-Path $SafeStopInput "fixture-$Index.jpg")
  Copy-Item (Join-Path $FixtureRoot "fixture.jpeg") (Join-Path $SafeStopInput "fixture-$Index.jpeg")
  Copy-Item (Join-Path $FixtureRoot "fixture.png")  (Join-Path $SafeStopInput "fixture-$Index.png")
  Copy-Item (Join-Path $FixtureRoot "fixture.mp4")  (Join-Path $SafeStopInput "fixture-$Index.mp4")
}
```

Add the folder, start default safe-copy processing, and click “安全停止” while a
file is being processed. Wait until the app reports a stopped/completed state.

```powershell
Get-ChildItem -LiteralPath $SafeStopInput -Recurse -Force |
  Where-Object Name -Match "_original$|^\.emke-ai-marker-"
```

Record: when stop was requested, the current-file outcome, processed/stopped
counts, final summary text, and any leftover temp/backup files.

Pass condition: the admitted current file reaches a terminal result, no new
file begins after the stop boundary, remaining files are reported as stopped
before processing, and no partial final output, owned temp, or backup remains.

### Step 12 — Cancel advanced original-mode confirmation

PowerShell / action: create a dedicated disposable fixture and baseline hash:

```powershell
$OriginalModeFixture = Join-Path $AcceptanceRoot "advanced-mode-cancel.jpg"
Copy-Item (Join-Path $FixtureRoot "fixture.jpg") $OriginalModeFixture
$OriginalModeBefore = (Get-FileHash -LiteralPath $OriginalModeFixture -Algorithm SHA256).Hash
```

Clear the list, add this file, enable “高级原件模式”, click the start action,
verify that the second destructive confirmation appears, then choose Cancel.

```powershell
$OriginalModeAfter = (Get-FileHash -LiteralPath $OriginalModeFixture -Algorithm SHA256).Hash
[pscustomobject]@{
  Before = $OriginalModeBefore
  After = $OriginalModeAfter
  Equal = ($OriginalModeBefore -eq $OriginalModeAfter)
}
Get-ChildItem -LiteralPath $AcceptanceRoot -Filter "*_original" -Recurse
```

Record: exact confirmation text, chosen Cancel action, app state afterward,
hash equality, and backup search.

Pass condition: a distinct second confirmation clearly identifies direct
original modification, Cancel performs no media write, the hash is unchanged,
and no `*_original` file is created.

### Step 13 — Prove advanced mode resets after restart

PowerShell / action: fully close the app, confirm its process exits, relaunch
the same extracted executable, and reopen Settings:

```powershell
Get-Process -Name "EMKE AI Marker" -ErrorAction SilentlyContinue
Start-Process -FilePath $AppPath -WorkingDirectory $PackageRoot
```

Record: process exit/relaunch observation, initial mode summary, and advanced
mode toggle state after relaunch.

Pass condition: advanced original mode is off and safe-copy mode is again the
default without relying on any manual reset.

### Step 14 — Verify 100%, 150%, and 200% scaling

PowerShell / action: use Windows Settings > System > Display to apply each
required scaling value. At each value, relaunch the app, inspect the main
window and both dialogs, and traverse all controls using `Tab`/`Shift+Tab`.
Do not infer one scaling result from another.

Record: one row per scaling value in the matrix below, including active
scaling, Logo/accent, clipping/overlap, dialog reachability, focus visibility,
and any issue.

Pass condition: every matrix row passes independently; text and controls remain
readable and reachable, no critical content clips or overlaps, the Logo and
accent render, and keyboard focus stays visible.

## Required display-scaling matrix

| Scaling | Logo/accent | Chinese layout | Dialogs | Visible focus | Status/evidence |
| --- | --- | --- | --- | --- | --- |
| 100% | record observation | record observation | record observation | record observation | pass/fail/blocked plus evidence |
| 150% | record observation | record observation | record observation | record observation | pass/fail/blocked plus evidence |
| 200% | record observation | record observation | record observation | record observation | pass/fail/blocked plus evidence |

## Setup interactive acceptance addendum

The automated release build already installs, hashes, self-tests and uninstalls
`emke-ai-marker-v2.0.1-windows-x64-setup.exe` in a temporary directory. For
interactive Setup acceptance on the same real Windows 11 x64 host, also record:

- the Setup filename and SHA-256 match `SHA256SUMS.txt`;
- installation succeeds without elevation and targets the current user;
- the Start Menu shortcut is present;
- the desktop-shortcut option is unchecked by default and works when selected;
- the installed application reaches its main window;
- Windows “Installed apps” uninstall removes the application; and
- the exact observed SmartScreen prompt and publisher, or an explicit
  `no SmartScreen prompt observed` result.

These Setup observations do not replace the 14 portable-package items above.

## Final decision and evidence retention

Record one status and nonempty sanitized evidence for the headless self-test
and every numbered item. End the result document with exactly one of:

```text
Final result: passed
Final result: failed
Final result: blocked
```

Only `Final result: passed` satisfies the Windows 11 x64 design acceptance
gate, and it is permitted only when the self-test and all 14 items are `pass`.
Automated tests, cross-builds, cross-publish output, package fixtures, or CI
configuration may be listed separately as context but are not real-machine
acceptance.

Retain the exact ZIP and SHA-256 with the result according to project policy.
Any subsequent code or package change invalidates the old acceptance result;
generate a fresh ZIP and SHA-256 and rerun the entire checklist.
