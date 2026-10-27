using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ashveil.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Spiel-Aufträge für den Auftragsordner (Welle 0; Max 06.10.2026 „Ja, erweitern“: Build-, Kurztest- und Spielstart-Aufträge,
    /// nur feste, geprüfte Aktionen):
    ///   „testclient_bauen“   Test-Client bauen wie Menü 200, ohne Dialoge.
    ///   „spieltest &lt;Name&gt;“  eine Spiel-Suite aus der festen Liste <see cref="Suiten"/> (die Suiten aus Menü Ashveil › Test, in
    ///                        Sandbox_M1; „kareth28“ seit 07.10., „begehung“ (#29) und „karten“ (#100) in Kareth v4): Szene öffnen, Play starten, auf den Bericht warten, Play
    ///                        beenden, Test-Clients schließen.
    ///                        Ergebnis: ERGEBNIS-Zeile und alle FEHLER- und LOG-Zeilen.
    /// Play läuft dabei sichtbar im Editor. Keine freien Szenen, Flags oder Befehle; nichts bei ungespeicherten Szenen.
    /// Die Weltuhr startet bei diesen Läufen immer um 15:30 (Editor-Standard, NET-020), auch wenn unter „Ashveil › Welt › Weltzeit
    /// im Editor“ etwas anderes gewählt ist; die Einstellung wird danach wiederhergestellt. So bleiben die Ergebnisse vergleichbar.
    /// Das Projekt startet Play ohne Neuladen der Skripte (Enter Play Mode Options), daher bleibt dieser Ablauf über Play hinweg im Speicher.
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragSpiel
    {
        private sealed class Suite
        {
            public string Flag, Szene, Bericht;
            /// <summary>Flag-Datei der Suite; leer = <see cref="DuoTestDirector.FlagFile"/>.</summary>
            public string FlagDatei;
            public bool Client;
            public int Minuten;
        }

        private static Suite Duo(string flag, string bericht, int minuten) =>
            new Suite { Flag = flag, Szene = SandboxSceneBuilder.ScenePath, Bericht = bericht, Client = true, Minuten = minuten };

        private static Suite Allein(string flag, string bericht, int minuten, string flagDatei = null) =>
            new Suite { Flag = flag, FlagDatei = flagDatei, Szene = SandboxSceneBuilder.ScenePath, Bericht = bericht, Client = false, Minuten = minuten };

        /// <summary>#28: ein Spieler in Kareth v4 (Figuren, Orte und Aufträge der Welle 0 gibt es nur dort).</summary>
        private static Suite AlleinKareth(string flag, string bericht, int minuten) =>
            new Suite { Flag = flag, Szene = KarethV4Builder.ScenePath, Bericht = bericht, Client = false, Minuten = minuten };

        /// <summary>
        /// Freigegebene Suiten (Name → Flag, Bericht, Test-Client nötig, Zeitlimit) – dieselben wie im Menü Ashveil › Test.
        /// Nicht dabei: Massenschlacht, die Messungen im fertigen Spiel (Leistung, Rendering-Grundlinie, GPU-Driven) und die Freischalt-Hilfen.
        /// </summary>
        private static readonly Dictionary<string, Suite> Suiten = new Dictionary<string, Suite>(StringComparer.OrdinalIgnoreCase)
        {
            { "selbsttest", Allein("1", "Ashveil_SelfTest_Editor.txt", 15, SelfTestRunner.EditorFlagFile) },
            { "zweispieler", Duo("1", "Ashveil_DuoTest.txt", 20) },
            { "fortschritt", Duo(DuoTestDirector.ProgressFlag, "Ashveil_ProgressTest.txt", 20) },
            { "gruppe_chat", Duo(DuoTestDirector.SocialFlag, "Ashveil_SocialTest.txt", 20) },
            { "kareth", Duo(DuoTestDirector.KarethFlag, "Ashveil_KarethTest.txt", 30) },
            { "gruppenabenteuer", Duo(DuoTestDirector.GroupFlag, "Ashveil_GroupTest.txt", 30) },
            { "gruppenabenteuer30", Duo(DuoTestDirector.GroupLongFlag, "Ashveil_GroupTest.txt", 50) },
            { "salzgruben", Duo(DuoTestDirector.MineFlag, "Ashveil_SalzgrubenTest.txt", 30) },
            { "actionbar", Duo(DuoTestDirector.BarFlag, "Ashveil_ActionbarTest.txt", 20) },
            { "charakter_editor", Duo(DuoTestDirector.CreatorFlag, "Ashveil_CreatorTest.txt", 20) },
            { "emotes", Duo(DuoTestDirector.EmoteFlag, "Ashveil_EmoteTest.txt", 20) },
            { "tageszeit", Duo(DuoTestDirector.DayNightFlag, "Ashveil_TageszeitTest.txt", 15) },
            { "lagerleben", Duo(DuoTestDirector.CampLifeFlag, "Ashveil_LagerlebenTest.txt", 30) },
            { "himmel", Duo(DuoTestDirector.SkyFlag, "Ashveil_HimmelTest.txt", 30) },
            { "silhouetten", Duo(DuoTestDirector.SilhouetteFlag, "Ashveil_SilhouettenTest.txt", 20) },
            { "weltzellen", Duo(DuoTestDirector.WorldFlag, "Ashveil_WeltTest.txt", 30) },
            { "kolosseum", Duo(DuoTestDirector.ArenaFlag, "Ashveil_KolosseumTest.txt", 30) },
            { "kolosseum_lauf", Duo(DuoTestDirector.ArenaRunFlag, "Ashveil_KolosseumLaufTest.txt", 30) },
            { "kampf", Duo(DuoTestDirector.CombatFlag, "Ashveil_KampfTest.txt", 20) },
            { "meisterschaften", Duo(DuoTestDirector.MasteryFlag, "Ashveil_MeisterschaftenTest.txt", 20) },
            { "reittier", Duo(DuoTestDirector.MountFlag, "Ashveil_ReittierTest.txt", 20) },
            { "nachlese", Duo(DuoTestDirector.SalvageFlag, "Ashveil_NachleseTest.txt", 20) },
            { "gefaehrte", Duo(DuoTestDirector.CompanionFlag, "Ashveil_GefaehrteTest.txt", 20) },
            { "pferdehof", Duo(DuoTestDirector.HorseFlag, "Ashveil_PferdehofTest.txt", 30) },
            { "fernkampf", Duo(DuoTestDirector.RangedFlag, "Ashveil_FernkampfTest.txt", 20) },
            { "klassen", Duo(DuoTestDirector.ClassesFlag, "Ashveil_KlassenTest.txt", 30) },
            { "balance", Allein(DuoTestDirector.BalanceFlag, "Ashveil_BalanceTest.txt", 30) },
            { "balance_gruppe", Duo(DuoTestDirector.BalanceGroupFlag, "Ashveil_BalanceGruppeTest.txt", 30) },
            { "glockenrast", Allein(DuoTestDirector.GlockeFlag, "Ashveil_GlockenrastTest.txt", 45) },
            { "glockenrast_duo", Duo(DuoTestDirector.GlockeDuoFlag, "Ashveil_GlockenrastDuoTest.txt", 45) },
            { "spawndichte", Duo(DuoTestDirector.SpawnDensityFlag, "Ashveil_SpawnDichteTest.txt", 20) },
            { "leistung_editor", Allein("1", "Ashveil_Leistung.txt", 20, PerformanceTest.FlagFile) },
            { "kareth28", AlleinKareth(DuoTestDirector.K28Flag, "Ashveil_Kareth28Test.txt", 40) },
            // #29 (07.10.2026): Begehung mit dem Charaktermotor – alle Wege beidseitig, Sperren, Ziele zu Fuß, Baumstämme
            { "begehung", AlleinKareth(DuoTestDirector.BegehungFlag, "Ashveil_BegehungTest.txt", 30) },
            // #100 (08.10.2026): Karten r2 – Banner, Minimap, Karte M, Entdeckungs-EP je Unterzone (PROG-013)
            { "karten", AlleinKareth(DuoTestDirector.KartenFlag, "Ashveil_KartenTest.txt", 15) },
            // Westweiden spielbar (12.10.2026): Weltzelle am Weidepass – Naht, Klamm zu Fuß, Laden/Entladen, NavMesh, Anker
            { "weidepass", AlleinKareth(DuoTestDirector.WeidepassFlag, "Ashveil_WeidepassTest.txt", 15) },
            // REP-059 (21.10.2026): neue UI-Zustände sichtbar – Auftragsbuch-Seiten, Tracker unter dem Rucksack, Abgabe bei vollem Rucksack, „Entkommen“
            { "ui_auftraege", AlleinKareth(DuoTestDirector.UiAuftraegeFlag, "Ashveil_UiAuftraegeTest.txt", 20) },
            // 21.10.2026 (Tripo-Kleidung): Kleid statt Schalen an CHR_W_TR01 (Vorschau/Charakter-Editor), Stufen, Verdeckung, Rückfall
            { "kleidung", Allein(DuoTestDirector.KleidungFlag, "Ashveil_KleidungTest.txt", 10) },
            // 21.10.2026 (Spielablauf Runde 3): die 21 Befund-Fixes im Host-Spiel nachprüfen – Eingabe, Handel, Kampf, Leiche, Grete, Tod, Weber
            { "spielablauf3", AlleinKareth(DuoTestDirector.Spielablauf3Flag, "Ashveil_Spielablauf3Test.txt", 20) },
            // 22.10.2026 (Mehrspieler-Proben): MEHRSPIELER-01/02/03/04 und EINSTIEG-04 mit zwei echten Test-Clients (Leiche außer Sicht und
            // Wiederverbinden, im Tod getrennt, toter Angreifer, Gruppenleitung) – Sandbox_M1, weil nur sie im Test-Client steckt
            { "mehrspieler4", Duo(DuoTestDirector.Mehrspieler4Flag, "Ashveil_Mehrspieler4Test.txt", 30) },
        };

        private enum Phase { Vorlauf, Starten, Laeuft, Beenden }

        /// <summary>
        /// Wartezeit vor Play: Ein unmittelbar vorher gelaufener Test-Runner räumt noch auf und meldet sonst „This cannot be used
        /// during play mode“ (gesehen 06.10. 21:25 im Tageszeit-Lauf direkt nach „tests“).
        /// </summary>
        private const double VorlaufSekunden = 8.0;

        private sealed class Lauf
        {
            public string Name, Bericht;
            public Suite Suite;
            public DateTime Start;
            public Phase Phase;
            public double NaechsterBlick;
            public string Ergebnis;
            public bool Fertig, Zeitueberschreitung;
            /// <summary>Editor-Einstellung der Weltzeit vor dem Lauf (null = nicht gesetzt), wird in <see cref="Ende"/> zurückgesetzt.</summary>
            public string ZeitVorher;
            public Action<bool, string> Melden;
        }

        private static Lauf _lauf;

        static AuftragSpiel()
        {
            Auftragsordner.Registrieren("testclient_bauen", (a, fertig) => fertig(NetworkSetupBuilder.BuildTestClientQuiet(out string m), m), true, 3600);
            Auftragsordner.Registrieren("spieltest", Spieltest, true, 3600);
        }

        private static void Spieltest(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string name = (a.parameter ?? "").Trim();
            if (!Suiten.TryGetValue(name, out Suite s))
            {
                fertig(false, "erlaubt: " + string.Join(", ", Suiten.Keys.OrderBy(k => k)));
                return;
            }
            if (_lauf != null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                fertig(false, "Play läuft schon – nichts gestartet");
                return;
            }
            if (s.Client && !NetworkSetupBuilder.TestClientCurrent(out string warum))
            {
                fertig(false, warum + " – erst Auftrag „testclient_bauen“");
                return;
            }
            if (!File.Exists(s.Szene))
            {
                fertig(false, "Szene fehlt: " + s.Szene);
                return;
            }
            if (EditorSceneManager.GetActiveScene().path != s.Szene) EditorSceneManager.OpenScene(s.Szene, OpenSceneMode.Single);
            NetworkSetupBuilder.StopTestClients();
            string flagDatei = string.IsNullOrEmpty(s.FlagDatei) ? DuoTestDirector.FlagFile : s.FlagDatei;
            Directory.CreateDirectory(Path.GetDirectoryName(flagDatei));
            File.WriteAllText(flagDatei, s.Flag);
            _lauf = new Lauf
            {
                Name = name, Suite = s, Bericht = Ashveil.Core.ProjectPaths.Logs(s.Bericht), Start = DateTime.Now, Phase = Phase.Vorlauf,
                ZeitVorher = EditorPrefs.HasKey(WorldTimeSettings.EditorPrefKey) ? EditorPrefs.GetString(WorldTimeSettings.EditorPrefKey) : null,
                Melden = fertig
            };
            EditorPrefs.SetString(WorldTimeSettings.EditorPrefKey, WorldTimeSettings.EditorDefault);
            EditorApplication.update -= Beobachten;
            EditorApplication.update += Beobachten;
            Debug.Log($"[Ashveil] Spieltest „{name}“: Play startet in {VorlaufSekunden:0} s ({s.Szene})");
        }

        private static void Beobachten()
        {
            Lauf l = _lauf;
            if (l == null)
            {
                EditorApplication.update -= Beobachten;
                return;
            }
            if (EditorApplication.timeSinceStartup < l.NaechsterBlick) return;
            l.NaechsterBlick = EditorApplication.timeSinceStartup + 2.0;
            double sek = (DateTime.Now - l.Start).TotalSeconds;
            switch (l.Phase)
            {
                case Phase.Vorlauf:
                    if (sek < VorlaufSekunden || EditorApplication.isCompiling || EditorApplication.isUpdating) break;
                    l.Phase = Phase.Starten;
                    l.Start = DateTime.Now;   // Bericht muss nach dem Play-Start geschrieben sein
                    // 08.10. (#100): Stand der Pause-Knopf noch, blieb Play nach dem ersten Bild stehen (Suite „karten“ hing 15 min).
                    if (EditorApplication.isPaused)
                    {
                        EditorApplication.isPaused = false;
                        Debug.LogWarning("[Ashveil] Spieltest: Der Editor stand auf Pause – vor dem Start aufgehoben.");
                    }
                    EditorApplication.isPlaying = true;
                    break;
                case Phase.Starten:
                    if (EditorApplication.isPlaying) l.Phase = Phase.Laeuft;
                    else if (sek > 120 && !EditorApplication.isPlayingOrWillChangePlaymode) Ende(l, false, "Play startete nicht");
                    break;
                case Phase.Laeuft:
                    if (!EditorApplication.isPlaying)
                    {
                        // Jemand hat Play beendet, bevor der Bericht fertig war
                        if (!EditorApplication.isPlayingOrWillChangePlaymode) { l.Ergebnis = Zwischenstand(l); l.Phase = Phase.Beenden; }
                        break;
                    }
                    if (EditorApplication.isPaused)
                    {
                        // z. B. „Error Pause“ der Konsole: weiterlaufen lassen – Fehler zählt die Suite selbst (LOG-Zeilen im Bericht)
                        EditorApplication.isPaused = false;
                        Debug.LogWarning("[Ashveil] Spieltest: Play war pausiert – fortgesetzt.");
                    }
                    if (BerichtFertig(l, out string text)) { l.Ergebnis = text; l.Fertig = true; l.Phase = Phase.Beenden; EditorApplication.isPlaying = false; }
                    else if (sek > l.Suite.Minuten * 60.0) { l.Ergebnis = Zwischenstand(l); l.Zeitueberschreitung = true; l.Phase = Phase.Beenden; EditorApplication.isPlaying = false; }
                    break;
                case Phase.Beenden:
                    if (EditorApplication.isPlayingOrWillChangePlaymode) break;
                    NetworkSetupBuilder.StopTestClients();
                    if (!l.Fertig)
                        Ende(l, false, (l.Zeitueberschreitung ? $"Zeitlimit {l.Suite.Minuten} min überschritten" : "Play endete vor dem Bericht") +
                                       "\n" + l.Ergebnis);
                    else Ende(l, Bestanden(l.Ergebnis), l.Ergebnis);
                    break;
            }
        }

        private static void Ende(Lauf l, bool ok, string meldung)
        {
            _lauf = null;
            EditorApplication.update -= Beobachten;
            if (l.ZeitVorher == null) EditorPrefs.DeleteKey(WorldTimeSettings.EditorPrefKey);
            else EditorPrefs.SetString(WorldTimeSettings.EditorPrefKey, l.ZeitVorher);
            Debug.Log($"[Ashveil] Spieltest „{l.Name}“ beendet: {(ok ? "ok" : "fehler")}");
            l.Melden(ok, meldung);
        }

        /// <summary>Bericht neu geschrieben und abgeschlossen (ERGEBNIS-Zeile, kein „läuft noch“)?</summary>
        private static bool BerichtFertig(Lauf l, out string text)
        {
            text = null;
            try
            {
                if (!File.Exists(l.Bericht) || File.GetLastWriteTime(l.Bericht) < l.Start) return false;
                string inhalt = File.ReadAllText(l.Bericht, Encoding.UTF8);
                if (!inhalt.Contains("ERGEBNIS:") || inhalt.Contains("(laeuft noch")) return false;
                text = Auszug(inhalt, l.Bericht);
                return true;
            }
            catch (IOException) { return false; }   // wird gerade geschrieben
        }

        private static string Zwischenstand(Lauf l)
        {
            try
            {
                if (File.Exists(l.Bericht) && File.GetLastWriteTime(l.Bericht) >= l.Start)
                    return Auszug(File.ReadAllText(l.Bericht, Encoding.UTF8), l.Bericht);
            }
            catch (IOException) { }
            return "(kein Bericht)";
        }

        /// <summary>ERGEBNIS-Zeile, alle FEHLER- und LOG-Zeilen und die letzten Zeilen bei Abbruch.</summary>
        private static string Auszug(string inhalt, string pfad)
        {
            string[] zeilen = inhalt.Replace("\r", "").Split('\n');
            var sb = new StringBuilder();
            string ergebnis = zeilen.LastOrDefault(z => z.StartsWith("ERGEBNIS:", StringComparison.Ordinal));
            sb.Append(ergebnis ?? "(noch kein ERGEBNIS)").Append(" · Bericht ").Append(pfad).Append('\n');
            foreach (string z in zeilen.Where(z => z.StartsWith("  FEHLER", StringComparison.Ordinal) || z.StartsWith("  LOG", StringComparison.Ordinal)).Take(60))
                sb.Append(z.Trim()).Append('\n');
            if (ergebnis == null)
                foreach (string z in zeilen.Where(z => z.Trim().Length > 0).Reverse().Take(6).Reverse()) sb.Append("… ").Append(z.Trim()).Append('\n');
            return sb.ToString().TrimEnd();
        }

        /// <summary>„ERGEBNIS: 24 bestanden, 0 fehlgeschlagen, 0 Fehler/Ausnahmen im Log“ → nichts fehlgeschlagen und keine Log-Fehler.</summary>
        private static bool Bestanden(string text)
        {
            string z = (text ?? "").Split('\n')[0];
            Match f = Regex.Match(z, @"(\d+) fehlgeschlagen"), l = Regex.Match(z, @"(\d+) Fehler/Ausnahmen");
            return z.StartsWith("ERGEBNIS:", StringComparison.Ordinal) && f.Success && f.Groups[1].Value == "0" && l.Success && l.Groups[1].Value == "0";
        }
    }
}
