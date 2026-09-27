using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpacePatriot
{
    // Dimensioned modules share meshes and materials. Silhouettes are authored in metres.
    public static class IndustrialArt
    {
        static readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
        static readonly Dictionary<string,Mesh> shapes=new Dictionary<string,Mesh>();
        static Texture2D wear;
        public static Material Metal(string name,Color color,float metallic=.65f,float rough=.56f)
        {
            if(materials.TryGetValue(name,out var found))return found;
            if(wear==null)
            {
                wear=new Texture2D(128,128,TextureFormat.RGBA32,true);wear.name="Brushed steel / edge wear";
                var rng=new System.Random(742);var pixels=new Color[128*128];
                for(int y=0;y<128;y++)for(int x=0;x<128;x++)
                { float v=.76f+(float)rng.NextDouble()*.17f+(y%17==0?.06f:0);if(x%31<1)v*=.65f;pixels[y*128+x]=new Color(v,v,v,1); }
                wear.SetPixels(pixels);wear.Apply();wear.wrapMode=TextureWrapMode.Repeat;
            }
            var m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};
            m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",1-rough);
            m.SetTexture("_BaseMap",wear);
            if(metallic>.3f)
            {var n=Resources.Load<Texture2D>("Surfaces/hull_normal");if(n!=null){m.SetTexture("_BumpMap",n);m.SetFloat("_BumpScale",.6f);m.EnableKeyword("_NORMALMAP");}}
            if(name=="Deck steel")
            {m.SetTexture("_BaseMap",Resources.Load<Texture2D>("Surfaces/plate_albedo"));m.SetTexture("_BumpMap",Resources.Load<Texture2D>("Surfaces/plate_normal"));m.SetColor("_BaseColor",new Color(1.1f,1.15f,1.14f));}
            if(name=="STRIDER worn enamel")
            {m.SetTexture("_BaseMap",Resources.Load<Texture2D>("Surfaces/hull_albedo"));m.SetColor("_BaseColor",new Color(1.7f,1.7f,1.5f));}
            if(name=="Roof oxide"||name=="Oxidized fasteners")m.SetTexture("_BaseMap",Resources.Load<Texture2D>("Surfaces/steel_albedo"));
            if(name.EndsWith("terrain")||name.EndsWith("stone"))
            {m.SetTexture("_BaseMap",Resources.Load<Texture2D>("Surfaces/terrain_albedo"));m.SetTexture("_BumpMap",Resources.Load<Texture2D>("Surfaces/terrain_normal"));m.SetFloat("_BumpScale",1.2f);m.EnableKeyword("_NORMALMAP");m.SetColor("_BaseColor",color*2.4f);}
            materials[name]=m;return m;
        }
        public static Material Glow(string name,Color c,float strength=2)
        {
            var m=Metal(name,c,0,.5f);m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",c*strength);return m;
        }
        public static Material Steel=>Metal("Graphite structure",new Color(.14f,.18f,.19f));
        public static Material Dark=>Metal("Rubber and carbon",new Color(.045f,.06f,.065f),.05f,.92f);
        public static Material Bone=>Metal("Ceramic insulation",new Color(.54f,.56f,.51f),.1f,.82f);
        public static Material Orange=>Metal("Safety ochre",new Color(.77f,.38f,.11f),.25f,.72f);
        public static Material WarmLight=>Glow("Warm fluorescent",new Color(1,.83f,.55f),2.5f);
        public static Material Cyan=>Glow("Instrument phosphor",new Color(.26f,.72f,.68f),1.3f);
        public static Transform Root(string name,Transform parent,Vector3 pos=default)
        { var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=pos;return t; }
        static Mesh Shape(List<Vector3[]> faces,string name)
        {
            var verts=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            foreach(var f in faces)
            {
                int n=verts.Count;Vector3 normal=Vector3.Cross(f[1]-f[0],f[2]-f[0]).normalized;
                foreach(var v in f){verts.Add(v);uv.Add(Mathf.Abs(normal.y)>.6f?new Vector2(v.x,v.z):Mathf.Abs(normal.x)>.6f?new Vector2(v.z,v.y):new Vector2(v.x,v.y));}
                for(int i=1;i<f.Length-1;i++){tris.Add(n);tris.Add(n+i);tris.Add(n+i+1);}
            }
            var m=new Mesh{name=name};m.SetVertices(verts);m.SetUVs(0,uv);m.SetTriangles(tris,0);m.RecalculateNormals();m.RecalculateTangents();m.RecalculateBounds();return m;
        }
        public static Mesh HullMesh(float[] z,float[] widths,float[] heights,float[] offsets,string name)
        {
            if(shapes.TryGetValue(name,out var cached)&&cached!=null)return cached;
            var rings=new List<Vector3[]>();
            for(int r=0;r<z.Length;r++)
            {
                float w=widths[r]/2,h=heights[r]/2,b=Mathf.Min(.12f,Mathf.Min(w,h)*.28f),o=offsets[r];
                rings.Add(new[]{new Vector3(-w+b,-h+o,z[r]),new Vector3(w-b,-h+o,z[r]),new Vector3(w,-h+b+o,z[r]),new Vector3(w,h-b+o,z[r]),new Vector3(w-b,h+o,z[r]),new Vector3(-w+b,h+o,z[r]),new Vector3(-w,h-b+o,z[r]),new Vector3(-w,-h+b+o,z[r])});
            }
            var faces=new List<Vector3[]>();var back=(Vector3[])rings[0].Clone();System.Array.Reverse(back);faces.Add(back);faces.Add(rings[rings.Count-1]);
            for(int r=0;r<rings.Count-1;r++)for(int j=0;j<8;j++)
            { int k=(j+1)%8;faces.Add(new[]{rings[r][j],rings[r][k],rings[r+1][k],rings[r+1][j]}); }
            var built=Shape(faces,name);shapes[name]=built;return built;
        }
        public static GameObject MeshObject(string name,Transform parent,Mesh mesh,Material material,Vector3 p,Vector3 scale)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localScale=scale;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;return go;
        }
        public static GameObject Box(string name,Transform parent,Vector3 p,Vector3 scale,Material material,bool collider=false)
        {
            float b=Mathf.Min(.065f,Mathf.Min(scale.x,Mathf.Min(scale.y,scale.z))*.12f);float z=scale.z*.5f;
            string key="Panel "+scale.x.ToString("F3")+"/"+scale.y.ToString("F3")+"/"+scale.z.ToString("F3");
            var mesh=HullMesh(new[]{-z,-z+b,z-b,z},new[]{scale.x-b*2,scale.x,scale.x,scale.x-b*2},new[]{scale.y-b*2,scale.y,scale.y,scale.y-b*2},new[]{0f,0,0,0},key);
            // Box geometry is baked into the mesh; the Transform remains unit scale.
            // A default BoxCollider is only 1 m across, regardless of the visible deck.
            var go=MeshObject(name,parent,mesh,material,p,Vector3.one);if(collider)go.AddComponent<BoxCollider>().size=scale;return go;
        }
        public static GameObject Cylinder(string name,Transform parent,Vector3 p,float radius,float length,Material mat,bool horizontal=false)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localScale=new Vector3(radius*2,length/2,radius*2);
            if(horizontal)go.transform.localRotation=Quaternion.Euler(90,0,0);Object.Destroy(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=mat;return go;
        }
        public static void Beam(Transform parent,Vector3 a,Vector3 b,float width,Material mat)
        { var g=Box("Welded brace",parent,(a+b)/2,new Vector3(width,width,Vector3.Distance(a,b)),mat);g.transform.localRotation=Quaternion.LookRotation(b-a); }
        public static TextMesh Label(Transform parent,string text,Vector3 p,float size,Color color,Vector3 rotation=default)
        {
            var go=new GameObject(text);go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localEulerAngles=rotation;
            var tm=go.AddComponent<TextMesh>();tm.text=text;tm.fontSize=64;tm.characterSize=size;tm.color=color;tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;return tm;
        }
        public static void Strip(Transform p,Vector3 pos,Vector3 scale,Material mat)=>Box("Inlaid guide light",p,pos,scale,mat);
        public static Transform Ship(Transform parent,int index,out Transform cabin,out Transform engines)
        {
            var spec=ShipSpec.Fleet[index];var root=Root(spec.name,parent);var ext=Root("Exterior hull",root);cabin=Root("Pressure cabin",root);engines=Root("Drive plumes",root);
            var paint=Metal(spec.name+" worn enamel",spec.paint,.5f,.6f);var rust=Metal("Oxidized fasteners",new Color(.31f,.19f,.12f),.4f,.8f);
            float breadth=index==2?1.65f:index==1?.78f:1;
            MeshObject("Riveted pressure hull",ext,HullMesh(new[]{-6f,-4,1,4,6},new[]{2.6f,4.3f,4.3f,2.5f,.4f},new[]{1.6f,2.8f,2.6f,1.4f,.3f},new[]{0f,0,0,-.1f,-.5f},"Meridian pressure shell"),paint,Vector3.zero,new Vector3(breadth,1,index==2?1.22f:1));
            Box("Ventral keel",ext,new Vector3(0,-1.2f,-.3f),new Vector3(1.8f*breadth,.6f,8.2f),Steel);
            for(int side=-1;side<=1;side+=2)
            {
                var wing=MeshObject("Swept equipment outrigger",ext,HullMesh(new[]{-4f,-1,2.3f},new[]{3f,4,.5f},new[]{.5f,.7f,.25f},new[]{0f,0,0},"Swept spar"),paint,new Vector3(side*3.1f*breadth,-.5f,-.8f),Vector3.one);
                wing.transform.localRotation=Quaternion.Euler(0,side*-12,side*-7);
                int driveCount=index==2?2:1;
                for(int j=0;j<driveCount;j++)
                {
                    float x=side*(3.9f*breadth+j*1.7f);float z=-3.2f+j;
                    Box("Ducted drive armor",ext,new Vector3(x,0,z),new Vector3(1.8f,1.65f,6.5f),paint);
                    Cylinder("Drive throat",ext,new Vector3(x,0,z-3.4f),.69f,.6f,Steel,true);
                    Cylinder("Deep nozzle",ext,new Vector3(x,0,z-3.73f),.48f,.05f,Dark,true);
                    var plume=Cylinder("Ion exhaust",engines,new Vector3(x,0,z-3.8f),.34f,.12f,Glow("Engine ice",new Color(.47f,.76f,.93f),4),true);
                    for(int k=0;k<6;k++)Box("Heat exchanger fin",ext,new Vector3(x,.83f,z-1.6f+k*.46f),new Vector3(1.55f,.14f,.13f),Steel);
                    Strip(ext,new Vector3(x,.25f,z+3.3f),new Vector3(.95f,.2f,.07f),WarmLight);
                    Box("Engine warning plate",ext,new Vector3(x,.85f,z+1.3f),new Vector3(1.2f,.05f,.65f),Orange);
                }
                for(int row=0;row<6;row++)
                {
                    Box("Overlapping service panel",ext,new Vector3(side*1.95f*breadth,.35f,-3.5f+row*1.1f),new Vector3(.18f,1.4f,.91f),row%3==0?Steel:paint);
                    Box("Latch",ext,new Vector3(side*2.08f*breadth,.75f,-3.3f+row*1.1f),new Vector3(.08f,.11f,.19f),rust);
                }
                Beam(ext,new Vector3(side*1.1f,-1,-3),new Vector3(side*2.3f,-2.4f,-3),.25f,Steel);
                Box("Landing shoe",ext,new Vector3(side*2.3f,-2.5f,-3),new Vector3(1.3f,.3f,1.7f),Dark);
                Box("Stenciled band",ext,new Vector3(side*1.2f*breadth,1.32f,-1),new Vector3(.3f,.06f,4.5f),Orange);
                var tail=Box("Vertical stabilizer",ext,new Vector3(side*2.1f*breadth,1.9f,-4.5f),new Vector3(.18f,2.5f,2.2f),paint);tail.transform.localRotation=Quaternion.Euler(15,0,side*-18);
            }
            Box("Nose landing strut",ext,new Vector3(0,-1.9f,3),new Vector3(.3f,1.5f,.3f),Steel);
            Box("Forward landing shoe",ext,new Vector3(0,-2.5f,3),new Vector3(1.2f,.3f,1.6f),Dark);
            // Cockpit framing is geometry, with no image overlay or billboard canopy.
            Box("Cabin sole",cabin,new Vector3(0,.0f,1.5f),new Vector3(2.3f,.2f,5.1f),Dark);
            Box("Pressure bulkhead",cabin,new Vector3(0,1.3f,-.8f),new Vector3(2.25f,2.6f,.14f),Steel);
            Box("Pilot seat",cabin,new Vector3(0,.45f,.65f),new Vector3(.9f,.25f,1.1f),Dark);
            Box("Pilot seat back",cabin,new Vector3(0,.95f,.12f),new Vector3(.9f,1.25f,.22f),Dark);
            for(int side=-1;side<=1;side+=2)
            {
                Beam(cabin,new Vector3(side*1.1f,.4f,3.2f),new Vector3(side*.9f,2.8f,2),.085f,Steel);
                Beam(cabin,new Vector3(side*.9f,2.8f,2),new Vector3(side*.9f,2.8f,-.7f),.1f,Steel);
                Box("Side console",cabin,new Vector3(side*.94f,.65f,1.5f),new Vector3(.45f,.4f,2.3f),Steel);
                for(int i=0;i<5;i++)Box("Mechanical switch",cabin,new Vector3(side*.95f,.91f,.9f+i*.2f),new Vector3(.12f,.06f,.06f),i==0?Orange:Bone);
            }
            Beam(cabin,new Vector3(-.9f,2.8f,2),new Vector3(.9f,2.8f,2),.1f,Steel);
            var glazing=Metal("Laminated canopy glass",new Color(.13f,.23f,.25f,.26f),.15f,.13f);
            glazing.SetFloat("_Surface",1);glazing.SetFloat("_ZWrite",0);glazing.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);glazing.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);glazing.SetFloat("_Cull",0);glazing.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");glazing.renderQueue=3000;
            MeshObject("Pressure glazing",cabin,HullMesh(new[]{-.75f,1.95f,3.55f},new[]{2.1f,1.9f,1.35f},new[]{2.2f,2.2f,.3f},new[]{1.6f,1.6f,.95f},"Canopy panes"),glazing,Vector3.zero,Vector3.one);
            Beam(cabin,new Vector3(-.68f,1.1f,3.55f),new Vector3(.68f,1.1f,3.55f),.09f,Steel);
            for(int side=-1;side<=1;side+=2)
            {
                Beam(cabin,new Vector3(side*.68f,1.1f,3.55f),new Vector3(side*.9f,2.7f,1.95f),.09f,Steel);
                for(int bolt=0;bolt<4;bolt++)Box("Hull fastening",ext,new Vector3(side*(1.8f*breadth),1.03f,-3+bolt),new Vector3(.13f,.13f,.13f),Bone);
            }
            Label(ext,"07",new Vector3(0,1.5f,-2.4f),.22f,new Color(.8f,.79f,.62f),new Vector3(90,0,0));
            Box("Instrument cowl",cabin,new Vector3(0,.74f,2.8f),new Vector3(2.15f,.38f,.65f),Steel);
            for(int i=-1;i<=1;i++)
            {
                Box("Inset display bezel",cabin,new Vector3(i*.62f,.92f,2.58f),new Vector3(.53f,.32f,.12f),Dark);
                Box("Phosphor instrument",cabin,new Vector3(i*.62f,.94f,2.51f),new Vector3(.44f,.2f,.015f),Glow("Dark instrument screen",new Color(.012f,.052f,.047f),.4f));
                var display=Label(cabin,i<0?"VELOCITY\n000 m/s":i==0?"MC–12\nSYSTEMS NOMINAL":"FUEL 100\nHULL 100",new Vector3(i*.62f,.94f,2.485f),.029f,new Color(.42f,.91f,.79f),new Vector3(0,180,0));display.name=i<0?"Velocity readout":i==0?"Systems readout":"Service readout";
                for(int tick=0;tick<5;tick++)Box("Instrument scale",cabin,new Vector3(i*.62f-.14f+tick*.07f,.865f,2.48f),new Vector3(.035f,.009f,.006f),Cyan);
            }
            Box("Control stick base",cabin,new Vector3(.35f,.28f,1.7f),new Vector3(.3f,.13f,.4f),Steel);
            var stick=Box("Flight control grip",cabin,new Vector3(.35f,.58f,1.7f),new Vector3(.1f,.5f,.13f),Dark);stick.transform.localRotation=Quaternion.Euler(-12,0,-7);
            Box("Thumb firing switch",cabin,new Vector3(.35f,.83f,1.65f),new Vector3(.08f,.06f,.09f),Orange);
            for(int side=-1;side<=1;side+=2)
            {Box("Rudder pedal",cabin,new Vector3(side*.3f,.16f,2.2f),new Vector3(.28f,.11f,.5f),Steel);Cylinder("Pressure conduit",cabin,new Vector3(side*1.04f,1.1f,1),.038f,2.7f,Bone,true);}
            Label(cabin,"MC / PRESSURE   101.3",new Vector3(0,.7f,2.39f),.025f,new Color(.7f,.75f,.67f),new Vector3(0,180,0));
            for(int i=0;i<3;i++)Box("Recessed dorsal hatch",ext,new Vector3(0,1.42f,-3.4f+i),new Vector3(1.3f,.1f,.65f),Steel);
            if(index==2)for(int side=-1;side<=1;side+=2)for(int i=0;i<3;i++)
            { Box("External freight pod",ext,new Vector3(side*3.3f,.3f,-3+i*2.5f),new Vector3(1.65f,2.4f,2.2f),i%2==0?Orange:Bone); }
            // Cabin remains visible from outside; hull forward roof is hidden only in first-person.
            return root;
        }
        public static void Crate(Transform parent,Vector3 pos,int kind=0)
        {
            var c=Root("Cargo container",parent,pos);var mat=kind==0?Orange:Bone;
            Box("Container shell",c,new Vector3(0,.65f,0),new Vector3(1.8f,1.3f,2.7f),mat,true);
            for(int side=-1;side<=1;side+=2)for(int i=0;i<5;i++)Box("Pressed corrugation",c,new Vector3(side*.92f,.65f,-1+i*.5f),new Vector3(.06f,1.1f,.09f),Steel);
            Box("Lid reinforcement",c,new Vector3(0,1.34f,0),new Vector3(1.9f,.11f,.25f),Steel);
        }
        public static void Terminal(Transform parent,Vector3 pos,string title,Material color)
        {
            var root=Root(title,parent,pos);
            Box("Bolted pedestal",root,new Vector3(0,.7f,0),new Vector3(.72f,1.4f,.55f),Steel,true);
            var panel=Box("Service terminal",root,new Vector3(0,1.5f,0),new Vector3(1.3f,.8f,.25f),Steel);
            Box("Inset screen",root,new Vector3(0,1.52f,-.15f),new Vector3(1.1f,.51f,.03f),color);
            Label(root,"PORT 07 / SERVICE\n"+title+"\nREADY",new Vector3(0,1.52f,-.18f),.006f,new Color(.045f,.08f,.075f),Vector3.zero);
            Label(root,title,new Vector3(0,2.2f,0),.014f,new Color(.86f,.83f,.72f),Vector3.zero);
        }
    }
}

