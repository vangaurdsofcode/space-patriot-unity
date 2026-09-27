"""Add aligned dorsal, underside, and wing-top concept projections to an atlas.

This review-stage repair preserves each part's existing three-quarter bake,
filling its top or bottom faces from matching concept views without changing
other atlas tiles.
"""
import argparse
from collections import deque
import json
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Vector

ap = argparse.ArgumentParser(description=__doc__)
ap.add_argument('--blend', required=True, type=Path)
ap.add_argument('--reference', required=True, type=Path)
ap.add_argument('--underside-reference', type=Path)
ap.add_argument('--wing-reference', type=Path)
ap.add_argument('--output', required=True, type=Path)
ap.add_argument('--render-dir', required=True, type=Path)
args = ap.parse_args(sys.argv[sys.argv.index('--') + 1:])
args.render_dir.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(args.blend))
scene = bpy.context.scene

def edge_connected_foreground(rgb):
    h, w, _ = rgb.shape
    candidate = (np.min(rgb, axis=2) > .91).ravel()
    bg = np.zeros(candidate.size, dtype=np.bool_)
    queue = deque()
    def seed(i):
        if candidate[i] and not bg[i]:
            bg[i] = True
            queue.append(i)
    for x in range(w): seed(x); seed((h - 1) * w + x)
    for y in range(1, h - 1): seed(y * w); seed(y * w + w - 1)
    while queue:
        i = queue.popleft(); x = i % w
        if x and candidate[i - 1] and not bg[i - 1]: bg[i - 1] = True; queue.append(i - 1)
        if x + 1 < w and candidate[i + 1] and not bg[i + 1]: bg[i + 1] = True; queue.append(i + 1)
        if i >= w and candidate[i - w] and not bg[i - w]: bg[i - w] = True; queue.append(i - w)
        if i + w < candidate.size and candidate[i + w] and not bg[i + w]: bg[i + w] = True; queue.append(i + w)
    return ~bg.reshape(h, w)

reference = bpy.data.images.load(str(args.reference), check_existing=False)
rw, rh = reference.size
ref_rgba = np.empty(rw * rh * 4, dtype=np.float32)
reference.pixels.foreach_get(ref_rgba)
ref_rgba = ref_rgba.reshape(rh, rw, 4)
ref_rgb = ref_rgba[:, :, :3]
mask = edge_connected_foreground(ref_rgb)
mask_y, mask_x = np.where(mask)
if len(mask_x) < 1000: raise RuntimeError('Could not segment foreground from dorsal concept')
ref_u0 = float(mask_x.min()) / rw
ref_u1 = float(mask_x.max() + 1) / rw
ref_top0 = float(mask_y.min()) / rh
ref_top1 = float(mask_y.max() + 1) / rh
print(f'DORSAL reference foreground: {mask.mean():.1%}', flush=True)
print(f'DORSAL artwork bounds: u={ref_u0:.4f}..{ref_u1:.4f}, top-v={ref_top0:.4f}..{ref_top1:.4f}', flush=True)

base = bpy.data.images.get('Kestrel_BaseColor_4096')
if base is None:
    raise RuntimeError('Could not find Kestrel_BaseColor_4096 in blend file')
W, H = base.size
if W != H: raise RuntimeError(f'Expected square atlas, got {W}x{H}')
atlas_flat = np.empty(W * H * 4, dtype=np.float32)
base.pixels.foreach_get(atlas_flat)
atlas = atlas_flat.reshape(H, W, 4)
before = atlas[:, :, :3].copy()
changed = np.zeros((H, W), dtype=np.bool_)
covered = np.zeros((H, W), dtype=np.bool_)

# The hull UV tile occupies the upper-left half of the atlas. Mesh positions
# are centered in the same local frame used by the existing side projection.
hull_meshes = [o for o in scene.objects if o.type == 'MESH' and o.name.startswith('Hull_') and o.name.endswith('_LOD0')]
if not hull_meshes: raise RuntimeError('No Hull_*_LOD0 objects found')
mesh = hull_meshes[0].data
uv_layer = mesh.uv_layers.get('AtlasUV')
if uv_layer is None: raise RuntimeError('Hull mesh has no AtlasUV layer')
coords = np.array([tuple(v.co) for v in mesh.vertices], dtype=np.float64)
lo = coords.min(axis=0); hi = coords.max(axis=0)
span = np.maximum(hi - lo, 1e-8)
print('Hull local bounds:', lo.tolist(), hi.tolist(), flush=True)

def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)

def tri_raster(uv, positions, face_normal):
    # Ignore undersides and near-vertical walls: side artwork already owns them.
    # Blend the dorsal plate map onto both the roof and the upper shoulder;
    # the Tripo hull has a rounded crown rather than a flat top plane.
    top_weight = float(smoothstep(0.0, .62, face_normal[1]))
    if top_weight <= .015: return
    px = uv[:, 0] * (W - 1)
    py = uv[:, 1] * (H - 1)
    xmin = max(0, int(np.floor(px.min()))); xmax = min(W - 1, int(np.ceil(px.max())))
    ymin = max(0, int(np.floor(py.min()))); ymax = min(H - 1, int(np.ceil(py.max())))
    if xmax < xmin or ymax < ymin: return
    xx, yy = np.meshgrid(np.arange(xmin, xmax + 1), np.arange(ymin, ymax + 1))
    x0, y0 = px[0], py[0]; x1, y1 = px[1], py[1]; x2, y2 = px[2], py[2]
    denom = (y1-y2)*(x0-x2) + (x2-x1)*(y0-y2)
    if abs(denom) < 1e-10: return
    a = ((y1-y2)*(xx-x2) + (x2-x1)*(yy-y2)) / denom
    b = ((y2-y0)*(xx-x2) + (x0-x2)*(yy-y2)) / denom
    c = 1.0 - a - b
    inside = (a >= -1e-5) & (b >= -1e-5) & (c >= -1e-5)
    # Top UV tile only; the other hull copies share mesh data but land elsewhere.
    inside &= (yy >= int(H * .5)) & (xx < int(W * .5))
    if not inside.any(): return
    ax, ay = xx[inside], yy[inside]
    xyz = a[inside, None] * positions[0] + b[inside, None] * positions[1] + c[inside, None] * positions[2]
    # Dorsal concept nose is left; +X is the nose. Up in the art is +Z.
    u = np.clip((hi[0] - xyz[:, 0]) / span[0], 0, 1)
    vtop = np.clip((xyz[:, 2] - lo[2]) / span[2], 0, 1)
    # Fit the isolated craft in the concept crop to the model's silhouette.
    # Sampling the full white-margin image made the art collapse into a stripe.
    src_u = ref_u0 + u * (ref_u1 - ref_u0)
    src_top = ref_top0 + vtop * (ref_top1 - ref_top0)
    su = np.clip(np.rint(src_u * (rw - 1)).astype(np.int32), 0, rw - 1)
    sv = np.clip(np.rint((1.0 - src_top) * (rh - 1)).astype(np.int32), 0, rh - 1)
    valid = mask[sv, su]
    if not valid.any(): return
    ax, ay = ax[valid], ay[valid]
    col = ref_rgb[sv[valid], su[valid]]
    w = top_weight
    atlas[ay, ax, :3] = atlas[ay, ax, :3] * (1.0 - w) + col * w
    changed[ay, ax] = True
    covered[ay, ax] = True

mesh.calc_loop_triangles()
for tri in mesh.loop_triangles:
    poly = mesh.polygons[tri.polygon_index]
    loop_ids = list(tri.loops)
    uv = np.array([tuple(uv_layer.data[i].uv) for i in loop_ids], dtype=np.float64)
    vids = list(tri.vertices)
    xyz = coords[vids]
    tri_raster(uv, xyz, np.array(poly.normal, dtype=np.float64))

underside_changed = np.zeros((H, W), dtype=np.bool_)
if args.underside_reference:
    underside = bpy.data.images.load(str(args.underside_reference), check_existing=False)
    uw, uh = underside.size
    underside_pixels = np.empty(uw * uh * 4, dtype=np.float32)
    underside.pixels.foreach_get(underside_pixels)
    underside_rgba = underside_pixels.reshape(uh, uw, 4)
    underside_rgb = underside_rgba[:, :, :3]
    underside_mask = edge_connected_foreground(underside_rgb)
    uy, ux = np.where(underside_mask)
    if len(ux) < 1000: raise RuntimeError('Could not segment foreground from underside concept')
    uu0, uu1 = float(ux.min())/uw, float(ux.max()+1)/uw
    uv0, uv1 = float(uy.min())/uh, float(uy.max()+1)/uh
    print(f'UNDERSIDE artwork bounds: u={uu0:.4f}..{uu1:.4f}, top-v={uv0:.4f}..{uv1:.4f}', flush=True)

    def tri_raster_bottom(uv, positions, face_normal):
        bottom_weight = float(smoothstep(0.0, .62, -face_normal[1]))
        if bottom_weight <= .015: return
        px = uv[:, 0] * (W - 1); py = uv[:, 1] * (H - 1)
        xmin=max(0,int(np.floor(px.min())));xmax=min(W-1,int(np.ceil(px.max())))
        ymin=max(0,int(np.floor(py.min())));ymax=min(H-1,int(np.ceil(py.max())))
        if xmax<xmin or ymax<ymin:return
        xx,yy=np.meshgrid(np.arange(xmin,xmax+1),np.arange(ymin,ymax+1))
        x0,y0=px[0],py[0];x1,y1=px[1],py[1];x2,y2=px[2],py[2]
        denom=(y1-y2)*(x0-x2)+(x2-x1)*(y0-y2)
        if abs(denom)<1e-10:return
        a=((y1-y2)*(xx-x2)+(x2-x1)*(yy-y2))/denom
        b=((y2-y0)*(xx-x2)+(x0-x2)*(yy-y2))/denom
        c=1.0-a-b
        inside=(a>=-1e-5)&(b>=-1e-5)&(c>=-1e-5)&(yy>=int(H*.5))&(xx<int(W*.5))
        if not inside.any():return
        ax,ay=xx[inside],yy[inside]
        xyz=a[inside,None]*positions[0]+b[inside,None]*positions[1]+c[inside,None]*positions[2]
        # The underside reference is presented from below with its nose on the
        # left. Looking at the same +X-nose hull from -Y reverses screen-right,
        # so its image-space U must be mirrored relative to the dorsal map.
        u=np.clip((xyz[:,0]-lo[0])/span[0],0,1)
        vtop=np.clip((hi[2]-xyz[:,2])/span[2],0,1)
        su=np.clip(np.rint((uu0+u*(uu1-uu0))*(uw-1)).astype(np.int32),0,uw-1)
        sv=np.clip(np.rint((1-(uv0+vtop*(uv1-uv0)))*(uh-1)).astype(np.int32),0,uh-1)
        valid=underside_mask[sv,su]
        if not valid.any():return
        ax,ay=ax[valid],ay[valid];col=underside_rgb[sv[valid],su[valid]]
        atlas[ay,ax,:3]=atlas[ay,ax,:3]*(1-bottom_weight)+col*bottom_weight
        underside_changed[ay,ax]=True

    for tri in mesh.loop_triangles:
        poly=mesh.polygons[tri.polygon_index]; loop_ids=list(tri.loops)
        uv=np.array([tuple(uv_layer.data[i].uv) for i in loop_ids],dtype=np.float64)
        xyz=coords[list(tri.vertices)]
        tri_raster_bottom(uv,xyz,np.array(poly.normal,dtype=np.float64))

wing_changed = np.zeros((H, W), dtype=np.bool_)
if args.wing_reference:
    wing = bpy.data.images.load(str(args.wing_reference), check_existing=False)
    ww, wh = wing.size
    wing_pixels = np.empty(ww * wh * 4, dtype=np.float32)
    wing.pixels.foreach_get(wing_pixels)
    wing_rgba = wing_pixels.reshape(wh, ww, 4)
    wing_rgb = wing_rgba[:, :, :3]
    wing_mask = edge_connected_foreground(wing_rgb)
    wy, wx = np.where(wing_mask)
    if len(wx) < 1000: raise RuntimeError('Could not segment foreground from wing concept')
    wu0, wu1 = float(wx.min()) / ww, float(wx.max() + 1) / ww
    wv0, wv1 = float(wy.min()) / wh, float(wy.max() + 1) / wh
    wing_meshes = [o for o in scene.objects if o.type == 'MESH' and o.name.startswith('Wing_') and o.name.endswith('_LOD0')]
    if not wing_meshes: raise RuntimeError('No Wing_*_LOD0 objects found')
    wing_mesh = wing_meshes[0].data
    wing_uv = wing_mesh.uv_layers.get('AtlasUV')
    if wing_uv is None: raise RuntimeError('Wing mesh has no AtlasUV layer')
    wing_coords = np.array([tuple(v.co) for v in wing_mesh.vertices], dtype=np.float64)
    wing_lo = wing_coords.min(axis=0); wing_hi = wing_coords.max(axis=0)
    wing_span = np.maximum(wing_hi - wing_lo, 1e-8)

    def tri_raster_wing(uv, positions, face_normal):
        weight = float(smoothstep(0.05, .7, face_normal[1]))
        if weight <= .01: return
        px = uv[:, 0] * (W - 1); py = uv[:, 1] * (H - 1)
        xmin=max(0,int(np.floor(px.min())));xmax=min(W-1,int(np.ceil(px.max())))
        ymin=max(0,int(np.floor(py.min())));ymax=min(H-1,int(np.ceil(py.max())))
        if xmax<xmin or ymax<ymin:return
        xx,yy=np.meshgrid(np.arange(xmin,xmax+1),np.arange(ymin,ymax+1))
        x0,y0=px[0],py[0];x1,y1=px[1],py[1];x2,y2=px[2],py[2]
        denom=(y1-y2)*(x0-x2)+(x2-x1)*(y0-y2)
        if abs(denom)<1e-10:return
        a=((y1-y2)*(xx-x2)+(x2-x1)*(yy-y2))/denom
        b=((y2-y0)*(xx-x2)+(x0-x2)*(yy-y2))/denom
        c=1.0-a-b
        inside=(a>=-1e-5)&(b>=-1e-5)&(c>=-1e-5)&(xx>=int(W*.5))&(yy>=int(H*.5))
        if not inside.any():return
        ax,ay=xx[inside],yy[inside]
        xyz=a[inside,None]*positions[0]+b[inside,None]*positions[1]+c[inside,None]*positions[2]
        # The source view puts the wing root to image-right; local +X is the
        # root, so preserve that direction in the projected texture.
        u=np.clip((xyz[:,0]-wing_lo[0])/wing_span[0],0,1)
        vtop=np.clip((xyz[:,2]-wing_lo[2])/wing_span[2],0,1)
        su=np.clip(np.rint((wu0+u*(wu1-wu0))*(ww-1)).astype(np.int32),0,ww-1)
        sv=np.clip(np.rint((1-(wv0+vtop*(wv1-wv0)))*(wh-1)).astype(np.int32),0,wh-1)
        valid=wing_mask[sv,su]
        if not valid.any():return
        ax,ay=ax[valid],ay[valid]
        col=wing_rgb[sv[valid],su[valid]]
        atlas[ay,ax,:3]=atlas[ay,ax,:3]*(1-weight)+col*weight
        wing_changed[ay,ax]=True

    wing_mesh.calc_loop_triangles()
    for tri in wing_mesh.loop_triangles:
        poly=wing_mesh.polygons[tri.polygon_index]
        uv=np.array([tuple(wing_uv.data[i].uv) for i in tri.loops],dtype=np.float64)
        tri_raster_wing(uv,wing_coords[list(tri.vertices)],np.array(poly.normal,dtype=np.float64))

if not changed.any(): raise RuntimeError('Dorsal overlay did not touch atlas pixels')
print(f'Dorsal projection coverage: {covered.sum()} atlas texels; '
      f'underside projection coverage: {underside_changed.sum()} atlas texels; '
      f'wing-top projection coverage: {wing_changed.sum()} atlas texels', flush=True)
base.pixels.foreach_set(atlas.reshape(-1))
base.filepath_raw = str(args.output.parent / 'Textures' / 'Kestrel_BaseColor.png')
base.file_format = 'PNG'
base.save()
# Keep blend self-contained and its external texture path usable by Unity.
base.pack()

# Update the visible LOD0 render and save a side-by-side diagnostics pair.
for obj in scene.objects:
    if obj.type == 'MESH':
        obj.hide_render = not obj.name.endswith('_LOD0')
        obj.hide_set(not obj.name.endswith('_LOD0'))
for obj in scene.objects:
    if obj.type == 'MESH' and obj.name.endswith('_LOD0'): obj.hide_set(False)

# Ensure the saved material points at the edited image object.
for mat in bpy.data.materials:
    for node in mat.node_tree.nodes if mat.use_nodes else []:
        if node.type == 'TEX_IMAGE' and node.image and node.image.name == base.name:
            node.image = base

camera_data = bpy.data.cameras.new('Dorsal bake review camera')
camera = bpy.data.objects.new('Dorsal bake review camera', camera_data); scene.collection.objects.link(camera)
scene.camera = camera; camera_data.type = 'ORTHO'
targets = [o for o in scene.objects if o.type == 'MESH' and o.name.endswith('_LOD0')]
points = [o.matrix_world @ Vector(c) for o in targets for c in o.bound_box]
lo_w = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
hi_w = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
center = (lo_w + hi_w) * .5; extent = max(hi_w - lo_w)
scene.render.engine = 'BLENDER_EEVEE_NEXT'; scene.render.resolution_x = 1200; scene.render.resolution_y = 900
scene.render.resolution_percentage = 100; scene.render.image_settings.file_format = 'PNG'; scene.render.film_transparent = True
for label, direction, up in [('top', Vector((0,1,.02)), Vector((0,0,1))),
                             ('quarter', Vector((.22,.72,-1)), Vector((0,1,0))),
                             ('underside', Vector((0,-1,.02)), Vector((0,0,-1)))]:
    camera.location = center + direction.normalized() * extent * 2.0
    camera.rotation_euler = (center - camera.location).to_track_quat('-Z','Y').to_euler()
    camera_data.ortho_scale = extent * 1.26
    scene.render.filepath = str(args.render_dir / f'{label}.png')
    bpy.ops.render.render(write_still=True)

# Keep the review FBX usable by Unity as well as the editable Blender scene.
bpy.ops.object.select_all(action='DESELECT')
export_meshes = [o for o in scene.objects if o.type == 'MESH']
for obj in export_meshes: obj.select_set(True)
if export_meshes:
    bpy.context.view_layer.objects.active = next((o for o in export_meshes if o.name.endswith('_LOD0')), export_meshes[0])
    bpy.ops.export_scene.fbx(filepath=str(args.output.with_suffix('.fbx')), use_selection=True,
                             object_types={'MESH'}, add_leaf_bones=False,
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                             path_mode='COPY', embed_textures=False, mesh_smooth_type='FACE')
bpy.ops.wm.save_as_mainfile(filepath=str(args.output))
report = {
    'source_build': str(args.blend),
    'dorsal_reference': str(args.reference),
    'underside_reference': str(args.underside_reference) if args.underside_reference else None,
    'foreground_fraction': float(mask.mean()),
    'foreground_bounds_uv_top_origin': [ref_u0, ref_top0, ref_u1, ref_top1],
    'atlas_size': [W, H],
    'dorsal_projected_texels': int(covered.sum()),
    'underside_projected_texels': int(underside_changed.sum()),
    'wing_reference': str(args.wing_reference) if args.wing_reference else None,
    'wing_top_projected_texels': int(wing_changed.sum()),
    'dorsal_atlas_coverage_fraction': float(covered.sum() / (W * H)),
    'underside_atlas_coverage_fraction': float(underside_changed.sum() / (W * H)),
    'wing_top_atlas_coverage_fraction': float(wing_changed.sum() / (W * H)),
    'active_build_changed': False,
    'limitations': ['color projection only reaches matching top/bottom hull faces',
                    'wing sides and undersides still need dedicated views',
                    'texture cannot add missing physical panel or canopy geometry'],
}
(args.output.parent / 'dorsal-overlay-report.json').write_text(json.dumps(report, indent=2), encoding='utf8')
print('Saved review bake:', args.output, flush=True)
