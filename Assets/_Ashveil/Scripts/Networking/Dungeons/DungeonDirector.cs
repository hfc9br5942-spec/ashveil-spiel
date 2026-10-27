using System.Collections.Generic;
using Ashveil.Gameplay.Enemies;
using Ashveil.World;
using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Server: verwaltet die Instanzen eines Dungeons (M6, Entscheidung Max 26.09.: eigene Instanz je Gruppe).
    ///
    ///   Betreten  – wer in den Eingang (<see cref="DungeonPortal"/>) laeuft, kommt in die Instanz seiner Gruppe
    ///               (allein: eine eigene). Gibt es noch keine, wird ein freier Platz (<see cref="DungeonSlot"/>) belegt
    ///               und seine Gegner erscheinen.
    ///   Verlassen – ueber den Ausgang (Foerderkorb) zurueck vor den Eingang.
    ///   Tod       – Wiederbelebung am Eingang der Instanz (NetworkMotion-Wiederbelebungspunkt).
    ///   Zuruecksetzen – ist eine Instanz <see cref="resetAfterEmpty"/> Sekunden leer, verschwinden ihre Gegner und
    ///               der Platz wird frei. Gegner in Instanzen kehren vorher nicht zurueck.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class DungeonDirector : MonoBehaviour
    {
        [SerializeField] private string dungeonId = "salzgruben";
        [SerializeField] private string displayName = "Salzgruben";
        [Tooltip("Sekunden, die eine Instanz leer sein darf, bevor sie zurueckgesetzt wird")]
        [SerializeField] private float resetAfterEmpty = 300f;
        [SerializeField] private float tickInterval = 0.25f;
        [Tooltip("Nach dem Betreten/Verlassen so lange keine erneute Pruefung (s) – kein Hin-und-her")]
        [SerializeField] private float cooldown = 3f;

        [Header("Texte (Systemnachrichten)")]
        [SerializeField] private string enterFreshText = "Ihr betretet die Salzgruben. Salz knirscht unter den Stiefeln – man sagt, es sei das Fleisch der Alten.";
        [SerializeField] private string enterFollowText = "Du betrittst die Salzgruben und folgst deiner Gruppe.";
        [SerializeField] private string leaveText = "Du verlässt die Salzgruben.";
        [SerializeField] private string busyText = "Alle Stollen der Salzgruben sind gerade belegt – versuch es gleich noch einmal.";
        [SerializeField] private string notReadyText = "Die Salzgruben sind noch nicht bereit.";

        /// <summary>Editor: zweite Instanz-Art (Kolosseum) mit eigener Kennung und eigenen Texten.</summary>
        public void Configure(string id, string name, string enterFresh, string enterFollow, string leave, string busy, string notReady)
        {
            dungeonId = id;
            displayName = name;
            enterFreshText = enterFresh;
            enterFollowText = enterFollow;
            leaveText = leave;
            busyText = busy;
            notReadyText = notReady;
        }

        /// <summary>Tests: kuerzere Wartezeit bis zum Zuruecksetzen (kleiner 0 = Einstellung benutzen).</summary>
        public static float ResetOverride = -1f;

        public sealed class Instance
        {
            public DungeonSlot Slot;
            public string Key;
            public bool Active;
            public int Generation;
            public float EmptySince = -1f;
            public readonly List<EnemyBrain> Enemies = new List<EnemyBrain>();
            public readonly HashSet<string> Inside = new HashSet<string>();
        }

        private static readonly Dictionary<string, DungeonDirector> ById = new Dictionary<string, DungeonDirector>();
        private readonly List<Instance> _instances = new List<Instance>();
        private readonly Dictionary<string, float> _cooldown = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _lastSlot = new Dictionary<string, int>();
        private readonly List<NetworkInventory> _players = new List<NetworkInventory>();
        private NetworkManager _manager;
        private DungeonInstanceHandler _handler;
        private DungeonPortal _portal;
        private bool _running;
        private float _timer;

        public string DungeonId => dungeonId;
        public string DisplayName => displayName;
        public IReadOnlyList<Instance> Instances => _instances;
        public int FreeSlots { get { int n = 0; foreach (Instance i in _instances) if (!i.Active) n++; return n; } }
        public float ResetAfterEmpty => ResetOverride >= 0f ? ResetOverride : resetAfterEmpty;

        public static DungeonDirector For(string id) => id != null && ById.TryGetValue(id, out DungeonDirector d) && d != null ? d : null;

        /// <summary>In welchem Platz war dieses Profil zuletzt (Tests; -1 = nie)?</summary>
        public int LastSlotOf(string profile) => profile != null && _lastSlot.TryGetValue(profile, out int s) ? s : -1;

        /// <summary>Instanz, in der dieses Profil gerade steht (oder null).</summary>
        public Instance InstanceOf(string profile)
        {
            foreach (Instance i in _instances)
                if (i.Active && i.Inside.Contains(profile)) return i;
            return null;
        }

        private void Awake()
        {
            ById[dungeonId] = this;
            EnemyEncounter.Announced += OnEncounterAnnounced;
            EnemyEncounter.CueSent += OnEncounterCue;
        }

        /// <summary>Signale der Begegnungen (Flut, Tor) an alle Spieler in derselben Instanz.</summary>
        private void OnEncounterCue(Vector3 where, string cue, float value)
        {
            if (!_running) return;
            DungeonSlot slot = null;
            foreach (Instance i in _instances)
                if (i.Slot != null && i.Slot.Contains(where)) { slot = i.Slot; break; }
            if (slot == null) return;
            foreach (NetworkInventory inv in NetworkInventory.Online.Values)
                if (inv != null && inv.IsReady && slot.Contains(inv.transform.position))
                    NetworkSocial.ServerCue(inv.ProfileId, where, cue, value);
        }

        /// <summary>Wer eine Instanz betritt, bekommt ihren Zustand: erst alles auf Anfang, dann z. B. „Tor offen“.</summary>
        private static void SendInstanceState(string profile, DungeonSlot slot)
        {
            Vector3 c = slot.transform.position;
            NetworkSocial.ServerCue(profile, c, Ashveil.Core.EncounterCues.Reset, 0f);
            foreach (EnemyEncounter e in slot.GetComponentsInChildren<EnemyEncounter>(true))
                foreach (KeyValuePair<string, float> kv in e.PersistentCues)
                    NetworkSocial.ServerCue(profile, e.transform.position, kv.Key, kv.Value);
        }

        /// <summary>Boss-Ansagen an alle Spieler in derselben Instanz.</summary>
        private void OnEncounterAnnounced(Vector3 where, string text)
        {
            if (!_running) return;
            DungeonSlot slot = null;
            foreach (Instance i in _instances)
                if (i.Slot != null && i.Slot.Contains(where)) { slot = i.Slot; break; }
            if (slot == null) return;
            foreach (NetworkInventory inv in NetworkInventory.Online.Values)
                if (inv != null && inv.IsReady && slot.Contains(inv.transform.position))
                    NetworkSocial.ServerSystemMessage(inv.ProfileId, text);
        }

        private void Start()
        {
            _handler = GetComponent<DungeonInstanceHandler>();
            _manager = InstanceFinder.NetworkManager;
            if (_manager != null) _manager.ServerManager.OnServerConnectionState += OnServerState;
        }

        private void OnDestroy()
        {
            if (ById.TryGetValue(dungeonId, out DungeonDirector d) && d == this) ById.Remove(dungeonId);
            EnemyEncounter.Announced -= OnEncounterAnnounced;
            EnemyEncounter.CueSent -= OnEncounterCue;
            if (_manager != null && _manager.ServerManager != null) _manager.ServerManager.OnServerConnectionState -= OnServerState;
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                _running = true;
                _instances.Clear();
                var slots = new List<DungeonSlot>();
                foreach (DungeonSlot s in DungeonSlot.All)
                    if (s != null && s.DungeonId == dungeonId) slots.Add(s);
                slots.Sort((a, b) => a.Index.CompareTo(b.Index));
                foreach (DungeonSlot s in slots) _instances.Add(new Instance { Slot = s });
                _portal = DungeonPortal.Find(dungeonId);
                Debug.Log($"[Ashveil] Server: {displayName} – {_instances.Count} Instanz-Plaetze, Eingang {(_portal != null ? "bereit" : "FEHLT")}.");
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                if (_handler != null) foreach (Instance i in _instances) if (i.Active) _handler.OnReset(this, i);
                _running = false;
                _instances.Clear();
                _cooldown.Clear();
            }
        }

        // ================================================================== Takt

        private void Update()
        {
            if (!_running) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = tickInterval;
            Tick();
        }

        private void Tick()
        {
            _players.Clear();
            foreach (NetworkInventory inv in NetworkInventory.Online.Values)
                if (inv != null && inv.IsReady) _players.Add(inv);

            foreach (Instance i in _instances) i.Inside.Clear();
            float now = Time.time;
            foreach (NetworkInventory inv in _players)
            {
                string profile = inv.ProfileId;
                Vector3 pos = inv.transform.position;
                Instance here = null;
                foreach (Instance i in _instances)
                    if (i.Slot != null && i.Slot.Contains(pos)) { here = i; break; }
                if (here != null) here.Inside.Add(profile);

                bool ready = !_cooldown.TryGetValue(profile, out float until) || now >= until;
                if (here == null)
                {
                    // Nicht in einem Platz DIESES Dungeons. Steht die Figur in der Oberwelt (in keinem Instanz-Platz –
                    // auch nicht in einer anderen Instanz-Art), Wiederbelebungspunkt einer alten Instanz vergessen.
                    if (DungeonSlot.At(pos) != null) continue;
                    var motion = inv.GetComponent<NetworkMotion>();
                    if (motion != null && motion.HasRespawnPoint && ready) motion.ServerClearRespawnPoint();
                    if (ready && _portal != null && _portal.Contains(pos)) Enter(inv);
                }
                else if (ready && (pos - here.Slot.ExitWorld).sqrMagnitude < here.Slot.ExitRadius * here.Slot.ExitRadius)
                {
                    Leave(inv);
                }
            }

            if (_handler != null)
                foreach (Instance i in _instances)
                    if (i.Active) _handler.OnTick(this, i, now);

            // Leere Instanzen nach einer Weile zuruecksetzen
            foreach (Instance i in _instances)
            {
                if (!i.Active) continue;
                if (i.Inside.Count > 0) { i.EmptySince = -1f; continue; }
                if (i.EmptySince < 0f) i.EmptySince = now;
                else if (now - i.EmptySince >= ResetAfterEmpty) ResetInstance(i);
            }
        }

        // ================================================================== Betreten / Verlassen

        /// <summary>Schluessel der Instanz: die Gruppe oder – allein – das eigene Profil.</summary>
        public static string KeyOf(string profile)
        {
            PartyService.Party p = PartyService.Of(profile);
            return p != null ? "party:" + p.Id : "solo:" + profile;
        }

        private void Enter(NetworkInventory inv)
        {
            string profile = inv.ProfileId;
            _cooldown[profile] = Time.time + cooldown;
            if (EnemyDirector.Instance == null || !EnemyDirector.Instance.Ready)
            {
                NetworkSocial.ServerSystemMessage(profile, notReadyText);
                return;
            }
            string key = KeyOf(profile);
            Instance inst = null;
            foreach (Instance i in _instances)
                if (i.Active && i.Key == key) { inst = i; break; }
            // 22.10. (INSTANZEN-01): Wer allein hineinging und danach einlud, hatte eine Instanz „solo:<profil>“ – die Gruppe
            // („party:<Id>“) bekam beim Nachkommen einen frischen Platz, und beide standen getrennt. Steht ein Gruppenmitglied gerade
            // in seiner Allein-Instanz, uebernimmt die Gruppe diese (Leitung bevorzugt) und schluesselt sie um – Max-Regel „eigene
            // Instanz je Gruppe“ auch beim nachtraeglichen Einladen. Ob das Mitglied drin steht, entscheidet seine Position (Inside
            // wird in Tick gerade neu aufgebaut); verlassene Allein-Instanzen werden nicht uebernommen.
            if (inst == null && PartyService.Of(profile) is PartyService.Party party)
            {
                foreach (Instance i in _instances)
                {
                    if (!i.Active || i.Key == null || !i.Key.StartsWith("solo:", System.StringComparison.Ordinal)) continue;
                    string owner = i.Key.Substring(5);
                    if (owner == profile || !party.Members.Contains(owner)) continue;
                    if (!NetworkInventory.Online.TryGetValue(owner, out NetworkInventory o) || o == null || !o.IsReady ||
                        i.Slot == null || !i.Slot.Contains(o.transform.position)) continue;
                    inst = i;
                    if (owner == party.Leader) break; // Leitung bevorzugen
                }
                if (inst != null)
                {
                    Debug.Log($"[Ashveil] Server: {displayName} Platz {inst.Slot.Index} {inst.Key} -> {key} (Gruppe folgt).");
                    inst.Key = key;
                }
            }
            bool fresh = false;
            if (inst == null)
            {
                foreach (Instance i in _instances)
                    if (!i.Active) { inst = i; break; }
                if (inst == null)
                {
                    NetworkSocial.ServerSystemMessage(profile, busyText);
                    return;
                }
                Activate(inst, key);
                fresh = true;
            }

            // Mitglieder einer Gruppe nebeneinander ankommen lassen
            int n = inst.Inside.Count;
            Vector3 right = Quaternion.Euler(0f, inst.Slot.ArrivalYaw, 0f) * Vector3.right;
            Vector3 pos = inst.Slot.ArrivalWorld + right * ((n % 5) - 2) * 1.2f;
            var motion = inv.GetComponent<NetworkMotion>();
            if (motion != null)
            {
                motion.ServerTeleport(pos, inst.Slot.ArrivalYaw);
                motion.ServerSetRespawnPoint(inst.Slot.ArrivalWorld, inst.Slot.ArrivalYaw);
            }
            inst.Inside.Add(profile);
            inst.EmptySince = -1f;
            _lastSlot[profile] = inst.Slot.Index;
            SendInstanceState(profile, inst.Slot);
            if (_handler != null) _handler.OnEnter(this, inst, inv, fresh);
            NetworkSocial.ServerSystemMessage(profile, fresh ? enterFreshText : enterFollowText);
            Debug.Log($"[Ashveil] Server: {inv.CharacterName} betritt {displayName} (Platz {inst.Slot.Index}, {(fresh ? "neu" : "bestehend")}).");
        }

        /// <summary>Server: Spieler aus der Instanz vor den Eingang bringen (Kolosseum: Lauf verloren).</summary>
        public void ServerLeave(NetworkInventory inv)
        {
            if (inv != null) Leave(inv);
        }

        /// <summary>Wo man nach dem Verlassen steht (vor dem Eingang).</summary>
        public bool TryGetReturn(out Vector3 position, out float yaw)
        {
            position = _portal != null ? _portal.ReturnWorld : Vector3.zero;
            yaw = _portal != null ? _portal.ReturnYaw : 0f;
            return _portal != null;
        }

        private void Leave(NetworkInventory inv)
        {
            string profile = inv.ProfileId;
            if (_handler != null)
                foreach (Instance i in _instances)
                    if (i.Active && i.Inside.Contains(profile)) _handler.OnLeave(this, i, inv);
            _cooldown[profile] = Time.time + cooldown;
            var motion = inv.GetComponent<NetworkMotion>();
            if (motion != null)
            {
                motion.ServerClearRespawnPoint();
                if (_portal != null) motion.ServerTeleport(_portal.ReturnWorld, _portal.ReturnYaw);
                else motion.ServerTeleportToSpawn();
            }
            foreach (Instance i in _instances) i.Inside.Remove(profile);
            NetworkSocial.ServerSystemMessage(profile, leaveText);
        }

        private void Activate(Instance inst, string key)
        {
            inst.Active = true;
            inst.Key = key;
            inst.Generation++;
            inst.EmptySince = -1f;
            inst.Enemies.Clear();
            foreach (EnemySpawnArea area in inst.Slot.GetComponentsInChildren<EnemySpawnArea>(true))
            {
                if (area.SummonOnly) continue; // ruft der Boss (EnemyEncounter)
                for (int k = 0; k < area.Count; k++)
                {
                    EnemyBrain b = EnemyDirector.Instance.SpawnOnce(area);
                    if (b != null) inst.Enemies.Add(b);
                }
            }
            foreach (EnemyEncounter e in inst.Slot.GetComponentsInChildren<EnemyEncounter>(true)) e.ServerReset();
            if (_handler != null) _handler.OnActivate(this, inst);
            Debug.Log($"[Ashveil] Server: {displayName} Platz {inst.Slot.Index} belegt ({key}), {inst.Enemies.Count} Gegner.");
        }

        /// <summary>Instanz leeren: Gegner und Leichen entfernen, Platz freigeben.</summary>
        public void ResetInstance(Instance inst)
        {
            if (_handler != null) _handler.OnReset(this, inst);
            foreach (EnemyBrain b in inst.Enemies)
                if (b != null) EnemyDirector.Instance?.Remove(b);
            inst.Enemies.Clear();
            // Herbeigerufene Helfer der Bosse gehoeren keiner Liste – alles im Platz abraeumen
            var leftovers = new List<EnemyBrain>();
            foreach (EnemyBrain b in EnemyBrain.All)
                if (b != null && b.IsActive && inst.Slot.Contains(b.transform.position)) leftovers.Add(b);
            foreach (EnemyBrain b in leftovers) EnemyDirector.Instance?.Remove(b);
            foreach (EnemyEncounter e in inst.Slot.GetComponentsInChildren<EnemyEncounter>(true)) e.ServerReset();
            inst.Active = false;
            inst.Key = null;
            inst.EmptySince = -1f;
            Debug.Log($"[Ashveil] Server: {displayName} Platz {inst.Slot.Index} zurueckgesetzt.");
        }

        /// <summary>Lebende Gegner einer Instanz (Tests, Anzeige).</summary>
        public static int AliveIn(Instance inst)
        {
            int n = 0;
            foreach (EnemyBrain b in inst.Enemies)
                if (b != null && b.GetComponent<Ashveil.Gameplay.Combat.Health>() is var h && h != null && h.IsAlive) n++;
            return n;
        }
    }
}
