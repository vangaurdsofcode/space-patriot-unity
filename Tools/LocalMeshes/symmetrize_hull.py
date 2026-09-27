"""Create a new six-view-UV source by keeping and mirroring the port hull half.

Blender --background SOURCE.blend --python-exit-code 1 --python this.py --
  --output NEW_DIRECTORY

World +X remains the nose and +Y remains up. World Z<=0 is authoritative.
The old starboard geometry is discarded. Port paint is mirrored through copied
UV corner coordinates; this does not repaint any atlas pixels. This creates a
NEW geometry baseline and must be followed by the independent post-bake unwrap.
No claim of watertightness is made unless the actual boundary audit supports it.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
from mirror_ship_pairs import reflection, world_vertices, _mesh_fingerprint

UV_NAME = 'AtlasUV_SixView_v2'
SOURCE_FACE = 'SymmetrySourceFace'
SIDE = 'SymmetrySide'
CHANNELS = (
    ('Kestrel_BaseColor_4096', 'Kestrel_BaseColor.png'),
    ('Kestrel_Normal_4096', 'Kestrel_Normal.png'),
    ('Kestrel_Metal_4096', 'Kestrel_MetallicSmoothness.png'),
    ('Kestrel_Roughness_4096', 'Kestrel_Roughness_source.png'),
)


def _image_hashes():
    return {name: hashlib.sha256(bytes(bpy.data.images[name].packed_file.data)).hexdigest()
            for name, _ in CHANNELS}


def _material_signature(obj):
    return [(material.name,
             sorted((node.name,node.image.name if node.image else None)
                    for node in material.node_tree.nodes if node.type=='TEX_IMAGE'),
             sorted((link.from_node.name,link.from_socket.name,link.to_node.name,link.to_socket.name)
                    for link in material.node_tree.links)) for material in obj.data.materials]


def _source_arrays(obj):
    mesh = obj.data
    if len(mesh.uv_layers) != 1 or mesh.uv_layers[0].name != UV_NAME:
        raise RuntimeError(f'{obj.name} must have exactly the completed six-view UV layer')
    if any(len(p.vertices) != 3 for p in mesh.polygons):
        raise RuntimeError('The geometry/UV provenance verifier requires triangulated source faces')
    return {'vertices': np.array([v.co[:] for v in mesh.vertices], np.float64),
            'faces': np.array([p.vertices[:] for p in mesh.polygons], np.int32),
            'uv': np.array([[mesh.uv_layers[0].data[i].uv[:] for i in p.loop_indices]
                            for p in mesh.polygons], np.float64),
            'materials': _material_signature(obj),
            'face_materials': np.array([p.material_index for p in mesh.polygons]),
            'matrix': np.array(obj.matrix_world, np.float64)}


def topology_report(obj, tolerance=1e-5):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    boundary = [edge for edge in bm.edges if edge.is_boundary]
    result = {'vertices': len(bm.verts), 'faces': len(bm.faces),
              'triangles': sum(len(face.verts)-2 for face in bm.faces),
              'boundary_edges': len(boundary),
              'centerline_boundary_edges': sum(all(abs((obj.matrix_world @ vertex.co).z) < tolerance
                                                    for vertex in edge.verts) for edge in boundary),
              'nonmanifold_edges': sum(not edge.is_manifold for edge in bm.edges),
              'edges_with_more_than_two_faces': sum(len(edge.link_faces)>2 for edge in bm.edges),
              'loose_vertices': sum(not vertex.link_edges for vertex in bm.verts),
              'watertight_manifold': bool(bm.faces) and all(edge.is_manifold for edge in bm.edges)}
    bm.free()
    return result


def symmetrize(obj, tolerance=1e-6):
    """Bisect, duplicate with UVs, reverse winding, weld paired seam vertices."""
    old_mesh = obj.data
    if old_mesh.has_custom_normals:
        raise RuntimeError('Custom split normals require an explicit symmetry transfer; refusing to discard them')
    mesh = old_mesh.copy()
    mesh.name = obj.name + '_port_symmetric_source'
    for attribute in (SOURCE_FACE, SIDE):
        if mesh.attributes.get(attribute):
            raise RuntimeError('Do not symmetrize an already processed source')
    provenance = mesh.attributes.new(SOURCE_FACE, 'INT', 'FACE')
    for index, item in enumerate(provenance.data):
        item.value = index
    mesh.attributes.new(SIDE, 'INT', 'FACE')
    bm = bmesh.new()
    bm.from_mesh(mesh)
    local_plane_point = obj.matrix_world.inverted() @ Vector((0,0,0))
    local_plane_normal = (obj.matrix_world.to_3x3().transposed() @ Vector((0,0,1))).normalized()
    local_mirror = obj.matrix_world.inverted() @ reflection('Z') @ obj.matrix_world
    bmesh.ops.bisect_plane(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces), dist=tolerance,
                          plane_co=local_plane_point, plane_no=local_plane_normal,
                          use_snap_center=True, clear_outer=True, clear_inner=False)
    if not bm.faces or any((obj.matrix_world @ vertex.co).z > tolerance*4 for vertex in bm.verts):
        raise RuntimeError('Bisect did not retain the intended world Z<=0 half')
    # A face lying wholly in the cut plane would become an internal duplicate
    # when mirrored. Remove that interior cap, not surrounding boundary edges.
    caps = [face for face in bm.faces if all(abs((obj.matrix_world @ vertex.co).z) <= tolerance
                                           for vertex in face.verts)]
    if caps:
        bmesh.ops.delete(bm, geom=caps, context='FACES_ONLY')
    bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method='FIXED', ngon_method='EAR_CLIP')
    side_layer = bm.faces.layers.int[SIDE]
    for face in bm.faces:
        face[side_layer] = -1
    kept_verts = set(bm.verts)
    kept_faces = len(bm.faces)
    for vertex in kept_verts:
        world = obj.matrix_world @ vertex.co
        if abs(world.z) <= tolerance:
            world.z = 0
            vertex.co = obj.matrix_world.inverted() @ world
    duplicated = bmesh.ops.duplicate(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces))
    new_vertices = [element for element in duplicated['geom'] if isinstance(element, bmesh.types.BMVert)]
    new_faces = [element for element in duplicated['geom'] if isinstance(element, bmesh.types.BMFace)]
    bmesh.ops.transform(bm, matrix=local_mirror, verts=new_vertices)
    bmesh.ops.reverse_faces(bm, faces=new_faces)
    for face in new_faces:
        face[side_layer] = 1
    pairs = {}
    for key, value in duplicated['vert_map'].items():
        original, copied = (key,value) if key in kept_verts else (value,key)
        if original not in kept_verts or copied in kept_verts:
            raise RuntimeError('Unexpected duplicate vertex mapping')
        if abs((obj.matrix_world @ original.co).z) <= tolerance:
            pairs[copied] = original
    bmesh.ops.weld_verts(bm, targetmap=pairs)
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    obj.data = mesh
    obj['symmetry_source_half'] = 'port / world Z<=0'
    obj['symmetry_plane_world_z'] = 0.0
    return {'retained_port_triangles': kept_faces, 'removed_cut_plane_caps': len(caps),
            'welded_generated_centerline_vertices': len(pairs)}


def validate(obj, source, tolerance=1e-5):
    mesh = obj.data
    if len(mesh.uv_layers) != 1 or mesh.uv_layers[0].name != UV_NAME or not mesh.uv_layers[0].active_render:
        raise RuntimeError(f'Incorrect source UV layer after symmetry: {obj.name}')
    if not np.array_equal(np.array(obj.matrix_world, np.float64), source['matrix']):
        raise RuntimeError('Symmetry changed object transform instead of geometry')
    points = world_vertices(obj)
    tree = KDTree(len(points))
    for index, point in enumerate(points):
        tree.insert(Vector(point), index)
    tree.balance()
    maximum = max(tree.find(Vector((point[0],point[1],-point[2])))[2] for point in points)
    if maximum > tolerance:
        raise RuntimeError(f'No reflected counterpart for hull vertex: {maximum} m')
    # Verify each mirrored face has matching geometry AND UV corner colors.
    side_data = mesh.attributes[SIDE].data
    signatures = {-1: Counter(), 1: Counter()}
    uv_data = mesh.uv_layers[0].data
    for polygon in mesh.polygons:
        side = side_data[polygon.index].value
        if side not in signatures:
            raise RuntimeError('Missing face-side provenance')
        corners = []
        for loop in polygon.loop_indices:
            position = points[mesh.loops[loop].vertex_index].copy()
            if side == 1:
                position[2] *= -1
            corners.append(tuple(np.round(position,5)) + tuple(np.round(uv_data[loop].uv,6)))
        if side == 1:
            corners.reverse()
        # Canonical cyclic order preserves winding, unlike sorting corners.
        key = min(tuple(corners[i:]+corners[:i]) for i in range(len(corners)))
        signatures[side][key] += 1
    if signatures[-1] != signatures[1]:
        raise RuntimeError('Mirrored face geometry, winding or UV corner correspondence differs')
    # Independently check the retained/interpolated paint against the original
    # source triangle, including newly created centerline corners.
    ids = np.array([item.value for item in mesh.attributes[SOURCE_FACE].data], np.int32)
    if _material_signature(obj) != source['materials']:
        raise RuntimeError('Hull material slots or texture graph connections changed')
    if not np.array_equal(np.array([p.material_index for p in mesh.polygons]),source['face_materials'][ids]):
        raise RuntimeError('Mirrored/cut hull faces use a different material')
    sides = np.array([item.value for item in side_data], np.int32)
    new_faces = np.array([p.vertices[:] for p in mesh.polygons], np.int32)
    position = np.array([v.co[:] for v in mesh.vertices], np.float64)[new_faces]
    mirror = np.array(obj.matrix_world.inverted() @ reflection('Z') @ obj.matrix_world, np.float64)
    position[sides==1] = position[sides==1] @ mirror[:3,:3].T + mirror[:3,3]
    old = source['vertices'][source['faces'][ids]]
    ab, ac = old[:,1]-old[:,0], old[:,2]-old[:,0]
    dot_ab = np.einsum('ij,ij->i',ab,ab)
    dot_ac = np.einsum('ij,ij->i',ac,ac)
    cross_dot = np.einsum('ij,ij->i',ab,ac)
    denominator = dot_ab*dot_ac-cross_dot*cross_dot
    valid = np.abs(denominator) > 1e-24
    if not np.all(valid):
        raise RuntimeError(f'{np.count_nonzero(~valid)} degenerate source faces prevent an independent UV correspondence check')
    relative = position-old[:,0,None,:]
    along_ab = np.einsum('ijk,ik->ij',relative,ab)
    along_ac = np.einsum('ijk,ik->ij',relative,ac)
    beta = (dot_ac[:,None]*along_ab-cross_dot[:,None]*along_ac)/denominator[:,None]
    gamma = (dot_ab[:,None]*along_ac-cross_dot[:,None]*along_ab)/denominator[:,None]
    original_uv = source['uv'][ids]
    expected_uv = (original_uv[:,0,None,:]+beta[:,:,None]*(original_uv[:,1,None,:]-original_uv[:,0,None,:])
                   +gamma[:,:,None]*(original_uv[:,2,None,:]-original_uv[:,0,None,:]))
    actual_uv = np.array([[uv_data[i].uv[:] for i in p.loop_indices] for p in mesh.polygons])
    max_uv_error = float(np.max(np.abs(expected_uv-actual_uv)))
    if max_uv_error > 5e-5:
        raise RuntimeError(f'UV interpolation no longer matches original port paint: {max_uv_error}')
    topology = topology_report(obj)
    if topology['centerline_boundary_edges']:
        raise RuntimeError('The newly mirrored centerline contains open boundary edges')
    return {'maximum_vertex_symmetry_error_metres': float(maximum),
            'mirrored_face_and_uv_correspondence': True,
            'mirrored_face_winding_verified': True,
            'material_slots_and_texture_graph_unchanged': True,
            'source_face_material_indices_preserved': True,
            'original_port_uv_interpolation_max_error': max_uv_error,
            'source_uv_layer': UV_NAME, 'uv_layer_count': len(mesh.uv_layers),
            'object_transform_unchanged': True, 'topology': topology}


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--output', required=True, type=Path)
    args = ap.parse_args(sys.argv[sys.argv.index('--')+1:])
    source_path = Path(bpy.data.filepath).resolve()
    if bpy.context.scene.get('kestrel_six_view_written') != 'top,bottom,port,starboard,nose,aft':
        raise RuntimeError('Symmetry source must be the completed protected six-view bake')
    out = args.output.resolve()
    if out.exists():
        raise RuntimeError('Use a new candidate directory; never overwrite prior review evidence')
    out.mkdir(parents=True)
    (out/'Textures').mkdir()
    before_images = _image_hashes()
    original = {level: _source_arrays(bpy.data.objects[f'Hull_LOD{level}']) for level in range(3)}
    untouched = {obj.name: _mesh_fingerprint(obj) for obj in bpy.data.objects
                 if obj.type=='MESH' and not obj.name.startswith('Hull_LOD')}
    report = {'source':str(source_path),'source_sha256':hashlib.sha256(source_path.read_bytes()).hexdigest(),
              'source_half':'port / world Z<=0', 'plane_world_z':0, 'nose_axis':'+X', 'up_axis':'+Y',
              'atlas_repainted':False, 'requires_post_bake_uv_transfer':True, 'lods':[]}
    for level in range(3):
        obj = bpy.data.objects[f'Hull_LOD{level}']
        before = topology_report(obj)
        processing = symmetrize(obj)
        result = validate(obj, original[level])
        report['lods'].append({'level':level,'before':before,'processing':processing,'after':result})
        print(json.dumps(report['lods'][-1],indent=2),flush=True)
    for name, filename in CHANNELS:
        image = bpy.data.images[name]
        data = bytes(image.packed_file.data)
        path = out/'Textures'/filename
        path.write_bytes(data)
        image.filepath_raw = str(path)
        image.pack(data=data,data_len=len(data))
        image.use_fake_user = True
    bpy.context.scene['hull_symmetry_source_half'] = report['source_half']
    bpy.context.scene['hull_symmetry_new_geometry_baseline'] = True
    bpy.ops.wm.save_as_mainfile(filepath=str(out/'Kestrel_K017.blend'))
    bpy.ops.wm.open_mainfile(filepath=str(out/'Kestrel_K017.blend'))
    for level in range(3):
        result = validate(bpy.data.objects[f'Hull_LOD{level}'],original[level])
        report['lods'][level]['reopened'] = result
    assert untouched == {name:_mesh_fingerprint(bpy.data.objects[name]) for name in untouched}, 'Unrelated component changed'
    assert before_images == _image_hashes(), 'Atlas bytes changed during hull symmetry'
    for name,filename in CHANNELS:
        assert bytes(bpy.data.images[name].packed_file.data) == (out/'Textures'/filename).read_bytes()
    report.update({'passed':True,'saved_files_reopened':True,'unpaired_components_unchanged':True,
                   'packed_and_external_images_identical':True,'atlas_sha256':before_images})
    (out/'hull-symmetry-validation.json').write_text(json.dumps(report,indent=2))
    ships = [obj for obj in bpy.data.objects if obj.type=='MESH' and any(obj.name.endswith(f'_LOD{i}') for i in range(3))]
    parts=[]
    for part,prefix in (('Hull','Hull_'),('Wing','Wing_'),('LandingGear','Gear_'),('Drive','Drive_')):
        group=[obj for obj in ships if obj.name.startswith(prefix)]
        parts.append({'part':part,'instances':sum(obj.name.endswith('_LOD0') for obj in group),
                      'lod_triangles':[sum(sum(len(p.vertices)-2 for p in obj.data.polygons)
                                           for obj in group if obj.name.endswith(f'_LOD{level}')) for level in range(3)]})
    (out/'assembly-report.json').write_text(json.dumps({'ship':'Kestrel K-017','parts':parts,
        'hull_uv0':UV_NAME,'status':'New port-symmetric geometry baseline; final UV transfer required'},indent=2))
    (out/'SOURCE_NOTES.md').write_text(
        '# Port-symmetric hull source\n\n'
        'World Z<=0 is the retained master half. World +X remains nose; +Y remains up. '
        'All three hull LODs were bisected, mirrored with UV corners and winding preserved, '
        'and corresponding generated centerline vertices welded. Starboard shape and color '
        'now follow the port half. The four atlas PNGs are byte-identical to the original bake.\n\n'
        'This is a new geometry baseline, retaining only AtlasUV_SixView_v2. Run the post-bake '
        'UV transfer against this blend before import. No new tangent-normal detail was generated. '
        'Existing port-side geometry/projection defects are mirrored too; no cleanup or concept '
        'fidelity is claimed. See hull-symmetry-validation.json for actual boundary/manifold '
        'counts. Wing, drive and gear instances were not changed in this source step.\n',encoding='utf8')


if __name__ == '__main__':
    main()
