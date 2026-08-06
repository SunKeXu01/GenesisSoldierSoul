import hashlib
import struct
import tempfile
import unittest
import zlib
from io import BytesIO
from pathlib import Path

from PIL import Image

from recover_private_rez_framed_prefix import (
    PNG_SIGNATURE,
    parse_cfb,
    parse_ascii_web_bundle,
    parse_ini,
    parse_start_end_config,
    parse_dds,
    parse_dtx,
    parse_gif,
    parse_jpeg,
    parse_lithtech_world,
    parse_mp4,
    parse_png,
    parse_tga,
    parse_webm,
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


def tga(width: int = 2, height: int = 1, *, rle: bool = False) -> bytes:
    header = bytearray(18)
    header[2] = 10 if rle else 2
    struct.pack_into("<HH", header, 12, width, height)
    header[16] = 24
    pixels = bytes((0, 0, 255)) * (width * height)
    if rle:
        pixels = bytes((0x80 | (width * height - 1), 0, 0, 255))
    footer = bytes(8) + b"TRUEVISION-XFILE.\x00"
    return bytes(header) + pixels + footer


def dtx(width: int = 2, height: int = 1) -> bytes:
    header = bytearray(164)
    struct.pack_into("<iiHHHH", header, 0, 0, -5, width, height, 1, 0)
    header[26] = 3
    return bytes(header) + bytes((0, 0, 255, 255)) * (width * height)


def image_bytes(format_name: str) -> bytes:
    output = BytesIO()
    Image.new("RGB", (2, 1), (255, 0, 0)).save(output, format=format_name)
    return output.getvalue()


def config() -> bytes:
    return b"<start>\r\nFile=test.dtx\r\nStartTime=1\r\n<end>\r\n\r\n"


def ini() -> bytes:
    return b"[Window]\r\nState=Normal\r\n[View]\r\nMode=LargeIcon\r\n"


def web_bundle() -> bytes:
    return (
        b"/*! jQuery v1.11.1 | (c) jQuery Foundation | jquery.org/license */\n"
        + b"jQuery=function(){};\n"
        + b"/* bundled CSS */ body { display: block; }\n"
    )


def overlay_bundle() -> bytes:
    return (
        b"//.overlay{ transition: all 0.3s; }\n"
        + b".overlay { background-color: black; }\n"
        + b"@media screen { .overlay { display: block; } }\n"
    )


def cfb() -> bytes:
    header = bytearray(512)
    header[:8] = b"\xD0\xCF\x11\xE0\xA1\xB1\x1A\xE1"
    struct.pack_into("<5H", header, 24, 0x3E, 3, 0xFFFE, 9, 6)
    struct.pack_into("<4I", header, 40, 0, 1, 0, 0)
    struct.pack_into("<5I", header, 56, 0x1000, 0xFFFFFFFE, 0, 0xFFFFFFFE, 0)
    struct.pack_into("<109I", header, 76, 1, *([0xFFFFFFFF] * 108))
    directory = bytearray(512)
    directory[:22] = "Root Entry".encode("utf-16le") + b"\x00\x00"
    fat = bytearray(b"\xFF" * 512)
    struct.pack_into("<II", fat, 0, 0xFFFFFFFE, 0xFFFFFFFD)
    return bytes(header + directory + fat)


def mp4() -> bytes:
    def box(kind: bytes, payload: bytes = b"") -> bytes:
        return struct.pack(">I4s", 8 + len(payload), kind) + payload

    return box(b"ftyp", b"mp42" + bytes(4) + b"mp42isom") + box(b"moov") + box(
        b"mdat", b"data"
    )


def webm() -> bytes:
    return b"\x1A\x45\xDF\xA3" + b"\x84webm" + b"\x18\x53\x80\x67" + b"\x84data"


def lithtech_world(*, invalid_child: bool = False) -> bytes:
    render_offset = 60
    header = struct.pack(
        "<15I",
        85,
        render_offset,
        render_offset,
        render_offset,
        render_offset,
        render_offset,
        render_offset,
        *([0] * 8),
    )
    block = (
        struct.pack("<6f", 0.0, 0.0, 0.0, 1.0, 1.0, 1.0)
        + struct.pack("<6I", 0, 0, 0, 0, 0, 0)
        + bytes((1 if invalid_child else 0,))
        + struct.pack("<2I", 1 if invalid_child else 0xFFFFFFFF, 0xFFFFFFFF)
    )
    render_world = struct.pack("<I", 1) + block + struct.pack("<I", 0)
    client_light_groups = struct.pack("<I", 0)
    return header + render_world + client_light_groups


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

    def test_parses_tga_raw_and_rle_with_footer(self):
        for rle in (False, True):
            data = tga(2, 1, rle=rle)
            with tempfile.TemporaryFile() as stream:
                stream.write(data)
                parsed = parse_tga(stream, 0, len(data))
            self.assertEqual(parsed["bytes"], len(data))
            self.assertEqual(parsed["rle"], rle)
            self.assertTrue(parsed["tga_2_footer"])

    def test_parses_header_sized_dtx(self):
        data = dtx(2, 1)
        with tempfile.TemporaryFile() as stream:
            stream.write(data)
            parsed = parse_dtx(stream, 0, len(data))
        self.assertEqual(parsed["bytes"], len(data))
        self.assertEqual(parsed["pixel_format"], "BGRA8888")

    def test_parses_complete_gif_and_jpeg(self):
        for parser, format_name in ((parse_gif, "GIF"), (parse_jpeg, "JPEG")):
            data = image_bytes(format_name)
            with tempfile.TemporaryFile() as stream:
                stream.write(data)
                parsed = parser(stream, 0, len(data))
            self.assertEqual(parsed["bytes"], len(data))
            self.assertEqual((parsed["width"], parsed["height"]), (2, 1))

    def test_parses_structured_config_and_cfb_extent(self):
        for parser, data in ((parse_start_end_config, config()), (parse_cfb, cfb())):
            with tempfile.TemporaryFile() as stream:
                stream.write(data)
                parsed = parser(stream, 0, len(data))
            self.assertEqual(parsed["bytes"], len(data))

    def test_parses_mp4_top_level_boxes(self):
        data = mp4()
        with tempfile.TemporaryFile() as stream:
            stream.write(data)
            parsed = parse_mp4(stream, 0, len(data))
        self.assertEqual(parsed["bytes"], len(data))
        self.assertEqual(parsed["box_counts"], {"ftyp": 1, "mdat": 1, "moov": 1})

    def test_parses_explicitly_sized_webm_segment(self):
        data = webm()
        with tempfile.TemporaryFile() as stream:
            stream.write(data)
            parsed = parse_webm(stream, 0, len(data))
        self.assertEqual(parsed["bytes"], len(data))
        self.assertEqual(parsed["doctype"], "webm")

    def test_parses_lithtech_world_v85_render_tail_at_exact_offset(self):
        data = lithtech_world()
        with tempfile.TemporaryFile() as stream:
            stream.write(data)
            parsed = parse_lithtech_world(stream, 0, len(data))
        self.assertEqual(parsed["bytes"], len(data))
        self.assertEqual(parsed["version"], 85)
        self.assertEqual(parsed["render_blocks"], 1)
        self.assertEqual(parsed["client_light_groups"], 0)
        self.assertEqual(parsed["sha256"], hashlib.sha256(data).hexdigest())

    def test_rejects_lithtech_world_with_invalid_child_index(self):
        data = lithtech_world(invalid_child=True)
        with tempfile.TemporaryFile() as stream:
            stream.write(data)
            with self.assertRaisesRegex(RezError, "child 0 index"):
                parse_lithtech_world(stream, 0, len(data))

    def test_parses_ini_and_binary_bounded_web_bundle(self):
        ini_data = ini()
        with tempfile.TemporaryFile() as stream:
            stream.write(ini_data + dtx())
            parsed = parse_ini(stream, 0, len(ini_data + dtx()))
        self.assertEqual(parsed["bytes"], len(ini_data))
        web_data = web_bundle()
        with tempfile.TemporaryFile() as stream:
            stream.write(web_data + png())
            parsed = parse_ascii_web_bundle(stream, 0, len(web_data + png()))
        self.assertEqual(parsed["bytes"], len(web_data))
        self.assertEqual(parsed["next_frame_kind"], "png")
        css_script = (
            b"body{ background: black; }\n"
            + b".main { background-image: url('x.png'); }\n"
            + b"function ShowMain() { return true; }\n"
        )
        with tempfile.TemporaryFile() as stream:
            stream.write(css_script + png())
            parsed = parse_ascii_web_bundle(stream, 0, len(css_script + png()))
        self.assertEqual(parsed["bundle_kind"], "css_and_script")
        overlay = overlay_bundle()
        with tempfile.TemporaryFile() as stream:
            stream.write(overlay + mp4())
            parsed = parse_ascii_web_bundle(stream, 0, len(overlay + mp4()))
        self.assertEqual(parsed["bundle_kind"], "css_overlay")
        self.assertEqual(parsed["next_frame_kind"], "mp4")

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

    def test_recovers_lithtech_world_and_preserves_unsupported_suffix(self):
        frame = lithtech_world()
        suffix = b"UNSUPPORTED"
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "RF164.REZ"
            data = bytearray(168)
            data.extend(frame + suffix)
            root_offset = len(data)
            data.extend(bytes(24))
            data[:2] = b"\r\n"
            data[126] = 0x1A
            struct.pack_into("<III", data, 127, 1, root_offset, 24)
            source.write_bytes(data)
            result = recover_framed_prefix(
                source,
                hashlib.sha256(data).hexdigest(),
                root_offset,
                root / "out",
            )
            self.assertEqual(
                [item["kind"] for item in result["resources"]],
                ["lithtech_world"],
            )
            recovered = root / "out" / result["outputs"][0]["path"]
            self.assertEqual(recovered.read_bytes(), frame)
            self.assertEqual(result["trailing_region"]["bytes"], len(suffix))

    def test_recovers_mixed_media_and_converts_tga_dtx(self):
        frames = [
            tga(),
            dtx(),
            image_bytes("GIF"),
            image_bytes("JPEG"),
            config(),
            cfb(),
            ini(),
            dtx(),
            web_bundle(),
            png(),
            overlay_bundle(),
            mp4(),
            webm(),
        ]
        suffix = b"UNSUPPORTED"
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "RF019.REZ"
            data = bytearray(168)
            data.extend(b"".join(frames) + suffix)
            root_offset = len(data)
            data.extend(bytes(24))
            data[:2] = b"\r\n"
            data[126] = 0x1A
            struct.pack_into("<III", data, 127, 1, root_offset, 24)
            source.write_bytes(data)
            result = recover_framed_prefix(
                source,
                hashlib.sha256(data).hexdigest(),
                root_offset,
                root / "out",
            )
            self.assertEqual(
                [item["kind"] for item in result["resources"]],
                [
                    "tga",
                    "dtx",
                    "gif",
                    "jpeg",
                    "config",
                    "cfb",
                    "ini",
                    "dtx",
                    "web_bundle",
                    "png",
                    "web_bundle",
                    "mp4",
                    "webm",
                ],
            )
            self.assertEqual(len(result["outputs"]), 16)
            self.assertEqual(result["trailing_region"]["bytes"], len(suffix))


if __name__ == "__main__":
    unittest.main()
