// ASHVEIL Flora-Validation – gemeinsame Funktionen (PROTOTYPE / DIAGNOSTIC, keine finale Shaderproduktion).
// Wind aus dem Vertexattribut _WIND (TEXCOORD2) = (Stammbiegung, Astflex, Phase, Blattflattern),
// Saison aus _SEASON (TEXCOORD3) = (Verfaerbungsreihenfolge, Laubfall-Schwelle, Herbstfarbton, Laubmarke).
// Steuerung ausschliesslich ueber globale Parameter (GPU-basiert, instancing- und SRP-Batcher-kompatibel):
//   _FV_WindDir    xyz Richtung (Welt), w Staerke 0..1
//   _FV_WindParams x Zeit, y Boeenanteil, z Flatterfaktor, w Astfaktor
//   _FV_SeasonParams x Saison 0..1, y Laubfall 0..1, z Schnee 0..1, w regionale Streuung
#ifndef ASHVEIL_FLORA_COMMON_INCLUDED
#define ASHVEIL_FLORA_COMMON_INCLUDED

float4 _FV_WindDir;
float4 _FV_WindParams;
float4 _FV_SeasonParams;

float3 FV_WindOffsetOS(float4 w, float3 positionOS, float3 objectPosWS)
{
    float strength = _FV_WindDir.w;
    if (strength <= 0.0001) return float3(0, 0, 0);
    float3 dirOS = normalize(TransformWorldToObjectDir(normalize(_FV_WindDir.xyz)));
    float3 sideOS = normalize(cross(dirOS, float3(0, 1, 0)) + float3(1e-4, 0, 0));
    float t = _FV_WindParams.x;
    float ph0 = objectPosWS.x * 0.37 + objectPosWS.z * 0.23;
    // Stamm: langsame Grundneigung + Boee (w.x = (h/H)^1.6)
    float gust = 1.0 + _FV_WindParams.y * (0.5 * sin(t * 0.31 + ph0 * 0.05) + 0.5 * sin(t * 0.73 + ph0 * 0.11));
    float trunk = 0.55 + 0.30 * sin(t * 0.85 + ph0) + 0.15 * sin(t * 2.1 + ph0 * 1.7);
    float3 off = dirOS * strength * gust * w.x * 0.75 * trunk;
    // Haupt-/Feinast: eigene Phase je Ast (w.z), Amplitude ueber Astflex (w.y)
    float br = sin(t * 1.9 + w.z * 6.2832) * 0.6 + sin(t * 3.7 + w.z * 11.0) * 0.4;
    off += (dirOS * 0.6 + sideOS * 0.5 * sin(t * 1.3 + w.z * 9.0)) * strength * gust * w.y * 0.16 * _FV_WindParams.w * (0.55 + 0.45 * br);
    off.y += -strength * w.y * w.y * 0.05 * (1.0 + 0.5 * br);
    // Blatt-/Nadelflattern: hochfrequent, sehr klein
    float fl = sin(t * 9.0 + w.z * 37.0 + positionOS.x * 3.1 + positionOS.z * 2.7 + positionOS.y * 1.3);
    off += float3(0.25, 1.0, 0.25) * strength * w.w * 0.035 * _FV_WindParams.z * fl;
    return off;
}

float3 FV_ObjectPositionWS()
{
    return float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
}

half3 FV_Autumn(half h)
{
    half3 a = half3(0.62, 0.50, 0.16), b = half3(0.66, 0.34, 0.10), c = half3(0.46, 0.16, 0.07), d = half3(0.36, 0.26, 0.14);
    return h < 0.33 ? lerp(a, b, h / 0.33) : (h < 0.66 ? lerp(b, c, (h - 0.33) / 0.33) : lerp(c, d, (h - 0.66) / 0.34));
}

// Regionale Streuung: Baeume in derselben Gegend verfaerben nicht exakt gleichzeitig (reiner Weltraum-Hash, keine CPU-Steuerung)
float FV_RegionalOffset(float3 objectPosWS)
{
    float h = frac(sin(dot(floor(objectPosWS.xz / 7.0), float2(12.9898, 78.233))) * 43758.5453);
    return (h - 0.5) * _FV_SeasonParams.w;
}

// true = Fragment verwerfen (Laubfall)
bool FV_LeafFallen(float4 s, float3 objectPosWS)
{
    return s.w > 0.5 && (s.y + FV_RegionalOffset(objectPosWS) * 0.5) < _FV_SeasonParams.y;
}

half3 FV_SeasonColor(half3 albedo, float4 s, float3 objectPosWS)
{
    if (s.w < 0.5 || _FV_SeasonParams.x <= 0.0) return albedo;
    float season = _FV_SeasonParams.x + FV_RegionalOffset(objectPosWS);
    half k = smoothstep(s.x - 0.12, s.x + 0.04, season);
    half lum = dot(albedo, half3(0.3, 0.59, 0.11));
    half3 au = FV_Autumn((half)s.z) * (0.35 + 1.6 * lum);
    return lerp(albedo, au * 0.5, k);
}

#endif
