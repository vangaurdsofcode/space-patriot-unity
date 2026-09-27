# Full-sphere vegetation validation

Authored 2026-09-26. **This document describes an automated acceptance pass; it is not a test-result report.** No live Unity checks were run while authoring it. Actual results are written only by the validation entry points below.

`Assets/SpacePatriot/Editor/SphericalVegetationValidation.cs` provides:

| Entry point | Environment | Evidence written |
|---|---|---|
| `SphericalVegetationValidation.RunCells()` | Edit or Play mode | `Validation/spherical-vegetation-cells.txt` |
| `SphericalVegetationValidation.RunSampling()` | Play mode, generated temperate world | `Validation/spherical-vegetation-sampling.txt` |
| `SphericalVegetationValidation.BeginRuntime()` | Unpaused Play mode, existing `FrontierGame` | `Validation/spherical-vegetation-runtime.txt` |
| `SphericalVegetationValidation.IsRunning` | Read-only status | True until completion/failure/restoration |
| `SphericalVegetationValidation.Stop()` | Abort a running pass | Restores state and records incomplete failure |

The Editor menu also exposes the cell check and runtime start/stop. `BeginRuntime()` returns immediately; the pass advances once per rendered Play-mode frame through `EditorApplication.update`. Wait for `SPHERICAL_VEGETATION_RUNTIME_PASS` or `SPHERICAL_VEGETATION_RUNTIME_FAIL`, rather than treating a successful method invocation or the `STARTED` log as test completion. Each streaming wait times out after 90 seconds. Do not pause Play mode or recompile scripts during the pass.

The runtime pass temporarily disables the main game update, clones its in-memory save, owns the existing camera, generates test worlds, and changes the planet transform. Completion, failure and an explicit stop restore the original save reference and settlement, planet transform, world profile, camera pose and game enabled state. Restoration regenerates the original world. It does not move or respawn ships, change ship assets, call campaign persistence, or alter source assets. Run it in a disposable Play-mode validation session because it intentionally rebuilds the environment.

The cell pass covers all six face centers, all twelve cube edges and all eight corners. Its geometric oracle samples a tangent disk and measures spherical chord distances to actual returned cell centers; it does not copy the cube-face indexing algorithm. It checks unique IDs and physical centers, valid indices, repeatable ordering/seeds/candidate directions, nearest-nine capping, two-metre seam overlap, disk coverage, world identity separation and north/south separation at equal x/z directions. Sub-metre distance tolerance accommodates float dot-product ties at the 18 km planet radius.

The runtime pass locates broad dry land beyond the former 2,700 m cap at equatorial and southern sectors on Earth and TRAPPIST-1 e. Each world's 512 deterministic sphere probes check the radial frame, outward slope normal, repeated sampling, typed habitat channels, correct atlas moisture routing, eligible dry-land constraints and non-clamped global variation. The pass then inspects actual blade vertices, tree-root transforms, and stored splat roots/rotations/centers after streaming completes. It rejects submerged/inward roots, unbounded layer counts, grass meshes that lose their small tangent frame, and roots more than 1.5 m from the sampled surface. Within 115 m of the focus, it independently queries the active terrain collider and applies the same 1.5 m limit.

Additional scenarios cover opposite-hemisphere eviction/revisit using actual blade-root fingerprints; an Earth cube-seam crossing with identical overlapping tile meshes; a 280 m frame recenter that keeps the committed forest and splats present while replacements build; exact tile-mesh regeneration after revisiting; a translated and rotated unit-scale planet; the 100/500/1,800 m altitude gates; a world switch immediately after asynchronous generation starts; and a gas-world negative control. The report records selected planet-local roots/up vectors and resident counts for subsequent visual captures.

API and diagnostic assumptions are explicit: `VegetationSample.valid` means dry eligible habitat, not merely a finite solid surface; `WorldForestLayer.current` is the committed root; each grass blade has twelve vertices from the source six-pair topology; Gaussian `meadow` and `woodland` records expose private diagnostic fields `root`, `position`, `rotation`, and `cellId`; both forest and Gaussian expose `UpdateAround(Vector3)`. Reflection failures fail the pass instead of silently skipping evidence.

This pass does not establish GPU image quality, visual continuity through wind animation, exact source species/density parity, non-unit planet scale support, or real per-frame CPU budgets. Capture the selected near blades, full trees and distant splats in Unity and measure frame times separately. Existing `GaussianVegetationValidation.Run()` remains the shader-projection test. Retention checks establish that old committed data remains present and active during generation; screenshots or render instrumentation are still needed to prove the distant draw call remains visible during every build frame.
