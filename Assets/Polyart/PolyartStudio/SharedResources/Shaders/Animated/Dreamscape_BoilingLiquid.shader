// Made with Amplify Shader Editor
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "Polyart/Animated/Boiling Liquid"
{
	Properties
	{
		_BaseColor("Base Color", Color) = (0.08539513,0.9528302,0.1375322,0)
		_HighlightColor("Highlight Color", Color) = (0.5613208,1,0.7317496,0)
		_ColorBlend("Color Blend", Range( 0 , 1)) = 0.5
		_BoilingSpeed("Boiling Speed", Range( 0 , 0.25)) = 0
		_Noise("Noise", Range( 0 , 2)) = 0.5
		_HeightMultiply("Height Multiply", Range( 0 , 0.1)) = 0.01
		_Transparency("Transparency", Range( 0 , 1)) = 1
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "AlphaTest+0" "IgnoreProjector" = "True" "IsEmissive" = "true"  }
		Cull Off
		Blend SrcAlpha OneMinusSrcAlpha
		
		CGPROGRAM
		#include "UnityShaderVariables.cginc"
		#pragma target 3.0
		#pragma surface surf Unlit keepalpha noshadow vertex:vertexDataFunc 
		struct Input
		{
			float3 worldPos;
		};

		uniform float _BoilingSpeed;
		uniform float _Noise;
		uniform float _HeightMultiply;
		uniform float4 _BaseColor;
		uniform float4 _HighlightColor;
		uniform float _ColorBlend;
		uniform float _Transparency;


		float3 mod3D289( float3 x ) { return x - floor( x / 289.0 ) * 289.0; }

		float4 mod3D289( float4 x ) { return x - floor( x / 289.0 ) * 289.0; }

		float4 permute( float4 x ) { return mod3D289( ( x * 34.0 + 1.0 ) * x ); }

		float4 taylorInvSqrt( float4 r ) { return 1.79284291400159 - r * 0.85373472095314; }

		float snoise( float3 v )
		{
			const float2 C = float2( 1.0 / 6.0, 1.0 / 3.0 );
			float3 i = floor( v + dot( v, C.yyy ) );
			float3 x0 = v - i + dot( i, C.xxx );
			float3 g = step( x0.yzx, x0.xyz );
			float3 l = 1.0 - g;
			float3 i1 = min( g.xyz, l.zxy );
			float3 i2 = max( g.xyz, l.zxy );
			float3 x1 = x0 - i1 + C.xxx;
			float3 x2 = x0 - i2 + C.yyy;
			float3 x3 = x0 - 0.5;
			i = mod3D289( i);
			float4 p = permute( permute( permute( i.z + float4( 0.0, i1.z, i2.z, 1.0 ) ) + i.y + float4( 0.0, i1.y, i2.y, 1.0 ) ) + i.x + float4( 0.0, i1.x, i2.x, 1.0 ) );
			float4 j = p - 49.0 * floor( p / 49.0 );  // mod(p,7*7)
			float4 x_ = floor( j / 7.0 );
			float4 y_ = floor( j - 7.0 * x_ );  // mod(j,N)
			float4 x = ( x_ * 2.0 + 0.5 ) / 7.0 - 1.0;
			float4 y = ( y_ * 2.0 + 0.5 ) / 7.0 - 1.0;
			float4 h = 1.0 - abs( x ) - abs( y );
			float4 b0 = float4( x.xy, y.xy );
			float4 b1 = float4( x.zw, y.zw );
			float4 s0 = floor( b0 ) * 2.0 + 1.0;
			float4 s1 = floor( b1 ) * 2.0 + 1.0;
			float4 sh = -step( h, 0.0 );
			float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
			float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
			float3 g0 = float3( a0.xy, h.x );
			float3 g1 = float3( a0.zw, h.y );
			float3 g2 = float3( a1.xy, h.z );
			float3 g3 = float3( a1.zw, h.w );
			float4 norm = taylorInvSqrt( float4( dot( g0, g0 ), dot( g1, g1 ), dot( g2, g2 ), dot( g3, g3 ) ) );
			g0 *= norm.x;
			g1 *= norm.y;
			g2 *= norm.z;
			g3 *= norm.w;
			float4 m = max( 0.6 - float4( dot( x0, x0 ), dot( x1, x1 ), dot( x2, x2 ), dot( x3, x3 ) ), 0.0 );
			m = m* m;
			m = m* m;
			float4 px = float4( dot( x0, g0 ), dot( x1, g1 ), dot( x2, g2 ), dot( x3, g3 ) );
			return 42.0 * dot( m, px);
		}


		void vertexDataFunc( inout appdata_full v, out Input o )
		{
			UNITY_INITIALIZE_OUTPUT( Input, o );
			float3 ase_worldPos = mul( unity_ObjectToWorld, v.vertex );
			float3 appendResult10 = (float3((ase_worldPos).xz , ( (ase_worldPos).y + ( _Time.y * _BoilingSpeed ) )));
			float simplePerlin3D18 = snoise( appendResult10*8.0 );
			float simplePerlin3D1 = snoise( appendResult10*4.0 );
			float temp_output_20_0 = ( ( simplePerlin3D18 * _Noise ) + simplePerlin3D1 );
			float3 ase_vertexNormal = v.normal.xyz;
			v.vertex.xyz += ( ( temp_output_20_0 * _HeightMultiply ) * ase_vertexNormal );
			v.vertex.w = 1;
		}

		inline half4 LightingUnlit( SurfaceOutput s, half3 lightDir, half atten )
		{
			return half4 ( 0, 0, 0, s.Alpha );
		}

		void surf( Input i , inout SurfaceOutput o )
		{
			float3 ase_worldPos = i.worldPos;
			float3 appendResult10 = (float3((ase_worldPos).xz , ( (ase_worldPos).y + ( _Time.y * _BoilingSpeed ) )));
			float simplePerlin3D18 = snoise( appendResult10*8.0 );
			float simplePerlin3D1 = snoise( appendResult10*4.0 );
			float temp_output_20_0 = ( ( simplePerlin3D18 * _Noise ) + simplePerlin3D1 );
			float temp_output_23_0 = ( temp_output_20_0 + 0.2 );
			float4 lerpResult27 = lerp( _BaseColor , _HighlightColor , ( _ColorBlend * temp_output_23_0 ));
			o.Emission = lerpResult27.rgb;
			float clampResult24 = clamp( temp_output_23_0 , 0.0 , 1.0 );
			o.Alpha = ( clampResult24 + ( 1.0 - _Transparency ) );
		}

		ENDCG
	}
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=18912
1012;111;2305;1607;168.3252;400.2267;1;True;False
Node;AmplifyShaderEditor.WorldPosInputsNode;4;-1478.797,269.2076;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.RangedFloatNode;29;-1595.669,1027.919;Inherit;False;Property;_BoilingSpeed;Boiling Speed;4;0;Create;True;0;0;0;False;0;False;0;0.1771;0;0.25;0;1;FLOAT;0
Node;AmplifyShaderEditor.TimeNode;9;-1582.922,818.9132;Inherit;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ComponentMaskNode;6;-1212.797,468.2076;Inherit;True;False;True;False;True;1;0;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;11;-1175.797,841.2077;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.ComponentMaskNode;5;-1213.797,261.2076;Inherit;True;True;False;True;True;1;0;FLOAT3;0,0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;8;-900.7975,604.2077;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;10;-778.7975,266.2076;Inherit;False;FLOAT3;4;0;FLOAT2;0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.NoiseGeneratorNode;18;-509.6245,-150.4037;Inherit;True;Simplex3D;False;True;2;0;FLOAT3;1,1,0;False;1;FLOAT;8;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;22;-541.6245,130.5963;Inherit;False;Property;_Noise;Noise;5;0;Create;True;0;0;0;False;0;False;0.5;0.936;0;2;0;1;FLOAT;0
Node;AmplifyShaderEditor.NoiseGeneratorNode;1;-165.5,256.5;Inherit;True;Simplex3D;False;True;2;0;FLOAT3;1,1,0;False;1;FLOAT;4;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;21;-69.62451,148.5963;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;20;131.1741,328.3402;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;31;817.2292,347.0884;Inherit;False;Property;_Transparency;Transparency;7;0;Create;True;0;0;0;False;0;False;1;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;23;401.5716,328.0549;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.2;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;30;206.4583,-146.447;Inherit;False;Property;_ColorBlend;Color Blend;2;0;Create;True;0;0;0;False;0;False;0.5;0.96;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;15;61.69934,546.9773;Inherit;False;Property;_HeightMultiply;Height Multiply;6;0;Create;True;0;0;0;False;0;False;0.01;0.0177;0;0.1;0;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;32;1114.929,354.8882;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;28;591.6375,-143.0969;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;14;366.2783,477.5213;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;25;291.6375,-631.0969;Inherit;False;Property;_BaseColor;Base Color;0;0;Create;True;0;0;0;False;0;False;0.08539513,0.9528302,0.1375322,0;0.04801529,0.7830188,0.09125083,0;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ColorNode;26;309.6375,-381.0969;Inherit;False;Property;_HighlightColor;Highlight Color;1;0;Create;True;0;0;0;False;0;False;0.5613208,1,0.7317496,0;0.5694317,1,0.1556603,0;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ClampOpNode;24;599.4697,328.2549;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.NormalVertexDataNode;12;322.8634,671.1207;Inherit;False;0;5;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.LerpOp;27;789.6375,-467.0969;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleAddOpNode;33;1159.128,181.9884;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;13;574.4903,476.0852;Inherit;True;2;2;0;FLOAT;0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;0;1599.178,212.8742;Float;False;True;-1;2;ASEMaterialInspector;0;0;Unlit;Polyart/Animated/Boiling Liquid;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;False;False;False;False;False;False;Off;0;False;-1;0;False;-1;False;0;False;-1;0;False;-1;False;0;Custom;0.6;True;False;0;True;Transparent;;AlphaTest;All;18;all;True;True;True;True;0;False;-1;False;0;False;-1;255;False;-1;255;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;-1;False;2;15;10;25;False;0.5;False;2;5;False;-1;10;False;-1;0;0;False;-1;0;False;-1;0;False;-1;0;False;-1;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;Relative;0;;3;-1;-1;-1;0;False;0;0;False;-1;-1;0;False;-1;0;0;0;False;0.1;False;-1;0;False;-1;False;15;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
WireConnection;6;0;4;0
WireConnection;11;0;9;2
WireConnection;11;1;29;0
WireConnection;5;0;4;0
WireConnection;8;0;6;0
WireConnection;8;1;11;0
WireConnection;10;0;5;0
WireConnection;10;2;8;0
WireConnection;18;0;10;0
WireConnection;1;0;10;0
WireConnection;21;0;18;0
WireConnection;21;1;22;0
WireConnection;20;0;21;0
WireConnection;20;1;1;0
WireConnection;23;0;20;0
WireConnection;32;0;31;0
WireConnection;28;0;30;0
WireConnection;28;1;23;0
WireConnection;14;0;20;0
WireConnection;14;1;15;0
WireConnection;24;0;23;0
WireConnection;27;0;25;0
WireConnection;27;1;26;0
WireConnection;27;2;28;0
WireConnection;33;0;24;0
WireConnection;33;1;32;0
WireConnection;13;0;14;0
WireConnection;13;1;12;0
WireConnection;0;2;27;0
WireConnection;0;9;33;0
WireConnection;0;11;13;0
ASEEND*/
//CHKSM=B31BA080E1E1E6CA01F158D47B2FA2650779D4ED