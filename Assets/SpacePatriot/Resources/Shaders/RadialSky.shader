Shader "SpacePatriot/RadialSky"
{
    Properties
    {
        _AirTint("Atmosphere tint", Color) = (0.22,0.49,0.76,1)
        _StarTint("Star tint", Color) = (1,1,1,1)
        _PlanetUp("Planetary up", Vector) = (0,1,0,0)
        _SunDirection("Sun direction", Vector) = (0,1,0,0)
        _PlanetRadius("Planet radius", Float) = 18000
        _AltitudeMeters("Observer altitude", Float) = 0
        _AtmosphereHeight("Atmosphere height", Float) = 650
        _AirStrength("Atmosphere strength", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 directionWS : TEXCOORD0; };
            half4 _AirTint;
            half4 _StarTint;
            float4 _PlanetUp;
            float4 _SunDirection;
            float _PlanetRadius;
            float _AltitudeMeters;
            float _AtmosphereHeight;
            float _AirStrength;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.directionWS=TransformObjectToWorldDir(input.positionOS.xyz);
                return output;
            }

            float Hash21(float2 p)
            {
                float3 p3=frac(float3(p.x,p.y,p.x)*.1031);
                p3+=dot(p3,p3.yzx+33.33);
                return frac((p3.x+p3.y)*p3.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 ray=normalize(input.directionWS);
                float3 up=normalize(_PlanetUp.xyz);
                float3 sun=normalize(_SunDirection.xyz);
                float elevation=dot(ray,up);
                float sunElevation=dot(up,sun);
                float daylight=.025+.975*smoothstep(-.22,.24,sunElevation);
                float altitude=max(0,_AltitudeMeters);
                float height=max(1,_AtmosphereHeight);
                float fading=1-smoothstep(height*.7,height*1.8,altitude);
                float air=saturate(_AirStrength)*exp(-altitude/(height*.48))*fading;
                float radius=max(1,_PlanetRadius);
                float horizon=-sqrt(saturate(1-pow(radius/(radius+altitude),2)));
                float relative=elevation-horizon;
                float zenith=smoothstep(-.055,.53,relative);
                float horizonGlow=exp(-abs(relative)*8.5);
                float forwardGlow=pow(saturate(dot(ray,sun)),12);
                float3 tint=_AirTint.rgb*_StarTint.rgb;
                float3 atmosphere=lerp(tint*.72,tint*.28,zenith);
                atmosphere+=tint*(.18*horizonGlow+.16*forwardGlow*horizonGlow);
                atmosphere*=daylight;
                // The source sky stays in space; atmosphere merely scatters over it.
                float3 color=float3(.0007,.0014,.0035);
                float2 starUV=float2(atan2(ray.z,ray.x)/6.2831853+.5,asin(clamp(ray.y,-1,1))/3.14159265+.5);
                float2 cell=starUV*float2(650,325);
                float2 id=floor(cell),sub=frac(cell);
                float2 center=float2(Hash21(id+5.7),Hash21(id+19.2))*.7+.15;
                float star=(1-smoothstep(.026,.026+max(fwidth(sub.x),fwidth(sub.y))*.45,length(sub-center)))*step(.9975,Hash21(id));
                color+=star*lerp(float3(.65,.79,1),float3(1,.8,.55),Hash21(id+45))*max(.05,1-air);
                color=lerp(color,atmosphere,air);
                float solar=saturate(dot(ray,sun));
                color+=_StarTint.rgb*(float3(1,.71,.28)*pow(solar,1600)*1.6+float3(.2,.065,.018)*pow(solar,110))*(.65+.35*(1-air));
                return half4(color,1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
