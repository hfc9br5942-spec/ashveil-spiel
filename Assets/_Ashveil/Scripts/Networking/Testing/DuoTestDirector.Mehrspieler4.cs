using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Weapons;
using Ashveil.World;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// 22.10.2026 (Spielablauf Runde 4, Mehrspieler-Proben): die Mehrspieler-Fixes aus Runde 3 mit ECHTEN Test-Clients – Runde 3 hatte
    /// sie nur auf dem Host bzw. per Direktaufruf geprüft. Editor = Host A, Test-Client B (Profil „duo-welt-…“, stellt seine Position
    /// wieder her wie ein echter Spieler; Aschenjäger mit Dolchen und Kurzbogen), Test-Client C (Aschenweber mit Nachlese).
    ///   1 MEHRSPIELER-03  B schickt Nahkampf, Schuss und Geschoss direkt an den Server (an der Sperre des eigenen Clients vorbei, wie
    ///                     ein veränderter/verspäteter Client) – lebend angenommen (Gegenprobe), tot verworfen („verworfen (tot)“).
    ///     KAMPF-SCHUSS    (seit 22.10.2026, Nachbesserung Runde 4) derselbe Bogen über den Sofortschuss-Weg (SubmitShot) → verworfen
    ///                     („Waffentyp passt nicht zum Schuss“, kein Schaden, kein Pfeil); über den Geschoss-Weg → angenommen, −1 Pfeil.
    ///                     Den Tod über den Schuss-Weg prüft die Suite deshalb nicht mehr (braucht eine Feuerwaffe; „nicht geprüft“).
    ///   2 MEHRSPIELER-01  Eigene Leiche weit vom Spawn: B (Beute, golden) und C (Nachlese, grün) gehen außer Sicht (FishNet zerstört die
    ///                     Client-Kopie), kommen zurück → neue Kopie glimmt; danach Wiederverbinden fern der Leiche und hin → glimmt.
    ///   3 MEHRSPIELER-02 / EINSTIEG-04  B lebt in einer Weltzelle (Speicherstand dort), stirbt und trennt im Tod: (a) Sandbox ohne
    ///                     Bindepunkt, Verbindung neu → Neuanmeldung am Wachtfeuer (Spawn); (b) mit einem Bindepunkt in einer Weltzelle
    ///                     wie Kareth v4, Spiel beendet und neu gestartet → Neuanmeldung am Bindepunkt. Nie am Sterbeort.
    ///                     „tot beim Trennen“ misst die Suite seit 22.10.2026 (Nachbesserung) am Server im Augenblick des Trennens
    ///                     (OnRemoteConnectionState Stopped, unmittelbar vor Despawn und Speichern).
    ///   4 MEHRSPIELER-04  Gruppe A (Leitung), B, C: B beendet das Spiel (Offline-Mitglied, älter als C), A verlässt über seinen Client
    ///                     (NetworkSocial.ServerLeave) → C führt, sieht sich als Leitung und kann einladen.
    ///   1b (seit 23.10.2026, Paket C, DuoTestDirector.Mehrspieler4.PaketC.cs) KAMPF-REAKTION (Treffer-Ergebnis je Fall genau einmal bei B),
    ///                     KAMPF-WIRKEN (C's Wirken für B sichtbar, Ende ausgelöst/abgebrochen), PAKETC-LEICHE (Trennpunkt als Leichenzustand
    ///                     für den späten Beobachter C, Wiederbeleben → 0). Neuer Agent-Befehl „paketc“ (nur lesen).
    ///   3b (seit 22.10.2026, Runde 6, DuoTestDirector.Mehrspieler4.Runde6.cs) Kolosseum mit A und C: INSTANZEN-01 (Gruppe folgt in die
    ///                     Allein-Instanz), INSTANZEN-04 (Salzgruben-Reset auf dem Host), INSTANZEN-03 (Ruhe der Asche im Ring), INSTANZEN-02
    ///                     (Truhe bei vollem Rucksack, Rückkehr über den Eingang), STORY-03 (Truhe beim Trennen ausgezahlt).
    ///   5 (seit 22.10.2026, Runde 4, DuoTestDirector.Mehrspieler4.Runde4.cs) MEHRSPIELER-05 Host-Start bei belegtem Port und zweite
    ///                     Anmeldung des Host-Profils; REL-01 Fenster schließen (WM_CLOSE/SC_CLOSE) → Application.Quit, Exit 0.
    /// Jede Prüfung ist so gebaut, dass sie mit dem Verhalten vor Runde 3 rot wird (Text „| alt: …“ am Ende der Zeile); Voraussetzungen
    /// stehen als Info-Zeilen und werden nur bei Nichterfüllung zum Fehler. Kein Spielcode geändert; neue Testbefehle des Agenten:
    /// „leiche“, „angriff-direkt“ (keine neuen RPCs). Start: Auftrag „spieltest mehrspieler4“. Bericht: Logs/Ashveil/Ashveil_Mehrspieler4Test.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string Mehrspieler4Flag = "mehrspieler4";
        private static bool _mehrspieler4Mode;
        private const string Mp4A = "duo-mp4-a", Mp4B = "duo-welt-mp4-b", Mp4C = "duo-mp4-c";
        private const string Mp4KlasseB = "class.ashstalker", Mp4KlasseC = "class.emberweaver";
        private const string Mp4Halten = "Gefallen – die Suite hält die Figur liegen.";

        private NetworkInventory _mp4B, _mp4C;
        private GameObject _mp4Bindepunkt;
        private int _mp4TotLog;
        private string _mp4TotMuster;
        private int _mp4WaffentypLog;          // 22.10.2026 (KAMPF-SCHUSS)
        private string _mp4WaffentypMuster;

        private IEnumerator Mehrspieler4Suite()
        {
            _sp3.Clear();
            _sp3Reihenfolge.Clear();
            _sp3Bilder.Clear();
            _sp3Id = null;
            _uiFehlendeHaken.Clear();
            Sp3Anlegen("MEHRSPIELER-03", "Toter Angreifer: Nahkampf, Schuss, Geschoss vom Server verworfen (echter Test-Client)");
            // 22.10.2026 (Nachbesserung Runde 4, Abnahme-Mangel 4): Bogen über den Sofortschuss-Weg
            Sp3Anlegen("KAMPF-SCHUSS", "Bogen über den Sofortschuss (ServerShot) verworfen, über das Geschoss (ServerProjectile) angenommen mit Pfeilverbrauch");
            Sp3Anlegen("MEHRSPIELER-01", "Eigene Leiche glimmt nach Despawn (Sichtweite) und nach Wiederverbinden wieder – Beute und Nachlese");
            Sp3Anlegen("MEHRSPIELER-02", "EINSTIEG-04/MEHRSPIELER-02: im Tod getrennt → Neuanmeldung am Wachtfeuer/Bindepunkt, nicht am Sterbeort");
            Sp3Anlegen("MEHRSPIELER-04", "Gruppenleitung geht beim Verlassen an ein Online-Mitglied (echte Clients)");
            // 22.10.2026 (Runde 4, Teil 5)
            Sp3Anlegen("MEHRSPIELER-05", "Host-Start bei belegtem Port verbindet nicht still; zweite Anmeldung des Host-Profils wirft den Host nicht hinaus");
            Sp3Anlegen("REL-01", "Fenster schließen (WM_CLOSE / Alt+F4) beendet über Application.Quit mit Exit 0");
            // 22.10.2026 (Runde 6, Teil 3b, DuoTestDirector.Mehrspieler4.Runde6.cs): Kolosseum mit Test-Client C
            Sp3Anlegen("INSTANZEN-01", "Gruppe folgt einem Mitglied in seine Allein-Instanz (dieselbe Arena)");
            Sp3Anlegen("INSTANZEN-04", "Ring-Anzeige des Hosts bleibt beim Salzgruben-Reset");
            Sp3Anlegen("INSTANZEN-03", "„Ruhe der Asche“ im Ring abgelehnt, draußen wirksam");
            Sp3Anlegen("INSTANZEN-02", "Kolosseum-Truhe bei vollem Rucksack: Gegenstand bleibt in der Truhe, Rückkehr über den Eingang");
            Sp3Anlegen("STORY-03", "Kolosseum-Truhe wird beim Trennen ausgezahlt");
            Mp4PaketCAnlegen();   // 23.10.2026 (Paket C / Kampfreaktionen, DuoTestDirector.Mehrspieler4.PaketC.cs)
            _mp4ProzessC = null;
            _mp4Abgewiesen = 0;

            Line("");
            Line("[Vorbereitung]");
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(Mp4A)) != null, 30f);
            if (!Mp4Voraus(a != null, "Spieler A (Editor-Host) angemeldet")) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!Mp4Voraus(File.Exists(exe), "Test-Client vorhanden")) yield break;
            Mp4StarteClient(exe, Mp4B, Mp4KlasseB);
            Mp4StarteClient(exe, Mp4C, Mp4KlasseC);
            NetworkInventory b = null, c = null;
            yield return WaitUntil(() => (b = OnlineInv(Mp4B)) != null && (c = OnlineInv(Mp4C)) != null, 150f);
            if (!Mp4Voraus(b != null && c != null, "B und C (zwei Test-Clients) angemeldet")) yield break;
            _mp4B = b;
            _mp4C = c;
            yield return new WaitForSecondsRealtime(2f);
            GameObject spawn = GameObject.Find("PlayerSpawn");
            Vector3 lager = spawn != null ? spawn.transform.position : Vector3.zero;
            a.GetComponent<Health>()?.SetInvulnerableFor(7200f);
            c.GetComponent<Health>()?.SetInvulnerableFor(7200f);
            b.ServerSetLevel(15);
            c.ServerSetLevel(15);
            yield return new WaitForSecondsRealtime(1f);
            Info($"A {a.CharacterName} ({a.ServerModel.Class?.DisplayName}), B {b.CharacterName} ({b.ServerModel.Class?.DisplayName}, Stufe {b.ServerModel.Level}), " +
                 $"C {c.CharacterName} ({c.ServerModel.Class?.DisplayName}, Stufe {c.ServerModel.Level}); Wachtfeuer/Spawn {lager.x:0}/{lager.z:0}");

            Func<NetworkPlayer, string> haltVorher = NetworkPlayer.HoldRespawn;
            Application.logMessageReceived += Mp4Log;
            try
            {
                yield return Mp4ToterAngreifer(lager);
                yield return Mp4PaketC(a, lager);   // 23.10.2026: KAMPF-REAKTION, KAMPF-WIRKEN, PAKETC-LEICHE (DuoTestDirector.Mehrspieler4.PaketC.cs)
                yield return Mp4Leiche(lager);
                yield return Mp4TotGetrennt(exe);
                yield return Mp6Kolosseum(a);   // 22.10.2026 (Runde 6): INSTANZEN-01/-02/-03/-04, STORY-03 (DuoTestDirector.Mehrspieler4.Runde6.cs)
                yield return Mp4Gruppenleitung(a);
                yield return Mp4Release(a, exe);   // 22.10.2026: MEHRSPIELER-05, REL-01 (DuoTestDirector.Mehrspieler4.Runde4.cs)
            }
            finally
            {
                Application.logMessageReceived -= Mp4Log;
                NetworkPlayer.HoldRespawn = haltVorher;
                if (_mp4Bindepunkt != null) Destroy(_mp4Bindepunkt);
                _mp4Bindepunkt = null;
            }
            Mp4Zusammenfassung();
        }

        // ================================================================== 1. MEHRSPIELER-03: toter Angreifer

        private sealed class Mp4Angriff
        {
            public bool Gesendet, Angenommen, Verworfen;
            public int Pfeile, TotLog, WaffentypLog;   // WaffentypLog seit 22.10.2026 (KAMPF-SCHUSS)
            public float Leben;
            public string Text = "";
        }

        private IEnumerator Mp4ToterAngreifer(Vector3 lager)
        {
            Line("");
            Line("[1 MEHRSPIELER-03: toter Angreifer – B (Test-Client) schickt Nahkampf, Schuss, Geschoss direkt an den Server]");
            Sp3Punkt("MEHRSPIELER-03", "Angriffsbitten von B's Client (NetworkCombat.Submit…, an der Client-Sperre vorbei): lebend → angenommen; tot → verworfen");
            NetworkInventory b = _mp4B;
            var nc = b.GetComponent<NetworkCombat>();
            var bw = b.GetComponent<WeaponUser>();
            var bh = b.GetComponent<Health>();
            var np = b.GetComponent<NetworkPlayer>();
            bool waffen = bw != null && bw.MeleeWeapon != null && bw.RangedWeapon != null && bw.RangedWeapon.IsProjectile && b.ServerModel.AmmoCount > 5;
            if (!Mp4Voraus(nc != null && bh != null && np != null && waffen,
                    $"B führt Nahkampfwaffe ({bw?.MeleeWeapon?.Id}) und Bogen ({bw?.RangedWeapon?.Id}), {b.ServerModel.AmmoCount} Pfeile")) yield break;
            EnemySpawnArea vorlage = Mp4Vorlage("enemy.ash_wolf");
            if (!Mp4Voraus(vorlage != null && EnemyDirector.Instance != null, "Aschwolf-Vorlage und Gegner-Regie")) yield break;
            EnemyBrain wolf = EnemyDirector.Instance.SpawnAt(vorlage.Prefab, Gnd(lager.x + 6f, lager.z + 18f), 180f, b.ServerModel.Level, "mp4-ziel",
                br => br.SetAggroProfile(AggroProfile.Passive), 20f);
            if (!Mp4Voraus(wolf != null, "Aschwolf als Ziel erzeugt (passiv)")) yield break;
            yield return new WaitForSecondsRealtime(1f);
            wolf.ServerStun(180f);
            Health wh = wolf.GetComponent<Health>();
            int zielId = wolf.GetComponent<NetworkObject>().ObjectId;
            Vector3 w = wolf.transform.position;
            Vector3 weg = Flach(lager - w);
            weg = weg.sqrMagnitude < 0.01f ? Vector3.back : weg.normalized;
            Vector3 stand = Gnd(w.x + weg.x * 1.8f, w.z + weg.z * 1.8f);
            yield return Ask(b, "goto", V(stand, YawTo(stand, w)));
            yield return new WaitForSecondsRealtime(1.5f);
            _mp4TotMuster = $"Angriff von Spieler {b.Owner.ClientId + 1} verworfen (tot)";
            _mp4WaffentypMuster = $"Angriff von Spieler {b.Owner.ClientId + 1} verworfen (Waffentyp passt nicht zum Schuss)";   // 22.10.2026 (KAMPF-SCHUSS)
            Info($"Ziel: Aschwolf Stufe {wolf.Level} betäubt, B steht {Flat(b.transform.position, w):0.0} m davor; Kampf-Ablehnungen bisher {nc.ServerRejected}");

            // ---------------------------------------------------------- lebend (Gegenprobe: dieselben Bitten werden angenommen)
            // Zwischen Schuss und Geschoss ≥ 3,2 s: beide teilen die Schusssperre; sonst würde schon das alte Spiel „zu schnell“ ablehnen.
            wh.Heal(wh.Max);
            var lebendNah = new Mp4Angriff();
            yield return Mp4Schicke(b, "melee", zielId, wh, lebendNah);
            var lebendSchuss = new Mp4Angriff();
            yield return Mp4Schicke(b, "shot", zielId, wh, lebendSchuss);
            yield return new WaitForSecondsRealtime(3.2f);
            var lebendGeschoss = new Mp4Angriff();
            yield return Mp4Schicke(b, "projectile", zielId, wh, lebendGeschoss);
            yield return new WaitForSecondsRealtime(3.2f);
            Info($"Lebend: Nahkampf {lebendNah.Text} · Schuss {lebendSchuss.Text} · Geschoss {lebendGeschoss.Text}");

            // ---------------------------------------------------------- tot (gehalten, damit die Wiederbelebung nicht dazwischenkommt)
            Func<NetworkPlayer, string> vorher = NetworkPlayer.HoldRespawn;
            NetworkPlayer.HoldRespawn = p => ReferenceEquals(p, np) ? Mp4Halten : vorher?.Invoke(p);
            var totNah = new Mp4Angriff();
            var totSchuss = new Mp4Angriff();
            var totGeschoss = new Mp4Angriff();
            var wiederGeschoss = new Mp4Angriff();
            try
            {
                UiSetze(bh, "_invulnerableUntil", -1f);
                bh.ApplyDamage(new DamageInfo { Amount = bh.Max * 20f, Direction = -b.transform.forward, Unblockable = true });
                yield return WaitUntil(() => !bh.IsAlive && np.ServerHeldDown, 3f);
                if (!Mp4Voraus(!bh.IsAlive && np.ServerHeldDown, $"B gefallen und liegt ({bh.Current:0}/{bh.Max:0})")) yield break;
                wh.Heal(wh.Max);
                yield return Mp4Schicke(b, "melee", zielId, wh, totNah);
                yield return Mp4Schicke(b, "shot", zielId, wh, totSchuss);
                yield return new WaitForSecondsRealtime(3.2f);
                yield return Mp4Schicke(b, "projectile", zielId, wh, totGeschoss);
                Info($"Tot: Nahkampf {totNah.Text} · Schuss {totSchuss.Text} · Geschoss {totGeschoss.Text}");
            }
            finally
            {
                NetworkPlayer.HoldRespawn = vorher;
                if (bh != null && !bh.IsAlive && np != null) np.ServerReviveHere(1f);
            }
            yield return new WaitForSecondsRealtime(3.2f);
            yield return Mp4Schicke(b, "projectile", zielId, wh, wiederGeschoss);
            Info($"Nach dem Erwachen (Gegenprobe): Geschoss {wiederGeschoss.Text}");

            // Rot mit dem alten Spiel: ValidateCommon prüfte nicht, ob der Angreifer lebt – die tote Bitte wäre angenommen worden
            // (ServerLastAttackTime neu, keine Ablehnung, kein „verworfen (tot)“, beim Geschoss ein Pfeil weniger).
            bool logErwartet = nc.ServerRejected <= 5;   // NetworkCombat.Reject schreibt nur die ersten 5 Ablehnungen ins Log
            Check(lebendNah.Angenommen && totNah.Verworfen && (!logErwartet || totNah.TotLog == 1),
                $"Nahkampf: lebend angenommen, tot verworfen (Ablehnung +1, kein Angriff gezählt, Wolf {totNah.Leben:+0;-0} Leben, Log „verworfen (tot)“ {totNah.TotLog}×) | alt: tot angenommen");
            Check(lebendGeschoss.Angenommen && lebendGeschoss.Pfeile == -1 && totGeschoss.Verworfen && totGeschoss.Pfeile == 0 && (!logErwartet || totGeschoss.TotLog == 1),
                $"Geschoss (Pfeil): lebend angenommen ({lebendGeschoss.Pfeile} Pfeil), tot verworfen ({totGeschoss.Pfeile} Pfeile, Log {totGeschoss.TotLog}×) | alt: tot angenommen, Pfeil verbraucht");
            // 22.10.2026 (Nachbesserung Runde 4): Der Schuss-Weg nimmt den Bogen nicht mehr an (KAMPF-SCHUSS unten) – den Tod prüft
            // ValidateCommon für alle drei Wege gleich, über den Schuss-Weg aber nur mit einer Feuerwaffe nachweisbar.
            Sp3NichtGeprueft("MEHRSPIELER-03", $"Schuss (SubmitShot) im Tod – braucht eine Feuerwaffe (Feuerwaffen ruhen); B's Bogen wird auf diesem Weg schon wegen des Waffentyps verworfen (tot: {totSchuss.Text})");
            if (!logErwartet) Info($"Log-Zeilen nicht prüfbar: B hatte schon {nc.ServerRejected} Ablehnungen (Log nur für die ersten 5)");
            if (!wiederGeschoss.Angenommen) Info("Hinweis: nach dem Erwachen wurde das Geschoss NICHT angenommen – " + wiederGeschoss.Text);

            // ---------------------------------------------------------- KAMPF-SCHUSS (22.10.2026, Abnahme Runde 4)
            // Rot mit dem alten Spiel: ServerShot prüfte den Waffentyp nicht – der Bogen über den Schuss-Weg wurde als Sofortschuss
            // angenommen (Lauf 22.10. 14:36: „Schuss angenommen … Ziel -61, Pfeile +0“), Schaden ohne Pfeil und ohne Flugbahn.
            // Gegenprobe im selben Check: derselbe Bogen über den Geschoss-Weg (so schießt der echte Client) wird angenommen, −1 Pfeil.
            Sp3Punkt("KAMPF-SCHUSS", "B (lebend, Bogen) schickt denselben Angriff über SubmitShot (Sofortschuss) und über SubmitProjectile (Geschoss)");
            Check(lebendSchuss.Verworfen && lebendSchuss.Pfeile == 0 && (!logErwartet || lebendSchuss.WaffentypLog == 1) &&
                  lebendGeschoss.Angenommen && lebendGeschoss.Pfeile == -1,
                $"Bogen über den Schuss-Weg verworfen (Ablehnung, kein Angriff gezählt, Ziel {lebendSchuss.Leben:+0;-0}, Pfeile {lebendSchuss.Pfeile:+0;-0}, " +
                $"Log „verworfen (Waffentyp …)“ {lebendSchuss.WaffentypLog}×); über den Geschoss-Weg angenommen (Pfeile {lebendGeschoss.Pfeile:+0;-0}, Ziel {lebendGeschoss.Leben:+0;-0}) " +
                "| alt: Schuss-Weg angenommen – Schaden ohne Pfeil und ohne Flugbahn");
            Info($"Tot über den Schuss-Weg: {totSchuss.Text}; Log „Waffentyp“ {totSchuss.WaffentypLog}×");
            if (wolf != null) EnemyDirector.Instance.Remove(wolf);
            b.GetComponent<Health>()?.SetInvulnerableFor(900f);   // für Teil 2 (Leiche weit draußen)
        }

        /// <summary>Eine Angriffsbitte über B's Client schicken und die Antwort des Servers lesen.</summary>
        private IEnumerator Mp4Schicke(NetworkInventory b, string art, int zielId, Health ziel, Mp4Angriff e)
        {
            var nc = b.GetComponent<NetworkCombat>();
            int rej0 = nc.ServerRejected, pf0 = b.ServerModel.AmmoCount, log0 = _mp4TotLog, wlog0 = _mp4WaffentypLog;
            float last0 = nc.ServerLastAttackTime, hp0 = ziel != null ? ziel.Current : 0f;
            yield return Ask(b, "angriff-direkt", art + " " + zielId);
            e.Gesendet = _last.ok;
            string antwort = _last.data;
            yield return new WaitForSecondsRealtime(0.9f);   // Bitte unterwegs, Geschoss im Flug
            bool gezaehlt = nc.ServerLastAttackTime > last0 + 0.001f;
            e.Angenommen = e.Gesendet && gezaehlt && nc.ServerRejected == rej0;
            e.Leben = ziel != null ? ziel.Current - hp0 : 0f;
            e.Pfeile = b.ServerModel.AmmoCount - pf0;
            e.TotLog = _mp4TotLog - log0;
            e.WaffentypLog = _mp4WaffentypLog - wlog0;
            e.Verworfen = e.Gesendet && !gezaehlt && nc.ServerRejected == rej0 + 1 && e.Leben > -0.01f;
            e.Text = $"{(e.Angenommen ? "angenommen" : e.Verworfen ? "verworfen" : "UNKLAR")} (Antwort „{antwort}“, Ablehnungen +{nc.ServerRejected - rej0}, " +
                     $"Angriff gezählt {(gezaehlt ? "ja" : "nein")}, Ziel {e.Leben:+0;-0}, Pfeile {e.Pfeile:+0;-0})";
        }

        private void Mp4Log(string message, string stackTrace, LogType type)
        {
            if (_mp4TotMuster != null && message != null && message.Contains(_mp4TotMuster)) _mp4TotLog++;
            if (_mp4WaffentypMuster != null && message != null && message.Contains(_mp4WaffentypMuster)) _mp4WaffentypLog++;   // 22.10.2026 (KAMPF-SCHUSS)
        }

        // ================================================================== 2. MEHRSPIELER-01: Leiche außer Sicht und zurück, Wiederverbinden

        private IEnumerator Mp4Leiche(Vector3 lager)
        {
            Line("");
            Line("[2 MEHRSPIELER-01: eigene Leiche weit draußen – außer Sicht und zurück, Wiederverbinden (B Beute, C Nachlese)]");
            Sp3Punkt("MEHRSPIELER-01", "Leiche ≥ 650 m vom Spawn; B und C zum Wachtfeuer (außer Sicht) und zurück; danach beide neu verbunden (Anmeldung am Wachtfeuer) und hin");
            NetworkInventory b = _mp4B, c = _mp4C;
            var cp = c.GetComponent<NetworkProgression>();
            string warum = cp != null ? cp.ServerUnlockSalvage() : "kein Fortschritt";
            yield return new WaitForSecondsRealtime(0.5f);
            bool nachlese = Mp4Voraus(warum == "" && cp.ServerSalvageUnlocked, $"C ({c.ServerModel.Class?.DisplayName}) hat die Nachlese erlernt{(warum != "" ? " – " + warum : "")}");
            EnemySpawnArea vorlage = Mp4Vorlage("enemy.salt_brute");
            if (!Mp4Voraus(vorlage != null, "Grubenschrat-Vorlage (Gegner mit Nachlese-Profil)")) yield break;
            Vector3? fern = Mp4FernerOrt(lager, 650f, out string ortInfo);
            if (!Mp4Voraus(fern.HasValue, "Ort weit vom Spawn auf dem NavMesh, außerhalb der Weltzellen und Instanzen: " + ortInfo)) yield break;
            Vector3 f = fern.Value;

            EnemyBrain brute = EnemyDirector.Instance.SpawnAt(vorlage.Prefab, f, 0f, 13, "mp4-leiche", br => br.SetAggroProfile(AggroProfile.Passive), 1500f);
            if (!Mp4Voraus(brute != null, "Grubenschrat am fernen Ort erzeugt")) yield break;
            yield return new WaitForSecondsRealtime(0.5f);
            brute.ServerStun(60f);
            Vector3 g = brute.transform.position;
            Vector3 standB = Gnd(g.x - 2.6f, g.z), standC = Gnd(g.x, g.z - 2.6f);
            float yawB = YawTo(standB, g), yawC = YawTo(standC, g);
            yield return Ask(b, "goto", V(standB, yawB));
            yield return Ask(c, "goto", V(standC, yawC));
            yield return new WaitForSecondsRealtime(2f);
            NetworkObject nob = brute.GetComponent<NetworkObject>();
            int id = nob.ObjectId;
            // C trägt genug bei (Nachlese braucht einen Mindestanteil), B gibt den Todesstoß – beide beuteberechtigt
            brute.Health.ApplyDamage(new DamageInfo { Amount = brute.Health.Max * 0.12f, Source = c.gameObject, WeaponId = "test" });
            brute.Health.ApplyDamage(new DamageInfo { Amount = brute.Health.Max * 3f, Source = b.gameObject, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(1.5f);
            CorpseLoot leiche = brute.GetComponent<CorpseLoot>();
            if (!Mp4Voraus(!brute.Health.IsAlive && leiche != null, "Grubenschrat besiegt, Leiche liegt")) yield break;
            if (!leiche.HasLootFor(Mp4B))
            {
                leiche.AddBonus(new[] { Mp4B }, Sp3Junk, 1);
                b.NotifyLootAvailable(nob);
                Info("Testhilfe: Beutewurf für B leer – eine Zerbrochene Taschenuhr ergänzt (Glimmen über NotifyLootAvailable wie beim Tod)");
            }
            nachlese &= Mp4Voraus(leiche.HasSalvageFor(Mp4C), $"Nachlese-Fund für C ({cp?.ServerSalvageNote})");
            if (!nachlese) Sp3NichtGeprueft("MEHRSPIELER-01", "Nachlese-Glimmen (C ohne Fund – siehe Voraussetzung)");
            Info("Sichtweite der Leiche (Sicht_Gegner): " + Mp4Sichtweite(nob));

            yield return Ask(b, "leiche", id.ToString());
            string b0 = _last.data;
            yield return Ask(c, "leiche", id.ToString());
            string c0 = _last.data;
            Info($"Ausgangslage: B „{b0}“, C „{c0}“");
            bool startB = b0.Contains("found=1") && b0.Contains("loot=1"), startC = c0.Contains("found=1") && c0.Contains("salvage=1");

            // ---------------------------------------------------------- außer Sicht (Wachtfeuer) und zurück
            yield return Ask(b, "goto", V(lager + new Vector3(3f, 0.3f, 3f), 0f));
            yield return Ask(c, "goto", V(lager + new Vector3(-3f, 0.3f, 3f), 0f));
            yield return WaitUntil(() => nob != null && !nob.Observers.Contains(b.Owner) && !nob.Observers.Contains(c.Owner), 30f);
            bool ausSichtServer = nob != null && !nob.Observers.Contains(b.Owner) && !nob.Observers.Contains(c.Owner);
            float abstand = Flat(b.transform.position, g);
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "leiche", id.ToString());
            string b1 = _last.data;
            yield return Ask(c, "leiche", id.ToString());
            string c1 = _last.data;
            Info($"Am Wachtfeuer ({abstand:0} m von der Leiche): Server-Sicht ohne B und C {(ausSichtServer ? "ja" : "NEIN")}; B „{b1}“, C „{c1}“");
            yield return Ask(b, "goto", V(standB, yawB));
            yield return Ask(c, "goto", V(standC, yawC));
            yield return WaitUntil(() => nob != null && nob.Observers.Contains(b.Owner) && nob.Observers.Contains(c.Owner), 30f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(b, "leiche", id.ToString());
            string b2 = _last.data;
            yield return Ask(c, "leiche", id.ToString());
            string c2 = _last.data;
            // Rot mit dem alten Spiel: die neue Kopie bekam kein Glimmen (reiner Client-Zustand; der Server meldete beim Wiedereintritt nichts)
            Check(startB && ausSichtServer && b1.Contains("found=0") && b2.Contains("found=1") && Mp4Inst(b2) != Mp4Inst(b0) && b2.Contains("loot=1"),
                $"B: Leiche außer Sicht ({abstand:0} m, Client-Kopie zerstört: „{b1}“), zurück → neue Kopie (Instanz {Mp4Inst(b0)} → {Mp4Inst(b2)}) glimmt golden „{b2}“ | alt: neue Kopie loot=0");
            if (nachlese)
                Check(startC && ausSichtServer && c1.Contains("found=0") && c2.Contains("found=1") && Mp4Inst(c2) != Mp4Inst(c0) && c2.Contains("salvage=1"),
                    $"C (Nachlese): Leiche außer Sicht („{c1}“), zurück → neue Kopie (Instanz {Mp4Inst(c0)} → {Mp4Inst(c2)}) mit grünem Bergen-Glimmen „{c2}“ | alt: neue Kopie salvage=0");

            // ---------------------------------------------------------- Wiederverbinden fern der Leiche, dann hin
            Sp3Punkt("MEHRSPIELER-01", "Wiederverbinden: B und C trennen an der Leiche, melden sich am Wachtfeuer neu an (Leiche dort nicht in Sicht) und gehen hin");
            NetworkInventory bAlt = b, cAlt = c;
            yield return Ask(b, "reconnect");
            yield return Ask(c, "reconnect");
            NetworkInventory bNeu = null, cNeu = null;
            yield return WaitUntil(() => (bNeu = OnlineInv(Mp4B)) != null && bNeu != bAlt && (cNeu = OnlineInv(Mp4C)) != null && cNeu != cAlt, 90f);
            if (!Mp4Voraus(bNeu != null && bNeu != bAlt && cNeu != null && cNeu != cAlt, "B und C wieder angemeldet")) { EnemyDirector.Instance.Remove(brute); yield break; }
            _mp4B = b = bNeu;
            _mp4C = c = cNeu;
            b.GetComponent<Health>()?.SetInvulnerableFor(900f);
            c.GetComponent<Health>()?.SetInvulnerableFor(7200f);
            yield return new WaitForSecondsRealtime(4f);
            bool fernBeimAnmelden = nob != null && !nob.Observers.Contains(b.Owner) && !nob.Observers.Contains(c.Owner) &&
                                    Flat(b.transform.position, g) > 560f && Flat(c.transform.position, g) > 560f;
            yield return Ask(b, "leiche", id.ToString());
            string b3 = _last.data;
            yield return Ask(c, "leiche", id.ToString());
            string c3 = _last.data;
            Info($"Nach der Anmeldung: B bei {b.transform.position.x:0}/{b.transform.position.z:0} ({Flat(b.transform.position, g):0} m), C {Flat(c.transform.position, g):0} m – Leiche nicht in Sicht {(fernBeimAnmelden ? "ja" : "NEIN")}; B „{b3}“, C „{c3}“");
            yield return Ask(b, "goto", V(standB, yawB));
            yield return Ask(c, "goto", V(standC, yawC));
            yield return WaitUntil(() => nob != null && nob.Observers.Contains(b.Owner) && nob.Observers.Contains(c.Owner), 30f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(b, "leiche", id.ToString());
            string b4 = _last.data;
            yield return Ask(c, "leiche", id.ToString());
            string c4 = _last.data;
            // Rot mit dem alten Spiel: Beim Login schickten NotifyAllCorpses/SalvageLogin das Glimmen für eine Leiche, die der neue Client
            // noch nicht hatte (kam als null an) – und beim späteren Erscheinen meldete niemand etwas.
            Check(fernBeimAnmelden && b3.Contains("found=0") && b4.Contains("found=1") && b4.Contains("loot=1"),
                $"B nach Wiederverbinden: Leiche erscheint erst beim Hingehen und glimmt golden „{b4}“ | alt: loot=0 (Login-Meldung ging ins Leere)");
            if (nachlese)
                Check(fernBeimAnmelden && c3.Contains("found=0") && c4.Contains("found=1") && c4.Contains("salvage=1"),
                    $"C nach Wiederverbinden: grünes Bergen-Glimmen „{c4}“ | alt: salvage=0");
            yield return Ask(b, "loot-open", id.ToString());
            Info($"B öffnet die Beute über den Agenten (Server-Weg, ohne Glimmen-Bedingung – nur Info): {(_last.ok ? "offen" : "nicht offen")} „{_last.data}“");
            EnemyDirector.Instance.Remove(brute);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        // ================================================================== 3. MEHRSPIELER-02 / EINSTIEG-04: im Tod getrennt

        private sealed class Mp4Tot
        {
            public string Abbruch;
            public string TrennInfo = "";   // 22.10.2026 (Nachbesserung): Zustand am Server im Augenblick des Trennens
            public bool LebendGespeichert, TotBeimTrennen, LebtDanach, ClientPos;
            public Vector3 Sterbeort, Server, Client;
            public WorldPosition Geladen;
        }

        private IEnumerator Mp4TotGetrennt(string exe)
        {
            Line("");
            Line("[3 EINSTIEG-04 / MEHRSPIELER-02: B lebt in einer Weltzelle, stirbt, trennt im Tod und meldet sich neu an]");
            Sp3Punkt("MEHRSPIELER-02", "Ausgangslage");
            WorldStreamer ws = WorldStreamer.Instance;
            GameObject spawn = GameObject.Find("PlayerSpawn");
            Vector3 lager = spawn != null ? spawn.transform.position : Vector3.zero;
            if (!Mp4Voraus(ws != null && ws.Manifest != null && BindPoint.Current == null && CharacterStore.RestoresPosition(Mp4B),
                    $"Sandbox mit Weltzellen-Streamer, ohne Bindepunkt (Wiederbelebung am Spawn = Wachtfeuer); B ({Mp4B}) stellt seine Position wieder her")) yield break;
            Vector2 xw = BorderMountainsFrame.ToWorld(832f, 196f);
            yield return Preload(ws, new Vector3(xw.x, 0f, xw.y), 40f);
            Vector3 x = GG(832f, 196f);
            WorldCellDefinition zelle = ws.Manifest.CellAt(x, out _);
            if (!Mp4Voraus(zelle != null, $"Sterbeort X {x.x:0}/{x.z:0} (Lagerplatz Grenzgebirge) liegt in der Weltzelle {zelle?.cellId}")) yield break;

            // ---------------------------------------------------------- (a) Sandbox ohne Bindepunkt, Verbindung neu
            Sp3Punkt("MEHRSPIELER-02", "(a) ohne Bindepunkt: B lebt bei X (Speicherstand dort), stirbt, Verbindung getrennt und neu (wie Netzabbruch)");
            var ea = new Mp4Tot();
            yield return Mp4StirbUndKommWieder(ws, x, zelle.cellId, null, false, exe, ea);
            if (ea.Abbruch != null) Mp4Voraus(false, "(a) " + ea.Abbruch);
            else
            {
                float zumLager = Flat(ea.Server, lager), zumOrt = Flat(ea.Server, ea.Sterbeort), clientLager = Flat(ea.Client, lager);
                // Rot mit dem alten Spiel: CapturePosition kehrte im Tod sofort zurück – die Lebend-Stelle X blieb im Speicherstand,
                // RestorePosition setzte B nach der Anmeldung an den Sterbeort.
                Check(ea.LebendGespeichert && ea.TotBeimTrennen && !ea.Geladen.IsSet,
                    $"Speicherstand nach dem Trennen im Tod: {(ea.Geladen.IsSet ? $"Stelle {ea.Geladen.cellId} {ea.Geladen.localPosition.x:0}/{ea.Geladen.localPosition.z:0}" : "keine Stelle (Start am Spawn)")} " +
                    $"(vorher lebend bei X gespeichert {(ea.LebendGespeichert ? "ja" : "NEIN")}, tot beim Trennen (am Server im Augenblick des Trennens) {(ea.TotBeimTrennen ? "ja" : "NEIN")}) | alt: Stelle X blieb");
                Check(ea.LebtDanach && zumLager < 12f && zumOrt > 100f && ea.ClientPos && clientLager < 12f,
                    $"B nach der Neuanmeldung am Wachtfeuer: Server {zumLager:0.0} m vom Spawn, {zumOrt:0} m vom Sterbeort; eigener Client {clientLager:0.0} m vom Spawn; lebt {(ea.LebtDanach ? "ja" : "NEIN")} | alt: lebend am Sterbeort");
            }

            // ---------------------------------------------------------- (b) Bindepunkt in einer Weltzelle (wie Kareth v4), Spiel beendet
            Sp3Punkt("MEHRSPIELER-02", "(b) mit Bindepunkt in einer Weltzelle (wie das Wachtfeuer in Kareth v4): B lebt bei X, stirbt, beendet das Spiel, startet neu");
            Vector3 wb = GG(680f, 62f);
            WorldCellDefinition zelleW = ws.Manifest.CellAt(wb, out _);
            if (!Mp4Voraus(zelleW != null && Flat(wb, x) > 60f, $"Test-Bindepunkt W {wb.x:0}/{wb.z:0} (Talpass-Eingang) in Weltzelle {zelleW?.cellId}, {Flat(wb, x):0} m von X")) yield break;
            _mp4Bindepunkt = new GameObject("MP4 Test-Bindepunkt (Wachtfeuer in einer Weltzelle)");
            _mp4Bindepunkt.transform.SetPositionAndRotation(wb, Quaternion.Euler(0f, 135f, 0f));
            BindPoint bind = _mp4Bindepunkt.AddComponent<BindPoint>();
            var eb = new Mp4Tot();
            try
            {
                if (!Mp4Voraus(BindPoint.Current == bind, "Test-Bindepunkt aktiv (BindPoint.Current)")) yield break;
                yield return Mp4StirbUndKommWieder(ws, x, zelle.cellId, wb, true, exe, eb);
            }
            finally
            {
                Destroy(_mp4Bindepunkt);
                _mp4Bindepunkt = null;
            }
            yield return null;
            Info($"Test-Bindepunkt wieder entfernt (BindPoint.Current {(BindPoint.Current == null ? "leer" : "NOCH GESETZT")})");
            if (eb.Abbruch != null) { Mp4Voraus(false, "(b) " + eb.Abbruch); yield break; }
            float zuW = Flat(eb.Server, wb), zuOrt = Flat(eb.Server, eb.Sterbeort), clientW = Flat(eb.Client, wb);
            // Rot mit dem alten Spiel: die Lebend-Stelle X blieb stehen (Rückkehr im Tod), RestorePosition setzte B an den Sterbeort.
            Check(eb.LebendGespeichert && eb.TotBeimTrennen && eb.Geladen.IsSet && eb.Geladen.cellId == zelleW.cellId && Flat(eb.Geladen.localPosition, wb) < 2f,
                $"Speicherstand nach Spielende im Tod: {(eb.Geladen.IsSet ? $"{eb.Geladen.cellId} {eb.Geladen.localPosition.x:0}/{eb.Geladen.localPosition.z:0} – {Flat(eb.Geladen.localPosition, wb):0.0} m vom Bindepunkt" : "keine Stelle")} " +
                $"(lebend bei X gespeichert {(eb.LebendGespeichert ? "ja" : "NEIN")}, tot beim Beenden (am Server im Augenblick des Trennens) {(eb.TotBeimTrennen ? "ja" : "NEIN")}) | alt: Stelle X");
            Check(eb.LebtDanach && zuW < 3f && zuOrt > 40f && eb.ClientPos && clientW < 3f,
                $"B nach Neustart am Bindepunkt: Server {zuW:0.0} m vom Bindepunkt, {zuOrt:0} m vom Sterbeort; eigener Client {clientW:0.0} m vom Bindepunkt | alt: am Sterbeort");
        }

        /// <summary>
        /// B lebt bei <paramref name="x"/> (Zelle geladen, Speicherstand dort), stirbt (die Suite hält ihn liegen), trennt im Tod
        /// (reconnect) bzw. beendet das Spiel (quit + neuer Start des Test-Clients) und meldet sich neu an.
        /// </summary>
        private IEnumerator Mp4StirbUndKommWieder(WorldStreamer ws, Vector3 x, string zelle, Vector3? bind, bool beenden, string exe, Mp4Tot e)
        {
            NetworkInventory b = _mp4B;
            var bh = b.GetComponent<Health>();
            var np = b.GetComponent<NetworkPlayer>();
            yield return Preload(ws, x, 30f);
            bh.SetInvulnerableFor(90f);   // Gegner im Grenzgebirge sollen B nicht vor der Probe umwerfen
            yield return Ask(b, "goto", V(x, 45f));
            yield return WaitClientCell(b, zelle, "Ready", 25f);
            yield return new WaitForSecondsRealtime(2f);
            b.SaveNow();
            WorldPosition lebend = b.SavedPosition;
            e.LebendGespeichert = lebend.IsSet && lebend.cellId == zelle && Flat(lebend.localPosition, b.transform.position) < 3f;
            Info($"Lebend gespeichert: {(lebend.IsSet ? $"{lebend.cellId} {lebend.localPosition.x:0}/{lebend.localPosition.z:0}" : "keine Stelle")} (B steht bei {b.transform.position.x:0}/{b.transform.position.z:0})");

            Func<NetworkPlayer, string> vorher = NetworkPlayer.HoldRespawn;
            NetworkPlayer.HoldRespawn = p => ReferenceEquals(p, np) ? Mp4Halten : vorher?.Invoke(p);
            NetworkInventory alt = b;
            // 22.10.2026 (Nachbesserung Runde 4, Abnahme-Mangel 1): „tot beim Trennen“ wird jetzt IM AUGENBLICK DES TRENNENS am Server
            // gemessen. FishNet meldet ServerManager.OnRemoteConnectionState(Stopped) unmittelbar VOR Objects.ClientDisconnected
            // (Despawn → NetworkInventory.OnStopServer → SaveNow → CapturePosition) – genau den Zustand, mit dem der Speicherstand
            // beim Trennen geschrieben wird. Vorher tastete die Suite erst NACH der Antwort auf „reconnect“/„quit“ ab: kam die
            // Abmeldung vor der Antwort, brach schon die erste Runde mit totZuletzt=false ab („tot beim Trennen NEIN“ in allen Läufen),
            // obwohl der Speicherstand den Tot-Zweig zeigte; beim Beenden (b) kam der Unity-null-Vergleich unten dazu. Mit dem alten Fehlverhalten (Lebend-Stelle X bleibt, Neuanmeldung am
            // Sterbeort) bleibt die Prüfung rot: dort ist der Speicherstand X gesetzt (Check 1) und B steht am Sterbeort (Check 2).
            FishNet.Managing.Server.ServerManager sm = FishNet.InstanceFinder.ServerManager;
            int bVerbindung = b.Owner != null ? b.Owner.ClientId : -1;
            bool gemessen = false, totGemessen = false, liegtGemessen = false;
            float lebenGemessen = -1f, stoppedNach = -1f;
            Action<FishNet.Connection.NetworkConnection, FishNet.Transporting.RemoteConnectionStateArgs> beimTrennen = (conn, args) =>
            {
                if (gemessen || args.ConnectionState != FishNet.Transporting.RemoteConnectionState.Stopped || conn == null || conn.ClientId != bVerbindung) return;
                gemessen = true;
                totGemessen = !bh.IsAlive;             // Objekt noch nicht despawnt (Despawn folgt im selben Aufruf)
                liegtGemessen = np.ServerHeldDown;
                lebenGemessen = bh.Current;
            };
            if (sm != null) sm.OnRemoteConnectionState += beimTrennen;
            try
            {
                UiSetze(bh, "_invulnerableUntil", -1f);
                bh.ApplyDamage(new DamageInfo { Amount = bh.Max * 20f, Direction = -b.transform.forward, Unblockable = true });
                yield return WaitUntil(() => !bh.IsAlive && np.ServerHeldDown, 3f);
                if (bh.IsAlive) { e.Abbruch = "B ist nicht gefallen"; yield break; }
                e.Sterbeort = b.transform.position;
                bool totVorher = !bh.IsAlive && np.ServerHeldDown;   // nur Info: Zustand beim Absenden der Bitte
                yield return Ask(b, beenden ? "quit" : "reconnect");
                // 22.10.2026 (Nachbesserung, Läufe r4n_mp4_1–3): Beim Beenden („quit“) erkannte die Schleife die Trennung NIE – B war laut
                // Diagnose nach 0,5 s getrennt und sein Objekt zerstört, aber „OnlineInv(Mp4B) != alt“ verglich null mit dem ZERSTÖRTEN
                // Objekt, und Unitys ==/!= halten ein zerstörtes Objekt für null (gleich → nie „weg“). Beim Wiederverbinden fiel das
                // nicht auf, weil dort ein neues Objekt kommt. Jetzt ReferenceEquals. Die Diagnosezeile bleibt im Bericht.
                float tBitte = Time.realtimeSinceStartup;
                DateTime bitteUtc = DateTime.UtcNow;
                float grenze = 60f;
                bool weg = false;
                // Diagnose (22.10.2026): wann verschwinden Verbindung (ServerManager.Clients) und Objekt, wann kommt „Stopped“?
                float verbindungWegNach = -1f, objektWegNach = -1f;
                while (Time.realtimeSinceStartup - tBitte < grenze)
                {
                    float jetzt = Time.realtimeSinceStartup - tBitte;
                    if (verbindungWegNach < 0f && (sm == null || !sm.Clients.ContainsKey(bVerbindung))) verbindungWegNach = jetzt;
                    if (objektWegNach < 0f && alt == null) objektWegNach = jetzt;
                    if (gemessen && stoppedNach < 0f) stoppedNach = jetzt;
                    if (!ReferenceEquals(OnlineInv(Mp4B), alt)) { weg = true; break; }   // nicht „!=“: zerstört == null bei Unity
                    yield return new WaitForSecondsRealtime(0.05f);
                }
                float bisWeg = Time.realtimeSinceStartup - tBitte;
                e.TotBeimTrennen = weg && gemessen && totGemessen;
                e.TrennInfo = !weg ? $"B blieb angemeldet ({grenze:0} s)"
                    : !gemessen ? $"Trennen am Server nicht beobachtet (Verbindung {bVerbindung}, ServerManager {(sm != null ? "da" : "FEHLT")})"
                    : $"am Server beim Trennen (vor dem Abbau): {(totGemessen ? "tot" : "LEBEND")}, Leben {lebenGemessen:0}, liegt gehalten {(liegtGemessen ? "ja" : "nein")}; beim Absenden tot {(totVorher ? "ja" : "nein")}";
                e.TrennInfo += $"; abgemeldet {bisWeg:0.0} s nach der Antwort auf „{(beenden ? "quit" : "reconnect")}“";
                e.TrennInfo += $"; Diagnose: Stopped-Ereignis {(gemessen ? $"nach ≤{Mathf.Max(0f, stoppedNach):0.0} s" : "NICHT gesehen")}, Verbindung aus ServerManager.Clients " +
                               $"{(verbindungWegNach >= 0f ? $"nach {verbindungWegNach:0.0} s" : "nie")}, Objekt zerstört {(objektWegNach >= 0f ? $"nach {objektWegNach:0.0} s" : "nein")}" +
                               (!weg && alt != null ? $", B-Objekt: IsReady {alt.IsReady}, Owner aktiv {(alt.Owner != null && alt.Owner.IsActive)}, Owner {alt.Owner?.ClientId}, lebt {bh.IsAlive}" : "");
                if (beenden) e.TrennInfo += "; " + Mp4ProzessBInfo(bitteUtc);
                Info("Trennen: " + e.TrennInfo);
            }
            finally
            {
                if (sm != null) sm.OnRemoteConnectionState -= beimTrennen;
                NetworkPlayer.HoldRespawn = vorher;
            }
            if (beenden)
            {
                float tFrei = Time.realtimeSinceStartup;
                bool nochDa = OnlineInv(Mp4B) != null;
                yield return WaitUntil(() => OnlineInv(Mp4B) == null, 20f);
                if (nochDa) Info($"Diagnose: nach dem Freigeben des Liegenhaltens war B {(OnlineInv(Mp4B) == null ? $"nach {Time.realtimeSinceStartup - tFrei:0.0} s abgemeldet" : "nach 20 s NOCH angemeldet")}");
                yield return new WaitForSecondsRealtime(2f);
                Mp4LogSichern(Mp4B, "_tot_beendet");   // 22.10.2026: der Neustart überschreibt das Log des beendeten B
                Mp4StarteClient(exe, Mp4B, Mp4KlasseB);
            }
            NetworkInventory neu = null;
            yield return WaitUntil(() => (neu = OnlineInv(Mp4B)) != null && neu != alt, beenden ? 150f : 60f);
            if (neu == null || neu == alt) { e.Abbruch = "B kam nicht wieder"; yield break; }
            _mp4B = neu;
            e.Geladen = neu.SavedPosition;   // so, wie der Speicherstand beim Anmelden geladen wurde (vor jedem neuen Speichern)
            // Wiederherstellung der Stelle: nur wenn eine Stelle gespeichert ist (dann lädt der Server die Zelle vor, bis 20 s)
            if (e.Geladen.IsSet)
            {
                Vector3 soll = e.Geladen.localPosition;
                yield return WaitUntil(() => neu != null && Flat(neu.transform.position, soll) < 3f, 40f);
            }
            yield return new WaitForSecondsRealtime(3f);
            e.Server = neu.transform.position;
            Health nh = neu.GetComponent<Health>();
            e.LebtDanach = nh != null && nh.IsAlive;
            yield return Ask(neu, "pos");
            e.ClientPos = _last.ok && TryPos(_last.data, out e.Client);
            Info($"Neu angemeldet ({(beenden ? "Spiel neu gestartet" : "Verbindung neu")}): Speicherstand {(e.Geladen.IsSet ? $"{e.Geladen.cellId} {e.Geladen.localPosition.x:0}/{e.Geladen.localPosition.z:0}" : "ohne Stelle")}; " +
                 $"Server {e.Server.x:0}/{e.Server.z:0}, Client {e.Client.x:0}/{e.Client.z:0}; Sterbeort {e.Sterbeort.x:0}/{e.Sterbeort.z:0}" +
                 (bind.HasValue ? $"; Bindepunkt {bind.Value.x:0}/{bind.Value.z:0}" : ""));
            neu.GetComponent<Health>()?.SetInvulnerableFor(900f);
        }

        // ================================================================== 4. MEHRSPIELER-04: Gruppenleitung

        private IEnumerator Mp4Gruppenleitung(NetworkInventory a)
        {
            Line("");
            Line("[4 MEHRSPIELER-04: Gruppenleitung beim Verlassen (A Leitung, B offline, C online)]");
            Sp3Punkt("MEHRSPIELER-04", "Gruppe A, B, C; B beendet das Spiel (Offline-Mitglied, älter als C); A verlässt die Gruppe über seinen Client (ServerLeave)");
            NetworkInventory b = _mp4B, c = _mp4C;
            string nA = a.CharacterName, nB = b.CharacterName, nC = c.CharacterName;
            if (!Mp4Voraus(nA != nB && nB != nC && nA != nC && PartyService.Of(Mp4A) == null && PartyService.Of(Mp4B) == null && PartyService.Of(Mp4C) == null,
                    $"Namen verschieden ({nA}, {nB}, {nC}), noch niemand in einer Gruppe")) yield break;
            yield return Ask(a, "party-invite", nB);
            yield return Ask(b, "party-respond", "1");
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Ask(a, "party-invite", nC);
            yield return Ask(c, "party-respond", "1");
            yield return new WaitForSecondsRealtime(0.6f);
            PartyService.Party p = PartyService.Of(Mp4A);
            if (!Mp4Voraus(p != null && p.Leader == Mp4A && p.Members.SequenceEqual(new[] { Mp4A, Mp4B, Mp4C }),
                    $"Gruppe: Leitung {p?.Leader}, Mitglieder {string.Join(", ", p?.Members ?? new List<string>())}")) yield break;
            yield return Ask(b, "quit");
            yield return WaitUntil(() => OnlineInv(Mp4B) == null, 30f);
            yield return new WaitForSecondsRealtime(1f);
            p = PartyService.Of(Mp4C);
            if (!Mp4Voraus(OnlineInv(Mp4B) == null && p != null && p.Leader == Mp4A && p.Members.Contains(Mp4B),
                    $"B hat das Spiel beendet, bleibt Gruppenmitglied (offline); Mitglieder {string.Join(", ", p?.Members ?? new List<string>())}")) yield break;
            yield return Ask(a, "party-leave");
            yield return new WaitForSecondsRealtime(0.8f);
            p = PartyService.Of(Mp4C);
            // Rot mit dem alten Spiel: PartyService.Leave gab die Leitung an Members[0] – das ist B (offline).
            Check(p != null && p.Leader == Mp4C && p.Members.SequenceEqual(new[] { Mp4B, Mp4C }) && PartyService.Of(Mp4A) == null,
                $"Server: A verlässt über seinen Client → Leitung „{p?.Leader}“ (Soll {Mp4C}, online; nicht {Mp4B}, offline), Mitglieder {string.Join(", ", p?.Members ?? new List<string>())} | alt: Leitung {Mp4B} (offline)");
            yield return Ask(c, "social");
            string sicht = (_last.data ?? "").Split('\n')[0];
            Check(_last.ok && sicht.Contains($"*{nC}(ich)") && sicht.Contains($"{nB}(off)") && !sicht.Contains($"*{nB}"),
                $"C's Client zeigt sich als Leitung, {nB} offline ohne Leitung („{sicht}“) | alt: „*{nB}(off)“");
            yield return Ask(c, "party-invite", nA);
            string einladung = _last.data ?? "";
            Check(einladung.Contains($"Einladung an {nA}"),
                $"C (neue Leitung) lädt A ein: {(einladung.Contains($"Einladung an {nA}") ? "geschickt" : "ABGELEHNT")} („{Mp4Letzte(einladung)}“) | alt: „Nur die Gruppenleitung kann einladen.“");
            yield return Ask(a, "party-respond", "0");
            yield return Ask(c, "party-leave");
            Sp3NichtGeprueft("MEHRSPIELER-04", "Verdrahtung ServerKick → PartyService.Leave(…, IsOnline) ohne beobachtbare Wirkung: entfernen darf nur die Leitung und nie sich selbst – beim Entfernen wechselt die Leitung nie");
        }

        // ================================================================== Hilfen

        private void Mp4StarteClient(string exe, string profil, string klasse)
        {
            // wie StartClient, zusätzlich mit Klassenwunsch für das frische Testprofil
            // 22.10.2026 (Teil 5): eigenes Log je Profil (-logFile), damit das Schließen über das Fenster geprüft werden kann
            string log = Mp4ClientLog(profil);
            Directory.CreateDirectory(Path.GetDirectoryName(log));
            var client = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"-ashveil-join 127.0.0.1 -ashveil-profile {profil} -ashveil-class {klasse} -ashveil-agent -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile \"{log}\"",
                WorkingDirectory = Path.GetDirectoryName(exe),
                UseShellExecute = false
            });
            if (client == null) throw new InvalidOperationException("Test-Client konnte nicht gestartet werden.");
            Clients.Add(client);
            if (profil == Mp4C) _mp4ProzessC = client;
            if (profil == Mp4B) _mp4ProzessB = client;   // 22.10.2026 (Nachbesserung): Prozessende beim Beenden im Tod
        }

        private System.Diagnostics.Process _mp4ProzessB;

        /// <summary>22.10.2026 (Nachbesserung): Ist B's Prozess nach „quit“ beendet – wann, mit welchem Exit-Code?</summary>
        private string Mp4ProzessBInfo(DateTime bitteUtc)
        {
            try
            {
                System.Diagnostics.Process p = _mp4ProzessB;
                if (p == null) return "B-Prozess unbekannt";
                p.Refresh();
                if (!p.HasExited) return "B-Prozess läuft NOCH";
                return $"B-Prozess beendet {(p.ExitTime.ToUniversalTime() - bitteUtc).TotalSeconds:0.0} s nach der Antwort, Exit-Code {p.ExitCode} (0x{p.ExitCode:X8})";
            }
            catch (Exception ex) { return "B-Prozess: " + ex.Message; }
        }

        /// <summary>22.10.2026 (Nachbesserung): Log eines Test-Clients vor dessen Neustart kopieren (gleicher -logFile-Pfad).</summary>
        private void Mp4LogSichern(string profil, string zusatz)
        {
            try
            {
                string log = Mp4ClientLog(profil);
                if (File.Exists(log)) File.WriteAllText(Path.Combine(Path.GetDirectoryName(log), profil + zusatz + ".log"), Mp4LogLesen(log));
            }
            catch (Exception ex) { Info("Log nicht gesichert: " + ex.Message); }
        }

        /// <summary>Voraussetzung: erfüllt → Info-Zeile (kein bestandener Check), nicht erfüllt → Fehler.</summary>
        private bool Mp4Voraus(bool ok, string was)
        {
            if (ok) Info("Voraussetzung: " + was);
            else Check(false, "Voraussetzung nicht erfüllt: " + was);
            return ok;
        }

        private static EnemySpawnArea Mp4Vorlage(string enemyId) => FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
            .FirstOrDefault(x => x.Definition != null && x.Definition.Id == enemyId && x.Prefab != null);

        /// <summary>Kennung der Client-Kopie aus der Antwort von „leiche“ (leer = keine Kopie).</summary>
        private static string Mp4Inst(string antwort)
        {
            int i = antwort?.IndexOf("inst=", StringComparison.Ordinal) ?? -1;
            return i >= 0 ? antwort.Substring(i + 5).Split(';')[0] : "";
        }

        private static string Mp4Letzte(string sozial)
        {
            string[] z = (sozial ?? "").Split('\n').Where(s => s.Trim().Length > 0).ToArray();
            return z.Length > 0 ? z[z.Length - 1] : "";
        }

        /// <summary>
        /// Ort ≥ <paramref name="mindestens"/> m vom Spawn: auf dem NavMesh, außerhalb der Weltzellen (kein Streaming nötig) und der
        /// Instanz-Plätze, möglichst weit weg von allen Gegnergebieten (weckt keine Gegner).
        /// </summary>
        private static Vector3? Mp4FernerOrt(Vector3 lager, float mindestens, out string info)
        {
            WorldStreamer ws = WorldStreamer.Instance;
            EnemySpawnArea[] gebiete = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None);
            Vector3? best = null;
            float bestFrei = float.MinValue;
            int geprueft = 0;
            for (float r = mindestens; r <= mindestens + 450f; r += 75f)
                for (int k = 0; k < 36; k++)
                {
                    float rad = k * 10f * Mathf.Deg2Rad;
                    Vector3 p = lager + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * r;
                    p = Gnd(p.x, p.z, 0f);
                    if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 6f, NavMesh.AllAreas)) continue;
                    p = hit.position;
                    if (Flat(p, lager) < mindestens) continue;
                    if (ws != null && ws.Manifest != null && ws.Manifest.CellAt(p, out _) != null) continue;
                    if (DungeonSlot.At(p) != null) continue;
                    geprueft++;
                    float frei = float.MaxValue;
                    foreach (EnemySpawnArea g in gebiete) if (g != null) frei = Mathf.Min(frei, Flat(g.transform.position, p) - g.Radius);
                    if (frei > bestFrei) { bestFrei = frei; best = p; }
                }
            float weck = EnemyDirector.Instance != null ? EnemyDirector.Instance.ActivateRange : 0f;
            info = best.HasValue
                ? $"{best.Value.x:0}/{best.Value.z:0}, {Flat(best.Value, lager):0} m vom Spawn, nächstes Gegnergebiet {bestFrei:0} m (Weckabstand {weck:0} m), {geprueft} Kandidaten"
                : $"kein Kandidat ({geprueft})";
            return best;
        }

        /// <summary>Sichtweite der Gegner-Sichtregel an diesem Objekt (nur Info: Prefab-Asset 500 m oder Laufzeit-Regel 180 m).</summary>
        private static string Mp4Sichtweite(NetworkObject nob)
        {
            try
            {
                var obs = nob != null ? nob.GetComponent<FishNet.Observing.NetworkObserver>() : null;
                FieldInfo liste = typeof(FishNet.Observing.NetworkObserver).GetField("_observerConditions", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var bed = liste?.GetValue(obs) as System.Collections.IList;
                if (bed == null) return "unbekannt";
                foreach (object o in bed)
                    if (o is EnemyInterestCondition eic)
                    {
                        FieldInfo d = typeof(EnemyInterestCondition).GetField("maximumDistance", BindingFlags.Instance | BindingFlags.NonPublic);
                        FieldInfo h = typeof(EnemyInterestCondition).GetField("hidePercent", BindingFlags.Instance | BindingFlags.NonPublic);
                        float dist = d != null ? (float)d.GetValue(eic) : -1f, hide = h != null ? (float)h.GetValue(eic) : 0f;
                        return $"{dist:0} m (Verschwinden ab {dist * (1f + hide):0} m, „{eic.name}“)";
                    }
                return "keine EnemyInterestCondition";
            }
            catch (Exception ex) { return "unbekannt (" + ex.Message + ")"; }
        }

        private void Mp4Zusammenfassung()
        {
            Sp3Punkt(null, null);
            if (_uiFehlendeHaken.Count > 0) Check(false, "Testhaken fehlen: " + string.Join(", ", _uiFehlendeHaken));
            Line("");
            Line("[PRÜFUNG je Befund]");
            foreach (string id in _sp3Reihenfolge)
            {
                Sp3Befund b = _sp3[id];
                string status = b.Fehler > 0 ? "FEHLGESCHLAGEN" : b.Ok == 0 ? "NICHT GEPRÜFT " : b.NichtGeprueft.Count > 0 ? "teilweise     " : "bestanden     ";
                Line($"  {status}  {id,-15} {b.Titel} ({b.Ok} ok, {b.Fehler} Fehler{(b.NichtGeprueft.Count > 0 ? $", {b.NichtGeprueft.Count}× nicht geprüft" : "")})");
            }
            Line("");
            Line("[Nicht geprüft (ehrlich)]");
            int n = 0;
            foreach (string id in _sp3Reihenfolge)
                foreach (string w in _sp3[id].NichtGeprueft) { Line($"  · {id}: {w}"); n++; }
            if (n == 0) Line("  · –");
        }
    }
}
