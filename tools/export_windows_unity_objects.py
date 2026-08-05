#!/usr/bin/env python3
"""Export every Unity object from Windows players lacking recovered projects."""

from __future__ import annotations

import argparse
import json
import re
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

from export_android_unity_objects import (
    EXPORT_TYPES,
    append_extension,
    export_payloads,
    object_name,
    raw_fallback,
    safe_name,
    sha256_bytes,
    sha256_file,
    write_verified,
)


def select_inputs(player: dict[str, object], workspace: Path) -> tuple[list[Path], list[Path]]:
    data_dir = workspace / str(player["data_dir"])
    serialized: list[Path] = []
    resources: list[Path] = []
    for item in player["key_files"]:  # type: ignore[index]
        path = data_dir / str(item["path"])
        if item["kind"] == "resource_stream":
            resources.append(path)
        else:
            serialized.append(path)
    return sorted(set(serialized)), sorted(set(resources))


def output_slug(player: dict[str, object]) -> str:
    digest = str(player.get("source_directory_sha256") or "unindexed")[:12]
    name = safe_name(Path(str(player["data_dir"])).name.removesuffix("_Data"), "UnityPlayer")
    return f"{name}__{digest}"


def export_player(
    player: dict[str, object], workspace: Path, output_base: Path
) -> dict[str, object]:
    import UnityPy

    serialized, resources = select_inputs(player, workspace)
    output_root = output_base / output_slug(player)
    environment = UnityPy.load(*[str(path) for path in serialized + resources])
    records: list[dict[str, object]] = []
    types: Counter[str] = Counter()
    statuses: Counter[str] = Counter()
    representations: Counter[str] = Counter()
    seen_keys: set[tuple[str, int]] = set()

    for obj in environment.objects:
        type_name = str(obj.type.name)
        path_id = int(obj.path_id)
        source_name = safe_name(Path(str(obj.assets_file.name)).name, "serialized")
        key = (str(obj.assets_file.name), path_id)
        if key in seen_keys:
            raise ValueError(f"duplicate Unity object key: {key}")
        seen_keys.add(key)
        name = object_name(obj)
        stem = f"{source_name}__{path_id}__{safe_name(name, type_name)}"
        base = output_root / "objects" / type_name / stem
        record: dict[str, object] = {
            "source_serialized_file": str(obj.assets_file.name),
            "path_id": path_id,
            "type": type_name,
            "name": name,
            "outputs": [],
        }
        try:
            if type_name in EXPORT_TYPES:
                payloads = export_payloads(obj, base)
                if not payloads:
                    raise ValueError("exporter returned no payload")
                object_status = "converted"
            else:
                payloads = [(append_extension(base, ".bin"), raw_fallback(obj), "raw_object_preserved")]
                object_status = "raw_preserved"
            for path, payload, representation in payloads:
                write_status = write_verified(path, payload)
                representations[representation] += 1
                record["outputs"].append(
                    {
                        "path": str(path.relative_to(output_base)),
                        "bytes": len(payload),
                        "sha256": sha256_bytes(payload),
                        "representation": representation,
                        "write_status": write_status,
                    }
                )
            record["status"] = object_status
        except Exception as error:
            fallback = append_extension(base, ".bin")
            try:
                payload = raw_fallback(obj)
                write_status = write_verified(fallback, payload)
                representations["raw_object_fallback"] += 1
                record["outputs"].append(
                    {
                        "path": str(fallback.relative_to(output_base)),
                        "bytes": len(payload),
                        "sha256": sha256_bytes(payload),
                        "representation": "raw_object_fallback",
                        "write_status": write_status,
                    }
                )
                record["status"] = "raw_fallback"
                record["conversion_error"] = f"{type(error).__name__}: {str(error)[:400]}"
            except Exception as fallback_error:
                record["status"] = "failed"
                record["conversion_error"] = f"{type(error).__name__}: {str(error)[:300]}"
                record["fallback_error"] = f"{type(fallback_error).__name__}: {str(fallback_error)[:300]}"
        types[type_name] += 1
        statuses[str(record["status"])] += 1
        records.append(record)

    manifest = {
        "data_dir": player["data_dir"],
        "source_directory_sha256": player.get("source_directory_sha256"),
        "unity_version": player["unity_version"],
        "backend": player["backend"],
        "output_root": str(output_root.relative_to(output_base)),
        "inputs": [
            {
                "path": str(path.relative_to(workspace)),
                "bytes": path.stat().st_size,
                "sha256": sha256_file(path),
                "role": "serialized" if path in serialized else "resource_stream",
            }
            for path in serialized + resources
        ],
        "objects": len(records),
        "types": dict(sorted(types.items())),
        "statuses": dict(sorted(statuses.items())),
        "representations": dict(sorted(representations.items())),
        "records": records,
    }
    (output_root / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    del environment
    return manifest


def markdown(report: dict[str, object]) -> str:
    lines = [
        "# Windows Unity 缺失发布包对象导出",
        "",
        "本流程只处理尚无 AssetRipper 恢复工程的 Windows `_Data`。每个对象至少保存一种表示；",
        "可解码资源转换为标准格式，结构对象保存类型树 JSON，不支持的对象保留原始二进制。",
        "输出以 `_Data` 总索引哈希隔离，不运行任何 Windows EXE/DLL。",
        "",
        "| `_Data` | Unity | 输入 | 对象 | 转换 | 原样/回退 | 失败 |",
        "|---|---|---:|---:|---:|---:|---:|",
    ]
    for package in report["players"]:  # type: ignore[index]
        statuses = Counter(package["statuses"])
        lines.append(
            f"| `{package['data_dir']}` | {package['unity_version']} | {len(package['inputs'])} | "
            f"{package['objects']} | {statuses['converted']} | "
            f"{statuses['raw_preserved'] + statuses['raw_fallback']} | {statuses['failed']} |"
        )
    total_objects = sum(item["objects"] for item in report["players"])  # type: ignore[index]
    total_failed = sum(item["statuses"].get("failed", 0) for item in report["players"])  # type: ignore[index]
    lines += [
        "",
        f"合计对象：{total_objects}；失败：{total_failed}。",
        "",
        "依赖：`UnityPy==1.25.2`、`Pillow==11.3.0`（固定于 `tools/requirements.txt`）。",
        "每个输入与输出的 SHA-256、字节数、表示方式和失败原因均在机器清单及分包 `manifest.json` 中。",
        "",
    ]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--windows-audit", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--markdown", type=Path, required=True)
    args = parser.parse_args()

    workspace = args.workspace.resolve()
    output_dir = args.output_dir.resolve()
    audit = json.loads(args.windows_audit.read_text(encoding="utf-8"))
    selected = [player for player in audit["unity_players"] if not player["exported_project"]]
    players = [export_player(player, workspace, output_dir) for player in selected]
    report: dict[str, object] = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "tool": "tools/export_windows_unity_objects.py",
        "parameters": {
            "selection": "unity_players where exported_project is null",
            "output_dir": str(output_dir),
        },
        "unitypy_version": __import__("UnityPy").__version__,
        "players": players,
    }
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.markdown.write_text(markdown(report), encoding="utf-8")
    print(
        json.dumps(
            {
                "players": len(players),
                "objects": sum(player["objects"] for player in players),
                "failed": sum(player["statuses"].get("failed", 0) for player in players),
            },
            ensure_ascii=False,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
