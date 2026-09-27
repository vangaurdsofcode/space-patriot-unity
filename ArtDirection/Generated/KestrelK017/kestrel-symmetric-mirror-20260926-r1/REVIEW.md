# Symmetric Kestrel review

The hull, wings and drives now use the port side as the master across world Z=0.
Nose remains +X; up remains +Y. The hull centerline is cut and welded, rather than
placing two complete overlapping hulls. Each hull LOD received a new UV unwrap
and paint transfer after the geometry change. Opposite module instances have
reflected geometry and corrected winding, without negative object scale.

This is the latest ArtLab review target (`active-build.txt`). Its Unity prefab is
`Assets/SpacePatriot/ArtLab/KestrelK017/kestrel-symmetric-mirror-20260926-r1/Kestrel_K017.prefab`.
It is also registered as the separate selectable `SP-K017` prototype in the
playable fleet. The original 100 craft remain in their own slots.

## Evidence

- [Actual Unity render](Renders/unity-0.png); nine captures cover all three LODs.
- [Side and centerline comparison board](SymmetryComparison/symmetry-comparison.png).
- [Image comparison metrics](SymmetryComparison/symmetry-view-metrics.json): silhouette IoU
  0.99988578–0.99992436, mean RGB differences 2.13–4.04 out of 255. No fitting or
  alignment was applied to conceal differences.
- [Hull source validation](hull-symmetry-validation.json): reopened geometry has exact
  vertex symmetry, correct reflected winding, retained/interpolated port UVs,
  unchanged source atlas bytes, and no open centerline edges.
- [Pair validation](mirror-pair-validation.json): all six wing/drive LOD pairs retain
  UV corners and reflected placement, with positive object transforms.
- [Final UV validation](saved-uv-repack-validation.json): three independent unwraps,
  no detected interior-texel overlaps, twelve packed/external texture matches,
  preserved non-hull atlas quadrants, and the final FBX hash.
- [Imported Unity mesh checks](unity-mirror-runtime-validation.txt): actual imported
  hull shape/normals and paired module position/UV/normal correspondence passed;
  maximum reflection error is below 0.0000031 metres. Hull texture sizes are
  4096/1024/512 by LOD, with separate material bindings.
- [Playable runtime checks](../../../../Validation/kestrel-playable-runtime.txt): the
  registered prototype passed 14 live checks for selection, launch, nose-first
  flight, landing gear, collision proxies and all three detail levels.

## Remaining work

Symmetry does not repair the retained half's source defects. The hull still has
372/370/370 open off-centerline edges and eight edges with more than two faces.
Its mirrored LODs contain 60,792/21,230/7,548 triangles because the retained port
half was denser than the discarded half. Paint projection seams, unfinished
drive/wing surfaces and hanging landing-gear mounts remain visible. The runtime
uses temporary collision proxies and the original family-zero pressure cabin;
matching authored cockpit, articulated gear and art-quality surface detail remain
unfinished. Registration as a flyable prototype does not approve the art.

The recipe and six-view build pipeline now repeat symmetry before the final UV
transfer, then mirror paired modules and verify the saved output again. Failed
or interrupted final publication leaves a failing UV report, preventing import
of a mixed FBX/texture set.
