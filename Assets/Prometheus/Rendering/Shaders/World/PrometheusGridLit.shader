// Draws an object-anchored triplanar grid in physical world units and shades the result with URP Lit lighting.
Shader "Prometheus/Rendering/World/Grid Lit"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.08,0.08,0.08,1)
        _LineColor("Line Color", Color) = (0.2,0.85,1,1)
        _CellSize("Cell Side Length", Float) = 1
        _LineWidth("Line Width", Float) = 0.02
        _GridOffset("Grid Offset", Vector) = (0,0,0,0)
        _ProjectionSharpness("Projection Sharpness", Range(1,32)) = 8
        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.35
        _SeasonTintStrength("Season Tint Strength", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "UniversalMaterialType"="Lit" }
        LOD 200
        Cull Back
        ZWrite On
        ZTest LEqual

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"
        #include "Assets/Prometheus/Rendering/ShaderLibrary/PrometheusEnvironment.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _LineColor;
            float4 _GridOffset;
            float _CellSize;
            float _LineWidth;
            float _ProjectionSharpness;
            half _Metallic;
            half _Smoothness;
            half _SeasonTintStrength;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 staticLightmapUV : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half fogFactor : TEXCOORD2;
            DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 3);
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD4;
            #endif
            #if defined(USE_APV_PROBE_OCCLUSION)
                float4 probeOcclusion : TEXCOORD5;
            #endif
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        // Transforms mesh geometry once for both Forward and GBuffer passes so both render paths evaluate the same grid surface.
        Varyings GridLitVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
            VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
            output.positionCS = positionInputs.positionCS;
            output.positionWS = positionInputs.positionWS;
            output.normalWS = normalInputs.normalWS;
            output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(positionInputs);
            #endif
            OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
            // Produces either legacy vertex SH or the APV vertex payload and optional probe occlusion required by the pixel sampler.
            OUTPUT_SH4(positionInputs.positionWS, output.normalWS, GetWorldSpaceNormalizeViewDir(positionInputs.positionWS), output.vertexSH, output.probeOcclusion);
            return output;
        }

        // Returns an anti-aliased line mask for one planar grid projection using physical grid and line dimensions.
        half CalculatePlanarGridMask(float2 anchoredPosition)
        {
            float2 gridCoordinates = anchoredPosition / _CellSize;
            float2 cellPosition = frac(gridCoordinates);
            float2 distanceToLine = min(cellPosition, 1.0 - cellPosition);
            float2 antiAliasWidth = fwidth(gridCoordinates);
            float halfLineWidth = saturate(_LineWidth * 0.5 / _CellSize);
            float2 lineMask = 1.0 - smoothstep(halfLineWidth - antiAliasWidth, halfLineWidth + antiAliasWidth, distanceToLine);
            return saturate(max(lineMask.x, lineMask.y));
        }

        // Builds object-oriented world-unit coordinates so the grid follows translation and rotation without inheriting non-uniform scale distortion.
        float3 GetAnchoredGridPosition(float3 positionWS, out float3 axisXWS, out float3 axisYWS, out float3 axisZWS)
        {
            axisXWS = TransformObjectToWorldDir(float3(1.0, 0.0, 0.0));
            axisYWS = TransformObjectToWorldDir(float3(0.0, 1.0, 0.0));
            axisZWS = TransformObjectToWorldDir(float3(0.0, 0.0, 1.0));
            float3 relativePositionWS = positionWS - TransformObjectToWorld(float3(0.0, 0.0, 0.0));
            float3 anchoredPosition = float3(dot(relativePositionWS, axisXWS), dot(relativePositionWS, axisYWS), dot(relativePositionWS, axisZWS));
            return anchoredPosition + _GridOffset.xyz;
        }

        // Blends three planar grid projections according to the surface normal so arbitrary meshes do not require authored grid UVs.
        half CalculateTriplanarGridMask(float3 positionWS, half3 normalWS)
        {
            float3 axisXWS;
            float3 axisYWS;
            float3 axisZWS;
            float3 gridPosition = GetAnchoredGridPosition(positionWS, axisXWS, axisYWS, axisZWS);
            float3 gridNormal = abs(float3(dot(normalWS, axisXWS), dot(normalWS, axisYWS), dot(normalWS, axisZWS)));
            float3 projectionWeights = pow(gridNormal, _ProjectionSharpness);
            projectionWeights /= projectionWeights.x + projectionWeights.y + projectionWeights.z;
            half xProjection = CalculatePlanarGridMask(gridPosition.zy);
            half yProjection = CalculatePlanarGridMask(gridPosition.xz);
            half zProjection = CalculatePlanarGridMask(gridPosition.xy);
            return saturate(dot(projectionWeights, half3(xProjection, yProjection, zProjection)));
        }

        // Builds one complete URP Lit surface shared by Forward and Deferred paths so GBuffer albedo exactly matches the visible procedural grid.
        SurfaceData BuildGridSurfaceData(float3 positionWS, half3 normalWS)
        {
            half gridMask = CalculateTriplanarGridMask(positionWS, normalWS);
            half3 gridColor = lerp(_BaseColor.rgb, _LineColor.rgb, gridMask);
            half3 seasonColor = PrometheusApplyGlobalSeasonTint(gridColor);
            SurfaceData surfaceData = (SurfaceData)0;
            surfaceData.albedo = lerp(gridColor, seasonColor, _SeasonTintStrength);
            surfaceData.metallic = _Metallic;
            surfaceData.specular = half3(0.0, 0.0, 0.0);
            surfaceData.smoothness = _Smoothness;
            surfaceData.normalTS = half3(0.0, 0.0, 1.0);
            surfaceData.emission = half3(0.0, 0.0, 0.0);
            surfaceData.occlusion = 1.0;
            surfaceData.alpha = 1.0;
            return surfaceData;
        }

        // Builds the URP lighting inputs shared by Forward PBR shading and Deferred GBuffer packing.
        InputData BuildGridInputData(Varyings input, half3 normalWS)
        {
            InputData inputData = (InputData)0;
            inputData.positionWS = input.positionWS;
            inputData.positionCS = input.positionCS;
            inputData.normalWS = normalWS;
            inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            // Matches URP Lit shadow-coordinate policy: cascade variants select and transform the correct cascade per pixel instead of interpolating incompatible matrices across sparse mesh triangles.
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #else
                inputData.shadowCoord = float4(0.0, 0.0, 0.0, 0.0);
            #endif
            inputData.fogCoord = input.fogFactor;
            inputData.vertexLighting = VertexLighting(input.positionWS, normalWS);
            inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            // Uses the URP 17 APV pixel-sampling signature only for probe-volume variants while preserving lightmap and legacy SH paths.
            #if defined(_SCREEN_SPACE_IRRADIANCE)
                inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy);
            #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                inputData.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(input.positionWS), normalWS, inputData.viewDirectionWS, input.positionCS.xy, input.probeOcclusion, inputData.shadowMask);
            #else
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, normalWS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
            #endif
            return inputData;
        }

        // Shades the procedural grid through URP Forward PBR lighting when the active renderer uses the Forward path.
        half4 GridLitForwardFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
            SurfaceData surfaceData = BuildGridSurfaceData(input.positionWS, normalWS);
            InputData inputData = BuildGridInputData(input, normalWS);
            half4 color = UniversalFragmentPBR(inputData, surfaceData);
            color.rgb = MixFog(color.rgb, input.fogFactor);
            color.a = 1.0;
            return color;
        }

        // Writes procedural albedo, metallic response, occlusion, world normal, smoothness, and baked GI into URP's Deferred buffers so MF.SSGI can light the grid surface.
        GBufferFragOutput GridLitGBufferFragment(Varyings input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
            SurfaceData surfaceData = BuildGridSurfaceData(input.positionWS, normalWS);
            InputData inputData = BuildGridInputData(input, normalWS);
            BRDFData brdfData;
            InitializeBRDFData(surfaceData.albedo, surfaceData.metallic, surfaceData.specular, surfaceData.smoothness, surfaceData.alpha, brdfData);
            Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
            MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI, inputData.shadowMask);
            half3 globalIllumination = GlobalIllumination(brdfData, (BRDFData)0, 0, inputData.bakedGI, surfaceData.occlusion, inputData.positionWS, inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
            return PackGBuffersBRDFData(brdfData, inputData, surfaceData.smoothness, surfaceData.emission + globalIllumination, surfaceData.occlusion);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            // Compiles the URP APV L1 and L2 variants required by SAMPLE_GI when this renderer receives Light Probes.
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma vertex GridLitVertex
            #pragma fragment GridLitForwardFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            ENDHLSL
        }

        Pass
        {
            Name "GBuffer"
            Tags { "LightMode"="UniversalGBuffer" }

            HLSLPROGRAM
            #pragma target 4.5
            // Compiles the URP APV L1 and L2 variants required by SAMPLE_GI when this renderer receives Light Probes.
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma exclude_renderers gles3 glcore
            #pragma vertex GridLitVertex
            #pragma fragment GridLitGBufferFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile_fragment _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex GridShadowVertex
            #pragma fragment GridShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Applies URP shadow bias and generates the correct clip-space depth for directional and punctual shadow maps.
            ShadowVaryings GridShadowVertex(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }

            // Writes only depth through the pass ColorMask so opaque grid objects cast solid geometry shadows.
            half4 GridShadowFragment(ShadowVaryings input) : SV_Target
            {
                return 0.0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
