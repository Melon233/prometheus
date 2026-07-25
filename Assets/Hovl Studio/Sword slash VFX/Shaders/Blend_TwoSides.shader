Shader "Hovl/Particles/Blend_TwoSides"
{
    Properties
    {
        _Cutoff("Mask Clip Value", Float) = 0.5
        _MainTex("Main Tex", 2D) = "white" {}
        _Mask("Mask", 2D) = "white" {}
        _Noise("Noise", 2D) = "white" {}
        _SpeedMainTexUVNoiseZW("Speed MainTex U/V + Noise Z/W", Vector) = (0, 0, 0, 0)
        _FrontFacesColor("Front Faces Color", Color) = (0, 0.2313726, 1, 1)
        _BackFacesColor("Back Faces Color", Color) = (0.1098039, 0.4235294, 1, 1)
        _Emission("Emission", Float) = 2
        [Toggle] _UseFresnel("Use Fresnel?", Float) = 1
        [Toggle] _SeparateFresnel("SeparateFresnel", Float) = 0
        _SeparateEmission("Separate Emission", Float) = 2
        _FresnelColor("Fresnel Color", Color) = (1, 1, 1, 1)
        _Fresnel("Fresnel", Float) = 1
        _FresnelEmission("Fresnel Emission", Float) = 1
        [Toggle] _UseCustomData("Use Custom Data?", Float) = 0
        [HideInInspector] _texcoord("", 2D) = "white" {}
        [HideInInspector] _tex4coord("", 2D) = "white" {}
        [HideInInspector] __dirty("", Int) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "IsEmissive" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "TwoSidedUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirectionWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                output.color = input.color;
                output.uv = input.uv;
                output.uv4 = input.uv4;
                return output;
            }

            half4 Frag(
                Varyings input,
                FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half isFrontFace = IS_FRONT_VFACE(facing, 1.0h, 0.0h);
                half3 normalWS = normalize(
                    IS_FRONT_VFACE(facing, input.normalWS, -input.normalWS));
                half3 viewDirectionWS = normalize(input.viewDirectionWS);

                half fresnel = PositivePow(
                    saturate(1.0h - dot(normalWS, viewDirectionWS)),
                    max(_Fresnel, 0.0001));

                half3 frontFresnelColor =
                    _FrontFacesColor.rgb * (1.0h - fresnel)
                    + _FresnelEmission * _FresnelColor.rgb * fresnel;
                half3 frontColor = lerp(
                    _FrontFacesColor.rgb,
                    frontFresnelColor,
                    saturate(_UseFresnel));
                half3 faceColor = lerp(
                    _BackFacesColor.rgb,
                    frontColor,
                    isFrontFace);

                float2 mainUV = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                mainUV += _SpeedMainTexUVNoiseZW.xy * _Time.y;
                half4 mainSample = SAMPLE_TEXTURE2D(
                    _MainTex, sampler_MainTex, mainUV);

                half3 combinedColor =
                    faceColor * mainSample.rgb;
                half3 separateFresnelColor =
                    faceColor
                    + _FresnelColor.rgb * mainSample.rgb * _SeparateEmission;
                half3 emission = lerp(
                    combinedColor,
                    separateFresnelColor,
                    saturate(_SeparateFresnel));
                emission *= _Emission * input.color.rgb * input.color.a;

                float2 maskUV = input.uv * _Mask_ST.xy + _Mask_ST.zw;
                half mask = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, maskUV).r;

                float2 noiseUV = input.uv4.xy * _Noise_ST.xy + _Noise_ST.zw;
                noiseUV += _SpeedMainTexUVNoiseZW.zw * _Time.y + input.uv4.w;
                half noise = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, noiseUV).r;

                half customData = lerp(
                    1.0h,
                    input.uv4.z,
                    saturate(_UseCustomData));
                clip(mask * noise * customData - _Cutoff);

                return half4(emission, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
