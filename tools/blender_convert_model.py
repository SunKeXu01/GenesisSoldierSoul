"""Blender-side model import and deterministic GLB export helper."""

from __future__ import annotations

import json
import sys
from pathlib import Path

import bpy


def main() -> None:
    separator = sys.argv.index("--")
    source = Path(sys.argv[separator + 1]).resolve()
    output = Path(sys.argv[separator + 2]).resolve()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    suffix = source.suffix.lower()
    if suffix == ".obj":
        result = bpy.ops.wm.obj_import(filepath=str(source), forward_axis="NEGATIVE_Z", up_axis="Y")
    elif suffix == ".fbx":
        result = bpy.ops.import_scene.fbx(filepath=str(source), use_image_search=True)
    elif suffix == ".3ds":
        result = bpy.ops.import_scene.autodesk_3ds(filepath=str(source))
    else:
        raise ValueError(f"unsupported model format: {suffix}")
    if "FINISHED" not in result:
        raise RuntimeError(f"model import did not finish: {result}")
    meshes = [item for item in bpy.data.objects if item.type == "MESH"]
    if not meshes:
        raise RuntimeError("model import produced no mesh objects")
    output.parent.mkdir(parents=True, exist_ok=True)
    result = bpy.ops.export_scene.gltf(
        filepath=str(output),
        export_format="GLB",
        export_apply=True,
        export_materials="EXPORT",
        export_yup=True,
    )
    if "FINISHED" not in result or not output.is_file():
        raise RuntimeError(f"GLB export did not finish: {result}")
    print(
        "GENESIS_MODEL_RESULT="
        + json.dumps(
            {
                "objects": len(bpy.data.objects),
                "meshes": len(meshes),
                "vertices": sum(len(item.data.vertices) for item in meshes),
                "polygons": sum(len(item.data.polygons) for item in meshes),
                "materials": len(bpy.data.materials),
                "images": len(bpy.data.images),
            },
            sort_keys=True,
        )
    )


if __name__ == "__main__":
    main()
