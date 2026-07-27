# Local ExifTool runtime

This directory is intentionally not committed except for this file.

From the repository root on Windows x64, first perform the locked NuGet
restore and then download and verify the locked ExifTool Windows package:

```powershell
dotnet restore Emke.AiMarker.sln --locked-mode
pwsh scripts\fetch-exiftool.ps1
```

The expected version, platform, URL, byte length, and SHA-256 are stored in
`packaging/exiftool.lock.json`. The release tool also creates
`exiftool-manifest.json`, recording every runtime payload file's size and
SHA-256. Integration tests and release builds reject a missing, added, or
modified payload.
