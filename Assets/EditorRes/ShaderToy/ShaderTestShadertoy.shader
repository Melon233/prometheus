Shader "Prometheus/ShaderTest/ShadertoyGenerated"
{
    Properties
    {
        _LayerCount ("动画层数", Range(1, 16)) = 16
        _AnimationSpeed ("动画速度", Float) = 1
        _Brightness ("整体亮度", Float) = 2
        _PatternScale ("图案缩放", Float) = 1
        _CellOffset ("层间偏移", Vector) = (0.03, 0.04, 0, 0)
        _WarmColor ("暖色基础色", Color) = (0.6, 0.46, 0.4, 1)
        _CoolColor ("冷色基础色", Color) = (0.25, 0.15, 0.3, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            // 材质参数控制动画层数、速度、亮度、缩放、层间偏移以及颜色混合。
            CBUFFER_START(UnityPerMaterial)
            float _LayerCount;
            float _AnimationSpeed;
            float _Brightness;
            float _PatternScale;
            float4 _CellOffset;
            float4 _WarmColor;
            float4 _CoolColor;
            CBUFFER_END
            float3 Hash(float2 p) { float3 h = frac(float3(p.x,p.y,p.x)*float3(0.1031,0.103,0.0973)); h += dot(h,h.yxz+33.33); return frac((h.xxy+h.yzz)*h.zyx); }
            float2x2 Rot(float a) { float s=sin(a), c=cos(a); return float2x2(c,s,-s,c); }
            V Vert(A i) { V o; o.p=TransformObjectToHClip(i.p.xyz); o.uv=i.uv; return o; }
            half4 Frag(V i) : SV_Target
            {
                float2 c=i.uv-0.5, q=c*2.0; float e=length(q), k=q.y+q.x; c.y/=_ScreenParams.x/_ScreenParams.y; c*=2.0; float3 col=0;
                for(float l=0;l<_LayerCount;l+=1) { float2 p=mul(c,transpose(Rot(l*2)))+_CellOffset.xy*l; float t=frac((_Time.y*_AnimationSpeed*0.5+l)/16)*16; float d=(16-t)*0.25; p/=(1/d); float lum=0.5/(d*d)*smoothstep(0,1.5,t); float2 id=floor(p+1); p=frac(p)*2-1; float3 n=Hash(id*0.5+l*0.01)*2-1; float2 off=n.xy*0.8; p+=off; float sc=lerp(0.2,min(1-abs(off.x),1-abs(off.y)),n.z*0.5+0.5)*_PatternScale; p/=sc; float r=frac(abs(n.x)*9.7)*0.8+0.2; float s=saturate(pow(max(1-abs(length(p)-r+e*0.2),0),25)*0.2+smoothstep(r,r-e*0.2,length(p))); float3 base=lerp(_WarmColor.rgb,_CoolColor.rgb+float3(0,k,k)*0.25,frac(l/16+n.y)); col+=s*pow(base,3)*lum; }
                return half4(sqrt(col)*_Brightness,1);
            }
            ENDHLSL
        }
    }
}
