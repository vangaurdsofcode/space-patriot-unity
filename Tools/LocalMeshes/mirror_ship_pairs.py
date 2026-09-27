"""Mirror paired modules across a ship-space plane without reversing nose/up.

The geometry reflection is baked into a fresh mesh, with corrected winding and
unchanged per-corner UVs. The object's transform retains a positive determinant.
Import ``mirror_instance`` from the assembler, or run this file in Blender on a
verified saved assembly with ``--output NEW_DIRECTORY --recipe kestrel-kit.json``.
This tool never rebakes textures and never edits the source assembly on disk.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector


def reflection(axis='Z', offset=0.0):
    axis = axis.upper()
    if axis not in ('X', 'Y', 'Z'):
        raise ValueError('Mirror plane axis must be X, Y or Z')
    index = 'XYZ'.index(axis)
    matrix = Matrix.Identity(4)
    matrix[index][index] = -1
    matrix[index][3] = 2 * float(offset)
    return matrix


def world_vertices(obj):
    points = np.empty((len(obj.data.vertices), 3), np.float64)
    for vertex in obj.data.vertices:
        points[vertex.index] = obj.matrix_world @ vertex.co
    return points


def _corner_values(mesh, values):
    return {(polygon.index, mesh.loops[index].vertex_index): values[index]
            for polygon in mesh.polygons for index in polygon.loop_indices}


def _uv_signature(mesh):
    signature = {}
    for layer in mesh.uv_layers:
        signature[layer.name] = _corner_values(mesh, [tuple(item.uv) for item in layer.data])
    return signature


def _root_proxy(points, axis, offset):
    """Measured inboard envelope; deliberately not an invented authored socket."""
    distance = np.abs(points[:, axis] - offset)
    width = max(1e-5, np.ptp(points[:, axis]) * .02)
    root = points[distance <= distance.min() + width]
    return {'method': 'centroid of innermost 2 percent span envelope; not an authored socket',
            'vertices': len(root), 'position': root.mean(axis=0).tolist()}


def validate_pair(source, target, axis='Z', offset=0.0, tolerance=5e-5):
    """Check saved geometry, corner UV identity, winding, origin and root proxy."""
    index = 'XYZ'.index(axis.upper())
    a, b = world_vertices(source), world_vertices(target)
    if a.shape != b.shape:
        raise RuntimeError(f'Mirrored vertex count mismatch: {source.name}, {target.name}')
    expected = a.copy()
    expected[:, index] = 2 * offset - expected[:, index]
    vertex_error = float(np.max(np.linalg.norm(expected - b, axis=1)))
    if vertex_error > tolerance:
        raise RuntimeError(f'World-space reflection error {vertex_error} m: {target.name}')
    if _uv_signature(source.data) != _uv_signature(target.data):
        raise RuntimeError(f'Mirrored UV corner bindings changed: {target.name}')
    if len(source.data.polygons) != len(target.data.polygons):
        raise RuntimeError(f'Mirrored polygon count changed: {target.name}')
    for left, right in zip(source.data.polygons, target.data.polygons):
        av, bv = list(left.vertices), list(right.vertices)
        if len(av) != len(bv) or set(av) != set(bv):
            raise RuntimeError(f'Mirrored topology changed: {target.name}')
        # A reflected geometric face needs the opposite index order to retain
        # an outward normal. Cyclic rotation of the first corner is harmless.
        pivot = bv.index(av[0])
        ordered = bv[pivot:] + bv[:pivot]
        if ordered != av[:1] + av[:0:-1]:
            raise RuntimeError(f'Mirrored winding was not reversed: {target.name}')
    plane = reflection(axis, offset)
    origin_error = ((plane @ source.matrix_world.translation) - target.matrix_world.translation).length
    determinant = target.matrix_world.to_3x3().determinant()
    if determinant <= 0 or origin_error > tolerance:
        raise RuntimeError(f'Invalid mirrored object transform: {target.name}')
    root_a, root_b = _root_proxy(a, index, offset), _root_proxy(b, index, offset)
    expected_root = np.array(root_a['position'])
    expected_root[index] = 2 * offset - expected_root[index]
    root_error = float(np.linalg.norm(expected_root - np.array(root_b['position'])))
    if root_error > tolerance:
        raise RuntimeError(f'Mirrored inboard envelope differs: {target.name}')
    return {'source': source.name, 'target': target.name, 'axis': axis.upper(), 'plane_offset': offset,
            'vertices': len(a), 'triangles': sum(len(p.vertices)-2 for p in source.data.polygons),
            'max_world_vertex_reflection_error_metres': vertex_error,
            'origin_reflection_error_metres': origin_error, 'positive_transform_determinant': determinant,
            'winding_reversed': True, 'per_corner_uvs_unchanged': True,
            'source_root_proxy': root_a, 'target_root_proxy': root_b,
            'root_proxy_reflection_error_metres': root_error}


def mirror_instance(source, name, *, axis='Z', offset=0.0, collection=None):
    """Create a mirror of an already placed source instance, returning its object."""
    bpy.context.view_layer.update()
    if source.type != 'MESH' or source.matrix_world.to_3x3().determinant() <= 0:
        raise ValueError('Mirror source must be a mesh with a positive transform determinant')
    if bpy.data.objects.get(name) is not None:
        raise ValueError(f'Refusing to overwrite existing object: {name}')
    mesh = source.data.copy()
    mesh.name = name + '_mirrored_mesh'
    local = reflection(axis)
    custom_normals = None
    if mesh.has_custom_normals:
        custom_normals = _corner_values(mesh, [tuple(item.vector) for item in mesh.corner_normals])
    mesh.transform(local)
    mesh.flip_normals()
    mesh.update()
    if custom_normals is not None:
        transformed = []
        for polygon in mesh.polygons:
            for loop in polygon.loop_indices:
                vertex = mesh.loops[loop].vertex_index
                transformed.append(local.to_3x3() @ Vector(custom_normals[(polygon.index, vertex)]))
        mesh.normals_split_custom_set(transformed)
    target = bpy.data.objects.new(name, mesh)
    (collection or bpy.context.scene.collection).objects.link(target)
    target.matrix_world = reflection(axis, offset) @ source.matrix_world @ local
    target.hide_render = source.hide_render
    target.hide_set(source.hide_get())
    target['mirror_source'] = source.name
    target['mirror_plane_axis'] = axis.upper()
    target['mirror_plane_offset'] = float(offset)
    bpy.context.view_layer.update()
    validate_pair(source, target, axis, offset)
    return target


def _mesh_fingerprint(obj):
    h = hashlib.sha256()
    h.update(np.array(obj.matrix_world, np.float32).tobytes())
    h.update(np.array([v.co[:] for v in obj.data.vertices], np.float32).tobytes())
    for polygon in obj.data.polygons:
        h.update(np.array(polygon.vertices, np.int32).tobytes())
    h.update(json.dumps(_uv_signature_serializable(obj.data), sort_keys=True).encode())
    return h.hexdigest()


def _uv_signature_serializable(mesh):
    return {layer.name: [(mesh.loops[index].vertex_index, list(layer.data[index].uv))
                        for index in range(len(mesh.loops))] for layer in mesh.uv_layers}


def _image_hashes():
    # Old zero-user 'Source ...' image datablocks can legitimately disappear
    # on the next Blender save. Protect the twelve actual final atlas images.
    return {image.name: hashlib.sha256(bytes(image.packed_file.data)).hexdigest()
            for image in bpy.data.images if image.packed_file and image.name.startswith('Kestrel_')}


def render_assembly_views(directory):
    """Show paired placements from six axes using their saved base-color atlases."""
    directory.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    ships = [o for o in scene.objects if o.type == 'MESH' and '_LOD' in o.name]
    targets = [o for o in ships if o.name.endswith('_LOD0')]
    for obj in ships:
        obj.hide_render = obj not in targets
        source_material = obj.data.materials[0]
        base = next(n.image for n in source_material.node_tree.nodes
                    if n.type == 'TEX_IMAGE' and n.image and n.image.name.startswith('Kestrel_BaseColor'))
        material = bpy.data.materials.new('Mirror audit albedo ' + obj.name)
        material.use_nodes = True
        material.node_tree.nodes.clear()
        texture = material.node_tree.nodes.new('ShaderNodeTexImage')
        texture.image = base
        emission = material.node_tree.nodes.new('ShaderNodeEmission')
        output = material.node_tree.nodes.new('ShaderNodeOutputMaterial')
        material.node_tree.links.new(texture.outputs['Color'], emission.inputs['Color'])
        material.node_tree.links.new(emission.outputs[0], output.inputs['Surface'])
        obj.data.materials.clear()
        obj.data.materials.append(material)
    scene.render.engine = 'BLENDER_EEVEE_NEXT'
    scene.render.resolution_x, scene.render.resolution_y = 1400, 1000
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = 'PNG'
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    scene.view_settings.exposure = 0
    scene.view_settings.gamma = 1
    data = bpy.data.cameras.new('Pair mirror review')
    camera = bpy.data.objects.new('Pair mirror review', data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    data.type = 'ORTHO'
    data.clip_end = 1000
    corners = [obj.matrix_world @ Vector(c) for obj in targets for c in obj.bound_box]
    lo = Vector([min(p[i] for p in corners) for i in range(3)])
    hi = Vector([max(p[i] for p in corners) for i in range(3)])
    center = (lo + hi) * .5
    for name, direction, up in (
            ('top', (0,1,0), (0,0,1)), ('bottom', (0,-1,0), (0,0,-1)),
            ('port', (0,0,-1), (0,1,0)), ('starboard', (0,0,1), (0,1,0)),
            ('nose', (1,0,0), (0,1,0)), ('aft', (-1,0,0), (0,1,0)),
            ('quarter', (1,.7,1), (0,1,0))):
        direction = Vector(direction).normalized()
        right = (-direction).cross(Vector(up)).normalized()
        up = direction.cross(right).normalized()
        camera.location = center + direction * (hi-lo).length * 3
        camera.rotation_euler = Matrix((right, up, direction)).transposed().to_euler()
        x = [(p-center).dot(right) for p in corners]
        y = [(p-center).dot(up) for p in corners]
        data.ortho_scale = max(max(x)-min(x), (max(y)-min(y))*1.4)*1.12
        scene.render.filepath = str(directory / (name + '.png'))
        bpy.ops.render.render(write_still=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--output', type=Path, required=True)
    ap.add_argument('--recipe', type=Path, required=True)
    ap.add_argument('--render', action='store_true')
    args = ap.parse_args(sys.argv[sys.argv.index('--')+1:])
    source = Path(bpy.data.filepath).resolve()
    validation = json.loads((source.parent/'saved-uv-repack-validation.json').read_text())
    if not validation.get('passed') or validation.get('validated_hull_lods') != 3:
        raise RuntimeError('Start from a saved, verified three-LOD UV candidate')
    out = args.output.resolve()
    if out.exists():
        raise RuntimeError('Use a fresh output directory; earlier candidates are preserved')
    out.mkdir(parents=True)
    shutil.copytree(source.parent/'Textures', out/'Textures')
    for filename in ('assembly-report.json', 'uv-repack-report.json'):
        shutil.copy2(source.parent/filename, out/filename)
    if (source.parent/'hull-symmetry-validation.json').exists():
        shutil.copy2(source.parent/'hull-symmetry-validation.json',out/'hull-symmetry-validation.json')
    before_images = _image_hashes()
    if len(before_images) != 12:
        raise RuntimeError('Expected twelve final packed atlas images')
    recipe = json.loads(args.recipe.read_text())
    mirror_specs = [instance for part in recipe['parts'] for instance in part['instances']
                    if instance.get('mirror_of')]
    changed_names = {spec['name'] + f'_LOD{level}' for spec in mirror_specs for level in range(3)}
    before_meshes = {obj.name: _mesh_fingerprint(obj) for obj in bpy.data.objects
                     if obj.type == 'MESH' and obj.name not in changed_names}
    reports = []
    for spec in mirror_specs:
        for level in range(3):
            target_name = spec['name'] + f'_LOD{level}'
            original = bpy.data.objects[target_name]
            old_positions = world_vertices(original)
            bpy.data.objects.remove(original, do_unlink=True)
            source_obj = bpy.data.objects[spec['mirror_of'] + f'_LOD{level}']
            target = mirror_instance(source_obj, target_name,
                                     axis=spec.get('mirror_axis','Z'), offset=spec.get('mirror_offset',0))
            report = validate_pair(source_obj, target, spec.get('mirror_axis','Z'), spec.get('mirror_offset',0))
            report['change_from_previous_world_vertex_max_metres'] = float(np.max(np.linalg.norm(world_vertices(target)-old_positions, axis=1)))
            reports.append(report)
    assert before_images == _image_hashes(), 'Mirroring changed packed image bytes'
    assert before_meshes == {name: _mesh_fingerprint(bpy.data.objects[name]) for name in before_meshes}, 'Mirroring changed an unrelated mesh'
    for image in bpy.data.images:
        filename = Path(image.filepath_raw).name
        if (out/'Textures'/filename).exists():
            image.filepath_raw = str(out/'Textures'/filename)
    ships = [o for o in bpy.context.scene.objects if o.type=='MESH' and any(o.name.endswith(f'_LOD{i}') for i in range(3))]
    bpy.ops.object.select_all(action='DESELECT')
    for obj in ships:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = bpy.data.objects['Hull_LOD0']
    bpy.ops.export_scene.fbx(filepath=str(out/'Kestrel_K017.fbx'), use_selection=True, object_types={'MESH'},
                           add_leaf_bones=False, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                           path_mode='COPY', embed_textures=False)
    for obj in ships:
        obj.hide_set(not obj.name.endswith('_LOD0'))
        obj.hide_render = not obj.name.endswith('_LOD0')
    bpy.ops.wm.save_as_mainfile(filepath=str(out/'Kestrel_K017.blend'))
    # Reopen before claiming success. No passed UV report is copied: final FBX
    # hashes must be freshly verified by verify_repacked_hull.py before import.
    bpy.ops.wm.open_mainfile(filepath=str(out/'Kestrel_K017.blend'))
    assert before_images == _image_hashes(), 'Packed textures changed after reopening'
    assert before_meshes == {name: _mesh_fingerprint(bpy.data.objects[name]) for name in before_meshes}, 'Unrelated geometry changed after reopening'
    for report in reports:
        validate_pair(bpy.data.objects[report['source']], bpy.data.objects[report['target']], report['axis'], report['plane_offset'])
    (out/'mirror-pair-validation.json').write_text(json.dumps({
        'passed': True, 'saved_files_reopened': True, 'source': str(source),
        'hull_and_unpaired_meshes_unchanged': True, 'packed_textures_unchanged': True,
        'packed_atlas_image_hashes': before_images,
        'pairs': reports, 'requires_fresh_saved_uv_validation': True,
        'attachment_scope': 'Root proxy is mirrored; contact with asymmetric hull is not certified.'}, indent=2))
    if args.render:
        render_assembly_views(out/'Renders'/'MirroredAssembly')


if __name__ == '__main__':
    main()
