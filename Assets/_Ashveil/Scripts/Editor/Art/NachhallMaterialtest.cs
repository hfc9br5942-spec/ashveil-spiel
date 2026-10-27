using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Ashveil.Gameplay.Motion.Procedural;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Schwert „Nachhall“ Rev. E – Blockout + PBR-Sets: Importeinstellungen, URP/Lit-Materialien, Prefabs je Größe,
    /// isolierte Testszene „Nachhall_Materialtest“ und Prüfbilder (nah / Spielkamera / fern, Tag / Dämmerung,
    /// Bloom an/aus, Streiflicht, 3×3-Kachelproben). Berührt keine Spielszenen, keine Spielmechanik, kein Inventar.
    /// Ergebnisse: Logs/Ashveil/Nachhall/*.png und Bericht.txt.
    /// </summary>
    public static class NachhallMaterialtest
    {
        private const string Root = "Assets/_Ashveil/Art/Entwurf/Nachhall";   // Entwurf/Blockout, kein Slot-Ersatz
        private const string MeshDir = Root + "/Meshes";
        private const string TexDir = Root + "/Texturen";
        private const string MatDir = Root + "/Materialien";
        private const string PrefabDir = Root + "/Prefabs";
        private const string ScenePath = Root + "/Nachhall_Materialtest.unity";
        private const int InGameMaxSize = 1024;   // Kachelsets: 1024 px auf 0,05–0,20 m reichen für Third-Person; Quellen bleiben 2048

        // Set-Name → (Material, Kachelgröße m, metallisch?)
        private static readonly (string set, string mat, float tile)[] Sets =
        {
            ("T31_Klingenstahl", "Nachhall_Steel", 0.50f),
            ("T31F_Klingenstahl_Bruch", "Nachhall_Fracture", 0.05f),
            ("T24_Dunkles_Wurzelholz", "Nachhall_Wood", 0.15f),
            ("T33_Alter_Knochen", "Nachhall_Bone", 0.10f),
        };

        private static readonly string[] Groessen = { "Kurzschwert", "Grossschwert" };

        [MenuItem("Ashveil/Waffen/Nachhall/1 Import und Materialien einrichten", priority = 700)]
        public static void Einrichten()
        {
            AssetDatabase.Refresh();
            // alte Dateinamen (_LOD0–2) lösten Unitys automatische LOD-Erkennung aus → in den Papierkorb
            foreach (string g in Groessen) foreach (string l in new[] { "LOD0", "LOD1", "LOD2" })
                { string op = $"{MeshDir}/Nachhall_{g}_{l}.obj"; if (AssetDatabase.LoadMainAssetAtPath(op) != null) AssetDatabase.MoveAssetToTrash(op); }
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
                TexturEinstellen(AssetDatabase.GUIDToAssetPath(guid));
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(MatDir);
            var mats = new Dictionary<string, Material>();
            foreach (var s in Sets) mats[s.mat] = PbrMaterial(s.set, s.mat);
            mats["Nachhall_Energy"] = EnergieMaterial();
            AssetDatabase.SaveAssets();
            foreach (string g in Groessen) ModellEinstellen($"{MeshDir}/Nachhall_{g}_Blockout.obj", mats);
            Directory.CreateDirectory(PrefabDir);
            foreach (string g in Groessen) PrefabBauen(g, mats);
            foreach (string g in Groessen)
            {
                for (int i = 0; i < Lods.Length; i++) ModellEinstellen($"{MeshDir}/Nachhall_{g}_Spiel{i}.obj", mats);
                SpielPrefabBauen(g, mats);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Ashveil] Nachhall: Import, Materialien und Prefabs eingerichtet.");
        }

        private static void TexturEinstellen(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            string n = Path.GetFileNameWithoutExtension(path);
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 4;
            imp.maxTextureSize = InGameMaxSize;
            imp.isReadable = false;
            imp.alphaIsTransparency = false;
            if (n.EndsWith("_NormalGL"))
            {
                imp.textureType = TextureImporterType.NormalMap;   // Unity erwartet OpenGL (+Y) → keine Umkehr
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
            }
            else if (n.EndsWith("_BaseColor") || n.EndsWith("_Emission"))
            {
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = true;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                if (n.EndsWith("_Emission")) imp.maxTextureSize = 1024;
            }
            else if (n.EndsWith("_Height"))
            {
                imp.textureType = TextureImporterType.SingleChannel;
                imp.sRGBTexture = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;   // 16 Bit erhalten; im Material nicht benutzt
            }
            else
            {
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = false;   // Roughness, Metallic, AO, URP-Maske: linear
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
            }
            imp.SaveAndReimport();
        }

        private static Texture2D Tex(string set, string map) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{set}/{set}_{map}.png");

        private static Material MaterialAnlegen(string name, Shader shader)
        {
            string p = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, p); }
            else m.shader = shader;
            return m;
        }

        private static Material PbrMaterial(string set, string name)
        {
            var m = MaterialAnlegen(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetFloat("_WorkflowMode", 1f);                          // Metallic
            m.SetTexture("_BaseMap", Tex(set, "BaseColor"));
            m.SetColor("_BaseColor", Color.white);
            var mask = Tex(set, "URP_MetallicSmoothness");            // R = Metallic, G = AO, A = 1 − Roughness
            m.SetTexture("_MetallicGlossMap", mask);
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);                            // Faktor auf Alpha der Maske
            m.SetFloat("_SmoothnessTextureChannel", 0f);              // Alpha der Metallic-Map
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_BumpMap", Tex(set, "NormalGL"));
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_OcclusionMap", mask);                      // URP liest G
            m.SetFloat("_OcclusionStrength", 1f);
            m.EnableKeyword("_OCCLUSIONMAP");
            m.SetTextureScale("_BaseMap", Vector2.one);               // UVs des Modells sind bereits in Kacheleinheiten
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material EnergieMaterial()
        {
            // URP/Unlit, opak, beidseitig: keine Beleuchtung, kein Emissions-Schlüsselwort (das URP beim Import
            // zurücksetzte), keine Transparenz-Sortierung. Farbe = Energietextur × Intensität (HDR-Faktor > 1 →
            // ohne Bloom hell lesbar, mit Bloom nur schmaler Saum, da die Fläche nur in den Spalten liegt).
            var m = MaterialAnlegen("Nachhall_Energy", Shader.Find("Universal Render Pipeline/Unlit"));
            m.shaderKeywords = new string[0];
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/NH_Energie/NH_Energie_Emission.png"));
            m.SetColor("_BaseColor", new Color(EnergieIntensitaet, EnergieIntensitaet, EnergieIntensitaet, 1f));
            m.renderQueue = -1;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        private const float EnergieIntensitaet = 1.4f;

        private static void ModellEinstellen(string path, Dictionary<string, Material> mats)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { Debug.LogWarning($"[Ashveil] Nachhall: Modell fehlt: {path}"); return; }
            imp.globalScale = 1f;
            imp.useFileScale = false;
            imp.importNormals = ModelImporterNormals.Calculate;
            imp.normalSmoothingAngle = 35f;                           // Fasen/Schneiden hart, Flächen glatt
            imp.importTangents = ModelImporterTangents.CalculateMikk;
            imp.importAnimation = false;
            imp.animationType = ModelImporterAnimationType.None;
            imp.isReadable = false;
            imp.meshCompression = ModelImporterMeshCompression.Off;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var kv in mats)
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            imp.SaveAndReimport();
        }

        private static void PrefabBauen(string groesse, Dictionary<string, Material> mats)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{MeshDir}/Nachhall_{groesse}_Blockout.obj");
            if (model == null) return;
            var root = new GameObject($"Nachhall_{groesse}");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            inst.name = "Modell";
            // Konvention RigidPart/Grip: Klinge entlang +Z, Breite Y, Dicke X, Meter, Ursprung = Griffmitte.
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var sm = r.sharedMaterials;
                for (int i = 0; i < sm.Length; i++)
                {
                    string key = sm[i] != null ? sm[i].name.Replace(" (Instance)", "") : "";
                    foreach (var kv in mats) if (key.StartsWith(kv.Key)) sm[i] = kv.Value;
                }
                r.sharedMaterials = sm;
                r.shadowCastingMode = r.gameObject.name.StartsWith("Energy") ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }
            PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Nachhall_{groesse}.prefab");
            Object.DestroyImmediate(root);
        }

        private static readonly string[] Lods = { "LOD0", "LOD1", "LOD2" };
        // Bildschirmanteil (Höhe), ab dem eine Stufe gilt (Unity rechnet mit der LODGroup-Größe; × LOD-Bias der Qualitätsstufe).
        // Kurzschwert 0,84 m, FOV 55°: LOD0 bis ≈ 5 m, LOD1 bis ≈ 16 m, LOD2 bis ≈ 67 m, danach ausgeblendet (Großschwert ≈ 1,8×).
        private static readonly float[] LodSchwellen = { 0.15f, 0.05f, 0.012f };

        /// <summary>Spielmodell: je LOD vier Objekte (Blade, Hilt, Guard, Pommel) unter einem Root mit LODGroup.
        /// Konvention wie Blockout (Klinge +Z, Ursprung Griffmitte). Keine Collider.</summary>
        private static void SpielPrefabBauen(string groesse, Dictionary<string, Material> mats)
        {
            var root = new GameObject($"Nachhall_{groesse}_Spiel");
            var lods = new List<LOD>();
            for (int i = 0; i < Lods.Length; i++)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{MeshDir}/Nachhall_{groesse}_Spiel{i}.obj");
                if (model == null) { Debug.LogWarning($"[Ashveil] Nachhall: {groesse} {Lods[i]} fehlt"); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                inst.name = Lods[i];
                var rs = inst.GetComponentsInChildren<Renderer>();
                foreach (var r in rs)
                {
                    var sm = r.sharedMaterials;
                    for (int k = 0; k < sm.Length; k++)
                    {
                        string key = sm[k] != null ? sm[k].name : "";
                        foreach (var kv in mats) if (key.StartsWith(kv.Key)) sm[k] = kv.Value;
                    }
                    r.sharedMaterials = sm;
                    r.shadowCastingMode = i == 2 ? ShadowCastingMode.Off : ShadowCastingMode.On;   // ferne Stufe ohne Schatten
                    r.receiveShadows = i < 2;
                }
                foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                lods.Add(new LOD(LodSchwellen[i], rs));
            }
            var lg = root.AddComponent<LODGroup>();
            lg.SetLODs(lods.ToArray());
            lg.fadeMode = LODFadeMode.None;
            lg.RecalculateBounds();
            root.AddComponent<Ashveil.Gameplay.Combat.WeaponEchoVisual>();   // Lichtpuls des „Nachhall“ (Server-RPC → alle Clients)
            PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Nachhall_{groesse}_Spiel.prefab");
            Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------ Spielmodell prüfen
        [MenuItem("Ashveil/Waffen/Nachhall/6 Spielmodell prüfen (LOD, Griff, Draw Calls)", priority = 705)]
        public static void SpielmodellPruefen()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Ashveil] Nachhall: nicht im Play-Modus."); return; }
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Ashveil", "Nachhall", "Spiel"));
            Directory.CreateDirectory(outDir);
            var sb = new StringBuilder("Nachhall – Spielmodell (LOD0–2)\n");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            try
            {
                var cam = Object.FindFirstObjectByType<Camera>();
                VolumeJeBild(cam);
                var bloomAus = GameObject.Find("BloomAus"); if (bloomAus != null) bloomAus.SetActive(true);
                foreach (var old in new[] { "Nachhall_Kurzschwert", "Nachhall_Grossschwert" })
                { var o = GameObject.Find(old); if (o != null) o.SetActive(false); }
                float x = -0.25f;
                foreach (string g in Groessen)
                {
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Nachhall_{g}_Spiel.prefab");
                    if (pf == null) { sb.AppendLine($"{g}: Spiel-Prefab fehlt"); x += 0.5f; continue; }
                    var s = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                    s.transform.SetPositionAndRotation(new Vector3(x, 1.0f, 0f), Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, 0f, 90f));
                    x += 0.5f;
                    var lg = s.GetComponent<LODGroup>();
                    sb.AppendLine($"\n{g}  (LODGroup-Größe {lg.size:F2} m)");
                    var lods = lg.GetLODs();
                    for (int i = 0; i < lods.Length; i++)
                    {
                        int tris = 0, sub = 0;
                        foreach (var r in lods[i].renderers)
                        {
                            var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                            for (int k = 0; k < mf.sharedMesh.subMeshCount; k++) tris += (int)mf.sharedMesh.GetIndexCount(k) / 3;
                            sub += mf.sharedMesh.subMeshCount;
                        }
                        sb.AppendLine($"  LOD{i}: ab Bildanteil {lods[i].screenRelativeTransitionHeight:F3}  {lods[i].renderers.Length} Renderer  {tris} Dreiecke  {sub} Draw Calls (Untermeshes)");
                    }
                    // Griffvermessung am Spielmodell (nur LOD0 aktiv messen)
                    var pivot = new GameObject("Pivot");
                    var clone = Object.Instantiate(pf, pivot.transform);
                    clone.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    foreach (Transform t in clone.transform) t.gameObject.SetActive(t.name == "LOD0");
                    var style = g == "Kurzschwert" ? GripStyle.OneHand : GripStyle.TwoHandBlade;
                    var gi = WeaponGrip.Measure(pivot.transform, style, 0.085f);
                    sb.AppendLine($"  Griff ({style}): gültig {gi.Valid}, {gi.HandleMin:F3}…{gi.HandleMax:F3} m = {(gi.HandleMax - gi.HandleMin) * 100f:F1} cm, Radius {gi.Radius * 1000f:F1} mm{(gi.HasLeft ? $", linke Hand {(gi.Right.z - gi.Left.z) * 100f:F1} cm unter der rechten" : "")}");
                    Object.DestroyImmediate(pivot);
                }
                // Bilder: jede LOD-Stufe aus gleicher Nähe (erzwungen) und reale Stufen in Entfernung
                Vector3 front = new Vector3(0.25f, -0.08f, 1f);
                var gruppen = Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude);
                for (int i = 0; i < 3; i++)
                {
                    foreach (var lg in gruppen) lg.ForceLOD(i);
                    Ziel(cam, new Vector3(0f, 1.45f, 0f), front, 1.6f, 35f);
                    Render(cam, Path.Combine(outDir, $"0{i + 1}_LOD{i}_erzwungen.png"), 1600, 1000);
                }
                foreach (var lg in gruppen) lg.ForceLOD(-1);
                foreach (float d in new[] { 4.2f, 9f, 18f, 35f, 80f })
                {
                    Ziel(cam, new Vector3(0f, 1.15f, 0f), new Vector3(0.35f, -0.45f, 1f), d, 55f);
                    string welche = string.Join(", ", gruppen.Select(lg => $"{lg.name.Replace("Nachhall_", "").Replace("_Spiel", "")}: LOD{AktiveStufe(lg, cam)}"));
                    sb.AppendLine($"Abstand {d:F1} m → {welche}");
                    Render(cam, Path.Combine(outDir, $"1{d:00}_abstand_{d:F0}m.png"), 1600, 1000);
                }
            }
            finally
            {
                File.WriteAllText(Path.Combine(outDir, "Spielmodell.txt"), sb.ToString(), Encoding.UTF8);
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);   // nichts speichern
                Debug.Log($"[Ashveil] Nachhall: Spielmodell geprüft → {outDir}");
            }
        }

        private static void Ziel(Camera cam, Vector3 target, Vector3 dir, float dist, float fov)
        {
            cam.fieldOfView = fov;
            cam.transform.position = target - dir.normalized * dist;
            cam.transform.LookAt(target);
            VolumeAktualisieren(cam);
        }

        /// <summary>Welche LOD-Stufe die Kamera für diese Gruppe wählt (Unity-Formel: Bildanteil der Gruppengröße).</summary>
        private static int AktiveStufe(LODGroup lg, Camera cam)
        {
            Vector3 c = lg.transform.TransformPoint(lg.localReferencePoint);
            float size = lg.size * Mathf.Max(lg.transform.lossyScale.x, lg.transform.lossyScale.y, lg.transform.lossyScale.z);
            float dist = Vector3.Distance(cam.transform.position, c);
            float h = size / (2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * QualitySettings.lodBias;
            var lods = lg.GetLODs();
            for (int i = 0; i < lods.Length; i++) if (h >= lods[i].screenRelativeTransitionHeight) return i;
            return -1;   // ausgeblendet
        }

        // ------------------------------------------------------------------ Testszene
        [MenuItem("Ashveil/Waffen/Nachhall/2 Testszene bauen", priority = 701)]
        public static void TestszeneBauen()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Ashveil] Nachhall: nicht im Play-Modus."); return; }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.60f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.42f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.20f, 0.18f);
            RenderSettings.skybox = null;

            var sun = new GameObject("Sonne").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.6f; sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            var boden = GameObject.CreatePrimitive(PrimitiveType.Plane);
            boden.name = "Boden_Neutralgrau"; boden.transform.localScale = new Vector3(3f, 1f, 3f);
            boden.GetComponent<Renderer>().sharedMaterial = Neutral("Nachhall_Test_Neutral", new Color(0.36f, 0.36f, 0.36f), 0.15f);

            // Schwerter aufrecht (Prefab +Z → Welt +Y), Griffmitte auf 1,0 m (Handhöhe)
            float x = -0.25f;
            foreach (string g in Groessen)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Nachhall_{g}.prefab");
                if (pf == null) continue;
                var s = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                s.transform.SetPositionAndRotation(new Vector3(x, 1.0f, 0f), Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, 0f, 90f));
                x += 0.5f;
            }
            // Größenreferenz 1,80 m
            var ref180 = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            ref180.name = "Referenz_1_80m"; ref180.transform.position = new Vector3(1.0f, 0.9f, 0.3f);
            ref180.transform.localScale = new Vector3(0.45f, 0.9f, 0.3f);
            ref180.GetComponent<Renderer>().sharedMaterial = Neutral("Nachhall_Test_Referenz", new Color(0.5f, 0.5f, 0.5f), 0.2f);

            // 3×3-Kachelproben je Set (Quad mit UV 0..3, Kantenlänge 3 × Kachel), an einer Tafel hinter den Schwertern
            float px = KachelStartX;   // linke Kante; abseits der Schwerter
            foreach (var s in Sets)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{s.mat}.mat");
                var q = new GameObject($"Kachelprobe_{s.set}");
                var mf = q.AddComponent<MeshFilter>(); mf.sharedMesh = KachelQuad();
                q.AddComponent<MeshRenderer>().sharedMaterial = mat;
                float size = 3f * s.tile;
                q.transform.localScale = new Vector3(size, size, 1f);
                q.transform.position = new Vector3(px + size * 0.5f, 1.0f + size * 0.5f, 0f);   // Quad zeigt −Z (zur Kamera)
                px += size + 0.08f;
            }

            // PostFX wie im Spiel: Kareth-Profil (Tonemapping, Bloom, Farbkorrektur …) + Override-Volume „Bloom aus“
            var vol = new GameObject("PostFX_Kareth").AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 0;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Ashveil/Art/Rendering/Ashveil_Kareth_Profile.asset");
            if (vol.sharedProfile == null) Debug.LogWarning("[Ashveil] Nachhall: Kareth-Profil nicht gefunden – Prüfbilder ohne Spiel-PostFX.");
            AssetDatabase.DeleteAsset($"{MatDir}/Nachhall_Test_PostFX.asset");   // alter, fehlerhaft gespeicherter Stand
            string offPath = $"{MatDir}/Nachhall_Test_BloomAus.asset";
            AssetDatabase.DeleteAsset(offPath);
            var off = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(off, offPath);
            var b0 = off.Add<Bloom>(true); b0.intensity.Override(0f);
            AssetDatabase.AddObjectToAsset(b0, off);                   // Komponente als Unter-Asset speichern
            EditorUtility.SetDirty(off); AssetDatabase.SaveAssets();
            var volOff = new GameObject("BloomAus").AddComponent<Volume>();
            volOff.isGlobal = true; volOff.priority = 10; volOff.sharedProfile = off;

            var cam = new GameObject("Kamera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.30f, 0.32f, 0.35f);
            cam.nearClipPlane = 0.02f; cam.fieldOfView = 35f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Ashveil] Nachhall: Testszene gespeichert: {ScenePath}");
        }

        private static Material Neutral(string name, Color c, float smooth)
        {
            var m = MaterialAnlegen(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        private const float KachelStartX = 2.0f;
        private static readonly Vector3 KachelMitte = new Vector3(3.32f, 1.40f, 0f);

        private static Mesh _quad;
        private static Mesh KachelQuad()
        {
            string p = $"{MatDir}/Nachhall_Kachelquad.asset";
            _quad = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "Nachhall_Kachelquad" };
            _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(3, 0), new Vector2(3, 3), new Vector2(0, 3) };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quad.RecalculateNormals(); _quad.RecalculateTangents(); _quad.RecalculateBounds();
            AssetDatabase.CreateAsset(_quad, p);
            return _quad;
        }

        // ------------------------------------------------------------------ Prüfbilder + Messung
        [MenuItem("Ashveil/Waffen/Nachhall/3 Prüfbilder und Messung", priority = 702)]
        public static void Pruefbilder()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Ashveil] Nachhall: nicht im Play-Modus."); return; }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);   // immer frisch von der Platte
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Ashveil", "Nachhall"));
            Directory.CreateDirectory(outDir);
            var cam = Object.FindFirstObjectByType<Camera>();
            var sun = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).First(l => l.type == LightType.Directional);
            var bloomAus = GameObject.Find("BloomAus");
            var kurz = GameObject.Find("Nachhall_Kurzschwert");
            var gross = GameObject.Find("Nachhall_Grossschwert");

            Vector3 kB = kurz != null ? BruchMitte(kurz) : new Vector3(-0.25f, 1.4f, 0f);
            Vector3 gB = gross != null ? BruchMitte(gross) : new Vector3(0.25f, 1.7f, 0f);

            void Tag() { sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f); sun.intensity = 1.6f; sun.color = new Color(1f, 0.96f, 0.9f); RenderSettings.ambientIntensity = 1f; }
            void Daemmerung() { sun.transform.rotation = Quaternion.Euler(7f, -70f, 0f); sun.intensity = 0.7f; sun.color = new Color(1f, 0.62f, 0.38f); RenderSettings.ambientIntensity = 0.45f; }
            // Streiflicht: fast parallel zur Klingenfläche (Flächennormale −Z zur Kamera), ~5° Einfall
            void Streif() { sun.transform.rotation = Quaternion.Euler(8f, 85f, 0f); sun.intensity = 2.2f; sun.color = Color.white; RenderSettings.ambientIntensity = 0.25f; }
            void Bloom(bool on) { if (bloomAus != null) bloomAus.SetActive(!on); }
            bool upd = VolumeJeBild(cam);
            void Shot(string name, Vector3 target, Vector3 dir, float dist, float fov)
            {
                cam.fieldOfView = fov;
                cam.transform.position = target - dir.normalized * dist;
                cam.transform.LookAt(target);
                VolumeAktualisieren(cam);
                Render(cam, Path.Combine(outDir, name + ".png"), 1600, 1000);
            }
            Vector3 front = new Vector3(0.25f, -0.08f, 1f);   // auf die Klingenfläche (Klingenflächen zeigen ±Z in der Szene)
            Tag(); Bloom(false);
            Shot("00_aufwaermen", kB, front, 0.80f, 30f);   // erster Render nach dem Öffnen ist leer → verwerfen
            File.Delete(Path.Combine(outDir, "00_aufwaermen.png"));
            Shot("01_nah_bruch_kurz_tag_bloomaus", kB, front, 0.80f, 30f);
            Bloom(true);
            Shot("02_nah_bruch_kurz_tag_bloomein", kB, front, 0.80f, 30f);
            Bloom(false);
            Shot("01b_detail_spalt_kurz", kB, front, 0.32f, 30f);
            Shot("03_nah_bruch_gross_tag_bloomaus", gB, front, 1.05f, 30f);
            Shot("04_nah_griff_parier_kurz", kurz != null ? kurz.transform.position + Vector3.up * 0.02f : Vector3.up, front, 0.38f, 30f);
            Shot("05_gesamt_tag", new Vector3(0.2f, 1.05f, 0f), new Vector3(0.1f, -0.1f, 1f), 3.2f, 35f);
            Shot("06_spielkamera_tag", new Vector3(0f, 1.15f, 0f), new Vector3(0.35f, -0.45f, 1f), 4.2f, 55f);
            Shot("07_fern_tag", new Vector3(0f, 1.1f, 0f), new Vector3(0.3f, -0.25f, 1f), 14f, 55f);
            Daemmerung();
            Shot("08_nah_bruch_kurz_daemmerung_bloomaus", kB, front, 0.80f, 30f);
            Bloom(true);
            Shot("09_spielkamera_daemmerung_bloomein", new Vector3(0f, 1.15f, 0f), new Vector3(0.35f, -0.45f, 1f), 4.2f, 55f);
            Bloom(false);
            Streif();
            Shot("10_streiflicht_klinge_kurz", kB, front, 0.80f, 30f);
            // Streiflicht-Reihe: Lichtrichtung wandert um die Klinge (bewegliches Streiflicht als Bildfolge)
            int si = 0;
            foreach (float yaw in new[] { 60f, 30f, -30f, -60f })
            {
                sun.transform.rotation = Quaternion.Euler(8f, yaw, 0f);
                Shot($"10{(char)('a' + si++)}_streiflicht_{(yaw >= 0 ? "p" : "m")}{Mathf.Abs(yaw):F0}", kB, front, 0.55f, 30f);
            }
            Tag();
            Shot("11_kachelproben_3x3", KachelMitte, new Vector3(0f, -0.05f, 1f), 2.9f, 40f);
            Streif();
            Shot("12_kachelproben_streiflicht", KachelMitte, new Vector3(0f, -0.05f, 1f), 2.9f, 40f);
            Tag(); Bloom(false);
            // Diagnose: nur die Energie zeigen
            var alle = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
            foreach (var r in alle) r.enabled = r.gameObject.name.StartsWith("Energy");
            Shot("13_diagnose_nur_energie", kB, front, 0.8f, 30f);
            foreach (var r in alle) r.enabled = true;
            Bloom(true);
            var diag = new StringBuilder("\nDiagnose Energie:\n");
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude).Where(m => m.gameObject.name.StartsWith("Energy")))
            {
                var m = mf.sharedMesh; var r = mf.GetComponent<Renderer>();
                int nanN = m.normals.Count(n => float.IsNaN(n.x) || float.IsNaN(n.y) || float.IsNaN(n.z));
                int nanT = m.tangents.Count(t => float.IsNaN(t.x) || float.IsNaN(t.y) || float.IsNaN(t.z) || float.IsNaN(t.w));
                var mat = r.sharedMaterial;
                diag.AppendLine($"  {mf.transform.parent?.parent?.name}/{mf.name}: Verts {m.vertexCount}, Bounds {r.bounds.center} {r.bounds.size}, NaN-Normalen {nanN}, NaN-Tangenten {nanT}, enabled {r.enabled}, Layer {r.gameObject.layer}");
                diag.AppendLine($"    Material {mat?.name} Shader {mat?.shader?.name} BaseColor {mat?.GetColor("_BaseColor")} BaseMap {mat?.GetTexture("_BaseMap")?.name} Cull {mat?.GetFloat("_Cull")} Queue {mat?.renderQueue}");
            }

            foreach (var st in Sets)
            {
                var pm = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{st.mat}.mat");
                if (pm != null) diag.AppendLine($"  {st.mat}: NORMALMAP {pm.IsKeywordEnabled("_NORMALMAP")} METALLICSPECGLOSSMAP {pm.IsKeywordEnabled("_METALLICSPECGLOSSMAP")} OCCLUSIONMAP {pm.IsKeywordEnabled("_OCCLUSIONMAP")}");
            }
            File.WriteAllText(Path.Combine(outDir, "Bericht.txt"), Messung(kurz, gross) + $"\nVolume-Update je Bild: {(upd ? "ja" : "NEIN")}\n" + diag, Encoding.UTF8);
            Debug.Log($"[Ashveil] Nachhall: Prüfbilder und Bericht in {outDir}");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);   // Lichtänderungen verwerfen
        }

        // ------------------------------------------------------------------ Prüfbilder in der echten Kareth-v4-Beleuchtung
        private const string KarethScene = "Assets/_Ashveil/Scenes/Kareth_v4.unity";

        [MenuItem("Ashveil/Waffen/Nachhall/4 Prüfbilder in Kareth v4 (ohne Speichern)", priority = 703)]
        public static void PruefbilderKareth()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Ashveil] Nachhall: nicht im Play-Modus."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(KarethScene, OpenSceneMode.Single);
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Ashveil", "Nachhall", "Kareth"));
            Directory.CreateDirectory(outDir);
            var sb = new StringBuilder("Nachhall in Kareth v4 – Szene wird NICHT gespeichert\n");
            try
            {
                // ebene, freie Bodenstelle nahe Untersteg (falls vorhanden), sonst Geländemitte; nur Terrain-Treffer zählen
                var terr = Terrain.activeTerrain;
                Vector3 anchor = terr.transform.position + new Vector3(terr.terrainData.size.x * 0.5f, 0f, terr.terrainData.size.z * 0.5f);
                var ort = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude).Where(t => t.name.Contains("Untersteg"))
                    .OrderBy(t => t.GetHierarchyPath()).FirstOrDefault();   // stabile Reihenfolge → immer dieselbe Stelle
                if (ort != null) anchor = ort.position + new Vector3(18f, 0f, -14f);
                Vector3 spot = Vector3.zero; bool ok = false;
                for (int i = 0; i < 400 && !ok; i++)
                {
                    float a = i * 2.4f, r = 1.5f * Mathf.Sqrt(i);
                    Vector3 q = anchor + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    if (!Physics.Raycast(new Vector3(q.x, 5000f, q.z), Vector3.down, out RaycastHit hit, 10000f)) continue;
                    if (!(hit.collider is TerrainCollider) || Vector3.Angle(hit.normal, Vector3.up) > 8f) continue;
                    // 3 m Umkreis frei von Nicht-Terrain-Collidern
                    if (Physics.OverlapSphere(hit.point + Vector3.up * 1.6f, 3f).Any(c => !(c is TerrainCollider))) continue;
                    spot = hit.point; ok = true;
                }
                if (!ok) { sb.AppendLine("Keine freie ebene Stelle gefunden."); return; }
                sb.AppendLine($"Stelle: {spot}");
                var root = new GameObject("Nachhall_Pruefung (nicht speichern)");
                float x = -0.25f;
                foreach (string g in Groessen)
                {
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Nachhall_{g}.prefab");
                    var sw = (GameObject)PrefabUtility.InstantiatePrefab(pf, root.transform);
                    sw.transform.SetPositionAndRotation(spot + new Vector3(x, 1.0f, 0f), Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, 0f, 90f));
                    x += 0.5f;
                }
                var off = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{MatDir}/Nachhall_Test_BloomAus.asset");
                var volOff = new GameObject("BloomAus").AddComponent<Volume>();
                volOff.transform.SetParent(root.transform);
                volOff.isGlobal = true; volOff.priority = 100; volOff.sharedProfile = off;
                var cam = new GameObject("Pruefkamera").AddComponent<Camera>();
                cam.transform.SetParent(root.transform);
                cam.nearClipPlane = 0.02f; cam.farClipPlane = 4000f;
                var main = Camera.main;
                if (main != null) { cam.clearFlags = main.clearFlags; cam.backgroundColor = main.backgroundColor; }
                var cd = cam.GetUniversalAdditionalCameraData();
                cd.renderPostProcessing = true;
                cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                bool upd = VolumeJeBild(cam);
                var kurz = root.transform.GetChild(0).gameObject; var gross = root.transform.GetChild(1).gameObject;
                Vector3 kB = BruchMitte(kurz), gB = BruchMitte(gross);
                Vector3 front = new Vector3(0.25f, -0.08f, 1f);
                void Shot(string name, Vector3 target, Vector3 dir, float dist, float fov, bool bloom)
                {
                    volOff.gameObject.SetActive(!bloom);
                    cam.fieldOfView = fov;
                    cam.transform.position = target - dir.normalized * dist;
                    cam.transform.LookAt(target);
                    VolumeAktualisieren(cam);
                    Render(cam, Path.Combine(outDir, name + ".png"), 1600, 1000);
                }
                Shot("00_aufwaermen", kB, front, 0.8f, 30f, false); File.Delete(Path.Combine(outDir, "00_aufwaermen.png"));
                Shot("01_nah_bruch_kurz_bloomaus", kB, front, 0.8f, 30f, false);
                Shot("02_nah_bruch_kurz_bloomein", kB, front, 0.8f, 30f, true);
                Shot("03_detail_spalt_kurz_bloomaus", kB, front, 0.32f, 30f, false);
                Shot("04_nah_bruch_gross_bloomaus", gB, front, 1.05f, 30f, false);
                Shot("05_griff_parier_kurz", kurz.transform.position + Vector3.up * 0.02f, front, 0.38f, 30f, true);
                Shot("06_gegen_himmel", spot + new Vector3(0f, 1.45f, 0f), new Vector3(0.2f, 0.28f, 1f), 1.6f, 40f, true);
                Shot("07_gegen_boden", spot + new Vector3(0f, 1.2f, 0f), new Vector3(0.2f, -0.7f, 1f), 1.8f, 40f, true);
                Shot("08_spielkamera", spot + new Vector3(0f, 1.15f, 0f), new Vector3(0.35f, -0.45f, 1f), 4.2f, 55f, true);
                Shot("09_spielkamera_rueckseite", spot + new Vector3(0f, 1.15f, 0f), new Vector3(-0.35f, -0.45f, -1f), 4.2f, 55f, true);
                Shot("10_fern", spot + new Vector3(0f, 1.1f, 0f), new Vector3(-0.3f, -0.3f, -1f), 14f, 55f, true);
                sb.AppendLine($"Volume-Update je Bild: {(upd ? "ja" : "NEIN")}");
                var sun = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).FirstOrDefault(l => l.type == LightType.Directional);
                if (sun != null) sb.AppendLine($"Sonne: {sun.name} Intensität {sun.intensity:F2} Richtung {sun.transform.forward}");
            }
            finally
            {
                File.WriteAllText(Path.Combine(outDir, "Bericht_Kareth.txt"), sb.ToString(), Encoding.UTF8);
                EditorSceneManager.OpenScene(KarethScene, OpenSceneMode.Single);   // Änderungen verwerfen, Kareth sauber offen lassen
                Debug.Log($"[Ashveil] Nachhall: Kareth-Prüfbilder in {outDir} (Szene unverändert)");
            }
        }

        // ------------------------------------------------------------------ Griffvermessung mit dem Projektwerkzeug
        [MenuItem("Ashveil/Waffen/Nachhall/5 Griffvermessung (WeaponGrip)", priority = 704)]
        public static void Griffvermessung()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Ashveil", "Nachhall"));
            Directory.CreateDirectory(outDir);
            var sb = new StringBuilder("Nachhall – Griffvermessung mit WeaponGrip.Measure (Projektcode, isolierte Vorschauszene)\n");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (float hand in new[] { 0.080f, 0.090f })
                    foreach (string g in Groessen)
                    {
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Nachhall_{g}.prefab");
                        if (pf == null) { sb.AppendLine($"{g}: Prefab fehlt"); continue; }
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf, scene);
                        // wie AppearanceRig.PlaceWeapons am Nahkampf-Anker: Euler(0,0,90)
                        var pivot = new GameObject("Pivot"); SceneManager.MoveGameObjectToScene(pivot, scene);
                        inst.transform.SetParent(pivot.transform, false);
                        inst.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                        foreach (var style in new[] { GripStyle.OneHand, GripStyle.TwoHandBlade })
                        {
                            var gi = WeaponGrip.Measure(pivot.transform, style, hand);
                            sb.AppendLine($"{g,-13} Handbreite {hand * 100f:F0} cm  {style,-13} gültig {gi.Valid}  Griff {gi.HandleMin:F3}…{gi.HandleMax:F3} m (Länge {(gi.HandleMax - gi.HandleMin) * 100f:F1} cm)  Radius {gi.Radius * 1000f:F1} mm  " +
                                          $"rechte Faust z {gi.Right.z:F3}" + (gi.HasLeft ? $"  linke Faust z {gi.Left.z:F3} (Abstand {(gi.Right.z - gi.Left.z) * 100f:F1} cm)" : "") + $"  {gi.Note}");
                        }
                        Object.DestroyImmediate(pivot);
                    }
                sb.AppendLine("\nVorgaben Auftrag (Vorschlag): Kurzschwert nutzbarer Griff 12–15 cm einhändig; Großschwert 27–32 cm zweihändig.");
                sb.AppendLine("Spielklasse laut WeaponPoses.StyleOf: Sword → OneHand, TwoHandedSword → TwoHandBlade.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                File.WriteAllText(Path.Combine(outDir, "Griff.txt"), sb.ToString(), Encoding.UTF8);
                Debug.Log("[Ashveil] Nachhall: Griffvermessung in Logs/Ashveil/Nachhall/Griff.txt");
            }
        }

        // URP aktualisiert Volumes je nach Version/Einstellung nur einmal pro Frame. Für mehrere Bilder im selben
        // Editor-Frame: Kamera auf „per Skript“ stellen und vor jedem Bild aktualisieren – per Reflection, damit eine
        // abweichende URP-API nie die Kompilierung bricht.
        private static System.Reflection.MethodInfo _updStack;
        private static bool VolumeJeBild(Camera cam)
        {
            var cd = cam.GetUniversalAdditionalCameraData();
            foreach (var pr in cd.GetType().GetProperties())
                if (pr.Name.IndexOf("volumeFrameworkUpdate", System.StringComparison.OrdinalIgnoreCase) >= 0 && pr.CanWrite && pr.PropertyType.IsEnum)
                {
                    var names = System.Enum.GetNames(pr.PropertyType);
                    string via = names.FirstOrDefault(n => n.IndexOf("Script", System.StringComparison.OrdinalIgnoreCase) >= 0);
                    if (via != null) pr.SetValue(cd, System.Enum.Parse(pr.PropertyType, via));
                }
            var ext = System.Type.GetType("UnityEngine.Rendering.Universal.CameraExtensions, Unity.RenderPipelines.Universal.Runtime");
            _updStack = ext?.GetMethod("UpdateVolumeStack", new[] { typeof(Camera) });
            return _updStack != null;
        }

        private static void VolumeAktualisieren(Camera cam)
        {
            // Nur wenn der VolumeManager schon initialisiert ist (sonst meldet URP einen Fehler beim ersten Bild)
            var vm = VolumeManager.instance;
            var init = vm.GetType().GetProperty("isInitialized");
            if (init != null && !(bool)init.GetValue(vm)) return;
            if (_updStack != null) _updStack.Invoke(null, new object[] { cam });
            else VolumeManager.instance.Update(cam.transform, ~0);
        }

        private static string GetHierarchyPath(this Transform t) => t.parent == null ? t.name : t.parent.GetHierarchyPath() + "/" + t.name;

        private static Vector3 BruchMitte(GameObject sword)
        {
            // Mitte der Fragmente (Weltraum); Fallback Klingenmitte
            var rs = sword.GetComponentsInChildren<Renderer>().Where(r => r.gameObject.name.StartsWith("Blade_Fragment")).ToArray();
            if (rs.Length == 0) return sword.transform.position + Vector3.up * 0.4f;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.center;
        }

        private static void Render(Camera cam, string path, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            var act = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = act; cam.targetTexture = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        }

        private static string Messung(GameObject kurz, GameObject gross)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Nachhall Rev. E – Messung (Unity, Editor) " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            foreach (var go in new[] { kurz, gross })
            {
                if (go == null) { sb.AppendLine("Prefab fehlt in der Szene."); continue; }
                int tris = 0; var b = new Bounds(go.transform.position, Vector3.zero);
                sb.AppendLine($"\n{go.name}");
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    var m = mf.sharedMesh; if (m == null) continue;
                    int t = 0; for (int i = 0; i < m.subMeshCount; i++) t += (int)m.GetIndexCount(i) / 3;
                    tris += t;
                    var r = mf.GetComponent<Renderer>(); b.Encapsulate(r.bounds);
                    sb.AppendLine($"  {mf.gameObject.name,-22} {t,6} Dreiecke  Materialien: {string.Join(", ", r.sharedMaterials.Select(x => x != null ? x.name : "FEHLT"))}");
                }
                sb.AppendLine($"  Summe {tris} Dreiecke, Hülle {b.size.x:F3} × {b.size.y:F3} × {b.size.z:F3} m");
            }
            sb.AppendLine("\nTexturspeicher (zur Laufzeit, Editor-Messung):");
            long sum = 0;
            var seen = new HashSet<Texture>();
            foreach (string mn in Sets.Select(s => s.mat).Append("Nachhall_Energy"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{mn}.mat");
                if (m == null) { sb.AppendLine($"  {mn}: Material fehlt"); continue; }
                foreach (string prop in m.GetTexturePropertyNames())
                {
                    var t = m.GetTexture(prop);
                    if (t == null || !seen.Add(t)) continue;
                    long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                    sum += bytes;
                    var t2 = t as Texture2D;
                    sb.AppendLine($"  {mn,-20} {prop,-18} {t.name,-46} {t.width}×{t.height} {(t2 != null ? t2.format.ToString() : "")} {bytes / 1048576f:F2} MB");
                }
            }
            sb.AppendLine($"  Summe (geteilte Texturen einmal gezählt): {sum / 1048576f:F1} MB");
            return sb.ToString();
        }
    }
}
