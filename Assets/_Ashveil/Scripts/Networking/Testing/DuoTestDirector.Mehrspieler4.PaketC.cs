using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// 23.10.2026 (Paket C + Kampfreaktionen + Wirken, Netzanteil der Spielablauf-Sitzung laut Codex-Detailplan
    /// REQ-20261022-CODEX-KAMPFREAKTIONEN-DETAILPLAN §5/§6 und Codex-Nachprüfung 1 „später Beobachter“): eigener Teil der Suite
    /// „spieltest mehrspieler4“ mit den ECHTEN Test-Clients B (Aschenjäger) und C (Aschenweber). Läuft nach Teil 1 (toter Angreifer).
    ///   KAMPF-REAKTION  Server trifft einen betäubten Aschwolf je Ergebnis einmal (Treffer, Ausgewichen, Schild voll/teilweise,
    ///                   Aschenschild voll/teilweise, Gift-Tick über DamageOverTime, Verfehlt über Health.RaiseMissed). Ausweichen,
    ///                   Block und Absorption werden am Ziel für die Probe fest eingestellt (Testobjekte statt Zufallswurf), sonst echter
    ///                   Trefferweg (Health.ApplyDamage → NetworkHealth.ObserversDamaged/-Missed). B muss jedes Ergebnis genau einmal
    ///                   empfangen, seine eigene Ableitung darf nicht abweichen, absorbierte Menge kommt an; der Host sieht je Treffer
    ///                   genau ein Damaged-Ereignis mit demselben Ergebnis und genau einen RPC (kein Doppel).
    ///   KAMPF-WIRKEN    C wirkt Heilung (2,5 s) auf B: B sieht Beginn und ausgelöstes Ende; zweites Wirken durch Bewegung
    ///                   unterbrochen, drittes durch C's Tod – B sieht jeweils ein nicht ausgelöstes Ende. Host empfängt je einmal.
    ///                   Den Animator-Parameter „Casting“ (Figuren-Sitzung) prüft die Suite mit, falls der Client-Build ihn hat.
    ///   PAKETC-LEICHE   Gegner am fernen Ort (≥ 650 m, C am Wachtfeuer außer Sicht) mit kritischem Hieb (Schwert/Axt) getötet → Server-
    ///                   Trennpunkt ≠ 0, B (beim Tod in Sicht) liest ihn; C kommt ERST DANACH in Sichtweite und liest ihn beim Spawn
    ///                   seiner Kopie (Ereignis genau einmal). Wiederbelebt → 0 bei C, erneut getötet → wieder ≠ 0 bei C (live), neuer
    ///                   Gegner (Wiederkehr = neues Objekt) beginnt mit 0.
    /// Neuer Testbefehl des Agenten: „paketc reaktion|wirken|leiche &lt;ObjectId&gt;“ (nur lesen, kein RPC). Die Darstellung (Haken der
    /// Figuren-Sitzung in EnemyVisual/Choreografie) prüft dieser Teil nicht – nur, dass Ergebnis und Zustand auf jedem Rechner ankommen.
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private const string PcReaktion = "KAMPF-REAKTION", PcWirken = "KAMPF-WIRKEN", PcLeiche = "PAKETC-LEICHE";

        /// <summary>Befunde dieses Teils anlegen (Aufruf in <see cref="Mehrspieler4Suite"/> bei den übrigen Sp3Anlegen).</summary>
        private void Mp4PaketCAnlegen()
        {
            Sp3Anlegen(PcReaktion, "Treffer-Ergebnis je Fall genau einmal beim echten Client B (Treffer, Ausgewichen, Block voll/teil, Absorb voll/teil, Tick, Verfehlt), Host ohne Doppel");
            Sp3Anlegen(PcWirken, "Wirken von C für Client B sichtbar: Beginn, ausgelöstes Ende, Unterbrechung durch Bewegung und Tod");
            Sp3Anlegen(PcLeiche, "Trennpunkt als Leichenzustand: später Beobachter C liest ihn beim Spawn, Wiederbeleben → 0, erneut töten → ≠ 0");
        }

        private IEnumerator Mp4PaketC(NetworkInventory a, Vector3 lager)
        {
            Line("");
            Line("[1b Paket C / Kampfreaktionen (23.10.2026): Treffer-Ergebnis, Wirken, Leichenzustand – echte Test-Clients B und C]");
            yield return PcReaktionen(a, lager);
            yield return PcWirkenProbe(a, lager);
            yield return PcLeichenzustand(lager);
        }

        // ================================================================== Testobjekte (fest eingestellte Ergebnisse am Ziel)

        private sealed class PcAusweicher : IDodger
        {
            public bool TryDodge(in DamageInfo info) => true;
        }

        private sealed class PcBlocker : IDamageBlocker
        {
            public float Anteil;
            public float Block(ref DamageInfo info, float amount)
            {
                float b = Anteil >= 1f ? amount : Mathf.Floor(amount * Anteil);
                info.Blocked = b;
                return amount - b;
            }
        }

        private sealed class PcAbsorber : IDamageAbsorber
        {
            public float Anteil;
            public float IncomingMultiplier => 1f;
            public float Absorb(ref DamageInfo info, float amount)
            {
                float a = Anteil >= 1f ? amount : Mathf.Floor(amount * Anteil);
                info.Absorbed += a;
                return amount - a;
            }
        }

        private sealed class PcFall
        {
            public TrefferReaktion Soll;
            public string Name, Alt;
            public object Dodger, Blocker, Absorber;
            public Action Ausloesen;
            public float Warten = 0.9f;
        }

        // ================================================================== KAMPF-REAKTION

        private IEnumerator PcReaktionen(NetworkInventory a, Vector3 lager)
        {
            Sp3Punkt(PcReaktion, "Aschwolf betäubt beim Wachtfeuer; A, B daneben; je Ergebnis ein Treffer vom Server, B liest seine Zähler");
            NetworkInventory b = _mp4B;
            EnemySpawnArea vorlage = Mp4Vorlage("enemy.ash_wolf");
            if (!Mp4Voraus(vorlage != null && EnemyDirector.Instance != null, "Aschwolf-Vorlage und Gegner-Regie")) yield break;
            EnemyBrain wolf = EnemyDirector.Instance.SpawnAt(vorlage.Prefab, Gnd(lager.x - 10f, lager.z + 16f), 180f, b.ServerModel.Level, "pc-reaktion",
                br => br.SetAggroProfile(AggroProfile.Passive), 20f);
            if (!Mp4Voraus(wolf != null, "Aschwolf als Ziel erzeugt (passiv)")) yield break;
            Health wh = wolf.GetComponent<Health>();
            NetworkHealth wnh = wolf.GetComponent<NetworkHealth>();
            NetworkObject nob = wolf.GetComponent<NetworkObject>();
            object dodgerAlt = UiFeld(wh, "_dodger"), blockerAlt = UiFeld(wh, "_blocker"), absorberAlt = UiFeld(wh, "_absorber");
            var hostTreffer = new List<TrefferReaktion>();
            Action<DamageInfo> mitschreiben = info => hostTreffer.Add(info.Reaktion);
            wh.Damaged += mitschreiben;
            try
            {
                yield return new WaitForSecondsRealtime(1f);
                wolf.ServerStun(180f);
                Vector3 w = wolf.transform.position;
                Vector3 standB = Gnd(w.x + 2.6f, w.z - 1.5f), standA = Gnd(w.x - 2.6f, w.z - 1.5f);
                yield return Ask(b, "goto", V(standB, YawTo(standB, w)));
                yield return Ask(a, "goto", V(standA, YawTo(standA, w)));
                yield return WaitUntil(() => nob != null && nob.Observers.Contains(b.Owner), 15f);
                yield return new WaitForSecondsRealtime(1f);
                if (!Mp4Voraus(wnh != null && nob != null && nob.Observers.Contains(b.Owner), $"B sieht den Wolf (Stufe {wolf.Level}, {Flat(b.transform.position, w):0.0} m)")) yield break;
                bool hostSieht = a.Owner != null && nob.Observers.Contains(a.Owner);
                Info($"Host-Client A beobachtet den Wolf: {(hostSieht ? "ja" : "NEIN – Host-RPC-Zählung nur als Info")}");
                int id = nob.ObjectId;
                GameObject quelle = a.gameObject;
                Func<float, bool, bool, DamageInfo> treffer = (menge, ausweichbar, blockbar) => new DamageInfo
                {
                    Amount = menge, Source = quelle, HitPoint = w + Vector3.up * 0.7f, Direction = Flach(w - standB).normalized,
                    WeaponId = "test", Dodgeable = ausweichbar, Unblockable = !blockbar
                };
                var faelle = new List<PcFall>
                {
                    new PcFall { Soll = TrefferReaktion.Schaden, Name = "Treffer", Alt = "Ergebnis fehlte im RPC",
                        Ausloesen = () => wh.ApplyDamage(treffer(30f, false, false)) },
                    new PcFall { Soll = TrefferReaktion.Ausgewichen, Name = "Ausgewichen", Alt = "Ergebnis fehlte im RPC", Dodger = new PcAusweicher(),
                        Ausloesen = () => wh.ApplyDamage(treffer(30f, true, true)) },
                    new PcFall { Soll = TrefferReaktion.BlockVoll, Name = "Schild voll geblockt", Alt = "Ergebnis fehlte im RPC", Blocker = new PcBlocker { Anteil = 1f },
                        Ausloesen = () => wh.ApplyDamage(treffer(60f, false, true)) },
                    new PcFall { Soll = TrefferReaktion.BlockTeil, Name = "Schild teilweise geblockt", Alt = "Ergebnis fehlte im RPC", Blocker = new PcBlocker { Anteil = 0.5f },
                        Ausloesen = () => wh.ApplyDamage(treffer(60f, false, true)) },
                    new PcFall { Soll = TrefferReaktion.AbsorbVoll, Name = "Aschenschild voll absorbiert", Alt = "Absorbed nicht übertragen – Client sah Nullschaden",
                        Absorber = new PcAbsorber { Anteil = 1f }, Ausloesen = () => wh.ApplyDamage(treffer(60f, false, false)) },
                    new PcFall { Soll = TrefferReaktion.AbsorbTeil, Name = "Aschenschild teilweise absorbiert", Alt = "Absorbed nicht übertragen – Client sah gewöhnlichen Treffer",
                        Absorber = new PcAbsorber { Anteil = 0.5f }, Ausloesen = () => wh.ApplyDamage(treffer(60f, false, false)) },
                    // echter periodischer Weg: ein Gift-Tick nach 1 s (Dauer 1,2 s → genau ein Tick)
                    new PcFall { Soll = TrefferReaktion.Tick, Name = "Gift-Tick (DamageOverTime)", Alt = "Tick auf reinen Clients nicht erkennbar (Waffen-Id nicht übertragen)",
                        Ausloesen = () => DamageOverTime.Apply(wh, quelle, 5f, 1.2f, "pc.test.dot"), Warten = 2.2f },
                    // echter Verfehlt-Weg (CombatResolver/Strike rufen dasselbe): nur Text beim Angreifer, jetzt für alle Beobachter eingeordnet
                    new PcFall { Soll = TrefferReaktion.Verfehlt, Name = "Verfehlt", Alt = "nur der Angreifer erfuhr davon, B nichts",
                        Ausloesen = () => wh.RaiseMissed(treffer(30f, false, false)) },
                };
                foreach (PcFall f in faelle)
                    yield return PcFallPruefen(b, id, wh, wnh, hostTreffer, hostSieht, f);
            }
            finally
            {
                if (wh != null)
                {
                    wh.Damaged -= mitschreiben;
                    UiSetze(wh, "_dodger", dodgerAlt);
                    UiSetze(wh, "_blocker", blockerAlt);
                    wh.SetAbsorber(absorberAlt as IDamageAbsorber);
                }
                if (wolf != null) EnemyDirector.Instance.Remove(wolf);
            }
        }

        private IEnumerator PcFallPruefen(NetworkInventory b, int id, Health wh, NetworkHealth wnh, List<TrefferReaktion> hostTreffer, bool hostSieht, PcFall f)
        {
            wh.Heal(wh.Max);
            UiSetze(wh, "_dodger", f.Dodger);
            UiSetze(wh, "_blocker", f.Blocker);
            wh.SetAbsorber(f.Absorber as IDamageAbsorber);
            yield return Ask(b, "paketc", "reaktion " + id);
            Dictionary<string, string> vor = PcWerte(_last.data);
            int[] bVor = PcZahlen(vor);
            int[] hostRpcVor = PcHostZahlen(wnh);
            int hostVor = hostTreffer.Count;
            f.Ausloesen();
            yield return new WaitForSecondsRealtime(f.Warten);
            UiSetze(wh, "_dodger", null);
            UiSetze(wh, "_blocker", null);
            wh.SetAbsorber(null);
            yield return Ask(b, "paketc", "reaktion " + id);
            Dictionary<string, string> nach = PcWerte(_last.data);
            int[] bNach = PcZahlen(nach);
            int[] hostRpcNach = PcHostZahlen(wnh);
            int soll = (int)f.Soll;
            bool bEinmal = PcDelta(bNach, bVor, soll) == 1 && PcSumme(bNach, bVor) == 1;
            int hostNeu = hostTreffer.Count - hostVor;
            bool hostTrefferOk = f.Soll == TrefferReaktion.Verfehlt ? hostNeu == 0 : hostNeu == 1 && hostTreffer[hostVor] == f.Soll;
            bool hostRpcOk = PcDelta(hostRpcNach, hostRpcVor, soll) == 1 && PcSumme(hostRpcNach, hostRpcVor) == 1;
            int abwVor = PcInt(vor, "abw"), abwNach = PcInt(nach, "abw");
            bool keineAbw = abwVor >= 0 && abwNach == abwVor;
            float abs = PcFloat(nach, "abs");
            bool absorbiert = f.Soll != TrefferReaktion.AbsorbVoll && f.Soll != TrefferReaktion.AbsorbTeil || abs > 0f;
            string hostText = hostSieht ? $"Host-RPC {PcDeltaText(hostRpcNach, hostRpcVor)}" : "Host-RPC nicht prüfbar (A sieht den Wolf nicht)";
            Check(bEinmal && hostTrefferOk && (hostRpcOk || !hostSieht) && keineAbw && absorbiert,
                $"{f.Name}: B empfängt „{f.Soll}“ genau einmal (Δ {PcDeltaText(bNach, bVor)}), eigene Ableitung abweichend {abwNach - abwVor}×" +
                (f.Soll == TrefferReaktion.AbsorbVoll || f.Soll == TrefferReaktion.AbsorbTeil ? $", absorbiert {abs:0} angekommen" : "") +
                $"; Host-Damaged {hostNeu}× ({(hostNeu > 0 ? hostTreffer[hostVor].ToString() : "–")}), {hostText} | alt: {f.Alt}");
        }

        private static int[] PcHostZahlen(NetworkHealth nh)
        {
            var z = new int[TrefferReaktionen.Anzahl];
            for (int i = 0; i < z.Length; i++) z[i] = nh != null ? nh.ReaktionenEmpfangen((TrefferReaktion)i) : 0;
            return z;
        }

        private static int PcDelta(int[] nach, int[] vor, int i) => i < nach.Length && i < vor.Length ? nach[i] - vor[i] : -99;

        private static int PcSumme(int[] nach, int[] vor)
        {
            int s = 0;
            for (int i = 0; i < Mathf.Min(nach.Length, vor.Length); i++) s += nach[i] - vor[i];
            return s;
        }

        private static string PcDeltaText(int[] nach, int[] vor)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Mathf.Min(nach.Length, vor.Length); i++)
            {
                int d = nach[i] - vor[i];
                if (d == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append((TrefferReaktion)i).Append(' ').Append(d.ToString("+0;-0"));
            }
            return sb.Length > 0 ? sb.ToString() : "nichts";
        }

        // ================================================================== KAMPF-WIRKEN

        private IEnumerator PcWirkenProbe(NetworkInventory a, Vector3 lager)
        {
            Sp3Punkt(PcWirken, "C (Aschenweber) wirkt Heilung auf B; B liest, was er von C's Wirken sieht (Erfolg, Bewegung, Tod)");
            NetworkInventory b = _mp4B, c = _mp4C;
            var cp = c.GetComponent<NetworkProgression>();
            var cn = c.GetComponent<NetworkMotion>();
            var cnp = c.GetComponent<NetworkPlayer>();
            Health bh = b.GetComponent<Health>(), ch = c.GetComponent<Health>();
            NetworkObject cNob = c.GetComponent<NetworkObject>();
            SpellDefinition heil = GameData.Get<SpellDefinition>("spell.weaver_heal");
            if (!Mp4Voraus(cp != null && cn != null && cnp != null && bh != null && ch != null && heil != null && !heil.Instant,
                    $"C mit Fortschritt/Bewegung, Heilung mit Wirkzeit ({heil?.castTime:0.0} s)")) yield break;
            if (cp.ServerSpellRank(heil.Id) <= 0) cp.ServerDebugLearn(heil.Id, 1);
            if (!Mp4Voraus(cp.ServerSpellRank(heil.Id) > 0 && cp.ServerResourceType == ResourceType.Mana, $"C kennt Heilung (Rang {cp.ServerSpellRank(heil.Id)}), Mana")) yield break;
            Vector3 standB = Gnd(lager.x + 4f, lager.z + 7f), standC = Gnd(lager.x - 2f, lager.z + 7f), standA = Gnd(lager.x + 1f, lager.z + 3f);
            yield return Ask(b, "goto", V(standB, YawTo(standB, standC)));
            yield return Ask(c, "goto", V(standC, YawTo(standC, standB)));
            yield return Ask(a, "goto", V(standA, YawTo(standA, standC)));
            yield return WaitUntil(() => cNob != null && cNob.Observers.Contains(b.Owner), 15f);
            yield return new WaitForSecondsRealtime(1.5f);
            if (!Mp4Voraus(cNob != null && cNob.Observers.Contains(b.Owner), $"B sieht C ({Flat(b.transform.position, c.transform.position):0.0} m)")) yield break;
            int cId = cNob.ObjectId;
            int hostBeg0 = cp.WirkenBeginneEmpfangen, hostEnd0 = cp.WirkenEndenEmpfangen;

            // ---------------------------------------------------------- 1 Erfolg
            cp.ServerDebugResetCooldowns();
            cp.ServerSetResource(cp.ServerResourceMax);
            yield return Ask(b, "paketc", "wirken " + cId);
            Dictionary<string, string> vor = PcWerte(_last.data);
            int gewirkt0 = cp.ServerSpellsCast;
            string why = cp.ServerCast(heil.Id, bh);
            if (!Mp4Voraus(why == "" && cp.ServerIsCasting, $"C wirkt Heilung auf B ({(why == "" ? "angenommen" : why)})")) yield break;
            yield return new WaitForSecondsRealtime(0.9f);
            yield return Ask(b, "paketc", "wirken " + cId);
            Dictionary<string, string> mitte = PcWerte(_last.data);
            bool hostMitte = cp.WirkenSichtbar;
            yield return WaitUntil(() => !cp.ServerIsCasting, 6f);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Ask(b, "paketc", "wirken " + cId);
            Dictionary<string, string> nach = PcWerte(_last.data);
            bool erfolgt = cp.ServerSpellsCast == gewirkt0 + 1;
            Check(erfolgt && PcInt(mitte, "beginn") - PcInt(vor, "beginn") == 1 && PcInt(mitte, "wirkt") == 1 &&
                  PcInt(nach, "ende") - PcInt(vor, "ende") == 1 && PcInt(nach, "wirkt") == 0 && PcInt(nach, "ausgeloest") == 1,
                $"Erfolg: B sieht C wirken (Beginn +{PcInt(mitte, "beginn") - PcInt(vor, "beginn")}, wirkt {PcInt(mitte, "wirkt")}) und das ausgelöste Ende " +
                $"(Ende +{PcInt(nach, "ende") - PcInt(vor, "ende")}, wirkt {PcInt(nach, "wirkt")}, ausgelöst {PcInt(nach, "ausgeloest")}); Server gewirkt {(erfolgt ? "ja" : "NEIN")} " +
                "| alt: B sah keine Wirkhaltung (nur Glut), Ende beim Erfolg nie gemeldet");
            int animMitte = PcInt(mitte, "anim"), animNach = PcInt(nach, "anim");
            if (animMitte >= 0 && animNach >= 0)
                Check(animMitte == 1 && animNach == 0, $"Animator „Casting“ von C auf B: während {animMitte}, danach {animNach} | alt: blieb 0 (nur der Besitzer setzte ihn)");
            else Sp3NichtGeprueft(PcWirken, $"Animator-Parameter „Casting“ auf B nicht vorhanden/bereit (anim {animMitte}/{animNach}) – nur der Netzzustand geprüft");
            Info($"Host (Editor) während des Wirkens: zeigt Wirken {(hostMitte ? "ja" : "NEIN")}, Animator {PcAnimWirkt(cNob)} (−1 = ohne Parameter)");

            // ---------------------------------------------------------- 2 Unterbrechung durch Bewegung
            yield return new WaitForSecondsRealtime(1.6f);   // globale Abklingzeit der Heilung abwarten
            cp.ServerDebugResetCooldowns();
            cp.ServerSetResource(cp.ServerResourceMax);
            yield return Ask(b, "paketc", "wirken " + cId);
            vor = PcWerte(_last.data);
            gewirkt0 = cp.ServerSpellsCast;
            why = cp.ServerCast(heil.Id, bh);
            if (Mp4Voraus(why == "" && cp.ServerIsCasting, $"C wirkt erneut ({(why == "" ? "angenommen" : why)})"))
            {
                yield return new WaitForSecondsRealtime(0.7f);
                Vector3 seit = c.transform.position + c.transform.right * 1.6f;
                cn.ServerTeleport(Gnd(seit.x, seit.z), c.transform.eulerAngles.y);
                yield return WaitUntil(() => !cp.ServerIsCasting, 3f);
                bool abgebrochen = !cp.ServerIsCasting && cp.ServerSpellsCast == gewirkt0;
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Ask(b, "paketc", "wirken " + cId);
                nach = PcWerte(_last.data);
                Check(abgebrochen && PcInt(nach, "beginn") - PcInt(vor, "beginn") == 1 && PcInt(nach, "ende") - PcInt(vor, "ende") == 1 &&
                      PcInt(nach, "wirkt") == 0 && PcInt(nach, "ausgeloest") == 0,
                    $"Unterbrechung (C bewegt sich 1,6 m): Server abgebrochen {(abgebrochen ? "ja" : "NEIN")}; B: Beginn +{PcInt(nach, "beginn") - PcInt(vor, "beginn")}, " +
                    $"Ende +{PcInt(nach, "ende") - PcInt(vor, "ende")}, wirkt {PcInt(nach, "wirkt")}, ausgelöst {PcInt(nach, "ausgeloest")} | alt: B sah keine Haltung");
            }
            yield return Ask(c, "goto", V(standC, YawTo(standC, standB)));
            yield return new WaitForSecondsRealtime(1.6f);

            // ---------------------------------------------------------- 3 Tod während des Wirkens
            Func<NetworkPlayer, string> haltVorher = NetworkPlayer.HoldRespawn;
            NetworkPlayer.HoldRespawn = p => ReferenceEquals(p, cnp) ? Mp4Halten : haltVorher?.Invoke(p);
            try
            {
                cp.ServerDebugResetCooldowns();
                cp.ServerSetResource(cp.ServerResourceMax);
                yield return Ask(b, "paketc", "wirken " + cId);
                vor = PcWerte(_last.data);
                why = cp.ServerCast(heil.Id, bh);
                if (Mp4Voraus(why == "" && cp.ServerIsCasting, $"C wirkt ein drittes Mal ({(why == "" ? "angenommen" : why)})"))
                {
                    yield return new WaitForSecondsRealtime(0.7f);
                    UiSetze(ch, "_invulnerableUntil", -1f);
                    ch.ApplyDamage(new DamageInfo { Amount = ch.Max * 20f, Direction = -c.transform.forward, Unblockable = true });
                    yield return WaitUntil(() => !ch.IsAlive && !cp.ServerIsCasting, 3f);
                    bool totAbgebrochen = !ch.IsAlive && !cp.ServerIsCasting;
                    yield return new WaitForSecondsRealtime(0.8f);
                    yield return Ask(b, "paketc", "wirken " + cId);
                    nach = PcWerte(_last.data);
                    Check(totAbgebrochen && PcInt(nach, "beginn") - PcInt(vor, "beginn") == 1 && PcInt(nach, "ende") - PcInt(vor, "ende") == 1 &&
                          PcInt(nach, "wirkt") == 0 && PcInt(nach, "ausgeloest") == 0,
                        $"Tod während des Wirkens: C gefallen und Wirken abgebrochen {(totAbgebrochen ? "ja" : "NEIN")}; B: Ende +{PcInt(nach, "ende") - PcInt(vor, "ende")}, " +
                        $"wirkt {PcInt(nach, "wirkt")}, ausgelöst {PcInt(nach, "ausgeloest")} | alt: B sah keine Haltung");
                }
            }
            finally
            {
                NetworkPlayer.HoldRespawn = haltVorher;
                if (ch != null && !ch.IsAlive && cnp != null) cnp.ServerReviveHere(1f);
                if (ch != null) ch.SetInvulnerableFor(7200f);   // wie in der Vorbereitung der Suite
            }
            yield return new WaitForSecondsRealtime(1f);
            int hostBeg = cp.WirkenBeginneEmpfangen - hostBeg0, hostEnd = cp.WirkenEndenEmpfangen - hostEnd0;
            bool hostSieht = a.Owner != null && cNob != null && cNob.Observers.Contains(a.Owner);
            if (hostSieht)
                Check(hostBeg == 3 && hostEnd == 3, $"Host (Editor) empfängt drei Beginne und drei Enden – je Wirken genau einmal (Beginne {hostBeg}, Enden {hostEnd}) | alt: Ende beim Erfolg fehlte (2)");
            else Info($"Host-Client A beobachtet C nicht – Host-Zählung nur Info (Beginne {hostBeg}, Enden {hostEnd})");
            Info($"C lebt wieder: {(ch != null && ch.IsAlive ? "ja" : "NEIN")}");

            // ---------------------------------------------------------- 4 Wirken des Hosts A (Besitzer ist der Host): B sieht es
            // Auf dem Host bleibt A's eigene Haltung beim SpellClient-Weg (IsOwner) – der Beobachter-RPC setzt sie dort nicht ein zweites Mal.
            var ap = a.GetComponent<NetworkProgression>();
            NetworkObject aNob = a.GetComponent<NetworkObject>();
            CharacterClassDefinition aKlasse = a.ServerModel != null ? a.ServerModel.Class : null;
            SpellDefinition aZauber = ap != null && aKlasse != null
                ? GameData.Database.All.OfType<SpellDefinition>().FirstOrDefault(s => s.classId == aKlasse.Id && !s.Instant && ap.ServerSpellRank(s.Id) > 0 &&
                                                                                     (s.target == SpellTarget.Self || s.target == SpellTarget.Friendly))
                : null;
            if (aZauber == null)
            {
                Sp3NichtGeprueft(PcWirken, $"Wirken des Hosts A: Klasse {aKlasse?.DisplayName ?? "?"} kennt keinen Zauber mit Wirkzeit auf sich/Freunde – B prüft das Wirken an C");
                yield break;
            }
            ap.ServerDebugResetCooldowns();
            ap.ServerSetResource(ap.ServerResourceMax);
            yield return Ask(b, "paketc", "wirken " + aNob.ObjectId);
            Dictionary<string, string> aVor = PcWerte(_last.data);
            why = ap.ServerCast(aZauber.Id, a.GetComponent<Health>());
            if (!Mp4Voraus(why == "" && ap.ServerIsCasting, $"A wirkt {aZauber.DisplayName} ({(why == "" ? "angenommen" : why)})")) yield break;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Ask(b, "paketc", "wirken " + aNob.ObjectId);
            Dictionary<string, string> aMitte = PcWerte(_last.data);
            yield return WaitUntil(() => !ap.ServerIsCasting, 8f);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Ask(b, "paketc", "wirken " + aNob.ObjectId);
            Dictionary<string, string> aNach = PcWerte(_last.data);
            Check(PcInt(aMitte, "beginn") - PcInt(aVor, "beginn") == 1 && PcInt(aMitte, "wirkt") == 1 && PcInt(aNach, "ende") - PcInt(aVor, "ende") == 1 &&
                  PcInt(aNach, "wirkt") == 0,
                $"Host A wirkt {aZauber.DisplayName}: B sieht Beginn (wirkt {PcInt(aMitte, "wirkt")}) und Ende (+{PcInt(aNach, "ende") - PcInt(aVor, "ende")}, " +
                $"ausgelöst {PcInt(aNach, "ausgeloest")}) | alt: B sah keine Wirkhaltung");
        }

        // ================================================================== PAKETC-LEICHE

        private IEnumerator PcLeichenzustand(Vector3 lager)
        {
            Sp3Punkt(PcLeiche, "Gegner fern (≥ 650 m) mit kritischem Hieb getötet, C am Wachtfeuer außer Sicht; danach C hin, Wiederbeleben, erneut töten");
            NetworkInventory b = _mp4B, c = _mp4C;
            WeaponDefinition hieb = GameData.Database.All.OfType<WeaponDefinition>().FirstOrDefault(x => Zerstueckelung.TrenntKlasse(x.weaponClass));
            if (!Mp4Voraus(hieb != null, $"Hiebwaffe (Schwert/Axt) für den Trennpunkt: {hieb?.Id}")) yield break;
            Vector3? fern = Mp4FernerOrt(lager, 650f, out string ortInfo);
            if (!Mp4Voraus(fern.HasValue, "Ort weit vom Wachtfeuer: " + ortInfo)) yield break;
            Vector3 f = fern.Value;
            Vector3 cLager = lager + new Vector3(-3f, 0.3f, 3f);
            yield return Ask(c, "goto", V(cLager, 0f));
            Vector3 standB0 = Gnd(f.x - 3f, f.z);
            yield return Ask(b, "goto", V(standB0, YawTo(standB0, f)));
            yield return new WaitForSecondsRealtime(1.5f);

            // Gegner, dessen Darstellung am Todesstoß einen Trennpunkt liefert (Mensch: Glied; Tier: Teil) – vorab rein berechnet
            var kandidaten = new List<string> { "enemy.salt_brute", "enemy.ash_wolf" };
            foreach (EnemySpawnArea g in FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None))
                if (g != null && g.Definition != null && g.Prefab != null && !kandidaten.Contains(g.Definition.Id)) kandidaten.Add(g.Definition.Id);
            EnemyBrain feind = null;
            byte vorab = 0;
            var versuche = new StringBuilder();
            int n = 0;
            foreach (string k in kandidaten)
            {
                if (n >= 6) break;
                EnemySpawnArea v = Mp4Vorlage(k);
                if (v == null) continue;
                n++;
                EnemyBrain e = EnemyDirector.Instance.SpawnAt(v.Prefab, f, 0f, Mathf.Max(1, b.ServerModel.Level - 2), "pc-leiche",
                    br => br.SetAggroProfile(AggroProfile.Passive), 600f);
                if (e == null) { versuche.Append(k).Append(" (nicht erzeugt) "); continue; }
                yield return new WaitForSecondsRealtime(0.6f);
                byte t = e != null ? Zerstueckelung.TrennpunktFuer(PcTodesstoss(e, hieb, 0), 0f, e.gameObject) : (byte)0;
                versuche.Append(k).Append(" → ").Append(t).Append("  ");
                if (t != 0) { feind = e; vorab = t; break; }
                if (e != null) EnemyDirector.Instance.Remove(e);
            }
            if (!Mp4Voraus(feind != null, $"Gegner mit Trennpunkt am fernen Ort ({versuche.ToString().Trim()})")) yield break;
            string feindId = feind.Definition != null ? feind.Definition.Id : "enemy.ash_wolf";
            try
            {
                feind.ServerStun(300f);
                NetworkObject nob = feind.GetComponent<NetworkObject>();
                Health fh = feind.GetComponent<Health>();
                NetworkHealth fnh = feind.GetComponent<NetworkHealth>();
                int id = nob.ObjectId;
                Vector3 g = feind.transform.position;
                Vector3 standB = Gnd(g.x - 2.6f, g.z);
                yield return Ask(b, "goto", V(standB, YawTo(standB, g)));
                yield return WaitUntil(() => nob != null && nob.Observers.Contains(b.Owner), 30f);
                yield return new WaitForSecondsRealtime(1f);
                bool bSieht = nob != null && nob.Observers.Contains(b.Owner), cSiehtNicht = nob != null && !nob.Observers.Contains(c.Owner);
                if (!Mp4Voraus(fnh != null && bSieht && cSiehtNicht,
                        $"{feind.Definition?.DisplayName} (Stufe {feind.Level}) am fernen Ort: B sieht ihn, C ({Flat(c.transform.position, g):0} m) nicht")) yield break;

                // ---------------------------------------------------------- Todesstoß, B in Sicht, C nicht
                fh.ApplyDamage(PcTodesstoss(feind, hieb, 0));
                yield return new WaitForSecondsRealtime(1.2f);
                byte t1 = fnh.Trennpunkt;
                if (!Mp4Voraus(!fh.IsAlive && t1 != 0, $"Server: tot, Leichenzustand Trennpunkt {t1} (vorab {vorab})")) yield break;
                yield return Ask(b, "paketc", "leiche " + id);
                Dictionary<string, string> bTod = PcWerte(_last.data);
                Check(PcInt(bTod, "found") == 1 && PcInt(bTod, "tot") == 1 && PcInt(bTod, "trenn") == t1 && PcInt(bTod, "ereignisse") >= 1,
                    $"B (beim Tod in Sicht) liest Trennpunkt {PcInt(bTod, "trenn")} (Server {t1}), Ereignisse {PcInt(bTod, "ereignisse")} | alt: kein Leichenzustand");
                yield return Ask(c, "paketc", "leiche " + id);
                string cVorher = _last.data;

                // ---------------------------------------------------------- später Beobachter C
                Vector3 standC = Gnd(g.x, g.z - 2.6f);
                yield return Ask(c, "goto", V(standC, YawTo(standC, g)));
                yield return WaitUntil(() => nob != null && nob.Observers.Contains(c.Owner), 30f);
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Ask(c, "paketc", "leiche " + id);
                Dictionary<string, string> cSpaet = PcWerte(_last.data);
                // Rot mit dem alten Spiel: der Treffer-RPC ist nicht gepuffert, NetworkHealth synchronisierte keinen Trennpunkt → trenn=0
                Check(cVorher.StartsWith("found=0") && PcInt(cSpaet, "found") == 1 && PcInt(cSpaet, "tot") == 1 && PcInt(cSpaet, "trenn") == t1 &&
                      PcInt(cSpaet, "ereignisse") == 1,
                    $"Später Beobachter C: vorher „{cVorher}“, nach dem Hingehen tot {PcInt(cSpaet, "tot")}, Trennpunkt {PcInt(cSpaet, "trenn")} (Server {t1}), " +
                    $"Ereignis {PcInt(cSpaet, "ereignisse")}× | alt: Trennpunkt 0 – Leiche bei C vollständig");

                // ---------------------------------------------------------- Wiederbeleben → 0
                int ereignisse0 = PcInt(cSpaet, "ereignisse");
                fh.Revive(1f);
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Ask(c, "paketc", "leiche " + id);
                Dictionary<string, string> cLebt = PcWerte(_last.data);
                Check(fh.IsAlive && fnh.Trennpunkt == 0 && PcInt(cLebt, "tot") == 0 && PcInt(cLebt, "trenn") == 0 && PcInt(cLebt, "ereignisse") == ereignisse0 + 1,
                    $"Wiederbelebt (Health.Revive): Server {fnh.Trennpunkt}, C lebt {(PcInt(cLebt, "tot") == 0 ? "ja" : "NEIN")}, Trennpunkt {PcInt(cLebt, "trenn")}, " +
                    $"Ereignisse +{PcInt(cLebt, "ereignisse") - ereignisse0} | alt: kein Zustand zum Zurücksetzen");

                // ---------------------------------------------------------- erneut töten, C schaut zu (live)
                // Vorab rein berechnen: liefert die Darstellung nach dem ersten Trennen (gleiches Objekt) noch einen Punkt? Sonst ist das
                // kein Netzfehler, sondern eine Darstellungsfrage (Figuren-Sitzung) – dann nur „nicht geprüft“.
                DamageInfo stoss2 = PcTodesstoss(feind, hieb, 1);
                byte vorab2 = Zerstueckelung.TrennpunktFuer(stoss2, 0f, feind.gameObject);
                fh.ApplyDamage(stoss2);
                yield return new WaitForSecondsRealtime(1.2f);
                byte t2 = fnh.Trennpunkt;
                yield return Ask(c, "paketc", "leiche " + id);
                Dictionary<string, string> cTod2 = PcWerte(_last.data);
                if (vorab2 == 0)
                {
                    Sp3NichtGeprueft(PcLeiche, $"zweiter Todesstoß am wiederbelebten Gegner: Darstellung liefert keinen Trennpunkt mehr (Server {t2}, C {PcInt(cTod2, "trenn")}, tot {PcInt(cTod2, "tot")})");
                }
                else
                    Check(!fh.IsAlive && t2 == vorab2 && PcInt(cTod2, "tot") == 1 && PcInt(cTod2, "trenn") == t2 && PcInt(cTod2, "ereignisse") == PcInt(cLebt, "ereignisse") + 1,
                        $"Erneut getötet: Server {t2} (vorab {vorab2}), C liest {PcInt(cTod2, "trenn")} (Ereignisse +{PcInt(cTod2, "ereignisse") - PcInt(cLebt, "ereignisse")}) | alt: Trennpunkt 0 bei C");
            }
            finally
            {
                if (feind != null) EnemyDirector.Instance.Remove(feind);
            }

            // ---------------------------------------------------------- Wiederkehr = neues Objekt, beginnt mit 0
            yield return new WaitForSecondsRealtime(1f);
            EnemySpawnArea vNeu = Mp4Vorlage(feindId) ?? Mp4Vorlage("enemy.ash_wolf");
            EnemyBrain neu = vNeu != null ? EnemyDirector.Instance.SpawnAt(vNeu.Prefab, f, 90f, Mathf.Max(1, b.ServerModel.Level - 2), "pc-leiche-neu",
                br => br.SetAggroProfile(AggroProfile.Passive), 20f) : null;
            if (neu == null) { Sp3NichtGeprueft(PcLeiche, "neuer Gegner nach dem Entfernen nicht erzeugt"); yield break; }
            try
            {
                neu.ServerStun(60f);
                NetworkObject nob2 = neu.GetComponent<NetworkObject>();
                yield return WaitUntil(() => nob2 != null && nob2.Observers.Contains(c.Owner), 20f);
                yield return new WaitForSecondsRealtime(1f);
                yield return Ask(c, "paketc", "leiche " + nob2.ObjectId);
                Dictionary<string, string> cNeu = PcWerte(_last.data);
                Check(PcInt(cNeu, "found") == 1 && PcInt(cNeu, "tot") == 0 && PcInt(cNeu, "trenn") == 0 && PcInt(cNeu, "ereignisse") == 0,
                    $"Wiederkehr (neues Objekt am selben Ort): C liest lebt, Trennpunkt {PcInt(cNeu, "trenn")}, Ereignisse {PcInt(cNeu, "ereignisse")}");
            }
            finally
            {
                if (neu != null) EnemyDirector.Instance.Remove(neu);
            }
        }

        /// <summary>Kritischer, tödlicher Hieb mit einer Trennwaffe auf den Zielpunkt (Variante verschiebt den Trefferpunkt). Ohne Quelle: keine Beute/EP.</summary>
        private static DamageInfo PcTodesstoss(EnemyBrain e, WeaponDefinition hieb, int variante)
        {
            var ct = e.GetComponentInChildren<CombatTarget>();
            Vector3 ziel = ct != null ? ct.AimPoint : e.transform.position + Vector3.up;
            Vector3 dir = e.transform.forward.sqrMagnitude > 0.01f ? -Flach(e.transform.forward).normalized : Vector3.forward;
            Vector3 punkt = ziel + (variante == 0 ? Vector3.zero : e.transform.right * 0.25f + Vector3.up * 0.1f);
            Health h = e.GetComponent<Health>();
            return new DamageInfo
            {
                Amount = (h != null ? h.Max : 1000f) * 20f, IsCritical = true, HitPoint = punkt, Direction = dir,
                WeaponId = hieb != null ? hieb.Id : "", Unblockable = true
            };
        }

        // ================================================================== Agent-Abfrage (läuft auf dem Client, der gefragt wird)

        /// <summary>
        /// „paketc reaktion|wirken|leiche &lt;ObjectId&gt;“: was DIESER Rechner von der Figur sieht. Nur lesen.
        ///   reaktion → found=1;n=&lt;je TrefferReaktion, Komma&gt;;abs=&lt;zuletzt absorbiert&gt;;abw=&lt;Ableitungs-Abweichungen gesamt&gt;
        ///   wirken   → found=1;beginn=..;ende=..;wirkt=0|1;ausgeloest=0|1;anim=-1|0|1 (Animator-Bool „Casting“, −1 = nicht vorhanden)
        ///   leiche   → found=0 | found=1;tot=0|1;trenn=&lt;Trennpunkt&gt;;ereignisse=&lt;TrennpunktGeaendert-Aufrufe&gt;
        /// </summary>
        internal static string PaketCAbfrage(string art, NetworkObject nob)
        {
            if (nob == null) return "found=0";
            switch (art)
            {
                case "reaktion":
                {
                    NetworkHealth nh = nob.GetComponent<NetworkHealth>() ?? nob.GetComponentInChildren<NetworkHealth>();
                    if (nh == null) return "fehler=keine NetworkHealth";
                    var sb = new StringBuilder("found=1;n=");
                    for (int i = 0; i < TrefferReaktionen.Anzahl; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(nh.ReaktionenEmpfangen((TrefferReaktion)i));
                    }
                    sb.Append(";abs=").Append(nh.LetzteAbsorbiert.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(";abw=").Append(NetworkHealth.ReaktionAbweichungen);
                    return sb.ToString();
                }
                case "wirken":
                {
                    NetworkProgression np = nob.GetComponent<NetworkProgression>();
                    if (np == null) return "fehler=kein NetworkProgression";
                    return $"found=1;beginn={np.WirkenBeginneEmpfangen};ende={np.WirkenEndenEmpfangen};wirkt={(np.WirkenSichtbar ? 1 : 0)};" +
                           $"ausgeloest={(np.WirkenZuletztAusgeloest ? 1 : 0)};anim={PcAnimWirkt(nob)}";
                }
                case "leiche":
                {
                    NetworkHealth nh = nob.GetComponent<NetworkHealth>() ?? nob.GetComponentInChildren<NetworkHealth>();
                    Health h = nh != null ? nh.GetComponent<Health>() : null;
                    if (nh == null || h == null) return "fehler=keine NetworkHealth";
                    return $"found=1;tot={(h.IsAlive ? 0 : 1)};trenn={nh.Trennpunkt};ereignisse={nh.TrennpunktEreignisse}";
                }
                default:
                    return "fehler=Art unbekannt: " + art;
            }
        }

        /// <summary>Animator-Bool „Casting“ der Figur auf diesem Rechner: 1/0, −1 = kein Animator/Parameter.</summary>
        private static int PcAnimWirkt(NetworkObject nob)
        {
            var br = nob != null ? nob.GetComponentInChildren<Ashveil.Gameplay.Characters.CharacterAnimatorBridge>(true) : null;
            Animator an = br != null ? br.Animator : null;
            if (an == null || an.runtimeAnimatorController == null || !an.isActiveAndEnabled) return -1;
            foreach (AnimatorControllerParameter p in an.parameters)
                if (p.type == AnimatorControllerParameterType.Bool && p.name == "Casting") return an.GetBool(p.nameHash) ? 1 : 0;
            return -1;
        }

        // ================================================================== Antworten lesen

        private static Dictionary<string, string> PcWerte(string s)
        {
            var d = new Dictionary<string, string>();
            foreach (string teil in (s ?? "").Split(';'))
            {
                int i = teil.IndexOf('=');
                if (i > 0) d[teil.Substring(0, i).Trim()] = teil.Substring(i + 1).Trim();
            }
            return d;
        }

        private static int PcInt(Dictionary<string, string> d, string k) =>
            d != null && d.TryGetValue(k, out string v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : -99;

        private static float PcFloat(Dictionary<string, string> d, string k) =>
            d != null && d.TryGetValue(k, out string v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : -99f;

        private static int[] PcZahlen(Dictionary<string, string> d)
        {
            var z = new int[TrefferReaktionen.Anzahl];
            for (int i = 0; i < z.Length; i++) z[i] = -99;
            if (d == null || !d.TryGetValue("n", out string v)) return z;
            string[] teile = v.Split(',');
            for (int i = 0; i < z.Length && i < teile.Length; i++)
                if (int.TryParse(teile[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) z[i] = n;
            return z;
        }
    }
}
