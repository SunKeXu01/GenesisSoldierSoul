#!/usr/bin/env python3
"""Strictly decode CrossFire's position-keyed REZ directory tables.

The compatibility key is archive-format data.  Parsing is deliberately strict:
every range, name, hexadecimal MD5, resource extent, and duplicate offset is
validated before an index is returned.
"""

from __future__ import annotations

import dataclasses
import re
import struct
from pathlib import Path, PurePosixPath

from rez_extract import RezError


CROSSFIRE_REZ_DIRECTORY_KEY = bytes.fromhex(
    "f0f09d090a66ad6a851dfd3f5123e7f3b10e78ecd1507b6b173f61c5790c5732"
    "1af3b86b68de2a5f01ba983a99c0540224f79b098723c46f0e6c44fadbfbe885"
    "abc2653c0ec493f66d0b8ad6118de38f71525d6efcfd2982b01d1311ae5cd5a9"
    "1bf8cefc799c5ad6cefd0c64ca601612315b083acf043eea23dc28fa20a5c0b8"
    "21735e6c6a2b31e96dbd9a73114cb1433a8e28cedc9bd431cf771de49f8a8b0a"
    "b24ec08ddd740b56cfb7eed574a7b51ba1a985cb4568ff1f59fbcd42daff5937"
    "05e7dc9e12bd1b87bb97029ac20466d3bea72c11664e10bda8b354c2c0398d17"
    "91dae021868ad324374a10130a3845e226c666c0de739b53e22d0a577eacc9c4"
    "0c0433d5fa9fe5158afd95cf9a571602b281be398c3a726a6f348a2f840eee96"
    "6d8083bc6a0245843a1c49a001b7da2c7696ff1d8e49a7caf5d6b0bd7f512125"
    "eaacb71516f624d70e5427960decd496c900334d43838c7b595e96af5facc34a"
    "f923fc627bfff5b90c916a01cdc987bb43fca4e7490db5c7c35a95f75291781d"
    "52c4bc635ae46a117bff8d728e64b553b807dd4e7f4df43599964ac6c6b720f6"
    "eba9a118afa77707e20b49bae112605541dda82103e55b8f811e8d8b6a11e06f"
    "f92f96c1ba8e4d0606629ae89266ccfb347b114234bc3ddc633e7af72cd41960"
    "f5f3c5e1f91d5fb4efefba4eb1357bbd261d61d0b0f42c6564846bfb3c746de1"
    "93d298362a185ffae2e1237c8c932e53ee40232c56f3fbb3ecbcfac706a6c04b"
    "cce8bbc14c84410167a28f43b2d6eab6a4a021f7455ebc8e9ff203cc3b5f3536"
    "d49118c39ea6363244e0fab2f191ef1f9d396610da18c2fe66739fbac8d22c7b"
    "236ad9bd9e02b2357e879e1b589ac10670493d9ab4469f4d67cb2a82dc754a32"
    "7050686e0a5c65f25ec4f60e34042324f34b30f3b24e260207c83d54e5fb6fb4"
    "b05e71d8e1b944926902bb5c162416703efd09bdf2d269e7ee74b3a1925ac099"
    "1af2dd3a625e817d66f0e914ca8fdd24a65ad4d8d3b8bb03031da619d1c69eba"
    "25a8d8160bcf8d5c5b78b9886019fbb8c1a0d965f324af9f6a4f72acd2b3ac2f"
    "875ccb2b9ad01c188fc7a74726d632e5684aa5c4317c16448cd8b08c01d6cd51"
    "372b627b0f6620d8884b6c23ab1c84a2af150195ac6203bb0fc23c290f2422b9"
    "6b728646a6d6cb060eb0042cbd7e3529edfef9b9c1bcc90ad85b2f33e9d00f3e"
    "9acc630ce0a3914a25e1a9b36bd2c6f2ba41d5510faefb7c0f30e49abe5036f9"
    "7a17628e7b94238c150cd548022bfbb6eb5b22be759e6a991a0df690fc577943"
    "016f2fcd74ab74f5659d43bb13ded56d9708a99e112e2a29a0fd3f8452dbfbb4"
    "6730b3080b2db7eeda41ed1c6a7f984f144575d442448c34864fd928af101e25"
    "22f71ac0bea05d1e7ce30fbe17e4c5d5f94dd07fa7"
)
MD5_RE = re.compile(r"^[0-9A-F]{32}$")
MAXIMUM_DIRECTORY_DEPTH = 128


@dataclasses.dataclass(frozen=True)
class CrossfireRezDirectoryEntry:
    path: str
    offset: int
    size: int
    timestamp: int
    resource_id: int
    md5: str


@dataclasses.dataclass(frozen=True)
class CrossfireRezDirectoryTable:
    offset: int
    size: int
    entries: int


def decode_crossfire_rez_directory(data: bytes, offset: int) -> bytes:
    key = CROSSFIRE_REZ_DIRECTORY_KEY
    return bytes(
        ((key[(offset + index) % len(key)] ^ (~value & 0xFF)) + 73) & 0xFF
        for index, value in enumerate(data)
    )


def encode_crossfire_rez_directory(data: bytes, offset: int) -> bytes:
    """Inverse transform used by synthetic fixtures and deterministic repacking."""
    key = CROSSFIRE_REZ_DIRECTORY_KEY
    return bytes(
        (~(key[(offset + index) % len(key)] ^ ((value - 73) & 0xFF))) & 0xFF
        for index, value in enumerate(data)
    )


def _safe_name(raw: bytes, label: str) -> str:
    try:
        value = raw.decode("ascii")
    except UnicodeDecodeError as error:
        raise RezError(f"non-ASCII CrossFire REZ {label}") from error
    if not value or any(ord(char) < 32 or char in "/\\" for char in value):
        raise RezError(f"unsafe CrossFire REZ {label}: {value!r}")
    return value


def parse_crossfire_rez_directory(
    source: Path, root_offset: int, root_size: int
) -> dict[str, object]:
    """Return a strict path/offset index for one encrypted directory tree."""
    file_size = source.stat().st_size
    if root_offset < 168 or root_size <= 0 or root_offset + root_size > file_size:
        raise RezError("invalid CrossFire REZ root directory range")
    raw = source.read_bytes()
    entries: list[CrossfireRezDirectoryEntry] = []
    tables: list[CrossfireRezDirectoryTable] = []
    visited: set[tuple[int, int]] = set()

    def parse_table(offset: int, size: int, parent: tuple[str, ...], depth: int) -> None:
        if depth > MAXIMUM_DIRECTORY_DEPTH:
            raise RezError("CrossFire REZ directory nesting exceeds safety limit")
        if offset < 168 or size <= 0 or offset + size > file_size:
            raise RezError(f"CrossFire REZ directory range outside archive: {offset}+{size}")
        key = (offset, size)
        if key in visited:
            raise RezError(f"duplicate or recursive CrossFire REZ directory range: {key}")
        visited.add(key)
        block = decode_crossfire_rez_directory(raw[offset : offset + size], offset)
        cursor = 0
        table_entries = 0
        children: list[tuple[int, int, tuple[str, ...]]] = []
        while cursor < len(block):
            if cursor + 4 > len(block):
                raise RezError(f"truncated CrossFire REZ entry type at {offset + cursor}")
            entry_type = struct.unpack_from("<i", block, cursor)[0]
            cursor += 4
            if entry_type == 1:
                if cursor + 16 > len(block):
                    raise RezError("truncated CrossFire REZ directory entry")
                table_offset, table_size, _timestamp, name_length = struct.unpack_from(
                    "<4i", block, cursor
                )
                cursor += 16
                if name_length <= 0 or cursor + name_length + 1 > len(block):
                    raise RezError("invalid CrossFire REZ directory-name length")
                name = _safe_name(block[cursor : cursor + name_length], "directory name")
                cursor += name_length + 1
                children.append((table_offset, table_size, (*parent, name)))
            elif entry_type == 0:
                if cursor + 28 > len(block):
                    raise RezError("truncated CrossFire REZ file entry")
                data_offset, data_size, timestamp, resource_id = struct.unpack_from(
                    "<4i", block, cursor
                )
                cursor += 16
                extension_raw = block[cursor : cursor + 4].rstrip(b"\0 ")
                cursor += 4
                _reserved, name_length = struct.unpack_from("<2i", block, cursor)
                cursor += 8
                if name_length <= 0 or cursor + name_length + 34 > len(block):
                    raise RezError("invalid CrossFire REZ file-name length")
                name = _safe_name(block[cursor : cursor + name_length], "file name")
                cursor += name_length + 2
                md5 = _safe_name(block[cursor : cursor + 32], "MD5")
                cursor += 32
                try:
                    extension = _safe_name(extension_raw, "extension")[::-1]
                except RezError:
                    raise RezError(f"invalid extension for CrossFire REZ file {name}")
                if not MD5_RE.fullmatch(md5):
                    raise RezError(f"invalid CrossFire REZ MD5 for {name}: {md5!r}")
                if data_offset < 168 or data_size <= 0 or data_offset + data_size > root_offset:
                    raise RezError(f"CrossFire REZ data extent outside data region: {name}")
                relative = PurePosixPath(*parent, f"{name}.{extension}")
                entries.append(
                    CrossfireRezDirectoryEntry(
                        relative.as_posix(), data_offset, data_size,
                        timestamp, resource_id, md5,
                    )
                )
            else:
                raise RezError(
                    f"invalid CrossFire REZ entry type {entry_type} at {offset + cursor - 4}"
                )
            table_entries += 1
        tables.append(CrossfireRezDirectoryTable(offset, size, table_entries))
        for child_offset, child_size, child_path in children:
            parse_table(child_offset, child_size, child_path, depth + 1)

    parse_table(root_offset, root_size, tuple(), 0)
    offsets: dict[int, CrossfireRezDirectoryEntry] = {}
    paths: set[str] = set()
    for entry in entries:
        if entry.offset in offsets:
            raise RezError(f"duplicate CrossFire REZ data offset: {entry.offset}")
        if entry.path.casefold() in paths:
            raise RezError(f"duplicate CrossFire REZ path: {entry.path}")
        offsets[entry.offset] = entry
        paths.add(entry.path.casefold())
    ordered = sorted(entries, key=lambda item: item.offset)
    for previous, current in zip(ordered, ordered[1:]):
        if previous.offset + previous.size > current.offset:
            raise RezError(f"overlapping CrossFire REZ resources at {current.offset}")
    return {
        "entries": entries,
        "by_offset": offsets,
        "tables": sorted(tables, key=lambda item: item.offset),
        "key_bytes": len(CROSSFIRE_REZ_DIRECTORY_KEY),
    }
