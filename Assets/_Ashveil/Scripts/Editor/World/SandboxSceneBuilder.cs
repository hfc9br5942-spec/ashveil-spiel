using System.Collections.Generic;
using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Gameplay.CameraSystem;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Debugging;
using Ashveil.UI;
using Ashveil.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Baut per Menueklick die komplette Sandbox-Szene (Milestone 1 + 2).
    ///
    ///   Ashveil > Sandbox-Szene erstellen
    ///
    /// Inhalt: Wachtfeuer (Spawn, NPC-Platzhalter, Haendler, Trainingsplatz), Bewegungs-Testparcours,
    /// Greybox-Landmarken der Senke von Kareth (Gefallener Waechter, Aschenweide, Granitplateau mit
    /// Kloster, Uhrwerk, Burg Veyrholt, Salzgruben, Aschenfeld mit wanderndem Glockenhirten),
    /// Licht und Nebel, Spieler-Prefab und MMO-Kamera.
    ///
    /// Die Szene ist ein Test- und Vorschauraum. Die echte Zone "Kareth" wird in Milestone 5 als
    /// Terrain gebaut; Masse und Sichtachsen von hier werden uebernommen.
    /// </summary>
    public static partial class SandboxSceneBuilder
    {
        public const string ScenePath = ProjectSetup.Root + "/Scenes/Sandbox_M1.unity";
        public const string PlayerPrefabPath = ProjectSetup.Root + "/Prefabs/Characters/Player_Placeholder.prefab";

        private static readonly Vector3 SpawnPosition = new Vector3(0f, 0.05f, -7f);

        // Farbpalette (dunkel, aber mit warmen/violetten Akzenten)
        private static Material _ground, _moss, _dirt, _moor, _ash, _stone, _stoneDark, _granite, _wood, _woodDark,
            _canvas, _straw, _bark, _metal, _bronze, _charred, _fire, _warmWindow, _violetGlow, _emberGlow,
            _coldGlow, _testGrey, _testAccent, _playerBody, _playerAccent, _npc, _npcQuest, _salt;
        private static Transform _playerSpawn;

        [MenuItem("Ashveil/Sandbox-Szene erstellen", priority = 1)]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst den Play-Modus beenden (Play-Knopf oben), dann die Szene neu erstellen.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
                !EditorUtility.DisplayDialog("Ashveil", "Sandbox_M1 wird aus dem Kareth-Bauplan neu erzeugt (gleiches Ergebnis wie bisher, Layout und Handarbeit bleiben erhalten). Der jetzige Stand wird vorher unter Backups/Kareth gesichert. Fortfahren?", "Neu erzeugen", "Abbrechen"))
                return;

            // Namensschilder brauchen die TextMeshPro-Grundressourcen (Schrift + Shader).
            // Fehlen sie, werden sie automatisch importiert und der Aufbau danach fortgesetzt.
            if (!TmpResources.EnsureImported(BuildInternal)) return;
            BuildInternal();
        }

        private static void BuildInternal()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Ashveil", "Projekt vorbereiten ...", 0.05f);
                ProjectSetup.EnsureFolders();
                ProjectSetup.EnsureLayers();

                // Quaternius-Pakete aus "SourceArt/ThirdParty" automatisch einlesen (Figuren, Animationen, Texturen)
                if ((!QuaterniusImport.IsImported || !QuaterniusImport.BaseCharactersImported) && QuaterniusImport.HasZipsToImport)
                {
                    EditorUtility.DisplayProgressBar("Ashveil", "Importiere Figuren & Animationen (einmalig, dauert etwas) ...", 0.1f);
                    if (QuaterniusImport.Run(out string report) >= 0) PlayerAnimatorBuilder.Build();
                    else Debug.LogWarning("[Ashveil] " + report);
                }
                K.ResetCache();
                // Art-Bible "Asche & Glut": URP-Qualitaet und – falls noch nicht vorhanden – Blender-Modelle erzeugen
                ArtStyleSetup.ApplyPipelineSettings();
                if (!ArtModels.Has("SM_Pine_A") && ArtPipeline.FindBlender() != null)
                {
                    EditorUtility.DisplayProgressBar("Ashveil", "Blender erzeugt Modelle (einmalig) ...", 0.08f);
                    if (!ArtPipeline.BuildWithBlender(out string artSummary)) Debug.LogWarning("[Ashveil] " + artSummary);
                }
                else ArtPipeline.EnsureMaterials();
                // Nachtrag 26.09.: der versteinerte Beschuetzer (Modelle einmalig aus Blender, Layout aus KarethLayout)
                GiantBuilder.EnsureModels();
                GiantBuilder.Prepare();
                CreateMaterials();

                // Handarbeit der alten Szene sichern, bevor sie ersetzt wird
                if (GiantBuilder.Layout.keepHandwork) HandworkKeeper.Capture(ScenePath);
                // Kanon 26.09.: vor jedem World-Pass den bestehenden Stand sichern
                KarethBackup.Run();
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                EditorUtility.DisplayProgressBar("Ashveil", "Licht & Atmosphaere ...", 0.15f);
                SetupLighting();

                Transform env = K.Group("Environment", null);
                EditorUtility.DisplayProgressBar("Ashveil", "Gelaende ...", 0.25f);
                BuildGround(env);
                EditorUtility.DisplayProgressBar("Ashveil", "Wachtfeuer ...", 0.35f);
                BuildCamp(env);
                EditorUtility.DisplayProgressBar("Ashveil", "Testparcours ...", 0.5f);
                BuildTestCourse(env);
                EditorUtility.DisplayProgressBar("Ashveil", "Landmarken ...", 0.65f);
                BuildLandmarks(env);
                // 27.09.: Grenzgebirge – Tunnel am Ende des Ostwegs + Fernansicht (die Zellen selbst sind eigene Szenen)
                BorderTunnel.Build(env);
                // 28.09.: Kolosseum Pass 2 – Vorplatz im Nordosten des Aschenfelds + Arena-Instanzen (InstanceLayout)
                ColosseumBuilder.BuildInto(env);

                EditorUtility.DisplayProgressBar("Ashveil", "Spieler & Kamera ...", 0.85f);
                GameObject prefab = BuildPlayerPrefab();

                // Milestone 3.3: Gegner (Aschwoelfe, Verblichene), ihr Lager und das NavMesh
                EditorUtility.DisplayProgressBar("Ashveil", "Gegner ...", 0.9f);
                // Milestone 3.4: Klassen, Berufe, Gegenstaende, Rezepte, Beutetabellen
                ItemContentBuilder.Build();
                NetworkSetupBuilder.AddSpawnables(EnemyBuilder.Build(env));
                CompanionBuilder.Build();   // Klassen-Identitaet Phase 4: Aschwolf-Gefaehrte (nach dem Aschwolf)
                // 27.09.: questrelevante Orte (Stuetzpunkte je Stufenband, reservierte Anker) – Tabelle KarethQuestSites
                KarethQuestSites.Build(env);
                ItemContentBuilder.AssignLootTables();
                // Milestone 4: NPCs, Haendler und die ersten Auftraege
                QuestContentBuilder.Build();
                // M8.3: Bewohner des Startlagers (echte Koerper, Tagesablaeufe) – nach dem Animator-Neubau
                if (_campRoot != null) BuildCampNpcs(_campRoot);
                // M8.6: Reisende auf den sicheren Wegen (Salzfuhrwerk, Pilger, liegengebliebener Wagen)
                if (_campRoot != null) BuildRoadLife(_campRoot.parent);
                // P3 Glockenrast (Stufe 5–9): Figuren, Erkundungsziele, Fundstellen der Auftraege G1–G13
                if (_campRoot != null) BuildGlockenrast(_campRoot.parent);
                // Art-Pass 4: Silhouetten-Buehne (ausgeblendet, nur fuer den Silhouettentest)
                if (_campRoot != null) BuildSilhouetteStage(_campRoot.parent);
                // Dynamic Sky Pass 11: Gebietsluft (Startlager neutral, Glockenmoor dunstig) mit weichen Blendzonen
                if (_campRoot != null)
                    SkyContentBuilder.PlaceBiomes(_campRoot.parent, Vector3.zero,
                        new Vector3(KarethTerrainBuilder.MoorCenter.x, 0f, KarethTerrainBuilder.MoorCenter.y));
                // Sichtbare Ausruestung: Aussehen je Item, Frisuren/Hauttoene/Farben
                AppearanceCatalog appearanceCatalog = AppearanceContentBuilder.Build();
                // M6.6: Faehigkeiten fuer Actionbar und Faehigkeitenbuch
                AbilityContentBuilder.Build();
                // P1: Klassenfaehigkeiten, Talente, Ressourcen, Klassenlehrer
                SpellContentBuilder.Build();
                // P2: Gegnerrollen der Balance-Tabelle
                BalanceRoleBuilder.Apply();
                DefinitionDatabase database = DefinitionValidator.ValidateAndRebuild();

                BuildCamera();
                ArtStyleSetup.SetupAtmosphere(RenderSettings.sun);
                // Milestone 3: Spieler kommt vom Server (Editor startet automatisch als Host)
                NetworkSetupBuilder.BuildNetworkManager(prefab, _playerSpawn, database, ItemContentBuilder.DefaultClass, ItemContentBuilder.Fists, appearanceCatalog);

                // Large-World E1: Weltzellen nach Bedarf laden (Grenzgebirge)
                WorldManifestBuilder.EnsureStreamer();
                new GameObject("DebugHud").AddComponent<DebugHud>();
                new GameObject("WorldLabels").AddComponent<WorldLabelRenderer>();
                new GameObject("HUD").AddComponent<HudRoot>();
                new GameObject("CombatText").AddComponent<CombatTextRenderer>();
                new GameObject("Inventory UI").AddComponent<InventoryUi>();
                new GameObject("Quest UI").AddComponent<QuestUi>();
                new GameObject("Social UI").AddComponent<SocialUi>();

                // Milestone 3.2: Puppen & Co. sind Netzwerkobjekte (Leben kommt vom Server)
                NetworkSetupBuilder.MakeSceneDamageablesNetworked();

                // Handarbeit wieder einsetzen (vor dem NavMesh, damit sie mitgebacken wird)
                if (GiantBuilder.Layout.keepHandwork) HandworkKeeper.Restore();

                // 30.09.: erzeugte Welt (Terrain-Generator V1: Umland, Meer, Fernansicht) VOR dem Kareth-NavMesh setzen –
                // nur vorhandene GEN-Assets, nichts neu gerechnet. Das Kareth-NavMesh ist auf sein Volumen begrenzt und
                // nimmt so nur den Streifen am Ostportal des Tunnels mit (dort endet die NavMesh-Verbindung).
                TerrainGen.GeneratedWorldPlacement.PlaceIntoOpenScene(false);
                Physics.SyncTransforms();

                // Milestone 5.4: NavMesh der ganzen Senke einmal im Editor backen (statt bei jedem Serverstart)
                EditorUtility.DisplayProgressBar("Ashveil", "NavMesh fuer Kareth backen (dauert etwas) ...", 0.95f);
                EnemyBuilder.BakeNavMesh(ProjectSetup.Root + "/Scenes/Sandbox_M1_NavMesh.asset");

                DynamicGI.UpdateEnvironment();
                EditorSceneManager.SaveScene(scene, ScenePath);
                // Szenen-IDs haengen am Pfad – deshalb nach dem ersten Speichern vergeben und erneut speichern
                NetworkSetupBuilder.GenerateSceneIds(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                // M6: NavMesh der Salzgruben erst nach dem Speichern backen – vorher findet das Backen die frischen
                // Kollider der Instanz-Plaetze nicht zuverlaessig (0,1 s, leeres NavMesh)
                EditorUtility.DisplayProgressBar("Ashveil", "NavMesh fuer die Salzgruben backen ...", 0.97f);
                SaltMineBuilder.BakeNavMesh();
                ColosseumBuilder.BakeNavMesh();
                EditorSceneManager.SaveScene(scene, ScenePath);
                AddSceneToBuildSettings(ScenePath);
                AssetDatabase.SaveAssets();

                // Vorlagen-Reste und veraltete Dateien entfernen (jetzt, da die alte Szene nicht mehr offen ist).
                ProjectCleanup.Run(false);

                // Gespeicherte Szene frisch laden: per Skript erzeugte Netzwerkobjekte sind erst nach dem Laden
                // vollstaendig (FishNet erwartet deserialisierte Felder – wichtig bei "Enter Play Mode Options").
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                Debug.Log("[Ashveil] Sandbox_M1 erstellt. Druecke Play. Steuerung: F1.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // =================================================================== Materialien

        private static void CreateMaterials()
        {
            _ground = K.Mat("Ground_Moss", new Color(0.23f, 0.27f, 0.19f));
            _moss = K.Mat("Moss", new Color(0.2f, 0.3f, 0.16f));
            _dirt = K.Mat("Path_Dirt", new Color(0.33f, 0.27f, 0.2f));
            _moor = K.Mat("Moor_Wet", new Color(0.15f, 0.2f, 0.17f), 0.55f);
            _ash = K.Mat("Ash_Ground", new Color(0.16f, 0.15f, 0.15f));
            _stone = K.Mat("Stone_Light", new Color(0.53f, 0.52f, 0.49f));
            _stoneDark = K.Mat("Stone_Dark", new Color(0.26f, 0.26f, 0.29f));
            _granite = K.Mat("Granite", new Color(0.42f, 0.43f, 0.46f));
            _wood = K.Mat("Wood", new Color(0.36f, 0.25f, 0.16f));
            _woodDark = K.Mat("Wood_Dark", new Color(0.2f, 0.14f, 0.1f));
            _canvas = K.Mat("Canvas", new Color(0.55f, 0.48f, 0.37f));
            _straw = K.Mat("Straw", new Color(0.7f, 0.6f, 0.34f));
            _bark = K.Mat("Bark_Dead", new Color(0.19f, 0.16f, 0.14f));
            _metal = K.Mat("Metal_Dark", new Color(0.2f, 0.21f, 0.23f), 0.45f);
            _bronze = K.Mat("Bronze", new Color(0.52f, 0.37f, 0.19f), 0.6f);
            _charred = K.Mat("Charred", new Color(0.1f, 0.09f, 0.08f));
            _salt = K.Mat("Salt", new Color(0.82f, 0.8f, 0.76f), 0.3f);
            _fire = K.Mat("Glow_Fire", new Color(1f, 0.5f, 0.15f), 0f, new Color(4f, 1.6f, 0.4f));
            _warmWindow = K.Mat("Glow_Window", new Color(1f, 0.7f, 0.35f), 0f, new Color(3f, 1.8f, 0.7f));
            _violetGlow = K.Mat("Glow_Violet", new Color(0.6f, 0.3f, 1f), 0f, new Color(1.6f, 0.5f, 3f));
            _emberGlow = K.Mat("Glow_Ember", new Color(1f, 0.35f, 0.1f), 0f, new Color(3f, 0.8f, 0.15f));
            _coldGlow = K.Mat("Glow_Cold", new Color(0.4f, 0.7f, 1f), 0f, new Color(0.6f, 1.4f, 3f));
            _testGrey = K.Mat("Test_Grey", new Color(0.5f, 0.5f, 0.52f));
            _testAccent = K.Mat("Test_Accent", new Color(0.75f, 0.55f, 0.25f));
            _playerBody = K.Mat("Player_Body", new Color(0.55f, 0.6f, 0.68f), 0.3f);
            _playerAccent = K.Mat("Player_Accent", new Color(0.95f, 0.75f, 0.35f), 0.2f, new Color(0.6f, 0.4f, 0.1f));
            _npc = K.Mat("NPC_Neutral", new Color(0.45f, 0.42f, 0.38f));
            _npcQuest = K.Mat("NPC_Quest", new Color(0.6f, 0.45f, 0.2f));
            AssetDatabase.SaveAssets();
        }

        // =================================================================== Licht

        private static void SetupLighting()
        {
            var sunGo = new GameObject("Sun (tiefe Abendsonne)");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.8f, 0.6f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(24f, -35f, 0f);
            RenderSettings.sun = sun;

            var sky = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            if (sky != null) RenderSettings.skybox = sky;

            // Kuehle Grundstimmung, warme Akzente kommen von Feuern und Fenstern.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.4f, 0.45f, 0.56f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.3f, 0.32f);
            RenderSettings.ambientGroundColor = new Color(0.11f, 0.1f, 0.09f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.5f, 0.54f, 0.6f);
            RenderSettings.fogDensity = 0.0019f; // Kareth: Landmarken bis ~1 km sichtbar
            ArtStyleSetup.SetupLighting(sun); // Art-Bible: Sonne, Himmel, Umgebungslicht, eigener Hoehennebel
        }

        // =================================================================== Gelaende

        private static void BuildGround(Transform env)
        {
            // Milestone 5.4: die ganze Senke von Kareth als Terrain (1,2 x 1,2 km), Wege sind in den Boden gemalt
            KarethTerrainBuilder.Build(env);
        }

        // =================================================================== Wachtfeuer (Spawn)

        private static Vector3 OnRing(float angleDeg, float radius, float y = 0f)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius);
        }

        // BuildCamp: siehe SandboxSceneBuilder.Camp.cs (Startlager, ab M8 vergroessert)

        private static void Npc(Transform parent, string name, string title, Vector3 pos, Material mat, string npcId, float? faceYaw = null)
        {
            Vector3 toFire = -pos;
            toFire.y = 0f;
            float yaw = faceYaw ?? (toFire.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toFire).eulerAngles.y : 0f);
            Transform npc = K.Group(name, parent, pos, new Vector3(0f, yaw, 0f));

            GameObject body = K.Prim(PrimitiveType.Capsule, "Body", npc, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), Vector3.zero, mat);
            K.Box("Face", npc, new Vector3(0f, 1.5f, 0.28f), new Vector3(0.35f, 0.1f, 0.1f), _stoneDark, default, false);
            K.SetLayerRecursively(npc.gameObject, LayerMask.NameToLayer(GameLayers.Interactable));
            K.MakeDynamic(body);

            // Markierungen (! / ?) setzt die Oberflaeche je Spieler – deshalb freundlicher Stil ohne festes "!"
            K.Label(npc, new Vector3(0f, 2.15f, 0f), name, title, WorldLabelStyle.Friendly, 40f);
            npc.gameObject.AddComponent<Ashveil.Gameplay.Quests.NpcIdentity>().Configure(npcId);
        }

        private static void BuildLookout(Transform parent, Vector3 pos)
        {
            Transform rock = K.Group("Aussichtsfels", parent, pos, new Vector3(0f, 25f, 0f));
            Vector3[] steps = { new Vector3(-3.2f, 0f, -2.2f), new Vector3(-1.4f, 0f, -3.3f), new Vector3(0.6f, 0f, -2.2f), new Vector3(0.2f, 0f, 0.2f) };
            for (int i = 0; i < steps.Length; i++)
            {
                float h = i + 1f;
                Vector3 size = i == steps.Length - 1 ? new Vector3(3.4f, h, 3.4f) : new Vector3(2.2f, h, 2.2f);
                K.Block("Fels " + (i + 1), rock, steps[i], size, _stone, i * 17f);
            }
            var areaGo = new GameObject("Aussichtspunkt (Auftragsziel)");
            areaGo.transform.SetParent(rock, false);
            areaGo.transform.localPosition = steps[steps.Length - 1] + Vector3.up * steps.Length;
            areaGo.AddComponent<Ashveil.Gameplay.Quests.QuestArea>().Configure(QuestContentBuilder.LookoutArea, 2.2f, 0.6f);
            K.Label(rock, steps[steps.Length - 1] + Vector3.up * (steps.Length + 2.6f), "Aussichtsfels", "", WorldLabelStyle.Place, 45f, 0.8f);
        }

        private static void Stall(Transform parent, string name, Vector3 pos, float facingFromCenter, Material cloth)
        {
            Transform stall = K.Group(name, parent, pos, new Vector3(0f, facingFromCenter + 180f, 0f));
            K.Block("Counter", stall, new Vector3(0f, 0f, 0f), new Vector3(2.6f, 1.0f, 0.8f), _wood);
            for (int i = 0; i < 4; i++)
            {
                float x = i < 2 ? -1.3f : 1.3f;
                float z = i % 2 == 0 ? -0.4f : 1.2f;
                K.Block("Post", stall, new Vector3(x, 0f, z), new Vector3(0.12f, 2.4f, 0.12f), _wood);
            }
            K.Box("Roof", stall, new Vector3(0f, 2.45f, 0.4f), new Vector3(3f, 0.06f, 2.2f), cloth, new Vector3(-8f, 0f, 0f));
        }

        private static void BuildSignpost(Transform parent, Vector3 pos)
        {
            Transform sign = K.Group("Wegweiser", parent, pos, Vector3.zero);
            K.Prim(PrimitiveType.Cylinder, "Post", sign, new Vector3(0f, 1.6f, 0f), new Vector3(0.18f, 1.6f, 0.18f), Vector3.zero, _wood);

            var targets = new List<(string, Vector3)>
            {
                ("Granitplateau / Kloster", new Vector3(KarethTerrainBuilder.RampX, 0f, 430f)),
                ("Glockenmoor", new Vector3(60f, 0f, 300f)),
                ("Aschenfeld", new Vector3(300f, 0f, -40f)),
                ("Holzfaellerlager", KarethTerrainBuilder.LumberCamp),
                ("Glockenrast", KarethQuestSites.GlockenrastCenter),
                ("Ostwacht", KarethQuestSites.OstwachtCenter),
                ("Salzgruben", new Vector3(-290f, 0f, -195f)),
                ("Testparcours", new Vector3(-60f, 0f, -50f))
            };

            for (int i = 0; i < targets.Count; i++)
            {
                (string label, Vector3 target) = targets[i];
                Vector3 dir = target - pos;
                dir.y = 0f;
                float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
                float y = 2.9f - i * 0.33f;
                Transform arm = K.Group("Arm " + label, sign, new Vector3(0f, y, 0f), new Vector3(0f, yaw, 0f));
                K.Box("Board", arm, new Vector3(0f, 0f, 0.85f), new Vector3(0.06f, 0.28f, 1.5f), _woodDark, default, false);
                K.Label(arm, new Vector3(0f, 0.25f, 1.6f), label, "", WorldLabelStyle.Place, 22f, 0.7f);
            }
        }

        // =================================================================== Testparcours

        private static void BuildTestCourse(Transform env)
        {
            var origin = new Vector3(-60f, 0f, -50f);
            Transform tc = K.Group("Bewegungs-Testparcours", env, origin, Vector3.zero);
            K.Label(tc, new Vector3(0f, 6f, 8f), "Bewegungs-Testparcours", "", WorldLabelStyle.Place, 90f, 1.1f);

            // Treppen: Stufenhoehe 0.2 / 0.35 / 0.5 (StepOffset = 0.4)
            float[] rises = { 0.2f, 0.35f, 0.5f };
            string[] riseTitles = { "Stufen 0,20 m", "Stufen 0,35 m", "Stufen 0,50 m" };
            string[] riseSubs = { "begehbar", "begehbar", "nur mit Sprung" };
            for (int s = 0; s < rises.Length; s++)
            {
                Transform stairs = K.Group("Stairs " + rises[s], tc, new Vector3(s * 6f, 0f, 0f), Vector3.zero);
                for (int i = 0; i < 8; i++)
                {
                    float h = rises[s] * (i + 1);
                    K.Block("Step", stairs, new Vector3(0f, 0f, -i * 0.6f), new Vector3(4f, h, 0.6f), i % 2 == 0 ? _testGrey : _testAccent);
                }
                K.Block("Top", stairs, new Vector3(0f, 0f, -4.5f - 1.5f), new Vector3(4f, rises[s] * 8f, 3f), _testGrey);
                K.Label(stairs, new Vector3(0f, 2.2f, 1.5f), riseTitles[s], riseSubs[s], WorldLabelStyle.Info, 35f);
            }

            // Rampen: 20 / 35 / 45 / 55 Grad (SlopeLimit = 50)
            float[] angles = { 20f, 35f, 45f, 55f };
            for (int i = 0; i < angles.Length; i++)
            {
                var low = new Vector3(-10f - i * 7f, 0f, 0f);
                Vector3 top = K.Ramp("Ramp " + angles[i], tc, low, 180f, angles[i], 12f, 5f, _testGrey);
                Vector3 localTop = tc.InverseTransformPoint(top);
                K.Block("Ramp Platform", tc, new Vector3(localTop.x, 0f, localTop.z - 2f), new Vector3(5f, localTop.y, 4f), _testAccent);
                string sub = angles[i] <= 50f ? "begehbar" : "zu steil – man rutscht";
                K.Label(tc, low + new Vector3(0f, 2.2f, 1.5f), $"Hang {angles[i]:0}°", sub, WorldLabelStyle.Info, 35f);
            }

            // Sprungluecken auf 1 m hohen Podesten
            Transform gaps = K.Group("Jump Gaps", tc, new Vector3(22f, 0f, 0f), Vector3.zero);
            float[] gapSizes = { 2f, 3f, 4f, 5f, 6.5f };
            float z = 0f;
            K.Block("Start", gaps, new Vector3(0f, 0f, z - 1.5f), new Vector3(4f, 1f, 3f), _testAccent);
            z -= 3f;
            foreach (float gap in gapSizes)
            {
                z -= gap;
                K.Label(gaps, new Vector3(2.8f, 2f, z + gap * 0.5f), $"Luecke {gap:0.#} m", "", WorldLabelStyle.Info, 30f);
                K.Block("Platform", gaps, new Vector3(0f, 0f, z - 1.5f), new Vector3(4f, 1f, 3f), _testGrey);
                z -= 3f;
            }
            K.Label(gaps, new Vector3(0f, 3f, 1.5f), "Sprungluecken", "ab 5 m mit Sprint", WorldLabelStyle.Info, 35f);

            // Mauern
            Transform walls = K.Group("Walls", tc, new Vector3(32f, 0f, 0f), Vector3.zero);
            float[] wallHeights = { 1f, 1.5f, 2.2f };
            for (int i = 0; i < wallHeights.Length; i++)
            {
                K.Block("Wall " + wallHeights[i], walls, new Vector3(0f, 0f, -3f - i * 6f), new Vector3(4f, wallHeights[i], 0.5f), _testGrey);
                K.Label(walls, new Vector3(2.8f, wallHeights[i] + 0.8f, -3f - i * 6f), $"Mauer {wallHeights[i]:0.0} m", "", WorldLabelStyle.Info, 30f);
            }

            // Tunnel (Kamerakollision)
            Transform tunnel = K.Group("Tunnel", tc, new Vector3(42f, 0f, -4f), Vector3.zero);
            K.Block("Wall L", tunnel, new Vector3(-1.75f, 0f, -7f), new Vector3(0.5f, 3f, 14f), _stoneDark);
            K.Block("Wall R", tunnel, new Vector3(1.75f, 0f, -7f), new Vector3(0.5f, 3f, 14f), _stoneDark);
            K.Block("Roof", tunnel, new Vector3(0f, 3f, -7f), new Vector3(4f, 0.5f, 14f), _stoneDark);
            K.Label(tunnel, new Vector3(0f, 4.4f, 0.5f), "Tunnel", "Kamerakollision", WorldLabelStyle.Info, 35f);

            // Saeulenwald (Kamera zwischen Hindernissen)
            Transform pillars = K.Group("Pillar Forest", tc, new Vector3(10f, 0f, -38f), Vector3.zero);
            var rng = new System.Random(3);
            for (int i = 0; i < 14; i++)
            {
                var p = new Vector3((float)rng.NextDouble() * 18f - 9f, 4f, (float)rng.NextDouble() * 14f - 7f);
                K.Prim(PrimitiveType.Cylinder, "Pillar", pillars, p, new Vector3(1.2f, 4f, 1.2f), Vector3.zero, _stone);
            }
            K.Label(pillars, new Vector3(0f, 9.5f, 0f), "Saeulenwald", "Kamera zwischen Hindernissen", WorldLabelStyle.Info, 45f);

            // Alte Bruecke zwischen zwei 5-m-Tuermen (auch Test fuer harte Landung)
            Transform bridge = K.Group("Bridge", tc, new Vector3(0f, 0f, -62f), Vector3.zero);
            K.Block("Tower A", bridge, new Vector3(-8f, 0f, 0f), new Vector3(6f, 5f, 6f), _stone);
            K.Block("Tower B", bridge, new Vector3(16f, 0f, 0f), new Vector3(6f, 5f, 6f), _stone);
            K.Box("Bridge Deck", bridge, new Vector3(4f, 4.85f, 0f), new Vector3(18f, 0.3f, 1.6f), _wood);
            float rampLen = 5f / Mathf.Sin(20f * Mathf.Deg2Rad);
            float rampRun = 5f / Mathf.Tan(20f * Mathf.Deg2Rad);
            K.Ramp("Bridge Ramp", bridge, new Vector3(-11f - rampRun, 0f, 0f), 90f, 20f, rampLen, 3f, _testGrey);
            K.Label(bridge, new Vector3(4f, 7.5f, 0f), "Bruecke 1,6 m breit", "Sprung von 5 m = harte Landung", WorldLabelStyle.Info, 45f);
        }

        // =================================================================== Landmarken

        private static void BuildLandmarks(Transform env)
        {
            Transform lm = K.Group("Landmarks", env);

            // Der versteinerte Beschuetzer ersetzt den kleinen Gefallenen Waechter (Konflikt, siehe Nachtrag 26.09.)
            if (GiantBuilder.Active) GiantBuilder.Build(lm);
            else BuildFallenGuardian(lm);
            BuildAshWillow(lm);
            BuildPlateauAndMonastery(lm);
            BuildClockwork(lm);
            BuildCastle(lm);
            BuildSaltMines(lm);
            BuildAshField(lm);
        }

        /// <summary>60 m lange, gestuerzte Waechterstatue direkt westlich des Lagers.</summary>
        private static void BuildFallenGuardian(Transform parent)
        {
            Transform g = K.Group("Der Gefallene Waechter", parent, new Vector3(-72f, 0f, 22f), new Vector3(0f, 12f, 0f));
            K.Box("Torso", g, new Vector3(0f, 4f, 0f), new Vector3(14f, 9f, 26f), _stone, new Vector3(0f, 0f, 8f));
            K.Box("Pelvis", g, new Vector3(0f, 3f, -16f), new Vector3(12f, 7f, 8f), _stone);
            K.Box("Leg L", g, new Vector3(3.5f, 2.3f, -30f), new Vector3(5f, 5f, 22f), _stone, new Vector3(0f, 4f, 0f));
            K.Box("Leg R", g, new Vector3(-3.5f, 2.3f, -31f), new Vector3(5f, 5f, 22f), _stone, new Vector3(0f, -6f, 3f));
            K.Box("Arm Ground", g, new Vector3(10f, 2.3f, 8f), new Vector3(5f, 5f, 24f), _stone, new Vector3(0f, 18f, 0f));
            K.Box("Arm Raised", g, new Vector3(-9f, 9f, 9f), new Vector3(5f, 5f, 20f), _stone, new Vector3(-48f, 0f, -12f));
            K.Box("Hand Raised", g, new Vector3(-9.8f, 17f, 15.5f), new Vector3(6f, 4f, 6f), _stone, new Vector3(-48f, 0f, -12f));
            Transform head = K.Group("Head (bewegt sich spaeter)", g, new Vector3(0f, 6.5f, 19f), new Vector3(-10f, 0f, 0f));
            K.Prim(PrimitiveType.Sphere, "Skull", head, new Vector3(0f, 0f, 3f), new Vector3(10f, 10f, 11f), Vector3.zero, _stone);
            K.Box("Helm Crest", head, new Vector3(0f, 5f, 2f), new Vector3(1.5f, 3f, 9f), _stoneDark);
            K.Box("Visor", head, new Vector3(0f, 0.5f, 8.2f), new Vector3(6f, 1.2f, 0.8f), _stoneDark, default, false);
            // Moos auf dem Koerper
            K.Box("Moss A", g, new Vector3(2f, 8.7f, -4f), new Vector3(8f, 0.3f, 10f), _moss, new Vector3(0f, 0f, 8f), false);
            K.Box("Moss B", g, new Vector3(-2f, 7f, -16f), new Vector3(9f, 0.3f, 6f), _moss, default, false);
            K.Label(g, new Vector3(14f, 2.5f, 20f), "Der Gefallene Waechter", "", WorldLabelStyle.Place, 90f);
        }

        private static Vector3 G(float x, float z, float lift = 0f) => KarethTerrainBuilder.OnGround(x, z, lift);

        /// <summary>Riesiger abgestorbener Baum im Glockenmoor, versunkene Kuppeln, violette Irrlichtpflanzen.</summary>
        private static void BuildAshWillow(Transform parent)
        {
            Transform t = K.Group("Aschenweide (Glockenmoor)", parent, G(95f, 365f, -0.5f), Vector3.zero);
            K.Prim(PrimitiveType.Cylinder, "Trunk", t, new Vector3(0f, 20f, 0f), new Vector3(6f, 20f, 6f), new Vector3(0f, 0f, 2f), _bark);
            var rng = new System.Random(5);
            for (int i = 0; i < 7; i++)
            {
                float yaw = i * 51f + (float)rng.NextDouble() * 20f;
                float h = 26f + (float)rng.NextDouble() * 12f;
                Transform branch = K.Group("Branch", t, new Vector3(0f, h, 0f), new Vector3(0f, yaw, 0f));
                float tilt = 45f + (float)rng.NextDouble() * 25f;
                K.Prim(PrimitiveType.Cylinder, "Limb", branch, new Vector3(0f, 0f, 0f), new Vector3(1.4f, 9f, 1.4f),
                    new Vector3(tilt, 0f, 0f), _bark, false);
            }
            for (int i = 0; i < 5; i++)
            {
                Transform root = K.Group("Root", t, Vector3.zero, new Vector3(0f, i * 72f, 0f));
                K.Prim(PrimitiveType.Cylinder, "Root", root, new Vector3(0f, 1f, 4.5f), new Vector3(1.6f, 4f, 1.6f), new Vector3(70f, 0f, 0f), _bark);
            }
            // Aufzugsschacht unter dem Baum (spaeter erreichbar)
            K.Block("Shaft Rim", t, new Vector3(6f, 0f, -6f), new Vector3(3.5f, 0.8f, 3.5f), _metal);
            K.Box("Shaft Dark", t, new Vector3(6f, 0.81f, -6f), new Vector3(2.8f, 0.02f, 2.8f), _charred, default, false);

            // Versunkene Kuppeln der alten Glockenstadt
            Transform domes = K.Group("Versunkene Kuppeln", parent, Vector3.zero, Vector3.zero);
            K.Prim(PrimitiveType.Sphere, "Dome", domes, G(5f, 290f, -8f), new Vector3(24f, 24f, 24f), Vector3.zero, _stone);
            K.Prim(PrimitiveType.Sphere, "Dome", domes, G(115f, 290f, -10f), new Vector3(28f, 28f, 28f), Vector3.zero, _stone);
            K.Prim(PrimitiveType.Sphere, "Dome", domes, G(30f, 420f, -8f), new Vector3(22f, 22f, 22f), Vector3.zero, _stone);
            K.Prim(PrimitiveType.Cylinder, "Sunken Bell Tower", domes, G(115f, 290f, 5f), new Vector3(3f, 3f, 3f), new Vector3(0f, 0f, 12f), _stoneDark);

            Transform plants = K.Group("Irrlichtpflanzen", parent, Vector3.zero, Vector3.zero);
            for (int i = 0; i < 30; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = (float)rng.NextDouble() * 120f;
                Vector3 p = G(40f + Mathf.Cos(a) * r, 330f + Mathf.Sin(a) * r);
                p.y = Mathf.Max(p.y, KarethTerrainBuilder.MoorWaterLevel) + 0.3f;
                K.Prim(PrimitiveType.Sphere, "Glow Bulb", plants, p, Vector3.one * (0.3f + (float)rng.NextDouble() * 0.35f), Vector3.zero, _violetGlow, false);
                if (i % 5 == 0) K.PointLight("Violet Light", plants, p + Vector3.up * 0.6f, new Color(0.65f, 0.3f, 1f), 9f, 1.8f);
            }
            K.Particles("Moor Wisps", plants, new Vector3(40f, 0f, 330f), new Color(0.75f, 0.5f, 1f, 0.35f), 0.35f, 6f, 0.3f, 8f, 80f, 110f);
            K.Label(t, new Vector3(0f, 3f, -8f), "Aschenweide", "Glockenmoor", WorldLabelStyle.Place, 120f);
        }

        /// <summary>Granitplateau (Terrain, +32 m) mit dem Kloster der Stillen Glocke (Ort, keine Instanz – Kanon: nur Salzgruben + Kolosseum).</summary>
        private static void BuildPlateauAndMonastery(Transform parent)
        {
            Transform p = K.Group("Granitplateau", parent, Vector3.zero, Vector3.zero);
            K.Label(p, G(KarethTerrainBuilder.RampX + 8f, 432f, 3f), "Granitplateau", "Kloster der Stillen Glocke", WorldLabelStyle.Place, 120f);
            // Aussichtspunkt an der Plateaukante: von hier sieht man die ganze Senke
            Transform view = K.Group("Aussichtspunkt", p, G(60f, 598f), Vector3.zero);
            K.Block("Fels", view, new Vector3(0f, -0.6f, 0f), new Vector3(5f, 1.4f, 4f), _granite, 12f);
            K.Label(view, new Vector3(0f, 2.5f, 0f), "Aussichtspunkt", "Blick ueber die Senke", WorldLabelStyle.Place, 60f);

            Transform m = K.Group("Kloster der Stillen Glocke", p, G(0f, 700f, -0.5f), Vector3.zero);
            K.Block("Bell Tower", m, new Vector3(18f, 0f, -4f), new Vector3(8f, 45f, 8f), _stone);
            K.Block("Tower Cap", m, new Vector3(18f, 45f, -4f), new Vector3(10f, 1f, 10f), _stoneDark);
            K.Prim(PrimitiveType.Cylinder, "Spire", m, new Vector3(18f, 50f, -4f), new Vector3(1.2f, 4f, 1.2f), Vector3.zero, _stoneDark);
            K.Box("Bell Opening", m, new Vector3(18f, 39f, -8.05f), new Vector3(3f, 4f, 0.1f), _warmWindow, default, false);
            K.PointLight("Bell Light", m, new Vector3(18f, 39f, -10f), new Color(1f, 0.7f, 0.35f), 22f, 3f, true);
            K.Block("Hall", m, new Vector3(-15f, 0f, 5f), new Vector3(30f, 14f, 18f), _stone);
            K.Box("Hall Roof", m, new Vector3(-15f, 14f, 5f), new Vector3(30f, 12.7f, 12.7f), _stoneDark, new Vector3(45f, 0f, 0f));
            for (int i = 0; i < 5; i++)
                K.Box("Window", m, new Vector3(-27f + i * 6f, 7f, -4.05f), new Vector3(1.4f, 3f, 0.1f), _warmWindow, default, false);
            K.Block("Wall", m, new Vector3(-5f, 0f, -18f), new Vector3(60f, 4f, 1.5f), _stone);
            K.Block("Portal", m, new Vector3(-15f, 0f, -4.3f), new Vector3(4f, 5f, 0.6f), _charred);
            K.Label(m, new Vector3(-15f, 6f, -6f), "Kloster der Stillen Glocke", "Stufe 13-17 · Tore verschlossen", WorldLabelStyle.Place, 70f);
        }

        /// <summary>Riesiges Zahnrad in der Ostwand – das Uhrwerk (Ort, keine Instanz), Tor versiegelt.</summary>
        private static void BuildClockwork(Transform parent)
        {
            Transform c = K.Group("Das Uhrwerk", parent, Vector3.zero, Vector3.zero);
            // Felsvorsprung, in dem das Rad steckt (Terrain bildet die Wand)
            K.Box("Fels um das Rad", c, new Vector3(548f, 42f, 120f), new Vector3(30f, 84f, 80f), _granite);

            Transform gear = K.Group("Gear", c, new Vector3(530f, 46f, 120f), new Vector3(0f, 0f, 90f));
            K.Prim(PrimitiveType.Cylinder, "Disc", gear, Vector3.zero, new Vector3(56f, 1.5f, 56f), Vector3.zero, _bronze, false);
            K.Prim(PrimitiveType.Cylinder, "Hub", gear, Vector3.zero, new Vector3(10f, 2.5f, 10f), Vector3.zero, _metal, false);
            for (int i = 0; i < 20; i++)
            {
                float a = i * 18f;
                Vector3 pos = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 29f);
                K.Box("Tooth", gear, pos, new Vector3(4f, 3f, 5f), _bronze, new Vector3(0f, a, 0f), false);
            }
            for (int i = 0; i < 6; i++)
                K.Box("Spoke", gear, Vector3.zero, new Vector3(2f, 2f, 50f), _metal, new Vector3(0f, i * 30f, 0f), false);
            var rot = gear.gameObject.AddComponent<SlowRotator>();
            K.SetVector3(rot, "localAxis", Vector3.up);
            K.SetFloat(rot, "degreesPerSecond", 1.5f);
            K.SetFloat(rot, "tickInterval", 2f);
            K.MakeDynamic(gear.gameObject);

            Transform gate = K.Group("Versiegeltes Tor", c, G(522f, 210f, -0.5f), new Vector3(0f, -90f, 0f));
            K.Block("Door L", gate, new Vector3(-5.5f, 0f, 0f), new Vector3(11f, 26f, 2f), _metal);
            K.Block("Door R", gate, new Vector3(5.5f, 0f, 0f), new Vector3(11f, 26f, 2f), _metal);
            K.Box("Seal Line", gate, new Vector3(0f, 13f, -1.05f), new Vector3(0.3f, 24f, 0.1f), _coldGlow, default, false);
            K.Block("Arch", gate, new Vector3(0f, 26f, 0f), new Vector3(26f, 4f, 3f), _stoneDark);
            K.Block("Rock Behind", gate, new Vector3(0f, 0f, 8f), new Vector3(34f, 34f, 14f), _granite);
            K.PointLight("Seal Light", gate, new Vector3(0f, 12f, -4f), new Color(0.4f, 0.7f, 1f), 18f, 2f);
            K.Label(gate, new Vector3(0f, 3f, -6f), "Das Uhrwerk", "Versiegelt", WorldLabelStyle.Place, 90f);
        }

        /// <summary>Burg Veyrholt hinter den Nordbergen – bewusst unerreichbar (naechste Zone).</summary>
        private static void BuildCastle(Transform parent)
        {
            Transform c = K.Group("Burg Veyrholt (Horizont)", parent, new Vector3(-380f, 60f, 1350f), Vector3.zero);
            K.Prim(PrimitiveType.Sphere, "Hill", c, new Vector3(0f, -40f, 0f), new Vector3(420f, 150f, 420f), Vector3.zero, _granite, false);
            K.Block("Keep", c, new Vector3(0f, 28f, 0f), new Vector3(40f, 30f, 30f), _stoneDark);
            Vector3[] towers = { new Vector3(-22f, 26f, -17f), new Vector3(22f, 26f, -17f), new Vector3(-22f, 26f, 17f), new Vector3(22f, 26f, 17f) };
            foreach (Vector3 t in towers)
                K.Prim(PrimitiveType.Cylinder, "Tower", c, t + Vector3.up * 20f, new Vector3(8f, 20f, 8f), Vector3.zero, _stoneDark, false);
            K.Prim(PrimitiveType.Cylinder, "Main Tower", c, new Vector3(6f, 58f, 4f), new Vector3(10f, 30f, 10f), Vector3.zero, _stoneDark, false);
            for (int i = 0; i < 6; i++)
                K.Box("Window", c, new Vector3(-12f + i * 5f, 45f + (i % 2) * 4f, -15.1f), new Vector3(1.5f, 2.5f, 0.2f), _warmWindow, default, false);
        }

        /// <summary>Eingang der Salzgruben (Dungeon 1) an der Steilkueste im Suedwesten, darunter ein Wrack.</summary>
        private static void BuildSaltMines(Transform parent)
        {
            Transform s = K.Group("Salzgruben (Dungeon 1)", parent, G(-290f, -195f, -0.3f), new Vector3(0f, 45f, 0f));
            K.Block("Cliff", s, new Vector3(0f, -2f, -14f), new Vector3(60f, 22f, 16f), _granite);
            K.Block("Portal Pillar L", s, new Vector3(-4f, 0f, -5.5f), new Vector3(1.5f, 7f, 1.5f), _wood);
            K.Block("Portal Pillar R", s, new Vector3(4f, 0f, -5.5f), new Vector3(1.5f, 7f, 1.5f), _wood);
            K.Block("Portal Beam", s, new Vector3(0f, 7f, -5.5f), new Vector3(10f, 1.2f, 1.5f), _wood);
            K.Box("Darkness", s, new Vector3(0f, 3.5f, -6.1f), new Vector3(6.5f, 7f, 0.2f), _charred, default, false);
            K.PointLight("Mine Lamp", s, new Vector3(3f, 5.5f, -4f), new Color(1f, 0.75f, 0.45f), 10f, 2f, true);
            for (int i = 0; i < 4; i++)
            {
                float x = i < 2 ? 10f : 16f;
                float z = i % 2 == 0 ? 0f : 6f;
                K.Block("Headframe Post", s, new Vector3(x, 0f, z), new Vector3(0.6f, 14f, 0.6f), _wood);
            }
            K.Block("Headframe Top", s, new Vector3(13f, 14f, 3f), new Vector3(7f, 1f, 7f), _woodDark);
            K.Prim(PrimitiveType.Cylinder, "Wheel", s, new Vector3(13f, 16.5f, 3f), new Vector3(4f, 0.25f, 4f), new Vector3(0f, 0f, 90f), _metal, false);
            for (int i = 0; i < 4; i++)
                K.Prim(PrimitiveType.Sphere, "Salt Heap", s, new Vector3(-12f + i * 4f, 0f, 5f + (i % 2) * 3f), new Vector3(3.5f, 2.2f, 3.5f), Vector3.zero, _salt);
            K.Label(s, new Vector3(0f, 9.5f, -3f), "Salzgruben", "Dungeon 1 · Stufe 10–14 · 1–5 Spieler", WorldLabelStyle.Place, 90f);
            // M6: Instanz-Plaetze, Eingang und Dungeon-Direktor (Docs/Design/ashveil-salzgruben.md)
            SaltMineBuilder.Build(parent, s);

            // M6.5: Lager der letzten Grubenleute neben dem Stollenmund (Auftraege, Grubenbedarf)
            Vector3 Local(float lx, float lz)
            {
                Vector3 wp = s.TransformPoint(new Vector3(lx, 0f, lz));
                return G(wp.x, wp.z);
            }
            Transform camp = K.Group("Lager der Grubenleute", parent, Local(-20f, 12f), new Vector3(0f, 45f, 0f));
            K.Block("Feuerstelle", camp, Vector3.zero, new Vector3(1.4f, 0.35f, 1.4f), _stoneDark);
            K.PointLight("Lagerfeuer", camp, new Vector3(0f, 0.9f, 0f), new Color(1f, 0.55f, 0.25f), 9f, 2.2f, true);
            Transform tent = K.Group("Zelt", camp, new Vector3(-5f, 0f, 4f), new Vector3(0f, 20f, 0f));
            if (ArtModels.Place("SM_Tent", tent, Vector3.zero, 0f, 1f, ArtModels.ColliderKind.Mesh) == null)
            {
                K.Box("Side L", tent, new Vector3(-0.95f, 1.1f, 0f), new Vector3(0.08f, 2.9f, 3.6f), _canvas, new Vector3(0f, 0f, -40f));
                K.Box("Side R", tent, new Vector3(0.95f, 1.1f, 0f), new Vector3(0.08f, 2.9f, 3.6f), _canvas, new Vector3(0f, 0f, 40f));
            }
            for (int i = 0; i < 3; i++)
                K.Prim(PrimitiveType.Cylinder, "Salzfass", camp, new Vector3(3.5f + i * 1.1f, 0.6f, 3f), new Vector3(0.9f, 0.6f, 0.9f), Vector3.zero, _woodDark);
            float face = s.eulerAngles.y; // Blick nach vorn, zu den Ankommenden
            Npc(parent, "Grubenmeisterin Vela Salk", "Aufträge", Local(-17f, 10f), _npcQuest, QuestContentBuilder.Vela, face);
            Npc(parent, "Bergmann Tobbe", "Grubenbedarf", Local(-23f, 10f), _npc, QuestContentBuilder.Tobbe, face);

            // Wrack in der Brandung unter der Kueste
            Transform w = K.Group("Wrack", parent, new Vector3(-410f, KarethTerrainBuilder.SeaLevel - 1f, -270f), new Vector3(8f, 30f, 14f));
            K.Box("Rumpf", w, new Vector3(0f, 2f, 0f), new Vector3(7f, 4f, 26f), _woodDark);
            K.Box("Mast", w, new Vector3(0f, 10f, 3f), new Vector3(0.6f, 16f, 0.6f), _wood, new Vector3(0f, 0f, 18f), false);
            K.Label(w, new Vector3(0f, 7f, 0f), "Wrack", "", WorldLabelStyle.Place, 120f);
        }

        /// <summary>Verbranntes Aschenfeld im Osten mit Rauchsaeule (Route des Glockenhirten, siehe EnemyBuilder).</summary>
        private static void BuildAshField(Transform parent)
        {
            Transform a = K.Group("Aschenfeld", parent, Vector3.zero, Vector3.zero);
            var rng = new System.Random(9);
            for (int i = 0; i < 90; i++)
            {
                float x = 225f + (float)rng.NextDouble() * 270f, z = -220f + (float)rng.NextDouble() * 510f;
                float h = 1f + (float)rng.NextDouble() * 4f;
                Vector3 p = G(x, z);
                K.Prim(PrimitiveType.Cylinder, "Burnt Stump", a, p + Vector3.up * (h * 0.5f - 0.2f), new Vector3(0.9f, h * 0.5f, 0.9f),
                    new Vector3((float)rng.NextDouble() * 10f, 0f, (float)rng.NextDouble() * 10f), _charred);
            }
            for (int i = 0; i < 40; i++)
            {
                float x = 230f + (float)rng.NextDouble() * 260f, z = -210f + (float)rng.NextDouble() * 490f;
                K.Box("Ember Crack", a, G(x, z, 0.03f), new Vector3(0.35f, 0.06f, 4f + (float)rng.NextDouble() * 8f), _emberGlow,
                    new Vector3(0f, (float)rng.NextDouble() * 180f, 0f), false);
            }
            Vector3 crater = G(420f, -150f);
            K.Particles("Smoke Column", a, crater, new Color(0.28f, 0.27f, 0.27f, 0.55f), 22f, 5f, 6f, 22f, 6f, 8f);
            K.PointLight("Crater Glow", a, crater + Vector3.up * 3f, new Color(1f, 0.4f, 0.15f), 30f, 3f, true);
            K.Label(a, G(300f, -40f, 3f), "Aschenfeld", "Hier wandert der Glockenhirte", WorldLabelStyle.Place, 120f);
        }

        // =================================================================== Spieler & Kamera

        private static GameObject BuildPlayerPrefab()
        {
            var root = new GameObject("Player_Placeholder");
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.4f;
            cc.skinWidth = 0.05f;
            cc.minMoveDistance = 0f;

            var motor = root.AddComponent<CharacterMotor>();
            root.AddComponent<PlayerInputReader>();
            root.AddComponent<PlayerCharacter>();

            Transform visual = K.Group("Visual", root.transform);
            Transform placeholder = K.Group("Placeholder", visual);
            K.Prim(PrimitiveType.Capsule, "Body", placeholder, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), Vector3.zero, _playerBody, false);
            K.Box("Visor (Blickrichtung)", placeholder, new Vector3(0f, 1.52f, 0.3f), new Vector3(0.42f, 0.12f, 0.12f), _playerAccent, default, false);
            K.Box("Backpack", placeholder, new Vector3(0f, 1.1f, -0.36f), new Vector3(0.45f, 0.55f, 0.2f), _woodDark, default, false);
            K.Box("Belt", placeholder, new Vector3(0f, 0.95f, 0f), new Vector3(0.74f, 0.1f, 0.74f), _woodDark, default, false);

            var body = root.AddComponent<PlaceholderBodyMotion>();
            K.SetRef(body, "motor", motor);
            K.SetRef(body, "visual", visual);

            var bridge = root.AddComponent<CharacterAnimatorBridge>();
            K.SetRef(bridge, "motor", motor);

            // Milestone 2: Leben, Zielwahl, Waffen, Waffenmodelle, animierte Koerper (falls importiert)
            PlayerRigBuilder.Equip(root, visual, placeholder, _metal, _wood, _fire);

            // Milestone 3: Netzwerk (NetworkObject, Positionsabgleich, Namensschild)
            NetworkSetupBuilder.MakeNetworkPlayer(root);

            K.MakeDynamic(root);
            K.SetLayerRecursively(root, LayerMask.NameToLayer(GameLayers.Player));

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
            NetworkSetupBuilder.FinalizePlayerPrefab(prefab);
            return prefab;
        }

        /// <summary>Kamera ohne Ziel – der eigene Spieler meldet sich nach dem Erscheinen selbst an.</summary>
        private static void BuildCamera()
        {
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
            camGo.transform.SetPositionAndRotation(SpawnPosition + new Vector3(0f, 3.4f, -6.8f), Quaternion.Euler(14f, 0f, 0f));
        }

        private static void AddSceneToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        [MenuItem("Ashveil/Sandbox-Szene oeffnen", priority = 2)]
        public static void Open()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                EditorUtility.DisplayDialog("Ashveil", "Die Sandbox existiert noch nicht. Erst 'Sandbox-Szene erstellen' ausfuehren.", "OK");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
