// Shared lighting for the FortBox shaders - a straight port of the PC game's
// panda_sky.py GLSL (_COMMON): same sun / ambient / fog / ACES tone map, so
// the phone renders the maps with the same colours. Units: 1 = 25 world px.
#ifndef FORTBOX_COMMON
#define FORTBOX_COMMON

float3 _FbSunDir;        // toward the sun (Unity axes)
float3 _FbLightDir;      // toward the light that casts shadows
float3 _FbLightCol;      // linear
float3 _FbAmbSky;
float3 _FbAmbGround;
float3 _FbFogCol;        // linear
float3 _FbGlowCol;
float4 _FbFogRange;      // start, end (units); y <= 0: no fog
float _FbExposure;
float _FbGroundBounce;

#define FB_PX 25.0

float3 fb_to_lin(float3 c) { return pow(max(c, 0.0), 2.2); }

float3 fb_tonemap(float3 x)
{
    x *= _FbExposure;
    float3 a = (x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14);
    return pow(saturate(a), 1.0 / 2.2);
}

float3 fb_atmosphere(float3 col, float3 wpos)
{
    if (_FbFogRange.y <= 0.0) return col;
    float3 d = wpos - _WorldSpaceCameraPos;
    float dist = length(d);
    float3 dir = d / max(dist, 0.001);
    float dens = 0.75 / max(1.0 / FB_PX, _FbFogRange.y - _FbFogRange.x);
    float f = 1.0 - exp(-max(0.0, dist - _FbFogRange.x) * dens);
    f *= lerp(1.0, 0.55, smoothstep(0.0, 900.0 / FB_PX, wpos.y));
    float s = pow(max(dot(dir, _FbSunDir), 0.0), 6.0);
    float3 fc = (_FbFogCol + _FbGlowCol * s * 0.6) * 3.1;
    return lerp(col, fc, saturate(f));
}

#endif
