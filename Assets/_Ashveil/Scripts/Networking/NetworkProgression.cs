using System.Collections.Generic;
using System.Text;
using Ashveil.Core.Events;
using Ashveil.Data;
using Ashveil.Gameplay.Audio;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Quests;
using Ashveil.Gameplay.Visuals;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Erfahrung, Stufen, Geld, Auftraege und Handel eines Spielers – serverautoritaer wie das Inventar:
    ///
    ///   Client : bittet ("nimm Auftrag X an", "kaufe Eintrag 2") und zeigt den Stand, den der Server schickt.
    ///   Server : prueft Entfernung zum NPC, Voraussetzungen, Geld und Platz; aendert Inventar und Stand
    ///            nur als Ganzes (alles oder nichts) und speichert ueber <see cref="NetworkInventory"/>.
    ///
    /// Erfahrung: wie Classic (Max 10.10.2026) – die Beteiligten teilen die Sieg-EP mit Gruppenbonus (<see cref="Progression.GroupShare"/>),
    /// graue Gegner geben nichts, Ruhe verdoppelt Sieg-EP (NetworkProgression.Rest.cs).
    /// Sammelziele zaehlen den Rucksack, Toetungsziele zaehlen fuer jeden Beteiligten, Erkundung prueft der
    /// Server an der Position, Gespraechsziele erledigt das Gespraech.
    /// </summary>
    [RequireComponent(typeof(NetworkInventory))]
    [DisallowMultipleComponent]
    public sealed partial class NetworkProgression : NetworkBehaviour, IProgressionService
    {
        [Tooltip("Zusaetzlicher Spielraum der Server-Entfernungspruefung (m)")]
        [SerializeField] private float rangeSlack = 1.5f;
        [SerializeField] private int buybackSlots = 6;

        private static readonly Dictionary<string, NetworkProgression> ByProfile = new Dictionary<string, NetworkProgression>();

        private NetworkInventory _inv;
        private Health _health;

        // ---- Server
        private string _profile;
        private string _dialogNpc;
        private readonly List<BuybackEntry> _buyback = new List<BuybackEntry>();
        private float _exploreTimer;
        private string _lastSent;

        // ---- Client (Besitzer)
        private bool _cReady;
        private int _cXp, _cLevel = 1;
        private long _cMoney;
        private readonly List<QuestView> _cQuests = new List<QuestView>();
        private readonly HashSet<string> _cCompleted = new HashSet<string>();
        private NpcDefinition _cDialog;
        private readonly List<BuybackEntry> _cBuyback = new List<BuybackEntry>();
        private int _cRev;

        /// <summary>Client: Stand-Nummer (steigt mit jeder Antwort des Servers).</summary>
        public int ClientRevision => _cRev;
        /// <summary>Client: letzte Fehlermeldung einer Aktion (leer = in Ordnung).</summary>
        public string LastError { get; private set; } = "";

        private void Awake()
        {
            _inv = GetComponent<NetworkInventory>();
            _health = GetComponent<Health>();
            MountAwake();
        }

        // ================================================================== Start / Ende

        public override void OnStartServer()
        {
            base.OnStartServer();
            _inv.ServerLoggedIn += OnServerLogin;
            _inv.ServerInventoryChanged += OnServerInventoryChanged;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _inv.ServerLoggedIn -= OnServerLogin;
            _inv.ServerInventoryChanged -= OnServerInventoryChanged;
            MasteryStopServer();
            MountStopServer();
            SalvageStopServer();
            CompanionStopServer();
            HorseStoryStopServer();
            SpellsStopServer();
            if (_profile != null && ByProfile.TryGetValue(_profile, out NetworkProgression cur) && cur == this) ByProfile.Remove(_profile);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner) ProgressionClient.Register(this);
            MountStartClient();
            SalvageStartClient();
            CompanionStartClient();
            HorseStoryStartClient();
            SpellsStartClient();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (IsOwner) ProgressionClient.Unregister(this);
            KartenStopClient();
            MountStopClient();
            SalvageStopClient();
            CompanionStopClient();
            HorseStoryStopClient();
            SpellsStopClient();
        }

        private void OnDestroy()
        {
            ProgressionClient.Unregister(this);
        }

        // ================================================================== Server: Zugriff

        private CharacterSaveData D => _inv.ServerData;
        private InventoryModel Model => _inv.ServerModel;
        private bool Loaded => _inv != null && _inv.IsReady && D != null && Model != null;
        public int ServerLevel => Loaded ? Model.Level : 1;
        public int ServerXp => Loaded ? D.xp : 0;
        public long ServerMoney => Loaded ? D.money : 0;
        public bool ServerIsActive(string questId) => Loaded && FindQuest(questId) != null;
        public bool ServerIsCompleted(string questId) => Loaded && D.completedQuests.Contains(questId);

        /// <summary>Server: Fortschritt eines Ziels (-1 = Auftrag laeuft nicht).</summary>
        public int ServerProgress(string questId, int objective)
        {
            QuestSave q = Loaded ? FindQuest(questId) : null;
            return q != null && objective < q.progress.Length ? q.progress[objective] : -1;
        }
        public static bool TryGet(string profile, out NetworkProgression p) => ByProfile.TryGetValue(profile ?? "", out p) && p != null;

        private void OnServerLogin()
        {
            _profile = _inv.ProfileId;
            ByProfile[_profile] = this;
            _buyback.Clear();
            _dialogNpc = null;
            _lastSent = null;
            RecountCollect();
            SendState(true);
            MasteryLogin();
            MountLogin();
            SalvageLogin();
            CompanionLogin();
            HorseStoryLogin();
            SpellsLogin();
            DiscoveryLogin();
            RestLogin();       // Ausgeruht: Ruhe seit dem letzten Zeitstempel (Max 10.10.: wie Classic)
            KartenLogin(); // PROG-013: entdeckte Unterzonen an den Client, Startunterzone bekannt
        }

        private void OnServerInventoryChanged()
        {
            if (!Loaded) return;
            RecountCollect();
            SendState(false);
        }

        private QuestSave FindQuest(string id)
        {
            foreach (QuestSave q in D.quests) if (q.id == id) return q;
            return null;
        }

        // ================================================================== Server: Erfahrung

        /// <summary>Server: ein Gegner ist besiegt – jeder Beteiligte bekommt Erfahrung und Auftragsfortschritt.</summary>
        public static void ServerEnemyKilled(IEnumerable<string> profiles, EnemyDefinition enemy, int enemyLevel, Vector3 where)
        {
            if (enemy == null) return;
            // Gruppen-EP wie Classic: die Beteiligten (online) teilen sich die EP mit Bonus (Max 10.10.2026)
            KillMembers.Clear();
            foreach (string p in profiles)
                if (TryGet(p, out NetworkProgression np) && np.Loaded && !KillMembers.Contains(np)) KillMembers.Add(np);
            float share = Progression.GroupShare(KillMembers.Count);
            float factor = Balance.XpFactor(enemy.balanceRole, enemy.elite);
            foreach (NetworkProgression np in KillMembers) np.ServerOnKill(enemy.Id, enemyLevel, where, factor * share);
            KillMembers.Clear();
        }

        private static readonly List<NetworkProgression> KillMembers = new List<NetworkProgression>(8);

        private void ServerOnKill(string enemyId, int enemyLevel, Vector3 where, float xpFactor = 1f)
        {
            if (!Loaded) return;
            bool changed = false;
            foreach (QuestSave q in D.quests)
            {
                QuestDefinition def = GameData.Quest(q.id);
                if (def == null) continue;
                for (int i = 0; i < def.objectives.Count; i++)
                {
                    QuestObjective o = def.objectives[i];
                    if (o.type != QuestObjectiveType.Kill || o.targetId != enemyId || q.progress[i] >= o.count) continue;
                    q.progress[i]++;
                    changed = true;
                }
            }
            int xp = Mathf.RoundToInt(Progression.KillXp(enemyLevel, Model.Level) * xpFactor);
            if (xp > 0)
            {
                xp += RestConsume(xp); // Ausgeruht: verdoppelt, solange Ruhe da ist
                AddXp(xp);
                if (Owner != null && Owner.IsActive) TargetXp(Owner, xp, where);
            }
            if (changed || xp > 0)
            {
                _inv.ServerCommit();
                SendState(false);
            }
        }

        /// <summary>Server: Erfahrung gutschreiben, bei Bedarf aufsteigen (auch mehrere Stufen).</summary>
        public void AddXp(int amount)
        {
            if (!Loaded || amount <= 0) return;
            int level = Model.Level;
            if (level >= Progression.GlobalMaxLevel) { D.xp = 0; return; }
            D.xp += amount;
            bool up = false;
            while (level < Progression.GlobalMaxLevel && D.xp >= Progression.XpToNext(level))
            {
                D.xp -= Progression.XpToNext(level);
                level++;
                up = true;
            }
            if (level >= Progression.GlobalMaxLevel) D.xp = 0;
            if (!up) return;
            _inv.ServerSetLevel(level); // Werte neu, speichern, Stand senden
            if (_health != null && _health.IsAlive) _health.Heal(_health.Max, null);
            SpellsLevelUp(level);
            ObserversLevelUp(level);
            Debug.Log($"[Ashveil] '{_profile}' erreicht Stufe {level}.");
        }

        // ================================================================== Server: Erkundung

        private void Update()
        {
            if (!IsServerInitialized || !Loaded) return;
            MasteryUpdate();
            MountUpdate();
            SalvageUpdate();
            CompanionUpdate();
            HorseStoryUpdate();
            SpellsUpdate();
            DiscoveryUpdate(); // P3: Entdeckungs-EP (erstes Betreten eines Quest-Orts)
            KartenUpdate();    // PROG-013: Entdeckungs-EP je Unterzone (zusätzlich, Max 08.10.)
            KarethUpdate();    // WORLD-238: Einladung beim ersten Betreten des Wachtfeuer-Lagers (Max 10.10.)
            RestUpdate();      // Ausgeruht: Ruhe im Gasthaus, Zeitstempel fuer die Zeit nach dem Abmelden
            _exploreTimer -= Time.deltaTime;
            if (_exploreTimer > 0f) return;
            _exploreTimer = 0.5f;
            bool changed = false;
            foreach (QuestSave q in D.quests)
            {
                QuestDefinition def = GameData.Quest(q.id);
                if (def == null) continue;
                for (int i = 0; i < def.objectives.Count; i++)
                {
                    QuestObjective o = def.objectives[i];
                    if (o.type != QuestObjectiveType.Explore || q.progress[i] >= o.count) continue;
                    QuestArea area = QuestArea.Find(o.targetId);
                    if (area == null || !area.Contains(transform.position)) continue;
                    q.progress[i] = o.count;
                    changed = true;
                    if (Owner != null && Owner.IsActive) TargetMessage(Owner, $"{o.Label} erkundet.");
                }
            }
            if (changed)
            {
                _inv.ServerCommit();
                SendState(false);
            }
        }

        // ================================================================== Server: Gespraech

        [ServerRpc]
        private void ServerTalk(string npcId)
        {
            if (!Loaded) return;
            if (!InRange(npcId, out NpcDefinition npc, out string err))
            {
                Reject(err);
                return;
            }
            _dialogNpc = npcId;
            bool changed = false;
            foreach (QuestSave q in D.quests)
            {
                QuestDefinition def = GameData.Quest(q.id);
                if (def == null) continue;
                for (int i = 0; i < def.objectives.Count; i++)
                {
                    QuestObjective o = def.objectives[i];
                    if (o.type != QuestObjectiveType.TalkTo || o.targetId != npcId || q.progress[i] >= o.count) continue;
                    q.progress[i] = o.count;
                    changed = true;
                    // 21.10.: Gespraechsziel bei einem Dritten (U2 Oswald, G10 Anselm) lief stumm durch – Meldung wie bei "erkundet";
                    // beim Abgabe-NPC nicht, dort zeigt der Dialog die Abgabe ohnehin (keine Doppelmeldung)
                    if (o.targetId != def.TurnInNpc && Owner != null && Owner.IsActive) TargetMessage(Owner, $"{o.Label}.");
                }
            }
            if (changed) _inv.ServerCommit();
            SendState(true);
            TargetDialog(Owner, npcId);
            TargetBuyback(Owner, BuybackStacks(), BuybackPrices());
        }

        [ServerRpc]
        private void ServerCloseDialog() => _dialogNpc = null;

        private bool InRange(string npcId, out NpcDefinition npc, out string error)
        {
            npc = GameData.Npc(npcId);
            NpcIdentity id = NpcIdentity.Find(npcId);
            error = null;
            // 21.10. (Nr. 8 Gefallene): Tote sprechen, handeln, nehmen an/geben ab und lernen nicht – eine Stelle fuer
            // ServerTalk, InDialog (Annehmen, Abgeben, Kaufen, Verkaufen, Rueckkauf) und InDialogQuiet (Ausbilder)
            if (_health != null && !_health.IsAlive) { error = "Du bist tot."; return false; }
            if (npc == null || id == null) { error = "Niemand da."; return false; }
            if (Vector3.Distance(id.transform.position, transform.position) > npc.interactRange + rangeSlack)
            {
                error = "Zu weit entfernt.";
                return false;
            }
            return true;
        }

        /// <summary>Aktionen nur im laufenden Gespraech mit diesem NPC und in Reichweite.</summary>
        private bool InDialog(string npcId, out NpcDefinition npc)
        {
            npc = null;
            if (string.IsNullOrEmpty(npcId) || _dialogNpc != npcId) { Reject("Kein Gespräch."); return false; }
            if (!InRange(npcId, out npc, out string err)) { Reject(err); return false; }
            return true;
        }

        // ================================================================== Server: Auftraege

        [ServerRpc]
        private void ServerAccept(string questId)
        {
            if (!Loaded) return;
            QuestDefinition q = GameData.Quest(questId);
            if (q == null) { Reject("Unbekannter Auftrag."); return; }
            if (!InDialog(q.giverNpc, out _)) return;
            string why = QuestRules.CanAccept(q, Model.Level, id => D.completedQuests.Contains(id), id => FindQuest(id) != null, D.quests.Count, ServerGate());
            if (why == null && q.offeredByStory && ServerStoryOffer() != q.Id) why = "Noch nicht verfügbar.";
            if (why != null) { Reject(why); return; }

            // Startgegenstaende muessen komplett passen
            var sim = Simulate();
            foreach (QuestItemReward r in q.startItems)
                if (r.item != null && sim.Add(Stack(r.item, r.count)) < r.count) { Reject("Kein Platz im Inventar."); return; }
            foreach (QuestItemReward r in q.startItems)
                if (r.item != null) Model.Add(Stack(r.item, r.count));

            var save = new QuestSave { id = q.Id, progress = new int[q.objectives.Count] };
            D.quests.Add(save);
            StoryQuestAccepted(q);
            GlockenrastQuestAccepted(q); // P3: Fundstuecke dieses Auftrags wieder aufhebbar
            // Gespraechsziel beim Geber selbst ist sofort erledigt
            for (int i = 0; i < q.objectives.Count; i++)
                if (q.objectives[i].type == QuestObjectiveType.TalkTo && q.objectives[i].targetId == _dialogNpc) save.progress[i] = q.objectives[i].count;
            RecountCollect();
            _inv.ServerCommit();
            SendState(true);
            TargetQuest(Owner, q.Id, false);
            Debug.Log($"[Ashveil] '{_profile}' nimmt Auftrag '{q.DisplayName}' an.");
        }

        [ServerRpc]
        private void ServerTurnIn(string questId, int choice)
        {
            if (!Loaded) return;
            QuestDefinition q = GameData.Quest(questId);
            QuestSave save = q != null ? FindQuest(q.Id) : null;
            if (save == null) { Reject("Auftrag läuft nicht."); return; }
            if (!InDialog(q.TurnInNpc, out _)) return;
            RecountCollect();
            for (int i = 0; i < q.objectives.Count; i++)
            {
                QuestObjective o = q.objectives[i];
                if (o.type == QuestObjectiveType.TalkTo && o.targetId == q.TurnInNpc) save.progress[i] = o.count;
                if (save.progress[i] < o.count) { Reject("Noch nicht erledigt."); return; }
            }

            List<QuestItemReward> fixedRewards = QuestRules.ForClass(q.rewardItems, Model.Class);
            List<QuestItemReward> choices = QuestRules.ForClass(q.choiceItems, Model.Class);
            QuestItemReward chosen = null;
            if (choices.Count > 0)
            {
                if (choice < 0 || choice >= choices.Count) { Reject("Bitte eine Belohnung wählen."); return; }
                chosen = choices[choice];
            }

            // Erst durchspielen, dann wirklich tun – alles oder nichts
            InventoryModel sim = Simulate();
            if (!ApplyTurnIn(sim, q, fixedRewards, chosen)) { Reject("Kein Platz im Inventar."); return; }
            ApplyTurnIn(Model, q, fixedRewards, chosen);

            D.quests.Remove(save);
            if (!D.completedQuests.Contains(q.Id)) D.completedQuests.Add(q.Id);
            if (q.grantsFlags != null)
                foreach (string f in q.grantsFlags) StoryMilestones.Grant(D, f);
            D.money += q.money;
            StoryQuestCompleted(q);
            _inv.ServerCommit();
            AddXp(q.xp);
            _inv.SaveNow(); // Abschluss sofort sichern
            SendStory();
            SendState(true);
            TargetQuest(Owner, q.Id, true);
            if (q.xp > 0) TargetXp(Owner, q.xp, transform.position);
            Debug.Log($"[Ashveil] '{_profile}' schliesst Auftrag '{q.DisplayName}' ab (+{q.xp} EP).");
        }

        private static bool ApplyTurnIn(InventoryModel m, QuestDefinition q, List<QuestItemReward> rewards, QuestItemReward chosen)
        {
            foreach (QuestObjective o in q.objectives)
            {
                if (o.type != QuestObjectiveType.Collect) continue;
                // 21.10. (Max: „Nach der Abgabe automatisch alle Reste dieses Questgegenstands entfernen; beim Aufgeben behalten“):
                // eigene Questgegenstaende dieses Auftrags ganz entfernen – Ueberschuss (Beute, Fundstellen nach erneutem Annehmen) war
                // sonst unverkaeuflich und nicht wegzuwerfen. Material und fremde Questgegenstaende: nur die verlangte Menge.
                ItemDefinition def = GameData.Item(o.targetId);
                bool eigen = def != null && def.category == ItemCategory.Quest && def.questId == q.Id;
                RemoveItems(m, o.targetId, eigen ? int.MaxValue : o.count);
            }
            foreach (QuestItemReward r in q.startItems)
                if (r.item != null) RemoveItems(m, r.item.Id, int.MaxValue);
            foreach (QuestItemReward r in rewards)
                if (m.Add(Stack(r.item, r.count)) < r.count) return false;
            if (chosen != null && m.Add(Stack(chosen.item, chosen.count)) < chosen.count) return false;
            return true;
        }

        [ServerRpc]
        private void ServerAbandon(string questId)
        {
            if (!Loaded) return;
            QuestSave save = FindQuest(questId);
            if (save == null) { Reject("Auftrag läuft nicht."); return; }
            QuestDefinition q = GameData.Quest(questId);
            D.quests.Remove(save);
            if (q != null)
                foreach (QuestItemReward r in q.startItems)
                    if (r.item != null) RemoveItems(Model, r.item.Id, int.MaxValue);
            StoryQuestAbandoned(q); // 22.10. (STORY-01): Pferde-Geschichte zurueck aufs Angebot (sonst Tank-Klassenkette dauerhaft gesperrt)
            _inv.ServerCommit();
            SendState(true);
        }

        /// <summary>Sammelziele = Menge im Rucksack (verkauft/weggeworfen zaehlt wieder runter).</summary>
        private void RecountCollect()
        {
            if (!Loaded) return;
            foreach (QuestSave q in D.quests)
            {
                QuestDefinition def = GameData.Quest(q.id);
                if (def == null) continue;
                if (q.progress == null || q.progress.Length != def.objectives.Count) System.Array.Resize(ref q.progress, def.objectives.Count);
                for (int i = 0; i < def.objectives.Count; i++)
                {
                    QuestObjective o = def.objectives[i];
                    if (o.type == QuestObjectiveType.Collect) q.progress[i] = Mathf.Min(o.count, Model.Count(o.targetId));
                }
            }
        }

        // ================================================================== Server: Handel

        [ServerRpc]
        private void ServerBuy(string npcId, int entry, int times)
        {
            if (!Loaded || !InDialog(npcId, out NpcDefinition npc)) return;
            if (!npc.vendor || entry < 0 || entry >= npc.stock.Count || npc.stock[entry].item == null) { Reject("Nicht im Angebot."); return; }
            times = Mathf.Clamp(times, 1, 20);
            VendorEntry e = npc.stock[entry];
            long price = (long)Progression.BuyPrice(e.item, e.price, e.count) * times;   // 21.10. (FORTSCHRITT-02): Stueckzahl des Angebots
            int amount = e.count * times;
            if (D.money < price) { Reject("Nicht genug Geld."); return; }
            if (Model.SpaceFor(Stack(e.item, amount)) < amount) { Reject("Kein Platz im Inventar."); return; }
            Model.Add(Stack(e.item, amount));
            D.money -= price;
            _inv.ServerCommit();
            SendState(true);
            TargetCoins(Owner);
        }

        [ServerRpc]
        private void ServerSell(string npcId, int bagIndex)
        {
            if (!Loaded || !InDialog(npcId, out NpcDefinition npc)) return;
            if (!npc.vendor) { Reject("Hier wird nicht gehandelt."); return; }
            if (!InventoryModel.IsBag(bagIndex) || Model.Bag[bagIndex].IsEmpty) { Reject("Dort liegt nichts."); return; }
            ItemStack s = Model.Bag[bagIndex];
            ItemDefinition def = GameData.Item(s.id);
            if (!Progression.Sellable(def)) { Reject("Das kauft hier niemand."); return; }
            int value = def.sellPrice * s.count;
            if (!Model.RemoveAt(bagIndex, s.count).Ok) { Reject("Verkauf fehlgeschlagen."); return; }
            D.money += value;
            _buyback.Insert(0, new BuybackEntry { Stack = s, Price = value });
            while (_buyback.Count > buybackSlots) _buyback.RemoveAt(_buyback.Count - 1);
            _inv.ServerCommit();
            SendState(true);
            TargetBuyback(Owner, BuybackStacks(), BuybackPrices());
            TargetCoins(Owner);
        }

        [ServerRpc]
        private void ServerBuyBack(string npcId, int index)
        {
            if (!Loaded || !InDialog(npcId, out NpcDefinition npc)) return;
            if (!npc.vendor || index < 0 || index >= _buyback.Count) { Reject("Nicht mehr da."); return; }
            BuybackEntry b = _buyback[index];
            if (D.money < b.Price) { Reject("Nicht genug Geld."); return; }
            if (Model.SpaceFor(b.Stack) < b.Stack.count) { Reject("Kein Platz im Inventar."); return; }
            Model.Add(b.Stack);
            D.money -= b.Price;
            _buyback.RemoveAt(index);
            _inv.ServerCommit();
            SendState(true);
            TargetBuyback(Owner, BuybackStacks(), BuybackPrices());
            TargetCoins(Owner);
        }

        private ItemStack[] BuybackStacks()
        {
            var a = new ItemStack[_buyback.Count];
            for (int i = 0; i < a.Length; i++) a[i] = _buyback[i].Stack;
            return a;
        }

        private int[] BuybackPrices()
        {
            var a = new int[_buyback.Count];
            for (int i = 0; i < a.Length; i++) a[i] = _buyback[i].Price;
            return a;
        }

        // ================================================================== Server: Hilfen

        private InventoryModel Simulate()
        {
            var sim = new InventoryModel { Class = Model.Class, Level = Model.Level };
            sim.Load((ItemStack[])Model.Bag.Clone(), (ItemStack[])Model.Equipment.Clone(), (ItemStack[])Model.Containers.Clone(), Model.AmmoId);
            return sim;
        }

        private static ItemStack Stack(ItemDefinition def, int count) =>
            new ItemStack(def.Id, count, def.bind == BindType.OnPickup);

        private static void RemoveItems(InventoryModel m, string id, int count)
        {
            for (int i = InventoryModel.BagSize - 1; i >= 0 && count > 0; i--)
            {
                ItemStack s = m.Bag[i];
                if (s.IsEmpty || s.id != id) continue;
                int take = Mathf.Min(count, s.count);
                m.RemoveAt(i, take);
                count -= take;
            }
        }

        /// <summary>Server: wer fragt (Volk, Klasse, Geschichtsmarken) – fuer Klassen- und Geschichtsquests.</summary>
        public QuestGate ServerGate() => !Loaded ? null : new QuestGate { Race = D.race, Class = Model.Class, HasFlag = D.HasFlag };

        /// <summary>Server: Geschichtsschritt eines laufenden Auftrags setzen (Zieltyp Story). true = geaendert.</summary>
        public bool ServerSetStoryObjective(string questId, string storyKey, int value)
        {
            QuestSave q = Loaded ? FindQuest(questId) : null;
            QuestDefinition def = q != null ? GameData.Quest(questId) : null;
            if (def == null) return false;
            bool changed = false;
            for (int i = 0; i < def.objectives.Count; i++)
            {
                QuestObjective o = def.objectives[i];
                if (o.type != QuestObjectiveType.Story || o.targetId != storyKey) continue;
                int v = Mathf.Clamp(value, 0, o.count);
                if (q.progress[i] == v) continue;
                q.progress[i] = v;
                changed = true;
            }
            if (changed)
            {
                _inv.ServerCommit();
                SendState(false);
            }
            return changed;
        }

        private void Reject(string error)
        {
            if (Owner != null && Owner.IsActive) TargetError(Owner, error ?? "");
        }

        /// <summary>Test (nur Test-Profile): Auftrag ohne Voraussetzungen starten.</summary>
        public void ServerDebugStartQuest(string questId)
        {
            QuestDefinition q = GameData.Quest(questId);
            if (!Loaded || q == null || FindQuest(questId) != null || !TestRechte.Erlaubt(_profile)) return;
            D.quests.Add(new QuestSave { id = q.Id, progress = new int[q.objectives.Count] });
            _inv.ServerCommit();
            SendState(true);
        }

        /// <summary>Test/Verwaltung: Geld setzen.</summary>
        public void ServerSetMoney(long copper)
        {
            if (!Loaded) return;
            D.money = System.Math.Max(0, copper);
            _inv.ServerCommit();
            SendState(true);
        }

        // ================================================================== Stand an den Besitzer

        private void SendState(bool force)
        {
            if (!Loaded || Owner == null || !Owner.IsActive) return;
            string quests = EncodeQuests();
            string completed = string.Join(",", D.completedQuests);
            string key = D.xp + "|" + Model.Level + "|" + D.money + "|" + quests + "|" + completed + "|" + D.restedXp + "|" + (_restInArea ? 1 : 0);
            if (!force && key == _lastSent) return;
            _lastSent = key;
            TargetState(Owner, D.xp, Model.Level, D.money, quests, completed, D.restedXp, _restInArea);
        }

        private string EncodeQuests()
        {
            var sb = new StringBuilder();
            foreach (QuestSave q in D.quests)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(q.id).Append(':');
                for (int i = 0; i < q.progress.Length; i++)
                {
                    if (i > 0) sb.Append('.');
                    sb.Append(q.progress[i]);
                }
            }
            return sb.ToString();
        }

        [TargetRpc]
        private void TargetState(NetworkConnection conn, int xp, int level, long money, string quests, string completed, int rested, bool inRestArea)
        {
            _cXp = xp;
            ProgressionClient.SetRested(rested, inRestArea);
            _cLevel = level;
            _cMoney = money;
            _cQuests.Clear();
            if (!string.IsNullOrEmpty(quests))
            {
                foreach (string part in quests.Split(';'))
                {
                    int c = part.IndexOf(':');
                    if (c <= 0) continue;
                    QuestDefinition def = GameData.Quest(part.Substring(0, c));
                    if (def == null) continue;
                    string[] nums = part.Substring(c + 1).Split('.');
                    var prog = new int[def.objectives.Count];
                    for (int i = 0; i < prog.Length && i < nums.Length; i++) int.TryParse(nums[i], out prog[i]);
                    _cQuests.Add(new QuestView { Def = def, Progress = prog });
                }
            }
            _cCompleted.Clear();
            if (!string.IsNullOrEmpty(completed))
                foreach (string id in completed.Split(',')) if (id.Length > 0) _cCompleted.Add(id);
            _cReady = true;
            LastError = "";
            _cRev++;
            ProgressionClient.RaiseChanged();
        }

        [TargetRpc]
        private void TargetDialog(NetworkConnection conn, string npcId)
        {
            _cDialog = GameData.Npc(npcId);
            _cRev++;
            ProgressionClient.RaiseChanged();
        }

        [TargetRpc]
        private void TargetBuyback(NetworkConnection conn, ItemStack[] stacks, int[] prices)
        {
            _cBuyback.Clear();
            for (int i = 0; stacks != null && i < stacks.Length; i++)
                _cBuyback.Add(new BuybackEntry { Stack = stacks[i], Price = prices != null && i < prices.Length ? prices[i] : 0 });
            ProgressionClient.RaiseChanged();
        }

        [TargetRpc]
        private void TargetError(NetworkConnection conn, string error)
        {
            LastError = error;
            _cRev++;
            if (!string.IsNullOrEmpty(error)) CombatTextEvent.Error(error);
            ProgressionClient.RaiseChanged();
        }

        [TargetRpc]
        private void TargetMessage(NetworkConnection conn, string text)
        {
            ProgressionClient.RaiseMessage(text);
        }

        [TargetRpc]
        private void TargetQuest(NetworkConnection conn, string questId, bool completed)
        {
            QuestDefinition q = GameData.Quest(questId);
            SfxPlayer.Play(completed ? SfxId.QuestComplete : SfxId.QuestAccept, transform.position + Vector3.up, 0.7f, 0f);
            ProgressionClient.RaiseQuest(q != null ? q.Title : questId, completed);
        }

        [TargetRpc]
        private void TargetXp(NetworkConnection conn, int amount, Vector3 where)
        {
            ProgressionClient.RaiseXp(amount, where);
        }

        [TargetRpc]
        private void TargetCoins(NetworkConnection conn)
        {
            SfxPlayer.Play(SfxId.Coins, transform.position + Vector3.up, 0.6f, 0.05f);
        }

        [ObserversRpc]
        private void ObserversLevelUp(int level)
        {
            LevelUpEffect.Play(transform);
            if (IsOwner) ProgressionClient.RaiseLevelUp(level);
        }

        // ================================================================== IProgressionService (Client)

        public bool Ready => _cReady;
        public int Level => _cLevel;
        public int Xp => _cXp;
        public int XpNeeded => Progression.XpToNext(_cLevel);
        public long Money => _cMoney;
        public IReadOnlyList<QuestView> Quests => _cQuests;
        public bool IsCompleted(string questId) => _cCompleted.Contains(questId);

        public bool IsActive(string questId)
        {
            foreach (QuestView v in _cQuests) if (v.Def != null && v.Def.Id == questId) return true;
            return false;
        }

        public NpcDefinition DialogNpc => _cDialog;
        public IReadOnlyList<BuybackEntry> Buyback => _cBuyback;

        public void Talk(string npcId) { if (_cReady) ServerTalk(npcId); }

        public void CloseDialog()
        {
            if (_cDialog == null) return;
            _cDialog = null;
            if (IsClientInitialized) ServerCloseDialog();
            ProgressionClient.RaiseChanged();
        }

        public void Accept(string questId) { if (_cReady) ServerAccept(questId); }
        public void TurnIn(string questId, int choice) { if (_cReady) ServerTurnIn(questId, choice); }
        public void Abandon(string questId) { if (_cReady) ServerAbandon(questId); }
        public void Buy(int entryIndex, int times) { if (_cReady && _cDialog != null) ServerBuy(_cDialog.Id, entryIndex, times); }
        public void Sell(int bagIndex) { if (_cReady && _cDialog != null) ServerSell(_cDialog.Id, bagIndex); }
        public void BuyBack(int index) { if (_cReady && _cDialog != null) ServerBuyBack(_cDialog.Id, index); }

        private void LateUpdate()
        {
            // Weggelaufen: Gespraech schliesst sich
            if (!IsOwner || _cDialog == null) return;
            NpcIdentity id = NpcIdentity.Find(_cDialog.Id);
            if (id == null || Vector3.Distance(id.transform.position, transform.position) > _cDialog.interactRange + 1f) CloseDialog();
        }
    }
}
