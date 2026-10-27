using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Ashveil.Core;
using Ashveil.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using K = Ashveil.EditorTools.GreyboxKit;
using L = Ashveil.EditorTools.BorderMountainsLayout;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Grenzgebirge – Greybox als erster Streaming-Prototyp (Large-World Phase D1). Zwei Zellen-Szenen
    /// (BorderMountains_West / _East), je zwei Gelaendekacheln 1200 x 1200 m im Kareth-Raster, ohne NetworkObjects.
    /// Inhalt nach Design ashveil-grenzgebirge.md §39: Dimension, Hoehenunterschied, Wegfuehrung, Klippe, Tal, Fussabdruck,
    /// Sichtbeziehungen – plus Groessenreferenzen (§44), sichere Anker, Quest-Orte, Gebietsluft und NavMesh je Zelle.
    /// Kareth bleibt unangetastet bis auf das Tunnelportal (BorderTunnel).
    /// 30.09. (Max, Weltkarte): Der Kern liegt jetzt suedoestlich von Kareth am Ostende des Grenzgebirges. Gebaut wird
    /// weiter im Entwurfsraum (Koordinaten wie bisher); die Zellen-Wurzel dreht und verschiebt alles in die Welt
    /// (<see cref="BorderMountainsFrame"/>). Gelaendekacheln koennen nicht gedreht werden – ihre Hoehen- und
    /// Farbkarten werden um 90° umkopiert und an der Weltposition gesetzt.
    /// </summary>
    public static class BorderMountainsBuilder
    {
        private const string TerrainFolder = ProjectSetup.Root + "/Terrain";
        private const int HeightRes = 513;
        private const int SplatRes = 512;

        public readonly struct Tile
        {
            public readonly string Name, CellId;
            public readonly Vector2 Origin;
            public Tile(string name, string cell, float x, float z)
            {
                Name = name;
                CellId = cell;
                Origin = new Vector2(x, z);
            }
            public string DataPath => $"{TerrainFolder}/{Name}_TerrainData.asset";
        }

        public static readonly Tile[] Tiles =
        {
            new Tile("GG_West_Sued", WorldManifestBuilder.CellBorderWest, 600f, -350f),
            new Tile("GG_West_Nord", WorldManifestBuilder.CellBorderWest, 600f, 850f),
            new Tile("GG_Ost_Sued", WorldManifestBuilder.CellBorderEast, 1800f, -350f),
            new Tile("GG_Ost_Nord", WorldManifestBuilder.CellBorderEast, 1800f, 850f),
        };

        private static Material _stone, _stoneDark, _wood, _woodDark, _canvas, _warm, _fire, _water, _bone, _metal, _figure, _roof;

        [MenuItem("Ashveil/Welt/Grenzgebirge bauen (Greybox)", priority = 510)]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst den Play-Modus beenden.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Ashveil",
                    "Grenzgebirge (Greybox) wird gebaut: zwei Zellen-Szenen unter Scenes/Regions, Tunnel am Ende des Ostwegs und Fernansicht in Sandbox_M1.\n\n" +
                    "Kareth wird NICHT neu erzeugt – nur am Tunnelportal wird eine schmale Kerbe in die Ostwand gesenkt, und das Kareth-NavMesh wird (in unter einer Sekunde) mit dem Tunnel neu gebacken.\n\nDauert etwa eine halbe Minute.",
                    "Bauen", "Abbrechen"))
                return;
            Build();
        }

        public static void Build()
        {
            var total = Stopwatch.StartNew();
            try
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                Scene world = SceneManager.GetSceneByPath(SandboxSceneBuilder.ScenePath);
                if (!world.IsValid() || !world.isLoaded) world = EditorSceneManager.OpenScene(SandboxSceneBuilder.ScenePath, OpenSceneMode.Single);
                for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                {
                    Scene s = SceneManager.GetSceneAt(i);
                    if (s != world) EditorSceneManager.CloseScene(s, true);
                }
                SceneManager.SetActiveScene(world);

                K.ResetCache();
                CreateMaterials();
                SkyContentBuilder.EnsureBorderBiomes();
                WorldManifest manifest = WorldManifestBuilder.EnsureManifest();

                Debug.Log("[Ashveil] Grenzgebirge Port-Pruefung (C# gegen Python-Prototyp): " + PortCheck());
                EditorUtility.DisplayProgressBar("Ashveil", "Grenzgebirge: Tunnel und Fernansicht in Kareth ...", 0.05f);
                if (!BorderTunnel.ApplyToWorld(world)) return;

                // Gegner-Arten (Prefabs muessen als Netzwerkobjekte registriert sein, Definitionen in der Datenbank)
                _enemies = EnemyBuilder.BuildBorderEnemies();
                var prefabs = new List<GameObject>();
                foreach (EnemyBuilder.BorderEnemy e in _enemies.Values) prefabs.Add(e.Prefab);
                NetworkSetupBuilder.AddSpawnables(prefabs);
                DefinitionValidator.ValidateAndRebuild();
                SceneManager.SetActiveScene(world);

                var report = new List<string>();
                BuildCell(WorldManifestBuilder.CellBorderWest, WorldManifestBuilder.BorderWestScene, 0.15f, report);
                BuildCell(WorldManifestBuilder.CellBorderEast, WorldManifestBuilder.BorderEastScene, 0.55f, report);

                WorldManifestBuilder.EnsureBuildSettings();
                SceneManager.SetActiveScene(world);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Ashveil] Grenzgebirge gebaut ({total.Elapsed.TotalSeconds:0} s, Weltversion {manifest.version}):\n  " + string.Join("\n  ", report) +
                          "\nZum Anschauen: Szenen unter Scenes/Regions zusaetzlich oeffnen (Rechtsklick › Open Scene Additive) – das Streaming folgt in Phase E1.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Hoehen an Stichpunkten, wie sie der Python-Prototyp liefert (ohne Bach; Punkte fern der Naht).</summary>
        private static readonly (float x, float z, float h)[] PortReference =
        {
            (700, 95, 8.785f), (925, 285, 29.79f), (985, 815, 419.884f), (1255, 520, 59.174f), (1500, 1100, 521.984f),
            (1750, 1050, 531.624f), (2250, 385, 96.908f), (2600, 1600, 552.069f), (1000, 1800, 629.443f), (2900, -200, 289.201f),
            (1320, 560, 56.296f)
        };

        public static string PortCheck()
        {
            float max = 0f;
            var sb = new System.Text.StringBuilder();
            foreach (var (x, z, h) in PortReference)
            {
                float c = L.Height(x, z);
                max = Mathf.Max(max, Mathf.Abs(c - h));
                sb.Append($"({x:0},{z:0}) {c:0.00}/{h:0.00}  ");
            }
            return $"groesste Abweichung {max:0.000} m – " + sb;
        }

        // ================================================================== Zelle

        private static void BuildCell(string cellId, string scenePath, float progress, List<string> report)
        {
            var sw = Stopwatch.StartNew();
            ProjectSetup.EnsureFolder(WorldManifestBuilder.RegionFolder);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            Transform root = K.Group("Zelle " + cellId, null);
            root.SetPositionAndRotation(BorderMountainsFrame.RootPosition, BorderMountainsFrame.RootRotation);
            Transform ground = K.Group("Gelaende " + cellId, null);   // Terrains nicht drehbar: eigene, ungedrehte Wurzel
            bool west = cellId == WorldManifestBuilder.CellBorderWest;
            float x0 = west ? L.MinX : L.MinX + L.TileSize, x1 = x0 + L.TileSize;

            int trees = 0;
            foreach (Tile t in Tiles)
            {
                if (t.CellId != cellId) continue;
                EditorUtility.DisplayProgressBar("Ashveil", $"Grenzgebirge: Gelaende {t.Name} ...", progress);
                trees += BuildTile(ground, root, t);
                progress += 0.08f;
            }
            EditorUtility.DisplayProgressBar("Ashveil", $"Grenzgebirge: Orte in {cellId} ...", progress);
            Transform places = K.Group("Orte", root);
            if (west) BuildWestPlaces(places);
            else BuildEastPlaces(places);
            BuildRiver(K.Group("Bach", root), x0, x1);
            BuildWaymarks(K.Group("Wegzeichen", root), x0, x1);
            BuildRocks(K.Group("Felsen", root), x0, x1);
            BuildBoundary(K.Group("Weltrand", root), x0, x1, west);
            BuildAnchors(K.Group("Sichere Anker", root), cellId);
            BuildQuestSites(K.Group("Quest-Orte", root), cellId);
            BuildBiomes(K.Group("Gebietsluft", root), x0, x1);
            BuildEnemies(K.Group("Gegner", root), x0, x1);

            Debug.Log($"[Ashveil] Grenzgebirge {cellId}: Gelaende und Orte fertig ({sw.Elapsed.TotalSeconds:0} s), backe NavMesh ...");
            EditorUtility.DisplayProgressBar("Ashveil", $"Grenzgebirge: NavMesh {cellId} backen (dauert) ...", progress + 0.12f);
            float bake = BakeCellNavMesh(ground, cellId, x0, x1);   // ungedreht: Terrain-Hoehenfelder lassen sich nur achsparallel sauber backen

            EditorSceneManager.SaveScene(scene, scenePath);
            SceneManager.SetActiveScene(SceneManager.GetSceneByPath(SandboxSceneBuilder.ScenePath));
            EditorSceneManager.CloseScene(scene, true);
            report.Add($"{cellId}: {sw.Elapsed.TotalSeconds:0} s, {trees} Baeume, NavMesh {bake:0.0} s -> {scenePath}");
        }

        // ================================================================== Gelaende

        private static int BuildTile(Transform ground, Transform root, Tile t)
        {
            var sw = Stopwatch.StartNew();
            ProjectSetup.EnsureFolder(TerrainFolder);
            var xs = new float[HeightRes];
            var zs = new float[HeightRes];
            var kh = new double[HeightRes];
            for (int i = 0; i < HeightRes; i++)
            {
                // gleiche Ausdruecke wie das Kareth-Terrain -> an der Naht x = 600 exakt dieselben Stuetzstellen
                xs[i] = t.Origin.x + i / (float)(HeightRes - 1) * L.TileSize;
                zs[i] = t.Origin.y + i / (float)(HeightRes - 1) * L.TileSize;
                kh[i] = L.KarethEdge(zs[i]);
            }
            var world = new float[HeightRes, HeightRes];
            var norm = new float[HeightRes, HeightRes];
            Parallel.For(0, HeightRes, iz =>
            {
                for (int ix = 0; ix < HeightRes; ix++)
                {
                    float h = (float)L.Final(xs[ix], zs[iz], kh[iz]);
                    world[iz, ix] = h;
                    norm[iz, ix] = Mathf.Clamp01((h - L.BaseY) / L.HeightRange);
                }
            });

            AssetDatabase.DeleteAsset(t.DataPath);
            var data = new TerrainData { heightmapResolution = HeightRes, alphamapResolution = SplatRes, baseMapResolution = 512 };
            data.SetDetailResolution(32, 16);
            data.size = new Vector3(L.TileSize, L.HeightRange, L.TileSize);
            data.SetHeights(0, 0, ToWorldGrid(norm));
            data.terrainLayers = Layers();
            PaintSplat(data, t, world);
            AssetDatabase.CreateAsset(data, t.DataPath);
            foreach (Texture2D alpha in data.alphamapTextures)
                if (alpha != null && !AssetDatabase.Contains(alpha)) AssetDatabase.AddObjectToAsset(alpha, data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain " + t.Name;
            go.transform.SetParent(ground, false);
            Vector2 wo = WorldOrigin(t);
            go.transform.position = new Vector3(wo.x, L.BaseY, wo.y);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 6f;
            terrain.basemapDistance = 600f;
            terrain.drawInstanced = true;
            terrain.allowAutoConnect = true;
            terrain.groupingID = 0;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainFolder + "/Kareth_Terrain.mat");
            if (mat != null) terrain.materialTemplate = mat;

            int trees = PlaceTrees(K.Group("Baeume " + t.Name, root), t, world);
            Debug.Log($"[Ashveil] Grenzgebirge {t.Name}: {sw.Elapsed.TotalSeconds:0.0} s, Hoehe {Min(world):0}…{Max(world):0} m, {trees} Baeume.");
            return trees;
        }

        /// <summary>Welt-Ecke (x/z min) der gedrehten Kachel.</summary>
        public static Vector2 WorldOrigin(Tile t)
        {
            Vector2 a = BorderMountainsFrame.ToWorld(t.Origin), b = BorderMountainsFrame.ToWorld(t.Origin + new Vector2(L.TileSize, L.TileSize));
            return new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y));
        }

        /// <summary>
        /// Raster aus dem Entwurfsraum [z, x] in die Welt drehen (+90°: Entwurf-x → Welt −z, Entwurf-z → Welt +x):
        /// welt[a, b] = entwurf[b, N−1−a]. Exakt, weil die Stuetzstellen aufeinander fallen.
        /// </summary>
        public static float[,] ToWorldGrid(float[,] local)
        {
            int n = local.GetLength(0);
            var w = new float[n, n];
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++) w[a, b] = local[b, n - 1 - a];
            return w;
        }

        private static float[,,] ToWorldGrid(float[,,] local)
        {
            int n = local.GetLength(0), l = local.GetLength(2);
            var w = new float[n, n, l];
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                    for (int k = 0; k < l; k++) w[a, b, k] = local[b, n - 1 - a, k];
            return w;
        }

        private static float Min(float[,] a)
        {
            float m = float.MaxValue;
            foreach (float v in a) m = Mathf.Min(m, v);
            return m;
        }

        private static float Max(float[,] a)
        {
            float m = float.MinValue;
            foreach (float v in a) m = Mathf.Max(m, v);
            return m;
        }

        // Reihenfolge = Kanal im Splatmap (die ersten sieben wie Kareth)
        private const int LGrass = 0, LForest = 1, LMoor = 2, LAsh = 3, LRock = 4, LPath = 5, LSand = 6, LSnow = 7;
        private static readonly string[] LayerNames = { "Grass", "ForestFloor", "Moor", "Ash", "Rock", "Path", "Sand" };

        private static TerrainLayer[] Layers()
        {
            var list = new List<TerrainLayer>();
            foreach (string n in LayerNames)
            {
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{TerrainFolder}/Layer_{n}.terrainlayer");
                if (l == null)
                {
                    Debug.LogWarning($"[Ashveil] Grenzgebirge: Gelaendeschicht {n} fehlt (erst Sandbox-Szene erstellen) – Ersatz ohne Muster.");
                    l = SimpleLayer(n, new Color(0.4f, 0.4f, 0.38f));
                }
                list.Add(l);
            }
            list.Add(SimpleLayer("Snow", new Color(0.8f, 0.83f, 0.88f)));
            return list.ToArray();
        }

        /// <summary>Schicht im Kareth-Stil (32 x 32 Pixel auf 4 m, Punktfilter, zwei Toene).</summary>
        private static TerrainLayer SimpleLayer(string name, Color color)
        {
            string texPath = $"{TerrainFolder}/Tex_{name}.asset", layerPath = $"{TerrainFolder}/Layer_{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer != null && layer.diffuseTexture != null) return layer;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Tex_" + name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var rng = new System.Random(name.GetHashCode());
            var px = new Color[n * n];
            for (int i = 0; i < px.Length; i++)
            {
                float v = (float)rng.NextDouble();
                Color c = v > 0.8f ? color * 1.07f : v < 0.15f ? color * 0.9f : color;
                c.a = 0.08f;
                px[i] = c;
            }
            tex.SetPixels(px);
            tex.Apply(true);
            AssetDatabase.DeleteAsset(texPath);
            AssetDatabase.CreateAsset(tex, texPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, layerPath);
            }
            layer.diffuseTexture = tex;
            layer.tileSize = new Vector2(4f, 4f);
            layer.smoothness = 0.05f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        private static float S(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

        /// <summary>Grobe Bodenfarbe (Fernansicht): Wiese, Fels an steilen Haengen, Schnee auf den Gipfeln.</summary>
        public static Color GroundColor(float h, float slope)
        {
            var grass = new Color(0.31f, 0.37f, 0.21f);
            var rock = new Color(0.42f, 0.42f, 0.43f);
            var snow = new Color(0.8f, 0.83f, 0.88f);
            Color c = Color.Lerp(grass, rock, Mathf.Max(S(0.55f, 0.9f, slope), S(480f, 640f, h) * 0.7f));
            return Color.Lerp(c, snow, S(640f, 700f, h) * (1f - S(0.9f, 1.3f, slope)));
        }

        private static readonly L.Polyline[] Routes = { L.Tal, L.RouteAWest, L.SchenkeBranch, L.RouteAEast };

        private static void PaintSplat(TerrainData data, Tile t, float[,] world)
        {
            int layers = data.terrainLayers.Length;
            var map = new float[SplatRes, SplatRes, layers];
            float cell = L.TileSize / SplatRes, hcell = L.TileSize / (HeightRes - 1);
            Parallel.For(0, SplatRes, iz =>
            {
                var w = new float[layers];
                float z = t.Origin.y + (iz + 0.5f) * cell;
                for (int ix = 0; ix < SplatRes; ix++)
                {
                    float x = t.Origin.x + (ix + 0.5f) * cell;
                    int hx = Mathf.Clamp(Mathf.RoundToInt((x - t.Origin.x) / hcell), 1, HeightRes - 2);
                    int hz = Mathf.Clamp(Mathf.RoundToInt((z - t.Origin.y) / hcell), 1, HeightRes - 2);
                    float h = world[hz, hx];
                    float gx = (world[hz, hx + 1] - world[hz, hx - 1]) / (2f * hcell);
                    float gz = (world[hz + 1, hx] - world[hz - 1, hx]) / (2f * hcell);
                    float slope = Mathf.Sqrt(gx * gx + gz * gz);
                    System.Array.Clear(w, 0, w.Length);

                    float n = (float)L.Fbm(x, z, 60, 2, 131);
                    float rock = Mathf.Max(S(0.55f, 0.9f, slope + n * 0.15f), S(470f, 640f, h + n * 40f) * 0.8f);
                    float snow = S(640f, 700f, h + n * 50f) * (1f - S(0.9f, 1.3f, slope));
                    // Talboden: Waldboden und Gras, Kiesbett am Bach
                    double dTal = L.Tal.Dist(x, z, out double arc);
                    float valley = 1f - S((float)L.Tal.AAt(arc) + 4f, (float)L.Tal.AAt(arc) + 25f, (float)dTal);
                    // Lichtung / Fussabdruck
                    double br = L.BasinR(x, z);
                    float moor = 0f, meadow = 0f, rimRock = 0f;
                    if (br < 1.05)
                    {
                        double sdf = L.FootSdf(x, z, out _, out double u, out _);
                        float inb = 1f - S(0.9f, 1.05f, (float)br);
                        meadow = inb * (sdf < 0 ? 1.4f : 0.7f);
                        moor = inb * (sdf < 0 && u < 60 ? 1.6f : 0f) + inb * (sdf < -2 && u > 255 ? 1.2f : 0f);
                        rimRock = inb * (sdf > 3 && sdf < 16 ? S(0.12f, 0.35f, slope) : 0f);
                    }
                    // Brandspuren an der alten Grenzbefestigung (Militaerspuren nach Osten, Design §1)
                    float ash = (1f - S(40f, 90f, Vector2.Distance(new Vector2(x, z), new Vector2(2250f, 385f)))) * 0.8f;
                    float path = 0f;
                    foreach (L.Polyline p in Routes)
                    {
                        double d = p.Dist(x, z, out _);
                        path = Mathf.Max(path, 1f - S(p == L.Tal ? 2.2f : 1.6f, p == L.Tal ? 4.2f : 3.2f, (float)d));
                    }
                    if (L.River != null)
                    {
                        double dr = L.River.Dist(x, z, out _);
                        w[LSand] = (1f - S(2.5f, 5f, (float)dr)) * 2f;
                    }

                    w[LGrass] = Mathf.Max(0.05f, 1f - rock) * (1f + meadow) + valley * 0.4f;
                    w[LForest] = valley * (1f - rock) * 0.9f * S(-0.1f, 0.2f, n);
                    w[LMoor] = moor;
                    w[LAsh] = ash;
                    w[LRock] = rock * 2.2f + rimRock * 2f;
                    w[LSnow] = snow * 3f;
                    w[LPath] = path * 4f;
                    float sum = 0f;
                    for (int l = 0; l < layers; l++) sum += w[l];
                    for (int l = 0; l < layers; l++) map[iz, ix, l] = w[l] / sum;
                }
            });
            data.SetAlphamaps(0, 0, ToWorldGrid(map));
        }

        // ------------------------------------------------------------------ Baeume (Groessenreferenz im Tal, §44)

        private static readonly (string model, float height)[] PineModels = { ("SM_Pine_A", 12f), ("SM_Pine_B", 14f), ("SM_Pine_C", 10f) };

        private static int PlaceTrees(Transform parent, Tile t, float[,] world)
        {
            var rng = new System.Random(t.Name.GetHashCode());
            float hcell = L.TileSize / (HeightRes - 1);
            int placed = 0;
            for (int attempt = 0; attempt < 30000 && placed < 520; attempt++)
            {
                float x = t.Origin.x + 10f + (float)rng.NextDouble() * (L.TileSize - 20f);
                float z = t.Origin.y + 10f + (float)rng.NextDouble() * (L.TileSize - 20f);
                int ix = Mathf.Clamp(Mathf.RoundToInt((x - t.Origin.x) / hcell), 1, HeightRes - 2);
                int iz = Mathf.Clamp(Mathf.RoundToInt((z - t.Origin.y) / hcell), 1, HeightRes - 2);
                float h = world[iz, ix];
                float slope = Mathf.Sqrt(Mathf.Pow((world[iz, ix + 1] - world[iz, ix - 1]) / (2f * hcell), 2f) + Mathf.Pow((world[iz + 1, ix] - world[iz - 1, ix]) / (2f * hcell), 2f));
                if (slope > 0.5f || h > 560f) continue;
                double dTal = L.Tal.Dist(x, z, out double arc);
                double hw = L.Tal.AAt(arc);
                // Dichte: Talboden (nicht auf dem Weg), Raender der Lichtung, einzelne Kiefern am Pass
                float want = 0f;
                if (dTal > 6 && dTal < hw + 60) want = 0.7f;
                double br = L.BasinR(x, z);
                if (br < 1.0) want = br > 0.82 ? 0.35f : 0.02f;   // die Lichtung bleibt offen – nur am Rand Baeume
                if (L.PlateauMask(x, z, out _) > 0.01) want = 0f;
                if (want <= 0f) want = h < 420f ? 0.08f : 0.03f;
                bool nearRoute = false;
                foreach (L.Polyline p in Routes)
                    if (p.Dist(x, z, out _) < 6.5) nearRoute = true;
                if (nearRoute || (L.River != null && L.River.Dist(x, z, out _) < 5)) continue;
                if (rng.NextDouble() > want) continue;
                float height = 9f + (float)rng.NextDouble() * 9f;
                Pine(parent, new Vector3(x, h - 0.2f, z), height);
                placed++;
            }
            return placed;
        }

        private static void Pine(Transform parent, Vector3 basePos, float height)
        {
            int variant = Mathf.Abs(Mathf.RoundToInt(basePos.x * 7.3f + basePos.z * 3.1f)) % PineModels.Length;
            (string model, float mh) = PineModels[variant];
            if (ArtModels.Has(model))
            {
                GameObject tree = ArtModels.Place(model, parent, basePos, (basePos.x * 13f + basePos.z * 7f) % 360f, height / mh, ArtModels.ColliderKind.Trunk, false);
                if (tree != null) tree.name = "Kiefer";
                return;
            }
            Transform tr = K.Group("Kiefer", parent, basePos, Vector3.zero);
            K.Prim(PrimitiveType.Cylinder, "Stamm", tr, new Vector3(0f, height * 0.25f, 0f), new Vector3(0.5f, height * 0.25f, 0.5f), Vector3.zero, _woodDark);
            K.Prim(PrimitiveType.Capsule, "Krone", tr, new Vector3(0f, height * 0.62f, 0f), new Vector3(height * 0.35f, height * 0.4f, height * 0.35f), Vector3.zero,
                K.Mat("Pine_Needles", new Color(0.13f, 0.21f, 0.14f)), false);
        }

        // ================================================================== Materialien

        private static void CreateMaterials()
        {
            _stone = K.Mat("GG_Stein", new Color(0.42f, 0.41f, 0.4f));
            _stoneDark = K.Mat("GG_Stein_Dunkel", new Color(0.26f, 0.25f, 0.25f));
            _wood = K.Mat("GG_Holz", new Color(0.3f, 0.21f, 0.14f));
            _woodDark = K.Mat("GG_Holz_Dunkel", new Color(0.19f, 0.13f, 0.09f));
            _roof = K.Mat("GG_Dach", new Color(0.17f, 0.15f, 0.15f));
            _canvas = K.Mat("GG_Plane", new Color(0.55f, 0.5f, 0.4f));
            _warm = K.Mat("GG_Fenster_Warm", new Color(1f, 0.72f, 0.4f), 0.2f, new Color(1f, 0.55f, 0.2f) * 2.5f);
            _fire = K.Mat("GG_Glut", new Color(1f, 0.45f, 0.15f), 0.2f, new Color(1f, 0.4f, 0.1f) * 3f);
            _water = K.Mat("GG_Wasser", new Color(0.12f, 0.2f, 0.24f), 0.85f);
            _bone = K.Mat("GG_Knochen", new Color(0.78f, 0.75f, 0.66f));
            _metal = K.Mat("GG_Metall", new Color(0.3f, 0.31f, 0.33f), 0.4f);
            _figure = K.Mat("GG_Figur", new Color(0.55f, 0.45f, 0.36f));
        }

        // ================================================================== Orte West (Tunnel bis Talsattel, Plateau)

        private static Vector3 G(float x, float z, float lift = 0f) => L.OnGround(x, z, lift);

        private static void BuildWestPlaces(Transform root)
        {
            BuildTunnelMouth(K.Group("Tunnelausgang", root));
            BuildCamp(K.Group("Lagerplatz am Serpentinenfuss", root), L.TalNode(2) + new Vector3(18f, 0f, -14f), 3, "Lagerplatz", "Rast vor dem Aufstieg");
            BuildSchenke(K.Group("Klippenschenke", root));
            BuildLichtung(K.Group("Lichtung", root));
            BuildSinkhole(K.Group("Schluckloch", root));
            Label(root, L.TalNode(3) + Vector3.up * 6f, "Felsspalte", "Talpass", 140f);
            Label(root, L.TalNode(1) + new Vector3(0f, 5f, 0f), "Abzweig Gebirgspass", "Talpass · Gebirgspass", 90f);
        }

        private static void BuildTunnelMouth(Transform root)
        {
            Vector3 p = G(L.TunnelEastX + 8f, L.TunnelZ);
            K.Block("Wegstein", root, p + new Vector3(4f, -0.2f, 6f), new Vector3(0.8f, 1.6f, 0.5f), _stone);
            Label(root, p + new Vector3(4f, 3f, 6f), "Talpass", "Grenzgebirge · Stufe 16–22", 100f);
            // Kleines Wachhaus: Groessenreferenz direkt am Ausgang
            Vector3 hut = G(L.TunnelEastX + 26f, L.TunnelZ + 20f);
            Hut(root, hut, 200f, "Zollhaeuschen (verlassen)");
        }

        private static void BuildCamp(Transform root, Vector3 c, int people, string title, string subtitle)
        {
            c = G(c.x, c.z);
            K.Prim(PrimitiveType.Cylinder, "Feuerstelle", root, c + new Vector3(0f, 0.1f, 0f), new Vector3(1.6f, 0.1f, 1.6f), Vector3.zero, _stoneDark);
            K.Prim(PrimitiveType.Sphere, "Glut", root, c + new Vector3(0f, 0.2f, 0f), new Vector3(1f, 0.2f, 1f), Vector3.zero, _fire, false);
            K.PointLight("Feuer", root, c + new Vector3(0f, 1f, 0f), new Color(1f, 0.55f, 0.25f), 12f, 2.2f, true);
            K.Particles("Rauch", root, c + new Vector3(0f, 0.5f, 0f), new Color(0.35f, 0.33f, 0.32f, 0.45f), 1.2f, 4f, 1.2f, 8f, 10f, 0.4f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 tp = G(c.x + (i == 0 ? -6f : 6f), c.z + 5f);
                K.Box("Zelt", root, tp + new Vector3(0f, 1f, 0f), new Vector3(3f, 2f, 3.5f), _canvas, new Vector3(0f, i * 30f, 45f));
            }
            for (int i = 0; i < people; i++)
            {
                float a = i * 2.1f;
                Figure(root, G(c.x + Mathf.Cos(a) * 3f, c.z + Mathf.Sin(a) * 3f), "Reisender");
            }
            Label(root, c + Vector3.up * 4f, title, subtitle, 70f);
        }

        /// <summary>Menschliche Groessenreferenz (1,8 m).</summary>
        private static void Figure(Transform root, Vector3 foot, string name)
        {
            K.Prim(PrimitiveType.Capsule, name, root, foot + new Vector3(0f, 0.9f, 0f), new Vector3(0.55f, 0.9f, 0.55f), Vector3.zero, _figure, false);
        }

        private static void Hut(Transform root, Vector3 p, float yaw, string title)
        {
            Transform h = K.Group(title, root, p, new Vector3(0f, yaw, 0f));
            K.Block("Waende", h, new Vector3(0f, -0.5f, 0f), new Vector3(6f, 3.5f, 5f), _wood);
            K.Box("Dach", h, new Vector3(0f, 3.6f, 0f), new Vector3(6.8f, 0.4f, 4.2f), _roof, new Vector3(0f, 0f, 0f));
            K.Box("Dach Giebel", h, new Vector3(0f, 4.2f, 0f), new Vector3(6.8f, 2.4f, 2.4f), _roof, new Vector3(45f, 0f, 0f));
            K.Box("Tuer", h, new Vector3(0f, 1f, -2.53f), new Vector3(1.1f, 2f, 0.08f), _woodDark, default, false);
            K.Box("Fenster", h, new Vector3(1.9f, 1.6f, -2.53f), new Vector3(0.8f, 0.7f, 0.06f), _warm, default, false);
        }

        // ------------------------------------------------------------------ Klippenschenke (§11–30, modular §46)

        private static void BuildSchenke(Transform root)
        {
            Vector2 c2 = L.SchenkeCenter;
            float floorY = L.PlateauHeight + 0.4f;
            // Blickrichtung Terrasse = Peilung zur Lichtung (137 Grad); lokales +z zeigt dorthin
            Transform s = K.Group("Die letzte Schwelle", root, new Vector3(c2.x, floorY, c2.y), new Vector3(0f, L.SchenkeBearing, 0f));
            const float w = 16f, d = 11f, wall = 4f;

            Transform m = K.Group("Fundament", s);
            K.Box("Sockel", m, new Vector3(0f, -1.2f, 0f), new Vector3(w + 1.2f, 2.8f, d + 1.2f), _stoneDark);

            Transform stone = K.Group("Steinwand", s);
            K.Box("Rueckwand", stone, new Vector3(0f, wall * 0.5f, -d * 0.5f), new Vector3(w, wall, 0.7f), _stone);
            K.Box("Seite West", stone, new Vector3(-w * 0.5f, wall * 0.5f, 0f), new Vector3(0.7f, wall, d), _stone);
            K.Box("Seite Ost", stone, new Vector3(w * 0.5f, wall * 0.5f, 0f), new Vector3(0.7f, wall, d), _stone);
            // Front (zur Terrasse) mit Tuerluecke in der Mitte
            K.Box("Front links", stone, new Vector3(-(w * 0.25f + 0.75f), wall * 0.5f, d * 0.5f), new Vector3(w * 0.5f - 1.5f, wall, 0.7f), _stone);
            K.Box("Front rechts", stone, new Vector3(w * 0.25f + 0.75f, wall * 0.5f, d * 0.5f), new Vector3(w * 0.5f - 1.5f, wall, 0.7f), _stone);
            K.Box("Front ueber Tuer", stone, new Vector3(0f, wall - 0.6f, d * 0.5f), new Vector3(3f, 1.2f, 0.7f), _stone);
            // Eingang auf der Rueckseite (vom Weg her)
            K.Box("Rueckwand Tuerluecke", stone, new Vector3(0f, 1.3f, -d * 0.5f), new Vector3(2.2f, 2.6f, 0.75f), _woodDark, default, false);

            Transform timber = K.Group("Holzwand", s);
            K.Box("Obergeschoss", timber, new Vector3(0f, wall + 1.6f, -0.5f), new Vector3(w - 1f, 3.2f, d - 2f), _wood);
            for (int i = -3; i <= 3; i++)
                K.Box("Balken", timber, new Vector3(i * 2.4f, wall + 1.6f, (d - 2f) * 0.5f - 0.45f), new Vector3(0.35f, 3.3f, 0.35f), _woodDark);

            Transform roof = K.Group("Dach", s);
            K.Box("Dach Nord", roof, new Vector3(0f, wall + 4.3f, -2.4f), new Vector3(w + 1.5f, 0.5f, 6.4f), _roof, new Vector3(-28f, 0f, 0f));
            K.Box("Dach Sued", roof, new Vector3(0f, wall + 4.3f, 1.4f), new Vector3(w + 1.5f, 0.5f, 6.4f), _roof, new Vector3(28f, 0f, 0f));

            Transform win = K.Group("Fenster", s);
            foreach (float x in new[] { -5.5f, -2.8f, 2.8f, 5.5f })
                K.Box("Fenster", win, new Vector3(x, 2f, d * 0.5f + 0.36f), new Vector3(1.2f, 1.1f, 0.06f), _warm, default, false);
            foreach (float x in new[] { -4f, 0f, 4f })
                K.Box("Fenster oben", win, new Vector3(x, wall + 1.8f, (d - 2f) * 0.5f - 0.2f), new Vector3(1f, 0.9f, 0.06f), _warm, default, false);

            Transform door = K.Group("Tuer", s);
            K.Box("Tuerrahmen", door, new Vector3(0f, 1.4f, d * 0.5f + 0.1f), new Vector3(2.6f, 2.9f, 0.3f), _woodDark, default, false);
            K.PointLight("Laterne an der Tuer", door, new Vector3(1.8f, 2.6f, d * 0.5f + 0.9f), new Color(1f, 0.64f, 0.3f), 10f, 2f, true);

            Transform chimney = K.Group("Kamin", s);
            K.Box("Schornstein", chimney, new Vector3(-w * 0.5f + 1.5f, wall + 3.5f, -d * 0.5f + 1.2f), new Vector3(1.6f, 7f, 1.6f), _stoneDark);
            K.Particles("Rauchfahne", chimney, new Vector3(-w * 0.5f + 1.5f, wall + 7.2f, -d * 0.5f + 1.2f), new Color(0.55f, 0.53f, 0.52f, 0.5f), 3.5f, 5f, 2.2f, 16f, 8f, 0.6f);
            K.Box("Feuerstelle", chimney, new Vector3(-w * 0.5f + 1.5f, 0.6f, -d * 0.5f + 1.1f), new Vector3(2.4f, 1.2f, 1.2f), _stoneDark);
            K.Box("Glut", chimney, new Vector3(-w * 0.5f + 1.5f, 0.5f, -d * 0.5f + 1.75f), new Vector3(1.2f, 0.3f, 0.3f), _fire, default, false);
            K.PointLight("Kaminfeuer", chimney, new Vector3(-w * 0.5f + 1.6f, 1.2f, -d * 0.5f + 2.6f), new Color(1f, 0.5f, 0.2f), 11f, 2.6f, true, true);

            // Innen: warm (Design §17 Kaelte -> Waerme)
            Transform inner = K.Group("Innen", s);
            K.Box("Theke", inner, new Vector3(4.5f, 0.55f, -3.2f), new Vector3(5f, 1.1f, 0.9f), _woodDark);
            for (int i = 0; i < 4; i++)
            {
                Vector3 tp = new Vector3(-4f + (i % 2) * 4.5f, 0.4f, 0.8f + (i / 2) * 2.6f);
                K.Box("Tisch", inner, tp, new Vector3(1.8f, 0.8f, 1.1f), _wood);
            }
            K.PointLight("Stube", inner, new Vector3(1f, 3f, 0f), new Color(1f, 0.62f, 0.32f), 12f, 1.8f, true);
            Figure(root, new Vector3(c2.x, floorY, c2.y) + Quaternion.Euler(0f, L.SchenkeBearing, 0f) * new Vector3(4.5f, 0f, -4.3f), "Wirt (Platzhalter)");

            Transform balcony = K.Group("Balkon", s);
            K.Box("Balkonboden", balcony, new Vector3(3f, wall + 0.1f, d * 0.5f + 1f), new Vector3(6f, 0.25f, 2f), _wood);

            Transform rail = K.Group("Gelaender", s);
            Transform terrace = K.Group("Terrasse", s);
            // Holzterrasse ueber der Klippenkante auf Pfosten
            const float tz0 = d * 0.5f + 0.5f, tDepth = 20f;   // bis ueber die Klippenkante (r ≈ 86)
            K.Box("Terrassenboden", terrace, new Vector3(0f, -0.05f, tz0 + tDepth * 0.5f), new Vector3(w + 2f, 0.3f, tDepth), _wood);
            for (int i = -2; i <= 2; i++)
                K.Box("Pfosten", terrace, new Vector3(i * 4.2f, -12f, tz0 + tDepth - 0.5f), new Vector3(0.45f, 24f, 0.45f), _woodDark);
            K.Box("Gelaender vorn", rail, new Vector3(0f, 0.95f, tz0 + tDepth - 0.1f), new Vector3(w + 2f, 0.12f, 0.12f), _woodDark);
            K.Box("Gelaender West", rail, new Vector3(-(w * 0.5f + 1f), 0.95f, tz0 + tDepth * 0.5f), new Vector3(0.12f, 0.12f, tDepth), _woodDark);
            K.Box("Gelaender Ost", rail, new Vector3(w * 0.5f + 1f, 0.95f, tz0 + tDepth * 0.5f), new Vector3(0.12f, 0.12f, tDepth), _woodDark);
            for (float x = -w * 0.5f - 1f; x <= w * 0.5f + 1.01f; x += 2f)
                K.Box("Pfosten Gelaender", rail, new Vector3(x, 0.5f, tz0 + tDepth - 0.1f), new Vector3(0.12f, 1f, 0.12f), _woodDark);
            for (int i = 0; i < 3; i++)
                K.Box("Terrassentisch", terrace, new Vector3(-5f + i * 5f, 0.5f, tz0 + 4f), new Vector3(1.6f, 0.8f, 1f), _wood);
            K.PointLight("Terrassenlaterne", terrace, new Vector3(-w * 0.5f, 2.2f, tz0 + tDepth - 0.4f), new Color(1f, 0.64f, 0.3f), 9f, 1.6f, true);

            Transform stairs = K.Group("Treppe", s);
            for (int i = 0; i < 6; i++)
                K.Box("Stufe", stairs, new Vector3(w * 0.5f + 2.5f, -0.25f - i * 0.35f, tz0 + 2f + i * 0.6f), new Vector3(2f, 0.3f, 0.6f), _stone);

            Transform annex = K.Group("Anbau", s);
            K.Block("Stall", annex, new Vector3(-w * 0.5f - 3.5f, -0.3f, -2f), new Vector3(5f, 3f, 6f), _wood);
            K.Box("Stalldach", annex, new Vector3(-w * 0.5f - 3.5f, 2.9f, -2f), new Vector3(5.6f, 0.35f, 6.6f), _roof, new Vector3(0f, 0f, 12f));

            // Aussichtsfelsen einige Meter daneben (§30)
            Vector2 look = L.FromPlateau(L.SchenkeBearing + 22f, 84f);
            K.Block("Aussichtsfelsen", root, new Vector3(look.x, L.PlateauHeight - 16f, look.y), new Vector3(7f, 16.4f, 7f), _stone, L.SchenkeBearing + 22f);
            // Rueckblick (Sichttest 4): Felsnase am Suedwestrand – Talpass, Tunnel und Kareth liegen darunter
            Vector2 back = L.FromPlateau(225f, 88f);
            K.Block("Rueckblick-Felsen", root, new Vector3(back.x, L.PlateauHeight - 14f, back.y), new Vector3(6f, 14.3f, 6f), _stone, 225f);

            // Weg vom Plateau-Pfad zur Tuer (Seitenanstieg, §27)
            Label(root, new Vector3(c2.x, floorY + 11f, c2.y), "Die letzte Schwelle", "Klippenschenke", 160f);
            for (int i = 0; i < 3; i++)
                Figure(root, new Vector3(c2.x, floorY, c2.y) + Quaternion.Euler(0f, L.SchenkeBearing, 0f) * new Vector3(-5f + i * 5f, 0f, tz0 + 5.2f), "Gast");
            GameObjectUtility.SetStaticEditorFlags(s.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        // ------------------------------------------------------------------ Lichtung (§5–9, §35)

        private static Vector3 FootPoint(float u, float v)
        {
            // Umkehrung von FootLocal ohne Verzerrung (fuer Platzierung genau genug)
            const float heelU0 = -330f / 2f + 58f;
            float a = u + heelU0;
            return G(L.FootCenter.x + a * (float)L.FootAx + v * (float)L.FootBx, L.FootCenter.y + a * (float)L.FootAz + v * (float)L.FootBz);
        }

        private static void BuildLichtung(Transform root)
        {
            // Zehen: kleine Teiche (von unten nur "regelmaessig angeordnete Wasserloecher")
            (float u, float v, float r)[] toes = { (292f, -54f, 30f), (306f, -3f, 21f), (301f, 35f, 18f), (289f, 64f, 16f), (272f, 88f, 14f) };
            foreach (var (u, v, r) in toes)
            {
                Vector3 p = FootPoint(u, v);
                float level = p.y + 1.2f;
                K.Prim(PrimitiveType.Cylinder, "Teich", root, new Vector3(p.x, level, p.z), new Vector3(r * 1.3f, 0.02f, r * 1.3f), Vector3.zero, _water, false);
            }
            // Ferse: tiefer, feuchter Grund – flache Tuempel
            Vector3 heel = FootPoint(0f, 0f);
            K.Prim(PrimitiveType.Cylinder, "Fersentuempel", root, heel + new Vector3(0f, 2.2f, 0f), new Vector3(46f, 0.02f, 36f), new Vector3(0f, 45f + 180f, 0f), _water, false);
            // Alte Tierknochen (Foreshadowing §9)
            var rng = new System.Random(52);
            for (int i = 0; i < 7; i++)
            {
                Vector3 b = FootPoint(20f + (float)rng.NextDouble() * 60f, -30f + (float)rng.NextDouble() * 60f);
                K.Prim(PrimitiveType.Capsule, "Knochen", root, b + new Vector3(0f, 0.2f, 0f), new Vector3(0.35f, 1.1f + (float)rng.NextDouble(), 0.35f),
                    new Vector3(90f, (float)rng.NextDouble() * 180f, 0f), _bone, false);
            }
            // Rippenbogen eines grossen (aber normalen) Tiers
            Vector3 rib = FootPoint(70f, 20f);
            for (int i = 0; i < 5; i++)
                K.Box("Rippe", root, rib + new Vector3(i * 1.1f, 1.2f, 0f), new Vector3(0.25f, 2.6f, 0.25f), _bone, new Vector3(0f, 0f, 18f), false);
            // Menschen leben hier, ohne zu wissen, was die Form bedeutet (§35): Hof auf dem Ballen
            Vector3 farm = FootPoint(200f, 30f);
            Hut(root, farm, 60f, "Hof auf der Lichtung");
            Figure(root, farm + new Vector3(6f, 0f, -3f), "Baeuerin");
            Figure(root, farm + new Vector3(-8f, 0f, 4f), "Knecht");
            for (int i = 0; i < 6; i++)
            {
                Vector3 fp = FootPoint(170f + i * 6f, 45f);
                K.Box("Zaun", root, fp + new Vector3(0f, 0.6f, 0f), new Vector3(0.15f, 1.2f, 6f), _woodDark, new Vector3(0f, 45f, 0f));
            }
            // Name bewusst unverdaechtig (§9: niemand sagt "Fussabdruck")
            Label(root, FootPoint(150f, 0f) + Vector3.up * 8f, "Die Lichtung", "Grenzgebirge", 160f);
        }

        private static void BuildSinkhole(Transform root)
        {
            Vector3 p = L.River != null ? L.River.PointAt(L.River.Length) : G(728f, 118f);
            p = G(p.x, p.z);
            K.Prim(PrimitiveType.Cylinder, "Schluckloch", root, p + new Vector3(0f, -0.3f, 0f), new Vector3(5f, 0.05f, 5f), Vector3.zero, K.Mat("GG_Schwarz", new Color(0.02f, 0.02f, 0.02f)), false);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                K.Block("Randfels", root, p + new Vector3(Mathf.Cos(a) * 3.6f, -0.6f, Mathf.Sin(a) * 3.6f), new Vector3(1.8f, 1.4f, 1.6f), _stone, i * 37f);
            }
        }

        // ================================================================== Orte Ost (Talsattel bis Bruchmark)

        private static void BuildEastPlaces(Transform root)
        {
            // Wachturmruine am Hochpass (Route A: Ruinen, staerkere Gegner)
            Vector3 hp = G(L.HighPass.x, L.HighPass.y);
            Transform ruin = K.Group("Wachturmruine am Hochpass", root, hp + new Vector3(14f, 0f, 12f), Vector3.zero);
            K.Prim(PrimitiveType.Cylinder, "Turmstumpf", ruin, new Vector3(0f, 5f, 0f), new Vector3(8f, 6f, 8f), Vector3.zero, _stone);
            K.Block("Mauerrest", ruin, new Vector3(-9f, -0.5f, 2f), new Vector3(10f, 3f, 1.2f), _stone, 20f);
            K.Block("Mauerrest", ruin, new Vector3(6f, -0.5f, -8f), new Vector3(7f, 2f, 1.2f), _stone, -35f);
            Label(ruin, new Vector3(0f, 14f, 0f), "Wachturmruine", "Hochpass", 180f);

            // Talsattel: Rastplatz
            BuildCamp(K.Group("Rast am Talsattel", root), L.TalNode(11) + new Vector3(12f, 0f, 16f), 2, "Talsattel", "Grenzgebirge");

            // Alte Grenzbefestigung (Militaerspuren nehmen nach Osten zu, §1)
            Vector3 f = L.TalNode(13);
            Transform fort = K.Group("Alte Grenzfeste", root, G(f.x, f.z + 34f), new Vector3(0f, -15f, 0f));
            K.Block("Mauer Nord", fort, new Vector3(0f, -1f, 18f), new Vector3(40f, 6f, 2f), _stoneDark);
            K.Block("Mauer West", fort, new Vector3(-20f, -1f, 6f), new Vector3(2f, 5f, 24f), _stoneDark);
            K.Block("Mauer Ost (eingestuerzt)", fort, new Vector3(20f, -1f, 10f), new Vector3(2f, 2.5f, 14f), _stoneDark);
            K.Prim(PrimitiveType.Cylinder, "Eckturm", fort, new Vector3(-20f, 4f, 18f), new Vector3(6f, 6f, 6f), Vector3.zero, _stoneDark);
            K.Box("Umgestuerzter Wagen", fort, new Vector3(6f, 0.8f, -4f), new Vector3(2.2f, 1.2f, 4f), _woodDark, new Vector3(0f, 30f, 80f));
            Label(fort, new Vector3(0f, 9f, 6f), "Alte Grenzfeste", "verlassen", 160f);

            // Verlassenes Lager an T14
            Vector3 t14 = L.TalNode(14);
            Transform lager = K.Group("Verlassenes Soldatenlager", root, G(t14.x + 20f, t14.z - 30f), Vector3.zero);
            for (int i = 0; i < 4; i++)
                K.Box("Zeltgerippe", lager, new Vector3(-9f + i * 6f, 1f, 0f), new Vector3(0.2f, 2f, 3.5f), _woodDark, new Vector3(0f, 0f, 35f));
            K.Prim(PrimitiveType.Cylinder, "Kalte Feuerstelle", lager, new Vector3(0f, 0.1f, 5f), new Vector3(1.6f, 0.1f, 1.6f), Vector3.zero, _stoneDark);
            Label(lager, new Vector3(0f, 4f, 0f), "Verlassenes Lager", "", 90f);

            // Ausgaenge in die Bruchmark: Felssturz (die Bruchmark ist noch nicht gebaut)
            Rockfall(root, L.ExitSouth, "Felssturz", "Weg in die Bruchmark verschuettet");
            Rockfall(root, L.ExitPass, "Felssturz am Pass", "Weg in die Bruchmark verschuettet");
        }

        private static void Rockfall(Transform root, Vector2 at, string title, string subtitle)
        {
            Vector3 p = G(at.x, at.y);
            Transform r = K.Group(title, root, p, Vector3.zero);
            var rng = new System.Random((int)at.y);
            for (int i = 0; i < 16; i++)
            {
                float s = 6f + (float)rng.NextDouble() * 12f;
                Vector3 o = new Vector3(20f + (float)rng.NextDouble() * 30f, -2f + (float)rng.NextDouble() * 10f, -30f + (float)rng.NextDouble() * 60f);
                if (ArtModels.Has("SM_Rock_A"))
                    ArtModels.Place(i % 2 == 0 ? "SM_Rock_A" : "SM_Rock_B", r, o, (float)rng.NextDouble() * 360f, s * 0.5f, ArtModels.ColliderKind.Mesh);
                else
                    K.Box("Felsblock", r, o, new Vector3(s, s * 0.8f, s), _stone, new Vector3((float)rng.NextDouble() * 40f, (float)rng.NextDouble() * 90f, 0f));
            }
            // Sperre (unsichtbar): Felsbloecke koennen Luecken lassen
            var block = new GameObject("Sperre");
            block.transform.SetParent(r, false);
            block.transform.localPosition = new Vector3(30f, 20f, 0f);
            block.AddComponent<BoxCollider>().size = new Vector3(20f, 60f, 90f);
            Label(r, new Vector3(0f, 6f, 0f), title, subtitle, 140f);
        }

        // ================================================================== Bach (§4)

        private static void BuildRiver(Transform root, float x0, float x1)
        {
            if (L.River == null) return;
            // Wasserflaeche in Stuecken entlang des Bachbetts
            const float step = 6f;
            for (float s = 0f; s < L.River.Length - 0.5f; s += step)
            {
                Vector3 a = L.River.PointAt(s), b = L.River.PointAt(Mathf.Min((float)L.River.Length, s + step));
                if (a.x < x0 || a.x >= x1) continue;
                Vector3 mid = (a + b) * 0.5f;
                float y = (float)L.RiverBedAt(s + step * 0.5f) + 0.55f;
                Vector3 dir = b - a;
                dir.y = 0f;
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                K.Box("Wasser", root, new Vector3(mid.x, y, mid.z), new Vector3(2.6f, 0.04f, dir.magnitude + 0.6f), _water, new Vector3(0f, yaw, 0f), false);
            }
            // Bruecken, wo der Talweg den Bach quert
            foreach (Vector3 c in L.RiverCrossings())
            {
                if (c.x < x0 || c.x >= x1) continue;
                L.Tal.Dist(c.x, c.z, out double arc);
                Vector3 dir = L.Tal.DirAt(arc);
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                float y = (float)L.Tal.HeightAt(arc);   // Weghoehe (die Rinne liegt darunter)
                Transform br = K.Group("Bruecke", root, new Vector3(c.x, y, c.z), new Vector3(0f, yaw, 0f));
                K.Box("Bohlen", br, new Vector3(0f, 0.35f, 0f), new Vector3(3.6f, 0.25f, 8f), _wood);
                K.Box("Gelaender", br, new Vector3(-1.75f, 1.2f, 0f), new Vector3(0.12f, 0.12f, 8f), _woodDark);
                K.Box("Gelaender", br, new Vector3(1.75f, 1.2f, 0f), new Vector3(0.12f, 0.12f, 8f), _woodDark);
            }
            // Wasserfall aus der Plateauwand in die Ferse (von unten gut sichtbar, "da oben will ich hin")
            if (x0 <= L.RiverSource.x && L.RiverSource.x < x1)
            {
                Vector3 top = L.WaterfallTop, foot = L.WaterfallFoot;
                Vector3 mid = (top + foot) * 0.5f;
                Vector3 d = top - foot;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                K.Box("Wasserfall", root, mid, new Vector3(3.5f, 0.3f, d.magnitude), _water, new Vector3(pitch, yaw, 0f), false);
                K.Particles("Gischt", root, foot + Vector3.up * 1f, new Color(0.85f, 0.9f, 0.95f, 0.35f), 3f, 8f, 1.5f, 3f, 40f, 3f);
            }
        }

        // ================================================================== Wegzeichen, Felsen

        private static void BuildWaymarks(Transform root, float x0, float x1)
        {
            // Steinmaennchen am Gebirgspass alle ~120 m, Grenzsteine im Osten
            foreach (L.Polyline p in new[] { L.RouteAWest, L.RouteAEast })
                for (double s = 60; s < p.Length; s += 120)
                {
                    Vector3 c = p.PointAt(s);
                    if (c.x < x0 || c.x >= x1) continue;
                    Vector3 side = Vector3.Cross(Vector3.up, p.DirAt(s)) * 4.5f;
                    Vector3 g = G(c.x + side.x, c.z + side.z);
                    bool border = c.x > 2000f;
                    K.Block(border ? "Grenzstein" : "Steinmaennchen", root, g + Vector3.down * 0.2f, border ? new Vector3(0.7f, 1.6f, 0.4f) : new Vector3(0.9f, 1.3f, 0.9f), _stone);
                }
        }

        private static void BuildRocks(Transform root, float x0, float x1)
        {
            string[] rocks = { "SM_Rock_A", "SM_Rock_B", "SM_Rock_C" };
            if (!ArtModels.Has(rocks[0])) return;
            var rng = new System.Random((int)x0);
            int placed = 0;
            for (int attempt = 0; attempt < 6000 && placed < 360; attempt++)
            {
                // Felsen entlang der Wege (Deckung, Groessenreferenz), dichter in der Felsspalte
                L.Polyline p = Routes[rng.Next(Routes.Length)];
                double s = rng.NextDouble() * p.Length;
                Vector3 c = p.PointAt(s);
                if (c.x < x0 + 5f || c.x >= x1 - 5f) continue;
                Vector3 side = Vector3.Cross(Vector3.up, p.DirAt(s));
                float off = (rng.NextDouble() < 0.5 ? -1f : 1f) * (8f + (float)rng.NextDouble() * 40f);
                float x = c.x + side.x * off, z = c.z + side.z * off;
                bool near = false;
                foreach (L.Polyline q in Routes)
                    if (q.Dist(x, z, out _) < 5.5) near = true;
                if (near || L.PlateauMask(x, z, out _) > 0.01 || (L.River != null && L.River.Dist(x, z, out _) < 4)) continue;
                float scale = 0.5f + (float)rng.NextDouble() * 2.6f;
                ArtModels.Place(rocks[rng.Next(rocks.Length)], root, G(x, z, -0.3f * scale), (float)rng.NextDouble() * 360f, scale,
                    scale < 0.8f ? ArtModels.ColliderKind.None : ArtModels.ColliderKind.Mesh, false);
                placed++;
            }
        }

        // ================================================================== Weltrand, Anker, Quest-Orte, Gebietsluft

        /// <summary>
        /// 30.09.: keine unsichtbaren Waende mehr – rund um den Kern liegt erzeugtes Gelaende (Terrain-Generator V1),
        /// der aeussere Weltrand kommt von GeneratedWorldPlacement. Die Felsstuerze an den Bruchmark-Ausgaengen bleiben.
        /// </summary>
        private static void BuildBoundary(Transform root, float x0, float x1, bool west) { }

        private static void BuildAnchors(Transform root, string cellId)
        {
            var list = cellId == WorldManifestBuilder.CellBorderWest
                ? new (string id, Vector2 p)[]
                {
                    ("gg.tunnel_ost", new Vector2(L.TunnelEastX + 12f, L.TunnelZ + 4f)),
                    ("gg.lagerplatz", new Vector2(L.TalNode(2).x + 10f, L.TalNode(2).z - 20f)),
                    ("gg.schenke", L.FromPlateau(300f, 30f)),
                    ("gg.lichtung", new Vector2(L.TalNode(6).x, L.TalNode(6).z)),
                    ("gg.pass_nord", new Vector2(1150f, 1105f)),
                }
                : new (string id, Vector2 p)[]
                {
                    ("gg.talsattel", new Vector2(L.TalNode(11).x, L.TalNode(11).z)),
                    ("gg.hochpass", L.HighPass),
                    ("gg.grenzfeste", new Vector2(L.TalNode(13).x, L.TalNode(13).z)),
                    ("gg.ausgang_sued", new Vector2(L.TalNode(15).x, L.TalNode(15).z)),
                    ("gg.ausgang_pass", new Vector2(2750f, 935f)),
                };
            foreach (var (id, p) in list)
            {
                var go = new GameObject("Anker " + id);
                go.transform.SetParent(root, false);
                // exakt auf den Weg setzen (Wege sind eben und begehbar)
                go.transform.localPosition = G(p.x, p.y, 0.1f);
                go.AddComponent<SafeSpawnAnchor>().Configure(id, cellId);
            }
        }

        /// <summary>
        /// Quest-Orte des Grenzgebirges (alle "geplant" – Inhalte folgen; Stufe 16–22 ist Vorschlag, Frage an Max).
        /// Gleiche Struktur wie KarethQuestSites, damit Karte, Pruefung und Tests sie finden.
        /// </summary>
        public static IEnumerable<(string id, string cell, string name, QuestSiteKind kind, int min, int max, Vector2 pos, float radius, string hub, string notes)> SiteTable()
        {
            string w = WorldManifestBuilder.CellBorderWest, e = WorldManifestBuilder.CellBorderEast;
            Vector2 sch = L.SchenkeCenter;
            yield return ("site.gg.schenke", w, "Die letzte Schwelle", QuestSiteKind.Hub, 16, 22, sch, 30f, "", "Stuetzpunkt: Wirt, Gerüchte, Kopfgelder – nicht ueberladen (§23)");
            yield return ("site.gg.lagerplatz", w, "Lagerplatz am Serpentinenfuss", QuestSiteKind.Hub, 16, 18, new Vector2(L.TalNode(2).x + 18f, L.TalNode(2).z - 14f), 20f, "", "kleiner Stuetzpunkt am Talpass");
            yield return ("site.gg.felsspalte", w, "Felsspalte", QuestSiteKind.Kill, 16, 18, new Vector2(978f, 300f), 45f, "site.gg.lagerplatz", "enge Stelle, Hinterhalte");
            yield return ("site.gg.lichtung", w, "Die Lichtung", QuestSiteKind.Collect, 17, 19, new Vector2(1280f, 540f), 120f, "site.gg.lagerplatz", "Kraeuter, Tiere, Hof – niemand spricht vom Fussabdruck (§9)");
            yield return ("site.gg.fussabdruck", w, "Blick von der Terrasse", QuestSiteKind.Hidden, 16, 22, L.FromPlateau(L.SchenkeBearing, 84f), 12f, "site.gg.schenke", "Erkenntnis ganz ohne Rueckmeldung (Max 28.09.: stumm – kein Popup, kein Eintrag, keine EP)");
            yield return ("site.gg.schluckloch", w, "Schluckloch", QuestSiteKind.Explore, 16, 18, new Vector2(728f, 118f), 15f, "site.gg.lagerplatz", "Bach verschwindet im Fels");
            yield return ("site.gg.tunnel", w, "Grenztunnel", QuestSiteKind.Transition, 14, 22, new Vector2(620f, L.TunnelZ), 20f, "", "Uebergang Kareth -> Grenzgebirge");
            yield return ("site.gg.hochpass", e, "Wachturmruine am Hochpass", QuestSiteKind.Elite, 19, 22, L.HighPass, 40f, "site.gg.schenke", "Route A: Ruinen, staerkere Gegner (§3)");
            yield return ("site.gg.talsattel", e, "Talsattel", QuestSiteKind.Explore, 18, 20, new Vector2(1832f, 531f), 25f, "site.gg.schenke", "Rastplatz, Wasserscheide");
            yield return ("site.gg.grenzfeste", e, "Alte Grenzfeste", QuestSiteKind.Story, 19, 21, new Vector2(2250f, 419f), 50f, "site.gg.schenke", "Kriegsreste, Militaerspuren (§1)");
            yield return ("site.gg.soldatenlager", e, "Verlassenes Soldatenlager", QuestSiteKind.Kill, 19, 21, new Vector2(2540f, 280f), 35f, "site.gg.schenke", "Deserteure?");
            yield return ("site.gg.ausgang_sued", e, "Felssturz (Bruchmark Sued)", QuestSiteKind.Transition, 20, 22, L.ExitSouth, 30f, "", "spaeter Weg in die Bruchmark");
            yield return ("site.gg.ausgang_pass", e, "Felssturz am Pass (Bruchmark)", QuestSiteKind.Transition, 20, 22, L.ExitPass, 30f, "", "spaeter Weg in die Bruchmark");
        }

        private static void BuildQuestSites(Transform root, string cellId)
        {
            foreach (var s in SiteTable())
            {
                if (s.cell != cellId) continue;
                var go = new GameObject("Ort " + s.name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = G(s.pos.x, s.pos.y);
                go.AddComponent<WorldQuestSite>().Configure(s.id, s.name, "grenzgebirge", s.hub, s.kind, QuestSiteStatus.Planned, s.min, s.max, s.radius, true, s.notes);
            }
        }

        private static void BuildBiomes(Transform root, float x0, float x1)
        {
            void V(string file, float x, float z, float r, float f)
            {
                if (x >= x0 && x < x1) SkyContentBuilder.PlaceVolume(root, file, root.TransformPoint(G(x, z)), r, f);
            }
            for (int i = 0; i < L.TalCount; i += 2)
            {
                Vector3 t = L.TalNode(i);
                V("Biome_Grenzgebirge_Tal", t.x, t.z, 150f, 90f);
            }
            V("Biome_Lichtung", L.FootCenter.x, L.FootCenter.y, 230f, 90f);
            V("Biome_Klippenschenke", L.PlateauCenter.x, L.PlateauCenter.y, 95f, 60f);
            for (double s = 250; s < L.RouteAEast.Length; s += 450)
            {
                Vector3 p = L.RouteAEast.PointAt(s);
                V("Biome_Grenzgebirge_Hoehe", p.x, p.z, 240f, 120f);
            }
        }

        // ================================================================== Gegner (Platzhalter-Arten, Stufe 16–22)

        private static Dictionary<string, EnemyBuilder.BorderEnemy> _enemies;

        /// <summary>
        /// Spawngebiete je Zelle – abseits der Hauptwege (20–40 m, geprueft im Prototyp), Talpass leicht (16–18), Gebirgspass
        /// und Osten schwerer (18–22), Elite am Hochpass. Der EnemyDirector erzeugt sie erst, wenn die Zelle bereit ist.
        /// </summary>
        private static void BuildEnemies(Transform root, float x0, float x1)
        {
            if (_enemies == null) return;
            EnemyBuilder.BorderEnemy wolf = _enemies[EnemyBuilder.Gratwolf], des = _enemies[EnemyBuilder.Deserteur],
                gun = _enemies[EnemyBuilder.DeserteurSchuetzin], spit = _enemies[EnemyBuilder.Spaltenspeier], brute = _enemies[EnemyBuilder.Felsschrat];
            var table = new (string name, float x, float z, EnemyBuilder.BorderEnemy e, int n, float r, string group, int lo, int hi)[]
            {
                ("Gratwoelfe Talhang", 776f, 116f, wolf, 3, 5f, "", 16, 17),
                ("Spaltenspeier Felsspalte", 977f, 310f, spit, 2, 4f, "", 16, 17),
                ("Gratwoelfe Lichtung Sued", 1330f, 430f, wolf, 3, 6f, "", 17, 18),
                ("Gratwoelfe Serpentinen", 704f, 320f, wolf, 2, 4f, "", 17, 18),
                ("Gratwoelfe Plateau Nord", 1102f, 1086f, wolf, 2, 4f, "", 18, 19),
                ("Gratwoelfe Talsattel", 1900f, 516f, wolf, 2, 4f, "", 18, 19),
                ("Deserteure Grenzfeste", 2250f, 420f, des, 3, 6f, "Alte Grenzfeste", 19, 21),
                ("Deserteurinnen Grenzfeste", 2262f, 432f, gun, 2, 3f, "Alte Grenzfeste", 19, 21),
                ("Deserteure Soldatenlager", 2560f, 280f, des, 2, 5f, "Verlassenes Soldatenlager", 19, 20),
                ("Deserteurin Soldatenlager", 2568f, 290f, gun, 1, 2f, "Verlassenes Soldatenlager", 19, 20),
                ("Felsschrat am Hochpass", 1785f, 1080f, brute, 1, 0f, "", 21, 22),
                ("Gratwoelfe Pass Ost", 2126f, 932f, wolf, 3, 5f, "", 20, 21),
            };
            foreach (var t in table)
            {
                if (t.x < x0 || t.x >= x1) continue;
                EnemyBuilder.PlaceArea(root, t.name, G(t.x, t.z), t.e, t.n, t.r, t.group, t.lo, t.hi);
            }
        }

        // ================================================================== NavMesh

        /// <summary>Verbindung Kareth-NavMesh (Tunnelroehre bis x = 658) -> Zellen-NavMesh am Ostportal.</summary>
        private static void BuildSeamLink(Transform root)
        {
            var go = new GameObject("NavMesh-Verbindung Tunnel");
            go.transform.SetParent(root, false);
            var link = go.AddComponent<NavMeshLink>();
            // Kareth-NavMesh reicht bis ans Ende der Roehre (x = 658), das Zellen-NavMesh beginnt auf dem Talboden
            link.startPoint = new Vector3(L.TunnelEastX - 4f, BorderTunnel.FloorAt(L.TunnelEastX - 4f), L.TunnelZ);
            link.endPoint = new Vector3(L.TunnelEastX + 8f, L.Height(L.TunnelEastX + 8f, L.TunnelZ), L.TunnelZ);
            link.width = L.TunnelWidth - 2f;
            link.bidirectional = true;
        }

        private static float BakeCellNavMesh(Transform ground, string cellId, float x0, float x1)
        {
            var go = new GameObject("NavMesh " + cellId + " (im Editor gebacken)");
            go.transform.SetParent(ground, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            // Volumen in Weltachsen: die Zelle (Entwurfsraum x0..x1, MinZ..MaxZ) ueber den Rahmen umgerechnet
            Vector2 a = BorderMountainsFrame.ToWorld(x0, L.MinZ), b = BorderMountainsFrame.ToWorld(x1, L.MaxZ);
            Rect w = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            surface.center = new Vector3(w.center.x, L.BaseY + L.HeightRange * 0.5f, w.center.y);
            surface.size = new Vector3(w.width, L.HeightRange, w.height);
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            int exclude = LayerMask.GetMask(GameLayers.Player, GameLayers.Enemy, GameLayers.Interactable, "Ignore Raycast", "UI");
            surface.layerMask = ~exclude;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.3f;
            surface.overrideTileSize = true;
            surface.tileSize = 256;
            var sw = Stopwatch.StartNew();
            // Kollider folgen verschobenen Objekten erst nach dem Abgleich – sonst backt das NavMesh die Terrains am Erzeugungsort
            Physics.SyncTransforms();
            surface.BuildNavMesh();
            string path = $"{WorldManifestBuilder.RegionFolder}/NavMesh_{cellId}.asset";
            AssetDatabase.DeleteAsset(path);
            if (surface.navMeshData != null)
            {
                AssetDatabase.CreateAsset(surface.navMeshData, path);
                EditorUtility.SetDirty(surface);
            }
            return (float)sw.Elapsed.TotalSeconds;
        }

        /// <summary>Diagnose: NavMesh-Wege durch den Tunnel in Abschnitten (Zelle gg_west muss offen sein).</summary>
        [MenuItem("Ashveil/Welt/Grenzgebirge: NavMesh im Tunnel pruefen", priority = 513)]
        public static void CheckTunnelNavMesh()
        {
            Scene cell = SceneManager.GetSceneByPath(WorldManifestBuilder.BorderWestScene);
            bool opened = false;
            if (!cell.IsValid() || !cell.isLoaded)
            {
                cell = EditorSceneManager.OpenScene(WorldManifestBuilder.BorderWestScene, OpenSceneMode.Additive);
                opened = true;
            }
            var sb = new System.Text.StringBuilder("[Ashveil] NavMesh-Diagnose Tunnel + Grenzgebirge-Kern:\n");
            foreach (var s in Object.FindObjectsByType<Unity.AI.Navigation.NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine($"  Flaeche '{s.name}' ({s.gameObject.scene.name}): aktiv {s.isActiveAndEnabled}, Daten {(s.navMeshData != null ? s.navMeshData.name : "KEINE")}, " +
                              $"pos {s.transform.position}, rot {s.transform.rotation.eulerAngles.y:0}");
            var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
            if (tri.vertices.Length > 0)
            {
                var bb = new Bounds(tri.vertices[0], Vector3.zero);
                foreach (Vector3 v in tri.vertices) bb.Encapsulate(v);
                sb.AppendLine($"  NavMesh gesamt: {tri.vertices.Length} Punkte, Grenzen {bb.min} .. {bb.max}");
                bool any = false; var gb = new Bounds();
                foreach (Vector3 v in tri.vertices)
                {
                    if (v.x < 1000f || v.x > 20000f) continue;
                    if (!any) { gb = new Bounds(v, Vector3.zero); any = true; } else gb.Encapsulate(v);
                }
                sb.AppendLine(any ? $"  NavMesh ostwaerts x>1000: Grenzen {gb.min} .. {gb.max}" : "  NavMesh ostwaerts x>1000: keins");
            }
            float Ground(float x, float z) => Physics.Raycast(new Vector3(x, 1500f, z), Vector3.down, out RaycastHit h, 3000f) ? h.point.y : float.NaN;
            float Floor(float x, float z) => Physics.Raycast(new Vector3(x, 30f, z), Vector3.down, out RaycastHit h, 60f) ? h.point.y : float.NaN;
            var pts = new List<(string, Vector3)>();
            float Tube(float x) => Physics.Raycast(new Vector3(x, BorderTunnel.FloorAt(Mathf.Clamp(x, L.TunnelWestX, L.TunnelEastX)) + 3f, L.TunnelZ), Vector3.down, out RaycastHit h, 8f) ? h.point.y : float.NaN;
            float[] tx = { 492f, 505f, 515f, 530f, 560f, 600f, 640f, 650f, 660f };
            foreach (float x in tx)
                pts.Add(($"Tunnel x {x:0}", new Vector3(x, x < 500f ? Ground(x, L.TunnelZ) : Tube(x), L.TunnelZ)));
            for (int i = 0; i + 1 < tx.Length; i++)
            {
                Vector3 pa = new Vector3(tx[i], tx[i] < 500f ? Ground(tx[i], L.TunnelZ) : Tube(tx[i]), L.TunnelZ);
                Vector3 pb = new Vector3(tx[i + 1], Tube(tx[i + 1]), L.TunnelZ);
                var path = new UnityEngine.AI.NavMeshPath();
                bool okA = UnityEngine.AI.NavMesh.SamplePosition(pa, out UnityEngine.AI.NavMeshHit ha, 2f, UnityEngine.AI.NavMesh.AllAreas);
                bool okB = UnityEngine.AI.NavMesh.SamplePosition(pb, out UnityEngine.AI.NavMeshHit hb, 2f, UnityEngine.AI.NavMesh.AllAreas);
                bool okP = okA && okB && UnityEngine.AI.NavMesh.CalculatePath(ha.position, hb.position, UnityEngine.AI.NavMesh.AllAreas, path);
                sb.AppendLine($"  Weg {tx[i]:0} -> {tx[i + 1]:0}: A {(okA ? ha.position.ToString() : "-")} B {(okB ? hb.position.ToString() : "-")} -> {(okP ? path.status.ToString() : "nicht berechenbar")}");
            }
            foreach (Vector2 d in new[] { new Vector2(680f, 62f), new Vector2(832f, 196f), new Vector2(1500f, 900f), new Vector2(2400f, 1500f) })
            {
                Vector2 w = BorderMountainsFrame.ToWorld(d.x, d.y);
                pts.Add(($"Kern {d.x:0}/{d.y:0} -> Welt {w.x:0}/{w.y:0}", new Vector3(w.x, Ground(w.x, w.y), w.y)));
            }
            foreach (var (n, pnt) in pts)
            {
                UnityEngine.AI.NavMeshHit hit = default;
                bool ok = !float.IsNaN(pnt.y) && UnityEngine.AI.NavMesh.SamplePosition(pnt + Vector3.up * 0.5f, out hit, 4f, UnityEngine.AI.NavMesh.AllAreas);
                string near = "";
                if (!ok && !float.IsNaN(pnt.y) && UnityEngine.AI.NavMesh.SamplePosition(pnt, out UnityEngine.AI.NavMeshHit far, 300f, UnityEngine.AI.NavMesh.AllAreas))
                    near = $" (naechstes NavMesh {far.position}, {Vector3.Distance(far.position, pnt):0} m)";
                string what = "";
                foreach (RaycastHit rh in Physics.RaycastAll(new Vector3(pnt.x, 1500f, pnt.z), Vector3.down, 3000f))
                    what += $" [{rh.collider.name} y {rh.point.y:0.0} ({rh.collider.gameObject.scene.name}, Ebene {LayerMask.LayerToName(rh.collider.gameObject.layer)})]";
                sb.AppendLine($"  {n}: Boden {pnt.y:0.0} -> {(ok ? $"NavMesh bei {hit.position}" : "KEIN NavMesh")}{near}\n     Treffer:{what}");
            }
            Debug.Log(sb.ToString());
            System.IO.File.WriteAllText(Ashveil.Core.ProjectPaths.Logs("Ashveil_TunnelNavMesh.txt"), sb.ToString());
            if (opened) EditorSceneManager.CloseScene(cell, true);
        }

        private static void Label(Transform root, Vector3 pos, string title, string subtitle, float dist) =>
            K.Label(root, pos, title, subtitle, WorldLabelStyle.Place, dist);
    }
}
