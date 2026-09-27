"""Render neutral silhouette views of the assembled hull, removing texture from the diagnosis."""
import argparse
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector

ap=argparse.ArgumentParser(description=__doc__)
ap.add_argument('--blend',required=True,type=Path)
ap.add_argument('--output',required=True,type=Path)
args=ap.parse_args(sys.argv[sys.argv.index('--')+1:])
args.output.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(args.blend))
scene=bpy.context.scene
target=next((o for o in scene.objects if o.type=='MESH' and o.name.startswith('Hull_LOD0')),None)
if target is None: raise RuntimeError('Could not find Hull_LOD0 mesh in build')
for obj in scene.objects:
    if obj.type=='MESH': obj.hide_render=(obj!=target)
target.hide_render=False
neutral=bpy.data.materials.new('DIAGNOSTIC neutral gray')
neutral.use_nodes=True
nodes=neutral.node_tree.nodes
nodes.clear()
em=nodes.new('ShaderNodeEmission'); em.inputs['Color'].default_value=(.36,.39,.42,1)
out=nodes.new('ShaderNodeOutputMaterial'); neutral.node_tree.links.new(em.outputs['Emission'],out.inputs['Surface'])
target.data.materials.clear(); target.data.materials.append(neutral)
camera_data=bpy.data.cameras.new('Neutral comparison camera')
camera=bpy.data.objects.new('Neutral comparison camera',camera_data); scene.collection.objects.link(camera)
scene.camera=camera; camera_data.type='ORTHO'
scene.render.engine='CYCLES'; scene.cycles.samples=16
scene.render.resolution_x=1024;scene.render.resolution_y=1024;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.film_transparent=True
deps=bpy.context.evaluated_depsgraph_get(); ev=target.evaluated_get(deps); me=ev.to_mesh()
corners=[target.matrix_world @ Vector(c) for c in target.bound_box]
lo=Vector((min(v.x for v in corners),min(v.y for v in corners),min(v.z for v in corners)))
hi=Vector((max(v.x for v in corners),max(v.y for v in corners),max(v.z for v in corners)))
center=(lo+hi)*.5; extent=max(hi-lo)
ev.to_mesh_clear()
views=(('Top',(0,1,0),(0,0,1)),('Bottom',(0,-1,0),(0,0,-1)),
       ('Starboard',(0,0,1),(0,1,0)),('Nose',(1,0,0),(0,1,0)))
for name,direction,up in views:
    back=Vector(direction).normalized(); up=Vector(up).normalized()
    right=up.cross(back).normalized(); camera_up=back.cross(right).normalized()
    camera.location=center+back*extent*2.2
    camera.rotation_euler=Matrix((right,camera_up,back)).transposed().to_quaternion().to_euler()
    camera_data.ortho_scale=extent*1.18
    scene.render.filepath=str(args.output/f'Hull_{name}_neutral.png')
    bpy.ops.render.render(write_still=True)
    print(f'RENDERED {name}: center={tuple(center)} extent={extent:.3f}',flush=True)
