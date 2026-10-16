// Opaque track geometry with neon edges.
// color.rgb = face colour (fog already mixed in on the CPU), color.a = edge visibility (1 - fog).
// uv = face coordinates; uv2.x = edge mode (0: quad, uv in 0..1 on both axes; 1: triangle, uv = two barycentrics;
// 2: lateral edges only, u in 0..1), uv2.y = edge strength. Edges are ~2 px wide at any distance (fwidth).
Shader "Tatva/Solid"
{
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Cull Off ZWrite On ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct a2v { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float2 e : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float4 color : TEXCOORD2; float2 uv : TEXCOORD0; float2 e : TEXCOORD1; };
            v2f vert (a2v v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                o.e = v.e;
                return o;
            }
            float4 frag (v2f i) : SV_Target
            {
                float d;
                if (i.e.x < 0.5) d = min(min(i.uv.x, 1.0 - i.uv.x), min(i.uv.y, 1.0 - i.uv.y));
                else if (i.e.x < 1.5) d = min(min(i.uv.x, i.uv.y), 1.0 - i.uv.x - i.uv.y);
                else d = min(i.uv.x, 1.0 - i.uv.x);
                float px = d / max(fwidth(d), 1e-6);
                float edge = saturate(1.8 - px * 0.7) * i.e.y * i.color.a;
                float3 c = i.color.rgb;
                return float4(lerp(c, c * 1.7 + 0.24, edge), 1);
            }
            ENDCG
        }
    }
}
