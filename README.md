# Space Patriot — Unity restoration

Open this folder in Unity 6000.3.25f1, then open `Assets/SpacePatriot/Scenes/Frontier.unity` and enter Play mode. This is an incomplete restoration; see PORT_STATUS.md for actual scope and remaining work.

## Controls

- Walk: W/S forward/back, A/D strafe, Shift run, F board or leave the pilot seat, E/Z interact.
- Flight: hold Space to launch/ascend; Ctrl descends. W/S forward/reverse; A/D strafe. G retracts gear; X brakes.
- Aim: hold right mouse and move, or use arrow keys. Q/E roll. V changes camera; U aligns the horizon.
- Mouse wheel changes the speed limit. Shift boosts. T toggles assist. L requests landing assist.
- Cockpit: click the physical softkeys; scroll over NAV/COMM/SENSOR/DRIVE knobs to change their settings.
- Interior: F leaves the seat; WASD walks. E operates stations and bulkheads. At the service lift, E goes down and Shift+E goes up. Use the aft airlock to disembark.
- Community jobs and the settlement directory are in the Operations overview. District terminals record deliveries, checkpoints and repairs.
- Z/I changes cockpit instrument mode. Tab/M navigation; K fleet; Escape computer/menu.
- Y arms weapons; 1/2/3 select ship weapons, 1/2 select ground weapons; R reloads; C selects a target.
- Settings includes separate mouse/controller vertical inversion and mouse sensitivity. Normal mode means up looks up.
- Cargo: buy at the freight exchange, approach the cargo ramp, open the hatch and load. Unload before selling or handing over freight contracts. Close the hatch before launch.

The original source and artwork are retained under `Reference/Original`. The current fleet includes ten revised Blender-built exterior families, original specifications, connected multideck interiors, and the separate flyable Kestrel K-017 prototype (`SP-K017`). The Downloads originals are untouched.

Grassworks, Spellworks, terrain, weather, ocean and fire behavior now have native Unity integrations. See [ENGINE_INTEGRATION.md](ENGINE_INTEGRATION.md) for exactly what runs and what remains incomplete. Rebuildable conversion tools are in [Tools/AssetPipeline](Tools/AssetPipeline).

## Development target

**Deliverable: the Unity project. Eventual release: an installable Windows PC game.** Browser delivery has been dropped. An installer will be prepared when the game is complete; this revision does not ship one.

Use Unity Hub to open this folder with **Unity 6000.3.25f1** and let its pinned packages import. Open `Assets/SpacePatriot/Scenes/Frontier.unity`, then press **Play**. Windows x64 is the active development target. No web server is required.

The future standalone player has window/fullscreen controls (F11), Save & Quit, and local JSON campaign saves with a previous-generation backup under `Application.persistentDataPath/saves`. Editor Play mode retains its existing PlayerPrefs save. Browser saves are not automatically migrated into the editor or desktop player.

`Space Patriot > Build Windows desktop` is available for later developer builds. Packaging and native-player verification are deferred; the current verification is in the Unity Editor. Historical WebGL tools and reports are retained as source history, not a delivery requirement.

Unity caches, local settings, generated builds and credentials are excluded from version control. Install Git LFS before cloning or pulling to receive the editable Blender and FBX files plus the Kestrel texture atlases. Public source availability does not grant a new license; existing asset and package terms still apply.

Validation reports live under `Validation/`. Virtual-device tests exercise the running flight controller, cargo, MFD controls and deck lifts. They do not establish physical controller feel, art quality, full game completion or sustained performance.

## Current visual revision

The first **locally generated mesh** remains an art-lab trial: [Kestrel drive trial](ArtDirection/Generated/KestrelDrive/VISUAL_REVIEW.md), [editable Blender file](ArtDirection/Generated/KestrelDrive/Kestrel_Drive.blend), and [artwork/mesh comparison](ArtDirection/local-mesh-review.html). TripoSG generated its geometry locally; Blender cleanup, three LODs, a mechanical rig and Unity animation import were verified. That isolated trial is not the finished fleet asset.

A repeatable [multipart ship recipe and Blender bake pipeline](Tools/LocalMeshes/README.md) builds Kestrel K-017 from a hull, paired wings, gear and drives. The current [symmetric source and six-view review](ArtDirection/Generated/KestrelK017/kestrel-symmetric-mirror-20260926-r1/REVIEW.md) includes the editable `.blend`, three FBX LODs, atlas maps and measured comparison renders. The hull is cut and mirrored across the ship's world-Z centerline; the wings and drives use reflected geometry. The same asset is now [registered as a playable, selectable prototype](ArtDirection/AUTHORED_SHIP_RUNTIME.md). Actual Editor tests passed launch, nose-first flight, gear controls and all three LODs. Gear fit, paint seams, authored cockpit and surface detail still need art work before this is a finished ship.

Model artists can start with the [model contribution guide](MODEL_CONTRIBUTING.md), which documents the ship axes, handoff folder, asset expectations, and comparison workflow. The [current side/centerline board](ArtDirection/Generated/KestrelK017/kestrel-symmetric-mirror-20260926-r1/SymmetryComparison/symmetry-comparison.png) and [live runtime capture](Validation/kestrel-playable-runtime.png) show the present asset and its remaining issues.

The working tools and pinned Windows environment are in [Tools/LocalMeshes](Tools/LocalMeshes). The installed neural model is an offline asset-authoring tool; it is not included in the game or this repository.

![Locally generated Kestrel engine after Blender cleanup](ArtDirection/Generated/KestrelDrive/quarter.png)

Editor captures of the refitted chassis and continuous terrain. These are work in progress, not final artwork.

![Current Meridian exterior in Unity](ArtDirection/Renders/refit-9.png)
![Current live cockpit in Unity](ArtDirection/Renders/live-cockpit.png)

Editable fleet model: [SpacePatriot-Fleet.blend](ArtDirection/Production/SpacePatriot-Fleet.blend). See [society and art implementation](ArtDirection/DESIGN_AND_IMPLEMENTATION.md) for source references, rebuild order and remaining limits.
