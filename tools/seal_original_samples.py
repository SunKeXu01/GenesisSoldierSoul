#!/usr/bin/env python3
"""Create a read-only APFS clone vault for original workspace samples.

The command never changes source files. It refuses a byte-copy fallback: if the
filesystem cannot create copy-on-write clones, it exits instead of consuming the
full logical size of the source collection.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
from datetime import datetime, timezone
from pathlib import Path


VAULT_NAME = ".original-samples-vault"


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def safe_relative(value: str) -> Path:
    relative = Path(value)
    if relative.is_absolute() or not relative.parts or any(part in {"", ".", ".."} for part in relative.parts):
        raise ValueError(f"unsafe inventory path: {value!r}")
    return relative


def within(path: Path, parent: Path) -> bool:
    try:
        path.resolve().relative_to(parent.resolve())
        return True
    except ValueError:
        return False


def make_directories_writable(root: Path) -> None:
    if not root.exists():
        return
    for folder, dirs, _ in os.walk(root):
        os.chmod(folder, 0o755)
        for name in dirs:
            os.chmod(Path(folder) / name, 0o755)


def seal_directories(root: Path) -> None:
    folders = [Path(folder) for folder, _, _ in os.walk(root)]
    for folder in sorted(folders, key=lambda item: len(item.parts), reverse=True):
        os.chmod(folder, 0o555)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--inventory", type=Path, required=True)
    parser.add_argument("--vault", type=Path)
    args = parser.parse_args()

    workspace = args.workspace.resolve()
    inventory_path = args.inventory.resolve()
    vault = (args.vault or workspace / VAULT_NAME).resolve()
    if vault.name != VAULT_NAME or vault.parent != workspace or not within(vault, workspace):
        raise SystemExit(f"vault must be {VAULT_NAME!r} directly inside the workspace")
    if not within(inventory_path, workspace):
        raise SystemExit("inventory must be inside the workspace")

    inventory = json.loads(inventory_path.read_text(encoding="utf-8"))
    selected = [
        record for record in inventory.get("samples", [])
        if record.get("kind") == "file" and record.get("provenance_class") == "workspace_original"
    ]
    make_directories_writable(vault)
    files_root = vault / "files"
    files_root.mkdir(parents=True, exist_ok=True)

    sealed: list[dict[str, object]] = []
    for record in selected:
        relative = safe_relative(str(record["path"]))
        source = workspace / relative
        destination = files_root / relative
        if source.is_symlink() or not source.is_file() or not within(source, workspace):
            raise SystemExit(f"source is not a safe regular file: {relative}")
        if source.stat().st_size != int(record["bytes"]):
            raise SystemExit(f"source size changed since inventory: {relative}")
        if sha256_file(source) != record["sha256"]:
            raise SystemExit(f"source hash changed since inventory: {relative}")
        destination.parent.mkdir(parents=True, exist_ok=True)
        status = "verified_existing_clone"
        if destination.exists():
            if destination.is_symlink() or not destination.is_file():
                raise SystemExit(f"unsafe existing vault entry: {destination}")
            if destination.stat().st_size != int(record["bytes"]) or sha256_file(destination) != record["sha256"]:
                raise SystemExit(f"existing vault entry differs from inventory: {relative}")
        else:
            result = subprocess.run(
                ["cp", "-c", str(source), str(destination)],
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
            )
            if result.returncode != 0:
                raise SystemExit(
                    "copy-on-write clone failed; no byte-copy fallback was attempted: "
                    + result.stderr.strip()
                )
            if sha256_file(destination) != record["sha256"]:
                raise SystemExit(f"clone verification failed: {relative}")
            status = "cloned_and_verified"
        os.chmod(destination, 0o444)
        sealed.append({
            "source": str(relative),
            "vault_path": str(destination.relative_to(workspace)),
            "bytes": record["bytes"],
            "sha256": record["sha256"],
            "mode": "444",
            "status": status,
        })

    manifest = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "workspace": str(workspace),
        "inventory": str(inventory_path.relative_to(workspace)),
        "inventory_sha256": inventory.get("inventory_sha256"),
        "method": "APFS copy-on-write clone via cp -c; no byte-copy fallback",
        "source_files_modified": False,
        "samples": len(sealed),
        "logical_bytes": sum(int(item["bytes"]) for item in sealed),
        "files": sealed,
    }
    manifest_path = vault / "manifest.json"
    if manifest_path.exists():
        os.chmod(manifest_path, 0o644)
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.chmod(manifest_path, 0o444)
    readme_path = vault / "README.md"
    if readme_path.exists():
        os.chmod(readme_path, 0o644)
    readme_path.write_text(
        "# 原始样本只读快照库\n\n"
        "本目录由 `GenesisSoldierSoul/tools/seal_original_samples.py` 生成。文件是源样本的 "
        "APFS 写时复制克隆，权限为只读；源文件未改动。完整来源、大小和 SHA-256 见 "
        "`manifest.json`。不要运行其中的 EXE、DLL 或其他未知程序。\n",
        encoding="utf-8",
    )
    os.chmod(readme_path, 0o444)
    seal_directories(vault)
    print(json.dumps({
        "vault": str(vault), "samples": len(sealed),
        "logical_bytes": manifest["logical_bytes"], "manifest": str(manifest_path),
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
