using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SpacePatriot;
using Object = UnityEngine.Object;

public static class AuthoredShipRuntimeValidation
{
    public static void Run()
    {
        using var inputScope = new ValidationInputScope();
        var game = FrontierGame.Instance;
        if (!Application.isPlaying || game == null) throw new InvalidOperationException("Kestrel gameplay validation requires Play mode.");
        var rows = new List<string>();
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("KESTREL RUNTIME FAILED: " + label);
            rows.Add("PASS " + label);
        }
        var fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(FrontierGame).GetField(name, fields).SetValue(game, value);
        object Get(string name) => typeof(FrontierGame).GetField(name, fields).GetValue(game);
        void Call(string name, params object[] args) => typeof(FrontierGame).GetMethod(name, fields).Invoke(game, args);
        var originalSave = game.save;
        var originalPosition = game.ship.position;
        var originalRotation = game.ship.rotation;
        bool originalFlying = game.flying, originalGear = game.gearDown, originalCockpit = game.cockpit;
        bool originalWalking = game.walking, originalMenu = game.menu, originalStarted = game.started;
        bool originalAboard = game.aboard, originalPowered = game.powered;
        var originalCameraPosition = game.view.transform.position;
        var originalCameraRotation = game.view.transform.rotation;
        var originalCameraRect = game.view.rect;
        var originalFov = game.view.fieldOfView;
        var originalKeyboard = Keyboard.current;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            var spec = ShipSpec.Fleet[100];
            Check(ShipSpec.Fleet.Length == 101 && spec.designation == "SP-K017" && spec.family == -1,
                "100 original craft remain and Kestrel has its own prototype slot");
            game.save = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(originalSave));
            game.save.ship = 100;
            game.save.fuel = 100;
            game.save.hull = spec.health;
            Call("RespawnShip");
            var marker = game.ship.GetComponentInChildren<AuthoredShipInstance>();
            Check(marker != null && marker.designation == spec.designation && marker.sourceBuild == "kestrel-symmetric-mirror-20260926-r1",
                "Actual FrontierGame ship uses registered authored Kestrel exterior");
            Check(marker.collisionParts == 8 && marker.measuredSize.x > 19.8f && marker.measuredSize.z > 19.3f,
                "Eight collision proxies and model axes match the playable ship");
            var exterior = game.ship.GetComponent<OriginalShip>().exterior;
            var lod = exterior.GetComponentInChildren<LODGroup>();
            Check(lod != null && lod.GetLODs().Length == 3 && lod.GetLODs()[0].renderers.Length == 8,
                "All three imported mesh LODs survive runtime instantiation");
            var gear = game.ship.GetComponent<OriginalShip>().gear;
            Check(gear.Length == 9, "Both main legs and nose gear are connected across LODs");
            game.ship.GetComponent<OriginalShip>().SetGear(false);
            Check(Array.TrueForAll(gear, t => t != null && !t.gameObject.activeSelf), "Gear command retracts every rendered LOD leg");
            game.ship.GetComponent<OriginalShip>().SetGear(true);
            Check(Array.TrueForAll(gear, t => t != null && t.gameObject.activeSelf), "Gear command restores every rendered LOD leg");
            Check(game.cabin.GetComponentsInChildren<CockpitControl>().Length >= 37,
                "Temporary pressure deck retains live cockpit controls and MFD keys");

            var openGround = new Vector3(650, 0, 230);
            openGround.y = game.world.SurfaceAt(openGround) + game.StandHeight;
            game.ship.position = openGround;
            game.ship.rotation = Quaternion.identity;
            game.flying = false; game.walking = false; game.aboard = false;
            game.cockpit = false; game.menu = false; game.started = true;
            game.gearDown = false; game.powered = true; game.throttle = 1;
            game.cargoDoor = false;
            Call("Launch");
            Check(game.flying, "Actual launch command clears the surface and enters flight");
            Set("focused", true); Set("inputNeutral", false); Set("velocity", Vector3.zero);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            InputSystem.Update();
            Call("Flight", .02f);
            var velocity = (Vector3)Get("velocity");
            Check(velocity.z > 0 && game.ship.forward.z > .99f,
                "Actual keyboard forward thrust flies the authored ship nose first");
            Check(exterior.GetComponentsInChildren<BoxCollider>().Length == 8,
                "Runtime collision proxies remain attached during flight");

            game.ship.position = new Vector3(0, game.world.Deck + game.StandHeight, 0);
            game.ship.rotation = Quaternion.identity;
            game.flying = false; game.walking = true;
            exterior.gameObject.SetActive(true);
            game.cabin.gameObject.SetActive(false);
            game.view.rect = new Rect(0, 0, 1, 1);
            game.view.fieldOfView = 46;
            game.view.transform.position = game.ship.position + new Vector3(17, 9, 20);
            game.view.transform.LookAt(game.ship.position + Vector3.up * 1.5f);
            for (int level = 0; level < 3; level++)
            {
                lod.ForceLOD(level);
                Check(lod.GetLODs()[level].renderers.Length == 8,
                    "Runtime LOD" + level + " renders all eight authored components");
                Capture(game.view, "Validation/kestrel-playable-runtime-lod" + level + ".png");
            }
            lod.ForceLOD(-1);
            Capture(game.view, "Validation/kestrel-playable-runtime.png");
            File.WriteAllLines("Validation/kestrel-playable-runtime.txt", rows);
            Debug.Log("KESTREL_PLAYABLE_RUNTIME_PASS " + rows.Count + " actual checks.");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            if (originalKeyboard != null) originalKeyboard.MakeCurrent();
            game.save = originalSave;
            Call("RespawnShip");
            game.ship.position = originalPosition;
            game.ship.rotation = originalRotation;
            game.flying = originalFlying; game.gearDown = originalGear;
            game.cockpit = originalCockpit; game.walking = originalWalking;
            game.menu = originalMenu; game.started = originalStarted;
            game.aboard = originalAboard; game.powered = originalPowered;
            game.view.transform.position = originalCameraPosition;
            game.view.transform.rotation = originalCameraRotation;
            game.view.rect = originalCameraRect;
            game.view.fieldOfView = originalFov;
        }
    }

    static void Capture(Camera camera, string path)
    {
        var rt = new RenderTexture(1280, 720, 24);
        var active = RenderTexture.active;
        var target = camera.targetTexture;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
            RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            Directory.CreateDirectory("Validation");
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = target;
            RenderTexture.active = active;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(rt);
        }
    }
}
