# EMKE AI Marker Python v1 archive

This directory preserves the v1.0.0 Python/Tkinter implementation for
one major release cycle as a behavior reference. It is not built,
packaged, or shipped by the EMKE AI Marker v2 product.

## Reference environment

The archived implementation requires Windows x64, Python 3.14.6 with
Tkinter, and ExifTool 13.59. Its product behavior and `APP_VERSION=1.0.0`
remain unchanged.

From the repository root, first prepare the current locked ExifTool runtime
using the v2 instructions in `BUILDING.md`. Then the reference source can be
started explicitly:

```powershell
$env:AI_MEDIA_MARKER_EXIFTOOL = (Resolve-Path .\runtime\exiftool\exiftool.exe)
$env:AI_MEDIA_MARKER_WORK_DIR = (Resolve-Path .\legacy\python)
py -3.14 .\legacy\python\src\ai_media_marker.py
```

The archived tests remain available as a behavior reference:

```powershell
py -3.14 -m unittest discover -s legacy/python/tests -v
```

The historical `scripts/build_release.py`, PyInstaller spec, dependency lock,
and launcher are retained only to explain and compare v1 behavior. They are
not the v2 build path and must not build, package, or ship EMKE AI Marker v2.
