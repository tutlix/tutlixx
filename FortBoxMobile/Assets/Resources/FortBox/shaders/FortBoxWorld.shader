// Buildings, props, trees, vehicles, figures, guns: PC WORLD_FRAG port.
Shader "FortBox/World"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _SpecK ("Specular", Float) = 0
        _Foliage ("Foliage", Float) = 0
        _Translucent ("Translucent (grass)", Float) = 0
        _AlphaCut ("Alpha cut", Float) = 0
        _Sway ("Wind sway", Float) = 0
        _InteriorLight ("Interior light", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #include "FortBoxCommon.cginc"

            sampler2D _MainTex;
            float4 _Tint;
            float _SpecK, _Foliage, _Translucent, _AlphaCut, _Sway;
            float4 _InteriorLight;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos : TEXCOORD0;
                float3 wnrm : TEXCOORD1;
                float4 vcol : COLOR;
                float2 uv : TEXCOORD2;
                SHADOW_COORDS(3)
            };

            v2f vert(appdata v)
            {
                v2f o;
                float4 p = v.vertex;
                if (_Sway > 0.0)
                {
                    // grass / leaves in the wind (PC WORLD_VERT, in world px)
                    float3 q = p.xyz * FB_PX;
                    float k = max(0.0, q.y) / 14.0;
                    float t = _Time.y;
                    float gust = 0.6 + 0.4 * sin(t * 0.7 + q.x * 0.0021 + q.z * 0.0017);
                    q.x += (sin(t * 2.1 + q.x * 0.031 - q.z * 0.017) * 1.6 + 2.2 * gust) * k * _Sway;
                    q.z -= cos(t * 1.7 - q.z * 0.027 - q.x * 0.011) * 1.4 * k * _Sway;
                    p.xyz = q / FB_PX;
                }
                o.pos = UnityObjectToClipPos(p);
                o.wpos = mul(unity_ObjectToWorld, p).xyz;
                o.wnrm = UnityObjectToWorldNormal(v.normal);
                o.vcol = v.color;
                o.uv = v.uv;
                TRANSFER_SHADOW(o);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 base = tex2D(_MainTex, i.uv) * i.vcol * _Tint;
                if (base.a < _AlphaCut) discard;
                float3 alb = fb_to_lin(base.rgb);
                if (_Translucent > 0.5) alb *= 2.0;
                float3 n = normalize(i.wnrm);
                if (_Translucent > 0.5) n = normalize(float3(n.x * 0.3, 1.0, n.z * 0.3));
                float ndl = dot(n, _FbLightDir);
                float wrap = _Translucent > 0.5 ? max(ndl * 0.6 + 0.4, 0.0) : max(ndl, 0.0);
                if (_Foliage > 0.5) wrap = max(ndl * 0.55 + 0.38, 0.0);
                float sh = SHADOW_ATTENUATION(i);
                float3 bounce = (_FbAmbSky * 0.6 + _FbLightCol * max(_FbLightDir.y, 0.0) * 0.12) * _FbGroundBounce;
                float3 amb = lerp(_FbAmbGround + bounce, _FbAmbSky, n.y * 0.5 + 0.5);
                float ao = lerp(0.55, 1.0, saturate(i.wpos.y * FB_PX / 30.0 + 0.35));
                float3 col = alb * (amb * ao * 1.15 + _FbLightCol * wrap * sh + _InteriorLight.rgb);
                float3 V = normalize(_WorldSpaceCameraPos - i.wpos);
                if (_Translucent < 0.5 && _Foliage < 0.5)
                {
                    float fr = pow(1.0 - max(dot(n, V), 0.0), 4.0);
                    col += _FbAmbSky * fr * 0.55 * (0.4 + 0.6 * sh);
                }
                if (_SpecK > 0.0)
                {
                    float3 h = normalize(V + _FbLightDir);
                    col += _FbLightCol * pow(max(dot(n, h), 0.0), 48.0) * _SpecK * sh;
                }
                return float4(fb_tonemap(fb_atmosphere(col, i.wpos)), 1.0);
            }
            ENDCG
        }
        // shadow caster (alpha-cut leaves cast leaf-shaped shadows)
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _AlphaCut;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 texcoord : TEXCOORD0; float4 color : COLOR; };
            struct v2f { V2F_SHADOW_CASTER; float2 uv : TEXCOORD1; float a : TEXCOORD2; };
            v2f vert(appdata v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                o.uv = v.texcoord;
                o.a = v.color.a;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                if (_AlphaCut > 0.0) clip(tex2D(_MainTex, i.uv).a * i.a - _AlphaCut);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    Fallback Off
}
