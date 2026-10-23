// Draws a replayed target in editor GUI (Scene view overlay, previews). Editor GUI writes its output gamma-encoded in
// linear-space projects, so values a capture stored gamma-encoded (8-bit UNORM final image) are decoded first.
Shader "Hidden/RenderDocToUnity/Display"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} _Decode ("Decode sRGB", Float) = 1 }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Decode;
            struct V2F { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
            V2F vert(appdata_img v)
            {
                V2F o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                return o;
            }
            float4 frag(V2F i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                if (_Decode > 0.5)
                    c.rgb = c.rgb <= 0.04045 ? c.rgb / 12.92 : pow((c.rgb + 0.055) / 1.055, 2.4);
                return float4(c.rgb, 1);
            }
            ENDCG
        }
    }
}
