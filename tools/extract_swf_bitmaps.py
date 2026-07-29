#!/usr/bin/env python3
"""Extract embedded bitmap resources from recovered SWF files.

The extractor intentionally does not execute ActionScript. It reads only the
documented SWF bitmap tags and writes their pixels as PNG files so the original
art can be imported by the restored browser client.
"""

from __future__ import annotations

import argparse
import io
import json
import math
import struct
import zlib
from pathlib import Path

from PIL import Image


DEFINE_BITS_LOSSLESS = 20
DEFINE_BITS_JPEG2 = 21
DEFINE_BITS_JPEG3 = 35
DEFINE_BITS_LOSSLESS2 = 36
DEFINE_BITS_JPEG4 = 90
EXPORT_ASSETS = 56
SYMBOL_CLASS = 76


def read_uncompressed_swf(path: Path) -> bytes:
    raw = path.read_bytes()
    signature = raw[:3]
    if signature == b"FWS":
        return raw
    if signature == b"CWS":
        return b"FWS" + raw[3:8] + zlib.decompress(raw[8:])
    raise ValueError(f"{path.name}: unsupported SWF signature {signature!r}")


def first_tag_offset(raw: bytes) -> int:
    nbits = raw[8] >> 3
    rect_size = math.ceil((5 + 4 * nbits) / 8)
    return 8 + rect_size + 4  # RECT + frame rate + frame count


def iter_tags(raw: bytes):
    offset = first_tag_offset(raw)
    while offset + 2 <= len(raw):
        header = struct.unpack_from("<H", raw, offset)[0]
        offset += 2
        code = header >> 6
        length = header & 0x3F
        if length == 0x3F:
            length = struct.unpack_from("<I", raw, offset)[0]
            offset += 4
        body = raw[offset : offset + length]
        yield code, body
        offset += length
        if code == 0:
            break


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


def decode_jpeg(body: bytes, tag_code: int) -> tuple[int, Image.Image]:
    character_id = struct.unpack_from("<H", body, 0)[0]
    if tag_code == DEFINE_BITS_JPEG2:
        return character_id, open_embedded_image(body[2:])

    alpha_offset = struct.unpack_from("<I", body, 2)[0]
    image_start = 6 if tag_code == DEFINE_BITS_JPEG3 else 8
    image_end = image_start + alpha_offset
    image = open_embedded_image(body[image_start:image_end])
    alpha = zlib.decompress(body[image_end:])
    expected = image.width * image.height
    if len(alpha) < expected:
        raise ValueError(f"alpha channel too short: {len(alpha)} < {expected}")
    image.putalpha(Image.frombytes("L", image.size, alpha[:expected]))
    return character_id, image


def decode_lossless(body: bytes, has_alpha: bool) -> tuple[int, Image.Image]:
    character_id, bitmap_format, width, height = struct.unpack_from("<HBHH", body, 0)
    cursor = 7
    color_table_size = 0
    if bitmap_format == 3:
        color_table_size = body[cursor] + 1
        cursor += 1
    decoded = zlib.decompress(body[cursor:])

    if bitmap_format == 3:
        entry_size = 4 if has_alpha else 3
        palette_bytes = color_table_size * entry_size
        palette_data = decoded[:palette_bytes]
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
            pixels.extend(table[index] for index in row)
        image = Image.new("RGBA", (width, height))
        image.putdata(pixels)
        return character_id, image

    if bitmap_format == 5:
        stride = width * 4
        pixels = bytearray(width * height * 4)
        output = 0
        for y in range(height):
            row = decoded[y * stride : (y + 1) * stride]
            for index in range(0, len(row), 4):
                alpha_or_padding, red, green, blue = row[index : index + 4]
                pixels[output : output + 4] = (
                    red,
                    green,
                    blue,
                    alpha_or_padding if has_alpha else 255,
                )
                output += 4
        return character_id, Image.frombytes("RGBA", (width, height), bytes(pixels))

    raise ValueError(f"unsupported lossless bitmap format {bitmap_format}")


def extract(path: Path, output: Path) -> dict[str, object]:
    raw = read_uncompressed_swf(path)
    destination = output / path.stem
    destination.mkdir(parents=True, exist_ok=True)
    records = []
    exported_symbols = []

    for code, body in iter_tags(raw):
        if code in (EXPORT_ASSETS, SYMBOL_CLASS):
            count = struct.unpack_from("<H", body, 0)[0]
            cursor = 2
            for _ in range(count):
                character_id = struct.unpack_from("<H", body, cursor)[0]
                cursor += 2
                end = body.index(0, cursor)
                name = body[cursor:end].decode("utf-8", "replace")
                cursor = end + 1
                exported_symbols.append(
                    {
                        "characterId": character_id,
                        "name": name,
                        "sourceTag": "SymbolClass"
                        if code == SYMBOL_CLASS
                        else "ExportAssets",
                    }
                )
            continue
        try:
            if code in (DEFINE_BITS_JPEG2, DEFINE_BITS_JPEG3, DEFINE_BITS_JPEG4):
                character_id, image = decode_jpeg(body, code)
            elif code in (DEFINE_BITS_LOSSLESS, DEFINE_BITS_LOSSLESS2):
                character_id, image = decode_lossless(
                    body, code == DEFINE_BITS_LOSSLESS2
                )
            else:
                continue
            filename = f"bitmap-{character_id:05d}.png"
            image.save(destination / filename)
            records.append(
                {
                    "characterId": character_id,
                    "tagCode": code,
                    "width": image.width,
                    "height": image.height,
                    "file": filename,
                }
            )
        except Exception as error:
            records.append({"tagCode": code, "error": str(error)})

    manifest = {
        "source": str(path),
        "bitmapCount": sum("file" in record for record in records),
        "errors": sum("error" in record for record in records),
        "bitmaps": records,
        "exportedSymbols": exported_symbols,
    }
    (destination / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    parser.add_argument("swf", nargs="+", type=Path)
    args = parser.parse_args()

    args.output.mkdir(parents=True, exist_ok=True)
    for path in args.swf:
        manifest = extract(path.resolve(), args.output.resolve())
        print(
            f"{path.name}: {manifest['bitmapCount']} bitmaps, "
            f"{manifest['errors']} errors"
        )


if __name__ == "__main__":
    main()
