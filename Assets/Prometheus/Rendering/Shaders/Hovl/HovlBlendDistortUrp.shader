// This URP-native shader replaces the original Surface Shader and samples the URP opaque texture instead of the unsupported GrabPass.
Shader "Prometheus/URP/Hovl/BlendDistort"
{
    Properties
    {
        _MainTex("MainTex", 2D) = "white" {}
        _Noise("Noise", 2D) = "white" {}
        _Flow("Flow", 2D) = "white" {}
        _Mask("Mask", 2D) = "white" {}
        _NormalMap("NormalMap", 2D) = "bump" {}
        _Color("Color", Color) = (0.5,0.5,0.5,1)
        _Distortionpower("Distortion power", Float) = 0
        _SpeedMainTexUVNoiseZW("Speed MainTex U/V + Noise Z/W", Vector) = (0,0,0,0)
        _DistortionSpeedXYPowerZ("Distortion Speed XY Power Z", Vector) = (0,0,0,0)
        _Emission("Emission", Float) = 2
        _Opacity("Opacity", Range(0,3)) = 1
        [Toggle] _Usedepth("Use depth?", Float) = 1
        [Toggle] _Softedges("Soft edges", Float) = 0
        _Depthpower("Depth power", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);
            TEXTURE2D(_Flow);
            SAMPLER(sampler_Flow);
            TEXTURE2D(_Mask);
            SAMPLER(sampler_Mask);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Noise_ST;
                float4 _Flow_ST;
                float4 _Mask_ST;
                float4 _NormalMap_ST;
                float4 _SpeedMainTexUVNoiseZW;
                float4 _DistortionSpeedXYPowerZ;
                half4 _Color;
                float _Distortionpower;
                float _Emission;
                float _Opacity;
                float _Usedepth;
                float _Softedges;
                float _Depthpower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float4 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                float4 texcoord : TEXCOORD3;
                float eyeDepth : TEXCOORD4;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // The vertex stage exports screen position and eye depth so opaque/depth texture sampling remains stable for particles.
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.screenPos = ComputeScreenPos(positionInputs.positionCS);
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.texcoord = input.texcoord;
                output.eyeDepth = -positionInputs.positionVS.z;
                output.color = input.color;
                return output;
            }

            // The fragment stage combines refracted scene color with the animated Hovl emission and preserves custom z/w particle controls.
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUv = input.screenPos.xy / input.screenPos.w;
                float2 normalUv = TRANSFORM_TEX(input.texcoord.xy, _NormalMap) + (_SpeedMainTexUVNoiseZW.zw * _Time.y);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, normalUv), _Distortionpower);
                half3 sceneColor = SampleSceneColor(saturate(screenUv + normalTS.xy));
                float2 mainUv = TRANSFORM_TEX(input.texcoord.xy, _MainTex) + (_SpeedMainTexUVNoiseZW.xy * _Time.y);
                float2 flowUv = TRANSFORM_TEX(input.texcoord.xy, _Flow) + (_DistortionSpeedXYPowerZ.xy * _Time.y);
                float2 maskUv = TRANSFORM_TEX(input.texcoord.xy, _Mask);
                half4 flowSample = SAMPLE_TEXTURE2D(_Flow, sampler_Flow, flowUv);
                half4 maskSample = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, maskUv);
                half4 mainSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, mainUv - ((flowSample * maskSample) * _DistortionSpeedXYPowerZ.z).rg);
                float2 noiseUv = TRANSFORM_TEX(input.texcoord.xy, _Noise) + (_SpeedMainTexUVNoiseZW.zw * _Time.y) + float2(input.texcoord.w, 0.0);
                half4 noiseSample = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, noiseUv);
                half baseAlpha = mainSample.a * noiseSample.a * _Color.a * input.color.a * _Opacity;
                half3 effectColor = (mainSample * noiseSample * _Color * input.color * _Emission * baseAlpha).rgb;
                half3 combinedColor = lerp(sceneColor + effectColor, sceneColor * effectColor, saturate(input.texcoord.z));
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUv), _ZBufferParams);
                half depthFade = saturate(abs((sceneEyeDepth - input.eyeDepth) / max(_Depthpower, 0.0001)));
                half alphaWithDepth = lerp(saturate(baseAlpha), saturate(baseAlpha) * depthFade, _Usedepth);
                half edge = saturate(pow(abs(dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS))), 3.0) * 5.0);
                half finalAlpha = lerp(alphaWithDepth, alphaWithDepth * edge, _Softedges);
                return half4(combinedColor, finalAlpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
