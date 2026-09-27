# Earth wild 01 — Mossback concept views

Status: **silhouette references only**. These generated images are not measured, mutually registered orthographic plates and must not be projected directly into UVs.

| View | File | Use |
| --- | --- | --- |
| Left | `left-master-v1.png` | Original shape and palette master; nose points right. |
| Front | `front-v1.png` | Bilateral anatomy and front silhouette. |
| Back | `back-v1.png` | Bilateral anatomy, tail, and rear silhouette. |
| Top | `top-v1.png` | Dorsal silhouette; nose points up, tail down. |
| Right | `right-v2.png` | Preferred visual counterpart to left; shape study only. |
| Right alternate | `right-v1.png` | Rejected as less aligned to left. |

For reference coordinates, use **+Y up** and **+Z forward**, with the midline at **X=0**. Do not infer a mesh up axis from page orientation alone; the source GLB import and Blender transform must be checked separately.

Read-only silhouette check: mark pixels where the minimum RGB channel is below 0.82 and calculate intersection-over-union with the horizontally reflected silhouette at the same 1536×1024 canvas size. Front: **0.9978**; back: **0.9885**; top: **0.9970**. The mirrored left/right pair has **0.8776** IoU with `right-v2.png` and **0.8226** with `right-v1.png`. These are visual symmetry checks, not proof of matching internal features, scale, camera, or texture coordinates.

The references were produced with the built-in image-generation tool. Generated details vary across views. For production bakes, use the actual approved geometry, verified camera matrices, and its final UV layout; do not substitute these images as color-projection bakes.
