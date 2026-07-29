Shader "CorvoShader/WaterAusfA" {
	Properties {
		_Color ("Color", Vector) = (0,0,0,0.5)
		[NoScaleOffset] _MainTex ("Water BASE", 2D) = "white" {}
		_TextureSize ("Texture Size", Range(0, 100000)) = 1000
		_mixCoeff ("Water Mixing", Range(0, 1)) = 0.1
		_WatSpeed ("Water Speed", Range(0, 10)) = 0.5
		_WaveL ("Wave Lenght", Range(0, 100)) = 1
		_WatRot ("Water Direction", Range(0, 360)) = 0
		[NoScaleOffset] [Normal] _refMap ("Reflection MAP", 2D) = "bump" {}
		_Glossiness ("Reflection", Range(0, 1)) = 0.9
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType"="Opaque" }
		LOD 200

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4x4 unity_ObjectToWorld;
			float4x4 unity_MatrixVP;
			float4 _MainTex_ST;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Vertex_Stage_Output
			{
				float2 uv : TEXCOORD0;
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.uv = (input.uv.xy * _MainTex_ST.xy) + _MainTex_ST.zw;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			Texture2D<float4> _MainTex;
			SamplerState sampler_MainTex;
			float4 _Color;

			struct Fragment_Stage_Input
			{
				float2 uv : TEXCOORD0;
			};

			float4 frag(Fragment_Stage_Input input) : SV_TARGET
			{
				return _MainTex.Sample(sampler_MainTex, input.uv.xy) * _Color;
			}

			ENDHLSL
		}
	}
	Fallback "Diffuse"
}