#!/usr/bin/env python3
"""Tests for the copy-on-write original sample vault."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("seal_original_samples.py")
SPEC = importlib.util.spec_from_file_location("seal_original_samples", MODULE_PATH)
assert SPEC and SPEC.loader
seal = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(seal)


class OriginalSampleVaultTests(unittest.TestCase):
    def test_rejects_unsafe_relative_paths(self) -> None:
        for value in ("../escape.zip", "/absolute.zip", "folder/../escape.zip", ""):
            with self.subTest(value=value), self.assertRaises(ValueError):
                seal.safe_relative(value)

    def test_clones_verifies_and_reuses_read_only_snapshot(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            source = workspace / "sample.zip"
            source.write_bytes(b"immutable source sample")
            digest = hashlib.sha256(source.read_bytes()).hexdigest()
            original_mode = source.stat().st_mode
            inventory = workspace / "inventory.json"
            inventory.write_text(json.dumps({
                "inventory_sha256": "inventory-digest",
                "samples": [{
                    "path": "sample.zip", "kind": "file",
                    "provenance_class": "workspace_original",
                    "bytes": source.stat().st_size, "sha256": digest,
                }],
            }), encoding="utf-8")
            command = [
                sys.executable, str(MODULE_PATH), "--workspace", str(workspace),
                "--inventory", str(inventory),
            ]
            first = subprocess.run(command, check=True, capture_output=True, text=True)
            second = subprocess.run(command, check=True, capture_output=True, text=True)
            self.assertIn('"samples": 1', first.stdout)
            self.assertIn('"samples": 1', second.stdout)
            clone = workspace / seal.VAULT_NAME / "files" / "sample.zip"
            manifest = json.loads((workspace / seal.VAULT_NAME / "manifest.json").read_text())
            self.assertEqual(clone.read_bytes(), source.read_bytes())
            self.assertEqual(clone.stat().st_mode & 0o777, 0o444)
            self.assertEqual(source.stat().st_mode, original_mode)
            self.assertEqual(manifest["files"][0]["status"], "verified_existing_clone")
            seal.make_directories_writable(workspace / seal.VAULT_NAME)


if __name__ == "__main__":
    unittest.main()
