// Flat unlit color, used for fire and debug markers.
Shader "Oathmarch/UnlitColor"
{
    Properties { _Color ("Color", Color) = (1,0.5,0,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            fixed4 frag () : SV_Target { return _Color; }
            ENDCG
        }
    }
}
