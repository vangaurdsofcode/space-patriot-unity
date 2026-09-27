"""Normalize, review, unwrap and bake one Tripo GLB/FBX in Blender.

Run: blender --background --python prepare_tripo_asset.py -- input.glb output_dir stable-id fauna 2.0 +x
Arguments: source, output directory, stable ID, category (weapon/flora/fauna/geology), target height in metres,
           source horizontal forward axis (+x, -x, +z, -z), optional pivot fraction and #RRGGBB fallback color.
Source asset convention: +Y up. Blender's importer converts it to +Z up; the normalizer restores +Y up.
"""
import bpy, json, math, os, re, sys
from mathutils import Vector, Quaternion, Matrix

args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
if len(args)<4: raise SystemExit('Expected source model, output directory, stable ID and category.')
source,out_dir,species,category=args[:4]
if category not in ('weapon','flora','fauna','geology'):raise SystemExit('Category must be weapon, flora, fauna or geology.')
target_height=float(args[4]) if len(args)>4 else 2.0
source_forward=args[5] if len(args)>5 else '+x'
pivot_fraction=tuple(float(v) for v in args[6].split(',')) if len(args)>6 else ((.5,.28,.62) if category=='weapon' else (.5,0,.5))
source_color_hex=args[7] if len(args)>7 else '#827d6e'
if not re.fullmatch(r'#[0-9a-fA-F]{6}',source_color_hex):raise SystemExit('Base color must be #RRGGBB.')
def srgb_to_linear(channel):
    value=channel/255
    return value/12.92 if value<=.04045 else ((value+.055)/1.055)**2.4
source_color=tuple(srgb_to_linear(int(source_color_hex[i:i+2],16)) for i in (1,3,5))
if len(pivot_fraction)!=3 or any(v<0 or v>1 for v in pivot_fraction):raise SystemExit('Pivot fractions must be three values in [0,1].')
source=os.path.abspath(source);out_dir=os.path.abspath(out_dir)
if not os.path.isfile(source): raise SystemExit('Input model not found: '+source)
os.makedirs(out_dir,exist_ok=True);review=os.path.join(out_dir,'review');bakes=os.path.join(out_dir,'bakes')
os.makedirs(review,exist_ok=True);os.makedirs(bakes,exist_ok=True)

# Keep import deterministic: empty objects, data and stale collections first.
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
for block in bpy.data.collections:
    if block.users==0:bpy.data.collections.remove(block)
ext=os.path.splitext(source)[1].lower()
if ext in ('.glb','.gltf'):bpy.ops.import_scene.gltf(filepath=os.path.abspath(source))
elif ext=='.fbx':bpy.ops.import_scene.fbx(filepath=os.path.abspath(source),automatic_bone_orientation=False)
elif ext=='.obj':
    if hasattr(bpy.ops.wm,'obj_import'):bpy.ops.wm.obj_import(filepath=os.path.abspath(source))
    else:bpy.ops.import_scene.obj(filepath=os.path.abspath(source))
else:raise SystemExit('Supported input is .glb, .gltf, .fbx or .obj.')
objects=list(bpy.context.selected_objects)
meshes=[o for o in objects if o.type=='MESH']
if not meshes:raise SystemExit('Input contains no mesh objects.')
root=bpy.data.objects.new(species+' / normalized import',None);bpy.context.scene.collection.objects.link(root)
for obj in objects:
    world=obj.matrix_world.copy();obj.parent=root;obj.matrix_parent_inverse=root.matrix_world.inverted();obj.matrix_world=world
axis={'+x':(1,0,0),'-x':(-1,0,0),'+z':(0,0,1),'-z':(0,0,-1)}
if source_forward not in axis:raise SystemExit('For +Y-up source models, forward axis must be horizontal: +x, -x, +z or -z.')
# glTF/FBX sources use +Y up; Blender imports them into its +Z-up world.
# Restore the source's upright basis *before* yawing its horizontal forward
# into the game's +Z. The old yaw-only transform stood this creature on its
# nose because its imported Blender Y axis was actually its source depth.
upright=Quaternion((1,0,0),-math.pi/2)
direction=Vector(axis[source_forward]);root.rotation_mode='QUATERNION'
root.rotation_quaternion=direction.rotation_difference(Vector((0,0,1))) @ upright
bpy.context.view_layer.update()
def bounds():
    points=[obj.matrix_world@Vector(corner) for obj in meshes for corner in obj.bound_box]
    lo=Vector(tuple(min(p[i] for p in points) for i in range(3)));hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
    return lo,hi
lo,hi=bounds();height=max(.001,hi.y-lo.y);root.scale=Vector((target_height/height,)*3);bpy.context.view_layer.update();lo,hi=bounds();extent=hi-lo;pivot=Vector((lo.x+extent.x*pivot_fraction[0],lo.y+extent.y*pivot_fraction[1],lo.z+extent.z*pivot_fraction[2]));root.location=-pivot;bpy.context.view_layer.update()
for obj in objects:obj.name=species+' / '+obj.name[:48]
untextured_objects=[]
for obj in meshes:
    if any(mat is not None for mat in obj.data.materials):continue
    # Local TripoSG generates geometry only. Give it an explicit neutral source
    # material so the Blender bake is real and reports no invented detail.
    material=bpy.data.materials.new(species+' / untextured source')
    material.use_nodes=True
    bsdf=material.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value=(*source_color,1)
    bsdf.inputs['Roughness'].default_value=.78
    obj.data.materials.clear()
    obj.data.materials.append(material)
    untextured_objects.append(obj.name)
for obj in meshes:
    if len(obj.data.uv_layers)==0:obj.data.uv_layers.new(name='UVMap_Source')
    uv=obj.data.uv_layers.get('SP_BakeUV') or obj.data.uv_layers.new(name='SP_BakeUV')
    obj.data.uv_layers.active=uv
    uv.active_render=True

# Unwrap all visible meshes in multi-object edit mode, then globally repack to
# remove Tripo UV overlap. The view cameras below never define texture UVs.
bpy.ops.object.select_all(action='DESELECT')
for obj in meshes:obj.select_set(True)
bpy.context.view_layer.objects.active=meshes[0]
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(island_margin=0.006,area_weight=0.0,correct_aspect=True,scale_to_bounds=False)
bpy.ops.uv.pack_islands(rotate=True,margin=0.008)
bpy.ops.object.mode_set(mode='OBJECT')

scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.render.resolution_x=1024;scene.render.resolution_y=1024;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
for obj in bpy.context.selected_objects:obj.select_set(False)
# Existing imported materials/textures become one stable base-color bake.
def bake_image(name,size=2048):
    im=bpy.data.images.new(name,width=size,height=size,alpha=True,float_buffer=False);im.generated_type='BLANK'
    # Cycles' normal/AO bake writes RGB but retains a generated image's alpha.
    # Transparent initialization made those saved maps completely invisible.
    im.generated_color={'basecolor':(0,0,0,0),'normal-tangent':(.5,.5,1,1),'ambient-occlusion':(1,1,1,1)}[name]
    im.filepath_raw=os.path.join(bakes,name+'.png');im.file_format='PNG';return im
images={name:bake_image(name) for name in ('basecolor','normal-tangent','ambient-occlusion')}
def set_bake_target(img):
    for obj in meshes:
        for mat in obj.data.materials:
            if mat is None:continue
            mat.use_nodes=True;nodes=mat.node_tree.nodes;target=next((n for n in nodes if n.type=='TEX_IMAGE' and n.image==img),None)
            if target is None:target=nodes.new('ShaderNodeTexImage');target.image=img
            for node in nodes:node.select=False
            target.select=True;nodes.active=target
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes:obj.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0]
scene.render.bake.margin=16;scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True
set_bake_target(images['basecolor']);scene.cycles.bake_type='DIFFUSE';bpy.ops.object.bake(type='DIFFUSE',pass_filter={'COLOR'})
print('SP_BAKE_STATE basecolor',images['basecolor'].is_dirty,flush=True)
images['basecolor'].save();set_bake_target(images['normal-tangent']);scene.cycles.bake_type='NORMAL';bpy.ops.object.bake(type='NORMAL',normal_space='TANGENT')
print('SP_BAKE_STATE normal-tangent',images['normal-tangent'].is_dirty,flush=True)
images['normal-tangent'].save();set_bake_target(images['ambient-occlusion']);scene.cycles.bake_type='AO';bpy.ops.object.bake(type='AO')
print('SP_BAKE_STATE ambient-occlusion',images['ambient-occlusion'].is_dirty,flush=True)
images['ambient-occlusion'].save()
for img in images.values():img.pack()
# Bind the baked base color and tangent normal to the exported Unity material;
# AO remains a separate map and is listed explicitly in the layout manifest.
images['basecolor'].colorspace_settings.name='sRGB'
images['normal-tangent'].colorspace_settings.name='Non-Color';images['ambient-occlusion'].colorspace_settings.name='Non-Color'
for obj in meshes:
    for mat in obj.data.materials:
        if mat is None:continue
        nodes=mat.node_tree.nodes;links=mat.node_tree.links;bsdf=next((n for n in nodes if n.type=='BSDF_PRINCIPLED'),None)
        if bsdf is None:continue
        color=nodes.new('ShaderNodeTexImage');color.image=images['basecolor'];color.label='SP baked base color';links.new(color.outputs['Color'],bsdf.inputs['Base Color'])
        normal=nodes.new('ShaderNodeTexImage');normal.image=images['normal-tangent'];normal.label='SP baked tangent normal';map_node=nodes.new('ShaderNodeNormalMap');links.new(normal.outputs['Color'],map_node.inputs['Color']);links.new(map_node.outputs['Normal'],bsdf.inputs['Normal'])

# Fixed orthographic review transforms use the same +Z facing rule for every asset.
for data in bpy.data.lights: bpy.data.lights.remove(data)
world=scene.world or bpy.data.worlds.new('SP Studio');scene.world=world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs['Color'].default_value=(.21,.23,.23,1);world.node_tree.nodes['Background'].inputs['Strength'].default_value=.7
def aim(obj,target):obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
for name,loc,up in [('key',(5,7,7),(0,0,1)),('fill',(-6,4,2),(0,0,1)),('rim',(2,5,-6),(0,0,1))]:
    ld=bpy.data.lights.new(name,'AREA');ld.energy=1250 if name=='key' else 780;ld.shape='DISK';ld.size=5;lo_obj=bpy.data.objects.new(name,ld);scene.collection.objects.link(lo_obj);lo_obj.location=loc;aim(lo_obj,(0,target_height*.48,0))
if category in ('flora','fauna','geology'):
    bpy.ops.mesh.primitive_plane_add(size=max(24,target_height*12),location=(0,-.006,0),rotation=(-math.pi/2,0,0));floor=bpy.context.object;floor.name='SP review-only neutral ground';floor_mat=bpy.data.materials.new('SP review ground');floor_mat.diffuse_color=(.16,.18,.18,1);floor_mat.use_nodes=True;floor_mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.16,.18,.18,1);floor_mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.88;floor.data.materials.append(floor_mat)
cam_data=bpy.data.cameras.new('SP orthographic review');cam=bpy.data.objects.new('SP orthographic review',cam_data);scene.collection.objects.link(cam);scene.camera=cam;cam_data.type='ORTHO';cam_data.ortho_scale=max(target_height*1.5,3)
view_specs={'front':((0,.5,8),(0,1,0)),'back':((0,.5,-8),(0,1,0)),'left':((-8,.5,0),(0,1,0)),'right':((8,.5,0),(0,1,0)),'top':((0,8,0),(0,0,1)),'three-quarter':((6,4.2,7),(0,1,0))}
view_report={}
for name,(pos,up) in view_specs.items():
    target=Vector((0,target_height*.5,0));cam.location=target+(Vector(pos)-Vector((0,.5,0)))*max(1,target_height/2);cam.data.lens=52;cam.data.dof.use_dof=False
    forward=(target-cam.location).normalized();right=forward.cross(Vector(up)).normalized();camera_up=right.cross(forward).normalized()
    cam.rotation_euler=Matrix((right,camera_up,-forward)).transposed().to_quaternion().to_euler()
    bpy.context.view_layer.update()
    camera_corners=[cam.matrix_world.inverted()@(obj.matrix_world@Vector(corner)) for obj in meshes for corner in obj.bound_box]
    width=max(p.x for p in camera_corners)-min(p.x for p in camera_corners)
    height=max(p.y for p in camera_corners)-min(p.y for p in camera_corners)
    cam.data.ortho_scale=max(width*1.15,height*1.15,2.5)
    scene.render.filepath=os.path.join(review,name+'.png');bpy.ops.render.render(write_still=True)
    view_report[name]={'camera':[float(v) for v in cam.location],'target':[0,target_height*.5,0],'up':list(up),'projection':'orthographic'}

# Record actual UV extents for review and bake reproducibility.
uv_report=[]
for obj in meshes:
    layer=obj.data.uv_layers.get('SP_BakeUV');coords=[(float(d.uv.x),float(d.uv.y)) for d in layer.data] if layer else []
    uv_report.append({'object':obj.name,'mesh':obj.data.name,'uvMap':'SP_BakeUV','uvBounds':[min(x for x,y in coords),min(y for x,y in coords),max(x for x,y in coords),max(y for x,y in coords)] if coords else None,'vertices':len(obj.data.vertices),'triangles':sum(len(p.vertices)-2 for p in obj.data.polygons)})
lo,hi=bounds();layout={'schema':'space-patriot-bake-layout','version':1,'assetId':species,'category':category,'source':os.path.basename(source),'sourceUp':'+Y','sourceForward':source_forward,'blenderImportUp':'+Z','importToCanonical':'-90deg around Blender X, then horizontal yaw to +Z','units':'metres','origin':'pivot-point','pivotFraction':list(pivot_fraction),'up':'+Y','forward':'+Z','targetHeight':target_height,'viewCoordinates':view_report,'uvMap':'SP_BakeUV','resolution':2048,'paddingPixels':16,'uvCoordinatesAreIndependentOfViews':True,'images':{k:os.path.relpath(v.filepath_raw,out_dir).replace('\\','/') for k,v in images.items()},'objects':uv_report,'bounds':{'min':list(lo),'max':list(hi)},'untexturedSourceObjects':untextured_objects,'sourceBaseColor':source_color_hex,'sourceMaterialNote':'Untextured TripoSG geometry receives flat authored base color; baked color contains no concept-detail projection' if untextured_objects else 'Original imported source materials baked'}
with open(os.path.join(out_dir,'bake-layout.json'),'w',encoding='utf-8') as f:json.dump(layout,f,indent=2)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir,species+'.blend'))
# Unity consumes the first exported UV layer as UV0. Keep the incoming UVs in
# the editable .blend, then export only the validated bake map as UV0.
for obj in meshes:
    for layer in list(obj.data.uv_layers):
        if layer.name!='SP_BakeUV':obj.data.uv_layers.remove(layer)
    obj.data.uv_layers.active_index=0
    obj.data.uv_layers[0].active_render=True
bpy.ops.object.select_all(action='DESELECT')
for obj in objects:obj.select_set(True)
bpy.context.view_layer.objects.active=meshes[0]
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir,species+'.fbx'),use_selection=True,apply_unit_scale=True,axis_forward='-Z',axis_up='Y',bake_space_transform=True,path_mode='COPY',embed_textures=True,add_leaf_bones=False)
print('SP_ASSET_BAKE_OK',category,species,len(meshes),len(uv_report))
