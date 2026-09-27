"""Constrained hull LOD reduction which rejects UV folds in the shared atlas.

Use ``simplify_uv_safe(source_object, target_triangles, name)`` in Blender.
The returned mesh is independent of the source and has passed the same strict
interior-texel overlap audit used by the post-bake transfer. The accompanying
metrics report achievable counts instead of claiming the requested budget.
"""
from collections import defaultdict
import json
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils.kdtree import KDTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
from repack_baked_hull import UV_NAME, UvOverlapError, transfer_map, uv_array


def _triangles(mesh):
    return sum(len(p.vertices) - 2 for p in mesh.polygons)


def _topology(mesh, uv):
    """Zero-weight UV seam/boundary vertices cannot be collapsed by Blender."""
    first = {}
    locked = set()
    adjacency = defaultdict(set)
    edge_faces = defaultdict(int)
    for polygon in mesh.polygons:
        for loop in polygon.loop_indices:
            vertex = mesh.loops[loop].vertex_index
            point = uv[loop]
            if vertex in first:
                if np.max(np.abs(first[vertex] - point)) > 1e-7:
                    locked.add(vertex)
            else:
                first[vertex] = point
        for edge in polygon.edge_keys:
            edge_faces[tuple(sorted(edge))] += 1
    for edge in mesh.edges:
        a, b = edge.vertices
        adjacency[a].add(b)
        adjacency[b].add(a)
        if edge_faces[tuple(sorted((a, b)))] != 2:
            locked.update((a, b))
    return locked, adjacency


def _expand(vertices, adjacency, rings=1):
    result = set(vertices)
    frontier = set(vertices)
    for _ in range(rings):
        following = set()
        for vertex in frontier:
            following.update(adjacency[vertex])
        frontier = following - result
        result.update(frontier)
    return result


def simplify_uv_safe(source_object, target_triangles, name='Hull_LOD_safe', *, max_attempts=12):
    """Return (new_mesh, metrics); never return an unaudited/duplicated LOD0.

    Chart boundary vertices are fixed using zero decimation weights. If interior
    simplification creates a fold, restore the original mesh and also fix the
    implicated neighborhood before retrying. Reaching a target is optional;
    achieving actual simplification and passing the overlap audit are mandatory.
    No image, source mesh, source transform or material is modified.
    """
    source = source_object.data
    if max_attempts < 1:
        raise ValueError('At least one audited simplification attempt is required')
    if len(source.uv_layers) != 1 or source.uv_layers[0].name != UV_NAME:
        raise ValueError('Expected exactly one freshly packed hull UV0')
    source_uv = uv_array(source, UV_NAME)
    original_triangles = _triangles(source)
    if not 0 < target_triangles < original_triangles:
        raise ValueError('Target must be positive and smaller than source triangle count')
    # Source must be clean before a new LOD can be claimed to preserve its atlas.
    transfer_map(source, source_uv, source_uv)
    locked, adjacency = _topology(source, source_uv)
    initial_locked = len(locked)
    nearest = KDTree(len(source.vertices))
    for vertex in source.vertices:
        nearest.insert(vertex.co, vertex.index)
    nearest.balance()
    attempts = []
    candidate = None
    temp = None
    previous_active = bpy.context.view_layer.objects.active
    selected = list(bpy.context.selected_objects)
    try:
        for attempt in range(max_attempts):
            candidate = source.copy()
            candidate.name = name
            temp = bpy.data.objects.new(name + '_audit', candidate)
            bpy.context.scene.collection.objects.link(temp)
            bpy.ops.object.select_all(action='DESELECT')
            temp.select_set(True)
            bpy.context.view_layer.objects.active = temp
            group = temp.vertex_groups.new(name='UV_safe_collapse')
            free = sorted(set(range(len(candidate.vertices))) - locked)
            if not free:
                raise RuntimeError('No eligible interior vertices remain for a reduced LOD')
            group.add(free, 1.0, 'REPLACE')
            if locked:
                group.add(sorted(locked), 0.0, 'REPLACE')
            modifier = temp.modifiers.new('UV seam constrained reduction', 'DECIMATE')
            modifier.decimate_type = 'COLLAPSE'
            modifier.ratio = target_triangles / original_triangles
            modifier.use_collapse_triangulate = True
            modifier.vertex_group = group.name
            modifier.vertex_group_factor = 1000.0
            bpy.ops.object.modifier_apply(modifier=modifier.name)
            candidate = temp.data
            count = _triangles(candidate)
            uv = uv_array(candidate, UV_NAME)
            try:
                _, owner, collapsed, ties = transfer_map(candidate, uv, uv)
                if count >= original_triangles:
                    raise RuntimeError('Constraints prevented all simplification; refusing duplicate LOD0')
                metrics = {
                    'source_triangles': original_triangles,
                    'requested_triangles': target_triangles,
                    'actual_triangles': count,
                    'triangle_reduction_fraction': 1.0 - count / original_triangles,
                    'target_met': count <= target_triangles,
                    'initial_protected_vertices': initial_locked,
                    'protected_vertices': len(locked),
                    'raster_overlap_texels': 0,
                    'triangles_without_pixel_centers': collapsed,
                    'sub_0_002_pixel_shared_edge_ties': ties,
                    'painted_texels': int((owner >= 0).sum()),
                    'attempts': attempts,
                }
                temp.vertex_groups.clear()
                bpy.data.objects.remove(temp, do_unlink=True)
                temp = None
                result, candidate = candidate, None
                return result, metrics
            except UvOverlapError as error:
                implicated = set()
                for face in error.faces:
                    for vertex_id in candidate.loop_triangles[face].vertices:
                        vertex = candidate.vertices[vertex_id]
                        _, source_id, _ = nearest.find(vertex.co)
                        implicated.add(source_id)
                extended = _expand(implicated, adjacency, rings=2)
                added = extended - locked
                attempts.append({'attempt': attempt + 1, 'triangles': count,
                                 'overlap': str(error), 'newly_protected': len(added)})
                print(json.dumps(attempts[-1]), flush=True)
                if not added:
                    raise RuntimeError('UV fold persists with no new source neighborhood to protect') from error
                locked.update(added)
            finally:
                if temp is not None:
                    bpy.data.objects.remove(temp, do_unlink=True)
                    temp = None
                if candidate is not None and candidate.users == 0:
                    bpy.data.meshes.remove(candidate)
                    candidate = None
        raise RuntimeError(f'No UV-safe reduced LOD after {max_attempts} attempts: {attempts}')
    finally:
        if temp is not None:
            bpy.data.objects.remove(temp, do_unlink=True)
        if candidate is not None and candidate.users == 0:
            bpy.data.meshes.remove(candidate)
        bpy.ops.object.select_all(action='DESELECT')
        for obj in selected:
            if obj.name in bpy.context.view_layer.objects:
                obj.select_set(True)
        if previous_active is not None and previous_active.name in bpy.context.view_layer.objects:
            bpy.context.view_layer.objects.active = previous_active
