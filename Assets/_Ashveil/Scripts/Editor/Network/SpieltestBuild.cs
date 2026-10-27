using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// #30 Spieltest r2 (Welle 0, 08.10.2026): Build von Kareth v4 für Max.
    /// - Nur die Szene Kareth_v4 mit ihren Spielsystemen (#26), Development-Build (wie der P4-Build: Entwickler-Konsole,
    ///   „/zeit“ für den Host).
    /// - Nur Menschen wählbar (Max 06.10.2026, Klick „Nur Mensch“): Kennzeichen ASHVEIL_NUR_MENSCH nur für diesen Build
    ///   (RaceCatalog.Playable; der Server prüft es in AppearanceCatalog.Sanitize).
    /// - Startdateien wie beim P4-Build: „Neues Profil.bat“ und „Spielen.bat“ starten als eigener Host; Weltuhr ab 09:00, danach
    ///   Echtzeit (Max 06.10.2026, Klick „Versatz auf Vormittag“, NET-020).
    /// - Spielstände liegen im Build-Ordner (Saves/), getrennt vom Projekt; ein Neubau lässt sie stehen.
    /// - Kurztest: drei echte Starts (<see cref="PlaytestSmoke"/>) plus „nur Mensch“, Startzeit 09:00 und Kartendaten (#100).
    ///   Seit r2.1 (UI-204, 09.10.2026) prüft Lauf 3 im fertigen Spiel die Esc-Regel und „Speichern und beenden“ (Menü-Probe);
    ///   jeder Lauf prüft außerdem, dass die Bild-Spitzen-Messung v2 mitläuft (CORE-005, Ruckler-Messung: <see cref="AuftragRuckler"/>).
    ///   Seit r2.2 (09.10.2026 abends, Max: Mausfilter fest einbauen) prüft jeder Lauf auch, dass der Mausfilter an ist
    ///   (Ashveil.Core.Platform.Mausfilter, CORE-005) und keinen Fehler meldet.
    ///   r2.3 (09.10.2026 nachts): Gras-Shader mit Forward+-Variante (schwarzes Gras im fertigen Spiel), dazu die Proben
    ///   „flacker_probe“ und „gras_probe“ (<see cref="AuftragFlacker"/>).
    ///   r2.4 (10.10.2026 nachts): Gras dunkler (M_Grass Farbe 1,0) und der Flacker-Rekorder (F11 sichert die letzten 120 Bilder,
    ///   Ashveil.Networking.FlackerRekorder), weil die Flacker-Probe v3 die von Max gesehenen Schattenaussetzer nicht nachstellt.
    ///   r2.5 (10.10.2026 mittags): GI-Flackerfix (REND-027) und Wiesen-Pass (Grasfläche, Grasschicht in zwei Maßstäben, Baumgruppen
    ///   um das Lager, kühler Morgendunst; CORE-009).
    ///   r2.8 (10.10.2026 spät): Idylle um Untersteg – Feldmark mit Korn und Flachs, Heuwiesen mit Schobern, Wassermühle am Muldenbach (Grete).
    ///   r2.9 (10.10.2026 nachts): Weltfülle nach Classic (≈ 860 Gegner statt 104, schlafende KI ohne Zuschauer) und 15 Nebenaufträge N01–N15.
    ///   r2.10 (10.10.2026 nachts): EP wie Classic – graue Gegner 0 EP, Gruppen-EP mit Bonus, Ausgeruht (Gasthaus „Zur Schale“), Dungeon-EP wie Forever.
    ///   r2.11 (11.10.2026): Weltfülle dichter (≈ 1 530 Gegner), Startmulde dicht wie Northshire (Max: „wo sind die Gegner?“).
    ///   r2.12 (11.10.2026): Classic-Dichte nach Emulator-Daten (≈ 3 650 Gegner, Lager), dynamische Wiederkehr bis 1/8, Gegner-Sicht 180 m.
    ///   r2.13 (11.10.2026): Aktivierungsraster wie MMO-Server (Gegner nur nahe bei Spielern als Figur), unsichtbare Gegner ohne Animation.
    ///   r2.14 (11.10.2026): Wecken der Gebiete verteilt auf mehrere Bilder (höchstens 6 Gegner je Bild).
    ///   r2.15 (11.10.2026): Wecken mit 3-ms-Zeitbudget je Bild, Tiere außerhalb des Bildes ohne Beinbewegung, Server ohne Grafik ohne Renderer und Gegner-Darstellung, Speichern höchstens alle 10 s.
    ///   r2.16 (11.10.2026): Gegner der offenen Welt senden ihre Lage jeden 2. Netztakt; Messprotokolle: nur die letzten 5 Sitzungen.
    ///   r2.7 (10.10.2026 abends): Auftragskreise auf Karte und Minimap (A/B/C), Einladung beim Ankommen, Lockrufe, Ilse vor dem Nordtor,
    ///   Untersteg mit vier neuen Figuren, Abmelde-Countdown außerhalb sicherer Orte (10 „Entscheidungen 10.10.2026 (abends)“).
    ///   r2.6 (10.10.2026 nachmittags): Optik Durchgang 2 – geschwungene Halme, kühleres Grün, Unterholz an Waldrändern,
    ///   Vegetation nahe der Kamera ausgeblendet (Dither), Sonne für Vegetation ohne distanceAttenuation (REND-028).
    /// Aufträge „spieltest_bauen“ und „spieltest_kurztest“; Menü Ashveil › Spieltest r2.
    /// </summary>
    public static class SpieltestBuild
    {
        public const string Folder = "Builds/Spieltest_r2";
        public const string NurMensch = "ASHVEIL_NUR_MENSCH";
        private const string Titel = "Spieltest r2.16 (Kareth v4, nur Mensch, Auftragskreise, Lager lebt, Untersteg 6, Felder und Muehle, Weltfuelle, 15 Nebenauftraege, EP wie Classic, Classic-Dichte, Aktivierungsraster)";   // nur ASCII (Startdateien)
        private const string Profil = "_smoke_r2";
        private const string Bericht = "Ashveil_SpieltestR2_Kurztest.txt";

        [InitializeOnLoadMethod]
        private static void Registrieren()
        {
            Auftragsordner.Registrieren("spieltest_bauen", (a, fertig) => fertig(Bauen(out string m), m), true, 1800);
            Auftragsordner.Registrieren("spieltest_kurztest", (a, fertig) => Kurztest(fertig), false, 600);
        }

        [MenuItem("Ashveil/Spieltest r2/Build erstellen (Kareth v4, nur Mensch)", priority = 1)]
        private static void BauenMenue()
        {
            if (Bauen(out string m)) Debug.Log("[Ashveil] " + m);
            else EditorUtility.DisplayDialog("Ashveil", m, "OK");
        }

        [MenuItem("Ashveil/Spieltest r2/Kurztest (3 kurze Starts)", priority = 2)]
        private static void KurztestMenue() => Kurztest((ok, m) => Debug.Log("[Ashveil] Spieltest r2 Kurztest: " + m));

        /// <summary>Build erstellen und Startdateien schreiben; false mit Meldung, wenn es nicht geht.</summary>
        public static bool Bauen(out string meldung)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { meldung = "Play läuft – während Play kann Unity nicht bauen"; return false; }
            if (!File.Exists(KarethV4Builder.ScenePath)) { meldung = "Kareth v4 ist noch nicht gebaut (Auftrag kareth_v4_bauen)"; return false; }
            if (EditorSceneManager.GetActiveScene().isDirty) { meldung = "ungespeicherte Änderungen in der offenen Szene – nichts gebaut"; return false; }

            // Szene wie im Spiel: kein Mess-Objekt, Spielsysteme an (beides speichert die Szene nur bei einer Änderung)
            WeltMaterialtest.KV4MessObjektEntfernen();
            int an = KarethV4Spiel.SetActiveForMeasurement(true);
            if (an > 0) Debug.Log($"[Ashveil] Spieltest r2: {an} Spielsystem-Objekte wieder eingeschaltet.");

            NetworkSetupBuilder.StopTestClients();   // ein laufendes Spiel aus diesem Ordner sperrt die Dateien
            NetworkSetupBuilder.ClearBurstOutput();
            string folder = Path.GetFullPath(Folder);
            Directory.CreateDirectory(folder);
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    // Westweiden (12.10.2026): die Weltzelle wird zur Laufzeit additiv geladen und muss im Build sein
                    scenes = KarethV4Weidepass.Aktiv ? new[] { KarethV4Builder.ScenePath, WestweidenZelle.ScenePath } : new[] { KarethV4Builder.ScenePath },
                    locationPathName = Path.Combine(folder, "Ashveil.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development,
                    extraScriptingDefines = new[] { NurMensch },
                });
            }
            finally
            {
                Ashveil.EditorTools.Rendering.DynamicGIBuildSupport.SetActiveForBuild(false);   // auch bei Fehlschlag wieder aus
            }
            if (report.summary.result != BuildResult.Succeeded)
            {
                meldung = $"Spieltest-Build fehlgeschlagen: {report.summary.result} ({report.summary.totalErrors} Fehler, Details im Protokoll)";
                Debug.LogError("[Ashveil] " + meldung);
                return false;
            }
            PlaytestBuildMenu.WriteStartFiles(folder, Titel);
            long mb = Ordnergroesse(folder) / 1048576;
            meldung = $"Spieltest-Build fertig ({report.summary.totalTime.TotalSeconds:0} s, {mb} MB): {folder} – Start „Neues Profil.bat“, danach „Spielen.bat“; nur Mensch, Weltuhr ab 09:00";
            Debug.Log("[Ashveil] " + meldung);
            return true;
        }

        /// <summary>Drei kurze Starts des Builds; fertig(ok, meldung) nach dem dritten Lauf.</summary>
        public static void Kurztest(Action<bool, string> fertig)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { fertig(false, "Play läuft – Kurztest nicht gestartet"); return; }
            string folder = Path.GetFullPath(Folder);
            if (!File.Exists(Path.Combine(folder, "Ashveil.exe"))) { fertig(false, "Spieltest-Build fehlt: " + folder + " (erst spieltest_bauen)"); return; }
            // Lauf 3 mit Menü-Probe (gleiches Argument wie Ashveil.UI.SpielmenueUi.ProbeArgument).
            // 22.10.2026 (Releaseablauf): Lauf 1/2 mit der Kurztest-Probe (Ashveil.Networking.SpieltestKurztestProbe.Argument:
            // Namensfeld EINSTIEG-01, F2 EINSTIEG-03, ein paar Schritte) und Lauf 4 = nach „Speichern und beenden“ an derselben Stelle laden
            // (seit der Nachbesserung 22.10. ebenfalls mit der Probe: Schritte, dann Fenster schliessen → Zeit und Stelle des Speicherns).
            PlaytestSmoke.Starten(folder, Profil, "Spieltest r2.16", Bericht, Zusatz, fertig, "-ashveil-menue-probe", "-ashveil-kurztest-probe", true);
        }

        private static void Zusatz(string log, int lauf)
        {
            string voelker = Zeile(log, "Charakter-Editor: wählbare Völker ");
            PlaytestSmoke.Check(voelker == Ashveil.Data.RaceCatalog.HumanId, $"nur Mensch wählbar (Charakter-Editor: {voelker ?? "keine Zeile"})");
            string welt = Zeile(log, "[Ashveil] Weltzeit: ");
            PlaytestSmoke.Check(welt != null && welt.Contains(", Welt 09:"), "Weltuhr startet um 09:00 (" + (welt ?? "keine Zeile") + ")");
            // #100: Karten (Minimap, Karte M, Entdeckungs-EP) – Daten aus StreamingAssets mit dem Szenenraster von Kareth v4
            string karten = Zeile(log, "[Karten] Kartendaten geladen: ");
            PlaytestSmoke.Check(karten != null && karten.Contains("Szenenraster kareth_v4"), "Karten geladen (" + (karten ?? "keine Zeile") + ")");
            // CORE-005 (09.10.2026): Die Bild-Spitzen-Messung v2 läuft mit (Sitzungsdatei unter Logs/Ashveil/Ruckler/)
            string spitzen = Zeile(log, "[Ashveil] Bild-Spitzen-Messung v2: ");
            PlaytestSmoke.Check(spitzen != null, "Bild-Spitzen-Messung v2 aktiv (" + (spitzen ?? "keine Zeile") + ")");
            // CORE-005 (09.10.2026 abends): Mausfilter an (Unitys Fenster umgeleitet) und ohne Fehler
            string maus = Zeile(log, "[Ashveil] Mausfilter: ");
            PlaytestSmoke.Check(maus != null && maus.StartsWith("an", StringComparison.Ordinal), "Mausfilter an (" + (maus ?? "keine Zeile") + ")");
            string mausFehler = Zeile(log, "[Ashveil] Mausfilter: aus (Fehler");
            PlaytestSmoke.Check(mausFehler == null, "Mausfilter ohne Fehler" + (mausFehler != null ? " (" + mausFehler + ")" : ""));
            // 22.10.2026 (REL-01/SPEICHERN-01): über das Fenster geschlossene Läufe (1, 2, 4; Lauf 3 beendet sich selbst) enden über
            // Application.Quit (Mausfilter fängt WM_CLOSE ab) – vorher beendete Unity innerhalb der Fensterprozedur und stürzte am Ende ab
            // (0xC0000005); PlaytestSmoke schickt seit 22.10. ein echtes WM_CLOSE statt CloseMainWindow (= TerminateProcess -2 im Editor-Mono)
            if (lauf != 3)
            {
                string zu = Zeile(log, "[Ashveil] Fenster schliessen");
                PlaytestSmoke.Check(zu != null, "Schliessen über das Fenster beendet über Application.Quit (REL-01): " + (zu != null ? "Fenster schliessen" + zu : "keine Zeile"));
            }
            // 22.10.2026 (Releaseablauf): Kurztest-Probe im fertigen Spiel – Lauf 1 Namensfeld (EINSTIEG-01), Lauf 2 F2 (EINSTIEG-03)
            if (lauf == 1) ProbeZeile(log, "Namensfeld ", "Namensfeld nimmt getippte Buchstaben an (EINSTIEG-01)");
            if (lauf == 2)
            {
                ProbeZeile(log, "F2 ", "F2 baut die eigene Figur nicht um (EINSTIEG-03)");
                // 22.10.2026 (REL-02): F10 darf im Spieltest-Build den Selbsttest nicht mehr an der eigenen Figur starten (vorher Hinweis „AKTIV“)
                string f10 = Zeile(log, "[Ashveil] Kurztest-Probe: Selbsttest-Taste F10 im Build: ");
                PlaytestSmoke.Check(f10 != null && f10.StartsWith("aus", StringComparison.Ordinal), "Selbsttest-Taste F10 im Spieltest-Build aus (REL-02): " + (f10 ?? "keine Zeile"));
                string gang = Zeile(log, "[Ashveil] Kurztest-Probe: gegangen ");
                PlaytestSmoke.Hinweis("Schritte der Probe: " + (gang ?? "keine Zeile"));
            }
            // 22.10.2026 (Nachbesserung Runde 4): Lauf 4 geht mit der Probe ein paar Schritte (Stellenprüfung beim Beenden in PlaytestSmoke)
            if (lauf == 4)
            {
                string gang = Zeile(log, "[Ashveil] Kurztest-Probe: gegangen ");
                PlaytestSmoke.Hinweis("Schritte der Probe (Lauf 4): " + (gang ?? "keine Zeile"));
            }
            if (lauf <= 2 || lauf == 4)
            {
                string eingabe = Zeile(log, "[Ashveil] Kurztest-Probe: Eingabe: ");
                PlaytestSmoke.Hinweis("Eingabe der Probe: " + (eingabe ?? "keine Zeile"));
            }
        }

        /// <summary>22.10.2026: Ergebniszeile der Kurztest-Probe – „bestanden“ = OK, „FEHLER“ = Fehler, „nicht geprüft“ = Hinweis.</summary>
        private static void ProbeZeile(string log, string teil, string label)
        {
            string z = Zeile(log, "[Ashveil] Kurztest-Probe: " + teil);
            if (z == null) PlaytestSmoke.Check(false, label + " (keine Zeile der Kurztest-Probe)");
            else if (z.StartsWith("nicht geprüft", StringComparison.Ordinal)) PlaytestSmoke.Hinweis(label + ": " + z);
            else PlaytestSmoke.Check(z.StartsWith("bestanden", StringComparison.Ordinal), label + " (" + z + ")");
        }

        /// <summary>Rest der ersten Logzeile, die den Anfang enthält (ohne Zeilenende), sonst null.</summary>
        private static string Zeile(string log, string anfang)
        {
            foreach (string roh in log.Split('\n'))
            {
                int i = roh.IndexOf(anfang, StringComparison.Ordinal);
                if (i >= 0) return roh.Substring(i + anfang.Length).Trim();
            }
            return null;
        }

        private static long Ordnergroesse(string dir)
        {
            try
            {
                return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(f => !f.FullName.Contains(Path.DirectorySeparatorChar + "Saves" + Path.DirectorySeparatorChar) &&
                                !f.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                    .Sum(f => f.Length);
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
