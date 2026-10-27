using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Flacker-Probe im fertigen Spiel (09.10.2026, Spieltest r2.2: flackernde Grashalme und Schatten beim Laufen) – Auftrag
    /// „flacker_probe“ und Menü Ashveil › Spieltest r2 › Flacker-Probe. Startet den Spieltest-Build einmal mit
    /// <see cref="Ashveil.Networking.FlackerProbe"/> (Profil „kareth-flacker“, Test-Profil am Wachtfeuer, Weltuhr Vormittag).
    /// Parameter (optional): „x:z:Blick“ als Startpunkt; sonst der erste Blickpunkt des Kareth-Plans. Bericht und PNGs schreibt
    /// das Spiel nach Builds/Spieltest_r2/Logs/Ashveil/Flacker/; die Zusammenfassung steht in der Auftragsmeldung.
    /// Ändert weder Build noch Projekt; Max' Profilzeiger und grafik.json bleiben unberührt.
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragFlacker
    {
        private const double LaufMinuten = 6.0;

        private sealed class Zustand
        {
            public string Folder, Log;
            public Process Proc;
            public DateTime Start;
            public double Bis;
            public Action<bool, string> Fertig;
            public string Probe, Unterordner;   // Startargument des Spiels und Ordner der Ergebnisse
        }

        private static Zustand _z;

        static AuftragFlacker()
        {
            Auftragsordner.Registrieren("flacker_probe", (a, f) => Messen(a, f, Ashveil.Networking.FlackerProbe.Argument, "Flacker", "Flacker-Probe"), false, (int)(LaufMinuten * 60.0) + 120);
            Auftragsordner.Registrieren("gras_probe", (a, f) => Messen(a, f, Ashveil.Networking.GrasProbe.Argument, "Gras", "Gras-Probe"), false, (int)(LaufMinuten * 60.0) + 120);
        }

        [MenuItem("Ashveil/Spieltest r2/Flacker-Probe (1 Start)", priority = 4)]
        private static void Menue() => Messen(null, (ok, m) => Debug.Log("[Ashveil] Flacker-Probe: " + m), Ashveil.Networking.FlackerProbe.Argument, "Flacker", "Flacker-Probe");

        [MenuItem("Ashveil/Spieltest r2/Gras-Probe (1 Start)", priority = 5)]
        private static void MenueGras() => Messen(null, (ok, m) => Debug.Log("[Ashveil] Gras-Probe: " + m), Ashveil.Networking.GrasProbe.Argument, "Gras", "Gras-Probe");

        private static void Messen(Auftragsordner.Auftrag a, Action<bool, string> fertig, string probeArgument, string unterordner, string probeName)
        {
            if (_z != null) { fertig(false, "Flacker-Probe läuft schon"); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) { fertig(false, "Play läuft – nichts gestartet"); return; }
            string folder = Path.GetFullPath(SpieltestBuild.Folder);
            if (!File.Exists(Path.Combine(folder, "Ashveil.exe"))) { fertig(false, "Spieltest-Build fehlt: " + folder + " (erst spieltest_bauen)"); return; }

            string punkt = a != null && !string.IsNullOrEmpty(a.parameter) ? a.parameter.Trim() : Startpunkt();
            _z = new Zustand { Folder = folder, Fertig = fertig, Start = DateTime.Now, Probe = probeName, Unterordner = unterordner };
            NetworkSetupBuilder.StopTestClients();
            _z.Log = Path.Combine(folder, unterordner + ".log");
            string args = $"-ashveil-host -ashveil-profile kareth-flacker {NetworkSetupBuilder.ZeitVormittag} " +
                          $"{probeArgument} {punkt}";
            try
            {
                _z.Proc = Process.Start(new ProcessStartInfo(Path.Combine(folder, "Ashveil.exe"), args + $" -logFile \"{_z.Log}\"")
                    { WorkingDirectory = folder, UseShellExecute = false });
            }
            catch (Exception e)
            {
                _z = null;
                fertig(false, "Prozess startet nicht: " + e.Message);
                return;
            }
            Debug.Log($"[Ashveil] {probeName} gestartet (PID {_z.Proc.Id}): {args}");
            _z.Bis = EditorApplication.timeSinceStartup + LaufMinuten * 60.0;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (_z == null) { EditorApplication.update -= Tick; return; }
            bool ende = _z.Proc.HasExited;
            if (!ende && EditorApplication.timeSinceStartup < _z.Bis) return;
            var sb = new StringBuilder();
            bool ok = true;
            if (!ende)
            {
                ok = false;
                sb.Append($"Spiel nach {LaufMinuten:0} min nicht von selbst beendet – Fenster wird geschlossen. ");
                // 22.10.2026 (Runde 5): echtes WM_CLOSE (PlaytestSmoke.WmCloseSenden) statt Process.CloseMainWindow – das ist im Mono des
                // Editors ein hartes TerminateProcess mit Exit -2 (ohne Abbau, ohne Schluss-Zusammenfassungen).
                bool gesendet = PlaytestSmoke.WmCloseSenden(_z.Proc);
                if (!_z.Proc.WaitForExit(15000))
                {
                    sb.Append("Spiel reagiert nicht – hart beendet" + (gesendet ? "" : " (WM_CLOSE nicht gesendet)") + ". ");
                    try { _z.Proc.Kill(); } catch (Exception) { }
                }
                else sb.Append($"Über das Fenster geschlossen (WM_CLOSE {(gesendet ? "gesendet" : "NICHT gesendet")}), Exit-Code {_z.Proc.ExitCode}. ");
            }
            _z.Proc.Dispose();
            EditorApplication.update -= Tick;

            string log = LogLesen(_z.Log);
            string[] zeilen = log.Replace("\r", "").Split('\n');
            string marke = "[Ashveil] " + _z.Probe + ": ";
            var probe = zeilen.Where(l => l.Contains(marke)).Select(l => l.Substring(l.IndexOf(marke, StringComparison.Ordinal) + marke.Length).Trim()).ToList();
            if (probe.Count == 0) { ok = false; sb.Append($"keine Zeilen der {_z.Probe} im Log (Build ohne Probe? erst spieltest_bauen). "); }
            if (probe.Any(l => l.StartsWith("FEHLER", StringComparison.Ordinal))) ok = false;
            if (!probe.Any(l => l.StartsWith("fertig", StringComparison.Ordinal))) { ok = false; sb.Append("Probe nicht zu Ende gelaufen. "); }
            int z = probe.FindIndex(l => l.StartsWith("=== Zusammenfassung", StringComparison.Ordinal));
            if (z >= 0) sb.AppendLine().Append(string.Join("\n", probe.Skip(z)));
            else sb.AppendLine().Append(string.Join("\n", probe.Take(60)));
            string ordner = Path.Combine(_z.Folder, "Logs", "Ashveil", _z.Unterordner);
            sb.AppendLine().Append("Bericht und Bilder: ").Append(ordner);
            Action<bool, string> fertig = _z.Fertig;
            _z = null;
            fertig?.Invoke(ok, sb.ToString());
        }

        /// <summary>Erster Blickpunkt des Kareth-Plans als „x:z:Blick“; sonst leer (Probe nimmt ihren Standard).</summary>
        private static string Startpunkt()
        {
            try
            {
                KarethV4Plan plan = KarethV4Builder.LoadPlan();
                KarethV4Blick b = plan?.blickpunkte?.FirstOrDefault();
                if (b == null) return "";
                float yaw = Mathf.Atan2(b.lx - b.x, b.lz - b.z) * Mathf.Rad2Deg;
                return b.x.ToString("0.0", CultureInfo.InvariantCulture) + ":" + b.z.ToString("0.0", CultureInfo.InvariantCulture) + ":" + yaw.ToString("0", CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ashveil] Flacker-Probe: Blickpunkt nicht lesbar – " + e.Message);
                return "";
            }
        }

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
