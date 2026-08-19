#ifndef SSR_SOLVE
#define SSR_SOLVE

	// Copyright 2021 Kronnect - All Rights Reserved.
    TEXTURE2D_X(_MainTex);
	float4 _MainTex_TexelSize;
    TEXTURE2D_X(_RayCastRT);
    float4 _RayCastRT_TexelSize;
    TEXTURE2D_X(_ReflectionsOpaqueRT);

    float4 _MaterialData;
    #define FRESNEL _MaterialData.y
    #define FUZZYNESS _MaterialData.z

    float4 _SSRSettings2;
    #define REFLECTIONS_MULTIPLIER _SSRSettings2.z

    float4 _SSRSettings7;
    #define VIGNETTE_RADIAL _SSRSettings7.w

    float4 _SSRSettings4;
    #define REFLECTIONS_MIN_INTENSITY _SSRSettings4.y
    #define REFLECTIONS_MAX_INTENSITY _SSRSettings4.z

    float4 _SSRSettings5;
    #define SKYBOX_INTENSITY _SSRSettings5.z
    #define OPAQUE_REFLECTIONS_BLENDING _SSRSettings5.w

    samplerCUBE _SkyboxCubemap;
    float3 _CameraViewDir;
    #define CAMERA_VIEW_DIR _CameraViewDir

    float4 _SSRBlurStrength;
    #define VIGNETTE_SIZE _SSRBlurStrength.z
    #define VIGNETTE_POWER _SSRBlurStrength.w

	struct AttributesFS {
		float4 positionHCS : POSITION;
		float2 uv          : TEXCOORD0;
		UNITY_VERTEX_INPUT_INSTANCE_ID
	};

 	struct VaryingsSSR {
    	float4 positionCS : SV_POSITION;
    	float2 uv  : TEXCOORD0;
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
    	return output;
	}

	half4 FragResolve (VaryingsSSR i) : SV_Target { 

        UNITY_SETUP_INSTANCE_ID(i);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        i.uv = SSRStereoTransformScreenSpaceTex(i.uv);

        half4 reflData = SAMPLE_TEXTURE2D_X(_RayCastRT, sampler_PointClamp, i.uv);
        if (reflData.w == 0) return 0;

        // anti-fireflies / despeckle
        // Commented out since this dims reflections at edges including contact points between surfaces and ground producing a visible gap
        // float2 texelSize = _RayCastRT_TexelSize.xy;
        // half4 reflData1 = SAMPLE_TEXTURE2D_X(_RayCastRT, sampler_PointClamp, i.uv + texelSize.xy);
        // half4 reflData2 = SAMPLE_TEXTURE2D_X(_RayCastRT, sampler_PointClamp, i.uv - texelSize.xy);
        // half4 reflData3 = SAMPLE_TEXTURE2D_X(_RayCastRT, sampler_PointClamp, i.uv + float2(texelSize.x, -texelSize.y));
        // half4 reflData4 = SAMPLE_TEXTURE2D_X(_RayCastRT, sampler_PointClamp, i.uv - float2(texelSize.x, -texelSize.y));
	    // half w0 = 1.0 / (reflData.w + 1.0);
	    // half w1 = 1.0 / (reflData1.w + 1.0);
	    // half w2 = 1.0 / (reflData2.w + 1.0);
	    // half w3 = 1.0 / (reflData3.w + 1.0);
	    // half w4 = 1.0 / (reflData4.w + 1.0);
	    // half dd  = 1.0 / (w0 + w1 + w2 + w3 + w4);
	    // reflData.w = (reflData.w * w0 + reflData1.w * w1 + reflData2.w * w2 + reflData3.w * w3 + reflData4.w * w4) * dd;        

        half4 reflection;
        #if SSR_SKYBOX
            if (reflData.w < 0) {
                half smoothness = -reflData.w;
                #if SSR_METALLIC_WORKFLOW
                    float reflectivity = length(reflData.xyz);
                    if (reflectivity <= 0) return 0;
                    float3 reflDir = reflData.xyz / reflectivity;
                    float reflectionIntensity = reflectivity * SKYBOX_INTENSITY;
                #else
                    float3 reflDir = reflData.xyz;
                    float reflectionIntensity = smoothness * SKYBOX_INTENSITY;
                #endif
                reflection = texCUBE(_SkyboxCubemap, reflDir);
                float fresnel = 1.0 - FRESNEL * (1.0 - abs(dot(reflDir, CAMERA_VIEW_DIR)));
                float reflectionAmount = reflectionIntensity * fresnel; 
                float blurAmount = (1 - smoothness) * 5.0;
                reflData.xy = float2(0.5, 0.5);
                reflData.z = blurAmount + 0.001;
                reflData.w = reflectionAmount;
            } else
        #endif
            
        reflection = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, reflData.xy);
        reflection.rgb = min(reflection.rgb, 8.0); // stop NAN pixels

        #if SSR_METALLIC_WORKFLOW
            half reflectionIntensity = reflData.a * REFLECTIONS_MULTIPLIER;
        #else
            half reflectionIntensity = clamp(reflData.a * REFLECTIONS_MULTIPLIER, REFLECTIONS_MIN_INTENSITY, REFLECTIONS_MAX_INTENSITY);
        #endif

        reflection.rgb *= reflectionIntensity;
        reflection.rgb = min(reflection.rgb, 1.2); // clamp max brightness

        // conserve energy
        half4 pixel = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv);
        reflection.rgb -= min(0.5, pixel.rgb * reflectionIntensity);

        // keep blur factor in alpha channel
        reflection.a = reflData.z;

        // vignette attenuation
        half2 reflDst = (reflData.xy - 0.5) * 2.0;
        half vd;
        if (VIGNETTE_RADIAL > 0.5) {
            vd = dot2(reflDst);
        } else {
            half2 edgeDist = reflDst * reflDst;
            vd = max(edgeDist.x, edgeDist.y);
        }
        half vignette = saturate(VIGNETTE_SIZE - vd);
        reflection *= vignette;

        #if SSR_BLEND_REFLECTIONS
            half4 reflOpaque = SAMPLE_TEXTURE2D_X(_ReflectionsOpaqueRT, sampler_LinearClamp, i.uv);
            half lumaReflection = Luminance(reflection.rgb);    
            reflection = lerp(reflOpaque, reflection, saturate(lumaReflection * OPAQUE_REFLECTIONS_BLENDING));
        #endif

        return reflection;
	}



#endif // SSR_SOLVE