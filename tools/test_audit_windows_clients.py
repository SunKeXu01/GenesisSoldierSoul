import hashlib
import struct
import unittest

from audit_windows_clients import UNITY_VERSION_RE, interesting_strings, parse_pe_bytes


def minimal_pe32() -> bytes:
    data = bytearray(0x400)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 0x3C, 0x80)
    data[0x80:0x84] = b"PE\0\0"
    struct.pack_into("<HHIIIHH", data, 0x84, 0x14C, 1, 0, 0, 0, 0xE0, 0x0102)
    optional = 0x98
    struct.pack_into("<H", data, optional, 0x10B)
    struct.pack_into("<I", data, optional + 16, 0x1000)
    struct.pack_into("<I", data, optional + 28, 0x400000)
    struct.pack_into("<HH", data, optional + 68, 2, 0x0140)
    struct.pack_into("<I", data, optional + 92, 16)
    section = optional + 0xE0
    struct.pack_into("<8sIIIIIIHHI", data, section, b".text\0\0\0", 0x100, 0x1000, 0x200, 0x200, 0, 0, 0, 0, 0x60000020)
    data[0x200:0x204] = b"TEST"
    return bytes(data)


class WindowsAuditTests(unittest.TestCase):
    def test_minimal_pe_header(self):
        parsed = parse_pe_bytes(minimal_pe32())
        self.assertEqual(parsed["format"], "PE32")
        self.assertEqual(parsed["architecture"], "x86")
        self.assertEqual(parsed["subsystem"], "windows_gui")
        self.assertTrue(parsed["mitigations"]["aslr"])
        self.assertTrue(parsed["mitigations"]["dep"])
        self.assertEqual(parsed["sections"][0]["sha256"], hashlib.sha256(minimal_pe32()[0x200:0x400]).hexdigest())

    def test_rejects_non_pe(self):
        with self.assertRaises(ValueError):
            parse_pe_bytes(b"not an executable")

    def test_string_categories(self):
        data = b"https://example.test/api\0Assets/Weapons/M4.prefab\0settings.json\0socket client server\0"
        result = interesting_strings(data)
        self.assertTrue(result["urls"])
        self.assertTrue(result["resource_paths"])
        self.assertTrue(result["config_paths"])
        self.assertTrue(result["network_or_engine"])

    def test_unity_china_version(self):
        self.assertEqual(UNITY_VERSION_RE.search(b"x 2018.4.14c1 y").group(0), b"2018.4.14c1")


if __name__ == "__main__":
    unittest.main()
