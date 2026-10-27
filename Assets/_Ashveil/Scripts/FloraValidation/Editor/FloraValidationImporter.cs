using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Ashveil.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ashveil.FloraValidation.EditorTools
{
    /// <summary>
    /// Importiert die externen Flora-v2-Testdaten (SourceArt/FloraValidation_v2: manifest.json, meshes/*.fvm.gz, textures/*.png)
    /// als Texturen, Meshes, Diagnose-Materialien und LOD-Prefabs nach Assets/_Ashveil/Art/FloraValidation.
    /// Nur fuer die isolierte Flora-Validation – keine Verwendung in Kareth, Weltgenerator oder Spielszenen.
    /// Menue: Ashveil › Flora-Validation › 1 Assets importieren.
    /// </summary>
    public static class FloraValidationImporter
    {
        public const string ArtRoot = "Assets/_Ashveil/Art/FloraValidation";
        public const string TexDir = ArtRoot + "/Textures";
        public const string MeshDir = ArtRoot + "/Meshes";
        public const string MatDir = ArtRoot + "/Materials";
        public const string PrefabDir = ArtRoot + "/Prefabs";
        public const string LitShader = "Ashveil/FloraValidation/FloraLit";
        public const string ImpostorShader = "Ashveil/FloraValidation/FloraImpostor";

        public static string SourceDir => ProjectPaths.SourceArt("FloraValidation_v2");

        // ------------------------------------------------------------------ Manifest (gleiches Format wie unity_export.py)

        [Serializable] public class MatDef
        {
            public string name, kind, albedo, normal, normalAtlas;
            public float[] color; public float rough, nscale, transl, octGain; public bool alpha, doubleSided; public int octGrid; public float[] octBox; public float[] octExt; public float octAO; public float[] crown;
        }
        [Serializable] public class AssetDef { public string name, file; public MatDef[] materials; public int tris, verts; }
        [Serializable] public class PrefabDef { public string name, family, kind, note; public string[] lods; }
        [Serializable] public class Manifest { public string version; public AssetDef[] assets; public PrefabDef[] prefabs; public float[] lodDistances; public string[] textures; }

        public static Manifest LoadManifest()
        {
            string p = Path.Combine(SourceDir, "manifest.json");
            if (!File.Exists(p)) throw new FileNotFoundException("Flora-Testdaten fehlen: " + p);
            return JsonUtility.FromJson<Manifest>(File.ReadAllText(p));
        }

        [MenuItem("Ashveil/Flora-Validation/1 Assets importieren", priority = 1)]
        public static void ImportMenu()
        {
            try { Import(); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        public const string CalibFile = "calibration_v3.json";

        /// <summary>v3-Kalibrierung (aus dem Unity-Kalibrierlauf), sonst null = v2-Grundzustand fuer den Kalibrierlauf.</summary>
        public static FloraCalibrationData LoadCalibration()
        {
            string p = Path.Combine(SourceDir, CalibFile);
            return File.Exists(p) ? JsonUtility.FromJson<FloraCalibrationData>(File.ReadAllText(p)) : null;
        }

        private static FloraCalibrationData _cal;

        /// <summary>1024er-Kopie eines v3-Impostor-Atlas (Speichervergleich 1024 vs. 2048, BC7/BC1).</summary>
        private static string K1(string file) => Path.GetFileNameWithoutExtension(file) + "_1k.png";
        private static bool IsOct3(string file) => Path.GetFileName(file).StartsWith("oct3_") || Path.GetFileName(file).StartsWith("oct4_");

        public static void Import()
        {
            Manifest man = LoadManifest();
            _cal = LoadCalibration();
            Debug.Log("[FloraValidation] Kalibrierung: " + (_cal != null ? $"vorhanden ({_cal.created})" : "keine (v2-Grundzustand)"));
            var texList = new List<string>(man.textures);
            foreach (string t in man.textures) if (IsOct3(t)) texList.Add(K1(t));
            foreach (string d in new[] { TexDir, MeshDir, MatDir, PrefabDir }) EnsureFolder(d);
            float t0 = Time.realtimeSinceStartup;

            // 1) Texturen kopieren und Importeinstellungen setzen
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string tex in texList)
                {
                    string src = Path.Combine(SourceDir, "textures", tex.EndsWith("_1k.png") ? tex.Replace("_1k.png", ".png") : tex);
                    string dst = TexPath(tex);
                    File.Copy(src, Path.Combine(ProjectPaths.ProjectRoot, dst), true);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            int ti = 0;
            foreach (string tex in texList)
            {
                EditorUtility.DisplayProgressBar("Flora-Validation", "Textur " + tex, 0.2f * ti++ / texList.Count);
                ConfigureTexture(tex);
            }

            // 2) Meshes + Materialien
            var meshes = new Dictionary<string, Mesh>();
            var mats = new Dictionary<string, Material[]>();
            var mats1k = new Dictionary<string, Material[]>();
            var matCache = new Dictionary<string, Material>();
            int ai = 0;
            foreach (AssetDef a in man.assets)
            {
                EditorUtility.DisplayProgressBar("Flora-Validation", "Mesh " + a.name, 0.2f + 0.6f * ai++ / man.assets.Length);
                Mesh m = ReadMesh(a);
                string mp = $"{MeshDir}/SM_FV_{a.name}.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(mp) != null) AssetDatabase.DeleteAsset(mp);
                AssetDatabase.CreateAsset(m, mp);
                var so = new SerializedObject(m);
                SerializedProperty rd = so.FindProperty("m_IsReadable");
                if (rd != null) { rd.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
                meshes[a.name] = m;
                var list = new Material[a.materials.Length];
                for (int i = 0; i < list.Length; i++) list[i] = GetMaterial(a.materials[i], matCache, a.name, false);
                mats[a.name] = list;
                if (a.name.EndsWith("_vfar3") || a.name.EndsWith("_vfar4"))
                {
                    var l1 = new Material[a.materials.Length];
                    for (int i = 0; i < l1.Length; i++) l1[i] = GetMaterial(a.materials[i], matCache, a.name, true);
                    mats1k[a.name] = l1;
                }
            }
            // Kalibrierkandidaten bekommen immer den unkalibrierten Grundzustand (sonst wuerde ein erneuter Kalibrierlauf
            // relative Farbfaktoren doppelt anwenden).
            var matsRaw = new Dictionary<string, Material[]>(); var mats1kRaw = new Dictionary<string, Material[]>();
            {
                var calSave = _cal; _cal = null;
                var calibAssets = new HashSet<string>();
                foreach (PrefabDef p in man.prefabs) if (p.kind == "calib") calibAssets.Add(p.lods[0]);
                foreach (AssetDef a in man.assets)
                {
                    if (!calibAssets.Contains(a.name)) continue;
                    var l = new Material[a.materials.Length];
                    for (int i = 0; i < l.Length; i++) l[i] = GetMaterial(a.materials[i], matCache, a.name, false);
                    matsRaw[a.name] = l;
                    if (a.name.EndsWith("_vfar3") || a.name.EndsWith("_vfar4"))
                    {
                        var l1 = new Material[a.materials.Length];
                        for (int i = 0; i < l1.Length; i++) l1[i] = GetMaterial(a.materials[i], matCache, a.name, true);
                        mats1kRaw[a.name] = l1;
                    }
                }
                _cal = calSave;
            }
            AssetDatabase.SaveAssets();

            // 3) Prefabs mit LODGroup
            int pi = 0;
            foreach (PrefabDef p in man.prefabs)
            {
                EditorUtility.DisplayProgressBar("Flora-Validation", "Prefab " + p.name, 0.8f + 0.2f * pi++ / man.prefabs.Length);
                if (p.kind == "calib") BuildPrefab(p, meshes, matsRaw, mats1kRaw);
                else BuildPrefab(p, meshes, mats, mats1k);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[FloraValidation] Import fertig: {man.textures.Length} Texturen, {man.assets.Length} Meshes, {matCache.Count} Materialien, {man.prefabs.Length} Prefabs ({Time.realtimeSinceStartup - t0:0.0} s). LOD-Bias {QualitySettings.lodBias:0.00}");
        }

        // ------------------------------------------------------------------ Texturen

        public static string TexPath(string file) => $"{TexDir}/T_FV_{Path.GetFileNameWithoutExtension(file)}.png";

        private static void ConfigureTexture(string file)
        {
            string path = TexPath(file);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[FloraValidation] Textur nicht importiert: " + path); return; }
            string n = Path.GetFileNameWithoutExtension(file);
            bool k1 = n.EndsWith("_1k"); if (k1) n = n.Substring(0, n.Length - 3);
            bool oct3 = n.StartsWith("oct3_") || n.StartsWith("oct4_");
            bool oct4 = n.StartsWith("oct4_");
            bool octNormal = n.StartsWith("oct") && n.EndsWith("_normal");
            bool octAlbedo = n.StartsWith("oct") && n.EndsWith("_albedo");
            bool normal = !octNormal && n.EndsWith("_n");
            bool clamp = n.StartsWith("leaf_") || n.StartsWith("needle_") || n.StartsWith("ivy_") || n.StartsWith("f10_card") || n.StartsWith("oct");
            bool cutout = n.StartsWith("leaf_") || n.StartsWith("needle_") || n.StartsWith("ivy_") || n.StartsWith("f10_card") || n.StartsWith("bark_shell") || octAlbedo;

            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !(normal || octNormal);
            ti.mipmapEnabled = true;
            ti.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 4;
            ti.maxTextureSize = 4096;
            ti.alphaSource = (cutout || (oct4 && octNormal)) ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;   // v4: AO im Alpha des Normal-Atlas
            ti.alphaIsTransparency = cutout;
            ti.mipMapsPreserveCoverage = cutout;
            ti.alphaTestReferenceValue = octAlbedo ? 0.42f : 0.5f;
            ti.textureCompression = TextureImporterCompression.Compressed;
            // v3-Impostor: 1024er-Kopie immer BC7/BC1; 2048 nur, wenn die Kalibrierung BC verlangt. Sonst Standardkompression.
            bool bc = oct3 && (k1 || (_cal != null && _cal.impostorBc));
            if (k1) ti.maxTextureSize = 1024;
            var ps = ti.GetPlatformTextureSettings("Standalone");
            ps.overridden = bc;
            if (bc) { ps.maxTextureSize = k1 ? 1024 : 2048; ps.format = (octNormal && !oct4) ? TextureImporterFormat.DXT1 : TextureImporterFormat.BC7; }
            ti.SetPlatformTextureSettings(ps);
            ti.SaveAndReimport();
        }

        // ------------------------------------------------------------------ Meshes (FVM1)

        private static Mesh ReadMesh(AssetDef a)
        {
            string p = Path.Combine(SourceDir, a.file);
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>();
            var wind = new List<Vector4>(); var season = new List<Vector4>();
            var subs = new List<int[]>(); var subMat = new List<int>();
            using (var fs = File.OpenRead(p))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var br = new BinaryReader(gz))
            {
                byte[] magic = br.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != 'F' || magic[1] != 'V' || magic[2] != 'M' || magic[3] != '1') throw new Exception("Kein FVM1: " + p);
                int nSub = br.ReadInt32();
                for (int s = 0; s < nSub; s++)
                {
                    int mat = br.ReadInt32(), nv = br.ReadInt32(), ni = br.ReadInt32();
                    int baseV = pos.Count;
                    for (int i = 0; i < nv; i++) pos.Add(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                    for (int i = 0; i < nv; i++) nrm.Add(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                    for (int i = 0; i < nv; i++) uv.Add(new Vector2(br.ReadSingle(), br.ReadSingle()));
                    for (int i = 0; i < nv; i++) col.Add(new Color(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                    for (int i = 0; i < nv; i++) wind.Add(new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                    for (int i = 0; i < nv; i++) season.Add(new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                    var idx = new int[ni];
                    for (int i = 0; i < ni; i++) idx[i] = (int)br.ReadUInt32() + baseV;
                    subs.Add(idx); subMat.Add(mat);
                }
            }
            var m = new Mesh { name = "SM_FV_" + a.name };
            m.indexFormat = pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(pos); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetColors(col); m.SetUVs(2, wind); m.SetUVs(3, season);
            m.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) m.SetTriangles(subs[s], s, false);
            // Submesh-Reihenfolge = Materialindex im Manifest
            if (subMat.Count != a.materials.Length || !IsIdentity(subMat))
            {
                // Materialien nach Submesh-Reihenfolge umsortieren
                var mm = new MatDef[subMat.Count];
                for (int s = 0; s < subMat.Count; s++) mm[s] = a.materials[Mathf.Clamp(subMat[s], 0, a.materials.Length - 1)];
                a.materials = mm;
            }
            bool impostor = a.materials.Length > 0 && a.materials[0].kind == "impostor";
            if (impostor)
            {
                float[] b = a.materials[0].octBox;
                var c = new Vector3(-b[0], b[1], b[2]);
                m.bounds = new Bounds(c, Vector3.one * (2f * b[3]));
            }
            else
            {
                m.RecalculateBounds();
                m.RecalculateTangents();
            }
            return m;
        }

        private static bool IsIdentity(List<int> l)
        {
            for (int i = 0; i < l.Count; i++) if (l[i] != i) return false;
            return true;
        }

        // ------------------------------------------------------------------ Materialien (PROTOTYPE / DIAGNOSTIC)

        private static bool IsFoliage(string kind) =>
            kind == "leaf" || kind == "needle" || kind == "grass" || kind == "grasscard" || kind == "ivyleaf" || kind == "shrubleaf";

        private static string Family(string asset) =>
            asset.StartsWith("f04a") ? "f04a" : asset.StartsWith("f01") ? "f01" : asset.StartsWith("f10") ? "f10" : "";

        private static bool IsCardKind(string kind) =>
            kind == "leaf" || kind == "needle" || kind == "ivyleaf" || kind == "shrubleaf" || kind == "grasscard";

        private static Material GetMaterial(MatDef d, Dictionary<string, Material> cache, string asset, bool tex1k)
        {
            bool imp = d.kind == "impostor";
            // v3-Kalibrierwerte (nur wenn calibration_v3.json vorhanden)
            float mip = 0f; float cutoff = imp ? 0.42f : 0.5f; float gain = d.octGain; Vector3 tint = Vector3.one; string ov = "";
            float nBlend = 0f, impTransl = -1f; Vector4 nSign = new Vector4(-1, 1, 1, 0);
            float aoS = 1f, aoD = 0f, bSharp = 1f, impSm = -1f, macro = 0f, thick = 0f, selfK = 0f, sheen = 0f;
            if (_cal != null)
            {
                string fam = Family(asset);
                if (d.alpha && IsCardKind(d.kind)) mip = fam == "f01" ? _cal.mipF01 : fam == "f04a" ? _cal.mipF04a : fam == "f10" ? _cal.mipF10 : 0f;
                if (imp)
                    foreach (ImpostorCal ic in _cal.impostors)
                        if (ic.asset == asset) { cutoff = ic.cutoff; gain = ic.gain; mip = ic.mipAlpha; nBlend = ic.normalBlend; impTransl = ic.translucency; nSign = new Vector4(ic.signX, 1, ic.signZ, 0);
                            aoS = ic.aoStrength; aoD = ic.aoDirect; bSharp = Mathf.Max(1f, ic.blendSharp); impSm = ic.smoothness; macro = ic.macroBlend; thick = ic.thickK; selfK = ic.selfK; sheen = ic.sheen; ov = "|imp:" + asset; }
                foreach (TintCal tc in _cal.tints)
                    if (tc.asset == asset && tc.material == d.kind)
                    {
                        tint = new Vector3(tc.r, tc.g, tc.b); if (tc.cutoff > 0) cutoff = tc.cutoff; ov += "|tint:" + asset;
                    }
            }
            string key = (imp ? "imp|" + d.albedo + (tex1k ? "|1k" : "")
                : $"{d.kind}|{d.albedo}|{d.normal}|{d.alpha}|{d.doubleSided}|{d.transl:0.###}|{d.nscale:0.###}|{d.rough:0.###}|{Col(d)}")
                + $"|mip{mip:0.###}" + ov;
            if (cache.TryGetValue(key, out Material m)) return m;

            string baseName = imp ? "M_FV_Impostor_" + Path.GetFileNameWithoutExtension(d.albedo).Replace("oct3_", "v3_").Replace("oct4_", "v4_").Replace("oct_", "").Replace("_albedo", "") + (tex1k ? "_1k" : "") + (ov.Contains("imp:") ? "_kal" : "")
                : "M_FV_" + d.kind + (string.IsNullOrEmpty(d.albedo) ? "_vc" : "_" + Path.GetFileNameWithoutExtension(d.albedo))
                  + (ov.Contains("tint:") ? "_" + asset.Replace("f10_group_", "") : "") + (mip > 0 ? "_mip" : "");
            string name = baseName; int k = 2;
            foreach (Material other in cache.Values) if (other.name == name) name = baseName + "_" + k++;
            string path = $"{MatDir}/{name}.mat";

            Shader sh = Shader.Find(imp ? ImpostorShader : LitShader);
            if (sh == null) throw new Exception("Shader fehlt: " + (imp ? ImpostorShader : LitShader));
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else m.shader = sh;
            m.name = name;
            m.enableInstancing = true;

            if (imp)
            {
                m.SetTexture("_BaseMap", tex1k ? Tex(K1(d.albedo)) : Tex(d.albedo));
                m.SetTexture("_NormalAtlas", tex1k ? Tex(K1(d.normalAtlas)) : Tex(d.normalAtlas));
                m.SetVector("_OctBox", new Vector4(-d.octBox[0], d.octBox[1], d.octBox[2], d.octBox[3]));
                m.SetVector("_OctExt", d.octExt != null && d.octExt.Length >= 3 ? new Vector4(d.octExt[0], d.octExt[1], d.octExt[2], 0) : Vector4.zero);
                m.SetFloat("_OctGrid", d.octGrid);
                m.SetFloat("_OctGain", gain);
                m.SetFloat("_NormalBlend", nBlend);
                m.SetVector("_NormalSign", nSign);
                m.SetFloat("_OctAO", d.octAO);
                m.SetFloat("_AOStrength", aoS);
                m.SetFloat("_AODirect", aoD);
                m.SetFloat("_OctBlendSharp", bSharp);
                // Micro-Pass: Kronen-Grossnormale (Ellipsoid aus der FAR-Belaubung) und Dicke fuer die Transluzenz
                if (d.crown != null && d.crown.Length >= 6)
                {
                    m.SetVector("_CrownC", new Vector4(d.crown[0], d.crown[1], d.crown[2], 1));
                    m.SetVector("_CrownR", new Vector4(d.crown[3], d.crown[4], d.crown[5], 0));
                }
                m.SetFloat("_MacroBlend", macro);
                m.SetFloat("_ThickK", thick);
                m.SetFloat("_SelfK", selfK);
                m.SetFloat("_Sheen", sheen);
                m.SetFloat("_Cutoff", cutoff);
                m.SetFloat("_MipAlpha", mip);
                // v4b: Kalibrierwerte NACH den Standardwerten setzen (v3 ueberschrieb die kalibrierte Transluzenz mit 0,18)
                m.SetFloat("_Smoothness", impSm >= 0f ? impSm : Mathf.Clamp01(1f - d.rough));
                m.SetFloat("_Translucency", impTransl >= 0f ? impTransl : 0.18f);
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                m.SetTexture("_BaseMap", Tex(d.albedo));
                Color bc0 = Col(d);
                m.SetColor("_BaseColor", new Color(bc0.r * tint.x, bc0.g * tint.y, bc0.b * tint.z, bc0.a));
                m.SetFloat("_MipAlpha", mip);
                Texture2D n = Tex(d.normal);
                m.SetTexture("_BumpMap", n);
                m.SetFloat("_UseNormalMap", n != null ? 1f : 0f);
                if (n != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
                m.SetFloat("_BumpScale", d.nscale);
                // Foliage: Smoothness begrenzt (Diagnosematerial ohne Spekular-Okklusion, sonst Himmels-Glanz auf Blattkarten)
                m.SetFloat("_Smoothness", Mathf.Clamp(1f - d.rough, 0f, IsFoliage(d.kind) ? 0.3f : 1f));
                m.SetFloat("_AlphaClip", d.alpha ? 1f : 0f);
                if (d.alpha) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cutoff", cutoff);
                m.SetFloat("_Cull", d.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
                m.SetFloat("_Translucency", d.transl);
                m.SetFloat("_Foliage", IsFoliage(d.kind) ? 1f : 0f);
                m.SetFloat("_WindScale", 1f);
                m.renderQueue = d.alpha ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            }
            EditorUtility.SetDirty(m);
            cache[key] = m;
            return m;
        }

        private static Color Col(MatDef d) =>
            d.color != null && d.color.Length >= 3 ? new Color(d.color[0], d.color[1], d.color[2], d.color.Length > 3 ? d.color[3] : 1f) : Color.white;

        private static Texture2D Tex(string file) => string.IsNullOrEmpty(file) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath(file));

        // ------------------------------------------------------------------ Prefabs

        /// <summary>LOD-Umschaltabstaende in Metern (bei Bildwinkel 60°, aktueller LOD-Bias eingerechnet). Letzter Wert = Ausblenden.</summary>
        public static float[] LodDistances(PrefabDef p)
        {
            switch (p.kind)
            {
                case "tree": return new[] { 35f, 80f, 160f, 1500f };
                case "f10group": return new[] { 18f, 50f, 130f };
                case "f19": return new[] { 12f, 30f, 150f };
            }
            string n = p.name;
            if (n.EndsWith("_hero")) return new[] { 130f };
            if (n.StartsWith("f19")) return new[] { 150f };
            if (n.StartsWith("f10_jung")) return new[] { 50f };
            if (n.StartsWith("f10_")) return new[] { 80f };
            if (n.StartsWith("dead_")) return new[] { 250f };
            return new[] { 200f };
        }

        private static readonly string[] TreeLodNames = { "NEAR", "MID", "FAR", "VFAR" };

        /// <summary>v3: LOD-Kette gemaess Kalibrierung (F04a FAR v3, Impostor v3, F10 LOD0-Variante).</summary>
        private static string[] ResolveLods(PrefabDef p, Dictionary<string, Mesh> meshes)
        {
            var l = (string[])p.lods.Clone();
            if (_cal == null) return l;
            if (p.kind == "tree")
            {
                if (_cal.useFar4 && p.family == "f04a" && meshes.ContainsKey(l[2] + "4")) l[2] = l[2] + "4";
                else if (_cal.useFar3 && p.family == "f04a" && meshes.ContainsKey(l[2] + "3")) l[2] = l[2] + "3";
                if (_cal.useVfar4 && meshes.ContainsKey(l[3] + "4")) l[3] = l[3] + "4";
                else if (_cal.useVfar3 && meshes.ContainsKey(l[3] + "3")) l[3] = l[3] + "3";
            }
            if (p.kind == "f10group" && !string.IsNullOrEmpty(_cal.f10Lod0))
            {
                string v = l[0] + _cal.f10Lod0;
                if (meshes.ContainsKey(v)) l[0] = v;
            }
            return l;
        }

        private static void BuildPrefab(PrefabDef p, Dictionary<string, Mesh> meshes, Dictionary<string, Material[]> mats, Dictionary<string, Material[]> mats1k)
        {
            if (p.kind == "calib")
            {
                BuildCalib(p.name, p.lods[0], meshes, mats);
                if (mats1k.ContainsKey(p.lods[0])) BuildCalib(p.name + "k", p.lods[0], meshes, mats1k);
                return;
            }
            var root = new GameObject(p.name);
            try
            {
                float[] dist = LodDistances(p);
                string[] lodNames = ResolveLods(p, meshes);
                var lods = new LOD[lodNames.Length];
                Bounds b0 = default;
                bool use1k = _cal != null && _cal.impostorMaxSize <= 1024;
                for (int i = 0; i < lodNames.Length; i++)
                {
                    string an = lodNames[i];
                    if (!meshes.TryGetValue(an, out Mesh mesh)) throw new Exception($"Prefab {p.name}: Mesh {an} fehlt");
                    string ln = p.kind == "tree" ? TreeLodNames[Mathf.Min(i, 3)] : "LOD" + i;
                    var go = new GameObject($"LOD{i}_{ln}");
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterials = use1k && mats1k.ContainsKey(an) ? mats1k[an] : mats[an];
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.receiveShadows = true;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    if (i == 0) b0 = mesh.bounds;
                    lods[i] = new LOD(0f, new Renderer[] { r });
                }
                // Groesse aus NEAR/LOD0 (der Impostor-Quad wuerde die Groesse verfaelschen)
                float size = Mathf.Max(b0.size.x, Mathf.Max(b0.size.y, b0.size.z));
                float k = 2f * Mathf.Tan(30f * Mathf.Deg2Rad);
                float prev = 1f;
                for (int i = 0; i < lods.Length; i++)
                {
                    float d = dist[Mathf.Min(i, dist.Length - 1)];
                    if (i == lods.Length - 1) d = dist[dist.Length - 1];
                    float h = Mathf.Clamp(size / (d * k), 0.0001f, 0.999f);
                    if (h >= prev) h = prev * 0.9f;
                    lods[i].screenRelativeTransitionHeight = h;
                    lods[i].fadeTransitionWidth = 0.15f;
                    prev = h;
                }
                var g = root.AddComponent<LODGroup>();
                g.fadeMode = LODFadeMode.CrossFade;
                g.animateCrossFading = true;
                g.SetLODs(lods);
                g.localReferencePoint = b0.center;
                // Schwellen fuer LOD-Bias 1 berechnet (Unity: relative Hoehe = size / (d * 2 tan(fov/2)) * lodBias, Schwelle <= 1).
                // Der Messlauf teilt die Gruppengroesse zur Laufzeit durch den tatsaechlichen LOD-Bias (FloraValidationRunner),
                // damit die geplanten Abstaende unabhaengig von der aktiven Grafikstufe gelten. LOD-Bias-Politik = spaetere TECH-Frage.
                g.size = size;
                PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{p.name}.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>
        /// LOD-Bias der Qualitaetsstufe, mit der der Messlauf laeuft (Standard-Stufe der Standalone-Plattform, sonst "PC").
        /// Der Editor kann ausserhalb des Play Mode eine andere Stufe aktiv haben – deshalb direkt aus QualitySettings.asset lesen.
        /// </summary>
        public static float PlayLodBias()
        {
            try
            {
                UnityEngine.Object[] objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
                if (objs != null && objs.Length > 0)
                {
                    var so = new SerializedObject(objs[0]);
                    SerializedProperty levels = so.FindProperty("m_QualitySettings");
                    int want = -1;
                    SerializedProperty def = so.FindProperty("m_PerPlatformDefaultQuality");
                    if (def != null)
                        for (int i = 0; i < def.arraySize; i++)
                        {
                            SerializedProperty e = def.GetArrayElementAtIndex(i);
                            SerializedProperty key = e.FindPropertyRelative("first"), val = e.FindPropertyRelative("second");
                            if (key != null && val != null && key.stringValue == "Standalone") want = val.intValue;
                        }
                    if (levels != null)
                        for (int i = 0; i < levels.arraySize; i++)
                        {
                            SerializedProperty l = levels.GetArrayElementAtIndex(i);
                            string n = l.FindPropertyRelative("name")?.stringValue;
                            if (i == want || (want < 0 && n == "PC"))
                            {
                                float b = l.FindPropertyRelative("lodBias").floatValue;
                                Debug.Log($"[FloraValidation] LOD-Bias der Messstufe \"{n}\": {b:0.00} (Editor aktuell {QualitySettings.lodBias:0.00})");
                                return b;
                            }
                        }
                }
            }
            catch (Exception e) { Debug.LogWarning("[FloraValidation] LOD-Bias nicht lesbar: " + e.Message); }
            return QualitySettings.lodBias;
        }

        /// <summary>Kalibrierkandidat: ein Objekt ohne LODGroup (nur fuer den Kalibrierbereich der Testszene).</summary>
        private static void BuildCalib(string name, string asset, Dictionary<string, Mesh> meshes, Dictionary<string, Material[]> mats)
        {
            if (!meshes.TryGetValue(asset, out Mesh mesh)) { Debug.LogWarning($"[FloraValidation] Kalibrierkandidat {name}: Mesh {asset} fehlt"); return; }
            var root = new GameObject(name);
            try
            {
                var go = new GameObject("LOD0_" + asset);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = mats[asset];
                r.shadowCastingMode = ShadowCastingMode.On;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static GameObject LoadPrefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{name}.prefab");

        // ------------------------------------------------------------------ Hilfen

        public static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
