# -*- mode: python ; coding: utf-8 -*-

import os
from pathlib import Path


PROJECT_ROOT = Path(SPECPATH).resolve().parent
SOURCE_PATH = PROJECT_ROOT / "src" / "ai_media_marker.py"
VERSION_FILE = os.environ.get("AI_MEDIA_MARKER_VERSION_FILE")

if not SOURCE_PATH.is_file():
    raise SystemExit(f"Missing application source: {SOURCE_PATH}")
if not VERSION_FILE:
    raise SystemExit(
        "AI_MEDIA_MARKER_VERSION_FILE is required. "
        "Build through scripts/build_release.py."
    )
if not Path(VERSION_FILE).is_file():
    raise SystemExit(f"Missing generated version file: {VERSION_FILE}")


a = Analysis(
    [str(SOURCE_PATH)],
    pathex=[str(PROJECT_ROOT / "src")],
    binaries=[],
    datas=[],
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)

pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name="AI人物媒体标记工具",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    version=str(Path(VERSION_FILE).resolve()),
)
