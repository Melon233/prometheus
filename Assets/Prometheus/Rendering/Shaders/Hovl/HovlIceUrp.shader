// This URP-native shader replaces the original Lambert Surface Shader while preserving object-space color grading, fresnel, vertex tint, and transparency.
Shader "Prometheus/URP/Hovl/Ice"
{
    Properties
    {
        _MainTex("Main Tex", 2D) = "white" {}
        _Color("Color", Color) = (0.02352941,0.2055747,1,1)
        _UpColor("Up Color", Color) = (0.4575472,0.7381514,1,1)
        _ColorPosition("Color Position", Range(0,1)) = 0.35
        _Emission("Emission", Float) = 1
        [HDR] _FresnelColor("Fresnel Color", Color) = (1,1,1,1)
        _FresnelPower("Fresnel Power", Float) = 6
        _FresnelScale("Fresnel Scale", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Fog.hlsl declares the required fog variants through include_with_pragmas, so they must not be declared a second time here.
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _UpColor;
                half4 _FresnelColor;
                float _ColorPosition;
                float _Emission;
                float _FresnelPower;
                float _FresnelScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half objectNormalY : TEXCOORD2;
                float2 mainUv : TEXCOORD3;
                half4 color : COLOR;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // The vertex stage retains the object-space normal height used by the original shader's vertical color gradient.
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.objectNormalY = input.normalOS.y;
                output.mainUv = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            // The fragment stage computes the original gradient and fresnel response as an unlit emissive URP color.
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half gradient = saturate(input.objectNormalY + lerp(-1.0h, 1.0h, _ColorPosition));
                half4 gradientColor = lerp(_Color, _UpColor, gradient);
                half fresnel = saturate(_FresnelScale * pow(1.0h - saturate(dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS))), max(_FresnelPower, 0.0001)));
                half4 surfaceColor = ((SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.mainUv) * gradientColor * (1.0h - fresnel)) + (fresnel * _FresnelColor)) * input.color;
                half3 finalColor = MixFog(surfaceColor.rgb * _Emission, input.fogFactor);
                return half4(finalColor, input.color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
