Shader "Hovl/Particles/BlendDistort"
{
    Properties
    {
        _MainTex("MainTex", 2D) = "white" {}
        _Noise("Noise", 2D) = "white" {}
        _Flow("Flow", 2D) = "white" {}
        _Mask("Mask", 2D) = "white" {}
        _NormalMap("NormalMap", 2D) = "bump" {}
        _Color("Color", Color) = (0.5, 0.5, 0.5, 1)
        _Distortionpower("Distortion power", Float) = 0
        _SpeedMainTexUVNoiseZW("Speed MainTex U/V + Noise Z/W", Vector) = (0, 0, 0, 0)
        _DistortionSpeedXYPowerZ("Distortion Speed XY Power Z", Vector) = (0, 0, 0, 0)
        _Emission("Emission", Float) = 2
        _Opacity("Opacity", Range(0, 3)) = 1
        [Toggle] _Usedepth("Use depth?", Float) = 1
        [Toggle] _Softedges("Soft edges", Float) = 0
        _Depthpower("Depth power", Float) = 1
        [HideInInspector] _texcoord("", 2D) = "white" {}
        [HideInInspector] _tex4coord("", 2D) = "white" {}
        [HideInInspector] __dirty("", Int) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "IsEmissive" = "True"
        }

        Pass
        {
            Name "BlendDistort"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 uv4 : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 uv4 : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half3 viewDirectionWS : TEXCOORD3;
                float eyeDepth : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

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
                half4 _Color;
                float4 _SpeedMainTexUVNoiseZW;
                float4 _DistortionSpeedXYPowerZ;
                float _Distortionpower;
                float _Emission;
                float _Opacity;
                float _Usedepth;
                float _Softedges;
                float _Depthpower;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.eyeDepth = -TransformWorldToView(positionInputs.positionWS).z;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirectionWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                output.color = input.color;
                output.uv = input.uv;
                output.uv4 = input.uv4;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 mainSpeed = _SpeedMainTexUVNoiseZW.xy;
                float2 noiseSpeed = _SpeedMainTexUVNoiseZW.zw;
                float2 flowSpeed = _DistortionSpeedXYPowerZ.xy;

                float2 normalUV = input.uv * _NormalMap_ST.xy + _NormalMap_ST.zw;
                normalUV += _Time.y * noiseSpeed;
                half3 distortionNormal = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, normalUV),
                    _Distortionpower);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half3 sceneColor = SampleSceneColor(saturate(screenUV + distortionNormal.xy));

                float2 flowUV = input.uv4.xy * _Flow_ST.xy + _Flow_ST.zw;
                flowUV += _Time.y * flowSpeed;
                half4 flowSample = SAMPLE_TEXTURE2D(_Flow, sampler_Flow, flowUV);

                float2 maskUV = input.uv * _Mask_ST.xy + _Mask_ST.zw;
                half4 maskSample = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, maskUV);
                float2 flowOffset = flowSample.rg * maskSample.rg * _DistortionSpeedXYPowerZ.z;

                float2 mainUV = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                mainUV += _Time.y * mainSpeed;
                half4 mainSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, mainUV - flowOffset);

                float2 noiseUV = input.uv * _Noise_ST.xy + _Noise_ST.zw;
                noiseUV += _Time.y * noiseSpeed + float2(input.uv4.w, 0.0);
                half4 noiseSample = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, noiseUV);

                half sourceAlpha = mainSample.a
                    * noiseSample.a
                    * _Color.a
                    * input.color.a
                    * _Opacity;

                half3 emissiveColor = mainSample.rgb
                    * noiseSample.rgb
                    * _Color.rgb
                    * input.color.rgb
                    * _Emission
                    * sourceAlpha;

                half blendMode = saturate(input.uv4.z);
                half3 additiveResult = sceneColor + emissiveColor;
                half3 multiplyResult = sceneColor * emissiveColor;
                half3 finalColor = lerp(additiveResult, multiplyResult, blendMode);

                half alpha = saturate(sourceAlpha);

                if (_Usedepth > 0.5)
                {
                    float rawDepth = SampleSceneDepth(screenUV);
                    float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                    half depthFade = saturate(
                        (sceneEyeDepth - input.eyeDepth) / max(_Depthpower, 0.0001));
                    alpha *= depthFade;
                }

                if (_Softedges > 0.5)
                {
                    half3 normalWS = normalize(input.normalWS);
                    half3 viewDirectionWS = normalize(input.viewDirectionWS);
                    half facing = saturate(abs(dot(normalWS, viewDirectionWS)));
                    alpha *= saturate(PositivePow(facing, 3.0h) * 5.0h);
                }

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
