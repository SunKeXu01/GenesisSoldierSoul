import json
import struct
import unittest

from convert_ltb_models import LtbError, make_glb, parse_ltb, validate_glb


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


if __name__ == "__main__":
    unittest.main()
