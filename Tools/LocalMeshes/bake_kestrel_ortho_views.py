"""Reset Kestrel hull UVs to a six-view atlas and bake exactly one view per run.

The hull owns the upper-left 2048x2048 quadrant of the shared 4096 atlas.
That quadrant is split into six non-overlapping directional charts. Each call
may paint one chart only; prior charts and the other three part quadrants are
left byte-for-byte unchanged. Run --reset-uv once, then feed each output blend
into the next call in this order: top, bottom, port, starboard, nose, aft.
"""
import argparse
import hashlib
import json
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector

VIEWS = ("top", "bottom", "port", "starboard", "nose", "aft")
TILE_ORDER = {"top": 0, "bottom": 1, "port": 2, "starboard": 3, "nose": 4, "aft": 5}
TILE_COLS, TILE_ROWS = 2, 3
ATLAS_SIZE = 4096
HULL_UV_MIN, HULL_UV_MAX = 0.0, 0.5
HULL_V_MIN, HULL_V_MAX = 0.5, 1.0
CHART_NAME = "AtlasUV_SixView_v2"

ap = argparse.ArgumentParser(description=__doc__)
ap.add_argument("--view", required=True, choices=VIEWS)
ap.add_argument("--reference", required=True, type=Path)
ap.add_argument("--output", required=True, type=Path)
ap.add_argument("--report", required=True, type=Path)
ap.add_argument("--reset-uv", action="store_true", help="Create fresh six-view UVs and a clean hull quadrant")
ap.add_argument("--render", type=Path, help="Optional orthographic view render for review")
args = ap.parse_args(sys.argv[sys.argv.index("--") + 1:])
if not args.reference.is_file():
    raise SystemExit(f"Missing view reference: {args.reference}")
if not bpy.data.filepath:
    raise SystemExit("Open a source ship blend before running the bake")

scene = bpy.context.scene
view_index = TILE_ORDER[args.view]
tile_col, tile_row = view_index % TILE_COLS, view_index // TILE_COLS
padding = 12

def tile_bounds(view):
    idx = TILE_ORDER[view]
    col, row = idx % TILE_COLS, idx // TILE_COLS
    # Exact integer pixel bounds shared by the unwrap, rasterizer and verifier.
    return (col * 1024, 4096 - round((row + 1) * 2048 / 3),
            (col + 1) * 1024, 4096 - round(row * 2048 / 3))

tile_x0, tile_y0, tile_x1, tile_y1 = tile_bounds(args.view)

def hull_lod_objects():
    result = [o for o in scene.objects if o.type == "MESH" and o.name.startswith("Hull_")
              and any(o.name.endswith(f"_LOD{n}") for n in range(3))]
    if not result:
        result = [o for o in scene.objects if o.type == "MESH" and o.name == "Hull_LOD0"]
    if not result:
        raise RuntimeError("Could not find LOD0-2 hull mesh objects")
    return result

def hull_objects():
    result = [o for o in hull_lod_objects() if o.name.endswith("_LOD0")]
    if not result:
        raise RuntimeError("Could not find LOD0 hull mesh objects")
    return result

def canonical_direction(poly, obj):
    # UV projection is in hull-local coordinates, so classify in that frame too.
    n = poly.normal.normalized()
    axis = max(range(3), key=lambda i: abs(n[i]))
    sign = 1 if n[axis] >= 0 else -1
    # Canonical project axes: X forward, Y up, Z starboard.
    return {(0, 1): "nose", (0, -1): "aft",
            (1, 1): "top", (1, -1): "bottom",
            (2, 1): "starboard", (2, -1): "port"}[(axis, sign)]

def view_projection(point, view):
    x, y, z = point
    # (horizontal, vertical, camera depth); positive depth is toward camera.
    return {
        "top": ( -x,  z,  y),
        "bottom": (-x, -z, -y),
        "port": (-x,  y, -z),
        "starboard": (x, y, z),
        "nose": (-z, y, x),
        "aft": (z, y, -x),
    }[view]

def set_six_view_uv(objects):
    # Bounds use the canonical, unplaced hull coordinates so all instances share
    # one stable UV chart and the orthographic view convention.
    reference_obj = next((o for o in objects if o.name.endswith("_LOD0")), objects[0])
    mesh = reference_obj.data
    coords = np.array([tuple(v.co) for v in mesh.vertices], dtype=np.float64)
    bounds = {}
    for view in VIEWS:
        projected = np.array([view_projection(c, view)[:2] for c in coords])
        bounds[view] = (projected.min(axis=0), projected.max(axis=0))
    for obj in objects:
        data = obj.data
        old = data.uv_layers.get(CHART_NAME)
        if old:
            data.uv_layers.remove(old)
        uv_layer = data.uv_layers.new(name=CHART_NAME)
        for poly in data.polygons:
            view = canonical_direction(poly, obj)
            min2, max2 = bounds[view]
            span = np.maximum(max2 - min2, 1e-8)
            x0, y0, x1, y1 = tile_bounds(view)
            for loop_index in poly.loop_indices:
                vert = data.vertices[data.loops[loop_index].vertex_index]
                pu, pv, _ = view_projection(vert.co, view)
                nu = np.clip((pu - min2[0]) / span[0], 0.0, 1.0)
                nv = np.clip((pv - min2[1]) / span[1], 0.0, 1.0)
                # Coordinates denote pixel centers, matching UV*size-.5 below.
                uv_layer.data[loop_index].uv = (
                    (x0 + padding + .5 + nu * (x1-x0-2*padding-1)) / ATLAS_SIZE,
                    (y0 + padding + .5 + nv * (y1-y0-2*padding-1)) / ATLAS_SIZE)
        # Unity samples UV0: remove the invalid older layouts, not just their
        # active-render flag. All hull LODs must export the same layout in UV0.
        for old_name in [layer.name for layer in data.uv_layers if layer.name != CHART_NAME]:
            data.uv_layers.remove(data.uv_layers[old_name])
        uv_layer = data.uv_layers.get(CHART_NAME)
        data.uv_layers.active = uv_layer
        for layer in data.uv_layers:
            layer.active_render = (layer == uv_layer)

def image_array(image):
    w, h = image.size
    if not w or not h:
        raise RuntimeError(f'Missing image pixels for {image.name}: {image.filepath}; preserve blend-relative textures when moving the source')
    raw = np.empty(w * h * 4, dtype=np.float32)
    image.pixels.foreach_get(raw)
    return raw.reshape((h, w, 4)), w, h

def background_mask(rgb):
    # The generated turnaround uses a flat cool-gray studio background. Grow
    # only the border-connected, low-chroma region so white hull panels remain.
    from collections import deque
    h, w, _ = rgb.shape
    corners = np.array([rgb[1, 1], rgb[1, w-2], rgb[h-2, 1], rgb[h-2, w-2]])
    bg = np.median(corners, axis=0)
    delta = np.max(np.abs(rgb - bg), axis=2)
    chroma = np.max(rgb, axis=2) - np.min(rgb, axis=2)
    candidate = (delta < 0.075) & (chroma < 0.06)
    visited = np.zeros((h, w), dtype=np.bool_)
    q = deque()
    for x in range(w):
        if candidate[0, x]: visited[0, x] = True; q.append((0, x))
        if candidate[h-1, x] and not visited[h-1, x]: visited[h-1, x] = True; q.append((h-1, x))
    for y in range(1, h-1):
        if candidate[y, 0] and not visited[y, 0]: visited[y, 0] = True; q.append((y, 0))
        if candidate[y, w-1] and not visited[y, w-1]: visited[y, w-1] = True; q.append((y, w-1))
    while q:
        y, x = q.pop()
        for yy, xx in ((y-1,x),(y+1,x),(y,x-1),(y,x+1)):
            if 0 <= yy < h and 0 <= xx < w and candidate[yy, xx] and not visited[yy, xx]:
                visited[yy, xx] = True; q.append((yy, xx))
    return ~visited

def raster_triangle(atlas, depth_buffer, chart_written, uv, xyz, image_rgb, foreground, bounds, view, registration):
    px = uv[:, 0] * ATLAS_SIZE - .5
    py = uv[:, 1] * ATLAS_SIZE - .5
    xmin = max(tile_x0 + padding, int(np.floor(px.min())))
    xmax = min(tile_x1 - padding - 1, int(np.ceil(px.max())))
    ymin = max(tile_y0 + padding, int(np.floor(py.min())))
    ymax = min(tile_y1 - padding - 1, int(np.ceil(py.max())))
    if xmin > xmax or ymin > ymax:
        return 0
    xx, yy = np.meshgrid(np.arange(xmin, xmax + 1), np.arange(ymin, ymax + 1))
    x0,y0=px[0],py[0]; x1,y1=px[1],py[1]; x2,y2=px[2],py[2]
    den=(y1-y2)*(x0-x2)+(x2-x1)*(y0-y2)
    if abs(den) < 1e-10: return 0
    a=((y1-y2)*(xx-x2)+(x2-x1)*(yy-y2))/den
    b=((y2-y0)*(xx-x2)+(x0-x2)*(yy-y2))/den
    c=1-a-b
    inside=(a>=-1e-5)&(b>=-1e-5)&(c>=-1e-5)
    if not inside.any(): return 0
    ix,iy=xx[inside],yy[inside]
    xyzp=a[inside,None]*xyz[0]+b[inside,None]*xyz[1]+c[inside,None]*xyz[2]
    projected=np.array([view_projection(p, view) for p in xyzp])
    min2,max2=bounds[view]
    h,w=foreground.shape
    # Register foreground to physical mesh coordinates with ONE pixel/metre
    # scale. Empty image margins must not shrink paint on the ship. Independent
    # X/Y stretch would hide an inconsistent concept or incorrect mesh shape.
    sample_x = registration['source_center'][0] + (projected[:,0] - (min2[0]+max2[0])*.5) * registration['pixels_per_unit']
    sample_y = registration['source_center'][1] + (projected[:,1] - (min2[1]+max2[1])*.5) * registration['pixels_per_unit']
    in_image=(sample_x>=0)&(sample_x<w)&(sample_y>=0)&(sample_y<h)
    su=np.clip(np.rint(sample_x).astype(np.int32),0,w-1)
    sv=np.clip(np.rint(sample_y).astype(np.int32),0,h-1)
    fg=in_image & foreground[sv,su]
    # Surfaces that project beyond the painted silhouette keep the neutral hull
    # finish. This avoids baking the studio background onto the ship.
    cam_depth=projected[:,2]
    current_depth=depth_buffer[iy,ix]
    # Depth test resolves intentional UV overlaps from an orthographic view;
    # it always keeps the face nearest the selected camera.
    visible=fg & (cam_depth >= current_depth - 1e-6)
    if not visible.any(): return 0
    ix,iy=ix[visible],iy[visible]
    cols=image_rgb[sv[visible],su[visible]]
    atlas[iy,ix,:3]=cols
    atlas[iy,ix,3]=1.0
    depth_buffer[iy,ix]=cam_depth[visible]
    chart_written[iy,ix]=True
    return int(visible.sum())

objects = hull_objects()
all_hull_lods = hull_lod_objects()
base = bpy.data.images.get("Kestrel_BaseColor_4096")
if base is None:
    base = next((im for im in bpy.data.images if im.name.startswith("Kestrel_BaseColor")), None)
if base is None:
    raise RuntimeError("No shared Kestrel base-color atlas found")
if tuple(base.size) != (ATLAS_SIZE, ATLAS_SIZE):
    raise RuntimeError(f"Unexpected atlas size: {tuple(base.size)}")

atlas_before, _, _ = image_array(base)
atlas = atlas_before.copy()
if args.reset_uv:
    set_six_view_uv(all_hull_lods)
    # Clear only the hull-owned quadrant. Wing, gear, drive and every other
    # atlas texel retain their original values. Each of six charts starts neutral.
    atlas[2048:, :2048, :] = np.array((0.34, 0.33, 0.29, 1.0), dtype=np.float32)
    scene["kestrel_six_view_uv"] = CHART_NAME
    scene["kestrel_six_view_written"] = ""
elif scene.get("kestrel_six_view_uv") != CHART_NAME:
    raise RuntimeError("This blend has no fresh six-view hull UVs; run the first pass with --reset-uv")

written = set(filter(None, str(scene.get("kestrel_six_view_written", "")).split(",")))
if args.view in written:
    raise RuntimeError(f"Refusing to recolor the already-baked {args.view} chart")
if list(v for v in VIEWS if v in written) != list(VIEWS[:view_index]):
    raise RuntimeError("Load the preceding pass and bake top, bottom, port, starboard, nose, aft in order")

ref = bpy.data.images.load(str(args.reference), check_existing=False)
ref_pixels, rw, rh = image_array(ref)
rgb = ref_pixels[:, :, :3]
mask = background_mask(rgb)
foreground_pixels = int(mask.sum())
if foreground_pixels < 10000:
    raise RuntimeError(f"Background segmentation failed for {args.view}: only {foreground_pixels} foreground pixels")
coords = np.array([tuple(v.co) for v in objects[0].data.vertices], dtype=np.float64)
bounds = {}
for view in VIEWS:
    p = np.array([view_projection(c, view)[:2] for c in coords])
    bounds[view] = (p.min(axis=0), p.max(axis=0))
ys, xs = np.nonzero(mask)
source_box = [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())]
min2, max2 = bounds[args.view]
registration = {
    'foreground_bounds_bottom_origin': source_box,
    'source_center': [(source_box[0]+source_box[2])*.5, (source_box[1]+source_box[3])*.5],
    'pixels_per_unit': (source_box[2]-source_box[0]) / float(max2[0]-min2[0]),
    'mesh_projected_span': (max2-min2).tolist(),
    'mode': 'uniform-scale foreground registration; no independent axis stretch',
}

depth = np.full((ATLAS_SIZE, ATLAS_SIZE), -np.inf, dtype=np.float32)
written_mask = np.zeros((ATLAS_SIZE, ATLAS_SIZE), dtype=np.bool_)
data = objects[0].data
uv = data.uv_layers.get(CHART_NAME)
if uv is None:
    raise RuntimeError("Six-view UV layer is missing")
data.calc_loop_triangles()
painted = 0
triangles = 0
for tri in data.loop_triangles:
    poly = data.polygons[tri.polygon_index]
    direction_name = canonical_direction(poly, objects[0])
    if direction_name != args.view:
        continue
    loop_ids = list(tri.loops)
    uvs = np.array([tuple(uv.data[i].uv) for i in loop_ids], dtype=np.float64)
    xyz = np.array([tuple(data.vertices[i].co) for i in tri.vertices], dtype=np.float64)
    painted += raster_triangle(atlas, depth, written_mask, uvs, xyz, rgb, mask, bounds, args.view, registration)
    triangles += 1
print(f"ORTHO_DIAG view={args.view} triangles={triangles} of {len(data.loop_triangles)}; foreground={mask.mean():.3f}", flush=True)
if painted < 1000:
    raise RuntimeError(f"{args.view} view painted too few texels ({painted}); orientation or UV chart is wrong")

args.output.parent.mkdir(parents=True, exist_ok=True)
texture_dir = args.output.parent / "Textures" / args.output.stem
texture_dir.mkdir(parents=True, exist_ok=True)

def save_and_replace_packed_bytes(image, pixels, filename):
    image.pixels.foreach_set(pixels.reshape(-1))
    image.update()
    image.filepath_raw = str(texture_dir / filename)
    image.file_format = 'PNG'
    image.save()
    # pack() without data retains the previous packed file! Always explicitly
    # replace it with the newly saved bytes so reopening cannot restore the old bake.
    png = (texture_dir / filename).read_bytes()
    image.pack(data=png, data_len=len(png))
    image.use_fake_user = True
    return hashlib.sha256(png).hexdigest()

changed = np.any(atlas != atlas_before, axis=2)
non_hull_changed = int(changed[:2048,:].sum() + changed[2048:,2048:].sum())
earlier_changed = {}
for previous in VIEWS[:view_index]:
    x0,y0,x1,y1 = tile_bounds(previous)
    earlier_changed[previous] = int(changed[y0:y1,x0:x1].sum())
if non_hull_changed or any(earlier_changed.values()):
    raise RuntimeError(f'Pass modified protected pixels: {non_hull_changed}, {earlier_changed}')
new_pixels = int(changed[tile_y0:tile_y1,tile_x0:tile_x1].sum())
if new_pixels == 0:
    raise RuntimeError('Selected chart did not change')
saved_hashes = {'base_color': save_and_replace_packed_bytes(base, atlas, 'Kestrel_BaseColor.png')}
for channel_name, image_name, filename, neutral in (
    ('normal', 'Kestrel_Normal_4096', 'Kestrel_Normal.png', (.5,.5,1.,1.)),
    ('metal', 'Kestrel_Metal_4096', 'Kestrel_MetallicSmoothness.png', (.35,.35,.35,.38)),
    ('roughness', 'Kestrel_Roughness_4096', 'Kestrel_Roughness_source.png', (.62,.62,.62,1.)),
):
    channel = bpy.data.images.get(image_name)
    if channel is None:
        source_path = Path(bpy.data.filepath).parent / 'Textures' / filename
        if not source_path.exists():
            raise RuntimeError(f'Missing companion atlas: {source_path}')
        channel = bpy.data.images.load(str(source_path), check_existing=False)
        channel.name = image_name
    channel.colorspace_settings.name = 'Non-Color'
    pixels,_,_ = image_array(channel)
    if args.reset_uv:
        # Old tangent detail/material islands cannot follow a different UV map.
        pixels[2048:,:2048,:] = neutral
    saved_hashes[channel_name] = save_and_replace_packed_bytes(channel, pixels, filename)
written.add(args.view)
scene["kestrel_six_view_written"] = ",".join(v for v in VIEWS if v in written)

# Render only the hull from the same orthographic direction, for a direct review.
render_path = None
if args.render:
    args.render.parent.mkdir(parents=True, exist_ok=True)
    for obj in scene.objects:
        if obj.type == "MESH":
            obj.hide_render = not (obj.name.startswith("Hull_") and obj.name.endswith("_LOD0"))
    hull = objects[0]
    bpy.context.view_layer.update()
    points = [hull.matrix_world @ Vector(c) for c in hull.bound_box]
    center = sum(points, Vector()) / len(points)
    ext = hull.dimensions
    radius = max(ext) * 2.2
    cam_data = bpy.data.cameras.new("SixViewBakeReviewCamera")
    cam = bpy.data.objects.new("SixViewBakeReviewCamera", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam_data.type = "ORTHO"
    direction_up = {
        "top": (Vector((0,1,0)), Vector((0,0,1))),
        "bottom": (Vector((0,-1,0)), Vector((0,0,-1))),
        "port": (Vector((0,0,-1)), Vector((0,1,0))),
        "starboard": (Vector((0,0,1)), Vector((0,1,0))),
        "nose": (Vector((1,0,0)), Vector((0,1,0))),
        "aft": (Vector((-1,0,0)), Vector((0,1,0))),
    }
    direction, up = direction_up[args.view]
    cam.location = center + direction * radius
    view_dir = (center - cam.location).normalized()
    right = view_dir.cross(up).normalized()
    camera_up = (-view_dir).cross(right).normalized()
    cam.rotation_euler = Matrix((right, camera_up, -view_dir)).transposed().to_euler()
    cam_data.ortho_scale = max(ext) * 1.18
    if args.view in ("port", "starboard"):
        scene.render.resolution_x, scene.render.resolution_y = 1600, 720
    else:
        scene.render.resolution_x, scene.render.resolution_y = 1200, 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = True
    scene.render.filepath = str(args.render)
    bpy.ops.render.render(write_still=True)
    render_path = str(args.render)

args.report.parent.mkdir(parents=True, exist_ok=True)
report = {
    "view": args.view,
    "reference": str(args.reference),
    "reference_size": [rw, rh],
    "foreground_fraction": float(mask.mean()),
    "uv_layer": CHART_NAME,
    "uv_reset_this_pass": bool(args.reset_uv),
    "tile_row_major": [tile_col, tile_row],
    "tiles_written": list(v for v in VIEWS if v in written),
    "triangles_considered": triangles,
    "atlas_texels_written_this_pass": int(written_mask.sum()),
    "changed_texels_in_selected_chart": new_pixels,
    "prior_directional_charts_recolored": any(earlier_changed.values()),
    "prior_chart_changed_pixel_counts": earlier_changed,
    "non_hull_atlas_quadrants_modified": bool(non_hull_changed),
    "reference_registration": registration,
    "saved_png_sha256": saved_hashes,
    "texture_directory": str(texture_dir),
    "review_render": render_path,
}
args.report.write_text(json.dumps(report, indent=2), encoding="utf8")
bpy.ops.wm.save_as_mainfile(filepath=str(args.output))
print(json.dumps(report, indent=2), flush=True)
