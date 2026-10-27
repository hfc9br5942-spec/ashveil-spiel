using System.Collections.Generic;
using System.Diagnostics;
using Ashveil.Core;
using Ashveil.Gameplay.Colosseum;
using Ashveil.Networking;
using Ashveil.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kolosseum Pass 2 – „Der Ring von Kareth“ als Greybox (Kanon: Docs/Design/ashveil-kolosseum.md, Plan: Pass 1 §7).
    ///
    ///   Kareth (Welt-Ebene): Vorplatz im Nordosten des Aschenfelds, Fassade halb in die Felswand eingelassen, Tor mit
    ///     Eingang (<see cref="DungeonPortal"/>), Ringmeister, Marketenderin, Waffenwart, Tafel, Feuer; ein alter Weg
    ///     zweigt vom Ostweg ab und fuehrt an Saeulenresten und einer zerbrochenen Statue vorbei hinauf.
    ///   Instanz: <see cref="SlotCount"/> Arenen (Ø 70 m) in der Kolosseum-Reihe des <see cref="InstanceLayout"/> –
    ///     vier Tore (N/O/S/W) mit Torgang, Bodenoeffnung in der Mitte, obere Galerie (Podium mit zwei Treppen),
    ///     Kaempferpforte als Ein- und Ausgang, Raenge, Aussenmauer, Waechterstatuen, Feuerschalen, Banner. Freiluft.
    ///   Eigenes NavMesh (Platz 0 gebacken, an allen Plaetzen eingesetzt), eigener <see cref="DungeonDirector"/>.
    ///
    /// Kareth wird nicht neu erzeugt: nur der Vorplatz wird im Gelaende geebnet und mit dem Weg eingefaerbt,
    /// danach wird das Kareth-NavMesh (unter einer Sekunde) neu gebacken.
    /// </summary>
    public static class ColosseumBuilder
    {
        public const string DungeonId = "kolosseum";
        public const string DisplayName = "Ring von Kareth";
        public const int SlotCount = 6;
        public const string NavMeshAssetPath = ProjectSetup.Root + "/Scenes/Kolosseum_NavMesh.asset";
        private const string ForecourtGroup = "Kolosseum – Vorplatz (Kareth)";
        private const string InstanceGroup = "Kolosseum – Instanzen (K1)";

        // ------------------------------------------------------------------ Vorplatz in Kareth (Weltkoordinaten)
        public static readonly Vector2 ForecourtCenter = new Vector2(490f, 285f);
        public const float ForecourtRadiusX = 26f, ForecourtRadiusZ = 38f, ForecourtBlend = 0.35f;
        /// <summary>Mittelpunkt des Fassadenbogens (im Fels) und sein Radius – die Stirn des Bogens liegt bei x ≈ 517.</summary>
        public static readonly Vector2 FacadeCenter = new Vector2(575f, 285f);
        public const float FacadeRadius = 58f;
        public const float FacadeHalfAngle = 40f;
        /// <summary>Eingang direkt vor dem Tor, Rueckkehr 9 m davor (Blick nach Westen).</summary>
        public static readonly Vector2 PortalPos = new Vector2(513.6f, 285f);
        public const float PortalRadius = 3.2f;

        /// <summary>Alter Weg: zweigt vom Ostweg ab, am Uhrwerk-Tor vorbei auf den Vorplatz.</summary>
        public static readonly Vector2[] ApproachPath =
        {
            new Vector2(450f, 35f), new Vector2(488f, 120f), new Vector2(494f, 205f), new Vector2(492f, 252f), new Vector2(496f, 280f)
        };

        // ------------------------------------------------------------------ Arena (lokal zum Platz, Boden y = 0)
        public const float ArenaRadius = 35f;
        public const float PodiumTop = 4.5f;          // obere Galerie
        public const float PodiumOuter = 39.5f;
        public const float GateHalfWidth = 4f;        // Tore 8 m breit, 7 m hoch
        public const float GateHeight = 7f;
        public const float WallThick = 1.5f;
        public const float OuterWallR0 = 57.5f, OuterWallR1 = 60f, OuterWallTop = 24f;
        public const float GateSpawnR = 50f, GateEntryR = 28f, GateEndR = 62.5f;
        public const float DoorBearing = 160f, DoorHalfWidth = 2f; // Kaempferpforte
        public static readonly float[] StairBearings = { 35f, 215f };
        public static readonly float[] GalleryBearings = { 55f, 235f };
        public static readonly (string id, string name, float bearing)[] Gates =
        {
            ("nord", "Nordtor", 0f), ("ost", "Osttor", 90f), ("sued", "Suedtor", 180f), ("west", "Westtor", 270f)
        };

        public static readonly Vector3 Arrival = Polar(DoorBearing, 30f, 0f);
        public const float ArrivalYaw = DoorBearing + 180f;
        public static readonly Vector3 Exit = Polar(DoorBearing, 42f, 0f);
        public const float ExitRadius = 1.8f;
        public static readonly Vector3 BoundsCenter = new Vector3(0f, 10f, 0f);
        public static readonly Vector3 BoundsSize = new Vector3(140f, 50f, 140f);

        public static Vector3 SlotOrigin(int index) => InstanceLayout.Slot(InstanceLayout.ColosseumRow, index);

        /// <summary>Punkt auf dem Kreis: Peilung 0 = +z (Norden), 90 = +x (Osten).</summary>
        public static Vector3 Polar(float bearing, float r, float y)
        {
            float a = bearing * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
        }

        public static readonly List<Transform> Slots = new List<Transform>();
        private static readonly List<NavMeshSurface> Surfaces = new List<NavMeshSurface>();

        private static Material _stone, _stoneDark, _stoneWarm, _sand, _metal, _wood, _woodDark, _banner, _dark, _fire, _gold, _npc, _npcQuest, _canvas;

        // ================================================================== Menue

        [MenuItem("Ashveil/Welt/Kolosseum bauen (Greybox)", priority = 530)]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst den Play-Modus beenden.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Ashveil",
                    "Kolosseum (Greybox, Pass 2) wird gebaut: Vorplatz mit Fassade, Ringmeister, Händlern und Eingang im Nordosten des Aschenfelds, " +
                    "dazu 6 Arena-Instanzen weit außerhalb der Welt.\n\n" +
                    "Kareth wird NICHT neu erzeugt – nur der Vorplatz wird geebnet und der alte Weg eingefärbt; das Kareth-NavMesh wird neu gebacken.",
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

                Transform env = null;
                foreach (GameObject go in world.GetRootGameObjects())
                    if (go.name == "Environment") env = go.transform;
                if (env == null)
                {
                    Debug.LogError("[Ashveil] Kolosseum: 'Environment' fehlt in Sandbox_M1 – bitte zuerst 'Sandbox-Szene erstellen'.");
                    return;
                }

                EditorUtility.DisplayProgressBar("Ashveil", "Kolosseum: NPCs und Datenbank ...", 0.05f);
                K.ResetCache();
                QuestContentBuilder.BuildColosseumNpcs();
                DefinitionValidator.ValidateAndRebuild();
                SceneManager.SetActiveScene(world);

                EditorUtility.DisplayProgressBar("Ashveil", "Kolosseum: Vorplatz im Gelaende ...", 0.15f);
                Terrain kareth = FindKarethTerrain(env);
                if (kareth != null)
                {
                    ApplyKarethForecourt(kareth.terrainData, kareth.transform.position);
                    AssetDatabase.SaveAssetIfDirty(kareth.terrainData);
                }
                else Debug.LogWarning("[Ashveil] Kolosseum: 'Terrain Kareth' nicht gefunden – Vorplatz ohne Einebnung.");

                EditorUtility.DisplayProgressBar("Ashveil", "Kolosseum: Fassade, Vorplatz, Arenen ...", 0.3f);
                BuildInto(env);

                EditorUtility.DisplayProgressBar("Ashveil", "Kolosseum: Kareth-NavMesh ...", 0.6f);
                foreach (NavMeshSurface s in env.GetComponentsInChildren<NavMeshSurface>(true))
                    if (s.name.StartsWith("NavMesh Kareth"))
                        EnemyBuilder.BakeNavMesh(s, ProjectSetup.Root + "/Scenes/Sandbox_M1_NavMesh.asset");

                EditorSceneManager.MarkSceneDirty(world);
                EditorSceneManager.SaveScene(world);
                // Wie bei den Salzgruben: erst nach dem Speichern backen (frische Kollider werden sonst nicht gefunden)
                EditorUtility.DisplayProgressBar("Ashveil", "Kolosseum: Arena-NavMesh ...", 0.8f);
                BakeNavMesh();
                EditorSceneManager.MarkSceneDirty(world);
                EditorSceneManager.SaveScene(world);

                // Large-World N3 + Instanz-Regel: Sichtbarkeit der Gegner und Spieler je Instanz-Platz
                InterestManagement.ApplyToEnemyPrefabs();
                AssetDatabase.SaveAssets();
                Debug.Log($"[Ashveil] Kolosseum gebaut ({total.Elapsed.TotalSeconds:0} s): Vorplatz bei {ForecourtCenter}, Hoehe {ForecourtY:0.00} m, " +
                          $"{Slots.Count} Arenen ab {SlotOrigin(0)}, Eingang '{DungeonId}'.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Vorplatz + Instanzen in die Welt-Ebene setzen (auch vom Sandbox-Neuaufbau benutzt). Ersetzt alte Gruppen.</summary>
        public static void BuildInto(Transform env)
        {
            CreateMaterials();
            foreach (string name in new[] { ForecourtGroup, InstanceGroup })
            {
                Transform old = env.Find(name);
                while (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                    old = env.Find(name);
                }
            }
            ClearAshDressing(env);
            BuildForecourt(env, FindKarethTerrain(env));
            BuildInstances(env);
        }

        private static Terrain FindKarethTerrain(Transform env)
        {
            foreach (Terrain t in env.GetComponentsInChildren<Terrain>(true))
                if (t.name == "Terrain Kareth") return t;
            return null;
        }

        private static void CreateMaterials()
        {
            _stone = K.Mat("Arena_Stone", new Color(0.47f, 0.47f, 0.48f));
            _stoneDark = K.Mat("Arena_Stone_Dark", new Color(0.3f, 0.3f, 0.32f));
            _stoneWarm = K.Mat("Arena_Stone_Warm", new Color(0.52f, 0.49f, 0.45f));
            _sand = K.Mat("Arena_Sand", new Color(0.5f, 0.45f, 0.37f));
            _metal = K.Mat("Arena_Metal", new Color(0.16f, 0.16f, 0.18f), 0.35f);
            _wood = K.Mat("Wood", new Color(0.36f, 0.25f, 0.16f));
            _woodDark = K.Mat("Wood_Dark", new Color(0.2f, 0.14f, 0.1f));
            _banner = K.Mat("Arena_Banner", new Color(0.38f, 0.07f, 0.06f));
            _dark = K.Mat("Arena_Darkness", new Color(0.02f, 0.02f, 0.025f), 0f);
            _fire = K.Mat("Arena_Fire", new Color(1f, 0.55f, 0.2f), 0.1f, new Color(1f, 0.45f, 0.15f) * 2.2f);
            _gold = K.Mat("Arena_OldGold", new Color(0.52f, 0.41f, 0.2f), 0.45f);
            _npc = K.Mat("Arena_Npc", new Color(0.42f, 0.36f, 0.3f));
            _npcQuest = K.Mat("Arena_Npc_Ringmeister", new Color(0.45f, 0.12f, 0.1f));
            _canvas = K.Mat("Canvas", new Color(0.55f, 0.5f, 0.42f));
        }

        // ================================================================== Gelaende (gezielt, idempotent)

        private static float S(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

        /// <summary>0 = Rand des Vorplatzes, &lt; 1 innen (normierter Ellipsenabstand).</summary>
        private static float ForecourtDist(float x, float z)
        {
            float dx = (x - ForecourtCenter.x) / ForecourtRadiusX, dz = (z - ForecourtCenter.y) / ForecourtRadiusZ;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Liegt der Punkt auf dem Vorplatz (mit Rand in m)? Aschenfeld-Deko und Felsen bleiben dort weg.</summary>
        public static bool InForecourt(float x, float z, float margin)
        {
            float dx = (x - ForecourtCenter.x) / (ForecourtRadiusX + margin), dz = (z - ForecourtCenter.y) / (ForecourtRadiusZ + margin);
            return dx * dx + dz * dz < 1f;
        }

        private static float _forecourtY = float.NaN;

        /// <summary>Hoehe des ebenen Vorplatzes: Mittel der urspruenglichen Gelaendehoehe in der Ellipse (fest, aus der Hoehenfunktion).</summary>
        public static float ForecourtY
        {
            get
            {
                if (!float.IsNaN(_forecourtY)) return _forecourtY;
                float sum = 0f;
                int n = 0;
                for (float x = ForecourtCenter.x - ForecourtRadiusX; x <= ForecourtCenter.x + ForecourtRadiusX; x += 2f)
                for (float z = ForecourtCenter.y - ForecourtRadiusZ; z <= ForecourtCenter.y + ForecourtRadiusZ; z += 2f)
                    if (ForecourtDist(x, z) <= 1f) { sum += KarethTerrainBuilder.Height(x, z); n++; }
                _forecourtY = n > 0 ? sum / n : 0.5f;
                return _forecourtY;
            }
        }

        /// <summary>
        /// Vorplatz ebnen und Weg + Platz einfaerben. Rechnet von der Hoehenfunktion aus (nicht vom aktuellen Gelaende),
        /// ist also beliebig oft wiederholbar. Aufgerufen vom Kareth-Neuaufbau und vom Menue.
        /// </summary>
        public static void ApplyKarethForecourt(TerrainData data, Vector3 terrainPos)
        {
            float target = ForecourtY;
            int res = data.heightmapResolution;
            float cellX = data.size.x / (res - 1), cellZ = data.size.z / (res - 1);
            float reachX = ForecourtRadiusX * (1f + ForecourtBlend) + 2f, reachZ = ForecourtRadiusZ * (1f + ForecourtBlend) + 2f;
            int ix0 = Mathf.Clamp(Mathf.FloorToInt((ForecourtCenter.x - reachX - terrainPos.x) / cellX), 0, res - 1);
            int ix1 = Mathf.Clamp(Mathf.CeilToInt((ForecourtCenter.x + reachX - terrainPos.x) / cellX), 0, res - 1);
            int iz0 = Mathf.Clamp(Mathf.FloorToInt((ForecourtCenter.y - reachZ - terrainPos.z) / cellZ), 0, res - 1);
            int iz1 = Mathf.Clamp(Mathf.CeilToInt((ForecourtCenter.y + reachZ - terrainPos.z) / cellZ), 0, res - 1);
            float[,] h = data.GetHeights(ix0, iz0, ix1 - ix0 + 1, iz1 - iz0 + 1);
            for (int z = 0; z < h.GetLength(0); z++)
            for (int x = 0; x < h.GetLength(1); x++)
            {
                float wx = terrainPos.x + (ix0 + x) * cellX, wz = terrainPos.z + (iz0 + z) * cellZ;
                float w = 1f - S(1f, 1f + ForecourtBlend, ForecourtDist(wx, wz));
                if (w <= 0f) continue;
                float orig = KarethTerrainBuilder.Height(wx, wz);
                h[z, x] = Mathf.Clamp01((Mathf.Lerp(orig, target, w) - terrainPos.y) / data.size.y);
            }
            data.SetHeights(ix0, iz0, h);

            // Weg und Platz einfaerben (Ebene "festgetretene Erde")
            int layer = KarethTerrainBuilder.PathLayer;
            if (layer < data.alphamapLayers)
            {
                int ares = data.alphamapResolution;
                float acx = data.size.x / ares, acz = data.size.z / ares;
                float minX = ApproachPath[0].x - 6f, maxX = ForecourtCenter.x + ForecourtRadiusX + 6f;
                float minZ = ApproachPath[0].y - 6f, maxZ = ForecourtCenter.y + ForecourtRadiusZ + 6f;
                foreach (Vector2 p in ApproachPath) { minX = Mathf.Min(minX, p.x - 6f); maxX = Mathf.Max(maxX, p.x + 6f); }
                int ax0 = Mathf.Clamp(Mathf.FloorToInt((minX - terrainPos.x) / acx), 0, ares - 1);
                int ax1 = Mathf.Clamp(Mathf.CeilToInt((maxX - terrainPos.x) / acx), 0, ares - 1);
                int az0 = Mathf.Clamp(Mathf.FloorToInt((minZ - terrainPos.z) / acz), 0, ares - 1);
                int az1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - terrainPos.z) / acz), 0, ares - 1);
                float[,,] map = data.GetAlphamaps(ax0, az0, ax1 - ax0 + 1, az1 - az0 + 1);
                int layers = map.GetLength(2);
                for (int z = 0; z < map.GetLength(0); z++)
                for (int x = 0; x < map.GetLength(1); x++)
                {
                    float wx = terrainPos.x + (ax0 + x + 0.5f) * acx, wz = terrainPos.z + (az0 + z + 0.5f) * acz;
                    float pw = PathWeight(wx, wz);
                    float oldPath = map[z, x, layer];
                    if (pw <= oldPath + 0.001f) continue;
                    // Weg-Anteil anheben, die anderen Ebenen anteilig kuerzen (Summe bleibt 1, wiederholbar)
                    float rest = 1f - oldPath;
                    float factor = rest > 1e-4f ? (1f - pw) / rest : 0f;
                    for (int l = 0; l < layers; l++) map[z, x, l] = l == layer ? pw : map[z, x, l] * factor;
                }
                data.SetAlphamaps(ax0, az0, map);
            }
            EditorUtility.SetDirty(data);
        }

        /// <summary>Gewicht "Weg" (0..1): Weg 1,6–3,4 m, Vorplatz innerhalb der Ellipse (Rand weich).</summary>
        private static float PathWeight(float x, float z)
        {
            var p = new Vector2(x, z);
            float best = float.MaxValue;
            for (int i = 0; i + 1 < ApproachPath.Length; i++)
            {
                Vector2 a = ApproachPath[i], b = ApproachPath[i + 1], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            float path = 1f - S(1.6f, 3.4f, best);
            float plaza = (1f - S(0.82f, 1.02f, ForecourtDist(x, z))) * 0.85f;
            return Mathf.Max(path, plaza);
        }

        /// <summary>Verbrannte Stuempfe und Glutrisse des Aschenfelds vom Vorplatz raeumen (vom Sandbox-Bau zufaellig verteilt).</summary>
        private static void ClearAshDressing(Transform env)
        {
            int n = 0;
            var doomed = new List<GameObject>();
            foreach (Transform t in env.GetComponentsInChildren<Transform>(true))
                if ((t.name == "Burnt Stump" || t.name == "Ember Crack") && InForecourt(t.position.x, t.position.z, 4f)) doomed.Add(t.gameObject);
            foreach (GameObject go in doomed) { Object.DestroyImmediate(go); n++; }
            if (n > 0) Debug.Log($"[Ashveil] Kolosseum: {n} Aschenfeld-Deko-Teile vom Vorplatz entfernt.");
        }

        // ================================================================== Vorplatz und Fassade (Kareth)

        private static Terrain _terrain;

        private static float Ground(float x, float z)
        {
            if (_terrain != null) return _terrain.SampleHeight(new Vector3(x, 0f, z)) + _terrain.transform.position.y;
            return InForecourt(x, z, 0f) ? ForecourtY : KarethTerrainBuilder.Height(x, z);
        }

        private static Vector3 G(float x, float z, float lift = 0f) => new Vector3(x, Ground(x, z) + lift, z);

        private static void BuildForecourt(Transform env, Terrain terrain)
        {
            _terrain = terrain;
            float y0 = ForecourtY;
            Transform root = K.Group(ForecourtGroup, env);
            BuildFacade(K.Group("Fassade (in die Felswand eingelassen)", root), y0);

            // Eingang (Instanz) direkt vor dem Tor, Rueckkehr 9 m davor mit Blick nach Westen
            var portalGo = new GameObject("Eingang (Instanz Kolosseum)");
            portalGo.transform.SetParent(root, false);
            portalGo.transform.position = new Vector3(PortalPos.x, y0, PortalPos.y);
            portalGo.AddComponent<DungeonPortal>().Configure(DungeonId, DisplayName, PortalRadius, new Vector3(-9f, 0f, 0f), 270f);

            Transform plaza = K.Group("Vorplatz", root);
            // Pflaster: lose Steinplatten (ohne Kollision – kein Stolpern, NavMesh bleibt glatt)
            var rng = new System.Random(4711);
            for (float x = ForecourtCenter.x - ForecourtRadiusX + 3f; x < ForecourtCenter.x + ForecourtRadiusX - 1f; x += 3.2f)
            for (float z = ForecourtCenter.y - ForecourtRadiusZ + 3f; z < ForecourtCenter.y + ForecourtRadiusZ - 3f; z += 3.2f)
            {
                if (ForecourtDist(x, z) > 0.88f || rng.NextDouble() < 0.35) continue;
                float s = 2.2f + (float)rng.NextDouble() * 0.8f;
                K.Box("Platte", plaza, new Vector3(x, y0 + 0.04f, z), new Vector3(s, 0.08f, s * (0.8f + (float)rng.NextDouble() * 0.4f)),
                    rng.NextDouble() < 0.5 ? _stone : _stoneDark, new Vector3(0f, (float)rng.NextDouble() * 12f - 6f, 0f), false);
            }

            // Feuerstelle mit Baenken – Treffpunkt der Gruppen (Kanon §54)
            Transform fire = K.Group("Feuerstelle", plaza, G(488f, 283f), Vector3.zero);
            for (int i = 0; i < 8; i++)
                K.Block("Stein", fire, Polar(i * 45f, 1.1f, 0f), new Vector3(0.5f, 0.35f, 0.5f), _stoneDark, i * 20f);
            K.Box("Glut", fire, new Vector3(0f, 0.15f, 0f), new Vector3(1.2f, 0.2f, 1.2f), _fire, default, false);
            K.PointLight("Lagerfeuer", fire, new Vector3(0f, 1f, 0f), new Color(1f, 0.55f, 0.25f), 11f, 2.4f, true);
            K.Particles("Rauch", fire, new Vector3(0f, 0.6f, 0f), new Color(0.3f, 0.29f, 0.28f, 0.35f), 1.2f, 4f, 1.1f, 5f, 8f, 0.3f);
            for (int i = 0; i < 3; i++)
            {
                float b = 200f + i * 70f;
                K.Box("Bank", fire, Polar(b, 3.6f, 0.25f), new Vector3(2.4f, 0.45f, 0.5f), _woodDark, new Vector3(0f, b + 90f, 0f));
            }

            // Ringmeister am Tor (Nordseite), Tafel der Ringkaempfer (Suedseite)
            Transform booth = K.Group("Stand des Ringmeisters", plaza, G(505f, 295f), new Vector3(0f, 250f, 0f));
            K.Block("Tisch", booth, new Vector3(0f, 0f, 1.2f), new Vector3(2.6f, 1f, 0.9f), _wood);
            K.Block("Pfosten L", booth, new Vector3(-1.5f, 0f, 2.2f), new Vector3(0.15f, 2.6f, 0.15f), _wood);
            K.Block("Pfosten R", booth, new Vector3(1.5f, 0f, 2.2f), new Vector3(0.15f, 2.6f, 0.15f), _wood);
            K.Box("Tuch", booth, new Vector3(0f, 2.6f, 1.6f), new Vector3(3.4f, 0.06f, 1.8f), _banner, new Vector3(-10f, 0f, 0f));
            K.Box("Buch", booth, new Vector3(0.4f, 1.05f, 1.2f), new Vector3(0.5f, 0.08f, 0.4f), _stoneDark, default, false);
            Npc(plaza, "Ringmeister Brannoc", "Der Ring von Kareth", G(504.2f, 292.6f), _npcQuest, QuestContentBuilder.Ringmaster, 250f);

            Transform board = K.Group("Tafel der Ringkaempfer", plaza, G(506f, 272f), new Vector3(0f, 290f, 0f));
            K.Block("Stele", board, Vector3.zero, new Vector3(3.2f, 4.2f, 0.7f), _stoneWarm);
            K.Box("Inschrift", board, new Vector3(0f, 2.6f, -0.37f), new Vector3(2.4f, 2.2f, 0.05f), _stoneDark, default, false);
            K.Label(board, new Vector3(0f, 5f, 0f), "Tafel der Ringkämpfer", "Bestenliste – folgt", WorldLabelStyle.Place, 40f, 0.8f);

            // Marketenderin (Nordwesten) und Waffenwart (Suedwesten)
            Transform stall = K.Group("Stand der Marketenderin", plaza, G(476f, 305f), new Vector3(0f, 125f, 0f));
            K.Block("Theke", stall, Vector3.zero, new Vector3(2.8f, 1f, 0.8f), _wood);
            for (int i = 0; i < 4; i++)
                K.Block("Pfosten", stall, new Vector3(i < 2 ? -1.4f : 1.4f, 0f, i % 2 == 0 ? -0.4f : 1.3f), new Vector3(0.12f, 2.5f, 0.12f), _wood);
            K.Box("Dach", stall, new Vector3(0f, 2.55f, 0.45f), new Vector3(3.2f, 0.06f, 2.3f), _canvas, new Vector3(-8f, 0f, 0f));
            for (int i = 0; i < 3; i++) K.Block("Kiste", stall, new Vector3(-2.2f + i * 0.2f, i * 0.6f, 1.6f), new Vector3(0.7f, 0.6f, 0.7f), _woodDark, i * 15f);
            Npc(plaza, "Marketenderin Oda", "Proviant & Pulver", G(477.3f, 303.6f), _npc, QuestContentBuilder.Sutler, 125f);

            Transform smith = K.Group("Waffenwart", plaza, G(475f, 266f), new Vector3(0f, 60f, 0f));
            K.Block("Amboss-Sockel", smith, new Vector3(0f, 0f, 1.5f), new Vector3(0.7f, 0.6f, 0.7f), _woodDark);
            K.Block("Amboss", smith, new Vector3(0f, 0.6f, 1.5f), new Vector3(1f, 0.35f, 0.45f), _metal);
            K.Block("Waffenstaender", smith, new Vector3(-2f, 0f, 1.8f), new Vector3(2.2f, 1.9f, 0.3f), _wood);
            for (int i = 0; i < 4; i++) K.Box("Klinge", smith, new Vector3(-2.8f + i * 0.5f, 1.2f, 1.6f), new Vector3(0.08f, 1.5f, 0.05f), _metal, new Vector3(0f, 0f, 6f), false);
            K.PointLight("Esse", smith, new Vector3(1.4f, 0.8f, 2f), new Color(1f, 0.5f, 0.2f), 5f, 1.4f, true);
            K.Block("Esse", smith, new Vector3(1.4f, 0f, 2f), new Vector3(1.2f, 0.8f, 1.2f), _stoneDark);
            Npc(plaza, "Waffenwart Hesk", "Rüstung (Reparatur folgt)", G(475.8f, 265.1f), _npc, QuestContentBuilder.Armorer, 60f);

            // Zelte der Ringkaempfer am Nordrand
            foreach ((float x, float z, float yaw) in new[] { (470f, 316f, 150f), (482f, 320f, 180f), (466f, 294f, 100f) })
            {
                Transform tent = K.Group("Zelt", plaza, G(x, z), new Vector3(0f, yaw, 0f));
                if (ArtModels.Place("SM_Tent", tent, Vector3.zero, 0f, 1f, ArtModels.ColliderKind.Mesh) == null)
                {
                    K.Box("Seite L", tent, new Vector3(-0.95f, 1.1f, 0f), new Vector3(0.08f, 2.9f, 3.6f), _canvas, new Vector3(0f, 0f, -40f));
                    K.Box("Seite R", tent, new Vector3(0.95f, 1.1f, 0f), new Vector3(0.08f, 2.9f, 3.6f), _canvas, new Vector3(0f, 0f, 40f));
                }
            }

            // Banner der heutigen Veranstalter rund um den Platz
            foreach ((float x, float z) in new[] { (468f, 280f), (470f, 256f), (500f, 312f), (486f, 318f), (500f, 258f), (478f, 250f) })
                BannerPole(plaza, G(x, z));

            // Anmarsch: Saeulenreste, Bloecke, zerbrochene Statue – erst dann oeffnet sich der Blick (Kanon §2)
            Transform ruins = K.Group("Anmarsch – Ruinen", root);
            Column(ruins, G(484f, 236f), 6.5f, 1.3f);
            Column(ruins, G(501f, 241f), 3.4f, 1.3f);
            Column(ruins, G(486f, 212f), 4.8f, 1.2f);
            K.Prim(PrimitiveType.Cylinder, "Gestuerzte Saeule", ruins, G(496f, 226f, 0.6f), new Vector3(1.3f, 3.5f, 1.3f), new Vector3(90f, 35f, 0f), _stone);
            K.Block("Block", ruins, G(480f, 222f, -0.3f), new Vector3(3.2f, 2.2f, 2.4f), _stoneWarm, 18f);
            K.Block("Block", ruins, G(504f, 219f, -0.3f), new Vector3(2.6f, 1.6f, 3.4f), _stone, -12f);
            K.Block("Block", ruins, G(499f, 262f, -0.2f), new Vector3(2f, 1.2f, 2f), _stoneDark, 40f);
            Transform statue = K.Group("Zerbrochene Statue", ruins, G(492f, 250f), new Vector3(0f, 250f, 0f));
            K.Block("Sockel", statue, Vector3.zero, new Vector3(3f, 1.6f, 3f), _stoneWarm);
            K.Block("Bein L", statue, new Vector3(-0.55f, 1.6f, 0f), new Vector3(0.8f, 3.2f, 0.9f), _stone);
            K.Block("Bein R", statue, new Vector3(0.6f, 1.6f, 0.2f), new Vector3(0.8f, 2.1f, 0.9f), _stone, 6f);
            K.Block("Kopf (gestuerzt)", statue, new Vector3(3.2f, -0.4f, 2.4f), new Vector3(1.8f, 2f, 1.9f), _stone, 30f);
            BannerPole(ruins, G(488f, 246f));
            BannerPole(ruins, G(497f, 246f));

            // Wegweiser an der Abzweigung vom Ostweg
            Transform sign = K.Group("Wegweiser Ring", root, G(ApproachPath[0].x - 2.5f, ApproachPath[0].y - 1f), new Vector3(0f, 20f, 0f));
            K.Block("Pfahl", sign, Vector3.zero, new Vector3(0.18f, 2.4f, 0.18f), _wood);
            K.Box("Schild", sign, new Vector3(0.6f, 2f, 0f), new Vector3(1.3f, 0.35f, 0.06f), _woodDark);
            K.Box("Wimpel", sign, new Vector3(0f, 2.9f, 0f), new Vector3(0.05f, 0.9f, 0.5f), _banner, default, false);
            K.Label(sign, new Vector3(0f, 3.2f, 0f), "Zum Ring von Kareth", "", WorldLabelStyle.Place, 35f, 0.7f);
            _terrain = null;
        }

        private static void BannerPole(Transform parent, Vector3 pos)
        {
            Transform p = K.Group("Bannerstange", parent, pos, Vector3.zero);
            K.Block("Stange", p, Vector3.zero, new Vector3(0.16f, 5.2f, 0.16f), _woodDark);
            K.Box("Querholz", p, new Vector3(0f, 5f, 0f), new Vector3(1.4f, 0.1f, 0.1f), _woodDark, default, false);
            K.Box("Banner", p, new Vector3(0f, 3.8f, 0.05f), new Vector3(1.2f, 2.3f, 0.04f), _banner, default, false);
        }

        private static void Column(Transform parent, Vector3 pos, float height, float diameter)
        {
            Transform c = K.Group("Saeulenrest", parent, pos, Vector3.zero);
            K.Block("Basis", c, new Vector3(0f, -0.2f, 0f), new Vector3(diameter * 1.35f, 0.6f, diameter * 1.35f), _stoneWarm);
            K.Prim(PrimitiveType.Cylinder, "Schaft", c, new Vector3(0f, height * 0.5f, 0f), new Vector3(diameter, height * 0.5f, diameter), Vector3.zero, _stone);
        }

        private static void Npc(Transform parent, string name, string title, Vector3 pos, Material mat, string npcId, float yaw)
        {
            Transform npc = K.Group(name, parent, pos, new Vector3(0f, yaw, 0f));
            GameObject body = K.Prim(PrimitiveType.Capsule, "Body", npc, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), Vector3.zero, mat);
            K.Box("Face", npc, new Vector3(0f, 1.5f, 0.28f), new Vector3(0.35f, 0.1f, 0.1f), _stoneDark, default, false);
            K.SetLayerRecursively(npc.gameObject, LayerMask.NameToLayer(GameLayers.Interactable));
            K.MakeDynamic(body);
            K.Label(npc, new Vector3(0f, 2.15f, 0f), name, title, WorldLabelStyle.Friendly, 40f);
            npc.gameObject.AddComponent<Ashveil.Gameplay.Quests.NpcIdentity>().Configure(npcId);
        }

        /// <summary>
        /// Bogen der alten Aussenmauer, halb im Fels: zwei Arkaden-Geschosse und Attika, in der Mitte das grosse Tor,
        /// darueber Reste eines dritten Geschosses (von Weitem erkennbar). Menschliche Reparaturen in Holz, rote Banner.
        /// </summary>
        private static void BuildFacade(Transform f, float y0)
        {
            var rng = new System.Random(1812);
            const float step = 5f;
            int n = Mathf.RoundToInt(FacadeHalfAngle / step);
            for (int k = -n; k <= n; k++)
            {
                float phi = k * step;
                float rad = phi * Mathf.Deg2Rad;
                var p = new Vector2(FacadeCenter.x - FacadeRadius * Mathf.Cos(rad), FacadeCenter.y + FacadeRadius * Mathf.Sin(rad));
                Vector2 toCenter = (FacadeCenter - p).normalized;
                float yaw = Mathf.Atan2(toCenter.x, toCenter.y) * Mathf.Rad2Deg; // +z der Gruppe zeigt in den Fels
                Transform seg = K.Group($"Joch {k:+0;-0;0}", f, new Vector3(p.x, y0, p.y), new Vector3(0f, yaw, 0f));
                float chord = 2f * FacadeRadius * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad) + 0.25f;
                bool end = Mathf.Abs(k) >= n - 1;
                float top = 22f - (end ? 3f + (float)rng.NextDouble() * 4f : (float)rng.NextDouble() * 0.8f);
                float bottom = Mathf.Min(Ground(p.x, p.y), y0) - y0 - 3f;
                K.Box("Mauer", seg, new Vector3(0f, (top + bottom) * 0.5f, 0f), new Vector3(chord, top - bottom, 3.2f), _stone);
                // Pilaster und Gesimse (Stirnseite zeigt nach -z, zum Platz)
                K.Box("Pilaster", seg, new Vector3(chord * 0.5f, (top + bottom) * 0.5f, -1.8f), new Vector3(0.9f, top - bottom, 0.5f), _stoneWarm);
                K.Box("Gesims unten", seg, new Vector3(0f, 10.2f, -1.75f), new Vector3(chord, 0.6f, 0.4f), _stoneWarm, default, false);
                if (!end) K.Box("Gesims oben", seg, new Vector3(0f, top - 0.8f, -1.75f), new Vector3(chord, 0.7f, 0.4f), _stoneWarm, default, false);
                if (k == 0)
                {
                    // Das Tor: 6,5 m breit, 9 m hoch, dahinter Dunkelheit (Uebergang in die Instanz)
                    K.Box("Tor (Dunkel)", seg, new Vector3(0f, 4.5f, -1.62f), new Vector3(6.5f, 9f, 0.08f), _dark, default, false);
                    K.Box("Torbogen", seg, new Vector3(0f, 9.4f, -1.75f), new Vector3(7.6f, 0.9f, 0.5f), _stoneWarm, default, false);
                    K.Box("Schlussstein", seg, new Vector3(0f, 9.9f, -1.9f), new Vector3(1f, 1.4f, 0.6f), _gold, default, false);
                    K.Box("Fallgitter (hochgezogen)", seg, new Vector3(0f, 8.6f, -1.5f), new Vector3(6.2f, 0.9f, 0.12f), _metal, default, false);
                }
                else
                {
                    K.Box("Arkade unten", seg, new Vector3(0f, 4f, -1.62f), new Vector3(2.8f, 6f, 0.08f), _dark, default, false);
                }
                if (top > 17f) K.Box("Arkade oben", seg, new Vector3(0f, 13.8f, -1.62f), new Vector3(2.4f, 4.6f, 0.08f), _dark, default, false);
                // Drittes Geschoss: nur Reste in der Mitte
                if (Mathf.Abs(k) <= 4 && rng.NextDouble() < 0.7)
                {
                    float extra = 3f + (float)rng.NextDouble() * 6f;
                    K.Box("Oberes Geschoss (Rest)", seg, new Vector3(0f, top + extra * 0.5f, 0.6f), new Vector3(chord * 0.9f, extra, 2.2f), _stoneDark);
                }
            }

            // Menschliche Zutaten: Holzrahmen ums Tor, Banner, Feuerschalen, Beschriftung
            float gx = FacadeCenter.x - FacadeRadius - 2.1f;
            Transform human = K.Group("Tor – heutige Veranstalter", f, new Vector3(gx, y0, FacadeCenter.y), new Vector3(0f, 90f, 0f));
            K.Block("Pfosten L", human, new Vector3(-3.6f, 0f, 0f), new Vector3(0.45f, 9.5f, 0.45f), _wood);
            K.Block("Pfosten R", human, new Vector3(3.6f, 0f, 0f), new Vector3(0.45f, 9.5f, 0.45f), _wood);
            K.Block("Balken", human, new Vector3(0f, 9.5f, 0f), new Vector3(8.4f, 0.55f, 0.55f), _woodDark);
            K.Box("Schild", human, new Vector3(0f, 10.6f, -0.1f), new Vector3(4.2f, 1f, 0.12f), _woodDark, default, false);
            foreach (float side in new[] { -1f, 1f })
            {
                K.Box("Banner", human, new Vector3(side * 5.6f, 14.5f, 0.6f), new Vector3(1.8f, 8f, 0.05f), _banner, default, false);
                Transform bowl = K.Group("Feuerschale", human, new Vector3(side * 6.2f, 0f, -2.2f), Vector3.zero);
                K.Block("Fuss", bowl, Vector3.zero, new Vector3(0.35f, 1.4f, 0.35f), _metal);
                K.Box("Schale", bowl, new Vector3(0f, 1.55f, 0f), new Vector3(1.2f, 0.35f, 1.2f), _metal);
                K.Box("Glut", bowl, new Vector3(0f, 1.78f, 0f), new Vector3(0.9f, 0.12f, 0.9f), _fire, default, false);
                K.PointLight("Feuer", bowl, new Vector3(0f, 2.4f, 0f), new Color(1f, 0.58f, 0.28f), 12f, 2.6f, true);
            }
            K.Label(human, new Vector3(0f, 12.2f, -1f), "Der Ring von Kareth", "Kolosseum · Stufe 14–16 · 1–5 Spieler", WorldLabelStyle.Place, 140f);
        }

        // ================================================================== Arena-Instanzen

        private static void BuildInstances(Transform env)
        {
            Slots.Clear();
            Surfaces.Clear();
            Transform root = K.Group(InstanceGroup, env);
            for (int i = 0; i < SlotCount; i++)
            {
                Transform slot = K.Group($"Kolosseum Platz {i}", root, SlotOrigin(i), Vector3.zero);
                BuildArena(slot, i);
                Slots.Add(slot);
            }

            var director = new GameObject("Dungeon-Direktor Kolosseum");
            director.transform.SetParent(root, false);
            director.AddComponent<DungeonDirector>().Configure(DungeonId, DisplayName,
                "Ihr tretet durch die Kämpferpforte in den Ring. Über euch johlt niemand – noch nicht.",
                "Du trittst in den Ring und folgst deiner Gruppe.",
                "Du verlässt den Ring.",
                "Alle Kampfplätze des Rings sind gerade belegt – versuch es gleich noch einmal.",
                "Der Ring ist noch nicht bereit.");
            // Pass 3: Wellen-Ablauf
            director.AddComponent<ColosseumInstanceHandler>().Configure(ColosseumContentBuilder.EnsureDefinition(), ArenaRadius);

            int exclude = LayerMask.GetMask(GameLayers.Player, GameLayers.Enemy, GameLayers.Interactable, "Ignore Raycast", "UI");
            foreach (Transform slot in Slots)
            {
                var surface = slot.gameObject.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.Volume;
                surface.center = new Vector3(0f, 10f, 0f);
                surface.size = new Vector3(134f, 34f, 134f);
                surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                surface.layerMask = ~exclude;
                surface.overrideVoxelSize = true;
                surface.voxelSize = 0.2f;
                surface.overrideTileSize = true;
                surface.tileSize = 256;
                Surfaces.Add(surface);
            }
        }

        private static void BuildArena(Transform slot, int index)
        {
            var ds = slot.gameObject.AddComponent<DungeonSlot>();
            ds.Configure(DungeonId, index, BoundsCenter, BoundsSize, Arrival, ArrivalYaw, Exit, ExitRadius);
            ds.ConfigureMood(SlotAtmosphere.OpenAir, "an der Kämpferpforte des Rings");

            Transform g = K.Group("Geometrie", slot);
            // Boden (reicht unter Raenge und Torgaenge)
            K.Box("Sandboden", g, new Vector3(0f, -0.5f, 0f), new Vector3(132f, 1f, 132f), _sand);

            var gates = new List<(float bearing, float half)>();
            foreach (var gt in Gates) gates.Add((gt.bearing, GateHalfWidth + WallThick));
            var withDoor = new List<(float, float)>(gates) { (DoorBearing, DoorHalfWidth + WallThick) };

            // Podium (obere Galerie) mit Bruestung, vier Raenge, Aussenmauer
            Ring(g, "Podium", ArenaRadius, PodiumOuter, 0f, PodiumTop, withDoor, _stoneDark, 6f);
            var parapetGaps = new List<(float, float)>(withDoor);
            foreach (float b in StairBearings) parapetGaps.Add((b, 1.8f));
            Ring(g, "Bruestung", ArenaRadius, ArenaRadius + 0.5f, PodiumTop, PodiumTop + 1f, parapetGaps, _stone, 6f);
            for (int k = 1; k <= 4; k++)
            {
                float r0 = PodiumOuter + (k - 1) * 4.5f;
                Ring(g, $"Rang {k}", r0, r0 + 4.5f, 0f, PodiumTop + 3f * k, k == 1 ? withDoor : gates, k % 2 == 0 ? _stone : _stoneWarm, 6f);
            }
            Ring(g, "Aussenmauer", OuterWallR0, OuterWallR1, 0f, OuterWallTop, gates, _stone, 5f);

            foreach (var gt in Gates) BuildGate(slot, g, gt.id, gt.name, gt.bearing);
            BuildDoor(g);
            foreach (float b in StairBearings) BuildStair(g, b);

            // Bodenoeffnung in der Mitte (Gitter, begehbar)
            Transform hatch = K.Group("Bodenoeffnung", g, Vector3.zero, Vector3.zero);
            K.Box("Schacht (dunkel)", hatch, new Vector3(0f, 0.01f, 0f), new Vector3(7f, 0.02f, 7f), _dark, default, false);
            for (int i = -3; i <= 3; i++)
            {
                K.Box("Gitterstab", hatch, new Vector3(i, 0.04f, 0f), new Vector3(0.14f, 0.06f, 7f), _metal, default, false);
                K.Box("Gitterstab", hatch, new Vector3(0f, 0.04f, i), new Vector3(7f, 0.06f, 0.14f), _metal, default, false);
            }
            K.Box("Rahmen", hatch, new Vector3(0f, 0.02f, 0f), new Vector3(7.8f, 0.04f, 7.8f), _stoneDark, default, false);
            AddGate(slot, "boden", "Bodenoeffnung", ArenaGateKind.FloorOpening, Vector3.zero, 0f, new Vector3(0f, 0f, 6f));

            // Galerie-Punkte (Fernkaempfer oben auf dem Podium)
            foreach (float b in GalleryBearings)
                AddGate(slot, "galerie" + Mathf.RoundToInt(b), b < 180f ? "Galerie Nordost" : "Galerie Suedwest", ArenaGateKind.Gallery,
                    Polar(b, 37.3f, PodiumTop), b + 180f, new Vector3(0f, 0f, 0.5f));

            // Deckung: vier alte Saeulen (zwei gebrochen)
            float[] heights = { 8f, 4.2f, 7.2f, 3f };
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = Polar(45f + i * 90f, 17f, 0f);
                K.Block("Saeulenbasis", g, c + Vector3.down * 0.2f, new Vector3(3.2f, 0.7f, 3.2f), _stoneWarm, i * 12f);
                K.Prim(PrimitiveType.Cylinder, "Saeule", g, c + Vector3.up * (heights[i] * 0.5f + 0.5f), new Vector3(2.4f, heights[i] * 0.5f, 2.4f), Vector3.zero, _stone);
                if (heights[i] < 5f)
                    K.Prim(PrimitiveType.Cylinder, "Trommel (gestuerzt)", g, c + Polar(i * 70f + 20f, 3.6f, 1.1f), new Vector3(2.2f, 1.1f, 2.2f),
                        new Vector3(90f, i * 70f + 20f, 0f), _stone);
            }

            Dress(g);
        }

        /// <summary>Kreisring aus Quadern zwischen r0 und r1, mit Luecken (Peilung, halbe Breite in m).</summary>
        private static void Ring(Transform parent, string name, float r0, float r1, float y0, float y1, List<(float bearing, float half)> gaps,
            Material mat, float maxStep)
        {
            Transform grp = K.Group(name, parent);
            float rm = (r0 + r1) * 0.5f;
            // Luecken als Winkelintervalle (am inneren Radius exakt, damit Durchgaenge die volle Breite haben)
            var cuts = new List<(float a, float b)>();
            foreach ((float bearing, float half) in gaps)
            {
                float d = Mathf.Asin(Mathf.Clamp01(half / r0)) * Mathf.Rad2Deg;
                cuts.Add((bearing - d, bearing + d));
            }
            cuts.Sort((x, y) => x.a.CompareTo(y.a));
            var arcs = new List<(float a, float b)>();
            if (cuts.Count == 0) arcs.Add((0f, 360f));
            else
            {
                for (int i = 0; i < cuts.Count; i++)
                {
                    float a = cuts[i].b, b = i + 1 < cuts.Count ? cuts[i + 1].a : cuts[0].a + 360f;
                    if (b - a > 0.2f) arcs.Add((a, b));
                }
            }
            foreach ((float a, float b) in arcs)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt((b - a) / maxStep));
                float st = (b - a) / n;
                for (int i = 0; i < n; i++)
                {
                    float mid = a + (i + 0.5f) * st;
                    float chord = 2f * r1 * Mathf.Sin(st * 0.5f * Mathf.Deg2Rad) + 0.05f;
                    K.Box(name, grp, Polar(mid, rm, (y0 + y1) * 0.5f), new Vector3(chord, y1 - y0, r1 - r0), mat, new Vector3(0f, mid, 0f));
                }
            }
        }

        private static void BuildGate(Transform slot, Transform g, string id, string name, float bearing)
        {
            Transform t = K.Group(name, g, Vector3.zero, new Vector3(0f, bearing, 0f)); // +z zeigt nach aussen
            float side = GateHalfWidth + WallThick * 0.5f;
            float len = OuterWallR1 - ArenaRadius;
            foreach (float s in new[] { -1f, 1f })
            {
                // Torwange (Turm) vom Arenarand bis zur Aussenmauer
                K.Box("Wange", t, new Vector3(s * side, (PodiumTop + 12f) * 0.5f, ArenaRadius + len * 0.5f), new Vector3(WallThick, PodiumTop + 12f, len), _stoneDark);
                K.Box("Wange aussen", t, new Vector3(s * side, OuterWallTop * 0.5f, OuterWallR0 + 1.25f), new Vector3(WallThick, OuterWallTop, 2.5f), _stone);
            }
            K.Box("Torbogen", t, new Vector3(0f, GateHeight + 1.5f, ArenaRadius + 1f), new Vector3(GateHalfWidth * 2f + WallThick * 2f, 3f, 2f), _stoneWarm);
            K.Box("Schlussstein", t, new Vector3(0f, GateHeight + 1.2f, ArenaRadius - 0.1f), new Vector3(1.1f, 1.6f, 0.4f), _gold, default, false);
            // Torkammer hinten: geschlossen, Fallgitter davor (kuenftige Gegner stehen dahinter)
            K.Box("Torkammer", t, new Vector3(0f, 6f, GateEndR), new Vector3(GateHalfWidth * 2f + WallThick * 2f, 12f, 2f), _stoneDark);
            for (int i = -3; i <= 3; i++)
                K.Box("Fallgitter", t, new Vector3(i * 1.2f, GateHeight * 0.5f, GateEndR - 2f), new Vector3(0.14f, GateHeight, 0.14f), _metal, default, false);
            K.Box("Fallgitter Riegel", t, new Vector3(0f, GateHeight - 0.2f, GateEndR - 2f), new Vector3(GateHalfWidth * 2f, 0.3f, 0.2f), _metal, default, false);
            K.Box("Torkammer (dunkel)", t, new Vector3(0f, GateHeight * 0.5f, GateEndR - 1.05f), new Vector3(GateHalfWidth * 2f, GateHeight, 0.05f), _dark, default, false);
            K.PointLight("Fackel Torgang", t, new Vector3(GateHalfWidth - 0.4f, 4f, GateSpawnR), new Color(1f, 0.6f, 0.3f), 9f, 1.5f, true);

            AddGate(slot, id, name, ArenaGateKind.Gate, Polar(bearing, GateSpawnR, 0f), bearing + 180f, new Vector3(0f, 0f, GateSpawnR - GateEntryR));

            // Feuerschalen links und rechts auf dem Podium
            foreach (float s in new[] { -1f, 1f })
            {
                float b = bearing + s * 12f;
                Vector3 p = Polar(b, 37.6f, PodiumTop);
                Transform bowl = K.Group("Feuerschale", g, p, Vector3.zero);
                K.Block("Fuss", bowl, Vector3.zero, new Vector3(0.35f, 1.2f, 0.35f), _metal);
                K.Box("Schale", bowl, new Vector3(0f, 1.35f, 0f), new Vector3(1.1f, 0.3f, 1.1f), _metal);
                K.Box("Glut", bowl, new Vector3(0f, 1.55f, 0f), new Vector3(0.8f, 0.1f, 0.8f), _fire, default, false);
                if (s > 0f) K.PointLight("Feuer", bowl, new Vector3(0f, 2.2f, 0f), new Color(1f, 0.58f, 0.28f), 14f, 2.2f, true);
            }
        }

        private static void AddGate(Transform slot, string id, string name, ArenaGateKind kind, Vector3 local, float yaw, Vector3 entryLocal)
        {
            var go = new GameObject("Arena-Eingang " + name);
            go.transform.SetParent(slot, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<ArenaGate>().Configure(id, name, kind, entryLocal);
        }

        /// <summary>Kaempferpforte: schmaler Gang durch Podium und ersten Rang; Ausgang (Radius) im Gang, Ankunft davor im Sand.</summary>
        private static void BuildDoor(Transform g)
        {
            Transform t = K.Group("Kaempferpforte", g, Vector3.zero, new Vector3(0f, DoorBearing, 0f));
            float side = DoorHalfWidth + WallThick * 0.5f;
            float r1 = PodiumOuter + 4.5f;
            float h = PodiumTop + 3f;
            foreach (float s in new[] { -1f, 1f })
                K.Box("Wange", t, new Vector3(s * side, h * 0.5f, (ArenaRadius + r1) * 0.5f), new Vector3(WallThick, h, r1 - ArenaRadius), _stoneDark);
            K.Box("Sturz", t, new Vector3(0f, 4f, ArenaRadius + 1f), new Vector3(DoorHalfWidth * 2f + WallThick * 2f, 1f, 2f), _stoneWarm);
            K.Box("Tuer (dunkel)", t, new Vector3(0f, 2f, r1 + 0.02f), new Vector3(DoorHalfWidth * 2f, 4f, 0.05f), _dark, default, false);
            K.PointLight("Laterne Pforte", t, new Vector3(DoorHalfWidth - 0.4f, 3f, ArenaRadius + 3f), new Color(1f, 0.72f, 0.42f), 8f, 1.4f, true);
            K.Label(t, new Vector3(0f, 5.2f, ArenaRadius - 0.5f), "Kämpferpforte", "Ausgang", WorldLabelStyle.Place, 30f, 0.7f);
        }

        /// <summary>Treppe vom Sand hinauf aufs Podium (obere Galerie): Rampe entlang der Wand, Absatz, Luecke in der Bruestung.</summary>
        private static void BuildStair(Transform g, float bearing)
        {
            const float rr = 32f;
            const float angle = 30f;
            float run = PodiumTop / Mathf.Tan(angle * Mathf.Deg2Rad);
            float len = PodiumTop / Mathf.Sin(angle * Mathf.Deg2Rad);
            Vector3 landing = Polar(bearing, (30.4f + ArenaRadius) * 0.5f, 0f);
            K.Block("Treppenabsatz", g, landing, new Vector3(3f, PodiumTop, ArenaRadius - 30.4f + 0.2f), _stoneWarm, bearing);
            float br = bearing * Mathf.Deg2Rad;
            var up = new Vector3(Mathf.Cos(br), 0f, -Mathf.Sin(br)); // tangential, mit wachsender Peilung
            Vector3 low = Polar(bearing, rr, 0f) - up * (1.5f + run);
            float yaw = Mathf.Atan2(up.x, up.z) * Mathf.Rad2Deg;
            K.Ramp("Treppe", g, low, yaw, angle, len, 3f, _stoneWarm);
        }

        /// <summary>Waechterstatuen ueber dem Nordtor, Banner an der Aussenmauer, Holzgeruest als Reparatur.</summary>
        private static void Dress(Transform g)
        {
            Transform d = K.Group("Ausstattung", g);
            foreach (float s in new[] { -1f, 1f })
            {
                Transform st = K.Group("Waechterstatue", d, Polar(s * 14f, (OuterWallR0 + OuterWallR1) * 0.5f, OuterWallTop), new Vector3(0f, s * 14f + 180f, 0f));
                K.Block("Sockel", st, Vector3.zero, new Vector3(3.4f, 1.2f, 2.4f), _stoneWarm);
                K.Block("Koerper", st, new Vector3(0f, 1.2f, 0f), new Vector3(2.4f, 7f, 1.6f), _stone);
                K.Block("Kopf", st, new Vector3(0f, 8.2f, 0f), new Vector3(1.2f, 1.5f, 1.2f), _stone);
                K.Box("Schwert", st, new Vector3(0f, 4.2f, -1f), new Vector3(0.4f, 7.5f, 0.2f), _gold, default, false);
            }
            for (int i = 0; i < 8; i++)
            {
                float b = 22.5f + i * 45f;
                if (Mathf.Abs(Mathf.DeltaAngle(b, DoorBearing)) < 8f) continue;
                K.Box("Banner", d, Polar(b, OuterWallR0 - 0.08f, OuterWallTop - 5.5f), new Vector3(2f, 9f, 0.05f), _banner, new Vector3(0f, b, 0f), false);
            }
            // Holzgeruest an einem Mauerstueck (die Menschen haben notduerftig repariert)
            Transform sc = K.Group("Geruest", d, Vector3.zero, new Vector3(0f, 300f, 0f));
            for (int i = 0; i < 3; i++)
            {
                K.Box("Stange", sc, new Vector3(-3f + i * 3f, OuterWallTop * 0.4f, OuterWallR0 - 1.2f), new Vector3(0.2f, OuterWallTop * 0.8f, 0.2f), _wood, default, false);
                K.Box("Brett", sc, new Vector3(0f, 6f + i * 5f, OuterWallR0 - 1f), new Vector3(7f, 0.15f, 1f), _woodDark, default, false);
            }
        }

        // ================================================================== NavMesh

        /// <summary>NavMesh von Platz 0 backen und an allen Plaetzen einsetzen (nach dem Speichern der Szene).</summary>
        public static void BakeNavMesh()
        {
            if (Surfaces.Count == 0 || Surfaces[0] == null)
            {
                // nach einem Domain-Reload: Flaechen aus der Szene holen
                Surfaces.Clear();
                foreach (DungeonSlot s in Object.FindObjectsByType<DungeonSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (s.DungeonId == DungeonId && s.GetComponent<NavMeshSurface>() != null) Surfaces.Add(s.GetComponent<NavMeshSurface>());
                Surfaces.Sort((a, b) => a.GetComponent<DungeonSlot>().Index.CompareTo(b.GetComponent<DungeonSlot>().Index));
            }
            if (Surfaces.Count == 0) return;
            float t0 = Time.realtimeSinceStartup;
            AssetDatabase.DeleteAsset(NavMeshAssetPath);
            Physics.SyncTransforms();
            NavMeshSurface first = Surfaces[0];
            first.BuildNavMesh();
            if (first.navMeshData == null)
            {
                Debug.LogWarning("[Ashveil] NavMesh Kolosseum: Backen fehlgeschlagen.");
                return;
            }
            AssetDatabase.CreateAsset(first.navMeshData, NavMeshAssetPath);
            foreach (NavMeshSurface s in Surfaces)
            {
                if (s == null) continue;
                s.navMeshData = first.navMeshData;
                EditorUtility.SetDirty(s);
            }
            Debug.Log($"[Ashveil] NavMesh Kolosseum gebacken ({Time.realtimeSinceStartup - t0:0.0} s), an {Surfaces.Count} Plaetzen.");
        }
    }
}
