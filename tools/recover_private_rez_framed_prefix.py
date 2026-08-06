#!/usr/bin/env python3
"""Recover a contiguous chain of strictly bounded resources from private REZ.

Parsing always starts at the fixed REZ data offset and stops at the first
unsupported byte.  It never searches for a later signature.  Supported frames
have self-proving boundaries or byte-identical loose peers: CRC-valid PNG,
header-sized DDS/DTX, complete TGA (including RLE packet accounting),
block-complete GIF, marker-complete JPEG, LithTech world v85, CFSprite v5,
CrossFire RPS tables, CRC-valid orphan IDAT chunks with known successors, and an
unambiguous chain of exact loose-file matches.
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
CFSPRITE_SIGNATURE = b"\x08\x00CFSprite"
CFSPRITE_VERSION = 5
CFSPRITE_SCHEMA = 9
CFSPRITE_TICK_RATE = 30
MAXIMUM_CFSPRITE_ITEMS = 100_000
MAXIMUM_CFSPRITE_STRING_BYTES = 4096
MAXIMUM_RPS_ITEMS = 100_000
MAXIMUM_RPS_STRING_BYTES = 4096
EXACT_PEER_PREFIX_BYTES = 16
SWF_SIGNATURES = {b"FWS", b"CWS"}
MAXIMUM_SWF_UNCOMPRESSED_BYTES = 512 * 1024 * 1024
MAXIMUM_SWF_TAGS = 1_000_000
FLV_SIGNATURE = b"FLV"
MAXIMUM_FLV_TAGS = 10_000_000
HTML_END_TAG = b"</html>"
WEB_BINARY_SUCCESSOR_KINDS = {
    "png",
    "dds",
    "gif",
    "jpeg",
    "mp4",
    "webm",
    "cfb",
    "swf",
    "flv",
    "lithtech_world",
    "tga",
    "dtx",
}


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


def index_exact_peers(peer_root: Path) -> dict[str, object]:
    """Index safe regular loose files by prefix without trusting their names."""
    buckets: dict[bytes, list[dict[str, object]]] = {}
    short: list[dict[str, object]] = []
    ignored_symlinks = 0
    for directory, directories, filenames in os.walk(peer_root, followlinks=False):
        base = Path(directory)
        safe_directories = []
        for name in sorted(directories):
            path = base / name
            if path.is_symlink():
                ignored_symlinks += 1
            else:
                safe_directories.append(name)
        directories[:] = safe_directories
        for name in sorted(filenames):
            path = base / name
            if path.is_symlink():
                ignored_symlinks += 1
                continue
            size = path.stat().st_size
            if size <= 0:
                continue
            with path.open("rb") as stream:
                prefix = stream.read(EXACT_PEER_PREFIX_BYTES)
            record = {
                "path": path,
                "relative_path": path.relative_to(peer_root).as_posix(),
                "bytes": size,
                "prefix": prefix,
            }
            if size < EXACT_PEER_PREFIX_BYTES:
                short.append(record)
            else:
                buckets.setdefault(prefix, []).append(record)
    return {
        "root": peer_root,
        "buckets": buckets,
        "short": short,
        "files": sum(len(items) for items in buckets.values()) + len(short),
        "ignored_symlinks": ignored_symlinks,
    }


def exact_peer_sha256(
    source: BinaryIO, offset: int, region_end: int, peer: dict[str, object]
) -> str | None:
    """Return the shared hash only when the peer exactly matches this span."""
    size = int(peer["bytes"])
    if offset + size > region_end:
        return None
    source.seek(offset)
    digest = hashlib.sha256()
    remaining = size
    with Path(peer["path"]).open("rb") as peer_stream:
        while remaining:
            source_data = source.read(min(CHUNK_SIZE, remaining))
            peer_data = peer_stream.read(len(source_data))
            if not source_data or source_data != peer_data:
                return None
            digest.update(source_data)
            remaining -= len(source_data)
        if peer_stream.read(1):
            raise RezError(f"exact peer grew while reading: {peer['relative_path']}")
    return digest.hexdigest()


def match_exact_peer(
    source: BinaryIO,
    offset: int,
    region_end: int,
    peer_index: dict[str, object],
) -> dict[str, object] | None:
    """Match one exact loose peer at an exact offset, rejecting length ambiguity."""
    source.seek(offset)
    prefix = source.read(min(EXACT_PEER_PREFIX_BYTES, region_end - offset))
    candidates = list(peer_index["buckets"].get(prefix, []))
    candidates.extend(
        peer
        for peer in peer_index["short"]
        if prefix.startswith(bytes(peer["prefix"]))
    )
    matches: list[tuple[dict[str, object], str]] = []
    for peer in candidates:
        digest = exact_peer_sha256(source, offset, region_end, peer)
        if digest is not None:
            matches.append((peer, digest))
    if not matches:
        return None
    lengths = sorted({int(peer["bytes"]) for peer, _ in matches})
    if len(lengths) != 1:
        raise RezError(
            f"ambiguous exact loose-peer lengths at {offset}: "
            + ", ".join(str(value) for value in lengths)
        )
    digests = {digest for _, digest in matches}
    if len(digests) != 1:
        raise RezError(f"exact peers disagree at {offset} despite equal lengths")
    aliases = sorted(str(peer["relative_path"]) for peer, _ in matches)
    return {
        "offset": offset,
        "bytes": lengths[0],
        "sha256": next(iter(digests)),
        "peer_paths": aliases,
        "peer_count": len(aliases),
    }


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


def parse_standalone_idat(
    stream: BinaryIO, offset: int, region_end: int
) -> dict[str, object]:
    """Preserve one CRC-valid orphan IDAT chunk before a known next frame."""
    if offset < 0 or offset + 12 > region_end:
        raise RezError(f"truncated standalone IDAT at {offset}")
    stream.seek(offset)
    header = stream.read(8)
    length, chunk_type = struct.unpack(">I4s", header)
    if chunk_type != b"IDAT" or length == 0 or length > MAXIMUM_PNG_CHUNK_BYTES:
        raise RezError(f"standalone IDAT header absent at exact offset {offset}")
    end = offset + 12 + length
    if end > region_end:
        raise RezError(f"standalone IDAT crosses REZ boundary at {offset}")
    data = stream.read(length)
    raw_crc = stream.read(4)
    expected_crc = zlib.crc32(chunk_type + data) & 0xFFFFFFFF
    actual_crc = struct.unpack(">I", raw_crc)[0]
    if actual_crc != expected_crc:
        raise RezError(f"standalone IDAT CRC mismatch at {offset}")
    stream.seek(end)
    successor = prefix_kind(stream.read(DTX_HEADER_BYTES))
    if successor is None or successor == "standalone_idat":
        raise RezError(f"standalone IDAT has no known successor at {end}")
    return {
        "offset": offset,
        "bytes": end - offset,
        "sha256": hash_region(stream, offset, end - offset),
        "payload_bytes": length,
        "crc32": f"{actual_crc:08x}",
        "next_frame_kind": successor,
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


def parse_cp949_web_bundle(
    stream: BinaryIO, offset: int, region_end: int
) -> dict[str, object]:
    """Parse a CP949 JavaScript/CSS bundle to a closed, exact binary successor."""
    stream.seek(offset)
    data = stream.read(
        min(MAXIMUM_TEXT_FRAME_BYTES + DTX_HEADER_BYTES, region_end - offset)
    )
    if not data.startswith(b"//val\r\n") or b"var " not in data[:128]:
        raise RezError(f"CP949 web bundle header absent at exact offset {offset}")
    index = 0
    while index < len(data):
        if index > 0:
            successor = prefix_kind(data[index : index + DTX_HEADER_BYTES])
            if successor in WEB_BINARY_SUCCESSOR_KINDS:
                bundle = data[:index]
                text = bundle.decode("cp949", errors="strict")
                if (
                    "function " not in text
                    or "body{" not in text
                    or not text.rstrip().endswith("}")
                    or text.count("{") != text.count("}")
                    or text.count("(") != text.count(")")
                    or text.count("[") != text.count("]")
                ):
                    raise RezError(f"incomplete CP949 web bundle grammar at {offset}")
                return {
                    "offset": offset,
                    "bytes": len(bundle),
                    "sha256": hashlib.sha256(bundle).hexdigest(),
                    "encoding": "cp949",
                    "bundle_kind": "javascript_and_css",
                    "non_ascii_characters": sum(ord(value) > 127 for value in text),
                    "next_frame_kind": successor,
                }
        byte = data[index]
        if byte >= 0x80:
            if index + 2 > len(data):
                raise RezError(f"truncated CP949 character at {offset + index}")
            try:
                data[index : index + 2].decode("cp949", errors="strict")
            except UnicodeDecodeError as error:
                raise RezError(f"invalid CP949 character at {offset + index}") from error
            index += 2
            continue
        if byte not in b"\t\n\r" and not 32 <= byte <= 126:
            raise RezError(f"invalid CP949 web bundle byte at {offset + index}")
        index += 1
    raise RezError(f"CP949 web bundle has no exact supported successor at {offset}")


def parse_ui_layout(
    stream: BinaryIO, offset: int, region_end: int
) -> dict[str, object]:
    """Parse a complete LithTech UI GROUP layout before an exact binary frame."""
    layout = read_ascii_prefix(stream, offset, region_end)
    if not layout.startswith(b"GROUP "):
        raise RezError(f"UI layout GROUP header absent at exact offset {offset}")
    try:
        text = layout.decode("ascii", errors="strict")
    except UnicodeDecodeError as error:
        raise RezError(f"UI layout is not ASCII at {offset}") from error
    group_names: list[str] = []
    default_names: list[str] = []
    component_counts: Counter[str] = Counter()
    component_open = False
    component_count = 0
    end_count = 0
    allowed_components = {"IMAGE", "STATIC", "BUTTON", "COMBOBUTTON", "SCROLLBAR"}
    for raw_line in text.replace("\r\n", "\n").split("\n"):
        line = raw_line.strip()
        if not line:
            continue
        if line.startswith("GROUP "):
            if component_open or len(line.split()) != 2:
                raise RezError(f"invalid UI GROUP line at {offset}: {line!r}")
            group_names.append(line.split()[1])
        elif line.startswith("DEFAULTGROUP "):
            if component_open or len(line.split()) != 2 or not group_names:
                raise RezError(f"invalid UI DEFAULTGROUP line at {offset}: {line!r}")
            name = line.split()[1]
            if name != group_names[-1]:
                raise RezError(f"UI default group does not match GROUP at {offset}")
            default_names.append(name)
        elif line == "-END":
            if not component_open:
                raise RezError(f"orphan UI component end at {offset}")
            component_open = False
            end_count += 1
        elif line.startswith("-"):
            if not component_open or len(line) < 2:
                raise RezError(f"UI property outside a component at {offset}: {line!r}")
        else:
            parts = line.split()
            if (
                component_open
                or len(parts) != 2
                or parts[0] not in allowed_components
                or len(default_names) != len(group_names)
            ):
                raise RezError(f"invalid UI component line at {offset}: {line!r}")
            component_open = True
            component_count += 1
            component_counts[parts[0]] += 1
    if (
        component_open
        or not group_names
        or group_names != default_names
        or component_count == 0
        or component_count != end_count
    ):
        raise RezError(f"incomplete UI layout grammar at {offset}")
    next_offset = offset + len(layout)
    stream.seek(next_offset)
    next_kind = prefix_kind(stream.read(DTX_HEADER_BYTES))
    if next_kind not in WEB_BINARY_SUCCESSOR_KINDS:
        raise RezError(f"UI layout has no exact binary successor at {next_offset}")
    return {
        "offset": offset,
        "bytes": len(layout),
        "sha256": hashlib.sha256(layout).hexdigest(),
        "encoding": "ascii",
        "groups": len(group_names),
        "group_names": group_names,
        "components": component_count,
        "component_counts": dict(sorted(component_counts.items())),
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


def validate_swf_body(body: bytes) -> dict[str, int]:
    """Validate the RECT, frame header, complete tag stream, and final End tag."""
    if len(body) < 6:
        raise RezError("truncated SWF frame body")
    rect_bits = body[0] >> 3
    if not 1 <= rect_bits <= 31:
        raise RezError(f"invalid SWF RECT bit width: {rect_bits}")
    rect_bytes = (5 + 4 * rect_bits + 7) // 8
    first_tag = rect_bytes + 4
    if first_tag > len(body):
        raise RezError("SWF RECT/frame header exceeds declared body")
    frame_rate_raw, frame_count = struct.unpack_from("<HH", body, rect_bytes)
    if frame_rate_raw == 0 or frame_count == 0:
        raise RezError("SWF has a zero frame rate or frame count")
    cursor = first_tag
    tag_count = 0
    while cursor + 2 <= len(body):
        header = struct.unpack_from("<H", body, cursor)[0]
        cursor += 2
        tag_code = header >> 6
        tag_bytes = header & 0x3F
        if tag_bytes == 0x3F:
            if cursor + 4 > len(body):
                raise RezError("truncated SWF long tag header")
            tag_bytes = struct.unpack_from("<I", body, cursor)[0]
            cursor += 4
        if cursor + tag_bytes > len(body):
            raise RezError(f"SWF tag {tag_code} exceeds declared body")
        cursor += tag_bytes
        tag_count += 1
        if tag_count > MAXIMUM_SWF_TAGS:
            raise RezError("SWF tag count exceeds safety limit")
        if tag_code == 0:
            if tag_bytes != 0:
                raise RezError("SWF End tag has a payload")
            if cursor != len(body):
                raise RezError("SWF bytes remain after the End tag")
            return {
                "rect_bits": rect_bits,
                "frame_rate_raw": frame_rate_raw,
                "frame_count": frame_count,
                "tag_count": tag_count,
            }
    raise RezError("SWF tag stream has no complete End tag")


def parse_swf(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse an exact FWS or self-delimiting CWS frame without scanning ahead."""
    if offset < 0 or offset + 8 > region_end:
        raise RezError(f"truncated SWF header at {offset}")
    stream.seek(offset)
    header = stream.read(8)
    signature = header[:3]
    version = header[3]
    declared_bytes = struct.unpack_from("<I", header, 4)[0]
    if signature not in SWF_SIGNATURES:
        raise RezError(f"SWF signature absent at exact offset {offset}")
    if not 1 <= version <= 50:
        raise RezError(f"unsupported SWF version at {offset}: {version}")
    if not 14 <= declared_bytes <= MAXIMUM_SWF_UNCOMPRESSED_BYTES:
        raise RezError(f"implausible SWF declared length at {offset}: {declared_bytes}")
    expected_body_bytes = declared_bytes - 8
    if signature == b"FWS":
        if offset + declared_bytes > region_end:
            raise RezError(f"FWS frame crosses REZ data boundary at {offset}")
        body = stream.read(expected_body_bytes)
        if len(body) != expected_body_bytes:
            raise RezError(f"truncated FWS body at {offset}")
        frame_bytes = declared_bytes
        compression = "none"
    else:
        decoder = zlib.decompressobj()
        body_buffer = bytearray()
        total_read = 0
        pending = b""
        while not decoder.eof:
            if not pending:
                remaining_region = region_end - stream.tell()
                if remaining_region <= 0:
                    raise RezError(f"truncated CWS Zlib stream at {offset}")
                pending = stream.read(min(CHUNK_SIZE, remaining_region))
                total_read += len(pending)
            try:
                decoded = decoder.decompress(
                    pending, expected_body_bytes - len(body_buffer) + 1
                )
            except zlib.error as error:
                raise RezError(f"invalid CWS Zlib stream at {offset}: {error}") from error
            body_buffer.extend(decoded)
            if len(body_buffer) > expected_body_bytes:
                raise RezError(f"CWS expands beyond its declared length at {offset}")
            pending = decoder.unconsumed_tail
        compressed_body_bytes = total_read - len(decoder.unused_data)
        if compressed_body_bytes <= 0:
            raise RezError(f"empty CWS Zlib stream at {offset}")
        body = bytes(body_buffer)
        if len(body) != expected_body_bytes:
            raise RezError(
                f"CWS declared length mismatch at {offset}: "
                f"expected={expected_body_bytes} actual={len(body)}"
            )
        frame_bytes = 8 + compressed_body_bytes
        compression = "zlib"
    structure = validate_swf_body(body)
    return {
        "offset": offset,
        "bytes": frame_bytes,
        "sha256": hash_region(stream, offset, frame_bytes),
        "signature": signature.decode("ascii"),
        "version": version,
        "declared_uncompressed_bytes": declared_bytes,
        "compression": compression,
        **structure,
    }


def parse_flv_on_metadata(payload: bytes) -> dict[str, object]:
    """Read the bounded primitive fields used by an FLV onMetaData object."""
    cursor = 0
    if len(payload) < 8 or payload[cursor] != 2:
        raise RezError("FLV script tag does not begin with an AMF string")
    cursor += 1
    name_bytes = struct.unpack_from(">H", payload, cursor)[0]
    cursor += 2
    if cursor + name_bytes > len(payload):
        raise RezError("truncated FLV metadata event name")
    name = payload[cursor : cursor + name_bytes]
    cursor += name_bytes
    if name != b"onMetaData" or cursor + 5 > len(payload) or payload[cursor] != 8:
        raise RezError("FLV first script tag is not an onMetaData ECMA array")
    cursor += 1
    declared_fields = struct.unpack_from(">I", payload, cursor)[0]
    cursor += 4
    values: dict[str, object] = {}
    parsed_fields = 0
    while cursor + 3 <= len(payload):
        if payload[cursor : cursor + 3] == b"\0\0\x09":
            cursor += 3
            if cursor != len(payload):
                raise RezError("bytes remain after FLV onMetaData object end")
            return {
                "declared_fields": declared_fields,
                "parsed_fields": parsed_fields,
                **values,
            }
        key_bytes = struct.unpack_from(">H", payload, cursor)[0]
        cursor += 2
        if cursor + key_bytes + 1 > len(payload):
            raise RezError("truncated FLV metadata key")
        key = payload[cursor : cursor + key_bytes].decode("utf-8", errors="strict")
        cursor += key_bytes
        value_type = payload[cursor]
        cursor += 1
        if value_type == 0:
            if cursor + 8 > len(payload):
                raise RezError(f"truncated FLV number metadata field {key}")
            value = struct.unpack_from(">d", payload, cursor)[0]
            cursor += 8
            if value != value or abs(value) == float("inf"):
                raise RezError(f"non-finite FLV metadata field {key}")
        elif value_type == 1:
            if cursor >= len(payload) or payload[cursor] not in {0, 1}:
                raise RezError(f"invalid FLV boolean metadata field {key}")
            value = bool(payload[cursor])
            cursor += 1
        elif value_type == 2:
            if cursor + 2 > len(payload):
                raise RezError(f"truncated FLV string metadata field {key}")
            value_bytes = struct.unpack_from(">H", payload, cursor)[0]
            cursor += 2
            if cursor + value_bytes > len(payload):
                raise RezError(f"truncated FLV string metadata value {key}")
            value = payload[cursor : cursor + value_bytes].decode(
                "utf-8", errors="replace"
            )
            cursor += value_bytes
        else:
            raise RezError(f"unsupported FLV metadata type {value_type} for {key}")
        values[key] = value
        parsed_fields += 1
    raise RezError("FLV onMetaData object has no complete end marker")


def parse_flv(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse a complete FLV tag chain ending at the region or a known successor."""
    if offset < 0 or offset + 13 > region_end:
        raise RezError(f"truncated FLV header at {offset}")
    stream.seek(offset)
    header = stream.read(9)
    if header[:3] != FLV_SIGNATURE or header[3] != 1:
        raise RezError(f"FLV v1 header absent at exact offset {offset}")
    flags = header[4]
    if flags & ~0x05 or not flags:
        raise RezError(f"invalid FLV type flags at {offset}: 0x{flags:02x}")
    data_offset = struct.unpack_from(">I", header, 5)[0]
    if data_offset < 9 or offset + data_offset + 4 > region_end:
        raise RezError(f"invalid FLV data offset at {offset}: {data_offset}")
    stream.seek(offset + data_offset)
    if stream.read(4) != b"\0\0\0\0":
        raise RezError(f"FLV first PreviousTagSize is not zero at {offset}")
    position = offset + data_offset + 4
    counts: Counter[str] = Counter()
    tag_count = 0
    metadata: dict[str, object] | None = None
    last_media_timestamp = -1
    next_frame_kind = None
    while position < region_end:
        stream.seek(position)
        prefix = stream.read(DTX_HEADER_BYTES)
        successor = prefix_kind(prefix)
        if successor is not None and successor != "flv" and tag_count:
            next_frame_kind = successor
            break
        if position + 15 > region_end:
            raise RezError(f"truncated FLV tag at {position}")
        stream.seek(position)
        tag_header = stream.read(11)
        tag_type = tag_header[0]
        data_bytes = int.from_bytes(tag_header[1:4], "big")
        timestamp = int.from_bytes(tag_header[4:7], "big") | (tag_header[7] << 24)
        stream_id = int.from_bytes(tag_header[8:11], "big")
        if tag_type not in {8, 9, 18} or stream_id != 0:
            raise RezError(f"invalid FLV tag header at {position}")
        tag_end = position + 11 + data_bytes
        if tag_end + 4 > region_end:
            raise RezError(f"FLV tag crosses REZ data boundary at {position}")
        if tag_type in {8, 9}:
            if timestamp < last_media_timestamp:
                raise RezError(f"FLV media timestamp regresses at {position}")
            last_media_timestamp = timestamp
        if tag_type == 18 and metadata is None:
            stream.seek(position + 11)
            metadata = parse_flv_on_metadata(stream.read(data_bytes))
        stream.seek(tag_end)
        previous_size = struct.unpack(">I", stream.read(4))[0]
        if previous_size != 11 + data_bytes:
            raise RezError(f"FLV PreviousTagSize mismatch at {position}")
        counts[{8: "audio", 9: "video", 18: "script"}[tag_type]] += 1
        tag_count += 1
        if tag_count > MAXIMUM_FLV_TAGS:
            raise RezError("FLV tag count exceeds safety limit")
        position = tag_end + 4
    if not tag_count or metadata is None:
        raise RezError(f"FLV has no tags or onMetaData at {offset}")
    duration = metadata.get("duration")
    if not isinstance(duration, float) or duration <= 0:
        raise RezError(f"FLV has no positive metadata duration at {offset}")
    if metadata.get("canSeekToEnd") is not True:
        raise RezError(f"FLV metadata does not confirm seek-to-end at {offset}")
    if last_media_timestamp < 0 or abs(duration * 1000 - last_media_timestamp) > 1000:
        raise RezError(
            f"FLV duration/timestamp mismatch at {offset}: "
            f"duration_ms={duration * 1000} last={last_media_timestamp}"
        )
    frame_bytes = position - offset
    return {
        "offset": offset,
        "bytes": frame_bytes,
        "sha256": hash_region(stream, offset, frame_bytes),
        "version": 1,
        "flags": flags,
        "tag_count": tag_count,
        "tag_counts": dict(sorted(counts.items())),
        "duration_seconds": duration,
        "last_media_timestamp_ms": last_media_timestamp,
        "next_frame_kind": next_frame_kind,
        "metadata_declared_fields": metadata["declared_fields"],
        "metadata_parsed_fields": metadata["parsed_fields"],
    }


def parse_html(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse one ASCII HTML document through its unique explicit closing tag."""
    if offset < 0 or offset >= region_end:
        raise RezError(f"invalid HTML offset {offset}")
    stream.seek(offset)
    data = stream.read(min(MAXIMUM_TEXT_FRAME_BYTES, region_end - offset))
    lower = data.lower()
    doctype_end = lower.find(b">")
    if (
        not lower.startswith(b"<!")
        or doctype_end < 0
        or b"doctype html" not in lower[: doctype_end + 1]
    ):
        raise RezError(f"HTML doctype absent at exact offset {offset}")
    end_index = lower.find(HTML_END_TAG)
    if end_index < 0:
        raise RezError(f"HTML closing tag absent within safety bound at {offset}")
    end = end_index + len(HTML_END_TAG)
    document = data[:end]
    lowered_document = lower[:end]
    if any(byte not in b"\t\n\r" and not 32 <= byte <= 126 for byte in document):
        raise RezError(f"HTML document is not bounded ASCII at {offset}")
    required = (b"<html", b"<head", b"</head>", b"<body", b"</body>", HTML_END_TAG)
    positions = [lowered_document.find(marker) for marker in required]
    if any(position < 0 for position in positions) or positions != sorted(positions):
        raise RezError(f"HTML document structure is incomplete at {offset}")
    if lowered_document.count(HTML_END_TAG) != 1:
        raise RezError(f"HTML document has an ambiguous closing tag at {offset}")
    return {
        "offset": offset,
        "bytes": len(document),
        "sha256": hashlib.sha256(document).hexdigest(),
        "encoding": "ascii",
        "doctype": document[2:doctype_end].decode("ascii").strip(),
        "explicit_end_tag": True,
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


class CFSpriteReader:
    """Bounded reader for CrossFire's unaligned CFSprite v5 stream."""

    def __init__(self, stream: BinaryIO, position: int, region_end: int):
        self.stream = stream
        self.position = position
        self.region_end = region_end

    def read(self, size: int, label: str) -> bytes:
        if size < 0 or self.position + size > self.region_end:
            raise RezError(f"truncated CFSprite {label} at {self.position}")
        self.stream.seek(self.position)
        value = self.stream.read(size)
        if len(value) != size:
            raise RezError(f"truncated CFSprite {label} at {self.position}")
        self.position += size
        return value

    def u8(self, label: str) -> int:
        return self.read(1, label)[0]

    def u16(self, label: str) -> int:
        return struct.unpack("<H", self.read(2, label))[0]

    def u32(self, label: str) -> int:
        return struct.unpack("<I", self.read(4, label))[0]

    def floats(self, count: int, label: str) -> tuple[float, ...]:
        values = struct.unpack(f"<{count}f", self.read(count * 4, label))
        if not all(value == value and abs(value) != float("inf") for value in values):
            raise RezError(f"non-finite CFSprite {label} at {self.position}")
        return values

    def string(self, label: str) -> str:
        length = self.u16(f"{label} length")
        if length == 0 or length > MAXIMUM_CFSPRITE_STRING_BYTES:
            raise RezError(f"invalid CFSprite {label} length: {length}")
        raw = self.read(length, label)
        if b"\0" in raw:
            raise RezError(f"CFSprite {label} contains an embedded NUL")
        try:
            value = raw.decode("ascii")
        except UnicodeDecodeError as error:
            raise RezError(f"non-ASCII CFSprite {label}") from error
        if any(ord(character) < 0x20 or ord(character) == 0x7F for character in value):
            raise RezError(f"control character in CFSprite {label}")
        return value


def parse_cfsprite(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse one self-delimiting CrossFire CFSprite v5 resource."""
    if offset < 0 or offset + len(CFSPRITE_SIGNATURE) > region_end:
        raise RezError(f"truncated CFSprite signature at {offset}")
    reader = CFSpriteReader(stream, offset, region_end)
    if reader.string("class name") != "CFSprite":
        raise RezError(f"CFSprite signature absent at exact offset {offset}")
    version = reader.u32("version")
    schema = reader.u32("schema")
    reserved = reader.u32("header reserved")
    tick_rate = reader.u32("tick rate")
    width = reader.u32("width")
    height = reader.u32("height")
    item_count = reader.u32("item count")
    if (
        version != CFSPRITE_VERSION
        or schema != CFSPRITE_SCHEMA
        or reserved != 0
        or tick_rate != CFSPRITE_TICK_RATE
    ):
        raise RezError(f"unsupported CFSprite v5 header at {offset}")
    if not 1 <= width <= 65536 or not 1 <= height <= 65536:
        raise RezError(f"invalid CFSprite canvas at {offset}: {width}x{height}")
    if not 1 <= item_count <= MAXIMUM_CFSPRITE_ITEMS:
        raise RezError(f"implausible CFSprite item count at {offset}: {item_count}")

    keyframe_count = 0
    names: list[str] = []
    paths: list[str] = []
    for item_index in range(item_count):
        label = f"item {item_index}"
        stored_index = reader.u32(f"{label} index")
        if stored_index != item_index:
            raise RezError(
                f"CFSprite {label} index mismatch: expected {item_index}, got {stored_index}"
            )
        name = reader.string(f"{label} name")
        resource_flags = tuple(reader.u32(f"{label} resource flag {i}") for i in range(3))
        filename = reader.string(f"{label} filename")
        path = reader.string(f"{label} path")
        if resource_flags[:2] != (1, 1) or resource_flags[2] == 0:
            raise RezError(f"invalid CFSprite {label} resource flags: {resource_flags}")
        if not filename.lower().endswith(".png") or not path.lower().endswith(".png"):
            raise RezError(f"CFSprite {label} does not reference a PNG")
        if "\\" in path or path.startswith("/") or ".." in path.split("/"):
            raise RezError(f"unsafe CFSprite {label} path: {path!r}")

        item_reserved = reader.u16(f"{label} reserved")
        rectangle = tuple(reader.u32(f"{label} rectangle {i}") for i in range(4))
        flags = reader.u32(f"{label} flags")
        flags_reserved = reader.u32(f"{label} flags reserved")
        enabled = reader.u8(f"{label} enabled")
        mode = reader.u8(f"{label} mode")
        item_keyframes = reader.u32(f"{label} keyframe count")
        if item_reserved != 0 or flags != 11 or flags_reserved != 0:
            raise RezError(f"invalid CFSprite {label} fixed fields")
        if any(value == 0 or value > 65536 for value in rectangle):
            raise RezError(f"invalid CFSprite {label} rectangle: {rectangle}")
        if enabled != 1 or mode not in {0, 1}:
            raise RezError(f"invalid CFSprite {label} state: enabled={enabled} mode={mode}")
        if not 1 <= item_keyframes <= MAXIMUM_CFSPRITE_ITEMS:
            raise RezError(f"implausible CFSprite {label} keyframe count: {item_keyframes}")

        previous_tick = -1
        resource_index = resource_flags[2]
        for frame_index in range(item_keyframes):
            frame_label = f"{label} keyframe {frame_index}"
            tick = reader.u32(f"{frame_label} tick")
            stored_resource_index = reader.u32(f"{frame_label} resource index")
            repeated_tick = reader.u32(f"{frame_label} repeated tick")
            transform = reader.floats(7, f"{frame_label} transform")
            ending_tick = reader.u32(f"{frame_label} ending tick")
            reader.read(4, f"{frame_label} color")
            if tick <= previous_tick or repeated_tick != tick or ending_tick != tick:
                raise RezError(f"invalid CFSprite {frame_label} tick sequence")
            if stored_resource_index != resource_index:
                raise RezError(f"invalid CFSprite {frame_label} resource reference")
            if any(abs(value) > 1_000_000 for value in transform):
                raise RezError(f"implausible CFSprite {frame_label} transform")
            previous_tick = tick
        keyframe_count += item_keyframes
        if keyframe_count > MAXIMUM_CFSPRITE_ITEMS:
            raise RezError(f"implausible total CFSprite keyframe count at {offset}")
        names.append(name)
        paths.append(path)

    size = reader.position - offset
    return {
        "offset": offset,
        "bytes": size,
        "sha256": hash_region(stream, offset, size),
        "version": version,
        "schema": schema,
        "tick_rate": tick_rate,
        "width": width,
        "height": height,
        "items": item_count,
        "keyframes": keyframe_count,
        "names": names,
        "paths": paths,
    }


def parse_rps(stream: BinaryIO, offset: int, region_end: int) -> dict[str, object]:
    """Parse one CrossFire resource-path table with its explicit index tail."""
    if offset < 0 or offset + 12 > region_end:
        raise RezError(f"truncated RPS resource-path table at {offset}")
    position = offset

    def read(size: int, label: str) -> bytes:
        nonlocal position
        if size < 0 or position + size > region_end:
            raise RezError(f"truncated RPS {label} at {position}")
        stream.seek(position)
        value = stream.read(size)
        if len(value) != size:
            raise RezError(f"truncated RPS {label} at {position}")
        position += size
        return value

    def u16(label: str) -> int:
        return struct.unpack("<H", read(2, label))[0]

    def u32(label: str) -> int:
        return struct.unpack("<I", read(4, label))[0]

    path_count = u32("path count")
    if path_count > MAXIMUM_RPS_ITEMS:
        raise RezError(f"implausible RPS path count at {offset}: {path_count}")
    paths: list[str] = []
    for path_index in range(path_count):
        length = u16(f"path {path_index} length")
        if length == 0 or length > MAXIMUM_RPS_STRING_BYTES:
            raise RezError(f"invalid RPS path {path_index} length: {length}")
        raw = read(length, f"path {path_index}")
        if b"\0" in raw:
            raise RezError(f"RPS path {path_index} contains an embedded NUL")
        try:
            path = raw.decode("cp949")
        except UnicodeDecodeError as error:
            raise RezError(f"invalid CP949 RPS path {path_index}") from error
        if any(ord(character) < 0x20 or ord(character) == 0x7F for character in path):
            raise RezError(f"control character in RPS path {path_index}")
        normalized = path.replace("\\", "/").lower()
        if not normalized.endswith((".png", ".jpg", ".jpeg", ".tga", ".dtx")):
            raise RezError(f"unsupported RPS path suffix at {path_index}: {path!r}")
        paths.append(path)

    index_count = u32("index count")
    if not 1 <= index_count <= MAXIMUM_RPS_ITEMS:
        raise RezError(f"implausible RPS index count at {offset}: {index_count}")
    indices = [u32(f"index {index}") for index in range(index_count)]
    if indices != list(range(index_count)):
        raise RezError(f"non-sequential RPS index table at {offset}: {indices[:16]}")
    if u32("reserved") != 0:
        raise RezError(f"nonzero RPS reserved field at {offset}")

    size = position - offset
    return {
        "offset": offset,
        "bytes": size,
        "sha256": hash_region(stream, offset, size),
        "path_count": path_count,
        "index_count": index_count,
        "paths": paths,
        "indices": indices,
        "encoding": "cp949",
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
    if prefix.startswith(CFSPRITE_SIGNATURE):
        return "cfsprite"
    if len(prefix) >= 16:
        rps_path_count = struct.unpack_from("<I", prefix)[0]
        if rps_path_count == 0 and prefix[:16] == struct.pack("<4I", 0, 1, 0, 0):
            return "rps"
        if 1 <= rps_path_count <= MAXIMUM_RPS_ITEMS:
            rps_first_length = struct.unpack_from("<H", prefix, 4)[0]
            if (
                1 <= rps_first_length <= min(MAXIMUM_RPS_STRING_BYTES, len(prefix) - 6)
                and b"\0" not in prefix[6 : 6 + rps_first_length]
                and prefix[6 : 6 + rps_first_length]
                .replace(b"\\", b"/")
                .lower()
                .endswith((b".png", b".jpg", b".jpeg", b".tga", b".dtx"))
            ):
                return "rps"
    if prefix.startswith(b"GROUP ") and b"DEFAULTGROUP " in prefix[:128]:
        return "ui_layout"
    if prefix.startswith(b"//val\r\n") and b"var " in prefix[:128]:
        return "cp949_web_bundle"
    lower_prefix = prefix[:64].lower()
    if lower_prefix.startswith(b"<!") and b"doctype html" in lower_prefix:
        return "html"
    if (
        len(prefix) >= 9
        and prefix[:3] == FLV_SIGNATURE
        and prefix[3] == 1
        and prefix[4] & ~0x05 == 0
        and prefix[4] != 0
        and struct.unpack_from(">I", prefix, 5)[0] >= 9
    ):
        return "flv"
    if (
        len(prefix) >= 8
        and prefix[:3] in SWF_SIGNATURES
        and 1 <= prefix[3] <= 50
        and 14 <= struct.unpack_from("<I", prefix, 4)[0]
        <= MAXIMUM_SWF_UNCOMPRESSED_BYTES
    ):
        return "swf"
    if (
        len(prefix) >= LITHTECH_WORLD_HEADER_BYTES
        and struct.unpack_from("<I", prefix)[0] == LITHTECH_WORLD_VERSION
    ):
        return "lithtech_world"
    if prefix.startswith(PNG_SIGNATURE):
        return "png"
    if (
        len(prefix) >= 12
        and prefix[4:8] == b"IDAT"
        and 0 < struct.unpack_from(">I", prefix)[0] <= MAXIMUM_PNG_CHUNK_BYTES
    ):
        return "standalone_idat"
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
    source: Path,
    source_sha256: str,
    region_end: int,
    output: Path,
    peer_root: Path | None = None,
) -> dict[str, object]:
    base = output / "private-rez-png-prefix" / f"{source.stem}__{source_sha256[:12]}"
    records: list[dict[str, object]] = []
    outputs: list[dict[str, object]] = []
    position = parse_rez_header(source)["data_offset"]
    peer_index = index_exact_peers(peer_root) if peer_root is not None else None
    with source.open("rb") as stream:
        while position < region_end:
            stream.seek(position)
            prefix = stream.read(DTX_HEADER_BYTES)
            kind = prefix_kind(prefix)
            if kind == "png":
                record = parse_png(stream, position, region_end)
                representation = "private_rez_crc_valid_png_frame"
            elif kind == "standalone_idat":
                record = parse_standalone_idat(stream, position, region_end)
                representation = "private_rez_crc_valid_standalone_idat_chunk"
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
            elif kind == "cp949_web_bundle":
                record = parse_cp949_web_bundle(stream, position, region_end)
                representation = "private_rez_grammar_bounded_cp949_web_bundle"
            elif kind == "ui_layout":
                record = parse_ui_layout(stream, position, region_end)
                representation = "private_rez_grammar_bounded_ui_layout"
            elif kind == "mp4":
                record = parse_mp4(stream, position, region_end)
                representation = "private_rez_box_sized_mp4_frame"
            elif kind == "webm":
                record = parse_webm(stream, position, region_end)
                representation = "private_rez_vint_sized_webm_frame"
            elif kind == "swf":
                record = parse_swf(stream, position, region_end)
                representation = (
                    "private_rez_zlib_complete_swf_frame"
                    if record["compression"] == "zlib"
                    else "private_rez_declared_length_swf_frame"
                )
            elif kind == "flv":
                record = parse_flv(stream, position, region_end)
                representation = "private_rez_complete_metadata_bounded_flv_frame"
            elif kind == "html":
                record = parse_html(stream, position, region_end)
                representation = "private_rez_explicit_end_tag_html_frame"
            elif kind == "lithtech_world":
                record = parse_lithtech_world(stream, position, region_end)
                representation = "private_rez_strict_lithtech_world_v85_frame"
            elif kind == "cfsprite":
                record = parse_cfsprite(stream, position, region_end)
                representation = "private_rez_strict_cfsprite_v5_frame"
            elif kind == "rps":
                record = parse_rps(stream, position, region_end)
                representation = "private_rez_strict_cp949_rps_frame"
            elif peer_index is not None:
                record = match_exact_peer(stream, position, region_end, peer_index)
                if record is None:
                    break
                kind = "exact_peer"
                representation = "private_rez_exact_loose_peer_frame"
            else:
                break
            if peer_index is not None and kind != "exact_peer":
                peer_match = match_exact_peer(stream, position, region_end, peer_index)
                if peer_match is not None:
                    if (
                        peer_match["bytes"] != record["bytes"]
                        or peer_match["sha256"] != record["sha256"]
                    ):
                        raise RezError(
                            f"self-bounded frame and exact peer disagree at {position}"
                        )
                    record["peer_paths"] = peer_match["peer_paths"]
                    record["peer_count"] = peer_match["peer_count"]
            if kind == "exact_peer":
                extension = (
                    Path(str(record["peer_paths"][0])).suffix.lower().lstrip(".")
                    or "bin"
                )
            else:
                extension = {
                    "jpeg": "jpg",
                    "config": "txt",
                    "web_bundle": "txt",
                    "cp949_web_bundle": "txt",
                    "ui_layout": "txt",
                    "lithtech_world": "dat",
                    "cfsprite": "xfi",
                    "rps": "rps",
                    "standalone_idat": "idat",
                    "html": "html",
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
        trailing_sha256 = hash_region(stream, position, region_end - position)
    return {
        "resources": records,
        "outputs": outputs,
        "exact_peer_index": (
            {
                "root": str(Path(peer_index["root"])),
                "files": peer_index["files"],
                "ignored_symlinks": peer_index["ignored_symlinks"],
            }
            if peer_index is not None
            else None
        ),
        "trailing_region": {
            "offset": position,
            "bytes": region_end - position,
            "sha256": trailing_sha256,
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
        peer_root = None
        if prefix_kind(prefix) is None:
            candidate_peer_root = source.with_suffix("")
            if not candidate_peer_root.is_dir():
                continue
            peer_root = candidate_peer_root
        actual_sha256 = sha256_file(source)
        if actual_sha256 != sample["sha256"]:
            raise RezError(f"source hash mismatch: {sample['path']}")
        recovered = recover_framed_prefix(
            source,
            actual_sha256,
            header["root_offset"],
            output,
            peer_root=peer_root,
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
        "tool_version": "15",
        "workspace": str(workspace),
        "root_inventory_sha256": index["inventory_sha256"],
        "safety": {
            "source_mode": "read_only",
            "unknown_executable_run": False,
            "sequential_from_data_offset_only": True,
            "signature_search_or_carving": False,
            "png_crc_required": True,
            "standalone_idat_crc_and_known_successor_required": True,
            "dds_dtx_header_sized_payload_required": True,
            "tga_pixel_or_rle_packet_accounting_required": True,
            "gif_sub_block_and_trailer_required": True,
            "jpeg_marker_and_eoi_required": True,
            "config_start_end_grammar_required": True,
            "cfb_difat_fat_extent_required": True,
            "ini_grammar_required": True,
            "web_bundle_printable_and_binary_successor_required": True,
            "cp949_web_bundle_grammar_and_binary_successor_required": True,
            "ui_layout_group_component_and_binary_successor_required": True,
            "mp4_top_level_box_chain_required": True,
            "webm_ebml_header_and_sized_segment_required": True,
            "swf_declared_length_complete_tag_stream_and_end_required": True,
            "cws_zlib_eof_required": True,
            "flv_tag_sizes_metadata_duration_and_exact_end_required": True,
            "html_doctype_structure_and_unique_end_tag_required": True,
            "lithtech_world_v85_render_tail_required": True,
            "cfsprite_v5_counts_indices_ticks_and_finite_transforms_required": True,
            "rps_cp949_paths_sequential_indices_and_zero_reserved_required": True,
            "exact_loose_peer_byte_equality_required": True,
            "exact_loose_peer_unique_length_required": True,
            "loose_peer_symlinks_ignored": True,
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
            "standalone_idat_chunks": sum(
                item["representation"]
                == "private_rez_crc_valid_standalone_idat_chunk"
                for item in all_outputs
            ),
            "standalone_idat_bytes": sum(
                int(item["bytes"])
                for item in all_outputs
                if item["representation"]
                == "private_rez_crc_valid_standalone_idat_chunk"
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
            "cfsprite_files": sum(
                item["representation"] == "private_rez_strict_cfsprite_v5_frame"
                for item in all_outputs
            ),
            "cfsprite_bytes": sum(
                int(item["bytes"])
                for item in all_outputs
                if item["representation"] == "private_rez_strict_cfsprite_v5_frame"
            ),
            "rps_files": sum(
                item["representation"] == "private_rez_strict_cp949_rps_frame"
                for item in all_outputs
            ),
            "rps_bytes": sum(
                int(item["bytes"])
                for item in all_outputs
                if item["representation"] == "private_rez_strict_cp949_rps_frame"
            ),
            "swf_files": sum(
                item["representation"].endswith("_swf_frame")
                for item in all_outputs
            ),
            "swf_bytes": sum(
                int(item["bytes"])
                for item in all_outputs
                if item["representation"].endswith("_swf_frame")
            ),
            "flv_files": sum(
                item["representation"]
                == "private_rez_complete_metadata_bounded_flv_frame"
                for item in all_outputs
            ),
            "flv_bytes": sum(
                int(item["bytes"])
                for item in all_outputs
                if item["representation"]
                == "private_rez_complete_metadata_bounded_flv_frame"
            ),
            "html_files": sum(
                item["representation"] == "private_rez_explicit_end_tag_html_frame"
                for item in all_outputs
            ),
            "html_bytes": sum(
                int(item["bytes"])
                for item in all_outputs
                if item["representation"] == "private_rez_explicit_end_tag_html_frame"
            ),
            "cp949_web_bundles": sum(
                item["representation"]
                == "private_rez_grammar_bounded_cp949_web_bundle"
                for item in all_outputs
            ),
            "cp949_web_bundle_bytes": sum(
                int(item["bytes"])
                for item in all_outputs
                if item["representation"]
                == "private_rez_grammar_bounded_cp949_web_bundle"
            ),
            "ui_layouts": sum(
                item["representation"] == "private_rez_grammar_bounded_ui_layout"
                for item in all_outputs
            ),
            "ui_layout_bytes": sum(
                int(item["bytes"])
                for item in all_outputs
                if item["representation"] == "private_rez_grammar_bounded_ui_layout"
            ),
            "exact_peer_resources": sum(
                "peer_paths" in resource
                for archive in archives
                for resource in archive["resources"]
            ),
            "exact_peer_bytes": sum(
                int(resource["bytes"])
                for archive in archives
                for resource in archive["resources"]
                if "peer_paths" in resource
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
