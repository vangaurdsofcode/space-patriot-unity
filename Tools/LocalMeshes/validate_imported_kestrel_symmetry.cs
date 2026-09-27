{
    string id = "kestrel-symmetric-mirror-20260926-r1";
    string path = "Assets/SpacePatriot/ArtLab/KestrelK017/" + id + "/Kestrel_K017.prefab";
    string output = "ArtDirection/Generated/KestrelK017/" + id + "/unity-mirror-runtime-validation.txt";
    var report = new System.Text.StringBuilder();
    UnityEngine.GameObject instance = null;
    try {
        instance = UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path));
        var filters = instance.GetComponentsInChildren<UnityEngine.MeshFilter>();
        var lods = instance.GetComponent<UnityEngine.LODGroup>().GetLODs();
        if (lods.Length != 3 || filters.Length != 24) throw new System.Exception("Expected 3 LODs / 24 meshes");
        for (int level = 0; level < 3; level++) {
            if (lods[level].renderers.Length != 8) throw new System.Exception("Missing LOD parts");
            var hull = System.Linq.Enumerable.Single(filters, f => f.name == "Hull_LOD" + level);
            var hullPoints = hull.sharedMesh.vertices;
            var hullNormals = hull.sharedMesh.normals;
            var hullBins = new System.Collections.Generic.Dictionary<UnityEngine.Vector3Int, System.Collections.Generic.List<int>>();
            float hullCell = .0001f;
            for (int i = 0; i < hullPoints.Length; i++) {
                hullPoints[i] = hull.transform.TransformPoint(hullPoints[i]);
                hullNormals[i] = hull.transform.worldToLocalMatrix.transpose.MultiplyVector(hullNormals[i]).normalized;
                var key = UnityEngine.Vector3Int.FloorToInt(hullPoints[i] / hullCell);
                if (!hullBins.ContainsKey(key)) hullBins[key] = new System.Collections.Generic.List<int>();
                hullBins[key].Add(i);
            }
            float hullMaxError = 0;
            for (int i = 0; i < hullPoints.Length; i++) {
                var point = hullPoints[i]; point.z = -point.z;
                var normal = hullNormals[i]; normal.z = -normal.z;
                var key = UnityEngine.Vector3Int.FloorToInt(point / hullCell);
                float best = float.PositiveInfinity;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++) {
                    System.Collections.Generic.List<int> indices;
                    if (!hullBins.TryGetValue(key + new UnityEngine.Vector3Int(x,y,z), out indices)) continue;
                    foreach (int j in indices) {
                        if (UnityEngine.Vector3.Dot(normal,hullNormals[j]) < .999f) continue;
                        best = UnityEngine.Mathf.Min(best, (point-hullPoints[j]).magnitude);
                    }
                }
                if (best > hullCell) throw new System.Exception(hull.name + " symmetry/normal failed at vertex " + i + ": " + best);
                hullMaxError = UnityEngine.Mathf.Max(hullMaxError, best);
            }
            report.AppendLine("PASS " + hull.name + ": " + hullPoints.Length + " imported vertices, symmetric shape/normals; maximum error " + hullMaxError.ToString("G9") + " m. Independent UV islands are deliberately not identical across the centerline.");
            var material = hull.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial;
            string suffix = level == 0 ? "" : "_LOD" + level;
            int expectedSize = level == 0 ? 4096 : level == 1 ? 1024 : 512;
            foreach (var channel in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap" }) {
                var texture = material.GetTexture(channel);
                var texturePath = UnityEditor.AssetDatabase.GetAssetPath(texture);
                if (texture == null || !texturePath.EndsWith(suffix + ".png") || texture.width != expectedSize)
                    throw new System.Exception("Wrong texture binding/size: " + hull.name + " " + channel + " " + texturePath);
                report.AppendLine(hull.name + " " + channel + " = " + texturePath + " (" + texture.width + "px)");
            }
            foreach (string module in new[] { "Wing", "Drive" }) {
                var source = System.Linq.Enumerable.Single(filters, f => f.name == module + "_Port_LOD" + level);
                var target = System.Linq.Enumerable.Single(filters, f => f.name == module + "_Starboard_LOD" + level);
                if (source.sharedMesh.vertexCount != target.sharedMesh.vertexCount) throw new System.Exception("Vertex count changed");
                if (source.transform.localToWorldMatrix.determinant <= 0 || target.transform.localToWorldMatrix.determinant <= 0)
                    throw new System.Exception("Negative instance determinant");
                var points = target.sharedMesh.vertices;
                var normals = target.sharedMesh.normals;
                var uvs = target.sharedMesh.uv;
                var bins = new System.Collections.Generic.Dictionary<UnityEngine.Vector3Int, System.Collections.Generic.List<int>>();
                float cell = .0001f;
                for (int i = 0; i < points.Length; i++) {
                    points[i] = target.transform.TransformPoint(points[i]);
                    normals[i] = target.transform.worldToLocalMatrix.transpose.MultiplyVector(normals[i]).normalized;
                    var key = UnityEngine.Vector3Int.FloorToInt(points[i] / cell);
                    if (!bins.ContainsKey(key)) bins[key] = new System.Collections.Generic.List<int>();
                    bins[key].Add(i);
                }
                var sourcePoints = source.sharedMesh.vertices;
                var sourceNormals = source.sharedMesh.normals;
                var sourceUvs = source.sharedMesh.uv;
                float maxError = 0;
                for (int i = 0; i < sourcePoints.Length; i++) {
                    var point = source.transform.TransformPoint(sourcePoints[i]); point.z = -point.z;
                    var normal = source.transform.worldToLocalMatrix.transpose.MultiplyVector(sourceNormals[i]).normalized; normal.z = -normal.z;
                    var key = UnityEngine.Vector3Int.FloorToInt(point / cell);
                    float best = float.PositiveInfinity;
                    for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++) {
                        System.Collections.Generic.List<int> indices;
                        if (!bins.TryGetValue(key + new UnityEngine.Vector3Int(x,y,z), out indices)) continue;
                        foreach (int j in indices) {
                            if ((sourceUvs[i] - uvs[j]).sqrMagnitude > 1e-8f || UnityEngine.Vector3.Dot(normal,normals[j]) < .999f) continue;
                            best = UnityEngine.Mathf.Min(best, (point-points[j]).magnitude);
                        }
                    }
                    if (best > cell) throw new System.Exception(source.name + " reflection/UV/normal failed at vertex " + i + ": " + best);
                    maxError = UnityEngine.Mathf.Max(maxError, best);
                }
                report.AppendLine("PASS " + module + " LOD" + level + ": " + sourcePoints.Length + " imported vertices, mirrored position/UV/normal; maximum position error " + maxError.ToString("G9") + " m.");
            }
        }
        UnityEngine.Object.DestroyImmediate(instance); instance = null;
        for (int level = 0; level < 3; level++) LocalShipImporter.CaptureBuild(id, level);
        System.IO.File.WriteAllText(output, "PASS: imported Unity prefab verified; 9 actual URP captures saved.\n" + report);
    } catch (System.Exception error) {
        System.IO.File.WriteAllText(output, "FAILED: " + error + "\n" + report);
        UnityEngine.Debug.LogException(error);
    } finally { if (instance != null) UnityEngine.Object.DestroyImmediate(instance); }
}
return "Finished imported-mesh verification and captures; read the saved report.";

