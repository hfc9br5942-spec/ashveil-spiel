using System.Collections;
using System.IO;
using System.Linq;
using Ashveil.Gameplay.Colosseum;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Kolosseum Pass 4 – der spielbare 10-Minuten-Lauf mit Maxmod (Host) und Krisbot (Test-Client), Zeitraffer ×10:
    /// Bereit-Glocke statt Automatik, HUD (Uhr, Block, Vorrat), Champions bei 5:00 und 10:00, Liegenbleiben statt
    /// Wiederbeleben am Tor, Wiederbeleben durch den Mitstreiter (5 s, Schaden bricht ab, Vorrat), Pruefpunkt-Truhe mit
    /// persoenlicher Belohnung (auch beim Gehen), sofort wieder frei, Wipe.
    /// Start: "Ashveil &gt; Test &gt; Kolosseum-Lauf (Pass 4, 2 Spieler)". Bericht: Logs/Ashveil_KolosseumLaufTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string ArenaRunFlag = "kolosseum4";
        private static bool _arenaRunMode;

        private IEnumerator ArenaRunSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(ArenaProfiles[0])) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, ArenaProfiles[1]);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(ArenaProfiles[1])) != null, 150f);
            Check(b != null, "Spieler B (Test-Client) angemeldet");
            if (b == null) yield break;
            yield return new WaitForSecondsRealtime(2f);

            DungeonDirector dir = DungeonDirector.For(ArenaId);
            DungeonPortal portal = DungeonPortal.Find(ArenaId);
            var handler = dir != null ? dir.GetComponent<ColosseumInstanceHandler>() : null;
            Check(dir != null && portal != null && handler != null && ColosseumInstanceHandler.Current == handler, "Kolosseum mit Ablauf (Pass 4) bereit");
            if (handler == null) yield break;
            Check(ArenaBell.All.Count == 6 && ArenaChest.All.Count == 6, $"Jede Arena hat eine Bereit-Glocke und eine Truhe ({ArenaBell.All.Count}/{ArenaChest.All.Count})");
            var bosses = handler.Bosses();
            Check(bosses.Count == 2 && Mathf.Approximately(bosses[0].atSeconds, 300f) && Mathf.Approximately(bosses[1].atSeconds, 600f),
                $"Zwei Champions: {string.Join(", ", bosses.Select(x => $"{ColosseumInstanceHandler.Clock(x.atSeconds)} {(x.prefab != null ? x.prefab.name : "?")}"))}");

            DungeonDirector.ResetOverride = 3f;
            ColosseumInstanceHandler.StartDelayOverride = -1f;
            ColosseumInstanceHandler.BellCountdownOverride = 3f;
            ColosseumInstanceHandler.TimeScaleOverride = 10f;
            ColosseumInstanceHandler.SeedOverride = ArenaTestSeed;
            ColosseumInstanceHandler.WipeDelayOverride = 2f;
            Health ha = a.GetComponent<Health>(), hb = b.GetComponent<Health>();
            bool safeA = true, safeB = true, guard = true, healA = false;
            StartCoroutine(KeepSafe(() => guard, () => safeA ? ha : null, () => safeB ? hb : null));
            StartCoroutine(KeepHealed(() => guard, () => healA ? ha : null));

            // Eine Gruppe – sonst bekommt jeder seine eigene Arena
            yield return Ask(a, "party-invite", b.CharacterName);
            yield return Ask(b, "party-respond", "1");
            yield return new WaitForSecondsRealtime(0.5f);
            Check(PartyService.Of(a.ProfileId) != null && PartyService.Of(a.ProfileId) == PartyService.Of(b.ProfileId), "Maxmod und Krisbot bilden eine Gruppe");

            // ---------------------------------------------------------- Betreten und Glocke
            Line("");
            Line("[Glocke]");
            DungeonDirector.Instance inst = null;
            yield return EnterRing(dir, portal, a, b);
            inst = dir.InstanceOf(a.ProfileId);
            Check(inst != null && dir.InstanceOf(b.ProfileId) == inst, "Maxmod und Krisbot stehen gemeinsam im Ring");
            if (inst == null) { guard = false; yield break; }
            DungeonSlot slot = inst.Slot;
            ArenaBell bell = ArenaBell.All.First(x => x.SlotIndex == slot.Index);
            ArenaChest chest = ArenaChest.All.First(x => x.SlotIndex == slot.Index);
            ColosseumInstanceHandler.RunState s = handler.StateOf(inst);
            yield return new WaitForSecondsRealtime(4f);
            yield return Ask(b, "arena");
            Check(s != null && !s.Started && _last.data.Contains("visible=1") && _last.data.Contains("run=0") && _last.data.Contains("revives=3"),
                $"Ohne Glocke beginnt nichts – Krisbots HUD: „Der Ring wartet“ ({_last.data})");
            Vector3 mid = slot.transform.position;
            yield return Ask(a, "goto", V(Gnd(mid.x, mid.z - 8f), 0f));
            yield return Ask(a, "arena-bell");
            Check(s.StartAt < 0f, "Glocke aus 15 m Entfernung: nichts passiert");
            Vector3 bellStand = bell.transform.position + bell.transform.forward * 1.6f;
            yield return Ask(b, "goto", V(bellStand, bell.transform.eulerAngles.y + 180f));
            yield return Ask(b, "arena-bell");
            Check(s.StartAt > 0f && s.BellRungBy == b.ProfileId && _last.data.Contains("countdown="), $"Krisbot läutet die Glocke – Countdown ({_last.data})");
            yield return WaitUntil(() => s.Started, 8f);
            Check(s.Started && s.Participants.Count == 2 && s.Revives == 3, $"Lauf beginnt mit 2 Teilnehmern und 3 Wiederbelebungen (Seed {s.Seed})");
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "arena");
            float clockB = ParseAfter(_last.data, "clock=");
            Check(_last.data.Contains("run=1") && Mathf.Abs(clockB - s.Elapsed) < 3f,
                $"Krisbots HUD-Uhr läuft mit dem Server ({clockB:0.0} s gegen {s.Elapsed:0.0} s Spielzeit, Zeitraffer ×10)");

            // ---------------------------------------------------------- Champion 1
            Line("");
            Line("[Champion 5:00]");
            yield return WaitUntil(() => s.Bosses.Count >= 1, 45f);
            EnemyBrain boss1 = s.Bosses.FirstOrDefault();
            Health bh1 = boss1 != null ? boss1.GetComponent<Health>() : null;
            float normal = boss1 != null ? boss1.Definition.HealthAt(boss1.Level) : 0f;
            Check(boss1 != null && bh1 != null && bh1.Max > normal * 3f && boss1.DamageScale > 1.2f,
                $"Bei {ColosseumInstanceHandler.Clock(s.Elapsed)}: {(boss1 != null ? boss1.Definition.DisplayName : "?")} Stufe {(boss1 != null ? boss1.Level : 0)}, " +
                $"{(bh1 != null ? bh1.Max : 0):0} Leben (normal {normal:0}), Schaden ×{(boss1 != null ? boss1.DamageScale : 0):0.00}");
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "arena");
            Check(_last.data.Contains("boss=1"), "Krisbots HUD: „Champion im Ring!“");
            if (bh1 != null) bh1.ApplyDamage(new DamageInfo { Amount = 999999f, Source = a.gameObject });
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "arena");
            Check(s.BossesKilled == 1 && s.Revives == 4 && _last.data.Contains("revives=4") && _last.data.Contains("boss=0"),
                $"Champion besiegt: +1 Wiederbelebung (Vorrat {s.Revives}, HUD {_last.data})");

            // ---------------------------------------------------------- Tod und Wiederbelebung
            Line("");
            Line("[Liegenbleiben und Wiederbeleben]");
            Vector3 spot = Gnd(mid.x + 6f, mid.z - 12f);
            yield return Ask(b, "goto", V(spot, 0f));
            yield return Ask(a, "goto", V(spot + new Vector3(1.2f, 0f, 0f), 270f));
            safeB = false;
            yield return new WaitForSecondsRealtime(1.6f);
            hb.ApplyDamage(new DamageInfo { Amount = 999999f, Source = a.gameObject, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(8f);
            yield return Ask(b, "arena");
            Check(!hb.IsAlive && s.Down.Contains(b.ProfileId) && slot.Contains(b.transform.position) && _last.data.Contains("down=1"),
                $"Krisbot fällt und bleibt nach 8 s liegen – kein Erwachen an der Pforte ({_last.data})");

            // Unterbrechen: Maxmod wird waehrend des Wiederbelebens getroffen. Die Wellen-Gegner verlassen dafuer kurz den
            // Ring (sonst entscheidet der Zufall, wer zuerst trifft), Maxmod wird geheilt, ist aber verwundbar.
            foreach (EnemyBrain e in inst.Enemies.Where(e => e != null && e.GetComponent<Health>() is Health eh && eh != null && eh.IsAlive).ToList())
                EnemyDirector.Instance.Remove(e);
            healA = true;
            safeA = false;
            yield return new WaitForSecondsRealtime(1.6f);
            yield return StandBy(a, b);
            yield return Ask(a, "arena-revive", b.ObjectId.ToString());
            Info($"Wiederbeleben angefragt: {_last.ok} ({_last.data}), Abstand Server {Vector3.Distance(a.transform.position, b.transform.position):0.0} m");
            bool began = s.Channels.ContainsKey(a.ProfileId);
            yield return Ask(a, "arena");
            bool barShown = ParseAfter(_last.data, "channel=") >= 0f;
            yield return new WaitForSecondsRealtime(1f);
            ha.ApplyDamage(new DamageInfo { Amount = 5f, Source = boss1 != null ? boss1.gameObject : null, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(0.8f);
            Check(began && barShown && !s.Channels.ContainsKey(a.ProfileId) && !hb.IsAlive && s.Revives == 4 && ha.IsAlive && !s.Lost,
                $"Maxmod beginnt (Leiste sichtbar: {barShown}), wird getroffen – abgebrochen, Vorrat unverändert ({s.Revives})");
            safeA = true;
            healA = false;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return StandBy(a, b);
            yield return Ask(a, "arena-revive", b.ObjectId.ToString());
            float t0 = Time.time;
            yield return WaitUntil(() => hb.IsAlive, 9f);
            float took = Time.time - t0, revivedAt = hb.Fraction;
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "arena");
            Check(hb.IsAlive && Mathf.Abs(revivedAt - 0.5f) < 0.02f && s.Revives == 3 && !s.Down.Contains(b.ProfileId) && _last.data.Contains("down=0") && took > 4.3f && took < 6.5f,
                $"Nach {took:0.0} s steht Krisbot wieder – mit {revivedAt * 100f:0} % Leben, Vorrat {s.Revives} ({_last.data})");

            // Kein Vorrat mehr (2 s Schutz nach dem Aufstehen abwarten)
            yield return new WaitForSecondsRealtime(1.2f);
            s.Revives = 0;
            hb.ApplyDamage(new DamageInfo { Amount = 999999f, Source = a.gameObject, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(0.5f);
            yield return StandBy(a, b);
            yield return Ask(a, "arena-revive", b.ObjectId.ToString());
            yield return new WaitForSecondsRealtime(1f);
            Check(!s.Channels.ContainsKey(a.ProfileId) && !hb.IsAlive, "Vorrat 0: Wiederbeleben wird abgelehnt");
            s.Revives = 1;
            yield return StandBy(a, b);
            yield return Ask(a, "arena-revive", b.ObjectId.ToString());
            yield return WaitUntil(() => hb.IsAlive, 9f);
            Check(hb.IsAlive && s.Revives == 0, "Mit einer Wiederbelebung im Vorrat klappt es wieder");
            safeB = true;
            yield return new WaitForSecondsRealtime(0.5f);
            hb.Heal(hb.Max);

            // ---------------------------------------------------------- Champion 2 und Truhe
            Line("");
            Line("[Champion 10:00 und Prüfpunkt]");
            yield return WaitUntil(() => s.Bosses.Count >= 2, 60f);
            EnemyBrain boss2 = s.Bosses.Count >= 2 ? s.Bosses[1] : null;
            Check(boss2 != null, $"Bei {ColosseumInstanceHandler.Clock(s.Elapsed)}: zweiter Champion {(boss2 != null ? boss2.Definition.DisplayName : "?")}");
            long moneyB = b.GetComponent<NetworkProgression>().ServerMoney, moneyA = a.GetComponent<NetworkProgression>().ServerMoney;
            int bagB = b.ServerModel.Bag.Count(x => !x.IsEmpty);
            if (boss2 != null) boss2.GetComponent<Health>().ApplyDamage(new DamageInfo { Amount = 9999999f, Source = b.gameObject });
            yield return new WaitForSecondsRealtime(1.5f);
            int left = inst.Enemies.Count(e => e != null && e.GetComponent<Health>() is Health eh && eh != null && eh.IsAlive);
            yield return Ask(b, "arena");
            Check(s.Won && left == 0 && _last.data.Contains("result=1") && _last.data.Contains("chest=1") && s.ChestPending.Count == 2,
                $"Prüfung bestanden: Ring leer ({left} Gegner), Truhe wartet auf beide ({_last.data})");
            yield return Ask(b, "arena-chest");
            Check(s.ChestPending.Contains(b.ProfileId), "Truhe aus der Ferne: nichts");
            Vector3 chestStand = chest.transform.position + chest.transform.forward * 1.6f;
            yield return Ask(b, "goto", V(chestStand, chest.transform.eulerAngles.y + 180f));
            yield return Ask(b, "arena-chest");
            long gotB = b.GetComponent<NetworkProgression>().ServerMoney - moneyB;
            int bagB2 = b.ServerModel.Bag.Count(x => !x.IsEmpty);
            s.ChestLog.TryGetValue(b.ProfileId, out string logB);
            Check(!s.ChestPending.Contains(b.ProfileId) && gotB == handler.Definition.checkpointMoney && bagB2 == bagB + 1 && logB != null && !logB.EndsWith("item=-") && _last.data.Contains("chest=0"),
                $"Krisbots Truhe: {logB} (Geld +{gotB}, Rucksack {bagB} → {bagB2})");
            yield return Ask(b, "arena-chest");
            Check(b.GetComponent<NetworkProgression>().ServerMoney - moneyB == gotB, "Zweites Öffnen: leer");
            // Maxmod geht, ohne die Truhe zu öffnen – der Ringmeister händigt den Anteil aus
            yield return Ask(a, "goto", V(MineGround(slot.ExitWorld), DoorYaw(slot)));
            yield return WaitUntil(() => DungeonSlot.At(a.transform.position) == null, 8f);
            s.ChestLog.TryGetValue(a.ProfileId, out string logA);
            Check(a.GetComponent<NetworkProgression>().ServerMoney - moneyA == handler.Definition.checkpointMoney && logA != null,
                $"Maxmod geht ohne zu öffnen und bekommt seinen Anteil trotzdem ({logA})");
            yield return Ask(b, "goto", V(MineGround(slot.ExitWorld), DoorYaw(slot)));
            yield return WaitUntil(() => DungeonSlot.At(b.transform.position) == null, 8f);
            yield return WaitUntil(() => !inst.Active, 4f);
            Check(!inst.Active && handler.StateOf(inst) == null, "Nach dem Sieg und dem Gehen ist die Arena sofort wieder frei");

            // ---------------------------------------------------------- Wipe
            Line("");
            Line("[Wipe]");
            yield return new WaitForSecondsRealtime(3.5f); // Pforten-Sperre
            yield return EnterRing(dir, portal, a, b);
            inst = dir.InstanceOf(a.ProfileId);
            s = handler.StateOf(inst);
            Check(inst != null && s != null && !s.Started, "Neuer Lauf: wartet wieder auf die Glocke");
            if (s == null) { guard = false; yield break; }
            bell = ArenaBell.All.First(x => x.SlotIndex == inst.Slot.Index);
            yield return Ask(a, "goto", V(bell.transform.position + bell.transform.forward * 1.6f, bell.transform.eulerAngles.y + 180f));
            yield return Ask(a, "arena-bell");
            yield return WaitUntil(() => s.Started, 8f);
            safeA = safeB = false;
            yield return new WaitForSecondsRealtime(1.6f);
            hb.ApplyDamage(new DamageInfo { Amount = 999999f, Source = a.gameObject, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(0.6f);
            Check(!s.Lost, "Einer liegt, einer steht: der Lauf geht weiter");
            ha.ApplyDamage(new DamageInfo { Amount = 999999f, Source = b.gameObject, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Ask(b, "arena");
            Check(s.Lost && s.Participants.Count == 2 && _last.data.Contains("result=2"), $"Beide liegen: Wipe, „Der Ring hat euch besiegt“ ({_last.data})");
            DungeonDirector.Instance wiped = inst;
            yield return WaitUntil(() => !wiped.Active, 8f);
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "arena");
            Check(!wiped.Active && ha.IsAlive && hb.IsAlive && ha.Fraction > 0.99f && hb.Fraction > 0.99f &&
                  DungeonSlot.At(a.transform.position) == null && DungeonSlot.At(b.transform.position) == null && _last.data.Contains("visible=0"),
                $"Beide stehen voll geheilt vor dem Tor, die Arena ist wieder frei, HUD aus ({_last.data})");

            guard = false;
            DungeonDirector.ResetOverride = -1f;
            ColosseumInstanceHandler.BellCountdownOverride = -1f;
            ColosseumInstanceHandler.TimeScaleOverride = 1f;
            ColosseumInstanceHandler.SeedOverride = -1;
            ColosseumInstanceHandler.WipeDelayOverride = -1f;
        }

        /// <summary>Neben den Gefallenen stellen (wie ein Spieler, der hinlaeuft).</summary>
        private IEnumerator StandBy(NetworkInventory reviver, NetworkInventory fallen)
        {
            Vector3 p = fallen.transform.position + new Vector3(1f, 0f, 0f);
            yield return Ask(reviver, "goto", V(Gnd(p.x, p.z), 270f));
        }

        private IEnumerator EnterRing(DungeonDirector dir, DungeonPortal portal, NetworkInventory a, NetworkInventory b)
        {
            Vector3 gate = Gnd(portal.transform.position.x, portal.transform.position.z);
            yield return Ask(a, "goto", V(gate, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(a.ProfileId) != null, 6f);
            yield return Ask(b, "goto", V(gate, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(b.ProfileId) != null, 6f);
        }

        /// <summary>Heilt, ohne unverwundbar zu machen (Treffer zaehlen, toeten aber nicht).</summary>
        private static IEnumerator KeepHealed(System.Func<bool> active, System.Func<Health> who)
        {
            while (active())
            {
                Health h = who();
                if (h != null && h.IsAlive && h.Fraction < 0.9f) h.Heal(h.Max);
                yield return new WaitForSecondsRealtime(0.1f);
            }
        }

        /// <summary>Haelt die Figuren unverwundbar und heil, solange die Bedingung gilt (null = diese Figur gerade nicht).</summary>
        private static IEnumerator KeepSafe(System.Func<bool> active, params System.Func<Health>[] who)
        {
            while (active())
            {
                foreach (System.Func<Health> f in who)
                {
                    Health h = f();
                    if (h == null || !h.IsAlive) continue;
                    h.SetInvulnerableFor(0.5f);
                    if (h.Fraction < 0.8f) h.Heal(h.Max);
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }
    }
}
