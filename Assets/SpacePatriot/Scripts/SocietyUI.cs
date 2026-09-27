using System;
using System.Collections;
using System.Linq;
using UnityEngine;
namespace SpacePatriot
{
    public partial class FrontierGame
    {
        Vector2 jobScroll,settlementScroll;string trackedJob="",selectedSettlement="";
        int lastCityNode=-1;
        void SocietyJobsPanel()
        {
            var city=SocietyEconomy.City(save,save.settlement);var info=Array.Find(SocietyEconomy.Catalog,c=>c.id==save.settlement);
            Text(info.name.ToUpperInvariant()+" / COMMUNITY CONTRACTS",400,229,920,28,16,amber,true);
            Text("Food "+city.food.ToString("0")+"  Water "+city.water.ToString("0")+"  Power "+city.power.ToString("0")+"  Health "+city.health.ToString("0")+"  Security "+city.security.ToString("0"),400,265,914,26,16,aqua);
            var jobs=save.society.jobs.Where(j=>j.city==save.settlement&&(j.status=="offered"||j.status=="accepted")).ToArray();
            if(jobs.Length==0)Text("No outstanding requests here. Residents continue their shifts; new work appears when local services need it.",400,330,890,100,21,muted);
            jobScroll=GUI.BeginScrollView(new Rect(399,310,923,383),jobScroll,new Rect(0,0,890,Mathf.Max(380,jobs.Length*148)));
            for(int i=0;i<jobs.Length;i++){var j=jobs[i];float y=i*148;Text(j.title,0,y,620,28,22,paper,true);Text(j.brief,0,y+35,615,69,16,muted);Text(j.reward+" CR · "+Mathf.CeilToInt((float)(j.deadline-save.society.time)/60)+" min · "+j.status,0,y+108,610,24,14,aqua);
                if(Button(j.status=="offered"?"ACCEPT":"TRACK",645,y+5,233,40,j.id==trackedJob)){if(j.status=="offered")SocietyEconomy.Accept(save,j);trackedJob=j.id;Save();Toast("Tracking: "+j.title);}
                if(j.status=="accepted"&&Button("ABANDON",645,y+55,233,34)){j.status="abandoned";if(trackedJob==j.id)trackedJob="";Save();}
            }
            GUI.EndScrollView();
            if(Button("SETTLEMENT DIRECTORY",400,713,440,38))page="settlements";
            if(Button("RESIDENTS & EVENTS",861,713,440,38))page="population";
        }
        void SettlementsPanel()
        {
            Text(CurrentWorld.name.ToUpperInvariant()+" / SETTLEMENT DIRECTORY",400,229,900,28,16,amber,true);
            Text("Select a regional approach. Land and secure the ship before requesting a port transfer.",400,265,900,47,17,muted);
            var places=Array.FindAll(SocietyEconomy.Catalog,c=>c.world==CurrentWorld.id);
            settlementScroll=GUI.BeginScrollView(new Rect(400,323,492,372),settlementScroll,new Rect(0,0,467,places.Length*43));
            for(int i=0;i<places.Length;i++)if(Button((places[i].id==save.settlement?"● ":"")+places[i].name,0,i*43,461,37,places[i].id==selectedSettlement))selectedSettlement=places[i].id;
            GUI.EndScrollView();var selected=Array.Find(places,c=>c.id==selectedSettlement)??Array.Find(places,c=>c.id==save.settlement);
            var city=SocietyEconomy.City(save,selected.id);Text(selected.name,918,327,390,68,27,paper,true);
            Text(selected.kind.ToUpperInvariant()+" / "+city.owner.ToUpperInvariant()+"\n\nResident records: "+save.society.residents.Count(r=>r.homeCity==selected.id)+"\nPower: "+city.power.ToString("0")+"%\nWater: "+city.water.ToString("0")+"%\nFood: "+city.food.ToString("0")+"%\nSecurity: "+city.security.ToString("0")+"%",918,414,385,235,18,muted);
            if(Button(selected.id==save.settlement?"CURRENT SETTLEMENT":"REQUEST APPROACH",918,656,387,44,true,AtPort&&!jumping&&selected.id!=save.settlement))StartCoroutine(ApproachSettlement(selected));
            Text("420 named cities and outposts. Simulation persists while regions are unloaded.",400,721,909,32,15,aqua);
        }
        IEnumerator ApproachSettlement(SettlementInfo destination)
        {
            if(!AtPort||cargoTransfer!=null)yield break;jumping=true;menu=false;selectedWorld=save.world;
            for(float t=0;t<1;t+=Time.deltaTime){travelFade=t;yield return null;}travelFade=1;
            save.settlement=destination.id;world.Generate(CurrentWorld);RespawnShip();walking=true;cockpit=false;SetSurfaceWalker(ship.position+new Vector3(-Spec.width*.65f,0,-3),ship.forward);ClearCombat();SpawnRaiders();RebuildCargo();Save();
            for(float t=1;t>0;t-=Time.deltaTime){travelFade=t;yield return null;}travelFade=0;jumping=false;Toast("Arrived at "+destination.name+". E uses district terminals; community contracts are in Operations.");
        }
        void CityInteraction(int node)
        {
            lastCityNode=node;bool changed=false;
            foreach(var j in save.society.jobs){int stage=j.stage;if(SocietyEconomy.Complete(save,j,save.settlement,node)){changed=true;Toast(j.title+" complete. +"+j.reward+" CR");}else if(stage!=j.stage){changed=true;Toast("Objective recorded: "+j.title+" ("+j.stage+")");}}
            if(changed){Save();RebuildCargo();}
            if(node==2){page="reactor";menu=true;}else {page="community";menu=true;}
        }
        void CompleteDistrictRepair()
        {
            if(lastCityNode!=2||!walking||Vector3.Distance(walkPosition,world.cityCheckpoints[2]+new Vector3(0,1.75f,-5))>7)return;foreach(var j in save.society.jobs)if(j.kind=="repair"&&SocietyEconomy.Complete(save,j,save.settlement,2,true))Toast("District power restored. +"+j.reward+" CR");Save();
        }
        void SocietyHud()
        {
            if(aboard){Text("DECK "+(deckLevel+1)+" / "+deckRoom,440,100,600,28,16,aqua,true);if(activeDeck.decks.Length>1&&Mathf.Abs(deckPosition.x)<1.2f&&Mathf.Abs(deckPosition.z+5)<1.3f)Prompt("E  LIFT DOWN     SHIFT+E  LIFT UP");return;}
            var job=save.society.jobs.Find(j=>j.id==trackedJob&&j.status=="accepted");
            if(job!=null&&!menu){int node=job.kind=="repair"||job.kind=="water"?2:job.kind=="medical"||job.kind=="survey"?4:job.kind=="courier"?(job.stage==0?1:3):job.kind=="patrol"?(job.stage==0?0:job.stage==1?3:2):1;
                Text("COMMUNITY / "+job.title,31,218,600,28,15,aqua,true);if(job.city==save.settlement)Marker(job.title,world.cityCheckpoints[node]+Vector3.up*3,aqua);}
        }
    }
}
