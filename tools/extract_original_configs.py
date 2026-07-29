#!/usr/bin/env python3
"""Extract and inventory the recovered original Genesis Soldier Soul resources."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import zlib
from collections import Counter
from pathlib import Path
from xml.etree import ElementTree


LOOSE_ASSET_EXTENSIONS = {
    ".png",
    ".jpg",
    ".jpeg",
    ".gif",
    ".mp3",
    ".swf",
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def extract_dfdt(source: Path, destination: Path) -> dict[str, object]:
    raw = source.read_bytes()
    record: dict[str, object] = {
        "name": source.name,
        "size": len(raw),
        "sha256": hashlib.sha256(raw).hexdigest(),
    }

    try:
        decoded = zlib.decompress(raw)
    except zlib.error:
        record["format"] = "amf-or-binary"
        return record

    stripped = decoded.lstrip(b"\xef\xbb\xbf \t\r\n")
    if stripped.startswith(b"<"):
        suffix = ".xml"
        decoded_format = "zlib-xml"
    elif stripped.startswith((b"{", b"[")):
        suffix = ".json"
        decoded_format = "zlib-json"
    else:
        suffix = ".bin"
        decoded_format = "zlib-binary"
    output = destination / f"{source.stem}{suffix}"
    output.write_bytes(decoded)
    record.update(
        {
            "format": decoded_format,
            "decodedSize": len(decoded),
            "output": output.name,
        }
    )
    return record


def extract_maps(
    room_config: Path, localized_strings: dict[str, str]
) -> list[dict[str, object]]:
    xml = ElementTree.parse(room_config)
    maps: list[dict[str, object]] = []
    for node in xml.findall(".//maps/map"):
        models = node.find("models")
        skybox = node.find("skybox")
        model_names = (models.get("values", "") if models is not None else "").split()
        maps.append(
            {
                "id": int(node.get("id", "0")),
                "key": node.get("key", ""),
                "displayName": localized_strings.get(
                    node.get("key", ""), node.get("key", "")
                ),
                "size": int(node.get("size", "0")),
                "height": int(node.get("height", "0")),
                "skybox": skybox.get("key") if skybox is not None else None,
                "models": model_names,
            }
        )
    return maps


def extract_weapons(
    props_config: Path, localized_strings: dict[str, str]
) -> list[dict[str, object]]:
    xml = ElementTree.parse(props_config)
    combat_types = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 19}
    weapons = []
    for node in xml.findall(".//cdy"):
        if "id" not in node.attrib or "name" not in node.attrib:
            continue
        category = int(node.get("type", "0"))
        stats = node.find("shuxing")
        if category not in combat_types or stats is None:
            continue
        key = node.get("name", "")
        weapons.append(
            {
                "id": int(node.get("id", "0")),
                "key": key,
                "displayName": localized_strings.get(key, key),
                "model": node.get("mc"),
                "category": category,
                "metadata": dict(node.attrib),
                "stats": dict(stats.attrib),
                "coefficients": dict(node.find("xishu").attrib)
                if node.find("xishu") is not None
                else {},
                "crosshair": dict(node.find("zhunxin").attrib)
                if node.find("zhunxin") is not None
                else {},
                "animation": dict(node.find("anim").attrib)
                if node.find("anim") is not None
                else {},
                "sound": dict(node.find("sound").attrib)
                if node.find("sound") is not None
                else {},
            }
        )
    return weapons


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path, help="Flattened original resource directory")
    parser.add_argument("output", type=Path, help="Generated extraction directory")
    args = parser.parse_args()

    source = args.source.resolve()
    output = args.output.resolve()
    configs = output / "configs"
    loose_assets = output / "loose-assets"
    configs.mkdir(parents=True, exist_ok=True)
    loose_assets.mkdir(parents=True, exist_ok=True)

    config_records = [
        extract_dfdt(path, configs)
        for path in sorted(source.glob("*.dfdt"), key=lambda item: item.name.lower())
    ]

    asset_records: list[dict[str, object]] = []
    extension_counts: Counter[str] = Counter()
    for path in sorted(source.iterdir(), key=lambda item: item.name.lower()):
        extension = path.suffix.lower()
        if not path.is_file() or extension not in LOOSE_ASSET_EXTENSIONS:
            continue
        extension_counts[extension] += 1
        asset_records.append(
            {
                "name": path.name,
                "extension": extension,
                "size": path.stat().st_size,
                "sha256": sha256(path),
            }
        )

    localization_path = configs / "res_zh.111031.json"
    localized_strings = (
        json.loads(localization_path.read_text(encoding="utf-8-sig"))
        if localization_path.exists()
        else {}
    )
    room_config = configs / "roomConfig.xml"
    maps = (
        extract_maps(room_config, localized_strings) if room_config.exists() else []
    )
    (output / "maps.json").write_text(
        json.dumps(maps, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    props_config = configs / "props.xml"
    weapons = (
        extract_weapons(props_config, localized_strings)
        if props_config.exists()
        else []
    )
    (output / "weapons.json").write_text(
        json.dumps(weapons, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    manifest = {
        "source": str(source),
        "configCount": len(config_records),
        "assetCount": len(asset_records),
        "mapCount": len(maps),
        "weaponCount": len(weapons),
        "assetCountsByExtension": dict(sorted(extension_counts.items())),
        "configs": config_records,
        "assets": asset_records,
    }
    (output / "manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    # Keep the original compressed configuration payloads byte-for-byte.
    compressed_configs = output / "original-dfdt"
    compressed_configs.mkdir(exist_ok=True)
    for path in source.glob("*.dfdt"):
        shutil.copy2(path, compressed_configs / path.name)

    print(
        f"Extracted {len(config_records)} configs, inventoried "
        f"{len(asset_records)} assets, found {len(maps)} maps and "
        f"{len(weapons)} weapons."
    )


if __name__ == "__main__":
    main()
