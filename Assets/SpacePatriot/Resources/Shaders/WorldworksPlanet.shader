Shader "SpacePatriot/WorldworksPlanet" {
 Properties { _GroundMap("Original geology atlas",2D)="white"{} _RockMap("Original cliff atlas",2D)="white"{} _NoiseVolume("Original world noise",3D)="white"{} _Living("Living world",Float)=0 _Seed("World seed",Float)=0 _PlanetCenter("Planet center",Vector)=(0,-18003,0,0) _PlanetRadius("Planet radius",Float)=18000 _WorldType("Source biome type",Float)=0 _Liquid("Source liquid type",Float)=0 _WorldAmplitude("Source relief amplitude",Float)=0.003 _Frequency("Source frequency",Float)=1 _TerrainBase("Source terrain base",Float)=0.44 _NoiseOffset("Source noise offset",Vector)=(0,0,0,0) _WorldTint("Source material tint",Vector)=(1,1,1,1) _SourceRight("Source frame right",Vector)=(1,0,0,0) _SourceUp("Source frame up",Vector)=(0,1,0,0) _SourceForward("Source frame forward",Vector)=(0,0,1,0) }
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" } Cull Off Pass {
 Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 float4 _WeatherworksSurface;
 TEXTURE2D(_GroundMap); SAMPLER(sampler_GroundMap);
 TEXTURE2D(_RockMap); SAMPLER(sampler_RockMap);
 TEXTURE3D(_NoiseVolume); SAMPLER(sampler_NoiseVolume);
 CBUFFER_START(UnityPerMaterial)
 float _Living,_Seed,_PlanetRadius,_WorldType,_Liquid,_WorldAmplitude,_Frequency,_TerrainBase;
 float4 _PlanetCenter;
 float4 _NoiseOffset,_WorldTint,_SourceRight,_SourceUp,_SourceForward;
 CBUFFER_END
 float hash3(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
 float noise3(float3 p){float3 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(lerp(hash3(i),hash3(i+float3(1,0,0)),f.x),lerp(hash3(i+float3(0,1,0)),hash3(i+float3(1,1,0)),f.x),f.y),lerp(lerp(hash3(i+float3(0,0,1)),hash3(i+float3(1,0,1)),f.x),lerp(hash3(i+float3(0,1,1)),hash3(i+1),f.x),f.y),f.z);}
 float fbm(float3 p){float n=0,a=.53;for(int k=0;k<5;k++){n+=noise3(p)*a;p=p*2.07+float3(5.2,1.7,9.3);a*=.48;}return n;}
 // The source engine interpolates its 64-cube table with smoothstep weights.
 // A trilinear sample at texel-center coordinates performs that same 8-corner
 // interpolation in one fetch instead of hundreds of point samples per pixel.
 float worldNoise(float3 p){float3 i=floor(p),f=frac(p);f=f*f*(3.-2.*f);return SAMPLE_TEXTURE3D_LOD(_NoiseVolume,sampler_NoiseVolume,(i+f+.5)/64.,0).r;}
 float sourceHeight(float3 n){float3 o=_NoiseOffset.xyz;float q=worldNoise(n*(12.*_Frequency)+o)*2.-1.;float v=.58*worldNoise(n*(3.5*_Frequency)+o)+.26*(1.-q*q)+.14*worldNoise(n*(42.*_Frequency)+o)+.02*worldNoise(n*(135.*_Frequency)+o);return _PlanetRadius*_WorldAmplitude*(v-_TerrainBase-.02);}
 float3 sourceMaterial(float3 n,float h,float lod){float3 o=_NoiseOffset.xyz;float large=worldNoise(n*9.+o),grain=lerp(large,worldNoise(n*800.+o),lod);float type=_WorldType;float3 albedo;
  if(type<.5){float strata=.5+.5*sin(h*.001*1900.+lerp(large,worldNoise(n*110.+o),lod)*3.);albedo=lerp(float3(.105,.115,.135),float3(.28,.245,.205),large);albedo*=.64+.24*grain+.12*strata;albedo=lerp(albedo,float3(.14,.13,.12),.12);}
  else if(type<1.5){float wet=_Liquid>.5&&h<=.02;if(wet)albedo=lerp(float3(.02,.21,.20),float3(.008,.036,.085),1.-smoothstep(-1.5,0.,h));else{float dry=smoothstep(.38,.66,worldNoise(n*14.+o+8.));albedo=lerp(float3(.07,.16,.067),float3(.28,.23,.12),dry);albedo=lerp(albedo,float3(.32,.31,.28),smoothstep(1.5,4.5,h));float snow=smoothstep(3.,6.,h);albedo=lerp(albedo,float3(.77,.84,.88),snow);albedo*=.78+grain*.34;}}
  else if(type<2.5){float strata=.5+.5*sin(h*.001*3500.+large*6.);albedo=lerp(float3(.24,.085,.038),float3(.57,.3,.12),strata*.6+large*.4);albedo*=.8+grain*.3;}
  else if(type<3.5){float3 drift=n+float3(_Time.y*.000004,0,0);float swirl=worldNoise(drift*13.+o)*2.+worldNoise(drift*31.+o)*.5;float bands=.5+.5*sin(n.y*82.+swirl*4.);float billow=worldNoise(drift*lerp(22.,400.,lod)+o+swirl)*.6+worldNoise(drift*lerp(48.,1400.,lod)+o)*.3+worldNoise(drift*lerp(96.,4200.,lod)+o)*.1;float3 tint=_WorldTint.rgb*1.35;albedo=lerp(tint*.38,tint*1.35+float3(.20,.15,.09),bands*.55+billow*.45)*(.65+billow*.75);albedo=lerp(albedo,float3(.74,.68,.57),smoothstep(.66,.89,billow)*.55);}
  else if(type<4.5){float crack=1.-smoothstep(.012,.085,abs(worldNoise(n*lerp(28.,170.,lod)+o)-.5));albedo=lerp(float3(.66,.77,.81),float3(.035,.20,.27),crack*.65)*(.8+.3*large);}
  else{albedo=lerp(float3(.038,.034,.034),float3(.16,.075,.047),grain);float lava=1.-smoothstep(.025,.09,abs(worldNoise(n*lerp(18.,110.,lod)+o)-.5));lava*=smoothstep(.25,.58,worldNoise(n*13.+o));albedo=lerp(albedo,float3(.29,.043,.008),lava*.7)+float3(2.5,.28,.014)*lava;}
  if(!(_Liquid>.5&&h<=.02))albedo*=_WorldTint.rgb*(.92+.08*(.5+.5*sin(h*.001*_PlanetRadius*440.+worldNoise(n*370.+o)*2.)));return albedo;}
 struct A {float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;float4 fields:COLOR;};
 struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;float2 uv:TEXCOORD2;float fog:TEXCOORD3;float4 fields:TEXCOORD4;};
 V vert(A i){V o;o.w=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(i.n);o.uv=i.uv;o.fields=i.fields;o.fog=0;return o;}
 half4 frag(V i):SV_Target {
  float3 n=normalize(i.n);float dist=distance(i.w,_WorldSpaceCameraPos);float local=i.uv.x;
  float3 detail=SAMPLE_TEXTURE2D(_GroundMap,sampler_GroundMap,i.w.xz*.045).rgb;
  float3 broad=SAMPLE_TEXTURE2D(_GroundMap,sampler_GroundMap,i.w.xz*.0027).rgb;
  float3 weights=pow(abs(n),4);weights/=max(.001,weights.x+weights.y+weights.z);
  float3 rock=SAMPLE_TEXTURE2D(_RockMap,sampler_RockMap,i.w.zy*.08).rgb*weights.x+SAMPLE_TEXTURE2D(_RockMap,sampler_RockMap,i.w.xz*.08).rgb*weights.y+SAMPLE_TEXTURE2D(_RockMap,sampler_RockMap,i.w.xy*.08).rgb*weights.z;
  float meadow=smoothstep(.6,.94,n.y)*_Living;
  float moisture=saturate(i.fields.r),temperature=saturate(i.fields.g),rockExposure=saturate(i.fields.b);
  float3 lush=lerp(float3(.055,.13,.036),float3(.18,.29,.072),saturate(detail.g*.8+moisture*.22));
  float3 dry=lerp(float3(.25,.145,.065),float3(.54,.36,.16),saturate(detail.r*.62+temperature*.38));
  float3 localGround=lerp(dry,lush,smoothstep(.24,.78,moisture));
  float exposedRock=smoothstep(.46,.83,rockExposure)*(1.-.32*moisture);
  localGround=lerp(localGround,lerp(rock,float3(.34,.32,.28),.35),exposedRock);
  float snow=smoothstep(.34,.12,temperature)*smoothstep(.28,.68,rockExposure);
  localGround=lerp(localGround,float3(.72,.79,.82),snow*.82);
  float3 land=lerp(lerp(broad,detail,.6),localGround,lerp(.83,1.,_Living));land=lerp(rock,land,smoothstep(.55,.85,n.y));
  float3 radial=normalize(i.w-_PlanetCenter.xyz),sourceN=normalize(_SourceRight.xyz*radial.x+_SourceUp.xyz*radial.y+_SourceForward.xyz*radial.z);
  float3 globe=sourceMaterial(sourceN,sourceHeight(sourceN),1-smoothstep(9000,18000,dist));
  if(_Living>.5){
   // Keep atmospheric clouds as a separate visual layer. Continents and
   // shorelines are vertex colors from PlanetEngineSurface, not a second,
   // unrelated shader noise field that disagrees with collision/terrain.
   float clouds=smoothstep(.66,.82,fbm(radial*18+float3(1,7,3)))*.24;
   globe=lerp(globe,float3(.76,.82,.84),clouds);
  }
  float3 base=lerp(globe,land,local);
  Light sun=GetMainLight(TransformWorldToShadowCoord(i.w));float lambert=saturate(dot(n,sun.direction));
  base*=1-_WeatherworksSurface.x*.29*local;
  base=lerp(base,float3(.76,.85,.88),_WeatherworksSurface.y*smoothstep(.38,.84,n.y)*local);
  float3 light=SampleSH(n)*.6+sun.color*lambert*sun.shadowAttenuation*.85;
  float3 c=base*light;float rim=pow(1-saturate(dot(n,normalize(_WorldSpaceCameraPos-i.w))),4);
  c+=float3(.055,.12,.19)*rim*lambert*smoothstep(900,3200,dist);
  // World-space Y is not altitude on this wrapped world: a ship can be
  // thousands of kilometres sideways from the planet while keeping y near 0.
  // Measure altitude radially or distant planets are incorrectly fogged solid.
  float cameraAltitude=length(_WorldSpaceCameraPos-_PlanetCenter.xyz)-_PlanetRadius;
  float atmosphere=1-smoothstep(450,1500,cameraAltitude);
  float fog=1-exp(-pow(dist*.00015*atmosphere,2));
  return half4(lerp(c,float3(.20,.28,.34),saturate(fog)),1);
 }
 ENDHLSL
 } }
}
