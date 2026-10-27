using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Ashveil.Rendering;
using Ashveil.Rendering.Sky;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kareth v4 – Tageszeit-Bilder ohne Play (Welle 0, Schritt 2, 06.10.2026): ausgewählte Blickpunkte zu mehreren Uhrzeiten
    /// über die Vorschau des <see cref="DayNightCycle"/> (15:30 = gestaltetes Licht von Stil A). Die Szene bleibt unverändert:
    /// Sie wird vorher frisch geöffnet und danach von der Platte neu geladen.
    /// Mit „himmel“ kommt Himmel V1 (Wolken, zwei Monde, Sterne) nur für diese Bilder dazu – Vergleich für Max.
    /// Ausgabe: Logs/Ashveil/KarethV4/Tageszeit_&lt;Zeitstempel&gt;[_HimmelV1]/HHMM_&lt;Nr&gt;_&lt;Blick&gt;.png
    /// Auftrag „tageszeit_bilder“: leer = 06:00, 10:00, 15:30, 19:00, 23:00; Stunden durch Komma („6,10,15.5“); Wort „himmel“.
    /// </summary>
    public static class KarethV4Tageszeit
    {
        public static readonly float[] StandardStunden = { 6f, 10f, 15.5f, 19f, 23f };

        /// <summary>Blickpunkte des Entwurfs für die Tageszeit-Bilder (Lager, Ankunft, Untersteg, Berg) plus „Lager schräg“.</summary>
        private static readonly string[] Blicke =
            { "Lager zum Berg", "Im Lager am Feuer", "Ankunftsstelle", "Untersteg Dorfplatz", "Muldentor nach Untersteg" };

        [InitializeOnLoadMethod]
        private static void Registrieren() => Auftragsordner.Registrieren("tageszeit_bilder", Auftrag, true, 1800);

        [MenuItem("Ashveil/Welt/Kareth v4: Tageszeit-Bilder (06, 10, 15:30, 19, 23 Uhr)", priority = 567)]
        private static void Menu()
        {
            bool ok = Render(StandardStunden, false, out string meldung);
            if (ok) Debug.Log("[Ashveil] Kareth v4: " + meldung);
            else EditorUtility.DisplayDialog("Ashveil", meldung, "OK");
        }

        private static void Auftrag(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            var stunden = new List<float>();
            bool himmel = false;
            foreach (string roh in (a.parameter ?? "").Split(new[] { ',', ' ', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = roh.Trim().ToLowerInvariant();
                if (w == "himmel") { himmel = true; continue; }
                if (!float.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out float h) || h < 0f || h > 24f)
                {
                    fertig(false, $"Parameter „{roh}“ nicht verstanden – erlaubt: Stunden (z. B. 6,10,15.5) und „himmel“");
                    return;
                }
                stunden.Add(h);
            }
            bool ok = Render(stunden.Count > 0 ? stunden.ToArray() : StandardStunden, himmel, out string meldung);
            fertig(ok, meldung);
        }

        /// <summary>Bilder rendern; false mit Meldung, wenn es nicht geht (Play, ungespeicherte Szene, kein Tageslauf).</summary>
        public static bool Render(float[] stunden, bool himmel, out string meldung)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { meldung = "Play läuft"; return false; }
            if (!File.Exists(KarethV4Builder.ScenePath)) { meldung = "Kareth v4 ist noch nicht gebaut"; return false; }
            if (EditorSceneManager.GetActiveScene().isDirty) { meldung = "ungespeicherte Änderungen in der offenen Szene – nichts gerendert"; return false; }

            EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            var cycle = Object.FindFirstObjectByType<DayNightCycle>();
            if (cycle == null)
            {
                meldung = "kein Tageslauf in Kareth v4 – Szene mit Stil A neu bauen (Auftrag kareth_v4_bauen)";
                return false;
            }
            List<KarethV4Shots.Shot> alle = KarethV4Shots.BuildShots(KarethV4Builder.LoadPlan(), Object.FindFirstObjectByType<Terrain>());
            List<KarethV4Shots.Shot> shots = alle.Where(s => s.Name.EndsWith("_Lager_schraeg", StringComparison.Ordinal) ||
                                                         Blicke.Any(b => s.Name.EndsWith("_" + b.Replace(' ', '_'), StringComparison.Ordinal))).ToList();
            string dir = Path.Combine(Ashveil.Core.ProjectPaths.Logs("KarethV4"),
                "Tageszeit_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm") + (himmel ? "_HimmelV1" : ""));
            int bilder = 0;
            bool vorschau = false;
            DayNightCycle.LichtProfil profil = cycle.Profil;
            try
            {
                if (himmel && cycle.GetComponent<SkyDirector>() == null)
                {
                    Light sun = RenderSettings.sun;
                    SkyContentBuilder.Install(cycle.gameObject, sun);   // nur im Speicher – die Szene wird danach neu geladen
                }
                vorschau = cycle.PreviewBegin();
                if (!vorschau)
                {
                    meldung = "Vorschau nicht möglich (keine Sonne in der Szene?)";
                    return false;
                }
                foreach (float h in stunden)
                {
                    // Tag 0 wie im Editor (gleiche Wolken und Monde in jedem Lauf); erst der Himmel, dann das Licht (Mondlicht)
                    SkyDirector.EditorPreviewHours = h;
                    SkyDirector sky = SkyDirector.Active;
                    if (sky != null) sky.Refresh(h);
                    cycle.PreviewAt(h);
                    KarethV4Shots.RenderShots(shots, dir, Stempel(h) + "_");
                    bilder += shots.Count;
                }
            }
            finally
            {
                if (vorschau) cycle.PreviewEnd();
                SkyDirector.EditorPreviewHours = null;
                // Frisch von der Platte: verwirft alles, was die Vorschau im Speicher verändert hat (Licht, Himmel V1)
                EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            }
            meldung = $"{bilder} Tageszeit-Bilder ({string.Join(", ", stunden.Select(Uhr))}{(himmel ? ", mit Himmel V1" : "")}; Stützstellen {profil}) in {dir}";
            return true;
        }

        private static string Stempel(float h)
        {
            int m = Mathf.RoundToInt(Mathf.Repeat(h, 24f) * 60f) % (24 * 60);
            return $"{m / 60:00}{m % 60:00}";
        }

        private static string Uhr(float h)
        {
            string s = Stempel(h);
            return s.Substring(0, 2) + ":" + s.Substring(2);
        }
    }
}
