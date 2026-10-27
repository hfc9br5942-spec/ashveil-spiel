using System.Collections.Generic;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Milestone 5.4 – Greybox der ganzen Senke von Kareth (1,2 x 1,2 km) als Unity-Terrain:
    ///
    ///   Norden   : Granitplateau (+32 m) mit Rampe bei x = 18, dahinter Berge
    ///   Westen   : Kiefernhang, huegelig, dichter Wald, Holzfaellerlager
    ///   Mitte    : Glockenmoor (flache Senke mit Wasser, versunkene Kuppeln, Aschenweide)
    ///   Osten    : Aschenfeld (flach, verbrannt) vor der Felswand mit dem Uhrwerk
    ///   Suedwest : Steilkueste, Meer, Salzgruben-Eingang
    ///   Sued-Mitte: Wachtfeuer – ein flacher Kern (r ~150 m) rund um das Lager, in dem alle
    ///              bisherigen Inhalte (Lager, Rudel, Lager der Verblichenen, Testparcours) unveraendert bleiben.
    ///
    /// Alles prozedural und deterministisch (gleiches Ergebnis bei jedem Neuaufbau). Die Hoehenfunktion
    /// <see cref="Height"/> nutzen auch die anderen Bauer, um Objekte auf den Boden zu stellen.
    /// </summary>
    public static class KarethTerrainBuilder
    {
        public const float MinX = -600f, MaxX = 600f, MinZ = -350f, MaxZ = 850f;
        public const float Size = 1200f;
        private const float BaseY = -40f;
        private const float HeightRange = 160f;
        private const int HeightRes = 513;
        private const int SplatRes = 512;
        private const string Folder = ProjectSetup.Root + "/Terrain";

        // Bereiche
        public static readonly Vector2 CoreCenter = new Vector2(-10f, 20f);
        public static readonly Vector2 MoorCenter = new Vector2(40f, 330f);
        public static readonly Vector3 LumberCamp = new Vector3(-250f, 0f, 240f);
        public const float PlateauHeight = 32f;
        public const float RampX = 18f;
        public const float SeaLevel = -24f;
        public const float MoorWaterLevel = -0.75f;

        // Wege (werden in den Boden gemalt)
        private static readonly Vector2[][] Paths =
        {
            new[] { new Vector2(0f, 0f), new Vector2(0f, 150f), new Vector2(RampX, 300f), new Vector2(RampX, 430f), new Vector2(RampX, 610f), new Vector2(0f, 700f) },
            new[] { new Vector2(0f, 30f), new Vector2(45f, 150f), new Vector2(50f, 260f), new Vector2(60f, 330f) },
            new[] { new Vector2(0f, 0f), new Vector2(110f, -40f), new Vector2(230f, -60f), new Vector2(330f, -20f), new Vector2(505f, 60f) },
            new[] { new Vector2(0f, 0f), new Vector2(-45f, -45f), new Vector2(-150f, -120f), new Vector2(-290f, -200f) },
            new[] { new Vector2(-45f, -45f), new Vector2(-60f, -50f) },
            new[] { new Vector2(-40f, 20f), new Vector2(-120f, 110f), new Vector2(-250f, 240f) },
            // 28.09.: alter Weg zum Ring von Kareth (Kolosseum), zweigt vom Ostweg ab
            ColosseumBuilder.ApproachPath,
            // 29.09.: Weideweg vom Holzfaellerlager zum Weidepass (Westweiden, Pferdehof Hallgrund)
            // (gleiche Punkte wie WestPasturesLayout.KarethRoad – hier als Literal, damit die statische Initialisierung nicht kreist)
            new[] { new Vector2(-250f, 240f), new Vector2(-330f, 236f), new Vector2(-420f, 247f), new Vector2(-520f, 250f), new Vector2(-600f, 250f) }
        };

        private static Material _barkMat, _pineMat, _pineDark, _deadMat, _woodMat, _woodDark, _waterMoor, _sea, _stoneMat;
        private static Mesh _cone;

        // ================================================================== Hoehe

        private static float S(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

        /// <summary>Gelaendehoehe (m) an einem Weltpunkt – dieselbe Funktion, aus der das Terrain gebaut wird.
        /// = bisheriges Kareth (<see cref="BaseHeight"/>) + P2.5-Relief (<see cref="KarethRelief"/>, abschaltbar).</summary>
        public static float Height(float x, float z) => BaseHeight(x, z) + KarethRelief.Offset(x, z);

        /// <summary>Wege fuer das Relief (Sattel an Wegen, ebener Wegquerschnitt).</summary>
        public static Vector2[][] RoadPaths => Paths;

        /// <summary>Das Kareth-Gelaende vor dem Relief-Pass (unveraendert seit 29.09.).</summary>
        public static float BaseHeight(float x, float z)
        {
            float n1 = Mathf.PerlinNoise(x * 0.011f + 100f, z * 0.011f + 100f) - 0.5f;
            float n2 = Mathf.PerlinNoise(x * 0.043f + 7f, z * 0.043f + 3f) - 0.5f;
            float h = 0f;

            // Kiefernhang: nach Westen ansteigende Huegel
            float west = S(-140f, -520f, x);
            h += west * (16f + n1 * 16f) + west * n2 * 3f;
            h += n1 * 3f * (1f - west);

            // Glockenmoor: flache nasse Senke
            float dm = Vector2.Distance(new Vector2(x, z), MoorCenter);
            float bowl = 1f - S(125f, 205f, dm);
            h = Mathf.Lerp(h, -1.4f + n2 * 1.4f, bowl);

            // Aschenfeld: eingeebnet
            float ash = S(170f, 235f, x) * S(-290f, -235f, z) * (1f - S(310f, 370f, z)) * (1f - S(505f, 535f, x));
            h = Mathf.Lerp(h, 0.5f + n2 * 1.6f, ash);

            // Felswand im Osten (Uhrwerk)
            float east = S(515f, 560f, x);
            h += east * (78f + n1 * 10f);

            // Granitplateau im Norden: steile Kante, Rampe bei x = RampX
            float corridor = 1f - S(12f, 26f, Mathf.Abs(x - RampX));
            float steep = S(560f, 588f, z);
            float ramp = Mathf.Clamp01(Mathf.InverseLerp(430f, 605f, z));
            float plateau = Mathf.Lerp(steep, ramp, corridor);
            h = Mathf.Lerp(h, PlateauHeight + n2 * 1.2f, plateau);

            // Randgebirge
            h += S(790f, 850f, z) * (50f + n1 * 20f);
            h += S(-535f, -600f, x) * (40f + n2 * 10f);

            // Suedkamm (nicht an der Kueste)
            float coastLine = (x + z + 520f) * 0.7071f; // < 0 = jenseits der Kuestenlinie (Suedwesten)
            h += S(-275f, -345f, z) * 24f * S(-40f, 30f, coastLine);

            // Steilkueste: jenseits der Linie faellt das Land ins Meer
            float sea = S(0f, -30f, coastLine);
            h = Mathf.Lerp(h, SeaLevel - 6f, sea);

            // Flacher Kern um das Lager (alle bisherigen Inhalte stehen auf y = 0)
            float dc = Vector2.Distance(new Vector2(x, z), CoreCenter);
            float core = 1f - S(150f, 230f, dc);
            h = Mathf.Lerp(h, 0f, core);
            return h;
        }

        /// <summary>Punkt auf dem Boden.</summary>
        public static Vector3 OnGround(float x, float z, float lift = 0f) => new Vector3(x, Height(x, z) + lift, z);

        public static bool InMoor(float x, float z) => Vector2.Distance(new Vector2(x, z), MoorCenter) < 150f;

        // ================================================================== Aufbau

        public static void Build(Transform env)
        {
            ProjectSetup.EnsureFolder(Folder);
            KarethRelief.Refresh(); // P2.5: Schalter aus dem Kareth-Layout neu lesen
            _allPaths = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(System.Linq.Enumerable.Concat(Paths, GiantBuilder.ExtraPaths()), StartCampLayout.Paths));
            CreateMaterials();
            Transform g = K.Group("Kareth (Gelaende)", env);

            Terrain terrain = BuildTerrain(g);
            BuildWater(g);
            BuildForest(g);
            BuildRocks(g);
            BuildGrassDetails(terrain);
            BuildLumberCamp(g);
            BuildMoorDressing(g);
            Debug.Log($"[Ashveil] Kareth-Gelaende: {Size:0} x {Size:0} m, Terrain '{terrain.name}', Relief {(KarethRelief.Enabled ? "an" : "aus")}.");
        }

        private static void CreateMaterials()
        {
            _barkMat = K.Mat("Pine_Bark", new Color(0.24f, 0.18f, 0.13f));
            _pineMat = K.Mat("Pine_Needles", new Color(0.13f, 0.21f, 0.14f));
            _pineDark = K.Mat("Pine_Needles_Dark", new Color(0.09f, 0.15f, 0.11f));
            _deadMat = K.Mat("Bark_Dead", new Color(0.19f, 0.16f, 0.14f));
            _woodMat = K.Mat("Wood", new Color(0.36f, 0.25f, 0.16f));
            _woodDark = K.Mat("Wood_Dark", new Color(0.2f, 0.14f, 0.1f));
            _stoneMat = K.Mat("Stone_Light", new Color(0.53f, 0.52f, 0.49f));
            _waterMoor = K.Mat("Water_Moor", new Color(0.08f, 0.105f, 0.1f), 0.82f);
            _sea = K.Mat("Water_Sea", new Color(0.07f, 0.1f, 0.12f), 0.82f);
            foreach (Material m in new[] { _barkMat, _pineMat, _pineDark })
            {
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }
        }

        // ------------------------------------------------------------------ Terrain

        private static Terrain BuildTerrain(Transform parent)
        {
            string dataPath = Folder + "/Kareth_TerrainData.asset";
            AssetDatabase.DeleteAsset(dataPath);
            var data = new TerrainData
            {
                heightmapResolution = HeightRes,
                alphamapResolution = SplatRes,
                baseMapResolution = 512
            };
            data.SetDetailResolution(32, 16);
            data.size = new Vector3(Size, HeightRange, Size);

            var heights = new float[HeightRes, HeightRes];
            for (int iz = 0; iz < HeightRes; iz++)
            {
                float z = MinZ + iz / (float)(HeightRes - 1) * Size;
                for (int ix = 0; ix < HeightRes; ix++)
                {
                    float x = MinX + ix / (float)(HeightRes - 1) * Size;
                    heights[iz, ix] = Mathf.Clamp01((Height(x, z) - BaseY) / HeightRange);
                }
            }
            data.SetHeights(0, 0, heights);

            data.terrainLayers = CreateLayers();
            PaintSplat(data);
            AssetDatabase.CreateAsset(data, dataPath);
            // Splat-Texturen muessen als Unter-Assets gespeichert werden, sonst malt Unity nach dem Neuladen nur Ebene 0
            foreach (Texture2D alpha in data.alphamapTextures)
                if (alpha != null && !AssetDatabase.Contains(alpha)) AssetDatabase.AddObjectToAsset(alpha, data);
            // 27.09.: Tunnel ins Grenzgebirge – einzige Aenderung an Kareth (schmale Kerbe am Westportal)
            BorderTunnel.ApplyKarethPortal(data);
            // 28.09.: Kolosseum-Vorplatz eben (Nordost-Aschenfeld)
            ColosseumBuilder.ApplyKarethForecourt(data, new Vector3(MinX, BaseY, MinZ));
            // 29.09.: Weidepass ins Randgebirge (Westweiden)
            WestPasturesBuilder.ApplyKarethPass(data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain Kareth";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(MinX, BaseY, MinZ);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
            var terrain = go.GetComponent<Terrain>();
            terrain.allowAutoConnect = true; // Nachbarkacheln (Grenzgebirge) gleicher Gruppe verbinden sich ohne Naht
            terrain.groupingID = 0;
            terrain.heightmapPixelError = 6f;
            terrain.basemapDistance = 600f;
            terrain.drawInstanced = true;
            Material mat = TerrainMaterial();
            if (mat != null) terrain.materialTemplate = mat;
            return terrain;
        }

        private static Material TerrainMaterial()
        {
            string path = Folder + "/Kareth_Terrain.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader s = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (s == null) return null;
            if (mat == null)
            {
                mat = new Material(s);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = s;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Reihenfolge = Kanal im Splatmap
        private const int LGrass = 0, LForest = 1, LMoor = 2, LAsh = 3, LRock = 4, LPath = 5, LSand = 6;
        /// <summary>Ebene "festgetretene Erde" (Wege, Plaetze) – fuer gezielte Nacharbeiten (Kolosseum-Vorplatz).</summary>
        public const int PathLayer = LPath;

        private static TerrainLayer[] CreateLayers()
        {
            return new[]
            {
                Layer("Grass", new Color(0.33f, 0.39f, 0.22f), 0.08f, 11),
                Layer("ForestFloor", new Color(0.27f, 0.28f, 0.18f), 0.1f, 12),
                Layer("Moor", new Color(0.22f, 0.25f, 0.2f), 0.08f, 13),
                Layer("Ash", new Color(0.31f, 0.29f, 0.27f), 0.08f, 14),
                Layer("Rock", new Color(0.44f, 0.44f, 0.45f), 0.1f, 15),
                Layer("Path", new Color(0.42f, 0.34f, 0.24f), 0.08f, 16),
                Layer("Sand", new Color(0.62f, 0.58f, 0.5f), 0.08f, 17)
            };
        }

        /// <summary>
        /// Boden-Textur im Ashveil-Stil (Art-Bible): 32 x 32 Pixel auf 4 m, Punktfilter, wenige Farbtoene mit
        /// kleinen Mustern (Halme, Nadeln, Kiesel, Risse, Glutkoerner). Alpha = Glaette (matt).
        /// </summary>
        private static TerrainLayer Layer(string name, Color color, float variation, int seed)
        {
            string texPath = Folder + "/Tex_" + name + ".asset";
            string layerPath = Folder + "/Layer_" + name + ".terrainlayer";
            AssetDatabase.DeleteAsset(texPath);
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true)
            {
                name = "Tex_" + name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                anisoLevel = 0
            };
            var rng = new System.Random(seed);
            var px = new Color[n * n];
            float[,] noise = PeriodicNoise(n, 8, rng);
            Color dark = color * 0.84f, light = color * 1.14f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float v = noise[x, y];
                Color c = v > 0.35f ? light : v < -0.35f ? dark : color;
                c *= 1f + ((float)rng.NextDouble() - 0.5f) * variation;
                px[y * n + x] = c;
            }
            PaintPattern(name, px, n, rng, color);
            for (int i = 0; i < px.Length; i++) px[i].a = 0.04f;
            tex.SetPixels(px);
            tex.Apply(true);
            AssetDatabase.CreateAsset(tex, texPath);

            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, layerPath);
            }
            layer.diffuseTexture = tex;
            layer.tileSize = new Vector2(4f, 4f);
            layer.smoothness = 0f;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        /// <summary>Kachelbares Wertrauschen -1..1 (Gitter aus cell x cell Pixeln).</summary>
        private static float[,] PeriodicNoise(int n, int cell, System.Random rng)
        {
            int g = n / cell;
            var grid = new float[g, g];
            for (int i = 0; i < g; i++)
            for (int j = 0; j < g; j++) grid[i, j] = (float)rng.NextDouble() * 2f - 1f;
            var r = new float[n, n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float fx = x / (float)cell, fy = y / (float)cell;
                int x0 = (int)fx % g, y0 = (int)fy % g, x1 = (x0 + 1) % g, y1 = (y0 + 1) % g;
                float tx = fx - (int)fx, ty = fy - (int)fy;
                float a = Mathf.Lerp(grid[x0, y0], grid[x1, y0], tx);
                float b = Mathf.Lerp(grid[x0, y1], grid[x1, y1], tx);
                r[x, y] = Mathf.Lerp(a, b, ty);
            }
            return r;
        }

        private static void PaintPattern(string name, Color[] px, int n, System.Random rng, Color baseColor)
        {
            void Set(int x, int y, Color c) => px[((y % n + n) % n) * n + ((x % n + n) % n)] = c;
            switch (name)
            {
                case "Grass":
                    for (int i = 0; i < 26; i++)
                    {
                        int x = rng.Next(n), y = rng.Next(n), len = 2 + rng.Next(2);
                        Color blade = baseColor * (1.15f + (float)rng.NextDouble() * 0.12f);
                        for (int k = 0; k < len; k++) Set(x, y + k, blade);
                    }
                    for (int i = 0; i < 4; i++) Set(rng.Next(n), rng.Next(n), new Color(0.46f, 0.44f, 0.24f));
                    break;
                case "ForestFloor":
                    for (int i = 0; i < 40; i++)
                    {
                        int x = rng.Next(n), y = rng.Next(n);
                        Color needle = new Color(0.33f, 0.24f, 0.16f) * (0.8f + (float)rng.NextDouble() * 0.4f);
                        Set(x, y, needle);
                        if (rng.NextDouble() < 0.5) Set(x + 1, y + (rng.NextDouble() < 0.5 ? 1 : 0), needle);
                    }
                    break;
                case "Moor":
                    for (int i = 0; i < 5; i++)
                    {
                        int cx = rng.Next(n), cy = rng.Next(n), r = 1 + rng.Next(2);
                        for (int dx = -r; dx <= r; dx++)
                        for (int dy = -r; dy <= r; dy++)
                            if (dx * dx + dy * dy <= r * r) Set(cx + dx, cy + dy, new Color(0.11f, 0.14f, 0.15f));
                    }
                    break;
                case "Ash":
                    for (int i = 0; i < 30; i++) Set(rng.Next(n), rng.Next(n), new Color(0.3f, 0.29f, 0.28f));
                    for (int i = 0; i < 6; i++) Set(rng.Next(n), rng.Next(n), new Color(0.75f, 0.32f, 0.12f)); // Glutkoerner
                    break;
                case "Rock":
                    for (int c = 0; c < 4; c++)
                    {
                        int x = rng.Next(n), y = rng.Next(n);
                        for (int k = 0; k < 9 + rng.Next(8); k++)
                        {
                            Set(x, y, baseColor * 0.55f);
                            x += rng.Next(3) - 1;
                            y += rng.NextDouble() < 0.7 ? 1 : 0;
                        }
                    }
                    for (int i = 0; i < 12; i++) Set(rng.Next(n), rng.Next(n), baseColor * 1.35f);
                    break;
                case "Path":
                    for (int i = 0; i < 18; i++)
                    {
                        int x = rng.Next(n), y = rng.Next(n);
                        Color pebble = new Color(0.47f, 0.45f, 0.42f) * (0.85f + (float)rng.NextDouble() * 0.3f);
                        Set(x, y, pebble);
                        Set(x + 1, y, pebble * 0.8f);
                    }
                    for (int y = 0; y < n; y++) { Set(9, y, px[y * n + 9] * 0.85f); Set(22, y, px[y * n + 22] * 0.85f); } // Fahrrinnen
                    break;
                case "Sand":
                    for (int i = 0; i < 14; i++) Set(rng.Next(n), rng.Next(n), new Color(0.78f, 0.76f, 0.7f));
                    for (int i = 0; i < 8; i++) Set(rng.Next(n), rng.Next(n), new Color(0.4f, 0.38f, 0.34f));
                    break;
            }
        }

        private static void PaintSplat(TerrainData data)
        {
            int layers = data.terrainLayers.Length;
            var map = new float[SplatRes, SplatRes, layers];
            float cell = Size / SplatRes;
            var w = new float[layers];
            for (int iz = 0; iz < SplatRes; iz++)
            {
                float z = MinZ + (iz + 0.5f) * cell;
                for (int ix = 0; ix < SplatRes; ix++)
                {
                    float x = MinX + (ix + 0.5f) * cell;
                    System.Array.Clear(w, 0, w.Length);
                    float h = Height(x, z);
                    float hx = Height(x + 2f, z) - Height(x - 2f, z);
                    float hz = Height(x, z + 2f) - Height(x, z - 2f);
                    float slope = Mathf.Atan(Mathf.Sqrt(hx * hx + hz * hz) / 4f) * Mathf.Rad2Deg;

                    float west = S(-110f, -200f, x);
                    float moor = 1f - S(120f, 185f, Vector2.Distance(new Vector2(x, z), MoorCenter));
                    float ash = S(170f, 235f, x) * S(-290f, -235f, z) * (1f - S(310f, 370f, z)) * (1f - S(505f, 535f, x));
                    float coastLine = (x + z + 520f) * 0.7071f;
                    float sand = S(25f, 0f, coastLine);
                    float rock = Mathf.Max(S(28f, 42f, slope), S(PlateauHeight - 4f, PlateauHeight + 1f, h) * S(540f, 580f, z) * 0.7f);
                    float path = PathWeight(x, z);

                    // Gras nur dort, wo keine andere Zone liegt (sonst schimmert es gruen durch Asche und Moor)
                    w[LGrass] = Mathf.Max(0.04f, 1f - Mathf.Max(Mathf.Max(west, moor), Mathf.Max(ash, sand)));
                    w[LForest] = west * 1.4f;
                    w[LMoor] = moor * 1.6f;
                    w[LAsh] = ash * 1.8f;
                    w[LSand] = sand * 1.5f;
                    w[LRock] = rock * 3f;
                    w[LPath] = path * 4f;
                    float sum = 0f;
                    for (int l = 0; l < layers; l++) sum += w[l];
                    for (int l = 0; l < layers; l++) map[iz, ix, l] = w[l] / sum;
                }
            }
            data.SetAlphamaps(0, 0, map);
        }

        private static float PathWeight(float x, float z)
        {
            var p = new Vector2(x, z);
            float best = float.MaxValue;
            foreach (Vector2[] line in AllPaths)
                for (int i = 0; i + 1 < line.Length; i++)
                    best = Mathf.Min(best, DistanceToSegment(p, line[i], line[i + 1]));
            // Plaetze im Startlager (Feuerplatz, Markt, Vorplatz der Halle …) – festgetretene Erde
            foreach ((Vector2 c, float r) in StartCampLayout.Plazas)
                best = Mathf.Min(best, Mathf.Max(0f, Vector2.Distance(p, c) - r + 1.6f));
            return 1f - S(1.6f, 3.4f, best);
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Wege plus die Zusatzwege des Riesen (Halle, Schaedeldecke). Wird bei jedem Aufbau neu gesetzt.</summary>
        private static Vector2[][] _allPaths;
        private static Vector2[][] AllPaths => _allPaths ?? Paths;

        /// <summary>Nahe an einem Weg – oder auf der Bodenflaeche des Riesen (dort wachsen weder Baeume noch Felsen).</summary>
        private static bool NearPath(float x, float z, float margin)
        {
            if (GiantBuilder.Blocks(x, z, margin * 2f)) return true;
            if (x < WestPasturesLayout.PassX0 + 6f && Mathf.Abs(z - WestPasturesLayout.PassZ) < 40f) return true; // Weidepass-Kerbe
            var p = new Vector2(x, z);
            foreach (Vector2[] line in AllPaths)
                for (int i = 0; i + 1 < line.Length; i++)
                    if (DistanceToSegment(p, line[i], line[i + 1]) < margin) return true;
            return false;
        }

        // ------------------------------------------------------------------ Wasser

        private static void BuildWater(Transform parent)
        {
            Transform w = K.Group("Wasser", parent);
            // Moor: flache Wasserflaeche (ohne Kollision – man watet hindurch)
            K.Prim(PrimitiveType.Cylinder, "Moorwasser", w, new Vector3(MoorCenter.x, MoorWaterLevel, MoorCenter.y),
                new Vector3(300f, 0.01f, 300f), Vector3.zero, _waterMoor, false);
            // Meer vor der Steilkueste
            K.Box("Meer", w, new Vector3(-560f, SeaLevel, -560f), new Vector3(900f, 0.1f, 900f), _sea, new Vector3(0f, 45f, 0f), false);
        }

        // ------------------------------------------------------------------ Wald

        private static Mesh Cone()
        {
            if (_cone != null) return _cone;
            string path = Folder + "/Mesh_Cone.asset";
            _cone = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (_cone != null) return _cone;
            const int sides = 10;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            // Mantel mit eigenen Vertices pro Seite (harte Kanten, Greybox-Look)
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                int b = verts.Count;
                verts.Add(new Vector3(Mathf.Sin(a0) * 0.5f, 0f, Mathf.Cos(a0) * 0.5f));
                verts.Add(new Vector3(Mathf.Sin(a1) * 0.5f, 0f, Mathf.Cos(a1) * 0.5f));
                verts.Add(new Vector3(0f, 1f, 0f));
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                // Boden
                int c = verts.Count;
                verts.Add(new Vector3(Mathf.Sin(a0) * 0.5f, 0f, Mathf.Cos(a0) * 0.5f));
                verts.Add(new Vector3(Mathf.Sin(a1) * 0.5f, 0f, Mathf.Cos(a1) * 0.5f));
                verts.Add(Vector3.zero);
                tris.Add(c); tris.Add(c + 1); tris.Add(c + 2);
            }
            _cone = new Mesh { name = "Cone" };
            _cone.SetVertices(verts);
            _cone.SetTriangles(tris, 0);
            _cone.RecalculateNormals();
            _cone.RecalculateBounds();
            AssetDatabase.CreateAsset(_cone, path);
            return _cone;
        }

        private static GameObject MeshObject(string name, Transform parent, Mesh mesh, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        private static readonly (string model, float height)[] PineModels = { ("SM_Pine_A", 12f), ("SM_Pine_B", 14f), ("SM_Pine_C", 10f) };

        private static void Pine(Transform parent, Vector3 basePos, float height, bool dark)
        {
            // Blender-Kiefer (Art-Bible), sonst Greybox-Kegel
            int variant = Mathf.Abs(Mathf.RoundToInt(basePos.x * 7.3f + basePos.z * 3.1f)) % PineModels.Length;
            (string model, float mh) = PineModels[variant];
            if (ArtModels.Has(model))
            {
                GameObject tree = ArtModels.Place(model, parent, basePos, (basePos.x * 13f + basePos.z * 7f) % 360f, height / mh,
                    ArtModels.ColliderKind.Trunk, true);
                if (tree != null) tree.name = "Kiefer";
                return;
            }
            Transform t = K.Group("Kiefer", parent, basePos, new Vector3(0f, basePos.x * 13f % 360f, 0f));
            float trunkR = 0.22f + height * 0.012f;
            K.Prim(PrimitiveType.Cylinder, "Stamm", t, new Vector3(0f, height * 0.5f, 0f), new Vector3(trunkR * 2f, height * 0.5f, trunkR * 2f), Vector3.zero, _barkMat);
            Material needles = dark ? _pineDark : _pineMat;
            float crownBase = height * 0.3f;
            float crownH = height * 0.78f;
            float crownR = height * 0.26f;
            MeshObject("Krone", t, Cone(), new Vector3(0f, crownBase, 0f), new Vector3(crownR * 2f, crownH, crownR * 2f), needles);
            MeshObject("Krone Oben", t, Cone(), new Vector3(0f, crownBase + crownH * 0.4f, 0f), new Vector3(crownR * 1.4f, crownH * 0.62f, crownR * 1.4f), needles);
        }

        private static void BuildForest(Transform parent)
        {
            Transform f = K.Group("Kiefernhang (Wald)", parent);
            var rng = new System.Random(4242);
            // Lichtungen: Rudel, Lager, Wege, Gefallener Waechter
            var clearings = new List<(Vector2 c, float r)>
            {
                (new Vector2(-42f, 62f), 22f), (new Vector2(-82f, 96f), 20f), (new Vector2(-200f, 150f), 20f),
                (new Vector2(LumberCamp.x, LumberCamp.z), 32f), (new Vector2(-72f, 22f), 45f), (new Vector2(-10f, 20f), 60f),
                (new Vector2(-60f, -50f), 50f), (new Vector2(-330f, 80f), 18f), (new Vector2(-150f, 330f), 22f)
            };
            // Quest-Orte im Wald (Oedis Huette, Harzerlager, Speiernest) – eine Tabelle: KarethQuestSites
            clearings.AddRange(KarethQuestSites.ForestClearings());
            int placed = 0;
            for (int attempt = 0; attempt < 5200 && placed < 620; attempt++)
            {
                float x = Mathf.Lerp(-560f, -60f, (float)rng.NextDouble());
                float z = Mathf.Lerp(-230f, 560f, (float)rng.NextDouble());
                // Dichte: im Westen voll, zum Lager hin ausduennen
                float density = S(-60f, -170f, x) * (1f - S(560f, 600f, z) * 0.9f);
                if (rng.NextDouble() > density) continue;
                if ((x + z + 520f) * 0.7071f < 20f) continue; // nicht an die Kueste
                if (NearPath(x, z, 5f)) continue;
                bool blocked = false;
                foreach ((Vector2 c, float r) in clearings)
                    if (Vector2.Distance(new Vector2(x, z), c) < r) { blocked = true; break; }
                if (blocked) continue;
                float h = Height(x, z);
                float slope = Mathf.Abs(Height(x + 2f, z) - Height(x - 2f, z)) + Mathf.Abs(Height(x, z + 2f) - Height(x, z - 2f));
                if (slope > 3.2f) continue;
                float height = 9f + (float)rng.NextDouble() * 9f;
                Pine(f, new Vector3(x, h - 0.2f, z), height, rng.NextDouble() < 0.4);
                placed++;
            }

            // Einzelne Kiefern am Moorrand und am Plateau
            for (int i = 0; i < 70; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 150f + (float)rng.NextDouble() * 60f;
                float x = MoorCenter.x + Mathf.Cos(a) * r, z = MoorCenter.y + Mathf.Sin(a) * r;
                if (x > 170f || NearPath(x, z, 5f) || Vector2.Distance(new Vector2(x, z), CoreCenter) < 90f) continue;
                Pine(f, new Vector3(x, Height(x, z) - 0.2f, z), 7f + (float)rng.NextDouble() * 6f, true);
                placed++;
            }

            // Abgestorbene Baeume im Moor
            Transform dead = K.Group("Tote Baeume (Moor)", parent);
            for (int i = 0; i < 40; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = (float)rng.NextDouble() * 130f;
                float x = MoorCenter.x + Mathf.Cos(a) * r, z = MoorCenter.y + Mathf.Sin(a) * r;
                if (NearPath(x, z, 4f)) continue;
                float h = 3f + (float)rng.NextDouble() * 6f;
                if (ArtModels.Has("SM_DeadTree"))
                {
                    ArtModels.Place("SM_DeadTree", dead, OnGround(x, z, -0.3f), (float)rng.NextDouble() * 360f, h / 7f,
                        ArtModels.ColliderKind.Trunk, true);
                    continue;
                }
                K.Prim(PrimitiveType.Cylinder, "Toter Baum", dead, new Vector3(x, Height(x, z) + h * 0.5f - 0.3f, z), new Vector3(0.5f, h * 0.5f, 0.5f),
                    new Vector3((float)rng.NextDouble() * 14f - 7f, 0f, (float)rng.NextDouble() * 14f - 7f), _deadMat);
            }
            Debug.Log($"[Ashveil] Kareth: {placed} Kiefern gesetzt.");
        }

        // ------------------------------------------------------------------ Felsen (Blender)

        private static void BuildRocks(Transform parent)
        {
            string[] rocks = { "SM_Rock_A", "SM_Rock_B", "SM_Rock_C" };
            if (!ArtModels.Has(rocks[0])) return;
            Transform r = K.Group("Felsen", parent);
            var rng = new System.Random(777);
            int placed = 0;
            for (int attempt = 0; attempt < 4000 && placed < 420; attempt++)
            {
                float x = Mathf.Lerp(MinX + 20f, MaxX - 20f, (float)rng.NextDouble());
                float z = Mathf.Lerp(MinZ + 20f, MaxZ - 20f, (float)rng.NextDouble());
                if (Vector2.Distance(new Vector2(x, z), Vector2.zero) < 32f) continue;          // Lager frei (Felsen im erweiterten Lager raeumt der Lageraufbau)
                if (NearPath(x, z, 4f)) continue;
                if (Mathf.Abs(x + 60f) < 30f && Mathf.Abs(z + 50f) < 40f) continue;            // Testparcours
                float h = Height(x, z);
                if (h < SeaLevel + 1f) continue;
                // Aschenfeld bleibt frei: Arena des Glockenhirten und Jagdgebiet der Grauen Mutter (Verstecke, Sprungwege)
                float ashField = S(150f, 215f, x) * S(-310f, -255f, z) * (1f - S(330f, 390f, z)) * (1f - S(525f, 555f, x));
                if (ashField > 0.01f) continue;
                float slope = Mathf.Abs(Height(x + 2f, z) - Height(x - 2f, z)) + Mathf.Abs(Height(x, z + 2f) - Height(x, z - 2f));
                // Haeufiger am Hang, im Wald und an Kanten, seltener auf Wiesen und im Moor
                float want = 0.18f + Mathf.Clamp01(slope / 6f) * 0.6f + S(-120f, -300f, x) * 0.25f;
                if (InMoor(x, z)) want *= 0.35f;
                if (rng.NextDouble() > want) continue;
                float scale = 0.35f + (float)rng.NextDouble() * (slope > 3f ? 2.4f : 1.4f);
                string model = rocks[rng.Next(rocks.Length)];
                // Kleine Steine ohne Kollision (man bleibt nicht an Kieseln haengen)
                ArtModels.Place(model, r, new Vector3(x, h - 0.25f * scale, z), (float)rng.NextDouble() * 360f, scale,
                    scale < 0.8f ? ArtModels.ColliderKind.None : ArtModels.ColliderKind.Mesh, true);
                placed++;
            }
            // Stuempfe und Baumstaemme im Wald
            if (ArtModels.Has("SM_Stump"))
                for (int i = 0; i < 90; i++)
                {
                    float x = Mathf.Lerp(-520f, -90f, (float)rng.NextDouble()), z = Mathf.Lerp(-200f, 520f, (float)rng.NextDouble());
                    if (NearPath(x, z, 4f) || (x + z + 520f) * 0.7071f < 20f) continue;
                    bool log = rng.NextDouble() < 0.35;
                    ArtModels.Place(log ? "SM_Log" : "SM_Stump", r, OnGround(x, z, -0.05f), (float)rng.NextDouble() * 360f,
                        0.8f + (float)rng.NextDouble() * 0.5f, log ? ArtModels.ColliderKind.Capsule : ArtModels.ColliderKind.Capsule, true);
                }
            Debug.Log($"[Ashveil] Kareth: {placed} Felsen gesetzt.");
        }

        // ------------------------------------------------------------------ Gras und Farn (Terrain-Details, Wind-Shader)

        private static void BuildGrassDetails(Terrain terrain)
        {
            GameObject grass = ArtModels.Get("SM_GrassClump"), fern = ArtModels.Get("SM_Fern");
            if (grass == null || terrain == null) return;
            TerrainData data = terrain.terrainData;
            const int res = 1024;
            data.SetDetailResolution(res, 32);
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            var protos = new System.Collections.Generic.List<DetailPrototype>
            {
                new DetailPrototype
                {
                    prototype = grass, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                    minWidth = 0.8f, maxWidth = 1.6f, minHeight = 0.7f, maxHeight = 1.5f, noiseSpread = 0.35f, alignToGround = 0.5f
                }
            };
            if (fern != null)
                protos.Add(new DetailPrototype
                {
                    prototype = fern, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                    minWidth = 0.8f, maxWidth = 1.4f, minHeight = 0.8f, maxHeight = 1.5f, noiseSpread = 0.5f, alignToGround = 0.3f
                });
            data.detailPrototypes = protos.ToArray();

            var grassMap = new int[res, res];
            var fernMap = new int[res, res];
            var rng = new System.Random(4711);
            float cell = Size / res;
            long total = 0;
            for (int iz = 0; iz < res; iz++)
            {
                float z = MinZ + (iz + 0.5f) * cell;
                for (int ix = 0; ix < res; ix++)
                {
                    float x = MinX + (ix + 0.5f) * cell;
                    float h = Height(x, z);
                    if (h < 0.2f && InMoor(x, z)) continue;                             // Moorwasser
                    if (h > PlateauHeight - 3f || h < SeaLevel + 2f) continue;          // Fels / Kueste
                    float ash = S(170f, 235f, x) * S(-290f, -235f, z) * (1f - S(310f, 370f, z)) * (1f - S(505f, 535f, x));
                    if (ash > 0.3f) continue;
                    if ((x + z + 520f) * 0.7071f < 25f) continue;                       // Sand
                    float slope = Mathf.Abs(Height(x + 1.5f, z) - Height(x - 1.5f, z)) + Mathf.Abs(Height(x, z + 1.5f) - Height(x, z - 1.5f));
                    if (slope > 2.6f) continue;
                    if (PathWeight(x, z) > 0.15f) continue;
                    if (GiantBuilder.Blocks(x, z, 0f, false)) continue;                   // Fels des Riesen
                    float campDist = new Vector2(x, z).magnitude;
                    if (campDist < 9f) continue;                                         // Feuerplatz
                    float density = StartCampLayout.Inside(x, z) ? 0.4f : 1f;           // im Lager zertreten
                    float west = S(-110f, -200f, x);
                    int g = rng.NextDouble() < 0.85 * density ? 1 + (rng.NextDouble() < 0.55 ? 1 : 0) + (rng.NextDouble() < 0.25 ? 1 : 0) : 0;
                    if (west > 0.5f) g = Mathf.Max(0, g - 1);
                    grassMap[iz, ix] = g;
                    total += g;
                    if (fern != null && west > 0.3f && rng.NextDouble() < 0.12 + 0.2 * west) fernMap[iz, ix] = 1;
                }
            }
            data.SetDetailLayer(0, 0, 0, grassMap);
            if (fern != null) data.SetDetailLayer(0, 0, 1, fernMap);
            terrain.detailObjectDistance = 85f;
            terrain.detailObjectDensity = 1f;
            data.wavingGrassStrength = 0.2f;
            data.RefreshPrototypes();
            terrain.Flush();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            var check = new System.Text.StringBuilder();
            foreach (DetailPrototype p in data.detailPrototypes)
                check.Append(p.prototype != null ? p.prototype.name : "null").Append(p.Validate(out string err) ? "=ok " : "=FEHLER(" + err + ") ");
            Debug.Log($"[Ashveil] Kareth: Gras gesetzt ({total} Bueschel). Details: {check} Dichte-Skala {QualitySettings.terrainDetailDensityScale:0.00}");
        }

        // ------------------------------------------------------------------ Holzfaellerlager (Verblichene, Stufe 5–7)

        private static void BuildLumberCamp(Transform parent)
        {
            Vector3 c = OnGround(LumberCamp.x, LumberCamp.z);
            Transform camp = K.Group("Holzfaellerlager", parent, c, new Vector3(0f, 30f, 0f));
            // Zwei Huetten
            for (int i = 0; i < 2; i++)
            {
                Transform hut = K.Group("Huette", camp, new Vector3(i == 0 ? -10f : 9f, -0.3f, i == 0 ? 6f : -7f), new Vector3(0f, i * 70f, 0f));
                K.Block("Wand Hinten", hut, new Vector3(0f, 0f, 2.5f), new Vector3(6f, 3f, 0.3f), _woodDark);
                K.Block("Wand L", hut, new Vector3(-3f, 0f, 0f), new Vector3(0.3f, 3f, 5f), _woodDark);
                K.Block("Wand R", hut, new Vector3(3f, 0f, 0f), new Vector3(0.3f, 3f, 5f), _woodDark);
                K.Block("Wand Vorne L", hut, new Vector3(-2f, 0f, -2.5f), new Vector3(2f, 3f, 0.3f), _woodDark);
                K.Block("Wand Vorne R", hut, new Vector3(2f, 0f, -2.5f), new Vector3(2f, 3f, 0.3f), _woodDark);
                K.Box("Dach L", hut, new Vector3(-1.6f, 3.6f, 0f), new Vector3(3.6f, 0.2f, 5.8f), _woodMat, new Vector3(0f, 0f, 28f));
                K.Box("Dach R", hut, new Vector3(1.6f, 3.6f, 0f), new Vector3(3.6f, 0.2f, 5.8f), _woodMat, new Vector3(0f, 0f, -28f));
            }
            // Holzstapel und Saegebock
            for (int i = 0; i < 6; i++)
                K.Prim(PrimitiveType.Cylinder, "Stamm", camp, new Vector3(2f + (i % 3) * 0.1f, 0.35f + (i / 3) * 0.6f, 4f + (i % 3) * 0.7f),
                    new Vector3(0.6f, 3f, 0.6f), new Vector3(0f, 0f, 90f), _barkMat);
            K.Block("Saegebock", camp, new Vector3(-4f, -0.2f, -3f), new Vector3(0.4f, 1f, 2.2f), _woodMat, 20f);
            for (int i = 0; i < 9; i++)
            {
                float a = i * 40f * Mathf.Deg2Rad;
                K.Prim(PrimitiveType.Cylinder, "Baumstumpf", camp, new Vector3(Mathf.Sin(a) * 20f, 0.2f, Mathf.Cos(a) * 20f), new Vector3(0.9f, 0.5f, 0.9f), Vector3.zero, _barkMat);
            }
            K.Label(camp, new Vector3(0f, 4.5f, 0f), "Holzfaellerlager", "Verblichene · Stufe 5-7", WorldLabelStyle.Place, 70f);
        }

        // ------------------------------------------------------------------ Moor

        private static void BuildMoorDressing(Transform parent)
        {
            // Stege ueber das Moorwasser entlang des Weges
            Transform m = K.Group("Moorstege", parent);
            var steg = new[] { new Vector2(47f, 200f), new Vector2(50f, 260f), new Vector2(60f, 330f) };
            for (int i = 0; i + 1 < steg.Length; i++)
            {
                Vector2 a = steg[i], b = steg[i + 1];
                int n = Mathf.CeilToInt(Vector2.Distance(a, b) / 2.2f);
                for (int k = 0; k <= n; k++)
                {
                    Vector2 p = Vector2.Lerp(a, b, k / (float)n);
                    float y = Mathf.Max(Height(p.x, p.y), MoorWaterLevel) + 0.12f;
                    float yaw = Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg;
                    K.Box("Planke", m, new Vector3(p.x, y, p.y), new Vector3(2.4f, 0.12f, 1.9f), _woodMat, new Vector3(0f, yaw, 0f), false);
                }
            }
        }
    }
}
