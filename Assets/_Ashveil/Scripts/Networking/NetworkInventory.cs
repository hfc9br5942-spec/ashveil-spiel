using System.Collections;
using System.Collections.Generic;
using System.Text;
using Ashveil.Core.Events;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Items;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Inventar, Ausruestung und Beute eines Spielers im Netz – vollstaendig serverautoritaer:
    ///
    ///   Client  : bittet ("verschiebe 3 nach 7", "nimm alles") – zeigt nur, was der Server zurueckschickt.
    ///   Server  : prueft jede Aenderung am <see cref="InventoryModel"/>, speichert atomar, schickt dem Besitzer
    ///             den neuen Stand und allen anderen nur die angelegte Ausruestung (fuer Waffen/Werte).
    ///
    /// Anmeldung mit Profil-ID: Derselbe Charakter kann nur einmal online sein; eine zweite Anmeldung
    /// speichert und trennt die erste – so kann nichts doppelt existieren.
    /// </summary>
    [RequireComponent(typeof(CharacterEquipment))]
    [DisallowMultipleComponent]
    public sealed partial class NetworkInventory : NetworkBehaviour, IInventoryService, ILootRecipient, IRangedSupply
    {
        [Tooltip("Sekunden nach einer Aenderung bis zum Speichern (fasst schnelle Aenderungen zusammen)")]
        [SerializeField] private float saveDelay = 0.3f;
        [Tooltip("Laufende Aenderungen hoechstens alle so viele Sekunden speichern (CORE-005, Lasttest 11.10.2026: 50 Bots ≈ 2 Schreibvorgaenge/s). Abmelden, Auftragsabschluss und Stufenaufstieg speichern sofort.")]
        [SerializeField] private float minSaveInterval = 10f;
        private float _lastSave = -999f;
        [Tooltip("Maximale Entfernung zur Leiche beim Pluendern (Server-Pruefung, etwas grosszuegiger als der Client)")]
        [SerializeField] private float lootRange = 6.5f;

        private static readonly Dictionary<string, NetworkInventory> OnlineProfiles = new Dictionary<string, NetworkInventory>();

        private readonly SyncVar<string> _equipIds = new SyncVar<string>(string.Empty);
        private readonly SyncVar<string> _classId = new SyncVar<string>(string.Empty);
        private readonly SyncVar<byte> _level = new SyncVar<byte>(1);

        private CharacterEquipment _equipment;
        private Health _health;

        // ---- Server
        private InventoryModel _model;
        private CharacterSaveData _data;
        private string _profile;
        private bool _loaded;
        private bool _dirty;
        private float _saveAt;
        private int _rev;
        private bool _createdNew;
        private readonly Dictionary<string, float> _useReadyAt = new Dictionary<string, float>();

        // ---- Client (Besitzer)
        private readonly ItemStack[] _cBag = new ItemStack[InventoryModel.BagSize];
        private readonly ItemStack[] _cEquip = new ItemStack[InventoryModel.EquipCount];
        private readonly ItemStack[] _cContainers = new ItemStack[InventoryModel.BagSlotCount];
        private string _cAmmo = "";
        private CharacterClassDefinition _cClass;
        private int _cLevel = 1;
        private int _cRev;
        private bool _cReady;
        private NetworkObject _lootObject;
        private readonly List<ItemStack> _loot = new List<ItemStack>();
        private bool _lootOpen;
        private int _lootRev;

        // ================================================================== Oeffentlich

        public static IReadOnlyDictionary<string, NetworkInventory> Online => OnlineProfiles;

        /// <summary>Server: Charaktername.</summary>
        public string CharacterName => _data != null ? _data.name : "";

        /// <summary>Server: Profil-ID (leer bis angemeldet).</summary>
        public string ProfileId => _profile;
        /// <summary>Server: zuletzt gespeicherte Weltposition (Tests, Large World E2).</summary>
        public Ashveil.World.WorldPosition SavedPosition => _data != null ? _data.position : default;
        public CharacterClassDefinition LootClass => _model != null ? _model.Class : null;
        public bool IsReady => _loaded;
        /// <summary>Server: das massgebliche Inventar.</summary>
        public InventoryModel ServerModel => _model;
        public bool CreatedNew => _createdNew;
        /// <summary>Client: Stand-Nummer der letzten Antwort des Servers.</summary>
        public int ClientRevision => _cRev;
        public int LootRevision => _lootRev;
        public string LastError { get; private set; } = "";

        /// <summary>Server: Charakter geladen (Profil, Daten, Inventar stehen).</summary>
        public event System.Action ServerLoggedIn;
        /// <summary>Server: Inventar-Stand wurde an den Besitzer geschickt (nach jeder Aenderung).</summary>
        public event System.Action ServerInventoryChanged;

        private void Awake()
        {
            _equipment = GetComponent<CharacterEquipment>();
            _health = GetComponent<Health>();
            _equipIds.OnChange += OnEquipIdsChanged;
        }

        private void OnDestroy()
        {
            ReleasePendingCreation();
            _equipIds.OnChange -= OnEquipIdsChanged;
            InventoryClient.Unregister(this);
            Ashveil.Gameplay.Characters.SitzungClient.Abmelden(this);
        }

        // ================================================================== Start / Ende

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_health != null) _health.Healed += OnServerHealed;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ReleasePendingCreation();
            if (_health != null) _health.Healed -= OnServerHealed;
            if (_loaded)
            {
                KolosseumTruheBeimAbmelden();   // 22.10. (STORY-03)
                SaveNow();
                if (_profile != null && OnlineProfiles.TryGetValue(_profile, out NetworkInventory cur) && cur == this)
                    OnlineProfiles.Remove(_profile);
            }
            _loaded = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner)
            {
                InventoryClient.Register(this);
                Ashveil.Gameplay.Characters.SitzungClient.Anmelden(this);   // UI-204: „Speichern und beenden“
                ServerLogin(ProfileSelection.Resolve(), ProfileSelection.RequestedClass());
            }
            else if (!IsServerInitialized)
            {
                ApplyEquipmentFromSync();
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (!IsOwner) return;
            InventoryClient.Unregister(this);
            Ashveil.Gameplay.Characters.SitzungClient.Abmelden(this);
        }

        private void OnApplicationQuit()
        {
            if (_loaded)
            {
                KolosseumTruheBeimAbmelden();   // 22.10. (STORY-03): Host beendet – vor dem letzten Speichern und Flush
                SaveNow();
            }
            _stelleBeimBeendenErfasst = true;
            CharacterStore.FlushSaves();
        }

        /// <summary>
        /// 22.10. (STORY-03): Server – eine nach dem Sieg nicht geöffnete Kolosseum-Truhe vor dem letzten Speichern auszahlen
        /// (Trennen, Spiel schließen, erneute Anmeldung). Bisher verfiel die Belohnung, weil nur der Ausgang sie aushändigte.
        /// </summary>
        private void KolosseumTruheBeimAbmelden()
        {
            ColosseumInstanceHandler kolosseum = ColosseumInstanceHandler.Current;
            if (kolosseum != null) kolosseum.ServerOnLogout(this);
        }

        /// <summary>
        /// 22.10.2026 (REL-01, Kurztest Lauf 2 → 3): Nach OnApplicationQuit baut Unity die Szene in beliebiger Reihenfolge ab. War der
        /// WorldStreamer vor dem Stopp des Servers zerstört, erfasste das Speichern in OnStopServer die Stelle im Szenenzweig
        /// („szene:…“) – der nächste Login in Kareth v4 (Weltzellen) stellte sie nicht her, die Figur begann am Spawn. Die in
        /// OnApplicationQuit (Welt noch vollständig) erfasste Stelle bleibt deshalb bis zum Ende stehen.
        /// </summary>
        private bool _stelleBeimBeendenErfasst;

        // ================================================================== Anmeldung (Server)

        [ServerRpc]
        private void ServerLogin(string profile, string requestedClass)
        {
            if (_loaded || _awaitingCreation) return;
            profile = (profile ?? "").Trim().ToLowerInvariant();
            if (!CharacterStore.IsValidProfileId(profile))
            {
                Debug.LogWarning($"[Ashveil] Anmeldung mit ungueltiger Profil-ID abgelehnt.");
                Owner.Disconnect(true);
                return;
            }
            // 22.10.2026 (MEHRSPIELER-05): Gehört das Profil dem Host-Spieler selbst (online oder im Charakter-Editor), wird der
            // Neuankömmling abgewiesen – vorher trennte die zweite Anmeldung den Host aus seinem eigenen Spiel (kopierter Build-Ordner
            // samt ashveil_profile.txt beim Mitspieler, zweites Fenster). Wiederverbinden eines Clients bleibt wie bisher (unten).
            NetworkInventory besetzt = OnlineProfiles.TryGetValue(profile, out NetworkInventory hostFigur) && hostFigur != null ? hostFigur : Pending(profile);
            if (besetzt != null && besetzt != this && besetzt.Owner != null && besetzt.Owner.IsActive && besetzt.Owner.IsLocalClient)
            {
                Debug.LogWarning($"[Ashveil] Profil '{profile}' gehört dem Host-Spieler und ist schon im Spiel – zweite Anmeldung abgewiesen.");
                Owner.Disconnect(true);
                return;
            }
            // Derselbe Charakter ist schon online (z. B. Wiederverbinden, bevor die alte Verbindung weg ist)
            if (OnlineProfiles.TryGetValue(profile, out NetworkInventory other) && other != null && other != this)
            {
                other.KolosseumTruheBeimAbmelden();   // 22.10. (STORY-03): offene Truhe der alten Verbindung mitspeichern
                other.SaveNow();
                other._loaded = false;
                OnlineProfiles.Remove(profile);
                Debug.Log($"[Ashveil] Profil '{profile}' meldet sich erneut an – alte Verbindung wird getrennt.");
                if (other.Owner != null && other.Owner.IsActive) other.Owner.Disconnect(true);
            }

            // M7: noch kein Charakter -> Charakter-Editor beim Spieler; die Figur bleibt bis dahin unsichtbar und untaetig
            if (CharacterStore.NeedsCreation(profile))
            {
                if (!TryBeginCreation(profile, requestedClass))
                {
                    Debug.LogWarning("[Ashveil] Charakter-Erstellung fuer dieses Profil ist bereits offen.");
                    Owner.Disconnect(true);
                    return;
                }
                var pl = GetComponent<NetworkPlayer>();
                if (pl != null) pl.ServerSetName("", true);
                var rng = new System.Random(profile.GetHashCode());
                TargetNeedsCreation(Owner, Ashveil.Data.CharacterNameRules.Random(false, rng)); // Editor beginnt mit "Mann"
                Debug.Log($"[Ashveil] Profil '{profile}' hat noch keinen Charakter – Charakter-Editor wird geöffnet.");
                return;
            }
            FinishLogin(profile, CharacterStore.LoadOrCreate(profile, requestedClass, out _createdNew));
        }

        // ================================================================== M7: Charakter-Editor (Server)

        private bool _awaitingCreation;
        private static readonly Dictionary<string, NetworkInventory> PendingProfiles = new Dictionary<string, NetworkInventory>();

        /// <summary>Server: Spieler, der gerade im Charakter-Editor ist (Tests).</summary>
        public static NetworkInventory Pending(string profile) =>
            profile != null && PendingProfiles.TryGetValue(profile, out NetworkInventory n) && n != null && n._awaitingCreation ? n : null;
        private string _pendingProfile, _pendingClass;

        /// <summary>Server: Ein Ersteller je Profil. Wie beim Laden gewinnt eine neue Verbindung gegen die alte.</summary>
        internal bool TryBeginCreation(string profile, string requestedClass)
        {
            if (_loaded || _awaitingCreation || !CharacterStore.IsValidProfileId(profile)) return false;
            NetworkInventory previous = Pending(profile);
            if (previous != null && previous != this)
            {
                previous.ReleasePendingCreation();
                if (previous.Owner != null && previous.Owner.IsActive) previous.Owner.Disconnect(true);
            }
            _pendingProfile = profile;
            _pendingClass = requestedClass;
            _awaitingCreation = true;
            PendingProfiles[profile] = this;
            return true;
        }

        /// <summary>Nur der eingetragene Ersteller darf den Charakter speichern.</summary>
        internal bool OwnsPendingCreation => _awaitingCreation && _pendingProfile != null && Pending(_pendingProfile) == this;

        /// <summary>Freigabe bei Abbruch/Abschluss; ein alter Besitzer entfernt keine neuere Reservierung.</summary>
        internal void ReleasePendingCreation()
        {
            if (_pendingProfile != null && PendingProfiles.TryGetValue(_pendingProfile, out NetworkInventory current) && current == this)
                PendingProfiles.Remove(_pendingProfile);
            _awaitingCreation = false;
            _pendingProfile = null;
            _pendingClass = null;
        }

        /// <summary>Server: wartet dieser Spieler auf den Charakter-Editor?</summary>
        public bool AwaitingCreation => _awaitingCreation;
        public string PendingProfile => _pendingProfile;
        public static event System.Action<NetworkInventory, bool, string> ServerCreationAnswered;

        /// <summary>Client: fertigen Charakter schicken (Name + Aussehen). Der Server prueft alles.</summary>
        public void SubmitCreation(string name, CharacterAppearanceData look, string classId)
        {
            if (look == null || !IsClientInitialized) return;
            ServerCreateCharacter(name ?? "", look.Encode(), classId ?? "");
        }

        [ServerRpc]
        private void ServerCreateCharacter(string name, string encodedLook, string classId)
        {
            if (!OwnsPendingCreation || _loaded) { Answer(false, "Es gibt nichts zu erstellen."); return; }
            if (encodedLook == null || encodedLook.Length > 1024 || (name != null && name.Length > 64) || (classId != null && classId.Length > 64)) { Answer(false, "Ungültige Angaben."); return; }
            // Klassenwahl: leer = Vorgabe der Anmeldung; sonst nur die waehlbaren Klassen (kein Magier, keine erfundenen Ids)
            string cls = string.IsNullOrEmpty(classId) ? _pendingClass : classId;
            if (!string.IsNullOrEmpty(classId) && (!CharacterClassDefinition.IsSelectable(classId) || GameData.Class(classId) == null))
            { Answer(false, "Diese Klasse kann man (noch) nicht wählen."); return; }
            string err = CharacterStore.ValidateNewName(name, _pendingProfile, out string clean);
            if (err != null) { Answer(false, err); return; }
            CharacterAppearanceData look = CharacterAppearanceData.Decode(encodedLook);
            look.EnsureArrays();
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            if (cat != null && cat.Sanitize(look)) Debug.Log("[Ashveil] Charakter-Editor: ungültige Aussehen-Angaben korrigiert.");
            string profile = _pendingProfile;
            CharacterSaveData data = CharacterStore.CreateFromEditor(profile, cls, clean, look);
            ReleasePendingCreation();
            _createdNew = true;
            Debug.Log($"[Ashveil] Charakter '{clean}' für Profil '{profile}' erstellt.");
            Answer(true, clean);
            FinishLogin(profile, data);
        }

        private void Answer(bool ok, string message)
        {
            ServerCreationAnswered?.Invoke(this, ok, message);
            if (Owner != null && Owner.IsActive) TargetCreationResult(Owner, ok, message);
        }

        [TargetRpc]
        private void TargetNeedsCreation(NetworkConnection conn, string suggestedName)
        {
            Ashveil.Gameplay.Characters.CharacterCreationClient.RaiseNeeded(new CreationService(this), suggestedName);
        }

        [TargetRpc]
        private void TargetCreationResult(NetworkConnection conn, bool ok, string message)
        {
            Ashveil.Gameplay.Characters.CharacterCreationClient.RaiseResult(ok, message);
        }

        private sealed class CreationService : Ashveil.Gameplay.Characters.ICharacterCreationService
        {
            private readonly NetworkInventory _inv;
            public CreationService(NetworkInventory inv) { _inv = inv; }
            public void Submit(string name, CharacterAppearanceData look, string classId) { if (_inv != null) _inv.SubmitCreation(name, look, classId); }
        }

        /// <summary>Server: geladenen oder neu erstellten Charakter uebernehmen und alles verteilen.</summary>
        private void FinishLogin(string profile, CharacterSaveData data)
        {
            _data = data;
            _profile = profile;
            _model = new InventoryModel
            {
                Class = GameData.Class(_data.classId) ?? GameDataBootstrap.DefaultClass,
                Level = Mathf.Max(1, _data.level)
            };
            _model.Load(_data.bag, _data.equipment, _data.containers, _data.ammoId);
            _model.EquipmentChanged += ApplyEquipmentServer;
            OnlineProfiles[profile] = this;
            _loaded = true;
            _unlocked.Clear();
            foreach (string a in _data.unlockedAppearances) if (!string.IsNullOrEmpty(a)) _unlocked.Add(a);
            CollectUnlocks();
            ApplyEquipmentServer();
            var look = GetComponent<NetworkAppearance>();
            if (look != null) look.ServerApply(_data.appearance, false);
            var np = GetComponent<NetworkPlayer>();
            if (np != null) np.ServerSetName(_data.name);
            SendSnapshot(null);
            NotifyAllCorpses();
            if (!_createdNew && CharacterStore.RestoresPosition(profile))
            {
                // Szene ohne Weltzellen (Kareth v4): gespeicherte Stelle oder Bindepunkt; sonst Weltzellen wie bisher
                if (WorldStreamer.Instance == null && Ashveil.World.ScenePositionKey.Current != null) RestoreScenePosition(_data.position);
                else if (_data.position.IsSet) StartCoroutine(RestorePosition(_data.position));
            }
            ServerLoggedIn?.Invoke();
            Debug.Log($"[Ashveil] Charakter '{profile}' {(_createdNew ? "neu angelegt" : "geladen")} ({(_model.Class != null ? _model.Class.DisplayName : "ohne Klasse")}, Stufe {_model.Level}).");
        }

        // ================================================================== Weltposition (Large World E2)

        /// <summary>Server: aktuelle Position als Weltposition merken (in Dungeon-Instanzen bleibt die letzte Weltposition).</summary>
        private void CapturePosition()
        {
            WorldStreamer ws = WorldStreamer.Instance;
            if (_data == null) return;
            if (_stelleBeimBeendenErfasst) return;   // 22.10.2026 (REL-01): Abbau nach OnApplicationQuit – Stelle von dort behalten
            var h = GetComponent<Health>();
            bool tot = h != null && !h.IsAlive;
            if (ws == null || ws.Manifest == null)
            {
                // Szene ohne Weltzellen (Kareth v4): Position mit Szenenschlüssel; in einer Dungeon-Instanz bleibt die letzte
                Ashveil.World.ScenePositionKey key = Ashveil.World.ScenePositionKey.Current;
                var motion = GetComponent<NetworkMotion>();
                if (key == null || (motion != null && motion.HasRespawnPoint)) return;
                // 21.10. (MEHRSPIELER-02 / EINSTIEG-04): tot gespeichert = so, wie die Wiederbelebung entschiede – am Bindepunkt.
                // Bisher blieb die letzte Lebend-Stelle stehen; wer tot das Spiel schloss oder die Verbindung verlor (die
                // Wiederbelebung ist nur eine Coroutine am alten Objekt), stand beim naechsten Anmelden lebend am Sterbeort.
                // Erst NACH der Dungeon-Pruefung: in einer Instanz bleibt die Weltposition vor dem Dungeon erhalten.
                // Ohne gespeicherte Stelle setzt RestoreScenePosition an den Bindepunkt; der naechste Lebend-Speicherstand
                // (Erwachen am Wachtfeuer oder Wiederbelebung am Ort) erfasst wieder die echte Stelle.
                if (tot) { _data.position = default; return; }
                _data.position = key.Capture(transform.position, transform.eulerAngles.y);
                return;
            }
            Vector3 pos = transform.position;
            float yaw = transform.eulerAngles.y;
            if (tot)
            {
                // 21.10. (EINSTIEG-04 / MEHRSPIELER-02, Nachbesserung aus der Suite spielablauf3): Kareth v4 hat seit dem Weidepass
                // (12.10.) einen Weltzellen-Streamer (Westweiden) – dort greift DIESER Zweig, nicht der Szenenzweig oben. Bisher blieb
                // hier die letzte Lebend-Stelle stehen, und RestorePosition setzte eine tot getrennte Figur wieder an den Sterbeort.
                // Tot gespeichert = so, wie die Wiederbelebung entschiede: am Bindepunkt (Wachtfeuer). In einer Dungeon-Instanz
                // (Wiederbelebungspunkt) bleibt die Weltposition vor dem Dungeon erhalten.
                // 21.10. (Nachbesserung): Ohne Bindepunkt (Sandbox-Welt) belebt ServerRespawn am Spawn wieder – deshalb dort keine
                // Stelle speichern (vorher blieb die Lebend-Stelle stehen = Anmeldung am Sterbeort). Ohne Stelle überspringt der Login
                // RestorePosition, die Figur beginnt am Spawn.
                var motion = GetComponent<NetworkMotion>();
                Ashveil.World.BindPoint bind = Ashveil.World.BindPoint.Current;
                if (motion != null && motion.HasRespawnPoint) return;
                if (bind == null) { _data.position = default; return; }
                pos = bind.Position;
                yaw = bind.Yaw;
            }
            Ashveil.World.WorldCellDefinition cell = ws.Manifest.CellAt(pos, out Ashveil.World.WorldRegionDefinition region);
            if (cell == null || region == null) return;
            _data.position = new Ashveil.World.WorldPosition
            {
                regionId = region.regionId,
                cellId = cell.cellId,
                localPosition = pos,
                yaw = yaw,
                worldVersion = ws.Manifest.version
            };
        }

        /// <summary>
        /// Server: nach dem Login an die gespeicherte Stelle. Test-Profile beginnen immer am Wachtfeuer (Regression).
        /// Ungueltige Weltversion -> naechster sicherer Anker der Zelle. Die Zelle wird vorher geladen (Regel 5).
        /// </summary>
        private IEnumerator RestorePosition(Ashveil.World.WorldPosition wp)
        {
            WorldStreamer ws = WorldStreamer.Instance;
            var motion = GetComponent<NetworkMotion>();
            if (ws == null || ws.Manifest == null || motion == null || !wp.IsSet) yield break;
            Ashveil.World.WorldCellDefinition cell = ws.Manifest.FindCell(wp.cellId);
            if (cell == null) yield break;                       // Zelle gibt es nicht mehr -> Wachtfeuer
            ws.RequestPreload(wp.localPosition, 30f);
            float until = Time.time + 20f;
            while (!ws.IsReadyAt(wp.localPosition) && Time.time < until) yield return null;
            if (!ws.IsReadyAt(wp.localPosition)) { Debug.LogWarning($"[Ashveil] Weltposition: Zelle {wp.cellId} nicht rechtzeitig bereit – Start am Wachtfeuer."); yield break; }
            Vector3 target = wp.localPosition;
            float yaw = wp.yaw;
            if (wp.worldVersion != ws.Manifest.version)
            {
                Ashveil.World.SafeSpawnAnchor a = Ashveil.World.SafeSpawnAnchor.Nearest(target, wp.cellId);
                if (a == null) yield break;
                target = a.transform.position;
                yaw = a.transform.eulerAngles.y;
                Debug.Log($"[Ashveil] Weltposition von '{_profile}' aus Weltversion {wp.worldVersion} – sicherer Anker {a.AnchorId}.");
            }
            else if (!Ashveil.Core.HoehlenVolumen.Enthaelt(target) && Ashveil.Core.WorldGround.TrySample(target, out float ground) && target.y < ground - 0.5f)
                target.y = ground + 0.2f; // unter das Gelaende geraten: auf den Boden (nicht in einer Hoehle)
            motion.ServerTeleport(target + Vector3.up * 0.1f, yaw);
            Debug.Log($"[Ashveil] '{_profile}' betritt die Welt bei {wp.regionId}/{wp.cellId} ({target.x:0}/{target.z:0}).");
        }

        /// <summary>
        /// Server, Szene ohne Weltzellen (Kareth v4): Ein bekannter Charakter beginnt an seiner gespeicherten Stelle, wenn sie in
        /// dieser Szene gilt (<see cref="Ashveil.World.ScenePositionKey.Accepts"/>), sonst am Bindepunkt (Wachtfeuer). Neue
        /// Charaktere bleiben am Spawn (Ankunftsstelle). Test-Profile kommen hier nicht an (<see cref="CharacterStore.RestoresPosition"/>).
        /// </summary>
        private void RestoreScenePosition(Ashveil.World.WorldPosition wp)
        {
            var motion = GetComponent<NetworkMotion>();
            Ashveil.World.ScenePositionKey key = Ashveil.World.ScenePositionKey.Current;
            if (motion == null || key == null) return;
            if (key.Accepts(wp))
            {
                Vector3 target = wp.localPosition;
                if (!Ashveil.Core.HoehlenVolumen.Enthaelt(target) && Ashveil.Core.WorldGround.TrySample(target, out float ground) && target.y < ground - 0.5f)
                    target.y = ground + 0.2f; // unter das Gelaende geraten: auf den Boden (nicht in einer Hoehle)
                motion.ServerTeleport(target + Vector3.up * 0.1f, wp.yaw);
                Debug.Log($"[Ashveil] '{_profile}' betritt {key.Key} an der gespeicherten Stelle ({target.x:0}/{target.z:0}).");
                return;
            }
            Ashveil.World.BindPoint bind = Ashveil.World.BindPoint.Current;
            if (bind == null) return;
            motion.ServerTeleport(bind.Position, bind.Yaw);
            Debug.Log($"[Ashveil] '{_profile}' beginnt am Bindepunkt ({(wp.IsSet ? "gespeicherte Stelle gilt in " + key.Key + " nicht" : "keine gespeicherte Stelle")}).");
        }

        private bool Guard()
        {
            if (_loaded && _model != null) return true;
            return false;
        }

        // ================================================================== Aussehen (Server)

        private readonly HashSet<string> _unlocked = new HashSet<string>();

        /// <summary>Server: freigeschaltete Aussehen (jedes Item, das man besessen hat).</summary>
        public ICollection<string> UnlockedAppearances => _unlocked;

        private void CollectUnlocks()
        {
            if (_model == null) return;
            bool added = false;
            foreach (ItemStack s in _model.Bag) added |= Unlock(s);
            foreach (ItemStack s in _model.Equipment) added |= Unlock(s);
            if (added) MarkDirty();
        }

        private bool Unlock(ItemStack s)
        {
            if (s.IsEmpty) return false;
            ItemDefinition d = GameData.Item(s.id);
            return d != null && d.appearance != null && _unlocked.Add(d.appearance.Id);
        }

        /// <summary>Server: Aussehen im Charakter ablegen (NetworkAppearance hat es geprueft).</summary>
        public void ServerStoreAppearance(CharacterAppearanceData d)
        {
            if (_data == null) return;
            _data.appearance = d.Clone();
            _data.appearance.race = Ashveil.Data.RaceCatalog.IndexOf(_data.race); // das Volk steht nach der Erstellung fest
            MarkDirty();
        }

        /// <summary>Server: gespeicherte Daten (Tests).</summary>
        public CharacterSaveData ServerData => _data;

        // ================================================================== Speichern (Server)

        private void MarkDirty()
        {
            _dirty = true;
            _saveAt = Mathf.Max(Time.unscaledTime + saveDelay, _lastSave + minSaveInterval);
        }

        private void Update()
        {
            if (_dirty && _loaded && Time.unscaledTime >= _saveAt) SaveNow();
            if (_lootOpen && _lootObject == null) CloseLootLocal();
            else if (_lootOpen && _lootObject != null && IsOwner &&
                     Vector3.Distance(_lootObject.transform.position, transform.position) > lootRange + 1f) CloseLootLocal();
        }

        /// <summary>Server: sofort speichern (Abmelden, Beenden, Tests).</summary>
        public void SaveNow()
        {
            if (!_loaded || _data == null || _model == null) return;
            _data.bag = (ItemStack[])_model.Bag.Clone();
            _data.equipment = (ItemStack[])_model.Equipment.Clone();
            _data.containers = (ItemStack[])_model.Containers.Clone();
            _data.ammoId = _model.AmmoId ?? "";
            _data.classId = _model.Class != null ? _model.Class.Id : "";
            _data.level = _model.Level;
            _data.unlockedAppearances = new List<string>(_unlocked);
            CapturePosition();
            try
            {
                CharacterStore.Service.Save(_data);
                _dirty = false;
                _lastSave = Time.unscaledTime;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Ashveil] Speichern von '{_profile}' fehlgeschlagen: {e.Message}");
            }
        }

        // ================================================================== Ausruestung

        private void ApplyEquipmentServer()
        {
            if (_model == null) return;
            _equipment.Apply(_model.Equipment, _model.Class, _model.Level, true);
            _equipIds.Value = JoinIds(_model.Equipment);
            _classId.Value = _model.Class != null ? _model.Class.Id : "";
            _level.Value = (byte)Mathf.Clamp(_model.Level, 1, 255);
        }

        private void OnEquipIdsChanged(string prev, string next, bool asServer)
        {
            if (asServer || IsServerInitialized || IsOwner) return;
            ApplyEquipmentFromSync();
        }

        private void ApplyEquipmentFromSync()
        {
            ItemStack[] eq = ParseIds(_equipIds.Value);
            _equipment.Apply(eq, GameData.Class(_classId.Value), _level.Value, false);
        }

        private static string JoinIds(ItemStack[] stacks)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < stacks.Length; i++)
            {
                if (i > 0) sb.Append('|');
                if (!stacks[i].IsEmpty) sb.Append(stacks[i].id);
            }
            return sb.ToString();
        }

        private static ItemStack[] ParseIds(string s)
        {
            var result = new ItemStack[InventoryModel.EquipCount];
            if (string.IsNullOrEmpty(s)) return result;
            string[] parts = s.Split('|');
            for (int i = 0; i < parts.Length && i < result.Length; i++)
                if (parts[i].Length > 0) result[i] = new ItemStack(parts[i], 1);
            return result;
        }

        // ================================================================== Stand an den Besitzer

        private void SendSnapshot(string error)
        {
            if (_loaded) ServerInventoryChanged?.Invoke();
            if (!_loaded || Owner == null || !Owner.IsActive) return;
            _rev++;
            TargetInventory(Owner, _rev, (ItemStack[])_model.Bag.Clone(), (ItemStack[])_model.Equipment.Clone(),
                (ItemStack[])_model.Containers.Clone(), _model.AmmoId ?? "",
                _model.Class != null ? _model.Class.Id : "", (byte)Mathf.Clamp(_model.Level, 1, 255), error ?? "");
        }

        [TargetRpc]
        private void TargetInventory(NetworkConnection conn, int rev, ItemStack[] bag, ItemStack[] equip, ItemStack[] containers, string ammoId,
            string classId, byte level, string error)
        {
            for (int i = 0; i < _cBag.Length; i++) _cBag[i] = bag != null && i < bag.Length ? bag[i] : ItemStack.Empty;
            for (int i = 0; i < _cEquip.Length; i++) _cEquip[i] = equip != null && i < equip.Length ? equip[i] : ItemStack.Empty;
            for (int i = 0; i < _cContainers.Length; i++) _cContainers[i] = containers != null && i < containers.Length ? containers[i] : ItemStack.Empty;
            _cAmmo = ammoId ?? "";
            _cClass = GameData.Class(classId);
            _cLevel = level;
            _cRev = rev;
            _cReady = true;
            LastError = error ?? "";
            if (!IsServerInitialized) _equipment.Apply(_cEquip, _cClass, _cLevel, false);
            if (!string.IsNullOrEmpty(error)) CombatTextEvent.Error(error);
            InventoryClient.RaiseChanged();
        }

        private void Result(InvResult r)
        {
            if (r.Ok)
            {
                MarkDirty();
                CollectUnlocks();
            }
            SendSnapshot(r.Ok ? null : r.Error);
        }

        // ================================================================== Munition (WoW: Pfeile im Koecher, Verbrauch je Schuss)

        /// <summary>Server: einen Pfeil der gewaehlten Sorte verbrauchen. Der Besitzer erfaehrt nur die geaenderte Zelle.</summary>
        public bool ServerConsumeAmmo(AmmoType type)
        {
            if (!_loaded || _model == null) return true; // Labor/Tests ohne Inventar: unbegrenzt
            ItemDefinition def = _model.AmmoDef;
            if (def == null || def.ammoType != type) return false;
            int cell = _model.ConsumeAmmoCell();
            if (cell < 0) return false;
            _dirty = true;
            _saveAt = Time.unscaledTime + 5f; // Pfeile sind haeufig – gesammelt speichern
            if (Owner != null && Owner.IsActive) TargetAmmoCell(Owner, cell, _model.Bag[cell]);
            if (_model.AmmoCount <= 0) SendSnapshot("Keine Munition mehr");
            return true;
        }

        [TargetRpc]
        private void TargetAmmoCell(NetworkConnection conn, int cell, ItemStack stack)
        {
            if (cell < 0 || cell >= _cBag.Length) return;
            _cBag[cell] = stack;
            InventoryClient.RaiseChanged();
        }

        private bool UseServerView => IsServerInitialized && _model != null && _loaded;

        public int AmmoCount(AmmoType type)
        {
            if (UseServerView)
            {
                ItemDefinition d = _model.AmmoDef;
                return d != null && d.ammoType == type ? _model.AmmoCount : 0;
            }
            if (!_cReady && !IsServerInitialized) return 1; // noch kein Stand: nicht blockieren, der Server entscheidet
            ItemDefinition def = string.IsNullOrEmpty(_cAmmo) ? null : GameData.Item(_cAmmo);
            if (def == null || def.ammoType != type) return 0;
            int n = 0;
            for (int i = 0; i < _cBag.Length; i++) if (!_cBag[i].IsEmpty && _cBag[i].id == _cAmmo) n += _cBag[i].count;
            return n;
        }

        public float AmmoDps(AmmoType type)
        {
            string id = UseServerView ? _model.AmmoId : _cAmmo;
            ItemDefinition def = string.IsNullOrEmpty(id) ? null : GameData.Item(id);
            return def != null && def.ammoType == type ? def.ammoDps : 0f;
        }

        public float RangedHaste
        {
            get
            {
                if (UseServerView) return _model.RangedHaste;
                float best = 0f;
                foreach (ItemStack c in _cContainers)
                {
                    ItemDefinition d = c.IsEmpty ? null : GameData.Item(c.id);
                    if (d != null && d.containerKind != ContainerKind.General) best = Mathf.Max(best, d.rangedHaste);
                }
                return best;
            }
        }

        // ================================================================== IInventoryService (Client)

        public bool Ready => _cReady;
        public ItemStack[] Bag => _cBag;
        public ItemStack[] Equipment => _cEquip;
        public ItemStack[] Containers => _cContainers;
        public string AmmoId => _cAmmo;
        public CharacterClassDefinition Class => _cClass;
        public int Level => _cLevel;

        public void Move(int from, int to) { if (_cReady) ServerMove(from, to); }
        public void Split(int from, int amount, int to) { if (_cReady) ServerSplit(from, amount, to); }
        public void Use(int bagIndex) { if (_cReady) ServerUse(bagIndex); }
        public void UseItem(string itemId) { if (_cReady && !string.IsNullOrEmpty(itemId) && itemId.Length <= 64) ServerUseItem(itemId); }
        public void AutoEquip(int bagIndex) { if (_cReady) ServerAutoEquip(bagIndex); }
        public void Unequip(EquipmentSlot slot) { if (_cReady) ServerUnequip((byte)slot); }

        [ServerRpc] private void ServerMove(int from, int to) { if (Guard()) Result(_model.Move(from, to)); }
        [ServerRpc] private void ServerSplit(int from, int amount, int to) { if (Guard()) Result(_model.Split(from, amount, to)); }
        [ServerRpc] private void ServerAutoEquip(int bagIndex) { if (Guard()) Result(_model.AutoEquip(bagIndex)); }

        [ServerRpc]
        private void ServerUnequip(byte slot)
        {
            if (!Guard()) return;
            if (slot >= InventoryModel.EquipCount) { SendSnapshot("Ungueltiger Platz."); return; }
            Result(_model.Unequip((EquipmentSlot)slot));
        }

        /// <summary>Actionbar: Gegenstand nach ID – erster passender Rucksackplatz, danach dieselben Pruefungen wie beim Klick.</summary>
        [ServerRpc]
        private void ServerUseItem(string itemId) => UseItemServer(itemId);

        private void UseItemServer(string itemId)
        {
            if (!Guard()) return;
            int index = -1;
            for (int i = 0; i < InventoryModel.BagSize; i++)
                if (!_model.Bag[i].IsEmpty && _model.Bag[i].id == itemId) { index = i; break; }
            if (index < 0) { SendSnapshot("Davon hast du nichts mehr."); return; }
            ItemDefinition def = _model.Def(_model.Bag[index]);
            if (def == null || def.IsEquippable || def.category != ItemCategory.Consumable) { SendSnapshot("Das kann man nicht benutzen."); return; }
            UseAt(index);
        }

        /// <summary>Server: Gegenstand nach ID benutzen (Tests, spaeter NPC-Skripte) – wie ServerUseItem.</summary>
        public void ServerUseItemById(string itemId) => UseItemServer(itemId);

        [ServerRpc]
        private void ServerUse(int bagIndex)
        {
            if (!Guard()) return;
            UseAt(bagIndex);
        }

        private void UseAt(int bagIndex)
        {
            if (!InventoryModel.IsBag(bagIndex) || _model.Bag[bagIndex].IsEmpty) { SendSnapshot("Dort liegt nichts."); return; }
            ItemStack s = _model.Bag[bagIndex];
            ItemDefinition def = _model.Def(s);
            if (def == null) { SendSnapshot("Unbekannter Gegenstand."); return; }
            if (def.IsEquippable || def.IsAmmo) { Result(_model.AutoEquip(bagIndex)); return; }
            if (def.requiredLevel > _model.Level) { SendSnapshot($"Benötigt Stufe {def.requiredLevel}."); return; }
            if (_useReadyAt.TryGetValue(def.Id, out float ready) && Time.time < ready) { SendSnapshot("Noch nicht bereit."); return; }

            if (def.teachesRecipe != null)
            {
                RecipeDefinition rec = def.teachesRecipe;
                string prof = rec.profession != null ? rec.profession.Id : "";
                int skill = _data.ProfessionSkillOf(prof);
                if (rec.profession != null && skill < 0) { SendSnapshot($"Benötigt den Beruf {rec.profession.DisplayName}."); return; }
                if (rec.profession != null && skill < rec.requiredSkill) { SendSnapshot($"Benötigt {rec.profession.DisplayName} ({rec.requiredSkill})."); return; }
                if (_data.knownRecipes.Contains(rec.Id)) { SendSnapshot("Dieses Rezept kennst du bereits."); return; }
                _data.knownRecipes.Add(rec.Id);
                Result(_model.RemoveAt(bagIndex, 1));
                return;
            }
            if (def.category != ItemCategory.Consumable) { SendSnapshot("Das kann man nicht benutzen."); return; }
            if (_health == null || !_health.IsAlive) { SendSnapshot("Du bist tot."); return; }

            foreach (ItemEffect e in def.effects)
            {
                if (e.type == ItemEffectType.Heal) _health.Heal(e.value, gameObject);
                else if (e.type == ItemEffectType.HealOverTime) StartCoroutine(HealOverTime(e.value, 10f));
            }
            if (def.useCooldown > 0f)
            {
                _useReadyAt[def.Id] = Time.time + def.useCooldown;
                if (Owner != null && Owner.IsActive) TargetCooldown(Owner, def.Id, def.useCooldown);
            }
            Result(_model.RemoveAt(bagIndex, 1));
        }

        /// <summary>Abklingzeit eines Gegenstands an den Besitzer (Actionbar zeigt Fuellung und Restzeit).</summary>
        [TargetRpc]
        private void TargetCooldown(NetworkConnection conn, string itemId, float seconds) => InventoryClient.SetCooldown(itemId, seconds);

        /// <summary>Server: Restsperre eines Gegenstands (Tests).</summary>
        public float ServerCooldownRemaining(string itemId) =>
            itemId != null && _useReadyAt.TryGetValue(itemId, out float t) ? Mathf.Max(0f, t - Time.time) : 0f;

        /// <summary>Server: Speicherstand geaendert (z. B. Actionbar) – verzoegert speichern.</summary>
        public void ServerMarkDirty() { if (_loaded) MarkDirty(); }

        private IEnumerator HealOverTime(float total, float duration)
        {
            float t = 0f;
            while (t < duration && _health != null && _health.IsAlive)
            {
                yield return new WaitForSeconds(1f);
                t += 1f;
                _health.Heal(total / duration, gameObject);
            }
        }

        // ================================================================== Beute

        public bool LootOpen => _lootOpen;
        public GameObject LootSource => _lootObject != null ? _lootObject.gameObject : null;
        public IReadOnlyList<ItemStack> Loot => _loot;

        public bool TryOpenLoot(GameObject corpse)
        {
            if (!_cReady || corpse == null) return false;
            NetworkObject nob = corpse.GetComponentInParent<NetworkObject>();
            if (nob == null) return false;
            ServerOpenLoot(nob);
            return true;
        }

        public void TakeLoot(int index)
        {
            if (_lootOpen && _lootObject != null) ServerTakeLoot(_lootObject, index);
        }

        public void TakeAllLoot()
        {
            if (_lootOpen && _lootObject != null) ServerTakeLoot(_lootObject, -1);
        }

        public void CloseLoot() => CloseLootLocal();

        private void CloseLootLocal()
        {
            if (!_lootOpen) return;
            _lootOpen = false;
            _lootObject = null;
            _loot.Clear();
            InventoryClient.RaiseLootChanged();
        }

        private bool ServerCorpse(NetworkObject corpseObj, out CorpseLoot corpse, bool checkRange)
        {
            corpse = corpseObj != null ? corpseObj.GetComponent<CorpseLoot>() : null;
            // 21.10. (Nr. 8 Gefallene): tot nicht pluendern – gilt fuers Oeffnen und Nehmen (wie Benutzen, UseAt)
            if (_health != null && !_health.IsAlive)
            {
                TargetLootError(Owner, "Du bist tot.");
                return false;
            }
            if (corpse == null || !corpse.IsRecipient(_profile))
            {
                TargetLootError(Owner, "Hier gibt es für dich nichts.");
                return false;
            }
            if (checkRange && Vector3.Distance(corpseObj.transform.position, transform.position) > lootRange)
            {
                TargetLootError(Owner, "Zu weit entfernt.");
                return false;
            }
            return true;
        }

        [ServerRpc]
        private void ServerOpenLoot(NetworkObject corpseObj)
        {
            if (!Guard() || !ServerCorpse(corpseObj, out CorpseLoot corpse, true)) return;
            TargetLoot(Owner, corpseObj, corpse.Get(_profile).ToArray(), true);
        }

        [ServerRpc]
        private void ServerTakeLoot(NetworkObject corpseObj, int index)
        {
            if (!Guard() || !ServerCorpse(corpseObj, out CorpseLoot corpse, true)) return;
            int takenStacks = 0, blocked = 0, blockedUnique = 0, droppedQuest = 0;
            int i = index < 0 ? 0 : index;
            while (i < corpse.ItemCount(_profile))
            {
                int before = corpse.ItemCount(_profile);
                bool questDropped = false;
                int taken = corpse.TakeInto(_profile, i, stack =>
                {
                    ItemDefinition def = _model.Def(stack);
                    // 21.10. (Max: Questgegenstand nur, solange der Auftrag laeuft und noch welche fehlen). Der nicht benoetigte Rest
                    // verschwindet aus der PERSOENLICHEN Beute dieses Spielers – sonst glimmt die Leiche weiter, oeffnet immer wieder
                    // und versperrt die Nachlese. Platzmangel bleibt Platzmangel (dann bleibt alles liegen wie bisher).
                    int room = QuestLootRoom(stack, def);
                    if (room >= stack.count)
                    {
                        if (def != null && def.bind == BindType.OnPickup) stack.bound = true;
                        return _model.Add(stack);
                    }
                    int added = 0;
                    if (room > 0)
                    {
                        ItemStack part = stack.WithCount(room);
                        if (def != null && def.bind == BindType.OnPickup) part.bound = true;
                        added = _model.Add(part);
                        if (added < room) return added;   // kein Platz: nichts verwerfen
                    }
                    questDropped = true;
                    return stack.count;                   // Benoetigtes aufgenommen, Rest verworfen
                });
                if (taken > 0) takenStacks++;
                if (questDropped) droppedQuest++;
                bool removed = corpse.ItemCount(_profile) < before;
                if (!removed)
                {
                    blocked++;
                    ItemStack rest = i < corpse.ItemCount(_profile) ? corpse.Get(_profile)[i] : default;
                    ItemDefinition rd = string.IsNullOrEmpty(rest.id) ? null : _model.Def(rest);
                    if (rd != null && rd.unique && _model.Count(rest.id) > 0) blockedUnique++;
                    i++; // (teilweise) liegen geblieben
                }
                if (index >= 0) break;
            }
            if (takenStacks > 0)
            {
                MarkDirty(); // Beute mit dem naechsten Speichertakt sichern (hoechstens 10 s spaeter; vorher sofort – CORE-005)
            }
            // Klare Meldung (CORE-007): Einzigartiges und nicht mehr Benoetigtes sind kein Platzproblem
            string err = blocked == 0 ? (droppedQuest > 0 ? "Brauchst du nicht mehr." : null)
                : blockedUnique == blocked ? "Davon kannst du nur einen besitzen."
                : "Kein Platz im Inventar – der Rest bleibt liegen.";
            SendSnapshot(err);
            List<ItemStack> left = corpse.Get(_profile);
            TargetLoot(Owner, corpseObj, left.ToArray(), left.Count > 0);
            if (left.Count == 0) TargetLootAvailable(Owner, corpseObj, false);
        }

        /// <summary>
        /// 21.10. (Max): Wie viele Stueck eines Questgegenstands darf der Spieler noch aufheben? Kein Questgegenstand (oder questId ohne
        /// Auftragsdefinition, z. B. Glockensplitter) → unbegrenzt.
        /// Eigener Auftrag laeuft nicht → 0; laeuft er und ist der Gegenstand ein Sammelziel → was bis zur verlangten Menge fehlt.
        /// Questgegenstaende, die kein Sammelziel sind (z. B. Geschichtsstuecke), bleiben bei laufendem Auftrag frei.
        /// </summary>
        private int QuestLootRoom(ItemStack stack, ItemDefinition def)
        {
            if (def == null || def.category != ItemCategory.Quest || string.IsNullOrEmpty(def.questId)) return int.MaxValue;
            // alte Fundstuecke ohne Auftrag (Verblichener Brief, Glockensplitter: questId ohne Auftragsdefinition) bleiben Beute – wie ServerWantsLoot
            QuestDefinition q = GameData.Quest(def.questId);
            if (q == null) return int.MaxValue;
            NetworkProgression prog = GetComponent<NetworkProgression>();
            if (prog == null) return int.MaxValue;
            if (!prog.ServerIsActive(def.questId)) return 0;
            int need = -1;
            foreach (QuestObjective o in q.objectives)
                if (o.type == QuestObjectiveType.Collect && o.targetId == stack.id) need = Mathf.Max(need, o.count);
            if (need < 0) return int.MaxValue;
            return Mathf.Max(0, need - _model.Count(stack.id));
        }

        [TargetRpc]
        private void TargetLoot(NetworkConnection conn, NetworkObject corpseObj, ItemStack[] items, bool open)
        {
            _lootRev++;
            _loot.Clear();
            if (items != null) _loot.AddRange(items);
            _lootObject = corpseObj;
            _lootOpen = open && corpseObj != null && _loot.Count > 0;
            if (!_lootOpen) { _lootObject = null; _loot.Clear(); }
            InventoryClient.RaiseLootChanged();
        }

        [TargetRpc]
        private void TargetLootError(NetworkConnection conn, string message)
        {
            _lootRev++;
            CombatTextEvent.Error(message);
            InventoryClient.RaiseLootChanged();
        }

        [TargetRpc]
        private void TargetLootAvailable(NetworkConnection conn, NetworkObject corpseObj, bool available)
        {
            if (corpseObj == null) return;
            var li = corpseObj.GetComponent<LootIndicator>();
            if (li != null) li.SetAvailable(available);
        }

        /// <summary>Server: Leiche hat (noch) Beute fuer diesen Spieler – Glimmen einschalten.</summary>
        public void NotifyLootAvailable(NetworkObject corpseObj)
        {
            if (!_loaded || corpseObj == null || Owner == null || !Owner.IsActive) return;
            var corpse = corpseObj.GetComponent<CorpseLoot>();
            TargetLootAvailable(Owner, corpseObj, corpse != null && corpse.HasLootFor(_profile));
        }

        private void NotifyAllCorpses()
        {
            foreach (CorpseLoot c in CorpseLoot.All)
            {
                if (c == null || !c.HasLootFor(_profile)) continue;
                var nob = c.GetComponent<NetworkObject>();
                if (nob != null && nob.IsSpawned) NotifyLootAvailable(nob);
            }
        }

        // ================================================================== Beteiligung durch Heilen

        private void OnServerHealed(float amount, GameObject healer)
        {
            if (healer != null && healer != gameObject) LootEligibility.CreditSupport(healer, gameObject, amount);
        }

        // ================================================================== Test/Entwicklung (nur Server)

        /// <summary>Server: Gegenstaende direkt geben (Selbsttest, spaeter GM-Befehle). Gibt die aufgenommene Menge zurueck.</summary>
        public int ServerGive(ItemStack stack)
        {
            if (!_loaded) return 0;
            int n = _model.Add(stack);
            if (n > 0) Result(InvResult.Success);
            return n;
        }

        /// <summary>Server: entfernt alle Stapel eines Gegenstands aus dem Rucksack.</summary>
        public int ServerRemoveAll(string id)
        {
            if (!_loaded) return 0;
            int removed = 0;
            for (int i = 0; i < InventoryModel.BagSize; i++)
            {
                if (_model.Bag[i].IsEmpty || _model.Bag[i].id != id) continue;
                removed += _model.Bag[i].count;
                _model.RemoveAt(i, _model.Bag[i].count);
            }
            if (removed > 0) Result(InvResult.Success);
            return removed;
        }

        /// <summary>Server: Aenderung am Modell von aussen (Auftraege, Handel) bestaetigen – speichern und Stand senden.</summary>
        public void ServerCommit(string error = null)
        {
            if (!_loaded) return;
            MarkDirty();
            CollectUnlocks();
            SendSnapshot(error);
        }

        /// <summary>Server: nur Fehlermeldung/Stand senden, ohne Aenderung.</summary>
        public void ServerReject(string error) => SendSnapshot(error);

        /// <summary>Server: Stufe setzen (Stufenaufstieg, Tests). Ausruestung wird neu bewertet.</summary>
        public void ServerSetLevel(int level)
        {
            if (!_loaded) return;
            _model.Level = Mathf.Clamp(level, 1, 255);
            ApplyEquipmentServer();
            Result(InvResult.Success);
        }

        /// <summary>Server, nur Test-Profile: Rucksack und Ausruestung leeren.</summary>
        public void ServerResetForTest()
        {
            if (!_loaded || !TestRechte.Erlaubt(_profile)) return;
            _model.Load(new ItemStack[InventoryModel.BagSize], new ItemStack[InventoryModel.EquipCount]);
            ApplyEquipmentServer();
            Result(InvResult.Success);
        }

        /// <summary>Server: fuellt alle freien Plaetze mit je einem Stueck (Test "volles Inventar").</summary>
        public int ServerFillFreeSlots(string fillerId)
        {
            if (!_loaded) return 0;
            int n = 0;
            for (int i = 0; i < InventoryModel.BagSize; i++)
            {
                if (!_model.Bag[i].IsEmpty || !_model.Usable(i) || _model.KindOf(InventoryModel.BagOf(i)) != ContainerKind.General) continue;
                _model.Bag[i] = new ItemStack(fillerId, 1);
                n++;
            }
            _model.RaiseChanged();
            Result(InvResult.Success);
            return n;
        }
    }
}
