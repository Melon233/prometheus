#ifndef SSR_SURF_FX
#define SSR_SURF_FX

    // Copyright 2021 Kronnect - All Rights Reserved.
    TEXTURE2D(_NoiseTex);
    TEXTURE2D(_BumpMap);
    TEXTURE2D(_SmoothnessMap);
    TEXTURE2D(_OcclusionMap);
    float4 _BumpMap_ST;
    float4 _NoiseTex_TexelSize;
    half   _BumpScale;
    half   _OcclusionStrength;

    float4 _MaterialData;
    #define SMOOTHNESS _MaterialData.x
    #define FRESNEL _MaterialData.y
    #define FUZZYNESS _MaterialData.z
    #define DECAY _MaterialData.w

    float4 _SSRSettings;
    #define THICKNESS _SSRSettings.x
    #define SAMPLES _SSRSettings.y
    #define BINARY_SEARCH_ITERATIONS _SSRSettings.z
    #define MAX_RAY_LENGTH _SSRSettings.w

    float4 _SSRSettings5;
    #define REFLECTIONS_THRESHOLD _SSRSettings5.y
    #define SKYBOX_INTENSITY _SSRSettings5.z

#if SSR_THICKNESS_FINE
    #define THICKNESS_FINE _SSRSettings5.x
#else
    #define THICKNESS_FINE THICKNESS
#endif

    float4 _SSRSettings2;
    #define JITTER _SSRSettings2.x
    #define CONTACT_HARDENING _SSRSettings2.y
    #define REFLECTIVITY _SSRSettings2.w

    float4 _SSRSettings3;
    #define INPUT_SIZE _SSRSettings3.xy
    #define GOLDEN_RATIO_ACUM _SSRSettings3.z
    #define DEPTH_BIAS _SSRSettings3.w

    float4 _SSRSettings7;
    #define FAR_CAMERA_ATTENUATION_START _SSRSettings7.x
    #define FAR_CAMERA_ATTENUATION_RANGE _SSRSettings7.y

    float3 _DistortionData;
    #define DISTORTION_SPEED _DistortionData.xy
    #define SKYBOX_MULTIPLIER _DistortionData.z

    TEXTURE2D(_MetallicGradientTex);
    TEXTURE2D(_SmoothnessGradientTex);

    TEXTURE2D_X(_TransparentNormalsRT);
    float _HasTransparentNormals;

    struct AttributesSurf {
        float4 positionOS   : POSITION;
        float2 texcoord     : TEXCOORD0;
        float3 normalOS     : NORMAL;
        float4 tangentOS    : TANGENT;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct VaryingsSSRSurf {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float4 scrPos : TEXCOORD1;
        float3 positionVS : TEXCOORD2;
        #if SSR_NORMALMAP
            float4 normal    : TEXCOORD3;    // xyz: normal, w: viewDir.x
            float4 tangent   : TEXCOORD4;    // xyz: tangent, w: viewDir.y
            float4 bitangent : TEXCOORD5;    // xyz: bitangent, w: viewDir.z
        #else
            float3 normal    : TEXCOORD3;
        #endif
        #if SSR_SKYBOX
            float3 viewDirWS : TEXCOORD6;
            float3 normalWS  : TEXCOORD7;
        #endif
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    VaryingsSSRSurf VertSSRSurf(AttributesSurf input) {

        VaryingsSSRSurf output = (VaryingsSSRSurf)0;

        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
        VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

        output.positionCS = positions.positionCS;
        output.positionVS = positions.positionVS * float3(1,1,-1);
        output.scrPos     = ComputeScreenPos(positions.positionCS);
        output.uv         = TRANSFORM_TEX(input.texcoord, _BumpMap);

        half3 viewDirWS = GetCameraPositionWS() - positions.positionWS;

        #if SSR_NORMALMAP
            output.normal = half4(normalInput.normalWS, viewDirWS.x);
            output.tangent = half4(normalInput.tangentWS, viewDirWS.y);
            output.bitangent = half4(normalInput.bitangentWS, viewDirWS.z);
        #else
            output.normal = TransformWorldToViewDir(normalInput.normalWS) * float3(1,1,-1);
        #endif
        
        #if SSR_SKYBOX
            output.viewDirWS = viewDirWS;
            output.normalWS = normalInput.normalWS;
        #endif

        #if UNITY_REVERSED_Z
            output.positionCS.z += 0.001;
        #else
            output.positionCS.z -= 0.001;
        #endif

        return output;
    }

    float collision;

#if SSR_METALLIC_WORKFLOW
    float4 SSR_Pass(float2 uv, float3 normalVS, float3 rayStart, float roughness, float reflectivity) {
#else
    float4 SSR_Pass(float2 uv, float3 normalVS, float3 rayStart, float roughness) {
#endif

        // depth clip check
        float sceneDepth = GetLinearDepth(uv);
        float pz = rayStart.z;
        if (sceneDepth < pz - DEPTH_BIAS) {
            #if SSR_SKYBOX
                collision = 0;
            #endif
            discard;
            return 0;
        }

        #if SSR_ORTHO_SUPPORT
            float3 viewDirVS = float3(0, 0, 1);
        #else
            float3 viewDirVS = normalize(rayStart);
        #endif
        float3 rayDir = reflect( viewDirVS, normalVS );

        float  rayLength = MAX_RAY_LENGTH;
        float3 rayEnd = rayStart + rayDir * rayLength;
        if (rayEnd.z < 1) {
            rayLength = (1 - rayStart.z) / rayDir.z;
            rayEnd = rayStart + rayDir * rayLength;
        }

        #if defined(USING_STEREO_MATRICES)
            float4x4 proj = unity_StereoCameraProjection[unity_StereoEyeIndex];
        #else
            float4x4 proj = unity_CameraProjection;
        #endif
        #if SSR_ORTHO_SUPPORT
            float4 sposEnd = mul(proj, float4(rayEnd, 1.0));
            float2 uv1 = (sposEnd.xy + 1.0) * 0.5;
            float4 p0 = float4(uv, rayStart.z, 1.0);
            float4 p1 = float4(uv1, rayEnd.z, 1.0);
        #else
            proj._13 *= -1; // lens shift fix
            proj._23 *= -1;
            float4 sposStart = mul(proj, float4(rayStart, 1.0));
            float4 sposEnd   = mul(proj, float4(rayEnd, 1.0));
            float k0 = rcp(sposStart.w);
            float k1 = rcp(sposEnd.w);
            float q0 = rayStart.z * k0;
            float q1 = rayEnd.z * k1;
            float2 uv1 = sposEnd.xy * rcp(rayEnd.z) * 0.5 + 0.5;
            float4 p0 = float4(uv,  q0, k0);
            float4 p1 = float4(uv1, q1, k1);
        #endif

        // Calculate pixel distance for adaptive sampling
        float2 duv = uv1 - uv;
        float2 duvPixel = abs(duv * INPUT_SIZE);
        float pixelDistance = max(duvPixel.x, duvPixel.y);
        pixelDistance = max(1, pixelDistance);
        
        int sampleCount = (int)min(pixelDistance, SAMPLES);
        float rcpSampleCount = rcp((float)sampleCount);
        float rcpPixelDist   = rcp(pixelDistance) * 2.0; // skips first pixel

        // Setup interpolation endpoints
        float4 p = p0;
        float4 pprev = p0;

        #if SSR_JITTER
            float jitter = SAMPLE_TEXTURE2D(_NoiseTex, sampler_PointRepeat, uv * INPUT_SIZE * _NoiseTex_TexelSize.xy + GOLDEN_RATIO_ACUM).r;
            jitter = 1.0 + jitter * JITTER;
        #else
            float jitter = 1;
        #endif

        float3 hitp = 0;
        float depthDiff;
        float prevDepthDiff = 0; // signed distance to surface at previous sample

        // Quadratic stepping with crossing detection
        UNITY_LOOP
        for (int k = 1; k <= sampleCount; k++) {
            pprev = p;
            float progress = k * rcpSampleCount;
            float t = progress * progress;  // Quadratic interpolation factor
            t = max(t, k * rcpPixelDist);   // Guard with minimum pixel step
            #if SSR_JITTER
                t = t * jitter;
            #endif
            p = lerp(p0, p1, t);
            if (any(p.xy < 0 | p.xy >= 1)) return 0.0.xxxx;

            #if SSR_ORTHO_SUPPORT
                pz = p.z;
            #else
                pz = p.z * rcp(p.w);
            #endif
            #if SSR_BACK_FACES
                float sceneBackDepth;
                GetLinearDepths(p.xy, sceneDepth, sceneBackDepth);
                depthDiff = pz - sceneDepth;
                float trueDepth = sceneBackDepth - sceneDepth;
                // Hit if within thickness OR if ray just crossed surface (prev sample was before)
                bool hit = depthDiff > 0 && (depthDiff < trueDepth || prevDepthDiff < 0);
            #else
                sceneDepth = GetLinearDepth(p.xy);
                depthDiff = pz - sceneDepth;
                bool hit = depthDiff > 0 && (depthDiff < THICKNESS || prevDepthDiff < 0);
            #endif
            prevDepthDiff = depthDiff;
        
            if (hit) {
                if (k<2) pprev = p; // if contact is found at first sample, don't backtrack since it can fetch a pixel color from the incorrect surface
                // Binary search refinement
                UNITY_LOOP
                for (int j = 0; j < BINARY_SEARCH_ITERATIONS; j++) {
                    float4 midp = (pprev + p) * 0.5;
                    #if SSR_ORTHO_SUPPORT
                        float midpz = midp.z;
                    #else
                        float midpz = midp.z * rcp(midp.w);
                    #endif
                    sceneDepth = GetLinearDepth(midp.xy);
                    float diff = midpz - sceneDepth;
                    if (diff > 0) {
                        p = midp; pz = midpz;
                        depthDiff = diff;
                    } else {
                        pprev = midp;
                    }
                }
                float hitAccuracy = 1.0 - depthDiff / THICKNESS;
                float candidateCollision = hitAccuracy;
            
#if SSR_THICKNESS_FINE
                if (candidateCollision > collision) {
                    hitp = float3(p.xy, pz);
                    collision = candidateCollision;
                }
                if (depthDiff < THICKNESS_FINE)
                    break;
#else
                hitp = float3(p.xy, pz);
                collision = candidateCollision;
                break;
#endif
            }
        }

        #if SSR_SKYBOX_VISIBLE_ONLY
        bool isSkyboxHit = false;
        if (collision <= 0 && all(p.xy >= 0 & p.xy < 1) && sceneDepth >= _ProjectionParams.z * 0.95) {
            collision = 1;
            hitp = float3(p.xy, rayStart.z);
            isSkyboxHit = true;
        }
        #endif

        if (collision > 0) {
            #if SSR_METALLIC_WORKFLOW
                float reflectionIntensityBase = reflectivity;
            #else
                float reflectionIntensityBase = 1.0 - roughness;
            #endif
            float fresnel = 1.0 - FRESNEL * abs(dot(normalVS, viewDirVS));

            float zdist = (hitp.z - rayStart.z) / (0.0001 + rayEnd.z - rayStart.z);
            collision *= 1.0 - saturate(zdist);

            #if SSR_SKYBOX_VISIBLE_ONLY
                float reflectionAmount = isSkyboxHit 
                    ? reflectionIntensityBase * fresnel * SKYBOX_INTENSITY
                    : reflectionIntensityBase * pow(collision, DECAY) * fresnel;
                float blurAmount = isSkyboxHit 
                    ? FUZZYNESS * roughness
                    : max(0, rayLength * zdist - CONTACT_HARDENING) * FUZZYNESS * roughness;
            #else
                float reflectionAmount = reflectionIntensityBase * pow(collision, DECAY) * fresnel; 
                float blurAmount = max(0, rayLength * zdist - CONTACT_HARDENING) * FUZZYNESS * roughness;
            #endif

            return float4(hitp.xy, blurAmount + 0.001, reflectionAmount);
        }
        return float4(0,0,0,0);
    }


    float4 FragSSRSurf (VaryingsSSRSurf input) : SV_Target {
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        input.scrPos.xy /= input.scrPos.w;
        //input.scrPos = SSRStereoTransformScreenSpaceTex(input.scrPos);

        if (input.positionVS.z > FAR_CAMERA_ATTENUATION_START + FAR_CAMERA_ATTENUATION_RANGE) {
            return 0;
        }

        float3 normalWS;
        #if SSR_SKYBOX
            normalWS = input.normalWS;
        #endif

        float2 uvDistorted = input.uv;
        #if SSR_NORMALMAP
            uvDistorted += fmod(_Time.xx * DISTORTION_SPEED, 10000);
        #endif

        #if SSR_SCREEN_SPACE_NORMALS
            float2 ssNormalsUV = input.scrPos.xy;
            normalWS = SampleSceneNormals(ssNormalsUV);
            if (_HasTransparentNormals > 0.5) {
                float3 transparentNormalWS = SAMPLE_TEXTURE2D_X_LOD(_TransparentNormalsRT, sampler_PointClamp, ssNormalsUV, 0).xyz;
                if (dot(transparentNormalWS, transparentNormalWS) > 0.25) {
                    normalWS = transparentNormalWS;
                }
            }
            float3 normalVS = TransformWorldToViewDir(normalWS);
            normalVS.z *= -1;
        #elif SSR_NORMALMAP
            float4 packedNormal = SAMPLE_TEXTURE2D(_BumpMap, sampler_LinearRepeat, uvDistorted);
            float3 normalTS = UnpackNormalScale(packedNormal, _BumpScale);
            half3 viewDirWS = half3(input.normal.w, input.tangent.w, input.bitangent.w);
            normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangent.xyz, input.bitangent.xyz, input.normal.xyz));
            float3 normalVS = TransformWorldToViewDir(normalWS);
            normalVS.z *= -1;
        #else
            float3 normalVS = input.normal;
        #endif

        float occlusion = 1.0;
        #if SSR_OCCLUSIONMAP
            occlusion = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_LinearRepeat, uvDistorted).r;
            occlusion = lerp(1.0, occlusion, _OcclusionStrength);
        #endif


       collision = -1;

       #if SSR_METALLIC_WORKFLOW
            #if SSR_SMOOTHNESSMAP
                float reflectivity = REFLECTIVITY * SAMPLE_TEXTURE2D(_SmoothnessMap, sampler_PointRepeat, input.uv).a;
            #else
                float reflectivity = REFLECTIVITY;
            #endif
            reflectivity = SAMPLE_TEXTURE2D_LOD(_MetallicGradientTex, sampler_LinearClamp, float2(reflectivity, 0), 0).r;
            float roughness = SAMPLE_TEXTURE2D_LOD(_SmoothnessGradientTex, sampler_LinearClamp, float2(1.0 - SMOOTHNESS, 0), 0).r;
            float4 reflection = SSR_Pass(input.scrPos.xy, normalVS, input.positionVS, roughness, reflectivity);
        #else
            #if SSR_SMOOTHNESSMAP
                float smoothness = SMOOTHNESS * SAMPLE_TEXTURE2D(_SmoothnessMap, sampler_PointRepeat, input.uv).a;
            #else
                float smoothness = SMOOTHNESS;
            #endif
            float roughness = 1.0 - max(0, smoothness - REFLECTIONS_THRESHOLD);
            float4 reflection = SSR_Pass(input.scrPos.xy, normalVS, input.positionVS, roughness);
        #endif

        #if SSR_SKYBOX
        if (collision < 0) {
            float3 viewDirWS = normalize(input.viewDirWS);
            float3 reflDir = reflect(-viewDirWS, normalWS);
            float sm = 1.0 - roughness;
            sm *= SKYBOX_MULTIPLIER;
            #if SSR_METALLIC_WORKFLOW
                reflection.xyz = reflDir * reflectivity * SKYBOX_MULTIPLIER;
            #else
                reflection.xyz = reflDir;
            #endif
            reflection.w = -sm;
        }
        #endif

        #if SSR_OCCLUSIONMAP
            reflection.w *= occlusion;
        #endif

        // attenuates far from camera
        reflection.w *= saturate ( 1.0 - (input.positionVS.z - FAR_CAMERA_ATTENUATION_START) / FAR_CAMERA_ATTENUATION_RANGE );

        return reflection;
    }


#endif // SSR_SURF_FX
