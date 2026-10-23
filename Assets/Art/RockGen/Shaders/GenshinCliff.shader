// RockGen 程序化悬崖的原神（蒙德）风格着色器（由 Stylized/GenshinRock 发展而来）。
// 光照：半兰伯特 → 两档卡通渐变，暗部乘冷色阴影色，亮部窄高光带 + 轮廓光 + 少量环境光。
// 岩石固有色：亮 / 暗两色按高度渐变（上浅下深），不叠噪声杂色；
//   + 棱边提亮（顶点色 R 曲率与屏幕空间法线变化）/ 凹缝压暗 + 顶边亮带（侧面最上方一段）
// 草地：只取顶点色 A 通道（RockGen 封口写入：草地边界环以内 1、外缘 0、过渡带内线性插值，侧面 / 倒角 / 底面 0），与岩石混合（可选 A² 平方平滑）；
//   草色在水平面上做大尺度黄绿 ↔ 蓝绿变化，再叠细碎斑点。
// 地面模式（_GroundMode）：忽略顶点色，整面纯草地，用于场景地面与岩顶草地风格一致；接触 AO 贴图由 RockGenGround 通过 MaterialPropertyBlock 设置。
// 顶点色：R = 曲率（0.5 平，>0.5 凸棱，<0.5 凹缝），G = AO，B = 岩板色调（本着色器不用），A = 草地权重。
// UV0.y = 相对高度（侧面 0 底 → 1 顶；顶面、倒角 1；底面 0），由 RockGen 的 Genshin 贴图映射写入。
Shader "RockGen/GenshinCliff"
{
    Properties
    {
        [Header(Rock)]
        _RockLight ("Rock Light", Color) = (0.74, 0.72, 0.66, 1)
        _RockDark ("Rock Dark", Color) = (0.45, 0.46, 0.50, 1)
        _HeightGrad ("Height Gradient (top lighter)", Range(0, 1)) = 0.25

        [Header(Edges)]
        _EdgeHighlight ("Edge Highlight (vertex R)", Range(0, 1)) = 0.5
        _CavityDark ("Cavity Dark (vertex R)", Range(0, 1)) = 0.4
        _LipHighlight ("Top Lip Highlight", Range(0, 1)) = 0.35
        _LipWidth ("Top Lip Width (height ratio)", Range(0.005, 0.2)) = 0.04
        _EdgeLine ("Screen-space Edge Line", Range(0, 1)) = 0.25
        _EdgeLineSharp ("Edge Line Sharpness", Float) = 6
        _ContactHeight ("Contact Darkening Height (m)", Float) = 2.5
        _ContactDark ("Contact Darkening (rock base)", Range(0, 1)) = 0.38

        [Header(Grass)]
        _GrassDeep ("Grass Deep (blue-green)", Color) = (0.33, 0.56, 0.22, 1)
        _GrassLight ("Grass Light (yellow-green)", Color) = (0.70, 0.80, 0.30, 1)
        _GrassScale ("Grass Variation Scale (1/m)", Float) = 0.06
        _GrassSpeckle ("Grass Speckle", Range(0, 0.3)) = 0.06
        _GrassSpeckleScale ("Grass Speckle Scale (1/m)", Float) = 1.5
        [Toggle] _GrassSquare ("Grass Blend Squared", Float) = 1
        [Toggle] _GroundMode ("Ground Mode (all grass, ignore vertex color)", Float) = 0
        [NoScaleOffset] _ContactAOTex ("Contact AO (set by RockGenGround)", 2D) = "white" {}
        _ContactAORect ("Contact AO Rect (x0, z0, 1/w, 1/h)", Vector) = (0, 0, 0, 0)

        [Header(Lighting)]
        _ShadowTint ("Shadow Tint (cool)", Color) = (0.58, 0.63, 0.82, 1)
        _RampMid ("Ramp Mid", Range(0, 1)) = 0.5
        _RampSoft ("Ramp Softness", Range(0.001, 0.3)) = 0.05
        _HighlightStrength ("Highlight Band", Range(0, 0.5)) = 0.1
        _ShadowStrength ("Received Shadow Strength", Range(0, 1)) = 0.85
        _RimColor ("Rim Color", Color) = (1, 0.97, 0.9, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _RockLight, _RockDark, _GrassDeep, _GrassLight, _ShadowTint, _RimColor;
            float _HeightGrad;
            float _EdgeHighlight, _CavityDark, _LipHighlight, _LipWidth, _EdgeLine, _EdgeLineSharp, _ContactHeight, _ContactDark;
            float _GrassScale, _GrassSpeckle, _GrassSpeckleScale, _GrassSquare, _GroundMode;
            float _RampMid, _RampSoft, _HighlightStrength, _ShadowStrength, _RimStrength;
            float4 _ContactAORect;
        CBUFFER_END
        TEXTURE2D(_ContactAOTex); SAMPLER(sampler_ContactAOTex);

        float3 _LightDirection;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
        };

        float hash31(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
        float vnoise3(float3 p)
        {
            float3 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(hash31(i), hash31(i + float3(1, 0, 0)), f.x), lerp(hash31(i + float3(0, 1, 0)), hash31(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(hash31(i + float3(0, 0, 1)), hash31(i + float3(1, 0, 1)), f.x), lerp(hash31(i + float3(0, 1, 1)), hash31(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }
        float fbm3(float3 p) { return vnoise3(p) * 0.55 + vnoise3(p * 2.03 + 7.1) * 0.3 + vnoise3(p * 4.1 + 3.3) * 0.15; }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : TEXCOORD2;
                float fogCoord : TEXCOORD3;
                float baseY : TEXCOORD4;
                float height : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                o.baseY = TransformObjectToWorld(float3(0, 0, 0)).y;   // 物体原点所在高度（≈ 地面）
                o.height = v.uv.y;                                      // 相对高度 0 底 → 1 顶
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 P = i.positionWS;
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 sc = ComputeScreenPos(TransformWorldToHClip(P));
                #else
                    float4 sc = TransformWorldToShadowCoord(P);
                #endif
                Light L = GetMainLight(sc);
                float3 V = GetWorldSpaceNormalizeViewDir(P);
                float curv = i.color.r, ao = i.color.g, grass = i.color.a, h = saturate(i.height);
                // 地面模式：普通网格（如场景地面 Plane）没有 RockGen 顶点色，整面按纯草地处理：平坦、无 AO、草地权重 1、无接触压暗
                curv = lerp(curv, 0.5, _GroundMode); ao = lerp(ao, 1.0, _GroundMode); grass = lerp(grass, 1.0, _GroundMode);

                // ---- 岩石固有色：亮 / 暗两色按高度渐变（上浅下深），无噪声杂色 ----
                half3 rock = lerp(_RockDark.rgb, _RockLight.rgb, saturate(0.5 + (h - 0.5) * _HeightGrad * 2.0));

                // ---- 棱边提亮 / 凹缝压暗 / 顶边亮带 ----
                float edge = saturate((curv - 0.55) / 0.3);
                float cav = saturate((0.45 - curv) / 0.3);
                float edgeLine = saturate((length(fwidth(i.normalWS)) - 0.04) * _EdgeLineSharp);
                edge = max(edge, edgeLine * _EdgeLine);
                float lip = smoothstep(1.0 - _LipWidth, 1.0, h) * (1.0 - grass);   // 侧面最上方一段与倒角，不压到草上
                rock = lerp(rock, rock * 1.35 + 0.04, saturate(edge * _EdgeHighlight + lip * _LipHighlight));
                rock *= 1.0 - cav * _CavityDark;

                // ---- 草地：顶点色 A 混合（_GrassSquare 开启时用 A² 平方平滑：靠近岩石一侧草地淡入更慢、过渡更柔和）；草色大尺度黄绿 ↔ 蓝绿变化 + 细碎斑点 ----
                grass = lerp(grass, grass * grass, _GrassSquare);
                float gn = fbm3(float3(P.x, 0.0, P.z) * _GrassScale);
                half3 grassCol = lerp(_GrassDeep.rgb, _GrassLight.rgb, smoothstep(0.3, 0.7, gn));
                grassCol *= 1.0 + (vnoise3(P * _GrassSpeckleScale) - 0.5) * 2.0 * _GrassSpeckle;
                half3 albedo = lerp(rock, grassCol, grass);

                // ---- 底部接触暗 + 顶点 AO ----
                float contact = lerp(saturate((P.y - i.baseY) / _ContactHeight), 1.0, _GroundMode);
                albedo *= lerp(1.0 - _ContactDark, 1.0, smoothstep(0.0, 1.0, contact)) * lerp(0.7, 1.0, ao);   // 岩脚接触压暗，与地面接触 AO 衔接
                // 地面接触 AO：RockGenGround 按岩石占地轮廓烘焙的俯视贴图（矩形外为 1）
                if (_ContactAORect.z > 0.0)
                {
                    float2 auv = (P.xz - _ContactAORect.xy) * _ContactAORect.zw;
                    if (all(auv >= 0.0) && all(auv <= 1.0)) albedo *= SAMPLE_TEXTURE2D(_ContactAOTex, sampler_ContactAOTex, auv).r;
                }

                // ---- 卡通光照 ----
                float sh = lerp(1.0, L.shadowAttenuation, _ShadowStrength);
                float lit = (dot(N, L.direction) * 0.5 + 0.5) * sh;
                float t = smoothstep(_RampMid - _RampSoft, _RampMid + _RampSoft, lit);
                float hi = smoothstep(0.86, 0.92, lit);
                half3 col = albedo * lerp(_ShadowTint.rgb, half3(1, 1, 1), t);
                col += albedo * hi * _HighlightStrength;
                col += _RimColor.rgb * pow(1.0 - saturate(dot(N, V)), 4.0) * _RimStrength * t;
                col *= lerp(half3(1, 1, 1), L.color, 0.35);
                col += SampleSH(N) * albedo * 0.12;
                col = MixFog(col, i.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(Attributes v) : SV_POSITION
            {
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, TransformObjectToWorldNormal(v.normalOS), _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(Attributes v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
