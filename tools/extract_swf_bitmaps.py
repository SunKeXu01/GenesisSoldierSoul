#!/usr/bin/env python3
"""Read-only SWF resource extraction without executing ActionScript.

The extractor supports FWS, CWS and ZWS containers.  It decodes bitmap tags to
PNG and preserves vector-shape, font, sprite/timeline and binary/ATF tag bodies
with hashes so unsupported details are never silently discarded.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import lzma
import math
import re
import struct
import zlib
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

from PIL import Image


DEFINE_BITS = 6
DEFINE_BITS_JPEG2 = 21
DEFINE_BITS_JPEG3 = 35
DEFINE_BITS_JPEG4 = 90
DEFINE_BITS_LOSSLESS = 20
DEFINE_BITS_LOSSLESS2 = 36
DEFINE_BINARY_DATA = 87
EXPORT_ASSETS = 56
SYMBOL_CLASS = 76
SHOW_FRAME = 1
FRAME_LABEL = 43
VECTOR_TAGS = {2, 22, 32, 46, 67, 83, 84}
FONT_TAGS = {10, 13, 48, 62, 73, 75, 88, 91}
SPRITE_TAGS = {39}
BITMAP_TAGS = {
    DEFINE_BITS,
    DEFINE_BITS_JPEG2,
    DEFINE_BITS_JPEG3,
    DEFINE_BITS_JPEG4,
    DEFINE_BITS_LOSSLESS,
    DEFINE_BITS_LOSSLESS2,
}
TAG_NAMES = {
    0: "End",
    1: "ShowFrame",
    2: "DefineShape",
    6: "DefineBits",
    10: "DefineFont",
    13: "DefineFontInfo",
    20: "DefineBitsLossless",
    21: "DefineBitsJPEG2",
    22: "DefineShape2",
    32: "DefineShape3",
    35: "DefineBitsJPEG3",
    36: "DefineBitsLossless2",
    39: "DefineSprite",
    43: "FrameLabel",
    46: "DefineMorphShape",
    48: "DefineFont2",
    56: "ExportAssets",
    62: "DefineFontInfo2",
    67: "DefineShape4",
    73: "DefineFontAlignZones",
    75: "DefineFont3",
    76: "SymbolClass",
    83: "DefineShape4",
    84: "DefineMorphShape2",
    87: "DefineBinaryData",
    88: "DefineFontName",
    90: "DefineBitsJPEG4",
    91: "DefineFont4",
}


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def safe_name(value: str) -> str:
    cleaned = re.sub(r"[^0-9A-Za-z\u4e00-\u9fff._-]+", "_", value).strip("._")
    return (cleaned or "swf")[:100]


def write_verified(path: Path, data: bytes) -> str:
    digest = sha256_bytes(data)
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        if not path.is_file() or path.stat().st_size != len(data) or sha256_file(path) != digest:
            raise ValueError(f"existing SWF output differs: {path}")
        return "verified_existing"
    path.write_bytes(data)
    if sha256_file(path) != digest:
        raise IOError(f"SWF output verification failed: {path}")
    return "exported"


def decode_zws(raw: bytes) -> tuple[bytes, dict[str, Any]]:
    if len(raw) < 17:
        raise ValueError("truncated ZWS header")
    declared_length = struct.unpack_from("<I", raw, 4)[0]
    compressed_length = struct.unpack_from("<I", raw, 8)[0]
    prop = raw[12]
    remainder = prop // 9
    lc, lp, pb = prop % 9, remainder % 5, remainder // 5
    dictionary_size = struct.unpack_from("<I", raw, 13)[0]
    filters = [
        {
            "id": lzma.FILTER_LZMA1,
            "dict_size": dictionary_size,
            "lc": lc,
            "lp": lp,
            "pb": pb,
        }
    ]
    decoder = lzma.LZMADecompressor(format=lzma.FORMAT_RAW, filters=filters)
    expected_body = max(0, declared_length - 8)
    body = decoder.decompress(raw[17:], max_length=expected_body)
    if not body:
        raise ValueError("ZWS LZMA stream produced no data")
    result = b"FWS" + raw[3:8] + body
    return result, {
        "compression": "ZWS/LZMA",
        "compressed_length_field": compressed_length,
        "lzma_dictionary_size": dictionary_size,
        "declared_body_bytes": expected_body,
        "actual_body_bytes": len(body),
        "length_matches": len(body) == expected_body,
    }


def read_uncompressed_swf(path: Path) -> tuple[bytes, dict[str, Any]]:
    raw = path.read_bytes()
    if len(raw) < 8:
        raise ValueError(f"{path.name}: truncated SWF header")
    signature = raw[:3]
    declared_length = struct.unpack_from("<I", raw, 4)[0]
    if signature == b"FWS":
        result = raw
        details = {"compression": "FWS/uncompressed"}
    elif signature == b"CWS":
        result = b"FWS" + raw[3:8] + zlib.decompress(raw[8:])
        details = {"compression": "CWS/zlib"}
    elif signature == b"ZWS":
        result, details = decode_zws(raw)
    else:
        raise ValueError(f"{path.name}: unsupported SWF signature {signature!r}")
    details.update(
        {
            "signature": signature.decode("ascii", errors="replace"),
            "version": raw[3],
            "declared_file_length": declared_length,
            "actual_uncompressed_length": len(result),
            "uncompressed_sha256": sha256_bytes(result),
        }
    )
    return result, details


def first_tag_offset(raw: bytes) -> int:
    if len(raw) < 9:
        raise ValueError("truncated SWF RECT")
    nbits = raw[8] >> 3
    rect_size = math.ceil((5 + 4 * nbits) / 8)
    offset = 8 + rect_size + 4
    if offset > len(raw):
        raise ValueError("SWF frame header exceeds file")
    return offset


def iter_tag_stream(raw: bytes, offset: int, end: int | None = None) -> Iterable[tuple[int, bytes, int, int]]:
    end = len(raw) if end is None else min(end, len(raw))
    index = 0
    while offset + 2 <= end:
        tag_offset = offset
        header = struct.unpack_from("<H", raw, offset)[0]
        offset += 2
        code = header >> 6
        length = header & 0x3F
        if length == 0x3F:
            if offset + 4 > end:
                raise ValueError("truncated long SWF tag header")
            length = struct.unpack_from("<I", raw, offset)[0]
            offset += 4
        if length < 0 or offset + length > end:
            raise ValueError(f"SWF tag {code} body exceeds stream")
        body = raw[offset : offset + length]
        yield code, body, tag_offset, index
        offset += length
        index += 1
        if code == 0:
            break


def iter_tags(raw: bytes) -> Iterable[tuple[int, bytes]]:
    for code, body, _, _ in iter_tag_stream(raw, first_tag_offset(raw)):
        yield code, body


def open_embedded_image(data: bytes) -> Image.Image:
    if data.startswith(b"\xff\xd9\xff\xd8"):
        data = data[2:]
    if not data.startswith((b"\xff\xd8", b"\x89PNG", b"GIF8")):
        candidates = [
            position
            for marker in (b"\xff\xd8", b"\x89PNG", b"GIF8")
            if (position := data.find(marker)) >= 0
        ]
        if candidates:
            data = data[min(candidates) :]
    return Image.open(io.BytesIO(data)).convert("RGBA")


def decode_jpeg(body: bytes, tag_code: int, jpeg_tables: bytes | None = None) -> tuple[int, Image.Image]:
    if len(body) < 2:
        raise ValueError("truncated JPEG bitmap tag")
    character_id = struct.unpack_from("<H", body, 0)[0]
    if tag_code in {DEFINE_BITS, DEFINE_BITS_JPEG2}:
        data = body[2:]
        if tag_code == DEFINE_BITS and jpeg_tables:
            data = jpeg_tables.rstrip(b"\xff\xd9") + data.lstrip(b"\xff\xd8")
        return character_id, open_embedded_image(data)
    if len(body) < 6:
        raise ValueError("truncated JPEG alpha tag")
    alpha_offset = struct.unpack_from("<I", body, 2)[0]
    image_start = 6 if tag_code == DEFINE_BITS_JPEG3 else 8
    image_end = image_start + alpha_offset
    if image_end > len(body):
        raise ValueError("JPEG payload exceeds tag body")
    image = open_embedded_image(body[image_start:image_end])
    alpha = zlib.decompress(body[image_end:])
    expected = image.width * image.height
    if len(alpha) < expected:
        raise ValueError(f"alpha channel too short: {len(alpha)} < {expected}")
    image.putalpha(Image.frombytes("L", image.size, alpha[:expected]))
    return character_id, image


def decode_lossless(body: bytes, has_alpha: bool) -> tuple[int, Image.Image]:
    if len(body) < 7:
        raise ValueError("truncated lossless bitmap tag")
    character_id, bitmap_format, width, height = struct.unpack_from("<HBHH", body, 0)
    cursor = 7
    color_table_size = 0
    if bitmap_format == 3:
        if cursor >= len(body):
            raise ValueError("missing lossless palette size")
        color_table_size = body[cursor] + 1
        cursor += 1
    decoded = zlib.decompress(body[cursor:])
    if bitmap_format == 3:
        entry_size = 4 if has_alpha else 3
        palette_bytes = color_table_size * entry_size
        palette_data = decoded[:palette_bytes]
        if len(palette_data) != palette_bytes:
            raise ValueError("truncated lossless palette")
        table = []
        for index in range(color_table_size):
            start = index * entry_size
            blue, green, red = palette_data[start : start + 3]
            alpha = palette_data[start + 3] if has_alpha else 255
            table.append((red, green, blue, alpha))
        stride = (width + 3) & ~3
        pixels = []
        indices = decoded[palette_bytes:]
        for y in range(height):
            row = indices[y * stride : y * stride + width]
            if len(row) != width:
                raise ValueError("truncated lossless index row")
            pixels.extend(table[index] for index in row)
        image = Image.new("RGBA", (width, height))
        image.putdata(pixels)
        return character_id, image
    if bitmap_format == 4:
        stride = ((width * 2) + 3) & ~3
        pixels = bytearray(width * height * 4)
        output = 0
        for y in range(height):
            row = decoded[y * stride : y * stride + width * 2]
            for index in range(0, len(row), 2):
                value = struct.unpack_from("<H", row, index)[0]
                red = ((value >> 10) & 0x1F) * 255 // 31
                green = ((value >> 5) & 0x1F) * 255 // 31
                blue = (value & 0x1F) * 255 // 31
                pixels[output : output + 4] = (red, green, blue, 255)
                output += 4
        return character_id, Image.frombytes("RGBA", (width, height), bytes(pixels))
    if bitmap_format == 5:
        stride = width * 4
        pixels = bytearray(width * height * 4)
        output = 0
        for y in range(height):
            row = decoded[y * stride : (y + 1) * stride]
            if len(row) != stride:
                raise ValueError("truncated lossless ARGB row")
            for index in range(0, len(row), 4):
                alpha_or_padding, red, green, blue = row[index : index + 4]
                pixels[output : output + 4] = (red, green, blue, alpha_or_padding if has_alpha else 255)
                output += 4
        return character_id, Image.frombytes("RGBA", (width, height), bytes(pixels))
    raise ValueError(f"unsupported lossless bitmap format {bitmap_format}")


def character_id(body: bytes) -> int | None:
    return struct.unpack_from("<H", body, 0)[0] if len(body) >= 2 else None


def parse_symbols(body: bytes, source_tag: str) -> list[dict[str, Any]]:
    if len(body) < 2:
        raise ValueError("truncated symbol table")
    count = struct.unpack_from("<H", body, 0)[0]
    cursor = 2
    result = []
    for _ in range(count):
        if cursor + 2 > len(body):
            raise ValueError("truncated symbol id")
        symbol_id = struct.unpack_from("<H", body, cursor)[0]
        cursor += 2
        end = body.find(b"\0", cursor)
        if end < 0:
            raise ValueError("unterminated symbol name")
        result.append(
            {
                "character_id": symbol_id,
                "name": body[cursor:end].decode("utf-8", errors="replace"),
                "source_tag": source_tag,
            }
        )
        cursor = end + 1
    return result


def output_record(path: Path, payload: bytes, output_root: Path, representation: str) -> dict[str, Any]:
    status = write_verified(path, payload)
    return {
        "path": str(path.relative_to(output_root)),
        "bytes": len(payload),
        "sha256": sha256_bytes(payload),
        "representation": representation,
        "write_status": status,
    }


def extract(path: Path, output: Path, workspace: Path | None = None) -> dict[str, Any]:
    source_hash = sha256_file(path)
    destination = output / f"{safe_name(path.stem)}__{source_hash[:12]}"
    raw_source = path.read_bytes()
    source_path = str(path.relative_to(workspace)) if workspace else str(path)
    try:
        raw, compression = read_uncompressed_swf(path)
    except Exception as error:
        record: dict[str, Any] = {
            "source": source_path,
            "source_bytes": len(raw_source),
            "source_sha256": source_hash,
            "status": "not_valid_swf",
            "error": f"{type(error).__name__}: {error}",
            "outputs": [],
        }
        if raw_source.startswith((b"x\x01", b"x\x9c", b"x\xda")):
            try:
                inflated = zlib.decompress(raw_source)
                target = destination / "non-swf-zlib-payload.bin"
                record["outputs"].append(output_record(target, inflated, output, "inflated_non_swf_payload"))
                record["status"] = "non_swf_zlib_payload_preserved"
            except zlib.error as inflate_error:
                record["inflate_error"] = str(inflate_error)
        destination.mkdir(parents=True, exist_ok=True)
        (destination / "manifest.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        return record

    tag_counts: Counter[str] = Counter()
    exported_symbols: list[dict[str, Any]] = []
    resources: list[dict[str, Any]] = []
    timeline = {"show_frames": 0, "frame_labels": [], "sprites": []}
    jpeg_tables: bytes | None = None
    parse_errors: list[str] = []
    try:
        tags = list(iter_tag_stream(raw, first_tag_offset(raw)))
    except Exception as error:
        tags = []
        parse_errors.append(f"{type(error).__name__}: {error}")

    for code, body, tag_offset, tag_index in tags:
        tag_name = TAG_NAMES.get(code, f"Tag{code}")
        tag_counts[tag_name] += 1
        if code == 8:
            jpeg_tables = body
            continue
        if code == SHOW_FRAME:
            timeline["show_frames"] += 1
            continue
        if code == FRAME_LABEL:
            label = body.split(b"\0", 1)[0].decode("utf-8", errors="replace")
            timeline["frame_labels"].append({"frame": timeline["show_frames"], "label": label})
            continue
        if code in {EXPORT_ASSETS, SYMBOL_CLASS}:
            try:
                exported_symbols.extend(parse_symbols(body, tag_name))
            except ValueError as error:
                parse_errors.append(f"tag {tag_index} {tag_name}: {error}")
            continue

        category: str | None = None
        if code in VECTOR_TAGS:
            category = "vectors"
        elif code in FONT_TAGS:
            category = "fonts"
        elif code in SPRITE_TAGS:
            category = "sprites"
        elif code == DEFINE_BINARY_DATA:
            category = "binary"
        elif code in BITMAP_TAGS:
            category = "bitmaps"
        if category is None:
            continue

        item: dict[str, Any] = {
            "tag_index": tag_index,
            "tag_offset": tag_offset,
            "tag_code": code,
            "tag_name": tag_name,
            "category": category,
            "character_id": character_id(body),
            "outputs": [],
        }
        cid = item["character_id"]
        prefix = f"tag-{tag_index:06d}-code-{code}-id-{cid if cid is not None else 'none'}"
        try:
            if code in BITMAP_TAGS:
                if code in {DEFINE_BITS_LOSSLESS, DEFINE_BITS_LOSSLESS2}:
                    decoded_id, image = decode_lossless(body, code == DEFINE_BITS_LOSSLESS2)
                else:
                    decoded_id, image = decode_jpeg(body, code, jpeg_tables)
                stream = io.BytesIO()
                image.save(stream, format="PNG")
                item["width"], item["height"] = image.size
                item["outputs"].append(
                    output_record(destination / "bitmaps" / f"bitmap-{decoded_id:05d}.png", stream.getvalue(), output, "decoded_png")
                )
                item["status"] = "decoded"
            elif code == DEFINE_BINARY_DATA:
                payload = body[6:] if len(body) >= 6 else b""
                atf_offset = payload.find(b"ATF", 0, min(len(payload), 64))
                if atf_offset >= 0:
                    payload = payload[atf_offset:]
                    suffix, representation = ".atf", "atf_texture_payload"
                    item["atf_offset"] = atf_offset
                else:
                    suffix, representation = ".bin", "define_binary_data"
                item["outputs"].append(
                    output_record(destination / "binary" / f"{prefix}{suffix}", payload, output, representation)
                )
                item["status"] = "preserved"
            else:
                suffix = ".shape" if category == "vectors" else ".font" if category == "fonts" else ".sprite"
                item["outputs"].append(
                    output_record(destination / category / f"{prefix}{suffix}", body, output, f"raw_{category[:-1]}_tag")
                )
                item["status"] = "preserved"
                if code in SPRITE_TAGS and len(body) >= 4:
                    sprite_id, frame_count = struct.unpack_from("<HH", body, 0)
                    nested_counts: Counter[str] = Counter()
                    try:
                        for nested_code, _, _, _ in iter_tag_stream(body, 4):
                            nested_counts[TAG_NAMES.get(nested_code, f"Tag{nested_code}")] += 1
                    except ValueError as error:
                        item["nested_parse_error"] = str(error)
                    sprite = {"character_id": sprite_id, "declared_frames": frame_count, "nested_tags": dict(sorted(nested_counts.items()))}
                    timeline["sprites"].append(sprite)
                    item["sprite"] = sprite
        except Exception as error:
            fallback = destination / category / f"{prefix}.bin"
            item["outputs"].append(output_record(fallback, body, output, "raw_tag_fallback"))
            item["status"] = "raw_fallback"
            item["error"] = f"{type(error).__name__}: {error}"
        resources.append(item)

    symbol_by_id: dict[int, list[str]] = {}
    for symbol in exported_symbols:
        symbol_by_id.setdefault(symbol["character_id"], []).append(symbol["name"])
    for resource in resources:
        resource["symbol_names"] = symbol_by_id.get(resource["character_id"], [])

    status_counts = Counter(item["status"] for item in resources)
    output_counts = Counter(
        output_item["representation"] for item in resources for output_item in item["outputs"]
    )
    manifest: dict[str, Any] = {
        "source": source_path,
        "source_bytes": len(raw_source),
        "source_sha256": source_hash,
        "status": "parsed" if not parse_errors else "parsed_with_errors",
        "compression": compression,
        "tag_counts": dict(sorted(tag_counts.items())),
        "resource_statuses": dict(sorted(status_counts.items())),
        "output_representations": dict(sorted(output_counts.items())),
        "resource_count": len(resources),
        "resources": resources,
        "exported_symbols": exported_symbols,
        "timeline": timeline,
        "parse_errors": parse_errors,
    }
    destination.mkdir(parents=True, exist_ok=True)
    (destination / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return manifest


def markdown(report: dict[str, Any]) -> str:
    summary = report["summary"]
    return "\n".join(
        [
            "# SWF / ATF 静态资源提取报告",
            "",
            "全部输入只按字节读取，不执行 ActionScript。位图转为 PNG；矢量、字体、Sprite/时间轴标签保存原始标签体；",
            "`DefineBinaryData` 中的 ATF 独立保存为 `.atf`。任何解码失败都会保留原始标签体并记录原因。",
            "",
            f"- 输入文件：{summary['sources']}",
            f"- 有效 SWF：{summary['parsed']}",
            f"- 非 SWF 的 zlib 缓存载荷：{summary['non_swf_zlib_payload_preserved']}",
            f"- 资源标签：{summary['resources']}",
            f"- PNG：{summary['decoded_png']}；矢量标签：{summary['raw_vector_tag']}；字体标签：{summary['raw_font_tag']}；Sprite 标签：{summary['raw_sprite_tag']}；ATF：{summary['atf_texture_payload']}",
            f"- 未保底失败：{summary['failed_resources']}",
            "",
            "机器清单逐源保存输入 SHA-256、压缩方式、SWF 版本、标签计数、符号名、帧标签、Sprite 时间轴、输出大小/哈希和错误。",
            "",
        ]
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    parser.add_argument("swf", nargs="*", type=Path)
    parser.add_argument("--workspace", type=Path)
    parser.add_argument("--root-index", type=Path)
    parser.add_argument("--json", type=Path)
    parser.add_argument("--markdown", type=Path)
    args = parser.parse_args()

    workspace = args.workspace.resolve() if args.workspace else None
    sources = list(args.swf)
    if args.root_index:
        if workspace is None:
            raise SystemExit("--workspace is required with --root-index")
        index = json.loads(args.root_index.read_text(encoding="utf-8"))
        sources.extend(workspace / item["path"] for item in index["samples"] if item["sample_type"] == "flash_swf")
    sources = sorted({path.resolve() for path in sources})
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    manifests = [extract(path, output, workspace) for path in sources]
    status_counts = Counter(item["status"] for item in manifests)
    representations = Counter(
        output_item["representation"]
        for manifest in manifests
        for resource in manifest.get("resources", [])
        for output_item in resource["outputs"]
    )
    summary = {
        "sources": len(manifests),
        "parsed": sum(item["status"] in {"parsed", "parsed_with_errors"} for item in manifests),
        "non_swf_zlib_payload_preserved": status_counts["non_swf_zlib_payload_preserved"],
        "not_valid_swf": status_counts["not_valid_swf"],
        "resources": sum(item.get("resource_count", 0) for item in manifests),
        "failed_resources": sum(item.get("resource_statuses", {}).get("failed", 0) for item in manifests),
        **dict(representations),
    }
    report = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "tool": "tools/extract_swf_bitmaps.py",
        "tool_version": "2",
        "parameters": {"root_index": str(args.root_index) if args.root_index else None, "output": str(output)},
        "summary": summary,
        "sources": manifests,
    }
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if args.markdown:
        args.markdown.parent.mkdir(parents=True, exist_ok=True)
        args.markdown.write_text(markdown(report), encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False))
    return 1 if summary["not_valid_swf"] or summary["failed_resources"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
