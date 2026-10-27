using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using Ashveil.UI;
using Ashveil.World;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Ashveil.Networking
{
    /// <summary>
    /// 22.10.2026 (Runde 4: Mehrspieler, Speichern/Laden, Releaseablauf) – Teil 8 der Suite spielablauf3, im Host-Spiel (Kareth v4):
    ///   REL-02        echter F10-Druck wie im gebauten Spiel (SelfTestRunner.WieImBuildFuerTests, Selbsttest nicht angefordert) →
    ///                 der Selbsttest startet nicht; angefordert bzw. im Editor bleibt F10 aktiv; die Tastenbelegung warnt nicht mehr.
    ///   SPEICHERN-02  „Speichern und beenden“ außerhalb sicherer Orte: Countdown, Esc, sofort erneut → der Server antwortet „zu schnell“
    ///                 (Menü wieder bereit statt „Keine Antwort“); Menü zu während „Speichere …“ setzt die Bitte zurück (der nächste
    ///                 Klick startet wieder den Countdown statt sofort zu beenden); tot → Abbruch mit Grund („gefallen“).
    /// Jede Prüfung nennt mit „| alt: …“, woran sie mit dem Verhalten vor dem 22.10. gescheitert wäre. Beenden wird über
    /// SpielmenueUi.BeendenFuerTests abgefangen (das Spiel endet nicht).
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private IEnumerator Sp3Runde4(NetworkInventory a, Health hp)
        {
            Line("");
            Line("[8 Runde 4: F10 im gebauten Spiel (REL-02), „Speichern und beenden“ (SPEICHERN-02)]");
            yield return Sp3F10();
            yield return Sp3SpeichernBeenden(a, hp);
            yield return Sp3StelleBeimBeenden(a);
        }

        // ================================================================== REL-01: Stelle beim Beenden

        /// <summary>
        /// Kurztest 22.10. 14:21: Lauf 2 (über das Fenster → Application.Quit) speicherte beim Abbau die Stelle im Szenenzweig
        /// („szene:…“, WorldStreamer schon zerstört), Lauf 3 begann deshalb am Spawn. Seit dem Fix bleibt die in OnApplicationQuit
        /// erfasste Stelle stehen. Nachgestellt: OnApplicationQuit (wie beim Beenden), dann eine weitere Erfassung an anderer Stelle.
        /// </summary>
        private IEnumerator Sp3StelleBeimBeenden(NetworkInventory a)
        {
            Sp3Punkt("REL-01", "Speichern beim Beenden: die in OnApplicationQuit erfasste Stelle bleibt beim Abbau (OnStopServer) stehen");
            const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo capture = typeof(NetworkInventory).GetMethod("CapturePosition", f);
            MethodInfo quit = typeof(NetworkInventory).GetMethod("OnApplicationQuit", f);
            FieldInfo erfasst = typeof(NetworkInventory).GetField("_stelleBeimBeendenErfasst", f);
            if (capture == null || quit == null) { _uiFehlendeHaken.Add("NetworkInventory.CapturePosition/OnApplicationQuit"); yield break; }
            Vector3 p0 = a.transform.position;
            WorldPosition beimBeenden = default, danach = default;
            Vector3 p1 = p0;
            try
            {
                quit.Invoke(a, null);   // wie beim Beenden: speichert (Testprofil) und hält die Stelle fest
                beimBeenden = a.SavedPosition;
            }
            catch (Exception e) { Info("OnApplicationQuit nachgestellt: " + e.GetType().Name + " " + e.Message); }
            Vector3 weg = Gnd(p0.x + 25f, p0.z + 25f);
            yield return Ask(a, "goto", V(weg + Vector3.up * 0.3f, 0f));
            yield return new WaitForSecondsRealtime(1f);
            p1 = a.transform.position;
            capture.Invoke(a, null);   // wie das Speichern in OnStopServer während des Abbaus
            danach = a.SavedPosition;
            Check(beimBeenden.IsSet && danach.Equals(beimBeenden) && Flat(p1, p0) > 10f,
                $"Nach OnApplicationQuit ({beimBeenden.cellId} {beimBeenden.localPosition.x:0}/{beimBeenden.localPosition.z:0}) erfasst eine weitere Speicherung {Flat(p1, p0):0} m weiter " +
                $"{(danach.Equals(beimBeenden) ? "nichts Neues" : $"NEU {danach.cellId} {danach.localPosition.x:0}/{danach.localPosition.z:0}")} " +
                "| alt: die Erfassung beim Abbau überschrieb die Stelle (mit zerstörtem WorldStreamer „szene:…“ → nächster Login am Spawn)");
            erfasst?.SetValue(a, false);   // Testfigur wieder normal
            capture.Invoke(a, null);
            Check(a.SavedPosition.IsSet && !a.SavedPosition.Equals(beimBeenden) && Flat(a.SavedPosition.localPosition, p1) < 2f,
                "Gegenprobe ohne Beenden: die Erfassung folgt wieder der Figur");
            Sp3NichtGeprueft("REL-01", "echter Abbau im gebauten Spiel – prüft der Kurztest (Lauf 2 → 3: „betritt die Welt an der gespeicherten Stelle“)");
        }

        // ================================================================== REL-02: F10

        private IEnumerator Sp3F10()
        {
            Sp3Punkt("REL-02", "Echter F10-Druck wie im gebauten Spiel (Selbsttest nicht angefordert) → kein Selbsttest an der eigenen Figur");
            SelfTestRunner runner = FindAnyObjectByType<SelfTestRunner>();
            FieldInfo laeuftFeld = typeof(SelfTestRunner).GetField("_running", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo angefordertFeld = typeof(SelfTestRunner).GetField("_requested", BindingFlags.NonPublic | BindingFlags.Instance);
            if (laeuftFeld == null || angefordertFeld == null) _uiFehlendeHaken.Add("SelfTestRunner._running/_requested");
            string eingabe = Sp3EingabeAn();
            Keyboard kb = Keyboard.current;
            if (runner == null || !runner.isActiveAndEnabled || laeuftFeld == null || angefordertFeld == null || kb == null || !kb.enabled)
            {
                Sp3NichtGeprueft("REL-02", runner == null ? "kein SelfTestRunner in der Szene" : !runner.isActiveAndEnabled ? "SelfTestRunner nicht aktiv" : "Tastatur/Felder fehlen (" + eingabe + ")");
                yield break;
            }
            bool angefordert = (bool)angefordertFeld.GetValue(runner);
            bool laeuftVorher = (bool)laeuftFeld.GetValue(runner);
            Info($"SelfTestRunner in der Szene: aktiv, angefordert {angefordert}, läuft {laeuftVorher}; {eingabe}");
            bool wieVorher = SelfTestRunner.WieImBuildFuerTests;
            bool kam = false, lief = false, aktivImBuild = true;
            SelfTestRunner.WieImBuildFuerTests = true;
            try
            {
                aktivImBuild = runner.F10Aktiv;
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.F10));
                yield return null;
                kam = RawInput.WasPressed(GameKey.SelfTest);
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                yield return null;
                yield return null;
                lief = (bool)laeuftFeld.GetValue(runner);
            }
            finally
            {
                SelfTestRunner.WieImBuildFuerTests = wieVorher;
                // Rot-Fall (altes Verhalten): der Selbsttest wäre an der Testfigur gestartet – sofort stoppen (OnDisable stellt zurück)
                if (lief && !laeuftVorher) { runner.enabled = false; runner.enabled = true; }
            }
            if (!kam) Sp3NichtGeprueft("REL-02", "der eingespeiste F10-Druck kam nicht im Spiel an");
            else Check(!lief && !aktivImBuild && !angefordert,
                $"F10 kam an (RawInput.WasPressed), wie im Build: F10 aktiv {aktivImBuild}, Selbsttest gestartet {(lief ? "JA" : "nein")} | alt: Selbsttest startet an der eigenen Figur (Teleport, Kampf, Beute, gespeichert)");
            // Gegenproben: angefordert (-ashveil-selftest / Menü-Flag) bzw. im Editor bleibt F10 wie bisher
            angefordertFeld.SetValue(runner, true);
            SelfTestRunner.WieImBuildFuerTests = true;
            bool mitAnforderung = runner.F10Aktiv;
            SelfTestRunner.WieImBuildFuerTests = wieVorher;
            angefordertFeld.SetValue(runner, angefordert);
            Check(mitAnforderung && runner.F10Aktiv, $"Gegenprobe: mit angefordertem Selbsttest im Build F10 aktiv {mitAnforderung}; im Editor F10 aktiv {runner.F10Aktiv}");
            FieldInfo reserviert = typeof(KeybindUi).GetField("Reserved", BindingFlags.NonPublic | BindingFlags.Static);
            if (reserviert == null) _uiFehlendeHaken.Add("KeybindUi.Reserved");
            else Check(reserviert.GetValue(null) is IDictionary<string, string> res && !res.ContainsKey("Key:F10") && res.ContainsKey("Key:F9"),
                "Tastenbelegung warnt im Spiel nicht mehr vor F10 („Selbsttest“, nur noch im Editor); F9 bleibt reserviert | alt: Warnung „auch Selbsttest – beides passiert“");
            Sp3NichtGeprueft("REL-02", "gebauter Spieltest-Build – prüft der Kurztest (Lauf 2: „Selbsttest-Taste F10 im Spieltest-Build aus“)");
        }

        // ================================================================== SPEICHERN-02: „Speichern und beenden“

        private IEnumerator Sp3SpeichernBeenden(NetworkInventory a, Health hp)
        {
            Sp3Punkt("SPEICHERN-02", "Ausgangslage: außerhalb sicherer Orte, kein Fenster offen, Beenden abgefangen");
            SpielmenueUi menue = SpielmenueUi.Instance;
            NetworkPlayer np = a.GetComponent<NetworkPlayer>();
            Vector3? draussen = Sp3Draussen(a);
            if (menue == null || np == null || !draussen.HasValue)
            {
                Check(false, $"Spielmenü {(menue != null ? "da" : "FEHLT")}, NetworkPlayer {(np != null ? "da" : "FEHLT")}, Ort außerhalb sicherer Orte {(draussen.HasValue ? "gefunden" : "NICHT gefunden")}");
                yield break;
            }
            yield return Ask(a, "goto", V(draussen.Value + Vector3.up * 0.3f, 0f));
            yield return new WaitForSecondsRealtime(1.5f);
            if (a.LootOpen) a.CloseLoot();
            for (int i = 0; i < 5 && (UiFensterstapel.Oberstes != "" || menue.IsOpen); i++)
            {
                if (menue.IsOpen) menue.Schliessen();
                else UiFensterstapel.EscAusloesen();
                yield return UiTakt();
            }
            bool sicher = NetworkInventory.ServerSichererOrt(a.transform.position);
            Info($"Stand {a.transform.position.x:0}/{a.transform.position.z:0}: sicherer Ort {sicher}; Fenster offen „{UiFensterstapel.Oberstes}“; Menü {(menue.IsOpen ? "offen" : "zu")}");
            if (sicher) { Check(false, "Voraussetzung: Stand außerhalb sicherer Orte"); yield break; }
            yield return new WaitForSecondsRealtime(2.3f);   // etwaige 2-s-Sperre früherer Bitten abwarten

            int beendet = 0;
            Action beendenVorher = SpielmenueUi.BeendenFuerTests;
            SpielmenueUi.BeendenFuerTests = () => beendet++;
            Func<NetworkPlayer, string> haltVorher = NetworkPlayer.HoldRespawn;
            try
            {
                // ---------------------------------------------------------- 1. Countdown, Esc, sofort erneut → „zu schnell“
                Sp3Punkt("SPEICHERN-02", "Countdown, Esc bricht ab, sofort erneut „Speichern und beenden“ (innerhalb der 2-s-Sperre) → Antwort des Servers");
                menue.Oeffnen();
                yield return UiTakt();
                float t0 = Time.unscaledTime;
                menue.SpeichernUndBeenden();
                yield return WaitUntil(() => menue.Countdown && a.ServerAbmeldenLaeuft, 3f);
                bool countdown1 = menue.Countdown && a.ServerAbmeldenLaeuft;
                menue.Schliessen();   // Esc während des Countdowns
                // warten, bis der Abbruch beim Server UND die Rückmeldung beim Client ist (sonst käme sie in die zweite Bitte)
                yield return WaitUntil(() => !a.ServerAbmeldenLaeuft && SitzungClient.LetzterAbbruch == "abgebrochen", 2f);
                bool abgebrochen1 = !a.ServerAbmeldenLaeuft && !SitzungClient.Wartet && SitzungClient.LetzterAbbruch == "abgebrochen";
                menue.Oeffnen();
                yield return UiTakt();
                float t1 = Time.unscaledTime;
                string abbruchVorher = SitzungClient.LetzterAbbruch;
                menue.SpeichernUndBeenden();
                bool gesendet = SitzungClient.Wartet;
                yield return WaitUntil(() => !SitzungClient.Wartet && SitzungClient.LetzterAbbruch == SitzungClient.ZuSchnell, 2.5f);
                yield return UiTakt();
                float antwort = Time.unscaledTime - t1;
                string status = menue.Status;
                Info($"Erste Bitte bei {t0:0.00} s, Countdown {(countdown1 ? "ja" : "NEIN")}, abgebrochen {(abgebrochen1 ? "ja" : "NEIN")}; zweite Bitte {t1 - t0:0.00} s danach (Sperre 2 s), " +
                     $"gesendet {gesendet}, Abbruchgrund vorher „{abbruchVorher}“ → „{SitzungClient.LetzterAbbruch}“ nach {antwort:0.00} s");
                yield return Sp3Foto("speichern02_zu_schnell");
                Check(countdown1 && abgebrochen1 && gesendet && t1 - t0 < 2f && !SitzungClient.Wartet && SitzungClient.LetzterAbbruch == SitzungClient.ZuSchnell &&
                      status.Contains("Moment") && !menue.Countdown && menue.IsOpen && beendet == 0 && !a.ServerAbmeldenLaeuft,
                    $"Zweite Bitte binnen 2 s: Server antwortet „{SitzungClient.LetzterAbbruch}“, Bitte erledigt (Wartet {SitzungClient.Wartet}), Menü „{Sp3OhneTags(status)}“, beendet {beendet}× " +
                    "| alt: keine Antwort – Wartet bleibt, nach 6 s „Keine Antwort vom Server“");

                // ---------------------------------------------------------- 2. Menü zu während „Speichere …“ → Bitte zurückgesetzt
                Sp3Punkt("SPEICHERN-02", "Menü schließen, während „Speichere …“ steht (vor der Antwort des Servers) → nächster Klick startet wieder den Countdown");
                yield return new WaitForSecondsRealtime(2.3f);
                menue.SpeichernUndBeenden();
                bool gesendet2 = SitzungClient.Wartet;
                menue.Schliessen();   // gleiche Bildfolge: die Antwort des Servers ist noch unterwegs
                bool frei = !SitzungClient.Wartet;
                yield return new WaitForSecondsRealtime(1f);
                bool serverRuhig = !a.ServerAbmeldenLaeuft;
                Check(gesendet2 && frei && serverRuhig && beendet == 0,
                    $"Menü zu während „Speichere …“: Bitte zurückgesetzt (Wartet {!frei}), Countdown am Server {(serverRuhig ? "abgebrochen/keiner" : "LÄUFT weiter")} | alt: Wartet bleibt stehen, Countdown läuft unsichtbar weiter");
                yield return new WaitForSecondsRealtime(2.3f);
                menue.Oeffnen();
                yield return UiTakt();
                menue.SpeichernUndBeenden();
                yield return WaitUntil(() => menue.Countdown || beendet > 0, 3f);
                bool countdown2 = menue.Countdown && a.ServerAbmeldenLaeuft;
                Check(countdown2 && beendet == 0,
                    $"Nächster Klick auf „Speichern und beenden“: Countdown {(countdown2 ? "läuft" : "FEHLT")}, Spiel beendet {beendet}× | alt: „kein Charakter verbunden“ – das Spiel endet sofort ohne Countdown und ohne Speichern-Bitte");
                menue.Schliessen();
                yield return WaitUntil(() => !a.ServerAbmeldenLaeuft, 2f);

                // ---------------------------------------------------------- 3. Tot außerhalb sicherer Orte → Abbruch mit Grund
                Sp3Punkt("SPEICHERN-02", "Gefallen außerhalb sicherer Orte: „Speichern und beenden“ bricht mit Grund ab");
                yield return new WaitForSecondsRealtime(2.3f);
                NetworkPlayer.HoldRespawn = p => ReferenceEquals(p, np) ? "Gefallen – die Suite hält die Figur liegen." : haltVorher?.Invoke(p);
                UiSetze(hp, "_invulnerableUntil", -1f);
                hp.ApplyDamage(new DamageInfo { Amount = hp.Max * 20f, Direction = -a.transform.forward, Unblockable = true });
                yield return WaitUntil(() => !hp.IsAlive && np.ServerHeldDown, 3f);
                yield return new WaitForSecondsRealtime(0.5f);
                if (!hp.IsAlive)
                {
                    menue.Oeffnen();
                    yield return UiTakt();
                    bool offen = menue.IsOpen;
                    menue.SpeichernUndBeenden();
                    yield return WaitUntil(() => !SitzungClient.Wartet && a.ServerAbmeldenAbbruch == "gestorben", 3f);
                    yield return UiTakt();
                    string statusTot = menue.Status;
                    yield return Sp3Foto("speichern02_tot");
                    Check(offen && a.ServerAbmeldenAbbruch == "gestorben" && statusTot.Contains("gefallen") && statusTot.Contains("Erwachen") && beendet == 0,
                        $"Tot: Server bricht ab („{a.ServerAbmeldenAbbruch}“), Menü „{Sp3OhneTags(statusTot)}“ | alt: nur „Abgebrochen.“ ohne Grund");
                }
                else Check(false, $"Figur gefallen ({hp.Current:0}/{hp.Max:0})");
            }
            finally
            {
                NetworkPlayer.HoldRespawn = haltVorher;
                if (menue.IsOpen) menue.Schliessen();
                if (!hp.IsAlive) np.ServerReviveHere(1f);
                hp.SetInvulnerableFor(7200f);
                SpielmenueUi.BeendenFuerTests = beendenVorher;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            Check(hp.IsAlive && !menue.IsOpen && !SitzungClient.Wartet && !a.ServerAbmeldenLaeuft && beendet == 0,
                $"Danach: Figur lebt, Menü zu, keine Bitte offen, am Server kein Countdown mehr ({(a.ServerAbmeldenLaeuft ? "HÄNGT – weitere Bitten würden ignoriert" : "frei")}), Spiel nicht beendet " +
                "| alt: nach dem Abbruch im Tod blieb am Server ein beendeter Countdown stehen – jede weitere Bitte der Sitzung wurde ignoriert");
            if (!a.ServerAbmeldenLaeuft)
            {
                // Gegenprobe nach dem Erwachen: eine neue Bitte startet wieder den Countdown
                yield return new WaitForSecondsRealtime(2.3f);
                SpielmenueUi.BeendenFuerTests = () => beendet++;
                try
                {
                    menue.Oeffnen();
                    yield return UiTakt();
                    menue.SpeichernUndBeenden();
                    yield return WaitUntil(() => menue.Countdown || beendet > 0, 3f);
                    Check(menue.Countdown && a.ServerAbmeldenLaeuft && beendet == 0, $"Nach dem Erwachen: „Speichern und beenden“ startet wieder den Countdown ({(menue.Countdown ? "läuft" : "FEHLT")})");
                    menue.Schliessen();
                    yield return WaitUntil(() => !a.ServerAbmeldenLaeuft, 2f);
                }
                finally
                {
                    if (menue.IsOpen) menue.Schliessen();
                    SpielmenueUi.BeendenFuerTests = beendenVorher;
                }
            }
            Sp3NichtGeprueft("SPEICHERN-02", "echter Client über das Netz (Antwortzeit > 0) – geprüft im Host-Spiel, dieselben RPCs");
        }

        /// <summary>Begehbarer Ort außerhalb sicherer Orte (Stützpunkte, Ankunftsplatz) nahe der Vorratswiesen.</summary>
        private static Vector3? Sp3Draussen(NetworkInventory a)
        {
            Vector3 basis = Sp3RuhigerOrt(a);
            for (int i = 0; i < 17; i++)
            {
                float r = i == 0 ? 0f : 40f * ((i - 1) / 4 + 1), w = ((i - 1) % 4) * 90f * Mathf.Deg2Rad;
                Vector3 p = basis + new Vector3(Mathf.Sin(w), 0f, Mathf.Cos(w)) * r;
                if (!NavMesh.SamplePosition(p, out NavMeshHit h, 8f, NavMesh.AllAreas)) continue;
                if (!NetworkInventory.ServerSichererOrt(h.position)) return h.position;
            }
            return null;
        }

        private static string Sp3OhneTags(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<[^>]+>", "").Replace("\n", " ");
    }
}
