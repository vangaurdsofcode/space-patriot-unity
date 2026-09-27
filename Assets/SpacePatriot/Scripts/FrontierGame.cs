using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using static SpacePatriot.IndustrialArt;

namespace SpacePatriot
{
    public partial class FrontierGame : MonoBehaviour
    {
        public static FrontierGame Instance;
        public SaveData save;
        public WorldInfo[] worlds;
        public CaseFile[] cases;
        public FrontierWorld world;
        public Camera view;
        public Spellworks effects;
        public Fireworks combustion;
        public Transform ship,cabin,engines;
        Transform exterior;
        TextMesh velocityDisplay,serviceDisplay;
        Vector3 velocity,walkPosition,walkForward=Vector3.forward;
        public bool flying,walking,cockpit,started,menu=true,dead;
        public string page="overview",toast="",reportTitle="",reportText="";
        public float speed,heat,shield=100,throttle,travelFade;
        float yaw,pitch,roll,walkYaw=0,walkPitch,toastTime,fireTime,hitTime,saveTime;
        bool docking,jumping;
        Place landingTarget;
        bool approachEntry;
        int selectedWorld=3,selectedCase,selectedShip;
        string targetedCase="water";
        AudioSource ambient,powerBed,oneShot,weaponSound;
        AudioClip coilClip,kineticClip,missileClip,rifleClip,pistolClip,buttonClip,impactClip;
        readonly List<Raider> raiders=new List<Raider>();
        readonly List<Bolt> bolts=new List<Bolt>();
        readonly List<Transform> debris=new List<Transform>();
        readonly List<WildlifeAgent> wildlife=new List<WildlifeAgent>();
        public Vector3 WildlifeTarget=>walking&&!aboard?walkPosition:ship?ship.position:Vector3.zero;
        const string SaveKey="SpacePatriot.Unity.Frontier.v1";
        public ShipSpec Spec=>ShipSpec.Fleet[save.ship];
        public WorldInfo CurrentWorld=>worlds[save.world];
        public bool AtPort {get {if(flying||jumping||ship==null||world==null)return false;var pad=world.Nearest(ship.position,"landing");return pad!=null&&Vector3.Distance(ship.position,pad.position)<pad.range+StandHeight;}}
        public float HullPercent=>save.hull/Spec.health*100;
        public bool HasSave=>!DesktopVerification.Active&&CampaignStorage.Exists;

        class Raider { public Transform body;public float hp=70,shoot;public Vector3 velocity,home;public bool dead; }
        class Bolt { public Transform body; public Vector3 velocity;public float age,damage=8,lifetime=3;public bool enemy,missile;public Raider target; }

        void Awake()
        {
            Instance=this;bindings=new FlightBindings();pilot=new PilotInput();Application.targetFrameRate=Application.platform==RuntimePlatform.WebGLPlayer?-1:60;QualitySettings.vSyncCount=0;
            worlds=JsonUtility.FromJson<WorldCatalog>(Resources.Load<TextAsset>("Worlds").text).worlds;
            cases=JsonUtility.FromJson<CampaignCatalog>(Resources.Load<TextAsset>("Campaign").text).arcs;
            try{save=DesktopVerification.Active?new SaveData():CampaignStorage.Load();}catch{save=new SaveData();}
            if(save==null||save.version<1)save=new SaveData();save.world=Mathf.Clamp(save.world,0,worlds.Length-1);if(save.version==1){save.ship=0;save.version=2;}save.ship=Mathf.Clamp(save.ship,0,ShipSpec.Fleet.Length-1);save.ownedShips??=new List<int>{0};save.dockCargo??=new List<DockCargo>();save.vessel??=new VesselState();
            foreach(var c in cases)Progression.Get(save,c.id);
            FrontierEconomy.Ensure(save,worlds);
            LivingUniverse.Ensure(save,worlds);
            save.inventory??=new FieldInventoryState();
            if(save.ammo==null||save.ammo.Length!=5)save.ammo=System.Array.ConvertAll(WeaponSpec.All,w=>new WeaponAmmo(w.mag,w.reserve));
            effects=new GameObject("Spellworks / combat and survey effects").AddComponent<Spellworks>();effects.transform.SetParent(transform);
            combustion=new GameObject("Fireworks / causal combustion").AddComponent<Fireworks>();combustion.transform.SetParent(transform);
            world=new GameObject("Frontier environment").AddComponent<FrontierWorld>();world.Generate(CurrentWorld);
            var cameraObj=new GameObject("Flight camera");cameraObj.tag="MainCamera";view=cameraObj.AddComponent<Camera>();cameraObj.AddComponent<AudioListener>();
            view.nearClipPlane=.08f;view.farClipPlane=7000;view.fieldOfView=65;view.backgroundColor=new Color(.2f,.28f,.36f);view.clearFlags=CameraClearFlags.Skybox;view.allowHDR=true;
            view.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing=true;
            RenderSettings.skybox=Resources.Load<Material>("Rendering/FrontierSky");
            RespawnShip();SetInitialCamera();MakeAudio();SpawnRaiders();
            var probe=new GameObject("Port reflection capture").AddComponent<ReflectionProbe>();probe.transform.position=new Vector3(0,world.Deck+7,0);probe.size=new Vector3(180,90,180);probe.resolution=128;probe.mode=UnityEngine.Rendering.ReflectionProbeMode.Realtime;probe.refreshMode=UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;probe.RenderProbe();
            selectedShip=save.ship;selectedWorld=save.world==3?2:3;
            if(!Application.isEditor&&Application.platform!=RuntimePlatform.WebGLPlayer&&!DesktopVerification.Active&&PlayerPrefs.GetInt("sp.fullscreen",0)==1)Screen.fullScreenMode=FullScreenMode.FullScreenWindow;
            if(DesktopVerification.Active)StartCoroutine(VerifyDesktopPlayer());
        }
        void SetInitialCamera(){view.transform.position=ship.position+new Vector3(19,10,24);view.transform.LookAt(ship.position+Vector3.up);}
        void RespawnShip()
        {
            if(ship!=null){ship.gameObject.SetActive(false);Destroy(ship.gameObject);}
            ship=new GameObject(Spec.name).transform;ship.SetParent(transform);
            if(!AuthoredShipVisuals.TryCreate(Spec,ship,StandHeight,out exterior))
            {
                var hullAsset=Resources.Load<GameObject>("OriginalShips/refit-"+Spec.family);
                if(hullAsset==null)throw new System.InvalidOperationException("Original fleet assets must be imported before entering the game.");
                exterior=Instantiate(hullAsset,ship).transform;exterior.name="Exterior hull";
                var basis=ShipSpec.Fleet[Spec.family*10];exterior.localScale=new Vector3(Spec.width/basis.width,Spec.height/basis.height,Spec.length/basis.length);
            }
            cabin=Instantiate(Resources.Load<GameObject>("OriginalShips/cabin-"+Spec.interiorFamily),ship).transform;
            cabin.name="Pressure cabin";PrepareDeck();cabin.localPosition=new Vector3(0,Spec.height*.23f,Spec.length*.18f);
            cabin.gameObject.AddComponent<CabinLighting>().Configure(activeDeck);
            engines=new GameObject("Drive plumes").transform;engines.SetParent(ship,false);
            var model=ship.gameObject.AddComponent<OriginalShip>();model.eye=cabin.localPosition;model.exterior=exterior;model.cabin=cabin;
            var gearParts=new List<Transform>();foreach(var t in exterior.GetComponentsInChildren<Transform>())if(t.name=="Landing gear")gearParts.Add(t);
            foreach(var r in exterior.GetComponentsInChildren<MeshRenderer>())if(r.name.StartsWith("Gear_",StringComparison.Ordinal)&&r.name.Contains("_LOD"))gearParts.Add(r.transform);
            model.gear=gearParts.ToArray();
            foreach(var control in cabin.GetComponentsInChildren<CockpitControl>())control.gameObject.layer=2;
            velocityDisplay=Label(cabin,"VELOCITY",new Vector3(-1.02f,-.87f,1.46f),.0018f,new Color(.75f,.84f,.52f));
            serviceDisplay=Label(cabin,"SYSTEMS",new Vector3(1.02f,-.87f,1.46f),.0018f,new Color(.75f,.84f,.52f));
            navigationDisplay=Label(cabin,"NAVIGATION",new Vector3(0,-.89f,1.44f),.002f,new Color(.75f,.84f,.52f));
            velocityDisplay.gameObject.SetActive(false);serviceDisplay.gameObject.SetActive(false);navigationDisplay.gameObject.SetActive(false);SetupMfd();
            foreach(var r in exterior.GetComponentsInChildren<MeshRenderer>())if(r.sharedMaterial.name=="Hull finish"){var material=r.material;material.SetColor("_BaseColor",Color.Lerp(Spec.paint,new Color(.14f,.17f,.19f),.45f));}
            var spawn=Spec.length>60?new Vector3(650,0,230):Vector3.zero;
            spawn.y=(Spec.length<=60&&world.lift!=null?world.Deck+world.lift.DeckOffset:world.SurfaceAt(spawn))+StandHeight;ship.position=spawn;ship.rotation=Quaternion.identity;
            velocity=Vector3.zero;angularVelocity=Vector3.zero;yaw=pitch=roll=0;throttle=1;speed=0;docking=false;flying=false;walking=false;aboard=false;
            gearDown=true;flightAssist=true;powered=true;cruise=false;cargoDoor=false;inputNeutral=true;launchPending=false;launchClearance=0;systemsHull=save.hull;
            save.hull=Mathf.Clamp(save.hull,1,Spec.health);RebuildCargo();
        }
        void StartGame()
        {
            started=true;menu=false;walking=true;SetSurfaceWalker(ship.position+new Vector3(-Spec.width*.65f,0,-3),ship.forward);
            Toast("WASD to walk • Right mouse to look • F to board • E to use equipment • Cargo handling at the freight terminal");Save();
        }
        public void Save()
        {
            if(DesktopVerification.Active)return;
            try{if(save.society!=null)save.society.savedUtc=DateTimeOffset.UtcNow.ToUnixTimeSeconds();CampaignStorage.Write(save);PlayerPrefs.Save();saveTime=Time.unscaledTime;}
            catch(Exception e) when(e is System.IO.IOException||e is UnauthorizedAccessException){Debug.LogError("Unable to save campaign: "+e.Message);Toast("Could not write the save file. Check available disk space.");saveTime=Time.unscaledTime;}
        }
        void OnApplicationPause(bool paused){if(paused&&save!=null)Save();}
        void OnApplicationQuit(){if(save!=null)Save();}
        public void Toast(string message){toast=message;toastTime=Time.unscaledTime+7;}
        static bool Down(Key k)=>Keyboard.current!=null&&Keyboard.current[k].wasPressedThisFrame;
        static bool Held(Key k)=>Keyboard.current!=null&&Keyboard.current[k].isPressed;
        static float Axis(Key negative,Key positive)=>(Held(positive)?1:0)-(Held(negative)?1:0);
        static bool MouseButton(int i)=>Mouse.current!=null&&(i==0?Mouse.current.leftButton.isPressed:Mouse.current.rightButton.isPressed);
        void Update()
        {
            // Unity can recompile scripts while Play Mode is active without
            // invoking Awake again. Recreate transient input helpers before
            // movement/combat callbacks use them after that domain reload.
            bindings??=new FlightBindings();pilot??=new PilotInput();
            float dt=Mathf.Min(Time.deltaTime,.04f);
            if(Down(Key.F11))ToggleDesktopFullscreen();
            float fit=Mathf.Min(Screen.width/1440f,Screen.height/900f);float rw=1440*fit/Screen.width,rh=900*fit/Screen.height;view.rect=new Rect((1-rw)/2,(1-rh)/2,rw,rh);view.aspect=1.6f;
            world.Atmosphere(view,walking&&!aboard?walkPosition:ship.position);
            world.StreamSurface(walking&&!aboard?walkPosition:ship.position,walking&&!aboard);
            if(Time.frameCount%8==0)UpdateInstruments();
            if(Down(Key.Escape)||Gamepad.current?.startButton.wasPressedThisFrame==true)
            {
                if(reportText!=""){reportText="";reportTitle="";}else if(started){menu=!menu;page="overview";}
                Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            }
            if(!started){Showcase(dt);return;}
            if(!menu&&!dead&&reportText==""&&!jumping)
            {
                UpdateWeapons(dt);
                if(Down(Key.Tab)||Down(Key.M)){menu=true;page="navigation";Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
                else if(walking&&Down(Key.O)){menu=true;page="hangar";}
                else if(walking)Walk(dt);else if(aboard)WalkDeck(dt);else Flight(dt);
                if(bindings.Down("Camera")&&!walking)cockpit=!cockpit;
                if(bindings.Down("Shipyard")){menu=true;page="hangar";}
                UpdateRaiders(dt);UpdateBolts(dt);
            }
            if(menu||reportText!=""||dead){inputNeutral=true;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
            TickVessel(dt);
            TickEconomy(dt);
            TickSociety(dt);
            TickCargo(dt);
            float volume=PlayerPrefs.GetFloat("sp.volume",.55f);
            bool enclosed=aboard||cockpit;
            ambient.transform.position=ship.position+ship.TransformDirection(new Vector3(0,0,-Spec.length*.32f));
            powerBed.transform.position=ship.position+ship.TransformDirection(new Vector3(0,Spec.height*.22f,0));
            ambient.spatialBlend=Mathf.Lerp(ambient.spatialBlend,enclosed?0f:1f,dt*2.5f);
            ambient.volume=Mathf.Lerp(ambient.volume,volume*(flying?.17f+Mathf.Clamp01(throttle/3f)*.09f:enclosed?.055f:walking?.018f:.025f),dt*3);
            ambient.pitch=Mathf.Lerp(ambient.pitch,flying?.86f+Mathf.Clamp(throttle,0,3)*.055f:.82f,dt*2);
            powerBed.spatialBlend=Mathf.Lerp(powerBed.spatialBlend,enclosed?0f:.65f,dt*2);
            powerBed.volume=Mathf.Lerp(powerBed.volume,volume*(powered?.075f:.012f)*(walking?.35f:1f),dt*2.5f);
            powerBed.pitch=Mathf.Lerp(powerBed.pitch,.98f+Mathf.Clamp01(heat/100f)*.035f,dt*1.5f);
            if(Time.unscaledTime-saveTime>45)Save();
            FollowCamera(dt);
        }
        void Showcase(float dt)
        {
            cabin.gameObject.SetActive(false);
            float t=Time.time*.035f;view.transform.position=ship.position+new Vector3(Spec.width*.9f+Mathf.Sin(t)*2,Spec.height*1.3f,Spec.length*.8f+Mathf.Cos(t)*2);view.transform.LookAt(ship.position);
        }
        void Walk(float dt)
        {
            if(MouseButton(1))
            {var delta=Mouse.current.delta.ReadValue()*PlayerPrefs.GetFloat("sp.sensitivity",1);walkYaw+=delta.x*.12f;walkPitch=Mathf.Clamp(walkPitch+delta.y*.12f*(PlayerPrefs.GetInt("sp.invert",0)==1?1:-1),-75,75);}
            walkYaw+=Axis(Key.LeftArrow,Key.RightArrow)*90*dt;walkPitch=Mathf.Clamp(walkPitch+Axis(Key.UpArrow,Key.DownArrow)*65*dt,-75,75);
            var pad=Gamepad.current;var stick=pad?.leftStick.ReadValue()??Vector2.zero;var look=pad?.rightStick.ReadValue()??Vector2.zero;
            walkYaw+=look.x*110*dt;walkPitch=Mathf.Clamp(walkPitch+look.y*90*dt*(PlayerPrefs.GetInt("sp.padInvert",0)==1?1:-1),-75,75);
            var move=new Vector3(bindings.Axis("Strafe left","Strafe right")+stick.x,0,bindings.Axis("Reverse","Forward")+stick.y);if(move.sqrMagnitude>1)move.Normalize();
            Vector3 groundPoint=walkPosition-Vector3.up*1.75f,normal=world.transform.up;
            if(world.TrySurface(walkPosition,out var currentPoint,out var currentNormal)){groundPoint=currentPoint;normal=currentNormal;}
            Vector3 forward=Vector3.ProjectOnPlane(walkForward,normal).normalized;
            if(forward.sqrMagnitude<.01f)forward=Vector3.ProjectOnPlane(Vector3.forward,normal).normalized;
            forward=Quaternion.AngleAxis(walkYaw,normal)*forward;Vector3 right=Vector3.Cross(normal,forward).normalized;
            Vector3 travel=right*move.x+forward*move.z;
            Vector3 next=walkPosition+travel*(Held(Key.LeftShift)?8:4.5f)*dt;
            // Walk on the tangent patch over the globe, keeping the feet attached
            // to the same collision surface used by ship landing.
            if(!Physics.Raycast(walkPosition,travel,out _,travel.magnitude*.5f)&&world.TrySurface(next,out var nextPoint,out var nextNormal)&&Vector3.Distance(groundPoint,nextPoint)<1.2f)
            {walkPosition=nextPoint+nextNormal*1.75f;walkForward=Vector3.ProjectOnPlane(forward,nextNormal).normalized;walkYaw=0;}
            if(Down(Key.E)||Down(Key.Z)||pad?.buttonEast.wasPressedThisFrame==true)Interact();
            if(bindings.Down("Board / leave seat")||Down(Key.J))BoardOrExit();
            if(Down(Key.B)){if(Held(Key.LeftShift))CollectFieldSample();else {Signal("scan");Toast("Survey scan complete. Shift+B collects a sample near the field station.");}}
        }
        Place NearInteract()
        {
            Place best=null;float closest=float.MaxValue;
            foreach(var p in world.places){if(p.kind=="landing")continue;float d=Vector3.Distance(walkPosition,p.position+Vector3.up*1.75f);if(d<p.range&&d<closest){closest=d;best=p;}}
            return best;
        }
        void Interact()
        {
            if(SpeakToResident())return;
            if(Vector3.Distance(walkPosition,new Vector3(49,world.Deck+1.75f,26))<4){menu=true;page="inventory";return;}
            var elevator=world.NearbyLift(walkPosition);if(elevator!=null){elevator.RequestNext();return;}
            if(AtCargoAccess){menu=true;page="cargo";return;}
            var near=NearInteract();
            if(near!=null)
            {
                if(near.kind.StartsWith("city-")){CityInteraction(int.Parse(near.kind.Substring(5)));return;}
                if(near.kind=="power"){page="reactor";menu=true;return;}
                Signal(near.kind);
                if(reportText==""){page=near.kind=="delivery"?"trade":"cases";menu=true;}
                return;
            }
            if(Vector3.Distance(walkPosition,ship.position)<Spec.width*.7f+5)BoardOrExit();
        }
        void Land(Place pad)=>CompleteLanding(pad.position+Vector3.up*(StandHeight-2.65f),Quaternion.identity);
        void LandSurface(Vector3 shipCenter,Quaternion attitude)=>CompleteLanding(shipCenter,attitude);
        void CompleteLanding(Vector3 position,Quaternion attitude)
        {
            ship.position=position;ship.rotation=attitude;velocity=Vector3.zero;speed=0;throttle=Mathf.Clamp(throttle,.05f,3);pitch=yaw=roll=0;flying=false;docking=false;surfaceLanding=false;launchClearance=0;
            Save();Toast("Surface landing secured. F to leave the seat. Cargo hatch and loading are available while landed.");Sound(buttonClip,.4f);
        }
        void FollowCamera(float dt)
        {
            cabin.gameObject.SetActive(aboard||!walking&&cockpit);
            if(aboard){view.transform.position=cabin.TransformPoint(deckPosition);view.transform.rotation=ship.rotation*Quaternion.Euler(walkPitch,walkYaw,0);exterior.gameObject.SetActive(false);}
            else if(walking)
            {view.transform.position=walkPosition;Vector3 up=world.transform.up,forward=walkForward;if(world.TrySurface(walkPosition,out _,out var normal)){up=normal;forward=Vector3.ProjectOnPlane(walkForward,normal).normalized;}forward=Quaternion.AngleAxis(walkYaw,up)*forward;view.transform.rotation=Quaternion.LookRotation(forward,up)*Quaternion.Euler(walkPitch,0,0);exterior.gameObject.SetActive(true);}
            else if(cockpit)
            {view.transform.position=cabin.position;view.transform.rotation=ship.rotation;exterior.gameObject.SetActive(false);}
            else
            {var target=ship.TransformPoint(new Vector3(0,Spec.height*.8f,-Spec.length*.95f));view.transform.position=Vector3.Lerp(view.transform.position,target,1-Mathf.Exp(-dt*5));view.transform.rotation=Quaternion.Slerp(view.transform.rotation,Quaternion.LookRotation(ship.position+ship.forward*22-view.transform.position),dt*7);exterior.gameObject.SetActive(true);}
            view.fieldOfView=Mathf.Lerp(view.fieldOfView,walking||aboard?68:cockpit?58.7f:speed>Spec.speed?76:65,dt*2);
        }
        void MakeAudio()
        {
            var engineObject=new GameObject("Drive acoustics / aft machinery");engineObject.transform.SetParent(transform,false);
            ambient=engineObject.AddComponent<AudioSource>();ambient.loop=true;ambient.spatialBlend=1;ambient.playOnAwake=false;ambient.volume=0;ambient.pitch=.82f;ambient.clip=EngineBed();ambient.dopplerLevel=.08f;ambient.rolloffMode=AudioRolloffMode.Logarithmic;ambient.minDistance=5;ambient.maxDistance=420;ambient.priority=32;ambient.Play();
            var reactorObject=new GameObject("Reactor acoustics / cabin structure");reactorObject.transform.SetParent(transform,false);
            powerBed=reactorObject.AddComponent<AudioSource>();powerBed.loop=true;powerBed.spatialBlend=.65f;powerBed.playOnAwake=false;powerBed.volume=0;powerBed.pitch=.98f;powerBed.clip=ReactorBed();powerBed.dopplerLevel=0;powerBed.rolloffMode=AudioRolloffMode.Logarithmic;powerBed.minDistance=4;powerBed.maxDistance=250;powerBed.priority=36;powerBed.Play();
            oneShot=new GameObject("Interface and confirmation sounds").AddComponent<AudioSource>();oneShot.transform.SetParent(transform,false);oneShot.spatialBlend=0;oneShot.playOnAwake=false;oneShot.dopplerLevel=0;oneShot.priority=32;
            weaponSound=new GameObject("Weapon transient / spatial").AddComponent<AudioSource>();weaponSound.transform.SetParent(transform,false);weaponSound.spatialBlend=.85f;weaponSound.playOnAwake=false;weaponSound.dopplerLevel=.12f;weaponSound.rolloffMode=AudioRolloffMode.Logarithmic;weaponSound.minDistance=4;weaponSound.maxDistance=240;weaponSound.priority=24;
            coilClip=CoilDischarge();kineticClip=WeaponReport("K-28 / twin autocannon",1080,145,88,.12f,.55f,.38f,2201);missileClip=WeaponReport("M-6 / seeker ignition",340,78,42,.46f,.58f,.48f,3119);rifleClip=WeaponReport("AR-30 / service rifle",1460,180,105,.105f,.82f,.27f,4193);pistolClip=WeaponReport("P-12 / sidearm",1780,260,125,.095f,.75f,.3f,5279);buttonClip=RelayClick();impactClip=HullImpact();
        }
        AudioClip EngineBed()
        {
            const int rate=44100;const float duration=4f;int count=(int)(rate*duration);var samples=new float[count];
            var random=new System.Random(817);float filtered=0,slowFilter=0;
            for(int i=0;i<count;i++)
            {
                float t=i/(float)rate;float breathe=.78f+.14f*Mathf.Sin(t*Mathf.PI*1.5f)+.08f*Mathf.Sin(t*Mathf.PI*3.5f);
                float rotor=.55f+.45f*Mathf.Pow(.5f+.5f*Mathf.Sin(t*Mathf.PI*2*23f),3);
                filtered=Mathf.Lerp(filtered,(float)(random.NextDouble()*2-1),.035f);
                slowFilter=Mathf.Lerp(slowFilter,filtered,.006f);
                float low=Mathf.Sin(t*Mathf.PI*2*31f)*.22f+Mathf.Sin(t*Mathf.PI*2*46f)*.12f+Mathf.Sin(t*Mathf.PI*2*69f)*.055f;
                float mid=Mathf.Sin(t*Mathf.PI*2*92f)*.075f+Mathf.Sin(t*Mathf.PI*2*138f)*.035f+filtered*.018f-slowFilter*.08f;
                float edge=Mathf.Min(1f,Mathf.Min(t/.018f,(duration-t)/.018f));
                samples[i]=(low*breathe*rotor+mid)*Mathf.Clamp01(edge)*.28f;
            }
            return Clip("Space Patriot / variable-speed drive bed",samples,rate);
        }
        AudioClip ReactorBed()
        {
            const int rate=44100;const float duration=4f;int count=(int)(rate*duration);var samples=new float[count];var random=new System.Random(52913);float filtered=0;
            for(int i=0;i<count;i++)
            {
                float t=i/(float)rate;float pulse=.86f+.14f*Mathf.Sin(t*Mathf.PI*2*3f);filtered=Mathf.Lerp(filtered,(float)(random.NextDouble()*2-1),.012f);
                float low=Mathf.Sin(t*Mathf.PI*2*27f)*.34f+Mathf.Sin(t*Mathf.PI*2*41f)*.17f+Mathf.Sin(t*Mathf.PI*2*54f)*.07f;
                float valve=Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*Mathf.PI*2*2f)),12)*.055f;
                float edge=Mathf.Min(1,Mathf.Min(t/.02f,(duration-t)/.02f));samples[i]=(low*pulse+filtered*.045f+valve)*Mathf.Clamp01(edge)*.23f;
            }
            return Clip("Space Patriot / reactor circulation and cabin structure",samples,rate);
        }
        AudioClip CoilDischarge()
        {
            const int rate=44100;const float duration=.32f;int count=(int)(rate*duration);var samples=new float[count];
            double phase=0;var random=new System.Random(17021);
            for(int i=0;i<count;i++)
            {
                float t=i/(float)rate;float f=920f*Mathf.Exp(-t*9f)+125f;phase+=Math.PI*2*f/rate;
                float decay=Mathf.Exp(-t*13f);float crack=t<.008f?(float)(random.NextDouble()*2-1)*Mathf.Exp(-t*360f):0;
                float arc=(Mathf.Sin((float)phase)+.28f*Mathf.Sin((float)(phase*2.03)))*decay;
                float tail=Mathf.Sin(t*Mathf.PI*2*(82f+18f*Mathf.Exp(-t*16f)))*Mathf.Exp(-t*8f)*.32f;
                samples[i]=(crack*.72f+arc*.3f+tail*.32f)*Mathf.Min(1f,t/.0015f);
            }
            return Clip("Kestrel / capacitor coil discharge",samples,rate);
        }
        AudioClip WeaponReport(string name,float startHz,float endHz,float subHz,float duration,float crack,float body,int seed)
        {
            const int rate=44100;int count=Mathf.CeilToInt(rate*duration);var samples=new float[count];var random=new System.Random(seed);double phase=0,subPhase=0;
            for(int i=0;i<count;i++)
            {
                float t=i/(float)rate,p=t/duration;float frequency=endHz+(startHz-endHz)*Mathf.Exp(-p*7f);phase+=Math.PI*2*frequency/rate;subPhase+=Math.PI*2*(subHz+18*Mathf.Exp(-p*10f))/rate;
                float transient=(float)(random.NextDouble()*2-1)*Mathf.Exp(-t*(crack>0.6f?210:145))*crack;
                float resonant=(Mathf.Sin((float)phase)+.24f*Mathf.Sin((float)(phase*2.01)))*Mathf.Exp(-p*4.1f)*body;
                float sub=Mathf.Sin((float)subPhase)*Mathf.Exp(-p*3f)*.3f;float tail=(float)(random.NextDouble()*2-1)*Mathf.Exp(-p*14f)*.06f;
                float attack=Mathf.Clamp01(t/.0018f),envelope=Mathf.Pow(1-p,.9f);samples[i]=(transient+resonant+sub+tail)*attack*envelope*.48f;
            }
            return Clip("Space Patriot / "+name,samples,rate);
        }
        AudioClip RelayClick()
        {
            const int rate=44100;const float duration=.11f;int count=(int)(rate*duration);var samples=new float[count];var random=new System.Random(7401);
            for(int i=0;i<count;i++)
            {
                float t=i/(float)rate;float attack=t<.0015f?1f:0f;
                float knock=Mathf.Sin(t*Mathf.PI*2*1840f)*Mathf.Exp(-t*75f)*.5f;
                float spring=Mathf.Sin(t*Mathf.PI*2*620f)*Mathf.Exp(-t*43f)*.20f;
                float grit=t<.004f?(float)(random.NextDouble()*2-1)*Mathf.Exp(-t*620f)*.4f:0;
                samples[i]=attack*grit+knock+spring;
            }
            return Clip("Kestrel / guarded relay click",samples,rate);
        }
        AudioClip HullImpact()
        {
            const int rate=44100;const float duration=.62f;int count=(int)(rate*duration);var samples=new float[count];var random=new System.Random(7519);
            for(int i=0;i<count;i++)
            {
                float t=i/(float)rate;float thud=Mathf.Sin(t*Mathf.PI*2*44f)*Mathf.Exp(-t*9f);
                float panel=Mathf.Sin(t*Mathf.PI*2*310f)*Mathf.Exp(-t*11f)*.27f+Mathf.Sin(t*Mathf.PI*2*487f)*Mathf.Exp(-t*16f)*.13f;
                float debris=(float)(random.NextDouble()*2-1)*Mathf.Exp(-t*70f)*.28f;
                samples[i]=(thud*.55f+panel+debris)*Mathf.Min(1f,t/.001f);
            }
            return Clip("Kestrel / hull plate impact",samples,rate);
        }
        static AudioClip Clip(string name,float[] samples,int rate)
        {var clip=AudioClip.Create(name,samples.Length,1,rate,false);clip.SetData(samples,0);return clip;}
        void Sound(AudioClip clip,float volume){oneShot.PlayOneShot(clip,volume*PlayerPrefs.GetFloat("sp.volume",.55f));}
        public void WeaponShot(string id,Vector3 origin,float volume=.42f)
        {
            AudioClip clip=id=="kinetic"?kineticClip:id=="laser"?coilClip:id=="missile"?missileClip:id=="rifle"?rifleClip:pistolClip;
            weaponSound.transform.position=origin;weaponSound.spatialBlend=walking||aboard?.14f:cockpit?.24f:.88f;weaponSound.pitch=UnityEngine.Random.Range(.97f,1.03f);weaponSound.PlayOneShot(clip,volume*PlayerPrefs.GetFloat("sp.volume",.55f));
        }
        public void Signal(string kind)
        {
            if(kind=="sample"||kind=="scan")
            {
                Vector3 origin=view.transform.position,direction=view.transform.forward,point=origin+direction*42,normal=-direction;
                if(Physics.Raycast(origin,direction,out var hit,2200,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)){point=hit.point+hit.normal*.12f;normal=hit.normal;}
                effects.Survey(point,normal);
            }
            foreach(var c in cases)
            {
                if(Progression.CompleteStep(save,c,CurrentWorld.name,kind,out var report))
                {reportTitle=c.title;reportText=report;targetedCase=c.id;RebuildCargo();Save();Sound(buttonClip,.5f);return;}
            }
            if(kind=="delivery")Toast("Freight desk ready. Accept a matching case and bring the required cargo.");
            else if(kind=="sample")Toast("Survey sample logged. Accept the relevant case to use this evidence.");
        }
        public void BeginJump(int destination)
        {
            if(!flying){Toast("Board and launch before requesting a jump.");return;}
            if(gearDown||!powered||cargoDoor){Toast("Retract landing gear, close the cargo hatch and power the engine bus before jumping.");return;}
            if(speed>70){Toast("Reduce speed below 70 m/s before jump alignment.");return;}
            if(ship.position.y<world.Deck+65){Toast("Climb 65 m above the port before jump alignment. Space engages vertical thrust.");return;}
            if(save.fuel<8){Toast("Jump requires 8 fuel. Return to port for fuel.");return;}
            if(destination==save.world){Toast("Already in this traffic sector.");return;}
            StartCoroutine(Jump(destination));
        }
        IEnumerator Jump(int destination)
        {
            jumping=true;menu=false;save.fuel-=8;Save();
            for(float t=0;t<1;t+=Time.unscaledDeltaTime){travelFade=t;yield return null;}
            ClearCombat();save.world=destination;world.Generate(CurrentWorld);ship.position=new Vector3(0,world.Deck+125,-180);ship.rotation=Quaternion.identity;pitch=yaw=roll=0;
            velocity=Vector3.forward*20;throttle=.2f;cockpit=false;SpawnRaiders();travelFade=1;Save();
            for(float t=1;t>0;t-=Time.unscaledDeltaTime){travelFade=t;yield return null;}
            travelFade=0;jumping=false;Toast("Arrived at "+CurrentWorld.name+". Port 07 is ahead. Press X to brake, extend gear with G, then L to land.");
        }
        void SpawnRaiders()
        {
            for(int i=0;i<2;i++)
            {
                var body=Root("Unregistered interceptor",transform,new Vector3(420+i*100,180+i*40,520));
                var hull=Instantiate(Resources.Load<GameObject>("OriginalShips/refit-1"),body).transform;hull.localPosition=Vector3.zero;
                raiders.Add(new Raider{body=body,home=body.position,shoot=Time.time+5+i});
            }
        }
        void ClearCombat(){selectedTarget=null;foreach(var r in raiders)if(r.body)Destroy(r.body.gameObject);raiders.Clear();foreach(var b in bolts)if(b.body)Destroy(b.body.gameObject);bolts.Clear();wildlife.Clear();}
        public void RegisterWildlife(WildlifeAgent actor){if(actor&&!wildlife.Contains(actor))wildlife.Add(actor);}
        public void WildlifeAttack(WildlifeAgent actor,float damage,string ability,float distance)
        {
            if(dead||menu||!walking||aboard)return;float scale=actor.species.Boss?Mathf.Lerp(1,.32f,Mathf.Clamp01(distance/85)):1;
            Vector3 point=walking?walkPosition:ship.position;string move=ability.ToLowerInvariant();Color cue=move.Contains("spore")||move.Contains("dust")?new Color(.56f,1.05f,.24f):move.Contains("screech")||move.Contains("pulse")?new Color(.28f,.82f,1.4f):new Color(1.4f,.35f,.12f);
            if(actor.species.Boss){for(int i=0;i<36;i++){float a=i*Mathf.PI*2/36;var dir=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));effects.Emit(point+dir*1.5f,dir*UnityEngine.Random.Range(4,13)+Vector3.up*.4f,cue,.8f,UnityEngine.Random.Range(.12f,.26f),move.Contains("pulse")?0:4,1.2f,.35f);}}
            else effects.Impact(point,Vector3.up,false,1);
            Damage(damage*scale);if(actor.species.Boss)Toast(actor.species.name.ToUpperInvariant()+" / "+ability.ToUpperInvariant()+"  •  EVADE THE TELEGRAPH");
        }
        public void WildlifeWarning(CreatureSpecies species,string ability)
        {if(species!=null&&species.Boss){Toast(species.name.ToUpperInvariant()+" / "+ability.ToUpperInvariant()+" INBOUND");string move=ability.ToLowerInvariant();Color cue=move.Contains("spore")||move.Contains("dust")?new Color(.35f,.82f,.19f):move.Contains("screech")||move.Contains("pulse")?new Color(.22f,.8f,1.15f):new Color(1.2f,.3f,.1f);for(int i=0;i<32;i++){float a=i*Mathf.PI*2/32;var dir=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));effects.Emit(WildlifeTarget+dir*(3+i%3*2),dir*1.2f,cue,1.35f,.09f,0,.15f,.2f);}}}
        public void WildlifeDefeated(WildlifeAgent actor)
        {if(actor?.species==null)return;int reward=actor.species.Boss?450:actor.species.aggression>.25f?75:15;save.credits+=reward;save.journal.Add("Field encounter: "+actor.species.name+" / recovered value "+reward+" cr");if(save.journal.Count>100)save.journal.RemoveAt(0);Toast(actor.species.name+" neutralized. Field salvage: "+reward+" cr.");Save();}
        void UpdateRaiders(float dt)
        {
            foreach(var r in raiders)
            {
                if(r.dead)continue;
                float distance=Vector3.Distance(r.body.position,ship.position);
                bool engage=flying&&distance<480&&ship.position.y>world.Deck+50;
                Vector3 destination=engage?ship.position+velocity*1.2f:r.home+new Vector3(Mathf.Sin(Time.time*.13f)*110,Mathf.Sin(Time.time*.2f)*24,Mathf.Cos(Time.time*.13f)*110);
                if(engage&&distance<85)destination=r.body.position+r.body.forward*180+Vector3.up*45;
                var dir=(destination-r.body.position).normalized;r.body.rotation=Quaternion.RotateTowards(r.body.rotation,Quaternion.LookRotation(dir),dt*55);
                r.velocity=r.body.forward*(engage?42:25);r.body.position+=r.velocity*dt;
                if(engage&&Time.time>r.shoot&&Vector3.Dot(r.body.forward,(ship.position-r.body.position).normalized)>.93f)
                {r.shoot=Time.time+1.3f;SpawnBolt(r.body.position+r.body.forward*5,(ship.position+velocity*.35f-r.body.position).normalized*230,true);}
            }
        }
        void SpawnBolt(Vector3 p,Vector3 vel,bool enemy,WeaponSpec spec=null)
        {
            bool missile=spec?.id=="missile";
            var body=Box(missile?"IR seeker":"Kinetic tracer",transform,p,missile?new Vector3(.23f,.23f,1.7f):new Vector3(.06f,.06f,1.8f),enemy?Glow("Hostile tracer",new Color(1,.3f,.08f),3):Glow("Kinetic tracer",new Color(1,.79f,.4f),3)).transform;body.rotation=Quaternion.LookRotation(vel);
            if(missile){var trail=body.gameObject.AddComponent<TrailRenderer>();trail.sharedMaterial=Glow("Missile plume",new Color(1,.22f,.045f),2);trail.time=.3f;trail.startWidth=.19f;trail.endWidth=0;}
            bolts.Add(new Bolt{body=body,velocity=vel,enemy=enemy,damage=spec?.damage??8,lifetime=spec==null?4:spec.range/Mathf.Max(1,spec.speed),missile=missile,target=missile?selectedTarget:null});
        }
        static float SegmentDistance(Vector3 p,Vector3 a,Vector3 b)
        {var v=b-a;float t=Mathf.Clamp01(Vector3.Dot(p-a,v)/Mathf.Max(.0001f,v.sqrMagnitude));return Vector3.Distance(p,a+t*v);}
        void UpdateBolts(float dt)
        {
            for(int i=bolts.Count-1;i>=0;i--)
            {
                var b=bolts[i];
                if(b.missile&&b.target!=null&&!b.target.dead&&b.target.body!=null){var desired=(b.target.body.position+b.target.velocity*.25f-b.body.position).normalized;float v=Mathf.MoveTowards(b.velocity.magnitude,640,dt*120);b.velocity=Vector3.RotateTowards(b.velocity.normalized,desired,dt*1.65f,0)*v;b.body.rotation=Quaternion.LookRotation(b.velocity);}
                Vector3 from=b.body.position,to=from+b.velocity*dt;bool hit=false;b.age+=dt;float nearest=1;
                if(Physics.Linecast(from,to,out var environment,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)){nearest=Vector3.Distance(from,environment.point)/Mathf.Max(.001f,Vector3.Distance(from,to));hit=true;}
                Raider victim=null;WildlifeAgent fauna=null;
                if(b.enemy){var t=SegmentHit(from,to,walking?walkPosition-Vector3.up*.7f:ship.position,walking?.6f:Spec.width*.22f);if(t.HasValue&&t.Value<=nearest){Damage(b.damage);nearest=t.Value;hit=true;}}
                else {foreach(var r in raiders){if(r.dead)continue;var t=SegmentHit(from,to,r.body.position,4);if(t.HasValue&&t.Value<=nearest){victim=r;fauna=null;nearest=t.Value;hit=true;}}
                    for(int k=wildlife.Count-1;k>=0;k--){var a=wildlife[k];if(!a){wildlife.RemoveAt(k);continue;}if(a.dead)continue;float radius=a.species.Boss?Mathf.Max(2,a.species.height*.16f):1.3f;var t=SegmentHit(from,to,a.transform.position+Vector3.up*radius,radius);if(t.HasValue&&t.Value<=nearest){fauna=a;victim=null;nearest=t.Value;hit=true;}}}
                if(victim!=null)HitRaider(victim,b.damage);
                if(fauna!=null)fauna.Hit(b.damage);
                b.body.position=Vector3.Lerp(from,to,nearest);if(hit||b.age>b.lifetime){if(hit){if(b.missile)Burst(b.body.position);else effects.Impact(b.body.position,-b.velocity.normalized);}Destroy(b.body.gameObject);bolts.RemoveAt(i);}

            }
            for(int i=debris.Count-1;i>=0;i--){if(!debris[i]){debris.RemoveAt(i);continue;}debris[i].position+=debris[i].forward*dt*22;debris[i].Rotate(40*dt,65*dt,0);}
        }
        void Burst(Vector3 p)
        {effects.Impact(p,Vector3.up,true,3);if(CurrentWorld.biome=="temperate"&&p.y<world.Height(p.x,p.z)+14)combustion.Ignite(new Vector3(p.x,world.Height(p.x,p.z)+.2f,p.z),8,1.4f);for(int i=0;i<12;i++){var d=Box("Hull fragment",transform,p,Vector3.one*UnityEngine.Random.Range(.15f,.65f),i<3?Orange:Steel).transform;d.rotation=UnityEngine.Random.rotation;debris.Add(d);Destroy(d.gameObject,3);}}
        void Damage(float amount)
        {
            if(dead||amount<=0)return;if(walking||aboard){save.crewHealth=Mathf.Max(0,save.crewHealth-amount);if(save.crewHealth==0){dead=true;menu=false;Save();}return;}hitTime=Time.time;float absorbed=Mathf.Min(shield,amount);shield-=absorbed;save.hull-=amount-absorbed;Sound(impactClip,.5f);
            if(save.hull<=0){save.hull=0;dead=true;menu=false;velocity=Vector3.zero;Save();}
        }
        void Rescue()
        {save.credits=Mathf.Max(0,save.credits-180);save.hull=Spec.health;save.crewHealth=100;save.fuel=Mathf.Max(save.fuel,35);shield=100;dead=false;ClearCombat();RespawnShip();SpawnRaiders();walking=true;SetSurfaceWalker(ship.position+new Vector3(-Spec.width*.65f,0,-3),ship.forward);Save();Toast("Port recovery complete. Service fee: up to 180 cr.");}
    }
}

