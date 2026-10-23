// Scene view overlay for RenderDocWireframe: pass 0 writes depth only (occluders: the reconstructed objects' meshes),
// pass 1 draws wire lines in _Color, depth-tested with _ZTest and pulled slightly towards the camera so a mesh's own
// edges are not hidden by its own surface.
Shader "Hidden/RenderDocToUnity/Wire"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.55, 0.1, 0.6)
        _ZTest ("ZTest", Float) = 4
        _Bias ("Depth bias (fraction of w)", Float) = 0.0004
    }
    HLSLINCLUDE
    #include "UnityCG.cginc"
    float4 _Color;
    float _Bias;
    float4 vert(float4 v : POSITION) : SV_Position
    {
        float4 p = UnityObjectToClipPos(v.xyz);
    #if UNITY_REVERSED_Z
        p.z += _Bias * p.w;
    #else
        p.z -= _Bias * p.w;
    #endif
        return p;
    }
    float4 vertDepth(float4 v : POSITION) : SV_Position { return UnityObjectToClipPos(v.xyz); }
    float4 fragColor() : SV_Target { return _Color; }
    float4 fragNone() : SV_Target { return 0; }
    ENDHLSL
    SubShader
    {
        Pass
        {
            ZWrite On ZTest LEqual Cull Off ColorMask 0
            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragNone
            ENDHLSL
        }
        Pass
        {
            ZWrite Off ZTest [_ZTest] Cull Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragColor
            ENDHLSL
        }
    }
}
