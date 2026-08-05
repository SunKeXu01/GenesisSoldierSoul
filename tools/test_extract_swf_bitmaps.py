import struct
import tempfile
import unittest
import zlib
from pathlib import Path

from extract_swf_bitmaps import extract, first_tag_offset, iter_tags, read_uncompressed_swf


def tag(code: int, body: bytes) -> bytes:
    if len(body) < 63:
        return struct.pack("<H", (code << 6) | len(body)) + body
    return struct.pack("<HI", (code << 6) | 63, len(body)) + body


def minimal_swf(signature: bytes = b"FWS") -> bytes:
    # RECT with Nbits=1, frame rate 1, one frame, vector + ShowFrame + End.
    body = b"\x08\x00" + b"\x00\x01" + b"\x01\x00"
    body += tag(2, b"\x07\x00vector") + tag(1, b"") + tag(0, b"")
    raw = b"FWS\x0a" + struct.pack("<I", 8 + len(body)) + body
    if signature == b"CWS":
        return b"CWS" + raw[3:8] + zlib.compress(raw[8:])
    return raw


class SwfExtractionTests(unittest.TestCase):
    def test_reads_fws_and_cws(self):
        with tempfile.TemporaryDirectory() as tmp:
            for signature in (b"FWS", b"CWS"):
                path = Path(tmp) / f"{signature.decode()}.swf"
                path.write_bytes(minimal_swf(signature))
                raw, details = read_uncompressed_swf(path)
                self.assertEqual(raw[:3], b"FWS")
                self.assertIn(details["compression"].split("/")[0], {"FWS", "CWS"})
                self.assertEqual(first_tag_offset(raw), 14)
                self.assertEqual([code for code, _ in iter_tags(raw)], [2, 1, 0])

    def test_extracts_vector_with_hash_isolation(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = root / "vector.swf"
            path.write_bytes(minimal_swf())
            manifest = extract(path, root / "out", root)
            self.assertEqual(manifest["status"], "parsed")
            self.assertEqual(manifest["resource_count"], 1)
            output = root / "out" / manifest["resources"][0]["outputs"][0]["path"]
            self.assertTrue(output.is_file())

    def test_preserves_mislabeled_zlib_payload(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path = root / "fontdata.swf"
            path.write_bytes(zlib.compress(b"not a swf font payload"))
            manifest = extract(path, root / "out", root)
            self.assertEqual(manifest["status"], "non_swf_zlib_payload_preserved")
            self.assertEqual(len(manifest["outputs"]), 1)


if __name__ == "__main__":
    unittest.main()
