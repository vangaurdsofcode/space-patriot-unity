using System;
using System.Collections.Generic;
using System.IO;
using SpacePatriot;
using UnityEngine;
using Object = UnityEngine.Object;

public static class VegetationSeaLevelValidation
{
    public static void Run()
    {
        var probe = new GameObject("Radial sea-level validation");
        var world = probe.AddComponent<FrontierWorld>();
        var lines = new List<string>();
        Mesh waterMesh = null;
        Material waterMaterial = null;
        try
        {
            Vector3[] normals = { Vector3.up, Vector3.right, Vector3.down, Vector3.forward,
                new Vector3(.14f,1,.04f).normalized, new Vector3(-.11f,1,.09f).normalized };
            foreach (var normal in normals)
            {
                float seaRadius = FrontierWorld.PlanetRadius + FrontierWorld.SeaLevel;
                Vector3 dry = world.PlanetCenter + normal * (seaRadius + 3);
                Vector3 wet = world.PlanetCenter + normal * (seaRadius - 1);
                if (!world.CanRootVegetation(dry) || world.CanRootVegetation(wet))
                    throw new Exception("Radial vegetation sea test changed with planet orientation: " + normal);
                if (Mathf.Abs(world.ElevationAboveSea(dry) - 3) > .006f)
                    throw new Exception("Dry surface elevation does not use the shared ocean datum.");
                lines.Add("PASS: dry land accepted and submerged land rejected at normal " + normal);
            }

            // Inspect Oceanworks' actual generated vertices, not a second copy
            // of the intended formula. They must lie on the same water sphere.
            world.info = new WorldInfo { biome = "temperate" };
            var water = new GameObject("Water datum validation");
            water.transform.SetParent(probe.transform, false);
            water.AddComponent<Oceanworks>().Initialize(world);
            waterMesh = water.GetComponent<MeshFilter>().sharedMesh;
            waterMaterial = water.GetComponent<MeshRenderer>().sharedMaterial;
            float worst = 0;
            foreach (var vertex in waterMesh.vertices)
                worst = Mathf.Max(worst, Mathf.Abs(world.ElevationAboveSea(vertex)));
            if (worst > .006f) throw new Exception("Water and vegetation disagree on sea level by " + worst + " m.");
            lines.Add("PASS: every Oceanworks vertex shares the radial sea level (max error " + worst.ToString("F6") + " m)");
            Directory.CreateDirectory("Validation");
            File.WriteAllLines("Validation/vegetation-radial-sea-level.txt", lines);
            Debug.Log("VEGETATION_RADIAL_SEA_LEVEL_PASS " + lines.Count);
        }
        finally
        {
            // Oceanworks uses deferred cleanup during play. Dispose its assets
            // first here so this probe is also safe to run in Edit mode.
            if (waterMesh) Object.DestroyImmediate(waterMesh);
            if (waterMaterial) Object.DestroyImmediate(waterMaterial);
            Object.DestroyImmediate(probe);
        }
    }

    public static Vector3 FindRecoveredLand(FrontierWorld world)
    {
        // Land wrongly excluded by the previous absolute-Y water test, far
        // enough from the port to make the curvature error unambiguous.
        for (int z = 300; z <= 1600; z += 64)
        for (int x = 1500; x <= 2450; x += 64)
        {
            if (new Vector2(x,z).magnitude > 2500 || !world.grass.CanGrow(x,z)) continue;
            float height = world.Height(x,z);
            if (height >= -10 || world.terrainFields.Sample(x,z).y < .1f) continue;
            return new Vector3(x,height,z);
        }
        throw new Exception("No previously excluded dry test land found in this world.");
    }

    public static void Runtime()
    {
        var world = FrontierGame.Instance?.world;
        if (world == null || world.info.biome != "temperate") throw new Exception("Enter a temperate world in Play mode.");
        Vector3 point = FindRecoveredLand(world);
        for (int i = 0; i < 10; i++) world.grass.UpdateAround(world.transform.TransformPoint(point + Vector3.up * 3));
        if (world.grass.BladeCount < 1000) throw new Exception("Recovered dry land did not generate near grass.");
        float elevation = world.ElevationAboveSea(point);
        var lines = new[] {
            "PASS: grass grows on land previously rejected for negative Y: " + point,
            "Radial clearance above sea: " + elevation.ToString("F3") + " m",
            "Grass blades: " + world.grass.BladeCount,
            "SCOPE: this check covers radial shoreline eligibility; SphericalVegetationValidation exercises full-sphere streaming and its remaining limits."
        };
        Directory.CreateDirectory("Validation");
        File.WriteAllLines("Validation/vegetation-radial-runtime.txt", lines);
        Debug.Log("VEGETATION_RADIAL_RUNTIME_PASS");
    }
}
