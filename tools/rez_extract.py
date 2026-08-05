#!/usr/bin/env python3
"""Safely list or extract LithTech/Jupiter REZ v1 archives.

This is a small, dependency-free port of the documented REZ directory layout.
It never executes any program shipped inside the recovery archives and rejects
paths that could escape the selected output directory.
"""

from __future__ import annotations

import argparse
import dataclasses
import json
import re
import struct
import sys
from pathlib import Path, PurePosixPath
from typing import BinaryIO, Iterable


HEADER_PREFIX_SIZE = 127
ENTRY_HEADER_SIZE = 16


class RezError(RuntimeError):
    pass


@dataclasses.dataclass(frozen=True)
class RezEntry:
    path: str
    offset: int
    size: int
    timestamp: int
    resource_id: int


def read_exact(stream: BinaryIO, size: int) -> bytes:
    data = stream.read(size)
    if len(data) != size:
        raise RezError(f"unexpected end of file: wanted {size}, got {len(data)}")
    return data


def read_u32(stream: BinaryIO) -> int:
    return struct.unpack("<I", read_exact(stream, 4))[0]


def decode_name(data: bytes) -> str:
    for encoding in ("utf-8", "gb18030", "cp1252"):
        try:
            return data.decode(encoding)
        except UnicodeDecodeError:
            continue
    return data.decode("latin1", errors="replace")


def read_cstring(block: bytes, cursor: int) -> tuple[str, int]:
    end = block.find(b"\0", cursor)
    if end < 0:
        raise RezError("unterminated string in directory block")
    return decode_name(block[cursor:end]), end + 1


def safe_parts(parts: Iterable[str]) -> tuple[str, ...]:
    result: list[str] = []
    for raw_part in parts:
        normalized = raw_part.replace("\\", "/")
        for part in PurePosixPath(normalized).parts:
            if part in ("", "."):
                continue
            if part == ".." or "/" in part or "\\" in part:
                raise RezError(f"unsafe archive path component: {raw_part!r}")
            result.append(part.replace(":", "_"))
    return tuple(result)


class RezArchive:
    def __init__(self, path: Path) -> None:
        self.path = path
        self.file_size = path.stat().st_size
        self.root_offset = 0
        self.root_size = 0
        self.version = 0
        self.entries: list[RezEntry] = []

    def load(self) -> None:
        with self.path.open("rb") as stream:
            prefix = read_exact(stream, HEADER_PREFIX_SIZE)
            if prefix[0:2] not in (b"\r\n", b"&#"):
                raise RezError("not a LithTech REZ archive")
            if prefix[126] != 0x1A:
                raise RezError(
                    "encoded REZ headers are not supported by the safe extractor"
                )

            version_offset = stream.tell()
            self.version = read_u32(stream)
            if self.version != 1:
                stream.seek(version_offset + 7)
                self.version = read_u32(stream)
                if self.version != 2:
                    raise RezError(f"unsupported REZ version: {self.version}")

            self.root_offset = read_u32(stream)
            self.root_size = read_u32(stream)
            # root timestamp, next write position, archive timestamp and
            # maximum-name metadata are useful to the original editor but not
            # needed for bounded extraction.
            read_exact(stream, 7 * 4 + 1)
            self._validate_range(self.root_offset, self.root_size, "root")
            self.entries.clear()
            self._read_directory_block(
                stream, self.root_offset, self.root_size, tuple(), set()
            )

    def _validate_range(self, offset: int, size: int, label: str) -> None:
        if offset < 0 or size < 0 or offset + size > self.file_size:
            raise RezError(
                f"{label} range outside archive: offset={offset} size={size}"
            )

    def _read_directory_block(
        self,
        stream: BinaryIO,
        offset: int,
        size: int,
        parent: tuple[str, ...],
        active_blocks: set[tuple[int, int]],
    ) -> None:
        key = (offset, size)
        if key in active_blocks:
            raise RezError(f"recursive directory block detected at {offset}")
        active_blocks.add(key)
        self._validate_range(offset, size, "directory")
        stream.seek(offset)
        block = read_exact(stream, size)
        cursor = 0
        children: list[tuple[int, int, tuple[str, ...]]] = []

        while cursor < len(block):
            if len(block) - cursor < ENTRY_HEADER_SIZE:
                raise RezError("truncated directory entry header")
            entry_type, data_offset, data_size, timestamp = struct.unpack_from(
                "<IIII", block, cursor
            )
            cursor += ENTRY_HEADER_SIZE

            if entry_type == 1:
                name, cursor = read_cstring(block, cursor)
                directory = safe_parts((*parent, name))
                if data_size:
                    children.append((data_offset, data_size, directory))
                continue

            if entry_type != 0:
                raise RezError(f"invalid directory entry type: {entry_type}")
            if len(block) - cursor < 12:
                raise RezError("truncated resource metadata")
            resource_id = struct.unpack_from("<I", block, cursor)[0]
            cursor += 4
            extension_raw = block[cursor : cursor + 4].split(b"\0", 1)[0]
            cursor += 4
            extension = decode_name(extension_raw)[::-1]
            key_count = struct.unpack_from("<I", block, cursor)[0]
            cursor += 4
            name, cursor = read_cstring(block, cursor)
            _description, cursor = read_cstring(block, cursor)
            key_bytes = key_count * 4
            if cursor + key_bytes > len(block):
                raise RezError("resource key table exceeds directory block")
            cursor += key_bytes
            self._validate_range(data_offset, data_size, name)

            filename = name if not extension else f"{name}.{extension}"
            entry_path = "/".join(safe_parts((*parent, filename)))
            self.entries.append(
                RezEntry(
                    path=entry_path,
                    offset=data_offset,
                    size=data_size,
                    timestamp=timestamp,
                    resource_id=resource_id,
                )
            )

        for child_offset, child_size, child_path in children:
            self._read_directory_block(
                stream,
                child_offset,
                child_size,
                child_path,
                active_blocks,
            )
        active_blocks.remove(key)

    def extract(
        self,
        output: Path,
        extension_filter: set[str],
        name_pattern: re.Pattern[str] | None,
    ) -> int:
        output.mkdir(parents=True, exist_ok=True)
        output_root = output.resolve()
        extracted = 0
        with self.path.open("rb") as stream:
            for entry in self.filtered(extension_filter, name_pattern):
                relative = Path(*safe_parts(entry.path.split("/")))
                destination = (output_root / relative).resolve()
                if output_root != destination and output_root not in destination.parents:
                    raise RezError(f"output path escaped root: {entry.path}")
                destination.parent.mkdir(parents=True, exist_ok=True)
                stream.seek(entry.offset)
                remaining = entry.size
                with destination.open("wb") as target:
                    while remaining:
                        chunk = read_exact(stream, min(remaining, 1024 * 1024))
                        target.write(chunk)
                        remaining -= len(chunk)
                extracted += 1
        return extracted

    def filtered(
        self,
        extension_filter: set[str],
        name_pattern: re.Pattern[str] | None,
    ) -> Iterable[RezEntry]:
        for entry in self.entries:
            extension = Path(entry.path).suffix.lower().lstrip(".")
            if extension_filter and extension not in extension_filter:
                continue
            if name_pattern is not None and not name_pattern.search(entry.path):
                continue
            yield entry


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="List or safely extract LithTech/Jupiter REZ v1 archives."
    )
    parser.add_argument("archives", nargs="+", type=Path)
    parser.add_argument(
        "--extract",
        type=Path,
        metavar="OUTPUT",
        help="extract matched resources below OUTPUT",
    )
    parser.add_argument(
        "--ext",
        default="",
        help="comma-separated extension allowlist, for example LTB,DTX,WAV",
    )
    parser.add_argument(
        "--name",
        help="case-insensitive regular expression matched against archive paths",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="emit one JSON inventory instead of a text table",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    extensions = {
        value.strip().lower()
        for value in args.ext.split(",")
        if value.strip()
    }
    pattern = re.compile(args.name, re.IGNORECASE) if args.name else None
    inventory: list[dict[str, object]] = []
    succeeded = 0
    failed = 0

    try:
        for archive_path in args.archives:
            try:
                archive = RezArchive(archive_path.resolve())
                archive.load()
                selected = list(archive.filtered(extensions, pattern))
                if args.extract is not None:
                    target = args.extract / archive_path.stem
                    count = archive.extract(target, extensions, pattern)
                    print(f"{archive_path}: extracted {count} files to {target}")
                elif args.json:
                    inventory.append(
                        {
                            "archive": str(archive_path),
                            "version": archive.version,
                            "entries": [
                                dataclasses.asdict(entry) for entry in selected
                            ],
                        }
                    )
                else:
                    print(
                        f"{archive_path}: version={archive.version} "
                        f"matched={len(selected)} total={len(archive.entries)}"
                    )
                    for entry in selected:
                        print(f"{entry.size:10d}  {entry.path}")
                succeeded += 1
            except (OSError, RezError) as error:
                failed += 1
                print(f"{archive_path}: {error}", file=sys.stderr)
        if args.json:
            json.dump(inventory, sys.stdout, ensure_ascii=False, indent=2)
            print()
        if failed:
            print(
                f"rez_extract: skipped {failed} unsupported/corrupt archives",
                file=sys.stderr,
            )
        return 0 if succeeded else 1
    except (OSError, RezError, re.error) as error:
        print(f"rez_extract: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
