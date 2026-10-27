// ASHVEIL Pathtracer – BSDF: Lambert-Diffus + GGX-Mikrofacetten (VNDF-Sampling nach Dupuy & Benyoub 2023),
// Schlick-Fresnel mit F0 wie URP (0,04 fuer Nichtmetalle), optionale duenne diffuse Durchlaessigkeit (Blaetter)
// und glattes Dielektrikum (Glas/Eis/Kristall) mit exaktem Fresnel.
// Rauheit wird NICHT kuenstlerisch veraendert: alpha = perzeptuelle Rauheit², nur numerisch nach unten begrenzt.
// Alle Berechnungen im lokalen Raum (z = Schattierungsnormale).
#ifndef ASH_PT_BRDF_INCLUDED
#define ASH_PT_BRDF_INCLUDED

#include "AshPTCommon.hlsl"

#define ASH_MIN_ALPHA 0.0016   // entspricht perzeptueller Rauheit 0,04 – verhindert Singularitaeten, kein Look-Eingriff

struct AshMaterial
{
    float3 albedo;
    float  metallic;
    float  alpha;          // GGX-Rauheit (perzeptuell²)
    float3 f0;
    float3 diffuseColor;   // Albedo * (1 - Metallic)
    float  thinTransmission;
    bool   dielectric;
    float  ior;
    bool   frontFace;
};

AshMaterial AshMakeMaterial(AshPTPayload p)
{
    AshMaterial m;
    m.albedo = saturate(p.albedo);
    m.metallic = saturate(p.metallic);
    float pr = saturate(p.roughness);
    m.alpha = max(pr * pr, ASH_MIN_ALPHA);
    m.f0 = lerp(float3(0.04, 0.04, 0.04), m.albedo, m.metallic);
    m.diffuseColor = m.albedo * (1.0 - m.metallic);
    m.thinTransmission = (p.flags & ASH_SURF_THINTRANS) != 0 ? saturate(p.transmission) : 0.0;
    m.dielectric = (p.flags & ASH_SURF_DIELECTRIC) != 0;
    m.ior = max(p.ior, 1.0001);
    m.frontFace = (p.flags & ASH_SURF_FRONT) != 0;
    return m;
}

float3 AshFresnelSchlick(float3 f0, float cosTheta)
{
    float f = pow(1.0 - saturate(cosTheta), 5.0);
    return f0 + (1.0 - f0) * f;
}

// Exakter Fresnel fuer Dielektrika (unpolarisiert). eta = n_aussen / n_innen relativ zur Einfallsseite.
float AshFresnelDielectric(float cosI, float eta)
{
    float sin2T = eta * eta * max(0.0, 1.0 - cosI * cosI);
    if (sin2T >= 1.0) return 1.0; // Totalreflexion
    float cosT = sqrt(1.0 - sin2T);
    float rs = (eta * cosI - cosT) / (eta * cosI + cosT);
    float rp = (cosI - eta * cosT) / (cosI + eta * cosT);
    return 0.5 * (rs * rs + rp * rp);
}

float AshGGX_D(float3 h, float a)
{
    float a2 = a * a;
    float c = h.z * h.z;
    float d = c * (a2 - 1.0) + 1.0;
    return a2 / (ASH_PI * d * d);
}

float AshGGX_Lambda(float3 v, float a)
{
    float c2 = v.z * v.z;
    float t2 = max(0.0, 1.0 - c2) / max(c2, 1e-8);
    return 0.5 * (-1.0 + sqrt(1.0 + a * a * t2));
}

float AshGGX_G1(float3 v, float a) { return 1.0 / (1.0 + AshGGX_Lambda(v, a)); }
float AshGGX_G2(float3 v, float3 l, float a) { return 1.0 / (1.0 + AshGGX_Lambda(v, a) + AshGGX_Lambda(l, a)); }

// Dupuy & Benyoub 2023: sichtbare Normalen per Kugelkappe (isotrop)
float3 AshSampleGGXVNDF(float3 v, float a, float2 u)
{
    float3 vh = normalize(float3(a * v.x, a * v.y, v.z));
    float phi = ASH_TWO_PI * u.x;
    float z = (1.0 - u.y) * (1.0 + vh.z) - vh.z;
    float sinT = sqrt(saturate(1.0 - z * z));
    float3 c = float3(sinT * cos(phi), sinT * sin(phi), z);
    float3 h = c + vh;
    return normalize(float3(a * h.x, a * h.y, max(h.z, 0.0)));
}

// Dichte einer VNDF-gesampelten Reflexionsrichtung l (zu v, im lokalen Raum)
float AshGGXReflectPdf(float3 v, float3 l, float a)
{
    if (v.z <= 0.0 || l.z <= 0.0) return 0.0;
    float3 h = normalize(v + l);
    return AshGGX_G1(v, a) * AshGGX_D(h, a) / (4.0 * v.z);
}

// Wahrscheinlichkeit, den Spiegel-Lappen zu waehlen (abhaengig nur von v → konsistent fuer MIS)
float AshSpecularProbability(AshMaterial m, float3 v)
{
    float3 F = AshFresnelSchlick(m.f0, v.z);
    float s = AshLuminance(F);
    float d = AshLuminance(m.diffuseColor * (1.0 - F));
    return clamp(s / max(s + d, 1e-6), 0.1, 0.95);
}

// Auswertung der opaken BSDF (Reflexion + duenne Durchlaessigkeit), ohne Kosinus. Liefert auch die Misch-Dichte.
float3 AshEvalBSDF(AshMaterial m, float3 v, float3 l, out float pdf)
{
    pdf = 0.0;
    if (v.z <= 0.0) return 0.0;
    float pSpec = AshSpecularProbability(m, v);
    float3 F0v = AshFresnelSchlick(m.f0, v.z);
    float3 kd = (1.0 - F0v);   // Energie, die nicht an der Oberflaeche gespiegelt wird
    float pT = m.thinTransmission;

    if (l.z > 0.0)
    {
        float3 h = normalize(v + l);
        float D = AshGGX_D(h, m.alpha);
        float G2 = AshGGX_G2(v, l, m.alpha);
        float3 F = AshFresnelSchlick(m.f0, dot(v, h));
        float3 spec = D * G2 * F / (4.0 * v.z * l.z);
        float3 diff = m.diffuseColor * kd * (1.0 - pT) * ASH_INV_PI;
        pdf = pSpec * AshGGXReflectPdf(v, l, m.alpha) + (1.0 - pSpec) * (1.0 - pT) * l.z * ASH_INV_PI;
        return spec + diff;
    }
    if (pT > 0.0 && l.z < 0.0)
    {
        pdf = (1.0 - pSpec) * pT * (-l.z) * ASH_INV_PI;
        return m.diffuseColor * kd * pT * ASH_INV_PI;
    }
    return 0.0;
}

// Sampling der opaken BSDF. Rueckgabe: Gewicht f*cos/pdf; pdf = Misch-Dichte (fuer MIS).
float3 AshSampleBSDF(AshMaterial m, float3 v, float4 u, out float3 l, out float pdf)
{
    l = float3(0, 0, 1);
    pdf = 0.0;
    if (v.z <= 0.0) return 0.0;
    float pSpec = AshSpecularProbability(m, v);
    if (u.x < pSpec)
    {
        float3 h = AshSampleGGXVNDF(v, m.alpha, u.yz);
        l = reflect(-v, h);
        if (l.z <= 0.0) return 0.0;
    }
    else
    {
        // Kosinus-verteilt, ggf. auf die Rueckseite (duenne Durchlaessigkeit)
        float r = sqrt(u.y);
        float phi = ASH_TWO_PI * u.z;
        l = float3(r * cos(phi), r * sin(phi), sqrt(max(0.0, 1.0 - u.y)));
        float uT = (u.x - pSpec) / max(1.0 - pSpec, 1e-6);
        if (uT < m.thinTransmission) l.z = -l.z;
    }
    float3 f = AshEvalBSDF(m, v, l, pdf);
    if (pdf <= 0.0) return 0.0;
    return f * abs(l.z) / pdf;
}

// Glattes Dielektrikum: waehlt Reflexion/Brechung nach Fresnel. Delta-Verteilung → kein MIS mit Lichtern.
// v zeigt vom Punkt weg, n zur Einfallsseite. Rueckgabe: Gewicht (1 bei Energieerhaltung), l in Welt.
float3 AshSampleDielectric(AshMaterial m, float3 v, float3 n, float u, out float3 l, out bool refracted)
{
    float eta = m.frontFace ? 1.0 / m.ior : m.ior;   // n_aussen / n_innen
    float cosI = saturate(dot(v, n));
    float F = AshFresnelDielectric(cosI, eta);
    refracted = false;
    if (u < F)
    {
        l = reflect(-v, n);
        return 1.0;
    }
    float3 t = refract(-v, n, eta);
    if (dot(t, t) < 1e-8) { l = reflect(-v, n); return 1.0; }
    l = normalize(t);
    refracted = true;
    return 1.0;   // Strahldichte-Skalierung eta² bewusst weggelassen (Importance-Transport; bei Kamera-Pfaden neutral)
}

#endif
