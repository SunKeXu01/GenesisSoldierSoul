#!/usr/bin/env python3
"""Recover a contiguous, structurally framed PNG/DDS prefix from private REZ."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import struct
import tempfile
import zlib
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import BinaryIO

from recover_private_rez import CHUNK_SIZE, entropy, parse_rez_header, sha256_file
from rez_extract import RezError


PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
MAXIMUM_PNG_CHUNK_BYTES = 128 * 1024 * 1024
MAXIMUM_PNG_CHUNKS = 1_000_000
DDS_MAGIC = b"DDS "
DDS_HEADER_BYTES = 128


def parse_png(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse one PNG at an exact offset and require complete CRC-valid framing."""
    if offset < 0 or offset + len(PNG_SIGNATURE) > region_end:
        raise RezError(f"truncated PNG signature at {offset}")
    stream.seek(offset)
    signature = stream.read(len(PNG_SIGNATURE))
    if signature != PNG_SIGNATURE:
        raise RezError(f"PNG signature absent at exact offset {offset}")
    digest = hashlib.sha256(signature)
    chunk_counts: Counter[str] = Counter()
    width = height = None
    saw_idat = False
    chunk_count = 0
    while True:
        chunk_offset = stream.tell()
        header = stream.read(8)
        if len(header) != 8:
            raise RezError(f"truncated PNG chunk header at {chunk_offset}")
        length, raw_type = struct.unpack(">I4s", header)
        if length > MAXIMUM_PNG_CHUNK_BYTES:
            raise RezError(f"PNG chunk too large at {chunk_offset}: {length}")
        if stream.tell() + length + 4 > region_end:
            raise RezError(f"PNG chunk crosses REZ data boundary at {chunk_offset}")
        data = stream.read(length)
        raw_crc = stream.read(4)
        expected_crc = zlib.crc32(raw_type + data) & 0xFFFFFFFF
        actual_crc = struct.unpack(">I", raw_crc)[0]
        if actual_crc != expected_crc:
            raise RezError(
                f"PNG CRC mismatch at {chunk_offset}: expected {expected_crc:08x}, "
                f"got {actual_crc:08x}"
            )
        try:
            chunk_type = raw_type.decode("ascii")
        except UnicodeDecodeError as error:
            raise RezError(f"non-ASCII PNG chunk type at {chunk_offset}") from error
        if not chunk_type.isalpha():
            raise RezError(f"invalid PNG chunk type at {chunk_offset}: {raw_type!r}")
        digest.update(header)
        digest.update(data)
        digest.update(raw_crc)
        chunk_counts[chunk_type] += 1
        chunk_count += 1
        if chunk_count > MAXIMUM_PNG_CHUNKS:
            raise RezError("PNG chunk count exceeds safety limit")
        if chunk_count == 1:
            if raw_type != b"IHDR" or length != 13:
                raise RezError(f"PNG does not begin with a 13-byte IHDR at {chunk_offset}")
            width, height = struct.unpack_from(">II", data, 0)
            if width == 0 or height == 0:
                raise RezError(f"PNG has zero dimension at {chunk_offset}")
        elif raw_type == b"IHDR":
            raise RezError(f"duplicate PNG IHDR at {chunk_offset}")
        if raw_type == b"IDAT":
            saw_idat = True
        if raw_type == b"IEND":
            if length != 0 or not saw_idat:
                raise RezError(f"invalid PNG IEND/IDAT structure at {chunk_offset}")
            end = stream.tell()
            return {
                "offset": offset,
                "bytes": end - offset,
                "sha256": digest.hexdigest(),
                "width": width,
                "height": height,
                "chunk_count": chunk_count,
                "chunk_counts": dict(sorted(chunk_counts.items())),
            }


def parse_dds(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse a legacy DDS whose exact payload size is derivable from its header."""
    if offset < 0 or offset + DDS_HEADER_BYTES > region_end:
        raise RezError(f"truncated DDS header at {offset}")
    stream.seek(offset)
    header = stream.read(DDS_HEADER_BYTES)
    if header[:4] != DDS_MAGIC:
        raise RezError(f"DDS signature absent at exact offset {offset}")
    size, flags, height, width, pitch, depth, mip_count = struct.unpack_from(
        "<7I", header, 4
    )
    pixel_format_size, pixel_format_flags = struct.unpack_from("<II", header, 76)
    fourcc = header[84:88]
    rgb_bits = struct.unpack_from("<I", header, 88)[0]
    caps, caps2 = struct.unpack_from("<II", header, 108)
    if size != 124 or pixel_format_size != 32 or width == 0 or height == 0:
        raise RezError(f"invalid DDS header at {offset}")
    if fourcc in {b"DXT1", b"ATI1", b"BC4U", b"BC4S"}:
        block_bytes: int | None = 8
    elif fourcc in {b"DXT2", b"DXT3", b"DXT4", b"DXT5", b"ATI2", b"BC5U", b"BC5S"}:
        block_bytes = 16
    else:
        raise RezError(
            f"unsupported DDS pixel format at {offset}: "
            f"fourcc={fourcc!r} rgb_bits={rgb_bits} flags=0x{pixel_format_flags:x}"
        )
    levels = max(1, mip_count)
    faces = 6 if caps2 & 0x200 else 1
    current_width = width
    current_height = height
    current_depth = max(1, depth)
    payload_bytes = 0
    for _ in range(levels):
        payload_bytes += (
            max(1, (current_width + 3) // 4)
            * max(1, (current_height + 3) // 4)
            * block_bytes
            * current_depth
        )
        current_width = max(1, current_width // 2)
        current_height = max(1, current_height // 2)
        current_depth = max(1, current_depth // 2)
    payload_bytes *= faces
    total_bytes = DDS_HEADER_BYTES + payload_bytes
    if offset + total_bytes > region_end:
        raise RezError(f"DDS payload crosses REZ data boundary at {offset}")
    stream.seek(offset)
    digest = hashlib.sha256()
    remaining = total_bytes
    while remaining:
        data = stream.read(min(CHUNK_SIZE, remaining))
        if not data:
            raise RezError(f"truncated DDS payload at {offset}")
        digest.update(data)
        remaining -= len(data)
    return {
        "offset": offset,
        "bytes": total_bytes,
        "sha256": digest.hexdigest(),
        "width": width,
        "height": height,
        "depth": depth,
        "mip_count": levels,
        "faces": faces,
        "fourcc": fourcc.decode("ascii", errors="replace").rstrip("\0"),
        "rgb_bits": rgb_bits,
        "flags": f"0x{flags:x}",
        "caps": f"0x{caps:x}",
        "caps2": f"0x{caps2:x}",
        "pitch_or_linear_size": pitch,
    }


def copy_verified_region(
    source: Path, offset: int, size: int, expected_sha256: str, destination: Path
) -> str:
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        if (
            not destination.is_file()
            or destination.stat().st_size != size
            or sha256_file(destination) != expected_sha256
        ):
            raise RezError(f"existing PNG prefix output differs: {destination}")
        return "verified_existing"
    with source.open("rb") as input_stream, tempfile.NamedTemporaryFile(
        dir=destination.parent, prefix=".partial-", delete=False
    ) as output_stream:
        temporary = Path(output_stream.name)
        input_stream.seek(offset)
        remaining = size
        digest = hashlib.sha256()
        try:
            while remaining:
                data = input_stream.read(min(CHUNK_SIZE, remaining))
                if not data:
                    raise RezError(f"short source read at {offset}")
                output_stream.write(data)
                digest.update(data)
                remaining -= len(data)
        except Exception:
            temporary.unlink(missing_ok=True)
            raise
    if digest.hexdigest() != expected_sha256:
        temporary.unlink(missing_ok=True)
        raise RezError(f"copied PNG hash mismatch at {offset}")
    os.replace(temporary, destination)
    return "recovered"


def recover_framed_prefix(
    source: Path, source_sha256: str, region_end: int, output: Path
) -> dict[str, object]:
    base = output / "private-rez-png-prefix" / f"{source.stem}__{source_sha256[:12]}"
    records: list[dict[str, object]] = []
    outputs: list[dict[str, object]] = []
    position = parse_rez_header(source)["data_offset"]
    with source.open("rb") as stream:
        while position + 4 <= region_end:
            stream.seek(position)
            signature = stream.read(len(PNG_SIGNATURE))
            if signature == PNG_SIGNATURE:
                record = parse_png(stream, position, region_end)
                kind = "png"
                representation = "private_rez_crc_valid_png_frame"
            elif signature[:4] == DDS_MAGIC:
                record = parse_dds(stream, position, region_end)
                kind = "dds"
                representation = "private_rez_header_sized_dds_frame"
            else:
                break
            destination = base / f"{kind}-{len(records):05d}.{kind}"
            status = copy_verified_region(
                source,
                int(record["offset"]),
                int(record["bytes"]),
                str(record["sha256"]),
                destination,
            )
            output_record = {
                "path": str(destination.relative_to(output)),
                "bytes": record["bytes"],
                "sha256": record["sha256"],
                "representation": representation,
                "status": status,
            }
            record.update(
                {"index": len(records), "kind": kind, "status": status, "output": output_record}
            )
            records.append(record)
            outputs.append(output_record)
            position += int(record["bytes"])
    if not records:
        raise RezError(f"no supported framed prefix at REZ data offset: {source}")
    with source.open("rb") as stream:
        stream.seek(position)
        sample = stream.read(min(4096, region_end - position))
    return {
        "resources": records,
        "outputs": outputs,
        "trailing_region": {
            "offset": position,
            "bytes": region_end - position,
            "prefix_hex": sample[:32].hex(),
            "sample_entropy": entropy(sample),
            "status": "non_png_suffix_preserved",
            "interpretation": (
                "recovery stopped at the first exact unsupported byte; no signature search "
                "or carving was performed"
            ),
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--root-index", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    args = parser.parse_args()
    workspace = args.workspace.resolve()
    output = args.output.resolve()
    index = json.loads(args.root_index.read_text(encoding="utf-8"))
    archives: list[dict[str, object]] = []
    all_outputs: list[dict[str, object]] = []
    for sample in index["samples"]:
        if sample["sample_type"] != "lithtech_rez":
            continue
        source = workspace / sample["path"]
        header = parse_rez_header(source)
        with source.open("rb") as stream:
            stream.seek(header["data_offset"])
            prefix = stream.read(len(PNG_SIGNATURE))
        if prefix != PNG_SIGNATURE:
            continue
        actual_sha256 = sha256_file(source)
        if actual_sha256 != sample["sha256"]:
            raise RezError(f"source hash mismatch: {sample['path']}")
        recovered = recover_framed_prefix(
            source, actual_sha256, header["root_offset"], output
        )
        record = {
            "source": sample["path"],
            "source_sha256": actual_sha256,
            "source_bytes": sample["bytes"],
            **header,
            "status": "contiguous_framed_prefix_recovered",
            **recovered,
        }
        archives.append(record)
        all_outputs.extend(recovered["outputs"])
    manifest = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "tool": "tools/recover_private_rez_framed_prefix.py",
        "tool_version": "1",
        "workspace": str(workspace),
        "root_inventory_sha256": index["inventory_sha256"],
        "safety": {
            "source_mode": "read_only",
            "unknown_executable_run": False,
            "sequential_from_data_offset_only": True,
            "signature_search_or_carving": False,
            "png_crc_required": True,
            "dds_header_sized_payload_required": True,
            "outputs_isolated_by_source_hash": True,
        },
        "archives": archives,
        "outputs": all_outputs,
        "summary": {
            "archives": len(archives),
            "png_images": sum(
                item["representation"] == "private_rez_crc_valid_png_frame"
                for item in all_outputs
            ),
            "png_bytes": sum(
                int(item["bytes"])
                for item in all_outputs
                if item["representation"] == "private_rez_crc_valid_png_frame"
            ),
            "dds_images": sum(
                item["representation"] == "private_rez_header_sized_dds_frame"
                for item in all_outputs
            ),
            "dds_bytes": sum(
                int(item["bytes"])
                for item in all_outputs
                if item["representation"] == "private_rez_header_sized_dds_frame"
            ),
            "trailing_bytes_preserved": sum(
                int(item["trailing_region"]["bytes"]) for item in archives
            ),
        },
    }
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps(manifest["summary"], ensure_ascii=False))
    return 0 if archives else 1


if __name__ == "__main__":
    raise SystemExit(main())
