#!/usr/bin/env python3
"""Convert strictly identified private-REZ texture streams to PNG."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import struct
from concurrent.futures import ProcessPoolExecutor
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from PIL import Image

from audit_special_formats import decode_dtx


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def identify_texture(prefix: bytes, size: int) -> str | None:
    if len(prefix) >= 28:
        resource_type, version = struct.unpack_from("<ii", prefix)
        width, height, mipmaps, _sections = struct.unpack_from("<HHHH", prefix, 8)
        if (
            resource_type in {0, 1}
            and version == -5
            and 0 < width <= 16384
            and 0 < height <= 16384
            and 0 < mipmaps <= 32
            and prefix[26] in {3, 4, 5, 6}
        ):
            return "dtx"
    if prefix.startswith(b"\x00\x00\x02\x00" + bytes(8)) and len(prefix) >= 18:
        width, height = struct.unpack_from("<HH", prefix, 12)
        bits = prefix[16]
        if bits in {16, 24, 32} and width and height:
            for header_size in (18, 44):
                if size == header_size + width * height * (bits // 8):
                    return "raw_texture"
    return None


def decode_raw_texture(data: bytes) -> tuple[bytes, dict[str, Any]]:
    if len(data) < 18 or not data.startswith(b"\x00\x00\x02\x00" + bytes(8)):
        raise ValueError("not a recognized private raw texture")
    width, height = struct.unpack_from("<HH", data, 12)
    bits = data[16]
    header_sizes = [
        header_size
        for header_size in (18, 44)
        if bits in {16, 24, 32}
        and len(data) == header_size + width * height * (bits // 8)
    ]
    if len(header_sizes) != 1:
        raise ValueError(
            f"raw texture size is ambiguous or invalid: {width}x{height}x{bits}, {len(data)}"
        )
    header_size = header_sizes[0]
    pixels = data[header_size:]
    if bits == 24:
        image = Image.frombytes("RGB", (width, height), pixels, "raw", "BGR")
        pixel_format = "BGR888"
    elif bits == 32:
        image = Image.frombytes("RGBA", (width, height), pixels, "raw", "BGRA")
        pixel_format = "BGRA8888"
    else:
        raise ValueError("16-bit private raw texture layout is preserved but not guessed")
    output = io.BytesIO()
    image.save(output, format="PNG")
    return output.getvalue(), {
        "width": width,
        "height": height,
        "bits_per_pixel": bits,
        "header_bytes": header_size,
        "decoded_pixel_format": pixel_format,
    }


def convert_one(task: dict[str, Any]) -> dict[str, Any]:
    input_path = Path(task["input_path"])
    output_path = Path(task["output_path"])
    data = input_path.read_bytes()
    actual_input_sha256 = sha256_bytes(data)
    if len(data) != task["input_bytes"] or actual_input_sha256 != task["input_sha256"]:
        raise ValueError(f"private REZ texture input provenance mismatch: {input_path}")
    if task["kind"] == "dtx":
        png, details = decode_dtx(data)
        representation = "private_rez_dtx_to_png"
    else:
        png, details = decode_raw_texture(data)
        representation = "private_rez_raw_texture_to_png"
    digest = sha256_bytes(png)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    if output_path.exists():
        if (
            not output_path.is_file()
            or output_path.stat().st_size != len(png)
            or sha256_file(output_path) != digest
        ):
            raise ValueError(f"existing private REZ PNG differs: {output_path}")
        status = "verified_existing"
    else:
        output_path.write_bytes(png)
        if sha256_file(output_path) != digest:
            raise IOError(f"private REZ PNG verification failed: {output_path}")
        status = "converted"
    return {
        "source_archive": task["source_archive"],
        "source_archive_sha256": task["source_archive_sha256"],
        "stream_index": task["stream_index"],
        "input": {
            "path": task["input_label"],
            "bytes": len(data),
            "sha256": actual_input_sha256,
        },
        "kind": task["kind"],
        "details": details,
        "output": {
            "path": task["output_relative"],
            "bytes": len(png),
            "sha256": digest,
            "representation": representation,
            "status": status,
        },
        "status": status,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--recovery-manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--workers", type=int, default=max(1, min(8, os.cpu_count() or 1)))
    args = parser.parse_args()

    workspace = args.workspace.resolve()
    output = args.output.resolve()
    recovery = json.loads(args.recovery_manifest.read_text(encoding="utf-8"))
    tasks = []
    preserved_unsupported = []
    for archive in recovery["archives"]:
        source_key = f"{Path(archive['source']).stem}__{archive['source_sha256'][:12]}"
        for stream in archive.get("streams") or []:
            prefix = bytes.fromhex(stream["prefix_hex"])
            kind = identify_texture(prefix, stream["decoded_bytes"])
            if kind is None:
                continue
            if kind == "raw_texture" and prefix[16] == 16:
                preserved_unsupported.append(
                    {
                        "source_archive": archive["source"],
                        "stream_index": stream["stream_index"],
                        "bytes": stream["decoded_bytes"],
                        "sha256": stream["sha256"],
                        "reason": "16-bit channel packing is not proven",
                    }
                )
                continue
            if "output" in stream:
                input_path = output / stream["output"]["path"]
                input_label = stream["output"]["path"]
            elif stream["loose_matches"]:
                input_path = workspace / stream["loose_matches"][0]
                input_label = stream["loose_matches"][0]
            else:
                raise ValueError(
                    f"recovered stream has no materialized or loose input: {archive['source']}:{stream['stream_index']}"
                )
            output_relative = str(
                Path("private-rez-media")
                / source_key
                / f"stream-{stream['stream_index']:05d}.png"
            )
            tasks.append(
                {
                    "kind": kind,
                    "source_archive": archive["source"],
                    "source_archive_sha256": archive["source_sha256"],
                    "stream_index": stream["stream_index"],
                    "input_path": str(input_path),
                    "input_label": input_label,
                    "input_bytes": stream["decoded_bytes"],
                    "input_sha256": stream["sha256"],
                    "output_path": str(output / output_relative),
                    "output_relative": output_relative,
                }
            )
    tasks.sort(key=lambda item: (item["source_archive"], item["stream_index"]))
    with ProcessPoolExecutor(max_workers=args.workers) as executor:
        records = list(executor.map(convert_one, tasks, chunksize=8))
    kind_counts = Counter(item["kind"] for item in records)
    manifest = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "tool": "tools/convert_private_rez_media.py",
        "tool_version": "1",
        "parameters": {
            "workspace": str(workspace),
            "recovery_manifest": str(args.recovery_manifest),
            "recovery_manifest_sha256": sha256_file(args.recovery_manifest),
            "output": str(output),
            "workers": args.workers,
        },
        "verification": "input byte count + SHA-256; exact texture header/payload invariant; output PNG SHA-256 readback",
        "records": records,
        "preserved_unsupported": preserved_unsupported,
        "summary": {
            "candidates": len(tasks),
            "converted_or_verified": len(records),
            "kind_counts": dict(sorted(kind_counts.items())),
            "output_bytes": sum(item["output"]["bytes"] for item in records),
            "preserved_unsupported": len(preserved_unsupported),
            "failures": 0,
        },
    }
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps(manifest["summary"], ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
