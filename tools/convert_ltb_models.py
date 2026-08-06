#!/usr/bin/env python3
"""Convert bounded LithTech Jupiter LTB v9 models to glTF 2.0 GLB.

The structural offsets are independently implemented from the loader notes in
Cote-Duke's LTB2X source release and the public CrossFire LithTech runtime
loader.  See third_party_notices/LTB2X-LICENSE.txt.  Mesh geometry, bounded
skeletal vertex bindings, skeletal animation channels, and vertex morph
animation channels are converted.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import struct
import tempfile
from collections import Counter
from concurrent.futures import ProcessPoolExecutor
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


MAX_MESHES = 100_000
MAX_VERTICES = 65_535
MAX_FACES = 65_535
MAX_ANIMATIONS = 10_000
MAX_KEYFRAMES = 100_000
MAX_SOCKETS = 100_000
OUTPUT_LAYOUT_VERSION = "layout-v7"

RENDER_OBJECT_RIGID = 4
RENDER_OBJECT_SKELETAL = 5
RENDER_OBJECT_VERTEX_ANIMATED = 6
RENDER_OBJECT_NULL = 7

VERTDATATYPE_POSITION = 0x0001
VERTDATATYPE_NORMAL = 0x0002
VERTDATATYPE_UVSETS_1 = 0x0010
VERTDATATYPE_UVSETS_2 = 0x0020
VERTDATATYPE_UVSETS_3 = 0x0040
VERTDATATYPE_UVSETS_4 = 0x0080
VERTDATATYPE_BASISVECTORS = 0x0100
VERTDATATYPE_KNOWN_MASK = 0x01FF

BLEND_NONE = 0
BLEND_NONINDEXED_B1 = 1
BLEND_NONINDEXED_B2 = 2
BLEND_NONINDEXED_B3 = 3
BLEND_INDEXED_B1 = 4
BLEND_INDEXED_B2 = 5
BLEND_INDEXED_B3 = 6


class LtbError(ValueError):
    pass


@dataclass
class LtbMesh:
    name: str
    mesh_type: int
    positions: list[tuple[float, float, float]]
    normals: list[tuple[float, float, float]]
    texcoords: list[tuple[float, float]]
    indices: list[int]
    joints: list[tuple[int, int, int, int]] | None = None
    weights: list[tuple[float, float, float, float]] | None = None
    animation_node: int | None = None
    unduplicated_vertex_count: int | None = None
    duplicate_map: list[tuple[int, int]] | None = None
    retained_vertex_indices: list[int] | None = None
    source_vertex_count: int | None = None


@dataclass
class LtbAnimationNode:
    translations: list[tuple[float, float, float]]
    rotations: list[tuple[float, float, float, float]]
    vertex_frames: list[list[tuple[float, float, float]]] | None = None


@dataclass
class LtbAnimation:
    name: str
    compression_type: int
    interpolation_ms: int
    times_ms: list[int]
    keyframe_strings: list[str]
    nodes: list[LtbAnimationNode]
    root_translation: tuple[float, float, float] = (0.0, 0.0, 0.0)


def complete_blend_weights(
    explicit: tuple[float, ...], label: str, vertex_index: int
) -> tuple[float, float, float, float]:
    if not all(math.isfinite(value) for value in explicit):
        raise LtbError(f"non-finite blend weight in {label} at vertex {vertex_index}")
    implicit = 1.0 - sum(explicit)
    values = [*explicit, implicit]
    if any(value < -1e-4 or value > 1.0001 for value in values):
        raise LtbError(f"invalid blend weights in {label} at vertex {vertex_index}")
    values = [min(1.0, max(0.0, value)) for value in values]
    total = sum(values)
    if total <= 1e-8:
        raise LtbError(f"zero blend weight total in {label} at vertex {vertex_index}")
    values = [value / total for value in values]
    values.extend([0.0] * (4 - len(values)))
    return tuple(values[:4])


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def bounded_slice(data: bytes, offset: int, size: int, label: str) -> bytes:
    if offset < 0 or size < 0 or offset + size > len(data):
        raise LtbError(f"{label} exceeds LTB bounds: offset={offset} size={size}")
    return data[offset : offset + size]


def u16(data: bytes, offset: int, label: str) -> int:
    return struct.unpack("<H", bounded_slice(data, offset, 2, label))[0]


def u32(data: bytes, offset: int, label: str) -> int:
    return struct.unpack("<I", bounded_slice(data, offset, 4, label))[0]


def read_ltb_string(data: bytes, cursor: int, label: str) -> tuple[str, int]:
    length = u16(data, cursor, f"{label} length")
    cursor += 2
    value = bounded_slice(data, cursor, length, label).decode(
        "cp1252", errors="replace"
    )
    return value, cursor + length


def normalize_quaternion(
    value: tuple[float, float, float, float], label: str
) -> tuple[float, float, float, float]:
    if not all(math.isfinite(component) for component in value):
        raise LtbError(f"non-finite quaternion in {label}")
    magnitude = math.sqrt(sum(component * component for component in value))
    if magnitude <= 1e-8:
        raise LtbError(f"zero quaternion in {label}")
    return tuple(component / magnitude for component in value)


def expand_animation_channel(
    values: list[tuple[float, ...]],
    keyframe_count: int,
    default: tuple[float, ...],
    label: str,
) -> list[tuple[float, ...]]:
    if not values:
        return [default] * keyframe_count
    if len(values) == 1:
        return values * keyframe_count
    if len(values) != keyframe_count:
        raise LtbError(
            f"invalid animation channel count in {label}: "
            f"{len(values)}/{keyframe_count}"
        )
    return values


def parse_composite_tail(
    data: bytes, cursor: int, bone_count: int
) -> tuple[list[LtbAnimation], dict[str, Any], int]:
    """Parse the model data following the preorder skeleton node records."""
    weight_set_count = u32(data, cursor, "weight set count")
    cursor += 4
    if weight_set_count > MAX_ANIMATIONS:
        raise LtbError(f"implausible weight set count: {weight_set_count}")
    for weight_set_index in range(weight_set_count):
        _, cursor = read_ltb_string(
            data, cursor, f"weight set {weight_set_index} name"
        )
        weight_count = u32(data, cursor, f"weight set {weight_set_index} count")
        cursor += 4
        if weight_count != bone_count:
            raise LtbError(
                f"weight set {weight_set_index} size mismatch: "
                f"{weight_count}/{bone_count}"
            )
        weights = struct.unpack(
            f"<{weight_count}f",
            bounded_slice(
                data,
                cursor,
                weight_count * 4,
                f"weight set {weight_set_index} values",
            ),
        )
        cursor += weight_count * 4
        if not all(math.isfinite(value) for value in weights):
            raise LtbError(f"non-finite weight set {weight_set_index}")

    child_model_count = u32(data, cursor, "child model count")
    cursor += 4
    if not 1 <= child_model_count <= 32:
        raise LtbError(f"implausible child model count: {child_model_count}")
    child_model_names = ["SELF"]
    for child_index in range(1, child_model_count):
        child_name, cursor = read_ltb_string(
            data, cursor, f"child model {child_index} name"
        )
        child_model_names.append(child_name)

    animation_count = u32(data, cursor, "animation count")
    cursor += 4
    if animation_count > MAX_ANIMATIONS:
        raise LtbError(f"implausible animation count: {animation_count}")
    animations: list[LtbAnimation] = []
    compression_counts: Counter[int] = Counter()
    duplicate_timestamps = 0
    vertex_animation_channels = 0
    total_keyframes = 0
    for animation_index in range(animation_count):
        dimensions = struct.unpack(
            "<3f",
            bounded_slice(data, cursor, 12, f"animation {animation_index} dimensions"),
        )
        cursor += 12
        if not all(math.isfinite(value) and value >= 0 for value in dimensions):
            raise LtbError(f"invalid animation dimensions at {animation_index}")
        name, cursor = read_ltb_string(
            data, cursor, f"animation {animation_index} name"
        )
        compression_type = u32(
            data, cursor, f"animation {animation_index} compression"
        )
        interpolation_ms = u32(
            data, cursor + 4, f"animation {animation_index} interpolation"
        )
        keyframe_count = u32(
            data, cursor + 8, f"animation {animation_index} keyframe count"
        )
        cursor += 12
        if compression_type not in (0, 1, 2, 3):
            raise LtbError(
                f"unsupported animation compression {compression_type} at "
                f"animation {animation_index}"
            )
        if not 1 <= keyframe_count <= MAX_KEYFRAMES:
            raise LtbError(
                f"implausible keyframe count at animation {animation_index}: "
                f"{keyframe_count}"
            )
        times_ms: list[int] = []
        keyframe_strings: list[str] = []
        for keyframe_index in range(keyframe_count):
            time_ms = u32(
                data,
                cursor,
                f"animation {animation_index} keyframe {keyframe_index} time",
            )
            cursor += 4
            keyframe_string, cursor = read_ltb_string(
                data,
                cursor,
                f"animation {animation_index} keyframe {keyframe_index} string",
            )
            if times_ms and time_ms < times_ms[-1]:
                raise LtbError(f"non-monotonic animation time in {name}")
            if times_ms and time_ms == times_ms[-1]:
                duplicate_timestamps += 1
            times_ms.append(time_ms)
            keyframe_strings.append(keyframe_string)

        animation_nodes: list[LtbAnimationNode] = []
        for bone_index in range(bone_count):
            label = f"animation {animation_index} bone {bone_index}"
            vertex_frames: list[list[tuple[float, float, float]]] | None = None
            if compression_type == 0:
                is_vertex_animation = bool(
                    bounded_slice(data, cursor, 1, f"{label} vertex flag")[0]
                )
                cursor += 1
                if is_vertex_animation:
                    vertex_frames = []
                    for keyframe_index in range(keyframe_count):
                        vertex_count = u32(
                            data,
                            cursor,
                            f"{label} frame {keyframe_index} vertex count",
                        )
                        cursor += 4
                        if vertex_count > MAX_VERTICES:
                            raise LtbError(
                                f"implausible vertex animation count in {label}: "
                                f"{vertex_count}"
                            )
                        values = struct.unpack(
                            f"<{vertex_count * 3}f",
                            bounded_slice(
                                data,
                                cursor,
                                vertex_count * 12,
                                f"{label} frame {keyframe_index} vertices",
                            ),
                        )
                        cursor += vertex_count * 12
                        if not all(math.isfinite(value) for value in values):
                            raise LtbError(f"non-finite vertex animation in {label}")
                        vertex_frames.append(
                            [
                                tuple(values[offset : offset + 3])
                                for offset in range(0, len(values), 3)
                            ]
                        )
                    translations = [(0.0, 0.0, 0.0)] * keyframe_count
                    rotations = [(0.0, 0.0, 0.0, 1.0)] * keyframe_count
                    vertex_animation_channels += 1
                else:
                    position_values = struct.unpack(
                        f"<{keyframe_count * 3}f",
                        bounded_slice(
                            data,
                            cursor,
                            keyframe_count * 12,
                            f"{label} positions",
                        ),
                    )
                    cursor += keyframe_count * 12
                    quaternion_values = struct.unpack(
                        f"<{keyframe_count * 4}f",
                        bounded_slice(
                            data,
                            cursor,
                            keyframe_count * 16,
                            f"{label} rotations",
                        ),
                    )
                    cursor += keyframe_count * 16
                    translations = [
                        tuple(position_values[offset : offset + 3])
                        for offset in range(0, len(position_values), 3)
                    ]
                    rotations = [
                        normalize_quaternion(
                            tuple(quaternion_values[offset : offset + 4]), label
                        )
                        for offset in range(0, len(quaternion_values), 4)
                    ]
            else:
                position_count = u32(data, cursor, f"{label} position count")
                cursor += 4
                position_width = 6 if compression_type == 2 else 12
                position_data = bounded_slice(
                    data,
                    cursor,
                    position_count * position_width,
                    f"{label} positions",
                )
                cursor += position_count * position_width
                if compression_type == 2:
                    packed_positions = struct.unpack(
                        f"<{position_count * 3}h", position_data
                    )
                    position_values = [
                        tuple(value / 16.0 for value in packed_positions[offset : offset + 3])
                        for offset in range(0, len(packed_positions), 3)
                    ]
                else:
                    unpacked_positions = struct.unpack(
                        f"<{position_count * 3}f", position_data
                    )
                    position_values = [
                        tuple(unpacked_positions[offset : offset + 3])
                        for offset in range(0, len(unpacked_positions), 3)
                    ]
                quaternion_count = u32(data, cursor, f"{label} rotation count")
                cursor += 4
                quaternion_width = 16 if compression_type == 1 else 8
                quaternion_data = bounded_slice(
                    data,
                    cursor,
                    quaternion_count * quaternion_width,
                    f"{label} rotations",
                )
                cursor += quaternion_count * quaternion_width
                if compression_type == 1:
                    unpacked_quaternions = struct.unpack(
                        f"<{quaternion_count * 4}f", quaternion_data
                    )
                    quaternion_values = [
                        tuple(unpacked_quaternions[offset : offset + 4])
                        for offset in range(0, len(unpacked_quaternions), 4)
                    ]
                else:
                    packed_quaternions = struct.unpack(
                        f"<{quaternion_count * 4}h", quaternion_data
                    )
                    quaternion_values = [
                        tuple(
                            value / 32767.0
                            for value in packed_quaternions[offset : offset + 4]
                        )
                        for offset in range(0, len(packed_quaternions), 4)
                    ]
                translations = expand_animation_channel(
                    position_values,
                    keyframe_count,
                    (0.0, 0.0, 0.0),
                    f"{label} positions",
                )
                rotations = [
                    normalize_quaternion(value, label)
                    for value in expand_animation_channel(
                        quaternion_values,
                        keyframe_count,
                        (0.0, 0.0, 0.0, 1.0),
                        f"{label} rotations",
                    )
                ]
            if not all(
                math.isfinite(value)
                for translation in translations
                for value in translation
            ):
                raise LtbError(f"non-finite animation translation in {label}")
            animation_nodes.append(
                LtbAnimationNode(translations, rotations, vertex_frames)
            )
        animations.append(
            LtbAnimation(
                name,
                compression_type,
                interpolation_ms,
                times_ms,
                keyframe_strings,
                animation_nodes,
            )
        )
        compression_counts[compression_type] += 1
        total_keyframes += keyframe_count

    socket_count = u32(data, cursor, "socket count")
    cursor += 4
    if socket_count > MAX_SOCKETS:
        raise LtbError(f"implausible socket count: {socket_count}")
    for socket_index in range(socket_count):
        node_index = u32(data, cursor, f"socket {socket_index} node")
        cursor += 4
        if node_index >= bone_count:
            raise LtbError(f"out-of-range socket node at {socket_index}")
        _, cursor = read_ltb_string(data, cursor, f"socket {socket_index} name")
        values = struct.unpack(
            "<10f", bounded_slice(data, cursor, 40, f"socket {socket_index} values")
        )
        cursor += 40
        if not all(math.isfinite(value) for value in values):
            raise LtbError(f"non-finite socket values at {socket_index}")

    animation_binding_count = 0
    for child_index in range(child_model_count):
        binding_count = u32(data, cursor, f"child {child_index} binding count")
        cursor += 4
        if binding_count > MAX_ANIMATIONS:
            raise LtbError(
                f"implausible animation binding count for child {child_index}: "
                f"{binding_count}"
            )
        binding_names = []
        for binding_index in range(binding_count):
            binding_name, cursor = read_ltb_string(
                data,
                cursor,
                f"child {child_index} binding {binding_index} name",
            )
            values = struct.unpack(
                "<6f",
                bounded_slice(
                    data,
                    cursor,
                    24,
                    f"child {child_index} binding {binding_index} values",
                ),
            )
            cursor += 24
            if not all(math.isfinite(value) for value in values):
                raise LtbError(
                    f"non-finite animation binding for child {child_index}"
                )
            binding_names.append(binding_name)
            if child_index == 0:
                if binding_index >= len(animations):
                    raise LtbError("self animation binding exceeds animation list")
                animations[binding_index].root_translation = tuple(values[3:6])
        if child_index == 0:
            animation_names = [animation.name for animation in animations]
            if binding_names != animation_names:
                raise LtbError("self animation bindings do not match animation list")
        animation_binding_count += binding_count

    details = {
        "weight_set_count": weight_set_count,
        "child_model_count": child_model_count,
        "child_model_names": child_model_names,
        "animation_count": animation_count,
        "animation_keyframes": total_keyframes,
        "animation_compression_counts": {
            str(key): value for key, value in sorted(compression_counts.items())
        },
        "duplicate_animation_timestamps": duplicate_timestamps,
        "vertex_animation_channels": vertex_animation_channels,
        "socket_count": socket_count,
        "animation_binding_count": animation_binding_count,
    }
    return animations, details, cursor


def vertex_stream_layout(
    flags: int, blend_type: int, label: str
) -> tuple[int, int | None, int | None, int | None]:
    """Mirror CrossFire's GetVertexFlags_and_Size for one D3D vertex stream."""
    if flags & ~VERTDATATYPE_KNOWN_MASK:
        raise LtbError(f"{label} has unknown vertex flags 0x{flags:08x}")
    uv_sets = 0
    for bit, count in (
        (VERTDATATYPE_UVSETS_1, 1),
        (VERTDATATYPE_UVSETS_2, 2),
        (VERTDATATYPE_UVSETS_3, 3),
        (VERTDATATYPE_UVSETS_4, 4),
    ):
        if flags & bit:
            uv_sets = count
            break
    position_offset = None
    normal_offset = None
    size = 0
    if flags & VERTDATATYPE_POSITION and flags & VERTDATATYPE_NORMAL:
        position_offset = 0
        weight_floats = {
            BLEND_NONE: 0,
            BLEND_NONINDEXED_B1: 1,
            BLEND_NONINDEXED_B2: 2,
            BLEND_NONINDEXED_B3: 3,
            BLEND_INDEXED_B1: 1,
            BLEND_INDEXED_B2: 2,
            BLEND_INDEXED_B3: 3,
        }[blend_type]
        indexed_bytes = 4 if blend_type >= BLEND_INDEXED_B1 else 0
        normal_offset = 12 + weight_floats * 4 + indexed_bytes
        size = normal_offset + 12
    uv_offset = size if uv_sets else None
    size += uv_sets * 8
    if flags & VERTDATATYPE_BASISVECTORS:
        size += 24
    if flags and not size:
        raise LtbError(f"{label} has no runtime-readable vertex payload")
    return size, position_offset, normal_offset, uv_offset


def parse_vertex_streams(
    data: bytes,
    cursor: int,
    vertex_count: int,
    stream_flags: tuple[int, int, int, int],
    blend_type: int,
    label: str,
) -> tuple[
    list[tuple[float, float, float]],
    list[tuple[float, float, float]],
    list[tuple[float, float]],
    int,
    list[dict[str, int]],
    set[int],
    list[tuple[float, float, float, float]] | None,
    list[tuple[int, int, int, int]] | None,
]:
    positions: list[tuple[float, float, float] | None] = [None] * vertex_count
    normals: list[tuple[float, float, float] | None] = [None] * vertex_count
    texcoords: list[tuple[float, float] | None] = [None] * vertex_count
    nonfinite_normal_vertices: set[int] = set()
    blend_weights: list[tuple[float, float, float, float]] | None = None
    blend_indices: list[tuple[int, int, int, int]] | None = None
    stream_details = []
    for stream_index, flags in enumerate(stream_flags):
        if not flags:
            continue
        stream_label = f"{label} vertex stream {stream_index}"
        stride, position_offset, normal_offset, uv_offset = vertex_stream_layout(
            flags, blend_type, stream_label
        )
        bounded_slice(data, cursor, vertex_count * stride, stream_label)
        for vertex_index in range(vertex_count):
            base = cursor + vertex_index * stride
            if position_offset is not None:
                position = struct.unpack_from("<3f", data, base + position_offset)
                if not all(math.isfinite(value) for value in position):
                    raise LtbError(
                        f"non-finite position in {stream_label} at vertex "
                        f"{vertex_index}"
                    )
                if positions[vertex_index] is None:
                    positions[vertex_index] = position
                explicit_weight_count = {
                    BLEND_NONE: 0,
                    BLEND_NONINDEXED_B1: 1,
                    BLEND_NONINDEXED_B2: 2,
                    BLEND_NONINDEXED_B3: 3,
                    BLEND_INDEXED_B1: 1,
                    BLEND_INDEXED_B2: 2,
                    BLEND_INDEXED_B3: 3,
                }[blend_type]
                if explicit_weight_count:
                    explicit = struct.unpack_from(
                        f"<{explicit_weight_count}f", data, base + 12
                    )
                    if blend_weights is None:
                        blend_weights = [
                            (0.0, 0.0, 0.0, 0.0) for _ in range(vertex_count)
                        ]
                    blend_weights[vertex_index] = complete_blend_weights(
                        explicit, stream_label, vertex_index
                    )
                    if blend_type >= BLEND_INDEXED_B1:
                        if blend_indices is None:
                            blend_indices = [
                                (0, 0, 0, 0) for _ in range(vertex_count)
                            ]
                        blend_indices[vertex_index] = struct.unpack_from(
                            "<4B", data, base + 12 + explicit_weight_count * 4
                        )
            if normal_offset is not None:
                normal = struct.unpack_from("<3f", data, base + normal_offset)
                if all(math.isfinite(value) for value in normal):
                    normal_length = math.sqrt(
                        sum(value * value for value in normal)
                    )
                    if normal_length > 1e-12:
                        normal = tuple(value / normal_length for value in normal)
                        if normals[vertex_index] is None:
                            normals[vertex_index] = normal
                    else:
                        nonfinite_normal_vertices.add(vertex_index)
                        normal = None
                else:
                    nonfinite_normal_vertices.add(vertex_index)
                    normal = None
                if normal is not None and normals[vertex_index] is None:
                    normals[vertex_index] = normal
            if uv_offset is not None:
                uv = struct.unpack_from("<2f", data, base + uv_offset)
                if not all(math.isfinite(value) for value in uv):
                    raise LtbError(
                        f"non-finite UV in {stream_label} at vertex {vertex_index}"
                    )
                if texcoords[vertex_index] is None:
                    texcoords[vertex_index] = uv
        stream_details.append(
            {"stream": stream_index, "flags": flags, "stride": stride}
        )
        cursor += vertex_count * stride
    if any(value is None for value in positions):
        raise LtbError(f"{label} has no position stream")
    if any(
        value is None and index not in nonfinite_normal_vertices
        for index, value in enumerate(normals)
    ):
        raise LtbError(f"{label} has no normal stream")
    if any(value is None for value in texcoords):
        raise LtbError(f"{label} has no UV stream")
    return (
        [value for value in positions if value is not None],
        [value if value is not None else (math.nan, math.nan, math.nan) for value in normals],
        [value for value in texcoords if value is not None],
        cursor,
        stream_details,
        nonfinite_normal_vertices,
        blend_weights,
        blend_indices,
    )


def parse_triangle_indices(
    data: bytes,
    cursor: int,
    vertex_count: int,
    triangle_count: int,
    label: str,
) -> tuple[list[int], int]:
    index_count = triangle_count * 3
    index_data = bounded_slice(data, cursor, index_count * 2, f"{label} index buffer")
    indices = list(struct.unpack(f"<{index_count}H", index_data)) if index_count else []
    if any(index >= vertex_count for index in indices):
        raise LtbError(f"out-of-range triangle index in {label}")
    return indices, cursor + index_count * 2


def repair_nonfinite_normals(
    positions: list[tuple[float, float, float]],
    normals: list[tuple[float, float, float]],
    indices: list[int],
    invalid_vertices: set[int],
    label: str,
) -> tuple[list[tuple[float, float, float]], int, int]:
    if not invalid_vertices:
        return normals, 0, 0
    accumulated = {vertex: [0.0, 0.0, 0.0] for vertex in invalid_vertices}
    neighbor_normals = {vertex: [0.0, 0.0, 0.0] for vertex in invalid_vertices}
    for index in range(0, len(indices), 3):
        triangle = indices[index : index + 3]
        if not any(vertex in invalid_vertices for vertex in triangle):
            continue
        p0, p1, p2 = (positions[vertex] for vertex in triangle)
        edge1 = tuple(p1[axis] - p0[axis] for axis in range(3))
        edge2 = tuple(p2[axis] - p0[axis] for axis in range(3))
        face = (
            edge1[1] * edge2[2] - edge1[2] * edge2[1],
            edge1[2] * edge2[0] - edge1[0] * edge2[2],
            edge1[0] * edge2[1] - edge1[1] * edge2[0],
        )
        if not all(math.isfinite(value) for value in face):
            raise LtbError(f"non-finite derived face normal in {label}")
        for vertex in triangle:
            if vertex in accumulated:
                for axis in range(3):
                    accumulated[vertex][axis] += face[axis]
                for neighbor in triangle:
                    if neighbor in invalid_vertices:
                        continue
                    for axis in range(3):
                        neighbor_normals[vertex][axis] += normals[neighbor][axis]
    repaired = list(normals)
    fallback_count = 0
    for vertex, value in accumulated.items():
        length = math.sqrt(sum(component * component for component in value))
        if not math.isfinite(length) or length <= 1e-12:
            value = neighbor_normals[vertex]
            length = math.sqrt(sum(component * component for component in value))
            fallback_count += 1
        if not math.isfinite(length) or length <= 1e-12:
            value = [0.0, 0.0, 1.0]
            length = 1.0
        repaired[vertex] = tuple(component / length for component in value)
    return repaired, len(invalid_vertices), fallback_count


def remove_unreferenced_nonfinite_normal_vertices(
    positions: list[tuple[float, float, float]],
    normals: list[tuple[float, float, float]],
    texcoords: list[tuple[float, float]],
    indices: list[int],
    invalid_vertices: set[int],
) -> tuple[
    list[tuple[float, float, float]],
    list[tuple[float, float, float]],
    list[tuple[float, float]],
    list[int],
    set[int],
    int,
]:
    referenced = set(indices)
    removable = invalid_vertices - referenced
    if not removable:
        return positions, normals, texcoords, indices, invalid_vertices, 0
    remap: dict[int, int] = {}
    filtered_positions = []
    filtered_normals = []
    filtered_texcoords = []
    for old_index, (position, normal, texcoord) in enumerate(
        zip(positions, normals, texcoords)
    ):
        if old_index in removable:
            continue
        remap[old_index] = len(filtered_positions)
        filtered_positions.append(position)
        filtered_normals.append(normal)
        filtered_texcoords.append(texcoord)
    filtered_indices = [remap[index] for index in indices]
    filtered_invalid = {remap[index] for index in invalid_vertices - removable}
    return (
        filtered_positions,
        filtered_normals,
        filtered_texcoords,
        filtered_indices,
        filtered_invalid,
        len(removable),
    )


def parse_crossfire_composite_ltb(
    data: bytes,
    command_length: int,
    top_mesh_count: int,
    first_mesh_cursor: int | None = None,
    oriented_bounding_box_count: int = 0,
) -> tuple[list[LtbMesh], dict[str, Any]]:
    """Parse CrossFire's ModelPiece/LOD/render-object layout and skeleton."""
    cursor = first_mesh_cursor if first_mesh_cursor is not None else 98 + command_length
    meshes: list[LtbMesh] = []
    render_object_type_counts: Counter[int] = Counter()
    submesh_slots = 0
    empty_submeshes = 0
    matrix_palette_submeshes = 0
    reindexed_bone_entries = 0
    vertex_animation_submeshes = 0
    repaired_normal_vertices = 0
    fallback_normal_vertices = 0
    removed_unreferenced_nonfinite_normal_vertices = 0
    stream_layout_counts: Counter[str] = Counter()
    bone_count = u32(data, 32, "bone count")
    if bone_count > 100_000:
        raise LtbError(f"implausible bone count: {bone_count}")
    for top_index in range(top_mesh_count):
        name_length = u16(data, cursor, f"top mesh {top_index} name length")
        cursor += 2
        name = bounded_slice(
            data, cursor, name_length, f"top mesh {top_index} name"
        ).decode("cp1252", errors="replace")
        cursor += name_length
        submesh_count = u32(data, cursor, f"top mesh {top_index} submesh count")
        cursor += 4
        if submesh_count > MAX_MESHES or submesh_slots + submesh_count > MAX_MESHES:
            raise LtbError(
                f"implausible composite submesh count at top mesh {top_index}: "
                f"{submesh_count}"
            )
        submesh_slots += submesh_count
        bounded_slice(
            data,
            cursor,
            submesh_count * 4 + 8,
            f"top mesh {top_index} LOD table",
        )
        cursor += submesh_count * 4 + 8
        for sub_index in range(submesh_count):
            label = f"submesh {top_index}/{sub_index}"
            bone_effector: int | None = None
            matrix_palette = False
            reindexed: tuple[int, ...] = ()
            animation_node: int | None = None
            unduplicated_vertex_count: int | None = None
            duplicate_map: list[tuple[int, int]] | None = None
            bounded_slice(data, cursor, 29, f"{label} header")
            texture_count = u32(data, cursor, f"{label} texture count")
            if texture_count > 4:
                raise LtbError(f"implausible texture count in {label}: {texture_count}")
            cursor += 4
            texture_indices = struct.unpack(
                "<4I", bounded_slice(data, cursor, 16, f"{label} textures")
            )
            cursor += 16
            render_style = u32(data, cursor, f"{label} render style")
            cursor += 4
            render_priority = bounded_slice(data, cursor, 1, f"{label} priority")[0]
            cursor += 1
            render_object_type = u32(data, cursor, f"{label} render object type")
            cursor += 4
            object_size = u32(data, cursor, f"{label} object size")
            cursor += 4
            object_end = cursor + object_size
            bounded_slice(data, cursor, object_size, f"{label} render object")
            if render_object_type == RENDER_OBJECT_NULL:
                cursor = object_end
                empty_submeshes += 1
            elif render_object_type == RENDER_OBJECT_RIGID:
                vertex_count = u32(data, cursor, f"{label} vertex count")
                triangle_count = u32(data, cursor + 4, f"{label} triangle count")
                max_bones_per_triangle = u32(
                    data, cursor + 8, f"{label} max bones per triangle"
                )
                max_bones_per_vertex = u32(
                    data, cursor + 12, f"{label} max bones per vertex"
                )
                stream_flags = struct.unpack(
                    "<4I", bounded_slice(data, cursor + 16, 16, f"{label} streams")
                )
                bone_effector = u32(data, cursor + 32, f"{label} bone effector")
                cursor += 36
                if max_bones_per_triangle != 1 or max_bones_per_vertex != 1:
                    raise LtbError(f"invalid rigid bone limits in {label}")
                blend_type = BLEND_NONE
            elif render_object_type == RENDER_OBJECT_SKELETAL:
                vertex_count = u32(data, cursor, f"{label} vertex count")
                triangle_count = u32(data, cursor + 4, f"{label} triangle count")
                max_bones_per_triangle = u32(
                    data, cursor + 8, f"{label} max bones per triangle"
                )
                max_bones_per_vertex = u32(
                    data, cursor + 12, f"{label} max bones per vertex"
                )
                reindexed_bones = bool(
                    bounded_slice(data, cursor + 16, 1, f"{label} reindex flag")[0]
                )
                stream_flags = struct.unpack(
                    "<4I", bounded_slice(data, cursor + 17, 16, f"{label} streams")
                )
                matrix_palette = bool(
                    bounded_slice(data, cursor + 33, 1, f"{label} palette flag")[0]
                )
                cursor += 34
                if matrix_palette:
                    matrix_palette_submeshes += 1
                    minimum_bone = u32(data, cursor, f"{label} minimum bone")
                    maximum_bone = u32(data, cursor + 4, f"{label} maximum bone")
                    cursor += 8
                    if maximum_bone < minimum_bone or maximum_bone >= max(bone_count, 1):
                        raise LtbError(
                            f"invalid matrix palette bone range in {label}: "
                            f"{minimum_bone}/{maximum_bone}"
                        )
                    if reindexed_bones:
                        reindexed_count = u32(
                            data, cursor, f"{label} reindexed bone count"
                        )
                        cursor += 4
                        if reindexed_count > bone_count + 1:
                            raise LtbError(
                                f"implausible reindexed bone count in {label}: "
                                f"{reindexed_count}"
                            )
                        reindexed = struct.unpack(
                            f"<{reindexed_count}I",
                            bounded_slice(
                                data,
                                cursor,
                                reindexed_count * 4,
                                f"{label} reindexed bones",
                            ),
                        )
                        if any(index >= bone_count for index in reindexed):
                            raise LtbError(f"out-of-range reindexed bone in {label}")
                        cursor += reindexed_count * 4
                        reindexed_bone_entries += reindexed_count
                    blend_type = {
                        2: BLEND_INDEXED_B1,
                        3: BLEND_INDEXED_B2,
                        4: BLEND_INDEXED_B3,
                    }.get(max_bones_per_vertex, -1)
                else:
                    blend_type = {
                        1: BLEND_NONE,
                        2: BLEND_NONINDEXED_B1,
                        3: BLEND_NONINDEXED_B2,
                        4: BLEND_NONINDEXED_B3,
                    }.get(max_bones_per_triangle, -1)
                if blend_type < 0:
                    raise LtbError(f"unsupported skeletal blend layout in {label}")
            elif render_object_type == RENDER_OBJECT_VERTEX_ANIMATED:
                vertex_animation_submeshes += 1
                vertex_count = u32(data, cursor, f"{label} vertex count")
                unduplicated_vertex_count = u32(
                    data, cursor + 4, f"{label} unduplicated vertex count"
                )
                triangle_count = u32(data, cursor + 8, f"{label} triangle count")
                max_bones_per_triangle = u32(
                    data, cursor + 12, f"{label} max bones per triangle"
                )
                max_bones_per_vertex = u32(
                    data, cursor + 16, f"{label} max bones per vertex"
                )
                stream_flags = struct.unpack(
                    "<4I", bounded_slice(data, cursor + 20, 16, f"{label} streams")
                )
                animation_node = u32(data, cursor + 36, f"{label} animation node")
                bone_effector = u32(data, cursor + 40, f"{label} bone effector")
                cursor += 44
                if unduplicated_vertex_count > vertex_count:
                    raise LtbError(f"invalid unduplicated vertex count in {label}")
                if animation_node >= bone_count:
                    raise LtbError(f"out-of-range animation node in {label}")
                blend_type = BLEND_NONE
            else:
                raise LtbError(
                    f"unsupported render object type {render_object_type} in {label}"
                )

            if render_object_type != RENDER_OBJECT_NULL:
                if vertex_count > MAX_VERTICES or triangle_count > MAX_FACES:
                    raise LtbError(
                        f"{label} exceeds bounded counts: "
                        f"{vertex_count}/{triangle_count}"
                    )
                (
                    positions,
                    normals,
                    texcoords,
                    cursor,
                    stream_details,
                    nonfinite_normal_vertices,
                    blend_weights,
                    blend_indices,
                ) = (
                    parse_vertex_streams(
                        data,
                        cursor,
                        vertex_count,
                        stream_flags,
                        blend_type,
                        label,
                    )
                )
                for detail in stream_details:
                    stream_layout_counts[
                        f"0x{detail['flags']:x}/{detail['stride']}"
                    ] += 1
                indices, cursor = parse_triangle_indices(
                    data, cursor, vertex_count, triangle_count, label
                )
                joints: list[tuple[int, int, int, int]] | None = None
                weights: list[tuple[float, float, float, float]] | None = None
                if (
                    render_object_type == RENDER_OBJECT_SKELETAL
                    and not matrix_palette
                ):
                    bone_set_count = u32(data, cursor, f"{label} bone set count")
                    cursor += 4
                    if bone_set_count > triangle_count + 1:
                        raise LtbError(
                            f"implausible bone set count in {label}: {bone_set_count}"
                        )
                    bone_set_data = bounded_slice(
                        data, cursor, bone_set_count * 12, f"{label} bone sets"
                    )
                    if blend_weights is None:
                        if max_bones_per_triangle != 1:
                            raise LtbError(f"missing direct blend weights in {label}")
                        blend_weights = [
                            (1.0, 0.0, 0.0, 0.0) for _ in range(vertex_count)
                        ]
                    joints = [(0, 0, 0, 0) for _ in range(vertex_count)]
                    assigned = [False] * vertex_count
                    previous_index_end = 0
                    for bone_set_index in range(bone_set_count):
                        first_vertex, set_vertex_count, *rest = struct.unpack_from(
                            "<HH4BI", bone_set_data, bone_set_index * 12
                        )
                        bone_slots = tuple(rest[:4])
                        index_end = rest[4]
                        if (
                            first_vertex + set_vertex_count > vertex_count
                            or index_end < previous_index_end
                            or index_end > len(indices)
                            or index_end % 3
                        ):
                            raise LtbError(f"invalid direct bone set in {label}")
                        previous_index_end = index_end
                        mapped_slots = tuple(0 if bone == 0xFF else bone for bone in bone_slots)
                        if any(
                            bone != 0xFF and bone >= bone_count for bone in bone_slots
                        ):
                            raise LtbError(f"out-of-range direct bone in {label}")
                        for vertex_index in range(
                            first_vertex, first_vertex + set_vertex_count
                        ):
                            if assigned[vertex_index]:
                                raise LtbError(f"overlapping direct bone sets in {label}")
                            for slot, weight in zip(
                                bone_slots, blend_weights[vertex_index]
                            ):
                                if slot == 0xFF and weight > 1e-5:
                                    raise LtbError(
                                        f"weighted empty direct bone slot in {label}"
                                    )
                            joints[vertex_index] = mapped_slots
                            assigned[vertex_index] = True
                    if bone_set_count and previous_index_end != len(indices):
                        raise LtbError(f"incomplete direct bone-set index coverage in {label}")
                    if any(not assigned[index] for index in set(indices)):
                        raise LtbError(f"unassigned referenced direct vertex in {label}")
                    weights = blend_weights
                    cursor += bone_set_count * 12
                elif render_object_type == RENDER_OBJECT_SKELETAL:
                    if blend_weights is None or blend_indices is None:
                        raise LtbError(f"missing matrix-palette skin data in {label}")
                    joints = []
                    for vertex_index, local_joints in enumerate(blend_indices):
                        if reindexed_bones and any(
                            index >= len(reindexed)
                            and weight > 1e-5
                            for index, weight in zip(
                                local_joints, blend_weights[vertex_index]
                            )
                        ):
                            raise LtbError(
                                f"out-of-range reindexed palette joint in {label}"
                            )
                        mapped = tuple(
                            (
                                reindexed[index]
                                if reindexed_bones and index < len(reindexed)
                                else index if not reindexed_bones else 0
                            )
                            for index in local_joints
                        )
                        if any(
                            joint >= bone_count and weight > 1e-5
                            for joint, weight in zip(
                                mapped, blend_weights[vertex_index]
                            )
                        ):
                            raise LtbError(f"out-of-range matrix-palette joint in {label}")
                        joints.append(mapped)
                    weights = blend_weights
                elif render_object_type == RENDER_OBJECT_VERTEX_ANIMATED:
                    duplicate_count = u32(data, cursor, f"{label} duplicate map count")
                    cursor += 4
                    if duplicate_count > vertex_count:
                        raise LtbError(
                            f"implausible duplicate map count in {label}: "
                            f"{duplicate_count}"
                        )
                    duplicate_data = bounded_slice(
                        data, cursor, duplicate_count * 4, f"{label} duplicate map"
                    )
                    duplicate_map = []
                    for duplicate_index in range(duplicate_count):
                        source, destination = struct.unpack_from(
                            "<HH", duplicate_data, duplicate_index * 4
                        )
                        if source >= vertex_count or destination >= vertex_count:
                            raise LtbError(f"out-of-range duplicate map in {label}")
                        duplicate_map.append((source, destination))
                    cursor += duplicate_count * 4
                elif render_object_type == RENDER_OBJECT_RIGID:
                    if bone_effector is None or bone_effector >= bone_count:
                        raise LtbError(f"out-of-range rigid bone effector in {label}")
                    joints = [(bone_effector, 0, 0, 0)] * vertex_count
                    weights = [(1.0, 0.0, 0.0, 0.0)] * vertex_count

                if joints is not None and weights is not None:
                    joints = [
                        tuple(
                            joint if weight > 1e-7 else 0
                            for joint, weight in zip(vertex_joints, vertex_weights)
                        )
                        for vertex_joints, vertex_weights in zip(joints, weights)
                    ]

                removable_vertices = nonfinite_normal_vertices - set(indices)
                retained_vertex_indices = [
                    index for index in range(vertex_count) if index not in removable_vertices
                ]
                (
                    positions,
                    normals,
                    texcoords,
                    indices,
                    nonfinite_normal_vertices,
                    removed_count,
                ) = remove_unreferenced_nonfinite_normal_vertices(
                    positions,
                    normals,
                    texcoords,
                    indices,
                    nonfinite_normal_vertices,
                )
                if removable_vertices and joints is not None and weights is not None:
                    joints = [
                        value
                        for index, value in enumerate(joints)
                        if index not in removable_vertices
                    ]
                    weights = [
                        value
                        for index, value in enumerate(weights)
                        if index not in removable_vertices
                    ]
                removed_unreferenced_nonfinite_normal_vertices += removed_count
                normals, repaired_count, fallback_count = repair_nonfinite_normals(
                    positions,
                    normals,
                    indices,
                    nonfinite_normal_vertices,
                    label,
                )
                repaired_normal_vertices += repaired_count
                fallback_normal_vertices += fallback_count
                if cursor != object_end:
                    raise LtbError(
                        f"{label} object size mismatch: parsed={cursor} "
                        f"expected={object_end}"
                    )
                mesh_name = name if submesh_count == 1 else f"{name}#{sub_index}"
                meshes.append(
                    LtbMesh(
                        mesh_name,
                        render_object_type,
                        positions,
                        normals,
                        texcoords,
                        indices,
                        joints,
                        weights,
                        animation_node,
                        unduplicated_vertex_count,
                        duplicate_map,
                        (
                            retained_vertex_indices
                            if render_object_type == RENDER_OBJECT_VERTEX_ANIMATED
                            else None
                        ),
                        (
                            vertex_count
                            if render_object_type == RENDER_OBJECT_VERTEX_ANIMATED
                            else None
                        ),
                    )
                )
                render_object_type_counts[render_object_type] += 1
            used_node_count = bounded_slice(
                data, cursor, 1, f"{label} used node count"
            )[0]
            cursor += 1
            used_nodes = bounded_slice(
                data, cursor, used_node_count, f"{label} used nodes"
            )
            if any(node >= bone_count for node in used_nodes):
                raise LtbError(f"out-of-range used node in {label}")
            cursor += used_node_count

    if not any(mesh.positions and mesh.indices for mesh in meshes):
        raise LtbError("composite layout contains no non-empty triangle mesh")
    bone_names = []
    bone_node_indices = []
    bone_flags = []
    bone_child_counts = []
    bone_matrices = []
    for bone_index in range(bone_count):
        name_length = u16(data, cursor, f"bone {bone_index} name length")
        cursor += 2
        bone_name = bounded_slice(
            data, cursor, name_length, f"bone {bone_index} name"
        ).decode("cp1252", errors="replace")
        cursor += name_length
        node_index, node_flags = struct.unpack(
            "<HB", bounded_slice(data, cursor, 3, f"bone {bone_index} flags")
        )
        cursor += 3
        if node_index != bone_index:
            raise LtbError(
                f"unexpected preorder node index at bone {bone_index}: {node_index}"
            )
        matrix = struct.unpack_from(
            "<16f", bounded_slice(data, cursor, 64, f"bone {bone_index} matrix")
        )
        cursor += 64
        if not all(math.isfinite(value) for value in matrix):
            raise LtbError(f"non-finite bone matrix at bone {bone_index}")
        child_count = u32(data, cursor, f"bone {bone_index} child count")
        cursor += 4
        if child_count > bone_count:
            raise LtbError(f"implausible child count at bone {bone_index}: {child_count}")
        bone_names.append(bone_name)
        bone_node_indices.append(node_index)
        bone_flags.append(node_flags)
        bone_child_counts.append(child_count)
        bone_matrices.append(list(matrix))
    bone_parents = [-1] * bone_count
    remaining_children = bone_child_counts.copy()
    for bone_index in range(1, bone_count):
        for parent in range(bone_index - 1, -1, -1):
            if remaining_children[parent] > 0:
                remaining_children[parent] -= 1
                bone_parents[bone_index] = parent
                break
        if bone_parents[bone_index] < 0:
            raise LtbError(f"bone hierarchy has no parent for bone {bone_index}")
    if bone_count and sum(bone_child_counts) != bone_count - 1:
        raise LtbError(
            f"bone hierarchy child total mismatch: {sum(bone_child_counts)}/{bone_count - 1}"
        )
    animations, tail_details, cursor = parse_composite_tail(
        data, cursor, bone_count
    )
    if cursor != len(data):
        raise LtbError(f"unparsed composite tail bytes: {len(data) - cursor}")
    details = {
        "header": [1, 9],
        "layout": "crossfire_top_mesh_submesh",
        "mesh_count": len(meshes),
        "skinned_mesh_count": sum(mesh.joints is not None for mesh in meshes),
        "top_mesh_count": top_mesh_count,
        "oriented_bounding_box_count": oriented_bounding_box_count,
        "submesh_slots": submesh_slots,
        "empty_submeshes": empty_submeshes,
        "vertex_count": sum(len(mesh.positions) for mesh in meshes),
        "triangle_count": sum(len(mesh.indices) // 3 for mesh in meshes),
        "render_object_type_counts": {
            str(key): value
            for key, value in sorted(render_object_type_counts.items())
        },
        "matrix_palette_submeshes": matrix_palette_submeshes,
        "reindexed_bone_entries": reindexed_bone_entries,
        "vertex_animation_submeshes": vertex_animation_submeshes,
        "repaired_normal_vertices": repaired_normal_vertices,
        "fallback_normal_vertices": fallback_normal_vertices,
        "removed_unreferenced_nonfinite_normal_vertices": (
            removed_unreferenced_nonfinite_normal_vertices
        ),
        "vertex_stream_layout_counts": dict(sorted(stream_layout_counts.items())),
        "skeleton_metadata": {
            "bone_count": bone_count,
            "names": bone_names,
            "node_indices": bone_node_indices,
            "flags": bone_flags,
            "parent_indices": bone_parents,
            "child_counts": bone_child_counts,
            "bind_matrices": bone_matrices,
        },
        **tail_details,
        "_animations": animations,
        "parsed_bytes": cursor,
        "trailing_bytes": len(data) - cursor,
    }
    return meshes, details


def parse_ltb(data: bytes) -> tuple[list[LtbMesh], dict[str, Any]]:
    if len(data) < 98 or struct.unpack_from("<HH", data, 0) != (1, 9):
        raise LtbError("not a supported LithTech LTB model header (expected 1,9)")
    command_length = u16(data, 84, "command length")
    command = bounded_slice(data, 86, command_length, "command line").decode(
        "cp1252", errors="replace"
    )
    header_cursor = 86 + command_length
    global_radius = struct.unpack(
        "<f", bounded_slice(data, header_cursor, 4, "global radius")
    )[0]
    if not math.isfinite(global_radius) or global_radius < 0:
        raise LtbError(f"invalid global radius: {global_radius}")
    header_cursor += 4
    oriented_bounding_box_count = u32(data, header_cursor, "OBB count")
    header_cursor += 4
    bone_count = u32(data, 32, "bone count")
    if oriented_bounding_box_count > bone_count:
        raise LtbError(
            f"implausible OBB count: {oriented_bounding_box_count}/{bone_count}"
        )
    for obb_index in range(oriented_bounding_box_count):
        record = bounded_slice(data, header_cursor, 68, f"OBB {obb_index}")
        values = struct.unpack_from("<15f", record)
        parent_node = struct.unpack_from("<I", record, 60)[0]
        radius = struct.unpack_from("<f", record, 64)[0]
        if not all(math.isfinite(value) for value in (*values, radius)):
            raise LtbError(f"non-finite OBB data at index {obb_index}")
        if parent_node >= bone_count:
            raise LtbError(f"out-of-range OBB parent at index {obb_index}")
        if radius < 0:
            raise LtbError(f"negative OBB radius at index {obb_index}")
        header_cursor += 68
    mesh_count = u32(data, header_cursor, "mesh count")
    header_cursor += 4
    if mesh_count > MAX_MESHES:
        raise LtbError(f"implausible LTB mesh count: {mesh_count}")
    first_mesh_cursor = header_cursor

    def mesh_candidates(mesh_index: int, cursor: int):
        name_length = u16(data, cursor, f"mesh {mesh_index} name length")
        cursor += 2
        name = bounded_slice(
            data, cursor, name_length, f"mesh {mesh_index} name"
        ).decode("cp1252", errors="replace")
        cursor += name_length
        base = cursor
        vertex_count = u16(data, base + 49, f"mesh {mesh_index} vertex count")
        face_count = u16(data, base + 53, f"mesh {mesh_index} face count")
        if vertex_count > MAX_VERTICES or face_count > MAX_FACES:
            raise LtbError(
                f"mesh {mesh_index} exceeds bounded counts: {vertex_count}/{face_count}"
            )
        mesh_type_head = u16(data, base + 57, f"mesh {mesh_index} type head")
        legacy_mesh_type = (
            5
            if mesh_type_head == 3
            else u16(data, base + 61, f"mesh {mesh_index} type")
        )
        index_count = face_count * 3
        probe = bounded_slice(data, base + 83, 4, f"mesh {mesh_index} geometry probe")
        preferred_offset = (
            85 if probe[0] == 0 and probe[1] == 0 and probe[2] and probe[3] else 83
        )

        def try_geometry(mesh_type: int, geometry_offset: int):
            include_weights = mesh_type in {3, 4}
            include_post_data = mesh_type != 1
            extra_float_bytes = 4 if mesh_type == 2 else 8 if mesh_type == 5 else 0
            stride = 12 + (12 if include_weights else 0) + 12 + extra_float_bytes + 8
            candidate_cursor = base + geometry_offset
            candidate_positions = []
            candidate_normals = []
            candidate_texcoords = []
            bounded_slice(
                data,
                candidate_cursor,
                vertex_count * stride,
                f"mesh {mesh_index} vertex buffer",
            )
            for _ in range(vertex_count):
                position = struct.unpack_from("<3f", data, candidate_cursor)
                candidate_cursor += 12
                if include_weights:
                    candidate_cursor += 12
                normal = struct.unpack_from("<3f", data, candidate_cursor)
                candidate_cursor += 12 + extra_float_bytes
                uv = struct.unpack_from("<2f", data, candidate_cursor)
                candidate_cursor += 8
                if not all(math.isfinite(value) for value in (*position, *normal, *uv)):
                    raise LtbError(f"non-finite vertex data in mesh {mesh_index}")
                candidate_positions.append(position)
                candidate_normals.append(normal)
                candidate_texcoords.append(uv)
            index_data = bounded_slice(
                data,
                candidate_cursor,
                index_count * 2,
                f"mesh {mesh_index} index buffer",
            )
            candidate_indices = (
                list(struct.unpack(f"<{index_count}H", index_data))
                if index_count
                else []
            )
            candidate_cursor += index_count * 2
            if any(index >= vertex_count for index in candidate_indices):
                raise LtbError(f"out-of-range triangle index in mesh {mesh_index}")
            if include_post_data:
                section_count = u32(
                    data, candidate_cursor, f"mesh {mesh_index} post section count"
                )
                candidate_cursor += 4
                if section_count > 1_000_000:
                    raise LtbError(f"implausible post section count: {section_count}")
                bounded_slice(
                    data,
                    candidate_cursor,
                    section_count * 12,
                    f"mesh {mesh_index} post sections",
                )
                candidate_cursor += section_count * 12
                final_size = bounded_slice(
                    data, candidate_cursor, 1, "final section size"
                )[0]
                candidate_cursor += 1
                bounded_slice(data, candidate_cursor, final_size, "final section")
                candidate_cursor += final_size
            else:
                bounded_slice(
                    data, candidate_cursor, 2, f"mesh {mesh_index} terminator"
                )
                candidate_cursor += 2
            return (
                candidate_positions,
                candidate_normals,
                candidate_texcoords,
                candidate_indices,
                candidate_cursor,
            )

        # LTB2X documents the field at +61 as the primary type, with the
        # special case head=3 -> type 5.  CrossFire also contains mismatched
        # pairs such as head/type 4/2 and 2/1.  Their bounded vertex layouts
        # match the head field, so prefer that in mismatched records and retain
        # the legacy interpretation as a fully validated fallback.
        type_candidates = []
        head_mesh_type = 5 if mesh_type_head == 3 else mesh_type_head
        for candidate in (head_mesh_type, legacy_mesh_type):
            if candidate in {1, 2, 3, 4, 5} and candidate not in type_candidates:
                type_candidates.append(candidate)
        if not type_candidates:
            raise LtbError(
                f"unsupported LTB mesh type {legacy_mesh_type} at mesh {mesh_index}"
            )
        geometry_errors = []
        candidates = []
        for mesh_type in type_candidates:
            for geometry_offset in (
                preferred_offset,
                85 if preferred_offset == 83 else 83,
            ):
                try:
                    positions, normals, texcoords, indices, cursor = try_geometry(
                        mesh_type, geometry_offset
                    )
                    candidates.append(
                        (
                            LtbMesh(
                                name,
                                mesh_type,
                                positions,
                                normals,
                                texcoords,
                                indices,
                            ),
                            cursor,
                        )
                    )
                except (LtbError, struct.error) as error:
                    geometry_errors.append(error)
        if not candidates:
            raise geometry_errors[0]
        # Equivalent type interpretations can produce the same geometry and
        # next cursor.  Keep a single deterministic candidate before bounded
        # whole-mesh-list backtracking.
        unique = []
        seen = set()
        for candidate_mesh, candidate_cursor in candidates:
            key = (
                candidate_mesh.mesh_type,
                candidate_cursor,
                tuple(candidate_mesh.indices[:12]),
            )
            if key not in seen:
                seen.add(key)
                unique.append((candidate_mesh, candidate_cursor))
        return unique

    failed_states: dict[tuple[int, int], LtbError] = {}

    def parse_from(mesh_index: int, cursor: int):
        if mesh_index == mesh_count:
            return [], cursor
        state = (mesh_index, cursor)
        if state in failed_states:
            raise failed_states[state]
        try:
            candidates = mesh_candidates(mesh_index, cursor)
        except (LtbError, struct.error) as error:
            failure = error if isinstance(error, LtbError) else LtbError(str(error))
            failed_states[state] = failure
            raise failure
        downstream_errors = []
        for mesh, next_cursor in candidates:
            try:
                remaining, final_cursor = parse_from(mesh_index + 1, next_cursor)
                return [mesh, *remaining], final_cursor
            except LtbError as error:
                downstream_errors.append(error)
        failure = downstream_errors[0] if downstream_errors else LtbError(
            f"no bounded layout candidate for mesh {mesh_index}"
        )
        failed_states[state] = failure
        raise failure

    try:
        meshes, cursor = parse_from(0, first_mesh_cursor)
    except LtbError as legacy_error:
        try:
            return parse_crossfire_composite_ltb(
                data,
                command_length,
                mesh_count,
                first_mesh_cursor,
                oriented_bounding_box_count,
            )
        except (LtbError, struct.error) as composite_error:
            raise LtbError(
                f"legacy layout: {legacy_error}; composite layout: {composite_error}"
            ) from composite_error
    mesh_type_counts: Counter[int] = Counter()
    for mesh_index, mesh in enumerate(meshes):
        mesh_type = mesh.mesh_type
        mesh_type_counts[mesh_type] += 1
    return meshes, {
        "header": [1, 9],
        "layout": "legacy_single_submesh",
        "command_line": command,
        "global_radius": global_radius,
        "oriented_bounding_box_count": oriented_bounding_box_count,
        "mesh_count": len(meshes),
        "vertex_count": sum(len(mesh.positions) for mesh in meshes),
        "triangle_count": sum(len(mesh.indices) // 3 for mesh in meshes),
        "mesh_type_counts": {
            str(key): value for key, value in sorted(mesh_type_counts.items())
        },
        "parsed_bytes": cursor,
        "trailing_bytes": len(data) - cursor,
    }


def align4(buffer: bytearray, pad: int = 0) -> None:
    buffer.extend(bytes([pad]) * ((-len(buffer)) % 4))


def multiply_matrix4(left: list[float], right: list[float]) -> list[float]:
    return [
        sum(left[row * 4 + inner] * right[inner * 4 + column] for inner in range(4))
        for row in range(4)
        for column in range(4)
    ]


def invert_matrix4(matrix: list[float], label: str) -> list[float]:
    augmented = [
        [*matrix[row * 4 : row * 4 + 4], *[float(row == column) for column in range(4)]]
        for row in range(4)
    ]
    for column in range(4):
        pivot = max(range(column, 4), key=lambda row: abs(augmented[row][column]))
        if abs(augmented[pivot][column]) <= 1e-10:
            raise LtbError(f"singular bind matrix for {label}")
        augmented[column], augmented[pivot] = augmented[pivot], augmented[column]
        scale = augmented[column][column]
        augmented[column] = [value / scale for value in augmented[column]]
        for row in range(4):
            if row == column:
                continue
            factor = augmented[row][column]
            augmented[row] = [
                value - factor * pivot_value
                for value, pivot_value in zip(augmented[row], augmented[column])
            ]
    inverse = [value for row in augmented for value in row[4:]]
    if not all(math.isfinite(value) for value in inverse):
        raise LtbError(f"non-finite inverse bind matrix for {label}")
    return inverse


def gltf_matrix(matrix: list[float]) -> list[float]:
    """Convert LTMatrix's row-major storage to glTF's column-major array."""
    return [matrix[row * 4 + column] for column in range(4) for row in range(4)]


def matrix_to_trs(
    matrix: list[float], label: str
) -> tuple[
    tuple[float, float, float],
    tuple[float, float, float, float],
    tuple[float, float, float],
]:
    if len(matrix) != 16 or not all(math.isfinite(value) for value in matrix):
        raise LtbError(f"invalid local matrix for {label}")
    if any(abs(matrix[index] - expected) > 1e-4 for index, expected in ((12, 0), (13, 0), (14, 0), (15, 1))):
        raise LtbError(f"non-affine local matrix for {label}")
    columns = [
        [matrix[row * 4 + column] for row in range(3)] for column in range(3)
    ]
    scale = tuple(math.sqrt(sum(value * value for value in column)) for column in columns)
    if any(value <= 1e-8 for value in scale):
        raise LtbError(f"degenerate local scale for {label}")
    rotation = [
        [matrix[row * 4 + column] / scale[column] for column in range(3)]
        for row in range(3)
    ]
    if any(
        abs(sum(rotation[row][axis] * rotation[other][axis] for axis in range(3)))
        > 1e-4
        for row in range(3)
        for other in range(row + 1, 3)
    ):
        raise LtbError(f"sheared local matrix for {label}")
    determinant = (
        rotation[0][0]
        * (rotation[1][1] * rotation[2][2] - rotation[1][2] * rotation[2][1])
        - rotation[0][1]
        * (rotation[1][0] * rotation[2][2] - rotation[1][2] * rotation[2][0])
        + rotation[0][2]
        * (rotation[1][0] * rotation[2][1] - rotation[1][1] * rotation[2][0])
    )
    if abs(determinant - 1.0) > 1e-4:
        raise LtbError(f"reflected local matrix for {label}")
    trace = rotation[0][0] + rotation[1][1] + rotation[2][2]
    if trace > 0:
        factor = math.sqrt(trace + 1.0) * 2.0
        quaternion = (
            (rotation[2][1] - rotation[1][2]) / factor,
            (rotation[0][2] - rotation[2][0]) / factor,
            (rotation[1][0] - rotation[0][1]) / factor,
            factor / 4.0,
        )
    elif rotation[0][0] > rotation[1][1] and rotation[0][0] > rotation[2][2]:
        factor = math.sqrt(1.0 + rotation[0][0] - rotation[1][1] - rotation[2][2]) * 2.0
        quaternion = (
            factor / 4.0,
            (rotation[0][1] + rotation[1][0]) / factor,
            (rotation[0][2] + rotation[2][0]) / factor,
            (rotation[2][1] - rotation[1][2]) / factor,
        )
    elif rotation[1][1] > rotation[2][2]:
        factor = math.sqrt(1.0 + rotation[1][1] - rotation[0][0] - rotation[2][2]) * 2.0
        quaternion = (
            (rotation[0][1] + rotation[1][0]) / factor,
            factor / 4.0,
            (rotation[1][2] + rotation[2][1]) / factor,
            (rotation[0][2] - rotation[2][0]) / factor,
        )
    else:
        factor = math.sqrt(1.0 + rotation[2][2] - rotation[0][0] - rotation[1][1]) * 2.0
        quaternion = (
            (rotation[0][2] + rotation[2][0]) / factor,
            (rotation[1][2] + rotation[2][1]) / factor,
            factor / 4.0,
            (rotation[1][0] - rotation[0][1]) / factor,
        )
    return (
        (matrix[3], matrix[7], matrix[11]),
        normalize_quaternion(quaternion, label),
        scale,
    )


def coalesced_keyframe_indices(times_ms: list[int]) -> list[int]:
    """Keep the final source key when LTB has duplicate millisecond timestamps."""
    indices: list[int] = []
    for index, time_ms in enumerate(times_ms):
        if indices and times_ms[indices[-1]] == time_ms:
            indices[-1] = index
        else:
            indices.append(index)
    return indices


def expand_vertex_animation_frame(
    mesh: LtbMesh, frame: list[tuple[float, float, float]], label: str
) -> list[tuple[float, float, float]]:
    if (
        mesh.unduplicated_vertex_count is None
        or mesh.duplicate_map is None
        or mesh.retained_vertex_indices is None
        or mesh.source_vertex_count is None
    ):
        raise LtbError(f"incomplete vertex animation metadata for {mesh.name}")
    if len(frame) != mesh.unduplicated_vertex_count:
        raise LtbError(
            f"vertex animation frame size mismatch in {label}: "
            f"{len(frame)}/{mesh.unduplicated_vertex_count}"
        )
    expanded: list[tuple[float, float, float] | None] = [
        None
    ] * mesh.source_vertex_count
    expanded[: len(frame)] = frame
    for source, destination in mesh.duplicate_map:
        if expanded[source] is None:
            raise LtbError(f"unresolved duplicate source in {label}: {source}")
        expanded[destination] = expanded[source]
    if any(expanded[index] is None for index in mesh.retained_vertex_indices):
        raise LtbError(f"incomplete vertex animation coverage in {label}")
    return [expanded[index] for index in mesh.retained_vertex_indices]  # type: ignore[misc]


def make_glb(
    meshes: list[LtbMesh],
    source: dict[str, Any],
    skeleton: dict[str, Any] | None = None,
    animations: list[LtbAnimation] | None = None,
) -> bytes:
    binary = bytearray()
    buffer_views = []
    accessors = []
    gltf_meshes = []
    nodes: list[dict[str, Any]] = []
    scene_nodes: list[int] = []
    skins: list[dict[str, Any]] = []
    gltf_animations: list[dict[str, Any]] = []
    mesh_node_indices: list[int] = []
    mesh_morph_ranges: list[dict[int, tuple[int, int]]] = []
    mesh_morph_target_counts: list[int] = []
    default_translations: list[tuple[float, float, float]] = []
    default_rotations: list[tuple[float, float, float, float]] = []

    def append_view(payload: bytes, target: int | None) -> int:
        align4(binary)
        offset = len(binary)
        binary.extend(payload)
        index = len(buffer_views)
        view = {"buffer": 0, "byteOffset": offset, "byteLength": len(payload)}
        if target is not None:
            view["target"] = target
        buffer_views.append(view)
        return index

    def append_accessor(
        view: int,
        component_type: int,
        count: int,
        value_type: str,
        minimum: list[float] | None = None,
        maximum: list[float] | None = None,
    ) -> int:
        accessor: dict[str, Any] = {
            "bufferView": view,
            "componentType": component_type,
            "count": count,
            "type": value_type,
        }
        if minimum is not None:
            accessor["min"] = minimum
        if maximum is not None:
            accessor["max"] = maximum
        accessors.append(accessor)
        return len(accessors) - 1

    animations = animations or []
    has_skinned_mesh = any(mesh.joints is not None for mesh in meshes)
    has_model_skeleton = skeleton is not None and (has_skinned_mesh or animations)
    if has_model_skeleton:
        if skeleton is None:
            raise LtbError("animated or skinned mesh has no skeleton metadata")
        names = skeleton["names"]
        parents = skeleton["parent_indices"]
        global_matrices = skeleton["bind_matrices"]
        bone_flags = skeleton.get("flags", [0] * len(names))
        bone_count = skeleton["bone_count"]
        if not (
            bone_count
            == len(names)
            == len(parents)
            == len(global_matrices)
            == len(bone_flags)
            <= 65_535
        ):
            raise LtbError("invalid skeleton metadata dimensions")
        for bone_index, (name, parent, global_matrix) in enumerate(
            zip(names, parents, global_matrices)
        ):
            local_matrix = (
                global_matrix
                if parent < 0
                else multiply_matrix4(
                    invert_matrix4(global_matrices[parent], f"bone {parent}"),
                    global_matrix,
                )
            )
            translation, rotation, scale = matrix_to_trs(
                local_matrix, f"bone {bone_index}"
            )
            default_translations.append(translation)
            default_rotations.append(rotation)
            nodes.append(
                {
                    "name": name,
                    "translation": list(translation),
                    "rotation": list(rotation),
                    "scale": list(scale),
                }
            )
            if parent < 0:
                scene_nodes.append(bone_index)
            else:
                nodes[parent].setdefault("children", []).append(bone_index)
        if has_skinned_mesh:
            inverse_bind_payload = b"".join(
                struct.pack(
                    "<16f",
                    *gltf_matrix(invert_matrix4(matrix, f"bone {index}")),
                )
                for index, matrix in enumerate(global_matrices)
            )
            inverse_bind_accessor = append_accessor(
                append_view(inverse_bind_payload, None), 5126, bone_count, "MAT4"
            )
            skins.append(
                {
                    "name": "LTB skeleton",
                    "inverseBindMatrices": inverse_bind_accessor,
                    "joints": list(range(bone_count)),
                    "skeleton": scene_nodes[0],
                }
            )

    for mesh in meshes:
        if not mesh.positions or not mesh.indices:
            continue
        position_payload = b"".join(struct.pack("<3f", *value) for value in mesh.positions)
        normal_payload = b"".join(struct.pack("<3f", *value) for value in mesh.normals)
        uv_payload = b"".join(struct.pack("<2f", *value) for value in mesh.texcoords)
        index_payload = struct.pack(f"<{len(mesh.indices)}H", *mesh.indices)
        mins = [min(value[axis] for value in mesh.positions) for axis in range(3)]
        maxs = [max(value[axis] for value in mesh.positions) for axis in range(3)]
        position_accessor = append_accessor(
            append_view(position_payload, 34962), 5126, len(mesh.positions), "VEC3", mins, maxs
        )
        normal_accessor = append_accessor(
            append_view(normal_payload, 34962), 5126, len(mesh.normals), "VEC3"
        )
        uv_accessor = append_accessor(
            append_view(uv_payload, 34962), 5126, len(mesh.texcoords), "VEC2"
        )
        index_accessor = append_accessor(
            append_view(index_payload, 34963), 5123, len(mesh.indices), "SCALAR"
        )
        attributes = {
            "POSITION": position_accessor,
            "NORMAL": normal_accessor,
            "TEXCOORD_0": uv_accessor,
        }
        if mesh.joints is not None or mesh.weights is not None:
            if (
                mesh.joints is None
                or mesh.weights is None
                or len(mesh.joints) != len(mesh.positions)
                or len(mesh.weights) != len(mesh.positions)
            ):
                raise LtbError(f"incomplete skin arrays for mesh {mesh.name}")
            joint_payload = b"".join(
                struct.pack("<4H", *value) for value in mesh.joints
            )
            weight_payload = b"".join(
                struct.pack("<4f", *value) for value in mesh.weights
            )
            attributes["JOINTS_0"] = append_accessor(
                append_view(joint_payload, 34962),
                5123,
                len(mesh.joints),
                "VEC4",
            )
            attributes["WEIGHTS_0"] = append_accessor(
                append_view(weight_payload, 34962),
                5126,
                len(mesh.weights),
                "VEC4",
            )
        primitive: dict[str, Any] = {
            "attributes": attributes,
            "indices": index_accessor,
            "mode": 4,
        }
        morph_targets: list[dict[str, int]] = []
        morph_target_names: list[str] = []
        morph_ranges: dict[int, tuple[int, int]] = {}
        if mesh.animation_node is not None and animations:
            if mesh.animation_node >= len(animations[0].nodes):
                raise LtbError(f"missing vertex animation node for mesh {mesh.name}")
            for animation_index, animation in enumerate(animations):
                animation_node = animation.nodes[mesh.animation_node]
                if animation_node.vertex_frames is None:
                    raise LtbError(
                        f"animation {animation.name} lacks vertex frames for {mesh.name}"
                    )
                start = len(morph_targets)
                for frame_index, frame in enumerate(animation_node.vertex_frames):
                    expanded = expand_vertex_animation_frame(
                        mesh, frame, f"{animation.name}/{mesh.name}/{frame_index}"
                    )
                    deltas = [
                        tuple(animated[axis] - base[axis] for axis in range(3))
                        for animated, base in zip(expanded, mesh.positions)
                    ]
                    payload = b"".join(
                        struct.pack("<3f", *value) for value in deltas
                    )
                    delta_mins = [
                        min(value[axis] for value in deltas) for axis in range(3)
                    ]
                    delta_maxs = [
                        max(value[axis] for value in deltas) for axis in range(3)
                    ]
                    accessor = append_accessor(
                        append_view(payload, 34962),
                        5126,
                        len(deltas),
                        "VEC3",
                        delta_mins,
                        delta_maxs,
                    )
                    morph_targets.append({"POSITION": accessor})
                    morph_target_names.append(
                        f"{animation.name}/frame-{frame_index:04d}"
                    )
                morph_ranges[animation_index] = (
                    start,
                    len(animation_node.vertex_frames),
                )
            primitive["targets"] = morph_targets
        gltf_mesh: dict[str, Any] = {
            "name": mesh.name,
            "primitives": [primitive],
            "extras": {"sourceMeshType": mesh.mesh_type},
        }
        if morph_targets:
            gltf_mesh["weights"] = [0.0] * len(morph_targets)
            gltf_mesh["extras"]["targetNames"] = morph_target_names
        gltf_meshes.append(gltf_mesh)
        mesh_node: dict[str, Any] = {
            "name": mesh.name,
            "mesh": len(gltf_meshes) - 1,
        }
        if mesh.joints is not None:
            mesh_node["skin"] = 0
        nodes.append(mesh_node)
        mesh_node_index = len(nodes) - 1
        mesh_node_indices.append(mesh_node_index)
        mesh_morph_ranges.append(morph_ranges)
        mesh_morph_target_counts.append(len(morph_targets))
        scene_nodes.append(mesh_node_index)
    if not gltf_meshes:
        raise LtbError("LTB contains no non-empty triangle mesh")
    if animations:
        if not has_model_skeleton or skeleton is None:
            raise LtbError("animation export requires skeleton metadata")
        bone_count = skeleton["bone_count"]
        bone_flags = skeleton.get("flags", [0] * bone_count)
        for animation_index, animation in enumerate(animations):
            if len(animation.nodes) != bone_count:
                raise LtbError(
                    f"animation node count mismatch in {animation.name}: "
                    f"{len(animation.nodes)}/{bone_count}"
                )
            selected_indices = coalesced_keyframe_indices(animation.times_ms)
            times_seconds = [
                animation.times_ms[index] / 1000.0 for index in selected_indices
            ]
            time_payload = struct.pack(
                f"<{len(times_seconds)}f", *times_seconds
            )
            time_accessor = append_accessor(
                append_view(time_payload, None),
                5126,
                len(times_seconds),
                "SCALAR",
                [times_seconds[0]],
                [times_seconds[-1]],
            )
            samplers: list[dict[str, Any]] = []
            channels: list[dict[str, Any]] = []
            for bone_index, animation_node in enumerate(animation.nodes):
                translations = []
                for source_index in selected_indices:
                    if bone_flags[bone_index] & 0x02:
                        translation = (
                            (0.0, 0.0, 0.0)
                            if skeleton["parent_indices"][bone_index] < 0
                            else default_translations[bone_index]
                        )
                    else:
                        translation = animation_node.translations[source_index]
                        if bone_index == 0:
                            translation = tuple(
                                translation[axis]
                                + animation.root_translation[axis]
                                for axis in range(3)
                            )
                    translations.append(translation)
                translation_payload = b"".join(
                    struct.pack("<3f", *value) for value in translations
                )
                translation_accessor = append_accessor(
                    append_view(translation_payload, None),
                    5126,
                    len(translations),
                    "VEC3",
                )
                samplers.append(
                    {
                        "input": time_accessor,
                        "output": translation_accessor,
                        "interpolation": "LINEAR",
                    }
                )
                channels.append(
                    {
                        "sampler": len(samplers) - 1,
                        "target": {"node": bone_index, "path": "translation"},
                    }
                )

                rotations = [
                    animation_node.rotations[index] for index in selected_indices
                ]
                continuous_rotations = []
                for rotation in rotations:
                    if continuous_rotations and sum(
                        left * right
                        for left, right in zip(continuous_rotations[-1], rotation)
                    ) < 0:
                        rotation = tuple(-value for value in rotation)
                    continuous_rotations.append(rotation)
                rotation_payload = b"".join(
                    struct.pack("<4f", *value) for value in continuous_rotations
                )
                rotation_accessor = append_accessor(
                    append_view(rotation_payload, None),
                    5126,
                    len(continuous_rotations),
                    "VEC4",
                )
                samplers.append(
                    {
                        "input": time_accessor,
                        "output": rotation_accessor,
                        "interpolation": "LINEAR",
                    }
                )
                channels.append(
                    {
                        "sampler": len(samplers) - 1,
                        "target": {"node": bone_index, "path": "rotation"},
                    }
                )

            for mesh_index, morph_ranges in enumerate(mesh_morph_ranges):
                if animation_index not in morph_ranges:
                    continue
                start, frame_count = morph_ranges[animation_index]
                target_count = mesh_morph_target_counts[mesh_index]
                weight_values = []
                for source_index in selected_indices:
                    if source_index >= frame_count:
                        raise LtbError(
                            f"morph frame mismatch in animation {animation.name}"
                        )
                    values = [0.0] * target_count
                    values[start + source_index] = 1.0
                    weight_values.extend(values)
                weight_payload = struct.pack(
                    f"<{len(weight_values)}f", *weight_values
                )
                weight_accessor = append_accessor(
                    append_view(weight_payload, None),
                    5126,
                    len(weight_values),
                    "SCALAR",
                )
                samplers.append(
                    {
                        "input": time_accessor,
                        "output": weight_accessor,
                        "interpolation": "LINEAR",
                    }
                )
                channels.append(
                    {
                        "sampler": len(samplers) - 1,
                        "target": {
                            "node": mesh_node_indices[mesh_index],
                            "path": "weights",
                        },
                    }
                )
            keyframe_events = [
                {"timeMs": time_ms, "value": value}
                for time_ms, value in zip(
                    animation.times_ms, animation.keyframe_strings
                )
                if value
            ]
            gltf_animation: dict[str, Any] = {
                "name": animation.name,
                "samplers": samplers,
                "channels": channels,
                "extras": {
                    "sourceCompressionType": animation.compression_type,
                    "interpolationMs": animation.interpolation_ms,
                    "duplicateTimestampsCoalesced": (
                        len(animation.times_ms) - len(selected_indices)
                    ),
                },
            }
            if keyframe_events:
                gltf_animation["extras"]["keyframeEvents"] = keyframe_events
            gltf_animations.append(gltf_animation)
    gltf = {
        "asset": {
            "version": "2.0",
            "generator": "GenesisSoldierSoul convert_ltb_models.py",
            "extras": {
                "sourceFormat": "LithTech Jupiter LTB v9",
                "sourceCoordinateSystem": "preserved; no unproven axis transform",
                "limitations": "materials, sockets, weight sets and child-model bindings are not converted",
                **source,
            },
        },
        "scene": 0,
        "scenes": [{"nodes": scene_nodes}],
        "nodes": nodes,
        "meshes": gltf_meshes,
        "buffers": [{"byteLength": len(binary)}],
        "bufferViews": buffer_views,
        "accessors": accessors,
    }
    if skins:
        gltf["skins"] = skins
    if gltf_animations:
        gltf["animations"] = gltf_animations
    json_chunk = bytearray(json.dumps(gltf, ensure_ascii=False, separators=(",", ":")).encode())
    align4(json_chunk, 0x20)
    align4(binary)
    total = 12 + 8 + len(json_chunk) + 8 + len(binary)
    return (
        struct.pack("<4sII", b"glTF", 2, total)
        + struct.pack("<I4s", len(json_chunk), b"JSON")
        + json_chunk
        + struct.pack("<I4s", len(binary), b"BIN\0")
        + binary
    )


def validate_glb(data: bytes) -> dict[str, int]:
    if len(data) < 20:
        raise LtbError("GLB is truncated")
    magic, version, total = struct.unpack_from("<4sII", data)
    if magic != b"glTF" or version != 2 or total != len(data):
        raise LtbError("invalid GLB header")
    json_size, json_type = struct.unpack_from("<I4s", data, 12)
    if json_type != b"JSON" or 20 + json_size + 8 > len(data):
        raise LtbError("invalid GLB JSON chunk")
    gltf = json.loads(data[20 : 20 + json_size].decode().rstrip(" "))
    bin_offset = 20 + json_size
    bin_size, bin_type = struct.unpack_from("<I4s", data, bin_offset)
    if bin_type != b"BIN\0" or bin_offset + 8 + bin_size != len(data):
        raise LtbError("invalid GLB BIN chunk")
    declared = gltf["buffers"][0]["byteLength"]
    if declared > bin_size or bin_size - declared > 3:
        raise LtbError("GLB buffer length mismatch")
    for view in gltf["bufferViews"]:
        if view.get("byteOffset", 0) + view["byteLength"] > declared:
            raise LtbError("GLB bufferView exceeds buffer")
    for mesh_index, mesh in enumerate(gltf["meshes"]):
        for primitive in mesh["primitives"]:
            attributes = primitive["attributes"]
            if ("JOINTS_0" in attributes) != ("WEIGHTS_0" in attributes):
                raise LtbError(f"incomplete skin attributes on mesh {mesh_index}")
            for target in primitive.get("targets", []):
                accessor = gltf["accessors"][target["POSITION"]]
                position_accessor = gltf["accessors"][attributes["POSITION"]]
                if (
                    accessor["type"] != "VEC3"
                    or accessor["componentType"] != 5126
                    or accessor["count"] != position_accessor["count"]
                ):
                    raise LtbError(f"invalid morph target on mesh {mesh_index}")
    for skin in gltf.get("skins", []):
        if not skin["joints"] or any(index >= len(gltf["nodes"]) for index in skin["joints"]):
            raise LtbError("invalid GLB skin joint list")
        accessor = gltf["accessors"][skin["inverseBindMatrices"]]
        if accessor["type"] != "MAT4" or accessor["count"] != len(skin["joints"]):
            raise LtbError("invalid GLB inverse bind matrices")
    for animation_index, animation in enumerate(gltf.get("animations", [])):
        if not animation["channels"] or not animation["samplers"]:
            raise LtbError(f"empty GLB animation {animation_index}")
        for channel in animation["channels"]:
            if channel["sampler"] >= len(animation["samplers"]):
                raise LtbError(f"invalid sampler in animation {animation_index}")
            target = channel["target"]
            if target["node"] >= len(gltf["nodes"]):
                raise LtbError(f"invalid animation node in animation {animation_index}")
            sampler = animation["samplers"][channel["sampler"]]
            input_accessor = gltf["accessors"][sampler["input"]]
            output_accessor = gltf["accessors"][sampler["output"]]
            if (
                input_accessor["type"] != "SCALAR"
                or input_accessor["componentType"] != 5126
                or input_accessor["count"] < 1
            ):
                raise LtbError(f"invalid animation input at {animation_index}")
            path = target["path"]
            if path == "translation":
                expected_type = "VEC3"
                expected_count = input_accessor["count"]
            elif path == "rotation":
                expected_type = "VEC4"
                expected_count = input_accessor["count"]
            elif path == "weights":
                node = gltf["nodes"][target["node"]]
                target_count = len(gltf["meshes"][node["mesh"]].get("weights", []))
                expected_type = "SCALAR"
                expected_count = input_accessor["count"] * target_count
            else:
                raise LtbError(f"unsupported animation path: {path}")
            if (
                output_accessor["type"] != expected_type
                or output_accessor["componentType"] != 5126
                or output_accessor["count"] != expected_count
            ):
                raise LtbError(f"invalid animation output at {animation_index}")
    return {
        "meshes": len(gltf["meshes"]),
        "nodes": len(gltf["nodes"]),
        "accessors": len(gltf["accessors"]),
        "skins": len(gltf.get("skins", [])),
        "animations": len(gltf.get("animations", [])),
        "animation_channels": sum(
            len(animation["channels"])
            for animation in gltf.get("animations", [])
        ),
        "morph_targets": sum(
            len(primitive.get("targets", []))
            for mesh in gltf["meshes"]
            for primitive in mesh["primitives"]
        ),
    }


def convert_one(task: dict[str, Any]) -> dict[str, Any]:
    input_path = Path(task["input_path"])
    data = input_path.read_bytes()
    if len(data) != task["input_bytes"] or sha256_bytes(data) != task["input_sha256"]:
        raise LtbError(f"LTB input provenance mismatch: {input_path}")
    meshes, details = parse_ltb(data)
    animations = details.pop("_animations", [])
    glb = make_glb(
        meshes,
        {
            "archive": task["source_archive"],
            "archiveSha256": task["source_archive_sha256"],
            "streamIndex": task["stream_index"],
            "inputSha256": task["input_sha256"],
        },
        details.get("skeleton_metadata"),
        animations,
    )
    validation = validate_glb(glb)
    digest = sha256_bytes(glb)
    output_path = Path(task["output_path"])
    prior_output_paths = [
        Path(path)
        for path in task.get("prior_output_paths", [task["legacy_output_path"]])
    ]
    output_path.parent.mkdir(parents=True, exist_ok=True)
    if output_path.exists():
        if output_path.stat().st_size != len(glb) or sha256_file(output_path) != digest:
            raise LtbError(f"existing GLB differs: {output_path}")
        status = "verified_existing"
    elif matching_prior := next(
        (
            path
            for path in prior_output_paths
            if path.is_file()
            and path.stat().st_size == len(glb)
            and sha256_file(path) == digest
        ),
        None,
    ):
        try:
            os.link(matching_prior, output_path)
            status = "migrated_verified_legacy_hardlink"
        except OSError:
            with tempfile.NamedTemporaryFile(
                dir=output_path.parent, prefix=".partial-", delete=False
            ) as temporary:
                temporary_path = Path(temporary.name)
                temporary.write(glb)
            os.replace(temporary_path, output_path)
            status = "migrated_verified_legacy_copy"
    else:
        with tempfile.NamedTemporaryFile(
            dir=output_path.parent, prefix=".partial-", delete=False
        ) as temporary:
            temporary_path = Path(temporary.name)
            temporary.write(glb)
        os.replace(temporary_path, output_path)
        status = (
            "converted_preserving_different_legacy"
            if any(path.exists() for path in prior_output_paths)
            else "converted"
        )
    if output_path.stat().st_size != len(glb) or sha256_file(output_path) != digest:
        raise LtbError(f"GLB readback failed: {output_path}")
    return {
        "source_archive": task["source_archive"],
        "source_archive_sha256": task["source_archive_sha256"],
        "stream_index": task["stream_index"],
        "input": {
            "path": task["input_label"],
            "bytes": len(data),
            "sha256": task["input_sha256"],
        },
        "details": details,
        "validation": validation,
        "output": {
            "path": task["output_relative"],
            "bytes": len(glb),
            "sha256": digest,
            "representation": "lithtech_ltb_v9_geometry"
            + ("_skin" if validation["skins"] else "")
            + ("_animation" if validation["animations"] else "")
            + "_glb",
            "status": status,
        },
        "status": status,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--recovery-manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--workers", type=int, default=4)
    args = parser.parse_args()
    workspace = args.workspace.resolve()
    output = args.output.resolve()
    recovery = json.loads(args.recovery_manifest.read_text(encoding="utf-8"))
    tasks = []
    for archive in recovery["archives"]:
        source_key = f"{Path(archive['source']).stem}__{archive['source_sha256'][:12]}"
        for stream in archive.get("streams") or []:
            if not stream["prefix_hex"].startswith("0100090000000000"):
                continue
            if "output" in stream:
                input_path = output / stream["output"]["path"]
                input_label = stream["output"]["path"]
            elif stream["loose_matches"]:
                input_path = workspace / stream["loose_matches"][0]
                input_label = stream["loose_matches"][0]
            else:
                raise LtbError("LTB stream has no readable input")
            relative = str(
                Path("private-rez-models")
                / OUTPUT_LAYOUT_VERSION
                / source_key
                / f"stream-{stream['stream_index']:05d}.glb"
            )
            legacy_relative = str(
                Path("private-rez-models")
                / source_key
                / f"stream-{stream['stream_index']:05d}.glb"
            )
            prior_version_relative = str(
                Path("private-rez-models")
                / "layout-v6"
                / source_key
                / f"stream-{stream['stream_index']:05d}.glb"
            )
            tasks.append(
                {
                    "source_archive": archive["source"],
                    "source_archive_sha256": archive["source_sha256"],
                    "stream_index": stream["stream_index"],
                    "input_path": str(input_path),
                    "input_label": input_label,
                    "input_bytes": stream["decoded_bytes"],
                    "input_sha256": stream["sha256"],
                    "output_path": str(output / relative),
                    "output_relative": relative,
                    "legacy_output_path": str(output / legacy_relative),
                    "prior_output_paths": [
                        str(output / prior_version_relative),
                        str(output / legacy_relative),
                    ],
                }
            )
    tasks.sort(key=lambda item: (item["source_archive"], item["stream_index"]))
    records = []
    failures = []
    with ProcessPoolExecutor(max_workers=args.workers) as executor:
        futures = [(task, executor.submit(convert_one, task)) for task in tasks]
        for task, future in futures:
            try:
                records.append(future.result())
            except Exception as error:
                failures.append(
                    {
                        "source_archive": task["source_archive"],
                        "stream_index": task["stream_index"],
                        "input": task["input_label"],
                        "error": f"{type(error).__name__}: {error}",
                    }
                )
    manifest = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "tool": "tools/convert_ltb_models.py",
        "tool_version": "8",
        "reference": {
            "name": "Cote-Duke LTB2X loader source",
            "url": "https://cote-duke.narod.ru/LtbSource.zip",
            "archive_sha256": "2c99507cf39082d33bd57095eb660a2639f0c2ec863fddf5d5faad000c0e8cf1",
            "license_notice": "tools/third_party_notices/LTB2X-LICENSE.txt",
        },
        "additional_references": [
            {
                "name": "NewLTBViewerTool CrossFire composite mesh loader",
                "url": "https://github.com/giaynhap/NewLTBViewerTool/blob/master/NewSALL/LtbLoader.cpp",
                "usage": "format behavior cross-check only; no external executable run",
            },
            {
                "name": "CrossFire LithTech D3D model runtime loaders",
                "url": "https://github.com/liquiddeath13/crossfire_base/blob/fbc4fc238dbfd3b76ad417d45a3d62e072f9d0e4/runtime/model/src/model_load.cpp",
                "usage": "model node, compressed animation channel, socket and binding behavior cross-check",
            },
            {
                "name": "LithTech Jupiter D3D model packer",
                "url": "https://github.com/jsj2008/lithtech/blob/0eab18289bed72879eddb648d3311075b108cf46/tools/Model_Packer/lta2ltb_d3d.cpp",
                "usage": "serialized render-object and vertex-field ordering cross-check",
            },
            {
                "name": "LithTech Jupiter animation serializer",
                "url": "https://github.com/jsj2008/lithtech/blob/0eab18289bed72879eddb648d3311075b108cf46/tools/shared/model/model_save.cpp",
                "usage": "keyframe, compression, vertex-frame, socket and animation binding field ordering cross-check",
            },
        ],
        "parameters": {
            "workspace": str(workspace),
            "recovery_manifest": str(args.recovery_manifest),
            "recovery_manifest_sha256": sha256_file(args.recovery_manifest),
            "output": str(output),
            "workers": args.workers,
            "output_layout_version": OUTPUT_LAYOUT_VERSION,
        },
        "scope": (
            "mesh geometry, normals, UVs and triangle indices; source coordinates "
            "preserved; proven rigid/direct/matrix-palette vertex bindings and bind "
            "matrices exported as glTF skins; skeletal TRS and vertex position "
            "channels exported as glTF animations and morph targets"
        ),
        "records": records,
        "failures": failures,
        "summary": {
            "candidates": len(tasks),
            "converted_or_verified": len(records),
            "failures": len(failures),
            "meshes": sum(item["validation"]["meshes"] for item in records),
            "vertices": sum(item["details"]["vertex_count"] for item in records),
            "triangles": sum(item["details"]["triangle_count"] for item in records),
            "output_bytes": sum(item["output"]["bytes"] for item in records),
            "layout_counts": dict(
                sorted(Counter(item["details"]["layout"] for item in records).items())
            ),
            "composite_skeleton_files": sum(
                item["details"].get("layout") == "crossfire_top_mesh_submesh"
                and item["details"]["skeleton_metadata"]["bone_count"] > 0
                for item in records
            ),
            "composite_bones": sum(
                item["details"].get("skeleton_metadata", {}).get("bone_count", 0)
                for item in records
            ),
            "skinned_glbs": sum(item["validation"]["skins"] > 0 for item in records),
            "skinned_meshes": sum(
                item["details"].get("skinned_mesh_count", 0) for item in records
            ),
            "animated_glbs": sum(
                item["validation"]["animations"] > 0 for item in records
            ),
            "animations": sum(
                item["validation"]["animations"] for item in records
            ),
            "animation_channels": sum(
                item["validation"]["animation_channels"] for item in records
            ),
            "animation_keyframes": sum(
                item["details"].get("animation_keyframes", 0) for item in records
            ),
            "morph_targets": sum(
                item["validation"]["morph_targets"] for item in records
            ),
            "matrix_palette_submeshes": sum(
                item["details"].get("matrix_palette_submeshes", 0)
                for item in records
            ),
            "vertex_animation_submeshes": sum(
                item["details"].get("vertex_animation_submeshes", 0)
                for item in records
            ),
            "oriented_bounding_boxes": sum(
                item["details"].get("oriented_bounding_box_count", 0)
                for item in records
            ),
            "repaired_normal_vertices": sum(
                item["details"].get("repaired_normal_vertices", 0)
                for item in records
            ),
            "fallback_normal_vertices": sum(
                item["details"].get("fallback_normal_vertices", 0)
                for item in records
            ),
            "removed_unreferenced_nonfinite_normal_vertices": sum(
                item["details"].get(
                    "removed_unreferenced_nonfinite_normal_vertices", 0
                )
                for item in records
            ),
        },
    }
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps(manifest["summary"], ensure_ascii=False))
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
