"""Isolated xatlas worker for Blender's post-bake UV transfer."""
import argparse
import json
from pathlib import Path
import numpy as np
import xatlas

ap=argparse.ArgumentParser(description=__doc__)
ap.add_argument('--input',required=True,type=Path)
ap.add_argument('--output',required=True,type=Path)
args=ap.parse_args()
data=np.load(args.input)
atlas=xatlas.Atlas()
split=data.get('split_faces',np.array([],np.int32))
keep=np.ones(len(data['faces']),dtype=bool)
keep[split]=False
atlas.add_mesh(data['vertices'],data['faces'][keep])
# Separate mesh entries are intentional: xatlas may weld coincident vertices
# within one mesh, undoing an ordinary vertex split and recreating the fold.
for face in split:
    atlas.add_mesh(data['vertices'][data['faces'][face]],np.array([[0,1,2]],np.uint32))
charts=xatlas.ChartOptions()
charts.fix_winding=False
charts.max_cost=1.0
charts.max_iterations=2
packing=xatlas.PackOptions()
packing.resolution=2048
packing.padding=10
packing.bilinear=True
atlas.generate(chart_options=charts,pack_options=packing)
mapping,indices,uv=atlas[0]
if not np.array_equal(mapping[indices],data['faces'][keep]):
    raise RuntimeError('xatlas changed source triangle order; refuse ambiguous transfer')
triangle_uv=np.empty((len(data['faces']),3,2),np.float32)
triangle_uv[keep]=uv[indices]
for index,face in enumerate(split,1):
    mapping,indices,uv=atlas[index]
    if not np.array_equal(mapping[indices],[[0,1,2]]):
        raise RuntimeError('xatlas changed isolated triangle order')
    triangle_uv[face]=uv[indices][0]
np.savez(args.output,triangle_uv=triangle_uv)
args.output.with_suffix('.json').write_text(json.dumps({
    'library':'xatlas==0.0.11','chart_count':sum(atlas.get_mesh_chart_count(i) for i in range(len(atlas))),
    'width':atlas.width,'height':atlas.height,'utilization':atlas.utilization,
    'padding':packing.padding,'isolated_folded_faces':data.get('split_faces',np.array([])).tolist()},indent=2))
