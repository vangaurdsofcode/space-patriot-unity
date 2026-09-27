"""Re-unwrap a baked hull and transfer its existing paint into the new UV0.

Blender --background INPUT.blend --python-exit-code 1 --python this.py --
  --output NEW_DIRECTORY

This is a post-bake transfer, not another artwork projection. It preserves the
other three atlas quadrants. The currently supported hull normal map must be
neutral: a detailed tangent map needs a tangent-basis normal rebake instead.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import subprocess

import bpy
import numpy as np

SIZE = 4096
HALF = SIZE // 2
UV_NAME = 'AtlasUV_Packed_v3'
CHANNELS = (
    ('Kestrel_BaseColor_4096', 'Kestrel_BaseColor.png'),
    ('Kestrel_Normal_4096', 'Kestrel_Normal.png'),
    ('Kestrel_Metal_4096', 'Kestrel_MetallicSmoothness.png'),
    ('Kestrel_Roughness_4096', 'Kestrel_Roughness_source.png'),
)

class UvOverlapError(RuntimeError):
    def __init__(self,count,faces,examples):
        self.faces=faces
        super().__init__(f'New UV layout has {count} overlapping interior texels: {examples}')


def pixels(image):
    if tuple(image.size) != (SIZE, SIZE):
        raise RuntimeError(f'Invalid source image dimensions: {image.name}')
    a = np.empty(SIZE * SIZE * 4, np.float32)
    image.pixels.foreach_get(a)
    return a.reshape(SIZE, SIZE, 4)


def sample(a, uv):
    xy = np.clip(uv * SIZE - .5, 0, SIZE - 1.0001)
    lo = xy.astype(np.int32)
    dx, dy = (xy-lo).T
    x,y = lo.T
    return ((a[y,x]*(1-dx[:,None]) + a[y,x+1]*dx[:,None])*(1-dy[:,None])
            +(a[y+1,x]*(1-dx[:,None]) + a[y+1,x+1]*dx[:,None])*dy[:,None])


def uv_array(mesh, name):
    a = np.empty((len(mesh.loops), 2), np.float32)
    mesh.uv_layers[name].data.foreach_get('uv', a.reshape(-1))
    return a


def digest(a):
    return hashlib.sha256(np.ascontiguousarray(a).tobytes()).hexdigest()


def transfer_map(mesh, source_uv, target_uv):
    """Rasterize UV triangles; retain face identity to detect true overlap."""
    mesh.calc_loop_triangles()
    # Hull's assigned atlas area is the upper-left quarter (Blender UV origin).
    owner = np.full((HALF, HALF), -1, np.int32)
    lookup = np.zeros((HALF, HALF, 2), np.float32)
    overlaps = 0
    shared_edge_ties = 0
    conflict_examples=[]
    conflict_faces=set()
    collapsed = 0
    for tri in mesh.loop_triangles:
        ids = np.array(tri.loops)
        q = target_uv[ids] * SIZE - np.array((.5, HALF+.5))
        lo = np.maximum(0, np.ceil(q.min(0)).astype(int))
        hi = np.minimum(HALF-1, np.floor(q.max(0)).astype(int))
        if np.any(hi < lo):
            collapsed += 1
            continue
        a,b,c = q
        ab,ac = b-a,c-a
        determinant = ab[0]*ac[1]-ab[1]*ac[0]
        if abs(determinant) < 1e-10:
            collapsed += 1
            continue
        yy,xx = np.mgrid[lo[1]:hi[1]+1,lo[0]:hi[0]+1]
        d = np.stack((xx-a[0],yy-a[1]),axis=-1)
        w1 = (d[...,0]*ac[1]-d[...,1]*ac[0])/determinant
        w2 = (ab[0]*d[...,1]-ab[1]*d[...,0])/determinant
        inside = (w1 > 1e-7) & (w2 > 1e-7) & (w1+w2 < 1-1e-7)
        rows,cols = yy[inside],xx[inside]
        if not len(rows):
            collapsed += 1
            continue
        conflicts=owner[rows,cols]>=0
        for row,col,previous in zip(rows[conflicts],cols[conflicts],owner[rows[conflicts],cols[conflicts]]):
            other=mesh.loop_triangles[int(previous)]
            common=set(tri.vertices).intersection(other.vertices)
            tie=False
            if len(common)==2:
                edge=[]
                matched=True
                for vertex in common:
                    li=tri.loops[list(tri.vertices).index(vertex)]
                    lj=other.loops[list(other.vertices).index(vertex)]
                    matched &= np.max(np.abs(target_uv[li]-target_uv[lj]))<1e-7
                    edge.append(target_uv[li]*SIZE-np.array((.5,HALF+.5)))
                delta=edge[1]-edge[0]
                point=np.array((col,row))-edge[0]
                distance=abs(delta[0]*point[1]-delta[1]*point[0])/max(1e-12,np.linalg.norm(delta))
                tie=matched and distance<.002
            if tie: shared_edge_ties+=1
            else:
                overlaps+=1
                conflict_faces.update((int(tri.index),int(previous)))
                if len(conflict_examples)<8:
                    conflict_examples.append({'triangle':int(tri.index),'other':int(previous),
                                              'shared_vertices':len(common),'texel':[int(col),int(row)]})
        owner[rows,cols] = tri.index
        weights = np.stack((1-w1[inside]-w2[inside],w1[inside],w2[inside]),axis=1)
        lookup[rows,cols] = weights @ source_uv[ids]
    if overlaps:
        raise UvOverlapError(overlaps,conflict_faces,conflict_examples)
    return lookup,owner,collapsed,shared_edge_ties


def dilate(a, valid, steps=8):
    """Extend color into gutters; never overwrite a rasterized surface texel."""
    for _ in range(steps):
        acc = np.zeros_like(a)
        count = np.zeros(valid.shape,np.float32)
        for dy,dx in ((-1,0),(1,0),(0,-1),(0,1)):
            shifted = np.roll(valid,(dy,dx),(0,1))
            if dy < 0: shifted[-1] = False
            if dy > 0: shifted[0] = False
            if dx < 0: shifted[:,-1] = False
            if dx > 0: shifted[:,0] = False
            acc += np.roll(a,(dy,dx),(0,1))*shifted[...,None]
            count += shifted
        fill = ~valid & (count > 0)
        a[fill] = acc[fill] / count[fill,None]
        valid |= fill
    return a


def surface_error(mesh, before_uv, after_uv, before, after):
    mesh.calc_loop_triangles()
    loops = np.array([t.loops for t in mesh.loop_triangles])
    xyz = np.array([v.co[:] for v in mesh.vertices])
    corners = xyz[np.array([t.vertices for t in mesh.loop_triangles])]
    area = np.linalg.norm(np.cross(corners[:,1]-corners[:,0],corners[:,2]-corners[:,0]),axis=1)*.5
    errors=[]
    alpha_errors=[]
    for weights in ((1/3,1/3,1/3),(.6,.2,.2),(.2,.6,.2),(.2,.2,.6)):
        old = np.einsum('tij,i->tj',before_uv[loops],weights)
        new = np.einsum('tij,i->tj',after_uv[loops],weights)
        difference=np.abs(sample(before,old)-sample(after,new))
        errors.append(difference[:,:3].mean(1))
        alpha_errors.append(difference[:,3])
    error = np.mean(errors,axis=0)
    return {'samples':len(loops)*4,'area_weighted_rgb_error':float(np.average(error,weights=area)),
            'surface_area_fraction_error_over_0_10':float(area[error>.10].sum()/area.sum()),
            'triangle_error_p50':float(np.median(error)), 'triangle_error_p95':float(np.quantile(error,.95)),
            'alpha_area_weighted_error':float(np.average(np.mean(alpha_errors,axis=0),weights=area))}


def lod_channels(level):
    suffix='' if level==0 else f'_LOD{level}'
    return [(name+suffix,filename.replace('.png',suffix+'.png')) for name,filename in CHANNELS]


def unwrap_hull(obj,out,python_executable):
    out.mkdir(parents=True,exist_ok=True)
    mesh=obj.data
    if len(mesh.uv_layers)!=1 or mesh.uv_layers[0].name!='AtlasUV_SixView_v2':
        raise RuntimeError('Input must be the corrected sequential bake, not the original failed atlas')
    before_uv=uv_array(mesh,mesh.uv_layers[0].name)
    bpy.ops.object.select_all(action='DESELECT')
    obj.hide_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active=obj
    mesh.uv_layers.new(name=UV_NAME)
    mesh.uv_layers.active_index=1
    mesh.uv_layers[1].active_render=True
    mesh.calc_loop_triangles()
    input_path=out/'unwrap-input.npz'
    output_path=out/'unwrap-output.npz'
    split_faces=set()
    for attempt in range(4):
        np.savez(input_path,vertices=np.array([v.co[:] for v in mesh.vertices],np.float32),
                 faces=np.array([t.vertices[:] for t in mesh.loop_triangles],np.uint32),
                 split_faces=np.array(sorted(split_faces),np.int32))
        subprocess.run([str(python_executable),str(Path(__file__).with_name('unwrap_xatlas.py')),
                        '--input',str(input_path),'--output',str(output_path)],check=True)
        triangle_uv=np.load(output_path)['triangle_uv']
        packed=np.empty((len(mesh.loops),2),np.float32)
        assigned={}
        for tri,uv in zip(mesh.loop_triangles,triangle_uv):
            for loop,point in zip(tri.loops,uv):
                if loop in assigned and not np.allclose(assigned[loop],point):
                    raise RuntimeError('Triangulate the source mesh before UV transfer')
                packed[loop]=point
                assigned[loop]=point
        packed=packed*(.5-24/SIZE)+np.array((12/SIZE,.5+12/SIZE))
        mesh.uv_layers[UV_NAME].data.foreach_set('uv',packed.astype(np.float32).reshape(-1))
        after_uv=uv_array(mesh,UV_NAME)
        try:
            lookup,owner,uncovered_triangles,shared_edge_ties=transfer_map(mesh,before_uv,after_uv)
            break
        except UvOverlapError as error:
            if attempt==3: raise
            split_faces.update(error.faces)
            print(f'Isolating folded UV faces and repacking: {sorted(split_faces)}',flush=True)
    mask=owner>=0
    report={'mesh':obj.name,'triangles':len(mesh.loop_triangles),'uv0':UV_NAME,
            'unwrap':json.loads(output_path.with_suffix('.json').read_text()),
            'raster_overlap_texels':0,'painted_texels':int(mask.sum()),
            'sub_0_002_pixel_shared_edge_ties':shared_edge_ties,
            'triangles_without_pixel_centers':uncovered_triangles,
            'method':'Post-bake UV transfer, protected non-hull quadrants, neutral normal only',
            'channels':{},'art_approved':False}
    return before_uv,after_uv,lookup,mask,report


def transfer_channels(obj,original_images,out,level,unwrapped):
    mesh=obj.data
    before_uv,after_uv,lookup,mask,report=unwrapped
    images=[]
    for index,(name,filename) in enumerate(lod_channels(level)):
        source_image=original_images[index]
        before=pixels(source_image)
        after=before.copy()
        tile=np.zeros((HALF,HALF,4),np.float32)
        tile[mask]=sample(before,lookup[mask])
        tile=dilate(tile,mask.copy())
        # Unused pixels get a stable finish; avoid unrelated old UV islands.
        outside=tile[:,:,3]==0
        default={'Kestrel_BaseColor.png':(.34,.33,.29,1),
                 'Kestrel_Normal.png':(.5,.5,1,1),
                 'Kestrel_MetallicSmoothness.png':(.35,.35,.35,.38),
                 'Kestrel_Roughness_source.png':(.62,.62,.62,1)}[CHANNELS[index][1]]
        tile[outside]=default
        after[HALF:,:HALF]=tile
        error=surface_error(mesh,before_uv,after_uv,before,after)
        if error['area_weighted_rgb_error']>.04 or error['surface_area_fraction_error_over_0_10']>.06 or error['alpha_area_weighted_error']>.01:
            raise RuntimeError(f'Transfer quality rejected for {filename}: {error}')
        image=source_image.copy()
        image.name=name
        image.pixels.foreach_set(after.reshape(-1))
        image.update()
        image.filepath_raw=str(out/'Textures'/filename)
        image.file_format='PNG'
        image.save()
        data=Path(image.filepath_raw).read_bytes()
        image.pack(data=data,data_len=len(data))
        image.use_fake_user=True
        report['channels'][filename]={'surface_transfer':error,'png_sha256':digest(data),
             'non_hull_unchanged':bool(np.array_equal(before[:HALF],after[:HALF]) and np.array_equal(before[HALF:,HALF:],after[HALF:,HALF:]))}
        print(filename, error,flush=True)
        images.append(image)
        del before,after,tile
    mesh.uv_layers.remove(mesh.uv_layers[0])
    mesh.uv_layers.active_index=0
    mesh.uv_layers[0].active_render=True
    return images,report


def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--output',required=True,type=Path)
    ap.add_argument('--python-executable',required=True,type=Path,
                    help='Local Python containing xatlas==0.0.11 and numpy')
    args=ap.parse_args(sys.argv[sys.argv.index('--')+1:])
    out=args.output.resolve()
    if (out/'Kestrel_K017.blend').exists():
        raise RuntimeError('Preserve earlier candidates; use a new output directory')
    out.mkdir(parents=True,exist_ok=True)
    (out/'Textures').mkdir(exist_ok=True)
    source=Path(bpy.data.filepath)
    if bpy.context.scene.get('kestrel_six_view_written')!='top,bottom,port,starboard,nose,aft':
        raise RuntimeError('Complete all six protected view passes before the final unwrap')
    original_images=[bpy.data.images[name] for name,_ in CHANNELS]
    normal=pixels(original_images[1])
    if np.max(np.abs(normal[HALF:,:HALF,:3]-np.array((.5,.5,1))))>.01:
        raise RuntimeError('Detailed normals require tangent-space rebaking; this transfer supports neutral hull normals')
    del normal
    # Keep the original images immutable while independently transferring each
    # already-simplified LOD. Decimating after UV packing introduced folds.
    for image in original_images: image.name='Source '+image.name
    original_material=bpy.data.objects['Hull_LOD0'].data.materials[0]
    report={'source':str(source),'source_sha256':digest(source.read_bytes()),'uv0':UV_NAME,
            'lods':[],'art_approved':False,'geometry_unchanged':True,
            'method':'Independent post-bake UV transfer per existing LOD; no post-unwrap decimation'}
    materials=[]
    for level in range(3):
        obj=bpy.data.objects[f'Hull_LOD{level}']
        unwrapped=unwrap_hull(obj,out/f'LOD{level}',args.python_executable)
        images,lod_report=transfer_channels(obj,original_images,out,level,unwrapped)
        report['lods'].append(lod_report)
        material=original_material.copy()
        material.name=f'Kestrel / final packed hull LOD{level}'
        for node in material.node_tree.nodes:
            if node.type=='TEX_IMAGE' and node.image in original_images:
                node.image=images[original_images.index(node.image)]
        materials.append(material)
    ships=[o for o in bpy.context.scene.objects if o.type=='MESH' and any(o.name.endswith(f'_LOD{i}') for i in range(3))]
    for ship in ships:
        level=int(ship.name[-1]) if ship.name.startswith('Hull_') else 0
        ship.data.materials.clear()
        ship.data.materials.append(materials[level])
    for image in original_images: image.use_fake_user=False
    bpy.ops.object.select_all(action='DESELECT')
    for ship in ships:
        ship.hide_set(False)
        ship.select_set(True)
    bpy.context.view_layer.objects.active=bpy.data.objects['Hull_LOD0']
    bpy.ops.export_scene.fbx(filepath=str(out/'Kestrel_K017.fbx'),use_selection=True,object_types={'MESH'},
        add_leaf_bones=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,path_mode='COPY',embed_textures=False)
    for ship in ships:
        ship.hide_set(not ship.name.endswith('_LOD0'))
        ship.hide_render=not ship.name.endswith('_LOD0')
    bpy.ops.wm.save_as_mainfile(filepath=str(out/'Kestrel_K017.blend'))
    parts=[]
    for part,prefix in (('Hull','Hull_'),('Wing','Wing_'),('LandingGear','Gear_'),('Drive','Drive_')):
        meshes=[o for o in ships if o.name.startswith(prefix)]
        parts.append({'part':part,'instances':sum(o.name.endswith('_LOD0') for o in meshes),
                      'lod_triangles':[sum(sum(len(p.vertices)-2 for p in o.data.polygons)
                       for o in meshes if o.name.endswith(f'_LOD{level}')) for level in range(3)]})
    (out/'assembly-report.json').write_text(json.dumps({'ship':'Kestrel K-017','parts':parts,
        'hull_uv0':UV_NAME,'status':'Post-bake UV review; projection seams and attachments remain art work'},indent=2))
    report['requires_saved_file_validation']='saved-uv-repack-validation.json'
    (out/'uv-repack-report.json').write_text(json.dumps(report,indent=2))
    # Save exact loop correspondence for diagnostics only. Independent verifier
    # must reopen the original and final .blend rather than trust these arrays.
    print(f'Post-bake UV candidate saved: {out}',flush=True)


if __name__=='__main__': main()
