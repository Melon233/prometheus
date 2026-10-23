// Writes raw depth values from an R32_SFloat texture into the bound depth target (initial contents of depth
// textures carried over from earlier frames, e.g. cached shadow maps). Texel rows map 1:1 to target rows.
// RG32_SFloat sources also carry the stencil value in g: pass 0 writes depth and clears stencil, then pass 1 is
// drawn once per stencil bit (Ref = WriteMask = the bit, pixels without the bit discarded).
Shader "Hidden/RenderDocToUnity/WriteDepth"
{
    Properties { _RDStencilBit ("Stencil bit", Float) = 1 }
    HLSLINCLUDE
    #pragma target 4.5
    Texture2D<float2> _RDDepth;
    float4 _RDDstSize;      // xy: target size (the source is scaled when a screen-relative target changed size)
    float _RDStencilBit;
    struct V2F { float4 pos : SV_Position; };
    V2F vert(uint id : SV_VertexID)
    {
        V2F o;
        float2 uv = float2((id << 1) & 2, id & 2);
        o.pos = float4(uv * 2 - 1, 0.5, 1);
        return o;
    }
    float2 Source(float4 pos)
    {
        uint w, h;
        _RDDepth.GetDimensions(w, h);
        int2 p = int2(pos.xy * float2(w, h) / max(_RDDstSize.xy, 1));
        return _RDDepth.Load(int3(min(p, int2(w, h) - 1), 0));
    }
    ENDHLSL
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite On Cull Off ColorMask 0
            Stencil { Ref 0 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float frag(V2F i) : SV_Depth { return Source(i.pos).x; }
            ENDHLSL
        }
        Pass
        {
            ZTest Always ZWrite Off Cull Off ColorMask 0
            Stencil { Ref [_RDStencilBit] WriteMask [_RDStencilBit] Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            void frag(V2F i)
            {
                uint s = (uint)round(Source(i.pos).y);
                if ((s & (uint)_RDStencilBit) == 0) discard;
            }
            ENDHLSL
        }
    }
}
