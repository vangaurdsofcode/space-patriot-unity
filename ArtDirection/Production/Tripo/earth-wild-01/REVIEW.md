# earth-wild-01 local mesh review — rejected draft

The concept image `ArtDirection/Production/ConceptViews/earth-wild-01/left-master-v1.png` is a useful side-view brief. The one-view local TripoSG mesh in `raw.glb` is **not approved for Unity**. The art standard requires six approved, matching concept views; this trial began from only the left image.

## Axis and bake correction

The generated glTF has `+Y` up and `+X` forward. Blender's glTF importer changes it to Blender `+Z` up. The preparation script now restores `+Y` up before yawing `+X` forward to canonical `+Z`, and it puts the review floor in the canonical `XZ` plane. In `prepared-v5/bake-layout.json`, the corrected metres bounds are `X=[-0.561115, +0.561115]`, `Y=[0, 1.759299]`, `Z=[-1.131896, +1.131897]`. The six v5 orthographic views confirm an upright four-legged stance and forward-facing muzzle.

The script now selects the intended image node separately for base-color, tangent-normal, and AO baking. In v6 all three bake calls dirty their own image. The saved AO atlas has 244 grayscale values in a 1/16-pixel-grid sample, compared with the previous all-white output. The tangent-normal atlas is uniformly neutral `(128,128,255)` because this untextured low-poly mesh was baked against itself; it contains no sculpted high-frequency detail. Base color is a declared flat `#687651` fallback, not texture projected from the concept. Its sRGB channels now bake to `(103,117,80)` on a covered atlas pixel, within one byte of the requested `(104,118,81)`; the previous linear-input mistake baked it too pale. The UV layer is newly packed as `SP_BakeUV` and exported as FBX UV0.

## Why the draft is rejected

- `raw.glb` contains 590,227 vertices and 1,180,116 triangles; it is not watertight and has 986 open boundary edges in Blender.
- Reflection about the lateral centre plane gives a median nearest-vertex mismatch of **14.5% of body width**, with 95th percentile **32.3%**. Only **13.2%** of sampled vertices have a reflected partner within 3% of body width.
- Using a light-foreground threshold of 150 on the saved 1024-pixel neutral-color v5 review PNGs, silhouette mirror IoU is **0.362 front**, **0.405 back**, and **0.446 top**. The left side versus reflected right side is **0.616**. V6 has the same geometry and a corrected darker base color; the front head, dorsal plates, and foot placements visibly drift off centre.
- A 30%-face-count prototype kept the canonical negative-X half and used Blender Symmetrize across `X=0`. Its X bounds became exactly `±0.561141 m`, but it had **1,350 open boundary edges** after mirroring. Symmetry alone did not repair the topology. This prototype was not baked or imported.

Next pass: approve a symmetrical front and back, paired left/right, top and three-quarter concept sheet with matching body proportions; regenerate from the multiview inputs; then fix mesh integrity before UV/bake review. Do not register this one-view mesh as a creature in Unity.
