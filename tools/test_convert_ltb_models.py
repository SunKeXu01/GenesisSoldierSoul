import json
import hashlib
import struct
import tempfile
import unittest
from pathlib import Path

from convert_ltb_models import (
    LtbError,
    convert_one,
    make_glb,
    parse_crossfire_composite_ltb,
    parse_ltb,
    validate_glb,
)


def minimal_ltb() -> bytes:
    data = bytearray(98)
    struct.pack_into("<HH", data, 0, 1, 9)
    struct.pack_into("<H", data, 84, 0)
    struct.pack_into("<I", data, 94, 1)
    name = b"Triangle"
    data.extend(struct.pack("<H", len(name)) + name)
    base = len(data)
    data.extend(bytes(83))
    struct.pack_into("<H", data, base + 49, 3)
    struct.pack_into("<H", data, base + 53, 1)
    struct.pack_into("<H", data, base + 57, 1)
    struct.pack_into("<H", data, base + 61, 1)
    for position, uv in (
        ((0.0, 0.0, 0.0), (0.0, 0.0)),
        ((1.0, 0.0, 0.0), (1.0, 0.0)),
        ((0.0, 1.0, 0.0), (0.0, 1.0)),
    ):
        data.extend(struct.pack("<3f3f2f", *position, 0.0, 0.0, 1.0, *uv))
    data.extend(struct.pack("<3H", 0, 1, 2))
    data.extend(struct.pack("<H", 0))
    return bytes(data)


def zero_x_padded_ltb() -> bytes:
    data = bytearray(minimal_ltb())
    name_length = struct.unpack_from("<H", data, 98)[0]
    base = 100 + name_length
    data[base + 83 : base + 83] = b"\x00\x00"
    return bytes(data)


def mismatched_skinned_ltb() -> bytes:
    data = bytearray(98)
    struct.pack_into("<HH", data, 0, 1, 9)
    struct.pack_into("<H", data, 84, 0)
    struct.pack_into("<I", data, 94, 1)
    name = b"SkinnedTriangle"
    data.extend(struct.pack("<H", len(name)) + name)
    base = len(data)
    data.extend(bytes(83))
    struct.pack_into("<H", data, base + 49, 3)
    struct.pack_into("<H", data, base + 53, 1)
    struct.pack_into("<H", data, base + 57, 4)
    struct.pack_into("<H", data, base + 61, 2)
    for position, uv in (
        ((0.0, 0.0, 0.0), (0.0, 0.0)),
        ((1.0, 0.0, 0.0), (1.0, 0.0)),
        ((0.0, 1.0, 0.0), (0.0, 1.0)),
    ):
        data.extend(
            struct.pack(
                "<3f3f3f2f",
                *position,
                1.0,
                0.0,
                0.0,
                0.0,
                0.0,
                1.0,
                *uv,
            )
        )
    data.extend(struct.pack("<3H", 0, 1, 2))
    data.extend(struct.pack("<I", 0))
    data.extend(b"\x00")
    return bytes(data)


def composite_ltb(
    render_object_type: int = 4,
    with_obb: bool = False,
    nonfinite_normal_vertex: int | None = None,
    direct_skeletal: bool = False,
    with_animation: bool = False,
) -> bytes:
    data = bytearray(94)
    struct.pack_into("<HH", data, 0, 1, 9)
    struct.pack_into("<I", data, 32, 1)
    struct.pack_into("<H", data, 84, 0)
    struct.pack_into("<f", data, 86, 10.0)
    struct.pack_into("<I", data, 90, int(with_obb))
    if with_obb:
        data.extend(
            struct.pack(
                "<15fIf",
                0.0,
                0.0,
                0.0,
                1.0,
                1.0,
                1.0,
                1.0,
                0.0,
                0.0,
                0.0,
                1.0,
                0.0,
                0.0,
                0.0,
                1.0,
                0,
                1.0,
            )
        )
    data.extend(struct.pack("<I", 1))
    name = b"Composite"
    data.extend(struct.pack("<H", len(name)) + name)
    data.extend(struct.pack("<I", 1))
    data.extend(struct.pack("<I", 0) + bytes(8))
    data.extend(struct.pack("<I4IIBI", 1, 0, 0, 0, 0, 0, 0, render_object_type))
    payload = bytearray()
    if render_object_type == 4:
        payload.extend(struct.pack("<4I4II", 3, 1, 1, 1, 0x13, 0, 0, 0, 0))
    elif render_object_type == 5:
        payload.extend(
            struct.pack("<4I", 3, 1, 1 if direct_skeletal else 6, 1 if direct_skeletal else 4)
        )
        payload.extend(b"\x00" if direct_skeletal else b"\x01")
        payload.extend(struct.pack("<4I", 0x13, 0, 0, 0))
        payload.extend(b"\x00" if direct_skeletal else b"\x01")
        if not direct_skeletal:
            payload.extend(struct.pack("<3I", 0, 0, 1))
            payload.extend(struct.pack("<I", 0))
    elif render_object_type == 6:
        payload.extend(
            struct.pack("<5I4I2I", 3, 3, 1, 1, 1, 0x13, 0, 0, 0, 0, 0)
        )
    else:
        raise ValueError(render_object_type)
    for vertex_index, (position, uv) in enumerate((
        ((0.0, 0.0, 0.0), (0.0, 0.0)),
        ((1.0, 0.0, 0.0), (1.0, 0.0)),
        ((0.0, 1.0, 0.0), (0.0, 1.0)),
    )):
        normal = (
            (float("nan"), 0.0, 1.0)
            if vertex_index == nonfinite_normal_vertex
            else (0.0, 0.0, 1.0)
        )
        if render_object_type == 5 and not direct_skeletal:
            payload.extend(
                struct.pack(
                    "<3f3f4B3f2f",
                    *position,
                    1.0,
                    0.0,
                    0.0,
                    0,
                    0,
                    0,
                    0,
                    *normal,
                    *uv,
                )
            )
        else:
            payload.extend(
                struct.pack("<3f3f2f", *position, *normal, *uv)
            )
    payload.extend(struct.pack("<3H", 0, 1, 2))
    if render_object_type == 5 and direct_skeletal:
        payload.extend(struct.pack("<IHH4BI", 1, 0, 3, 0, 0xFF, 0xFF, 0xFF, 3))
    if render_object_type == 6:
        payload.extend(struct.pack("<I", 0))
    data.extend(struct.pack("<I", len(payload)) + payload)
    data.extend(b"\x01\x00")
    bone_name = b"root"
    data.extend(struct.pack("<H", len(bone_name)) + bone_name + bytes(3))
    data.extend(
        struct.pack(
            "<16fI",
            1.0,
            0.0,
            0.0,
            0.0,
            0.0,
            1.0,
            0.0,
            0.0,
            0.0,
            0.0,
            1.0,
            0.0,
            0.0,
            0.0,
            0.0,
            1.0,
            0,
        )
    )
    if not with_animation:
        data.extend(struct.pack("<IIIII", 0, 1, 0, 0, 0))
        return bytes(data)
    animation_name = b"Move"
    data.extend(struct.pack("<III", 0, 1, 1))
    data.extend(struct.pack("<3fH", 1.0, 1.0, 1.0, len(animation_name)))
    data.extend(animation_name)
    data.extend(struct.pack("<III", 0, 200, 2))
    data.extend(struct.pack("<IH", 0, 0))
    data.extend(struct.pack("<IH", 1000, 0))
    if render_object_type == 6:
        data.extend(b"\x01")
        for z_offset in (0.0, 1.0):
            data.extend(struct.pack("<I", 3))
            for position in (
                (0.0, 0.0, z_offset),
                (1.0, 0.0, z_offset),
                (0.0, 1.0, z_offset),
            ):
                data.extend(struct.pack("<3f", *position))
    else:
        data.extend(b"\x00")
        data.extend(struct.pack("<6f", 0.0, 0.0, 0.0, 1.0, 0.0, 0.0))
        data.extend(
            struct.pack(
                "<8f",
                0.0,
                0.0,
                0.0,
                1.0,
                0.0,
                0.0,
                2**-0.5,
                2**-0.5,
            )
        )
    data.extend(struct.pack("<I", 0))
    data.extend(struct.pack("<IH", 1, len(animation_name)))
    data.extend(animation_name)
    data.extend(struct.pack("<6f", 1.0, 1.0, 1.0, 2.0, 0.0, 0.0))
    return bytes(data)


class LtbModelTests(unittest.TestCase):
    def test_parses_bounded_mesh_and_writes_valid_glb(self):
        meshes, details = parse_ltb(minimal_ltb())
        self.assertEqual(details["triangle_count"], 1)
        glb = make_glb(meshes, {"test": True})
        validation = validate_glb(glb)
        self.assertEqual(validation["meshes"], 1)
        json_size = struct.unpack_from("<I", glb, 12)[0]
        document = json.loads(glb[20 : 20 + json_size].decode().rstrip(" "))
        self.assertEqual(document["asset"]["version"], "2.0")

    def test_rejects_out_of_range_triangle_index(self):
        data = bytearray(minimal_ltb())
        struct.pack_into("<H", data, len(data) - 8, 9)
        with self.assertRaisesRegex(LtbError, "out-of-range"):
            parse_ltb(bytes(data))

    def test_resolves_two_byte_geometry_padding_when_x_starts_at_zero(self):
        meshes, details = parse_ltb(zero_x_padded_ltb())
        self.assertEqual(details["triangle_count"], 1)
        self.assertEqual(max(meshes[0].indices), 2)

    def test_uses_validated_head_layout_for_mismatched_type_fields(self):
        meshes, details = parse_ltb(mismatched_skinned_ltb())
        self.assertEqual(details["mesh_type_counts"], {"4": 1})
        self.assertEqual(details["triangle_count"], 1)
        self.assertEqual(meshes[0].mesh_type, 4)

    def test_layout_v7_preserves_different_prior_output(self):
        source = minimal_ltb()
        digest = hashlib.sha256(source).hexdigest()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            input_path = root / "source.ltb"
            legacy_path = root / "legacy.glb"
            output_path = root / "layout-v7.glb"
            input_path.write_bytes(source)
            legacy_path.write_bytes(b"legacy-must-remain")
            result = convert_one(
                {
                    "input_path": str(input_path),
                    "input_bytes": len(source),
                    "input_sha256": digest,
                    "source_archive": "RF000.REZ",
                    "source_archive_sha256": "a" * 64,
                    "stream_index": 7,
                    "input_label": "source.ltb",
                    "output_path": str(output_path),
                    "output_relative": "layout-v7.glb",
                    "legacy_output_path": str(legacy_path),
                }
            )
            self.assertEqual(legacy_path.read_bytes(), b"legacy-must-remain")
            self.assertEqual(result["status"], "converted_preserving_different_legacy")
            self.assertEqual(result["output"]["sha256"], hashlib.sha256(output_path.read_bytes()).hexdigest())

    def test_parses_composite_submesh_and_skeleton_metadata(self):
        data = composite_ltb()
        meshes, details = parse_crossfire_composite_ltb(data, 0, 1, 98)
        self.assertEqual(len(meshes), 1)
        self.assertEqual(details["layout"], "crossfire_top_mesh_submesh")
        self.assertEqual(details["triangle_count"], 1)
        self.assertEqual(details["render_object_type_counts"], {"4": 1})
        self.assertEqual(details["skeleton_metadata"]["names"], ["root"])
        self.assertEqual(details["skeleton_metadata"]["parent_indices"], [-1])

    def test_parses_reindexed_matrix_palette_geometry(self):
        meshes, details = parse_crossfire_composite_ltb(
            composite_ltb(5), 0, 1, 98
        )
        self.assertEqual(len(meshes), 1)
        self.assertEqual(details["render_object_type_counts"], {"5": 1})
        self.assertEqual(details["matrix_palette_submeshes"], 1)
        self.assertEqual(details["reindexed_bone_entries"], 1)
        self.assertEqual(details["vertex_stream_layout_counts"], {"0x13/48": 1})
        self.assertEqual(meshes[0].joints[0], (0, 0, 0, 0))
        self.assertEqual(meshes[0].weights[0], (1.0, 0.0, 0.0, 0.0))

        glb = make_glb(meshes, {"test": True}, details["skeleton_metadata"])
        self.assertEqual(validate_glb(glb)["skins"], 1)
        json_size = struct.unpack_from("<I", glb, 12)[0]
        document = json.loads(glb[20 : 20 + json_size].decode().rstrip(" "))
        attributes = document["meshes"][0]["primitives"][0]["attributes"]
        self.assertIn("JOINTS_0", attributes)
        self.assertIn("WEIGHTS_0", attributes)
        self.assertEqual(document["nodes"][-1]["skin"], 0)

    def test_parses_direct_bone_set_skin_mapping(self):
        meshes, details = parse_crossfire_composite_ltb(
            composite_ltb(5, direct_skeletal=True), 0, 1, 98
        )
        self.assertEqual(details["skinned_mesh_count"], 1)
        self.assertEqual(meshes[0].joints, [(0, 0, 0, 0)] * 3)
        self.assertEqual(meshes[0].weights, [(1.0, 0.0, 0.0, 0.0)] * 3)

    def test_parses_vertex_animated_geometry(self):
        meshes, details = parse_crossfire_composite_ltb(
            composite_ltb(6), 0, 1, 98
        )
        self.assertEqual(len(meshes), 1)
        self.assertEqual(details["render_object_type_counts"], {"6": 1})
        self.assertEqual(details["vertex_animation_submeshes"], 1)

    def test_exports_skeletal_animation_channels_and_root_binding(self):
        meshes, details = parse_crossfire_composite_ltb(
            composite_ltb(5, direct_skeletal=True, with_animation=True),
            0,
            1,
            98,
        )
        self.assertEqual(details["animation_count"], 1)
        self.assertEqual(details["animation_keyframes"], 2)
        animation = details["_animations"][0]
        self.assertEqual(animation.root_translation, (2.0, 0.0, 0.0))
        glb = make_glb(
            meshes,
            {"test": True},
            details["skeleton_metadata"],
            details["_animations"],
        )
        validation = validate_glb(glb)
        self.assertEqual(validation["animations"], 1)
        self.assertEqual(validation["animation_channels"], 2)
        json_size = struct.unpack_from("<I", glb, 12)[0]
        document = json.loads(glb[20 : 20 + json_size].decode().rstrip(" "))
        self.assertNotIn("matrix", document["nodes"][0])
        self.assertEqual(document["animations"][0]["name"], "Move")

    def test_exports_vertex_animation_as_morph_targets_and_weights(self):
        meshes, details = parse_crossfire_composite_ltb(
            composite_ltb(6, with_animation=True), 0, 1, 98
        )
        glb = make_glb(
            meshes,
            {"test": True},
            details["skeleton_metadata"],
            details["_animations"],
        )
        validation = validate_glb(glb)
        self.assertEqual(validation["animations"], 1)
        self.assertEqual(validation["morph_targets"], 2)
        self.assertEqual(validation["animation_channels"], 3)
        json_size = struct.unpack_from("<I", glb, 12)[0]
        document = json.loads(glb[20 : 20 + json_size].decode().rstrip(" "))
        primitive = document["meshes"][0]["primitives"][0]
        self.assertEqual(len(primitive["targets"]), 2)
        self.assertTrue(
            any(
                channel["target"]["path"] == "weights"
                for channel in document["animations"][0]["channels"]
            )
        )

    def test_repairs_referenced_nonfinite_normal_from_triangle_geometry(self):
        meshes, details = parse_crossfire_composite_ltb(
            composite_ltb(4, nonfinite_normal_vertex=0), 0, 1, 98
        )
        self.assertEqual(details["repaired_normal_vertices"], 1)
        self.assertEqual(meshes[0].normals[0], (0.0, 0.0, 1.0))

    def test_normalizes_finite_vertex_normals_for_gltf(self):
        data = bytearray(composite_ltb(4))
        vertex_data = data.find(struct.pack("<3f3f2f", 0.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0))
        self.assertGreater(vertex_data, 0)
        struct.pack_into("<3f", data, vertex_data + 12, 0.0, 0.0, 2.0)
        meshes, _ = parse_crossfire_composite_ltb(bytes(data), 0, 1, 98)
        self.assertEqual(meshes[0].normals[0], (0.0, 0.0, 1.0))

    def test_skips_valid_oriented_bounding_boxes_before_geometry(self):
        meshes, details = parse_ltb(composite_ltb(4, with_obb=True))
        self.assertEqual(len(meshes), 1)
        self.assertEqual(details["oriented_bounding_box_count"], 1)


if __name__ == "__main__":
    unittest.main()
