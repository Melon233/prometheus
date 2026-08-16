Shader "Prometheus/Rendering/Skybox/Gradient Sun"
{
    Properties
    {
        [HideInInspector] _GradientColorCount("Gradient Color Count", Float) = 3
        [HideInInspector] [HDR] _GradientColor0("Gradient Color 0", Color) = (0.012, 0.018, 0.055, 1)
        [HideInInspector] [HDR] _GradientColor1("Gradient Color 1", Color) = (0.72, 0.34, 0.18, 1)
        [HideInInspector] [HDR] _GradientColor2("Gradient Color 2", Color) = (0.08, 0.34, 0.78, 1)
        [HideInInspector] [HDR] _GradientColor3("Gradient Color 3", Color) = (0.08, 0.34, 0.78, 1)
        [HideInInspector] [HDR] _GradientColor4("Gradient Color 4", Color) = (0.08, 0.34, 0.78, 1)
        [HideInInspector] [HDR] _GradientColor5("Gradient Color 5", Color) = (0.08, 0.34, 0.78, 1)
        [HideInInspector] [HDR] _GradientColor6("Gradient Color 6", Color) = (0.08, 0.34, 0.78, 1)
        [HideInInspector] [HDR] _GradientColor7("Gradient Color 7", Color) = (0.08, 0.34, 0.78, 1)
        [HideInInspector] _GradientColorTime0("Gradient Color Time 0", Float) = 0
        [HideInInspector] _GradientColorTime1("Gradient Color Time 1", Float) = 0.5
        [HideInInspector] _GradientColorTime2("Gradient Color Time 2", Float) = 1
        [HideInInspector] _GradientColorTime3("Gradient Color Time 3", Float) = 1
        [HideInInspector] _GradientColorTime4("Gradient Color Time 4", Float) = 1
        [HideInInspector] _GradientColorTime5("Gradient Color Time 5", Float) = 1
        [HideInInspector] _GradientColorTime6("Gradient Color Time 6", Float) = 1
        [HideInInspector] _GradientColorTime7("Gradient Color Time 7", Float) = 1
        [HideInInspector] _GradientAlphaCount("Gradient Alpha Count", Float) = 2
        [HideInInspector] _GradientAlpha0("Gradient Alpha 0", Float) = 1
        [HideInInspector] _GradientAlpha1("Gradient Alpha 1", Float) = 1
        [HideInInspector] _GradientAlpha2("Gradient Alpha 2", Float) = 1
        [HideInInspector] _GradientAlpha3("Gradient Alpha 3", Float) = 1
        [HideInInspector] _GradientAlpha4("Gradient Alpha 4", Float) = 1
        [HideInInspector] _GradientAlpha5("Gradient Alpha 5", Float) = 1
        [HideInInspector] _GradientAlpha6("Gradient Alpha 6", Float) = 1
        [HideInInspector] _GradientAlpha7("Gradient Alpha 7", Float) = 1
        [HideInInspector] _GradientAlphaTime0("Gradient Alpha Time 0", Float) = 0
        [HideInInspector] _GradientAlphaTime1("Gradient Alpha Time 1", Float) = 1
        [HideInInspector] _GradientAlphaTime2("Gradient Alpha Time 2", Float) = 1
        [HideInInspector] _GradientAlphaTime3("Gradient Alpha Time 3", Float) = 1
        [HideInInspector] _GradientAlphaTime4("Gradient Alpha Time 4", Float) = 1
        [HideInInspector] _GradientAlphaTime5("Gradient Alpha Time 5", Float) = 1
        [HideInInspector] _GradientAlphaTime6("Gradient Alpha Time 6", Float) = 1
        [HideInInspector] _GradientAlphaTime7("Gradient Alpha Time 7", Float) = 1
        [HideInInspector] _GradientMode("Gradient Mode", Float) = 0
        _Exposure("Exposure", Range(0, 8)) = 1
        _SunGradientAxisInfluence("Sun Rotation Influence", Range(0, 1)) = 0
        [HDR] _SunColor("Sun Color", Color) = (1, 0.88, 0.65, 1)
        _SunIntensity("Sun Intensity", Range(0, 32)) = 4
        _SunAngularDiameter("Sun Angular Diameter", Range(0.1, 15)) = 0.53
        _SunHaloSize("Sun Halo Size", Range(0.1, 90)) = 8
        _SunHaloIntensity("Sun Halo Intensity", Range(0, 8)) = 0.25
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "Skybox"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GradientColor0;
                float4 _GradientColor1;
                float4 _GradientColor2;
                float4 _GradientColor3;
                float4 _GradientColor4;
                float4 _GradientColor5;
                float4 _GradientColor6;
                float4 _GradientColor7;
                float4 _SunColor;
                float _GradientColorCount;
                float _GradientColorTime0;
                float _GradientColorTime1;
                float _GradientColorTime2;
                float _GradientColorTime3;
                float _GradientColorTime4;
                float _GradientColorTime5;
                float _GradientColorTime6;
                float _GradientColorTime7;
                float _GradientAlphaCount;
                float _GradientAlpha0;
                float _GradientAlpha1;
                float _GradientAlpha2;
                float _GradientAlpha3;
                float _GradientAlpha4;
                float _GradientAlpha5;
                float _GradientAlpha6;
                float _GradientAlpha7;
                float _GradientAlphaTime0;
                float _GradientAlphaTime1;
                float _GradientAlphaTime2;
                float _GradientAlphaTime3;
                float _GradientAlphaTime4;
                float _GradientAlphaTime5;
                float _GradientAlphaTime6;
                float _GradientAlphaTime7;
                float _GradientMode;
                float _Exposure;
                float _SunGradientAxisInfluence;
                float _SunIntensity;
                float _SunAngularDiameter;
                float _SunHaloSize;
                float _SunHaloIntensity;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
            };

            /// Converts a linear RGB color to Oklab for perceptually uniform gradient interpolation.
            float3 LinearRgbToOklab(float3 color)
            {
                float3 lms = mul(float3x3(0.4122214708, 0.5363325363, 0.0514459929, 0.2119034982, 0.6806995451, 0.1073969566, 0.0883024619, 0.2817188376, 0.6299787005), color);
                lms = sign(lms) * pow(abs(lms), 1.0 / 3.0);
                return mul(float3x3(0.2104542553, 0.7936177850, -0.0040720468, 1.9779984951, -2.4285922050, 0.4505937099, 0.0259040371, 0.7827717662, -0.8086757660), lms);
            }

            /// Converts an Oklab color back to linear RGB after perceptual interpolation.
            float3 OklabToLinearRgb(float3 color)
            {
                float3 lms = mul(float3x3(1.0, 0.3963377774, 0.2158037573, 1.0, -0.1055613458, -0.0638541728, 1.0, -0.0894841775, -1.2914855480), color);
                lms = lms * lms * lms;
                return mul(float3x3(4.0767416621, -3.3077115913, 0.2309699292, -1.2684380046, 2.6097574011, -0.3413193965, -0.0041960863, -0.7034186147, 1.7076147010), lms);
            }

            /// Interpolates one gradient segment using the mode authored by Unity's Gradient Bar.
            float4 InterpolateGradientColor(float4 previousColor, float4 nextColor, float interpolation)
            {
                if (_GradientMode < 0.5)
                {
                    return lerp(previousColor, nextColor, interpolation);
                }

                if (_GradientMode < 1.5)
                {
                    return interpolation < 1.0 ? previousColor : nextColor;
                }

                float3 perceptualColor = OklabToLinearRgb(lerp(LinearRgbToOklab(previousColor.rgb), LinearRgbToOklab(nextColor.rgb), interpolation));
                return float4(perceptualColor, lerp(previousColor.a, nextColor.a, interpolation));
            }

            /// Evaluates the eight material-backed HDR color keys displayed by the custom Gradient Bar.
            float4 EvaluateGradientColor(float coordinate)
            {
                float4 colors[8] = { _GradientColor0, _GradientColor1, _GradientColor2, _GradientColor3, _GradientColor4, _GradientColor5, _GradientColor6, _GradientColor7 };
                float times[8] = { _GradientColorTime0, _GradientColorTime1, _GradientColorTime2, _GradientColorTime3, _GradientColorTime4, _GradientColorTime5, _GradientColorTime6, _GradientColorTime7 };
                float4 result = colors[0];
                [unroll] for (int index = 1; index < 8; index++)
                {
                    if (index >= (int)_GradientColorCount)
                    {
                        break;
                    }

                    float segmentInterpolation = saturate((coordinate - times[index - 1]) / max(times[index] - times[index - 1], 0.00001));
                    result = InterpolateGradientColor(colors[index - 1], colors[index], segmentInterpolation);
                    if (coordinate <= times[index])
                    {
                        break;
                    }
                }

                return result;
            }

            /// Evaluates the independent alpha keys stored by Unity's Gradient Bar.
            float EvaluateGradientAlpha(float coordinate)
            {
                float alphas[8] = { _GradientAlpha0, _GradientAlpha1, _GradientAlpha2, _GradientAlpha3, _GradientAlpha4, _GradientAlpha5, _GradientAlpha6, _GradientAlpha7 };
                float times[8] = { _GradientAlphaTime0, _GradientAlphaTime1, _GradientAlphaTime2, _GradientAlphaTime3, _GradientAlphaTime4, _GradientAlphaTime5, _GradientAlphaTime6, _GradientAlphaTime7 };
                float result = alphas[0];
                [unroll] for (int index = 1; index < 8; index++)
                {
                    if (index >= (int)_GradientAlphaCount)
                    {
                        break;
                    }

                    float segmentInterpolation = saturate((coordinate - times[index - 1]) / max(times[index] - times[index - 1], 0.00001));
                    result = _GradientMode > 0.5 && _GradientMode < 1.5 && segmentInterpolation < 1.0 ? alphas[index - 1] : lerp(alphas[index - 1], alphas[index], segmentInterpolation);
                    if (coordinate <= times[index])
                    {
                        break;
                    }
                }

                return result;
            }

            /// Builds the world-space sky ray consumed by the gradient and moving sun calculations.
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.directionWS = TransformObjectToWorldDir(input.positionOS);
                return output;
            }

            /// Renders the authored vertical gradient and a sun disk that follows the URP Sun Source direction.
            half4 Frag(Varyings input) : SV_Target
            {
                float3 viewDirection = normalize(input.directionWS);
                Light mainLight = GetMainLight();
                float3 sunDirection = normalize(mainLight.direction);
                float3 gradientAxis = normalize(lerp(float3(0.0, 1.0, 0.0), sunDirection, _SunGradientAxisInfluence));
                float gradientCoordinate = saturate(dot(viewDirection, gradientAxis) * 0.5 + 0.5);
                float4 gradientColor = EvaluateGradientColor(gradientCoordinate);
                gradientColor.a = EvaluateGradientAlpha(gradientCoordinate);
                float sunDirectionDot = dot(viewDirection, sunDirection);
                float sunDiskThreshold = cos(radians(_SunAngularDiameter * 0.5));
                float sunDiskAntialiasing = max(fwidth(sunDirectionDot), 0.00001);
                float sunDisk = smoothstep(sunDiskThreshold - sunDiskAntialiasing, sunDiskThreshold + sunDiskAntialiasing, sunDirectionDot);
                float sunHaloThreshold = cos(radians(_SunHaloSize));
                float sunHalo = pow(saturate((sunDirectionDot - sunHaloThreshold) / max(1.0 - sunHaloThreshold, 0.00001)), 2.0);
                float3 sunRadiance = _SunColor.rgb * mainLight.color * _SunIntensity * (sunDisk + sunHalo * _SunHaloIntensity);
                return half4((gradientColor.rgb + sunRadiance) * _Exposure, gradientColor.a);
            }
            ENDHLSL
        }
    }

    CustomEditor "Xuan.Prometheus.Rendering.Editor.PrometheusGradientSkyboxShaderGUI"
    Fallback Off
}
