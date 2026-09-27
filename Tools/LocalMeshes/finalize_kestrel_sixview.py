"""Export and render a fully saved six-view candidate, never an in-memory pass."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector

ap = argparse.ArgumentParser(description=__doc__)
ap.add_argument('--output', required=True, type=Path)
ap.add_argument('--no-renders', action='store_true')
args = ap.parse_args(sys.argv[sys.argv.index('--') + 1:])
out = args.output.resolve()
out.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
validation_path = out/'saved-bake-validation.json'
if not validation_path.is_file():
    raise RuntimeError('Run the saved-blend verifier with a baseline before exporting')
validation = json.loads(validation_path.read_text())
if (not validation.get('passed') or not validation.get('first_pass_non_hull_baseline_verified')
        or validation.get('requested_passes') != ['top','bottom','port','starboard','nose','aft']):
    raise RuntimeError('A passing full six-pass baseline comparison is required before exporting')
if scene.get('kestrel_six_view_written') != 'top,bottom,port,starboard,nose,aft':
    raise RuntimeError('All six saved passes are required before exporting')
ships = [o for o in scene.objects if o.type == 'MESH' and any(o.name.endswith(f'_LOD{i}') for i in range(3))]
hull = next(o for o in ships if o.name == 'Hull_LOD0')
for o in ships:
    if o.name.startswith('Hull_'):
        if len(o.data.uv_layers) != 1 or o.data.uv_layers[0].name != 'AtlasUV_SixView_v2':
            raise RuntimeError(f'{o.name} does not export the corrected layout as UV0')
texture_dir = out / 'Textures'
texture_dir.mkdir(exist_ok=True)
for name, filename in (
    ('Kestrel_BaseColor_4096', 'Kestrel_BaseColor.png'),
    ('Kestrel_Normal_4096', 'Kestrel_Normal.png'),
    ('Kestrel_Metal_4096', 'Kestrel_MetallicSmoothness.png'),
    ('Kestrel_Roughness_4096', 'Kestrel_Roughness_source.png'),
):
    im = bpy.data.images[name]
    if not im.packed_file:
        raise RuntimeError(f'{name} is not packed')
    # Copy the verified packed bytes without resaving/re-encoding the pixels.
    data = bytes(im.packed_file.data)
    channel_key = {'Kestrel_BaseColor_4096':'base_color', 'Kestrel_Normal_4096':'normal',
                   'Kestrel_Metal_4096':'metallic_smoothness', 'Kestrel_Roughness_4096':'roughness'}[name]
    expected_hash = validation['passes'][-1]['channels'][channel_key]['packed_png_sha256']
    if hashlib.sha256(data).hexdigest() != expected_hash:
        raise RuntimeError(f'{name} no longer matches the verified final pass')
    target = texture_dir / filename
    target.write_bytes(data)
    im.filepath_raw = str(target)
    im.pack(data=data, data_len=len(data))

bpy.ops.object.select_all(action='DESELECT')
for o in ships:
    o.hide_set(False)
    o.select_set(True)
bpy.context.view_layer.objects.active = hull
bpy.ops.export_scene.fbx(
    filepath=str(out/'Kestrel_K017.fbx'), use_selection=True,
    object_types={'MESH'}, add_leaf_bones=False, axis_forward='-Z', axis_up='Y',
    apply_unit_scale=True, path_mode='COPY', embed_textures=False, mesh_smooth_type='FACE')
for o in ships:
    o.hide_set(not o.name.endswith('_LOD0'))
    o.hide_render = not o.name.endswith('_LOD0')
bpy.ops.wm.save_as_mainfile(filepath=str(out/'Kestrel_K017.blend'))
parts = []
for part, prefix in (('Hull','Hull_'),('Wing','Wing_'),('LandingGear','Gear_'),('Drive','Drive_')):
    meshes = [o for o in ships if o.name.startswith(prefix)]
    parts.append({'part': part, 'instances': sum(o.name.endswith('_LOD0') for o in meshes),
                  'lod_triangles': [sum(sum(len(poly.vertices)-2 for poly in o.data.polygons)
                                    for o in meshes if o.name.endswith(f'_LOD{lod}')) for lod in range(3)]})
(out/'assembly-report.json').write_text(json.dumps({
    'ship': 'Kestrel K-017', 'parts': parts, 'hull_uv0': 'AtlasUV_SixView_v2',
    'status': 'Review candidate; validated saved six-view hull bake. Other parts retain previous art.',
    'normal_detail': 'Neutral tangent normal on re-unwrapped hull; no false high-poly detail claim',
}, indent=2))
if args.no_renders:
    sys.exit(0)

# Review uses albedo alone, so lighting cannot conceal paint registration errors.
mat = bpy.data.materials.new('REVIEW unlit saved atlas')
mat.use_nodes = True
mat.node_tree.nodes.clear()
tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
tex.image = bpy.data.images['Kestrel_BaseColor_4096']
em = mat.node_tree.nodes.new('ShaderNodeEmission')
surface = mat.node_tree.nodes.new('ShaderNodeOutputMaterial')
mat.node_tree.links.new(tex.outputs['Color'], em.inputs['Color'])
mat.node_tree.links.new(em.outputs[0], surface.inputs['Surface'])
for o in ships:
    o.data.materials.clear()
    o.data.materials.append(mat)
scene.view_settings.view_transform = 'Standard'
scene.view_settings.look = 'None'
scene.view_settings.exposure = 0
scene.view_settings.gamma = 1
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.resolution_x = 1600
scene.render.resolution_y = 900
scene.render.resolution_percentage = 100
cam_data = bpy.data.cameras.new('Saved-atlas review camera')
cam = bpy.data.objects.new('Saved-atlas review camera', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam_data.type = 'ORTHO'
cam_data.clip_end = 10000
renders = out/'Renders'/'Saved'
renders.mkdir(parents=True, exist_ok=True)

def render(name, targets, direction, up):
    for o in ships:
        o.hide_render = o not in targets
    points = [o.matrix_world @ Vector(c) for o in targets for c in o.bound_box]
    lo = Vector([min(v[i] for v in points) for i in range(3)])
    hi = Vector([max(v[i] for v in points) for i in range(3)])
    center = (lo+hi)*.5
    direction = Vector(direction).normalized()
    right = (-direction).cross(Vector(up)).normalized()
    up = direction.cross(right).normalized()
    cam.location = center + direction * (hi-lo).length * 3
    cam.rotation_euler = Matrix((right, up, direction)).transposed().to_euler()
    projected_x = [(v-center).dot(right) for v in points]
    projected_y = [(v-center).dot(up) for v in points]
    width = max(projected_x)-min(projected_x)
    height = max(projected_y)-min(projected_y)
    cam_data.ortho_scale = max(width, height*scene.render.resolution_x/scene.render.resolution_y)*1.12
    scene.render.filepath = str(renders/f'{name}.png')
    bpy.ops.render.render(write_still=True)

for name, direction, up in (
    ('top',(0,1,0),(0,0,1)), ('bottom',(0,-1,0),(0,0,-1)),
    ('port',(0,0,-1),(0,1,0)), ('starboard',(0,0,1),(0,1,0)),
    ('nose',(1,0,0),(0,1,0)), ('aft',(-1,0,0),(0,1,0)),
):
    render(name, [hull], direction, up)
render('assembly-quarter', [o for o in ships if o.name.endswith('_LOD0')], (1,.65,1), (0,1,0))
(out/'export-review.json').write_text(json.dumps({
    'source': '06-aft.blend, reopened before finalization',
    'blend': 'Kestrel_K017.blend', 'fbx': 'Kestrel_K017.fbx',
    'hull_uv0': 'AtlasUV_SixView_v2', 'all_hull_lods': True,
    'renders': 'Renders/Saved',
    'status': 'saved bake and UV verification candidate; visual/art approval remains separate',
}, indent=2))
