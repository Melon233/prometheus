// This URP-native shader preserves the two-sided color, fresnel, dissolve, and particle custom-data behavior used by the original Hovl material.
Shader "Prometheus/URP/Hovl/Blend_TwoSides"
{
    Properties
    {
        _Cutoff("Mask Clip Value", Float) = 0.5
        _MainTex("Main Tex", 2D) = "white" {}
        _Mask("Mask", 2D) = "white" {}
        _Noise("Noise", 2D) = "white" {}
        _SpeedMainTexUVNoiseZW("Speed MainTex U/V + Noise Z/W", Vector) = (0,0,0,0)
        _FrontFacesColor("Front Faces Color", Color) = (0,0.2313726,1,1)
        _BackFacesColor("Back Faces Color", Color) = (0.1098039,0.4235294,1,1)
        _Emission("Emission", Float) = 2
        [Toggle] _UseFresnel("Use Fresnel?", Float) = 1
        [Toggle] _SeparateFresnel("Separate Fresnel", Float) = 0
        _SeparateEmission("Separate Emission", Float) = 2
        _FresnelColor("Fresnel Color", Color) = (1,1,1,1)
        _Fresnel("Fresnel", Float) = 1
        _FresnelEmission("Fresnel Emission", Float) = 1
        [Toggle] _UseCustomData("Use Custom Data?", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="Transparent" "PreviewType"="Plane" }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
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
            TEXTURE2D(_Mask);
            SAMPLER(sampler_Mask);
            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Mask_ST;
                float4 _Noise_ST;
                float4 _SpeedMainTexUVNoiseZW;
                half4 _FrontFacesColor;
                half4 _BackFacesColor;
                half4 _FresnelColor;
                float _Cutoff;
                float _Emission;
                float _UseFresnel;
                float _SeparateFresnel;
                float _SeparateEmission;
                float _Fresnel;
                float _FresnelEmission;
                float _UseCustomData;
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
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 texcoord : TEXCOORD2;
                half4 color : COLOR;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // The vertex stage keeps the original particle UV z/w channels because the effect stores custom dissolve controls in them.
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
                output.texcoord = input.texcoord;
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            // The fragment stage reproduces the original face-color selection, fresnel emission, and mask/noise clipping without a Surface Shader.
            half4 Frag(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = saturate(pow(1.0h - saturate(abs(dot(normalize(input.normalWS), viewDirectionWS))), max(_Fresnel, 0.0001)));
                half4 frontColor = lerp(_FrontFacesColor, (_FrontFacesColor * (1.0h - fresnel)) + (_FresnelEmission * _FresnelColor * fresnel), _UseFresnel);
                half isFrontFace = IS_FRONT_VFACE(frontFace, 1.0h, 0.0h);
                half4 faceColor = lerp(_BackFacesColor, frontColor, isFrontFace);
                float2 mainUv = TRANSFORM_TEX(input.texcoord.xy, _MainTex) + (_SpeedMainTexUVNoiseZW.xy * _Time.y);
                half4 mainSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, mainUv);
                float2 maskUv = TRANSFORM_TEX(input.texcoord.xy, _Mask);
                float2 noiseUv = TRANSFORM_TEX(input.texcoord.xy, _Noise) + (_SpeedMainTexUVNoiseZW.zw * _Time.y) + input.texcoord.w;
                half dissolve = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, maskUv).r * SAMPLE_TEXTURE2D(_Noise, sampler_Noise, noiseUv).r * lerp(1.0h, input.texcoord.z, _UseCustomData);
                clip(dissolve - _Cutoff);
                half3 joinedEmission = (faceColor * _Emission * input.color * input.color.a * mainSample).rgb;
                half3 separatedEmission = ((faceColor + (_FresnelColor * mainSample * _SeparateEmission)) * _Emission * input.color * input.color.a).rgb;
                half3 finalColor = MixFog(lerp(joinedEmission, separatedEmission, _SeparateFresnel), input.fogFactor);
                return half4(finalColor, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
