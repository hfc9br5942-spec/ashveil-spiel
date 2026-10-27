using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.Core;
using Ashveil.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden spielbar (Lückenliste Punkt 1, Max 12.10.2026 „ok“): die Westweiden als additive Weltzelle neben Kareth v4
    /// (WORLD-120, Large-World-Architektur §2.1: Zellen-Szenen ohne Netzobjekte; Gegner erzeugt der Server in der Welt-Ebene,
    /// sobald die Zelle bereit ist). Inhalt wie der Entwurf (Gelände, Orte, Wasser, Hecken, Füllplan), ohne Entwurfshilfen
    /// (Wegstangen, Ausgangssäulen), ohne eigenes Licht (es gilt das Licht der Welt-Ebene), mit sicheren Ankern und eigenem NavMesh.
    ///
    /// Lage und Naht: <see cref="KarethV4Weidepass"/>. Die Ostkante der Westweiden folgt auf 300 m weich der Kareth-Westkante
    /// (mit Klamm), die äußerste Spalte ist mit ihr identisch. Der Entwurf (Scenes/Westweiden/Westweiden_Entwurf.unity) bleibt
    /// unverändert. Eigene Pfade: Scenes/Regions/Westweiden_Zelle*.  Weltverzeichnis: Data/World/WorldManifest_KarethV4.asset
    /// (das alte Weltverzeichnis der Sandbox bleibt unberührt). Auftrag „westweiden_zelle“; danach „kareth_v4_bauen“.
    /// </summary>
    [InitializeOnLoad]
    public static class WestweidenZelle
    {
        public const string RegionId = "Human_Westweiden_V2", CellId = "westweiden_v2", SceneTag = "westweiden";
        public const string KarethRegionId = "Human_KarethV4", KarethCellId = "kareth_v4";
        public const string SceneDir = ProjectSetup.Root + "/Scenes/Regions";
        public const string ScenePath = SceneDir + "/Westweiden_Zelle.unity";
        private const string TerrainDataPath = SceneDir + "/Westweiden_Zelle_Gelaende.asset";
        private const string NavMeshPath = SceneDir + "/Westweiden_Zelle_NavMesh.asset";
        public const string ManifestPath = WorldManifestBuilder.Folder + "/WorldManifest_KarethV4.asset";
        public const string RootName = "Westweiden (Weltzelle)";
        public static string HoehePfad => WestweidenEntwurf.HoeheDatei;
        private const float NahtBreite = 300f;

        static WestweidenZelle()
        {
            Auftragsordner.Registrieren("westweiden_zelle", (a, fertig) =>
            {
                try { string m = Bauen(out bool ok); fertig(ok, m); }
                catch (Exception e) { fertig(false, "Ausnahme: " + e.GetType().Name + " " + e.Message); }
            }, true, 3600);
        }

        [MenuItem("Ashveil/Welt/Westweiden: Spielzelle bauen (an Kareth v4)", priority = 592)]
        private static void Menu() => Debug.Log("[Ashveil] " + Bauen(out _));

        public static string Bauen(out bool ok)
        {
            ok = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Play läuft – nichts gebaut";
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return "offene Szene hat ungespeicherte Änderungen – nichts gebaut";
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            TerrainLayer[] layers = WestweidenEntwurf.Layer(out string layerFehler);
            if (layers == null) return layerFehler;
            string vorher = SceneManager.GetActiveScene().path;
            var log = new StringBuilder();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // ---- Naht: Kareth-Westkante (mit Klamm) → Ostkante der Westweiden
                KarethV4Plan p = KarethV4Builder.LoadPlan();
                float[,] kareth = KarethV4Weidepass.KarethMitKlamm(p, out string klamm);
                float[] kante = KarethV4Weidepass.LetzteKnoten;
                float[,] h = WestweidenEntwurf.HoehenLesen();
                int naht = Naht(h, kante, p);
                log.Append(klamm).Append(" · Naht: ").Append(naht).Append(" Höhenpunkte angeglichen · ");

                ProjectSetup.EnsureFolder(SceneDir);
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Terrain t = WestweidenEntwurf.Gelaende(layers, log, h, TerrainDataPath);
                t.gameObject.name = "Westweiden_Gelaende (Zelle)";
                t.groupingID = 8;
                t.allowAutoConnect = false;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
                var orte = WestweidenEntwurf.Orte(t, log, true);
                log.Append(WestweidenOrte.Bauen(t, orte, WestweidenEntwurf.Farbe)).Append(" Baukörper · ");
                log.Append(WestweidenWasser.Bauen(t, KarethV4Weidepass.WwSX, KarethV4Weidepass.WwSZ));
                log.Append(WestweidenHecken.Bauen(t));
                log.Append(WestweidenFuelle.Bauen(t, WestweidenEntwurf.Farbe));
                // Klang (14.10.2026, TECH-125): Bach, Brandung, Mühlen, Lerchen/Grillen – nach Wasser, Orten und Herden
                log.Append(WestweidenKlang.Bauen(t, KarethV4Weidepass.WwSX, KarethV4Weidepass.WwSZ));
                // Eichen als Überhälter in den Hecken (Punkt 6, 13.10.2026, Vorschlag) – vor dem Bewuchs (frei unter dem Stamm)
                log.Append(WestweidenKreide.Bauen(t, orte, SceneDir + "/Westweiden_Zelle_Kreidefelsen.asset"));   // 14.10.: Kreidefelsen an steilen Hängen (Vorschlag)
                log.Append(WestweidenEichen.Bauen(t));
                // Bodenbewuchs (Punkt 7, 13.10.2026): Gras und Korn wie in Kareth – nach Bauten, Wasser und Füllplan (frei unter ihnen)
                log.Append(WestweidenBewuchs.Bauen(t));
                log.Append(WestweidenEntwurf.WegPruefung(t, Path.Combine(ProjectPaths.Logs("Westweiden"), "Zelle")));
                log.Append(Anker(t, orte)).Append(" · ");
                log.Append(Sperren(t)).Append(" · ");

                // ---- alles unter eine Wurzel und an die Lage in Kareth (Wasserflächen tragen Welt-Rechtecke: mitschieben)
                var root = new GameObject(RootName).transform;
                foreach (GameObject g in scene.GetRootGameObjects())
                    if (g.transform != root) g.transform.SetParent(root, true);
                Vector3 versatz = KarethV4Weidepass.WwUrsprung;
                root.position = versatz;
                log.Append(WestweidenFuelle.TiereVerschieben(root, versatz));   // Herdentiere: Tagespläne in Weltkoordinaten nachziehen
                int wasser = 0;
                foreach (WaterVolume w in UnityEngine.Object.FindObjectsByType<WaterVolume>(FindObjectsInactive.Include))
                {
                    var so = new SerializedObject(w);
                    SerializedProperty area = so.FindProperty("area");
                    Rect r = area.rectValue;
                    area.rectValue = new Rect(r.x + versatz.x, r.y + versatz.z, r.width, r.height);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    wasser++;
                }
                Physics.SyncTransforms();
                // Gegner-Weltfülle (Spawngebiete, keine Netzobjekte) – nach dem Verschieben, Lagen in Weltkoordinaten
                log.Append(WestweidenGegner.Build(root, t, versatz)).Append(" · ");
                // Aufträge W01–W28 (Punkt 2): Auftragsfiguren und Fundstellen – vor dem NavMesh, Weltkoordinaten
                log.Append(WestweidenAuftraege.Build(root, t, versatz, orte)).Append(" · ");
                // Bewohner mit Tagesablauf (Punkt 3, 13.10.2026): nach den Auftragsfiguren (gleiche Platz-Helfer), vor dem NavMesh
                log.Append(WestweidenBewohner.Build(root, orte, versatz)).Append(" · ");
                // Außenstation Kreidekeller (Punkt 5): Höhle, Gelände-Löcher, Gegner, Glocke, Kellermeister – vor dem NavMesh
                log.Append(WestweidenKreidekeller.Build(root, t, versatz, CellId)).Append(" · ");
                log.Append($"Lage in Kareth ({versatz.x:0} / {versatz.y:0} / {versatz.z:0}), {wasser} Wasserflächen verschoben · ");

                // ---- NavMesh der Zelle (gebacken nach dem Speichern)
                var nav = new GameObject("NavMesh Westweiden (im Editor gebacken)");
                nav.transform.SetParent(root, false);
                nav.transform.position = Vector3.zero;
                var surface = nav.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.Volume;
                surface.center = new Vector3(versatz.x + KarethV4Weidepass.WwSX * 0.5f, versatz.y + 250f, versatz.z + KarethV4Weidepass.WwSZ * 0.5f);
                surface.size = new Vector3(KarethV4Weidepass.WwSX, 700f, KarethV4Weidepass.WwSZ);
                surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                surface.layerMask = ~LayerMask.GetMask(GameLayers.Player, GameLayers.Enemy, GameLayers.Interactable, "Ignore Raycast", "UI");
                surface.overrideVoxelSize = true;
                surface.voxelSize = 0.3f;
                surface.overrideTileSize = true;
                surface.tileSize = 256;
                int tief = KarethV4Spiel.DeepWaterNotWalkable(nav.transform, scene);

                EditorSceneManager.SaveScene(scene, ScenePath);
                foreach (TerrainCollider tc in UnityEngine.Object.FindObjectsByType<TerrainCollider>(FindObjectsInactive.Exclude))
                {
                    tc.enabled = false;
                    tc.enabled = true;
                }
                Physics.SyncTransforms();
                var bake = System.Diagnostics.Stopwatch.StartNew();
                EnemyBuilder.BakeNavMesh(surface, NavMeshPath);
                Vector3 lr = orte.TryGetValue("L3 Langer Rain", out Vector3 lrl) ? lrl + versatz : versatz;
                bool navOk = surface.navMeshData != null && NavMesh.SamplePosition(lr, out NavMeshHit _, 6f, NavMesh.AllAreas);
                log.Append(navOk ? $"NavMesh gebacken in {bake.Elapsed.TotalSeconds:0} s ({tief} Tiefwasserflächen gesperrt) · "
                                 : "NavMesh LEER oder ohne Fläche in Langer Rain – bitte prüfen · ");
                // Bewohner (Punkt 3): Plätze in Taschen ohne NavMesh-Verbindung an den nächsten erreichbaren Punkt rücken
                if (navOk) log.Append(WestweidenBewohner.NachDemNavMesh(root)).Append(" · ");
                // Auftragsführung (Punkt 4, 13.10.2026): Kreise auf Karte und Minimap aus den gebauten Lagen
                log.Append(WestweidenFuehrung.Schreiben()).Append(" · ");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();

                // ---- Build-Liste und Weltverzeichnis
                var szenen = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                if (!szenen.Any(s => s.path == ScenePath))
                {
                    szenen.Add(new EditorBuildSettingsScene(ScenePath, true));
                    EditorBuildSettings.scenes = szenen.ToArray();
                }
                WorldManifest m = EnsureManifest();
                log.Append($"Weltverzeichnis {ManifestPath} v{m.version} · Szene {ScenePath} · {sw.Elapsed.TotalSeconds:0} s · danach „kareth_v4_bauen“");
                ok = navOk;
                return log.ToString();
            }
            finally
            {
                if (!string.IsNullOrEmpty(vorher) && File.Exists(vorher)) EditorSceneManager.OpenScene(vorher, OpenSceneMode.Single);
            }
        }

        /// <summary>Ostkante der Westweiden weich (300 m) auf die Kareth-Westkante legen; äußerste Spalte identisch.</summary>
        private static int Naht(float[,] h, float[] kante, KarethV4Plan p)
        {
            int n = 0, R = KarethV4Weidepass.WwRes;
            float dx = KarethV4Weidepass.WwSX / (R - 1);
            float basis = KarethV4Weidepass.WwY0 + KarethV4Weidepass.WwDy;
            for (int iz = 0; iz < R; iz++)
            {
                float z = KarethV4Weidepass.WwZ0 + iz * KarethV4Weidepass.WwDz;
                float wz = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((z - (p.z0 - 100f)) / 100f));   // südlich von Kareth keine Nachbarkachel
                if (wz <= 0f) continue;
                float ziel = Mathf.Clamp01((kante[iz] - basis) / KarethV4Weidepass.WwSY);
                for (int ix = 0; ix < R; ix++)
                {
                    float xl = ix * dx;
                    float wx = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((xl - (KarethV4Weidepass.WwSX - NahtBreite)) / NahtBreite));
                    if (wx <= 0f) continue;
                    h[iz, ix] = Mathf.Lerp(h[iz, ix], ziel, wx * wz);
                    n++;
                }
            }
            return n;
        }

        /// <summary>Sichere Anker (Masterplan §49): Login, Sturz, ungültige Stelle. Neben den Plätzen, nicht auf Bauten.</summary>
        private static string Anker(Terrain t, Dictionary<string, Vector3> orte)
        {
            var root = new GameObject("Sichere Anker (Westweiden)").transform;
            int n = 0;
            foreach ((string ort, string id, Vector2 neben) in new[]
                     {
                         ("L3 Langer Rain", "ww_langer_rain", new Vector2(14f, -18f)), ("L2 Kammsattel", "ww_kammsattel", new Vector2(0f, 0f)),
                         ("L4 Hallgrund", "ww_hallgrund", new Vector2(-22f, 20f)), ("L6 Dreispannhof", "ww_dreispannhof", new Vector2(20f, 18f))
                     })
            {
                if (!orte.TryGetValue(ort, out Vector3 o)) continue;
                float x = o.x + neben.x, z = o.z + neben.y;
                var g = new GameObject("Anker " + ort);
                g.transform.SetParent(root, false);
                g.transform.position = new Vector3(x, WestweidenEntwurf.Hoehe(t, x, z) + 0.3f, z);
                g.AddComponent<SafeSpawnAnchor>().Configure(id, CellId);
                n++;
            }
            return n + " sichere Anker";
        }

        /// <summary>
        /// Geschlossene Ausgänge (wie Kareth Welle 0, WORLD-114: sichtbar durch Bauwerk oder Gelände, keine unsichtbare Wand).
        /// Die Randkämme (Topografie v7) haben an den Ausgängen eine Kerbe; darin steht die Sperre. Kareth (Weidepass) ist offen.
        /// </summary>
        private static string Sperren(Terrain t)
        {
            var root = new GameObject("Geschlossene Ausgänge (Westweiden)").transform;
            Material holz = GreyboxKit.Mat("WW Sperre Holz", new Color(0.36f, 0.27f, 0.18f));
            Material holzDunkel = GreyboxKit.Mat("WW Sperre Holz dunkel", new Color(0.24f, 0.19f, 0.14f));
            Material kreide = GreyboxKit.Mat("WW Sperre Kreide", new Color(0.70f, 0.69f, 0.64f));
            Material stein = GreyboxKit.Mat("WW Sperre Stein", new Color(0.50f, 0.49f, 0.46f));
            int n = 0;
            // Eschenfeld (Süd): Landwehr – Palisade quer durch die Kerbe, Tor auf der Kornspur geschlossen, Gitter über dem Mühlbach
            n += Palisade(root, t, "Landwehr Eschenfeld", new Vector2(2728f, 58f), new Vector2(2972f, 58f), holz, holzDunkel, new[] { 2800f }, new[] { 2900f });
            // Eiszahn (Nord): Steinschlag in der schmalen Kerbe des Außenbogens
            n += Felssturz(root, t, "Steinschlag am Eiszahn-Steig", new Vector2(1104f, 3352f), new Vector2(1196f, 3352f), kreide, stein, 11);
            // Grenzgebirge (Südost): Bergsturz vor der Kerbe am Hohen Pass
            n += Felssturz(root, t, "Bergsturz am Hohen Pass", new Vector2(4128f, 58f), new Vector2(4128f, 166f), kreide, stein, 23);
            // Salzsattel (Nordost): der Weidensteig-Pfad endet am Kareth-Randkamm – Felssturz macht das Ende lesbar
            n += Felssturz(root, t, "Felssturz am Salzsattel", new Vector2(4105f, 2965f), new Vector2(4155f, 2905f), kreide, stein, 37);
            return n + " Sperrteile an 4 geschlossenen Ausgängen";
        }

        private static int Palisade(Transform root, Terrain t, string name, Vector2 a, Vector2 b, Material holz, Material dunkel, float[] tore, float[] gitter)
        {
            Transform g = GreyboxKit.Group(name, root);
            Vector2 d = (b - a).normalized;
            float len = Vector2.Distance(a, b), yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg + 90f;
            int n = 0;
            for (float s = 0f; s < len; s += 3.5f)
            {
                Vector2 p0 = a + d * s, p1 = a + d * Mathf.Min(s + 3.5f, len), m = (p0 + p1) * 0.5f;
                float h0 = WestweidenEntwurf.Hoehe(t, p0.x, p0.y), h1 = WestweidenEntwurf.Hoehe(t, p1.x, p1.y);
                float lo = Mathf.Min(h0, h1) - 0.8f, hi = Mathf.Max(h0, h1) + 3.4f;
                bool tor = System.Array.Exists(tore, x => Mathf.Abs(m.x - x) < 3f);
                bool gi = System.Array.Exists(gitter, x => Mathf.Abs(m.x - x) < 6f);
                GreyboxKit.Prim(PrimitiveType.Cube, tor ? "Tor (verschlossen)" : gi ? "Bachgitter" : "Palisade", g,
                    new Vector3(m.x, (lo + hi) * 0.5f, m.y), new Vector3(Vector2.Distance(p0, p1) + 0.1f, hi - lo, tor ? 0.3f : 0.5f),
                    new Vector3(0f, yaw, 0f), tor || gi ? dunkel : holz);
                n++;
                if (tor)
                    foreach (float side in new[] { -2.2f, 2.2f })
                    {
                        Vector2 q = m + d * side;
                        float hq = WestweidenEntwurf.Hoehe(t, q.x, q.y);
                        GreyboxKit.Prim(PrimitiveType.Cube, "Torpfosten", g, new Vector3(q.x, hq + 2.4f, q.y), new Vector3(0.7f, 4.8f, 0.7f), new Vector3(0f, yaw, 0f), dunkel);
                        n++;
                    }
            }
            return n;
        }

        private static int Felssturz(Transform root, Terrain t, string name, Vector2 a, Vector2 b, Material m1, Material m2, int seed)
        {
            Transform g = GreyboxKit.Group(name, root);
            var rnd = new System.Random(seed);
            Vector2 d = (b - a).normalized, quer = new Vector2(-d.y, d.x);
            float len = Vector2.Distance(a, b);
            int n = 0;
            for (float s = 0f; s <= len; s += 3.2f)
                for (int reihe = -1; reihe <= 1; reihe++)
                {
                    if (reihe != 0 && rnd.NextDouble() < 0.35) continue;
                    Vector2 p = a + d * s + quer * (reihe * 3.5f + (float)(rnd.NextDouble() - 0.5) * 1.5f);
                    float gr = 3.6f + (float)rnd.NextDouble() * 2.6f - Mathf.Abs(reihe) * 1.2f;
                    float hp = WestweidenEntwurf.Hoehe(t, p.x, p.y);
                    GreyboxKit.Prim(PrimitiveType.Cube, "Sturzblock", g, new Vector3(p.x, hp + gr * 0.3f, p.y),
                        new Vector3(gr * (0.9f + (float)rnd.NextDouble() * 0.4f), gr * (0.7f + (float)rnd.NextDouble() * 0.3f), gr),
                        new Vector3((float)rnd.NextDouble() * 30f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 30f), rnd.NextDouble() < 0.6 ? m1 : m2);
                    n++;
                }
            return n;
        }

        /// <summary>Weltverzeichnis für Kareth v4 (Welt-Ebene, immer geladen) und die Westweiden-Zelle – getrennt vom alten
        /// Verzeichnis der Sandbox (Sandbox_M1 bleibt unverändert).</summary>
        public static WorldManifest EnsureManifest()
        {
            ProjectSetup.EnsureFolder(WorldManifestBuilder.Folder);
            var m = AssetDatabase.LoadAssetAtPath<WorldManifest>(ManifestPath);
            if (m == null)
            {
                m = ScriptableObject.CreateInstance<WorldManifest>();
                AssetDatabase.CreateAsset(m, ManifestPath);
            }
            var kRect = new Rect(KarethV4Weidepass.KanteX, -2000f, 4096f, 4096f);
            WorldRegionDefinition k = Region(m, KarethRegionId, "Kareth", kRect, new Vector2Int(1, 12));
            Zelle(k, KarethCellId, "", kRect, true, 10, 0f, 0f);
            k.isProtectedFromGeneration = true;
            var wRect = new Rect(KarethV4Weidepass.WwX0, KarethV4Weidepass.WwZ0, KarethV4Weidepass.WwSX, KarethV4Weidepass.WwSZ);
            WorldRegionDefinition w = Region(m, RegionId, "Westweiden", wRect, new Vector2Int(8, 16));
            Zelle(w, CellId, ScenePath, wRect, false, 5, 400f, 650f);
            if (!k.neighbors.Contains(RegionId)) k.neighbors.Add(RegionId);
            if (!w.neighbors.Contains(KarethRegionId)) w.neighbors.Add(KarethRegionId);
            EditorUtility.SetDirty(k);
            EditorUtility.SetDirty(w);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        private static WorldRegionDefinition Region(WorldManifest m, string id, string name, Rect bounds, Vector2Int stufen)
        {
            WorldRegionDefinition r = m.FindRegion(id);
            if (r == null)
            {
                string pfad = $"{WorldManifestBuilder.Folder}/Region_{id}.asset";
                r = AssetDatabase.LoadAssetAtPath<WorldRegionDefinition>(pfad);
                if (r == null)
                {
                    r = ScriptableObject.CreateInstance<WorldRegionDefinition>();
                    AssetDatabase.CreateAsset(r, pfad);
                }
                m.regions.Add(r);
            }
            r.regionId = id;
            r.displayName = name;
            r.bounds = bounds;
            r.recommendedLevel = stufen;
            return r;
        }

        private static void Zelle(WorldRegionDefinition r, string id, string szene, Rect bounds, bool immer, int prio, float laden, float entladen)
        {
            WorldCellDefinition c = r.cells.FirstOrDefault(x => x != null && x.cellId == id);
            if (c == null) r.cells.Add(c = new WorldCellDefinition { cellId = id });
            c.scenePath = szene;
            c.bounds = bounds;
            c.alwaysLoaded = immer;
            c.streamingPriority = prio;
            if (!immer)
            {
                c.loadMargin = laden;       // Weidegatter liegt 172 m vor der Kante: Laden beginnt im Salzseetal, lange vor der Klamm
                c.unloadMargin = entladen;
                c.holdSeconds = 20f;
            }
        }
    }
}
