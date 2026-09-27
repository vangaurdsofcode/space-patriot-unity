using System;
using Random=UnityEngine.Random;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static SpacePatriot.IndustrialArt;

namespace SpacePatriot
{
    public sealed class Place
    {
        public string name,kind;public Vector3 position;public float range;
        public Place(string n,string k,Vector3 p,float r=5){name=n;kind=k;position=p;range=r;}
    }
    public partial class FrontierWorld : MonoBehaviour
    {
        public WorldInfo info;
        public List<Place> places=new List<Place>();
        public Vector3 station=new Vector3(0,2500,1800), outpost=new Vector3(-410,0,380),grove=new Vector3(330,0,250);
        public float Deck=>info.biome=="gas"?125:12;
        Transform content;
        Transform orbit;
        readonly List<UnityEngine.Object> generatedAssets=new List<UnityEngine.Object>();
        public void Generate(WorldInfo world)
        {
            VegetationRevision++;
            info=world;var data=FrontierGame.Instance.save;settlement=Array.Find(SocietyEconomy.Catalog,c=>c.id==data.settlement&&c.world==world.id)??Array.Find(SocietyEconomy.Catalog,c=>c.primary&&c.world==world.id);data.settlement=settlement.id;
            // The streamed terrain patch is cached beside per-world content.
            // Dispose it before the old world material is destroyed so it
            // cannot keep a broken material after a planet switch.
            if(streamedTerrainRoot)
            {
                var filter=streamedTerrainRoot.GetComponent<MeshFilter>();
                if(filter)filter.sharedMesh=null;
                if(streamedTerrainCollider)streamedTerrainCollider.sharedMesh=null;
                if(streamedTerrainMesh)Destroy(streamedTerrainMesh);
                streamedTerrainMesh=null;
                streamedTerrainRoot.gameObject.SetActive(false);
            }
            if(content!=null){content.gameObject.SetActive(false);Destroy(content.gameObject);}places.Clear();
            foreach(var asset in generatedAssets)if(asset!=null)Destroy(asset);generatedAssets.Clear();
            content=Root(world.name+" / frontier",transform);Random.InitState(world.seed);
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.68f,.72f,.77f);
            RenderSettings.ambientEquatorColor=new Color(.47f,.51f,.55f);RenderSettings.ambientGroundColor=new Color(.17f,.16f,.15f);
            // Match the source Three.js surface FogExp2 density. At Unity's
            // metre scale, .0008 erases the HTML vegetation engine's 1.4-1.85km
            // woodland band before its distance fade can do its job.
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.00022f;
            RenderSettings.fogColor=world.biome=="desert"?new Color(.38f,.28f,.23f):new Color(.20f,.28f,.34f);
            var sunObj=new GameObject("Late afternoon sun");sunObj.transform.SetParent(content);sunObj.transform.rotation=Quaternion.Euler(24,-32,0);
            var sun=sunObj.AddComponent<Light>();sun.type=LightType.Directional;sun.color=new Color(1,.86f,.69f);sun.intensity=2.05f;sun.shadows=LightShadows.Soft;sun.shadowStrength=.83f;RenderSettings.sun=sun;
            Terrain();Port(Vector3.zero,false);
            outpost.y=Height(outpost.x,outpost.z)+5;Port(outpost,true);
            grove.y=Height(grove.x,grove.z);
            places.Add(new Place(world.biome=="temperate"?"Survey grove":"Geological survey","sample",grove,12));
            for(int i=0;i<14;i++)
            { float angle=i*2.4f;Vector3 pos=grove+new Vector3(Mathf.Sin(angle)*(4+i),0,Mathf.Cos(angle)*(4+i));pos.y=Height(pos.x,pos.z);Rock(pos,world.biome=="temperate"?2:1); }
            var survey=Root("Field survey station",content,grove);Terminal(survey,Vector3.zero,"SURVEY / 07",Cyan);
            Box("Survey field deck",survey,new Vector3(0,.5f,0),new Vector3(50,1,30),Steel,true);
            places.Add(new Place("Survey landing pad","landing",grove+new Vector3(-12,3.65f,0),13));
            OrbitalStation();
            for(int i=0;i<95;i++)
            {
                var p=new Vector3(Random.Range(-1100f,1100f),0,Random.Range(-1100f,1100f));
                if(p.magnitude<110||Vector3.Distance(new Vector3(p.x,0,p.z),new Vector3(outpost.x,0,outpost.z))<65)continue;
                p.y=Height(p.x,p.z);Rock(p,Random.Range(.8f,4));
            }
            CityDistricts();BiomePopulation();
            // One combined static set avoids issuing a draw for every bolt and beam.
            var staticObjects=new List<GameObject>();foreach(var renderer in content.GetComponentsInChildren<MeshRenderer>()){renderer.gameObject.isStatic=renderer.GetComponentInParent<HangarLift>()==null&&renderer.GetComponent<BuildingLiftPart>()==null&&renderer.GetComponentInParent<DistrictShuttle>()==null;if(renderer.gameObject.isStatic&&renderer.gameObject.name!="Continuous spherical terrain")staticObjects.Add(renderer.gameObject);}
            StaticBatchingUtility.Combine(staticObjects.ToArray(),content.gameObject);
            BuildOrbit();
            grass=new GameObject("Grassworks / regional vegetation").AddComponent<Grassworks>();grass.transform.SetParent(content);grass.Initialize(this);
            if(world.biome=="temperate")content.gameObject.AddComponent<WorldForestLayer>().Initialize(this);
            if(world.biome=="temperate")content.gameObject.AddComponent<GaussianVegetationLayer>().Initialize(this);
            var weather=new GameObject("Weatherworks / climate").AddComponent<Weatherworks>();weather.transform.SetParent(content);weather.Initialize(this);
            var water=new GameObject("Oceanworks / watershed").AddComponent<Oceanworks>();water.transform.SetParent(content);water.Initialize(this);
        }
        void Rock(Vector3 p,float scale)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name="Eroded outcrop";go.transform.SetParent(content);go.transform.position=p;go.transform.localScale=new Vector3(scale*3,scale*1.7f,scale*2.1f);go.transform.rotation=Random.rotation;
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=Metal(info.id+" stone",info.Surface*.71f,.05f,.94f);
            var rockMesh=UnityEngine.Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);var rockVertices=rockMesh.vertices;
            for(int v=0;v<rockVertices.Length;v++){var a=rockVertices[v];float f=.75f+Mathf.PerlinNoise(a.x*7+6,a.y*6+a.z*3+9)*.55f;rockVertices[v]=a*f;}
            rockMesh.vertices=rockVertices;rockMesh.RecalculateNormals();rockMesh.RecalculateBounds();go.GetComponent<MeshFilter>().sharedMesh=rockMesh;generatedAssets.Add(rockMesh);
            if(info.biome=="temperate" && scale>2)
            {
                var prefab=Resources.Load<GameObject>("OriginalShips/"+(info.name=="Earth"?"conifer":"alien-flora-"+(Mathf.Abs(Mathf.RoundToInt(p.x))%3)));
                if(prefab!=null){var tree=Instantiate(prefab,content).transform;tree.position=p;tree.localScale=Vector3.one*scale*.4f;}

            }
        }
        public float SurfaceAt(Vector3 pos)
        {
            float height=Height(pos.x,pos.z);
            if(Mathf.Abs(pos.x-165)<240&&Mathf.Abs(pos.z)<240)height=Mathf.Max(height,Deck);
            if(Mathf.Abs(pos.x)<75&&Mathf.Abs(pos.z)<125)height=Mathf.Max(height,Deck);
            if(Mathf.Abs(pos.x-outpost.x)<60&&Mathf.Abs(pos.z-outpost.z)<60)height=Mathf.Max(height,outpost.y);
            if(Mathf.Abs(pos.x-station.x)<345&&Mathf.Abs(pos.z-station.z)<700&&pos.y>station.y-15)height=Mathf.Max(height,station.y);
            if(Mathf.Abs(pos.x-grove.x)<25&&Mathf.Abs(pos.z-grove.z)<15)height=Mathf.Max(height,grove.y+1);
            // Find the supporting floor below the walker, never the roof above them.
            if(Physics.Raycast(pos+Vector3.up*.1f,Vector3.down,out var hit,Mathf.Max(4,pos.y-height+1),Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))height=Mathf.Max(height,hit.point.y);
            return height;
        }
        public float AltitudeAboveSurface(Vector3 position)
        {
            if(TrySurface(position,out var point,out var normal))return Mathf.Max(0,Vector3.Dot(position-point,normal));
            return Mathf.Max(0,position.y-SurfaceAt(position));
        }
        public Place Nearest(Vector3 p,string kind=null)
        {
            Place best=null;float distance=float.MaxValue;
            foreach(var place in places){if(kind!=null&&place.kind!=kind)continue;float d=Vector3.Distance(p,place.position);if(d<distance){best=place;distance=d;}}return best;
        }
        public void Atmosphere(Camera camera,Vector3 observer)
        {
            UpdateRadialAtmosphere(camera,observer);
        }
        void BuildOrbit()
        {
            if(orbit!=null)Destroy(orbit.gameObject);orbit=Root("Orbital scale vista",transform);
            var vertices=new List<Vector3>();var triangles=new List<int>();
            Random.InitState(983);
            for(int i=0;i<700;i++)
            {
                Vector3 d=Random.onUnitSphere;if(d.y<-.18f)continue;Vector3 p=d*10000;Vector3 right=Vector3.Cross(d,Vector3.up).normalized*Random.Range(1.3f,3.2f);Vector3 up=Vector3.Cross(right.normalized,d)*right.magnitude;
                int start=vertices.Count;vertices.Add(p-right-up);vertices.Add(p+right-up);vertices.Add(p+right+up);vertices.Add(p-right+up);triangles.AddRange(new[]{start,start+2,start+1,start,start+3,start+2});
            }
            var starMesh=new Mesh{name="Distant star field"};starMesh.SetVertices(vertices);starMesh.SetTriangles(triangles,0);starMesh.RecalculateNormals();starMesh.RecalculateBounds();
            generatedAssets.Add(starMesh);
            var starmat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));starmat.SetColor("_BaseColor",new Color(.8f,.87f,1));starmat.SetFloat("_Cull",0);
            generatedAssets.Add(starmat);
            MeshObject("Stars",orbit,starMesh,starmat,Vector3.zero,Vector3.one);orbit.gameObject.SetActive(false);
        }
    }
}


