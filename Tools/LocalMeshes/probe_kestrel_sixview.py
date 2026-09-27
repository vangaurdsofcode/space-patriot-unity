import bpy, numpy as np
from mathutils import Vector
VIEWS=('top','bottom','port','starboard','nose','aft')
def canonical_direction(poly,obj):
    n=(obj.matrix_world.to_3x3()@poly.normal).normalized(); a=max(range(3),key=lambda i:abs(n[i])); s=1 if n[a]>=0 else -1
    return {(1,1):'top',(1,-1):'bottom',(2,-1):'port',(2,1):'starboard',(0,1):'nose',(0,-1):'aft'}[(a,s)]
o=next(x for x in bpy.context.scene.objects if x.type=='MESH' and x.name=='Hull_LOD0')
d=o.data; d.calc_loop_triangles(); uv=d.uv_layers.get('AtlasUV_SixView_v1')
print('object',o.name,'location',tuple(o.location),'rotation',tuple(o.rotation_euler),'bounds',tuple(o.dimensions))
for v in VIEWS:
    tris=[]; points=[]
    for t in d.loop_triangles:
        p=d.polygons[t.polygon_index]
        if canonical_direction(p,o)!=v: continue
        tris.append(t)
        points.extend(tuple(d.vertices[i].co) for i in t.vertices)
    p=np.array(points)
    uvs=np.array([tuple(uv.data[l].uv) for t in tris for l in t.loops])
    print(v,'tris',len(tris),'xyzmin',p.min(0).round(3).tolist(),'xyzmax',p.max(0).round(3).tolist(),'uvmin',uvs.min(0).round(3).tolist(),'uvmax',uvs.max(0).round(3).tolist())
