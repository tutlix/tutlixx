// Ground: PC GROUND_FRAG port - the map's 2D ground texture with grass,
// procedural materials (slabs, granite, cobbles, snow, sand, cracked desert,
// concrete), asphalt grain, wet puddles with sky / neon reflections.
// Patterns run in world px (w) exactly like the PC shader.
Shader "FortBox/Ground"
{
    Properties
    {
        _GroundTex ("Ground (2D map)", 2D) = "gray" {}
        _GrassTex ("Grass", 2D) = "green" {}
        _DetailTex ("Detail", 2D) = "gray" {}
        _NoiseTex ("Noise", 2D) = "gray" {}
        _MapSize ("Map size px", Vector) = (3200, 1716, 0, 0)
        _OutsideCol ("Outside colour", Color) = (0.3, 0.55, 0.24, 1)
        _OutsideGrass ("Outside is grass", Float) = 1
        _GrassOn ("Grass on", Float) = 1
        _GrassRef ("Grass ref luminance", Float) = 0.5
        _GrassTint ("Grass tint", Color) = (1,1,1,1)
        _BaseCol ("Material base colour", Color) = (0.5,0.5,0.5,1)
        _BaseMat ("Material id", Float) = 0
        _Wet ("Wet", Float) = 0
        _Neon ("Neon", Float) = 0
        _NeonCol ("Neon colour", Color) = (1, 0.4, 0.9, 1)
        _Detail ("Detail quality", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-10" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #include "FortBoxCommon.cginc"

            sampler2D _GroundTex, _GrassTex, _DetailTex, _NoiseTex;
            float4 _MapSize, _OutsideCol, _GrassTint, _BaseCol, _NeonCol;
            float _OutsideGrass, _GrassOn, _GrassRef, _BaseMat, _Wet, _Neon, _Detail;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 gnrm : TEXCOORD1; SHADOW_COORDS(2) };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.gnrm = UnityObjectToWorldNormal(v.normal);
                TRANSFER_SHADOW(o);
                return o;
            }

            float lum(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
            float hash2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float2 hash22(float2 p) { return frac(sin(float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)))) * 43758.5453); }
            float gmod(float x, float y) { return x - y * floor(x / y); }

            float3 voro(float2 x)
            {
                float2 n = floor(x), f = frac(x);
                float m1 = 8.0, m2 = 8.0;
                float2 id = n;
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        float2 g = float2((float)i, (float)j);
                        float2 r = g + hash22(n + g) * 0.85 + 0.075 - f;
                        float d = dot(r, r);
                        if (d < m1) { m2 = m1; m1 = d; id = n + g; }
                        else if (d < m2) { m2 = d; }
                    }
                return float3(sqrt(m1), sqrt(m2) - sqrt(m1), hash2(id));
            }
            float nz(float2 w, float sc) { return tex2D(_NoiseTex, w / sc).r; }
            float2 slabs(float2 w, float2 size, float stagger)
            {
                float2 q = w / size;
                q.x += stagger * gmod(floor(q.y), 2.0);
                float2 c = floor(q), f = frac(q);
                float2 d = min(f, 1.0 - f) * size;
                return float2(min(d.x, d.y), hash2(c));
            }

            float4 material(float2 w, int id, float far)
            {
                float3 base = _BaseCol.rgb;
                float3 c = base;
                float h = 0.5;
                if (id == 1)
                {
                    float2 s = slabs(w, float2(26.0, 26.0), 0.0);
                    float j = smoothstep(0.0, 1.1, s.x);
                    float tone = 0.9 + 0.17 * s.y + 0.12 * (nz(w, 520.0) - 0.5);
                    c *= tone * lerp(0.58, 1.0, j);
                    c *= 1.0 - 0.14 * smoothstep(0.68, 0.84, nz(w + 31.0, 140.0));
                    h = 0.55 * j + 0.1 * tex2D(_DetailTex, w / 11.0).r;
                }
                else if (id == 2)
                {
                    float2 s = slabs(w, float2(46.0, 46.0), 0.5);
                    float j = smoothstep(0.0, 0.8, s.x);
                    float speck = tex2D(_DetailTex, w / 5.0).r;
                    c *= (0.8 + 0.34 * s.y) * lerp(0.42, 1.0, j) * (0.88 + 0.24 * speck);
                    h = 0.6 * j;
                }
                else if (id == 3)
                {
                    float3 v = voro(w / 4.4);
                    float gap = smoothstep(0.04, 0.24, v.y);
                    float dome = 1.0 - v.x * 0.55;
                    c *= lerp(float3(0.58, 0.54, 0.48), float3(0.78 + 0.42 * v.z, 0.76 + 0.4 * v.z, 0.72 + 0.4 * v.z), gap);
                    c *= 0.96 + 0.08 * (nz(w, 380.0) - 0.5) * 2.0;
                    h = gap * (0.45 + 0.55 * dome);
                }
                else if (id == 4)
                {
                    float n1 = nz(w, 340.0), n2 = nz(w + 17.0, 90.0), n3 = nz(w + 3.0, 24.0);
                    h = n1 * 0.6 + n2 * 0.3 + n3 * 0.1;
                    c *= 0.95 + 0.07 * h;
                    c *= lerp(0.9, 1.0, smoothstep(0.25, 0.5, nz(w + 9.0, 700.0)));
                }
                else if (id == 5)
                {
                    float d = dot(w, float2(0.8, 0.6)) + 28.0 * nz(w, 420.0);
                    float r = 0.5 + 0.5 * sin(d / 4.6);
                    float grain = tex2D(_DetailTex, w / 6.0).r;
                    h = r * 0.6 + grain * 0.15;
                    c *= (0.92 + 0.1 * r) * (0.9 + 0.2 * grain) * (0.94 + 0.12 * nz(w, 900.0));
                }
                else if (id == 6)
                {
                    float2 wq = w + 6.0 * float2(nz(w, 90.0), nz(w + 13.0, 90.0));
                    float3 v = voro(wq / 15.0);
                    float crack = (1.0 - smoothstep(0.0, 0.035, v.y)) * (0.55 + 0.45 * smoothstep(0.3, 0.6, nz(w, 160.0)));
                    float3 p = voro(w / 3.2);
                    float pebble = smoothstep(0.42, 0.2, p.x) * step(0.82, p.z);
                    c *= (0.93 + 0.12 * v.z) * lerp(1.0, 0.62, crack) * (0.9 + 0.2 * tex2D(_DetailTex, w / 7.0).r)
                         * (0.94 + 0.12 * nz(w + 50.0, 700.0));
                    c = lerp(c, base * float3(0.78, 0.74, 0.7), pebble * 0.8);
                    h = (1.0 - crack) * 0.6 + pebble * 0.3;
                }
                else if (id == 7)
                {
                    float2 s = slabs(w, float2(92.0, 92.0), 0.0);
                    float j = smoothstep(0.0, 1.4, s.x);
                    float3 v = voro(w / 58.0);
                    float crk = (1.0 - smoothstep(0.0, 0.025, v.y)) * smoothstep(0.62, 0.8, nz(w + 5.0, 300.0));
                    float oil = smoothstep(0.78, 0.88, nz(w + 71.0, 70.0)) * 0.55;
                    c *= (0.88 + 0.18 * s.y) * lerp(0.5, 1.0, j) * lerp(1.0, 0.6, crk) * lerp(1.0, 0.62, oil)
                         * (0.9 + 0.2 * tex2D(_DetailTex, w / 13.0).r);
                    h = 0.55 * j * (1.0 - crk * 0.6);
                }
                c = lerp(base * 0.97, c, far);
                return float4(c, lerp(0.5, h, far));
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 w = float2(i.wpos.x, -i.wpos.z) * FB_PX;          // world px (y down)
                float2 guv = w / _MapSize.xy;
                bool inside = guv.x >= 0.0 && guv.x <= 1.0 && guv.y >= 0.0 && guv.y <= 1.0;
                float3 m = inside ? tex2D(_GroundTex, float2(guv.x, 1.0 - guv.y)).rgb : _OutsideCol.rgb;
                float greenness = m.g - max(m.r, m.b);
                float gmask = _GrassOn * smoothstep(0.03, 0.09, greenness);
                if (!inside) gmask = _GrassOn * _OutsideGrass;
                float2 p1 = w / 150.0;
                float2 p2 = float2(0.8 * w.x + 0.6 * w.y, -0.6 * w.x + 0.8 * w.y) / 61.0;
                float4 nzA = tex2D(_NoiseTex, w / 2400.0);
                float4 nzB = tex2D(_NoiseTex, w / 640.0 + 0.37);
                float3 g1 = tex2D(_GrassTex, p1).rgb;
                float3 g2 = tex2D(_GrassTex, p2).rgb;
                float3 grass = lerp(g1, g2, 0.35 + 0.3 * nzB.b);
                float3 dryc = float3(0.62, 0.60, 0.36);
                grass = lerp(grass, grass * dryc * 1.9, smoothstep(0.55, 0.85, nzA.r) * 0.35);
                grass *= lerp(0.78, 1.18, nzA.g) * lerp(0.9, 1.08, nzB.a);
                float rel = inside ? clamp(lum(m) / max(0.05, _GrassRef), 0.72, 1.25) : 1.0;
                grass = lerp(float3(lum(grass), lum(grass), lum(grass)), grass, 0.84);
                grass *= rel * _GrassTint.rgb * float3(1.42, 1.4, 1.3);
                float dt = tex2D(_DetailTex, w / 90.0).r * 0.6 + tex2D(_DetailTex, w / 23.0).r * 0.4;
                float3 other = m * (0.55 + 0.9 * dt) * lerp(0.9, 1.08, nzA.g);

                int mid = (int)(_BaseMat + 0.5);
                float3 gn = normalize(i.gnrm);
                float3 n = gn;
                float isb = 0.0, asph = 0.0, hgt = 0.5;
                float px = max(fwidth(w.x), fwidth(w.y)) / max(_Detail, 0.25);
                if (mid > 0)
                {
                    isb = inside ? 1.0 - smoothstep(0.012, 0.034, length(m - _BaseCol.rgb)) : 1.0 - gmask;
                    float sat = max(m.r, max(m.g, m.b)) - min(m.r, min(m.g, m.b));
                    asph = (1.0 - isb) * (1.0 - gmask) * (1.0 - smoothstep(0.29, 0.36, lum(m))) * (1.0 - smoothstep(0.05, 0.11, sat));
                    if (isb > 0.001)
                    {
                        float period = mid == 1 ? 26.0 : mid == 2 ? 46.0 : mid == 3 ? 4.4 : mid == 6 ? 15.0 : mid == 7 ? 60.0 : 30.0;
                        float far = 1.0 - smoothstep(period * 0.1, period * 0.32, px);
                        float4 a = material(w, mid, far);
                        float e = 0.6;
                        float hx = material(w + float2(e, 0.0), mid, far).a - a.a;
                        float hy = material(w + float2(0.0, e), mid, far).a - a.a;
                        float bump = mid == 3 ? 1.6 : mid == 6 ? 1.0 : mid == 5 ? 0.9 : mid == 4 ? 14.0 : 1.2;
                        // PC: normal += (-hx, hy, 0) in Panda axes (x, y, up) -> Unity (x, up, z = Panda y)
                        float3 nb = normalize(gn + float3(-hx, 0.0, hy) * bump / e);
                        n = normalize(lerp(n, nb, isb));
                        hgt = a.a;
                        float3 matc = inside ? a.rgb * (m / max(_BaseCol.rgb, float3(0.02, 0.02, 0.02))) : a.rgb;
                        other = lerp(other, matc, isb);
                    }
                    if (asph > 0.001)
                    {
                        float ag = tex2D(_DetailTex, w / 7.0).r * 0.6 + tex2D(_DetailTex, w / 2.3).r * 0.4;
                        float3 v = voro(w / 140.0);
                        float seam = (1.0 - smoothstep(0.0, 0.012, v.y)) * smoothstep(0.5, 0.7, nz(w, 260.0));
                        float far = 1.0 - smoothstep(1.0, 4.0, px);
                        float3 ac = m * (0.78 + 0.44 * ag) * lerp(1.0, 0.55, seam) * lerp(0.94, 1.05, nz(w + 3.0, 600.0));
                        other = lerp(other, lerp(m * 0.98, ac, far), asph);
                        hgt = lerp(hgt, 0.5 + 0.2 * ag - seam * 0.4, asph);
                    }
                }
                float3 alb = fb_to_lin(lerp(other, grass, gmask));
                float sh = SHADOW_ATTENUATION(i);
                float ndl = max(dot(n, _FbLightDir), 0.0);
                float relief = lerp(0.86, 1.1, lum(grass) * 2.2) * gmask + (1.0 - gmask);
                float hard = (mid > 0 && mid != 4 && mid != 5 && mid != 6) ? (isb + asph) * (1.0 - gmask) : 0.0;
                float3 Vw = normalize(_WorldSpaceCameraPos - i.wpos);
                float damp = _Wet * hard * (1.0 - smoothstep(0.55, 0.8, Vw.y));
                float pud = 0.0;
                if (damp > 0.001)
                {
                    float pn = nz(w + 41.0, 260.0) * 0.7 + nz(w + 7.0, 70.0) * 0.3;
                    pud = smoothstep(0.64, 0.7, pn + (0.5 - hgt) * 0.25 + (_Wet - 0.5) * 0.1) * damp;
                    alb *= lerp(1.0, 0.72, damp * 0.8);
                    alb *= 1.0 - pud * 0.45;
                    n = normalize(lerp(n, gn, pud));
                }
                float3 col = alb * (lerp(_FbAmbGround, _FbAmbSky, n.y * 0.5 + 0.5) * 1.05 + _FbLightCol * ndl * sh * relief);
                if (damp > 0.001)
                {
                    float3 R = reflect(-Vw, n);
                    float fres = 0.03 + 0.97 * pow(1.0 - max(dot(n, Vw), 0.0), 5.0);
                    float sk = pow(max(dot(R, _FbSunDir), 0.0), 8.0);
                    float3 refl = (_FbFogCol * lerp(0.35, 0.95, smoothstep(0.02, 0.35, R.y)) + _FbGlowCol * sk * 0.6) * 3.1;
                    float rip = nz(w * 3.0 + 11.0, 40.0);
                    refl *= 0.75 + 0.35 * rip;
                    refl = lerp(refl, refl * _NeonCol.rgb * 2.2, _Neon * smoothstep(0.45, 0.75, nz(w + 99.0, 160.0)));
                    refl += _FbLightCol * pow(max(dot(R, _FbLightDir), 0.0), 420.0) * 6.0 * sh;
                    float rk = saturate(pud * 0.85 + damp * 0.18) * fres;
                    col = lerp(col, refl, rk);
                }
                if (mid == 4 && isb > 0.0)
                {
                    float gl = step(0.992, hash2(floor(w * 1.6)));
                    float3 R = reflect(-Vw, n);
                    col += _FbLightCol * gl * pow(max(dot(R, _FbLightDir), 0.0), 24.0) * 3.0 * sh * isb * (1.0 - smoothstep(0.6, 2.0, px));
                    col *= lerp(float3(0.9, 0.95, 1.08), float3(1.0, 1.0, 1.0), sh);
                }
                if (!inside) col *= 0.97;
                return float4(fb_tonemap(fb_atmosphere(col, i.wpos)), 1.0);
            }
            ENDCG
        }
        // depth only (camera depth texture / screen-space shadow receiving)
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"
            struct v2f { V2F_SHADOW_CASTER; };
            v2f vert(appdata_base v) { v2f o; TRANSFER_SHADOW_CASTER_NORMALOFFSET(o) return o; }
            float4 frag(v2f i) : SV_Target { SHADOW_CASTER_FRAGMENT(i) }
            ENDCG
        }
    }
    Fallback Off
}
