"""Probe hull-local bounds and surface hits for fitting authored Blender detail."""
import argparse, json, sys
from pathlib import Path
import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ap=argparse.ArgumentParser(description=__doc__)
ap.add_argument('--blend',required=True,type=Path)
ap.add_argument('--output',required=True,type=Path)
args=ap.parse_args(sys.argv[sys.argv.index('--')+1:])
bpy.ops.wm.open_mainfile(filepath=str(args.blend))
obj=next((o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith('Hull_LOD0')),None)
if obj is None: raise RuntimeError('Hull_LOD0 not found')
coords=[v.co.copy() for v in obj.data.vertices]
lo=Vector(tuple(min(v[i] for v in coords) for i in range(3)))
hi=Vector(tuple(max(v[i] for v in coords) for i in range(3)))
bm=bmesh.new(); bm.from_mesh(obj.data)
bvh=BVHTree.FromBMesh(bm)
hits=[]
for x in [round(float(v),3) for v in range(-9,10,1)]:
    row={'x':x,'z_hits':[]}
    for z in [-3,-2,-1,0,1,2,3]:
        hit=bvh.ray_cast(Vector((x,hi.y+5,z)),Vector((0,-1,0)),hi.y-lo.y+10)
        if hit[0] is not None:
            row['z_hits'].append({'z':z,'y':round(hit[0].y,4),'normal':tuple(round(c,3) for c in hit[1])})
    hits.append(row)
result={'object':obj.name,'object_location':tuple(obj.location),'local_bounds':{'min':tuple(lo),'max':tuple(hi),'size':tuple(hi-lo)},'top_raycast_grid':hits}
bm.free()
args.output.parent.mkdir(parents=True,exist_ok=True)
args.output.write_text(json.dumps(result,indent=2),encoding='utf8')
print(json.dumps(result,indent=2))
