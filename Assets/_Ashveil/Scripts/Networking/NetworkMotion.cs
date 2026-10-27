using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Bewegung mit voller Server-Vorhersage (FishNet Prediction, Client-Side Prediction + Reconciliation):
    ///
    ///   Besitzer : sammelt pro Netz-Takt einen <see cref="MoveCommand"/> (nur Absichten, keine Positionen),
    ///              simuliert sofort selbst (keine Eingabeverzoegerung) und schickt den Befehl zum Server.
    ///   Server   : simuliert mit demselben Motor und denselben Befehlen – seine Position gilt. Er schickt
    ///              jeden Takt den Motor-Zustand zurueck.
    ///   Besitzer : weicht sein Stand ab, setzt er sich auf den Server-Zustand und spielt die noch
    ///              unbestaetigten Befehle erneut ab. Die Grafik (Kind "Graphics") wird geglaettet.
    ///   Andere   : sehen die Server-Position ueber den (server-autoritativen) NetworkTransform.
    ///
    /// Folge: Geschwindigkeit, Spruenge, Ausweichen und Ausdauer kann kein Client mehr faelschen.
    /// Teleports (Wiederbeleben, Tests) laufen ueber den Server.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor))]
    [DisallowMultipleComponent]
    public sealed class NetworkMotion : TickNetworkBehaviour
    {
        public struct MoveData : IReplicateData
        {
            public MoveCommand Cmd;
            private uint _tick;

            public MoveData(MoveCommand cmd)
            {
                Cmd = cmd;
                _tick = 0;
            }

            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public struct StateData : IReconcileData
        {
            public MotorState State;
            private uint _tick;

            public StateData(MotorState state)
            {
                State = state;
                _tick = 0;
            }

            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        [Tooltip("Faellt der Spieler unter diese Hoehe, setzt ihn der Server zum Wachtfeuer zurueck")]
        [SerializeField] private float killHeight = -60f;

        private CharacterMotor _motor;
        private PlayerCharacter _player;
        private Health _health;
        private NetworkInventory _inv;
        private Vector3 _spawnPosition;
        private float _spawnYaw;
        // Dungeon-Instanz: Wiederbelebung am Eingang der Instanz statt am Wachtfeuer (nur Server)
        private bool _hasRespawnPoint;
        private Vector3 _respawnPoint;
        private float _respawnYaw;
        private MoveData _lastTicked;

        // Besitzer: Position vor dem Abgleich – danach vergleichen = der Ruck, den der Spieler spuert
        private Vector3 _preReconcilePosition;
        private bool _reconciledThisTick;
        private float _ignoreCorrectionsUntil;
        private bool _reconcileHooked;

        /// <summary>Test/Diagnose: wie oft der Besitzer auf den Server-Stand korrigiert wurde.</summary>
        public int Corrections { get; private set; }
        /// <summary>Test/Diagnose: groesste Korrektur (m).</summary>
        public float MaxCorrection { get; private set; }

        /// <summary>Test/Diagnose: Zaehler zuruecksetzen.</summary>
        public void ResetCorrectionStats()
        {
            Corrections = 0;
            MaxCorrection = 0f;
            MaxCorrectionInfo = "-";
            _statsSince = Time.unscaledTime;
        }

        /// <summary>Test/Diagnose: wo und wann die groesste Korrektur passierte.</summary>
        public string MaxCorrectionInfo { get; private set; } = "-";
        private float _statsSince;

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor>();
            _player = GetComponent<PlayerCharacter>();
            _health = GetComponent<Health>();
            _inv = GetComponent<NetworkInventory>();
            _spawnPosition = transform.position;
            _spawnYaw = transform.eulerAngles.y;
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        /// <summary>Alle vernetzten Figuren dieses Prozesses (Weltzellen-Bedarf, <see cref="WorldStreamer"/>).</summary>
        public static readonly System.Collections.Generic.List<NetworkMotion> Active = new System.Collections.Generic.List<NetworkMotion>();

        // Fallschutz (Server): letzte Position auf geladenem Boden
        private Vector3 _lastSafe;
        private bool _hasLastSafe;
        private float _heldLogged;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            if (!Active.Contains(this)) Active.Add(this);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            Active.Remove(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActive() => Active.Clear();

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Der eigene Spieler simuliert nicht mehr im Frame-Takt, sondern nur noch im Netz-Takt
            if (IsOwner && _player != null) _player.NetworkDriven = true;
            if (IsOwner && !IsServerInitialized && !_reconcileHooked)
            {
                PredictionManager.OnPreReconcile += OnPreReconcile;
                PredictionManager.OnPostReconcile += OnPostReconcile;
                _reconcileHooked = true;
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (IsOwner && _player != null) _player.NetworkDriven = false;
            if (_reconcileHooked && PredictionManager != null)
            {
                PredictionManager.OnPreReconcile -= OnPreReconcile;
                PredictionManager.OnPostReconcile -= OnPostReconcile;
            }
            _reconcileHooked = false;
        }

        private void OnPreReconcile(uint clientTick, uint serverTick)
        {
            _preReconcilePosition = transform.position;
            _reconciledThisTick = false;
        }

        /// <summary>Nach Abgleich und erneutem Abspielen: wie weit ist der Spieler gesprungen?</summary>
        private void OnPostReconcile(uint clientTick, uint serverTick)
        {
            if (!_reconciledThisTick) return;
            float error = Vector3.Distance(_preReconcilePosition, transform.position);
            if (error > 0.01f && Time.unscaledTime >= _ignoreCorrectionsUntil)
            {
                Corrections++;
                if (error > MaxCorrection)
                {
                    MaxCorrection = error;
                    Vector3 a = _preReconcilePosition, b = transform.position;
                    MaxCorrectionInfo = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0:0.0}/{1:0.0}/{2:0.0}>{3:0.0}/{4:0.0}/{5:0.0}@{6:0.00}s,t{7}",
                        a.x, a.y, a.z, b.x, b.y, b.z, Time.unscaledTime - _statsSince, clientTick);
                }
            }
        }

        // ================================================================== Takt

        protected override void TimeManager_OnTick()
        {
            Replicate(BuildMoveData());
        }

        protected override void TimeManager_OnPostTick()
        {
            CreateReconcile();
        }

        private MoveData BuildMoveData()
        {
            if (!IsOwner || _player == null) return default;
            return new MoveData(_player.ConsumeCommand());
        }

        public override void CreateReconcile()
        {
            Reconcile(new StateData(_motor.CaptureState()));
        }

        [Replicate]
        private void Replicate(MoveData md, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            // Zuschauer simulieren nicht (sie sehen die Server-Position)
            if (!IsServerInitialized && !IsOwner) return;

            // Server: fehlt ein Befehl (Paketverlust), den letzten fuer einen Takt weiterfuehren – ohne Einmal-Aktionen
            if (IsServerInitialized && !IsOwner)
            {
                if (state.ContainsTicked() && state.ContainsCreated()) _lastTicked = md;
                else if (!state.ContainsCreated())
                {
                    uint gap = md.GetTick() - _lastTicked.GetTick();
                    md = gap <= 1 ? _lastTicked : default;
                    md.Cmd.Jump = false;
                    md.Cmd.Dodge = false;
                }
            }

            MoveCommand cmd = md.Cmd;
            if (_health != null && !_health.IsAlive) cmd = default; // gefallen: nur Schwerkraft

            // Wiederholte Takte (Korrektur) duerfen keine Klaenge/Animationen erneut ausloesen
            _motor.SuppressEvents = state.IsReplayed();
            _motor.Simulate(cmd, (float)TimeManager.TickDelta);
            _motor.SuppressEvents = false;

            if (IsServerInitialized && !state.IsReplayed())
            {
                GuardWorld();
                TickDrowning((float)TimeManager.TickDelta);
            }
        }

        // Ertrinken (MOV-169): Ist der Atem aufgebraucht und der Kopf unter Wasser, verliert die Figur alle drownInterval
        // Sekunden einen Anteil ihres Höchstlebens (Classic-Vorbild, Werte offen). Nur der Server teilt Schaden aus.
        private float _drownTimer;

        private void TickDrowning(float dt)
        {
            if (_health == null) return;
            CharacterMotorSettings s = _motor.Settings;
            if (SwimRules.DrownTick(ref _drownTimer, _health.IsAlive && _motor.State.Drowning, dt, s.drownInterval))
                _health.ApplyDamage(SwimRules.DrownHit(_health.Max, s.drownFraction, transform.position));
        }

        /// <summary>
        /// Server: Weltzellen-Regeln 6 und 7 (Docs/World/WorldStreaming.md). Betritt die Figur eine Zelle, die noch nicht
        /// geladen ist, wird sie an der letzten sicheren Stelle gehalten ("warten statt fallen"). Faellt sie trotzdem unter
        /// die Welt, kommt sie zum naechsten sicheren Anker ihrer Zelle – sonst wie bisher zum Wiederbelebungspunkt.
        /// </summary>
        private void GuardWorld()
        {
            Vector3 pos = transform.position;
            WorldStreamer ws = WorldStreamer.Instance;
            if (ws != null && !ws.IsWalkable(pos))
            {
                // Nach einem Teleport in eine noch ladende Zelle gibt es keine sichere Stelle: dort schweben lassen
                if (!_hasLastSafe)
                {
                    _lastSafe = pos;
                    _hasLastSafe = true;
                }
                _motor.Teleport(_lastSafe, transform.eulerAngles.y);
                if (Time.unscaledTime > _heldLogged)
                {
                    _heldLogged = Time.unscaledTime + 5f;
                    Debug.Log($"[Ashveil] Weltzellen: Figur gehalten – Zelle {ws.CellAt(pos)} ist auf dem Server noch nicht geladen.");
                }
                return;
            }
            if (Ashveil.Core.WorldGround.UnterDerWelt(pos, killHeight))
            {
                string cell = ws != null ? ws.CellAt(pos) : null;
                Ashveil.World.SafeSpawnAnchor anchor = cell != null ? Ashveil.World.SafeSpawnAnchor.Nearest(pos, cell) : null;
                if (anchor != null) _motor.Teleport(anchor.transform.position, anchor.transform.eulerAngles.y);
                else _motor.Teleport(RespawnPosition, RespawnYaw);
                _hasLastSafe = false;
                return;
            }
            if (_motor.State.Grounded)
            {
                _lastSafe = pos;
                _hasLastSafe = true;
            }
        }

        [Reconcile]
        private void Reconcile(StateData sd, Channel channel = Channel.Unreliable)
        {
            if (IsServerInitialized || !IsOwner) return;
            _reconciledThisTick = true;
            _motor.RestoreState(sd.State);
        }

        // ================================================================== Teleport

        /// <summary>Server: Spieler versetzen (Wiederbeleben, Tests). Der Besitzer folgt mit der naechsten Korrektur.</summary>
        public void ServerTeleport(Vector3 position, float yaw)
        {
            if (!IsServerInitialized) return;
            _motor.Teleport(position, yaw);
            _hasLastSafe = false;
            // Ziel in einer Zelle: vorladen (Regel 5 – Login/Teleport in eine Zelle)
            if (WorldStreamer.Instance != null) WorldStreamer.Instance.RequestPreload(position);
        }

        /// <summary>Server: zurueck zum Wachtfeuer.</summary>
        public void ServerTeleportToSpawn() => ServerTeleport(RespawnPosition, RespawnYaw);

        // Reihenfolge: Dungeon-Wiederbelebungspunkt, Bindepunkt der Szene (Kareth v4: Wachtfeuer, Start an der Ankunftsstelle), Spawn
        private Vector3 RespawnPosition =>
            _hasRespawnPoint ? _respawnPoint : Ashveil.World.BindPoint.Current != null ? Ashveil.World.BindPoint.Current.Position : _spawnPosition;
        private float RespawnYaw =>
            _hasRespawnPoint ? _respawnYaw : Ashveil.World.BindPoint.Current != null ? Ashveil.World.BindPoint.Current.Yaw : _spawnYaw;

        /// <summary>Server: eigener Wiederbelebungspunkt (z. B. Foerderhalle der Dungeon-Instanz).</summary>
        public void ServerSetRespawnPoint(Vector3 position, float yaw)
        {
            _hasRespawnPoint = true;
            _respawnPoint = position;
            _respawnYaw = yaw;
        }

        /// <summary>Server: wieder am Wachtfeuer wiederbeleben.</summary>
        public void ServerClearRespawnPoint() => _hasRespawnPoint = false;

        public bool HasRespawnPoint => _hasRespawnPoint;

        /// <summary>
        /// Test-Werkzeuge: Versetzen erbitten. Der Server erlaubt es nur fuer Test-Profile (selftest*, duo-*, fresh-*, social-*).
        /// Der Besitzer versetzt sich sofort selbst, damit er nicht auf die Antwort warten muss.
        /// </summary>
        public void RequestTeleport(Vector3 position, float yaw)
        {
            if (IsServerInitialized) { ServerTeleport(position, yaw); return; }
            if (!IsOwner) return;
            _motor.Teleport(position, yaw);
            // Bis der Server nachzieht, weicht der Client absichtlich ab – das ist keine Fehlvorhersage
            _ignoreCorrectionsUntil = Time.unscaledTime + 1f;
            ServerRequestTeleport(position, yaw);
        }

        [ServerRpc]
        private void ServerRequestTeleport(Vector3 position, float yaw)
        {
            if (_inv == null || string.IsNullOrEmpty(_inv.ProfileId) || !TestRechte.Erlaubt(_inv.ProfileId))
            {
                Debug.LogWarning($"[Ashveil] Teleport-Bitte von '{_inv?.ProfileId}' abgelehnt (kein Test-Profil).");
                return;
            }
            ServerTeleport(position, yaw);
        }
    }
}
