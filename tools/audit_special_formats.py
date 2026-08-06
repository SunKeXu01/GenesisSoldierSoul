#!/usr/bin/env python3
"""Audit and convert specialized recovery formats with one provenance ledger."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
import re
import struct
import zipfile
import zlib
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath
from typing import Any, Iterable

from PIL import Image

from lithtech_ltc import LtcDecodeError, decode_ltc, lta_structure
from rez_extract import RezArchive, RezError, safe_parts


PAK_MAGIC_BYTES = struct.pack("<I", 0x5A6F12E1)
UTOC_MAGIC = b"-==--==--==--==-"
GUID_RE = re.compile(r"\bguid:\s*([0-9a-fA-F]{32})\b")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def write_verified(path: Path, data: bytes) -> str:
    digest = sha256_bytes(data)
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        if not path.is_file() or path.stat().st_size != len(data) or sha256_file(path) != digest:
            raise ValueError(f"existing special-format output differs: {path}")
        return "verified_existing"
    path.write_bytes(data)
    if sha256_file(path) != digest:
        raise IOError(f"special-format output verification failed: {path}")
    return "converted"


def entropy(data: bytes) -> float:
    if not data:
        return 0.0
    counts = Counter(data)
    length = len(data)
    return round(-sum((count / length) * math.log2(count / length) for count in counts.values()), 4)


def read_fstring(data: bytes, offset: int, *, maximum_bytes: int = 1024 * 1024) -> tuple[str, int]:
    if offset < 0 or offset + 4 > len(data):
        raise ValueError("truncated FString length")
    length = struct.unpack_from("<i", data, offset)[0]
    offset += 4
    if length == 0:
        return "", offset
    byte_count = abs(length) * (2 if length < 0 else 1)
    if byte_count > maximum_bytes or offset + byte_count > len(data):
        raise ValueError(f"invalid FString byte count: {byte_count}")
    raw = data[offset : offset + byte_count]
    offset += byte_count
    if length < 0:
        if not raw.endswith(b"\0\0"):
            raise ValueError("unterminated UTF-16 FString")
        return raw[:-2].decode("utf-16le", errors="strict"), offset
    if not raw.endswith(b"\0"):
        raise ValueError("unterminated UTF-8 FString")
    return raw[:-1].decode("utf-8", errors="strict"), offset


def safe_unreal_path(value: str) -> str:
    normalized = value.replace("\\", "/")
    pure = PurePosixPath(normalized)
    if (
        pure.is_absolute()
        or ".." in pure.parts
        or not pure.parts
        or (pure.parts and ":" in pure.parts[0])
    ):
        raise ValueError(f"unsafe Unreal directory entry: {value}")
    return "/".join(part for part in pure.parts if part not in {"", "."})


def extension_counts(entries: list[dict[str, Any]]) -> dict[str, int]:
    counts = Counter(Path(item["path"]).suffix.lower() or "[no_extension]" for item in entries)
    return dict(sorted(counts.items()))


def parse_pak_directory_index(data: bytes, expected_entries: int) -> list[dict[str, Any]]:
    if len(data) < 4:
        raise ValueError("truncated Pak full-directory index")
    offset = 0
    directory_count = struct.unpack_from("<i", data, offset)[0]
    offset += 4
    if directory_count < 0 or directory_count > max(expected_entries, 1_000_000):
        raise ValueError(f"invalid Pak directory count: {directory_count}")
    entries: list[dict[str, Any]] = []
    for _ in range(directory_count):
        directory, offset = read_fstring(data, offset)
        if offset + 4 > len(data):
            raise ValueError("truncated Pak directory file count")
        file_count = struct.unpack_from("<i", data, offset)[0]
        offset += 4
        if file_count < 0 or file_count > expected_entries:
            raise ValueError(f"invalid Pak file count: {file_count}")
        for _ in range(file_count):
            filename, offset = read_fstring(data, offset)
            if offset + 4 > len(data):
                raise ValueError("truncated Pak encoded-entry offset")
            encoded_entry_offset = struct.unpack_from("<I", data, offset)[0]
            offset += 4
            entries.append(
                {
                    "path": safe_unreal_path(directory + filename),
                    "encoded_entry_offset": encoded_entry_offset,
                }
            )
    if offset != len(data):
        raise ValueError(f"Pak directory index has {len(data) - offset} trailing bytes")
    if len(entries) != expected_entries:
        raise ValueError(
            f"Pak directory entry mismatch: expected {expected_entries}, got {len(entries)}"
        )
    return entries


def parse_iostore_directory_index(data: bytes) -> dict[str, Any]:
    mount_point, offset = read_fstring(data, 0)
    if offset + 4 > len(data):
        raise ValueError("truncated IoStore directory count")
    directory_count = struct.unpack_from("<i", data, offset)[0]
    offset += 4
    if directory_count <= 0 or directory_count > 1_000_000:
        raise ValueError(f"invalid IoStore directory count: {directory_count}")
    directory_bytes = directory_count * 16
    if offset + directory_bytes > len(data):
        raise ValueError("truncated IoStore directory table")
    directories = [
        struct.unpack_from("<IIII", data, offset + index * 16)
        for index in range(directory_count)
    ]
    offset += directory_bytes
    if offset + 4 > len(data):
        raise ValueError("truncated IoStore file count")
    file_count = struct.unpack_from("<i", data, offset)[0]
    offset += 4
    if file_count < 0 or file_count > 10_000_000:
        raise ValueError(f"invalid IoStore file count: {file_count}")
    file_bytes = file_count * 12
    if offset + file_bytes > len(data):
        raise ValueError("truncated IoStore file table")
    files = [
        struct.unpack_from("<III", data, offset + index * 12)
        for index in range(file_count)
    ]
    offset += file_bytes
    if offset + 4 > len(data):
        raise ValueError("truncated IoStore string count")
    string_count = struct.unpack_from("<i", data, offset)[0]
    offset += 4
    if string_count < 0 or string_count > 10_000_000:
        raise ValueError(f"invalid IoStore string count: {string_count}")
    strings = []
    for _ in range(string_count):
        value, offset = read_fstring(data, offset)
        strings.append(value)
    if offset != len(data):
        raise ValueError(f"IoStore directory index has {len(data) - offset} trailing bytes")

    none = 0xFFFFFFFF
    entries: list[dict[str, Any]] = []
    visited_directories: set[int] = set()

    def walk(directory_index: int, prefix: str) -> None:
        if directory_index >= len(directories) or directory_index in visited_directories:
            raise ValueError("invalid/cyclic IoStore directory graph")
        visited_directories.add(directory_index)
        name_index, first_child, _next_sibling, first_file = directories[directory_index]
        if name_index == none:
            directory_name = ""
        elif name_index < len(strings):
            directory_name = strings[name_index]
        else:
            raise ValueError("IoStore directory name index out of range")
        current_prefix = prefix + (directory_name + "/" if directory_name else "")
        visited_files: set[int] = set()
        file_index = first_file
        while file_index != none:
            if file_index >= len(files) or file_index in visited_files:
                raise ValueError("invalid/cyclic IoStore file chain")
            visited_files.add(file_index)
            file_name_index, next_file, toc_entry_index = files[file_index]
            if file_name_index >= len(strings):
                raise ValueError("IoStore file name index out of range")
            entries.append(
                {
                    "path": safe_unreal_path(current_prefix + strings[file_name_index]),
                    "toc_entry_index": toc_entry_index,
                }
            )
            file_index = next_file
        child_index = first_child
        sibling_guard: set[int] = set()
        while child_index != none:
            if child_index >= len(directories) or child_index in sibling_guard:
                raise ValueError("invalid/cyclic IoStore sibling chain")
            sibling_guard.add(child_index)
            next_child = directories[child_index][2]
            walk(child_index, current_prefix)
            child_index = next_child

    walk(0, "")
    if len(entries) != file_count or len(visited_directories) != directory_count:
        raise ValueError(
            "IoStore directory graph coverage mismatch: "
            f"directories {len(visited_directories)}/{directory_count}, "
            f"files {len(entries)}/{file_count}"
        )
    return {
        "mount_point": mount_point,
        "directory_count": directory_count,
        "file_count": file_count,
        "string_count": string_count,
        "entries": entries,
    }


def output_record(path: Path, data: bytes, output: Path, representation: str) -> dict[str, Any]:
    status = write_verified(path, data)
    return {
        "path": str(path.relative_to(output)),
        "bytes": len(data),
        "sha256": sha256_bytes(data),
        "representation": representation,
        "status": status,
    }


def audit_unitypackages(
    workspace: Path,
    samples: list[dict[str, Any]],
    safe_manifest: dict[str, Any],
) -> dict[str, Any]:
    extracted_by_source = {item["archive"]: item for item in safe_manifest["archives"]}
    packages = []
    for sample in samples:
        source_path = sample["path"]
        extraction = extracted_by_source.get(source_path)
        package: dict[str, Any] = {
            "source": source_path,
            "bytes": sample["bytes"],
            "sha256": sample["sha256"],
            "status": "missing_safe_extraction",
            "assets": [],
        }
        if not extraction or extraction["status"] not in {"extracted", "already_extracted"}:
            packages.append(package)
            continue
        destination = workspace / extraction["destination"]
        materialized = destination / "_materialized"
        errors: list[str] = []
        for pathname_file in sorted(destination.glob("*/pathname")):
            guid = pathname_file.parent.name.lower()
            pathname = pathname_file.read_text(encoding="utf-8", errors="replace").strip("\0\r\n")
            if not re.fullmatch(r"[0-9a-f]{32}", guid):
                errors.append(f"invalid GUID directory: {guid}")
                continue
            pure = PurePosixPath(pathname.replace("\\", "/"))
            if not pure.parts or pure.parts[0] != "Assets" or ".." in pure.parts:
                errors.append(f"unsafe original path: {pathname}")
                continue
            raw_asset = pathname_file.parent / "asset"
            raw_meta = pathname_file.parent / "asset.meta"
            target_asset = materialized.joinpath(*pure.parts)
            target_meta = Path(str(target_asset) + ".meta")
            item: dict[str, Any] = {
                "guid": guid,
                "original_path": pathname,
                "raw_asset": None,
                "raw_meta": None,
                "materialized_asset": None,
                "materialized_meta": None,
                "dependencies": [],
                "status": "directory",
            }
            if raw_asset.is_file():
                raw_hash = sha256_file(raw_asset)
                item["raw_asset"] = {"bytes": raw_asset.stat().st_size, "sha256": raw_hash}
                if not target_asset.is_file():
                    errors.append(f"missing materialized asset: {pathname}")
                    item["status"] = "missing_materialized_asset"
                else:
                    target_hash = sha256_file(target_asset)
                    item["materialized_asset"] = {
                        "path": str(target_asset.relative_to(workspace)),
                        "bytes": target_asset.stat().st_size,
                        "sha256": target_hash,
                    }
                    if target_hash != raw_hash:
                        errors.append(f"asset hash mismatch: {pathname}")
                        item["status"] = "asset_hash_mismatch"
                    else:
                        item["status"] = "materialized"
            elif not target_asset.is_dir():
                errors.append(f"missing materialized directory: {pathname}")
                item["status"] = "missing_materialized_directory"
            if raw_meta.is_file():
                raw_meta_hash = sha256_file(raw_meta)
                raw_meta_text = raw_meta.read_text(encoding="utf-8", errors="ignore")
                item["raw_meta"] = {"bytes": raw_meta.stat().st_size, "sha256": raw_meta_hash}
                if guid not in {value.lower() for value in GUID_RE.findall(raw_meta_text)}:
                    errors.append(f"meta GUID does not match directory: {pathname}")
                if target_meta.is_file():
                    target_meta_hash = sha256_file(target_meta)
                    item["materialized_meta"] = {
                        "path": str(target_meta.relative_to(workspace)),
                        "bytes": target_meta.stat().st_size,
                        "sha256": target_meta_hash,
                    }
                    if target_meta_hash != raw_meta_hash:
                        errors.append(f"meta hash mismatch: {pathname}")
                else:
                    errors.append(f"missing materialized meta: {pathname}")
            dependency_values: set[str] = set()
            for candidate in (raw_asset, raw_meta):
                if candidate.is_file() and candidate.stat().st_size <= 16 * 1024 * 1024:
                    dependency_values.update(value.lower() for value in GUID_RE.findall(candidate.read_text(encoding="utf-8", errors="ignore")))
            item["dependencies"] = sorted(dependency_values - {guid})
            package["assets"].append(item)
        package["errors"] = errors
        package["status"] = "verified" if not errors else "verification_failed"
        package["asset_count"] = sum(item["status"] == "materialized" for item in package["assets"])
        package["directory_count"] = sum(item["status"] == "directory" for item in package["assets"])
        package["dependency_edges"] = sum(len(item["dependencies"]) for item in package["assets"])
        packages.append(package)
    return {
        "packages": packages,
        "asset_bundles": {"samples": 0, "status": "not_present_in_inventory"},
        "summary": {
            "packages": len(packages),
            "verified": sum(item["status"] == "verified" for item in packages),
            "assets": sum(item.get("asset_count", 0) for item in packages),
            "dependency_edges": sum(item.get("dependency_edges", 0) for item in packages),
        },
    }


def decode_dtx(data: bytes) -> tuple[bytes, dict[str, Any]]:
    if len(data) < 164:
        raise ValueError("DTX file shorter than 164-byte header")
    resource_type, version = struct.unpack_from("<ii", data, 0)
    width, height, mipmaps, sections = struct.unpack_from("<HHHH", data, 8)
    flags, user_flags = struct.unpack_from("<II", data, 16)
    bpp_identifier = data[26]
    if width <= 0 or height <= 0:
        raise ValueError(f"invalid DTX dimensions: {width}x{height}")
    if bpp_identifier == 3:
        pixel_bytes = width * height * 4
        if 164 + pixel_bytes > len(data):
            raise ValueError(
                f"truncated BGRA8888 DTX payload: {width}x{height}, {len(data)} bytes"
            )
        pixels = data[164 : 164 + pixel_bytes]
        image = Image.frombytes("RGBA", (width, height), pixels, "raw", "BGRA")
        decoded_format = "BGRA8888"
    elif bpp_identifier in {4, 5, 6}:
        fourcc = {4: b"DXT1", 5: b"DXT3", 6: b"DXT5"}[bpp_identifier]
        block_bytes = 8 if bpp_identifier == 4 else 16
        base_bytes = ((width + 3) // 4) * ((height + 3) // 4) * block_bytes
        if 164 + base_bytes > len(data):
            raise ValueError(
                f"truncated {fourcc.decode()} DTX payload: "
                f"{width}x{height}, {len(data)} bytes"
            )
        header_values = [
            124,
            0x00081007,
            height,
            width,
            base_bytes,
            0,
            1,
            *([0] * 11),
            32,
            0x4,
            struct.unpack("<I", fourcc)[0],
            0,
            0,
            0,
            0,
            0,
            0x1000,
            0,
            0,
            0,
            0,
        ]
        dds = b"DDS " + struct.pack("<31I", *header_values) + data[164 : 164 + base_bytes]
        try:
            with Image.open(io.BytesIO(dds)) as source:
                image = source.convert("RGBA")
        except Exception as error:
            raise ValueError(f"Pillow failed to decode {fourcc.decode()} DTX") from error
        decoded_format = fourcc.decode()
    else:
        raise ValueError(f"unsupported DTX bpp identifier: {bpp_identifier}")
    stream = io.BytesIO()
    image.save(stream, format="PNG")
    return stream.getvalue(), {
        "resource_type": resource_type,
        "version": version,
        "width": width,
        "height": height,
        "mipmaps": mipmaps,
        "sections": sections,
        "flags": flags,
        "user_flags": user_flags,
        "bpp_identifier": bpp_identifier,
        "decoded_pixel_format": decoded_format,
    }


def rez_private_evidence(path: Path, error: Exception) -> dict[str, Any]:
    evidence: dict[str, Any] = {"error": f"{type(error).__name__}: {error}"}
    try:
        archive = RezArchive(path)
        with path.open("rb") as stream:
            prefix = stream.read(127)
            if len(prefix) < 127:
                return evidence
            archive.version = struct.unpack("<I", stream.read(4))[0]
            archive.root_offset = struct.unpack("<I", stream.read(4))[0]
            archive.root_size = struct.unpack("<I", stream.read(4))[0]
            if 0 <= archive.root_offset < path.stat().st_size:
                stream.seek(archive.root_offset)
                block = stream.read(min(4096, archive.root_size, path.stat().st_size - archive.root_offset))
                evidence.update(
                    {
                        "header_version": archive.version,
                        "root_offset": archive.root_offset,
                        "root_size": archive.root_size,
                        "directory_prefix_hex": block[:32].hex(),
                        "directory_sample_entropy": entropy(block),
                        "directory_first_u32": struct.unpack_from("<I", block, 0)[0] if len(block) >= 4 else None,
                    }
                )
    except Exception as nested:
        evidence["evidence_error"] = f"{type(nested).__name__}: {nested}"
    return evidence


def audit_rez(
    workspace: Path,
    samples: list[dict[str, Any]],
    output: Path,
) -> dict[str, Any]:
    archives = []
    for sample in samples:
        path = workspace / sample["path"]
        record: dict[str, Any] = {
            "source": sample["path"],
            "bytes": sample["bytes"],
            "sha256": sample["sha256"],
            "status": "unknown",
            "entries": [],
            "outputs": [],
        }
        if sha256_file(path) != sample["sha256"]:
            record.update({"status": "source_hash_mismatch", "error": "root inventory digest mismatch"})
            archives.append(record)
            continue
        try:
            archive = RezArchive(path)
            archive.load()
            record.update({"status": "parsed_standard", "version": archive.version})
            archive_output = output / "rez" / f"{path.stem}__{sample['sha256'][:12]}"
            with path.open("rb") as stream:
                for entry in archive.entries:
                    stream.seek(entry.offset)
                    data = stream.read(entry.size)
                    if len(data) != entry.size:
                        raise RezError(f"short resource read: {entry.path}")
                    destination = archive_output.joinpath(*safe_parts(entry.path.split("/")))
                    raw_output = output_record(destination, data, output, "rez_entry_original")
                    entry_record: dict[str, Any] = {
                        "path": entry.path,
                        "offset": entry.offset,
                        "bytes": entry.size,
                        "resource_id": entry.resource_id,
                        "sha256": sha256_bytes(data),
                        "outputs": [raw_output],
                        "status": "extracted",
                    }
                    if destination.suffix.lower() == ".dtx":
                        try:
                            png, details = decode_dtx(data)
                            png_path = destination.with_suffix(".png")
                            entry_record["outputs"].append(output_record(png_path, png, output, "decoded_dtx_png"))
                            entry_record["dtx"] = details
                            entry_record["status"] = "converted"
                        except Exception as error:
                            entry_record["status"] = "preserved_conversion_failed"
                            entry_record["error"] = f"{type(error).__name__}: {error}"
                    elif destination.suffix.lower() == ".ltb":
                        metadata = {
                            "classification": "LithTech render-style LTB, not a geometry model",
                            "path_evidence": entry.path,
                            "header_hex": data[:64].hex(),
                            "header_u16": list(struct.unpack_from("<" + "H" * min(8, len(data) // 2), data, 0)),
                        }
                        metadata_bytes = (json.dumps(metadata, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
                        entry_record["outputs"].append(
                            output_record(Path(str(destination) + ".json"), metadata_bytes, output, "ltb_renderstyle_metadata")
                        )
                        entry_record["ltb"] = metadata
                        entry_record["status"] = "classified_and_preserved"
                    record["entries"].append(entry_record)
                    record["outputs"].extend(entry_record["outputs"])
        except (OSError, RezError, struct.error) as error:
            record["status"] = "private_or_unsupported_directory"
            record["private_variant_evidence"] = rez_private_evidence(path, error)
        archives.append(record)

    loose_ltc = sorted((workspace / "CF2.0").rglob("*.LTC")) + sorted((workspace / "CF2.0").rglob("*.ltc"))
    loose_ltc = sorted(set(path.resolve() for path in loose_ltc))
    ltc_records = []
    for path in loose_ltc:
        source_data = path.read_bytes()
        source_sha256 = sha256_bytes(source_data)
        record: dict[str, Any] = {
            "path": str(path.relative_to(workspace)),
            "bytes": len(source_data),
            "sha256": source_sha256,
            "sample_entropy": entropy(source_data[:4096]),
            "header_hex": source_data[:32].hex(),
            "decoder": "CrossFire repeating XOR mask + LithTech LTC/LZSS v0",
        }
        try:
            decoded = decode_ltc(source_data)
            destination = output / "ltc-decoded" / f"{path.stem}__{source_sha256[:12]}.lta"
            converted = output_record(destination, decoded.data, output, "decoded_lithtech_lta")
            plaintext_peer = path.with_suffix(".LTA")
            peer_match = None
            if plaintext_peer.is_file() and plaintext_peer.resolve() != path.resolve():
                peer_match = decoded.data == plaintext_peer.read_bytes()
            record.update(
                {
                    "status": "decoded_and_verified",
                    "termination": decoded.termination,
                    "bits_consumed": decoded.bits_consumed,
                    "input_bits": decoded.input_bits,
                    "decoded_bytes": len(decoded.data),
                    "decoded_sha256": sha256_bytes(decoded.data),
                    "structure": lta_structure(decoded.data),
                    "plaintext_peer": (
                        {
                            "path": str(plaintext_peer.relative_to(workspace)),
                            "exact_match": peer_match,
                        }
                        if peer_match is not None
                        else None
                    ),
                    "outputs": [converted],
                }
            )
        except (LtcDecodeError, OSError) as error:
            record.update(
                {
                    "status": "preserved_decode_failed",
                    "failure_reason": f"{type(error).__name__}: {error}",
                    "outputs": [],
                }
            )
        ltc_records.append(record)
    return {
        "archives": archives,
        "loose_ltc": ltc_records,
        "summary": {
            "archives": len(archives),
            "parsed_standard": sum(item["status"] == "parsed_standard" for item in archives),
            "private_or_unsupported": sum(item["status"] == "private_or_unsupported_directory" for item in archives),
            "entries": sum(len(item["entries"]) for item in archives),
            "dtx_png": sum(
                output_item["representation"] == "decoded_dtx_png"
                for item in archives
                for output_item in item["outputs"]
            ),
            "ltb_classified": sum(
                output_item["representation"] == "ltb_renderstyle_metadata"
                for item in archives
                for output_item in item["outputs"]
            ),
            "ltc_sources": len(ltc_records),
            "ltc_decoded": sum(item["status"] == "decoded_and_verified" for item in ltc_records),
            "ltc_decode_failed": sum(item["status"] == "preserved_decode_failed" for item in ltc_records),
            "ltc_end_token": sum(item.get("termination") == "end_token" for item in ltc_records),
            "ltc_physical_eof": sum(item.get("termination") == "physical_eof" for item in ltc_records),
            "ltc_decoded_bytes": sum(item.get("decoded_bytes", 0) for item in ltc_records),
            "ltc_plaintext_peer_exact_matches": sum(
                item.get("plaintext_peer", {}).get("exact_match", False)
                for item in ltc_records
                if item.get("plaintext_peer")
            ),
        },
    }


def parse_pak(path: Path) -> dict[str, Any]:
    size = path.stat().st_size
    with path.open("rb") as stream:
        stream.seek(max(0, size - 512))
        tail = stream.read()
    relative_footer = tail.rfind(PAK_MAGIC_BYTES)
    if relative_footer < 0:
        raise ValueError("Pak magic not found in final 512 bytes")
    footer_offset = size - len(tail) + relative_footer
    footer = tail[relative_footer:]
    if len(footer) < 44:
        raise ValueError("truncated Pak footer")
    magic, version, index_offset, index_size = struct.unpack_from("<IIQQ", footer, 0)
    expected_sha1 = footer[24:44]
    if index_offset + index_size > footer_offset:
        raise ValueError("Pak index overlaps footer or exceeds file")
    with path.open("rb") as stream:
        stream.seek(index_offset)
        index = stream.read(index_size)
    actual_sha1 = hashlib.sha1(index).digest()
    mount_point = None
    entry_count = None
    if len(index) >= 4:
        string_length = struct.unpack_from("<i", index, 0)[0]
        if 0 < string_length <= min(len(index) - 4, 4096):
            mount_point = index[4 : 4 + string_length].rstrip(b"\0").decode("utf-8", errors="replace")
            if 4 + string_length + 4 <= len(index):
                entry_count = struct.unpack_from("<I", index, 4 + string_length)[0]
    compression_methods = []
    if len(footer) >= 204:
        for offset in range(44, 204, 32):
            value = footer[offset : offset + 32].split(b"\0", 1)[0].decode("ascii", errors="replace")
            if value:
                compression_methods.append(value)
    result: dict[str, Any] = {
        "bytes": size,
        "sha256": sha256_file(path),
        "footer_offset": footer_offset,
        "footer_bytes": size - footer_offset,
        "magic": f"0x{magic:08x}",
        "version": version,
        "index_offset": index_offset,
        "index_size": index_size,
        "index_sha1_expected": expected_sha1.hex(),
        "index_sha1_actual": actual_sha1.hex(),
        "index_sha1_verified": actual_sha1 == expected_sha1,
        "mount_point": mount_point,
        "entry_count": entry_count,
        "compression_methods": compression_methods,
    }
    # Version 10+ stores compact encoded entries in the primary index and
    # points at separately hashed path-hash and full-directory indices.
    # Older/minimal fixtures legitimately stop after mount + entry count.
    primary_position = 4 + (abs(string_length) * (2 if string_length < 0 else 1)) + 4
    if version >= 10 and primary_position + 92 <= len(index):
        path_hash_seed = struct.unpack_from("<Q", index, primary_position)[0]
        primary_position += 8
        path_hash_present = struct.unpack_from("<I", index, primary_position)[0]
        primary_position += 4
        path_hash_offset, path_hash_size = struct.unpack_from("<QQ", index, primary_position)
        primary_position += 16
        path_hash_expected = index[primary_position : primary_position + 20]
        primary_position += 20
        directory_present = struct.unpack_from("<I", index, primary_position)[0]
        primary_position += 4
        directory_offset, directory_size = struct.unpack_from("<QQ", index, primary_position)
        primary_position += 16
        directory_expected = index[primary_position : primary_position + 20]
        primary_position += 20
        encoded_entries_size = struct.unpack_from("<I", index, primary_position)[0]
        primary_position += 4
        encoded_entries_end = primary_position + encoded_entries_size
        if encoded_entries_end not in {len(index), len(index) - 4}:
            raise ValueError("Pak encoded-entry table size does not match primary index")
        frozen_index = False
        if encoded_entries_end + 4 == len(index):
            frozen_value = struct.unpack_from("<I", index, encoded_entries_end)[0]
            if frozen_value not in {0, 1}:
                raise ValueError(f"invalid Pak frozen-index flag: {frozen_value}")
            frozen_index = bool(frozen_value)

        def read_secondary(offset: int, length: int) -> bytes:
            if offset < 0 or length < 0 or offset + length > footer_offset:
                raise ValueError("Pak secondary index exceeds archive bounds")
            with path.open("rb") as secondary_stream:
                secondary_stream.seek(offset)
                block = secondary_stream.read(length)
            if len(block) != length:
                raise ValueError("short Pak secondary-index read")
            return block

        path_hash_data = read_secondary(path_hash_offset, path_hash_size)
        directory_data = read_secondary(directory_offset, directory_size)
        path_hash_actual = hashlib.sha1(path_hash_data).digest()
        directory_actual = hashlib.sha1(directory_data).digest()
        if path_hash_actual != path_hash_expected:
            raise ValueError("Pak path-hash secondary index SHA-1 mismatch")
        if directory_actual != directory_expected:
            raise ValueError("Pak full-directory secondary index SHA-1 mismatch")
        directory_entries = parse_pak_directory_index(
            directory_data, entry_count or 0
        )
        result.update(
            {
                "path_hash_seed": path_hash_seed,
                "path_hash_index_present": bool(path_hash_present),
                "path_hash_index_offset": path_hash_offset,
                "path_hash_index_size": path_hash_size,
                "path_hash_index_sha1_expected": path_hash_expected.hex(),
                "path_hash_index_sha1_actual": path_hash_actual.hex(),
                "path_hash_index_sha1_verified": True,
                "full_directory_index_present": bool(directory_present),
                "full_directory_index_offset": directory_offset,
                "full_directory_index_size": directory_size,
                "full_directory_index_sha1_expected": directory_expected.hex(),
                "full_directory_index_sha1_actual": directory_actual.hex(),
                "full_directory_index_sha1_verified": True,
                "encoded_entries_size": encoded_entries_size,
                "frozen_index": frozen_index,
                "directory_entry_count": len(directory_entries),
                "extension_counts": extension_counts(directory_entries),
                "_directory_entries": directory_entries,
                "_encoded_entries_data": index[
                    primary_position:encoded_entries_end
                ],
            }
        )
    return result


def parse_utoc(path: Path) -> dict[str, Any]:
    with path.open("rb") as stream:
        header = stream.read(144)
    if len(header) < 32 or header[:16] != UTOC_MAGIC:
        raise ValueError("invalid/truncated UTOC header")
    compressed_block_entry_size = struct.unpack_from("<I", header, 32)[0]
    compression_method_count = struct.unpack_from("<I", header, 36)[0]
    compression_method_name_length = struct.unpack_from("<I", header, 40)[0]
    compression_block_size = struct.unpack_from("<I", header, 44)[0]
    directory_index_size = struct.unpack_from("<I", header, 48)[0]
    partition_count = struct.unpack_from("<I", header, 52)[0]
    container_id = struct.unpack_from("<Q", header, 56)[0]
    encryption_key_guid = header[64:80].hex()
    container_flags = header[80]
    perfect_hash_seed_count = struct.unpack_from("<I", header, 84)[0]
    partition_size = struct.unpack_from("<Q", header, 88)[0]
    chunks_without_perfect_hash_count = struct.unpack_from("<I", header, 96)[0]
    toc_entry_count = struct.unpack_from("<I", header, 24)[0]
    compressed_block_count = struct.unpack_from("<I", header, 28)[0]
    header_size = struct.unpack_from("<I", header, 20)[0]
    if header_size < 100 or header_size > path.stat().st_size:
        raise ValueError(f"invalid UTOC header size: {header_size}")
    if compressed_block_entry_size < 12:
        raise ValueError(
            f"unsupported UTOC compressed-block entry size: {compressed_block_entry_size}"
        )
    directory_offset = (
        header_size
        + toc_entry_count * 12
        + toc_entry_count * 10
        + perfect_hash_seed_count * 4
        + chunks_without_perfect_hash_count * 4
        + compressed_block_count * compressed_block_entry_size
        + compression_method_count * compression_method_name_length
    )
    if directory_offset + directory_index_size > path.stat().st_size:
        raise ValueError("UTOC directory index exceeds file bounds")
    with path.open("rb") as stream:
        stream.seek(
            directory_offset
            - compression_method_count * compression_method_name_length
        )
        method_table = stream.read(
            compression_method_count * compression_method_name_length
        )
        stream.seek(directory_offset)
        directory_data = stream.read(directory_index_size)
    compression_methods = []
    for index in range(compression_method_count):
        start = index * compression_method_name_length
        value = method_table[
            start : start + compression_method_name_length
        ].split(b"\0", 1)[0].decode("ascii", errors="replace")
        if value:
            compression_methods.append(value)
    result: dict[str, Any] = {
        "bytes": path.stat().st_size,
        "sha256": sha256_file(path),
        "magic": header[:16].decode("ascii"),
        "version": header[16],
        "header_size": header_size,
        "toc_entry_count": toc_entry_count,
        "compressed_block_count": compressed_block_count,
        "compressed_block_entry_size": compressed_block_entry_size,
        "compression_method_count": compression_method_count,
        "compression_method_name_length": compression_method_name_length,
        "compression_methods": compression_methods,
        "compression_block_size": compression_block_size,
        "directory_index_offset": directory_offset,
        "directory_index_size": directory_index_size,
        "partition_count": partition_count,
        "partition_size": partition_size,
        "container_id": f"0x{container_id:016x}",
        "encryption_key_guid": encryption_key_guid,
        "container_flags": container_flags,
        "perfect_hash_seed_count": perfect_hash_seed_count,
        "chunks_without_perfect_hash_count": chunks_without_perfect_hash_count,
    }
    if directory_index_size:
        directory = parse_iostore_directory_index(directory_data)
        entries = directory.pop("entries")
        result.update(directory)
        result["directory_index_sha256"] = sha256_bytes(directory_data)
        result["extension_counts"] = extension_counts(entries)
        result["_directory_entries"] = entries
    else:
        result.update(
            {
                "mount_point": None,
                "directory_count": 0,
                "file_count": 0,
                "string_count": 0,
                "extension_counts": {},
                "_directory_entries": [],
            }
        )
    return result


def safe_zip_name(name: str) -> tuple[str, ...]:
    normalized = name.replace("\\", "/")
    pure = PurePosixPath(normalized)
    if pure.is_absolute() or ".." in pure.parts or (pure.parts and ":" in pure.parts[0]):
        raise ValueError(f"unsafe ZIP member: {name}")
    return tuple(part for part in pure.parts if part not in {"", "."})


def write_unreal_directory_manifest(
    source_label: str,
    source_sha256: str,
    container_kind: str,
    entries: list[dict[str, Any]],
    output: Path,
) -> dict[str, Any]:
    source_stem = Path(source_label).stem
    destination = (
        output
        / "unreal-index"
        / f"{source_stem}__{source_sha256[:12]}"
        / f"{container_kind}-directory-index.json"
    )
    manifest = {
        "source": source_label,
        "source_sha256": source_sha256,
        "container_kind": container_kind,
        "entry_count": len(entries),
        "extension_counts": extension_counts(entries),
        "entries": sorted(entries, key=lambda item: item["path"]),
    }
    data = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    return output_record(
        destination, data, output, f"{container_kind}_directory_index_manifest"
    )


def extract_supported_pak_entries(
    path: Path,
    source_label: str,
    source_sha256: str,
    index_offset: int,
    directory_entries: list[dict[str, Any]],
    encoded_entries: bytes,
    output: Path,
) -> dict[str, Any]:
    """Extract only fully understood uncompressed and Zlib v11 encodings.

    0xe0000000 is the compact 32-bit offset/size form with no compression.
    Compression-method index 2 is the Pak footer's Zlib method and carries an
    explicit block size, offset, uncompressed size, and stored size. Every
    payload is checked against the serialized FPakEntry SHA-1 before a
    source-hash-isolated output is written. Oodle and unknown combinations
    remain untouched and are counted explicitly.
    """

    supported_flag = 0xE0000000
    unsupported_flags: Counter[int] = Counter()
    method_counts: Counter[str] = Counter()
    records = []
    outputs = []
    engine_associations: set[str] = set()
    source_stem = Path(source_label).stem
    base = output / "unreal-extracted" / f"{source_stem}__{source_sha256[:12]}"
    with path.open("rb") as stream:
        for entry in directory_entries:
            encoded_offset = entry["encoded_entry_offset"]
            if encoded_offset + 12 > len(encoded_entries):
                raise ValueError(
                    f"Pak encoded entry exceeds table: {entry['path']}"
                )
            flags = struct.unpack_from("<I", encoded_entries, encoded_offset)[0]
            compression_method_index = (flags >> 23) & 0x3F
            if flags == supported_flag:
                data_offset, payload_size = struct.unpack_from(
                    "<II", encoded_entries, encoded_offset + 4
                )
                if data_offset + 53 + payload_size > index_offset:
                    raise ValueError(
                        f"Pak payload overlaps index or exceeds data: {entry['path']}"
                    )
                stream.seek(data_offset)
                header = stream.read(53)
                if len(header) != 53:
                    raise ValueError(
                        f"truncated serialized FPakEntry: {entry['path']}"
                    )
                serialized_offset, stored_size, uncompressed_size, method = (
                    struct.unpack_from("<QQQi", header, 0)
                )
                expected_sha1 = header[28:48]
                encrypted = header[48]
                compression_block_size = struct.unpack_from("<I", header, 49)[0]
                if (
                    stored_size != payload_size
                    or uncompressed_size != payload_size
                    or method != 0
                    or encrypted != 0
                    or compression_block_size != 0
                ):
                    raise ValueError(
                        "Pak compact uncompressed entry disagrees with serialized "
                        f"header: {entry['path']}"
                    )
                payload = stream.read(payload_size)
                stored_payload = payload
                representation = "pak_uncompressed_entry_original"
                method_label = "uncompressed"
            elif compression_method_index == 2:
                if encoded_offset + 20 > len(encoded_entries):
                    raise ValueError(
                        f"truncated Pak compact Zlib entry: {entry['path']}"
                    )
                (
                    _encoded_flags,
                    encoded_block_size,
                    data_offset,
                    uncompressed_size,
                    stored_size,
                ) = struct.unpack_from("<IIIII", encoded_entries, encoded_offset)
                stream.seek(data_offset)
                base_header = stream.read(52)
                if len(base_header) != 52:
                    raise ValueError(
                        f"truncated serialized Zlib FPakEntry: {entry['path']}"
                    )
                (
                    serialized_offset,
                    serialized_stored_size,
                    serialized_uncompressed_size,
                    method,
                ) = struct.unpack_from("<QQQi", base_header, 0)
                expected_sha1 = base_header[28:48]
                block_count = struct.unpack_from("<I", base_header, 48)[0]
                if block_count <= 0 or block_count > 1_000_000:
                    raise ValueError(
                        f"invalid Pak Zlib block count: {entry['path']}: {block_count}"
                    )
                stream.seek(data_offset + 52)
                block_table = stream.read(block_count * 16 + 5)
                if len(block_table) != block_count * 16 + 5:
                    raise ValueError(
                        f"truncated Pak Zlib block table: {entry['path']}"
                    )
                blocks = [
                    struct.unpack_from("<QQ", block_table, index * 16)
                    for index in range(block_count)
                ]
                encrypted = block_table[block_count * 16]
                compression_block_size = struct.unpack_from(
                    "<I", block_table, block_count * 16 + 1
                )[0]
                if (
                    serialized_stored_size != stored_size
                    or serialized_uncompressed_size != uncompressed_size
                    or method != 2
                    or encrypted != 0
                    or compression_block_size != encoded_block_size
                ):
                    raise ValueError(
                        "Pak compact Zlib entry disagrees with serialized header: "
                        f"{entry['path']}"
                    )
                header_size = 57 + block_count * 16
                if blocks[0][0] != header_size:
                    raise ValueError(
                        f"Pak Zlib first block does not follow header: {entry['path']}"
                    )
                if any(
                    end <= start
                    or (index > 0 and start != blocks[index - 1][1])
                    for index, (start, end) in enumerate(blocks)
                ):
                    raise ValueError(
                        f"invalid Pak Zlib block ranges: {entry['path']}"
                    )
                if blocks[-1][1] - blocks[0][0] != stored_size:
                    raise ValueError(
                        f"Pak Zlib stored-size mismatch: {entry['path']}"
                    )
                if data_offset + blocks[-1][1] > index_offset:
                    raise ValueError(
                        f"Pak Zlib payload overlaps index: {entry['path']}"
                    )
                decoded_blocks = []
                stored_blocks = []
                for start, end in blocks:
                    stream.seek(data_offset + start)
                    compressed = stream.read(end - start)
                    if len(compressed) != end - start:
                        raise ValueError(
                            f"short Pak Zlib block read: {entry['path']}"
                        )
                    try:
                        decoded = zlib.decompress(compressed)
                    except zlib.error as error:
                        raise ValueError(
                            f"Pak Zlib decompression failed: {entry['path']}: {error}"
                        ) from error
                    if len(decoded) > compression_block_size:
                        raise ValueError(
                            f"Pak Zlib block exceeds declared block size: {entry['path']}"
                        )
                    decoded_blocks.append(decoded)
                    stored_blocks.append(compressed)
                payload = b"".join(decoded_blocks)
                stored_payload = b"".join(stored_blocks)
                payload_size = len(payload)
                if payload_size != uncompressed_size:
                    raise ValueError(
                        f"Pak Zlib uncompressed-size mismatch: {entry['path']}"
                    )
                representation = "pak_zlib_decoded_entry"
                method_label = "zlib"
            else:
                unsupported_flags[flags] += 1
                continue
            if len(payload) != payload_size:
                raise ValueError(f"short Pak payload read: {entry['path']}")
            stored_sha1 = hashlib.sha1(stored_payload).digest()
            if stored_sha1 != expected_sha1:
                raise ValueError(
                    f"Pak stored-payload SHA-1 mismatch: {entry['path']}"
                )
            actual_sha1 = hashlib.sha1(payload).digest()
            relative = safe_unreal_path(entry["path"])
            destination = base.joinpath(*PurePosixPath(relative).parts)
            output_item = output_record(
                destination, payload, output, representation
            )
            outputs.append(output_item)
            method_counts[method_label] += 1
            record = {
                "path": relative,
                "encoded_entry_offset": encoded_offset,
                "data_offset": data_offset,
                "serialized_offset": serialized_offset,
                "bytes": payload_size,
                "sha1": actual_sha1.hex(),
                "stored_sha1": stored_sha1.hex(),
                "sha256": output_item["sha256"],
                "method": method_label,
                "output_path": output_item["path"],
            }
            if relative.lower().endswith(".uproject"):
                try:
                    project = json.loads(payload.decode("utf-8-sig"))
                    association = project.get("EngineAssociation")
                    if isinstance(association, str) and association:
                        record["engine_association"] = association
                        engine_associations.add(association)
                except (UnicodeDecodeError, json.JSONDecodeError) as error:
                    raise ValueError(
                        f"invalid extracted .uproject JSON: {relative}: {error}"
                    ) from error
            records.append(record)

    stable_manifest = {
        "source": source_label,
        "source_sha256": source_sha256,
        "supported_compact_flag": f"0x{supported_flag:08x}",
        "supported_compression_method_indices": {"0": "uncompressed", "2": "Zlib"},
        "verification": "serialized FPakEntry bounds + block ranges + compression/encryption fields + stored-payload SHA-1; decoded payload gets a separate SHA-1/SHA-256",
        "extracted_count": len(records),
        "extracted_bytes": sum(item["bytes"] for item in records),
        "method_counts": dict(sorted(method_counts.items())),
        "engine_associations": sorted(engine_associations),
        "unsupported_flag_counts": {
            f"0x{flag:08x}": count
            for flag, count in sorted(unsupported_flags.items())
        },
        "entries": sorted(records, key=lambda item: item["path"]),
    }
    manifest_destination = base / "_pak-supported-extraction-v2.json"
    manifest_data = (
        json.dumps(stable_manifest, ensure_ascii=False, indent=2) + "\n"
    ).encode("utf-8")
    manifest_output = output_record(
        manifest_destination,
        manifest_data,
        output,
        "pak_supported_extraction_manifest",
    )
    outputs.append(manifest_output)
    return {
        "supported_flag": f"0x{supported_flag:08x}",
        "extracted_count": len(records),
        "extracted_bytes": sum(item["bytes"] for item in records),
        "method_counts": dict(sorted(method_counts.items())),
        "engine_associations": sorted(engine_associations),
        "unsupported_flag_counts": stable_manifest["unsupported_flag_counts"],
        "manifest": manifest_output,
        "entries": stable_manifest["entries"],
        "outputs": outputs,
    }


def write_unreal_package_closure_manifest(
    source_label: str,
    source_sha256: str,
    directory_entries: list[dict[str, Any]],
    extracted_entries: list[dict[str, Any]],
    extraction_manifest: dict[str, Any],
    output: Path,
) -> dict[str, Any]:
    """Classify extracted Unreal packages without pretending to deserialize them.

    A package is a .uasset or .umap plus any same-stem .uexp/.ubulk/.uptnl
    members named by the verified Pak directory.  The result only establishes
    file-level closure; it deliberately does not claim that UObject exports can
    already be decoded or converted.
    """

    primary_extensions = {".uasset", ".umap"}
    companion_extensions = (".uexp", ".ubulk", ".uptnl")
    directory_by_folded = {
        item["path"].casefold(): item["path"] for item in directory_entries
    }
    extracted_by_folded = {
        item["path"].casefold(): item for item in extracted_entries
    }
    packages = []
    companion_owners: set[str] = set()
    for entry in sorted(extracted_entries, key=lambda item: item["path"]):
        package_path = PurePosixPath(entry["path"])
        if package_path.suffix.casefold() not in primary_extensions:
            continue
        stem = str(package_path.with_suffix(""))
        directory_companions = []
        extracted_companions = []
        missing_companions = []
        for extension in companion_extensions:
            folded = f"{stem}{extension}".casefold()
            companion = directory_by_folded.get(folded)
            if companion is None:
                continue
            directory_companions.append(companion)
            companion_owners.add(companion.casefold())
            if folded in extracted_by_folded:
                extracted_companions.append(companion)
            else:
                missing_companions.append(companion)
        if not directory_companions:
            status = "no_external_companions_listed"
            readiness = "self_contained_file_candidate"
        elif not missing_companions:
            status = "complete"
            readiness = "file_set_complete_candidate"
        elif extracted_companions:
            status = "partial"
            readiness = "not_ready_missing_companions"
        else:
            status = "primary_only"
            readiness = "not_ready_missing_companions"
        packages.append(
            {
                "path": entry["path"],
                "sha256": entry["sha256"],
                "bytes": entry["bytes"],
                "namespace": package_path.parts[0] if package_path.parts else "",
                "directory_companions": directory_companions,
                "extracted_companions": extracted_companions,
                "missing_companions": missing_companions,
                "closure_status": status,
                "conversion_readiness": readiness,
            }
        )
    extracted_orphans = sorted(
        item["path"]
        for item in extracted_entries
        if PurePosixPath(item["path"]).suffix.casefold() in companion_extensions
        and item["path"].casefold() not in companion_owners
    )
    closure_counts = Counter(item["closure_status"] for item in packages)
    readiness_counts = Counter(item["conversion_readiness"] for item in packages)
    namespace_counts = Counter(item["namespace"] for item in packages)
    manifest = {
        "source": source_label,
        "source_sha256": source_sha256,
        "basis": {
            "directory_entry_count": len(directory_entries),
            "extracted_entry_count": len(extracted_entries),
            "extraction_manifest": extraction_manifest,
        },
        "semantics": "File-level package closure only; no UObject deserialization or media conversion is claimed.",
        "summary": {
            "extracted_primary_packages": len(packages),
            "closure_status_counts": dict(sorted(closure_counts.items())),
            "conversion_readiness_counts": dict(sorted(readiness_counts.items())),
            "namespace_counts": dict(sorted(namespace_counts.items())),
            "orphan_extracted_companions": len(extracted_orphans),
        },
        "packages": packages,
        "orphan_companions": extracted_orphans,
    }
    source_stem = Path(source_label).stem
    destination = (
        output
        / "unreal-analysis"
        / f"{source_stem}__{source_sha256[:12]}"
        / "package-closure.json"
    )
    data = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    manifest_output = output_record(
        destination, data, output, "unreal_package_closure_manifest"
    )
    return {**manifest["summary"], "manifest": manifest_output}


def audit_unreal(
    workspace: Path,
    samples: list[dict[str, Any]],
    android_audit: dict[str, Any],
    output: Path,
) -> dict[str, Any]:
    records = []
    for sample in samples:
        path = workspace / sample["path"]
        record: dict[str, Any] = {
            "source": sample["path"],
            "sample_type": sample["sample_type"],
            "bytes": sample["bytes"],
            "sha256": sample["sha256"],
            "status": "parsed",
            "outputs": [],
        }
        try:
            if sample["sample_type"] == "unreal_pak":
                parsed = parse_pak(path)
                entries = parsed.pop("_directory_entries", [])
                encoded_entries = parsed.pop("_encoded_entries_data", b"")
                if entries:
                    manifest = write_unreal_directory_manifest(
                        sample["path"], sample["sha256"], "pak", entries, output
                    )
                    record["outputs"].append(manifest)
                    parsed["directory_manifest"] = manifest
                    extraction = extract_supported_pak_entries(
                        path,
                        sample["path"],
                        sample["sha256"],
                        parsed["index_offset"],
                        entries,
                        encoded_entries,
                        output,
                    )
                    closure = write_unreal_package_closure_manifest(
                        sample["path"],
                        sample["sha256"],
                        entries,
                        extraction["entries"],
                        extraction["manifest"],
                        output,
                    )
                    extraction.pop("entries")
                    record["outputs"].extend(extraction.pop("outputs"))
                    record["outputs"].append(closure["manifest"])
                    parsed["package_closure"] = closure
                    parsed["pak_extraction"] = extraction
                record["pak"] = parsed
            elif sample["sample_type"] == "unreal_utoc":
                parsed = parse_utoc(path)
                entries = parsed.pop("_directory_entries", [])
                if entries:
                    manifest = write_unreal_directory_manifest(
                        sample["path"], sample["sha256"], "utoc", entries, output
                    )
                    record["outputs"].append(manifest)
                    parsed["directory_manifest"] = manifest
                record["utoc"] = parsed
            elif sample["sample_type"] == "unreal_ucas":
                record["ucas"] = {"bytes": path.stat().st_size, "sha256": sha256_file(path)}
        except Exception as error:
            record.update({"status": "parse_failed", "error": f"{type(error).__name__}: {error}"})
        records.append(record)

    embedded = []
    for package in android_audit.get("packages", []):
        for container in package.get("embedded_obb", []):
            if container.get("container") != "zip" or not container.get("contains_unreal_pak"):
                continue
            source = workspace / package["extracted_root"] / container["path"]
            if not source.is_file():
                continue
            with zipfile.ZipFile(source) as archive:
                for info in archive.infolist():
                    if info.is_dir() or not info.filename.lower().endswith(".pak"):
                        continue
                    parts = safe_zip_name(info.filename)
                    data = archive.read(info)
                    destination = output / "unreal" / f"{source.stem}__{sha256_file(source)[:12]}" / Path(*parts)
                    output_item = output_record(destination, data, output, "embedded_android_pak")
                    pak = parse_pak(destination)
                    entries = pak.pop("_directory_entries", [])
                    encoded_entries = pak.pop("_encoded_entries_data", b"")
                    directory_manifest = write_unreal_directory_manifest(
                        info.filename,
                        output_item["sha256"],
                        "pak",
                        entries,
                        output,
                    )
                    pak["directory_manifest"] = directory_manifest
                    extraction = extract_supported_pak_entries(
                        destination,
                        info.filename,
                        output_item["sha256"],
                        pak["index_offset"],
                        entries,
                        encoded_entries,
                        output,
                    )
                    closure = write_unreal_package_closure_manifest(
                        info.filename,
                        output_item["sha256"],
                        entries,
                        extraction["entries"],
                        extraction["manifest"],
                        output,
                    )
                    extraction.pop("entries")
                    extraction_outputs = extraction.pop("outputs")
                    pak["package_closure"] = closure
                    pak["pak_extraction"] = extraction
                    embedded.append(
                        {
                            "container": str(source.relative_to(workspace)),
                            "member": info.filename,
                            "output": output_item,
                            "outputs": [
                                output_item,
                                directory_manifest,
                                *extraction_outputs,
                                closure["manifest"],
                            ],
                            "pak": pak,
                            "status": "extracted_and_parsed",
                        }
                    )
    utoc_by_stem = {Path(item["source"]).stem: item for item in records if item["sample_type"] == "unreal_utoc"}
    ucas_by_stem = {Path(item["source"]).stem: item for item in records if item["sample_type"] == "unreal_ucas"}
    pairs = [
        {"stem": stem, "utoc": utoc_by_stem[stem]["source"], "ucas": ucas_by_stem[stem]["source"], "status": "paired"}
        for stem in sorted(set(utoc_by_stem) & set(ucas_by_stem))
    ]
    pak_method_counts: Counter[str] = Counter()
    for item in records + embedded:
        pak_method_counts.update(
            item.get("pak", {}).get("pak_extraction", {}).get("method_counts", {})
        )
    return {
        "containers": records,
        "embedded_android_paks": embedded,
        "iostore_pairs": pairs,
        "summary": {
            "root_containers": len(records),
            "parsed_root_containers": sum(item["status"] == "parsed" for item in records),
            "embedded_android_paks": len(embedded),
            "verified_pak_indices": sum(
                item.get("pak", {}).get("index_sha1_verified", False) for item in records
            )
            + sum(item["pak"]["index_sha1_verified"] for item in embedded),
            "iostore_pairs": len(pairs),
            "directory_manifests": sum(
                output_item["representation"].endswith(
                    "_directory_index_manifest"
                )
                for item in records
                for output_item in item.get("outputs", [])
            )
            + sum(
                output_item["representation"].endswith(
                    "_directory_index_manifest"
                )
                for item in embedded
                for output_item in item.get("outputs", [])
            ),
            "directory_files": sum(
                item.get("pak", {}).get("directory_entry_count", 0)
                + item.get("utoc", {}).get("file_count", 0)
                for item in records
            )
            + sum(item["pak"].get("directory_entry_count", 0) for item in embedded),
            "pak_entries_extracted": sum(
                item.get("pak", {})
                .get("pak_extraction", {})
                .get("extracted_count", 0)
                for item in records
            )
            + sum(
                item["pak"]
                .get("pak_extraction", {})
                .get("extracted_count", 0)
                for item in embedded
            ),
            "pak_bytes_extracted": sum(
                item.get("pak", {})
                .get("pak_extraction", {})
                .get("extracted_bytes", 0)
                for item in records
            )
            + sum(
                item["pak"]
                .get("pak_extraction", {})
                .get("extracted_bytes", 0)
                for item in embedded
            ),
            "pak_method_counts": dict(sorted(pak_method_counts.items())),
            "engine_associations": sorted(
                {
                    association
                    for item in records + embedded
                    for association in item.get("pak", {})
                    .get("pak_extraction", {})
                    .get("engine_associations", [])
                }
            ),
            "package_closure_manifests": sum(
                bool(item.get("pak", {}).get("package_closure"))
                for item in records + embedded
            ),
            "extracted_primary_packages": sum(
                item.get("pak", {})
                .get("package_closure", {})
                .get("extracted_primary_packages", 0)
                for item in records + embedded
            ),
            "file_set_complete_candidates": sum(
                item.get("pak", {})
                .get("package_closure", {})
                .get("conversion_readiness_counts", {})
                .get("file_set_complete_candidate", 0)
                for item in records + embedded
            ),
            "self_contained_file_candidates": sum(
                item.get("pak", {})
                .get("package_closure", {})
                .get("conversion_readiness_counts", {})
                .get("self_contained_file_candidate", 0)
                for item in records + embedded
            ),
        },
    }


def verify_conversion_manifest(path: Path, output_root: Path, record_iter: Iterable[dict[str, Any]]) -> dict[str, Any]:
    outputs = bytes_total = 0
    errors = []
    for output_item in record_iter:
        target = output_root / output_item["path"]
        if not target.is_file():
            errors.append(f"missing:{target}")
            continue
        if target.stat().st_size != output_item["bytes"] or sha256_file(target) != output_item["sha256"]:
            errors.append(f"hash_or_size:{target}")
            continue
        outputs += 1
        bytes_total += output_item["bytes"]
    return {
        "manifest": str(path),
        "outputs_verified": outputs,
        "bytes_verified": bytes_total,
        "errors": errors,
        "status": "verified" if not errors else "verification_failed",
    }


def audit_conversion_manifests(repo: Path) -> dict[str, Any]:
    results = []
    flash_path = repo / "recovery/flash-static-extraction.json"
    flash = json.loads(flash_path.read_text(encoding="utf-8"))
    flash_root = Path(flash["parameters"]["output"])
    results.append(
        verify_conversion_manifest(
            flash_path,
            flash_root,
            (
                output_item
                for source in flash["sources"]
                for output_item in (
                    list(source.get("outputs", []))
                    + [
                        nested_output
                        for resource in source.get("resources", [])
                        for nested_output in resource["outputs"]
                    ]
                )
            ),
        )
    )
    media_path = repo / "recovery/recovered-media-conversions.json"
    media = json.loads(media_path.read_text(encoding="utf-8"))
    media_root = Path(media["parameters"]["output"])
    results.append(
        verify_conversion_manifest(
            media_path,
            media_root,
            (output_item for record in media["records"] for output_item in record["outputs"]),
        )
    )
    for relative, root_key, package_key in (
        ("recovery/android-unity-object-exports.json", None, "packages"),
        ("recovery/windows-unity-object-exports.json", "parameters", "players"),
    ):
        manifest_path = repo / relative
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        if package_key == "packages":
            package_results = []
            for package in manifest[package_key]:
                root = Path(package["package_root"])
                package_results.append(
                    verify_conversion_manifest(
                        manifest_path,
                        root,
                        (output_item for record in package["records"] for output_item in record["outputs"]),
                    )
                )
            results.extend(package_results)
        else:
            root = Path(manifest["parameters"]["output_dir"])
            results.append(
                verify_conversion_manifest(
                    manifest_path,
                    root,
                    (output_item for package in manifest[package_key] for record in package["records"] for output_item in record["outputs"]),
                )
            )
    return {
        "manifests": results,
        "summary": {
            "manifests": len(results),
            "verified": sum(item["status"] == "verified" for item in results),
            "outputs": sum(item["outputs_verified"] for item in results),
            "bytes": sum(item["bytes_verified"] for item in results),
            "errors": sum(len(item["errors"]) for item in results),
        },
    }


def audit_uassetapi_outputs(output: Path) -> dict[str, Any]:
    reports = []
    outputs = []
    for report_path in sorted(
        (output / "unreal-analysis").glob("*/uassetapi-audit.json")
    ):
        report = json.loads(report_path.read_text(encoding="utf-8"))
        source_key = report_path.parent.name
        report_outputs = [
            {
                **record["full_parse"]["json_output"],
                "path": str(
                    PurePosixPath("unreal-object-json")
                    / source_key
                    / record["full_parse"]["json_output"]["path"]
                ),
            }
            for record in report["records"]
            if record["full_parse"]["status"] == "parsed"
        ]
        report_output = {
            "path": str(report_path.relative_to(output)),
            "bytes": report_path.stat().st_size,
            "sha256": sha256_file(report_path),
            "representation": "uassetapi_audit_manifest",
        }
        all_outputs = [report_output, *report_outputs]
        verification = verify_conversion_manifest(
            report_path, output, all_outputs
        )
        binary_equal = sum(
            record["full_parse"].get("binary_equality_verified") is True
            for record in report["records"]
        )
        reports.append(
            {
                "manifest": report_output,
                "tool_version": report["tool_version"],
                "dependency": report["dependency"],
                "engine_version": report["engine_version"],
                "summary": report["summary"],
                "binary_equality_verified": binary_equal,
                "verification": verification,
            }
        )
        outputs.extend(all_outputs)
    return {
        "reports": reports,
        "outputs": outputs,
        "summary": {
            "reports": len(reports),
            "candidates": sum(item["summary"]["candidates"] for item in reports),
            "structural_parsed": sum(
                item["summary"]["structural_parsed"] for item in reports
            ),
            "full_parsed": sum(item["summary"]["full_parsed"] for item in reports),
            "binary_equality_verified": sum(
                item["binary_equality_verified"] for item in reports
            ),
            "outputs": sum(
                item["verification"]["outputs_verified"] for item in reports
            ),
            "errors": sum(
                len(item["verification"]["errors"]) for item in reports
            ),
        },
    }


def audit_private_rez_supplements(repo: Path, output: Path) -> dict[str, Any]:
    recovery_path = repo / "recovery/private-rez-recovery.json"
    media_path = repo / "recovery/private-rez-media-conversions.json"
    model_path = repo / "recovery/private-rez-model-conversions.json"
    if not recovery_path.is_file() or not media_path.is_file():
        return {
            "status": "not_generated",
            "verifications": [],
            "summary": {},
        }
    recovery = json.loads(recovery_path.read_text(encoding="utf-8"))
    media = json.loads(media_path.read_text(encoding="utf-8"))
    recovery_verification = verify_conversion_manifest(
        recovery_path, output, recovery["outputs"]
    )
    media_verification = verify_conversion_manifest(
        media_path,
        output,
        (record["output"] for record in media["records"]),
    )
    model = (
        json.loads(model_path.read_text(encoding="utf-8"))
        if model_path.is_file()
        else None
    )
    model_verification = (
        verify_conversion_manifest(
            model_path,
            output,
            (record["output"] for record in model["records"]),
        )
        if model is not None
        else None
    )
    verifications = [recovery_verification, media_verification]
    if model_verification is not None:
        verifications.append(model_verification)
    verification_errors = sum(len(item["errors"]) for item in verifications)
    return {
        "status": "verified" if not verification_errors else "verification_failed",
        "manifests": {
            "recovery": str(recovery_path),
            "media": str(media_path),
            **({"model": str(model_path)} if model is not None else {}),
        },
        "recovery_tool_version": recovery["tool_version"],
        "media_tool_version": media["tool_version"],
        **(
            {"model_tool_version": model["tool_version"]}
            if model is not None
            else {}
        ),
        "verifications": verifications,
        "summary": {
            **recovery["summary"],
            "png_conversions": media["summary"]["converted_or_verified"],
            "png_kind_counts": media["summary"]["kind_counts"],
            "png_bytes": media["summary"]["output_bytes"],
            "media_preserved_unsupported": media["summary"][
                "preserved_unsupported"
            ],
            "media_failures": media["summary"]["failures"],
            "ltb_glb_conversions": (
                model["summary"]["converted_or_verified"] if model is not None else 0
            ),
            "ltb_glb_meshes": model["summary"]["meshes"] if model is not None else 0,
            "ltb_glb_vertices": (
                model["summary"]["vertices"] if model is not None else 0
            ),
            "ltb_glb_triangles": (
                model["summary"]["triangles"] if model is not None else 0
            ),
            "ltb_glb_bytes": (
                model["summary"]["output_bytes"] if model is not None else 0
            ),
            "ltb_glb_failures": model["summary"]["failures"] if model is not None else 0,
            "verification_errors": verification_errors,
        },
    }


def markdown(report: dict[str, Any]) -> str:
    unity = report["unity"]
    rez = report["rez"]
    flash = report["flash"]
    unreal = report["unreal"]
    media = report["media"]
    manifests = report["conversion_provenance"]
    return "\n".join(
        [
            "# 专用资源格式转换与证据账本",
            "",
            "所有操作均为只读源文件、来源哈希隔离输出；未知 EXE/DLL 从未执行。",
            "",
            "## UnityPackage / AssetBundle",
            "",
            f"- UnityPackage：{unity['summary']['verified']}/{unity['summary']['packages']} 验证；物化资产 {unity['summary']['assets']}；依赖 GUID 边 {unity['summary']['dependency_edges']}。",
            "- AssetBundle：总索引中无独立样本，记录为不适用；Unity 序列化资源由 Android/Windows 对象导出清单覆盖。",
            "",
            "## REZ / DTX / LTB / LTC",
            "",
            f"- REZ：{rez['summary']['parsed_standard']} 个标准包、{rez['summary']['private_or_unsupported']} 个私有目录变体。",
            f"- 标准条目：{rez['summary']['entries']}；DTX→PNG：{rez['summary']['dtx_png']}；LTB RenderStyle 分类：{rez['summary']['ltb_classified']}。",
            f"- 私有 REZ 内容恢复：LZMA 流 {rez['private_recovery']['summary'].get('lzma_streams', 0)}，解码字节 {rez['private_recovery']['summary'].get('decoded_bytes', 0)}；新物化输出 {rez['private_recovery']['summary'].get('materialized_outputs', 0)}；严格转换 PNG {rez['private_recovery']['summary'].get('png_conversions', 0)}（{json.dumps(rez['private_recovery']['summary'].get('png_kind_counts', {}), ensure_ascii=False, sort_keys=True)}）；LTB 几何→GLB {rez['private_recovery']['summary'].get('ltb_glb_conversions', 0)}、保留失败 {rez['private_recovery']['summary'].get('ltb_glb_failures', 0)}，门禁错误 {rez['private_recovery']['summary'].get('verification_errors', 0)}。",
            f"- loose LTC：{rez['summary']['ltc_decoded']}/{rez['summary']['ltc_sources']} 个严格解码，失败 {rez['summary']['ltc_decode_failed']}；显式结束标记 {rez['summary']['ltc_end_token']}、物理 EOF 结束 {rez['summary']['ltc_physical_eof']}，输出 {rez['summary']['ltc_decoded_bytes']} 字节；同名明文样本精确匹配 {rez['summary']['ltc_plaintext_peer_exact_matches']}。",
            "",
            "## Flash / ATF",
            "",
            f"- SWF 输入：{flash['summary']['sources']}；有效 SWF：{flash['summary']['parsed']}；资源标签：{flash['summary']['resources']}。",
            f"- PNG：{flash['summary'].get('decoded_png', 0)}；矢量：{flash['summary'].get('raw_vector_tag', 0)}；字体：{flash['summary'].get('raw_font_tag', 0)}；Sprite：{flash['summary'].get('raw_sprite_tag', 0)}；ATF：{flash['summary'].get('atf_texture_payload', 0)}。",
            "",
            "## Unreal",
            "",
            f"- 根容器：{unreal['summary']['parsed_root_containers']}/{unreal['summary']['root_containers']}；Android 内嵌 PAK：{unreal['summary']['embedded_android_paks']}；已验证 PAK 索引：{unreal['summary']['verified_pak_indices']}；IoStore 配对：{unreal['summary']['iostore_pairs']}。",
            f"- PAK/UTOC 完整目录清单：{unreal['summary']['directory_manifests']}；恢复文件路径：{unreal['summary']['directory_files']}。PAK 主索引、路径哈希索引、完整目录索引 SHA-1 均验证；UTOC v5 头、压缩方法、完美哈希表布局和目录图均做边界/覆盖检查。",
            f"- Pak v11 已验证并提取支持条目：{unreal['summary']['pak_entries_extracted']} 个、{unreal['summary']['pak_bytes_extracted']} 字节（方法：{json.dumps(unreal['summary']['pak_method_counts'], ensure_ascii=False, sort_keys=True)}）；`.uproject` EngineAssociation：{', '.join(unreal['summary']['engine_associations']) or '未发现'}。",
            f"- Unreal 包文件闭包：{unreal['summary']['package_closure_manifests']} 份清单、{unreal['summary']['extracted_primary_packages']} 个已提取 `.uasset/.umap`；完整伴随文件候选 {unreal['summary']['file_set_complete_candidates']}，目录未列外置伴随文件候选 {unreal['summary']['self_contained_file_candidates']}。这只证明文件级闭包，不代表 UObject 已反序列化。",
            f"- UAssetAPI 对象审计：候选 {unreal['uassetapi']['summary']['candidates']}，结构解析 {unreal['uassetapi']['summary']['structural_parsed']}，完整 UObject 解析 {unreal['uassetapi']['summary']['full_parsed']}，二进制一致性验证 {unreal['uassetapi']['summary']['binary_equality_verified']}，门禁错误 {unreal['uassetapi']['summary']['errors']}。",
            "- UCAS 保留配对与哈希；已识别 Oodle 压缩，剩余压缩条目的内容级对象恢复仍需可信 Oodle 解码器。目录记录与真实内容输出分开统计。",
            "",
            "## 标准媒体",
            "",
            f"- 模型→GLB：{media['model_to_glb']}；贴图→PNG：{media['texture_to_png']}；音频→WAV：{media['audio_to_wav']}；失败合计：{media['model_to_glb_failed'] + media['texture_to_png_failed'] + media['audio_to_wav_failed']}。",
            "",
            "## 转换可追溯性",
            "",
            f"- 验证清单：{manifests['summary']['verified']}/{manifests['summary']['manifests']}；逐输出：{manifests['summary']['outputs']}；字节：{manifests['summary']['bytes']}；错误：{manifests['summary']['errors']}。",
            "- 每条转换保存工具版本、参数、输入/依赖哈希、输出哈希、表示方式和失败原因；原文件未删除或改写。",
            "",
        ]
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--root-index", type=Path, required=True)
    parser.add_argument("--safe-extraction", type=Path, required=True)
    parser.add_argument("--android-audit", type=Path, required=True)
    parser.add_argument("--flash-manifest", type=Path, required=True)
    parser.add_argument("--media-manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--markdown", type=Path, required=True)
    args = parser.parse_args()

    workspace = args.workspace.resolve()
    repo = Path(__file__).resolve().parent.parent
    output = args.output.resolve()
    index = json.loads(args.root_index.read_text(encoding="utf-8"))
    samples_by_type: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for sample in index["samples"]:
        samples_by_type[sample["sample_type"]].append(sample)
    safe_manifest = json.loads(args.safe_extraction.read_text(encoding="utf-8"))
    android_audit = json.loads(args.android_audit.read_text(encoding="utf-8"))
    flash = json.loads(args.flash_manifest.read_text(encoding="utf-8"))
    media = json.loads(args.media_manifest.read_text(encoding="utf-8"))

    report: dict[str, Any] = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "tool": "tools/audit_special_formats.py",
        "tool_version": "8",
        "workspace": str(workspace),
        "root_inventory_sha256": index["inventory_sha256"],
        "safety": {
            "source_mode": "read_only",
            "unknown_executable_run": False,
            "outputs_isolated_by_source_hash": True,
        },
    }
    report["unity"] = audit_unitypackages(workspace, samples_by_type["unity_package"], safe_manifest)
    report["rez"] = audit_rez(workspace, samples_by_type["lithtech_rez"], output)
    report["rez"]["private_recovery"] = audit_private_rez_supplements(repo, output)
    report["flash"] = {"manifest": str(args.flash_manifest), "summary": flash["summary"]}
    unreal_samples = samples_by_type["unreal_pak"] + samples_by_type["unreal_ucas"] + samples_by_type["unreal_utoc"]
    report["unreal"] = audit_unreal(workspace, unreal_samples, android_audit, output)
    report["unreal"]["uassetapi"] = audit_uassetapi_outputs(output)
    report["media"] = media["summary"]
    report["conversion_provenance"] = audit_conversion_manifests(repo)
    report["conversion_provenance"]["manifests"].extend(
        report["rez"]["private_recovery"]["verifications"]
    )
    special_outputs = (
        output_item
        for archive in report["rez"]["archives"]
        for output_item in archive["outputs"]
    )
    special_outputs = (
        list(special_outputs)
        + [
            output_item
            for container in report["unreal"]["containers"]
            for output_item in container.get("outputs", [])
        ]
        + [
            output_item
            for item in report["unreal"]["embedded_android_paks"]
            for output_item in item.get("outputs", [item["output"]])
        ]
        + report["unreal"]["uassetapi"]["outputs"]
        + [
            output_item
            for item in report["rez"]["loose_ltc"]
            for output_item in item.get("outputs", [])
        ]
    )
    report["conversion_provenance"]["manifests"].append(
        verify_conversion_manifest(args.json, output, special_outputs)
    )
    unity_outputs = [
        output_item
        for package in report["unity"]["packages"]
        for asset in package["assets"]
        for output_item in (asset.get("materialized_asset"), asset.get("materialized_meta"))
        if output_item is not None
    ]
    report["conversion_provenance"]["manifests"].append(
        verify_conversion_manifest(args.safe_extraction, workspace, unity_outputs)
    )
    provenance_items = report["conversion_provenance"]["manifests"]
    report["conversion_provenance"]["summary"] = {
        "manifests": len(provenance_items),
        "verified": sum(item["status"] == "verified" for item in provenance_items),
        "outputs": sum(item["outputs_verified"] for item in provenance_items),
        "bytes": sum(item["bytes_verified"] for item in provenance_items),
        "errors": sum(len(item["errors"]) for item in provenance_items),
    }
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.markdown.write_text(markdown(report), encoding="utf-8")
    print(
        json.dumps(
            {
                "unity_packages_verified": report["unity"]["summary"]["verified"],
                "rez_standard": report["rez"]["summary"]["parsed_standard"],
                "rez_private": report["rez"]["summary"]["private_or_unsupported"],
                "unreal_containers": report["unreal"]["summary"]["root_containers"],
                "conversion_outputs_verified": report["conversion_provenance"]["summary"]["outputs"],
                "verification_errors": report["conversion_provenance"]["summary"]["errors"],
            },
            ensure_ascii=False,
        )
    )
    return 1 if (
        report["unity"]["summary"]["verified"] != report["unity"]["summary"]["packages"]
        or report["conversion_provenance"]["summary"]["errors"]
    ) else 0


if __name__ == "__main__":
    raise SystemExit(main())
