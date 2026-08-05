#!/usr/bin/env python3
"""Inventory reconstructed Unity APK assets without loading game code."""

from __future__ import annotations

import argparse
import json
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

import UnityPy


CANDIDATE_TERMS = (
    "weapon", "rifle", "pistol", "shotgun", "sniper", "knife", "grenade",
    "m4a1", "m16", "m9", "ak47", "ak74", "awp", "famas", "an94", "m249",
    "gatling", "assault", "手枪", "步枪", "狙击", "霰弹", "武器", "枪", "刀", "手雷",
)
INTERESTING_TYPES = {
    "AnimationClip", "AnimatorController", "AudioClip", "GameObject", "Material",
    "Mesh", "MonoBehaviour", "Sprite", "Texture2D",
}


def inventory_asset(path: Path) -> dict[str, object]:
    environment = UnityPy.load(str(path))
    counts: Counter[str] = Counter()
    candidates: list[dict[str, object]] = []
    named: Counter[str] = Counter()
    errors: list[str] = []
    for obj in environment.objects:
        type_name = str(obj.type.name)
        counts[type_name] += 1
        if type_name not in INTERESTING_TYPES:
            continue
        try:
            name = obj.peek_name() or ""
        except Exception as exc:  # damaged legacy objects remain auditable
            errors.append(f"{type_name}:{type(exc).__name__}:{str(exc)[:120]}")
            continue
        if name:
            named[type_name] += 1
        lower = name.lower()
        if name and any(term in lower for term in CANDIDATE_TERMS):
            candidates.append(
                {"type": type_name, "name": name, "path_id": int(obj.path_id)}
            )
    del environment
    return {
        "file": str(path),
        "bytes": path.stat().st_size,
        "objects": sum(counts.values()),
        "types": dict(sorted(counts.items())),
        "named_objects": dict(sorted(named.items())),
        "weapon_candidates": candidates,
        "read_errors": errors[:40],
    }


def markdown(report: dict[str, object]) -> str:
    lines = [
        "# APK Unity 资源静态审计（2026-08-03）",
        "",
        "输入来自隔离 APK 解包及完整 `*.splitN` 重组结果；只读取 Unity 序列化对象目录，不执行 APK、DEX 或原生库。",
        "",
        f"解析器：UnityPy `{report['unitypy_version']}`。",
        "",
        "| 安装包资源文件 | 大小 | 对象 | Mesh | Material | Texture2D | AnimationClip | AudioClip | 武器名候选 |",
        "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |",
    ]
    for item in report["assets"]:  # type: ignore[index]
        types = item["types"]
        lines.append(
            "| `{}` | {:.2f} MiB | {} | {} | {} | {} | {} | {} | {} |".format(
                item["file"], item["bytes"] / 1024**2, item["objects"],
                types.get("Mesh", 0), types.get("Material", 0),
                types.get("Texture2D", 0), types.get("AnimationClip", 0),
                types.get("AudioClip", 0), len(item["weapon_candidates"]),
            )
        )
    lines += ["", "## 武器名称候选", ""]
    candidates = [
        (item["file"], candidate)
        for item in report["assets"]  # type: ignore[index]
        for candidate in item["weapon_candidates"]
    ]
    if candidates:
        for file_name, candidate in candidates:
            lines.append(
                f"- `{file_name}` — {candidate['type']} `{candidate['name']}` "
                f"(path id {candidate['path_id']})"
            )
    else:
        lines.append("- 未发现名称可直接证明新武器完整闭包的对象。")
    lines += [
        "",
        "## 结论门禁",
        "",
        "- 名称命中只用于候选定位，不代表资源闭包完整。",
        "- 正式接入仍必须同时证明模型、贴图/材质、第一人称手部骨架和动作依赖。",
        "- DEX、SO 与来源不明程序未执行；PAK/UCAS/UTOC 未使用猜测性解包。",
        "",
    ]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--materialization-manifest", type=Path, required=True)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--markdown", type=Path, required=True)
    args = parser.parse_args()
    materialized = json.loads(
        args.materialization_manifest.read_text(encoding="utf-8")
    )
    root = args.root.resolve()
    assets = [
        inventory_asset(root / item["output"])
        for item in materialized["files"]
        if item["status"] in {"materialized", "already_materialized"}
        and not str(item["split_base"]).endswith(".resource")
    ]
    report: dict[str, object] = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "unitypy_version": getattr(UnityPy, "__version__", "unknown"),
        "assets": assets,
    }
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    args.markdown.write_text(markdown(report), encoding="utf-8")
    print(
        json.dumps(
            {
                "assets": len(assets),
                "objects": sum(item["objects"] for item in assets),
                "weapon_candidates": sum(
                    len(item["weapon_candidates"]) for item in assets
                ),
            },
            ensure_ascii=False,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
