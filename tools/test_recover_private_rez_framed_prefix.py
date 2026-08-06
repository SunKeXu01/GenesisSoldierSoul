import hashlib
import struct
import tempfile
import unittest
import zlib
from pathlib import Path

from recover_private_rez_framed_prefix import (
    PNG_SIGNATURE,
    parse_dds,
    parse_png,
    recover_framed_prefix,
)
from rez_extract import RezError


def chunk(kind: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


def png(width: int = 1, height: int = 1) -> bytes:
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    pixels = zlib.compress(b"\0\0\0\0\0")
    return PNG_SIGNATURE + chunk(b"IHDR", ihdr) + chunk(b"IDAT", pixels) + chunk(b"IEND", b"")


def dds_dxt1(width: int = 4, height: int = 4) -> bytes:
    header = bytearray(128)
    header[:4] = b"DDS "
    struct.pack_into("<7I", header, 4, 124, 0x81007, height, width, 8, 0, 1)
    struct.pack_into("<II4s", header, 76, 32, 4, b"DXT1")
    struct.pack_into("<I", header, 108, 0x1000)
    payload_bytes = max(1, (width + 3) // 4) * max(1, (height + 3) // 4) * 8
    return bytes(header) + bytes(payload_bytes)


class PrivateRezPngPrefixTests(unittest.TestCase):
    def test_parses_crc_valid_png_at_exact_offset(self):
        data = png(3, 2)
        with tempfile.TemporaryFile() as stream:
            stream.write(data)
            parsed = parse_png(stream, 0, len(data))
        self.assertEqual((parsed["width"], parsed["height"]), (3, 2))
        self.assertEqual(parsed["bytes"], len(data))
        self.assertEqual(parsed["sha256"], hashlib.sha256(data).hexdigest())

    def test_rejects_bad_crc(self):
        data = bytearray(png())
        data[-1] ^= 1
        with tempfile.TemporaryFile() as stream:
            stream.write(data)
            with self.assertRaisesRegex(RezError, "CRC mismatch"):
                parse_png(stream, 0, len(data))

    def test_parses_header_sized_dds_at_exact_offset(self):
        data = dds_dxt1(8, 4)
        with tempfile.TemporaryFile() as stream:
            stream.write(data)
            parsed = parse_dds(stream, 0, len(data))
        self.assertEqual((parsed["width"], parsed["height"]), (8, 4))
        self.assertEqual(parsed["bytes"], len(data))
        self.assertEqual(parsed["fourcc"], "DXT1")

    def test_recovers_only_contiguous_prefix_and_preserves_suffix(self):
        first = png(1, 1)
        middle = dds_dxt1()
        second = png(2, 2)
        suffix = b"NOT-A-PNG" + PNG_SIGNATURE
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "RF199.REZ"
            data = bytearray(168)
            data.extend(first + middle + second + suffix)
            root_offset = len(data)
            data.extend(bytes(23))
            data[:2] = b"\r\n"
            data[126] = 0x1A
            struct.pack_into("<III", data, 127, 1, root_offset, 23)
            source.write_bytes(data)
            result = recover_framed_prefix(
                source,
                hashlib.sha256(data).hexdigest(),
                root_offset,
                root / "out",
            )
            outputs = [root / "out" / item["path"] for item in result["outputs"]]
            self.assertEqual(
                [item.read_bytes() for item in outputs], [first, middle, second]
            )
            self.assertEqual(result["trailing_region"]["bytes"], len(suffix))
            self.assertIn("no signature search", result["trailing_region"]["interpretation"])


if __name__ == "__main__":
    unittest.main()
