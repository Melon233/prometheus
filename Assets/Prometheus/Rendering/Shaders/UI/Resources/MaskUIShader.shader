// Resources 目录保证仅由运行时代码查找的 UI Shader 仍会进入 Player 构建。
Shader "Prometheus/UI/Alpha Mask"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _MaskUvTransform ("Mask UV Scale And Offset", Vector) = (1,1,0,0)
        _FadeStartDistance ("Fade Start Distance", Range(0,1.4142)) = 0.78
        _FadeCompleteDistance ("Fade Complete Distance", Range(0,1.4142)) = 1
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            /// <summary>描述 Unity UI CanvasRenderer 提交给顶点阶段的基础顶点数据。</summary>
            struct AppData
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            /// <summary>携带主纹理 UV、局部裁剪位置和顶点颜色到像素阶段。</summary>
            struct Varyings
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float4 _MaskUvTransform;
            half _FadeStartDistance;
            half _FadeCompleteDistance;

            /// <summary>把 UI 顶点转换到裁剪空间，并保留 Canvas 局部位置供 RectMask 和软裁剪使用。</summary>
            Varyings Vert(AppData input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            /// <summary>根据控件中心距离和代码配置的起点、终点、曲线计算径向透明度，同时保留 Unity UI 裁剪和 AlphaClip 协议。</summary>
            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed4 color = (tex2D(_MainTex, input.texcoord) + _TextureSampleAdd) * input.color;
                float2 maskUv = input.texcoord * _MaskUvTransform.xy + _MaskUvTransform.zw;
                half radialDistance = length((maskUv - 0.5h) * 2.0h);
                half fadeProgress = saturate((radialDistance - _FadeStartDistance) / (_FadeCompleteDistance - _FadeStartDistance));
                // 使用五次 smootherstep 让透明度及其一阶、二阶变化率在虚化区间两端连续衔接。
                half smootherProgress = fadeProgress * fadeProgress * fadeProgress * (fadeProgress * (fadeProgress * 6.0h - 15.0h) + 10.0h);
                half fadeAlpha = 1.0h - smootherProgress;
                color.a = input.color.a * fadeAlpha;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001h);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
