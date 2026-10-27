using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Story;
using FishNet;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;
using C = Ashveil.Data.GlockenrastCatalog;

namespace Ashveil.Networking
{
    /// <summary>
    /// P3 Glockenrast (1 Spieler): eine neue Figur (Eidwaechter, Stufe 5) spielt alle dreizehn Auftraege G1–G13 in der Reihenfolge
    /// der Kette. Geprueft: Inhalte vorhanden (Auftraege, Figuren, Orte, Fundstellen), Voraussetzungen/Kettenreihenfolge, Ziele
    /// (Gespraech, Toeten, Auftragsbeute nur mit Auftrag, Fundstuecke nur einmal und nur aus der Naehe, Erkunden, Geschichtsschritte),
    /// Belohnungen (Erfahrung, Geld, feste und Wahl-Belohnung je Klasse), EP-Summe, Entdeckungs-EP, Geschichtsmarken am Ende,
    /// Gruss nach Marke und Speicherstand. Test-Hilfen: Spielfigur unverwundbar, Gegner werden neben der Figur erzeugt und mit
    /// einem Treffer der Figur besiegt (der Kampf selbst ist Gegenstand anderer Tests); fehlt fuer einen Auftrag die Stufe,
    /// wird die Luecke als „Nachhilfe“ gutgeschrieben und im Bericht ausgewiesen (Balance-Hinweis).
    /// Start: "Ashveil › Test › Glockenrast (1 Spieler)". Bericht: Logs/…/Ashveil_GlockenrastTest.txt
    /// </summary>
    public sealed partial class DuoTestDirector
    {
        public const string GlockeFlag = "glockenrast";
        private static bool _glockeMode;
        private const string GlockeProfile = "duo-glocke";

        private long _grQuestXp, _grTopUp, _grKillXp, _grDiscoveryXp;

        private static long GrTotal(NetworkInventory a, NetworkProgression p) => Progression.TotalXpTo(a.ServerModel.Level) + p.ServerXp;

        private IEnumerator GlockenrastSuite()
        {
            NetworkInventory a = null;
            yield return WaitUntil(() => (a = OnlineInv(GlockeProfile)) != null, 30f);
            Check(a != null, "Spieler angemeldet (neue Figur)");
            if (a == null) yield break;
            var pa = a.GetComponent<NetworkProgression>();
            Check(pa != null, "Spieler hat Fortschritt (NetworkProgression)");
            if (pa == null) yield break;
            yield return new WaitForSecondsRealtime(3f); // Anmelden + erste Entdeckungspruefung (Startlager ohne EP)
            a.GetComponent<Health>()?.SetInvulnerableFor(7200f);
            _grQuestXp = _grTopUp = _grKillXp = _grDiscoveryXp = 0;

            // ---------------------------------------------------------- Inhalte
            Line("");
            Line("[Inhalte]");
            Check(a.ServerModel.Level == 5 && pa.ServerXp == 0 && C.Quests.All(q => !pa.ServerIsCompleted(q) && !pa.ServerIsActive(q)),
                $"Neue Figur auf Stufe 5, 0 EP, kein Glockenrast-Auftrag (Klasse {a.ServerModel.Class?.DisplayName})");
            List<QuestDefinition> defs = C.Quests.Select(GameData.Quest).ToList();
            Check(defs.All(d => d != null), $"Alle 13 Aufträge vorhanden ({defs.Count(d => d != null)}/13)");
            if (defs.Any(d => d == null)) yield break;
            Check(defs.Sum(d => d.xp) == C.QuestXpTotal, $"EP-Summe der Aufträge = {C.QuestXpTotal} (Tabelle der Vorgabe)");
            int[] minLv = { 5, 5, 5, 5, 6, 6, 6, 7, 7, 8, 8, 9, 9 };
            Check(defs.Select(d => d.minLevel).SequenceEqual(minLv), "Mindeststufen 5–9 wie in der Tabelle");
            Check(GrPrereqs(C.G2, C.G1) && GrPrereqs(C.G3, C.G2) && GrPrereqs(C.G4, C.G1) && GrPrereqs(C.G5, C.G1) && GrPrereqs(C.G6, C.G5) &&
                  GrPrereqs(C.G7, C.G1) && GrPrereqs(C.G8, C.G1) && GrPrereqs(C.G9, C.G2) && GrPrereqs(C.G10, C.G6, C.G8) &&
                  GrPrereqs(C.G11, C.G10) && GrPrereqs(C.G12, C.G11) && GrPrereqs(C.G13, C.G12) && GameData.Quest(C.G1).prerequisites.Count == 0,
                "Voraussetzungen wie freigegeben (G1 → G2/G4/G5/G7/G8, G2 → G3/G9, G6 + G8 → G10 → G11 → G12 → G13)");
            string[] npcs = { C.Bralt, C.Anselm, C.Runa, C.Merle, C.Jasko, C.Pilgrim, C.Tamo, C.Veit, C.Maelis };
            string missingNpc = string.Join(", ", npcs.Where(n => GameData.Npc(n) == null || NpcIdentity.Find(n) == null));
            Check(missingNpc.Length == 0, "Figuren in Daten und Szene: Bralt, Anselm, Runa, Merle, Jasko, Pilger, Tamo, Veit, Maelis" +
                                         (missingNpc.Length > 0 ? " – fehlt: " + missingNpc : ""));
            string[] areas = { C.AreaLumberCamp, C.AreaResinCamp, C.AreaSkullcap, C.AreaBellTower, C.AreaNorthMoor };
            Check(areas.All(x => QuestArea.Find(x) != null), $"Erkundungsziele in der Szene ({areas.Count(x => QuestArea.Find(x) != null)}/5)");
            Check(C.Nodes.All(n => StoryAnchor.Find(n.Action) != null), $"Fundstellen und Geschichtsschritte in der Szene ({C.Nodes.Count(n => StoryAnchor.Find(n.Action) != null)}/{C.Nodes.Count})");
            ItemDefinition gland = GameData.Item(C.ItemVenomGland);
            LootTableDefinition spitterLoot = GameData.Get<LootTableDefinition>("loot.needle_spitter");
            Check(gland != null && gland.category == ItemCategory.Quest && gland.questId == C.G3 && spitterLoot != null && spitterLoot.entries.Any(e => e.item == gland),
                "Giftdrüse ist Auftragsgegenstand von G3 und Beute der Nadelspeier");
            foreach (string w in new[] { "item.gr_camp_sword", "item.gr_camp_dagger", "item.gr_camp_staff", "item.gr_camp_bow", "item.gr_moor_sword", "item.gr_moor_bow" })
            {
                ItemDefinition d = GameData.Item(w);
                if (d == null || d.weapon == null) { Check(false, $"Belohnungswaffe {w} vorhanden"); continue; }
                float avg = (d.weapon.minDamage + d.weapon.maxDamage) * 0.5f;
                Info($"{d.DisplayName} (Stufe {d.requiredLevel}, Gegenstandsstufe {d.itemLevel}): {d.weapon.minDamage:0}–{d.weapon.maxDamage:0}, " +
                     $"Budget {Balance.WeaponAverage(d.itemLevel, d.quality):0.0} → ×{avg / Balance.WeaponAverage(d.itemLevel, d.quality):0.00}");
            }
            Check(pa.ServerDiscovered(KarethHub) && pa.ServerXp == 0, "Startlager gilt als entdeckt – ohne Erfahrung fürs Anmelden");

            // ---------------------------------------------------------- G1
            Line("");
            Line("[G1 Der Weg nach Norden]");
            yield return GrAccept(a, pa, C.G1);
            Check(Marker(C.Anselm) == QuestMarkerState.Complete, "Anselm zeigt gelbes „?“ (Gespräch erledigt die Abgabe)");
            long before = GrTotal(a, pa);
            NpcIdentity anselm = NpcIdentity.Find(C.Anselm);
            yield return GotoNpc(a, C.Anselm);
            yield return new WaitForSecondsRealtime(2.5f);
            long disc = GrTotal(a, pa) - before;
            _grDiscoveryXp += disc;
            Check(pa.ServerDiscovered(KarethQuestSitesHub) && disc == Progression.DiscoveryXp(5),
                $"Entdeckt: Glockenrast (+{disc} EP, erwartet {Progression.DiscoveryXp(5)} = 20 + 5 × Stufe 5)");
            yield return Ask(a, "npc-talk", C.Anselm);
            yield return Ask(a, "quest-accept", C.G8);
            Check(!_last.ok && !pa.ServerIsActive(C.G8), "G8 vor Abschluss von G1 abgelehnt (Kette)");
            yield return GrTurnIn(a, pa, C.G1);
            // Zurueck zum Wachtfeuer und wieder hinauf: die Glockenrast ist schon entdeckt
            yield return GotoNpc(a, C.Bralt);
            yield return new WaitForSecondsRealtime(1.5f);
            long again = GrTotal(a, pa);
            yield return GotoNpc(a, C.Anselm);
            yield return new WaitForSecondsRealtime(2f);
            Check(GrTotal(a, pa) == again, "Zweiter Besuch der Glockenrast gibt keine Entdeckungs-EP");
            Check(anselm != null && Vector3.Distance(anselm.transform.position, KarethQuestSitesCenter) < 25f, "Anselm steht an der Glockenrast");

            // ---------------------------------------------------------- G2 Nadeln im Schilf
            Line("");
            Line("[G2 Nadeln im Schilf]");
            yield return GrAccept(a, pa, C.G2);
            yield return Ask(a, "quest-accept", C.G3);
            Check(!_last.ok && !pa.ServerIsActive(C.G3), "G3 vor Abschluss von G2 abgelehnt (Kette)");
            yield return GrGotoSite(a, pa, "site.kareth.moorrand.west");
            int leaks = 0;
            yield return GrKill(a, pa, C.EnemySpitter, 5, 8, corpse => { if (corpse != null && corpse.Get(a.ProfileId).Any(s => s.id == C.ItemVenomGland)) leaks++; });
            Check(pa.ServerProgress(C.G2, 0) == 8, $"Acht Nadelspeier besiegt ({pa.ServerProgress(C.G2, 0)}/8)");
            Check(leaks == 0, "Ohne „Gegengift“ fällt keine Giftdrüse (Auftragsbeute)");
            yield return GrTurnIn(a, pa, C.G2);

            // ---------------------------------------------------------- G3 Gegengift
            Line("");
            Line("[G3 Gegengift]");
            yield return GrAccept(a, pa, C.G3);
            yield return GrGotoSite(a, pa, "site.kareth.moorrand.ost");
            int kills = 0;
            while (a.ServerModel.Count(C.ItemVenomGland) < 6 && kills < 30)
            {
                kills++;
                yield return GrKill(a, pa, C.EnemySpitter, 5, 1, null, C.ItemVenomGland);
            }
            Check(a.ServerModel.Count(C.ItemVenomGland) == 6, $"Sechs Giftdrüsen gesammelt ({kills} Nadelspeier, {a.ServerModel.Count(C.ItemVenomGland)} Drüsen)");
            int extra = 0;
            yield return GrKill(a, pa, C.EnemySpitter, 5, 1, corpse => { if (corpse != null && corpse.Get(a.ProfileId).Any(s => s.id == C.ItemVenomGland)) extra++; });
            Check(extra == 0, "Mit sechs Drüsen fallen keine weiteren");
            int potions = a.ServerModel.Count("item.healing_draught");
            yield return GrTurnIn(a, pa, C.G3);
            Check(a.ServerModel.Count("item.healing_draught") == potions + 3 && a.ServerModel.Count(C.ItemVenomGland) == 0,
                "Maelis nimmt die Drüsen, gibt drei Heiltränke");

            // ---------------------------------------------------------- G4 Ein Rad fuer Tamo
            Line("");
            Line("[G4 Ein Rad für Tamo]");
            yield return GrAccept(a, pa, C.G4);
            yield return GrTake(a, pa, C.NodeLetter, false); // falscher Auftrag: Brief gehoert zu G6
            Check(!pa.ServerStoryNote.EndsWith(": ok") && a.ServerModel.Count(C.ItemLetter) == 0, $"Fundstück eines anderen Auftrags nicht aufhebbar ({pa.ServerStoryNote})");
            yield return GrExplore(a, pa, C.G4, 0, C.AreaLumberCamp);
            StoryAnchor wheelAnchor = StoryAnchor.Find(C.NodeWheel);
            if (wheelAnchor != null)
            {
                yield return Ask(a, "goto", V(wheelAnchor.Position + new Vector3(12f, 0.3f, 0f), 0f));
                yield return Ask(a, "story-act", C.NodeWheel);
                Check(pa.ServerStoryNote.Contains("zu weit") && a.ServerModel.Count(C.ItemSpareWheel) == 0, "Aus 12 m Entfernung abgelehnt (Server prüft die Nähe)");
            }
            yield return GrTake(a, pa, C.NodeWheel, true);
            yield return Ask(a, "story-act", C.NodeWheel);
            Check(a.ServerModel.Count(C.ItemSpareWheel) == 1, $"Ersatzrad nur einmal aufhebbar ({pa.ServerStoryNote})");
            yield return new WaitForSecondsRealtime(0.8f);
            QuestNodeCondition wheelCond = wheelAnchor != null ? wheelAnchor.GetComponent<QuestNodeCondition>() : null;
            Check(wheelCond != null && wheelCond.Taken && !wheelCond.Available(), "Eigener Client: Ersatzrad genommen, nicht mehr anklickbar (Phasing)");
            long m4 = pa.ServerMoney;
            yield return GrTurnIn(a, pa, C.G4);
            Check(a.ServerModel.Count("item.wool_bag") >= 1 && pa.ServerMoney - m4 == 400, "Tamo: Geld und Beutel");

            // ---------------------------------------------------------- G5 Das leere Lager
            Line("");
            Line("[G5 Das leere Lager]");
            yield return GrEnsureLevel(a, pa, 6);
            yield return GrAccept(a, pa, C.G5);
            yield return GrGotoSite(a, pa, "site.kareth.holzfaellerlager");
            yield return GrKill(a, pa, C.EnemyFaded, 6, 6, null);
            Check(pa.ServerProgress(C.G5, 0) == 6, $"Sechs Verblichene besiegt ({pa.ServerProgress(C.G5, 0)}/6)");
            yield return GrTurnIn(a, pa, C.G5, "item.gr_camp_sword");
            Check(a.ServerModel.Count("item.gr_camp_sword") == 1 && a.ServerModel.Count("item.gr_camp_dagger") == 0 && a.ServerModel.Count("item.gr_camp_staff") == 0,
                "Waffe zur Wahl (Stufe 6): Lagerwächterklinge gewählt – nur diese");

            // ---------------------------------------------------------- G6 Harz ohne Haende
            Line("");
            Line("[G6 Harz ohne Hände]");
            yield return GrAccept(a, pa, C.G6);
            yield return GrExplore(a, pa, C.G6, 0, C.AreaResinCamp);
            yield return GrTake(a, pa, C.NodeLetter, true);
            yield return GrTake(a, pa, C.NodeLedger, true);
            yield return GrTurnIn(a, pa, C.G6);
            Check(a.ServerModel.Count(C.ItemLetter) == 0 && a.ServerModel.Count(C.ItemLedger) == 0, "Brief und Kassenbuch an Merle abgegeben");
            yield return Ask(a, "npc-talk", C.Merle);
            yield return Ask(a, "quest-accept", C.G10);
            Check(!_last.ok && !pa.ServerIsActive(C.G10), "G10 ohne G8 abgelehnt (braucht G6 und G8)");

            // ---------------------------------------------------------- G7 Die Schaedeldecke
            Line("");
            Line("[G7 Die Schädeldecke]");
            yield return GrAccept(a, pa, C.G7);
            yield return GrExplore(a, pa, C.G7, 0, C.AreaSkullcap);
            for (int i = 0; i < C.RubbingCount; i++) yield return GrTake(a, pa, C.NodeRubbing(i), true);
            Check(a.ServerModel.Count(C.ItemRubbing) == C.RubbingCount, $"Drei Abriebe ({a.ServerModel.Count(C.ItemRubbing)})");
            yield return GrTurnIn(a, pa, C.G7);
            Check(a.ServerModel.Count("item.gr_settler_ring") == 1, "Veit trägt ein – Siedlerring erhalten");

            // ---------------------------------------------------------- G8 Was man unter Wasser findet
            Line("");
            Line("[G8 Was man unter Wasser findet]");
            yield return GrEnsureLevel(a, pa, 7);
            yield return GrAccept(a, pa, C.G8);
            for (int i = 0; i < C.FragmentCount; i++) yield return GrTake(a, pa, C.NodeFragment(i), true);
            Check(pa.ServerProgress(C.G8, 0) == C.FragmentCount, $"Fünf Glockenscherben ({pa.ServerProgress(C.G8, 0)}/5)");
            yield return GrTurnIn(a, pa, C.G8);
            Check(a.ServerModel.Count("item.gr_founder_cloak") == 1 && a.ServerModel.Count(C.ItemBellFragment) == 0, "Anselm nimmt die Scherben, gibt den Gießermantel");

            // ---------------------------------------------------------- G9 Torf und Zorn
            Line("");
            Line("[G9 Torf und Zorn]");
            yield return GrAccept(a, pa, C.G9);
            yield return GrGotoSite(a, pa, "site.kareth.aschenweide");
            yield return GrKill(a, pa, C.EnemyPeatBrute, 7, 5, null);
            Check(pa.ServerProgress(C.G9, 0) == 5, $"Fünf Torfschrate besiegt ({pa.ServerProgress(C.G9, 0)}/5)");
            QuestDefinition g9 = GameData.Quest(C.G9);
            List<QuestItemReward> armorChoice = QuestRules.ForClass(g9.choiceItems, a.ServerModel.Class);
            Info("Rüstung zur Wahl für " + a.ServerModel.Class?.DisplayName + ": " + string.Join(", ", armorChoice.Select(r => r.item.DisplayName)));
            yield return GrTurnIn(a, pa, C.G9, "item.gr_moorwarden_gauntlets");
            Check(a.ServerModel.Count("item.gr_moorwarden_gauntlets") == 1, "Rüstungsteil zur Wahl: Panzerhandschuhe der Moorwache");

            // ---------------------------------------------------------- G10 Das Zeichen im Buch
            Line("");
            Line("[G10 Das Zeichen im Buch]");
            yield return GrEnsureLevel(a, pa, 8);
            yield return GrAccept(a, pa, C.G10);
            yield return GotoNpc(a, C.Runa);
            yield return Ask(a, "npc-talk", C.Runa);
            yield return Ask(a, "quest-turnin", C.G10);
            Check(!_last.ok && pa.ServerIsActive(C.G10), "Erst Anselm zur Rede stellen – Runa allein reicht nicht");
            yield return GotoNpc(a, C.Anselm);
            yield return Ask(a, "npc-talk", C.Anselm);
            Check(pa.ServerProgress(C.G10, 0) == 1, "Anselm zur Rede gestellt");
            yield return GrTurnIn(a, pa, C.G10);
            Check(pa.ServerGate().HasFlag(C.FlagConfrontation), "Marke: Aussprache");

            // ---------------------------------------------------------- G11 Der schweigende Turm
            Line("");
            Line("[G11 Der schweigende Turm]");
            yield return GrAccept(a, pa, C.G11);
            yield return GrExplore(a, pa, C.G11, 0, C.AreaBellTower);
            yield return GrTake(a, pa, C.NodeStrike, false);
            Check(pa.ServerProgress(C.G11, 1) == 1 && pa.ServerStoryNote == C.NodeStrike + ": ok",
                $"Geschichtsschritt {C.StoryBellStruck}: Scherbe angeschlagen, der Boden antwortet ({pa.ServerStoryNote})");
            yield return Ask(a, "story-act", C.NodeStrike);
            Check(pa.ServerStoryNote.Contains("schon erledigt"), "Zweites Anschlagen ändert nichts");
            yield return GrTurnIn(a, pa, C.G11);

            // ---------------------------------------------------------- G12 Nordmoor
            Line("");
            Line("[G12 Nordmoor]");
            yield return GrEnsureLevel(a, pa, 9);
            yield return GrAccept(a, pa, C.G12);
            yield return GrExplore(a, pa, C.G12, 0, C.AreaNorthMoor);
            yield return GrKill(a, pa, C.EnemyPeatBrute, 9, 3, null);
            yield return GrKill(a, pa, C.EnemySpitter, 9, 3, null);
            Check(pa.ServerProgress(C.G12, 1) == 3 && pa.ServerProgress(C.G12, 2) == 3, "Sechs Moorgegner besiegt (3 Torfschrate, 3 Nadelspeier)");
            yield return GrTurnIn(a, pa, C.G12, null, false);
            Check(!_last.ok && pa.ServerIsActive(C.G12), "Abgabe bei Jasko erst, wenn er gefunden ist");
            yield return GrTake(a, pa, C.NodeJasko, false);
            Check(pa.ServerProgress(C.G12, 3) == 1, $"Geschichtsschritt {C.StoryJaskoFound}: Jasko gefunden");
            long m12 = pa.ServerMoney;
            yield return GrTurnIn(a, pa, C.G12);
            Check(pa.ServerMoney - m12 == 600, "Jasko gibt den Lohn (Geld)");

            // ---------------------------------------------------------- G13 Kommst du heute Nacht nach Hause?
            Line("");
            Line("[G13 Kommst du heute Nacht nach Hause?]");
            yield return GrAccept(a, pa, C.G13);
            yield return GrTurnIn(a, pa, C.G13, "item.gr_resin_pendant");
            Check(a.ServerModel.Count("item.gr_resin_pendant") == 1 && a.ServerModel.Count("item.gr_moor_sword") == 0,
                "Waffe oder Schmuck zur Wahl (Stufe 9): Harztropfen-Anhänger gewählt – nur dieser");
            GrEndFlags(pa);
            yield return new WaitForSecondsRealtime(1f);
            NpcDefinition anselmDef = GameData.Npc(C.Anselm);
            string greet = anselmDef != null ? anselmDef.GreetingFor(StoryClient.HasFlag) : "";
            Check(greet.StartsWith("(Anselm betet laut"), "Eigener Client: Anselm betet jetzt laut (Gruss nach Marke)");

            // ---------------------------------------------------------- Summen und Speicherstand
            Line("");
            Line("[Summen]");
            Check(C.Quests.All(pa.ServerIsCompleted), $"Alle 13 Aufträge abgeschlossen ({C.Quests.Count(pa.ServerIsCompleted)}/13)");
            Check(_grQuestXp == C.QuestXpTotal, $"EP aus Aufträgen: {_grQuestXp} (Soll {C.QuestXpTotal})");
            Info($"EP aus Siegen (Test-Gegner): {_grKillXp} · Entdeckungen: {_grDiscoveryXp} ({pa.ServerDiscoveredCount} Orte) · Nachhilfe: {_grTopUp}");
            Info($"Stand: Stufe {a.ServerModel.Level}, {pa.ServerXp}/{Progression.XpToNext(a.ServerModel.Level)} EP, {Progression.FormatMoney(pa.ServerMoney, false)}");
            Check(a.ServerModel.Level >= 9, $"Stufe 9 erreicht (Stufe {a.ServerModel.Level})");
            if (_grTopUp > 0) Info("Hinweis Balance: Aufträge + Siege + Entdeckungen reichten nicht ganz – die Nachhilfe zeigt die Lücke.");
            a.SaveNow();
            bool loaded = CharacterStore.Service.TryLoad(GlockeProfile, out CharacterSaveData sd);
            Check(loaded && sd.version == CharacterSaveData.CurrentVersion && C.Quests.All(q => sd.completedQuests.Contains(q)) &&
                  sd.discoveredAreas != null && sd.discoveredAreas.Contains(KarethQuestSitesHub) && sd.storyFlags.Contains(C.FlagHomecoming),
                $"Speicherstand v{(loaded ? sd.version : 0)}: 13 Aufträge, entdeckte Orte ({(loaded && sd.discoveredAreas != null ? sd.discoveredAreas.Count : 0)}), Marken");
        }

        private const string KarethHub = "site.kareth.hub.wachtfeuer";
        private const string KarethQuestSitesHub = "site.kareth.hub.glockenrast";
        private static Vector3 KarethQuestSitesCenter
        {
            get
            {
                Ashveil.World.WorldQuestSite s = Ashveil.World.WorldQuestSite.Find(KarethQuestSitesHub);
                return s != null ? s.transform.position : Vector3.zero;
            }
        }

        private void GrEndFlags(NetworkProgression pa)
        {
            QuestGate g = pa.ServerGate();
            Check(g != null && g.HasFlag(C.FlagHomecoming) && g.HasFlag(C.FlagRunaHoldsKey) && g.HasFlag(C.FlagAnselmPraysAloud) && g.HasFlag(C.FlagSeparateEnds),
                "Ende: Marken gesetzt (heimgekehrt, Runa hält den Schlüssel, Anselm betet laut, getrennte Enden)");
        }

        private static bool GrPrereqs(string quest, params string[] need)
        {
            QuestDefinition q = GameData.Quest(quest);
            if (q == null) return false;
            var have = q.prerequisites.Where(p => p != null).Select(p => p.Id).OrderBy(x => x).ToList();
            return have.SequenceEqual(need.OrderBy(x => x));
        }

        // ================================================================== Hilfen (Glockenrast)

        private IEnumerator GrAccept(NetworkInventory a, NetworkProgression pa, string quest)
        {
            QuestDefinition q = GameData.Quest(quest);
            long x0 = GrTotal(a, pa);
            yield return GotoNpc(a, q.giverNpc);
            yield return new WaitForSecondsRealtime(1.2f); // Entdeckung am Geber abwarten
            _grDiscoveryXp += GrTotal(a, pa) - x0;
            yield return Ask(a, "npc-talk", q.giverNpc);
            yield return Ask(a, "quest-accept", quest);
            Check(_last.ok && pa.ServerIsActive(quest), $"„{q.Title}“ bei {GameData.Npc(q.giverNpc)?.Name} angenommen (Stufe {a.ServerModel.Level})");
        }

        /// <summary>Abgabe beim Abgabe-NPC, Belohnung pruefen (EP genau, Geld genau, Wahl nach Klasse).</summary>
        private IEnumerator GrTurnIn(NetworkInventory a, NetworkProgression pa, string quest, string pick = null, bool expectOk = true)
        {
            QuestDefinition q = GameData.Quest(quest);
            long d0 = GrTotal(a, pa);
            yield return GotoNpc(a, q.TurnInNpc);
            yield return new WaitForSecondsRealtime(1.2f); // Entdeckungen am Abgabeort vorher abschliessen
            _grDiscoveryXp += GrTotal(a, pa) - d0;
            yield return Ask(a, "npc-talk", q.TurnInNpc);
            int choice = -1;
            List<QuestItemReward> choices = QuestRules.ForClass(q.choiceItems, a.ServerModel.Class);
            if (choices.Count > 0)
            {
                choice = pick != null ? choices.FindIndex(r => r.item.Id == pick) : 0;
                if (choice < 0) { Info($"{pick} ist für diese Klasse nicht wählbar – nehme das erste Teil"); choice = 0; }
            }
            long xp0 = GrTotal(a, pa), m0 = pa.ServerMoney;
            yield return Ask(a, "quest-turnin", choice >= 0 ? quest + " " + choice : quest);
            if (!expectOk) yield break;
            long dXp = GrTotal(a, pa) - xp0;
            _grQuestXp += dXp;
            Check(_last.ok && pa.ServerIsCompleted(quest) && !pa.ServerIsActive(quest), $"„{q.Title}“ bei {GameData.Npc(q.TurnInNpc)?.Name} abgegeben");
            Check(dXp == q.xp && pa.ServerMoney - m0 == q.money, $"Belohnung: {dXp} EP (Soll {q.xp}), {pa.ServerMoney - m0} Kupfer (Soll {q.money})" +
                                                               (choice >= 0 ? $", gewählt: {choices[choice].item.DisplayName}" : ""));
        }

        private IEnumerator GrEnsureLevel(NetworkInventory a, NetworkProgression pa, int level)
        {
            if (a.ServerModel.Level >= level) yield break;
            long need = Progression.TotalXpTo(level) - GrTotal(a, pa);
            Info($"Nachhilfe: {need} EP bis Stufe {level} (Stufe {a.ServerModel.Level}, {pa.ServerXp} EP)");
            _grTopUp += need;
            pa.AddXp((int)need);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        private IEnumerator GrGotoSite(NetworkInventory a, NetworkProgression pa, string siteId)
        {
            Ashveil.World.WorldQuestSite s = Ashveil.World.WorldQuestSite.Find(siteId);
            if (s == null) { Check(false, "Quest-Ort in der Szene: " + siteId); yield break; }
            Vector3 p = s.transform.position;
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 8f, NavMesh.AllAreas)) p = hit.position;
            long x0 = GrTotal(a, pa);
            yield return Ask(a, "goto", V(p + Vector3.up * 0.3f, 0f));
            yield return new WaitForSecondsRealtime(1.5f);
            _grDiscoveryXp += GrTotal(a, pa) - x0;
        }

        private IEnumerator GrExplore(NetworkInventory a, NetworkProgression pa, string quest, int objective, string areaId)
        {
            QuestArea area = QuestArea.Find(areaId);
            if (area == null) { Check(false, "Erkundungsziel in der Szene: " + areaId); yield break; }
            long x0 = GrTotal(a, pa);
            yield return Ask(a, "goto", V(area.transform.position + Vector3.up * 0.3f, 0f));
            yield return new WaitForSecondsRealtime(1.6f);
            _grDiscoveryXp += GrTotal(a, pa) - x0;
            Check(pa.ServerProgress(quest, objective) >= 1, $"Ort erkundet: {areaId}");
        }

        /// <summary>Fundstelle / Geschichtsschritt ueber den echten Client-Weg (Rechtsklick-Bitte an den Server).</summary>
        private IEnumerator GrTake(NetworkInventory a, NetworkProgression pa, string action, bool expectItem)
        {
            StoryAnchor an = StoryAnchor.Find(action);
            if (an == null) { Check(false, "Fundstelle in der Szene: " + action); yield break; }
            C.TryNode(action, out C.Node n);
            int before = !string.IsNullOrEmpty(n.Item) ? a.ServerModel.Count(n.Item) : 0;
            Vector3 p = an.Position + new Vector3(1.1f, 0.3f, 0f);
            long x0 = GrTotal(a, pa);
            yield return Ask(a, "goto", V(p, 0f));
            yield return Ask(a, "story-act", action);
            _grDiscoveryXp += GrTotal(a, pa) - x0; // einzige EP-Quelle hier: Entdeckung des Ortes
            if (!expectItem) yield break;
            Check(pa.ServerStoryNote == action + ": ok" && a.ServerModel.Count(n.Item) == before + 1,
                $"{GameData.Item(n.Item)?.DisplayName} aufgehoben ({action}: {pa.ServerStoryNote})");
        }

        /// <summary>
        /// Test-Gegner neben der Figur: ein kleiner Treffer (beteiligt), dann ein toedlicher – Beute und Erfahrung wie im Spiel.
        /// takeItem: diesen Gegenstand aus der Leiche nehmen (nur ihn, damit der Rucksack nicht volllaeuft).
        /// </summary>
        private IEnumerator GrKill(NetworkInventory a, NetworkProgression pa, string enemyId, int level, int count, System.Action<CorpseLoot> inspect,
            string takeItem = null)
        {
            EnemySpawnArea template = FindObjectsByType<EnemySpawnArea>(FindObjectsSortMode.None)
                .FirstOrDefault(x => x.Definition != null && x.Definition.Id == enemyId && x.Prefab != null);
            if (template == null) { Check(false, "Gegner-Vorlage gefunden: " + enemyId); yield break; }
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = a.transform.position + a.transform.forward * 3.5f;
                if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 6f, NavMesh.AllAreas)) pos = hit.position;
                GameObject go = Instantiate(template.Prefab, pos, Quaternion.identity);
                go.GetComponent<EnemyBrain>().Configure(level, pos, "glockenrast-test");
                InstanceFinder.ServerManager.Spawn(go);
                yield return new WaitForSecondsRealtime(0.5f);
                var hp = go.GetComponent<Health>();
                long x0 = GrTotal(a, pa);
                if (hp != null)
                {
                    hp.ApplyDamage(new DamageInfo { Amount = 1f, Source = a.gameObject });
                    yield return new WaitForSecondsRealtime(0.1f);
                    hp.ApplyDamage(new DamageInfo { Amount = hp.Max * 20f, Source = a.gameObject, Unblockable = true });
                }
                yield return new WaitForSecondsRealtime(0.5f);
                _grKillXp += GrTotal(a, pa) - x0;
                if (hp == null || hp.IsAlive) { Check(false, $"Test-Gegner {enemyId} besiegt"); continue; }
                var corpse = go.GetComponent<CorpseLoot>();
                inspect?.Invoke(corpse);
                if (takeItem != null && corpse != null)
                {
                    int idx = corpse.Get(a.ProfileId).FindIndex(s => s.id == takeItem);
                    if (idx >= 0)
                    {
                        string nid = go.GetComponent<NetworkObject>().ObjectId.ToString();
                        yield return Ask(a, "goto", V(go.transform.position + new Vector3(1.2f, 0.3f, -1f), 0f));
                        yield return Ask(a, "loot-open", nid);
                        if (_last.ok) yield return Ask(a, "loot-take", idx.ToString());
                    }
                }
            }
        }
    }
}
