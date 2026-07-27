# Windows 11 x64 smoke result — 2026-07-27

Acceptance date: `2026-07-27`

Outcome: the acceptance gate is blocked. No Windows 11 x64 host and no
Windows-verified product ZIP/checksum were available, so the staged self-test
and every GUI/media item were not executed. `blocked` is not `pass` and does
not satisfy the design acceptance gate.

## Artifact and host identity

| Field | Recorded value |
| --- | --- |
| Windows edition/build | blocked — no Windows 11 x64 host |
| Architecture | blocked — no Windows 11 x64 host |
| Display scaling | blocked — no Windows 11 x64 host; 100%, 150%, and 200% not executed |
| ZIP filename | blocked — no verified product ZIP |
| ZIP SHA-256 | blocked — no verified product ZIP; no product hash recorded |
| App file version | blocked — no verified product ZIP to extract on Windows |
| ExifTool version | blocked — packaged Windows ExifTool was not executed |
| SmartScreen behavior | blocked — no Windows 11 x64 host and no verified product ZIP |

No placeholder version, product SHA-256, Windows build, or SmartScreen behavior
has been inferred. In particular, the deterministic ZIP hash from a controlled
Release.Tests fixture is not a production ZIP hash and is not recorded as
artifact identity here.

## Item results

| Item | Status | Evidence |
| --- | --- | --- |
| Self-test | blocked | not executed — no Windows 11 x64 host and no verified ZIP; no exit code or `Result=ok` report exists |
| 1 | blocked | not executed — no verified product ZIP was available for full Windows extraction |
| 2 | blocked | not executed — no verified product ZIP or product `SHA256SUMS.txt` was available to compare |
| 3 | blocked | not executed — no Windows 11 x64 host and no extracted product executable |
| 4 | blocked | not executed — Logo, `#36A39E`, Chinese layout, and visible focus require the real WPF UI on Windows |
| 5 | blocked | not executed — the four controlled formats were not dragged into a real Windows app instance |
| 6 | blocked | not executed — default safe-copy mode was not run from a verified Windows package |
| 7 | blocked | not executed — there was no real-machine default-copy run from which to collect before/after source hashes |
| 8 | blocked | not executed — no real Windows outputs existed for read-only strict verification |
| 9 | blocked | not executed — no real-machine CSV or packaged ExifTool execution existed to inspect |
| 10 | blocked | not executed — target-conflict behavior was not exercised on a Windows package |
| 11 | blocked | not executed — safe stop was not exercised during a real Windows multi-file batch |
| 12 | blocked | not executed — advanced original mode and its second confirmation were not opened on Windows |
| 13 | blocked | not executed — no Windows app lifecycle existed to prove advanced mode reset after restart |
| 14 | blocked | not executed — 100%, 150%, and 200% Windows display scaling were unavailable |

Every status uses the checklist vocabulary `pass`, `fail`, or `blocked`. There
are no skipped items represented as passes.

## Sanitized environment inventory

- The available development host was macOS on arm64, not Windows.
- No usable local Windows virtualization/runtime was present: qemu, Lima,
  Tart, Multipass, Parallels, VMware, UTM, and Windows Remote Desktop were
  unavailable.
- The remote-access device inventory exposed only macOS and iOS device classes;
  no Windows device was available. Private device names were intentionally not
  recorded.
- Task 11 produced only a self-contained `win-x64` cross-publish on macOS.
  Because the staged WPF `--self-test` could not run, it did not produce a
  verified production ZIP or product SHA-256.

This inventory explains the block. It is not Windows evidence and does not
convert any item to pass or fail.

## Automated evidence (not Windows acceptance)

Existing local evidence reported before this acceptance record:

| Evidence | Existing local result | Boundary |
| --- | --- | --- |
| Release tests | 93 passed | macOS automated contract evidence only; not a Windows staged self-test or product ZIP |
| Core tests | 52 passed | platform-independent automated evidence only |
| Infrastructure tests | 84 passed | automated evidence; Windows handle and UI behavior remain unaccepted |
| Integration tests | 11 passed | real local ExifTool 13.59 on macOS with controlled fixtures; not packaged Windows ExifTool |
| App and App.Tests | Release cross-build passed | WPF compilation evidence only; binaries were not launched |
| App publish | self-contained `win-x64` cross-publish passed | cross-publish only; staged package self-test and production ZIP were not completed |

These results are explicitly not CI proof, package acceptance, SmartScreen
evidence, Windows GUI evidence, signing evidence, installer evidence, or
public-release evidence.

## Real-machine evidence boundary

Only fresh evidence from the immutable checklist run on a Windows 11 x64 host
against one newly verified product ZIP and its exact SHA-256 can change this
record's gate outcome to `passed`. The headless self-test and all 14 numbered
items must each be `pass`; `blocked` can never satisfy that gate.

If any code or package byte changes, this record becomes invalid for the new
artifact. Generate a new ZIP and SHA-256 and create a new result record rather
than editing or reusing this one.

Final result: blocked
