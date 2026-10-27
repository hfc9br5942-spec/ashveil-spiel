// ASHVEIL Pathtracer – gemeinsame Grundlagen: Konstanten, Strahl-Nutzlast, Hashes, Sobol+Owen-Folge,
// robuster Strahlversatz, Orthonormalbasis. Wird von Raygen (.raytrace) und Treffer-Shadern (PTSurface) geteilt,
// deshalb nur reines HLSL ohne URP-Includes.
#ifndef ASH_PT_COMMON_INCLUDED
#define ASH_PT_COMMON_INCLUDED

#define ASH_PI        3.14159265358979
#define ASH_TWO_PI    6.28318530717959
#define ASH_INV_PI    0.31830988618379
#define ASH_INV_TWO_PI 0.15915494309189
#define ASH_FLT_MAX   3.402823e38

// Instanzmasken (8 Bit). Figur/Kreatur/Boss teilen sich ein Bit, die Kategorie steht zusaetzlich in der Instanzliste.
#define ASH_MASK_STATIC      0x01
#define ASH_MASK_TERRAIN     0x02
#define ASH_MASK_VEGETATION  0x04
#define ASH_MASK_CHARACTER   0x08
#define ASH_MASK_PROPS       0x10
#define ASH_MASK_TRANSPARENT 0x20
#define ASH_MASK_EMISSIVE    0x40
#define ASH_MASK_VFX         0x80
#define ASH_MASK_ALL         0xFF
// Schattenstrahlen ignorieren reine Licht-/Effekt-Stellvertreter
#define ASH_MASK_SHADOW      (ASH_MASK_ALL & ~ASH_MASK_VFX)

// Oberflaechen-Flags (Nutzlast)
#define ASH_SURF_HIT          0x01
#define ASH_SURF_FRONT        0x02
#define ASH_SURF_DOUBLESIDED  0x04
#define ASH_SURF_DIELECTRIC   0x08   // echtes Glas/Eis/Kristall (Brechung)
#define ASH_SURF_UNLIT        0x10
#define ASH_SURF_THINTRANS    0x20   // duenne diffuse Durchlaessigkeit (Blaetter)

// Nutzlast fuer ALLE Strahlarten (eine Trefferguppe je Material; Schattenstrahlen nutzen nur hitT/Flags).
struct AshPTPayload
{
    float  hitT;          // Eingabe: -; Ausgabe: Trefferabstand (<0 = verfehlt)
    float3 position;      // Welt (relativ zum RTAS-Ursprung), aus Baryzentrik – genauer als o + t*d
    float3 geoNormal;     // geometrische Normale, zur Vorderseite
    float3 shadingNormal; // Normal-Map-Normale
    float3 albedo;
    float  metallic;
    float  roughness;     // perzeptuell (1 - Glaette), wie URP
    float3 emission;      // Strahldichte der Oberflaeche (linear)
    float  opacity;       // Deckung (Alpha bei transparenten Materialien; 1 = deckend)
    float  ior;
    float  transmission;  // Anteil duenne Durchlaessigkeit bzw. Glas
    float3 absorption;    // Beer-Lambert-Koeffizient (je Meter) im Innern
    uint   flags;
    uint   instanceId;    // eigene Instanz-ID (Liste im Szenenverwalter)
    uint   primitiveIndex;
    float  coneWidth;     // Eingabe: Strahlkegel-Breite am Ursprung (m); Ausgabe: am Treffer
    float  coneSpread;    // Eingabe: Aufweitung (rad) – fuer Textur-LOD
};

// ------------------------------------------------------------------ Hashes
uint AshHashU(uint x)
{
    // lowbias32 (Chris Wellons)
    x ^= x >> 16; x *= 0x7feb352du;
    x ^= x >> 15; x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

uint AshHashCombine(uint seed, uint v)
{
    return seed ^ (v + 0x9e3779b9u + (seed << 6) + (seed >> 2));
}

// ------------------------------------------------------------------ Sobol + Owen (Burley 2020)
static const uint AshSobolDir[4][32] =
{
    { 0x80000000u, 0x40000000u, 0x20000000u, 0x10000000u, 0x08000000u, 0x04000000u, 0x02000000u, 0x01000000u, 0x00800000u, 0x00400000u, 0x00200000u, 0x00100000u, 0x00080000u, 0x00040000u, 0x00020000u, 0x00010000u, 0x00008000u, 0x00004000u, 0x00002000u, 0x00001000u, 0x00000800u, 0x00000400u, 0x00000200u, 0x00000100u, 0x00000080u, 0x00000040u, 0x00000020u, 0x00000010u, 0x00000008u, 0x00000004u, 0x00000002u, 0x00000001u },
    { 0x80000000u, 0xc0000000u, 0xa0000000u, 0xf0000000u, 0x88000000u, 0xcc000000u, 0xaa000000u, 0xff000000u, 0x80800000u, 0xc0c00000u, 0xa0a00000u, 0xf0f00000u, 0x88880000u, 0xcccc0000u, 0xaaaa0000u, 0xffff0000u, 0x80008000u, 0xc000c000u, 0xa000a000u, 0xf000f000u, 0x88008800u, 0xcc00cc00u, 0xaa00aa00u, 0xff00ff00u, 0x80808080u, 0xc0c0c0c0u, 0xa0a0a0a0u, 0xf0f0f0f0u, 0x88888888u, 0xccccccccu, 0xaaaaaaaau, 0xffffffffu },
    { 0x80000000u, 0xc0000000u, 0x60000000u, 0x90000000u, 0xe8000000u, 0x5c000000u, 0x8e000000u, 0xc5000000u, 0x68800000u, 0x9cc00000u, 0xee600000u, 0x55900000u, 0x80680000u, 0xc09c0000u, 0x60ee0000u, 0x90550000u, 0xe8808000u, 0x5cc0c000u, 0x8e606000u, 0xc5909000u, 0x6868e800u, 0x9c9c5c00u, 0xeeee8e00u, 0x5555c500u, 0x8000e880u, 0xc0005cc0u, 0x60008e60u, 0x9000c590u, 0xe8006868u, 0x5c009c9cu, 0x8e00eeeeu, 0xc5005555u },
    { 0x80000000u, 0xc0000000u, 0x20000000u, 0x50000000u, 0xf8000000u, 0x74000000u, 0xa2000000u, 0x93000000u, 0xd8800000u, 0x25400000u, 0x59e00000u, 0xe6d00000u, 0x78080000u, 0xb40c0000u, 0x82020000u, 0xc3050000u, 0x208f8000u, 0x51474000u, 0xfbea2000u, 0x75d93000u, 0xa0858800u, 0x914e5400u, 0xdbe79e00u, 0x25db6d00u, 0x58800080u, 0xe54000c0u, 0x79e00020u, 0xb6d00050u, 0x800800f8u, 0xc00c0074u, 0x200200a2u, 0x50050093u }
};

uint AshSobol(uint index, uint dim)
{
    uint x = 0;
    [unroll] for (uint bit = 0; bit < 32; bit++)
    {
        uint mask = (index >> bit) & 1u;
        x ^= mask * AshSobolDir[dim][bit];
    }
    return x;
}

uint AshLaineKarras(uint x, uint seed)
{
    x += seed;
    x ^= x * 0x6c50b47cu;
    x ^= x * 0xb82f1e52u;
    x ^= x * 0xc7afe638u;
    x ^= x * 0x8d22f6e6u;
    return x;
}

uint AshNestedUniformScramble(uint x, uint seed)
{
    x = reversebits(x);
    x = AshLaineKarras(x, seed);
    return reversebits(x);
}

// Vierdimensionaler Punkt einer gemischten, Owen-verwuerfelten Sobol-Folge. Jede "Dimensionsgruppe" (Kamera,
// BSDF je Abprall, Licht je Abprall …) hat einen eigenen Seed – deterministisch, keine Dimensionskollision.
float4 AshSobolOwen4(uint index, uint seed)
{
    uint i = AshNestedUniformScramble(index, seed);
    uint4 x = uint4(AshSobol(i, 0), AshSobol(i, 1), AshSobol(i, 2), AshSobol(i, 3));
    x.x = AshNestedUniformScramble(x.x, AshHashCombine(seed, 0u));
    x.y = AshNestedUniformScramble(x.y, AshHashCombine(seed, 1u));
    x.z = AshNestedUniformScramble(x.z, AshHashCombine(seed, 2u));
    x.w = AshNestedUniformScramble(x.w, AshHashCombine(seed, 3u));
    return min(float4(x >> 8) * (1.0 / 16777216.0), 0.99999994);
}

// Reservierte Dimensionsgruppen (je 4 Zahlen)
#define ASH_DIM_CAMERA      0u   // Pixel-Jitter xy, Linse zw
#define ASH_DIM_BSDF(b)     (1u + 3u * (b))   // Lappenwahl, u, v, Russisches Roulette
#define ASH_DIM_LIGHT(b)    (2u + 3u * (b))   // Lichtwahl, u, v, Reserve
#define ASH_DIM_EXTRA(b)    (3u + 3u * (b))   // Transparenz-Durchgang, Medium, Reserve

struct AshSampler
{
    uint pixelSeed;
    uint sampleIndex;
};

AshSampler AshInitSampler(uint2 pixel, uint sampleIndex, uint globalSeed)
{
    AshSampler s;
    s.pixelSeed = AshHashU(AshHashCombine(AshHashU(pixel.x * 73856093u ^ pixel.y * 19349663u), globalSeed));
    s.sampleIndex = sampleIndex;
    return s;
}

float4 AshSample4(AshSampler s, uint dimGroup)
{
    return AshSobolOwen4(s.sampleIndex, AshHashU(AshHashCombine(s.pixelSeed, dimGroup)));
}

// ------------------------------------------------------------------ Geometrie-Helfer
float AshLuminance(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
float AshMax3(float3 v) { return max(v.x, max(v.y, v.z)); }

// Waechter & Binder (Ray Tracing Gems, Kap. 6): massstabsgerechter Versatz gegen Selbstschnitt, ohne feste 0,01.
float3 AshOffsetRay(float3 p, float3 n)
{
    const float origin = 1.0 / 32.0;
    const float floatScale = 1.0 / 65536.0;
    const float intScale = 256.0;
    int3 ofi = int3(intScale * n);
    float3 pi = float3(
        asfloat(asint(p.x) + (p.x < 0 ? -ofi.x : ofi.x)),
        asfloat(asint(p.y) + (p.y < 0 ? -ofi.y : ofi.y)),
        asfloat(asint(p.z) + (p.z < 0 ? -ofi.z : ofi.z)));
    return float3(
        abs(p.x) < origin ? p.x + floatScale * n.x : pi.x,
        abs(p.y) < origin ? p.y + floatScale * n.y : pi.y,
        abs(p.z) < origin ? p.z + floatScale * n.z : pi.z);
}

// Duff et al. 2017 – verzweigungsfreie Orthonormalbasis
void AshBasis(float3 n, out float3 t, out float3 b)
{
    float s = n.z >= 0.0 ? 1.0 : -1.0;
    float a = -1.0 / (s + n.z);
    float c = n.x * n.y * a;
    t = float3(1.0 + s * n.x * n.x * a, s * c, -s * n.x);
    b = float3(c, s + n.y * n.y * a, -n.y);
}

float3 AshToLocal(float3 v, float3 t, float3 b, float3 n) { return float3(dot(v, t), dot(v, b), dot(v, n)); }
float3 AshToWorld(float3 v, float3 t, float3 b, float3 n) { return v.x * t + v.y * b + v.z * n; }

bool AshIsFinite(float3 v) { return all(v == v) && all(abs(v) < 1e30); }

#endif
