# Radial sky and ascent check

Verified in the connected Unity 6000.3.25f1 Editor on 2026-09-26. The previous `Skybox/Procedural` material had a fixed world-Y horizon. `FrontierWorld.Atmosphere` also received `ship.position.y - Deck`, so equatorial and southern locations had the wrong altitude and the camera switched abruptly to a solid background while ascending.

`FrontierSky.cs` now measures altitude and local up from the transformed planet center. It supplies those values to `RadialSky.shader` every frame, with the current world's climate tint and sun direction. The camera keeps a skybox during ascent; atmospheric color and fog fade with radial altitude. Mercury has no air, Mars has thin air, and TRAPPIST-1 e uses a red-star climate tint. The sky shader also draws sparse stars into the background so they cover every viewing direction around the planet.

## Live acceptance

The [Unity runtime report](../Validation/radial-sky-runtime.txt) records 12 GPU captures. Earth at 3 m altitude produced identical sampled zenith and horizon colors at the north pole, equator, and south pole when the sun was placed at the same local angle. Mean zenith luminance decreased through 200, 450, 800, and 1,200 m, while `CameraClearFlags` remained `Skybox` throughout. Fog density declined from 0.0002152 at 3 m to zero in space. Mars rendered a thinner sky; Mercury rendered a dark, airless sky with zero fog. The shader was loaded and supported by the live Editor.

- [Earth equatorial world view](../Validation/radial-sky-earth-equator-world.png)
- [Earth southern world view](../Validation/radial-sky-earth-south-world.png)
- [Earth 800 m ascent](../Validation/radial-sky-earth-ascent-800.png)
- [TRAPPIST-1 e sky](../Validation/radial-sky-trappist-1-e.png)
- [Mercury airless sky](../Validation/radial-sky-mercury.png)

The two world captures are actual terrain renders from the same far-hemisphere locations used in spherical vegetation validation. Their skies no longer contain the old vertical/slanted grey ground hemisphere. The capture camera did not populate the vegetation layers, so these images check sky and terrain integration rather than finished biome art.

## Remaining visual work

This is a radial sky approximation. It has not ported the original `world.frag.glsl` 12-step atmosphere scattering or the source weather clouds and spectral molecule overrides. The space-facing planetary limb and the foreground terrain lighting need a joint atmosphere/lighting pass. The older orbital star mesh remains centered near the authored port; the new shader's stars fill the missing directions, but the redundant geometry should be retired or replaced. Terrain material repetition, planet streaming LOD, and world-engine biome fidelity are separate open work.
