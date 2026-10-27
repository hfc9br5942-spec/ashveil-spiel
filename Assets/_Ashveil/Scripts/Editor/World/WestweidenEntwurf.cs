using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden – Unity-Entwurf v1 (Max 12.10.2026: Topografie-Entwurf „als Arbeitsgrundlage freigegeben“).
    /// Projektregel 26 „World Design vor Objekt-Placement“: nur Gelände, Bodenfamilien, Wege und Greybox-Marken für Orte und
    /// Ausgänge – keine Bäume, kein Gras, keine Gebäude. Daten aus dem Topografie-Generator (`Tools/claude/topo/export_unity.py`):
    /// Höhe 1025² (−50 … +550 m, Terrain 4200 × 600 × 3400 m), Bodenmischung 2048² × 5 (gzip), Orte und Wege als CSV.
    /// Lagen schematisch, gelten bis zum Übertragungsplan (C5). Eigene Szene `Scenes/Westweiden/Westweiden_Entwurf.unity`;
    /// Kareth, Sandbox und Spielszenen bleiben unberührt. Auftrag „westweiden_entwurf“ (Parameter: Unterordner der Bilder).
    /// </summary>
    [InitializeOnLoad]
    public static class WestweidenEntwurf
    {
        private const string DataDir = ProjectSetup.Root + "/Data/Westweiden/Entwurf";
        private const string SceneDir = ProjectSetup.Root + "/Scenes/Westweiden";
        private const string ScenePath = SceneDir + "/Westweiden_Entwurf.unity";
        private const string LayerDir = ProjectSetup.Root + "/Art/Westweiden/Proben/Layer";
        private const string TerrainMat = ProjectSetup.Root + "/Art/Westweiden/Proben/WW_Terrain_Probe.mat";
        private const float SX = 4200f, SY = 600f, SZ = 3400f, Y0 = -50f;

        static WestweidenEntwurf()
        {
            Auftragsordner.Registrieren("westweiden_entwurf", (a, fertig) =>
            {
                try { string m = Run(string.IsNullOrWhiteSpace(a.parameter) ? null : a.parameter.Trim(), out bool ok); fertig(ok, m); }
                catch (Exception e) { fertig(false, "Ausnahme: " + e.GetType().Name + " " + e.Message); }
            }, true, 1800);
        }

        [MenuItem("Ashveil/Welt/Westweiden: Entwurf bauen und Bilder", priority = 591)]
        private static void Menu() => Debug.Log("[Ashveil] " + Run(null, out _));

        private static string Abs(string assetPath) => Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);

        public static string Run(string sub, out bool ok)
        {
            ok = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Play läuft – nichts gebaut";
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) return "offene Szene hat ungespeicherte Änderungen – nichts gebaut";
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            TerrainLayer[] layerListe = Layer(out string layerFehler);
            if (layerListe == null) return layerFehler;
            var layers = new List<TerrainLayer>(layerListe);
            string vorher = SceneManager.GetActiveScene().path;
            var log = new StringBuilder();
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ProjectSetup.EnsureFolder(SceneDir);
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Terrain t = Gelaende(layers.ToArray(), log);
                WestweidenTexturprobe.Licht();
                Westwind();                                        // 12.10.: Westwind-Stimmung (Prüfung F6)
                var orte = Orte(t, log);
                log.Append(WestweidenOrte.Bauen(t, orte, Farbe)).Append(" Baukörper · ");
                Wegmarken(t, log);
                log.Append(WestweidenWasser.Bauen(t, SX, SZ));   // 12.10.: echtes Wasser statt Greybox-Meer
                log.Append(WestweidenHecken.Bauen(t));            // 12.10.: Flur – Weißdornhecken
                log.Append(WestweidenFuelle.Bauen(t, Farbe));     // 12.10.: Füllplan v1 (Fundorte, Findlinge, Kopfweiden, Herden)
                log.Append(WestweidenKlang.Bauen(t, SX, SZ));     // 14.10.: Klangquellen (Bach, Brandung, Mühlen, Lerchen/Grillen)
                log.Append(WestweidenKreide.Bauen(t, orte, SceneDir + "/Westweiden_Entwurf_Kreidefelsen.asset"));   // 14.10.: Kreidefelsen an steilen Hängen (Vorschlag)
                log.Append(WestweidenEichen.Bauen(t));            // 13.10.: Eichen als Überhälter in den Hecken (Vorschlag)
                log.Append(WestweidenBewuchs.Bauen(t));           // 13.10.: Bodenbewuchs (Gras, Korn) wie in der Zelle
                log.Append(WegPruefung(t, Path.Combine(Ashveil.Core.ProjectPaths.Logs("Westweiden"), sub ?? "Entwurf")));   // Max 12.10.: nichts auf Wegen
                EditorSceneManager.SaveScene(scene, ScenePath);
                string dir = Path.Combine(Ashveil.Core.ProjectPaths.Logs("Westweiden"), sub ?? ("Entwurf_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm")));
                KarethV4Shots.RenderShots(Shots(t, orte), dir, "WWE_");
                log.Append("Szene ").Append(ScenePath).Append(" · Bilder in ").Append(dir);
                ok = true;
                return log.ToString();
            }
            finally
            {
                if (!string.IsNullOrEmpty(vorher) && File.Exists(vorher)) EditorSceneManager.OpenScene(vorher, OpenSceneMode.Single);
            }
        }

        /// <summary>Die sieben Bodenfamilien (A, B, C, D, Kreidefels, Weg, Acker) – null mit Meldung, wenn eine fehlt.</summary>
        internal static TerrainLayer[] Layer(out string fehler)
        {
            fehler = null;
            var layers = new List<TerrainLayer>();
            foreach (string n in new[] { "WW_Weidegras_A", "WW_Weidegras_B", "WW_Weidegras_C", "WW_Weidegras_D", "WW_Kreidefels", "WW_Lehmweg", "WW_Acker_Korn" })   // 12.10.: Acker (Flur)
            {
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerDir + "/" + n + ".terrainlayer") ?? NeuerLayer(n);
                if (l == null) { fehler = "Terrain-Layer " + n + " fehlt – erst Auftrag „texturprobe_ww“ bzw. Texturen nach Art/Westweiden/Proben"; return null; }
                layers.Add(l);
            }
            return layers.ToArray();
        }

        /// <summary>Höhenkarte der Westweiden (normiert, [z, x], 1025²) aus den Entwurfsdaten.</summary>
        internal static float[,] HoehenLesen()
        {
            byte[] hb = File.ReadAllBytes(Abs(DataDir + "/WW_Hoehe_1025_r16.bytes"));
            const int HR = 1025;
            var h = new float[HR, HR];
            for (int z = 0; z < HR; z++)
                for (int x = 0; x < HR; x++)
                {
                    int i = (z * HR + x) * 2;
                    h[z, x] = (hb[i] | (hb[i + 1] << 8)) / 65535f;
                }
            return h;
        }

        internal const string HoeheDatei = DataDir + "/WW_Hoehe_1025_r16.bytes";

        /// <summary>Kreidefels (v2) und spätere Familien: Layer anlegen, wenn nur die Texturen da sind (Import wie bei der Texturprobe).</summary>
        private static TerrainLayer NeuerLayer(string n)
        {
            string src = ProjectSetup.Root + "/Art/Westweiden/Proben/" + n;
            var alb = AssetDatabase.LoadAssetAtPath<Texture2D>(src + "_Albedo.png");
            if (alb == null) return null;
            foreach (string suf in new[] { "_Albedo", "_Normal", "_Mask" })
            {
                var imp = AssetImporter.GetAtPath(src + suf + ".png") as TextureImporter;
                if (imp == null) continue;
                imp.textureType = suf == "_Normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.sRGBTexture = suf == "_Albedo";
                imp.maxTextureSize = 2048; imp.anisoLevel = 8; imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
            ProjectSetup.EnsureFolder(LayerDir);
            var l = new TerrainLayer
            {
                diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(src + "_Albedo.png"),
                normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(src + "_Normal.png"),
                maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(src + "_Mask.png"),
                tileSize = n.Contains("Acker") ? new Vector2(6f, 6f) : new Vector2(10f, 10f),   // Acker: Drillreihen 0,15 m
                normalScale = 1f
            };
            AssetDatabase.CreateAsset(l, LayerDir + "/" + n + ".terrainlayer");
            return l;
        }

        /// <summary>Gelände bauen. Die Spielzelle (<see cref="WestweidenZelle"/>) übergibt eigene Höhen (Naht zu Kareth) und ein
        /// eigenes TerrainData, damit der Entwurf unverändert bleibt.</summary>
        internal static Terrain Gelaende(TerrainLayer[] layers, StringBuilder log, float[,] hoehe = null, string tdPath = null)
        {
            const int HR = 1025;
            float[,] h = hoehe ?? HoehenLesen();
            tdPath = tdPath ?? SceneDir + "/Westweiden_Entwurf_Gelaende.asset";
            var td = AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath);
            if (td == null) { td = new TerrainData(); AssetDatabase.CreateAsset(td, tdPath); }
            td.heightmapResolution = HR;
            td.size = new Vector3(SX, SY, SZ);
            td.SetHeights(0, 0, h);
            td.terrainLayers = layers;
            const int AR = 2048;
            int L = layers.Length;   // 7: A, B, C, D, Kreidefels, Weg, Acker
            td.alphamapResolution = AR;
            byte[] sb;
            using (var fs = File.OpenRead(Abs(DataDir + "/WW_Splat_2048_gz.bytes")))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var ms = new MemoryStream()) { gz.CopyTo(ms); sb = ms.ToArray(); }
            if (sb.Length != AR * AR * L) { log.Append("Bodenmischung hat falsche Größe ").Append(sb.Length).Append(" · "); }
            var a = new float[AR, AR, L];
            for (int z = 0; z < AR; z++)
                for (int x = 0; x < AR; x++)
                {
                    int i = (z * AR + x) * L;
                    float s = 0f;
                    for (int k = 0; k < L; k++) { a[z, x, k] = sb[i + k] / 255f; s += a[z, x, k]; }
                    if (s > 0f) for (int k = 0; k < L; k++) a[z, x, k] /= s;
                    else a[z, x, 1] = 1f;
                }
            td.SetAlphamaps(0, 0, a);
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
            GameObject go = Terrain.CreateTerrainGameObject(td);
            go.name = "Westweiden_Gelaende (Entwurf v1)";
            go.transform.position = new Vector3(0f, Y0, 0f);
            var t = go.GetComponent<Terrain>();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainMat);
            if (mat != null) t.materialTemplate = mat;
            t.basemapDistance = 800f;
            t.heightmapPixelError = 4f;
            t.drawInstanced = true;
            log.Append("Gelände 4,2 × 3,4 km · ");
            return t;
        }

        internal static Material Farbe(string name, Color c)
        {
            string p = ProjectSetup.Root + "/Art/Westweiden/Proben/WW_Greybox_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static GameObject Block(Transform parent, string name, PrimitiveType typ, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(typ);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());   // Greybox: nur Bild, keine Kollision
            return g;
        }

        internal static float Hoehe(Terrain t, float x, float z) => t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;

        /// <summary>Greybox-Marken: Höfe/Orte als Blöcke, Windmühlen als drei Türme, Gefahrenorte rot, Ausgänge blaue Säulen.</summary>
        internal static Dictionary<string, Vector3> Orte(Terrain t, StringBuilder log, bool spiel = false)
        {
            var root = new GameObject("Greybox Orte (Entwurf)").transform;
            Material grau = Farbe("Ort", new Color(0.72f, 0.70f, 0.64f)), rot = Farbe("Gefahr", new Color(0.70f, 0.25f, 0.20f)),
                     blau = Farbe("Ausgang", new Color(0.25f, 0.42f, 0.70f)), holz = Farbe("Muehle", new Color(0.55f, 0.45f, 0.32f));
            var d = new Dictionary<string, Vector3>();
            foreach (string line in File.ReadAllLines(Abs(DataDir + "/WW_Orte.csv"), Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 5) continue;
                float x = float.Parse(f[1], CultureInfo.InvariantCulture), z = float.Parse(f[2], CultureInfo.InvariantCulture);
                int st = int.Parse(f[3], CultureInfo.InvariantCulture);
                x = Mathf.Clamp(x, 5f, SX - 5f); z = Mathf.Clamp(z, 5f, SZ - 5f);
                float y = Hoehe(t, x, z);
                d[f[0]] = new Vector3(x, y, z);
                if (f[4] == "ausgang" && spiel) continue;   // Spielzelle: keine Entwurfssäulen an den Ausgängen (CORE-009)
                if (f[4] == "ausgang") { Block(root, "Ausgang " + f[0], PrimitiveType.Cylinder, new Vector3(x, y + 15f, z), new Vector3(4f, 15f, 4f), blau); continue; }
                if (f[0].Contains("Windmühlen"))
                {
                    for (int k = 0; k < 3; k++)
                    {
                        float mx = x - 60f + k * 60f, mz = z + (k == 1 ? 25f : 0f), my = Hoehe(t, mx, mz);
                        // Prüfung 12.10. (F1): Mühlen als Hero-Landmarke – vom Kammsattel (1,6 km) als Silhouette lesbar: Turm 24 m, Flügel 30 m (stilisiert über echtem Maß)
                        // 14.10.2026 (Lückenliste Punkt 6): Modell mit Turm, Haube, drehenden Gitterflügeln und Steert; Flügel nach Westen (Westwind)
                        if (WindmuehleModell.Bauen(root, "Windmuehle " + (k + 1), new Vector3(mx, my, mz), -90f, k, holz) != null) continue;
                        Block(root, "Windmuehle " + (k + 1), PrimitiveType.Cylinder, new Vector3(mx, my + 12f, mz), new Vector3(9f, 12f, 9f), holz);
                        Block(root, "Haube " + (k + 1), PrimitiveType.Cube, new Vector3(mx, my + 25f, mz), new Vector3(8f, 3.5f, 9f), holz);
                        foreach (float wink in new[] { 30f + k * 20f, 120f + k * 20f })
                            Block(root, "Fluegel " + (k + 1), PrimitiveType.Cube, new Vector3(mx, my + 25f, mz - 5.5f), new Vector3(1.6f, 30f, 0.4f), holz)
                                .transform.rotation = Quaternion.Euler(0f, 0f, wink);
                    }
                    continue;
                }
                if (Ausgeformt.Contains(f[0])) continue;   // ausgeformt in WestweidenOrte
                if (spiel) continue;                       // Spielzelle: keine Entwurfsblöcke für noch nicht ausgeformte Orte (Lauf op_454: Figur stand im Kammsattel-Block)
                bool gefahr = st >= 14 || f[0].Contains("Zollhütte") || f[0].Contains("Steinriegel") || f[0].Contains("Kreidekeller");
                bool gross = f[0].Contains("Langer Rain") || f[0].Contains("hof") || f[0].Contains("Hallgrund");
                Vector3 s = gross ? new Vector3(40f, 8f, 28f) : new Vector3(10f, 5f, 10f);
                Block(root, "Ort " + f[0], PrimitiveType.Cube, new Vector3(x, y + s.y * 0.5f - 0.5f, z), s, gefahr ? rot : grau);
            }
            log.Append(d.Count).Append(" Orte/Ausgänge · ");
            return d;
        }

        /// <summary>Wegstangen alle 120 m entlang der Wege (Lesbarkeit im Entwurf; der Weg selbst ist die Bodenschicht).</summary>
        private static void Wegmarken(Terrain t, StringBuilder log)
        {
            var root = new GameObject("Greybox Wegmarken (Entwurf)").transform;
            Material m = Farbe("Weg", new Color(0.85f, 0.80f, 0.62f));
            var wege = new Dictionary<string, List<Vector2>>();
            foreach (string line in File.ReadAllLines(Abs(DataDir + "/WW_Wege.csv"), Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 3) continue;
                if (!wege.TryGetValue(f[0], out var l)) wege[f[0]] = l = new List<Vector2>();
                l.Add(new Vector2(float.Parse(f[1], CultureInfo.InvariantCulture), float.Parse(f[2], CultureInfo.InvariantCulture)));
            }
            int n = 0;
            foreach (var kv in wege)
            {
                float rest = 0f;
                for (int i = 1; i < kv.Value.Count; i++)
                {
                    Vector2 p0 = kv.Value[i - 1], p1 = kv.Value[i];
                    float len = Vector2.Distance(p0, p1);
                    for (float s = rest; s < len; s += 120f)
                    {
                        Vector2 p = Vector2.Lerp(p0, p1, s / len);
                        float x = Mathf.Clamp(p.x, 2f, SX - 2f), z = Mathf.Clamp(p.y, 2f, SZ - 2f);
                        Block(root, kv.Key, PrimitiveType.Cylinder, new Vector3(x + 3f, Hoehe(t, x + 3f, z) + 1.5f, z), new Vector3(0.3f, 1.5f, 0.3f), m);
                        n++;
                    }
                    rest = (rest - len) % 120f; if (rest < 0f) rest += 120f;
                }
            }
            log.Append(n).Append(" Wegstangen · ");
        }

        /// <summary>Westwind-Stimmung der Westweiden (Claude 12.10.2026 nach Edlin-Prüfung F6, Max: „das bestmögliche“; VORSCHLAG):
        /// Prüf- und Heldenstunde später Nachmittag (Sonne im Westen über dem Steilrand, 22° hoch → Streiflicht auf Mühlen und Hecken),
        /// silbriger Meeresdunst (dichter in Bodennähe), ziehende Wolkenschatten. Stil A bleibt Grundlage (gleiche Bausteine, andere Werte);
        /// im Spiel läuft der Tageslauf weiter – dauerhaft regionstypisch sind Dunst und Wolken.</summary>
        private static void Westwind()
        {
            Light sun = RenderSettings.sun;
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(22f, 78f, 0f);      // Licht fällt von West-Süd-West nach Osten
                sun.color = new Color(1f, 0.80f, 0.60f);
                sun.intensity = 2.1f;
                var cookie = AssetDatabase.LoadAssetAtPath<Texture2D>(ProjectSetup.Root + "/Art/Westweiden/Proben/WW_Wolkenschatten.png");
                if (cookie != null)
                {
                    var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(cookie)) as TextureImporter;
                    if (imp != null && imp.textureType != TextureImporterType.Cookie)
                    {
                        imp.textureType = TextureImporterType.Cookie;
                        imp.wrapMode = TextureWrapMode.Repeat;
                        imp.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                        imp.mipmapEnabled = true;
                        imp.SaveAndReimport();
                        cookie = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(cookie));
                    }
                    sun.cookie = cookie;
                    var ws = sun.gameObject.AddComponent<Ashveil.Rendering.WolkenSchatten>();
                    ws.kachel = 3000f;
                    var data = sun.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>()
                               ?? sun.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                    data.lightCookieSize = new Vector2(3000f, 3000f);
                    data.lightCookieOffset = new Vector2(400f, 900f);              // Prüfbilder: feste Lage (Wolke nicht über jedem Ort)
                }
            }
            var atmo = UnityEngine.Object.FindFirstObjectByType<Ashveil.Rendering.AtmosphereController>();
            if (atmo != null)
            {
                var so = new SerializedObject(atmo);
                void F(string n, float v) { var p = so.FindProperty(n); if (p != null) p.floatValue = v; }
                void C(string n, Color v) { var p = so.FindProperty(n); if (p != null) p.colorValue = v; }
                C("fogColor", new Color(0.68f, 0.74f, 0.80f));                     // silbriger Meeresdunst
                F("density", 0.0009f); F("heightFalloff", 0.010f); F("baseHeight", 0f);
                F("haze", 0.00022f); F("maxOpacity", 0.8f);
                C("sunScatter", new Color(1.10f, 0.86f, 0.62f)); F("sunPower", 6f);
                so.ApplyModifiedPropertiesWithoutUndo();
                atmo.Apply();
            }
        }

        /// <summary>Weg-Prüfung (Max 12.10.: „Zäune, die über Wege gehen“): tastet jeden Weg alle 2 m ab und meldet alles, was dort steht –
        /// Kollider (Häuser, Hecken, Steine, Bäume) über eine Kugelabfrage in Hüfthöhe, Zaunteile über ihre Lage. Brücken zählen nicht.
        /// Ergebnis im Auftragsprotokoll und als weg_pruefung.txt neben den Bildern.</summary>
        internal static string WegPruefung(Terrain t, string dir)
        {
            Physics.SyncTransforms();
            var funde = new Dictionary<string, int>();
            var zaun = new List<Transform>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.name == "Pfosten" || r.name == "Latte" || r.name == "Heckenstück") zaun.Add(r.transform);
            var wege = new Dictionary<string, List<Vector2>>();
            foreach (string line in File.ReadAllLines(Abs(DataDir + "/WW_Wege.csv"), Encoding.UTF8))
            {
                string[] f = line.Split(';');
                if (f.Length < 3) continue;
                if (!wege.TryGetValue(f[0], out var l)) wege[f[0]] = l = new List<Vector2>();
                l.Add(new Vector2(float.Parse(f[1], CultureInfo.InvariantCulture), float.Parse(f[2], CultureInfo.InvariantCulture)));
            }
            var treffer = new List<string>();
            foreach (var kv in wege)
            {
                float halb = kv.Key.StartsWith("Hofstraße") || kv.Key.StartsWith("Vorspannrampe") ? 1.5f : 0.8f;
                for (int i = 1; i < kv.Value.Count; i++)
                {
                    Vector2 a = kv.Value[i - 1], b = kv.Value[i];
                    float len = Vector2.Distance(a, b);
                    for (float s = 0f; s < len; s += 2f)
                    {
                        Vector2 p = Vector2.Lerp(a, b, s / len);
                        float y = Hoehe(t, p.x, p.y);
                        foreach (var col in Physics.OverlapSphere(new Vector3(p.x, y + 1.0f, p.y), halb))
                        {
                            if (col is TerrainCollider) continue;
                            string n = col.transform.parent != null ? col.transform.parent.name + "/" + col.name : col.name;
                            if (n.Contains("Brücke") || n.Contains("Deck") || n.Contains("Brunnen")) continue;   // Brunnen steht bewusst auf dem Platz
                            string key = kv.Key + ": " + n;
                            if (!funde.ContainsKey(key)) { funde[key] = 0; treffer.Add($"{key} bei ({p.x:0}/{p.y:0})"); }
                            funde[key]++;
                        }
                    }
                }
            }
            int zaunAufWeg = 0;
            foreach (var z in zaun)
                if (WestweidenOrte.WegAbstand(z.position.x, z.position.z) < 0.2f) { zaunAufWeg++; if (zaunAufWeg <= 20) treffer.Add($"Zaun/Hecke auf Weg: {z.parent?.name}/{z.name} bei ({z.position.x:0}/{z.position.z:0})"); }
            try { Directory.CreateDirectory(dir); File.WriteAllLines(Path.Combine(dir, "weg_pruefung.txt"), treffer); } catch { }
            return $"Weg-Prüfung: {funde.Count} Objekte mit Kollision auf Wegen, {zaunAufWeg} Zaun-/Heckenteile auf Wegen · ";
        }

        private static readonly HashSet<string> Ausgeformt = new HashSet<string>
        {
            "L3 Langer Rain", "L4 Hallgrund", "L6 Dreispannhof", "L12 Heckenhof (Rosenhag)", "L7 Hirtenlager",
            "L10 Bürgerwehr-Posten", "Kornwagen", "L9 Alte Zollhütte",
        };

        private static List<KarethV4Shots.Shot> Shots(Terrain t, Dictionary<string, Vector3> o)
        {
            var s = new List<KarethV4Shots.Shot>();
            Vector3 P(string k, Vector3 fallback) => o.TryGetValue(k, out var v) ? v : fallback;
            void Blick(string name, string von, string auf, float hoch, float fov = 60f)
            {
                Vector3 a = P(von, Vector3.zero), b = P(auf, Vector3.zero);
                if (a == Vector3.zero || b == Vector3.zero) return;
                // Entwurf 2: Auge 35 m vor dem Ort-Block (Richtung Ziel), sonst steht die Kamera im Block und sieht dessen Schatten
                Vector3 dir = b - a; dir.y = 0f; dir.Normalize();
                Vector3 e = a + dir * 35f;
                e.y = Hoehe(t, e.x, e.z) + hoch;
                Vector3 ziel = b + new Vector3(0f, 10f, 0f);
                if (ziel.y < e.y - 40f) ziel.y = e.y - 40f;          // tiefer gelegene Ziele (Meer, Westmarken): Blick zum Horizont
                s.Add(new KarethV4Shots.Shot { Name = name, Eye = e, Target = ziel, Fov = fov });
            }
            Blick("1_Kammsattel_auf_Muehlen", "L2 Kammsattel", "L1 Windmühlen", 1.8f);
            Blick("2_LangerRain_auf_Muehlen", "L3 Langer Rain", "L1 Windmühlen", 1.8f);
            Blick("3_Hirtenlager_auf_Greifhorst", "L7 Hirtenlager", "Greif-Horst", 1.8f);
            // Entwurf 8: Erster Horizont – vom Ort steigt das Gelände noch 15 m bis zur Kante (x ≈ 300); Auge an der Kante, Blick genau nach Westen
            if (o.TryGetValue("L11 Erster Horizont", out Vector3 eh))
            {
                // Kante suchen: höchster Punkt zwischen x 200 und 400, Auge 6 m westlich davon auf Augenhöhe über der Kante
                float kx = 300f, kh = float.MinValue;
                for (float xx = 200f; xx <= 400f; xx += 2f) { float hh = Hoehe(t, xx, eh.z); if (hh > kh) { kh = hh; kx = xx; } }
                Vector3 e4 = new Vector3(kx - 6f, kh + 1.8f, eh.z);
                s.Add(new KarethV4Shots.Shot { Name = "4_ErsterHorizont_aufs_Meer", Eye = e4, Target = new Vector3(-2500f, 0f, eh.z), Fov = 70f });
            }
            Blick("5_Buergerwehr_auf_Passfuss", "L10 Bürgerwehr-Posten", "Passfuß (Heckenkönig)", 1.8f);
            Blick("6_Zollhuette_auf_Kornwagen", "L9 Alte Zollhütte", "Kornwagen", 1.8f);
            // Orte (Greybox v1): Langer Rain von der Hofstraße (Ost) und schräg von oben, Hallgrund von oben
            if (o.TryGetValue("L3 Langer Rain", out Vector3 lr))
            {
                Vector3 e1 = lr + new Vector3(110f, 0f, 8f); e1.y = Hoehe(t, e1.x, e1.z) + 1.8f;
                s.Add(new KarethV4Shots.Shot { Name = "9_LangerRain_Ankunft_Ost", Eye = e1, Target = lr + new Vector3(0f, 4f, 10f), Fov = 60f });
                s.Add(new KarethV4Shots.Shot { Name = "10_LangerRain_schraeg", Eye = lr + new Vector3(-90f, 70f, -110f), Target = lr + new Vector3(0f, 0f, 5f), Fov = 55f });
            }
            if (o.TryGetValue("L4 Hallgrund", out Vector3 hg))
                s.Add(new KarethV4Shots.Shot { Name = "11_Hallgrund_schraeg", Eye = hg + new Vector3(-110f, 60f, -120f), Target = hg + new Vector3(-30f, 0f, -30f), Fov = 55f });
            void Ort(string name, string ort, Vector3 auge, Vector3 ziel)
            {
                if (!o.TryGetValue(ort, out Vector3 c)) return;
                Vector3 e = c + auge; e.y = Mathf.Max(e.y, Hoehe(t, e.x, e.z) + 3f);
                s.Add(new KarethV4Shots.Shot { Name = name, Eye = e, Target = c + ziel, Fov = 55f });
            }
            Ort("12_Dreispannhof_schraeg", "L6 Dreispannhof", new Vector3(80f, 45f, -90f), new Vector3(-10f, 0f, 0f));
            Ort("13_Heckenhof_schraeg", "L12 Heckenhof (Rosenhag)", new Vector3(-40f, 50f, -130f), new Vector3(-30f, 0f, -30f));
            Ort("14_Hirtenlager_schraeg", "L7 Hirtenlager", new Vector3(90f, 45f, -60f), new Vector3(15f, 0f, 15f));
            Ort("15_Posten_schraeg", "L10 Bürgerwehr-Posten", new Vector3(70f, 40f, -70f), new Vector3(-5f, 0f, -5f));
            Ort("16_Zollhuette_schraeg", "L9 Alte Zollhütte", new Vector3(70f, 40f, -70f), new Vector3(8f, 0f, 10f));
            Ort("17_Kornwagen_nah", "Kornwagen", new Vector3(22f, 4f, -14f), new Vector3(6f, 1f, 2f));
            // Dachfamilie (12.10.): nahe Blicke auf Reet alt/neu und Schindel
            Ort("18_Dreispannhof_Daecher_nah", "L6 Dreispannhof", new Vector3(34f, 7f, -34f), new Vector3(0f, 5f, 6f));
            Ort("19_LangerRain_Daecher_nah", "L3 Langer Rain", new Vector3(30f, 6f, -2f), new Vector3(-6f, 5f, 24f));
            // Wasser (12.10.): Brücke der Hofstraße am Ostrand von Langer Rain, Abflusstal nach Süden
            // Flur (12.10.): Hecken und Felder um Langer Rain und den Dreispannhof, Blick vom Kammsattel
            Ort("22_Flur_Langer_Rain_Sued", "L3 Langer Rain", new Vector3(-60f, 40f, -260f), new Vector3(0f, 0f, 60f));
            Ort("23_Flur_Hecke_nah", "L6 Dreispannhof", new Vector3(140f, 3f, -60f), new Vector3(60f, 1f, 0f));
            Ort("20_Muehlbach_Bruecke_Ost", "L3 Langer Rain", new Vector3(112f, 4f, -28f), new Vector3(74f, 0f, 4f));
            // Holzbau (14.10.): Koppelzaun in Hallgrund, Gartenzaun von Langer Rain, Brücke nah
            Ort("24_Koppel_Hallgrund_nah", "L4 Hallgrund", new Vector3(-38f, 2.2f, -82f), new Vector3(-70f, 1f, -40f));
            Ort("25_Gartenzaun_LangerRain_nah", "L3 Langer Rain", new Vector3(12f, 2.4f, 76f), new Vector3(-6f, 1f, 59f));
            Ort("26_Bruecke_nah", "L3 Langer Rain", new Vector3(88f, 1.2f, -6f), new Vector3(74f, 0f, 4f));
            // Kreidefelsen (14.10.): Kliff der Westküste von oben entlang der Küste, Felskessel-Rand, Kehrental zum Grenzgebirge
            {
                Vector3 e = new Vector3(400f, 0f, 1180f); e.y = Hoehe(t, e.x, e.z) + 18f;
                s.Add(new KarethV4Shots.Shot { Name = "27_Kreidekliff_nach_Norden", Eye = e, Target = new Vector3(170f, 5f, 1750f), Fov = 60f });
            }
            Ort("28_Felskessel_Rand", "L5 Felskessel", new Vector3(-90f, 25f, -110f), new Vector3(0f, 10f, 0f));
            Ort("29_Kehrental", "Passfuß (Heckenkönig)", new Vector3(-80f, 20f, 40f), new Vector3(260f, 20f, -140f));
            {
                Vector3 e = new Vector3(2470f, 0f, 930f); e.y = Hoehe(t, e.x, e.z) + 22f;
                s.Add(new KarethV4Shots.Shot { Name = "21_Abflusstal_nach_Sueden", Eye = e, Target = new Vector3(2650f, 8f, 560f), Fov = 60f });
            }
            s.Add(new KarethV4Shots.Shot { Name = "7_Ueberblick_von_Suedost", Eye = new Vector3(SX * 0.85f, 650f, -400f), Target = new Vector3(SX * 0.45f, 60f, SZ * 0.55f), Fov = 55f });
            s.Add(new KarethV4Shots.Shot { Name = "8_Draufsicht", Eye = new Vector3(SX * 0.5f, 1500f, SZ * 0.5f), Target = new Vector3(SX * 0.5f, 0f, SZ * 0.5f), Ortho = SZ * 0.53f });
            return s;
        }
    }
}
