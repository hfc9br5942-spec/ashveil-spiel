// ASHVEIL Dynamisches GI – gemeinsame Funktionen fuer Probengitter (DDGI-Verfahren): Speicherordnung mit Umlauf-
// Adressierung (das Gitter wandert mit der Kamera, ohne Daten umzukopieren), Oktaeder-Abbildung, Abtastung mit
// Sichtbarkeit (Chebyshev) gegen Lichtlecks. Genutzt vom Probenstrahl-Shader, vom Mischen, vom Bildpass und vom Gras.
//
// Einheit der gespeicherten Werte: kosinusgewichtete mittlere Strahldichte = Bestrahlungsstaerke / π. Das ist genau
// die Groesse, die URP als "bakedGI" erwartet (Albedo × bakedGI = indirektes diffuses Licht).
#ifndef ASH_GI_COMMON_INCLUDED
#define ASH_GI_COMMON_INCLUDED

#define ASH_GI_TILE 8          // Texel je Probe (Kantenlaenge) fuer Licht und Abstand

Texture2DArray<float4> _AshGIIrradiance;   // Scheibe = Kaskade
Texture2DArray<float4> _AshGIDistance;     // rg: mittlerer Abstand, mittlerer Abstand²
StructuredBuffer<int4> _AshGIState;        // xyz Zelle, w Flags (1 gueltig, 2 im Inneren von Geometrie)

float4 _AshGIGrid;        // xyz Proben je Achse, w Kaskaden
float4 _AshGICascade0;    // xyz erste Zelle (Weltzellen), w Abstand (m)
float4 _AshGICascade1;
float4 _AshGIBias;        // x Normalen-Versatz, y Blick-Versatz (Anteil des Probenabstands), z Staerke, w Rand-Blende (Zellen)

int3 AshGIDims() { return int3(_AshGIGrid.xyz); }
uint AshGICount() { int3 n = AshGIDims(); return (uint)(n.x * n.y * n.z); }

float4 AshGICascade(int c) { return c == 0 ? _AshGICascade0 : _AshGICascade1; }

int3 AshGIWrap(int3 cell)
{
    int3 n = AshGIDims();
    return ((cell % n) + n) % n;
}

uint AshGISlot(int3 s)
{
    int3 n = AshGIDims();
    return (uint)(s.x + s.y * n.x + s.z * n.x * n.y);
}

int3 AshGISlotCoords(uint slot)
{
    int3 n = AshGIDims();
    return int3(slot % (uint)n.x, (slot / (uint)n.x) % (uint)n.y, slot / (uint)(n.x * n.y));
}

// Welche Weltzelle belegt der Speicherplatz s gerade (Gitter beginnt bei base)?
int3 AshGICellForSlot(int3 s, int3 base)
{
    int3 n = AshGIDims();
    return base + ((((s - base) % n) + n) % n);
}

// Linke obere Ecke der Kachel einer Probe im Atlas
int2 AshGITileOrigin(int3 s)
{
    int3 n = AshGIDims();
    return int2((s.x + s.y * n.x) * ASH_GI_TILE, s.z * ASH_GI_TILE);
}

float2 AshGIOctEncode(float3 d)
{
    d /= (abs(d.x) + abs(d.y) + abs(d.z));
    float2 o = d.xy;
    if (d.z < 0)
    {
        float sx = d.x >= 0 ? 1.0 : -1.0;
        float sy = d.y >= 0 ? 1.0 : -1.0;
        o = float2((1.0 - abs(d.y)) * sx, (1.0 - abs(d.x)) * sy);
    }
    return o * 0.5 + 0.5;
}

float3 AshGIOctDecode(float2 f)
{
    f = f * 2.0 - 1.0;
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float t = saturate(-n.z);
    n.x += n.x >= 0 ? -t : t;
    n.y += n.y >= 0 ? -t : t;
    return normalize(n);
}

// Bilinear innerhalb der Kachel (ohne Randtexel: an der Kante geklemmt)
float4 AshGILoadTile(Texture2DArray<float4> tex, int cascade, int3 s, float2 oct)
{
    int2 o = AshGITileOrigin(s);
    float2 uv = oct * ASH_GI_TILE - 0.5;
    float2 i0 = floor(uv);
    float2 f = uv - i0;
    int2 a = clamp(int2(i0), 0, ASH_GI_TILE - 1);
    int2 b = clamp(int2(i0) + 1, 0, ASH_GI_TILE - 1);
    float4 v00 = tex.Load(int4(o + int2(a.x, a.y), cascade, 0));
    float4 v10 = tex.Load(int4(o + int2(b.x, a.y), cascade, 0));
    float4 v01 = tex.Load(int4(o + int2(a.x, b.y), cascade, 0));
    float4 v11 = tex.Load(int4(o + int2(b.x, b.y), cascade, 0));
    return lerp(lerp(v00, v10, f.x), lerp(v01, v11, f.x), f.y);
}

// Eine Kaskade abtasten. p = Weltposition, n = Normale, toEye = Richtung zur Kamera (oder 0).
// Rueckgabe: Licht; w = Summe der Gewichte, edge = 0 am Rand … 1 im Inneren (fuer die Kaskaden-Ueberblendung)
float3 AshGISampleCascade(int c, float3 p, float3 n, float3 toEye, out float wsum, out float edge)
{
    float4 cas = AshGICascade(c);
    float spacing = cas.w;
    int3 base = int3(cas.xyz);
    int3 dims = AshGIDims();
    float3 pb = p + (n * _AshGIBias.x + toEye * _AshGIBias.y) * spacing;
    float3 g = pb / spacing - float3(base);        // in Zellen relativ zum Gitteranfang
    wsum = 0;
    edge = 0;
    if (any(g < 0.0) || any(g > float3(dims - 1))) return 0;
    float3 distEdge = min(g, float3(dims - 1) - g);
    edge = saturate((min(distEdge.x, min(distEdge.y, distEdge.z)) - 0.5) / max(_AshGIBias.w, 0.5));

    float3 g0 = floor(g);
    float3 a = g - g0;
    float3 sum = 0;
    [unroll] for (int i = 0; i < 8; i++)
    {
        int3 off = int3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
        int3 cell = base + int3(g0) + off;
        int3 s = AshGIWrap(cell);
        uint idx = AshGISlot(s) + (uint)c * AshGICount();
        int4 st = _AshGIState[idx];
        if (st.x != cell.x || st.y != cell.y || st.z != cell.z || (st.w & 1) == 0 || (st.w & 2) != 0) continue;
        float3 probePos = float3(cell) * spacing;
        float3 tri = lerp(1.0 - a, a, float3(off));
        float w = tri.x * tri.y * tri.z;
        float3 toProbe = probePos - p;
        float lenTP = length(toProbe);
        float3 dirTP = lenTP > 1e-4 ? toProbe / lenTP : n;
        float wrap = (dot(dirTP, n) + 1.0) * 0.5;
        w *= wrap * wrap + 0.2;
        // Sichtbarkeit: liegt der Punkt fuer die Probe "hinter einer Wand"?
        float3 fromProbe = pb - probePos;
        float dist = length(fromProbe);
        float2 m = AshGILoadTile(_AshGIDistance, c, s, AshGIOctEncode(dist > 1e-4 ? fromProbe / dist : n)).rg;
        if (dist > m.x)
        {
            float var = abs(m.y - m.x * m.x);
            float d = dist - m.x;
            float cheb = var / max(var + d * d, 1e-6);
            w *= max(cheb * cheb * cheb, 0.0);
        }
        w = max(w, 1e-5);
        float3 e = AshGILoadTile(_AshGIIrradiance, c, s, AshGIOctEncode(n)).rgb;
        sum += w * e;
        wsum += w;
    }
    return wsum > 0 ? sum / wsum : 0;
}

// Beide Kaskaden: innen die feine, zum Rand hin die grobe. valid = 0, wenn keine Probe traf (dann Rueckfall nutzen).
float3 AshGISample(float3 p, float3 n, float3 toEye, out float valid)
{
    float w0, e0, w1, e1;
    float3 c0 = AshGISampleCascade(0, p, n, toEye, w0, e0);
    valid = 1;
    if (w0 > 1e-4 && e0 >= 0.999) return c0 * _AshGIBias.z;   // tief in der feinen Kaskade: grobe nicht noetig
    float3 c1 = 0;
    w1 = 0; e1 = 0;
    if (_AshGIGrid.w > 1.5) c1 = AshGISampleCascade(1, p, n, toEye, w1, e1);
    if (w0 > 1e-4 && w1 > 1e-4) return lerp(c1, c0, e0) * _AshGIBias.z;
    if (w0 > 1e-4) return c0 * _AshGIBias.z;
    if (w1 > 1e-4) return c1 * _AshGIBias.z;
    valid = 0;
    return 0;
}

#endif
