// Made with Amplify Shader Editor v1.9.8.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Polyart/Dreamscape/Builtin/Grass"
{
	Properties
	{
		[Header(Base Maps)][Header(.)][SingleLineTexture]_MainTex("Foliage Texture", 2D) = "white" {}
		[SingleLineTexture]_VariationMask("Variation Mask", 2D) = "white" {}
		[Header(Base Parameters)]_VariationMapScale("Variation Map Scale", Float) = 15
		[HDR]_ColorTop("Color Top", Color) = (0,0,0,0)
		_ColorTopVariation("Color Top Variation", Color) = (0,0,0,0)
		_ColorBottom("Color Bottom", Color) = (0,0,0,0)
		_ColorBottomLevel("Color Bottom Level", Float) = 0
		_ColorBottomMaskFade("Color Bottom Mask Fade", Range( 0 , 1)) = 0
		_SpecularIntensity("Specular Intensity", Range( 0 , 1)) = 0.1
		_FoliageRoughness("Foliage Roughness", Range( 0 , 1)) = 0.1
		_MaskClip("Mask Clip", Range( 0 , 1)) = 0.5
		[Header(Wind)][Toggle(_USEGLOBALWINDSETTINGS_ON)] _UseGlobalWindSettings("Use Global Wind Settings?", Float) = 0
		[HideInInspector]_WindNoiseTexture("Wind Noise Texture", 2D) = "white" {}
		_LockPositionGradient("Lock Position Gradient", Range( 0 , 10)) = 2
		_WindNoise01Size("Wind Noise 01 Size", Range( 0 , 100)) = 0
		_WindNoise01Multiplier("Wind Noise 01 Multiplier", Range( -3 , 3)) = 1
		_WindNoise02Size("Wind Noise 02 Size", Range( 0 , 100)) = 0
		_WindNoise02Multiplier("Wind Noise 02 Multiplier", Range( -3 , 3)) = 1
		[Toggle(_USEVERTEXCOLOR_ON)] _UseVertexColor("Use Vertex Color?", Float) = 0
		_VertexColorOffset("Vertex Color Offset", Float) = 0
		_VertexColorGradient("Vertex Color Gradient", Float) = 1
		[Header(Dithering)][Toggle(_USEDITHERING_ON)] _UseDithering("Use Dithering?", Float) = 0
		[Toggle(_USEGLOBALSETTING_ON)] _UseGlobalSetting("Use Global Setting?", Float) = 0
		_Blend("Blend", Range( 0 , 1)) = 0
		_DitherBottomLevel("Dither Bottom Level", Range( -10 , 10)) = 0
		_DitherFade("Dither Fade", Range( 0 , 10)) = 0
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Opaque"  "Queue" = "AlphaTest+0" "IgnoreProjector" = "True" }
		Cull Off
		AlphaToMask On
		CGINCLUDE
		#include "UnityShaderVariables.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 4.0
		#pragma multi_compile_instancing
		#pragma shader_feature_local _USEGLOBALWINDSETTINGS_ON
		#pragma shader_feature_local _USEVERTEXCOLOR_ON
		#pragma shader_feature _USEDITHERING_ON
		#pragma shader_feature_local _USEGLOBALSETTING_ON
		#define ASE_VERSION 19801
		struct Input
		{
			float3 worldPos;
			float2 uv_texcoord;
			float4 ase_positionOS4f;
			float4 screenPosition;
		};

		uniform sampler2D _WindNoiseTexture;
		uniform float _WindNoise01Size;
		uniform float WindNoise01;
		uniform float _WindNoise01Multiplier;
		uniform float WindNoise01Multiplier;
		uniform float _WindNoise02Size;
		uniform float WindNoise02;
		uniform float _WindNoise02Multiplier;
		uniform float WindNoise02Multiplier;
		uniform float _LockPositionGradient;
		uniform float _VertexColorOffset;
		uniform float _VertexColorGradient;
		uniform sampler2D _MainTex;
		uniform float4 _MainTex_ST;
		uniform float4 _ColorTop;
		uniform float4 _ColorTopVariation;
		uniform sampler2D _VariationMask;
		uniform float _VariationMapScale;
		uniform float4 _ColorBottom;
		uniform float _ColorBottomLevel;
		uniform float _ColorBottomMaskFade;
		uniform float _Blend;
		uniform float _SpecularIntensity;
		uniform float _FoliageRoughness;
		uniform float _DitherBottomLevel;
		uniform float DitherBottomLevel;
		uniform float _DitherFade;
		uniform float DitherFade;
		uniform float _MaskClip;


		struct Gradient
		{
			int type;
			int colorsLength;
			int alphasLength;
			float4 colors[8];
			float2 alphas[8];
		};


		Gradient NewGradient(int type, int colorsLength, int alphasLength, 
		float4 colors0, float4 colors1, float4 colors2, float4 colors3, float4 colors4, float4 colors5, float4 colors6, float4 colors7,
		float2 alphas0, float2 alphas1, float2 alphas2, float2 alphas3, float2 alphas4, float2 alphas5, float2 alphas6, float2 alphas7)
		{
			Gradient g;
			g.type = type;
			g.colorsLength = colorsLength;
			g.alphasLength = alphasLength;
			g.colors[ 0 ] = colors0;
			g.colors[ 1 ] = colors1;
			g.colors[ 2 ] = colors2;
			g.colors[ 3 ] = colors3;
			g.colors[ 4 ] = colors4;
			g.colors[ 5 ] = colors5;
			g.colors[ 6 ] = colors6;
			g.colors[ 7 ] = colors7;
			g.alphas[ 0 ] = alphas0;
			g.alphas[ 1 ] = alphas1;
			g.alphas[ 2 ] = alphas2;
			g.alphas[ 3 ] = alphas3;
			g.alphas[ 4 ] = alphas4;
			g.alphas[ 5 ] = alphas5;
			g.alphas[ 6 ] = alphas6;
			g.alphas[ 7 ] = alphas7;
			return g;
		}


		float4 SampleGradient( Gradient gradient, float time )
		{
			float3 color = gradient.colors[0].rgb;
			UNITY_UNROLL
			for (int c = 1; c < 8; c++)
			{
			float colorPos = saturate((time - gradient.colors[c-1].w) / ( 0.00001 + (gradient.colors[c].w - gradient.colors[c-1].w)) * step(c, (float)gradient.colorsLength-1));
			color = lerp(color, gradient.colors[c].rgb, lerp(colorPos, step(0.01, colorPos), gradient.type));
			}
			#ifndef UNITY_COLORSPACE_GAMMA
			color = half3(GammaToLinearSpaceExact(color.r), GammaToLinearSpaceExact(color.g), GammaToLinearSpaceExact(color.b));
			#endif
			float alpha = gradient.alphas[0].x;
			UNITY_UNROLL
			for (int a = 1; a < 8; a++)
			{
			float alphaPos = saturate((time - gradient.alphas[a-1].y) / ( 0.00001 + (gradient.alphas[a].y - gradient.alphas[a-1].y)) * step(a, (float)gradient.alphasLength-1));
			alpha = lerp(alpha, gradient.alphas[a].x, lerp(alphaPos, step(0.01, alphaPos), gradient.type));
			}
			return float4(color, alpha);
		}


		float4 ASEScreenPositionNormalizedToPixel( float4 screenPosNorm )
		{
			float4 screenPosPixel = screenPosNorm * float4( _ScreenParams.xy, 1, 1 );
			#if UNITY_UV_STARTS_AT_TOP
				screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x < 0 ) ? _ScreenParams.y - screenPosPixel.y : screenPosPixel.y );
			#else
				screenPosPixel.xy = float2( screenPosPixel.x, ( _ProjectionParams.x > 0 ) ? _ScreenParams.y - screenPosPixel.y : screenPosPixel.y );
			#endif
			return screenPosPixel;
		}


		inline float Dither4x4Bayer( int x, int y )
		{
			const float dither[ 16 ] = {
			     1,  9,  3, 11,
			    13,  5, 15,  7,
			     4, 12,  2, 10,
			    16,  8, 14,  6 };
			int r = y * 4 + x;
			return dither[ r ] / 16; // same # of instructions as pre-dividing due to compiler magic
		}


		void vertexDataFunc( inout appdata_full v, out Input o )
		{
			UNITY_INITIALIZE_OUTPUT( Input, o );
			float4 color128 = IsGammaSpace() ? float4(0,0,0,0) : float4(0,0,0,0);
			float3 ase_positionWS = mul( unity_ObjectToWorld, v.vertex );
			#ifdef _USEGLOBALWINDSETTINGS_ON
				float staticSwitch30_g13 = WindNoise01;
			#else
				float staticSwitch30_g13 = _WindNoise01Size;
			#endif
			#ifdef _USEGLOBALWINDSETTINGS_ON
				float staticSwitch31_g13 = WindNoise01Multiplier;
			#else
				float staticSwitch31_g13 = _WindNoise01Multiplier;
			#endif
			#ifdef _USEGLOBALWINDSETTINGS_ON
				float staticSwitch33_g13 = WindNoise02;
			#else
				float staticSwitch33_g13 = _WindNoise02Size;
			#endif
			#ifdef _USEGLOBALWINDSETTINGS_ON
				float staticSwitch38_g13 = WindNoise02Multiplier;
			#else
				float staticSwitch38_g13 = _WindNoise02Multiplier;
			#endif
			float4 lerpResult27_g13 = lerp( ( tex2Dlod( _WindNoiseTexture, float4( ( ( float2( 0,0.2 ) * _Time.y ) + ( (ase_positionWS).xz / staticSwitch30_g13 ) ), 0, 0.0) ) * staticSwitch31_g13 ) , ( tex2Dlod( _WindNoiseTexture, float4( ( ( float2( 0,0.1 ) * _Time.y ) + ( (ase_positionWS).xz / staticSwitch33_g13 ) ), 0, 0.0) ) * staticSwitch38_g13 ) , 0.5);
			Gradient gradient111 = NewGradient( 0, 2, 2, float4( 0, 0, 0, 0 ), float4( 1, 1, 1, 0.6294194 ), 0, 0, 0, 0, 0, 0, float2( 1, 0 ), float2( 1, 1 ), 0, 0, 0, 0, 0, 0 );
			float vVertexColor197 = pow( ( v.color.r + _VertexColorOffset ) , _VertexColorGradient );
			#ifdef _USEVERTEXCOLOR_ON
				float staticSwitch191 = vVertexColor197;
			#else
				float staticSwitch191 = pow( SampleGradient( gradient111, v.texcoord.xy.y ).r , _LockPositionGradient );
			#endif
			float4 lerpResult108 = lerp( color128 , lerpResult27_g13 , staticSwitch191);
			float4 vWind116 = lerpResult108;
			v.vertex.xyz += vWind116.rgb;
			v.vertex.w = 1;
			float4 ase_positionOS4f = v.vertex;
			o.ase_positionOS4f = ase_positionOS4f;
			float4 ase_positionSS = ComputeScreenPos( UnityObjectToClipPos( v.vertex ) );
			o.screenPosition = ase_positionSS;
		}

		void surf( Input i , inout SurfaceOutputStandardSpecular o )
		{
			float2 uv_MainTex = i.uv_texcoord * _MainTex_ST.xy + _MainTex_ST.zw;
			float4 tex2DNode22 = tex2D( _MainTex, uv_MainTex );
			float3 ase_positionWS = i.worldPos;
			float4 lerpResult132 = lerp( _ColorTop , _ColorTopVariation , tex2D( _VariationMask, (( ase_positionWS / _VariationMapScale )).xz ));
			float3 ase_positionOS = i.ase_positionOS4f.xyz;
			float4 lerpResult23 = lerp( lerpResult132 , _ColorBottom , saturate( ( ( ase_positionOS.y + ( _ColorBottomLevel * -1.0 ) ) * ( ( _ColorBottomMaskFade * -1.0 ) * 2 ) ) ));
			float4 blendOpSrc216 = tex2DNode22;
			float4 blendOpDest216 = lerpResult23;
			float4 lerpBlendMode216 = lerp(blendOpDest216,(( blendOpDest216 > 0.5 ) ? ( 1.0 - 2.0 * ( 1.0 - blendOpDest216 ) * ( 1.0 - blendOpSrc216 ) ) : ( 2.0 * blendOpDest216 * blendOpSrc216 ) ),_Blend);
			float4 vColor27 = ( saturate( lerpBlendMode216 ));
			o.Albedo = vColor27.rgb;
			float3 temp_cast_1 = (_SpecularIntensity).xxx;
			o.Specular = temp_cast_1;
			o.Smoothness = ( 1.0 - _FoliageRoughness );
			o.Alpha = 1;
			float vAlpha125 = tex2DNode22.a;
			float4 ase_positionSS = i.screenPosition;
			float4 ase_positionSSNorm = ase_positionSS / ase_positionSS.w;
			ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
			float4 ase_positionSS_Pixel = ASEScreenPositionNormalizedToPixel( ase_positionSSNorm );
			float dither8_g11 = Dither4x4Bayer( fmod( ase_positionSS_Pixel.x, 4 ), fmod( ase_positionSS_Pixel.y, 4 ) );
			#ifdef _USEGLOBALSETTING_ON
				float staticSwitch12_g11 = DitherBottomLevel;
			#else
				float staticSwitch12_g11 = _DitherBottomLevel;
			#endif
			#ifdef _USEGLOBALSETTING_ON
				float staticSwitch11_g11 = DitherFade;
			#else
				float staticSwitch11_g11 = _DitherFade;
			#endif
			dither8_g11 = step( dither8_g11, saturate( saturate( ( ( ase_positionOS.y + staticSwitch12_g11 ) * ( staticSwitch11_g11 * 2 ) ) ) * 1.00001 ) );
			float vTerrainDither181 = dither8_g11;
			#ifdef _USEDITHERING_ON
				float staticSwitch182 = ( vAlpha125 * vTerrainDither181 );
			#else
				float staticSwitch182 = vAlpha125;
			#endif
			clip( staticSwitch182 - _MaskClip );
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf StandardSpecular keepalpha fullforwardshadows vertex:vertexDataFunc 

		ENDCG
		Pass
		{
			Name "ShadowCaster"
			Tags{ "LightMode" = "ShadowCaster" }
			ZWrite On
			AlphaToMask Off
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 4.0
			#pragma multi_compile_shadowcaster
			#pragma multi_compile UNITY_PASS_SHADOWCASTER
			#pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
			#include "HLSLSupport.cginc"
			#if ( SHADER_API_D3D11 || SHADER_API_GLCORE || SHADER_API_GLES || SHADER_API_GLES3 || SHADER_API_METAL || SHADER_API_VULKAN )
				#define CAN_SKIP_VPOS
			#endif
			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "UnityPBSLighting.cginc"
			struct v2f
			{
				V2F_SHADOW_CASTER;
				float2 customPack1 : TEXCOORD1;
				float4 customPack2 : TEXCOORD2;
				float4 customPack3 : TEXCOORD3;
				float3 worldPos : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};
			v2f vert( appdata_full v )
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID( v );
				UNITY_INITIALIZE_OUTPUT( v2f, o );
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( o );
				UNITY_TRANSFER_INSTANCE_ID( v, o );
				Input customInputData;
				vertexDataFunc( v, customInputData );
				float3 worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				half3 worldNormal = UnityObjectToWorldNormal( v.normal );
				o.customPack1.xy = customInputData.uv_texcoord;
				o.customPack1.xy = v.texcoord;
				o.customPack2.xyzw = customInputData.ase_positionOS4f;
				o.customPack3.xyzw = customInputData.screenPosition;
				o.worldPos = worldPos;
				TRANSFER_SHADOW_CASTER_NORMALOFFSET( o )
				return o;
			}
			half4 frag( v2f IN
			#if !defined( CAN_SKIP_VPOS )
			, UNITY_VPOS_TYPE vpos : VPOS
			#endif
			) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( IN );
				Input surfIN;
				UNITY_INITIALIZE_OUTPUT( Input, surfIN );
				surfIN.uv_texcoord = IN.customPack1.xy;
				surfIN.ase_positionOS4f = IN.customPack2.xyzw;
				surfIN.screenPosition = IN.customPack3.xyzw;
				float3 worldPos = IN.worldPos;
				half3 worldViewDir = normalize( UnityWorldSpaceViewDir( worldPos ) );
				surfIN.worldPos = worldPos;
				SurfaceOutputStandardSpecular o;
				UNITY_INITIALIZE_OUTPUT( SurfaceOutputStandardSpecular, o )
				surf( surfIN, o );
				#if defined( CAN_SKIP_VPOS )
				float2 vpos = IN.pos;
				#endif
				SHADOW_CASTER_FRAGMENT( IN )
			}
			ENDCG
		}
	}
	Fallback "Diffuse"
}
/*ASEBEGIN
Version=19801
Node;AmplifyShaderEditor.CommentaryNode;31;-3952,-1564;Inherit;False;2609.802;1011.224;Comment;25;27;125;23;22;21;132;20;17;140;19;18;135;15;16;139;199;198;14;138;12;137;136;13;216;217;Color;1,1,1,1;0;0
Node;AmplifyShaderEditor.RangedFloatNode;13;-3808,-892;Inherit;False;Property;_ColorBottomLevel;Color Bottom Level;6;0;Create;True;0;0;0;False;0;False;0;-0.66;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.WorldPosInputsNode;136;-3888,-1228;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.RangedFloatNode;137;-3888,-1084;Inherit;False;Property;_VariationMapScale;Variation Map Scale;2;1;[Header];Create;True;1;Base Parameters;0;0;False;0;False;15;30;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;12;-3888,-812;Inherit;False;Property;_ColorBottomMaskFade;Color Bottom Mask Fade;7;0;Create;True;0;0;0;False;0;False;0;-1;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;196;-2967.053,580.0191;Inherit;False;1028;327.5676;Comment;6;197;194;195;193;192;190;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;184;-2964.891,-347.551;Inherit;False;1584.154;900.76;Comment;10;116;108;191;128;186;188;185;187;121;218;;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;121;-2892.79,172.7101;Inherit;False;912.9565;352;;5;113;114;112;111;110;Vertical Gradient;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;138;-3568,-1228;Inherit;False;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.PosVertexDataNode;14;-3632,-1036;Inherit;False;0;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.VertexColorNode;190;-2876.669,630.0191;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;193;-2917.053,805.5867;Inherit;False;Property;_VertexColorOffset;Vertex Color Offset;20;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;198;-3568,-892;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;-1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;199;-3568,-796;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;-1;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;167;-2192,-1856;Inherit;False;851.437;250.8007;Dithering;4;181;204;201;202;;1,1,1,1;0;0
Node;AmplifyShaderEditor.GradientNode;111;-2828.792,220.4162;Inherit;False;0;2;2;0,0,0,0;1,1,1,0.6294194;1,0;1,1;0;1;OBJECT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;110;-2842.791,298.4167;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ComponentMaskNode;139;-3424,-1244;Inherit;False;True;False;True;True;1;0;FLOAT3;0,0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;195;-2611.053,802.5867;Inherit;False;Property;_VertexColorGradient;Vertex Color Gradient;21;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;15;-3344,-924;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;192;-2571.053,655.5867;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScaleNode;16;-3376,-796;Inherit;False;2;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;201;-2176,-1712;Inherit;False;Property;_DitherFade;Dither Fade;27;0;Create;True;0;0;0;False;0;False;0;0;0;10;0;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode;194;-2377.053,653.5867;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.GradientSampleNode;112;-2589.793,223.4162;Inherit;True;2;0;OBJECT;;False;1;FLOAT;0;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;114;-2561.835,409.7108;Inherit;False;Property;_LockPositionGradient;Lock Position Gradient;14;0;Create;True;0;0;0;False;0;False;2;0.95;0;10;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;202;-2176,-1792;Inherit;False;Property;_DitherBottomLevel;Dither Bottom Level;26;0;Create;True;0;0;0;False;0;False;0;0;-10;10;0;1;FLOAT;0
Node;AmplifyShaderEditor.TexturePropertyNode;19;-2768,-812;Inherit;True;Property;_MainTex;Foliage Texture;0;2;[Header];[SingleLineTexture];Create;False;2;Base Maps;.;0;0;False;0;False;None;None;False;white;Auto;Texture2D;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;17;-3200,-940;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;140;-3216,-1260;Inherit;True;Property;_VariationMask;Variation Mask;1;1;[SingleLineTexture];Create;True;0;0;0;False;0;False;-1;ecd0578dd5d2cb54e90e298c6fbe1019;ecd0578dd5d2cb54e90e298c6fbe1019;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ColorNode;135;-2912,-1276;Inherit;False;Property;_ColorTopVariation;Color Top Variation;4;0;Create;True;0;0;0;False;0;False;0,0,0,0;0.4353558,0.5849056,0,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ColorNode;18;-2928,-1484;Inherit;False;Property;_ColorTop;Color Top;3;1;[HDR];Create;True;0;0;0;False;0;False;0,0,0,0;0.4353558,0.5849056,0,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RangedFloatNode;188;-2640.371,55.7639;Inherit;False;Property;_WindNoise02Multiplier;Wind Noise 02 Multiplier;18;0;Create;True;0;0;0;False;0;False;1;0;-3;3;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;185;-2641.421,-172.035;Inherit;False;Property;_WindNoise01Size;Wind Noise 01 Size;15;0;Create;True;0;0;0;False;0;False;0;0;0;100;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;187;-2640.371,-97.2354;Inherit;False;Property;_WindNoise02Size;Wind Noise 02 Size;17;0;Create;True;0;0;0;False;0;False;0;0;0;100;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;197;-2186.053,648.5867;Inherit;False;vVertexColor;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode;113;-2240.835,222.7103;Inherit;True;False;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;22;-2528,-812;Inherit;True;Property;_TextureSample0;Texture Sample 0;4;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Instance;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.LerpOp;132;-2640,-1292;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;186;-2640.371,-20.23573;Inherit;False;Property;_WindNoise01Multiplier;Wind Noise 01 Multiplier;16;0;Create;True;0;0;0;False;0;False;1;0;-3;3;0;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;20;-2912,-1100;Inherit;False;Property;_ColorBottom;Color Bottom;5;0;Create;True;0;0;0;False;0;False;0,0,0,0;0.5454459,0.8018868,0,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SaturateNode;21;-3040,-944;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;204;-1888,-1792;Inherit;False;PA_Dithering;23;;11;51907001cc2d98f4bb2ac7260d1fff6c;0;2;9;FLOAT;0;False;10;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;23;-2400,-972;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;181;-1568,-1792;Inherit;False;vTerrainDither;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;191;-1937.404,218.6381;Inherit;False;Property;_UseVertexColor;Use Vertex Color?;19;0;Create;True;0;0;0;False;0;False;0;0;0;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;128;-2192,-304;Inherit;False;Constant;_Color2;Color2;7;0;Create;True;0;0;0;False;0;False;0,0,0,0;0,0,0,0;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RegisterLocalVarNode;125;-2176,-688;Inherit;False;vAlpha;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;217;-1984,-768;Inherit;False;Property;_Blend;Blend;25;0;Create;True;0;0;0;False;0;False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;218;-2308.206,-92.46032;Inherit;False;PA_SF_WindGrass;11;;13;b1ba21c96122aa44f8eac64c8d72c027;0;4;29;FLOAT;10;False;34;FLOAT;10;False;32;FLOAT;1;False;39;FLOAT;1;False;1;COLOR;0
Node;AmplifyShaderEditor.GetLocalVarNode;158;-1325.317,375.0651;Inherit;False;181;vTerrainDither;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;126;-1294.043,271.5366;Inherit;False;125;vAlpha;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;108;-1757.449,-113.2636;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.BlendOpsNode;216;-1920,-896;Inherit;False;Overlay;True;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;1;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;162;-1000.607,191.5835;Inherit;False;Property;_FoliageRoughness;Foliage Roughness;9;0;Create;True;0;0;0;False;0;False;0.1;0.1;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;27;-1696,-896;Inherit;False;vColor;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;116;-1578.237,-118.6165;Inherit;False;vWind;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;161;-1040,352;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;206;-723.4158,196.1579;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;215;-848,112;Inherit;False;Property;_SpecularIntensity;Specular Intensity;8;0;Create;True;0;0;0;False;0;False;0.1;0.1;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;200;-848,32;Inherit;False;Property;_MaskClip;Mask Clip;10;0;Create;True;0;0;0;False;0;False;0.5;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;29;-752,-48;Inherit;False;27;vColor;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.GetLocalVarNode;122;-752,384;Inherit;False;116;vWind;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.StaticSwitch;182;-816,272;Inherit;False;Property;_UseDithering;Use Dithering?;22;0;Create;True;0;0;0;False;1;Header(Dithering);False;0;0;0;True;;Toggle;2;Key0;Key1;Create;False;True;All;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;0;-447.4691,37.94351;Float;False;True;-1;4;;0;0;StandardSpecular;Polyart/Dreamscape/Builtin/Grass;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;False;True;False;False;False;False;Off;0;False;;0;False;;False;0;False;;0;False;;False;0;Custom;0.59;True;True;0;True;Opaque;;AlphaTest;All;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;0;0;False;;0;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;-1;-1;-1;-1;0;True;0;0;False;;-1;0;True;_MaskClip;0;0;0;False;0.1;False;;0;False;;False;17;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;16;FLOAT4;0,0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
WireConnection;138;0;136;0
WireConnection;138;1;137;0
WireConnection;198;0;13;0
WireConnection;199;0;12;0
WireConnection;139;0;138;0
WireConnection;15;0;14;2
WireConnection;15;1;198;0
WireConnection;192;0;190;1
WireConnection;192;1;193;0
WireConnection;16;0;199;0
WireConnection;194;0;192;0
WireConnection;194;1;195;0
WireConnection;112;0;111;0
WireConnection;112;1;110;2
WireConnection;17;0;15;0
WireConnection;17;1;16;0
WireConnection;140;1;139;0
WireConnection;197;0;194;0
WireConnection;113;0;112;1
WireConnection;113;1;114;0
WireConnection;22;0;19;0
WireConnection;132;0;18;0
WireConnection;132;1;135;0
WireConnection;132;2;140;0
WireConnection;21;0;17;0
WireConnection;204;9;202;0
WireConnection;204;10;201;0
WireConnection;23;0;132;0
WireConnection;23;1;20;0
WireConnection;23;2;21;0
WireConnection;181;0;204;0
WireConnection;191;1;113;0
WireConnection;191;0;197;0
WireConnection;125;0;22;4
WireConnection;218;29;185;0
WireConnection;218;34;187;0
WireConnection;218;32;186;0
WireConnection;218;39;188;0
WireConnection;108;0;128;0
WireConnection;108;1;218;0
WireConnection;108;2;191;0
WireConnection;216;0;22;0
WireConnection;216;1;23;0
WireConnection;216;2;217;0
WireConnection;27;0;216;0
WireConnection;116;0;108;0
WireConnection;161;0;126;0
WireConnection;161;1;158;0
WireConnection;206;0;162;0
WireConnection;182;1;126;0
WireConnection;182;0;161;0
WireConnection;0;0;29;0
WireConnection;0;3;215;0
WireConnection;0;4;206;0
WireConnection;0;10;182;0
WireConnection;0;11;122;0
ASEEND*/
//CHKSM=7B57564166A6E43129F329D9A407F89DE659A64A