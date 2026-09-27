"""Reopen source/result, check UV transfer, and render actual saved albedo."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Matrix,Vector
sys.path.insert(0,str(Path(__file__).parent))
from repack_baked_hull import CHANNELS,UV_NAME,HALF,pixels,uv_array,surface_error,transfer_map,lod_channels


def geometry(mesh):
    return np.array([v.co[:] for v in mesh.vertices]), np.array([p.vertices[:] for p in mesh.polygons])


def render_views(directory):
    directory.mkdir(parents=True,exist_ok=True)
    scene=bpy.context.scene
    ships=[o for o in scene.objects if o.type=='MESH' and any(o.name.endswith(f'_LOD{i}') for i in range(3))]
    hull=bpy.data.objects['Hull_LOD0']
    mat=bpy.data.materials.new('Reopened UV transfer albedo review')
    mat.use_nodes=True
    nodes=mat.node_tree.nodes
    nodes.clear()
    tex=nodes.new('ShaderNodeTexImage')
    tex.image=bpy.data.images[CHANNELS[0][0]]
    emission=nodes.new('ShaderNodeEmission')
    output=nodes.new('ShaderNodeOutputMaterial')
    mat.node_tree.links.new(tex.outputs['Color'],emission.inputs['Color'])
    mat.node_tree.links.new(emission.outputs[0],output.inputs['Surface'])
    for ship in ships:
        level=int(ship.name[-1]) if ship.name.startswith('Hull_') else 0
        ship_mat=mat.copy()
        for node in ship_mat.node_tree.nodes:
            if node.type=='TEX_IMAGE':
                node.image=bpy.data.images.get(lod_channels(level)[0][0]) or tex.image
        ship.data.materials.clear()
        ship.data.materials.append(ship_mat)
    scene.render.engine='BLENDER_EEVEE_NEXT'
    scene.render.resolution_x=1200
    scene.render.resolution_y=750
    scene.render.resolution_percentage=100
    scene.render.film_transparent=True
    scene.render.image_settings.file_format='PNG'
    scene.view_settings.view_transform='Standard'
    scene.view_settings.look='None'
    scene.view_settings.exposure=0
    scene.view_settings.gamma=1
    camera_data=bpy.data.cameras.new('UV comparison')
    camera=bpy.data.objects.new('UV comparison',camera_data)
    scene.collection.objects.link(camera)
    scene.camera=camera
    camera_data.type='ORTHO'
    camera_data.clip_end=1000
    for name,direction,up in (
        ('top',(0,1,0),(0,0,1)),('bottom',(0,-1,0),(0,0,-1)),
        ('port',(0,0,-1),(0,1,0)),('starboard',(0,0,1),(0,1,0)),
        ('nose',(1,0,0),(0,1,0)),('aft',(-1,0,0),(0,1,0)),
        ('assembly-quarter',(1,.65,1),(0,1,0)),
        ('hull-lod1-quarter',(1,.65,1),(0,1,0)),('hull-lod2-quarter',(1,.65,1),(0,1,0))):
        targets=[o for o in ships if o.name.endswith('_LOD0')] if name=='assembly-quarter' else [hull]
        if name.startswith('hull-lod'):
            targets=[bpy.data.objects['Hull_LOD'+name[8]]]
        for ship in ships: ship.hide_render=ship not in targets
        corners=[o.matrix_world@Vector(c) for o in targets for c in o.bound_box]
        lo=Vector([min(p[i] for p in corners) for i in range(3)])
        hi=Vector([max(p[i] for p in corners) for i in range(3)])
        center=(lo+hi)*.5
        direction=Vector(direction).normalized()
        right=(-direction).cross(Vector(up)).normalized()
        up=direction.cross(right).normalized()
        camera.location=center+direction*(hi-lo).length*3
        camera.rotation_euler=Matrix((right,up,direction)).transposed().to_euler()
        x=[(p-center).dot(right) for p in corners]
        y=[(p-center).dot(up) for p in corners]
        camera_data.ortho_scale=max(max(x)-min(x),(max(y)-min(y))*1.6)*1.12
        scene.render.filepath=str(directory/f'{name}.png')
        bpy.ops.render.render(write_still=True)


ap=argparse.ArgumentParser(description=__doc__)
ap.add_argument('--source',required=True,type=Path)
ap.add_argument('--candidate',required=True,type=Path)
ap.add_argument('--render',action='store_true')
args=ap.parse_args(sys.argv[sys.argv.index('--')+1:])
out=args.candidate.resolve()
(out/'saved-uv-repack-validation.json').write_text(json.dumps({
    'passed':False,'status':'Verification started but has not completed; do not import'},indent=2))
bpy.ops.wm.open_mainfile(filepath=str(args.source.resolve()))
original={}
for level in range(3):
    mesh=bpy.data.objects[f'Hull_LOD{level}'].data
    original[level]=(uv_array(mesh,mesh.uv_layers[0].name),geometry(mesh))
before_images={name:pixels(bpy.data.images[name]) for name,_ in CHANNELS}
if args.render: render_views(out/'Renders'/'Before')
bpy.ops.wm.open_mainfile(filepath=str(out/'Kestrel_K017.blend'))
report={'passed':True,'saved_files_reopened':True,'hull_geometry_unchanged':True,
        'validated_hull_lods':0,'source':str(args.source.resolve()),'uv0':UV_NAME,'lods':[],
        'textures':[],'fbx_sha256':hashlib.sha256((out/'Kestrel_K017.fbx').read_bytes()).hexdigest()}
for level in range(3):
    obj=bpy.data.objects[f'Hull_LOD{level}']
    mesh=obj.data
    before_uv,before_geo=original[level]
    assert all(np.array_equal(a,b) for a,b in zip(before_geo,geometry(mesh))), f'LOD{level} geometry changed during UV-only operation'
    layers=mesh.uv_layers
    assert len(layers)==1 and layers[0].name==UV_NAME and layers[0].active_render, 'Incorrect final UV0'
    after_uv=uv_array(mesh,UV_NAME)
    assert after_uv[:,0].min()>0 and after_uv[:,0].max()<.5 and after_uv[:,1].min()>.5 and after_uv[:,1].max()<1
    _,owner,collapsed,ties=transfer_map(mesh,before_uv,after_uv)
    lod={'mesh':obj.name,'triangles':len(mesh.loop_triangles),'raster_overlap_texels':0,
         'sub_0_002_pixel_shared_edge_ties':ties,'triangles_without_pixel_centers':collapsed,'channels':{}}
    material_images={node.image.name for node in obj.data.materials[0].node_tree.nodes if node.type=='TEX_IMAGE' and node.image}
    assert all(name in material_images for name,_ in lod_channels(level)[:3]), 'Material uses a different LOD atlas'
    for index,(name,filename) in enumerate(lod_channels(level)):
        image=bpy.data.images[name]
        before,after=before_images[CHANNELS[index][0]],pixels(image)
        def byte(a): return np.rint(a*255).astype(np.int16)
        protected=(np.array_equal(byte(before[:HALF]),byte(after[:HALF])) and
                   np.array_equal(byte(before[HALF:,HALF:]),byte(after[HALF:,HALF:])))
        data=(out/'Textures'/filename).read_bytes()
        packed_matches=image.packed_file is not None and bytes(image.packed_file.data)==data
        error=surface_error(mesh,before_uv,after_uv,before,after)
        passed=protected and packed_matches and error['area_weighted_rgb_error']<=.04 and error['surface_area_fraction_error_over_0_10']<=.06 and error['alpha_area_weighted_error']<=.01
        lod['channels'][filename]={'passed':passed,'protected_non_hull_rgba8_unchanged':protected,
            'packed_matches_external':packed_matches,'png_sha256':hashlib.sha256(data).hexdigest(),'surface_transfer':error}
        report['passed'] &= passed
        report['textures'].append({'file':filename,'sha256':hashlib.sha256(data).hexdigest()})
        del after
    report['lods'].append(lod)
    report['validated_hull_lods']+=1
(out/'saved-uv-repack-validation.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2),flush=True)
if not report['passed']: raise RuntimeError('Saved UV repack rejected')
if args.render: render_views(out/'Renders'/'After')
