using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
namespace SpacePatriot
{
    // Three independent native displays, refreshed at 8 Hz. Textures are owned
    // by this component and disposed when changing vessels; no baked fake values.
    public sealed class MfdCanvas : IDisposable
    {
        public const int W=640,H=448;public readonly Texture2D texture;readonly Color32[] pixels=new Color32[W*H];
        readonly Color32 ink=new Color32(180,199,127,255),dim=new Color32(63,80,46,255),black=new Color32(9,17,15,255);
        static readonly Dictionary<char,string> Font=new(){
            ['A']="0E11111F111111",['B']="1E11111E11111E",['C']="0F10101010100F",['D']="1E11111111111E",['E']="1F10101E10101F",['F']="1F10101E101010",['G']="0F10101711110F",['H']="1111111F111111",['I']="0E04040404040E",['J']="0702020212120C",['K']="11121418141211",['L']="1010101010101F",['M']="111B1515111111",['N']="11191513111111",['O']="0E11111111110E",['P']="1E11111E101010",['Q']="0E11111115120D",['R']="1E11111E141211",['S']="0F10100E01011E",['T']="1F040404040404",['U']="1111111111110E",['V']="11111111110A04",['W']="11111115151B11",['X']="11110A040A1111",['Y']="11110A04040404",['Z']="1F01020408101F",['0']="0E11131519110E",['1']="040C040404040E",['2']="0E11010204081F",['3']="1E01010601011E",['4']="02060A121F0202",['5']="1F10101E01011E",['6']="0E10101E11110E",['7']="1F010204080808",['8']="0E11110E11110E",['9']="0E11110F01010E",['-']="0000001F000000",['/']="01010204081010",['.']="00000000000C0C",[':']="000C0C000C0C00",['%']="191A0204080B13",['+']="0004041F040400",['>']="10080402040810",['<']="01020408040201"};
        public MfdCanvas(){texture=new Texture2D(W,H,TextureFormat.RGBA32,false){name="Live flight display",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};Clear();Flush();}
        public void Clear(){Array.Fill(pixels,black);Box(7,7,W-14,H-14,false);}
        void Dot(int x,int y,Color32 c){if(x>=0&&y>=0&&x<W&&y<H)pixels[(H-1-y)*W+x]=c;}
        public void Line(int x0,int y0,int x1,int y1,bool bright=false){int dx=Math.Abs(x1-x0),sx=x0<x1?1:-1,dy=-Math.Abs(y1-y0),sy=y0<y1?1:-1,err=dx+dy;for(int guard=0;guard<1800;guard++){Dot(x0,y0,bright?ink:dim);if(x0==x1&&y0==y1)break;int e=2*err;if(e>=dy){err+=dy;x0+=sx;}if(e<=dx){err+=dx;y0+=sy;}}}
        public void Box(int x,int y,int w,int h,bool fill=true){if(fill){for(int j=Math.Max(0,y);j<Math.Min(H,y+h);j++)for(int i=Math.Max(0,x);i<Math.Min(W,x+w);i++)Dot(i,j,ink);}else{Line(x,y,x+w,y);Line(x+w,y,x+w,y+h);Line(x+w,y+h,x,y+h);Line(x,y+h,x,y);}}
        public void Text(string text,int x,int y,int size=2){foreach(char raw in text.ToUpperInvariant()){if(Font.TryGetValue(raw,out var glyph))for(int row=0;row<7;row++){int bits=Convert.ToInt32(glyph.Substring(row*2,2),16);for(int col=0;col<5;col++)if((bits&(1<<(4-col)))!=0)Box(x+col*size,y+row*size,size,size);}x+=6*size;if(x>W-18)break;}}
        public void Circle(int x,int y,int radius){int px=x+radius,py=y;for(int k=1;k<=96;k++){float a=k*Mathf.PI/48;int nx=x+Mathf.RoundToInt(Mathf.Cos(a)*radius),ny=y+Mathf.RoundToInt(Mathf.Sin(a)*radius);Line(px,py,nx,ny);px=nx;py=ny;}}
        public void Bar(string title,float value,int row){int y=89+row*45;Text(title,28,y,3);Box(278,y-4,260,25,false);Box(281,y-1,Mathf.RoundToInt(Mathf.Clamp01(value)*253),18);Text((value*100).ToString("000"),554,y,3);}
        public void Flush(){texture.SetPixels32(pixels);texture.Apply(false);}
        public void Dispose(){if(texture)UnityEngine.Object.Destroy(texture);}
    }
    public sealed class MfdLifetime : MonoBehaviour
    {
        public MfdCanvas[] screens;public Material[] materials;
        void OnDestroy(){if(screens!=null)foreach(var screen in screens)screen?.Dispose();if(materials!=null)foreach(var m in materials)if(m)Destroy(m);}
    }
    public partial class FrontierGame
    {
        MfdCanvas[] mfd;readonly int[] mfdPage={0,0,0};int radarRange=2,commChannel,powerBus;float mfdNext;string cockpitHint="";
        static readonly int[] Ranges={250,500,1000,2500,5000};
        static readonly string[] PowerBuses={"engines","weapons","shields"};
        string MfdMenu(int screen)=>screen==1?"navigation":screen==2&&mfdPage[2]==2?"cargo":"systems";
        void OpenMfdMenu(int screen)
        {
            if(screen<0||screen>=mfdPage.Length)return;
            page=MfdMenu(screen);menu=true;inputNeutral=true;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        }
        string MfdControlHint(int action)
        {
            if(action>=40&&action<=43)return new[]{"NAV: scroll destination / click route","COMM: scroll channel / click traffic","SENSOR: scroll radar range / click radar","DRIVE: scroll speed limit / click flight page"}[action-40];
            if(action>=100&&action<=127)
            {
                int screen=(action-100)/10,key=(action-100)%10;
                if(key>=8)return "";
                string[] names=screen==0?new[]{"Previous flight page","Next flight page","Increase speed limit","Decrease speed limit",flightAssist?"Disable flight assist":"Enable flight assist",powered?"Turn main power off":"Turn main power on",lightsOn?"Turn cabin lights off":"Turn cabin lights on",flying?"Request landing":"Launch ship"}:
                    screen==1?new[]{"Previous navigation page","Next navigation page","Next destination","Previous destination","Jump to "+worlds[selectedWorld].name,flying?"Request landing":"Launch ship","Cycle radar range","Scan local contacts"}:
                    new[]{"Previous systems page","Next systems page","Increase "+PowerBuses[powerBus]+" power","Decrease "+PowerBuses[powerBus]+" power","Select next power bus",armed?"Safe weapons":"Arm weapons","Open cargo handling","Open vessel systems"};
                return names[key]+" / click";
            }
            return action>=0&&action<6?new[]{powered?"Main power OFF":"Main power ON",flightAssist?"Flight assist OFF":"Flight assist ON",gearDown?"Retract landing gear":"Extend landing gear",lightsOn?"Cabin lights OFF":"Cabin lights ON","Tactical boost",cruise?"Cruise OFF":"Cruise ON"}[action]+" / click":"Cockpit switch / click";
        }
        void MfdFooter(MfdCanvas canvas,int screen)
        {
            // The centre bezel's rotary housing overlaps its bottom edge in
            // the actual seated view. Keep its interaction hint above radar.
            if(screen!=1)canvas.Line(23,412,615,412);
            if(screen==1)canvas.Text("CLICK: NAVIGATION / SCROLL: PAGE "+(mfdPage[screen]+1)+"/3",25,63,2);
            else canvas.Text("CLICK "+MfdMenu(screen).ToUpperInvariant()+" / SCROLL "+(mfdPage[screen]+1)+"/3",25,422,3);
        }
        void SetupMfd()
        {
            foreach(var control in cabin.GetComponentsInChildren<CockpitControl>())control.InitializeMotion();
            mfdNext=0;mfd=new[]{new MfdCanvas(),new MfdCanvas(),new MfdCanvas()};var mats=new List<Material>();
            foreach(var control in cabin.GetComponentsInChildren<CockpitControl>())if(control.screen>=0&&control.screen<3){var renderer=control.GetComponent<MeshRenderer>();var material=new Material(Shader.Find("Universal Render Pipeline/Unlit")){name="Live MFD / "+control.screen};material.SetTexture("_BaseMap",mfd[control.screen].texture);material.SetColor("_BaseColor",Color.white);renderer.sharedMaterial=material;mats.Add(material);}
            var lifetime=cabin.gameObject.AddComponent<MfdLifetime>();lifetime.screens=mfd;lifetime.materials=mats.ToArray();
        }
        public void OperateMfd(int action,int direction=1)
        {
            if(action>=40&&action<=43){if(action==40){selectedWorld=(selectedWorld+worlds.Length+direction)%worlds.Length;mfdPage[1]=1;}if(action==41){commChannel=(commChannel+3+direction)%3;mfdPage[1]=2;}if(action==42){radarRange=Mathf.Clamp(radarRange+direction,0,Ranges.Length-1);mfdPage[1]=0;}if(action==43){throttle=Mathf.Clamp(throttle+direction*.05f,.05f,3);mfdPage[0]=0;}}
            else if(action>=100){int screen=(action-100)/10,key=(action-100)%10;if(screen>2)return;
                if(key>=8)return;
                if(key<2)mfdPage[screen]=(mfdPage[screen]+3+(key==0?-1:1))%3;
                else if(screen==0){if(key==2||key==3)throttle=Mathf.Clamp(throttle+(key==2?.05f:-.05f),.05f,3);if(key==4)ActivateCockpit(1);if(key==5)ActivateCockpit(0);if(key==6)ActivateCockpit(3);if(key==7)RequestLanding();}
                else if(screen==1){if(key==2||key==3){selectedWorld=(selectedWorld+worlds.Length+(key==2?1:-1))%worlds.Length;mfdPage[1]=1;}if(key==4)BeginJump(selectedWorld);if(key==5)RequestLanding();if(key==6){radarRange=(radarRange+1)%Ranges.Length;mfdPage[1]=0;}if(key==7)Signal("scan");}
                else{if(key==2||key==3){save.vessel.Allocate(PowerBuses[powerBus],key==2?1:-1);mfdPage[2]=1;}if(key==4){powerBus=(powerBus+1)%3;mfdPage[2]=1;}if(key==5)armed=!armed;if(key==6){mfdPage[2]=2;OpenMfdMenu(2);}if(key==7){mfdPage[2]=0;OpenMfdMenu(2);}}
            }else ActivateCockpit(action);
            foreach(var control in cabin.GetComponentsInChildren<CockpitControl>())if(control.action==action)control.Turn(direction);
            mfdNext=0;
        }
        void UpdateMfd()
        {
            if(mfd==null||Time.unscaledTime<mfdNext)return;mfdNext=Time.unscaledTime+.125f;
            foreach(var control in cabin.GetComponentsInChildren<CockpitControl>())if(control.action>=0&&control.action<40)control.SetState(control.action==0?powered:control.action==1?flightAssist:control.action==2?gearDown:control.action==3?lightsOn:control.action==5?cruise:flying);
            foreach(var light in cabin.GetComponentsInChildren<Light>())light.enabled=lightsOn;
            foreach(var c in mfd)c.Clear();
            var p=mfd[0];p.Text(new[]{"PROP / FLIGHT CONTROL","FUEL / CONSUMPTION","THERMAL / COMPONENTS"}[mfdPage[0]],25,27,3);p.Line(23,54,615,54);
            if(!powered){p.Text("MAIN BUS OFFLINE",90,200,3);p.Text("MASTER PWR TO START",80,250,3);}
            else if(mfdPage[0]==0){p.Bar("FUEL",save.fuel/100,0);p.Bar("THRUST",Mathf.Clamp01(speed/Spec.speed),1);p.Bar("POWER",save.vessel.Factor("engines",powered),2);p.Bar("SHIELD",shield/100,3);p.Bar("HEAT",heat/100,4);p.Text("SPD "+speed.ToString("000")+" M/S",25,337,4);p.Text("LIMIT "+(throttle*100).ToString("000")+"%",340,337,3);}
            else if(mfdPage[0]==1){p.Bar("FUEL",save.fuel/100,0);p.Text("JUMP RESERVE "+Mathf.FloorToInt(save.fuel/8),28,170,3);p.Text("LOADED MASS "+LoadedMass.ToString("0.0")+" T",28,232,3);p.Text("ENGINE ACCEL "+EngineAcceleration.ToString("0.0"),28,276,3);}
            else for(int i=0;i<save.vessel.components.Count;i++)p.Bar(save.vessel.components[i].id,save.vessel.components[i].temperature/100,i);
            p.Text((flightAssist?"IFCS":"DECOUPLED")+" / "+(gearDown?"GEAR DOWN":"GEAR UP"),25,387,3);MfdFooter(p,0);
            var n=mfd[1];n.Text(new[]{"NAV / SURFACE RADAR","ROUTE / SECTOR ATLAS","COMM / TRAFFIC FEED"}[mfdPage[1]],25,27);n.Line(23,54,615,54);
            if(mfdPage[1]==0){foreach(int radius in new[]{50,100,147})n.Circle(246,229,radius);n.Line(90,229,400,229);n.Line(246,83,246,385);n.Line(236,242,246,218,true);n.Line(246,218,256,242,true);n.Line(236,242,256,242,true);
                foreach(var place in world.places){Vector3 v=ship.InverseTransformDirection(place.position-ship.position);int x=246+Mathf.RoundToInt(v.x/Ranges[radarRange]*145),y=229-Mathf.RoundToInt(v.z/Ranges[radarRange]*145);if(Vector2.Distance(new Vector2(x,y),new Vector2(246,229))<145)n.Box(x-2,y-2,5,5);}
                foreach(var vessel in traffic.Values)if(vessel.gameObject.activeSelf){Vector3 v=ship.InverseTransformDirection(vessel.position-ship.position);int x=246+Mathf.RoundToInt(v.x/Ranges[radarRange]*145),y=229-Mathf.RoundToInt(v.z/Ranges[radarRange]*145);if(Vector2.Distance(new Vector2(x,y),new Vector2(246,229))<145){n.Line(x-3,y-3,x+3,y+3,true);n.Line(x-3,y+3,x+3,y-3,true);}}
                n.Text("ALT M",452,85);for(int i=0;i<9;i++)n.Line(452,130+i*25,470+(i%2)*8,130+i*25);n.Text(Mathf.Max(0,world.AltitudeAboveSurface(ship.position)-StandHeight).ToString("0000"),492,224,3);n.Text("RNG "+Ranges[radarRange],28,395);n.Text("HDG "+ship.eulerAngles.y.ToString("000"),355,395);}
            else if(mfdPage[1]==1){n.Text(worlds[selectedWorld].name,28,100,3);n.Text(worlds[selectedWorld].system+" SYSTEM",28,157);n.Text("JUMP COST 8 FUEL",28,218);n.Text("FUEL "+save.fuel.ToString("0"),28,259);n.Text(!flying?"LAUNCH FIRST":gearDown?"RETRACT GEAR":speed>70?"REDUCE SPEED":"ALIGNMENT AVAILABLE",28,320);n.Text("SELECT +/-   RIGHT 1 JUMP",28,395);}
            else{n.Text(new[]{"PORT OPERATIONS","FREIGHT DISPATCH","SECTOR PATROL"}[commChannel],28,84);int count=0;for(int i=save.society.events.Count-1;i>=0&&count<6;i--){string text=save.society.events[i];if(commChannel==1&&!text.Contains("food")&&!text.Contains("Freight"))continue;if(commChannel==2&&!text.Contains("patrol"))continue;count++;n.Text(text.Length>47?text.Substring(0,47):text,25,135+count*39,2);}n.Text("COMM DIAL SELECTS CHANNEL",28,395);}
            var s=mfd[2];s.Text(new[]{"SYS / VESSEL STATUS","POWER / DISTRIBUTION","HOLD / CARGO MANIFEST"}[mfdPage[2]],25,27,3);s.Line(23,54,615,54);
            if(mfdPage[2]==0){for(int i=0;i<save.vessel.components.Count;i++){var c=save.vessel.components[i];s.Text(c.id,28,91+i*43,3);s.Text(c.enabled?c.health.ToString("000")+"%":"OFFLINE",438,91+i*43,3);}s.Text("CABIN "+save.vessel.pressure.ToString("000")+"%",28,373,3);}
            else if(mfdPage[2]==1){string[] buses={"engines","weapons","shields"};for(int i=0;i<3;i++){s.Text((i==powerBus?"> ":"  ")+buses[i],28,107+i*75,3);s.Text(save.vessel.Allocation(buses[i]).ToString("00"),500,107+i*75,3);}s.Text("12 POINT BUS / +/- ADJUST",28,371,3);}
            else{s.Text("ORGANICS "+save.organics,28,103,3);s.Text("ORE      "+save.ore,28,167,3);s.Text("CRYSTAL  "+save.crystal,28,231,3);s.Text("CAPACITY "+Progression.Used(save)+" / "+Spec.capacity,28,313,3);s.Text(cargoDoor?"HATCH OPEN / LAUNCH LOCK":"HATCH SEALED",28,373,3);}
            MfdFooter(n,1);MfdFooter(s,2);foreach(var c in mfd)c.Flush();
        }
        void PointCockpit()
        {
            cockpitHint="";if(!cockpit||menu||Mouse.current==null)return;var ray=view.ScreenPointToRay(Mouse.current.position.ReadValue());
            if(!Physics.Raycast(ray,out var hit,8,1<<2)||!hit.collider.TryGetComponent<CockpitControl>(out var control))return;
            int a=control.action;cockpitHint=a>=0?MfdControlHint(a):control.screen>=0&&control.screen<3?"Click: "+MfdMenu(control.screen)+" / Scroll: display page":"Live multifunction display";
            float wheel=Mouse.current.scroll.ReadValue().y;if(a>=40&&a<=43&&Mathf.Abs(wheel)>.01f){OperateMfd(a,wheel>0?1:-1);return;}
            if(a<0&&control.screen>=0&&control.screen<3&&Mathf.Abs(wheel)>.01f){int screen=control.screen;mfdPage[screen]=(mfdPage[screen]+3+(wheel>0?1:-1))%3;mfdNext=0;return;}
            if(!Mouse.current.leftButton.wasPressedThisFrame)return;
            if(a>=40&&a<=43){if(a==40)mfdPage[1]=1;if(a==41)mfdPage[1]=2;if(a==42)mfdPage[1]=0;if(a==43)mfdPage[0]=(mfdPage[0]+1)%3;mfdNext=0;}
            else if(a>=0)OperateMfd(a);else if(control.screen>=0)OpenMfdMenu(control.screen);
        }
    }
}
