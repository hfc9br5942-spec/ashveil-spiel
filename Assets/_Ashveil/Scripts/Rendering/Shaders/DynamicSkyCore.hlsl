// Ashveil – Dynamic Sky: Himmelsfarbe fuer eine Richtung. Gemeinsam genutzt von der Himmelskuppel (DynamicSky.shader)
// und dem Pathtracer (Himmel wird in eine Lat-Long-Karte gebacken → derselbe Himmel wie im Spiel).
// Erwartet vorher URP Core.hlsl (_Time). sunDiskScale 0 = ohne Sonnenscheibe (der Pathtracer rechnet sie analytisch).
#ifndef ASH_DYNAMIC_SKY_CORE_INCLUDED
#define ASH_DYNAMIC_SKY_CORE_INCLUDED

float4 _AshSkyZenith, _AshSkyHorizon, _AshSkyGround, _AshSkyGlow;
float4 _AshSkySun, _AshSkySunColor;
float4 _AshCelX, _AshCelY, _AshCelZ;
float4 _AshMoonA, _AshMoonB, _AshMoonAColor, _AshMoonBColor, _AshMoonSeed;
float4 _AshStars;
float4 _AshCloudLow, _AshCloudLowOfs, _AshCloudHigh, _AshCloudHighOfs;
float4 _AshCloudLit, _AshCloudShadow, _AshSkyHaze;
float4 _AshConstStars[64];
float _AshConstCount;
float4 _AshVeilNormal;    // Schleier (Kanon 30.09.): Ebene des Grosskreises ueber der Kerbe (xyz, Welt), w = an (0/1)
float4 _AshVeilTangent;   // Richtung der Kerbe (xyz, waagerecht), w = Breite des Bandes (Kosinus-Mass)
float4 _AshVeilParams;    // x Wanderzeit der Straenge, y Schleierstoss 0..1, z Staerke (Schleiereinfluss am Ort), w Adern-Farbanteil
float4 _AshMeteorA0, _AshMeteorA1, _AshMeteorB0, _AshMeteorB1;   // Start (xyz, Fortschritt), Ende (xyz, Helligkeit)


// ------------------------------------------------------------------ Rauschen
float Hash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float3 Hash32(float2 p)
{
    float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yxz + 33.33);
    return frac((q.xxy + q.yzz) * q.zyx);
}

float3 Wrap(float3 i, float period) { return i - period * floor(i / period); }

// Wertrauschen, in x/y/z mit 'period' kachelnd (Windversaetze laufen modulo 512)
float Noise3(float3 p, float period)
{
    float3 i = floor(p), f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float3 i0 = Wrap(i, period), i1 = Wrap(i + 1.0, period);
    float a = Hash13(float3(i0.x, i0.y, i0.z)), b = Hash13(float3(i1.x, i0.y, i0.z));
    float c = Hash13(float3(i0.x, i1.y, i0.z)), d = Hash13(float3(i1.x, i1.y, i0.z));
    float e = Hash13(float3(i0.x, i0.y, i1.z)), g = Hash13(float3(i1.x, i0.y, i1.z));
    float h = Hash13(float3(i0.x, i1.y, i1.z)), k = Hash13(float3(i1.x, i1.y, i1.z));
    return lerp(lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y), lerp(lerp(e, g, u.x), lerp(h, k, u.x), u.y), u.z);
}

float Fbm(float3 p, int octaves)
{
    float v = 0.0, amp = 0.5, norm = 0.0, period = 512.0;
    [loop] for (int o = 0; o < octaves; o++)
    {
        v += Noise3(p, period) * amp;
        norm += amp;
        p = p * 2.03 + 17.1;
        period *= 2.0;
        amp *= 0.5;
    }
    return v / norm;
}

// ------------------------------------------------------------------ Sterne
float3 StarField(float3 c, float density, float pixAng)
{
    float3 a = abs(c);
    float2 uv; float face;
    if (a.x >= a.y && a.x >= a.z) { uv = c.yz / a.x; face = c.x > 0 ? 0 : 1; }
    else if (a.y >= a.z) { uv = c.xz / a.y; face = c.y > 0 ? 2 : 3; }
    else { uv = c.xy / a.z; face = c.z > 0 ? 4 : 5; }
    const float N = 110.0;
    float2 g = (uv * 0.5 + 0.5) * N;
    float2 cell = floor(g), f = frac(g);
    float3 h = Hash32(cell + face * 137.13);
    if (h.x > density) return 0;
    float2 pos = 0.2 + 0.6 * h.yz;
    float d = length(f - pos);
    float pix = pixAng * N * 0.5;   // Pixelgroesse in Zellen (ohne Ableitungen in Verzweigungen)
    float bright = pow(frac(h.x * 57.3 + h.y * 11.1), 5.0) * 1.6 + 0.08;
    float r = max(pix * 0.75, 0.035 + 0.05 * bright);
    float s = saturate(1.0 - d / r);
    s *= s;
    float3 tint = lerp(float3(1.0, 0.82, 0.62), float3(0.72, 0.84, 1.0), frac(h.z * 91.7));
    return tint * s * bright;
}

float3 Constellations(float3 c, float pixAng)
{
    float3 col = 0;
    int count = (int)_AshConstCount;
    [loop] for (int i = 0; i < count; i++)
    {
        float4 st = _AshConstStars[i];
        float cosd = dot(c, st.xyz);
        if (cosd < 0.9995) continue;
        float ang = sqrt(max(0.0, 2.0 * (1.0 - cosd)));
        float r = max(pixAng * 1.1, 0.0009 + 0.0011 * st.w);
        float s = saturate(1.0 - ang / r);
        col += float3(1.0, 0.95, 0.86) * s * s * (0.9 + 1.4 * st.w);
    }
    return col;
}

// ------------------------------------------------------------------ Sternschnuppen (Pass 12)
// Leuchtspur auf einem Grosskreisbogen von m0.xyz nach m1.xyz; Kopf bei m0.w, Schweif dahinter.
float3 MeteorTrail(float3 d, float4 m0, float4 m1, float pixAng)
{
    if (m1.w <= 0.001) return 0;
    float3 S = m0.xyz, E = m1.xyz;
    float3 n = cross(S, E);
    float nl = length(n);
    if (nl < 1e-5) return 0;
    n /= nl;
    float arc = atan2(nl, dot(S, E));
    float3 T = cross(n, S);
    float ang = atan2(dot(d, T), dot(d, S));
    float head = m0.w * arc;
    float tail = max(0.0, head - arc * 0.65);
    if (ang < tail || ang > head + 0.003) return 0;
    float perp = abs(dot(d, n));
    float along = saturate((ang - tail) / max(head - tail, 1e-4));
    float width = max(pixAng * 0.8, 0.0005) * (0.5 + 0.9 * along);
    float core = exp(-perp * perp / (width * width));
    float glow = exp(-perp * perp / (width * width * 9.0)) * 0.25;   // weicher Schein um die Spur
    return float3(1.0, 0.92, 0.78) * (core + glow) * pow(along, 1.4) * m1.w * 5.0;
}

// ------------------------------------------------------------------ Monde
float3 MoonDisk(float3 d, float4 moon, float4 color, float seed, float3 sunDir, float pixAng, out float cover)
{
    cover = 0;
    float3 m = moon.xyz;
    float r = moon.w;
    float cosang = dot(d, m);
    float3 halo = color.rgb * pow(saturate(cosang), 2200.0 / max(r * 60.0, 0.2)) * 0.04 * color.a;
    if (cosang < cos(r * 1.05)) return halo * saturate(0.5 - 0.5 * dot(m, sunDir));
    float3 up = abs(m.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 R = normalize(cross(up, m));
    float3 U = cross(m, R);
    float sr = sin(r);
    float2 uv = float2(dot(d, R), dot(d, U)) / sr;
    float rr = dot(uv, uv);
    float edge = saturate((1.0 - rr) / max(3.0 * pixAng / sr, 1e-4));
    if (rr >= 1.0) return halo;
    float z = sqrt(1.0 - rr);
    float3 n = uv.x * R + uv.y * U - z * m;               // Oberflaeche, dem Beobachter zugewandt
    float lit = smoothstep(-0.04, 0.12, dot(n, sunDir));
    float mare = Fbm(float3(uv * 2.3 + seed * 7.31, seed), 4);
    float albedo = 0.72 + 0.34 * smoothstep(0.35, 0.7, mare);
    float3 col = color.rgb * (lit * albedo * (0.85 + 0.15 * z) + 0.025);  // schwaches Erdlicht auf der Nachtseite
    cover = edge;
    return col * color.a * edge + halo;
}

// ------------------------------------------------------------------ Wolken
// Wolkenfeld: liefert Deckung (Rueckgabe) und Dicke 0..1 (fuer dunklere, dichte Stellen)
float CloudLow(float2 uv, float cov, float dens, float erosion, int oct, out float thick)
{
    float n = Fbm(float3(uv + _AshCloudLowOfs.xy, _AshCloudLowOfs.z), oct);
    n = saturate((n - 0.5) * 1.9 + 0.5);   // fbm streut eng um 0,5 – spreizen, damit die Bedeckung stimmt
#if defined(ASHSKY_HIGH)
    float detail = Fbm(float3(uv * 4.1 + _AshCloudLowOfs.xy * 1.7, _AshCloudLowOfs.z * 2.0 + 40.0), 2);
    n -= (detail - 0.5) * 0.18 * erosion;
#endif
    float lo = 1.0 - cov;
    float soft = lerp(0.3, 0.08, erosion);
    thick = saturate((n - lo) / 0.55);
    return smoothstep(lo, lo + soft, n) * dens;
}


float3 AshDynamicSky(float3 d, float pixAng, float sunDiskScale)
{

    float y = d.y;
    float3 sunDir = _AshSkySun.xyz;
    float mu = dot(d, sunDir);
    
    // --- Verlauf
    float hw = pow(1.0 - saturate(y), 3.0);
    float3 col = lerp(_AshSkyZenith.rgb, _AshSkyHorizon.rgb, hw);
    col = lerp(col, _AshSkyGround.rgb, saturate(-y * 6.0));
    
    // --- Leuchten um die Sonne und Daemmerungsband am Horizont
    float glowS = _AshSkyGlow.a;
    float band = exp(-abs(y) * 7.0) * pow(saturate(mu * 0.5 + 0.5), 3.0);
    col += _AshSkyGlow.rgb * glowS * (pow(saturate(mu), 8.0) * 0.45 + band * 0.9);
    
    float sky = saturate(y * 25.0 + 0.3);   // ueber dem Horizont
    
    // --- Sterne und Milchstrasse (im Himmelsgewoelbe)
    float starVis = _AshStars.x * sky;
    float3 stars = 0;
    if (starVis > 0.003)
    {
        float3 c = float3(dot(d, _AshCelX.xyz), dot(d, _AshCelY.xyz), dot(d, _AshCelZ.xyz));
        stars = StarField(c, _AshStars.z, pixAng) + Constellations(c, pixAng);
        float twinkle = 0.85 + 0.15 * sin(_Time.y * 3.1 + dot(c, float3(311.7, 127.1, 74.7)));
        stars *= twinkle;
        stars *= starVis;
        if (_AshVeilNormal.w > 0.5)
        {
            // Der Schleier ist die Milchstrasse – mit ihrer Magie. Er dreht NICHT mit den Sternen: er haengt weltfest
            // ueber der Grossen Kerbe, umso hoeher, je naeher man ihr kommt (SkyDirector rechnet die Ebene je Ort).
            float3 n = _AshVeilNormal.xyz, t1 = _AshVeilTangent.xyz, t2 = normalize(cross(n, t1));
            float v = dot(d, n);
            float along = atan2(dot(d, t2), dot(d, t1));
            float width = _AshVeilTangent.w;
            float band = exp(-pow(v / width, 2.0));
            float core = exp(-pow(v / (width * 0.35), 2.0));
            float drift = _AshVeilParams.x;
            // Leuchtende Nebelstraenge, die ueber Tage wandern; dunkle Adern, die sich verschieben
            float strands = Fbm(float3(along * 3.2 + drift * 0.07, v / width * 1.7, drift * 0.02), 4);
            float fine = Fbm(float3(along * 11.0 - drift * 0.05, v / width * 5.0 + 3.0, drift * 0.03 + 7.0), 3);
            float veins = smoothstep(0.52, 0.62, Fbm(float3(along * 6.0 + 17.0, v / width * 3.0, drift * 0.015 + 31.0), 3));
            float glow = band * smoothstep(0.3, 0.8, strands) * (0.6 + 0.4 * fine) + core * 0.35;
            glow *= 1.0 - veins * 0.75;
            float pulse = _AshVeilParams.y;
            float3 cold = lerp(float3(0.34, 0.52, 0.66), float3(0.62, 0.86, 0.9), core);        // Blau/Teal, heller Kern
            float3 accent = lerp(float3(0.55, 0.36, 0.78), float3(0.95, 0.74, 0.38), fine);       // Violett/Gold nur in Adern und Stoessen
            float accentMix = saturate(veins * band * _AshVeilParams.w + pulse * 0.6 * band * fine);
            float3 veilCol = lerp(cold, accent, accentMix);
            float strength = (0.11 + 0.18 * pulse) * _AshStars.y * (0.6 + 0.4 * _AshVeilParams.z);
            stars += veilCol * glow * strength * starVis;
            // Sterne im dichten Band leicht verschleiert
            stars *= 1.0 - 0.25 * core * starVis;
        }
        else
        {
            float mwBand = exp(-pow(dot(c, normalize(float3(0.25, 0.82, 0.51))) / 0.2, 2.0));
            float mwNoise = Fbm(c * 5.0 + 11.0, 3);
            stars += float3(0.52, 0.55, 0.66) * mwBand * smoothstep(0.35, 0.85, mwNoise) * 0.08 * _AshStars.y * starVis;
        }
        stars *= smoothstep(0.0, 0.12, y);   // am Horizont schluckt der Dunst die Sterne
    }
    col += stars;
    
    // --- Sternschnuppen (vor Monden und Wolken gezeichnet: Wolken verdecken sie)
    float3 meteors = MeteorTrail(d, _AshMeteorA0, _AshMeteorA1, pixAng) + MeteorTrail(d, _AshMeteorB0, _AshMeteorB1, pixAng);
    col += meteors * smoothstep(0.0, 0.08, y);
    
    // --- Monde (vor den Wolken, hinter dem Dunst)
    float coverA, coverB;
    float3 moonA = MoonDisk(d, _AshMoonA, _AshMoonAColor, _AshMoonSeed.x, sunDir, pixAng, coverA);
    float3 moonB = MoonDisk(d, _AshMoonB, _AshMoonBColor, _AshMoonSeed.y, sunDir, pixAng, coverB);
    float moonFade = smoothstep(-0.01, 0.05, y);
    col = lerp(col, col * 0.15, max(coverA, coverB) * moonFade); // Mondscheibe verdeckt Sterne
    col += (moonA + moonB) * moonFade;
    
    // --- Sonnenscheibe
    float sunSize = 0.0095;
    float disk = smoothstep(cos(sunSize * 1.35), cos(sunSize), mu) * _AshSkySun.w;
    float3 sunCol = _AshSkySunColor.rgb;
    float trans = _AshSkySunColor.a;
    
    // --- Wolken (Projektion auf eine Kuppel ueber der Welt)
    float3 lit = _AshCloudLit.rgb, shade = _AshCloudShadow.rgb;
    float cloudMask = 0;
    if (y > -0.02)
    {
        float yy = max(y, 0.0);
        float horizonFade = smoothstep(0.0, 0.14, yy);
        // Hohe Schicht (Cirrus): gestreckt entlang des Windes, schneller
    #if !defined(ASHSKY_LOW)
        {
            float2 p = d.xz / (yy + 0.32) * 0.9;
            float2 w = _AshCloudHighOfs.zw;
            float2 q = float2(dot(p, w) * 0.55, dot(p, float2(-w.y, w.x)) * 1.7);
            float2 o = _AshCloudHighOfs.xy;
            float2 qo = float2(dot(o, w) * 0.55, dot(o, float2(-w.y, w.x)) * 1.7);
            float n = Fbm(float3((q + qo) * 1.3, _AshCloudLowOfs.z * 0.6 + 90.0), 3);
            n = saturate((n - 0.5) * 1.8 + 0.5);
            n *= 0.55 + 0.45 * Fbm(float3(p * 0.9 + o * 0.5, 7.0), 2);   // Zuege aufbrechen, keine Strahlen
            float cov = _AshCloudHigh.x;
            float ch = smoothstep(1.0 - cov, 1.0 - cov + 0.42, n) * _AshCloudHigh.y * 0.75 * horizonFade;
            float3 hc = lit * 1.05 + sunCol * pow(saturate(mu), 4.0) * 0.35 * _AshSkyGlow.a;
            col = lerp(col, hc, ch);
            cloudMask = max(cloudMask, ch * 0.4);
        }
    #endif
        // Untere Schicht (Cumulus)
        {
            float scale = 1.35 / max(_AshCloudLow.z, 0.3);
            float2 uv = d.xz / (yy + 0.09) * scale;
        #if defined(ASHSKY_LOW)
            int oct = 3;
        #elif defined(ASHSKY_HIGH)
            int oct = 5;
        #else
            int oct = 4;
        #endif
            float thick;
            float c = CloudLow(uv, _AshCloudLow.x, _AshCloudLow.y, _AshCloudLow.w, oct, thick);
            c *= horizonFade;
            if (c > 0.001)
            {
                // Dichte Stellen sind dunkler (Unterseiten), duenne Raender heller
                float light = 1.0 - thick * 0.62;
            #if !defined(ASHSKY_LOW)
                // Lichtprobe Richtung Sonne: wird die Wolke dorthin dicker, liegt dieser Teil im Schatten
                float2 toSun = normalize(sunDir.xz + 1e-4) * 0.16;
                float thick2;
                CloudLow(uv + toSun, _AshCloudLow.x, 1.0, _AshCloudLow.w, 2, thick2);
                light *= saturate(1.15 - max(0.0, thick2 - thick) * 2.4);
            #endif
                float3 cc = lerp(shade, lit, light);
                // Silberrand und warmes Gegenlicht nahe der Sonne
                cc += sunCol * pow(saturate(mu), 6.0) * (1.0 - c) * 0.9 * _AshSkyGlow.a * trans;
                col = lerp(col, cc, saturate(c * 1.15));
                cloudMask = max(cloudMask, c);
            }
        }
    }
    
    // Sonne hinter den Wolken
    col += sunCol * disk * 9.0 * trans * sunDiskScale * (1.0 - saturate(cloudMask * 1.3));
    
    // --- Horizontdunst (verschmilzt Gelaende und Himmel)
    float haze = _AshSkyHaze.a;
    col = lerp(col, _AshSkyHaze.rgb, saturate(haze * exp(-abs(y) * 9.0)));
    return col;
}

#endif
