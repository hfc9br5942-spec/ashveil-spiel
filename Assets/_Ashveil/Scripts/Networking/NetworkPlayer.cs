using System.Collections;
using Ashveil.Core.Events;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Targeting;
using Ashveil.Gameplay.Weapons;
using Ashveil.World;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>Bewegungszustand fuer die Darstellung fremder Spieler (Position/Drehung kommen per NetworkTransform).</summary>
    public struct ProxyMotion
    {
        public Vector3 Velocity;
        public Vector3 DodgeDirection;
        public byte Flags;

        public const byte Grounded = 1, Sprinting = 2, Walking = 4, Dodging = 8, Plunging = 16, Jogging = 32, EpicWalking = 64;
    }

    /// <summary>
    /// Netzwerk-Adapter des Spielers. Das Gameplay (Bewegung, Waffen, Koerper) weiss nichts vom Netz –
    /// dieser Baustein verbindet es:
    ///
    ///   Eigener Spieler:  steuert lokal wie bisher und meldet Bewegungszustand, Spruenge, Rollen,
    ///                     Angriffe, Waffe und Koerper an den Server.
    ///   Fremder Spieler:  Eingabe, Kamera und Zielwahl sind aus; die Figur zeigt nur, was der Server
    ///                     weiterreicht (Position ueber NetworkTransform, Rest ueber RPCs/SyncVars).
    ///
    /// Stufe "Rauchtest" (Milestone 3.1): Bewegung ist noch client-autoritaer. Kampf wird in 3.2
    /// serverautoritaer, Bewegungs-Vorhersage mit Server-Korrektur folgt in Milestone 5.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    [DisallowMultipleComponent]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        [Tooltip("Wie oft pro Sekunde der Bewegungszustand gesendet wird")]
        [SerializeField] private float motionSendRate = 15f;
        [Tooltip("Namensschild ueber fremden Spielern")]
        [SerializeField] private WorldLabel nameplate;
        [Tooltip("Sekunden bis zur Wiederbelebung am Wachtfeuer")]
        [SerializeField] private float respawnDelay = 6f;
        [Tooltip("Ausserhalb des Kampfes: Leben pro Sekunde (Anteil vom Maximum)")]
        [SerializeField] private float outOfCombatRegen = 0.05f;
        [SerializeField] private float outOfCombatDelay = 8f;

        /// <summary>Eigene Figur ist gestartet (OnStartClient). REQ-20261019-003: Testcode (Lasttest-Bot) haengt sich hier an.</summary>
        public static event System.Action<NetworkPlayer> LocalPlayerStarted;

        private readonly SyncVar<bool> _melee = new SyncVar<bool>(true);
        private readonly SyncVar<string> _displayName = new SyncVar<string>(string.Empty);

        private CharacterMotor _motor;
        private WeaponUser _weapons;
        private CharacterBodySwitcher _bodies;
        private PlayerCharacter _player;
        private PlayerInputReader _input;
        private TargetingSystem _targeting;
        private SelectionIndicator _selection;
        private Health _health;
        private bool _serverHooked;

        private bool _local;
        private bool _subscribed;
        private float _sendTimer;

        private const byte EventJump = 1, EventLand = 2, EventDodge = 3, EventReload = 4;

        /// <summary>Anzeigename (vom Server vergeben).</summary>
        public string DisplayName => _displayName.Value;

        /// <summary>Server: Charaktername setzen (kommt mit dem Speicherstand).</summary>
        public void ServerSetName(string n)
        {
            if (!string.IsNullOrEmpty(n)) _displayName.Value = n;
        }

        /// <summary>Server: Name setzen, auch leer (M7: Spieler im Charakter-Editor hat noch keinen Namen und kein Schild).</summary>
        public void ServerSetName(string n, bool allowEmpty)
        {
            if (allowEmpty) _displayName.Value = n ?? "";
            else ServerSetName(n);
        }

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor>();
            _weapons = GetComponent<WeaponUser>();
            _bodies = GetComponent<CharacterBodySwitcher>();
            _player = GetComponent<PlayerCharacter>();
            _input = GetComponent<PlayerInputReader>();
            _targeting = GetComponent<TargetingSystem>();
            _selection = GetComponent<SelectionIndicator>();
            _health = GetComponent<Health>();

            _melee.OnChange += OnMeleeChanged;
            _displayName.OnChange += OnNameChanged;
        }

        private void OnDestroy()
        {
            ServerList.Remove(this);
            _melee.OnChange -= OnMeleeChanged;
            _displayName.OnChange -= OnNameChanged;
            Unsubscribe();
        }

        // ================================================================== Start

        // Server: alle Spielerfiguren (Spawn-Fairness und Gegnerdichte, CORE-008 02.10.)
        private static readonly System.Collections.Generic.List<NetworkPlayer> ServerList = new System.Collections.Generic.List<NetworkPlayer>();

        /// <summary>Server: alle angemeldeten Spielerfiguren.</summary>
        public static System.Collections.Generic.IReadOnlyList<NetworkPlayer> ServerPlayers => ServerList;

        /// <summary>Server: Figur lebt (zaehlt fuer Spawn-Abstand und Gebietsdichte).</summary>
        public bool ServerAlive => _health != null && _health.IsAlive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetServerList() => ServerList.Clear();

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (!ServerList.Contains(this)) ServerList.Add(this);
            _displayName.Value = "Wanderer " + (Owner.ClientId + 1);
            if (_health != null && !_serverHooked)
            {
                _serverHooked = true;
                _health.Died += OnServerDied;
            }
            // Reiner Server oder fremder Spieler auf dem Host: sofort nur Darstellung. Auf dem Host kommt
            // OnStartClient erst ein paar Ticks spaeter – bis dahin darf die Figur weder Kamera noch Eingabe uebernehmen.
            if (IsServerOnlyStarted || !Owner.IsLocalClient) BecomeProxy();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _local = IsOwner;
            if (_local)
            {
                BecomeLocal();
                LocalPlayerStarted?.Invoke(this); // NET-012 Lasttest-Bot (Ashveil.Networking.Testing)
                if (nameplate != null) nameplate.gameObject.SetActive(false);
                Ashveil.Gameplay.Characters.LocalPlayerInfo.Name = _displayName.Value ?? "";
                Subscribe();
                // Ausgangslage melden (spaeter hinzukommende Spieler bekommen sie ueber die SyncVars)
                if (_weapons != null) ServerSetMelee(_weapons.UsingMelee);
            }
            else
            {
                BecomeProxy();
                if (_weapons != null) _weapons.ProxySetWeapon(_melee.Value, true);
                OnNameChanged(string.Empty, _displayName.Value, false);
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ServerList.Remove(this);
            if (_serverHooked && _health != null) _health.Died -= OnServerDied;
            _serverHooked = false;
        }

        // ================================================================== Tod & Wiederbelebung (Server)

        /// <summary>
        /// Server: Regel fuer den Tod (Kolosseum Pass 4). Gibt sie einen Text zurueck, bleibt die Figur liegen (keine
        /// Wiederbelebung nach <see cref="respawnDelay"/>) und der Besitzer bekommt diesen Text; null = normaler Ablauf.
        /// </summary>
        public static System.Func<NetworkPlayer, string> HoldRespawn;

        /// <summary>Server: liegt die Figur gerade (vom Kolosseum festgehalten)?</summary>
        public bool ServerHeldDown { get; private set; }

        private void OnServerDied()
        {
            string hold = HoldRespawn != null ? HoldRespawn(this) : null;
            if (hold != null)
            {
                ServerHeldDown = true;
                TargetDownMessage(Owner, hold);
                return;
            }
            var m = GetComponent<NetworkMotion>();
            string where = null;
            if (m != null && m.HasRespawnPoint)
            {
                Ashveil.World.DungeonSlot slot = Ashveil.World.DungeonSlot.At(transform.position);
                where = slot != null ? slot.RespawnText : "am Eingang";
            }
            TargetDied(Owner, where ?? string.Empty);
            StartCoroutine(ServerRespawn());
        }

        /// <summary>
        /// P1: Zeit, in der ein Mitspieler den Gefallenen noch zurueckholen kann (Ruhe der Asche). Liefert die laengste
        /// Wartezeit in s (0 = normal). Belebt ihn jemand in dieser Zeit, entfaellt das Erwachen am Wachtfeuer.
        /// </summary>
        public static System.Func<NetworkPlayer, float> ResurrectWindow;

        private IEnumerator ServerRespawn()
        {
            float wait = respawnDelay;
            if (ResurrectWindow != null) wait = Mathf.Max(wait, ResurrectWindow(this));
            float until = Time.time + wait;
            while (Time.time < until)
            {
                if (_health != null && _health.IsAlive) yield break; // zurueckgeholt
                yield return null;
            }
            if (_health == null || _health.IsAlive) yield break;
            _health.ResetToFull();
            var motion = GetComponent<NetworkMotion>();
            if (motion != null) motion.ServerTeleportToSpawn(); // Server versetzt – der Besitzer folgt mit der Korrektur
            TargetRespawn(Owner);
        }

        /// <summary>Server: an Ort und Stelle wiederbeleben (Mitspieler im Kolosseum).</summary>
        public void ServerReviveHere(float fraction)
        {
            if (_health == null || _health.IsAlive) return;
            ServerHeldDown = false;
            _health.Revive(fraction);
            _health.SetInvulnerableFor(2f);
            TargetRespawn(Owner);
        }

        /// <summary>Server: sofort voll wiederbeleben und an diese Stelle versetzen (Kolosseum: Lauf verloren/beendet).</summary>
        public void ServerRespawnAt(Vector3 position, float yaw)
        {
            if (_health == null) return;
            ServerHeldDown = false;
            if (!_health.IsAlive) _health.Revive(1f);
            _health.ResetToFull();
            var motion = GetComponent<NetworkMotion>();
            if (motion != null) motion.ServerTeleport(position, yaw);
            TargetRespawn(Owner);
        }

        [TargetRpc]
        private void TargetDownMessage(NetworkConnection conn, string text) => CombatTextEvent.Error(text);

        [TargetRpc]
        private void TargetDied(NetworkConnection conn, string where)
        {
            CombatTextEvent.Error(!string.IsNullOrEmpty(where) ? $"Du bist gefallen – gleich erwachst du {where}."
                                                               : "Du bist gefallen – gleich erwachst du am Wachtfeuer.");
        }

        [TargetRpc]
        private void TargetRespawn(NetworkConnection conn)
        {
            if (_player != null) _player.Respawn();
        }

        /// <summary>Server: ausserhalb des Kampfes langsam heilen (wie in klassischen MMOs).</summary>
        private void ServerRegen()
        {
            if (_health == null || !_health.IsAlive || _health.Current >= _health.Max) return;
            if (Time.time - _health.LastDamageTime < outOfCombatDelay) return;
            _health.Heal(_health.Max * outOfCombatRegen * Time.deltaTime);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Unsubscribe();
        }

        /// <summary>Eigene Figur: alles an (falls vorher vorsichtshalber abgeschaltet).</summary>
        private void BecomeLocal()
        {
            if (GetComponent<ArenaInteractor>() == null) gameObject.AddComponent<ArenaInteractor>(); // Kolosseum Pass 4
            if (GetComponent<Ashveil.Gameplay.Story.StoryInteractor>() == null) gameObject.AddComponent<Ashveil.Gameplay.Story.StoryInteractor>(); // Geschichts-Handlungen
            if (_player != null) _player.enabled = true;
            if (_input != null) _input.enabled = true;
            if (_targeting != null) _targeting.enabled = true;
            if (_selection != null) _selection.enabled = true;
            if (_motor != null) _motor.SetProxy(false);
            // EINSTIEG-03 (21.10., Nachbesserung): Die Entwicklertaste F2 (Körper m/w wechseln, setzt Frisur/Bart zurück und
            // speichert) ist für vernetzte Spielerfiguren ganz aus – auch in Development-/Spieltest-Builds, die Max spielt (die
            // erste Fassung koppelte sie an DEVELOPMENT_BUILD und wirkte dort deshalb nicht). Körper wechselt man über C → Aussehen.
            if (_bodies != null) _bodies.SetLocalControl(false);
        }

        /// <summary>Fremde Figur: keine Eingabe, keine Kamera, keine Zielwahl – nur Darstellung.</summary>
        private void BecomeProxy()
        {
            if (_player != null) _player.enabled = false;
            if (_input != null) _input.enabled = false;
            if (_targeting != null) _targeting.enabled = false;
            if (_selection != null) _selection.enabled = false;
            if (_motor != null) _motor.SetProxy(true);
            if (_bodies != null) _bodies.SetLocalControl(false);
            if (nameplate != null) nameplate.gameObject.SetActive(true);
        }

        // ================================================================== Eigener Spieler -> Server

        private void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            if (_motor != null)
            {
                _motor.Jumped += OnLocalJumped;
                _motor.Landed += OnLocalLanded;
                _motor.Dodged += OnLocalDodged;
            }
            if (_weapons != null)
            {
                _weapons.AttackStarted += OnLocalAttack;
                _weapons.WeaponChanged += OnLocalWeaponChanged;
                _weapons.ReloadStarted += OnLocalReload;
            }
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            if (_motor != null)
            {
                _motor.Jumped -= OnLocalJumped;
                _motor.Landed -= OnLocalLanded;
                _motor.Dodged -= OnLocalDodged;
            }
            if (_weapons != null)
            {
                _weapons.AttackStarted -= OnLocalAttack;
                _weapons.WeaponChanged -= OnLocalWeaponChanged;
                _weapons.ReloadStarted -= OnLocalReload;
            }
        }

        private void Update()
        {
            if (IsServerInitialized) ServerRegen();
            if (!_local || !IsClientInitialized || _motor == null) return;
            _sendTimer -= Time.unscaledDeltaTime;
            if (_sendTimer > 0f) return;
            _sendTimer = 1f / Mathf.Max(1f, motionSendRate);

            MotorState s = _motor.State;
            byte flags = 0;
            if (s.Grounded) flags |= ProxyMotion.Grounded;
            if (s.IsSprinting) flags |= ProxyMotion.Sprinting;
            if (s.IsWalking) flags |= ProxyMotion.Walking;
            if (_motor.IsDodging) flags |= ProxyMotion.Dodging;
            if (s.IsPlunging) flags |= ProxyMotion.Plunging;
            if (s.IsJogging) flags |= ProxyMotion.Jogging;
            if (s.IsEpicWalking) flags |= ProxyMotion.EpicWalking;
            ServerMotion(new ProxyMotion { Velocity = s.Velocity, DodgeDirection = s.DodgeDirection, Flags = flags });
        }

        private void OnLocalJumped() => ServerEvent(EventJump, 0f, Vector3.zero);
        private void OnLocalLanded(float impact) => ServerEvent(EventLand, impact, Vector3.zero);
        private void OnLocalDodged(Vector3 dir) => ServerEvent(EventDodge, 0f, dir);
        private void OnLocalReload() => ServerEvent(EventReload, 0f, Vector3.zero);

        private void OnLocalAttack(Ashveil.Data.AttackStep step, bool special)
        {
            if (_weapons == null) return;
            ServerAttack((byte)_weapons.CurrentKind, (byte)Mathf.Clamp(_weapons.CurrentComboIndex, 0, 255));
        }

        private void OnLocalWeaponChanged(Ashveil.Data.WeaponDefinition w)
        {
            if (_weapons != null && IsClientInitialized) ServerSetMelee(_weapons.UsingMelee);
        }


        // ================================================================== RPCs

        [ServerRpc]
        private void ServerMotion(ProxyMotion motion, Channel channel = Channel.Unreliable)
        {
            ObserversMotion(motion);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ObserversMotion(ProxyMotion m, Channel channel = Channel.Unreliable)
        {
            // Auf dem Server simuliert der Motor selbst (Vorhersage) – gemeldete Werte nur fuer reine Clients
            if (_local || _motor == null || IsServerInitialized) return;
            _motor.ApplyProxyState(m.Velocity,
                (m.Flags & ProxyMotion.Grounded) != 0,
                (m.Flags & ProxyMotion.Sprinting) != 0,
                (m.Flags & ProxyMotion.Walking) != 0,
                (m.Flags & ProxyMotion.Dodging) != 0,
                m.DodgeDirection,
                (m.Flags & ProxyMotion.Plunging) != 0,
                (m.Flags & ProxyMotion.Jogging) != 0,
                (m.Flags & ProxyMotion.EpicWalking) != 0);
        }

        [ServerRpc]
        private void ServerEvent(byte kind, float value, Vector3 direction)
        {
            ObserversEvent(kind, value, direction);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ObserversEvent(byte kind, float value, Vector3 direction)
        {
            if (_local) return;
            if (IsServerInitialized && kind != EventReload) return; // Server-Simulation loest Sprung/Landung/Rolle selbst aus
            switch (kind)
            {
                case EventJump: if (_motor != null) _motor.RaiseProxyJumped(); break;
                case EventLand: if (_motor != null) _motor.RaiseProxyLanded(value); break;
                case EventDodge: if (_motor != null) _motor.RaiseProxyDodged(direction); break;
                case EventReload: if (_weapons != null) _weapons.ProxyReload(); break;
            }
        }

        [ServerRpc]
        private void ServerAttack(byte kind, byte comboIndex)
        {
            ObserversAttack(kind, comboIndex);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ObserversAttack(byte kind, byte comboIndex)
        {
            if (_local || _weapons == null) return;
            _weapons.ProxyAttack((AttackKind)kind, comboIndex);
        }


        [ServerRpc]
        private void ServerSetMelee(bool melee)
        {
            _melee.Value = melee;
            // Server braucht die aktive Waffe selbst (Pruefung der Angriffe), auch ohne eigenen Client
            if (!IsOwner && _weapons != null) _weapons.ProxySetWeapon(melee, IsServerOnlyStarted);
        }

        // ================================================================== SyncVar-Aenderungen


        private void OnMeleeChanged(bool prev, bool next, bool asServer)
        {
            if (asServer || _local || _weapons == null) return;
            _weapons.ProxySetWeapon(next);
        }

        private void OnNameChanged(string prev, string next, bool asServer)
        {
            if (!asServer && _local) Ashveil.Gameplay.Characters.LocalPlayerInfo.Name = next ?? "";
            if (asServer || _local || nameplate == null) return;
            nameplate.gameObject.SetActive(!string.IsNullOrEmpty(next));
            if (!string.IsNullOrEmpty(next)) nameplate.SetText(next, "");
        }
    }
}
