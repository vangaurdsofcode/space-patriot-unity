using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Imports the complete textured Kestrel as a review prefab in its own art lab.
public static class LocalShipImporter
{
    const string Root = "Assets/SpacePatriot/ArtLab/KestrelK017";
    const string ActiveBuild = "ArtDirection/Generated/KestrelK017/active-build.txt";

    [MenuItem("Space Patriot/Art lab/Import latest baked Kestrel")]
    public static void ImportLatest()
    {
        if (!File.Exists(ActiveBuild)) throw new FileNotFoundException("Build the Kestrel component kit first.", ActiveBuild);
        ImportBuild(File.ReadAllText(ActiveBuild).Trim());
    }

    public static void ImportBuild(string buildId)
    {
        ValidateBuildId(buildId);
        string source = "ArtDirection/Generated/KestrelK017/" + buildId;
        string savedValidationPath = source + "/saved-bake-validation.json";
        bool validatedSixView = File.Exists(savedValidationPath);
        if (validatedSixView)
        {
            var savedValidation = JsonUtility.FromJson<SavedBakeValidation>(File.ReadAllText(savedValidationPath));
            if (savedValidation == null || !savedValidation.passed)
                throw new InvalidDataException("Saved bake validation must pass before importing " + buildId + ".");
        }
        var report = JsonUtility.FromJson<BuildReport>(File.ReadAllText(source + "/assembly-report.json"));
        if (report == null || report.parts == null || report.parts.Length != 4) throw new InvalidDataException("Kestrel build report is incomplete.");
        bool validatedRepack = report.hull_uv0 == "AtlasUV_Packed_v3" || File.Exists(source + "/uv-repack-report.json");
        if (validatedRepack)
        {
            string path = source + "/saved-uv-repack-validation.json";
            if (!File.Exists(path)) throw new InvalidDataException("Final UVs require saved-file repack validation: " + buildId);
            var validation = JsonUtility.FromJson<SavedUvRepackValidation>(File.ReadAllText(path));
            if (validation == null || !validation.passed || !validation.saved_files_reopened ||
                validation.validated_hull_lods != 3 || validation.textures == null || validation.textures.Length != 12)
                throw new InvalidDataException("All three final hull LODs must pass independent saved UV validation: " + buildId);
            CheckFileHash(source + "/Kestrel_K017.fbx", validation.fbx_sha256);
            for (int level = 0; level < 3; level++)
            foreach (string channel in new[] { "Kestrel_BaseColor", "Kestrel_Normal", "Kestrel_MetallicSmoothness", "Kestrel_Roughness_source" })
            {
                string name = channel + (level == 0 ? "" : "_LOD" + level) + ".png";
                var texture = validation.textures.SingleOrDefault(t => t.file == name);
                if (texture == null) throw new InvalidDataException("UV validation is missing " + name);
                CheckFileHash(source + "/Textures/" + name, texture.sha256);
            }
        }

        string destination = Root + "/" + buildId;
        Directory.CreateDirectory(destination + "/Textures");
        Copy(source + "/Kestrel_K017.fbx", destination + "/Kestrel_K017.fbx");
        foreach (string name in new[] { "Kestrel_BaseColor.png", "Kestrel_Normal.png", "Kestrel_MetallicSmoothness.png" })
            Copy(source + "/Textures/" + name, destination + "/Textures/" + name);
        AssetDatabase.ImportAsset(destination + "/Textures/Kestrel_BaseColor.png", ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(destination + "/Textures/Kestrel_Normal.png", ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(destination + "/Textures/Kestrel_MetallicSmoothness.png", ImportAssetOptions.ForceSynchronousImport);

        var baseImporter = (TextureImporter)AssetImporter.GetAtPath(destination + "/Textures/Kestrel_BaseColor.png");
        baseImporter.sRGBTexture = true; baseImporter.maxTextureSize = 4096; baseImporter.textureCompression = TextureImporterCompression.CompressedHQ; baseImporter.SaveAndReimport();
        var normalImporter = (TextureImporter)AssetImporter.GetAtPath(destination + "/Textures/Kestrel_Normal.png");
        normalImporter.textureType = TextureImporterType.NormalMap; normalImporter.maxTextureSize = 4096; normalImporter.textureCompression = TextureImporterCompression.CompressedHQ; normalImporter.SaveAndReimport();
        var metalImporter = (TextureImporter)AssetImporter.GetAtPath(destination + "/Textures/Kestrel_MetallicSmoothness.png");
        metalImporter.sRGBTexture = false; metalImporter.maxTextureSize = 4096; metalImporter.textureCompression = TextureImporterCompression.CompressedHQ; metalImporter.SaveAndReimport();

        string fbxPath = destination + "/Kestrel_K017.fbx";
        AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceSynchronousImport);
        var modelImporter = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        modelImporter.materialImportMode = ModelImporterMaterialImportMode.None;
        modelImporter.importAnimation = false;
        modelImporter.importNormals = ModelImporterNormals.Import;
        modelImporter.bakeAxisConversion = true;
        modelImporter.meshCompression = ModelImporterMeshCompression.Off;
        modelImporter.generateSecondaryUV = false;
        modelImporter.SaveAndReimport();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (model == null) throw new InvalidDataException("Unity did not import the Kestrel FBX.");
        var hullUvReports = ValidateHullUvs(model, validatedSixView || validatedRepack);

        var materialPath = destination + "/Kestrel_BakedAtlas.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.name = "Kestrel / Atlas review material";
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(destination + "/Textures/Kestrel_BaseColor.png"));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(destination + "/Textures/Kestrel_Normal.png"));
        material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(destination + "/Textures/Kestrel_MetallicSmoothness.png"));
        material.SetFloat("_Metallic", .45f); material.SetFloat("_Smoothness", 1f); material.SetFloat("_BumpScale", .8f);
        material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP");
        EditorUtility.SetDirty(material);
        var hullMaterials = new[] { material, material, material };
        if (validatedRepack)
            for (int level = 1; level < 3; level++)
                hullMaterials[level] = ImportHullLodMaterial(source, destination, material, level);

        var root = new GameObject("Kestrel K-017 / baked local build");
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.SetParent(root.transform, false);
            // The FBX exporter emits Blender Y-up coordinates in the file's
            // Z-up basis. Unity imports that basis with Y/Z exchanged; this
            // local correction restores +Y height and Z port/starboard span.
            instance.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            foreach (var importedLod in instance.GetComponentsInChildren<LODGroup>(true))
                Object.DestroyImmediate(importedLod);
            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length < 18) throw new InvalidDataException("The assembled FBX is missing parts or LODs: " + renderers.Length);
            foreach (var renderer in renderers)
                renderer.sharedMaterial = validatedRepack && renderer.name.StartsWith("Hull_LOD", StringComparison.Ordinal)
                    ? hullMaterials[int.Parse(renderer.name.Substring("Hull_LOD".Length))] : material;
            var lods = new LOD[3];
            for (int level = 0; level < lods.Length; level++)
            {
                var levelRenderers = renderers.Where(r => r.name.EndsWith("_LOD" + level, StringComparison.Ordinal)).Cast<Renderer>().ToArray();
                if (levelRenderers.Length != 8) throw new InvalidDataException("LOD" + level + " has " + levelRenderers.Length + " parts; expected hull, two wings, two drives and three legs.");
                lods[level] = new LOD(new[] { .42f, .12f, .025f }[level], levelRenderers);
            }
            var group = root.AddComponent<LODGroup>(); group.SetLODs(lods); group.RecalculateBounds();
            Bounds bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            if (bounds.size.x < 17 || bounds.size.z < 10) throw new InvalidDataException("Assembled ship extents are too small: " + bounds.size);
            PrefabUtility.SaveAsPrefabAsset(root, destination + "/Kestrel_K017.prefab");
            AssetDatabase.SaveAssetIfDirty(material);
            string normalStatus = validatedSixView || validatedRepack
                ? "Hull tangent-normal chart is neutral after the UV reset; no new hull high-to-low normal bake is claimed. Other parts retain their supplied normal atlas."
                : "Normal atlas imported as supplied; high-to-low detail correctness was not checked by this importer.";
            string importSummary =
                "PASS: complete locally generated Kestrel FBX imported.\nBuild: " + buildId + "\nRenderers: " + renderers.Length + " across three LOD levels.\n" +
                "Bounds: " + bounds.size + " metres.\n" + normalStatus + "\n" +
                string.Join("\n", hullUvReports.Select(uv => uv.mesh + ": " + uv.uv0Count + " UV0 coordinates, " + uv.uv1Count + " UV1 coordinates.")) +
                "\nPrefab remains in the ArtLab; active-build.txt and runtime ships were not changed.\n";
            Directory.CreateDirectory("Validation");
            File.WriteAllText("Validation/kestrel-textured-ship-import-" + buildId + ".txt", importSummary);
            File.WriteAllText(source + "/unity-import-validation.json", JsonUtility.ToJson(new ImportReviewReport {
                build=buildId, passed=true, savedBakeValidationPresent=validatedSixView, postBakeUvValidationPresent=validatedRepack,
                renderers=renderers.Length, bounds=bounds.size, hullUvs=hullUvReports,
                normalStatus=normalStatus, prefab=destination+"/Kestrel_K017.prefab", runtimePromoted=false
            }, true));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [MenuItem("Space Patriot/Art lab/Capture latest baked Kestrel")]
    public static void CaptureLatest()
    {
        if (!File.Exists(ActiveBuild)) throw new FileNotFoundException("No active Kestrel build.", ActiveBuild);
        CaptureBuild(File.ReadAllText(ActiveBuild).Trim());
    }

    public static void CaptureBuild(string id, int forcedLod = 0)
    {
        ValidateBuildId(id);
        if (forcedLod < 0 || forcedLod > 2) throw new ArgumentOutOfRangeException(nameof(forcedLod));
        string prefabPath = Root + "/" + id + "/Kestrel_K017.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) throw new InvalidOperationException("Import Kestrel build " + id + " before capturing it.");
        Directory.CreateDirectory("ArtDirection/Generated/KestrelK017/" + id + "/Renders");
        var stage = new GameObject("Kestrel art review stage"); stage.transform.position = new Vector3(50000, 8000, 0);
        var ship = Object.Instantiate(prefab, stage.transform);
        foreach (var t in ship.GetComponentsInChildren<Transform>()) t.gameObject.layer = 30;
        ship.GetComponent<LODGroup>().ForceLOD(forcedLod);
        var camera = new GameObject("Kestrel review camera").AddComponent<Camera>();
        camera.transform.SetParent(stage.transform, false); camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.48f, .53f, .56f); camera.nearClipPlane = .1f; camera.farClipPlane = 500;
        camera.orthographic = false; camera.fieldOfView = 42;
        var light = new GameObject("Kestrel review key").AddComponent<Light>();
        light.transform.SetParent(stage.transform, false); light.type = LightType.Directional; light.intensity = 1.7f;
        light.transform.localRotation = Quaternion.Euler(35, -28, 0); light.cullingMask = 1 << 30;
        Bounds b = ship.GetComponent<LODGroup>().GetLODs()[0].renderers[0].bounds;
        foreach (var r in ship.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        bool fog = RenderSettings.fog; RenderSettings.fog = false;
        var rt = new RenderTexture(1500, 1050, 24); var previous = RenderTexture.active;
        try
        {
            var views = new[] { new Vector3(.22f, .65f, -1.0f), new Vector3(0f, .28f, -1.7f), new Vector3(0f, 1.4f, 0f) };
            for (int i = 0; i < views.Length; i++)
            {
                float distanceFactor = forcedLod == 0 ? 1.65f : forcedLod == 1 ? 4.2f : 12f;
                camera.transform.position = b.center + views[i].normalized * Mathf.Max(b.size.x, b.size.z) * distanceFactor;
                camera.transform.LookAt(b.center, i == 2 ? Vector3.forward : Vector3.up); camera.targetTexture = rt;
                RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt; var image = new Texture2D(1500, 1050, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1500, 1050), 0, 0); image.Apply();
                string source = "ArtDirection/Generated/KestrelK017/" + id + "/Renders/unity-" + (forcedLod == 0 ? "" : "lod" + forcedLod + "-") + i + ".png";
                File.WriteAllBytes(source, image.EncodeToPNG()); Object.DestroyImmediate(image);
            }
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(rt);
            RenderSettings.fog = fog; Object.DestroyImmediate(stage);
        }
    }

    static void Copy(string source, string destination)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("Required baked ship asset was not produced.", source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(source, destination, true);
    }

    static void ValidateBuildId(string id)
    {
        if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,64}$"))
            throw new InvalidDataException("Invalid Kestrel build id.");
    }

    static HullUvReport[] ValidateHullUvs(GameObject model, bool requireSingleLayout)
    {
        var filters = model.GetComponentsInChildren<MeshFilter>(true);
        var result = new HullUvReport[3];
        for (int level = 0; level < result.Length; level++)
        {
            string name = "Hull_LOD" + level;
            var hull = filters.SingleOrDefault(f => f.name == name);
            if (hull == null || hull.sharedMesh == null) throw new InvalidDataException("Imported model is missing " + name + ".");
            var mesh = hull.sharedMesh;
            Vector2[] uv = mesh.uv, uv1 = mesh.uv2;
            if (uv.Length != mesh.vertexCount || uv.Length == 0)
                throw new InvalidDataException(name + " is missing complete UV0 coordinates.");
            var low = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var high = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var coordinate in uv)
            {
                if (!float.IsFinite(coordinate.x) || !float.IsFinite(coordinate.y))
                    throw new InvalidDataException(name + " has non-finite UV0 coordinates.");
                low = Vector2.Min(low, coordinate); high = Vector2.Max(high, coordinate);
            }
            if ((high-low).x <= 0 || (high-low).y <= 0)
                throw new InvalidDataException(name + " has a collapsed UV0 layout.");
            if (requireSingleLayout && (uv1.Length != 0 || mesh.HasVertexAttribute(VertexAttribute.TexCoord1)))
                throw new InvalidDataException(name + " still contains a stale UV1 layout after the six-view reset.");
            if (requireSingleLayout && (low.x < 0 || high.x > .50001f || low.y < .49999f || high.y > 1.00001f))
                throw new InvalidDataException(name + " UV0 lies outside the reserved hull atlas quadrant.");
            result[level] = new HullUvReport { mesh=name, vertices=mesh.vertexCount, uv0Count=uv.Length, uv1Count=uv1.Length, minimum=low, maximum=high };
        }
        return result;
    }

    static void CheckFileHash(string path, string expected)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        string actual = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Final asset changed after UV validation: " + path);
    }

    static Material ImportHullLodMaterial(string source, string destination, Material basis, int level)
    {
        string suffix = "_LOD" + level;
        string[] channels = { "Kestrel_BaseColor", "Kestrel_Normal", "Kestrel_MetallicSmoothness" };
        string[] properties = { "_BaseMap", "_BumpMap", "_MetallicGlossMap" };
        string materialPath = destination + "/Kestrel_Hull" + suffix + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(basis);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.CopyPropertiesFromMaterial(basis);
        material.name = "Kestrel / independently baked hull " + suffix;
        for (int channel = 0; channel < channels.Length; channel++)
        {
            string filename = channels[channel] + suffix + ".png";
            string texturePath = destination + "/Textures/" + filename;
            Copy(source + "/Textures/" + filename, texturePath);
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.textureType = channel == 1 ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = channel == 0;
            importer.maxTextureSize = level == 1 ? 1024 : 512;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            material.SetTexture(properties[channel], AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        }
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    [Serializable] class BuildReport { public string ship, hull_uv0; public PartReport[] parts; }
    [Serializable] class PartReport { public string part; }
    [Serializable] class SavedBakeValidation { public bool passed; }
    [Serializable] class SavedUvRepackValidation { public bool passed, saved_files_reopened; public int validated_hull_lods; public string fbx_sha256; public ValidatedTexture[] textures; }
    [Serializable] class ValidatedTexture { public string file, sha256; }
    [Serializable] class HullUvReport { public string mesh; public int vertices, uv0Count, uv1Count; public Vector2 minimum, maximum; }
    [Serializable] class ImportReviewReport
    {
        public string build, prefab, normalStatus;
        public bool passed, savedBakeValidationPresent, postBakeUvValidationPresent, runtimePromoted;
        public int renderers;
        public Vector3 bounds;
        public HullUvReport[] hullUvs;
    }
}
