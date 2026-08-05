import hashlib
import lzma
import struct
import tempfile
import unittest
from pathlib import Path

from recover_private_rez import (
    DATA_OFFSET,
    LooseIndex,
    classify_payload,
    parse_rez_header,
    recover_lzma_region,
)


def private_rez(data_region: bytes, directory: bytes = b"private") -> bytes:
    prefix = bytearray(127)
    prefix[:2] = b"\r\n"
    prefix[126] = 0x1A
    root_offset = DATA_OFFSET + len(data_region)
    header = struct.pack("<III", 1, root_offset, len(directory)) + bytes(29)
    return bytes(prefix) + header + data_region + directory


def lzma_stream(data: bytes) -> bytes:
    encoded = bytearray(lzma.compress(
        data,
        format=lzma.FORMAT_ALONE,
        filters=[
            {
                "id": lzma.FILTER_LZMA1,
                "dict_size": 16 * 1024 * 1024,
                "lc": 3,
                "lp": 0,
                "pb": 2,
            }
        ],
    ))
    struct.pack_into("<Q", encoded, 5, len(data))
    return bytes(encoded)


class PrivateRezRecoveryTests(unittest.TestCase):
    def test_recovers_concatenated_lzma_streams_and_reuses_loose_match(self):
        first = b"\x89PNG\r\n\x1a\n" + b"pixels"
        second = b"unmatched text\n"
        region = lzma_stream(first) + lzma_stream(second)
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "RF001.REZ"
            source.write_bytes(private_rez(region))
            loose = root / "known.png"
            loose.write_bytes(first)
            header = parse_rez_header(source)
            records, outputs, trailing = recover_lzma_region(
                source,
                hashlib.sha256(source.read_bytes()).hexdigest(),
                header["root_offset"],
                root / "out",
                LooseIndex([loose], root),
            )
        self.assertEqual(len(records), 2)
        self.assertIsNone(trailing)
        self.assertEqual(records[0]["status"], "verified_existing_loose_match")
        self.assertEqual(records[0]["classification"], "png")
        self.assertEqual(records[1]["classification"], "txt")
        self.assertEqual(len(outputs), 1)
        self.assertEqual(outputs[0]["bytes"], len(second))

    def test_rejects_corrupt_declared_size(self):
        encoded = bytearray(lzma_stream(b"payload"))
        struct.pack_into("<Q", encoded, 5, 999)
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "bad.REZ"
            source.write_bytes(private_rez(bytes(encoded)))
            with self.assertRaisesRegex(
                Exception, "invalid LZMA|declared-size mismatch|truncated LZMA"
            ):
                recover_lzma_region(
                    source,
                    hashlib.sha256(source.read_bytes()).hexdigest(),
                    parse_rez_header(source)["root_offset"],
                    root / "out",
                    LooseIndex([], root),
                )

    def test_classifies_standard_media_signatures(self):
        self.assertEqual(classify_payload(b"OggS" + bytes(32)), "ogg")
        self.assertEqual(classify_payload(b"OTTO" + bytes(32)), "otf")

    def test_preserves_trailing_private_directory_region(self):
        payload = b"resource"
        region = lzma_stream(payload) + b"encrypted-directory"
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "mixed.REZ"
            source.write_bytes(private_rez(region))
            records, outputs, trailing = recover_lzma_region(
                source,
                hashlib.sha256(source.read_bytes()).hexdigest(),
                parse_rez_header(source)["root_offset"],
                root / "out",
                LooseIndex([], root),
            )
        self.assertEqual(len(records), 1)
        self.assertEqual(len(outputs), 1)
        self.assertEqual(trailing["bytes"], len(b"encrypted-directory"))


if __name__ == "__main__":
    unittest.main()
