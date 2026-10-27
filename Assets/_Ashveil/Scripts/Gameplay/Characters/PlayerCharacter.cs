using Ashveil.Core.Events;
using Ashveil.Gameplay.CameraSystem;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Targeting;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// Kompositionswurzel des lokalen Spielers: verbindet Eingabe, Kamera und Motor
    /// in der richtigen Reihenfolge pro Frame:
    ///
    ///   1. Eingabe lesen
    ///   2. Kamera dreht (Maus)            -> liefert Blickrichtung
    ///   3. Bewegungsbefehl bauen & simulieren
    ///   4. Kamera positioniert sich (LateUpdate)
    ///
    /// Mit dem Netzwerk-Layer wird Schritt 3 in einen Netzwerk-Tick verlagert;
    /// die uebrigen Komponenten bleiben unveraendert.
    /// </summary>
    [RequireComponent(typeof(CharacterMotor), typeof(PlayerInputReader))]
    [DisallowMultipleComponent]
    public sealed class PlayerCharacter : MonoBehaviour
    {
        [SerializeField] private MMOCameraController cameraController;

        [Tooltip("Wie schnell sich der Charakter beim Angriff zum Ziel dreht (Grad/s)")]
        [SerializeField] private float softLockTurnSpeed = 720f;

        [Tooltip("Faellt der Spieler unter diese Hoehe, wird er zum Spawn zurueckgesetzt")]
        [SerializeField] private float killHeight = -60f;

        private CharacterMotor _motor;
        private PlayerInputReader _input;
        private TargetingSystem _targeting;
        private WeaponUser _weapons;
        private Combat.Health _health;
        private Camera _camera;
        private Vector3 _spawnPosition;
        private float _spawnYaw;

        public CharacterMotor Motor => _motor;

        /// <summary>
        /// true = die Bewegung laeuft im Netz-Takt (Server-Vorhersage): dieses Skript sammelt nur noch
        /// Befehle, <see cref="ConsumeCommand"/> holt sie pro Takt ab.
        /// </summary>
        public bool NetworkDriven { get; set; }
        private MoveCommand _pending;

        /// <summary>Netz-Takt: gesammelten Befehl abholen (Einmal-Aktionen wie Sprung werden danach geloescht).</summary>
        public MoveCommand ConsumeCommand()
        {
            MoveCommand c = _pending;
            _pending.Jump = false;
            _pending.Dodge = false;
            return c;
        }

        private void Queue(MoveCommand cmd)
        {
            bool jump = _pending.Jump || cmd.Jump, dodge = _pending.Dodge || cmd.Dodge;
            _pending = cmd;
            _pending.Jump = jump;
            _pending.Dodge = dodge;
        }

        /// <summary>Kamera folgt der geglaetteten Grafik, falls vorhanden (Netz-Takt ist grober als die Bildrate).</summary>
        private Transform CameraAnchor
        {
            get
            {
                Transform g = transform.Find("Graphics");
                return g != null ? g : transform;
            }
        }
        public PlayerInputReader Input => _input;
        public MMOCameraController CameraController => cameraController;
        public TargetingSystem Targeting => _targeting;
        public WeaponUser Weapons => _weapons;

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor>();
            _input = GetComponent<PlayerInputReader>();
            _targeting = GetComponent<TargetingSystem>();
            _weapons = GetComponent<WeaponUser>();
            _health = GetComponent<Combat.Health>();
            _spawnPosition = transform.position;
            _spawnYaw = transform.eulerAngles.y;
        }

        private void OnEnable()
        {
            if (_motor != null) _motor.Dodged += OnDodged;
        }

        private void OnDisable()
        {
            if (_motor != null) _motor.Dodged -= OnDodged;
            UiFensterstapel.HatZiel = false;
        }

        /// <summary>
        /// Hechtrolle: bewusst OHNE Unverwundbarkeit – sie rettet nur, wenn man rechtzeitig aus dem
        /// Wirkungsbereich eines Angriffs heraus ist. Leichtes Kamera-Zucken und Luftzug-Klang.
        /// </summary>
        private void OnDodged(Vector3 direction)
        {
            EventBus<CameraImpulseEvent>.Raise(new CameraImpulseEvent(transform.position, 0.12f, 5f));
            Audio.SfxPlayer.Play(Audio.SfxId.SwingHeavy, transform.position + Vector3.up, 0.5f, 0.1f);
        }

        private void Start()
        {
            if (cameraController == null)
                cameraController = FindAnyObjectByType<MMOCameraController>();

            if (cameraController != null)
            {
                cameraController.SetTarget(CameraAnchor, _motor);
                _camera = cameraController.GetComponent<Camera>();
            }
            else Debug.LogWarning("[PlayerCharacter] Keine MMOCameraController-Kamera in der Szene gefunden.", this);

            EventBus<LocalPlayerSpawnedEvent>.Raise(new LocalPlayerSpawnedEvent(gameObject));
        }

        private void Update()
        {
            // Esc-Regel (UI-204): mit Ziel hebt Esc erst das Ziel auf, ohne Ziel oeffnet es das Spielmenue.
            // EINSTIEG-05 (21.10.): nur lebend – im Tod laeuft _targeting.Tick nicht, das Ziel bliebe stehen und Esc
            // wuerde bei jedem Druck "Ziel aufheben" waehlen, das Spielmenue kaeme nie.
            UiFensterstapel.HatZiel = _targeting != null && _targeting.Current != null && (_health == null || _health.IsAlive);
            _input.Poll();

            float cameraYaw = transform.eulerAngles.y;
            if (cameraController != null)
            {
                cameraController.HandleLook(_input.LookDelta, _input.IsMouseLookActive, _input.ZoomSteps);
                cameraYaw = cameraController.Yaw;
            }

            float dt = Time.deltaTime;

            // Gefallen: keine Steuerung, nur Schwerkraft (Wiederbelebung kommt vom Server)
            if (_health != null && !_health.IsAlive)
            {
                _input.AutolaufAus(); // EINSTIEG-05 (21.10.): nach dem Erwachen nicht von selbst weiterlaufen
                if (NetworkDriven) _pending = default;
                else _motor.Simulate(default(MoveCommand), dt);
                return;
            }

            // Kampf: Zielwahl und Waffen (vor der Bewegung, damit Soft-Lock im selben Frame greift)
            CombatInput combat = _input.Combat;
            if (_targeting != null)
                _targeting.Tick(combat.TargetNext, combat.ClearTarget, combat.ClickSelect, combat.ClickPosition, _camera);
                if (combat.NextWeakPoint) _targeting.CycleWeakPoint(_camera);
            MoveCommand cmd = _input.BuildCommand(cameraYaw);
            // Tauchwinkel im Wasser (MOV-169): nur beim Steuern mit der rechten Maustaste (wie in WoW) – sonst schwimmt man
            // waagerecht, auch wenn der Soft-Lock die Figur zum Ziel dreht.
            cmd.SwimPitch = _input.IsSteering && cameraController != null ? cameraController.Pitch : 0f;

            // Soft-Lock nur, wenn der Spieler nicht selbst laeuft/dreht – oder waehrend eines laufenden Schlags.
            bool playerSteers = Mathf.Abs(cmd.Forward) > 0.01f || Mathf.Abs(cmd.Turn) > 0.01f;
            if (_weapons != null)
            {
                _weapons.SetSoftLockAllowed(!playerSteers || _weapons.IsInAttackMotion);
                _weapons.Tick(combat, dt, _camera, _input.IsSteering);
            }

            // Soft-Lock: Beim Angreifen dreht sich der Charakter zum Ziel (als normaler Bewegungsbefehl,
            // damit es spaeter genauso vom Server nachvollzogen wird).
            bool softLock = _weapons != null && _weapons.WantsFacing && (!playerSteers || _weapons.IsInAttackMotion);
            if (softLock && !cmd.SteerWithCamera)
            {
                cmd.SteerWithCamera = true;
                cmd.DesiredYaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, _weapons.FacingYaw, softLockTurnSpeed * dt);
                cmd.Turn = 0f;
            }

            if (_weapons != null) cmd.Plunge = _weapons.WantsPlunge;
            if (NetworkDriven)
            {
                Queue(cmd); // Server-Vorhersage: simuliert wird im Netz-Takt (NetworkMotion)
                return;
            }
            _motor.Simulate(cmd, dt);
            TickDrowningOffline(dt);

            if (Ashveil.Core.WorldGround.UnterDerWelt(transform.position, killHeight)) Respawn();
        }

        // Ertrinken ohne Netz (Offline-Test, MOV-169). Im Netz teilt der Server den Schaden aus (NetworkMotion).
        private float _drownTimer;

        private void TickDrowningOffline(float dt)
        {
            if (_health == null) return;
            CharacterMotorSettings s = _motor.Settings;
            if (SwimRules.DrownTick(ref _drownTimer, _health.IsAlive && _motor.State.Drowning, dt, s.drownInterval))
                _health.ApplyDamage(SwimRules.DrownHit(_health.Max, s.drownFraction, transform.position));
        }

        /// <summary>Zurueck zum Spawnpunkt (spaeter: naechster Friedhof/Wachtfeuer).</summary>
        public void Respawn()
        {
            if (!NetworkDriven) _motor.Teleport(_spawnPosition, _spawnYaw); // im Netz versetzt der Server
            _pending = default;
            if (cameraController != null)
            {
                cameraController.SetTarget(CameraAnchor, _motor);
                _camera = cameraController.GetComponent<Camera>();
            }
        }
    }
}
