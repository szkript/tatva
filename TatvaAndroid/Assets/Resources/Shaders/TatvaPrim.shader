// Premultiplied colours: alpha 0 = additive, alpha > 0 = normal blend.
// uv.x = feather sharpness, uv.y = signed distance across the primitive (-1..1).
Shader "Tatva/Prim"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct a2v { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : TEXCOORD1; float2 uv : TEXCOORD0; };
            v2f vert (a2v v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }
            float4 frag (v2f i) : SV_Target
            {
                float k = saturate((1.0 - abs(i.uv.y)) * i.uv.x);
                return i.color * k;
            }
            ENDCG
        }
    }
}
