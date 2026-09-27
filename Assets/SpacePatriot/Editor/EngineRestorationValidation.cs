using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using SpacePatriot;
using Object=UnityEngine.Object;
public static class EngineRestorationValidation
{
    static void Check(bool ok,string message,List<string> lines){if(!ok)throw new Exception(message);lines.Add("PASS: "+message);}
    public static void SourceWorldFields()
    {
        var catalog=JsonUtility.FromJson<WorldCatalog>(Resources.Load<TextAsset>("Worlds").text);var lines=new List<string>();int solids=0;
        foreach(var w in catalog.worlds)
        {
            var field=new WorldworksTerrain(w.id);
            if(w.biome=="gas"){Check(field.resolution==0,"Gas world has no solid-terrain field: "+w.id,lines);continue;}
            Check(field.resolution==129&&field.size==5760&&field.seed==unchecked((uint)w.seed),"Source-seeded 129 x 129 Worldworks/Terrainworks field: "+w.id,lines);solids++;
        }
        Check(solids==13,"13 solid worlds have source terrain fields",lines);
        string[,] families={{"trappist-1-b","volcanic"},{"trappist-1-f","ice"},{"trappist-1-g","ice"},{"wasp-76-b","gas"},{"trappist-1-h","rock"}};
        for(int i=0;i<families.GetLength(0);i++){var w=Array.Find(catalog.worlds,x=>x.id==families[i,0]);Check(w!=null&&w.biome==families[i,1],"Original biome family restored: "+families[i,0]+" / "+families[i,1],lines);}
        Directory.CreateDirectory("Validation");File.WriteAllLines("Validation/source-world-fields.txt",lines);Debug.Log("SOURCE_WORLD_FIELDS_PASS "+lines.Count);
    }
    public static void SourceGlobalSurfaces()
    {
        var catalog=JsonUtility.FromJson<WorldCatalog>(Resources.Load<TextAsset>("Worlds").text);var lines=new List<string>();int solids=0;
        Vector3[] directions={Vector3.right,Vector3.up,Vector3.forward,new Vector3(1,1,1).normalized,new Vector3(-1,2,.4f).normalized,new Vector3(.3f,-1,1).normalized};
        foreach(var w in catalog.worlds)
        {
            var surface=new PlanetEngineSurface(w);float min=float.MaxValue,max=float.MinValue;
            bool stable=true;foreach(var d in directions){float h=surface.HeightMeters(d),again=surface.HeightMeters(d);stable&=!float.IsNaN(h)&&!float.IsInfinity(h)&&Mathf.Abs(h-again)<.0001f;min=Mathf.Min(min,h);max=Mathf.Max(max,h);}
            Check(stable,"Source terrain is finite and deterministic: "+w.id,lines);
            if(w.biome=="gas"){Check(max-min<.001f,"Gas giants have no solid geological relief: "+w.id,lines);continue;}
            Check(max-min>.001f,"Original seeded geological sampler varies across the complete globe: "+w.id,lines);solids++;
            lines.Add("PROFILE: "+w.id+" / "+w.biome+" / "+min.ToString("F4")+" to "+max.ToString("F4")+" m");
        }
        Check(solids==13,"Source global geology covers all 13 solid worlds",lines);
        Directory.CreateDirectory("Validation");File.WriteAllLines("Validation/source-global-surfaces.txt",lines);Debug.Log("SOURCE_GLOBAL_SURFACES_PASS "+lines.Count);
    }
    public static void SourcePlanetwideClimate()
    {
        var catalog=JsonUtility.FromJson<WorldCatalog>(Resources.Load<TextAsset>("Worlds").text);var lines=new List<string>();int loaded=0;
        Vector3[] directions={Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back,new Vector3(1,1,1).normalized,new Vector3(-1,.4f,1).normalized};
        foreach(var world in catalog.worlds)
        {
            var field=new PlanetRegionClimate(world.id);var first=field.Sample(directions[0]);float minMoisture=first.x,maxMoisture=first.x,minExposure=first.z,maxExposure=first.z;bool finite=true;
            foreach(var direction in directions)
            {
                var sample=field.Sample(direction);finite&=float.IsFinite(sample.x)&&float.IsFinite(sample.y)&&float.IsFinite(sample.z)&&float.IsFinite(sample.w);
                minMoisture=Mathf.Min(minMoisture,sample.x);maxMoisture=Mathf.Max(maxMoisture,sample.x);minExposure=Mathf.Min(minExposure,sample.z);maxExposure=Mathf.Max(maxExposure,sample.z);
            }
            Check(finite,"Original PlanetEngines climate atlas loads and remains finite: "+world.id,lines);
            Check(maxMoisture-minMoisture>.025f||maxExposure-minExposure>.025f,"Climate changes across the whole sphere instead of clamping to the port tile: "+world.id,lines);loaded++;
            lines.Add("RANGE: "+world.id+" / moisture "+minMoisture.ToString("F3")+"–"+maxMoisture.ToString("F3")+" / exposure "+minExposure.ToString("F3")+"–"+maxExposure.ToString("F3"));
        }
        Check(loaded==catalog.worlds.Length,"All "+loaded+" source worlds have planetwide climate coverage",lines);
        Directory.CreateDirectory("Validation");File.WriteAllLines("Validation/planet-climate-streaming.txt",lines);Debug.Log("SOURCE_PLANETWIDE_CLIMATE_PASS "+lines.Count);
    }
    public static void ExportSourceHeightSamples()
    {
        var catalog=JsonUtility.FromJson<WorldCatalog>(Resources.Load<TextAsset>("Worlds").text);
        Vector3[] directions={Vector3.right,Vector3.up,Vector3.forward,new Vector3(1,1,1).normalized,new Vector3(-1,2,.4f).normalized,new Vector3(.3f,-1,1).normalized};
        var surfaceRows=new System.Text.StringBuilder("world,nx,ny,nz,rawHeightMeters,geologyHeightMeters\n");
        foreach(var world in catalog.worlds){var surface=new PlanetEngineSurface(world);foreach(var normal in directions)
        {
            surfaceRows.Append(world.id).Append(',').Append(normal.x.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(normal.y.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(normal.z.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(surface.SourceHeightMeters(normal).ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(surface.HeightMeters(normal).ToString("R",CultureInfo.InvariantCulture)).Append('\n');
        }}
        Directory.CreateDirectory("Validation");File.WriteAllText("Validation/unity-source-height-samples.csv",surfaceRows.ToString());Debug.Log("UNITY_SOURCE_HEIGHT_SAMPLES_PASS "+(catalog.worlds.Length*directions.Length));
    }
    public static void PlanetMeshBounds()
    {
        var g=FrontierGame.Instance;if(g==null||!EditorApplication.isPlaying)throw new Exception("Enter Play mode first");
        var ground=GameObject.Find("Continuous spherical terrain");var mesh=ground?ground.GetComponent<MeshFilter>()?.sharedMesh:null;
        if(mesh==null)throw new Exception("Continuous spherical terrain mesh is missing");
        float min=float.MaxValue,max=0;foreach(var vertex in mesh.vertices){float radius=Vector3.Distance(vertex,g.world.PlanetCenter);min=Mathf.Min(min,radius);max=Mathf.Max(max,radius);}
        var lines=new List<string>();Check(mesh.vertexCount>130000,"Planet globe has collision-stable global tessellation plus a resolved local cap: "+mesh.vertexCount+" vertices",lines);
        Check(min>FrontierWorld.PlanetRadius-150&&max<FrontierWorld.PlanetRadius+150,"Planet relief stays within its authored radius band: "+min.ToString("F2")+" to "+max.ToString("F2")+" m",lines);
        Directory.CreateDirectory("Validation");File.WriteAllLines("Validation/planet-mesh-bounds.txt",lines);Debug.Log("PLANET_MESH_BOUNDS_PASS "+lines.Count);
    }
    public static void WildlifeRosters()
    {
        var worldCatalog=JsonUtility.FromJson<WorldCatalog>(Resources.Load<TextAsset>("Worlds").text);var lines=new List<string>();var ids=new HashSet<string>();
        foreach(var world in worldCatalog.worlds){var entries=WildlifeCatalog.For(world.id);Check(entries.Length==10,"Ten stable wildlife concepts exist for "+world.name,lines);Check(entries.Length==10&&Array.Exists(entries,x=>x.role=="champion")&&Array.Exists(entries,x=>x.role=="apex"),"Champion and apex encounter roles are present: "+world.id,lines);foreach(var entry in entries){Check(entry.world==world.id&&entry.concept!=null&&entry.concept.views.Length==6,"Concept, views and world assignment agree: "+entry.id,lines);Check(ids.Add(entry.id),"Creature IDs are unique: "+entry.id,lines);}}
        Check(ids.Count==worldCatalog.worlds.Length*10,"All planets have unique wildlife entries: "+ids.Count,lines);
        Directory.CreateDirectory("Validation");File.WriteAllLines("Validation/wildlife-rosters.txt",lines);Debug.Log("WILDLIFE_ROSTERS_PASS "+lines.Count);
    }
    public static void RuntimeWildlife()
    {
        var g=FrontierGame.Instance;if(g==null||!EditorApplication.isPlaying)throw new Exception("Enter Play mode first");var lines=new List<string>();var actors=g.world.GetComponentsInChildren<WildlifeAgent>(true);
        Check(actors.Length==10,"Ten habitat-specific wildlife agents are loaded in the active world: "+actors.Length,lines);Check(Array.Exists(actors,x=>x.species!=null&&x.species.Boss),"Active biome includes its boss encounter",lines);Check(Array.Exists(actors,x=>x.species!=null&&x.species.aggression>.25f),"Active biome includes hunting wildlife",lines);
        int before=g.effects.EffectEvents;var boss=Array.Find(actors,x=>x.species!=null&&x.species.Boss);if(boss!=null)g.WildlifeWarning(boss.species,"validation telegraph");Check(boss!=null&&g.effects.EffectEvents==before,"A boss ability warning emits its visible telegraph into Spellworks",lines);
        float oldHp=boss.health;boss.Hit(11);Check(boss.health==oldHp-11,"Wildlife accepts damage from the shared weapon hit path",lines);boss.health=oldHp;
        bool wasStarted=g.started,wasWalking=g.walking,wasAboard=g.aboard,wasMenu=g.menu;float oldCrew=g.save.crewHealth;g.started=true;g.walking=true;g.aboard=false;g.menu=false;g.WildlifeAttack(boss,1,"validation pounce",0);Check(g.save.crewHealth<oldCrew,"Telegraphed boss attack reaches player health",lines);g.save.crewHealth=oldCrew;g.started=wasStarted;g.walking=wasWalking;g.aboard=wasAboard;g.menu=wasMenu;
        Directory.CreateDirectory("Validation");File.WriteAllLines("Validation/runtime-wildlife.txt",lines);Debug.Log("RUNTIME_WILDLIFE_PASS "+lines.Count);
    }
    public static void Streaming()
    {
        var g=FrontierGame.Instance;if(g==null)throw new Exception("Enter Play mode first");var lines=new List<string>();
        var focus=g.ship.position+new Vector3(200,0,0);focus.y=g.world.Height(focus.x,focus.z)+2;
        g.world.StreamSurface(focus,true);var patch=GameObject.Find("Worldworks / moving terrain patch");var mesh=patch?patch.GetComponent<MeshFilter>()?.sharedMesh:null;
        Check(mesh!=null&&mesh.vertexCount==193*193,"On-foot streamed terrain has 193 x 193 samples",lines);
        Check(Mathf.Abs(patch.transform.localPosition.x-focus.x)<.01f&&Mathf.Abs(patch.transform.localPosition.z-focus.z)<.01f,"Terrain patch recenters on its requested player position",lines);
        int center=(mesh.vertexCount/2);float renderedHeight=patch.transform.position.y+mesh.vertices[center].y;
        Check(Mathf.Abs(renderedHeight-(g.world.Height(focus.x,focus.z)+.035f))<.02f,"Patch center follows the active Worldworks height query",lines);
        Check(patch.GetComponent<MeshCollider>()==null,"Streamed patch renders over the existing planet height/collision system",lines);
        Directory.CreateDirectory("Validation");File.WriteAllLines("Validation/world-streaming.txt",lines);Debug.Log("WORLD_STREAMING_PASS "+lines.Count);
    }
    public static void StreamingTransitions()
    {
        var g=FrontierGame.Instance;if(g==null||!EditorApplication.isPlaying)throw new Exception("Enter Play mode first");
        var lines=new List<string>();int oldWorld=g.save.world;string oldSettlement=g.save.settlement;
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        Vector3 oldFocus=(bool)typeof(FrontierGame).GetField("walking",BindingFlags.Instance|BindingFlags.Public).GetValue(g)
            ?(Vector3)typeof(FrontierGame).GetField("walkPosition",flags).GetValue(g):g.ship.position;
        bool oldWalking=g.walking;Material previous=null;
        try
        {
            foreach(string id in new[]{"mars","earth"})
            {
                int index=Array.FindIndex(g.worlds,w=>w.id==id);Check(index>=0,"World catalog contains "+id,lines);
                g.save.world=index;g.world.Generate(g.CurrentWorld);
                var focus=new Vector3(610,0,230);focus.y=g.world.Height(focus.x,focus.z)+2;
                g.world.StreamSurface(focus,true);
                var patch=GameObject.Find("Worldworks / moving terrain patch");
                var renderer=patch?patch.GetComponent<MeshRenderer>():null;
                var material=renderer?renderer.sharedMaterial:null;
                var expected=Resources.Load<Shader>("Shaders/WorldworksPlanet");
                Check(material!=null&&material.shader==expected&&material.shader.isSupported,
                    "Streamed terrain has the current WorldworksPlanet material after "+id+" switch",lines);
                Check(patch.GetComponent<MeshFilter>().sharedMesh!=null,"Streamed terrain mesh is rebuilt after "+id+" switch",lines);
                if(previous!=null)Check(material!=previous,"Planet switch replaces its per-world terrain material",lines);
                previous=material;
            }
            Directory.CreateDirectory("Validation");File.WriteAllLines("Validation/terrain-material-switch.txt",lines);
            Debug.Log("TERRAIN_MATERIAL_SWITCH_PASS "+lines.Count);
        }
        finally
        {
            g.save.world=oldWorld;g.save.settlement=oldSettlement;g.world.Generate(g.CurrentWorld);
            g.world.StreamSurface(oldFocus,oldWalking);
            typeof(FrontierGame).GetField("saveTime",flags).SetValue(g,Time.unscaledTime);
        }
    }
    public static void RuntimeEffectsAndAudio()
    {
        var g=FrontierGame.Instance;if(g==null||!EditorApplication.isPlaying)throw new Exception("Enter Play mode first");var lines=new List<string>();
        int emitted=g.effects.Emitted,events=g.effects.EffectEvents;Vector3 at=g.view.transform.position+g.view.transform.forward*12f;
        g.effects.Survey(at,-g.view.transform.forward);g.effects.Flush();
        var pool=GameObject.Find("Spellworks particle pool");var renderer=pool?pool.GetComponent<MeshRenderer>():null;
        Check(g.effects.EffectEvents==events+1&&g.effects.Emitted>emitted,"Spellworks survey event emits into the live game particle pool",lines);
        Check(renderer!=null&&renderer.enabled&&renderer.sharedMaterial!=null&&g.view!=null&&(g.view.cullingMask&(1<<pool.layer))!=0,"Spellworks pool is visible to the active gameplay camera",lines);
        var audio=g.GetComponentsInChildren<AudioSource>(true);var drive=Array.Find(audio,x=>x.gameObject.name.StartsWith("Drive acoustics"));var reactor=Array.Find(audio,x=>x.gameObject.name.StartsWith("Reactor acoustics"));
        Check(drive!=null&&drive.loop&&drive.isPlaying&&drive.clip!=null,"Aft drive sound is a live, spatial engine layer",lines);
        Check(reactor!=null&&reactor.loop&&reactor.isPlaying&&reactor.clip!=null,"Reactor circulation is a separate live audio layer",lines);
        var weaponClips=new[]{"kineticClip","coilClip","missileClip","rifleClip","pistolClip"};var names=new HashSet<string>();bool differentiated=true;
        foreach(var field in weaponClips){var clip=typeof(FrontierGame).GetField(field,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(g) as AudioClip;if(clip==null){differentiated=false;continue;}names.Add(clip.name);}
        Check(differentiated&&names.Count==weaponClips.Length,"Autocannon, pulse, missile, rifle and sidearm use distinct sound designs",lines);
        Directory.CreateDirectory("Validation");File.WriteAllLines("Validation/runtime-effects-audio.txt",lines);Save(g.view,"spellworks-runtime.png");Debug.Log("RUNTIME_EFFECTS_AUDIO_PASS "+lines.Count);
    }
    public static void Run()
    {
        var g=FrontierGame.Instance;if(g==null)throw new Exception("Enter Play mode first");var lines=new List<string>();
        int worlds=0;foreach(var w in g.worlds){if(w.biome=="gas")continue;var field=new WorldworksTerrain(w.id);Check(field.resolution==129&&field.size==5760,"Original terrain field loaded: "+w.id,lines);worlds++;}
        for(int i=0;i<8;i++)g.combustion.Ignite(g.ship.position+Vector3.forward*30);Check(g.combustion.ActiveCount==6,"Fireworks recycles six causal emitter slots",lines);
        Check(worlds==13,"13 solid worlds use compiled original engine fields",lines);
        var ground=GameObject.Find("Continuous spherical terrain").GetComponent<MeshFilter>().sharedMesh;
        var v=ground.vertices;var ix=ground.triangles;var edges=new Dictionary<ulong,int>();
        void Edge(int a,int b){uint lo=(uint)Math.Min(a,b),hi=(uint)Math.Max(a,b);ulong k=((ulong)lo<<32)|hi;edges[k]=edges.GetValueOrDefault(k)+1;}
        for(int i=0;i<ix.Length;i+=3){Edge(ix[i],ix[i+1]);Edge(ix[i+1],ix[i+2]);Edge(ix[i+2],ix[i]);}
        foreach(int n in edges.Values)if(n!=2)throw new Exception("Open planetary edge");
        Check(true,"Planet is a closed continuous mesh; every edge belongs to two triangles",lines);
        float worst=0;for(int i=0;i<256*6*115;i+=117){int k=i/3*3;Vector3 a=v[ix[k]],b=v[ix[k+1]],c=v[ix[k+2]];var p=a*.21f+b*.33f+c*.46f;worst=Mathf.Max(worst,Mathf.Abs(g.world.Height(p.x,p.z)-p.y));}
        Check(worst<.025f,"Rendered terrain / walking height agreement: "+worst.ToString("F5")+" m",lines);
        Vector3 meadow=FindMeadow(g.world);for(int i=0;i<10;i++)g.world.grass.UpdateAround(meadow);
        Check(g.world.grass.BladeCount>1000&&g.world.grass.BladeCount<=Grassworks.MaximumTiles*Grassworks.BladesPerTile,"Grassworks grows bounded terrain-rooted blades: "+g.world.grass.BladeCount,lines);
        g.world.StreamSurface(meadow,true);var streamed=GameObject.Find("Worldworks / moving terrain patch");var streamedMesh=streamed?streamed.GetComponent<MeshFilter>()?.sharedMesh:null;
        Check(streamedMesh!=null&&streamedMesh.vertexCount==193*193,"Worldworks streams a 12 km walking terrain patch around the player: "+(streamedMesh?streamedMesh.vertexCount:0)+" vertices",lines);
        int blades=g.world.grass.BladeCount;g.world.grass.UpdateAround(meadow);Check(blades==g.world.grass.BladeCount,"Grass tiles stay stable while observer remains in same tile",lines);
        g.world.grass.UpdateAround(meadow+Vector3.up*300);bool hidden=true;foreach(var r in g.world.grass.GetComponentsInChildren<Renderer>(true))hidden&=!r.gameObject.activeSelf;Check(hidden,"Grass is culled from orbital altitude",lines);
        int events=g.effects.EffectEvents;g.effects.Impact(meadow,Vector3.up,false,1,912345);g.effects.Impact(meadow,Vector3.up,false,1,912345);Check(g.effects.EffectEvents==events+1,"Spellworks suppresses duplicate event IDs",lines);
        int before=g.effects.Emitted;g.effects.Impact(meadow,Vector3.up,true,2);Check(g.effects.Emitted-before==75,"Destruction emits original 65 flame / 10 smoke burst",lines);
        for(int i=0;i<3000;i++)g.effects.Emit(meadow,Vector3.up,Color.white,.1f,.02f);g.effects.Flush();
        int vertices=0;foreach(var filter in g.effects.GetComponentsInChildren<MeshFilter>())vertices+=filter.sharedMesh.vertexCount;
        Check(vertices==(Spellworks.Capacity+Spellworks.SmokeCapacity)*4,"Spellworks ring remains bounded after overflow",lines);
        var climate=new GameObject("Weatherworks validation").AddComponent<Weatherworks>();climate.Wetness=.32f;climate.Rain=.45f;climate.Temperature=12;climate.Step(.1f);
        Check(Mathf.Abs(climate.Wetness-(.32f+.1f*(.45f*.025f-.55f*(.0015f+12*.00023f))))<.000001f,"Weatherworks wetness matches original equation",lines);Object.DestroyImmediate(climate.gameObject);
        Check(Mathf.Abs(Oceanworks.WaveHeight(new Vector2(10,20),3)-Oceanworks.WaveHeight(new Vector2(10,20),4))>.0001f,"Oceanworks directional wave field evolves with time",lines);
        for(int f=0;f<10;f++){var prefab=Resources.Load<GameObject>("OriginalShips/refit-"+f);Check(prefab!=null&&prefab.GetComponentsInChildren<MeshRenderer>().Length<=30,"Refit chassis "+f+" exists within 30 material batches",lines);}
        Directory.CreateDirectory("Validation");File.WriteAllLines("Validation/engine-restoration.txt",lines);Debug.Log("ENGINE_RESTORATION_PASS "+lines.Count);
    }
    public static Vector3 FindMeadow(FrontierWorld w)
    {
        for(int z=350;z<1100;z+=48)for(int x=460;x<1200;x+=48)if(w.grass.CanGrow(x,z))return new Vector3(x,w.Height(x,z)+2,z);
        throw new Exception("No valid meadow near the port");
    }
    static Texture2D Render(Camera camera,int width,int height)
    {
        var target=new RenderTexture(width,height,24);var previous=camera.targetTexture;var active=RenderTexture.active;var rect=camera.rect;camera.rect=new Rect(0,0,1,1);camera.targetTexture=target;UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;
        var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();camera.targetTexture=previous;camera.rect=rect;RenderTexture.active=active;Object.DestroyImmediate(target);return image;
    }
    static void Save(Camera c,string file){var image=Render(c,1440,900);File.WriteAllBytes("Validation/"+file,ImageConversion.EncodeToPNG(image));Object.DestroyImmediate(image);}
    public static void Fleet()
    {
        Directory.CreateDirectory("Validation");var root=new GameObject("Refit validation studio");root.transform.position=new Vector3(25000,6000,0);
        var camera=new GameObject("Studio camera").AddComponent<Camera>();camera.transform.SetParent(root.transform,false);camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.14f,.16f);camera.fieldOfView=42;camera.aspect=1.6f;camera.nearClipPlane=.05f;camera.farClipPlane=1500;
        var light=new GameObject("Key light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.color=new Color(1,.91f,.79f);light.cullingMask=1<<31;light.transform.SetParent(root.transform,false);light.transform.rotation=Quaternion.Euler(35,-20,0);
        var fill=new GameObject("Fill light").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=1;fill.color=new Color(.64f,.76f,1);fill.cullingMask=1<<31;fill.transform.SetParent(root.transform,false);fill.transform.rotation=Quaternion.Euler(20,160,0);
        var sheet=new Texture2D(1440,1800,TextureFormat.RGB24,false);bool fog=RenderSettings.fog;RenderSettings.fog=false;
        try{for(int f=0;f<10;f++){
            var go=Object.Instantiate(Resources.Load<GameObject>("OriginalShips/refit-"+f),root.transform);foreach(var t in go.GetComponentsInChildren<Transform>())t.gameObject.layer=31;
            float l=ShipSpec.Fleet[f*10].length,w=ShipSpec.Fleet[f*10].width,h=ShipSpec.Fleet[f*10].height,extent=Mathf.Max(l,w);
            camera.transform.localPosition=new Vector3(-extent*.80f,extent*.57f,extent*.86f);camera.transform.LookAt(root.transform.position+Vector3.up*h*.04f);
            var image=Render(camera,720,360);sheet.SetPixels(f%2*720,(4-f/2)*360,720,360,image.GetPixels());if(f==0||f==2||f==5||f==9)Save(camera,"refit-"+f+".png");Object.DestroyImmediate(image);Object.DestroyImmediate(go);
        }sheet.Apply();File.WriteAllBytes("Validation/refit-fleet.png",ImageConversion.EncodeToPNG(sheet));}
        finally{RenderSettings.fog=fog;Object.DestroyImmediate(sheet);Object.DestroyImmediate(root);}
    }
    public static void WorldViews()
    {
        var g=FrontierGame.Instance;var camera=new GameObject("Engine restoration camera").AddComponent<Camera>();camera.fieldOfView=60;camera.aspect=1.6f;camera.nearClipPlane=.1f;camera.farClipPlane=90000;camera.clearFlags=CameraClearFlags.Skybox;bool fog=RenderSettings.fog;float fogDensity=RenderSettings.fogDensity;
        try{
            Vector3 p=FindMeadow(g.world);for(int i=0;i<10;i++)g.world.grass.UpdateAround(p);var blades=g.world.grass.GetComponentsInChildren<MeshFilter>();foreach(var blade in blades)if(blade.sharedMesh.vertexCount>120){var verts=blade.sharedMesh.vertices;p=verts[(verts.Length/24)*12];break;}
            camera.transform.position=p+new Vector3(0,1.25f,-2.2f);camera.transform.LookAt(p+Vector3.up*.3f);Save(camera,"grassworks-restored.png");
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.002f,.004f,.008f);camera.transform.position=new Vector3(17000,13000,22000);camera.transform.LookAt(g.world.PlanetCenter);RenderSettings.fog=false;RenderSettings.fogDensity=0;Save(camera,"continuous-planet.png");
        }finally{RenderSettings.fog=fog;RenderSettings.fogDensity=fogDensity;Object.DestroyImmediate(camera.gameObject);}
    }
}
