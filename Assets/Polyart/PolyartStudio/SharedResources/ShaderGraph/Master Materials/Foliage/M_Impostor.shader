Shader "Polyart/Utilities/Impostor"
{
    Properties
    {
        // Textures
        _MainTex("Albedo", 2D) = "white" {} 
        _NormalMap("Normals", 2D) = "bump" {}

        // Impostor Grid Params
        _GridSize("Grid Size", float) = 64
        _HorizontalSegments("Horizontal Segments", range(1, 64)) = 16
        _VerticalSegments("VerticalSegments", range(1, 15)) = 3
        _VerticalOffset("Vertical Offset", range(-15, 15)) = 0
        _VerticalStep("Horizontal Step", range(0.0, 90.0)) = 15.0

        // Color Customization
        _Hue("Hue Shift", Range(-1, 1)) = 0
        _Saturation("Saturation", Range(0, 2)) = 1
        _Value("Value (Brightness)", Range(0, 2)) = 1
        _Contrast("Contrast", Range(0, 2)) = 1
        _Smoothness("Smoothness", range(0, 1)) = 0.5
        _Metallic("Metallic", range(0, 1)) = 1.0
        _NormalStrength("Normal Strength", range(0, 10)) = 1.0

        // Opacity
        _AlphaClipThreshold("Alpha Clip Threshold", range(0, 1)) = 0.355

        // Fog
        [Toggle] _EnableFog("Enable Fog", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalRenderPipeline" }
        LOD 200
        Cull Off
        ZWrite On
        AlphaToMask On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #define DEG2RAD 0.01745328

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv           : TEXCOORD0;
                float3 positionWS   : TEXCOORD1;
                float3 normalWS     : TEXCOORD2;
                float4 tangentWS    : TEXCOORD3;
                float3 viewDirWS    : TEXCOORD4;
                float4 positionCS   : SV_POSITION;
                half fogFactor      : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _GridSize;
                uint _HorizontalSegments;
                float _VerticalSegments;
                float _VerticalOffset;
                float _VerticalStep;
                float _Smoothness;
                float _Metallic;
                float _NormalStrength;
                float _Hue;
                float _Saturation;
                float _Value;
                float _Contrast;
                float _AlphaClipThreshold;
                float _EnableFog;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 viewVec = -_WorldSpaceCameraPos + TransformObjectToWorld(float4(0,0,0,1)).xyz;
                float3 viewDir = normalize(viewVec);

                float3 M2 = mul((float3x3)GetWorldToObjectMatrix(), viewDir);
                float3 M0 = cross(mul((float3x3)GetWorldToObjectMatrix(), float3(0, 1, 0)), M2);
                float3 M1 = cross(M2, M0);
                float3x3 mat = float3x3(normalize(M0), normalize(M1), M2);

                float3 rotatedPosition = mul(input.positionOS.xyz, mat);
                output.positionWS = TransformObjectToWorld(rotatedPosition);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                
                output.normalWS = TransformObjectToWorldNormal(mul(input.normalOS, mat));
                output.tangentWS = float4(TransformObjectToWorldDir(mul(input.tangentOS.xyz, mat)), input.tangentOS.w);
                output.viewDirWS = viewDir;
                output.uv = input.uv;//TRANSFORM_TEX(input.uv, _MainTex);

                // Calculate fog factor
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            void CalculateUVs(float2 uv_MainTex, float3 viewDir, out float2 UvLB, out float2 UvRB, out float2 UvLT, out float2 UvRT, out float alpha, out float beta)
            {
                float gridSide = round(sqrt(_GridSize));
                float gridStep = 1.0 / gridSide;

                float3x3 rotationScaleMatrix = float3x3(UNITY_MATRIX_M[0].xyz, UNITY_MATRIX_M[1].xyz, UNITY_MATRIX_M[2].xyz);
                float3x3 rotationOnlyMatrix;
                rotationOnlyMatrix[0] = normalize(rotationScaleMatrix[0]);
                rotationOnlyMatrix[1] = normalize(rotationScaleMatrix[1]);
                rotationOnlyMatrix[2] = normalize(rotationScaleMatrix[2]);
                float trYawOffset = -atan2(rotationOnlyMatrix._31, rotationOnlyMatrix._11);

                float yaw = atan2(viewDir.z, viewDir.x) + TWO_PI + trYawOffset;
                float yawId = (round((yaw / TWO_PI) * _HorizontalSegments) % _HorizontalSegments);
                float yawFrac = frac((yaw / TWO_PI) * _HorizontalSegments);

                float elevation = asin(viewDir.y);
                float elevationId = max(min(round(elevation / (_VerticalStep * DEG2RAD)) - _VerticalOffset, _VerticalSegments), -_VerticalSegments);
                float elevationFrac = frac(elevation / (_VerticalStep * DEG2RAD));

                alpha = 1.0 - yawFrac;
                beta = 1.0 - elevationFrac;

                float subdLB;
                float subdRB;
                float subdLT;
                float subdRT;

                if (elevationFrac < 0.5) {
                    if (yawFrac < 0.5) {
                        subdLB = yawId + (elevationId + _VerticalSegments) * _HorizontalSegments;
                        subdRB = (yawId + 1) % _HorizontalSegments + (elevationId + _VerticalSegments) * _HorizontalSegments;
                        subdLT = yawId + (elevationId + (elevationId == _VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments; /// 
                        subdRT = ((yawId + 1) % _HorizontalSegments + (elevationId + (elevationId == _VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments);
                    }
                    else {
                        subdLB = (yawId - 1) % _HorizontalSegments + (elevationId + _VerticalSegments) * _HorizontalSegments;
                        subdRB = yawId + (elevationId + _VerticalSegments) * _HorizontalSegments;
                        subdLT = ((yawId - 1) % _HorizontalSegments + (elevationId + (elevationId == _VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments);
                        subdRT = (yawId + (elevationId + (elevationId == _VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments); /// 
                    }
                }
                else {
                    if (yawFrac < 0.5) {
                        subdLB = yawId + (elevationId - (elevationId == -_VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments; /// 
                        subdRB = (yawId + 1) % _HorizontalSegments + (elevationId + _VerticalSegments - (elevationId == -_VerticalSegments ? 0 : 1)) * _HorizontalSegments;
                        subdLT = yawId + (elevationId + _VerticalSegments) * _HorizontalSegments;
                        subdRT = ((yawId + 1) % _HorizontalSegments + (elevationId + _VerticalSegments) * _HorizontalSegments);
                    }
                    else {
                        subdLB = (yawId - 1) % _HorizontalSegments + (elevationId + _VerticalSegments - (elevationId == -_VerticalSegments ? 0 : 1)) * _HorizontalSegments;
                        subdRB = yawId + (elevationId + _VerticalSegments - (elevationId == -_VerticalSegments ? 0 : 1)) * _HorizontalSegments; /// 
                        subdLT = ((yawId - 1) % _HorizontalSegments) + (elevationId + _VerticalSegments) * _HorizontalSegments;
                        subdRT = yawId + (elevationId + _VerticalSegments) * _HorizontalSegments;
                    }
                }

                UvLB = uv_MainTex / gridSide + float2((subdLB % gridSide), floor(subdLB / gridSide)) * gridStep;
                UvRB = uv_MainTex / gridSide + float2((subdRB % gridSide), floor(subdRB / gridSide)) * gridStep;
                UvRT = uv_MainTex / gridSide + float2((subdRT % gridSide), floor(subdRT / gridSide)) * gridStep;
                UvLT = uv_MainTex / gridSide + float2((subdLT % gridSide), floor(subdLT / gridSide)) * gridStep;
            }

            // Convert RGB to HSV
            float3 RGBtoHSV(float3 color)
            {
                float maxVal = max(color.r, max(color.g, color.b));
                float minVal = min(color.r, min(color.g, color.b));
                float delta = maxVal - minVal;

                // Hue
                float hue = 0.0;
                if (delta > 0.0)
                {
                    if (maxVal == color.r)
                        hue = (color.g - color.b) / delta + (color.g < color.b ? 6.0 : 0.0);
                    else if (maxVal == color.g)
                        hue = (color.b - color.r) / delta + 2.0;
                    else
                        hue = (color.r - color.g) / delta + 4.0;

                    hue /= 6.0;
                }

                // Saturation
                float saturation = (maxVal == 0.0) ? 0.0 : (delta / maxVal);

                // Value
                float value = maxVal;

                return float3(hue, saturation, value);
            }

            // Convert HSV to RGB
            float3 HSVtoRGB(float3 hsv)
            {
                float h = hsv.x * 6.0;
                float s = hsv.y;
                float v = hsv.z;

                float c = v * s;
                float x = c * (1.0 - abs(fmod(h, 2.0) - 1.0));
                float m = v - c;

                float3 rgb;
                if (h < 1.0)
                    rgb = float3(c, x, 0.0);
                else if (h < 2.0)
                    rgb = float3(x, c, 0.0);
                else if (h < 3.0)
                    rgb = float3(0.0, c, x);
                else if (h < 4.0)
                    rgb = float3(0.0, x, c);
                else if (h < 5.0)
                    rgb = float3(x, 0.0, c);
                else
                    rgb = float3(c, 0.0, x);

                return rgb + m;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float4 color, normal;
                float metallic;
                float alpha, beta;
                float2 UvLB, UvLT, UvRB, UvRT;

                float2 uv_MainTex = input.uv;
                float3 viewDir = -input.viewDirWS;

                CalculateUVs(uv_MainTex, viewDir, UvLB, UvLT, UvRB, UvRT, alpha, beta);

                float4 cLB = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, UvLB);
                float4 cRB = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, UvRB);
                float4 cLT = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, UvLT);
                float4 cRT = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, UvRT);
                float4 cB = lerp(cRB, cLB, beta);
                float4 cT = lerp(cRT, cLT, beta);
                color = lerp(cT, cB, alpha);

                cLB = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, UvLB);
                cRB = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, UvRB);
                cLT = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, UvLT);
                cRT = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, UvRT);
                cB = lerp(cRB, cLB, beta);
                cT = lerp(cRT, cLT, beta);
                normal = lerp(cT, cB, alpha);

                // Normal mapping
                float3 normalTS = (normal * 2.0 - 1.0);
                normalTS.y = -normalTS.y;
                normalTS.xy *= _NormalStrength;
                normalTS = normalize(normalTS);


                // Convert RGB to HSV
                float3 hsv = RGBtoHSV(color.rgb);

                // Apply Hue adjustment (wrapping around)
                hsv.x = frac(hsv.x + _Hue);
                if (hsv.x < 0.0) hsv.x += 1.0;

                // Apply Saturation and Value adjustments
                hsv.y = saturate(hsv.y * _Saturation);
                hsv.z = saturate(hsv.z * _Value);

                // Convert back to RGB
                float3 rgb = HSVtoRGB(hsv);

                // Apply Contrast adjustment
                rgb = saturate((rgb - 0.5) * _Contrast + 0.5);


                // Prepare surface data
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = SafeNormalize(input.viewDirWS);
                inputData.tangentToWorld = CreateTangentToWorld(inputData.normalWS, input.tangentWS.xyz, input.tangentWS.w);
                inputData.normalWS = TransformTangentToWorld(normalTS, inputData.tangentToWorld);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.bakedGI = SampleSH(inputData.normalWS);  // Ambient light fallback

                // Before finalColor calculation
                float3 ambient = SampleSH(inputData.normalWS);

                // Surface data
                SurfaceData surfaceData;
                surfaceData.albedo = rgb;
                surfaceData.metallic = _Metallic;
                surfaceData.specular = 0.0;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = normalTS;
                surfaceData.emission = 0.0;
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = color.a;
                surfaceData.clearCoatMask = 0.0;
                surfaceData.clearCoatSmoothness = 0.0;

                // Alpha clipping
                clip(surfaceData.alpha - _AlphaClipThreshold);

                // Lighting
                half4 finalColor = UniversalFragmentPBR(inputData, surfaceData);

                // Apply fog
                if (_EnableFog > 0.5)
                {
                    finalColor.rgb = MixFog(finalColor.rgb, input.fogFactor);
                }

                return finalColor;
            }
            ENDHLSL
        }
    }
}