using System.Collections;
using System.IO;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Quests;
using FishNet;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Networking
{
    /// <summary>
    /// Abnahmetest Milestone 4 mit zwei echten Spielern (frische Charaktere auf Stufe 1):
    /// Tutorialkette bis Stufe 3, Erfahrung fuer jeden Beteiligten, Auftragsziele (Gespraech, Sammeln,
    /// Erkunden, Toeten), Belohnung waehlen, alles-oder-nichts bei vollem Rucksack, Handel mit Rueckkauf,
    /// Speichern und Wiederverbinden. Start: "Ashveil &gt; Test &gt; Fortschritt (Stufen, Auftraege, Handel)".
    /// Bericht: &lt;Projekt&gt;/Logs/Ashveil_ProgressTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        private const string FreshA = "fresh-a", FreshB = "fresh-b";
        private const string Q1 = "quest.blade_for_the_watch", Q2 = "quest.tough_hides", Q3 = "quest.view_over_the_hollow",
                             Q4 = "quest.the_faded", Q5 = "quest.powder_for_the_watch";
        private const string NBralt = "npc.oskar_bralt", NIlse = "npc.ilse_harrow", NMaelis = "npc.maelis",
                             NGerrit = "npc.gerrit", NCarto = "npc.cartographer";

        private IEnumerator ProgressSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(FreshA)) != null, 30f);
            Check(a != null, "Spieler A (Editor-Host, neuer Charakter) angemeldet");
            if (a == null) yield break;
            var pa = a.GetComponent<NetworkProgression>();
            Check(pa != null, "Spieler hat Fortschritt (NetworkProgression)");
            if (pa == null) yield break;
            yield return new WaitForSecondsRealtime(1f);

            // ---------------------------------------------------------- Stufenregeln (Update 26.09.)
            Line("");
            Line("[Stufenregeln]");
            Check(Progression.GlobalMaxLevel == 60 && Progression.CurrentContentTargetLevel == 20,
                $"Hoechststufe {Progression.GlobalMaxLevel}, Zielstufe des aktuellen Inhalts {Progression.CurrentContentTargetLevel} (keine Obergrenze)");
            bool rising = true;
            for (int l = 1; l < Progression.GlobalMaxLevel - 1; l++)
                if (Progression.XpToNext(l + 1) <= Progression.XpToNext(l)) rising = false;
            Check(rising && Progression.XpToNext(Progression.GlobalMaxLevel) == 0, "Jede Stufe bis 60 braucht mehr Erfahrung als die vorige");
            // Max 03.10.2026: ganze Kurve 1–60 im Classic-Mass (vorher: 19 -> 20 bewusst langsamer, x5)
            long total60 = Progression.TotalXpTo(Progression.GlobalMaxLevel);
            Check(total60 > 4_000_000 && total60 < 4_170_000 && Progression.XpToNext(1) == 400,
                $"Kurve im Classic-Mass: 1 -> 2 {Progression.XpToNext(1)} EP, bis 60 gesamt {total60:N0} EP (Classic-Groessenordnung ca. 4,08 Mio.)");
            Info($"EP je Stufe: 1:{Progression.XpToNext(1)} 5:{Progression.XpToNext(5)} 10:{Progression.XpToNext(10)} 15:{Progression.XpToNext(15)} " +
                 $"19:{Progression.XpToNext(19)} · bis 20 gesamt {Progression.TotalXpTo(20):N0} · bis 60 {Progression.TotalXpTo(60):N0}");

            // ---------------------------------------------------------- Neuer Charakter
            Line("");
            Line("[Neuer Charakter]");
            Check(a.ServerModel.Level == 1 && pa.ServerXp == 0 && pa.ServerMoney == Progression.StartMoney && !pa.ServerIsActive(Q1),
                $"Stufe 1, 0 Erfahrung, {Progression.StartMoney} Kupfer, keine Auftraege");
            yield return Ask(a, "progress");
            Check(_last.ok && _last.data.StartsWith($"lvl=1;xp=0;money={Progression.StartMoney};quests=;"), "Client zeigt denselben Stand");
            Check(Marker(NBralt) == QuestMarkerState.Available, "Hauptmann Bralt zeigt gelbes \"!\"");
            Check(Marker(NIlse) == QuestMarkerState.None && Marker(NMaelis) == QuestMarkerState.None, "Andere NPCs noch ohne Markierung");
            Info($"Erfahrung bis Stufe 2/3/4: {Progression.XpToNext(1)} / {Progression.XpToNext(2)} / {Progression.XpToNext(3)}");

            // ---------------------------------------------------------- Auftrag 1: Gespraech
            Line("");
            Line("[Auftrag 1: Eine Klinge fuer die Wacht]");
            yield return Ask(a, "npc-talk", NBralt);
            Check(!_last.ok && _last.data.Contains("Zu weit"), "Gespraech aus der Ferne abgelehnt (Server prueft Entfernung)");
            yield return GotoNpc(a, NBralt);
            yield return Ask(a, "npc-talk", NBralt);
            Check(_last.ok, "Gespraech mit Bralt");
            yield return Ask(a, "quest-accept", Q2);
            Check(!_last.ok && pa.ServerIsActive(Q2) == false, "Auftrag 2 vor Auftrag 1 abgelehnt (Voraussetzung)");
            yield return Ask(a, "quest-accept", Q1);
            Check(_last.ok && pa.ServerIsActive(Q1), "Auftrag 1 angenommen");
            Check(Marker(NIlse) == QuestMarkerState.Complete, "Ilse zeigt jetzt gelbes \"?\" (Abgabe)");
            yield return Ask(a, "quest-accept", Q1);
            Check(!_last.ok, "Doppelt annehmen abgelehnt");
            yield return GotoNpc(a, NIlse);
            yield return Ask(a, "npc-talk", NIlse);
            yield return Ask(a, "quest-turnin", Q1);
            int blade = IndexOf(a, "item.harrow_blade");
            Check(_last.ok && pa.ServerIsCompleted(Q1) && !pa.ServerIsActive(Q1), "Auftrag 1 abgegeben");
            Check(pa.ServerXp == 150 && pa.ServerMoney == Progression.StartMoney + 50, $"Belohnung: 150 Erfahrung, 50 Kupfer (jetzt {pa.ServerXp} EP, {pa.ServerMoney} Kupfer)");
            Check(blade >= 0 && a.ServerModel.Bag[blade].bound && IndexOf(a, "item.harrow_knife") < 0 && IndexOf(a, "item.cracked_focus") < 0,
                "Klassenpassende Klinge erhalten (Eidwaechter: Uebungsklinge, gebunden) – fremde Klassenteile nicht");
            yield return Ask(a, "autoequip", blade.ToString());
            Check(a.ServerModel.GetEquipped(EquipmentSlot.MainHand).id == "item.harrow_blade", "Neue Klinge angelegt");
            yield return Ask(a, "quest-turnin", Q1);
            Check(!_last.ok && pa.ServerXp == 150, "Zweites Abgeben bringt nichts");
            Check(Marker(NIlse) == QuestMarkerState.Available && Marker(NCarto) == QuestMarkerState.Available, "Folgeauftraege bei Ilse und Kartograf verfuegbar");

            // ---------------------------------------------------------- Zweiter Spieler
            string exe = Ashveil.Core.ProjectPaths.TestClientExe;
            if (!File.Exists(exe)) { Check(false, "Test-Client vorhanden"); yield break; }
            StartClient(exe, FreshB);
            NetworkInventory b = null;
            yield return WaitUntil(() => (b = OnlineInv(FreshB)) != null, 90f);
            Check(b != null, "Spieler B (Test-Client, neuer Charakter) angemeldet");
            if (b == null) yield break;
            var pb = b.GetComponent<NetworkProgression>();
            yield return new WaitForSecondsRealtime(2f);

            // ---------------------------------------------------------- Auftrag 2: Kampf und Beute
            Line("");
            Line("[Auftrag 2: Zaehe Felle – Kampf, Beute, Erfahrung]");
            yield return Ask(a, "npc-talk", NIlse);
            yield return Ask(a, "quest-accept", Q2);
            Check(_last.ok && pa.ServerIsActive(Q2), "Auftrag 2 angenommen");
            EnemySpawnArea wolves = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Definition != null && x.Definition.Id == "enemy.ash_wolf" && x.Prefab != null);
            Check(wolves != null, "Aschwolf-Vorlage gefunden");
            if (wolves == null) yield break;
            int kills = 0, levelBefore = a.ServerModel.Level;
            bool firstChecked = false;
            while (a.ServerModel.Count(Pelt) < 4 && kills < 7)
            {
                int xpA0 = pa.ServerXp, lvlA0 = a.ServerModel.Level, xpB0 = pb.ServerXp, rA0 = pa.ServerRestedXp, rB0 = pb.ServerRestedXp;
                GameObject wolf = null;
                yield return SpawnAndKill(wolves, 1, a, b, go => wolf = go);
                if (wolf == null) break;
                kills++;
                if (!firstChecked)
                {
                    firstChecked = true;
                    // Gruppen-EP wie Classic (Max 10.10.2026): zwei Beteiligte teilen die EP (Bonus erst ab drei); Ruhe verdoppelt den Anteil
                    float share = Progression.GroupShare(2);
                    int sA = Mathf.RoundToInt(Progression.KillXp(1, lvlA0) * share), sB = Mathf.RoundToInt(Progression.KillXp(1, 1) * share);
                    int expA = sA + Progression.RestedBonus(sA, rA0), expB = sB + Progression.RestedBonus(sB, rB0);
                    Check(pa.ServerXp == xpA0 + expA || a.ServerModel.Level > lvlA0, $"A bekommt seinen Anteil fuer den Wolf (+{expA})");
                    Check(pb.ServerXp == xpB0 + expB, $"B bekommt als Beteiligter seinen Anteil (+{expB}, geteilt wie Classic) – kein letzter Treffer noetig");
                }
                yield return LootAll(a, wolf);
                yield return LootAll(b, wolf);
            }
            Check(a.ServerModel.Count(Pelt) >= 4, $"Vier Pelze gesammelt ({kills} Woelfe, {a.ServerModel.Count(Pelt)} Pelze)");
            var hp = a.GetComponent<Health>();
            var eq = a.GetComponent<CharacterEquipment>();
            Check(hp != null && eq != null && Mathf.Approximately(hp.Max, eq.Stats.MaxHealth) && eq.Stats.Level == a.ServerModel.Level,
                $"Leben waechst mit der Stufe ({hp.Max:0})");
            yield return Ask(a, "progress");
            Check(_last.data.Contains(Q2 + ":4!"), "Sammelziel zaehlt den Rucksack (4/4)");
            Check(Marker(NMaelis) == QuestMarkerState.Complete, "Maelis zeigt gelbes \"?\"");
            int peltsBefore = a.ServerModel.Count(Pelt);
            // Woelfe koennen dieselben Teile zufaellig fallen lassen – deshalb den Zuwachs pruefen, nicht die Gesamtzahl
            int sabatonsBefore = a.ServerModel.Count("item.iron_sabatons"), glovesBefore = a.ServerModel.Count("item.stalker_gloves");
            yield return GotoNpc(a, NMaelis);
            yield return Ask(a, "npc-talk", NMaelis);
            yield return Ask(a, "quest-turnin", Q2);
            Check(!_last.ok && _last.data.Contains("Belohnung") && pa.ServerIsActive(Q2), "Abgabe ohne gewaehlte Belohnung abgelehnt");
            yield return Ask(a, "quest-turnin", Q2 + " 2");
            Check(_last.ok && pa.ServerIsCompleted(Q2), "Auftrag 2 abgegeben");
            // EP-Kurve im Classic-Mass (Max 03.10.2026): der erste Aufstieg kommt mit der Abgabe von Auftrag 2, nicht schon durch die Woelfe
            Check(a.ServerModel.Level > levelBefore, $"Stufenaufstieg durch Kampf und Auftraege (jetzt Stufe {a.ServerModel.Level}, {pa.ServerXp} EP)");
            Check(a.ServerModel.Count(Pelt) == peltsBefore - 4, "Genau vier Pelze abgegeben");
            Check(a.ServerModel.Count("item.iron_sabatons") == sabatonsBefore + 1 && a.ServerModel.Count("item.stalker_gloves") == glovesBefore,
                "Gewaehlte Belohnung (Eisenschuhe) erhalten, nur diese");

            // ---------------------------------------------------------- Auftrag 3: Erkunden
            Line("");
            Line("[Auftrag 3: Blick ueber die Senke – Erkunden]");
            yield return GotoNpc(a, NCarto);
            yield return Ask(a, "npc-talk", NCarto);
            yield return Ask(a, "quest-accept", Q3);
            Check(_last.ok && pa.ServerIsActive(Q3), "Auftrag 3 angenommen");
            QuestArea lookout = QuestArea.Find("area.lookout");
            Check(lookout != null, "Aussichtsfels mit Auftragsziel in der Szene");
            if (lookout == null) yield break;
            Vector3 top = lookout.transform.position;
            Vector3 foot = new Vector3(top.x, 0.05f, top.z) + (new Vector3(-top.x, 0f, -top.z).normalized * 5f);
            yield return Ask(a, "goto", V(foot, 0f));
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Ask(a, "progress");
            Check(_last.data.Contains(Q3 + ":0"), "Am Fuss des Felsens zaehlt es noch nicht");
            yield return Ask(a, "goto", V(top + Vector3.up * 0.1f, 0f));
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Ask(a, "progress");
            Check(_last.data.Contains(Q3 + ":1!"), "Oben auf dem Aussichtsfels: Ziel erreicht");
            yield return GotoNpc(a, NCarto);
            yield return Ask(a, "npc-talk", NCarto);
            int xp3 = pa.ServerXp, lvl3 = a.ServerModel.Level;
            int filled = a.ServerFillFreeSlots(Filler);
            yield return Ask(a, "quest-turnin", Q3);
            Check(!_last.ok && _last.data.Contains("Kein Platz") && pa.ServerIsActive(Q3) && pa.ServerXp == xp3 && a.ServerModel.Level == lvl3,
                $"Voller Rucksack ({filled} Plaetze gefuellt): Abgabe abgelehnt, nichts verloren, keine Erfahrung");
            a.ServerRemoveAll(Filler);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Ask(a, "quest-turnin", Q3);
            Check(_last.ok && pa.ServerIsCompleted(Q3) && a.ServerModel.Count("item.grey_wool_cloak") == 1, "Mit Platz: Auftrag 3 abgegeben, Umhang erhalten");
            // Classic-Mass (Max 03.10.2026): 1 -> 3 braucht 1 300 EP; die Tutorialkette bringt Stufe 2 (vorher Stufe 3)
            Check(a.ServerModel.Level >= 2, $"Neuer Spieler erreicht mit der Tutorialkette Stufe 2 (Stufe {a.ServerModel.Level}, {pa.ServerXp} EP)");

            // ---------------------------------------------------------- Handel
            Line("");
            Line("[Handel]");
            yield return GotoNpc(a, NMaelis);
            yield return Ask(a, "npc-talk", NMaelis);
            NpcDefinition maelis = GameData.Npc(NMaelis);
            int draught = a.ServerModel.Count("item.healing_draught");
            long m0 = pa.ServerMoney;
            int price = Progression.BuyPrice(maelis.stock[0].item, maelis.stock[0].price);
            yield return Ask(a, "buy", "0 1");
            Check(_last.ok && pa.ServerMoney == m0 - price && a.ServerModel.Count("item.healing_draught") == draught + 1,
                $"Heiltrank gekauft ({price} Kupfer)");
            long m1 = pa.ServerMoney;
            pa.ServerSetMoney(1);
            yield return Ask(a, "buy", "0 1");
            Check(!_last.ok && _last.data.Contains("Geld") && a.ServerModel.Count("item.healing_draught") == draught + 1, "Zu wenig Geld: Kauf abgelehnt");
            pa.ServerSetMoney(m1);
            a.ServerGive(new ItemStack("item.broken_pocketwatch", 1));
            int watch = IndexOf(a, "item.broken_pocketwatch");
            int watchValue = GameData.Item("item.broken_pocketwatch").sellPrice;
            yield return Ask(a, "sell", watch.ToString());
            Check(_last.ok && pa.ServerMoney == m1 + watchValue && a.ServerModel.Count("item.broken_pocketwatch") == 0, $"Taschenuhr verkauft (+{watchValue} Kupfer)");
            yield return Ask(a, "buyback", "0");
            Check(_last.ok && pa.ServerMoney == m1 && a.ServerModel.Count("item.broken_pocketwatch") == 1, "Zurueckgekauft zum selben Preis");
            a.ServerGive(new ItemStack("item.faded_letter", 1));
            yield return Ask(a, "sell", IndexOf(a, "item.faded_letter").ToString());
            Check(!_last.ok && a.ServerModel.Count("item.faded_letter") == 1, "Auftragsgegenstand ist unverkaeuflich");
            yield return GotoNpc(a, NBralt);
            yield return Ask(a, "npc-talk", NBralt);
            yield return Ask(a, "sell", IndexOf(a, "item.broken_pocketwatch").ToString());
            Check(!_last.ok && a.ServerModel.Count("item.broken_pocketwatch") == 1, "Beim Hauptmann (kein Haendler) kein Verkauf");

            // ---------------------------------------------------------- Zwei Spieler: persoenliche Auftraege
            Line("");
            Line("[Zwei Spieler]");
            Check(!pb.ServerIsCompleted(Q1) && !pb.ServerIsActive(Q2), "B's Auftraege bleiben unberuehrt (persoenlich)");
            yield return Ask(a, "quest-accept", Q4);
            Check(_last.ok && pa.ServerIsActive(Q4), "A nimmt \"Die Verblichenen\" an (Stufe 2 + Auftrag 2 noetig)");
            yield return GotoNpc(b, NBralt);
            yield return Ask(b, "npc-talk", NBralt);
            yield return Ask(b, "quest-accept", Q4);
            Check(!_last.ok && !pb.ServerIsActive(Q4), "B (Stufe 1, ohne Vorauftraege) bekommt ihn nicht");
            yield return Ask(b, "quest-accept", Q1);
            Check(_last.ok && pb.ServerIsActive(Q1), "B nimmt Auftrag 1 an");
            EnemySpawnArea faded = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Definition != null && x.Definition.Id == "enemy.faded_one" && x.Prefab != null);
            if (faded != null)
            {
                int xpB0 = pb.ServerXp;
                GameObject husk = null;
                // Kampf draussen am Feuer (der Hauptmann sitzt seit dem Nachtrag in der Halle des Riesen – dort ist es eng)
                StartCoroutine(Ask(b, "goto", V(new Vector3(-3f, 0.3f, 6f), 0f)));
                yield return Ask(a, "goto", V(new Vector3(0f, 0.3f, 6f), 0f));
                yield return new WaitForSecondsRealtime(1f);
                yield return SpawnAndKill(faded, 1, a, b, go => husk = go);
                yield return Ask(a, "progress");
                Check(husk != null && _last.data.Contains(Q4 + ":1"), "Verblichener besiegt: A's Toetungsziel 1/4");
                Check(pb.ServerXp > xpB0 && !pb.ServerIsActive(Q4), "B bekommt Erfahrung, aber keinen fremden Auftragsfortschritt");
            }
            else Check(false, "Verblichenen-Vorlage gefunden");

            // ---------------------------------------------------------- Speichern und Wiederverbinden
            Line("");
            Line("[Speichern und Wiederverbinden]");
            a.SaveNow();
            bool loaded = CharacterStore.Service.TryLoad(FreshA, out CharacterSaveData sa);
            Check(loaded && sa.level == a.ServerModel.Level && sa.xp == pa.ServerXp && sa.money == pa.ServerMoney,
                $"Speicherstand A: Stufe {sa?.level}, {sa?.xp} EP, {sa?.money} Kupfer");
            Check(loaded && sa.completedQuests.Count == 3 && sa.quests.Count == 1 && sa.quests[0].id == Q4 && sa.quests[0].progress[0] == 1,
                "Speicherstand A: 3 erledigte Auftraege, \"Die Verblichenen\" mit Fortschritt 1");
            NetworkInventory bOld = b;
            yield return Ask(b, "reconnect");
            NetworkInventory b2 = null;
            yield return WaitUntil(() => (b2 = OnlineInv(FreshB)) != null && b2 != bOld, 40f);
            Check(b2 != null && b2 != bOld, "B ist wieder da");
            if (b2 != null)
            {
                yield return new WaitForSecondsRealtime(1.5f);
                var pb2 = b2.GetComponent<NetworkProgression>();
                yield return Ask(b2, "progress");
                Check(pb2 != null && pb2.ServerIsActive(Q1) && _last.ok && _last.data.Contains(Q1), "B hat nach dem Wiederverbinden denselben Auftragsstand");
                Check(pb2 != null && pb2.ServerXp > 0, $"B's Erfahrung ist erhalten ({pb2?.ServerXp} EP)");
            }
            Info($"A: Stufe {a.ServerModel.Level}, {pa.ServerXp} EP, {pa.ServerMoney} Kupfer · {Describe(a)}");
        }

        // ================================================================== Hilfen (Fortschritt)

        private static QuestMarkerState Marker(string npcId) =>
            QuestRules.MarkerFor(npcId, ProgressionClient.Current, InventoryClient.Current != null ? InventoryClient.Current.Class : null);

        /// <summary>Vor den NPC stellen (zum Lagerfeuer hin versetzt), Blick auf ihn.</summary>
        private IEnumerator GotoNpc(NetworkInventory who, string npcId)
        {
            NpcIdentity n = NpcIdentity.Find(npcId);
            if (n == null) yield break;
            Vector3 p = n.transform.position;
            Vector3 toCenter = new Vector3(-p.x, 0f, -p.z);
            Vector3 dir = toCenter.sqrMagnitude > 0.01f ? toCenter.normalized : Vector3.forward;
            Vector3 stand = p + dir * 2f;
            if (NavMesh.SamplePosition(stand, out NavMeshHit hit, 3f, NavMesh.AllAreas)) stand = hit.position;
            yield return Ask(who, "goto", V(stand + Vector3.up * 0.05f, Quaternion.LookRotation(-dir).eulerAngles.y));
        }

        /// <summary>Gegner neben A erzeugen, A und B greifen an, bis er faellt (Test-Hilfe: Leben wird zwischendurch aufgefuellt).</summary>
        private IEnumerator SpawnAndKill(EnemySpawnArea template, int level, NetworkInventory a, NetworkInventory b, System.Action<GameObject> done)
        {
            Vector3 basePos = a.transform.position + a.transform.forward * 4f;
            if (NavMesh.SamplePosition(basePos, out NavMeshHit hit, 6f, NavMesh.AllAreas)) basePos = hit.position;
            GameObject go = Instantiate(template.Prefab, basePos, Quaternion.identity);
            go.GetComponent<EnemyBrain>().Configure(level, basePos, "progress-test");
            InstanceFinder.ServerManager.Spawn(go);
            yield return new WaitForSecondsRealtime(0.8f);
            var hp = go.GetComponent<Health>();
            int id = go.GetComponent<NetworkObject>().ObjectId;
            float t = 0f;
            while (hp != null && hp.IsAlive && t < 60f)
            {
                Vector3 w = go.transform.position;
                yield return Ask(a, "goto", V(w + new Vector3(1.6f, 0f, -1.2f), -50f));
                if (b != null) yield return Ask(b, "goto", V(w + new Vector3(-1.6f, 0f, -1.2f), 50f));
                yield return Ask(a, "attack", id.ToString());
                if (b != null) yield return Ask(b, "attack", id.ToString());
                float w8 = 0f;
                while (hp.IsAlive && w8 < 5f) { w8 += 0.25f; t += 0.25f; yield return new WaitForSecondsRealtime(0.25f); }
                t += 4f;
                foreach (NetworkInventory p in new[] { a, b })
                {
                    var ph = p != null ? p.GetComponent<Health>() : null;
                    if (ph != null && ph.IsAlive && ph.Fraction < 0.5f) ph.Heal(ph.Max, null);
                }
            }
            yield return Ask(a, "stop");
            if (b != null) yield return Ask(b, "stop");
            yield return new WaitForSecondsRealtime(0.6f);
            done(hp != null && !hp.IsAlive ? go : null);
        }

        private IEnumerator LootAll(NetworkInventory who, GameObject corpse)
        {
            if (corpse == null) yield break;
            var c = corpse.GetComponent<CorpseLoot>();
            if (c == null || c.Get(who.ProfileId).Count == 0) yield break;
            yield return Ask(who, "goto", V(corpse.transform.position + new Vector3(1.2f, 0f, -1f), 0f));
            if (corpse == null) yield break; // Leiche waehrend des Hinlaufens verschwunden
            string id = corpse.GetComponent<NetworkObject>().ObjectId.ToString();
            for (int i = 0; i < 3; i++)
            {
                yield return Ask(who, "loot-open", id);
                if (_last.ok) break;
                yield return new WaitForSecondsRealtime(1f);
            }
            if (_last.ok) yield return Ask(who, "loot-takeall");
        }
    }
}
