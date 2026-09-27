"""Create, independently reopen and render a fresh final UV transfer candidate."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys

ap=argparse.ArgumentParser(description=__doc__)
ap.add_argument('--blender',required=True,type=Path)
ap.add_argument('--input',required=True,type=Path)
ap.add_argument('--output',required=True,type=Path)
ap.add_argument('--symmetrize-hull',action='store_true',
                help='Keep world-Z<=0 hull half and mirror it before the final per-LOD unwrap')
args=ap.parse_args()
here=Path(__file__).resolve().parent
import xatlas  # Fail before modifying outputs if the pinned dependency is absent.
source=args.input.resolve()
out=args.output.resolve()
base=[str(args.blender.resolve()),'--background']
if args.symmetrize_hull:
    symmetric=out/'HullSymmetry'
    subprocess.run(base+[str(source),'--python-exit-code','1','--python',str(here/'symmetrize_hull.py'),
        '--','--output',str(symmetric)],check=True)
    validation=json.loads((symmetric/'hull-symmetry-validation.json').read_text())
    if not validation.get('passed') or not validation.get('saved_files_reopened'):
        raise RuntimeError('Saved hull symmetry validation did not pass')
    source=symmetric/'Kestrel_K017.blend'
subprocess.run(base+[str(source),'--python-exit-code','1','--python',str(here/'repack_baked_hull.py'),
    '--','--output',str(out),'--python-executable',sys.executable],check=True)
subprocess.run(base+['--python-exit-code','1','--python',str(here/'verify_repacked_hull.py'),
    '--','--source',str(source),'--candidate',str(out),'--render'],check=True)
symmetry_report=source.parent/'hull-symmetry-validation.json'
if symmetry_report.exists():
    shutil.copy2(symmetry_report,out/'hull-symmetry-validation.json')
