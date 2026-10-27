using System.Collections.Generic;
using System.IO;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.EditorTools.TerrainGen
{
    public enum PreviewMode
    {
        Relief,
        Height,
        Slope,
        Hydrology,
        Biomes,
        Factions,
        Progression,
        Geology,
        VeilScar,
        Settlement
    }

    [System.Flags]
    public enum PreviewOverlay
    {
        None = 0,
        Routes = 1,
        Rivers = 2,
        Pois = 4,
        Regions = 8,
        Protected = 16,
        Candidates = 32,
        Scale = 64
    }

    /// <summary>
    /// 2D-Vorschau der Makrowelt (Vorgabe §50–54, §73): Relief/Hoehe/Neigung/Wasser/Biome/Fraktionen/Stufen/Geologie/
    /// Kerbe/Siedlungseignung, dazu Wege, Fluesse, Orte, Regionsgrenzen, Schutzbereiche, Massstab. Farben nur Debug.
    /// </summary>
    public static class MacroPreviewRenderer
    {
        public static readonly Color[] BandColors =
        {
            new Color(0.30f, 0.75f, 0.35f), new Color(0.70f, 0.85f, 0.25f), new Color(0.95f, 0.80f, 0.20f),
            new Color(0.95f, 0.50f, 0.15f), new Color(0.85f, 0.20f, 0.20f), new Color(0.60f, 0.15f, 0.55f)
        };

        public static Color FactionColor(FactionAffinity f)
        {
            switch (f)
            {
                case FactionAffinity.HumanAlliance: return new Color(0.25f, 0.45f, 0.95f);
                case FactionAffinity.NharimAlliance: return new Color(0.85f, 0.25f, 0.30f);
                case FactionAffinity.Neutral: return new Color(0.75f, 0.75f, 0.70f);
                case FactionAffinity.Contested: return new Color(0.95f, 0.60f, 0.10f);
                default: return new Color(0.45f, 0.45f, 0.50f);
            }
        }

        public static Color BiomeColor(MacroBiome b)
        {
            switch (b)
            {
                case MacroBiome.Ocean: return new Color(0.10f, 0.22f, 0.42f);
                case MacroBiome.TemperatePlains: return new Color(0.55f, 0.70f, 0.35f);
                case MacroBiome.MixedForest: return new Color(0.22f, 0.48f, 0.22f);
                case MacroBiome.PineHighlands: return new Color(0.18f, 0.36f, 0.28f);
                case MacroBiome.Wetland: return new Color(0.30f, 0.52f, 0.48f);
                case MacroBiome.Moor: return new Color(0.48f, 0.42f, 0.30f);
                case MacroBiome.GranitePlateau: return new Color(0.62f, 0.60f, 0.56f);
                case MacroBiome.CoastalCliff: return new Color(0.72f, 0.68f, 0.55f);
                case MacroBiome.Ashland: return new Color(0.40f, 0.37f, 0.36f);
                case MacroBiome.Alpine: return new Color(0.92f, 0.93f, 0.96f);
                case MacroBiome.DeepValley: return new Color(0.28f, 0.40f, 0.25f);
                case MacroBiome.AncientForest: return new Color(0.10f, 0.32f, 0.16f);
                case MacroBiome.VeilScar: return new Color(0.35f, 0.25f, 0.55f);
                case MacroBiome.CanyonRim: return new Color(0.58f, 0.46f, 0.40f);
                default: return Color.magenta;
            }
        }

        public static Texture2D Render(TerrainGenerationContext ctx, PreviewMode mode, PreviewOverlay overlays, Texture2D reuse = null)
        {
            MacroHeightField f = ctx.Field;
            int w = f.Width, h = f.Height;
            var px = new Color32[w * h];
            float sea = ctx.Profile.seaLevel;
            f.MinMax(out float mn, out float mx);
            byte[] biomes = ctx.Biomes;
            foreach (string m in new[] { MaskSet.Mountain, MaskSet.Valley, MaskSet.Plateau, MaskSet.ScarInner, MaskSet.AncientMaterial, MaskSet.River,
                                         MaskSet.VeilInfluence, MaskSet.VeilGlass, MaskSet.MeltRock, MaskSet.Fracture, MaskSet.Settlement })
                ctx.Masks.Get(m);   // vorher anlegen: im parallelen Teil wird nur gelesen
            var regionIndex = RegionMap(ctx.Profile, f);

            System.Threading.Tasks.Parallel.For(0, h, z =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = z * w + x;
                    float hv = f.H[i];
                    float shade = Hillshade(f, x, z);
                    Color c;
                    switch (mode)
                    {
                        case PreviewMode.Height:
                            c = hv < sea ? Color.Lerp(new Color(0.05f, 0.1f, 0.3f), new Color(0.2f, 0.4f, 0.7f), Mathf.InverseLerp(mn, sea, hv))
                                         : Heat(Mathf.InverseLerp(sea, mx, hv));
                            c *= 0.75f + 0.25f * shade;
                            break;
                        case PreviewMode.Slope:
                        {
                            float s = f.SlopeDeg(x, z);
                            c = s < 12f ? new Color(0.35f, 0.75f, 0.35f) : s < 30f ? new Color(0.9f, 0.8f, 0.3f) : s < 45f ? new Color(0.9f, 0.45f, 0.2f) : new Color(0.7f, 0.1f, 0.1f);
                            if (hv < sea) c = new Color(0.15f, 0.2f, 0.35f);
                            c *= 0.7f + 0.3f * shade;
                            break;
                        }
                        case PreviewMode.Hydrology:
                        {
                            c = Relief(hv, sea, mx, shade) * 0.55f;
                            if (ctx.Hydrology != null)
                            {
                                float a = Mathf.Log10(1f + ctx.Hydrology.Accumulation[i]) / 4.5f;
                                c = Color.Lerp(c, new Color(0.2f, 0.55f, 1f), Mathf.Clamp01((a - 0.35f) * 2.2f));
                                if (ctx.Hydrology.LakeDepth[i] > 0f) c = new Color(0.25f, 0.5f, 0.9f);
                            }
                            break;
                        }
                        case PreviewMode.Biomes:
                            c = BiomeColor(biomes != null ? (MacroBiome)biomes[i] : MacroBiome.TemperatePlains) * (0.7f + 0.3f * shade);
                            break;
                        case PreviewMode.Factions:
                        case PreviewMode.Progression:
                        {
                            c = Relief(hv, sea, mx, shade) * 0.5f;
                            int ri = regionIndex[i];
                            if (ri >= 0)
                            {
                                MacroRegionDefinition r = ctx.Profile.regions[ri];
                                Color rc = mode == PreviewMode.Factions ? FactionColor(r.faction) : BandColors[WorldProgressionGraph.Band(r)];
                                if (r.isSlot) rc = new Color(0.5f, 0.5f, 0.55f);
                                c = Color.Lerp(c, rc * (0.65f + 0.35f * shade), 0.65f);
                            }
                            break;
                        }
                        case PreviewMode.Geology:
                        {
                            c = Relief(hv, sea, mx, shade) * 0.6f;
                            c = Color.Lerp(c, new Color(0.55f, 0.35f, 0.2f), ctx.Masks.Get(MaskSet.Mountain)[i] * 0.7f);
                            c = Color.Lerp(c, new Color(0.25f, 0.7f, 0.3f), ctx.Masks.Get(MaskSet.Valley)[i] * 0.6f);
                            c = Color.Lerp(c, new Color(0.85f, 0.75f, 0.35f), ctx.Masks.Get(MaskSet.Plateau)[i] * 0.7f);
                            c = Color.Lerp(c, new Color(0.55f, 0.2f, 0.85f), ctx.Masks.Get(MaskSet.ScarInner)[i] * 0.8f);
                            c = Color.Lerp(c, new Color(1f, 0.3f, 0.6f), ctx.Masks.Get(MaskSet.AncientMaterial)[i] * 0.4f);
                            c = Color.Lerp(c, new Color(0.3f, 0.6f, 1f), ctx.Masks.Get(MaskSet.River)[i]);
                            break;
                        }
                        case PreviewMode.VeilScar:
                        {
                            c = Relief(hv, sea, mx, shade) * 0.5f;
                            c = Color.Lerp(c, new Color(0.35f, 0.7f, 0.85f), ctx.Masks.Get(MaskSet.VeilInfluence)[i] * 0.6f);
                            c = Color.Lerp(c, new Color(0.1f, 0.1f, 0.12f), ctx.Masks.Get(MaskSet.VeilGlass)[i]);
                            c = Color.Lerp(c, new Color(0.9f, 0.4f, 0.2f), ctx.Masks.Get(MaskSet.MeltRock)[i] * 0.6f);
                            c = Color.Lerp(c, new Color(1f, 1f, 1f), ctx.Masks.Get(MaskSet.Fracture)[i] * 0.3f);
                            break;
                        }
                        case PreviewMode.Settlement:
                        {
                            c = Relief(hv, sea, mx, shade) * 0.45f;
                            c = Color.Lerp(c, new Color(1f, 0.85f, 0.2f), ctx.Masks.Get(MaskSet.Settlement)[i]);
                            break;
                        }
                        default:
                            c = Relief(hv, sea, mx, shade);
                            break;
                    }
                    c.a = 1f;
                    px[i] = c;
                }
            });

            if ((overlays & PreviewOverlay.Regions) != 0) DrawRegionEdges(px, regionIndex, w, h);
            if ((overlays & PreviewOverlay.Protected) != 0)
                foreach (ProtectedAreaDefinition a in ctx.Profile.protectedAreas) DrawRect(px, f, a.rect, new Color32(255, 255, 255, 255));
            if ((overlays & PreviewOverlay.Candidates) != 0 && ctx.Hydrology != null)
                foreach (RiverCandidate rc in ctx.Hydrology.Candidates) DrawLine(px, f, rc.Path, new Color32(120, 200, 255, 255), 0);
            if ((overlays & PreviewOverlay.Rivers) != 0)
                foreach (RiverDefinition r in ctx.Profile.rivers) DrawLine(px, f, r.path, new Color32(40, 110, 255, 255), 1);
            if ((overlays & PreviewOverlay.Routes) != 0)
                foreach (RoadCorridorDefinition r in ctx.Profile.roads)
                {
                    Color32 col = r.routeClass == RouteClass.Main ? new Color32(255, 240, 200, 255) : r.routeClass == RouteClass.Secondary ? new Color32(230, 200, 150, 255) : new Color32(200, 170, 140, 255);
                    DrawLine(px, f, r.path, col, r.routeClass == RouteClass.Main ? 1 : 0);
                }
            if ((overlays & PreviewOverlay.Pois) != 0)
                foreach (MacroPoiDefinition p in ctx.Profile.pois)
                {
                    Color32 col = p.hubTier == HubTier.CapitalSlot ? new Color32(255, 215, 0, 255) : p.viewpoint ? new Color32(255, 255, 255, 255) : new Color32(20, 20, 20, 255);
                    DrawDot(px, f, p.position, 2 + p.importance, col);
                }
            if ((overlays & PreviewOverlay.Scale) != 0) DrawScale(px, f);

            Texture2D tex = reuse != null && reuse.width == w && reuse.height == h ? reuse : new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        public static void SavePng(Texture2D tex, string name)
        {
            ProjectSetup.EnsureFolder(TerrainGenPaths.PreviewFolder);
            File.WriteAllBytes($"{TerrainGenPaths.PreviewFolder}/{name}.png", tex.EncodeToPNG());
        }

        // ------------------------------------------------------------------ Helfer

        public static int[] RegionMap(MacroWorldProfile p, MacroHeightField f)
        {
            var map = new int[f.Count];
            System.Threading.Tasks.Parallel.For(0, f.Height, z =>
            {
                for (int x = 0; x < f.Width; x++)
                {
                    int i = z * f.Width + x;
                    Vector2 w = f.World(x, z);
                    map[i] = -1;
                    // Handgebaute Regionen (Rechtecke) vorrangig, danach die erste passende
                    for (int k = 0; k < p.regions.Count; k++)
                        if (p.regions[k].polygon.Count >= 3 && ContinentModifier.InPolygon(p.regions[k].polygon, w))
                        {
                            if (map[i] < 0 || p.regions[k].isProtectedFromGeneration) map[i] = k;
                        }
                }
            });
            return map;
        }

        private static float Hillshade(MacroHeightField f, int x, int z)
        {
            float dx = (f.Get(x + 1, z) - f.Get(x - 1, z)) / (2f * f.CellSize);
            float dz = (f.Get(x, z + 1) - f.Get(x, z - 1)) / (2f * f.CellSize);
            Vector3 n = new Vector3(-dx * 2.5f, 1f, -dz * 2.5f).normalized;
            return Mathf.Clamp01(Vector3.Dot(n, new Vector3(-0.55f, 0.7f, 0.45f).normalized));
        }

        private static Color Relief(float hv, float sea, float mx, float shade)
        {
            if (hv < sea) return Color.Lerp(new Color(0.08f, 0.16f, 0.3f), new Color(0.2f, 0.35f, 0.5f), Mathf.Clamp01(1f + (hv - sea) / 300f));
            float t = Mathf.InverseLerp(sea, Mathf.Max(sea + 1f, mx), hv);
            Color c = t < 0.12f ? Color.Lerp(new Color(0.55f, 0.62f, 0.45f), new Color(0.5f, 0.55f, 0.4f), t / 0.12f)
                    : t < 0.5f ? Color.Lerp(new Color(0.5f, 0.55f, 0.4f), new Color(0.55f, 0.5f, 0.44f), (t - 0.12f) / 0.38f)
                    : Color.Lerp(new Color(0.55f, 0.5f, 0.44f), new Color(0.93f, 0.93f, 0.95f), (t - 0.5f) / 0.5f);
            return c * (0.45f + 0.75f * shade);
        }

        private static Color Heat(float t)
        {
            if (t < 0.25f) return Color.Lerp(new Color(0.1f, 0.5f, 0.2f), new Color(0.6f, 0.8f, 0.2f), t / 0.25f);
            if (t < 0.5f) return Color.Lerp(new Color(0.6f, 0.8f, 0.2f), new Color(0.9f, 0.7f, 0.2f), (t - 0.25f) / 0.25f);
            if (t < 0.75f) return Color.Lerp(new Color(0.9f, 0.7f, 0.2f), new Color(0.7f, 0.35f, 0.2f), (t - 0.5f) / 0.25f);
            return Color.Lerp(new Color(0.7f, 0.35f, 0.2f), Color.white, (t - 0.75f) / 0.25f);
        }

        private static bool ToPx(MacroHeightField f, Vector2 w, out int x, out int z)
        {
            x = Mathf.RoundToInt((w.x - f.Origin.x) / f.CellSize);
            z = Mathf.RoundToInt((w.y - f.Origin.y) / f.CellSize);
            return x >= 0 && z >= 0 && x < f.Width && z < f.Height;
        }

        private static void Plot(Color32[] px, int w, int h, int x, int z, Color32 c)
        {
            if (x < 0 || z < 0 || x >= w || z >= h) return;
            px[z * w + x] = c;
        }

        private static void DrawLine(Color32[] px, MacroHeightField f, IReadOnlyList<Vector2> pts, Color32 c, int thick)
        {
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                float len = Vector2.Distance(pts[k], pts[k + 1]);
                int n = Mathf.Max(2, Mathf.CeilToInt(len / (f.CellSize * 0.5f)));
                for (int s = 0; s <= n; s++)
                {
                    Vector2 p = Vector2.Lerp(pts[k], pts[k + 1], (float)s / n);
                    ToPx(f, p, out int x, out int z);
                    for (int a = -thick; a <= thick; a++)
                        for (int b = -thick; b <= thick; b++) Plot(px, f.Width, f.Height, x + a, z + b, c);
                }
            }
        }

        private static void DrawDot(Color32[] px, MacroHeightField f, Vector2 p, int r, Color32 c)
        {
            ToPx(f, p, out int x, out int z);
            for (int a = -r; a <= r; a++)
                for (int b = -r; b <= r; b++)
                    if (a * a + b * b <= r * r) Plot(px, f.Width, f.Height, x + a, z + b, (a * a + b * b >= (r - 1) * (r - 1)) ? new Color32(0, 0, 0, 255) : c);
        }

        private static void DrawRect(Color32[] px, MacroHeightField f, Rect r, Color32 c)
        {
            var pts = new List<Vector2> { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin) };
            DrawLine(px, f, pts, c, 0);
        }

        private static void DrawRegionEdges(Color32[] px, int[] map, int w, int h)
        {
            var edge = new Color32(15, 15, 15, 255);
            for (int z = 1; z < h - 1; z++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = z * w + x;
                    if (map[i] != map[i + 1] || map[i] != map[i + w]) px[i] = edge;
                }
        }

        /// <summary>Massstab 1 km und 5 km unten links (§91).</summary>
        private static void DrawScale(Color32[] px, MacroHeightField f)
        {
            int y0 = 12, x0 = 12;
            int one = Mathf.RoundToInt(1000f / f.CellSize), five = Mathf.RoundToInt(5000f / f.CellSize);
            for (int x = 0; x < five; x++)
                for (int t = 0; t < 4; t++)
                {
                    bool black = (x / one) % 2 == 0;
                    Plot(px, f.Width, f.Height, x0 + x, y0 + t, black ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255));
                }
        }
    }
}
