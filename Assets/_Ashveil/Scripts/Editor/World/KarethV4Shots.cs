using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kareth v4 – Review-Bilder aus der offenen Szene Kareth_v4 (ohne Play): Blickpunkte aus dem Entwurf plus Draufsichten.
    /// Ausgabe: Logs/Ashveil/KarethV4/*.png. Menü: Ashveil › Welt › Kareth v4: Review-Bilder.
    /// </summary>
    public static class KarethV4Shots
    {
        private const int Width = 1600, Height = 900;

        internal struct Shot { public string Name; public Vector3 Eye, Target; public float Fov, Ortho; }

        [MenuItem("Ashveil/Welt/Kareth v4: Review-Bilder", priority = 562)]
        public static void RenderMenu()
        {
            string dir = Render();
            if (dir != null) Debug.Log("[Ashveil] Kareth v4: Review-Bilder in " + dir);
        }

        public static string Render() => Render(null);

        /// <summary>Blickpunkt-Name als Dateiname (06.10.: „Muldentor: Berggestalt“ – der Doppelpunkt ist unter Windows verboten).</summary>
        private static string Dateiname(string name)
        {
            var sb = new System.Text.StringBuilder(name ?? "");
            foreach (char c in Path.GetInvalidFileNameChars()) sb.Replace(c, '_');
            return sb.Replace(' ', '_').ToString();
        }

        /// <summary>Review-Bilder in einen eigenen Unterordner (Vergleich „Sinn und Gelände“ 05.10.; vorher = StilA_nachher).</summary>
        [MenuItem("Ashveil/Welt/Kareth v4: Review-Bilder (Unterordner Integration)", priority = 563)]
        public static void RenderIntegration()
        {
            string dir = Render("Integration_nachher");
            if (dir != null) Debug.Log("[Ashveil] Kareth v4: Review-Bilder in " + dir);
        }

        /// <summary>Wie <see cref="Render()"/>, Ausgabe in Logs/Ashveil/KarethV4/&lt;sub&gt; (Vergleiche, z. B. Stil A vorher/nachher).</summary>
        public static string Render(string sub)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return null;
            // Immer frisch von der Platte öffnen: direkt nach dem Bau zeigt das Terrain sonst nur die erste Bodenschicht
            // (03.10. gesehen: Gewichte im TerrainData richtig, Bild trotzdem nur Gras – nach erneutem Öffnen korrekt)
            if (!File.Exists(KarethV4Builder.ScenePath)) return null;
            if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            List<Shot> shots = BuildShots(KarethV4Builder.LoadPlan(), Object.FindFirstObjectByType<Terrain>());

            string dir = Ashveil.Core.ProjectPaths.Logs("KarethV4");
            if (!string.IsNullOrEmpty(sub)) dir = Path.Combine(dir, sub);
            RenderShots(shots, dir, "");
            Debug.Log($"[Ashveil] Kareth v4: {shots.Count} Review-Bilder -> {dir}");
            return dir;
        }

        /// <summary>Blickpunkte aus dem Entwurf plus Draufsichten; die Nummer im Namen ist die Reihenfolge (Vergleiche über Läufe).</summary>
        internal static List<Shot> BuildShots(KarethV4Plan plan, Terrain terrain)
        {
            float G(float x, float z) => terrain != null ? terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y : 0f;

            var shots = new List<Shot>();
            int i = 1;
            foreach (KarethV4Blick b in plan.blickpunkte)
                shots.Add(new Shot { Name = $"{i++:00}_{Dateiname(b.name)}", Eye = new Vector3(b.x, G(b.x, b.z) + 1.8f, b.z),
                    Target = new Vector3(b.lx, b.ly, b.lz), Fov = 62f });
            shots.Add(new Shot { Name = $"{i++:00}_Lager_von_oben", Eye = new Vector3(0f, 400f, -2f), Target = new Vector3(0f, 0f, -1.99f), Ortho = 62f });
            shots.Add(new Shot { Name = $"{i++:00}_Mulde_von_oben", Eye = new Vector3(200f, 900f, 50f), Target = new Vector3(200f, 0f, 50.01f), Ortho = 520f });
            shots.Add(new Shot { Name = $"{i++:00}_Region_von_oben", Eye = new Vector3(-200f, 1500f, 50f), Target = new Vector3(-200f, 0f, 50.01f), Ortho = 2050f });
            shots.Add(new Shot { Name = $"{i++:00}_Lager_schraeg", Eye = new Vector3(60f, G(60f, 75f) + 38f, 75f), Target = new Vector3(0f, 1f, -10f), Fov = 55f });
            return shots;
        }

        // ================================================================== Nahbilder für die Texturabnahme (Max 07.10.)

        private static readonly string[] BauMaterialien =
        {
            "KV4 Stein", "KV4 Stein dunkel", "KV4 Stein bemoost", "KV4 Holz", "KV4 Holz dunkel", "KV4 Holz frisch",
            "KV4 Holz alt (vergraut)", "KV4 Lehmputz", "KV4 Schindeln", "KV4 Schindeln Halle",
        };

        /// <summary>
        /// Nahbilder je Bodenschicht und Gebäudematerial (Abnahme der Texturen aus Max' Paket, 07.10.): Boden aus 1,7 m Augenhöhe
        /// 3,5 m vor einer Stelle, an der die Schicht mindestens 80 % hat (die nächste zum Lager außerhalb der Palisade; Fels am Hang
        /// 28–60°, sonst flach);
        /// Gebäude 3,5–9 m vor der Seite eines Bauteils, die zum Lager zeigt, Dächer von oben. Gleiche Stellen bei gleichem Bau →
        /// vergleichbar über Läufe. Ausgabe Logs/Ashveil/KarethV4/&lt;sub&gt; (Standard Nah_JJJJ-MM-TT_HHMM).
        /// </summary>
        public static string RenderNah(string sub)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return null;
            if (!File.Exists(KarethV4Builder.ScenePath)) return null;
            if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            List<Shot> shots = NahShots(Object.FindFirstObjectByType<Terrain>());
            string dir = Path.Combine(Ashveil.Core.ProjectPaths.Logs("KarethV4"),
                string.IsNullOrEmpty(sub) ? "Nah_" + System.DateTime.Now.ToString("yyyy-MM-dd_HHmm") : sub);
            RenderShots(shots, dir, "");
            Debug.Log($"[Ashveil] Kareth v4: {shots.Count} Nahbilder -> {dir}");
            return shots.Count > 0 ? dir : null;
        }

        [MenuItem("Ashveil/Welt/Kareth v4: Nahbilder Texturen", priority = 565)]
        public static void RenderNahMenu()
        {
            string dir = RenderNah(null);
            if (dir != null) Debug.Log("[Ashveil] Kareth v4: Nahbilder in " + dir);
        }

        internal static List<Shot> NahShots(Terrain t)
        {
            var shots = new List<Shot>();
            if (t == null || t.terrainData == null) return shots;
            TerrainData d = t.terrainData;
            Vector3 o = t.transform.position, size = d.size;
            int res = d.alphamapResolution;
            float[,,] a = d.GetAlphamaps(0, 0, res, res);
            TerrainLayer[] layers = d.terrainLayers;
            float Hoehe(Vector3 p) => t.SampleHeight(p) + o.y;
            // Baumstämme für die Sichtprüfung (Kronen haben keinen Kollider): Auge und Ziel nicht direkt an einem Baum
            var baeume = new List<Vector2>();
            foreach (TreeInstance ti in d.treeInstances)
                baeume.Add(new Vector2(o.x + ti.position.x * size.x, o.z + ti.position.z * size.z));
            bool Frei(Vector3 eye, Vector3 target)
            {
                var e2 = new Vector2(eye.x, eye.z);
                var t2 = new Vector2(target.x, target.z);
                foreach (Vector2 bt in baeume)
                    if ((bt - e2).sqrMagnitude < 3.5f * 3.5f || (bt - t2).sqrMagnitude < 2.5f * 2.5f) return false;
                // freie Sicht: nichts außer dem Gelände zwischen Auge und Ziel
                foreach (RaycastHit h in Physics.RaycastAll(eye, (target + Vector3.up * 0.3f - eye).normalized, Vector3.Distance(eye, target)))
                    if (!(h.collider is TerrainCollider)) return false;
                return true;
            }
            for (int li = 0; li < layers.Length && li < a.GetLength(2); li++)
            {
                if (layers[li] == null) continue;
                string nm = layers[li].name;
                bool fels = nm.Contains("Granit") || nm.Contains("Rock");
                // Kandidaten nach Abstand zum Lager: zuerst streng (Schicht ≥ 80 %, Umgebung ≥ 60 %), dann locker für schmale
                // Schichten wie Wege (Raster 4 m: ein Weg ist oft nur eine Zelle breit)
                Shot? gefunden = null;
                for (int pass = 0; pass < 2 && gefunden == null; pass++)
                {
                    float wMin = pass == 0 ? 0.8f : 0.55f, nMin = pass == 0 ? 0.6f : 0.3f;
                    int nOff = pass == 0 ? 3 : 1;
                    var kand = new List<(float dist, Vector3 p, Vector3 n)>();
                    for (int z = 4; z < res - 4; z += pass == 0 ? 3 : 1)
                    for (int x = 4; x < res - 4; x += pass == 0 ? 3 : 1)
                    {
                        if (a[z, x, li] < wMin) continue;
                        if (a[z - nOff, x, li] < nMin || a[z + nOff, x, li] < nMin || a[z, x - nOff, li] < nMin || a[z, x + nOff, li] < nMin) continue;
                        float wx = o.x + (x + 0.5f) / res * size.x, wz = o.z + (z + 0.5f) / res * size.z;
                        float dist = wx * wx + wz * wz;   // nah am Lager (Ursprung), aber außerhalb der Palisade
                        if (dist < 70f * 70f) continue;
                        Vector3 n = d.GetInterpolatedNormal((x + 0.5f) / res, (z + 0.5f) / res);
                        float steil = Vector3.Angle(n, Vector3.up);
                        if (fels ? steil < 28f || steil > 60f : steil > 18f) continue;
                        kand.Add((dist, new Vector3(wx, 0f, wz), n));
                    }
                    kand.Sort((p, q) => p.dist.CompareTo(q.dist));
                    for (int k = 0; k < kand.Count && k < 400 && gefunden == null; k++)
                    {
                        Vector3 bp = kand[k].p;
                        bp.y = Hoehe(bp);
                        Vector3 back = fels ? new Vector3(kand[k].n.x, 0f, kand[k].n.z) : Vector3.back;   // am Hang: vom Tal aus
                        if (back.sqrMagnitude < 0.01f) back = Vector3.back;
                        Vector3 eye = bp + back.normalized * 3.5f;
                        eye.y = Mathf.Max(Hoehe(eye), bp.y) + 1.7f;
                        if (!Frei(eye, bp)) continue;
                        gefunden = new Shot { Name = $"Boden_{li}_{nm}", Eye = eye, Target = bp, Fov = 50f };
                    }
                }
                if (gefunden != null) shots.Add(gefunden.Value);
                else Debug.LogWarning($"[Ashveil] Kareth v4 Nahbilder: keine freie Stelle für {nm}");
            }
            // Gebäude: je Material das Teil nächst dem Lagerzentrum (mindestens 1,5 m breit und 1 m hoch)
            var gesehen = new HashSet<string>();
            var rs = new List<MeshRenderer>(Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None));
            rs.Sort((p, q) => p.bounds.center.sqrMagnitude.CompareTo(q.bounds.center.sqrMagnitude));
            foreach (MeshRenderer r in rs)
            {
                Material m = r.sharedMaterial;
                if (m == null) continue;
                string basis = m.name.StartsWith("W_KV4_") ? m.name.Substring(6) : m.name;
                if (System.Array.IndexOf(BauMaterialien, basis) < 0 || gesehen.Contains(basis)) continue;
                Bounds b = r.bounds;
                if (Mathf.Max(b.size.x, b.size.z) < 1.5f || b.size.y < 1f) continue;
                gesehen.Add(basis);
                Vector3 c = b.center;
                bool dach = basis.Contains("Schindel");
                // Blick auf die breite Seite (Normale = Achse mit der kleineren Ausdehnung), von der Lagerseite
                Vector3 nrm = b.extents.x < b.extents.z ? new Vector3(c.x > 0f ? -1f : 1f, 0f, 0f) : new Vector3(0f, 0f, c.z > 0f ? -1f : 1f);
                float dist = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) + 3f, 3.5f, 9f);
                Vector3 eye = c + nrm * dist;
                eye.y = dach ? b.max.y + 2.5f : Mathf.Max(c.y, Hoehe(eye) + 1.7f);
                shots.Add(new Shot { Name = "Bau_" + Dateiname(basis), Eye = eye, Target = c, Fov = 50f });
            }
            return shots;
        }

        /// <summary>Bilder der Liste aus der offenen Szene rendern (1600 × 900, mit Nachbearbeitung); Dateiname = Präfix + Name.</summary>
        internal static void RenderShots(List<Shot> shots, string dir, string prefix)
        {
            Directory.CreateDirectory(dir);
            GameObject camGo = null;
            RenderTexture rt = null;
            try
            {
                foreach (ParticleSystem ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Simulate(6f, true, true);
                camGo = new GameObject("KarethV4-Kamera") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = 0.2f;
                cam.allowHDR = true;
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                int k = 0;
                foreach (Shot s in shots)
                {
                    EditorUtility.DisplayProgressBar("Kareth v4", s.Name, (float)k++ / shots.Count);
                    cam.transform.position = s.Eye;
                    cam.orthographic = s.Ortho > 0f;
                    if (s.Ortho > 0f)
                    {
                        cam.orthographicSize = s.Ortho;
                        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                        cam.farClipPlane = 3000f;
                    }
                    else
                    {
                        cam.transform.rotation = Quaternion.LookRotation(s.Target - s.Eye);
                        cam.fieldOfView = s.Fov;
                        cam.farClipPlane = 5000f;
                    }
                    cam.targetTexture = rt;
                    var request = new RenderPipeline.StandardRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                    else cam.Render();
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, prefix + s.Name + ".png"), tex.EncodeToPNG());
                }
                Object.DestroyImmediate(tex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (camGo != null) Object.DestroyImmediate(camGo);
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            }
        }
    }
}
