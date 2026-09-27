"""Generate every Kestrel part, atlas-bake it in Blender, and export one FBX."""
import argparse
import datetime as dt
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1]
ap = argparse.ArgumentParser(description=__doc__)
ap.add_argument("--build-id", default=dt.datetime.now().strftime("build-%Y%m%d-%H%M%S"))
ap.add_argument("--tool-root", type=Path, default=PROJECT.parents[1] / "work")
ap.add_argument("--steps", type=int, default=None)
ap.add_argument("--depth", type=int, choices=(8, 9, 10), default=None)
ap.add_argument("--resume", action="store_true", help="Reuse matching raw meshes already in this build")
ap.add_argument("--reuse-meshes-from", default=None,
                help="Rebuild the bake from current kit settings using raw meshes in an existing build")
ap.add_argument("--do-not-activate", action="store_true",
                help="Keep the current active build while producing a comparison candidate")
args = ap.parse_args()
if not re.fullmatch(r"[A-Za-z0-9_-]{1,64}", args.build_id):
    raise SystemExit("Build id must use letters, digits, hyphens or underscores.")

tool_root = args.tool_root.resolve()
python = tool_root / "tools/hy3d-env/Scripts/python.exe"
blender = tool_root / "tools/blender-4.5.10-windows-x64/blender.exe"
source = tool_root / "tools/TripoSG-source"
weights = tool_root / "models/TripoSG"
for item in (python, blender, source, weights):
    if not item.exists():
        raise SystemExit(f"Missing local mesh tool: {item}. See Tools/LocalMeshes/README.md.")

config_path = HERE / "kestrel-kit.json"
config = json.loads(config_path.read_text(encoding="utf8"))
steps = args.steps or config["default_steps"]
depth = args.depth or config["default_depth"]
build_root = PROJECT / "ArtDirection/Generated/KestrelK017" / args.build_id
if build_root.exists() and not args.resume:
    raise SystemExit(f"Build already exists: {build_root}. Choose another --build-id or use --resume.")
if args.resume and (build_root / '01-top.blend').exists():
    raise SystemExit('This build already has saved six-view passes. Choose a new --build-id and --reuse-meshes-from to preserve its evidence.')
build_root.mkdir(parents=True, exist_ok=True)
records = []

for part in config["parts"]:
    record = dict(part)
    reference = (PROJECT / part["reference"]).resolve()
    if not reference.is_file():
        raise SystemExit(f"Missing {part['id']} reference: {reference}")
    record["reference_path"] = str(reference)
    record["reference_sha256"] = hashlib.sha256(reference.read_bytes()).hexdigest()
    if part.get("orthographic_reference_directory"):
        view_directory = (PROJECT / part["orthographic_reference_directory"]).resolve()
        record["orthographic_reference_directory_path"] = str(view_directory)
        record["orthographic_reference_sha256"] = {}
        for view in ("top", "bottom", "port", "starboard", "nose", "aft"):
            view_image = view_directory / f"{view}.png"
            if not view_image.is_file():
                raise SystemExit(f"Missing {part['id']} {view} view: {view_image}")
            record["orthographic_reference_sha256"][view] = hashlib.sha256(view_image.read_bytes()).hexdigest()
    if part.get("dorsal_reference"):
        dorsal_reference = (PROJECT / part["dorsal_reference"]).resolve()
        if not dorsal_reference.is_file():
            raise SystemExit(f"Missing {part['id']} dorsal bake reference: {dorsal_reference}")
        record["dorsal_reference_path"] = str(dorsal_reference)
        record["dorsal_reference_sha256"] = hashlib.sha256(dorsal_reference.read_bytes()).hexdigest()
    if part.get("underside_reference"):
        underside_reference = (PROJECT / part["underside_reference"]).resolve()
        if not underside_reference.is_file():
            raise SystemExit(f"Missing {part['id']} underside bake reference: {underside_reference}")
        record["underside_reference_path"] = str(underside_reference)
        record["underside_reference_sha256"] = hashlib.sha256(underside_reference.read_bytes()).hexdigest()
    if part["generation"] == "local":
        component_root = build_root / "Parts" / part["id"]
        mesh = component_root / "raw.glb"
        report = mesh.with_suffix(".generation.json")
        if args.reuse_meshes_from:
            source_root = PROJECT / "ArtDirection/Generated/KestrelK017" / args.reuse_meshes_from
            mesh = source_root / "Parts" / part["id"] / "raw.glb"
            report = mesh.with_suffix(".generation.json")
            if not (mesh.is_file() and report.is_file()):
                raise SystemExit(f"Missing reusable mesh or provenance report: {mesh}")
            old = json.loads(report.read_text(encoding="utf8"))
            if old.get("input_sha256") != record["reference_sha256"]:
                raise SystemExit(f"Reusable mesh was generated from a different reference: {mesh}")
        elif mesh.exists() or report.exists():
            if not args.resume or not (mesh.exists() and report.exists()):
                raise SystemExit(f"Refusing to replace partial/generated mesh: {mesh}")
            old = json.loads(report.read_text(encoding="utf8"))
            if (old.get("input_sha256") != record["reference_sha256"] or old.get("seed") != part["seed"]
                    or old.get("steps") != steps or old.get("octree_depth") != depth):
                raise SystemExit(f"Existing mesh provenance does not match this reference, seed, steps or depth: {mesh}")
        else:
            command = [str(python), str(HERE / "generate_mesh.py"),
                       "--source", str(source), "--weights", str(weights),
                       "--image", str(reference), "--output", str(mesh),
                       "--seed", str(part["seed"]), "--steps", str(steps), "--depth", str(depth)]
            subprocess.run(command, check=True, env={**os.environ, "PYTHONUTF8": "1"})
        record["mesh_path"] = str(mesh)
        record["generation_report"] = str(report)
    else:
        mesh = (PROJECT / part["mesh"]).resolve()
        if not mesh.is_file():
            raise SystemExit(f"Missing reusable component {part['id']}: {mesh}")
        record["mesh_path"] = str(mesh)
        record["generation_report"] = str(mesh.with_suffix(".generation.json"))
    records.append(record)

config["steps"] = steps
config["depth"] = depth
config["build_id"] = args.build_id
config["parts"] = records
resolved = build_root / "build-manifest.json"
resolved.write_text(json.dumps(config, indent=2), encoding="utf8")
command = [str(blender), "--background", "--python-exit-code", "1", "--python",
           str(HERE / "assemble_ship.py"), "--", "--manifest", str(resolved),
           "--project-root", str(PROJECT), "--output", str(build_root)]
subprocess.run(command, check=True)

# The single three-quarter sheet leaves the crown and underside unpainted.
# Project a separate, aligned dorsal sheet onto upward-facing hull UV texels,
# preserving the side projection, then refresh the review renders and FBX.
hull = next((part for part in records if part["id"] == "Hull"), None)
if hull and hull.get("orthographic_reference_directory_path"):
    subprocess.run([str(python), str(HERE / "bake_sixview_pipeline.py"),
                    "--blender", str(blender), "--input", str(build_root / "Kestrel_K017.blend"),
                    "--references", hull["orthographic_reference_directory_path"],
                    "--recipe", str(resolved),
                    "--output", str(build_root)], check=True)
elif hull and hull.get("dorsal_reference_path"):
    original_blend = build_root / "Kestrel_K017.blend"
    overlay_blend = build_root / "Kestrel_K017.dorsal-pass.blend"
    overlay_fbx = overlay_blend.with_suffix(".fbx")
    overlay_renders = build_root / "Renders" / "DorsalOverlay"
    overlay_command = [str(blender), "--background", "--python-exit-code", "1", "--python",
                       str(HERE / "bake_hull_dorsal_overlay.py"), "--",
                       "--blend", str(original_blend), "--reference", hull["dorsal_reference_path"],
                        "--output", str(overlay_blend), "--render-dir", str(overlay_renders)]
    if hull.get("underside_reference_path"):
        overlay_command.extend(["--underside-reference", hull["underside_reference_path"]])
    # A three-quarter wing image is not a valid top-view atlas. Only run the
    # direct wing overlay when a separately authored, aligned top reference is
    # present; otherwise retain the perspective camera projection baked above.
    wing = next((part for part in records if part["id"] == "Wing"), None)
    if wing and wing.get("top_reference_path"):
        overlay_command.extend(["--wing-reference", wing["top_reference_path"]])
    subprocess.run(overlay_command, check=True)
    overlay_blend.replace(original_blend)
    overlay_fbx.replace(build_root / "Kestrel_K017.fbx")
    final_renders = build_root / "Renders" / "Final"
    final_renders.mkdir(parents=True, exist_ok=True)
    for view in ("top", "quarter", "underside"):
        rendered_view = overlay_renders / f"{view}.png"
        if rendered_view.is_file():
            shutil.copy2(rendered_view, final_renders / f"{view}.png")

if not args.do_not_activate:
    stage = PROJECT / "ArtDirection/Generated/KestrelK017/active-build.txt"
    tmp = stage.with_suffix(".tmp")
    tmp.write_text(args.build_id + "\n", encoding="utf8")
    os.replace(tmp, stage)
print(f"Kestrel build finished: {build_root}")
