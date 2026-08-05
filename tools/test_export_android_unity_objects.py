#!/usr/bin/env python3
"""Safety and determinism tests for Android Unity object exports."""

from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("export_android_unity_objects.py")
SPEC = importlib.util.spec_from_file_location("export_android_unity_objects", MODULE_PATH)
assert SPEC and SPEC.loader
exporter = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(exporter)


class AndroidUnityExportTests(unittest.TestCase):
    def test_safe_name_removes_path_and_control_characters(self) -> None:
        self.assertEqual(exporter.safe_name("../Gun:Main\x00", "fallback"), "Gun_Main")
        self.assertEqual(exporter.safe_name("...", "fallback"), "fallback")

    def test_verified_output_is_idempotent_and_rejects_collision(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "nested" / "asset.bin"
            self.assertEqual(exporter.write_verified(path, b"payload"), "exported")
            self.assertEqual(exporter.write_verified(path, b"payload"), "verified_existing")
            with self.assertRaises(ValueError):
                exporter.write_verified(path, b"different")

    def test_extension_is_appended_without_losing_path_id(self) -> None:
        base = Path("sharedassets0.assets__42__Gun")
        self.assertEqual(
            exporter.append_extension(base, ".json").name,
            "sharedassets0.assets__42__Gun.json",
        )


if __name__ == "__main__":
    unittest.main()
