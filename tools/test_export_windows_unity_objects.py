import unittest
from pathlib import Path

from export_windows_unity_objects import output_slug, select_inputs


class WindowsUnityExportTests(unittest.TestCase):
    def test_hash_isolates_output(self):
        player = {
            "data_dir": "_解压资源/示例/Test_Data",
            "source_directory_sha256": "abcdef1234567890",
        }
        self.assertEqual(output_slug(player), "Test__abcdef123456")

    def test_selects_serialized_and_streams(self):
        player = {
            "data_dir": "source/Test_Data",
            "key_files": [
                {"path": "level0", "kind": "scene_serialized_file"},
                {"path": "sharedassets0.resource", "kind": "resource_stream"},
            ],
        }
        serialized, resources = select_inputs(player, Path("/workspace"))
        self.assertEqual(serialized, [Path("/workspace/source/Test_Data/level0")])
        self.assertEqual(resources, [Path("/workspace/source/Test_Data/sharedassets0.resource")])


if __name__ == "__main__":
    unittest.main()
