Shader "Skybox/6 Sided" {
Properties {
 _Tint ("Tint Color", Color) = (0.5,0.5,0.5,0.5)
[Gamma]  _Exposure ("Exposure", Range(0,8)) = 1
 _Rotation ("Rotation", Range(0,360)) = 0
[NoScaleOffset]  _FrontTex ("Front [+Z]   (HDR)", 2D) = "grey" { }
[NoScaleOffset]  _BackTex ("Back [-Z]   (HDR)", 2D) = "grey" { }
[NoScaleOffset]  _LeftTex ("Left [+X]   (HDR)", 2D) = "grey" { }
[NoScaleOffset]  _RightTex ("Right [-X]   (HDR)", 2D) = "grey" { }
[NoScaleOffset]  _UpTex ("Up [+Y]   (HDR)", 2D) = "grey" { }
[NoScaleOffset]  _DownTex ("Down [-Y]   (HDR)", 2D) = "grey" { }
}
	//DummyShaderTextExporter
	
	SubShader{
		Tags { "RenderType" = "Opaque" }
		LOD 200
		CGPROGRAM
#pragma surface surf Lambert
#pragma target 3.0
		sampler2D _MainTex;
		struct Input
		{
			float2 uv_MainTex;
		};
		void surf(Input IN, inout SurfaceOutput o)
		{
			float4 c = tex2D(_MainTex, IN.uv_MainTex);
			o.Albedo = c.rgb;
		}
		ENDCG
	}
}