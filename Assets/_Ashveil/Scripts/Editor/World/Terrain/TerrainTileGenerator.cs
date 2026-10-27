using System.Collections.Generic;
using System.Diagnostics;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ashveil.EditorTools.TerrainGen
{
    /// <summary>Eine erzeugte Kachel im 1200-m-Raster von Kareth (Vorgabe §69: RegionId, CellId, TileCoord).</summary>
    public sealed class GeneratedTile
    {
        public Vector2Int Coord;       // Kareth = (0, 0)
        public Rect Rect;
        /// <summary>513 an Kanten zu Handarbeit (gleiche Stuetzstellen), sonst 257 (Greybox, Vorgabe §9).</summary>
        public int Resolution = 257;
        public string RegionId = "";
        public string CellId => $"gen_{Coord.x}_{Coord.y}";
        public string Name => $"GEN_Terrain_{Coord.x}_{Coord.y}";
        public string DataPath => $"{TerrainGenPaths.TerrainFolder}/{Name}_TerrainData.asset";
        public float SeamError;
    }

    /// <summary>
    /// Hoch aufgeloeste Kacheln aus der Makrowelt (Vorgabe §8–10, §13–14): bikubisch aus dem Makrofeld, feine Grate
    /// im Gebirge, leichtes Detail – und an jeder Kante zu Handarbeit exakt deren Randhoehe (TerrainSeamBridge).
    /// Nur Kacheln, die kein geschuetzter Bereich belegt; bestehende GEN-Kacheln werden ersetzt, sonst nichts.
    /// </summary>
    public static class TerrainTileGenerator
    {
        public const float TileSize = 1200f;
        public const int ResolutionNearHandwork = 513;
        public const int ResolutionGreybox = 257;
        public const float BaseY = -400f;
        public const float HeightRange = 1800f;
        /// <summary>
        /// Begehbares Umland (Vorgabe §110 Schritte 2–3): Kareth-Gebiet (Silberwald, Kronenwald, Salzsee, Eschenfeld), der
        /// Weg zum Grenzgebirge-Kern (Talpass) und dessen Auslaeufer bis zum Rand der Bruchmark. Aussen: Fernansicht.
        /// </summary>
        public static readonly Rect RingArea = new Rect(-3000f, -5150f, 9600f, 8400f);

        public static Vector2 TileOrigin(Vector2Int c) => new Vector2(-600f + c.x * TileSize, -350f + c.y * TileSize);

        public static List<GeneratedTile> PlanRing(MacroWorldProfile profile, TerrainGenerationContext ctx)
        {
            var list = new List<GeneratedTile>();
            int[] regionMap = MacroPreviewRenderer.RegionMap(profile, ctx.Field);
            for (float z = RingArea.yMin; z < RingArea.yMax - 1f; z += TileSize)
                for (float x = RingArea.xMin; x < RingArea.xMax - 1f; x += TileSize)
                {
                    var rect = new Rect(x, z, TileSize, TileSize);
                    bool blocked = false;
                    foreach (ProtectedAreaDefinition a in profile.protectedAreas)
                        if (a.rect.Overlaps(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f))) blocked = true;
                    if (blocked) continue;   // PROTECTED REGION SKIPPED (§75)
                    var coord = new Vector2Int(Mathf.RoundToInt((x + 600f) / TileSize), Mathf.RoundToInt((z + 350f) / TileSize));
                    var t = new GeneratedTile { Coord = coord, Rect = rect };
                    foreach (ProtectedAreaDefinition a in profile.protectedAreas)
                    {
                        Rect grown = new Rect(a.rect.x - 1f, a.rect.y - 1f, a.rect.width + 2f, a.rect.height + 2f);
                        if (grown.Overlaps(rect)) t.Resolution = ResolutionNearHandwork;
                    }
                    MacroHeightField f = ctx.Field;
                    int ix = Mathf.Clamp(Mathf.RoundToInt((rect.center.x - f.Origin.x) / f.CellSize), 0, f.Width - 1);
                    int iz = Mathf.Clamp(Mathf.RoundToInt((rect.center.y - f.Origin.y) / f.CellSize), 0, f.Height - 1);
                    int ri = regionMap[iz * f.Width + ix];
                    t.RegionId = ri >= 0 ? profile.regions[ri].regionId : "Unassigned";
                    list.Add(t);
                }
            return list;
        }

        /// <summary>Hoehe an einem Weltpunkt in Kachelaufloesung (gleiche Funktion fuer alle Kacheln = nahtlos).</summary>
        public static float SampleFine(TerrainGenerationContext ctx, float x, float z)
        {
            MacroHeightField f = ctx.Field;
            float macro = f.SampleBicubic(x, z);
            float mountain = f.SampleBilinear(ctx.Masks.Get(MaskSet.Mountain), x, z);
            float road = f.SampleBilinear(ctx.Masks.Get(MaskSet.Road), x, z);
            float river = f.SampleBilinear(ctx.Masks.Get(MaskSet.River), x, z);
            float scar = f.SampleBilinear(ctx.Masks.Get(MaskSet.ScarInner), x, z);
            float keep = 1f - Mathf.Clamp01(Mathf.Max(road, river) * 1.5f);
            double ridge = AshNoise.Ridged(x, z, 420, 4, ctx.DetailSeed + 7) - 0.35;
            double fine = AshNoise.Fbm(x, z, 140, 3, ctx.DetailSeed + 11);
            float detail = (float)(ridge * 70.0 * mountain * mountain + fine * (2.5 + 6.0 * mountain) + ridge * 14.0 * scar) * keep;
            float h = macro + detail;
            // Naht zu Handarbeit: exakt deren Randhoehe, weich in die Makrohoehe (Detail blendet mit aus)
            h = ProtectedAreaModifier.BlendAt(ctx.Protected, ctx.Profile.transitionDistance, x, z, h, out float w);
            if (w > 0f && ctx.Protected.NearestEdge(x, z, out float edge, out _) < 0.01f) h = edge;
            return TunnelExit(x, z, h);
        }

        /// <summary>
        /// Handarbeit im erzeugten Gelaende: Ausgang des Osttunnels (Kareth-Ostwand, z = 60, Boden 6 m bei x = 658).
        /// Kerbe fuer die Roehre bis x = 660, danach ein Einschnitt, der sich ins Eschenfeld oeffnet.
        /// </summary>
        /// <summary>Graben vom Ostportal ins Eschenfeld (Sohle, Weltpunkte) – in voller Aufloesung, nach der Kareth-Naht.</summary>
        private static readonly Vector3[] Graben =
        {
            new Vector3(658f, 6f, 60f), new Vector3(900f, 7f, 40f), new Vector3(1300f, 10f, -40f), new Vector3(1800f, 12f, -160f),
            new Vector3(2300f, 15f, -320f), new Vector3(2800f, 18f, -480f)
        };
        private static Vector2[] _graben2;

        public static float TunnelExit(float x, float z, float h)
        {
            const float tz = BorderMountainsLayout.TunnelZ, eastX = BorderMountainsLayout.TunnelEastX;
            if (x < 598f || x > 3100f || z > 400f || z < -800f) return h;
            if (x > 900f || Mathf.Abs(z - tz) > 120f) return Graben2(x, z, h);
            float floor = x <= eastX ? BorderTunnel.FloorAt(x) - 1.5f : BorderTunnel.EastFloor + (x - eastX) * 0.02f;
            float dz = Mathf.Abs(z - tz);
            if (x <= eastX)
            {
                // Die Roehre laeuft bis zum Ostportal im Fels: ueber ihr bleibt das Gelaende mindestens 2 m ueber der Decke,
                // sonst schnitte die Gelaendeflaeche durch den Tunnel (Wand fuer Spieler und NavMesh). An der Naht
                // (x = 600) liegt Kareths Wand ohnehin hoeher – dort aendert sich nichts.
                float ceiling = BorderTunnel.FloorAt(x) + BorderMountainsLayout.TunnelHeight + 2f;
                float cover = (float)Ashveil.World.AshNoise.SmoothStep(BorderTunnel.NotchHalfWidth + 14f, BorderTunnel.NotchHalfWidth + 3f, dz);
                return Mathf.Max(h, Mathf.Lerp(h, ceiling, cover));
            }
            // Einschnitt: Sohle 16 m breit, Waende steil, weitet sich nach Osten
            float t = Mathf.InverseLerp(eastX, 900f, x);
            float half = Mathf.Lerp(9f, 40f, t), wallW = Mathf.Lerp(10f, 80f, t);
            float wgt = (float)Ashveil.World.AshNoise.SmoothStep(half + wallW, half, dz);
            return Mathf.Min(Mathf.Lerp(h, Mathf.Min(h, floor), wgt), Graben2(x, z, h));
        }

        /// <summary>Weiter Graben: Sohle nach dem Profil, Waende 35°, oeffnet sich nach Osten; senkt nur ab.</summary>
        private static float Graben2(float x, float z, float h)
        {
            if (_graben2 == null)
            {
                _graben2 = new Vector2[Graben.Length];
                for (int i = 0; i < Graben.Length; i++) _graben2[i] = new Vector2(Graben[i].x, Graben[i].z);
            }
            float d = Ashveil.World.WorldPolyline.Distance(_graben2, new Vector2(x, z), out int seg, out float t, out float along, out _);
            float floor = Mathf.Lerp(Graben[seg].y, Graben[seg + 1].y, t);
            float half = Mathf.Lerp(8f, 70f, Mathf.Clamp01(along / 2000f));
            float target = floor + Mathf.Max(0f, d - half) * 0.7f;   // Waende ~35°
            // an der Kareth-Naht (x = 600) unveraendert, erst ab x = 640 voll
            return Mathf.Lerp(h, Mathf.Min(h, target), (float)Ashveil.World.AshNoise.SmoothStep(640, 700, x));
        }

        public static List<GeneratedTile> Generate(TerrainGenerationContext ctx, List<GeneratedTile> tiles)
        {
            ProjectSetup.EnsureFolder(TerrainGenPaths.TerrainFolder);
            var sw = Stopwatch.StartNew();
            TerrainLayer[] layers = TerrainLayers.Load();
            tilesByCoord = new Dictionary<Vector2Int, GeneratedTile>();
            foreach (GeneratedTile gt in tiles) tilesByCoord[gt.Coord] = gt;
            foreach (string m in new[] { MaskSet.Mountain, MaskSet.Road, MaskSet.River, MaskSet.ScarInner, MaskSet.TreeDensity, MaskSet.Moisture, MaskSet.VeilGlass, MaskSet.MeltRock })
                ctx.Masks.Get(m);   // vorher anlegen: parallel wird nur gelesen
            try
            {
                for (int k = 0; k < tiles.Count; k++)
                {
                    GeneratedTile t = tiles[k];
                    if (EditorUtility.DisplayCancelableProgressBar("ASHVEIL Terrain-Generator", $"Kachel {t.Name} ({t.RegionId})", (float)k / tiles.Count))
                    {
                        ctx.Log.Add("Kachel-Erzeugung abgebrochen");
                        break;
                    }
                    BuildTile(ctx, t, layers);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            AssetDatabase.SaveAssets();
            ctx.Log.Add($"{tiles.Count} Kacheln (513 an Handarbeit, sonst 257) in {sw.ElapsedMilliseconds} ms");
            return tiles;
        }

        private static void BuildTile(TerrainGenerationContext ctx, GeneratedTile t, TerrainLayer[] layers)
        {
            int res = t.Resolution;
            var heights = new float[res, res];
            float step = TileSize / (res - 1);
            float x0 = t.Rect.xMin, z0 = t.Rect.yMin;
            var world = new float[res * res];
            System.Threading.Tasks.Parallel.For(0, res, iz =>
            {
                for (int ix = 0; ix < res; ix++)
                {
                    float wx = x0 + ix * step, wz = z0 + iz * step;
                    float h = SampleFine(ctx, wx, wz);
                    world[iz * res + ix] = h;
                    heights[iz, ix] = Mathf.Clamp01((h - BaseY) / HeightRange);
                }
            });

            // Kanten zu groeberen Nachbarn (257) linear angleichen, sonst Risse zwischen GEN-Kacheln
            if (res == ResolutionNearHandwork) MatchCoarseEdges(t, tilesByCoord, world, heights, res);

            // Naht pruefen (§99): Rand gegen geschuetztes Gelaende
            float seam = 0f;
            for (int k = 0; k < res; k++)
            {
                seam = Mathf.Max(seam, EdgeError(ctx, x0 + k * step, z0, world[k]));
                seam = Mathf.Max(seam, EdgeError(ctx, x0 + k * step, z0 + TileSize, world[(res - 1) * res + k]));
                seam = Mathf.Max(seam, EdgeError(ctx, x0, z0 + k * step, world[k * res]));
                seam = Mathf.Max(seam, EdgeError(ctx, x0 + TileSize, z0 + k * step, world[k * res + res - 1]));
            }
            t.SeamError = seam;

            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(t.DataPath);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, t.DataPath);
            }
            data.heightmapResolution = res;
            data.alphamapResolution = 256;
            data.baseMapResolution = 256;
            data.size = new Vector3(TileSize, HeightRange, TileSize);
            data.SetHeights(0, 0, heights);
            data.terrainLayers = layers;
            PaintSplat(ctx, t, data, world, res, step);
            EditorUtility.SetDirty(data);
        }

        private static Dictionary<Vector2Int, GeneratedTile> tilesByCoord = new Dictionary<Vector2Int, GeneratedTile>();

        private static void MatchCoarseEdges(GeneratedTile t, Dictionary<Vector2Int, GeneratedTile> all, float[] world, float[,] heights, int res)
        {
            void Fix(int ix0, int iz0, int dx, int dz)
            {
                // jede ungerade Stuetzstelle = Mittel der Nachbarn (die 257er-Kachel kennt nur die geraden)
                for (int k = 1; k < res - 1; k += 2)
                {
                    int ia = (iz0 + dz * (k - 1)) * res + ix0 + dx * (k - 1), ib = (iz0 + dz * (k + 1)) * res + ix0 + dx * (k + 1);
                    int i = (iz0 + dz * k) * res + ix0 + dx * k;
                    world[i] = (world[ia] + world[ib]) * 0.5f;
                    heights[iz0 + dz * k, ix0 + dx * k] = Mathf.Clamp01((world[i] - BaseY) / HeightRange);
                }
            }
            bool Coarse(int cx, int cz) => all.TryGetValue(new Vector2Int(t.Coord.x + cx, t.Coord.y + cz), out GeneratedTile n) && n.Resolution == ResolutionGreybox;
            if (Coarse(0, -1)) Fix(0, 0, 1, 0);              // Suedkante
            if (Coarse(0, 1)) Fix(0, res - 1, 1, 0);         // Nordkante
            if (Coarse(-1, 0)) Fix(0, 0, 0, 1);              // Westkante
            if (Coarse(1, 0)) Fix(res - 1, 0, 0, 1);         // Ostkante
        }

        private static float EdgeError(TerrainGenerationContext ctx, float x, float z, float h)
        {
            if (!ctx.Protected.TrySampleExact(x, z, out float ph)) return 0f;
            return Mathf.Abs(ph - h);
        }

        private static void PaintSplat(TerrainGenerationContext ctx, GeneratedTile t, TerrainData data, float[] world, int hres, float hstep)
        {
            int ares = data.alphamapResolution;
            int layerCount = data.terrainLayers.Length;
            var alpha = new float[ares, ares, layerCount];
            MacroHeightField f = ctx.Field;
            float[] trees = ctx.Masks.Get(MaskSet.TreeDensity), road = ctx.Masks.Get(MaskSet.Road), wet = ctx.Masks.Get(MaskSet.Moisture);
            float[] glass = ctx.Masks.Get(MaskSet.VeilGlass), melt = ctx.Masks.Get(MaskSet.MeltRock);
            float sea = ctx.Profile.seaLevel;
            System.Threading.Tasks.Parallel.For(0, ares, az =>
            {
                for (int ax = 0; ax < ares; ax++)
                {
                    float u = (float)ax / (ares - 1), v = (float)az / (ares - 1);
                    int hx = Mathf.Clamp(Mathf.RoundToInt(u * (hres - 1)), 1, hres - 2), hz = Mathf.Clamp(Mathf.RoundToInt(v * (hres - 1)), 1, hres - 2);
                    float h = world[hz * hres + hx];
                    float dx = world[hz * hres + hx + 1] - world[hz * hres + hx - 1];
                    float dz = world[(hz + 1) * hres + hx] - world[(hz - 1) * hres + hx];
                    float slope = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) / (2f * hstep)) * Mathf.Rad2Deg;
                    float wx = t.Rect.xMin + u * TileSize, wz = t.Rect.yMin + v * TileSize;
                    float tr = f.SampleBilinear(trees, wx, wz), rd = f.SampleBilinear(road, wx, wz), mo = f.SampleBilinear(wet, wx, wz);
                    float gl = f.SampleBilinear(glass, wx, wz), me = f.SampleBilinear(melt, wx, wz);
                    var w = new float[8];
                    w[TerrainLayers.Grass] = 1f;
                    w[TerrainLayers.Forest] = tr * 1.4f;
                    w[TerrainLayers.Moor] = Mathf.Clamp01((mo - 0.75f) * 3f) * (1f - Mathf.Clamp01(slope / 15f));
                    w[TerrainLayers.Ash] = gl * 2f + me * 0.6f;
                    w[TerrainLayers.Rock] = Mathf.Clamp01((slope - 26f) / 10f) * 3f + Mathf.Clamp01((h - 700f) / 250f);
                    w[TerrainLayers.Path] = Mathf.Clamp01((rd - 0.55f) * 3f) * 2f;
                    w[TerrainLayers.Sand] = Mathf.Clamp01((sea + 5f - h) / 4f) * 3f;
                    w[TerrainLayers.Snow] = Mathf.Clamp01((h - 1300f) / 250f) * 3f * (1f - Mathf.Clamp01((slope - 40f) / 10f));
                    float sum = 0f;
                    for (int l = 0; l < layerCount && l < 8; l++) sum += w[l];
                    for (int l = 0; l < layerCount && l < 8; l++) alpha[az, ax, l] = w[l] / Mathf.Max(1e-4f, sum);
                }
            });
            data.SetAlphamaps(0, 0, alpha);
        }
    }

    /// <summary>Gelaendeschichten wie Kareth/Grenzgebirge (gleiche Reihenfolge, Schnee als 8.).</summary>
    public static class TerrainLayers
    {
        public const int Grass = 0, Forest = 1, Moor = 2, Ash = 3, Rock = 4, Path = 5, Sand = 6, Snow = 7;
        private static readonly string[] Names = { "Grass", "ForestFloor", "Moor", "Ash", "Rock", "Path", "Sand", "Snow" };

        public static TerrainLayer[] Load()
        {
            var list = new List<TerrainLayer>();
            foreach (string n in Names)
            {
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{ProjectSetup.Root}/Terrain/Layer_{n}.terrainlayer");
                if (l != null) list.Add(l);
                else Debug.LogWarning($"[Ashveil] Terrain-Generator: Schicht {n} fehlt (erst Sandbox bzw. Grenzgebirge bauen).");
            }
            return list.ToArray();
        }
    }
}
