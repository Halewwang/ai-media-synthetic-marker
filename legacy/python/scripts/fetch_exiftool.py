from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import uuid
import zipfile
from pathlib import Path, PurePosixPath


PROJECT_ROOT = Path(__file__).resolve().parent.parent
REPOSITORY_ROOT = PROJECT_ROOT.parents[1]
LOCK_PATH = REPOSITORY_ROOT / "packaging" / "exiftool.lock.json"
DEFAULT_TARGET = REPOSITORY_ROOT / "runtime" / "exiftool"
CREATE_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)
MANIFEST_NAME = "exiftool-manifest.json"


class FetchError(RuntimeError):
    pass


def read_lock() -> dict[str, object]:
    try:
        lock = json.loads(LOCK_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise FetchError(f"无法读取 ExifTool 锁定文件：{exc}") from exc

    required = {"version", "archive_name", "url", "size", "sha256"}
    missing = required.difference(lock)
    if missing:
        raise FetchError(f"ExifTool 锁定文件缺少字段：{', '.join(sorted(missing))}")
    return lock


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _is_link_or_junction(path: Path) -> bool:
    if path.is_symlink():
        return True
    is_junction = getattr(os.path, "isjunction", None)
    return bool(is_junction and is_junction(path))


def collect_payload_records(root: Path) -> list[dict[str, object]]:
    records: list[dict[str, object]] = []
    for current_root, directory_names, file_names in os.walk(
        root,
        topdown=True,
        followlinks=False,
    ):
        current = Path(current_root)
        for directory_name in list(directory_names):
            directory = current / directory_name
            if _is_link_or_junction(directory):
                raise FetchError(f"ExifTool 运行目录包含链接或联接点：{directory}")
        for file_name in file_names:
            path = current / file_name
            relative = path.relative_to(root)
            if relative.parts == ("README.md",):
                continue
            if relative.parts == (MANIFEST_NAME,):
                continue
            if _is_link_or_junction(path) or not path.is_file():
                raise FetchError(f"ExifTool 运行目录包含非常规文件：{path}")
            records.append(
                {
                    "path": relative.as_posix(),
                    "size": path.stat().st_size,
                    "sha256": sha256_file(path),
                }
            )
    records.sort(key=lambda item: str(item["path"]).casefold())
    return records


def write_install_manifest(
    root: Path,
    lock: dict[str, object],
) -> None:
    manifest = {
        "schema_version": 1,
        "exiftool_version": str(lock["version"]),
        "archive_name": str(lock["archive_name"]),
        "archive_size": int(lock["size"]),
        "archive_sha256": str(lock["sha256"]).casefold(),
        "files": collect_payload_records(root),
    }
    (root / MANIFEST_NAME).write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def install_manifest_matches(
    root: Path,
    lock: dict[str, object],
) -> bool:
    manifest_path = root / MANIFEST_NAME
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        if not isinstance(manifest, dict):
            return False
        if manifest.get("schema_version") != 1:
            return False
        if str(manifest.get("exiftool_version")) != str(lock["version"]):
            return False
        if str(manifest.get("archive_name")) != str(lock["archive_name"]):
            return False
        if int(manifest.get("archive_size", -1)) != int(lock["size"]):
            return False
        if str(manifest.get("archive_sha256", "")).casefold() != str(
            lock["sha256"]
        ).casefold():
            return False
        expected_records = manifest.get("files")
        if not isinstance(expected_records, list):
            return False
        return expected_records == collect_payload_records(root)
    except (FetchError, OSError, ValueError, TypeError, json.JSONDecodeError):
        return False


def validate_archive(path: Path, lock: dict[str, object]) -> None:
    if not path.is_file():
        raise FetchError(f"找不到 ExifTool 压缩包：{path}")

    expected_size = int(lock["size"])
    actual_size = path.stat().st_size
    if actual_size != expected_size:
        raise FetchError(
            f"ExifTool 压缩包大小不符：期望 {expected_size}，实际 {actual_size}"
        )

    expected_hash = str(lock["sha256"]).casefold()
    actual_hash = sha256_file(path).casefold()
    if actual_hash != expected_hash:
        raise FetchError(
            "ExifTool 压缩包 SHA-256 不符。\n"
            f"期望：{expected_hash}\n"
            f"实际：{actual_hash}"
        )

    if not zipfile.is_zipfile(path):
        raise FetchError("下载内容不是有效的 ZIP 文件。")


def download_archive(destination: Path, lock: dict[str, object]) -> None:
    request = urllib.request.Request(
        str(lock["url"]),
        headers={"User-Agent": "ai-media-synthetic-marker-build/1.0"},
    )
    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            with destination.open("xb") as output:
                shutil.copyfileobj(response, output)
    except (OSError, urllib.error.URLError) as exc:
        raise FetchError(f"下载 ExifTool 失败：{exc}") from exc


def safe_extract(archive_path: Path, destination: Path) -> None:
    destination = destination.resolve()
    with zipfile.ZipFile(archive_path) as archive:
        for info in archive.infolist():
            member = PurePosixPath(info.filename)
            if member.is_absolute() or ".." in member.parts:
                raise FetchError(f"ZIP 包含不安全路径：{info.filename}")
            if member.parts and ":" in member.parts[0]:
                raise FetchError(f"ZIP 包含不安全盘符：{info.filename}")

            unix_mode = info.external_attr >> 16
            if unix_mode and stat.S_ISLNK(unix_mode):
                raise FetchError(f"ZIP 包含不允许的符号链接：{info.filename}")

            resolved = (destination / Path(*member.parts)).resolve()
            try:
                resolved.relative_to(destination)
            except ValueError as exc:
                raise FetchError(f"ZIP 路径越界：{info.filename}") from exc

        archive.extractall(destination)


def run_exiftool_version(executable: Path) -> str:
    try:
        completed = subprocess.run(
            [str(executable), "-ver"],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            creationflags=CREATE_NO_WINDOW,
            timeout=60,
            check=False,
        )
    except OSError as exc:
        raise FetchError(f"无法启动 ExifTool：{exc}") from exc

    stdout = completed.stdout.decode("utf-8-sig", errors="replace").strip()
    stderr = completed.stderr.decode("utf-8-sig", errors="replace").strip()
    if completed.returncode != 0:
        raise FetchError(stderr or stdout or f"ExifTool 退出码 {completed.returncode}")
    return stdout


def installation_is_ready(
    target: Path,
    lock: dict[str, object],
) -> bool:
    expected_version = str(lock["version"])
    executable = target / "exiftool.exe"
    try:
        if not executable.is_file():
            return False
        if not (target / "exiftool_files").is_dir():
            return False
        if not (target / "README.txt").is_file():
            return False
        if not install_manifest_matches(target, lock):
            return False
        return run_exiftool_version(executable) == expected_version
    except (FetchError, OSError):
        return False


def remove_tree(path: Path) -> None:
    def make_writable_and_retry(function, failed_path, _error_info) -> None:
        os.chmod(failed_path, stat.S_IWRITE)
        function(failed_path)

    shutil.rmtree(path, onerror=make_writable_and_retry)


def install_exiftool(
    archive_path: Path,
    target: Path,
    lock: dict[str, object],
    force: bool,
) -> None:
    expected_version = str(lock["version"])
    target = target.resolve()
    target.parent.mkdir(parents=True, exist_ok=True)

    if installation_is_ready(target, lock):
        print(f"ExifTool {expected_version} 已就绪：{target}")
        return

    preserved_readme: bytes | None = None
    existing_entries: list[Path] = []
    if target.exists():
        readme = target / "README.md"
        if readme.is_file():
            preserved_readme = readme.read_bytes()
        existing_entries = [
            item for item in target.iterdir() if item.name.casefold() != "readme.md"
        ]
        if existing_entries and not force:
            raise FetchError(
                f"目标目录已有其他内容：{target}\n"
                "请先确认内容，或明确使用 --force 重新安装。"
            )

    with tempfile.TemporaryDirectory(
        prefix=".fetch-exiftool-",
        dir=target.parent,
    ) as temporary:
        temporary_root = Path(temporary)
        extracted = temporary_root / "extracted"
        extracted.mkdir()
        safe_extract(archive_path, extracted)

        launchers = list(extracted.rglob("exiftool(-k).exe"))
        if len(launchers) != 1:
            raise FetchError(
                f"压缩包中应有且仅有一个 exiftool(-k).exe，实际找到 {len(launchers)} 个。"
            )

        payload = launchers[0].parent
        if not (payload / "exiftool_files").is_dir():
            raise FetchError("压缩包缺少 exiftool_files 文件夹。")
        if not (payload / "README.txt").is_file():
            raise FetchError("压缩包缺少 README.txt。")

        install_stage = temporary_root / "install"
        shutil.copytree(payload, install_stage)
        (install_stage / "exiftool(-k).exe").rename(
            install_stage / "exiftool.exe"
        )
        if preserved_readme is not None:
            (install_stage / "README.md").write_bytes(preserved_readme)
        write_install_manifest(install_stage, lock)

        installed_version = run_exiftool_version(install_stage / "exiftool.exe")
        if installed_version != expected_version:
            raise FetchError(
                f"ExifTool 版本不符：期望 {expected_version}，实际 {installed_version}"
            )

        backup: Path | None = None
        if target.exists():
            backup = target.with_name(f".{target.name}.backup-{uuid.uuid4().hex}")
            target.rename(backup)
        try:
            install_stage.rename(target)
        except Exception:
            if backup is not None and backup.exists() and not target.exists():
                backup.rename(target)
            raise
        else:
            if backup is not None and backup.exists():
                remove_tree(backup)

    print(f"ExifTool {expected_version} 安装完成：{target}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="下载、校验并准备项目锁定的 ExifTool Windows 运行组件。"
    )
    parser.add_argument(
        "--target",
        type=Path,
        default=DEFAULT_TARGET,
        help="安装目录，默认 runtime/exiftool。",
    )
    parser.add_argument(
        "--archive",
        type=Path,
        help="使用本地 ZIP，不进行联网下载。",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="替换目标目录中已有的非占位内容。",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        lock = read_lock()
        target = args.target.resolve()
        if installation_is_ready(target, lock):
            print(f"ExifTool {lock['version']} 已就绪：{target}")
            return 0

        if args.archive:
            archive_path = args.archive.resolve()
            validate_archive(archive_path, lock)
            install_exiftool(archive_path, target, lock, args.force)
            return 0

        target.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(
            prefix=".download-exiftool-",
            dir=target.parent,
        ) as temporary:
            archive_path = Path(temporary) / str(lock["archive_name"])
            print(f"正在下载 ExifTool {lock['version']}……")
            download_archive(archive_path, lock)
            validate_archive(archive_path, lock)
            install_exiftool(archive_path, target, lock, args.force)
        return 0
    except (FetchError, OSError) as exc:
        print(f"错误：{exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
