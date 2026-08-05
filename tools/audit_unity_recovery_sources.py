#!/usr/bin/env python3
"""Inventory recovered Unity players and exported weapon assets.

The tool is intentionally read-only. It does not execute binaries from the
archives and does not modify an AssetRipper export. Reports are deterministic
so they can be reviewed and regenerated as new source packages are added.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from collections import Counter
from pathlib import Path


UNITY_VERSION_RE = re.compile(
    rb"\b(?:5\.\d+\.\d+[abfp]\d+|20(?:0[5-9]|1\d|2\d)\.\d+\.\d+[abfp]\d+)\b"
)
WEAPON_NAME_RE = re.compile(
    r"(?:weapon|rifle|pistol|shotgun|sniper|knife|grenade|machinegun|"
    r"muzzle|firearm|m4a1|m16|ak[-_ ]?74|awp)",
    re.IGNORECASE,
)
PREFAB_SIGNAL_RE = re.compile(
    r"m_Name:\s*(WeaponMainLocator|Main|Muzzle|FireLocator|"
    r"LeftHandLocator|RightHand|Clip|Magazine|Trigger)\b",
    re.IGNORECASE,
)
CLIP_NAME_RE = re.compile(
    r"(?:idle|fire|reload|wield|draw|holster|run|walk|zoom|aim)",
    re.IGNORECASE,
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def unity_version(data_dir: Path) -> str | None:
    managers = data_dir / "globalgamemanagers"
    if not managers.is_file():
        return None
    sample = managers.read_bytes()[: 4 * 1024 * 1024]
    match = UNITY_VERSION_RE.search(sample)
    return match.group(0).decode("ascii") if match else None


def directory_size(root: Path) -> int:
    return sum(path.stat().st_size for path in root.rglob("*") if path.is_file())


def normalized_name(path: Path) -> str:
    return re.sub(r"[^0-9a-z\u4e00-\u9fff]", "", path.name.lower())


def match_export(data_dir: Path, exports: list[Path]) -> Path | None:
    source_name = normalized_name(data_dir.parent)
    best: tuple[int, Path] | None = None
    for export in exports:
        export_name = normalized_name(export.parents[1])
        common = len(set(source_name) & set(export_name))
        if source_name in export_name or export_name in source_name:
            common += 100
        if best is None or common > best[0]:
            best = (common, export)
    return best[1] if best and best[0] >= 3 else None


def asset_counts(assets: Path) -> dict[str, int]:
    result: dict[str, int] = {}
    for folder in (
        "GameObject",
        "Mesh",
        "Material",
        "Texture2D",
        "AnimationClip",
        "AnimatorController",
        "AudioClip",
        "TextAsset",
        "Scenes",
        "Scripts",
    ):
        target = assets / folder
        result[folder] = (
            sum(1 for path in target.rglob("*") if path.is_file() and path.suffix != ".meta")
            if target.exists()
            else 0
        )
    return result


def weapon_prefabs(assets: Path) -> list[dict[str, object]]:
    result: list[dict[str, object]] = []
    for prefab in sorted(assets.rglob("*.prefab")):
        try:
            text = prefab.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        name_hit = bool(WEAPON_NAME_RE.search(prefab.stem))
        signals = sorted(set(PREFAB_SIGNAL_RE.findall(text)))
        if not name_hit and len(signals) < 2:
            continue
        result.append(
            {
                "path": str(prefab.relative_to(assets)),
                "name": prefab.stem,
                "socket_signals": signals,
                "sha256": sha256(prefab),
            }
        )
    return result


def weapon_clips(assets: Path) -> list[str]:
    folder = assets / "AnimationClip"
    if not folder.exists():
        return []
    return sorted(
        str(path.relative_to(assets))
        for path in folder.rglob("*.anim")
        if CLIP_NAME_RE.search(path.stem)
    )


def inspect_player(data_dir: Path, export: Path | None, root: Path) -> dict[str, object]:
    managed = data_dir / "Managed" / "Assembly-CSharp.dll"
    shared = sorted(data_dir.glob("sharedassets*.assets"))
    levels = sorted(
        path for path in data_dir.glob("level*") if not path.name.endswith(".resS")
    )
    streams = sorted(data_dir.glob("*.resS"))
    record: dict[str, object] = {
        "data_dir": str(data_dir.relative_to(root)),
        "unity_version": unity_version(data_dir),
        "architecture": "Mono" if managed.is_file() else "unknown_or_IL2CPP",
        "assembly_csharp": str(managed.relative_to(root)) if managed.is_file() else None,
        "size_bytes": directory_size(data_dir),
        "sharedassets_count": len(shared),
        "level_count": len(levels),
        "resource_stream_count": len(streams),
        "has_resources_assets": (data_dir / "resources.assets").is_file(),
        "exported_project": str(export.relative_to(root)) if export else None,
    }
    if export:
        assets = export / "Assets"
        record["export_counts"] = asset_counts(assets)
        record["weapon_prefabs"] = weapon_prefabs(assets)
        record["weapon_animation_clips"] = weapon_clips(assets)
    else:
        record["export_counts"] = None
        record["weapon_prefabs"] = []
        record["weapon_animation_clips"] = []
    return record


def markdown(report: dict[str, object]) -> str:
    lines = [
        "# Unity 安装包与武器恢复索引",
        "",
        "此文件由 `tools/audit_unity_recovery_sources.py` 生成。",
        "审计过程只读取数据，不运行压缩包中的 EXE/DLL。",
        "",
        "## 安装包总览",
        "",
        "| Unity `_Data` | 版本 | 架构 | 共享包 | 关卡 | 已恢复工程 | 武器 Prefab |",
        "|---|---|---:|---:|---:|---:|---:|",
    ]
    for player in report["players"]:  # type: ignore[index]
        lines.append(
            "| `{}` | {} | {} | {} | {} | {} | {} |".format(
                player["data_dir"],
                player["unity_version"] or "未识别",
                player["architecture"],
                player["sharedassets_count"],
                player["level_count"],
                "是" if player["exported_project"] else "否",
                len(player["weapon_prefabs"]),
            )
        )
    lines += ["", "## 恢复武器候选", ""]
    for player in report["players"]:  # type: ignore[index]
        prefabs = player["weapon_prefabs"]
        if not prefabs:
            continue
        lines += [f"### `{player['data_dir']}`", ""]
        for prefab in prefabs:
            sockets = ", ".join(prefab["socket_signals"]) or "无定位点信号"
            lines.append(f"- `{prefab['path']}` — {sockets}")
        clips = player["weapon_animation_clips"]
        lines.append(f"- 相关动画候选：{len(clips)}")
        lines.append("")
    lines += [
        "## 推荐恢复顺序",
        "",
        "1. 优先使用已恢复 Prefab 且具有 `WeaponMainLocator` / `Muzzle` 的武器。",
        "2. 用 GUID 依赖闭包复制 Mesh、Material、Texture、AnimationClip 和 AudioClip。",
        "3. 在独立预览场景验收后再进入 WebGL 主工程。",
        "4. 没有完整依赖的孤立 FBX/OBJ 只作候选，不直接对玩家开放。",
        "",
    ]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--markdown", type=Path, required=True)
    args = parser.parse_args()

    root = args.workspace.resolve()
    extracted = root / "_解压资源"
    recovered = root / "原程序恢复"
    exports = sorted(recovered.glob("*/UnityProject/ExportedProject"))
    data_dirs = sorted(path for path in extracted.rglob("*_Data") if path.is_dir())
    players = [inspect_player(path, match_export(path, exports), root) for path in data_dirs]

    prefab_names = Counter(
        prefab["name"]
        for player in players
        for prefab in player["weapon_prefabs"]  # type: ignore[index]
    )
    report: dict[str, object] = {
        "workspace": str(root),
        "player_count": len(players),
        "export_count": len(exports),
        "players": players,
        "weapon_prefab_name_frequency": dict(sorted(prefab_names.items())),
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
                "players": len(players),
                "exports": len(exports),
                "weapon_prefabs": sum(len(p["weapon_prefabs"]) for p in players),
                "json": str(args.json),
                "markdown": str(args.markdown),
            },
            ensure_ascii=False,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
