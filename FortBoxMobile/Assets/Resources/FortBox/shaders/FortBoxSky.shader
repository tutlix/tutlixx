// Sky dome: PC SKY_FRAG port (gradient, sun scattering, sun disk, two
// drifting cloud layers with self-shadowing). Drawn on an inside-out sphere
// that follows the camera.
Shader "FortBox/Sky"
{
    Properties
    {
        _NoiseTex ("Noise", 2D) = "gray" {}
        _Zenith ("Zenith", Color) = (0.05, 0.16, 0.45, 1)
        _Horizon ("Horizon", Color) = (0.23, 0.30, 0.42, 1)
        _SunCol ("Sun colour", Color) = (1, 0.95, 0.88, 1)
        _SunInt ("Sun intensity", Float) = 2.15
        _Golden ("Golden hour", Float) = 0
        _Cover ("Cloud cover", Float) = 0.4
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Front
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "FortBoxCommon.cginc"
            sampler2D _NoiseTex;
            float4 _Zenith, _Horizon, _SunCol;
            float _SunInt, _Golden, _Cover;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert(float4 v : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v);
                o.dir = v.xyz;
                return o;
            }
            float fbm(float2 p)
            {
                float a = 0.5, s = 0.0;
                for (int i = 0; i < 5; i++)
                {
                    s += a * tex2Dlod(_NoiseTex, float4(p, 0, 0)).r;
                    p = float2(1.6 * p.x - 1.2 * p.y, 1.2 * p.x + 1.6 * p.y) + 0.17;
                    a *= 0.5;
                }
                return s;
            }
            float4 frag(v2f i) : SV_Target
            {
                const float SKY_K = 3.1;
                float3 d = normalize(i.dir);
                float e = d.y;
                float up = pow(max(e, 0.0), 0.42);
                float3 col = lerp(_Horizon.rgb, _Zenith.rgb, up) * 1.22;
                col = lerp(col, _Horizon.rgb * 0.82, smoothstep(0.0, -0.25, e));
                float cs = max(dot(d, _FbSunDir), 0.0);
                float lowsun = 1.0 - smoothstep(0.0, 0.5, _FbSunDir.y);
                col += _SunCol.rgb * _SunInt * (pow(cs, 6.0) * (0.05 + 0.18 * lowsun) + pow(cs, 60.0) * 0.25);
                col += _SunCol.rgb * _Golden * pow(cs, 2.0) * 0.18 * (1.0 - smoothstep(0.0, 0.45, abs(e)));
                float sun_disk = smoothstep(0.99982, 0.99992, cs);
                float3 sun_c = _SunCol.rgb * _SunInt * 18.0 * sun_disk * smoothstep(-0.02, 0.01, e);
                float cl = 0.0;
                float3 cloud_col = float3(0, 0, 0);
                if (e > 0.0)
                {
                    float t = _Time.y;
                    float2 p = d.xz / (e + 0.12);
                    float2 wind = float2(t * 0.0045, t * 0.0016);
                    float n1 = fbm(p * 0.09 + wind);
                    float n2 = fbm(p * 0.21 - wind * 1.7 + 4.3);
                    float thr = lerp(0.86, 0.36, _Cover);
                    float dens = smoothstep(thr, thr + 0.12, n1 * 0.78 + n2 * 0.35);
                    float hi = smoothstep(0.62, 0.95, fbm(p * 0.05 + float2(t * 0.002, 0.0) + 9.0)) * 0.35 * (1.0 - _Cover * 0.5);
                    float2 ts = normalize(_FbSunDir.xz + 0.0001) * 0.06;
                    float n3 = fbm((p + ts * 3.0) * 0.09 + wind);
                    float shade = clamp(1.0 - (n3 * 0.78 + n2 * 0.35 - thr) * 1.6, 0.25, 1.0);
                    float silver = pow(cs, 8.0) * (1.0 - dens) * 2.5;
                    float3 lightc = _SunCol.rgb * _SunInt * (0.32 * shade + silver * 0.5) + _FbAmbSky * 1.6;
                    float g = dot(lightc, float3(0.33, 0.33, 0.33)) * 0.8;
                    lightc = lerp(lightc, float3(g, g, g), _Cover * 0.55);
                    lightc = max(lightc, float3(0.006, 0.008, 0.016));
                    cl = saturate(dens + hi * (1.0 - dens)) * smoothstep(0.0, 0.16, e);
                    cloud_col = lightc * lerp(1.0, 0.7, dens * _Cover);
                }
                col = lerp(col, cloud_col, cl * 0.96);
                col += sun_c * (1.0 - cl);
                return float4(fb_tonemap(col * SKY_K / 1.22), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
