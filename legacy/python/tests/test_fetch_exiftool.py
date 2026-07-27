from __future__ import annotations

import hashlib
import importlib.util
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
FETCH_SCRIPT = PROJECT_ROOT / "scripts" / "fetch_exiftool.py"
SPEC = importlib.util.spec_from_file_location(
    "ai_media_marker_fetch_exiftool_tests",
    FETCH_SCRIPT,
)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"无法加载脚本：{FETCH_SCRIPT}")
fetch_exiftool = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = fetch_exiftool
SPEC.loader.exec_module(fetch_exiftool)


class ArchiveValidationTests(unittest.TestCase):
    def test_archive_size_hash_and_zip_format_are_all_required(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive_path = root / "exiftool.zip"
            with zipfile.ZipFile(archive_path, "w") as archive:
                archive.writestr("README.txt", "placeholder")
            data = archive_path.read_bytes()
            lock = {
                "size": len(data),
                "sha256": hashlib.sha256(data).hexdigest(),
            }

            fetch_exiftool.validate_archive(archive_path, lock)

            archive_path.write_bytes(data + b"tampered")
            with self.assertRaises(fetch_exiftool.FetchError):
                fetch_exiftool.validate_archive(archive_path, lock)

    def test_safe_extract_rejects_parent_directory_escape(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive_path = root / "unsafe.zip"
            destination = root / "output"
            destination.mkdir()
            with zipfile.ZipFile(archive_path, "w") as archive:
                archive.writestr("../escape.txt", "must not escape")

            with self.assertRaises(fetch_exiftool.FetchError):
                fetch_exiftool.safe_extract(archive_path, destination)

            self.assertFalse((root / "escape.txt").exists())


class InstallManifestTests(unittest.TestCase):
    def test_manifest_detects_any_payload_change(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "exiftool"
            files_dir = root / "exiftool_files"
            files_dir.mkdir(parents=True)
            (root / "exiftool.exe").write_bytes(b"launcher")
            (root / "README.txt").write_text("readme", encoding="utf-8")
            (files_dir / "perl.exe").write_bytes(b"perl")
            lock = {
                "version": "13.59",
                "archive_name": "exiftool-13.59_64.zip",
                "size": 123,
                "sha256": "ab" * 32,
            }

            fetch_exiftool.write_install_manifest(root, lock)
            self.assertTrue(fetch_exiftool.install_manifest_matches(root, lock))

            (files_dir / "perl.exe").write_bytes(b"tampered")
            self.assertFalse(fetch_exiftool.install_manifest_matches(root, lock))


if __name__ == "__main__":
    unittest.main()
