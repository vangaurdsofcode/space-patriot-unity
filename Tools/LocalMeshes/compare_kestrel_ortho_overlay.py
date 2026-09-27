"""Make fixed-length reference/model overlays for six-view bake review."""
import argparse
import json
from collections import deque
from pathlib import Path
import numpy as np
from PIL import Image

ap=argparse.ArgumentParser()
ap.add_argument('--reference',required=True,type=Path)
ap.add_argument('--render',required=True,type=Path)
ap.add_argument('--output',required=True,type=Path)
args=ap.parse_args()
ref=Image.open(args.reference).convert('RGB')
render=Image.open(args.render).convert('RGBA')

def content_mask(image, render_alpha=False):
    arr=np.asarray(image)
    if render_alpha:
        mask=arr[:,:,3]>12
    else:
        rgb=arr[:,:,:3].astype(np.int16)
        corners=np.concatenate((rgb[:8,:8].reshape(-1,3),rgb[:8,-8:].reshape(-1,3),rgb[-8:,:8].reshape(-1,3),rgb[-8:,-8:].reshape(-1,3)))
        bg=np.median(corners,axis=0)
        candidate=(np.max(np.abs(rgb-bg),axis=2)<19.125)&((rgb.max(2)-rgb.min(2))<15.3)
        # Remove only border-connected studio background. A plain difference
        # threshold incorrectly punches holes through grey/white hull panels.
        h,w=candidate.shape
        exterior=np.zeros((h,w),dtype=bool)
        pending=deque()
        for y,x in ([(0,x) for x in range(w)]+[(h-1,x) for x in range(w)]+
                    [(y,0) for y in range(h)]+[(y,w-1) for y in range(h)]):
            if candidate[y,x] and not exterior[y,x]:
                exterior[y,x]=True; pending.append((y,x))
        while pending:
            y,x=pending.pop()
            for yy,xx in ((y-1,x),(y+1,x),(y,x-1),(y,x+1)):
                if 0<=yy<h and 0<=xx<w and candidate[yy,xx] and not exterior[yy,xx]:
                    exterior[yy,xx]=True;pending.append((yy,xx))
        mask=~exterior
    return mask

def content_box(image, render_alpha=False):
    ys,xs=np.where(content_mask(image, render_alpha))
    if not len(xs): raise RuntimeError(f'Cannot find foreground in {image}')
    return (int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1))

rb=content_box(ref)
mb=content_box(render,True)
rw,rh=rb[2]-rb[0],rb[3]-rb[1]
mw,mh=mb[2]-mb[0],mb[3]-mb[1]
# Lock both silhouettes to the reference nose-to-tail length. Any height
# difference stays visible and signals a model scale/shape mismatch.
scale=rw/mw
scaled=render.crop(mb).resize((rw,max(1,round(mh*scale))),Image.Resampling.LANCZOS)
canvas=Image.new('RGBA',(ref.width,ref.height),(0,0,0,0))
canvas.alpha_composite(scaled,(rb[0],round((rb[1]+rb[3]-scaled.height)/2)))
ref_rgba=ref.convert('RGBA')
background=Image.new('RGBA',ref.size,ref.getpixel((1,1))+(255,))
model_panel=Image.alpha_composite(background,canvas)
overlay=Image.blend(ref_rgba,model_panel,.5)
side=Image.new('RGB',(ref.width*3,ref.height),(28,31,34))
side.paste(ref,(0,0));side.paste(model_panel.convert('RGB'),(ref.width,0))
side.paste(overlay.convert('RGB'),(ref.width*2,0))
args.output.parent.mkdir(parents=True,exist_ok=True)
side.save(args.output)
reference_mask=content_mask(ref)
model_mask=np.asarray(canvas)[:,:,3]>12
intersection=reference_mask & model_mask
union=reference_mask | model_mask
color_error=np.abs(np.asarray(ref).astype(float)-np.asarray(model_panel)[:,:,:3].astype(float))
report={'columns':['reference','actual saved render','50 percent overlay'],
        'reference_bounds':rb,'render_bounds':mb,'model_height_at_fixed_length':scaled.height,
        'reference_height':rh,'silhouette_iou':float(intersection.sum()/max(1,union.sum())),
        'rgb_mae_on_shared_foreground':float(color_error[intersection].mean()) if intersection.any() else None,
        'meaning':'Diagnostic only: common width and one scale preserve geometry mismatch. Not an art-approval score.',
        'output':str(args.output)}
args.output.with_suffix('.json').write_text(json.dumps(report,indent=2))
print(report)
