Shader "Custom/SnowTrailShaderTextured"
{
 Properties { _MainTex ("Snow texture", 2D) = "white" {} _Color ("Tint", Color) = (1,1,1,1) }
 SubShader
 {
  Tags { "RenderType"="Transparent" "Queue"="Transparent" }
  Offset -1,-1
  ZWrite Off
  CGPROGRAM
  #pragma surface surf Lambert alpha:fade
  sampler2D _MainTex; fixed4 _Color;
  struct Input { float2 uv_MainTex; };
  void surf(Input IN,inout SurfaceOutput o)
  {
   fixed4 c=tex2D(_MainTex,IN.uv_MainTex)*_Color;
   o.Albedo=dot(c.rgb,fixed3(.3,.59,.11))*.85;
   o.Alpha=.2*saturate(min(IN.uv_MainTex.x,1-IN.uv_MainTex.x)*8);
  }
  ENDCG
 }
 Fallback "Transparent/Diffuse"
}
