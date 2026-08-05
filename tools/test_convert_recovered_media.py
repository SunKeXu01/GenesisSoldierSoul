import tempfile
import unittest
from pathlib import Path

from convert_recovered_media import dependency_files, destination_for, safe_name, write_verified


class RecoveredMediaTests(unittest.TestCase):
    def test_obj_dependencies(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "mesh.obj").write_text("mtllib mesh.mtl\n")
            (root / "mesh.mtl").write_text("map_Kd diffuse.png\n")
            (root / "diffuse.png").write_bytes(b"png")
            self.assertEqual(
                dependency_files(root / "mesh.obj"),
                [root / "diffuse.png", root / "mesh.mtl"],
            )

    def test_destination_has_source_hash(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "source" / "mesh.obj"
            source.parent.mkdir()
            source.write_bytes(b"mesh")
            destination = destination_for(source, root, root / "out", "models", ".glb")
            self.assertIn("d30ca7a7a32b", str(destination))

    def test_write_is_idempotent_and_rejects_collision(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "output.bin"
            self.assertEqual(write_verified(path, b"same"), "converted")
            self.assertEqual(write_verified(path, b"same"), "verified_existing")
            with self.assertRaises(ValueError):
                write_verified(path, b"different")


if __name__ == "__main__":
    unittest.main()
