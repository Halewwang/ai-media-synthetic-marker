# Local ExifTool runtime

This directory is intentionally not committed except for this file.

Run the following command from the repository root to download and verify the
locked ExifTool Windows package:

```powershell
py -3.14 scripts\fetch_exiftool.py
```

The expected version, URL, file size, and SHA-256 are stored in
`packaging/exiftool.lock.json`.

The fetch script also creates `exiftool-manifest.json`, which records every
runtime file's size and SHA-256. Release builds reject missing, added, or
modified ExifTool payload files.
