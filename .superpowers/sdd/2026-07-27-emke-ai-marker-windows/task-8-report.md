# Task 8 Report — compact branded WPF shell

## Status

Implemented the confirmed compact single-page WPF shell, approved EMKE brand
assets, centralized Chinese resources, native file/folder selection, validated
shell opening, drag/drop path handoff, and the production App composition root.

The App and App.Tests cross-build for the Windows x64 target with zero warnings
and zero errors. Core and Infrastructure tests run on this host and pass.
App.Tests compile but cannot execute on this macOS arm64 host because the test
project is intentionally `win-x64` and no x64 `dotnet` host is installed.

Task 9 behavior was not added: there is no advanced settings dialog,
confirmation dialog, single-instance guard, close workflow, or self-test. The
Task 8 settings shell callback is deliberately empty until Task 9 supplies the
real dialog. The two deferred Task 7 Minor items (empty-selection messaging and
UI synchronization-context behavior) remain unchanged.

## Brand assets

The source was copied byte-for-byte from the approved SVG. It was rendered
before derivation and inspected as a black E-shaped form crossed by the
`#36A39E` wave.

| Asset | SHA-256 | Dimensions / container |
| --- | --- | --- |
| `assets/branding/emke-app-logo.svg` | `c98e5a189b344bd5adb6b49848acdf307ce0f12c4e60a428c4c421fe258142a6` | `202 x 202` SVG |
| `src/Emke.AiMarker.App/Assets/emke-app-logo-32.png` | `9133a25194968274a0421759e9d11d80fad925c15469b8433f3084e0d398beb3` | `32 x 32` PNG |
| `src/Emke.AiMarker.App/Assets/emke-app-logo-256.png` | `9db6cc877988dd56e5051e42c9739291958aa0d3d54194ef71b335f51832a3f6` | `256 x 256` PNG |
| `src/Emke.AiMarker.App/Assets/emke-ai-marker.ico` | `b2c7b8a625fee7d85791acf1b9bd70ed3d182d337ce30d4c45463ec6109de2e2` | one `256 x 256`, 32-bit icon entry containing PNG data |

`cmp` confirmed the repository SVG and approved source have identical bytes.
Both PNG sizes were opened with `view_image`. The ICO was converted back to a
`256 x 256` PNG and opened as well. All three show the same black E-shaped form
and teal wave with the original white canvas, no crop, blank output, or
substitution.

## TDD evidence

The Task 8 test contracts and helper were created before the production
resources and services.

### RED 1 — missing brand/resource artifacts

A host-side static execution of the same artifact contract failed for the
expected missing production files:

```text
AssertionError: Task 8 contract missing:
assets/branding/emke-app-logo.svg,
assets/branding/brand-assets.json,
src/Emke.AiMarker.App/Assets/emke-app-logo-32.png,
src/Emke.AiMarker.App/Resources/Theme.xaml,
src/Emke.AiMarker.App/Resources/Strings.zh-CN.xaml
```

### RED 2 — missing resource text and shell contracts

After the harness and shell behavior tests were written, the App.Tests
cross-build failed against the pre-implementation App:

```text
CS0246: The type or namespace name 'IAppText' could not be found
```

The same test change also referenced the not-yet-created concrete
`ShellService`; compilation stopped at the first missing contract.

### GREEN

After the minimal production implementation:

```text
Task 8 host-static contract: PASS
```

The host-static execution independently checks:

- approved SVG bytes, SHA-256, manifest values, and declared dimensions;
- PNG signatures/dimensions and ICO header/entry count;
- exact brand tokens and required Chinese resource keys;
- the exact four-format native dialog filter;
- absence of CJK text in App C# and `MainWindow.xaml`;
- `MaxWidth=720`, vertical-only scrolling, all four state references,
  drag/drop handlers, bound details expander, and accessibility names.

`BrandResourceTests` uses WPF `BitmapDecoder` for both PNG derivatives and
checks the ICO container. Those tests are compile-proven but not represented
as executed on macOS.

## Implemented UI and adapters

- One WPF `Window` with a fixed white header, approved 32-pixel Logo resource,
  EMKE name, settings command, and a vertically scrolling content area capped
  at 720 DIPs.
- Central tokens for `#F7F9F9`, `#36A39E`, `#E3F3F1`, `#172022`, and
  `#B42318`, plus Segoe UI Variable with Segoe UI fallback. A restrained pale
  violet completion panel preserves the confirmed Quiet Violet tone while
  teal remains the action, focus, and progress color.
- Empty/Ready selection surface, Ready summary and exact read-only marker,
  safe-copy statement, details Expander, Running progress/safe stop, and
  Completed summary/output/log/reset sections driven by Task 7 `State` and
  commands.
- No horizontal scrolling; compact margins and controls; visible teal focus
  visuals and automation names on the interactive shell controls.
- Drag/drop code-behind only validates the WPF file-drop payload/state and
  forwards the path array to `MainWindowViewModel.AddPathsAsync`.
- `OpenFileDialog` with multi-select and exact
  `*.jpg;*.jpeg;*.png;*.mp4`; `OpenFolderDialog` with multi-select.
- `ShellService` normalizes and verifies that a file or directory exists
  before invoking the native shell with `UseShellExecute=true`.
- Every App-owned visible Chinese string, including ViewModel summary/error
  formats, lives in `Strings.zh-CN.xaml`. `IAppText` keeps the ViewModel free of
  WPF resource APIs, while `ResourceAppText` is the WPF adapter.
- The App composition root creates the physical scanner, storage probe,
  copy transaction, ExifTool client, media/batch processors, CSV writer,
  selection/prompt/shell adapters, ViewModel, and MainWindow.

## Verification

Commands and final results:

```text
./.superpowers/dotnet/dotnet build \
  src/Emke.AiMarker.App/Emke.AiMarker.App.csproj \
  -c Debug -p:EnableWindowsTargeting=true --nologo
```

```text
Build succeeded. 0 Warning(s), 0 Error(s).
```

```text
./.superpowers/dotnet/dotnet build \
  tests/Emke.AiMarker.App.Tests/Emke.AiMarker.App.Tests.csproj \
  -c Debug -p:EnableWindowsTargeting=true --nologo
```

```text
Build succeeded. 0 Warning(s), 0 Error(s).
```

The same two Release cross-builds also succeeded with zero warnings and zero
errors.

Focused App test execution rebuilt all projects, discovered the test assembly,
and then stopped at the exact host boundary:

```text
Could not find a 'dotnet' host for the 'X64' architecture.
Test Run Aborted.
```

Runnable suites:

- Core: 52 passed, 0 failed.
- Infrastructure: 78 passed, 0 failed.
- Host-static Task 8 contract: passed.
- `dotnet format --verify-no-changes` limited to all changed C# files: passed.
- `git diff --check`: passed.

A full-solution format check also inspected the untouched WPF-template
`AssemblyInfo.cs` and reported its pre-existing comment alignment as
whitespace changes. That file is byte-identical to `HEAD` and was kept out of
scope; the changed-file format check is clean.

## Files

Created:

- `assets/branding/emke-app-logo.svg`
- `assets/branding/brand-assets.json`
- `src/Emke.AiMarker.App/Assets/emke-app-logo-32.png`
- `src/Emke.AiMarker.App/Assets/emke-app-logo-256.png`
- `src/Emke.AiMarker.App/Assets/emke-ai-marker.ico`
- `src/Emke.AiMarker.App/Resources/Theme.xaml`
- `src/Emke.AiMarker.App/Resources/Controls.xaml`
- `src/Emke.AiMarker.App/Resources/Strings.zh-CN.xaml`
- `src/Emke.AiMarker.App/Services/IAppText.cs`
- `src/Emke.AiMarker.App/Services/ResourceAppText.cs`
- `src/Emke.AiMarker.App/Services/FileSelectionService.cs`
- `src/Emke.AiMarker.App/Services/ShellService.cs`
- `tests/Emke.AiMarker.App.Tests/Resources/BrandResourceTests.cs`
- `tests/Emke.AiMarker.App.Tests/Services/ShellServiceTests.cs`
- `tests/Emke.AiMarker.App.Tests/TestSupport/RepositoryRoot.cs`

Modified:

- `src/Emke.AiMarker.App/App.xaml`
- `src/Emke.AiMarker.App/App.xaml.cs`
- `src/Emke.AiMarker.App/Emke.AiMarker.App.csproj`
- `src/Emke.AiMarker.App/MainWindow.xaml`
- `src/Emke.AiMarker.App/MainWindow.xaml.cs`
- `src/Emke.AiMarker.App/ViewModels/MainWindowViewModel.cs`
- `tests/Emke.AiMarker.App.Tests/TestSupport/MainWindowHarness.cs`

## Self-review and proof limits

- Re-read the Task 8 brief after implementation and checked every named asset,
  token, string category, state section, command, drag/drop event, dialog
  setting, and service boundary.
- Confirmed the copied SVG has no source-path provenance in product files and
  no private media, CSV, runtime payload, build output, cache, or local
  absolute path is part of the Task 8 file set.
- Confirmed code-behind performs no scanning, media processing, shell launch,
  or settings logic.
- Confirmed the App C# production tree and shell XAML contain no hard-coded
  Chinese copy.
- Confirmed Task 9 files and behavior are absent and Core/Infrastructure
  production sources are untouched.

Not proven on this host:

- execution of App.Tests or startup of the WPF executable;
- WPF resource lookup, dispatcher/binding behavior, native dialogs, drag/drop,
  shell invocation, accessibility tree, or focus rendering on Windows;
- Empty/Ready/Running/Completed visual layout at 100%, 150%, and 200% Windows
  display scaling;
- Chinese clipping, minimum-window behavior, Windows DPI behavior, package
  icon display, Windows x64 real-machine behavior, signing, packaging, or
  release acceptance.

Windows workstation inspection at 100%, 150%, and 200% remains an explicit
later acceptance gate and must reject any cropped Logo, clipped Chinese text,
missing focus ring, or horizontal scrolling.

---

## Fix round 1/5 — distinguish primary-button keyboard focus

Review found that the original `BrandFocusVisual` used the same teal brush as
the primary button background. Because the single ring was inset with
`Margin="2"`, keyboard focus on a primary action could visually merge into the
button fill.

### TDD RED

The static focus contract was added before changing `Controls.xaml`. It
requires:

- secondary buttons to use `BrandFocusVisual`;
- primary buttons to override that with an independent
  `PrimaryButtonFocusVisual`;
- secondary focus to combine a teal ring with white separation;
- primary focus to combine a white ring with a dark inner ring;
- exactly two nonzero borders per focus visual and no negative Margin.

The host-executable contract failed against the original resources with the
intended message:

```text
AssertionError: primary action lacks an independent high-contrast focus visual
```

### GREEN

`BrandFocusVisual` now renders an inset two-pixel teal border and a separate
white inner border. `PrimaryButtonFocusVisual` renders an inset two-pixel white
border and a separate dark inner border, and `PrimaryButtonStyle` explicitly
selects it. All border margins are positive (`1` and `4`), so this fix does not
depend on a negative external margin that a parent could clip.

The same host-static contract then reported:

```text
Task 8 focus-static contract: PASS
```

Verification after the fix:

- App Release cross-build: succeeded, 0 warnings / 0 errors.
- App.Tests Release cross-build: succeeded, 0 warnings / 0 errors.
- Core: 52 passed, 0 failed.
- Infrastructure: 78 passed, 0 failed.
- Changed-file format check: passed.
- `git diff --check`: passed.

Only `Controls.xaml`, its static resource test, and this report changed. The
registered ShellService missing-path English-message Minor was intentionally
not changed.

The static structure check proves the resource contract, not actual rendering.
Keyboard focus contrast, clipping, high-contrast-mode behavior, and DPI
behavior still require Windows visual inspection.
