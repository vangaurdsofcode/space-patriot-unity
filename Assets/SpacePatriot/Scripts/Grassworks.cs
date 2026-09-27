using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace SpacePatriot
{
    // Native grasspack renderer. Cells belong to the planet, never the observer's tangent plane.
    public sealed class Grassworks : MonoBehaviour
    {
        public const int TileSize=48, BladesPerTile=12000, MaximumTiles=9;
        public int BladeCount {get;private set;}
        public int ResidentTiles=>tiles.Count;
        FrontierWorld world;Material material;int revision;
        Vector3 selectedAt=Vector3.positiveInfinity;
        List<WorldVegetationCell> wanted=new();
        sealed class Tile {public GameObject go;public Mesh mesh;public int blades;}
        readonly Dictionary<WorldVegetationCell,Tile> tiles=new();
        public void Initialize(FrontierWorld owner)
        {
            world=owner;revision=world.VegetationRevision;
            transform.localPosition=Vector3.zero;transform.localRotation=Quaternion.identity;transform.localScale=Vector3.one;
            if(world.info.biome!="temperate"){enabled=false;return;}
            material=new Material(Resources.Load<Shader>("Shaders/Grassworks"));
        }
        void Update(){var camera=FrontierGame.Instance?.view;if(camera!=null)UpdateAround(camera.transform.position);}
        public void UpdateAround(Vector3 eye)
        {
            if(world==null||world.info.biome!="temperate")return;
            if(revision!=world.VegetationRevision){ClearTiles();revision=world.VegetationRevision;selectedAt=Vector3.positiveInfinity;}
            bool visible=world.TryVegetationFocus(eye,out var focus,out float altitude)&&altitude<100f&&altitude> -8f;
            foreach(var tile in tiles.Values)tile.go.SetActive(visible);if(!visible)return;
            if((focus.position-selectedAt).sqrMagnitude>64f)
            {
                wanted=PlanetVegetationCells.Near(world.info.id,focus.up,TileSize,105,MaximumTiles);selectedAt=focus.position;
                var remove=new List<WorldVegetationCell>();foreach(var key in tiles.Keys)if(!wanted.Contains(key))remove.Add(key);
                foreach(var key in remove){Release(tiles[key]);tiles.Remove(key);}
            }
            // One tile per frame and nine resident tiles, even after a teleport.
            foreach(var key in wanted)if(!tiles.ContainsKey(key)){tiles.Add(key,BuildTile(key));return;}
        }
        // Kept for port-authoring tools. Global placement never calls Height(x,z).
        public bool CanGrow(float x,float z)
        {
            var sample=world.SampleVegetation(new Vector3(x,world.Height(x,z),z)-world.PlanetCenter);
            return sample.valid&&Vector3.Dot(sample.normal,sample.up)>.82f;
        }
        Tile BuildTile(WorldVegetationCell key)
        {
            var random=new System.Random(key.Seed(world.info.seed));float R()=>(float)random.NextDouble();
            var center=world.SampleVegetation(key.Direction());Quaternion rotation=PlanetVegetationCells.Rotation(center.up),inverse=Quaternion.Inverse(rotation);
            // A shared surface lattice makes dense blades inexpensive while retaining relief and shoreline rejection.
            const int resolution=16;var grid=new VegetationSample[(resolution+1)*(resolution+1)];
            for(int z=0;z<=resolution;z++)for(int x=0;x<=resolution;x++)grid[z*(resolution+1)+x]=world.SampleVegetation(key.Direction(x/(float)resolution,z/(float)resolution));
            var positions=new List<Vector3>();var uv=new List<Vector2>();var extras=new List<Vector2>();var colors=new List<Color>();var indices=new List<int>();int count=0;
            for(int blade=0;blade<BladesPerTile;blade++)
            {
                float u=R()*resolution,v=R()*resolution;int ix=Mathf.Min(resolution-1,(int)u),iz=Mathf.Min(resolution-1,(int)v);float tx=u-ix,tz=v-iz;
                var a=grid[iz*(resolution+1)+ix];var b=grid[iz*(resolution+1)+ix+1];var c=grid[(iz+1)*(resolution+1)+ix];var d=grid[(iz+1)*(resolution+1)+ix+1];
                if(!a.valid||!b.valid||!c.valid||!d.valid||Vector3.Dot(a.normal,a.up)<.82f)continue;
                float moisture=Mathf.Lerp(Mathf.Lerp(a.moisture,b.moisture,tx),Mathf.Lerp(c.moisture,d.moisture,tx),tz);
                // Preserve source density variation under the explicit native mesh budget.
                if(R()>Mathf.Clamp01((9000+moisture*24000)/33000f))continue;
                Vector3 root=Vector3.Lerp(Vector3.Lerp(a.position,b.position,tx),Vector3.Lerp(c.position,d.position,tx),tz);
                Vector3 normal=Vector3.Lerp(Vector3.Lerp(a.up,b.up,tx),Vector3.Lerp(c.up,d.up,tx),tz).normalized;
                float seed=R(),angle=R()*Mathf.PI*2,height=Mathf.Lerp(Mathf.Lerp(a.grassHeight,b.grassHeight,tx),Mathf.Lerp(c.grassHeight,d.grassHeight,tx),tz)*Mathf.Lerp(.65f,1.35f,R()),width=.022f+moisture*.035f;
                Vector3 tangent=Vector3.ProjectOnPlane(rotation*new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)),normal).normalized;
                var color=world.info.GrassPigment(moisture);int first=positions.Count;
                for(int segment=0;segment<=5;segment++)for(int side=0;side<2;side++)
                {
                    float t=segment/5f,lateral=(side-.5f)*width*Mathf.Pow(1-t,.72f);
                    positions.Add(inverse*(root-center.position+normal*(t*height-.025f)+tangent*lateral));
                    uv.Add(new Vector2(side,t));extras.Add(new Vector2(seed,height));colors.Add(color);
                    if(segment<5&&side==0){int q=first+segment*2;indices.Add(q);indices.Add(q+2);indices.Add(q+1);indices.Add(q+1);indices.Add(q+2);indices.Add(q+3);}
                }
                count++;
            }
            var mesh=new Mesh{name="Grassworks tile "+key,indexFormat=IndexFormat.UInt32};mesh.SetVertices(positions);mesh.SetUVs(0,uv);mesh.SetUVs(1,extras);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();var bounds=mesh.bounds;bounds.Expand(4);mesh.bounds=bounds;
            var go=new GameObject(mesh.name);go.transform.SetParent(transform,false);go.transform.localPosition=center.position;go.transform.localRotation=rotation;go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=true;
            BladeCount+=count;return new Tile{go=go,mesh=mesh,blades=count};
        }
        void Release(Tile tile){BladeCount-=tile.blades;if(tile.go!=null){tile.go.SetActive(false);Destroy(tile.go);}if(tile.mesh!=null)Destroy(tile.mesh);}
        void ClearTiles(){foreach(var tile in tiles.Values)Release(tile);tiles.Clear();}
        void OnDestroy(){ClearTiles();if(material!=null)Destroy(material);}
    }
}
