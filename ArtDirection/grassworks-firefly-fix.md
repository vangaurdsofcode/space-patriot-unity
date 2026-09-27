# Original Grassworks firefly fix

Scope: `Reference/Original/vendor/user-engines/grasspack3js.html`, which imports Three.js **0.180.0** from jsDelivr. This changes the original standalone engine demo. The Unity port has no native firefly implementation, so this is not evidence that a Unity effect was corrected.

The previous 120-point system used `PointsMaterial` with perspective size attenuation and one opacity oscillation shared by every insect. Near-camera point growth and synchronized brightness were removed. The new material:

- caps the entire circular point sprite at **6 CSS pixels**, including at different render pixel ratios;
- has zero opacity within **0.45 m**, smoothly reaching normal opacity by **2 m**, and discards behind-camera points;
- uses a soft round halo and yellow-green core; its brightest linear source color is `(0.48, 0.62, 0.035)` and maximum opacity is **0.42**;
- explicitly uses normal alpha blending, depth testing, no depth writes and no HDR/tone-mapping boost;
- stores a separate phase and **2.8–7 second** pulse period for each insect;
- preserves the existing drifting positions, checkbox visibility and Reset behavior.

The browser shader uses the r180 `ShaderMaterial` path and its `colorspace_fragment` conversion. These were checked against the [r180 WebGLProgram source](https://github.com/mrdoob/three.js/blob/r180/src/renderers/webgl/WebGLProgram.js) and [r180 color-space shader chunk](https://github.com/mrdoob/three.js/blob/r180/src/renderers/shaders/ShaderChunk/colorspace_fragment.glsl.js).

## Reproduce checks

Run `node Tools/EngineValidation/verify_grassworks_fireflies.mjs`. It extracts and parses the actual demo module, executes the actual firefly setup/animation against minimal Three.js data stubs, and checks projection/fade/alpha bounds over depth, pixel ratio and viewport-size combinations. Results are saved to `Validation/grassworks-firefly-source-regression.json`. This is source/CPU verification, not a browser shader compile.

Serve the repository with a local HTTP server and open `Reference/Original/vendor/user-engines/grasspack3js.html?validate-fireflies=1`. A visible JSON panel reports actual WebGL render-target pixel tests using a clone of the production firefly material: visible fragments, six-pixel footprint, near/behind suppression, 120 overlapping insects, pulse brightness, and existing toggle event handlers. Ordinary visits do not run the diagnostics.

The test temporarily renders into a 256-square target and restores the original renderer target and visibility state. It does not substitute a separate demo or screenshot. A successful browser result must show `passed: true`; source checks alone do not establish GPU success.

The maximum-pulse GPU sample uses phase `PI/2` at time zero, and requires a measurable luminous core (`peak >= 20/255`) as well as the upper size/color bounds. The first narrow-core draft was too faint between fragment centres; broadening the Gaussian core corrected that sampling loss without relaxing the size or near-camera protection.

## Actual browser result

The root agent opened the final demo in the parent browser through CUA, reloaded after the core adjustment, and observed **all eight WebGL checks pass**. The maximum-pulse insect produced a 4 x 4-pixel footprint with peak 31/255 and zero white pixels. The 120-insect overlap produced peak 118/255 and zero white pixels; near-camera and behind-camera samples produced no lit pixels. The dim pulse produced peak 3/255. The complete observed measurements and source hash are recorded in `Validation/grassworks-firefly-gpu.json`.

The isolated GPU result establishes shader visibility, bounds and overlap behavior. It does not claim exhaustive full-scene camera-trajectory testing. The ordinary local preview is `http://127.0.0.1:8794/Reference/Original/vendor/user-engines/grasspack3js.html`.
