using System.Collections.Generic;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// GREAT VEIL SCAR (Vorgabe §28–34): Explosion + Aufreissen + Aufschmelzung + Aufwerfung + spaetere Erosion.
    /// Innen stark negativ (Sohle aus den Kontrollpunkten, faellt nach Westen ins Meer), Waende mit Terrassen und
    /// Abbruechen, aufgeworfene Schultern nur auf ausgewaehlten Abschnitten, Bruchlinien draussen abnehmend,
    /// Nebenbrueche (Bruchmarker Riss). Nicht symmetrisch: Warp, regionales Rauschen, Asymmetrie je Punkt.
    /// Schreibt die Masken VeilGlass / MeltRock / Fracture / AncientMaterial und den Schleiereinfluss.
    /// </summary>
    public sealed class VeilScarModifier : TerrainModifier
    {
        public override string Name => "Grosse Kerbe";
        public override int Priority => 300;
        public override ModifierBlend Blend => ModifierBlend.LerpToTarget;

        public override void Apply(TerrainGenerationContext ctx)
        {
            VeilScarDefinition def = ctx.Profile.veilScar;
            if (def == null || def.points.Count < 2) return;
            var pts = new Vector2[def.points.Count];
            float maxW = 0f;
            for (int k = 0; k < pts.Length; k++)
            {
                pts[k] = def.points[k].position;
                maxW = Mathf.Max(maxW, def.points[k].width);
            }
            float[] h = ctx.Field.H;
            float[] inner = ctx.Masks.Get(MaskSet.ScarInner), rimM = ctx.Masks.Get(MaskSet.ScarRim);
            float[] glass = ctx.Masks.Get(MaskSet.VeilGlass), melt = ctx.Masks.Get(MaskSet.MeltRock);
            float[] frac = ctx.Masks.Get(MaskSet.Fracture), ancient = ctx.Masks.Get(MaskSet.AncientMaterial);
            float[] veil = ctx.Masks.Get(MaskSet.VeilInfluence);
            float[] cliff = ctx.Masks.Get(MaskSet.Cliff);
            float[] land = ctx.Masks.Get(MaskSet.Land);
            int seed = AshNoise.Derive(ctx.TerrainSeed, def.id);
            float reach = maxW * 0.5f + 9000f;

            ctx.ForEachCell((i, w) =>
            {
                double wx = w.x, wz = w.y;
                AshNoise.Warp(ref wx, ref wz, 5200, 380, seed);
                Vector2 q = new Vector2((float)wx, (float)wz);
                float d = WorldPolyline.Distance(pts, q, out int seg, out float t, out float along, out _);
                if (d > reach) return;
                VeilScarPoint a = def.points[seg], b = def.points[seg + 1];
                float width = Mathf.Lerp(a.width, b.width, t);
                float floor = Mathf.Lerp(a.floorElevation, b.floorElevation, t);
                float rimUp = Mathf.Lerp(a.rimUplift, b.rimUplift, t);
                float fracture = Mathf.Lerp(a.fractureStrength, b.fractureStrength, t);
                float terrace = Mathf.Lerp(a.terraceStrength, b.terraceStrength, t);
                float asym = Mathf.Lerp(a.asymmetry, b.asymmetry, t);
                float glassP = Mathf.Lerp(a.glassFieldProbability, b.glassFieldProbability, t);
                float veilI = Mathf.Lerp(a.veilIntensity, b.veilIntensity, t);
                float side = WorldPolyline.Side(pts, q, seg);

                // Einsturzbuchten: die Breite schwankt entlang der Kerbe (§30)
                float collapse = 1f + 0.22f * (float)AshNoise.Fbm(along, 91.0, 7000, 3, seed + 7);
                float half = width * 0.5f * collapse * (1f + asym * 0.18f * side);
                float u = d / Mathf.Max(1f, half);

                float hv = h[i];
                // ---- Aussen: aufgeworfene Schultern auf ausgewaehlten Abschnitten (§32) + Bruchlinien (§31)
                float segmentSel = S(-0.15f, 0.35f, (float)AshNoise.Fbm(along, 17.0, 9000, 2, seed + 11));
                float rimSide = 1f + asym * side * 0.6f;
                float rimShape = Mathf.Exp(-Mathf.Pow((u - 1.05f) / 0.42f, 2f));
                float rimAdd = rimUp * rimShape * segmentSel * rimSide;
                float outside = Mathf.Clamp01(u - 1f);
                float fracLines = (float)AshNoise.Ridged(wx, wz, 1300, 4, seed + 13);
                float fracAdd = fracture * (fracLines - 0.45f) * 55f * Mathf.Exp(-outside * half / 2600f) * S(0.85f, 1.05f, u);
                hv += (rimAdd + fracAdd) * Mathf.Clamp01(land[i] * 1.5f + 0.4f);

                // ---- Innen: Sohle + terrassierte Waende (§31); Sohle unter Meeresniveau fuellt sich (Fjord)
                if (u < 1.05f)
                {
                    float s = S(0.28f, 1.02f, u);
                    if (terrace > 0f)
                    {
                        const float steps = 6f;
                        float stepped = Mathf.Round(s * steps) / steps;
                        s = Mathf.Lerp(s, stepped + (s - stepped) * 0.25f, terrace * 0.7f);
                    }
                    // Abbrueche: Rutschmassen verbreitern die Sohle stellenweise
                    float slide = S(0.3f, 0.7f, (float)AshNoise.Fbm(wx, wz, 2200, 3, seed + 17)) * 0.18f;
                    s = Mathf.Clamp01(s - slide * (1f - s));
                    float floorH = floor + (float)AshNoise.Fbm(wx, wz, 1400, 4, seed + 19) * 18f;
                    float target = Mathf.Lerp(floorH, hv, s);
                    hv = Mathf.Min(hv, target);
                    float inn = 1f - s;
                    inner[i] = Mathf.Max(inner[i], inn);
                    float wallness = s * (1f - s) * 4f;
                    melt[i] = Mathf.Max(melt[i], wallness * 0.9f);
                    cliff[i] = Mathf.Max(cliff[i], wallness);
                    float gn = (float)AshNoise.Fbm(wx, wz, 1800, 3, seed + 23) * 0.5f + 0.5f;
                    if (gn > 1f - glassP) glass[i] = Mathf.Max(glass[i], inn * S(1f - glassP, 1f - glassP + 0.15f, gn));
                }
                rimM[i] = Mathf.Max(rimM[i], rimShape * S(0.8f, 1.0f, u));
                frac[i] = Mathf.Max(frac[i], fracture * fracLines * Mathf.Exp(-outside * half / 2600f));
                ancient[i] = Mathf.Max(ancient[i], rimShape * segmentSel);
                veil[i] = Mathf.Max(veil[i], veilI * Mathf.Exp(-Mathf.Max(0f, d - half) / 5000f));
                h[i] = hv;
            });

            foreach (SecondaryFractureDefinition br in def.branches) ApplyBranch(ctx, br, seed);
        }

        /// <summary>Nebenbruch (§33): keilt zur Spitze aus, Sohle steigt bis knapp unter das Umland.</summary>
        private static void ApplyBranch(TerrainGenerationContext ctx, SecondaryFractureDefinition br, int scarSeed)
        {
            if (br.path.Count < 2) return;
            Vector2[] pts = br.path.ToArray();
            float length = WorldPolyline.Length(pts);
            float[] h = ctx.Field.H;
            float[] inner = ctx.Masks.Get(MaskSet.ScarInner), melt = ctx.Masks.Get(MaskSet.MeltRock);
            float[] cliff = ctx.Masks.Get(MaskSet.Cliff), veil = ctx.Masks.Get(MaskSet.VeilInfluence), frac = ctx.Masks.Get(MaskSet.Fracture);
            int seed = AshNoise.Derive(scarSeed, br.id);
            ctx.ForEachCell((i, w) =>
            {
                double wx = w.x, wz = w.y;
                AshNoise.Warp(ref wx, ref wz, 2600, 260, seed);
                Vector2 q = new Vector2((float)wx, (float)wz);
                float d = WorldPolyline.Distance(pts, q, out _, out _, out float along, out _);
                float t = Mathf.Clamp01(along / Mathf.Max(1f, length));
                float half = br.width * 0.5f * Mathf.Lerp(1f, 0.35f, t * t);
                if (d > half * 1.6f) return;
                float u = d / half;
                float s = S(0.25f, 1.0f, u);
                float stepped = Mathf.Round(s * 5f) / 5f;
                s = Mathf.Lerp(s, stepped + (s - stepped) * 0.3f, 0.4f);
                float hv = h[i];
                float floorH = Mathf.Lerp(br.floorElevation, hv - br.tipDepth, Mathf.Pow(t, 1.6f)) + (float)AshNoise.Fbm(wx, wz, 900, 3, seed + 3) * 10f;
                float target = Mathf.Lerp(floorH, hv, s);
                if (target < hv) h[i] = target;
                float inn = (1f - s) * (1f - t * 0.5f);
                inner[i] = Mathf.Max(inner[i], inn);
                float wallness = s * (1f - s) * 4f;
                melt[i] = Mathf.Max(melt[i], wallness * 0.7f);
                cliff[i] = Mathf.Max(cliff[i], wallness);
                frac[i] = Mathf.Max(frac[i], (1f - Mathf.Clamp01(u - 1f)) * 0.6f);
                veil[i] = Mathf.Max(veil[i], 0.5f * (1f - t) * (1f - Mathf.Clamp01(u - 1f)));
            });
        }
    }
}
