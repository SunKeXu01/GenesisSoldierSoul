#!/usr/bin/env python3
"""Safely stage recovered archives into source-isolated directories.

The extractor never executes recovered files.  Every archive gets a unique
destination derived from its workspace-relative path and content hash, so two
packages containing Assets/foo.prefab cannot overwrite one another.
"""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import os
import shutil
import subprocess
import tempfile
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath


SUPPORTED_SUFFIXES = (
    ".zip",
    ".rar",
    ".7z",
    ".unitypackage",
    ".apk",
    ".apk.1",
)
IGNORED_DIRS = {
    ".git",
    "Library",
    "Temp",
    "Logs",
    "node_modules",
    "Build",
    "dist",
    "_安全解压资源",
}
ASSET_CATEGORIES = {
    "model": {".fbx", ".obj", ".3ds", ".dae", ".blend", ".ltb"},
    "texture": {".png", ".jpg", ".jpeg", ".tga", ".bmp", ".dds", ".dtx"},
    "audio": {".wav", ".mp3", ".ogg", ".aif", ".aiff"},
    "animation": {".anim", ".controller", ".avatar", ".mask"},
    "unity": {".prefab", ".unity", ".mat", ".asset"},
    "source": {".cs", ".js", ".shader", ".cginc", ".hlsl"},
}


def is_supported(path: Path) -> bool:
    lower = path.name.lower()
    return any(lower.endswith(suffix) for suffix in SUPPORTED_SUFFIXES)


def discover(workspace: Path, output: Path) -> list[Path]:
    archives: list[Path] = []
    for folder, dirs, files in os.walk(workspace):
        folder_path = Path(folder)
        dirs[:] = [
            name
            for name in dirs
            if name not in IGNORED_DIRS
            and (folder_path / name).resolve() != output
        ]
        archives.extend(
            folder_path / name for name in files if is_supported(folder_path / name)
        )
    return sorted(archives)


def hash_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def destination_name(relative: Path, digest: str) -> str:
    stem = str(relative).replace(os.sep, "__")
    safe = "".join(character if character.isalnum() or character in "._-" else "_" for character in stem)
    return safe[:160] + "__" + digest[:12]


def list_members(path: Path) -> tuple[list[str], str | None]:
    process = subprocess.run(
        ["bsdtar", "-tf", str(path)],
        capture_output=True,
        text=True,
        errors="replace",
        timeout=180,
        check=False,
    )
    members = [line.strip() for line in process.stdout.splitlines() if line.strip()]
    error = None if process.returncode == 0 else process.stderr.strip()[-500:]
    return members, error


def unsafe_member(member: str) -> str | None:
    normalized = member.replace("\\", "/")
    if "\x00" in normalized:
        return "nul_byte"
    pure = PurePosixPath(normalized)
    if pure.is_absolute():
        return "absolute_path"
    if any(part == ".." for part in pure.parts):
        return "parent_traversal"
    if pure.parts and ":" in pure.parts[0]:
        return "drive_path"
    return None


def validate_tree(root: Path) -> str | None:
    resolved_root = root.resolve()
    for path in root.rglob("*"):
        if path.is_symlink():
            return "symbolic_link:" + str(path.relative_to(root))
        try:
            path.resolve().relative_to(resolved_root)
        except ValueError:
            return "escaped_path:" + str(path)
    return None


def category_for_asset(pathname: str) -> str:
    suffix = PurePosixPath(pathname.lower()).suffix
    for category, suffixes in ASSET_CATEGORIES.items():
        if suffix in suffixes:
            return category
    return "other"


def materialize_unitypackage(root: Path) -> tuple[dict[str, object], str | None]:
    """Rebuild a UnityPackage's Assets tree without executing Unity.

    UnityPackage archives store every item below a GUID directory and keep the
    original project-relative name in a small ``pathname`` file.  The raw GUID
    tree remains untouched; this creates a sibling ``_materialized/Assets``
    tree that can be searched, diffed, or copied into a Unity project.
    """
    materialized = root / "_materialized"
    if materialized.exists():
        manifest_path = materialized / ".genesis-unitypackage.json"
        if manifest_path.is_file():
            try:
                return json.loads(manifest_path.read_text(encoding="utf-8")), None
            except (OSError, json.JSONDecodeError):
                return {}, "invalid_existing_materialized_manifest"
        return {}, "materialized_destination_exists"

    staging = root / "_materialized-staging"
    if staging.exists():
        return {}, "materialized_staging_exists"
    staging.mkdir()
    categories: Counter[str] = Counter()
    paths: list[str] = []
    try:
        for pathname_file in sorted(root.glob("*/pathname")):
            try:
                pathname = pathname_file.read_text(
                    encoding="utf-8", errors="strict"
                ).strip("\x00\r\n")
            except (OSError, UnicodeError) as exc:
                return {}, "pathname_read_error:" + str(exc)[:200]
            reason = unsafe_member(pathname)
            if reason:
                return {}, "unsafe_unity_path:" + reason + ":" + pathname[:200]
            pure = PurePosixPath(pathname.replace("\\", "/"))
            if not pure.parts or pure.parts[0] != "Assets":
                return {}, "unity_path_outside_assets:" + pathname[:200]

            source_dir = pathname_file.parent
            source_asset = source_dir / "asset"
            source_meta = source_dir / "asset.meta"
            target = staging.joinpath(*pure.parts)
            if source_asset.is_file():
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source_asset, target)
                categories[category_for_asset(pathname)] += 1
                paths.append(pathname)
            else:
                target.mkdir(parents=True, exist_ok=True)
            if source_meta.is_file():
                target_meta = Path(str(target) + ".meta")
                target_meta.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source_meta, target_meta)

        summary: dict[str, object] = {
            "assets": len(paths),
            "categories": dict(sorted(categories.items())),
            "paths": paths,
        }
        (staging / ".genesis-unitypackage.json").write_text(
            json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        staging.replace(materialized)
        return summary, None
    finally:
        if staging.exists():
            shutil.rmtree(staging)


def extract_archive(
    path: Path,
    destination: Path,
    source_reference: str,
    digest: str,
) -> tuple[bool, str | None, dict[str, object] | None]:
    destination.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix="archive-", dir=destination.parent))
    try:
        process = subprocess.run(
            [
                "bsdtar",
                "--no-same-owner",
                "--no-same-permissions",
                "-xf",
                str(path),
                "-C",
                str(staging),
            ],
            capture_output=True,
            text=True,
            errors="replace",
            timeout=900,
            check=False,
        )
        if process.returncode != 0:
            return False, process.stderr.strip()[-500:], None
        tree_error = validate_tree(staging)
        if tree_error:
            return False, tree_error, None
        materialized = None
        if path.name.lower().endswith(".unitypackage"):
            materialized, materialize_error = materialize_unitypackage(staging)
            if materialize_error:
                return False, materialize_error, None
        (staging / ".genesis-source.json").write_text(
            json.dumps(
                {
                    "archive": source_reference,
                    "bytes": path.stat().st_size,
                    "sha256": digest,
                    "extracted_at_utc": datetime.now(timezone.utc).isoformat(),
                    "method": "read-only bsdtar extraction; recovered files were not executed",
                    "materialized": materialized,
                },
                ensure_ascii=False,
                indent=2,
            ) + "\n",
            encoding="utf-8",
        )
        if destination.exists():
            return False, "destination_exists", None
        staging.replace(destination)
        return True, None, materialized
    finally:
        if staging.exists():
            shutil.rmtree(staging)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--only", action="append", default=[])
    parser.add_argument("--max-bytes", type=int, default=4 * 1024**3)
    parser.add_argument("--extract", action="store_true")
    args = parser.parse_args()

    workspace = args.workspace.resolve()
    output = args.output.resolve()
    selected = discover(workspace, output)
    if args.only:
        selected = [
            path
            for path in selected
            if any(
                fnmatch.fnmatch(str(path.relative_to(workspace)), pattern)
                or pattern in str(path.relative_to(workspace))
                for pattern in args.only
            )
        ]

    records: list[dict[str, object]] = []
    for archive in selected:
        relative = archive.relative_to(workspace)
        size = archive.stat().st_size
        record: dict[str, object] = {
            "archive": str(relative),
            "bytes": size,
            "status": "listed",
        }
        if size > args.max_bytes:
            record["status"] = "skipped_size_limit"
            records.append(record)
            continue
        members, list_error = list_members(archive)
        record["members"] = len(members)
        if list_error:
            record["status"] = "unsupported_or_damaged"
            record["error"] = list_error
            records.append(record)
            continue
        unsafe = [
            {"member": member, "reason": reason}
            for member in members
            if (reason := unsafe_member(member)) is not None
        ]
        if unsafe:
            record["status"] = "rejected_unsafe_paths"
            record["unsafe"] = unsafe[:20]
            records.append(record)
            continue

        digest = hash_file(archive)
        destination = output / destination_name(relative, digest)
        record["sha256"] = digest
        record["destination"] = str(destination.relative_to(workspace))
        if destination.exists():
            record["status"] = "already_extracted"
            if archive.name.lower().endswith(".unitypackage"):
                materialized, materialize_error = materialize_unitypackage(
                    destination
                )
                if materialize_error:
                    record["status"] = "materialize_failed"
                    record["error"] = materialize_error
                else:
                    record["materialized"] = materialized
        elif args.extract:
            success, error, materialized = extract_archive(
                archive,
                destination,
                str(relative),
                digest,
            )
            record["status"] = "extracted" if success else "extract_failed"
            if error:
                record["error"] = error
            if materialized is not None:
                record["materialized"] = materialized
        else:
            record["status"] = "ready"
        records.append(record)

    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    summary: dict[str, int] = {}
    for record in records:
        status = str(record["status"])
        summary[status] = summary.get(status, 0) + 1
    report = {
        "workspace": str(workspace),
        "output": str(output),
        "extract": args.extract,
        "summary": summary,
        "archives": records,
    }
    args.manifest.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(summary, ensure_ascii=False))
    return 1 if (
        summary.get("rejected_unsafe_paths")
        or summary.get("extract_failed")
        or summary.get("materialize_failed")
    ) else 0


if __name__ == "__main__":
    raise SystemExit(main())
