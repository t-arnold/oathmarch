// Greybox lit shader (Built-in RP). Kept in Resources so greybox never depends on Shader.Find.
Shader "Oathmarch/SimpleLit"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) _Emission ("Emission", Color) = (0,0,0,0) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows
        #pragma multi_compile_instancing
        fixed4 _Color; fixed4 _Emission;
        struct Input { float3 worldPos; };
        void surf (Input IN, inout SurfaceOutput o) { o.Albedo = _Color.rgb; o.Emission = _Emission.rgb; }
        ENDCG
    }
    Fallback "Diffuse"
}
