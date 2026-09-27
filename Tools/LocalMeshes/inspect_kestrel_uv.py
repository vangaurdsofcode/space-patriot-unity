import bpy
from pathlib import Path
for o in bpy.context.scene.objects:
    if o.type == 'MESH' and (o.name.endswith('_LOD0') or o.name.endswith('_LOD1') or o.name.endswith('_LOD2')):
        print('OBJECT', o.name, 'uvs', [(u.name, getattr(u, 'active_render', None)) for u in o.data.uv_layers])
        if o.name.startswith('Hull'):
            for m in o.data.materials:
                if not m or not m.use_nodes: continue
                print('MAT', m.name)
                for n in m.node_tree.nodes:
                    if n.type in {'TEX_IMAGE','UVMAP','TEX_COORD'}:
                        print('NODE', n.name, n.type, getattr(n,'uv_map',''), getattr(getattr(n,'image',None),'name',None))
