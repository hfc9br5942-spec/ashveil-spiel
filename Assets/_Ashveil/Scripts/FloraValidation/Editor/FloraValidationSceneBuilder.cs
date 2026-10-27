using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using L = Ashveil.FloraValidation.FloraValidationLayout;

namespace Ashveil.FloraValidation.EditorTools
{
    /// <summary>
    /// Baut die isolierte Testszene Assets/_Ashveil/Scenes/Validation/FloraValidation_FV1.unity (nicht in den Build-Einstellungen).
    /// Kein Level, kein Kareth, kein Weltgenerator: nur Testbereiche weit auseinander, jeder mit eigenem flachen Boden.
    /// Menue: Ashveil › Flora-Validation › 2 Szene bauen / 3 Messlauf starten.
    /// </summary>
    public static class FloraValidationSceneBuilder
    {
        public const string ScenePath = "Assets/_Ashveil/Scenes/Validation/FloraValidation_FV1.unity";

        [Serializable] private class HabItem { public string prefab, kind; public float x, z, rot, scale; }
        [Serializable] private class HabLayout { public HabItem[] items; }

        [MenuItem("Ashveil/Flora-Validation/2 Szene bauen", priority = 2)]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            FloraValidationImporter.EnsureFolder("Assets/_Ashveil/Scenes/Validation");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Licht / Umgebung (neutral, reproduzierbar, ohne Baking)
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.63f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.43f, 0.40f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.18f, 0.15f);
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.fog = false;

            var sunGo = new GameObject("FV_Sonne");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            sunGo.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;

            var camGo = new GameObject("FV_Kamera") { tag = "MainCamera" };
            Camera cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 3000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true;
            var cd = camGo.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = false;
            cd.renderShadows = true;
            camGo.AddComponent<AudioListener>();

            var envGo = new GameObject("FV_Umgebung");
            FloraEnvironment env = envGo.AddComponent<FloraEnvironment>();
            env.ApplyPreset(FloraEnvironment.WindPreset.Normal);

            Material ground = GroundMaterial();
            Transform hero = Area("FV_A_Hero", L.Hero, 1400, ground, new Vector3(560, 0, 0));
            Transform group = Area("FV_B_Gruppe", L.Group, 600, ground);
            Transform habitat = Area("FV_C_Habitat", L.Habitat, 800, ground);
            Transform stress = Area("FV_D_Stress", L.Stress, 1400, ground);
            Transform instSame = Area("FV_Inst_A_gleich", L.InstSame, 800, ground);
            Transform instSeeds = Area("FV_Inst_B_Seeds", L.InstSeeds, 800, ground);
            Transform instMixed = Area("FV_Inst_C_gemischt", L.InstMixed, 800, ground);
            Transform lineF01 = Area("FV_Reihe_F01", L.LineupF01, 1400, ground);
            Transform lineF04 = Area("FV_Reihe_F04a", L.LineupF04, 1400, ground);
            Transform f10Full = Area("FV_F10_voll", L.F10Full, 500, ground);
            Transform f10Lod = Area("FV_F10_LOD", L.F10Lod, 500, ground);
            Transform f19Full = Area("FV_F19_voll", L.F19Full, 500, ground);
            Transform f19Lod = Area("FV_F19_LOD", L.F19Lod, 500, ground);
            Transform calib = Area("FV_Kalibrierung_v3", L.Calib, 2400, ground, new Vector3(750, 0, 300));

            // A – Hero-Reihe
            for (int i = 0; i < L.HeroItems.Length; i++) Place(hero, L.HeroItems[i], L.HeroPos(i), 0, 1);
            // B – kleine Gruppe
            foreach (var (prefab, pos, rot) in L.GroupItems) Place(group, prefab, L.Group + pos, rot, 1, unique: false);
            // C – Mini-Habitat v2 (Layout aus dem externen Prototyp)
            HabLayout hab = JsonUtility.FromJson<HabLayout>(File.ReadAllText(Path.Combine(FloraValidationImporter.SourceDir, "layout_habitat2.json")));
            foreach (HabItem it in hab.items) Place(habitat, it.prefab, L.Habitat + new Vector3(it.x, 0, it.z), it.rot, it.scale, unique: false);
            // D – Stress: 3 x 3 Habitat-Kacheln (nur Messung)
            for (int tx = -1; tx <= 1; tx++)
                for (int tz = -1; tz <= 1; tz++)
                    foreach (HabItem it in hab.items)
                        Place(stress, it.prefab, L.Stress + new Vector3(tx * L.StressTile + it.x, 0, tz * L.StressTile + it.z), it.rot + 90 * (tx + 3 * tz), it.scale, unique: false);
            // Instancing-Raster
            var rnd = new System.Random(4711);
            string[] seeds = new string[6];
            for (int s = 0; s < 6; s++) seeds[s] = $"f01_v1_bestand_s{s + 1}";
            var mixed = new List<string>();
            foreach (string a in new[] { "f01_v1_bestand", "f01_v2_solitaer", "f01_v3_altbaum", "f04a_v1_bestand", "f04a_v2_freistand", "f04a_v3_windform" })
                for (int s = 1; s <= 6; s++) mixed.Add($"{a}_s{s}");
            for (int x = 0; x < L.InstGrid; x++)
                for (int z = 0; z < L.InstGrid; z++)
                {
                    Vector3 o = new Vector3(x * L.InstSpacing + (float)rnd.NextDouble() * 2f, 0, z * L.InstSpacing + (float)rnd.NextDouble() * 2f);
                    float rot = (float)rnd.NextDouble() * 360f;
                    Place(instSame, "f01_v1_bestand_s1", L.InstSame + o, rot, 1, unique: false);
                    Place(instSeeds, seeds[rnd.Next(seeds.Length)], L.InstSeeds + o, rot, 1, unique: false);
                    Place(instMixed, mixed[rnd.Next(mixed.Count)], L.InstMixed + o, rot, 1, unique: false);
                }
            // LOD-Reihen
            for (int i = 0; i < 3; i++)
            {
                Place(lineF01, L.LineupF01Items[i], L.LineupF01 + new Vector3((i - 1) * L.LineupSpacing, 0, 0), 0, 1);
                Place(lineF04, L.LineupF04Items[i], L.LineupF04 + new Vector3((i - 1) * L.LineupSpacing, 0, 0), 0, 1);
            }
            // F10-Felder (volle v2-Geometrie gegen LOD-Test), F19-Felder
            for (int x = 0; x < L.F10Grid; x++)
                for (int z = 0; z < L.F10Grid; z++)
                {
                    string s = (x + z) % 2 == 0 ? "sommer" : "trocken";
                    Vector3 o = new Vector3(x * L.F10Spacing, 0, z * L.F10Spacing);
                    float rot = (float)rnd.NextDouble() * 360f;
                    Place(f10Full, $"f10_group_{s}_hero", L.F10Full + o, rot, 1, unique: false);
                    Place(f10Lod, $"f10_group_{s}", L.F10Lod + o, rot, 1, unique: false);
                }
            for (int x = 0; x < L.F19GridX; x++)
                for (int z = 0; z < L.F19GridZ; z++)
                {
                    Vector3 o = new Vector3(x * L.F19Spacing, 0, z * L.F19Spacing);
                    float rot = (float)rnd.NextDouble() * 360f;
                    Place(f19Full, "f19_trunk_full", L.F19Full + o, rot, 1, unique: false);
                    Place(f19Lod, "f19_trunk", L.F19Lod + o, rot, 1, unique: false);
                }

            // v3-Kalibrierbereich: alle Einzel-LOD-Kandidaten einer Gruppe an derselben Position (der Lauf schaltet sie einzeln)
            for (int i = 0; i < L.CalTrees.Length; i++)
            {
                string t = L.CalTrees[i];
                var grp = new GameObject("FV_Cal_" + t).transform;
                grp.SetParent(calib, false);
                grp.position = L.Calib + new Vector3(i * 300f, 0, 0);
                foreach (string v in L.CalTreeVariants)
                    if (FloraValidationImporter.LoadPrefab($"cal_{t}_{v}") != null) Place(grp, $"cal_{t}_{v}", grp.position, 0, 1, unique: false);
            }
            string[] calSeasons = { "sommer", "trocken" };
            for (int i = 0; i < calSeasons.Length; i++)
            {
                var grp = new GameObject("FV_Cal_f10_" + calSeasons[i]).transform;
                grp.SetParent(calib, false);
                grp.position = L.Calib + new Vector3(i * 60f, 0, 600);
                foreach (string v in L.CalF10Variants)
                    if (FloraValidationImporter.LoadPrefab($"cal_f10_group_{calSeasons[i]}_{v}") != null)
                        Place(grp, $"cal_f10_group_{calSeasons[i]}_{v}", grp.position, 0, 1, unique: false);
            }

            // Messlauf
            var runGo = new GameObject("FV_Messlauf");
            FloraValidationRunner run = runGo.AddComponent<FloraValidationRunner>();
            run.cam = cam; run.sun = sun; run.env = env;
            run.areaHero = hero; run.areaGroup = group; run.areaHabitat = habitat; run.areaStress = stress;
            run.areaInstSame = instSame; run.areaInstSeeds = instSeeds; run.areaInstMixed = instMixed;
            run.areaLineupF01 = lineF01; run.areaLineupF04 = lineF04;
            run.areaF10Full = f10Full; run.areaF10Lod = f10Lod; run.areaF19Full = f19Full; run.areaF19Lod = f19Lod;
            run.areaCalib = calib;
            run.debugShader = Shader.Find("Ashveil/FloraValidation/FloraDebug");
            run.autoRun = true;

            // Startansicht: Gruppe B aus der Spielkamera
            camGo.transform.position = L.Group + new Vector3(0, 2.6f, -7);
            camGo.transform.LookAt(L.Group + new Vector3(0, 5, 18));

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[FloraValidation] Szene gebaut: {ScenePath} (nicht in den Build-Einstellungen)");
        }

        private static Material GroundMaterial()
        {
            string path = FloraValidationImporter.MatDir + "/M_FV_Boden.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(FloraValidationImporter.LitShader)); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(FloraValidationImporter.TexPath("soil_litter_v2.png")));
            m.SetTextureScale("_BaseMap", new Vector2(250, 250));
            Texture2D n = AssetDatabase.LoadAssetAtPath<Texture2D>(FloraValidationImporter.TexPath("soil_litter_v2_n.png"));
            m.SetTexture("_BumpMap", n);
            m.SetTextureScale("_BumpMap", new Vector2(250, 250));
            if (n != null) { m.EnableKeyword("_NORMALMAP"); m.SetFloat("_UseNormalMap", 1); }
            m.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.85f));
            m.SetFloat("_Smoothness", 0.08f);
            m.SetFloat("_Cull", (float)CullMode.Back);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Transform Area(string name, Vector3 origin, float groundSize, Material ground, Vector3 groundOffset = default)
        {
            var root = new GameObject(name).transform;
            root.position = origin;
            var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
            g.name = L.GroundName;
            UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(root, false);
            g.transform.localPosition = groundOffset;
            g.transform.localScale = new Vector3(groundSize / 10f, 1, groundSize / 10f);
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = ground;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return root;
        }

        private static readonly HashSet<string> Missing = new HashSet<string>();

        private static void Place(Transform parent, string prefab, Vector3 worldPos, float rotY, float scale, bool unique = true)
        {
            GameObject p = FloraValidationImporter.LoadPrefab(prefab);
            if (p == null)
            {
                if (Missing.Add(prefab)) Debug.LogWarning("[FloraValidation] Prefab fehlt: " + prefab);
                return;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(p, parent);
            go.name = prefab;
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.Euler(0, rotY, 0);
            go.transform.localScale = Vector3.one * (scale <= 0 ? 1 : scale);
            GameObjectUtility.SetStaticEditorFlags(go, 0);
        }

        // ------------------------------------------------------------------ Messlauf

        [MenuItem("Ashveil/Flora-Validation/3 Messlauf starten (Play)", priority = 3)]
        public static void RunFull() => Run(false, 0);

        [MenuItem("Ashveil/Flora-Validation/3b Kurzlauf (Funktionstest)", priority = 4)]
        public static void RunQuick() => Run(true, 0);

        [MenuItem("Ashveil/Flora-Validation/4 v3-Kalibrierung (Play)", priority = 5)]
        public static void RunCalibration() => Run(false, 1);

        [MenuItem("Ashveil/Flora-Validation/5 Player-Build (Development) + Messlauf", priority = 6)]
        public static void PlayerBuild() => FloraValidationPlayerBuild.BuildAndRun();

        [MenuItem("Ashveil/Flora-Validation/7 Micro-Pass Blindclips (Play)", priority = 8)]
        public static void RunBlind() => Run(false, 5);

        [MenuItem("Ashveil/Flora-Validation/6 GRD-Test (Play, isolierte Kopie)", priority = 7)]
        public static void RunGrd()
        {
            RenderPipelineAsset copy = FloraValidationPlayerBuild.GrdTestPipeline();
            if (copy == null) return;
            Run(false, 2, copy);
        }

        [MenuItem("Ashveil/Flora-Validation/Alles ausfuehren (Import, Szene, Messlauf)", priority = 20)]
        public static void All()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            try { FloraValidationImporter.Import(); }
            finally { EditorUtility.ClearProgressBar(); }
            Build();
            Run(false, 0);
        }

        private static void Run(bool quick, int mode, RenderPipelineAsset grd = null)
        {
            if (EditorApplication.isPlaying) return;
            if (SceneManager.GetActiveScene().path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                if (!File.Exists(Path.Combine(Ashveil.Core.ProjectPaths.ProjectRoot, ScenePath))) { Debug.LogError("[FloraValidation] Szene fehlt – zuerst „2 Szene bauen“."); return; }
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            var run = UnityEngine.Object.FindFirstObjectByType<FloraValidationRunner>();
            if (run == null) { Debug.LogError("[FloraValidation] FV_Messlauf fehlt in der Szene."); return; }
            run.quick = quick; run.autoRun = true; run.mode = mode; run.grdPipeline = grd;
            EditorUtility.SetDirty(run);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            EditorApplication.EnterPlaymode();
        }
    }
}
