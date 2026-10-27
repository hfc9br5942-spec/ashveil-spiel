using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kernmaterialien der Welt (PBR, 04.10.2026, Auftrag 04_PBR_PRODUKTION Teil 1): Import, URP/Lit-Materialien,
    /// Terrain-Layer (URP Terrain/Lit mit Maske und Höhenmischung), isolierter Testbereich „Welt_Materialtest“
    /// (Wiese, Erdweg mit Saum, Schotter, Fels, Stadtpflaster, Mauer, Holzbalken, Ausrüstung) und Prüfbilder.
    /// Bestehende Szenen und Kareth-Layer werden nicht verändert. Kareth v4 nur über den eigenen, umkehrbaren Schalter.
    /// </summary>
    public static class WeltMaterialtest
    {
        private const string Root = "Assets/_Ashveil/Art/Welt/Materialien";
        // Texturquelle (Max 07.10.: „Bei den Texturen die ich dir gegeben habe sind die Details nicht mehr erkennbar. Kannst du die
        // beibehalten?“ – „Erstelle aus den Texturen immer zum Stil und Spiel passende Versionen falls nötig“):
        //   „paket“      = aus Max' Farbbildern (Paket ASHVEIL_TEXTURES_V1; claude/welt/pbr_aus_paket.py): BaseColor ist sein Bild
        //                  (nahtlos, 2048), passende Karten daraus abgeleitet (1024) → Texturen_Paket (Standard)
        //   „prozedural“ = Sets vom 04.10. (pbr_welt.py, aus dem Paket nur die Palette) → Texturen (Vergleich und Rückweg)
        // Umschalten: Menü 12/13 oder Auftrag „texturen paket|prozedural“.
        public const string TexQuelleKey = "Ashveil.Welt.TexturQuelle";
        private const string TexDirProzedural = Root + "/Texturen";
        private const string TexDirPaket = Root + "/Texturen_Paket";
        public static bool PaketTexturen => EditorPrefs.GetString(TexQuelleKey, "paket") != "prozedural";
        private static string TexDir => PaketTexturen && Directory.Exists(TexDirPaket) ? TexDirPaket : TexDirProzedural;
        private const string MatDir = Root + "/Materialien";
        private const string LayerDir = Root + "/TerrainLayer";
        private const string MeshDir = Root + "/Meshes";
        private const string ScenePath = Root + "/Welt_Materialtest.unity";
        private const string TerrainDataPath = Root + "/Welt_Materialtest_Terrain.asset";
        private const int MaxSize = 1024;           // prozedurale Quellen 2048; im Spiel 1024 (Kachel 0,5–2 m → 0,5–2 mm/px)
        private const int MaxSizePaket = 2048;      // Paket: BaseColor 2048 (aus Max' 1254 periodisch hochgerechnet), Karten 1024

        // Set, Kachel (m), als Terrain-Layer
        private static readonly (string set, float tile, bool terrain)[] Sets =
        {
            ("T01_Wiesenboden", 2f, true), ("T03_Erdweg", 2f, true), ("T04_Schotterweg", 2f, true), ("T11_Granit", 2f, true),
            ("T15_Stadtpflaster", 2f, false), ("T17_Steinmauer", 2f, false), ("T21_Bauholz", 1f, false), ("T25_Braunes_Leder", 0.5f, false),
            ("T02_Waldboden", 2f, true), ("T05_Moorboden", 2f, true), ("T10_Asche", 1f, true), ("T39_Salzkruste", 1f, true),
            ("T18_Kalkputz", 2f, false), ("T19_Dachschiefer", 2f, false), ("T22_Holzbohlen", 2f, false),
        };

        // ================================================================== 1 Import
        [MenuItem("Ashveil/Welt/Materialien (PBR)/1 Import, Materialien, Terrain-Layer", priority = 580)]
        public static void Einrichten()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Ashveil] Weltmaterialien: nicht im Play-Modus."); return; }
            AssetDatabase.Refresh();
            foreach (string d in new[] { MatDir, LayerDir, MeshDir }) Directory.CreateDirectory(d);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var s in Sets)
                    foreach (string p in Directory.GetFiles($"{TexDir}/{s.set}", "*.png"))
                        TexturEinstellen(p.Replace('\\', '/'));
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
            var sb = new StringBuilder("[Ashveil] Weltmaterialien eingerichtet:");
            foreach (var s in Sets)
            {
                Material m = LitMat(s.set, s.tile);
                sb.Append($" {m.name}");
                if (s.terrain) { TerrainLayer l = Layer(s.set, s.tile); sb.Append($"+{l.name}"); }
            }
            TerrainMaterial4();
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
        }

        private static void TexturEinstellen(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            string n = Path.GetFileNameWithoutExtension(path);
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 8;                         // flache Böden unter flachem Blickwinkel
            // Paket (Max' Texturen): Dateien schon in Projektgröße (BaseColor 2048, Karten 1024) – voll übernehmen. Prozedurale
            // Quellen (2048) → 1024 wie bisher. Nur Zweierpotenzen: 1256er Quellen (NPOT) fielen am 07.10. auf unkomprimiertes
            // RGBA32 zurück (929 MB statt BC7).
            imp.maxTextureSize = path.Contains("/Texturen_Paket/") ? MaxSizePaket : MaxSize;
            imp.npotScale = TextureImporterNPOTScale.ToNearest;
            imp.isReadable = false;
            imp.alphaIsTransparency = false;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            if (n.EndsWith("_NormalGL")) { imp.textureType = TextureImporterType.NormalMap; }      // OpenGL +Y = Unity, keine Umkehr
            else if (n.EndsWith("_BaseColor")) { imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; }
            else if (n.EndsWith("_Height")) { imp.textureType = TextureImporterType.SingleChannel; imp.sRGBTexture = false; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.maxTextureSize = 512; }
            else { imp.textureType = TextureImporterType.Default; imp.sRGBTexture = false; }       // Roughness, AO, Masken: linear
            imp.SaveAndReimport();
        }

        private static Texture2D Tex(string set, string map) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{set}/{set}_{map}.png");

        /// <summary>URP/Lit für Meshes mit UVs in Metern (Kachelung = 1 / Kachelgröße).</summary>
        public static Material LitMat(string set, float tile)
        {
            string p = $"{MatDir}/W_{set}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (m == null) { m = new Material(sh) { name = "W_" + set }; AssetDatabase.CreateAsset(m, p); }
            m.shader = sh;
            m.SetFloat("_WorkflowMode", 1f);
            m.SetTexture("_BaseMap", Tex(set, "BaseColor")); m.SetColor("_BaseColor", Color.white);
            Texture2D mask = Tex(set, "URP_MetallicSmoothness");             // R = Metallic, G = AO, A = 1 − Roughness
            m.SetTexture("_MetallicGlossMap", mask); m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f); m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_BumpMap", Tex(set, "NormalGL")); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_OcclusionMap", mask); m.SetFloat("_OcclusionStrength", 1f); m.EnableKeyword("_OCCLUSIONMAP");
            Vector2 sc = Vector2.one / tile;
            m.SetTextureScale("_BaseMap", sc);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // Bodenkacheln (Terrain V2, Max 08.10.2026, Klick „r2, Kachel 3,5 m“): Faktor auf die Grundkachel der Terrain-Sets –
        // Standard 1,75: 2 m → 3,5 m, Asche und Salz 1 m → 1,75 m. Nur die PBR-Terrain-Layer (PBR_*, auch die Kareth-Wiese);
        // Mesh-Materialien (W_*) und Kareths Layer_* bleiben. Umkehrbar: Auftrag „bodenkachel 1“ (= bisherige 2 m / 1 m).
        public const string BodenKachelKey = "Ashveil.Welt.BodenKachelFaktor";
        public const float BodenKachelStandard = 1.75f;
        public static float BodenKachelFaktor => Mathf.Clamp(EditorPrefs.GetFloat(BodenKachelKey, BodenKachelStandard), 0.25f, 8f);

        public static TerrainLayer Layer(string set, float tile)
        {
            string p = $"{LayerDir}/PBR_{set}.terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(p);
            if (l == null) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, p); }
            l.diffuseTexture = Tex(set, "BaseColor");
            l.normalMapTexture = Tex(set, "NormalGL"); l.normalScale = 1f;
            l.maskMapTexture = Tex(set, "TerrainMask");                       // R Metallic, G AO, B Height, A Smoothness
            l.maskMapRemapMin = Vector4.zero; l.maskMapRemapMax = Vector4.one;
            float kachel = tile * BodenKachelFaktor;
            l.tileSize = new Vector2(kachel, kachel); l.tileOffset = Vector2.zero;
            l.metallic = 0f; l.smoothness = 0.2f;
            EditorUtility.SetDirty(l);
            return l;
        }

        /// <summary>Terrain/Lit mit Höhenmischung (nur bei höchstens 4 Layern wirksam) und Normal-/Masken-Schlüsselwörtern.</summary>
        private static Material TerrainMaterial4()
        {
            string p = $"{MatDir}/W_Terrain_Hoehenmischung.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            Shader sh = TerrainShader();
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            m.shader = sh; KachelfreiWerte(m);
            m.SetFloat("_EnableHeightBlend", 1f); m.SetFloat("_HeightTransition", 0.25f); m.EnableKeyword("_TERRAIN_BLEND_HEIGHT");
            m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_MASKMAP");
            m.SetFloat("_EnableInstancedPerPixelNormal", 1f); m.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Terrain/Lit für mehr als 4 Layer (Kareth v4, 7 Layer): ohne Höhenmischung, mit Normalen und Masken.</summary>
        public static Material TerrainMaterialViele()
        {
            string p = $"{MatDir}/W_Terrain_Viele_Layer.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            Shader sh = TerrainShader();
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            m.shader = sh; KachelfreiWerte(m);
            m.SetFloat("_EnableHeightBlend", 0f); m.DisableKeyword("_TERRAIN_BLEND_HEIGHT");
            m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_MASKMAP");
            m.SetFloat("_EnableInstancedPerPixelNormal", 1f); m.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            EditorUtility.SetDirty(m);
            return m;
        }


        // ================================================================== Kachelfreier Boden (eigener Terrain-Shader)
        // „Ashveil/Terrain/Lit (Kachelfrei)“ = URP Terrain/Lit mit Hex-Versatz je Zelle (kein sichtbares Kachelraster)
        // und großflächiger Helligkeits-/Trockenvariation in Weltkoordinaten. Rückfall: URP Terrain/Lit.
        public const string KachelfreiKey = "Ashveil.Welt.Kachelfrei";
        private static Shader TerrainShader() => Shader.Find("Ashveil/Terrain/Lit (Kachelfrei)") ?? Shader.Find("Universal Render Pipeline/Terrain/Lit");

        private static void KachelfreiWerte(Material m)
        {
            if (!m.HasProperty("_AshKachelfrei")) return;
            bool an = EditorPrefs.GetBool(KachelfreiKey, true);
            m.SetFloat("_AshKachelfrei", an ? 1f : 0f);
            m.SetFloat("_AshZellen", 0.4f); m.SetFloat("_AshKontrast", 4f);
            m.SetFloat("_AshMakroStaerke", an ? 0.20f : 0f);
            m.SetFloat("_AshMakroMassstab", 0.045f);
            m.SetVector("_AshMakroTon", new Vector4(1.15f, 1.02f, 0.72f, an ? 0.65f : 0f));
        }

        [MenuItem("Ashveil/Welt/Materialien (PBR)/8 Boden kachelfrei an", priority = 600)]
        public static void KachelfreiAn() => KachelfreiSchalten(true);
        [MenuItem("Ashveil/Welt/Materialien (PBR)/9 Boden kachelfrei aus (Vergleich)", priority = 601)]
        public static void KachelfreiAus() => KachelfreiSchalten(false);

        private static void KachelfreiSchalten(bool an)
        {
            if (EditorApplication.isPlaying) return;
            EditorPrefs.SetBool(KachelfreiKey, an);
            var mats = new[] { TerrainMaterial4(), TerrainMaterialViele() };
            AssetDatabase.SaveAssets();
            foreach (Terrain t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
                foreach (var m in mats)
                    if (t.materialTemplate == m) { t.materialTemplate = null; t.materialTemplate = m; t.Flush(); }
            Debug.Log($"[Ashveil] Boden kachelfrei {(an ? "an" : "aus")} – Shader {mats[0].shader.name}");
        }


        // ================================================================== 10 Kareth v4: Messung im fertigen Spiel (CORE-005)
        [MenuItem("Ashveil/Welt/Materialien (PBR)/10 Kareth v4: Build-Messung (fertiges Spiel)", priority = 602)]
        public static void KarethV4BuildMessung()
        {
            if (EditorApplication.isPlaying) return;
            if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            var scene = EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            Terrain terrain = Object.FindFirstObjectByType<Terrain>();
            KarethV4Plan plan = KarethV4Builder.LoadPlan();
            float G(float x, float z) => terrain != null ? terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y : 0f;
            var names = new List<string>(); var eyes = new List<Vector3>(); var targets = new List<Vector3>();
            foreach (KarethV4Blick b in plan.blickpunkte)
            {
                names.Add(b.name); eyes.Add(new Vector3(b.x, G(b.x, b.z) + 1.8f, b.z)); targets.Add(new Vector3(b.lx, b.ly, b.lz));
            }
            names.Add("Lager schräg (Übersicht)"); eyes.Add(new Vector3(60f, G(60f, 75f) + 38f, 75f)); targets.Add(new Vector3(0f, 1f, -10f));

            string root = Directory.GetParent(Application.dataPath).FullName;
            string dir = Path.Combine(root, "Builds", "Messung_KarethV4");
            string exe = Path.Combine(dir, "AshveilKV4.exe");
            KV4MessObjektEntfernen();
            scene = EditorSceneManager.GetActiveScene();
            // Spielsysteme (#26: Kamera, Netz, HUD) für die Messung aus – die Messung bringt ihre eigene Kamera mit
            int spielAus = KarethV4Spiel.SetActiveForMeasurement(false);
            if (spielAus > 0) UnityEngine.Debug.Log($"[Ashveil] Kareth v4 Mess-Build: {spielAus} Spielsystem-Objekte für die Messung ausgeschaltet.");
            var go = new GameObject("KV4 Messung (nur Mess-Build)");
            var m = go.AddComponent<Ashveil.Rendering.KarethV4Messung>();
            m.names = names.ToArray(); m.eyes = eyes.ToArray(); m.targets = targets.ToArray();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            UnityEditor.Build.Reporting.BuildReport report;
            var sw = Stopwatch.StartNew();
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { KarethV4Builder.ScenePath }, locationPathName = exe,
                    target = BuildTarget.StandaloneWindows64, options = BuildOptions.None,
                });
            }
            finally
            {
                // Unity lädt die Szene nach dem Bauen neu – Mess-Objekt deshalb aus der frisch geöffneten Szene entfernen
                KV4MessObjektEntfernen();
                KarethV4Spiel.SetActiveForMeasurement(true);
            }
            UnityEngine.Debug.Log($"[Ashveil] Kareth v4 Mess-Build: {report.summary.result}, {report.summary.totalSize / 1048576} MB, {sw.Elapsed.TotalSeconds:0} s, Fehler {report.summary.totalErrors} → {exe}");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) return;
            string txt = Path.Combine(dir, "KV4_Messung.txt");
            if (File.Exists(txt)) File.Delete(txt);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe, WorkingDirectory = dir, UseShellExecute = false,
                Arguments = $"-screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -logFile \"{Path.Combine(dir, "player.log")}\"",
            });
            UnityEngine.Debug.Log("[Ashveil] Kareth v4 Messung gestartet – Bericht: " + txt);
        }


        [MenuItem("Ashveil/Welt/Materialien (PBR)/11 Kareth v4: Mess-Objekt entfernen", priority = 603)]
        public static void KV4MessObjektEntfernen()
        {
            if (EditorApplication.isPlaying) return;
            var sc = EditorSceneManager.GetActiveScene();
            if (sc.path != KarethV4Builder.ScenePath) sc = EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            int n = 0;
            foreach (var m in Object.FindObjectsByType<Ashveil.Rendering.KarethV4Messung>(FindObjectsInactive.Include)) { Object.DestroyImmediate(m.gameObject); n++; }
            if (n > 0) { EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc); }
            UnityEngine.Debug.Log($"[Ashveil] Kareth v4: {n} Mess-Objekt(e) entfernt.");
        }

        // ================================================================== 2 Testbereich
        private const float Size = 80f, RangeY = 14f, BaseH = 3f;
        private static readonly Vector2 PlazaMin = new Vector2(8f, 30f), PlazaMax = new Vector2(20f, 40f);
        private static readonly Vector2[] PathPts = { new Vector2(20f, 35f), new Vector2(33f, 37.5f), new Vector2(48f, 27f), new Vector2(62f, 22f), new Vector2(76f, 24f) };
        private static readonly Vector2 Knoll = new Vector2(56f, 56f);

        [MenuItem("Ashveil/Welt/Materialien (PBR)/2 Testbereich bauen", priority = 581)]
        public static void TestbereichBauen()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Ashveil] Weltmaterialien: nicht im Play-Modus."); return; }
            if (Tex("T01_Wiesenboden", "BaseColor") == null) Einrichten();
            if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Licht und Himmel (Tageslicht; Dämmerung stellen die Prüfbilder um)
            var sky = AssetDatabase.GetBuiltinExtraResource<UnityEngine.Material>("Default-Skybox.mat");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox; RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.004f;
            RenderSettings.fogColor = new Color(0.62f, 0.66f, 0.70f);
            var sun = new GameObject("Sonne").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.7f; sun.color = new Color(1f, 0.95f, 0.88f); sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -40f, 0f);
            RenderSettings.sun = sun;

            Terrain t = TerrainBauen();
            float H(float x, float z) => t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y;
            var root = new GameObject("Testobjekte").transform;
            float plazaY = H(14f, 35f);

            // Stadtpflaster-Fläche (eigene Fläche über dem Gelände) mit Randsteinen aus Granit
            Material mPfl = LitMat("T15_Stadtpflaster", 2f), mMauer = LitMat("T17_Steinmauer", 2f), mHolz = LitMat("T21_Bauholz", 1f),
                     mLeder = LitMat("T25_Braunes_Leder", 0.5f), mFels = LitMat("T11_Granit", 2f);
            Box("Pflaster", root, new Vector3(14f, plazaY - 0.08f + 0.05f, 35f), new Vector3(12f, 0.16f, 10f), mPfl);
            for (float z = PlazaMin.y; z < PlazaMax.y - 0.1f; z += 0.9f)
                Box("Randstein", root, new Vector3(PlazaMax.x + 0.12f, plazaY - 0.02f, z + 0.45f), new Vector3(0.24f, 0.22f, 0.88f), mFels);

            // Mauer mit Tor (Holzrahmen) an der Nordkante des Platzes
            float wallZ = PlazaMax.y + 0.35f;
            Box("Mauer_West", root, new Vector3(10.75f, plazaY + 1.3f, wallZ), new Vector3(5.5f, 2.6f, 0.6f), mMauer);
            Box("Mauer_Ost", root, new Vector3(17.25f, plazaY + 1.3f, wallZ), new Vector3(5.5f, 2.6f, 0.6f), mMauer);
            Box("Torpfosten_W", root, new Vector3(13.35f, plazaY + 1.15f, wallZ), new Vector3(0.26f, 2.3f, 0.3f), mHolz, alongY: true);
            Box("Torpfosten_O", root, new Vector3(14.65f, plazaY + 1.15f, wallZ), new Vector3(0.26f, 2.3f, 0.3f), mHolz, alongY: true);
            Box("Torsturz", root, new Vector3(14f, plazaY + 2.45f, wallZ), new Vector3(1.9f, 0.3f, 0.34f), mHolz, alongY: false);
            Box("Mauerkrone_Balken", root, new Vector3(10.75f, plazaY + 2.68f, wallZ), new Vector3(5.5f, 0.16f, 0.5f), mHolz, alongY: false);
            // Holzgerüst (Pfosten und Riegel) am Weg
            Vector3 g0 = new Vector3(27f, 0, 41f); float gy = H(g0.x, g0.z);
            Box("Pfosten_1", root, new Vector3(g0.x - 1.6f, gy + 1.3f, g0.z), new Vector3(0.24f, 2.6f, 0.24f), mHolz, alongY: true);
            Box("Pfosten_2", root, new Vector3(g0.x + 1.6f, gy + 1.3f, g0.z), new Vector3(0.24f, 2.6f, 0.24f), mHolz, alongY: true);
            Box("Riegel", root, new Vector3(g0.x, gy + 2.45f, g0.z), new Vector3(3.8f, 0.24f, 0.28f), mHolz, alongY: false);
            // Hütte: Kalkputz, Fachwerk-Eckpfosten, Schieferdach, Bohlentür
            Material mPutz = LitMat("T18_Kalkputz", 2f), mDach = LitMat("T19_Dachschiefer", 2f), mBohle = LitMat("T22_Holzbohlen", 2f);
            Vector3 h0 = new Vector3(31f, 0f, 21f); float hy = H(h0.x, h0.z) - 0.1f;
            Box("Huette_Wand_S", root, new Vector3(h0.x, hy + 1.3f, h0.z - 2f), new Vector3(5f, 2.6f, 0.3f), mPutz);
            Box("Huette_Wand_N", root, new Vector3(h0.x, hy + 1.3f, h0.z + 2f), new Vector3(5f, 2.6f, 0.3f), mPutz);
            Box("Huette_Wand_W", root, new Vector3(h0.x - 2.35f, hy + 1.3f, h0.z), new Vector3(0.3f, 2.6f, 3.7f), mPutz);
            Box("Huette_Wand_O", root, new Vector3(h0.x + 2.35f, hy + 1.3f, h0.z), new Vector3(0.3f, 2.6f, 3.7f), mPutz);
            foreach (var c in new[] { new Vector2(-2.5f, -2.15f), new Vector2(2.5f, -2.15f), new Vector2(-2.5f, 2.15f), new Vector2(2.5f, 2.15f) })
                Box("Huette_Eckpfosten", root, new Vector3(h0.x + c.x, hy + 1.35f, h0.z + c.y), new Vector3(0.26f, 2.7f, 0.26f), mHolz, alongY: true);
            Box("Huette_Schwelle_S", root, new Vector3(h0.x, hy + 2.66f, h0.z - 2.15f), new Vector3(5.3f, 0.22f, 0.26f), mHolz, alongY: false);
            Box("Huette_Schwelle_N", root, new Vector3(h0.x, hy + 2.66f, h0.z + 2.15f), new Vector3(5.3f, 0.22f, 0.26f), mHolz, alongY: false);
            var dS = Box("Huette_Dach_S", root, new Vector3(h0.x, hy + 3.45f, h0.z - 1.25f), new Vector3(6f, 0.14f, 3.2f), mDach);
            dS.transform.rotation = Quaternion.Euler(-35f, 0f, 0f);
            var dN = Box("Huette_Dach_N", root, new Vector3(h0.x, hy + 3.45f, h0.z + 1.25f), new Vector3(6f, 0.14f, 3.2f), mDach);
            dN.transform.rotation = Quaternion.Euler(-35f, 180f, 0f);
            Box("Huette_Tuer", root, new Vector3(h0.x + 0.6f, hy + 1.05f, h0.z - 2.17f), new Vector3(1.0f, 1.9f, 0.08f), mBohle);
            // Kiste aus Holz mit Lederbündel und Kurzschwert
            Vector3 c0 = new Vector3(18.6f, plazaY + 0.4f, 38.6f);
            Box("Kiste", root, c0, new Vector3(0.9f, 0.8f, 0.6f), mHolz, alongY: false);
            var bund = Rund("Lederbuendel", root, PrimitiveType.Capsule, c0 + new Vector3(0f, 0.55f, 0f), new Vector3(0.28f, 0.32f, 0.28f), mLeder);
            bund.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            var kurz = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Ashveil/Art/Entwurf/Nachhall/Prefabs/Nachhall_Kurzschwert_Spiel.prefab");
            if (kurz != null)
            {
                var k = (GameObject)PrefabUtility.InstantiatePrefab(kurz, root);
                k.transform.SetPositionAndRotation(c0 + new Vector3(-0.05f, 0.42f, -0.18f), Quaternion.Euler(0f, 70f, 0f) * Quaternion.Euler(0f, 90f, 0f));
            }
            var gross = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Ashveil/Art/Entwurf/Nachhall/Prefabs/Nachhall_Grossschwert_Spiel.prefab");
            if (gross != null)
            {
                var g = (GameObject)PrefabUtility.InstantiatePrefab(gross, root);
                g.transform.SetPositionAndRotation(new Vector3(16.3f, plazaY + 0.55f, wallZ - 0.42f), Quaternion.Euler(-72f, 0f, 0f) * Quaternion.Euler(0f, 0f, 90f));
            }
            // Runde Körper je Material (Kugel + Zylinder) auf einer Reihe an der Wiese
            int i = 0;
            foreach (var s in Sets)
            {
                Vector3 pR = new Vector3(26f + (i % 8) * 2.2f, 0f, 49f + (i / 8) * 3.2f); pR.y = H(pR.x, pR.z);   // zwei Reihen à 8 (nicht auf die Felskuppe)
                Rund($"Kugel_{s.set}", root, PrimitiveType.Sphere, pR + Vector3.up * 0.5f, Vector3.one, LitMat(s.set, s.tile));
                Rund($"Zylinder_{s.set}", root, PrimitiveType.Cylinder, pR + new Vector3(0f, 0.6f, 1.4f), new Vector3(0.7f, 0.6f, 0.7f), LitMat(s.set, s.tile));
                i++;
            }
            // Größenreferenz 1,80 m am Weg
            Vector3 rp = new Vector3(30f, 0f, 37f); rp.y = H(rp.x, rp.z);
            var refc = GameObject.CreatePrimitive(PrimitiveType.Capsule); refc.name = "Referenz_1_80m"; refc.transform.SetParent(root);
            refc.transform.position = rp + Vector3.up * 0.9f; refc.transform.localScale = new Vector3(0.5f, 0.9f, 0.35f);
            var refm = new UnityEngine.Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.45f, 0.45f, 0.47f) };
            refc.GetComponent<Renderer>().sharedMaterial = refm;

            // PostFX wie im Spiel
            var vol = new GameObject("PostFX_Kareth").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Ashveil/Art/Rendering/Ashveil_Kareth_Profile.asset");
            var cam = new GameObject("Kamera").AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(new Vector3(36f, plazaY + 6f, 26f), Quaternion.Euler(18f, -60f, 0f));
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 1500f; cam.fieldOfView = 55f;
            var cd = cam.GetUniversalAdditionalCameraData(); cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Ashveil] Weltmaterialien: Testbereich gespeichert ({ScenePath}).");
        }

        // ---------------------------------------------------------------- Gelände
        private static float Fbm(float x, float z, float f, int oct, float seed)
        {
            float a = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < oct; o++) { a += (Mathf.PerlinNoise(x * f + seed, z * f + seed * 1.7f) - 0.5f) * amp; norm += amp; amp *= 0.5f; f *= 2.03f; }
            return a / norm;
        }

        /// <summary>Abstand zur Wegmitte (m) und Weglänge-Anteil 0..1 an der nächsten Stelle (Catmull-Rom-Polylinie).</summary>
        private static List<Vector2> _poly;
        private static (float d, float t) PathDist(Vector2 p)
        {
            if (_poly == null)
            {
                _poly = new List<Vector2>();
                for (int s = 0; s < PathPts.Length - 1; s++)
                {
                    Vector2 p0 = PathPts[Mathf.Max(0, s - 1)], p1 = PathPts[s], p2 = PathPts[s + 1], p3 = PathPts[Mathf.Min(PathPts.Length - 1, s + 2)];
                    for (int k = 0; k < 24; k++)
                    {
                        float u = k / 24f, u2 = u * u, u3 = u2 * u;
                        _poly.Add(0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3));
                    }
                }
                _poly.Add(PathPts[PathPts.Length - 1]);
            }
            float best = float.MaxValue; int bi = 0;
            for (int k = 0; k < _poly.Count - 1; k++)
            {
                Vector2 a = _poly[k], b = _poly[k + 1], ab = b - a;
                float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float d = (a + ab * u - p).magnitude;
                if (d < best) { best = d; bi = k; }
            }
            return (best, bi / (float)(_poly.Count - 1));
        }

        private static Terrain TerrainBauen()
        {
            _poly = null;
            AssetDatabase.DeleteAsset(TerrainDataPath);
            int res = 257, sres = 1024;
            var data = new TerrainData { heightmapResolution = res, alphamapResolution = sres, baseMapResolution = 1024 };
            data.size = new Vector3(Size, RangeY, Size);
            var h = new float[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = x / (res - 1f) * Size, wz = z / (res - 1f) * Size;
                float y = BaseH + 0.6f * Fbm(wx, wz, 0.035f, 4, 3.1f);
                // Felskuppe
                float r = Vector2.Distance(new Vector2(wx, wz), Knoll);
                float knoll = Mathf.Clamp01(1f - r / 17f); knoll = knoll * knoll * (3f - 2f * knoll);
                y += knoll * (6.5f + 2.5f * Fbm(wx, wz, 0.12f, 4, 7.7f)) + knoll * 1.2f * Mathf.Abs(Fbm(wx, wz, 0.3f, 3, 2.2f)) * 2f;
                // Platz eben, Weg leicht eingetieft und geglättet
                Vector2 pp = new Vector2(wx, wz);
                float inPlaza = Mathf.Clamp01(1f - Mathf.Max(Mathf.Max(PlazaMin.x - wx, wx - PlazaMax.x), Mathf.Max(PlazaMin.y - wz, wz - PlazaMax.y)) / 3f);
                y = Mathf.Lerp(y, BaseH, inPlaza);
                (float d, float tt) = PathDist(pp);
                float onPath = Mathf.Clamp01(1f - (d - 1.2f) / 2.5f);
                y = Mathf.Lerp(y, BaseH + 0.25f * Fbm(wx, wz, 0.05f, 2, 5f) - 0.04f, onPath * 0.9f);
                h[z, x] = y / RangeY;
            }
            data.SetHeights(0, 0, h);
            var a = new float[sres, sres, 4];
            for (int z = 0; z < sres; z++)
            for (int x = 0; x < sres; x++)
            {
                float wx = (x + 0.5f) / sres * Size, wz = (z + 0.5f) / sres * Size;
                Vector2 pp = new Vector2(wx, wz);
                (float d, float tt) = PathDist(pp);
                // Wegkern 1,1 m Halbbreite, abgetretener Saum mit unregelmäßiger Kante (keine Sinuslinie)
                float edgeN = Fbm(wx, wz, 0.9f, 3, 11f) * 0.9f + Fbm(wx, wz, 0.25f, 2, 13f) * 0.6f;
                float path = Mathf.Clamp01((1.9f + edgeN - d) / 0.8f);
                // abgetretene Flecken jenseits des Rands (weniger Gras, wo oft gegangen wird), unregelmäßig
                float worn = Mathf.Clamp01((Fbm(wx, wz, 0.7f, 3, 29f) * 2.2f + 0.25f) * Mathf.Clamp01((3.4f - d) / 1.5f));
                path = Mathf.Max(path, worn * 0.85f);
                float gravelZone = Mathf.Clamp01((0.45f - tt) / 0.08f);                 // erste Weghälfte geschottert
                // Schotterkante wandert: mal bis ins Gras, mal breiter Erdsaum
                float gEdge = 1.35f + 0.9f * Fbm(wx, wz, 0.45f, 3, 31f) + 0.35f * Fbm(wx, wz, 1.6f, 2, 17f);
                float gravel = path * gravelZone * Mathf.Clamp01((gEdge - d) / 0.45f);
                // Kies streut in den Erdsaum (Dichte zum Rand abnehmend)
                gravel = Mathf.Max(gravel, path * gravelZone * 0.35f * Mathf.Clamp01(Fbm(wx, wz, 2.2f, 2, 19f) * 3f + 0.2f));
                // Fels: Kuppe (Neigung und Höhe), mit Erde in Mulden
                float hx = data.GetInterpolatedHeight(wx / Size, wz / Size);
                float slope = data.GetSteepness(wx / Size, wz / Size);
                float rock = Mathf.Clamp01((slope - 22f) / 10f) + Mathf.Clamp01((hx - (BaseH + 4.5f)) / 1.5f);
                rock = Mathf.Clamp01(rock + 0.5f * Fbm(wx, wz, 0.5f, 3, 23f)) * Mathf.Clamp01((hx - BaseH - 0.6f) / 1f);
                // Platzrand: Erde
                float plaza = Mathf.Clamp01(1f - Mathf.Max(Mathf.Max(PlazaMin.x - wx, wx - PlazaMax.x), Mathf.Max(PlazaMin.y - wz, wz - PlazaMax.y)) / 1.2f);
                path = Mathf.Max(path, plaza * 0.9f);
                float wR = rock, wG = gravel * (1 - wR), wP = Mathf.Max(0f, path - gravel) * (1 - wR), wW = Mathf.Max(0f, 1f - wR - wG - wP);
                float sum = wW + wP + wG + wR;
                a[z, x, 0] = wW / sum; a[z, x, 1] = wP / sum; a[z, x, 2] = wG / sum; a[z, x, 3] = wR / sum;
            }
            // Erst als Asset anlegen, dann Gewichte setzen und die Alphamap-Texturen als Unter-Assets speichern
            // (sonst gingen die Gewichte beim Speichern/Neuladen verloren – Bild zeigte nur Layer 0)
            AssetDatabase.CreateAsset(data, TerrainDataPath);
            data.terrainLayers = new[] { Layer("T01_Wiesenboden", 2f), Layer("T03_Erdweg", 2f), Layer("T04_Schotterweg", 2f), Layer("T11_Granit", 2f) };
            data.SetAlphamaps(0, 0, a);
            foreach (Texture2D tx in data.alphamapTextures) if (tx != null && !AssetDatabase.Contains(tx)) AssetDatabase.AddObjectToAsset(tx, data);
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
            Debug.Log("[Ashveil] Weltmaterialien: Gewichte am Weg " + Gewichte(data, 36f, 36.5f) + ", am Fels " + Gewichte(data, 54f, 54f));
            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Testgelaende";
            var t = go.GetComponent<Terrain>();
            t.materialTemplate = TerrainMaterial4();
            t.drawInstanced = true; t.heightmapPixelError = 2f; t.basemapDistance = 400f;
            // Prüfen (Kareth-v4-Erfahrung: Gewichte gingen einmal nach dem Anlegen verloren) und notfalls erneut setzen
            int n = sres; int px = Mathf.RoundToInt(36f / Size * n), pz = Mathf.RoundToInt(36.5f / Size * n);
            if (Mathf.Abs(data.GetAlphamaps(px, pz, 1, 1)[0, 0, 0] - a[pz, px, 0]) > 0.02f)
            {
                Debug.LogWarning("[Ashveil] Weltmaterialien: Gewichte verloren – werden erneut gesetzt.");
                data.SetAlphamaps(0, 0, a);
                foreach (Texture2D tx in data.alphamapTextures) if (tx != null && !AssetDatabase.Contains(tx)) AssetDatabase.AddObjectToAsset(tx, data);
                EditorUtility.SetDirty(data); AssetDatabase.SaveAssets(); t.Flush();
            }
            return t;
        }

        private static string Gewichte(TerrainData d, float x, float z)
        {
            int n = d.alphamapResolution;
            int ix = Mathf.Clamp(Mathf.RoundToInt(x / Size * n), 0, n - 1), iz = Mathf.Clamp(Mathf.RoundToInt(z / Size * n), 0, n - 1);
            float[,,] w = d.GetAlphamaps(ix, iz, 1, 1);
            var sb = new StringBuilder();
            for (int l = 0; l < d.terrainLayers.Length; l++) sb.Append($"{d.terrainLayers[l].name.Replace("PBR_", "").Substring(0, 3)}={w[0, 0, l]:0.00} ");
            return sb.ToString().Trim();
        }

        // ---------------------------------------------------------------- Meshes mit UVs in Metern
        private static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, UnityEngine.Material m, bool alongY = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.position = center;
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size, alongY);
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        /// <summary>Quader, UV in Metern je Fläche. alongY: V läuft senkrecht (Pfosten, Mauer); sonst entlang der längsten Achse
        /// (Balken: Faser längs). Mauerlagen bleiben waagerecht.</summary>
        private static Mesh BoxMesh(Vector3 s, bool alongY)
        {
            string key = $"Box_{s.x:0.00}_{s.y:0.00}_{s.z:0.00}_{(alongY ? "V" : "L")}".Replace(',', '.');
            string p = $"{MeshDir}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (mesh != null) return mesh;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            Vector3 hs = s * 0.5f;
            void Face(Vector3 nrm, Vector3 uAx, Vector3 vAx, float uLen, float vLen)
            {
                int b = v.Count;
                Vector3 c = Vector3.Scale(nrm, hs);
                Vector3 du = uAx * (uLen * 0.5f), dv = vAx * (vLen * 0.5f);
                v.Add(c - du - dv); v.Add(c + du - dv); v.Add(c + du + dv); v.Add(c - du + dv);
                for (int k = 0; k < 4; k++) n.Add(nrm);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(uLen, 0)); uv.Add(new Vector2(uLen, vLen)); uv.Add(new Vector2(0, vLen));
                tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            // Längsachse für Balken: längste horizontale Achse
            bool longX = s.x >= s.z;
            if (alongY)
            {
                Face(Vector3.forward, Vector3.left, Vector3.up, s.x, s.y); Face(Vector3.back, Vector3.right, Vector3.up, s.x, s.y);
                Face(Vector3.right, Vector3.forward, Vector3.up, s.z, s.y); Face(Vector3.left, Vector3.back, Vector3.up, s.z, s.y);
                Face(Vector3.up, Vector3.right, Vector3.forward, s.x, s.z); Face(Vector3.down, Vector3.right, Vector3.back, s.x, s.z);
            }
            else if (longX)
            {   // V entlang X (Faser längs des Balkens)
                Face(Vector3.forward, Vector3.up, Vector3.left, s.y, s.x); Face(Vector3.back, Vector3.up, Vector3.right, s.y, s.x);
                Face(Vector3.up, Vector3.forward, Vector3.right, s.z, s.x); Face(Vector3.down, Vector3.back, Vector3.right, s.z, s.x);
                Face(Vector3.right, Vector3.forward, Vector3.up, s.z, s.y); Face(Vector3.left, Vector3.back, Vector3.up, s.z, s.y);
            }
            else
            {
                Face(Vector3.right, Vector3.up, Vector3.forward, s.y, s.z); Face(Vector3.left, Vector3.up, Vector3.back, s.y, s.z);
                Face(Vector3.up, Vector3.right, Vector3.forward, s.x, s.z); Face(Vector3.down, Vector3.left, Vector3.forward, s.x, s.z);
                Face(Vector3.forward, Vector3.left, Vector3.up, s.x, s.y); Face(Vector3.back, Vector3.right, Vector3.up, s.x, s.y);
            }
            mesh = new Mesh { name = key };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            // Dreiecksorientierung prüfen (Normale aus Kreuzprodukt muss zur Flächennormale zeigen), sonst umdrehen
            var ts = mesh.triangles; var vs = mesh.vertices; var ns = mesh.normals;
            for (int k = 0; k < ts.Length; k += 3)
            {
                Vector3 fn = Vector3.Cross(vs[ts[k + 1]] - vs[ts[k]], vs[ts[k + 2]] - vs[ts[k]]);
                if (Vector3.Dot(fn, ns[ts[k]]) < 0) { int tmp = ts[k + 1]; ts[k + 1] = ts[k + 2]; ts[k + 2] = tmp; }
            }
            mesh.triangles = ts; mesh.RecalculateTangents();
            AssetDatabase.CreateAsset(mesh, p);
            return mesh;
        }

        private static GameObject Rund(string name, Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, UnityEngine.Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.position = pos; go.transform.localScale = scale;
            // Primitive haben UV 0..1 je Körper → Material-Kopie mit Kachelung passend zum Umfang (≈ 3 m bei Ø 1 m)
            var mi = new UnityEngine.Material(m) { name = m.name + "_Rund" };
            float circ = Mathf.PI * Mathf.Max(scale.x, scale.z);
            float tile = 1f / m.GetTextureScale("_BaseMap").x;
            mi.SetTextureScale("_BaseMap", new Vector2(circ / tile, (type == PrimitiveType.Sphere ? circ * 0.5f : scale.y * 2f) / tile));
            go.GetComponent<Renderer>().sharedMaterial = mi;
            return go;
        }

        // ================================================================== 3 Prüfbilder und Messung
        private struct Shot { public string Name; public Vector3 Eye, Target; public float Fov; }

        [MenuItem("Ashveil/Welt/Materialien (PBR)/3 Prüfbilder und Messung", priority = 582)]
        public static void Pruefbilder()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Ashveil] Weltmaterialien: nicht im Play-Modus."); return; }
            if (!File.Exists(ScenePath)) TestbereichBauen();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string dir = Ashveil.Core.ProjectPaths.Logs("WeltMaterialien");
            Directory.CreateDirectory(dir);
            Terrain t = Object.FindFirstObjectByType<Terrain>();
            float H(float x, float z) => t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y;
            float py = H(14f, 35f);
            var shots = new List<Shot>
            {
                new Shot { Name = "01_Nah_Pflaster_Rand", Eye = new Vector3(21.6f, py + 1.4f, 33.2f), Target = new Vector3(19.4f, py, 34.6f), Fov = 50f },
                new Shot { Name = "02_Nah_Wegsaum", Eye = new Vector3(37f, H(37f, 33f) + 1.5f, 33.5f), Target = new Vector3(37.5f, H(37.5f, 35.5f), 35.5f), Fov = 55f },
                new Shot { Name = "03_Nah_Schotter_Erde", Eye = new Vector3(44f, H(44f, 27f) + 1.6f, 26f), Target = new Vector3(46.5f, H(46.5f, 28f), 28.5f), Fov = 55f },
                new Shot { Name = "04_Nah_Mauer_Tor", Eye = new Vector3(15.5f, py + 1.6f, 36.6f), Target = new Vector3(14f, py + 1.4f, 40.5f), Fov = 55f },
                new Shot { Name = "05_Nah_Fels", Eye = new Vector3(48f, H(48f, 50f) + 1.8f, 50f), Target = new Vector3(53f, H(53f, 54f) + 0.5f, 54f), Fov = 55f },
                new Shot { Name = "06_Spielkamera_Weg", Eye = new Vector3(24f, H(24f, 31f) + 3.2f, 31.5f), Target = new Vector3(36f, H(36f, 36f) + 0.8f, 36f), Fov = 60f },
                new Shot { Name = "07_Spielkamera_Platz", Eye = new Vector3(25f, py + 3.4f, 30f), Target = new Vector3(14f, py + 1.2f, 37.5f), Fov = 60f },
                new Shot { Name = "08_Fern", Eye = new Vector3(78f, H(78f, 4f) + 22f, 4f), Target = new Vector3(34f, BaseH, 40f), Fov = 55f },
                new Shot { Name = "10_Huette", Eye = new Vector3(26.5f, H(26.5f, 14.5f) + 2.0f, 14.5f), Target = new Vector3(31f, H(31f, 21f) + 1.9f, 21f), Fov = 55f },
                new Shot { Name = "09_Rundkoerper", Eye = new Vector3(33.5f, H(33.5f, 44f) + 1.8f, 44f), Target = new Vector3(33.5f, H(33.5f, 50f) + 0.6f, 50f), Fov = 62f },
            };
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
            Quaternion tagRot = sun.transform.rotation; float tagI = sun.intensity; Color tagC = sun.color;
            Color fogTag = RenderSettings.fogColor;
            var camGo = new GameObject("Pruefkamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f; cam.farClipPlane = 1500f; cam.allowHDR = true;
            var cd = cam.GetUniversalAdditionalCameraData(); cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var rep = new StringBuilder($"ASHVEIL Weltmaterialien – Prüfbericht {System.DateTime.Now:yyyy-MM-dd HH:mm}\n\n");
            try
            {
                Render(cam, Path.Combine(dir, "_aufwaermen.png"), 64, 64);
                foreach (string zeit in new[] { "Tag", "Daemmerung" })
                {
                    if (zeit == "Tag") { sun.transform.rotation = tagRot; sun.intensity = tagI; sun.color = tagC; RenderSettings.fogColor = fogTag; RenderSettings.ambientIntensity = 1f; }
                    else
                    {
                        // Tiefe Abendsonne seitlich (Streiflicht über Pflaster, Mauer und Weg), Himmel bläulich
                        sun.transform.rotation = Quaternion.Euler(7f, 25f, 0f); sun.intensity = 1.3f; sun.color = new Color(1f, 0.64f, 0.40f);
                        RenderSettings.fogColor = new Color(0.50f, 0.45f, 0.46f); RenderSettings.ambientIntensity = 1.15f;
                    }
                    DynamicGI.UpdateEnvironment();
                    foreach (Shot s in shots)
                    {
                        cam.transform.position = s.Eye; cam.transform.rotation = Quaternion.LookRotation(s.Target - s.Eye); cam.fieldOfView = s.Fov;
                        Render(cam, Path.Combine(dir, $"{s.Name}_{zeit}.png"), 1600, 900);
                    }
                }
                sun.transform.rotation = tagRot; sun.intensity = tagI; sun.color = tagC; RenderSettings.fogColor = fogTag; RenderSettings.ambientIntensity = 1f;
                DynamicGI.UpdateEnvironment();

                // Vorher/Nachher: dieselbe Spielkamera mit den bisherigen Kareth-Bodenschichten (Pixeltexturen, ohne Normalen)
                TerrainLayer[] pbr = t.terrainData.terrainLayers;
                var alt = new[] { "Grass", "Path", "Path", "Rock" }.Select(n => AssetDatabase.LoadAssetAtPath<TerrainLayer>($"Assets/_Ashveil/Terrain/Layer_{n}.terrainlayer")).ToArray();
                if (alt.All(l => l != null))
                {
                    t.terrainData.terrainLayers = alt;
                    foreach (Shot s in shots.Where(x => x.Name.StartsWith("06") || x.Name.StartsWith("02")))
                    {
                        cam.transform.position = s.Eye; cam.transform.rotation = Quaternion.LookRotation(s.Target - s.Eye); cam.fieldOfView = s.Fov;
                        Render(cam, Path.Combine(dir, $"{s.Name}_Vorher_Kareth_Pixel.png"), 1600, 900);
                    }
                    t.terrainData.terrainLayers = pbr;
                }

                // Messung: Renderzeit (CPU, Editor, Mittel über 30 Bilder), Texturspeicher, Layer/Pässe
                Shot sp = shots.First(x => x.Name.StartsWith("06"));
                cam.transform.position = sp.Eye; cam.transform.rotation = Quaternion.LookRotation(sp.Target - sp.Eye);
                var rt = new RenderTexture(1920, 1080, 24); cam.targetTexture = rt;
                for (int k = 0; k < 5; k++) cam.Render();
                var sw = Stopwatch.StartNew(); for (int k = 0; k < 30; k++) cam.Render(); sw.Stop();
                cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt);
                rep.AppendLine($"Renderzeit Spielkamera 1920×1080 (Editor, CPU-Seite inkl. Übergabe, Mittel 30 Bilder): {sw.Elapsed.TotalMilliseconds / 30:0.00} ms");
                long mem = 0; var texs = new HashSet<Texture>();
                foreach (var s in Sets)
                    foreach (string map in new[] { "BaseColor", "NormalGL", "URP_MetallicSmoothness", "TerrainMask" })
                    {
                        var tx = Tex(s.set, map); if (tx == null || !texs.Add(tx)) continue;
                        mem += Profiler.GetRuntimeMemorySizeLong(tx);
                    }
                rep.AppendLine($"Texturen der {Sets.Length} Sets im Speicher (Editor-Messung, 1024², BC-komprimiert, Mipmaps): {texs.Count} Texturen, {mem / 1048576f:0.0} MB");
                rep.AppendLine($"Gewichte (gespeichert, frisch geladen): Weg {Gewichte(t.terrainData, 36f, 36.5f)} · Schotter {Gewichte(t.terrainData, 26f, 36.5f)} · Fels {Gewichte(t.terrainData, 54f, 54f)}");
                rep.AppendLine($"Terrain: {t.terrainData.terrainLayers.Length} Layer (1 Pass), Höhenmischung {(t.materialTemplate.IsKeywordEnabled("_TERRAIN_BLEND_HEIGHT") ? "an" : "aus")}, Material {t.materialTemplate.shader.name}");
                int rends = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length;
                var mats = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct().Count();
                rep.AppendLine($"Testobjekte: {rends} Mesh-Renderer, {mats} verschiedene Materialien");
                rep.AppendLine("\nBilder: " + dir);
                File.WriteAllText(Path.Combine(dir, "Bericht.txt"), rep.ToString());
                Debug.Log("[Ashveil] Weltmaterialien: Prüfbilder und Bericht in " + dir);
            }
            finally { Object.DestroyImmediate(camGo); }
        }

        private static void Render(Camera cam, string path, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req); else cam.Render();
            var act = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = act; cam.targetTexture = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        }

        // ================================================================== 4 Kareth v4 (umkehrbar)
        public const string KV4Key = "Ashveil.KarethV4.PBRBoden";
        private static readonly Dictionary<string, string> KV4Map = new Dictionary<string, string>
        {
            { "Grass", "T01_Wiesenboden" }, { "Rock", "T11_Granit" }, { "Path", "T03_Erdweg" },
            { "ForestFloor", "T02_Waldboden" }, { "Moor", "T05_Moorboden" }, { "Ash", "T10_Asche" }, { "Sand", "T39_Salzkruste" },
        };

        /// <summary>Für den Kareth-v4-Bau: PBR-Layer statt der Pixel-Layer, wenn eingeschaltet und vorhanden.</summary>
        public static TerrainLayer KarethV4Layer(string name)
        {
            if (!EditorPrefs.GetBool(KV4Key, false) || !KV4Map.TryGetValue(name, out string set)) return null;
            if (set == "T01_Wiesenboden")
            {
                var k = AssetDatabase.LoadAssetAtPath<TerrainLayer>(KV4WiesePfad);
                if (k != null) return k;
            }
            return AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{LayerDir}/PBR_{set}.terrainlayer");
        }

        // Wiese nur für Kareth etwas grüner (Max 04.10.: „etwas grüner“). Eigener Layer mit Farbfaktor (Diffuse-Remap, linear);
        // das Set T01 und PBR_T01_Wiesenboden (Testbereich) bleiben unverändert. Mittel sRGB 0,35/0,37/0,19 → ≈ 0,32/0,38/0,18.
        private const string KV4WiesePfad = LayerDir + "/PBR_T01_Wiesenboden_Kareth.terrainlayer";
        public static readonly Vector4 KV4WieseTon = new Vector4(0.82f, 1.05f, 0.90f, 1f);

        private static void KV4WieseAnlegen()
        {
            var basis = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{LayerDir}/PBR_T01_Wiesenboden.terrainlayer");
            if (basis == null) return;
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(KV4WiesePfad);
            if (l == null) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, KV4WiesePfad); }
            l.diffuseTexture = basis.diffuseTexture; l.normalMapTexture = basis.normalMapTexture; l.normalScale = basis.normalScale;
            l.maskMapTexture = basis.maskMapTexture; l.maskMapRemapMin = basis.maskMapRemapMin; l.maskMapRemapMax = basis.maskMapRemapMax;
            l.tileSize = basis.tileSize; l.tileOffset = basis.tileOffset; l.metallic = basis.metallic; l.smoothness = basis.smoothness;
            l.diffuseRemapMin = Vector4.zero; l.diffuseRemapMax = KV4WieseTon;
            EditorUtility.SetDirty(l);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Ashveil/Welt/Materialien (PBR)/4 Kareth v4: PBR-Boden an (alle 7 Schichten)", priority = 583)]
        public static void KarethV4An() => KarethV4Schalten(true);

        [MenuItem("Ashveil/Welt/Materialien (PBR)/5 Kareth v4: PBR-Boden aus (bisherige Layer)", priority = 584)]
        public static void KarethV4Aus() => KarethV4Schalten(false);

        private static void KarethV4Schalten(bool an)
        {
            if (EditorApplication.isPlaying) return;
            EditorPrefs.SetBool(KV4Key, an);
            if (an) KV4WieseAnlegen();
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/_Ashveil/Terrain/KarethV4_TerrainData.asset");
            if (data == null) { Debug.LogWarning("[Ashveil] Kareth v4: TerrainData fehlt – Szene zuerst bauen."); return; }
            KarethV4Plan plan = KarethV4Builder.LoadPlan();
            var layers = data.terrainLayers.ToArray();
            for (int i = 0; i < plan.layers.Length && i < layers.Length; i++)
            {
                TerrainLayer pbr = an ? KarethV4Layer(plan.layers[i]) : null;
                layers[i] = pbr != null ? pbr : AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/_Ashveil/Terrain/Layer_" + plan.layers[i] + ".terrainlayer") ?? layers[i];
            }
            data.terrainLayers = layers;
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
            // Material nur in der Kareth-v4-Szene tauschen (Kareth_Terrain.mat bleibt für die Sandbox unverändert)
            if (EditorSceneManager.GetActiveScene().path == KarethV4Builder.ScenePath)
            {
                Terrain t = Object.FindFirstObjectByType<Terrain>();
                if (t != null)
                {
                    t.materialTemplate = an ? TerrainMaterialViele() : AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/_Ashveil/Terrain/Kareth_Terrain.mat");
                    EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
                    EditorSceneManager.SaveScene(t.gameObject.scene);
                }
            }
            Debug.Log($"[Ashveil] Kareth v4: PBR-Boden {(an ? "an" : "aus")} – Layer: {string.Join(", ", layers.Select(l => l != null ? l.name : "null"))}");
        }
    
        // ================================================================== 6/7 Kareth v4: Gebäude (umkehrbar)
        // Greybox-Material → (PBR-Set, Kachel, Art). Farben der Greybox bleiben als Tönung erhalten (Kareth-Palette).
        private enum Art { Mauer, Holz, Dach }
        private static readonly Dictionary<string, (string set, float tile, Art art)> KV4Bau = new Dictionary<string, (string, float, Art)>
        {
            { "KV4 Stein", ("T17_Steinmauer", 2f, Art.Mauer) }, { "KV4 Stein dunkel", ("T17_Steinmauer", 2f, Art.Mauer) },
            { "KV4 Stein bemoost", ("T17_Steinmauer", 2f, Art.Mauer) },
            { "KV4 Holz", ("T21_Bauholz", 1f, Art.Holz) }, { "KV4 Holz dunkel", ("T21_Bauholz", 1f, Art.Holz) },
            { "KV4 Holz frisch", ("T21_Bauholz", 1f, Art.Holz) }, { "KV4 Holz alt (vergraut)", ("T22_Holzbohlen", 2f, Art.Holz) },
            { "KV4 Lehmputz", ("T18_Kalkputz", 2f, Art.Mauer) },
            { "KV4 Schindeln", ("T19_Dachschiefer", 2f, Art.Dach) }, { "KV4 Schindeln Halle", ("T19_Dachschiefer", 2f, Art.Dach) },
        };
        // Mittlere BaseColor der Sets (sRGB, aus den Manifesten) – für die Tönung auf die Greybox-Farbe
        private static readonly Dictionary<string, Color> SetMittelProzedural = new Dictionary<string, Color>
        {
            { "T17_Steinmauer", new Color(0.570f, 0.538f, 0.483f) }, { "T21_Bauholz", new Color(0.502f, 0.377f, 0.263f) },
            { "T22_Holzbohlen", new Color(0.342f, 0.305f, 0.261f) }, { "T18_Kalkputz", new Color(0.771f, 0.743f, 0.684f) },
            { "T19_Dachschiefer", new Color(0.258f, 0.267f, 0.294f) },
        };
        // dieselben Werte für die Paket-Texturen (claude/welt/paket/<Set>/<Set>_paket.json, basecolor_srgb_mittel, 07.10.)
        private static readonly Dictionary<string, Color> SetMittelPaket = new Dictionary<string, Color>
        {
            { "T17_Steinmauer", new Color(0.595f, 0.549f, 0.485f) }, { "T21_Bauholz", new Color(0.511f, 0.385f, 0.269f) },
            { "T22_Holzbohlen", new Color(0.450f, 0.395f, 0.338f) }, { "T18_Kalkputz", new Color(0.833f, 0.796f, 0.739f) },
            { "T19_Dachschiefer", new Color(0.296f, 0.301f, 0.304f) },
        };
        private static Dictionary<string, Color> SetMittel => TexDir == TexDirPaket ? SetMittelPaket : SetMittelProzedural;
        /// <summary>PBR-Gebäude beim Kareth-v4-Bau anwenden (Menü 6 an / 7 aus). Bis 07.10. galt der Tausch nur bis zum nächsten
        /// Neuaufbau – danach stand wieder die Greybox (Befund 07.10.).</summary>
        public const string KV4GebaeudeKey = "Ashveil.KarethV4.PBRGebaeude";
        private const string KV4MeshDir = MeshDir + "/KV4";
        private const string KV4MatPrefix = "W_KV4_";

        [MenuItem("Ashveil/Welt/Materialien (PBR)/6 Kareth v4: PBR-Gebäude an (Stein, Holz, Putz, Dach)", priority = 585)]
        public static void KarethV4GebaeudeAn()
        {
            if (EditorApplication.isPlaying) return;
            EditorPrefs.SetBool(KV4GebaeudeKey, true);
            if (EditorSceneManager.GetActiveScene().path != KarethV4Builder.ScenePath) EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            string bericht = KarethV4GebaeudeAnwenden();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[Ashveil] " + bericht);
        }

        /// <summary>PBR-Gebäude in der gerade offenen Szene – auch mitten im Kareth-v4-Bau vor dem Speichern (KarethV4Builder):
        /// Greybox-Quader und -Zylinder der KV4-Materialien bekommen Meshes mit UVs in Metern und die getönten W_KV4_*-Materialien.
        /// Öffnet und speichert nichts.</summary>
        public static string KarethV4GebaeudeAnwenden()
        {
            Directory.CreateDirectory(KV4MeshDir);
            var mats = new Dictionary<string, UnityEngine.Material>();
            int box = 0, cyl = 0, other = 0;
            Mesh builtinCube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"), builtinZyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            var andere = new HashSet<string>();
            var meshes = new Dictionary<string, Mesh>();
                foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || r.sharedMaterials.Length != 1 || r.sharedMaterial == null) continue;
                    string orig = r.sharedMaterial.name;
                    if (!KV4Bau.TryGetValue(orig, out var e)) continue;
                    string mn = mf.sharedMesh.name;
                    Vector3 sc = r.transform.lossyScale; sc = new Vector3(Mathf.Abs(sc.x), Mathf.Abs(sc.y), Mathf.Abs(sc.z));
                    Mesh m;
                    if (mn == "Cube" || mf.sharedMesh == builtinCube) { m = KV4Box(sc, e.art, r.transform, meshes); box++; }
                    else if (mn == "Cylinder" || mf.sharedMesh == builtinZyl) { m = KV4Zylinder(sc, meshes); cyl++; }
                    else { other++; if (andere.Count < 6) andere.Add(mn); continue; }
                    mf.sharedMesh = m;
                    if (!mats.TryGetValue(orig, out var pm)) mats[orig] = pm = KV4Material(orig, r.sharedMaterial.color, e.set, e.tile);
                    r.sharedMaterial = pm;
                }
            AssetDatabase.SaveAssets();
            return $"Kareth v4: PBR-Gebäude an ({(TexDir == TexDirPaket ? "Texturen aus Max' Paket" : "prozedurale Texturen")}) – {box} Quader, {cyl} Zylinder umgestellt, " +
                   $"{other} andere Formen unverändert ({string.Join(", ", andere)}); {meshes.Count} Meshes, {mats.Count} Materialien.";
        }

        [MenuItem("Ashveil/Welt/Materialien (PBR)/7 Kareth v4: PBR-Gebäude aus (Greybox)", priority = 586)]
        public static void KarethV4GebaeudeAus()
        {
            if (EditorApplication.isPlaying) return;
            EditorPrefs.SetBool(KV4GebaeudeKey, false);
            if (EditorSceneManager.GetActiveScene().path != KarethV4Builder.ScenePath) EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"), cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            int n = 0;
            foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.sharedMaterial == null || !r.sharedMaterial.name.StartsWith(KV4MatPrefix)) continue;
                string orig = r.sharedMaterial.name.Substring(KV4MatPrefix.Length);
                var om = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>($"{ProjectSetup.GreyboxMaterials}/{orig}.mat");
                var mf = r.GetComponent<MeshFilter>();
                if (om == null || mf == null) continue;
                r.sharedMaterial = om;
                if (mf.sharedMesh != null) mf.sharedMesh = mf.sharedMesh.name.StartsWith("KV4Zyl") ? cylinder : cube;
                n++;
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Ashveil] Kareth v4: PBR-Gebäude aus – {n} Teile auf Greybox zurückgesetzt.");
        }

        // ================================================================== 12/13 Texturquelle (Max 07.10.)
        [MenuItem("Ashveil/Welt/Materialien (PBR)/12 Texturen aus Max' Paket (Standard)", priority = 604)]
        public static void TexturenPaket() => Debug.Log("[Ashveil] " + TexturenAnwenden(true));

        [MenuItem("Ashveil/Welt/Materialien (PBR)/13 Texturen prozedural (Vergleich, Rückweg)", priority = 605)]
        public static void TexturenProzedural() => Debug.Log("[Ashveil] " + TexturenAnwenden(false));

        /// <summary>Texturquelle umschalten und neu zuweisen: Import (Paket: BaseColor 2048, Karten 1024, BC7/BC5), W_*-Materialien, PBR_*-Layer,
        /// Kareth-Wiese (eigener Layer mit Farbfaktor) und die W_KV4_*-Gebäudematerialien (Tönung mit dem Mittel der Quelle).
        /// Szenen werden nicht neu gebaut – Terrain-Layer und Materialien sind Assets und wirken sofort.</summary>
        public static string TexturenAnwenden(bool paket)
        {
            if (EditorApplication.isPlaying) return "Texturen: nicht im Play-Modus";
            EditorPrefs.SetString(TexQuelleKey, paket ? "paket" : "prozedural");
            if (paket && !Directory.Exists(TexDirPaket)) return "Texturen: " + TexDirPaket + " fehlt – nichts umgestellt";
            Einrichten();
            if (EditorPrefs.GetBool(KV4Key, false)) KV4WieseAnlegen();
            int kv4 = 0;
            foreach (var kv in KV4Bau)
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Material>($"{MatDir}/{KV4MatPrefix}{kv.Key}.mat") == null) continue;
                var grey = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>($"{ProjectSetup.GreyboxMaterials}/{kv.Key}.mat");
                if (grey == null) continue;
                KV4Material(kv.Key, grey.color, kv.Value.set, kv.Value.tile);
                kv4++;
            }
            AssetDatabase.SaveAssets();
            return $"Texturen {(TexDir == TexDirPaket ? "aus Max' Paket (" + TexDirPaket + ")" : "prozedural (" + TexDirProzedural + ")")}: " +
                   $"{Sets.Length} Sets eingerichtet, {kv4} Gebäudematerialien neu getönt · {TexturBericht()}";
        }

        /// <summary>
        /// Bodenkacheln setzen (Auftrag „bodenkachel“): Faktor merken und alle vorhandenen PBR-Terrain-Layer neu einstellen, dazu die
        /// Kareth-Wiese. Terrain-Layer sind Assets – Kareth v4 und der Testbereich zeigen es sofort, ohne Neubau.
        /// </summary>
        public static string BodenKachelSetzen(float faktor)
        {
            if (EditorApplication.isPlaying) return "Bodenkacheln: nicht im Play-Modus";
            float f = Mathf.Clamp(faktor, 0.25f, 8f);
            EditorPrefs.SetFloat(BodenKachelKey, f);
            var sb = new StringBuilder();
            int n = 0;
            foreach (var s in Sets)
            {
                if (!s.terrain || AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{LayerDir}/PBR_{s.set}.terrainlayer") == null) continue;
                TerrainLayer l = Layer(s.set, s.tile);
                sb.Append(n++ > 0 ? ", " : "").Append($"{s.set} {l.tileSize.x:0.##} m");
            }
            bool wiese = EditorPrefs.GetBool(KV4Key, false);
            if (wiese) KV4WieseAnlegen();
            AssetDatabase.SaveAssets();
            var kareth = AssetDatabase.LoadAssetAtPath<TerrainLayer>(KV4WiesePfad);
            return $"Bodenkacheln: Faktor {f:0.##} auf {n} PBR-Terrain-Layer ({sb})" +
                   (wiese && kareth != null ? $"; Kareth-Wiese {kareth.tileSize.x:0.##} m" : "; Kareth v4 nutzt die PBR-Layer nicht (Schalter aus)");
        }

        /// <summary>Aktive Quelle, Größen, Formate und Speicher aller Set-Texturen (Editor; im Spiel nur die benutzten).</summary>
        public static string TexturBericht()
        {
            long bytes = 0;
            int n = 0;
            var groessen = new SortedSet<string>();
            var formate = new SortedSet<string>();
            foreach (var s in Sets)
                foreach (string map in new[] { "BaseColor", "NormalGL", "TerrainMask", "URP_MetallicSmoothness" })
                {
                    Texture2D t = Tex(s.set, map);
                    if (t == null) continue;
                    n++;
                    bytes += Profiler.GetRuntimeMemorySizeLong(t);
                    groessen.Add($"{t.width}×{t.height}");
                    formate.Add(t.format.ToString());
                }
            return $"Quelle {(TexDir == TexDirPaket ? "Paket" : "prozedural")}: {n} Texturen, {bytes / 1048576.0:0} MB im Editor " +
                   $"({string.Join(", ", groessen)}; {string.Join(", ", formate)})";
        }

        private static UnityEngine.Material KV4Material(string orig, Color greybox, string set, float tile)
        {
            string p = $"{MatDir}/{KV4MatPrefix}{orig}.mat";
            UnityEngine.Material basis = LitMat(set, tile);
            var m = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(p);
            if (m == null) { m = new UnityEngine.Material(basis); AssetDatabase.CreateAsset(m, p); }
            else m.CopyPropertiesFromMaterial(basis);
            m.name = KV4MatPrefix + orig;
            // Tönung in linearem Raum: Ziel = Greybox-Farbe, Ausgang = Set-Mittel; begrenzt, damit die Struktur erhalten bleibt
            Color mean = SetMittel.TryGetValue(set, out Color mc) ? mc : Color.gray;
            Color f = new Color(Mathf.Clamp(greybox.linear.r / Mathf.Max(0.01f, mean.linear.r), 0.35f, 2.2f),
                                Mathf.Clamp(greybox.linear.g / Mathf.Max(0.01f, mean.linear.g), 0.35f, 2.2f),
                                Mathf.Clamp(greybox.linear.b / Mathf.Max(0.01f, mean.linear.b), 0.35f, 2.2f), 1f);
            m.SetColor("_BaseColor", new Color(Mathf.LinearToGammaSpace(f.r), Mathf.LinearToGammaSpace(f.g), Mathf.LinearToGammaSpace(f.b), 1f));
            EditorUtility.SetDirty(m);
            return m;
        }

        private static float Q(float v) => Mathf.Max(0.05f, Mathf.Round(v / 0.05f) * 0.05f);

        /// <summary>Einheitsquader (±0,5) mit UVs in Metern für die gegebene Skalierung.
        /// Mauer: V senkrecht, oben V entlang z. Holz: V entlang der längsten Achse jeder Fläche. Dach: oben V entlang der Gefälleachse.</summary>
        private static Mesh KV4Box(Vector3 s, Art art, Transform t, Dictionary<string, Mesh> cache)
        {
            s = new Vector3(Q(s.x), Q(s.y), Q(s.z));
            bool slopeZ = true;
            if (art == Art.Dach)
            {
                Vector3 e = t.eulerAngles; float ex = Mathf.Abs(Mathf.DeltaAngle(0, e.x)), ez = Mathf.Abs(Mathf.DeltaAngle(0, e.z));
                slopeZ = ex >= ez;
            }
            string key = $"KV4Box_{art}_{(slopeZ ? "z" : "x")}_{s.x:0.00}_{s.y:0.00}_{s.z:0.00}".Replace(',', '.');
            if (cache.TryGetValue(key, out Mesh cm)) return cm;
            string p = $"{KV4MeshDir}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (mesh != null) { cache[key] = mesh; return mesh; }
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            void Face(Vector3 nrm, Vector3 uAx, Vector3 vAx, float uLen, float vLen)
            {
                // Holz: V auf die längere Kante legen (Faser längs)
                if (art == Art.Holz && uLen > vLen) { var ta = uAx; uAx = vAx; vAx = -ta; float tl = uLen; uLen = vLen; vLen = tl; }
                int b = v.Count;
                Vector3 c = nrm * 0.5f, du = uAx * 0.5f, dv = vAx * 0.5f;
                v.Add(c - du - dv); v.Add(c + du - dv); v.Add(c + du + dv); v.Add(c - du + dv);
                for (int k = 0; k < 4; k++) n.Add(nrm);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(uLen, 0)); uv.Add(new Vector2(uLen, vLen)); uv.Add(new Vector2(0, vLen));
                tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            Face(Vector3.forward, Vector3.left, Vector3.up, s.x, s.y); Face(Vector3.back, Vector3.right, Vector3.up, s.x, s.y);
            Face(Vector3.right, Vector3.forward, Vector3.up, s.z, s.y); Face(Vector3.left, Vector3.back, Vector3.up, s.z, s.y);
            if (art == Art.Dach && !slopeZ) { Face(Vector3.up, Vector3.forward, Vector3.left, s.z, s.x); Face(Vector3.down, Vector3.forward, Vector3.right, s.z, s.x); }
            else { Face(Vector3.up, Vector3.right, Vector3.forward, s.x, s.z); Face(Vector3.down, Vector3.right, Vector3.back, s.x, s.z); }
            mesh = new Mesh { name = key };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            var ts = mesh.triangles; var vs = mesh.vertices; var ns = mesh.normals;
            for (int k = 0; k < ts.Length; k += 3)
            {
                Vector3 fn = Vector3.Cross(vs[ts[k + 1]] - vs[ts[k]], vs[ts[k + 2]] - vs[ts[k]]);
                if (Vector3.Dot(fn, ns[ts[k]]) < 0) { int tmp = ts[k + 1]; ts[k + 1] = ts[k + 2]; ts[k + 2] = tmp; }
            }
            mesh.triangles = ts; mesh.RecalculateTangents(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, p);
            cache[key] = mesh;
            return mesh;
        }

        /// <summary>Zylinder wie Unitys Primitiv (Radius 0,5, Höhe 2), Mantel-UV in Metern (U Umfang, V Länge = Faser), Deckel planar.</summary>
        private static Mesh KV4Zylinder(Vector3 s, Dictionary<string, Mesh> cache)
        {
            s = new Vector3(Q(s.x), Q(s.y), Q(s.z));
            string key = $"KV4Zyl_{s.x:0.00}_{s.y:0.00}_{s.z:0.00}".Replace(',', '.');
            if (cache.TryGetValue(key, out Mesh cm)) return cm;
            string p = $"{KV4MeshDir}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (mesh != null) { cache[key] = mesh; return mesh; }
            const int seg = 16;
            float circ = Mathf.PI * (s.x + s.z) * 0.5f, len = 2f * s.y;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f; Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * 0.5f + Vector3.down); n.Add(d); uv.Add(new Vector2(i / (float)seg * circ, 0));
                v.Add(d * 0.5f + Vector3.up); n.Add(d); uv.Add(new Vector2(i / (float)seg * circ, len));
            }
            for (int i = 0; i < seg; i++) { int b = i * 2; tri.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 }); }
            foreach (float y in new[] { -1f, 1f })
            {
                int c = v.Count; v.Add(new Vector3(0, y, 0)); n.Add(new Vector3(0, y, 0)); uv.Add(Vector2.zero);
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f; Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    v.Add(d * 0.5f + new Vector3(0, y, 0)); n.Add(new Vector3(0, y, 0)); uv.Add(new Vector2(d.x * s.x, d.z * s.z));
                }
                for (int i = 0; i < seg; i++) tri.AddRange(y > 0 ? new[] { c, c + 2 + i, c + 1 + i } : new[] { c, c + 1 + i, c + 2 + i });
            }
            mesh = new Mesh { name = key };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            var ts = mesh.triangles; var vs = mesh.vertices; var ns = mesh.normals;
            for (int k = 0; k < ts.Length; k += 3)
            {
                Vector3 fn = Vector3.Cross(vs[ts[k + 1]] - vs[ts[k]], vs[ts[k + 2]] - vs[ts[k]]);
                if (Vector3.Dot(fn, ns[ts[k]] + ns[ts[k + 1]] + ns[ts[k + 2]]) < 0) { int tmp = ts[k + 1]; ts[k + 1] = ts[k + 2]; ts[k + 2] = tmp; }
            }
            mesh.triangles = ts; mesh.RecalculateTangents(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, p);
            cache[key] = mesh;
            return mesh;
        }
    }
}
