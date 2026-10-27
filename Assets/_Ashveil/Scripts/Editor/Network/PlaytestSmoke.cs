using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Pre-P4 Sanity Check (02.10.2026): startet den ECHTEN Build dreimal kurz und liest dessen Unity-Log (-logFile).
    ///   Lauf 1 – wie "Neues Profil.bat": ohne Profil-Datei, -ashveil-host → Welt laedt, Charakter-Editor wird angefordert
    ///   Lauf 2 – Smoke-Profil mit -ashveil-autocreate → Charakter wird angelegt, Speicherstand entsteht (die Zeile "betritt die Welt bei" erscheint nur beim Laden einer gespeicherten Position)
    ///   Lauf 3 – gleiches Profil erneut → Speicherstand wird geladen
    ///   Lauf 4 (seit 22.10.2026, nur mit lauf4Laden) – nach „Speichern und beenden“ erneut laden, Stelle = Speicherstand; mit
    ///          Probe-Argumenten (seit der Nachbesserung 22.10.) ein paar Schritte, dann über das Fenster schliessen → Speicherstand
    ///          muss zwischen WM_CLOSE und Prozessende geschrieben sein und die Stelle beim Schliessen tragen
    /// Jeder Lauf wird nach ~45 s ueber das Fenster geschlossen, nicht hart beendet (seit 22.10.2026 echtes WM_CLOSE per PostMessage –
    /// Process.CloseMainWindow beendete im Mono des Editors hart mit Exit -2).
    /// Nur Werkzeug, aendert weder Build noch Projekt. Bericht: Logs/Ashveil/Ashveil_P4Smoke.txt (P4) bzw. der Bericht des Aufrufers.
    /// Der Profilzeiger des Builds (ashveil_profile.txt) wird vorher gesichert und danach wiederhergestellt bzw. entfernt,
    /// damit Max mit "Neues Profil.bat" frisch beginnt; die Smoke-Spielstaende bleiben unter Saves/Characters/_smoke_*.
    /// Seit #30 (08.10.2026) auch fuer den Spieltest-Build r2 (<see cref="SpieltestBuild"/>): eigener Ordner, eigenes Profil,
    /// Zusatzpruefungen je Lauf und Rueckmeldung an den Auftragsordner.
    /// Seit r2.1 (UI-204, 09.10.2026) optional mit Zusatz-Argumenten fuer Lauf 3: Dann prueft das Spiel selbst die Esc-Regel
    /// und beendet sich ueber „Speichern und beenden“ – erwartet wird, dass es sich selbst schliesst (bis 90 s).
    /// Seit 22.10.2026 (Runde 5, RELEASE-02): Die Sicherung (ashveil_profile.txt.vor_smoke) wird nie überschrieben. Bleibt sie nach
    /// einem abgebrochenen Lauf liegen (Domain-Reload durch Neukompilieren, Unity-Absturz), legt sie der nächste Start – und schon
    /// das Neuladen des Editors (<see cref="VerwaisteSicherungenPruefen"/>) – zuerst zurück; Protokoll in
    /// Logs/Ashveil/Ashveil_Kurztest_Profilzeiger.txt.
    /// Seit 22.10.2026 (Runde 6, Codex-Paket A, REP-20261022-CODEX-PROFILZEIGER-REVIEW): VOR jeder Zeigeränderung (Start und Ende)
    /// wird geprüft, ob ein Spiel aus genau diesem Build-Ordner läuft – unabhängig davon, ob eine Sicherung liegt. Dreiwertig:
    /// läuft / sicher keins / nicht feststellbar (Programmpfad eines noch laufenden Ashveil.exe unlesbar); die beiden letzten
    /// Fälle blockieren und werden protokolliert. Ein fremdes Spiel wird nie beendet (nur NetworkSetupBuilder.StopTestClients
    /// beendet weiterhin den Test-Client unter Builds/TestClient). Restgrenze: Zwischen Prüfung und Zeigeränderung liegt kein
    /// Schloss – startet Max genau in diesem Augenblick ein Spiel aus dem Ordner, sieht die Prüfung es nicht.
    /// </summary>
    [InitializeOnLoad]
    public static class PlaytestSmoke
    {
        private const float RunSeconds = 45f;
        private static readonly StringBuilder Report = new StringBuilder();
        private static int _step, _fehler;
        private static double _until;
        private static Process _proc;
        private static string _folder, _log, _profil, _titel, _bericht;
        private static Action<string, int> _zusatz;
        private static Action<bool, string> _fertig;
        private static bool _laeuft;
        /// <summary>Zusatz-Argumente fuer Lauf 3 (leer = wie bisher); gesetzt = das Spiel beendet sich in Lauf 3 selbst.</summary>
        private static string _lauf3Zusatz;
        private const float SelbstEndeSekunden = 90f;
        /// <summary>
        /// 22.10.2026 (Releaseablauf): Zusatz-Argumente fuer Lauf 1 und 2 (Kurztest-Probe im fertigen Spiel: Namensfeld, F2, ein paar
        /// Schritte) und ein vierter Lauf, der nach „Speichern und beenden“ erneut laedt und die Stelle mit dem Speicherstand vergleicht.
        /// Ohne diese Angaben bleibt der Ablauf wie bisher (P4-Smoke).
        /// </summary>
        private static string _lauf12Zusatz;
        private static bool _lauf4;
        private const float ProbeSekunden = 60f;
        private const float StellenToleranz = 1.5f;
        private static float[] _stelleLauf2, _stelleLauf3;
        private static int LetzterLauf => _lauf4 ? 4 : 3;

        [MenuItem("Ashveil/P3/P4-Build Smoke-Test (3 kurze Starts)", priority = 5)]
        public static void Run()
        {
            string folder = Path.GetFullPath(PlaytestBuildMenu.Folder);
            if (!File.Exists(Path.Combine(folder, "Ashveil.exe")))
            {
                EditorUtility.DisplayDialog("Ashveil", "P4-Build fehlt: " + folder, "OK");
                return;
            }
            Starten(folder, "_smoke_p4", "P4-Build", "Ashveil_P4Smoke.txt", null, null);
        }

        /// <summary>
        /// Drei kurze Starts eines Builds. zusatz(logText, lauf) prueft je Lauf weitere Zeilen (mit <see cref="Check"/>);
        /// fertig(ok, meldung) kommt nach dem dritten Lauf – ok, wenn keine Pruefung fehlschlug.
        /// </summary>
        public static void Starten(string folder, string profil, string titel, string bericht, Action<string, int> zusatz, Action<bool, string> fertig,
            string lauf3Zusatz = null, string lauf12Zusatz = null, bool lauf4Laden = false)
        {
            if (_laeuft)
            {
                fertig?.Invoke(false, "Smoke-Test läuft schon");
                return;
            }
            _folder = folder;
            _profil = profil;
            _titel = titel;
            _bericht = bericht;
            _zusatz = zusatz;
            _fertig = fertig;
            _lauf3Zusatz = string.IsNullOrEmpty(lauf3Zusatz) ? null : lauf3Zusatz;
            _lauf12Zusatz = string.IsNullOrEmpty(lauf12Zusatz) ? null : lauf12Zusatz;
            _lauf4 = lauf4Laden;
            _stelleLauf2 = _stelleLauf3 = null;
            _zeitLauf3 = _zelleLauf3 = null;
            _fehler = 0;
            NetworkSetupBuilder.StopTestClients();
            // 22.10.2026 (Runde 5, RELEASE-02): eine Sicherung aus einem abgebrochenen Lauf ZUERST zurücklegen (bzw. beide behalten) und
            // die Sicherung nie überschreiben – vorher kopierte jeder Start mit overwrite=true, ein zweiter Lauf nach einem Abbruch
            // überschrieb Max' Zeiger also endgültig mit dem Zufallszeiger aus Lauf 1.
            // 22.10.2026 (Runde 6, Codex-Paket A): davor die Prozessprüfung – auch ohne liegengebliebene Sicherung (vorher prüfte nur
            // VerwaisteSicherungZurueck, und das kehrte ohne .vor_smoke vor der Prüfung zurück). Alles in ZeigerVorbereiten (testbar).
            string pointer = Path.Combine(_folder, "ashveil_profile.txt");
            string abgelehnt = ZeigerVorbereiten(_folder, out string wiederher);
            if (abgelehnt != null)
            {
                fertig?.Invoke(false, "Kurztest nicht gestartet: " + abgelehnt);
                return;
            }
            _eigenePids.Clear();
            _laeuft = true;
            Report.Clear();
            Report.AppendLine($"ASHVEIL {_titel} Smoke-Test · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Report.AppendLine("Build: " + _folder);
            if (wiederher != null) Hinweis("Profilzeiger aus einem abgebrochenen Lauf: " + wiederher);
            if (File.Exists(pointer)) File.Delete(pointer);
            // Alten Smoke-Spielstand entfernen, sonst laedt Lauf 2 ihn, statt neu anzulegen
            string oldSmoke = Path.Combine(_folder, "Saves", "Characters", _profil + ".json");
            if (File.Exists(oldSmoke)) File.Delete(oldSmoke);
            if (File.Exists(oldSmoke + ".bak")) File.Delete(oldSmoke + ".bak"); // sonst stellt das Spiel die Sicherung wieder her
            _step = 0;
            Next();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        /// <summary>Pruefzeile in den Bericht (auch fuer Zusatzpruefungen); FEHLER zaehlt gegen das Ergebnis.</summary>
        public static void Check(bool ok, string label)
        {
            Report.AppendLine((ok ? "  OK    " : "  FEHLER ") + label);
            if (!ok) _fehler++;
        }

        /// <summary>22.10.2026: Hinweiszeile in den Bericht (zaehlt nicht gegen das Ergebnis, z. B. „nicht geprüft“).</summary>
        public static void Hinweis(string text) => Report.AppendLine("  HINWEIS " + text);

        private static void Start(string label, string args)
        {
            _log = Path.Combine(_folder, $"Smoke_{_step}.log");
            if (File.Exists(_log)) File.Delete(_log);
            Report.AppendLine("");
            Report.AppendLine($"[{label}] Argumente: {args}");
            try
            {
                _proc = Process.Start(new ProcessStartInfo(Path.Combine(_folder, "Ashveil.exe"), args + $" -logFile \"{_log}\"")
                    { WorkingDirectory = _folder, UseShellExecute = false });
                _eigenePids.Add(_proc.Id);   // 22.10.2026 (Runde 6, Codex-Paket A): eigene Läufe zählen am Ende nicht als fremdes Spiel
                Report.AppendLine("  OK    Prozess gestartet (PID " + _proc.Id + ")");
            }
            catch (Exception e)
            {
                Check(false, "Prozess startet nicht: " + e.Message);
                _proc = null;
            }
            _until = EditorApplication.timeSinceStartup + (SelbstEnde ? SelbstEndeSekunden : _lauf12Zusatz != null && (_step <= 2 || _step == 4) ? ProbeSekunden : RunSeconds);
            _schliessenUtc = _endeUtc = null;
        }

        /// <summary>
        /// 22.10.2026 (Nachbesserung Runde 4): Zeitpunkt (UTC), zu dem das WM_CLOSE dieses Laufs gepostet wurde, und Prozessende –
        /// das Speichern beim Beenden (OnApplicationQuit) muss dazwischen liegen (Lauf 4).
        /// </summary>
        private static DateTime? _schliessenUtc, _endeUtc;
        private const double SpeicherFensterVorher = 0.25, SpeicherFensterNachher = 1.0;

        private static DateTime EndeUtc(Process p)
        {
            try { return p.ExitTime.ToUniversalTime(); }
            catch (Exception) { return DateTime.UtcNow; }
        }

        /// <summary>Beendet sich das Spiel in diesem Lauf selbst (Menü-Probe mit „Speichern und beenden“)?</summary>
        private static bool SelbstEnde => _step == 3 && _lauf3Zusatz != null;

        private static string Zusatz12 => _lauf12Zusatz != null ? " " + _lauf12Zusatz : "";

        private static void Next()
        {
            _step++;
            switch (_step)
            {
                case 1: Start("Lauf 1: wie Neues Profil.bat", "-ashveil-host " + NetworkSetupBuilder.ZeitVormittag + Zusatz12); break;
                case 2: Start($"Lauf 2: Charakter anlegen (Profil {_profil})", $"-ashveil-host -ashveil-profile {_profil} -ashveil-autocreate " + NetworkSetupBuilder.ZeitVormittag + Zusatz12); break;
                case 3:
                    Start(_lauf3Zusatz == null ? "Lauf 3: erneuter Start, Spielstand laden" : "Lauf 3: erneuter Start, Spielstand laden, Menü-Probe, Speichern und beenden",
                        $"-ashveil-host -ashveil-profile {_profil} " + NetworkSetupBuilder.ZeitVormittag + (_lauf3Zusatz != null ? " " + _lauf3Zusatz : ""));
                    break;
                // 22.10.2026: nach „Speichern und beenden“ erneut laden – Stelle = Speicherstand aus Lauf 3?
                // 22.10.2026 (Nachbesserung Runde 4): mit der Kurztest-Probe – die Figur geht ein paar Schritte, damit die Prüfung
                // „Schliessen über das Fenster speichert beim Beenden“ auch die STELLE prüfen kann (nicht nur die Ladestelle wiederfindet).
                case 4 when _lauf4:
                    Start("Lauf 4: nach „Speichern und beenden“ erneut laden, ein paar Schritte, über das Fenster schliessen",
                        $"-ashveil-host -ashveil-profile {_profil} " + NetworkSetupBuilder.ZeitVormittag + Zusatz12);
                    break;
                default: Finish(); break;
            }
        }

        private static void Tick()
        {
            if (_step > LetzterLauf) return;
            bool exited = _proc == null || _proc.HasExited;
            if (!exited && EditorApplication.timeSinceStartup < _until) return;
            if (!exited)
            {
                if (SelbstEnde) Check(false, $"Spiel hat sich nicht selbst beendet (Menü-Probe, {SelbstEndeSekunden:0} s)");
                // 22.10.2026 (REL-01): ECHTES WM_CLOSE posten. Process.CloseMainWindow ist im Mono des Editors TerminateProcess(…, -2) –
                // daher kamen alle bisherigen „über das Fenster geschlossenen“ Läufe mit Exit -2 ohne Abbau (hart beendet, kein Speichern).
                _schliessenUtc = DateTime.UtcNow;   // 22.10.2026 (Nachbesserung): Speichern beim Beenden muss danach liegen
                bool gesendet = WmCloseSenden(_proc);
                if (!_proc.WaitForExit(15000))
                {
                    Check(false, "Fenster schliessen: Prozess reagiert nicht – hart beendet" + (gesendet ? "" : " (WM_CLOSE nicht gesendet)"));
                    try { _proc.Kill(); } catch (Exception) { }
                }
                else
                {
                    _endeUtc = EndeUtc(_proc);
                    Check(gesendet && _proc.ExitCode == 0, $"Spiel ueber das Fenster geschlossen: echtes WM_CLOSE {(gesendet ? "gesendet" : "NICHT gesendet")}, Exit-Code {_proc.ExitCode}");
                }
            }
            else if (_proc != null && SelbstEnde) Check(_proc.ExitCode == 0, "Spiel hat sich über „Speichern und beenden“ selbst beendet (Exit-Code " + _proc.ExitCode + ")");
            else if (_proc != null) Check(false, "Prozess hat sich vorzeitig beendet (Exit-Code " + _proc.ExitCode + ")");
            Evaluate();
            Next();
        }

        private static void Evaluate()
        {
            string text = LogLesen(_log);
            void Has(string needle, string label) => Check(text.Contains(needle), label + "  ⟨" + needle + "⟩");
            Report.AppendLine("  ·     Log: " + (text.Length > 0 ? $"{text.Length / 1024} KB" : "fehlt"));
            Has("Netzwerk: Spieler 1 betritt die Welt", "Welt geladen, Host laeuft, Spieler verbunden");
            // Schaerfe-Korrektur 02.10.: Ohne -screen-* Argumente startet der Build in voller Desktop-Aufloesung
            Has("(Desktop ", "Anzeige-Modus gesetzt");
            Has(", nativ)", "Anzeige in nativer Desktop-Aufloesung (keine hochskalierte 960x540)");
            if (_step == 1) Has("hat noch keinen Charakter – Charakter-Editor wird geöffnet", "neues Profil → Charakter-Editor wird geoeffnet");
            if (_step == 2)
            {
                Has($"Charakter '{_profil}' neu angelegt", "Charakter angelegt");
                string save = Path.Combine(_folder, "Saves", "Characters", _profil + ".json");
                Check(File.Exists(save), "Speicherstand geschrieben: " + save);
                if (File.Exists(save))
                {
                    string json = File.ReadAllText(save);
                    Match v = Regex.Match(json, "\"version\":\\s*(\\d+)");
                    Report.AppendLine("  ·     Speicherstand: " + (v.Success ? "v" + v.Groups[1].Value : "Version unklar") +
                                      (json.Contains("\"level\": 1") ? ", Stufe 1" : "") + $", {json.Length} Zeichen");
                }
            }
            if (_step == 3) Has($"Charakter '{_profil}' geladen", "Spielstand beim zweiten Start geladen");
            if (_step == 4) Has($"Charakter '{_profil}' geladen", "Spielstand nach „Speichern und beenden“ geladen");
            if (_lauf4) StellePruefen(text);
            if (SelbstEnde)
            {
                // UI-204: Esc-Regel mit echten Fenstern und „Speichern und beenden“ im fertigen Spiel
                Has("[Ashveil] Menü-Probe: bestanden", "Esc-Regel: Rucksack, dann Fähigkeiten, dann Spielmenü, dann zu");
                Has($"Speichern und beenden: '{_profil}' gespeichert", "Server speichert und bestätigt");
                Has("Speichern und beenden: Das Spiel wird beendet.", "Spiel endet erst nach der Bestätigung");
            }
            try { _zusatz?.Invoke(text, _step); }
            catch (Exception e) { Check(false, "Zusatzprüfung: " + e.GetType().Name + ": " + e.Message); }
            int exceptions = 0;
            foreach (string line in text.Split('\n'))
            {
                if (line.Contains("Exception")) exceptions++;
            }
            Report.AppendLine((exceptions == 0 ? "  OK    " : "  HINWEIS ") + $"Ausnahmen im Log: {exceptions}");
        }

        /// <summary>
        /// 22.10.2026 (Releaseablauf): Speichern und Laden an derselben Stelle.
        ///   Lauf 2: Stelle im Speicherstand nach dem Schliessen (= dort, wohin die Kurztest-Probe gegangen ist?)
        ///   Lauf 3: Ladestelle („betritt … (x/z)“) = Speicherstand aus Lauf 2; danach Stelle im Speicherstand nach „Speichern und beenden“
        ///   Lauf 4: Ladestelle = Speicherstand aus Lauf 3
        /// Nur lesend (Smoke-Profil im Build-Ordner).
        /// </summary>
        private static void StellePruefen(string text)
        {
            string save = Path.Combine(_folder, "Saves", "Characters", _profil + ".json");
            float[] geladen = Ladestelle(text);
            if (_step == 2)
            {
                _stelleLauf2 = Speicherstelle(save);
                Report.AppendLine("  ·     Speicherstand nach Lauf 2: Stelle " + Stelle(_stelleLauf2) + ", Zelle " + (Speicherzelle(save) ?? "?"));   // 22.10.: Zelle („szene:…“ = nicht wiederherstellbar)
                Match g = Regex.Match(text, @"Kurztest-Probe: gegangen von \((-?\d+)/(-?\d+)\) nach \((-?\d+)/(-?\d+)\)");
                if (g.Success && _stelleLauf2 != null)
                {
                    var von = new[] { Zahl(g.Groups[1].Value), Zahl(g.Groups[2].Value) };
                    var nach = new[] { Zahl(g.Groups[3].Value), Zahl(g.Groups[4].Value) };
                    string grund = Nah(von, _stelleLauf2) && !Nah(von, nach) ? " – Speicherstand steht noch vor den Schritten" : "";
                    Check(Nah(nach, _stelleLauf2), $"Schliessen über das Fenster speichert die Stelle nach den Schritten der Probe ({Stelle(nach)} ↔ Speicherstand {Stelle(_stelleLauf2)}{grund})");
                }
            }
            else if (_step == 3)
            {
                Check(geladen != null && _stelleLauf2 != null && Nah(geladen, _stelleLauf2),
                    $"Lauf 3 betritt die Welt an der gespeicherten Stelle (geladen {Stelle(geladen)} ↔ Speicherstand Lauf 2 {Stelle(_stelleLauf2)})");
                _stelleLauf3 = Speicherstelle(save);
                _zeitLauf3 = Speicherzeit(save);
                _zelleLauf3 = Speicherzelle(save);   // 22.10.2026 (Nachbesserung)
                Report.AppendLine("  ·     Speicherstand nach „Speichern und beenden“: Stelle " + Stelle(_stelleLauf3) + ", Zelle " + (_zelleLauf3 ?? "?") + ", gespeichert " + (_zeitLauf3 ?? "?"));
            }
            else if (_step == 4)
            {
                Check(geladen != null && _stelleLauf3 != null && Nah(geladen, _stelleLauf3),
                    $"nach „Speichern und beenden“ an derselben Stelle geladen (geladen {Stelle(geladen)} ↔ Speicherstand Lauf 3 {Stelle(_stelleLauf3)})");
                // 22.10.2026: Das Schließen über das Fenster (WM_CLOSE wie X/Alt+F4) muss beim Beenden speichern (OnApplicationQuit) –
                // jeder Speichervorgang setzt savedAtUtc neu, auch ohne Änderung.
                // 22.10.2026 (Nachbesserung Runde 4, Abnahme): geschärft – vorher reichte „anders als nach Lauf 3“, das machte jede
                // Zwischenspeicherung während des Laufs grün. Jetzt: (1) savedAtUtc liegt zwischen dem Posten des WM_CLOSE und dem
                // Prozessende (−0,25 s/+1 s Uhr-Spielraum) und (2) der Speicherstand trägt die Stelle, an der die Figur beim Schließen
                // stand – nach den Schritten der Probe in Lauf 4, in derselben Zelle wie nach Lauf 3 (nicht „szene:…“).
                string zeit4 = Speicherzeit(save);
                DateTime? gespeichert = Utc(zeit4);
                bool zeitOk = gespeichert.HasValue && _schliessenUtc.HasValue && _endeUtc.HasValue &&
                              gespeichert.Value >= _schliessenUtc.Value.AddSeconds(-SpeicherFensterVorher) &&
                              gespeichert.Value <= _endeUtc.Value.AddSeconds(SpeicherFensterNachher);
                string zeitText = !gespeichert.HasValue ? $"Speicherstand {zeit4 ?? "fehlt"}"
                    : !_schliessenUtc.HasValue || !_endeUtc.HasValue ? $"gespeichert {gespeichert.Value:HH:mm:ss.fff} UTC, Schliessen/Ende nicht gemessen (hart beendet?)"
                    : $"gespeichert {gespeichert.Value:HH:mm:ss.fff} UTC = {(gespeichert.Value - _schliessenUtc.Value).TotalSeconds:+0.00;-0.00} s nach dem WM_CLOSE, " +
                      $"{(_endeUtc.Value - gespeichert.Value).TotalSeconds:0.00} s vor dem Prozessende ({_endeUtc.Value:HH:mm:ss.fff})";
                float[] stelle4 = Speicherstelle(save);
                string zelle4 = Speicherzelle(save);
                Match g4 = Regex.Match(text, @"Kurztest-Probe: gegangen von \((-?\d+)/(-?\d+)\) nach \((-?\d+)/(-?\d+)\)");
                float[] von4 = g4.Success ? new[] { Zahl(g4.Groups[1].Value), Zahl(g4.Groups[2].Value) } : null;
                float[] soll = g4.Success ? new[] { Zahl(g4.Groups[3].Value), Zahl(g4.Groups[4].Value) } : geladen;
                bool gegangen = von4 != null && !Nah(von4, soll);
                bool stelleOk = Nah(stelle4, soll) && zelle4 != null && zelle4 == _zelleLauf3 && !zelle4.StartsWith("szene:", StringComparison.Ordinal);
                Check(zeitOk && stelleOk,
                    $"Schliessen über das Fenster speichert beim Beenden: {zeitText}; Stelle {Stelle(stelle4)} in Zelle {zelle4 ?? "?"} ↔ beim Schliessen " +
                    $"{Stelle(soll)} ({(g4.Success ? $"nach den Schritten der Probe von {Stelle(von4)}" : "Ladestelle, keine Schritte der Probe")}), Zelle nach Lauf 3 {_zelleLauf3 ?? "?"}");
                if (!gegangen)
                    Hinweis("Lauf 4: die Figur ist nicht gegangen – die Stellenprüfung beim Beenden unterscheidet dann nicht zwischen „beim Beenden gespeichert“ und „Stand aus Lauf 3“ (die Zeitprüfung gilt weiter)");
            }
        }

        /// <summary>22.10.2026 (Nachbesserung): Zelle des Speicherstands nach Lauf 3 (Vergleich mit dem Speichern beim Beenden in Lauf 4).</summary>
        private static string _zelleLauf3;

        /// <summary>22.10.2026 (Nachbesserung): savedAtUtc („o“-Format) als UTC-Zeit, sonst null.</summary>
        private static DateTime? Utc(string zeit)
        {
            if (string.IsNullOrEmpty(zeit)) return null;
            return DateTime.TryParse(zeit, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime t)
                ? t.ToUniversalTime() : (DateTime?)null;
        }

        private static string _zeitLauf3;

        /// <summary>22.10.2026: "cellId" der gespeicherten Stelle, sonst null.</summary>
        private static string Speicherzelle(string save)
        {
            if (!File.Exists(save)) return null;
            Match m = Regex.Match(File.ReadAllText(save), @"""cellId""\s*:\s*""([^""]*)""");
            return m.Success ? m.Groups[1].Value : null;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd, uint nachricht, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// 22.10.2026 (REL-01): WM_CLOSE an das Hauptfenster posten – wie X bzw. Alt+F4 beim Spieler. Nicht Process.CloseMainWindow:
        /// das ist im Mono des Unity-Editors ein hartes TerminateProcess mit Exit-Code -2 (Beleg: Mehrspieler4-Teil 5, 22.10.).
        /// </summary>
        public static bool WmCloseSenden(Process p)
        {
            try
            {
                p.Refresh();
                IntPtr h = p.HasExited ? IntPtr.Zero : p.MainWindowHandle;
                return h != IntPtr.Zero && PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception) { return false; }
        }

        /// <summary>22.10.2026: "savedAtUtc" des Speicherstands, sonst null.</summary>
        private static string Speicherzeit(string save)
        {
            if (!File.Exists(save)) return null;
            Match m = Regex.Match(File.ReadAllText(save), @"""savedAtUtc""\s*:\s*""([^""]*)""");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Letzte Zeile „'profil' betritt … (x/z).“ des Servers (Szenen- und Weltzellen-Zweig), sonst null.</summary>
        private static float[] Ladestelle(string text)
        {
            MatchCollection m = Regex.Matches(text, "'" + Regex.Escape(_profil) + @"' betritt [^\n]*?\((-?\d+)/(-?\d+)\)\.");
            if (m.Count == 0) return null;
            Match l = m[m.Count - 1];
            return new[] { Zahl(l.Groups[1].Value), Zahl(l.Groups[2].Value) };
        }

        /// <summary>x/z aus "position" → "localPosition" des Speicherstands, sonst null.</summary>
        private static float[] Speicherstelle(string save)
        {
            if (!File.Exists(save)) return null;
            Match m = Regex.Match(File.ReadAllText(save),
                @"""localPosition""\s*:\s*\{\s*""x""\s*:\s*([-0-9.Ee+]+)\s*,\s*""y""\s*:\s*[-0-9.Ee+]+\s*,\s*""z""\s*:\s*([-0-9.Ee+]+)");
            return m.Success ? new[] { Zahl(m.Groups[1].Value), Zahl(m.Groups[2].Value) } : null;
        }

        private static float Zahl(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        private static bool Nah(float[] a, float[] b) =>
            a != null && b != null && Math.Abs(a[0] - b[0]) <= StellenToleranz && Math.Abs(a[1] - b[1]) <= StellenToleranz;

        private static string Stelle(float[] p) =>
            p == null ? "keine" : string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:0.0}/{1:0.0})", p[0], p[1]);

        /// <summary>
        /// Log lesen. Beendet sich das Spiel selbst (Lauf 3 mit Menü-Probe), hält Windows die Datei noch einen Moment fest
        /// („Sharing violation“) – dann kurz warten, höchstens 5 s.
        /// </summary>
        private static string LogLesen(string path)
        {
            for (int versuch = 0; ; versuch++)
            {
                try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
                catch (IOException) when (versuch < 25) { System.Threading.Thread.Sleep(200); }
            }
        }

        // ------------------------------------------------------------------ 22.10.2026 (Runde 5, RELEASE-02): Sicherung des Profilzeigers

        /// <summary>Endung der Sicherung von Max' Profilzeiger während eines Kurztests (ashveil_profile.txt.vor_smoke).</summary>
        private const string Sicherung = ".vor_smoke";
        private const string ZeigerBericht = "Ashveil_Kurztest_Profilzeiger.txt";
        private static double _naechstePruefung;
        private static readonly System.Collections.Generic.HashSet<string> _blockiertGemeldet = new System.Collections.Generic.HashSet<string>();

        static PlaytestSmoke()
        {
            // Nach jedem Neuladen des Editors läuft kein Kurztest mehr (sein Zustand ist mit dem Domain-Reload weg) – eine
            // liegengebliebene Sicherung gehört dann zurück. Erst nach dem Laden (delayCall), nicht im statischen Konstruktor.
            EditorApplication.delayCall += VerwaisteSicherungenPruefen;
        }

        /// <summary>
        /// Alle Build-Ordner (Builds/*) mit liegengebliebener Sicherung prüfen (ohne laufenden Kurztest). Läuft noch ein Spiel aus
        /// dem Ordner, bleibt die Sicherung liegen und wird alle 15 s erneut geprüft – ein laufendes Spiel könnte den Zeiger noch lesen.
        /// </summary>
        private static void VerwaisteSicherungenPruefen()
        {
            EditorApplication.update -= SpaeterPruefen;
            if (_laeuft) return;
            bool offen = false;
            try
            {
                string builds = Ashveil.Core.ProjectPaths.Get(Ashveil.Core.ProjectPaths.BuildsDir);
                if (!Directory.Exists(builds)) return;
                foreach (string dir in Directory.GetDirectories(builds))
                {
                    if (!File.Exists(Path.Combine(dir, "ashveil_profile.txt" + Sicherung))) continue;
                    VerwaisteSicherungZurueck(dir, out bool blockiert);
                    offen |= blockiert;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Ashveil] Kurztest-Profilzeiger: Prüfung fehlgeschlagen – " + e.Message); }
            if (!offen) return;
            _naechstePruefung = EditorApplication.timeSinceStartup + 15.0;
            EditorApplication.update += SpaeterPruefen;
        }

        private static void SpaeterPruefen()
        {
            if (EditorApplication.timeSinceStartup >= _naechstePruefung) VerwaisteSicherungenPruefen();
        }

        /// <summary>
        /// Liegt im Build-Ordner eine Sicherung aus einem abgebrochenen Lauf, wird sie ZUERST zurückgelegt – nie überschrieben, nie
        /// verworfen:
        ///   · Zeiger fehlt oder zeigt auf ein Profil ohne Spielstand (der unbenutzte Zufallszeiger aus Lauf 1) → Sicherung zurück.
        ///   · Zeiger gleich der Sicherung → nur die doppelte Sicherung entfernen.
        ///   · Zeiger zeigt auf einen anderen vorhandenen Spielstand (z. B. hat Max inzwischen eine Figur angelegt) → beide behalten:
        ///     Sicherung mit Zeitstempel umbenennen und warnen – welche Figur gilt, entscheidet Max.
        ///   · Läuft ein Spiel aus diesem Ordner → nichts anfassen (<paramref name="blockiert"/>).
        /// Rückgabe: was geschah (Bericht/Protokoll), null = keine Sicherung vorhanden. Nur Zeigerdateien, nie Spielstände.
        /// </summary>
        public static string VerwaisteSicherungZurueck(string folder, out bool blockiert, bool protokollieren = true)
        {
            blockiert = false;
            string pointer = Path.Combine(folder, "ashveil_profile.txt");
            string bak = pointer + Sicherung;
            if (!File.Exists(bak)) return null;
            string text;
            try
            {
                // 22.10.2026 (Runde 6, Codex-Paket A): dreiwertig – auch „nicht feststellbar“ blockiert (vorher galt ein unlesbarer
                // Programmpfad eines noch laufenden Spiels als „kein Spiel“)
                string sperre = SpielSperre(folder);
                if (sperre != null)
                {
                    blockiert = true;
                    text = $"Sicherung {Path.GetFileName(bak)} aus einem abgebrochenen Lauf liegt noch, aber {sperre} – " +
                           "nichts angefasst; erneute Prüfung, sobald es beendet ist";
                    if (!_blockiertGemeldet.Add(folder)) return text;   // nur einmal protokollieren
                }
                else
                {
                    _blockiertGemeldet.Remove(folder);
                    string gesichert = File.ReadAllText(bak).Trim();
                    string aktuell = File.Exists(pointer) ? File.ReadAllText(pointer).Trim() : null;
                    bool aktuellBenutzt = !string.IsNullOrEmpty(aktuell) && File.Exists(Path.Combine(folder, "Saves", "Characters", aktuell + ".json"));
                    if (aktuell == gesichert)
                    {
                        File.Delete(bak);
                        text = $"Zeiger „{gesichert}“ stand schon wieder an seinem Platz – doppelte Sicherung entfernt";
                    }
                    else if (!aktuellBenutzt)
                    {
                        if (File.Exists(pointer)) File.Delete(pointer);
                        File.Move(bak, pointer);
                        text = $"Sicherung zurückgelegt: Zeiger wieder „{gesichert}“ (vorher {(aktuell == null ? "kein Zeiger" : $"„{aktuell}“ ohne Spielstand")})";
                    }
                    else
                    {
                        string neu = bak + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        File.Move(bak, neu);
                        text = $"BEIDE behalten: aktueller Zeiger „{aktuell}“ hat einen Spielstand, die Sicherung „{gesichert}“ heißt jetzt " +
                               $"{Path.GetFileName(neu)} – welche Figur gilt, bitte von Hand entscheiden";
                    }
                }
            }
            catch (Exception e)
            {
                blockiert = true;
                text = $"Sicherung {Path.GetFileName(bak)} nicht zurückgelegt: {e.GetType().Name}: {e.Message}";
            }
            if (protokollieren) ZeigerProtokoll(folder, text);
            return text;
        }

        // ------------------------------------------------------------------ 22.10.2026 (Runde 6, Codex-Paket A): Prozessprüfung

        /// <summary>22.10.2026 (Runde 6, Codex-Paket A): Läuft ein Spiel aus genau diesem Build-Ordner?</summary>
        public enum SpielZustand
        {
            /// <summary>Alle Ashveil.exe gelesen, keiner aus diesem Ordner (oder sicher beendet).</summary>
            SicherKeiner,
            /// <summary>Ein Ashveil.exe aus genau diesem Ordner läuft.</summary>
            Laeuft,
            /// <summary>Ein noch laufendes Ashveil.exe ließ sich nicht zuordnen (Pfad unlesbar) oder die Prozessliste fehlt.</summary>
            NichtFeststellbar
        }

        /// <summary>22.10.2026 (Runde 6): ein laufendes Ashveil.exe, wie die Prüfung es sieht (für EditMode-Tests ersetzbar).</summary>
        public readonly struct SpielProzess
        {
            public readonly int Pid;
            /// <summary>Voller Programmpfad; null = nicht lesbar.</summary>
            public readonly string Pfad;
            /// <summary>true = sicher beendet (HasExited); ein beendeter Prozess blockiert nie.</summary>
            public readonly bool Beendet;
            /// <summary>Warum der Pfad nicht lesbar war (Protokoll).</summary>
            public readonly string Fehler;

            public SpielProzess(int pid, string pfad, bool beendet = false, string fehler = null)
            {
                Pid = pid;
                Pfad = pfad;
                Beendet = beendet;
                Fehler = fehler;
            }
        }

        /// <summary>Nur EditMode-Tests: ersetzt die Liste der laufenden Ashveil.exe (null = echte Prozessliste). Nach dem Test zurücksetzen.</summary>
        public static Func<IEnumerable<SpielProzess>> ProzesseFuerTests;

        /// <summary>
        /// 22.10.2026 (Runde 6, Codex-Paket A): reine Auswertung. <paramref name="ausgenommen"/> = eigene Kurztest-Läufe (PIDs).
        /// Läuft ein Prozess aus genau diesem Ordner → <see cref="SpielZustand.Laeuft"/>; sonst, wenn ein noch laufender Prozess
        /// nicht zuzuordnen war → <see cref="SpielZustand.NichtFeststellbar"/>; sonst <see cref="SpielZustand.SicherKeiner"/>.
        /// Ein Spiel aus einem anderen (eindeutig gelesenen) Ordner zählt nicht – auch nicht bei gleichem Namensanfang.
        /// </summary>
        public static SpielZustand SpielZustandBestimmen(string folder, IEnumerable<SpielProzess> prozesse, ICollection<int> ausgenommen,
            out int pid, out string grund)
        {
            pid = 0;
            grund = null;
            string exe;
            try { exe = Path.GetFullPath(Path.Combine(folder, "Ashveil.exe")); }
            catch (Exception e)
            {
                grund = "Build-Ordner nicht lesbar: " + e.Message;
                return SpielZustand.NichtFeststellbar;
            }
            if (prozesse == null)
            {
                grund = "Prozessliste nicht lesbar";
                return SpielZustand.NichtFeststellbar;
            }
            bool unklar = false;
            foreach (SpielProzess p in prozesse)
            {
                if (p.Beendet || (ausgenommen != null && ausgenommen.Contains(p.Pid))) continue;
                string voll = null;
                if (p.Pfad != null)
                {
                    try { voll = Path.GetFullPath(p.Pfad); }
                    catch (Exception) { voll = null; }
                }
                if (voll == null)
                {
                    if (!unklar)
                    {
                        unklar = true;
                        pid = p.Pid;
                        grund = p.Fehler ?? "Programmpfad nicht lesbar";
                    }
                    continue;
                }
                if (string.Equals(voll, exe, StringComparison.OrdinalIgnoreCase))
                {
                    pid = p.Pid;
                    grund = null;
                    return SpielZustand.Laeuft;
                }
            }
            return unklar ? SpielZustand.NichtFeststellbar : SpielZustand.SicherKeiner;
        }

        /// <summary>22.10.2026 (Runde 6): Prüfung mit der echten Prozessliste (bzw. <see cref="ProzesseFuerTests"/>).</summary>
        public static SpielZustand SpielPruefen(string folder, ICollection<int> ausgenommen, out int pid, out string grund)
        {
            List<SpielProzess> prozesse;
            try { prozesse = new List<SpielProzess>(ProzesseFuerTests != null ? ProzesseFuerTests() : AshveilProzesse()); }
            catch (Exception e)
            {
                pid = 0;
                grund = "Prozessliste nicht lesbar: " + e.GetType().Name + ": " + e.Message;
                return SpielZustand.NichtFeststellbar;
            }
            return SpielZustandBestimmen(folder, prozesse, ausgenommen, out pid, out grund);
        }

        /// <summary>22.10.2026 (Runde 6): null = sicher kein Spiel aus diesem Ordner; sonst der Grund für die Sperre (Bericht/Protokoll).</summary>
        private static string SpielSperre(string folder, ICollection<int> ausgenommen = null)
        {
            switch (SpielPruefen(folder, ausgenommen, out int pid, out string grund))
            {
                case SpielZustand.Laeuft:
                    return $"ein Spiel aus diesem Ordner läuft (PID {pid})";
                case SpielZustand.NichtFeststellbar:
                    return "nicht feststellbar, ob ein Spiel aus diesem Ordner läuft (" + (pid != 0 ? $"PID {pid}: " : "") + grund + ")";
                default:
                    return null;
            }
        }

        /// <summary>
        /// 22.10.2026 (Runde 6): alle laufenden Ashveil.exe mit Programmpfad. Erst über MainModule, sonst über
        /// QueryFullProcessImageName (geht auch bei Prozessen, deren Module der Editor nicht lesen darf); bleibt der Pfad unlesbar,
        /// zählt ein sicher beendeter Prozess als beendet, alles andere als „nicht feststellbar“.
        /// </summary>
        private static List<SpielProzess> AshveilProzesse()
        {
            var liste = new List<SpielProzess>();
            foreach (Process p in Process.GetProcessesByName("Ashveil"))
            {
                try
                {
                    string pfad = null, fehler = null;
                    try { pfad = p.MainModule?.FileName; }
                    catch (Exception e) { fehler = e.GetType().Name + ": " + e.Message; }
                    if (string.IsNullOrEmpty(pfad)) pfad = PfadUeberKernel(p.Id);
                    bool beendet = false;
                    if (string.IsNullOrEmpty(pfad))
                    {
                        pfad = null;
                        try { beendet = p.HasExited; }
                        catch (Exception e) { fehler = (fehler != null ? fehler + "; " : "") + "Laufzustand nicht lesbar (" + e.GetType().Name + ")"; }
                    }
                    liste.Add(new SpielProzess(p.Id, pfad, beendet, fehler ?? (pfad == null ? "Programmpfad nicht lesbar" : null)));
                }
                finally { p.Dispose(); }
            }
            return liste;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint zugriff, bool erben, int pid);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr prozess, int flags, StringBuilder name, ref int laenge);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>22.10.2026 (Runde 6): Programmpfad über PROCESS_QUERY_LIMITED_INFORMATION, sonst null.</summary>
        private static string PfadUeberKernel(int pid)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(0x1000, false, pid);
                if (h == IntPtr.Zero) return null;
                var name = new StringBuilder(1024);
                int laenge = name.Capacity;
                return QueryFullProcessImageName(h, 0, name, ref laenge) && laenge > 0 ? name.ToString(0, laenge) : null;
            }
            catch (Exception) { return null; }
            finally
            {
                if (h != IntPtr.Zero)
                {
                    try { CloseHandle(h); }
                    catch (Exception) { /* nur Aufräumen */ }
                }
            }
        }

        /// <summary>
        /// 22.10.2026 (Runde 6, Codex-Paket A): alles vor dem ersten Lauf, was den Profilzeiger betrifft, in dieser Reihenfolge:
        ///   1. Prozessprüfung – unabhängig davon, ob eine Sicherung liegt. Läuft ein Spiel aus genau diesem Ordner oder ist das nicht
        ///      feststellbar → nichts anfassen, ablehnen, protokollieren. Das Spiel wird NICHT beendet (es kann Max' eigenes sein).
        ///   2. Liegengebliebene Sicherung zuerst zurücklegen (<see cref="VerwaisteSicherungZurueck"/>).
        ///   3. Zeiger sichern (.vor_smoke, nie überschreiben).
        /// Rückgabe null = bereit (<paramref name="wiederher"/>: was Schritt 2 tat, sonst null), sonst der Grund der Ablehnung.
        /// Ändert nur Zeigerdateien, nie Spielstände; löscht den Zeiger nicht (das macht <see cref="Starten"/> direkt danach).
        /// </summary>
        public static string ZeigerVorbereiten(string folder, out string wiederher, bool protokollieren = true)
        {
            wiederher = null;
            string sperre = SpielSperre(folder);
            if (sperre != null)
            {
                string text = sperre + " – Profilzeiger und Sicherung nicht angefasst, das Spiel wird nicht beendet; bitte erst schließen";
                if (protokollieren) ZeigerProtokoll(folder, "Kurztest nicht gestartet – " + text);
                return text;
            }
            wiederher = VerwaisteSicherungZurueck(folder, out bool blockiert, protokollieren);
            if (blockiert) return wiederher;
            string pointer = Path.Combine(folder, "ashveil_profile.txt");
            try
            {
                if (File.Exists(pointer)) File.Copy(pointer, pointer + Sicherung, false);   // false: nie überschreiben
            }
            catch (Exception e)
            {
                string text = "Sicherung des Profilzeigers nicht möglich (" + e.Message + ")";
                if (protokollieren) ZeigerProtokoll(folder, "Kurztest nicht gestartet – " + text);
                return text;
            }
            return null;
        }

        /// <summary>22.10.2026 (Runde 6): PIDs der in diesem Kurztest gestarteten Läufe (am Ende von der Prozessprüfung ausgenommen).</summary>
        private static readonly HashSet<int> _eigenePids = new HashSet<int>();

        /// <summary>Protokoll: Editor-Log und Logs/Ashveil/Ashveil_Kurztest_Profilzeiger.txt (angehängt, mit Zeit und Ordner).</summary>
        private static void ZeigerProtokoll(string folder, string text)
        {
            Debug.LogWarning($"[Ashveil] Kurztest-Profilzeiger ({folder}): {text}");
            try
            {
                string path = Ashveil.Core.ProjectPaths.Logs(ZeigerBericht);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} · {folder} · {text}{Environment.NewLine}");
            }
            catch (Exception) { /* Protokoll ist Beiwerk */ }
        }

        private static void Finish()
        {
            EditorApplication.update -= Tick;
            string pointer = Path.Combine(_folder, "ashveil_profile.txt");
            // 22.10.2026 (Runde 6, Codex-Paket A): auch das Zurücklegen am Ende ändert den Zeiger – läuft inzwischen ein fremdes Spiel
            // aus dem Ordner (eigene Läufe ausgenommen) oder ist das nicht feststellbar, nichts anfassen; eine liegende Sicherung legt
            // die Prüfung nach dem Neuladen bzw. alle 15 s (VerwaisteSicherungenPruefen) zurück, sobald kein Spiel mehr läuft.
            string sperre = SpielSperre(_folder, _eigenePids);
            if (sperre == null)
            {
                try
                {
                    // Lauf 1 hat einen neuen Profilzeiger angelegt – entfernen, damit Max wirklich frisch beginnt
                    if (File.Exists(pointer)) File.Delete(pointer);
                    if (File.Exists(pointer + Sicherung)) File.Move(pointer + Sicherung, pointer);
                }
                catch (Exception e) { sperre = "Zurücklegen fehlgeschlagen: " + e.GetType().Name + ": " + e.Message; }
            }
            if (sperre != null)
            {
                bool bak = File.Exists(pointer + Sicherung);
                Hinweis("Profilzeiger nicht zurückgelegt: " + sperre + (bak
                    ? " – die Sicherung bleibt liegen und wird zurückgelegt, sobald kein Spiel aus dem Ordner mehr läuft"
                    : " – vorher gab es keinen Zeiger; der Zeiger aus Lauf 1 bleibt stehen"));
                ZeigerProtokoll(_folder, "Kurztest-Ende: Profilzeiger nicht zurückgelegt – " + sperre);
                if (bak) EditorApplication.delayCall += VerwaisteSicherungenPruefen;
            }
            string path = Ashveil.Core.ProjectPaths.Logs(_bericht);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Report.AppendLine("");
            Report.AppendLine(_fehler == 0 ? "ERGEBNIS: bestanden" : $"ERGEBNIS: {_fehler} Prüfung(en) fehlgeschlagen");
            File.WriteAllText(path, Report.ToString());
            Debug.Log($"[Ashveil] {_titel}-Smoke-Test fertig → " + path);
            _laeuft = false;
            Action<bool, string> fertig = _fertig;
            _fertig = null;
            fertig?.Invoke(_fehler == 0, (_fehler == 0 ? "bestanden" : $"{_fehler} Prüfung(en) fehlgeschlagen") + $" ({LetzterLauf} Starts) · Bericht {path}");
        }
    }
}
