using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Felssatz Kareth (05.10.2026; Ideenliste D-02 §3.6, Max: Regelstopp-Ausnahme „Felssatz“): eigene Felsformen aus
    /// Blender statt der Polyeder des Felsbaukastens. Unity startet Blender im Hintergrund mit
    /// Tools/Blender/ashveil_build_felsen.py: 5 Formen × 6 Varianten × 3 Detailstufen, Prüfung PASS / WARNING / FAIL.
    /// Ergebnis in Assets/_Ashveil/Art/Felsen: Felssatz_Kareth.fbx, Felssatz_Kareth.json (Liste), Prüfbericht (.txt);
    /// Quelle zum Weiterbearbeiten: SourceArt/Blender/Ashveil_Felssatz_Kareth.blend.
    /// FelsKit nimmt den Satz beim nächsten Szenenbau (Kareth v4), wenn er da und eingeschaltet ist; Netze mit FAIL nicht.
    /// Keine Fremdmodelle (ART-099): alle Formen entstehen aus eigenem Code.
    /// </summary>
    public static class FelssatzPipeline
    {
        public const string Ordner = ProjectSetup.Root + "/Art/Felsen";
        public const string FbxPfad = Ordner + "/Felssatz_Kareth.fbx";
        public const string ListePfad = Ordner + "/Felssatz_Kareth.json";
        public const string BerichtPfad = Ordner + "/Felssatz_Kareth_Pruefung.txt";
        private const string PrefKey = "Ashveil.FelsKit.Felssatz";
        private const string MenuBenutzen = "Ashveil/Grafik/Felssatz in Kareth benutzen";

        private static string ProjectDir => Ashveil.Core.ProjectPaths.ProjectRoot;
        private static string ScriptPath => Ashveil.Core.ProjectPaths.Tools("Blender", "ashveil_build_felsen.py");
        private static string ExportDir => Ashveil.Core.ProjectPaths.SourceArt("Export", "Felsen");
        private static string BlendPath => Ashveil.Core.ProjectPaths.SourceArt("Blender", "Ashveil_Felssatz_Kareth.blend");
        public static string LogPath => Ashveil.Core.ProjectPaths.Logs("Ashveil_Blender_Felsen.txt");

        /// <summary>Felssatz beim Szenenbau benutzen (Standard: ja). Aus = alte Polyeder des Felsbaukastens.</summary>
        public static bool Benutzen
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set { EditorPrefs.SetBool(PrefKey, value); _cache = null; }
        }

        // ================================================================== Menü

        [MenuItem("Ashveil/Grafik/Felssatz Kareth erzeugen (Blender, mit Prüfung)", priority = 404)]
        public static void ErzeugenMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            bool ok = Erzeugen(out string summary);
            if (ok) Debug.Log("[Ashveil] " + summary);
            else Debug.LogError("[Ashveil] " + summary);
            EditorUtility.DisplayDialog("Ashveil – Felssatz", summary, "OK");
        }

        [MenuItem(MenuBenutzen, priority = 405)]
        private static void UmschaltenMenu()
        {
            Benutzen = !Benutzen;
            Menu.SetChecked(MenuBenutzen, Benutzen);
            Debug.Log("[Ashveil] Felssatz in Kareth: " + (Benutzen ? "an" : "aus (alte Polyeder)") + " – wirkt beim nächsten Szenenbau.");
        }

        [MenuItem(MenuBenutzen, true)]
        private static bool UmschaltenPruefen()
        {
            Menu.SetChecked(MenuBenutzen, Benutzen);
            return true;
        }

        // ================================================================== Blender

        public static bool Erzeugen(out string summary)
        {
            string blender = ArtPipeline.FindBlender();
            if (blender == null)
            {
                summary = "Blender nicht gefunden (C:/Program Files/Blender Foundation/Blender x.y/blender.exe).";
                return false;
            }
            if (!File.Exists(ScriptPath))
            {
                summary = "Blender-Skript fehlt: " + ScriptPath;
                return false;
            }
            Directory.CreateDirectory(ExportDir);
            Directory.CreateDirectory(Path.GetDirectoryName(BlendPath));
            string report = Path.Combine(ExportDir, "Felssatz_Kareth_Pruefung.json");
            foreach (string alt in new[] { "Felssatz_Kareth.fbx", "Felssatz_Kareth.json", "Felssatz_Kareth_Pruefung.json", "Felssatz_Kareth_Pruefung.txt" })
                if (File.Exists(Path.Combine(ExportDir, alt))) File.Delete(Path.Combine(ExportDir, alt));

            if (!ArtPipeline.RunBlenderProcess(blender, ScriptPath,
                    $"--export \"{ExportDir}\" --blend \"{BlendPath}\" --report \"{report}\"",
                    LogPath, "den Felssatz", out _, out summary))
                return false;

            ProjectSetup.EnsureFolder(Ordner);
            string ziel = Path.Combine(ProjectDir, Ordner);
            foreach (string name in new[] { "Felssatz_Kareth.fbx", "Felssatz_Kareth.json", "Felssatz_Kareth_Pruefung.txt" })
            {
                string q = Path.Combine(ExportDir, name);
                if (!File.Exists(q))
                {
                    summary = "Blender lief, aber " + name + " fehlt. Protokoll: " + LogPath;
                    return false;
                }
                File.Copy(q, Path.Combine(ziel, name), true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(FbxPfad, ImportAssetOptions.ForceUpdate);
            _cache = null;

            Satz s = Laden(out string info, true);
            summary = $"{summary}. {info}\nBericht: {BerichtPfad}\nQuelle: {BlendPath}\n" +
                      "Kareth v4 neu bauen (Ashveil › Welt › Kareth v4: Szene bauen), damit die Felsen erscheinen.";
            return s != null;
        }

        // ================================================================== Laden

        [Serializable] private class Liste { public int version; public string fbx; public int varianten; public string blender; public Eintrag[] netze; }
        [Serializable] private class Eintrag { public string name; public int form; public int variante; public int lod; public int dreiecke; public string status; }

        /// <summary>Ein Felsnetz in der Einheitsbox [-0,5; 0,5]³ (Unity-Achsen), Umlaufsinn wie von Unity importiert.</summary>
        public sealed class Netz
        {
            public Vector3[] V;
            public Vector3[] N;
            public int[] T;
            public int Dreiecke => T.Length / 3;
        }

        public sealed class Satz
        {
            public const int Lods = 3;
            public Netz[,,] Netze;                 // [Form, Variante, LOD]
            public List<int>[] Varianten;          // je Form die brauchbaren Varianten (alle Stufen da, nicht FAIL)
            public string Quelle = "";

            /// <summary>Variante für einen Wunschwert (z. B. Variante aus dem Plan 0..11), -1 wenn die Form fehlt.</summary>
            public int Variante(int form, int wunsch)
            {
                if (form < 0 || form >= Varianten.Length) return -1;
                List<int> l = Varianten[form];
                if (l.Count == 0) return -1;
                return l[((wunsch % l.Count) + l.Count) % l.Count];
            }

            public Netz Get(int form, int variante, int lod) => Netze[form, variante, Mathf.Clamp(lod, 0, Lods - 1)];
        }

        private static Satz _cache;

        /// <summary>Satz laden (FBX + Liste), prüfen und zwischenspeichern. null, wenn nichts Brauchbares da ist.</summary>
        public static Satz Laden(out string info, bool neu = false)
        {
            if (_cache != null && !neu)
            {
                info = _cache.Quelle;
                return _cache;
            }
            _cache = null;
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(ListePfad);
            if (text == null)
            {
                info = "Felssatz fehlt (" + ListePfad + ") – Menü Ashveil › Grafik › Felssatz Kareth erzeugen.";
                return null;
            }
            Liste liste;
            try { liste = JsonUtility.FromJson<Liste>(text.text); }
            catch (Exception e)
            {
                info = "Felssatz-Liste unlesbar: " + e.Message;
                return null;
            }
            var meshes = new Dictionary<string, Mesh>();
            foreach (Mesh m in AssetDatabase.LoadAllAssetsAtPath(FbxPfad).OfType<Mesh>())
                meshes[m.name] = m;
            if (liste?.netze == null || meshes.Count == 0)
            {
                info = "Felssatz unvollständig: Liste " + (liste?.netze?.Length ?? 0) + " Einträge, FBX " + meshes.Count + " Netze.";
                return null;
            }

            int formen = FelsKit.Formen, varianten = Mathf.Max(1, liste.varianten);
            var s = new Satz { Netze = new Netz[formen, varianten, Satz.Lods], Varianten = new List<int>[formen] };
            int geladen = 0, ohne = 0, fail = 0;
            var warnungen = new List<string>();
            foreach (Eintrag e in liste.netze)
            {
                if (e.form < 0 || e.form >= formen || e.variante < 0 || e.variante >= varianten || e.lod < 0 || e.lod >= Satz.Lods) continue;
                if (e.status == "FAIL") { fail++; continue; }
                if (!meshes.TryGetValue(e.name, out Mesh m)) { ohne++; continue; }
                if (!m.isReadable) { warnungen.Add(e.name + " nicht lesbar (Import-Regel?)"); continue; }
                var n = new Netz { V = m.vertices, N = m.normals, T = m.triangles };
                if (n.N == null || n.N.Length != n.V.Length)
                {
                    var tmp = UnityEngine.Object.Instantiate(m);
                    tmp.RecalculateNormals();
                    n.N = tmp.normals;
                    UnityEngine.Object.DestroyImmediate(tmp);
                    warnungen.Add(e.name + " ohne Normalen (neu berechnet)");
                }
                s.Netze[e.form, e.variante, e.lod] = Verschweissen(n);
                geladen++;
            }

            // Achsen prüfen: Wand hat die ebene Rückseite bei z = -0,5, Säule den ebenen Fuß bei y = -0,5
            if (Gespiegelt(s, 4, 2, out bool zFlip) && zFlip) { Spiegeln(s, 2); warnungen.Add("z gespiegelt (Wand-Rückseite lag bei +z)"); }
            if (Gespiegelt(s, 2, 1, out bool yFlip) && yFlip) { Spiegeln(s, 1); warnungen.Add("y gespiegelt (Säulenfuß lag oben)"); }
            Normieren(s, warnungen);

            for (int f = 0; f < formen; f++)
            {
                s.Varianten[f] = new List<int>();
                for (int v = 0; v < varianten; v++)
                {
                    bool alle = true;
                    for (int l = 0; l < Satz.Lods; l++) alle &= s.Netze[f, v, l] != null;
                    if (alle) s.Varianten[f].Add(v);
                }
            }
            string formenText = string.Join(", ", Enumerable.Range(0, formen).Select(f => $"{FelsKit.FormName(f)} {s.Varianten[f].Count}"));
            s.Quelle = $"Felssatz v{liste.version} (Blender {liste.blender}): {geladen} Netze geladen, {fail} FAIL übersprungen" +
                       (ohne > 0 ? $", {ohne} fehlen in der FBX" : "") + $"; Varianten je Form: {formenText}" +
                       (warnungen.Count > 0 ? ". Hinweise: " + string.Join("; ", warnungen.Take(6)) : "");
            info = s.Quelle;
            foreach (string w in warnungen) Debug.LogWarning("[Ashveil] Felssatz: " + w);
            if (geladen == 0) return null;
            _cache = s;
            return s;
        }

        /// <summary>Punkte mit gleicher Lage und Normale zusammenlegen. Unity trennt Punkte an den UV-Nähten aus Blender;
        /// FelsKit rechnet eigene Welt-UVs, die Nähte kosten dort nur Speicher (05.10.: Felsdatei 184 MB).</summary>
        private static Netz Verschweissen(Netz n)
        {
            var map = new Dictionary<(Vector3, Vector3), int>(n.V.Length);
            var v = new List<Vector3>(n.V.Length);
            var nn = new List<Vector3>(n.V.Length);
            var neu = new int[n.V.Length];
            for (int i = 0; i < n.V.Length; i++)
            {
                var key = (n.V[i], n.N[i]);
                if (!map.TryGetValue(key, out int j))
                {
                    j = v.Count;
                    map[key] = j;
                    v.Add(n.V[i]);
                    nn.Add(n.N[i]);
                }
                neu[i] = j;
            }
            var t = new int[n.T.Length];
            for (int i = 0; i < t.Length; i++) t[i] = neu[n.T[i]];
            return new Netz { V = v.ToArray(), N = nn.ToArray(), T = t };
        }

        /// <summary>Liegt die ebene Seite einer Form auf der falschen Seite der Achse? (Flächenanteil auf beiden Seiten)</summary>
        private static bool Gespiegelt(Satz s, int form, int achse, out bool spiegeln)
        {
            double minus = 0, plus = 0;
            for (int v = 0; v < s.Netze.GetLength(1); v++)
            {
                Netz n = s.Netze[form, v, 0];
                if (n == null) continue;
                for (int t = 0; t < n.T.Length; t += 3)
                {
                    Vector3 a = n.V[n.T[t]], b = n.V[n.T[t + 1]], c = n.V[n.T[t + 2]];
                    Vector3 cr = Vector3.Cross(b - a, c - a);
                    float flaeche = cr.magnitude * 0.5f;
                    if (flaeche < 1e-9f) continue;
                    Vector3 fn = cr / (2f * flaeche);
                    float lo = Mathf.Min(a[achse], Mathf.Min(b[achse], c[achse])), hi = Mathf.Max(a[achse], Mathf.Max(b[achse], c[achse]));
                    if (fn[achse] < -0.95f && hi < -0.45f) minus += flaeche;
                    if (fn[achse] > 0.95f && lo > 0.45f) plus += flaeche;
                }
            }
            spiegeln = plus > 2 * minus && plus > 0.05;
            return minus + plus > 0;
        }

        private static void Spiegeln(Satz s, int achse)
        {
            foreach (Netz n in s.Netze)
            {
                if (n == null) continue;
                for (int i = 0; i < n.V.Length; i++)
                {
                    Vector3 v = n.V[i]; v[achse] = -v[achse]; n.V[i] = v;
                    Vector3 m = n.N[i]; m[achse] = -m[achse]; n.N[i] = m;
                }
                for (int t = 0; t < n.T.Length; t += 3) (n.T[t + 1], n.T[t + 2]) = (n.T[t + 2], n.T[t + 1]);
            }
        }

        /// <summary>Sicherheit gegen falschen Maßstab: Netze, die deutlich aus der Einheitsbox fallen, auf sie normieren.</summary>
        private static void Normieren(Satz s, List<string> warnungen)
        {
            int n0 = 0;
            for (int f = 0; f < s.Netze.GetLength(0); f++)
            for (int v = 0; v < s.Netze.GetLength(1); v++)
            {
                Netz n = s.Netze[f, v, 0];
                if (n == null || n.V.Length == 0) continue;
                Bounds b = new Bounds(n.V[0], Vector3.zero);
                foreach (Vector3 p in n.V) b.Encapsulate(p);
                float gross = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)), klein = Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z));
                if (gross < 1.25f && klein > 0.75f && b.center.magnitude < 0.1f) continue;
                // gleiche Abbildung für alle Stufen dieser Variante (sonst springt die Größe beim LOD-Wechsel)
                Vector3 sk = new Vector3(1f / Mathf.Max(1e-6f, b.size.x), 1f / Mathf.Max(1e-6f, b.size.y), 1f / Mathf.Max(1e-6f, b.size.z));
                for (int l = 0; l < Satz.Lods; l++)
                {
                    Netz m = s.Netze[f, v, l];
                    if (m == null) continue;
                    for (int i = 0; i < m.V.Length; i++) m.V[i] = Vector3.Scale(m.V[i] - b.center, sk);
                    for (int i = 0; i < m.N.Length; i++) m.N[i] = Vector3.Scale(m.N[i], new Vector3(1f / sk.x, 1f / sk.y, 1f / sk.z)).normalized;
                }
                n0++;
            }
            if (n0 > 0) warnungen.Add($"{n0} Varianten auf die Einheitsbox normiert (Maßstab im Export prüfen)");
        }
    }

    /// <summary>Import-Regeln für den Felssatz: Meter, Achsen von Blender eingebacken, Normalen aus der Datei,
    /// lesbar (FelsKit fasst die Netze zusammen), keine Materialien, keine Animation.</summary>
    public sealed class FelssatzImportRules : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("/_Ashveil/Art/Felsen/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importBlendShapes = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.addCollider = false;
            mi.generateSecondaryUV = false;
        }
    }
}
