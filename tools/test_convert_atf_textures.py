import io
import lzma
import struct
import unittest

import imagecodecs
import numpy
from PIL import Image

from convert_atf_textures import (
    ATF_COMPRESSED,
    ATF_COMPRESSED_ALPHA,
    AtfError,
    dds_to_png,
    make_dds,
    parse_atf,
)


def atf_lzma(data: bytes) -> bytes:
    encoded = lzma.compress(data, format=lzma.FORMAT_ALONE)
    return encoded[:5] + encoded[13:]


def make_atf(texture_format: int) -> bytes:
    red_green = numpy.array([[[255, 0, 0]], [[0, 255, 0]]], dtype=numpy.uint8)
    if texture_format == ATF_COMPRESSED:
        records = [
            atf_lzma(b"\x00\x00\x00\x00"),
            imagecodecs.jpegxr_encode(red_green),
        ] + [b""] * 6
    else:
        alpha = numpy.array([[255], [0]], dtype=numpy.uint8)
        records = [
            atf_lzma(b"\x00" * 6),
            imagecodecs.jpegxr_encode(alpha),
            atf_lzma(b"\x00" * 4),
            imagecodecs.jpegxr_encode(red_green),
        ] + [b""] * 6
    body = bytes((texture_format, 2, 2, 1))
    for record in records:
        body += len(record).to_bytes(3, "big") + record
    return b"ATF" + len(body).to_bytes(3, "big") + body


class ConvertAtfTexturesTests(unittest.TestCase):
    def test_reconstructs_dxt1_and_png(self):
        texture = parse_atf(make_atf(ATF_COMPRESSED))
        dds, mip_count = make_dds(texture)
        self.assertEqual(dds[:4], b"DDS ")
        self.assertEqual(dds[84:88], b"DXT1")
        self.assertEqual(mip_count, 1)
        png = dds_to_png(dds, 4, 4)
        with Image.open(io.BytesIO(png)) as image:
            self.assertEqual(image.size, (4, 4))
            self.assertEqual(image.mode, "RGBA")

    def test_reconstructs_dxt5_alpha(self):
        texture = parse_atf(make_atf(ATF_COMPRESSED_ALPHA))
        dds, mip_count = make_dds(texture)
        self.assertEqual(dds[84:88], b"DXT5")
        self.assertEqual(mip_count, 1)
        with Image.open(io.BytesIO(dds)) as image:
            image.load()
            self.assertEqual(image.size, (4, 4))

    def test_rejects_declared_length_mismatch(self):
        data = bytearray(make_atf(ATF_COMPRESSED))
        data[5] ^= 1
        with self.assertRaisesRegex(AtfError, "length mismatch"):
            parse_atf(bytes(data))

    def test_rejects_non_contiguous_mips(self):
        data = bytearray(make_atf(ATF_COMPRESSED))
        data[9] = 3
        empty_level = b"\x00\x00\x00" * 8
        populated = make_atf(ATF_COMPRESSED)[10:]
        data.extend(empty_level + populated)
        data[3:6] = (len(data) - 6).to_bytes(3, "big")
        with self.assertRaisesRegex(AtfError, "non-contiguous"):
            make_dds(parse_atf(bytes(data)))


if __name__ == "__main__":
    unittest.main()
