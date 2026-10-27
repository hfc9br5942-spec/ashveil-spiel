using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden – Kreidefelsen (Claude 14.10.2026, Lückenliste Punkt 6 „Kreide-Felssatz“, VORSCHLAG): Wo das Gelände steiler als
    /// 40° ist (Kreidekliff der Westküste, Rand des Felskessels, Kehrental zum Grenzgebirge, Durchlässe nach Eiszahn und Eschenfeld,
    /// Rampe zu den Westmarken), treten Kreidebänke und -blöcke aus dem Hang. Formen: der vorhandene Felssatz aus Blender
    /// (FelssatzPipeline, Wand/Block/Platte) – keine neuen Netze; Material: die Kreide-Bodentextur der Westweiden (WW_Kreidefels)
    /// auf einer Kopie des Granit-Materials, Oberseiten mit Rasen. Frei bleiben: Wege (halbe Felsbreite + 5 m), Orte (45 m, Kreidekeller 80 m und sein Wegnetz 25 m),
    /// Gegnergebiete, die Naht zu Kareth (Ostrand 150 m) und alles unter dem Meeresspiegel.
    /// Eigene Felsdatei je Szene (Entwurf/Zelle), sonst zerstört ein Entwurfsbau die Netze der Zelle.
    /// </summary>
    public static class WestweidenKreide
    {
        private const string MatDir = ProjectSetup.Root + "/Art/Westweiden/Kreide";
        private const string ProbenDir = ProjectSetup.Root + "/Art/Westweiden/Proben";
        private const string GranitPfad = ProjectSetup.Root + "/Art/Welt/Materialien/Materialien/W_T11_Granit.mat";
        private const string GegnerPfad = ProjectSetup.Root + "/Data/Westweiden/Entwurf/WW_Gegner.json";
        public const float Schwelle = 40f;

        [System.Serializable] private sealed class Eintrag { public float x, z, radius; public float[] alt; }
        [System.Serializable] private sealed class Datei { public Eintrag[] gebiete; }

        /// <summary>Aus = keine Kreidefelsen (Gelände wie vorher).</summary>
        public static readonly bool Aktiv = true;

        public static string Bauen(Terrain t, Dictionary<string, Vector3> orte, string assetPfad)
        {
            if (!Aktiv) return "";
            Material fels = Mat("WW_Kreide_Fels", new Color(1.12f, 1.12f, 1.08f, 1f), 0.15f);
            Material rasen = Mat("WW_Kreide_Rasen", new Color(0.62f, 0.78f, 0.46f, 1f), 0.25f);
            if (fels == null || rasen == null) return "Kreidefelsen: Material fehlt · ";
            TerrainData td = t.terrainData;
            Vector3 tp = t.transform.position;
            float sx = td.size.x, sz = td.size.z;

            var sperr = new List<(Vector2 p, float r)>();
            if (orte != null)
                foreach (var kv in orte) sperr.Add((new Vector2(kv.Value.x, kv.Value.z), kv.Key.Contains("Kreidekeller") ? 80f : 45f));
            // Wegnetz des Kreidekellers (Felsband → Spalt → … → Kaminsteig; nicht in WW_Wege): Streifen von 25 m frei
            // (Spieltest op_674: ein Fels auf dem Felsband unterbrach das NavMesh zum Spalt)
            var kk = WestweidenKreidekeller.Pruefpunkte();
            for (int i = 0; i < kk.Count; i++)
            {
                Vector2 a0 = new Vector2(kk[i].lokal.x, kk[i].lokal.z);
                sperr.Add((a0, 30f));
                if (i == 0) continue;
                Vector2 a1 = new Vector2(kk[i - 1].lokal.x, kk[i - 1].lokal.z);
                float len = Vector2.Distance(a0, a1);
                for (float d = 0f; d < len; d += 5f) sperr.Add((Vector2.Lerp(a1, a0, d / len), 25f));
            }
            string gp = Path.Combine(Path.GetDirectoryName(Application.dataPath), GegnerPfad);
            if (File.Exists(gp))
            {
                Datei d = JsonUtility.FromJson<Datei>(File.ReadAllText(gp));
                if (d?.gebiete != null)
                    foreach (Eintrag e in d.gebiete)
                    {
                        sperr.Add((new Vector2(e.x, e.z), e.radius + 10f));
                        if (e.alt != null) for (int i = 0; i + 1 < e.alt.Length; i += 2) sperr.Add((new Vector2(e.alt[i], e.alt[i + 1]), 18f));
                    }
            }

            var rng = new System.Random(1404);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var daten = new List<float>();
            int n = 0;
            const float Raster = 24f;
            for (float z0 = 30f; z0 < sz - 30f; z0 += Raster)
                for (float x0 = 30f; x0 < sx - 30f; x0 += Raster)
                {
                    float x = x0 + R(-6f, 6f), z = z0 + R(-6f, 6f);
                    float zufall = (float)rng.NextDouble();
                    if (x > sx - 150f) continue;                                    // Naht zu Kareth
                    float u = (x - tp.x) / sx, v = (z - tp.z) / sz;
                    float steil = td.GetSteepness(u, v);
                    if (steil < Schwelle) continue;
                    if (zufall > Mathf.Min(1f, (steil - Schwelle) / 20f) * 0.8f) continue;
                    float y = t.SampleHeight(new Vector3(x, 0f, z)) + tp.y;
                    if (y < 0.8f) continue;                                         // Meer
                    var p = new Vector2(x, z);
                    bool frei = true;
                    foreach (var s in sperr) if (Vector2.Distance(p, s.p) < s.r) { frei = false; break; }
                    if (!frei || WestweidenOrte.WegAbstand(x, z) < 8f) continue;
                    // hangabwärts: Front der Wand (+z der Form) zeigt ins Tal
                    Vector3 nrm = td.GetInterpolatedNormal(u, v);
                    float yaw = Mathf.Atan2(nrm.x, nrm.z) * Mathf.Rad2Deg;
                    // Größe nach Steilheit: an den 60–100 m hohen Kliffwänden wirken kleine Steine wie Krümel – die Bänke müssen die
                    // gestreckte Geländetextur der Wand brechen (Bildprüfung 14.10., Kreide_1014)
                    float k = Mathf.Clamp01((steil - Schwelle) / 30f);
                    int form; Vector3 gr; float neig;
                    double w = rng.NextDouble();
                    if (w < 0.6) { form = 4; gr = new Vector3(Mathf.Lerp(8f, 26f, k), Mathf.Lerp(4f, 12f, k), Mathf.Lerp(3f, 7f, k)) * R(0.8f, 1.2f); neig = 0.7f; }   // Wand: Kreidebank
                    else if (w < 0.85) { form = 0; gr = new Vector3(R(4f, 8f), R(3f, 6f), R(4f, 8f)) * Mathf.Lerp(1f, 1.6f, k); neig = 0.3f; }                        // Block
                    else { form = 1; gr = new Vector3(Mathf.Lerp(8f, 18f, k), R(1.5f, 3f), R(4f, 8f)) * R(0.8f, 1.2f); neig = 0.6f; }                                 // Platte (Schichtkante)
                    float pitch = -(steil - 20f) * neig + R(-5f, 5f);   // oben in den Hang gelehnt (+z zeigt ins Tal)
                    float halb = Mathf.Max(gr.x, gr.z) * 0.5f;
                    if (WestweidenOrte.WegAbstand(x, z) < halb + 5f) continue;                              // ragt nicht in den Weg
                    // aus der Wand heraus: Mitte um einen Teil der Tiefe hangabwärts versetzen, Höhe vom Ausgangspunkt behalten
                    // (an fast senkrechten Wänden liegt der Geländepunkt sonst in der Wand und nur ein Rand schaut heraus)
                    var tal = new Vector2(nrm.x, nrm.z);
                    if (tal.sqrMagnitude > 1e-4f) tal.Normalize();
                    float off = gr.z * (form == 0 ? 0.3f : 0.4f) * Mathf.Lerp(0.3f, 1f, k);
                    float x2 = x + tal.x * off, z2 = z + tal.y * off;
                    float y2 = t.SampleHeight(new Vector3(x2, 0f, z2)) + tp.y;
                    float dy = (y - y2) - gr.y * R(0.15f, 0.3f);
                    daten.AddRange(new[] { x2, z2, dy, form, rng.Next(FelsKit.Varianten), gr.x, gr.y, gr.z, yaw + R(-12f, 12f), pitch, R(-6f, 6f), 0.75f });
                    n++;
                }
            if (n == 0) return "Kreidefelsen: keine steilen Stellen · ";
            var root = new GameObject("Kreidefelsen (Westweiden)").transform;
            int gebaut = FelsKit.BuildSatz(daten.ToArray(), root, (x, z) => t.SampleHeight(new Vector3(x, 0f, z)) + tp.y, assetPfad,
                                           Path.GetFileNameWithoutExtension(assetPfad), fels, rasen, "Kreidefelsen (Felssatz)", "WW");
            if (gebaut < 0) { Object.DestroyImmediate(root.gameObject); return "Kreidefelsen: Felssatz fehlt oder ist abgeschaltet – keine gebaut · "; }
            Physics.SyncTransforms();
            return $"Kreidefelsen: {gebaut} an steilen Hängen (≥ {Schwelle:0}°) · ";
        }

        /// <summary>Kopie des Granit-Materials (gleicher Shader, gleiche Einstellungen) mit den Kreide-Texturen der Westweiden.</summary>
        private static Material Mat(string name, Color tint, float glanz)
        {
            string pfad = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(pfad);
            if (m == null)
            {
                var granit = AssetDatabase.LoadAssetAtPath<Material>(GranitPfad);
                var alb = AssetDatabase.LoadAssetAtPath<Texture2D>(ProbenDir + "/WW_Kreidefels_Albedo.png");
                if (granit == null || alb == null) return null;
                ProjectSetup.EnsureFolder(MatDir);
                m = new Material(granit) { name = name };
                AssetDatabase.CreateAsset(m, pfad);
            }
            var a = AssetDatabase.LoadAssetAtPath<Texture2D>(ProbenDir + "/WW_Kreidefels_Albedo.png");
            var nm = AssetDatabase.LoadAssetAtPath<Texture2D>(ProbenDir + "/WW_Kreidefels_Normal.png");
            var mk = AssetDatabase.LoadAssetAtPath<Texture2D>(ProbenDir + "/WW_Kreidefels_Mask.png");
            var kachel = new Vector2(0.25f, 0.25f);   // 4 m je Kachel (UV der Felsen in Metern)
            foreach (string p in new[] { "_BaseMap", "_MainTex" }) if (m.HasProperty(p)) { m.SetTexture(p, a); m.SetTextureScale(p, kachel); }
            if (nm != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", nm); m.SetTextureScale("_BumpMap", kachel); }
            if (mk != null)
                foreach (string p in new[] { "_MetallicGlossMap", "_OcclusionMap" }) if (m.HasProperty(p)) m.SetTexture(p, mk);
            m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", glanz);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
