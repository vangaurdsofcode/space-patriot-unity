using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using SpacePatriot;

// A reviewed ArtLab FBX becomes a selectable runtime vessel only after the
// saved UV report, imported Unity prefab, materials, and measured axes agree.
public static class AuthoredShipRegistration
{
    const string Generated = "ArtDirection/Generated/KestrelK017/";
    const string ArtLab = "Assets/SpacePatriot/ArtLab/KestrelK017/";
    const string ResourceRoot = "Assets/SpacePatriot/Resources/AuthoredShips/KestrelK017/";
    const string CatalogPath = "Assets/SpacePatriot/Resources/AuthoredShipVisuals.json";

    [Serializable] class SavedValidation
    {
        public bool passed, saved_files_reopened;
        public int validated_hull_lods;
        public string fbx_sha256;
        public TextureHash[] textures;
    }
    [Serializable] class TextureHash { public string file, sha256; }
    [Serializable] class ImportReport
    {
        public bool passed, postBakeUvValidationPresent;
        public int renderers;
        public string prefab;
        public Vector3 bounds;
    }

    [MenuItem("Space Patriot/Art lab/Register verified Kestrel prototype")]
    public static void RegisterCurrentKestrel()
        => RegisterBuild("kestrel-symmetric-mirror-20260926-r1");

    public static void RegisterBuild(string buildId)
    {
        if (string.IsNullOrEmpty(buildId) || buildId.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("Invalid authored ship build id.");
        string source = Generated + buildId;
        string validationPath = source + "/saved-uv-repack-validation.json";
        string importPath = source + "/unity-import-validation.json";
        var validation = JsonUtility.FromJson<SavedValidation>(File.ReadAllText(validationPath));
        var imported = JsonUtility.FromJson<ImportReport>(File.ReadAllText(importPath));
        if (validation == null || !validation.passed || !validation.saved_files_reopened ||
            validation.validated_hull_lods != 3 || validation.textures?.Length != 12 ||
            imported == null || !imported.passed || !imported.postBakeUvValidationPresent || imported.renderers != 24)
            throw new InvalidDataException("Kestrel source and imported prefab need passing saved bake and Unity reports.");
        CheckHash(source + "/Kestrel_K017.fbx", validation.fbx_sha256);
        foreach (var texture in validation.textures)
        {
            if (texture == null || string.IsNullOrEmpty(texture.file)) throw new InvalidDataException("Missing Kestrel texture hash.");
            CheckHash(source + "/Textures/" + texture.file, texture.sha256);
        }
        string prefabPath = ArtLab + buildId + "/Kestrel_K017.prefab";
        if (imported.prefab != prefabPath) throw new InvalidDataException("Unity review report points to a different prefab.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) throw new InvalidDataException("Import the reviewed ArtLab prefab before registration.");
        CheckHash(ArtLab + buildId + "/Kestrel_K017.fbx", validation.fbx_sha256);

        // Roughness stays a source-channel audit image. Unity uses the other
        // nine exported maps across independent hull LOD materials.
        foreach (var texture in validation.textures.Where(t => !t.file.StartsWith("Kestrel_Roughness_source", StringComparison.Ordinal)))
            CheckHash(ArtLab + buildId + "/Textures/" + texture.file, texture.sha256);

        var spec = ShipSpec.Fleet.SingleOrDefault(s => s.designation == "SP-K017");
        if (spec == null || spec.family >= 0 || spec.interiorFamily != 0)
            throw new InvalidDataException("Kestrel must have its own prototype catalog entry and an explicit temporary cabin family.");
        var visual = new AuthoredShipVisual {
            designation = spec.designation,
            resource = "AuthoredShips/KestrelK017/" + buildId,
            sourceBuild = buildId,
            sourceFbxSha256 = validation.fbx_sha256,
            length = spec.length, width = spec.width, height = spec.height,
            rotationY = -90f
        };
        AuthoredShipVisuals.ValidatePrefab(prefab, spec, visual);
        if (Mathf.Abs(imported.bounds.x - spec.length) > .35f ||
            Mathf.Abs(imported.bounds.y - spec.height) > .35f ||
            Mathf.Abs(imported.bounds.z - spec.width) > .35f)
            throw new InvalidDataException("Imported Kestrel size disagrees with its prototype specification.");

        Directory.CreateDirectory(ResourceRoot);
        AssetDatabase.Refresh();
        string destination = ResourceRoot + buildId + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(destination) == null && !AssetDatabase.CopyAsset(prefabPath, destination))
            throw new IOException("Could not copy validated ArtLab prefab into Resources.");
        AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
        var registeredPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(destination);
        AuthoredShipVisuals.ValidatePrefab(registeredPrefab, spec, visual);

        var catalog = File.Exists(CatalogPath)
            ? JsonUtility.FromJson<AuthoredShipVisualCatalog>(File.ReadAllText(CatalogPath))
            : new AuthoredShipVisualCatalog();
        var rows = catalog?.ships?.Where(s => s != null && s.designation != visual.designation).ToList()
            ?? new System.Collections.Generic.List<AuthoredShipVisual>();
        rows.Add(visual);
        catalog = new AuthoredShipVisualCatalog { ships = rows.ToArray() };
        File.WriteAllText(CatalogPath, JsonUtility.ToJson(catalog, true));
        AssetDatabase.ImportAsset(CatalogPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.SaveAssets();
        AuthoredShipVisuals.Reload();
        if (Resources.Load<GameObject>(visual.resource) == null || AuthoredShipVisuals.Find(spec)?.sourceBuild != buildId)
            throw new InvalidDataException("Authored ship registry did not load after import.");

        Directory.CreateDirectory("Validation");
        File.WriteAllText("Validation/authored-kestrel-registration.txt",
            "PASS Kestrel prototype registration\nSource build: " + buildId +
            "\nCraft ID: SP-K017 (separate from 100 original variants)\nFBX SHA256: " + validation.fbx_sha256 +
            "\nSaved UV LODs: 3\nSource maps checked: 12\nUnity maps checked: 9\nLOD0 parts/collision proxies: 8\n" +
            "Source art prefab: " + prefabPath + "\nRuntime prefab: " + destination +
            "\nSource Blender +X nose rotates -90 degrees about Unity Y to gameplay +Z.\n" +
            "Temporary cockpit/deck: original family 0. This does not validate Kestrel interior art.\n");
        Debug.Log("AUTHORED_KESTREL_REGISTERED " + buildId);
    }

    static void CheckHash(string path, string expected)
    {
        if (string.IsNullOrEmpty(expected) || !File.Exists(path)) throw new InvalidDataException("Missing hashed ship asset: " + path);
        using var sha = SHA256.Create();
        string actual = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Ship asset changed since saved UV validation: " + path);
    }
}
