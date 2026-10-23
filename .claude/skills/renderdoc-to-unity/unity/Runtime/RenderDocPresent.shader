// Copies the replayed final image into a camera target: rows fixed up, supersampled images box-filtered down (each
// target pixel averages the source texels under it, in linear space), gamma-encoded 8-bit sources decoded so the
// camera's linear/sRGB target gets linear values (or re-encoded when the target stores gamma values as is).
Shader "Hidden/RenderDocToUnity/Present"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            Texture2D<float4> _RDFinal;
            float4 _RDParams;       // x source is gamma-encoded, y flip rows, zw source size
            float4 _RDTargetSize;   // xy target size, z encode output to gamma
            struct V2F { float4 pos : SV_Position; };
            V2F vert(uint id : SV_VertexID)
            {
                V2F o;
                float2 uv = float2((id << 1) & 2, id & 2);
                o.pos = float4(uv * 2 - 1, 0.5, 1);
                return o;
            }
            float3 Decode(float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }
            float3 Encode(float3 c) { return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055; }
            float4 frag(V2F i) : SV_Target
            {
                float2 pixel = floor(i.pos.xy);                    // target pixel, row 0 = first row written by this pass
                if (_RDParams.y > 0.5) pixel.y = _RDTargetSize.y - 1 - pixel.y;
                float2 ratio = _RDParams.zw / _RDTargetSize.xy;    // source texels per target pixel
                int2 n = clamp(int2(ceil(ratio - 0.01)), 1, 4);
                int2 maxP = int2(_RDParams.zw) - 1;
                float3 sum = 0;
                for (int y = 0; y < n.y; y++)
                    for (int x = 0; x < n.x; x++)
                    {
                        float2 s = (pixel + (float2(x, y) + 0.5) / n) * ratio;
                        float3 c = _RDFinal.Load(int3(min(int2(s), maxP), 0)).rgb;
                        sum += _RDParams.x > 0.5 ? Decode(c) : c;
                    }
                float3 c = sum / (n.x * n.y);
                if (_RDTargetSize.z > 0.5) c = Encode(c);
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
