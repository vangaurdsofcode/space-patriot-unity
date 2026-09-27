using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace SpacePatriot
{
    /// <summary>Source Gaussian vegetation LODs, streamed in stable spherical cells.</summary>
    public sealed class GaussianVegetationLayer : MonoBehaviour
    {
        const int BatchSize=1023;
        const float MeadowRadius=440f,WoodlandRadius=2050f,RebuildDistance=240f;
        public const int MaximumMeadowSplats=60000,MaximumWoodlandSplats=150000;
        public int MeadowCount=>meadow.Count;
        public int WoodlandCount=>woodland.Count;
        public int VisibleMeadowCount=>visibleMeadow.Count;
        public int VisibleWoodlandCount=>visibleWoodland.Count;
        public bool IsBuilding=>buildJob!=null;
        sealed class Splat
        {
            public Vector3 root,position,scale;
            public Quaternion rotation;
            public string cellId;
            public Vector4 tint;
        }
        static readonly int TintId=Shader.PropertyToID("_InstanceTint"),RangeId=Shader.PropertyToID("_Range"),FadeId=Shader.PropertyToID("_Fade"),WindId=Shader.PropertyToID("_Wind"),FogDensityId=Shader.PropertyToID("_FogDensity"),FogColorId=Shader.PropertyToID("_FogColor");
        static readonly Matrix4x4[] Matrices=new Matrix4x4[BatchSize];
        static readonly Vector4[] Tints=new Vector4[BatchSize];
        readonly List<Splat> meadow=new(),woodland=new(),visibleMeadow=new(),visibleWoodland=new();
        MaterialPropertyBlock block;FrontierWorld world;Mesh quad;Material material;Coroutine buildJob;
        Vector3 builtAt=Vector3.positiveInfinity,buildingAt;int revision;float lastCull=-1,fade;
        public void Initialize(FrontierWorld owner)
        {
            world=owner;revision=world.VegetationRevision;block=new MaterialPropertyBlock();
            quad=new Mesh{name="Gaussian vegetation instanced quad"};quad.vertices=new[]{new Vector3(-1,-1,0),new Vector3(1,-1,0),new Vector3(1,1,0),new Vector3(-1,1,0)};
            quad.uv=new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)};quad.triangles=new[]{0,1,2,0,2,3};
            // Shader expands each sigma-scaled instance to a three-sigma footprint.
            quad.bounds=new Bounds(Vector3.zero,Vector3.one*6);
            material=new Material(Resources.Load<Shader>("Shaders/GaussianVegetation")){enableInstancing=true};
        }
        void Update(){var camera=FrontierGame.Instance?.view;if(camera!=null)UpdateAround(camera.transform.position);}
        public void UpdateAround(Vector3 worldEye)
        {
            if(world==null||material==null)return;
            if(revision!=world.VegetationRevision){Cancel();meadow.Clear();woodland.Clear();visibleMeadow.Clear();visibleWoodland.Clear();builtAt=Vector3.positiveInfinity;revision=world.VegetationRevision;}
            bool active=world.TryVegetationFocus(worldEye,out var focus,out float altitude)&&altitude<1800&&altitude> -8;
            float target=active?1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(500,1800,altitude)):0;
            fade=Mathf.MoveTowards(fade,target,Time.deltaTime*2);
            if(active)
            {
                if(buildJob!=null&&(focus.position-buildingAt).sqrMagnitude>WoodlandRadius*WoodlandRadius)Cancel();
                if(buildJob==null&&(focus.position-builtAt).sqrMagnitude>RebuildDistance*RebuildDistance){buildingAt=focus.position;buildJob=StartCoroutine(BuildAt(focus,revision));}
            }
            else Cancel();
            var camera=FrontierGame.Instance?.view;if(camera==null||fade<=.001f)return;
            // Retain the previous complete neighborhood while replacement data builds.
            if(Time.time-lastCull>=.2f)
            {
                CullSort(meadow,visibleMeadow,camera,445);CullSort(woodland,visibleWoodland,camera,1880);lastCull=Time.time;
            }
            float wind=Mathf.Clamp(world.info.temperature>280?.32f:.2f,.08f,.45f);
            DrawBatches(visibleMeadow,camera,new Vector4(22,55,300,420),wind);
            DrawBatches(visibleWoodland,camera,new Vector4(170,240,1450,1850),wind);
        }
        void CullSort(List<Splat> source,List<Splat> output,Camera camera,float maxRange)
        {
            output.Clear();Vector3 eye=world.transform.InverseTransformPoint(camera.transform.position);float maxSqr=maxRange*maxRange;
            var planes=GeometryUtility.CalculateFrustumPlanes(camera);Vector3 lossy=world.transform.lossyScale;float maxScale=Mathf.Max(Mathf.Abs(lossy.x),Mathf.Abs(lossy.y),Mathf.Abs(lossy.z));
            foreach(var point in source)
            {
                if((point.position-eye).sqrMagnitude>=maxSqr)continue;
                Vector3 center=world.transform.TransformPoint(point.position);float radius=3*Mathf.Max(point.scale.x,point.scale.y,point.scale.z)*maxScale;bool visible=true;
                foreach(var plane in planes)if(plane.GetDistanceToPoint(center)<-radius){visible=false;break;}
                if(visible)output.Add(point);
            }
            Vector4 depth=(camera.worldToCameraMatrix*world.transform.localToWorldMatrix).GetRow(2);
            float ViewDepth(Splat point)=>depth.x*point.position.x+depth.y*point.position.y+depth.z*point.position.z;
            output.Sort((a,b)=>ViewDepth(a).CompareTo(ViewDepth(b)));
        }
        void DrawBatches(List<Splat> points,Camera camera,Vector4 range,float wind)
        {
            var matrix=world.transform.localToWorldMatrix;
            for(int start=0;start<points.Count;start+=BatchSize)
            {
                int count=Mathf.Min(BatchSize,points.Count-start);
                for(int i=0;i<count;i++){var p=points[start+i];Matrices[i]=matrix*Matrix4x4.TRS(p.position,p.rotation,p.scale);Tints[i]=p.tint;}
                block.Clear();block.SetVectorArray(TintId,Tints);block.SetVector(RangeId,range);block.SetFloat(FadeId,fade);block.SetFloat(WindId,wind);
                block.SetFloat(FogDensityId,RenderSettings.fog?RenderSettings.fogDensity:0);block.SetColor(FogColorId,RenderSettings.fogColor);
                Graphics.DrawMeshInstanced(quad,0,material,Matrices,count,block,ShadowCastingMode.Off,false,gameObject.layer,camera,LightProbeUsage.Off,null);
            }
        }
        IEnumerator BuildAt(VegetationSample focus,int generation)
        {
            yield return null;
            var nextMeadow=new List<Splat>(40000);var nextWoodland=new List<Splat>(60000);float started=Time.realtimeSinceStartup;int attempts=0;
            foreach(var cell in PlanetVegetationCells.Near(world.info.id,focus.up,7,MeadowRadius,20000))
            {
                if(++attempts%32==0&&Time.realtimeSinceStartup-started>.004f){yield return null;started=Time.realtimeSinceStartup;}
                var random=new System.Random(cell.Seed(world.info.seed));float R()=>(float)random.NextDouble();
                var sample=world.SampleVegetation(cell.Direction(.15f+R()*.7f,.15f+R()*.7f));
                if(!sample.valid||(sample.position-focus.position).sqrMagnitude>MeadowRadius*MeadowRadius)continue;
                Quaternion rotation=PlanetVegetationCells.Rotation(sample.up);float green=.065f+R()*.065f;Color grass=world.info.GrassPigment(sample.moisture);
                nextMeadow.Add(Point(cell,sample,rotation,sample.position+sample.up*.18f,new Vector3(3.2f,.22f,3.2f),grass,green*2,.75f));
                for(int k=0;k<2;k++)
                {
                    Vector3 offset=rotation*new Vector3((R()-.5f)*2,0,(R()-.5f)*2);float sx=.3f+R()*.35f,sy=.24f+R()*.2f;
                    nextMeadow.Add(Point(cell,sample,rotation,sample.position+sample.up*.35f+offset,new Vector3(sx,sy,sx),grass,green*2.7f,.8f));
                }
                if(nextMeadow.Count>=MaximumMeadowSplats)break;
            }
            foreach(var cell in PlanetVegetationCells.Near(world.info.id,focus.up,24,WoodlandRadius,36000))
            {
                if(++attempts%32==0&&Time.realtimeSinceStartup-started>.004f){yield return null;started=Time.realtimeSinceStartup;}
                var random=new System.Random(cell.Seed(world.info.seed)^0x5F3759DF);float R()=>(float)random.NextDouble();
                var sample=world.SampleVegetation(cell.Direction(.15f+R()*.7f,.15f+R()*.7f));
                if(!sample.valid||(sample.position-focus.position).sqrMagnitude>WoodlandRadius*WoodlandRadius)continue;
                Quaternion rotation=PlanetVegetationCells.Rotation(sample.up);float green=.065f+R()*.065f;Color grass=world.info.GrassPigment(sample.moisture);
                nextWoodland.Add(Point(cell,sample,rotation,sample.position+sample.up*.18f,new Vector3(13.68f,.35f,13.68f),grass,green*2,.42f));
                if(R()<Mathf.Max(.12f,sample.forestCover))for(int k=0;k<4;k++)
                {
                    float height=(4+k*2)*(1.2f-sample.exposure*.35f),width=4.3f-k*.72f;
                    float brightness=Mathf.Max(.017f+R()*.012f,Mathf.Max(.037f+R()*.022f,.014f+R()*.01f))*2;
                    nextWoodland.Add(Point(cell,sample,rotation,sample.position+sample.up*height,new Vector3(width,1.7f,width),world.info.FoliagePigment,brightness,.8f));
                }
                if(nextWoodland.Count+5>MaximumWoodlandSplats)break;
            }
            if(generation!=world.VegetationRevision){buildJob=null;yield break;}
            meadow.Clear();meadow.AddRange(nextMeadow);woodland.Clear();woodland.AddRange(nextWoodland);builtAt=focus.position;lastCull=-1;buildJob=null;
        }
        static Splat Point(WorldVegetationCell cell,VegetationSample sample,Quaternion rotation,Vector3 position,Vector3 scale,Color color,float brightness,float alpha)=>new Splat{root=sample.position,position=position,rotation=rotation,scale=scale,cellId=cell.ToString(),tint=new Vector4(color.r*brightness,color.g*brightness,color.b*brightness,alpha)};
        void Cancel(){if(buildJob!=null){StopCoroutine(buildJob);buildJob=null;}}
        void OnDestroy(){Cancel();if(quad!=null)Destroy(quad);if(material!=null)Destroy(material);}
    }
}


