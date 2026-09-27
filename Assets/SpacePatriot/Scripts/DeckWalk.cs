using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
namespace SpacePatriot
{
    [Serializable] public class DeckCatalog {public DeckPlan[] plans;}
    [Serializable] public class DeckPlan {public int family;public float end;public DeckRoom[] rooms;public DeckFixture[] fixtures;public DeckDoor[] doors;public DeckStation[] stations;public DeckLevel[] decks;public DeckStation[] lifts;}
    [Serializable] public class DeckLevel {public int index;public string name;public float y;}
    [Serializable] public class DeckRoom {public string id,name;public float x0,x1,z0,z1,y;public int deck;}
    [Serializable] public class DeckFixture {public string id,type;public float x,z,w,d,h,y;public int deck;public bool solid;}
    [Serializable] public class DeckDoor {public string id,name,axis;public float x,z,width,y;public int deck;}
    [Serializable] public class DeckStation {public string id,name;public float x,z,y;public int deck;}
    public partial class FrontierGame
    {
        DeckPlan activeDeck;
        HashSet<string> openDoors=new HashSet<string>();
        string deckRoom="FLIGHT DECK";
        int deckLevel;float deckLiftTarget;bool deckLiftMoving;Transform deckLiftPlatform;
        void PrepareDeck()
        {
            activeDeck=Array.Find(JsonUtility.FromJson<DeckCatalog>(Resources.Load<TextAsset>("DeckPlans").text).plans,p=>p.family==Spec.interiorFamily);
            if(activeDeck==null)throw new InvalidOperationException("Missing interior plan for "+Spec.name);
            deckLevel=0;deckLiftMoving=false;openDoors.Clear();foreach(var d in activeDeck.doors)openDoors.Add(d.id);
            if(activeDeck.decks.Length>1){deckLiftPlatform=IndustrialArt.Box("Crew service lift",cabin,new Vector3(0,-1.70f,-5),new Vector3(2.16f,.12f,2.26f),IndustrialArt.Steel).transform;}
            foreach(var part in cabin.GetComponentsInChildren<OriginalDoorPart>())part.SetOpen(true,10);
        }
        void WalkDeck(float dt)
        {
            if(activeDeck==null||activeDeck.family!=Spec.interiorFamily)PrepareDeck();
            if(deckLiftMoving){deckPosition.y=Mathf.MoveTowards(deckPosition.y,deckLiftTarget,dt*1.65f);deckLiftPlatform.localPosition=new Vector3(0,deckPosition.y-1.70f,-5);if(Mathf.Abs(deckPosition.y-deckLiftTarget)<.001f){deckLiftMoving=false;Toast("Deck "+(deckLevel+1)+" / "+activeDeck.decks[deckLevel].name);}return;}
            if(MouseButton(1)){var delta=Mouse.current.delta.ReadValue()*PlayerPrefs.GetFloat("sp.sensitivity",1);walkYaw+=delta.x*.12f;walkPitch=Mathf.Clamp(walkPitch+delta.y*.12f*(PlayerPrefs.GetInt("sp.invert",0)==1?1:-1),-75,75);}
            var pad=Gamepad.current;Vector2 sticks=pad?.leftStick.ReadValue()??Vector2.zero;
            var look=pad?.rightStick.ReadValue()??Vector2.zero;walkYaw+=(look.x*110+bindings.Axis("Yaw left","Yaw right")*90)*dt;walkPitch=Mathf.Clamp(walkPitch+(look.y*90*(PlayerPrefs.GetInt("sp.padInvert",0)==1?1:-1)+bindings.Axis("Pitch up","Pitch down")*65)*dt,-75,75);
            var motion=Quaternion.Euler(0,walkYaw,0)*new Vector3(bindings.Axis("Strafe left","Strafe right")+sticks.x,0,bindings.Axis("Reverse","Forward")+sticks.y)*(bindings.Held("Boost")?4.5f:2.4f)*dt;
            int steps=Mathf.Max(1,Mathf.CeilToInt(motion.magnitude/.1f));for(int i=0;i<steps;i++){var next=deckPosition+new Vector3(motion.x/steps,0,0);if(ValidDeck(next))deckPosition=next;next=deckPosition+new Vector3(0,0,motion.z/steps);if(ValidDeck(next))deckPosition=next;}
            deckRoom="CENTRAL PASSAGE";foreach(var room in activeDeck.rooms)if(!room.id.StartsWith("corridor")&&room.deck==deckLevel&&deckPosition.x>=room.x0&&deckPosition.x<=room.x1&&-deckPosition.z>=room.z0&&-deckPosition.z<=room.z1)deckRoom=room.name;
            foreach(var part in cabin.GetComponentsInChildren<OriginalDoorPart>())part.SetOpen(openDoors.Contains(part.id),dt);
            if(bindings.Down("Board / leave seat")){aboard=false;inputNeutral=true;return;}
            if(!Down(Key.E)&&!Down(Key.Z)&&pad?.buttonEast.wasPressedThisFrame!=true)return;
            if(activeDeck.decks.Length>1&&Mathf.Abs(deckPosition.x)<1.05f&&Mathf.Abs(deckPosition.z+5)<1.05f){deckLevel=(deckLevel+(Held(Key.LeftShift)?activeDeck.decks.Length-1:1))%activeDeck.decks.Length;deckLiftTarget=activeDeck.decks[deckLevel].y;deckPosition.x=0;deckPosition.z=-5;deckLiftMoving=true;Toast("Service lift travelling to deck "+(deckLevel+1));return;}
            if(-deckPosition.z>activeDeck.end-2.4f){if(flying){Toast("Airlock interlock: land before disembarking.");return;}aboard=false;walking=true;if(!SetSurfaceWalker(CargoAccess,ship.forward))aboard=true;return;}
            foreach(var station in activeDeck.stations)if(station.deck==deckLevel&&Vector2.Distance(new Vector2(deckPosition.x,-deckPosition.z),new Vector2(station.x,station.z))<1.3f){
                if(station.id=="pilot"){aboard=false;inputNeutral=true;}
                else if(station.id=="medical"){if(save.crewHealth<100&&save.vessel.spares>=1){save.vessel.spares--;save.crewHealth=Mathf.Min(100,save.crewHealth+40);Toast("Medical consumables replenished; crew recovery complete.");Save();}else Toast("Medical treatment requires an injury and one supply component.");}
                else {page=station.id=="cargo"?"cargo":station.id=="science"?"inventory":"systems";menu=true;}return;}
            foreach(var door in activeDeck.doors)if(door.deck==deckLevel&&Vector2.Distance(new Vector2(deckPosition.x,-deckPosition.z),new Vector2(door.x,door.z))<2.2f){if(openDoors.Contains(door.id)){openDoors.Remove(door.id);if(!ValidDeck(deckPosition)){openDoors.Add(door.id);Toast("Bulkhead occupied. Step clear before closing.");return;}}else openDoors.Add(door.id);Toast(door.name+(openDoors.Contains(door.id)?" open.":" closed."));return;}
            if(deckRoom=="CARGO HOLD"){menu=true;page="cargo";}
        }
        bool ValidDeck(Vector3 position)
            =>DeckWalkable(activeDeck,openDoors,position);
        public static bool DeckWalkable(DeckPlan activeDeck,HashSet<string> openDoors,Vector3 position)
        {
            float x=position.x,z=-position.z;
            // Erode the union, not each room: adjacent doorways must remain connected.
            for(int i=0;i<12;i++){float a=i*Mathf.PI/6,px=x+Mathf.Cos(a)*.25f,pz=z+Mathf.Sin(a)*.25f;bool inside=false;
                foreach(var r in activeDeck.rooms)if(Mathf.Abs(position.y-r.y)<.2f&&px>=r.x0&&px<=r.x1&&pz>=r.z0&&pz<=r.z1){inside=true;break;}if(!inside)return false;}
            foreach(var f in activeDeck.fixtures)if(Mathf.Abs(position.y-f.y)<.2f&&f.solid&&Mathf.Abs(x-f.x)<f.w*.5f+.25f&&Mathf.Abs(z-f.z)<f.d*.5f+.25f)return false;
            foreach(var d in activeDeck.doors)if(Mathf.Abs(position.y-d.y)<.2f&&!openDoors.Contains(d.id)&&(d.axis=="z"?Mathf.Abs(z-d.z)<.4f&&Mathf.Abs(x-d.x)<d.width*.5f+.25f:Mathf.Abs(x-d.x)<.4f&&Mathf.Abs(z-d.z)<d.width*.5f+.25f))return false;
            // Side bulkheads are solid except their measured openings, even while the room unions overlap.
            foreach(var r in activeDeck.rooms){if(r.id.StartsWith("corridor")||Mathf.Abs(position.y-r.y)>.2f)continue;foreach(float edge in new[]{r.x0,r.x1})if(Mathf.Abs(Mathf.Abs(edge)-1.2f)<.01f&&Mathf.Abs(x-edge)<.32f&&z>r.z0-.25f&&z<r.z1+.25f){bool portal=false;foreach(var d in activeDeck.doors)if(d.deck==r.deck&&d.axis=="x"&&Mathf.Abs(d.x-edge)<.1f&&Mathf.Abs(z-d.z)<d.width/2-.25f)portal=true;if(!portal)return false;}}
            return true;
        }
    }
}
