using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.Core;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Quests;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.EditorTools.Quality
{
    /// <summary>Ein Befund der Weltprüfung.</summary>
    [Serializable]
    public sealed class WqFinding
    {
        public string severity;
        public string category;
        public string code;
        public string message;
        public string where;
        public bool hasPosition;
        public Vector3 position;
    }

    /// <summary>Kennzahlen einer Einheit (Szene, Weltzelle oder Raster-Kachel) für das Leistungsbudget (§17).</summary>
    [Serializable]
    public sealed class WqUnit
    {
        public string name;
        public int gameObjects, renderers, meshRenderers, shadowCasters, smallShadowCasters, lights, realtimeLights, particleSystems;
        public int colliders, meshColliders, npcs, enemySpawns, enemies, materials, textures, drawCallsEstimate, navMeshTriangles;
        public long triangles, meshColliderTriangles, textureBytes, meshBytes;
    }

    /// <summary>Gesamtbericht einer Prüfung; wird als Text und JSON geschrieben.</summary>
    [Serializable]
    public sealed class WqReport
    {
        public string scene, created, unity;
        public int info, warning, critical;
        public List<WqFinding> findings = new List<WqFinding>();
        public List<WqUnit> units = new List<WqUnit>();
        public List<string> notes = new List<string>();

        [NonSerialized] private readonly Dictionary<string, int> _perCode = new Dictionary<string, int>();
        [NonSerialized] private readonly Dictionary<string, (WqSeverity sev, string cat, int extra)> _suppressed =
            new Dictionary<string, (WqSeverity, string, int)>();

        /// <summary>Je Befundart höchstens so viele Einzelzeilen; der Rest wird am Ende zusammengefasst.</summary>
        public const int MaxPerCode = 25;

        public void Add(WqSeverity sev, string category, string code, string message, Vector3? pos = null, string where = "")
        {
            if (sev == WqSeverity.Critical) critical++;
            else if (sev == WqSeverity.Warning) warning++;
            else info++;
            _perCode.TryGetValue(code, out int n);
            _perCode[code] = n + 1;
            if (n >= MaxPerCode)
            {
                _suppressed.TryGetValue(code, out var s);
                _suppressed[code] = (sev > s.sev ? sev : s.sev, category, s.extra + 1);
                return;
            }
            findings.Add(new WqFinding
            {
                severity = Label(sev), category = category, code = code, message = message, where = where ?? "",
                hasPosition = pos.HasValue, position = pos ?? Vector3.zero
            });
        }

        /// <summary>Wie oft eine Befundart gemeldet wurde (auch die nicht einzeln aufgeführten).</summary>
        public int CountOf(string code) => _perCode.TryGetValue(code, out int n) ? n : 0;

        /// <summary>Zusammenfassungszeilen für nicht einzeln aufgeführte Befunde (sie sind in den Summen schon enthalten).</summary>
        public void Finish()
        {
            foreach (KeyValuePair<string, (WqSeverity sev, string cat, int extra)> kv in _suppressed)
                findings.Add(new WqFinding
                {
                    severity = Label(kv.Value.sev), category = kv.Value.cat, code = kv.Key, where = "",
                    message = $"… und {kv.Value.extra} weitere Befunde dieser Art (nur die ersten {MaxPerCode} stehen einzeln da)"
                });
            _suppressed.Clear();
        }

        public static string Label(WqSeverity sev) => sev == WqSeverity.Critical ? "CRITICAL" : sev == WqSeverity.Warning ? "WARNING" : "INFO";
    }

    /// <summary>
    /// ASHVEIL WORLD QUALITY VALIDATOR V1 (Werkzeug-Direktive Max 05.10., §16–18). Prüft NUR LESEND die geöffnete Szene:
    /// Platzierung (nichts schwebt, nichts liegt vergraben), Kollision (keine unsichtbaren Wände, keine Objekte zum
    /// Hindurchlaufen), Navigation (NavMesh-Inseln, Erreichbarkeit von Ankern, Questorten, Eingängen, NPCs), Wege (Steigung,
    /// Bäume im Weg), Wasser (offene Ränder, Tiefen), Assets (Shader, Texturen, LOD) und Leistungsbudget je Szene, Weltzelle
    /// und 256-m-Kachel. Grenzwerte nur aus Messungen (<see cref="WorldQualityBudgets"/>), sonst nur INFO.
    /// Bericht: Logs/Ashveil/WorldQuality/WQ_&lt;Szene&gt;_&lt;Zeit&gt;.txt und .json. Ändert weder Szene noch Assets.
    /// </summary>
    public static class WorldQualityValidator
    {
        /// <summary>Darf schweben bzw. hängt an etwas (Dächer, Lampen, Schilder, Brücken, Boote …).</summary>
        private static readonly string[] FloatExempt =
        {
            "wasser", "water", "salzsee", "dach", "first", "lampe", "laterne", "licht", "schild", "fahne", "banner", "wimpel",
            "seil", "leine", "rauch", "funken", "vogel", "brücke", "bruecke", "steg", "bogen", "kette", "krone", "blatt", "plane",
            "himmel", "wolke", "mond", "sonne", "ausguck", "balken", "sturz", "glocke", "netz", "boot", "kahn", "floss", "boje"
        };

        /// <summary>Braucht keinen Collider (Bewuchs, Effekte, Stoff, Wasser …).</summary>
        private static readonly string[] ColliderExempt =
        {
            "gras", "busch", "farn", "strauch", "blume", "moos", "laub", "efeu", "ranke", "decal", "spur", "rauch", "licht",
            "wasser", "water", "plane", "fahne", "banner", "seil", "netz", "schatten", "funken", "glut", "staub", "nebel"
        };

        /// <summary>Ortsnamen, die zufällig wie ein Stichwort enden („Untersteg“ ist ein Dorf, kein Steg).</summary>
        private static readonly string[] PlaceNames = { "untersteg", "glockenrast", "aschenwacht", "eschenfurt", "kareth" };

        [MenuItem("Ashveil/Welt/Weltqualität prüfen (offene Szene, nur lesen)", priority = 502)]
        public static void RunMenu()
        {
            WqReport r = Run();
            string path = Write(r);
            string msg = $"[Ashveil] Weltqualität {r.scene}: {r.critical} kritisch · {r.warning} Warnungen · {r.info} Hinweise → {path}";
            if (r.critical > 0) Debug.LogWarning(msg);
            else Debug.Log(msg);
            if (!Application.isBatchMode &&
                EditorUtility.DisplayDialog("Ashveil – Weltqualität",
                    $"{r.scene}\n\n{r.critical} kritisch · {r.warning} Warnungen · {r.info} Hinweise\n\nBericht:\n{path}", "Bericht öffnen", "OK"))
                EditorUtility.OpenWithDefaultApp(path);
        }

        // ================================================================================================== Ablauf

        public static WqReport Run()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var scenes = new List<string>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (s.isLoaded) scenes.Add(s.name);
            }
            var r = new WqReport
            {
                scene = scenes.Count == 0 ? "(keine Szene)" : string.Join("+", scenes),
                created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                unity = Application.unityVersion
            };
            try
            {
                Progress("Szene lesen", 0.02f);
                Ctx ctx = Ctx.Collect();
                r.notes.Add($"{ctx.Renderers.Count} Renderer, {ctx.Colliders.Count} Collider, {ctx.Terrains.Length} Terrains" +
                            (ctx.Plan != null ? " · Kareth-v4-Entwurfsdaten gelesen (Wege, Lager-Spawn)" : ""));
                Progress("Platzierung", 0.10f);
                Safe(r, "Platzierung", () => CheckPlacement(ctx, r));
                Progress("Kollision", 0.25f);
                Safe(r, "Kollision", () => CheckColliders(ctx, r));
                Progress("Navigation", 0.40f);
                Safe(r, "Navigation", () => CheckNavigation(ctx, r));
                Progress("Wege", 0.50f);
                Safe(r, "Wege", () => CheckRoadsAndTrees(ctx, r));
                Progress("Wasser", 0.60f);
                Safe(r, "Wasser", () => CheckWater(ctx, r));
                Progress("Assets", 0.70f);
                Safe(r, "Assets", () => CheckAssets(ctx, r));
                Progress("Budget", 0.85f);
                Safe(r, "Budget", () => CheckBudgets(ctx, r));
                Progress("Weltdaten", 0.95f);
                Safe(r, "Weltdaten", () => CheckWorldData(r));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            r.Finish();
            r.notes.Add($"Laufzeit {sw.Elapsed.TotalSeconds:0.0} s");
            return r;
        }

        private static void Progress(string step, float t)
        {
            if (!Application.isBatchMode) EditorUtility.DisplayProgressBar("Ashveil – Weltqualität (nur lesen)", step + " …", t);
        }

        private static void Safe(WqReport r, string category, Action check)
        {
            try { check(); }
            catch (Exception e)
            {
                r.Add(WqSeverity.Warning, category, "CHECK_FAILED", $"Prüfung abgebrochen: {e.GetType().Name}: {e.Message}");
                Debug.LogException(e);
            }
        }

        public static string Write(WqReport r)
        {
            string dir = ProjectPaths.Logs("WorldQuality");
            Directory.CreateDirectory(dir);
            string stem = "WQ_" + Sanitize(r.scene) + "_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm");
            string txt = Path.Combine(dir, stem + ".txt");
            string text = ToText(r);
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(txt, text, utf8);
            File.WriteAllText(Path.Combine(dir, stem + ".json"), JsonUtility.ToJson(r, true), utf8);
            File.WriteAllText(Path.Combine(dir, "WQ_" + Sanitize(r.scene) + "_latest.txt"), text, utf8);
            return txt;
        }

        public static string ToText(WqReport r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ASHVEIL World Quality Validator V1 · {r.created} · Unity {r.unity}");
            sb.AppendLine($"Szene: {r.scene}");
            sb.AppendLine($"Ergebnis: {r.critical} CRITICAL · {r.warning} WARNING · {r.info} INFO");
            foreach (string n in r.notes) sb.AppendLine("  " + n);
            sb.AppendLine();
            foreach (string sev in new[] { "CRITICAL", "WARNING", "INFO" })
            {
                List<WqFinding> list = r.findings.Where(f => f.severity == sev).ToList();
                if (list.Count == 0) continue;
                sb.AppendLine($"== {sev} ({list.Count} Zeilen)");
                foreach (IGrouping<string, WqFinding> g in list.GroupBy(f => f.category))
                {
                    sb.AppendLine($"  [{g.Key}]");
                    foreach (WqFinding f in g)
                        sb.AppendLine($"    {f.code,-24} {f.message}" +
                                      (f.hasPosition ? $"  @ {f.position.x:0}/{f.position.y:0.0}/{f.position.z:0}" : "") +
                                      (string.IsNullOrEmpty(f.where) ? "" : $"  · {f.where}"));
                }
                sb.AppendLine();
            }
            sb.AppendLine("== Budget je Einheit (Grenzen: Scripts/Editor/World/Quality/WorldQualityBudgets.cs)");
            foreach (WqUnit u in r.units)
                sb.AppendLine($"  {u.name}: {u.meshRenderers} Mesh-Renderer ({u.renderers} Renderer gesamt) · {u.triangles / 1000.0:0} k Dreiecke · " +
                              $"{u.shadowCasters} Schattenwerfer ({u.smallShadowCasters} klein) · {u.realtimeLights}/{u.lights} Echtzeitlichter · " +
                              $"{u.particleSystems} Partikelsysteme · {u.colliders} Collider ({u.meshColliders} Netz, {u.meshColliderTriangles / 1000.0:0} k Dreiecke) · " +
                              $"{u.npcs} NPCs · {u.enemySpawns} Gegnergebiete ({u.enemies} Gegner) · {u.materials} Materialien · {u.textures} Texturen " +
                              $"({u.textureBytes / 1048576.0:0} MB, Editor-Wert) · Netze ≈ {u.meshBytes / 1048576.0:0} MB · " +
                              $"≈ {u.drawCallsEstimate} Draw Calls ohne Batching · {u.navMeshTriangles} NavMesh-Dreiecke · {u.gameObjects} GameObjects");
            return sb.ToString();
        }

        private static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; t != null && i < 4; i++, t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>Trägt das Objekt oder sein direkter Elternteil eines der Stichworte im Namen (<see cref="WorldQualityRules.NameHasKeyword"/>)?</summary>
        private static bool HasKeyword(Transform t, string[] keys) =>
            WorldQualityRules.NameHasKeyword(t.name, keys, PlaceNames) ||
            (t.parent != null && WorldQualityRules.NameHasKeyword(t.parent.name, keys, PlaceNames));

        private static T[] Find<T>() where T : UnityEngine.Object => UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Exclude);

        private static Mesh MeshOf(Renderer r)
        {
            if (r == null) return null;
            if (r is SkinnedMeshRenderer sk) return sk.sharedMesh;
            return r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
        }

        private static long Triangles(Mesh m)
        {
            if (m == null) return 0;
            long t = 0;
            for (int s = 0; s < m.subMeshCount; s++) t += m.GetIndexCount(s) / 3;
            return t;
        }

        private static float MaxSize(Bounds b) => Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));

        // ================================================================================================== Szenendaten

        private sealed class Ctx
        {
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly List<Collider> Colliders = new List<Collider>();
            public Terrain[] Terrains = new Terrain[0];
            public Light[] Lights = new Light[0];
            public ParticleSystem[] Particles = new ParticleSystem[0];
            public NpcIdentity[] Npcs = new NpcIdentity[0];
            public EnemySpawnArea[] Spawns = new EnemySpawnArea[0];
            public Vector3[] TransformPositions = new Vector3[0];
            public NavMeshTriangulation Nav;
            public KarethV4Plan Plan;
            public BoundsGrid RendererGrid, ColliderGrid;

            public static Ctx Collect()
            {
                var c = new Ctx();
                foreach (Renderer r in Find<Renderer>())
                {
                    if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r is BillboardRenderer) continue;
                    if (r.GetType().Name == "VFXRenderer") continue;
                    if (r.GetComponentInParent<Terrain>() != null) continue;
                    c.Renderers.Add(r);
                }
                foreach (Collider col in Find<Collider>())
                    if (col.enabled && !(col is TerrainCollider) && !(col is CharacterController)) c.Colliders.Add(col);
                c.Terrains = Terrain.activeTerrains.Where(t => t != null && t.terrainData != null).ToArray();
                c.Lights = Find<Light>().Where(l => l.enabled).ToArray();
                c.Particles = Find<ParticleSystem>();
                c.Npcs = Find<NpcIdentity>();
                c.Spawns = Find<EnemySpawnArea>();
                c.TransformPositions = Find<Transform>().Select(t => t.position).ToArray();
                c.Nav = NavMesh.CalculateTriangulation();
                c.RendererGrid = new BoundsGrid(c.Renderers.Select(x => x.bounds).ToList());
                c.ColliderGrid = new BoundsGrid(c.Colliders.Select(x => x.bounds).ToList());
                try
                {
                    // Entwurfsdaten nur lesen (nicht über KarethV4Builder.LoadPlan, das einen statischen Zustand setzt)
                    string planPath = KarethV4Builder.DataFolder + "/KarethV4_Plan.json";
                    if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path == KarethV4Builder.ScenePath && File.Exists(planPath))
                        c.Plan = JsonUtility.FromJson<KarethV4Plan>(File.ReadAllText(planPath));
                }
                catch (Exception)
                {
                    c.Plan = null;
                }
                return c;
            }

            public float? Ground(float x, float z) => WorldGround.TrySample(new Vector3(x, 0f, z), out float h) ? h : (float?)null;

            /// <summary>Höchster und tiefster Bodenpunkt unter der Grundfläche (Mitte und vier Punkte auf 40 % zur Ecke).</summary>
            public bool GroundRange(Bounds b, out float min, out float max)
            {
                min = float.MaxValue;
                max = float.MinValue;
                int n = 0;
                for (int i = 0; i < 5; i++)
                {
                    float ox = i == 0 ? 0f : (i == 1 || i == 4 ? -0.4f : 0.4f);
                    float oz = i == 0 ? 0f : (i <= 2 ? -0.4f : 0.4f);
                    float? g = Ground(b.center.x + ox * b.size.x, b.center.z + oz * b.size.z);
                    if (!g.HasValue) continue;
                    n++;
                    min = Mathf.Min(min, g.Value);
                    max = Mathf.Max(max, g.Value);
                }
                return n > 0;
            }
        }

        /// <summary>Raster über die Grundflächen (x/z) von Bounds, für schnelle Nachbarschaftsabfragen.</summary>
        private sealed class BoundsGrid
        {
            private const float Cell = 4f, LargeFootprint = 64f;
            private readonly List<Bounds> _all;
            private readonly Dictionary<Vector2Int, List<int>> _cells = new Dictionary<Vector2Int, List<int>>();
            private readonly List<int> _large = new List<int>();

            public BoundsGrid(List<Bounds> all)
            {
                _all = all;
                for (int i = 0; i < all.Count; i++)
                {
                    Bounds b = all[i];
                    if (b.size.x > LargeFootprint || b.size.z > LargeFootprint)
                    {
                        _large.Add(i);
                        continue;
                    }
                    int x0 = Mathf.FloorToInt(b.min.x / Cell), x1 = Mathf.FloorToInt(b.max.x / Cell);
                    int z0 = Mathf.FloorToInt(b.min.z / Cell), z1 = Mathf.FloorToInt(b.max.z / Cell);
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                        {
                            var k = new Vector2Int(x, z);
                            if (!_cells.TryGetValue(k, out List<int> list)) _cells[k] = list = new List<int>();
                            list.Add(i);
                        }
                }
            }

            public Bounds this[int i] => _all[i];

            public IEnumerable<int> Near(Vector3 p)
            {
                if (_cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell)), out List<int> list))
                    foreach (int i in list) yield return i;
                foreach (int i in _large) yield return i;
            }

            public IEnumerable<int> Overlapping(Bounds b)
            {
                var seen = new HashSet<int>();
                int x0 = Mathf.FloorToInt(b.min.x / Cell), x1 = Mathf.FloorToInt(b.max.x / Cell);
                int z0 = Mathf.FloorToInt(b.min.z / Cell), z1 = Mathf.FloorToInt(b.max.z / Cell);
                if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 4096)
                {
                    for (int i = 0; i < _all.Count; i++)
                        if (_all[i].Intersects(b)) yield return i;
                    yield break;
                }
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                        if (_cells.TryGetValue(new Vector2Int(x, z), out List<int> list))
                            foreach (int i in list)
                                if (seen.Add(i) && _all[i].Intersects(b)) yield return i;
                foreach (int i in _large)
                    if (seen.Add(i) && _all[i].Intersects(b)) yield return i;
            }
        }

        // ================================================================================================== Platzierung

        private static void CheckPlacement(Ctx ctx, WqReport r)
        {
            int checkedN = 0, floating = 0, buried = 0;
            for (int i = 0; i < ctx.Renderers.Count; i++)
            {
                if (!(ctx.Renderers[i] is MeshRenderer ren)) continue;   // Figuren und Tiere (Skinned) bewegen sich
                Bounds b = ren.bounds;
                if (b.size.x > 40f || b.size.z > 40f) continue;           // Felskacheln, Gelände-Netze
                if (HasKeyword(ren.transform, FloatExempt)) continue;
                if (!ctx.GroundRange(b, out float gMin, out float gMax)) continue;
                checkedN++;
                if (WorldQualityRules.IsBuried(b.max.y, gMin))
                {
                    buried++;
                    r.Add(WqSeverity.Warning, "Platzierung", "PROP_BURIED", $"liegt ganz unter dem Gelände ({gMin - b.max.y:0.00} m)", b.center, PathOf(ren.transform));
                    continue;
                }
                if (!WorldQualityRules.IsFloating(b.min.y, gMax, false, WorldQualityBudgets.FloatingTolerance)) continue;
                if (Supported(ctx, i, b)) continue;
                floating++;
                r.Add(WqSeverity.Warning, "Platzierung", "PROP_FLOATING", $"schwebt {b.min.y - gMax:0.00} m über dem Boden, nichts trägt es", b.center, PathOf(ren.transform));
            }
            r.notes.Add($"Platzierung: {checkedN} Objekte geprüft, {floating} schwebend, {buried} vergraben (Regel „Sinn und Gelände“)");
        }

        /// <summary>Steht unter der Unterkante ein anderes Objekt (Renderer oder Collider)? Fünf Proben knapp darunter.</summary>
        private static bool Supported(Ctx ctx, int self, Bounds b)
        {
            float y = b.min.y - 0.12f;
            for (int i = 0; i < 5; i++)
            {
                float ox = i == 0 ? 0f : (i == 1 || i == 4 ? -0.6f : 0.6f);
                float oz = i == 0 ? 0f : (i <= 2 ? -0.6f : 0.6f);
                var p = new Vector3(b.center.x + ox * b.extents.x, y, b.center.z + oz * b.extents.z);
                foreach (int j in ctx.RendererGrid.Near(p))
                {
                    if (j == self) continue;
                    Bounds o = ctx.RendererGrid[j];
                    o.Expand(0.2f);
                    if (o.Contains(p)) return true;
                }
                foreach (int j in ctx.ColliderGrid.Near(p))
                {
                    Bounds o = ctx.ColliderGrid[j];
                    o.Expand(0.2f);
                    if (o.Contains(p)) return true;
                }
            }
            return false;
        }

        // ================================================================================================== Kollision

        private static void CheckColliders(Ctx ctx, WqReport r)
        {
            int missing = 0;
            foreach (Renderer ren in ctx.Renderers)
            {
                if (!(ren is MeshRenderer)) continue;
                Bounds b = ren.bounds;
                float width = Mathf.Max(b.size.x, b.size.z);
                if (width < 1.5f || b.size.y < 1.0f || width > 40f) continue;
                if (HasKeyword(ren.transform, ColliderExempt)) continue;
                if (ctx.GroundRange(b, out _, out float gMax) && b.min.y > gMax + 1.0f) continue;   // nicht am Boden
                if (ren.GetComponentInParent<Collider>() != null || ren.GetComponentInChildren<Collider>() != null) continue;
                float size = MaxSize(b);
                float vol = Mathf.Max(b.size.x * b.size.y * b.size.z, 1e-3f);
                bool covered = false;
                foreach (int j in ctx.ColliderGrid.Overlapping(b))
                {
                    Bounds c = ctx.ColliderGrid[j];
                    if (MaxSize(c) > 4f * size + 4f) continue;   // Felskacheln und Ähnliches decken nichts ab
                    Vector3 d = Vector3.Min(b.max, c.max) - Vector3.Max(b.min, c.min);
                    if (d.x > 0f && d.y > 0f && d.z > 0f && d.x * d.y * d.z >= 0.3f * vol)
                    {
                        covered = true;
                        break;
                    }
                }
                if (covered) continue;
                missing++;
                r.Add(WqSeverity.Warning, "Kollision", "COLLIDER_MISSING", $"kein Collider – man läuft hindurch ({width:0.0} × {b.size.y:0.0} m)", b.center, PathOf(ren.transform));
            }

            int invisible = 0;
            foreach (Collider col in ctx.Colliders)
            {
                string where = PathOf(col.transform);
                if (col is MeshCollider mc)
                {
                    if (mc.sharedMesh == null)
                    {
                        r.Add(WqSeverity.Critical, "Kollision", "MESHCOLLIDER_EMPTY", "MeshCollider ohne Netz", col.bounds.center, where);
                        continue;
                    }
                    long tris = Triangles(mc.sharedMesh);
                    if (tris >= WorldQualityBudgets.MeshColliderTrianglesWarning)
                        r.Add(WqSeverity.Warning, "Kollision", "MESHCOLLIDER_HEAVY", $"Kollisionsnetz mit {tris / 1000.0:0} k Dreiecken", col.bounds.center, where);
                    if (!mc.convex && col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic)
                        r.Add(WqSeverity.Critical, "Kollision", "MESHCOLLIDER_DYNAMIC", "nicht-konvexer MeshCollider an bewegtem Rigidbody", col.bounds.center, where);
                }
                if (col.isTrigger) continue;
                if (col.TryGetComponent(out Renderer own) && own.enabled) continue;
                float cs = MaxSize(col.bounds);
                bool visible = false;
                foreach (int j in ctx.RendererGrid.Overlapping(col.bounds))
                {
                    if (MaxSize(ctx.RendererGrid[j]) > 8f * cs + 16f) continue;   // nur Geometrie passender Größe zählt
                    visible = true;
                    break;
                }
                if (visible) continue;
                invisible++;
                r.Add(WqSeverity.Warning, "Kollision", "COLLIDER_INVISIBLE", "fester Collider ohne sichtbare Geometrie – unsichtbare Wand? (WORLD-114)", col.bounds.center, where);
            }
            r.notes.Add($"Kollision: {missing} große Objekte ohne Collider, {invisible} Collider ohne sichtbare Geometrie");
        }

        // ================================================================================================== Navigation

        private static void CheckNavigation(Ctx ctx, WqReport r)
        {
            NavMeshTriangulation tri = ctx.Nav;
            if (tri.vertices == null || tri.vertices.Length == 0 || tri.indices == null || tri.indices.Length < 3)
            {
                r.Add(WqSeverity.Info, "Navigation", "NAV_NONE", "kein NavMesh in der offenen Szene – Navigationsprüfungen übersprungen");
                return;
            }
            List<WorldQualityRules.NavIsland> islands = WorldQualityRules.NavIslands(tri.vertices, tri.indices, 0.05f, out _);
            float total = islands.Sum(i => i.Area);
            r.notes.Add($"NavMesh: {tri.indices.Length / 3} Dreiecke, {islands.Count} zusammenhängende Flächen, " +
                        $"Hauptfläche {islands[0].Area / 1e6f:0.00} km² von {total / 1e6f:0.00} km²");
            for (int i = 1; i < islands.Count && i <= 10; i++)
                if (islands[i].Area >= 25f)
                    r.Add(WqSeverity.Info, "Navigation", "NAV_ISLAND",
                        $"getrennte NavMesh-Fläche {islands[i].Area:0} m² ({islands[i].Triangles} Dreiecke) – gewollt (Dach, Fels) oder Loch?", islands[i].Sample);

            // Ausgangspunkt: sicherer Anker, sonst der Lager-Spawn aus den Entwurfsdaten, sonst die Hauptfläche
            SafeSpawnAnchor[] anchors = Find<SafeSpawnAnchor>();
            Vector3 origin = islands[0].Sample;
            string originName = "Hauptfläche";
            if (anchors.Length > 0)
            {
                origin = anchors[0].transform.position;
                originName = "Anker " + anchors[0].AnchorId;
            }
            else if (ctx.Plan != null && ctx.Plan.lager != null && ctx.Plan.lager.spawn != null && ctx.Plan.lager.spawn.Length >= 2)
            {
                float? g = ctx.Ground(ctx.Plan.lager.spawn[0], ctx.Plan.lager.spawn[1]);
                if (g.HasValue)
                {
                    origin = new Vector3(ctx.Plan.lager.spawn[0], g.Value, ctx.Plan.lager.spawn[1]);
                    originName = "Lager-Spawn";
                }
            }
            if (!NavMesh.SamplePosition(origin, out NavMeshHit originHit, 3f, NavMesh.AllAreas))
            {
                r.Add(WqSeverity.Critical, "Navigation", "NAV_ORIGIN_OFF", $"Ausgangspunkt ({originName}) liegt nicht auf dem NavMesh", origin);
                return;
            }

            var targets = new List<(string what, Vector3 pos, WqSeverity offSev, WqSeverity unreachSev)>();
            foreach (SafeSpawnAnchor a in anchors)
                targets.Add(("Sicherer Anker " + a.AnchorId, a.transform.position, WqSeverity.Critical, WqSeverity.Critical));
            foreach (WorldQuestSite q in Find<WorldQuestSite>())
                if (q.Reachable) targets.Add(("Questort " + q.Id, q.transform.position, WqSeverity.Warning, WqSeverity.Critical));
            foreach (WorldPoi p in Find<WorldPoi>())
                targets.Add(("Ort " + p.Id, p.transform.position, WqSeverity.Info, WqSeverity.Warning));
            foreach (DungeonPortal d in Find<DungeonPortal>())
                targets.Add(("Dungeon-Eingang " + d.DungeonId, d.transform.position, WqSeverity.Warning, WqSeverity.Critical));
            foreach (EnemySpawnArea e in ctx.Spawns)
                if (!e.SummonOnly) targets.Add(("Gegnergebiet " + e.Group, e.transform.position, WqSeverity.Warning, WqSeverity.Info));
            foreach (NpcIdentity n in ctx.Npcs)
                targets.Add(("NPC " + n.NpcId, n.transform.position, WqSeverity.Warning, WqSeverity.Warning));
            // Orte aus den Entwurfsdaten (Kareth v4: Ortsmarken ohne WorldPoi) – Gipfel und Pässe dürfen unerreichbar sein
            if (ctx.Plan != null && ctx.Plan.orte != null)
                foreach (KarethV4Ort o in ctx.Plan.orte)
                {
                    float? g = o != null ? ctx.Ground(o.x, o.z) : null;
                    if (!g.HasValue) continue;
                    bool bewusst = BewusstAbseits.TryGetValue(o.id ?? "", out string grund);
                    targets.Add(("Planort " + o.id + (bewusst ? " (bewusst: " + grund + ")" : ""), new Vector3(o.x, g.Value, o.z),
                        WqSeverity.Info, bewusst ? WqSeverity.Info : WqSeverity.Warning));
                }

            var path = new NavMeshPath();
            int off = 0, unreachable = 0, checkedN = 0, longPaths = 0;
            foreach ((string what, Vector3 pos, WqSeverity offSev, WqSeverity unreachSev) in targets.Take(400))
            {
                checkedN++;
                if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                {
                    off++;
                    r.Add(offSev, "Navigation", "NAV_OFFMESH", $"{what}: nicht auf dem NavMesh (2,5 m Suchradius)", pos);
                    continue;
                }
                if (!PathReachable(originHit.position, hit.position, path, out int hops))
                {
                    unreachable++;
                    r.Add(unreachSev, "Navigation", "NAV_UNREACHABLE", $"{what}: zu Fuß nicht erreichbar ({path.status})", pos);
                }
                else if (hops > 1) longPaths++;
            }
            r.notes.Add($"Navigation ab {originName}: {checkedN} Ziele, {off} neben dem NavMesh, {unreachable} unerreichbar" +
                        (longPaths > 0 ? $" ({longPaths} lange Wege in Etappen geprüft)" : ""));
        }

        /// <summary>Planorte, die bewusst nicht über das NavMesh (Gegner-Wegfindung) erreichbar sind: Hinweis statt Warnung.</summary>
        private static readonly Dictionary<string, string> BewusstAbseits = new Dictionary<string, string>
        {
            { "berg", "Gipfel" },
            { "nordpass", "Ausgang geschlossen, WORLD-114" },
            { "graue_mutter", "Hort auf dem Klettergrat, Max 06.10.; Spieler kommen hin, die Gegner-Wegfindung nicht" },
        };

        /// <summary>
        /// Erreichbar über das NavMesh? Lange Wege (Kilometer über ein feines NavMesh) liefert CalculatePath oft nur als
        /// „PathPartial“, weil die Suche ihr Knotenlimit erreicht – dann ab dem erreichten Punkt weitersuchen (Etappen).
        /// Kommt eine Etappe nicht mehr voran, ist das Ziel wirklich abgetrennt.
        /// </summary>
        private static bool PathReachable(Vector3 from, Vector3 to, NavMeshPath path, out int hops, int maxHops = 12)
        {
            Vector3 start = from;
            for (hops = 1; hops <= maxHops; hops++)
            {
                if (!NavMesh.CalculatePath(start, to, NavMesh.AllAreas, path)) return false;
                if (path.status == NavMeshPathStatus.PathComplete) return true;
                Vector3[] corners = path.corners;
                if (path.status != NavMeshPathStatus.PathPartial || corners.Length == 0) return false;
                Vector3 end = corners[corners.Length - 1];
                if ((end - start).sqrMagnitude < 4f || (end - to).sqrMagnitude >= (start - to).sqrMagnitude - 1f) return false;
                start = end;
            }
            return false;
        }

        // ================================================================================================== Wege und Bäume

        private sealed class Road
        {
            public string Name, Type;
            public float Width, Limit;
            public readonly List<Vector2> Line = new List<Vector2>();
            /// <summary>Höhe des Wegplans je Punkt (auf Brücken das Deck), leer bei älteren Daten.</summary>
            public readonly List<float> PlanY = new List<float>();
            /// <summary>Schwellen dieses Wegs am Ausgang (Welle 0, WORLD-114): Lage und Art.</summary>
            public readonly List<(Vector2 pos, string typ)> Gates = new List<(Vector2, string)>();
        }

        /// <summary>#29: Steile Stellen bis zu diesem Abstand von einer Schwelle gehören zur Sperre am Ausgang (bewusst geschlossen,
        /// WORLD-114; etwa die Granitstufen am Nordpass) – Hinweis statt Warnung.</summary>
        private const float GateZone = 100f;
        /// <summary>#29: Liegt der Wegplan mehr als so viel über dem Gelände und trägt dort ein Collider in Planhöhe (Brücke, Steg),
        /// zählt das Deck als Weg – nicht das Bachbett darunter.</summary>
        private const float DeckMin = 1f;

        private static List<Road> RoadsFromPlan(KarethV4Plan plan)
        {
            var roads = new List<Road>();
            if (plan == null || plan.wege == null) return roads;
            foreach (KarethV4Weg w in plan.wege)
            {
                if (w == null || w.xyz == null || w.xyz.Length < 6) continue;
                var road = new Road
                {
                    Name = w.name, Type = w.typ, Width = w.breite > 0f ? w.breite : 3f,
                    // steigung aus der Geländekette als Anteil (0,09) – ältere Daten ohne Feld: Wegeklasse
                    Limit = w.steigung > 0f ? (w.steigung <= 1f ? w.steigung * 100f : w.steigung) : WorldQualityBudgets.GradeLimit(w.typ)
                };
                for (int i = 0; i + 2 < w.xyz.Length; i += 3)   // x, y, z
                {
                    road.Line.Add(new Vector2(w.xyz[i], w.xyz[i + 2]));
                    road.PlanY.Add(w.xyz[i + 1]);
                }
                if (plan.schwellen != null)
                    foreach (KarethV4Schwelle s in plan.schwellen)
                        if (s != null && s.weg == w.name) road.Gates.Add((new Vector2(s.x, s.z), s.typ));
                roads.Add(road);
            }
            return roads;
        }

        private static void CheckRoadsAndTrees(Ctx ctx, WqReport r)
        {
            List<Road> roads = RoadsFromPlan(ctx.Plan);
            if (roads.Count > 0) CheckPlannedRoads(ctx, r, roads);
            else CheckSplatRoads(ctx, r);
        }

        private static void CheckPlannedRoads(Ctx ctx, WqReport r, List<Road> roads)
        {
            const float step = 2f, window = 10f;
            int steep = 0, decks = 0;
            float totalLength = 0f;
            Physics.SyncTransforms();
            var pts = new List<Vector2>();
            var planY = new List<float>();
            foreach (Road road in roads)
            {
                ResampleRoad(road, step, pts, planY);
                totalLength += pts.Count * step;
                float worst = 0f, worstGate = 0f;
                Vector3 worstPos = Vector3.zero, gatePos = Vector3.zero;
                string gateTyp = null;
                // Abschnitte getrennt bewerten: ohne Gelände darunter (Wege über den Kachelrand hinaus), normal, an einer Schwelle.
                // Auf Brücken und Stegen zählt das Deck (#29: vorher das Bachbett, 70 % unter der Brücke der Salzstraße West).
                var runH = new List<float>();
                var runP = new List<Vector2>();
                int runClass = -1;
                string runGate = null;
                for (int i = 0; i <= pts.Count; i++)
                {
                    int cls = -1;
                    float h = 0f;
                    string gate = null;
                    if (i < pts.Count)
                    {
                        float? g = ctx.Ground(pts[i].x, pts[i].y);
                        if (g.HasValue)
                        {
                            h = g.Value;
                            float py = planY[i];
                            if (!float.IsNaN(py) && py > h + DeckMin && DeckAt(pts[i].x, pts[i].y, py))
                            {
                                h = py;
                                decks++;
                            }
                            cls = 0;
                            foreach ((Vector2 pos, string typ) gt in road.Gates)
                                if ((gt.pos - pts[i]).sqrMagnitude < GateZone * GateZone)
                                {
                                    cls = 1;
                                    gate = gt.typ;
                                    break;
                                }
                        }
                    }
                    if (cls != runClass)
                    {
                        if (runH.Count >= 3)
                        {
                            float gr = WorldQualityRules.WorstGrade(runH, step, window, out int at);
                            if (at >= 0 && runClass == 0 && gr > worst)
                            {
                                worst = gr;
                                worstPos = new Vector3(runP[at].x, runH[at], runP[at].y);
                            }
                            else if (at >= 0 && runClass == 1 && gr > worstGate)
                            {
                                worstGate = gr;
                                gatePos = new Vector3(runP[at].x, runH[at], runP[at].y);
                                gateTyp = runGate;
                            }
                        }
                        runH.Clear();
                        runP.Clear();
                        runClass = cls;
                        runGate = gate;
                    }
                    if (cls >= 0)
                    {
                        runH.Add(h);
                        runP.Add(pts[i]);
                    }
                }
                if (worstGate > road.Limit * WorldQualityBudgets.GradeWarningFactor)
                    r.Add(WqSeverity.Info, "Wege", "ROAD_STEEP_GATE",
                        $"{road.Name} ({road.Type}): an der Schwelle „{gateTyp}“ bis {worstGate:0} % – Ausgang bewusst geschlossen (WORLD-114), keine Warnung", gatePos);
                WqSeverity sev = worst >= WorldQualityBudgets.GradeCriticalPercent ? WqSeverity.Critical
                    : worst > road.Limit * WorldQualityBudgets.GradeWarningFactor ? WqSeverity.Warning : WqSeverity.Info;
                if (sev == WqSeverity.Info) continue;
                steep++;
                r.Add(sev, "Wege", "ROAD_STEEP", $"{road.Name} ({road.Type}): bis {worst:0} % auf {window:0} m (geplant höchstens {road.Limit:0} %)", worstPos);
            }

            // Bäume in Wegen (Regel „Sinn und Gelände“: keine Bäume in Wegen) – Wegstrecken in einem 16-m-Raster
            const float cell = 16f;
            var grid = new Dictionary<Vector2Int, List<(int road, int seg)>>();
            for (int ri = 0; ri < roads.Count; ri++)
            {
                Road road = roads[ri];
                float pad = road.Width * 0.5f + 0.5f;
                for (int si = 0; si + 1 < road.Line.Count; si++)
                {
                    Vector2 a = road.Line[si], b = road.Line[si + 1];
                    int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - pad) / cell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + pad) / cell);
                    int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - pad) / cell), z1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + pad) / cell);
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                        {
                            var k = new Vector2Int(x, z);
                            if (!grid.TryGetValue(k, out List<(int road, int seg)> list)) grid[k] = list = new List<(int road, int seg)>();
                            list.Add((ri, si));
                        }
                }
            }
            int trees = 0, onRoad = 0;
            foreach (Terrain t in ctx.Terrains)
            {
                TerrainData d = t.terrainData;
                Vector3 o = t.transform.position, s = d.size;
                TreeInstance[] all = d.treeInstances;
                foreach (TreeInstance ti in all)
                {
                    trees++;
                    var p = new Vector2(o.x + ti.position.x * s.x, o.z + ti.position.z * s.z);
                    if (!grid.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell)), out List<(int road, int seg)> cands)) continue;
                    foreach ((int ri, int si) in cands)
                    {
                        Road road = roads[ri];
                        if (WorldQualityRules.DistanceToSegment(p, road.Line[si], road.Line[si + 1]) > road.Width * 0.5f + 0.5f) continue;
                        onRoad++;
                        r.Add(WqSeverity.Warning, "Wege", "TREE_ON_ROAD", $"Baum im Weg „{road.Name}“ (WORLD-225)", new Vector3(p.x, o.y + ti.position.y * s.y, p.y));
                        break;
                    }
                }
            }
            r.notes.Add($"Wege (Entwurfsdaten): {roads.Count} Wege, {totalLength / 1000f:0.0} km, {steep} zu steil (Schwellenbereich {GateZone:0} m ausgenommen, " +
                        $"{decks * 2} m auf Brückendecks gemessen); {onRoad} von {trees} Bäumen im Weg");
        }

        /// <summary>Wie <see cref="WorldQualityRules.Resample"/> (gleiche Punkte), dazu die Planhöhe je Punkt (NaN bei älteren Daten).</summary>
        private static void ResampleRoad(Road road, float step, List<Vector2> pts, List<float> ys)
        {
            pts.Clear();
            ys.Clear();
            List<Vector2> line = road.Line;
            bool hasY = road.PlanY.Count == line.Count;
            if (line.Count == 0) return;
            pts.Add(line[0]);
            ys.Add(hasY ? road.PlanY[0] : float.NaN);
            float carry = 0f;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vector2 a = line[i], b = line[i + 1];
                float len = Vector2.Distance(a, b);
                if (len < 1e-6f) continue;
                float pos = step - carry;
                while (pos <= len + 1e-5f)
                {
                    float f = Mathf.Min(pos, len) / len;
                    pts.Add(Vector2.Lerp(a, b, f));
                    ys.Add(hasY ? Mathf.Lerp(road.PlanY[i], road.PlanY[i + 1], f) : float.NaN);
                    pos += step;
                }
                carry = len - (pos - step);
            }
        }

        /// <summary>Trägt in Planhöhe ein Collider (Brückendeck, Steg) – und nicht das Gelände?</summary>
        private static bool DeckAt(float x, float z, float planY)
        {
            if (!Physics.Raycast(new Vector3(x, planY + 1.5f, z), Vector3.down, out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            return !(hit.collider is TerrainCollider) && Mathf.Abs(hit.point.y - planY) < 0.8f;
        }

        private static void CheckSplatRoads(Ctx ctx, WqReport r)
        {
            int onRoad = 0, steepCells = 0, layersFound = 0;
            foreach (Terrain t in ctx.Terrains)
            {
                TerrainData d = t.terrainData;
                int road = -1;
                TerrainLayer[] layers = d.terrainLayers;
                for (int l = 0; l < layers.Length; l++)
                {
                    string n = layers[l] != null ? layers[l].name.ToLowerInvariant() : "";
                    if (n.Contains("weg") || n.Contains("road") || n.Contains("path") || n.Contains("pfad"))
                    {
                        road = l;
                        break;
                    }
                }
                if (road < 0) continue;
                layersFound++;
                int res = d.alphamapResolution;
                float[,,] a = d.GetAlphamaps(0, 0, res, res);
                Vector3 o = t.transform.position, s = d.size;
                foreach (TreeInstance ti in d.treeInstances)
                {
                    int x = Mathf.Clamp(Mathf.RoundToInt(ti.position.x * (res - 1)), 0, res - 1);
                    int z = Mathf.Clamp(Mathf.RoundToInt(ti.position.z * (res - 1)), 0, res - 1);
                    if (a[z, x, road] < 0.5f) continue;
                    onRoad++;
                    r.Add(WqSeverity.Warning, "Wege", "TREE_ON_ROAD", $"Baum auf der Wegschicht ({t.name}, WORLD-225)",
                        new Vector3(o.x + ti.position.x * s.x, o.y + ti.position.y * s.y, o.z + ti.position.z * s.z));
                }
                int stride = Mathf.Max(1, res / 512);
                for (int z = 0; z < res; z += stride)
                    for (int x = 0; x < res; x += stride)
                    {
                        if (a[z, x, road] < 0.5f) continue;
                        float nx = x / (float)(res - 1), nz = z / (float)(res - 1);
                        float steepDeg = d.GetSteepness(nx, nz);
                        if (steepDeg < 20f) continue;
                        steepCells++;
                        r.Add(steepDeg >= 35f ? WqSeverity.Warning : WqSeverity.Info, "Wege", "ROAD_STEEP_SPLAT",
                            $"Wegschicht auf {steepDeg:0}° Hang ({t.name})", new Vector3(o.x + nx * s.x, o.y + d.GetInterpolatedHeight(nx, nz), o.z + nz * s.z));
                    }
            }
            if (layersFound == 0) r.Add(WqSeverity.Info, "Wege", "ROAD_NONE", "keine Wege gefunden (weder Entwurfsdaten noch Wegschicht im Terrain)");
            else r.notes.Add($"Wege (Terrain-Schicht): {onRoad} Bäume auf Wegen, {steepCells} steile Wegstellen ab 20°");
        }

        // ================================================================================================== Wasser

        private static void CheckWater(Ctx ctx, WqReport r)
        {
            int bodies = 0;
            foreach (Renderer ren in ctx.Renderers)
            {
                if (!(ren is MeshRenderer)) continue;
                string name = ren.name.ToLowerInvariant();
                string mat = ren.sharedMaterial != null ? ren.sharedMaterial.name.ToLowerInvariant() : "";
                bool isWater = name.Contains("wasser") || name.Contains("water") || name.Contains("salzsee") ||
                               mat.Contains("wasser") || mat.Contains("water") || mat.Contains("salzsee");
                Bounds b = ren.bounds;
                if (!isWater || b.size.y > 0.5f || b.size.x < 4f || b.size.z < 4f) continue;
                bodies++;
                float waterY = b.center.y;
                Rect area = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
                foreach (WorldQualityRules.OpenEdge e in WorldQualityRules.OpenWaterEdges(area, waterY, ctx.Ground, 4f, 8f))
                    r.Add(WqSeverity.Warning, "Wasser", "WATER_OPEN_EDGE",
                        $"{ren.name}: Wasserfläche endet offen auf {e.Length:0} m, Gelände bis {e.Deepest:0.0} m unter dem Spiegel " +
                        "(sichtbare Kante; beim Schwimmen ein Leck)", (e.From + e.To) * 0.5f, PathOf(ren.transform));

                // Tiefen innerhalb der Fläche (für Schwimmen und Tauchen, MOV-169)
                var depths = new List<float>();
                int samples = 0;
                float stepW = Mathf.Max(8f, Mathf.Max(area.width, area.height) / 120f);
                for (float z = area.yMin + stepW * 0.5f; z < area.yMax; z += stepW)
                    for (float x = area.xMin + stepW * 0.5f; x < area.xMax; x += stepW)
                    {
                        float? g = ctx.Ground(x, z);
                        if (!g.HasValue) continue;
                        samples++;
                        if (g.Value < waterY) depths.Add(waterY - g.Value);
                    }
                depths.Sort();
                string depthText = depths.Count == 0 ? "kein Grund unter dem Spiegel"
                    : $"{100f * depths.Count / Mathf.Max(1, samples):0} % der Fläche unter Wasser (≈ {depths.Count * stepW * stepW / 1e6f:0.00} km²), " +
                      $"Tiefe Median {depths[depths.Count / 2]:0.0} m, höchstens {depths[depths.Count - 1]:0.0} m";
                bool hasCollider = ren.TryGetComponent(out Collider wc) && wc.enabled;
                // Schwimmen kennt nur WaterVolume (der Motor fragt sie ab) – ein Trigger allein lässt niemanden schwimmen
                bool swimZone = ren.TryGetComponent(out Ashveil.Core.WaterVolume _);
                if (hasCollider && !wc.isTrigger)
                    r.Add(WqSeverity.Warning, "Wasser", "WATER_SOLID", $"{ren.name}: fester Collider auf der Wasserfläche – man läuft über das Wasser",
                        b.center, PathOf(ren.transform));
                float swimDepth = new Ashveil.Gameplay.Characters.CharacterMotorSettings().swimEnterDepth;
                if (!swimZone && depths.Count > 0 && depths[depths.Count - 1] > swimDepth)
                    r.Add(WqSeverity.Warning, "Wasser", "WATER_NO_SWIM", $"{ren.name}: tiefer als {swimDepth:0.0} m, aber keine Schwimmfläche (WaterVolume, MOV-169) – man läuft unter Wasser über den Grund",
                        b.center, PathOf(ren.transform));
                r.Add(WqSeverity.Info, "Wasser", "WATER_BODY", $"{ren.name}: Spiegel {waterY:0.0} m, {area.width:0} × {area.height:0} m, {depthText}" +
                                                          (swimZone ? " · Schwimmfläche" : ""), b.center, PathOf(ren.transform));
            }
            if (bodies == 0) r.Add(WqSeverity.Info, "Wasser", "WATER_NONE", "keine Wasserflächen gefunden");
        }

        // ================================================================================================== Assets

        private static readonly string[] NormalSlots = { "_BumpMap", "_NormalMap", "_DetailNormalMap" };
        private static readonly string[] LinearSlots = { "_MetallicGlossMap", "_MaskMap", "_OcclusionMap", "_SpecGlossMap", "_ParallaxMap" };

        private static void CheckAssets(Ctx ctx, WqReport r)
        {
            var materials = new HashSet<Material>();
            var meshes = new HashSet<Mesh>();
            foreach (Renderer ren in ctx.Renderers)
            {
                Material[] mats = ren.sharedMaterials;
                if (mats.Length > WorldQualityBudgets.MaterialsPerRendererWarning)
                    r.Add(WqSeverity.Warning, "Assets", "MAT_TOO_MANY", $"{mats.Length} Materialien an einem Renderer", ren.bounds.center, PathOf(ren.transform));
                foreach (Material m in mats)
                    if (m != null) materials.Add(m);
                Mesh mesh = MeshOf(ren);
                if (mesh != null) meshes.Add(mesh);

                // Netze ohne LOD-Gruppe
                if (mesh != null && ren is MeshRenderer && ren.GetComponentInParent<LODGroup>() == null)
                {
                    long tris = Triangles(mesh);
                    WqSeverity sev = WorldQualityRules.Classify(tris, WorldQualityBudgets.MeshTrianglesWithoutLodWarning, WorldQualityBudgets.MeshTrianglesWithoutLodCritical);
                    if (sev != WqSeverity.Info)
                        r.Add(sev, "Assets", "MESH_NO_LOD", $"{mesh.name}: {tris / 1000.0:0} k Dreiecke ohne LOD-Gruppe", ren.bounds.center, PathOf(ren.transform));
                }
                Vector3 ls = ren.transform.lossyScale;
                float maxS = Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
                float minS = Mathf.Min(Mathf.Abs(ls.x), Mathf.Min(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
                if (minS < 0.02f || maxS > 50f)
                    r.Add(WqSeverity.Info, "Assets", "SCALE_EXTREME", $"Skalierung {ls.x:0.###}/{ls.y:0.###}/{ls.z:0.###}", ren.bounds.center, PathOf(ren.transform));
            }

            // LOD-Ketten: jede Stufe weniger Dreiecke als die vorige
            foreach (LODGroup g in Find<LODGroup>())
            {
                LOD[] lods = g.GetLODs();
                long prev = -1;
                for (int i = 0; i < lods.Length; i++)
                {
                    long t = 0;
                    foreach (Renderer lr in lods[i].renderers)
                        if (lr != null) t += Triangles(MeshOf(lr));
                    if (prev >= 0 && t > prev)
                        r.Add(WqSeverity.Warning, "Assets", "LOD_ORDER", $"LOD{i} hat mehr Dreiecke ({t}) als LOD{i - 1} ({prev})", g.transform.position, PathOf(g.transform));
                    else if (prev > 0 && t > 0 && prev / (double)t > 8.0)
                        r.Add(WqSeverity.Info, "Assets", "LOD_JUMP", $"Sprung LOD{i - 1} → LOD{i}: {prev} → {t} Dreiecke (×{prev / (double)t:0})", g.transform.position, PathOf(g.transform));
                    prev = t;
                }
            }

            // Materialien und Shader
            var textures = new Dictionary<Texture, (string slot, string owner)>();
            var dupGroups = new Dictionary<string, List<Material>>();
            foreach (Material m in materials)
            {
                string matPath = AssetDatabase.GetAssetPath(m);
                Shader sh = m.shader;
                if (sh == null || !sh.isSupported || sh.name == "Hidden/InternalErrorShader")
                    r.Add(WqSeverity.Critical, "Assets", "SHADER_BROKEN", $"Material {m.name}: Shader fehlt oder läuft nicht ({(sh != null ? sh.name : "keiner")})", null, matPath);
                else if (sh.name == "Standard" || sh.name.StartsWith("Standard (") || sh.name.StartsWith("Legacy Shaders/") ||
                         sh.name.StartsWith("Mobile/") || sh.name.StartsWith("Particles/Standard"))
                    r.Add(WqSeverity.Critical, "Assets", "SHADER_NOT_URP", $"Material {m.name}: Built-in-Shader {sh.name} (in URP rosa)", null, matPath);

                bool transparent = m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f;
                if (transparent)
                {
                    float alpha = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").a : 1f;
                    Texture baseMap = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                    bool texAlpha = baseMap != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(baseMap)) is TextureImporter bi && bi.DoesSourceTextureHaveAlpha();
                    if (alpha >= 0.999f && !texAlpha)
                        r.Add(WqSeverity.Warning, "Assets", "MAT_TRANSPARENT_NO_ALPHA", $"Material {m.name}: transparent ohne Alpha (teuer, ohne Nutzen)", null, matPath);
                }
                else if (m.renderQueue >= 3000)
                    r.Add(WqSeverity.Warning, "Assets", "MAT_QUEUE", $"Material {m.name}: deckend, aber Render-Queue {m.renderQueue}", null, matPath);

                foreach (string prop in m.GetTexturePropertyNames())
                {
                    Texture t = m.GetTexture(prop);
                    if (t != null && !textures.ContainsKey(t)) textures[t] = (prop, m.name);
                }

                Texture main = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                string key = $"{(sh != null ? sh.name : "")}|{(main != null ? AssetDatabase.GetAssetPath(main) + main.name : "")}|" +
                             $"{c.r:0.00},{c.g:0.00},{c.b:0.00},{c.a:0.00}|{string.Join(",", m.shaderKeywords.OrderBy(x => x))}";
                if (!dupGroups.TryGetValue(key, out List<Material> group)) dupGroups[key] = group = new List<Material>();
                group.Add(m);
            }
            foreach (List<Material> g in dupGroups.Values.Where(l => l.Count > 1).OrderByDescending(l => l.Count).Take(15))
                r.Add(WqSeverity.Info, "Assets", "MAT_DUPLICATE", $"{g.Count} gleiche Materialien: {string.Join(", ", g.Take(5).Select(x => x.name))}{(g.Count > 5 ? " …" : "")}");

            // Terrain-Schichten zählen mit
            foreach (Terrain t in ctx.Terrains)
                foreach (TerrainLayer l in t.terrainData.terrainLayers)
                {
                    if (l == null) continue;
                    if (l.diffuseTexture != null && !textures.ContainsKey(l.diffuseTexture)) textures[l.diffuseTexture] = ("_Diffuse", "Terrain-Schicht " + l.name);
                    if (l.normalMapTexture != null && !textures.ContainsKey(l.normalMapTexture)) textures[l.normalMapTexture] = ("_BumpMap", "Terrain-Schicht " + l.name);
                    if (l.maskMapTexture != null && !textures.ContainsKey(l.maskMapTexture)) textures[l.maskMapTexture] = ("_MaskMap", "Terrain-Schicht " + l.name);
                }

            // Texturen
            foreach (KeyValuePair<Texture, (string slot, string owner)> kv in textures)
            {
                Texture tex = kv.Key;
                string path = AssetDatabase.GetAssetPath(tex);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                int real = Mathf.Max(tex.width, tex.height);
                if (imp != null) real = Mathf.Min(real, imp.maxTextureSize);
                if (real > WorldQualityBudgets.TextureSizeCritical)
                    r.Add(WqSeverity.Critical, "Assets", "TEX_TOO_LARGE", $"{tex.name}: {real} px (Grenze {WorldQualityBudgets.TextureSizeCritical}) · {kv.Value.owner}", null, path);
                else if (real > WorldQualityBudgets.TextureSizeWarning)
                    r.Add(WqSeverity.Warning, "Assets", "TEX_TOO_LARGE", $"{tex.name}: {real} px (Grenze {WorldQualityBudgets.TextureSizeWarning}) · {kv.Value.owner}", null, path);
                if (imp == null) continue;
                if (!imp.mipmapEnabled && imp.textureType != TextureImporterType.Sprite && imp.textureType != TextureImporterType.GUI)
                    r.Add(WqSeverity.Warning, "Assets", "TEX_NO_MIPS", $"{tex.name}: ohne Mipmaps (flimmert in der Ferne, mehr Bandbreite)", null, path);
                if (Array.IndexOf(NormalSlots, kv.Value.slot) >= 0 && imp.textureType != TextureImporterType.NormalMap)
                    r.Add(WqSeverity.Warning, "Assets", "TEX_NORMAL_IMPORT", $"{tex.name}: als {imp.textureType} importiert, benutzt als Normalmap ({kv.Value.owner})", null, path);
                if (Array.IndexOf(LinearSlots, kv.Value.slot) >= 0 && imp.sRGBTexture)
                    r.Add(WqSeverity.Warning, "Assets", "TEX_SRGB", $"{tex.name}: sRGB an, benutzt als lineare Karte {kv.Value.slot} ({kv.Value.owner})", null, path);
                if (imp.isReadable)
                    r.Add(WqSeverity.Info, "Assets", "TEX_READWRITE", $"{tex.name}: Read/Write an (doppelter Speicher – nötig?)", null, path);
            }

            // Netze: Ursprung unten mittig (ART-074) bei eigenen Modellen, doppelte Netze in verschiedenen Dateien
            var meshGroups = new Dictionary<string, List<Mesh>>();
            foreach (Mesh m in meshes)
            {
                string path = AssetDatabase.GetAssetPath(m);
                if (path.Contains("/Art/Models") || path.Contains("/Art/Welt/"))
                {
                    Bounds mb = m.bounds;
                    float size = Mathf.Max(mb.size.x, mb.size.z);
                    bool centred = Mathf.Abs(mb.center.x) <= 0.25f * size + 0.1f && Mathf.Abs(mb.center.z) <= 0.25f * size + 0.1f;
                    bool bottom = Mathf.Abs(mb.min.y) <= 0.1f * mb.size.y + 0.05f;
                    if (!centred || !bottom)
                        r.Add(WqSeverity.Info, "Assets", "MESH_PIVOT", $"{m.name}: Ursprung nicht unten mittig (Mitte {mb.center.x:0.00}/{mb.center.z:0.00}, Unterkante {mb.min.y:0.00})", null, path);
                }
                string key = $"{m.vertexCount}|{Triangles(m)}|{m.bounds.size.x:0.00}|{m.bounds.size.y:0.00}|{m.bounds.size.z:0.00}";
                if (!meshGroups.TryGetValue(key, out List<Mesh> list)) meshGroups[key] = list = new List<Mesh>();
                list.Add(m);
            }
            foreach (List<Mesh> g in meshGroups.Values.Where(l => l.Count > 1 && l.Select(x => AssetDatabase.GetAssetPath(x)).Distinct().Count() > 1).Take(15))
                r.Add(WqSeverity.Info, "Assets", "MESH_DUPLICATE", $"{g.Count} gleiche Netze in verschiedenen Dateien: {string.Join(", ", g.Take(4).Select(x => x.name))}");

            r.notes.Add($"Assets: {materials.Count} Materialien, {textures.Count} Texturen, {meshes.Count} Netze geprüft");
        }

        // ================================================================================================== Budget

        private static void CheckBudgets(Ctx ctx, WqReport r)
        {
            WqUnit scene = Measure("Szene " + r.scene, ctx, _ => true);
            r.units.Add(scene);
            WqSeverity rs = WorldQualityRules.Classify(scene.meshRenderers, WorldQualityBudgets.RenderersWarning, WorldQualityBudgets.RenderersCritical);
            r.Add(rs, "Budget", "BUDGET_RENDERERS", $"{scene.meshRenderers} Mesh-Renderer in der Szene (Warnung ab {WorldQualityBudgets.RenderersWarning}, " +
                                                     $"kritisch ab {WorldQualityBudgets.RenderersCritical}) – {WorldQualityBudgets.RenderersSource}");
            r.Add(WqSeverity.Info, "Budget", "BUDGET_TRIANGLES", $"{scene.triangles / 1e6:0.00} Mio. Dreiecke in Netzen (nur nächste LOD-Stufe) – {WorldQualityBudgets.TrianglesSource}");
            r.Add(WqSeverity.Info, "Budget", "BUDGET_LIGHTS", $"{scene.realtimeLights} Echtzeitlichter von {scene.lights} – {WorldQualityBudgets.LightsSource}");
            r.Add(WqSeverity.Info, "Budget", "BUDGET_SHADOWS", $"{scene.shadowCasters} Schattenwerfer, davon {scene.smallShadowCasters} kleiner als " +
                                                               $"{WorldQualityBudgets.SmallShadowCasterSize:0.0} m – {WorldQualityBudgets.ShadowSource}");

            // Weltzellen aus dem Weltverzeichnis, sofern Objekte darin liegen
            string guid = AssetDatabase.FindAssets("t:WorldManifest").FirstOrDefault();
            WorldManifest manifest = guid != null ? AssetDatabase.LoadAssetAtPath<WorldManifest>(AssetDatabase.GUIDToAssetPath(guid)) : null;
            if (manifest != null)
                foreach ((WorldRegionDefinition region, WorldCellDefinition cell) in manifest.AllCells())
                {
                    WorldCellDefinition c = cell;
                    WqUnit u = Measure($"Zelle {c.cellId} ({region.regionId})", ctx, p => c.Contains(p, 0f));
                    if (u.renderers == 0) continue;
                    r.units.Add(u);
                    WqSeverity cs = WorldQualityRules.Classify(u.meshRenderers, WorldQualityBudgets.RenderersWarning, WorldQualityBudgets.RenderersCritical);
                    if (cs != WqSeverity.Info)
                        r.Add(cs, "Budget", "CELL_OVERLOAD", $"Zelle {c.cellId}: {u.meshRenderers} Mesh-Renderer – {WorldQualityBudgets.RenderersSource}");
                }

            // Brennpunkte im 256-m-Raster: erst zählen, dann nur die acht dichtesten Kacheln genau vermessen
            float size = WorldQualityBudgets.HotspotTileSize;
            var perTile = new Dictionary<Vector2Int, int>();
            foreach (Renderer ren in ctx.Renderers)
            {
                if (!(ren is MeshRenderer)) continue;
                Vector2Int k = WorldQualityRules.TileKey(ren.bounds.center, size);
                perTile.TryGetValue(k, out int n);
                perTile[k] = n + 1;
            }
            foreach (KeyValuePair<Vector2Int, int> kv in perTile.OrderByDescending(x => x.Value).Take(8).ToList())
            {
                Vector2Int k = kv.Key;
                WqUnit t = Measure($"Kachel {k.x * size:0}/{k.y * size:0} (+{size:0} m)", ctx, p => WorldQualityRules.TileKey(p, size) == k);
                r.units.Add(t);
                r.Add(WqSeverity.Info, "Budget", "HOTSPOT", $"{t.name}: {t.meshRenderers} Mesh-Renderer, {t.triangles / 1000.0:0} k Dreiecke, " +
                                                            $"{t.shadowCasters} Schattenwerfer, {t.realtimeLights} Echtzeitlichter");
            }
        }

        private static WqUnit Measure(string name, Ctx ctx, Func<Vector3, bool> inside)
        {
            var u = new WqUnit { name = name };
            var mats = new HashSet<Material>();
            var texs = new HashSet<Texture>();
            var meshesSeen = new HashSet<Mesh>();
            foreach (Renderer ren in ctx.Renderers)
            {
                if (!inside(ren.bounds.center)) continue;
                u.renderers++;
                if (ren is MeshRenderer) u.meshRenderers++;
                Material[] ms = ren.sharedMaterials;
                u.drawCallsEstimate += Mathf.Max(1, ms.Length);
                foreach (Material m in ms)
                {
                    if (m == null || !mats.Add(m)) continue;
                    foreach (string p in m.GetTexturePropertyNames())
                    {
                        Texture t = m.GetTexture(p);
                        if (t != null) texs.Add(t);
                    }
                }
                if (ren.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                {
                    u.shadowCasters++;
                    if (MaxSize(ren.bounds) < WorldQualityBudgets.SmallShadowCasterSize) u.smallShadowCasters++;
                }
                LODGroup g = ren.GetComponentInParent<LODGroup>();
                if (g != null)
                {
                    LOD[] lods = g.GetLODs();
                    if (lods.Length > 0 && Array.IndexOf(lods[0].renderers, ren) < 0) continue;   // nur die nächste Stufe zählt als Dreiecke
                }
                Mesh mesh = MeshOf(ren);
                if (mesh == null) continue;
                u.triangles += Triangles(mesh);
                if (!meshesSeen.Add(mesh)) continue;
                long idx = 0;
                for (int s = 0; s < mesh.subMeshCount; s++) idx += mesh.GetIndexCount(s);
                int stride = mesh.vertexBufferCount > 0 ? mesh.GetVertexBufferStride(0) : 32;
                u.meshBytes += WorldQualityRules.MeshBytesEstimate(mesh.vertexCount, stride, idx, mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32);
            }
            u.materials = mats.Count;
            u.textures = texs.Count;
            foreach (Texture t in texs) u.textureBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
            foreach (Collider c in ctx.Colliders)
            {
                if (!inside(c.bounds.center)) continue;
                u.colliders++;
                if (c is MeshCollider mc && mc.sharedMesh != null)
                {
                    u.meshColliders++;
                    u.meshColliderTriangles += Triangles(mc.sharedMesh);
                }
            }
            foreach (Light l in ctx.Lights)
            {
                if (!inside(l.transform.position)) continue;
                u.lights++;
                if (l.lightmapBakeType != LightmapBakeType.Baked) u.realtimeLights++;
            }
            foreach (ParticleSystem p in ctx.Particles)
                if (inside(p.transform.position)) u.particleSystems++;
            foreach (NpcIdentity n in ctx.Npcs)
                if (inside(n.transform.position)) u.npcs++;
            foreach (EnemySpawnArea e in ctx.Spawns)
                if (inside(e.transform.position))
                {
                    u.enemySpawns++;
                    u.enemies += e.Count;
                }
            foreach (Vector3 p in ctx.TransformPositions)
                if (inside(p)) u.gameObjects++;
            if (ctx.Nav.indices != null && ctx.Nav.vertices != null)
                for (int i = 0; i + 2 < ctx.Nav.indices.Length; i += 3)
                    if (inside(ctx.Nav.vertices[ctx.Nav.indices[i]])) u.navMeshTriangles++;
            return u;
        }

        // ================================================================================================== Weltdaten

        private static void CheckWorldData(WqReport r)
        {
            List<string> problems = WorldManifestBuilder.Validate(out _);
            foreach (string p in problems) r.Add(WqSeverity.Warning, "Weltdaten", "WORLD_DATA", p);
            r.notes.Add($"Weltdaten (Ashveil › Welt › Welt prüfen): {problems.Count} Hinweise");
        }
    }
}
