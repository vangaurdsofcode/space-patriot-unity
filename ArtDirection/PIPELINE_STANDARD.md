# Space Patriot concept-to-game art standard

All four production categories use one stable asset ID from the brief through the Unity prefab. The contract is metres, `+Y` up, `+Z` facing forward, six orthographic review views, and a separately generated `SP_BakeUV` atlas. Camera pixels never become UV coordinates. The shared index is `Concepts/manifest.json`; the creature roster compiler adds ten named and mechanically described wildlife entries for every catalogued world.

## 1. Approve a concept before making a mesh

Use the generated `ArtDirection/Creatures/<world-id>-concepts.json` for each world's ten fauna briefs. For every stable ID, keep an approved hero image and six independent view images named `front.png`, `left.png`, `right.png`, `back.png`, `top.png`, and `three-quarter.png` in an `ArtDirection/Production/ConceptViews/<stable-id>/` folder. Keep background, camera scale, ground line, pose, silhouette, and lighting consistent across the six images. Use the matching category block from `Concepts/manifest.json`. Flora uses root contact at `Y=0`; fauna uses the feet at `Y=0`; geology uses its ground contact at `Y=0`; a weapon uses the trigger-grip or mount point as its origin. The weapon emitter faces `+Z`.

The images are references for shape and proportion. Do not treat a collage or one perspective view as a texture atlas. Tripo's multi-view endpoint accepts named views; it requires at least the front plus another view. Supply the independent approved views and turn off texturing on the geometry draft where that option is available, then inspect the model against all six views before approving it. The current API also converts a completed task to FBX with PNG textures, UV packing, and center-bottom pivot options; our Blender pass then makes the coordinate and bake outputs uniform across categories. See [Tripo multiview generation](https://developers.tripo3d.ai/en/docs/generation-multiview-to-model/standard) and [Tripo model conversion](https://developers.tripo3d.ai/en/docs/models-convert).

## 2. Return from Tripo through Blender

Put the returned `.glb` or `.fbx` beside the concept folder and run from the project root:

```powershell
blender --background --python Tools/AssetPipeline/creatures/prepare_tripo_asset.py -- `
  ArtDirection/Production/Tripo/earth-wild-01/raw.glb `
  ArtDirection/Production/Tripo/earth-wild-01/prepared-review earth-wild-01 fauna 2.0 +x 0.5,0,0.5 '#687651'
```

The arguments after `--` are source model, output folder, stable ID, category, target height in metres, and the model's horizontal source-forward axis (`+x`, `-x`, `+z`, or `-z`). Source models use `+Y` up. Blender's glTF importer changes that to Blender `+Z` up, so the script restores `+Y` up before turning the forward axis to game `+Z`. Verify the source axis from model bounds and a side view; an incorrect axis can make all six bake reviews appear valid while standing the asset on its nose. The next optional argument sets the pivot as normalized `x,y,z` fractions inside aligned bounds. The last optional argument sets a flat sRGB `#RRGGBB` fallback color for untextured geometry; the script converts it to Blender linear input before baking, and it does not manufacture concept-art detail. Omit the pivot for ground-centred categories; weapons default to a grip/mount fraction and should be reviewed per model.

The Blender run outputs a review render for each canonical view, `basecolor.png`, `normal-tangent.png`, `ambient-occlusion.png`, a `.blend` source file, an `.fbx` draft, and `bake-layout.json` with the final object bounds, axis conversion, camera transforms, UV bounds per mesh, atlas name, size, and padding. It preserves the source UV layer in Blender and exports only `SP_BakeUV` as UV0 for Unity. The view grid and bake coordinates stay independent. Bake margin is 16 pixels at 2048². Check the normal and AO PNG alpha as well as RGB; a bake command returning success does not prove that the saved map contains visible pixels. Fix topology or UV failures in Blender, rerun, and compare the six matched reference views before import.

Compare approved views and Blender output with overlays and per-view pixel error:

```powershell
node Tools/AssetPipeline/creatures/compare-views.mjs `
  ArtDirection/Production/ConceptViews/earth-wild-01 `
  ArtDirection/Production/Tripo/earth-wild-01/review `
  ArtDirection/Production/Tripo/earth-wild-01/comparison
```

The comparison is a review aid, not a blind pass threshold: paint, light and soft tissue can change pixels. Check silhouette, proportions, contact point, orientation, texture seams, overlapping bake islands, and detail readability in every view.

## 3. Category-specific checks

Weapons need a visible emitter, control surfaces, power-cell access, and readable grip/mount silhouette. Flora needs a grounded trunk/root system, multiple growth stages, wind response, and terrain-climate suitability. Fauna needs a locomotion rig or named joints, sensory features, threat cues, behavior role, ability telegraphs and a scale reference; each world has foragers, flyers, burrowers, predators, a champion and an apex entry. Geology needs a primary landform, coherent strata/fracture flow, erosion direction, material zones and collision scale. Preserve each asset's stable ID in Unity so generated Tripo replacements can slot into existing gameplay records.

Generated base anatomies remain the safe fallback. A Tripo replacement is accepted only after the six-view overlays and Unity scale/material inspection are reviewed. Keep source `.blend`, incoming model, exact brief, final maps, FBX and `bake-layout.json` together so another artist can repeat the pass without regenerating or guessing coordinates.
