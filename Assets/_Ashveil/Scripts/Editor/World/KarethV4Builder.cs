using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kareth v4 (Entwurf Claude, Max 03.10.2026: „gestalte das gesamte Startgebiet nach deiner Vorstellung“) als EIGENE Szene.
    ///
    /// Baut aus den Entwurfsdaten (Data/KarethV4, erzeugt mit claude/kareth_v4/export.py) eine begehbare Szene zum Ansehen:
    /// Gelände 4 × 4 km (2 m Raster), Bodenschichten, Wald als Terrain-Bäume, Felsen, Gras, Salzsee und Moorwasser,
    /// das neue Lager Wachtfeuer (<see cref="KarethV4Camp"/>) und Blickpunkte.
    ///
    /// Bewusst NICHT angefasst: Sandbox_M1 (Testgelände, Spieltest r2), dessen Terrain, NavMesh, Inhalte, Tests.
    /// Eigene Pfade: Scenes/Kareth_v4.unity, Terrain/KarethV4_TerrainData.asset, Scenes/Kareth_v4_NavMesh.asset.
    /// Spielsysteme (Netz, Figur, Kamera, HUD, NavMesh) setzt <see cref="KarethV4Spiel"/> (Welle 0, #26); Gegner, NPCs und
    /// Aufträge kommen mit dem Umzug der Inhalte (#27).
    /// Menü: Ashveil › Welt › Kareth v4: Szene bauen (Entwurf).
    /// </summary>
    public static class KarethV4Builder
    {
        public const string DataFolder = ProjectSetup.Root + "/Data/KarethV4";
        public const string ScenePath = ProjectSetup.Root + "/Scenes/Kareth_v4.unity";
        private const string TerrainFolder = ProjectSetup.Root + "/Terrain";
        private const string TerrainDataPath = TerrainFolder + "/KarethV4_TerrainData.asset";

        /// <summary>Sonnenrichtung Kareth v4 (Max 05.10.: Sonne aus Südwest, Nachmittag): Das Licht fällt nach NNO (Gierwinkel 35°),
        /// kommt also aus SSW/SW (Azimut ≈ 215°). Mit Berg v2 liegt das Lager bei Südost-Sonne im Bergschatten
        /// (Höhendaten bei 28° Sonnenhöhe: Südost 2 %, Südwest 79 % des Lagers in der Sonne).</summary>
        public const float SunYaw = 35f;

        private static Terrain _terrain;
        public static KarethV4Plan Plan { get; private set; }

        // #29 Begehung (07.10.2026): Terrain-Bäume hatten keinen Kollider (Vorlage war das nackte Modell) – man lief durch die Stämme,
        // anders als in der Sandbox (ArtModels.ColliderKind.Trunk). Jetzt eigene Vorlagen mit Stamm-Kapsel und LOD-Gruppe (Fernstufe).
        public const string BaumVorlagenKey = "Ashveil.KarethV4.Baumvorlagen";
        private const string BaumFolder = TerrainFolder + "/KarethV4_Baeume";
        private const string BaumMenu = "Ashveil/Welt/Kareth v4: Bäume mit Stamm-Kollider und Fernstufe";
        /// <summary>Unter diesem Anteil der Bildhöhe wird ein Baum nicht mehr gezeichnet (Fernstufe „ausblenden“, mit Überblendung):
        /// bei 60° Sicht und LOD-Faktor 1 eine 12-m-Kiefer ab etwa 1,3 km, ein kleiner toter Baum ab etwa 0,9 km. Die Sichtweite der
        /// Bäume im Terrain (1,6 km) bleibt die Obergrenze.</summary>
        public const float BaumFernstufe = 0.008f;

        /// <summary>Schalter (Standard an): Bäume mit Stamm-Kollider und Fernstufe. Aus = wie vor #29 (Modell direkt, ohne Kollider).</summary>
        public static bool BaumVorlagen
        {
            get => EditorPrefs.GetBool(BaumVorlagenKey, true);
            set => EditorPrefs.SetBool(BaumVorlagenKey, value);
        }

        [MenuItem(BaumMenu, priority = 567)]
        private static void BaumVorlagenUmschalten()
        {
            BaumVorlagen = !BaumVorlagen;
            Debug.Log("[Ashveil] Kareth v4: Bäume mit Stamm-Kollider und Fernstufe " + (BaumVorlagen ? "an" : "aus") + " (gilt beim nächsten „Szene bauen“).");
        }

        [MenuItem(BaumMenu, true)]
        private static bool BaumVorlagenPruefen()
        {
            Menu.SetChecked(BaumMenu, BaumVorlagen);
            return true;
        }

        [MenuItem("Ashveil/Welt/Kareth v4: Szene bauen (Entwurf)", priority = 560)]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string report = Build();
            Debug.Log("[Ashveil] Kareth v4: " + report);
        }

        [MenuItem("Ashveil/Welt/Kareth v4: Szene oeffnen", priority = 561)]
        public static void OpenMenu()
        {
            if (!File.Exists(ScenePath)) { EditorUtility.DisplayDialog("Ashveil", "Kareth v4 ist noch nicht gebaut.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            FrameCamp();
        }

        public static KarethV4Plan LoadPlan()
        {
            string path = DataFolder + "/KarethV4_Plan.json";
            if (!File.Exists(path)) throw new FileNotFoundException("Entwurfsdaten fehlen: " + path);
            var plan = JsonUtility.FromJson<KarethV4Plan>(File.ReadAllText(path));
            Plan = plan;
            return plan;
        }

        /// <summary>Höhe des Geländes an (x, z) – nach dem Bau aus dem Terrain, sonst 0.</summary>
        public static float Ground(float x, float z)
        {
            if (_terrain == null) return 0f;
            return _terrain.SampleHeight(new Vector3(x, 0f, z)) + _terrain.transform.position.y;
        }

        public static Vector3 OnGround(float x, float z, float dy = 0f) => new Vector3(x, Ground(x, z) + dy, z);

        public static string Build()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            KarethV4Plan p = LoadPlan();
            // Alte PNG-Schichtkarten werden seit 03.10. nicht mehr gelesen (Rohdaten) – in den Papierkorb (Max: ok)
            foreach (string old in new[] { "KarethV4_Splat0.png", "KarethV4_Splat1.png", "KarethV4_Splat2.png", "KarethV4_Gras.png" })
                if (File.Exists(DataFolder + "/" + old)) AssetDatabase.MoveAssetToTrash(DataFolder + "/" + old);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            K.ResetCache();
            SetupLighting();
            Transform root = K.Group("Kareth v4 (Entwurf Claude)", null);

            EditorUtility.DisplayProgressBar("Kareth v4", "Gelände", 0.1f);
            _terrain = BuildTerrain(p, root);
            if (KarethV4Weidepass.Aktiv) Debug.Log("[Ashveil] Kareth v4: " + KarethV4Weidepass.PlanRaeumen(p));
            EditorUtility.DisplayProgressBar("Kareth v4", "Bäume", 0.35f);
            int trees = BuildTrees(p, _terrain);
            EditorUtility.DisplayProgressBar("Kareth v4", "Gras", 0.45f);
            long grass = BuildGrass(p, _terrain);
            EditorUtility.DisplayProgressBar("Kareth v4", "Felsen", 0.6f);
            int rocks = BuildRocks(p, root);
            BuildWater(p, root);
            EditorUtility.DisplayProgressBar("Kareth v4", "Lager Wachtfeuer", 0.75f);
            string camp = KarethV4Camp.Build(p, root);
            BuildMarkers(p, root);
            // Welle 0 (Map Pass 05.10.): Schwellen-Bauwerke an den Ausgängen, sichtbar geschlossen (WORLD-114)
            string schwellen = KarethV4Schwellen.Build(p, root);
            // Welle 0 (#27): Figuren mit Tagesablauf, Lagertiere, Auftragsorte, Erkundungsziele, Fundstellen (Beidatei
            // KarethV4_Inhalte.json) – vor Spielsystemen und NavMesh, damit Requisiten Hindernisse sind
            EditorUtility.DisplayProgressBar("Kareth v4", "Inhalte (Figuren, Aufträge, Tiere)", 0.85f);
            string inhalte = KarethV4Inhalte.Build(root);
            // Akt I „Das Register“ (13.10.2026): Halle begehbar – Torgang, Halle, Treppe, Salzkeller, Fundstellen R1/R2/R4 (vor dem NavMesh)
            inhalte += " · " + KarethV4Halle.Build(root, _terrain);
            // PBR-Gebäude (Weltmaterialien 04.10.; seit 07.10. bei jedem Bau – vorher stand nach einem Neuaufbau wieder die Greybox)
            if (EditorPrefs.GetBool(WeltMaterialtest.KV4GebaeudeKey, true))
            {
                EditorUtility.DisplayProgressBar("Kareth v4", "PBR-Gebäude", 0.88f);
                Debug.Log("[Ashveil] " + WeltMaterialtest.KarethV4GebaeudeAnwenden());
            }
            // Stil A (Max 05.10.): Licht- und Atmosphäre-Pass, wenn eingeschaltet (Ashveil › Welt › Stil A)
            if (StilAKareth.Enabled) Debug.Log("[Ashveil] Kareth v4: Stil A – " + StilAKareth.Apply());
            // Welle 0 (#26): Spielsysteme wie in der Sandbox – Netz, Spielfigur, Kamera, HUD, NavMesh, Start an der Ankunftsstelle
            string spiel = KarethV4Spiel.Enabled ? KarethV4Spiel.Build(p, root) : "Spielsysteme aus (Menü „Kareth v4: Spielsysteme beim Bau“)";
            EditorUtility.DisplayProgressBar("Kareth v4", "Speichern", 0.95f);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (KarethV4Spiel.Enabled) spiel += " · " + KarethV4Spiel.AfterSave(scene);   // Szenen-IDs, NavMesh, frisch laden
            EditorUtility.ClearProgressBar();
            FrameCamp();
            return $"Szene {ScenePath} in {sw.Elapsed.TotalSeconds:0} s: Terrain {p.size:0} m ({p.res}²), {trees} Bäume, {rocks} Felsen, " +
                   $"{grass} Grasbüschel, Lager: {camp}, {schwellen}, {inhalte}, {spiel}";
        }

        // ================================================================== Licht

        private static void SetupLighting()
        {
            var sunGo = new GameObject("Sonne (Nachmittag, SW)");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.88f, 0.72f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(34f, SunYaw, 0f);   // von SW (Wind- und Wetterseite, Regionsblatt)
            RenderSettings.sun = sun;
            var sky = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.5f, 0.6f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.35f, 0.34f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.11f, 0.1f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.6f, 0.65f, 0.7f);
            RenderSettings.fogDensity = 0.0008f;   // Region 4 km: Berg und Pass bleiben lesbar
            ArtStyleSetup.SetupLighting(sun);
            // ArtStyleSetup dreht die Sonne für Sandbox und Spieltest auf Südost (dort unverändert). Kareth v4 behält Südwest (Max 05.10.).
            sunGo.transform.rotation = Quaternion.Euler(34f, SunYaw, 0f);
            RenderSettings.fogDensity = Mathf.Min(RenderSettings.fogDensity, 0.0009f);
        }

        // ================================================================== Gelände

        private static Terrain BuildTerrain(KarethV4Plan p, Transform parent)
        {
            Directory.CreateDirectory(TerrainFolder);
            AssetDatabase.DeleteAsset(TerrainDataPath);
            var data = new TerrainData { heightmapResolution = p.res, alphamapResolution = p.splatRes, baseMapResolution = 1024 };
            data.size = new Vector3(p.size, p.rangeY, p.size);

            byte[] raw = File.ReadAllBytes(DataFolder + "/KarethV4_Hoehe.raw");
            if (raw.Length != p.res * p.res * 2) throw new IOException($"Höhenkarte hat {raw.Length} Bytes, erwartet {p.res * p.res * 2}");
            var heights = new float[p.res, p.res];
            for (int z = 0, i = 0; z < p.res; z++)
            for (int x = 0; x < p.res; x++, i += 2)
                heights[z, x] = (raw[i] | (raw[i + 1] << 8)) / 65535f;
            // Westweiden spielbar (12.10.2026): Klamm am Weidepass und Randspalte auf das Westweiden-Raster (nur mit Anschluss)
            if (KarethV4Weidepass.Aktiv) Debug.Log("[Ashveil] Kareth v4: " + KarethV4Weidepass.Kerbe(p, heights));
            data.SetHeights(0, 0, heights);

            TerrainLayer[] basisLayer = Layers(p.layers);
            // Wiesen-Pass (10.10.2026, CORE-009; ART-113 „Material-Familie in mehreren Maßstäben“ als erster Schritt): Die Grasschicht
            // wird in zwei Fassungen derselben Textur aufgeteilt – Grundkachel und 2,4-fache Kachel, etwas dunkler und olivgrauer –
            // und mit großräumigem Rauschen (≈ 55 m und 17 m) verteilt. Aus der Nähe bleibt das Bild, aus der Mitte und Ferne
            // verschwindet das Kachelmuster, die Wiese bekommt Flecken wie eine echte Wiese. Keine neue Textur, Kareth-Layer unverändert.
            // Acht Schichten = zwei Terrain-Pässe wie bisher (sieben brauchten schon zwei); eine dritte Fassung kostete einen Pass mehr.
            data.terrainLayers = GrasVarianten(basisLayer, p.layers, out int grasA, out int grasB);
            // Schichtgewichte als Rohdaten (keine Bild-Dekodierung, eindeutige Zeilenfolge): je Zelle 7 Bytes
            // Gras, Waldboden, Moor, Asche, Fels, Weg, Salz; Zeilen z Süd→Nord, Spalten x West→Ost
            byte[] sb = File.ReadAllBytes(DataFolder + "/KarethV4_Splat.bytes");
            int n = p.splatRes, L = data.terrainLayers.Length;
            const int C = 7;
            if (sb.Length != n * n * C) throw new IOException($"Schichtkarte hat {sb.Length} Bytes, erwartet {n * n * C}");
            if (KarethV4Weidepass.Aktiv) Debug.Log($"[Ashveil] Kareth v4: Klamm-Bodenschicht {KarethV4Weidepass.Splat(p, sb)} Zellen (Sohle Weg, Wände Fels)");
            var alpha = new float[n, n, L];
            float zellenMeter = p.size / n;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int k = (z * n + x) * C;
                float sum = 0f;
                for (int l = 0; l < C && l < basisLayer.Length; l++) sum += sb[k + l];
                if (sum <= 0f) { alpha[z, x, 0] = 1f; continue; }
                for (int l = 0; l < C && l < basisLayer.Length; l++) alpha[z, x, l] = sb[k + l] / sum;
                if (grasB >= 0 && alpha[z, x, grasA] > 0.001f)
                {
                    float wx = p.x0 + x * zellenMeter, wz = p.z0 + z * zellenMeter;
                    float nB = Mathf.PerlinNoise(wx / 55f + 13.7f, wz / 55f + 4.1f) * 0.65f + Mathf.PerlinNoise(wx / 17f + 2.3f, wz / 17f + 9.9f) * 0.35f;
                    float anteilB = Mathf.Clamp01((nB - 0.44f) / 0.26f) * 0.85f;   // weiche Flecken, nie ganz deckend
                    float g = alpha[z, x, grasA];
                    alpha[z, x, grasB] = g * anteilB;
                    alpha[z, x, grasA] = g * (1f - anteilB);
                }
            }
            data.SetAlphamaps(0, 0, alpha);

            AssetDatabase.CreateAsset(data, TerrainDataPath);
            foreach (Texture2D a in data.alphamapTextures)
                if (a != null && !AssetDatabase.Contains(a)) AssetDatabase.AddObjectToAsset(a, data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain Kareth v4";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(p.x0, p.baseY, p.z0);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
            var terrain = go.GetComponent<Terrain>();
            // Stämme der Baumvorlagen blockieren (#29): „Enable Tree Colliders“ ist Standard; hier ausdrücklich gesetzt, falls vorhanden
            var terrainCollider = go.GetComponent<TerrainCollider>();
            if (terrainCollider != null)
            {
                var so = new SerializedObject(terrainCollider);
                SerializedProperty trees = so.FindProperty("m_EnableTreeColliders");
                if (trees != null && !trees.boolValue)
                {
                    trees.boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            terrain.groupingID = 7;                 // nicht mit Sandbox-Kacheln verbinden
            terrain.allowAutoConnect = false;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 900f;
            terrain.drawInstanced = true;
            terrain.treeDistance = 1600f;
            terrain.treeBillboardDistance = 5000f;   // Kiefern haben (noch) keinen Billboard-Shader: immer als Modell zeichnen
            terrain.treeCrossFadeLength = 30f;
            terrain.treeMaximumFullLODCount = 600;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainFolder + "/Kareth_Terrain.mat");
            if (mat != null) terrain.materialTemplate = mat;
            if (EditorPrefs.GetBool(WeltMaterialtest.KV4Key, false)) terrain.materialTemplate = WeltMaterialtest.TerrainMaterialViele();
            // Prüfausgabe: Schichten und Gewichte am Wachtfeuer und an der Felsstirn
            int ax = Mathf.RoundToInt((0f - p.x0) / p.size * (p.splatRes - 1)), az = Mathf.RoundToInt((0f - p.z0) / p.size * (p.splatRes - 1));
            int bz = Mathf.RoundToInt((-60f - p.z0) / p.size * (p.splatRes - 1));
            float[,,] chk = data.GetAlphamaps(ax, az, 1, 1), chk2 = data.GetAlphamaps(ax, bz, 1, 1);
            // Direkt nach einer Neukompilierung gingen die Gewichte einmal verloren (nur Gras, 03.10. 22:01).
            // Prüfen und notfalls erneut setzen, damit kein stiller Fehlbau entsteht.
            if (Mathf.Abs(chk[0, 0, 0] - alpha[az, ax, 0]) > 0.02f || Mathf.Abs(chk2[0, 0, 0] - alpha[bz, ax, 0]) > 0.02f)
            {
                Debug.LogWarning("[Ashveil] Kareth v4: Bodenschichten nach dem Anlegen verloren – werden erneut gesetzt.");
                data.terrainLayers = GrasVarianten(Layers(p.layers), p.layers, out _, out _);
                data.SetAlphamaps(0, 0, alpha);
                foreach (Texture2D a2 in data.alphamapTextures)
                    if (a2 != null && !AssetDatabase.Contains(a2)) AssetDatabase.AddObjectToAsset(a2, data);
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
                terrain.Flush();
                chk = data.GetAlphamaps(ax, az, 1, 1); chk2 = data.GetAlphamaps(ax, bz, 1, 1);
            }
            var sbx = new System.Text.StringBuilder("[Ashveil] Kareth v4 Schichten:");
            for (int l = 0; l < data.terrainLayers.Length; l++)
                sbx.Append($" {(data.terrainLayers[l] != null ? data.terrainLayers[l].name : "null")}={chk[0, 0, l]:0.00}/{chk2[0, 0, l]:0.00}");
            sbx.Append($" | Material {(terrain.materialTemplate != null ? terrain.materialTemplate.shader.name : "null")}, Alphamap-Texturen {data.alphamapTextureCount}");
            Debug.Log(sbx.ToString());
            return terrain;
        }

        /// <summary>Wiesen-Pass: eine abgeleitete Grasschicht (größere Kachel, eigener Ton) an die Schichten anhängen.
        /// Eigenes Asset <c>KarethV4_Layer_GrassB</c>; die Quelle (Kareth-Layer oder PBR-Layer) bleibt unverändert.</summary>
        private static TerrainLayer[] GrasVarianten(TerrainLayer[] layers, string[] names, out int grasA, out int grasB)
        {
            grasA = System.Array.IndexOf(names, "Grass"); grasB = -1;
            if (grasA < 0 || grasA >= layers.Length || layers[grasA] == null) return layers;
            TerrainLayer b = Ableiten(layers[grasA], "B", 2.4f, new Vector4(0.88f, 0.92f, 0.82f, 1f), new Vector2(1.3f, 0.7f));
            var list = new List<TerrainLayer>(layers) { b };
            grasB = layers.Length;
            return list.ToArray();
        }

        private static TerrainLayer Ableiten(TerrainLayer q, string kennung, float kachelFaktor, Vector4 ton, Vector2 versatz)
        {
            string path = TerrainFolder + "/KarethV4_Layer_Grass" + kennung + ".terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (l == null) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, path); }
            l.diffuseTexture = q.diffuseTexture; l.normalMapTexture = q.normalMapTexture; l.normalScale = q.normalScale;
            l.maskMapTexture = q.maskMapTexture; l.maskMapRemapMin = q.maskMapRemapMin; l.maskMapRemapMax = q.maskMapRemapMax;
            l.metallic = q.metallic; l.smoothness = q.smoothness; l.specular = q.specular;
            l.tileSize = q.tileSize * kachelFaktor; l.tileOffset = q.tileOffset + versatz;
            l.diffuseRemapMin = q.diffuseRemapMin;
            Vector4 qm = q.diffuseRemapMax == Vector4.zero ? Vector4.one : q.diffuseRemapMax;
            l.diffuseRemapMax = Vector4.Scale(qm, ton);
            EditorUtility.SetDirty(l);
            return l;
        }

        private static TerrainLayer[] Layers(string[] names)
        {
            var list = new List<TerrainLayer>();
            Color[] fallback = { new Color(0.33f, 0.39f, 0.22f), new Color(0.27f, 0.28f, 0.18f), new Color(0.22f, 0.25f, 0.2f),
                                 new Color(0.31f, 0.29f, 0.27f), new Color(0.44f, 0.44f, 0.45f), new Color(0.42f, 0.34f, 0.24f),
                                 new Color(0.62f, 0.58f, 0.5f) };
            for (int i = 0; i < names.Length; i++)
            {
                // PBR-Bodenmaterialien (Schalter „Ashveil › Welt › Materialien (PBR)“), sonst dieselben Pixel-Layer wie Kareth
                TerrainLayer pbr = WeltMaterialtest.KarethV4Layer(names[i]);
                if (pbr != null) { list.Add(pbr); continue; }
                // Dieselben Schichten wie Kareth (Art-Bible-Pixeltexturen); nur lesen, nichts neu erzeugen
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainFolder + "/Layer_" + names[i] + ".terrainlayer");
                if (layer == null)
                {
                    string path = TerrainFolder + "/KarethV4_Layer_" + names[i] + ".terrainlayer";
                    layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                    if (layer == null)
                    {
                        var tex = new Texture2D(4, 4);
                        var px = new Color[16];
                        for (int k = 0; k < 16; k++) px[k] = fallback[Mathf.Min(i, fallback.Length - 1)];
                        tex.SetPixels(px); tex.Apply();
                        AssetDatabase.CreateAsset(tex, TerrainFolder + "/KarethV4_Tex_" + names[i] + ".asset");
                        layer = new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(4f, 4f) };
                        AssetDatabase.CreateAsset(layer, path);
                    }
                }
                list.Add(layer);
            }
            return list.ToArray();
        }

        private static Texture2D LoadPng(string file)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false, true);
            tex.LoadImage(File.ReadAllBytes(DataFolder + "/" + file));
            return tex;
        }

        // ================================================================== Wald (Terrain-Bäume), Gras, Felsen

        private static int BuildTrees(KarethV4Plan p, Terrain t)
        {
            string[] names = { "SM_Pine_A", "SM_Pine_B", "SM_Pine_C", "SM_DeadTree" };
            var protos = new List<TreePrototype>();
            var map = new int[names.Length];
            int vorlagen = 0;
            for (int i = 0; i < names.Length; i++)
            {
                GameObject prefab = ArtModels.Get(names[i]);
                map[i] = -1;
                if (prefab == null) continue;
                if (BaumVorlagen)
                {
                    GameObject vorlage = BaumVorlage(names[i], prefab);
                    if (vorlage != null)
                    {
                        prefab = vorlage;
                        vorlagen++;
                    }
                    else Debug.LogWarning("[Ashveil] Kareth v4: Baumvorlage für " + names[i] + " nicht gespeichert – Modell ohne Kollider.");
                }
                map[i] = protos.Count;
                protos.Add(new TreePrototype { prefab = prefab, bendFactor = 0.2f });
            }
            if (protos.Count == 0) { Debug.LogWarning("[Ashveil] Kareth v4: keine Baummodelle gefunden."); return 0; }
            TerrainData d = t.terrainData;
            d.treePrototypes = protos.ToArray();
            var list = new List<TreeInstance>(p.baeume.Length / 5);
            // Wiesen-Pass (10.10.2026, Max: „Ja, Gruppen und Waldränder“): Auf der offenen Wiese um das Lager (Grasschicht stark,
            // Waldboden schwach, bis 420 m vom Wachtfeuer) standen die Kiefern gleichmäßig verstreut. Jetzt bleiben sie nur in
            // Gruppen (Rauschen ≈ 60 m) oder als wenige Solitäre; Gruppen bekommen ein bis zwei Nachbarn dazu. Wege, Lager und
            // Wald außerhalb der Wiese bleiben, wie der Plan sie setzt.
            byte[] splat = File.ReadAllBytes(DataFolder + "/KarethV4_Splat.bytes");
            int sn = p.splatRes;
            float SplatAn(float wx, float wz, int kanal)
            {
                int ix = Mathf.Clamp(Mathf.RoundToInt((wx - p.x0) / p.size * (sn - 1)), 0, sn - 1);
                int iz = Mathf.Clamp(Mathf.RoundToInt((wz - p.z0) / p.size * (sn - 1)), 0, sn - 1);
                int k = (iz * sn + ix) * 7; float sum = 0f;
                for (int l = 0; l < 7; l++) sum += splat[k + l];
                return sum > 0f ? splat[k + kanal] / sum : 0f;
            }
            var rng = new System.Random(1010);
            int entfernt = 0, dazu = 0;
            // Bäume nahe an Blickpunkten und Orten des Plans sind gesetzte Landmarken (z. B. die Kiefer am Ankunftspfad) – bleiben
            var landmarken = new List<Vector2>();
            if (p.blickpunkte != null) foreach (var b in p.blickpunkte) { landmarken.Add(new Vector2(b.x, b.z)); landmarken.Add(new Vector2(b.lx, b.lz)); }
            if (p.orte != null) foreach (var o in p.orte) landmarken.Add(new Vector2(o.x, o.z));
            bool Landmarke(float wx, float wz) { foreach (var l in landmarken) if ((l.x - wx) * (l.x - wx) + (l.y - wz) * (l.y - wz) < 30f * 30f) return true; return false; }
            for (int i = 0; i + 4 < p.baeume.Length; i += 5)
            {
                int kind = Mathf.Clamp((int)p.baeume[i + 2], 0, names.Length - 1);
                int proto = map[kind] >= 0 ? map[kind] : map[0];
                if (proto < 0) continue;
                float s = p.baeume[i + 3];
                float bx = p.baeume[i], bz = p.baeume[i + 1];
                bool wiese = bx * bx + bz * bz < 420f * 420f && SplatAn(bx, bz, 0) > 0.55f && SplatAn(bx, bz, 1) < 0.35f;
                if (wiese)
                {
                    float gruppe = Mathf.PerlinNoise(bx / 60f + 5.5f, bz / 60f + 8.25f);
                    bool solitaer = rng.NextDouble() < 0.07 || Landmarke(bx, bz);
                    if (gruppe < 0.58f && !solitaer) { entfernt++; continue; }
                    if (gruppe >= 0.58f)
                    {
                        int nachbarn = 1 + (gruppe > 0.68f ? 1 : 0);
                        for (int q = 0; q < nachbarn; q++)
                        {
                            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 4f + (float)rng.NextDouble() * 5f;
                            float nx = bx + Mathf.Cos(a) * r, nz = bz + Mathf.Sin(a) * r;
                            if (nx * nx + nz * nz < 70f * 70f) continue;                      // nicht ins Lager
                            if (SplatAn(nx, nz, 5) > 0.25f || SplatAn(nx, nz, 0) < 0.5f) continue;   // nicht auf Wege, nur auf Gras
                            float ns = s * (0.8f + (float)rng.NextDouble() * 0.3f);
                            list.Add(new TreeInstance
                            {
                                position = new Vector3((nx - p.x0) / p.size, 0f, (nz - p.z0) / p.size),
                                prototypeIndex = proto, widthScale = ns, heightScale = ns * 1.05f,
                                rotation = (float)rng.NextDouble() * Mathf.PI * 2f, color = Color.white, lightmapColor = Color.white
                            });
                            dazu++;
                        }
                    }
                }
                list.Add(new TreeInstance
                {
                    position = new Vector3((bx - p.x0) / p.size, 0f, (bz - p.z0) / p.size),
                    prototypeIndex = proto, widthScale = s, heightScale = s * 1.05f,
                    rotation = p.baeume[i + 4] * Mathf.Deg2Rad, color = Color.white, lightmapColor = Color.white
                });
            }
            Debug.Log($"[Ashveil] Kareth v4 Wiesen-Pass: Bäume auf der Wiese um das Lager – {entfernt} verstreute entfernt, {dazu} in Gruppen ergänzt.");
            d.SetTreeInstances(list.ToArray(), true);
            t.Flush();
            EditorUtility.SetDirty(d);
            if (BaumVorlagen) Debug.Log($"[Ashveil] Kareth v4: {vorlagen} von {protos.Count} Baumvorlagen mit Stamm-Kollider und Fernstufe ({BaumFernstufe * 100f:0.0} % Bildhöhe).");
            return list.Count;
        }

        /// <summary>
        /// Vorlage für Terrain-Bäume (#29): das Modell als Kind, eine Kapsel um den Stamm wie <see cref="ArtModels.ColliderKind.Trunk"/>
        /// (Radius 0,35 m, 60 % der Höhe – Kronen darf man unterlaufen) und eine LOD-Gruppe mit einer Stufe, die unter
        /// <see cref="BaumFernstufe"/> ausblendet. Unity verlangt für Bäume ohne LOD-Gruppe den Nature-Shader (Warnung bei jedem Bau);
        /// mit LOD-Gruppe entfällt das. Wird bei jedem Bau neu geschrieben (Pfad und GUID bleiben gleich).
        /// </summary>
        public static GameObject BaumVorlage(string name, GameObject model)
        {
            Directory.CreateDirectory(BaumFolder);
            string path = $"{BaumFolder}/KV4_{name}.prefab";
            var root = new GameObject("KV4_" + name);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.transform.SetParent(root.transform, false);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
                Renderer[] renderers = inst.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) return null;
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                var cap = root.AddComponent<CapsuleCollider>();
                cap.direction = 1;
                cap.radius = 0.35f;
                cap.height = Mathf.Max(1.5f, b.max.y * 0.6f);
                cap.center = new Vector3(0f, cap.height * 0.5f, 0f);
                var lod = root.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(BaumFernstufe, renderers) });
                lod.fadeMode = LODFadeMode.CrossFade;
                lod.animateCrossFading = true;
                lod.RecalculateBounds();
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
                return ok ? saved : null;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static long BuildGrass(KarethV4Plan p, Terrain t)
        {
            // Wiesen-Pass (10.10.2026, CORE-009): Grasfläche (gebogene Halmkarten, gemalte Textur) statt des Stern-Büschels
            GameObject grass = GrasPatchGenerator.Vorlage() ?? ArtModels.Get("SM_GrassClump"), fern = ArtModels.Get("SM_Fern");
            if (grass == null) return 0;
            TerrainData d = t.terrainData;
            const int res = 2048;                    // 2 m je Zelle
            d.SetDetailResolution(res, 32);
            d.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            var protos = new List<DetailPrototype>
            {
                new DetailPrototype { prototype = grass, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                    minWidth = 1.1f, maxWidth = 1.7f, minHeight = 0.85f, maxHeight = 1.35f, noiseSpread = 0.45f, alignToGround = 0.6f }
            };
            if (fern != null)
                protos.Add(new DetailPrototype { prototype = fern, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                    minWidth = 0.8f, maxWidth = 1.4f, minHeight = 0.8f, maxHeight = 1.5f, noiseSpread = 0.5f, alignToGround = 0.3f });
            // Idylle (WORLD-244, 10.10.2026): Korn und Flachs als eigene Details auf den Feldern (KarethV4_Felder.bytes)
            byte[] felder = KarethV4Idylle.Felder(p.splatRes);
            int kornL = -1, flachsL = -1;
            if (felder != null)
            {
                GameObject korn = FeldPatchGenerator.Korn(), flachs = FeldPatchGenerator.Flachs();
                if (korn != null)
                {
                    kornL = protos.Count;
                    protos.Add(new DetailPrototype { prototype = korn, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                        minWidth = 1.35f, maxWidth = 1.7f, minHeight = 1.3f, maxHeight = 1.55f, noiseSpread = 0.3f, alignToGround = 0.15f });
                }
                if (flachs != null)
                {
                    flachsL = protos.Count;
                    protos.Add(new DetailPrototype { prototype = flachs, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                        minWidth = 1.35f, maxWidth = 1.7f, minHeight = 1.15f, maxHeight = 1.35f, noiseSpread = 0.3f, alignToGround = 0.15f });
                }
            }
            var kf = new int[res, res];
            var ff = new int[res, res];
            long feld = 0;
            d.detailPrototypes = protos.ToArray();
            byte[] px = File.ReadAllBytes(DataFolder + "/KarethV4_Gras.bytes");       // Zeilen Süd→Nord
            byte[] sb = File.ReadAllBytes(DataFolder + "/KarethV4_Splat.bytes");      // Kanal 1 = Waldboden
            int mw = p.splatRes;
            var g = new int[res, res];
            var f = new int[res, res];
            var rng = new System.Random(4711);
            long total = 0;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                int k = (z * mw / res) * mw + (x * mw / res);
                if (felder != null)
                {
                    byte fc = felder[k];
                    // dicht und gleichmäßig wie ein bestelltes Feld: drei Flecken je 2-m-Zelle, selten eine Lücke
                    if (fc == KarethV4Idylle.Korn && kornL >= 0) { int c0 = rng.NextDouble() < 0.06 ? 2 : 3; kf[z, x] = c0; feld += c0; continue; }
                    if (fc == KarethV4Idylle.Flachs && flachsL >= 0) { int c0 = rng.NextDouble() < 0.1 ? 2 : 3; ff[z, x] = c0; feld += c0; continue; }
                }
                float dens = px[k] / 255f;
                if (dens < 0.05f) continue;
                // Flecken statt Büschel: je 2-m-Zelle bis zu drei Flecken (1,1–1,7 m breit) – dicht in Flächen, Ränder dünner;
                // auf Wegen (Schicht „Path“) kein Gras, am Wegrand ausgedünnt – die Wege bleiben lesbar (Gameplay vor Optik)
                float weg = sb[k * 7 + 5] / 255f;
                if (weg > 0.35f) continue;
                dens *= 1f - Mathf.Clamp01(weg / 0.35f) * 0.8f;
                int c = 0;
                for (int s = 0; s < 3; s++) if (rng.NextDouble() < dens * 0.9) c++;
                g[z, x] = c; total += c;
                float wald = sb[k * 7 + 1] / 255f;
                // Durchgang 2 (10.10.): Unterholz am Waldrand – Farn dichter, wo Wald und Wiese ineinander übergehen (0,2–0,7),
                // im tiefen Wald wie bisher; Gras im Wald dünner (Nadelstreu statt Wiese)
                float rand = wald > 0.2f && wald < 0.7f ? 1f - Mathf.Abs(wald - 0.45f) / 0.25f : 0f;
                if (fern != null && wald > 0.2f && rng.NextDouble() < 0.25 * wald + 0.35 * rand) f[z, x] = rand > 0.5f && rng.NextDouble() < 0.5 ? 2 : 1;
                if (wald > 0.4f) g[z, x] = Mathf.Min(g[z, x], 1);
            }
            d.SetDetailLayer(0, 0, 0, g);
            if (fern != null) d.SetDetailLayer(0, 0, 1, f);
            if (kornL >= 0) d.SetDetailLayer(0, 0, kornL, kf);
            if (flachsL >= 0) d.SetDetailLayer(0, 0, flachsL, ff);
            if (felder != null) Debug.Log($"[Ashveil] Kareth v4 Idylle: {feld} Feldpflanzen-Flecken (Korn, Flachs).");
            t.detailObjectDistance = 90f;
            t.detailObjectDensity = 1f;
            d.wavingGrassStrength = 0.2f;
            d.RefreshPrototypes();
            t.Flush();
            EditorUtility.SetDirty(d);
            AssetDatabase.SaveAssetIfDirty(d);
            return total;
        }

        private static int BuildRocks(KarethV4Plan p, Transform parent)
        {
            // Seit 05.10.: Felsen nach Geologie aus dem Felsbaukasten (je 64-m-Kachel ein Mesh); die alte Streuung nur noch als Rückfall
            if (p.felsenV2 != null && p.felsenV2.Length >= 12) return FelsKit.Build(p.felsenV2, parent, Ground);
            Transform rocks = K.Group("Felsen", parent);
            string[] names = { "SM_Rock_A", "SM_Rock_B", "SM_Rock_C" };
            Material fallback = K.Mat("KV4 Fels", new Color(0.43f, 0.42f, 0.41f));
            int count = 0;
            for (int i = 0; i + 4 < p.felsen.Length; i += 5)
            {
                float x = p.felsen[i], z = p.felsen[i + 1], s = p.felsen[i + 3];
                string name = names[Mathf.Clamp((int)p.felsen[i + 2], 0, 2)];
                Vector3 pos = OnGround(x, z, -0.25f * s);
                if (ArtModels.Place(name, rocks, pos, p.felsen[i + 4], s, ArtModels.ColliderKind.Mesh, true) == null)
                    K.Block("Fels", rocks, pos, new Vector3(1.6f, 1.1f, 1.4f) * s, fallback, p.felsen[i + 4]);
                count++;
            }
            return count;
        }

        // ================================================================== Wasser

        private static void BuildWater(KarethV4Plan p, Transform parent)
        {
            Transform w = K.Group("Wasser", parent);
            Material lake = K.Mat("KV4 Salzsee", new Color(0.33f, 0.47f, 0.52f), 0.85f);
            Material moor = K.Mat("KV4 Moorwasser", new Color(0.17f, 0.2f, 0.16f), 0.9f);
            // Salzsee: Westrand der Kachel (Gelände darunter liegt tiefer, Salzkruste knapp darüber)
            float x1 = -1150f;
            float width = x1 - p.x0;
            GameObject see = K.Prim(PrimitiveType.Plane, "Salzsee", w, new Vector3(p.x0 + width * 0.5f, p.seeY, p.z0 + p.size * 0.5f),
                new Vector3(width / 10f, 1f, p.size / 10f), Vector3.zero, lake, false);
            // Glockenmoor: stehendes Wasser zwischen den Bulten (seit Welle 0 auch flache Tümpel um die versunkenen Kuppeln)
            GameObject moorGo = K.Prim(PrimitiveType.Plane, "Moorwasser", w, new Vector3(170f, -21.7f, 1080f), new Vector3(90f, 1f, 70f), Vector3.zero, moor, false);
            // Schwimmen und Tauchen (MOV-169, Welle 0): beide Flächen sind Wasser für den Bewegungsmotor. Der Grund ist das
            // Gelände – wo es über dem Spiegel liegt, ist trocken; flaches Wasser zum Waten ergibt sich von selbst.
            see.AddComponent<Ashveil.Core.WaterVolume>().Configure(Rect.MinMaxRect(p.x0, p.z0, x1, p.z0 + p.size));
            moorGo.AddComponent<Ashveil.Core.WaterVolume>().Configure(Rect.MinMaxRect(170f - 450f, 1080f - 350f, 170f + 450f, 1080f + 350f));
        }

        // ================================================================== Orte, Spawn, Blickpunkte

        private static void BuildMarkers(KarethV4Plan p, Transform parent)
        {
            Transform m = K.Group("Orte (Entwurf, nur Markierung)", parent);
            foreach (KarethV4Ort o in p.orte)
            {
                var go = new GameObject("Ort " + o.id);
                go.transform.SetParent(m, false);
                go.transform.position = OnGround(o.x, o.z, 0.1f);
            }
            Transform views = K.Group("Blickpunkte", parent);
            foreach (KarethV4Blick b in p.blickpunkte)
            {
                var go = new GameObject("Blick " + b.name);
                go.transform.SetParent(views, false);
                Vector3 eye = OnGround(b.x, b.z, 1.8f);
                go.transform.position = eye;
                go.transform.rotation = Quaternion.LookRotation(new Vector3(b.lx, b.ly, b.lz) - eye);
            }
        }

        public static void FrameCamp()
        {
            SceneView v = SceneView.lastActiveSceneView;
            if (v == null) return;
            v.LookAt(new Vector3(0f, 2f, 0f), Quaternion.Euler(28f, 160f, 0f), 70f);
        }
    }
}
