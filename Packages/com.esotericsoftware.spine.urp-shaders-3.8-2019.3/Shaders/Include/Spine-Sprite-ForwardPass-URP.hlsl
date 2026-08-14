#ifndef VERTEX_LIT_FORWARD_PASS_URP_INCLUDED
#define VERTEX_LIT_FORWARD_PASS_URP_INCLUDED

#include "Include/Spine-Sprite-Common-URP.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "SpineCoreShaders/SpriteLighting.cginc"

#if defined(_RIM_LIGHTING) || defined(_ADDITIONAL_LIGHTS) || defined(MAIN_LIGHT_CALCULATE_SHADOWS)
	#define NEEDS_POSITION_WS
#endif

////////////////////////////////////////
// Vertex output struct
//
struct VertexOutputLWRP
{
	float4 pos : SV_POSITION;
	fixed4 vertexColor : COLOR;
	float3 texcoord : TEXCOORD0;

	half4 fogFactorAndVertexLight : TEXCOORD1;
	half3 viewDirectionWS : TEXCOORD2;

	DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 3);

#if defined(_NORMALMAP)
	half4 normalWorld : TEXCOORD4;
	half4 tangentWorld : TEXCOORD5;
	half4 binormalWorld : TEXCOORD6;
#else
	half3 normalWorld : TEXCOORD4;
#endif
#if (defined(_MAIN_LIGHT_SHADOWS) || defined(MAIN_LIGHT_CALCULATE_SHADOWS)) && !defined(_RECEIVE_SHADOWS_OFF)
	float4 shadowCoord : TEXCOORD7;
#endif
#if defined(NEEDS_POSITION_WS)
	float4 positionWS : TEXCOORD8;
#endif
	UNITY_VERTEX_OUTPUT_STEREO
};

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////
half3 LightweightLightVertexSimplified(float3 positionWS, half3 normalWS) {
#ifdef _MAIN_LIGHT_VERTEX
	Light mainLight = GetMainLight();
	half3 attenuatedLightColor = mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation);
	half3 diffuseLightColor = LightingLambert(attenuatedLightColor, mainLight.direction, normalWS);
#else
	half3 diffuseLightColor = half3(0, 0, 0);
#endif

#ifdef _ADDITIONAL_LIGHTS_VERTEX
	int pixelLightCount = GetAdditionalLightsCount();
	for (int i = 0; i < pixelLightCount; ++i)
	{
		Light light = GetAdditionalLight(i, positionWS);
		half3 attenuatedLightColor = light.color * (light.distanceAttenuation * light.shadowAttenuation);
		diffuseLightColor += LightingLambert(attenuatedLightColor, light.direction, normalWS);
	}
#endif // _ADDITIONAL_LIGHTS_VERTEX
	return diffuseLightColor;
}

#ifdef _DIFFUSE_RAMP
half3 LightingLambertRamped(half3 lightColor, float attenuation, half3 lightDir, half3 normal)
{
	half angleDot = max(0, dot(lightDir, normal));
	return calculateRampedDiffuse(lightColor, attenuation, angleDot);
}
#endif

#if defined(SPECULAR)

// URP 17 replaced the legacy inputComp type with InputData while retaining the lighting fields used by Spine 3.8.
half4 LightweightFragmentPBRSimplified(InputData inputComp, half4 texAlbedoAlpha, half metallic, half3 specular,
	half smoothness, half3 emission, half4 vertexColor)
{
	half4 albedo = texAlbedoAlpha * vertexColor;

	BRDFData brdfData;
	half ignoredAlpha = 1; // ignore alpha, otherwise
	InitializeBRDFData(albedo.rgb, metallic, specular, smoothness, ignoredAlpha, brdfData);
	brdfData.specular *= albedo.a;

#ifndef _MAIN_LIGHT_VERTEX
#if (defined(_MAIN_LIGHT_SHADOWS) || defined(MAIN_LIGHT_CALCULATE_SHADOWS)) && !defined(_RECEIVE_SHADOWS_OFF)
	Light mainLight = GetMainLight(inputComp.shadowCoord);
#else
	Light mainLight = GetMainLight();
#endif

	half3 finalColor = inputComp.bakedGI;
	finalColor += LightingPhysicallyBased(brdfData, mainLight, inputComp.normalWS, inputComp.viewDirectionWS);
#else // _MAIN_LIGHT_VERTEX
	half3 finalColor = inputComp.bakedGI;
#endif // _MAIN_LIGHT_VERTEX

#ifdef _ADDITIONAL_LIGHTS
	int pixelLightCount = GetAdditionalLightsCount();
	for (int i = 0; i < pixelLightCount; ++i)
	{
		Light light = GetAdditionalLight(i, inputComp.positionWS);
		finalColor += LightingPhysicallyBased(brdfData, light, inputComp.normalWS, inputComp.viewDirectionWS);
	}
#endif

#ifdef _ADDITIONAL_LIGHTS_VERTEX
	finalColor += inputComp.vertexLighting * brdfData.diffuse;
#endif
	finalColor += emission;
	return prepareLitPixelForOutput(half4(finalColor, albedo.a), vertexColor);
}

#else // !SPECULAR

// Use the current URP lighting input type so non-specular Sprite variants compile on Unity 6.
half4 LightweightFragmentBlinnPhongSimplified(InputData inputComp, half4 texDiffuseAlpha, half3 emission, half4 vertexColor)
{
	half4 diffuse = texDiffuseAlpha * vertexColor;

#ifndef _MAIN_LIGHT_VERTEX
#if (defined(_MAIN_LIGHT_SHADOWS) || defined(MAIN_LIGHT_CALCULATE_SHADOWS)) && !defined(_RECEIVE_SHADOWS_OFF)
	Light mainLight = GetMainLight(inputComp.shadowCoord);
#else
	Light mainLight = GetMainLight();
#endif
	half3 diffuseLighting = inputComp.bakedGI;

	half3 attenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
	half3 attenuatedLightColor = mainLight.color * attenuation;
#ifndef _DIFFUSE_RAMP
	diffuseLighting += LightingLambert(attenuatedLightColor, mainLight.direction, inputComp.normalWS);
#else
	diffuseLighting += LightingLambertRamped(mainLight.color, attenuation, mainLight.direction, inputComp.normalWS);
#endif
#else // _MAIN_LIGHT_VERTEX
	half3 diffuseLighting = inputComp.bakedGI;
#endif // _MAIN_LIGHT_VERTEX

#ifdef _ADDITIONAL_LIGHTS
	int pixelLightCount = GetAdditionalLightsCount();
	for (int i = 0; i < pixelLightCount; ++i)
	{
		Light light = GetAdditionalLight(i, inputComp.positionWS);
		half3 attenuation = (light.distanceAttenuation * light.shadowAttenuation);
		half3 attenuatedLightColor = light.color * attenuation;
#ifndef _DIFFUSE_RAMP
		diffuseLighting += LightingLambert(attenuatedLightColor, light.direction, inputComp.normalWS);
#else
		diffuseLighting += LightingLambertRamped(light.color, attenuation, light.direction, inputComp.normalWS);
#endif
	}
#endif
#ifdef _ADDITIONAL_LIGHTS_VERTEX
	diffuseLighting += inputComp.vertexLighting;
#endif
	diffuseLighting += emission;
	//half3 finalColor = diffuseLighting * diffuse + emission;
	half3 finalColor = diffuseLighting * diffuse.rgb;
	return prepareLitPixelForOutput(half4(finalColor, diffuse.a), vertexColor);
}
#endif // SPECULAR

VertexOutputLWRP ForwardPassVertexSprite(VertexInput input)
{
	VertexOutputLWRP output = (VertexOutputLWRP)0;

	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	output.pos = calculateLocalPos(input.vertex);
	output.vertexColor = calculateVertexColor(input.color);
	output.texcoord = float3(calculateTextureCoord(input.texcoord), 0);

	float3 positionWS = TransformObjectToWorld(input.vertex.xyz);

	float backFaceSign = 1;
#if defined(FIXED_NORMALS_BACKFACE_RENDERING)
	backFaceSign = calculateBackfacingSign(positionWS.xyz);
#endif
	output.viewDirectionWS = GetCameraPositionWS() - positionWS;
#if defined(NEEDS_POSITION_WS)
	output.positionWS = float4(positionWS, 1);
#endif

#if defined(PER_PIXEL_LIGHTING)

	half3 normalWS = calculateSpriteWorldNormal(input, -backFaceSign);
	output.normalWorld.xyz = normalWS;

#if defined(_NORMALMAP)
	output.tangentWorld.xyz = calculateWorldTangent(input.tangent);
	output.binormalWorld.xyz = calculateSpriteWorldBinormal(input, output.normalWorld.xyz, output.tangentWorld.xyz, backFaceSign);
#endif

#else // !PER_PIXEL_LIGHTING
	half3 fixedNormal = half3(0, 0, -1);
	half3 normalWS = normalize(mul((float3x3)unity_ObjectToWorld, fixedNormal));

#endif // !PER_PIXEL_LIGHTING
	output.fogFactorAndVertexLight.yzw = LightweightLightVertexSimplified(positionWS, normalWS);


#if (defined(_MAIN_LIGHT_SHADOWS) || defined(MAIN_LIGHT_CALCULATE_SHADOWS)) && !defined(_RECEIVE_SHADOWS_OFF)
	VertexPositionInputs vertexInput;
	vertexInput.positionWS = positionWS;
	vertexInput.positionCS = output.pos;
	output.shadowCoord = GetShadowCoord(vertexInput);
#endif

#if defined(_FOG)
	half fogFactor = ComputeFogFactor(output.pos.z);
	output.fogFactorAndVertexLight.x = fogFactor;
#endif

	OUTPUT_SH(normalWS.xyz, output.vertexSH);
	return output;
}

half4 ForwardPassFragmentSprite(VertexOutputLWRP input) : SV_Target
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	fixed4 texureColor = calculateTexturePixel(input.texcoord.xy);
	RETURN_UNLIT_IF_ADDITIVE_SLOT(texureColor, input.vertexColor) // shall be called before ALPHA_CLIP
	ALPHA_CLIP(texureColor, input.vertexColor)

	// Fill the current URP InputData struct with the values calculated by the Spine vertex and normal paths.
	InputData inputComp;
#if !defined(_RECEIVE_SHADOWS_OFF)
	#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
		inputComp.shadowCoord = input.shadowCoord;
	#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
		inputComp.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
	#elif defined(_MAIN_LIGHT_SHADOWS)
		inputComp.shadowCoord = input.shadowCoord;
	#else
		inputComp.shadowCoord = float4(0, 0, 0, 0);
	#endif
#endif

	inputComp.viewDirectionWS = input.viewDirectionWS;
	inputComp.vertexLighting = input.fogFactorAndVertexLight.yzw;

#if defined(PER_PIXEL_LIGHTING)
	#if defined(_NORMALMAP)
	half3 normalWS = calculateNormalFromBumpMap(input.texcoord.xy, input.tangentWorld.xyz, input.binormalWorld.xyz, input.normalWorld.xyz);
	#else
	half3 normalWS = input.normalWorld.xyz;
	#endif
#else // !PER_PIXEL_LIGHTING
	half3 fixedNormal = half3(0, 0, -1);
	half3 normalWS = normalize(mul((float3x3)unity_ObjectToWorld, fixedNormal));
#endif // !PER_PIXEL_LIGHTING

	inputComp.normalWS = normalWS;
	inputComp.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, inputComp.normalWS);
#if defined(_RIM_LIGHTING) || defined(_ADDITIONAL_LIGHTS)
	inputComp.positionWS = input.positionWS.rgb;
#endif

#if defined(SPECULAR)
	half2 metallicGloss = getMetallicGloss(input.texcoord.xy);
	half metallic = metallicGloss.x;
	half smoothness = metallicGloss.y; // this is 1 minus the square root of real roughness m.

	half3 specular = half3(0, 0, 0);
	half4 emission = half4(0, 0, 0, 1);
	APPLY_EMISSION_SPECULAR(emission, input.texcoord.xy)
	half4 pixel = LightweightFragmentPBRSimplified(inputComp, texureColor, metallic, specular, smoothness, emission.rgb, input.vertexColor);
#else
	half3 emission = half3(0, 0, 0);
	APPLY_EMISSION(emission, input.texcoord.xy)
	half4 pixel = LightweightFragmentBlinnPhongSimplified(inputComp, texureColor, emission, input.vertexColor);
#endif

#if defined(_RIM_LIGHTING)
	pixel.rgb = applyRimLighting(input.positionWS.xyz, normalWS, pixel);
#endif

	COLORISE(pixel)
	APPLY_FOG_LWRP(pixel, input.fogFactorAndVertexLight.x)

	return pixel;
}

#endif
