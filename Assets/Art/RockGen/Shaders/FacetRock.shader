// RockGen 切削岩石（FacetRockGenerator）的原神风格着色器。光照与 RockGen/GenshinCliff 相同：半兰伯特 → 两档卡通渐变，暗部乘冷色阴影色，窄高光带 + 轮廓光 + 少量环境光。
// 棱边法线平滑：生成器在每条棱两侧 normalSmoothWidth 米处插支撑环，划出平滑带；本着色器逐像素计算带内法线的过渡曲线（不用法线贴图）。
//   UV1.xyz = facet 法线 Nf（物体空间），UV1.w = t（棱上 1 → 支撑环 0，带内按到棱的距离线性变化；facet 内部 0）；
//   UV2.xyz = t × 目标法线（棱上为两侧 facet 的平分法线，转角为顶点法线），插值后归一化即目标方向 Nt，t → 0 时自然无权重。
//   法线 = slerp(Nf, Nt, p)，p 由 _Profile 选择：Round = 圆角（半径恒定的圆弧与 facet 相切：p = asin(t·sin h) / h，h 为 Nf 与 Nt 的夹角），
//   Smooth = smoothstep(t)，Linear = t。网格自带法线是线性过渡的近似，供阴影偏移与不支持的着色器退化使用。
// 顶点色：R = facet 色调（每个 facet 一个随机值，平滑带内按 1 − p 淡出，棱上两侧一致），G = 相对高度（岩底 0 → 岩顶 1）。
// 注意：任何按 facet 取值、在棱上不连续的量（色调、UV0 贴图等）都会让平滑后的棱重新读成硬边，必须在带内淡出。
Shader "RockGen/FacetRock"
{
    Properties
    {
        [Header(Rock)]
        _RockLight ("Rock Light", Color) = (0.74, 0.72, 0.66, 1)
        _RockDark ("Rock Dark", Color) = (0.45, 0.46, 0.50, 1)
        _HeightGrad ("Height Gradient (top lighter)", Range(0, 1)) = 0.3
        _FacetTone ("Facet Tone Variation", Range(0, 0.5)) = 0.1

        [Header(Edge Smoothing)]
        [Enum(Round, 0, Smooth, 1, Linear, 2)] _Profile ("Smoothing Profile", Float) = 0
        _EdgeHighlight ("Edge Highlight", Range(0, 1)) = 0.3
        _EdgeHighlightStart ("Edge Highlight Start (profile)", Range(0, 1)) = 0.4

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
            half4 _RockLight, _RockDark, _ShadowTint, _RimColor;
            float _HeightGrad, _FacetTone, _Profile, _EdgeHighlight, _EdgeHighlightStart;
            float _RampMid, _RampSoft, _HighlightStrength, _ShadowStrength, _RimStrength;
        CBUFFER_END

        float3 _LightDirection;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
            float4 facet : TEXCOORD1;   // xyz = facet 法线，w = t
            float3 target : TEXCOORD2;  // t × 目标法线
        };
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
                float3 facetWS : TEXCOORD1;
                float3 targetWS : TEXCOORD2;
                float t : TEXCOORD3;
                float4 color : TEXCOORD4;
                float fogCoord : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.facetWS = TransformObjectToWorldNormal(v.facet.xyz);
                float ml = length(v.target);
                o.targetWS = ml > 1e-6 ? TransformObjectToWorldNormal(v.target / ml) * ml : 0;   // 保留长度 t，插值后仍是 t × 目标法线
                o.t = v.facet.w;
                o.color = v.color;
                o.fogCoord = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // 带内法线：slerp(Nf, Nt, p)，同时输出 p（用于棱边提亮）
            float3 EdgeNormal(float3 Nf, float3 m, float t, out float p)
            {
                p = 0;
                float ml = length(m);
                if (t <= 1e-4 || ml <= 1e-6) return Nf;
                float3 Nt = m / ml;
                float h = acos(clamp(dot(Nf, Nt), -1.0, 1.0));
                t = saturate(t);
                if (_Profile < 0.5) p = h > 1e-3 ? asin(saturate(t * sin(h))) / h : t;   // 圆角：与 facet 相切的等半径圆弧
                else if (_Profile < 1.5) p = smoothstep(0.0, 1.0, t);
                else p = t;
                if (h < 1e-3) return normalize(lerp(Nf, Nt, p));
                return normalize((Nf * sin((1.0 - p) * h) + Nt * sin(p * h)) / sin(h));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 Nf = normalize(i.facetWS);
                float p;
                float3 N = EdgeNormal(Nf, i.targetWS, i.t, p);
                float3 P = i.positionWS;
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 sc = ComputeScreenPos(TransformWorldToHClip(P));
                #else
                    float4 sc = TransformWorldToShadowCoord(P);
                #endif
                Light L = GetMainLight(sc);
                float3 V = GetWorldSpaceNormalizeViewDir(P);

                // ---- 固有色：亮 / 暗两色按高度渐变 + facet 色调 + 棱边提亮 ----
                float h01 = saturate(i.color.g);
                half3 albedo = lerp(_RockDark.rgb, _RockLight.rgb, saturate(0.5 + (h01 - 0.5) * _HeightGrad * 2.0));
                // facet 色调在平滑带内随过渡曲线淡出（棱上为 0）：两侧 facet 色调不同，不淡出时棱上亮度跳变，读起来仍是硬边
                albedo *= 1.0 + (i.color.r - 0.5) * 2.0 * _FacetTone * (1.0 - p);
                float hl = smoothstep(_EdgeHighlightStart, 1.0, p) * _EdgeHighlight;
                albedo = lerp(albedo, albedo * 1.35 + 0.04, hl);

                // ---- 卡通光照 ----
                float sh = lerp(1.0, L.shadowAttenuation, _ShadowStrength);
                float lit = (dot(N, L.direction) * 0.5 + 0.5) * sh;
                float tr = smoothstep(_RampMid - _RampSoft, _RampMid + _RampSoft, lit);
                float hi = smoothstep(0.86, 0.92, lit);
                half3 col = albedo * lerp(_ShadowTint.rgb, half3(1, 1, 1), tr);
                col += albedo * hi * _HighlightStrength;
                col += _RimColor.rgb * pow(1.0 - saturate(dot(N, V)), 4.0) * _RimStrength * tr;
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
