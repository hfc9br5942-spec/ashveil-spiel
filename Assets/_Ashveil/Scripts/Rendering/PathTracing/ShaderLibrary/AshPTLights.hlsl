// ASHVEIL Pathtracer – Lichtquellen: Sonne/Mond als Scheibe mit endlicher Winkelgroesse, Himmel mit
// Helligkeits-Importance-Sampling (Alias-Tabelle), lokale Kugellichter (Punkt/Spot, aus URP-Lichtern) und
// leuchtende Dreiecke (Alias-Tabelle nach Flaeche × Leuchtdichte). Auswahl je Schattierungspunkt nach
// geschaetztem Beitrag; dieselben Funktionen liefern die Dichten fuer Multiple Importance Sampling.
// Einheiten: URP-Werte werden so umgerechnet, dass eine weisse Lambert-Flaeche genau so hell wird wie im
// Rasterbild bei gleichem Einfallswinkel (Raster: Albedo × Licht × cos, hier Albedo/π × π·Licht × cos).
#ifndef ASH_PT_LIGHTS_INCLUDED
#define ASH_PT_LIGHTS_INCLUDED

#include "AshPTCommon.hlsl"

struct AshPTLight
{
    float4 posRadius;   // xyz Position (relativ zum RTAS-Ursprung), w Radius (m)
    float4 intensity;   // rgb Strahlstaerke (bereits × π), w Typ (0 Punkt, 1 Spot)
    float4 spotDir;     // xyz Leuchtrichtung, w cos(aeusserer Winkel/2)
    float4 spotParams;  // x cos(innerer Winkel/2), y 1 = fuer Kamera sichtbar
};

struct AshPTEmissiveTri
{
    float4 p0;          // xyz, w Flaeche
    float4 p1;          // xyz, w Auswahlwahrscheinlichkeit
    float4 p2;          // xyz, w Instanz-ID (asfloat)
    float4 info;        // x Primitiv-Index (asfloat), y Leuchtdichte (Luminanz), z Rueckseite leuchtet (0/1)
};

RaytracingAccelerationStructure _AshPTScene;

StructuredBuffer<AshPTLight> _AshPTLights;
StructuredBuffer<AshPTEmissiveTri> _AshPTEmissive;
StructuredBuffer<float4> _AshPTEmissiveAlias;   // x Schwelle, y Alias (asfloat)
StructuredBuffer<uint4> _AshPTInstances;         // x Offset erstes leuchtendes Dreieck (0xFFFFFFFF = keins), y Kategorie
StructuredBuffer<float4> _AshPTEnvAlias;         // x Schwelle, y Alias (asfloat), z Texel-Wahrscheinlichkeit
Texture2D<float4> _AshPTEnvMap;
SamplerState sampler_AshPTEnvMap;

float4 _AshPTSunDir;        // xyz zur Sonne, w cos(Winkelradius)
float4 _AshPTSunRadiance;   // rgb Strahldichte der Scheibe, w 1 = Scheibe sichtbar/treffbar (Sonne), 0 = nur Licht (Mond)
float4 _AshPTEnvParams;     // x Breite, y Hoehe, z Staerke, w Modus (0 Karte, 1 gleichmaessig, 2 aus)
float4 _AshPTEnvUniform;    // rgb gleichmaessige Strahldichte, w Leistung (π·Luminanz)
float4 _AshPTLightCounts;   // x lokale Lichter, y leuchtende Dreiecke, z Gesamtleistung leuchtend, w Sonnen-Bestrahlungsstaerke (Luminanz)
float4 _AshPTEmissiveCenter; // xyz Schwerpunkt der Leuchtflaechen, w Radius

// ------------------------------------------------------------------ Himmel
float2 AshDirToLatLong(float3 d)
{
    float phi = atan2(d.z, d.x);
    float theta = acos(clamp(d.y, -1.0, 1.0));
    return float2(phi * ASH_INV_TWO_PI + 0.5, theta * ASH_INV_PI);
}

float3 AshLatLongToDir(float2 uv)
{
    float phi = (uv.x - 0.5) * ASH_TWO_PI;
    float theta = uv.y * ASH_PI;
    float st = sin(theta);
    return float3(st * cos(phi), cos(theta), st * sin(phi));
}

float3 AshEnvRadiance(float3 d)
{
    int mode = (int)_AshPTEnvParams.w;
    if (mode == 1) return _AshPTEnvUniform.rgb;
    if (mode == 2) return 0;
    float2 uv = AshDirToLatLong(d);
    return _AshPTEnvMap.SampleLevel(sampler_AshPTEnvMap, uv, 0).rgb * _AshPTEnvParams.z;
}

float AshEnvPdf(float3 d)
{
    int mode = (int)_AshPTEnvParams.w;
    if (mode == 1) return 1.0 / (4.0 * ASH_PI);
    if (mode == 2) return 0.0;
    float2 uv = AshDirToLatLong(d);
    uint w = (uint)_AshPTEnvParams.x, h = (uint)_AshPTEnvParams.y;
    uint i = min((uint)(uv.x * w), w - 1), j = min((uint)(uv.y * h), h - 1);
    float p = _AshPTEnvAlias[j * w + i].z;
    float st = sin(uv.y * ASH_PI);
    if (st <= 1e-6) return 0.0;
    return p * (float)(w * h) / (2.0 * ASH_PI * ASH_PI * st);
}

float3 AshSampleEnv(float3 u, out float3 d, out float pdf)
{
    int mode = (int)_AshPTEnvParams.w;
    if (mode == 1)
    {
        float z = 1.0 - 2.0 * u.x;
        float r = sqrt(max(0.0, 1.0 - z * z));
        float phi = ASH_TWO_PI * u.y;
        d = float3(r * cos(phi), z, r * sin(phi));
        pdf = 1.0 / (4.0 * ASH_PI);
        return _AshPTEnvUniform.rgb;
    }
    if (mode == 2) { d = float3(0, 1, 0); pdf = 0; return 0; }
    uint w = (uint)_AshPTEnvParams.x, h = (uint)_AshPTEnvParams.y;
    uint n = w * h;
    float fu = u.x * n;
    uint idx = min((uint)fu, n - 1);
    float4 e = _AshPTEnvAlias[idx];
    if (frac(fu) >= e.x) idx = asuint(e.y);
    uint i = idx % w, j = idx / w;
    float2 uv = float2((i + u.y) / w, (j + u.z) / h);
    d = AshLatLongToDir(uv);
    pdf = AshEnvPdf(d);
    return _AshPTEnvMap.SampleLevel(sampler_AshPTEnvMap, uv, 0).rgb * _AshPTEnvParams.z;
}

// ------------------------------------------------------------------ Kegel (Sonne, Kugellichter)
float3 AshSampleCone(float3 axis, float cosMax, float2 u)
{
    float cosT = 1.0 - u.x * (1.0 - cosMax);
    float sinT = sqrt(max(0.0, 1.0 - cosT * cosT));
    float phi = ASH_TWO_PI * u.y;
    float3 t, b;
    AshBasis(axis, t, b);
    return normalize(t * (sinT * cos(phi)) + b * (sinT * sin(phi)) + axis * cosT);
}

float AshConePdf(float cosMax) { return 1.0 / (ASH_TWO_PI * max(1.0 - cosMax, 1e-9)); }

bool AshSunActive() { return AshMax3(_AshPTSunRadiance.rgb) > 0.0; }

// Strahl trifft die Sonnenscheibe (nur wenn sie als treffbar markiert ist)
float3 AshSunDiskRadiance(float3 d)
{
    if (_AshPTSunRadiance.w < 0.5 || !AshSunActive()) return 0;
    return dot(d, _AshPTSunDir.xyz) >= _AshPTSunDir.w ? _AshPTSunRadiance.rgb : 0;
}

// ------------------------------------------------------------------ lokale Lichter
float AshSpotFactor(AshPTLight L, float3 fromLight)
{
    if (L.intensity.w < 0.5) return 1.0;
    float c = dot(fromLight, L.spotDir.xyz);
    return smoothstep(L.spotDir.w, L.spotParams.x, c);
}

float AshLocalWeight(AshPTLight L, float3 x)
{
    float3 dv = L.posRadius.xyz - x;
    float d2 = max(dot(dv, dv), L.posRadius.w * L.posRadius.w);
    float s = AshSpotFactor(L, normalize(-dv));
    return AshLuminance(L.intensity.rgb) * s / max(d2, 1e-4);
}

// Kugel-Schnitt: kleinstes t > 0
bool AshHitSphere(float3 o, float3 d, float3 c, float r, out float t)
{
    float3 oc = o - c;
    float b = dot(oc, d);
    float cc = dot(oc, oc) - r * r;
    float disc = b * b - cc;
    t = 0;
    if (disc < 0.0) return false;
    float s = sqrt(disc);
    t = -b - s;
    if (t <= 0.0) t = -b + s;
    return t > 0.0;
}

float3 AshLocalRadiance(AshPTLight L, float3 dirToLight)
{
    float r = max(L.posRadius.w, 1e-3);
    // Kugel der Strahlstaerke I leuchtet mit L = I / (π r²)
    return L.intensity.rgb * AshSpotFactor(L, -dirToLight) / (ASH_PI * r * r);
}

// ------------------------------------------------------------------ Kategorienwahl
struct AshLightChoice
{
    float pSun, pEnv, pLocal, pEmissive;
    float localSum;
};

AshLightChoice AshChooseLights(float3 x, float3 n)
{
    AshLightChoice c;
    float wSun = 0, wEnv = 0, wLocal = 0, wEm = 0;
    if (AshSunActive()) wSun = _AshPTLightCounts.w * (0.1 + 0.9 * saturate(dot(n, _AshPTSunDir.xyz)));
    int mode = (int)_AshPTEnvParams.w;
    if (mode != 2) wEnv = _AshPTEnvUniform.w;
    uint nl = (uint)_AshPTLightCounts.x;
    for (uint i = 0; i < nl; i++) wLocal += AshLocalWeight(_AshPTLights[i], x);
    if (_AshPTLightCounts.y > 0)
    {
        float3 dv = _AshPTEmissiveCenter.xyz - x;
        wEm = _AshPTLightCounts.z / max(dot(dv, dv), max(_AshPTEmissiveCenter.w * _AshPTEmissiveCenter.w, 1.0));
    }
    float present = (wSun > 0 ? 1 : 0) + (wEnv > 0 ? 1 : 0) + (wLocal > 0 ? 1 : 0) + (wEm > 0 ? 1 : 0);
    float sum = wSun + wEnv + wLocal + wEm;
    c.localSum = wLocal;
    if (sum <= 0 || present == 0) { c.pSun = c.pEnv = c.pLocal = c.pEmissive = 0; return c; }
    float floorShare = 0.1 / present;   // jede vorhandene Quelle bekommt mindestens etwas → robust, weiterhin erwartungstreu
    c.pSun = wSun > 0 ? 0.9 * wSun / sum + floorShare : 0;
    c.pEnv = wEnv > 0 ? 0.9 * wEnv / sum + floorShare : 0;
    c.pLocal = wLocal > 0 ? 0.9 * wLocal / sum + floorShare : 0;
    c.pEmissive = wEm > 0 ? 0.9 * wEm / sum + floorShare : 0;
    return c;
}

// ------------------------------------------------------------------ leuchtende Dreiecke
uint AshPickEmissive(float u)
{
    uint n = (uint)_AshPTLightCounts.y;
    float fu = u * n;
    uint idx = min((uint)fu, n - 1);
    float4 e = _AshPTEmissiveAlias[idx];
    if (frac(fu) >= e.x) idx = asuint(e.y);
    return idx;
}

// Dichte (Raumwinkel) fuer einen BSDF-Strahl, der ein leuchtendes Dreieck getroffen hat
float AshEmissiveHitPdf(uint instanceId, uint prim, float dist, float cosLight, AshLightChoice c)
{
    if (c.pEmissive <= 0 || cosLight <= 1e-6) return 0;
    uint first = _AshPTInstances[instanceId].x;
    if (first == 0xFFFFFFFFu) return 0;
    AshPTEmissiveTri tri = _AshPTEmissive[first + prim];
    float pArea = tri.p1.w / max(tri.p0.w, 1e-12);
    return c.pEmissive * pArea * dist * dist / cosLight;
}

#endif
