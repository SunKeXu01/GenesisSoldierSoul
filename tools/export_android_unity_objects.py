#!/usr/bin/env python3
"""Export auditable Unity object payloads from source-isolated Android assets."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path


IMAGE_TYPES = {"Texture2D", "Sprite"}
STRUCTURAL_TYPES = {
    "Animation", "AnimationClip", "Animator", "AnimatorController", "Avatar",
    "Canvas", "CanvasGroup", "CanvasRenderer", "GameObject", "LightmapSettings",
    "Material", "MeshFilter", "MeshRenderer", "MonoBehaviour", "MonoScript",
    "NavMeshData", "ParticleSystem", "ParticleSystemRenderer", "RectTransform",
    "RenderSettings", "SkinnedMeshRenderer", "SpriteRenderer", "Transform",
}
DIRECT_TYPES = IMAGE_TYPES | {"AudioClip", "Font", "Mesh", "Shader", "TextAsset"}
EXPORT_TYPES = STRUCTURAL_TYPES | DIRECT_TYPES


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def safe_name(value: str, fallback: str) -> str:
    cleaned = re.sub(r"[\\/:*?\"<>|\x00-\x1f]+", "_", value).strip(" ._")
    cleaned = re.sub(r"\s+", "_", cleaned)
    return (cleaned or fallback)[:100]


def json_bytes(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2, default=str) + "\n").encode("utf-8")


def write_verified(path: Path, data: bytes) -> str:
    digest = sha256_bytes(data)
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        if not path.is_file() or path.stat().st_size != len(data) or sha256_file(path) != digest:
            raise ValueError(f"existing output differs: {path}")
        return "verified_existing"
    path.write_bytes(data)
    if sha256_file(path) != digest:
        raise IOError(f"output verification failed: {path}")
    return "exported"


def append_extension(base: Path, extension: str) -> Path:
    return base.parent / (base.name + extension)


def object_name(obj: object) -> str:
    try:
        return str(obj.peek_name() or "")
    except Exception:
        return ""


def raw_fallback(obj: object) -> bytes:
    try:
        return bytes(obj.get_raw_data())
    except Exception:
        data = obj.read()
        return json_bytes(data)


def export_payloads(obj: object, base: Path) -> list[tuple[Path, bytes, str]]:
    type_name = str(obj.type.name)
    data = obj.read()
    if type_name in IMAGE_TYPES:
        image = data.image
        stream = io.BytesIO()
        image.save(stream, format="PNG")
        return [(append_extension(base, ".png"), stream.getvalue(), "decoded_png")]
    if type_name == "Mesh":
        from UnityPy.export.MeshExporter import export_mesh
        value = export_mesh(data)
        return [(append_extension(base, ".obj"), value.encode("utf-8") if isinstance(value, str) else bytes(value), "wavefront_obj")]
    if type_name == "AudioClip":
        from UnityPy.export.AudioClipConverter import extract_audioclip_samples
        outputs = []
        for sample_name, sample in sorted(extract_audioclip_samples(data).items()):
            suffix = Path(sample_name).suffix or ".bin"
            sample_stem = safe_name(Path(sample_name).stem, "sample")
            outputs.append((base.parent / f"{base.name}__{sample_stem}{suffix}", bytes(sample), "decoded_audio"))
        return outputs
    if type_name == "Shader":
        from UnityPy.export.ShaderConverter import export_shader
        value = export_shader(data)
        return [(append_extension(base, ".shader"), value.encode("utf-8") if isinstance(value, str) else bytes(value), "shader_text")]
    if type_name == "TextAsset":
        value = getattr(data, "m_Script", getattr(data, "script", b""))
        return [(append_extension(base, ".txt"), value.encode("utf-8") if isinstance(value, str) else bytes(value), "text_asset")]
    if type_name == "Font":
        value = bytes(getattr(data, "m_FontData", b""))
        if value:
            suffix = ".otf" if value.startswith(b"OTTO") else ".ttf" if value.startswith(b"\x00\x01\x00\x00") else ".fontdata"
            return [(append_extension(base, suffix), value, "font_binary")]
    tree = obj.read_typetree()
    return [(append_extension(base, ".json"), json_bytes(tree), "type_tree_json")]


def export_package(
    archive: str, package_root: Path, asset_paths: list[Path], resource_paths: list[Path]
) -> dict[str, object]:
    import UnityPy

    output_root = package_root / "_exported-unity-objects"
    environment = UnityPy.load(*[str(path) for path in asset_paths + resource_paths])
    records: list[dict[str, object]] = []
    counts: Counter[str] = Counter()
    statuses: Counter[str] = Counter()
    for obj in environment.objects:
        type_name = str(obj.type.name)
        if type_name not in EXPORT_TYPES:
            continue
        name = object_name(obj)
        source_name = safe_name(Path(str(obj.assets_file.name)).name, "serialized")
        stem = f"{source_name}__{int(obj.path_id)}__{safe_name(name, type_name)}"
        base = output_root / type_name / stem
        record: dict[str, object] = {
            "type": type_name, "path_id": int(obj.path_id), "name": name,
            "source_serialized_file": str(obj.assets_file.name), "outputs": [],
        }
        try:
            payloads = export_payloads(obj, base)
            if not payloads:
                raise ValueError("exporter returned no payload")
            for path, payload, representation in payloads:
                status = write_verified(path, payload)
                statuses[status] += 1
                record["outputs"].append({
                    "path": str(path.relative_to(package_root)), "bytes": len(payload),
                    "sha256": sha256_bytes(payload), "representation": representation,
                    "status": status,
                })
            record["status"] = "exported"
        except Exception as exc:
            fallback = append_extension(base, ".bin")
            try:
                payload = raw_fallback(obj)
                status = write_verified(fallback, payload)
                statuses[status] += 1
                record["outputs"].append({
                    "path": str(fallback.relative_to(package_root)), "bytes": len(payload),
                    "sha256": sha256_bytes(payload), "representation": "raw_object_fallback",
                    "status": status,
                })
                record["status"] = "raw_fallback"
                record["export_error"] = f"{type(exc).__name__}: {str(exc)[:300]}"
            except Exception as fallback_exc:
                record["status"] = "failed"
                record["export_error"] = f"{type(exc).__name__}: {str(exc)[:200]}"
                record["fallback_error"] = f"{type(fallback_exc).__name__}: {str(fallback_exc)[:200]}"
        counts[type_name] += 1
        records.append(record)
    del environment
    return {
        "archive": archive, "package_root": str(package_root),
        "input_assets": [{"path": str(path), "bytes": path.stat().st_size, "sha256": sha256_file(path)} for path in asset_paths],
        "resource_streams": [{"path": str(path), "bytes": path.stat().st_size, "sha256": sha256_file(path)} for path in resource_paths],
        "objects": len(records), "types": dict(sorted(counts.items())),
        "output_statuses": dict(sorted(statuses.items())), "records": records,
    }


def markdown(report: dict[str, object]) -> str:
    lines = [
        "# Android Unity 对象导出清单（2026-08-04）", "",
        "输出按 APK 来源目录隔离；源 APK、DEX、SO 和 Unity 数据均只读，未运行任何包内代码。"
        "可直接转换的贴图、Sprite、Mesh、音频、Shader、文本和字体使用标准格式；"
        "场景/Prefab 层级、骨骼、动画、材质和 UI 组件保存为类型树 JSON，失败对象保留原始二进制。", "",
        "| APK | 输入序列化文件 | 资源流 | 导出对象 | 直接/JSON成功 | 原始回退 | 失败 |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]
    for package in report["packages"]:
        statuses = Counter(record["status"] for record in package["records"])
        lines.append(
            f"| `{package['archive']}` | {len(package['input_assets'])} | {len(package['resource_streams'])} | "
            f"{package['objects']} | {statuses['exported']} | {statuses['raw_fallback']} | {statuses['failed']} |"
        )
    lines += ["", "## 类型汇总", ""]
    total: Counter[str] = Counter()
    for package in report["packages"]:
        total.update(package["types"])
    lines.append("，".join(f"{key} {value}" for key, value in sorted(total.items())) + "。")
    lines += [
        "", "## 表示方式", "",
        "- `Texture2D/Sprite` → PNG；`Mesh` → OBJ；`AudioClip` → WAV/原编码样本；`Shader` → Shader 文本；字体 → TTF/OTF/原字节。",
        "- `GameObject/Transform/RectTransform/Avatar/Animator/AnimationClip/Material/MonoBehaviour` 等 → 类型树 JSON，用于恢复场景、Prefab、骨架、动作、材质和 UI 引用关系。",
        "- 解码不支持时输出 `.bin`，同时在机器清单记录错误；没有对象因解码失败而静默丢失。",
        "- Unreal OBB/PAK 不由此工具猜测性解包，继续按专用格式任务处理。", "",
        "依赖固定在 `tools/requirements.txt`（UnityPy 1.25.2、Pillow 11.3.0）。导出命令：", "",
        "```bash",
        "PYTHONPATH=/path/to/pinned/site-packages python3 tools/export_android_unity_objects.py \\",
        "  --workspace .. --materialization-manifest recovery/unity-split-materialization.json \\",
        "  --extraction-index recovery/safe-apk-extraction-index.json \\",
        "  --json recovery/android-unity-object-exports.json \\",
        "  --markdown recovery/ANDROID_UNITY_OBJECT_EXPORTS_2026-08-04.md",
        "```", "",
    ]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--materialization-manifest", type=Path, required=True)
    parser.add_argument("--extraction-index", type=Path, required=True)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--markdown", type=Path, required=True)
    args = parser.parse_args()
    workspace = args.workspace.resolve()
    materialized = json.loads(args.materialization_manifest.read_text(encoding="utf-8"))
    extraction = json.loads(args.extraction_index.read_text(encoding="utf-8"))
    extraction_root = (workspace / str(extraction["output"])).resolve()
    destinations = {str(item["archive"]): (workspace / str(item["destination"])).resolve() for item in extraction["archives"]}
    grouped: dict[str, list[Path]] = defaultdict(list)
    for item in materialized["files"]:
        if item["status"] not in {"materialized", "already_materialized"}:
            continue
        if str(item["split_base"]).lower().endswith((".resource", ".ress")):
            continue
        grouped[str(item["archive"])].append(extraction_root / str(item["output"]))
    packages = []
    for archive, assets in grouped.items():
        package_root = destinations[archive]
        source_data = package_root / "assets/bin/Data"
        resources = sorted(path for path in source_data.glob("*.resource") if path.is_file())
        materialized_resources = sorted(
            path for path in (package_root / "_materialized-unity-data").glob("*.resource") if path.is_file()
        )
        resources = sorted({path.resolve(): path for path in resources + materialized_resources}.values())
        packages.append(export_package(archive, package_root, sorted(assets), resources))
    report: dict[str, object] = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "unitypy_version": __import__("UnityPy").__version__, "packages": packages,
    }
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.markdown.write_text(markdown(report), encoding="utf-8")
    print(json.dumps({
        "packages": len(packages), "objects": sum(p["objects"] for p in packages),
        "failed": sum(sum(r["status"] == "failed" for r in p["records"]) for p in packages),
        "raw_fallback": sum(sum(r["status"] == "raw_fallback" for r in p["records"]) for p in packages),
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
