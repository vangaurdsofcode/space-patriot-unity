# Local artwork → mesh → Blender → Unity

This is an asset-authoring toolchain. The neural model is not bundled with the game or loaded by Unity.

## Selected model

**VAST TripoSG**, image-conditioned geometry model. Its [official model card](https://huggingface.co/VAST-AI/TripoSG) lists an MIT license and a CUDA GPU with more than 8 GB VRAM. The development machine has an RTX 5060 Ti with 16 GB VRAM and 96 GB system RAM. Actual generation measurements belong in each asset's `.generation.json` report, not inferred from the requirements.

- Source: [VAST-AI-Research/TripoSG](https://github.com/VAST-AI-Research/TripoSG), revision `fc5c40990181e2a756c4e0b1c2f4d6b5202faf8c`.
- Weights: `VAST-AI/TripoSG`, revision `2c1c516d22d58db486a058d98d31bb6177344e06`.
- The official weight files total approximately 7.95 GB. Keep them in a local tool cache, outside version control.
- Shape generation runs locally. The concept reference itself was made with the image-generation tool; it is not a locally generated image.
- We do not use the upstream demo's separate BRIA background-removal model. Supply an isolated object on white.
- `prepare_triposg.py` adapts mesh extraction to Windows with scikit-image marching cubes; it does not change the learned weights. This avoids compiling the optional `diso` CUDA extension. A mask excludes unevaluated cells in the decoder's sparse field, preventing an artificial second shell at the field boundary.

Hunyuan3D 2.1 was also investigated. Its shape stage fits the GPU's nominal memory capacity, but its license restricts territories for both the model and outputs, and its full PBR texture stage lists 21 GB VRAM. It is not the selected production tool. Microsoft's official TRELLIS.2 setup lists Linux and a 24 GB GPU. See their [Hunyuan requirements and license](https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1) and [TRELLIS.2 requirements](https://github.com/microsoft/TRELLIS.2).

## Component workflow

1. Make one clear reference for one physical component: engine nacelle, cockpit shell, landing strut, pressure door, wall bay, service kiosk. Use a plain white background and show the full silhouette. A multi-panel concept sheet is not a suitable direct model input.
2. Generate an untextured mesh with a recorded seed and pinned model. Judge silhouette, proportions, openings and hidden-side plausibility in several views. Reject bad outputs instead of dressing them with texture.
3. Clean the accepted geometry in Blender. Keep a high-resolution source. Separate anything that must move, rebuild precise mounting faces, author production UVs/materials, create LODs and collision proxies.
4. Assemble ships/buildings from those parts at metre scale. Animate rigid mechanisms around real mounting pivots. Cockpit buttons, screens, door apertures and connected walkable rooms remain authored functional structures.
5. Export FBX with named clips and pivots. Configure materials, animation clips, LODGroup and gameplay bindings using Unity Editor APIs. Test the actual imported asset in the scene.

## Kestrel multipart ship build

`kestrel-kit.json` is the editable ship recipe: it assigns one isolated reference and deterministic model seed per component, sizes the mesh in metres, places mirrored wings and duplicate engine nacelles, records each projection camera's view sign/azimuth/elevation, and sets per-part LOD budgets. The hull, wing and landing gear are generated locally; the two engine instances reuse the already generated engine. All prompts and source references are in `ArtDirection/Modules/prompts.json` and `ArtDirection/Modules/`.

Run the full pipeline from PowerShell on the configured development machine:

```powershell
.\Tools\LocalMeshes\build-kestrel.ps1
```

It generates any missing raw components with TripoSG, retains a JSON provenance report beside each mesh, then runs `assemble_ship.py` in Blender. Blender cleans and scales each module, unwraps a dedicated UV atlas, projects concept color to front- and reverse-facing surfaces, uses a stable material on unseen faces, bakes roughness/metallic maps, makes three LODs per component and exports an editable `.blend`, a combined FBX and review renders. The hull recipe supplies dorsal and underside concept views, and the wing recipe supplies a separate wing-top view. `bake_hull_dorsal_overlay.py` fits each segmented crop to the corresponding hull or wing UV tile and saves post-bake views under both `Renders/DorsalOverlay/` and `Renders/Final/`. `dorsal-overlay-report.json` records per-view atlas coverage. Earlier build folders may contain pre-overlay images in `Renders/`; use `Renders/Final/` for the finished bake. The current TripoSG parts are untextured and non-watertight, so high-to-low normal ray baking is **off by default**; enable `bake_high_detail_normals` in the manifest only for an authored, cleaned high-poly source. Wing sides and undersides still need their own art views. The FBX and atlas are review candidates; generated geometry and projected color need human art review before replacing production ship assets.

The isolated `kestrel-bake-quiet-surface-20260926` candidate removed the cracked-looking fallback and stray normal-ray pixels, but its atlas left reverse hull and wing faces nearly blank. `kestrel-bake-bilateral-pass2-20260926` mirrored the single concept projection across both sides, improving paint coverage but leaving top and underside surfaces unfinished. Its [comparison board](../../ArtDirection/Generated/KestrelK017/kestrel-bake-bilateral-pass2-20260926/Renders/Comparison/comparison-board.png) and [six-view ship board](../../ArtDirection/Generated/KestrelK017/kestrel-bake-bilateral-pass2-20260926/Renders/Comparison/six-view-ship-board.png) remain review-only.

The perspective paint projection now uses one shared image scale for horizontal and vertical coordinates; earlier versions independently stretched both axes to the reference crop. The `kestrel-aspect-preserving-pass-20260926-r1` rebake reused the existing `kestrel-v1` meshes and regenerated the atlas plus six-view diagnostic boards. It remains a non-active candidate: the hull and full-ship silhouette checks are still below art-approval quality, and the single-view wing mesh fails the current comparison. Review [the bake board](../../ArtDirection/Generated/KestrelK017/kestrel-aspect-preserving-pass-20260926-r1/Renders/Comparison/comparison-board.png) and [the assembled six-view board](../../ArtDirection/Generated/KestrelK017/kestrel-aspect-preserving-pass-20260926-r1/Renders/Comparison/six-view-ship-board.png). Do not promote it by changing `active-build.txt`; fix the underlying wing/view fit and verify the hull and assembly against matched concept cameras first.

### Fresh Kestrel hull UVs and ordered six-view bake

The `kestrel-sixview-fit-review-20260926-r2` candidate is rejected: saving an already packed Blender image and calling `pack()` without supplying new bytes retained its old packed PNG. Every reopened pass restored the original atlas. Its old preservation-only check missed this because it never required new paint. Do not reuse that candidate or its success claims.

`bake_kestrel_ortho_views.py` now makes `AtlasUV_SixView_v2` the sole UV0 on all three hull LODs, uses exact 12-pixel gutters, registers the artwork foreground at one uniform pixel/metre scale, and explicitly packs each newly saved PNG. It replaces the invalid hull albedo, normal and material channels on reset, then paints top, bottom, port, starboard, nose and aft one at a time. Earlier view pixels and non-hull atlas regions are protected. Normal/metal/roughness get neutral hull values compatible with the new UVs instead of sampling unrelated old atlas islands.

The [r3 candidate](../../ArtDirection/Generated/KestrelK017/kestrel-sixview-persisted-20260926-r3/) passed independent save/reopen checks and Unity UV0 import for all LODs. `verify_kestrel_ortho_saved.py` reopens every blend, requires actual target-chart changes, compares protected pixels, checks packed bytes against each pass PNG, and validates UV0 and companion PBR channels. It correctly rejects the old broken candidate. `finalize_kestrel_sixview.py` exports only after the six passes and renders the reopened final file, avoiding intermediate in-memory preview claims. Visual issues still visible in the final renders include faceted projection seams, a detached forward landing leg and unfinished wing/drive surfaces. This is not a production art approval.

`kestrel-kit.json` now selects the six-view route for normal builds. `build_ship.py` records all six source image hashes, runs the ordered pipeline and verification, then finalizes the FBX and blend. The cropped source views and coordinate convention are in [`turnaround.json`](../../ArtDirection/Modules/kestrel-hull-ortho-v1/turnaround.json). To run the bake independently, pass absolute paths and a fresh output directory:

```powershell
python Tools/LocalMeshes/bake_sixview_pipeline.py --blender C:/Tools/Blender/blender.exe --input C:/Builds/Kestrel/Kestrel_K017.blend --references C:/SpacePatriot/ArtDirection/Modules/kestrel-hull-ortho-v1 --output C:/Builds/Kestrel-sixview
```

Unity's `LocalShipImporter.ImportBuild(buildId)` imports a specific verified candidate into ArtLab without changing `active-build.txt`; `CaptureBuild(buildId)` renders that same candidate. A failed saved-bake report is rejected. The imported hull must have UV0 and no stale UV1 layout. The actual game fleet is tracked separately in [`fleet-mesh-port-inventory.json`](../../ArtDirection/fleet-mesh-port-inventory.json); Kestrel currently has no runtime family mapping.

### Final UV unwrap after the ordered paint bake

The final output now gets a separate post-bake unwrap. `post_bake_uv_pipeline.py` runs
`repack_baked_hull.py`, then independently reopens the source and result with
`verify_repacked_hull.py`. The [xatlas Python bindings](https://github.com/mworchel/xatlas-python)
(`xatlas==0.0.11`, installed in the authoring Python) create new charts. A strict
texel-overlap audit detects folded charts; the affected faces are split into
separate UV islands and repacked. This does not move or replace hull vertices.

The transfer samples the already completed six-view paint into the new
`AtlasUV_Packed_v3` layout. It does not project another view over existing paint.
All four atlas channels retain their non-hull quadrants. The old hull UV layer
is removed after transfer. Each existing hull LOD is independently
unwrapped and transferred into its own texture set; no decimation happens after
packing. A rejected earlier candidate showed why: post-pack decimation folded
the lower-LOD UVs. Unity binds the correct material per hull LOD and caps the
lower texture imports at 1024/512 pixels with mipmaps. Other parts keep the
protected LOD0 atlas. Detailed
tangent-space normal maps are rejected by this transfer: the current neutral
hull normal is supported; an authored detailed normal must be rebaked into the
new tangent basis.

### Hull and module symmetry

The current recipe uses world +X for the nose, +Y for up, and Z for span. Its
`Hull.symmetry` setting retains the port half (world Z<=0), cuts at Z=0, mirrors
that half with its paint, and welds matching centerline vertices. This deliberately
replaces the old starboard shape and paint with the port master. It runs after
the six protected projection passes and before the final unwrap. Existing
off-centerline holes and projection defects are not repaired by symmetry.

`symmetrize_hull.py` checks saved/reopened vertex and face symmetry, winding,
interpolated UVs, material bindings, exact source PNGs, and a closed centerline.
The port half has more triangles than the old starboard half: the current
symmetric hulls have 60,792 / 21,230 / 7,548 triangles. Each is independently
unwrapped afterward; the old layout is not reused as the final bake layout.

Wings and drives use `mirror_of` recipe entries, so the second instance is a
true reflection of the first across Z=0. Opposite rotations are not equivalent.
`mirror_ship_pairs.py` bakes reflected geometry with reversed triangle winding,
preserved per-corner UVs, and positive object transforms. This mirrors existing
root placements; it does not certify an authored mounting socket or hull contact.

Normal `build-kestrel.ps1` builds follow the recipe through these steps. The
six-view pipeline preserves its intermediate projection, `PostBakeUV/HullSymmetry`,
UV, and `PairedModules` evidence. Final FBX/texture hashes are verified again after
module mirroring. To apply just hull symmetry and the final UV transfer to an
existing completed six-view blend, add `--symmetrize-hull` to
`post_bake_uv_pipeline.py`. Omitting it preserves the supplied geometry.

`bake_sixview_pipeline.py` runs this stage by default after top, bottom, port,
starboard, nose and aft. The six intermediate blends remain available for
checking paint preservation; `PostBakeUV/` holds the transfer evidence, and the
root blend/FBX/textures plus `Renders/Saved/` are the final repacked result.
`--projection-only` is an explicit diagnostic option to stop before this stage.

An independent transfer can be run with absolute paths:

```powershell
python Tools/LocalMeshes/post_bake_uv_pipeline.py --blender C:/Tools/Blender/blender.exe --input C:/Builds/Kestrel/06-aft.blend --output C:/Builds/Kestrel-final-uv
```

`saved-uv-repack-validation.json` records save/reopen checks, exact protected
pixels, packed/external PNG agreement, UV overlap checks on all three LODs,
material bindings and surface RGBA comparisons (including smoothness alpha).
Unity refuses repacked builds unless all three saved LODs passed and all twelve
texture hashes plus the FBX hash match. `CaptureBuild(buildId, lod)` captures
each imported LOD at an appropriate distance. A passing transfer preserves the existing appearance;
it does not certify concept matching or repair pre-existing projection seams,
malformed hull geometry, wings or floating landing-gear attachments.

The current review build projects the hull's [dorsal](../../ArtDirection/Generated/KestrelK017/kestrel-wing-top-pipeline-20260926/Renders/Final/top.png) and [underside](../../ArtDirection/Generated/KestrelK017/kestrel-wing-top-pipeline-20260926/Renders/Final/underside.png) references and paints the wing tops from their own view. Its [quarter view](../../ArtDirection/Generated/KestrelK017/kestrel-wing-top-pipeline-20260926/Renders/Final/quarter.png) is the post-bake image, and per-view coverage is recorded in that build's `dorsal-overlay-report.json`. Oblique and underside-facing wing surfaces still need their own art views. A neutral-emission render strips color and confirms the generated hull has no physical canopy glazing or raised panel geometry ([top](../../Validation/Hull_Top_neutral.png), [bottom](../../Validation/Hull_Bottom_neutral.png), [starboard](../../Validation/Hull_Starboard_neutral.png)). The color projection fits detected white-background crops, avoiding detail shrinkage from full concept margins. The mesh still needs physical cockpit/frame and hull-panel geometry; art pixels cannot create real openings or raised details. The neutral-view renderer is `render_hull_geometry_views.py`; the alpha-safe silhouette overlay tool is `compare_hull_view_candidates.py`. The review candidate does not change the current active build.

To repeat or resume one named build without regenerating successful meshes:

```powershell
.\Tools\LocalMeshes\build-kestrel.ps1 -BuildId kestrel-v1 -Resume
```

To rebake existing geometry against the current `kestrel-kit.json` without running TripoSG again, choose a new build id and reuse a prior build's source meshes. The new bake remains a review candidate unless `-DoNotActivate` is omitted:

```powershell
.\Tools\LocalMeshes\build-kestrel.ps1 -BuildId kestrel-rebake -ReuseMeshesFrom kestrel-v1
```

The command checks each reused mesh's recorded source-image hash before assembling. This avoids silently reusing geometry generated from a different concept image and avoids stale camera/UV settings stored in an older build manifest.

The Unity Editor menu **Space Patriot → Art lab → Import latest baked Kestrel** copies the result into a versioned ArtLab folder, configures one URP atlas material and creates a prefab with the three levels grouped by distance. **Capture latest baked Kestrel** saves actual Unity renders for comparison. These commands leave the live fleet untouched. The color bake transfers visible paint placement from the reference; it cannot invent hidden-side paint or repair inaccurate mesh silhouettes. Screens, openings, controls, mechanical pivots and walkable interiors still require explicit authored geometry and interaction systems.

Before accepting a bake, run the comparison pass:

```powershell
.\Tools\LocalMeshes\compare-kestrel.ps1 -BuildId kestrel-v1
```

It renders the ship and each LOD0 component from Nose, Aft, Starboard, Port, Top and Bottom with the actual atlas through an unlit material. It also renders each component from its recorded projection camera, so the concept comparison uses the same side and perspective that drove its UV bake. It compares the concept-facing part renders in hue/saturation space and writes silhouette IoU, area, and centroid metrics. Magenta/cyan contour overlays show where the concept and mesh disagree; white edges overlap. The six-view ship board exposes attachment gaps and unsupported surfaces; a whole-ship quarter-view overlay and plan render help review form and mounting offsets. Outputs are stored under `Renders/Comparison/`, including `six-view-ship-board.png`. Part comparisons fit foreground bounds to equal review cells; only whole-ship views preserve assembly offsets. These metrics flag mismatches for review; they do not certify a generated mesh as production-ready.

## Commands

Use an isolated Python 3.12 environment with PyTorch 2.8.0 + CUDA 12.8 and torchvision 0.23.0 for this RTX 50-series machine. Install `requirements-shape.txt` alongside those packages. Clone the pinned source and download the pinned official model before running offline inference.

`requirements-windows.lock` records the complete working Windows environment, including CUDA wheels. Install it with `python -m pip install --extra-index-url https://download.pytorch.org/whl/cu128 -r requirements-windows.lock`. The environment passes `pip check` and an actual CUDA matrix operation on this machine.

```powershell
python prepare_triposg.py C:/Tools/TripoSG
python generate_mesh.py --source C:/Tools/TripoSG --weights C:/Models/TripoSG `
  --image ../../ArtDirection/Modules/kestrel-drive-reference-v2.png `
  --output ../../ArtDirection/Generated/KestrelDrive/raw.glb --seed 726 --steps 50 --depth 9
blender --background --python-exit-code 1 --python clean_and_rig.py -- `
  --input ../../ArtDirection/Generated/KestrelDrive/raw.glb `
  --output ../../ArtDirection/Generated/KestrelDrive --metres 6
```

The Blender script produces a cleaned source, three LODs and a rigid drive-gimbal rig with Flight/Landing clips, plus inspection renders and an editable `.blend`. It deliberately uses a neutral inspection material. Production texturing and final joint alignment require visual review; a successful export is not proof that the component matches the concept.

On the configured development machine, `run-local.ps1 -Output <new-folder>` runs generation and Blender in sequence using the existing local cache. It refuses to overwrite an existing raw mesh. The historical environment folder is named `hy3d-env`, but the selected and installed model is TripoSG. `-ToolRoot` selects a relocated tool cache. Custom references use `-Image`, `-Name`, `-Metres` and `-Seed`.

Unity's **Space Patriot → Art lab → Import local Kestrel drive** command creates an inspection prefab from the Blender FBX. It verifies three LODs, six-metre maximum extent, named Flight/Landing clips and a 90-degree hinge movement. **Capture local Kestrel drive** produces actual URP renders for comparison. Passing these technical checks does not approve the art for the live fleet.

Do not generate an entire navigable city or multideck ship as one fused object. Doors, traversable spaces, moving machinery and MFD hit areas need separate geometry and gameplay integration. Generated topology is a starting point for authored assets.
