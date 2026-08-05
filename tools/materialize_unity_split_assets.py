#!/usr/bin/env python3
"""Reassemble Unity Android ``*.splitN`` data without executing an APK.

Unity can store serialized asset and resource files as numbered chunks inside
``assets/bin/Data``.  This tool verifies that every chunk sequence is complete,
concatenates it into a source-isolated materialized directory, and records the
source package and SHA-256 of the result.  Original chunks are never modified.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import tempfile
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path


SPLIT_PATTERN = re.compile(r"^(?P<base>.+)\.split(?P<index>[0-9]+)$")


def hash_path(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def split_groups(data_dir: Path) -> dict[str, list[tuple[int, Path]]]:
    groups: dict[str, list[tuple[int, Path]]] = defaultdict(list)
    for path in sorted(data_dir.iterdir()):
        if not path.is_file():
            continue
        match = SPLIT_PATTERN.match(path.name)
        if match:
            groups[match.group("base")].append((int(match.group("index")), path))
    return dict(groups)


def validate_sequence(parts: list[tuple[int, Path]]) -> str | None:
    indices = sorted(index for index, _ in parts)
    if not indices:
        return "empty_sequence"
    expected = list(range(indices[-1] + 1))
    if indices != expected:
        missing = sorted(set(expected) - set(indices))
        return "missing_chunks:" + ",".join(str(index) for index in missing[:40])
    return None


def materialize(parts: list[tuple[int, Path]], destination: Path) -> dict[str, object]:
    error = validate_sequence(parts)
    if error:
        return {"status": "incomplete", "error": error}

    destination.parent.mkdir(parents=True, exist_ok=True)
    digest = hashlib.sha256()
    total = 0
    with tempfile.NamedTemporaryFile(
        prefix=destination.name + ".",
        dir=destination.parent,
        delete=False,
    ) as stream:
        staging = Path(stream.name)
        try:
            for _, part in sorted(parts):
                with part.open("rb") as source:
                    for chunk in iter(lambda: source.read(1024 * 1024), b""):
                        stream.write(chunk)
                        digest.update(chunk)
                        total += len(chunk)
        except Exception:
            staging.unlink(missing_ok=True)
            raise

    output_hash = digest.hexdigest()
    if destination.exists():
        existing = hash_path(destination)
        if existing == output_hash:
            staging.unlink()
            return {
                "status": "already_materialized",
                "bytes": total,
                "sha256": output_hash,
            }
    staging.replace(destination)
    return {"status": "materialized", "bytes": total, "sha256": output_hash}


def package_root_for(data_dir: Path, root: Path) -> Path | None:
    for parent in (data_dir, *data_dir.parents):
        if (parent / ".genesis-source.json").is_file():
            return parent
        if parent == root:
            break
    return None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    args = parser.parse_args()
    root = args.root.resolve()

    records: list[dict[str, object]] = []
    for data_dir in sorted(root.glob("**/assets/bin/Data")):
        package_root = package_root_for(data_dir, root)
        if package_root is None:
            continue
        source = json.loads(
            (package_root / ".genesis-source.json").read_text(encoding="utf-8")
        )
        for base, parts in sorted(split_groups(data_dir).items()):
            destination = package_root / "_materialized-unity-data" / base
            result = materialize(parts, destination)
            records.append(
                {
                    "archive": source.get("archive"),
                    "archive_sha256": source.get("sha256"),
                    "split_base": base,
                    "chunks": len(parts),
                    "output": str(destination.relative_to(root)),
                    **result,
                }
            )

    summary: dict[str, int] = {}
    for record in records:
        status = str(record["status"])
        summary[status] = summary.get(status, 0) + 1
    report = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "root": str(root),
        "summary": summary,
        "files": records,
    }
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(summary, ensure_ascii=False))
    return 1 if summary.get("incomplete") else 0


if __name__ == "__main__":
    raise SystemExit(main())
