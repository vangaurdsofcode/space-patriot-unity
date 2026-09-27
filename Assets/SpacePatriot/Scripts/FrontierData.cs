using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpacePatriot
{
    [Serializable] public class WorldCatalog { public WorldInfo[] worlds; }
    [Serializable] public class WorldInfo
    {
        public string id, name, system, biome;
        public int seed;
        public float temperature, radius, stellarTemperatureK=5772f;
        public bool AlienPhotosynthesis=>stellarTemperatureK>0&&stellarTemperatureK<4400f;
        public Color GrassPigment(float moisture)
        {
            float m=Mathf.Clamp01(moisture);
            Color baseColor=AlienPhotosynthesis?new Color(.065f,.24f,.45f):new Color(.18f,.42f,.075f);
            Color dryColor=AlienPhotosynthesis?new Color(.30f,.12f,.34f):new Color(.43f,.36f,.095f);
            float factor=(.7f+m*.6f)*(1-(1-m)*.35f),dry=(1-m)*.35f;
            return new Color(baseColor.r*factor+dryColor.r*dry,baseColor.g*factor+dryColor.g*dry,baseColor.b*factor+dryColor.b*dry,1);
        }
        public Color FoliagePigment=>AlienPhotosynthesis?new Color(.42f,.15f,.49f):new Color(.55f,.65f,.39f);
        public Color Surface => biome switch {
            "temperate" => new Color(.22f,.28f,.22f), "desert" => new Color(.43f,.23f,.14f),
            "ice" => new Color(.39f,.48f,.51f), "volcanic" => new Color(.22f,.18f,.16f),
            "gas" => new Color(.29f,.37f,.44f), _ => new Color(.31f,.29f,.25f) };
    }
    [Serializable] public class CampaignCatalog { public CaseFile[] arcs; }
    [Serializable] public class CaseFile
    {
        public string id, title, speaker, brief;
        public string[] requires;
        public CaseStep[] steps;
        public CaseChoice[] choices;
    }
    [Serializable] public class CaseStep { public string kind, world, objective, report, good; public int units; }
    [Serializable] public class MarketEffect { public string world, good; public int stock; }
    [Serializable] public class CaseChoice
    {
        public string label, faction, ending;
        public int standing, credits;
        public MarketEffect market;
    }
    [Serializable] public class CaseProgress { public string id; public int step, choice=-1; public bool accepted; }
    [Serializable] public class StockChange { public string world, good; public int units; }
    [Serializable] public class SaveData
    {
        public int version=2, world=2, ship, credits=1800, organics=4, ore, crystal;public string settlement="";
        public int union, helix, redwake, kills;
        public float hull=100, fuel=100,crewHealth=100;
        public bool striderOwned, wayfarerOwned;
        public List<int> ownedShips=new List<int>{0};
        public List<DockCargo> dockCargo=new List<DockCargo>();
        public VesselState vessel=new VesselState();
        public EconomyState economy=new EconomyState();
        public SocietyState society=new SocietyState();
        public FieldInventoryState inventory=new FieldInventoryState();
        public WeaponAmmo[] ammo=Array.ConvertAll(WeaponSpec.All,w=>new WeaponAmmo(w.mag,w.reserve));
        public List<CaseProgress> cases=new List<CaseProgress>();
        public List<StockChange> stock=new List<StockChange>();
        public List<string> journal=new List<string>();
    }
    [Serializable] public class FleetCatalog { public OriginalCraft[] crafts; }
    [Serializable] public class OriginalCraft
    {
        public string id,name,className,description; public int index,family,interiorFamily,variant,capacity,engines;
        public float speed,acceleration,turn,mass,hyperSpeed,fuelRate; public float[] dimensions,paint,accent;
    }
    public sealed class ShipSpec
    {
        public string name,designation,role,description;
        public float speed,thrust,turn,health=100,mass,length,width,height;
        public int capacity,price,family,interiorFamily,variant;
        public Color paint;
        public ShipSpec(OriginalCraft c){name=c.name;designation=c.id;role=c.className;description=c.description;
            speed=c.speed;thrust=c.acceleration;turn=c.turn*70;capacity=c.capacity;price=Mathf.RoundToInt(c.dimensions[0]*8);
            family=c.family;interiorFamily=family<0?c.interiorFamily:family;variant=c.variant;mass=c.mass;length=c.dimensions[0];width=c.dimensions[1];height=c.dimensions[2];paint=new Color(c.paint[0],c.paint[1],c.paint[2]);}
        static ShipSpec[] fleet;
        public static ShipSpec[] Fleet { get { if(fleet==null){var records=JsonUtility.FromJson<FleetCatalog>(Resources.Load<TextAsset>("Fleet").text).crafts;fleet=Array.ConvertAll(records,c=>new ShipSpec(c));}return fleet; } }
    }
    public static class Progression
    {
        public static CaseProgress Get(SaveData save,string id)
        {
            var p=save.cases.Find(x=>x.id==id);
            if(p==null){p=new CaseProgress{id=id};save.cases.Add(p);} return p;
        }
        public static bool Unlocked(SaveData save,CaseFile file)
        {
            foreach(var id in file.requires) if(Get(save,id).choice<0) return false;
            return true;
        }
        public static int Cargo(SaveData s,string good)=>good=="ore"?s.ore:good=="crystal"?s.crystal:s.organics;
        public static void AddCargo(SaveData s,string good,int amount)
        { if(good=="ore")s.ore+=amount;else if(good=="crystal")s.crystal+=amount;else s.organics+=amount; }
        public static int Used(SaveData s)=>s.organics+s.ore+s.crystal;
        public static int Price(SaveData s,WorldInfo w,string good)
            =>FrontierEconomy.Price(s,w,good);
        public static bool CompleteStep(SaveData save,CaseFile file,string world,string kind,out string report)
        {
            report=null;var p=Get(save,file.id);
            if(!p.accepted||p.choice>=0||p.step>=file.steps.Length)return false;
            var step=file.steps[p.step];
            if(step.world!=world||step.kind!=kind)return false;
            if(step.kind=="delivery")
            { if(Cargo(save,step.good)<step.units)return false; AddCargo(save,step.good,-step.units); }
            report=step.report;p.step++;save.journal.Add(file.title+" — "+report);return true;
        }
        public static bool Resolve(SaveData save,CaseFile file,int index)
        {
            var p=Get(save,file.id);
            if(!p.accepted||p.choice>=0||p.step<file.steps.Length||index<0||index>=file.choices.Length)return false;
            var c=file.choices[index];p.choice=index;save.credits+=c.credits;
            if(c.faction=="union")save.union+=c.standing;else if(c.faction=="helix")save.helix+=c.standing;else save.redwake+=c.standing;
            save.stock.Add(new StockChange{world=c.market.world,good=c.market.good,units=c.market.stock});
            save.journal.Add(file.title+" — "+c.ending);return true;
        }
    }
}
