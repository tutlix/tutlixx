// Glowing parts (lights, visors, neon, emblems): PC "unlit" path.
Shader "FortBox/Glow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Pulse ("Pulse", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "FortBoxCommon.cginc"
            sampler2D _MainTex;
            float4 _Tint;
            float _Pulse;
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float4 vcol : COLOR; float2 uv : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.vcol = v.color;
                o.uv = v.uv;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float4 base = tex2D(_MainTex, i.uv) * i.vcol * _Tint;
                float3 alb = fb_to_lin(base.rgb) * _Pulse;
                return float4(fb_tonemap(fb_atmosphere(alb * 2.2, i.wpos)), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
