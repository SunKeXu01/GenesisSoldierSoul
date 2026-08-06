#!/usr/bin/env python3
"""Convert bounded LithTech Jupiter LTB v9 meshes to glTF 2.0 GLB.

The structural offsets are independently implemented from the loader notes in
Cote-Duke's LTB2X source release and the public CrossFire LithTech runtime
loader.  See third_party_notices/LTB2X-LICENSE.txt.  Mesh geometry is converted;
bounded skeleton metadata is audited, while skins and animations remain in the
source LTB and are not claimed in the GLB.
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
OUTPUT_LAYOUT_VERSION = "layout-v3"

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
]:
    positions: list[tuple[float, float, float] | None] = [None] * vertex_count
    normals: list[tuple[float, float, float] | None] = [None] * vertex_count
    texcoords: list[tuple[float, float] | None] = [None] * vertex_count
    nonfinite_normal_vertices: set[int] = set()
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
            if normal_offset is not None:
                normal = struct.unpack_from("<3f", data, base + normal_offset)
                if all(math.isfinite(value) for value in normal):
                    if normals[vertex_index] is None:
                        normals[vertex_index] = normal
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
) -> tuple[list[tuple[float, float, float]], int]:
    if not invalid_vertices:
        return normals, 0
    accumulated = {vertex: [0.0, 0.0, 0.0] for vertex in invalid_vertices}
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
    repaired = list(normals)
    for vertex, value in accumulated.items():
        length = math.sqrt(sum(component * component for component in value))
        if not math.isfinite(length) or length <= 1e-12:
            raise LtbError(f"cannot derive finite normal for vertex {vertex} in {label}")
        repaired[vertex] = tuple(component / length for component in value)
    return repaired, len(invalid_vertices)


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
                removed_unreferenced_nonfinite_normal_vertices += removed_count
                normals, repaired_count = repair_nonfinite_normals(
                    positions,
                    normals,
                    indices,
                    nonfinite_normal_vertices,
                    label,
                )
                repaired_normal_vertices += repaired_count
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
                    bounded_slice(
                        data, cursor, bone_set_count * 12, f"{label} bone sets"
                    )
                    cursor += bone_set_count * 12
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
                    for duplicate_index in range(duplicate_count):
                        source, destination = struct.unpack_from(
                            "<HH", duplicate_data, duplicate_index * 4
                        )
                        if source >= vertex_count or destination >= vertex_count:
                            raise LtbError(f"out-of-range duplicate map in {label}")
                    cursor += duplicate_count * 4
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
    bone_child_counts = []
    bone_matrices = []
    for bone_index in range(bone_count):
        name_length = u16(data, cursor, f"bone {bone_index} name length")
        cursor += 2
        bone_name = bounded_slice(
            data, cursor, name_length, f"bone {bone_index} name"
        ).decode("cp1252", errors="replace")
        cursor += name_length
        bounded_slice(data, cursor, 3, f"bone {bone_index} flags")
        cursor += 3
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
    return meshes, {
        "header": [1, 9],
        "layout": "crossfire_top_mesh_submesh",
        "mesh_count": len(meshes),
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
        "removed_unreferenced_nonfinite_normal_vertices": (
            removed_unreferenced_nonfinite_normal_vertices
        ),
        "vertex_stream_layout_counts": dict(sorted(stream_layout_counts.items())),
        "skeleton_metadata": {
            "bone_count": bone_count,
            "names": bone_names,
            "parent_indices": bone_parents,
            "child_counts": bone_child_counts,
            "bind_matrices": bone_matrices,
        },
        "parsed_bytes": cursor,
        "trailing_bytes": len(data) - cursor,
    }


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
    for mesh in meshes:
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


def make_glb(meshes: list[LtbMesh], source: dict[str, Any]) -> bytes:
    binary = bytearray()
    buffer_views = []
    accessors = []
    gltf_meshes = []
    nodes = []

    def append_view(payload: bytes, target: int) -> int:
        align4(binary)
        offset = len(binary)
        binary.extend(payload)
        index = len(buffer_views)
        buffer_views.append(
            {"buffer": 0, "byteOffset": offset, "byteLength": len(payload), "target": target}
        )
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
        gltf_meshes.append(
            {
                "name": mesh.name,
                "primitives": [
                    {
                        "attributes": {
                            "POSITION": position_accessor,
                            "NORMAL": normal_accessor,
                            "TEXCOORD_0": uv_accessor,
                        },
                        "indices": index_accessor,
                        "mode": 4,
                    }
                ],
                "extras": {"sourceMeshType": mesh.mesh_type},
            }
        )
        nodes.append({"name": mesh.name, "mesh": len(gltf_meshes) - 1})
    if not gltf_meshes:
        raise LtbError("LTB contains no non-empty triangle mesh")
    gltf = {
        "asset": {
            "version": "2.0",
            "generator": "GenesisSoldierSoul convert_ltb_models.py",
            "extras": {
                "sourceFormat": "LithTech Jupiter LTB v9",
                "sourceCoordinateSystem": "preserved; no unproven axis transform",
                "limitations": "bones and animations are not converted",
                **source,
            },
        },
        "scene": 0,
        "scenes": [{"nodes": list(range(len(nodes)))}],
        "nodes": nodes,
        "meshes": gltf_meshes,
        "buffers": [{"byteLength": len(binary)}],
        "bufferViews": buffer_views,
        "accessors": accessors,
    }
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
    return {
        "meshes": len(gltf["meshes"]),
        "nodes": len(gltf["nodes"]),
        "accessors": len(gltf["accessors"]),
    }


def convert_one(task: dict[str, Any]) -> dict[str, Any]:
    input_path = Path(task["input_path"])
    data = input_path.read_bytes()
    if len(data) != task["input_bytes"] or sha256_bytes(data) != task["input_sha256"]:
        raise LtbError(f"LTB input provenance mismatch: {input_path}")
    meshes, details = parse_ltb(data)
    glb = make_glb(
        meshes,
        {
            "archive": task["source_archive"],
            "archiveSha256": task["source_archive_sha256"],
            "streamIndex": task["stream_index"],
            "inputSha256": task["input_sha256"],
        },
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
            "representation": "lithtech_ltb_v9_geometry_glb",
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
                / "layout-v2"
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
        "tool_version": "4",
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
                "url": "https://github.com/liquiddeath13/crossfire_base/tree/fbc4fc238dbfd3b76ad417d45a3d62e072f9d0e4/runtime/render_a/src/sys/d3d",
                "usage": "render-object IDs, matrix-palette, vertex-stream and OBB behavior cross-check",
            },
            {
                "name": "LithTech Jupiter D3D model packer",
                "url": "https://github.com/jsj2008/lithtech/blob/0eab18289bed72879eddb648d3311075b108cf46/tools/Model_Packer/lta2ltb_d3d.cpp",
                "usage": "serialized render-object and vertex-field ordering cross-check",
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
            "preserved; composite-layout bone hierarchy/bind-matrix metadata audited "
            "but not exported as a glTF skin; animations not claimed"
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
