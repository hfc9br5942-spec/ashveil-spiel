using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Gameplay.CameraSystem;
using Ashveil.Gameplay.Debugging;
using Ashveil.UI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kareth v4 spielbar machen (Welle 0, Aufgabe #26; Kareth-Pass Teil 13 Schritt 2, von Max freigegeben): dieselben
    /// Spielsysteme wie in der Sandbox – Netz (Host startet im Editor von selbst), Spielfigur vom Server, MMO-Kamera, HUD mit
    /// Atemleiste, Inventar, Aufträge, Gruppe, Namensschilder, Testanzeige (F1) – und ein im Editor gebackenes NavMesh.
    /// Start an der Ankunftsstelle (Entscheidung 5), Blick die Steinmännchen-Linie entlang zum Nordtor.
    ///
    /// Dazu die Gegner der Jagdgebiete (<see cref="KarethV4Gegner"/>, vorhandene Kreaturen).
    ///
    /// Bewusst NICHT: Sandbox-Inhalte, Spielerprefab neu bauen (gemeinsam genutzt), Inhalts-Bauer, Weltzellen-Streaming
    /// (die Weltzelle „kareth“ zeigt auf Sandbox_M1) und eine zweite Atmosphäre. Der Tageslauf hängt seit Welle 0 Schritt 2
    /// (06.10.) am Stil-A-Objekt (<see cref="StilAKareth"/>, DayNightCycle; 15:30 = Stil A).
    /// Sandbox_M1 bleibt unverändert. Figuren und Aufträge kommen mit dem Rest von #27.
    ///
    /// Aufruf aus <see cref="KarethV4Builder.Build"/> (Schalter: Menü „Kareth v4: Spielsysteme beim Bau“), einzeln über
    /// „Kareth v4: Spielsysteme einbauen (offene Szene)“.
    /// </summary>
    public static class KarethV4Spiel
    {
        public const string Key = "Ashveil.KarethV4.Spielsysteme";
        public const string NavMeshPath = ProjectSetup.Root + "/Scenes/Kareth_v4_NavMesh.asset";
        public const string SpawnName = "PlayerSpawn";            // Tests suchen den Spawn unter diesem Namen
        public const string NavMeshName = "NavMesh Kareth v4 (im Editor gebacken)";
        public const string BindName = "Bindepunkt (Wachtfeuer)";
        public const string SceneRulesName = "Szenenregeln (Position, Aufträge)";
        public const string PositionKey = "kareth_v4";
        /// <summary>Bindepunkt 8 m nördlich der Feuerstelle, Blick aufs Feuer (frei von Sitzen, Tanzplatz und Wegen der Figuren).</summary>
        private static readonly Vector3 BindXZYaw = new Vector3(0f, 8f, 180f);
        /// <summary>Aufträge, die es in Welle 0 hier nicht gibt (Q6 schickt zu den Salzgruben – Region Salzsee, Welle 1).</summary>
        private static readonly string[] HiddenQuests = { "quest.salt_and_silence" };
        private const string DatabasePath = ProjectSetup.Root + "/Data/DefinitionDatabase.asset";
        private const string MenuToggle = "Ashveil/Welt/Kareth v4: Spielsysteme beim Bau";

        /// <summary>Wurzelobjekte der Spielsysteme – dieselben Namen wie in der Sandbox.</summary>
        public static readonly string[] RootNames =
            { "Network", "Main Camera", "DebugHud", "WorldLabels", "HUD", "CombatText", "Inventory UI", "Quest UI", "Social UI", KartenName, GespraecheName, StreamerName };

        /// <summary>Westweiden spielbar (12.10.2026): Weltzellen-Streamer der Welt-Ebene (Kareth v4 + Westweiden-Zelle).</summary>
        public const string StreamerName = "WorldStreamer";
        /// <summary>Klanglandschaft (14.10.2026): Wind, Weltquellen, Klanggebiete – nur am Client.</summary>
        public const string KlangName = "Klanglandschaft";

        /// <summary>Lockrufe nach draußen (WORLD-239): Gesprächsschnipsel der Lagerfiguren, nur am Client.</summary>
        public const string GespraecheName = "Gespräche (Lager)";

        /// <summary>Karten r2 (#100, Max 08.10.2026): Kartenbezug (Daten für Server und Client) und Kartensystem (Banner, Minimap, Karte M).</summary>
        public const string KartenName = "Karten";
        /// <summary>Szene → Welt: das Wachtfeuer liegt in der Welt bei (8800, 17270) (Weltrahmen v2, karten_unity.json).</summary>
        public static readonly Vector2 KartenWeltversatz = new Vector2(8800f, 17270f);
        /// <summary>Szenenraster unter StreamingAssets/Ashveil/Karten/szenen (claude/karten_r2/karten_r2.py).</summary>
        public const string KartenSzenenRaster = "kareth_v4";
        /// <summary>Szenenraster der Weltzelle Westweiden (8 m, Weltkoordinaten) – nur geladen, wenn die Zelle gebaut ist.</summary>
        public const string KartenWestweidenRaster = "westweiden_v2";

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(Key, true);
            set => EditorPrefs.SetBool(Key, value);
        }

        private static NavMeshSurface _surface;
        private static Vector3 _spawnPos;
        private static bool _built;

        [MenuItem(MenuToggle, priority = 565)]
        private static void ToggleMenu()
        {
            Enabled = !Enabled;
            Debug.Log("[Ashveil] Kareth v4: Spielsysteme beim Bau " + (Enabled ? "an" : "aus") + " (gilt beim nächsten „Szene bauen“).");
        }

        [MenuItem(MenuToggle, true)]
        private static bool ToggleMenuValidate()
        {
            Menu.SetChecked(MenuToggle, Enabled);
            return true;
        }

        [MenuItem("Ashveil/Welt/Kareth v4: Spielsysteme einbauen (offene Szene)", priority = 566)]
        private static void BuildIntoOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK"); return; }
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != KarethV4Builder.ScenePath) { EditorUtility.DisplayDialog("Ashveil", "Erst Kareth v4 öffnen (Menü 561).", "OK"); return; }
            GameObject rootGo = GameObject.Find("Kareth v4 (Entwurf Claude)");
            if (rootGo == null) { EditorUtility.DisplayDialog("Ashveil", "Die Szene enthält kein „Kareth v4 (Entwurf Claude)“.", "OK"); return; }
            Remove(scene, rootGo.transform);
            string report = Build(KarethV4Builder.LoadPlan(), rootGo.transform);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, KarethV4Builder.ScenePath);
            if (_built) report += " · " + AfterSave(scene);
            Debug.Log("[Ashveil] Kareth v4: " + report);
        }

        /// <summary>
        /// Spielsysteme in die offene Kareth-v4-Szene setzen (vor dem Speichern). Danach nach dem Speichern
        /// <see cref="AfterSave"/> aufrufen (Szenen-IDs, NavMesh).
        /// </summary>
        public static string Build(KarethV4Plan p, Transform root)
        {
            _built = false;
            _surface = null;
            if (!TmpResources.IsImported) return "Spielsysteme ausgelassen: TextMeshPro-Grundressourcen fehlen (einmal „Sandbox-Szene erstellen“)";
            ProjectSetup.EnsureFolders();
            ProjectSetup.EnsureLayers();

            // Gemeinsame Daten laden – nicht neu bauen (Spielerprefab, Datenbank, Startklasse, Fäuste, Aussehen, Netzobjekte)
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SandboxSceneBuilder.PlayerPrefabPath);
            var db = AssetDatabase.LoadAssetAtPath<DefinitionDatabase>(DatabasePath);
            CharacterClassDefinition startClass = null;
            WeaponDefinition fists = null;
            if (db != null)
            {
                db.TryGet("class.warden", out startClass);
                db.TryGet("weapon.fists", out fists);
            }
            var catalog = AssetDatabase.LoadAssetAtPath<AppearanceCatalog>(AppearanceContentBuilder.CatalogPath);
            bool spawnables = AssetDatabase.LoadMainAssetAtPath(NetworkSetupBuilder.SpawnablesPath) != null;
            if (prefab == null || db == null || startClass == null || fists == null || catalog == null || !spawnables)
                return "Spielsysteme ausgelassen: gemeinsame Daten fehlen (Spielerprefab, Datenbank, Klasse, Fäuste, Aussehen oder Netzobjekte) – einmal „Sandbox-Szene erstellen“";

            // Spawn an der Ankunftsstelle, Blick die Steinmännchen-Linie entlang (sonst nach Nordnordost zum Lager)
            Vector3 spawnPos = SpawnPosition(p, root);
            _spawnPos = spawnPos;
            float yaw = SpawnYaw(p);
            Transform spawn = K.Group(SpawnName, root, root.InverseTransformPoint(spawnPos), new Vector3(0f, yaw, 0f));

            // Bindepunkt am Wachtfeuer (Kanon 05.10.: Start an der Ankunftsstelle, „Rückkehr- und Bindepunkt bleibt das Wachtfeuer“):
            // Wiederbeleben, Sturz aus der Welt, bekannte Charaktere ohne gültige Stelle. Dazu Positionsschlüssel (Szene ohne
            // Weltzellen, Version aus Höhenkarte und Plan) und Auftragsfilter (Q6 erst in Welle 1).
            Vector3 bindPos = GroundPoint(BindXZYaw.x, BindXZYaw.y);
            Transform bind = K.Group(BindName, root, root.InverseTransformPoint(bindPos), new Vector3(0f, BindXZYaw.z, 0f));
            bind.gameObject.AddComponent<Ashveil.World.BindPoint>();
            Transform rules = K.Group(SceneRulesName, root);
            int worldVersion = WorldVersion();
            rules.gameObject.AddComponent<Ashveil.World.ScenePositionKey>().Configure(PositionKey, worldVersion);
            var questFilter = rules.gameObject.AddComponent<Ashveil.Gameplay.Quests.SceneQuestFilter>();
            questFilter.Configure("Noch nicht verfügbar.", HiddenQuests);
            // #28: die Ergänzungsaufträge gibt es nur in Kareth v4; Westweiden (12.10.2026) hängt als Weltzelle an derselben Welt-Ebene
            if (KarethV4Weidepass.Aktiv) questFilter.SetTags(Ashveil.Data.KarethCatalog.SceneTag, WestweidenZelle.SceneTag);
            else questFilter.SetTags(Ashveil.Data.KarethCatalog.SceneTag);

            // Kamera wie in der Sandbox (SandboxSceneBuilder.BuildCamera): ohne Ziel, die eigene Figur meldet sich selbst an
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            int previewLayer = LayerMask.NameToLayer(Ashveil.Gameplay.Visuals.CharacterPreviewStage.LayerName);
            if (previewLayer >= 0) cam.cullingMask &= ~(1 << previewLayer);
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 2500f;
            camGo.AddComponent<AudioListener>();
            ArtStyleSetup.SetupCamera(cam);
            var mmo = camGo.AddComponent<MMOCameraController>();
            K.SetInt(mmo, "collisionMask", GameLayers.CameraCollisionMask());
            camGo.transform.SetPositionAndRotation(spawn.TransformPoint(new Vector3(0f, 3.4f, -6.8f)), spawn.rotation * Quaternion.Euler(14f, 0f, 0f));

            // Netz: Host startet im Editor von selbst, die Figur kommt vom Server an den Spawn
            NetworkSetupBuilder.BuildNetworkManager(prefab, spawn, db, startClass, fists, catalog);
            new GameObject("DebugHud").AddComponent<DebugHud>();
            new GameObject("WorldLabels").AddComponent<WorldLabelRenderer>();
            new GameObject("HUD").AddComponent<HudRoot>();
            new GameObject("CombatText").AddComponent<CombatTextRenderer>();
            new GameObject("Inventory UI").AddComponent<InventoryUi>();
            new GameObject("Quest UI").AddComponent<QuestUi>();
            new GameObject("Social UI").AddComponent<SocialUi>();
            // Klang (14.10.2026, TECH-125): Klanglandschaft am Client – Wind überall, Weltquellen und Klanggebiete der Zellen
            var klang = new GameObject(KlangName).AddComponent<Ashveil.Gameplay.Audio.KlangLandschaft>();
            klang.windBett = WestweidenKlang.Clip("WW_Wind_Weide_Bett");
            // Mischpult (Nachhall, 14.10.2026): Welt trocken · Hoehle mit Send zum Steam-Audio-Nachhall · Betten
            var mix = AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>(AuftragNachhall.Pfad);
            if (mix != null)
            {
                UnityEngine.Audio.AudioMixerGroup G(string n) => System.Array.Find(mix.FindMatchingGroups(""), g => g.name == n);
                klang.weltGruppe = G("Welt"); klang.hoehleGruppe = G("Hoehle"); klang.bettGruppe = G("Betten");
            }
            // Schritte nach Boden (14.10.2026): je Boden 10 Varianten (Freesound CC0, Art/ThirdParty/Audio/Westweiden)
            AudioClip[] S(string b) => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(
                System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(1, 10), i => WestweidenKlang.Clip($"WW_Schritt_{b}_{i}")), c => c != null));
            klang.schrittGras = S("Gras"); klang.schrittKies = S("Kies"); klang.schrittFels = S("Fels");
            klang.schrittMatsch = S("Matsch"); klang.schrittHolz = S("Holz"); klang.schrittWasser = S("Wasser");
            // Lockrufe nach draußen (WORLD-239, Max 10.10.2026): Gesprächsschnipsel der Lagerfiguren, nur am Client
            new GameObject(GespraecheName).AddComponent<Ashveil.UI.NpcGespraeche>();
            // Karten (#100): Daten aus StreamingAssets/Ashveil/Karten mit dem Szenenraster „kareth_v4“ (Unterzonen wie gebaut)
            var karten = new GameObject(KartenName);
            karten.AddComponent<Ashveil.Maps.KartenBezug>().Konfigurieren("Ashveil/Karten", KartenSzenenRaster, KartenWeltversatz,
                KarethV4Weidepass.Aktiv ? new[] { KartenWestweidenRaster } : new string[0]);
            karten.AddComponent<Ashveil.Maps.KartenSystem>();
            // Auftragsführung A/B/C (Max 10.10.2026): Kreise und Unterzonen der 29 Kareth-Aufträge neben das Szenenraster schreiben
            Debug.Log("[Ashveil] Kareth v4: " + KarethV4Fuehrung.Schreiben());
            NetworkSetupBuilder.MakeSceneDamageablesNetworked();
            // Westweiden (12.10.2026, WORLD-120): Weltzellen-Streamer – lädt die Westweiden-Zelle, sobald sich ein Spieler dem Weidepass
            // nähert (Server: für alle, Client: für die eigene Figur), ohne Ladebildschirm. Gegner der Zelle erscheinen erst, wenn sie bereit ist.
            string streamer = "ohne Weltzellen";
            if (KarethV4Weidepass.Aktiv)
            {
                var ws = new GameObject(StreamerName).AddComponent<Ashveil.Networking.WorldStreamer>();
                ws.Configure(WestweidenZelle.EnsureManifest());
                streamer = "Weltzellen-Streamer (Westweiden)";
            }

            // Gegner in die Jagdgebiete J1–J7 (#27, Welle 0: vorhandene Kreaturen; der EnemyDirector am Netz lässt sie erscheinen)
            string gegner = KarethV4Gegner.Build(root);

            // NavMesh über das ganze Gelände (gebacken nach dem Speichern, siehe AfterSave)
            var nav = new GameObject(NavMeshName);
            nav.transform.SetParent(root, false);
            _surface = nav.AddComponent<NavMeshSurface>();
            _surface.collectObjects = CollectObjects.Volume;
            _surface.center = new Vector3(p.x0 + p.size * 0.5f, p.baseY + p.rangeY * 0.5f, p.z0 + p.size * 0.5f) - root.position;
            _surface.size = new Vector3(p.size, p.rangeY, p.size);
            _surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            _surface.layerMask = ~LayerMask.GetMask(GameLayers.Player, GameLayers.Enemy, GameLayers.Interactable, "Ignore Raycast", "UI");
            _surface.overrideVoxelSize = true;
            _surface.voxelSize = 0.3f;                     // wie die Gelände-Zellen (Westweiden, Grenzgebirge)
            _surface.overrideTileSize = true;
            _surface.tileSize = 256;
            int noWalk = DeepWaterNotWalkable(nav.transform, null);

            _built = true;
            return $"Spielsysteme: Spawn {spawnPos.x:0}/{spawnPos.z:0} (Blick {yaw:0}°), Bindepunkt {bindPos.x:0}/{bindPos.z:0}, Weltversion {worldVersion}, " +
                   $"Kamera, Netz, HUD, Karten, {streamer}, {gegner}, NavMesh-Fläche " +
                   $"{p.size:0} m ({noWalk} Wasserflächen ab Schwimmtiefe gesperrt)";
        }

        /// <summary>Nach dem Speichern: Szenen-IDs für das Netz, NavMesh backen, speichern, Szene frisch laden.</summary>
        public static string AfterSave(Scene scene)
        {
            if (!_built) return "";
            string path = KarethV4Builder.ScenePath;
            string nav = "NavMesh nicht gebacken";
            try
            {
                // Szenen-IDs hängen am Pfad – nach dem ersten Speichern vergeben und erneut speichern (wie die Sandbox)
                NetworkSetupBuilder.GenerateSceneIds(scene);
                EditorSceneManager.SaveScene(scene, path);
                if (_surface != null)
                {
                    EditorUtility.DisplayProgressBar("Kareth v4", "NavMesh backen (4 × 4 km, dauert etwas) …", 0.97f);
                    // Frisch erzeugtes Terrain: Collider einmal aus- und einschalten, sonst bleibt das NavMesh fast leer
                    foreach (TerrainCollider tc in Object.FindObjectsByType<TerrainCollider>(FindObjectsInactive.Exclude))
                    {
                        tc.enabled = false;
                        tc.enabled = true;
                    }
                    Physics.SyncTransforms();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    EnemyBuilder.BakeNavMesh(_surface, NavMeshPath);
                    // Probe am Spawn statt der ganzen Triangulierung (4 × 4 km): liegt dort NavMesh?
                    bool atSpawn = NavMesh.SamplePosition(_spawnPos, out NavMeshHit _, 3f, NavMesh.AllAreas);
                    nav = _surface.navMeshData != null && atSpawn
                        ? $"NavMesh gebacken in {sw.Elapsed.TotalSeconds:0} s"
                        : "NavMesh LEER oder ohne Fläche am Spawn – bitte prüfen (Gegner würden es beim Serverstart neu bauen)";
                    if (!atSpawn) Debug.LogError("[Ashveil] Kareth v4: kein NavMesh am Spawn " + _spawnPos);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene, path);
                }
                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _surface = null;
                _built = false;
            }
            // Frisch laden: per Skript erzeugte Netzobjekte sind erst nach dem Laden vollständig (wie die Sandbox)
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            return nav;
        }

        /// <summary>
        /// Mess-Build (CORE-005, eigene Messkamera): Spielsysteme vorübergehend aus- und danach wieder einschalten, damit
        /// keine zweite Kamera, kein Netz-Fenster und kein HUD die Messung verfälschen. Speichert die Szene bei Änderung.
        /// </summary>
        public static int SetActiveForMeasurement(bool active)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != KarethV4Builder.ScenePath) return 0;
            int n = 0;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (System.Array.IndexOf(RootNames, go.name) >= 0 && go.activeSelf != active)
                {
                    go.SetActive(active);
                    n++;
                }
            if (n > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            return n;
        }

        /// <summary>Spielsysteme aus der Szene nehmen (für den Neubau einzeln über das Menü).</summary>
        public static int Remove(Scene scene, Transform root)
        {
            int n = 0;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (System.Array.IndexOf(RootNames, go.name) >= 0)
                {
                    Object.DestroyImmediate(go);
                    n++;
                }
            foreach (string child in new[] { SpawnName, BindName, SceneRulesName, NavMeshName, KarethV4Gegner.RootName })
            {
                Transform t = root.Find(child);
                if (t != null) { Object.DestroyImmediate(t.gameObject); n++; }
            }
            return n;
        }

        // ---------------------------------------------------------------- Hilfen

        private static Vector3 SpawnPosition(KarethV4Plan p, Transform root)
        {
            float x = p.lager != null && p.lager.spawn != null && p.lager.spawn.Length >= 2 ? p.lager.spawn[0] : 0f;
            float z = p.lager != null && p.lager.spawn != null && p.lager.spawn.Length >= 2 ? p.lager.spawn[1] : 7f;
            Transform marker = root.Find("Wachtfeuer v4 (Lager)/Spawn (Entwurf)");
            if (marker != null) return marker.position;
            Terrain t = Object.FindAnyObjectByType<Terrain>();
            float y = t != null ? t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y : KarethV4Builder.Ground(x, z);
            return new Vector3(x, y + 0.05f, z);
        }

        private static Vector3 GroundPoint(float x, float z)
        {
            Terrain t = Object.FindAnyObjectByType<Terrain>();
            float y = t != null ? t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y : KarethV4Builder.Ground(x, z);
            return new Vector3(x, y + 0.05f, z);
        }

        /// <summary>
        /// Weltversion für gespeicherte Positionen (<see cref="Ashveil.World.ScenePositionKey"/>): Prüfsumme (FNV-1a) über Höhenkarte
        /// und Plan. Wird das Gelände neu gerechnet, gelten alte Stellen nicht mehr – bekannte Charaktere beginnen am Bindepunkt.
        /// </summary>
        private static int WorldVersion()
        {
            uint h = 2166136261u;
            var dateien = new System.Collections.Generic.List<string> { KarethV4Builder.DataFolder + "/KarethV4_Hoehe.raw", KarethV4Builder.DataFolder + "/KarethV4_Plan.json" };
            // Westweiden angeschlossen: Klamm und Zellengelände gehören zur Welt – ändern sie sich, gelten alte Stellen nicht mehr
            if (KarethV4Weidepass.Aktiv) dateien.Add(WestweidenZelle.HoehePfad);
            foreach (string f in dateien)
            {
                if (!System.IO.File.Exists(f)) continue;
                foreach (byte b in System.IO.File.ReadAllBytes(f))
                {
                    h ^= b;
                    h *= 16777619u;
                }
            }
            return (int)(h & 0x7FFFFFFFu) | 1;
        }

        /// <summary>Blick am Start: die ersten Steinmännchen der Linie zum Nordtor entlang (Lore: die Wache holt die
        /// Neuankömmlinge dort ab). Ohne Linie nach Nordnordost.</summary>
        private static float SpawnYaw(KarethV4Plan p)
        {
            if (p.lager?.spuren != null)
                foreach (KarethV4Spur s in p.lager.spuren)
                    if (s != null && s.typ == "steinmaennchen" && s.linie != null && s.linie.Length >= 4)
                    {
                        float dx = s.linie[2] - s.linie[0], dz = s.linie[3] - s.linie[1];
                        if (dx * dx + dz * dz > 1f) return Mathf.Repeat(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, 360f);
                    }
            return 20f;
        }

        /// <summary>
        /// Gegner gehen nicht ins tiefe Wasser (MOV-169, wie Classic): Unter jeder Schwimmfläche wird der Grund ab der
        /// Schwimmtiefe für das NavMesh gesperrt; flaches Wasser zum Waten bleibt begehbar.
        /// </summary>
        internal static int DeepWaterNotWalkable(Transform parent, UnityEngine.SceneManagement.Scene? nurSzene)
        {
            int area = NavMesh.GetAreaFromName("Not Walkable");
            if (area < 0) return 0;
            float swimDepth = new Ashveil.Gameplay.Characters.CharacterMotorSettings().swimEnterDepth;
            int n = 0;
            foreach (WaterVolume w in Object.FindObjectsByType<WaterVolume>(FindObjectsInactive.Exclude))
            {
                if (nurSzene.HasValue && w.gameObject.scene != nurSzene.Value) continue;
                Rect a = w.AreaXZ;
                if (a.width <= 0f || a.height <= 0f) continue;
                float top = w.SurfaceY - swimDepth;
                const float bottom = -400f;
                var go = new GameObject($"Kein NavMesh unter tiefem Wasser ({w.name})");
                go.transform.SetParent(parent, false);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;
                var mod = go.AddComponent<NavMeshModifierVolume>();
                mod.center = new Vector3(a.center.x, (top + bottom) * 0.5f, a.center.y);
                mod.size = new Vector3(a.width, top - bottom, a.height);
                mod.area = area;
                n++;
            }
            return n;
        }
    }
}
