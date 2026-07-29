#!/usr/bin/env python3
"""Copy a Unity asset and every GUID-addressed dependency into another project.

AssetRipper exports use stable GUID references.  Keeping those GUIDs intact lets us
move a recovered scene under a namespaced folder without rewriting its YAML.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
from collections import deque
from pathlib import Path


GUID_RE = re.compile(rb"guid:\s*([0-9a-fA-F]{32})")
META_GUID_RE = re.compile(rb"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)
TEXT_LIMIT = 64 * 1024 * 1024


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def asset_for_meta(meta: Path) -> Path:
    return meta.with_name(meta.name.removesuffix(".meta"))


def build_guid_index(assets: Path) -> dict[str, Path]:
    result: dict[str, Path] = {}
    for meta in assets.rglob("*.meta"):
        try:
            match = META_GUID_RE.search(meta.read_bytes())
        except OSError:
            continue
        if not match:
            continue
        guid = match.group(1).decode("ascii").lower()
        asset = asset_for_meta(meta)
        if asset.exists():
            result[guid] = asset
    return result


def referenced_guids(path: Path) -> set[str]:
    paths = [path]
    meta = path.with_name(path.name + ".meta")
    if meta.exists():
        paths.append(meta)

    result: set[str] = set()
    for candidate in paths:
        try:
            if candidate.stat().st_size > TEXT_LIMIT:
                continue
            data = candidate.read_bytes()
        except OSError:
            continue
        # Binary assets may contain accidental byte matches. Unity's serialized
        # YAML and importer metadata always use this ASCII token.
        result.update(match.decode("ascii").lower() for match in GUID_RE.findall(data))
    return result


def copy_pair(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    if source.is_dir():
        destination.mkdir(parents=True, exist_ok=True)
    else:
        shutil.copy2(source, destination)
    source_meta = source.with_name(source.name + ".meta")
    if source_meta.exists():
        shutil.copy2(source_meta, destination.with_name(destination.name + ".meta"))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-assets", type=Path, required=True)
    parser.add_argument("--root-asset", type=Path, required=True)
    parser.add_argument("--target-assets", type=Path, required=True)
    parser.add_argument("--namespace", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    source_assets = args.source_assets.resolve()
    root_asset = args.root_asset.resolve()
    target_assets = args.target_assets.resolve()
    if source_assets not in root_asset.parents:
        parser.error("--root-asset must be inside --source-assets")

    source_index = build_guid_index(source_assets)
    target_index = build_guid_index(target_assets)

    pending = deque([root_asset])
    visited: set[Path] = set()
    missing: set[str] = set()
    reused: dict[str, str] = {}
    conflicts: list[dict[str, str]] = []

    while pending:
        asset = pending.popleft()
        if asset in visited:
            continue
        visited.add(asset)
        for guid in referenced_guids(asset):
            source_dep = source_index.get(guid)
            if source_dep is None:
                # Unity built-ins and package references are expected here.
                if guid not in target_index:
                    missing.add(guid)
                continue
            target_dep = target_index.get(guid)
            if target_dep is not None:
                reused[guid] = str(target_dep.relative_to(target_assets))
                if source_dep.is_file() and target_dep.is_file():
                    if sha256(source_dep) != sha256(target_dep):
                        conflicts.append(
                            {
                                "guid": guid,
                                "source": str(source_dep.relative_to(source_assets)),
                                "target": str(target_dep.relative_to(target_assets)),
                            }
                        )
                continue
            pending.append(source_dep)

    copied: list[dict[str, str]] = []
    for source in sorted(visited):
        relative = source.relative_to(source_assets)
        destination = target_assets / args.namespace / relative
        copied.append(
            {
                "source": str(relative),
                "target": str(destination.relative_to(target_assets)),
            }
        )
        if not args.dry_run:
            copy_pair(source, destination)

    manifest = {
        "source_assets": str(source_assets),
        "root_asset": str(root_asset.relative_to(source_assets)),
        "target_assets": str(target_assets),
        "namespace": str(args.namespace),
        "asset_count": len(copied),
        "reused_guid_count": len(reused),
        "missing_guid_count": len(missing),
        "conflict_count": len(conflicts),
        "copied": copied,
        "reused": reused,
        "missing_guids": sorted(missing),
        "conflicts": conflicts,
    }
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )

    print(
        json.dumps(
            {
                "asset_count": len(copied),
                "reused_guid_count": len(reused),
                "missing_guid_count": len(missing),
                "conflict_count": len(conflicts),
                "dry_run": args.dry_run,
                "manifest": str(args.manifest),
            },
            ensure_ascii=False,
        )
    )
    return 2 if conflicts else 0


if __name__ == "__main__":
    raise SystemExit(main())
