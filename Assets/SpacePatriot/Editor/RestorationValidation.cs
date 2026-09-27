using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEditor;
using SpacePatriot;
public static class RestorationValidation
{
    [MenuItem("Space Patriot/Validate restored controls and cargo")]
    public static void Run()
    {
        var results=new List<string>();
        void Check(bool ok,string text){if(!ok)throw new Exception("RESTORATION TEST FAILED: "+text);results.Add("PASS "+text);}
        var keys=new FlightBindings();
        // Test defaults without overwriting any user rebindings.
        if(!PlayerPrefs.HasKey("sp.bindings")){
            Check(keys.KeyFor("Strafe left")==Key.A&&keys.KeyFor("Strafe right")==Key.D,"A/D strafe bindings");
            Check(keys.KeyFor("Ascend")==Key.Space&&keys.KeyFor("Descend")==Key.LeftCtrl,"Space/Ctrl vertical bindings");
            Check(keys.KeyFor("Brake")==Key.X&&keys.KeyFor("Camera")==Key.V,"X brake and V camera bindings");}
        Vector3 Step(Vector3 velocity,Vector3 input,bool assist,bool brake=false,bool power=true)=>FlightMotor.Step(velocity,Quaternion.identity,input,100,20,.1f,assist,brake,power);
        Check(Step(Vector3.zero,Vector3.right,true).x>0,"Strafe creates lateral thrust");
        Check(Step(Vector3.zero,Vector3.back,true).z<0,"Reverse thrust works from rest");
        Check(Step(Vector3.zero,Vector3.up,true).y>0&&Step(Vector3.zero,Vector3.down,true).y<0,"Both vertical thrust axes work");
        Vector3 start=new Vector3(25,0,0),result=Step(start,Vector3.forward,false);
        Check(Mathf.Abs(result.x-25)<.001f&&result.z>0,"Decoupled forward thrust preserves lateral momentum");
        Check(Step(start,Vector3.zero,false)==start,"Decoupled neutral input coasts");
        Check(Step(start,Vector3.zero,true).magnitude<25,"Assist counters drift on release");
        Check(Step(start,Vector3.zero,false,true).magnitude<25,"Brake works with assist off");
        Check(Step(start,Vector3.zero,true,true,false)==start,"Unpowered ship cannot magically brake");
        var q=Quaternion.identity;for(int i=0;i<120;i++)q=FlightMotor.Rotate(q,new Vector3(0,0,90),1/60f);
        Check(Vector3.Dot(q*Vector3.up,Vector3.down)>.999f,"Roll permits inversion without an upright clamp");
        var s=new SaveData{organics=0,ore=0,crystal=0};CargoHandling.Stage(s,2,"ore",2);
        Check(!CargoHandling.Transfer(s,3,"ore",32,true),"Cargo cannot load from a different world's dock");
        Check(CargoHandling.Transfer(s,2,"ore",1,true)&&s.ore==1&&CargoHandling.Staged(s,2,"ore")==1,"Loading transfers exactly one unit");
        Check(!CargoHandling.Transfer(s,2,"ore",1,true),"Hold capacity blocks loading without deleting dock cargo");
        Check(CargoHandling.Transfer(s,2,"ore",1,false)&&s.ore==0&&CargoHandling.Staged(s,2,"ore")==2,"Unloading conserves cargo");
        Check(!CargoHandling.Transfer(s,2,"ore",1,false),"Cannot unload an empty hold");
        var rt=JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(s));Check(CargoHandling.Staged(rt,2,"ore")==2,"Dock inventory survives save round trip");
        var systems=new VesselState();systems.Allocate("engines",1);Check(systems.engines+systems.weapons+systems.shields==12,"Power redistribution conserves 12 points");
        systems.Get("engines").enabled=false;Check(systems.Factor("engines",true)==0,"Disabled engine component removes thrust");
        bool originalsPreserved=ShipSpec.Fleet.Length>=100;
        for(int i=0;i<100&&originalsPreserved;i++)originalsPreserved=ShipSpec.Fleet[i].family==i/10;
        Check(originalsPreserved,"All 100 original craft variants remain in their catalog slots");
        Check(ShipSpec.Fleet[70].length>80&&ShipSpec.Fleet[90].length>210,"Large-ship dimensions are preserved");
        foreach(int family in new[]{0,1,2,3,4,5,6,7,8,9})Check(Resources.Load<GameObject>("OriginalShips/hull-"+family)!=null,"Original hull prefab "+family);
        foreach(var prefab in Resources.LoadAll<GameObject>("OriginalShips")){int missing=0;foreach(var node in prefab.GetComponentsInChildren<Transform>(true))missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject);Check(missing==0,"No missing script references: "+prefab.name);}
        Check(Resources.Load<GameObject>("OriginalShips/cabin-0").GetComponentsInChildren<CockpitControl>().Length==37,"Six switches, four rotary controls, 24 MFD softkeys and three displays survive prefab serialization");
        Check(Resources.LoadAll<Material>("OriginalSurfaces").Length==128,"All 128 atlas cells available in Unity");
        var worlds=JsonUtility.FromJson<WorldCatalog>(Resources.Load<TextAsset>("Worlds").text).worlds;
        var trade=new SaveData();FrontierEconomy.Ensure(trade,worlds);Check(trade.economy.markets.Count==19,"Every world has an independent persistent market");
        for(int i=0;i<300;i++)FrontierEconomy.Tick(trade,worlds,.5f);
        Check(trade.economy.convoys.Count>0&&trade.economy.markets.Exists(m=>m.activity>0),"Background convoys depart and deliver goods");
        Check(trade.economy.markets.TrueForAll(m=>m.ore>=0&&m.organics>=0&&m.crystal>=0)&&trade.economy.factions.TrueForAll(f=>f.treasury>=0),"Market and faction balances remain nonnegative during simulation");
        trade.world=1;var job=new FreightJob{from=worlds[0].id,to=worlds[1].id,good="ore",units=4,reward=240,status="in transit"};trade.economy.jobs.Add(job);trade.ore=4;
        Check(!FrontierEconomy.Deliver(trade,worlds[1],job),"Freight cannot be delivered straight from the hold");
        for(int i=0;i<4;i++)CargoHandling.Transfer(trade,1,"ore",32,false);int credits=trade.credits;
        Check(FrontierEconomy.Deliver(trade,worlds[1],job)&&trade.credits==credits+240&&CargoHandling.Staged(trade,1,"ore")==0,"Unloaded freight is consumed and paid exactly once");
        Check(!FrontierEconomy.Deliver(trade,worlds[1],job)&&trade.credits==credits+240,"Completed freight contracts cannot pay twice");
        var plans=JsonUtility.FromJson<DeckCatalog>(Resources.Load<TextAsset>("DeckPlans").text).plans;
        foreach(var plan in plans){var open=new HashSet<string>();foreach(var door in plan.doors)open.Add(door.id);
            // Flood actual collision boundaries to prove the flight deck connects to each room.
            var seen=new HashSet<Vector2Int>();var pending=new Queue<Vector2Int>();pending.Enqueue(new Vector2Int(0,0));
            while(pending.Count>0){var p=pending.Dequeue();if(seen.Contains(p)||!FrontierGame.DeckWalkable(plan,open,new Vector3(p.x*.2f,0,-p.y*.2f)))continue;seen.Add(p);
                pending.Enqueue(p+Vector2Int.up);pending.Enqueue(p+Vector2Int.down);pending.Enqueue(p+Vector2Int.left);pending.Enqueue(p+Vector2Int.right);}
            foreach(var room in plan.rooms){if(room.deck!=0)continue;bool accessible=false;foreach(var p in seen)if(p.x*.2f>room.x0+.4f&&p.x*.2f<room.x1-.4f&&p.y*.2f>room.z0+.4f&&p.y*.2f<room.z1-.4f){accessible=true;break;}
                Check(accessible,"Connected walkable "+room.name+" on family "+plan.family);}
        }
        Directory.CreateDirectory("Validation");File.WriteAllLines("Validation/restoration-tests.txt",results);Debug.Log("RESTORATION_VALIDATION_PASS: "+results.Count+" checks. These do not certify gameplay or full feature parity.");
    }
}
