using Ashveil.World;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>
    /// BIOME MASKS (Vorgabe §57–63): Temperatur (Breitengrad-Gefaelle nach Sueden waermer, Hoehe, Waermeanomalie an
    /// der Kerbe), Feuchte (Westwind vom Meer mit Regenschatten hinter Gebirgen, Wassernaehe, Abfluss), Neigung,
    /// Geologie, Schleiereinfluss → Biom je Zelle. Weiche Uebergaenge ueber Dichte-Masken; keine Lore daraus.
    /// Dazu Siedlungseignung (§55, nur Debug).
    /// </summary>
    public sealed class BiomePass : TerrainModifier
    {
        public override string Name => "Biome, Vegetation, Siedlungseignung";
        public override int Priority => 1200;

        public override void Apply(TerrainGenerationContext ctx)
        {
            MacroHeightField f = ctx.Field;
            int w = f.Width, hh = f.Height, n = f.Count;
            float sea = ctx.Profile.seaLevel;
            float[] h = f.H;
            float[] land = ctx.Masks.Get(MaskSet.Land);
            float[] temp = ctx.Masks.Get(MaskSet.Temperature), moist = ctx.Masks.Get(MaskSet.Moisture);
            float[] veil = ctx.Masks.Get(MaskSet.VeilInfluence), inner = ctx.Masks.Get(MaskSet.ScarInner), rim = ctx.Masks.Get(MaskSet.ScarRim);
            float[] plateau = ctx.Masks.Get(MaskSet.Plateau), valley = ctx.Masks.Get(MaskSet.Valley), river = ctx.Masks.Get(MaskSet.River);
            float[] lake = ctx.Masks.Get(MaskSet.Lake), road = ctx.Masks.Get(MaskSet.Road), cliff = ctx.Masks.Get(MaskSet.Cliff);
            float[] trees = ctx.Masks.Get(MaskSet.TreeDensity), shrubs = ctx.Masks.Get(MaskSet.ShrubDensity), grass = ctx.Masks.Get(MaskSet.GrassDensity);
            float[] settle = ctx.Masks.Get(MaskSet.Settlement);
            int seed = ctx.BiomeSeed;

            // ---- Feuchte: Westwind, Zeile fuer Zeile von West nach Ost (Regenschatten, §59–60)
            System.Threading.Tasks.Parallel.For(0, hh, z =>
            {
                float carried = 1f;
                float prevH = h[z * w];
                for (int x = 0; x < w; x++)
                {
                    int i = z * w + x;
                    if (land[i] < 0.5f)
                    {
                        carried = Mathf.Min(1f, carried + 0.02f);   // ueber Wasser nimmt die Luft Feuchte auf
                        moist[i] = 1f;
                        prevH = sea;
                        continue;
                    }
                    float rise = Mathf.Max(0f, h[i] - prevH);
                    float rain = carried * (0.004f + rise * 0.004f);
                    carried = Mathf.Max(0.08f, carried - rain - 0.0008f);
                    moist[i] = Mathf.Clamp01(carried * 0.75f + rain * 25f);
                    prevH = Mathf.Lerp(prevH, h[i], 0.35f);
                }
            });

            byte[] biomes = new byte[n];
            float zMin = f.Origin.y, zSpan = (hh - 1) * f.CellSize;
            ctx.ForEachCell((i, p) =>
            {
                int x = i % w, z = i / w;
                float hv = h[i];
                float slope = f.SlopeDeg(x, z);
                float lat = (p.y - zMin) / zSpan;   // 0 = Sueden
                float t = 17f - lat * 7f - Mathf.Max(0f, hv) * 0.0065f + veil[i] * 4f;
                temp[i] = t;
                float wet = Mathf.Clamp01(moist[i] + river[i] * 0.4f + lake[i] * 0.5f + valley[i] * 0.15f);
                moist[i] = wet;
                float patch = (float)AshNoise.Fbm(p.x, p.y, 2400, 3, seed) * 0.5f + 0.5f;

                // Hinweise aus der Weltkarte (benannte Waelder): weicher Rand mit Rauschen
                MacroBiome hint = MacroBiome.Ocean;
                foreach (BiomeHint bh in ctx.Profile.biomeHints)
                {
                    float d = Vector2.Distance(p, bh.center) / Mathf.Max(1f, bh.radius);
                    float edge = (float)AshNoise.Fbm(p.x, p.y, 420, 3, seed + 7) * 0.35f + (patch - 0.5f) * 0.3f;
                    if (d + edge < 1f) hint = bh.biome;
                }

                MacroBiome b;
                if (land[i] < 0.5f && hv < sea + 0.5f) b = MacroBiome.Ocean;
                else if (inner[i] > 0.55f) b = MacroBiome.VeilScar;
                else if (rim[i] > 0.5f) b = MacroBiome.CanyonRim;
                else if (land[i] < 0.9f && slope > 18f) b = MacroBiome.CoastalCliff;
                else if (hv > 1050f || t < 1.5f) b = MacroBiome.Alpine;
                else if (valley[i] > 0.6f && slope > 16f) b = MacroBiome.DeepValley;
                else if (hint != MacroBiome.Ocean && slope < 30f && hv < 900f) b = hint;
                else if (lake[i] > 0.7f || (slope < 2f && wet > 0.92f && hv < 60f)) b = MacroBiome.Wetland;
                else if (plateau[i] > 0.6f && wet > 0.55f) b = MacroBiome.Moor;
                else if (plateau[i] > 0.6f || (hv > 500f && slope < 12f)) b = MacroBiome.GranitePlateau;
                else if (wet < 0.28f && patch > 0.62f && veil[i] > 0.05f) b = MacroBiome.Ashland;
                else if (hv > 380f && wet > 0.35f) b = MacroBiome.PineHighlands;
                else if (wet > 0.6f && patch > 0.72f && lat < 0.45f) b = MacroBiome.AncientForest;
                else if (wet > 0.42f && patch > 0.4f) b = MacroBiome.MixedForest;
                else b = MacroBiome.TemperatePlains;
                biomes[i] = (byte)b;

                // Dichte-Masken (weich, §61, §63)
                float slopeOk = 1f - S(28f, 42f, slope);
                float forest = b == MacroBiome.MixedForest || b == MacroBiome.AncientForest ? 0.85f : b == MacroBiome.PineHighlands ? 0.7f
                             : b == MacroBiome.TemperatePlains ? 0.12f : b == MacroBiome.Wetland ? 0.2f : b == MacroBiome.Moor ? 0.08f : 0f;
                trees[i] = forest * slopeOk * (1f - road[i]) * (1f - river[i]) * (0.7f + 0.3f * patch);
                shrubs[i] = Mathf.Clamp01((wet * 0.6f + 0.2f) * slopeOk * (b == MacroBiome.Ocean || b == MacroBiome.VeilScar || b == MacroBiome.Alpine ? 0f : 1f));
                grass[i] = Mathf.Clamp01((b == MacroBiome.TemperatePlains ? 0.9f : b == MacroBiome.Moor || b == MacroBiome.Wetland ? 0.7f : 0.45f) * slopeOk * land[i]);

                // Siedlungseignung (§55): maessige Neigung, Wasser nah, Weg, fruchtbar, geschuetzt – nicht Kerbe/Hochgebirge/Sumpf
                float s = (1f - S(4f, 14f, slope)) * 0.35f + Mathf.Clamp01(river[i] * 2f + lake[i]) * 0.2f + road[i] * 0.2f +
                          (b == MacroBiome.TemperatePlains || b == MacroBiome.MixedForest ? 0.15f : 0f) + valley[i] * 0.1f;
                if (b == MacroBiome.Ocean || b == MacroBiome.VeilScar || b == MacroBiome.Alpine || b == MacroBiome.Wetland || cliff[i] > 0.5f) s = 0f;
                if (veil[i] > 0.6f) s *= 0.3f;
                settle[i] = Mathf.Clamp01(s);
            });
            ctx.Biomes = biomes;
        }
    }
}
