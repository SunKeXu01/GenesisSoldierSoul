import io
import struct
import unittest

from PIL import Image

from convert_private_rez_media import decode_raw_texture, identify_texture


class PrivateRezMediaTests(unittest.TestCase):
    def test_decodes_exact_24_bit_bgr_texture(self):
        header = bytearray(44)
        header[:4] = b"\x00\x00\x02\x00"
        struct.pack_into("<HH", header, 12, 2, 1)
        header[16] = 24
        png, details = decode_raw_texture(bytes(header) + bytes([0, 0, 255, 0, 255, 0]))
        with Image.open(io.BytesIO(png)) as image:
            self.assertEqual(image.getpixel((0, 0)), (255, 0, 0))
            self.assertEqual(image.getpixel((1, 0)), (0, 255, 0))
        self.assertEqual(details["header_bytes"], 44)

    def test_identifies_exact_private_texture_and_rejects_extra_bytes(self):
        header = bytearray(44)
        header[:4] = b"\x00\x00\x02\x00"
        struct.pack_into("<HH", header, 12, 4, 4)
        header[16] = 32
        self.assertEqual(identify_texture(bytes(header), 44 + 64), "raw_texture")
        self.assertIsNone(identify_texture(bytes(header), 45 + 64))


if __name__ == "__main__":
    unittest.main()
