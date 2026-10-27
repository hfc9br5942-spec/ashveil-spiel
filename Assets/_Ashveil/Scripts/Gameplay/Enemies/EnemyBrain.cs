using System;
using System.Collections.Generic;
using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>Zustand der Gegner-KI.</summary>
    public enum EnemyState : byte
    {
        Idle,
        Wander,
        Chase,
        Attack,
        Flee,
        Return,
        Dead
    }

    /// <summary>Sichtbarkeit eines Gegners (Tarnung des Russluchses, Abtauchen des Nadelspeiers).</summary>
    public enum EnemyConcealment : byte
    {
        None,
        /// <summary>Getarnt: unsichtbar, nicht anvisierbar, bis jemand zu nah kommt.</summary>
        Camouflaged,
        /// <summary>Abgetaucht: unsichtbar und unverwundbar, taucht woanders wieder auf.</summary>
        Submerged
    }

    /// <summary>
    /// Schlanke Zustandsmaschine eines Gegners – laeuft NUR auf dem Server (Clients sehen Position,
    /// Leben und Angriffe gespiegelt):
    ///
    ///   Ruhe/Streifen/Route ─(Spieler im Aggro-Radius oder Treffer)→ Verfolgen ⇄ Angriff
    ///        ↑                                                          │
    ///        └──── Rueckkehr (heilt, unverwundbar) ←─ Leine ────────────┤
    ///                                                          Flucht ←─┘ (bei wenig Leben, selten: 1 von 10 Kämpfen)
    ///   Tod → Leiche → Wiederkehr (steuert der EnemyDirector)
    ///
    /// Sonderverhalten aus der <see cref="EnemyDefinition"/>: Stampfer mit Flaechenschaden und Warnring,
    /// Gift, Abtauchen wenn bedraengt, Tarnung + Anspringen von Einzelgaengern, feste Route mit Leine
    /// zur Route (Glockenhirte). Denkt mit 10 Hz; Bewegung ueber NavMeshAgent.
    /// </summary>
    [RequireComponent(typeof(Health))]
    [DisallowMultipleComponent]
    public sealed class EnemyBrain : MonoBehaviour, IDodger
    {
        private static readonly List<EnemyBrain> AllBrains = new List<EnemyBrain>();
        private static readonly Collider[] AreaBuffer = new Collider[32];
        private static readonly Collider[] TargetBuffer = new Collider[16];
        private static readonly Collider[] WakeBuffer = new Collider[2];
        private static int _playerMask = -1;
        private static int PlayerMask => _playerMask >= 0 ? _playerMask : (_playerMask = LayerMask.GetMask(GameLayers.Player));

        /// <summary>
        /// Ruhe ohne Zuschauer (CORE-005, Weltfülle 10.10.2026): Steht kein Spieler näher als <see cref="WakeRadius"/>, denkt ein
        /// ruhender oder streifender Gegner nicht (keine Wahrnehmung, kein Streifen) und prüft nur alle 1,5–2,5 s, ob jemand kommt.
        /// Kämpfe, Rückkehr, Routen und Arenen sind ausgenommen. So kosten Hunderte Gegner in einer Gegend ohne Spieler fast nichts.
        /// </summary>
        public const float WakeRadius = 110f;
        private float _wakeCheckAt;
        private bool _awake = true;

        [SerializeField] private EnemyDefinition definition;
        [Tooltip("Hoehe der Augen/Muendung ueber dem Boden (Sichtlinie, Schuesse)")]
        [SerializeField] private float eyeHeight = 1.5f;

        private const float ThinkInterval = 0.1f;
        private const float PounceCooldown = 9f;
        private const float LonerRadius = 15f;

        // Flucht bei wenig Leben (Max 07.10.2026: „Gegner, die weglaufen, selten, langsamer und nicht so weit“): einmal je Kampf
        // gewürfelt, nur in 1 von 10 Kämpfen; dann 70 % Lauftempo, etwa 6 m, nach 2 s zurück in den Kampf. Beutetiere, die vor
        // Angreifern ausweichen (fleesWhenAttacked), laufen weiter wie bisher (14 m, 110 %, 3,5 s).
        private const float LowHealthFleeChance = 0.1f;
        private const float LowHealthFleeDistance = 6f;
        private const float LowHealthFleeSpeed = 0.7f;
        private const float LowHealthFleeSeconds = 2f;

        // Seitschritt (Max 07.10.2026, Aschwolf: „kurze Schritte zur Seite“): nach einem Angriff höchstens einmal, solange die
        // Abklingzeit noch läuft. Ein kurzer Bogen um das Ziel (der Abstand bleibt), der Blick bleibt beim Ziel – kein Kreisen.
        private const float SidestepMinCooldown = 0.75f;
        private const float SidestepSeconds = 0.5f;
        private const float SidestepSpeed = 0.5f;

        private Health _health;
        private NavMeshAgent _agent;
        private CombatTarget _combatTarget;
        private bool _active;
        private float _thinkTimer;

        private Health _target;
        private float _attackCooldown;
        private float _windupTimer = -1f;
        private AttackKind _pending;
        private int _attackCount;
        private float _wanderTimer;
        private float _fleeTimer;
        private float _sidestepUntil = -1f;
        private bool _sidestepReady;
        private bool _hasFled;
        private bool _howled;
        /// <summary>Wer diesen Gegner verletzt hat – nur diese duerfen Defensive/Passive-Gegner als Ziel haben.</summary>
        private readonly System.Collections.Generic.List<Health> _aggressors = new System.Collections.Generic.List<Health>();

        /// <summary>Wirksames Aggro-Profil (Gebiet kann den Wert des Gegnertyps ueberschreiben).</summary>
        public AggroProfile Aggro { get; private set; } = AggroProfile.Aggressive;

        /// <summary>Vor dem Spawnen: Aggro-Profil setzen (sonst gilt das des Gegnertyps). Neutrale Tiere werden dabei gelb oder rot.</summary>
        public void SetAggroProfile(AggroProfile profile)
        {
            Faction before = Faction;
            Aggro = profile;
            if (_combatTarget != null) _combatTarget.SetFaction(Faction);
            if (Faction != before) FactionChanged?.Invoke();
        }

        /// <summary>#28: Zugehörigkeit hat sich geändert (neutrales Tier wird durch Gebiet oder Arena feindlich) – Netz und Namensschild.</summary>
        public event Action FactionChanged;

        /// <summary>
        /// Zugehörigkeit für Namensschild, Zielrahmen und Aktionsleiste (#28, wie in Classic): neutrale Tiere, die nicht von selbst
        /// angreifen, sind gelb (<see cref="EnemyDefinition.ShowsNeutral"/>), alle anderen Gegner rot. Angreifbar sind beide; wie ein
        /// Gegner kämpft, hängt nur am Aggro-Profil.
        /// </summary>
        public Faction Faction => definition != null && !_arena && definition.ShowsNeutral(Aggro) ? Faction.Neutral : Faction.Hostile;

        // Kolosseum: Arena-Modus (keine Leine, keine Rueckkehr mit Vollheilung, Wahrnehmung = ganze Arena)
        private bool _arena;
        private Vector3 _arenaCenter;
        private float _arenaRadius;

        public bool InArena => _arena;

        /// <summary>
        /// Server, nach <see cref="Configure"/> und vor dem Spawnen: Gegner gehoert zu einer Arena. Er kennt jeden Spieler
        /// in der Arena (ohne Sichtlinie), laeuft nie heim und heilt sich nicht an einer Leine – er kaempft, bis er faellt.
        /// Ohne Ziel sammelt er sich in der Mitte.
        /// </summary>
        public void ServerSetArenaMode(Vector3 center, float radius)
        {
            _arena = true;
            _arenaCenter = center;
            _arenaRadius = radius;
            Faction before = Faction;
            Aggro = AggroProfile.Aggressive;
            if (_combatTarget != null) _combatTarget.SetFaction(Faction);
            if (Faction != before) FactionChanged?.Invoke();
            Home = center;
        }

        // Route
        private EnemyRoute _route;
        private int _routeIndex;
        private float _routePause;

        // Abtauchen / Tarnung / Sprung
        private float _pressedTimer;
        private float _diveTimer = -1f;
        private bool _diveMoved;
        private float _pounceCooldown;
        private float _throwCooldown;

        private enum AttackKind : byte { None, Single, Area, Pounce, LeapAt, Throw }

        // Combat-Masterplan: Moveset und Koerperteile (nur Gegner, deren Definition sie hat)
        private EnemyMovePlanner _planner;
        private EnemyAttackDefinition _move;
        private EnemyAnatomy _anatomy;
        private float _stunUntil = -1f;

        /// <summary>Tempo-Faktor (gebrochenes Bein: langsamer).</summary>
        public float SpeedFactor { get; private set; } = 1f;
        /// <summary>Verlangsamung durch Faehigkeiten (Donnerschlag, Zurechtstutzen) – wirkt zusaetzlich zu <see cref="SpeedFactor"/>.</summary>
        public float SlowFactor { get; private set; } = 1f;

        // P1 Bedrohung (WoW Classic): wer am meisten Bedrohung erzeugt, wird angegriffen – gewechselt wird erst,
        // wenn jemand das aktuelle Ziel um 10 % (Nahkampf) bzw. 30 % (auf Entfernung) uebertrifft. Spott erzwingt.
        private readonly Dictionary<Health, float> _threat = new Dictionary<Health, float>();
        private Health _taunter;
        private float _tauntUntil = -1f;
        /// <summary>Server (Tests/Anzeige): Bedrohung eines Angreifers.</summary>
        public float ThreatOf(Health h) => h != null && _threat.TryGetValue(h, out float v) ? v : 0f;
        /// <summary>Server: aktuelles Ziel.</summary>
        public Health CurrentTarget => _target;
        /// <summary>Server: laeuft ein Spott?</summary>
        public bool IsTaunted => _taunter != null && Time.time < _tauntUntil;
        /// <summary>Server: kniet/taumelt gerade (keine Angriffe, keine Bewegung).</summary>
        public bool IsStunned => Time.time < _stunUntil;
        public EnemyAnatomy Anatomy => _anatomy;
        public EnemyMovePlanner Planner => _planner;
        /// <summary>Server: zuletzt begonnener Angriff aus dem Moveset (Id, 0 = keiner).</summary>
        public byte LastMoveId { get; private set; }
        /// <summary>Server (Tests): wie oft jeder Angriff begonnen wurde.</summary>
        public readonly Dictionary<byte, int> MoveCounts = new Dictionary<byte, int>();
        /// <summary>Server (Tests): die letzten 64 begonnenen Angriffe in Reihenfolge.</summary>
        public readonly List<byte> MoveHistory = new List<byte>();

        public EnemyDefinition Definition => definition;
        public int Level { get; private set; } = 1;
        public Vector3 Home { get; private set; }
        public string Group { get; private set; } = "";
        public EnemyState State { get; private set; } = EnemyState.Idle;
        public EnemyConcealment Concealment { get; private set; } = EnemyConcealment.None;
        public EnemyRoute Route => _route;
        public int RouteIndex => _routeIndex;
        public bool IsHerding => _route != null && _routePause > 0f && (State == EnemyState.Idle || State == EnemyState.Wander);
        public Health Target => _target;
        public bool IsActive => _active;

        // Diagnose 21.10.2026 (Selbsttest Gehöft: sporadisch keine Wahrnehmung) – nur lesen, ändert keinen Zustand.
        /// <summary>Augenhöhe für die Sichtlinie (wie <see cref="HasLineOfSight"/>).</summary>
        public float EyeHeight => eyeHeight;
        /// <summary>Ergebnis der letzten Zuschauer-Prüfung (<see cref="WakeRadius"/>); false = denkt gerade nicht.</summary>
        public bool IsAwake => _awake;
        /// <summary>Radius der eigenen Wahrnehmung je Aggro-Profil, wie in <see cref="PerceiveTarget"/> (0 = nimmt niemanden von sich aus wahr).</summary>
        public float PerceptionRadius
        {
            get
            {
                if (definition == null) return 0f;
                if (_arena) return _arenaRadius + 10f;
                switch (Aggro)
                {
                    case AggroProfile.Aggressive:
                    case AggroProfile.Encounter: return definition.aggroRadius;
                    case AggroProfile.Territorial: return definition.territoryRadius;
                    default: return 0f;
                }
            }
        }
        /// <summary>Spieler-Layermaske, wie sie die Wahrnehmung benutzt (statisch zwischengespeichert).</summary>
        public static int PlayerLayerMask => PlayerMask;
        /// <summary>Layermaske der Sichtlinie (wie <see cref="HasLineOfSight"/>).</summary>
        public static int SightBlockMask => GameLayers.CameraCollisionMask() & ~LayerMask.GetMask(GameLayers.Enemy);
        /// <summary>Server: was die eigene Wahrnehmung jetzt fände (dieselbe Prüfung wie im Denkschritt, ohne Zustandswechsel).</summary>
        public Health ProbePerception() => _active && definition != null ? PerceiveTarget() : null;

        public Health Health => _health;
        public float HealthFraction => _health != null ? _health.Fraction : 0f;
        /// <summary>Im Kampf (verfolgt oder greift an)?</summary>
        public bool InCombat => State == EnemyState.Chase || State == EnemyState.Attack;
        /// <summary>Server: noch nicht explodierte Ladungen.</summary>
        public int PendingStrikes => _strikes.Count;

        /// <summary>Zustand gewechselt (Server).</summary>
        public event Action<EnemyState> StateChanged;
        /// <summary>Angriff angekuendigt: Zielpunkt, Ausholzeit (s), Fernkampf? (Server)</summary>
        public event Action<Vector3, float, bool> AttackBegan;
        /// <summary>Angriff aus dem Moveset beginnt: Id, Ausholzeit (s) (Server) – kommt vor AttackBegan/AreaAttackBegan.</summary>
        public event Action<byte, float> MoveBegan;
        /// <summary>Stampfer angekuendigt: Mitte, Warnzeit (s), Radius (Server)</summary>
        public event Action<Vector3, float, float> AreaAttackBegan;
        /// <summary>Angriff ausgeloest: getroffen?, Punkt (Server)</summary>
        public event Action<bool, Vector3> AttackResolved;
        /// <summary>Sprung: von, nach (Server)</summary>
        public event Action<Vector3, Vector3> Pounced;
        /// <summary>Ruft das Rudel (Server)</summary>
        public event Action Howled;
        /// <summary>Tarnung/Abtauchen geaendert (Server)</summary>
        public event Action<EnemyConcealment> ConcealmentChanged;
        /// <summary>Gestorben (Server)</summary>
        public event Action<EnemyBrain> Killed;
        /// <summary>Ferngezuendete Ladung (Boss-Faehigkeit) explodiert: Mitte, Radius, jemand getroffen? (Server)</summary>
        public event Action<Vector3, float, bool> StrikeExploded;

        // Ferngezuendete Flaechenangriffe (Sprengladungen des Vorarbeiters)
        private struct PendingStrike
        {
            public Vector3 Center;
            public float Radius;
            public float Due;
            public float Damage;
            public string WeaponId;
        }

        private readonly List<PendingStrike> _strikes = new List<PendingStrike>();

        public static IReadOnlyList<EnemyBrain> All => AllBrains;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _agent = GetComponent<NavMeshAgent>();
            _combatTarget = GetComponent<CombatTarget>();
        }

        private void OnEnable()
        {
            if (!AllBrains.Contains(this)) AllBrains.Add(this);
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;
        }

        private void OnDisable()
        {
            AllBrains.Remove(this);
            _health.Damaged -= OnDamaged;
            _health.Died -= OnDied;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => AllBrains.Clear();

        // ================================================================== Einrichtung (Server)

        /// <summary>Vor dem Spawnen: Stufe, Heimat, Gruppe festlegen und Werte setzen.</summary>
        public void Configure(int level, Vector3 home, string group)
        {
            Level = Mathf.Max(1, level);
            Home = home;
            Group = group ?? "";
            if (definition == null) return;
            Aggro = definition.aggroProfile;
            _health.Configure(definition.HealthAt(Level), false);
            if (_combatTarget != null)
                _combatTarget.Configure(definition.DisplayName, Level, Faction, _combatTarget.AimPoint - transform.position,
                    _combatTarget.SelectionRadius, definition.elite, definition.levelLabel);
            _anatomy = EnemyAnatomy.Ensure(gameObject, definition, true);
            _planner = definition.HasMoveset ? new EnemyMovePlanner(definition) : null;
            SpeedFactor = 1f;
            ClearThreat();
        }

        /// <summary>Vor dem Spawnen: feste Wanderroute (Glockenhirte).</summary>
        public void SetRoute(EnemyRoute route)
        {
            _route = route != null && route.Count >= 2 ? route : null;
            if (_route != null)
            {
                _route.ClosestPoint(transform.position, out _routeIndex);
                Home = _route.ClosestPoint(transform.position, out _);
            }
        }

        /// <summary>Server: KI starten (NavMesh muss vorhanden sein).</summary>
        public void Activate()
        {
            if (definition == null)
            {
                Debug.LogWarning("[Ashveil] Gegner ohne Definition: " + name, this);
                return;
            }
            _active = true;
            if (_agent != null)
            {
                _agent.enabled = true;
                if (!_agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                    _agent.Warp(hit.position);
                _agent.speed = definition.walkSpeed;
                _agent.stoppingDistance = 0.2f;
            }
            _wanderTimer = UnityEngine.Random.Range(1f, 5f);
            SetState(EnemyState.Idle);
            SetConcealment(definition.camouflaged ? EnemyConcealment.Camouflaged : EnemyConcealment.None);
        }

        /// <summary>Clients: KI bleibt aus, nur Darstellung.</summary>
        public void DisableForClient()
        {
            _active = false;
            if (_agent != null) _agent.enabled = false;
            enabled = true; // Registry/Events bleiben fuer Darstellung
        }

        // ================================================================== Takt

        private void Update()
        {
            if (!_active || State == EnemyState.Dead) return;
            float dt = Time.deltaTime;
            _attackCooldown -= dt;
            _pounceCooldown -= dt;
            _throwCooldown -= dt;
            if (_strikes.Count > 0) UpdateStrikes();
            if (Time.time < _stunUntil) return; // kniet oder taumelt

            if (_windupTimer >= 0f)
            {
                if (_pending == AttackKind.LeapAt) FacePoint(_leapPoint, dt, 720f);
                else if (_pending != AttackKind.Area) FaceTarget(dt, 720f);
                _windupTimer -= dt;
                if (_windupTimer < 0f) ResolveAttack();
            }
            else if (State == EnemyState.Chase || State == EnemyState.Attack)
            {
                if (IsStopped || Time.time < _sidestepUntil) FaceTarget(dt, 360f);
            }

            _thinkTimer -= dt;
            if (_thinkTimer > 0f) return;
            _thinkTimer = ThinkInterval;
            Think();
        }

        private void Think()
        {
            if (_diveTimer >= 0f)
            {
                ThinkDive();
                return;
            }

            switch (State)
            {
                case EnemyState.Idle:
                case EnemyState.Wander:
                    if (!HatZuschauer()) break;
                    Health found = PerceiveTarget();
                    if (found != null) { Engage(found); return; }
                    if (_route != null) ThinkRoute();
                    else ThinkWander();
                    break;

                case EnemyState.Chase:
                case EnemyState.Attack:
                    ThinkCombat();
                    break;

                case EnemyState.Flee:
                    _fleeTimer -= ThinkInterval;
                    if (_fleeTimer <= 0f)
                    {
                        if (!FleesFromAttackers && IsValidTarget(_target) && !IsBeyondLeash()) SetState(EnemyState.Chase);
                        else BeginReturn();
                    }
                    break;

                case EnemyState.Return:
                    _health.SetInvulnerableFor(0.3f);
                    if (HorizontalDistance(transform.position, Home) < 2f || (_agent != null && !_agent.pathPending && _agent.remainingDistance < 1.5f))
                    {
                        _health.ResetToFull();
                        _hasFled = false;
                        _howled = false;
                        _aggressors.Clear();
                        ClearThreat();
                        _attackCount = 0;
                        SpeedFactor = 1f;
                        _planner?.Reset();
                        if (definition.camouflaged) SetConcealment(EnemyConcealment.Camouflaged);
                        SetState(EnemyState.Idle);
                    }
                    break;
            }
        }

        private bool HatZuschauer()
        {
            if (_route != null || _arena) return true;
            if (Time.time < _wakeCheckAt) return _awake;
            _awake = Physics.OverlapSphereNonAlloc(transform.position, WakeRadius, WakeBuffer, PlayerMask, QueryTriggerInteraction.Ignore) > 0;
            _wakeCheckAt = Time.time + (_awake ? 1f : UnityEngine.Random.Range(1.5f, 2.5f));
            if (!_awake && _agent != null && _agent.isOnNavMesh && !_agent.isStopped && State == EnemyState.Wander) { _agent.ResetPath(); SetState(EnemyState.Idle); }
            return _awake;
        }

        private void ThinkWander()
        {
            if (_arena && _wanderTimer > 0.6f) _wanderTimer = 0.6f; // aus dem Torgang sofort in den Sand
            _wanderTimer -= ThinkInterval;
            if (_wanderTimer > 0f) return;
            _wanderTimer = UnityEngine.Random.Range(4f, 9f);
            float wanderRadius = _arena ? Mathf.Max(8f, definition.wanderRadius) : definition.wanderRadius;
            if (wanderRadius <= 0.1f) return;

            Vector2 r = UnityEngine.Random.insideUnitCircle * wanderRadius;
            Vector3 p = Home + new Vector3(r.x, 0f, r.y);
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                MoveTo(hit.position, definition.walkSpeed);
                SetState(EnemyState.Wander);
            }
        }

        /// <summary>Feste Route abschreiten, an Huete-Punkten stehen bleiben.</summary>
        private void ThinkRoute()
        {
            if (_routePause > 0f)
            {
                _routePause -= ThinkInterval;
                if (_routePause <= 0f) _routeIndex = (_routeIndex + 1) % _route.Count;
                return;
            }
            Vector3 goal = _route.Point(_routeIndex);
            Home = goal;
            if (HorizontalDistance(transform.position, goal) < 3f)
            {
                if (_route.IsPausePoint(_routeIndex))
                {
                    _routePause = _route.PauseSeconds;
                    Stop();
                    SetState(EnemyState.Idle);
                    return;
                }
                _routeIndex = (_routeIndex + 1) % _route.Count;
                goal = _route.Point(_routeIndex);
            }
            if (_agent != null && (!_agent.hasPath || Vector3.Distance(_agent.destination, goal) > 2f || _agent.isStopped))
                MoveTo(goal, definition.walkSpeed);
            SetState(EnemyState.Wander);
        }

        private void ThinkCombat()
        {
            // Ziel ungültig oder weit außerhalb des eigenen Gebiets (weggesprungen, Instanz, Wiederbeleben; Max 09.10.2026)
            if (!IsValidTarget(_target) || !ImGebiet(_target))
            {
                // Wer hat mich sonst noch angegriffen? Danach nur die eigene Wahrnehmung – sonst heim.
                Health next = NearestAggressor() ?? PerceiveTarget();
                if (next != null && next != _target && !FleesFromAttackers) { _target = next; return; }
                BeginReturn();
                return;
            }
            if (IsBeyondLeash()) { BeginReturn(); return; }

            if (definition.fleeHealthFraction > 0f && !_hasFled && _health.Fraction < definition.fleeHealthFraction)
            {
                _hasFled = true; // einmal je Kampf würfeln
                if (UnityEngine.Random.value < LowHealthFleeChance)
                {
                    BeginFlee(LowHealthFleeDistance, LowHealthFleeSpeed, LowHealthFleeSeconds);
                    return;
                }
            }

            if (_windupTimer >= 0f) return; // holt gerade aus
            UpdateThreatTarget();

            Vector3 targetPos = _target.transform.position;
            float dist = HorizontalDistance(transform.position, targetPos);

            // #28 Schlickwurf: ein Nahkämpfer wirft ab und zu, wenn sein Ziel auf Abstand ist (sichtbares Ausholen, nur Schaden)
            if (definition.style == EnemyStyle.Melee && TryBeginThrow(dist)) return;

            if (definition.style == EnemyStyle.Ranged)
            {
                bool los = HasLineOfSight(_target);
                if (dist < definition.tooCloseDistance)
                {
                    // Bedraengt: erst zurueckweichen, bei Dauerdruck abtauchen (Nadelspeier)
                    _pressedTimer += ThinkInterval;
                    if (definition.diveWhenPressedFor > 0f && _pressedTimer >= definition.diveWhenPressedFor)
                    {
                        BeginDive();
                        return;
                    }
                    Vector3 away = (transform.position - targetPos);
                    away.y = 0f;
                    Vector3 p = transform.position + away.normalized * 4f;
                    // KAMPF-02 (21.10.): Zurueckweichen nur innerhalb von 80 % der Leine (wie das Abtauchen, ThinkDive) und mit
                    // derselben Abstandsrechnung wie IsBeyondLeash (Route/Heimpunkt, Arena ohne Leine). Vorher wich die Schuetzin
                    // einem Nahkaempfer endlos zurueck, schoss nie und setzte sich an der Leine mit vollem Leben zurueck.
                    float dHome = _route != null ? _route.DistanceTo(p) : HorizontalDistance(p, Home);
                    bool retreatOk = _arena || dHome <= definition.leashDistance * 0.8f;
                    if (retreatOk && NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                    {
                        MoveTo(hit.position, definition.runSpeed * 0.8f);
                        SetState(EnemyState.Chase);
                    }
                    else
                    {
                        // Eingekeilt: stehen bleiben und aus kurzer Distanz schiessen
                        Stop();
                        if (_attackCooldown <= 0f && los && dist <= definition.attackRange) BeginAttack();
                        else SetState(EnemyState.Chase);
                    }
                }
                else
                {
                    _pressedTimer = Mathf.Max(0f, _pressedTimer - ThinkInterval * 0.5f);
                    if (dist > definition.preferredDistance + 2f || !los)
                    {
                        MoveTo(targetPos, definition.runSpeed);
                        SetState(EnemyState.Chase);
                    }
                    else
                    {
                        Stop();
                        if (_attackCooldown <= 0f && los && dist <= definition.attackRange) BeginAttack();
                        else SetState(EnemyState.Chase);
                    }
                }
            }
            else if (_planner != null)
            {
                ThinkMoves(dist, targetPos);
            }
            else
            {
                // Anspringen aus mittlerer Entfernung (Russluchs)
                if (definition.pounceRange > 0f && _pounceCooldown <= 0f && dist > 4f && dist <= definition.pounceRange && HasLineOfSight(_target))
                {
                    BeginPounce();
                    return;
                }

                bool nextIsArea = NextAttackIsArea();
                float reach = nextIsArea ? Mathf.Max(definition.attackRange, definition.areaRadius * 0.7f) : definition.attackRange;
                if (dist > reach * 0.85f)
                {
                    MoveTo(targetPos, definition.runSpeed);
                    SetState(EnemyState.Chase);
                }
                else if (Time.time < _sidestepUntil)
                {
                    SetState(EnemyState.Chase); // tritt gerade zur Seite
                }
                else
                {
                    if (_sidestepReady && _attackCooldown > SidestepMinCooldown)
                    {
                        _sidestepReady = false; // höchstens ein Seitschritt je Angriff
                        if (definition.sidestepChance > 0f && UnityEngine.Random.value < definition.sidestepChance && BeginSidestep(targetPos, dist))
                            return;
                    }
                    Stop();
                    if (_attackCooldown <= 0f) BeginAttack();
                    else SetState(EnemyState.Chase);
                }
            }
        }

        // ================================================================== Kampf

        private void Engage(Health target)
        {
            if (!IsValidTarget(target) || State == EnemyState.Dead || State == EnemyState.Return) return;
            bool fresh = State == EnemyState.Idle || State == EnemyState.Wander;
            _target = target;
            _routePause = 0f;
            if (fresh)
            {
                _attackCooldown = Mathf.Max(_attackCooldown, 0.4f);
                _throwCooldown = Mathf.Max(_throwCooldown, 1.5f); // erst ansehen, dann werfen
            }
            if (Concealment == EnemyConcealment.Camouflaged) SetConcealment(EnemyConcealment.None);
            SetState(EnemyState.Chase);

            // Nur Ton und Darstellung – KEINE Social-Aggro: andere Gegner werden nicht alarmiert (Kanon 26.09.)
            if (definition.howlsOnEngage && !_howled)
            {
                _howled = true;
                Howled?.Invoke();
            }
        }

        /// <summary>Eigene Wahrnehmung je Aggro-Profil (Passive/Defensive nehmen niemanden von sich aus wahr).</summary>
        private Health PerceiveTarget()
        {
            if (_arena) return FindArenaTarget();
            switch (Aggro)
            {
                case AggroProfile.Aggressive:
                case AggroProfile.Encounter:
                    return FindTarget(definition.aggroRadius);
                case AggroProfile.Territorial:
                    return FindTarget(definition.territoryRadius);
                default:
                    return null;
            }
        }

        // ================================================================== Bedrohung (P1)

        /// <summary>Server: Bedrohung gutschreiben (Schaden, Heilung, Tank-Faehigkeiten). Wer noch nicht kaempft, greift an.</summary>
        public void ServerAddThreat(Health source, float amount)
        {
            if (!_active || State == EnemyState.Dead || State == EnemyState.Return || !IsValidTarget(source) || amount <= 0f) return;
            _threat[source] = ThreatOf(source) + amount;
            if (!_aggressors.Contains(source)) _aggressors.Add(source);
            if (FleesFromAttackers) return;
            if (State == EnemyState.Idle || State == EnemyState.Wander) Engage(source);
        }

        /// <summary>Server: Spott – greift <paramref name="taunter"/> sofort an und bleibt so lange bei ihm; danach hat er die hoechste Bedrohung.</summary>
        public void ServerTaunt(Health taunter, float seconds)
        {
            if (!_active || State == EnemyState.Dead || !IsValidTarget(taunter)) return;
            float top = 0f;
            foreach (var kv in _threat) if (kv.Value > top) top = kv.Value;
            _threat[taunter] = Mathf.Max(ThreatOf(taunter), top) + 1f;
            if (!_aggressors.Contains(taunter)) _aggressors.Add(taunter);
            _taunter = taunter;
            _tauntUntil = Time.time + Mathf.Max(0.5f, seconds);
            if (State == EnemyState.Idle || State == EnemyState.Wander || State == EnemyState.Return) Engage(taunter);
            else if (_windupTimer < 0f) _target = taunter;
        }

        /// <summary>Server: Verlangsamung setzen (1 = normal).</summary>
        public void ServerSetSlowFactor(float factor)
        {
            SlowFactor = Mathf.Clamp(factor, 0.2f, 1f);
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh && !_agent.isStopped && definition != null)
                _agent.speed = Mathf.Min(_agent.speed, definition.runSpeed * SpeedFactor * SlowFactor);
        }

        private void UpdateThreatTarget()
        {
            if (_arena && _threat.Count == 0) return;
            if (IsTaunted && IsValidTarget(_taunter) && ImGebiet(_taunter)) { _target = _taunter; return; }
            if (_threat.Count == 0) return;
            Health best = null;
            float bestT = 0f;
            List<Health> dead = null;
            foreach (var kv in _threat)
            {
                if (!IsValidTarget(kv.Key)) { (dead ??= new List<Health>()).Add(kv.Key); continue; }
                if (!ImGebiet(kv.Key)) continue;   // weit weg (Sprung, Instanz): nicht wählen, bleibt aber in der Liste
                if (kv.Value > bestT) { bestT = kv.Value; best = kv.Key; }
            }
            if (dead != null) foreach (Health h in dead) _threat.Remove(h);
            if (best == null || best == _target) return;
            float cur = ThreatOf(_target);
            float dist = HorizontalDistance(transform.position, best.transform.position);
            float need = dist <= definition.attackRange + 1.5f ? 1.1f : 1.3f;
            if (!IsValidTarget(_target) || !ImGebiet(_target) || bestT > cur * need) _target = best;
        }

        private void ClearThreat()
        {
            _threat.Clear();
            _taunter = null;
            _tauntUntil = -1f;
            SlowFactor = 1f;
        }

        private Health NearestAggressor()
        {
            Health best = null;
            float bestD = float.MaxValue;
            for (int i = _aggressors.Count - 1; i >= 0; i--)
            {
                Health h = _aggressors[i];
                if (!IsValidTarget(h)) { _aggressors.RemoveAt(i); continue; }
                if (!ImGebiet(h)) continue;
                float d = (h.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = h; }
            }
            return best;
        }

        private bool NextAttackIsArea() => definition.areaAttackEvery > 0 && (_attackCount + 1) % definition.areaAttackEvery == 0;

        private void BeginAttack()
        {
            SetState(EnemyState.Attack);
            _sidestepReady = true;
            _attackCount++;
            bool area = definition.areaAttackEvery > 0 && _attackCount % definition.areaAttackEvery == 0;
            if (area)
            {
                _pending = AttackKind.Area;
                _windupTimer = definition.areaWindup;
                _attackCooldown = definition.attackInterval + definition.areaWindup;
                AreaAttackBegan?.Invoke(transform.position, definition.areaWindup, definition.areaRadius);
                return;
            }
            _pending = AttackKind.Single;
            _windupTimer = definition.windup;
            _attackCooldown = definition.attackInterval + definition.windup;
            Vector3 aim = _target != null ? AimPoint(_target) : transform.position + transform.forward;
            AttackBegan?.Invoke(aim, definition.windup, definition.style == EnemyStyle.Ranged);
        }

        /// <summary>
        /// #28 Schlickwurf (Baustein 29, Schlickwerfer): Wurf auf ein Ziel zwischen Mindest- und Höchstweite mit Sichtlinie.
        /// Klar erkennbares Ausholen, begrenzte Reichweite, nur Schaden (kein Verlangsamen, keine Sichtblockade). Der Server rechnet
        /// den Treffer wie einen Fernschuss; wer ausweicht, wird nicht getroffen.
        /// </summary>
        private bool TryBeginThrow(float dist)
        {
            if (!definition.Throws || _throwCooldown > 0f || _attackCooldown > 0f || _target == null) return false;
            if (dist < definition.throwMinRange || dist > definition.throwRange || !HasLineOfSight(_target)) return false;
            SetState(EnemyState.Attack);
            Stop();
            _move = null;
            _pending = AttackKind.Throw;
            _windupTimer = definition.throwWindup;
            _throwCooldown = definition.throwCooldown;
            _attackCooldown = definition.attackInterval + definition.throwWindup;
            ThrowsStarted++;
            AttackBegan?.Invoke(AimPoint(_target), definition.throwWindup, true);
            return true;
        }

        /// <summary>Server (Tests): begonnene Würfe seit dem Erscheinen.</summary>
        public int ThrowsStarted { get; private set; }

        // ---------------------------------------------------------------- Moveset (Combat-Masterplan §102–§104)

        private bool PartBroken(byte id) => _anatomy != null && _anatomy.IsBroken(id);

        private float _retreatUntil = -1f;

        private void ThinkMoves(float dist, Vector3 targetPos)
        {
            if (Time.time < _retreatUntil) return; // tritt gerade zurueck
            if (_attackCooldown <= 0f)
            {
                EnemyAttackDefinition m = _planner.Pick(dist, Time.time, PartBroken, UnityEngine.Random.value);
                if (m != null)
                {
                    BeginMove(m, -1f);
                    return;
                }
            }
            float reach = _planner.ApproachDistance(PartBroken);
            if (dist > reach * 0.85f)
            {
                MoveTo(targetPos, definition.runSpeed);
                SetState(EnemyState.Chase);
            }
            else
            {
                Stop();
                SetState(EnemyState.Chase);
            }
        }

        private void BeginMove(EnemyAttackDefinition m, float windupOverride)
        {
            SetState(EnemyState.Attack);
            _attackCount++;
            _move = m;
            LastMoveId = m.id;
            MoveCounts[m.id] = MoveCounts.TryGetValue(m.id, out int n) ? n + 1 : 1;
            MoveHistory.Add(m.id);
            if (MoveHistory.Count > 64) MoveHistory.RemoveAt(0);
            _planner.MarkUsed(m, Time.time);
            float windup = windupOverride > 0f ? windupOverride : m.windup;
            _windupTimer = windup;
            _attackCooldown = definition.attackInterval + windup;
            MoveBegan?.Invoke(m.id, windup);
            switch (m.kind)
            {
                case EnemyMoveKind.Area:
                    Stop();
                    _pending = AttackKind.Area;
                    AreaAttackBegan?.Invoke(transform.position, windup, m.areaRadius > 0f ? m.areaRadius : definition.areaRadius);
                    break;
                case EnemyMoveKind.Charge:
                    // Ansturm: laeuft waehrend des Ausholens mit hohem Tempo auf das Ziel zu, trifft am Ende, wenn nah genug
                    _pending = AttackKind.Pounce;
                    if (_target != null) MoveTo(_target.transform.position, definition.runSpeed * 2.6f);
                    AttackBegan?.Invoke(_target != null ? AimPoint(_target) : transform.position + transform.forward, windup, false);
                    break;
                default:
                    Stop();
                    _pending = AttackKind.Single;
                    AttackBegan?.Invoke(_target != null ? AimPoint(_target) : transform.position + transform.forward, windup, false);
                    break;
            }
        }

        /// <summary>Nach einem Moveset-Angriff: herabbeugen, Folgeangriff, Abstand nehmen. true = Folgeangriff laeuft.</summary>
        private bool AfterMove(EnemyAttackDefinition m)
        {
            if (m == null) return false;
            if (m.lowersHeadFor > 0f && _anatomy != null) _anatomy.SetLowered(m.lowersHeadFor);
            if (m.followUp != 0 && IsValidTarget(_target))
            {
                EnemyAttackDefinition f = definition.Move(m.followUp);
                if (f != null && EnemyMovePlanner.Allowed(f, PartBroken))
                {
                    BeginMove(f, m.followUpWindup);
                    return true;
                }
            }
            if (m.retreatAfter > 0f && _target != null)
            {
                Vector3 away = transform.position - _target.transform.position;
                away.y = 0f;
                Vector3 p = transform.position + (away.sqrMagnitude > 1e-3f ? away.normalized : -transform.forward) * m.retreatAfter;
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    MoveTo(hit.position, definition.walkSpeed * 1.5f);
                    _retreatUntil = Time.time + Mathf.Min(2.5f, m.retreatAfter / Mathf.Max(0.5f, definition.walkSpeed * 1.5f));
                }
            }
            return false;
        }

        /// <summary>
        /// Server (Praxis-Meisterschaften §23): holt dieser Gegner gerade zu einem Angriff aus, der <paramref name="target"/>
        /// treffen wuerde? Nahkampf/Ansturm: nur das eigene Ziel in Reichweite; Stampfer: jeder im Radius.
        /// </summary>
        public bool Threatens(Health target, out float secondsLeft)
        {
            secondsLeft = 0f;
            if (!_active || target == null || _windupTimer < 0f || State == EnemyState.Dead || _pending == AttackKind.None || _pending == AttackKind.LeapAt) return false;
            secondsLeft = _windupTimer;
            float dist = HorizontalDistance(transform.position, target.transform.position);
            if (_pending == AttackKind.Area)
            {
                float r = _move != null && _move.areaRadius > 0f ? _move.areaRadius : definition.areaRadius;
                return dist <= r + 0.5f;
            }
            if (_target != target) return false;
            if (_pending == AttackKind.Throw) return dist <= definition.throwRange + 1f;
            if (definition.style == EnemyStyle.Ranged) return dist <= definition.attackRange + 1f;
            float range = _move != null ? (_move.kind == EnemyMoveKind.Charge ? _move.maxRange : _move.maxRange) : _pending == AttackKind.Pounce ? definition.pounceRange : definition.attackRange;
            return dist <= range * 1.15f + 1.3f;
        }

        /// <summary>Server: kniet oder taumelt fuer so viele Sekunden (bricht das Ausholen ab).</summary>
        public void ServerStun(float seconds)
        {
            // KAMPF-01 (21.10.): Heimkehrende sind nicht betaeubbar. Der Heimweg wird nur einmal angestossen (BeginReturn → MoveTo);
            // Stop() setzte isStopped, nichts startete den Agenten neu – der Gegner blieb fuer immer unverwundbar stehen.
            // Gilt fuer alle Aufrufer (Schildhieb/Ansturm, Anatomie, Dominieren, Pferdegeschichte). 23.10.2026: Der Abbruch des
            // Dominierens ruft hier nicht mehr ServerStun(0) (wirkte wegen Mathf.Max nie), sondern ServerStunEnde.
            if (!_active || State == EnemyState.Dead || State == EnemyState.Return) return;
            _stunUntil = Mathf.Max(_stunUntil, Time.time + seconds);
            _windupTimer = -1f;
            _pending = AttackKind.None;
            _move = null;
            Stop();
            _attackCooldown = Mathf.Max(_attackCooldown, 0.8f);
        }

        /// <summary>
        /// 23.10.2026 (STORY-05 Nachtrag): Server – eine selbst gesetzte Betäubung vorzeitig beenden (Dominieren abgebrochen: der Wille
        /// des Wesens ist wieder frei). <paramref name="eigeneBis"/> ist das Ende (Time.time), das der Aufrufer selbst über
        /// <see cref="ServerStun"/> gesetzt hat. Hat eine andere Quelle die Betäubung darüber hinaus verlängert (ServerStun nimmt das
        /// Maximum), bleibt sie bestehen. Grenze: Einzelenden werden nicht geführt – eine kürzere fremde Betäubung, die vollständig in
        /// der eigenen aufging, endet mit. Andere Aufrufer von ServerStun sind unverändert.
        /// </summary>
        public void ServerStunEnde(float eigeneBis)
        {
            if (!_active || State == EnemyState.Dead) return;
            if (_stunUntil > eigeneBis + 0.05f) return;   // eine andere Quelle betäubt länger → bleibt
            _stunUntil = -1f;   // wie nach natürlichem Ablauf: Update denkt im nächsten Takt wieder (Think setzt den Agenten fort)
        }

        /// <summary>Server: Tempo-Faktor (gebrochenes Bein).</summary>
        public void ServerSetSpeedFactor(float factor) => SpeedFactor = Mathf.Clamp(factor, 0.2f, 1f);

        /// <summary>Server: Schadensfaktor (Kolosseum-Champions). 1 = normal.</summary>
        public float DamageScale { get; private set; } = 1f;
        public void ServerSetDamageScale(float scale) => DamageScale = Mathf.Clamp(scale, 0.1f, 10f);

        /// <summary>Kurzer Schritt zur Seite auf einem Bogen um das Ziel (der Abstand bleibt); der Blick bleibt beim Ziel.</summary>
        private bool BeginSidestep(Vector3 targetPos, float dist)
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return false;
            Vector3 from = transform.position - targetPos;
            from.y = 0f;
            if (from.sqrMagnitude < 0.04f) return false;
            float step = UnityEngine.Random.Range(0.9f, 1.3f);
            float angle = step / Mathf.Max(0.8f, dist) * Mathf.Rad2Deg * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
            Vector3 p = targetPos + Quaternion.Euler(0f, angle, 0f) * from;
            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 1f, NavMesh.AllAreas)) return false;
            MoveTo(hit.position, definition.runSpeed * SidestepSpeed);
            _agent.updateRotation = false; // seitlich treten, FaceTarget dreht zum Ziel
            _sidestepUntil = Time.time + SidestepSeconds;
            SetState(EnemyState.Chase);
            return true;
        }

        private void BeginPounce()
        {
            SetState(EnemyState.Attack);
            Stop();
            _pending = AttackKind.Pounce;
            _pounceCooldown = PounceCooldown;
            _windupTimer = 0.6f;
            _attackCooldown = definition.attackInterval;
            AttackBegan?.Invoke(AimPoint(_target), 0.6f, false);
        }

        // ------------------------------------------------------------------ Sprung auf einen Punkt (Geschichts-Begegnungen)

        private Vector3 _leapPoint;
        private Action<Vector3> _leapLanded;

        /// <summary>
        /// Server: Satz auf einen Punkt (kein Spieler) – z. B. „Sprung zur Herde“ des Maehnenreissers. Deutlich angekuendigt
        /// (Ausholen = <paramref name="windup"/>), landet kurz vor dem Punkt; <paramref name="landed"/> meldet die Landestelle.
        /// Wird der Gegner betaeubt, faellt der Sprung aus. false = geht gerade nicht (holt schon aus, betaeubt, tot).
        /// </summary>
        public bool ServerLeapAt(Vector3 point, float windup, Action<Vector3> landed)
        {
            if (!_active || State == EnemyState.Dead || IsStunned || _windupTimer >= 0f) return false;
            SetState(EnemyState.Attack);
            Stop();
            _move = null;
            _pending = AttackKind.LeapAt;
            _leapPoint = point;
            _leapLanded = landed;
            _windupTimer = Mathf.Max(0.2f, windup);
            _attackCooldown = definition.attackInterval + windup;
            AttackBegan?.Invoke(point + Vector3.up * 0.9f, windup, false);
            return true;
        }

        /// <summary>Holt gerade zu einem Satz auf einen Punkt aus (Tests/Begegnung).</summary>
        public bool IsLeapingAtPoint => _pending == AttackKind.LeapAt && _windupTimer >= 0f;

        private void ResolveLeapAt()
        {
            Vector3 from = transform.position;
            Vector3 dir = _leapPoint - from;
            dir.y = 0f;
            Vector3 land = _leapPoint - (dir.sqrMagnitude > 1e-3f ? dir.normalized : transform.forward) * 1.6f;
            if (NavMesh.SamplePosition(land, out NavMeshHit hitPos, 3f, NavMesh.AllAreas))
            {
                if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.Warp(hitPos.position);
                else transform.position = hitPos.position;
                Pounced?.Invoke(from, hitPos.position);
                land = hitPos.position;
            }
            Action<Vector3> cb = _leapLanded;
            _leapLanded = null;
            AttackResolved?.Invoke(false, land);
            SetState(EnemyState.Chase);
            cb?.Invoke(land);
        }

        private void FacePoint(Vector3 point, float dt, float degPerSecond)
        {
            Vector3 to = point - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), degPerSecond * dt);
        }

        private void ResolveAttack()
        {
            _windupTimer = -1f;
            AttackKind kind = _pending;
            _pending = AttackKind.None;
            EnemyAttackDefinition move = _move;
            _move = null;
            if (kind == AttackKind.LeapAt)
            {
                ResolveLeapAt();
                return;
            }

            if (kind == AttackKind.Area)
            {
                ResolveArea(move);
                SetState(EnemyState.Chase);
                AfterMove(move);
                return;
            }

            if (!IsValidTarget(_target))
            {
                SetState(EnemyState.Chase);
                return;
            }

            if (kind == AttackKind.Pounce && move != null) Stop(); // Ansturm endet
            else if (kind == AttackKind.Pounce)
            {
                // Satz bis kurz vor das Ziel – danach normaler Biss mit Aufschlag
                Vector3 from = transform.position;
                Vector3 dir = _target.transform.position - from;
                dir.y = 0f;
                Vector3 land = _target.transform.position - dir.normalized * 1.3f;
                if (NavMesh.SamplePosition(land, out NavMeshHit hitPos, 2.5f, NavMesh.AllAreas))
                {
                    if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.Warp(hitPos.position);
                    else transform.position = hitPos.position;
                    Pounced?.Invoke(from, hitPos.position);
                }
            }

            bool hit;
            bool thrown = kind == AttackKind.Throw;
            Vector3 point = AimPoint(_target);
            if (definition.style == EnemyStyle.Ranged || thrown)
            {
                var motor = _target.GetComponent<CharacterMotor>();
                bool dodging = motor != null && motor.IsDodging;
                float accuracy = thrown ? definition.throwAccuracy : definition.rangedAccuracy;
                float reach = thrown ? definition.throwRange + 1f : definition.attackRange;
                hit = HasLineOfSight(_target) && !dodging && UnityEngine.Random.value <= accuracy &&
                      HorizontalDistance(transform.position, _target.transform.position) <= reach;
                if (!hit)
                {
                    // Fehlschuss: knapp daneben (fuer die Leuchtspur)
                    point += new Vector3(UnityEngine.Random.Range(-0.8f, 0.8f), UnityEngine.Random.Range(-0.3f, 0.6f), UnityEngine.Random.Range(-0.8f, 0.8f));
                }
            }
            else
            {
                // Nahkampf: Wer rechtzeitig aus Reichweite oder Schlagwinkel ist, wird nicht getroffen
                Vector3 to = _target.transform.position - transform.position;
                to.y = 0f;
                float range = move != null ? (move.kind == EnemyMoveKind.Charge ? 3.2f : move.maxRange) : definition.attackRange;
                float arc = move != null ? move.arc : definition.attackArc;
                float reach = range * 1.15f + 0.3f;
                hit = to.magnitude <= reach && (to.magnitude < 0.6f || kind == AttackKind.Pounce || Vector3.Angle(transform.forward, to) <= arc * 0.5f);
            }

            if (hit)
            {
                Vector3 dir = _target.transform.position - transform.position;
                dir.y = 0f;
                float amount = definition.RollDamage(Level) *
                               (move != null ? move.damageMultiplier : kind == AttackKind.Pounce ? 1.5f : thrown ? definition.throwDamageFactor : 1f);
                bool wasAlive = _target.IsAlive;
                _target.ApplyDamage(new DamageInfo
                {
                    Amount = Mathf.Round(amount * DamageScale),
                    IsCritical = false,
                    Source = gameObject,
                    HitPoint = point,
                    Direction = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward,
                    Knockback = move != null ? move.knockback : kind == AttackKind.Pounce ? 0.8f : thrown ? 0.15f : 0.3f,
                    WeaponId = thrown ? definition.Id + ".throw" : definition.Id,
                    Dodgeable = definition.style != EnemyStyle.Ranged && !thrown
                });
                if (wasAlive && definition.poisonPerSecond > 0f && !_target.IsTemporarilyInvulnerable)
                    DamageOverTime.Apply(_target, gameObject, definition.poisonPerSecond + 0.5f * (Level - 1), definition.poisonDuration, definition.Id + ".poison");
            }
            AttackResolved?.Invoke(hit, point);
            SetState(EnemyState.Chase);
            AfterMove(move);
        }

        /// <summary>Stampfer: trifft alle Spieler im Radius (Hechtrolle weicht aus).</summary>
        private void ResolveArea(EnemyAttackDefinition move = null)
        {
            Vector3 c = transform.position;
            float radius = move != null && move.areaRadius > 0f ? move.areaRadius : definition.areaRadius;
            float mult = move != null ? move.damageMultiplier : definition.areaDamageMultiplier;
            bool any = DamagePlayersInRadius(c, radius, definition.RollDamage(Level) * mult,
                definition.Id + ".stomp", 1.2f);
            AttackResolved?.Invoke(any, c);
        }

        /// <summary>Beutetiere weichen dem Angreifer aus (14 m, 110 % Lauftempo, 3,5 s).</summary>
        private void BeginFlee() => BeginFlee(14f, 1.1f, 3.5f);

        private void BeginFlee(float distance, float speedFactor, float seconds)
        {
            _hasFled = true;
            _fleeTimer = seconds;
            _windupTimer = -1f;
            Vector3 away = transform.position - _target.transform.position;
            away.y = 0f;
            Vector3 p = transform.position + away.normalized * distance;
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, Mathf.Min(5f, distance * 0.5f), NavMesh.AllAreas))
                MoveTo(hit.position, definition.runSpeed * speedFactor);
            SetState(EnemyState.Flee);
        }

        private void BeginReturn()
        {
            _move = null;
            _sidestepUntil = -1f;
            _sidestepReady = false;
            _target = null;
            _windupTimer = -1f;
            _pending = AttackKind.None;
            _pressedTimer = 0f;
            _strikes.Clear();
            if (_arena)
            {
                // Arena: kein Heimweg, keine Heilung – zur Mitte, dort das naechste Ziel suchen
                _wanderTimer = 0f;
                SetState(EnemyState.Idle);
                return;
            }
            if (_route != null)
            {
                Home = _route.ClosestPoint(transform.position, out _routeIndex);
                _routePause = 0f;
            }
            MoveTo(Home, definition.runSpeed);
            SetState(EnemyState.Return);
        }

        // ---------------------------------------------------------------- Abtauchen

        private void BeginDive()
        {
            _pressedTimer = 0f;
            _windupTimer = -1f;
            _pending = AttackKind.None;
            _diveTimer = 0f;
            _diveMoved = false;
            Stop();
            SetConcealment(EnemyConcealment.Submerged);
        }

        private void ThinkDive()
        {
            _health.SetInvulnerableFor(0.3f);
            _diveTimer += ThinkInterval;
            if (!_diveMoved && _diveTimer >= 1.4f)
            {
                _diveMoved = true;
                // 12–18 m vom Bedraenger weg, in Reichweite der Heimat wieder auftauchen
                Vector3 from = _target != null ? _target.transform.position : transform.position;
                for (int tries = 0; tries < 8; tries++)
                {
                    Vector2 r = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(12f, 18f);
                    Vector3 p = from + new Vector3(r.x, 0f, r.y);
                    if (HorizontalDistance(p, Home) > definition.leashDistance * 0.8f) continue;
                    if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
                    if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.Warp(hit.position);
                    else transform.position = hit.position;
                    break;
                }
            }
            if (_diveTimer >= 2.2f)
            {
                _diveTimer = -1f;
                SetConcealment(EnemyConcealment.None);
                _attackCooldown = Mathf.Min(_attackCooldown, 0.5f);
                SetState(EnemyState.Chase);
            }
        }

        private void OnDamaged(DamageInfo info)
        {
            if (!_active || State == EnemyState.Dead) return;
            Health attacker = info.Source != null ? info.Source.GetComponentInParent<Health>() : null;
            if (attacker == null || attacker == _health) return;
            if (Concealment == EnemyConcealment.Camouflaged) SetConcealment(EnemyConcealment.None);
            if (!_aggressors.Contains(attacker)) _aggressors.Add(attacker);
            IThreatModifier tm = info.Source.GetComponentInParent<IThreatModifier>();
            _threat[attacker] = ThreatOf(attacker) + Mathf.Max(1f, info.Amount) * (tm != null ? tm.ThreatMultiplier : 1f);
            if (FleesFromAttackers)
            {
                // Beutetiere greifen nie an – sie weichen dem Angreifer aus
                if (State == EnemyState.Idle || State == EnemyState.Wander)
                {
                    _target = attacker;
                    BeginFlee();
                }
                return;
            }
            if (State == EnemyState.Idle || State == EnemyState.Wander) Engage(attacker);
            else if (_target == null || !IsValidTarget(_target)) _target = attacker;
        }

        private void OnDied()
        {
            if (!_active) return;
            ClearThreat();
            _move = null;
            _windupTimer = -1f;
            _diveTimer = -1f;
            _strikes.Clear();
            _target = null;
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
            SetConcealment(EnemyConcealment.None);
            SetState(EnemyState.Dead);
            Killed?.Invoke(this);
        }

        // ================================================================== Begegnungen (Server, von Boss-Skripten benutzt)

        /// <summary>
        /// Server: Ladung/Einschlag an beliebiger Stelle. Clients sehen sofort den Warnring (wie beim Stampfer);
        /// nach <paramref name="windup"/> Sekunden trifft er alle Spieler im Radius (Hechtrolle weicht aus).
        /// </summary>
        public void ServerAreaStrike(Vector3 center, float radius, float windup, float damage, string weaponSuffix = "strike")
        {
            if (!_active || State == EnemyState.Dead) return;
            _strikes.Add(new PendingStrike
            {
                Center = center,
                Radius = Mathf.Max(0.5f, radius),
                Due = Time.time + Mathf.Max(0.1f, windup),
                Damage = damage,
                WeaponId = (definition != null ? definition.Id : "enemy") + "." + weaponSuffix
            });
            AreaAttackBegan?.Invoke(center, windup, radius);
        }

        /// <summary>Server: alle noch nicht gezuendeten Ladungen entschaerfen (Rueckkehr, Tod).</summary>
        public void ServerClearStrikes() => _strikes.Clear();

        /// <summary>Server: sofort dieses Ziel angreifen (herbeigerufene Helfer).</summary>
        public void ServerEngage(Health target)
        {
            if (!_active || !IsValidTarget(target)) return;
            if (!_aggressors.Contains(target)) _aggressors.Add(target);
            // Skript-Befehl: dieses Ziel zuerst – an die Spitze der Bedrohung (sonst wuerde die Tabelle gleich zurueckwechseln)
            float top = 0f;
            foreach (var kv in _threat) if (kv.Value > top) top = kv.Value;
            _threat[target] = Mathf.Max(ThreatOf(target), top * 1.35f + 1f);
            Engage(target);
        }

        /// <summary>
        /// Server: neue Heimat setzen und dorthin gehen (z. B. Lichtfresser zur Lampe). Im Kampf erst danach –
        /// die Leine gilt ab dann von der neuen Heimat aus.
        /// </summary>
        public void ServerSetHome(Vector3 home)
        {
            if (!_active || State == EnemyState.Dead) return;
            Home = home;
            if (InCombat || State == EnemyState.Flee) return;
            MoveTo(home, definition.runSpeed);
            SetState(EnemyState.Wander);
        }

        /// <summary>Server: Kampf abbrechen und heimkehren (z. B. ortsfester Boss, wenn niemand mehr in der Naehe ist).</summary>
        public void ServerDisengage()
        {
            if (!_active || State == EnemyState.Dead || State == EnemyState.Return) return;
            BeginReturn();
        }

        /// <summary>Server: naechster Angriff fruehestens in <paramref name="seconds"/> (z. B. waehrend eines Wurfs).</summary>
        public void ServerDelayAttacks(float seconds) => _attackCooldown = Mathf.Max(_attackCooldown, seconds);

        private void UpdateStrikes()
        {
            float now = Time.time;
            for (int i = _strikes.Count - 1; i >= 0; i--)
            {
                PendingStrike s = _strikes[i];
                if (now < s.Due) continue;
                _strikes.RemoveAt(i);
                bool any = DamagePlayersInRadius(s.Center, s.Radius, s.Damage, s.WeaponId, 1.4f);
                StrikeExploded?.Invoke(s.Center, s.Radius, any);
            }
        }

        private bool DamagePlayersInRadius(Vector3 c, float radius, float damage, string weaponId, float knockback)
        {
            int mask = LayerMask.GetMask(GameLayers.Player);
            int n = Physics.OverlapSphereNonAlloc(c, radius + 0.5f, AreaBuffer, mask, QueryTriggerInteraction.Ignore);
            bool any = false;
            var done = new HashSet<Health>();
            for (int i = 0; i < n; i++)
            {
                Health h = AreaBuffer[i].GetComponentInParent<Health>();
                if (!IsValidTarget(h) || done.Contains(h)) continue;
                if (HorizontalDistance(h.transform.position, c) > radius) continue;
                if (Mathf.Abs(h.transform.position.y - c.y) > 3f) continue;
                done.Add(h);
                Vector3 dir = h.transform.position - c;
                dir.y = 0f;
                h.ApplyDamage(new DamageInfo
                {
                    Amount = Mathf.Round(damage * DamageScale),
                    Source = gameObject,
                    HitPoint = h.transform.position + Vector3.up,
                    Direction = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward,
                    Knockback = knockback,
                    WeaponId = weaponId,
                    Unblockable = true // Flaeche: ausweichen statt blocken
                });
                any = true;
            }
            return any;
        }

        // ================================================================== Ausweichen (WoW Classic: Gegner weichen 5 % der Nahkampfschlaege von vorn aus)

        public bool TryDodge(in DamageInfo info)
        {
            if (!info.Dodgeable || _health == null || !_health.IsAlive) return false;
            Vector3 fromAttacker = -info.Direction;
            fromAttacker.y = 0f;
            if (fromAttacker.sqrMagnitude > 1e-4f && Vector3.Dot(transform.forward, fromAttacker.normalized) < 0f) return false;
            return UnityEngine.Random.value < DodgeChance;
        }

        /// <summary>Tests: 0 schaltet das Ausweichen der Gegner ab.</summary>
        public static float DodgeChance = 0.05f;

        // ================================================================== Hilfen

        /// <summary>Arena: naechster lebender Spieler innerhalb der Arena (ohne Sichtlinie – die Arena ist offen).</summary>
        private Health FindArenaTarget()
        {
            int mask = LayerMask.GetMask(GameLayers.Player);
            int n = Physics.OverlapSphereNonAlloc(_arenaCenter, _arenaRadius + 10f, ArenaBuffer, mask, QueryTriggerInteraction.Ignore);
            Health best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Health h = ArenaBuffer[i].GetComponentInParent<Health>();
                if (!IsValidTarget(h)) continue;
                float d = (h.transform.position - transform.position).sqrMagnitude;
                if (d < bestScore) { bestScore = d; best = h; }
            }
            return best;
        }

        private static readonly Collider[] ArenaBuffer = new Collider[32];

        private Health FindTarget(float radius)
        {
            // ohne Speicher je Aufruf (vorher OverlapSphere mit neuem Feld je Denkschritt – bei vielen Gegnern Müll für den GC)
            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, TargetBuffer, PlayerMask, QueryTriggerInteraction.Ignore);
            Health best = null;
            float bestScore = float.MaxValue;
            for (int k = 0; k < count; k++)
            {
                Collider c = TargetBuffer[k];
                Health h = c.GetComponentInParent<Health>();
                if (!IsValidTarget(h)) continue;
                float d = (h.transform.position - transform.position).sqrMagnitude;
                if (Concealment == EnemyConcealment.Camouflaged)
                {
                    // Getarnt: nur Einzelgaenger angreifen – oder wer ihm zu nahe kommt
                    bool close = d <= definition.revealRadius * definition.revealRadius;
                    if (!close && definition.huntsLoners && !IsLoner(h)) continue;
                }
                if (!HasLineOfSight(h)) continue;
                if (d < bestScore) { bestScore = d; best = h; }
            }
            return best;
        }

        private static bool IsLoner(Health h)
        {
            int mask = LayerMask.GetMask(GameLayers.Player);
            foreach (Collider c in Physics.OverlapSphere(h.transform.position, LonerRadius, mask, QueryTriggerInteraction.Ignore))
            {
                Health other = c.GetComponentInParent<Health>();
                if (other != null && other != h && other.IsAlive) return false;
            }
            return true;
        }

        /// <summary>Nur Beutetiere (Passiv + fleesWhenAttacked) fliehen vor Angreifern; alle anderen Passiven wehren sich wie in Classic (Max 03.10.2026).</summary>
        private bool FleesFromAttackers => Aggro == AggroProfile.Passive && definition != null && definition.fleesWhenAttacked;

        private bool IsValidTarget(Health h) => h != null && h.isActiveAndEnabled && h.IsAlive && h != _health;

        /// <summary>
        /// Ziel weit außerhalb des eigenen Gebiets – weggesprungen, in eine Instanz, wiederbelebt, später Ruhestein: Der Gegner gibt
        /// es auf und kehrt heim, wie in Classic (Max 09.10.2026: „Ja, einbauen“). Beim normalen Weglaufen greift vorher die eigene
        /// Leine (<see cref="IsBeyondLeash"/>): Der Verfolger hängt dicht hinter seinem Ziel und erreicht sie, bevor das Ziel so
        /// weit draußen ist. Vorher blieb ein Gegner, der sein Ziel nicht mehr erreichen konnte, ewig im Kampf – und hielt die
        /// Figur „im Kampf“ (kein Aufsitzen, keine Fähigkeiten „nur außerhalb des Kampfes“, kein Reiserhythmus).
        /// </summary>
        public const float ZielGebietFaktor = 1.5f, ZielGebietZugabe = 20f;

        /// <summary>true, wenn ein Ziel mit diesem Abstand zum eigenen Gebiet (Heimpunkt bzw. Route) aufgegeben wird.</summary>
        public static bool ZielAusserhalbGebiet(float abstandZumGebiet, float leine) => abstandZumGebiet > leine * ZielGebietFaktor + ZielGebietZugabe;

        private bool ImGebiet(Health h)
        {
            if (_arena || h == null || definition == null) return true;
            Vector3 p = h.transform.position;
            float d = _route != null ? _route.DistanceTo(p) : HorizontalDistance(p, Home);
            return !ZielAusserhalbGebiet(d, definition.leashDistance);
        }

        private bool IsBeyondLeash()
        {
            if (_arena) return false;
            if (_route != null) return _route.DistanceTo(transform.position) > definition.leashDistance;
            return HorizontalDistance(transform.position, Home) > definition.leashDistance;
        }

        private bool HasLineOfSight(Health h)
        {
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 aim = AimPoint(h);
            return !Physics.Linecast(eye, aim, GameLayers.CameraCollisionMask() & ~LayerMask.GetMask(GameLayers.Enemy), QueryTriggerInteraction.Ignore);
        }

        private static Vector3 AimPoint(Health h) => h.transform.position + Vector3.up * 1.2f;

        private void MoveTo(Vector3 pos, float speed)
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;
            _agent.isStopped = false;
            _agent.speed = speed * SpeedFactor * SlowFactor;
            _agent.updateRotation = true;
            _agent.SetDestination(pos);
        }

        private void Stop()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
            _agent.updateRotation = false;
        }

        private bool IsStopped => _agent == null || !_agent.enabled || _agent.isStopped;

        private void FaceTarget(float dt, float degPerSecond)
        {
            if (_target == null) return;
            Vector3 to = _target.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-4f) return;
            Quaternion want = Quaternion.LookRotation(to);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, degPerSecond * dt);
        }

        private void SetState(EnemyState s)
        {
            if (State == s) return;
            State = s;
            StateChanged?.Invoke(s);
        }

        private void SetConcealment(EnemyConcealment c)
        {
            if (Concealment == c) return;
            Concealment = c;
            if (_combatTarget != null) _combatTarget.Concealed = c != EnemyConcealment.None;
            ConcealmentChanged?.Invoke(c);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
