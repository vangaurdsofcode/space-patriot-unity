using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Render the real vegetation shader and compare its footprint with a projected 3D ellipsoid.</summary>
public static class GaussianVegetationValidation
{
    const int Size = 512;

    public static void Run()
    {
        if (!SystemInfo.supportsInstancing) throw new Exception("GPU instancing is required for vegetation validation.");
        var shader = Resources.Load<Shader>("Shaders/GaussianVegetation");
        if (shader == null || !shader.isSupported) throw new Exception("Gaussian vegetation shader is unavailable.");
        var material = new Material(shader) { enableInstancing = true };
        var mesh = new Mesh { name = "Gaussian footprint validation" };
        mesh.vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) };
        mesh.uv = new[] { new Vector2(-1,-1), new Vector2(1,-1), new Vector2(1,1), new Vector2(-1,1) };
        mesh.triangles = new[] { 0,1,2, 0,2,3 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 6);
        var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        target.Create();
        var pixels = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        var lines = new List<string>();
        var previous = RenderTexture.active;
        Vector4 oldScreenParams = Shader.GetGlobalVector("_ScreenParams");
        try
        {
            // Meadow, canopy, rotated planet/camera, and off-axis camera cases
            // expose lost instance scale and mixing world with camera-space axes.
            CheckFootprint("flat meadow", new Vector3(9, .7f, 9), Quaternion.Euler(24, 0, 0), Quaternion.identity, Vector3.zero);
            CheckFootprint("tall canopy", new Vector3(4, 8, 4), Quaternion.identity, Quaternion.identity, Vector3.zero);
            CheckFootprint("rotated camera", new Vector3(9, 2, 4), Quaternion.Euler(21, 67, 18), Quaternion.identity, Vector3.zero);
            CheckFootprint("rotated planet", new Vector3(9, 2, 4), Quaternion.Euler(-17, 32, 9), Quaternion.Euler(18, 49, 31), Vector3.zero);
            CheckFootprint("off-axis", new Vector3(7, 2, 4), Quaternion.Euler(12, 36, -14), Quaternion.Euler(0, 25, 0), new Vector3(9, 5, 0));

            // Original gaussian-splats.js rejects ellipsoids crossing the eye
            // plane; their perspective covariance otherwise forms screen sheets.
            var nearView = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.Translate(new Vector3(0, 0, 4));
            var nearModel = Matrix4x4.Scale(new Vector3(2, 2, 2));
            var nearPixels = Render(nearModel, nearView);
            double nearEnergy = 0;
            foreach (var color in nearPixels) nearEnergy += color.r;
            if (nearEnergy > .001) throw new Exception("An ellipsoid crossing the eye plane was rendered.");
            lines.Add("PASS: eye-plane crossing is culled instead of expanding over the screen");

            Directory.CreateDirectory("Validation");
            File.WriteAllLines("Validation/gaussian-vegetation-projection.txt", lines);
            Debug.Log("GAUSSIAN_VEGETATION_PROJECTION_PASS " + lines.Count);
        }
        finally
        {
            RenderTexture.active = previous;
            Shader.SetGlobalVector("_ScreenParams", oldScreenParams);
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(material);
        }

        Color[] Render(Matrix4x4 model, Matrix4x4 view)
        {
            var projection = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, 1, .1f, 500), true);
            var block = new MaterialPropertyBlock();
            block.SetVectorArray("_InstanceTint", new[] { new Vector4(1, 1, 1, 1) });
            block.SetVector("_Range", new Vector4(0, 0, 400, 450));
            block.SetFloat("_Fade", 1);
            block.SetFloat("_Wind", 0);
            block.SetFloat("_FogDensity", 0);
            using (var command = new CommandBuffer { name = "Validate source Gaussian covariance" })
            {
                command.SetRenderTarget(target);
                command.ClearRenderTarget(true, true, Color.clear);
                command.SetViewport(new Rect(0, 0, Size, Size));
                command.SetViewProjectionMatrices(view, projection);
                command.SetGlobalVector("_ScreenParams", new Vector4(Size, Size, 1 + 1f / Size, 1 + 1f / Size));
                command.DrawMeshInstanced(mesh, 0, material, 0, new[] { model }, 1, block);
                Graphics.ExecuteCommandBuffer(command);
            }
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
            pixels.Apply(false, false);
            return pixels.GetPixels();
        }

        void CheckFootprint(string name, Vector3 sigma, Quaternion cameraRotation, Quaternion rotation, Vector3 center)
        {
            Vector3 eye = cameraRotation * (Vector3.back * 80);
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(eye, cameraRotation, Vector3.one).inverse;
            Matrix4x4 model = Matrix4x4.TRS(center, rotation, sigma);
            var colors = Render(model, view);
            double mass = 0, sumX = 0, sumY = 0;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                double weight = colors[y * Size + x].r;
                mass += weight; sumX += weight * (x + .5); sumY += weight * (y + .5);
            }
            if (mass < 1) throw new Exception(name + ": vegetation produced no measurable footprint.");
            double meanX = sumX / mass, meanY = sumY / mass, xx = 0, xy = 0, yy = 0;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                double weight = colors[y * Size + x].r, dx = x + .5 - meanX, dy = y + .5 - meanY;
                xx += weight * dx * dx; xy += weight * dx * dy; yy += weight * dy * dy;
            }
            xx /= mass; xy /= mass; yy /= mass;

            // Numerical derivatives of projected 3D offsets provide an
            // independent oracle, without copying the shader Jacobian/eigensteps.
            var vp = GL.GetGPUProjectionMatrix(Matrix4x4.Perspective(60, 1, .1f, 500), true) * view;
            Vector2 Project(Vector3 p)
            {
                Vector4 clip = vp * new Vector4(p.x, p.y, p.z, 1);
                return new Vector2(clip.x / clip.w, clip.y / clip.w) * (Size * .5f);
            }
            double expectedXX = .3, expectedXY = 0, expectedYY = .3;
            foreach (Vector3 basis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                Vector3 axis = model.MultiplyVector(basis);
                Vector2 derivative = (Project(center + axis * .01f) - Project(center - axis * .01f)) / .02f;
                expectedXX += derivative.x * derivative.x;
                expectedXY += derivative.x * derivative.y;
                expectedYY += derivative.y * derivative.y;
            }
            // Rasterization discards outside the original engine's 3-sigma disk.
            double tail = Math.Exp(-4.5), truncation = (1 - 5.5 * tail) / (1 - tail);
            expectedXX *= truncation; expectedXY *= truncation; expectedYY *= truncation;
            double error = Math.Max(Math.Abs(xx - expectedXX) / expectedXX, Math.Abs(yy - expectedYY) / expectedYY);
            double crossError = Math.Abs(Math.Abs(xy) - Math.Abs(expectedXY)) / Math.Sqrt(expectedXX * expectedYY);
            if (error > .06 || crossError > .06)
                throw new Exception($"{name}: GPU footprint differs from projected ellipsoid: xx {xx:F2}/{expectedXX:F2}, yy {yy:F2}/{expectedYY:F2}, xy {xy:F2}/{expectedXY:F2}.");
            lines.Add($"PASS: {name} GPU covariance agrees with projected 3D ellipsoid (variance error {error:P2}; cross error {crossError:P2})");
        }
    }
}
