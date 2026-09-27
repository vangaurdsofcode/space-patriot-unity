# Cockpit windshield: Port 07 roof diagnostic

The broad gray panel in the parked Mule cockpit is the **Port 07 hangar roof**, not part of the cockpit model. A live Unity renderer-ablation check identified `Frontier environment/Earth / frontier/hangar(Clone)/Hangar roof 1` as the surface covering the upper windshield. Disabling cabin renderers did not affect it because those meshes belong to the ship, while the roof belongs to the world.

The roof is part of the launch sequence in `HangarLift.Advance`: the paired slabs slide sideways as the platform rises 26 m. Removing those meshes or changing the spawn position would break the covered berth. The parked view is recorded in `Validation/cockpit-roof-closed.png`. The same live scene after advancing the functional lift to its fully open state is recorded in `Validation/cockpit-roof-open.png` and `Validation/cockpit-mfd-menu-cockpit.png`; the windshield is clear.

The 31 physical screen, softkey, and rotary-control checks in `Validation/cockpit-mfd-menu-runtime.txt` passed while the lift was open. The validation restored the lift, ship, and landing pad state afterward. The open view still has a large area of plain sky and lacks the distant port composition of the cockpit target image; that is a separate environment/framing art task.
