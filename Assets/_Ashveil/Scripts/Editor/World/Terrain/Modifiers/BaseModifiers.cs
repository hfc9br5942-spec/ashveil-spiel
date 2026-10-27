using System.Collections.Generic;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// BASE WORLD FORM + CONTINENTAL SHAPE + REGIONAL ELEVATION (Vorgabe §3): Kueste aus dem Land-Vieleck mit
    /// gebrochener Linie (Buchten, Landzungen, §120), flaches Tiefland, grossraeumige Anhebungen, Meeresgrund.
    /// </summary>
    public sealed class ContinentModifier : TerrainModifier
    {
        public override string Name => "Kontinent und Umland";
        public override int Priority => 0;
        public override ModifierBlend Blend => ModifierBlend.LerpToTarget;

        public override void Apply(TerrainGenerationContext ctx)
        {
            MacroWorldProfile p = ctx.Profile;
            List<Vector2> coast = p.coastline;
            ContinentSdf map = ContinentSdf.Load(p.continentSdf);
            if (map != null) ctx.Log.Add($"Kueste aus der Weltkarte ({map.Width}×{map.Height} Pixel à {map.Pixel} m)");
            float[] land = ctx.Masks.Get(MaskSet.Land);
            float[] h = ctx.Field.H;
            int seed = ctx.TerrainSeed;
            ctx.ForEachCell((i, w) =>
            {
                double x = w.x, z = w.y;
                float sd = map != null ? map.Sample(w.x, w.y) : SignedDistance(coast, w);
                // Kuestenlinie brechen: Buchten und Landzungen, nahe der handgebauten Kueste ruhiger
                float calm = Mathf.Clamp01((Vector2.Distance(w, Vector2.zero) - 1500f) / 3500f);
                sd += (float)AshNoise.Fbm(x, z, 2600, 4, seed + 3) * p.coastNoise * calm;
                float lm = S(-220f, 260f, sd);
                land[i] = lm;

                double wx = x, wz = z;
                AshNoise.Warp(ref wx, ref wz, 7000, 700, seed + 9);
                float baseH = p.lowlandHeight + (float)AshNoise.Fbm(wx, wz, p.regionalNoiseScale, 4, seed + 17) * p.regionalNoiseAmp;
                baseH += 22f * S(0f, 7000f, sd);   // Inland steigt sanft an
                foreach (RegionalElevation r in p.regionalElevation)
                {
                    float d = Vector2.Distance(w, r.center) / Mathf.Max(1f, r.radius);
                    if (d >= 1f) continue;
                    float f = 1f - d * d;
                    baseH += r.height * f * f;
                }
                baseH = Mathf.Max(baseH, p.seaLevel + 3f);
                float sea = p.seaLevel - 10f - 150f * S(0f, 5500f, -sd) + (float)AshNoise.Fbm(x, z, 3000, 3, seed + 23) * 14f;
                h[i] = Mathf.Lerp(sea, baseH, lm);
            });
        }

        /// <summary>Vorzeichenbehafteter Abstand zum Vieleck (innen positiv).</summary>
        public static float SignedDistance(List<Vector2> poly, Vector2 p)
        {
            if (poly == null || poly.Count < 3) return 1e6f;
            bool inside = false;
            float best = float.MaxValue;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                float d = (p - (a + ab * t)).sqrMagnitude;
                if (d < best) best = d;
            }
            float dist = Mathf.Sqrt(best);
            return inside ? dist : -dist;
        }

        public static bool InPolygon(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }
    }

    /// <summary>
    /// MOUNTAIN BELTS + RIDGES (Vorgabe §17–20): Distanzfeld zum Kamm-Linienzug, Profil Vorberge → steigende
    /// Hoehenzuege → Hauptkamm → Nebenkaemme, Ridged Multifractal mit Domain Warp, Gipfel variieren entlang des Kamms.
    /// </summary>
    public sealed class MountainBeltModifier : TerrainModifier
    {
        public override string Name => "Gebirgszuege";
        public override int Priority => 100;

        public override void Apply(TerrainGenerationContext ctx)
        {
            float[] h = ctx.Field.H;
            float[] mask = ctx.Masks.Get(MaskSet.Mountain);
            float[] land = ctx.Masks.Get(MaskSet.Land);
            foreach (MountainBeltDefinition b in ctx.Profile.mountainBelts)
            {
                if (b.controlPoints.Count < 2) continue;
                Vector2[] pts = b.controlPoints.ToArray();
                float length = WorldPolyline.Length(pts);
                int seed = ctx.TerrainSeed ^ (b.seed * 7919);
                float half = b.width * 0.5f;
                ctx.ForEachCell((i, w) =>
                {
                    double wx = w.x, wz = w.y;
                    AshNoise.Warp(ref wx, ref wz, 6000, Mathf.Min(520f, b.width * 0.08f), seed + 1);   // Makroform bleibt (§20)
                    Vector2 q = new Vector2((float)wx, (float)wz);
                    float d = WorldPolyline.Distance(pts, q, out int seg, out _, out float along, out _);
                    if (d > half * 1.25f) return;
                    float side = WorldPolyline.Side(pts, q, seg);
                    float hw = half * (1f + b.asymmetry * side);
                    float u = d / Mathf.Max(1f, hw);
                    if (u >= 1.15f) return;
                    // Enden abrunden
                    float taper = Mathf.Min(2600f, length * 0.18f);
                    float ends = S(0f, taper, along) * S(length, length - taper, along);

                    float foothills = S(1.15f, 0.5f, u);
                    float core = S(0.78f, 0.0f, u);
                    double ridge = AshNoise.Ridged(wx, wz, b.ridgeScale, 6, seed + 11);
                    double ridgeMix = AshNoise.Lerp(0.75, 0.45 + ridge * 0.9, b.ridgeStrength);
                    double pv = 1.0 + b.peakVariation * AshNoise.Fbm(along, 377.0, 5200, 3, seed + 13);
                    double secondary = AshNoise.Ridged(wx + 913, wz - 417, b.ridgeScale * 0.42, 4, seed + 17);
                    double crest = (b.peakElevation - b.baseElevation) * System.Math.Pow(core, 1.25) * ridgeMix * pv;
                    double foot = b.baseElevation * foothills * (0.65 + 0.55 * secondary);
                    // Taeler zwischen den Graten: wo der Grat schwach ist, tiefer schneiden
                    double valleys = -b.valleyStrength * (b.peakElevation - b.baseElevation) * 0.22 * core * (1.0 - ridge);
                    double rough = b.roughness * 28.0 * AshNoise.Fbm(wx, wz, 520, 4, seed + 19) * (0.3 + core);
                    float add = (float)((foot + crest + valleys + rough) * ends) * Mathf.Clamp01(land[i] * 1.2f);
                    h[i] = Combine(h[i], add, 1f, ModifierBlend.Add);
                    float m = core * ends;
                    if (m > mask[i]) mask[i] = m;
                });
            }
        }
    }

    /// <summary>VALLEYS (Vorgabe §25): Talsohle aus den Kontrollpunkten, Flanken V bis U, Maeandern; senkt nur ab.</summary>
    public sealed class ValleyModifier : TerrainModifier
    {
        public override string Name => "Taeler und Schluchten";
        public override int Priority => 200;
        public override ModifierBlend Blend => ModifierBlend.Min;

        public override void Apply(TerrainGenerationContext ctx)
        {
            float[] h = ctx.Field.H;
            float[] vmask = ctx.Masks.Get(MaskSet.Valley);
            foreach (ValleyDefinition v in ctx.Profile.valleys)
            {
                if (v.centerline.Count < 2) continue;
                var pts = new Vector2[v.centerline.Count];
                for (int k = 0; k < pts.Length; k++) pts[k] = new Vector2(v.centerline[k].x, v.centerline[k].z);
                int seed = AshNoise.Derive(ctx.TerrainSeed, v.id);
                float half = v.width * 0.5f, floorHalf = v.floorWidth * 0.5f;
                ctx.ForEachCell((i, w) =>
                {
                    double wx = w.x, wz = w.y;
                    AshNoise.Warp(ref wx, ref wz, 2400, v.meander, seed);
                    Vector2 q = new Vector2((float)wx, (float)wz);
                    float d = WorldPolyline.Distance(pts, q, out int seg, out float t, out _, out _);
                    if (d > half) return;
                    float floor = Mathf.Lerp(v.centerline[seg].y, v.centerline[seg + 1].y, t) + (float)AshNoise.Fbm(w.x, w.y, 900, 3, seed + 3) * v.noise;
                    float tt = Mathf.Clamp01((d - floorHalf) / Mathf.Max(1f, half - floorHalf));
                    float wall = Mathf.Pow(tt, v.sideSlope);
                    float target = floor + (h[i] - floor) * wall;
                    if (target < h[i]) h[i] = target;
                    float m = 1f - tt;
                    if (m > vmask[i]) vmask[i] = m;
                });
            }
        }
    }

    /// <summary>
    /// PASS (Vorgabe §24): laenglicher Sattel entlang Start→Ende mit Schultern. Die Sohle folgt einem geglaetteten
    /// Laengsprofil (hoechstens 15° Steigung, nie hoeher als die Sattelhoehe) – so bleibt der Pass begehbar, auch wenn
    /// die Grate daneben zerrissen sind. Seitlich steigen die Schultern an (kein Krater, keine Rinne).
    /// </summary>
    public sealed class PassModifier : TerrainModifier
    {
        public override string Name => "Paesse";
        public override int Priority => 210;
        public override ModifierBlend Blend => ModifierBlend.LerpToTarget;

        public override void Apply(TerrainGenerationContext ctx)
        {
            MacroHeightField f = ctx.Field;
            float[] h = f.H;
            foreach (PassDefinition ps in ctx.Profile.passes)
            {
                Vector2 a = ps.start, b = ps.end, ab = b - a;
                float len = ab.magnitude;
                if (len < 1f) continue;
                Vector2 dir = ab / len;
                int seed = AshNoise.Derive(ctx.TerrainSeed, ps.id);
                float half = ps.width * 0.5f;
                // Laengsprofil: Gelaende abtasten, glaetten, Steigung begrenzen, nie ueber der Sattelhoehe
                int n = Mathf.Max(3, Mathf.CeilToInt(len / f.CellSize) + 1);
                var prof = new float[n];
                for (int k = 0; k < n; k++)
                {
                    float t = (float)k / (n - 1);
                    Vector2 q = a + ab * t;
                    float c = 2f * Mathf.Abs(t - 0.5f);
                    prof[k] = Mathf.Min(f.SampleBilinear(q.x, q.y), ps.targetElevation + ps.traversalBias * c * c);
                }
                var sm = new float[n];
                for (int k = 0; k < n; k++)
                {
                    float sum = 0f;
                    int cnt = 0;
                    for (int j2 = -5; j2 <= 5; j2++)
                    {
                        sum += prof[Mathf.Clamp(k + j2, 0, n - 1)];
                        cnt++;
                    }
                    sm[k] = sum / cnt;
                }
                float maxDh = Mathf.Tan(15f * Mathf.Deg2Rad) * len / (n - 1);
                for (int k = 1; k < n; k++) sm[k] = Mathf.Min(sm[k], sm[k - 1] + maxDh);
                for (int k = n - 2; k >= 0; k--) sm[k] = Mathf.Min(sm[k], sm[k + 1] + maxDh);
                ctx.ForEachCell((i, w) =>
                {
                    Vector2 rel = w - a;
                    float along = Vector2.Dot(rel, dir);
                    float t = along / len;
                    if (t < -0.2f || t > 1.2f) return;
                    float perp = Mathf.Abs(rel.x * -dir.y + rel.y * dir.x);
                    if (perp > half + ps.shoulderFalloff) return;
                    float tc = Mathf.Clamp01(t);
                    float fk = tc * (n - 1);
                    int k0 = Mathf.Min((int)fk, n - 2);
                    float target = Mathf.Lerp(sm[k0], sm[k0 + 1], fk - k0) + (float)AshNoise.Fbm(w.x, w.y, 300, 3, seed) * ps.roughness * 0.3f;
                    float lengthwise = S(-0.2f, 0.05f, t) * S(1.2f, 0.95f, t);
                    // Sohle: auf das Profil ziehen (auch leicht anheben); Schultern: nur absenken, steigend nach aussen
                    float core = S(half * 1.6f, half * 0.6f, perp) * lengthwise;
                    h[i] = Mathf.Lerp(h[i], target, core);
                    float shoulder = S(half + ps.shoulderFalloff, half, perp) * lengthwise;
                    float wall = target + Mathf.Max(0f, perp - half) * 0.45f;
                    h[i] = Combine(h[i], wall, shoulder, ModifierBlend.Min);
                });
            }
        }
    }

    /// <summary>PLATEAUS (Vorgabe §26): erkennbare Oberflaeche, natuerlich gebrochene Raender, Klippenstaerke.</summary>
    public sealed class PlateauModifier : TerrainModifier
    {
        public override string Name => "Hochplateaus";
        public override int Priority => 220;
        public override ModifierBlend Blend => ModifierBlend.LerpToTarget;

        public override void Apply(TerrainGenerationContext ctx)
        {
            float[] h = ctx.Field.H;
            float[] pm = ctx.Masks.Get(MaskSet.Plateau);
            float[] cliff = ctx.Masks.Get(MaskSet.Cliff);
            foreach (PlateauDefinition pl in ctx.Profile.plateaus)
            {
                float yaw = pl.yaw * Mathf.Deg2Rad, cs = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
                int seed = AshNoise.Derive(ctx.TerrainSeed, pl.id);
                float rMin = Mathf.Min(pl.radii.x, pl.radii.y);
                float band = pl.falloff * (1f - pl.cliffStrength * 0.75f) / rMin;
                ctx.ForEachCell((i, w) =>
                {
                    Vector2 r = w - pl.center;
                    float lx = r.x * cs - r.y * sn, lz = r.x * sn + r.y * cs;
                    float e = Mathf.Sqrt((lx / pl.radii.x) * (lx / pl.radii.x) + (lz / pl.radii.y) * (lz / pl.radii.y));
                    if (e > 1.6f) return;
                    e += (float)AshNoise.Fbm(w.x, w.y, rMin * 0.55, 4, seed) * pl.edgeRoughness;
                    float wgt = S(1f + band, 1f, e);
                    if (wgt <= 0f) return;
                    float target = pl.targetHeight + (float)AshNoise.Fbm(w.x, w.y, rMin * 0.4, 4, seed + 5) * pl.interiorNoise;
                    h[i] = Combine(h[i], target, wgt, ModifierBlend.LerpToTarget);
                    if (wgt > pm[i]) pm[i] = wgt;
                    float edge = wgt * (1f - wgt) * 4f * pl.cliffStrength;
                    if (edge > cliff[i]) cliff[i] = edge;
                });
            }
        }
    }

    /// <summary>LAKES (Vorgabe §40): Seebecken in natuerlichen Senken – nie zufaellige Kreise.</summary>
    public sealed class LakeModifier : TerrainModifier
    {
        public override string Name => "Seen";
        public override int Priority => 230;
        public override ModifierBlend Blend => ModifierBlend.Min;

        public override void Apply(TerrainGenerationContext ctx)
        {
            float[] h = ctx.Field.H;
            float[] lakeM = ctx.Masks.Get(MaskSet.Lake);
            foreach (LakeDefinition lk in ctx.Profile.lakes)
            {
                float yaw = lk.yaw * Mathf.Deg2Rad, cs = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
                int seed = AshNoise.Derive(ctx.TerrainSeed, lk.id);
                float rMin = Mathf.Min(lk.radii.x, lk.radii.y);
                ctx.ForEachCell((i, w) =>
                {
                    Vector2 r = w - lk.center;
                    float lx = r.x * cs - r.y * sn, lz = r.x * sn + r.y * cs;
                    float e = Mathf.Sqrt((lx / lk.radii.x) * (lx / lk.radii.x) + (lz / lk.radii.y) * (lz / lk.radii.y));
                    e += (float)AshNoise.Fbm(w.x, w.y, rMin * 0.6, 3, seed) * 0.15f;
                    float outer = 1f + lk.shoreWidth / rMin;
                    if (e > outer) return;
                    // Becken: tiefster Punkt in der Mitte, Ufer steigt bis zum Umland
                    float bowl = Mathf.Lerp(lk.floorElevation, lk.waterLevel + 2f, Mathf.Clamp01(e * e));
                    float target = e < 1f ? bowl : Mathf.Lerp(lk.waterLevel + 2f, h[i], S(1f, outer, e));
                    if (target < h[i]) h[i] = target;
                    if (e < 1f) lakeM[i] = Mathf.Max(lakeM[i], 1f - e * 0.5f);
                });
            }
        }
    }
}
