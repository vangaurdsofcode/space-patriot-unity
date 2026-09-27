using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SpacePatriot;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Exercises the same physical collider/PointCockpit route used by the pilot.
// Run in Play Mode after other live Editor validation has released the scene.
public static class CockpitMfdValidation
{
    public static void Runtime()
    {
        using var scope=new ValidationInputScope();
        var game=FrontierGame.Instance;
        if(!Application.isPlaying||game==null)throw new InvalidOperationException("A running FrontierGame is required.");
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var type=typeof(FrontierGame);
        object Get(string name)=>type.GetField(name,flags).GetValue(game);
        void Set(string name,object value)=>type.GetField(name,flags).SetValue(game,value);
        object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(game,args);
        var log=new List<string>();
        void Check(bool value,string name){if(!value)throw new Exception(name);log.Add("PASS "+name);}
        var stateNames=new[]{"selectedWorld","commChannel","radarRange","powerBus","mfdNext","inputNeutral","cockpitHint"};
        var state=stateNames.ToDictionary(name=>name,Get);
        var pages=(int[])Get("mfdPage");var oldPages=(int[])pages.Clone();
        bool oldMenu=game.menu,oldCockpit=game.cockpit,oldWalking=game.walking,oldAboard=game.aboard;
        string oldPage=game.page;float oldThrottle=game.throttle;
        var cameraPosition=game.view.transform.position;var cameraRotation=game.view.transform.rotation;float cameraFov=game.view.fieldOfView;
        bool cabinActive=game.cabin.gameObject.activeSelf;
        var exterior=(Transform)Get("exterior");bool exteriorActive=exterior.gameObject.activeSelf;
        var oldLock=Cursor.lockState;bool oldCursor=Cursor.visible;
        var previousMouse=Mouse.current;var mouse=InputSystem.AddDevice<Mouse>();
        var bus=game.save.vessel;int engines=bus.engines,weapons=bus.weapons,shields=bus.shields;
        void Point(CockpitControl control,bool click=false,float scroll=0)
        {
            game.menu=false;
            InputSystem.ResetDevice(mouse);
            var p=game.view.WorldToScreenPoint(control.GetComponent<Renderer>().bounds.center);
            Check(p.z>0,"Control "+control.name+" is in front of the pilot camera");
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(p.x,p.y),buttons=(ushort)(click?1:0),scroll=new Vector2(0,scroll)});
            InputSystem.Update();Call("PointCockpit");
        }
        try
        {
            game.cockpit=true;game.walking=false;game.aboard=false;game.menu=false;
            Call("FollowCamera",10f);Physics.SyncTransforms();
            var controls=game.cabin.GetComponentsInChildren<CockpitControl>();
            var displays=Enumerable.Range(0,3).Select(i=>controls.First(c=>c.screen==i)).ToArray();
            pages[0]=pages[1]=pages[2]=0;
            Point(displays[0],true);Check(game.menu&&game.page=="systems","Flight display opens vessel systems through its physical screen");
            Point(displays[1],true);Check(game.menu&&game.page=="navigation","Navigation display opens the plotted-course menu");
            Point(displays[2],true);Check(game.menu&&game.page=="systems","Systems display opens vessel systems");
            pages[2]=2;Point(displays[2],true);Check(game.menu&&game.page=="cargo","Cargo manifest display opens cargo handling");
            Check(Cursor.lockState==CursorLockMode.None&&Cursor.visible,"Display menus release and reveal the cursor");
            pages[1]=2;Point(displays[1],false,120);Check(!game.menu&&pages[1]==0,"Display wheel advances and wraps pages without opening a menu");
            Point(displays[1],false,-120);Check(pages[1]==2,"Display wheel reverses and wraps pages");
            int destination=(int)Get("selectedWorld");Point(controls.First(c=>c.action==40),false,120);
            Check((int)Get("selectedWorld")==((destination+1)%game.worlds.Length)&&pages[1]==1,"NAV knob selects a destination and immediately shows the route");
            Point(controls.First(c=>c.action==41),false,120);Check(pages[1]==2,"COMM knob immediately shows traffic channel");
            Point(controls.First(c=>c.action==42),false,120);Check(pages[1]==0,"SENSOR knob immediately shows radar");
            game.throttle=1;pages[0]=2;Point(controls.First(c=>c.action==43),false,120);Check(pages[0]==0&&game.throttle>1,"DRIVE knob shows changed speed limit on the flight page");
            pages[2]=0;Set("powerBus",0);bus.engines=bus.weapons=bus.shields=4;Point(controls.First(c=>c.action==124),true);Point(controls.First(c=>c.action==122),true);
            Check(pages[2]==1&&bus.weapons==5&&bus.engines+bus.weapons+bus.shields==12,"Physical power softkeys reveal distribution and reallocate actual power");
            Point(controls.First(c=>c.action==126));Check(((string)Get("cockpitHint")).Contains("Open cargo handling"),"Softkey hover names its actual action");
            Point(controls.First(c=>c.action==126),true);Check(game.menu&&game.page=="cargo"&&pages[2]==2,"Cargo softkey and manifest share the same menu route");
            Point(controls.First(c=>c.action==127),true);Check(game.menu&&game.page=="systems","Systems softkey opens the systems menu");
            game.menu=false;Set("mfdNext",0f);Call("UpdateMfd");
            Directory.CreateDirectory("Validation");
            var canvases=(MfdCanvas[])Get("mfd");
            Check(canvases.Select(c=>c.texture.GetInstanceID()).Distinct().Count()==3,"All three displays retain independent live textures");
            for(int i=0;i<canvases.Length;i++)File.WriteAllBytes("Validation/cockpit-mfd-menu-"+i+".png",canvases[i].texture.EncodeToPNG());
            CaptureCockpit(game.view,"Validation/cockpit-mfd-menu-cockpit.png");
            File.WriteAllLines("Validation/cockpit-mfd-menu-runtime.txt",log);
            Debug.Log("COCKPIT_MFD_MENU_PASS "+log.Count);
        }
        finally
        {
            InputSystem.RemoveDevice(mouse);if(previousMouse!=null)previousMouse.MakeCurrent();
            foreach(var item in state)Set(item.Key,item.Value);
            Array.Copy(oldPages,pages,pages.Length);
            bus.engines=engines;bus.weapons=weapons;bus.shields=shields;
            game.menu=oldMenu;game.page=oldPage;game.cockpit=oldCockpit;game.walking=oldWalking;game.aboard=oldAboard;game.throttle=oldThrottle;
            game.view.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
            game.view.fieldOfView=cameraFov;
            game.cabin.gameObject.SetActive(cabinActive);exterior.gameObject.SetActive(exteriorActive);
            Cursor.lockState=oldLock;Cursor.visible=oldCursor;
            Set("mfdNext",0f);Call("UpdateMfd");
        }
    }
    static void CaptureCockpit(Camera camera,string path)
    {
        const int width=1440,height=900;
        var target=new RenderTexture(width,height,24);
        var previous=camera.targetTexture;var active=RenderTexture.active;var rect=camera.rect;
        Texture2D image=null;
        try
        {
            camera.rect=new Rect(0,0,1,1);camera.targetTexture=target;
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;image=new Texture2D(width,height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture=previous;camera.rect=rect;RenderTexture.active=active;
            if(image)UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
