using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Mounts;
using Ashveil.World;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Klassenreittier des Eidwaechters (Class Identity Masterplan Phase 2, Max 28.09.): Editor-Host Maxmod + Test-Client
    /// Krisbot (Eidwaechterin). Freischalten, Leiste, Aufsitzen mit Abbruch, +40 % Tempo, kein Sprint/Rolle im Sattel,
    /// Bindung statt Laufkunst, Absitzen bei Treffer/Angriff/Tod/Teleport, kein Reiten im Kampf oder in Instanzen,
    /// Sichtbarkeit fuer andere, Speichern. Start: "Ashveil &gt; Test &gt; Reittier (2 Spieler)". Bericht: Logs/Ashveil_ReittierTest.txt
    /// Seit 09.10.2026: Wilde Gegner, die Krisbot ins Visier nehmen, verlassen waehrend der Suite die Welt (sonst haelt etwa ein
    /// Russluchs Krisbot „im Kampf“ und verfaelscht die Regeln); neu „Gegner gibt auf“ (Ziel springt weit weg, EnemyBrain).
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string MountFlag = "reittier";
        private static bool _mountMode;

        private IEnumerator MountSuite()
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

            NetworkProgression bp = b.GetComponent<NetworkProgression>();
            CharacterMotor bm = b.GetComponent<CharacterMotor>();
            NetworkMotion bn = b.GetComponent<NetworkMotion>();
            Health bh = b.GetComponent<Health>();
            ClassMountDefinition horse = MountCatalog.Warhorse;
            // Wilde Gegner in Kareth sollen die Messungen nicht stoeren (ein Treffer wirft aus dem Sattel – das pruefen wir gezielt)
            bool safe = true, guard = true;
            StartCoroutine(KeepSafe(() => guard, () => safe ? bh : null));
            // 09.10.2026: Wilde Gegner, die Krisbot ins Visier nehmen, halten ihn „im Kampf“ (kein Aufsitzen) und verfaelschen damit
            // die Regeln, die hier geprueft werden. Sie verlassen die Welt wie gezaehmt (ihr Gebiet belebt sie spaeter wieder);
            // der eigene Uebungsgegner bleibt.
            EnemyBrain uebung = null;
            StartCoroutine(WildeGegnerFernhalten(() => guard, bh, () => uebung));
            string bond = horse.bond.id;

            // ---------------------------------------------------------- Katalog
            Line("");
            Line("[Katalog]");
            Check(MountCatalog.ForClass(MountCatalog.WardenClassId) == horse && MountCatalog.ForClass("class.ashstalker") == null && MountCatalog.ForClass("class.emberweaver") == null,
                "Nur der Eidwächter hat ein Klassenreittier (Kriegspferd)");
            Check(Mathf.Approximately(MountCatalog.SpeedBonus(horse, 0), 0.4f) && Mathf.Abs(MountCatalog.SpeedBonus(horse, 100) - 0.5f) < 0.001f &&
                  Mathf.Approximately(MountCatalog.MountSeconds(horse, 0), 1.5f) && Mathf.Abs(MountCatalog.MountSeconds(horse, 100) - 0.75f) < 0.001f,
                $"+40 % Tempo, Bindung bis +50 %; Aufsitzen 1,5 s, mit voller Bindung 0,75 s (Rang 25 nach {horse.bond.TotalFor(25) / 1000:0} km)");
            Check(MasteryCatalog.Get(bond) == horse.bond, "Bindung läuft über die Meisterschafts-Technik");

            // ---------------------------------------------------------- Freischalten
            Line("");
            Line("[Freischalten]");
            bp.ServerRemoveMount();
            bp.ServerSetMasteryPoints(bond, 0);
            bp.ServerSetMasteryPoints(MasteryCatalog.RunningId, 0);
            Vector3 start = Gnd(470f, 110f);
            bn.ServerTeleport(start, 0f);
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Ask(b, "mount");
            yield return new WaitForSecondsRealtime(0.5f);
            Check(!bp.ServerMounted && !bp.ServerMounting && bp.ServerMountReject.Contains("kein Reittier"), $"Ohne Reittier: „{bp.ServerMountReject}“");
            string cls = b.LootClass != null ? b.LootClass.DisplayName : "?";
            string why = bp.ServerUnlockMount(MountCatalog.WarhorseId);
            yield return new WaitForSecondsRealtime(1f);
            var bar = b.GetComponent<NetworkActionBar>();
            bool onBar = bar != null && bar.ServerSlots.Any(e => e.type == ActionEntryType.Mount && e.referenceId == MountCatalog.WarhorseId);
            yield return Ask(b, "mountstate");
            Check(why == "" && bp.ServerHasMount && onBar && _last.ok && _last.data.Contains("has=1") && _last.data.Contains("name=Kriegspferd"),
                $"Krisbot ({cls}) bekommt das Kriegspferd, es liegt auf der Leiste ({_last.data})");

            // ---------------------------------------------------------- Aufsitzen mit Abbruch
            Line("");
            Line("[Aufsitzen]");
            yield return Ask(b, "mount");
            bool casting = bp.ServerMounting;
            bool bar1 = _last.data.Contains("castLabel=Kriegspferd rufen") && !_last.data.Contains("cast=-1");
            yield return Ask(b, "walk", "0.8 1");
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Ask(b, "mountstate");
            Check(casting && bar1 && !bp.ServerMounting && !bp.ServerMounted && _last.data.Contains("interrupted=1"),
                $"Aufsitzen beginnt (Zauberleiste „Kriegspferd rufen“), Loslaufen bricht ab ({_last.data})");
            bn.ServerTeleport(start, 0f);
            yield return new WaitForSecondsRealtime(2f);
            float t0 = Time.time;
            yield return Ask(b, "mount");
            yield return WaitUntil(() => bp.ServerMounted, 4f);
            float took = Time.time - t0;
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Ask(b, "mountstate");
            string mine = _last.data;
            yield return Ask(a, "mountseen", b.ObjectId.ToString());
            var hostView = b.GetComponent<MountVisual>();
            Check(bp.ServerMounted && took > 1.2f && took < 2.4f && bm.IsMounted && mine.Contains("mounted=1") && mine.Contains("motor=1") && mine.Contains("visible=1") &&
                  hostView != null && hostView.Visible,
                $"Nach {took:0.0} s im Sattel – Server, Krisbots Vorhersage und Maxmods Bildschirm zeigen das Pferd ({mine})");

            // ---------------------------------------------------------- Tempo und Regeln im Sattel
            Line("");
            Line("[Im Sattel]");
            double run0 = bp.ServerMasteryPoints(MasteryCatalog.RunningId), bond0 = bp.ServerMasteryPoints(bond), ridden0 = bp.ServerMetersRidden;
            Vector3 s0 = b.transform.position;
            int maxGait = 0;
            bool done = false;
            StartCoroutine(Flag(Ask(b, "walk", "6 1"), () => done = true));
            while (!done) { if (hostView != null) maxGait = Mathf.Max(maxGait, hostView.Gait); yield return null; }
            yield return new WaitForSecondsRealtime(1.5f);
            float dRide = Flat(b.transform.position - s0);
            double bondGain = bp.ServerMasteryPoints(bond) - bond0, runGain = bp.ServerMasteryPoints(MasteryCatalog.RunningId) - run0;
            Check(bp.ServerMounted && bondGain > dRide * 0.8f && runGain < 1.0 && bp.ServerMetersRidden - ridden0 > dRide * 0.8f,
                $"{dRide:0} m geritten: Bindung +{bondGain:0}, Laufkunst +{runGain:0.0} (Laufkunst zählt nur zu Fuß)");
            Check(maxGait == 3, $"Im vollen Tempo galoppiert das Pferd (Gangart {maxGait})");

            float stamina0 = bm.State.Stamina;
            yield return Ask(b, "dodge");
            Check(bp.ServerMounted && bm.State.DodgeCooldown <= 0.01f && bm.State.Stamina >= stamina0 - 0.1f, "Im Sattel keine Hechtrolle (keine Ausdauer, keine Abklingzeit)");

            // Zu Fuss dieselbe Zeit zum Vergleich
            yield return Ask(b, "mount");
            yield return new WaitForSecondsRealtime(0.8f);
            Check(!bp.ServerMounted && !bm.IsMounted, "Nochmal die Leiste: absitzen");
            bn.ServerTeleport(start, 0f);
            yield return new WaitForSecondsRealtime(2f);
            s0 = b.transform.position;
            yield return Ask(b, "walk", "6 1");
            yield return new WaitForSecondsRealtime(1.5f);
            float dFoot = Flat(b.transform.position - s0);
            float ratio = dRide / Mathf.Max(1f, dFoot);
            Check(ratio > 1.3f && ratio < 1.46f, $"Gleiche 6 s: zu Fuß {dFoot:0.0} m, im Sattel {dRide:0.0} m (×{ratio:0.00}, Ziel ×1,40)");
            Line("      (Kampfzustand nach dem Laufen: " + KampfZustand(bp, bh) + ")");   // 09.10.2026: Diagnose für die Abweichung seit r2.1

            // ---------------------------------------------------------- Treffer, Kampf, Angriff
            Line("");
            Line("[Kampf]");
            EnemySpawnArea area = Object.FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Definition != null && x.Definition.Id == "enemy.salt_brute" && x.Prefab != null);
            EnemyBrain brute = area != null ? EnemyDirector.Instance.SpawnAt(area.Prefab, start + new Vector3(0f, 0f, 14f), 180f, 13, "",
                br => br.SetAggroProfile(AggroProfile.Passive), 5f) : null;
            Check(brute != null, "Übungsgegner (Grubenschrat, betäubt) erzeugt");
            uebung = brute;
            if (brute != null) brute.ServerStun(120f);
            bn.ServerTeleport(start, 0f);
            yield return new WaitForSecondsRealtime(2f);
            yield return Ask(b, "mount");
            yield return WaitUntil(() => bp.ServerMounted, 4f);
            bool wasUp = bp.ServerMounted;
            string warum1 = wasUp ? "" : " – Aufsitzen vorher abgelehnt: „" + bp.ServerMountReject + "“, " + KampfZustand(bp, bh);
            safe = false;
            yield return new WaitForSecondsRealtime(0.6f); // Schutz ablaufen lassen
            if (brute != null) bh.ApplyDamage(new DamageInfo { Amount = 2f, Source = brute.gameObject, Direction = Vector3.back, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Ask(b, "mountstate");
            Check(wasUp && !bp.ServerMounted && _last.data.Contains("mounted=0") && _last.data.Contains("motor=0"), $"Ein Gegnertreffer wirft Krisbot aus dem Sattel ({_last.data}){warum1}");
            yield return Ask(b, "mount");
            yield return new WaitForSecondsRealtime(0.4f);
            Check(!bp.ServerMounting && !bp.ServerMounted && bp.ServerMountReject.Contains("Kampf"), $"Im Kampf kein Aufsitzen: „{bp.ServerMountReject}“");
            safe = true;

            // Aufsitzen, dann selbst angreifen
            yield return WaitUntil(() => !bp.ServerInCombat, 8f);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Ask(b, "mount");
            yield return WaitUntil(() => bp.ServerMounted, 4f);
            bool up2 = bp.ServerMounted;
            string warum2 = up2 ? "" : " – Aufsitzen abgelehnt: „" + bp.ServerMountReject + "“, " + KampfZustand(bp, bh);
            if (brute != null)
            {
                bn.ServerTeleport(brute.transform.position + brute.transform.forward * 2f, brute.transform.eulerAngles.y + 180f);
                yield return new WaitForSecondsRealtime(1.2f);
                Check(up2 && !bp.ServerMounted, "Teleport (Instanzwechsel, Wiederbeleben …) setzt ab" + warum2);
                yield return WaitUntil(() => !bp.ServerInCombat, 8f);
                yield return Ask(b, "mount");
                yield return WaitUntil(() => bp.ServerMounted, 4f);
                bool up3 = bp.ServerMounted;
                string warum3 = up3 ? "" : " – Aufsitzen abgelehnt: „" + bp.ServerMountReject + "“, " + KampfZustand(bp, bh);
                yield return Ask(b, "attack", brute.GetComponent<FishNet.Object.NetworkObject>().ObjectId.ToString());
                yield return new WaitForSecondsRealtime(2f);
                yield return Ask(b, "stop");
                Check(up3 && !bp.ServerMounted, "Wer im Sattel angreift, sitzt ab" + warum3);
                EnemyDirector.Instance.Remove(brute);
            }

            // ---------------------------------------------------------- Instanz und Tod
            Line("");
            Line("[Instanz und Tod]");
            yield return WaitUntil(() => !bp.ServerInCombat, 8f);
            DungeonSlot ring = DungeonSlot.All.FirstOrDefault(sl => sl != null && sl.DungeonId == "kolosseum" && sl.Index == 5);
            if (ring != null)
            {
                bn.ServerTeleport(ring.ArrivalWorld + Vector3.up * 0.3f, ring.ArrivalYaw);
                yield return new WaitForSecondsRealtime(2f);
                yield return Ask(b, "mount");
                yield return new WaitForSecondsRealtime(0.4f);
                Check(!bp.ServerMounting && !bp.ServerMounted && bp.ServerMountReject.Contains("nicht reiten"), $"Im Ring (Instanz) kein Reittier: „{bp.ServerMountReject}“" + (bp.ServerMountReject.Contains("nicht reiten") ? "" : " – " + KampfZustand(bp, bh)));
                bn.ServerTeleport(start, 0f);
                yield return new WaitForSecondsRealtime(2f);
            }
            yield return Ask(b, "mount");
            yield return WaitUntil(() => bp.ServerMounted, 4f);
            bool up4 = bp.ServerMounted;
            string warum4 = up4 ? "" : " – Aufsitzen abgelehnt: „" + bp.ServerMountReject + "“, " + KampfZustand(bp, bh);
            safe = false;
            yield return new WaitForSecondsRealtime(0.6f);
            bh.ApplyDamage(new DamageInfo { Amount = 999999f, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(0.5f);
            Check(up4 && !bh.IsAlive && !bp.ServerMounted, "Wer im Sattel stirbt, liegt ohne Pferd am Boden" + warum4);
            yield return WaitUntil(() => bh.IsAlive, 10f);
            safe = true;
            yield return new WaitForSecondsRealtime(1f);

            // ---------------------------------------------------------- Gegner gibt auf (09.10.2026, Max: „Ja, einbauen“)
            Line("");
            Line("[Gegner gibt auf]");
            if (ring != null && area != null)
            {
                bn.ServerTeleport(start, 0f);
                yield return new WaitForSecondsRealtime(2f);
                EnemyBrain jaeger = EnemyDirector.Instance.SpawnAt(area.Prefab, start + new Vector3(0f, 0f, 6f), 180f, 13, "",
                    br => br.SetAggroProfile(AggroProfile.Aggressive), 5f);
                uebung = jaeger;
                yield return WaitUntil(() => jaeger != null && jaeger.Target == bh && jaeger.InCombat, 6f);
                bool gepackt = jaeger != null && jaeger.Target == bh && jaeger.InCombat;
                bn.ServerTeleport(ring.ArrivalWorld + Vector3.up * 0.3f, ring.ArrivalYaw);
                yield return new WaitForSecondsRealtime(1.5f);
                bool laesstAb = jaeger == null || !(jaeger.IsActive && jaeger.InCombat && jaeger.Target == bh);
                yield return WaitUntil(() => !bp.ServerInCombat, 6f);
                Check(gepackt && laesstAb && !bp.ServerInCombat,
                    $"Springt das Ziel weit weg (Ring), gibt der Gegner auf und Krisbot ist nicht mehr im Kampf (Gegner hatte ihn: {(gepackt ? "ja" : "nein")} · {KampfZustand(bp, bh)})");
                bn.ServerTeleport(start, 0f);
                yield return new WaitForSecondsRealtime(1f);
                if (jaeger != null) EnemyDirector.Instance.Remove(jaeger);
                uebung = null;
            }
            else Check(false, "Gegner gibt auf: Ring oder Gegnergebiet fehlt");

            // ---------------------------------------------------------- Bindung und Speichern
            Line("");
            Line("[Bindung und Speichern]");
            bn.ServerTeleport(start, 0f);
            yield return new WaitForSecondsRealtime(2f);
            yield return WaitUntil(() => !bp.ServerInCombat, 8f);
            bp.ServerSetMasteryPoints(bond, horse.bond.TotalFor(50) + 1);
            yield return new WaitForSecondsRealtime(0.5f);
            t0 = Time.time;
            yield return Ask(b, "mount");
            yield return WaitUntil(() => bp.ServerMounted, 4f);
            took = Time.time - t0;
            float want = MountCatalog.SpeedBonus(horse, 50);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Ask(b, "mountstate");
            Check(bp.ServerMounted && Mathf.Abs(bm.State.MountSpeed - want) < 0.001f && _last.data.Contains("speed=" + want.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)) &&
                  took < 1.45f,
                $"Bindung 50: +{want * 100f:0.0} % Tempo, Aufsitzen in {took:0.0} s ({_last.data})");
            b.SaveNow();
            yield return new WaitForSecondsRealtime(0.5f);
            bool loaded = CharacterStore.Service.TryLoad(ArenaProfiles[1], out CharacterSaveData saved);
            Check(loaded && saved.version == CharacterSaveData.CurrentVersion && saved.mount != null && saved.mount.id == MountCatalog.WarhorseId &&
                  saved.mastery.Any(m => m.id == bond && m.points > horse.bond.TotalFor(50)) && saved.lifetime.metersRidden > 10,
                $"Speicherstand v{(loaded ? saved.version : 0)}: Reittier, Bindung und {(loaded ? saved.lifetime.metersRidden : 0):0} m im Sattel");

            // Aufraeumen
            bp.ServerRemoveMount();
            bp.ServerSetMasteryPoints(bond, 0);
            yield return new WaitForSecondsRealtime(0.5f);
            if (bar != null)
            {
                ActionBarEntry[] slots = bar.ServerSlots;
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i].type == ActionEntryType.Mount) bar.ClearServer(i / ActionBarLayout.SlotsPerPage, i % ActionBarLayout.SlotsPerPage);
            }
            b.SaveNow();
            guard = false;
            Check(!bp.ServerHasMount && !bp.ServerMounted, "Ausgangslage wiederhergestellt");
        }

        /// <summary>
        /// Test (09.10.2026): Wilde Gegner, die die Figur ins Visier nehmen, verlassen die Welt wie gezaehmt (ihr Gebiet belebt sie
        /// spaeter wieder) – ausser dem eigenen Uebungsgegner.
        /// </summary>
        private IEnumerator WildeGegnerFernhalten(System.Func<bool> aktiv, Health ziel, System.Func<EnemyBrain> ausser)
        {
            var weg = new List<EnemyBrain>();
            while (aktiv())
            {
                weg.Clear();
                EnemyBrain eigener = ausser();
                foreach (EnemyBrain e in EnemyBrain.All)
                    if (e != null && e != eigener && e.Target == ziel) weg.Add(e);
                foreach (EnemyBrain e in weg)
                {
                    Line("      (wilder Gegner aus dem Weg genommen: " + e.name + ")");
                    EnemyDirector.Instance.ServerRemoveTamed(e);
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        /// <summary>Diagnose (09.10.2026): Kampfzustand des Servers und Gegner, die die Figur gerade als Ziel haben.</summary>
        private static string KampfZustand(NetworkProgression p, Health h)
        {
            var sb = new StringBuilder();
            sb.Append(p.ServerInCombat ? "im Kampf" : "nicht im Kampf").Append(" (").Append(p.ServerCombatReason).Append(')');
            int n = 0;
            foreach (EnemyBrain e in EnemyBrain.All)
            {
                if (e == null || e.Target != h) continue;
                if (n++ >= 4) { sb.Append(" …"); break; }
                sb.Append(" · Ziel von ").Append(e.name).Append(e.InCombat ? " (kämpft)" : "").Append(e.IsActive ? "" : " (ruht)").Append(' ')
                  .Append(Vector3.Distance(e.transform.position, h.transform.position).ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append(" m");
            }
            return sb.ToString();
        }
    }
}
