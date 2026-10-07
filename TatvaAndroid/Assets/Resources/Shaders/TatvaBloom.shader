Shader "Hidden/TatvaBloom"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex; float4 _MainTex_TexelSize;
    sampler2D _Bloom1; sampler2D _Bloom2;
    float _Int1, _Int2, _Spread;
    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

    float4 fragDown (v2f i) : SV_Target
    {
        float2 d = _MainTex_TexelSize.xy;
        float3 c = tex2D(_MainTex, i.uv + float2(-d.x, -d.y)).rgb + tex2D(_MainTex, i.uv + float2(d.x, -d.y)).rgb
                 + tex2D(_MainTex, i.uv + float2(-d.x, d.y)).rgb + tex2D(_MainTex, i.uv + float2(d.x, d.y)).rgb;
        return float4(c * 0.25, 1);
    }
    float3 blur (float2 uv, float2 dir)
    {
        float2 o1 = dir * 1.3846153846 * _Spread, o2 = dir * 3.2307692308 * _Spread;
        return tex2D(_MainTex, uv).rgb * 0.2270270270
             + (tex2D(_MainTex, uv + o1).rgb + tex2D(_MainTex, uv - o1).rgb) * 0.3162162162
             + (tex2D(_MainTex, uv + o2).rgb + tex2D(_MainTex, uv - o2).rgb) * 0.0702702703;
    }
    float4 fragBlurH (v2f i) : SV_Target { return float4(blur(i.uv, float2(_MainTex_TexelSize.x, 0)), 1); }
    float4 fragBlurV (v2f i) : SV_Target { return float4(blur(i.uv, float2(0, _MainTex_TexelSize.y)), 1); }
    float4 fragComp (v2f i) : SV_Target
    {
        float3 c = tex2D(_MainTex, i.uv).rgb;
        c += tex2D(_Bloom1, i.uv).rgb * _Int1 + tex2D(_Bloom2, i.uv).rgb * _Int2;
        return float4(c, 1);
    }
    ENDCG
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment fragDown
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment fragBlurH
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment fragBlurV
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert
               #pragma fragment fragComp
               ENDCG }
    }
}
