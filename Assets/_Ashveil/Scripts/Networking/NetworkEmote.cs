using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// M8: Emotes im Netz. Der Spieler bittet (Chat /winken, Emote-Menue J, Actionbar), der Server prueft
    /// (lebt, nicht im Kampf, steht am Boden) und verteilt nur Emote-ID + Starttakt – keine laufende Uebertragung.
    /// Beendet wird serverseitig durch Bewegung, Sprung, Ausweichen, Angriff, Schaden, Tod oder Ablauf (Einmal-Emotes);
    /// der eigene Client beendet bei Bewegung sofort selbst (kein Nachlaufen). Spaeter Hinzukommende sehen dieselbe Stelle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkEmote : NetworkBehaviour, IEmoteService
    {
        public const float CombatLock = 5f;
        private const float MoveCancelSpeed = 0.5f;

        private readonly SyncVar<byte> _emote = new SyncVar<byte>(0);
        private readonly SyncVar<uint> _startTick = new SyncVar<uint>(0);

        private CharacterBodySwitcher _bodies;
        private CharacterMotor _motor;
        private Health _health;
        private NetworkCombat _combat;
        private NetworkInventory _inv;
        private EmotePoser _poser;

        public EmoteId Current => (EmoteId)_emote.Value;
        public EmotePoser Poser => ResolvePoser();
        /// <summary>Server: Anzahl gestarteter Emotes (Tests).</summary>
        public int ServerStarted { get; private set; }
        public string ServerLastReject { get; private set; } = "";

        private void Awake()
        {
            _bodies = GetComponent<CharacterBodySwitcher>();
            _motor = GetComponent<CharacterMotor>();
            _health = GetComponent<Health>();
            _combat = GetComponent<NetworkCombat>();
            _inv = GetComponent<NetworkInventory>();
            _emote.OnChange += OnEmoteChanged;
            _startTick.OnChange += OnStartChanged;
            if (_bodies != null) _bodies.BodyApplied += OnBodyApplied;
        }

        private void OnDestroy()
        {
            _emote.OnChange -= OnEmoteChanged;
            _startTick.OnChange -= OnStartChanged;
            if (_bodies != null) _bodies.BodyApplied -= OnBodyApplied;
            EmoteClient.Unregister(this);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_health != null)
            {
                _health.Damaged += OnServerDamaged;
                _health.Died += OnServerDied;
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (_health != null)
            {
                _health.Damaged -= OnServerDamaged;
                _health.Died -= OnServerDied;
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner) EmoteClient.Register(this);
            Apply();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            EmoteClient.Unregister(this);
        }

        // ================================================================== Client

        public void Request(EmoteId id)
        {
            if (!IsClientInitialized || !IsOwner) return;
            if (!EmoteCatalog.TryGet(id, out _)) return;
            ResolvePoser()?.Play(id); // sofort zeigen, der Server bestaetigt oder nimmt es zurueck
            ServerPlay((byte)id);
        }

        public void RequestStop()
        {
            if (!IsClientInitialized || !IsOwner) return;
            ResolvePoser()?.Stop();
            ServerStop();
        }

        private void OnEmoteChanged(byte prev, byte next, bool asServer)
        {
            if (asServer && !IsClientInitialized) return;
            Apply();
        }

        private void OnStartChanged(uint prev, uint next, bool asServer)
        {
            if (asServer && !IsClientInitialized) return;
            Apply(); // dasselbe Emote nochmal (z. B. zweimal winken)
        }

        private void OnBodyApplied(int index)
        {
            _poser?.Stop();
            _poser = null;
            Apply();
        }

        private void Apply()
        {
            EmotePoser p = ResolvePoser();
            if (p == null) return;
            var id = (EmoteId)_emote.Value;
            if (id == EmoteId.None) { p.Stop(); return; }
            p.Play(id, ElapsedSinceStart());
        }

        private float ElapsedSinceStart()
        {
            if (TimeManager == null) return 0f;
            uint now = TimeManager.Tick;
            return now > _startTick.Value ? (float)((now - _startTick.Value) * TimeManager.TickDelta) : 0f;
        }

        private EmotePoser ResolvePoser()
        {
            if (_poser != null && _poser.isActiveAndEnabled) return _poser;
            GameObject model = _bodies != null ? _bodies.ActiveModel : null;
            _poser = model != null ? model.GetComponentInChildren<EmotePoser>(true) : GetComponentInChildren<EmotePoser>();
            return _poser;
        }

        private void Update()
        {
            // Eigener Spieler: Bewegung beendet das Emote sofort (der Server tut es ebenfalls)
            if (IsOwner && _motor != null)
            {
                EmotePoser p = ResolvePoser();
                if (p != null && p.Current != EmoteId.None && Moving()) p.Stop();
            }
            if (IsServerInitialized) ServerTick();
        }

        private bool Moving() =>
            _motor.HorizontalSpeed > MoveCancelSpeed || !_motor.State.Grounded || _motor.IsDodging;

        [TargetRpc]
        private void TargetRejected(NetworkConnection conn, string reason)
        {
            ResolvePoser()?.Stop();
            EmoteClient.RaiseMessage(reason);
        }

        // ================================================================== Server

        [ServerRpc]
        private void ServerPlay(byte id) => ServerTryPlay((EmoteId)id);

        [ServerRpc]
        private void ServerStop() => ServerClear();

        /// <summary>Server: pruefen und starten. Leer = gestartet, sonst der Grund (auch fuer Tests).</summary>
        public string ServerTryPlay(EmoteId id)
        {
            string why = ServerCheck(id);
            ServerLastReject = why;
            if (why.Length > 0)
            {
                if (Owner != null && Owner.IsActive) TargetRejected(Owner, why);
                return why;
            }
            _startTick.Value = TimeManager.Tick;
            _emote.Value = (byte)id;
            ServerStarted++;
            return "";
        }

        private string ServerCheck(EmoteId id)
        {
            if (!EmoteCatalog.TryGet(id, out _)) return "Unbekanntes Emote.";
            if (_inv != null && !_inv.IsReady) return "Nicht bereit.";
            if (_health != null && !_health.IsAlive) return "Nicht jetzt.";
            float lastFight = Mathf.Max(_health != null ? _health.LastDamageTime : -999f, _combat != null ? _combat.ServerLastAttackTime : -999f);
            if (Time.time - lastFight < CombatLock) return "Nicht mitten im Kampf.";
            if (_motor != null && _motor.IsSwimming) return "Nicht im Wasser.";
            if (_motor != null && (!_motor.State.Grounded || _motor.IsDodging)) return "Nicht in der Luft.";
            return "";
        }

        public void ServerClear()
        {
            if (_emote.Value != 0) _emote.Value = 0;
        }

        private void ServerTick()
        {
            if (_emote.Value == 0) return;
            if (!EmoteCatalog.TryGet((EmoteId)_emote.Value, out EmoteInfo info)) { ServerClear(); return; }
            if (!info.Loop && ElapsedSinceStart() > info.Duration + 0.2f) { ServerClear(); return; }
            if (_motor != null && Moving()) { ServerClear(); return; }
            if (_combat != null && Time.time - _combat.ServerLastAttackTime < 0.2f) ServerClear();
        }

        private void OnServerDamaged(DamageInfo info) => ServerClear();
        private void OnServerDied() => ServerClear();
    }
}
