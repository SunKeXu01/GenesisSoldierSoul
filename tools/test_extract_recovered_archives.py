#!/usr/bin/env python3
"""Regression tests for recovered archive staging and UnityPackage rebuilding."""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from extract_recovered_archives import (
    materialize_unitypackage,
    unsafe_member,
    validate_tree,
)
from materialize_unity_split_assets import materialize, validate_sequence


class RecoveredArchiveTests(unittest.TestCase):
    def test_rejects_archive_path_escape_forms(self) -> None:
        self.assertEqual(unsafe_member("../escape.txt"), "parent_traversal")
        self.assertEqual(unsafe_member("/tmp/escape.txt"), "absolute_path")
        self.assertEqual(unsafe_member("C:/escape.txt"), "drive_path")
        self.assertIsNone(unsafe_member("Assets/Weapons/M4A1.prefab"))

    def test_materializes_unity_asset_and_meta(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            entry = root / "0123456789abcdef"
            entry.mkdir()
            (entry / "pathname").write_text(
                "Assets/Weapons/M4A1.prefab\n", encoding="utf-8"
            )
            (entry / "asset").write_text("prefab-data", encoding="utf-8")
            (entry / "asset.meta").write_text("guid: test", encoding="utf-8")

            summary, error = materialize_unitypackage(root)

            self.assertIsNone(error)
            self.assertEqual(summary["assets"], 1)
            self.assertEqual(summary["categories"], {"unity": 1})
            target = root / "_materialized/Assets/Weapons/M4A1.prefab"
            self.assertEqual(target.read_text(encoding="utf-8"), "prefab-data")
            self.assertEqual(
                Path(str(target) + ".meta").read_text(encoding="utf-8"),
                "guid: test",
            )
            manifest = json.loads(
                (root / "_materialized/.genesis-unitypackage.json")
                .read_text(encoding="utf-8")
            )
            self.assertEqual(manifest["paths"], ["Assets/Weapons/M4A1.prefab"])

    def test_rejects_materialized_path_outside_assets(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            entry = root / "0123456789abcdef"
            entry.mkdir()
            (entry / "pathname").write_text(
                "Packages/foreign.asset\n", encoding="utf-8"
            )
            (entry / "asset").write_text("data", encoding="utf-8")

            summary, error = materialize_unitypackage(root)

            self.assertEqual(summary, {})
            self.assertEqual(error, "unity_path_outside_assets:Packages/foreign.asset")
            self.assertFalse((root / "_materialized").exists())

    def test_materializes_complete_unity_split_sequence(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            first = root / "sharedassets0.assets.split0"
            second = root / "sharedassets0.assets.split1"
            first.write_bytes(b"first-")
            second.write_bytes(b"second")
            output = root / "materialized/sharedassets0.assets"

            result = materialize([(1, second), (0, first)], output)

            self.assertEqual(result["status"], "materialized")
            self.assertEqual(output.read_bytes(), b"first-second")

    def test_rejects_gapped_unity_split_sequence(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            zero = root / "data.assets.split0"
            two = root / "data.assets.split2"
            zero.write_bytes(b"zero")
            two.write_bytes(b"two")

            self.assertEqual(
                validate_sequence([(0, zero), (2, two)]),
                "missing_chunks:1",
            )

    def test_rejects_symbolic_links_in_extracted_tree(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            outside = root.parent / "genesis-symlink-target.txt"
            outside.write_text("outside", encoding="utf-8")
            link = root / "escaped-link"
            try:
                link.symlink_to(outside)
                self.assertEqual(
                    validate_tree(root),
                    "symbolic_link:escaped-link",
                )
            finally:
                link.unlink(missing_ok=True)
                outside.unlink(missing_ok=True)

    def test_split_materialization_is_idempotent(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            zero = root / "data.assets.split0"
            one = root / "data.assets.split1"
            zero.write_bytes(b"stable-")
            one.write_bytes(b"payload")
            output = root / "materialized/data.assets"

            first = materialize([(0, zero), (1, one)], output)
            first_bytes = output.read_bytes()
            second = materialize([(0, zero), (1, one)], output)

            self.assertEqual(first["status"], "materialized")
            self.assertEqual(second["status"], "already_materialized")
            self.assertEqual(first["sha256"], second["sha256"])
            self.assertEqual(output.read_bytes(), first_bytes)

    def test_unitypackage_materialization_reuses_verified_manifest(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            entry = root / "0123456789abcdef"
            entry.mkdir()
            (entry / "pathname").write_text(
                "Assets/Weapons/Stable.prefab\n", encoding="utf-8"
            )
            (entry / "asset").write_text("stable", encoding="utf-8")

            first, first_error = materialize_unitypackage(root)
            second, second_error = materialize_unitypackage(root)

            self.assertIsNone(first_error)
            self.assertIsNone(second_error)
            self.assertEqual(first, second)
            self.assertEqual(
                (root / "_materialized/Assets/Weapons/Stable.prefab")
                .read_text(encoding="utf-8"),
                "stable",
            )


if __name__ == "__main__":
    unittest.main()
