// Made with Amplify Shader Editor v1.9.8.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "M_WaterLine"
{
    Properties
    {
        _Color0("Color 0", Color) = (0.2906728,0.3717396,0.8679245,1)
        _TerrainPosAndSize("TerrainPosAndSize", Vector) = (0,0,0,0)
        _TerrainHeightMap("TerrainHeightMap", 2D) = "black" {}
        _ShoreDistanceWPODampening("Shore Distance WPO Dampening", Float) = 4
        _RefractionNoiseTexture("Refraction Noise Texture", 2D) = "white" {}
        _DistortionIntensity("Distortion Intensity", Float) = 0.05
        _DistortionSpeed("Distortion Speed", Float) = 0.05
        _WaterHeight("_WaterHeight", Float) = 0
        _TerrainHeight("TerrainHeight", Vector) = (0,0,0,0)
        _MainTex("_MainTex", 2D) = "white" {}
        [HideInInspector] _texcoord( "", 2D ) = "white" {}

    }

    SubShader
    {
		LOD 0

        
		CGINCLUDE
		#pragma target 3.0
		ENDCG
		Blend Off
		AlphaToMask Off
		Cull Back
		ColorMask RGBA
		ZWrite On
		ZTest LEqual
		Offset 0 , 0
		
		
        Pass
        {
			Name "Custom RT Init"
            CGPROGRAM
            #define ASE_VERSION 19801

            #include "UnityCustomRenderTexture.cginc"

            #pragma vertex ASEInitCustomRenderTextureVertexShader
            #pragma fragment frag
            #pragma target 3.5
			#include "UnityShaderVariables.cginc"
			#include "UnityCG.cginc"
			#include "GerstnerWave.hlsl"


			struct ase_appdata_init_customrendertexture
			{
				float4 vertex : POSITION;
				float4 texcoord : TEXCOORD0;
				
			};

			// User facing vertex to fragment structure for initialization materials
			struct ase_v2f_init_customrendertexture
			{
				float4 vertex : SV_POSITION;
				float2 texcoord : TEXCOORD0;
				float3 direction : TEXCOORD1;
				
			};

			uniform sampler2D _MainTex;
			uniform float4 _MainTex_ST;
			uniform sampler2D _RefractionNoiseTexture;
			uniform float _DistortionSpeed;
			uniform float _DistortionIntensity;
			uniform float4 _Color0;
			uniform float _ShoreDistanceWPODampening;
			uniform float _WaterHeight;
			uniform sampler2D _TerrainHeightMap;
			uniform float4 _TerrainPosAndSize;
			uniform float4 _TerrainHeight;
			void GerstnerWaves( inout float WPO, inout float3 Normals, float2 WorldPos, float Time )
			{
				CalculateGerstnerWaves_float(WorldPos, Time,WPO, Normals);
			}
			


			ase_v2f_init_customrendertexture ASEInitCustomRenderTextureVertexShader (ase_appdata_init_customrendertexture v )
			{
				ase_v2f_init_customrendertexture o;
				
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.texcoord = float3(v.texcoord.xy, CustomRenderTexture3DTexcoordW);
				o.direction = CustomRenderTextureComputeCubeDirection(v.texcoord.xy);
				return o;
			}

            float4 frag(ase_v2f_init_customrendertexture IN ) : COLOR
            {
                float4 finalColor;
				float4 color245 = IsGammaSpace() ? float4(0.03924884,0.16138,0.3962264,1) : float4(0.003037836,0.02232108,0.130239,1);
				float2 uv_MainTex = IN.texcoord * _MainTex_ST.xy + _MainTex_ST.zw;
				float2 texCoord268 = IN.texcoord * float2( 1,1 ) + float2( 0,0 );
				float2 clampResult235 = clamp( ( texCoord268 + ( tex2D( _RefractionNoiseTexture, ( texCoord268 + ( _Time.y * _DistortionSpeed ) ) ).r * _DistortionIntensity ) ) , float2( 0,0 ) , float2( 1,1 ) );
				float3 gammaToLinear305 = GammaToLinearSpace( tex2D( _MainTex, clampResult235 ).rgb );
				float2 texCoord267 = IN.texcoord * float2( 1,1 ) + float2( 0,0 );
				float2 break59 = texCoord267;
				#ifdef UNITY_REVERSED_Z
				float staticSwitch51 = ( 1.0 - 1.0 );
				#else
				float staticSwitch51 = 1.0;
				#endif
				float3 appendResult52 = (float3(break59.x , break59.y , staticSwitch51));
				float4 appendResult54 = (float4((appendResult52*2.0 + -1.0) , 1.0));
				float4 temp_output_50_0 = mul( unity_CameraInvProjection, appendResult54 );
				float4 appendResult62 = (float4(( ( (temp_output_50_0).xyz / (temp_output_50_0).w ) * float3(1,1,-1) ) , 1.0));
				float3 Near_Plane_World_Pos87 = (mul( unity_CameraToWorld, appendResult62 )).xyz;
				float temp_output_101_0 = (Near_Plane_World_Pos87).y;
				float3 break211 = Near_Plane_World_Pos87;
				float2 appendResult199 = (float2(break211.x , break211.z));
				float2 appendResult198 = (float2(_TerrainPosAndSize.x , _TerrainPosAndSize.y));
				float2 appendResult201 = (float2(_TerrainPosAndSize.z , _TerrainPosAndSize.w));
				float Water_Depth_Texture208 = ( _WaterHeight - ( ( tex2D( _TerrainHeightMap, ( ( appendResult199 - appendResult198 ) / appendResult201 ) ).r * _TerrainHeight.x ) + _TerrainHeight.y ) );
				float smoothstepResult217 = smoothstep( 0.0 , _ShoreDistanceWPODampening , Water_Depth_Texture208);
				float localGerstnerWaves95 = ( 0.0 );
				float WPO95 = 0.0;
				float3 temp_cast_2 = (0.0).xxx;
				float3 Normals95 = temp_cast_2;
				float3 break97 = Near_Plane_World_Pos87;
				float2 appendResult94 = (float2(break97.x , break97.z));
				float2 WorldPos95 = appendResult94;
				float Time95 = _Time.y;
				{
				CalculateGerstnerWaves_float(WorldPos95, Time95,WPO95, Normals95);
				}
				float WPO_Gerstner98 = WPO95;
				float WPO_Final213 = ( smoothstepResult217 * WPO_Gerstner98 );
				float temp_output_104_0 = ( WPO_Final213 + _WaterHeight );
				float4 lerpResult247 = lerp( tex2D( _MainTex, uv_MainTex ) , float4( ( gammaToLinear305 * _Color0.rgb ) , 0.0 ) , step( temp_output_101_0 , temp_output_104_0 ));
				float smoothstepResult242 = smoothstep( 0.002 , 0.007 , distance( temp_output_101_0 , temp_output_104_0 ));
				float4 lerpResult243 = lerp( float4( color245.rgb , 0.0 ) , lerpResult247 , smoothstepResult242);
				
                finalColor = lerpResult243;
				return finalColor;
            }
            ENDCG
        }
    }
	
	CustomEditor "AmplifyShaderEditor.MaterialInspector"
	Fallback Off
}
/*ASEBEGIN
Version=19801
Node;AmplifyShaderEditor.RangedFloatNode;74;-4576,1744;Inherit;False;Constant;_Float6;Float 6;1;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;56;-4416,1808;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;267;-4757.675,1356.758;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.StaticSwitch;51;-4256,1744;Float;False;Property;_Keyword0;Keyword 0;3;0;Fetch;True;0;0;0;False;0;False;0;0;0;False;UNITY_REVERSED_Z;Toggle;2;Key0;Key1;Fetch;False;True;All;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.BreakToComponentsNode;59;-4320,1568;Inherit;False;FLOAT2;1;0;FLOAT2;0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.DynamicAppendNode;52;-3984,1568;Inherit;False;FLOAT3;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;1;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ScaleAndOffsetNode;64;-3824,1568;Inherit;False;3;0;FLOAT3;0,0,0;False;1;FLOAT;2;False;2;FLOAT;-1;False;1;FLOAT3;0
Node;AmplifyShaderEditor.DynamicAppendNode;54;-3600,1568;Inherit;False;FLOAT4;4;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;1;False;1;FLOAT4;0
Node;AmplifyShaderEditor.CameraProjectionNode;63;-3712,1472;Inherit;False;unity_CameraInvProjection;0;1;FLOAT4x4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;50;-3424,1536;Inherit;False;2;2;0;FLOAT4x4;0,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1;False;1;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.ComponentMaskNode;49;-3264,1472;Inherit;False;True;True;True;False;1;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ComponentMaskNode;53;-3264,1584;Inherit;False;False;False;False;True;1;0;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;61;-3024,1520;Inherit;False;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.Vector3Node;73;-3008,1632;Inherit;False;Constant;_Vector0;Vector 0;1;0;Create;True;0;0;0;False;0;False;1,1,-1;0,0,0;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;72;-2784,1504;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.CameraToWorldMatrix;58;-2656,1424;Inherit;False;0;1;FLOAT4x4;0
Node;AmplifyShaderEditor.DynamicAppendNode;62;-2592,1504;Inherit;False;FLOAT4;4;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;1;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;57;-2432,1456;Inherit;False;2;2;0;FLOAT4x4;0,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1;False;1;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.ComponentMaskNode;78;-2272,1456;Inherit;False;True;True;True;False;1;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;87;-2048,1456;Inherit;False;Near Plane World Pos;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode;209;-5248,592;Inherit;False;87;Near Plane World Pos;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.Vector4Node;196;-5088,736;Inherit;False;Property;_TerrainPosAndSize;TerrainPosAndSize;3;0;Create;False;0;0;0;False;0;False;0,0,0,0;0,0,991.36,1000;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.BreakToComponentsNode;211;-4992,592;Inherit;False;FLOAT3;1;0;FLOAT3;0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.DynamicAppendNode;198;-4832,736;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode;199;-4832,608;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;200;-4624,640;Inherit;False;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode;201;-4832,832;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;202;-4480,736;Inherit;False;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SamplerNode;203;-4336,704;Inherit;True;Property;_TerrainHeightMap;TerrainHeightMap;4;0;Create;True;0;0;0;False;0;False;-1;2c6536772776dd84f872779990273bfc;None;True;0;False;black;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.Vector4Node;250;-4007.772,905.5092;Inherit;False;Property;_TerrainHeight;TerrainHeight;10;0;Create;True;0;0;0;False;0;False;0,0,0,0;0,0,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.GetLocalVarNode;96;-2272,2128;Inherit;False;87;Near Plane World Pos;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;205;-3840,752;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.BreakToComponentsNode;97;-2000,2128;Inherit;False;FLOAT3;1;0;FLOAT3;0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.SimpleAddOpNode;249;-3645.772,876.5092;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;237;-4064,336;Inherit;False;Property;_WaterHeight;_WaterHeight;9;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;232;-3304.78,390.4019;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;234;-3328,512;Inherit;False;Property;_DistortionSpeed;Distortion Speed;8;0;Create;True;0;0;0;False;0;False;0.05;0.05;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;89;-1884.987,2243.502;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;90;-1852.987,1923.502;Inherit;False;Constant;_Float5;Float 1;17;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;91;-1852.987,1859.502;Inherit;False;Constant;_Float7;Float 0;17;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;94;-1852.987,2131.502;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;207;-3632,688;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;233;-3088,384;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;268;-3444.284,-265.8273;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.CustomExpressionNode;95;-1644.987,1971.502;Inherit;False;CalculateGerstnerWaves_float(WorldPos, Time,WPO, Normals)@$;7;Create;4;True;WPO;FLOAT;0;InOut;;Inherit;False;True;Normals;FLOAT3;0,0,0;InOut;;Inherit;False;True;WorldPos;FLOAT2;0,0;In;;Inherit;False;True;Time;FLOAT;0;In;;Inherit;False;Gerstner Waves;False;True;0;;False;5;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT3;0,0,0;False;3;FLOAT2;0,0;False;4;FLOAT;0;False;3;FLOAT;0;FLOAT;2;FLOAT3;3
Node;AmplifyShaderEditor.RegisterLocalVarNode;208;-3472,688;Inherit;False;Water Depth Texture;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;231;-2864,352;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;98;-1392,1968;Inherit;False;WPO Gerstner;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;214;-3152.215,943.5317;Inherit;False;Property;_ShoreDistanceWPODampening;Shore Distance WPO Dampening;5;0;Create;False;0;0;0;False;0;False;4;5;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;215;-3088.215,863.5317;Inherit;False;208;Water Depth Texture;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;223;-2672,320;Inherit;True;Property;_RefractionNoiseTexture;Refraction Noise Texture;6;0;Create;True;0;0;0;False;0;False;-1;a8a71463226d3cd4cbf9c8308307789b;a8a71463226d3cd4cbf9c8308307789b;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RangedFloatNode;230;-2608,528;Inherit;False;Property;_DistortionIntensity;Distortion Intensity;7;0;Create;True;0;0;0;False;0;False;0.05;0.03;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;216;-3120,1088;Inherit;False;98;WPO Gerstner;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SmoothstepOpNode;217;-2848.215,863.5317;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;10;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;224;-2288,368;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;218;-2592.215,927.5317;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;222;-2480,48;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;213;-2432.215,927.5317;Inherit;False;WPO Final;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ClampOpNode;235;-2352,48;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;2;FLOAT2;1,1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode;102;-1424,784;Inherit;False;213;WPO Final;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;100;-1360,560;Inherit;False;87;Near Plane World Pos;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SamplerNode;270;-2032,16;Inherit;True;Property;_MainTex;_MainTex;12;0;Fetch;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ColorNode;4;-2000,304;Inherit;False;Property;_Color0;Color 0;1;0;Create;True;0;0;0;False;0;False;0.2906728,0.3717396,0.8679245,1;0.06968674,0.1452683,0.509434,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleAddOpNode;104;-992,848;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ComponentMaskNode;101;-1104,560;Inherit;False;False;True;False;True;1;0;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GammaToLinearNode;305;-1591.111,165.6129;Inherit;False;0;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StepOpNode;248;-960,320;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DistanceOpNode;238;-648.0304,458.7771;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;273;-1312,224;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.SamplerNode;258;-1200,-144;Inherit;True;Property;_MainTex;_MainTex;11;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.LerpOp;247;-720,160;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.ColorNode;245;-512,656;Inherit;False;Constant;_WaterLineColor;Water Line Color;12;0;Create;True;0;0;0;False;0;False;0.03924884,0.16138,0.3962264,1;0,0,0,0;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SmoothstepOpNode;242;-464,496;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0.002;False;2;FLOAT;0.007;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;294;-816,-656;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ScreenDepthNode;293;-480,-656;Inherit;False;1;1;0;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;296;336,-656;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScreenPosInputsNode;55;-4848,1568;Float;False;0;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.FunctionNode;60;-4624,1568;Inherit;False;Non Stereo Screen Pos;-1;;4;1731ee083b93c104880efc701e11b49b;0;1;23;FLOAT4;0,0,0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;144;-4464,3216;Inherit;False;Constant;_Float10;Float 6;1;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;146;-4304,3280;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;148;-4144,3216;Float;False;Property;_Keyword0;Keyword 0;3;0;Fetch;True;0;0;0;False;0;False;0;0;0;False;UNITY_REVERSED_Z;Toggle;2;Key0;Key1;Fetch;False;True;All;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PosVertexDataNode;164;-4080,3040;Inherit;False;0;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.CameraProjectionNode;153;-4000,2912;Inherit;False;unity_CameraInvProjection;0;1;FLOAT4x4;0
Node;AmplifyShaderEditor.DynamicAppendNode;150;-3872,3040;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;1;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;154;-3711.232,2974.146;Inherit;False;2;2;0;FLOAT4x4;0,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1;False;1;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.ComponentMaskNode;155;-3551.232,2910.146;Inherit;False;True;True;True;False;1;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ComponentMaskNode;156;-3551.232,3022.146;Inherit;False;False;False;False;True;1;0;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;157;-3311.232,2958.146;Inherit;False;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.Vector3Node;158;-3295.232,3070.146;Inherit;False;Constant;_Vector3;Vector 0;1;0;Create;True;0;0;0;False;0;False;1,1,-1;0,0,0;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;159;-3071.232,2942.146;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.DynamicAppendNode;161;-2879.232,2942.146;Inherit;False;FLOAT4;4;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;1;False;1;FLOAT4;0
Node;AmplifyShaderEditor.CameraToWorldMatrix;160;-2943.232,2862.146;Inherit;False;0;1;FLOAT4x4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;162;-2719.232,2894.146;Inherit;False;2;2;0;FLOAT4x4;0,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1;False;1;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.PosVertexDataNode;167;-2512,2976;Inherit;False;0;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.TransformPositionNode;169;-2512,2832;Inherit;False;World;Object;False;Fast;True;1;0;FLOAT3;0,0,0;False;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.Vector4Node;212;-4000,544;Inherit;False;Property;_FlowPivot;_FlowPivot;2;0;Fetch;True;0;0;0;False;0;False;0,0,0,0;0,0,0,0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;92;-1852.987,1987.502;Inherit;False;Constant;_Float8;Float 7;33;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;93;-1852.987,2051.502;Inherit;False;Constant;_Float9;Float 8;42;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;168;-2240,2928;Inherit;False;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.DynamicAppendNode;251;-2864,1056;Inherit;False;FLOAT3;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ComponentMaskNode;105;-1216,704;Inherit;False;False;True;False;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;243;-160,432;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SamplerNode;292;-336,96;Inherit;True;Property;_CameraDepthTexture;_CameraDepthTexture;12;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleSubtractOpNode;303;448,-656;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;304;592,-656;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;10;False;1;FLOAT;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;266;16,432;Float;False;True;-1;3;AmplifyShaderEditor.MaterialInspector;0;1;M_WaterLine;6ce779933eb99f049b78d6163735e06f;True;Custom RT Init;0;0;Custom RT Init;1;False;True;0;1;False;;0;False;;0;1;False;;0;False;;True;0;False;;0;False;;False;False;False;False;False;False;False;False;False;True;0;False;;False;True;0;False;;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;1;False;;True;3;False;;True;True;0;False;;0;False;;True;0;True;2;False;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;3;False;2;Include;;False;;Native;False;0;0;;Include;GerstnerWave.hlsl;False;;Custom;False;0;0;;;0;0;Standard;0;0;1;True;False;;False;0
WireConnection;56;0;74;0
WireConnection;51;1;74;0
WireConnection;51;0;56;0
WireConnection;59;0;267;0
WireConnection;52;0;59;0
WireConnection;52;1;59;1
WireConnection;52;2;51;0
WireConnection;64;0;52;0
WireConnection;54;0;64;0
WireConnection;50;0;63;0
WireConnection;50;1;54;0
WireConnection;49;0;50;0
WireConnection;53;0;50;0
WireConnection;61;0;49;0
WireConnection;61;1;53;0
WireConnection;72;0;61;0
WireConnection;72;1;73;0
WireConnection;62;0;72;0
WireConnection;57;0;58;0
WireConnection;57;1;62;0
WireConnection;78;0;57;0
WireConnection;87;0;78;0
WireConnection;211;0;209;0
WireConnection;198;0;196;1
WireConnection;198;1;196;2
WireConnection;199;0;211;0
WireConnection;199;1;211;2
WireConnection;200;0;199;0
WireConnection;200;1;198;0
WireConnection;201;0;196;3
WireConnection;201;1;196;4
WireConnection;202;0;200;0
WireConnection;202;1;201;0
WireConnection;203;1;202;0
WireConnection;205;0;203;1
WireConnection;205;1;250;1
WireConnection;97;0;96;0
WireConnection;249;0;205;0
WireConnection;249;1;250;2
WireConnection;94;0;97;0
WireConnection;94;1;97;2
WireConnection;207;0;237;0
WireConnection;207;1;249;0
WireConnection;233;0;232;0
WireConnection;233;1;234;0
WireConnection;95;1;91;0
WireConnection;95;2;90;0
WireConnection;95;3;94;0
WireConnection;95;4;89;0
WireConnection;208;0;207;0
WireConnection;231;0;268;0
WireConnection;231;1;233;0
WireConnection;98;0;95;2
WireConnection;223;1;231;0
WireConnection;217;0;215;0
WireConnection;217;2;214;0
WireConnection;224;0;223;1
WireConnection;224;1;230;0
WireConnection;218;0;217;0
WireConnection;218;1;216;0
WireConnection;222;0;268;0
WireConnection;222;1;224;0
WireConnection;213;0;218;0
WireConnection;235;0;222;0
WireConnection;270;1;235;0
WireConnection;104;0;102;0
WireConnection;104;1;237;0
WireConnection;101;0;100;0
WireConnection;305;0;270;5
WireConnection;248;0;101;0
WireConnection;248;1;104;0
WireConnection;238;0;101;0
WireConnection;238;1;104;0
WireConnection;273;0;305;0
WireConnection;273;1;4;5
WireConnection;247;0;258;0
WireConnection;247;1;273;0
WireConnection;247;2;248;0
WireConnection;242;0;238;0
WireConnection;293;0;294;0
WireConnection;296;0;293;0
WireConnection;60;23;55;0
WireConnection;146;0;144;0
WireConnection;148;1;144;0
WireConnection;148;0;146;0
WireConnection;150;0;164;1
WireConnection;150;1;164;2
WireConnection;150;2;148;0
WireConnection;154;0;153;0
WireConnection;154;1;150;0
WireConnection;155;0;154;0
WireConnection;156;0;154;0
WireConnection;157;0;155;0
WireConnection;157;1;156;0
WireConnection;159;0;157;0
WireConnection;159;1;158;0
WireConnection;161;0;159;0
WireConnection;162;0;160;0
WireConnection;162;1;161;0
WireConnection;169;0;162;0
WireConnection;168;0;169;0
WireConnection;168;1;167;0
WireConnection;251;1;216;0
WireConnection;105;0;102;0
WireConnection;243;0;245;5
WireConnection;243;1;247;0
WireConnection;243;2;242;0
WireConnection;303;0;296;0
WireConnection;304;0;303;0
WireConnection;266;0;243;0
ASEEND*/
//CHKSM=2A97FB48C519EE6848E7E287C0501B760FCE8253