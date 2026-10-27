using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.World;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// Abnahmetest M6.1 „Die Salzgruben“ mit drei echten Spielern: Maxmod (Editor-Host) und Krisbot bilden eine
    /// Gruppe, Maltebot geht allein. Geprueft: Aufbau aller Instanz-Plaetze, eigene Instanz je Gruppe, Gegner nur in
    /// belegten Instanzen, Stimmung unter Tage, Kampf ohne Wiederkehr, Wiederbelebung am Eingang, Verlassen,
    /// Zuruecksetzen leerer Instanzen und frische Gegner beim erneuten Betreten.
    /// Start: "Ashveil &gt; Test &gt; Salzgruben (Instanzen, 3 Spieler)". Bericht: Logs/Ashveil/Ashveil_SalzgrubenTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private static readonly string[] MineProfiles = { "mine-a", "mine-b", "mine-c" };
        private const string NVela = "npc.vela_salk", NTobbe = "npc.tobbe";
        private const string QShift = "quest.the_last_shift", QShip = "quest.ship_in_the_mountain", QHeart = "quest.what_beats_in_the_heart",
                             QSalt = "quest.mine_salt";
        public const string MineFlag = "salzgruben";
        private static bool _mineMode;

        private static Vector3 MineGround(Vector3 p, float lift = 0.3f)
        {
            int mask = ~LayerMask.GetMask(Ashveil.Core.GameLayers.Player, Ashveil.Core.GameLayers.Enemy, "Ignore Raycast");
            return Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 12f, mask, QueryTriggerInteraction.Ignore)
                ? hit.point + Vector3.up * lift
                : p + Vector3.up * lift;
        }

        private static int CountOf(NetworkInventory inv, string id)
        {
            int n = 0;
            foreach (Ashveil.Data.ItemStack st in inv.ServerModel.Bag) if (!st.IsEmpty && st.id == id) n += st.count;
            return n;
        }

        private static List<EnemyBrain> AliveInSlot(DungeonSlot slot) =>
            EnemyBrain.All.Where(b => b != null && slot.Contains(b.transform.position) && b.GetComponent<Health>() is Health h && h != null && h.IsAlive).ToList();

        private static int ExpectedEnemies(DungeonSlot slot) =>
            slot.GetComponentsInChildren<EnemySpawnArea>(true).Where(a => !a.SummonOnly).Sum(a => a.Count);

        private IEnumerator MineSuite()
        {
            // ---------------------------------------------------------- Anmeldung
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(MineProfiles[0])) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host) angemeldet");
            if (a == null) yield break;
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            for (int i = 1; i < 3; i++) StartClient(exe, MineProfiles[i]);
            yield return WaitUntil(() => MineProfiles.All(p => OnlineInv(p) != null), 150f);
            var p3 = MineProfiles.Select(OnlineInv).ToList();
            Check(p3.All(x => x != null), "Spieler B und C (Test-Clients) angemeldet");
            if (p3.Any(x => x == null)) yield break;
            NetworkInventory b = p3[1], c = p3[2];
            yield return new WaitForSecondsRealtime(2f);
            Line("  ·     Besetzung: " + string.Join(", ", p3.Select(x => $"{x.CharacterName} (Stufe {x.ServerModel.Level})")));

            // ---------------------------------------------------------- Aufbau
            Line("");
            Line("[Aufbau]");
            DungeonDirector dir = DungeonDirector.For("salzgruben");
            DungeonPortal portal = DungeonPortal.Find("salzgruben");
            var slots = DungeonSlot.All.Where(s => s != null && s.DungeonId == "salzgruben").OrderBy(s => s.Index).ToList();
            Check(dir != null, "Dungeon-Direktor der Salzgruben laeuft");
            Check(portal != null, "Eingang am Stollenmund vorhanden");
            Check(slots.Count == 6 && dir != null && dir.Instances.Count == 6, $"Sechs Instanz-Plaetze ({slots.Count})");
            if (dir == null || portal == null || slots.Count == 0) yield break;
            DungeonDirector.ResetOverride = 3f; // Test: leere Instanzen nach 3 s zuruecksetzen (sonst 5 min)

            // Jeder Raum ist vom Eingang aus erreichbar (erster und letzter Platz)
            string[] rooms = { "Foerderhalle", "Salzhalle", "Salzsee-Grotte", "Wrackhoehle", "Herzkaverne", "Lorenbahn" };
            Vector3[] local =
            {
                new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 72f), new Vector3(85f, -8f, 80f), new Vector3(-80f, -6f, 66f),
                new Vector3(85f, -14f, 170f), new Vector3(85f, -8f, 0f)
            };
            foreach (DungeonSlot s in new[] { slots[0], slots[slots.Count - 1] })
            {
                var missing = new List<string>();
                var path = new NavMeshPath();
                NavMesh.SamplePosition(s.ArrivalWorld, out NavMeshHit start, 3f, NavMesh.AllAreas);
                for (int r = 0; r < rooms.Length; r++)
                {
                    Vector3 target = s.transform.TransformPoint(local[r]);
                    bool ok = NavMesh.SamplePosition(target, out NavMeshHit h, 4f, NavMesh.AllAreas) &&
                              NavMesh.CalculatePath(start.position, h.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    if (!ok) missing.Add(rooms[r]);
                }
                Check(missing.Count == 0, $"Platz {s.Index}: alle {rooms.Length} Raeume vom Foerderkorb aus erreichbar" +
                    (missing.Count > 0 ? " (fehlt: " + string.Join(", ", missing) + ")" : ""));
            }
            Check(slots.All(s => AliveInSlot(s).Count == 0), "Vor dem Betreten sind alle Plaetze leer (keine Gegner)");
            int expected = ExpectedEnemies(slots[0]);
            Line($"  ·     {expected} Gegner je Instanz, Eingang bei {portal.transform.position:F0}");

            // ---------------------------------------------------------- Lager der Grubenleute (M6.5)
            Line("");
            Line("[Lager der Grubenleute: Auftraege]");
            var pa = a.GetComponent<NetworkProgression>();
            var pb = b.GetComponent<NetworkProgression>();
            Check(Ashveil.Gameplay.Quests.NpcIdentity.Find(NVela) != null && Ashveil.Gameplay.Quests.NpcIdentity.Find(NTobbe) != null,
                "Grubenmeisterin Vela Salk und Bergmann Tobbe stehen vor dem Stollenmund");
            yield return GotoNpc(a, NVela);
            yield return Ask(a, "npc-talk", NVela);
            Check(_last.ok, "Gespraech mit Vela Salk");
            yield return Ask(a, "quest-accept", QHeart);
            Check(!_last.ok && !pa.ServerIsActive(QHeart), "„Was im Herzen schlaegt“ gibt es erst nach „Die letzte Schicht“");
            yield return Ask(a, "quest-accept", QShift);
            Check(_last.ok && pa.ServerIsActive(QShift), "Maxmod nimmt „Die letzte Schicht“ an");
            yield return Ask(a, "quest-accept", QShip);
            Check(_last.ok && pa.ServerIsActive(QShip), "Maxmod nimmt „Das Schiff im Berg“ an (optional)");
            yield return Ask(a, "quest-accept", QSalt);
            Check(_last.ok && pa.ServerIsActive(QSalt), "Maxmod nimmt „Grubensalz“ an");
            yield return GotoNpc(b, NVela);
            yield return Ask(b, "npc-talk", NVela);
            yield return Ask(b, "quest-accept", QShift);
            Check(_last.ok && pb.ServerIsActive(QShift), "Krisbot nimmt „Die letzte Schicht“ an");
            yield return GotoNpc(a, NTobbe);
            yield return Ask(a, "npc-talk", NTobbe);
            pa.ServerSetMoney(5000); // Testfigur: 50 Silber Reisegeld
            long money0 = pa.ServerMoney;
            int potions0 = CountOf(a, "item.strong_healing_draught");
            yield return Ask(a, "buy", "1");
            Check(_last.ok && pa.ServerMoney < money0 && CountOf(a, "item.strong_healing_draught") > potions0,
                $"Bei Tobbe einen starken Heiltrank gekauft ({money0 - pa.ServerMoney} Kupfer)");

            // ---------------------------------------------------------- Gruppe betritt
            Line("");
            Line("[Gruppe betritt die Salzgruben]");
            Vector3 front = Gnd(portal.ReturnWorld.x, portal.ReturnWorld.z);
            Vector3 mouth = Gnd(portal.transform.position.x, portal.transform.position.z);
            yield return Ask(a, "goto", V(front, portal.ReturnYaw));
            yield return Ask(b, "goto", V(front + Vector3.right * 2f, portal.ReturnYaw));
            yield return Ask(c, "goto", V(front - Vector3.right * 2f, portal.ReturnYaw));
            yield return Ask(a, "party-invite", b.CharacterName);
            yield return Ask(b, "party-respond", "1");
            yield return new WaitForSecondsRealtime(1f);
            Check(PartyService.Of(a.ProfileId) != null && PartyService.Of(a.ProfileId) == PartyService.Of(b.ProfileId), "Maxmod und Krisbot bilden eine Gruppe");

            yield return Ask(a, "goto", V(mouth, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(a.ProfileId) != null, 6f);
            yield return Ask(b, "goto", V(mouth, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(b.ProfileId) != null, 6f);
            DungeonDirector.Instance ia = dir.InstanceOf(a.ProfileId), ib = dir.InstanceOf(b.ProfileId);
            Check(ia != null, "Maxmod ist nach dem Stollenmund in einer Instanz");
            Check(ia != null && ia == ib, $"Krisbot landet in derselben Instanz wie ihre Gruppe (Platz {(ia != null ? ia.Slot.Index : -1)})");
            if (ia == null) yield break;
            DungeonSlot groupSlot = ia.Slot;
            yield return new WaitForSecondsRealtime(1.5f);
            Check(Vector3.Distance(a.transform.position, groupSlot.ArrivalWorld) < 5f, "Ankunft in der Foerderhalle");
            int alive = AliveInSlot(groupSlot).Count;
            Check(alive == expected && ia.Enemies.Count == expected, $"Gegner der Instanz sind erschienen ({alive}/{expected})");
            Check(slots.Where(s => s != groupSlot).All(s => AliveInSlot(s).Count == 0), "Andere Plaetze bleiben leer");
            var defs = AliveInSlot(groupSlot).Select(e => e.Definition.Id).Distinct().OrderBy(x => x).ToList();
            Check(defs.Count == 8 && defs.Contains("enemy.foreman_hask") && defs.Contains("enemy.salt_mother") && defs.Contains("enemy.mine_heart") &&
                  defs.Contains("enemy.drowned_captain"), "Vier Gegnerarten und vier Bosse: " + string.Join(", ", defs));
            Check(AliveInSlot(groupSlot).All(e => e.Level >= 10 && e.Level <= 14), "Alle Gegner Stufe 10–14");

            yield return Ask(b, "indoor");
            Check(_last.data.StartsWith("indoor=1;sun=0") && _last.data.EndsWith("slot=" + groupSlot.Index), $"Krisbot: unter Tage – Sonne aus, dunkle Stimmung ({_last.data})");
            yield return Ask(a, "indoor");
            Check(_last.data.StartsWith("indoor=1;sun=0"), $"Maxmod (Host): unter Tage ({_last.data})");

            // ---------------------------------------------------------- Maltebot allein
            Line("");
            Line("[Maltebot allein]");
            yield return Ask(c, "goto", V(mouth, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(c.ProfileId) != null, 6f);
            DungeonDirector.Instance ic = dir.InstanceOf(c.ProfileId);
            Check(ic != null && ic != ia, $"Maltebot bekommt eine eigene Instanz (Platz {(ic != null ? ic.Slot.Index : -1)})");
            Check(dir.Instances.Count(i => i.Active) == 2, $"Zwei Instanzen belegt, {dir.FreeSlots} frei");
            if (ic != null) Check(AliveInSlot(ic.Slot).Count == expected, "Auch Maltebots Instanz hat ihre eigenen Gegner");

            // ---------------------------------------------------------- Kampf ohne Wiederkehr
            Line("");
            Line("[Kampf im Hauptstollen]");
            var duo = new List<NetworkInventory> { a, b };
            int xpA0 = a.GetComponent<NetworkProgression>().ServerXp;
            Vector3 pack = groupSlot.transform.TransformPoint(new Vector3(0f, 0f, 22f));
            var packEnemies = AliveInSlot(groupSlot).Where(e => Vector3.Distance(e.Home, pack) < 8f).ToList();
            int kills = 0;
            float fightT = 0f;
            foreach (EnemyBrain e in packEnemies)
            {
                var hh = e.GetComponent<Health>();
                string id = e.GetComponent<NetworkObject>().ObjectId.ToString();
                // P2 Balance: Dungeon-Gegner sind fuer 3–5 Spieler in Dungeon-Ausruestung gebaut – die zwei Testfiguren mit
                // Startwaffen bekommen einen Vorsprung, gekaempft wird trotzdem echt (Treffer, Tod, keine Wiederkehr)
                if (hh != null && hh.IsAlive && hh.Current > 160f)
                    hh.ApplyDamage(new DamageInfo { Amount = hh.Current - 160f, Source = a.gameObject, WeaponId = "test" });
                float one = 0f;
                while (hh != null && hh.IsAlive && one < 50f)
                {
                    Vector3 w = e.transform.position;
                    yield return AskAll(duo, (p, i) => "goto", (p, i) =>
                    {
                        Vector3 o = Quaternion.Euler(0f, i * 150f + 20f, 0f) * Vector3.forward * 1.8f;
                        Vector3 g = MineGround(w + o);
                        return V(g, YawTo(g, w));
                    });
                    yield return AskAll(duo, (p, i) => "attack", (p, i) => id);
                    float w8 = 0f;
                    while (hh.IsAlive && w8 < 4f) { w8 += 0.25f; yield return new WaitForSecondsRealtime(0.25f); }
                    one += 5f;
                    foreach (NetworkInventory p in duo)
                    {
                        var ph = p.GetComponent<Health>();
                        if (ph != null && ph.IsAlive && ph.Fraction < 0.45f) ph.Heal(ph.Max, null);
                    }
                }
                fightT += one;
                if (hh != null && !hh.IsAlive) kills++;
            }
            yield return AskAll(duo, (p, i) => "stop", (p, i) => "");
            Check(packEnemies.Count >= 3 && kills == packEnemies.Count, $"Maxmod und Krisbot besiegen den ersten Trupp ({kills}/{packEnemies.Count}, {fightT:0} s)");
            Check(a.GetComponent<NetworkProgression>().ServerXp > xpA0 || a.ServerModel.Level > 12, "Erfahrung fuer die Grubenarbeiter");
            int aliveAfter = AliveInSlot(groupSlot).Count;
            yield return new WaitForSecondsRealtime(8f);
            Check(AliveInSlot(groupSlot).Count == aliveAfter && aliveAfter == expected - kills,
                $"Gefallene kehren in der Instanz nicht zurueck ({AliveInSlot(groupSlot).Count} leben noch)");

            // ---------------------------------------------------------- Tod in der Grube
            Line("");
            Line("[Tod und Wiederbelebung]");
            var hb = b.GetComponent<Health>();
            hb.ApplyDamage(new DamageInfo { Amount = hb.Max * 5f });
            yield return WaitUntil(() => !hb.IsAlive, 3f);
            Check(!hb.IsAlive, "Krisbot faellt in der Grube (Testschaden)");
            yield return WaitUntil(() => hb.IsAlive, 20f);
            yield return new WaitForSecondsRealtime(1f);
            float toArrival = Vector3.Distance(b.transform.position, groupSlot.ArrivalWorld);
            Check(hb.IsAlive && toArrival < 5f, $"Krisbot erwacht in der Foerderhalle ihrer Instanz ({toArrival:0.0} m vom Eingang), nicht am Wachtfeuer");
            Check(dir.InstanceOf(b.ProfileId) == ia, "Krisbot ist weiter in ihrer Instanz");

            // ---------------------------------------------------------- Boss 1: Vorarbeiter Hask
            yield return HaskSuite(a, b, groupSlot);

            // ---------------------------------------------------------- Abgabe draussen, Folgeauftrag, zurueck zur Gruppe
            Line("");
            Line("[Zwischendurch zu Vela: Die letzte Schicht]");
            Check(pa.ServerProgress(QShift, 0) >= 1 && pb.ServerProgress(QShift, 0) >= 1, "Hask zaehlt fuer den Auftrag – bei Maxmod und bei Krisbot");
            yield return Ask(a, "goto", V(MineGround(groupSlot.ExitWorld), 180f));
            yield return WaitUntil(() => DungeonSlot.At(a.transform.position) == null, 6f);
            yield return GotoNpc(a, NVela);
            yield return Ask(a, "npc-talk", NVela);
            int xpShift = pa.ServerXp, lvlShift = a.ServerModel.Level;
            yield return Ask(a, "quest-turnin", QShift + " 0");
            Check(_last.ok && pa.ServerIsCompleted(QShift) && (pa.ServerXp > xpShift || a.ServerModel.Level > lvlShift),
                "„Die letzte Schicht“ abgegeben – Erfahrung und eine Belohnung zur Wahl");
            yield return Ask(a, "quest-accept", QHeart);
            Check(_last.ok && pa.ServerIsActive(QHeart), "Jetzt gibt Vela „Was im Herzen schlaegt“");
            yield return new WaitForSecondsRealtime(3.5f);
            yield return Ask(a, "goto", V(mouth, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(a.ProfileId) != null, 6f);
            Check(dir.InstanceOf(a.ProfileId) == ia, $"Maxmod kehrt in die Instanz seiner Gruppe zurueck (Platz {(dir.InstanceOf(a.ProfileId) != null ? dir.InstanceOf(a.ProfileId).Slot.Index : -1)})");

            // ---------------------------------------------------------- Boss 2: Die Salzmutter
            yield return SaltMotherSuite(a, b, groupSlot, dir, portal);

            // ---------------------------------------------------------- Optional: Der Ertrunkene Kapitaen
            yield return CaptainSuite(a, b, groupSlot);

            // ---------------------------------------------------------- Endboss: Das Grubenherz
            yield return HeartSuite(a, b, groupSlot);

            // ---------------------------------------------------------- Verlassen und Zuruecksetzen
            Line("");
            Line("[Verlassen und Zuruecksetzen]");
            yield return Ask(a, "goto", V(MineGround(groupSlot.ExitWorld), 180f));
            yield return WaitUntil(() => dir.InstanceOf(a.ProfileId) == null && DungeonSlot.At(a.transform.position) == null, 6f);
            yield return new WaitForSecondsRealtime(1f);
            float toFront = Vector3.Distance(a.transform.position, portal.ReturnWorld);
            Check(DungeonSlot.At(a.transform.position) == null && toFront < 5f, $"Maxmod verlaesst die Grube ueber den Foerderkorb und steht vor dem Eingang ({toFront:0.0} m)");
            Check(!a.GetComponent<NetworkMotion>().HasRespawnPoint, "Draussen gilt wieder das Wachtfeuer als Wiederbelebungspunkt");
            yield return Ask(a, "indoor");
            Check(_last.data.StartsWith("indoor=0;sun=1"), $"Draussen scheint wieder die Sonne ({_last.data})");
            Check(ia.Active, "Die Instanz bleibt bestehen, solange Krisbot drin ist");

            yield return Ask(b, "goto", V(MineGround(groupSlot.ExitWorld), 180f));
            yield return WaitUntil(() => DungeonSlot.At(b.transform.position) == null, 6f);
            yield return WaitUntil(() => !ia.Active, 10f);
            Check(!ia.Active, "Leere Instanz wird zurueckgesetzt (Test: nach 3 s, sonst 5 min)");
            yield return new WaitForSecondsRealtime(1f);
            Check(AliveInSlot(groupSlot).Count == 0 && EnemyBrain.All.Count(e => e != null && groupSlot.Contains(e.transform.position)) == 0,
                "Ihre Gegner und Leichen sind verschwunden");

            // Erneut hinein: frische Gegner
            yield return Ask(a, "goto", V(mouth, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(a.ProfileId) != null, 6f);
            DungeonDirector.Instance again = dir.InstanceOf(a.ProfileId);
            Check(again != null && AliveInSlot(again.Slot).Count == expected, $"Beim erneuten Betreten warten frische Gegner ({(again != null ? AliveInSlot(again.Slot).Count : 0)}/{expected})");
            if (again != null)
            {
                LoreGate g2 = LoreGate.In(again.Slot);
                Check(g2 != null && !g2.IsOpen, "Das Lorenbahn-Gitter ist in der frischen Instanz wieder verschlossen");
                var lamps2 = MineLamp.In(again.Slot);
                Check(lamps2.Count == 4 && lamps2.All(l => l.IsLit), "Alle vier Herzlampen brennen in der frischen Instanz");
            }
            if (again != null)
            {
                yield return Ask(a, "goto", V(MineGround(again.Slot.ExitWorld), 180f));
                yield return WaitUntil(() => a == null || DungeonSlot.At(a.transform.position) == null, 6f);
            }
            if (ic != null)
            {
                // Am 07.10. war Maltebots Figur hier schon weg (Test-Client C getrennt): klar melden statt bis zum Zeitlimit zu hängen
                Check(c != null, "Maltebot (Test-Client C) ist bis zum Schluss verbunden");
                if (c != null)
                {
                    yield return Ask(c, "goto", V(MineGround(ic.Slot.ExitWorld), 180f));
                    yield return WaitUntil(() => c == null || DungeonSlot.At(c.transform.position) == null, 6f);
                }
            }
            yield return WaitUntil(() => dir.Instances.All(i => !i.Active), 10f);
            Check(dir.Instances.All(i => !i.Active) && dir.FreeSlots == 6, "Am Ende sind alle sechs Plaetze wieder frei");
            DungeonDirector.ResetOverride = -1f;

            // ---------------------------------------------------------- Abgabe am Ende
            Line("");
            Line("[Zurueck bei Vela]");
            Check(pa.ServerProgress(QHeart, 0) >= 1 && pa.ServerProgress(QShip, 0) >= 1, "Grubenherz und Kapitaen zaehlen fuer die Auftraege");
            yield return GotoNpc(a, NVela);
            yield return Ask(a, "npc-talk", NVela);
            int xpEnd = pa.ServerXp, lvlEnd = a.ServerModel.Level;
            yield return Ask(a, "quest-turnin", QHeart + " 0");
            bool heartDone = _last.ok && pa.ServerIsCompleted(QHeart);
            yield return Ask(a, "quest-turnin", QShip + " 0");
            Check(heartDone && pa.ServerIsCompleted(QShip), "„Was im Herzen schlaegt“ und „Das Schiff im Berg“ abgegeben");
            Check(CountOf(a, "item.red_vein_salt") >= 2 && (pa.ServerXp > xpEnd || a.ServerModel.Level > lvlEnd),
                $"Belohnung: Rotaderiges Salz und Erfahrung (Stufe {a.ServerModel.Level}, {pa.ServerXp} EP)");
        }

        /// <summary>M6.2: Vorarbeiter Hask – Sprengladungen, Arbeiter rufen, Abbruch per Leine, Sieg mit Beute.</summary>
        private IEnumerator HaskSuite(NetworkInventory a, NetworkInventory b, DungeonSlot slot)
        {
            Line("");
            Line("[Boss 1: Vorarbeiter Hask]");
            var duo = new List<NetworkInventory> { a, b };
            var enc = slot.GetComponentInChildren<ForemanEncounter>();
            Check(enc != null, "Begegnung Vorarbeiter Hask liegt in der Salzhalle");
            if (enc == null) yield break;
            yield return WaitUntil(() => enc.Boss != null, 3f);
            EnemyBrain hask = enc.Boss;
            Check(hask != null && hask.Level == 14 && hask.Definition.elite, $"Hask wartet in der Salzhalle (Stufe {(hask != null ? hask.Level : 0)}, Elite)");
            if (hask == null) yield break;
            var hh = hask.GetComponent<Health>();
            string id = hask.GetComponent<NetworkObject>().ObjectId.ToString();
            Check(!enc.Engaged && hask.State != EnemyState.Chase, "Kampf beginnt erst, wenn jemand kommt");

            var announcements = new List<string>();
            System.Action<Vector3, string> onAnnounce = (w, t) => { if (slot.Contains(w)) announcements.Add(t); };
            EnemyEncounter.Announced += onAnnounce;
            int explosions = 0, explosionHits = 0;
            System.Action<Vector3, float, bool> onBoom = (c, r, any) => { explosions++; if (any) explosionHits++; };
            hask.StrikeExploded += onBoom;
            ForemanEncounter.FirstChargeOverride = 1.5f;

            // Vor Hask stellen und angreifen
            IEnumerator Fight(float seconds, bool standStill)
            {
                float t = 0f;
                while (t < seconds && hh.IsAlive)
                {
                    Vector3 w = hask.transform.position;
                    if (!standStill || t == 0f)
                    {
                        yield return AskAll(duo, (p, i) => "goto", (p, i) =>
                        {
                            Vector3 o = Quaternion.Euler(0f, i * 140f + 30f, 0f) * Vector3.forward * 2.2f;
                            Vector3 g = MineGround(w + o);
                            return V(g, YawTo(g, w));
                        });
                        yield return AskAll(duo, (p, i) => "attack", (p, i) => id);
                    }
                    yield return new WaitForSecondsRealtime(1f);
                    t += 1.3f;
                    foreach (NetworkInventory p in duo)
                    {
                        var ph = p.GetComponent<Health>();
                        if (ph != null && ph.IsAlive && ph.Fraction < 0.6f) ph.Heal(ph.Max, null);
                    }
                }
            }

            yield return Fight(2f, false);
            yield return WaitUntil(() => enc.Engaged, 5f);
            Check(enc.Engaged, "Hask stellt sich zum Kampf");
            Check(announcements.Count > 0, "Ansage an die Gruppe: " + (announcements.Count > 0 ? announcements[0] : "–"));

            // Sprengladungen: stehen bleiben -> Warnring, Explosion trifft
            yield return WaitUntil(() => hask.PendingStrikes > 0, 8f);
            int pending = hask.PendingStrikes;
            bool ring = FindObjectsByType<GroundWarning>(FindObjectsSortMode.None).Length > 0;
            Check(pending >= 2, $"Hask wirft Sprengladungen ({pending} auf einmal)");
            yield return null;
            ring |= FindObjectsByType<GroundWarning>(FindObjectsSortMode.None).Length > 0;
            Check(ring, "Warnringe am Boden zeigen, wo sie liegen");
            yield return Fight(4.5f, true);
            Check(explosions >= pending, $"Die Ladungen explodieren nach der Zuendschnur ({explosions})");
            Check(explosionHits > 0, "Wer im Ring stehen bleibt, wird getroffen");

            // Ausweichen: Ladungen unter beide legen, beide laufen raus
            int hits0 = explosionHits, boom0 = explosions;
            yield return AskAll(duo, (p, i) => "stop", (p, i) => "");
            Vector3 hp0 = hask.transform.position;
            foreach (NetworkInventory p in duo) hask.ServerAreaStrike(p.transform.position, 3.5f, 1.6f, 70f, "dynamite");
            yield return AskAll(duo, (p, i) => "goto", (p, i) =>
            {
                Vector3 away = p.transform.position - hp0;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
                return V(MineGround(p.transform.position + away.normalized * 8f), 0f);
            });
            yield return WaitUntil(() => explosions >= boom0 + 2, 4f);
            Check(explosions >= boom0 + 2 && explosionHits == hits0, "Wer rechtzeitig aus dem Ring geht, bleibt heil");

            // Arbeiter rufen (Testschaden bis unter 66 % und 33 %)
            yield return Fight(1f, false);
            hh.ApplyDamage(new DamageInfo { Amount = Mathf.Max(1f, hh.Current - hh.Max * 0.6f), Source = a.gameObject, WeaponId = "test" });
            yield return WaitUntil(() => enc.Waves >= 1, 3f);
            yield return new WaitForSecondsRealtime(1f);
            Check(enc.Waves == 1 && enc.LivingAdds == 2, $"Bei 66 % ruft Hask zwei Arbeiter ({enc.LivingAdds})");
            Check(enc.Adds.All(x => x == null || x.InCombat), "Die Arbeiter greifen sofort an");
            Check(announcements.Any(t => t.Contains("Schicht")), "Ansage: „Schicht! Alle Mann an die Hacken!“");
            hh.ApplyDamage(new DamageInfo { Amount = Mathf.Max(1f, hh.Current - hh.Max * 0.3f), Source = a.gameObject, WeaponId = "test" });
            yield return WaitUntil(() => enc.Waves >= 2, 3f);
            Check(enc.Waves == 2 && enc.Enraged, $"Bei 33 % zweite Welle und schnellere Wuerfe ({enc.LivingAdds} Arbeiter)");

            // Abbruch: Gruppe flieht in die Foerderhalle -> Leine, alles zurueck
            yield return AskAll(duo, (p, i) => "stop", (p, i) => "");
            yield return AskAll(duo, (p, i) => "goto", (p, i) => V(MineGround(slot.ArrivalWorld + Vector3.right * (i * 2f - 1f)), 0f));
            yield return WaitUntil(() => !enc.Engaged, 25f);
            Check(!enc.Engaged, "Flieht die Gruppe, bricht Hask den Kampf ab (Leine)");
            Check(enc.LivingAdds == 0, "Seine Arbeiter verschwinden");
            yield return WaitUntil(() => hask.State == EnemyState.Idle, 25f);
            Check(hh.Fraction > 0.99f && hask.PendingStrikes == 0, $"Hask ist wieder bei vollem Leben ({hh.Fraction * 100f:0} %)");

            // Zweiter Versuch: Sieg
            int xp0 = a.GetComponent<NetworkProgression>().ServerXp;
            yield return Fight(3f, false);
            yield return WaitUntil(() => enc.Engaged, 5f);
            Check(enc.Attempts == 2, "Zweiter Versuch");
            hh.ApplyDamage(new DamageInfo { Amount = Mathf.Max(1f, hh.Current - FinishRest(hh, 0.04f)), Source = a.gameObject, WeaponId = "test" });
            yield return Fight(40f, false);
            yield return AskAll(duo, (p, i) => "stop", (p, i) => "");
            Check(!hh.IsAlive, "Maxmod und Krisbot besiegen Vorarbeiter Hask");
            yield return WaitUntil(() => enc.Defeated, 3f);
            yield return new WaitForSecondsRealtime(1f);
            Check(enc.Defeated && enc.LivingAdds == 0, "Nach dem Sieg ziehen sich die Arbeiter zurueck");
            var corpse = hask.GetComponent<Ashveil.Gameplay.Items.CorpseLoot>();
            Check(corpse != null && corpse.HasAnyLoot, "Hask hinterlaesst Beute");
            Check(a.GetComponent<NetworkProgression>().ServerXp > xp0 || a.ServerModel.Level > 12, "Erfahrung fuer den Boss");
            Check(announcements.Any(t => t.Contains("fällt")), "Ansage zum Sieg");
            yield return new WaitForSecondsRealtime(5f);
            Check(hask != null && !hh.IsAlive && AliveInSlot(slot).All(e => e.Definition.Id != "enemy.foreman_hask"), "Hask kehrt in dieser Instanz nicht zurueck");

            hask.StrikeExploded -= onBoom;
            EnemyEncounter.Announced -= onAnnounce;
            ForemanEncounter.FirstChargeOverride = -1f;
            foreach (NetworkInventory p in duo) { var ph = p.GetComponent<Health>(); if (ph != null && ph.IsAlive) ph.Heal(ph.Max, null); }
        }

        /// <summary>M6.3: Die Salzmutter – Salzspucke, Flut (Plattformen retten), Krebsbrut, Sieg oeffnet die Lorenbahn.</summary>
        private IEnumerator SaltMotherSuite(NetworkInventory a, NetworkInventory b, DungeonSlot slot, DungeonDirector dir, DungeonPortal portal)
        {
            Line("");
            Line("[Boss 2: Die Salzmutter]");
            var duo = new List<NetworkInventory> { a, b };
            var enc = slot.GetComponentInChildren<SaltMotherEncounter>();
            LoreGate gate = LoreGate.In(slot);
            Check(enc != null && gate != null, "Begegnung Salzmutter und Lorenbahn-Gitter liegen in der Grotte");
            if (enc == null || gate == null) yield break;
            yield return WaitUntil(() => enc.Boss != null, 3f);
            EnemyBrain mother = enc.Boss;
            Check(mother != null && mother.Level == 14 && mother.Definition.elite, $"Die Salzmutter wartet im See (Stufe {(mother != null ? mother.Level : 0)}, Elite)");
            if (mother == null) yield break;
            var mh = mother.GetComponent<Health>();
            string id = mother.GetComponent<NetworkObject>().ObjectId.ToString();

            // Gitter zu – auf dem Server und bei Krisbot
            Vector3 grotto = slot.transform.TransformPoint(new Vector3(85f, -8f, 80f));
            yield return Ask(b, "goto", V(MineGround(grotto + new Vector3(-8f, 0f, -14f)), 0f));
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Ask(b, "grotto");
            Check(!gate.IsOpen && _last.data.Contains("gate=0"), $"Lorenbahn-Gitter ist verschlossen (Server und Krisbot: {_last.data})");

            var announcements = new List<string>();
            System.Action<Vector3, string> onAnnounce = (w, t) => { if (slot.Contains(w)) announcements.Add(t); };
            EnemyEncounter.Announced += onAnnounce;
            float floodA = 0f, floodB = 0f;
            System.Action<DamageInfo> dmgA = d => { if (d.WeaponId != null && d.WeaponId.EndsWith(".flood")) floodA += d.Amount; };
            System.Action<DamageInfo> dmgB = d => { if (d.WeaponId != null && d.WeaponId.EndsWith(".flood")) floodB += d.Amount; };
            var ha = a.GetComponent<Health>();
            var hb = b.GetComponent<Health>();
            ha.Damaged += dmgA;
            hb.Damaged += dmgB;
            SaltMotherEncounter.FirstFloodOverride = 3f;
            SaltMotherEncounter.FirstSpitOverride = 1f;

            void HealAll()
            {
                foreach (NetworkInventory p in duo)
                {
                    var ph = p.GetComponent<Health>();
                    if (ph != null && ph.IsAlive && ph.Fraction < 0.6f) ph.Heal(ph.Max, null);
                }
            }

            IEnumerator Fight(float seconds, IList<NetworkInventory> who)
            {
                float t = 0f;
                while (t < seconds && mh.IsAlive)
                {
                    Vector3 w = mother.transform.position;
                    yield return AskAll(who, (p, i) => "goto", (p, i) =>
                    {
                        Vector3 o = Quaternion.Euler(0f, i * 140f + 30f, 0f) * Vector3.forward * 3.2f;
                        Vector3 g = MineGround(w + o);
                        return V(g, YawTo(g, w));
                    });
                    yield return AskAll(who, (p, i) => "attack", (p, i) => id);
                    yield return new WaitForSecondsRealtime(1f);
                    t += 1.3f;
                    HealAll();
                }
            }

            // Kampf beginnt: Maxmod unten am Ufer, Krisbot erst mit
            yield return Fight(2f, duo);
            yield return WaitUntil(() => enc.Engaged, 5f);
            Check(enc.Engaged, "Die Salzmutter stellt sich zum Kampf");
            Check(announcements.Count > 0, "Ansage: " + (announcements.Count > 0 ? announcements[0] : "–"));

            // Flut: Warnung -> Krisbot auf eine Plattform, Maxmod bleibt unten
            yield return WaitUntil(() => enc.FloodWarning || enc.FloodActive, 6f);
            Check(enc.FloodWarning || enc.FloodActive, "Die Flut kuendigt sich an");
            Check(announcements.Any(t => t.Contains("Plattformen")), "Warnung an die Gruppe: „auf die Plattformen!“");
            Vector3 platform = MineGround(slot.transform.TransformPoint(new Vector3(95f, -8f, 76f)), 0.1f);
            yield return Ask(b, "stop", "");
            yield return Ask(b, "goto", V(platform, 0f));
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Ask(b, "grotto");
            Check(_last.data.StartsWith("flood=") && !_last.data.StartsWith("flood=0.00") && !_last.data.StartsWith("flood=-"),
                $"Krisbot sieht das Wasser steigen ({_last.data})");
            Check(b.transform.position.y >= enc.SafeHeightWorld, $"Krisbot steht auf einer Plattform ({b.transform.position.y - grotto.y:0.00} m ueber dem Grund)");
            // Maxmod bleibt unten am Grund – an einer Stelle ohne Plattform
            Vector3 lowSpot = MineGround(grotto + new Vector3(0f, 0f, -12f), 0.1f);
            yield return Ask(a, "stop", "");
            yield return Ask(a, "goto", V(lowSpot, 0f));
            yield return WaitUntil(() => enc.FloodActive, 6f);
            Check(enc.FloodActive, "Die Flut steht in der Grotte");
            float t0 = 0f;
            while (t0 < 3f)
            {
                if (a.transform.position.y >= enc.SafeHeightWorld || Vector3.Distance(a.transform.position, lowSpot) > 2f) yield return Ask(a, "goto", V(lowSpot, 0f));
                yield return new WaitForSecondsRealtime(0.75f);
                t0 += 0.9f;
                HealAll();
                if (b.transform.position.y < enc.SafeHeightWorld) yield return Ask(b, "goto", V(platform, 0f));
            }
            yield return Ask(b, "grotto");
            Check(_last.data.StartsWith("flood=0.9") || _last.data.StartsWith("flood=1.0") || _last.data.StartsWith("flood=0.8"),
                $"Bei Krisbot steht das Wasser hoch ({_last.data})");
            Check(floodA > 0f, $"Maxmod unten im Wasser nimmt Schaden ({floodA:0})");
            Check(floodB <= 0f, $"Krisbot auf der Plattform bleibt trocken ({floodB:0})");
            yield return WaitUntil(() => !enc.FloodActive, 10f);
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Ask(b, "grotto");
            Check(!enc.FloodActive && _last.data.StartsWith("flood=0.00"), $"Danach sinkt das Wasser wieder ({_last.data})");
            Check(enc.Spits > 0, $"Sie spuckt Salz nach den Spielern ({enc.Spits}x)");

            // Krebsbrut bei 75 %
            yield return Fight(1f, duo);
            mh.ApplyDamage(new DamageInfo { Amount = Mathf.Max(1f, mh.Current - mh.Max * 0.72f), Source = a.gameObject, WeaponId = "test" });
            yield return WaitUntil(() => enc.BroodWaves >= 1, 3f);
            yield return new WaitForSecondsRealtime(1f);
            Check(enc.BroodWaves == 1 && enc.LivingAdds == 3, $"Bei 75 % schluepfen drei junge Salzkrebse ({enc.LivingAdds})");
            Check(enc.Adds.All(x => x == null || x.InCombat), "Die Brut greift sofort an");

            // Sieg
            int xp0 = a.GetComponent<NetworkProgression>().ServerXp;
            mh.ApplyDamage(new DamageInfo { Amount = Mathf.Max(1f, mh.Current - FinishRest(mh, 0.03f)), Source = a.gameObject, WeaponId = "test" });
            yield return Fight(45f, duo);
            yield return AskAll(duo, (p, i) => "stop", (p, i) => "");
            Check(!mh.IsAlive, "Maxmod und Krisbot besiegen die Salzmutter");
            yield return WaitUntil(() => enc.Defeated, 3f);
            yield return new WaitForSecondsRealtime(1.5f);
            Check(enc.BroodWaves == 3, $"Alle drei Brutwellen kamen ({enc.BroodWaves})");
            Check(enc.Defeated && enc.LivingAdds == 0, "Nach dem Sieg verschwindet die Brut");
            var corpse = mother.GetComponent<Ashveil.Gameplay.Items.CorpseLoot>();
            Check(corpse != null && corpse.HasAnyLoot, "Die Salzmutter hinterlaesst Beute");
            Check(a.GetComponent<NetworkProgression>().ServerXp > xp0 || a.ServerModel.Level > 12, "Erfahrung fuer den Boss");
            Check(announcements.Any(t => t.Contains("Lorenbahn")), "Ansage: die Lorenbahn ist frei");

            // Gitter offen – Server, Krisbot, und man kommt hindurch
            Check(gate.IsOpen, "Das Lorenbahn-Gitter ist offen (Server)");
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Ask(b, "grotto");
            Check(_last.data.Contains("gate=1"), $"Auch bei Krisbot ist das Gitter offen ({_last.data})");
            Vector3 gp = slot.transform.TransformPoint(new Vector3(85f, -8f, 56.5f)); // Boden unter dem Gitter (es faehrt hoch)
            Physics.SyncTransforms();
            bool blocked = Physics.Raycast(gp + Vector3.up * 1.2f + Vector3.forward * 2f, Vector3.back, 4f, ~LayerMask.GetMask(Ashveil.Core.GameLayers.Player, Ashveil.Core.GameLayers.Enemy), QueryTriggerInteraction.Ignore);
            Check(!blocked, "Der Weg durch das Gitter ist frei");

            // Wer spaeter (wieder) hineinkommt, sieht das offene Gitter: Krisbot geht hinaus und wieder hinein
            yield return Ask(b, "goto", V(MineGround(slot.ExitWorld), 180f));
            yield return WaitUntil(() => DungeonSlot.At(b.transform.position) == null, 6f);
            yield return new WaitForSecondsRealtime(3.5f);
            Vector3 mouth = Gnd(portal.transform.position.x, portal.transform.position.z);
            yield return Ask(b, "goto", V(mouth, portal.ReturnYaw + 180f));
            yield return WaitUntil(() => dir.InstanceOf(b.ProfileId) != null, 6f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(b, "grotto");
            Check(dir.InstanceOf(b.ProfileId) != null && dir.InstanceOf(b.ProfileId).Slot == slot && _last.data.Contains("gate=1"),
                $"Zurueck in der Instanz sieht Krisbot das Gitter weiter offen ({_last.data})");

            ha.Damaged -= dmgA;
            hb.Damaged -= dmgB;
            EnemyEncounter.Announced -= onAnnounce;
            SaltMotherEncounter.FirstFloodOverride = -1f;
            SaltMotherEncounter.FirstSpitOverride = -1f;
            foreach (NetworkInventory p in duo) { var ph = p.GetComponent<Health>(); if (ph != null && ph.IsAlive) ph.Heal(ph.Max, null); }
        }

        /// <summary>M6.4: Das Grubenherz – Lichtfresser loeschen Lampen, Herz heilt sich, Anzuenden, Finsternis, Sieg.</summary>
        private IEnumerator HeartSuite(NetworkInventory a, NetworkInventory b, DungeonSlot slot)
        {
            Line("");
            Line("[Endboss: Das Grubenherz]");
            var duo = new List<NetworkInventory> { a, b };
            var enc = slot.GetComponentInChildren<HeartEncounter>();
            List<MineLamp> lamps = MineLamp.In(slot);
            Check(enc != null && lamps.Count == 4 && enc.LampCount == 4, $"Begegnung Grubenherz mit vier Lampen ({lamps.Count})");
            if (enc == null || lamps.Count != 4) yield break;
            yield return WaitUntil(() => enc.Boss != null, 3f);
            EnemyBrain heart = enc.Boss;
            Check(heart != null && heart.Level == 14 && heart.Definition.elite, $"Das Grubenherz schlaegt in der Kaverne (Stufe {(heart != null ? heart.Level : 0)}, Elite)");
            if (heart == null) yield break;
            var hh = heart.GetComponent<Health>();
            string id = heart.GetComponent<NetworkObject>().ObjectId.ToString();
            Vector3 hp = heart.transform.position;
            Vector3 home = hp;

            // Beide in die Kaverne (Suedseite, noch ausser Reichweite)
            yield return AskAll(duo, (p, i) => "goto", (p, i) => V(MineGround(hp + new Vector3(i * 2f - 1f, 0f, -22f)), 0f));
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "heart");
            Check(lamps.All(l => l.IsLit) && _last.data.StartsWith("lamps=1111"), $"Alle Lampen brennen (Server und Krisbot: {_last.data})");

            var announcements = new List<string>();
            System.Action<Vector3, string> onAnnounce = (w, t) => { if (slot.Contains(w)) announcements.Add(t); };
            EnemyEncounter.Announced += onAnnounce;
            float dark = 0f;
            System.Action<DamageInfo> onDark = d => { if (d.WeaponId != null && d.WeaponId.EndsWith(".darkness")) dark += d.Amount; };
            var ha = a.GetComponent<Health>();
            var hb = b.GetComponent<Health>();
            ha.Damaged += onDark;
            hb.Damaged += onDark;
            HeartEncounter.FirstEaterOverride = 1.5f;

            void HealAll()
            {
                foreach (NetworkInventory p in duo)
                {
                    var ph = p.GetComponent<Health>();
                    if (ph != null && ph.IsAlive && ph.Fraction < 0.75f) ph.Heal(ph.Max, null);
                }
            }

            // Warten, ohne dass die Testfiguren sterben (das Herz trifft weiter, die Finsternis auch)
            IEnumerator Hold(float seconds, System.Func<bool> until = null)
            {
                float t = 0f;
                while (t < seconds && (until == null || !until()))
                {
                    HealAll();
                    yield return new WaitForSecondsRealtime(0.25f);
                    t += 0.25f;
                }
                HealAll();
            }

            void State(string when) =>
                Line($"  ·     {when}: Kampf={(enc.Engaged ? "ja" : "nein")}, Versuch {enc.Attempts}, Herz {heart.State} {hh.Current:0}/{hh.Max:0}, " +
                     $"Lampen {4 - enc.DarkLamps}/4, A {ha.Current:0}, B {hb.Current:0}");

            IEnumerator Fight(float seconds, IList<NetworkInventory> who, System.Func<bool> until = null)
            {
                float t = 0f;
                while (t < seconds && hh.IsAlive && (until == null || !until()))
                {
                    yield return AskAll(who, (p, i) => "goto", (p, i) =>
                    {
                        Vector3 o = Quaternion.Euler(0f, i * 60f + 150f, 0f) * Vector3.forward * 3.2f; // Nahkampf erreicht den Kristallriesen
                        Vector3 g = MineGround(home + o);
                        return V(g, YawTo(g, home));
                    });
                    yield return AskAll(who, (p, i) => "attack", (p, i) => id);
                    yield return new WaitForSecondsRealtime(1f);
                    t += 1.3f;
                    HealAll();
                }
            }

            yield return Fight(2f, duo);
            yield return WaitUntil(() => enc.Engaged, 5f);
            Check(enc.Engaged, "Das Grubenherz erwacht");
            Check(announcements.Count > 0, "Ansage: " + (announcements.Count > 0 ? announcements[0] : "–"));

            // Lichtfresser kriechen zu den Lampen und loeschen eine
            yield return WaitUntil(() => enc.LivingAdds >= 2, 6f);
            var eaters = enc.Adds.Where(x => x != null).ToList();
            Check(eaters.Count >= 2 && eaters.All(x => enc.LampOf(x) >= 0 && !x.InCombat), $"Lichtfresser kriechen zu den Lampen ({eaters.Count})");
            yield return Fight(25f, duo, () => enc.LampsLost >= 1);
            Check(enc.LampsLost >= 1, $"Ungestoert loeschen sie eine Lampe ({enc.LampsLost})");
            int darkIdx = Enumerable.Range(0, 4).FirstOrDefault(i => !enc.IsLit(i));
            yield return new WaitForSecondsRealtime(1f);
            yield return Ask(b, "heart");
            Check(!lamps[darkIdx].IsLit && _last.data.Contains("0"), $"Die Lampe ist aus – auf dem Server und bei Krisbot ({_last.data})");
            Check(announcements.Any(t => t.Contains("erlischt")), "Ansage: „Eine Grubenlampe erlischt“");

            // Dunkle Lampen heilen das Herz
            yield return AskAll(duo, (p, i) => "stop", (p, i) => "");
            hh.ApplyDamage(new DamageInfo { Amount = Mathf.Round(hh.Current - hh.Max * 0.5f), Source = a.gameObject, WeaponId = "test" });
            yield return Hold(0.5f);
            float before = hh.Current;
            yield return Hold(2.5f);
            Check(hh.Current > before, $"Das Herz trinkt Licht und heilt sich ({before:0} -> {hh.Current:0})");

            // Krisbot zuendet die Lampe wieder an
            darkIdx = Enumerable.Range(0, 4).FirstOrDefault(i => !enc.IsLit(i));
            Vector3 lp = enc.LampPosition(darkIdx);
            Vector3 toHeart = home - lp; toHeart.y = 0f;
            yield return Ask(b, "goto", V(MineGround(lp + toHeart.normalized * 1.3f), 0f));
            yield return Hold(7f, () => enc.IsLit(darkIdx));
            Check(enc.IsLit(darkIdx) && enc.LampsRelit >= 1, "Wer an der dunklen Lampe steht, zuendet sie wieder an");
            yield return Hold(1f);
            yield return Ask(b, "heart");
            Check(lamps[darkIdx].IsLit && _last.data[6 + darkIdx] == '1', $"Krisbot sieht die Lampe wieder brennen ({_last.data})");

            // Stoert man einen Lichtfresser, laesst er von der Lampe ab
            foreach (EnemyBrain e in enc.Adds.Where(x => x != null && x.Health != null && x.Health.IsAlive).ToList())
                e.Health.ApplyDamage(new DamageInfo { Amount = e.Health.Max * 5f, Source = a.gameObject, WeaponId = "test" });
            yield return new WaitForSecondsRealtime(0.5f);
            enc.ServerSpawnEaters(1);
            yield return new WaitForSecondsRealtime(0.5f);
            EnemyBrain lone = enc.Adds.LastOrDefault(x => x != null && x.Health != null && x.Health.IsAlive);
            Check(lone != null && !lone.InCombat, "Ein neuer Lichtfresser macht sich auf den Weg");
            if (lone != null)
            {
                string eid = lone.GetComponent<NetworkObject>().ObjectId.ToString();
                // Er laeuft weiter – hinterher, bis ein Treffer sitzt
                for (int tries = 0; tries < 6 && lone != null && !lone.InCombat; tries++)
                {
                    Vector3 w = lone.transform.position;
                    Vector3 g = MineGround(w + lone.transform.forward * 1.6f);
                    yield return Ask(a, "goto", V(g, YawTo(g, w)));
                    yield return Ask(a, "attack", eid);
                    yield return Hold(1f, () => lone == null || lone.InCombat);
                }
                Check(lone != null && lone.InCombat, "Angegriffen, wehrt er sich statt die Lampe zu loeschen");
                int lost = enc.LampsLost;
                yield return Hold(2f);
                if (lone != null && lone.Health.IsAlive) lone.Health.ApplyDamage(new DamageInfo { Amount = lone.Health.Max * 5f, Source = a.gameObject, WeaponId = "test" });
                Check(enc.LampsLost == lost, "Die Lampe brennt weiter");
                yield return Ask(a, "stop", "");
            }

            // Finsternis
            State("Vor der Finsternis");
            enc.ServerDarkenAllForTest();
            yield return AskAll(duo, (p, i) => "goto", (p, i) => V(MineGround(home + new Vector3(i * 3f - 1.5f, 0f, -12f)), 0f));
            float dark0 = dark;
            yield return Hold(6f, () => dark > dark0);
            yield return Ask(b, "heart");
            Check(enc.DarkLamps == 4 && _last.data.StartsWith("lamps=0000"), $"Alle Lampen aus ({_last.data})");
            Check(dark > dark0 && announcements.Any(t => t.Contains("Finsternis")), $"Die Finsternis frisst an der Gruppe ({dark - dark0:0} Schaden)");
            HealAll();
            Vector3 l0 = enc.LampPosition(0);
            Vector3 th = home - l0; th.y = 0f;
            yield return Ask(b, "goto", V(MineGround(l0 + th.normalized * 1.3f), 0f));
            yield return Hold(7f, () => enc.IsLit(0));
            float dark1 = dark;
            yield return Hold(4f);
            Check(enc.IsLit(0) && dark == dark1, "Brennt eine Lampe wieder, endet die Finsternis");

            // Sieg
            enc.ServerRelightAllForTest();
            foreach (EnemyBrain e in enc.Adds.Where(x => x != null && x.Health != null && x.Health.IsAlive).ToList())
                e.Health.ApplyDamage(new DamageInfo { Amount = e.Health.Max * 5f, Source = a.gameObject, WeaponId = "test" });
            int xp0 = a.GetComponent<NetworkProgression>().ServerXp;
            State("Vor dem letzten Schlag");
            hh.ApplyDamage(new DamageInfo { Amount = Mathf.Max(1f, hh.Current - FinishRest(hh, 0.01f)), Source = a.gameObject, WeaponId = "test" });
            yield return Fight(40f, duo);
            yield return AskAll(duo, (p, i) => "stop", (p, i) => "");
            if (hh.IsAlive) State("Herz lebt noch");
            Check(!hh.IsAlive, "Maxmod und Krisbot zerschlagen das Grubenherz");
            yield return WaitUntil(() => enc.Defeated, 3f);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(b, "heart");
            Check(lamps.All(l => l.IsLit) && _last.data.StartsWith("lamps=1111"), $"Danach brennen alle Lampen wieder ({_last.data})");
            Check(enc.LivingAdds == 0, "Keine Lichtfresser mehr");
            var corpse = heart.GetComponent<Ashveil.Gameplay.Items.CorpseLoot>();
            Check(corpse != null && corpse.HasAnyLoot, "Das Grubenherz hinterlaesst Beute");
            string[] heartItems = { "item.salt_giant_cuirass", "item.shadowhide_jerkin", "item.lamplight_staff", "item.heart_crystal_blade" };
            Check(corpse != null && corpse.Get(a.ProfileId).Any(st => heartItems.Contains(st.id)) && corpse.Get(a.ProfileId).Any(st => st.id == "item.heart_shard"),
                "Eigene Beute fuer Maxmod: ein Stueck aus der Beute des Herzens und ein Herzsplitter");
            Check(a.GetComponent<NetworkProgression>().ServerXp > xp0 || a.ServerModel.Level > 12, "Erfahrung fuer den Endboss");
            Check(announcements.Any(t => t.Contains("zerspringt")), "Ansage zum Sieg");

            ha.Damaged -= onDark;
            hb.Damaged -= onDark;
            EnemyEncounter.Announced -= onAnnounce;
            HeartEncounter.FirstEaterOverride = -1f;
            foreach (NetworkInventory p in duo) { var ph = p.GetComponent<Health>(); if (ph != null && ph.IsAlive) ph.Heal(ph.Max, null); }
        }

        /// <summary>M6.5: Der Ertrunkene Kapitaen (optional) – Breitseite in einer Linie, Matrosen, Sieg mit Beute.</summary>
        private IEnumerator CaptainSuite(NetworkInventory a, NetworkInventory b, DungeonSlot slot)
        {
            Line("");
            Line("[Optional: Der Ertrunkene Kapitaen]");
            var duo = new List<NetworkInventory> { a, b };
            var enc = slot.GetComponentInChildren<CaptainEncounter>();
            Check(enc != null, "Begegnung Kapitaen liegt in der Wrackhoehle");
            if (enc == null) yield break;
            yield return WaitUntil(() => enc.Boss != null, 3f);
            EnemyBrain cap = enc.Boss;
            Check(cap != null && cap.Level == 13 && cap.Definition.elite, $"Der Kapitaen steht vor seinem Wrack (Stufe {(cap != null ? cap.Level : 0)}, Elite)");
            if (cap == null) yield break;
            var ch = cap.GetComponent<Health>();
            string id = cap.GetComponent<NetworkObject>().ObjectId.ToString();
            var announcements = new List<string>();
            System.Action<Vector3, string> onAnnounce = (w, t) => { if (slot.Contains(w)) announcements.Add(t); };
            EnemyEncounter.Announced += onAnnounce;
            int booms = 0;
            System.Action<Vector3, float, bool> onBoom = (c, r, any) => booms++;
            cap.StrikeExploded += onBoom;
            CaptainEncounter.FirstBroadsideOverride = 2f;

            void HealAll()
            {
                foreach (NetworkInventory p in duo)
                {
                    var ph = p.GetComponent<Health>();
                    if (ph != null && ph.IsAlive && ph.Fraction < 0.75f) ph.Heal(ph.Max, null);
                }
            }

            IEnumerator Fight(float seconds)
            {
                float t = 0f;
                while (t < seconds && ch.IsAlive)
                {
                    Vector3 w = cap.transform.position;
                    yield return AskAll(duo, (p, i) => "goto", (p, i) =>
                    {
                        Vector3 o = Quaternion.Euler(0f, i * 140f + 30f, 0f) * Vector3.forward * 2.2f;
                        Vector3 g = MineGround(w + o);
                        return V(g, YawTo(g, w));
                    });
                    yield return AskAll(duo, (p, i) => "attack", (p, i) => id);
                    yield return new WaitForSecondsRealtime(1f);
                    t += 1.3f;
                    HealAll();
                }
            }

            yield return Fight(2f);
            yield return WaitUntil(() => enc.Engaged, 5f);
            Check(enc.Engaged, "Der Kapitaen stellt sich");
            Check(announcements.Count > 0, "Ansage: " + (announcements.Count > 0 ? announcements[0] : "–"));

            yield return WaitUntil(() => enc.Broadsides >= 1, 6f);
            var line = new List<Vector3>(enc.LastLine);
            bool straight = line.Count >= 3;
            if (straight)
            {
                Vector3 d0 = line[1] - line[0]; d0.y = 0f;
                for (int i = 2; i < line.Count; i++)
                {
                    Vector3 di = line[i] - line[i - 1]; di.y = 0f;
                    if (Vector3.Angle(d0, di) > 25f) straight = false;
                }
            }
            Check(enc.Broadsides >= 1 && straight, $"Breitseite: {line.Count} Einschlaege in einer Linie");
            yield return Fight(3.5f);
            Check(booms >= line.Count && line.Count > 0, $"Die Einschlaege gehen nacheinander hoch ({booms})");

            ch.ApplyDamage(new DamageInfo { Amount = Mathf.Max(1f, ch.Current - ch.Max * 0.55f), Source = a.gameObject, WeaponId = "test" });
            yield return WaitUntil(() => enc.CrewWaves >= 1, 3f);
            yield return new WaitForSecondsRealtime(1f);
            Check(enc.CrewWaves == 1 && enc.LivingAdds == 2, $"Bei 60 %: „Alle Mann an Deck!“ – zwei Matrosen ({enc.LivingAdds})");
            Check(enc.Adds.All(x => x == null || x.InCombat), "Die Matrosen greifen sofort an");
            ch.ApplyDamage(new DamageInfo { Amount = Mathf.Max(1f, ch.Current - ch.Max * 0.25f), Source = a.gameObject, WeaponId = "test" });
            yield return WaitUntil(() => enc.CrewWaves >= 2, 3f);
            Check(enc.CrewWaves == 2 && enc.Enraged, "Bei 30 % die zweite Welle und doppelte Breitseiten");

            int xp0 = a.GetComponent<NetworkProgression>().ServerXp;
            ch.ApplyDamage(new DamageInfo { Amount = Mathf.Max(1f, ch.Current - FinishRest(ch, 0.02f)), Source = a.gameObject, WeaponId = "test" });
            yield return Fight(40f);
            yield return AskAll(duo, (p, i) => "stop", (p, i) => "");
            Check(!ch.IsAlive, "Maxmod und Krisbot besiegen den Kapitaen");
            yield return WaitUntil(() => enc.Defeated, 3f);
            yield return new WaitForSecondsRealtime(1.5f);
            Check(enc.Defeated && enc.LivingAdds == 0, "Die Mannschaft geht mit ihm unter");
            var corpse = cap.GetComponent<Ashveil.Gameplay.Items.CorpseLoot>();
            string[] capItems = { "item.drowned_tricorne", "item.drowned_sabre", "item.brine_focus" };
            Check(corpse != null && corpse.Get(a.ProfileId).Any(st => capItems.Contains(st.id)) && corpse.Get(b.ProfileId).Any(st => capItems.Contains(st.id)),
                "Jeder bekommt ein Stueck aus der Beute des Kapitaens (persoenlich, passend zur Klasse)");
            Check(announcements.Any(t => t.Contains("Landgang")), "Ansage: „Endlich … Landgang.“");
            Check(a.GetComponent<NetworkProgression>().ServerProgress(QShip, 0) >= 1, "Zaehlt fuer „Das Schiff im Berg“");

            cap.StrikeExploded -= onBoom;
            EnemyEncounter.Announced -= onAnnounce;
            CaptainEncounter.FirstBroadsideOverride = -1f;
            foreach (NetworkInventory p in duo) { var ph = p.GetComponent<Health>(); if (ph != null && ph.IsAlive) ph.Heal(ph.Max, null); }
        }

        /// <summary>P2 Balance: Rest fuer den echten Todesstoss – hoechstens 150 Leben (Bosse haben jetzt Tabellenleben).</summary>
        private static float FinishRest(Health h, float fraction) => Mathf.Min(h.Max * fraction, 150f);
    }
}
