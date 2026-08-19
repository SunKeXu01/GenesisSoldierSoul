#!/usr/bin/env python3
"""Convert a recovered GLB into a Unity-compatible FBX with Blender.

Run with:
    blender --background --python tools/glb_to_fbx.py -- input.glb output.fbx
"""

from __future__ import annotations

import sys
from pathlib import Path

import bpy


def arguments() -> tuple[Path, Path]:
    if "--" not in sys.argv:
        raise SystemExit("expected -- input.glb output.fbx")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 2:
        raise SystemExit("expected exactly two arguments: input.glb output.fbx")
    return Path(values[0]).resolve(), Path(values[1]).resolve()


def main() -> None:
    source, destination = arguments()
    if not source.is_file():
        raise SystemExit(f"missing GLB input: {source}")

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source))
    for item in tuple(bpy.data.objects):
        if item.type in {"CAMERA", "LIGHT"}:
            bpy.data.objects.remove(item, do_unlink=True)

    destination.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(destination),
        use_selection=False,
        apply_unit_scale=True,
        bake_space_transform=False,
        object_types={"EMPTY", "MESH", "ARMATURE"},
        path_mode="COPY",
        embed_textures=True,
        add_leaf_bones=False,
        bake_anim=False,
    )


if __name__ == "__main__":
    main()
