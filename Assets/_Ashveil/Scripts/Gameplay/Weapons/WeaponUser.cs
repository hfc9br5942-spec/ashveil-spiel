using System;
using System.Collections.Generic;
using Ashveil.Core.Events;
using Ashveil.Data;
using Ashveil.Gameplay.Audio;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Targeting;
using UnityEngine;

namespace Ashveil.Gameplay.Weapons
{
    /// <summary>Art des laufenden Angriffs (fuer Animation und Effekte).</summary>
    public enum AttackKind
    {
        Combo,
        Special,
        /// <summary>Angriff im Sprung (Taste 1 in der Luft)</summary>
        Air,
        /// <summary>Sturzangriff (Taste 2 in der Luft)</summary>
        Plunge
    }

    /// <summary>Phase des aktuellen Waffenablaufs.</summary>
    public enum WeaponPhase
    {
        Idle,
        Windup,
        Active,
        Recovery,
        Reloading,
        Swapping
    }

    /// <summary>
    /// Fuehrt Waffen: Auto-Angriff, Kombos, Spezialangriff, Munition, Nachladen und Waffenwechsel.
    ///
    ///   1  Auto-Angriff an/aus (mit Ziel). Ohne Ziel: ein einzelner Angriff in Blickrichtung.
    ///   2  Spezialangriff der aktiven Waffe (Abklingzeit)
    ///   F  Nahkampf- <-> Fernkampfwaffe
    ///   R  Nachladen
    ///
    /// Waehrend eines Angriffs dreht sich der Charakter automatisch zum Ziel (Soft-Lock), solange
    /// nicht mit der rechten Maustaste selbst gelenkt wird. Die eigentliche Trefferermittlung
    /// macht <see cref="CombatResolver"/> (spaeter serverseitig).
    /// </summary>
    [RequireComponent(typeof(TargetingSystem))]
    [DisallowMultipleComponent]
    public sealed class WeaponUser : MonoBehaviour
    {
        [Header("Waffen")]
        [SerializeField] private WeaponDefinition meleeWeapon;
        [SerializeField] private WeaponDefinition rangedWeapon;
        [Tooltip("Zweite Nahkampfwaffe in der Nebenhand (Beidhaendig, nur Aschenjaeger)")]
        [SerializeField] private WeaponDefinition offhandWeapon;
        [SerializeField] private bool startWithMelee = true;

        [Header("Geometrie")]
        [SerializeField] private float meleeOriginHeight = 1.0f;
        [SerializeField] private float muzzleHeight = 1.35f;
        [SerializeField] private float muzzleForward = 0.55f;

        [Header("Ablauf")]
        [SerializeField] private float swapTime = 0.45f;
        [Tooltip("Ab dieser Abweichung (Grad) wartet der Angriff, bis der Charakter zum Ziel gedreht ist")]
        [SerializeField] private float maxFacingError = 50f;
        [Tooltip("Ohne Ziel: maximaler Winkel zwischen Blickrichtung und Kameraziel fuer Schuesse")]
        [SerializeField] private float freeAimCone = 70f;

        private TargetingSystem _targeting;
        private readonly Dictionary<WeaponDefinition, int> _ammo = new Dictionary<WeaponDefinition, int>();
        private readonly Dictionary<WeaponDefinition, float> _specialReadyAt = new Dictionary<WeaponDefinition, float>();
        private readonly List<HitResult> _hits = new List<HitResult>();
        private readonly List<Vector3> _directions = new List<Vector3>();

        private bool _usingMelee;
        private WeaponPhase _phase = WeaponPhase.Idle;
        private float _phaseTimer;
        private float _phaseDuration;
        private AttackStep _step;
        private bool _stepIsSpecial;
        private int _comboIndex;
        private float _lastAttackEndTime = -99f;
        private bool _autoAttack;
        private bool _queuedSingle;
        private bool _queuedSpecial;
        private bool _queuedAir;
        private bool _queuedPlunge;
        private float _nextErrorTime;
        private Camera _camera;
        private bool _steering;
        private bool _softLockAllowed = true;
        private CharacterMotor _motor;
        private AttackKind _kind;
        private bool _airSlashUsed;
        private bool _plungeUsed;
        private bool _plungeFalling;

        /// <summary>
        /// Vom Spieler gesetzt: darf der Soft-Lock den Charakter gerade drehen? (Nein, solange der
        /// Spieler selbst laeuft oder mit A/D dreht – sonst koennte man nicht vom Ziel weglaufen.)
        /// </summary>
        public void SetSoftLockAllowed(bool allowed) => _softLockAllowed = allowed;

        // ---------------------------------------------------------------- Oeffentliche Sicht (HUD, Effekte, Animation)

        public WeaponDefinition ActiveWeapon => _usingMelee ? meleeWeapon : rangedWeapon;
        public WeaponDefinition MeleeWeapon => meleeWeapon;
        public WeaponDefinition RangedWeapon => rangedWeapon;
        public WeaponPhase Phase => _phase;
        /// <summary>0..1 Fortschritt der aktuellen Phase.</summary>
        public float PhaseProgress => _phaseDuration > 0f ? Mathf.Clamp01(1f - _phaseTimer / _phaseDuration) : 1f;
        public AttackStep CurrentStep => _step;
        /// <summary>Index des laufenden Kombo-Schlags (0, 1, 2 ...), fuer die Animation.</summary>
        public int CurrentComboIndex => _comboIndex;
        public bool CurrentStepIsSpecial => _stepIsSpecial;
        public AttackKind CurrentKind => _kind;
        /// <summary>Soll der Motor gerade nach unten stossen (Sturzangriff)?</summary>
        public bool WantsPlunge => _plungeFalling;
        public bool IsAutoAttacking => _autoAttack;
        public bool IsReloading => _phase == WeaponPhase.Reloading;
        public int Ammo => ActiveWeapon != null && _ammo.TryGetValue(ActiveWeapon, out int a) ? a : 0;
        public int MagazineSize => ActiveWeapon != null ? ActiveWeapon.magazineSize : 0;

        /// <summary>Server hat anders gezaehlt (z. B. Schuss mit leerem Magazin abgelehnt): seinen Stand uebernehmen.</summary>
        public void CorrectAmmo(WeaponDefinition w, int ammo)
        {
            if (w == null || !w.UsesAmmo) return;
            _ammo[w] = Mathf.Clamp(ammo, 0, w.magazineSize);
        }
        public float SpecialCooldownTotal => ActiveWeapon != null ? ActiveWeapon.specialCooldown : 0f;
        public float SpecialCooldownRemaining =>
            ActiveWeapon != null && _specialReadyAt.TryGetValue(ActiveWeapon, out float t) ? Mathf.Max(0f, t - Time.time) : 0f;

        /// <summary>Holt aus oder trifft gerade (fuer Soft-Lock und Animation).</summary>
        public bool IsInAttackMotion => _phase == WeaponPhase.Windup || _phase == WeaponPhase.Active;

        /// <summary>Soll der Charakter gerade zum Ziel gedreht werden?</summary>
        public bool WantsFacing { get; private set; }
        public float FacingYaw { get; private set; }

        /// <summary>Angriff beginnt (Ausholen). Parameter: Schritt, Spezial?</summary>
        public event Action<AttackStep, bool> AttackStarted;
        /// <summary>Nahkampfschlag ausgefuehrt. Parameter: Treffer (kann leer sein).</summary>
        public event Action<IReadOnlyList<HitResult>> MeleeStruck;
        /// <summary>Schuss abgefeuert. Parameter: Muendung, Einschlaege.</summary>
        public event Action<Vector3, IReadOnlyList<HitResult>> Fired;
        public event Action<WeaponDefinition> WeaponChanged;
        /// <summary>Nachladen begonnen (Animation).</summary>
        public event Action ReloadStarted;
        /// <summary>Sturzangriff aufgeschlagen. Parameter: Position, Radius.</summary>
        public event Action<Vector3, float> Slammed;

        private IAttackAuthority _authority;
        private IRangedSupply _supply;

        /// <summary>Schaden der Nebenhand im Verhaeltnis (WoW: 50 %).</summary>
        public const float OffhandDamage = 0.5f;

        /// <summary>Zweite Nahkampfwaffe (Beidhaendig) oder null.</summary>
        public WeaponDefinition OffhandWeapon => offhandWeapon;
        /// <summary>Kaempft gerade mit zwei Waffen (Nahkampf aktiv, beide Haende bewaffnet).</summary>
        public bool IsDualWielding => _usingMelee && IsDual(meleeWeapon, offhandWeapon);
        /// <summary>Der laufende Schlag kommt aus der Nebenhand (Kombo wechselt die Haende ab).</summary>
        public bool CurrentStrikeIsOffhand => IsDualWielding && _kind == AttackKind.Combo && (_comboIndex & 1) == 1;
        /// <summary>Wirkt/zielt gerade (Bogen spannen, Zauber sammeln) – fuer Zauberleiste und Animation.</summary>
        public bool IsCasting => _phase == WeaponPhase.Windup && ActiveWeapon != null && ActiveWeapon.IsProjectile &&
                                 (_kind == AttackKind.Combo || _kind == AttackKind.Special);
        /// <summary>Pfeil/Zauber losgelassen. Parameter: Ursprung, Richtung, gezielt?</summary>
        public event Action<Vector3, Vector3, bool> ProjectileLaunched;

        private static bool IsDual(WeaponDefinition main, WeaponDefinition off) =>
            main != null && off != null && main.IsMelee && off.IsMelee;

        /// <summary>Welche Waffe schlaegt bei Art + Kombo-Index zu, und mit welchem Schadensfaktor (Server und Client gleich).</summary>
        public static WeaponDefinition StrikeWeapon(WeaponDefinition main, WeaponDefinition off, AttackKind kind, int comboIndex, out float scale)
        {
            scale = 1f;
            if (kind == AttackKind.Combo && IsDual(main, off) && (comboIndex & 1) == 1)
            {
                scale = OffhandDamage;
                return off;
            }
            return main;
        }

        /// <summary>
        /// Angriffsschritt mit Tempo: Beim Bogen bestimmt das Waffentempo (durch den Koecher beschleunigt) den Takt des
        /// automatischen Schusses wie in WoW – Zielen (aimTime) + Loslassen + Rest bis zum naechsten Schuss.
        /// </summary>
        public static AttackStep EffectiveStep(WeaponDefinition w, AttackKind kind, int comboIndex, float haste)
        {
            AttackStep st = StepFor(w, kind, comboIndex);
            if (w == null || w.archetype != WeaponArchetype.Projectile || kind != AttackKind.Combo) return st;
            float cycle = w.attackSpeed / (1f + Mathf.Max(0f, haste));
            st.windup = w.aimTime > 0f ? w.aimTime : st.windup;
            st.active = Mathf.Min(st.active, 0.1f);
            st.recovery = Mathf.Max(0.05f, cycle - st.windup - st.active);
            return st;
        }

        private float Haste => _supply != null ? _supply.RangedHaste : 0f;

        /// <summary>Schaden entscheidet der Server – hier nur vorausberechnen und Absicht melden.</summary>
        private bool RemoteAuthority => _authority != null && _authority.IsRemoteAuthority;

        private void Awake()
        {
            _authority = GetComponent<IAttackAuthority>();
            _supply = GetComponent<IRangedSupply>();
            _targeting = GetComponent<TargetingSystem>();
            _motor = GetComponent<CharacterMotor>();
            _usingMelee = startWithMelee || rangedWeapon == null;
            if (meleeWeapon == null && rangedWeapon != null) _usingMelee = false;
            InitWeapon(meleeWeapon);
            InitWeapon(rangedWeapon);
            InitWeapon(offhandWeapon);
        }

        private void Start()
        {
            WeaponChanged?.Invoke(ActiveWeapon);
        }

        private void OnEnable()
        {
            if (_motor != null) _motor.Landed += OnLanded;
        }

        private void OnDisable()
        {
            if (_motor != null) _motor.Landed -= OnLanded;
        }

        // Schwimmen ist kein Sprung (MOV-169): im Wasser gelten die Bodenangriffe, keine Luft- oder Sturzangriffe.
        private bool IsAirborne => _motor != null && !_motor.State.Grounded && !_motor.IsSwimming;
        private bool IsDodging => _motor != null && _motor.IsDodging;

        private void InitWeapon(WeaponDefinition w)
        {
            if (w == null) return;
            _ammo[w] = w.UsesAmmo ? w.magazineSize : 0;
            _specialReadyAt[w] = 0f;
        }

        /// <summary>Waffen wechseln (Ausruestung). Eine fehlende Seite schaltet auf die andere um.</summary>
        public void SetWeapons(WeaponDefinition melee, WeaponDefinition ranged, WeaponDefinition offhand = null)
        {
            if (!IsDual(melee, offhand)) offhand = null;
            if (melee == meleeWeapon && ranged == rangedWeapon && offhand == offhandWeapon) return;
            WeaponDefinition before = ActiveWeapon;
            bool offChanged = offhand != offhandWeapon;
            meleeWeapon = melee;
            rangedWeapon = ranged;
            offhandWeapon = offhand;
            if (offhand != null && !_specialReadyAt.ContainsKey(offhand)) InitWeapon(offhand);
            if (melee != null && !_ammo.ContainsKey(melee)) InitWeapon(melee);
            if (ranged != null && !_ammo.ContainsKey(ranged)) InitWeapon(ranged);
            if (_usingMelee && meleeWeapon == null && rangedWeapon != null) _usingMelee = false;
            else if (!_usingMelee && rangedWeapon == null) _usingMelee = true;
            if (ActiveWeapon != before || offChanged)
            {
                _autoAttack = false;
                _queuedSingle = false;
                _queuedSpecial = false;
                _comboIndex = 0;
                _queuedSwap = false;
                if (_phase != WeaponPhase.Idle) SetPhase(WeaponPhase.Idle, 0f);
            }
            WeaponChanged?.Invoke(ActiveWeapon);
        }

        // ---------------------------------------------------------------- Takt

        /// <summary>Einmal pro Frame vom Spieler aufgerufen, vor der Bewegung.</summary>
        public void Tick(in CombatInput input, float dt, Camera cam, bool steeringWithMouse)
        {
            _camera = cam;
            _steering = steeringWithMouse;
            WeaponDefinition w = ActiveWeapon;
            CombatTarget target = _targeting.Current;
            bool hasTarget = target != null && target.IsAttackable;

            // --- Eingaben
            if (input.SwapWeapon || _queuedSwap) BeginSwap();
            if (input.Reload) BeginReload(true);
            bool airMoves = w != null && w.hasAirAttacks && IsAirborne;
            if (input.Ability1)
            {
                if (airMoves) _queuedAir = true;
                else if (_autoAttack) _autoAttack = false;
                else if (hasTarget) _autoAttack = true;
                else _queuedSingle = true;
            }
            if (input.Ability2)
            {
                if (airMoves) _queuedPlunge = true;
                else if (w != null)
                {
                    if (SpecialCooldownRemaining > 0f) Error("Noch nicht bereit");
                    else _queuedSpecial = true;
                }
            }
            if (!IsAirborne)
            {
                _queuedAir = false;
                _queuedPlunge = false;
                _airSlashUsed = false;
                _plungeUsed = false;
                if (_plungeFalling) OnLanded(0f); // Sicherheitsnetz, falls eine Landung verpasst wurde
            }

            // Ausweichen unterbricht das Ausholen
            if (IsDodging && _phase == WeaponPhase.Windup && _kind != AttackKind.Plunge)
            {
                if (_kind == AttackKind.Special && w != null) _specialReadyAt[w] = 0f; // Abklingzeit zurueckgeben
                SetPhase(WeaponPhase.Idle, 0f);
            }

            if (!hasTarget) _autoAttack = false;

            // --- Blickrichtung zum Ziel
            bool attackingNow = _phase == WeaponPhase.Windup || _phase == WeaponPhase.Active;
            WantsFacing = hasTarget && !_steering && (_autoAttack || attackingNow || _queuedSpecial || _queuedSingle);
            if (WantsFacing)
            {
                Vector3 to = target.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) FacingYaw = Quaternion.LookRotation(to).eulerAngles.y;
                else WantsFacing = false;
            }

            // --- Ablauf
            if (_phase != WeaponPhase.Idle)
            {
                // Sturzangriff: die aktive Phase endet erst mit der Landung (siehe OnLanded)
                if (_plungeFalling) return;
                // Zielen/Wirken nur im Stand (WoW): Bewegung setzt das Spannen bzw. Wirken zurueck
                if (IsCasting && _motor != null && _motor.HorizontalSpeed > 0.6f)
                {
                    _phaseTimer = _phaseDuration;
                    return;
                }
                _phaseTimer -= dt;
                if (_phaseTimer <= 0f) AdvancePhase();
                return;
            }

            if (w == null || IsDodging) return;

            // --- Angriffe im Sprung (eigene Bewegungen, ohne Ziel/Reichweite)
            // Pro Sprung ein Luftschlag und ein Sturzangriff (Luftschlag -> Sturzangriff ist moeglich).
            bool canPlunge = _queuedPlunge && !_plungeUsed;
            bool canSlash = _queuedAir && !_airSlashUsed;
            if (IsAirborne && w.hasAirAttacks && (canPlunge || canSlash))
            {
                AttackKind kind = canPlunge ? AttackKind.Plunge : AttackKind.Air;
                if (kind == AttackKind.Plunge) { _plungeUsed = true; _queuedPlunge = false; }
                else { _airSlashUsed = true; _queuedAir = false; }
                BeginSpecialMove(w, kind);
                return;
            }
            // Waffen mit eigenen Luftangriffen nutzen im Sprung keine Boden-Kombo (Auto-Angriff pausiert).
            if (IsAirborne && w.hasAirAttacks) return;

            if (_queuedSpecial || _autoAttack || _queuedSingle)
            {
                if (w.UsesAmmo && Ammo <= 0)
                {
                    SfxPlayer.Play(SfxId.EmptyClick, transform.position, 0.6f);
                    BeginReload(false);
                    return;
                }
                if (w.ammoType != AmmoType.None && _supply != null && _supply.AmmoCount(w.ammoType) <= 0)
                {
                    _autoAttack = false;
                    _queuedSingle = false;
                    _queuedSpecial = false;
                    Error(w.ammoType == AmmoType.Bolt ? "Keine Bolzen" : "Keine Pfeile");
                    return;
                }

                if (hasTarget)
                {
                    if (!InRange(w, target))
                    {
                        _queuedSingle = false;
                        _queuedSpecial = false;
                        Error("Ziel ausser Reichweite");
                        return;
                    }
                    if (TooClose(w, target))
                    {
                        // Toter Winkel des Bogens (WoW): Auto-Schuss wartet, bis das Ziel weit genug weg ist
                        _queuedSingle = false;
                        _queuedSpecial = false;
                        Error("Ziel zu nah");
                        return;
                    }
                    if (FacingError(target) > maxFacingError)
                    {
                        // Wird gerade selbst gelenkt/gelaufen, dreht der Soft-Lock nicht – dann Hinweis.
                        if (_steering || !_softLockAllowed) Error("Ziel muss vor dir sein");
                        return;
                    }
                }

                bool special = _queuedSpecial;
                _queuedSpecial = false;
                _queuedSingle = false;
                BeginAttack(w, special);
            }
        }

        // ---------------------------------------------------------------- Angriffe

        private void BeginAttack(WeaponDefinition w, bool special)
        {
            if (special)
            {
                _step = w.special;
                _specialReadyAt[w] = Time.time + w.specialCooldown;
            }
            else
            {
                if (Time.time - _lastAttackEndTime > w.comboResetTime || w.combo == null || _comboIndex >= w.combo.Length)
                    _comboIndex = 0;
                _step = EffectiveStep(w, AttackKind.Combo, _comboIndex, Haste);
            }
            _stepIsSpecial = special;
            _kind = special ? AttackKind.Special : AttackKind.Combo;
            SetPhase(WeaponPhase.Windup, _step.windup);
            if (w.IsProjectile)
                SfxPlayer.Play(w.IsSpell ? SfxId.CastCharge : SfxId.BowDraw, transform.position + Vector3.up * 1.3f, 0.55f);
            AttackStarted?.Invoke(_step, special);
        }

        /// <summary>Luftschlag oder Sturzangriff.</summary>
        private void BeginSpecialMove(WeaponDefinition w, AttackKind kind)
        {
            _kind = kind;
            _step = kind == AttackKind.Plunge ? w.plunge : w.airAttack;
            _stepIsSpecial = kind == AttackKind.Plunge;
            SetPhase(WeaponPhase.Windup, _step.windup);
            AttackStarted?.Invoke(_step, _stepIsSpecial);
        }

        /// <summary>Landung: Sturzangriff entlaedt sich als Schockwelle; Luftangriff wieder frei.</summary>
        private void OnLanded(float impact)
        {
            _airSlashUsed = false;
            _plungeUsed = false;
            if (!_plungeFalling) return;
            _plungeFalling = false;

            WeaponDefinition w = ActiveWeapon;
            if (w != null)
            {
                var req = new AttackRequest
                {
                    Attacker = gameObject,
                    Origin = transform.position + Vector3.up * 0.8f,
                    Forward = transform.forward,
                    Weapon = w,
                    Step = w.plunge,
                    LocalPlayer = !RemoteAuthority,
                    RangeOverride = w.plungeRadius,
                    PredictOnly = RemoteAuthority
                };
                int hits = CombatResolver.ResolveMelee(req, _hits);
                if (RemoteAuthority) _authority.SubmitMelee(AttackIntentKind.Plunge, 0, req.Origin, req.Forward);
                SfxPlayer.Play(SfxId.HitHeavy, transform.position, 1f, 0.05f);
                SfxPlayer.Play(SfxId.GunshotHeavy, transform.position, 0.45f, 0.1f); // dumpfer Aufschlag
                EventBus<CameraImpulseEvent>.Raise(new CameraImpulseEvent(transform.position, Mathf.Max(0.5f, w.plunge.impact), 20f));
                if (hits > 0) HitStop.Request(w.plunge.hitStop);
                Slammed?.Invoke(transform.position, w.plungeRadius);
            }
            SetPhase(WeaponPhase.Recovery, _step.recovery);
        }

        private void AdvancePhase()
        {
            WeaponDefinition w = ActiveWeapon;
            switch (_phase)
            {
                case WeaponPhase.Windup:
                    SetPhase(WeaponPhase.Active, _step.active);
                    if (_kind == AttackKind.Plunge)
                    {
                        // Jetzt nach unten stossen; Treffer erst bei der Landung.
                        if (IsAirborne) _plungeFalling = true;
                        else OnLanded(0f);
                        SfxPlayer.Play(SfxId.SwingHeavy, transform.position + Vector3.up, 0.9f);
                    }
                    else if (w != null)
                    {
                        if (w.IsMelee) PerformMelee(w);
                        else if (w.IsProjectile) PerformProjectile(w);
                        else PerformShot(w);
                    }
                    break;

                case WeaponPhase.Active:
                    SetPhase(WeaponPhase.Recovery, _step.recovery);
                    break;

                case WeaponPhase.Recovery:
                    _lastAttackEndTime = Time.time;
                    if (_kind == AttackKind.Combo && w != null && w.combo != null && w.combo.Length > 0)
                        _comboIndex = (_comboIndex + 1) % w.combo.Length;
                    SetPhase(WeaponPhase.Idle, 0f);
                    break;

                case WeaponPhase.Reloading:
                    if (w != null && w.UsesAmmo) _ammo[w] = w.magazineSize;
                    SetPhase(WeaponPhase.Idle, 0f);
                    break;

                case WeaponPhase.Swapping:
                    SetPhase(WeaponPhase.Idle, 0f);
                    break;

                default:
                    SetPhase(WeaponPhase.Idle, 0f);
                    break;
            }
        }

        private void PerformMelee(WeaponDefinition main)
        {
            WeaponDefinition w = StrikeWeapon(main, offhandWeapon, _kind, _comboIndex, out float scale);
            var req = new AttackRequest
            {
                Attacker = gameObject,
                Origin = transform.position + Vector3.up * meleeOriginHeight,
                Forward = transform.forward,
                Weapon = w,
                Step = _step,
                LocalPlayer = !RemoteAuthority,
                PredictOnly = RemoteAuthority,
                DamageScale = scale,
                // Max 03.10.2026: Standardangriffe (Schlagfolge, Luftschlag) treffen nur ein Ziel
                SingleTarget = _kind == AttackKind.Combo || _kind == AttackKind.Air,
                PreferredTarget = _targeting != null && _targeting.Current != null ? _targeting.Current.gameObject : null
            };
            int hits = CombatResolver.ResolveMelee(req, _hits);
            if (RemoteAuthority) _authority.SubmitMelee((AttackIntentKind)_kind, _comboIndex, req.Origin, req.Forward);

            bool heavy = _stepIsSpecial || _step.damageMultiplier >= 1.5f;
            SfxPlayer.Play(w.attackSound, heavy ? SfxId.SwingHeavy : SfxId.Swing, req.Origin, 0.8f);
            if (hits > 0)
            {
                SfxPlayer.Play(w.hitSound, heavy ? SfxId.HitHeavy : SfxId.HitStraw, _hits[0].Point, 1f);
                HitStop.Request(_step.hitStop);
                if (_step.impact > 0f)
                    EventBus<CameraImpulseEvent>.Raise(new CameraImpulseEvent(req.Origin, _step.impact, 12f));
            }
            MeleeStruck?.Invoke(_hits);
        }

        private void PerformShot(WeaponDefinition w)
        {
            if (w.UsesAmmo)
            {
                int ammo = Ammo;
                if (ammo <= 0) return;
                _ammo[w] = ammo - 1;
            }

            Vector3 muzzle = transform.position + Vector3.up * muzzleHeight + transform.forward * muzzleForward;
            Vector3 aimDir = ComputeAimDirection(w, muzzle);

            _directions.Clear();
            int pellets = w.archetype == WeaponArchetype.Spread ? Mathf.Max(1, w.pellets) : 1;
            for (int i = 0; i < pellets; i++)
            {
                if (pellets == 1 || w.spreadAngle <= 0f) _directions.Add(aimDir);
                else
                {
                    Vector2 r = UnityEngine.Random.insideUnitCircle * Mathf.Tan(w.spreadAngle * Mathf.Deg2Rad);
                    Quaternion basis = Quaternion.LookRotation(aimDir);
                    _directions.Add((basis * new Vector3(r.x, r.y, 1f)).normalized);
                }
            }

            var req = new AttackRequest
            {
                Attacker = gameObject,
                Origin = muzzle,
                Forward = aimDir,
                Weapon = w,
                Step = _step,
                LocalPlayer = !RemoteAuthority,
                PredictOnly = RemoteAuthority
            };
            int hits = CombatResolver.ResolveShot(req, _directions, _hits);
            if (RemoteAuthority) _authority.SubmitShot((AttackIntentKind)_kind, _comboIndex, muzzle, _directions);

            bool heavy = _stepIsSpecial || pellets > 1;
            SfxPlayer.Play(w.attackSound, heavy ? SfxId.GunshotHeavy : SfxId.Gunshot, muzzle, 1f, 0.05f);
            if (hits > 0)
            {
                foreach (HitResult h in _hits)
                    if (h.HitTarget) { SfxPlayer.Play(w.hitSound, SfxId.HitStraw, h.Point, 0.6f); break; }
                HitStop.Request(_step.hitStop);
            }
            EventBus<CameraImpulseEvent>.Raise(new CameraImpulseEvent(muzzle, Mathf.Max(0.12f, _step.impact), 6f));
            Fired?.Invoke(muzzle, _hits);
            // Leeres Magazin: Beim naechsten Angriffsversuch wird automatisch nachgeladen.
        }

        /// <summary>
        /// Pfeil/Bolzen/Zauber loslassen. Mit Ziel wie in WoW (fliegt sicher hin, Wurf beim Eintreffen), ohne Ziel
        /// frei in Blickrichtung der Kamera mit Flugbahn. Der Server verbraucht die Munition und rechnet den Schaden.
        /// </summary>
        private void PerformProjectile(WeaponDefinition w)
        {
            if (w.ammoType != AmmoType.None && _supply != null && _supply.AmmoCount(w.ammoType) <= 0)
            {
                Error(w.ammoType == AmmoType.Bolt ? "Keine Bolzen" : "Keine Pfeile");
                _autoAttack = false;
                return;
            }
            Vector3 muzzle = transform.position + Vector3.up * muzzleHeight + transform.forward * muzzleForward;
            CombatTarget target = _targeting.Current;
            bool targeted = target != null && target.IsAttackable;
            Vector3 dir = ComputeAimDirection(w, muzzle);
            if (!targeted && w.projectileGravity > 0f && !w.IsSpell) dir = Loft(w, muzzle, dir);

            var req = new AttackRequest
            {
                Attacker = gameObject,
                Origin = muzzle,
                Forward = dir,
                Weapon = w,
                Step = _step,
                LocalPlayer = !RemoteAuthority,
                PredictOnly = RemoteAuthority,
                BonusDamage = AmmoBonus(w, _supply)
            };
            ProjectileSystem.Launch(new ProjectileLaunch
            {
                Request = req,
                Origin = muzzle,
                Direction = dir,
                Target = targeted ? target : null,
                Resolve = !RemoteAuthority,
                Visual = true,
                Look = LookOf(w),
                Color = w.effectColor
            });
            if (RemoteAuthority) _authority.SubmitProjectile((AttackIntentKind)_kind, _comboIndex, muzzle, dir, targeted ? target : null);
            SfxPlayer.Play(w.attackSound, w.IsSpell ? SfxId.CastRelease : SfxId.BowRelease, muzzle, 0.8f, 0.05f);
            ProjectileLaunched?.Invoke(muzzle, dir, targeted);
        }

        /// <summary>WoW: Munitions-DPS × Waffentempo als Zusatzschaden je Schuss.</summary>
        public static float AmmoBonus(WeaponDefinition w, IRangedSupply supply) =>
            w == null || w.ammoType == AmmoType.None || supply == null ? 0f : supply.AmmoDps(w.ammoType) * w.attackSpeed;

        public static ProjectileLook LookOf(WeaponDefinition w) =>
            w != null && w.IsSpell ? ProjectileLook.Arcane : (w != null && w.ammoType == AmmoType.Bolt ? ProjectileLook.Bolt : ProjectileLook.Arrow);

        /// <summary>Freier Schuss: so weit anheben, dass der Pfeil den anvisierten Punkt im Bogen erreicht.</summary>
        private static Vector3 Loft(WeaponDefinition w, Vector3 from, Vector3 dir)
        {
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            float d = Mathf.Min(w.range, 60f);
            float v = w.projectileSpeed, g = w.projectileGravity;
            // kleiner Winkel: Anhebung ~ g*d / (2 v²)
            float lift = Mathf.Clamp(g * d / (2f * v * v), 0f, 0.35f);
            if (flat.sqrMagnitude < 1e-4f) return dir;
            return (dir + Vector3.up * lift).normalized;
        }

        /// <summary>
        /// Mit Ziel: auf den Zielpunkt. Ohne Ziel: dorthin, wohin die Kamera schaut
        /// (Fadenkreuz-frei, aber intuitiv), begrenzt auf einen Kegel vor dem Charakter.
        /// </summary>
        private Vector3 ComputeAimDirection(WeaponDefinition w, Vector3 muzzle)
        {
            CombatTarget target = _targeting.Current;
            if (target != null && target.IsAttackable) return (target.AimPoint - muzzle).normalized;

            Vector3 forward = transform.forward;
            if (_camera == null) return forward;

            Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int mask = CombatResolver.BlockMask | CombatResolver.TargetMask;
            Vector3 aimPoint = ray.origin + ray.direction * w.range;
            if (Physics.Raycast(ray, out RaycastHit hit, w.range + 20f, mask, QueryTriggerInteraction.Ignore) &&
                !hit.collider.transform.IsChildOf(transform))
                aimPoint = hit.point;

            Vector3 dir = (aimPoint - muzzle).normalized;
            if (Vector3.Angle(forward, dir) > freeAimCone) return forward;
            return dir;
        }

        // ---------------------------------------------------------------- Nachladen & Wechsel

        private void BeginReload(bool manual)
        {
            WeaponDefinition w = ActiveWeapon;
            if (w == null || !w.UsesAmmo) return;
            if (_phase == WeaponPhase.Reloading || _phase == WeaponPhase.Swapping) return;
            if (Ammo >= w.magazineSize)
            {
                if (manual) Error("Magazin ist voll");
                return;
            }
            if (_phase != WeaponPhase.Idle && _phase != WeaponPhase.Recovery) return;

            SetPhase(WeaponPhase.Reloading, w.reloadTime);
            if (RemoteAuthority) _authority.SubmitReload();
            SfxPlayer.Play(w.reloadSound, SfxId.Reload, transform.position + Vector3.up, 0.7f, 0.02f);
            ReloadStarted?.Invoke();
        }

        private bool _queuedSwap;

        private void BeginSwap()
        {
            if (meleeWeapon == null || rangedWeapon == null) { _queuedSwap = false; return; }
            // Mitten im Schlag: Wechsel direkt danach ausfuehren statt verwerfen
            if (_phase == WeaponPhase.Windup || _phase == WeaponPhase.Active) { _queuedSwap = true; return; }
            _queuedSwap = false;
            _usingMelee = !_usingMelee;
            _autoAttack = false;
            _queuedSingle = false;
            _queuedSpecial = false;
            _comboIndex = 0;
            SetPhase(WeaponPhase.Swapping, swapTime);
            SfxPlayer.Play(SfxId.WeaponDraw, transform.position + Vector3.up, 0.6f);
            WeaponChanged?.Invoke(ActiveWeapon);
        }

        // ---------------------------------------------------------------- Netzwerk-Abbild (fremde Spieler)

        /// <summary>true = Nahkampfwaffe aktiv.</summary>
        public bool UsingMelee => _usingMelee;

        /// <summary>Waffe eines fremden Spielers umschalten (nur Darstellung).</summary>
        public void ProxySetWeapon(bool melee, bool silent = false)
        {
            if (melee && meleeWeapon == null) return;
            if (!melee && rangedWeapon == null) return;
            if (_usingMelee == melee) return;
            _usingMelee = melee;
            _comboIndex = 0;
            if (!silent) SfxPlayer.Play(SfxId.WeaponDraw, transform.position + Vector3.up, 0.4f);
            WeaponChanged?.Invoke(ActiveWeapon);
        }

        /// <summary>Angriff eines fremden Spielers darstellen (Animation/Effekt, kein Schaden).</summary>
        public void ProxyAttack(AttackKind kind, int comboIndex)
        {
            WeaponDefinition w = ActiveWeapon;
            if (w == null) return;
            _kind = kind;
            switch (kind)
            {
                case AttackKind.Special: _step = w.special; break;
                case AttackKind.Air: _step = w.airAttack; break;
                case AttackKind.Plunge: _step = w.plunge; break;
                default:
                    _comboIndex = w.combo != null && w.combo.Length > 0 ? Mathf.Clamp(comboIndex, 0, w.combo.Length - 1) : 0;
                    _step = w.combo != null && w.combo.Length > 0 ? w.combo[_comboIndex] : w.special;
                    break;
            }
            _stepIsSpecial = kind == AttackKind.Special || kind == AttackKind.Plunge;
            AttackStarted?.Invoke(_step, _stepIsSpecial);
        }

        /// <summary>Welcher Angriffsschritt gehoert zu Art + Kombo-Index (Server nutzt das zur Pruefung).</summary>
        public static AttackStep StepFor(WeaponDefinition w, AttackKind kind, int comboIndex)
        {
            switch (kind)
            {
                case AttackKind.Special: return w.special;
                case AttackKind.Air: return w.airAttack;
                case AttackKind.Plunge: return w.plunge;
                default:
                    if (w.combo == null || w.combo.Length == 0) return w.special;
                    return w.combo[Mathf.Clamp(comboIndex, 0, w.combo.Length - 1)];
            }
        }

        /// <summary>Nahkampftreffer eines fremden Spielers darstellen (Funken, Klang).</summary>
        public void ProxyMeleeStruck(IReadOnlyList<HitResult> hits)
        {
            WeaponDefinition w = ActiveWeapon;
            if (hits != null && hits.Count > 0)
                SfxPlayer.Play(w != null ? w.hitSound : null, SfxId.HitStraw, hits[0].Point, 0.7f);
            _hits.Clear();
            if (hits != null) _hits.AddRange(hits);
            MeleeStruck?.Invoke(_hits);
        }

        /// <summary>Schuss eines fremden Spielers darstellen (Muendungsfeuer, Leuchtspur).</summary>
        public void ProxyFired(Vector3 muzzle, IReadOnlyList<HitResult> hits)
        {
            WeaponDefinition w = ActiveWeapon;
            SfxPlayer.Play(w != null ? w.attackSound : null, SfxId.Gunshot, muzzle, 0.8f, 0.05f);
            _hits.Clear();
            if (hits != null) _hits.AddRange(hits);
            Fired?.Invoke(muzzle, _hits);
        }

        /// <summary>Pfeil/Zauber eines fremden Spielers darstellen (nur Bild, kein Schaden).</summary>
        public void ProxyProjectile(Vector3 origin, Vector3 dir, CombatTarget target)
        {
            WeaponDefinition w = ActiveWeapon;
            if (w == null || !w.IsProjectile) w = rangedWeapon != null && rangedWeapon.IsProjectile ? rangedWeapon : meleeWeapon;
            if (w == null) return;
            ProjectileSystem.Launch(new ProjectileLaunch
            {
                Request = new AttackRequest { Attacker = gameObject, Origin = origin, Forward = dir, Weapon = w, PredictOnly = true },
                Origin = origin,
                Direction = dir,
                Target = target,
                Resolve = false,
                Visual = true,
                Look = LookOf(w),
                Color = w.effectColor
            });
            SfxPlayer.Play(w.attackSound, w.IsSpell ? SfxId.CastRelease : SfxId.BowRelease, origin, 0.6f, 0.05f);
            ProjectileLaunched?.Invoke(origin, dir, target != null);
        }

        /// <summary>Sturzangriff eines fremden Spielers darstellen (Schockwelle).</summary>
        public void ProxySlammed(Vector3 position, float radius)
        {
            SfxPlayer.Play(SfxId.HitHeavy, position, 0.8f, 0.05f);
            Slammed?.Invoke(position, radius);
        }

        /// <summary>Nachladen eines fremden Spielers darstellen.</summary>
        public void ProxyReload()
        {
            WeaponDefinition w = ActiveWeapon;
            if (w != null) SfxPlayer.Play(w.reloadSound, SfxId.Reload, transform.position + Vector3.up, 0.5f, 0.02f);
            ReloadStarted?.Invoke();
        }

        private void SetPhase(WeaponPhase phase, float duration)
        {
            _phase = phase;
            _phaseDuration = Mathf.Max(0f, duration);
            _phaseTimer = _phaseDuration;
        }

        // ---------------------------------------------------------------- Hilfen

        /// <summary>
        /// KAMPF-03 (21.10.): Rest-Zuschlag der Nahkampfreichweite – Gegnerposition auf dem Client interpoliert, Ursprung
        /// vom Client gemeldet. Klein halten: sonst wieder Schwuenge ins Leere am Rand.
        /// </summary>
        private const float MeleeRangeSlack = 0.15f;
        private static readonly List<Collider> BodyColliderBuffer = new List<Collider>();

        private bool InRange(WeaponDefinition w, CombatTarget target)
        {
            if (w.IsMelee)
            {
                // KAMPF-03 (21.10.): Nahkampf wie der Treffer messen (CombatResolver.ResolveMelee: Kugel mit Waffenreichweite um
                // den Schlagursprung gegen den echten Koerper-Collider) statt am Auswahlring + 0,4. Vorher startete der
                // Autoangriff gegen einen Menschen bis ~3,6 m Mitte zu Mitte, traf aber nur bis ~2,75 m – Schwung ohne Zahl,
                // ohne „Ziel ausser Reichweite“, und ein vorgemerkter Heldenhafter Schlag verfiel.
                Collider body = BodyCollider(target);
                if (body is CapsuleCollider || body is SphereCollider || body is BoxCollider)
                {
                    Vector3 origin = transform.position + Vector3.up * meleeOriginHeight;
                    return Vector3.Distance(origin, body.ClosestPoint(origin)) <= w.range + MeleeRangeSlack;
                }
                // andere Collider (Charakter-Controller, Mesh): waagerecht bis zur Koerperkante
                Vector3 ma = transform.position, mb = target.transform.position;
                ma.y = 0f;
                mb.y = 0f;
                float bodyRadius = body != null ? Mathf.Min(body.bounds.extents.x, body.bounds.extents.z) : 0.35f;
                return Vector3.Distance(ma, mb) - bodyRadius <= w.range + MeleeRangeSlack;
            }
            Vector3 a = transform.position;
            Vector3 b = target.transform.position;
            a.y = 0f;
            b.y = 0f;
            float dist = Vector3.Distance(a, b) - target.SelectionRadius;
            return dist <= w.range;
        }

        /// <summary>Koerper-Collider des Ziels (der feste, kein Trigger – Trefferzonen sind Trigger); null = keiner.</summary>
        private static Collider BodyCollider(CombatTarget target)
        {
            Collider found = null;
            target.GetComponents(BodyColliderBuffer);
            foreach (Collider c in BodyColliderBuffer)
            {
                if (c != null && c.enabled && !c.isTrigger) { found = c; break; }
            }
            BodyColliderBuffer.Clear();
            return found;
        }

        private bool TooClose(WeaponDefinition w, CombatTarget target)
        {
            if (w.minRange <= 0f) return false;
            Vector3 a = transform.position, b = target.transform.position;
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b) - target.SelectionRadius < w.minRange;
        }

        private float FacingError(CombatTarget target)
        {
            Vector3 to = target.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return 0f;
            return Vector3.Angle(transform.forward, to);
        }

        private void Error(string message)
        {
            if (Time.unscaledTime < _nextErrorTime) return;
            _nextErrorTime = Time.unscaledTime + 1f;
            CombatTextEvent.Error(message);
        }
    }
}
