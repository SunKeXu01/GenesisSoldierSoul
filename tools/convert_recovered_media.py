#!/usr/bin/env python3
"""Convert selected recovered models, textures and audio with full provenance."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import subprocess
import tempfile
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

from PIL import Image

from legacy_3ds import load_3ds, write_obj


MODEL_SUFFIXES = {".obj", ".fbx", ".3ds"}
IMAGE_SUFFIXES = {".bmp", ".tga"}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def safe_name(value: str) -> str:
    cleaned = re.sub(r"[^0-9A-Za-z\u4e00-\u9fff._-]+", "_", value).strip("._")
    return (cleaned or "asset")[:100]


def source_record(path: Path, workspace: Path, role: str = "source") -> dict[str, Any]:
    return {
        "path": str(path.relative_to(workspace)),
        "role": role,
        "bytes": path.stat().st_size,
        "sha256": sha256_file(path),
    }


def write_verified(path: Path, data: bytes) -> str:
    digest = hashlib.sha256(data).hexdigest()
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        if not path.is_file() or path.stat().st_size != len(data) or sha256_file(path) != digest:
            raise ValueError(f"existing media output differs: {path}")
        return "verified_existing"
    path.write_bytes(data)
    if sha256_file(path) != digest:
        raise IOError(f"media output hash verification failed: {path}")
    return "converted"


def dependency_files(source: Path) -> list[Path]:
    dependencies: set[Path] = set()
    if source.suffix.lower() == ".obj":
        text = source.read_text(encoding="utf-8", errors="ignore")
        for line in text.splitlines():
            if line.lower().startswith("mtllib "):
                candidate = source.parent / line.split(None, 1)[1].strip()
                if candidate.is_file():
                    dependencies.add(candidate)
                    material = candidate.read_text(encoding="utf-8", errors="ignore")
                    for row in material.splitlines():
                        if re.match(r"(?i)^map_[a-z]+\s+", row):
                            texture = candidate.parent / row.split(None, 1)[1].strip()
                            if texture.is_file():
                                dependencies.add(texture)
    for sibling in source.parent.iterdir():
        if sibling.is_file() and sibling.suffix.lower() in {".png", ".jpg", ".jpeg", ".bmp", ".tga"}:
            dependencies.add(sibling)
    return sorted(dependencies)


def destination_for(source: Path, workspace: Path, output: Path, category: str, suffix: str) -> Path:
    digest = sha256_file(source)
    relative_name = safe_name("__".join(source.relative_to(workspace).parts))
    return output / category / f"{relative_name}__{digest[:12]}" / f"{safe_name(source.stem)}{suffix}"


def convert_model(
    source: Path,
    workspace: Path,
    output: Path,
    blender: Path,
    blender_script: Path,
) -> dict[str, Any]:
    destination = destination_for(source, workspace, output, "models", ".glb")
    record: dict[str, Any] = {
        "kind": "model_to_glb",
        "inputs": [source_record(source, workspace)]
        + [source_record(path, workspace, "dependency") for path in dependency_files(source)],
        "outputs": [],
        "parameters": {"format": "GLB", "export_apply": True, "export_yup": True},
    }
    try:
        if destination.is_file():
            data = destination.read_bytes()
            status = "verified_existing"
            details = None
        else:
            with tempfile.TemporaryDirectory(prefix="genesis-model-") as temp:
                staged = Path(temp) / "converted.glb"
                import_source = source
                legacy_details = None
                if source.suffix.lower() == ".3ds":
                    import_source = Path(temp) / "legacy-3ds.obj"
                    legacy_details = write_obj(load_3ds(source), import_source)
                process = subprocess.run(
                    [
                        str(blender),
                        "--background",
                        "--factory-startup",
                        "--python",
                        str(blender_script),
                        "--",
                        str(import_source),
                        str(staged),
                    ],
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    check=False,
                    timeout=300,
                    text=True,
                    errors="replace",
                )
                if process.returncode != 0 or not staged.is_file():
                    raise RuntimeError(f"Blender exit {process.returncode}: {process.stdout[-1000:]}")
                match = re.search(r"GENESIS_MODEL_RESULT=(\{.*\})", process.stdout)
                details = json.loads(match.group(1)) if match else None
                if legacy_details:
                    details = {"legacy_3ds": legacy_details, "blender": details}
                data = staged.read_bytes()
                status = write_verified(destination, data)
        record["outputs"].append(
            {
                "path": str(destination.relative_to(output)),
                "bytes": len(data),
                "sha256": hashlib.sha256(data).hexdigest(),
                "format": "glb",
                "status": status,
            }
        )
        record["conversion_details"] = details
        record["status"] = "converted"
    except Exception as error:
        record["status"] = "failed"
        record["error"] = f"{type(error).__name__}: {error}"
    return record


def convert_image(source: Path, workspace: Path, output: Path) -> dict[str, Any]:
    destination = destination_for(source, workspace, output, "textures", ".png")
    record: dict[str, Any] = {
        "kind": "texture_to_png",
        "inputs": [source_record(source, workspace)],
        "outputs": [],
        "parameters": {"format": "PNG", "color_mode": "RGBA"},
    }
    try:
        with Image.open(source) as image:
            converted = image.convert("RGBA")
            stream = io.BytesIO()
            converted.save(stream, format="PNG")
            width, height = converted.size
        data = stream.getvalue()
        status = write_verified(destination, data)
        record["outputs"].append(
            {
                "path": str(destination.relative_to(output)),
                "bytes": len(data),
                "sha256": hashlib.sha256(data).hexdigest(),
                "format": "png",
                "status": status,
            }
        )
        record.update({"status": "converted", "width": width, "height": height})
    except Exception as error:
        record["status"] = "failed"
        record["error"] = f"{type(error).__name__}: {error}"
    return record


def convert_audio(source: Path, workspace: Path, output: Path, afconvert: Path) -> dict[str, Any]:
    destination = destination_for(source, workspace, output, "audio", ".wav")
    record: dict[str, Any] = {
        "kind": "audio_to_wav",
        "inputs": [source_record(source, workspace)],
        "outputs": [],
        "parameters": {"container": "WAVE", "codec": "LEI16"},
    }
    try:
        if destination.is_file():
            data = destination.read_bytes()
            status = "verified_existing"
        else:
            with tempfile.TemporaryDirectory(prefix="genesis-audio-") as temp:
                staged = Path(temp) / "converted.wav"
                process = subprocess.run(
                    [str(afconvert), "-f", "WAVE", "-d", "LEI16", str(source), str(staged)],
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    check=False,
                    timeout=120,
                    text=True,
                    errors="replace",
                )
                if process.returncode != 0 or not staged.is_file():
                    raise RuntimeError(f"afconvert exit {process.returncode}: {process.stdout[-800:]}")
                data = staged.read_bytes()
                status = write_verified(destination, data)
        if not data.startswith(b"RIFF") or data[8:12] != b"WAVE":
            raise ValueError("afconvert output is not RIFF/WAVE")
        record["outputs"].append(
            {
                "path": str(destination.relative_to(output)),
                "bytes": len(data),
                "sha256": hashlib.sha256(data).hexdigest(),
                "format": "wav_pcm16",
                "status": status,
            }
        )
        record["status"] = "converted"
    except Exception as error:
        record["status"] = "failed"
        record["error"] = f"{type(error).__name__}: {error}"
    return record


def unique_paths(paths: Iterable[Path]) -> list[Path]:
    return sorted({path.resolve() for path in paths if path.is_file()})


def markdown(report: dict[str, Any]) -> str:
    summary = report["summary"]
    return "\n".join(
        [
            "# 恢复媒体标准格式转换报告",
            "",
            "原文件全部保留。模型通过固定 Blender 版本转为 GLB，BMP/TGA 转为 PNG，选定 MP3 转为 PCM WAV；",
            "每条记录保存工具、参数、输入依赖哈希、输出哈希和失败原因。",
            "",
            f"- 模型：{summary['model_to_glb']}（失败 {summary['model_to_glb_failed']}）",
            f"- 贴图：{summary['texture_to_png']}（失败 {summary['texture_to_png_failed']}）",
            f"- 音频：{summary['audio_to_wav']}（失败 {summary['audio_to_wav_failed']}）",
            f"- 输出字节：{summary['output_bytes']}",
            "",
        ]
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--blender", type=Path, required=True)
    parser.add_argument("--afconvert", type=Path, required=True)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--markdown", type=Path, required=True)
    args = parser.parse_args()

    workspace = args.workspace.resolve()
    output = args.output.resolve()
    extracted = workspace / "_解压资源"
    models = unique_paths(
        path for path in extracted.rglob("*") if path.suffix.lower() in MODEL_SUFFIXES
    )
    images = unique_paths(
        path
        for root in (workspace / "_解压资源", workspace / "_安全解压资源")
        for path in root.rglob("*")
        if path.suffix.lower() in IMAGE_SUFFIXES
        and ("创世兵魂武器" in str(path) or path.name == "ScreenSelector.bmp")
    )
    audio = unique_paths(
        path
        for root in (workspace / "_解压资源" / "创世兵魂素材", workspace / "CF2.0")
        for path in root.rglob("*.mp3")
    )

    blender_version = subprocess.run(
        [str(args.blender), "--version"], capture_output=True, text=True, errors="replace", check=False
    ).stdout.splitlines()[0]
    records = [
        convert_model(path, workspace, output, args.blender.resolve(), Path(__file__).with_name("blender_convert_model.py"))
        for path in models
    ]
    records += [convert_image(path, workspace, output) for path in images]
    records += [convert_audio(path, workspace, output, args.afconvert.resolve()) for path in audio]
    counts = Counter(record["kind"] for record in records if record["status"] == "converted")
    failures = Counter(record["kind"] for record in records if record["status"] == "failed")
    summary = {
        "model_to_glb": counts["model_to_glb"],
        "model_to_glb_failed": failures["model_to_glb"],
        "texture_to_png": counts["texture_to_png"],
        "texture_to_png_failed": failures["texture_to_png"],
        "audio_to_wav": counts["audio_to_wav"],
        "audio_to_wav_failed": failures["audio_to_wav"],
        "output_bytes": sum(output_item["bytes"] for record in records for output_item in record["outputs"]),
    }
    report = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "tool": "tools/convert_recovered_media.py",
        "tool_version": "1",
        "toolchain": {
            "blender": blender_version,
            "afconvert": str(args.afconvert.resolve()),
            "pillow": Image.__version__ if hasattr(Image, "__version__") else __import__("PIL").__version__,
        },
        "parameters": {
            "model_roots": ["_解压资源"],
            "legacy_3ds_intermediate": "bounded 3DS triangle mesh parser to OBJ",
            "audio_roots": ["_解压资源/创世兵魂素材", "CF2.0"],
            "output": str(output),
        },
        "summary": summary,
        "records": records,
    }
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.markdown.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.markdown.write_text(markdown(report), encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False))
    return 1 if sum(failures.values()) else 0


if __name__ == "__main__":
    raise SystemExit(main())
