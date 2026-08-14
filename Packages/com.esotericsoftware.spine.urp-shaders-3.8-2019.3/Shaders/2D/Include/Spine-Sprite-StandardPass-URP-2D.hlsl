#ifndef SPRITE_STANDARD_PASS_URP_INCLUDED
#define SPRITE_STANDARD_PASS_URP_INCLUDED

// URP 17 centralizes shape-light declarations and the current 2D surface/input structures in these includes.
#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"

#include "../Include/SpineCoreShaders/ShaderShared.cginc"
#include "../Include/SpineCoreShaders/SpriteLighting.cginc"

TEXTURE2D(_MaskTex);
SAMPLER(sampler_MaskTex);

struct VertexOutputSpriteURP2D
{
	float4 pos : SV_POSITION;
	fixed4 vertexColor : COLOR;
	float3 texcoord : TEXCOORD0;
	float2 lightingUV : TEXCOORD1;

	half3 viewDirectionWS : TEXCOORD2;

#if defined(_NORMALMAP)
	half4 normalWorld : TEXCOORD4;
	half4 tangentWorld : TEXCOORD5;
	half4 binormalWorld : TEXCOORD6;
#else
	half3 normalWorld : TEXCOORD4;
#endif
#if defined(_RIM_LIGHTING)
	float4 positionWS : TEXCOORD8;
#endif
};

VertexOutputSpriteURP2D CombinedShapeLightVertex(VertexInput input)
{
	VertexOutputSpriteURP2D output = (VertexOutputSpriteURP2D)0;

	UNITY_SETUP_INSTANCE_ID(input);

	output.pos = calculateLocalPos(input.vertex);
	float4 clipVertex = output.pos / output.pos.w;
	output.lightingUV = ComputeScreenPos(clipVertex).xy;

	output.vertexColor = calculateVertexColor(input.color);
	output.texcoord = float3(calculateTextureCoord(input.texcoord), 0);

	float3 positionWS = TransformObjectToWorld(input.vertex.xyz);

	float backFaceSign = 1;
#if defined(FIXED_NORMALS_BACKFACE_RENDERING)
	backFaceSign = calculateBackfacingSign(positionWS.xyz);
#endif
	output.viewDirectionWS = GetCameraPositionWS() - positionWS;

#if defined(_RIM_LIGHTING)
	output.positionWS = float4(positionWS, 1);
#endif
	half3 normalWS = calculateSpriteWorldNormal(input, -backFaceSign);
	output.normalWorld.xyz = normalWS;

#if defined(_RIM_LIGHTING)
	#if defined(_NORMALMAP)
	output.tangentWorld.xyz = calculateWorldTangent(input.tangent);
	output.binormalWorld.xyz = calculateSpriteWorldBinormal(input, output.normalWorld.xyz, output.tangentWorld.xyz, backFaceSign);
	#endif
#endif
	return output;
}

half4 CombinedShapeLightFragment(VertexOutputSpriteURP2D input) : SV_Target
{
	fixed4 texureColor = calculateTexturePixel(input.texcoord.xy);
	RETURN_UNLIT_IF_ADDITIVE_SLOT(texureColor, input.vertexColor) // shall be called before ALPHA_CLIP
	ALPHA_CLIP(texureColor, input.vertexColor)

	texureColor *= input.vertexColor;

	half4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.texcoord.xy);
	SurfaceData2D surfaceData;
	InputData2D inputData;
	InitializeSurfaceData(texureColor.rgb, texureColor.a, mask, surfaceData);
	InitializeInputData(input.texcoord.xy, input.lightingUV, inputData);
	half4 pixel = CombinedShapeLightShared(surfaceData, inputData);

#if defined(_RIM_LIGHTING)
	#if defined(_NORMALMAP)
	half3 normalWS = calculateNormalFromBumpMap(input.texcoord.xy, input.tangentWorld.xyz, input.binormalWorld.xyz, input.normalWorld.xyz);
	#else
	half3 normalWS = input.normalWorld.xyz;
	#endif

	pixel.rgb = applyRimLighting(input.positionWS.xyz, normalWS, pixel);
#endif

	APPLY_EMISSION(pixel.rgb, input.texcoord)
	pixel = prepareLitPixelForOutput(pixel, input.vertexColor);
	COLORISE(pixel)
	return pixel;
}

#endif
