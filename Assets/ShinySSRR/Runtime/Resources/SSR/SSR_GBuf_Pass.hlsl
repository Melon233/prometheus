#ifndef SSR_GBUF_PASS
#define SSR_GBUF_PASS

	// Copyright 2021 Kronnect - All Rights Reserved.
    TEXTURE2D(_NoiseTex);
    float4 _NoiseTex_TexelSize;

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

    float4 _SSRSettings3;
    #define INPUT_SIZE _SSRSettings3.xy
    #define GOLDEN_RATIO_ACUM _SSRSettings3.z

    float4 _SSRSettings7;
    #define FAR_CAMERA_ATTENUATION_START _SSRSettings7.x
    #define FAR_CAMERA_ATTENUATION_RANGE _SSRSettings7.y

    float4x4 _WorldToViewDir;
    float4x4 _ViewToWorldDir;

    float3 _SSRBoundsMin, _SSRBoundsSize;
    #define BOUNDS_MIN _SSRBoundsMin
    #define BOUNDS_SIZE _SSRBoundsSize

    float _TransparentSmoothness;
    
    TEXTURE2D_X(_GBuffer0);
    TEXTURE2D_X(_GBuffer1);
    TEXTURE2D_X(_GBuffer2);
    TEXTURE2D_X(_TransparentNormalsRT);
    TEXTURE2D_X(_SmoothnessMetallicRT);
    TEXTURE2D(_MetallicGradientTex);
    TEXTURE2D(_SmoothnessGradientTex);

	struct AttributesFS {
		float4 positionHCS : POSITION;
		float4 uv          : TEXCOORD0;
		UNITY_VERTEX_INPUT_INSTANCE_ID
	};

 	struct VaryingsSSR {
    	float4 positionCS : SV_POSITION;
    	float4 uv  : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
	};


	VaryingsSSR VertSSR(AttributesFS input) {
	    VaryingsSSR output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = float4(input.positionHCS.xyz, 1.0);

		#if UNITY_UV_STARTS_AT_TOP
		    output.positionCS.y *= -1;
		#endif

        output.uv = input.uv;
        float4 projPos = output.positionCS * 0.5;
        projPos.xy = projPos.xy + projPos.w;
        output.uv.zw = projPos.xy;
    	return output;
	}

    float collision;

#if SSR_METALLIC_WORKFLOW
	float4 SSR_Pass(float2 uv, float3 normalVS, float3 rayStart, float3 viewDirVS, float roughness, float reflectivity) {
#else
	float4 SSR_Pass(float2 uv, float3 normalVS, float3 rayStart, float3 viewDirVS, float roughness) {
#endif

        float3 rayDir = reflect( viewDirVS, normalVS );
        float pz = rayStart.z;

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
        float sceneDepth, depthDiff;
        float prevDepthDiff = 0;

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
            if (any(p.xy < 0 | p.xy >= 1)) return 0;
            
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


	float4 FragSSR (VaryingsSSR input) : SV_Target {

        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float depth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, input.uv.xy).r;
        #if UNITY_REVERSED_Z
            depth = 1.0 - depth;
        #endif
        if (depth >= 1.0) return float4(0,0,0,0);

        depth = 2.0 * depth - 1.0;
        float2 zw = SSRStereoTransformScreenSpaceTex(input.uv.zw);
        #if defined(USING_STEREO_MATRICES)
            float3 positionVS = ComputeViewSpacePosition(zw, depth, unity_StereoCameraInvProjection[unity_StereoEyeIndex]);
        #else
            float3 positionVS = ComputeViewSpacePosition(zw, depth, unity_CameraInvProjection);
        #endif

        if (positionVS.z > FAR_CAMERA_ATTENUATION_START + FAR_CAMERA_ATTENUATION_RANGE) {
            return 0;
        }
        
        #if SSR_LIMIT_BOUNDS
            float3 positionWS = mul(_ViewToWorldDir , float4(positionVS.x, positionVS.y, -positionVS.z, 1.0)).xyz;
            float3 inside = (positionWS - BOUNDS_MIN) / BOUNDS_SIZE;
            if (any(floor(inside)!=0)) return 0;
        #endif

        float2 uv = SSRStereoTransformScreenSpaceTex(input.uv.xy);

        float3 normalWS;
        float occlusion;

        #if SSR_CUSTOM_SMOOTHNESS_METALLIC_PASS
            normalWS = SampleSceneNormals(uv);
            occlusion = 1.0;
        #else
            float4 normals = SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, sampler_PointClamp, uv, 0);
            #if defined(_GBUFFER_NORMALS_OCT)
                half2 remappedOctNormalWS = Unpack888ToFloat2(normals.xyz); // values between [ 0,  1]
                half2 octNormalWS = remappedOctNormalWS.xy * 2.0h - 1.0h;    // values between [-1, +1]
                normalWS = UnpackNormalOctQuadEncode(octNormalWS);
            #else
                normalWS = normals.xyz;
            #endif

            float4 gbuffer1 = SAMPLE_TEXTURE2D_X(_GBuffer1, sampler_PointClamp, uv);
            occlusion = gbuffer1.a;

            #if SSR_TRANSPARENT_NORMAL_PREPASS
                float3 transparentNormalWS = SAMPLE_TEXTURE2D_X(_TransparentNormalsRT, sampler_PointClamp, uv).xyz;
                if (dot(transparentNormalWS, transparentNormalWS) > 1e-6) {
                    normalWS = transparentNormalWS;
                    normals.w = _TransparentSmoothness;
                    occlusion = 1.0;
                }
            #endif
        #endif

        float3 normalVS = mul((float3x3)_WorldToViewDir, normalWS);
        normalVS.z *= -1.0;

        collision = -1;
        #if SSR_ORTHO_SUPPORT
            float3 viewDirVS = float3(0, 0, 1);
        #else
            float3 viewDirVS = normalize(positionVS);
        #endif

        // get physically attributes
        #if SSR_METALLIC_WORKFLOW
            #if SSR_CUSTOM_SMOOTHNESS_METALLIC_PASS
                float2 smoothnessMetallic = SAMPLE_TEXTURE2D_X(_SmoothnessMetallicRT, sampler_PointClamp, uv).rg;
                float smoothness = smoothnessMetallic.r;
                float reflectivity = smoothnessMetallic.g;
            #else
                float4 gbuffer0 = SAMPLE_TEXTURE2D_X(_GBuffer0, sampler_PointClamp, uv);
                float reflectivity, smoothness = normals.w;
                uint materialFlags = UnpackMaterialFlags(gbuffer0.a);
                if (materialFlags & kMaterialFlagSpecularSetup) {
                    reflectivity = max(gbuffer1.r, max(gbuffer1.g, gbuffer1.b));
                } else {
                    reflectivity = gbuffer1.r;
                }
                if (reflectivity <= 0) return 0;
            #endif

            reflectivity = SAMPLE_TEXTURE2D_LOD(_MetallicGradientTex, sampler_LinearClamp, float2(reflectivity, 0), 0).r;
            if (reflectivity <= 0) return 0;

            float roughness = SAMPLE_TEXTURE2D_LOD(_SmoothnessGradientTex, sampler_LinearClamp, float2(1.0 - smoothness, 0), 0).r;
       		float4 reflection = SSR_Pass(input.uv.xy, normalVS, positionVS, viewDirVS, roughness, reflectivity);
        #else
            #if SSR_CUSTOM_SMOOTHNESS_METALLIC_PASS
                float smoothness = SAMPLE_TEXTURE2D_X(_SmoothnessMetallicRT, sampler_PointClamp, uv).r;
            #else
                float smoothness = normals.w;
            #endif
            float roughness = 1.0 - max(0, smoothness - REFLECTIONS_THRESHOLD);
            if (roughness >= 1.0) return 0;
   		    float4 reflection = SSR_Pass(input.uv.xy, normalVS, positionVS, viewDirVS, roughness);
        #endif
        
        #if SSR_SKYBOX
        if (collision < 0) {
            viewDirVS.z *= -1;
            float3 viewDirWS = mul((float3x3)_ViewToWorldDir, viewDirVS);
            float3 reflDir = reflect(viewDirWS, normalWS);
            #if SSR_METALLIC_WORKFLOW
                reflection.xyz = reflDir * reflectivity;
            #else
                reflection.xyz = reflDir;
            #endif
            float sm = 1.0 - roughness;
            reflection.w = -sm;
        }
        #endif

        reflection.w *= occlusion;

        // attenuates far from camera
        reflection.w *= saturate ( 1.0 - (positionVS.z - FAR_CAMERA_ATTENUATION_START) / FAR_CAMERA_ATTENUATION_RANGE );

        return reflection;
	}


#endif // SSR_GBUF_PASS