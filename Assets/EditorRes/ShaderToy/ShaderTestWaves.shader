// Ported from https://www.shadertoy.com/view/tffSDr
Shader "Prometheus/ShaderTest/Waves"
{
    Properties
    {
        _LayerCount ("Layer Count", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float _LayerCount;

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float3 palette(float t)
            {
                float3 a = float3(0.5, 0.5, 0.5);
                float3 b = float3(0.5, 0.5, 0.5);
                float3 c = float3(1.0, 1.0, 1.0);
                float3 d = float3(0.1, 0.4, 0.5);
                return a + b * cos(6.28318530718 * (c * t + d));
            }

            float4 wave(float2 uv, float amp, float freq, float phase, float thick, float3 hue)
            {
                float x = uv.x - phase;
                float y = uv.y + amp * sin(freq * x);
                float bright = smoothstep(0.0, 1.0, 1.0 - abs(y) / thick);
                return float4(bright.xxx * hue, 1.0);
            }

            float4 frag(Varyings i) : SV_Target
            {
                // uv centered on [-1,1] in Y, matching (2*fragCoord - res)/res.y
                float2 uv = i.uv * 2.0 - 1.0;
                uv.x *= _ScreenParams.x / _ScreenParams.y;

                float4 color = float4(0, 0, 0, 1);
                float step = 1.0 / _LayerCount;
                for (float layer = 0.0; layer < 1.0; layer += step)
                {
                    float amp = 0.25 + 0.25 * sin(_Time.y + layer) * (1.0 - layer);
                    float freq = 2.0;
                    float phase = _Time.y * (1.0 - layer);
                    float sbfa = abs(uv.x);
                    float thick = 0.01 + 0.001 * pow(sbfa, 8.0);
                    float3 hue = palette(0.5 * uv.x + layer - 0.5 * _Time.y);
                    color += wave(uv, amp, freq, phase, thick, hue);
                }

                return color;
            }
            ENDHLSL
        }
    }
}
