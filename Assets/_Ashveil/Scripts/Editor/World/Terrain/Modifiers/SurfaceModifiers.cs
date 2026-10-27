using System.Collections.Generic;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>Hoehenprofil entlang eines Linienzugs (fuer Fluesse und Wege).</summary>
    internal static class PathProfile
    {
        public static List<Vector2> Resample(IReadOnlyList<Vector2> path, float step)
        {
            var list = new List<Vector2>();
            float len = WorldPolyline.Length(path);
            int n = Mathf.Max(2, Mathf.CeilToInt(len / step) + 1);
            for (int k = 0; k < n; k++) list.Add(WorldPolyline.PointAt(path, len * k / (n - 1)));
            return list;
        }
    }

    /// <summary>
    /// RIVER CARVING (Vorgabe §38): Flussbett + kleines Tal + sanfte Umgebung. Das Bett faellt garantiert bergab
    /// (laufendes Minimum von der Quelle zur Muendung), die Breite waechst mit der Einzugsflaeche.
    /// </summary>
    public sealed class RiverModifier : TerrainModifier
    {
        public override string Name => "Flussbetten";
        public override int Priority => 400;
        public override ModifierBlend Blend => ModifierBlend.Min;

        public override void Apply(TerrainGenerationContext ctx)
        {
            MacroHeightField f = ctx.Field;
            float[] h = f.H;
            float[] rm = ctx.Masks.Get(MaskSet.River);
            foreach (RiverDefinition r in ctx.Profile.rivers)
            {
                if (r.path.Count < 2) continue;
                List<Vector2> pts = PathProfile.Resample(r.path, f.CellSize);
                var bed = new float[pts.Count];
                float run = float.MaxValue;
                for (int k = 0; k < pts.Count; k++)
                {
                    float s = f.SampleBilinear(pts[k].x, pts[k].y) - r.depth;
                    run = Mathf.Min(run - 0.05f, s);   // immer leicht fallend
                    bed[k] = run;
                }
                Vector2[] arr = pts.ToArray();
                float half = Mathf.Max(r.width * 0.5f, f.CellSize * 0.6f);
                float bank = half + Mathf.Max(160f, r.width * 5f);
                ctx.ForEachCell((i, w) =>
                {
                    float d = WorldPolyline.Distance(arr, w, out int seg, out float t, out _, out _);
                    if (d > bank) return;
                    float b = Mathf.Lerp(bed[seg], bed[Mathf.Min(seg + 1, bed.Length - 1)], t);
                    float wall = Mathf.Clamp01((d - half) / (bank - half));
                    float target = b + (h[i] - b) * wall * wall;
                    if (target < h[i]) h[i] = target;
                    float m = 1f - wall;
                    if (m > rm[i]) rm[i] = m;
                });
            }
        }
    }

    /// <summary>
    /// ROAD / TRAVEL CORRIDORS (Vorgabe §43–45): baut keine Strasse, macht den Korridor begehbar – Hoehenprofil
    /// glaetten, Steigung begrenzen, schneiden/aufschuetten. MAIN/SECONDARY stark, EXPLORATION schwach, SECRET kaum.
    /// </summary>
    public sealed class RoadCorridorModifier : TerrainModifier
    {
        public override string Name => "Reisekorridore";
        public override int Priority => 500;
        public override ModifierBlend Blend => ModifierBlend.FlattenTowards;

        public override void Apply(TerrainGenerationContext ctx)
        {
            MacroHeightField f = ctx.Field;
            float[] h = f.H;
            float[] rm = ctx.Masks.Get(MaskSet.Road);
            float[] riverMask = ctx.Masks.Get(MaskSet.River);
            foreach (RoadCorridorDefinition road in ctx.Profile.roads)
            {
                if (road.path.Count < 2 || road.routeClass == RouteClass.Secret) continue;
                float step = f.CellSize;
                List<Vector2> pts = PathProfile.Resample(road.path, step);
                int n = pts.Count;
                var prof = new float[n];
                for (int k = 0; k < n; k++) prof[k] = f.SampleBilinear(pts[k].x, pts[k].y);
                // glaetten (gleitendes Mittel) und Steigung begrenzen (vor- und rueckwaerts)
                var sm = new float[n];
                for (int k = 0; k < n; k++)
                {
                    float sum = 0f;
                    int c = 0;
                    for (int j = -4; j <= 4; j++)
                    {
                        int q = Mathf.Clamp(k + j, 0, n - 1);
                        sum += prof[q];
                        c++;
                    }
                    sm[k] = sum / c;
                }
                float maxDh = Mathf.Tan(road.maxSlope * Mathf.Deg2Rad) * step;
                for (int k = 1; k < n; k++) sm[k] = Mathf.Clamp(sm[k], sm[k - 1] - maxDh, sm[k - 1] + maxDh);
                for (int k = n - 2; k >= 0; k--) sm[k] = Mathf.Clamp(sm[k], sm[k + 1] - maxDh, sm[k + 1] + maxDh);
                float classW = road.routeClass == RouteClass.Main ? 1f : road.routeClass == RouteClass.Secondary ? 0.75f : 0.3f;
                Vector2[] arr = pts.ToArray();
                float half = Mathf.Max(road.width * 0.5f, f.CellSize * 0.5f);
                float shoulder = half + f.CellSize * 2.5f;
                ctx.ForEachCell((i, w) =>
                {
                    float d = WorldPolyline.Distance(arr, w, out int seg, out float t, out _, out _);
                    if (d > shoulder || riverMask[i] > 0.5f) return;   // Flussbett bleibt (spaeter Bruecke/Furt)
                    float target = Mathf.Lerp(sm[seg], sm[Mathf.Min(seg + 1, n - 1)], t);
                    float wgt = S(shoulder, half, d) * classW;
                    float k2 = h[i] > target ? road.cutStrength : road.fillStrength;
                    // Hauptwege folgen dem geglaetteten Profil fast ganz (begehbar), Nebenwege weicher. Begrenzt: hoechstens
                    // 12 m Damm und 35 m Einschnitt – sonst entstuenden kuenstliche Waelle durch die Landschaft
                    float strength = road.routeClass == RouteClass.Main ? k2 : k2 * Mathf.Max(0.2f, road.flattenStrength);
                    float delta = Mathf.Clamp(target - h[i], -35f, 12f);
                    h[i] += delta * wgt * strength;
                    if (wgt > rm[i]) rm[i] = wgt;
                });
            }
        }
    }

    /// <summary>LANDMARK MODIFIERS (Vorgabe §27, §113–114): handgesetzte uralte Spuren. Nie zufaellig.</summary>
    public sealed class AncientScarModifier : TerrainModifier
    {
        public override string Name => "Uralte Spuren";
        public override int Priority => 600;
        public override ModifierBlend Blend => ModifierBlend.Subtract;

        public override void Apply(TerrainGenerationContext ctx)
        {
            float[] h = ctx.Field.H;
            float[] glass = ctx.Masks.Get(MaskSet.VeilGlass), anc = ctx.Masks.Get(MaskSet.AncientMaterial), frac = ctx.Masks.Get(MaskSet.Fracture);
            foreach (AncientScarDefinition a in ctx.Profile.ancientScars)
            {
                if (a.representsExisting) continue;   // liegt in Handarbeit (Fussabdruck) – nur Daten
                float yaw = a.yaw * Mathf.Deg2Rad, cs = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
                int seed = AshNoise.Derive(ctx.TerrainSeed, a.id);
                ctx.ForEachCell((i, w) =>
                {
                    Vector2 r = w - a.center;
                    float lx = r.x * cs + r.y * sn, lz = -r.x * sn + r.y * cs;   // lx entlang der Laenge
                    float hl = a.length * 0.5f, hw = a.width * 0.5f;
                    if (Mathf.Abs(lx) > hl * 1.5f || Mathf.Abs(lz) > hw * 2.5f + 200f) return;
                    float wgt;
                    switch (a.type)
                    {
                        case AncientScarType.StraightFault:
                            // ungewoehnlich gerade: kein Warp, harte Kante, eine Seite versetzt
                            wgt = S(hw * 1.8f, hw * 0.6f, Mathf.Abs(lz)) * S(hl, hl * 0.85f, Mathf.Abs(lx));
                            h[i] -= a.depth * wgt;
                            if (lz > 0f) h[i] += a.depth * 0.6f * S(hl, hl * 0.85f, Mathf.Abs(lx)) * S(0f, hw * 3f, lz);
                            frac[i] = Mathf.Max(frac[i], wgt);
                            break;
                        default:
                            float e = Mathf.Sqrt((lx / hl) * (lx / hl) + (lz / hw) * (lz / hw)) + (float)AshNoise.Fbm(w.x, w.y, 400, 3, seed) * 0.12f;
                            wgt = S(1.15f, 0.55f, e);
                            h[i] -= a.depth * wgt;
                            if (a.type == AncientScarType.GlassField) glass[i] = Mathf.Max(glass[i], wgt);
                            anc[i] = Mathf.Max(anc[i], wgt * 0.8f);
                            break;
                    }
                });
            }
        }
    }

    /// <summary>LOCAL TERRAIN DETAIL (Vorgabe §42): kleine Amplitude, spart Wege, Fluesse und Taeler aus.</summary>
    public sealed class LocalDetailModifier : TerrainModifier
    {
        public override string Name => "Lokales Detail";
        public override int Priority => 700;

        public override void Apply(TerrainGenerationContext ctx)
        {
            float[] h = ctx.Field.H;
            float[] road = ctx.Masks.Get(MaskSet.Road), river = ctx.Masks.Get(MaskSet.River), land = ctx.Masks.Get(MaskSet.Land);
            float[] mountain = ctx.Masks.Get(MaskSet.Mountain);
            float amp = ctx.Profile.localDetailAmp;
            int seed = ctx.DetailSeed;
            ctx.ForEachCell((i, w) =>
            {
                float keep = (1f - Mathf.Max(road[i], river[i])) * land[i];
                if (keep <= 0f) return;
                float n = (float)AshNoise.Fbm(w.x, w.y, 640, 4, seed) * amp * (1f + mountain[i] * 2f);
                h[i] += n * keep;
            });
        }
    }

    /// <summary>
    /// EROSION (Vorgabe §41): thermische Hangentlastung auf dem groben Feld – Material rutscht, wo der Hang steiler
    /// als der Schuettwinkel ist. Sequentiell (deterministisch), geschuetzte Bereiche, Wege und Fluesse bleiben.
    /// </summary>
    public sealed class MacroErosionPass : TerrainModifier
    {
        public override string Name => "Erosion (thermisch)";
        public override int Priority => 800;

        public override void Apply(TerrainGenerationContext ctx)
        {
            MacroHeightField f = ctx.Field;
            int w = f.Width, hh = f.Height;
            float[] h = f.H;
            float[] land = ctx.Masks.Get(MaskSet.Land);
            float[] road = ctx.Masks.Get(MaskSet.Road), river = ctx.Masks.Get(MaskSet.River);
            float talus = Mathf.Tan(ctx.Profile.talusAngle * Mathf.Deg2Rad) * f.CellSize;
            float strength = ctx.Profile.erosionStrength;
            var delta = new float[h.Length];
            for (int it = 0; it < ctx.Profile.erosionIterations; it++)
            {
                System.Array.Clear(delta, 0, delta.Length);
                for (int z = 1; z < hh - 1; z++)
                    for (int x = 1; x < w - 1; x++)
                    {
                        int i = z * w + x;
                        if (land[i] < 0.5f || road[i] > 0.5f || river[i] > 0.5f) continue;
                        int lowest = -1;
                        float maxDiff = 0f;
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dz == 0) continue;
                                int j = i + dz * w + dx;
                                float diff = h[i] - h[j];
                                if (dx != 0 && dz != 0) diff *= 0.7071f;
                                if (diff > maxDiff)
                                {
                                    maxDiff = diff;
                                    lowest = j;
                                }
                            }
                        if (lowest < 0 || maxDiff <= talus) continue;
                        float move = (maxDiff - talus) * 0.5f * strength;
                        delta[i] -= move;
                        delta[lowest] += move;
                    }
                for (int i = 0; i < h.Length; i++) h[i] += delta[i];
            }
        }
    }

    /// <summary>FINAL SMOOTHING (Vorgabe §3): leichter 3×3-Weichzeichner gegen Treppen, Klippen bleiben.</summary>
    public sealed class SmoothingModifier : TerrainModifier
    {
        public override string Name => "Glaetten";
        public override int Priority => 900;

        public override void Apply(TerrainGenerationContext ctx)
        {
            MacroHeightField f = ctx.Field;
            int w = f.Width, hh = f.Height;
            float[] src = (float[])f.H.Clone();
            float[] cliff = ctx.Masks.Get(MaskSet.Cliff), river = ctx.Masks.Get(MaskSet.River), road = ctx.Masks.Get(MaskSet.Road);
            System.Threading.Tasks.Parallel.For(1, hh - 1, z =>
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int i = z * w + x;
                    float sum = src[i] * 4f + (src[i - 1] + src[i + 1] + src[i - w] + src[i + w]) * 2f +
                                src[i - w - 1] + src[i - w + 1] + src[i + w - 1] + src[i + w + 1];
                    float blurred = sum / 16f;
                    float keep = Mathf.Max(Mathf.Clamp01(cliff[i]) * 0.8f, Mathf.Max(river[i], road[i] * 0.6f));
                    f.H[i] = Mathf.Lerp(blurred, src[i], keep);
                }
            });
        }
    }

    /// <summary>
    /// PROTECTED REGIONS + TRANSITION (Vorgabe §10, §75): im geschuetzten Bereich steht exakt die Handarbeit
    /// (nur gelesen), aussen gleicht eine Uebergangszone die Makrohoehe an den Rand an. Zuletzt, damit nichts danach
    /// die Naht wieder verschiebt.
    /// </summary>
    public sealed class ProtectedAreaModifier : TerrainModifier
    {
        public override string Name => "Schutz und Uebergang";
        public override int Priority => 1000;
        public override ModifierBlend Blend => ModifierBlend.LerpToTarget;

        public override void Apply(TerrainGenerationContext ctx)
        {
            if (ctx.Protected == null) return;
            float[] h = ctx.Field.H;
            float[] pm = ctx.Masks.Get(MaskSet.Protected), tm = ctx.Masks.Get(MaskSet.Transition);
            float dist = ctx.Profile.transitionDistance;
            ProtectedHeightSampler ps = ctx.Protected;
            ctx.ForEachCell((i, w) =>
            {
                if (ps.TrySample(w.x, w.y, out float ph) && ps.Inside(w.x, w.y))
                {
                    h[i] = ph;
                    pm[i] = 1f;
                    return;
                }
                float d = ps.NearestEdge(w.x, w.y, out float edge, out _);
                if (d >= dist) return;
                float wgt = S(dist, 0f, d);
                h[i] = Mathf.Lerp(h[i], edge, wgt);
                tm[i] = wgt;
            });
        }

        /// <summary>Gleiche Regel fuer einen beliebigen Punkt (Kachel-Generator nutzt sie auf voller Aufloesung).</summary>
        public static float BlendAt(ProtectedHeightSampler ps, float dist, float x, float z, float macro, out float weight)
        {
            weight = 0f;
            if (ps == null) return macro;
            float d = ps.NearestEdge(x, z, out float edge, out _);
            if (d >= dist) return macro;
            weight = (float)AshNoise.SmoothStep(dist, 0f, d);
            return Mathf.Lerp(macro, edge, weight);
        }
    }
}
