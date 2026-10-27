using System.IO;
using Ashveil.Core;
using Ashveil.Gameplay.CameraSystem;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Motion.Lab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Baut die Motion-Lab-Szene (§106): Schachbrett-Boden (Fussgleiten sichtbar), Bahnen fuer Laufen, Haenge 10/20/30°,
    /// feine und grobe Treppen, kleine Hindernisse, Knie-/Vault-Kanten, Gang zwischen Waenden und Ziel-Dummys
    /// (klein bis Boss). Dazu eine Spielfigur ohne Netz (gleicher Aufbau wie der Spieler) und die Aufnahme-Kamera.
    /// </summary>
    public static class MotionLabBuilder
    {
        public const string ScenePath = ProjectSetup.Root + "/Scenes/MotionLab.unity";
        private const string CheckerPath = ProjectSetup.Root + "/Art/Materials/Greybox/MotionLab_Checker.png";

        private static Material _ground, _lane, _obstacle, _wall, _dummy, _metal, _wood, _fire, _mark;

        [MenuItem("Ashveil/Motion/Motion-Lab bauen", priority = 340)]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorUtility.DisplayDialog("Ashveil – Motion-Lab", "Motion-Lab gebaut: " + ScenePath + "\n\nAshveil › Motion › Motion-Lab: selbst laufen (F8 = Panel)\noder › Aufnahme (Kontaktboegen nach Logs/Ashveil/Motion).", "OK");
        }

        [MenuItem("Ashveil/Motion/Motion-Lab: selbst laufen", priority = 341)]
        public static void PlayManual() => Play(null);

        [MenuItem("Ashveil/Motion/Motion-Lab: Aufnahme Tank (Mann)", priority = 342)]
        public static void RecordMale() => Play("mann");

        [MenuItem("Ashveil/Motion/Motion-Lab: Aufnahme Tank (Frau)", priority = 343)]
        public static void RecordFemale() => Play("frau");

        [MenuItem("Ashveil/Motion/Motion-Lab: Aufnahme roh (Vergleich)", priority = 344)]
        public static void RecordRaw() => Play("mann-roh");

        [MenuItem("Ashveil/Motion/Motion-Lab: Aufnahme Waffen (Mann)", priority = 345)]
        public static void RecordWeaponsMale() => Play("waffen-mann");

        [MenuItem("Ashveil/Motion/Motion-Lab: Aufnahme Waffen (Frau)", priority = 346)]
        public static void RecordWeaponsFemale() => Play("waffen-frau");

        [MenuItem("Ashveil/Motion/Motion-Lab: Anatomie-Pruefung (Mann)", priority = 347)]
        public static void AnatomyMale() => Play("anatomie-mann");

        [MenuItem("Ashveil/Motion/Motion-Lab: Anatomie-Pruefung (Frau)", priority = 348)]
        public static void AnatomyFemale() => Play("anatomie-frau");

        private static void Play(string record)
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
            else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory("Temp");
            if (!string.IsNullOrEmpty(record)) File.WriteAllText(MotionLabDirector.FlagFile, record);
            else if (File.Exists(MotionLabDirector.FlagFile)) File.Delete(MotionLabDirector.FlagFile);
            EditorApplication.isPlaying = true;
        }

        public static void Build()
        {
            K.ResetCache();
            _ground = CheckerMaterial();
            _lane = K.Mat("Lab_Lane", new Color(0.36f, 0.37f, 0.4f));
            _obstacle = K.Mat("Lab_Obstacle", new Color(0.55f, 0.42f, 0.28f));
            _wall = K.Mat("Lab_Wall", new Color(0.5f, 0.52f, 0.56f));
            _dummy = K.Mat("Lab_Dummy", new Color(0.62f, 0.3f, 0.26f));
            _mark = K.Mat("Lab_Mark", new Color(0.95f, 0.8f, 0.2f));
            _metal = K.Mat("Metal", new Color(0.55f, 0.56f, 0.6f), 0.5f);
            _wood = K.Mat("Wood", new Color(0.42f, 0.29f, 0.18f));
            _fire = K.Mat("Glow", new Color(1f, 0.6f, 0.25f), 0.2f, new Color(1f, 0.5f, 0.15f) * 2f);

            ProjectSetup.EnsureFolder(ProjectSetup.Root + "/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Licht: neutral und hell – es geht um Bewegung, nicht um Stimmung
            var sunGo = new GameObject("Sonne");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.62f);
            RenderSettings.fog = false;

            Transform world = K.Group("Motion-Lab", null);
            K.Box("Boden (1-m-Schachbrett)", world, new Vector3(20f, -0.25f, 40f), new Vector3(160f, 0.5f, 200f), _ground);

            Transform lanes = K.Group("Bahnen", world);
            Transform[] anchors = new Transform[12];
            int n = 0;

            // Bahn 0: gerade Strecke (Gangarten, Anhalten, Drehen, Strafe, Sprung)
            anchors[n++] = Anchor(lanes, "Flat", new Vector3(0f, 0f, -30f));
            K.Strip("Bahn gerade", lanes, new Vector3(0f, 0f, -32f), new Vector3(0f, 0f, 118f), 3f, _lane, 0.004f);
            for (int z = -30; z <= 118; z += 10) K.Box($"Meter {z}", lanes, new Vector3(1.9f, 0.02f, z), new Vector3(0.6f, 0.04f, 0.08f), _mark, default, false);

            // Bahnen 1–3: Haenge 10°, 20°, 30° (hinauf, Plateau, hinab)
            float[] angles = { 10f, 20f, 30f };
            for (int i = 0; i < angles.Length; i++)
            {
                float x = 12f + i * 10f;
                Vector3 top = K.Ramp($"Hang {angles[i]}° hinauf", lanes, new Vector3(x, 0f, 0f), 0f, angles[i], 16f, 4f, _lane);
                K.Block($"Plateau {angles[i]}°", lanes, new Vector3(x, 0f, top.z + 3f), new Vector3(4f, top.y, 6f), _lane);
                K.Ramp($"Hang {angles[i]}° hinab", lanes, new Vector3(x, 0f, top.z + 6f + (top.z - 0f)), 180f, angles[i], 16f, 4f, _lane);
                anchors[n++] = Anchor(lanes, $"Slope{angles[i]:0}", new Vector3(x, 0f, -4f));
                if (Mathf.Approximately(angles[i], 20f)) anchors[n++] = Anchor(lanes, "Slope20Top", new Vector3(x, top.y, top.z + 3f));
            }

            // Bahn 4: feine Treppe (15/30 cm), Bahn 5: grobe Stufen (30/45 cm)
            anchors[n++] = Anchor(lanes, "Stairs", new Vector3(46f, 0f, -3f));
            Stairs(lanes, "Treppe fein", new Vector3(46f, 0f, 0f), 16, 0.15f, 0.3f);
            anchors[n++] = Anchor(lanes, "StairsCoarse", new Vector3(54f, 0f, -3f));
            Stairs(lanes, "Stufen grob", new Vector3(54f, 0f, 0f), 8, 0.3f, 0.45f);

            // Bahn 6: kleine Steine, Ast, Knie-Kante, Vault-Kante
            anchors[n++] = Anchor(lanes, "Obstacles", new Vector3(64f, 0f, -3f));
            Transform ob = K.Group("Hindernisse", lanes);
            float[] pebble = { 0.06f, 0.1f, 0.14f, 0.2f };
            for (int i = 0; i < pebble.Length; i++)
                K.Block($"Stein {pebble[i] * 100:0} cm", ob, new Vector3(64f + (i % 2 == 0 ? -0.4f : 0.4f), 0f, 3f + i * 2.5f), new Vector3(0.35f, pebble[i], 0.3f), _obstacle, i * 23f);
            K.Prim(PrimitiveType.Cylinder, "Ast (liegend, 12 cm)", ob, new Vector3(64f, 0.06f, 14f), new Vector3(0.12f, 1.4f, 0.12f), new Vector3(0f, 0f, 90f), _wood);
            K.Block("Kante Knie (45 cm)", ob, new Vector3(64f, 0f, 20f), new Vector3(2.5f, 0.45f, 0.4f), _obstacle);
            K.Block("Kante Vault (90 cm)", ob, new Vector3(64f, 0f, 27f), new Vector3(2.5f, 0.9f, 0.5f), _obstacle);
            K.Block("Stamm (60 cm)", ob, new Vector3(64f, 0f, 34f), new Vector3(3f, 0.6f, 0.6f), _wood);

            // Bahn 7: enger Gang
            anchors[n++] = Anchor(lanes, "Corridor", new Vector3(74f, 0f, -3f));
            K.Block("Wand links", lanes, new Vector3(73.1f, 0f, 12f), new Vector3(0.3f, 2.6f, 22f), _wall);
            K.Block("Wand rechts", lanes, new Vector3(74.9f, 0f, 12f), new Vector3(0.3f, 2.6f, 22f), _wall);

            // Ziel-Dummys (Groessen §29, Boss-Koerper mit Kopf in 8 m)
            Transform dummies = K.Group("Ziel-Dummys", world);
            Dummy(dummies, "Klein (0,6 m)", new Vector3(-14f, 0f, 0f), 0.6f, 0.35f);
            Dummy(dummies, "Mensch (1,8 m)", new Vector3(-14f, 0f, 6f), 1.8f, 0.4f);
            Dummy(dummies, "Gross (3,5 m)", new Vector3(-14f, 0f, 13f), 3.5f, 0.9f);
            Dummy(dummies, "Riesig (7 m)", new Vector3(-16f, 0f, 24f), 7f, 1.8f);
            Transform boss = K.Group("Boss-Koerper", dummies, new Vector3(-20f, 0f, 40f), Vector3.zero);
            K.Block("Bein links", boss, new Vector3(-1.6f, 0f, 0f), new Vector3(1.1f, 4f, 1.1f), _dummy);
            K.Block("Bein rechts", boss, new Vector3(1.6f, 0f, 0f), new Vector3(1.1f, 4f, 1.1f), _dummy);
            K.Block("Rumpf", boss, new Vector3(0f, 4f, 0f), new Vector3(4.5f, 3f, 2.5f), _dummy);
            K.Prim(PrimitiveType.Sphere, "Kopf (8 m)", boss, new Vector3(0f, 8f, 0.6f), new Vector3(1.8f, 1.8f, 1.8f), Vector3.zero, _dummy);

            // Spieldaten (Gegenstaende, Aussehen), damit die Figur echte Waffen/Schilde anlegen kann
            var dataGo = new GameObject("Spieldaten");
            var boot = dataGo.AddComponent<Ashveil.Data.GameDataBootstrap>();
            K.SetRef(boot, "database", AssetDatabase.LoadAssetAtPath<Ashveil.Data.DefinitionDatabase>(ProjectSetup.Root + "/Data/DefinitionDatabase.asset"));
            K.SetRef(boot, "defaultClass", AssetDatabase.LoadAssetAtPath<Ashveil.Data.CharacterClassDefinition>(ProjectSetup.Root + "/Data/Classes/Class_Eidwaechter.asset"));
            K.SetRef(boot, "unarmed", AssetDatabase.LoadAssetAtPath<Ashveil.Data.WeaponDefinition>(PlayerRigBuilder.WeaponFolder + "/Weapon_Fists.asset"));
            K.SetRef(boot, "appearanceCatalog", AssetDatabase.LoadAssetAtPath<Ashveil.Data.AppearanceCatalog>(AppearanceContentBuilder.CatalogPath));

            // Figur und Kameras
            GameObject puppet = BuildPuppet(new Vector3(0f, 0f, -30f));
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 800f;
            camGo.AddComponent<AudioListener>();
            ArtStyleSetup.SetupCamera(cam);
            var mmo = camGo.AddComponent<MMOCameraController>();
            K.SetInt(mmo, "collisionMask", GameLayers.CameraCollisionMask());
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 3.4f, -37f), Quaternion.Euler(14f, 0f, 0f));

            var capGo = new GameObject("Aufnahme-Kamera");
            Camera cap = capGo.AddComponent<Camera>();
            cap.enabled = false;
            cap.nearClipPlane = 0.1f; cap.farClipPlane = 300f;
            cap.clearFlags = CameraClearFlags.SolidColor;
            cap.backgroundColor = new Color(0.78f, 0.8f, 0.84f);
            ArtStyleSetup.SetupCamera(cap);

            var directorGo = new GameObject("Motion-Lab Regie");
            var director = directorGo.AddComponent<MotionLabDirector>();
            K.SetRef(director, "puppet", puppet.GetComponent<CharacterMotor>());
            K.SetRef(director, "player", puppet.GetComponent<PlayerCharacter>());
            K.SetRef(director, "captureCamera", cap);
            var so = new SerializedObject(director);
            SerializedProperty arr = so.FindProperty("lanes");
            arr.arraySize = n;
            for (int i = 0; i < n; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = anchors[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        private static Transform Anchor(Transform parent, string name, Vector3 pos) => K.Group(name, parent, pos, Vector3.zero);

        private static void Stairs(Transform parent, string name, Vector3 start, int steps, float rise, float run)
        {
            Transform g = K.Group(name, parent);
            for (int i = 0; i < steps; i++)
                K.Block($"Stufe {i + 1}", g, start + new Vector3(0f, 0f, i * run + run * 0.5f), new Vector3(3f, rise * (i + 1), run), _lane);
            float h = rise * steps, end = steps * run;
            K.Block("Podest", g, start + new Vector3(0f, 0f, end + 2f), new Vector3(3f, h, 4f), _lane);
            for (int i = 0; i < steps; i++)
                K.Block($"Stufe hinab {i + 1}", g, start + new Vector3(0f, 0f, end + 4f + i * run + run * 0.5f), new Vector3(3f, h - rise * (i + 1) + 0.001f, run), _lane);
        }

        private static void Dummy(Transform parent, string name, Vector3 pos, float height, float radius)
        {
            K.Prim(PrimitiveType.Capsule, name, parent, pos + Vector3.up * (height * 0.5f), new Vector3(radius * 2f, height * 0.5f, radius * 2f), Vector3.zero, _dummy);
        }

        /// <summary>Gleicher Aufbau wie der Spieler (Motor, Eingabe, Kamera-Anbindung, Waffen, Koerper) – nur ohne Netz.</summary>
        private static GameObject BuildPuppet(Vector3 pos)
        {
            var root = new GameObject("Lab-Figur");
            root.transform.position = pos;
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 50f; cc.stepOffset = 0.4f; cc.skinWidth = 0.05f; cc.minMoveDistance = 0f;
            var motor = root.AddComponent<CharacterMotor>();
            root.AddComponent<PlayerInputReader>();
            root.AddComponent<PlayerCharacter>();

            Transform visual = K.Group("Visual", root.transform);
            Transform placeholder = K.Group("Placeholder", visual);
            K.Prim(PrimitiveType.Capsule, "Body", placeholder, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), Vector3.zero, _dummy, false);
            var body = root.AddComponent<PlaceholderBodyMotion>();
            K.SetRef(body, "motor", motor);
            K.SetRef(body, "visual", visual);
            var bridge = root.AddComponent<CharacterAnimatorBridge>();
            K.SetRef(bridge, "motor", motor);
            PlayerRigBuilder.Equip(root, visual, placeholder, _metal, _wood, _fire);
            // Echte Ausruestung (Waffen-Parcours legt nacheinander Schwert+Schild, Zweihaender, Stab, Gewehr … an)
            root.AddComponent<Ashveil.Gameplay.Items.CharacterEquipment>();
            root.AddComponent<Ashveil.Gameplay.Visuals.EquipmentVisuals>();
            K.MakeDynamic(root);
            K.SetLayerRecursively(root, LayerMask.NameToLayer(GameLayers.Player));
            return root;
        }

        private static Material CheckerMaterial()
        {
            ProjectSetup.EnsureFolder(ProjectSetup.Root + "/Art/Materials/Greybox");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(CheckerPath);
            if (tex == null)
            {
                var t = new Texture2D(64, 64, TextureFormat.RGB24, false);
                Color a = new Color(0.62f, 0.63f, 0.65f), b = new Color(0.48f, 0.49f, 0.52f), line = new Color(0.35f, 0.36f, 0.38f);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        bool edge = x % 32 == 0 || y % 32 == 0;
                        t.SetPixel(x, y, edge ? line : ((x / 32 + y / 32) % 2 == 0 ? a : b));
                    }
                File.WriteAllBytes(CheckerPath, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(CheckerPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(CheckerPath);
                imp.filterMode = FilterMode.Bilinear;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.SaveAndReimport();
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(CheckerPath);
            }
            Material m = K.Mat("Lab_Checker", Color.white);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            // Boden 160 × 200 m, eine Kachel (2 × 2 Felder) = 2 m → 1-m-Felder
            m.mainTextureScale = new Vector2(80f, 100f);
            if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", new Vector2(80f, 100f));
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
