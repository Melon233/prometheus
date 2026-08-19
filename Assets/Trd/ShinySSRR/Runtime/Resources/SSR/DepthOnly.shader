Shader "Hidden/Kronnect/SSR/DepthOnly"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        _AlphaCutOff("Alpha CutOff", Float) = 0
        _Color("Color", Color) = (1,1,1,1)
        _Cull("Culling", Int) = 0
    }
    SubShader
    {
        ColorMask 0
        ZWrite On
        Cull [_Cull]

        Pass
        {
            Name "Shiny SSR DepthOnly Pass"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment
            #pragma multi_compile_local _ DEPTH_PREPASS_ALPHA_TEST
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            #if DEPTH_PREPASS_ALPHA_TEST
                CBUFFER_START(UnityPerMaterial)
                    half _AlphaCutOff;
                    float4 _BaseMap_ST;
                CBUFFER_END

                #ifdef UNITY_DOTS_INSTANCING_ENABLED
                    UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                    UNITY_DOTS_INSTANCED_PROP(float, _AlphaCutOff)
                    UNITY_DOTS_INSTANCED_PROP(float4, _BaseMap_ST)
                    UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
                    #define _AlphaCutOff UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _AlphaCutOff)
                    #define _BaseMap_ST UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseMap_ST)
                #endif
            #endif

            #include "DepthOnly_Include.hlsl"

            ENDHLSL
        }
    }

    SubShader
    {
        ColorMask 0
        ZWrite On
        Cull [_Cull]

        Pass
        {
            Name "Shiny SSR DepthOnly Pass"  // for older GPUs with no DOTs instancing
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex UnlitPassVertex
            #pragma fragment UnlitPassFragment
            #pragma multi_compile_local _ DEPTH_PREPASS_ALPHA_TEST
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            #if DEPTH_PREPASS_ALPHA_TEST
                CBUFFER_START(UnityPerMaterial)
                    half _AlphaCutOff;
                    float4 _BaseMap_ST;
                CBUFFER_END
            #endif

            #include "DepthOnly_Include.hlsl"

            ENDHLSL
        }

    }
}
