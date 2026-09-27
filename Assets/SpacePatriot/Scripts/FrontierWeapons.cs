using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
namespace SpacePatriot
{
    public sealed class WeaponSpec
    {
        public string id,name;public float speed,damage,interval,reload,range,heat;public int mag,reserve;
        public WeaponSpec(string key,string title,float velocity,float hit,float delay,int magazine,int spare,float reloadSeconds,float distance,float heating){id=key;name=title;speed=velocity;damage=hit;interval=delay;mag=magazine;reserve=spare;reload=reloadSeconds;range=distance;heat=heating;}
        public static readonly WeaponSpec[] All={new("kinetic","K-28 TWIN AUTOCANNON",1650,14,.1f,120,960,2.7f,3200,1.2f),new("laser","L-9 PULSE ARRAY",0,19,.2f,100,0,0,2400,7.5f),new("missile","M-6 IR SEEKER",320,105,1,6,12,4.5f,5000,12),new("rifle","AR-30 SERVICE RIFLE",820,22,.12f,30,180,1.9f,800,1.8f),new("sidearm","P-12 SIDEARM",550,29,.28f,12,72,1.4f,400,1.5f)};
    }
    [Serializable] public class WeaponAmmo {public int mag,reserve;public WeaponAmmo(int m,int r){mag=m;reserve=r;}}
    public partial class FrontierGame
    {
        int shipWeapon,groundWeapon=3,weaponVisualIndex=-1;
        float reloadRemaining,capacitor=100,lockProgress;
        int reloadWeapon=-1;
        Raider selectedTarget;
        Transform weaponVisual;
        public int WeaponIndex=>walking||aboard?groundWeapon:shipWeapon;
        public WeaponSpec Weapon=>WeaponSpec.All[WeaponIndex];
        public WeaponAmmo Ammo=>save.ammo[WeaponIndex];
        void UpdateWeapons(float dt)
        {
            if(!started||dead||menu||bindings==null||view==null||save==null||save.ammo==null)return;
            var pad=Gamepad.current;
            if(bindings.Down("Arm weapons"))armed=!armed;
            if(Down(Key.Digit1)){if(walking||aboard)groundWeapon=3;else shipWeapon=0;armed=true;}
            if(Down(Key.Digit2)){if(walking||aboard)groundWeapon=4;else shipWeapon=1;armed=true;}
            if(Down(Key.Digit3)&&!walking&&!aboard){shipWeapon=2;armed=true;}
            if(bindings.Down("Reload")||pad?.buttonWest.wasPressedThisFrame==true)ReloadWeapon();
            if(bindings.Down("Target")||pad?.buttonNorth.wasPressedThisFrame==true){int previous=selectedTarget==null?-1:raiders.IndexOf(selectedTarget);selectedTarget=null;for(int i=1;i<=raiders.Count;i++){var r=raiders[(previous+i)%raiders.Count];if(!r.dead){selectedTarget=r;break;}}lockProgress=0;}
            bool lockable=selectedTarget!=null&&!selectedTarget.dead&&Vector3.Angle(view.transform.forward,selectedTarget.body.position-view.transform.position)<12&&Vector3.Distance(view.transform.position,selectedTarget.body.position)<5000;
            lockProgress=Mathf.MoveTowards(lockProgress,lockable?1:0,dt*.65f);
            capacitor=Mathf.Min(100,capacitor+dt*15);
            if(reloadRemaining>0){reloadRemaining-=dt;if(reloadRemaining<=0){var w=WeaponSpec.All[reloadWeapon];var a=save.ammo[reloadWeapon];int count=Mathf.Min(w.mag-a.mag,a.reserve);a.mag+=count;a.reserve-=count;reloadWeapon=-1;Save();}}
            bool onFoot=walking||aboard;
            if(weaponVisualIndex!=groundWeapon||weaponVisual==null){if(weaponVisual!=null)Destroy(weaponVisual.gameObject);weaponVisual=Instantiate(Resources.Load<GameObject>("OriginalShips/"+(groundWeapon==3?"rifle":"sidearm")),view.transform).transform;weaponVisualIndex=groundWeapon;weaponVisual.localPosition=new Vector3(.23f,-.27f,.42f);weaponVisual.localRotation=Quaternion.identity;foreach(var t in weaponVisual.GetComponentsInChildren<Transform>())t.gameObject.layer=2;}
            weaponVisual.gameObject.SetActive(onFoot&&armed);
            if(onFoot&&armed){bool aiming=MouseButton(1)||pad?.leftTrigger.ReadValue()>.3f;weaponVisual.localPosition=Vector3.Lerp(weaponVisual.localPosition,aiming?new Vector3(0,-.17f,.35f):new Vector3(.23f,-.27f,.42f),dt*12);weaponVisual.localRotation=Quaternion.Euler(reloadRemaining>0?25:0,0,0);
                if((MouseButton(0)||pad?.rightTrigger.ReadValue()>.3f)&&Time.time>fireTime)Fire();}
        }
        void ReloadWeapon(){if(Weapon.id=="laser"||reloadRemaining>0||Ammo.mag>=Weapon.mag||Ammo.reserve<=0)return;reloadWeapon=WeaponIndex;reloadRemaining=Weapon.reload;Toast("Reloading "+Weapon.name+".");}
        void Fire()
        {
            if(!armed||reloadRemaining>0||heat>=95||cruise||!(walking||aboard)&&save.vessel.Factor("weapons",powered)<.05f)return;
            var spec=Weapon;if(spec.id=="laser"&&capacitor<12||spec.id!="laser"&&Ammo.mag<=0){Toast(spec.id=="laser"?"Capacitor charging.":"Magazine empty. R reloads.");fireTime=Time.time+.3f;return;}
            if(spec.id=="missile"&&lockProgress<1){Toast("Select a target with C and keep it inside the reticle until seeker lock.");fireTime=Time.time+.3f;return;}
            fireTime=Time.time+spec.interval;heat+=spec.heat;if(spec.id=="laser")capacitor-=12;else Ammo.mag--;
            Vector3 direction=walking||aboard?view.transform.forward:ship.forward,origin=walking||aboard?view.transform.position+direction*.4f:ship.position+direction*Spec.length*.5f;
            WeaponShot(spec.id,origin);
            effects.Muzzle(origin,direction,spec.id=="laser");
            if(spec.id=="laser"){
                Vector3 end=origin+direction*spec.range;float distance=spec.range;if(Physics.Raycast(origin,direction,out var hit,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)){distance=hit.distance;end=hit.point;}
                Raider enemy=null;foreach(var r in raiders){if(r.dead)continue;float? t=SegmentHit(origin,end,r.body.position,4);if(t.HasValue){end=Vector3.Lerp(origin,end,t.Value);enemy=r;}}
                if(enemy!=null)HitRaider(enemy,spec.damage);
                if(enemy!=null||distance<spec.range)effects.Impact(end,-direction);
                var go=new GameObject("Red pulse beam");var line=go.AddComponent<LineRenderer>();line.sharedMaterial=IndustrialArt.Glow("Pulse red",new Color(1,.025f,.015f),4);line.positionCount=2;line.SetPosition(0,origin);line.SetPosition(1,end);line.startWidth=.065f;line.endWidth=.025f;Destroy(go,.09f);return;
            }
            SpawnBolt(origin,direction*spec.speed+(walking?Vector3.zero:velocity),false,spec);
        }
        static float? SegmentHit(Vector3 a,Vector3 b,Vector3 center,float radius)
        {var d=b-a;var oc=a-center;float aa=Vector3.Dot(d,d),bb=2*Vector3.Dot(oc,d),cc=Vector3.Dot(oc,oc)-radius*radius;if(cc<=0)return 0;if(aa<.000001f)return null;float disc=bb*bb-4*aa*cc;if(disc<0)return null;float t=(-bb-Mathf.Sqrt(disc))/(2*aa);return t>=0&&t<=1?t:(float?)null;}
        void HitRaider(Raider r,float amount)
        {if(r.dead)return;r.hp-=amount;Sound(impactClip,.25f);if(r.hp>0)return;r.dead=true;Burst(r.body.position);Destroy(r.body.gameObject);save.kills++;save.credits+=90;Signal("combat");Save();Toast("Hostile disabled. Recovery contract +90 cr.");}
        void WeaponsHud()
        {
            if(!started||menu||cockpit&&!walking&&!aboard&&!armed)return;string ammo=Weapon.id=="laser"?capacitor.ToString("0")+"%":Ammo.mag+" / "+Ammo.reserve;
            Text(Weapon.name+"   "+ammo+"   "+(reloadRemaining>0?"RELOADING":armed?"ARMED":"SAFE"),465,794,510,30,14,amber,true,TextAnchor.UpperCenter);
            if(selectedTarget!=null&&!selectedTarget.dead){Marker("TARGET / "+selectedTarget.hp.ToString("0")+"%",selectedTarget.body.position,amber);if(Weapon.id=="missile")Text("SEEKER  "+(lockProgress*100).ToString("0")+"%",500,600,440,30,16,amber,true,TextAnchor.MiddleCenter);}
        }
    }
}
