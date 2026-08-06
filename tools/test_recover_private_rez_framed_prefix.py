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
    parse_cfsprite,
    parse_cp949_web_bundle,
    parse_ascii_web_bundle,
    parse_ini,
    parse_start_end_config,
    parse_standalone_idat,
    parse_swf,
    parse_dds,
    parse_dtx,
    parse_flv,
    parse_gif,
    parse_html,
    parse_jpeg,
    parse_lithtech_world,
    parse_mp4,
    parse_png,
    parse_rps,
    parse_tga,
    parse_ui_layout,
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


def standalone_idat() -> bytes:
    return chunk(b"IDAT", zlib.compress(b"orphan pixels"))


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


def swf(signature: bytes = b"FWS", *, declared_delta: int = 0) -> bytes:
    body = b"\x08\x00" + b"\x00\x01" + b"\x01\x00"
    body += struct.pack("<HHH", 1 << 6, 1 << 6, 0)
    declared = 8 + len(body) + declared_delta
    header = signature + b"\x08" + struct.pack("<I", declared)
    return header + (zlib.compress(body) if signature == b"CWS" else body)


def flv(*, bad_previous_size: bool = False) -> bytes:
    metadata = b"\x02\x00\x0aonMetaData\x08\x00\x00\x00\x02"
    metadata += b"\x00\x08duration\x00" + struct.pack(">d", 1.0)
    metadata += b"\x00\x0ccanSeekToEnd\x01\x01\x00\x00\x09"

    def tag(kind: int, payload: bytes, timestamp: int) -> bytes:
        header = bytes((kind,)) + len(payload).to_bytes(3, "big")
        header += (timestamp & 0xFFFFFF).to_bytes(3, "big")
        header += bytes(((timestamp >> 24) & 0xFF,)) + b"\0\0\0"
        previous = 0 if bad_previous_size else 11 + len(payload)
        return header + payload + struct.pack(">I", previous)

    return (
        b"FLV\x01\x01\x00\x00\x00\x09\0\0\0\0"
        + tag(18, metadata, 0)
        + tag(9, b"\x12", 1000)
    )


def html() -> bytes:
    return (
        b"<! DOCTYPE html>\r\n<html><head><title>Test</title></head>"
        b"<body><div>Static</div></body></html>"
    )


def cp949_web_bundle() -> bytes:
    return (
        "//val\r\nvar label = '랭킹';\r\nfunction show(){ return label; }\r\n"
        "body{ color: white; }\r\n"
    ).encode("cp949")


def ui_layout() -> bytes:
    return (
        b"GROUP TEST\r\n\r\nDEFAULTGROUP TEST\r\n\r\n"
        b"STATIC Label\r\n-POSITIONX 1\r\n--DEFAULTMSG \"\"\r\n-END\r\n\r\n"
    )


def cfsprite(*, repeated_tick_delta: int = 0) -> bytes:
    def text(value: str) -> bytes:
        raw = value.encode("ascii")
        return struct.pack("<H", len(raw)) + raw

    data = bytearray(text("CFSprite"))
    data.extend(struct.pack("<7I", 5, 9, 0, 30, 256, 64, 1))
    data.extend(struct.pack("<I", 0))
    data.extend(text("TestSprite"))
    data.extend(struct.pack("<3I", 1, 1, 3))
    data.extend(text("test.png"))
    data.extend(text("ui/test.png"))
    data.extend(struct.pack("<H4I2I2B", 0, 64, 64, 64, 64, 11, 0, 1, 0))
    data.extend(struct.pack("<I", 2))
    for tick in (0, 10):
        data.extend(struct.pack("<3I", tick, 3, tick + repeated_tick_delta))
        data.extend(struct.pack("<7f", 1.0, 1.0, 0.0, 1.0, 0.0, 1.0, 0.0))
        data.extend(struct.pack("<I4B", tick, 255, 255, 255, 0))
    return bytes(data)


def rps(*, bad_index: bool = False) -> bytes:
    paths = [
        "f:/_ svn/svn_개발실/cfclient/rez/ui/test.png".encode("cp949"),
        b"ui/second.png",
    ]
    data = bytearray(struct.pack("<I", len(paths)))
    for path in paths:
        data.extend(struct.pack("<H", len(path)))
        data.extend(path)
    data.extend(struct.pack("<I", 2))
    data.extend(struct.pack("<2I", 0, 2 if bad_index else 1))
    data.extend(struct.pack("<I", 0))
    return bytes(data)


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

    def test_parses_crc_valid_standalone_idat_before_known_successor(self):
        data = standalone_idat()
        successor = png()
        with tempfile.TemporaryFile() as stream:
            stream.write(data + successor)
            parsed = parse_standalone_idat(stream, 0, len(data + successor))
        self.assertEqual(parsed["bytes"], len(data))
        self.assertEqual(parsed["next_frame_kind"], "png")
        self.assertEqual(parsed["sha256"], hashlib.sha256(data).hexdigest())

    def test_rejects_standalone_idat_without_known_successor(self):
        data = standalone_idat()
        with tempfile.TemporaryFile() as stream:
            stream.write(data + b"UNKNOWN")
            with self.assertRaisesRegex(RezError, "no known successor"):
                parse_standalone_idat(stream, 0, len(data + b"UNKNOWN"))

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

    def test_parses_self_delimiting_cfsprite_v5_at_exact_offset(self):
        data = cfsprite()
        with tempfile.TemporaryFile() as stream:
            stream.write(data + png())
            parsed = parse_cfsprite(stream, 0, len(data + png()))
        self.assertEqual(parsed["bytes"], len(data))
        self.assertEqual(parsed["items"], 1)
        self.assertEqual(parsed["keyframes"], 2)
        self.assertEqual(parsed["names"], ["TestSprite"])
        self.assertEqual(parsed["sha256"], hashlib.sha256(data).hexdigest())

    def test_rejects_cfsprite_inconsistent_keyframe_ticks(self):
        data = cfsprite(repeated_tick_delta=1)
        with tempfile.TemporaryFile() as stream:
            stream.write(data)
            with self.assertRaisesRegex(RezError, "tick sequence"):
                parse_cfsprite(stream, 0, len(data))

    def test_parses_cp949_rps_path_and_index_tables(self):
        data = rps()
        with tempfile.TemporaryFile() as stream:
            stream.write(data + png())
            parsed = parse_rps(stream, 0, len(data + png()))
        self.assertEqual(parsed["bytes"], len(data))
        self.assertEqual(parsed["path_count"], 2)
        self.assertEqual(parsed["index_count"], 2)
        self.assertIn("개발실", parsed["paths"][0])
        self.assertEqual(parsed["sha256"], hashlib.sha256(data).hexdigest())

    def test_rejects_nonsequential_rps_index_table(self):
        data = rps(bad_index=True)
        with tempfile.TemporaryFile() as stream:
            stream.write(data)
            with self.assertRaisesRegex(RezError, "non-sequential RPS index"):
                parse_rps(stream, 0, len(data))

    def test_parses_complete_fws_and_cws_at_exact_offset(self):
        for signature, compression in ((b"FWS", "none"), (b"CWS", "zlib")):
            data = swf(signature)
            with tempfile.TemporaryFile() as stream:
                stream.write(data + png())
                parsed = parse_swf(stream, 0, len(data + png()))
            self.assertEqual(parsed["bytes"], len(data))
            self.assertEqual(parsed["compression"], compression)
            self.assertEqual(parsed["frame_count"], 1)
            self.assertEqual(parsed["tag_count"], 3)
            self.assertEqual(parsed["sha256"], hashlib.sha256(data).hexdigest())

    def test_rejects_cws_declared_length_mismatch(self):
        data = swf(b"CWS", declared_delta=1)
        with tempfile.TemporaryFile() as stream:
            stream.write(data)
            with self.assertRaisesRegex(RezError, "declared length mismatch"):
                parse_swf(stream, 0, len(data))

    def test_parses_metadata_bounded_flv_before_known_successor(self):
        data = flv()
        successor = swf(b"CWS")
        with tempfile.TemporaryFile() as stream:
            stream.write(data + successor)
            parsed = parse_flv(stream, 0, len(data + successor))
        self.assertEqual(parsed["bytes"], len(data))
        self.assertEqual(parsed["tag_counts"], {"script": 1, "video": 1})
        self.assertEqual(parsed["duration_seconds"], 1.0)
        self.assertEqual(parsed["last_media_timestamp_ms"], 1000)
        self.assertEqual(parsed["next_frame_kind"], "swf")

    def test_rejects_flv_previous_tag_size_mismatch(self):
        data = flv(bad_previous_size=True)
        with tempfile.TemporaryFile() as stream:
            stream.write(data)
            with self.assertRaisesRegex(RezError, "PreviousTagSize mismatch"):
                parse_flv(stream, 0, len(data))

    def test_parses_html_through_unique_explicit_end_tag(self):
        data = html()
        suffix = b"var next = true;"
        with tempfile.TemporaryFile() as stream:
            stream.write(data + suffix)
            parsed = parse_html(stream, 0, len(data + suffix))
        self.assertEqual(parsed["bytes"], len(data))
        self.assertTrue(parsed["explicit_end_tag"])
        self.assertEqual(parsed["sha256"], hashlib.sha256(data).hexdigest())

    def test_parses_cp949_web_bundle_at_closed_binary_successor(self):
        data = cp949_web_bundle()
        successor = png()
        with tempfile.TemporaryFile() as stream:
            stream.write(data + successor)
            parsed = parse_cp949_web_bundle(stream, 0, len(data + successor))
        self.assertEqual(parsed["bytes"], len(data))
        self.assertEqual(parsed["encoding"], "cp949")
        self.assertEqual(parsed["non_ascii_characters"], 2)
        self.assertEqual(parsed["next_frame_kind"], "png")

    def test_rejects_unbalanced_cp949_web_bundle(self):
        data = cp949_web_bundle().replace(b"}", b"", 1)
        with tempfile.TemporaryFile() as stream:
            stream.write(data + png())
            with self.assertRaisesRegex(RezError, "incomplete CP949 web bundle grammar"):
                parse_cp949_web_bundle(stream, 0, len(data + png()))

    def test_parses_complete_ui_layout_before_binary_successor(self):
        data = ui_layout()
        successor = png()
        with tempfile.TemporaryFile() as stream:
            stream.write(data + successor)
            parsed = parse_ui_layout(stream, 0, len(data + successor))
        self.assertEqual(parsed["bytes"], len(data))
        self.assertEqual(parsed["group_names"], ["TEST"])
        self.assertEqual(parsed["component_counts"], {"STATIC": 1})
        self.assertEqual(parsed["next_frame_kind"], "png")

    def test_rejects_ui_layout_without_component_end(self):
        data = ui_layout().replace(b"-END\r\n", b"")
        with tempfile.TemporaryFile() as stream:
            stream.write(data + png())
            with self.assertRaisesRegex(RezError, "incomplete UI layout grammar"):
                parse_ui_layout(stream, 0, len(data + png()))

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
            peers = root / "RF164"
            peers.mkdir()
            (peers / "map.DAT").write_bytes(frame)
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
                peer_root=peers,
            )
            self.assertEqual(
                [item["kind"] for item in result["resources"]],
                ["lithtech_world"],
            )
            self.assertEqual(result["resources"][0]["peer_paths"], ["map.DAT"])
            recovered = root / "out" / result["outputs"][0]["path"]
            self.assertEqual(recovered.read_bytes(), frame)
            self.assertEqual(result["trailing_region"]["bytes"], len(suffix))

    def test_recovers_unambiguous_exact_loose_peer_chain(self):
        first = b"A" * 24
        second = b"B" * 3
        suffix = b"UNSUPPORTED" + first
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            peers = root / "RB001"
            (peers / "REZ" / "BUTES").mkdir(parents=True)
            (peers / "REZ" / "BUTES" / "first.DAT").write_bytes(first)
            (peers / "REZ" / "BUTES" / "first-alias.DAT").write_bytes(first)
            (peers / "REZ" / "BUTES" / "second.LTC").write_bytes(second)
            source = root / "RB001.REZ"
            data = bytearray(168)
            data.extend(first + second + suffix)
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
                peer_root=peers,
            )
            self.assertEqual(
                [item["kind"] for item in result["resources"]],
                ["exact_peer", "exact_peer"],
            )
            self.assertEqual(result["resources"][0]["peer_count"], 2)
            self.assertEqual(
                result["resources"][0]["peer_paths"],
                ["REZ/BUTES/first-alias.DAT", "REZ/BUTES/first.DAT"],
            )
            outputs = [root / "out" / item["path"] for item in result["outputs"]]
            self.assertEqual([item.read_bytes() for item in outputs], [first, second])
            self.assertEqual(result["trailing_region"]["bytes"], len(suffix))

    def test_rejects_ambiguous_exact_peer_lengths(self):
        short = b"X" * 20
        long = short + b"Y" * 8
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            peers = root / "RB001"
            peers.mkdir()
            (peers / "short.DAT").write_bytes(short)
            (peers / "long.DAT").write_bytes(long)
            source = root / "RB001.REZ"
            data = bytearray(168)
            data.extend(long)
            root_offset = len(data)
            data.extend(bytes(24))
            data[:2] = b"\r\n"
            data[126] = 0x1A
            struct.pack_into("<III", data, 127, 1, root_offset, 24)
            source.write_bytes(data)
            with self.assertRaisesRegex(RezError, "ambiguous exact loose-peer lengths"):
                recover_framed_prefix(
                    source,
                    hashlib.sha256(data).hexdigest(),
                    root_offset,
                    root / "out",
                    peer_root=peers,
                )

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
            standalone_idat(),
            png(),
            ui_layout(),
            png(),
            overlay_bundle(),
            mp4(),
            webm(),
            swf(b"CWS"),
            flv(),
            swf(b"FWS"),
            html(),
            cp949_web_bundle(),
            png(),
            cfsprite(),
            rps(),
            png(),
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
                    "standalone_idat",
                    "png",
                    "ui_layout",
                    "png",
                    "web_bundle",
                    "mp4",
                    "webm",
                    "swf",
                    "flv",
                    "swf",
                    "html",
                    "cp949_web_bundle",
                    "png",
                    "cfsprite",
                    "rps",
                    "png",
                ],
            )
            self.assertEqual(len(result["outputs"]), 29)
            self.assertEqual(result["trailing_region"]["bytes"], len(suffix))


if __name__ == "__main__":
    unittest.main()
