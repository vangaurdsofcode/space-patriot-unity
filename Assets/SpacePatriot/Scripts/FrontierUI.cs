using UnityEngine;

namespace SpacePatriot
{
    public partial class FrontierGame
    {
        readonly Color paper=new Color(.87f,.87f,.79f),muted=new Color(.48f,.55f,.56f),amber=new Color(.92f,.57f,.25f),aqua=new Color(.38f,.76f,.71f);
        readonly Color panel=new Color(.045f,.065f,.072f,.96f),line=new Color(.22f,.28f,.29f);
        GUIStyle type,button;
        Vector2 journalScroll;
        float valveA=.25f,valveB=.8f,valveC=.1f;
        bool restartConfirm;
        readonly System.Collections.Generic.List<string> menuControls=new System.Collections.Generic.List<string>();
        int menuFocus,menuActionFrame=-1,menuNavigationFrame=-1;
        string focusPage;
        void Styles()
        {
            if(type!=null)return;
            type=new GUIStyle(GUI.skin.label){font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),fontSize=18,wordWrap=true,richText=false,padding=new RectOffset(0,0,0,0)};
            type.normal.textColor=paper;
            button=new GUIStyle(type){alignment=TextAnchor.MiddleLeft,padding=new RectOffset(18,15,0,0)};
        }
        void Fill(float x,float y,float w,float h,Color c){var old=GUI.color;GUI.color=c;GUI.DrawTexture(new Rect(x,y,w,h),Texture2D.whiteTexture);GUI.color=old;}
        void Text(string t,float x,float y,float w,float h,int size=18,Color? color=null,bool bold=false,TextAnchor align=TextAnchor.UpperLeft)
        {type.fontSize=size;type.normal.textColor=color??paper;type.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;type.alignment=align;GUI.Label(new Rect(x,y,w,h),t,type);}
        void Rule(float x,float y,float w)=>Fill(x,y,w,1,line);
        bool Button(string label,float x,float y,float w=260,float h=46,bool primary=false,bool enabled=true)
        {
            string controlId=x+":"+y+":"+label;if(Event.current.type==EventType.Layout&&enabled)menuControls.Add(controlId);
            bool selected=UnityEngine.InputSystem.Gamepad.current!=null&&menuControls.IndexOf(controlId)==menuFocus;
            var rect=new Rect(x,y,w,h);bool hover=rect.Contains(Event.current.mousePosition)||selected;
            Fill(x,y,w,h,primary?amber:hover?new Color(.17f,.23f,.24f):new Color(.10f,.14f,.15f));
            if(!primary)Fill(x,y,3,h,hover?aqua:line);
            button.fontSize=17;button.fontStyle=primary?FontStyle.Bold:FontStyle.Normal;button.normal.textColor=!enabled?muted:primary?new Color(.08f,.10f,.10f):paper;
            bool old=GUI.enabled;GUI.enabled=enabled;GUI.SetNextControlName(controlId);bool click=GUI.Button(rect,label,button);GUI.enabled=old;
            if(selected&&enabled&&menuActionFrame!=Time.frameCount&&UnityEngine.InputSystem.Gamepad.current?.buttonSouth.wasPressedThisFrame==true){click=true;menuActionFrame=Time.frameCount;}
            if(click)Sound(buttonClip,.25f);return click;
        }
        void Tag(string title,string value,float x,float y,float width=190)
        {Text(title,x,y,width,22,12,muted,true);Text(value,x,y+23,width,40,23,paper,true);}
        void Meter(string label,float v,float max,float x,float y,float w,Color color)
        {Text(label,x,y,w,22,12,muted,true);Text(Mathf.CeilToInt(v).ToString(),x,y,w,22,14,paper,true,TextAnchor.UpperRight);Fill(x,y+27,w,3,line);Fill(x,y+27,w*Mathf.Clamp01(v/max),3,color);}
        void OnGUI()
        {
            if(save==null||view==null)return;Styles();float fit=Mathf.Min(Screen.width/1440f,Screen.height/900f);float ox=(Screen.width-1440*fit)/2,oy=(Screen.height-900*fit)/2;
            if(Event.current.type==EventType.Layout){var pad=UnityEngine.InputSystem.Gamepad.current;if(focusPage!=page){menuFocus=0;focusPage=page;}
                if(pad!=null&&menuNavigationFrame!=Time.frameCount&&menuControls.Count>0){if(pad.dpad.down.wasPressedThisFrame||pad.dpad.right.wasPressedThisFrame)menuFocus=(menuFocus+1)%menuControls.Count;if(pad.dpad.up.wasPressedThisFrame||pad.dpad.left.wasPressedThisFrame)menuFocus=(menuFocus+menuControls.Count-1)%menuControls.Count;if(pad.buttonEast.wasPressedThisFrame&&started)menu=false;menuNavigationFrame=Time.frameCount;}menuControls.Clear();}
            GUI.matrix=Matrix4x4.identity;Fill(0,0,ox,Screen.height,Color.black);Fill(Screen.width-ox,0,ox,Screen.height,Color.black);Fill(0,0,Screen.width,oy,Color.black);Fill(0,Screen.height-oy,Screen.width,oy,Color.black);
            GUI.matrix=Matrix4x4.TRS(new Vector3(ox,oy,0),Quaternion.identity,new Vector3(fit,fit,1));
            if(!started)TitleScreen();else
            {
                FlightHud();WeaponsHud();SocietyHud();
                var boss=WildlifeAgent.NearestBoss(WildlifeTarget,125);if(boss)BossHud(boss);
                if(!menu&&cockpitHint!="")Prompt(cockpitHint);
                if(menu)Computer();
                if(reportText!="")Report();
                if(dead)Recovery();
            }
            if(Time.unscaledTime<toastTime)
            {Fill(265,831,910,46,new Color(.035f,.05f,.055f,.97f));Fill(265,831,3,46,amber);Text(toast,284,845,870,30,16,paper);}
            if(travelFade>0)
            {Fill(0,0,1440,900,new Color(.015f,.025f,.03f,Mathf.Clamp01(travelFade)));Text("TRANSIT / "+worlds[selectedWorld].name.ToUpperInvariant(),0,440,1440,60,24,aqua,true,TextAnchor.MiddleCenter);}
        }
        void BossHud(WildlifeAgent actor)
        {
            Fill(424,38,592,58,new Color(.025f,.04f,.043f,.91f));Text(actor.species.name.ToUpperInvariant()+"  /  APEX CONTACT",440,44,560,20,13,amber,true,TextAnchor.MiddleCenter);
            Fill(465,72,510,8,new Color(.18f,.22f,.22f));Fill(465,72,510*actor.HealthFraction,8,actor.HealthFraction<.25f?new Color(.83f,.23f,.15f):amber);
        }
        void TitleScreen()
        {
            Fill(0,0,570,900,new Color(.025f,.044f,.052f,.91f));Fill(570,0,3,900,new Color(.71f,.43f,.18f,.7f));
            Text("FRONTIER SERVICE  /  07",58,52,460,25,13,amber,true);
            Text("SPACE\nPATRIOT",53,150,520,190,76,paper,true);
            Rule(58,366,410);Text("THE LONG DEBT",58,392,420,35,22,paper,true);
            Text("A working ship. A disputed frontier.\nThe people behind every manifest.",58,440,420,85,22,muted);
            if(Button(HasSave?"CONTINUE SHIFT   →":"ENTER THE FRONTIER   →",58,563,410,57,true))StartGame();
            if(Button("FLIGHT MANUAL",58,635,197,45))page=page=="help"?"overview":"help";
            if(Button("SETTINGS",271,635,197,45))page=page=="settings"?"overview":"settings";
            if(Application.platform!=RuntimePlatform.WebGLPlayer&&Button(Application.isEditor?"STOP PLAY MODE":"QUIT TO DESKTOP",58,700,410,45))QuitToDesktop();
            Text("WINDOWS DESKTOP  /  DEVELOPMENT BUILD",58,802,450,24,11,muted,true);
            Text("LOCAL SAVE  •  NO IN-GAME PURCHASES",58,829,450,24,11,muted);
            Fill(1001,715,384,108,new Color(.025f,.044f,.052f,.89f));Text(Spec.designation+" / "+Spec.role,1026,735,340,20,12,amber,true);Text(Spec.name,1026,765,340,42,32,paper,true);
            if(page=="help"||page=="settings")
            {Fill(621,90,735,575,panel);Text(page=="help"?"FLIGHT MANUAL":"SETTINGS",654,118,600,45,29,paper,true);if(page=="help")Help(654,189);else Settings(654,195);if(Button("CLOSE",1123,601,190,40))page="overview";}
        }
        void FlightHud()
        {
            if(cockpit&&!walking&&!aboard&&!menu&&!dead){Text("ESC  COMPUTER   |   F  LEAVE HELM   |   RIGHT MOUSE  STEER",31,867,780,20,11,muted,true);return;}
            Fill(0,0,1440,70,new Color(.025f,.045f,.05f,.88f));
            Text("SP / 07",31,23,140,25,17,amber,true);Text(CurrentWorld.name.ToUpperInvariant()+"  /  "+(walking?"ON FOOT":flying?"FLIGHT OPERATIONS":"DOCKED"),174,24,650,25,15,paper,true);
            Text(save.credits.ToString("N0")+" CR",1155,24,250,25,16,paper,true,TextAnchor.UpperRight);
            if(!menu&&reportText==""&&!dead)
            {
                Text("TAB  NAVIGATION     ESC  FLIGHT COMPUTER",31,861,510,23,12,muted,true);
                if(!walking)
                {
                    Fill(31,702,256,121,new Color(.025f,.045f,.05f,.80f));Tag("VELOCITY",speed.ToString("0")+" m/s",48,720,142);Tag("ALTITUDE",Mathf.Max(0,world.AltitudeAboveSurface(ship.position)-StandHeight).ToString("0")+" m",179,720,95);
                    Text("SPEED LIMIT  "+(throttle*100).ToString("0")+"%",48,784,220,24,13,muted,true);
                    Fill(1150,687,259,136,new Color(.025f,.045f,.05f,.80f));Meter("HULL",HullPercent,100,1170,704,218,amber);Meter("FUEL",save.fuel,100,1170,755,218,aqua);
                    Text("SHIELD  "+shield.ToString("0")+"    HEAT  "+heat.ToString("0"),1150,840,259,24,12,heat>80?amber:muted,true,TextAnchor.UpperRight);
                    if(flying)
                    {
                        Fill(700,450,12,1,paper);Fill(729,450,12,1,paper);Fill(720,429,1,12,paper);Fill(720,460,1,12,paper);
                        Marker("PORT 07",new Vector3(0,world.Deck+5,0),aqua);
                        Marker("TRAFFIC 04",world.station+Vector3.up*4,aqua);
                        Marker("REMOTE FREIGHT",world.outpost+Vector3.up*4,muted);
                        Marker("SURVEY FIELD",world.grove+Vector3.up*4,aqua);
                        foreach(var r in raiders)if(!r.dead&&Vector3.Distance(ship.position,r.body.position)<650)Marker("UNREGISTERED",r.body.position,amber);
                        if(docking)Text("APPROACH ASSIST",510,580,420,40,18,aqua,true,TextAnchor.MiddleCenter);
                        if(save.fuel<12)Text("LOW FUEL / RETURN TO PORT",470,640,500,35,19,amber,true,TextAnchor.MiddleCenter);
                    }
                    else Prompt("SPACE  LAUNCH     F  LEAVE SEAT     V  CAMERA");
                }
                else
                {
                    var near=NearInteract();
                    if(near!=null)Prompt("E  "+near.name.ToUpperInvariant());
                    else if(Vector3.Distance(walkPosition,CargoAccess)<14)Prompt("E  CARGO MANIFEST     F  BOARD");
                    else if(Vector3.Distance(walkPosition,ship.position)<Spec.width*.7f+8)Prompt("F  BOARD "+Spec.name);
                    Text("WASD  WALK    SHIFT  RUN    RIGHT MOUSE / ARROWS  LOOK",550,861,830,23,12,muted,true,TextAnchor.UpperRight);
                    Fill(718,448,4,4,paper);
                    foreach(var p in world.places)if(p.kind!="landing"&&Vector3.Distance(walkPosition,p.position)<65)Marker(p.name.ToUpperInvariant(),p.position+Vector3.up*2.8f,aqua);
                }
                var c=FindTracked();if(c!=null)
                {
                    var progress=Progression.Get(save,c.id);Text("ACTIVE CASE / "+c.title.ToUpperInvariant(),33,102,610,25,13,amber,true);
                    Text(progress.step<c.steps.Length?c.steps[progress.step].objective:"Evidence complete. Open Cases to choose an outcome.",33,133,465,90,19,paper);
                }
            }
        }
        CaseFile FindTracked()
        {foreach(var c in cases){var p=Progression.Get(save,c.id);if(c.id==targetedCase&&p.accepted&&p.choice<0)return c;}foreach(var c in cases){var p=Progression.Get(save,c.id);if(p.accepted&&p.choice<0)return c;}return null;}
        void Prompt(string text){Fill(441,739,558,55,new Color(.03f,.055f,.06f,.94f));Text(text,457,755,526,26,16,aqua,true,TextAnchor.MiddleCenter);}
        void Marker(string label,Vector3 position,Color color)
        {
            Vector3 s=view.WorldToViewportPoint(position);if(s.z<2||s.x<.07f||s.x>.93f||s.y<.18f||s.y>.84f)return;
            float x=s.x*1440,y=(1-s.y)*900;Fill(x-4,y-4,8,1,color);Fill(x-4,y-4,1,8,color);Fill(x+4,y-4,1,8,color);Fill(x-4,y+4,8,1,color);
            Text(label+"  "+Vector3.Distance(view.transform.position,position).ToString("0")+" m",x-125,y+14,250,22,11,color,true,TextAnchor.UpperCenter);
        }
        void Computer()
        {
            Fill(0,70,1440,830,new Color(.01f,.025f,.03f,.67f));Fill(88,112,1264,682,panel);
            Text("FLIGHT\nCOMPUTER",117,144,227,100,30,paper,true);Text(Spec.designation+" / "+Spec.name,117,244,231,30,12,amber,true);Rule(117,287,200);
            string[] keys={"overview","navigation","cases","trade","cargo","contracts","hangar","systems","journal","help","controls","settings"};string[] names={"Operations","Navigation","The Long Debt","Freight exchange","Cargo handling","Freight contracts","Fleet & service","Vessel systems","Journal","Flight manual","Controls","Settings"};
            for(int i=0;i<keys.Length;i++)if(Button(names[i],109,291+i*34,233,30,page==keys[i]))page=keys[i];
            if(Button("RETURN TO SHIFT  →",109,719,233,43))menu=false;
            if(Application.platform!=RuntimePlatform.WebGLPlayer&&Button(Application.isEditor?"SAVE & STOP":"SAVE & QUIT",109,767,233,25))QuitToDesktop();
            Fill(367,141,1,619,line);Text(PageTitle(),399,143,864,45,30,paper,true);Rule(399,203,918);
            switch(page)
            {
                case "community":SocietyJobsPanel();break;case "settlements":SettlementsPanel();break;case "overview":Overview();break;case "inventory":InventoryPanel();break;case "population":PopulationPanel();break;case "navigation":Navigation();break;case "cases":Cases();break;case "trade":Trade();break;
                case "contracts":ContractsPanel();break;case "cargo":CargoPanel();break;case "systems":SystemsPanel();break;case "controls":ControlsPanel();break;case "hangar":Hangar();break;case "journal":Journal();break;case "help":Help(400,229);break;case "settings":Settings(400,236);break;case "reactor":Reactor();break;
            }
        }
        string PageTitle()=>page switch{"contracts"=>"FREIGHT CONTRACTS","cargo"=>"CARGO HANDLING","systems"=>"VESSEL SYSTEMS","controls"=>"CONTROLS & INPUT","navigation"=>"PLOT A COURSE","cases"=>"THE LONG DEBT","trade"=>"FREIGHT EXCHANGE","hangar"=>"SHIPYARD & SERVICE","journal"=>"FLIGHT JOURNAL","help"=>"FLIGHT MANUAL","settings"=>"SETTINGS","reactor"=>"CITY POWER BUS",_=>"SHIFT OPERATIONS"};
        void Overview()
        {
            Tag("LOCAL SECTOR",CurrentWorld.name,400,233,320);Tag("VESSEL",Spec.name,778,233,235);Tag("ACCOUNT",save.credits.ToString("N0")+" CR",1080,233,236);
            Rule(400,320,918);Text("YOUR NEXT MOVE",400,352,810,28,14,amber,true);var c=FindTracked();
            if(c==null)
            {Text("Start with the water ledger.",400,398,865,44,29,paper,true);Text("Ivo Rook has a freight problem on Earth. Read his case file, then use the operations terminal behind the landing pad. Four units of organics are already in your hold.",400,457,795,116,21,muted);}
            else
            {var p=Progression.Get(save,c.id);Text(c.title,400,398,865,44,29,paper,true);Text(p.step<c.steps.Length?c.steps[p.step].objective:"All evidence is collected. Choose what happens next in Cases.",400,457,795,100,22,muted);}
            if(Button("OPEN CASE FILES",400,622,276,49,true))page="cases";
            if(Button("PLAN ROUTE",695,622,276,49))page="navigation";
            if(Button("SECTOR ACTIVITY",990,622,326,49))page="population";
            if(Button("FIELD INVENTORY",400,678,276,32))page="inventory";
            if(Button("COMMUNITY JOBS",695,678,276,32))page="community";
            if(Button("CITIES & OUTPOSTS",990,678,326,32))page="settlements";
            Text("Progress saves locally after trading, landing, transit, and campaign decisions.",400,715,874,42,15,muted);
        }
        void Navigation()
        {
            for(int i=0;i<worlds.Length;i++)
            {int col=i/10,row=i%10;bool same=i==save.world;if(Button((same?"● ":"")+worlds[i].name,400+col*223,231+row*48,208,41,i==selectedWorld))selectedWorld=i;}
            var w=worlds[selectedWorld];Fill(870,230,447,487,new Color(.07f,.10f,.11f));
            Text(w.system.ToUpperInvariant()+" SYSTEM",898,255,396,22,12,amber,true);Text(w.name,898,296,383,60,34,paper,true);
            Text(w.biome=="gas"?"SUSPENDED INDUSTRIAL HABITAT":w.biome.ToUpperInvariant()+" / FRONTIER PORT",898,366,389,42,14,aqua,true);
            Text("Port 07: operations, freight and power.\nTraffic 04: records and docking.\nRemote pad: deliveries.\nSurvey site: field samples.",898,422,382,150,19,muted);
            Tag("JUMP COST","8 fuel",898,580,160);Tag("RESERVE",save.fuel.ToString("0")+" / 100",1100,580,180);
            if(Button(selectedWorld==save.world?"CURRENT SECTOR":"ALIGN & JUMP  →",898,658,390,44,true,selectedWorld!=save.world&&!jumping))BeginJump(selectedWorld);
            Text("Launch, climb above 65 m, and reduce speed below 70 m/s to jump.",400,734,908,37,15,muted);
        }
        void Cases()
        {
            for(int i=0;i<cases.Length;i++)
            {var p=Progression.Get(save,cases[i].id);bool unlocked=Progression.Unlocked(save,cases[i]);string marker=p.choice>=0?"✓":!unlocked?"–":p.accepted?"●":"○";if(Button(marker+"  "+cases[i].title,400,229+i*54,290,46,selectedCase==i))selectedCase=i;}
            var c=cases[selectedCase];var progress=Progression.Get(save,c.id);bool available=Progression.Unlocked(save,c);
            Text(c.speaker.ToUpperInvariant(),719,235,590,34,13,amber,true);Text(c.title,719,277,590,75,29,paper,true);
            if(!available)
            {Text("This case follows earlier investigations.",719,379,580,45,22,paper);Text("Complete: "+string.Join(", ",c.requires),719,443,570,85,18,muted);return;}
            if(progress.choice>=0)
            {Text(c.choices[progress.choice].ending,719,379,576,250,20,muted);Text("CASE CLOSED / DECISION SAVED",719,692,560,30,14,aqua,true);return;}
            if(!progress.accepted)
            {Text(c.brief,719,368,580,241,20,muted);if(Button("TAKE THE CASE",719,680,580,47,true)){progress.accepted=true;targetedCase=c.id;Save();Toast("Case accepted. "+c.steps[0].objective);}return;}
            if(progress.step<c.steps.Length)
            {
                var step=c.steps[progress.step];Text("EVIDENCE  "+(progress.step+1)+" / "+c.steps.Length,719,365,580,30,13,aqua,true);Text(step.objective,719,414,580,100,26,paper,true);
                Text("Complete this action in the world. Read terminals on foot; carry cargo to the freight desk; land at the traffic station for its records.",719,537,570,96,18,muted);
                if(Button("TRACK THIS OBJECTIVE",719,680,580,47,true)){targetedCase=c.id;Toast("Tracking: "+step.objective);menu=false;}
            }
            else
            {
                Text("The evidence is complete. Your decision changes faction standing, payment, and local market prices.",719,375,579,102,20,muted);
                for(int i=0;i<c.choices.Length;i++)if(Button(c.choices[i].label,719,520+i*95,580,76,i==0))
                {if(Progression.Resolve(save,c,i)){reportTitle=c.title;reportText=c.choices[i].ending;Save();Toast("Decision recorded. +"+c.choices[i].credits+" cr.");}}
            }
        }
        void Trade()
        {
            Text("Loaded hold: "+Progression.Used(save)+" / "+Spec.capacity+" units",400,236,800,37,23,paper,true);
            Text(AtPort?"Purchases arrive on the dock. Load them through Cargo handling. Unload goods before selling.":"Exchange access is available after landing.",400,289,902,61,17,muted);
            string[] goods={"organics","ore","crystal"};string[] descriptions={"Filter cultures, food and medical feedstock.","Construction stock and replacement parts.","Power cells and precision lattice material."};
            for(int i=0;i<3;i++)
            {
                float y=377+i*107;int price=Progression.Price(save,CurrentWorld,goods[i]);int units=Progression.Cargo(save,goods[i]);
                Rule(400,y-17,918);Text(goods[i].ToUpperInvariant(),400,y,333,26,19,paper,true);Text(descriptions[i],400,y+33,400,52,16,muted);
                Text(units+" HOLD\n"+CargoHandling.Staged(save,save.world,goods[i])+" DOCK",813,y+6,136,58,15,aqua,true);
                if(Button("BUY / "+price,956,y,170,47,false,AtPort&&save.credits>=price&&save.economy.markets.Find(m=>m.id==CurrentWorld.id).Stock(goods[i])>=1))
                {save.economy.markets.Find(m=>m.id==CurrentWorld.id).Add(goods[i],-1);save.credits-=price;CargoHandling.Stage(save,save.world,goods[i],1);RebuildCargo();Save();}
                int sell=FrontierEconomy.Price(save,CurrentWorld,goods[i],false);if(Button("SELL / "+sell,1138,y,170,47,false,AtPort&&CargoHandling.Staged(save,save.world,goods[i])-Mathf.Max(0,FrontierEconomy.Reserved(save,goods[i])-Progression.Cargo(save,goods[i]))>0))
                {save.economy.markets.Find(m=>m.id==CurrentWorld.id).Add(goods[i],1);save.credits+=sell;CargoHandling.Stage(save,save.world,goods[i],-1);RebuildCargo();Save();}
            }
            Text("Cargo must be physically aboard to travel or fulfil a delivery. Open Cargo handling to load and unload.",400,713,905,58,16,amber);
        }
        void Hangar()
        {
            for(int i=0;i<10;i++){int col=i%6,row=i/6;if(Button(ShipSpec.Fleet[i*10].name.Split(' ')[0],400+col*153,228+row*39,145,33,selectedShip<100&&selectedShip/10==i))selectedShip=i*10+(selectedShip<100?selectedShip%10:0);}
            if(ShipSpec.Fleet.Length>100&&Button("KESTREL",400+4*153,267,145,33,selectedShip==100))selectedShip=100;
            if(selectedShip<100&&Button("← FINISH",400,317,195,35))selectedShip=selectedShip/10*10+(selectedShip%10+9)%10;
            Text(ShipSpec.Fleet[selectedShip].name,611,319,470,36,21,paper,true);
            if(selectedShip<100&&Button("FINISH →",1113,317,195,35))selectedShip=selectedShip/10*10+(selectedShip%10+1)%10;
            var spec=ShipSpec.Fleet[selectedShip];Text(spec.designation+" / "+spec.role,400,371,915,27,13,amber,true);Text(spec.description,400,411,890,77,19,muted);
            Tag("SPEED",spec.speed.ToString("0")+" m/s",400,503,220);Tag("HOLD",spec.capacity+" units",634,503,210);Tag("LENGTH",spec.length.ToString("0.0")+" m",868,503,220);Tag("PRICE",spec.price+" CR",1102,503,205);
            var authored=AuthoredShipVisuals.Find(spec);
            bool visualReady=spec.family>=0||(authored!=null&&Resources.Load<GameObject>(authored.resource)!=null);
            bool owned=save.ownedShips.Contains(selectedShip);bool eligible=visualReady&&AtPort&&Progression.Used(save)<=spec.capacity&&cargoTransfer==null;
            if(Button(save.ship==selectedShip?"CURRENT VESSEL":owned?"ASSIGN VESSEL":"PURCHASE & ASSIGN",400,600,430,49,true,eligible&&save.ship!=selectedShip&&(owned||save.credits>=spec.price)))
            {
                if(!owned){save.credits-=spec.price;save.ownedShips.Add(selectedShip);}
                save.ship=selectedShip;save.hull=spec.health;activeDeck=null;RespawnShip();walking=true;SetSurfaceWalker(ship.position+new Vector3(-Spec.width*.65f,0,-3),ship.forward);Save();Toast("Fleet assignment complete. "+spec.name+" is ready at the port.");
            }
            int service=Mathf.CeilToInt((Spec.health-save.hull)*1.4f+(100-save.fuel)*2);
            if(Button("REPAIR + REFUEL / "+service+" CR",851,600,456,49,false,AtPort&&save.credits>=service))
            {save.credits-=service;save.hull=Spec.health;save.fuel=100;shield=100;systemsHull=save.hull;Save();Toast("Hull repaired and fuel replenished. Component maintenance is available under Vessel systems.");}
            int rearm=0;for(int i=0;i<WeaponSpec.All.Length;i++){var w=WeaponSpec.All[i];if(i!=1)rearm+=Mathf.Max(0,w.mag+w.reserve-save.ammo[i].mag-save.ammo[i].reserve)*(i==2?12:1);}
            if(Button("REARM / "+rearm+" CR",851,666,456,42,false,AtPort&&rearm>0&&save.credits>=rearm)){save.credits-=rearm;save.ammo=System.Array.ConvertAll(WeaponSpec.All,w=>new WeaponAmmo(w.mag,w.reserve));reloadRemaining=0;reloadWeapon=-1;Save();Toast("Magazines and reserve ammunition replenished.");}
            Text((spec.interiorFamily==9?"4":spec.interiorFamily==7?"3":spec.interiorFamily==2||spec.interiorFamily==5||spec.interiorFamily==6?"2":"1")+" walkable decks · F leave helm · E stations / bulkheads. Service lift: E down, Shift+E up.",400,654,425,50,15,muted);
            if(AtPort&&save.fuel<12&&Button("PORT EMERGENCY FUEL / 12 UNITS",400,706,460,42))
            {save.fuel=12;Save();Toast("Safety reserve issued. No charge.");}
        }
        void Journal()
        {
            Text("Union "+save.union+"     Helix "+save.helix+"     Independent "+save.redwake+"     Hostiles disabled "+save.kills,400,234,910,31,17,aqua,true);
            journalScroll=GUI.BeginScrollView(new Rect(400,294,920,455),journalScroll,new Rect(0,0,889,Mathf.Max(445,save.journal.Count*145)));
            if(save.journal.Count==0)Text("The journal records evidence and the consequences of your decisions.",0,20,815,90,23,muted);
            for(int i=0;i<save.journal.Count;i++){int index=save.journal.Count-1-i;Text((index+1).ToString("00"),0,i*145+4,45,25,13,amber,true);Text(save.journal[index],54,i*145,818,123,18,paper);Rule(54,i*145+134,818);}
            GUI.EndScrollView();
        }
        void Help(float x,float y)
        {
            string[] left={"ON FOOT","TRANSLATION","ROTATION","FLIGHT","LAND / CARGO","COCKPIT"};
            string[] right={"WASD move · Shift run · F board/leave seat · E interact · Right mouse / arrows look",
                "W/S forward/reverse · A/D strafe · Space/Ctrl up/down · Mouse wheel speed limit · Shift boost · X brake",
                "Mouse while holding right button, or arrow keys: pitch/yaw · Q/E or [/] roll · U level to horizon",
                "Space or L launch · T flight assist · G gear · P power · Cockpit switch for cruise · H jump · Double-tap W tactical thrust",
                "X brake · G extend gear · L lands on a pad or any solid ground below 650 m and 45 m/s. Buy at exchange; load at cargo ramp.",
                "V cockpit/chase · Z/I instruments · Click MFD for its menu; scroll MFD pages or knobs. Hover switches for actions. Y arm · 1/2/3 select · R reload · C target · Tab navigation · Esc menu"};
            for(int i=0;i<left.Length;i++){Text(left[i],x,y+i*71,110,30,12,amber,true);Text(right[i],x+118,y+i*71,started?783:522,65,16,paper);}
        }
        void Settings(float x,float y)
        {
            Text("AUDIO LEVEL",x,y,540,30,13,amber,true);float volume=PlayerPrefs.GetFloat("sp.volume",.55f);float newVolume=GUI.HorizontalSlider(new Rect(x,y+46,500,20),volume,0,1);if(newVolume!=volume)PlayerPrefs.SetFloat("sp.volume",newVolume);
            Text("GRAPHICS",x,y+80,590,25,13,amber,true);
            if(Button("STANDARD",x,y+113,237,36))SetQuality(false);if(Button("HIGH",x+258,y+113,237,36))SetQuality(true);
            bool mouseInvert=PlayerPrefs.GetInt("sp.invert",0)==1,padInvert=PlayerPrefs.GetInt("sp.padInvert",0)==1;
            if(Button("MOUSE Y: "+(mouseInvert?"INVERTED":"NORMAL"),x,y+180,237,38)){PlayerPrefs.SetInt("sp.invert",mouseInvert?0:1);PlayerPrefs.Save();}
            if(Button("STICK Y: "+(padInvert?"INVERTED":"NORMAL"),x+258,y+180,260,38)){PlayerPrefs.SetInt("sp.padInvert",padInvert?0:1);PlayerPrefs.Save();}
            float sensitivity=PlayerPrefs.GetFloat("sp.sensitivity",1);Text("MOUSE SENSITIVITY  "+sensitivity.ToString("0.0"),x,y+238,510,24,13,amber,true);
            float changed=GUI.HorizontalSlider(new Rect(x,y+272,500,20),sensitivity,.25f,2);if(changed!=sensitivity)PlayerPrefs.SetFloat("sp.sensitivity",changed);
            Text("Normal: move mouse/stick up to look up. Hold right mouse to steer. Arrow keys also steer.",x,y+305,started?870:610,38,15,muted);
            if(Application.platform!=RuntimePlatform.WebGLPlayer&&Button("F11 / "+(Screen.fullScreen?"WINDOWED":"FULLSCREEN"),x+(started?370:0),y+345,started?300:350,46,false,!Application.isEditor))ToggleDesktopFullscreen();
            if(started&&Button(restartConfirm?"CONFIRM NEW SHIFT":"START A NEW SHIFT",x,y+345,350,46,false))
            {
                if(!restartConfirm){restartConfirm=true;Toast("Press Confirm New Shift to replace this profile's saved progress.");}
                else{CampaignStorage.Reset();UnityEngine.SceneManagement.SceneManager.LoadScene(0);}
            }
        }
        void SetQuality(bool high)
        {
            QualitySettings.shadowDistance=high?220:100;QualitySettings.shadows=high?ShadowQuality.All:ShadowQuality.HardOnly;
            var pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if(pipeline!=null)pipeline.renderScale=high?1:.8f;PlayerPrefs.SetInt("sp.high",high?1:0);Toast(high?"High graphics selected.":"Standard graphics selected.");
        }
        void Reactor()
        {
            Text("MAINTENANCE / MANUAL TRANSFER",400,239,900,28,13,amber,true);Text("Balance the three feeds",400,286,900,50,28,paper,true);
            Text("Match each supply to its marked operating point, then reconnect the district bus.",400,350,887,65,20,muted);
            string[] labels={"COOLANT  /  TARGET 45%","PRIMARY  /  TARGET 62%","RESERVE  /  TARGET 38%"};float[] values={valveA,valveB,valveC};
            for(int i=0;i<3;i++){Text(labels[i],400,445+i*65,386,28,14,paper,true);values[i]=GUI.HorizontalSlider(new Rect(815,451+i*65,420,20),values[i],0,1);Text((values[i]*100).ToString("0")+"%",1242,445+i*65,80,30,16,aqua);}
            valveA=values[0];valveB=values[1];valveC=values[2];bool ready=Mathf.Abs(valveA-.45f)<.07f&&Mathf.Abs(valveB-.62f)<.07f&&Mathf.Abs(valveC-.38f)<.07f;
            if(Button(ready?"RECONNECT DISTRICT BUS":"FEEDS OUTSIDE TOLERANCE",400,677,904,50,ready,ready))
            {save.inventory.cityPower=1;CompleteDistrictRepair();Signal("power");menu=false;Toast("District bus online. Stable power restored.");}
        }
        void Report()
        {
            Fill(0,70,1440,830,new Color(.015f,.029f,.032f,.85f));Fill(275,214,891,458,panel);Fill(275,214,4,458,amber);
            Text("FIELD REPORT / EVIDENCE RECORDED",316,252,800,26,13,amber,true);Text(reportTitle,316,297,800,73,31,paper,true);Text(reportText,316,389,796,172,22,paper);
            if(Button("RETURN TO SHIFT",316,599,795,47,true)){reportText="";reportTitle="";menu=false;}
        }
        void Recovery()
        {
            Fill(0,70,1440,830,new Color(.02f,.025f,.026f,.89f));Text("VESSEL DISABLED",345,259,750,60,40,amber,true);Text("Port recovery can retrieve you and your cargo. The service costs up to 180 credits. Your case progress is preserved.",345,360,715,111,24,paper);
            if(Button("REQUEST PORT RECOVERY",345,546,748,59,true))Rescue();
        }
    }
}
