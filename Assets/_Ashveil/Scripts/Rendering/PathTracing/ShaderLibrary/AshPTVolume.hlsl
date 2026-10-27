// ASHVEIL Pathtracer – Volumen V1 (§45–49): Hoehennebel + gleichmaessiger Dunst als echtes Medium.
// Dichte sigma_t(h) = a·exp(-b·(h - h0)) + c (dieselben Werte wie der Raster-Nebel des AtmosphereControllers).
// Durchlaessigkeit analytisch fuer jedes Strahlstueck (auch Schattenstrahlen), Einfachstreuung auf dem Kamerastrahl:
// Abstand proportional zu sigma·T gewaehlt, dort Sonne (mit Schatten → Lichtstrahlen aus Verdeckung, kein Overlay),
// Himmel (eine Probe, mit Sichtbarkeit) und ein lokales Licht (Feuer im Nebel). Henyey-Greenstein-Phase.
#ifndef ASH_PT_VOLUME_INCLUDED
#define ASH_PT_VOLUME_INCLUDED

#include "AshPTCommon.hlsl"

float4 _AshPTFog;      // x Dichte am Boden a (1/m), y Hoehenabnahme b (1/m), z Basishoehe h0 (relativ), w Dunst c (1/m)
float4 _AshPTFog2;     // x Streu-Albedo, y Phase g, z Himmelsabstand (m), w 1 = an
float4 _AshPTFogTint;  // rgb Faerbung der Streuung

bool AshFogOn() { return _AshPTFog2.w > 0.5 && (_AshPTFog.x > 0 || _AshPTFog.w > 0); }

// Optische Tiefe entlang o + d·s, s in [0, t]
float AshFogOpticalDepth(float3 o, float3 d, float t)
{
    float a = _AshPTFog.x, b = max(_AshPTFog.y, 1e-6), c = _AshPTFog.w;
    float h = o.y - _AshPTFog.z;
    float k = b * d.y;
    float heightPart;
    if (abs(k) > 1e-5)
        heightPart = a * exp(-b * h) * (1.0 - exp(-k * t)) / k;
    else
        heightPart = a * exp(-b * h) * t;
    return max(0.0, heightPart) + c * t;
}

float AshFogTransmittance(float3 o, float3 d, float t)
{
    if (!AshFogOn()) return 1.0;
    return exp(-AshFogOpticalDepth(o, d, min(t, _AshPTFog2.z)));
}

// Richtung ins Unendliche (Sonne, Himmel): nur die Bodennebel-Schicht; der gleichmaessige Dunst ist in der
// Sonnenfarbe des SkyDirectors (Atmosphaeren-Durchlaessigkeit) schon enthalten – sonst doppelt gedaempft.
float AshFogTransmittanceSky(float3 o, float3 d)
{
    if (!AshFogOn()) return 1.0;
    float a = _AshPTFog.x, b = max(_AshPTFog.y, 1e-6);
    float h = o.y - _AshPTFog.z;
    float k = b * d.y;
    float tau = k > 1e-4 ? a * exp(-b * h) / k : a * exp(-b * h) * _AshPTFog2.z;
    return exp(-max(tau, 0.0));
}

float AshPhaseHG(float cosT, float g)
{
    float g2 = g * g;
    float denom = 1.0 + g2 - 2.0 * g * cosT;
    return (1.0 - g2) / (4.0 * ASH_PI * denom * sqrt(max(denom, 1e-6)));
}

// Abstand s* mit Dichte sigma(s)·T(s) / (1 - T(t)) waehlen (Bisektion auf der optischen Tiefe)
float AshFogSampleDistance(float3 o, float3 d, float t, float u, out float oneMinusT)
{
    float tauT = AshFogOpticalDepth(o, d, t);
    oneMinusT = 1.0 - exp(-tauT);
    float target = -log(max(1.0 - u * oneMinusT, 1e-7));
    float lo = 0.0, hi = t;
    [unroll] for (int i = 0; i < 14; i++)
    {
        float mid = 0.5 * (lo + hi);
        if (AshFogOpticalDepth(o, d, mid) < target) lo = mid; else hi = mid;
    }
    return 0.5 * (lo + hi);
}

#endif
