#!/usr/bin/env python3
"""Recover independently framed resources from private CrossFire REZ files.

The private RF archives retain the public REZ v1 header but encrypt/obfuscate
their directory block.  Their data region is often a deterministic sequence of
LZMA-Alone streams, one stream per resource.  This tool decodes only that
provable framing, validates every stream's declared size and EOF, and never
guesses directory names or decryption keys.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import lzma
import os
import struct
import tempfile
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import BinaryIO, Iterable

from rez_extract import RezArchive, RezError


DATA_OFFSET = 168
LZMA_PROPERTIES = 0x5D
LZMA_DICTIONARY_SIZE = 16 * 1024 * 1024
CHUNK_SIZE = 1024 * 1024


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(CHUNK_SIZE), b""):
            digest.update(chunk)
    return digest.hexdigest()


def entropy(data: bytes) -> float:
    if not data:
        return 0.0
    counts = Counter(data)
    size = len(data)
    from math import log2

    return round(-sum((count / size) * log2(count / size) for count in counts.values()), 4)


def parse_rez_header(path: Path) -> dict[str, int]:
    size = path.stat().st_size
    if size < DATA_OFFSET:
        raise RezError("REZ is shorter than its v1 header")
    with path.open("rb") as stream:
        prefix = stream.read(127)
        if prefix[:2] not in (b"\r\n", b"&#") or prefix[126] != 0x1A:
            raise RezError("not a supported LithTech REZ header")
        version, root_offset, root_size = struct.unpack("<III", stream.read(12))
    if version != 1:
        raise RezError(f"private recovery only supports REZ v1, got {version}")
    if root_offset < DATA_OFFSET or root_offset + root_size > size:
        raise RezError(
            f"invalid REZ root range: offset={root_offset} size={root_size} file={size}"
        )
    return {
        "version": version,
        "root_offset": root_offset,
        "root_size": root_size,
        "data_offset": DATA_OFFSET,
        "data_size": root_offset - DATA_OFFSET,
    }


def classify_payload(prefix: bytes) -> str:
    if prefix.startswith(b"\x89PNG\r\n\x1a\n"):
        return "png"
    if prefix.startswith(b"OggS"):
        return "ogg"
    if prefix.startswith((b"FSB4", b"FSB5")):
        return "fsb"
    if prefix.startswith(b"RIFF") and prefix[8:12] == b"WAVE":
        return "wav"
    if prefix.startswith(b"ID3") or (
        len(prefix) >= 2 and prefix[0] == 0xFF and prefix[1] & 0xE0 == 0xE0
    ):
        return "mp3"
    if prefix.startswith(b"OTTO"):
        return "otf"
    if prefix.startswith(b"\x00\x01\x00\x00"):
        return "ttf"
    if prefix.startswith((b"\r\nRezMgr", b"&#RezMgr")):
        return "rez"
    if prefix.startswith(b"DDS "):
        return "dds"
    sample = prefix[:256]
    if sample:
        printable = sum(byte in b"\t\r\n" or 32 <= byte < 127 for byte in sample)
        if printable / len(sample) >= 0.9:
            return "txt"
    return "bin"


class LooseIndex:
    def __init__(self, paths: Iterable[Path], workspace: Path) -> None:
        self.workspace = workspace
        self.by_size: dict[int, list[Path]] = defaultdict(list)
        self.hashes: dict[Path, str] = {}
        for path in paths:
            self.by_size[path.stat().st_size].append(path)

    def matches(self, size: int, digest: str) -> list[str]:
        matches = []
        for path in self.by_size.get(size, []):
            actual = self.hashes.get(path)
            if actual is None:
                actual = sha256_file(path)
                self.hashes[path] = actual
            if actual == digest:
                matches.append(str(path.relative_to(self.workspace)))
        return sorted(matches)


def _decode_one_stream(
    stream: BinaryIO,
    region_end: int,
    temporary: BinaryIO,
) -> dict[str, object]:
    start = stream.tell()
    header = stream.read(13)
    stream.seek(start)
    if len(header) != 13:
        raise RezError(f"truncated LZMA-Alone header at {start}")
    properties = header[0]
    dictionary_size = struct.unpack_from("<I", header, 1)[0]
    declared_size = struct.unpack_from("<Q", header, 5)[0]
    if properties != LZMA_PROPERTIES or dictionary_size != LZMA_DICTIONARY_SIZE:
        raise RezError(
            f"unsupported LZMA-Alone framing at {start}: "
            f"properties=0x{properties:02x} dictionary={dictionary_size}"
        )
    decoder = lzma.LZMADecompressor(format=lzma.FORMAT_ALONE)
    digest = hashlib.sha256()
    decoded_size = 0
    prefix = bytearray()
    while not decoder.eof:
        remaining = region_end - stream.tell()
        if remaining <= 0:
            raise RezError(f"truncated LZMA stream at {start}")
        chunk = stream.read(min(CHUNK_SIZE, remaining))
        data = chunk
        while True:
            try:
                decoded = decoder.decompress(data, max_length=CHUNK_SIZE)
            except lzma.LZMAError as error:
                raise RezError(f"invalid LZMA stream at {start}: {error}") from error
            data = b""
            if decoded:
                temporary.write(decoded)
                digest.update(decoded)
                decoded_size += len(decoded)
                if len(prefix) < 256:
                    prefix.extend(decoded[: 256 - len(prefix)])
            if decoder.eof or decoder.needs_input:
                break
    if decoder.unused_data:
        stream.seek(-len(decoder.unused_data), os.SEEK_CUR)
    compressed_end = stream.tell()
    if compressed_end <= start:
        raise RezError(f"LZMA stream made no progress at {start}")
    if decoded_size != declared_size:
        raise RezError(
            f"LZMA declared-size mismatch at {start}: "
            f"declared={declared_size} decoded={decoded_size}"
        )
    return {
        "compressed_offset": start,
        "compressed_bytes": compressed_end - start,
        "decoded_bytes": decoded_size,
        "sha256": digest.hexdigest(),
        "header_properties": f"0x{properties:02x}",
        "dictionary_size": dictionary_size,
        "declared_size": declared_size,
        "prefix_hex": bytes(prefix[:32]).hex(),
        "classification": classify_payload(bytes(prefix)),
    }


def recover_lzma_region(
    source: Path,
    source_sha256: str,
    region_end: int,
    output: Path,
    loose_index: LooseIndex,
) -> tuple[
    list[dict[str, object]], list[dict[str, object]], dict[str, object] | None
]:
    base = output / "private-rez-decoded" / f"{source.stem}__{source_sha256[:12]}"
    base.mkdir(parents=True, exist_ok=True)
    records = []
    outputs = []
    with source.open("rb") as stream:
        stream.seek(DATA_OFFSET)
        index = 0
        while stream.tell() < region_end:
            candidate_offset = stream.tell()
            candidate_header = stream.read(5)
            stream.seek(candidate_offset)
            if candidate_header != b"\x5d\x00\x00\x00\x01":
                remaining = region_end - candidate_offset
                sample = stream.read(min(4096, remaining))
                trailing = {
                    "offset": candidate_offset,
                    "bytes": remaining,
                    "prefix_hex": sample[:32].hex(),
                    "sample_entropy": entropy(sample),
                    "status": "opaque_unframed_region_preserved",
                    "interpretation": "likely private directory blocks or an unframed resource; not decoded without a proven boundary",
                }
                return records, outputs, trailing
            with tempfile.NamedTemporaryFile(dir=base, prefix=".partial-", delete=False) as temporary:
                temporary_path = Path(temporary.name)
                try:
                    record = _decode_one_stream(stream, region_end, temporary)
                except Exception:
                    temporary_path.unlink(missing_ok=True)
                    raise
            matches = loose_index.matches(record["decoded_bytes"], record["sha256"])
            record.update({"stream_index": index, "loose_matches": matches})
            if matches:
                temporary_path.unlink()
                record["status"] = "verified_existing_loose_match"
            else:
                extension = record["classification"]
                destination = base / f"stream-{index:05d}.{extension}"
                if destination.exists():
                    if (
                        not destination.is_file()
                        or destination.stat().st_size != record["decoded_bytes"]
                        or sha256_file(destination) != record["sha256"]
                    ):
                        temporary_path.unlink(missing_ok=True)
                        raise RezError(f"existing decoded output differs: {destination}")
                    temporary_path.unlink()
                    status = "verified_existing"
                else:
                    os.replace(temporary_path, destination)
                    status = "decoded"
                output_item = {
                    "path": str(destination.relative_to(output)),
                    "bytes": record["decoded_bytes"],
                    "sha256": record["sha256"],
                    "representation": "private_rez_lzma_decoded_stream",
                    "status": status,
                }
                outputs.append(output_item)
                record.update({"status": status, "output": output_item})
            records.append(record)
            index += 1
    return records, outputs, None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--root-index", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    args = parser.parse_args()

    workspace = args.workspace.resolve()
    output = args.output.resolve()
    root_index = json.loads(args.root_index.read_text(encoding="utf-8"))
    samples = [
        item for item in root_index["samples"] if item["sample_type"] == "lithtech_rez"
    ]
    loose_paths = sorted(
        path.resolve()
        for path in (workspace / "CF2.0").rglob("*")
        if path.is_file() and path.suffix.casefold() != ".rez"
    )
    loose_index = LooseIndex(loose_paths, workspace)
    archives = []
    all_outputs = []
    for sample in samples:
        source = workspace / sample["path"]
        if sha256_file(source) != sample["sha256"]:
            raise RezError(f"source hash mismatch: {sample['path']}")
        try:
            standard = RezArchive(source)
            standard.load()
            archives.append(
                {
                    "source": sample["path"],
                    "source_sha256": sample["sha256"],
                    "status": "standard_rez_not_reprocessed",
                }
            )
            continue
        except RezError:
            pass
        header = parse_rez_header(source)
        with source.open("rb") as stream:
            stream.seek(DATA_OFFSET)
            prefix = stream.read(32)
        record: dict[str, object] = {
            "source": sample["path"],
            "source_sha256": sample["sha256"],
            "bytes": sample["bytes"],
            **header,
            "data_prefix_hex": prefix.hex(),
            "data_sample_entropy": entropy(prefix),
            "streams": [],
            "outputs": [],
        }
        if header["data_size"] == 0:
            record["status"] = "empty_data_region_private_directory"
        elif (
            len(prefix) >= 5
            and prefix[0] == LZMA_PROPERTIES
            and struct.unpack_from("<I", prefix, 1)[0] == LZMA_DICTIONARY_SIZE
        ):
            streams, outputs, trailing = recover_lzma_region(
                source, sample["sha256"], header["root_offset"], output, loose_index
            )
            record.update(
                {
                    "status": "concatenated_lzma_streams_recovered",
                    "streams": streams,
                    "outputs": outputs,
                    "trailing_unframed_region": trailing,
                }
            )
            all_outputs.extend(outputs)
        elif header["data_size"] <= 1:
            record["status"] = "empty_or_padding_data_region_private_directory"
        else:
            record["status"] = "raw_or_encrypted_data_region_unframed"
            record["classification"] = classify_payload(prefix)
        archives.append(record)

    classification_counts = Counter(
        stream["classification"]
        for archive in archives
        for stream in archive.get("streams", [])
    )
    status_counts = Counter(item["status"] for item in archives)
    manifest = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "tool": "tools/recover_private_rez.py",
        "tool_version": "1",
        "workspace": str(workspace),
        "root_inventory_sha256": root_index["inventory_sha256"],
        "safety": {
            "source_mode": "read_only",
            "unknown_executable_run": False,
            "directory_decryption_guessed": False,
            "stream_size_and_eof_required": True,
            "outputs_isolated_by_source_hash": True,
        },
        "archives": archives,
        "outputs": all_outputs,
        "summary": {
            "archives": len(archives),
            "archive_status_counts": dict(sorted(status_counts.items())),
            "lzma_streams": sum(len(item.get("streams", [])) for item in archives),
            "decoded_bytes": sum(
                stream["decoded_bytes"]
                for archive in archives
                for stream in archive.get("streams", [])
            ),
            "verified_loose_matches": sum(
                stream["status"] == "verified_existing_loose_match"
                for archive in archives
                for stream in archive.get("streams", [])
            ),
            "materialized_outputs": len(all_outputs),
            "materialized_bytes": sum(item["bytes"] for item in all_outputs),
            "classification_counts": dict(sorted(classification_counts.items())),
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
