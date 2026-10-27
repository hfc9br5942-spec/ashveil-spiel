using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ashveil.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden – Texturprobe (ART-115, 11 §AO, Gebietsblatt Westweiden §8.2/8.3; Claude 12.10.2026 nachts).
    /// Baut eine eigene Probeszene (nicht Kareth, nicht Sandbox) mit den vier selbst gestalteten Probematerialien:
    /// Weidegras-Familie A–D und Lehmweg als Terrain-Layer, Weißdornhecke als Blattkarten, Trim-Sheet als Mauerstück.
    /// Licht wie Stil A (Sonne 28°, Trilight, Profil und Himmel von Stil A nur gelesen – Kareths Stil-A-Schalter bleibt unberührt).
    /// Danach Bilder aus drei Distanzen (ART-113-Abnahme) nach Logs/Ashveil/Westweiden/&lt;Parameter&gt; und Texturspeicher.
    /// Auftrag „texturprobe_ww“ (Parameter: Unterordner). Umkehrbar: alles liegt unter Art/Westweiden/Proben und Scenes/Proben.
    /// Die offene Szene wird danach wieder geöffnet; mit ungespeicherten Änderungen bricht der Auftrag ab.
    /// </summary>
    [InitializeOnLoad]
    public static class WestweidenTexturprobe
    {
        private const string Src = ProjectSetup.Root + "/Art/Westweiden/Proben";
        private const string LayerDir = Src + "/Layer";
        private const string SceneDir = ProjectSetup.Root + "/Scenes/Proben";
        private const string ScenePath = SceneDir + "/WW_Texturprobe.unity";
        private const string StilAProfile = ProjectSetup.Root + "/Art/Rendering/Ashveil_StilA_Kareth.asset";
        private const string StilASky = ProjectSetup.Root + "/Art/Rendering/Sky_StilA_Kareth.mat";
        private const string StilAFog = ProjectSetup.Root + "/Art/Rendering/M_AtmosphereFog.mat";
        private const float Size = 1024f, MaxH = 60f;

        static WestweidenTexturprobe()
        {
            Auftragsordner.Registrieren("texturprobe_ww", (a, fertig) =>
            {
                try { string m = Run(string.IsNullOrWhiteSpace(a.parameter) ? null : a.parameter.Trim(), out bool ok); fertig(ok, m); }
                catch (Exception e) { fertig(false, "Ausnahme: " + e.GetType().Name + " " + e.Message); }
            }, true, 1800);
        }

        [MenuItem("Ashveil/Welt/Westweiden: Texturprobe bauen und Bilder", priority = 590)]
        private static void Menu() => Debug.Log("[Ashveil] " + Run(null, out _));

        public static string Run(string sub, out bool ok)
        {
            ok = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Play läuft – nichts gebaut";
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return "offene Szene hat ungespeicherte Änderungen – nichts gebaut";
            string vorher = SceneManager.GetActiveScene().path;
            var log = new StringBuilder();
            try
            {
                if (!Texturen(log)) return log.ToString();
                TerrainLayer[] layers = Layers(log);
                if (layers == null) return log.ToString();
                ProjectSetup.EnsureFolder(SceneDir);
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Terrain terrain = BuildTerrain(layers);
                Licht();
                var hecke = Hecke(terrain);
                var mauer = Mauer(terrain);
                EditorSceneManager.SaveScene(scene, ScenePath);

                string dir = Ashveil.Core.ProjectPaths.Logs("Westweiden");
                dir = Path.Combine(dir, sub ?? ("Texturprobe_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm")));
                KarethV4Shots.RenderShots(Shots(terrain, hecke, mauer), dir, "WW_");
                log.Append("Bilder in ").Append(dir).Append(" · ").Append(Speicher());
                ok = true;
                return log.ToString();
            }
            finally
            {
                if (!string.IsNullOrEmpty(vorher) && File.Exists(vorher)) EditorSceneManager.OpenScene(vorher, OpenSceneMode.Single);
            }
        }

        // ------------------------------------------------------------------------------------------------ Texturen
        internal static bool Texturen(StringBuilder log)
        {
            // Probe 3: von außen geänderte PNGs zuerst einlesen (ohne Refresh rechnete Probe 3 mit den alten Texturen)
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!AssetDatabase.IsValidFolder(Src)) { log.Append("Ordner ").Append(Src).Append(" fehlt"); return false; }
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Src }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp == null || p.Contains("Wolkenschatten")) continue;   // Licht-Kachel (Cookie) richtet WestweidenEntwurf.Westwind ein
                bool normal = p.EndsWith("_Normal.png"), lin = p.EndsWith("_Mask.png") || p.EndsWith("_Height.png") || p.EndsWith("_Wellen.png");   // Wellen: Daten, linear
                bool alpha = p.Contains("Weissdorn") && p.EndsWith("_Albedo.png");
                TextureImporterType typ = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                bool aendern = imp.textureType != typ || imp.sRGBTexture == (normal || lin) || imp.maxTextureSize != 2048 || imp.anisoLevel != 8
                               || imp.alphaIsTransparency != alpha;
                if (!aendern) continue;
                imp.textureType = typ;
                imp.sRGBTexture = !(normal || lin);
                imp.maxTextureSize = 2048;
                imp.mipmapEnabled = true;
                imp.anisoLevel = 8;
                imp.wrapMode = alpha ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                imp.alphaIsTransparency = alpha;
                imp.mipMapsPreserveCoverage = alpha;
                imp.alphaTestReferenceValue = 0.4f;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
                n++;
            }
            log.Append(n).Append(" Texturen eingestellt · ");
            return true;
        }

        private static T Tex<T>(string name) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(Src + "/" + name + ".png");

        private static TerrainLayer[] Layers(StringBuilder log)
        {
            ProjectSetup.EnsureFolder(LayerDir);
            var list = new List<TerrainLayer>();
            foreach (var (name, tile) in new[] { ("WW_Weidegras_A", 6f), ("WW_Weidegras_B", 6f), ("WW_Weidegras_C", 6f), ("WW_Weidegras_D", 6f), ("WW_Lehmweg", 4f) })
            {
                var alb = Tex<Texture2D>(name + "_Albedo");
                if (alb == null) { log.Append(name).Append(" fehlt"); return null; }
                string path = LayerDir + "/" + name + ".terrainlayer";
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (l == null) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, path); }
                l.diffuseTexture = alb;
                l.normalMapTexture = Tex<Texture2D>(name + "_Normal");
                l.maskMapTexture = Tex<Texture2D>(name + "_Mask");
                l.tileSize = new Vector2(tile, tile);
                l.normalScale = 1f;
                // URP-Terrain liest die Maske als R Metallic, G AO, B Höhe (Höhenmischung), A Glätte – B ist bei den Terrain-Proben die Höhe
                l.maskMapRemapMin = Vector4.zero;
                l.maskMapRemapMax = Vector4.one;
                EditorUtility.SetDirty(l);
                list.Add(l);
            }
            AssetDatabase.SaveAssets();
            return list.ToArray();
        }

        // ------------------------------------------------------------------------------------------------ Gelände
        private static float Fbm(float x, float z, int oct, float baseF)
        {
            float a = 0.5f, f = baseF, s = 0f, t = 0f;
            for (int i = 0; i < oct; i++) { s += a * Mathf.PerlinNoise(x * f + 31.7f * i, z * f + 17.3f * i); t += a; a *= 0.5f; f *= 2f; }
            return s / t;
        }

        private static float WegX(float z) => Size * 0.5f + 1.2f * Mathf.Sin(z / 37f) + 8f * Mathf.Sin(z / 160f);

        private static Terrain BuildTerrain(TerrainLayer[] layers)
        {
            string tdPath = SceneDir + "/WW_Texturprobe_Gelaende.asset";
            var td = AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath);
            if (td == null) { td = new TerrainData(); AssetDatabase.CreateAsset(td, tdPath); }
            td.heightmapResolution = 513;
            td.size = new Vector3(Size, MaxH, Size);
            int hr = td.heightmapResolution;
            var h = new float[hr, hr];
            for (int y = 0; y < hr; y++)
                for (int x = 0; x < hr; x++)
                {
                    float wx = x / (float)(hr - 1) * Size, wz = y / (float)(hr - 1) * Size;
                    // sanfte Weiderücken (Westweiden: Grasrücken, Höhen 20–180 m im Original; hier nur Probe ±20 m)
                    float v = Fbm(wx, wz, 5, 1f / 260f) * 0.55f;
                    float weg = Mathf.Abs(wx - WegX(wz));
                    v = Mathf.Lerp(v, Fbm(WegX(wz), wz, 5, 1f / 260f) * 0.55f, Mathf.Clamp01(1f - (weg - 3f) / 10f) * 0.8f);   // Weg folgt dem Gelände ruhig
                    h[y, x] = v;
                }
            td.SetHeights(0, 0, h);
            td.alphamapResolution = 1024;
            td.terrainLayers = layers;
            int ar = td.alphamapResolution;
            var a = new float[ar, ar, layers.Length];
            for (int y = 0; y < ar; y++)
                for (int x = 0; x < ar; x++)
                {
                    float wx = (x + 0.5f) / ar * Size, wz = (y + 0.5f) / ar * Size;
                    float macro = Fbm(wx + 500f, wz, 4, 1f / 70f), region = Fbm(wx, wz + 900f, 3, 1f / 260f), wet = Fbm(wx + 1300f, wz + 200f, 3, 1f / 120f);
                    float hn = td.GetInterpolatedHeight(wx / Size, wz / Size) / MaxH;
                    float wA = Mathf.Clamp01(1.2f - Mathf.Abs(macro - 0.42f) * 5f);
                    float wB = Mathf.Clamp01(1.2f - Mathf.Abs(macro - 0.58f) * 5f) * (0.6f + region);
                    float wC = Mathf.SmoothStep(0f, 1f, (hn - 0.30f) / 0.12f) * Mathf.SmoothStep(0f, 1f, (region - 0.45f) / 0.15f);   // trocken auf den Rücken
                    float wD = Mathf.SmoothStep(0f, 1f, (0.24f - hn) / 0.06f) * Mathf.SmoothStep(0f, 1f, (wet - 0.5f) / 0.1f) * 1.5f;  // feucht in Senken
                    float d = Mathf.Abs(wx - WegX(wz));
                    float wW = Mathf.Clamp01((2.3f - d) / 0.7f);
                    float[] w = { wA, wB, wC, wD };
                    float sum = 1e-4f;
                    for (int i = 0; i < 4; i++) { w[i] = Mathf.Pow(w[i] + 0.02f, 3f); sum += w[i]; }
                    for (int i = 0; i < 4; i++) a[y, x, i] = w[i] / sum * (1f - wW);
                    a[y, x, 4] = wW;
                }
            td.SetAlphamaps(0, 0, a);
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();

            GameObject go = Terrain.CreateTerrainGameObject(td);
            go.name = "Westweiden_Probe_Gelaende";
            var t = go.GetComponent<Terrain>();
            // Probe 2: projekteigener Terrain-Shader mit Hex-Versatz und Makro (TECH-114-Weg) statt URP-Standard – gegen das Kachelraster in der Ferne
            var shader = Shader.Find("Ashveil/Terrain/Lit (Kachelfrei)") ?? Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader != null)
            {
                string mp = Src + "/WW_Terrain_Probe.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, mp); }
                mat.shader = shader;
                mat.SetFloat("_AshKachelfrei", 1f);
                mat.SetFloat("_AshZellen", 0.4f);
                mat.SetFloat("_AshKontrast", 4f);
                mat.SetFloat("_AshMakroStaerke", 0.12f);
                mat.SetFloat("_AshMakroMassstab", 0.015f);
                mat.SetFloat("_EnableHeightBlend", 1f);
                mat.SetFloat("_HeightTransition", 0.25f);
                mat.EnableKeyword("_TERRAIN_BLEND_HEIGHT");
                mat.EnableKeyword("_MASKMAP");
                mat.EnableKeyword("_NORMALMAP");
                t.materialTemplate = mat;
            }
            t.basemapDistance = 600f;
            t.heightmapPixelError = 3f;
            t.drawInstanced = true;
            return t;
        }

        // ------------------------------------------------------------------------------------------------ Licht wie Stil A
        internal static void Licht()
        {
            var sunGo = new GameObject("Sonne (wie Stil A)");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sunGo.transform.rotation = Quaternion.Euler(28f, KarethV4Builder.SunYaw, 0f);
            sun.color = new Color(1f, 0.84f, 0.66f);
            sun.intensity = 2.0f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.54f, 0.60f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.47f, 0.48f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.27f, 0.23f, 0.19f);
            RenderSettings.fog = false;
            var sky = AssetDatabase.LoadAssetAtPath<Material>(StilASky);
            if (sky != null) RenderSettings.skybox = sky;

            var go = new GameObject("Stil A (gelesen, Probe)");
            var fogMat = AssetDatabase.LoadAssetAtPath<Material>(StilAFog);
            if (fogMat != null)
            {
                var atmo = go.AddComponent<AtmosphereController>();
                var so = new SerializedObject(atmo);
                void F(string n, float v) { var p = so.FindProperty(n); if (p != null) p.floatValue = v; }
                void C(string n, Color v) { var p = so.FindProperty(n); if (p != null) p.colorValue = v; }
                void R(string n, UnityEngine.Object v) { var p = so.FindProperty(n); if (p != null) p.objectReferenceValue = v; }
                R("fogMaterial", fogMat); R("sun", sun);
                C("fogColor", new Color(0.58f, 0.62f, 0.68f)); F("density", 0.0006f); F("heightFalloff", 0.006f); F("baseHeight", 0f);
                F("maxOpacity", 0.82f); F("startDistance", 25f); F("haze", 0.00012f); F("horizonSharpness", 5f); F("skyDistance", 2500f);
                C("sunScatter", new Color(1.05f, 0.82f, 0.58f)); F("sunPower", 8f);
                so.ApplyModifiedPropertiesWithoutUndo();
                atmo.Apply();
            }
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(StilAProfile);
            if (profile != null)
            {
                var vol = go.AddComponent<Volume>();
                vol.isGlobal = true;
                vol.priority = 5f;
                vol.sharedProfile = profile;
            }
        }

        // ------------------------------------------------------------------------------------------------ Hecke und Mauer
        internal static Material LitMat(string name, string tex, bool cutout)
        {
            string mp = Src + "/" + name + ".mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, mp); }
            m.shader = shader;
            m.SetTexture("_BaseMap", Tex<Texture2D>(tex + "_Albedo"));
            m.SetTexture("_BumpMap", Tex<Texture2D>(tex + "_Normal"));
            m.EnableKeyword("_NORMALMAP");
            var mask = Tex<Texture2D>(tex + "_Mask");
            m.SetTexture("_MetallicGlossMap", mask);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetTexture("_OcclusionMap", mask);
            m.EnableKeyword("_OCCLUSIONMAP");
            m.SetFloat("_Cull", 0f);   // Probe: beidseitig (Karten und Mauer-Ausrichtung egal)
            if (cutout)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.4f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f);
                m.doubleSidedGI = true;
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Probe 2: Hecke mit dem projekteigenen Vegetations-Shader (Durchscheinen, weiche Normalen, beidseitig).</summary>
        internal static Material FoliageMat()
        {
            var shader = Shader.Find("Ashveil/Foliage");
            if (shader == null) return null;
            string mp = Src + "/WW_Weissdorn_Foliage_Probe.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, mp); }
            m.shader = shader;
            m.SetTexture("_BaseMap", Tex<Texture2D>("WW_Weissdorn_Atlas_Albedo"));
            m.SetFloat("_AlphaClip", 1f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cutoff", 0.4f);
            m.SetFloat("_Translucency", 0.4f);
            m.SetFloat("_NormalUp", 0.7f);
            m.SetFloat("_WindStrength", 0f);   // Netz liegt in Weltkoordinaten – Wind wäre überall voll
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Mesh SaveMesh(Mesh mesh, string name)
        {
            string p = Src + "/" + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (old != null) { old.Clear(); EditorUtility.CopySerialized(mesh, old); UnityEngine.Object.DestroyImmediate(mesh); return old; }
            AssetDatabase.CreateAsset(mesh, p);
            return mesh;
        }

        /// <summary>Heckenkarten: über Kreuz gestellte Karten (1,4 × 1,8 m), linke Atlashälfte, entlang des Wegs auf der Westseite.</summary>
        private static Vector3 Hecke(Terrain t)
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var norms = new List<Vector3>(); var tris = new List<int>();
            var rnd = new System.Random(77);
            Vector3 mitte = Vector3.zero; int k = 0;
            for (float z = 380f; z < 470f; z += 0.9f)
            {
                float x = WegX(z) - 4.2f + (float)rnd.NextDouble() * 0.6f;
                float y = t.SampleHeight(new Vector3(x, 0, z));
                float hgt = 1.6f + (float)rnd.NextDouble() * 0.5f, w = 1.4f;
                for (int c = 0; c < 3; c++)
                {
                    float ang = c * 1.047f + (float)rnd.NextDouble() * 0.6f;
                    var dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * (w * 0.5f);
                    int b = verts.Count;
                    var p = new Vector3(x, y - 0.1f, z);
                    verts.Add(p - dir); verts.Add(p + dir); verts.Add(p + dir + Vector3.up * hgt); verts.Add(p - dir + Vector3.up * hgt);
                    uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0.5f, 0f)); uvs.Add(new Vector2(0.5f, 1f)); uvs.Add(new Vector2(0f, 1f));
                    var nrm = Vector3.Cross(dir, Vector3.up).normalized;
                    for (int i = 0; i < 4; i++) norms.Add((nrm + Vector3.up * 0.6f).normalized);   // weiche Blattnormalen
                    tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                }
                mitte += new Vector3(x, y, z); k++;
            }
            var mesh = new Mesh { name = "WW_Hecke_Probe", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetNormals(norms); mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            mesh = SaveMesh(mesh, "WW_Hecke_Probe_Netz");
            var go = new GameObject("Weissdornhecke (Probe)");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = FoliageMat() ?? LitMat("WW_Weissdorn_Probe", "WW_Weissdorn_Atlas", true);
            return mitte / Mathf.Max(1, k);
        }

        /// <summary>Mauerstück 8 × 2,6 m auf der Ostseite: Sockel, Bruchstein, Schwelle, Kalkputz (Bänder des Trim-Sheets).</summary>
        private static Vector3 Mauer(Terrain t)
        {
            float z0 = 420f, len = 8f, x = WegX(z0) + 6f;
            float y = t.SampleHeight(new Vector3(x, 0, z0 + len * 0.5f)) - 0.2f;
            var bands = new[] { (0.0f, 0.5f, 0.76f, 0.92f), (0.5f, 1.9f, 0.0f, 0.40f), (1.9f, 2.15f, 0.60f, 0.76f), (2.15f, 2.9f, 0.40f, 0.60f) };
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            foreach (var (h0, h1, v0, v1) in bands)
            {
                int b = verts.Count;
                float u1 = len / 2f;   // 2 m Kachelbreite
                // Unity-UV: v = 1 oben im Bild → Band (v0..v1 von oben gemessen) umrechnen
                float vb = 1f - v1, vt = 1f - v0;
                verts.Add(new Vector3(x, y + h0, z0)); verts.Add(new Vector3(x, y + h0, z0 + len));
                verts.Add(new Vector3(x, y + h1, z0 + len)); verts.Add(new Vector3(x, y + h1, z0));
                uvs.Add(new Vector2(0, vb)); uvs.Add(new Vector2(u1, vb)); uvs.Add(new Vector2(u1, vt)); uvs.Add(new Vector2(0, vt));
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });   // Vorderseite nach −x (zum Weg)
            }
            var mesh = new Mesh { name = "WW_Mauer_Probe" };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            mesh = SaveMesh(mesh, "WW_Mauer_Probe_Netz");
            var go = new GameObject("Mauerstueck (Probe)");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = LitMat("WW_Trim_Probe", "WW_Trim_Bruchstein_Kalkputz", false);
            return new Vector3(x, y + 1.4f, z0 + len * 0.5f);
        }

        // ------------------------------------------------------------------------------------------------ Bilder
        private static List<KarethV4Shots.Shot> Shots(Terrain t, Vector3 hecke, Vector3 mauer)
        {
            float H(float x, float z) => t.SampleHeight(new Vector3(x, 0, z));
            var s = new List<KarethV4Shots.Shot>();
            void Add(string name, float ex, float ez, float eh, float tx, float tz, float th, float fov = 55f)
                => s.Add(new KarethV4Shots.Shot { Name = name, Eye = new Vector3(ex, H(ex, ez) + eh, ez), Target = new Vector3(tx, H(tx, tz) + th, tz), Fov = fov });
            float wx = WegX(300f);
            Add("1_nah_Weide_1-5m", wx - 6f, 300f, 1.7f, wx - 7.5f, 303f, 0f);
            Add("1_nah_Weg_1-5m", wx, 300f, 1.7f, WegX(304f), 304f, 0f);
            Add("2_mittel_Weide_Weg_10-50m", wx - 1f, 260f, 1.8f, WegX(300f), 300f, 0.5f);
            Add("2_mittel_quer_10-50m", wx - 3f, 300f, 1.8f, wx - 40f, 330f, 0f);
            Add("3_landschaft_50-500m", wx + 20f, 120f, 30f, WegX(520f), 520f, 0f, 60f);
            Add("3_landschaft_hoch_500m+", 200f, 60f, 120f, 600f, 700f, 0f, 60f);
            // Probe 2: Augenhöhe immer über dem Gelände (Probe 1 lag bei „Hecke nah“ unter dem Boden)
            Add("4_Hecke_nah", hecke.x + 4.5f, hecke.z - 2f, 1.7f, hecke.x, hecke.z, 0.9f, 50f);
            Add("4_Hecke_mittel", hecke.x + 14f, hecke.z - 26f, 1.8f, hecke.x, hecke.z, 0.9f, 50f);
            Add("5_Mauer_nah", mauer.x - 4f, mauer.z - 1.5f, 1.7f, mauer.x, mauer.z, 1.2f, 50f);
            Add("5_Mauer_mittel", mauer.x - 16f, mauer.z - 18f, 1.8f, mauer.x, mauer.z, 1.2f, 50f);
            return s;
        }

        private static string Speicher()
        {
            long sum = 0; int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Src }))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                if (tex == null) continue;
                sum += Profiler.GetRuntimeMemorySizeLong(tex); n++;
            }
            return $"Texturspeicher Proben {n} Texturen {sum / (1024f * 1024f):0.0} MB (Editor-Wert, inkl. Mips)";
        }
    }
}
