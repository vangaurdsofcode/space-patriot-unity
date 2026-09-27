"""Compare proposed dorsal/ventral hull art with actual model-relative renders."""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
GEN = ROOT / 'ArtDirection/Generated/KestrelK017'
MODULES = ROOT / 'ArtDirection/Modules'
ap = argparse.ArgumentParser(description=__doc__)
ap.add_argument('--build-id', default='kestrel-bake-bilateral-pass2-20260926')
ap.add_argument('--output', type=Path, default=ROOT/'Validation/hull-view-overlay-v1')
args = ap.parse_args()
comparison = GEN / args.build_id / 'Renders/Comparison'
args.output.mkdir(parents=True, exist_ok=True)

def load_mask(path, use_alpha=False):
    im = Image.open(path).convert('RGBA')
    arr = np.asarray(im)
    # Concept sheets have an opaque white field; their alpha channel covers
    # the whole canvas. Model renders use alpha only if it carries transparency.
    if use_alpha and np.min(arr[:,:,3]) < 250:
        mask = arr[:, :, 3] > 24
    else:
        rgb = arr[:, :, :3]
        mask = np.min(rgb, axis=2) < 246
    ys, xs = np.where(mask)
    if len(xs) < 20:
        raise ValueError(f'No foreground detected in {path}')
    box = (int(xs.min()), int(ys.min()), int(xs.max()+1), int(ys.max()+1))
    return im, mask, box

def normalize(im, mask, box, size=512):
    x0, y0, x1, y1 = box
    inner = size - 64
    scale = min(inner / max(1, x1-x0), inner / max(1, y1-y0))
    wh = (max(1, round((x1-x0)*scale)), max(1, round((y1-y0)*scale)))
    rgb = im.convert('RGB').crop(box).resize(wh, Image.Resampling.LANCZOS)
    crop = Image.fromarray((mask*255).astype('uint8')).crop(box).resize(wh, Image.Resampling.NEAREST)
    canvas = Image.new('RGB', (size, size), (18,24,30))
    mask_canvas = Image.new('L', (size,size), 0)
    p = ((size-wh[0])//2, (size-wh[1])//2)
    canvas.paste(rgb, p); mask_canvas.paste(crop, p)
    return np.asarray(canvas), np.asarray(mask_canvas) > 127

def compare(reference, model):
    ri, rm, rb = load_mask(reference, use_alpha=False)
    mi, mm, mb = load_mask(model, use_alpha=True)
    a, ma = normalize(ri, rm, rb)
    b, mb = normalize(mi, mm, mb)
    inter = ma & mb; union = ma | mb
    overlay = np.zeros((512,512,3), dtype=np.uint8)
    overlay[ma & ~mb] = (255,55,185)
    overlay[mb & ~ma] = (40,230,255)
    overlay[inter] = (78,105,105)
    yy, xx = np.mgrid[:512,:512]
    ca=(float(xx[ma].mean()),float(yy[ma].mean()))
    cb=(float(xx[mb].mean()),float(yy[mb].mean()))
    return (float(inter.sum()/max(1,union.sum())), float(mb.sum()/max(1,ma.sum())),
            Image.fromarray(a), Image.fromarray(b), Image.fromarray(overlay),
            float(np.hypot(ca[0]-cb[0],ca[1]-cb[1])/512))

refs = {
    'Ventral candidate': MODULES/'kestrel-hull-underside-reference-v1.png',
    'Dorsal candidate': MODULES/'kestrel-hull-dorsal-reference-v1.png',
}
views = {'Blender +Y / Top': comparison/'Hull_View_Top.png',
         'Blender -Y / Bottom': comparison/'Hull_View_Bottom.png'}
panel=512; label_h=58; gap=14; cols=3; rows=len(refs)*len(views)
board=Image.new('RGB',(cols*panel+(cols+1)*gap, rows*(panel+label_h)+(rows+1)*gap),(18,24,30))
draw=ImageDraw.Draw(board)
font=ImageFont.truetype('arial.ttf',17)
metrics={}
i=0
for ref_name,ref_path in refs.items():
    for view_name,view_path in views.items():
        iou,area,a,b,overlay,centroid=compare(ref_path,view_path)
        key=f'{ref_name} vs {view_name}'
        metrics[key]={'silhouette_iou':iou,'model_to_reference_area':area,'centroid_offset_fraction':centroid}
        y=gap+i*(panel+label_h+gap)
        for col,(title,img) in enumerate(((ref_name,a),('ACTUAL MODEL '+view_name,b),('OVERLAY',overlay))):
            x=gap+col*(panel+gap)
            board.paste(img,(x,y+label_h))
            draw.text((x+8,y+5),title,font=font,fill=(230,235,237))
            if col==2:
                draw.text((x+8,y+31),f'IoU {iou:.3f} · Area {area:.3f} · Δcenter {centroid:.3f}',font=ImageFont.truetype('arial.ttf',13),fill=(150,169,179))
        i+=1
board.save(args.output/'comparison.png')
(args.output/'metrics.json').write_text(json.dumps(metrics,indent=2),encoding='utf8')
print(json.dumps(metrics,indent=2))
print(f'Wrote {args.output.resolve()}')
