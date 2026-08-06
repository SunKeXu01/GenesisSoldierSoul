#!/usr/bin/env python3
"""Convert legacy Adobe Texture Format payloads to DDS and PNG.

The recovered corpus uses version-0 ATF ``COMPRESSED`` and
``COMPRESSED_ALPHA`` records.  Their desktop DXT blocks are split between an
LZMA-compressed lookup stream and JPEG-XR endpoint images, so the DXT stream
must be reconstructed before it can be placed in a standard DDS container.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import lzma
import struct
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import imagecodecs
from PIL import Image


ATF_COMPRESSED = 2
ATF_COMPRESSED_ALPHA = 4
FORMAT_RECORD_COUNTS = {ATF_COMPRESSED: 8, ATF_COMPRESSED_ALPHA: 10}


class AtfError(ValueError):
    """Raised when an ATF record violates the supported legacy layout."""


@dataclass(frozen=True)
class AtfTexture:
    format: int
    width: int
    height: int
    declared_mip_count: int
    levels: tuple[tuple[bytes, ...], ...]


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def parse_atf(data: bytes) -> AtfTexture:
    if len(data) < 10 or data[:3] != b"ATF":
        raise AtfError("missing legacy ATF header")
    declared_length = int.from_bytes(data[3:6], "big")
    if declared_length != len(data) - 6:
        raise AtfError(
            f"ATF body length mismatch: declared {declared_length}, actual {len(data) - 6}"
        )
    texture_type = data[6]
    if texture_type & 0x80:
        raise AtfError("cube-map ATF is not supported by this converter")
    texture_format = texture_type & 0x7F
    record_count = FORMAT_RECORD_COUNTS.get(texture_format)
    if record_count is None:
        raise AtfError(f"unsupported legacy ATF format {texture_format}")
    width_power, height_power, mip_count = data[7], data[8], data[9]
    if width_power > 11 or height_power > 11 or not 1 <= mip_count <= 12:
        raise AtfError("invalid ATF dimensions or mip count")

    offset = 10
    levels: list[tuple[bytes, ...]] = []
    for _ in range(mip_count):
        records = []
        for _ in range(record_count):
            if offset + 3 > len(data):
                raise AtfError("truncated ATF record length")
            length = int.from_bytes(data[offset : offset + 3], "big")
            offset += 3
            end = offset + length
            if end > len(data):
                raise AtfError("truncated ATF record payload")
            records.append(data[offset:end])
            offset = end
        levels.append(tuple(records))
    if offset != len(data):
        raise AtfError(f"ATF has {len(data) - offset} trailing bytes")
    return AtfTexture(
        format=texture_format,
        width=1 << width_power,
        height=1 << height_power,
        declared_mip_count=mip_count,
        levels=tuple(levels),
    )


def populated_levels(texture: AtfTexture) -> tuple[tuple[bytes, ...], ...]:
    populated = []
    encountered_empty = False
    for records in texture.levels:
        is_populated = any(records)
        if is_populated and encountered_empty:
            raise AtfError("non-contiguous populated ATF mip levels")
        if is_populated:
            required = 2 if texture.format == ATF_COMPRESSED else 4
            if not all(records[index] for index in range(required)):
                raise AtfError("incomplete desktop DXT records")
            populated.append(records)
        else:
            encountered_empty = True
    if not populated:
        raise AtfError("ATF contains no populated mip levels")
    return tuple(populated)


def decompress_atf_lzma(payload: bytes, expected: int) -> bytes:
    if len(payload) < 5 or payload[0] != 0x5D:
        raise AtfError("invalid ATF LZMA stream")
    # Legacy ATF omits the eight-byte uncompressed-size field from an LZMA-alone
    # header.  The stream may also omit an EOS marker; LZMADecompressor accepts
    # that bounded representation while lzma.decompress intentionally does not.
    decoder = lzma.LZMADecompressor(format=lzma.FORMAT_ALONE)
    try:
        decoded = decoder.decompress(payload[:5] + (b"\xFF" * 8) + payload[5:])
    except lzma.LZMAError as error:
        raise AtfError(f"invalid ATF LZMA payload: {error}") from error
    if len(decoded) < expected:
        raise AtfError(f"short ATF LZMA output: expected {expected}, got {len(decoded)}")
    if any(decoded[expected:]):
        raise AtfError(f"non-zero ATF LZMA padding: {len(decoded) - expected} bytes")
    return decoded[:expected]


def decode_jpegxr(payload: bytes, expected_height: int, expected_width: int, grayscale: bool):
    if not payload.startswith(b"II\xBC\x01"):
        raise AtfError("invalid ATF JPEG-XR stream")
    try:
        image = imagecodecs.jpegxr_decode(payload)
    except Exception as error:
        raise AtfError(f"JPEG-XR decode failed: {error}") from error
    if image.shape[:2] != (expected_height, expected_width):
        raise AtfError(
            f"JPEG-XR dimensions mismatch: expected {(expected_height, expected_width)}, "
            f"got {image.shape[:2]}"
        )
    if grayscale:
        if len(image.shape) != 2:
            raise AtfError(f"expected grayscale JPEG-XR, got shape {image.shape}")
    elif len(image.shape) != 3 or image.shape[2] < 3:
        raise AtfError(f"expected RGB JPEG-XR, got shape {image.shape}")
    return image


def rgb565(pixel: Any) -> bytes:
    red, green, blue = (int(pixel[index]) for index in range(3))
    value = (
        (((red * 31 + 127) // 255) << 11)
        | (((green * 63 + 127) // 255) << 5)
        | ((blue * 31 + 127) // 255)
    )
    return struct.pack("<H", value)


def reconstruct_level(
    texture_format: int, records: tuple[bytes, ...], width: int, height: int
) -> bytes:
    block_columns = max(1, (width + 3) // 4)
    block_rows = max(1, (height + 3) // 4)
    block_count = block_columns * block_rows
    endpoint_height = block_rows * 2

    if texture_format == ATF_COMPRESSED:
        lookup = decompress_atf_lzma(records[0], block_count * 4)
        endpoints = decode_jpegxr(records[1], endpoint_height, block_columns, False)
        output = bytearray()
        for y in range(block_rows):
            for x in range(block_columns):
                block = y * block_columns + x
                output.extend(rgb565(endpoints[y, x]))
                output.extend(rgb565(endpoints[block_rows + y, x]))
                output.extend(lookup[block * 4 : block * 4 + 4])
        return bytes(output)

    alpha_lookup = decompress_atf_lzma(records[0], block_count * 6)
    alpha_endpoints = decode_jpegxr(records[1], endpoint_height, block_columns, True)
    color_lookup = decompress_atf_lzma(records[2], block_count * 4)
    color_endpoints = decode_jpegxr(records[3], endpoint_height, block_columns, False)
    output = bytearray()
    for y in range(block_rows):
        for x in range(block_columns):
            block = y * block_columns + x
            output.extend(
                (int(alpha_endpoints[y, x]), int(alpha_endpoints[block_rows + y, x]))
            )
            output.extend(alpha_lookup[block * 6 : block * 6 + 6])
            output.extend(rgb565(color_endpoints[y, x]))
            output.extend(rgb565(color_endpoints[block_rows + y, x]))
            output.extend(color_lookup[block * 4 : block * 4 + 4])
    return bytes(output)


def make_dds(texture: AtfTexture) -> tuple[bytes, int]:
    levels = populated_levels(texture)
    payloads = []
    for level, records in enumerate(levels):
        width = max(1, texture.width >> level)
        height = max(1, texture.height >> level)
        payloads.append(reconstruct_level(texture.format, records, width, height))

    fourcc = b"DXT1" if texture.format == ATF_COMPRESSED else b"DXT5"
    flags = 0x1 | 0x2 | 0x4 | 0x1000 | 0x80000
    caps = 0x1000
    if len(payloads) > 1:
        flags |= 0x20000
        caps |= 0x8 | 0x400000
    header = b"DDS " + struct.pack(
        "<I6I11I",
        124,
        flags,
        texture.height,
        texture.width,
        len(payloads[0]),
        0,
        len(payloads),
        *([0] * 11),
    )
    header += struct.pack("<II4s5I", 32, 0x4, fourcc, 0, 0, 0, 0, 0)
    header += struct.pack("<5I", caps, 0, 0, 0, 0)
    if len(header) != 128:
        raise AssertionError("invalid DDS header size")
    return header + b"".join(payloads), len(payloads)


def dds_to_png(dds: bytes, width: int, height: int) -> bytes:
    try:
        with Image.open(io.BytesIO(dds)) as image:
            image.load()
            if image.size != (width, height):
                raise AtfError(f"DDS dimensions mismatch: {image.size}")
            converted = image.convert("RGBA")
            output = io.BytesIO()
            converted.save(output, format="PNG", optimize=False)
    except AtfError:
        raise
    except Exception as error:
        raise AtfError(f"DDS verification/PNG conversion failed: {error}") from error
    return output.getvalue()


def write_verified(path: Path, data: bytes) -> str:
    digest = sha256_bytes(data)
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        if not path.is_file() or path.stat().st_size != len(data) or sha256_file(path) != digest:
            raise AtfError(f"existing output differs: {path}")
        return "verified_existing"
    path.write_bytes(data)
    if sha256_file(path) != digest:
        raise IOError(f"output verification failed: {path}")
    return "converted"


def output_item(path: Path, output_root: Path, data: bytes, representation: str) -> dict[str, Any]:
    status = write_verified(path, data)
    return {
        "path": str(path.relative_to(output_root)),
        "bytes": len(data),
        "sha256": sha256_bytes(data),
        "representation": representation,
        "status": status,
    }


def convert_one(source: Path, input_root: Path, output_root: Path, repo: Path) -> dict[str, Any]:
    relative = source.relative_to(input_root)
    record: dict[str, Any] = {
        "source": {
            "path": str(source.relative_to(repo)),
            "bytes": source.stat().st_size,
            "sha256": sha256_file(source),
        },
        "outputs": [],
    }
    try:
        texture = parse_atf(source.read_bytes())
        dds, mip_count = make_dds(texture)
        png = dds_to_png(dds, texture.width, texture.height)
        stem = output_root / relative.with_suffix("")
        record["outputs"] = [
            output_item(stem.with_suffix(".dds"), output_root, dds, "dds_dxt1" if texture.format == 2 else "dds_dxt5"),
            output_item(stem.with_suffix(".png"), output_root, png, "png_rgba"),
        ]
        record.update(
            {
                "status": "converted",
                "atf_format": texture.format,
                "atf_format_name": "compressed" if texture.format == 2 else "compressed_alpha",
                "width": texture.width,
                "height": texture.height,
                "declared_mip_count": texture.declared_mip_count,
                "converted_mip_count": mip_count,
            }
        )
    except Exception as error:
        record.update({"status": "failed", "error": f"{type(error).__name__}: {error}"})
    return record


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    args = parser.parse_args()

    repo = Path(__file__).resolve().parent.parent
    input_root = args.input.resolve()
    output_root = args.output.resolve()
    sources = sorted(input_root.rglob("*.atf"))
    records = [convert_one(source, input_root, output_root, repo) for source in sources]
    converted = [record for record in records if record["status"] == "converted"]
    outputs = [item for record in converted for item in record["outputs"]]
    format_counts: dict[str, int] = {}
    for record in converted:
        name = record["atf_format_name"]
        format_counts[name] = format_counts.get(name, 0) + 1
    manifest = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "tool": "tools/convert_atf_textures.py",
        "tool_version": "1",
        "parameters": {
            "input": str(input_root),
            "output": str(output_root),
            "source_mode": "read_only",
            "dds_formats": ["DXT1", "DXT5"],
            "png_mode": "RGBA",
            "imagecodecs_version": imagecodecs.__version__,
        },
        "summary": {
            "sources": len(sources),
            "converted": len(converted),
            "failed": len(records) - len(converted),
            "outputs": len(outputs),
            "output_bytes": sum(item["bytes"] for item in outputs),
            "format_counts": dict(sorted(format_counts.items())),
            "declared_mip_levels": sum(record.get("declared_mip_count", 0) for record in converted),
            "converted_mip_levels": sum(record.get("converted_mip_count", 0) for record in converted),
        },
        "records": records,
    }
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest["summary"], ensure_ascii=False))
    return 1 if manifest["summary"]["failed"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
