import hashlib
import json
import struct
import tempfile
import unittest
import zlib
from pathlib import Path

from audit_special_formats import (
    audit_private_rez_supplements,
    audit_uassetapi_outputs,
    decode_compact_pak_entry,
    decode_dtx,
    extract_supported_pak_entries,
    load_locked_pyooz_wheel,
    parse_iostore_directory_index,
    parse_pak,
    parse_pak_directory_index,
    parse_utoc,
    safe_unreal_path,
    safe_zip_name,
    write_unreal_package_closure_manifest,
)
from lithtech_ltc import (
    CROSSFIRE_XOR_KEY,
    LtcDecodeError,
    decode_ltc,
    decode_ltc_lta_root_prefix,
    lta_structure,
)


def fstring(value: str) -> bytes:
    encoded = value.encode("utf-8") + b"\0"
    return struct.pack("<i", len(encoded)) + encoded


class SpecialFormatTests(unittest.TestCase):
    @staticmethod
    def _pack_ltc_bits(bits):
        clear = bytes(
            sum(bit << offset for offset, bit in enumerate(bits[index : index + 8]))
            for index in range(0, len(bits), 8)
        )
        return bytes(value ^ CROSSFIRE_XOR_KEY[index % 16] for index, value in enumerate(clear))

    def test_decodes_crossfire_ltc_literal_and_end_token(self):
        bits = [0] * 32
        for value in b"(ok)":
            bits.append(1)
            bits.extend((value >> shift) & 1 for shift in range(7, -1, -1))
        bits.extend([0] + [0] * 12 + [0] * 4)
        result = decode_ltc(self._pack_ltc_bits(bits))
        self.assertEqual(result.data, b"(ok)")
        self.assertEqual(result.termination, "end_token")
        self.assertEqual(lta_structure(result.data)["parenthesis_depth"], 0)

    def test_decodes_crossfire_ltc_span_and_physical_eof(self):
        bits = [0] * 32
        for value in b"ab":
            bits.append(1)
            bits.extend((value >> shift) & 1 for shift in range(7, -1, -1))
        bits.append(0)
        bits.extend((1 >> shift) & 1 for shift in range(11, -1, -1))
        bits.extend([0, 0, 0, 0])  # stored length 0 means a two-byte span
        result = decode_ltc(self._pack_ltc_bits(bits))
        self.assertEqual(result.data, b"abab")
        self.assertEqual(result.termination, "physical_eof")

    def test_decodes_token_bounded_lta_root_prefix_with_zero_alignment(self):
        bits = [0] * 32
        for value in b"(world)":
            bits.append(1)
            bits.extend((value >> shift) & 1 for shift in range(7, -1, -1))
        consumed = len(bits)
        bits.extend([0] * ((16 - len(bits) % 16) % 16))
        encoded = self._pack_ltc_bits(bits) + b"SUCCESSOR"
        result = decode_ltc_lta_root_prefix(encoded)
        self.assertEqual(result.data, b"(world)")
        self.assertEqual(result.bits_consumed, consumed)
        self.assertEqual(result.input_bytes, len(bits) // 8)
        self.assertEqual(result.padding_bits, len(bits) - consumed)

    def test_lta_root_prefix_rejects_non_text_and_nonzero_padding(self):
        for payload in (b"(bad\x01)", b"(ok)"):
            bits = [0] * 32
            for value in payload:
                bits.append(1)
                bits.extend((value >> shift) & 1 for shift in range(7, -1, -1))
            bits.extend([0] * ((16 - len(bits) % 16) % 16))
            if payload == b"(ok)":
                bits[-1] = 1
            with self.assertRaises(LtcDecodeError):
                decode_ltc_lta_root_prefix(self._pack_ltc_bits(bits))

    def test_decodes_raw_bgra_dtx(self):
        data = bytearray(164 + 16)
        struct.pack_into("<iiHHHHII", data, 0, 0, -5, 2, 2, 1, 0, 0x88, 0)
        data[26] = 3
        data[164:] = bytes([0, 0, 255, 255] * 4)
        png, details = decode_dtx(bytes(data))
        self.assertTrue(png.startswith(b"\x89PNG"))
        self.assertEqual((details["width"], details["height"]), (2, 2))

    def test_decodes_dxt1_dtx_through_bounded_dds_wrapper(self):
        data = bytearray(164 + 8)
        struct.pack_into("<iiHHHHII", data, 0, 0, -5, 4, 4, 1, 0, 0, 0)
        data[26] = 4
        struct.pack_into("<HHI", data, 164, 0xF800, 0x07E0, 0)
        png, details = decode_dtx(bytes(data))
        self.assertTrue(png.startswith(b"\x89PNG"))
        self.assertEqual(details["decoded_pixel_format"], "DXT1")

    def test_parses_pak_footer_and_index_hash(self):
        mount = b"../../../\0"
        index = struct.pack("<i", len(mount)) + mount + struct.pack("<I", 7) + b"index"
        footer = struct.pack("<IIQQ", 0x5A6F12E1, 11, 4, len(index)) + hashlib.sha1(index).digest() + b"Zlib\0" + bytes(155)
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "test.pak"
            path.write_bytes(b"data" + index + footer)
            parsed = parse_pak(path)
            self.assertTrue(parsed["index_sha1_verified"])
            self.assertEqual(parsed["entry_count"], 7)
            self.assertEqual(parsed["mount_point"], "../../../")

    def test_parses_pak_v11_full_directory_index(self):
        directory = (
            struct.pack("<i", 1)
            + fstring("Content/")
            + struct.pack("<i", 2)
            + fstring("Map.umap")
            + struct.pack("<I", 0)
            + fstring("Map.uexp")
            + struct.pack("<I", 12)
        )
        path_hash = b"path-hash-secondary"
        prefix = b"DATA"
        path_hash_offset = len(prefix)
        directory_offset = path_hash_offset + len(path_hash)
        primary_offset = directory_offset + len(directory)
        mount = fstring("../../../")
        encoded_entries = bytes(24)
        primary = (
            mount
            + struct.pack("<I", 2)
            + struct.pack("<Q", 1234)
            + struct.pack("<IQQ", 1, path_hash_offset, len(path_hash))
            + hashlib.sha1(path_hash).digest()
            + struct.pack("<IQQ", 1, directory_offset, len(directory))
            + hashlib.sha1(directory).digest()
            + struct.pack("<I", len(encoded_entries))
            + encoded_entries
        )
        footer = (
            struct.pack(
                "<IIQQ", 0x5A6F12E1, 11, primary_offset, len(primary)
            )
            + hashlib.sha1(primary).digest()
            + b"Zlib\0"
            + bytes(155)
        )
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "full-index.pak"
            path.write_bytes(prefix + path_hash + directory + primary + footer)
            parsed = parse_pak(path)
        self.assertTrue(parsed["full_directory_index_sha1_verified"])
        self.assertEqual(parsed["directory_entry_count"], 2)
        self.assertEqual(
            [item["path"] for item in parsed["_directory_entries"]],
            ["Content/Map.umap", "Content/Map.uexp"],
        )

    def test_extracts_only_sha1_verified_uncompressed_pak_entry(self):
        payload = b'{"EngineAssociation":"5.3"}'
        serialized = (
            struct.pack("<QQQi", 0, len(payload), len(payload), 0)
            + hashlib.sha1(payload).digest()
            + b"\0"
            + struct.pack("<I", 0)
            + payload
        )
        encoded = struct.pack("<III", 0xE0000000, 0, len(payload))
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            pak = root / "sample.pak"
            pak.write_bytes(serialized)
            result = extract_supported_pak_entries(
                pak,
                "Sample/Content.pak",
                hashlib.sha256(serialized).hexdigest(),
                len(serialized),
                [{"path": "Game.uproject", "encoded_entry_offset": 0}],
                encoded,
                root / "out",
            )
            self.assertEqual(result["extracted_count"], 1)
            self.assertEqual(result["engine_associations"], ["5.3"])
            extracted = root / "out" / result["outputs"][0]["path"]
            self.assertEqual(extracted.read_bytes(), payload)

    def test_decodes_compact_pak_entry_with_implicit_block_size(self):
        flags = 0xE08000A0
        encoded = struct.pack("<IIII", flags, 0x1234, 0x20000, 0x4567)
        entry = decode_compact_pak_entry(encoded, 0)
        self.assertEqual(entry["compression_method_index"], 1)
        self.assertEqual(entry["compression_block_count"], 2)
        self.assertEqual(entry["compression_block_size"], 0x10000)
        self.assertEqual(entry["data_offset"], 0x1234)
        self.assertEqual(entry["uncompressed_size"], 0x20000)
        self.assertEqual(entry["stored_size"], 0x4567)
        self.assertEqual(entry["encoded_bytes"], 16)

    def test_decodes_compact_pak_entry_with_64_bit_fields(self):
        flags = 0x00800040
        encoded = struct.pack(
            "<IQQQ", flags, 0x1_0000_1234, 0x2_0000_5678, 0x3_0000_9ABC
        )
        entry = decode_compact_pak_entry(encoded, 0)
        self.assertEqual(entry["compression_method_index"], 1)
        self.assertEqual(entry["compression_block_count"], 1)
        self.assertEqual(entry["compression_block_size"], 0x2_0000_5678)
        self.assertEqual(entry["data_offset"], 0x1_0000_1234)
        self.assertEqual(entry["stored_size"], 0x3_0000_9ABC)
        self.assertEqual(entry["encoded_bytes"], 28)

    def test_rejects_truncated_compact_pak_entry(self):
        with self.assertRaisesRegex(ValueError, "stored size"):
            decode_compact_pak_entry(struct.pack("<III", 0xE08000A0, 1, 2), 0)

    def test_decodes_blocked_zlib_pak_entry_and_verifies_sha1(self):
        payload = b"zlib payload" * 200
        compressed = zlib.compress(payload)
        header_size = 73
        serialized = (
            struct.pack("<QQQi", 0, len(compressed), len(payload), 2)
            + hashlib.sha1(compressed).digest()
            + struct.pack("<IQQ", 1, header_size, header_size + len(compressed))
            + b"\0"
            + struct.pack("<I", len(payload))
            + compressed
        )
        encoded = struct.pack(
            "<IIIII",
            0xE100007F,
            len(payload),
            0,
            len(payload),
            len(compressed),
        )
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            pak = root / "zlib.pak"
            pak.write_bytes(serialized)
            result = extract_supported_pak_entries(
                pak,
                "Sample/Zlib.pak",
                hashlib.sha256(serialized).hexdigest(),
                len(serialized),
                [{"path": "Config.ini", "encoded_entry_offset": 0}],
                encoded,
                root / "out",
            )
            self.assertEqual(result["method_counts"], {"zlib": 1})
            extracted = root / "out" / result["outputs"][0]["path"]
            self.assertEqual(extracted.read_bytes(), payload)

    def test_decodes_hash_verified_oodle_pak_entry_with_locked_provider(self):
        payload = b"oodle payload" * 200
        compressed = b"synthetic-oodle-block"
        header_size = 73
        serialized = (
            struct.pack("<QQQi", 0, len(compressed), len(payload), 1)
            + hashlib.sha1(compressed).digest()
            + struct.pack("<IQQ", 1, header_size, header_size + len(compressed))
            + b"\0"
            + struct.pack("<I", len(payload))
            + compressed
        )
        encoded = struct.pack(
            "<IIIII",
            0xE080007F,
            len(payload),
            0,
            len(payload),
            len(compressed),
        )
        calls = []

        def decoder(data, output_bytes):
            calls.append((data, output_bytes))
            return payload

        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            pak = root / "oodle.pak"
            pak.write_bytes(serialized)
            result = extract_supported_pak_entries(
                pak,
                "Sample/Oodle.pak",
                hashlib.sha256(serialized).hexdigest(),
                len(serialized),
                [{"path": "Asset.uexp", "encoded_entry_offset": 0}],
                encoded,
                root / "out",
                decoder,
                {"package": "test-provider"},
            )
            self.assertEqual(result["method_counts"], {"oodle": 1})
            self.assertEqual(calls, [(compressed, len(payload))])
            extracted = root / "out" / result["outputs"][0]["path"]
            self.assertEqual(extracted.read_bytes(), payload)

    def test_rejects_unlocked_pyooz_wheel(self):
        with tempfile.TemporaryDirectory() as tmp:
            wheel = Path(tmp) / "pyooz.whl"
            wheel.write_bytes(b"not the approved wheel")
            with self.assertRaisesRegex(ValueError, "wheel SHA-256 mismatch"):
                load_locked_pyooz_wheel(wheel, "0" * 64)

    def test_classifies_unreal_package_file_closure(self):
        directory_entries = [
            {"path": "Game/T_Ready.uasset"},
            {"path": "Game/T_Ready.uexp"},
            {"path": "Game/T_Missing.uasset"},
            {"path": "Game/T_Missing.uexp"},
            {"path": "Game/Inline.uasset"},
        ]
        extracted_entries = [
            {"path": "Game/T_Ready.uasset", "sha256": "a" * 64, "bytes": 1},
            {"path": "Game/T_Ready.uexp", "sha256": "b" * 64, "bytes": 2},
            {"path": "Game/T_Missing.uasset", "sha256": "c" * 64, "bytes": 3},
            {"path": "Game/Inline.uasset", "sha256": "d" * 64, "bytes": 4},
        ]
        with tempfile.TemporaryDirectory() as tmp:
            arguments = (
                "Content.pak",
                "1" * 64,
                directory_entries,
                extracted_entries,
            )
            result = write_unreal_package_closure_manifest(
                *arguments,
                {
                    "path": "manifest.json",
                    "sha256": "2" * 64,
                    "bytes": 10,
                    "status": "converted",
                },
                Path(tmp),
            )
            manifest = Path(tmp) / result["manifest"]["path"]
            content = json.loads(manifest.read_text())
            repeated = write_unreal_package_closure_manifest(
                *arguments,
                {
                    "path": "manifest.json",
                    "sha256": "2" * 64,
                    "bytes": 10,
                    "status": "verified_existing",
                },
                Path(tmp),
            )
            self.assertEqual(repeated["manifest"]["status"], "verified_existing")
        self.assertEqual(result["extracted_primary_packages"], 3)
        self.assertEqual(
            result["conversion_readiness_counts"],
            {
                "file_set_complete_candidate": 1,
                "not_ready_missing_companions": 1,
                "self_contained_file_candidate": 1,
            },
        )
        statuses = {
            item["path"]: item["closure_status"] for item in content["packages"]
        }
        self.assertEqual(statuses["Game/T_Ready.uasset"], "complete")

    def test_verifies_uassetapi_json_outputs(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source_key = "Sample__123456789abc"
            json_root = root / "unreal-object-json" / source_key
            json_root.mkdir(parents=True)
            converted = json_root / "Game.uasset.json"
            converted.write_text('{"ok":true}\n')
            report_path = root / "unreal-analysis" / source_key / "uassetapi-audit.json"
            report_path.parent.mkdir(parents=True)
            report_path.write_text(
                json.dumps(
                    {
                        "tool_version": "2",
                        "dependency": {"name": "UAssetAPI", "version": "1.1.0"},
                        "engine_version": "4.27",
                        "summary": {
                            "candidates": 1,
                            "structural_parsed": 1,
                            "full_parsed": 1,
                        },
                        "records": [
                            {
                                "full_parse": {
                                    "status": "parsed",
                                    "binary_equality_verified": True,
                                    "json_output": {
                                        "path": "Game.uasset.json",
                                        "bytes": converted.stat().st_size,
                                        "sha256": hashlib.sha256(converted.read_bytes()).hexdigest(),
                                        "representation": "uassetapi_json",
                                    },
                                }
                            }
                        ],
                    }
                )
            )
            result = audit_uassetapi_outputs(root)
        self.assertEqual(result["summary"]["full_parsed"], 1)
        self.assertEqual(result["summary"]["binary_equality_verified"], 1)
        self.assertEqual(result["summary"]["outputs"], 2)
        self.assertEqual(result["summary"]["errors"], 0)

    def test_verifies_private_rez_supplement_outputs(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            output = repo / "recovery/special-formats"
            output.mkdir(parents=True)
            decoded = output / "private-rez-decoded/item.bin"
            decoded.parent.mkdir()
            decoded.write_bytes(b"decoded")
            png = output / "private-rez-media/item.png"
            png.parent.mkdir()
            png.write_bytes(b"png")
            glb = output / "private-rez-models/item.glb"
            glb.parent.mkdir()
            glb.write_bytes(b"glb")
            common_summary = {
                "lzma_streams": 1,
                "decoded_bytes": 7,
                "materialized_outputs": 1,
            }
            (repo / "recovery/private-rez-recovery.json").write_text(
                json.dumps(
                    {
                        "tool_version": "1",
                        "outputs": [
                            {
                                "path": "private-rez-decoded/item.bin",
                                "bytes": 7,
                                "sha256": hashlib.sha256(b"decoded").hexdigest(),
                            }
                        ],
                        "summary": common_summary,
                    }
                )
            )
            (repo / "recovery/private-rez-media-conversions.json").write_text(
                json.dumps(
                    {
                        "tool_version": "1",
                        "records": [
                            {
                                "output": {
                                    "path": "private-rez-media/item.png",
                                    "bytes": 3,
                                    "sha256": hashlib.sha256(b"png").hexdigest(),
                                }
                            }
                        ],
                        "summary": {
                            "converted_or_verified": 1,
                            "kind_counts": {"dtx": 1},
                            "output_bytes": 3,
                            "preserved_unsupported": 0,
                            "failures": 0,
                        },
                    }
                )
            )
            (repo / "recovery/private-rez-model-conversions.json").write_text(
                json.dumps(
                    {
                        "tool_version": "1",
                        "records": [
                            {
                                "output": {
                                    "path": "private-rez-models/item.glb",
                                    "bytes": 3,
                                    "sha256": hashlib.sha256(b"glb").hexdigest(),
                                }
                            }
                        ],
                        "summary": {
                            "converted_or_verified": 1,
                            "failures": 1,
                            "meshes": 2,
                            "vertices": 3,
                            "triangles": 1,
                            "output_bytes": 3,
                        },
                    }
                )
            )
            result = audit_private_rez_supplements(repo, output)
        self.assertEqual(result["status"], "verified")
        self.assertEqual(result["summary"]["png_conversions"], 1)
        self.assertEqual(result["summary"]["ltb_glb_conversions"], 1)
        self.assertEqual(result["summary"]["ltb_glb_skinned_files"], 0)
        self.assertEqual(result["summary"]["ltb_glb_skinned_meshes"], 0)
        self.assertEqual(result["summary"]["ltb_glb_animated_files"], 0)
        self.assertEqual(result["summary"]["ltb_glb_animations"], 0)
        self.assertEqual(result["summary"]["ltb_glb_animation_keyframes"], 0)
        self.assertEqual(result["summary"]["ltb_glb_animation_channels"], 0)
        self.assertEqual(result["summary"]["ltb_glb_morph_targets"], 0)
        self.assertEqual(result["summary"]["ltb_glb_failures"], 1)
        self.assertEqual(len(result["verifications"]), 3)
        self.assertEqual(result["summary"]["verification_errors"], 0)

    def test_parses_utoc_header(self):
        header = bytearray(144)
        header[:16] = b"-==--==--==--==-"
        header[16] = 5
        struct.pack_into("<III", header, 20, 144, 25, 30)
        struct.pack_into("<I", header, 32, 12)
        struct.pack_into("<I", header, 52, 1)
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "test.utoc"
            path.write_bytes(header + bytes(910))
            parsed = parse_utoc(path)
            self.assertEqual((parsed["version"], parsed["toc_entry_count"]), (5, 25))

    def test_parses_iostore_directory_graph_and_utoc_manifest(self):
        none = 0xFFFFFFFF
        directory = (
            fstring("../../../")
            + struct.pack("<i", 1)
            + struct.pack("<IIII", none, none, none, 0)
            + struct.pack("<i", 1)
            + struct.pack("<III", 0, none, 7)
            + struct.pack("<i", 1)
            + fstring("Map.uasset")
        )
        parsed_directory = parse_iostore_directory_index(directory)
        self.assertEqual(
            parsed_directory["entries"],
            [{"path": "Map.uasset", "toc_entry_index": 7}],
        )
        header = bytearray(144)
        header[:16] = b"-==--==--==--==-"
        header[16] = 5
        struct.pack_into("<I", header, 20, 144)
        struct.pack_into("<I", header, 32, 12)
        struct.pack_into("<I", header, 48, len(directory))
        struct.pack_into("<I", header, 52, 1)
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "directory.utoc"
            path.write_bytes(header + directory)
            parsed = parse_utoc(path)
        self.assertEqual(parsed["file_count"], 1)
        self.assertEqual(parsed["_directory_entries"][0]["path"], "Map.uasset")

    def test_rejects_unsafe_zip_path(self):
        with self.assertRaises(ValueError):
            safe_zip_name("../../escape.pak")

    def test_rejects_unsafe_unreal_directory_path(self):
        with self.assertRaises(ValueError):
            safe_unreal_path("../../escape.uasset")


if __name__ == "__main__":
    unittest.main()
