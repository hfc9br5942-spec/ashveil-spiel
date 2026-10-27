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
using L = Ashveil.EditorTools.WestPasturesLayout;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden (Arbeitstitel) – neue Weltzelle westlich von Kareth fuer die Tank-Klassenquests Mensch (Max 29.09.).
    /// Eine Kachel 1200 x 1200 m (x −1800…−600), eigene Szene unter Scenes/Regions, Streaming wie das Grenzgebirge.
    /// Kareth bleibt Kareth: nur die Passkerbe im Randgebirge, der Weideweg und ein Wegweiser (Backup vorher).
    /// Inhalt: Pferdehof Hallgrund, Weiden und Aecker, Kiefernwald, Bach mit zwei Furten, Hohlweg, Felskessel (Sackgasse),
    /// Suchgebiete und Hinweise der Herde, Heimweg-Route, Bedrohungsanker fuer den Weidegang, NavMesh der Zelle.
    /// </summary>
    public static partial class WestPasturesBuilder
    {
        public const string RegionId = "Human_Westweiden";
        public const string CellId = "westweiden";
        public static string ScenePath => WorldManifestBuilder.RegionFolder + "/Westweiden.unity";
        private const string TerrainFolder = ProjectSetup.Root + "/Terrain";
        private const string TileName = "WW_Hallgrund";
        public static string TileDataPath => $"{TerrainFolder}/{TileName}_TerrainData.asset";
        private const int HeightRes = 513, SplatRes = 512;
        public const string WorldGroupName = "Westweiden (Welt-Ebene)";

        private static Material _wood, _woodDark, _roof, _stone, _stoneDark, _straw, _soil, _crop, _water, _leather, _bone, _hide, _fur, _warm, _cloth;

        [MenuItem("Ashveil/Welt/Westweiden bauen (Pferdehof, Greybox)", priority = 515)]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst den Play-Modus beenden.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Ashveil",
                    "Westweiden (neue Weltzelle westlich von Kareth) wird gebaut: Pferdehof Hallgrund, Wald, Bach, Hohlweg und Felskessel " +
                    "fuer die Tank-Klassenquests.\n\nKareth wird NICHT neu erzeugt – vorher wird eine Sicherung angelegt, dann nur eine Passkerbe " +
                    "ins Randgebirge gesenkt, der Weideweg gemalt und das Kareth-NavMesh neu gebacken.\n\nDauert etwa eine Minute.",
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
                KarethBackup.Run();

                EditorUtility.DisplayProgressBar("Ashveil", "Westweiden: Inhalte (Auftraege, Raubtier, Herde) ...", 0.05f);
                QuestContentBuilder.BuildHumanTankQuests();
                EnemyBuilder.BuildHorseStoryContent();
                DefinitionValidator.ValidateAndRebuild();
                SceneManager.SetActiveScene(world);
                WorldManifestBuilder.EnsureManifest();

                EditorUtility.DisplayProgressBar("Ashveil", "Westweiden: Weidepass in Kareth ...", 0.12f);
                if (!ApplyToWorld(world)) return;

                var report = new List<string>();
                BuildCell(report);
                WorldManifestBuilder.EnsureBuildSettings();
                SceneManager.SetActiveScene(world);
                AssetDatabase.SaveAssets();
                string routeReport = WestPasturesRouteTool.Measure(false);
                Debug.Log($"[Ashveil] Westweiden gebaut ({total.Elapsed.TotalSeconds:0} s):\n  " + string.Join("\n  ", report) + "\n" + routeReport);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ================================================================== Welt-Ebene (Kareth)

        /// <summary>Passkerbe ins Kareth-Terrain (auch beim Kareth-Neubau aufgerufen): gleiche Funktion wie die Zelle.</summary>
        public static void ApplyKarethPass(TerrainData data)
        {
            var origin = new Vector3(KarethTerrainBuilder.MinX, -40f, KarethTerrainBuilder.MinZ);
            float size = KarethTerrainBuilder.Size;
            int res = data.heightmapResolution;
            float cell = size / (res - 1);
            int ix0 = 0;
            int ix1 = Mathf.Clamp(Mathf.CeilToInt((L.PassX0 + 4f - origin.x) / cell), 0, res - 1);
            int iz0 = Mathf.Clamp(Mathf.FloorToInt((L.PassZ - 70f - origin.z) / cell), 0, res - 1);
            int iz1 = Mathf.Clamp(Mathf.CeilToInt((L.PassZ + 70f - origin.z) / cell), 0, res - 1);
            float[,] h = data.GetHeights(ix0, iz0, ix1 - ix0 + 1, iz1 - iz0 + 1);
            for (int z = 0; z < h.GetLength(0); z++)
            for (int x = 0; x < h.GetLength(1); x++)
            {
                float wx = origin.x + (ix0 + x) * cell, wz = origin.z + (iz0 + z) * cell;
                float cur = origin.y + h[z, x] * data.size.y;
                float cut = (float)L.Notch(wx, wz, cur);
                if (cut < cur) h[z, x] = Mathf.Clamp01((cut - origin.y) / data.size.y);
            }
            data.SetHeights(ix0, iz0, h);
            PaintKarethRoad(data, origin, size);
            EditorUtility.SetDirty(data);
        }

        private static void PaintKarethRoad(TerrainData data, Vector3 origin, float size)
        {
            int layers = data.terrainLayers.Length;
            int path = KarethTerrainBuilder.PathLayer;
            if (path >= layers) return;
            int res = data.alphamapResolution;
            float cell = size / res;
            int ix0 = Mathf.Clamp(Mathf.FloorToInt((-610f - origin.x) / cell), 0, res - 1);
            int ix1 = Mathf.Clamp(Mathf.CeilToInt((-240f - origin.x) / cell), 0, res - 1);
            int iz0 = Mathf.Clamp(Mathf.FloorToInt((215f - origin.z) / cell), 0, res - 1);
            int iz1 = Mathf.Clamp(Mathf.CeilToInt((275f - origin.z) / cell), 0, res - 1);
            float[,,] a = data.GetAlphamaps(ix0, iz0, ix1 - ix0 + 1, iz1 - iz0 + 1);
            for (int z = 0; z < a.GetLength(0); z++)
            for (int x = 0; x < a.GetLength(1); x++)
            {
                Vector2 p = new Vector2(origin.x + (ix0 + x + 0.5f) * cell, origin.z + (iz0 + z + 0.5f) * cell);
                float d = DistToPolyline(p, L.KarethRoad);
                float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.6f, 3.2f, d));
                if (w <= 0f) continue;
                for (int l = 0; l < layers; l++) a[z, x, l] *= 1f - w;
                a[z, x, path] += w;
            }
            data.SetAlphamaps(ix0, iz0, a);
        }

        public static float DistToPolyline(Vector2 p, Vector2[] line)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector2 a = line[i], b = line[i + 1], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        /// <summary>Passkerbe, Weg, Wegweiser, baumfreie Trasse und Kareth-NavMesh neu.</summary>
        public static bool ApplyToWorld(Scene world)
        {
            Transform env = null;
            foreach (GameObject go in world.GetRootGameObjects())
                if (go.name == "Environment") env = go.transform;
            if (env == null)
            {
                Debug.LogError("[Ashveil] Westweiden: 'Environment' fehlt in Sandbox_M1 – bitte zuerst 'Sandbox-Szene erstellen'.");
                return false;
            }
            Terrain kareth = null;
            foreach (Terrain t in env.GetComponentsInChildren<Terrain>(true))
                if (t.name == "Terrain Kareth") kareth = t;
            if (kareth == null)
            {
                Debug.LogError("[Ashveil] Westweiden: 'Terrain Kareth' nicht gefunden.");
                return false;
            }
            ApplyKarethPass(kareth.terrainData);
            kareth.allowAutoConnect = true;
            kareth.groupingID = 0;
            AssetDatabase.SaveAssetIfDirty(kareth.terrainData);

            // Baeume und Felsen aus Kerbe und Weg raeumen (sie stuenden sonst in der Luft oder im Weg)
            int removed = 0;
            var doomed = new List<GameObject>();
            foreach (Transform t in env.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.parent == null) continue;
                string pn = t.parent.name;
                if (pn != "Kiefernhang (Wald)" && pn != "Felsen") continue;
                Vector2 p = new Vector2(t.position.x, t.position.z);
                bool inNotch = p.x < L.PassX0 + 6f && Mathf.Abs(p.y - L.PassZ) < 40f;
                if (inNotch || DistToPolyline(p, L.KarethRoad) < 5.5f) doomed.Add(t.gameObject);
            }
            foreach (GameObject g in doomed) { Object.DestroyImmediate(g); removed++; }

            Transform old = env.Find(WorldGroupName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Transform root = K.Group(WorldGroupName, env);
            // Wegweiser am Holzfaellerlager und Passname
            Vector3 sign = KarethTerrainBuilder.OnGround(-270f, 246f);
            Transform s = K.Group("Wegweiser Hallgrund", root, sign, new Vector3(0f, -90f, 0f));
            K.Block("Pfosten", s, Vector3.zero, new Vector3(0.18f, 2.2f, 0.18f), _woodDark);
            K.Box("Brett", s, new Vector3(0f, 1.8f, 0.5f), new Vector3(0.08f, 0.32f, 1.2f), _wood);
            K.Label(s, new Vector3(0f, 2.7f, 0f), Ashveil.Data.Loc.T("sign.weidepass"), "", WorldLabelStyle.Place, 45f);
            K.Label(root, new Vector3(-560f, (float)L.PassFloorAt(-560f) + 6f, L.PassZ), Ashveil.Data.Loc.T("place.weidepass.name"), Ashveil.Data.Loc.T("place.westweiden.sub"), WorldLabelStyle.Place, 120f);

            WorldManifestBuilder.EnsureStreamer();
            foreach (NavMeshSurface ns in env.GetComponentsInChildren<NavMeshSurface>(true))
                if (ns.name.StartsWith("NavMesh Kareth"))
                {
                    ns.center = EnemyBuilder.KarethNavMeshCenter;
                    ns.size = EnemyBuilder.KarethNavMeshSize;
                    EnemyBuilder.BakeNavMesh(ns, ProjectSetup.Root + "/Scenes/Sandbox_M1_NavMesh.asset");
                }
            EditorSceneManager.MarkSceneDirty(world);
            EditorSceneManager.SaveScene(world);
            Debug.Log($"[Ashveil] Westweiden: Weidepass in Kareth (Boden {L.PassFloor:0.0} m), {removed} Baeume/Felsen verlegt, Kareth-NavMesh neu.");
            return true;
        }

        // ================================================================== Zelle

        private static void BuildCell(List<string> report)
        {
            var sw = Stopwatch.StartNew();
            ProjectSetup.EnsureFolder(WorldManifestBuilder.RegionFolder);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            Transform root = K.Group("Zelle " + CellId, null);

            EditorUtility.DisplayProgressBar("Ashveil", "Westweiden: Gelaende ...", 0.2f);
            float[,] world = BuildTile(root);
            EditorUtility.DisplayProgressBar("Ashveil", "Westweiden: Wald und Felsen ...", 0.35f);
            int trees = PlaceTrees(K.Group("Baeume", root), world);
            PlaceRocks(K.Group("Felsen", root));
            BuildBrook(K.Group("Bach", root));
            BuildRavineDressing(K.Group("Hohlweg", root));
            EditorUtility.DisplayProgressBar("Ashveil", "Westweiden: Pferdehof und Geschichte ...", 0.45f);
            BuildFarm(K.Group("Pferdehof Hallgrund", root));
            BuildStory(K.Group("Geschichte (Pferdehof)", root));
            BuildBoundary(K.Group("Weltrand", root));
            BuildAnchors(K.Group("Sichere Anker", root));
            BuildQuestSites(K.Group("Quest-Orte", root));
            BuildEnemies(K.Group("Gegner", root));
            BuildLabels(K.Group("Ortsnamen", root));
            BuildSea(root);
            BuildSeamLink(root);
            if (true)
            {
                Transform biomes = K.Group("Gebietsluft", root);
                SkyContentBuilder.PlaceVolume(biomes, "Biome_Lichtung", G(L.FarmCenter.x, L.FarmCenter.y), 200f, 90f);
            }

            EditorUtility.DisplayProgressBar("Ashveil", "Westweiden: NavMesh backen (dauert) ...", 0.6f);
            float bake = BakeCellNavMesh(root);
            EditorSceneManager.SaveScene(scene, ScenePath);
            SceneManager.SetActiveScene(SceneManager.GetSceneByPath(SandboxSceneBuilder.ScenePath));
            EditorSceneManager.CloseScene(scene, true);
            report.Add($"{CellId}: {sw.Elapsed.TotalSeconds:0} s, {trees} Baeume, NavMesh {bake:0.0} s -> {ScenePath}");
        }

        public static Vector3 G(float x, float z, float lift = 0f) => L.OnGround(x, z, lift);

        // ------------------------------------------------------------------ Gelaende

        private static float[,] BuildTile(Transform root)
        {
            ProjectSetup.EnsureFolder(TerrainFolder);
            var xs = new float[HeightRes];
            var zs = new float[HeightRes];
            var kh = new double[HeightRes];
            for (int i = 0; i < HeightRes; i++)
            {
                xs[i] = L.MinX + i / (float)(HeightRes - 1) * L.TileSize;
                zs[i] = L.MinZ + i / (float)(HeightRes - 1) * L.TileSize;
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
            AssetDatabase.DeleteAsset(TileDataPath);
            var data = new TerrainData { heightmapResolution = HeightRes, alphamapResolution = SplatRes, baseMapResolution = 512 };
            data.SetDetailResolution(32, 16);
            data.size = new Vector3(L.TileSize, L.HeightRange, L.TileSize);
            data.SetHeights(0, 0, norm);
            data.terrainLayers = Layers();
            PaintSplat(data, world);
            AssetDatabase.CreateAsset(data, TileDataPath);
            foreach (Texture2D alpha in data.alphamapTextures)
                if (alpha != null && !AssetDatabase.Contains(alpha)) AssetDatabase.AddObjectToAsset(alpha, data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain " + TileName;
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(L.MinX, L.BaseY, L.MinZ);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 6f;
            terrain.basemapDistance = 600f;
            terrain.drawInstanced = true;
            terrain.allowAutoConnect = true;
            terrain.groupingID = 0;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainFolder + "/Kareth_Terrain.mat");
            if (mat != null) terrain.materialTemplate = mat;
            return world;
        }

        private const int LGrass = 0, LForest = 1, LMoor = 2, LAsh = 3, LRock = 4, LPath = 5, LSand = 6;
        private static readonly string[] LayerNames = { "Grass", "ForestFloor", "Moor", "Ash", "Rock", "Path", "Sand" };

        private static TerrainLayer[] Layers()
        {
            var list = new List<TerrainLayer>();
            foreach (string n in LayerNames)
            {
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{TerrainFolder}/Layer_{n}.terrainlayer");
                if (l != null) list.Add(l);
                else Debug.LogWarning($"[Ashveil] Westweiden: Gelaendeschicht {n} fehlt (erst Sandbox-Szene erstellen).");
            }
            return list.ToArray();
        }

        private static float S(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

        private static void PaintSplat(TerrainData data, float[,] world)
        {
            int layers = data.terrainLayers.Length;
            if (layers < LayerNames.Length) return;
            var map = new float[SplatRes, SplatRes, layers];
            float cell = L.TileSize / SplatRes, hcell = L.TileSize / (HeightRes - 1);
            Parallel.For(0, SplatRes, iz =>
            {
                var w = new float[layers];
                float z = L.MinZ + (iz + 0.5f) * cell;
                for (int ix = 0; ix < SplatRes; ix++)
                {
                    float x = L.MinX + (ix + 0.5f) * cell;
                    int hx = Mathf.Clamp(Mathf.RoundToInt((x - L.MinX) / hcell), 1, HeightRes - 2);
                    int hz = Mathf.Clamp(Mathf.RoundToInt((z - L.MinZ) / hcell), 1, HeightRes - 2);
                    float h = world[hz, hx];
                    float gx = (world[hz, hx + 1] - world[hz, hx - 1]) / (2f * hcell);
                    float gz = (world[hz + 1, hx] - world[hz - 1, hx]) / (2f * hcell);
                    float slope = Mathf.Sqrt(gx * gx + gz * gz);
                    System.Array.Clear(w, 0, w.Length);
                    float n = (float)BorderMountainsLayout.Fbm(x, z, 60, 2, 331);
                    float rock = S(0.6f, 0.95f, slope + n * 0.15f);
                    float forest = L.ForestDensity(x, z);
                    double dRoad = L.PassRoad.Dist(x, z, out _);
                    double dBrook = L.Brook.Dist(x, z, out _);
                    double dRav = L.Ravine.Dist(x, z, out _);
                    float path = Mathf.Max(1f - S(1.8f, 3.4f, (float)dRoad), (1f - S(2.4f, 4.6f, (float)dRav)) * 0.7f);
                    float farm = 1f - S(40f, 70f, Vector2.Distance(new Vector2(x, z), L.FarmCenter));
                    w[LGrass] = Mathf.Max(0.05f, 1f - rock) * (1f - forest * 0.7f);
                    w[LForest] = forest * (1f - rock) * 1.4f;
                    w[LRock] = rock * 2.4f;
                    w[LPath] = path * 4f + farm * 0.35f;
                    w[LSand] = (1f - S(2.2f, 4.5f, (float)dBrook)) * 2.2f + S(-8f, -22f, h) * 3f;
                    w[LMoor] = 0f;
                    w[LAsh] = 0f;
                    float sum = 0f;
                    for (int l = 0; l < layers; l++) sum += w[l];
                    for (int l = 0; l < layers; l++) map[iz, ix, l] = w[l] / sum;
                }
            });
            data.SetAlphamaps(0, 0, map);
        }

        // ------------------------------------------------------------------ Baeume und Felsen

        private static readonly (string model, float height)[] PineModels = { ("SM_Pine_A", 12f), ("SM_Pine_B", 14f), ("SM_Pine_C", 10f) };

        private static int PlaceTrees(Transform parent, float[,] world)
        {
            var rng = new System.Random(29092026);
            float hcell = L.TileSize / (HeightRes - 1);
            int placed = 0;
            for (int attempt = 0; attempt < 60000 && placed < 1700; attempt++)
            {
                float x = L.MinX + 8f + (float)rng.NextDouble() * (L.TileSize - 16f);
                float z = L.MinZ + 8f + (float)rng.NextDouble() * (L.TileSize - 16f);
                int ix = Mathf.Clamp(Mathf.RoundToInt((x - L.MinX) / hcell), 1, HeightRes - 2);
                int iz = Mathf.Clamp(Mathf.RoundToInt((z - L.MinZ) / hcell), 1, HeightRes - 2);
                float h = world[iz, ix];
                if (h < L.SeaLevel + 1.5f) continue;
                float slope = Mathf.Sqrt(Mathf.Pow((world[iz, ix + 1] - world[iz, ix - 1]) / (2f * hcell), 2f) + Mathf.Pow((world[iz + 1, ix] - world[iz - 1, ix]) / (2f * hcell), 2f));
                if (slope > 0.75f) continue;
                if (L.Blocked(x, z, 1.5f)) continue;
                float want = L.ForestDensity(x, z);
                if (want < 0.03f) want = 0.012f; // einzelne Baeume auf der Weide
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
                GameObject tree = ArtModels.Place(model, parent, basePos, (basePos.x * 13f + basePos.z * 7f) % 360f, height / mh, ArtModels.ColliderKind.Trunk, true);
                if (tree != null) tree.name = "Kiefer";
                return;
            }
            Transform tr = K.Group("Kiefer", parent, basePos, Vector3.zero);
            K.Prim(PrimitiveType.Cylinder, "Stamm", tr, new Vector3(0f, height * 0.25f, 0f), new Vector3(0.5f, height * 0.25f, 0.5f), Vector3.zero, _woodDark);
            K.Prim(PrimitiveType.Capsule, "Krone", tr, new Vector3(0f, height * 0.62f, 0f), new Vector3(height * 0.35f, height * 0.4f, height * 0.35f), Vector3.zero,
                K.Mat("Pine_Needles", new Color(0.13f, 0.21f, 0.14f)), false);
        }

        private static void PlaceRocks(Transform root)
        {
            string[] rocks = { "SM_Rock_A", "SM_Rock_B", "SM_Rock_C" };
            if (!ArtModels.Has(rocks[0])) return;
            var rng = new System.Random(7209);
            int placed = 0;
            // Felsen im Wald und am Hang (Deckung, Orientierung), dichter an der Bergflanke
            for (int attempt = 0; attempt < 8000 && placed < 260; attempt++)
            {
                float x = L.MinX + 10f + (float)rng.NextDouble() * (L.TileSize - 20f);
                float z = L.MinZ + 10f + (float)rng.NextDouble() * (L.TileSize - 20f);
                if (L.Blocked(x, z, 3f)) continue;
                float h = L.Height(x, z);
                if (h < L.SeaLevel + 1f) continue;
                float want = 0.08f + L.ForestDensity(x, z) * 0.15f + S(520f, 760f, z) * 0.3f;
                if (rng.NextDouble() > want) continue;
                float scale = 0.6f + (float)rng.NextDouble() * 2.4f;
                ArtModels.Place(rocks[rng.Next(rocks.Length)], root, G(x, z, -0.3f * scale), (float)rng.NextDouble() * 360f, scale,
                    scale < 0.9f ? ArtModels.ColliderKind.None : ArtModels.ColliderKind.Mesh, true);
                placed++;
            }
            // Felskessel: Bloecke am Fuss der Waende (Sackgasse lesbar machen)
            for (int i = 0; i < 22; i++)
            {
                float a = i / 22f * Mathf.PI * 2f;
                Vector2 p = L.BasinCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (L.BasinRadius + 3f);
                if (L.Ravine.Dist(p.x, p.y, out _) < 9f) continue;
                ArtModels.Place(rocks[i % 3], root, G(p.x, p.y, -0.4f), i * 47f, 1.6f + (i % 4) * 0.5f, ArtModels.ColliderKind.Mesh, true);
            }
        }

        // ------------------------------------------------------------------ Bach, Hohlweg, Meer

        private static void BuildBrook(Transform root)
        {
            const float step = 6f;
            var brook = L.Brook;
            for (float s = 0f; s < brook.Length - 0.5f; s += step)
            {
                Vector3 a = brook.PointAt(s), b = brook.PointAt(Mathf.Min((float)brook.Length, s + step));
                Vector3 mid = (a + b) * 0.5f;
                float y = L.BrookBedAt(s + step * 0.5f) + 0.5f;
                if (y < L.SeaLevel) continue;
                Vector3 dir = b - a;
                dir.y = 0f;
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                K.Box("Wasser", root, new Vector3(mid.x, y, mid.z), new Vector3(2.6f, 0.04f, dir.magnitude + 0.6f), _water, new Vector3(0f, yaw, 0f), false);
            }
            // Uferboeschung: der Bach ist nur an den Furten querbar (das Hoehenraster ist zu grob fuer steile Ufer – deshalb
            // eine Boeschungskante mit Kollision, 1,6 m hoch, auf beiden Seiten; an den Furten offen)
            Transform banks = K.Group("Uferboeschung", root);
            var bankMat = K.Mat("WW_Uferboeschung", new Color(0.26f, 0.21f, 0.15f));
            const float bankStep = 4f;
            for (float s = 0f; s < brook.Length - 0.5f; s += bankStep)
            {
                Vector3 a = brook.PointAt(s), b = brook.PointAt(Mathf.Min((float)brook.Length, s + bankStep));
                Vector3 mid = (a + b) * 0.5f;
                if (L.BrookBedAt(s) < L.SeaLevel + 1f) continue;
                bool ford = false;
                foreach (Vector2 f in L.Fords) if (Vector2.Distance(new Vector2(mid.x, mid.z), f) < 9f) ford = true;
                if (ford) continue;
                Vector3 dir = b - a;
                dir.y = 0f;
                Vector3 side = Vector3.Cross(Vector3.up, dir.normalized);
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                for (int k = -1; k <= 1; k += 2)
                {
                    Vector3 p = mid + side * (k * 2.9f);
                    float gy = L.Height(p.x, p.z);
                    K.Box("Boeschung", banks, new Vector3(p.x, gy + 0.2f, p.z), new Vector3(0.9f, 1.6f, dir.magnitude + 0.5f), bankMat, new Vector3(0f, yaw, k * 8f));
                }
            }

            // Furten: flache Stellen mit Trittsteinen
            foreach (Vector2 f in L.Fords)
            {
                Transform ford = K.Group("Furt", root, G(f.x, f.y), Vector3.zero);
                for (int i = -2; i <= 2; i++)
                    K.Prim(PrimitiveType.Sphere, "Trittstein", ford, new Vector3(i * 1.3f, 0.05f, (i % 2) * 0.6f), new Vector3(0.9f, 0.3f, 0.8f), Vector3.zero, _stone, false);
            }
        }

        private static void BuildRavineDressing(Transform root)
        {
            // Gestein an den Waenden des Hohlwegs, damit er sich eng anfuehlt
            if (!ArtModels.Has("SM_Rock_A")) return;
            var rv = L.Ravine;
            int i = 0;
            for (double s = 8; s < rv.Length - 10; s += 11, i++)
            {
                Vector3 c = rv.PointAt(s);
                Vector3 dir = rv.DirAt(s);
                Vector3 side = Vector3.Cross(Vector3.up, dir) * (i % 2 == 0 ? 6.5f : -6.5f);
                ArtModels.Place(i % 3 == 0 ? "SM_Rock_B" : "SM_Rock_A", root, G(c.x + side.x, c.z + side.z, -0.6f), i * 61f, 2.2f + (i % 3) * 0.6f, ArtModels.ColliderKind.Mesh, true);
            }
        }

        private static void BuildSea(Transform root)
        {
            var sea = K.Mat("Meer", new Color(0.1f, 0.16f, 0.2f), 0.85f);
            K.Box("Meer Westweiden", root, new Vector3(-1200f, L.SeaLevel, -330f), new Vector3(1250f, 0.1f, 420f), sea, Vector3.zero, false);
        }

        // ------------------------------------------------------------------ Rand, Anker, Orte, Gegner

        private static void BuildBoundary(Transform root)
        {
            void Wall(string name, Vector3 center, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.position = center;
                go.AddComponent<BoxCollider>().size = size;
            }
            float cx = (L.MinX + L.MaxX) * 0.5f;
            Wall("Rand West", new Vector3(L.MinX + 1f, 150f, (L.MinZ + L.MaxZ) * 0.5f), new Vector3(2f, 500f, L.TileSize));
            Wall("Rand Nord", new Vector3(cx, 150f, L.MaxZ - 1f), new Vector3(L.TileSize, 500f, 2f));
            Wall("Rand Sued", new Vector3(cx, 150f, L.MinZ + 1f), new Vector3(L.TileSize, 500f, 2f));
        }

        private static void BuildAnchors(Transform root)
        {
            var list = new (string id, Vector2 p)[]
            {
                ("ww.pass", new Vector2(-640f, L.PassZ)),
                ("ww.hof", new Vector2(-778f, 250f)),
                ("ww.furt", new Vector2(L.Fords[1].x + 12f, L.Fords[1].y)),
            };
            foreach (var (id, p) in list)
            {
                var go = new GameObject("Anker " + id);
                go.transform.SetParent(root, false);
                go.transform.position = G(p.x, p.y, 0.1f);
                go.AddComponent<SafeSpawnAnchor>().Configure(id, CellId);
            }
        }

        public static IEnumerable<(string id, string name, QuestSiteKind kind, int min, int max, Vector2 pos, float radius, string notes)> SiteTable()
        {
            yield return ("site.ww.hallgrund", Ashveil.Data.Loc.T("place.hallgrund.name"), QuestSiteKind.Story, 3, 20, L.FarmCenter, 60f, "Pferdehof – Tank-Klassenquests Mensch (Q1 ab 3, Q2 ab 20)");
            yield return ("site.ww.fohlensenke", Ashveil.Data.Loc.T("place.fohlensenke.name"), QuestSiteKind.Story, 3, 6, L.BasinCenter, 30f, "Sackgasse am Ende des Hohlwegs – Fohlen, Maehnenreisser");
            yield return ("site.ww.hohlweg", Ashveil.Data.Loc.T("place.hohlweg.name"), QuestSiteKind.Explore, 3, 6, new Vector2(-1268f, 668f), 20f, "gewundene Schlucht in die Bergflanke");
            yield return ("site.ww.weidegrund", "Große Weide", QuestSiteKind.Story, 20, 22, new Vector2(-905f, 250f), 60f, "Weidegang: drei Bedrohungen, Weidetor");
        }

        private static void BuildQuestSites(Transform root)
        {
            foreach (var s in SiteTable())
            {
                var go = new GameObject("Ort " + s.name);
                go.transform.SetParent(root, false);
                go.transform.position = G(s.pos.x, s.pos.y);
                go.AddComponent<WorldQuestSite>().Configure(s.id, s.name, "westweiden", "", s.kind, QuestSiteStatus.Ready, s.min, s.max, s.radius, true, s.notes);
            }
        }

        private static void BuildEnemies(Transform root)
        {
            // Etwas Leben im Wald (Stufe 3–5), abseits der Suchgebiete – der Weg soll nicht leer, aber nicht verstopft sein
            var wolfDef = AssetDatabase.LoadAssetAtPath<Ashveil.Data.EnemyDefinition>(EnemyBuilder.DefFolder + "/Enemy_Aschwolf.asset");
            var wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyBuilder.PrefabFolder + "/Enemy_Aschwolf.prefab");
            if (wolfDef == null || wolfPrefab == null) return;
            var e = new EnemyBuilder.BorderEnemy { Def = wolfDef, Prefab = wolfPrefab };
            EnemyBuilder.PlaceArea(root, "Rudel Westwald", G(-1150f, 250f), e, 3, 4f, "", 3, 5);
            EnemyBuilder.PlaceArea(root, "Rudel Bergflanke", G(-1560f, 520f), e, 2, 4f, "", 4, 5);
            EnemyBuilder.PlaceArea(root, "Rudel Kuestenwald", G(-1450f, -130f), e, 3, 4f, "", 4, 5);
        }

        private static void BuildLabels(Transform root)
        {
            K.Label(root, G(L.FarmCenter.x + 10f, L.FarmCenter.y + 35f, 9f), Ashveil.Data.Loc.T("place.hallgrund.name"), Ashveil.Data.Loc.T("place.hallgrund.sub"), WorldLabelStyle.Place, 160f);
            K.Label(root, G(-1300f, 560f, 8f), Ashveil.Data.Loc.T("place.hohlweg.name"), "", WorldLabelStyle.Place, 90f);
            K.Label(root, G(L.BasinCenter.x, L.BasinCenter.y, 12f), Ashveil.Data.Loc.T("place.fohlensenke.name"), "", WorldLabelStyle.Place, 70f);
        }

        // ------------------------------------------------------------------ NavMesh

        /// <summary>Kareth-NavMesh (bis x = −600) ↔ Zellen-NavMesh im Weidepass.</summary>
        private static void BuildSeamLink(Transform root)
        {
            var go = new GameObject("NavMesh-Verbindung Weidepass");
            go.transform.SetParent(root, false);
            var link = go.AddComponent<NavMeshLink>();
            link.startPoint = new Vector3(-596f, (float)L.PassFloorAt(-596f), L.PassZ);
            link.endPoint = new Vector3(-606f, L.Height(-606f, L.PassZ), L.PassZ);
            link.width = L.PassHalfWidth * 2f - 2f;
            link.bidirectional = true;
        }

        [MenuItem("Ashveil/Welt/Westweiden: NavMesh neu backen", priority = 517)]
        public static void RebakeMenu()
        {
            Scene cell = SceneManager.GetSceneByPath(ScenePath);
            bool opened = false;
            if (!cell.IsValid() || !cell.isLoaded) { cell = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); opened = true; }
            NavMeshSurface surface = null;
            foreach (GameObject g in cell.GetRootGameObjects())
                foreach (NavMeshSurface s in g.GetComponentsInChildren<NavMeshSurface>(true)) surface = s;
            if (surface == null) { Debug.LogWarning("[Ashveil] Westweiden: keine NavMeshSurface."); return; }
            var sources = new List<UnityEngine.AI.NavMeshBuildSource>();
            var bounds = new Bounds(surface.transform.TransformPoint(surface.center), surface.size);
            UnityEngine.AI.NavMeshBuilder.CollectSources(bounds, surface.layerMask, surface.useGeometry, 0, new List<UnityEngine.AI.NavMeshBuildMarkup>(), sources);
            int terrains = 0;
            foreach (var src in sources) if (src.shape == UnityEngine.AI.NavMeshBuildSourceShape.Terrain) terrains++;
            var sw = Stopwatch.StartNew();
            surface.BuildNavMesh();
            string path = $"{WorldManifestBuilder.RegionFolder}/NavMesh_{CellId}.asset";
            AssetDatabase.DeleteAsset(path);
            if (surface.navMeshData != null) { AssetDatabase.CreateAsset(surface.navMeshData, path); EditorUtility.SetDirty(surface); }
            EditorSceneManager.MarkSceneDirty(cell);
            EditorSceneManager.SaveScene(cell);
            var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
            Debug.Log($"[Ashveil] Westweiden NavMesh: {sources.Count} Quellen ({terrains} Gelaende) in {bounds}, Backen {sw.Elapsed.TotalSeconds:0.0} s, jetzt {tri.indices.Length / 3} Dreiecke gesamt.");
            if (opened) EditorSceneManager.CloseScene(cell, true);
        }

        private static float BakeCellNavMesh(Transform root)
        {
            // Frisch erzeugtes Gelaende: Kollision erst auffrischen, sonst backt das NavMesh (fast) leer (Befund 29.09.)
            foreach (TerrainCollider tc in root.GetComponentsInChildren<TerrainCollider>(true)) { tc.enabled = false; tc.enabled = true; }
            Physics.SyncTransforms();
            var go = new GameObject("NavMesh " + CellId + " (im Editor gebacken)");
            go.transform.SetParent(root, false);
            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3((L.MinX + L.MaxX) * 0.5f, L.BaseY + L.HeightRange * 0.5f, (L.MinZ + L.MaxZ) * 0.5f);
            surface.size = new Vector3(L.TileSize, L.HeightRange, L.TileSize);
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            int exclude = LayerMask.GetMask(GameLayers.Player, GameLayers.Enemy, GameLayers.Interactable, "Ignore Raycast", "UI");
            surface.layerMask = ~exclude;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.3f;
            surface.overrideTileSize = true;
            surface.tileSize = 256;
            var sw = Stopwatch.StartNew();
            surface.BuildNavMesh();
            string path = $"{WorldManifestBuilder.RegionFolder}/NavMesh_{CellId}.asset";
            AssetDatabase.DeleteAsset(path);
            if (surface.navMeshData != null)
            {
                AssetDatabase.CreateAsset(surface.navMeshData, path);
                EditorUtility.SetDirty(surface);
            }
            return (float)sw.Elapsed.TotalSeconds;
        }

        // ------------------------------------------------------------------ Materialien

        private static void CreateMaterials()
        {
            _wood = K.Mat("WW_Holz", new Color(0.36f, 0.26f, 0.17f));
            _woodDark = K.Mat("WW_Holz_Dunkel", new Color(0.2f, 0.14f, 0.1f));
            _roof = K.Mat("WW_Dach", new Color(0.24f, 0.19f, 0.15f));
            _stone = K.Mat("WW_Stein", new Color(0.44f, 0.43f, 0.41f));
            _stoneDark = K.Mat("WW_Stein_Dunkel", new Color(0.27f, 0.26f, 0.25f));
            _straw = K.Mat("WW_Stroh", new Color(0.66f, 0.56f, 0.32f));
            _soil = K.Mat("WW_Acker", new Color(0.24f, 0.18f, 0.12f));
            _crop = K.Mat("WW_Saat", new Color(0.42f, 0.46f, 0.22f));
            _water = K.Mat("WW_Wasser", new Color(0.12f, 0.2f, 0.24f), 0.85f);
            _leather = K.Mat("WW_Leder", new Color(0.33f, 0.2f, 0.11f));
            _bone = K.Mat("WW_Knochen", new Color(0.78f, 0.75f, 0.66f));
            _hide = K.Mat("WW_Fell_Braun", new Color(0.3f, 0.2f, 0.13f));
            _fur = K.Mat("WW_Fellbueschel", new Color(0.12f, 0.11f, 0.1f));
            _warm = K.Mat("WW_Fenster_Warm", new Color(1f, 0.72f, 0.4f), 0.2f, new Color(1f, 0.55f, 0.2f) * 2f);
            _cloth = K.Mat("WW_Tuch", new Color(0.46f, 0.4f, 0.32f));
        }
    }
}
