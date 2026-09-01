// Made with Amplify Shader Editor
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Polyart/Animated/Flipbook Material"
{
	Properties
	{
		_TextureSample0("Texture Sample 0", 2D) = "white" {}
		_AnimationSpeed("Animation Speed", Int) = 22
		_Rows("Rows", Int) = 4
		_Columns("Columns", Int) = 8
		_ColorTint("Color Tint", Color) = (1,1,1,1)
		_AlphaMultiply("Alpha Multiply", Range( 0 , 5)) = 0.5
		_Opacity("Opacity", Range( 0 , 1)) = 1
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+0" "IgnoreProjector" = "True" "IsEmissive" = "true"  }
		Cull Off
		CGINCLUDE
		#include "UnityShaderVariables.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.0
		struct Input
		{
			float2 uv_texcoord;
		};

		uniform float4 _ColorTint;
		uniform sampler2D _TextureSample0;
		uniform int _Columns;
		uniform int _Rows;
		uniform int _AnimationSpeed;
		uniform float _AlphaMultiply;
		uniform float _Opacity;

		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			// *** BEGIN Flipbook UV Animation vars ***
			// Total tiles of Flipbook Texture
			float fbtotaltiles1 = (float)_Columns * (float)_Rows;
			// Offsets for cols and rows of Flipbook Texture
			float fbcolsoffset1 = 1.0f / (float)_Columns;
			float fbrowsoffset1 = 1.0f / (float)_Rows;
			// Speed of animation
			float fbspeed1 = _Time.y * (float)_AnimationSpeed;
			// UV Tiling (col and row offset)
			float2 fbtiling1 = float2(fbcolsoffset1, fbrowsoffset1);
			// UV Offset - calculate current tile linear index, and convert it to (X * coloffset, Y * rowoffset)
			// Calculate current tile linear index
			float fbcurrenttileindex1 = round( fmod( fbspeed1 + (float)0, fbtotaltiles1) );
			fbcurrenttileindex1 += ( fbcurrenttileindex1 < 0) ? fbtotaltiles1 : 0;
			// Obtain Offset X coordinate from current tile linear index
			float fblinearindextox1 = round ( fmod ( fbcurrenttileindex1, (float)_Columns ) );
			// Multiply Offset X by coloffset
			float fboffsetx1 = fblinearindextox1 * fbcolsoffset1;
			// Obtain Offset Y coordinate from current tile linear index
			float fblinearindextoy1 = round( fmod( ( fbcurrenttileindex1 - fblinearindextox1 ) / (float)_Columns, (float)_Rows ) );
			// Reverse Y to get tiles from Top to Bottom
			fblinearindextoy1 = (int)((float)_Rows-1) - fblinearindextoy1;
			// Multiply Offset Y by rowoffset
			float fboffsety1 = fblinearindextoy1 * fbrowsoffset1;
			// UV Offset
			float2 fboffset1 = float2(fboffsetx1, fboffsety1);
			// Flipbook UV
			half2 fbuv1 = i.uv_texcoord * fbtiling1 + fboffset1;
			// *** END Flipbook UV Animation vars ***
			float4 tex2DNode2 = tex2D( _TextureSample0, fbuv1 );
			o.Emission = ( _ColorTint * tex2DNode2 ).rgb;
			o.Alpha = ( pow( tex2DNode2.a , _AlphaMultiply ) * _Opacity );
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf Unlit alpha:fade keepalpha fullforwardshadows 

		ENDCG
		Pass
		{
			Name "ShadowCaster"
			Tags{ "LightMode" = "ShadowCaster" }
			ZWrite On
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
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
			sampler3D _DitherMaskLOD;
			struct v2f
			{
				V2F_SHADOW_CASTER;
				float2 customPack1 : TEXCOORD1;
				float3 worldPos : TEXCOORD2;
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
				float3 worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				half3 worldNormal = UnityObjectToWorldNormal( v.normal );
				o.customPack1.xy = customInputData.uv_texcoord;
				o.customPack1.xy = v.texcoord;
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
				float3 worldPos = IN.worldPos;
				half3 worldViewDir = normalize( UnityWorldSpaceViewDir( worldPos ) );
				SurfaceOutput o;
				UNITY_INITIALIZE_OUTPUT( SurfaceOutput, o )
				surf( surfIN, o );
				#if defined( CAN_SKIP_VPOS )
				float2 vpos = IN.pos;
				#endif
				half alphaRef = tex3D( _DitherMaskLOD, float3( vpos.xy * 0.25, o.Alpha * 0.9375 ) ).a;
				clip( alphaRef - 0.01 );
				SHADOW_CASTER_FRAGMENT( IN )
			}
			ENDCG
		}
	}
	Fallback "Diffuse"
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=18912
1012;111;2305;1607;1534.998;788.4639;1;True;False
Node;AmplifyShaderEditor.TextureCoordinatesNode;3;-975,-246.5;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleTimeNode;7;-921,259.5;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.IntNode;6;-897,179.5;Inherit;False;Constant;_Int2;Int 2;1;0;Create;True;0;0;0;False;0;False;0;0;False;0;1;INT;0
Node;AmplifyShaderEditor.IntNode;5;-957,94.5;Inherit;False;Property;_AnimationSpeed;Animation Speed;1;0;Create;True;0;0;0;False;0;False;22;20;False;0;1;INT;0
Node;AmplifyShaderEditor.IntNode;8;-897,13.5;Inherit;False;Property;_Rows;Rows;2;0;Create;True;0;0;0;False;0;False;4;8;False;0;1;INT;0
Node;AmplifyShaderEditor.IntNode;4;-899,-72.5;Inherit;False;Property;_Columns;Columns;3;0;Create;True;0;0;0;False;0;False;8;8;False;0;1;INT;0
Node;AmplifyShaderEditor.TFHCFlipBookUVAnimation;1;-581,27.5;Inherit;False;0;0;6;0;FLOAT2;0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.RangedFloatNode;19;-319.9977,331.5361;Inherit;False;Property;_AlphaMultiply;Alpha Multiply;5;0;Create;True;0;0;0;False;0;False;0.5;1.98;0;5;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;2;-316,0.5;Inherit;True;Property;_TextureSample0;Texture Sample 0;0;0;Create;True;0;0;0;False;0;False;-1;None;12d8e543b699ed6459db5ce76c5ba9d6;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.PowerNode;18;65.00226,112.5361;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;21;-327.9977,410.5361;Inherit;False;Property;_Opacity;Opacity;6;0;Create;True;0;0;0;False;0;False;1;0.526;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;10;-264,-213.5;Inherit;False;Property;_ColorTint;Color Tint;4;0;Create;True;0;0;0;False;0;False;1,1,1,1;0,0.8773585,0.07040574,0;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;9;71,-0.5;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;20;261.0023,171.5361;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;0;459.1999,-74.69997;Float;False;True;-1;2;ASEMaterialInspector;0;0;Unlit;Polyart/Animated/Flipbook Material;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;False;False;False;False;False;False;Off;0;False;-1;0;False;-1;False;0;False;-1;0;False;-1;False;0;Transparent;0.5;True;True;0;False;Transparent;;Transparent;All;18;all;True;True;True;True;0;False;-1;False;0;False;-1;255;False;-1;255;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;False;2;15;10;25;False;0.5;True;2;5;False;-1;10;False;-1;0;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;Relative;0;;-1;-1;-1;-1;0;False;0;0;False;-1;-1;0;False;-1;0;0;0;False;0.1;False;-1;0;False;-1;False;15;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
WireConnection;1;0;3;0
WireConnection;1;1;4;0
WireConnection;1;2;8;0
WireConnection;1;3;5;0
WireConnection;1;4;6;0
WireConnection;1;5;7;0
WireConnection;2;1;1;0
WireConnection;18;0;2;4
WireConnection;18;1;19;0
WireConnection;9;0;10;0
WireConnection;9;1;2;0
WireConnection;20;0;18;0
WireConnection;20;1;21;0
WireConnection;0;2;9;0
WireConnection;0;9;20;0
ASEEND*/
//CHKSM=24EE2B4D66150A2C74119793477AFBBE43DABC3B