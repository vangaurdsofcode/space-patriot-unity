using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SpacePatriot
{
    [Serializable] public sealed class AuthoredShipVisualCatalog
    {
        public AuthoredShipVisual[] ships = Array.Empty<AuthoredShipVisual>();
    }

    [Serializable] public sealed class AuthoredShipVisual
    {
        public string designation, resource, sourceBuild, sourceFbxSha256;
        public float length, width, height, rotationY;
    }

    public sealed class AuthoredShipInstance : MonoBehaviour
    {
        public string designation, sourceBuild;
        public Vector3 measuredSize;
        public int collisionParts;
    }

    // Exact craft designations keep an authored exterior from silently replacing
    // every variant in a family. The original 100 craft remain the fallback.
    public static class AuthoredShipVisuals
    {
        const string CatalogResource = "AuthoredShipVisuals";
        static AuthoredShipVisualCatalog catalog;

        public static AuthoredShipVisualCatalog Catalog
        {
            get
            {
                if (catalog == null)
                {
                    var asset = Resources.Load<TextAsset>(CatalogResource);
                    catalog = asset == null ? new AuthoredShipVisualCatalog() :
                        JsonUtility.FromJson<AuthoredShipVisualCatalog>(asset.text);
                    if (catalog == null || catalog.ships == null)
                        throw new InvalidOperationException("Authored ship visual catalog is invalid.");
                }
                return catalog;
            }
        }

        public static void Reload() { catalog = null; }

        public static AuthoredShipVisual Find(ShipSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            AuthoredShipVisual found = null;
            foreach (var item in Catalog.ships)
            {
                if (item == null || item.designation != spec.designation) continue;
                if (found != null) throw new InvalidOperationException("Duplicate authored exterior for " + spec.designation);
                found = item;
            }
            return found;
        }

        public static void ValidatePrefab(GameObject prefab, ShipSpec spec, AuthoredShipVisual visual)
        {
            if (prefab == null || visual == null) throw new InvalidOperationException("Authored ship prefab or registration is missing.");
            if (string.IsNullOrWhiteSpace(visual.resource) || string.IsNullOrWhiteSpace(visual.sourceBuild) ||
                string.IsNullOrWhiteSpace(visual.sourceFbxSha256) || visual.designation != spec.designation)
                throw new InvalidOperationException("Authored ship registration is incomplete for " + spec.designation);
            if (!float.IsFinite(visual.rotationY) || Mathf.Abs(visual.rotationY) > 180 ||
                Mathf.Abs(visual.length - spec.length) > .25f ||
                Mathf.Abs(visual.width - spec.width) > .25f ||
                Mathf.Abs(visual.height - spec.height) > .25f)
                throw new InvalidOperationException("Authored ship size or orientation does not match the catalog: " + spec.designation);
            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("Authored prefab contains colliders; runtime builds one proxy for each LOD0 part.");
            var groups = prefab.GetComponentsInChildren<LODGroup>(true);
            if (groups.Length != 1) throw new InvalidOperationException("Authored ship needs exactly one LOD group: " + spec.designation);
            var lods = groups[0].GetLODs();
            if (lods.Length != 3) throw new InvalidOperationException("Authored ship needs three LODs: " + spec.designation);
            float previous = 1f;
            for (int level = 0; level < lods.Length; level++)
            {
                if (lods[level].screenRelativeTransitionHeight <= 0 || lods[level].screenRelativeTransitionHeight >= previous ||
                    lods[level].renderers == null || lods[level].renderers.Length != 8)
                    throw new InvalidOperationException("Authored ship LOD" + level + " is incomplete: " + spec.designation);
                previous = lods[level].screenRelativeTransitionHeight;
                foreach (var renderer in lods[level].renderers)
                {
                    if (!(renderer is MeshRenderer meshRenderer) || renderer.GetComponent<MeshFilter>()?.sharedMesh is not Mesh mesh ||
                        mesh.vertexCount == 0 || mesh.uv.Length != mesh.vertexCount ||
                        meshRenderer.sharedMaterials.Length != 1 || meshRenderer.sharedMaterial == null ||
                        meshRenderer.sharedMaterial.GetTexture("_BaseMap") == null ||
                        meshRenderer.sharedMaterial.GetTexture("_BumpMap") == null ||
                        meshRenderer.sharedMaterial.GetTexture("_MetallicGlossMap") == null)
                        throw new InvalidOperationException("Authored ship LOD" + level + " has missing mesh, UVs or material: " + renderer?.name);
                }
            }
            if (lods.SelectMany(l => l.renderers).Distinct().Count() != 24)
                throw new InvalidOperationException("Authored ship LOD renderers overlap or are missing: " + spec.designation);
        }

        public static bool TryCreate(ShipSpec spec, Transform ship, float standHeight, out Transform exterior)
        {
            exterior = null;
            var visual = Find(spec);
            if (visual == null)
            {
                if (spec.family < 0) throw new InvalidOperationException("No verified exterior is registered for " + spec.designation);
                return false;
            }
            var prefab = Resources.Load<GameObject>(visual.resource);
            ValidatePrefab(prefab, spec, visual);
            var root = new GameObject("Exterior hull").transform;
            root.SetParent(ship, false);
            try
            {
                var instance = UnityEngine.Object.Instantiate(prefab, root).transform;
                instance.name = spec.name + " / authored exterior";
                instance.localRotation = Quaternion.Euler(0, visual.rotationY, 0);
                var lod0 = instance.GetComponent<LODGroup>().GetLODs()[0].renderers;
                Bounds bounds = lod0[0].bounds;
                foreach (var renderer in lod0.Skip(1)) bounds.Encapsulate(renderer.bounds);
                if (Mathf.Abs(bounds.size.x - spec.width) > .35f ||
                    Mathf.Abs(bounds.size.y - spec.height) > .35f ||
                    Mathf.Abs(bounds.size.z - spec.length) > .35f)
                    throw new InvalidOperationException("Authored ship points along the wrong axis or has the wrong size: " + bounds.size);

                // Mesh coordinates stay at their authored scale. Align the lowest
                // foot with the game's surface contact height instead of stretching
                // the hull to fit another craft's dimensions.
                float verticalShift = -standHeight - bounds.min.y;
                instance.localPosition = new Vector3(0, verticalShift, 0);
                int proxies = 0;
                foreach (var renderer in lod0)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    var meshBounds = filter.sharedMesh.bounds;
                    var proxy = new GameObject(renderer.name + " / collision proxy");
                    proxy.layer = 2; // Flight's default-layer sphere cast must never hit its own hull.
                    proxy.transform.SetParent(renderer.transform, false);
                    var box = proxy.AddComponent<BoxCollider>();
                    box.center = meshBounds.center;
                    box.size = meshBounds.size;
                    proxies++;
                }
                var marker = root.gameObject.AddComponent<AuthoredShipInstance>();
                marker.designation = spec.designation;
                marker.sourceBuild = visual.sourceBuild;
                marker.measuredSize = bounds.size;
                marker.collisionParts = proxies;
                exterior = root;
                return true;
            }
            catch
            {
                UnityEngine.Object.Destroy(root.gameObject);
                throw;
            }
        }
    }
}
