using System.IO;
using UnityEngine;

namespace SpacePatriot
{
    /// <summary>Planetwide climate atlas exported by the original HTML PlanetEngines.region.</summary>
    public sealed class PlanetRegionClimate
    {
        readonly int width, height;
        readonly Vector4[] samples;

        public PlanetRegionClimate(string worldId)
        {
            var asset = Resources.Load<TextAsset>("Worldworks/Climate/" + worldId);
            if (asset == null) return;
            using var reader = new BinaryReader(new MemoryStream(asset.bytes));
            width = reader.ReadInt32();
            height = reader.ReadInt32();
            reader.ReadUInt32(); // source world seed, retained in the binary header for audits
            reader.ReadSingle();
            if (width < 2 || height < 2 || asset.bytes.Length != 16 + width * height * 16)
            {
                width = height = 0;
                return;
            }
            samples = new Vector4[width * height];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        /// <summary>Return moisture, temperature, rock exposure and forest cover for a local planet normal.</summary>
        public Vector4 Sample(Vector3 unityNormal)
        {
            if (samples == null || width < 2 || height < 2) return new Vector4(.2f, .5f, .8f, 0);
            Vector3 source = PlanetEngineSurface.ToSourceNormal(unityNormal.normalized);
            float longitude = Mathf.Atan2(source.z, source.x) / (2 * Mathf.PI) + .5f;
            float latitude = Mathf.Asin(Mathf.Clamp(source.y, -1, 1)) / Mathf.PI + .5f;
            float x = Mathf.Repeat(longitude * width, width);
            float y = Mathf.Clamp(latitude * (height - 1), 0, height - 1.001f);
            int x0 = Mathf.FloorToInt(x), x1 = (x0 + 1) % width;
            int y0 = Mathf.FloorToInt(y), y1 = Mathf.Min(y0 + 1, height - 1);
            float tx = x - x0, ty = y - y0;
            return Vector4.Lerp(Vector4.Lerp(samples[y0 * width + x0], samples[y0 * width + x1], tx),
                                Vector4.Lerp(samples[y1 * width + x0], samples[y1 * width + x1], tx), ty);
        }
    }
}
