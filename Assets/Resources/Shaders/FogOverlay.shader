// Oathmarch fog overlay: a big quad over the ground textured with the brain's fog grid (alpha = darkness).
// Lives in Resources so it is always included and can be loaded without Shader.Find.
Shader "Oathmarch/FogOverlay"
{
    Properties { _MainTex ("Fog", 2D) = "black" {} _Tint ("Tint", Color) = (0.03,0.04,0.06,1) }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off
        ZTest LEqual
        Cull Off
        Offset -2, -2
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; fixed4 _Tint;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            fixed4 frag (v2f i) : SV_Target { fixed a = tex2D(_MainTex, i.uv).a; return fixed4(_Tint.rgb, a); }
            ENDCG
        }
    }
    Fallback Off
}
