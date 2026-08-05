import struct
import tempfile
import unittest
from pathlib import Path

from legacy_3ds import ThreeDSError, load_3ds, write_obj


def chunk(chunk_id: int, body: bytes) -> bytes:
    return struct.pack("<HI", chunk_id, len(body) + 6) + body


def triangle_3ds() -> bytes:
    vertices = struct.pack("<H", 3) + struct.pack("<fffffffff", 0, 0, 0, 1, 0, 0, 0, 1, 0)
    faces = struct.pack("<H", 1) + struct.pack("<HHHH", 0, 1, 2, 0)
    uv = struct.pack("<H", 3) + struct.pack("<ffffff", 0, 0, 1, 0, 0, 1)
    mesh = chunk(0x4100, chunk(0x4110, vertices) + chunk(0x4120, faces) + chunk(0x4140, uv))
    obj = chunk(0x4000, b"Triangle\0" + mesh)
    return chunk(0x4D4D, chunk(0x3D3D, obj))


class Legacy3dsTests(unittest.TestCase):
    def test_parses_and_writes_triangle(self):
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / "triangle.3ds"
            output = Path(tmp) / "triangle.obj"
            source.write_bytes(triangle_3ds())
            meshes = load_3ds(source)
            self.assertEqual((len(meshes), len(meshes[0].vertices), len(meshes[0].faces)), (1, 3, 1))
            stats = write_obj(meshes, output)
            self.assertEqual(stats, {"meshes": 1, "vertices": 3, "faces": 1})
            self.assertIn("f 1/1 2/2 3/3", output.read_text())

    def test_rejects_bad_face_index(self):
        data = triangle_3ds().replace(struct.pack("<HHHH", 0, 1, 2, 0), struct.pack("<HHHH", 0, 1, 9, 0))
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / "bad.3ds"
            source.write_bytes(data)
            with self.assertRaises(ThreeDSError):
                load_3ds(source)


if __name__ == "__main__":
    unittest.main()
