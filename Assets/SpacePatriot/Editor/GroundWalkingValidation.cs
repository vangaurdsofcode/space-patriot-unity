using System;
using SpacePatriot;
using UnityEditor;
using UnityEngine;

public static class GroundWalkingValidation
{
    [MenuItem("Space Patriot/Validate grounded walking")]
    public static void Validate()
    {
        var root = new GameObject("Ground walking validation");
        root.transform.position = new Vector3(6200, 0, 6200);
        var world = root.AddComponent<FrontierWorld>();
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        int passed = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new Exception("GROUND_WALKING_FAILED: " + label);
            Debug.Log("GROUND_WALKING_PASS: " + label);
            passed++;
        }
        try
        {
            var floor = IndustrialArt.Box("Test port apron", root.transform,
                new Vector3(0, -.25f, 0), new Vector3(12, .5f, 12), material, true);
            var collider = floor.GetComponent<BoxCollider>();
            Check(collider != null && collider.size == new Vector3(12, .5f, 12),
                "visible floor and collision footprint have matching dimensions");

            Vector3 at = root.transform.position;
            Vector3 supportedFeet = at + new Vector3(4, .05f, 4);
            Check(world.TryWalkSupport(supportedFeet, .45f, .85f,
                out var point, out _, out var support) && support == collider &&
                Mathf.Abs(point.y) < .01f &&
                Mathf.Abs(point.x - supportedFeet.x) < .01f &&
                Mathf.Abs(point.z - supportedFeet.z) < .01f,
                "walk support uses the port floor at its edge, without horizontal drift");
            Check(!world.TryWalkSupport(at + new Vector3(7, .05f, 0), .45f, .85f,
                out _, out _, out _), "missing floor stops movement at the apron edge");

            var wall = new GameObject("Test structural wall");
            wall.transform.SetParent(root.transform, false);
            wall.transform.localPosition = new Vector3(1, .9f, 0);
            wall.AddComponent<BoxCollider>().size = new Vector3(.2f, 1.8f, 2);
            Check(world.TryWalkSupport(at, .45f, .85f, out point, out _, out support) &&
                world.WalkClear(point, world.WalkUp(point), support),
                "open floor accepts a standing capsule");
            Vector3 wallFeet = at + new Vector3(1, 0, 0);
            Check(world.TryWalkSupport(wallFeet, .45f, .85f, out point, out _, out support) &&
                !world.WalkClear(point, world.WalkUp(point), support),
                "structural wall blocks a walking capsule");

            floor.transform.localPosition += Vector3.up * .35f;
            Check(world.TryWalkSupport(at, .45f, .85f, out point, out _, out support) &&
                Mathf.Abs(point.y - .35f) < .01f,
                "walker can follow a deck rising within the step limit");
            floor.transform.localPosition += Vector3.up * 1.2f;
            Check(!world.TryWalkSupport(at, .45f, .85f, out _, out _, out _),
                "excessive step height is rejected");

            var steep = new GameObject("Test steep slope");
            steep.transform.SetParent(root.transform, false);
            steep.transform.localPosition = new Vector3(24, 0, 0);
            steep.transform.localRotation = Quaternion.Euler(0, 0, 60);
            steep.AddComponent<BoxCollider>().size = new Vector3(6, .2f, 6);
            Check(!world.TryWalkSupport(at + new Vector3(24, .15f, 0), .45f, .85f,
                out _, out _, out _), "steep face is not walkable");
            Debug.Log("GROUND_WALKING_VALIDATION_PASS: " + passed + " grounded checks");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
