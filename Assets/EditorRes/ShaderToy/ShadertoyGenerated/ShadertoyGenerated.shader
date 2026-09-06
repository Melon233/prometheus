Shader "Prometheus/Shadertoy/ShadertoyGenerated"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; };
            Varyings Vert(Attributes input) { Varyings output; output.positionHCS = TransformObjectToHClip(input.positionOS.xyz); return output; }
            float glsl_mod(float x, float y) { return x - y * floor(x / y); }
            float2 glsl_mod(float2 x, float2 y) { return x - y * floor(x / y); }
            float3 glsl_mod(float3 x, float3 y) { return x - y * floor(x / y); }
            float4 glsl_mod(float4 x, float4 y) { return x - y * floor(x / y); }
            float3 glsl_expand3(float x) { return float3(x, x, x); }
            float3 glsl_expand3(float3 x) { return x; }
            float3 glsl_expand3(float4 x) { return x.xyz; }
            float2 glsl_expand2(float x) { return float2(x, x); }
            float2 glsl_expand2(float2 x) { return x; }
            float4 glsl_expand4(float x) { return float4(x, x, x, x); }
            float4 glsl_expand4(float3 x, float w) { return float4(x, w); }
            // ref:
            // https://www.shadertoy.com/view/tssSDN
            // https://qiita.com/ukeyshima/items/221b0384d39f521cad8f
            
            #define MAX_DIST 1000.
            #define SURF_DIST .0001
            #define EPS .0001
            #define PI 3.141592
            #define PI2 PI*2.
            #define saturate(a) clamp(a,0.,1.)
            #define S(a,b,t) smoothstep(a,b,t)
            #define TS(a,b,c,d,t) smoothstep(a,b,t) * smoothstep(d,c,t)
            #define REP(p,r) glsl_mod(p,r) - r * .5  
            
            /*
             * utilities
             */
            
            float rand(float2 co){
                return frac(sin(dot(co, float2(12.9898, 78.233))) * 43758.5453);
            }
            
            float2x2 rot(float a) {
                float s = sin(a), c = cos(a);
                return float2x2(c, -s, s, c);
            }
            
            float sdSphere(float3 p, float s) {
                return length(p) - s;
            }
            
            float sdBox(float3 p, float3 b) {
                float3 q = abs(p) - b;
                return length(max(q, 0.)) + min(max(q.x, max(q.y, q.z)), 0.);
            }
            
            float opUnion(float d1, float d2) {
                return min(d1, d2);
            }
            
            // ref: https://www.shadertoy.com/view/ldlcRf
            float2 minMat(float2 d1, float2 d2) {
            	return (d1.x < d2.x) ? d1 : d2;
            }
            
            /* end utilities */
            
            float rep = .04;
            
            float2 scene(float3 p) {
            
                float2 d = float2(100000., 0.);
                float mat = 0.;
                float t = _Time.y;
                float2 m = float4(0, 0, 0, 0).xy / float3(_ScreenParams.xy, 1.0).xy;
                
                float3 q = p;
                
                // float rep = .05;
            
                float3 spo = float3(
                    sin(t * 1.8) * .25,
                    .32,
                    cos(t * 2.2) * .3
                );
                float3 sp = q - spo;
                d.x = sdSphere(sp, .075);
            
                float2 id = floor(q.xz / rep);
            
                float hash = rand(id * .001);
            
                q.xz = glsl_mod(q.xz, rep) - rep * .5;
            
                float3 bcp = glsl_expand3(0);
                bcp.xz = id * rep + rep * .5;
                
                float bsDist = length(spo.xz - bcp.xz);
                
                float s = smoothstep(0., .5, bsDist);
                
                q -= float3(
                    0.,
                    .125 - (sin(hash * PI2 + t * (2. + bsDist * .015)) * .05) * (1. - pow(s, .9)),
                    0.
                );
                
                d =  minMat(
                    d, 
                    float2(sdBox(q, float3(rep * .5, .1, rep * .5)), 1.)
                );   
                
                return d;
            }
            
            float3 getNormal(float3 p) {
                float2 e = float2(EPS, 0);
                return normalize(
                	float3(
                        scene(p + e.xyy).x - scene(p - e.xyy).x,
                        scene(p + e.yxy).x - scene(p - e.yxy).x,
                        scene(p + e.yyx).x - scene(p - e.yyx).x
                    )
                );
            }
            
            float2 raymarch(float3 ro, float3 rd, float side) {
                float accDist = 0.;    
                float mat = 0.;
            
                for(int i = 0; i < 128; i++) {
                    float3 p = ro + rd * accDist;
                    float2 result = scene(p);
                    float dist = result.x * side;
                    float3 rdi = 1. / rd;
                    mat = result.y;
                    if(abs(dist) < SURF_DIST || accDist > MAX_DIST) {
                        break;
                    }
            
                    accDist += min(
                        min(
                            (step(0., rd.x) - glsl_mod(p.x, rep)) * rdi.x,
                            (step(0., rd.z) - glsl_mod(p.z, rep)) * rdi.z
                        ) + .0001,
                       dist
                    );        
                }
                
                return float2(accDist, mat);
            }
            
            
            /*
            // front: z-
            float3 getRayDir(float2 uv, float3 p, float3 l, float z) {
                float3 forward = normalize(l - p);
                float3 right = normalize(cross(float3(0., 1., 0.), forward));
                float3 up = normalize(cross(forward, right));
                
                return normalize(right * uv.x + up * uv.y + forward * z);        
            }
            */
            
            // front z+
            float3 getRayDir(float2 uv, float3 p, float3 l, float z) {
                float3 forward = normalize(l - p);
                float3 right = normalize(cross(forward, float3(0., 1., 0.)));
                float3 up = normalize(cross(right, forward));
                
                return normalize(right * uv.x + up * uv.y + forward * z);        
            }
            
            float3x3 camera(float3 ro, float3 ta, float cr )
            {
            	float3 cw = normalize(ta - ro);
            	float3 cp = float3(sin(cr), cos(cr),0.);
            	float3 cu = normalize( cross(cw,cp) );
            	float3 cv = normalize( cross(cu,cw) );
                return float3x3( cu, cv, cw );
            }
            
            void MainImage(out float4 fragColor, in float2 fragCoord)
            {
                float t = _Time.y;
            
                float2 uv = (fragCoord.xy * 2. - float3(_ScreenParams.xy, 1.0).xy) / min(float3(_ScreenParams.xy, 1.0).x, float3(_ScreenParams.xy, 1.0).y);
                float2 m = (float4(0, 0, 0, 0).xy * 2. - float3(_ScreenParams.xy, 1.0).xy) / min(float3(_ScreenParams.xy, 1.0).x, float3(_ScreenParams.xy, 1.0).y);
            
                float3 ro = float3(
                    m.x * 0. + 1.,
                    m.y * 0. + 1.,
                    1.2
                );
                float3 ta = float3(0., .2, 0.);
                float3 rd = getRayDir(uv, ro, ta, 3.5);
            
                float2 result = raymarch(ro, rd, 1.);
                float dist = result.x;
                float mat = result.y;
                
                float3 col = glsl_expand3(0.);
                
                if(dist < MAX_DIST) {    
                    float3 p = ro + rd * dist;
                    float3 l = normalize(float3(1., 1., -1.));
                    float3 n = getNormal(p);    
                    float3 r = reflect(rd, n);
            
                    float diffuse = dot(l, n) * .5 + .5;
                    float3 diffuseColor = glsl_expand3(diffuse);
            
                    if(mat < .5) {
                        diffuseColor *= float3(1., 0., 0.);
                    } else {
                        diffuseColor *= float3(1., 1., 1.); 
                        if(n.x > .5) {
                            diffuseColor = diffuse * float3(1., 0., 0.);
                        }
                        if(n.y > .5) {
                            diffuseColor = diffuse * float3(1., .9, .9);
                        }
                        if(n.z > .5) {
                            diffuseColor = diffuse * float3(.6, 0., 0.);
                        }
                    }
            
                    col = diffuseColor;       
                }
            
                    
                col = pow(col, glsl_expand3(.4545));
                
                fragColor = float4(col, 1.);
            }
            half4 Frag(Varyings input) : SV_Target { float4 color; MainImage(color, input.positionHCS.xy); return color; }
            ENDHLSL
        }
    }
}
