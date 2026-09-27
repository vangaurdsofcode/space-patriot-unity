using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using SpacePatriot;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Behavioural acceptance checks for full-sphere vegetation. RunCells is safe in
/// Edit mode; BeginRuntime needs an unpaused Play-mode FrontierGame. The runtime
/// driver temporarily owns the camera and world, then restores their original
/// state. It never moves, respawns or changes a ship and never writes a save.
/// </summary>
public static class SphericalVegetationValidation
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const float Radius = FrontierWorld.PlanetRadius;
    const string ReportPath = "Validation/spherical-vegetation-runtime.txt";
    static IEnumerator running;
    static Action restore;
    static List<string> runtimeLines;
    static int previousFrame = -1;
    public static bool IsRunning => running != null;

    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("SPHERICAL VEGETATION: " + message);
    }

    static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:R},{1:R},{2:R})", v.x, v.y, v.z);
    static float Metres(Vector3 a, Vector3 b) => (a.normalized - b.normalized).magnitude * Radius;
    static object Field(object owner, string name)
    {
        var field = owner.GetType().GetField(name, Fields);
        Require(field != null, owner.GetType().Name + " has no diagnostic field " + name);
        return field.GetValue(owner);
    }

    [MenuItem("Space Patriot/Validation/Spherical vegetation cells")]
    public static void RunCells()
    {
        var lines = new List<string>();
        var probes = new List<Vector3> { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        // Every cube edge and corner, without relying on the implementation's
        // face ordering, projection formula or neighbour-remapping convention.
        for (int zero = 0; zero < 3; zero++)
        for (int a = -1; a <= 1; a += 2)
        for (int b = -1; b <= 1; b += 2)
        {
            var d = new Vector3(a, b, 0);
            if (zero == 0) d = new Vector3(0, a, b);
            if (zero == 1) d = new Vector3(a, 0, b);
            probes.Add(d.normalized);
        }
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2) probes.Add(new Vector3(x, y, z).normalized);

        int checkedCells = 0;
        foreach (var d in probes)
        {
            var cells = PlanetVegetationCells.Near("earth", d, 48, 160);
            var again = PlanetVegetationCells.Near("earth", d, 48, 160);
            Require(cells.Count > 9, "A spherical neighbourhood is empty or incomplete at " + V(d));
            Require(cells.Count == again.Count, "Repeated cell query changed size.");
            var ids = new HashSet<string>();
            var centers = new List<Vector3>();
            float previous = -1;
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                Require(ids.Add(c.ToString()), "Duplicate seam ID " + c);
                Require(c.WorldId == "earth" && c.Face >= 0 && c.Face < 6 && c.Divisions > 0 && c.U >= 0 && c.U < c.Divisions && c.V >= 0 && c.V < c.Divisions, "Invalid persistent cell " + c);
                Require(c.Equals(again[i]) && c.Seed(12345) == again[i].Seed(12345), "Repeated query changed identity, order or seed.");
                var center = c.Direction();
                Require(Finite(center) && Mathf.Abs(center.magnitude - 1) < .00001f, "Cell center is not a unit direction.");
                // Distinct cell IDs must not produce the same physical center.
                Require(centers.All(p => (p - center).sqrMagnitude > 1e-14f), "Two seam IDs own one physical center.");
                centers.Add(center);
                float distance = Metres(d, center);
                // Dot-product sorting at 18 km quantizes sub-metre ties in float.
                Require(distance + .7f >= previous, "Cell query is not nearest first.");
                Require(distance < 160 + 48 * 2, "Cell query escaped its bounded neighbourhood.");
                previous = distance;
                Require((c.Direction(.173f, .827f) - again[i].Direction(.173f, .827f)).sqrMagnitude == 0, "Candidate direction is not repeatable.");
            }
            var nearest = PlanetVegetationCells.Near("earth", d, 48, 160, 9);
            Require(nearest.Count == 9 && nearest.SequenceEqual(cells.Take(9)), "Resident cap does not select the nearest nine cells.");
            // Cover a disk in an independently constructed tangent plane. A
            // missing edge/corner neighbour leaves a measurable geometric hole.
            Vector3 tangent = Vector3.Cross(d, Mathf.Abs(d.y) < .8f ? Vector3.up : Vector3.right).normalized;
            Vector3 other = Vector3.Cross(d, tangent).normalized;
            for (int ring = 0; ring <= 3; ring++)
            for (int angle = 0; angle < 24; angle++)
            {
                float phase = angle * Mathf.PI / 12;
                Vector3 target = (d * Radius + (tangent * Mathf.Cos(phase) + other * Mathf.Sin(phase)) * (ring * 45)).normalized;
                Require(centers.Min(p => Metres(target, p)) < 48 * 1.2f, "Geometric hole at cube seam/corner near " + V(d));
            }
            // A two-metre step must retain most of a 160 m neighbourhood.
            var moved = PlanetVegetationCells.Near("earth", (d * Radius + tangent * 2).normalized, 48, 160);
            Require(moved.Count(c => ids.Contains(c.ToString())) >= cells.Count * .85f, "Crossing a seam replaced unrelated persistent cells.");
            checkedCells += cells.Count;
        }

        Vector3 north = new Vector3(.17f, .98f, .08f).normalized;
        Vector3 south = new Vector3(north.x, -north.y, north.z);
        var northCells = PlanetVegetationCells.Near("earth", north, 48, 160);
        var southCells = PlanetVegetationCells.Near("earth", south, 48, 160);
        Require(!northCells.Intersect(southCells).Any(), "Equal x/z on opposite hemispheres aliases cell IDs.");
        Require(northCells.All(c => c.Direction().y > 0) && southCells.All(c => c.Direction().y < 0), "Opposite hemisphere query returned mirrored northern cells.");
        var otherWorld = PlanetVegetationCells.Near("trappist-1-e", north, 48, 160);
        Require(!northCells.Intersect(otherWorld).Any(), "Different worlds share persistent cell IDs.");
        lines.Add("PASS: 6 face centers, all 12 edges and all 8 corners have finite, unique, sorted, deterministic cells and no geometric coverage gaps.");
        lines.Add("PASS: nine-cell cap, two-metre seam crossing, interior candidate revisit, north/south separation and world ID separation.");
        lines.Add("Inspected cells: " + checkedCells);
        Directory.CreateDirectory("Validation");
        File.WriteAllLines("Validation/spherical-vegetation-cells.txt", lines);
        Debug.Log("SPHERICAL_VEGETATION_CELLS_PASS " + checkedCells);
    }

    public static void RunSampling()
    {
        var world = FrontierGame.Instance?.world;
        Require(world != null, "RunSampling needs a generated live world.");
        var lines = new List<string>();
        CheckSampling(world, lines);
        Directory.CreateDirectory("Validation");
        File.WriteAllLines("Validation/spherical-vegetation-sampling.txt", lines);
        Debug.Log("SPHERICAL_VEGETATION_SAMPLING_PASS " + world.info.id);
    }

    static void CheckSampling(FrontierWorld world, List<string> lines)
    {
        Require(world.info.biome == "temperate", "Sampling acceptance needs a temperate planet.");
        int valid = 0, wet = 0, southValid = 0;
        float minMoisture = 1, maxMoisture = 0;
        var climate = new PlanetRegionClimate(world.info.id);
        for (int i = 0; i < 512; i++)
        {
            // Fibonacci sphere probes avoid favouring the source atlas axes.
            float y = 1 - 2 * (i + .5f) / 512;
            float a = i * 2.39996323f, r = Mathf.Sqrt(1 - y * y);
            Vector3 d = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            var s = world.SampleVegetation(d);
            var repeat = world.SampleVegetation(d * 7);
            Require(Finite(s.position) && Finite(s.normal) && Finite(s.up), "Sampler produced a non-finite frame.");
            Require(Vector3.Dot((s.position - world.PlanetCenter).normalized, d) > .999999f && Vector3.Dot(s.up, d) > .999999f, "Sampler confused radial up with surface position or slope normal.");
            Require(Mathf.Abs(s.normal.magnitude - 1) < .0001f && Vector3.Dot(s.normal, s.up) > 0, "Surface normal is invalid or points inward.");
            Require((s.position - repeat.position).magnitude < .015f && s.valid == repeat.valid, "Normalizing a direction changed its surface or habitat.");
            foreach (float value in new[] { s.moisture, s.temperature, s.exposure, s.forestCover })
                Require(float.IsFinite(value) && value >= 0 && value <= 1, "Habitat channel is not finite and normalized.");
            Require(Mathf.Abs(s.moisture - climate.Sample(d).x) < .0001f, "Typed sampler changed the atlas moisture channel or applied the source-frame transform twice.");
            Require(float.IsFinite(s.grassDensity) && s.grassDensity > 0 && float.IsFinite(s.grassHeight) && s.grassHeight > 0 && float.IsFinite(s.grassWidth) && s.grassWidth > 0, "Source grass profile is missing.");
            Require(s.plantVariant >= 0 && s.plantVariant <= 2, "Source plant variant is out of range.");
            if (s.valid)
            {
                Require(world.CanRootVegetation(s.position), "Valid habitat is submerged or within shore clearance.");
                Require(s.moisture >= .04f && Vector3.Dot(s.normal, s.up) > .55f, "Valid habitat violates moisture or slope eligibility.");
                valid++; if (d.y < 0) southValid++;
            }
            if (!world.CanRootVegetation(s.position)) { wet++; Require(!s.valid, "Wet sample is marked eligible."); }
            minMoisture = Mathf.Min(minMoisture, s.moisture); maxMoisture = Mathf.Max(maxMoisture, s.moisture);
        }
        Require(valid > 0 && southValid > 0, "No eligible far-hemisphere habitat exists.");
        Require(maxMoisture - minMoisture > .025f, "Global habitat appears clamped to one port value.");
        Require(!world.SampleVegetation(Vector3.zero).valid, "Zero direction accepted as vegetation habitat.");
        var n = world.SampleVegetation(new Vector3(.14f, .98f, .08f));
        var samesXZ = world.SampleVegetation(new Vector3(.14f, -.98f, .08f));
        Require(n.position.y > world.PlanetCenter.y && samesXZ.position.y < world.PlanetCenter.y && Vector3.Distance(n.position, samesXZ.position) > Radius,
            "Surface samples at equal x/z directions reused the northern square root.");
        lines.Add($"PASS: {world.info.id}: 512 whole-sphere radial/normal/habitat probes; eligible {valid}, southern eligible {southValid}, wet rejected {wet}; moisture {minMoisture:F3}–{maxMoisture:F3}.");
    }

    static VegetationSample FindSite(FrontierWorld world, bool south)
    {
        float best = float.NegativeInfinity;
        VegetationSample chosen = default;
        for (int band = 0; band < 5; band++)
        for (int longitude = 0; longitude < 240; longitude++)
        {
            float y = south ? -.45f - band * .105f : -.14f + band * .07f;
            float phase = longitude * Mathf.PI / 120;
            float r = Mathf.Sqrt(1 - y * y);
            var s = world.SampleVegetation(new Vector3(r * Mathf.Cos(phase), y, r * Mathf.Sin(phase)));
            if (!s.valid || Vector3.Dot(s.normal, s.up) < .94f || world.ElevationAboveSea(s.position) < 3) continue;
            float score = s.moisture + s.forestCover * .8f - s.exposure * .2f;
            if (score <= best) continue;
            Vector3 tangent = Vector3.Cross(s.up, Vector3.up).normalized;
            Vector3 forward = Vector3.Cross(s.up, tangent);
            int dry = 0;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4;
                var neighbour = world.SampleVegetation((s.up * Radius + (tangent * Mathf.Cos(a) + forward * Mathf.Sin(a)) * 95).normalized);
                if (neighbour.valid && Vector3.Dot(neighbour.normal, neighbour.up) > .8f) dry++;
            }
            if (dry < 7) continue;
            best = score; chosen = s;
        }
        Require(chosen.valid, "No broad dry " + (south ? "southern" : "equatorial") + " site found on " + world.info.id);
        Require(new Vector2(chosen.position.x, chosen.position.z).magnitude > 2700, "Chosen site did not cross the old 2700 m cap.");
        return chosen;
    }

    static VegetationSample FindSeamSite(FrontierWorld world, out Vector3 across)
    {
        VegetationSample best = default;
        across = Vector3.zero;
        float score = float.NegativeInfinity;
        for (int axis = 0; axis < 3; axis++)
        for (int a = -1; a <= 1; a += 2)
        for (int b = -1; b <= 1; b += 2)
        for (int step = 0; step <= 80; step++)
        {
            float third = -.95f + step * 1.9f / 80;
            Vector3 d = axis == 0 ? new Vector3(third, a, b) : axis == 1 ? new Vector3(a, third, b) : new Vector3(a, b, third);
            Vector3 cross = axis == 0 ? new Vector3(0, a, -b) : axis == 1 ? new Vector3(a, 0, -b) : new Vector3(a, -b, 0);
            var s = world.SampleVegetation(d.normalized);
            if (!s.valid || Vector3.Dot(s.normal, s.up) < .9f || s.moisture < .12f) continue;
            var left = world.SampleVegetation((s.up * Radius - cross.normalized * 35).normalized);
            var right = world.SampleVegetation((s.up * Radius + cross.normalized * 35).normalized);
            if (!left.valid || !right.valid) continue;
            float candidate = s.moisture + s.forestCover;
            if (candidate <= score) continue;
            best = s; across = cross.normalized; score = candidate;
        }
        Require(best.valid, "No dry cube-face seam found for a runtime crossing on " + world.info.id);
        return best;
    }

    static T Layer<T>(FrontierWorld world) where T : Component
        => world.GetComponentsInChildren<T>(true).FirstOrDefault(c => c.gameObject.activeInHierarchy);

    static void Focus(FrontierGame game, VegetationSample site, float height = 3, bool stream = true)
    {
        var world = game.world;
        Vector3 eye = world.transform.TransformPoint(site.position + site.up * height);
        Vector3 up = world.transform.TransformDirection(site.up);
        Vector3 forward = Vector3.ProjectOnPlane(world.transform.forward, up).normalized;
        if (forward.sqrMagnitude < .1f) forward = Vector3.ProjectOnPlane(world.transform.right, up).normalized;
        game.view.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(forward, up));
        if (stream) world.StreamSurface(eye, true);
        Physics.SyncTransforms();
        world.grass.UpdateAround(eye);
        Layer<WorldForestLayer>(world)?.UpdateAround(eye);
        Layer<GaussianVegetationLayer>(world)?.UpdateAround(eye);
    }

    static IEnumerator Settle(FrontierGame game, VegetationSample site)
    {
        double deadline = EditorApplication.timeSinceStartup + 90;
        int stable = 0;
        while (stable < 4)
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Streaming did not settle within 90 s on " + game.world.info.id);
            Focus(game, site);
            var forest = Layer<WorldForestLayer>(game.world);
            var splats = Layer<GaussianVegetationLayer>(game.world);
            Require(forest && splats, "One of the vegetation layers is absent.");
            stable = !forest.IsBuilding && !splats.IsBuilding && game.world.grass.ResidentTiles == 9 ? stable + 1 : 0;
            yield return null;
        }
    }

    static void CheckRendered(FrontierGame game, VegetationSample focus, string label, List<string> lines)
    {
        var world = game.world;
        var grass = world.grass;
        var forest = Layer<WorldForestLayer>(world);
        var splats = Layer<GaussianVegetationLayer>(world);
        Require(grass.ResidentTiles > 0 && grass.ResidentTiles <= 9 && grass.BladeCount > 0 && grass.BladeCount <= 9 * 12000, label + ": grass population is empty or unbounded.");
        Require(forest.TreeCount > 0 && forest.TreeCount < 10000, label + ": tree population is empty or unbounded.");
        Require(splats.MeadowCount > 0 && splats.WoodlandCount > 0 && splats.MeadowCount <= 60000 && splats.WoodlandCount <= 150000, label + ": splat population is empty or exceeds its 60k/150k limits.");
        Require(splats.VisibleMeadowCount >= 0 && splats.VisibleMeadowCount <= splats.MeadowCount && splats.VisibleWoodlandCount >= 0 && splats.VisibleWoodlandCount <= splats.WoodlandCount,
            label + ": visible splat lists exceed resident data.");
        Require(splats.VisibleMeadowCount + splats.VisibleWoodlandCount > 0, label + ": no committed splats reached the camera's visible lists.");
        int inspectedBlades = 0, inspectedTrees = 0, inspectedSplats = 0, actualBlades = 0;
        float worstCollider = 0, worstGrassSurface = 0, minimumOutward = 1;
        void CheckRoot(Vector3 root, Vector3 tipDirection, bool collider)
        {
            Require(Finite(root) && Finite(tipDirection), label + ": non-finite rendered geometry.");
            var sample = world.SampleVegetation(root - world.PlanetCenter);
            float outward = Vector3.Dot(tipDirection.normalized, sample.up);
            minimumOutward = Mathf.Min(minimumOutward, outward);
            Require(outward > .5f, label + ": plant points sideways or inward.");
            Require(world.ElevationAboveSea(root) >= FrontierWorld.VegetationShoreClearance - .15f, label + ": rendered root is submerged.");
            float rootError = Mathf.Abs(Vector3.Dot(root - sample.position, sample.up));
            worstGrassSurface = Mathf.Max(worstGrassSurface, rootError);
            Require(rootError < 1.5f, label + ": root floats or sinks relative to sampled surface by " + rootError + " m.");
            if (!collider || Vector3.Distance(root, focus.position) > 115) return;
            Vector3 query = world.transform.TransformPoint(root + sample.up * 6);
            Require(world.TrySurface(query, out var surface, out _), label + ": no active terrain collider under rendered root.");
            float error = Mathf.Abs(Vector3.Dot(world.transform.TransformPoint(root) - surface, world.transform.TransformDirection(sample.up)));
            worstCollider = Mathf.Max(worstCollider, error);
            Require(error < 1.5f, label + ": rendered root disagrees with active collider by " + error + " m.");
        }
        foreach (var filter in grass.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = filter.sharedMesh;
            if (!mesh || mesh.vertexCount == 0) continue;
            var vertices = mesh.vertices;
            Require(vertices.Length % 12 == 0, "Diagnostic expects the original six-segment-pair grass blade topology.");
            int bladeCount = vertices.Length / 12;
            actualBlades += bladeCount;
            for (int blade = 0; blade < bladeCount; blade += Mathf.Max(1, bladeCount / 96))
            {
                int first = blade * 12;
                Vector3 root = world.transform.InverseTransformPoint(filter.transform.TransformPoint((vertices[first] + vertices[first + 1]) * .5f));
                Vector3 tip = world.transform.InverseTransformPoint(filter.transform.TransformPoint((vertices[first + 10] + vertices[first + 11]) * .5f));
                CheckRoot(root, tip - root, true); inspectedBlades++;
            }
            Require(mesh.bounds.size.magnitude < 250, label + ": grass tile mesh lost its bounded tangent frame.");
        }
        Require(actualBlades == grass.BladeCount, label + ": reported blade budget differs from actual resident meshes.");
        Transform current = Field(forest, "current") as Transform;
        Require(current && current.gameObject.activeInHierarchy, label + ": committed forest is hidden.");
        foreach (Transform tree in current)
        {
            Vector3 root = world.transform.InverseTransformPoint(tree.position);
            CheckRoot(root, world.transform.InverseTransformDirection(tree.up), true);
            Require(Vector3.Distance(root, focus.position) < 265, label + ": tree escaped the 240 m neighbourhood.");
            inspectedTrees++;
        }
        foreach (string fieldName in new[] { "meadow", "woodland" })
        {
            var list = (IList)Field(splats, fieldName);
            for (int i = 0; i < list.Count; i += Mathf.Max(1, list.Count / 256))
            {
                object point = list[i];
                Vector3 root = (Vector3)Field(point, "root"), center = (Vector3)Field(point, "position");
                Quaternion rotation = (Quaternion)Field(point, "rotation");
                Vector3 up = (root - world.PlanetCenter).normalized;
                CheckRoot(root, rotation * Vector3.up, false);
                Require(Vector3.Dot(center - root, up) >= -.1f, label + ": splat canopy center points below its root.");
                Require(Vector3.ProjectOnPlane(center - root, up).magnitude < 3, label + ": splat offsets use global +Y instead of radial up.");
                Require(Vector3.Distance(root, focus.position) < (fieldName == "meadow" ? 470 : 2080), label + ": splat escaped its source layer radius.");
                Require(!string.IsNullOrEmpty((string)Field(point, "cellId")), label + ": splat lacks a persistent candidate ID.");
                inspectedSplats++;
            }
        }
        Require(inspectedBlades > 0 && inspectedTrees > 0 && inspectedSplats > 0, label + ": no actual generated geometry was inspected.");
        Require(inspectedTrees == forest.TreeCount, label + ": reported tree count differs from the committed hierarchy.");
        // Inspect the matrices actually handed to the instanced draw, including
        // the planet transform, rather than only trusting stored orientations.
        splats.UpdateAround(game.view.transform.position);
        var visible = (IList)Field(splats, splats.VisibleWoodlandCount > 0 ? "visibleWoodland" : "visibleMeadow");
        var matrixField = typeof(GaussianVegetationLayer).GetField("Matrices", BindingFlags.Static | BindingFlags.NonPublic);
        Require(matrixField != null && visible.Count > 0, label + ": no instance matrix diagnostic available.");
        var matrices = (Matrix4x4[])matrixField.GetValue(null);
        int lastBatch = (visible.Count - 1) / 1023 * 1023;
        for (int i = 0; i < visible.Count - lastBatch; i += 17)
        {
            var point = visible[lastBatch + i];
            Vector3 center = (Vector3)Field(point, "position");
            Quaternion localRotation = (Quaternion)Field(point, "rotation");
            Vector3 drawnCenter = matrices[i].MultiplyPoint3x4(Vector3.zero);
            Vector3 drawnUp = matrices[i].MultiplyVector(Vector3.up).normalized;
            Require((drawnCenter - world.transform.TransformPoint(center)).magnitude < .025f && Vector3.Dot(drawnUp, world.transform.TransformDirection(localRotation * Vector3.up).normalized) > .9999f,
                label + ": Gaussian draw matrix lost its local frame or planet transform.");
        }
        lines.Add($"PASS: {label}: blades {grass.BladeCount}/{grass.ResidentTiles} tiles; trees {forest.TreeCount}; meadow {splats.MeadowCount} ({splats.VisibleMeadowCount} visible); woodland {splats.WoodlandCount} ({splats.VisibleWoodlandCount} visible). Inspected {inspectedBlades} blade roots, {inspectedTrees} tree roots and {inspectedSplats} splats plus actual instanced draw matrices; worst sampler error {worstGrassSurface:F3} m, active collider error {worstCollider:F3} m, minimum outward dot {minimumOutward:F3}.");
    }

    static string Fingerprint(FrontierWorld world)
    {
        // Compare actual generated blade centers, not the PRNG implementation.
        var rows = new List<string>();
        foreach (var filter in world.grass.GetComponentsInChildren<MeshFilter>())
        {
            var vertices = filter.sharedMesh.vertices;
            for (int i = 0; i + 1 < vertices.Length; i += 12)
            {
                Vector3 p = world.transform.InverseTransformPoint(filter.transform.TransformPoint((vertices[i] + vertices[i + 1]) * .5f));
                rows.Add(Mathf.RoundToInt(p.x * 100) + "," + Mathf.RoundToInt(p.y * 100) + "," + Mathf.RoundToInt(p.z * 100));
            }
        }
        rows.Sort(StringComparer.Ordinal);
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (string row in rows) foreach (char c in row) { hash ^= c; hash *= 1099511628211UL; }
            return rows.Count + ":" + hash.ToString("X16");
        }
    }

    static Dictionary<string, string> TileFingerprints(FrontierWorld world)
    {
        var result = new Dictionary<string, string>();
        foreach (var filter in world.grass.GetComponentsInChildren<MeshFilter>())
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                foreach (int value in new[] { Mathf.RoundToInt(vertex.x * 1000), Mathf.RoundToInt(vertex.y * 1000), Mathf.RoundToInt(vertex.z * 1000) })
                { hash ^= (uint)value; hash *= 1099511628211UL; }
                result.Add(filter.name, filter.sharedMesh.vertexCount + ":" + hash.ToString("X16"));
            }
        }
        return result;
    }

    [MenuItem("Space Patriot/Validation/Begin spherical vegetation runtime")]
    public static void BeginRuntime()
    {
        Require(running == null, "A runtime pass is already running.");
        var game = FrontierGame.Instance;
        Require(Application.isPlaying && !EditorApplication.isPaused && game && game.world && game.view, "Enter unpaused Play mode before BeginRuntime.");
        Require((game.world.transform.lossyScale - Vector3.one).sqrMagnitude < .000001f, "This acceptance pass assumes unit planet scale.");
        runtimeLines = new List<string> { "RUNNING: spherical vegetation acceptance; " + DateTime.UtcNow.ToString("O") };
        var oldSave = game.save;
        string oldSettlement = oldSave.settlement;
        var oldInfo = game.world.info;
        bool oldEnabled = game.enabled;
        Vector3 oldWorldPosition = game.world.transform.position, oldCameraPosition = game.view.transform.position;
        Quaternion oldWorldRotation = game.world.transform.rotation, oldCameraRotation = game.view.transform.rotation;
        game.save = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(oldSave));
        game.enabled = false;
        restore = () =>
        {
            if (!game) return;
            game.save = oldSave;
            if (game.world)
            {
                game.world.transform.SetPositionAndRotation(oldWorldPosition, oldWorldRotation);
                if (Application.isPlaying) game.world.Generate(oldInfo);
            }
            oldSave.settlement = oldSettlement;
            if (game.view) game.view.transform.SetPositionAndRotation(oldCameraPosition, oldCameraRotation);
            game.enabled = oldEnabled;
            Physics.SyncTransforms();
        };
        running = Runtime(game);
        previousFrame = -1;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayState;
        Directory.CreateDirectory("Validation");
        File.WriteAllLines(ReportPath, runtimeLines);
        Debug.Log("SPHERICAL_VEGETATION_RUNTIME_STARTED; inspect " + ReportPath + " for completion.");
    }

    static IEnumerator Runtime(FrontierGame game)
    {
        RunCells();
        foreach (string worldId in new[] { "earth", "trappist-1-e" })
        {
            var info = Array.Find(game.worlds, w => w.id == worldId);
            Require(info != null, "World catalog lacks " + worldId);
            int revision = game.world.VegetationRevision;
            game.save.world = Array.IndexOf(game.worlds, info);
            game.world.Generate(info);
            Require(game.world.VegetationRevision != revision, "Generate did not invalidate old vegetation work.");
            CheckSampling(game.world, runtimeLines);
            var side = FindSite(game.world, false);
            var south = FindSite(game.world, true);
            runtimeLines.Add("SITE: " + worldId + " equatorial root=" + V(side.position) + " up=" + V(side.up));
            runtimeLines.Add("SITE: " + worldId + " southern root=" + V(south.position) + " up=" + V(south.up));
            Focus(game, side);
            var settle = Settle(game, side); while (settle.MoveNext()) yield return null;
            CheckRendered(game, side, worldId + " equatorial", runtimeLines);
            string before = Fingerprint(game.world);

            // Move far enough to evict all near cells, then revisit their roots.
            Focus(game, south);
            settle = Settle(game, south); while (settle.MoveNext()) yield return null;
            CheckRendered(game, south, worldId + " southern", runtimeLines);
            Focus(game, side);
            settle = Settle(game, side); while (settle.MoveNext()) yield return null;
            Require(before == Fingerprint(game.world), "Eviction/revisit changed actual generated grass roots on " + worldId);
            runtimeLines.Add("PASS: " + worldId + " opposite-hemisphere teleport and actual blade-root fingerprint revisit " + before);

            if (worldId == "earth")
            {
                var seam = FindSeamSite(game.world, out var across);
                var left = game.world.SampleVegetation((seam.up * Radius - across * 15).normalized);
                var right = game.world.SampleVegetation((seam.up * Radius + across * 15).normalized);
                Focus(game, left);
                settle = Settle(game, left); while (settle.MoveNext()) yield return null;
                var leftTiles = TileFingerprints(game.world);
                Focus(game, right);
                settle = Settle(game, right); while (settle.MoveNext()) yield return null;
                var rightTiles = TileFingerprints(game.world);
                var shared = leftTiles.Keys.Intersect(rightTiles.Keys).ToArray();
                Require(shared.Length >= 2, "Runtime seam crossing discarded all overlapping resident cells.");
                Require(shared.All(id => leftTiles[id] == rightTiles[id]), "Overlapping blade geometry changed across a cube-face seam.");
                var oldForest = Layer<WorldForestLayer>(game.world);
                var oldSplats = Layer<GaussianVegetationLayer>(game.world);
                Transform committed = Field(oldForest, "current") as Transform;
                int oldMeadow = oldSplats.MeadowCount, oldWoodland = oldSplats.WoodlandCount;
                // A 280 m move exceeds both rebuild distances while retaining
                // a nearby, still meaningful old neighbourhood during the build.
                var rebase = game.world.SampleVegetation((right.up * Radius + across * 280).normalized);
                Focus(game, rebase);
                Require(oldForest.IsBuilding && oldSplats.IsBuilding && committed && committed.gameObject.activeInHierarchy,
                    "Replacement generation hid the committed forest neighbourhood.");
                Require(oldSplats.MeadowCount == oldMeadow && oldSplats.WoodlandCount == oldWoodland,
                    "Replacement generation discarded committed splats before it was ready.");
                settle = Settle(game, rebase); while (settle.MoveNext()) yield return null;
                Focus(game, right);
                settle = Settle(game, right); while (settle.MoveNext()) yield return null;
                var revisited = TileFingerprints(game.world);
                Require(rightTiles.Count == revisited.Count && rightTiles.All(pair => revisited.TryGetValue(pair.Key, out var value) && value == pair.Value),
                    "Frame recenter and revisit changed cell-local blade geometry.");
                runtimeLines.Add("PASS: earth runtime cube seam " + V(seam.up) + "; " + shared.Length + " overlapping tiles retain exact mesh fingerprints; 280 m rebase retains committed forest/splats and revisits deterministically.");
                Focus(game, side);
                settle = Settle(game, side); while (settle.MoveNext()) yield return null;
            }

            Vector3 originalPosition = game.world.transform.position;
            Quaternion originalRotation = game.world.transform.rotation;
            game.world.transform.SetPositionAndRotation(new Vector3(812, -233, 477), Quaternion.Euler(23, 67, -31));
            Focus(game, side);
            Require(game.world.TryVegetationFocus(game.view.transform.position, out var transformed, out float altitude) && Mathf.Abs(altitude - 3) < .03f && (transformed.position - side.position).magnitude < .03f,
                "Rotated/translated planet mixed world eye with local surface coordinates.");
            yield return null;
            CheckRendered(game, side, worldId + " translated/rotated", runtimeLines);
            game.world.transform.SetPositionAndRotation(originalPosition, originalRotation);
            Focus(game, side);

            foreach (float height in new[] { 80f, 110f, 400f, 550f })
            {
                Focus(game, side, height, false); yield return null;
                bool grassVisible = game.world.grass.GetComponentsInChildren<MeshRenderer>().Any(r => r.enabled && r.gameObject.activeInHierarchy);
                var forest = Layer<WorldForestLayer>(game.world);
                var current = Field(forest, "current") as Transform;
                Require(grassVisible == (height < 100), "Near grass altitude gate differs from 100 m at " + height);
                Require(current && current.gameObject.activeInHierarchy == (height < 500), "Full tree altitude gate differs from 500 m at " + height);
            }
            // Fade is intentionally gradual. Verify its direction across 1800 m.
            var gaussian = Layer<GaussianVegetationLayer>(game.world);
            Focus(game, side, 1750, false);
            for (int frame = 0; frame < 8; frame++) yield return null;
            float lowFade = (float)Field(gaussian, "fade");
            Focus(game, side, 1850, false);
            for (int frame = 0; frame < 8; frame++) yield return null;
            float highFade = (float)Field(gaussian, "fade");
            Require(lowFade > 0 && highFade < lowFade, "Gaussian vegetation did not fade above its 1800 m altitude gate.");
            runtimeLines.Add("PASS: " + worldId + " grass/tree/splat altitude gates at 100/500/1800 m.");
            File.WriteAllLines(ReportPath, runtimeLines);
        }

        // Initiate new work and replace its world before it may commit. This
        // exercises real coroutines rather than invoking their enumerators by hand.
        var previousWorld = game.world;
        var staleForest = Layer<WorldForestLayer>(previousWorld);
        var staleSplats = Layer<GaussianVegetationLayer>(previousWorld);
        var newFocus = FindSite(previousWorld, true);
        Focus(game, newFocus);
        Require(staleForest.IsBuilding || staleSplats.IsBuilding, "World-switch scenario failed to start an asynchronous build.");
        int oldRevision = previousWorld.VegetationRevision;
        var earth = Array.Find(game.worlds, w => w.id == "earth");
        game.save.world = Array.IndexOf(game.worlds, earth);
        previousWorld.Generate(earth);
        Require(previousWorld.VegetationRevision != oldRevision, "World switch retained its vegetation revision.");
        Require(!staleForest.gameObject.activeInHierarchy && !staleSplats.gameObject.activeInHierarchy, "Old-world layers remained drawable after Generate.");
        var earthSite = FindSite(previousWorld, false);
        Focus(game, earthSite);
        var finalSettle = Settle(game, earthSite); while (finalSettle.MoveNext()) yield return null;
        Require(!staleForest && !staleSplats, "Old world retained pending layer objects after frame destruction.");
        CheckRendered(game, earthSite, "earth after mid-build world switch", runtimeLines);
        runtimeLines.Add("PASS: world generation changed during a build; stale layer objects were retired and replacement roots belong to the new world.");
        var gas = Array.Find(game.worlds, w => w.biome == "gas");
        Require(gas != null, "World catalog has no gas-world negative control.");
        game.save.world = Array.IndexOf(game.worlds, gas);
        game.world.Generate(gas);
        Require(!game.world.SampleVegetation(Vector3.right).valid && !game.world.SampleVegetation(Vector3.down).valid,
            "A gas giant accepts solid vegetation habitat.");
        Require(!Layer<WorldForestLayer>(game.world) && !Layer<GaussianVegetationLayer>(game.world) && game.world.grass.BladeCount == 0,
            "A gas giant generated a living vegetation layer.");
        runtimeLines.Add("PASS: " + gas.id + " gas-world negative control rejects habitat and living layers.");
        runtimeLines.Add("PASS: automated geometry acceptance completed. Visual captures and frame-time profiling remain separate evidence.");
    }

    static void Tick()
    {
        if (running == null || previousFrame == Time.frameCount) return;
        previousFrame = Time.frameCount;
        try
        {
            if (!Application.isPlaying) { Stop("Play mode ended before completion."); return; }
            if (running.MoveNext()) return;
            Finish(null);
        }
        catch (Exception e) { Finish(e); }
    }

    static void OnPlayState(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && running != null) Stop("Play mode exited before completion.");
    }

    [MenuItem("Space Patriot/Validation/Stop spherical vegetation runtime")]
    public static void Stop() => Stop("Stopped by operator before completion.");
    static void Stop(string reason) => Finish(new Exception(reason));

    static void Finish(Exception failure)
    {
        if (running == null) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayState;
        (running as IDisposable)?.Dispose();
        running = null;
        try { restore?.Invoke(); }
        catch (Exception e) { failure = failure == null ? e : new AggregateException(failure, e); }
        finally { restore = null; }
        runtimeLines.Add(failure == null ? "SPHERICAL_VEGETATION_RUNTIME_PASS" : "SPHERICAL_VEGETATION_RUNTIME_FAIL: " + failure);
        Directory.CreateDirectory("Validation");
        File.WriteAllLines(ReportPath, runtimeLines);
        if (failure == null) Debug.Log("SPHERICAL_VEGETATION_RUNTIME_PASS"); else Debug.LogError("SPHERICAL_VEGETATION_RUNTIME_FAIL: " + failure);
    }
}
