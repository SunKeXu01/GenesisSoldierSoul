#!/usr/bin/env python3
"""Recover a contiguous chain of strictly framed media from private REZ.

Parsing always starts at the fixed REZ data offset and stops at the first
unsupported byte.  It never searches for a later signature.  Supported frames
have self-proving boundaries: CRC-valid PNG, header-sized DDS/DTX, complete
TGA (including RLE packet accounting), block-complete GIF, and marker-complete
JPEG.
"""

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

from PIL import Image

from audit_special_formats import decode_dtx
from recover_private_rez import CHUNK_SIZE, entropy, parse_rez_header, sha256_file
from rez_extract import RezError


PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
MAXIMUM_PNG_CHUNK_BYTES = 128 * 1024 * 1024
MAXIMUM_PNG_CHUNKS = 1_000_000
DDS_MAGIC = b"DDS "
DDS_HEADER_BYTES = 128
GIF_SIGNATURES = {b"GIF87a", b"GIF89a"}
JPEG_SIGNATURE = b"\xFF\xD8"
TGA_FOOTER_SIGNATURE = b"TRUEVISION-XFILE.\x00"
DTX_HEADER_BYTES = 164
CFB_SIGNATURE = b"\xD0\xCF\x11\xE0\xA1\xB1\x1A\xE1"
CFB_FREE = 0xFFFFFFFF
CFB_END = 0xFFFFFFFE
CFB_FAT = 0xFFFFFFFD
CFB_DIFAT = 0xFFFFFFFC
MAXIMUM_TEXT_FRAME_BYTES = 16 * 1024 * 1024
MP4_TOP_LEVEL_BOXES = {
    b"ftyp",
    b"free",
    b"skip",
    b"wide",
    b"mdat",
    b"moov",
    b"uuid",
    b"meta",
    b"moof",
    b"mfra",
    b"sidx",
    b"pdin",
    b"styp",
}
EBML_HEADER_ID = 0x1A45DFA3
EBML_SEGMENT_ID = 0x18538067
EBML_SIGNATURE = b"\x1A\x45\xDF\xA3"
LITHTECH_WORLD_VERSION = 85
LITHTECH_WORLD_HEADER_BYTES = 60
LITHTECH_RENDER_VERTEX_BYTES = 68
MAXIMUM_WORLD_ITEMS = 10_000_000
MAXIMUM_WORLD_STRING_BYTES = 4096
MAXIMUM_WORLD_RECURSION = 64


def hash_region(stream: BinaryIO, offset: int, size: int) -> str:
    stream.seek(offset)
    digest = hashlib.sha256()
    remaining = size
    while remaining:
        data = stream.read(min(CHUNK_SIZE, remaining))
        if not data:
            raise RezError(f"truncated framed resource at {offset}")
        digest.update(data)
        remaining -= len(data)
    return digest.hexdigest()


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


def parse_tga(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse an uncompressed or RLE true-color/grayscale TGA exactly."""
    if offset < 0 or offset + 18 > region_end:
        raise RezError(f"truncated TGA header at {offset}")
    stream.seek(offset)
    header = stream.read(18)
    id_length, color_map_type, image_type = struct.unpack_from("<BBB", header)
    width, height = struct.unpack_from("<HH", header, 12)
    bits_per_pixel = header[16]
    if (
        color_map_type != 0
        or image_type not in {2, 3, 10, 11}
        or width == 0
        or height == 0
        or bits_per_pixel not in {8, 24, 32}
    ):
        raise RezError(f"unsupported TGA header at exact offset {offset}")
    bytes_per_pixel = bits_per_pixel // 8
    position = offset + 18 + id_length
    if position > region_end:
        raise RezError(f"TGA image ID crosses REZ data boundary at {offset}")
    pixel_count = width * height
    if image_type in {2, 3}:
        position += pixel_count * bytes_per_pixel
        packet_count = 0
    else:
        stream.seek(position)
        decoded_pixels = 0
        packet_count = 0
        while decoded_pixels < pixel_count:
            raw_packet = stream.read(1)
            if not raw_packet:
                raise RezError(f"truncated TGA RLE packet at {offset}")
            count = (raw_packet[0] & 0x7F) + 1
            if decoded_pixels + count > pixel_count:
                raise RezError(f"TGA RLE packet overruns image at {offset}")
            encoded_bytes = bytes_per_pixel if raw_packet[0] & 0x80 else count * bytes_per_pixel
            if stream.tell() + encoded_bytes > region_end:
                raise RezError(f"TGA RLE data crosses REZ boundary at {offset}")
            stream.seek(encoded_bytes, os.SEEK_CUR)
            decoded_pixels += count
            packet_count += 1
        position = stream.tell()
    if position > region_end:
        raise RezError(f"TGA pixels cross REZ data boundary at {offset}")
    stream.seek(position)
    footer = stream.read(26)
    has_footer = len(footer) == 26 and footer[8:] == TGA_FOOTER_SIGNATURE
    if has_footer:
        position += 26
    size = position - offset
    return {
        "offset": offset,
        "bytes": size,
        "sha256": hash_region(stream, offset, size),
        "width": width,
        "height": height,
        "bits_per_pixel": bits_per_pixel,
        "image_type": image_type,
        "rle": image_type in {10, 11},
        "rle_packets": packet_count,
        "tga_2_footer": has_footer,
    }


def parse_dtx(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse a section-free DTX v-5 whose mip payload size is derivable."""
    if offset < 0 or offset + DTX_HEADER_BYTES > region_end:
        raise RezError(f"truncated DTX header at {offset}")
    stream.seek(offset)
    header = stream.read(DTX_HEADER_BYTES)
    resource_type, version = struct.unpack_from("<ii", header)
    width, height, mipmaps, sections = struct.unpack_from("<HHHH", header, 8)
    bpp_identifier = header[26]
    if (
        resource_type not in {0, 1}
        or version != -5
        or width == 0
        or height == 0
        or not 1 <= mipmaps <= 32
        or sections != 0
        or bpp_identifier not in {3, 4, 5, 6}
    ):
        raise RezError(f"unsupported DTX header at exact offset {offset}")
    payload_bytes = 0
    for level in range(mipmaps):
        level_width = max(1, width >> level)
        level_height = max(1, height >> level)
        if bpp_identifier == 3:
            payload_bytes += level_width * level_height * 4
        else:
            block_bytes = 8 if bpp_identifier == 4 else 16
            payload_bytes += (
                max(1, (level_width + 3) // 4)
                * max(1, (level_height + 3) // 4)
                * block_bytes
            )
    size = DTX_HEADER_BYTES + payload_bytes
    if offset + size > region_end:
        raise RezError(f"DTX payload crosses REZ data boundary at {offset}")
    return {
        "offset": offset,
        "bytes": size,
        "sha256": hash_region(stream, offset, size),
        "resource_type": resource_type,
        "version": version,
        "width": width,
        "height": height,
        "mip_count": mipmaps,
        "sections": sections,
        "bpp_identifier": bpp_identifier,
        "pixel_format": {3: "BGRA8888", 4: "DXT1", 5: "DXT3", 6: "DXT5"}[
            bpp_identifier
        ],
    }


def read_gif_sub_blocks(stream: BinaryIO, position: int, region_end: int) -> int:
    stream.seek(position)
    while True:
        raw_length = stream.read(1)
        if not raw_length:
            raise RezError(f"truncated GIF sub-block at {position}")
        length = raw_length[0]
        position += 1
        if length == 0:
            return position
        if position + length > region_end:
            raise RezError(f"GIF sub-block crosses REZ boundary at {position}")
        stream.seek(length, os.SEEK_CUR)
        position += length


def parse_gif(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse a GIF through its trailer with complete sub-block accounting."""
    if offset < 0 or offset + 13 > region_end:
        raise RezError(f"truncated GIF header at {offset}")
    stream.seek(offset)
    header = stream.read(13)
    if header[:6] not in GIF_SIGNATURES:
        raise RezError(f"GIF signature absent at exact offset {offset}")
    width, height = struct.unpack_from("<HH", header, 6)
    if width == 0 or height == 0:
        raise RezError(f"GIF has zero dimensions at {offset}")
    position = offset + 13
    if header[10] & 0x80:
        position += 3 * (1 << ((header[10] & 0x07) + 1))
    image_count = extension_count = 0
    while position < region_end:
        stream.seek(position)
        introducer = stream.read(1)
        position += 1
        if introducer == b"\x3B":
            if image_count == 0:
                raise RezError(f"GIF has no image descriptor at {offset}")
            size = position - offset
            return {
                "offset": offset,
                "bytes": size,
                "sha256": hash_region(stream, offset, size),
                "width": width,
                "height": height,
                "images": image_count,
                "extensions": extension_count,
                "version": header[:6].decode("ascii"),
            }
        if introducer == b"\x21":
            if position >= region_end:
                raise RezError(f"truncated GIF extension at {position}")
            position += 1  # extension label
            extension_count += 1
            position = read_gif_sub_blocks(stream, position, region_end)
            continue
        if introducer != b"\x2C" or position + 9 > region_end:
            raise RezError(f"invalid GIF block at {position - 1}")
        stream.seek(position)
        descriptor = stream.read(9)
        position += 9
        if descriptor[8] & 0x80:
            position += 3 * (1 << ((descriptor[8] & 0x07) + 1))
        if position >= region_end:
            raise RezError(f"truncated GIF image data at {position}")
        position += 1  # LZW minimum code size
        image_count += 1
        position = read_gif_sub_blocks(stream, position, region_end)
    raise RezError(f"GIF trailer missing at {offset}")


def parse_jpeg(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse JPEG markers and entropy scans through the exact EOI marker."""
    if offset < 0 or offset + 4 > region_end:
        raise RezError(f"truncated JPEG header at {offset}")
    stream.seek(offset)
    if stream.read(2) != JPEG_SIGNATURE:
        raise RezError(f"JPEG signature absent at exact offset {offset}")
    position = offset + 2
    width = height = None
    scans = 0
    while position < region_end:
        stream.seek(position)
        if stream.read(1) != b"\xFF":
            raise RezError(f"JPEG marker prefix absent at {position}")
        marker_byte = stream.read(1)
        while marker_byte == b"\xFF":
            marker_byte = stream.read(1)
        if not marker_byte:
            raise RezError(f"truncated JPEG marker at {position}")
        marker = marker_byte[0]
        position = stream.tell()
        if marker == 0xD9:
            size = position - offset
            if width is None or scans == 0:
                raise RezError(f"incomplete JPEG structure at {offset}")
            return {
                "offset": offset,
                "bytes": size,
                "sha256": hash_region(stream, offset, size),
                "width": width,
                "height": height,
                "scans": scans,
            }
        if marker in set(range(0xD0, 0xD8)) | {0x01, 0xD8}:
            continue
        if position + 2 > region_end:
            raise RezError(f"truncated JPEG segment length at {position}")
        stream.seek(position)
        segment_length = struct.unpack(">H", stream.read(2))[0]
        if segment_length < 2 or position + segment_length > region_end:
            raise RezError(f"invalid JPEG segment length at {position}: {segment_length}")
        segment_start = position + 2
        if marker in set(range(0xC0, 0xC4)) | set(range(0xC5, 0xC8)) | set(
            range(0xC9, 0xCC)
        ) | set(range(0xCD, 0xD0)):
            if segment_length < 7:
                raise RezError(f"truncated JPEG frame header at {position}")
            stream.seek(segment_start + 1)
            height, width = struct.unpack(">HH", stream.read(4))
            if width == 0 or height == 0:
                raise RezError(f"JPEG has zero dimensions at {position}")
        position += segment_length
        if marker != 0xDA:
            continue
        scans += 1
        stream.seek(position)
        while position < region_end:
            raw = stream.read(1)
            if not raw:
                break
            position += 1
            if raw != b"\xFF":
                continue
            following = stream.read(1)
            if not following:
                break
            position += 1
            if following == b"\x00" or 0xD0 <= following[0] <= 0xD7:
                continue
            stream.seek(-2, os.SEEK_CUR)
            position -= 2
            break
    raise RezError(f"JPEG EOI marker missing at {offset}")


def parse_start_end_config(
    stream: BinaryIO, offset: int, region_end: int
) -> dict[str, object]:
    """Parse one or more strict ``<start>``/``<end>`` ASCII config blocks."""
    stream.seek(offset)
    collected = bytearray()
    limit = min(region_end - offset, MAXIMUM_TEXT_FRAME_BYTES)
    while len(collected) < limit:
        raw = stream.read(1)
        if not raw or raw[0] not in {9, 10, 13} | set(range(32, 127)):
            break
        collected.extend(raw)
    if not collected.startswith(b"<start>\r\n"):
        raise RezError(f"config block absent at exact offset {offset}")
    try:
        text = collected.decode("ascii")
    except UnicodeDecodeError as error:
        raise RezError(f"non-ASCII config block at {offset}") from error
    lines = text.replace("\r\n", "\n").split("\n")
    index = block_count = pair_count = 0
    while index < len(lines):
        while index < len(lines) and lines[index] == "":
            index += 1
        if index >= len(lines):
            break
        if lines[index] != "<start>":
            raise RezError(f"invalid config start line at {offset}: {lines[index]!r}")
        index += 1
        block_pairs = 0
        while index < len(lines) and lines[index] != "<end>":
            line = lines[index]
            if "=" not in line:
                raise RezError(f"invalid config key/value at {offset}: {line!r}")
            key, value = line.split("=", 1)
            if (
                not key
                or not all(character.isalnum() or character == "_" for character in key)
                or not value
            ):
                raise RezError(f"invalid config key/value at {offset}: {line!r}")
            block_pairs += 1
            pair_count += 1
            index += 1
        if index >= len(lines) or block_pairs == 0:
            raise RezError(f"unterminated/empty config block at {offset}")
        index += 1
        block_count += 1
    if block_count == 0:
        raise RezError(f"empty config sequence at {offset}")
    size = len(collected)
    return {
        "offset": offset,
        "bytes": size,
        "sha256": hashlib.sha256(collected).hexdigest(),
        "blocks": block_count,
        "key_value_pairs": pair_count,
        "encoding": "ascii",
        "line_endings": "CRLF" if b"\r\n" in collected else "LF",
    }


def read_ascii_prefix(stream: BinaryIO, offset: int, region_end: int) -> bytes:
    stream.seek(offset)
    collected = bytearray()
    limit = min(region_end - offset, MAXIMUM_TEXT_FRAME_BYTES)
    allowed = {9, 10, 13} | set(range(32, 127))
    while len(collected) < limit:
        raw = stream.read(1)
        if not raw or raw[0] not in allowed:
            break
        collected.extend(raw)
    if len(collected) == limit and offset + len(collected) < region_end:
        raise RezError(f"ASCII frame exceeds safety limit at {offset}")
    return bytes(collected)


def parse_ini(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse a complete printable INI frame ending at the next binary frame."""
    collected = read_ascii_prefix(stream, offset, region_end)
    try:
        text = collected.decode("ascii")
    except UnicodeDecodeError as error:
        raise RezError(f"non-ASCII INI at {offset}") from error
    section_count = pair_count = 0
    in_section = False
    for raw_line in text.replace("\r\n", "\n").split("\n"):
        line = raw_line.strip()
        if not line or line.startswith((";", "#")):
            continue
        if line.startswith("[") and line.endswith("]") and len(line) > 2:
            section_count += 1
            in_section = True
            continue
        if not in_section or "=" not in line or not line.split("=", 1)[0].strip():
            raise RezError(f"invalid INI line at {offset}: {raw_line!r}")
        pair_count += 1
    if section_count == 0 or pair_count == 0:
        raise RezError(f"empty INI structure at {offset}")
    return {
        "offset": offset,
        "bytes": len(collected),
        "sha256": hashlib.sha256(collected).hexdigest(),
        "sections": section_count,
        "key_value_pairs": pair_count,
        "encoding": "ascii",
    }


def parse_ascii_web_bundle(
    stream: BinaryIO, offset: int, region_end: int
) -> dict[str, object]:
    """Preserve one printable web bundle bounded by the next exact binary frame."""
    collected = read_ascii_prefix(stream, offset, region_end)
    jquery_bundle = (
        collected.startswith(b"/*! jQuery ")
        and b"jQuery" in collected
        and b"jquery.org/license" in collected[:256]
    )
    css_script_bundle = (
        collected.startswith((b"body{", b"body {"))
        and b"background" in collected
        and b"function " in collected
        and collected.count(b"{") == collected.count(b"}")
    )
    css_overlay = (
        collected.startswith(b"//.overlay{")
        and b".overlay" in collected
        and b"@media" in collected
        and collected.count(b"{") == collected.count(b"}")
    )
    if len(collected) < 100 or not (jquery_bundle or css_script_bundle or css_overlay):
        raise RezError(f"unsupported ASCII web bundle at {offset}")
    next_offset = offset + len(collected)
    stream.seek(next_offset)
    next_prefix = stream.read(DTX_HEADER_BYTES)
    next_kind = prefix_kind(next_prefix)
    if next_kind is None:
        raise RezError(f"web bundle has no exact supported successor at {next_offset}")
    return {
        "offset": offset,
        "bytes": len(collected),
        "sha256": hashlib.sha256(collected).hexdigest(),
        "encoding": "ascii",
        "bundle_kind": (
            "jquery"
            if jquery_bundle
            else "css_and_script"
            if css_script_bundle
            else "css_overlay"
        ),
        "next_frame_kind": next_kind,
    }


def parse_cfb(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Derive an OLE CFB boundary from its DIFAT/FAT allocation tables."""
    if offset < 0 or offset + 512 > region_end:
        raise RezError(f"truncated CFB header at {offset}")
    stream.seek(offset)
    header = stream.read(512)
    if header[:8] != CFB_SIGNATURE:
        raise RezError(f"CFB signature absent at exact offset {offset}")
    minor, major, byte_order, sector_shift, mini_shift = struct.unpack_from(
        "<5H", header, 24
    )
    if major not in {3, 4} or byte_order != 0xFFFE:
        raise RezError(f"unsupported CFB version/byte order at {offset}")
    if sector_shift != (9 if major == 3 else 12) or mini_shift != 6:
        raise RezError(f"invalid CFB sector shifts at {offset}")
    if any(header[34:40]):
        raise RezError(f"non-zero CFB reserved header bytes at {offset}")
    sector_size = 1 << sector_shift
    if offset + sector_size > region_end:
        raise RezError(f"CFB header sector crosses REZ boundary at {offset}")
    if major == 4:
        stream.seek(offset + 512)
        if any(stream.read(sector_size - 512)):
            raise RezError(f"non-zero CFB v4 header padding at {offset}")
    directory_sectors, fat_sector_count, first_directory = struct.unpack_from(
        "<III", header, 40
    )
    mini_cutoff, first_mini_fat, mini_fat_count, first_difat, difat_count = (
        struct.unpack_from("<IIIII", header, 56)
    )
    if major == 3 and directory_sectors != 0:
        raise RezError(f"CFB v3 directory sector count must be zero at {offset}")
    if fat_sector_count == 0 or mini_cutoff != 0x1000:
        raise RezError(f"invalid CFB FAT count/cutoff at {offset}")
    difat = [value for value in struct.unpack_from("<109I", header, 76) if value != CFB_FREE]
    difat_sector_ids: list[int] = []
    next_difat = first_difat
    for index in range(difat_count):
        if next_difat > 0xFFFFFFFA:
            raise RezError(f"invalid CFB DIFAT chain at {offset}")
        sector_offset = offset + (next_difat + 1) * sector_size
        if sector_offset + sector_size > region_end:
            raise RezError(f"CFB DIFAT sector crosses REZ boundary at {offset}")
        stream.seek(sector_offset)
        values = struct.unpack(f"<{sector_size // 4}I", stream.read(sector_size))
        difat.extend(value for value in values[:-1] if value != CFB_FREE)
        difat_sector_ids.append(next_difat)
        next_difat = values[-1]
        if index == difat_count - 1 and next_difat != CFB_END:
            raise RezError(f"unterminated CFB DIFAT chain at {offset}")
    if difat_count == 0 and first_difat != CFB_END:
        raise RezError(f"unexpected CFB DIFAT start at {offset}")
    if len(difat) != fat_sector_count or len(set(difat)) != len(difat):
        raise RezError(f"CFB DIFAT/FAT count mismatch at {offset}")
    fat_entries: list[int] = []
    for sector_id in difat:
        if sector_id > 0xFFFFFFFA:
            raise RezError(f"invalid CFB FAT sector id at {offset}")
        sector_offset = offset + (sector_id + 1) * sector_size
        if sector_offset + sector_size > region_end:
            raise RezError(f"CFB FAT sector crosses REZ boundary at {offset}")
        stream.seek(sector_offset)
        fat_entries.extend(struct.unpack(f"<{sector_size // 4}I", stream.read(sector_size)))
    allocated = [index for index, value in enumerate(fat_entries) if value != CFB_FREE]
    if not allocated:
        raise RezError(f"CFB contains no allocated sectors at {offset}")
    last_sector = allocated[-1]
    for index in allocated:
        value = fat_entries[index]
        if value <= 0xFFFFFFFA and value > last_sector:
            raise RezError(f"CFB FAT points beyond allocated extent at {offset}")
    for sector_id in difat:
        if sector_id > last_sector or fat_entries[sector_id] != CFB_FAT:
            raise RezError(f"CFB FAT sector is not self-marked at {offset}")
    for sector_id in difat_sector_ids:
        if sector_id > last_sector or fat_entries[sector_id] != CFB_DIFAT:
            raise RezError(f"CFB DIFAT sector is not self-marked at {offset}")
    for sector_id in (first_directory,):
        if sector_id > last_sector:
            raise RezError(f"CFB directory starts beyond allocated extent at {offset}")
    if mini_fat_count and first_mini_fat > last_sector:
        raise RezError(f"CFB mini FAT starts beyond allocated extent at {offset}")
    size = (last_sector + 2) * sector_size
    if offset + size > region_end:
        raise RezError(f"CFB allocation crosses REZ data boundary at {offset}")
    return {
        "offset": offset,
        "bytes": size,
        "sha256": hash_region(stream, offset, size),
        "minor_version": minor,
        "major_version": major,
        "sector_size": sector_size,
        "allocated_sectors": len(allocated),
        "last_allocated_sector": last_sector,
        "fat_sectors": fat_sector_count,
        "difat_sectors": difat_count,
        "mini_fat_sectors": mini_fat_count,
    }


def parse_mp4(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse a complete ISO BMFF/MP4 top-level box chain at an exact offset."""
    position = offset
    box_count = 0
    box_counts: Counter[str] = Counter()
    while position + 8 <= region_end:
        stream.seek(position)
        header = stream.read(16)
        size32, box_type = struct.unpack_from(">I4s", header)
        if box_type not in MP4_TOP_LEVEL_BOXES:
            break
        if box_type == b"ftyp" and box_count > 0:
            break
        header_bytes = 8
        if size32 == 1:
            if len(header) < 16:
                raise RezError(f"truncated MP4 extended box header at {position}")
            box_size = struct.unpack_from(">Q", header, 8)[0]
            header_bytes = 16
        elif size32 == 0:
            raise RezError(f"unbounded MP4 box is not accepted at {position}")
        else:
            box_size = size32
        if box_size < header_bytes or position + box_size > region_end:
            raise RezError(f"invalid MP4 box size at {position}: {box_size}")
        if box_count == 0:
            if box_type != b"ftyp" or box_size < 16:
                raise RezError(f"MP4 does not begin with a valid ftyp at {offset}")
            stream.seek(position + header_bytes)
            major_brand = stream.read(4)
            if len(major_brand) != 4 or not all(32 <= byte < 127 for byte in major_brand):
                raise RezError(f"invalid MP4 major brand at {offset}")
        box_counts[box_type.decode("ascii")] += 1
        box_count += 1
        position += box_size
    if box_count == 0 or not box_counts["ftyp"] or not box_counts["moov"] or not box_counts["mdat"]:
        raise RezError(f"incomplete MP4 top-level structure at {offset}")
    size = position - offset
    return {
        "offset": offset,
        "bytes": size,
        "sha256": hash_region(stream, offset, size),
        "box_count": box_count,
        "box_counts": dict(sorted(box_counts.items())),
    }


def read_ebml_vint(
    stream: BinaryIO, position: int, region_end: int, *, element_id: bool
) -> tuple[int, int]:
    if position >= region_end:
        raise RezError(f"truncated EBML VINT at {position}")
    stream.seek(position)
    first_raw = stream.read(1)
    if not first_raw or first_raw[0] == 0:
        raise RezError(f"invalid EBML VINT at {position}")
    first = first_raw[0]
    length = 1
    mask = 0x80
    while not first & mask:
        length += 1
        mask >>= 1
    if length > 8 or position + length > region_end:
        raise RezError(f"invalid EBML VINT length at {position}: {length}")
    rest = stream.read(length - 1)
    if len(rest) != length - 1:
        raise RezError(f"truncated EBML VINT at {position}")
    if element_id:
        value = int.from_bytes(first_raw + rest, "big")
    else:
        value = first & (mask - 1)
        for byte in rest:
            value = (value << 8) | byte
        if value == (1 << (7 * length)) - 1:
            raise RezError(f"unknown-size EBML element is not accepted at {position}")
    return value, length


def parse_webm(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse an EBML Header plus one explicitly sized Matroska/WebM Segment."""
    header_id, header_id_bytes = read_ebml_vint(stream, offset, region_end, element_id=True)
    if header_id != EBML_HEADER_ID:
        raise RezError(f"EBML signature absent at exact offset {offset}")
    header_size, header_size_bytes = read_ebml_vint(
        stream, offset + header_id_bytes, region_end, element_id=False
    )
    header_payload = offset + header_id_bytes + header_size_bytes
    if header_size > 1024 * 1024 or header_payload + header_size > region_end:
        raise RezError(f"invalid EBML header size at {offset}: {header_size}")
    stream.seek(header_payload)
    header_data = stream.read(header_size)
    if b"webm" not in header_data.lower() and b"matroska" not in header_data.lower():
        raise RezError(f"EBML DocType is not WebM/Matroska at {offset}")
    segment_offset = header_payload + header_size
    segment_id, segment_id_bytes = read_ebml_vint(
        stream, segment_offset, region_end, element_id=True
    )
    if segment_id != EBML_SEGMENT_ID:
        raise RezError(f"Matroska Segment absent after EBML header at {segment_offset}")
    segment_size, segment_size_bytes = read_ebml_vint(
        stream, segment_offset + segment_id_bytes, region_end, element_id=False
    )
    end = segment_offset + segment_id_bytes + segment_size_bytes + segment_size
    if end > region_end:
        raise RezError(f"Matroska Segment crosses REZ boundary at {offset}")
    size = end - offset
    return {
        "offset": offset,
        "bytes": size,
        "sha256": hash_region(stream, offset, size),
        "doctype": "webm" if b"webm" in header_data.lower() else "matroska",
        "ebml_header_bytes": header_size,
        "segment_bytes": segment_size,
    }


class LithTechWorldReader:
    """Bounded little-endian reader for LithTech Jupiter world render data."""

    def __init__(self, stream: BinaryIO, position: int, region_end: int) -> None:
        self.stream = stream
        self.position = position
        self.region_end = region_end

    def require(self, size: int, label: str) -> None:
        if size < 0 or self.position + size > self.region_end:
            raise RezError(
                f"LithTech world {label} crosses region boundary at {self.position}: "
                f"size={size} end={self.region_end}"
            )

    def read(self, size: int, label: str) -> bytes:
        self.require(size, label)
        self.stream.seek(self.position)
        data = self.stream.read(size)
        if len(data) != size:
            raise RezError(f"truncated LithTech world {label} at {self.position}")
        self.position += size
        return data

    def skip(self, size: int, label: str) -> None:
        self.require(size, label)
        self.position += size

    def u8(self, label: str) -> int:
        return self.read(1, label)[0]

    def u16(self, label: str) -> int:
        return struct.unpack("<H", self.read(2, label))[0]

    def u32(self, label: str) -> int:
        return struct.unpack("<I", self.read(4, label))[0]

    def floats(self, count: int, label: str) -> tuple[float, ...]:
        values = struct.unpack(f"<{count}f", self.read(count * 4, label))
        if not all(value == value and abs(value) != float("inf") for value in values):
            raise RezError(f"non-finite LithTech world {label} at {self.position}")
        return values

    def string(self, label: str, maximum: int = MAXIMUM_WORLD_STRING_BYTES) -> bytes:
        length = self.u16(f"{label} length")
        if length > maximum:
            raise RezError(f"LithTech world {label} is too long: {length}")
        value = self.read(length, label)
        if b"\0" in value:
            raise RezError(f"LithTech world {label} contains an embedded NUL")
        return value

    def count(self, label: str, maximum: int = MAXIMUM_WORLD_ITEMS) -> int:
        value = self.u32(label)
        if value > maximum:
            raise RezError(f"implausible LithTech world {label}: {value}")
        return value


def parse_lithtech_render_polygon(
    reader: LithTechWorldReader, label: str, *, occluder: bool
) -> None:
    vertices = reader.u8(f"{label} vertex count")
    if vertices < 3:
        raise RezError(f"LithTech world {label} has fewer than three vertices")
    reader.floats(vertices * 3, f"{label} vertices")
    reader.floats(4, f"{label} plane")
    if occluder:
        reader.u32(f"{label} id")


def parse_lithtech_render_light_group(
    reader: LithTechWorldReader, label: str
) -> None:
    reader.string(f"{label} id")
    reader.floats(3, f"{label} color")
    reader.skip(reader.count(f"{label} vertex intensity bytes"), f"{label} intensities")
    section_count = reader.count(f"{label} section lightmap count")
    for section_index in range(section_count):
        sub_count = reader.count(f"{label} section {section_index} sub-lightmap count")
        for sub_index in range(sub_count):
            prefix = f"{label} section {section_index} sub-lightmap {sub_index}"
            reader.skip(16, f"{prefix} rectangle")
            reader.skip(reader.count(f"{prefix} bytes"), f"{prefix} data")


def parse_lithtech_render_block(
    reader: LithTechWorldReader, label: str, block_count: int
) -> dict[str, int]:
    bounds = reader.floats(6, f"{label} bounds")
    if any(value < 0 for value in bounds[3:]):
        raise RezError(f"LithTech world {label} has negative half-dimensions")
    section_count = reader.count(f"{label} section count")
    section_triangles = 0
    for section_index in range(section_count):
        prefix = f"{label} section {section_index}"
        reader.string(f"{prefix} texture 0", 1024)
        reader.string(f"{prefix} texture 1", 1024)
        reader.u8(f"{prefix} shader")
        triangles = reader.count(f"{prefix} triangle count")
        if triangles == 0:
            raise RezError(f"LithTech world {prefix} has zero triangles")
        section_triangles += triangles
        if section_triangles > MAXIMUM_WORLD_ITEMS:
            raise RezError(f"implausible LithTech world {label} section triangles")
        reader.string(f"{prefix} texture effect", 1024)
        reader.u32(f"{prefix} lightmap width")
        reader.u32(f"{prefix} lightmap height")
        reader.skip(reader.count(f"{prefix} lightmap bytes"), f"{prefix} lightmap")

    vertex_count = reader.count(f"{label} vertex count")
    vertex_data = reader.read(
        vertex_count * LITHTECH_RENDER_VERTEX_BYTES, f"{label} vertices"
    )
    for vertex_index in range(vertex_count):
        base = vertex_index * LITHTECH_RENDER_VERTEX_BYTES
        position = struct.unpack_from("<3f", vertex_data, base)
        if not all(
            value == value and abs(value) != float("inf") for value in position
        ):
            raise RezError(
                f"non-finite LithTech world {label} vertex {vertex_index} position"
            )

    triangle_count = reader.count(f"{label} triangle count")
    if triangle_count != section_triangles:
        raise RezError(
            f"LithTech world {label} triangle total mismatch: "
            f"sections={section_triangles} block={triangle_count}"
        )
    for triangle_index in range(triangle_count):
        indices = (
            reader.u32(f"{label} triangle {triangle_index} index 0"),
            reader.u32(f"{label} triangle {triangle_index} index 1"),
            reader.u32(f"{label} triangle {triangle_index} index 2"),
        )
        reader.u32(f"{label} triangle {triangle_index} polygon index")
        if any(index >= vertex_count for index in indices):
            raise RezError(
                f"LithTech world {label} triangle {triangle_index} has invalid "
                f"vertex index {indices} for {vertex_count} vertices"
            )

    sky_count = reader.count(f"{label} sky portal count")
    for polygon_index in range(sky_count):
        parse_lithtech_render_polygon(
            reader, f"{label} sky portal {polygon_index}", occluder=False
        )
    occluder_count = reader.count(f"{label} occluder count")
    for polygon_index in range(occluder_count):
        parse_lithtech_render_polygon(
            reader, f"{label} occluder {polygon_index}", occluder=True
        )
    light_group_count = reader.count(f"{label} light group count")
    for group_index in range(light_group_count):
        parse_lithtech_render_light_group(reader, f"{label} light group {group_index}")

    child_flags = reader.u8(f"{label} child flags")
    if child_flags & ~0x03:
        raise RezError(f"LithTech world {label} has invalid child flags 0x{child_flags:02x}")
    for child_index in range(2):
        index = reader.u32(f"{label} child {child_index}")
        if child_flags & (1 << child_index) and index >= block_count:
            raise RezError(
                f"LithTech world {label} child {child_index} index {index} "
                f"exceeds block count {block_count}"
            )
    return {
        "sections": section_count,
        "vertices": vertex_count,
        "triangles": triangle_count,
        "sky_portals": sky_count,
        "occluders": occluder_count,
        "light_groups": light_group_count,
    }


def parse_lithtech_render_world(
    reader: LithTechWorldReader, label: str, depth: int = 0
) -> dict[str, int]:
    if depth > MAXIMUM_WORLD_RECURSION:
        raise RezError("LithTech render-world recursion exceeds safety limit")
    block_count = reader.count(f"{label} block count")
    totals = {
        "render_worlds": 1,
        "render_blocks": block_count,
        "sections": 0,
        "vertices": 0,
        "triangles": 0,
        "sky_portals": 0,
        "occluders": 0,
        "light_groups": 0,
        "child_world_models": 0,
    }
    for block_index in range(block_count):
        block = parse_lithtech_render_block(
            reader, f"{label} block {block_index}", block_count
        )
        for key, value in block.items():
            totals[key] += value
    model_count = reader.count(f"{label} child world-model count")
    for model_index in range(model_count):
        reader.string(f"{label} child world-model {model_index} name", 256)
        child = parse_lithtech_render_world(
            reader, f"{label} child world-model {model_index}", depth + 1
        )
        for key, value in child.items():
            totals[key] += value
    totals["child_world_models"] += model_count
    return totals


def parse_lithtech_world(
    stream: BinaryIO, offset: int, region_end: int
) -> dict[str, object]:
    """Parse a Jupiter world v85 through its self-delimiting render tail.

    The layout follows CrossFire's published ``ReadWorldHeader``,
    ``CD3D_RenderWorld::Load`` and ``CD3D_RenderBlock::Load`` implementations.
    It starts at an exact offset and never searches for a later world header.
    """
    if offset < 0 or offset + LITHTECH_WORLD_HEADER_BYTES > region_end:
        raise RezError(f"truncated LithTech world header at {offset}")
    stream.seek(offset)
    header = stream.read(LITHTECH_WORLD_HEADER_BYTES)
    values = struct.unpack("<15I", header)
    version = values[0]
    if version != LITHTECH_WORLD_VERSION:
        raise RezError(f"LithTech world v85 header absent at exact offset {offset}")
    section_offsets = values[1:7]
    if not all(
        LITHTECH_WORLD_HEADER_BYTES <= value < region_end - offset
        for value in section_offsets
    ):
        raise RezError(f"LithTech world section offset is outside the frame at {offset}")
    if tuple(sorted(section_offsets)) != section_offsets:
        raise RezError(f"LithTech world section offsets are not monotonic at {offset}")

    reader = LithTechWorldReader(stream, offset + section_offsets[-1], region_end)
    totals = parse_lithtech_render_world(reader, "root render world")
    client_group_count = reader.count("client light group count")
    client_samples = 0
    for group_index in range(client_group_count):
        label = f"client light group {group_index}"
        reader.string(f"{label} id")
        reader.floats(3, f"{label} color")
        reader.skip(12, f"{label} minimum sample coordinate")
        extents = struct.unpack("<3i", reader.read(12, f"{label} sample extents"))
        if any(value < 0 for value in extents):
            raise RezError(f"LithTech world {label} has negative sample extents")
        samples = extents[0] * extents[1] * extents[2]
        if samples > MAXIMUM_WORLD_ITEMS:
            raise RezError(f"implausible LithTech world {label} sample count: {samples}")
        reader.skip(samples, f"{label} samples")
        client_samples += samples

    size = reader.position - offset
    if size <= LITHTECH_WORLD_HEADER_BYTES:
        raise RezError(f"LithTech world parser made no progress at {offset}")
    return {
        "offset": offset,
        "bytes": size,
        "sha256": hash_region(stream, offset, size),
        "version": version,
        "object_data_offset": section_offsets[0],
        "blind_object_data_offset": section_offsets[1],
        "light_grid_offset": section_offsets[2],
        "collision_data_offset": section_offsets[3],
        "particle_blocker_data_offset": section_offsets[4],
        "render_data_offset": section_offsets[5],
        "client_light_groups": client_group_count,
        "client_light_samples": client_samples,
        **totals,
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
            raise RezError(f"existing framed-resource output differs: {destination}")
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
        raise RezError(f"copied framed-resource hash mismatch at {offset}")
    os.replace(temporary, destination)
    return "recovered"


def write_verified_bytes(destination: Path, data: bytes) -> str:
    digest = hashlib.sha256(data).hexdigest()
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        if (
            not destination.is_file()
            or destination.stat().st_size != len(data)
            or sha256_file(destination) != digest
        ):
            raise RezError(f"existing converted frame differs: {destination}")
        return "verified_existing"
    destination.write_bytes(data)
    if sha256_file(destination) != digest:
        raise RezError(f"converted frame hash mismatch: {destination}")
    return "converted"


def converted_png(source: Path, kind: str) -> tuple[bytes, dict[str, object]]:
    data = source.read_bytes()
    if kind == "dtx":
        png, details = decode_dtx(data)
        return png, {"converter": "decode_dtx", **details}
    try:
        with Image.open(source) as image:
            image.load()
            converted = image.convert("RGBA")
            with tempfile.SpooledTemporaryFile(max_size=16 * 1024 * 1024) as stream:
                converted.save(stream, format="PNG", optimize=False)
                stream.seek(0)
                return stream.read(), {
                    "converter": "Pillow",
                    "width": converted.width,
                    "height": converted.height,
                    "mode": "RGBA",
                }
    except Exception as error:
        raise RezError(f"Pillow failed to convert {kind}: {source}") from error


def prefix_kind(prefix: bytes) -> str | None:
    if (
        len(prefix) >= LITHTECH_WORLD_HEADER_BYTES
        and struct.unpack_from("<I", prefix)[0] == LITHTECH_WORLD_VERSION
    ):
        return "lithtech_world"
    if prefix.startswith(PNG_SIGNATURE):
        return "png"
    if prefix.startswith(DDS_MAGIC):
        return "dds"
    if prefix[:6] in GIF_SIGNATURES:
        return "gif"
    if prefix.startswith(JPEG_SIGNATURE):
        return "jpeg"
    if len(prefix) >= 16 and prefix[4:8] == b"ftyp":
        return "mp4"
    if prefix.startswith(EBML_SIGNATURE):
        return "webm"
    if prefix.startswith(CFB_SIGNATURE):
        return "cfb"
    if prefix.startswith(b"<start>\r\n") or prefix.startswith(b"<start>\n"):
        return "config"
    if prefix.startswith(b"/*! jQuery "):
        return "web_bundle"
    if prefix.startswith((b"body{", b"body {")):
        return "web_bundle"
    if prefix.startswith(b"//.overlay{"):
        return "web_bundle"
    if prefix.startswith(b"[") and b"]" in prefix[:128] and b"=" in prefix[:256]:
        return "ini"
    if len(prefix) >= 18:
        id_length, color_map_type, image_type = struct.unpack_from("<BBB", prefix)
        width, height = struct.unpack_from("<HH", prefix, 12)
        if (
            color_map_type == 0
            and image_type in {2, 3, 10, 11}
            and width
            and height
            and prefix[16] in {8, 24, 32}
        ):
            return "tga"
    if len(prefix) >= DTX_HEADER_BYTES:
        resource_type, version = struct.unpack_from("<ii", prefix)
        width, height, mipmaps, sections = struct.unpack_from("<HHHH", prefix, 8)
        if (
            resource_type in {0, 1}
            and version == -5
            and width
            and height
            and 1 <= mipmaps <= 32
            and sections == 0
            and prefix[26] in {3, 4, 5, 6}
        ):
            return "dtx"
    return None


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
            prefix = stream.read(DTX_HEADER_BYTES)
            kind = prefix_kind(prefix)
            if kind == "png":
                record = parse_png(stream, position, region_end)
                representation = "private_rez_crc_valid_png_frame"
            elif kind == "dds":
                record = parse_dds(stream, position, region_end)
                representation = "private_rez_header_sized_dds_frame"
            elif kind == "tga":
                record = parse_tga(stream, position, region_end)
                representation = "private_rez_packet_sized_tga_frame"
            elif kind == "dtx":
                record = parse_dtx(stream, position, region_end)
                representation = "private_rez_header_sized_dtx_frame"
            elif kind == "gif":
                record = parse_gif(stream, position, region_end)
                representation = "private_rez_block_complete_gif_frame"
            elif kind == "jpeg":
                record = parse_jpeg(stream, position, region_end)
                representation = "private_rez_marker_complete_jpeg_frame"
            elif kind == "config":
                record = parse_start_end_config(stream, position, region_end)
                representation = "private_rez_structured_ascii_config_frame"
            elif kind == "cfb":
                record = parse_cfb(stream, position, region_end)
                representation = "private_rez_fat_sized_cfb_frame"
            elif kind == "ini":
                record = parse_ini(stream, position, region_end)
                representation = "private_rez_structured_ascii_ini_frame"
            elif kind == "web_bundle":
                record = parse_ascii_web_bundle(stream, position, region_end)
                representation = "private_rez_binary_bounded_ascii_web_bundle"
            elif kind == "mp4":
                record = parse_mp4(stream, position, region_end)
                representation = "private_rez_box_sized_mp4_frame"
            elif kind == "webm":
                record = parse_webm(stream, position, region_end)
                representation = "private_rez_vint_sized_webm_frame"
            elif kind == "lithtech_world":
                record = parse_lithtech_world(stream, position, region_end)
                representation = "private_rez_strict_lithtech_world_v85_frame"
            else:
                break
            extension = {
                "jpeg": "jpg",
                "config": "txt",
                "web_bundle": "txt",
                "lithtech_world": "dat",
            }.get(str(kind), str(kind))
            destination = base / f"{kind}-{len(records):05d}.{extension}"
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
            record_outputs = [output_record]
            if kind in {"tga", "dtx"}:
                png, conversion = converted_png(destination, str(kind))
                converted_destination = base / f"converted-{len(records):05d}.png"
                converted_status = write_verified_bytes(converted_destination, png)
                converted_output = {
                    "path": str(converted_destination.relative_to(output)),
                    "bytes": len(png),
                    "sha256": hashlib.sha256(png).hexdigest(),
                    "representation": f"private_rez_{kind}_frame_to_png",
                    "status": converted_status,
                }
                record["conversion"] = conversion
                record_outputs.append(converted_output)
            record.update(
                {
                    "index": len(records),
                    "kind": kind,
                    "status": status,
                    "output": output_record,
                    "outputs": record_outputs,
                }
            )
            records.append(record)
            outputs.extend(record_outputs)
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
            "status": "unsupported_suffix_preserved",
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
            prefix = stream.read(DTX_HEADER_BYTES)
        if prefix_kind(prefix) is None:
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
        "tool_version": "7",
        "workspace": str(workspace),
        "root_inventory_sha256": index["inventory_sha256"],
        "safety": {
            "source_mode": "read_only",
            "unknown_executable_run": False,
            "sequential_from_data_offset_only": True,
            "signature_search_or_carving": False,
            "png_crc_required": True,
            "dds_dtx_header_sized_payload_required": True,
            "tga_pixel_or_rle_packet_accounting_required": True,
            "gif_sub_block_and_trailer_required": True,
            "jpeg_marker_and_eoi_required": True,
            "config_start_end_grammar_required": True,
            "cfb_difat_fat_extent_required": True,
            "ini_grammar_required": True,
            "web_bundle_printable_and_binary_successor_required": True,
            "mp4_top_level_box_chain_required": True,
            "webm_ebml_header_and_sized_segment_required": True,
            "lithtech_world_v85_render_tail_required": True,
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
            "lithtech_worlds": sum(
                item["representation"]
                == "private_rez_strict_lithtech_world_v85_frame"
                for item in all_outputs
            ),
            "lithtech_world_bytes": sum(
                int(item["bytes"])
                for item in all_outputs
                if item["representation"]
                == "private_rez_strict_lithtech_world_v85_frame"
            ),
            "framed_resources": sum(len(item["resources"]) for item in archives),
            "resource_kind_counts": dict(
                sorted(
                    Counter(
                        str(resource["kind"])
                        for archive in archives
                        for resource in archive["resources"]
                    ).items()
                )
            ),
            "raw_frame_bytes": sum(
                int(resource["bytes"])
                for archive in archives
                for resource in archive["resources"]
            ),
            "converted_png_images": sum(
                item["representation"].endswith("_frame_to_png")
                for item in all_outputs
            ),
            "converted_png_bytes": sum(
                int(item["bytes"])
                for item in all_outputs
                if item["representation"].endswith("_frame_to_png")
            ),
            "outputs": len(all_outputs),
            "output_bytes": sum(int(item["bytes"]) for item in all_outputs),
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
