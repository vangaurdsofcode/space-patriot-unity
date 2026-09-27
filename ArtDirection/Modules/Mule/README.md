# Mule pressure-hull reference

This reference belongs to gameplay family **2 / Mule**. The input is the named Mule in the upper-right panel of `ArtDirection/fleet-target.png`; Kestrel is not used as a substitute.

`pressure-hull-turnaround-v1.png` is the immutable six-view source, generated with the built-in image generation tool. The exact prompt is in `pressure-hull-v1.prompt.txt`. It isolates the central cab and pressure tunnel so that pods, trusses, drives, landing gear and moving hatches can become separate editable components.

The original fleet board calls Mule 50 m long, while the existing catalog variants are roughly 30–35 m. This study preserves the catalog. Its proposed **26 x 9.6 x 7.1 m central hull** leaves room inside the 34.1 x 22.4 x 7.53 m Mule Chalk exterior envelope for external cargo equipment, and accommodates the existing 8.4 m room width and two decks separated by 3.3 m. These are art/planning bounds; full assembly and interior clearances have not yet been measured against a mesh.

## Extract and register

Run `python ArtDirection/Modules/Mule/prepare_pressure_hull_views.py` from the repository with Pillow, NumPy and SciPy installed. It preserves the original sheet, crops each labeled view into `views-raw/`, then creates transparent metric references in `views-registered/`. Registration is 40 pixels per metre on identical 1536 x 768 canvases. `view-registration.json` records the source hash, exact crop coordinates, detected object bounds and rescaling for every view.

The source generator did not give every panel exactly the same pixels-per-metre scale. Registration fixes the overall envelope explicitly. It does **not** prove the windows, seams and silhouette details are identical projections of one 3D object. Resolve those differences with matched-view mesh overlays; do not blindly project the raw sheet into a UV atlas.

## Next stage

Use one isolated view for the first local image-to-mesh experiment, then align the generated hull to all six registered views. The manifest defines camera directions and image axes. Nose is +Z and dorsal is +Y in the final Unity asset. Raw Tripo orientation must be measured rather than assumed. The existing room-plan coordinate system points +Z aft, so room fitting needs an explicit adapter check.

Rebuild UVs after fitting. Bake top, bottom and each remaining side separately without overwriting earlier view ownership. Reopen the saved Blender file and compare persisted textures after every pass. A rendered preview, all-LOD UV check, sealed-hull coverage, assembly attachment review and room/door/lift fit are required before runtime use.

No Tripo GPU inference, Blender mesh, bake, import or runtime replacement was performed for this reference task. `manifest.json` lists missing component art and the remaining validation; this is a ready reference experiment, not a completed Mule ship.
