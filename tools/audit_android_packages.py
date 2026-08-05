#!/usr/bin/env python3
"""Perform a repeatable, execution-free audit of extracted Android packages."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import struct
import subprocess
import xml.etree.ElementTree as ET
import zipfile
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path


ANDROID_NS = "http://schemas.android.com/apk/res/android"
ANDROID = "{" + ANDROID_NS + "}"
UNITY_VERSION_RE = re.compile(rb"(?<![0-9])([3456]|20\d\d)\.\d+\.\d+[abcfp]\d+(?![0-9])")
URL_RE = re.compile(rb"(?:https?|wss?)://[^\x00-\x20\"'<>]{4,240}", re.I)
GAME_TERMS = (
    "weapon", "gun", "rifle", "pistol", "shotgun", "grenade", "knife",
    "player", "health", "damage", "network", "room", "map", "controller",
    "武器", "手雷", "伤害", "玩家", "地图", "房间",
)
PAK_MAGIC = b"\xe1\x12\x6f\x5a"
PAK_VERSION_NAMES = {
    8: "FNameBasedCompressionMethod", 9: "FrozenIndex",
    10: "PathHashIndex", 11: "Fnv64BugFix", 12: "Utf8PakDirectory",
}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def run(command: list[str], env: dict[str, str] | None = None) -> str:
    completed = subprocess.run(
        command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
        errors="replace", env=env,
    )
    if completed.returncode != 0:
        raise RuntimeError(
            f"command failed ({completed.returncode}): {' '.join(command)}\n"
            + completed.stderr.strip()
        )
    return completed.stdout


def parse_manifest(xml_text: str) -> dict[str, object]:
    root = ET.fromstring(xml_text)
    application = root.find("application")
    uses_sdk = root.find("uses-sdk")
    components: dict[str, list[dict[str, object]]] = {}
    for tag in ("activity", "activity-alias", "service", "provider", "receiver"):
        entries: list[dict[str, object]] = []
        if application is not None:
            for node in application.findall(tag):
                actions = [
                    child.get(ANDROID + "name")
                    for intent in node.findall("intent-filter")
                    for child in intent.findall("action")
                    if child.get(ANDROID + "name")
                ]
                categories = [
                    child.get(ANDROID + "name")
                    for intent in node.findall("intent-filter")
                    for child in intent.findall("category")
                    if child.get(ANDROID + "name")
                ]
                entries.append({
                    "name": node.get(ANDROID + "name"),
                    "exported": node.get(ANDROID + "exported"),
                    "permission": node.get(ANDROID + "permission"),
                    "authorities": node.get(ANDROID + "authorities"),
                    "actions": actions, "categories": categories,
                })
        components[tag] = entries
    permissions = sorted({
        node.get(ANDROID + "name")
        for tag in ("uses-permission", "uses-permission-sdk-23")
        for node in root.findall(tag)
        if node.get(ANDROID + "name")
    })
    return {
        "package": root.get("package"),
        "version_code": root.get(ANDROID + "versionCode"),
        "version_name": root.get(ANDROID + "versionName"),
        "install_location": root.get(ANDROID + "installLocation"),
        "platform_build_version": root.get("platformBuildVersionName"),
        "min_sdk": uses_sdk.get(ANDROID + "minSdkVersion") if uses_sdk is not None else None,
        "target_sdk": uses_sdk.get(ANDROID + "targetSdkVersion") if uses_sdk is not None else None,
        "permissions": permissions,
        "components": components,
        "application": {
            "name": application.get(ANDROID + "name") if application is not None else None,
            "debuggable": application.get(ANDROID + "debuggable") if application is not None else None,
            "uses_cleartext_traffic": application.get(ANDROID + "usesCleartextTraffic") if application is not None else None,
            "network_security_config": application.get(ANDROID + "networkSecurityConfig") if application is not None else None,
            "backup_agent": application.get(ANDROID + "backupAgent") if application is not None else None,
        },
    }


def parse_dex_header(path: Path) -> dict[str, object]:
    data = path.read_bytes()[:112]
    if len(data) < 104 or not data.startswith(b"dex\n"):
        raise ValueError(f"not a complete DEX header: {path}")
    value = lambda offset: struct.unpack_from("<I", data, offset)[0]
    return {
        "path": path.name, "bytes": path.stat().st_size, "sha256": sha256_file(path),
        "dex_version": data[4:7].decode("ascii", "replace"),
        "file_size_header": value(32), "strings": value(56), "types": value(64),
        "prototypes": value(72), "fields": value(80), "methods": value(88),
        "classes": value(96),
    }


def elf_architecture(path: Path) -> tuple[str | None, str | None]:
    data = path.read_bytes()[:64]
    if len(data) < 20 or not data.startswith(b"\x7fELF"):
        return None, None
    endian = "<" if data[5] == 1 else ">"
    machine = struct.unpack_from(endian + "H", data, 18)[0]
    bits = "64" if data[4] == 2 else "32"
    arch = {3: "x86", 40: "arm", 62: "x86_64", 183: "arm64"}.get(machine, f"machine-{machine}")
    return arch, bits


def find_unity_version(root: Path) -> str | None:
    candidates = [
        root / "assets/bin/Data/globalgamemanagers", root / "assets/bin/Data/mainData",
        root / "assets/bin/Data/data.unity3d",
    ]
    candidates += sorted((root / "assets/bin/Data").glob("*"))[:20] if (root / "assets/bin/Data").is_dir() else []
    for path in candidates:
        if not path.is_file():
            continue
        with path.open("rb") as stream:
            match = UNITY_VERSION_RE.search(stream.read(1024 * 1024))
        if match:
            return match.group(0).decode("ascii", "replace")
    return None


def detect_engine(root: Path) -> dict[str, object]:
    files = {path.relative_to(root).as_posix().lower() for path in root.rglob("*") if path.is_file()}
    has_unity = any(path.endswith("/libunity.so") for path in files)
    has_ue = any(path.endswith("/libue4.so") for path in files)
    has_il2cpp = any(path.endswith("/libil2cpp.so") for path in files)
    has_metadata = any(path.endswith("/global-metadata.dat") for path in files)
    has_mono = any("/libmono" in path and path.endswith(".so") for path in files)
    if has_ue:
        return {"engine": "Unreal Engine", "backend": "native UE4", "version": None,
                "evidence": ["libUE4.so", "assets/UE4CommandLine.txt"]}
    if has_unity:
        backend = "IL2CPP" if has_il2cpp or has_metadata else "Mono" if has_mono else "unknown"
        evidence = ["libunity.so"]
        if has_mono:
            evidence.append("libmono*.so")
        if has_il2cpp:
            evidence.append("libil2cpp.so")
        if has_metadata:
            evidence.append("global-metadata.dat")
        return {"engine": "Unity", "backend": backend, "version": find_unity_version(root), "evidence": evidence}
    return {"engine": "unknown", "backend": "unknown", "version": None, "evidence": []}


def audit_native_libraries(root: Path) -> list[dict[str, object]]:
    records: list[dict[str, object]] = []
    for path in sorted(root.glob("lib/*/*.so")):
        arch, bits = elf_architecture(path)
        records.append({
            "path": path.relative_to(root).as_posix(), "abi_directory": path.parent.name,
            "bytes": path.stat().st_size, "sha256": sha256_file(path),
            "elf_architecture": arch, "elf_bits": bits,
        })
    return records


def audit_data_layout(root: Path) -> dict[str, object]:
    data_root = root / "assets/bin/Data"
    files = [path for path in data_root.rglob("*") if path.is_file()] if data_root.is_dir() else []
    managed = sorted(path.relative_to(root).as_posix() for path in files if "Managed" in path.parts and path.suffix.lower() == ".dll")
    split_files = [path for path in files if re.search(r"\.split\d+$", path.name)]
    serialized = [path for path in files if path.suffix.lower() == ".assets" or re.fullmatch(r"level\d+", path.name)]
    streams = [path for path in files if path.suffix.lower() in {".resource", ".ress"}]
    return {
        "present": data_root.is_dir(), "files": len(files),
        "bytes": sum(path.stat().st_size for path in files),
        "managed_assemblies": managed, "managed_assembly_count": len(managed),
        "split_files": len(split_files), "serialized_files": len(serialized),
        "resource_streams": len(streams),
        "has_global_metadata": any(path.name == "global-metadata.dat" for path in files),
    }


def audit_embedded_obb(root: Path) -> list[dict[str, object]]:
    records: list[dict[str, object]] = []
    for path in sorted(root.rglob("*obb*")):
        if not path.is_file():
            continue
        record: dict[str, object] = {
            "path": path.relative_to(root).as_posix(), "bytes": path.stat().st_size,
            "sha256": sha256_file(path), "container": "unknown", "members": 0,
            "member_samples": [],
        }
        if zipfile.is_zipfile(path):
            with zipfile.ZipFile(path) as archive:
                names = archive.namelist()
                pak_versions = []
                for name in names:
                    if not name.lower().endswith(".pak"):
                        continue
                    data = archive.read(name)
                    offset = data.rfind(PAK_MAGIC)
                    if offset >= 0 and offset + 8 <= len(data):
                        version = struct.unpack_from("<i", data, offset + 4)[0]
                        pak_versions.append({
                            "member": name, "version": version,
                            "version_name": PAK_VERSION_NAMES.get(version),
                            "footer_offset": offset,
                        })
            record["container"] = "zip"
            record["members"] = len(names)
            record["member_samples"] = names[:40]
            record["contains_unreal_pak"] = any(name.lower().endswith(".pak") for name in names)
            record["unreal_pak_versions"] = pak_versions
        records.append(record)
    return records


def dex_endpoints(path: Path) -> list[str]:
    data = path.read_bytes()
    return sorted({match.group(0).decode("utf-8", "replace") for match in URL_RE.finditer(data)})[:100]


def monodis_table(monodis: Path, assembly: Path, option: str) -> str:
    return run([str(monodis), f"--{option}", str(assembly)])


def audit_mono(root: Path, output_root: Path, monodis: Path) -> dict[str, object] | None:
    assembly = root / "assets/bin/Data/Managed/Assembly-CSharp.dll"
    if not assembly.is_file():
        return None
    output_root.mkdir(parents=True, exist_ok=True)
    tables: dict[str, str] = {}
    for option in ("typedef", "fields", "method", "constant"):
        text = monodis_table(monodis, assembly, option)
        (output_root / f"{option}.txt").write_text(text, encoding="utf-8")
        tables[option] = text
    il_path = output_root / "Assembly-CSharp.il"
    run([str(monodis), f"--output={il_path}", str(assembly)])
    il_text = il_path.read_text(encoding="utf-8", errors="replace")
    candidate_types = sorted({
        line.strip() for line in tables["typedef"].splitlines()
        if any(term in line.lower() for term in GAME_TERMS)
    })
    state_machines = sorted({
        line.strip() for line in tables["typedef"].splitlines()
        if "c__Iterator" in line or "d__" in line or ("<" in line and ">" in line)
    })
    literals = Counter(re.findall(r"\bldc\.(?:i4(?:\.\w+)?|i8|r4|r8)\s+([^\r\n]+)", il_text))
    return {
        "assembly": assembly.relative_to(root).as_posix(), "bytes": assembly.stat().st_size,
        "sha256": sha256_file(assembly), "metadata_directory": str(output_root),
        "types": max(0, len(tables["typedef"].splitlines()) - 2),
        "fields": max(0, len(tables["fields"].splitlines()) - 2),
        "methods": max(0, len(tables["method"].splitlines()) - 2),
        "constants": max(0, len(tables["constant"].splitlines()) - 2),
        "candidate_types": candidate_types, "state_machines": state_machines,
        "numeric_il_literals": [{"value": key.strip(), "uses": value} for key, value in literals.most_common(50)],
        "il_sha256": sha256_file(il_path),
    }


def parse_dex_package_summary(text: str) -> dict[str, object]:
    total = None
    packages: list[dict[str, object]] = []
    for line in text.splitlines():
        match = re.match(r"([PCM])\s+d\s+(\d+)\s+(\d+)\s+(\d+)\s+(.+)$", line.strip())
        if not match:
            continue
        kind, defined, referenced, size, name = match.groups()
        entry = {"kind": kind, "defined": int(defined), "referenced": int(referenced), "bytes": int(size), "name": name}
        if name == "<TOTAL>":
            total = entry
        elif kind == "P" and name.count(".") <= 2:
            packages.append(entry)
    return {"total": total, "top_packages": packages[:80]}


def audit_package(
    archive_record: dict[str, object], workspace: Path, extraction_root: Path,
    apkanalyzer: Path, java_home: Path, monodis: Path, mono_output_root: Path,
) -> dict[str, object]:
    apk = workspace / str(archive_record["archive"])
    root = workspace / str(archive_record["destination"])
    env = dict(os.environ)
    env["JAVA_HOME"] = str(java_home)
    manifest_xml = run([str(apkanalyzer), "manifest", "print", str(apk)], env)
    dex_summary = parse_dex_package_summary(
        run([str(apkanalyzer), "dex", "packages", "--defined-only", str(apk)], env)
    )
    dex_files = [parse_dex_header(path) for path in sorted(root.glob("classes*.dex"))]
    for record in dex_files:
        record["network_endpoints"] = dex_endpoints(root / str(record["path"]))
    slug = root.name
    mono = audit_mono(root, mono_output_root / slug, monodis)
    return {
        "archive": archive_record["archive"], "archive_sha256": archive_record["sha256"],
        "archive_bytes": archive_record["bytes"], "extracted_root": archive_record["destination"],
        "manifest": parse_manifest(manifest_xml), "engine": detect_engine(root),
        "dex": {"files": dex_files, "summary": dex_summary},
        "native_libraries": audit_native_libraries(root), "data": audit_data_layout(root),
        "embedded_obb": audit_embedded_obb(root), "mono": mono,
        "security_boundary": "static_only_no_install_no_execution_no_auth_bypass",
    }


def cross_version(packages: list[dict[str, object]], root_index: dict[str, object]) -> dict[str, object]:
    roots = {str(package["extracted_root"]): str(package["archive"]) for package in packages}
    by_hash: dict[str, list[tuple[str, str]]] = defaultdict(list)
    for sample in root_index.get("samples", []):
        path = str(sample.get("path", ""))
        for root, archive in roots.items():
            if path == root or path.startswith(root + "/"):
                by_hash[str(sample["sha256"])].append((archive, path))
                break
    shared = []
    for digest, entries in sorted(by_hash.items()):
        archives = sorted({archive for archive, _ in entries})
        if len(archives) > 1:
            shared.append({"sha256": digest, "archives": archives, "paths": [path for _, path in entries]})
    return {
        "package_count": len(packages), "shared_exact_sample_groups": len(shared),
        "shared_samples": shared,
        "engines": dict(Counter(f"{p['engine']['engine']} {p['engine']['backend']}" for p in packages)),
    }


def summarize_unity_objects(
    packages: list[dict[str, object]], unity_inventory: dict[str, object]
) -> dict[str, dict[str, object]]:
    summary: dict[str, dict[str, object]] = {}
    for package in packages:
        marker = str(package["extracted_root"])
        assets = [
            item for item in unity_inventory.get("assets", [])
            if marker in str(item.get("file", ""))
        ]
        types: Counter[str] = Counter()
        for item in assets:
            types.update(item.get("types", {}))
        summary[str(package["archive"])] = {
            "serialized_files": len(assets),
            "objects": sum(int(item.get("objects", 0)) for item in assets),
            "types": dict(sorted(types.items())),
            "weapon_candidates": sum(len(item.get("weapon_candidates", [])) for item in assets),
        }
    return summary


def summarize_unity_exports(unity_exports: dict[str, object], manifest_path: Path) -> dict[str, object]:
    packages = []
    for package in unity_exports.get("packages", []):
        statuses = Counter(record.get("status") for record in package.get("records", []))
        packages.append({
            "archive": package.get("archive"), "objects": package.get("objects", 0),
            "exported": statuses["exported"], "raw_fallback": statuses["raw_fallback"],
            "failed": statuses["failed"],
        })
    return {
        "manifest": str(manifest_path), "unitypy_version": unity_exports.get("unitypy_version"),
        "packages": packages, "objects": sum(int(item["objects"]) for item in packages),
        "failed": sum(int(item["failed"]) for item in packages),
    }


def markdown(report: dict[str, object]) -> str:
    packages = report["packages"]
    lines = [
        "# Android 安装包综合静态逆向（2026-08-04）", "",
        "本报告只读取 APK、DEX、ELF、Unity 序列化数据和伪装 OBB 的目录/元数据；未安装或运行 APK、DEX、SO、EXE/DLL，未绕过账号、付费或第三方认证。", "",
        "## 版本矩阵", "",
        "| APK | SHA-256 | 包名 | 版本 | SDK | 引擎/后端 | 引擎版本 | DEX | SO | Managed |",
        "|---|---|---|---|---|---|---|---:|---:|---:|",
    ]
    for package in packages:
        manifest = package["manifest"]
        engine = package["engine"]
        lines.append(
            "| `{}` | `{}` | `{}` | {} | {}→{} | {} / {} | {} | {} | {} | {} |".format(
                package["archive"], str(package["archive_sha256"])[:16], manifest["package"],
                manifest["version_name"] or "—", manifest["min_sdk"] or "—", manifest["target_sdk"] or "—",
                engine["engine"], engine["backend"], engine["version"] or "—",
                len(package["dex"]["files"]), len(package["native_libraries"]),
                package["data"]["managed_assembly_count"],
            )
        )
    lines += ["", "## Manifest 与安全配置", ""]
    for package in packages:
        manifest = package["manifest"]
        components = manifest["components"]
        lines += [
            f"### `{package['archive']}`", "",
            f"权限：{', '.join(manifest['permissions']) if manifest['permissions'] else '无声明权限'}。",
            "组件：" + "，".join(f"{key} {len(value)}" for key, value in components.items()) + "。",
            f"网络安全配置：`networkSecurityConfig={manifest['application']['network_security_config']}`，"
            f"`usesCleartextTraffic={manifest['application']['uses_cleartext_traffic']}`。", "",
        ]
        for tag, entries in components.items():
            for entry in entries:
                lines.append(f"- {tag} `{entry['name']}`；actions={entry['actions']}；exported={entry['exported']}")
        lines.append("")
    lines += ["## DEX、原生库与数据容器", ""]
    for package in packages:
        dex = package["dex"]
        total = dex["summary"]["total"] or {}
        data = package["data"]
        lines += [
            f"### `{package['archive']}`", "",
            f"DEX {len(dex['files'])} 个；定义总量 {total.get('defined', '—')}，引用 {total.get('referenced', '—')}，"
            f"头部类定义合计 {sum(item['classes'] for item in dex['files'])}。",
            f"原生库 {len(package['native_libraries'])} 个；ABI："
            + (", ".join(sorted({item['abi_directory'] for item in package['native_libraries']})) or "无") + "。",
            f"Unity 数据文件 {data['files']}，序列化文件 {data['serialized_files']}，资源流 {data['resource_streams']}，"
            f"完整分片 {data['split_files']}，Managed 程序集 {data['managed_assembly_count']}。", "",
        ]
        for obb in package["embedded_obb"]:
            lines.append(
                f"- 伪装/内嵌 OBB `{obb['path']}`：{obb['container']}，{obb['members']} 项，"
                f"contains_unreal_pak={obb.get('contains_unreal_pak', False)}，"
                f"Pak footer={obb.get('unreal_pak_versions', [])}。"
            )
        if package["embedded_obb"]:
            lines.append("")
    lines += ["## Mono 元数据与 IL 恢复", ""]
    for package in packages:
        mono = package["mono"]
        if mono is None:
            lines.append(f"- `{package['archive']}`：非 Mono 包，不适用。")
        else:
            lines.append(
                f"- `{package['archive']}`：类型 {mono['types']}、字段 {mono['fields']}、方法 {mono['methods']}、"
                f"常量 {mono['constants']}、编译器状态机 {len(mono['state_machines'])}；完整元数据表和 IL 位于 "
                f"`{mono['metadata_directory']}`。"
            )
    lines += [
        "", "三个 Unity 包均为 Mono；当前样本没有 `libil2cpp.so` 或 `global-metadata.dat`，因此 IL2CPP 字段布局/方法地址恢复明确记为不适用。",
        "Unreal 包的 PAK 尾部版本为 11（`Fnv64BugFix`）；[Epic 官方 `FPakInfo` API 文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/PakFile/FPakInfo_2)可确认该格式枚举，"
        "但样本未嵌入可验证的 UE4 点版本，因此不从容器版本反推具体引擎补丁号。",
        "", "## Unity 场景与资源对象交叉盘点", "",
        "| APK | 重组文件 | 对象 | GameObject | Mesh | AnimationClip | Material | Texture2D | Shader | AudioClip | Font | Sprite |",
        "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for package in packages:
        item = report["unity_objects"][str(package["archive"])]
        types = item["types"]
        lines.append(
            "| `{}` | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} | {} |".format(
                package["archive"], item["serialized_files"], item["objects"],
                types.get("GameObject", 0), types.get("Mesh", 0), types.get("AnimationClip", 0),
                types.get("Material", 0), types.get("Texture2D", 0), types.get("Shader", 0),
                types.get("AudioClip", 0), types.get("Font", 0), types.get("Sprite", 0),
            )
        )
    lines += [
        "", "`遗迹杀戮(1)` 提供最完整的场景层级、动作和游戏逻辑；`兵魂回忆录` 提供更丰富的新版本网格/UI；"
        "`base.apk.1` 与 `遗迹杀戮(1)` 共享 Unity 5.2.5f1 运行时及若干资源流，可用于交叉补缺。"
        "对象计数是只读目录恢复结果，不代表所有依赖闭包自动完整。",
        f"三个 Unity APK 已进一步导出 {report['unity_exports']['objects']} 个清单相关对象；"
        f"失败 {report['unity_exports']['failed']}。"
        "贴图/Sprite、Mesh、音频、Shader、字体使用标准格式，场景/Prefab、骨架、动作、材质与 UI 引用使用类型树 JSON，"
        "不支持解码的旧对象保留原始二进制。",
        "", "## 跨版本补全结论", "",
        f"四包之间发现 {report['cross_version']['shared_exact_sample_groups']} 组跨包完全相同的已索引样本。"
        "Unity 三包提供可恢复的程序集、场景和序列化资源；Unreal 地图浏览包提供独立的 PAK/IoStore 方向，不能按 Unity GUID 合并。",
        "已有 UnityPy 对象审计覆盖 17 个重组序列化文件、10,491 个对象；Glock/USP 候选仍缺贴图绑定和第一人称手骨骼，不能进入正式武器栏。",
        "", "## 输出与边界", "",
        "- 机器报告：`recovery/android-static-audit.json`。",
        "- Mono 表与完整 IL：`recovery/android-mono-metadata/`。",
        "- Unity 对象总账：`recovery/apk-unity-asset-inventory.json`。",
        "- Unity 对象导出：`recovery/android-unity-object-exports.json` 与 `recovery/ANDROID_UNITY_OBJECT_EXPORTS_2026-08-04.md`。",
        "- 本轮恢复的是静态结构和可识别行为证据，不声称破解加密容器、在线认证或付费逻辑。", "",
    ]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--extraction-index", type=Path, required=True)
    parser.add_argument("--root-index", type=Path, required=True)
    parser.add_argument("--unity-inventory", type=Path, required=True)
    parser.add_argument("--unity-exports", type=Path, required=True)
    parser.add_argument("--apkanalyzer", type=Path, default=Path(shutil.which("apkanalyzer") or "apkanalyzer"))
    parser.add_argument("--java-home", type=Path, required=True)
    parser.add_argument("--monodis", type=Path, required=True)
    parser.add_argument("--mono-output", type=Path, required=True)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--markdown", type=Path, required=True)
    args = parser.parse_args()
    workspace = args.workspace.resolve()
    extraction = json.loads(args.extraction_index.read_text(encoding="utf-8"))
    root_index = json.loads(args.root_index.read_text(encoding="utf-8"))
    unity_inventory = json.loads(args.unity_inventory.read_text(encoding="utf-8"))
    unity_exports = json.loads(args.unity_exports.read_text(encoding="utf-8"))
    packages = [
        audit_package(record, workspace, workspace / str(extraction["output"]), args.apkanalyzer,
                      args.java_home, args.monodis, args.mono_output.resolve())
        for record in extraction["archives"]
    ]
    report: dict[str, object] = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "tools": {"apkanalyzer": str(args.apkanalyzer), "java_home": str(args.java_home), "monodis": str(args.monodis)},
        "packages": packages, "cross_version": cross_version(packages, root_index),
        "unity_objects": summarize_unity_objects(packages, unity_inventory),
        "unity_exports": summarize_unity_exports(unity_exports, args.unity_exports),
        "unity_object_inventory": str(args.unity_inventory),
    }
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.markdown.write_text(markdown(report), encoding="utf-8")
    print(json.dumps({
        "packages": len(packages), "mono": sum(p["mono"] is not None for p in packages),
        "dex": sum(len(p["dex"]["files"]) for p in packages),
        "native_libraries": sum(len(p["native_libraries"]) for p in packages),
        "shared_exact_samples": report["cross_version"]["shared_exact_sample_groups"],
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
