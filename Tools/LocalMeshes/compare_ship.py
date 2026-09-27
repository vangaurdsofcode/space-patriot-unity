"""Render a bake diagnostic in Blender and compare it to the component art."""
import argparse
import html
import json
import math
import subprocess
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1]
ap = argparse.ArgumentParser(description=__doc__)
ap.add_argument('--build-id', default=None, help='Build under ArtDirection/Generated/KestrelK017')
ap.add_argument('--tool-root', type=Path, default=PROJECT.parents[1] / 'work')
ap.add_argument('--skip-render', action='store_true', help='Reuse existing Blender comparison views')
args = ap.parse_args()

if args.build_id:
    build = PROJECT / 'ArtDirection/Generated/KestrelK017' / args.build_id
else:
    build_id = (PROJECT / 'ArtDirection/Generated/KestrelK017/active-build.txt').read_text(encoding='utf-8').strip()
    build = PROJECT / 'ArtDirection/Generated/KestrelK017' / build_id
if not (build / 'Kestrel_K017.blend').is_file():
    raise SystemExit(f'No assembled Blender ship at {build}')
out = build / 'Renders/Comparison'
out.mkdir(parents=True, exist_ok=True)
blender = args.tool_root.resolve() / 'tools/blender-4.5.10-windows-x64/blender.exe'
if not args.skip_render:
    if not blender.exists():
        raise SystemExit(f'Blender not found: {blender}')
    subprocess.run([str(blender), '--background', '--python-exit-code', '1', '--python',
                    str(HERE / 'compare_ship_blender.py'), '--', '--blend', str(build/'Kestrel_K017.blend'),
                    '--output', str(out)], check=True)

config = json.loads((HERE / 'kestrel-kit.json').read_text(encoding='utf-8'))
PANEL = 512
PAD = 20
BG = (18, 24, 30)
FONT = ImageFont.truetype('arial.ttf', 18)
SMALL = ImageFont.truetype('arial.ttf', 14)

def foreground_reference(image):
    arr = np.asarray(image.convert('RGB'), dtype=np.uint8)
    # Reference boards use a near-white field; threshold keeps pale paint and
    # highlights inside the ship while excluding the clean background.
    mask = np.min(arr, axis=2) < 246
    ys, xs = np.where(mask)
    if len(xs) < 20:
        raise ValueError('Could not detect the reference foreground')
    return arr, mask, (int(xs.min()), int(ys.min()), int(xs.max()+1), int(ys.max()+1))

def render_mask_and_rgb(path):
    im = Image.open(path).convert('RGBA')
    arr = np.asarray(im)
    mask = arr[:, :, 3] > 24
    if mask.sum() < 20:
        # Some Blender/driver builds return opaque alpha despite transparent film.
        rgb = arr[:, :, :3]
        mask = np.min(rgb, axis=2) < 246
    return arr[:, :, :3], mask

def fit_pair(rgb_a, mask_a, box_a, rgb_b, mask_b, size=PANEL):
    inner = size - 72
    by,bx=np.where(mask_b)
    if len(bx)<20:
        raise ValueError('Could not detect model foreground')
    box_b=(int(bx.min()),int(by.min()),int(bx.max()+1),int(by.max()+1))
    def normalized(rgb, mask, box):
        x0, y0, x1, y1 = box
        crop_rgb = Image.fromarray(rgb).crop(box)
        crop_mask = Image.fromarray((mask*255).astype('uint8')).crop(box)
        scale = min(inner/max(1,x1-x0), inner/max(1,y1-y0))
        target = (max(1, round((x1-x0)*scale)), max(1, round((y1-y0)*scale)))
        crop_rgb = crop_rgb.resize(target, Image.Resampling.LANCZOS)
        crop_mask = crop_mask.resize(target, Image.Resampling.NEAREST)
        canvas_rgb = Image.new('RGB', (size,size), BG)
        canvas_mask = Image.new('L', (size,size), 0)
        p = ((size-target[0])//2, (size-target[1])//2)
        canvas_rgb.paste(crop_rgb, p)
        canvas_mask.paste(crop_mask, p)
        return np.asarray(canvas_rgb), np.asarray(canvas_mask)>127
    return normalized(rgb_a,mask_a,box_a), normalized(rgb_b,mask_b,box_b)

def hue_sat_delta(a, b, overlap):
    from colorsys import rgb_to_hsv
    aa = a[overlap].astype(np.float32)/255.0
    bb = b[overlap].astype(np.float32)/255.0
    if len(aa) == 0: return None, None, None, None
    # numpy vectorized RGB->HSV (hue in turns)
    def hsv(x):
        mx=x.max(axis=1); mn=x.min(axis=1); d=mx-mn
        h=np.zeros(len(x),dtype=np.float32)
        nz=d>1e-7
        r,g,bl=x[:,0],x[:,1],x[:,2]
        mr=nz & (mx==r); mg=nz & (mx==g); mb=nz & (mx==bl)
        h[mr]=((g[mr]-bl[mr])/d[mr])%6
        h[mg]=(bl[mg]-r[mg])/d[mg]+2
        h[mb]=(r[mb]-g[mb])/d[mb]+4
        h/=6
        s=np.divide(d,mx,out=np.zeros_like(d),where=mx>1e-7)
        return h,s
    ha,sa=hsv(aa); hb,sb=hsv(bb)
    sat_valid=(sa>.08)&(sb>.08)
    hue = np.abs(((ha[sat_valid]-hb[sat_valid]+.5)%1)-.5)*360 if sat_valid.any() else np.array([])
    sat_error=np.abs(sa-sb)*100
    hue_image=np.zeros_like(a,dtype=np.uint8)
    sat_image=np.zeros_like(a,dtype=np.uint8)
    if sat_valid.any():
        full_hue=np.zeros(len(aa),dtype=np.float32)
        full_hue[sat_valid]=np.abs(((ha[sat_valid]-hb[sat_valid]+.5)%1)-.5)*360
        # Hue error: green is close, red-to-magenta is increasingly wrong.
        heat=np.clip(full_hue/180,0,1)
        hue_colors=np.stack((255*heat,255*(1-heat),40+130*heat),axis=1).astype(np.uint8)
        hue_image[overlap]=hue_colors
    sat_level=np.clip(sat_error/45,0,1)
    sat_colors=np.stack((255*sat_level,180+75*(1-sat_level),255*(1-sat_level)),axis=1).astype(np.uint8)
    sat_image[overlap]=sat_colors
    return (float(np.mean(hue)) if hue.size else None, float(np.mean(sat_error)),
            Image.fromarray(hue_image),Image.fromarray(sat_image))

def score(ref_img, model_path, ref_mask_override=None):
    if ref_mask_override is None:
        ref_rgb, ref_mask, ref_box = foreground_reference(ref_img)
    else:
        ref_rgb=np.asarray(ref_img.convert('RGB'),dtype=np.uint8)
        ref_mask=ref_mask_override
        ys,xs=np.where(ref_mask)
        ref_box=(int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1))
    mesh_rgb, mesh_mask = render_mask_and_rgb(model_path)
    ys,xs=np.where(mesh_mask)
    if len(xs)<20: raise ValueError(f'Empty model silhouette: {model_path}')
    mesh_box=(int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1))
    (a,ma),(b,mb)=fit_pair(ref_rgb,ref_mask,ref_box,mesh_rgb,mesh_mask, PANEL)
    overlap=ma&mb
    union=ma|mb
    iou=float(overlap.sum()/max(1,union.sum()))
    area=float(mb.sum()/max(1,ma.sum()))
    yy,xx=np.mgrid[:PANEL,:PANEL]
    ca=(float(xx[ma].mean()),float(yy[ma].mean()))
    cb=(float(xx[mb].mean()),float(yy[mb].mean()))
    centroid=math.hypot(ca[0]-cb[0],ca[1]-cb[1])/PANEL
    hue,sat,hue_map,sat_map=hue_sat_delta(a,b,overlap)
    # Magenta = concept contour; cyan = mesh contour; white = agreement.
    edge_a=np.asarray(Image.fromarray(ma.astype('uint8')*255).filter(ImageFilter.FIND_EDGES))>0
    edge_b=np.asarray(Image.fromarray(mb.astype('uint8')*255).filter(ImageFilter.FIND_EDGES))>0
    overlay=np.zeros((PANEL,PANEL,3),dtype=np.uint8)
    overlay[ma & ~mb]=(255,55,185)
    overlay[mb & ~ma]=(40,230,255)
    overlay[ma & mb]=(78,105,105)
    overlay[edge_a & edge_b]=(255,255,255)
    overlay[edge_a & ~edge_b]=(255,55,185)
    overlay[edge_b & ~edge_a]=(40,230,255)
    panels=[Image.fromarray(a),Image.fromarray(b),Image.fromarray(overlay)]
    return {'silhouette_iou':iou,'mesh_to_reference_area':area,'centroid_offset_fraction':centroid,
            'mean_hue_error_degrees':hue,'mean_saturation_error_percentage_points':sat,
            'reference_pixels':int(ma.sum()),'mesh_pixels':int(mb.sum())}, panels, hue_map, sat_map

def card(title, image, subtitle=''):
    canvas=Image.new('RGB',(PANEL,PANEL+64),BG)
    canvas.paste(image,(0,64))
    draw=ImageDraw.Draw(canvas)
    draw.text((12,9),title,font=FONT,fill=(230,235,237))
    if subtitle: draw.text((12,36),subtitle,font=SMALL,fill=(150,169,179))
    return canvas

cards=[]; results={}
for part in config['parts']:
    category=part['id']
    ref=Image.open(PROJECT / part['reference']).convert('RGB')
    metrics, panels, hue_map, sat_map=score(ref,out/f'{category}_atlas.png')
    results[category]=metrics
    hue_map.save(out/f'{category}_hue-error.png')
    sat_map.save(out/f'{category}_saturation-error.png')
    subtitles=[f"Concept reference",f"Atlas sample · IoU {metrics['silhouette_iou']:.3f}",
               f"magenta ref · cyan mesh · white overlap\nΔhue {metrics['mean_hue_error_degrees'] if metrics['mean_hue_error_degrees'] is not None else 'n/a'}° · Δsat {metrics['mean_saturation_error_percentage_points']:.1f}pp"]
    for label,panel,sub in zip(('REFERENCE','BAKED MESH','EDGE OVERLAY'),panels,subtitles):
        cards.append(card(f'{category} / {label}',panel,sub))

# Match the complete assembled ship to its fleet concept using a similarly
# elevated nose-quarter camera. This retains whole-ship silhouette and spacing.
fleet=Image.open(PROJECT/'ArtDirection/fleet-target.png').convert('RGB')
fleet_box=(70,0,770,420)
fleet_crop=fleet.crop(fleet_box)
fleet_arr=np.asarray(fleet_crop,dtype=np.int16)
background=np.median(np.concatenate((fleet_arr[0:12,600:630].reshape(-1,3),fleet_arr[405:420,500:540].reshape(-1,3))),axis=0)
fleet_mask=np.linalg.norm(fleet_arr-background,axis=2)>12
# Exclude the residual heading/callouts while retaining the ship's nose and legs.
fleet_mask[:72,:175]=False
fleet_mask[70:145,:55]=False
fleet_mask[310:,:75]=False
metrics,panels,hue_map,sat_map=score(fleet_crop,out/'ShipQuarter_atlas.png',fleet_mask)
metrics['reference']='ArtDirection/fleet-target.png Kestrel ship crop, approximate foreground mask'
results['ShipQuarter']=metrics
hue_map.save(out/'ShipQuarter_hue-error.png')
sat_map.save(out/'ShipQuarter_saturation-error.png')
for label,panel,sub in zip(('CONCEPT SHIP','ASSEMBLED SHIP','SHIP OVERLAY'),panels,
    ('Fleet Kestrel concept crop',f"All parts · IoU {metrics['silhouette_iou']:.3f}",
     'magenta ref · cyan model · white overlap')):
    cards.append(card(label,panel,sub))

# Keep placement data visible in the report; unlike part-normalized overlays,
# this records every component transform in the ship coordinate system.
placements={p['id']:{i['name']:({'mirror_of':i['mirror_of'], 'mirror_axis':i.get('mirror_axis','Z'),
                               'mirror_plane_offset_m':i.get('mirror_offset',0)} if i.get('mirror_of') else
                              {'position_m':i['position'],'rotation_deg':i['rotation']})
                       for i in p['instances']} for p in config['parts']}
results['mount_transforms']=placements

gap=14; cols=3; rows=math.ceil(len(cards)/cols)
board=Image.new('RGB',(cols*PANEL+(cols+1)*gap,rows*(PANEL+64)+(rows+1)*gap),BG)
for i,c in enumerate(cards): board.paste(c,(gap+(i%cols)*(PANEL+gap),gap+(i//cols)*(PANEL+64+gap)))
board_path=out/'comparison-board.png';board.save(board_path)
view_names=('Nose','Aft','Starboard','Port','Top','Bottom')
view_cards=[]
for view_name in view_names:
    im=Image.open(out/f'Ship_View_{view_name}.png').convert('RGBA')
    clean=Image.new('RGB',im.size,BG)
    clean.paste(im,(0,0),im.getchannel('A'))
    clean=clean.resize((PANEL,PANEL),Image.Resampling.LANCZOS)
    view_cards.append(card('SHIP / '+view_name.upper(),clean,'Atlas-textured model view · '+view_name))
view_board=Image.new('RGB',(3*PANEL+4*gap,2*(PANEL+64)+3*gap),BG)
for i,c in enumerate(view_cards):
    view_board.paste(c,(gap+(i%3)*(PANEL+gap),gap+(i//3)*(PANEL+64+gap)))
view_board.save(out/'six-view-ship-board.png')
(out/'comparison-metrics.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
html_rows=[]
for category,metrics in results.items():
    html_rows.append(f'<tr><th>{html.escape(category)}</th><td>{metrics.get("silhouette_iou",0):.3f}</td><td>{metrics.get("mesh_to_reference_area", "—")}</td><td>{metrics.get("mean_hue_error_degrees", "—")}</td><td>{metrics.get("mean_saturation_error_percentage_points", "—")}</td></tr>')
color_links=''.join(f'<section><h2>{html.escape(category)} color error</h2><p>Hue map: green close, magenta high error. Saturation map: blue close, yellow high error.</p><img src="{html.escape(category)}_hue-error.png" alt="Hue difference"><img src="{html.escape(category)}_saturation-error.png" alt="Saturation difference"></section>' for category in (*[p['id'] for p in config['parts']],'ShipQuarter'))
model_view_sections=''.join(f'<section><h2>{html.escape(name)} model views</h2>'+''.join(f'<figure><figcaption>{html.escape(view)}</figcaption><img src="{html.escape(name)}_View_{html.escape(view)}.png" alt="{html.escape(name)} from {html.escape(view)} view"></figure>' for view in view_names)+'</section>' for name in ('Ship',*[p['id'] for p in config['parts']]))
page=f'''<!doctype html><meta charset="utf-8"><title>Kestrel bake comparison</title><style>body{{background:#10161b;color:#e6edef;font:16px system-ui;margin:2rem}}img{{max-width:100%;height:auto}}section img{{width:min(48%,700px);image-rendering:auto}}figure{{display:inline-block;vertical-align:top;width:min(30%,480px);margin:1rem}}figure img{{width:100%}}table{{border-collapse:collapse;margin:1.5rem 0}}td,th{{border:1px solid #40515c;padding:.55rem .8rem;text-align:right}}th:first-child{{text-align:left}}.key{{color:#9eacb4}}</style><h1>Kestrel reference and bake check</h1><p class="key">Component images are independently centered and scaled to compare form. The ship overlay keeps all component offsets together. Magenta: concept; cyan: mesh; white: matching edges. Hue map: green close, magenta high error. Saturation map: blue close, yellow high error. Six-direction model views show the produced atlas from Nose, Aft, Starboard, Port, Top, and Bottom.</p><table><tr><th>View</th><th>Silhouette IoU</th><th>Area ratio</th><th>Mean hue error °</th><th>Saturation error pp</th></tr>{''.join(html_rows)}</table><img src="comparison-board.png" alt="Reference, baked mesh and edge overlays"><h2>Assembled ship from six directions</h2><img src="six-view-ship-board.png" alt="Ship atlas from six directions">{model_view_sections}{color_links}'''
(out/'index.html').write_text(page,encoding='utf-8')
print(f'Wrote comparison board: {board_path}')
