#!/usr/bin/env python3
"""Build a read-only inventory for every recovered resource pool and archive."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import struct
import subprocess
import zipfile
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path


CATEGORIES = {
    "source": {".cs", ".cpp", ".c", ".h", ".hpp", ".js", ".ts", ".as", ".lua"},
    "model": {".fbx", ".obj", ".3ds", ".max", ".ma", ".mb", ".c4d", ".dae", ".blend", ".ltb"},
    "texture": {".png", ".jpg", ".jpeg", ".tga", ".bmp", ".dds", ".dtx", ".psd", ".atf"},
    "audio": {".wav", ".mp3", ".ogg", ".flac", ".aif", ".aiff"},
    "animation": {".anim", ".controller", ".avatar", ".mask"},
    "unity": {".prefab", ".unity", ".mat", ".asset", ".assets", ".ress", ".bundle"},
    "ui": {".swf", ".fla", ".html", ".css", ".uxml", ".uss"},
    "video": {".mp4", ".mov", ".avi", ".webm"},
    "configuration": {".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".txt"},
    "binary": {".exe", ".dll", ".so", ".dylib", ".lib"},
}
ARCHIVE_SUFFIXES = (
    ".zip", ".rar", ".7z", ".unitypackage", ".apk", ".apk.1", ".rez",
    ".pak", ".ucas", ".utoc", ".obb",
)
OPAQUE_PACKAGE_SUFFIXES = (".pak", ".ucas", ".utoc", ".obb")
SAMPLE_SUFFIXES = (
    ".apk", ".apk.1", ".obb", ".exe", ".dll", ".so", ".dylib",
    ".assets", ".ress", ".resource", ".bundle", ".unity3d", ".unitypackage", ".rez",
    ".pak", ".ucas", ".utoc", ".swf", ".atf", ".zip", ".rar", ".7z",
)
EXTRACTED_OR_GENERATED_ROOTS = {
    "_安全解压资源", "_解压资源", "原程序恢复", "GenesisSoldierSoul",
}
IGNORED_DIRS = {
    ".git", "Library", "Temp", "Logs", "obj", "node_modules", "Build", "dist",
    ".idea", ".vscode", "UserSettings", ".original-samples-vault",
}
CANDIDATE_WORDS = (
    "weapon", "rifle", "pistol", "shotgun", "sniper", "knife", "grenade",
    "m4a1", "m16", "ak74", "awp", "角色", "人物", "动作", "动画", "地图",
    "音效", "枪", "手雷", "脚步", "hud", "ui", "scene", "player",
)


def include_directory(parent: Path, name: str, workspace: Path) -> bool:
    if name in IGNORED_DIRS:
        return False
    try:
        relative = (parent / name).relative_to(workspace)
    except ValueError:
        return True
    # Conversion reports and source-hash-isolated outputs are derived evidence,
    # not new original samples.  Excluding this exact project path keeps the
    # immutable source inventory stable while still allowing a separate ledger.
    return relative.parts[:2] != ("GenesisSoldierSoul", "recovery")


def is_archive(path: Path | str) -> bool:
    name = str(path).lower()
    return any(name.endswith(suffix) for suffix in ARCHIVE_SUFFIXES)


def category_for(name: str) -> str:
    lower = name.lower()
    if is_archive(lower):
        return "archive"
    suffix = Path(lower).suffix
    for category, suffixes in CATEGORIES.items():
        if suffix in suffixes:
            return category
    return "other"


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def normalized_suffix(path: Path | str) -> str:
    lower = str(path).lower()
    for suffix in sorted(SAMPLE_SUFFIXES, key=len, reverse=True):
        if lower.endswith(suffix):
            return suffix
    return Path(lower).suffix


def sample_type(path: Path) -> str:
    suffix = normalized_suffix(path)
    by_suffix = {
        ".apk": "android_apk", ".apk.1": "android_apk_split_or_renamed",
        ".obb": "android_obb", ".exe": "windows_pe_executable",
        ".dll": "dynamic_or_managed_library", ".so": "elf_shared_library",
        ".dylib": "mach_o_dynamic_library", ".assets": "unity_serialized_file",
        ".ress": "unity_resource_stream", ".resource": "unity_resource_stream",
        ".bundle": "unity_asset_bundle",
        ".unity3d": "unity_asset_bundle", ".unitypackage": "unity_package",
        ".rez": "lithtech_rez", ".pak": "unreal_pak", ".ucas": "unreal_ucas",
        ".utoc": "unreal_utoc", ".swf": "flash_swf", ".atf": "flash_atf",
        ".zip": "zip_archive", ".rar": "rar_archive", ".7z": "7z_archive",
    }.get(suffix)
    return by_suffix or sniff_sample_type(path) or "unknown_sample"


def sniff_sample_type(path: Path) -> str | None:
    """Recognize common containers by magic when names or suffixes are damaged."""
    try:
        with path.open("rb") as stream:
            header = stream.read(4096)
    except OSError:
        return None
    if header.startswith((b"UnityFS\0", b"UnityRaw\0", b"UnityWeb\0")):
        return "unity_asset_bundle"
    if header.startswith(b"PK\x03\x04"):
        return "zip_archive"
    if header.startswith(b"Rar!\x1a\x07"):
        return "rar_archive"
    if header.startswith(b"7z\xbc\xaf\x27\x1c"):
        return "7z_archive"
    if header[:3] in {b"FWS", b"CWS", b"ZWS"}:
        return "flash_swf"
    if header.startswith(b"ATF"):
        return "flash_atf"
    if header.startswith(b"\x7fELF"):
        return "elf_binary"
    if header.startswith(b"MZ") and len(header) >= 64:
        pe_offset = struct.unpack_from("<I", header, 0x3C)[0]
        if pe_offset + 24 <= len(header) and header[pe_offset:pe_offset + 4] == b"PE\0\0":
            characteristics = struct.unpack_from("<H", header, pe_offset + 22)[0]
            return "windows_pe_library" if characteristics & 0x2000 else "windows_pe_executable"
    return None


def binary_architecture(path: Path) -> tuple[str | None, str | None]:
    """Return architecture and static format evidence without loading the binary."""
    try:
        with path.open("rb") as stream:
            header = stream.read(4096)
            if header.startswith(b"MZ") and len(header) >= 64:
                pe_offset = struct.unpack_from("<I", header, 0x3C)[0]
                if pe_offset + 6 > len(header):
                    stream.seek(pe_offset)
                    pe = stream.read(6)
                else:
                    pe = header[pe_offset:pe_offset + 6]
                if pe[:4] == b"PE\0\0":
                    machine = struct.unpack_from("<H", pe, 4)[0]
                    return {
                        0x014C: "x86", 0x8664: "x86_64", 0x01C0: "arm",
                        0x01C4: "armv7", 0xAA64: "arm64",
                    }.get(machine, f"pe-machine-0x{machine:04x}"), "PE machine field"
            if header.startswith(b"\x7fELF") and len(header) >= 20:
                endian = "<" if header[5] == 1 else ">"
                machine = struct.unpack_from(endian + "H", header, 18)[0]
                bits = "64" if header[4] == 2 else "32"
                arch = {3: "x86", 40: "arm", 62: "x86_64", 183: "arm64"}.get(
                    machine, f"elf-machine-{machine}"
                )
                return f"{arch}-{bits}", "ELF class and machine fields"
    except OSError:
        return None, None
    return None, None


UNITY_VERSION_RE = re.compile(rb"(?<![0-9])([3456]|20\d\d)\.\d+\.\d+[abcfp]\d+(?![0-9])")


def unity_version_from_bytes(data: bytes) -> str | None:
    match = UNITY_VERSION_RE.search(data)
    return match.group(0).decode("ascii", "replace") if match else None


def inspect_apk_metadata(path: Path) -> tuple[str | None, list[str], str | None]:
    """Inspect APK members only; AndroidManifest.xml may remain binary AXML."""
    try:
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            arches = sorted({
                name.split("/", 2)[1]
                for name in names
                if name.startswith("lib/") and name.count("/") >= 2
            })
            engine = "unknown"
            lowered = {name.lower() for name in names}
            if any("libunity.so" in name for name in lowered):
                engine = "Unity IL2CPP" if any("libil2cpp.so" in name for name in lowered) else "Unity Mono/unknown backend"
            elif any("libue4.so" in name for name in lowered):
                engine = "Unreal Engine 4"
            version = None
            for candidate in (
                "assets/bin/Data/globalgamemanagers", "assets/bin/Data/data.unity3d",
            ):
                if candidate in names:
                    with archive.open(candidate) as stream:
                        version = unity_version_from_bytes(stream.read(1024 * 1024))
                    if version:
                        break
            return version, arches, engine
    except (OSError, zipfile.BadZipFile, KeyError, RuntimeError):
        return None, [], None


def provenance_class(relative: Path) -> str:
    return "extracted_or_generated" if relative.parts and relative.parts[0] in EXTRACTED_OR_GENERATED_ROOTS else "workspace_original"


def sample_file_record(path: Path, workspace: Path, hash_cache: dict[Path, str]) -> dict[str, object]:
    relative = path.relative_to(workspace)
    resolved = path.resolve()
    digest = hash_cache.setdefault(resolved, sha256_file(path))
    arch, arch_evidence = binary_architecture(path)
    version = None
    engine = None
    architectures: list[str] = [arch] if arch else []
    if normalized_suffix(path) in {".apk", ".apk.1"}:
        version, architectures, engine = inspect_apk_metadata(path)
    else:
        try:
            with path.open("rb") as stream:
                version = unity_version_from_bytes(stream.read(1024 * 1024))
        except OSError:
            pass
    mode = path.stat().st_mode & 0o777
    return {
        "path": str(relative), "kind": "file", "sample_type": sample_type(path),
        "bytes": path.stat().st_size, "sha256": digest,
        "version": version, "architectures": architectures,
        "architecture_evidence": arch_evidence, "engine": engine,
        "mode": f"{mode:03o}", "owner_write_bit": bool(mode & 0o200),
        "provenance_class": provenance_class(relative),
        "scan_policy": "read_only_static_scan",
    }


def sample_directory_record(path: Path, workspace: Path, hash_cache: dict[Path, str]) -> dict[str, object]:
    relative = path.relative_to(workspace)
    digest = hashlib.sha256()
    files = 0
    total_bytes = 0
    version = None
    for child in sorted(item for item in path.rglob("*") if item.is_file() and not item.is_symlink()):
        child_relative = child.relative_to(path).as_posix()
        resolved = child.resolve()
        child_hash = hash_cache.setdefault(resolved, sha256_file(child))
        size = child.stat().st_size
        files += 1
        total_bytes += size
        digest.update(child_relative.encode("utf-8", "surrogateescape"))
        digest.update(b"\0" + str(size).encode("ascii") + b"\0" + child_hash.encode("ascii") + b"\n")
        if version is None and child.name in {"globalgamemanagers", "data.unity3d"}:
            try:
                with child.open("rb") as stream:
                    version = unity_version_from_bytes(stream.read(1024 * 1024))
            except OSError:
                pass
    mode = path.stat().st_mode & 0o777
    return {
        "path": str(relative), "kind": "directory", "sample_type": "unity_player_data_directory",
        "bytes": total_bytes, "files": files, "sha256": digest.hexdigest(),
        "version": version, "architectures": [], "architecture_evidence": None,
        "engine": "Unity", "mode": f"{mode:03o}", "owner_write_bit": bool(mode & 0o200),
        "provenance_class": provenance_class(relative),
        "scan_policy": "read_only_static_scan",
    }


def build_sample_inventory(
    workspace: Path, hash_cache: dict[Path, str] | None = None
) -> tuple[list[dict[str, object]], list[list[str]], str]:
    files: list[Path] = []
    unity_data_dirs: list[Path] = []
    for folder, dirs, names in os.walk(workspace):
        folder_path = Path(folder)
        dirs[:] = [name for name in dirs if include_directory(folder_path, name, workspace)]
        if folder_path.name.endswith("_Data"):
            unity_data_dirs.append(folder_path)
        for name in names:
            path = folder_path / name
            if (
                not path.is_symlink()
                and (normalized_suffix(path) in SAMPLE_SUFFIXES or sniff_sample_type(path) is not None)
            ):
                files.append(path)
    hash_cache = {} if hash_cache is None else hash_cache
    records = [sample_file_record(path, workspace, hash_cache) for path in sorted(files)]
    records += [sample_directory_record(path, workspace, hash_cache) for path in sorted(unity_data_dirs)]
    records.sort(key=lambda item: str(item["path"]))
    by_hash: dict[str, list[dict[str, object]]] = {}
    for record in records:
        by_hash.setdefault(str(record["sha256"]), []).append(record)
    duplicate_groups: list[list[str]] = []
    for group_number, group in enumerate(
        (items for _, items in sorted(by_hash.items()) if len(items) > 1), 1
    ):
        paths = [str(item["path"]) for item in group]
        duplicate_groups.append(paths)
        for item in group:
            item["duplicate_group"] = group_number
            item["canonical_path"] = paths[0]
    inventory_digest = hashlib.sha256(
        json.dumps(records, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()
    return records, duplicate_groups, inventory_digest


def scan_pool(path: Path, workspace: Path) -> dict[str, object]:
    counts: Counter[str] = Counter()
    extensions: Counter[str] = Counter()
    candidates: list[str] = []
    total_bytes = 0
    file_count = 0
    archives: list[Path] = []

    if path.is_file():
        iterator = [(path.parent, [], [path.name])]
    else:
        iterator = os.walk(path)
    for folder, dirs, files in iterator:
        folder_path = Path(folder)
        dirs[:] = [item for item in dirs if include_directory(folder_path, item, workspace)]
        for filename in files:
            file_path = folder_path / filename
            try:
                size = file_path.stat().st_size
            except OSError:
                continue
            file_count += 1
            total_bytes += size
            category = category_for(filename)
            counts[category] += 1
            suffix = ".apk.1" if filename.lower().endswith(".apk.1") else file_path.suffix.lower()
            extensions[suffix or "[no extension]"] += 1
            relative = str(file_path.relative_to(workspace))
            if category == "archive":
                archives.append(file_path)
            if (
                category in {"model", "animation", "audio", "unity", "ui"}
                and any(word in relative.lower() for word in CANDIDATE_WORDS)
                and len(candidates) < 80
            ):
                candidates.append(relative)

    return {
        "path": str(path.relative_to(workspace)),
        "files": file_count,
        "bytes": total_bytes,
        "categories": dict(sorted(counts.items())),
        "top_extensions": dict(extensions.most_common(24)),
        "candidate_samples": candidates,
        "archive_paths": archives,
    }


def inspect_archive(
    path: Path, workspace: Path, hash_cache: dict[Path, str] | None = None
) -> dict[str, object]:
    resolved = path.resolve()
    digest = sha256_file(path)
    if hash_cache is not None:
        digest = hash_cache.setdefault(resolved, digest)
    record: dict[str, object] = {
        "path": str(path.relative_to(workspace)),
        "bytes": path.stat().st_size,
        "sha256": digest,
        "status": "not_listed",
        "members": 0,
        "categories": {},
        "candidate_samples": [],
    }
    categories: Counter[str] = Counter()
    samples: list[str] = []
    if path.suffix.lower() == ".rez":
        record["status"] = "specialized_rez_reader"
        record["note"] = "Use tools/rez_extract.py; CF RF*.REZ directory blocks may be private/encrypted."
        return record
    if any(str(path).lower().endswith(suffix) for suffix in OPAQUE_PACKAGE_SUFFIXES):
        record["status"] = "specialized_package_reader_required"
        record["note"] = (
            "Opaque game package: indexed and hashed only; use a format-specific "
            "read-only parser before extraction."
        )
        return record
    try:
        process = subprocess.Popen(
            ["bsdtar", "-tf", str(path)],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            errors="replace",
        )
        assert process.stdout is not None
        for line in process.stdout:
            member = line.strip()
            if not member or member.endswith("/"):
                continue
            record["members"] = int(record["members"]) + 1
            category = category_for(member)
            categories[category] += 1
            if (
                category in {"model", "animation", "audio", "unity", "ui", "source"}
                and any(word in member.lower() for word in CANDIDATE_WORDS)
                and len(samples) < 24
            ):
                samples.append(member)
        _, stderr = process.communicate(timeout=120)
        record["status"] = "listed" if process.returncode == 0 else "unsupported_or_damaged"
        if stderr.strip():
            record["note"] = stderr.strip().splitlines()[-1][:300]
    except (OSError, subprocess.TimeoutExpired) as exc:
        if "process" in locals():
            process.kill()
        record["status"] = "timeout" if isinstance(exc, subprocess.TimeoutExpired) else "tool_error"
        record["note"] = str(exc)[:300]
    record["categories"] = dict(sorted(categories.items()))
    record["candidate_samples"] = samples
    return record


def markdown(report: dict[str, object]) -> str:
    lines = [
        "# 全目录恢复资源索引",
        "",
        "由 `tools/audit_root_resources.py` 只读生成；压缩包仅读取目录表，不运行其中的程序。",
        "",
        "## 资源池总览",
        "",
        "| 资源池 | 文件 | 大小 | 源码 | 模型 | 动画 | 贴图 | 音频 | Unity | 压缩包 |",
        "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for pool in report["pools"]:  # type: ignore[index]
        categories = pool["categories"]
        lines.append(
            "| `{}` | {} | {:.2f} GiB | {} | {} | {} | {} | {} | {} | {} |".format(
                pool["path"], pool["files"], pool["bytes"] / 1024**3,
                categories.get("source", 0), categories.get("model", 0),
                categories.get("animation", 0), categories.get("texture", 0),
                categories.get("audio", 0), categories.get("unity", 0),
                categories.get("archive", 0),
            )
        )
    status_counts = Counter(item["status"] for item in report["archives"])  # type: ignore[index]
    samples = report["samples"]  # type: ignore[index]
    sample_types = Counter(item["sample_type"] for item in samples)
    provenance = Counter(item["provenance_class"] for item in samples)
    lines += [
        "", "## 原始样本总账", "",
        f"共记录 {len(samples)} 个文件或 Unity `_Data` 样本；清单摘要 SHA-256：`{report['inventory_sha256']}`。",
        f"精确重复组 {len(report['duplicate_groups'])} 个；来源区分：" +
        "，".join(f"{key} {value}" for key, value in sorted(provenance.items())) + "。",
        "所有记录均由只读静态扫描产生；`owner_write_bit` 如实记录源文件权限，不据此修改原样本。",
        "", "类型汇总：" + "，".join(f"{key} {value}" for key, value in sorted(sample_types.items())) + "。",
        "", "| 样本 | 类型 | 大小 | SHA-256 | 版本/引擎 | 架构 | 来源 | 重复组 |",
        "|---|---|---:|---|---|---|---|---:|",
    ]
    for sample in samples:
        version_engine = " / ".join(
            str(value) for value in (sample.get("version"), sample.get("engine")) if value
        ) or "—"
        architectures = ", ".join(sample.get("architectures", [])) or "—"
        lines.append(
            "| `{}` | {} | {} | `{}` | {} | {} | {} | {} |".format(
                sample["path"], sample["sample_type"], sample["bytes"],
                str(sample["sha256"])[:16], version_engine, architectures,
                sample["provenance_class"], sample.get("duplicate_group", "—"),
            )
        )
    lines += [
        "", "## 压缩包目录审计", "",
        "状态汇总：" + "，".join(f"{key} {value}" for key, value in sorted(status_counts.items())) + "。",
        "CF 的 REZ 文件统一交给 `tools/rez_extract.py`，不在下表重复列出 200 余行。",
        "", "| 压缩包 | SHA-256 | 状态 | 文件 | 模型 | 动画 | 音频 | Unity | 源码 |",
        "|---|---|---|---:|---:|---:|---:|---:|---:|",
    ]
    for archive in report["archives"]:  # type: ignore[index]
        if archive["status"] == "specialized_rez_reader":
            continue
        categories = archive["categories"]
        lines.append(
            "| `{}` | `{}` | {} | {} | {} | {} | {} | {} | {} |".format(
                archive["path"], archive["sha256"][:16],
                archive["status"], archive["members"],
                categories.get("model", 0), categories.get("animation", 0),
                categories.get("audio", 0), categories.get("unity", 0),
                categories.get("source", 0),
            )
        )
    ranked = report["next_candidates"]  # type: ignore[index]
    lines += ["", "## 下一批优先候选", ""]
    for index, candidate in enumerate(ranked, 1):
        lines.append(
            f"{index}. `{candidate['path']}` — {candidate['reason']}"
        )
    lines += [
        "", "## 使用原则", "",
        "- 优先采用已有完整 Prefab、骨骼、动画和材质依赖闭包的资源。",
        "- 孤立模型先进入预览与依赖审计，不直接加入正式武器栏。",
        "- EXE/DLL 只作静态文件与托管程序集分析，不执行、不绕过保护。",
        "- 每项正式接入必须同时留下来源、参数、自动审计和运行截图。",
        "",
    ]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--markdown", type=Path, required=True)
    args = parser.parse_args()
    workspace = args.workspace.resolve()

    pool_paths = [path for path in sorted(workspace.iterdir()) if path.is_dir() and not path.name.startswith(".")]
    loose_files = [path for path in sorted(workspace.iterdir()) if path.is_file() and not path.name.startswith(".")]
    pools = [scan_pool(path, workspace) for path in pool_paths]
    if loose_files:
        direct = [scan_pool(path, workspace) for path in loose_files]
        loose_summary = {
            "path": "[根目录散落文件与压缩包]",
            "files": sum(item["files"] for item in direct),
            "bytes": sum(item["bytes"] for item in direct),
            "categories": dict(
                sum((Counter(item["categories"]) for item in direct), Counter())
            ),
            "top_extensions": {},
            "candidate_samples": [],
            "archive_paths": [
                path for item in direct for path in item["archive_paths"]
            ],
        }
        pools.insert(0, loose_summary)

    archives_by_path = {
        path.resolve(): path
        for pool in pools
        for path in pool.pop("archive_paths")
    }
    hash_cache: dict[Path, str] = {}
    archives = [inspect_archive(path, workspace, hash_cache) for path in sorted(archives_by_path.values())]

    next_candidates = [
        {"path": "原程序恢复/*/UnityProject/ExportedProject", "reason": "继续按 GUID 审计尚未登记的完整 Prefab、动画和材质闭包"},
        {"path": "CF2.0/CrossFire", "reason": "继续只读转换 WAV、LTB/DTX 与配置，补齐脚步、换枪、爆炸和角色反馈来源"},
        {"path": "根目录 APK/APK.1", "reason": "已按安装包归档并记录哈希；继续静态比对资源表，不安装、不执行"},
        {"path": "_解压资源 与根目录地图包", "reason": "继续补齐地图材质、天空、光照和遮挡来源记录"},
        {"path": "根目录界面与音效压缩包", "reason": "继续替换仍有来源缺口的 HUD 图标和临时声音"},
    ]
    report: dict[str, object] = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "workspace": str(workspace),
        "pools": pools,
        "archives": archives,
        "next_candidates": next_candidates,
    }
    samples, duplicate_groups, inventory_digest = build_sample_inventory(workspace, hash_cache)
    report["samples"] = samples
    report["duplicate_groups"] = duplicate_groups
    report["inventory_sha256"] = inventory_digest
    report["source_handling"] = {
        "scan_mode": "read_only",
        "source_files_modified": False,
        "extraction_policy": "outputs isolated by source relative path and SHA-256",
        "note": "Permissions are recorded, not changed; the inventory digest detects later mutation.",
    }
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.markdown.write_text(markdown(report), encoding="utf-8")
    print(json.dumps({"pools": len(pools), "archives": len(archives), "json": str(args.json), "markdown": str(args.markdown)}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
