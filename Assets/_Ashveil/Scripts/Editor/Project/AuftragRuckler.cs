using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Ruckler-Messung im fertigen Spiel (CORE-005, 09.10.2026 nach dem Spieltest r2) – Auftrag „ruckler_messung“ und Menü
    /// Ashveil › Spieltest r2 › Ruckler-Messung. Startet den Spieltest-Build zweimal mit der Ruckler-Probe
    /// (<see cref="Ashveil.Networking.RucklerProbe"/>) und der vollen Bild-Spitzen-Messung
    /// (<see cref="Ashveil.UI.Diagnostics.FrameSpikeProfiler"/>):
    ///   Lauf 1 – Profil „creator-ruckler“: Charakter-Editor mit allen Auswahlen, dann Welt, Fenster und Blickpunkte (die zwölf des
    ///            Kareth-Plans rund um Lager und Untersteg, dazu elf ferne Orte ohne Gegner aus der Beidatei der Inhalte).
    ///   Lauf 2 – Profil „kareth-ruckler“: gleich in die Welt, gleicher Weg (zweiter Start: Was bleibt, wenn Treiber und
    ///            Windows schon warm sind?).
    /// Beide Profile sind Test-Profile: Sie beginnen am Wachtfeuer, und die Probe darf sie versetzen. Max' Profilzeiger
    /// (ashveil_profile.txt) wird nicht berührt. Bericht: Logs/…/Ashveil_Ruckler_Messung.txt, die Sitzungsdateien liegen im
    /// Build unter Logs/Ashveil/Ruckler/. Ändert weder Build noch Projekt.
    /// Seit 12.10.2026 (Westweiden, Lückenliste Punkt 9) zusätzlich Auftrag „ruckler_westweiden“: ein Lauf mit Westweiden-Blickpunkten
    /// (Zelle laden, Hecken, Wasser, Kreidekeller, Zelle entladen) → Logs/…/Ashveil_Ruckler_Westweiden.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragRuckler
    {
        private const string Bericht = "Ashveil_Ruckler_Messung.txt";
        private const string BerichtWestweiden = "Ashveil_Ruckler_Westweiden.txt";
        private const double LaufMinuten = 12.0;
        private const int MaxBlicke = 12;

        /// <summary>Ferne Orte für die Probe (Kennung in KarethV4_Inhalte.json, Versatz in m, Name) – mit Abstand zu Gegnergebieten.</summary>
        private static readonly (string id, float dx, float dz, string name)[] Ferne =
        {
            ("site.kareth.hub.glockenrast", 0f, 0f, "Glockenrast"),
            ("site.kareth.wegschrein", 0f, 0f, "Wegschrein"),
            ("site.kareth.rastplatz", 0f, 0f, "Alter Rastplatz"),
            ("site.kareth.hub.ostwacht", 0f, 0f, "Aschenwacht"),
            ("site.kareth.aschenfeld", -50f, 120f, "Aschenfeld, Weg ins Feld"),
            ("site.kareth.uhrwerk", 0f, 0f, "Uhrwerk"),
            ("site.kareth.kueste", 0f, 0f, "Fangkörbe am Salzsee"),
            ("site.kareth.salzufer.damm", -100f, 150f, "Dammbruch"),
            ("site.kareth.harzerlager", 0f, 0f, "Harzerlager"),
            ("site.kareth.oedi", 0f, 0f, "Ödis Hütte"),
            ("site.kareth.wuestung", -20f, 160f, "Wüstungshöfe von Norden"),
        };

        private static readonly string[][] LaeufeKareth =
        {
            new[] { "creator-ruckler", "Lauf 1: neues Profil mit Charakter-Editor, danach die Welt" },
            new[] { "kareth-ruckler", "Lauf 2: gleich in die Welt (zweiter Start)" },
        };

        /// <summary>
        /// Westweiden (Lückenliste Punkt 9, 12.10.2026): ein Lauf vom Wachtfeuer in die Westweiden-Zelle und zurück – misst das Laden
        /// der Zelle (angekündigte Reise), die Orte mit viel Geometrie (Hecken in Langer Rain und am Dreispannhof, Wasser, Mühlen),
        /// den Kreidekeller (Felsband, Spalt) und das Entladen am Wachtfeuer. Lokale Westweiden-Meter + Ursprung in Kareth.
        /// </summary>
        private static readonly string[][] LaeufeWestweiden =
        {
            new[] { "kareth-westweiden-ruckler", "Lauf 1: Wachtfeuer → Westweiden (Zelle laden) → Orte → Wachtfeuer (Zelle entladen)" },
        };

        private static readonly (float x, float z, float yaw, float y, string name)[] WestweidenBlicke =
        {
            (3820f, 1560f, 262f, float.NaN, "Kammsattel, Blick auf die Mühlen"),
            (3350f, 2350f, 180f, float.NaN, "Hallgrund"),
            (2300f, 1500f, 90f, float.NaN, "Langer Rain, Mühlplatz (Hecken, Bach, Brücke)"),
            (2110f, 1840f, 0f, float.NaN, "Mühlenrain (vor den Mühlen)"),   // 14.10.: vorher genau in der mittleren Mühle (seit dem Modell im Turm)
            (1650f, 1080f, 45f, float.NaN, "Dreispannhof (Hecken, Kopfweiden)"),
            (2350f, 470f, 300f, float.NaN, "Kornwagen an der Kornspur"),
            (3130f, 810f, 150f, float.NaN, "Bürgerwehr-Posten, Blick zum Passfuß"),
            (1900f, 2860f, 230f, float.NaN, "Hirtenlager auf dem Nordrücken"),
            (470f, 1750f, 270f, float.NaN, "Erster Horizont (Meer, Steilrand)"),
            (400f, 1640f, 200f, float.NaN, "Felsband zum Kreidekeller, oben"),
            (326f, 1505.5f, 90f, 270.6f + 0.3f, "Kreidekeller: Spalt (Höhle)"),
            (900f, 1450f, 270f, float.NaN, "Hofstraße West (Wanderfindlinge)"),
        };

        private static string[][] Laeufe = LaeufeKareth;
        private static string _bericht = Bericht;

        private sealed class Zustand
        {
            public string Folder, Blicke;
            public List<string> Namen;
            public int Index = -1;
            public Process Proc;
            public DateTime Start;
            public double Bis;
            public string Log;
            public int Fehler;
            public readonly StringBuilder Report = new StringBuilder();
            public readonly StringBuilder Kurz = new StringBuilder();
            public Action<bool, string> Fertig;
        }

        private static Zustand _z;

        static AuftragRuckler()
        {
            Auftragsordner.Registrieren("ruckler_messung", (a, f) => { Laeufe = LaeufeKareth; _bericht = Bericht; Messen(a, f); }, false, (int)(LaeufeKareth.Length * LaufMinuten * 60.0) + 300);
            Auftragsordner.Registrieren("ruckler_westweiden", (a, f) => { Laeufe = LaeufeWestweiden; _bericht = BerichtWestweiden; Messen(a, f); }, false, (int)(LaufMinuten * 60.0) + 300);
        }

        [MenuItem("Ashveil/Spieltest r2/Ruckler-Messung (2 Starts mit Probe)", priority = 3)]
        private static void Menue() => Messen(null, (ok, m) => Debug.Log("[Ashveil] Ruckler-Messung: " + m));

        private static void Messen(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            if (_z != null) { fertig(false, "Ruckler-Messung läuft schon"); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) { fertig(false, "Play läuft – nichts gestartet"); return; }
            string folder = Path.GetFullPath(SpieltestBuild.Folder);
            if (!File.Exists(Path.Combine(folder, "Ashveil.exe"))) { fertig(false, "Spieltest-Build fehlt: " + folder + " (erst spieltest_bauen)"); return; }

            _z = new Zustand { Folder = folder, Fertig = fertig };
            _z.Blicke = Laeufe == LaeufeWestweiden ? WestweidenArgument(out _z.Namen) : BlickArgument(out _z.Namen);
            NetworkSetupBuilder.StopTestClients();
            _z.Report.AppendLine($"ASHVEIL Ruckler-Messung (CORE-005) · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _z.Report.AppendLine("Build: " + folder);
            _z.Report.Append(Laeufe == LaeufeWestweiden ? "Blickpunkte (Westweiden): " : "Blickpunkte (Kareth-Plan): ");
            for (int i = 0; i < _z.Namen.Count; i++) _z.Report.Append(i > 0 ? " · " : "").Append(i + 1).Append(" = ").Append(_z.Namen[i]);
            _z.Report.AppendLine(_z.Namen.Count == 0 ? "keine" : "");
            // Grafikspeicher des Editors (dieselbe Grafikkarte): Ist er knapp, lagert Windows aus und das Spiel wartet (v2.4)
            long editorGfx = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
            _z.Report.AppendLine($"Editor: Grafikspeicher (Unity, beim Treiber angelegt) {editorGfx / (1024L * 1024L)} MB · Grafikkarte {SystemInfo.graphicsMemorySize} MB · " +
                                 $"offene Szene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            Naechster();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Naechster()
        {
            _z.Index++;
            if (_z.Index >= Laeufe.Length)
            {
                Abschluss();
                return;
            }
            string profil = Laeufe[_z.Index][0], titel = Laeufe[_z.Index][1];
            _z.Log = Path.Combine(_z.Folder, $"Ruckler_{_z.Index + 1}.log");
            string args = $"-ashveil-host -ashveil-profile {profil} {NetworkSetupBuilder.ZeitVormittag} " +
                          $"{Ashveil.Networking.RucklerProbe.Argument} {_z.Blicke} {Ashveil.UI.Diagnostics.FrameSpikeProfiler.VollArgument}";
            _z.Report.AppendLine().AppendLine($"[{titel}]").AppendLine("  Argumente: " + args);
            _z.Start = DateTime.Now;
            try
            {
                _z.Proc = Process.Start(new ProcessStartInfo(Path.Combine(_z.Folder, "Ashveil.exe"), args + $" -logFile \"{_z.Log}\"")
                    { WorkingDirectory = _z.Folder, UseShellExecute = false });
                _z.Report.AppendLine("  Prozess gestartet (PID " + _z.Proc.Id + ")");
            }
            catch (Exception e)
            {
                Pruefe(false, "Prozess startet nicht: " + e.Message);
                _z.Proc = null;
            }
            _z.Bis = EditorApplication.timeSinceStartup + LaufMinuten * 60.0;
            if (_z.Proc == null) Naechster();
        }

        private static void Tick()
        {
            if (_z == null)
            {
                EditorApplication.update -= Tick;
                return;
            }
            if (_z.Proc == null) return;
            bool ende = _z.Proc.HasExited;
            if (!ende && EditorApplication.timeSinceStartup < _z.Bis) return;
            if (!ende)
            {
                Pruefe(false, $"Spiel nach {LaufMinuten:0} min nicht von selbst beendet – Fenster wird geschlossen");
                // 22.10.2026 (Runde 5): echtes WM_CLOSE (PlaytestSmoke.WmCloseSenden) statt Process.CloseMainWindow – das ist im Mono des
                // Editors ein hartes TerminateProcess mit Exit -2; dann fehlte die Schluss-Zusammenfassung der Ruckler-Sitzung.
                bool gesendet = PlaytestSmoke.WmCloseSenden(_z.Proc);
                if (!_z.Proc.WaitForExit(15000))
                {
                    Pruefe(false, "Fenster schliessen: Spiel reagiert nicht – hart beendet" + (gesendet ? "" : " (WM_CLOSE nicht gesendet)"));
                    try { _z.Proc.Kill(); } catch (Exception) { }
                }
                else _z.Report.AppendLine($"  ·      über das Fenster geschlossen (WM_CLOSE {(gesendet ? "gesendet" : "NICHT gesendet")}), Exit-Code {_z.Proc.ExitCode}");
            }
            else Pruefe(_z.Proc.ExitCode == 0, "Spiel hat sich selbst beendet (Exit-Code " + _z.Proc.ExitCode + ")");
            try { Auswerten(); }
            catch (Exception e) { Pruefe(false, "Auswertung: " + e.GetType().Name + ": " + e.Message); }
            _z.Proc.Dispose();
            _z.Proc = null;
            Naechster();
        }

        private static void Pruefe(bool ok, string text)
        {
            _z.Report.AppendLine((ok ? "  OK     " : "  FEHLER ") + text);
            if (!ok) _z.Fehler++;
        }

        // ================================================================== Auswertung eines Laufs

        private static void Auswerten()
        {
            string log = LogLesen(_z.Log);
            Pruefe(log.Contains("[Ashveil] Bild-Spitzen-Messung v2"), "Bild-Spitzen-Messung v2 im Build (sonst erst spieltest_bauen)");
            Pruefe(log.Contains("[Ashveil] Ruckler-Probe: beginnt"), "Ruckler-Probe gestartet");
            Pruefe(log.Contains("[Ashveil] Ruckler-Probe: fertig"), "Ruckler-Probe zu Ende gelaufen");
            foreach (string zeile in log.Split('\n'))
            {
                int i = zeile.IndexOf("[Ashveil] Ruckler-Probe: ", StringComparison.Ordinal);
                if (i >= 0) _z.Report.AppendLine("  ·      " + zeile.Substring(i + 10).Trim());
            }
            int ausnahmen = log.Split('\n').Count(l => l.Contains("Exception"));
            _z.Report.AppendLine((ausnahmen == 0 ? "  OK     " : "  HINWEIS ") + $"Ausnahmen im Log: {ausnahmen}");

            string sitzung = SitzungFinden();
            if (sitzung == null)
            {
                Pruefe(false, "keine Sitzungsdatei unter Logs/Ashveil/Ruckler");
                _z.Kurz.Append($"Lauf {_z.Index + 1}: keine Sitzungsdatei. ");
                return;
            }
            string text = LogLesen(sitzung);
            _z.Report.AppendLine("  Sitzung: " + sitzung);
            int z = text.IndexOf("=== Zusammenfassung", StringComparison.Ordinal);
            Pruefe(z >= 0, "Zusammenfassung geschrieben");
            if (z >= 0)
            {
                string zf = text.Substring(z).TrimEnd();
                _z.Report.AppendLine(zf);
                string haenger = Zeile(zf, "  Hänger: "), davon = Zeile(zf, "  Davon ≥ 50 ms: "), zust = Zeile(zf, "  Neue Grafik-Zustände in der Sitzung: ");
                _z.Kurz.Append($"Lauf {_z.Index + 1}: Hänger {haenger ?? "?"}");
                if (davon != null) _z.Kurz.Append($"; ≥ 50 ms {davon}");
                if (zust != null) _z.Kurz.Append($"; neue Grafik-Zustände {zust}");
                string gfx = Zeile(zf, "  Grafikspeicher (Unity, beim Treiber angelegt): ");
                if (gfx != null) _z.Kurz.Append($"; Grafikspeicher {gfx}");
                _z.Kurz.Append(". ");
            }
            // die längsten Hänger mit ihren Messpunkten
            var lang = Laengste(text, 8);
            if (lang.Count > 0)
            {
                _z.Report.AppendLine("  Die längsten Hänger:");
                foreach (string block in lang) _z.Report.Append(block);
            }
        }

        /// <summary>Die Sitzungsdatei dieses Laufs (neueste seit dem Start).</summary>
        private static string SitzungFinden()
        {
            string dir = Path.Combine(_z.Folder, "Logs", "Ashveil", "Ruckler");
            if (!Directory.Exists(dir)) return null;
            return new DirectoryInfo(dir).GetFiles("Sitzung_*.txt")
                .Where(f => f.LastWriteTime >= _z.Start.AddSeconds(-5))
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }

        private static readonly Regex Kopf = new Regex(@"^\d\d:\d\d:\d\d Bild \d+: (\d+) ms");

        /// <summary>Die n längsten Hänger, je mit den eingerückten Zeilen darunter.</summary>
        private static List<string> Laengste(string text, int n)
        {
            var bloecke = new List<KeyValuePair<int, string>>();
            string[] zeilen = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < zeilen.Length; i++)
            {
                Match m = Kopf.Match(zeilen[i]);
                if (!m.Success) continue;
                var sb = new StringBuilder("    ").AppendLine(zeilen[i]);
                for (int k = i + 1; k < zeilen.Length && zeilen[k].StartsWith("    ", StringComparison.Ordinal); k++) sb.Append("  ").AppendLine(zeilen[k]);
                bloecke.Add(new KeyValuePair<int, string>(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), sb.ToString()));
            }
            return bloecke.OrderByDescending(b => b.Key).Take(n).Select(b => b.Value).ToList();
        }

        private static string Zeile(string text, string anfang)
        {
            foreach (string roh in text.Replace("\r", "").Split('\n'))
                if (roh.StartsWith(anfang, StringComparison.Ordinal)) return roh.Substring(anfang.Length).Trim();
            return null;
        }

        private static void Abschluss()
        {
            EditorApplication.update -= Tick;
            string path = Ashveil.Core.ProjectPaths.Logs(_bericht);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            _z.Report.AppendLine();
            _z.Report.AppendLine(_z.Fehler == 0 ? (Laeufe.Length == 1 ? "ERGEBNIS: Lauf vollständig" : "ERGEBNIS: beide Läufe vollständig") : $"ERGEBNIS: {_z.Fehler} Prüfung(en) fehlgeschlagen");
            File.WriteAllText(path, _z.Report.ToString());
            string kurz = (_z.Fehler == 0 ? "" : $"{_z.Fehler} Prüfung(en) fehlgeschlagen · ") + _z.Kurz.ToString().Trim() + " Bericht " + path;
            Debug.Log("[Ashveil] Ruckler-Messung fertig → " + path);
            Action<bool, string> fertig = _z.Fertig;
            bool ok = _z.Fehler == 0;
            _z = null;
            fertig?.Invoke(ok, kurz);
        }

        // ================================================================== Hilfen

        /// <summary>„B;x:z:Blickrichtung;…“ aus den Blickpunkten des Kareth-Plans (höchstens 12) und den fernen Orten.</summary>
        private static string BlickArgument(out List<string> namen)
        {
            namen = new List<string>();
            var sb = new StringBuilder("B");
            try
            {
                KarethV4Plan plan = KarethV4Builder.LoadPlan();
                if (plan != null && plan.blickpunkte != null)
                    foreach (KarethV4Blick b in plan.blickpunkte.Take(MaxBlicke))
                    {
                        float yaw = Mathf.Atan2(b.lx - b.x, b.lz - b.z) * Mathf.Rad2Deg;
                        sb.Append(';').Append(b.x.ToString("0.0", CultureInfo.InvariantCulture)).Append(':')
                          .Append(b.z.ToString("0.0", CultureInfo.InvariantCulture)).Append(':').Append(yaw.ToString("0", CultureInfo.InvariantCulture));
                        namen.Add(b.name);
                    }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ashveil] Ruckler-Messung: Blickpunkte nicht lesbar – " + e.Message);
            }
            try
            {
                KarethV4Inhalte.Daten d = KarethV4Inhalte.Load();
                if (d != null && d.orte != null)
                    foreach (var f in Ferne)
                    {
                        KarethV4Inhalte.Ort o = d.orte.FirstOrDefault(x => x.id == f.id);
                        if (o == null) continue;
                        sb.Append(';').Append((o.x + f.dx).ToString("0.0", CultureInfo.InvariantCulture)).Append(':')
                          .Append((o.z + f.dz).ToString("0.0", CultureInfo.InvariantCulture)).Append(":0");
                        namen.Add(f.name);
                    }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ashveil] Ruckler-Messung: ferne Orte nicht lesbar – " + e.Message);
            }
            return sb.ToString();
        }

        /// <summary>Westweiden-Blickpunkte als Argument (Weltkoordinaten = lokal + Ursprung; Höhe nur für die Höhle).</summary>
        private static string WestweidenArgument(out List<string> namen)
        {
            namen = new List<string>();
            var sb = new StringBuilder("B");
            Vector3 v = KarethV4Weidepass.WwUrsprung;
            foreach (var b in WestweidenBlicke)
            {
                sb.Append(';').Append((b.x + v.x).ToString("0.0", CultureInfo.InvariantCulture)).Append(':')
                  .Append((b.z + v.z).ToString("0.0", CultureInfo.InvariantCulture)).Append(':').Append(b.yaw.ToString("0", CultureInfo.InvariantCulture));
                if (!float.IsNaN(b.y)) sb.Append(':').Append((b.y + v.y).ToString("0.0", CultureInfo.InvariantCulture));
                namen.Add(b.name);
            }
            return sb.ToString();
        }

        /// <summary>Datei lesen; hält Windows sie nach dem Beenden noch kurz fest, bis 5 s warten.</summary>
        private static string LogLesen(string path)
        {
            for (int versuch = 0; ; versuch++)
            {
                try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
                catch (IOException) when (versuch < 25) { System.Threading.Thread.Sleep(200); }
            }
        }
    }
}
