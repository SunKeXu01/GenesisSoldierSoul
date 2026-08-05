#!/usr/bin/env python3
"""Regression tests for the read-only full-workspace sample inventory."""

from __future__ import annotations

import importlib.util
import os
import shutil
import struct
import tempfile
import unittest
import zipfile
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("audit_root_resources.py")
SPEC = importlib.util.spec_from_file_location("audit_root_resources", MODULE_PATH)
assert SPEC and SPEC.loader
audit = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audit)


class RootResourceInventoryTests(unittest.TestCase):
    def test_excludes_generated_recovery_outputs_from_original_samples(self):
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            generated = workspace / "GenesisSoldierSoul" / "recovery" / "special-formats"
            generated.mkdir(parents=True)
            (generated / "derived.atf").write_bytes(b"ATF-derived")
            original = workspace / "原始素材"
            original.mkdir()
            (original / "source.atf").write_bytes(b"ATF-original")
            records, _, _ = audit.build_sample_inventory(workspace)
            paths = {record["path"] for record in records}
            self.assertIn("原始素材/source.atf", paths)
            self.assertNotIn("GenesisSoldierSoul/recovery/special-formats/derived.atf", paths)

    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.workspace = Path(self.temporary.name)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def test_detects_pe_apk_unity_data_and_exact_duplicates(self) -> None:
        pe = bytearray(256)
        pe[:2] = b"MZ"
        struct.pack_into("<I", pe, 0x3C, 0x80)
        pe[0x80:0x84] = b"PE\0\0"
        struct.pack_into("<H", pe, 0x84, 0x8664)
        (self.workspace / "client.exe").write_bytes(pe)

        apk = self.workspace / "game.apk.1"
        with zipfile.ZipFile(apk, "w") as archive:
            archive.writestr("lib/arm64-v8a/libunity.so", b"unity")
            archive.writestr("lib/arm64-v8a/libil2cpp.so", b"il2cpp")
            archive.writestr("assets/bin/Data/globalgamemanagers", b"header 2019.4.40f1 tail")

        first = self.workspace / "first.zip"
        with zipfile.ZipFile(first, "w") as archive:
            archive.writestr("asset.txt", b"same")
        shutil.copyfile(first, self.workspace / "second.zip")

        data = self.workspace / "Release_Data"
        data.mkdir()
        (data / "globalgamemanagers").write_bytes(b"Unity 2020.3.48f1")
        (data / "sharedassets0.assets").write_bytes(b"asset payload")
        (data / "sharedassets0.resource").write_bytes(b"resource payload")
        (self.workspace / "damaged-name.bin").write_bytes(b"UnityFS\0bundle payload")

        records, duplicates, digest = audit.build_sample_inventory(self.workspace)
        by_path = {item["path"]: item for item in records}

        self.assertEqual(by_path["client.exe"]["architectures"], ["x86_64"])
        self.assertEqual(by_path["game.apk.1"]["sample_type"], "android_apk_split_or_renamed")
        self.assertEqual(by_path["game.apk.1"]["version"], "2019.4.40f1")
        self.assertEqual(by_path["game.apk.1"]["architectures"], ["arm64-v8a"])
        self.assertEqual(by_path["game.apk.1"]["engine"], "Unity IL2CPP")
        self.assertEqual(by_path["Release_Data"]["version"], "2020.3.48f1")
        self.assertEqual(by_path["Release_Data"]["files"], 3)
        self.assertEqual(
            by_path["Release_Data/sharedassets0.assets"]["sample_type"],
            "unity_serialized_file",
        )
        self.assertEqual(
            by_path["Release_Data/sharedassets0.resource"]["sample_type"],
            "unity_resource_stream",
        )
        self.assertEqual(by_path["damaged-name.bin"]["sample_type"], "unity_asset_bundle")
        self.assertIn(["first.zip", "second.zip"], duplicates)
        self.assertEqual(len(digest), 64)

    def test_inventory_is_deterministic_and_does_not_modify_sources(self) -> None:
        source = self.workspace / "sample.rez"
        source.write_bytes(b"REZ sample bytes")
        os.chmod(source, 0o444)
        before = (source.read_bytes(), source.stat().st_mode, source.stat().st_mtime_ns)

        first_records, first_duplicates, first_digest = audit.build_sample_inventory(self.workspace)
        second_records, second_duplicates, second_digest = audit.build_sample_inventory(self.workspace)
        after = (source.read_bytes(), source.stat().st_mode, source.stat().st_mtime_ns)

        self.assertEqual(before, after)
        self.assertEqual(first_records, second_records)
        self.assertEqual(first_duplicates, second_duplicates)
        self.assertEqual(first_digest, second_digest)
        self.assertFalse(first_records[0]["owner_write_bit"])
        self.assertEqual(first_records[0]["scan_policy"], "read_only_static_scan")


if __name__ == "__main__":
    unittest.main()
