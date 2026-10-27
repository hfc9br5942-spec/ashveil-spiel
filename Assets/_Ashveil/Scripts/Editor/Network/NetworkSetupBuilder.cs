using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Ashveil.Gameplay.Combat;
using Ashveil.Networking;
using Ashveil.World;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Milestone 3.1 – Netzwerk (FishNet):
    ///   · macht das Spieler-Prefab netzwerkfaehig (NetworkObject, NetworkTransform, NetworkPlayer, Namensschild)
    ///   · legt den NetworkManager mit Tugboat-Transport, Spawner und Start-Baustein in die Szene
    ///   · baut und startet einen Test-Client, der sich mit dem Editor verbindet
    /// </summary>
    public static class NetworkSetupBuilder
    {
        public const string NetFolder = ProjectSetup.Root + "/Data/Network";
        public const string SpawnablesPath = NetFolder + "/SpawnablePrefabs.asset";
        private const string ClientFolder = "Builds/TestClient";
        private const string ClientExe = ClientFolder + "/Ashveil.exe";

        /// <summary>
        /// Weltzeit für Messungen und Bilder im fertigen Spiel (NET-020): Start 15:30 = gestaltetes Licht, wie früher bei jedem Start.
        /// Ohne den Parameter liefe ein Build, der selbst Host ist, in Echtzeit – Bilder und Messwerte hingen dann von der Uhrzeit ab.
        /// </summary>
        public const string ZeitGestaltet = WorldTimeSettings.Arg + " gestaltet";

        /// <summary>Weltzeit für Spieltest-Builds (Max 06.10.2026, Klick „Versatz auf Vormittag“): Start 09:00, danach Echtzeit.</summary>
        public const string ZeitVormittag = WorldTimeSettings.Arg + " vormittag";

        // ---------------------------------------------------------------- Spieler

        private static void SetBool(SerializedObject so, string path, bool v)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null) p.boolValue = v;
            else Debug.LogWarning($"[Ashveil] Einstellung '{path}' nicht gefunden ({so.targetObject.GetType().Name}).");
        }

        private static void SetFloat(SerializedObject so, string path, float v)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null) p.floatValue = v;
            else Debug.LogWarning($"[Ashveil] Einstellung '{path}' nicht gefunden ({so.targetObject.GetType().Name}).");
        }

        private static void SetEnum(SerializedObject so, string path, int v)
        {
            SerializedProperty p = so.FindProperty(path);
            if (p != null) p.intValue = v;
            else Debug.LogWarning($"[Ashveil] Einstellung '{path}' nicht gefunden ({so.targetObject.GetType().Name}).");
        }

        /// <summary>FishNet-NetworkTransform: Spruenge ueber 'threshold' Meter sofort uebernehmen.</summary>
        private static void EnableTeleport(SerializedObject so, float threshold)
        {
            SerializedProperty on = so.FindProperty("_enableTeleport");
            SerializedProperty th = so.FindProperty("_teleportThreshold");
            if (on == null || th == null)
            {
                Debug.LogWarning("[Ashveil] NetworkTransform: Teleport-Einstellung nicht gefunden (FishNet-Version?).");
                return;
            }
            on.boolValue = true;
            th.floatValue = threshold;
        }

        /// <summary>Noch nicht gespeicherte Spielerfigur netzwerkfaehig machen (vor SaveAsPrefabAsset).</summary>
        public static void MakeNetworkPlayer(GameObject root)
        {
            var nob = root.AddComponent<NetworkObject>();
            InterestManagement.ApplyPlayer(root); // Large-World N3: nah, eigene Figur oder gleiche Gruppe

            // Milestone 5: volle Server-Vorhersage. Wurzel = simulierte Figur (Motor, CharacterController),
            // Kind "Graphics" = alles Sichtbare, wird zwischen den Netz-Takten geglaettet.
            Transform graphics = new GameObject("Graphics").transform;
            graphics.SetParent(root.transform, false);
            Transform visual = root.transform.Find("Visual");
            if (visual != null) visual.SetParent(graphics, false);

            // Positionsabgleich fuer Zuschauer: jetzt server-autoritativ (der Server simuliert die Eingaben)
            var nt = root.AddComponent<NetworkTransform>();
            nt.SetSynchronizeScale(false);
            var so = new SerializedObject(nt);
            SerializedProperty auth = so.FindProperty("_clientAuthoritative");
            if (auth != null) auth.boolValue = false;
            SerializedProperty toOwner = so.FindProperty("_sendToOwner");
            if (toOwner != null) toOwner.boolValue = false; // der Besitzer sagt selbst voraus
            EnableTeleport(so, 8f); // Wiederbelebung/Versetzen springt sofort statt ueber Sekunden zu gleiten
            so.ApplyModifiedPropertiesWithoutUndo();

            var nso = new SerializedObject(nob);
            SetBool(nso, "_enablePrediction", true);
            SetEnum(nso, "_predictionType", 0); // Other (CharacterController)
            SetBool(nso, "_enableStateForwarding", false); // Zuschauer ueber den NetworkTransform
            SerializedProperty ntRef = nso.FindProperty("_networkTransform");
            if (ntRef != null) ntRef.objectReferenceValue = nt;
            nso.ApplyModifiedPropertiesWithoutUndo();

            var smoother = graphics.gameObject.AddComponent<FishNet.Component.Transforming.Beta.NetworkTickSmoother>();
            var sso = new SerializedObject(smoother);
            SerializedProperty target = sso.FindProperty("_initializationSettings.TargetTransform");
            if (target != null) target.objectReferenceValue = root.transform;
            foreach (string block in new[] { "_controllerMovementSettings", "_spectatorMovementSettings" })
            {
                SetBool(sso, block + ".EnableTeleport", true);
                SetFloat(sso, block + ".TeleportThreshold", 4f);
                SerializedProperty interp = sso.FindProperty(block + ".InterpolationValue");
                if (interp != null) interp.intValue = block.StartsWith("_controller") ? 1 : 2;
            }
            SetBool(sso, "_favorPredictionNetworkTransform", true);
            sso.ApplyModifiedPropertiesWithoutUndo();
            if (target == null) Debug.LogWarning("[Ashveil] NetworkTickSmoother: Einstellungen nicht gefunden (FishNet-Version?).");

            root.AddComponent<NetworkMotion>();

            // Namensschild fuer fremde Spieler (ueber dem Kopf, geglaettet mit der Grafik)
            var plateGo = new GameObject("Nameplate");
            plateGo.transform.SetParent(graphics, false);
            plateGo.transform.localPosition = new Vector3(0f, 2.15f, 0f);
            var plate = plateGo.AddComponent<WorldLabel>();
            plate.Configure("Wanderer", "", WorldLabelStyle.Friendly, 45f);
            plateGo.SetActive(false);

            var np = root.AddComponent<NetworkPlayer>();
            K.SetRef(np, "nameplate", plate);

            // Milestone 3.2: Kampf serverautoritaer, Leben gespiegelt
            root.AddComponent<NetworkCombat>();
            if (root.GetComponent<Health>() != null) root.AddComponent<NetworkHealth>();

            // Milestone 3.4: Ausruestung, Inventar & Beute (serverautoritaer), Pluendern per Rechtsklick, Mehrspieler-Test
            root.AddComponent<Ashveil.Gameplay.Items.CharacterEquipment>();
            root.AddComponent<NetworkInventory>();
            root.AddComponent<Ashveil.Gameplay.Items.LootInteractor>();
            root.AddComponent<SelfTestAgent>();

            // Sichtbare Ausruestung + persoenliches Aussehen (Koerper, Frisur, Farben) im Netz
            root.AddComponent<Ashveil.Gameplay.Visuals.EquipmentVisuals>();
            root.AddComponent<NetworkAppearance>();

            // Milestone 4: Erfahrung, Stufen, Auftraege, Handel; Rechtsklick auf NPC = Gespraech
            root.AddComponent<NetworkProgression>();
            root.AddComponent<Ashveil.Gameplay.Quests.NpcInteractor>();

            // Milestone 5: Gruppe und Chat; Rechtsklick auf Spieler = einladen / fluestern
            root.AddComponent<Ashveil.Gameplay.Social.PlayerIdentity>();
            root.AddComponent<Ashveil.Gameplay.Social.PlayerInteractor>();
            root.AddComponent<NetworkSocial>();

            // M6.6: 24-Slot-Actionbar (Belegung je Charakter auf dem Server)
            root.AddComponent<NetworkActionBar>();
            if (root.GetComponent<NetworkEmote>() == null) root.AddComponent<NetworkEmote>(); // M8
            if (root.GetComponent<NetworkWorldTime>() == null) root.AddComponent<NetworkWorldTime>(); // M8: Tageszeit
        }

        // ---------------------------------------------------------------- Szenenobjekte

        /// <summary>Alles in der Szene, das Leben hat (Puppen, Boss-Platzhalter), wird Netzwerkobjekt.</summary>
        public static int MakeSceneDamageablesNetworked()
        {
            int count = 0;
            foreach (Health h in Object.FindObjectsByType<Health>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                GameObject go = h.gameObject;
                if (go.GetComponentInParent<NetworkObject>(true) == null) go.AddComponent<NetworkObject>();
                if (go.GetComponent<NetworkHealth>() == null) go.AddComponent<NetworkHealth>();

                // Bewegt sich von selbst (Wegpunkte): nur der Server bewegt, Clients sehen die Server-Position
                var walker = go.GetComponent<WaypointWalker>();
                if (walker != null && go.GetComponent<NetworkTransform>() == null)
                {
                    var nt = go.AddComponent<NetworkTransform>();
                    nt.SetSynchronizeScale(false);
                    var so = new SerializedObject(nt);
                    SerializedProperty auth = so.FindProperty("_clientAuthoritative");
                    if (auth != null) auth.boolValue = false;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    var driven = go.AddComponent<ServerDriven>();
                    var dso = new SerializedObject(driven);
                    SerializedProperty list = dso.FindProperty("serverOnly");
                    list.arraySize = 1;
                    list.GetArrayElementAtIndex(0).objectReferenceValue = walker;
                    dso.ApplyModifiedPropertiesWithoutUndo();
                }
                count++;
            }
            return count;
        }

        /// <summary>
        /// Szenen-IDs fuer Netzwerkobjekte vergeben (entspricht FishNets "Reserialize Scenes").
        /// Die Szene muss dafuer schon einmal gespeichert sein (die ID haengt am Szenenpfad).
        /// </summary>
        public static void GenerateSceneIds(Scene scene)
        {
            const BindingFlags st = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            const BindingFlags inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo create = typeof(NetworkObject).GetMethod("CreateSceneId", st, null,
                new[] { typeof(Scene), typeof(bool), typeof(int).MakeByRefType() }, null);
            MethodInfo reserialize = typeof(NetworkObject).GetMethod("ReserializeEditorSetValues", inst);
            if (create == null)
            {
                Debug.LogWarning("[Ashveil] Netzwerk: Szenen-IDs konnten nicht automatisch vergeben werden. " +
                                 "Bitte Menue 'Tools > Fish-Networking > Utility > Reserialize NetworkObjects' ausfuehren.");
                return;
            }
            if (reserialize == null)
                Debug.LogWarning("[Ashveil] Netzwerk: FishNet-Methode 'ReserializeEditorSetValues' nicht gefunden – bitte " +
                                 "'Tools > Fish-Networking > Utility > Reserialize NetworkObjects' ausfuehren.");
            object[] args = { scene, true, 0 };
            if (create.Invoke(null, args) is System.Collections.IEnumerable list)
            {
                foreach (object o in list)
                {
                    reserialize?.Invoke(o, new object[] { true, false });
                    if (o is Object uo) EditorUtility.SetDirty(uo);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Ashveil] Netzwerk: Szenen-IDs vergeben ({args[2]} Objekte). Hinweis: Nach jedem Neuaufbau der " +
                      "Szene auch den Test-Client neu bauen, sonst passen die Objekte nicht zusammen.");
        }

        /// <summary>Nach dem Speichern: Prefab darf keine Szenen-ID tragen (sonst haelt FishNet es fuer ein Szenenobjekt).</summary>
        public static void FinalizePlayerPrefab(GameObject prefab)
        {
            var nob = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
            if (nob == null) return;
            nob.SetSceneId(NetworkObject.UNSET_SCENEID_VALUE);
            EditorUtility.SetDirty(nob);
            EditorUtility.SetDirty(prefab);

            ProjectSetup.EnsureFolder(NetFolder);
            var spawnables = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(SpawnablesPath);
            if (spawnables == null)
            {
                spawnables = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                AssetDatabase.CreateAsset(spawnables, SpawnablesPath);
            }
            spawnables.Clear();
            spawnables.AddObject(nob, true, false);
            EditorUtility.SetDirty(spawnables);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Weitere Netzwerk-Prefabs (Gegner) an die Liste haengen – nach dem Spieler (feste Reihenfolge).</summary>
        public static void AddSpawnables(System.Collections.Generic.IEnumerable<GameObject> prefabs)
        {
            var spawnables = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(SpawnablesPath);
            if (spawnables == null) return;
            foreach (GameObject p in prefabs)
            {
                var nob = p != null ? p.GetComponent<NetworkObject>() : null;
                if (nob != null) spawnables.AddObject(nob, true, false);
            }
            EditorUtility.SetDirty(spawnables);
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- Szene

        /// <summary>NetworkManager + Tugboat + Spawner + Start-Baustein in die offene Szene legen.</summary>
        public static void BuildNetworkManager(GameObject playerPrefab, Transform spawnPoint,
            Ashveil.Data.DefinitionDatabase database = null, Ashveil.Data.CharacterClassDefinition defaultClass = null,
            Ashveil.Data.WeaponDefinition unarmed = null, Ashveil.Data.AppearanceCatalog appearanceCatalog = null)
        {
            var go = new GameObject("Network");
            // Datenbank zuerst (alle anderen lesen Definitionen per ID)
            var data = go.AddComponent<Ashveil.Data.GameDataBootstrap>();
            K.SetRef(data, "database", database);
            K.SetRef(data, "defaultClass", defaultClass);
            K.SetRef(data, "unarmed", unarmed);
            K.SetRef(data, "appearanceCatalog", appearanceCatalog);
            var nm = go.AddComponent<NetworkManager>();
            // Netz-Takt 50 Hz: Bewegung wird in festen Schritten simuliert (Vorhersage), 50 Hz halten Spruenge knackig
            var time = go.GetComponent<FishNet.Managing.Timing.TimeManager>();
            if (time == null) time = go.AddComponent<FishNet.Managing.Timing.TimeManager>();
            var tso = new SerializedObject(time);
            SerializedProperty rate = tso.FindProperty("_tickRate");
            if (rate != null) rate.intValue = 50;
            else Debug.LogWarning("[Ashveil] TimeManager: _tickRate nicht gefunden.");
            tso.ApplyModifiedPropertiesWithoutUndo();
            var tugboat = go.AddComponent<Tugboat>();
            var tm = go.GetComponent<TransportManager>();
            if (tm == null) tm = go.AddComponent<TransportManager>();
            tm.Transport = tugboat;
            nm.SpawnablePrefabs = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(SpawnablesPath);

            var spawner = go.AddComponent<NetworkPlayerSpawner>();
            K.SetRef(spawner, "playerPrefab", playerPrefab != null ? playerPrefab.GetComponent<NetworkObject>() : null);
            K.SetRef(spawner, "spawnPoint", spawnPoint);

            var boot = go.AddComponent<NetworkBootstrap>();
            K.SetRef(boot, "manager", nm);

            go.AddComponent<EnemyDirector>();
            go.AddComponent<SelfTestRunner>();
            go.AddComponent<DuoTestDirector>();

            EditorUtility.SetDirty(nm);
            EditorUtility.SetDirty(tm);
        }

        // ---------------------------------------------------------------- Test-Client

        [MenuItem("Ashveil/Netzwerk/Test-Client bauen", priority = 200)]
        public static void BuildTestClient()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst den Play-Modus beenden – waehrend Play kann Unity nicht bauen.", "OK");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SandboxSceneBuilder.ScenePath) == null)
            {
                EditorUtility.DisplayDialog("Ashveil", "Erst 'Ashveil > Sandbox-Szene erstellen' ausfuehren.", "OK");
                return;
            }

            if (BuildTestClientQuiet(out string _))
                EditorUtility.DisplayDialog("Ashveil", "Test-Client ist fertig.\n\nJetzt Play druecken (Editor = Host) und dann\n'Ashveil > Netzwerk > Test-Client starten'.", "OK");
        }

        /// <summary>
        /// Test-Client bauen ohne Dialoge (Auftragsordner „testclient_bauen“, Welle 0 06.10.2026); sonst wie das Menue.
        /// </summary>
        public static bool BuildTestClientQuiet(out string meldung)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { meldung = "Play läuft – während Play kann Unity nicht bauen"; return false; }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SandboxSceneBuilder.ScenePath) == null) { meldung = "Sandbox-Szene fehlt"; return false; }
            StopTestClients(); // laufender Client sperrt die exe
            ClearBurstOutput();
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.enableFrameTimingStats = true; // CPU-/GPU-Zeiten fuer den Leistungstest
            Directory.CreateDirectory(ClientFolder);

            var options = new BuildPlayerOptions
            {
                scenes = BuildScenes(),
                locationPathName = ClientExe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { TestBuild.Symbol }
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Ashveil.EditorTools.Rendering.DynamicGIBuildSupport.SetActiveForBuild(false);   // auch bei Fehlschlag wieder aus
            if (report.summary.result == BuildResult.Succeeded)
            {
                File.WriteAllText(StampFile, SceneHash());
                meldung = $"Test-Client gebaut ({report.summary.totalTime.TotalSeconds:0} s): {Path.GetFullPath(ClientExe)}";
                Debug.Log("[Ashveil] " + meldung);
                return true;
            }
            meldung = "Test-Client konnte nicht gebaut werden: " + report.summary.result + " (" + report.summary.totalErrors + " Fehler)";
            Debug.LogError("[Ashveil] " + meldung + " – Details stehen oben in der Konsole.");
            return false;
        }

        /// <summary>Ist der Test-Client gebaut und passt er zu Szene und Laufzeit-Skripten (Stempel)?</summary>
        public static bool TestClientCurrent(out string warum)
        {
            if (!File.Exists(Path.GetFullPath(ClientExe))) { warum = "Test-Client fehlt"; return false; }
            string stamp = File.Exists(StampFile) ? File.ReadAllText(StampFile).Trim() : null;
            if (stamp != SceneHash()) { warum = "Test-Client ist veraltet (Szene oder Laufzeit-Skripte geändert)"; return false; }
            warum = "";
            return true;
        }

        /// <summary>
        /// 12h-Block 01.10.2026 (P4-Vorbereitung): Development-Build in einen eigenen Ordner (z. B. Builds/Playtest_P4_2026-10-02).
        /// Gleiche Szenen wie der Test-Client, aber BuildOptions.Development. Aendert keine Projekteinstellung.
        /// Spielstaende landen im Build-Ordner (Saves/), nicht im Projekt.
        /// </summary>
        public static bool BuildPlaytest(string folder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
            ClearBurstOutput();
            Directory.CreateDirectory(folder);
            var options = new BuildPlayerOptions
            {
                scenes = BuildScenes(),
                locationPathName = Path.Combine(folder, "Ashveil.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Ashveil.EditorTools.Rendering.DynamicGIBuildSupport.SetActiveForBuild(false);
            bool ok = report.summary.result == BuildResult.Succeeded;
            if (ok) Debug.Log($"[Ashveil] P4-Playtest-Build fertig ({report.summary.totalTime.TotalSeconds:0} s): {Path.GetFullPath(folder)}");
            else Debug.LogError("[Ashveil] P4-Playtest-Build fehlgeschlagen: " + report.summary.result);
            return ok;
        }

        /// <summary>Welt-Ebene zuerst, dann die Zellen-Szenen (das Streaming laedt sie additiv nach).</summary>
        private static string[] BuildScenes()
        {
            var list = new List<string> { SandboxSceneBuilder.ScenePath };
            list.AddRange(WorldManifestBuilder.CellScenes());
            return list.ToArray();
        }

        [MenuItem("Ashveil/Netzwerk/Test-Client starten", priority = 201)]
        public static void StartTestClient() => StartTestClient(false);

        [MenuItem("Ashveil/Netzwerk/Test-Client starten (mit Selbsttest)", priority = 202)]
        public static void StartTestClientWithSelfTest() => StartTestClient(true);

        /// <summary>Play starten und dabei automatisch den Selbsttest laufen lassen (Bericht in Logs/).</summary>
        [MenuItem("Ashveil/Test/Selbsttest (startet Play)", priority = 300)]
        public static void RunSelfTestInEditor()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SelfTestRunner.EditorFlagFile));
            File.WriteAllText(SelfTestRunner.EditorFlagFile, "1");
            if (!EditorApplication.isPlaying) StopTestClients(); // alter Client koennte Port 7770 belegen
            if (!EditorApplication.isPlaying) EditorApplication.isPlaying = true;
            else Debug.Log("[Ashveil] Selbsttest: laeuft beim naechsten Play-Start – oder im Spiel F10 druecken.");
        }

        /// <summary>Grafikstufen pruefen und Bildrate je Stufe messen – ohne und mit 50 Spielfiguren (Bericht Logs/Ashveil/Ashveil_Leistung.txt).</summary>
        [MenuItem("Ashveil/Test/Leistung (Grafikstufen, 50 Figuren)", priority = 307)]
        public static void RunPerformanceTest()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(PerformanceTest.FlagFile));
            File.WriteAllText(PerformanceTest.FlagFile, "1");
            StopTestClients();
            EditorApplication.isPlaying = true;
        }

        /// <summary>Massenschlacht: 100 → 3000 Figuren (500 gegen 500 bis volle Classic-Ebene) im Blickfeld (Bericht Ashveil_Massenschlacht.txt).</summary>
        [MenuItem("Ashveil/Test/Massenschlacht (bis 3000 Figuren)", priority = 309)]
        public static void RunMassBattleTest()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(MassBattleTest.FlagFile));
            File.WriteAllText(MassBattleTest.FlagFile, "1");
            StopTestClients();
            EditorApplication.isPlaying = true;
        }

        /// <summary>Massenschlacht im fertigen Spiel (ohne Editor-Last). Bericht: Builds/TestClient/Logs/Ashveil/Ashveil_Massenschlacht_Spiel.txt.</summary>
        [MenuItem("Ashveil/Test/Massenschlacht im fertigen Spiel (1080p)", priority = 310)]
        public static void RunMassBattleInGame() => RunBenchInGame(MassBattleTest.Arg, "Massenschlacht", "Ashveil_Massenschlacht_Spiel.txt", 8);

        /// <summary>
        /// Derselbe Leistungstest im fertigen Spiel (Test-Client, 1920 x 1080 im Fenster, eigener Host, ohne Editor-Last).
        /// Bericht: Builds/TestClient/Logs/Ashveil/Ashveil_Leistung_Spiel.txt. Das Spiel beendet sich danach selbst.
        /// </summary>
        [MenuItem("Ashveil/Test/Leistung im fertigen Spiel (1080p)", priority = 308)]
        public static void RunPerformanceTestInGame()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden – der Editor soll waehrend der Messung nichts rechnen.", "OK");
                return;
            }
            string exe = Path.GetFullPath(ClientExe);
            string stamp = File.Exists(StampFile) ? File.ReadAllText(StampFile).Trim() : null;
            if (!File.Exists(exe) || stamp != SceneHash())
            {
                if (EditorUtility.DisplayDialog("Ashveil", "Der Test-Client fehlt oder ist veraltet. Jetzt bauen? Danach das Menue erneut waehlen.", "Bauen", "Abbrechen"))
                    BuildTestClient();
                return;
            }
            StopTestClients();
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "-ashveil-host -ashveil-perftest -ashveil-profile perftest " + ZeitGestaltet + " -screen-fullscreen 0 -screen-width 1920 -screen-height 1080",
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false
            });
            Debug.Log("[Ashveil] Leistungstest im fertigen Spiel gestartet (ca. 2 Minuten) – Bericht: " +
                      Path.Combine(Path.GetDirectoryName(exe), "Logs", "Ashveil", "Ashveil_Leistung_Spiel.txt"));
        }

        /// <summary>
        /// Rendering-Bibel Phase B: Leistungs-Grundlinie im fertigen Spiel – 8 feste Blickpunkte (Startlager, Wald, Glockenmoor,
        /// Aschenfeld, Kolosseum-Vorplatz, Klippenschenke, Salzgruben, Arena) × 4 Grafikstufen, dazu Bilder je Ort.
        /// Bericht: Builds/TestClient/Logs/Ashveil/Ashveil_RenderBaseline.txt (+ .csv, Bilder unter RenderBaseline/).
        /// </summary>
        [MenuItem("Ashveil/Test/Rendering-Grundlinie im fertigen Spiel (1080p)", priority = 320)]
        public static void RunRenderBaselineInGame() => RunBenchInGame("-ashveil-renderbench", "Rendering-Grundlinie", "Ashveil_RenderBaseline.txt", 4);

        [MenuItem("Ashveil/Test/GPU-Driven-Vergleich im fertigen Spiel (1080p)", priority = 321)]
        public static void RunGpuDrivenInGame() => RunBenchInGame("-ashveil-renderbench-gpu", "GPU-Driven-Vergleich", "Ashveil_RenderGpuDriven.txt", 6);

        [MenuItem("Ashveil/Test/GPU-Driven-Vergleich unter DirectX 12 (1080p)", priority = 322)]
        public static void RunGpuDrivenDx12InGame() => RunBenchInGame("-ashveil-renderbench-gpu -force-d3d12", "GPU-Driven-Vergleich (DirectX 12)", "Ashveil_RenderGpuDriven.txt", 6);

        private static void RunBenchInGame(string flag, string title, string report, int minutes)
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden – der Editor soll waehrend der Messung nichts rechnen.", "OK");
                return;
            }
            string exe = Path.GetFullPath(ClientExe);
            string stamp = File.Exists(StampFile) ? File.ReadAllText(StampFile).Trim() : null;
            if (!File.Exists(exe) || stamp != SceneHash())
            {
                if (EditorUtility.DisplayDialog("Ashveil", "Der Test-Client fehlt oder ist veraltet. Jetzt bauen? Danach das Menue erneut waehlen.", "Bauen", "Abbrechen"))
                    BuildTestClient();
                return;
            }
            StopTestClients();
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"-ashveil-host {flag} -ashveil-profile perftest {ZeitGestaltet} -screen-fullscreen 0 -screen-width 1920 -screen-height 1080",
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false
            });
            Debug.Log($"[Ashveil] {title} im fertigen Spiel gestartet (ca. {minutes} Minuten) – Bericht: " +
                      Path.Combine(Path.GetDirectoryName(exe), "Logs", "Ashveil", report));
        }

        private static string StampFile => Path.Combine(ClientFolder, "scene.stamp");

        /// <summary>Stand von Szene + Laufzeit-Skripten (ohne Editor-Code) + Grafik-Einstellungen: aendert sich eins, ist der Test-Client veraltet.</summary>
        private static string SceneHash()
        {
            string scene = Path.GetFullPath(SandboxSceneBuilder.ScenePath);
            if (!File.Exists(scene)) return "";
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var files = new List<string> { scene };
                foreach (string cell in WorldManifestBuilder.CellScenes()) files.Add(Path.GetFullPath(cell));
                string scripts = Path.GetFullPath(ProjectSetup.Root + "/Scripts");
                string editor = Path.Combine(scripts, "Editor");
                foreach (string f in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
                    if (!f.StartsWith(editor, System.StringComparison.OrdinalIgnoreCase)) files.Add(f);
                string data = Path.GetFullPath(ProjectSetup.Root + "/Data/Appearances/appearances.json");
                if (File.Exists(data)) files.Add(data);
                // Render-Pipeline- und Grafik-Einstellungen gehoeren zum Build (z. B. GPU Resident Drawer an/aus)
                if (Directory.Exists("Assets/Settings"))
                    foreach (string f in Directory.GetFiles("Assets/Settings", "*.asset", SearchOption.AllDirectories)) files.Add(Path.GetFullPath(f));
                foreach (string f in new[] { "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset", "ProjectSettings/ProjectSettings.asset" })
                    if (File.Exists(f)) files.Add(Path.GetFullPath(f));
                files.Sort(System.StringComparer.Ordinal);
                foreach (string f in files)
                {
                    byte[] bytes = File.ReadAllBytes(f);
                    md5.TransformBlock(bytes, 0, bytes.Length, null, 0);
                }
                md5.TransformFinalBlock(new byte[0], 0, 0);
                return System.BitConverter.ToString(md5.Hash).Replace("-", "");
            }
        }

        /// <summary>
        /// Raeumt Temp/BurstOutput vorab auf. Liegt das Projekt in OneDrive, sperrt die Synchronisation
        /// dort gelegentlich Ordner – der Build scheitert dann mit "Access denied".
        /// </summary>
        internal static void ClearBurstOutput()
        {
            string dir = Path.GetFullPath("Temp/BurstOutput");
            if (!Directory.Exists(dir)) return;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        File.SetAttributes(f, FileAttributes.Normal);
                    foreach (string d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories))
                        new DirectoryInfo(d).Attributes = FileAttributes.Directory;
                    Directory.Delete(dir, true);
                    return;
                }
                catch (System.Exception e)
                {
                    if (attempt == 4) Debug.LogWarning("[Ashveil] Temp/BurstOutput liess sich nicht leeren: " + e.Message);
                    else System.Threading.Thread.Sleep(500);
                }
            }
        }

        /// <summary>Abnahmetest 3.4: Editor (Host, Spieler A) + Test-Client (Spieler B) besiegen gemeinsam einen Gegner.</summary>
        [MenuItem("Ashveil/Test/Zwei-Spieler-Test (Beute und Inventar)", priority = 301)]
        public static void RunDuoTest() => RunTwoPlayerTest("1");

        /// <summary>Abnahmetest Milestone 4: neue Charaktere, Tutorialkette bis Stufe 3, Erfahrung, Handel, Speichern.</summary>
        [MenuItem("Ashveil/Test/Fortschritt (Stufen, Auftraege, Handel)", priority = 302)]
        public static void RunProgressTest() => RunTwoPlayerTest(DuoTestDirector.ProgressFlag);

        /// <summary>Milestone 5: Gruppe und Chat mit drei Spielern (Editor + zwei Test-Clients).</summary>
        [MenuItem("Ashveil/Test/Gruppe und Chat (3 Spieler)", priority = 303)]
        public static void RunSocialTest() => RunTwoPlayerTest(DuoTestDirector.SocialFlag);

        /// <summary>Milestone 5.4: Gelaende, NavMesh, neue Gegner und Glockenhirte (Editor + ein Test-Client).</summary>
        [MenuItem("Ashveil/Test/Kareth (Gelaende, Gegner, Boss)", priority = 304)]
        public static void RunKarethTest() => RunTwoPlayerTest(DuoTestDirector.KarethFlag);

        /// <summary>Milestone 5.5: vier Spieler gemeinsam (Editor + drei Test-Clients), ca. 12 Minuten.</summary>
        [MenuItem("Ashveil/Test/Gruppenabenteuer (4 Spieler)", priority = 305)]
        public static void RunGroupTest() => RunTwoPlayerTest(DuoTestDirector.GroupFlag);

        /// <summary>Wie oben, aber Dauerbetrieb bis 30 Minuten Gesamtzeit (Abnahme "4 Spieler spielen 30 min").</summary>
        [MenuItem("Ashveil/Test/Salzgruben (Instanzen, 3 Spieler)", priority = 309)]
        public static void RunSaltMineTest() => RunTwoPlayerTest(DuoTestDirector.MineFlag);

        [MenuItem("Ashveil/Test/Actionbar (2 Spieler)", priority = 310)]
        public static void RunActionBarTest() => RunTwoPlayerTest(DuoTestDirector.BarFlag);

        [MenuItem("Ashveil/Test/Emotes (3 Spieler)", priority = 312)]
        public static void RunEmoteTest() => RunTwoPlayerTest(DuoTestDirector.EmoteFlag);

        /// <summary>Large World: Kareth + Grenzgebirge als eine Welt (Tunnel, Laden/Entladen, Speicherstand, 5x hin und zurueck).</summary>
        [MenuItem("Ashveil/Test/Weltzellen (Streaming, 3 Spieler)", priority = 318)]
        public static void RunWorldTest() => RunTwoPlayerTest(DuoTestDirector.WorldFlag);

        /// <summary>Kolosseum Pass 2: Vorplatz, sechs Arenen, Gruppe/Solo, Tod, Verlassen, Sichtbarkeit je Instanz.</summary>
        [MenuItem("Ashveil/Test/Kolosseum (Greybox, 3 Spieler)", priority = 319)]
        public static void RunArenaTest() => RunTwoPlayerTest(DuoTestDirector.ArenaFlag);

        /// <summary>Combat-Masterplan: Kampf-Prototyp Grubenschrat (Koerperteile, Moveset, Niederwurf, Tod, Beute).</summary>
        [MenuItem("Ashveil/Test/Kampf-Prototyp (Grubenschrat, 3 Spieler)", priority = 319)]
        public static void RunCombatTest() => RunTwoPlayerTest(DuoTestDirector.CombatFlag);

        /// <summary>Praxis-Meisterschaften, erster Prototyp: Laufkunst und Ausweichen (Lernen, Wirkung, Anzeige, Speichern).</summary>
        [MenuItem("Ashveil/Test/Meisterschaften (2 Spieler)", priority = 319)]
        public static void RunMasteryTest() => RunTwoPlayerTest(DuoTestDirector.MasteryFlag);

        /// <summary>Kolosseum Pass 4: Glocke, HUD, Champions 5:00/10:00, Wiederbeleben, Prüfpunkt-Truhe, Wipe.</summary>
        [MenuItem("Ashveil/Test/Kolosseum-Lauf (Pass 4, 2 Spieler)", priority = 319)]
        public static void RunArenaRunTest() => RunTwoPlayerTest(DuoTestDirector.ArenaRunFlag);

        /// <summary>Klassen-Identität Phase 2: Kriegspferd des Eidwächters.</summary>
        [MenuItem("Ashveil/Test/Reittier (2 Spieler)", priority = 319)]
        public static void RunMountTest() => RunTwoPlayerTest(DuoTestDirector.MountFlag);

        /// <summary>Im Play-Modus: dem eigenen Eidwächter das Kriegspferd geben (bis die Klassenquest fertig ist).</summary>
        [MenuItem("Ashveil/Test/Kriegspferd freischalten (Play-Modus, eigene Figur)", priority = 330)]
        public static void UnlockWarhorse()
        {
            if (!Application.isPlaying) { EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play drücken und einloggen.", "OK"); return; }
            foreach (NetworkProgression p in Object.FindObjectsByType<NetworkProgression>(FindObjectsSortMode.None))
            {
                if (!p.IsServerInitialized || !p.IsOwner) continue;
                string why = p.ServerUnlockMount(Ashveil.Data.MountCatalog.WarhorseId);
                EditorUtility.DisplayDialog("Ashveil", why.Length == 0 ? "Das Kriegspferd liegt jetzt auf deiner Leiste. Klick darauf zum Aufsitzen." : why, "OK");
                return;
            }
            EditorUtility.DisplayDialog("Ashveil", "Keine eigene Figur gefunden (Play als Host starten).", "OK");
        }

        /// <summary>Im Play-Modus: dem eigenen Heiler (Aschenweber) die Nachlese geben (bis die Klassenquest fertig ist).</summary>
        [MenuItem("Ashveil/Test/Nachlese freischalten (Play-Modus, eigener Aschenweber)", priority = 331)]
        public static void UnlockSalvage()
        {
            if (!Application.isPlaying) { EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play drücken und einloggen.", "OK"); return; }
            foreach (NetworkProgression p in Object.FindObjectsByType<NetworkProgression>(FindObjectsSortMode.None))
            {
                if (!p.IsServerInitialized || !p.IsOwner) continue;
                string why = p.ServerUnlockSalvage();
                EditorUtility.DisplayDialog("Ashveil", why.Length == 0
                    ? "Nachlese erlernt. Nach einem Kampf gegen einen Grubenschrat glimmt seine Leiche für dich grün: Rechtsklick zum Bergen (nach der Beute)."
                    : why, "OK");
                return;
            }
            EditorUtility.DisplayDialog("Ashveil", "Keine eigene Figur gefunden (Play als Host starten).", "OK");
        }

        [MenuItem("Ashveil/Test/Pferdehof (2 Spieler)", priority = 322)]
        public static void RunHorseStoryTest() => RunTwoPlayerTest(DuoTestDirector.HorseFlag);

        [MenuItem("Ashveil/Test/Klassen und Fähigkeiten (2 Spieler)", priority = 324)]
        public static void RunClassesTest() => RunTwoPlayerTest(DuoTestDirector.ClassesFlag);

        /// <summary>P2 Balance: echte Kaempfe je Klasse und Stufe – nur der Editor spielt, kein Test-Client noetig.</summary>
        [MenuItem("Ashveil/Test/Balance (1 Spieler, Messung)", priority = 325)]
        public static void RunBalanceTest()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden – der Test startet Play selbst.", "OK");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(DuoTestDirector.FlagFile));
            File.WriteAllText(DuoTestDirector.FlagFile, DuoTestDirector.BalanceFlag);
            EditorApplication.isPlaying = true;
        }

        /// <summary>P3 Glockenrast: eine neue Figur spielt G1–G13 von Stufe 5 bis 9 – nur der Editor spielt, kein Test-Client noetig.</summary>
        [MenuItem("Ashveil/Test/Glockenrast (1 Spieler)", priority = 327)]
        public static void RunGlockenrastTest()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden – der Test startet Play selbst.", "OK");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(DuoTestDirector.FlagFile));
            File.WriteAllText(DuoTestDirector.FlagFile, DuoTestDirector.GlockeFlag);
            EditorApplication.isPlaying = true;
        }

        /// <summary>P3 Glockenrast mit zwei Figuren (12h-Block): Entdeckung, Fundstuecke, Siege und Auftragsbeute je Figur.</summary>
        [MenuItem("Ashveil/Test/Glockenrast (2 Spieler)", priority = 328)]
        public static void RunGlockenrastDuoTest() => RunTwoPlayerTest(DuoTestDirector.GlockeDuoFlag);

        /// <summary>CORE-008 (02.10.): Spawn-Fairness (kein Erscheinen am Spieler) und Gegnerdichte nach Spielerzahl.</summary>
        [MenuItem("Ashveil/Test/Spawn-Fairness und Dichte (2 Spieler)", priority = 329)]
        public static void RunSpawnDensityTest() => RunTwoPlayerTest(DuoTestDirector.SpawnDensityFlag);

        [MenuItem("Ashveil/Test/Balance Gruppe (3 Spieler, Elite)", priority = 326)]
        public static void RunBalanceGroupTest() => RunTwoPlayerTest(DuoTestDirector.BalanceGroupFlag);

        [MenuItem("Ashveil/Test/Fernkampf und Taschen (2 Spieler)", priority = 323)]
        public static void RunRangedTest() => RunTwoPlayerTest(DuoTestDirector.RangedFlag);

        [MenuItem("Ashveil/Test/Gefährte (2 Spieler)", priority = 321)]
        public static void RunCompanionTest() => RunTwoPlayerTest(DuoTestDirector.CompanionFlag);

        /// <summary>Im Play-Modus: der eigenen Aschenjaegerin das Dominieren geben (bis die Klassenquest fertig ist).</summary>
        [MenuItem("Ashveil/Test/Dominieren freischalten (Play-Modus, eigene Aschenjägerin)", priority = 332)]
        public static void UnlockDominate()
        {
            if (!Application.isPlaying) { EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play drücken und einloggen.", "OK"); return; }
            foreach (NetworkProgression p in Object.FindObjectsByType<NetworkProgression>(FindObjectsSortMode.None))
            {
                if (!p.IsServerInitialized || !p.IsOwner) continue;
                string why = p.ServerUnlockDominate();
                EditorUtility.DisplayDialog("Ashveil", why.Length == 0
                    ? "Dominieren liegt auf deiner Leiste. Schwäche einen Aschwolf unter 35 % Leben, wähle ihn als Ziel und halte „Dominieren“ 3 s."
                    : why, "OK");
                return;
            }
            EditorUtility.DisplayDialog("Ashveil", "Keine eigene Figur gefunden (Play als Host starten).", "OK");
        }

        [MenuItem("Ashveil/Test/Nachlese (2 Spieler)", priority = 320)]
        public static void RunSalvageTest() => RunTwoPlayerTest(DuoTestDirector.SalvageFlag);

        [MenuItem("Ashveil/Test/Lagerleben (2 Spieler)", priority = 314)]
        public static void RunCampLifeTest() => RunTwoPlayerTest(DuoTestDirector.CampLifeFlag);

        [MenuItem("Ashveil/Test/Himmel (30 Tage, 2 Spieler)", priority = 315)]
        public static void RunSkyTest() => RunTwoPlayerTest(DuoTestDirector.SkyFlag);

        [MenuItem("Ashveil/Test/Figuren-Silhouetten (2, 5, 15, 30 m)", priority = 316)]
        public static void RunSilhouetteTest() => RunTwoPlayerTest(DuoTestDirector.SilhouetteFlag);

        [MenuItem("Ashveil/Test/Tageszeit (3 Spieler)", priority = 313)]
        public static void RunDayNightTest() => RunTwoPlayerTest(DuoTestDirector.DayNightFlag);

        [MenuItem("Ashveil/Test/Charakter-Editor (2 Spieler)", priority = 311)]
        public static void RunCreatorTest() => RunTwoPlayerTest(DuoTestDirector.CreatorFlag);

        /// <summary>M7: Play mit einem ganz neuen Profil – der Charakter-Editor oeffnet sich (legt einen Speicherstand "neu-…" an).</summary>
        [MenuItem("Ashveil/Charakter-Editor ausprobieren", priority = 60)]
        public static void TryCharacterCreator()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ProfileSelection.NewCharacterFlagFile));
            File.WriteAllText(ProfileSelection.NewCharacterFlagFile, "1");
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Ashveil/Test/Gruppenabenteuer 30 Minuten (4 Spieler)", priority = 306)]
        public static void RunGroupLongTest() => RunTwoPlayerTest(DuoTestDirector.GroupLongFlag);

        private static void RunTwoPlayerTest(string flag)
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden – der Test startet Play selbst.", "OK");
                return;
            }
            string exe = Path.GetFullPath(ClientExe);
            string stamp = File.Exists(StampFile) ? File.ReadAllText(StampFile).Trim() : null;
            if (!File.Exists(exe) || stamp != SceneHash())
            {
                if (!EditorUtility.DisplayDialog("Ashveil", "Der Test-Client fehlt oder ist veraltet. Jetzt bauen und danach den Test starten?", "Bauen & testen", "Abbrechen"))
                    return;
                BuildTestClient();
                stamp = File.Exists(StampFile) ? File.ReadAllText(StampFile).Trim() : null;
                if (stamp != SceneHash()) return;
            }
            StopTestClients();
            Directory.CreateDirectory(Path.GetDirectoryName(DuoTestDirector.FlagFile));
            File.WriteAllText(DuoTestDirector.FlagFile, flag);
            EditorApplication.isPlaying = true;
        }

        /// <summary>Beendet nur Builds/TestClient/Ashveil.exe; gleichnamige andere Spielbuilds bleiben erhalten.</summary>
        [MenuItem("Ashveil/Netzwerk/Test-Clients beenden", priority = 203)]
        public static void StopTestClients()
        {
            int killed = 0;
            foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ClientExe)))
            {
                try
                {
                    if (p.Id == Process.GetCurrentProcess().Id) continue;
                    if (!IsTestClientExecutable(p.MainModule?.FileName)) continue;
                    p.Kill();
                    p.WaitForExit(3000);
                    killed++;
                }
                catch (System.Exception e) { Debug.LogWarning("[Ashveil] Test-Client konnte nicht beendet werden: " + e.Message); }
                finally { p.Dispose(); }
            }
            if (killed > 0) Debug.Log($"[Ashveil] {killed} laufende(n) Test-Client(s) beendet.");
        }

        private static bool IsTestClientExecutable(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable)) return false;
            try
            {
                if (!Path.IsPathRooted(executable)) return false;
                return string.Equals(Path.GetFullPath(executable), Path.GetFullPath(ClientExe), System.StringComparison.OrdinalIgnoreCase);
            }
            catch (System.ArgumentException) { return false; }
            catch (System.NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }

        private static void StartTestClient(bool selfTest)
        {
            string exe = Path.GetFullPath(ClientExe);
            if (!File.Exists(exe))
            {
                if (EditorApplication.isPlaying)
                {
                    EditorUtility.DisplayDialog("Ashveil", "Es gibt noch keinen Test-Client. Bitte Play beenden und 'Ashveil > Netzwerk > Test-Client bauen' ausfuehren.", "OK");
                    return;
                }
                if (EditorUtility.DisplayDialog("Ashveil", "Es gibt noch keinen Test-Client. Jetzt bauen? (dauert beim ersten Mal einige Minuten)", "Bauen", "Abbrechen"))
                    BuildTestClient();
                return;
            }
            // Szene nach dem Client-Build neu erstellt? Dann passen die Netzwerk-IDs nicht mehr zusammen.
            // (Unity laesst die exe bei neuem Build unveraendert – daher ein Stempel mit dem Szenen-Hash.)
            string stamp = File.Exists(StampFile) ? File.ReadAllText(StampFile).Trim() : null;
            if (stamp != SceneHash())
            {
                if (EditorApplication.isPlaying)
                {
                    EditorUtility.DisplayDialog("Ashveil", "Die Szene wurde nach dem letzten Test-Client neu erstellt.\n\n" +
                        "Bitte Play beenden und 'Ashveil > Netzwerk > Test-Client bauen' ausfuehren.", "OK");
                    return;
                }
                if (EditorUtility.DisplayDialog("Ashveil", "Die Szene wurde nach dem letzten Test-Client neu erstellt. Test-Client jetzt neu bauen?", "Neu bauen", "Abbrechen"))
                    BuildTestClient();
                return;
            }
            if (!EditorApplication.isPlaying)
                Debug.Log("[Ashveil] Hinweis: Der Test-Client verbindet sich mit dem Editor. Druecke Play, damit der Editor als Host laeuft.");
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = (selfTest ? "" : "-ashveil-profile maltebot ") + "-ashveil-join 127.0.0.1 -screen-fullscreen 0 -screen-width 1280 -screen-height 720" + (selfTest ? " -ashveil-selftest" : ""),
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false
            });
        }
    }
}
