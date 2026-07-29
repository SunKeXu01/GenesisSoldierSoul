Shader "Skybox/Procedural" {
Properties {
 _SunSize ("Sun size", Range(0,1)) = 0.04
 _AtmosphereThickness ("Atmoshpere Thickness", Range(0,5)) = 1
 _SkyTint ("Sky Tint", Color) = (0.5,0.5,0.5,1)
 _GroundColor ("Ground", Color) = (0.369,0.349,0.341,1)
 _Exposure ("Exposure", Range(0,8)) = 1.3
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