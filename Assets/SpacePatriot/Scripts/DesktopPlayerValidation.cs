using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpacePatriot
{
    public partial class FrontierGame
    {
        readonly List<string> desktopChecks = new();
        string verificationDirectory;
        Keyboard verificationKeyboard;
        Mouse verificationMouse;
        void DesktopCheck(bool value, string label)
        { if (!value) throw new InvalidOperationException(label); desktopChecks.Add("PASS " + label); }

        IEnumerator VerifyDesktopPlayer()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "--verification-output");
            verificationDirectory = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, "verification");
            Directory.CreateDirectory(verificationDirectory);
            yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();
            if (!DesktopStep(() => {
                var image = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(verificationDirectory,"title.png"),image.EncodeToPNG());Destroy(image);
                enabled=false; // Freeze normal input, autosave and showcase; exercise the same game methods below.
                foreach(var device in InputSystem.devices.ToArray())InputSystem.DisableDevice(device);
                verificationKeyboard=InputSystem.AddDevice<Keyboard>();verificationMouse=InputSystem.AddDevice<Mouse>();bindings=new FlightBindings(false);
                DesktopCheck(!Application.isEditor&&Application.platform==RuntimePlatform.WindowsPlayer,"Actual Windows player started with a graphics device: "+SystemInfo.graphicsDeviceName);
                DesktopCheck(worlds.Length==19&&cases.Length==9,"World and campaign resources load in the native player");
                DesktopCheck(save.society.cities.Count==420&&save.society.residents.Count>4000,"420 settlements and more than 4,000 persistent residents initialise");
                StartGame();menu=false;walking=false;aboard=false;cockpit=true;focused=true;inputNeutral=false;
                FrameDesktop(Key.Space);
                DesktopCheck(world.lift.Raising&&!flying,"Space requests launch and starts the hangar lift");
                world.lift.Advance(9);FrameDesktop();
                DesktopCheck(flying&&world.lift.Ready,"Ship launches after the platform and roof clear");
                launchClearance=0;ship.position=new Vector3(0,world.Deck+120,0);gearDown=false;velocity=Vector3.zero;
                FrameDesktop(Key.A);DesktopCheck(velocity.x<0,"A supplies left strafe");
                velocity=Vector3.zero;FrameDesktop(Key.D);DesktopCheck(velocity.x>0,"D supplies right strafe");
                velocity=Vector3.zero;FrameDesktop(Key.S);DesktopCheck(velocity.z<0,"S supplies reverse thrust");
                velocity=Vector3.zero;FrameDesktop(Key.Space);DesktopCheck(velocity.y>0,"Space supplies upward thrust");
                velocity=Vector3.zero;FrameDesktop(Key.LeftCtrl);DesktopCheck(velocity.y<0,"Ctrl supplies downward thrust");
                velocity=new Vector3(20,0,0);FrameDesktop(Key.X);DesktopCheck(velocity.magnitude<20,"X brakes the ship");
                VerifyAnywhereLanding();
                FrameDesktop();velocity=Vector3.zero;ship.rotation=Quaternion.identity;FollowCamera(10);Physics.SyncTransforms();
                var dial=cabin.GetComponentsInChildren<CockpitControl>().First(c=>c.action==43);
                var point=view.WorldToScreenPoint(dial.GetComponent<Renderer>().bounds.center);float previousThrottle=throttle;
                InputSystem.QueueStateEvent(verificationMouse,new MouseState{position=new Vector2(point.x,point.y),scroll=new Vector2(0,120)});InputSystem.Update();PointCockpit();
                DesktopCheck(throttle>previousThrottle,"Physical DRIVE knob changes actual flight speed limit");
                var soft=cabin.GetComponentsInChildren<CockpitControl>().First(c=>c.action==101);point=view.WorldToScreenPoint(soft.GetComponent<Renderer>().bounds.center);int previousPage=mfdPage[0];
                InputSystem.ResetDevice(verificationMouse);InputSystem.QueueStateEvent(verificationMouse,new MouseState{position=new Vector2(point.x,point.y),buttons=1});InputSystem.Update();PointCockpit();
                DesktopCheck(mfdPage[0]!=previousPage,"Physical MFD softkey selects the next live page");
                mfdNext=0;UpdateMfd();DesktopCheck(mfd.Select(s=>s.texture.GetInstanceID()).Distinct().Count()==3,"Three independent live cockpit displays render");
                var pixels=mfd[1].texture.GetPixels32();DesktopCheck(pixels.Count(p=>p.g>120)>300&&pixels.Count(p=>p.g<40)>100000,"Navigation display contains illuminated marks on a dark background");
                ship.position=new Vector3(650,world.Deck+StandHeight,230);flying=false;FollowCamera(10);DesktopCapture("cockpit");
                FrameDesktop();save.organics=save.ore=save.crystal=0;cargoDoor=true;walking=true;walkPosition=CargoAccess;RebuildCargo();CargoHandling.Stage(save,save.world,"ore",1);
                DesktopCheck(StartCargoTransfer("ore",true),"Physical cargo loading starts at the cargo access");TickCargo(2.6f);DesktopCheck(save.ore==1&&CargoHandling.Staged(save,save.world,"ore")==0,"Cargo loading conserves hold and dock inventory");
                DesktopCheck(StartCargoTransfer("ore",false),"Physical cargo unloading starts");TickCargo(2.6f);DesktopCheck(save.ore==0&&CargoHandling.Staged(save,save.world,"ore")==1,"Cargo unloading conserves hold and dock inventory");
                var remote=save.society.cities.First(c=>c.world!=CurrentWorld.id);float food=remote.food;LivingUniverse.Step(save,worlds,25);DesktopCheck(remote.food!=food,"Society simulation advances in an unloaded world");
                string scratch=Path.Combine(verificationDirectory,"isolated-save");save.credits=12345;CampaignStorage.Write(save,scratch);save.credits=12346;CampaignStorage.Write(save,scratch);
                DesktopCheck(CampaignStorage.ReadFiles(scratch).credits==12346,"Native save file roundtrip restores progress");File.WriteAllText(Path.Combine(scratch,"frontier-v1.json"),"interrupted-save");DesktopCheck(CampaignStorage.ReadFiles(scratch).credits==12345,"Damaged current save recovers the previous complete generation");

            }))yield break;
            yield return null;
            foreach(int index in new[]{20,70,90})
            {
                if(!DesktopStep(()=>{
                    save.ship=index;RespawnShip();aboard=true;walking=false;deckPosition=new Vector3(0,0,-5);focused=true;
                    for(int level=1;level<activeDeck.decks.Length;level++){
                        InputSystem.QueueStateEvent(verificationKeyboard,new KeyboardState());InputSystem.Update();InputSystem.QueueStateEvent(verificationKeyboard,new KeyboardState(Key.E));InputSystem.Update();WalkDeck(.02f);
                        InputSystem.QueueStateEvent(verificationKeyboard,new KeyboardState());InputSystem.Update();for(int i=0;i<120;i++)WalkDeck(.02f);
                        DesktopCheck(Mathf.Abs(deckPosition.y+level*3.3f)<.01f,Spec.name+" service lift reaches deck "+(level+1));
                    }
                    walkYaw=180;walkPitch=0;deckPosition.z=-8;FollowCamera(10);DesktopCapture("ship-"+index+"-lower-deck");
                }))yield break;
                yield return null;
            }
            if(!DesktopStep(()=>{
                aboard=false;walking=true;cabin.gameObject.SetActive(false);exterior.gameObject.SetActive(true);TickSociety(1);
                view.transform.position=new Vector3(325,world.Deck+120,230);view.transform.LookAt(new Vector3(600,world.Deck,-170));DesktopCapture("city");
                view.transform.position=new Vector3(495,world.Deck+1.75f,-91);view.transform.LookAt(new Vector3(531,world.Deck+5,-138));DesktopCapture("city-street");
                Screen.SetResolution(1280,800,FullScreenMode.Windowed);
            }))yield break;
            yield return new WaitForSecondsRealtime(.3f);
            if(!DesktopStep(()=>{DesktopCheck(!Screen.fullScreen&&Screen.width==1280&&Screen.height==800,"Desktop window changes to 1280 x 800");Screen.fullScreenMode=FullScreenMode.FullScreenWindow;}))yield break;
            yield return new WaitForSecondsRealtime(.3f);
            if(!DesktopStep(()=>{DesktopCheck(Screen.fullScreen,"Borderless fullscreen works");Screen.SetResolution(1440,900,FullScreenMode.Windowed);}))yield break;
            yield return new WaitForSecondsRealtime(.3f);
            if(!DesktopStep(()=>DesktopCheck(!Screen.fullScreen&&Screen.width==1440,"Returning to windowed mode works")))yield break;
            File.WriteAllLines(Path.Combine(verificationDirectory,"desktop-player.txt"),desktopChecks);
            Debug.Log("SPACE_PATRIOT_DESKTOP_VERIFICATION_PASS "+desktopChecks.Count);Application.Quit(0);
        }
        bool DesktopStep(Action action)
        {
            try{action();return true;}
            catch(Exception e){desktopChecks.Add("FAIL "+e);File.WriteAllLines(Path.Combine(verificationDirectory,"desktop-player.txt"),desktopChecks);Debug.LogException(e);Application.Quit(1);return false;}
        }
        void VerifyAnywhereLanding()
        {
            var previousPosition=ship.position;var previousRotation=ship.rotation;var previousVelocity=velocity;var previousSpeed=speed;bool previousFlying=flying,previousGear=gearDown;
            var center=world.transform.TransformPoint(world.PlanetCenter);float probeRadius=FrontierWorld.PlanetRadius+100;
            foreach(var direction in new[]{Vector3.up,Vector3.down,Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
                DesktopCheck(world.TrySurface(center+direction*probeRadius,out _,out _),"Solid terrain resolves on planet face "+direction);
            DesktopCheck(world.TrySurface(center+Vector3.down*probeRadius,out var point,out var normal),"Far-hemisphere surface resolves for landing");
            ship.position=point+normal*(StandHeight+100);var forward=Vector3.ProjectOnPlane(Vector3.forward,normal);if(forward.sqrMagnitude<.001f)forward=Vector3.Cross(normal,Vector3.right);
            ship.rotation=Quaternion.LookRotation(forward,normal);gearDown=true;flying=true;speed=0;velocity=Vector3.zero;RequestLanding();
            DesktopCheck(surfaceLanding&&docking,"Landing assist accepts unpadded ground on the far hemisphere");
            DesktopCheck(Vector3.Dot(surfaceLandingRotation*Vector3.up,normal)>.999f,"Landing assist aligns landing gear to local ground normal");
            Vector3 farWalker=point+normal*1.75f;world.StreamSurface(farWalker,false);
            DesktopCheck(world.TrySurface(farWalker,out var streamedGround,out var streamedNormal),"Far-side landing builds a collidable streamed terrain patch");
            Vector3 tangent=Vector3.ProjectOnPlane(Vector3.forward,streamedNormal).normalized;
            if(tangent.sqrMagnitude<.01f)tangent=Vector3.ProjectOnPlane(Vector3.right,streamedNormal).normalized;
            DesktopCheck(world.TrySurface(farWalker+tangent*4,out var walkedGround,out var walkedNormal)&&Vector3.Dot(streamedNormal,walkedNormal)>.8f&&Vector3.Distance(streamedGround,walkedGround)<5,"On-foot steps remain attached to the far-side tangent terrain");
            surfaceLanding=false;docking=false;flying=previousFlying;gearDown=previousGear;velocity=previousVelocity;speed=previousSpeed;ship.position=previousPosition;ship.rotation=previousRotation;
        }
        void FrameDesktop(params Key[] keys)
        {InputSystem.QueueStateEvent(verificationMouse,new MouseState());InputSystem.QueueStateEvent(verificationKeyboard,new KeyboardState(keys));InputSystem.Update();focused=true;inputNeutral=false;Flight(.02f);}
        void DesktopCapture(string name)
        {
            cabin.GetComponent<CabinLighting>().SendMessage("LateUpdate");
            var target=new RenderTexture(1600,1000,24);var previous=view.targetTexture;var rect=view.rect;float aspect=view.aspect;var active=RenderTexture.active;
            try{
                view.rect=new Rect(0,0,1,1);view.aspect=1.6f;view.targetTexture=target;
                RenderPipeline.SubmitRenderRequest(view,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;
                var image=new Texture2D(1600,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1000),0,0);image.Apply();File.WriteAllBytes(Path.Combine(verificationDirectory,name+".png"),image.EncodeToPNG());Destroy(image);
            }finally{view.targetTexture=previous;view.rect=rect;view.aspect=aspect;RenderTexture.active=active;Destroy(target);}
        }
    }
}
