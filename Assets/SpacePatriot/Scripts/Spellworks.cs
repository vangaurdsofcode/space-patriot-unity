using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace SpacePatriot
{
    // Native port of Spellworks ParticlePool: fixed rings, original age/drag/gravity
    // integration, velocity-aligned sparks and six analytic billboard styles.
    public sealed class Spellworks : MonoBehaviour
    {
        public const int Capacity=2048,SmokeCapacity=512;
        public int Emitted {get;private set;}
        public int EffectEvents {get;private set;}
        sealed class Pool
        {
            public Mesh mesh;public Material material;public int capacity,cursor;public bool dirty;
            public Vector3[] positions;public Color[] colors;public List<Vector4> velocity,life,style;
            public Pool(Transform root,int count,bool smoke)
            {
                capacity=count;positions=new Vector3[count*4];colors=new Color[count*4];velocity=new(count*4);life=new(count*4);style=new(count*4);var uv=new Vector2[count*4];var indices=new int[count*6];
                for(int i=0;i<count;i++){for(int j=0;j<4;j++){int k=i*4+j;uv[k]=new Vector2(j==0||j==3?-.5f:.5f,j<2?-.5f:.5f);velocity.Add(Vector4.zero);life.Add(new Vector4(-10000,1,0,0));style.Add(Vector4.zero);}int q=i*6,a=i*4;indices[q]=a;indices[q+1]=a+1;indices[q+2]=a+2;indices[q+3]=a;indices[q+4]=a+2;indices[q+5]=a+3;}
                mesh=new Mesh{name=smoke?"Spellworks smoke pool":"Spellworks particle pool"};mesh.MarkDynamic();mesh.vertices=positions;mesh.uv=uv;mesh.triangles=indices;mesh.bounds=new Bounds(Vector3.zero,Vector3.one*150000);
                material=new Material(Resources.Load<Shader>("Shaders/Spellworks"));material.SetFloat("_DstBlend",smoke?(float)BlendMode.OneMinusSrcAlpha:(float)BlendMode.One);
                var go=new GameObject(mesh.name);go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;dirty=true;
            }
            public void Emit(Vector3 p,Vector3 v,Color c,float start,float duration,float size,int type,float gravity,float drag,float seed)
            {int slot=cursor++%capacity;for(int j=0;j<4;j++){int k=slot*4+j;positions[k]=p;colors[k]=c;velocity[k]=v;life[k]=new Vector4(start,duration,size,seed);style[k]=new Vector4(type,gravity,drag,seed*3-1.5f);}dirty=true;}
            public void Flush(){if(!dirty)return;mesh.vertices=positions;mesh.colors=colors;mesh.SetUVs(1,velocity);mesh.SetUVs(2,life);mesh.SetUVs(3,style);mesh.bounds=new Bounds(Vector3.zero,Vector3.one*150000);dirty=false;}
            public void Dispose(){Object.Destroy(mesh);Object.Destroy(material);}
        }
        Pool particles,smoke;
        readonly Queue<ulong> eventQueue=new();readonly HashSet<ulong> eventIds=new();
        void Awake(){particles=new Pool(transform,Capacity,false);smoke=new Pool(transform,SmokeCapacity,true);}
        public void Emit(Vector3 p,Vector3 velocity,Color color,float duration,float size,int type=1,float gravity=4,float drag=.9f)
        {(type==2?smoke:particles).Emit(p,velocity,color,Time.time,duration,size,type,gravity,drag,Random.value);Emitted++;}
        public void Impact(Vector3 p,Vector3 normal,bool explosive=false,float scale=1,ulong eventId=0)
        {
            if(eventId!=0){if(!eventIds.Add(eventId))return;eventQueue.Enqueue(eventId);if(eventQueue.Count>512)eventIds.Remove(eventQueue.Dequeue());}
            EffectEvents++;int count=explosive?65:9;
            for(int i=0;i<count;i++){Vector3 v=(Random.onUnitSphere+normal*.45f).normalized*Random.Range(explosive?4:1,explosive?14:4)*scale;
                Emit(p,v,new Color(Random.Range(.72f,.97f),Random.Range(.08f,.26f),.008f)*2,Random.Range(explosive?.4f:.08f,explosive?1.1f:.22f),Random.Range(explosive?.12f:.018f,explosive?.42f:.04f)*Mathf.Sqrt(scale),explosive?4:1);}
            for(int i=0;i<(explosive?10:3);i++)Emit(p,Vector3.up*Random.Range(.3f,2)+Random.insideUnitSphere*.4f,new Color(.11f,.09f,.065f),explosive?2:.45f,explosive?.8f:.12f,2,-.1f,.9f);
        }
        public void Muzzle(Vector3 p,Vector3 direction,bool laser=false)
        {for(int i=0;i<4;i++)Emit(p,direction*Random.Range(2,6)+Random.insideUnitSphere*.5f,laser?new Color(2,.03f,.01f):new Color(2,1.1f,.35f),.075f,.18f,5,0,2);}
        public void Survey(Vector3 p)=>Survey(p,Vector3.up);
        public void Survey(Vector3 p,Vector3 normal)
        {
            EffectEvents++;var axis=normal.sqrMagnitude>.01f?normal.normalized:Vector3.up;var reference=Mathf.Abs(Vector3.Dot(axis,Vector3.up))>.92f?Vector3.forward:Vector3.up;
            var right=Vector3.Cross(reference,axis).normalized;var tangent=Vector3.Cross(axis,right).normalized;Color scan=new Color(.18f,1.45f,1.12f);
            for(int i=0;i<64;i++){float a=i*Mathf.PI*2/64;var d=right*Mathf.Cos(a)+tangent*Mathf.Sin(a);Emit(p+d*.18f,d*3.2f+axis*.35f,scan,1.25f,.12f,0,0,1.1f);}
            for(int i=0;i<12;i++){float a=i*Mathf.PI*2/12;var d=right*Mathf.Cos(a)+tangent*Mathf.Sin(a);Emit(p+d*.35f,axis*Random.Range(1.1f,2.4f)+d*Random.Range(.25f,.7f),new Color(.35f,.85f,1.4f),.8f,.08f,5,0,1.4f);}
        }
        public void Flush(){if(particles==null||smoke==null)return;particles.Flush();smoke.Flush();}
        void LateUpdate()=>Flush();
        void OnDestroy(){particles?.Dispose();smoke?.Dispose();}
    }
}
