using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace SpacePatriot
{
    // Oceanworks' directional swell, crossing waves, ripples and crest sharpening.
    public sealed class Oceanworks : MonoBehaviour
    {
        public static float WaveHeight(Vector2 p,float time,float height=.18f,float length=14,float speed=.7f,float ripples=.2f)
        {
            float Wave(float a,float l,float v,float phase)=>Mathf.Sin(Vector2.Dot(p,new Vector2(Mathf.Cos(a),Mathf.Sin(a)))*2*Mathf.PI/Mathf.Max(l,.1f)-time*v+phase);
            float d=35*Mathf.Deg2Rad;
            float h=Wave(d,length,speed*1.15f,0)*height*.56f+Wave(d+.68f,length*.53f,speed*1.55f,1.7f)*height*.25f+Wave(d-1.04f,length*.27f,speed*2.15f,3.2f)*height*.12f+Wave(d+1.58f,4.2f,speed*3.1f,2.3f)*ripples*.1f;
            h+=Mathf.Sin(p.x*2.4f+p.y*2.05f-time*speed*4.4f)*ripples*.025f;float positive=Mathf.Max(h/Mathf.Max(height,.1f),0);return h+positive*positive*.55f*height*.28f;
        }
        Mesh mesh;Material material;
        public void Initialize(FrontierWorld world)
        {
            if(world.info.biome!="temperate"){enabled=false;return;}
            var vertices=new List<Vector3>();var indices=new List<int>();const int rings=180,sectors=256;
            for(int ring=0;ring<=rings;ring++)for(int j=0;j<=sectors;j++){
                float a=j*Mathf.PI*2/sectors,r=ring*15f,x=Mathf.Cos(a)*r,z=Mathf.Sin(a)*r;
                float seaRadius=FrontierWorld.PlanetRadius+FrontierWorld.SeaLevel;
                float y=world.PlanetCenter.y+Mathf.Sqrt(seaRadius*seaRadius-r*r);
                vertices.Add(new Vector3(x,y,z));if(ring<rings&&j<sectors){int k=ring*(sectors+1)+j;indices.AddRange(new[]{k,k+1,k+sectors+1,k+1,k+sectors+2,k+sectors+1});}
            }
            mesh=new Mesh{name="Oceanworks curved water",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();var bounds=mesh.bounds;bounds.Expand(2);mesh.bounds=bounds;
            material=new Material(Resources.Load<Shader>("Shaders/Oceanworks"));gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
        }
        void OnDestroy(){if(mesh!=null)Destroy(mesh);if(material!=null)Destroy(material);}
    }
}
