Shader "SpacePatriot/Grassworks" {
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Cull Off
 Pass { Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_fog
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 struct A {float3 p:POSITION;float2 uv:TEXCOORD0;float2 extra:TEXCOORD1;float4 color:COLOR;};
 struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;float2 uv:TEXCOORD1;float seed:TEXCOORD2;float3 color:COLOR;float fog:TEXCOORD3;float daylight:TEXCOORD4;};
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
 V vert(A i){V o;float3 p=i.p;float t=_Time.y,random=i.extra.x;
 float large=sin(p.x*.11+p.z*.075+t),crossWind=sin(p.x*-.055+p.z*.16+t*1.37);
 float gust=noise(p.xz*.2+t*float2(.15,.09)),micro=sin(p.x*.75+p.z*.63+t*3.2+random*8);
 float field=large*.48+crossWind*.22+(gust-.5)*1.2+micro*.04,bend=pow(i.uv.y,1.7);
 p.x+=.6*field*bend*.58;p.z+=.6*field*bend*.22+bend*bend*i.extra.y*.075;
 p.x+=sin(t*5+random*17+p.x)*.5*.025*bend;
 o.w=TransformObjectToWorld(p);o.p=TransformWorldToHClip(o.w);o.uv=i.uv;o.seed=random;o.color=i.color.rgb;o.fog=ComputeFogFactor(o.p.z);
 float3 radialUp=normalize(mul((float3x3)GetObjectToWorldMatrix(),float3(0,1,0)));
 float sun=saturate((dot(radialUp,GetMainLight().direction)+.06)/.22);
 o.daylight=.055+.945*sun*sun*(3-2*sun);return o;}
 half4 frag(V i):SV_Target {clip(1-smoothstep(78,105,distance(i.w,_WorldSpaceCameraPos))-i.seed*.8);
 float3 pigment=max(i.color,0);
 float3 c=lerp(pigment*.27,pigment,smoothstep(0,.45,i.uv.y));
 c=lerp(c,min(pigment*1.7,float3(.8,.8,.8)),pow(i.uv.y,2.4)*.65);
 c*=1.+(i.seed-.5)*.18;
 // grasspack3js is an unlit vegetation material with its own per-blade
 // directional response. Multiplying by URP sun a second time overexposes
 // the tips and turns the canopy yellow in the native Unity port.
 c*=.58+.42*abs(sin(i.seed*18+i.w.x*.08));c+=pigment*.15*i.uv.y*i.uv.y;
 // Match the source's local daylight, rather than glowing on the night side.
 return half4(MixFog(c*i.daylight,i.fog),1);}
 ENDHLSL
 } }
}
