using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Audio;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Story;
using Ashveil.World;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// P3 Glockenrast (Stufe 5–9) – serverseitige Teile, die ueber die vorhandenen Zieltypen hinausgehen:
    ///   · Rechtsklick-Handlungen (<see cref="GlockenrastCatalog.Nodes"/>): Fundstuecke aufheben (Ersatzrad, Brief, Kassenbuch,
    ///     Abriebe, Glockenscherben) und zwei Geschichtsschritte (Scherbe am Glockenturm anschlagen, Jasko im Nordmoor finden).
    ///     Der Server prueft Auftrag, Entfernung zum Anker und ob das Fundstueck fuer diesen Spieler schon genommen ist.
    ///   · Auftragsbeute: Gegenstaende der Kategorie Quest mit questId fallen nur, solange der Auftrag laeuft und noch welche fehlen.
    ///   · Entdeckungs-Erfahrung (wie Classic): erstes Betreten eines benannten Quest-Orts (WorldQuestSite) gibt 20 + 5 × Ortsstufe
    ///     Erfahrung, je Charakter einmal (Speicherstand v11: discoveredAreas). Nicht fuer geplante/versteckte Orte; das Startgebiet
    ///     (Hub Wachtfeuer und seine Orte) wird ohne EP als entdeckt vermerkt.
    /// </summary>
    public sealed partial class NetworkProgression
    {
        /// <summary>Das Startlager und seine Orte gelten als bekannt (keine Entdeckungs-EP im Startgebiet).</summary>
        private const string StartSiteId = "site.kareth.hub.wachtfeuer";
        private const float DiscoveryInterval = 1f, DiscoveryLoginDelay = 2f;

        private float _discoveryTimer;
        private bool _discoveryPrimed;

        /// <summary>Server: letzte Entdeckung (Tests).</summary>
        public string ServerLastDiscovery { get; private set; } = "";

        // ================================================================== Auftragsbeute

        /// <summary>
        /// Server (Beute je Empfaenger): Auftragsgegenstaende (Kategorie Quest, questId eines vorhandenen Auftrags) fallen nur,
        /// solange dieser Auftrag laeuft und das Sammelziel noch nicht voll ist. Alles andere faellt wie bisher.
        /// </summary>
        public static bool ServerWantsLoot(string profile, ItemDefinition item)
        {
            if (item == null) return true;
            // 02.10. (CORE-008): Einzigartiges, das der Empfaenger schon besitzt, faellt fuer ihn nicht – sonst liegt
            // Beute in der Leiche, die er nie aufheben kann ("Verblichener Brief" im Gruppentest).
            if (item.unique && TryGet(profile, out NetworkProgression owner) && owner.Loaded && owner.Model.Count(item.Id) > 0) return false;
            if (item.category != ItemCategory.Quest || string.IsNullOrEmpty(item.questId)) return true;
            QuestDefinition q = GameData.Quest(item.questId);
            if (q == null) return true; // alte Fundstuecke ohne Auftrag (Verblichener Brief, Glockensplitter) bleiben Beute
            if (!TryGet(profile, out NetworkProgression p) || !p.Loaded) return false;
            if (p.FindQuest(q.Id) == null) return false;
            int need = 0;
            foreach (QuestObjective o in q.objectives)
                if (o.type == QuestObjectiveType.Collect && o.targetId == item.Id) need = Mathf.Max(need, o.count);
            return need <= 0 || p.Model.Count(item.Id) < need;
        }

        // ================================================================== Rechtsklick-Handlungen

        /// <summary>Handlung "gr.*" (aus <see cref="ServerStoryAction"/>). Rueckgabe: leer = ok, sonst Grund.</summary>
        private string GlockenrastAction(string action)
        {
            if (!GlockenrastCatalog.TryNode(action, out GlockenrastCatalog.Node n)) return "unbekannt";
            // 21.10. (Nr. 8 Gefallene): tot keine Fundstuecke und Geschichtsschritte (gr./ww./rg.) – wie Bergen und Pferde-Story
            if (_health != null && !_health.IsAlive) { Reject("Du bist tot."); return "tot"; }
            if (FindQuest(n.Quest) == null) return "Auftrag läuft nicht";
            if (StoryAnchor.Find(n.Action) == null) return "Ort fehlt in der Szene";
            if (DistTo(n.Action) > n.Range + rangeSlack) { Reject("Zu weit entfernt."); return "zu weit"; }

            if (!string.IsNullOrEmpty(n.Item))
            {
                string flag = GlockenrastCatalog.TakenFlag(n.Action);
                if (D.HasFlag(flag)) return "schon genommen";
                ItemDefinition def = GameData.Item(n.Item);
                if (def == null) return "Gegenstand fehlt";
                // 22.10. (STORY-04): einzigartiges Fundstueck schon im Rucksack (nach Aufgeben behalten, Max-Regel REP-062) – kein Platzproblem.
                // Vorher rot „Kein Platz im Inventar.“ bei jedem Klick, der Hinweis blieb. Jetzt: als genommen vermerken (Fundstueck
                // verschwindet fuer diesen Spieler), klare Meldung wie beim Pluendern (CORE-007). Repariert auch schon betroffene Staende.
                if (def.unique && Model.Count(def.Id) > 0)
                {
                    StoryMilestones.Grant(D, flag);
                    _inv.ServerMarkDirty();
                    SendStory();
                    if (Owner != null && Owner.IsActive) TargetMessage(Owner, "Davon kannst du nur einen besitzen.");
                    return "schon genommen";
                }
                if (Model.SpaceFor(Stack(def, 1)) < 1) { Reject("Kein Platz im Inventar."); return "kein Platz"; }
                Model.Add(Stack(def, 1));
                StoryMilestones.Grant(D, flag);
                _inv.ServerCommit(); // Sammelziele zaehlen neu, Stand geht raus (OnServerInventoryChanged)
                SendStory();         // Marke „genommen“ an den eigenen Client (Fundstueck verschwindet nur fuer ihn)
                if (Owner != null && Owner.IsActive) TargetMessage(Owner, def.DisplayName + " aufgehoben.");
                return "";
            }

            if (string.IsNullOrEmpty(n.Story)) return "leer";
            // Reihenfolge (Akt I: „Erst das Brett, dann das Buch.“)
            if (!string.IsNullOrEmpty(n.After) && !ServerStoryStepDone(n.Quest, n.After))
            {
                if (Owner != null && Owner.IsActive) TargetMessage(Owner, Loc.T(n.AfterKey));
                return "erst " + n.After;
            }
            if (!ServerSetStoryObjective(n.Quest, n.Story, 1)) return "schon erledigt";
            if (!string.IsNullOrEmpty(n.MessageKey) && Owner != null && Owner.IsActive) TargetMessage(Owner, Loc.T(n.MessageKey));
            if (Owner != null && Owner.IsActive)
            {
                if (n.Action == GlockenrastCatalog.NodeStrike)
                {
                    // Mythos, bewusst offen: der Boden antwortet – ein tiefer, langsamer Schlag. Keine Erklaerung.
                    TargetGlockenrastCue(Owner, 1, StoryAnchor.Find(n.Action).Position);
                    TargetMessage(Owner, Loc.T("story.glockenrast.strike.answer"));
                    TargetMessage(Owner, Loc.T("story.glockenrast.strike.runa"));
                }
                else if (n.Action == GlockenrastCatalog.NodeJasko)
                    TargetMessage(Owner, Loc.T("story.glockenrast.jasko.found"));
            }
            _inv.SaveNow();
            return "";
        }

        /// <summary>Server: ist der Geschichtsschritt <paramref name="story"/> des laufenden Auftrags erledigt?</summary>
        public bool ServerStoryStepDone(string questId, string story)
        {
            QuestSave q = Loaded ? FindQuest(questId) : null;
            QuestDefinition def = q != null ? GameData.Quest(questId) : null;
            if (def == null) return false;
            for (int i = 0; i < def.objectives.Count && i < q.progress.Length; i++)
                if (def.objectives[i].type == QuestObjectiveType.Story && def.objectives[i].targetId == story && q.progress[i] >= def.objectives[i].count) return true;
            return false;
        }

        /// <summary>Beim (Neu-)Annehmen: „genommen“-Marken der Fundstuecke dieses Auftrags loeschen (Abbruch und Neubeginn).</summary>
        private void GlockenrastQuestAccepted(QuestDefinition q)
        {
            if (q == null || D.storyFlags == null) return;
            bool changed = false;
            // 22.10. (STORY-04): einzigartige Fundstuecke, die noch im Rucksack sind (beim Aufgeben behalten), bleiben „genommen“ –
            // sonst lag das Stueck wieder sichtbar am Fundort und jeder Klick meldete „Kein Platz im Inventar.“
            foreach (GlockenrastCatalog.Node n in GlockenrastCatalog.Nodes)
                if (n.Quest == q.Id && !string.IsNullOrEmpty(n.Item) && !OwnsUniqueItem(n.Item) && D.storyFlags.Remove(GlockenrastCatalog.TakenFlag(n.Action))) changed = true;
            foreach (GlockenrastCatalog.Node n in WestweidenCatalog.Nodes)   // Westweiden-Fundstellen (12.10.2026)
                if (n.Quest == q.Id && !string.IsNullOrEmpty(n.Item) && !OwnsUniqueItem(n.Item) && D.storyFlags.Remove(GlockenrastCatalog.TakenFlag(n.Action))) changed = true;
            if (changed) SendStory();
        }

        /// <summary>22.10. (STORY-04): einzigartiger Gegenstand, den der Spieler schon besitzt (kann kein zweites Mal aufheben).</summary>
        private bool OwnsUniqueItem(string itemId)
        {
            ItemDefinition def = GameData.Item(itemId);
            return def != null && def.unique && Model.Count(def.Id) > 0;
        }

        [TargetRpc]
        private void TargetGlockenrastCue(NetworkConnection conn, int cue, Vector3 where)
        {
            if (cue != 1) return;
            // Tiefer Glockenschlag aus dem Boden, gedaempft, dann ein Nachstampfen
            SfxPlayer.PlayFar(SfxId.Bell, where + Vector3.down * 6f, 0.9f, 120f, 0f);
            SfxPlayer.PlayFar(SfxId.Stomp, where + Vector3.down * 4f, 0.5f, 60f, 0f);
        }

        // ================================================================== Entdeckungen

        private void DiscoveryLogin()
        {
            if (D.discoveredAreas == null) D.discoveredAreas = new List<string>();
            if (!D.discoveredAreas.Contains(StartSiteId)) D.discoveredAreas.Add(StartSiteId);
            _discoveryPrimed = false;
            _discoveryTimer = DiscoveryLoginDelay; // Position nach dem Anmelden erst setzen lassen
        }

        /// <summary>Server: Ort schon entdeckt?</summary>
        public bool ServerDiscovered(string siteId) => Loaded && D.discoveredAreas != null && D.discoveredAreas.Contains(siteId);
        public int ServerDiscoveredCount => Loaded && D.discoveredAreas != null ? D.discoveredAreas.Count : 0;

        private static bool Discoverable(WorldQuestSite s) =>
            s != null && !string.IsNullOrEmpty(s.Id) && s.Status != QuestSiteStatus.Planned && s.Kind != QuestSiteKind.Hidden && s.Reachable;

        private static bool Inside(WorldQuestSite s, Vector3 p)
        {
            Vector3 d = p - s.transform.position;
            if (Mathf.Abs(d.y) > 30f) return false;
            d.y = 0f;
            return d.sqrMagnitude <= s.Radius * s.Radius;
        }

        private void DiscoveryUpdate()
        {
            _discoveryTimer -= Time.deltaTime;
            if (_discoveryTimer > 0f) return;
            _discoveryTimer = DiscoveryInterval;
            if (D.discoveredAreas == null) D.discoveredAreas = new List<string>();
            Vector3 p = transform.position;
            bool changed = false;
            foreach (WorldQuestSite s in WorldQuestSite.All)
            {
                if (!Discoverable(s) || D.discoveredAreas.Contains(s.Id) || !Inside(s, p)) continue;
                D.discoveredAreas.Add(s.Id);
                changed = true;
                // Erster Blick nach dem Anmelden: wo man steht, kennt man schon (kein EP fuers Einloggen).
                // Startgebiet (Wachtfeuer und seine Orte, Stufe 1–4): bekannt, ohne EP – die Tutorialkette bleibt genau bemessen.
                if (!_discoveryPrimed || s.Id == StartSiteId || s.HubId == StartSiteId) continue;
                int xp = Progression.DiscoveryXp(s.MinLevel);
                ServerLastDiscovery = s.Id + "+" + xp;
                AddXp(xp);
                if (Owner != null && Owner.IsActive)
                {
                    TargetMessage(Owner, $"Entdeckt: {s.DisplayName}");
                    TargetXp(Owner, xp, p + Vector3.up * 2f);
                }
                Debug.Log($"[Ashveil] '{_profile}' entdeckt {s.DisplayName} (+{xp} EP).");
            }
            _discoveryPrimed = true;
            if (!changed) return;
            _inv.ServerMarkDirty();
            SendState(false);
        }
    }
}
