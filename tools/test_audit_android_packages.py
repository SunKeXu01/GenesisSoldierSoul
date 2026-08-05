#!/usr/bin/env python3
"""Parser tests for Android static auditing."""

from __future__ import annotations

import importlib.util
import struct
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("audit_android_packages.py")
SPEC = importlib.util.spec_from_file_location("audit_android_packages", MODULE_PATH)
assert SPEC and SPEC.loader
audit = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audit)


class AndroidAuditTests(unittest.TestCase):
    def test_manifest_components_permissions_and_network_policy(self) -> None:
        xml = '''<manifest xmlns:android="http://schemas.android.com/apk/res/android"
          package="com.example.game" android:versionCode="7" android:versionName="1.2">
          <uses-sdk android:minSdkVersion="21" android:targetSdkVersion="29" />
          <uses-permission android:name="android.permission.INTERNET" />
          <application android:debuggable="false" android:usesCleartextTraffic="true"
             android:networkSecurityConfig="@xml/network_security_config">
            <activity android:name=".Main" android:exported="true"><intent-filter>
              <action android:name="android.intent.action.MAIN" />
              <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter></activity>
            <service android:name=".Sync" android:exported="false" />
            <provider android:name=".Files" android:authorities="com.example.files" />
          </application>
        </manifest>'''
        manifest = audit.parse_manifest(xml)
        self.assertEqual(manifest["package"], "com.example.game")
        self.assertEqual(manifest["permissions"], ["android.permission.INTERNET"])
        self.assertEqual(manifest["components"]["activity"][0]["actions"], ["android.intent.action.MAIN"])
        self.assertEqual(manifest["components"]["service"][0]["exported"], "false")
        self.assertEqual(manifest["application"]["uses_cleartext_traffic"], "true")

    def test_dex_header_and_package_summary(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "classes.dex"
            header = bytearray(112)
            header[:8] = b"dex\n035\0"
            for offset, value in ((32, 112), (56, 10), (64, 9), (72, 8), (80, 7), (88, 6), (96, 5)):
                struct.pack_into("<I", header, offset, value)
            path.write_bytes(header)
            record = audit.parse_dex_header(path)
            self.assertEqual(record["dex_version"], "035")
            self.assertEqual(record["classes"], 5)
        summary = audit.parse_dex_package_summary(
            "P d 10 20 300 <TOTAL>\nP d 8 9 200 com.example\nC d 1 1 20 com.example.Main\n"
        )
        self.assertEqual(summary["total"]["defined"], 10)
        self.assertEqual(summary["top_packages"][0]["name"], "com.example")


if __name__ == "__main__":
    unittest.main()
