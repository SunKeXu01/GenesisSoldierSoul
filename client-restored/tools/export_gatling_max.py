#!/usr/bin/env python3
"""Convert the recovered Genesis Soldier Soul Gatling MAX scene to FBX.

Run with Blender and the open-source ``io_scene_max`` checkout:

    blender --background --python tools/export_gatling_max.py -- \
      --importer /path/to/io_scene_max \
      --source /path/to/加特林机枪2.max \
      --output Assets/RecoveredWeapons/Gatling/Gatling.fbx

The source uses solid 3ds Max materials rather than bitmap maps.  The script
preserves all four material groups and creates an explicit barrel pivot for the
six original barrel meshes and their three retaining rings.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector


BARREL_PARTS = {
    "Circle62",
    "Circle73",
    "Circle74",
    "Circle75",
    "Circle76",
    "Circle77",
    "Circle78",
    "Circle79",
    "Circle80",
    "Circle81",
}
MATERIAL_NAMES = {
    "03 - Default": "GatlingSteel",
    "04 - Default": "GatlingBody",
    "06 - Default": "GatlingAccent",
    "Bronze": "GatlingBronze",
}
MATERIAL_COLORS = {
    "GatlingSteel": (0.34, 0.37, 0.39, 1.0),
    "GatlingBody": (0.055, 0.065, 0.075, 1.0),
    "GatlingAccent": (0.18, 0.2, 0.22, 1.0),
    "GatlingBronze": (0.32, 0.13, 0.035, 1.0),
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--importer", required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--report")
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    return parser.parse_args(argv)


def parent_preserving_world(child: bpy.types.Object, parent: bpy.types.Object) -> None:
    world = child.matrix_world.copy()
    child.parent = parent
    child.matrix_world = world


def main() -> None:
    args = parse_args()
    importer_root = Path(args.importer).resolve()
    sys.path.insert(0, str(importer_root))
    import source as io_scene_max

    io_scene_max.register()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    result = bpy.ops.import_scene.max(
        filepath=str(Path(args.source).resolve()),
        scale_objects=1.0,
        use_image_search=True,
        object_filter={"MATERIAL", "UV", "PRIMITIVE", "EMPTY", "ARMATURE"},
        use_collection=False,
        use_apply_matrix=True,
        axis_forward="Y",
        axis_up="Z",
    )
    if "FINISHED" not in result:
        raise RuntimeError(f"MAX import failed: {sorted(result)}")

    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(meshes) != 72:
        raise RuntimeError(f"expected 72 Gatling meshes, imported {len(meshes)}")

    for material in bpy.data.materials:
        renamed = MATERIAL_NAMES.get(material.name)
        if not renamed:
            continue
        material.name = renamed
        material.diffuse_color = MATERIAL_COLORS[renamed]
        material.metallic = 0.68 if renamed != "GatlingBronze" else 0.58
        material.roughness = 0.27
        material.use_nodes = True
        shader = material.node_tree.nodes.get("Principled BSDF")
        if shader:
            shader.inputs["Base Color"].default_value = MATERIAL_COLORS[renamed]
            shader.inputs["Metallic"].default_value = material.metallic
            shader.inputs["Roughness"].default_value = material.roughness

    root = bpy.data.objects.new("Recovered_Gatling_Candidate", None)
    bpy.context.scene.collection.objects.link(root)
    # The original scene uses centimetre-like modelling units.  A 0.025 scale
    # yields a one-metre firearm while preserving every source object transform.
    root.scale = Vector((0.025, 0.025, 0.025))

    barrel = bpy.data.objects.new("GatlingBarrelAssembly", None)
    bpy.context.scene.collection.objects.link(barrel)
    barrel.location = Vector((-6.36, -5.13, 3.21))
    parent_preserving_world(barrel, root)

    for obj in meshes:
        parent_preserving_world(obj, barrel if obj.name in BARREL_PARTS else root)

    muzzle = bpy.data.objects.new("Muzzle", None)
    bpy.context.scene.collection.objects.link(muzzle)
    muzzle.location = Vector((-6.36, -7.0, 3.21))
    parent_preserving_world(muzzle, barrel)

    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for child in root.children_recursive:
        child.select_set(True)
    bpy.context.view_layer.objects.active = root

    output = Path(args.output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(output),
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        apply_unit_scale=True,
        bake_space_transform=False,
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="AUTO",
    )

    points = [obj.matrix_world @ Vector(corner) for obj in meshes
              for corner in obj.bound_box]
    lower = [min(point[index] for point in points) for index in range(3)]
    upper = [max(point[index] for point in points) for index in range(3)]
    report = {
        "source": str(Path(args.source).resolve()),
        "output": str(output),
        "meshCount": len(meshes),
        "vertexCount": sum(len(obj.data.vertices) for obj in meshes),
        "polygonCount": sum(len(obj.data.polygons) for obj in meshes),
        "barrelParts": sorted(BARREL_PARTS),
        "materialMode": "source solid-color groups; no bitmap maps",
        "materials": sorted(MATERIAL_COLORS),
        "bounds": {"min": lower, "max": upper},
    }
    report_path = Path(args.report).resolve() if args.report else output.with_suffix(
        ".json"
    )
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2))
    print("GATLING_EXPORT", json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()
