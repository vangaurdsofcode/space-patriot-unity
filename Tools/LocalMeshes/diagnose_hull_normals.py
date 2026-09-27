import argparse, sys
from pathlib import Path
import bpy, numpy as np

ap=argparse.ArgumentParser();ap.add_argument('--blend',required=True);ap.add_argument('--prefix',default='Hull');args=ap.parse_args(sys.argv[sys.argv.index('--')+1:])
bpy.ops.wm.open_mainfile(filepath=args.blend)
obj=next(o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith(args.prefix+'_') and o.name.endswith('_LOD0'))
m=obj.data; areas=np.array([p.area for p in m.polygons]); normals=np.array([tuple(p.normal) for p in m.polygons]); centers=np.array([tuple(p.center) for p in m.polygons])
print('areas:',areas.sum(),'faces:',len(areas),'bounds:',tuple(obj.dimensions))
for axis,name in enumerate('XYZ'):
 d=normals[:,axis]
 print(name,'positive face-area shares:',{str(t):round(float(areas[d>t].sum()/areas.sum()),3) for t in (-.2,0,.1,.25,.5,.75)})
top=normals[:,1]>.4
print('top-facing area hull-local center percentiles',np.percentile(centers[top],(5,50,95),axis=0).tolist() if top.any() else None)
print('normal bins Y',np.histogram(normals[:,1],bins=[-1,-.5,0,.25,.5,.75,1],weights=areas)[0].tolist())
