#!/usr/bin/env python3
"""Decode Alternativa3D A3D2 protocol payloads and export their geometry as GLB.

The original game stores weapon models in DefineBinaryData SWF tags.  Their payload
is a two/four-byte packet envelope followed by zlib data containing the Alternativa
protocol representation of ``versions.version2.a3d.A3D2``.

Protocol/schema references used to document this interoperability implementation:

* AlternativaPlatform/Alternativa3D, Parser.as (MPL-2.0), vertex half unpacking.
* MapMakersAndProgrammers/TankiOnline2.0DemoClient, generated A3D2 codecs,
  packet optional-map and collection schemas.

This file is an independent Python implementation and contains no copied assets.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import struct
import sys
import zlib
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable


SCHEMAS: dict[str, list[tuple[str, Any]]] = {
    "A3D2": [
        ("ambientLights", ("vec?", "A3D2AmbientLight")),
        ("animationClips", ("vec?", "A3D2AnimationClip")),
        ("animationTracks", ("vec?", "A3D2Track")),
        ("boxes", ("vec?", "A3D2Box")),
        ("cubeMaps", ("vec?", "A3D2CubeMap")),
        ("decals", ("vec?", "A3D2Decal")),
        ("directionalLights", ("vec?", "A3D2DirectionalLight")),
        ("images", ("vec?", "A3D2Image")),
        ("indexBuffers", ("vec?", "A3D2IndexBuffer")),
        ("joints", ("vec?", "A3D2Joint")),
        ("maps", ("vec?", "A3D2Map")),
        ("materials", ("vec?", "A3D2Material")),
        ("meshes", ("vec?", "A3D2Mesh")),
        ("objects", ("vec?", "A3D2Object")),
        ("omniLights", ("vec?", "A3D2OmniLight")),
        ("skins", ("vec?", "A3D2Skin")),
        ("spotLights", ("vec?", "A3D2SpotLight")),
        ("sprites", ("vec?", "A3D2Sprite")),
        ("vertexBuffers", ("vec?", "A3D2VertexBuffer")),
    ],
    "A3D2AmbientLight": [("boundBoxId", "i32?"), ("color", "u32"), ("id", "i64"), ("intensity", "f32"), ("name", "str?"), ("parentId", "i64?"), ("transform", "A3D2Transform?"), ("visible", "bool")],
    "A3D2DirectionalLight": [("boundBoxId", "i32?"), ("color", "u32"), ("id", "i64"), ("intensity", "f32"), ("name", "str?"), ("parentId", "i64?"), ("transform", "A3D2Transform?"), ("visible", "bool")],
    "A3D2OmniLight": [("attenuationBegin", "f32"), ("attenuationEnd", "f32"), ("boundBoxId", "i32?"), ("color", "u32"), ("id", "i64"), ("intensity", "f32"), ("name", "str?"), ("parentId", "i64?"), ("transform", "A3D2Transform?"), ("visible", "bool")],
    "A3D2SpotLight": [("attenuationBegin", "f32"), ("attenuationEnd", "f32"), ("boundBoxId", "i32?"), ("color", "u32"), ("falloff", "f32"), ("hotspot", "f32"), ("id", "i64"), ("intensity", "f32"), ("name", "str?"), ("parentId", "i64?"), ("transform", "A3D2Transform?"), ("visible", "bool")],
    # The game's generated v2 codec appends duration in 1/10000-second ticks
    # after the track ids. Older community importers omit it, which shifts every
    # following clip/track and makes the animation payload look corrupt.
    "A3D2AnimationClip": [("id", "i32"), ("loop", "bool"), ("name", "str?"), ("objectIDs", ("vec?", "i64")), ("tracks", ("vec", "i32")), ("durationTicks", "u32")],
    "A3D2Keyframe": [("time", "f32"), ("transform", "A3D2Transform")],
    # This client uses the compact v2 track representation: a byte stream of
    # variable-size transforms plus u16 offsets into that stream.  The public
    # legacy importer expects an inline vector<A3D2Keyframe> here instead.
    "A3D2Track": [("id", "i32"), ("compressedKeyframes", "bytes"), ("keyframeOffsets", ("vec", "u16")), ("objectName", "str")],
    "A3D2Box": [("box", ("vec", "f32")), ("id", "i32")],
    "A3D2CubeMap": [("backId", "i32?"), ("bottomId", "i32?"), ("frontId", "i32?"), ("id", "i32"), ("leftId", "i32?"), ("rightId", "i32?"), ("topId", "i32")],
    "A3D2Image": [("id", "i32"), ("url", "str")],
    "A3D2Map": [("channel", "u16"), ("id", "i32"), ("imageId", "i32")],
    "A3D2Material": [("diffuseMapId", "i32?"), ("glossinessMapId", "i32?"), ("id", "i32"), ("lightMapId", "i32?"), ("normalMapId", "i32?"), ("opacityMapId", "i32?"), ("reflectionCubeMapId", "i32?"), ("specularMapId", "i32?")],
    "A3D2IndexBuffer": [("byteBuffer", "bytes"), ("id", "i32"), ("indexCount", "i32")],
    "A3D2VertexBuffer": [("attributes", ("vec", "enum")), ("byteBuffer", "bytes"), ("id", "i32"), ("vertexCount", "u16")],
    "A3D2Surface": [("indexBegin", "i32"), ("materialId", "i32?"), ("numTriangles", "i32")],
    "A3D2JointBindTransform": [("bindPoseTransform", "A3D2Transform"), ("id", "i64")],
    "A3D2Joint": [("boundBoxId", "i32?"), ("id", "i64"), ("name", "str?"), ("parentId", "i64?"), ("transform", "A3D2Transform?"), ("visible", "bool")],
    "A3D2Object": [("boundBoxId", "i32?"), ("id", "i64"), ("name", "str?"), ("parentId", "i64?"), ("transform", "A3D2Transform?"), ("visible", "bool")],
    "A3D2Mesh": [("boundBoxId", "i32?"), ("id", "i64"), ("indexBufferId", "i32"), ("name", "str?"), ("parentId", "i64?"), ("surfaces", ("vec", "A3D2Surface")), ("transform", "A3D2Transform?"), ("vertexBuffers", ("vec", "i32")), ("visible", "bool")],
    "A3D2Decal": [("boundBoxId", "i32?"), ("id", "i64"), ("indexBufferId", "i32"), ("name", "str?"), ("offset", "f32"), ("parentId", "i64?"), ("surfaces", ("vec", "A3D2Surface")), ("transform", "A3D2Transform?"), ("vertexBuffers", ("vec", "i32")), ("visible", "bool")],
    "A3D2Skin": [("boundBoxId", "i32?"), ("id", "i64"), ("indexBufferId", "i32"), ("jointBindTransforms", ("vec", "A3D2JointBindTransform")), ("joints", ("vec", "i64")), ("name", "str?"), ("numJoints", ("vec", "u16")), ("parentId", "i64?"), ("surfaces", ("vec", "A3D2Surface")), ("transform", "A3D2Transform?"), ("vertexBuffers", ("vec", "i32")), ("visible", "bool")],
    "A3D2Sprite": [("alwaysOnTop", "bool"), ("boundBoxId", "i32?"), ("height", "f32"), ("id", "i64"), ("materialId", "id"), ("name", "str?"), ("originX", "f32"), ("originY", "f32"), ("parentId", "i64?"), ("perspectiveScale", "bool"), ("rotation", "f32"), ("transform", "A3D2Transform?"), ("visible", "bool"), ("width", "f32")],
    "A3D2Transform": [("matrix", "A3DMatrix")],
    "A3DMatrix": [(name, "f32") for name in "abcdefghijkl"],
}


@dataclass
class Reader:
    data: bytes
    pos: int = 0
    optional_bits: list[bool] | None = None
    optional_pos: int = 0

    def take(self, count: int) -> bytes:
        end = self.pos + count
        if end > len(self.data):
            raise ValueError(f"read past payload at {self.pos}: need {count}, size {len(self.data)}")
        value = self.data[self.pos:end]
        self.pos = end
        return value

    def unpack(self, fmt: str) -> Any:
        size = struct.calcsize(fmt)
        return struct.unpack(fmt, self.take(size))[0]

    def length(self) -> int:
        first = self.unpack(">B")
        if first & 0x80 == 0:
            return first
        if first & 0x40 == 0:
            return ((first & 0x3F) << 8) | self.unpack(">B")
        return ((first & 0x3F) << 16) | (self.unpack(">B") << 8) | self.unpack(">B")

    def optional(self) -> bool:
        if self.optional_bits is None or self.optional_pos >= len(self.optional_bits):
            raise ValueError(f"optional map exhausted at bit {self.optional_pos}")
        result = self.optional_bits[self.optional_pos]
        self.optional_pos += 1
        return result


def unwrap_payload(source: bytes) -> Reader:
    # DefineBinaryData includes a 4-byte character ID/reserved prefix.  Accept raw
    # zlib too, which makes the tool useful with separately extracted payloads.
    zpos = source.find(b"\x78\x9c", 0, 12)
    if zpos < 0:
        zpos = source.find(b"\x78\xda", 0, 12)
    if zpos < 0:
        raise ValueError("zlib stream not found in the first 12 bytes")
    data = zlib.decompress(source[zpos:])
    reader = Reader(data)
    first = reader.unpack(">B")
    if first & 0x80:
        if first & 0x40:
            mask_len = ((first & 0x3F) << 16) | (reader.unpack(">B") << 8) | reader.unpack(">B")
        else:
            mask_len = first & 0x3F
        mask = reader.take(mask_len)
        bit_count = mask_len * 8
    else:
        inline_bytes = (first & 0x60) >> 5
        packed = bytes([first]) + reader.take(inline_bytes)
        bit_count = (5, 13, 21, 29)[inline_bytes]
        # The first three bits encode the inline-mask width rather than optional
        # values.  Shift them out and retain the fixed packet width; without the
        # width mask, 29-bit maps whose high bits are set overflow ``to_bytes``.
        packet_bits = 8 * (inline_bytes + 1)
        value = (int.from_bytes(packed, "big") << 3) & ((1 << packet_bits) - 1)
        mask = value.to_bytes(inline_bytes + 1, "big")
    reader.optional_bits = [bool(mask[i // 8] & (1 << (7 - i % 8))) for i in range(bit_count)]
    version_major = reader.unpack(">H")
    version_minor = reader.unpack(">H")
    if version_major != 2:
        raise ValueError(f"unsupported A3D protocol version {version_major}.{version_minor}")
    return reader


def read_value(reader: Reader, spec: Any, path: str = "root") -> Any:
    if isinstance(spec, tuple):
        kind, element = spec
        if kind.endswith("?") and reader.optional():
            return None
        count = reader.length()
        if os.environ.get("A3D2_TRACE"):
            print(f"TRACE {path}: vector length={count}, byte={reader.pos}, optional={reader.optional_pos}", file=sys.stderr)
        return [read_value(reader, element, f"{path}[{i}]") for i in range(count)]
    optional = spec.endswith("?")
    base = spec[:-1] if optional else spec
    if optional and reader.optional():
        return None
    primitive: dict[str, Callable[[], Any]] = {
        "bool": lambda: reader.unpack(">b") != 0,
        "i32": lambda: reader.unpack(">i"),
        "u32": lambda: reader.unpack(">I"),
        "i64": lambda: reader.unpack(">q"),
        "u16": lambda: reader.unpack(">H"),
        "f32": lambda: reader.unpack(">f"),
        "enum": lambda: reader.unpack(">i"),
        "str": lambda: reader.take(reader.length()).decode("utf-8"),
        "bytes": lambda: reader.take(reader.length()),
        # commons.Id is a protocol long wrapper in the source client.
        "id": lambda: reader.unpack(">q"),
    }
    if base in primitive:
        return primitive[base]()
    result = {}
    for field, field_spec in SCHEMAS[base]:
        try:
            result[field] = read_value(reader, field_spec, f"{path}.{field}")
        except Exception as exc:
            raise ValueError(f"{path}.{field} at byte {reader.pos}, optional bit {reader.optional_pos}: {exc}") from exc
    return result


def decode(source: bytes) -> tuple[dict[str, Any], Reader]:
    reader = unwrap_payload(source)
    result = read_value(reader, "A3D2")
    return result, reader


ATTRIBUTE_NAMES = {0: "POSITION", 1: "NORMAL", 2: "TANGENT4", 3: "JOINT", 4: "TEXCOORD"}
ATTRIBUTE_WIDTHS = {0: 3, 1: 3, 2: 4, 3: 4, 4: 2}


def unpack_vertices(buffer: dict[str, Any]) -> list[dict[str, tuple[float, ...]]]:
    raw = buffer["byteBuffer"]
    if len(raw) % 2:
        raise ValueError(f"vertex buffer {buffer['id']} has odd byte length")
    values = []
    # The compressed stream retains ByteArray's default BIG_ENDIAN order.  The
    # Alternativa unpacker expands its half-like words into LITTLE_ENDIAN f32.
    for i in range(0, len(raw), 2):
        data = struct.unpack(">H", raw[i:i + 2])[0]
        if data & 0x7FFF == 0:
            values.append(-0.0 if data & 0x8000 else 0.0)
            continue
        bits = (((data & 0x7FFF) + 0x1C000) << 13) & 0x7FFFFFFF
        if data > 0x8000:
            bits |= 0x80000000
        values.append(struct.unpack("<f", struct.pack("<I", bits))[0])
    count = buffer["vertexCount"]
    attrs = buffer["attributes"]
    expected_stride = sum(ATTRIBUTE_WIDTHS[a] for a in attrs)
    actual_stride = len(values) // count if count else 0
    if actual_stride != expected_stride or len(values) != count * actual_stride:
        raise ValueError(
            f"vertex buffer {buffer['id']} stride mismatch: attrs={attrs}, "
            f"expected={expected_stride}, actual={actual_stride}, values={len(values)}, vertices={count}"
        )
    result = []
    for vertex_index in range(count):
        cursor = vertex_index * actual_stride
        vertex: dict[str, tuple[float, ...]] = {}
        uv_index = joint_index = 0
        for attr in attrs:
            width = ATTRIBUTE_WIDTHS[attr]
            key = ATTRIBUTE_NAMES[attr]
            if attr == 4:
                key += str(uv_index)
                uv_index += 1
            elif attr == 3:
                key += str(joint_index)
                joint_index += 1
            vertex[key] = tuple(values[cursor:cursor + width])
            cursor += width
        result.append(vertex)
    return result


def unpack_indices(buffer: dict[str, Any]) -> list[int]:
    raw = buffer["byteBuffer"]
    count = buffer["indexCount"]
    if len(raw) != count * 2:
        raise ValueError(f"index buffer {buffer['id']} is {len(raw)} bytes for {count} indices")
    return list(struct.unpack("<" + "H" * count, raw))


def matrix4(transform: dict[str, Any] | None) -> list[float]:
    if transform is None:
        return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
    m = transform["matrix"]
    # Alternativa Matrix3D is a 3x4 affine row matrix. glTF stores columns.
    return [m["a"], m["e"], m["i"], 0, m["b"], m["f"], m["j"], 0, m["c"], m["g"], m["k"], 0, m["d"], m["h"], m["l"], 1]


class GlbBuilder:
    def __init__(self) -> None:
        self.binary = bytearray()
        self.gltf: dict[str, Any] = {
            "asset": {"version": "2.0", "generator": "Genesis Soldier Soul A3D2 recovery"},
            "scene": 0, "scenes": [{"nodes": []}], "nodes": [], "meshes": [],
            "buffers": [{"byteLength": 0}], "bufferViews": [], "accessors": [], "materials": [],
        }

    def blob_view(self, payload: bytes) -> int:
        while len(self.binary) % 4:
            self.binary.append(0)
        offset = len(self.binary)
        self.binary.extend(payload)
        index = len(self.gltf["bufferViews"])
        self.gltf["bufferViews"].append({"buffer": 0, "byteOffset": offset, "byteLength": len(payload)})
        return index

    def accessor(self, values: list[tuple[float, ...]] | list[int], component_type: int, kind: str, target: int) -> int:
        while len(self.binary) % 4:
            self.binary.append(0)
        offset = len(self.binary)
        if component_type == 5126:
            flat = [x for value in values for x in value]  # type: ignore[union-attr]
            self.binary.extend(struct.pack("<" + "f" * len(flat), *flat))
        elif component_type == 5123:
            self.binary.extend(struct.pack("<" + "H" * len(values), *values))
        else:
            raise ValueError(component_type)
        view = len(self.gltf["bufferViews"])
        self.gltf["bufferViews"].append({"buffer": 0, "byteOffset": offset, "byteLength": len(self.binary) - offset, "target": target})
        accessor: dict[str, Any] = {"bufferView": view, "componentType": component_type, "count": len(values), "type": kind}
        if component_type == 5126 and values:
            width = len(values[0])  # type: ignore[index]
            accessor["min"] = [min(v[i] for v in values) for i in range(width)]  # type: ignore[index]
            accessor["max"] = [max(v[i] for v in values) for i in range(width)]  # type: ignore[index]
        index = len(self.gltf["accessors"])
        self.gltf["accessors"].append(accessor)
        return index

    def finish(self, output: Path) -> None:
        self.gltf["buffers"][0]["byteLength"] = len(self.binary)
        json_bytes = json.dumps(self.gltf, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        json_bytes += b" " * ((4 - len(json_bytes) % 4) % 4)
        bin_bytes = bytes(self.binary) + b"\0" * ((4 - len(self.binary) % 4) % 4)
        total = 12 + 8 + len(json_bytes) + 8 + len(bin_bytes)
        glb = struct.pack("<4sII", b"glTF", 2, total)
        glb += struct.pack("<I4s", len(json_bytes), b"JSON") + json_bytes
        glb += struct.pack("<I4s", len(bin_bytes), b"BIN\0") + bin_bytes
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(glb)


def export_glb(model: dict[str, Any], output: Path, texture: Path | None = None) -> None:
    builder = GlbBuilder()
    texture_index = None
    if texture:
        view = builder.blob_view(texture.read_bytes())
        mime = "image/png" if texture.suffix.lower() == ".png" else "image/jpeg"
        builder.gltf["images"] = [{"name": texture.stem, "bufferView": view, "mimeType": mime}]
        builder.gltf["samplers"] = [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}]
        builder.gltf["textures"] = [{"source": 0, "sampler": 0}]
        texture_index = 0
    materials = model.get("materials") or []
    for material in materials:
        pbr: dict[str, Any] = {"baseColorFactor": [0.75, 0.75, 0.75, 1], "metallicFactor": 0.1, "roughnessFactor": 0.65}
        if texture_index is not None:
            pbr["baseColorTexture"] = {"index": texture_index}
            pbr["baseColorFactor"] = [1, 1, 1, 1]
        builder.gltf["materials"].append({"name": f"A3D2 Material {material['id']}", "pbrMetallicRoughness": pbr})
    material_lookup = {material["id"]: i for i, material in enumerate(materials)}
    vertex_buffers = {buffer["id"]: buffer for buffer in model.get("vertexBuffers") or []}
    index_buffers = {buffer["id"]: buffer for buffer in model.get("indexBuffers") or []}
    objects = (model.get("meshes") or []) + (model.get("skins") or []) + (model.get("decals") or [])
    for obj in objects:
        buffers = [vertex_buffers[i] for i in obj["vertexBuffers"]]
        decoded = [unpack_vertices(buffer) for buffer in buffers]
        if not decoded:
            continue
        vertex_count = len(decoded[0])
        combined = [{k: v for stream in decoded for k, v in stream[i].items()} for i in range(vertex_count)]
        positions = [v["POSITION"] for v in combined]
        attributes: dict[str, int] = {"POSITION": builder.accessor(positions, 5126, "VEC3", 34962)}
        if all("NORMAL" in v for v in combined):
            attributes["NORMAL"] = builder.accessor([v["NORMAL"] for v in combined], 5126, "VEC3", 34962)
        if all("TEXCOORD0" in v for v in combined):
            attributes["TEXCOORD_0"] = builder.accessor([v["TEXCOORD0"] for v in combined], 5126, "VEC2", 34962)
        all_indices = unpack_indices(index_buffers[obj["indexBufferId"]])
        primitives = []
        for surface in obj["surfaces"]:
            begin = surface["indexBegin"]
            indices = all_indices[begin:begin + surface["numTriangles"] * 3]
            primitive: dict[str, Any] = {"attributes": attributes, "indices": builder.accessor(indices, 5123, "SCALAR", 34963)}
            if surface["materialId"] in material_lookup:
                primitive["material"] = material_lookup[surface["materialId"]]
            primitives.append(primitive)
        mesh_index = len(builder.gltf["meshes"])
        builder.gltf["meshes"].append({"name": obj.get("name") or f"Object {obj['id']}", "primitives": primitives})
        node_index = len(builder.gltf["nodes"])
        builder.gltf["nodes"].append({"name": obj.get("name") or f"Object {obj['id']}", "mesh": mesh_index, "matrix": matrix4(obj.get("transform"))})
        builder.gltf["scenes"][0]["nodes"].append(node_index)
    if not objects:
        raise ValueError("A3D2 payload contains no mesh, skin, or decal geometry")
    builder.finish(output)


def print_summary(model: dict[str, Any], reader: Reader) -> None:
    print(f"decoded bytes: {reader.pos}/{len(reader.data)}; optional bits: {reader.optional_pos}/{len(reader.optional_bits or [])}")
    for key, value in model.items():
        if value is not None:
            print(f"{key}: {len(value) if isinstance(value, list) else value}")
    for image in model.get("images") or []:
        print(f"image {image['id']}: {image['url']}")
    tracks = {track["id"]: track for track in model.get("animationTracks") or []}
    for clip in model.get("animationClips") or []:
        frame_counts = [len(tracks[track_id]["keyframeOffsets"]) for track_id in clip["tracks"]]
        count_text = f"{min(frame_counts)}-{max(frame_counts)}" if frame_counts else "0"
        print(
            f"animation {clip['name']!r}: tracks={len(clip['tracks'])} "
            f"frames={count_text} duration={clip['durationTicks'] / 10000:.4f}s"
        )
    for obj in (model.get("meshes") or []) + (model.get("skins") or []):
        print(f"geometry {obj['id']} {obj.get('name')!r}: index={obj['indexBufferId']} vertex={obj['vertexBuffers']} surfaces={len(obj['surfaces'])}")
    for buffer in model.get("vertexBuffers") or []:
        stride = len(buffer["byteBuffer"]) // 2 // max(1, buffer["vertexCount"])
        attrs = [ATTRIBUTE_NAMES.get(attr, str(attr)) for attr in buffer["attributes"]]
        print(f"vertex buffer {buffer['id']}: vertices={buffer['vertexCount']} halfs={len(buffer['byteBuffer']) // 2} stride={stride} attrs={attrs}")


def compact_track_timing(track: dict[str, Any]) -> dict[str, Any]:
    """Decode the timing prefix used by this game's compact animation tracks.

    Every recovered weapon-animation keyframe record is 27 bytes: a big-endian
    u16 delta in 1/10000-second ticks followed by a still-proprietary 25-byte
    transform.  Keeping timing decoding separate from transform decoding lets
    the inventory prove clip duration/frame cadence without claiming that the
    matrices are already usable.
    """
    data = track["compressedKeyframes"]
    offsets = track["keyframeOffsets"]
    if not offsets:
        return {
            "keyframeRecordBytes": None,
            "transformBytesPerKeyframe": None,
            "frameDeltaTicks": [],
            "durationFromDeltasSeconds": 0,
        }

    sizes = [
        (offsets[index + 1] if index + 1 < len(offsets) else len(data)) - offset
        for index, offset in enumerate(offsets)
    ]
    if any(size < 2 for size in sizes):
        raise ValueError(f"compact animation track {track['id']} contains a record shorter than its u16 timing prefix")
    deltas = [int.from_bytes(data[offset:offset + 2], "big") for offset in offsets]
    record_size = sizes[0] if len(set(sizes)) == 1 else None
    return {
        "keyframeRecordBytes": record_size,
        "transformBytesPerKeyframe": record_size - 2 if record_size is not None else None,
        "timeEncoding": "big-endian u16 delta ticks",
        "frameDeltaTicks": deltas,
        "durationFromDeltasSeconds": sum(deltas) / 10000,
    }


def write_animation_inventory(model: dict[str, Any], source: Path, output: Path) -> None:
    tracks = {track["id"]: track for track in model.get("animationTracks") or []}
    inventory = {
        "source": str(source),
        "format": "Alternativa3D A3D2 compact animation",
        "timeBase": 10000,
        "clips": [],
    }
    for clip in model.get("animationClips") or []:
        clip_tracks = [tracks[track_id] for track_id in clip["tracks"]]
        timing = [compact_track_timing(track) for track in clip_tracks]
        duration_ticks = clip["durationTicks"]
        timing_errors = [abs(round(item["durationFromDeltasSeconds"] * 10000) - duration_ticks) for item in timing]
        inventory["clips"].append({
            "name": clip["name"],
            "loop": clip["loop"],
            "durationSeconds": duration_ticks / 10000,
            "trackCount": len(clip_tracks),
            "timingEncoding": "per-keyframe big-endian u16 delta at 1/10000 second",
            "maximumTimingDifferenceTicks": max(timing_errors, default=0),
            "tracks": [{
                "id": track["id"],
                "objectName": track["objectName"],
                "keyframeCount": len(track["keyframeOffsets"]),
                "compressedBytes": len(track["compressedKeyframes"]),
                "sha256": hashlib.sha256(track["compressedKeyframes"]).hexdigest(),
                **track_timing,
            } for track, track_timing in zip(clip_tracks, timing)],
        })
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(inventory, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path, nargs="?")
    parser.add_argument("--texture", type=Path)
    parser.add_argument("--dump-json", type=Path)
    parser.add_argument("--animation-inventory", type=Path)
    args = parser.parse_args()
    model, reader = decode(args.input.read_bytes())
    print_summary(model, reader)
    if reader.pos != len(reader.data):
        # ParserA3D decodes one A3D2 object and intentionally ignores package
        # trailer metadata used by some model exporters (not geometry bytes).
        print(f"trailing package metadata: {len(reader.data) - reader.pos} bytes")
    if args.dump_json:
        serializable = json.loads(json.dumps(model, default=lambda value: f"<{len(value)} bytes>" if isinstance(value, bytes) else str(value)))
        args.dump_json.write_text(json.dumps(serializable, ensure_ascii=False, indent=2), encoding="utf-8")
    if args.animation_inventory:
        write_animation_inventory(model, args.input, args.animation_inventory)
        print(f"wrote {args.animation_inventory}")
    if args.output:
        export_glb(model, args.output, args.texture)
        print(f"wrote {args.output}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"error: {exc}", file=sys.stderr)
        raise
