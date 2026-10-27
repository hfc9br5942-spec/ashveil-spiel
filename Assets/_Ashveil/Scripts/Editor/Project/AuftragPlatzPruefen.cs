using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// 23.10.2026 (Spielablauf-Sitzung: „ob Grete in Tür oder Wand ragt“): Auftrag „platz_pruefen“ – Bewohner-Plätze aus KarethV4_Inhalte.json
    /// in der Kareth-v4-Szene prüfen, ohne sie zu verändern. Je Platz eine Ersatzfigur in Sitz- (Sitzhöhe aus Parameter oder „art“) bzw.
    /// Stehhaltung; gemessen wird, ob Rumpf, Kopf oder Beine in Collider anderer Objekte ragen (Gelände zählt nicht) und wie weit die
    /// nächste Wand hinter dem Rücken ist. Bilder von vorn und von der Seite nach Logs/Ashveil/Plaetze/. Danach wird die Szene
    /// ungespeichert neu geladen. Parameter: „platz.id[:sitzhoehe],…“, z. B. „untersteg.muehle.tuer:0“; Sitzhöhe weglassen = aus „art“.
    /// </summary>
    [InitializeOnLoad]
    internal static class AuftragPlatzPruefen
    {
        static AuftragPlatzPruefen()
        {
            Auftragsordner.Registrieren("platz_pruefen", (a, fertig) => { string m = Pruefen(a.parameter ?? "", out bool ok); fertig(ok, m); }, true, 600);
        }

        private static string Pruefen(string parameter, out bool ok)
        {
            ok = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Play läuft";
            if (!File.Exists(KarethV4Builder.ScenePath)) return "Kareth v4 fehlt";
            if (EditorSceneManager.GetActiveScene().isDirty) return "offene Szene hat ungespeicherte Änderungen – nicht angefasst";
            KarethV4Inhalte.Daten daten = KarethV4Inhalte.Load();
            if (daten == null || daten.plaetze == null) return "Inhalte fehlen";
            var plaetze = new Dictionary<string, KarethV4Inhalte.Platz>();
            foreach (KarethV4Inhalte.Platz p in daten.plaetze) if (p != null && !string.IsNullOrEmpty(p.id)) plaetze[p.id] = p;

            EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            Terrain terrain = Object.FindFirstObjectByType<Terrain>();
            string ordner = Ashveil.Core.ProjectPaths.Logs("Plaetze");
            Directory.CreateDirectory(ordner);
            var bericht = new StringBuilder();
            var temp = new List<Object>();
            try
            {
                foreach (string teil in parameter.Split(new[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] t = teil.Split(':');
                    // Probeposition ohne Datenänderung: „id@x/z/blick“
                    string[] probe = t[0].Split('@');
                    if (!plaetze.TryGetValue(probe[0], out KarethV4Inhalte.Platz p)) { bericht.Append(t[0]).Append(": unbekannt · "); continue; }
                    if (probe.Length > 1)
                    {
                        string[] w = probe[1].Split('/');
                        var ic = System.Globalization.CultureInfo.InvariantCulture;
                        if (w.Length == 3)
                            p = new KarethV4Inhalte.Platz { id = p.id + "_probe", hoehe = p.hoehe, art = p.art, y = p.y,
                                x = float.Parse(w[0], ic), z = float.Parse(w[1], ic), yaw = float.Parse(w[2], ic) };
                    }
                    float sitz = -1f;
                    if (t.Length > 1) float.TryParse(t[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sitz);
                    else if (p.art != null && p.art.StartsWith("sitz:")) float.TryParse(p.art.Substring(5), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sitz);
                    bericht.Append(EinPlatz(p, sitz, terrain, ordner, temp)).Append(" · ");
                }
                ok = true;
                return bericht.ToString().TrimEnd(' ', '·') + " · Bilder: " + ordner;
            }
            finally
            {
                foreach (Object o in temp) if (o != null) Object.DestroyImmediate(o);
                // nichts behalten: Szene ungespeichert neu laden (die Ersatzfigur hat sie als geändert markiert)
                EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            }
        }

        /// <summary>Collider einer Figur (Animator darüber oder darunter) – die Figur steht selbst schon am Platz.</summary>
        private static bool Figur(Collider c) => c.GetComponentInParent<Animator>() != null || c.GetComponentInChildren<Animator>() != null;

        private static string EinPlatz(KarethV4Inhalte.Platz p, float sitz, Terrain terrain, string ordner, List<Object> temp)
        {
            float Boden(float x, float z) => p.hoehe == "boden" ? p.y : (terrain != null ? terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y : 0f) + (p.hoehe == "gelaende" ? p.y : 0f);
            Vector3 fuss = new Vector3(p.x, Boden(p.x, p.z), p.z);
            Quaternion blick = Quaternion.Euler(0f, p.yaw, 0f);
            Vector3 vor = blick * Vector3.forward, rechts = blick * Vector3.right, hoch = Vector3.up;
            bool sitzt = sitz >= 0f;
            // Ersatzkörper (Mensch ~1,75 m): Sitzen = Gesäß auf Sitzhöhe, Oberschenkel nach vorn, Unterschenkel zum Boden; Stehen = Säule
            List<(string Name, Vector3 A, Vector3 B, float R)> Teile(Vector3 f)
            {
                var l = new List<(string, Vector3, Vector3, float)>();
                if (sitzt)
                {
                    Vector3 becken = f + hoch * (sitz + 0.12f);                                   // Gesäß-/Beckenhöhe
                    Vector3 knie = sitz < 0.2f ? becken + vor * 0.48f + hoch * 0.18f : becken + vor * 0.45f;   // am Boden: Knie leicht angezogen
                    Vector3 ferse = sitz < 0.2f ? f + vor * 0.72f + hoch * 0.06f : knie - hoch * Mathf.Max(0.05f, sitz) + vor * 0.05f;
                    l.Add(("Rumpf", becken + hoch * 0.12f, becken + hoch * 0.58f - vor * 0.04f, 0.17f));
                    l.Add(("Kopf", becken + hoch * 0.75f, becken + hoch * 0.82f, 0.11f));
                    l.Add(("Oberschenkel", becken + vor * 0.08f, knie, 0.09f));
                    l.Add(("Unterschenkel", knie, ferse, 0.07f));
                }
                else
                {
                    l.Add(("Beine", f + hoch * 0.12f, f + hoch * 0.85f, 0.14f));
                    l.Add(("Rumpf", f + hoch * 0.95f, f + hoch * 1.45f, 0.18f));
                    l.Add(("Kopf", f + hoch * 1.58f, f + hoch * 1.68f, 0.11f));
                }
                return l;
            }
            SortedSet<string> Treffer(Vector3 f)
            {
                var s = new SortedSet<string>();
                foreach (var (name, a, b, r) in Teile(f))
                    foreach (Collider c in Physics.OverlapCapsule(a, b, r * 0.92f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (c is TerrainCollider || Figur(c)) continue;   // Gelände und Figuren (die Figur selbst steht schon dort)
                        s.Add($"{name}→{c.name}");
                    }
                return s;
            }
            var teile = Teile(fuss);
            var treffer = Treffer(fuss);
            // Nächste freie Stelle (10–50 cm, acht Richtungen, Blickrichtung bleibt) als Vorschlag für die Daten
            string vorschlag = "";
            if (treffer.Count > 0)
                for (float r = 0.1f; r <= 0.501f && vorschlag.Length == 0; r += 0.1f)
                    for (int k = 0; k < 8 && vorschlag.Length == 0; k++)
                    {
                        Vector3 d = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * r;
                        Vector3 f2 = new Vector3(p.x + d.x, Boden(p.x + d.x, p.z + d.z), p.z + d.z);
                        if (Treffer(f2).Count == 0)
                            vorschlag = string.Format(System.Globalization.CultureInfo.InvariantCulture, ", Vorschlag frei bei x {0:0.00} z {1:0.00} ({2:0.0} m)", f2.x, f2.z, r);
                    }
            // Wand im Rücken (nur Sitzen): Abstand vom Rücken bis zum nächsten Collider hinter der Figur
            string ruecken = "";
            if (sitzt)
            {
                Vector3 r0 = fuss + hoch * (sitz + 0.45f);
                ruecken = Physics.Raycast(r0, -vor, out RaycastHit h, 2f, ~0, QueryTriggerInteraction.Ignore) && !(h.collider is TerrainCollider) && !Figur(h.collider)
                    ? $", Wand im Rücken {h.distance - 0.17f:0.00} m ({h.collider.name})" : ", Rücken frei";
            }
            // Hindernis vor dem Gesicht (Augenhöhe): Abstand bis zur nächsten Wand/Tür nach vorn
            Vector3 auge = fuss + hoch * (sitzt ? sitz + 0.85f : 1.6f);
            float frei = 3f;
            string vorne = Physics.Raycast(auge, vor, out RaycastHit hv, 3f, ~0, QueryTriggerInteraction.Ignore) && !(hv.collider is TerrainCollider) && !Figur(hv.collider)
                ? $", vor dem Gesicht {(frei = hv.distance):0.00} m {hv.collider.name}" : ", vorne frei";
            // Ersatzfigur sichtbar machen und Bilder: von vorn (2,6 m) und von der Seite
            var figur = new GameObject("Platzprüfung " + p.id);
            temp.Add(figur);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = treffer.Count > 0 ? new Color(0.9f, 0.2f, 0.15f) : new Color(0.2f, 0.75f, 0.3f) };
            temp.Add(mat);
            foreach (var (name, a, b, r) in teile)
            {
                var k = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(k.GetComponent<Collider>());
                k.transform.SetParent(figur.transform, true);
                k.transform.position = (a + b) * 0.5f;
                k.transform.up = (b - a).sqrMagnitude > 1e-6f ? (b - a).normalized : Vector3.up;
                k.transform.localScale = new Vector3(r * 2f, ((b - a).magnitude + 2f * r) * 0.5f, r * 2f);
                k.GetComponent<Renderer>().sharedMaterial = mat;
            }
            var camGo = new GameObject("Platzkamera");
            temp.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 50f; cam.nearClipPlane = 0.05f;
            var rt = new RenderTexture(640, 480, 24);
            temp.Add(rt);
            cam.targetTexture = rt;
            Vector3 mitte = fuss + hoch * (sitzt ? 0.5f : 0.9f);
            var bild = new Texture2D(1920, 480, TextureFormat.RGB24, false);
            temp.Add(bild);
            Vector3[] von = { vor * Mathf.Min(2.6f, frei - 0.15f) + hoch * 0.6f, rechts * 2.4f + vor * 0.6f + hoch * 0.7f, -vor * 2.6f + hoch * 1.2f };
            for (int i = 0; i < 3; i++)
            {
                cam.transform.position = mitte + von[i];
                cam.transform.LookAt(mitte);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(640, 480, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); tex.Apply();
                bild.SetPixels(i * 640, 0, 640, 480, tex.GetPixels());
                Object.DestroyImmediate(tex);
                RenderTexture.active = null;
            }
            cam.targetTexture = null;
            bild.Apply();
            File.WriteAllBytes(Path.Combine(ordner, "Platz_" + p.id.Replace('.', '_') + ".png"), bild.EncodeToPNG());
            string haltung = sitzt ? $"sitzt (Sitzhöhe {sitz:0.00})" : "steht";
            return treffer.Count == 0 ? $"{p.id} {haltung}: frei{ruecken}{vorne}" : $"{p.id} {haltung}: ragt in {string.Join(", ", treffer)}{ruecken}{vorne}{vorschlag}";
        }
    }
}
