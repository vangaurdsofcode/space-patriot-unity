# Spherical vegetation implementation and evidence

Verified in the connected Unity 6000.3.25f1 Editor on 2026-09-26. This replaces the former 2,700 m vegetation cutoff; it does not claim full planetary rendering or final vegetation art is finished.

`PlanetVegetation.cs` supplies one typed radial surface/habitat sample and persistent equal-angle cube cells keyed by world, face, resolution and cell coordinates. Grassworks, ForestStage and Gaussian vegetation use it together. Roots use the rendered globe height away from the authored northern port, global climate atlas channels, a 2 m radial sea clearance, radial up and northern-only port/outpost/grove exclusions. North/south positions with equal x/z no longer alias. Source `PlanetEngines.region` grass height, width, density variation, forest-cover and species formulas are evaluated in the source coordinate frame. Native grass density is deliberately capped rather than claiming the HTML engine's full blade population.

Near grass retains at most nine 48 m cells / 108,000 blades; full trees retain a 240 m neighborhood capped at 2,000 trees. Gaussian generation covers 440 m meadow and 2,050 m woodland neighborhoods with 60,000/150,000 resident splat limits, frustum/range culling and back-to-front sorting. During a rebuild there can be one committed and one pending neighborhood; the old neighborhood stays visible until replacement commits. Teleports and world changes cancel stale work. Activation gates match the source's 100/500/1,800 m bands. Every 32 attempted candidates, including rejected points, construction checks a 4 ms work target; the timestamp restarts after each yield.

The streamed surface patch now derives its frame from the planet-center radial direction, not a sloped surface normal. Surface queries synchronize PhysX after a changed planet matrix and invalidate the old patch anchor. Day/night factors in the actual grass and Gaussian shaders now match source `vegetation-stage.js`; this is separate from the original engine-lab firefly effect.

## Actual results

- [Full runtime acceptance](../Validation/spherical-vegetation-runtime.txt): PASS on Earth and TRAPPIST-1 e, equatorial and southern dry land, root/collider agreement, stable eviction/revisit, all-layer altitude gates, translated/rotated unit-scale planet, mid-build world switch and gas-world negative control. Worst inspected live collider error: 0.071 m; minimum outward plant dot: 1.000.
- [Cell acceptance](../Validation/spherical-vegetation-cells.txt): 2,496 inspected cells; all six face centers, twelve seams and eight corners, no spatial holes, unique world/hemisphere IDs and repeatable seeds/geometry. The runtime Earth seam retained six identical overlapping tile meshes.
- [GPU daylight](../Validation/vegetation-daylight.txt): measured grass night/day pixel energy 0.05500; Gaussian 0.06000, matching source values.
- [GPU covariance](../Validation/gaussian-vegetation-projection.txt): five projected ellipsoid cases and eye-plane rejection still pass with the updated shader.
- [Query profiling](../Validation/spherical-vegetation-query-cost.txt): on this Editor, synchronous cell selection/sort measured 0.03–0.08 ms for grass, 0.6–1.1 ms for trees, 7.8–11.1 ms for meadow and 14.8–18.5 ms for woodland. This runs per recenter, not every frame. It is **not** a hard 4 ms total-frame guarantee; selection/sort and final mesh/static-batch uploads remain synchronous.

| Site | Blades / tiles | Trees | Meadow resident / visible | Woodland resident / visible |
|---|---:|---:|---:|---:|
| Earth equatorial | 97,943 / 9 | 1,003 | 43,716 / 11,230 | 115,669 / 24,560 |
| Earth southern | 100,458 / 9 | 1,119 | 47,364 / 12,391 | 113,759 / 28,893 |
| TRAPPIST-1 e equatorial | 102,477 / 9 | 1,218 | 51,000 / 13,024 | 121,045 / 29,055 |
| TRAPPIST-1 e southern | 103,518 / 9 | 789 | 20,604 / 5,865 | 53,970 / 11,044 |

## Visual review and remaining work

Captures are direct renders of the actual Unity camera; accompanying `.txt` files record planet, position, radial up, layer counts and whether generation had settled. No lighting change was made to obtain the daylight site.

- [Earth equatorial night](../Validation/spherical-earth-equatorial-night.png)
- [Earth southern daylight](../Validation/spherical-earth-southern-day.png)
- [TRAPPIST-1 e equatorial](../Validation/spherical-trappist-equatorial.png)
- [TRAPPIST-1 e southern](../Validation/spherical-trappist-southern.png)

The captures expose an existing atmosphere defect: `FrontierSky.mat` uses Unity's procedural sky with a fixed world-Y horizon. At equatorial/southern radial camera orientations its grey ground hemisphere becomes a vertical/slanted sky band. `FrontierWorld.Atmosphere` currently switches Skybox/SolidColor but does not supply radial atmospheric up. This needs a separate radial sky/atmosphere port; the pictures are not presented as final visual quality.

This pass also does not establish arbitrary non-unit planet scale, exact high-resolution climate/source population equivalence, weather/grass wind-shelter parity, final tree art, complete terrain/geology parity, or a finished planet-to-space transition. Non-temperate world profile metadata mismatches identified in the original design note are still separate work. The original grass engine-lab fireflies are not implemented by these native vegetation classes.
