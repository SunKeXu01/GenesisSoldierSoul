#!/usr/bin/env python3
"""Read-only static audit for Windows game clients and Unity player data.

Unknown Windows executables are parsed as bytes and are never launched.  The
only subprocess used for target material is ``monodis``, which reads managed
assemblies and emits metadata/IL without loading them as native code.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import struct
import subprocess
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable


UNITY_VERSION_RE = re.compile(
    rb"\b(?:5\.\d+\.\d+[abcfp]\d+|20(?:0[5-9]|1\d|2\d)\.\d+\.\d+[abcfp]\d+)\b"
)
ASCII_RE = re.compile(rb"[\x20-\x7e]{6,}")
UTF16_RE = re.compile(rb"(?:[\x20-\x7e]\x00){6,}")
IL_STRING_RE = re.compile(r'\bldstr\s+"((?:[^"\\]|\\.)*)"')
TABLE_ROW_RE = re.compile(r"^\s*\d+:", re.MULTILINE)
TYPE_ROW_RE = re.compile(r"^\s*\d+:\s+([^\s(]+)", re.MULTILINE)
URL_RE = re.compile(r"(?i)\b(?:https?|wss?)://[^\s\"'<>]{4,}")
CONFIG_RE = re.compile(r"(?i)(?:[A-Za-z0-9_./\\-]+\.(?:ini|json|xml|cfg|config|txt))\b")
RESOURCE_PATH_RE = re.compile(
    r"(?i)(?:Assets/|Resources/|StreamingAssets/|sharedassets|level\d+|"
    r"[A-Za-z0-9_./\\-]+\.(?:prefab|unity|asset|bundle|mat|fbx|obj|png|dds|tga|wav|ogg))"
)

MACHINE_NAMES = {
    0x014C: "x86",
    0x8664: "x86_64",
    0x01C0: "arm",
    0xAA64: "arm64",
}
SUBSYSTEM_NAMES = {
    1: "native",
    2: "windows_gui",
    3: "windows_console",
    9: "windows_ce_gui",
    10: "efi_application",
}
RESOURCE_TYPES = {
    1: "CURSOR",
    2: "BITMAP",
    3: "ICON",
    4: "MENU",
    5: "DIALOG",
    6: "STRING",
    7: "FONTDIR",
    8: "FONT",
    9: "ACCELERATOR",
    10: "RCDATA",
    14: "GROUP_ICON",
    16: "VERSION",
    24: "MANIFEST",
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_c_string(data: bytes, offset: int, limit: int = 4096) -> str | None:
    if offset < 0 or offset >= len(data):
        return None
    end = data.find(b"\0", offset, min(len(data), offset + limit))
    if end < 0:
        end = min(len(data), offset + limit)
    return data[offset:end].decode("ascii", errors="replace")


def section_entropy(blob: bytes) -> float:
    if not blob:
        return 0.0
    counts = Counter(blob)
    length = len(blob)
    return round(-sum((n / length) * math.log2(n / length) for n in counts.values()), 4)


def parse_pe_bytes(data: bytes) -> dict[str, Any]:
    """Parse security-relevant PE structure without loading the image."""
    if len(data) < 0x40 or data[:2] != b"MZ":
        raise ValueError("not a DOS/PE image")
    pe_offset = struct.unpack_from("<I", data, 0x3C)[0]
    if pe_offset + 24 > len(data) or data[pe_offset : pe_offset + 4] != b"PE\0\0":
        raise ValueError("missing PE signature")
    coff = pe_offset + 4
    machine, section_count, timestamp, _, _, optional_size, characteristics = struct.unpack_from(
        "<HHIIIHH", data, coff
    )
    optional = coff + 20
    if optional + optional_size > len(data) or optional_size < 72:
        raise ValueError("truncated PE optional header")
    magic = struct.unpack_from("<H", data, optional)[0]
    if magic == 0x10B:
        pe_format, thunk_size, ordinal_mask = "PE32", 4, 0x80000000
        image_base = struct.unpack_from("<I", data, optional + 28)[0]
        directory_count_offset, directory_offset = 92, 96
    elif magic == 0x20B:
        pe_format, thunk_size, ordinal_mask = "PE32+", 8, 0x8000000000000000
        image_base = struct.unpack_from("<Q", data, optional + 24)[0]
        directory_count_offset, directory_offset = 108, 112
    else:
        raise ValueError(f"unsupported optional-header magic 0x{magic:04x}")
    entrypoint = struct.unpack_from("<I", data, optional + 16)[0]
    subsystem, dll_characteristics = struct.unpack_from("<HH", data, optional + 68)
    directory_count = 0
    if optional_size >= directory_count_offset + 4:
        directory_count = min(16, struct.unpack_from("<I", data, optional + directory_count_offset)[0])
    directories: list[tuple[int, int]] = []
    for index in range(directory_count):
        pos = optional + directory_offset + index * 8
        directories.append(struct.unpack_from("<II", data, pos) if pos + 8 <= optional + optional_size else (0, 0))
    while len(directories) < 16:
        directories.append((0, 0))

    sections: list[dict[str, Any]] = []
    section_table = optional + optional_size
    for index in range(section_count):
        pos = section_table + index * 40
        if pos + 40 > len(data):
            raise ValueError("truncated PE section table")
        raw_name, virtual_size, virtual_address, raw_size, raw_offset, _, _, _, _, flags = struct.unpack_from(
            "<8sIIIIIIHHI", data, pos
        )
        name = raw_name.split(b"\0", 1)[0].decode("ascii", errors="replace")
        blob = data[raw_offset : min(len(data), raw_offset + raw_size)] if raw_offset < len(data) else b""
        sections.append(
            {
                "name": name,
                "virtual_address": virtual_address,
                "virtual_size": virtual_size,
                "raw_offset": raw_offset,
                "raw_size": raw_size,
                "characteristics": f"0x{flags:08x}",
                "entropy": section_entropy(blob),
                "sha256": hashlib.sha256(blob).hexdigest(),
            }
        )

    def rva_to_offset(rva: int) -> int | None:
        if rva == 0:
            return None
        for section in sections:
            start = int(section["virtual_address"])
            span = max(int(section["virtual_size"]), int(section["raw_size"]))
            if start <= rva < start + span:
                offset = int(section["raw_offset"]) + rva - start
                return offset if 0 <= offset < len(data) else None
        return rva if rva < len(data) else None

    imports: list[dict[str, Any]] = []
    import_rva, import_size = directories[1]
    import_offset = rva_to_offset(import_rva)
    if import_offset is not None:
        for descriptor_index in range(min(4096, max(1, import_size // 20 + 1))):
            pos = import_offset + descriptor_index * 20
            if pos + 20 > len(data):
                break
            original_thunk, _, _, name_rva, first_thunk = struct.unpack_from("<IIIII", data, pos)
            if not any((original_thunk, name_rva, first_thunk)):
                break
            name_offset = rva_to_offset(name_rva)
            dll_name = read_c_string(data, name_offset) if name_offset is not None else None
            thunk_offset = rva_to_offset(original_thunk or first_thunk)
            functions: list[str] = []
            ordinals: list[int] = []
            if thunk_offset is not None:
                fmt = "<I" if thunk_size == 4 else "<Q"
                for thunk_index in range(65536):
                    thunk_pos = thunk_offset + thunk_index * thunk_size
                    if thunk_pos + thunk_size > len(data):
                        break
                    value = struct.unpack_from(fmt, data, thunk_pos)[0]
                    if value == 0:
                        break
                    if value & ordinal_mask:
                        ordinals.append(value & 0xFFFF)
                    else:
                        hint_name_offset = rva_to_offset(value)
                        name = read_c_string(data, hint_name_offset + 2) if hint_name_offset is not None else None
                        if name:
                            functions.append(name)
            imports.append(
                {
                    "dll": dll_name or f"unresolved_rva_{name_rva:08x}",
                    "functions": functions,
                    "ordinals": ordinals,
                }
            )

    exports: list[str] = []
    export_rva, _ = directories[0]
    export_offset = rva_to_offset(export_rva)
    if export_offset is not None and export_offset + 40 <= len(data):
        number_of_names = struct.unpack_from("<I", data, export_offset + 24)[0]
        address_of_names = struct.unpack_from("<I", data, export_offset + 32)[0]
        names_offset = rva_to_offset(address_of_names)
        if names_offset is not None:
            for index in range(min(number_of_names, 100000)):
                pos = names_offset + index * 4
                if pos + 4 > len(data):
                    break
                name_offset = rva_to_offset(struct.unpack_from("<I", data, pos)[0])
                name = read_c_string(data, name_offset) if name_offset is not None else None
                if name:
                    exports.append(name)

    resource_types: list[str] = []
    resource_rva, resource_size = directories[2]
    resource_offset = rva_to_offset(resource_rva)
    if resource_offset is not None and resource_offset + 16 <= len(data):
        named, ids = struct.unpack_from("<HH", data, resource_offset + 12)
        for index in range(min(named + ids, 4096)):
            pos = resource_offset + 16 + index * 8
            if pos + 8 > len(data):
                break
            name_or_id = struct.unpack_from("<I", data, pos)[0]
            if name_or_id & 0x80000000:
                resource_types.append(f"named_0x{name_or_id & 0x7fffffff:08x}")
            else:
                resource_types.append(RESOURCE_TYPES.get(name_or_id, f"TYPE_{name_or_id}"))

    return {
        "format": pe_format,
        "machine": f"0x{machine:04x}",
        "architecture": MACHINE_NAMES.get(machine, f"unknown_0x{machine:04x}"),
        "timestamp": timestamp,
        "timestamp_utc": datetime.fromtimestamp(timestamp, tz=timezone.utc).isoformat() if timestamp else None,
        "characteristics": f"0x{characteristics:04x}",
        "is_dll": bool(characteristics & 0x2000),
        "entrypoint_rva": entrypoint,
        "image_base": image_base,
        "subsystem": SUBSYSTEM_NAMES.get(subsystem, f"unknown_{subsystem}"),
        "dll_characteristics": f"0x{dll_characteristics:04x}",
        "mitigations": {
            "aslr": bool(dll_characteristics & 0x0040),
            "high_entropy_va": bool(dll_characteristics & 0x0020),
            "dep": bool(dll_characteristics & 0x0100),
            "control_flow_guard": bool(dll_characteristics & 0x4000),
        },
        "sections": sections,
        "imports": imports,
        "exports": exports,
        "resource_directory": {
            "present": bool(resource_rva and resource_size),
            "rva": resource_rva,
            "size": resource_size,
            "top_level_types": resource_types,
        },
        "clr_header": {"present": bool(directories[14][0]), "rva": directories[14][0], "size": directories[14][1]},
    }


def interesting_strings(data: bytes, limit: int = 500) -> dict[str, list[str]]:
    categories: dict[str, set[str]] = {
        "urls": set(),
        "config_paths": set(),
        "resource_paths": set(),
        "network_or_engine": set(),
    }
    encoded_values = (
        (match.group(0).decode("ascii", errors="ignore") for match in ASCII_RE.finditer(data)),
        (match.group(0).decode("utf-16le", errors="ignore") for match in UTF16_RE.finditer(data)),
    )
    for values in encoded_values:
        for value in values:
            if len(value) > 512:
                value = value[:512] + "…"
            if URL_RE.search(value):
                categories["urls"].add(value)
            if CONFIG_RE.search(value):
                categories["config_paths"].add(value)
            if RESOURCE_PATH_RE.search(value):
                categories["resource_paths"].add(value)
            if re.search(r"(?i)\b(?:socket|websocket|tcp|udp|http|server|client|connect|unity|unreal|steam)\b", value):
                categories["network_or_engine"].add(value)
    return {key: sorted(values)[:limit] for key, values in categories.items()}


def inspect_pe(path: Path, root: Path, known_hash: str | None = None) -> dict[str, Any]:
    data = path.read_bytes()
    record: dict[str, Any] = {
        "path": str(path.relative_to(root)),
        "bytes": len(data),
        "sha256": known_hash or hashlib.sha256(data).hexdigest(),
    }
    try:
        record.update(parse_pe_bytes(data))
        record["strings"] = interesting_strings(data)
        record["status"] = "parsed"
    except (ValueError, struct.error) as error:
        record.update({"status": "not_pe_or_malformed", "error": str(error)})
    return record


def unity_version(data_dir: Path) -> tuple[str | None, str | None]:
    candidates = (
        data_dir / "globalgamemanagers",
        data_dir / "mainData",
        data_dir / "data.unity3d",
        data_dir / "level0",
        data_dir / "resources.assets",
        data_dir / "Resources" / "unity_builtin_extra",
    )
    for candidate in candidates:
        if candidate.is_file():
            with candidate.open("rb") as stream:
                match = UNITY_VERSION_RE.search(stream.read(8 * 1024 * 1024))
            if match:
                return match.group(0).decode("ascii"), candidate.relative_to(data_dir).as_posix()
    return None, None


def key_unity_files(data_dir: Path, root: Path, known_hashes: dict[str, str]) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for path in sorted(data_dir.rglob("*")):
        if not path.is_file():
            continue
        rel_data = path.relative_to(data_dir).as_posix()
        with path.open("rb") as stream:
            header = stream.read(16)
        kind: str | None = None
        if re.fullmatch(r"level\d+", path.name):
            kind = "scene_serialized_file"
        elif re.fullmatch(r"sharedassets\d+\.assets", path.name):
            kind = "shared_asset_file"
        elif path.name in {
            "resources.assets",
            "globalgamemanagers",
            "globalgamemanagers.assets",
            "mainData",
            "unity default resources",
            "unity_builtin_extra",
        }:
            kind = "unity_data_file"
        elif header.startswith((b"UnityFS", b"UnityWeb", b"UnityRaw")):
            kind = "asset_bundle"
        elif "StreamingAssets" in path.parts:
            kind = "streaming_asset"
        elif path.suffix in {".resS", ".resource"}:
            kind = "resource_stream"
        if kind is None:
            continue
        workspace_rel = str(path.relative_to(root))
        result.append(
            {
                "path": rel_data,
                "kind": kind,
                "bytes": path.stat().st_size,
                "sha256": known_hashes.get(workspace_rel) or sha256(path),
            }
        )
    return result


def normalized(text: str) -> str:
    return re.sub(r"[^0-9a-z\u4e00-\u9fff]", "", text.lower())


def map_export(data_dir: Path, exports: list[Path]) -> Path | None:
    source = normalized(data_dir.parent.name)
    aliases = {
        "六月单机更新金字塔": "六月单机更新金字塔",
        "界面怀旧版十一月更": "界面怀旧版十一月",
        "创世遗迹杀戮": "",
        "退魔炮测": "",
    }
    source = aliases.get(source, source)
    if not source:
        return None
    exact = [path for path in exports if normalized(path.parents[1].name) == source]
    return exact[0] if len(exact) == 1 else None


def export_counts(export: Path | None) -> dict[str, int] | None:
    if export is None:
        return None
    assets = export / "Assets"
    folders = (
        "Scenes",
        "GameObject",
        "Mesh",
        "Material",
        "Texture2D",
        "AnimationClip",
        "AnimatorController",
        "AudioClip",
        "Shader",
        "Font",
        "TextAsset",
        "Scripts",
    )
    return {
        folder: sum(1 for p in (assets / folder).rglob("*") if p.is_file() and p.suffix != ".meta")
        if (assets / folder).is_dir()
        else 0
        for folder in folders
    }


def monodis_table(monodis: Path, option: str, assembly: Path) -> str:
    proc = subprocess.run(
        [str(monodis), option, str(assembly)],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
    )
    return proc.stdout.decode("utf-8", errors="replace")


def inspect_managed_assembly(assembly: Path, monodis: Path, output_dir: Path, root: Path) -> dict[str, Any]:
    output_dir.mkdir(parents=True, exist_ok=True)
    tables: dict[str, str] = {}
    for name, option in (("typedef", "--typedef"), ("fields", "--fields"), ("method", "--method"), ("constant", "--constant")):
        text = monodis_table(monodis, option, assembly)
        (output_dir / f"{name}.txt").write_text(text, encoding="utf-8")
        tables[name] = text
    il_path = output_dir / "Assembly-CSharp.il"
    with il_path.open("wb") as output:
        proc = subprocess.run([str(monodis), str(assembly)], stdout=output, stderr=subprocess.STDOUT, check=False)
    il_text = il_path.read_text(encoding="utf-8", errors="replace")
    strings = sorted(set(IL_STRING_RE.findall(il_text)))
    types = sorted(set(TYPE_ROW_RE.findall(tables["typedef"])))
    field_rows = [line.strip() for line in tables["fields"].splitlines() if TABLE_ROW_RE.match(line)]
    method_rows = [line.strip() for line in tables["method"].splitlines() if TABLE_ROW_RE.match(line)]

    def matching(pattern: str, values: Iterable[str]) -> list[str]:
        regex = re.compile(pattern, re.IGNORECASE)
        return sorted({value for value in values if regex.search(value)})

    semantic = {
        "game_flow_types": matching(r"game|match|round|lobby|room|spawn|player|loading|scene", types),
        "weapon_types": matching(r"weapon|gun|rifle|pistol|shot|bullet|ammo|knife|grenade|damage", types),
        "network_types": matching(r"network|socket|client|server|protocol|message|packet|http", types),
        "resource_types": matching(r"resource|asset|bundle|audio|texture|material|prefab|ui", types),
        "urls": matching(r"(?:https?|wss?)://", strings),
        "resource_paths": matching(r"Assets/|Resources/|StreamingAssets/|\.(?:prefab|unity|asset|png|wav|ogg)$", strings),
        "network_strings": matching(r"socket|server|client|connect|room|lobby|protocol", strings),
        "game_flow_strings": matching(r"game|match|round|lobby|room|spawn|player|loading|scene|map", strings),
        "weapon_fields": matching(r"weapon|gun|rifle|pistol|shot|bullet|ammo|clip|magazine|reload|damage|recoil|spread|fire", field_rows),
        "weapon_methods": matching(r"weapon|gun|rifle|pistol|shot|bullet|ammo|reload|damage|fire", method_rows),
        "network_fields": matching(r"network|socket|client|server|protocol|message|packet|room|lobby", field_rows),
        "network_methods": matching(r"network|socket|client|server|protocol|message|packet|connect|room|lobby", method_rows),
    }
    return {
        "path": str(assembly.relative_to(root)),
        "bytes": assembly.stat().st_size,
        "sha256": sha256(assembly),
        "monodis_exit_code": proc.returncode,
        "metadata": {
            "types": len(TABLE_ROW_RE.findall(tables["typedef"])),
            "fields": len(TABLE_ROW_RE.findall(tables["fields"])),
            "methods": len(TABLE_ROW_RE.findall(tables["method"])),
            "constants": len(TABLE_ROW_RE.findall(tables["constant"])),
            "compiler_state_machines": sum("<" in value and ">" in value for value in types),
        },
        "semantic_evidence": semantic,
        "outputs": {name: str((output_dir / f"{name}.txt").relative_to(root / "GenesisSoldierSoul")) for name in tables}
        | {"full_il": str(il_path.relative_to(root / "GenesisSoldierSoul"))},
    }


def discover_unity_players(root: Path) -> list[Path]:
    extracted = root / "_解压资源"
    return sorted(path for path in extracted.rglob("*_Data") if path.is_dir())


def inspect_unity_player(
    data_dir: Path,
    root: Path,
    exports: list[Path],
    known_hashes: dict[str, str],
) -> dict[str, Any]:
    managed_dir = data_dir / "Managed"
    assembly = managed_dir / "Assembly-CSharp.dll"
    metadata = data_dir / "il2cpp_data" / "Metadata" / "global-metadata.dat"
    parent = data_dir.parent
    stem = data_dir.name[: -len("_Data")]
    player = parent / f"{stem}.exe"
    game_assembly = parent / "GameAssembly.dll"
    export = map_export(data_dir, exports)
    key_files = key_unity_files(data_dir, root, known_hashes)
    version, version_evidence = unity_version(data_dir)
    data_relative = str(data_dir.relative_to(root))
    return {
        "release_root": str(parent.relative_to(root)),
        "data_dir": data_relative,
        "source_directory_sha256": known_hashes.get(data_relative),
        "player_exe": str(player.relative_to(root)) if player.is_file() else None,
        "unity_version": version,
        "unity_version_evidence": version_evidence,
        "backend": "Mono" if assembly.is_file() else "IL2CPP" if metadata.is_file() or game_assembly.is_file() else "unknown",
        "assembly_csharp": str(assembly.relative_to(root)) if assembly.is_file() else None,
        "global_metadata": str(metadata.relative_to(root)) if metadata.is_file() else None,
        "game_assembly": str(game_assembly.relative_to(root)) if game_assembly.is_file() else None,
        "managed_assemblies": len(list(managed_dir.glob("*.dll"))) if managed_dir.is_dir() else 0,
        "key_files": key_files,
        "key_file_counts": dict(sorted(Counter(item["kind"] for item in key_files).items())),
        "exported_project": str(export.relative_to(root)) if export else None,
        "export_counts": export_counts(export),
    }


def discover_release_roots(root: Path, players: list[dict[str, Any]]) -> list[Path]:
    roots = {root / player["release_root"] for player in players}
    crossfire = root / "CF2.0" / "CrossFire"
    if crossfire.is_dir():
        roots.add(crossfire)
    for shipping in (root / "_解压资源").rglob("*-Win64-Shipping.exe"):
        if len(shipping.parents) >= 3:
            roots.add(shipping.parents[2])
    return sorted(roots)


def cross_version_report(players: list[dict[str, Any]], managed: dict[str, dict[str, Any]]) -> dict[str, Any]:
    versions = Counter(player["unity_version"] or "unknown" for player in players)
    resource_owners: dict[str, list[str]] = defaultdict(list)
    for player in players:
        for item in player["key_files"]:
            resource_owners[item["sha256"]].append(f"{player['data_dir']}::{item['path']}")
    shared = {digest: paths for digest, paths in resource_owners.items() if len({p.split("::", 1)[0] for p in paths}) > 1}
    assembly_types: dict[str, set[str]] = {}
    for player in players:
        assembly_path = player["assembly_csharp"]
        if assembly_path and assembly_path in managed:
            evidence = managed[assembly_path]["semantic_evidence"]
            assembly_types[player["data_dir"]] = set().union(
                evidence["game_flow_types"], evidence["weapon_types"], evidence["network_types"], evidence["resource_types"]
            )
    unique_by_player: dict[str, list[str]] = {}
    for owner, types in assembly_types.items():
        others = set().union(*(value for key, value in assembly_types.items() if key != owner)) if len(assembly_types) > 1 else set()
        unique_by_player[owner] = sorted(types - others)
    richest = sorted(
        (
            {
                "data_dir": player["data_dir"],
                "key_resource_bytes": sum(item["bytes"] for item in player["key_files"]),
                "key_resource_files": len(player["key_files"]),
                "exported_asset_files": sum((player["export_counts"] or {}).values()),
            }
            for player in players
        ),
        key=lambda item: (item["exported_asset_files"], item["key_resource_bytes"]),
        reverse=True,
    )
    return {
        "unity_versions": dict(sorted(versions.items())),
        "shared_key_resource_hash_groups": len(shared),
        "shared_key_resources": shared,
        "unique_semantic_types_by_player": unique_by_player,
        "resource_richness_ranking": richest,
    }


def markdown(report: dict[str, Any]) -> str:
    summary = report["summary"]
    supplemental = {
        item["data_dir"]: item for item in (report.get("supplemental_unity_exports") or {}).get("players", [])
    }
    lines = [
        "# Windows 客户端静态逆向报告",
        "",
        "此报告由 `tools/audit_windows_clients.py` 生成。未知 EXE/DLL 从未被执行、注册或注入；",
        "PE 结构由审计器直接按字节解析，托管 DLL 仅交给 `monodis` 做静态元数据/IL 读取。",
        "",
        "## 总览",
        "",
        f"- Unity Windows 发布包：{summary['unity_players']}",
        f"- Unreal Windows 发布包：{summary['unreal_players']}",
        f"- 发布根目录：{summary['release_roots']}",
        f"- PE 出现次数 / 去重后模块：{summary['pe_occurrences']} / {summary['unique_pe_modules']}",
        f"- Mono `Assembly-CSharp.dll`：{summary['managed_game_assemblies']}",
        f"- Unity 关键资源文件：{summary['unity_key_files']}",
        f"- Unity 恢复覆盖：{summary['unity_recovery_coverage']}/{summary['unity_players']}",
        "",
        "## Unity 发布包",
        "",
        "| `_Data` | Unity | 后端 | 架构 | 场景 | Shared | Bundle | Streaming | 已恢复工程 |",
        "|---|---|---|---|---:|---:|---:|---:|---|",
    ]
    pe_by_path = report["pe_by_path"]
    for player in report["unity_players"]:
        exe = pe_by_path.get(player["player_exe"] or "", {})
        counts = player["key_file_counts"]
        lines.append(
            "| `{}` | {} | {} | {} | {} | {} | {} | {} | {} |".format(
                player["data_dir"],
                player["unity_version"] or "未嵌入/未识别",
                player["backend"],
                exe.get("architecture", "未识别"),
                counts.get("scene_serialized_file", 0),
                counts.get("shared_asset_file", 0),
                counts.get("asset_bundle", 0),
                counts.get("streaming_asset", 0),
                "AssetRipper" if player["exported_project"] else "逐对象清单" if player["data_dir"] in supplemental else "否",
            )
        )
    lines += ["", "## Mono 游戏程序集", ""]
    for path, item in report["managed_game_assemblies"].items():
        meta = item["metadata"]
        semantic = item["semantic_evidence"]
        lines += [
            f"### `{path}`",
            "",
            f"- 类型 / 字段 / 方法 / 常量：{meta['types']} / {meta['fields']} / {meta['methods']} / {meta['constants']}",
            f"- 游戏流程 / 武器 / 网络 / 资源语义类型：{len(semantic['game_flow_types'])} / {len(semantic['weapon_types'])} / {len(semantic['network_types'])} / {len(semantic['resource_types'])}",
            f"- 完整 IL：`{item['outputs']['full_il']}`",
            "",
        ]
    lines += ["## 原生 PE 与关键调用关系", ""]
    native = [item for item in report["unique_pe_modules"] if item.get("status") == "parsed" and not item["clr_header"]["present"]]
    imports = Counter(entry["dll"].lower() for item in native for entry in item["imports"])
    lines += [
        f"- 原生去重模块：{len(native)}",
        f"- 具有 Win32 资源段：{sum(item['resource_directory']['present'] for item in native)}",
        f"- 常见导入模块：{', '.join(f'{name} ({count})' for name, count in imports.most_common(15))}",
        "- 每个模块的节区哈希/熵、导入函数、导出符号、资源类型、配置/网络/资源字符串均在 JSON 中保留。",
        "",
        "## 跨版本补全线索",
        "",
    ]
    for item in report["cross_version"]["resource_richness_ranking"]:
        lines.append(
            f"- `{item['data_dir']}`：恢复工程资产 {item['exported_asset_files']}，关键资源 {item['key_resource_files']} 个 / {item['key_resource_bytes']} 字节"
        )
    lines += [
        "",
        f"跨包完全相同的关键资源哈希组：{report['cross_version']['shared_key_resource_hash_groups']}。",
        "具体共享路径与各包独有的游戏流程/武器/网络/资源类型见机器可读 JSON。",
        "",
        "## 安全边界",
        "",
        "- `target_executed: false`",
        "- `target_registered: false`",
        "- `target_injected: false`",
        "- 原始样本未改写；所有输出位于 `GenesisSoldierSoul/recovery/`。",
        "",
    ]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--root-index", type=Path, required=True)
    parser.add_argument("--monodis", type=Path, required=True)
    parser.add_argument("--metadata-dir", type=Path, required=True)
    parser.add_argument("--unity-exports", type=Path)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--markdown", type=Path, required=True)
    args = parser.parse_args()

    root = args.workspace.resolve()
    index = json.loads(args.root_index.read_text(encoding="utf-8"))
    known_hashes = {item["path"]: item["sha256"] for item in index["samples"] if "sha256" in item}
    exports = sorted((root / "原程序恢复").glob("*/UnityProject/ExportedProject"))
    players = [inspect_unity_player(path, root, exports, known_hashes) for path in discover_unity_players(root)]
    release_roots = discover_release_roots(root, players)

    pe_occurrences: list[Path] = []
    for release_root in release_roots:
        pe_occurrences.extend(
            path for path in release_root.rglob("*") if path.is_file() and path.suffix.lower() in {".exe", ".dll"}
        )
    pe_occurrences = sorted(set(pe_occurrences))
    unique_paths_by_hash: dict[str, list[Path]] = defaultdict(list)
    for path in pe_occurrences:
        rel = str(path.relative_to(root))
        digest = known_hashes.get(rel) or sha256(path)
        unique_paths_by_hash[digest].append(path)
    unique_pe: list[dict[str, Any]] = []
    pe_by_path: dict[str, dict[str, Any]] = {}
    for digest, paths in sorted(unique_paths_by_hash.items()):
        canonical = paths[0]
        record = inspect_pe(canonical, root, digest)
        record["occurrences"] = [str(path.relative_to(root)) for path in paths]
        unique_pe.append(record)
        for path in paths:
            pe_by_path[str(path.relative_to(root))] = record

    managed: dict[str, dict[str, Any]] = {}
    for index_number, player in enumerate(players, start=1):
        if not player["assembly_csharp"]:
            continue
        assembly = root / player["assembly_csharp"]
        safe_name = re.sub("[^0-9A-Za-z\u4e00-\u9fff._-]+", "_", data_name(player["data_dir"]))
        slug = f"{index_number:02d}_{safe_name}"
        managed[player["assembly_csharp"]] = inspect_managed_assembly(
            assembly, args.monodis.resolve(), args.metadata_dir.resolve() / slug, root
        )

    unreal_players: list[dict[str, Any]] = []
    for shipping in sorted((root / "_解压资源").rglob("*-Win64-Shipping.exe")):
        unreal_root = shipping.parents[2]
        containers = []
        for path in sorted((unreal_root / "Content" / "Paks").glob("*")) if (unreal_root / "Content" / "Paks").is_dir() else []:
            if path.is_file() and path.suffix.lower() in {".pak", ".ucas", ".utoc"}:
                containers.append(
                    {
                        "path": str(path.relative_to(root)),
                        "bytes": path.stat().st_size,
                        "sha256": known_hashes.get(str(path.relative_to(root))) or sha256(path),
                    }
                )
        unreal_players.append(
            {
                "release_root": str(unreal_root.relative_to(root)),
                "shipping_exe": str(shipping.relative_to(root)),
                "architecture": pe_by_path.get(str(shipping.relative_to(root)), {}).get("architecture"),
                "containers": containers,
                "engine": "Unreal Engine 4" if "UE4" in json.dumps(pe_by_path.get(str(shipping.relative_to(root)), {})) else "Unreal Engine (directory evidence)",
            }
        )

    raw_supplemental_exports = (
        json.loads(args.unity_exports.read_text(encoding="utf-8"))
        if args.unity_exports and args.unity_exports.is_file()
        else None
    )
    supplemental_exports = (
        {
            "manifest": str(args.unity_exports),
            "unitypy_version": raw_supplemental_exports["unitypy_version"],
            "players": [
                {
                    "data_dir": item["data_dir"],
                    "source_directory_sha256": item["source_directory_sha256"],
                    "output_root": item["output_root"],
                    "objects": item["objects"],
                    "statuses": item["statuses"],
                }
                for item in raw_supplemental_exports["players"]
            ],
        }
        if raw_supplemental_exports
        else None
    )
    supplemental_dirs = {
        item["data_dir"] for item in (supplemental_exports or {}).get("players", [])
    }
    recovery_coverage = sum(
        bool(player["exported_project"]) or player["data_dir"] in supplemental_dirs for player in players
    )
    report: dict[str, Any] = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "workspace": str(root),
        "root_inventory_sha256": index["inventory_sha256"],
        "safety": {
            "mode": "read_only_static_analysis",
            "target_executed": False,
            "target_registered": False,
            "target_injected": False,
            "subprocess_policy": "monodis only; target assemblies passed as data inputs",
        },
        "summary": {
            "unity_players": len(players),
            "unreal_players": len(unreal_players),
            "release_roots": len(release_roots),
            "pe_occurrences": len(pe_occurrences),
            "unique_pe_modules": len(unique_pe),
            "managed_game_assemblies": len(managed),
            "unity_key_files": sum(len(player["key_files"]) for player in players),
            "unity_recovery_coverage": recovery_coverage,
        },
        "release_roots": [str(path.relative_to(root)) for path in release_roots],
        "unity_players": players,
        "unreal_players": unreal_players,
        "managed_game_assemblies": managed,
        "supplemental_unity_exports": supplemental_exports,
        "unique_pe_modules": unique_pe,
        "pe_by_path": {path: {key: value for key, value in record.items() if key not in {"sections", "imports", "exports", "strings", "occurrences"}} for path, record in pe_by_path.items()},
        "cross_version": cross_version_report(players, managed),
    }
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.markdown.write_text(markdown(report), encoding="utf-8")
    print(json.dumps(report["summary"], ensure_ascii=False))
    return 0


def data_name(path: str) -> str:
    return Path(path).name.removesuffix("_Data")


if __name__ == "__main__":
    raise SystemExit(main())
