using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// CORE-005 / NET-012 (02.10.2026): erster Bot-Lasttest auf einem Rechner.
    ///   1. Build nach Builds/LoadTest (Development, keine Projekteinstellung geaendert)
    ///   2. Server ohne Grafik:  -batchmode -nographics -ashveil-server -ashveil-loadtest … (misst sich selbst)
    ///   3. N Bots ohne Grafik:  -batchmode -nographics -ashveil-join 127.0.0.1 -ashveil-bot (spielen ueber echte Eingaben)
    ///   4. Nach der Messzeit: Bots beenden, Bericht Logs/Ashveil/Ashveil_Lasttest.txt
    /// Beendet nur die eigenen Prozesse (ein laufendes Spiel von Max bleibt unberuehrt).
    /// Hinweis im Bericht: Server und Bots teilen sich einen Rechner – die Werte sind eine Obergrenze der Last.
    /// </summary>
    [InitializeOnLoad]
    public static class LoadTestRunner
    {
        private const string Folder = "Builds/LoadTest";
        /// <summary>Ordner des laufenden Tests (Builds/LoadTest = Sandbox-Build, oder der fertige Spieltest-Build mit Kareth v4).</summary>
        private static string _exeFolder = Folder;
        private static System.Action<bool, string> _done;

        /// <summary>
        /// 11.10.2026 (Max: „hunderte Spieler zeitgleich wie in WoW“): Auftrag „lasttest“ – Server und Bots aus dem fertigen
        /// Spieltest-Build (Kareth v4 mit der Weltfülle), ohne neu zu bauen. Parameter „Bots“, „Bots|Sekunden“ oder „Bots|Sekunden|diag“ (Standard 50|300; diag = Ausschluss-Messung ab 110 s).
        /// Nur starten, wenn Max nicht spielt (gleicher Netzwerk-Port).
        /// </summary>
        static LoadTestRunner()
        {
            Auftragsordner.Registrieren("lasttest", (a, fertig) =>
            {
                string[] p = (a.parameter ?? "").Split('|');
                int bots = p.Length > 0 && int.TryParse(p[0].Trim(), out int b) && b > 0 ? b : 50;
                float sec = p.Length > 1 && float.TryParse(p[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float t) && t > 0 ? t : 300f;
                string folder = Path.GetFullPath(SpieltestBuild.Folder);
                if (!File.Exists(Path.Combine(folder, "Ashveil.exe"))) { fertig(false, "Spieltest-Build fehlt (erst spieltest_bauen)"); return; }
                bool diag = p.Length > 2 && p[2].Trim().ToLowerInvariant() == "diag"; // Ausschluss-Messung: wer kostet am Server
                Run(bots, sec, diag, folder, fertig);
            }, false, 3600);
        }
        private static readonly List<Process> Procs = new List<Process>();
        private static int _bots, _started;
        private static float _seconds;
        private static double _t0, _nextBot;
        private static string _dir, _csv, _serverLog;
        private static Process _server;
        private static double _measuredAt, _nextPoll; // Messung fertig (Zusammenfassung geschrieben) → Server muss sich danach beenden
        private static double _exitSeconds = -1;

        [MenuItem("Ashveil/Lasttest/Server + 10 Bots (3 min)", priority = 1)]
        private static void Run10() => Run(10, 180f);

        [MenuItem("Ashveil/Lasttest/Server + 25 Bots (5 min)", priority = 2)]
        private static void Run25() => Run(25, 300f);

        [MenuItem("Ashveil/Lasttest/Server + 50 Bots (5 min)", priority = 3)]
        private static void Run50() => Run(50, 300f);

        [MenuItem("Ashveil/Lasttest/50 Bots – Diagnose: wer kostet am Server (8 min)", priority = 4)]
        private static void Run50Diag() => Run(50, 480f, true);

        private static bool _diag;

        private static void Run(int bots, float seconds, bool diag = false, string exeFolder = null, System.Action<bool, string> done = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (done != null) done(false, "Play läuft – nichts gestartet");
                else EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst Play beenden.", "OK");
                return;
            }
            if (Procs.Count > 0) { Debug.LogWarning("[Ashveil][Lasttest] Laeuft bereits."); done?.Invoke(false, "Lasttest läuft schon"); return; }
            _exeFolder = exeFolder ?? Folder;
            _done = done;
            if (exeFolder == null && !NetworkSetupBuilder.BuildPlaytest(Folder)) return;
            _bots = bots;
            _seconds = seconds;
            _diag = diag;
            _started = 0;
            _measuredAt = 0;
            _nextPoll = 0;
            _exitSeconds = -1;
            _dir = Path.GetFullPath(Path.Combine("Logs", "Ashveil", "Lasttest"));
            Directory.CreateDirectory(_dir);
            foreach (string f in Directory.GetFiles(_dir)) { try { File.Delete(f); } catch (IOException) { } }
            _csv = Path.Combine(_dir, "server.csv");
            _serverLog = Path.Combine(_dir, "server.log");
            string exe = Path.GetFullPath(Path.Combine(_exeFolder, "Ashveil.exe"));
            // Diagnose: ab Sekunde 110 (alle Bots da) je Komponententyp kurz abschalten und die Ersparnis messen
            _server = Start(exe, (diag ? "-ashveil-loadtest-ablation 110 " : "") + $"-batchmode -nographics -ashveil-server -ashveil-loadtest \"{_csv}\" " +
                                 $"-ashveil-loadtest-seconds {seconds.ToString(CultureInfo.InvariantCulture)} -logFile \"{_serverLog}\"");
            _t0 = EditorApplication.timeSinceStartup;
            _nextBot = _t0 + 20.0; // Server laedt Welt und NavMesh
            EditorApplication.update += Tick;
            Debug.Log($"[Ashveil][Lasttest] Server gestartet, {bots} Bots folgen, Messzeit {seconds:0} s.");
        }

        /// <summary>
        /// Bots leichter (11 §AQ, Messung 11.10. nachts): Ein Bot ohne Grafik belegte ≈ 600 MB, obwohl Unity nur 375 MB wirklich nutzte –
        /// der Rest sind reservierte Zuteiler-Blöcke (16 MB je Block, je Arbeitsfaden einer; 24 Kerne → 23 Jobfäden) und Spielraum.
        /// Darum: nur 2 Jobfäden (Bots rechnen fast nichts), kleinere Blöcke, Grafik-Zuteiler minimal. Reine Startparameter, keine Codeänderung
        /// im Spiel; gilt nur für die Bots des Prüfstands, nicht für Server oder Spieler. Ergebnis im Lasttest-Bericht (rechner_frei_mb je Bot).
        /// </summary>
        private const string BotSpeicherArgs =
            "-job-worker-count 2 " +
            "-memorysetup-main-allocator-block-size=4194304 -memorysetup-thread-allocator-block-size=2097152 " +
            "-memorysetup-gfx-main-allocator-block-size=1048576 -memorysetup-gfx-thread-allocator-block-size=1048576 " +
            "-memorysetup-bucket-allocator-block-size=1048576 -memorysetup-bucket-allocator-block-count=2 " +
            "-memorysetup-cache-allocator-block-size=1048576 -memorysetup-typetree-allocator-block-size=1048576 " +
            "-memorysetup-temp-allocator-size-main=1048576 -memorysetup-job-temp-allocator-block-size=524288 " +
            "-memorysetup-temp-allocator-size-job-worker=131072 -memorysetup-temp-allocator-size-background-worker=16384 " +
            "-memorysetup-temp-allocator-size-nav-mesh-worker=32768 -memorysetup-temp-allocator-size-audio-worker=16384 " +
            "-memorysetup-temp-allocator-size-preload-manager=131072 -memorysetup-temp-allocator-size-gfx=65536";

        private static Process Start(string exe, string args)
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p != null) Procs.Add(p);
            return p;
        }

        private static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            string exe = Path.GetFullPath(Path.Combine(_exeFolder, "Ashveil.exe"));
            if (_started < _bots && now >= _nextBot)
            {
                _started++;
                string id = _started.ToString("00");
                Start(exe, $"-batchmode -nographics {BotSpeicherArgs} -ashveil-join 127.0.0.1 -ashveil-profile bot-{id} -ashveil-autocreate -ashveil-bot " +
                           $"-logFile \"{Path.Combine(_dir, $"bot_{id}.log")}\"");
                _nextBot = now + 1.5;
            }
            if (_measuredAt == 0 && now >= _nextPoll)
            {
                _nextPoll = now + 2.0;
                if (SummaryWritten()) _measuredAt = now;
            }

            bool serverGone = _server == null || _server.HasExited;
            if (serverGone && _measuredAt > 0 && _exitSeconds < 0) _exitSeconds = now - _measuredAt;
            // Nach der Messung hoechstens 30 s fuers Herunterfahren; sonst Zeitlimit wie bisher
            if (serverGone || (_measuredAt > 0 && now - _measuredAt > 30.0) || now - _t0 > _seconds + 120.0) Finish(serverGone);
        }

        private static void Finish(bool serverEnded)
        {
            EditorApplication.update -= Tick;
            // Erst alle beenden, dann gemeinsam hoechstens 10 s warten (einzeln je 5 s dauerte das mit 50 Bots Minuten)
            foreach (Process p in Procs)
                try { if (!p.HasExited) p.Kill(); } catch (System.Exception) { }
            var sw = Stopwatch.StartNew();
            foreach (Process p in Procs)
            {
                try { int left = 10000 - (int)sw.ElapsedMilliseconds; if (left > 0 && !p.HasExited) p.WaitForExit(left); } catch (System.Exception) { }
                p.Dispose();
            }
            Procs.Clear();
            WriteReport(serverEnded);
        }

        private static bool SummaryWritten()
        {
            try
            {
                if (!File.Exists(_csv)) return false;
                using var fs = new FileStream(_csv, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var r = new StreamReader(fs);
                return r.ReadToEnd().Contains("ZUSAMMENFASSUNG");
            }
            catch (IOException) { return false; }
        }

        private static int CountIn(string file, string needle)
        {
            if (!File.Exists(file)) return -1;
            int n = 0;
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var r = new StreamReader(fs);
                string line;
                while ((line = r.ReadLine()) != null) if (line.Contains(needle)) n++;
            }
            catch (IOException) { return -1; }
            return n;
        }

        private static void WriteReport(bool serverEnded)
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            void Check(bool ok, string what) { sb.AppendLine((ok ? "  OK    " : "  FEHLER ") + what); if (ok) pass++; else fail++; }
            sb.AppendLine($"ASHVEIL Lasttest (NET-012, CORE-005): Server ohne Grafik + {_bots} Bots ohne Grafik, ein Rechner · {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}" +
                          (_diag ? " · DIAGNOSE (Ausschluss-Messung ab 110 s, Spiel waehrenddessen absichtlich gestoert)" : ""));
            sb.AppendLine("  ·     Server und alle Bots laufen auf demselben Rechner – die Bots kosten selbst Rechenzeit (gedrosselt auf 20 Bilder/s). Werte sind daher eine vorsichtige Obergrenze. „Arbeit“ = reine Rechenzeit des Servers je Bild, „cpu_kerne“ = belegte Prozessorkerne des Servers.");
            bool measured = SummaryWritten();
            Check(measured, "Server laeuft die ganze Messzeit durch (kein Absturz, Messung vollstaendig)");
            string quitFlag = Path.Combine(_dir, "server_beenden.txt");
            bool watchdog = File.Exists(quitFlag) && File.ReadAllText(quitFlag).Contains("Waechter");
            if (watchdog) serverEnded = false;
            Check(serverEnded, watchdog ? "Server faehrt danach sauber herunter (Beenden hing 15 s – vom Waechter hart beendet)" : serverEnded
                ? $"Server faehrt danach sauber herunter ({(_exitSeconds >= 0 ? _exitSeconds.ToString("0", CultureInfo.InvariantCulture) + " s" : "beendet")})"
                : "Server faehrt danach sauber herunter (hing laenger als 30 s – zwangsbeendet)");
            int botsActive = 0, botExceptions = 0;
            for (int i = 1; i <= _bots; i++)
            {
                string f = Path.Combine(_dir, $"bot_{i:00}.log");
                if (CountIn(f, "[Ashveil][Bot] Lasttest-Bot aktiv") > 0) botsActive++;
                botExceptions += System.Math.Max(0, CountIn(f, "Exception"));
            }
            Check(botsActive == _bots, $"Alle Bots sind in der Welt und spielen ({botsActive}/{_bots})");
            int serverExceptions = CountIn(_serverLog, "Exception");
            Check(serverExceptions == 0, $"Keine Ausnahmen im Server-Log ({serverExceptions})");
            Check(botExceptions == 0, $"Keine Ausnahmen in den Bot-Logs ({botExceptions})");
            string summary = File.Exists(_csv) ? File.ReadAllLines(_csv).FirstOrDefault(l => l.StartsWith("ZUSAMMENFASSUNG")) : null;
            if (summary != null)
            {
                var kv = summary.Split(';').Skip(1).Select(x => x.Split('=')).Where(x => x.Length == 2)
                    .ToDictionary(x => x[0], x => float.Parse(x[1], CultureInfo.InvariantCulture));
                Check(kv["max_spieler"] >= _bots, $"Server sieht alle Bots als Spielerfiguren (hoechstens {kv["max_spieler"]:0} gleichzeitig)");
                Check(kv["arbeit_p99_ms"] <= 16.7f, $"Server-Arbeit passt in den 60-Hz-Takt: 99 % der Bilder unter 16,7 ms Rechenzeit ({kv["arbeit_p99_ms"]:0.0} ms, Mittel {kv["arbeit_mittel_ms"]:0.0} ms)");
                Check(kv["bild_p99_ms"] <= 33.4f, $"Server-Takt stabil: 99 % der Bilder unter 33 ms ({kv["bild_p99_ms"]:0.0} ms, Mittel {kv["bild_mittel_ms"]:0.0} ms)");
                // 11.10.2026: Ein langes Bild, in dem die Spielschleife selbst kurz war, ist kein Serverbefund – der Prozess war nicht am Zug
                // (Rechner mit 51 Prozessen, Auslagerung bei vollem Speicher). Der Bericht nennt beides.
                float schleife = kv.TryGetValue("bild_max_schleife_ms", out float sl) ? sl : -1f;
                bool haenger = kv["bild_max_ms"] > 250f;
                bool ausserhalb = haenger && schleife >= 0f && schleife <= 100f;
                Check(!haenger || ausserhalb, ausserhalb
                    ? $"Kein grosser Haenger in der Spielschleife des Servers (laengstes Bild {kv["bild_max_ms"]:0} ms, davon Spielschleife nur {schleife:0} ms – der Rest fiel ausserhalb an: Rechner/Pruefstand; 99,9 % {kv["bild_p999_ms"]:0} ms)"
                    : $"Kein grosser Haenger am Server waehrend gespielt wird (laengstes Bild {kv["bild_max_ms"]:0} ms" + (schleife >= 0f ? $", davon Spielschleife {schleife:0} ms" : "") + $", 99,9 % {kv["bild_p999_ms"]:0} ms)");
                sb.AppendLine($"  ·     Gemessen ab der ersten Verbindung. Serverstart (Welt, NavMesh) laengstes Bild {kv["start_max_ms"]:0} ms – vor dem ersten Spieler, getrennt bewertet.");
                sb.AppendLine($"  ·     Speicherbereinigungen am Server: {kv["gc"]:0}");
                // 11.10.2026: 51 Prozesse auf einem Rechner – ist der Arbeitsspeicher voll, lagert Windows aus; Haenger sind dann Pruefstand, nicht Server
                if (kv.TryGetValue("rechner_last_max_pct", out float last) && kv.TryGetValue("rechner_gesamt_mb", out float gesamt))
                    Check(last <= 90f, $"Pruefstand nicht im Speichermangel: Arbeitsspeicher des Rechners hoechstens {last:0} % belegt ({gesamt / 1024f:0} GB eingebaut; 50 Bots brauchen ≈ 30 GB, je Bot ein ganzer Spielprozess)");
            }
            else Check(false, "Server-Messung vorhanden (server.csv)");
            string parts = File.Exists(_csv) ? File.ReadAllLines(_csv).FirstOrDefault(l => l.StartsWith("AUFSCHLUESSELUNG;")) : null;
            if (!string.IsNullOrEmpty(parts))
            {
                sb.AppendLine("  ·     Wo die Server-Zeit hingeht (Mittel je Bild, ms; Ober- und Unterpunkte stehen beide drin):");
                foreach (string p in parts.Substring("AUFSCHLUESSELUNG;".Length).Split('|')) sb.AppendLine("          " + p.Trim());
            }
            string mem = File.Exists(_csv) ? File.ReadAllLines(_csv).FirstOrDefault(l => l.StartsWith("SPEICHER;")) : null;
            if (!string.IsNullOrEmpty(mem)) sb.AppendLine("  ·     Speicher-Anforderungen am Server (Muell fuer die Bereinigung): " + mem.Substring("SPEICHER;".Length).Replace(";", ", "));
            string saves = File.Exists(_csv) ? File.ReadAllLines(_csv).FirstOrDefault(l => l.StartsWith("SPEICHERN;")) : null;
            if (!string.IsNullOrEmpty(saves)) sb.AppendLine("  ·     Speichern der Charaktere (Hintergrund-Faden): " + saves.Substring("SPEICHERN;".Length).Replace(";", ", "));
            if (File.Exists(quitFlag)) foreach (string l in File.ReadAllLines(quitFlag)) sb.AppendLine("  ·     Beenden: " + l);
            string abl = Path.Combine(_dir, "server_ausschluss.txt");
            if (File.Exists(abl))
            {
                sb.AppendLine("  ·     Ausschluss-Messung (wer kostet wie viel; Ersparnis in ms je Bild):");
                foreach (string l in File.ReadAllLines(abl)) sb.AppendLine("          " + l);
            }
            // Bleiben alle verbunden? (02.10.: 2 von 50 Bots fielen bei Sekunden-Haengern heraus)
            if (File.Exists(_csv))
            {
                var rows = File.ReadAllLines(_csv).Skip(1).Where(l => l.Length > 0 && char.IsDigit(l[0])).Select(l => l.Split(';')).ToList();
                if (rows.Count > 0)
                {
                    int last = int.Parse(rows[rows.Count - 1][1], CultureInfo.InvariantCulture);
                    Check(last >= _bots, $"Alle Bots bleiben bis zum Ende verbunden ({last}/{_bots})");
                }
            }
            int kills = System.Math.Max(0, CountIn(_serverLog, "besiegt"));
            sb.AppendLine($"  ·     Kaempfe: {kills} Gegner von Bots besiegt (Server-Log).");
            // Kaempfen die Bots wirklich? Gegnerzahl am Server muss im Verlauf sinken (Tote, Wiederbelebung)
            if (File.Exists(_csv))
            {
                var enemies = File.ReadAllLines(_csv).Skip(1).Where(l => l.Length > 0 && char.IsDigit(l[0]))
                    .Select(l => l.Split(';')).Where(x => x.Length > 3).Select(x => int.Parse(x[3], CultureInfo.InvariantCulture)).ToList();
                if (enemies.Count > 0)
                    sb.AppendLine($"  ·     Gegner am Leben: zu Beginn {enemies[0]}, kleinster Wert {enemies.Min()} – " +
                                  (enemies.Min() < enemies[0] ? "die Bots kaempfen." : "die Bots haben nichts getoetet (reiner Bewegungs-Lasttest)."));
            }
            sb.AppendLine();
            sb.AppendLine("Verlauf (alle 5 s): siehe Logs/Ashveil/Lasttest/server.csv");
            if (File.Exists(_csv))
                foreach (string l in File.ReadAllLines(_csv).Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("ZUSAMMENFASSUNG") && !l.StartsWith("AUFSCHLUESSELUNG") && !l.StartsWith("SPEICHER")).Take(80))
                    sb.AppendLine("  " + l);
            sb.AppendLine();
            sb.AppendLine($"ERGEBNIS: {pass} bestanden, {fail} fehlgeschlagen");
            string report = Path.GetFullPath(Path.Combine("Logs", "Ashveil", "Ashveil_Lasttest.txt"));
            File.WriteAllText(report, sb.ToString());
            Debug.Log($"[Ashveil][Lasttest] Fertig: {pass} ok, {fail} fehlgeschlagen → {report}");
            System.Action<bool, string> done = _done;
            _done = null;
            done?.Invoke(fail == 0, $"Lasttest {_bots} Bots ({System.IO.Path.GetFileName(_exeFolder.TrimEnd('/', '\\'))}): {pass} bestanden, {fail} fehlgeschlagen · Bericht {report}");
        }
    }
}
