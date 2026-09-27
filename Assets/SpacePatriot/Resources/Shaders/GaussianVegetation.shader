Shader "SpacePatriot/GaussianVegetation"
{
    Properties { _FogColor("Fog Color", Color)=(.2,.28,.34,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+8" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceTint)
            UNITY_INSTANCING_BUFFER_END(Props)

            CBUFFER_START(UnityPerMaterial)
                float4 _Range;
                float4 _FogColor;
                float _Fade, _Wind, _FogDensity;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 gaussian : TEXCOORD0;
                float4 tint : TEXCOORD1;
                float distanceToEye : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                float3 center = TransformObjectToWorld(float3(0,0,0));
                // Instance scale stores the ellipsoid's three standard deviations.
                // Direction helpers normalize by default, which would turn every
                // meadow/canopy splat into the same unit-sized shape.
                float3 axisXWS = mul((float3x3)GetObjectToWorldMatrix(), float3(1,0,0));
                float3 axisYWS = mul((float3x3)GetObjectToWorldMatrix(), float3(0,1,0));
                float3 axisZWS = mul((float3x3)GetObjectToWorldMatrix(), float3(0,0,1));
                // Wind bends in the instance's surface tangent, including at
                // the equator and southern hemisphere, rather than world X.
                center += normalize(axisXWS) * sin(_Time.y*.65 + center.x*.09 + center.z*.05) * _Wind * length(axisYWS) * .15;
                // Match gaussian-splats.js: covariance and center must both be
                // in camera space before applying the perspective Jacobian.
                float3 axisX = mul((float3x3)UNITY_MATRIX_V, axisXWS);
                float3 axisY = mul((float3x3)UNITY_MATRIX_V, axisYWS);
                float3 axisZ = mul((float3x3)UNITY_MATRIX_V, axisZWS);
                float3 viewCenter = TransformWorldToView(center);
                float z = max(.05, -viewCenter.z);
                float distanceToEye = length(viewCenter);
                float depthSigma = length(float3(axisX.z,axisY.z,axisZ.z));
                float nearFade = _Range.y > _Range.x ? smoothstep(_Range.x,_Range.y,min(distanceToEye,z)) : 1;
                float farFade = 1-smoothstep(_Range.z,_Range.w,distanceToEye);
                output.tint = UNITY_ACCESS_INSTANCED_PROP(Props,_InstanceTint);
                float sun=saturate((dot(normalize(axisYWS),GetMainLight().direction)+.06)/.22);
                output.tint.rgb*=.06+.94*sun*sun*(3-2*sun);
                output.tint.a *= nearFade * farFade * _Fade;
                output.gaussian = input.uv * 3;

                if(viewCenter.z > -.05 || z < 3*depthSigma || output.tint.a < .001)
                {
                    output.positionCS=float4(2,2,2,1);
                    output.distanceToEye=distanceToEye;
                    return output;
                }

                float2 q0=float2(UNITY_MATRIX_P._m00*(axisX.x/z+viewCenter.x*axisX.z/(z*z)),UNITY_MATRIX_P._m11*(axisX.y/z+viewCenter.y*axisX.z/(z*z)));
                float2 q1=float2(UNITY_MATRIX_P._m00*(axisY.x/z+viewCenter.x*axisY.z/(z*z)),UNITY_MATRIX_P._m11*(axisY.y/z+viewCenter.y*axisY.z/(z*z)));
                float2 q2=float2(UNITY_MATRIX_P._m00*(axisZ.x/z+viewCenter.x*axisZ.z/(z*z)),UNITY_MATRIX_P._m11*(axisZ.y/z+viewCenter.y*axisZ.z/(z*z)));
                float a=q0.x*q0.x+q1.x*q1.x+q2.x*q2.x;
                float b=q0.x*q0.y+q1.x*q1.y+q2.x*q2.y;
                float d=q0.y*q0.y+q1.y*q1.y+q2.y*q2.y;
                a+=.3*4/(_ScreenParams.x*_ScreenParams.x);
                d+=.3*4/(_ScreenParams.y*_ScreenParams.y);
                float middle=.5*(a+d), radius=sqrt(max(0,.25*(a-d)*(a-d)+b*b));
                float lambda1=max(.0000000001,middle+radius),lambda2=max(.0000000001,middle-radius);
                float2 major=abs(b)>.0000000001 ? normalize(float2(b,lambda1-a)) : (a>=d?float2(1,0):float2(0,1));
                float2 minor=float2(-major.y,major.x);
                float2 ellipse=3*(major*sqrt(lambda1)*input.uv.x+minor*sqrt(lambda2)*input.uv.y);
                output.positionCS=TransformWorldToHClip(center);
                output.positionCS.xy+=ellipse*output.positionCS.w;
                output.distanceToEye=distanceToEye;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float radiusSquared=dot(input.gaussian,input.gaussian);
                if(radiusSquared>9) discard;
                float alpha=min(.96,input.tint.a*exp(-.5*radiusSquared));
                if(alpha<.002) discard;
                float fog=1-exp(-_FogDensity*_FogDensity*input.distanceToEye*input.distanceToEye);
                float3 color=lerp(input.tint.rgb,_FogColor.rgb,saturate(fog));
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
}
