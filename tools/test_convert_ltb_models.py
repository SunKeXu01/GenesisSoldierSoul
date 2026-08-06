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


def composite_ltb() -> bytes:
    data = bytearray(98)
    struct.pack_into("<HH", data, 0, 1, 9)
    struct.pack_into("<I", data, 32, 1)
    struct.pack_into("<H", data, 84, 0)
    struct.pack_into("<I", data, 94, 1)
    name = b"Composite"
    data.extend(struct.pack("<H", len(name)) + name)
    data.extend(struct.pack("<I", 1))
    data.extend(struct.pack("<I", 0) + bytes(8))
    data.extend(bytes(4) + struct.pack("<I", 0) + bytes(17))
    data.extend(struct.pack("<II", 0, 1))
    data.extend(struct.pack("<III", 3, 1, 1) + bytes(20))
    for position, uv in (
        ((0.0, 0.0, 0.0), (0.0, 0.0)),
        ((1.0, 0.0, 0.0), (1.0, 0.0)),
        ((0.0, 1.0, 0.0), (0.0, 1.0)),
    ):
        data.extend(struct.pack("<3f3f2f", *position, 0.0, 0.0, 1.0, *uv))
    data.extend(struct.pack("<3H", 0, 1, 2) + b"\0")
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

    def test_layout_v2_preserves_different_legacy_output(self):
        source = minimal_ltb()
        digest = hashlib.sha256(source).hexdigest()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            input_path = root / "source.ltb"
            legacy_path = root / "legacy.glb"
            output_path = root / "layout-v2.glb"
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
                    "output_relative": "layout-v2.glb",
                    "legacy_output_path": str(legacy_path),
                }
            )
            self.assertEqual(legacy_path.read_bytes(), b"legacy-must-remain")
            self.assertEqual(result["status"], "converted_preserving_different_legacy")
            self.assertEqual(result["output"]["sha256"], hashlib.sha256(output_path.read_bytes()).hexdigest())

    def test_parses_composite_submesh_and_skeleton_metadata(self):
        data = composite_ltb()
        meshes, details = parse_crossfire_composite_ltb(data, 0, 1)
        self.assertEqual(len(meshes), 1)
        self.assertEqual(details["layout"], "crossfire_top_mesh_submesh")
        self.assertEqual(details["triangle_count"], 1)
        self.assertEqual(details["skeleton_metadata"]["names"], ["root"])
        self.assertEqual(details["skeleton_metadata"]["parent_indices"], [-1])


if __name__ == "__main__":
    unittest.main()
