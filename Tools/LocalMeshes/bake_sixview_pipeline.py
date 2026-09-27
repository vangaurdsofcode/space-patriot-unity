"""Run the ordered bake, reopen verification, FBX export and saved-file renders."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
VIEWS = ('top', 'bottom', 'port', 'starboard', 'nose', 'aft')
ap = argparse.ArgumentParser(description=__doc__)
ap.add_argument('--blender', required=True, type=Path)
ap.add_argument('--input', required=True, type=Path)
ap.add_argument('--references', required=True, type=Path)
ap.add_argument('--output', required=True, type=Path)
ap.add_argument('--recipe',type=Path,default=HERE/'kestrel-kit.json',
                help='Assembly recipe controlling hull symmetry and paired module mirrors')
ap.add_argument('--projection-only', action='store_true',help='Diagnostic: stop before the final UV unwrap/texture transfer')
args = ap.parse_args()
recipe=json.loads(args.recipe.read_text())
hull=next(part for part in recipe['parts'] if part['id']=='Hull')
symmetry=hull.get('symmetry')
if symmetry and symmetry != {'axis':'Z','offset':0,'source_side':'negative'}:
    raise SystemExit('Only port-master hull symmetry across world Z=0 is supported')
mirror_pairs=any(instance.get('mirror_of') for part in recipe['parts'] for instance in part['instances'])
if not args.projection_only:
    import xatlas  # Requires xatlas==0.0.11 in the orchestration Python.
blender, source, references, output = (p.resolve() for p in (args.blender,args.input,args.references,args.output))
for path in (blender,source,*(references/f'{view}.png' for view in VIEWS)):
    if not path.is_file():
        raise SystemExit(f'Missing required pipeline input: {path}')
output.mkdir(parents=True, exist_ok=True)
if any((output/f'{i+1:02}-{view}.blend').exists() for i,view in enumerate(VIEWS)):
    raise SystemExit('Use a fresh output directory: existing saved passes are preserved for comparison.')
source_directory = output/'Source'
source_directory.mkdir(exist_ok=True)
baseline = source_directory/'Kestrel_K017.blend'
shutil.copy2(source, baseline)

# The source may use blend-relative external PBR files. Relocate those files
# with the baseline so copying a blend cannot silently lose its image data.
(source_directory/'Textures').mkdir(exist_ok=True)
for filename in ('Kestrel_BaseColor.png','Kestrel_Normal.png',
                 'Kestrel_MetallicSmoothness.png','Kestrel_Roughness_source.png'):
    texture_source = source.parent/'Textures'/filename
    destination = source_directory/'Textures'/filename
    if texture_source.is_file() and texture_source != destination:
        shutil.copy2(texture_source, destination)

previous = baseline
for index,view in enumerate(VIEWS,1):
    result = output/f'{index:02}-{view}.blend'
    command = [str(blender),'--background',str(previous),'--python-exit-code','1',
               '--python',str(HERE/'bake_kestrel_ortho_views.py'),'--',
               '--view',view,'--reference',str(references/f'{view}.png'),
               '--output',str(result),'--report',str(output/f'{index:02}-{view}.json')]
    if index == 1:
        command.append('--reset-uv')
    subprocess.run(command,check=True)
    previous = result
subprocess.run([str(blender),'--background','--python-exit-code','1','--python',
                str(HERE/'verify_kestrel_ortho_saved.py'),'--','--directory',str(output),
                '--baseline',str(baseline)],check=True)
subprocess.run([str(blender),'--background',str(previous),'--python-exit-code','1','--python',
                str(HERE/'finalize_kestrel_sixview.py'),'--','--output',str(output)]
                + ([] if args.projection_only else ['--no-renders']),check=True)
if not args.projection_only:
    packed=output/'PostBakeUV'
    subprocess.run([sys.executable,str(HERE/'post_bake_uv_pipeline.py'),'--blender',str(blender),
                    '--input',str(previous),'--output',str(packed)]
                    + (['--symmetrize-hull'] if symmetry else []),check=True)
    uv_source=packed/'HullSymmetry'/'Kestrel_K017.blend' if symmetry else previous
    final=packed
    if mirror_pairs:
        final=output/'PairedModules'
        subprocess.run([str(blender),'--background',str(packed/'Kestrel_K017.blend'),
                        '--python-exit-code','1','--python',str(HERE/'mirror_ship_pairs.py'),
                        '--','--output',str(final),'--recipe',str(args.recipe.resolve()),'--render'],check=True)
        subprocess.run([str(blender),'--background','--python-exit-code','1',
                        '--python',str(HERE/'verify_repacked_hull.py'),'--','--source',str(uv_source),
                        '--candidate',str(final),'--render'],check=True)
    # Block import throughout publication. A stopped copy must not combine a
    # new packed-UV FBX with the old projection textures and a stale pass.
    validation_path=output/'saved-uv-repack-validation.json'
    validation_path.write_text(json.dumps({'passed':False,'status':'Publishing verified final assets; incomplete'}))
    shutil.copy2(final/'uv-repack-report.json',output/'uv-repack-report.json')
    for filename in ('Kestrel_K017.blend','Kestrel_K017.fbx','assembly-report.json'):
        shutil.copy2(final/filename,output/filename)
    if symmetry:
        shutil.copy2(packed/'HullSymmetry'/'hull-symmetry-validation.json',output/'hull-symmetry-validation.json')
    if mirror_pairs:
        shutil.copy2(final/'mirror-pair-validation.json',output/'mirror-pair-validation.json')
        shutil.copytree(final/'Renders'/'MirroredAssembly',output/'Renders'/'MirroredAssembly',dirs_exist_ok=True)
    shutil.copytree(final/'Textures',output/'Textures',dirs_exist_ok=True)
    shutil.copytree(final/'Renders'/'After',output/'Renders'/'Saved',dirs_exist_ok=True)
    ready=validation_path.with_suffix('.ready')
    shutil.copy2(final/'saved-uv-repack-validation.json',ready)
    ready.replace(validation_path)
print(f'Verified saved six-view bake and export: {output}',flush=True)
