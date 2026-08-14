// This URP-native shader replaces GrabPass with the camera opaque texture while retaining normal-map refraction and soft-particle depth fading.
Shader "Prometheus/URP/Hovl/Distortion"
{
    Properties
    {
        _NormalMap("Normal Map", 2D) = "bump" {}
        _Distortionpower("Distortion power", Float) = 0.05
        _InvFade("Soft Particles Factor", Range(0.01,3.0)) = 1.0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }

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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _NormalMap_ST;
                float _Distortionpower;
                float _InvFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float2 normalUv : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // The vertex stage passes normalized screen coordinates and positive eye depth for URP texture sampling.
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.screenPos = ComputeScreenPos(positionInputs.positionCS);
                output.normalUv = TRANSFORM_TEX(input.texcoord, _NormalMap);
                output.eyeDepth = -positionInputs.positionVS.z;
                output.color = input.color;
                return output;
            }

            // The fragment stage offsets the copied opaque color by the normal map and attenuates intersections against the scene depth texture.
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUv = input.screenPos.xy / input.screenPos.w;
                float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUv), _ZBufferParams);
                half depthFade = saturate(_InvFade * (sceneEyeDepth - input.eyeDepth));
                half particleAlpha = input.color.a * depthFade;
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.normalUv));
                float2 distortionUv = normalTS.xy * _CameraOpaqueTexture_TexelSize.xy * _Distortionpower * particleAlpha;
                half3 sceneColor = SampleSceneColor(saturate(screenUv + distortionUv));
                half distortionShape = saturate((abs(normalTS.x) + (abs(normalTS.y) * 30.0h)) - 0.03h);
                return half4(sceneColor, distortionShape);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
