using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Gameplay.Colosseum;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Quests;
using Ashveil.World;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// Kolosseum Pass 2 (Greybox): Editor-Host Maxmod + Test-Clients Krisbot und Maltebot, Stufe 15.
    /// Aufbau (6 Arenen, Tore/Bodenoeffnung/Galerie per NavMesh erreichbar, Freiluft), Vorplatz (Weg ab dem Wachtfeuer,
    /// Ringmeister, Handel), Gruppe teilt eine Arena, Solo eine eigene, Tod und Wiederbelebung an der Kaempferpforte,
    /// Verlassen und Zuruecksetzen, und die neue Instanz-Sichtbarkeit (Probe mit zwei Salzgruben-Instanzen, 300 m auseinander).
    /// Start: "Ashveil &gt; Test &gt; Kolosseum (Greybox, 3 Spieler)". Bericht: Logs/Ashveil_KolosseumTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string ArenaFlag = "kolosseum";
        private static bool _arenaMode;
        private static readonly string[] ArenaProfiles = { "arena-a", "arena-b", "arena-c" };
        private const string ArenaId = "kolosseum", MineId = "salzgruben";
        private const string NRing = "npc.ringmaster", NOda = "npc.ring_sutler", NHesk = "npc.ring_armorer";

        private static int IntField(string reply, string key)
        {
            int i = reply?.IndexOf(key + "=", System.StringComparison.Ordinal) ?? -1;
            if (i < 0) return -1;
            string v = reply.Substring(i + key.Length + 1).Split(';')[0];
            return int.TryParse(v, out int n) ? n : -1;
        }

        private IEnumerator ArenaSuite()
        {
            // ---------------------------------------------------------- Anmeldung
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(ArenaProfiles[0])) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            for (int i = 1; i < 3; i++) StartClient(exe, ArenaProfiles[i]);
            yield return WaitUntil(() => ArenaProfiles.All(p => OnlineInv(p) != null && OnlineInv(p).IsReady), 150f);
            var p3 = ArenaProfiles.Select(OnlineInv).ToList();
            Check(p3.All(x => x != null), "Spieler B und C (Test-Clients) angemeldet");
            if (p3.Any(x => x == null)) yield break;
            NetworkInventory b = p3[1], c = p3[2];
            yield return new WaitForSecondsRealtime(2f);
            Line("  ·     Besetzung: " + string.Join(", ", p3.Select(x => $"{x.CharacterName} (Stufe {x.ServerModel.Level})")));

            // ---------------------------------------------------------- Aufbau
            Line("");
            Line("[Aufbau]");
            DungeonDirector dir = DungeonDirector.For(ArenaId);
            DungeonPortal portal = DungeonPortal.Find(ArenaId);
            var slots = DungeonSlot.All.Where(s => s != null && s.DungeonId == ArenaId).OrderBy(s => s.Index).ToList();
            Check(dir != null, "Dungeon-Direktor des Kolosseums laeuft");
            Check(portal != null, "Eingang am Tor des Vorplatzes vorhanden");
            Check(slots.Count == 6 && dir != null && dir.Instances.Count == 6, $"Sechs Arena-Plaetze ({slots.Count})");
            if (dir == null || portal == null || slots.Count == 0) yield break;
            DungeonDirector mine = DungeonDirector.For(MineId);
            var mineSlots = DungeonSlot.All.Where(s => s != null && s.DungeonId == MineId).ToList();
            Check(mine != null && mineSlots.Count == 6 && DungeonPortal.Find(MineId) != null && DungeonPortal.Find(MineId) != portal,
                "Salzgruben unveraendert daneben: eigener Direktor, eigener Eingang, sechs Plaetze");
            Check(slots.All(s => !s.IsUnderground) && mineSlots.All(s => s.IsUnderground), "Arenen unter freiem Himmel, Salzgruben unter Tage");
            WorldStreamer ws = WorldStreamer.Instance;
            Check(slots.All(s => ws == null || ws.CellAt(s.transform.position) == null) &&
                  slots.All(s => mineSlots.All(m => Vector3.Distance(s.transform.position, m.transform.position) > 900f)),
                $"Arenen liegen ausserhalb aller Weltzellen und weit weg von den Salzgruben (Platz 0 bei {slots[0].transform.position:F0})");
            DungeonDirector.ResetOverride = 3f;
            // Die Grundpruefungen laufen ohne Wellen (die kommen in einem eigenen Abschnitt)
            ColosseumInstanceHandler.StartDelayOverride = 99999f;
            ColosseumInstanceHandler.TimeScaleOverride = 1f;
            ColosseumInstanceHandler.SeedOverride = -1;

            foreach (DungeonSlot s in new[] { slots[0], slots[slots.Count - 1] })
            {
                var gates = ArenaGate.All.Where(g => g != null && s.Contains(g.SpawnWorld)).ToList();
                var missing = new List<string>();
                var path = new NavMeshPath();
                bool startOk = NavMesh.SamplePosition(s.ArrivalWorld, out NavMeshHit start, 3f, NavMesh.AllAreas);
                var targets = new List<(string, Vector3)>();
                foreach (ArenaGate g in gates)
                {
                    targets.Add((g.DisplayName, g.SpawnWorld));
                    targets.Add((g.DisplayName + " (Einlauf)", g.EntryWorld));
                }
                targets.Add(("Kaempferpforte (Ausgang)", s.ExitWorld));
                foreach ((string name, Vector3 target) in targets)
                {
                    bool ok = startOk && NavMesh.SamplePosition(target, out NavMeshHit h, 2.5f, NavMesh.AllAreas) &&
                              NavMesh.CalculatePath(start.position, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    if (!ok) missing.Add(name);
                }
                int nGate = gates.Count(g => g.Kind == ArenaGateKind.Gate), nFloor = gates.Count(g => g.Kind == ArenaGateKind.FloorOpening),
                    nGal = gates.Count(g => g.Kind == ArenaGateKind.Gallery);
                Check(nGate == 4 && nFloor == 1 && nGal == 2, $"Platz {s.Index}: vier Tore, Bodenoeffnung, zwei Galerie-Punkte ({nGate}/{nFloor}/{nGal})");
                Check(missing.Count == 0, $"Platz {s.Index}: alle {targets.Count} Punkte von der Kaempferpforte aus erreichbar (NavMesh)" +
                    (missing.Count > 0 ? " – fehlt: " + string.Join(", ", missing) : ""));
            }
            Check(slots.All(s => EnemyBrain.All.Count(e => e != null && s.Contains(e.transform.position)) == 0), "Vor dem Betreten stehen keine Gegner in den Arenen");

            // Vorplatz: Leute, Ebene, Weg vom Wachtfeuer
            NpcIdentity ring = NpcIdentity.Find(NRing), oda = NpcIdentity.Find(NOda), hesk = NpcIdentity.Find(NHesk);
            Check(ring != null && oda != null && hesk != null &&
                  new[] { ring, oda, hesk }.All(n => Vector3.Distance(n.transform.position, portal.transform.position) < 45f),
                "Ringmeister Brannoc, Marketenderin Oda und Waffenwart Hesk stehen am Vorplatz");
            var heights = new List<float>();
            for (int ix = -2; ix <= 2; ix++)
            for (int iz = -3; iz <= 3; iz++)
                heights.Add(Ashveil.Core.WorldGround.SampleHeight(new Vector3(490f + ix * 8f, 0f, 285f + iz * 8f), 0f));
            float spread = heights.Max() - heights.Min();
            Check(spread < 0.8f, $"Vorplatz ist eben ({spread:0.00} m Unterschied auf 32 × 48 m)");
            Vector3 fire = Gnd(0f, 20f);
            Vector3 front = Gnd(portal.ReturnWorld.x, portal.ReturnWorld.z);
            var campPath = new NavMeshPath();
            bool reach = NavMesh.SamplePosition(fire, out NavMeshHit f0, 6f, NavMesh.AllAreas) && NavMesh.SamplePosition(front, out NavMeshHit f1, 4f, NavMesh.AllAreas) &&
                         NavMesh.CalculatePath(f0.position, f1.position, NavMesh.AllAreas, campPath) && campPath.status == NavMeshPathStatus.PathComplete;
            float len = 0f;
            for (int i = 1; i < campPath.corners.Length; i++) len += Vector3.Distance(campPath.corners[i - 1], campPath.corners[i]);
            Check(reach, $"Vom Wachtfeuer zu Fuss zum Ring erreichbar ({len:0} m Weg, etwa {len / 7f / 60f:0.0} min laufen)");

            // ---------------------------------------------------------- Wellenplan (ohne Netz)
            yield return WavePlanChecks(dir, slots[0]);

            // ---------------------------------------------------------- Vorplatz
            Line("");
            Line("[Vorplatz]");
            yield return GotoNpc(a, NRing);
            yield return Ask(a, "npc-talk", NRing);
            Check(_last.ok, "Maxmod spricht mit Ringmeister Brannoc");
            var pb = b.GetComponent<NetworkProgression>();
            yield return GotoNpc(b, NOda);
            yield return Ask(b, "npc-talk", NOda);
            pb.ServerSetMoney(5000);
            long money0 = pb.ServerMoney;
            int potions0 = CountOf(b, "item.strong_healing_draught");
            yield return Ask(b, "buy", "1");
            Check(_last.ok && pb.ServerMoney < money0 && CountOf(b, "item.strong_healing_draught") > potions0,
                $"Krisbot kauft bei Oda einen starken Heiltrank ({money0 - pb.ServerMoney} Kupfer)");
            yield return GotoNpc(c, NHesk);
            yield return Ask(c, "npc-talk", NHesk);
            Check(_last.ok, "Maltebot spricht mit Waffenwart Hesk (Handel)");

            // ---------------------------------------------------------- Gruppe betritt den Ring
            Line("");
            Line("[Gruppe betritt den Ring]");
            Vector3 gate = Gnd(portal.transform.position.x, portal.transform.position.z);
            yield return Ask(a, "goto", V(front, portal.ReturnYaw));
            yield return Ask(b, "goto", V(front + Vector3.forward * 2f, portal.ReturnYaw));
            yield return Ask(c, "goto", V(front - Vector3.forward * 2f, portal.ReturnYaw));
            yield return Ask(a, "party-invite", b.CharacterName);
            yield return Ask(b, "party-respond", "1");
            yield return new WaitForSecondsRealtime(1f);
            Check(PartyService.Of(a.ProfileId) != null && PartyService.Of(a.ProfileId) == PartyService.Of(b.ProfileId), "Maxmod und Krisbot bilden eine Gruppe");

            yield return Ask(a, "goto", V(gate, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(a.ProfileId) != null, 6f);
            yield return Ask(b, "goto", V(gate, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(b.ProfileId) != null, 6f);
            DungeonDirector.Instance ia = dir.InstanceOf(a.ProfileId), ib = dir.InstanceOf(b.ProfileId);
            Check(ia != null, "Maxmod ist nach dem Tor in einer Arena");
            Check(ia != null && ia == ib, $"Krisbot landet in derselben Arena (Platz {(ia != null ? ia.Slot.Index : -1)})");
            if (ia == null) yield break;
            DungeonSlot groupSlot = ia.Slot;
            yield return new WaitForSecondsRealtime(1.5f);
            Check(Vector3.Distance(a.transform.position, groupSlot.ArrivalWorld) < 5f && Vector3.Distance(b.transform.position, groupSlot.ArrivalWorld) < 5f,
                "Beide stehen vor der Kaempferpforte im Sand");
            yield return Ask(b, "indoor");
            Check(_last.data.StartsWith("indoor=0;sun=1"), $"Krisbot: freier Himmel, Sonne an ({_last.data})");
            yield return Ask(a, "indoor");
            Check(_last.data.StartsWith("indoor=0;sun=1"), $"Maxmod (Host): freier Himmel ({_last.data})");
            yield return new WaitForSecondsRealtime(1.5f);
            Check(a.GetComponent<NetworkMotion>().HasRespawnPoint && b.GetComponent<NetworkMotion>().HasRespawnPoint,
                "Wiederbelebungspunkt bleibt in der Arena gesetzt (der Salzgruben-Direktor loescht ihn nicht mehr)");

            Line("");
            Line("[Maltebot allein]");
            yield return Ask(c, "goto", V(gate, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(c.ProfileId) != null, 6f);
            DungeonDirector.Instance ic = dir.InstanceOf(c.ProfileId);
            Check(ic != null && ic != ia, $"Maltebot bekommt eine eigene Arena (Platz {(ic != null ? ic.Slot.Index : -1)})");
            Check(dir.Instances.Count(i => i.Active) == 2, $"Zwei Arenen belegt, {dir.FreeSlots} frei");
            yield return new WaitForSecondsRealtime(2f);
            yield return Ask(b, "players");
            int seenB = IntField(_last.data, "players");
            yield return Ask(c, "players");
            int seenC = IntField(_last.data, "players");
            Check(seenB == 2 && seenC == 1, $"Sicht: Krisbot sieht sich und Maxmod ({seenB}), Maltebot in der Nachbar-Arena nur sich ({seenC})");

            // ---------------------------------------------------------- Tod
            Line("");
            Line("[Tod und Wiederbelebung]");
            var hb = b.GetComponent<Health>();
            hb.ApplyDamage(new DamageInfo { Amount = hb.Max * 5f });
            yield return WaitUntil(() => !hb.IsAlive, 3f);
            Check(!hb.IsAlive, "Krisbot faellt im Ring (Testschaden)");
            yield return WaitUntil(() => hb.IsAlive, 20f);
            yield return new WaitForSecondsRealtime(1f);
            float toArrival = Vector3.Distance(b.transform.position, groupSlot.ArrivalWorld);
            Check(hb.IsAlive && toArrival < 5f, $"Krisbot erwacht an der Kaempferpforte ihrer Arena ({toArrival:0.0} m), nicht am Wachtfeuer");
            Check(dir.InstanceOf(b.ProfileId) == ia, "Krisbot ist weiter in ihrer Arena");

            // ---------------------------------------------------------- Verlassen und Zuruecksetzen
            Line("");
            Line("[Verlassen und Zuruecksetzen]");
            yield return Ask(a, "goto", V(MineGround(groupSlot.ExitWorld), DoorYaw(groupSlot)));
            yield return WaitUntil(() => dir.InstanceOf(a.ProfileId) == null && DungeonSlot.At(a.transform.position) == null, 6f);
            yield return new WaitForSecondsRealtime(1f);
            float toFront = Vector3.Distance(a.transform.position, portal.ReturnWorld);
            Check(DungeonSlot.At(a.transform.position) == null && toFront < 5f, $"Maxmod verlaesst den Ring durch die Kaempferpforte und steht auf dem Vorplatz ({toFront:0.0} m)");
            Check(!a.GetComponent<NetworkMotion>().HasRespawnPoint, "Draussen gilt wieder das Wachtfeuer");
            Check(ia.Active, "Die Arena bleibt, solange Krisbot drin ist");
            yield return Ask(b, "goto", V(MineGround(groupSlot.ExitWorld), DoorYaw(groupSlot)));
            yield return WaitUntil(() => DungeonSlot.At(b.transform.position) == null, 6f);
            yield return WaitUntil(() => !ia.Active, 10f);
            Check(!ia.Active, "Leere Arena wird zurueckgesetzt (Test: nach 3 s, sonst 5 min)");
            if (ic != null)
            {
                yield return Ask(c, "goto", V(MineGround(ic.Slot.ExitWorld), DoorYaw(ic.Slot)));
                yield return WaitUntil(() => DungeonSlot.At(c.transform.position) == null, 6f);
            }
            yield return WaitUntil(() => dir.Instances.All(i => !i.Active), 10f);
            Check(dir.Instances.All(i => !i.Active) && dir.FreeSlots == 6, "Alle sechs Arenen wieder frei");

            // ---------------------------------------------------------- Wellen im Ring
            yield return WaveRunChecks(dir, portal, a, b, c);

            // ---------------------------------------------------------- Instanz-Sichtbarkeit (Probe: Salzgruben)
            Line("");
            Line("[Sichtbarkeit je Instanz (Probe mit zwei Salzgruben-Instanzen)]");
            DungeonPortal minePortal = DungeonPortal.Find(MineId);
            Vector3 mouth = Gnd(minePortal.transform.position.x, minePortal.transform.position.z);
            Vector3 mineFront = Gnd(minePortal.ReturnWorld.x, minePortal.ReturnWorld.z);
            yield return Ask(a, "goto", V(mineFront, minePortal.ReturnYaw));
            yield return Ask(b, "goto", V(mineFront + Vector3.right * 2f, minePortal.ReturnYaw));
            yield return Ask(c, "goto", V(mineFront - Vector3.right * 2f, minePortal.ReturnYaw));
            yield return new WaitForSecondsRealtime(3.5f);
            foreach (NetworkInventory p in new[] { a, b, c })
            {
                yield return Ask(p, "goto", V(mouth, minePortal.ReturnYaw + 180f));
                NetworkInventory who = p;
                yield return WaitUntil(() => mine.InstanceOf(who.ProfileId) != null, 6f);
            }
            DungeonDirector.Instance ma = mine.InstanceOf(a.ProfileId), mc = mine.InstanceOf(c.ProfileId);
            Check(ma != null && mc != null && ma != mc && mine.InstanceOf(b.ProfileId) == ma, "Gruppe und Maltebot in zwei Salzgruben-Instanzen");
            if (ma != null && mc != null)
            {
                float apart = Vector3.Distance(ma.Slot.transform.position, mc.Slot.transform.position);
                Info($"Die beiden Instanzen liegen {apart:0} m auseinander – innerhalb der 500 m Gegner-Sichtweite");
                yield return new WaitForSecondsRealtime(3f);
                int aliveB = EnemyBrain.All.Count(e => e != null && ma.Slot.Contains(e.transform.position) && e.GetComponent<Health>().IsAlive);
                int aliveC = EnemyBrain.All.Count(e => e != null && mc.Slot.Contains(e.transform.position) && e.GetComponent<Health>().IsAlive);
                // Diagnose (Server): wer beobachtet welche Gegner?
                FishNet.Connection.NetworkConnection connB = b.GetComponent<FishNet.Object.NetworkObject>().Owner;
                var enemiesAll = Object.FindObjectsByType<NetworkEnemy>(FindObjectsSortMode.None);
                int obsSame = 0, obsOther = 0, obsOpen = 0, withObserver = 0, withCond = 0;
                foreach (NetworkEnemy e in enemiesAll)
                {
                    var nob = e.GetComponent<FishNet.Object.NetworkObject>();
                    var no = e.GetComponent<FishNet.Observing.NetworkObserver>();
                    if (no != null) withObserver++;
                    if (no != null && no.GetObserverCondition<EnemyInterestCondition>() != null) withCond++;
                    if (nob == null || !nob.Observers.Contains(connB)) continue;
                    DungeonSlot es = DungeonSlot.At(e.transform.position);
                    if (es == null) obsOpen++; else if (es == ma.Slot) obsSame++; else obsOther++;
                }
                Info($"Server: {enemiesAll.Length} Gegner, {withObserver} mit NetworkObserver, {withCond} mit Instanz-Regel; Krisbot beobachtet same={obsSame} other={obsOther} open={obsOpen}");
                yield return Ask(b, "enemies");
                int bSame = IntField(_last.data, "same"), bOther = IntField(_last.data, "other");
                Check(bSame >= aliveB - 1 && bSame > 0 && bOther == 0, $"Krisbot bekommt nur die Gegner ihrer Instanz ({_last.data}, Server: {aliveB})");
                yield return Ask(c, "enemies");
                int cSame = IntField(_last.data, "same"), cOther = IntField(_last.data, "other");
                Check(cSame >= aliveC - 1 && cSame > 0 && cOther == 0, $"Maltebot bekommt nur die Gegner seiner Instanz ({_last.data}, Server: {aliveC})");
                yield return Ask(c, "players");
                Check(IntField(_last.data, "players") == 1, $"Maltebot sieht die Gruppe in der Nachbar-Instanz nicht ({_last.data})");
                yield return Ask(b, "players");
                Check(IntField(_last.data, "players") == 2, $"Krisbot sieht ihre Gruppe ({_last.data})");

                foreach ((NetworkInventory p, DungeonDirector.Instance inst) in new[] { (a, ma), (b, ma), (c, mc) })
                {
                    yield return Ask(p, "goto", V(MineGround(inst.Slot.ExitWorld), 180f));
                    NetworkInventory who = p;
                    yield return WaitUntil(() => DungeonSlot.At(who.transform.position) == null, 6f);
                }
                yield return WaitUntil(() => mine.Instances.All(i => !i.Active), 12f);
                Check(mine.Instances.All(i => !i.Active), "Salzgruben danach wieder frei");
            }
            DungeonDirector.ResetOverride = -1f;
            ColosseumInstanceHandler.StartDelayOverride = -1f;
            ColosseumInstanceHandler.TimeScaleOverride = 1f;
            ColosseumInstanceHandler.SeedOverride = -1;
        }

        /// <summary>Blick in den Gang der Kaempferpforte (vom Sand weg).</summary>
        private static float DoorYaw(DungeonSlot s)
        {
            Vector3 d = s.ExitWorld - s.transform.position;
            d.y = 0f;
            return Quaternion.LookRotation(d).eulerAngles.y;
        }
    }
}
