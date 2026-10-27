using System;
using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Data.Motion;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Gameplay.Motion
{
    /// <summary>
    /// Ein Einstiegspunkt je Figur fuer das Motion-System (§4). Entscheidet keine Spielregeln: liest den Zustand,
    /// baut den <see cref="MotionContext"/>, loest die Bewegungs-Schichten auf und waehlt Varianten.
    ///
    /// Phase M1: nur Lesen und Waehlen – die Ausgabe an Animator/Rig uebernehmen weiterhin
    /// <c>CharacterAnimatorBridge</c> und <c>StancePoser</c>. Sichtbar aendert sich nichts; Debug-Anzeige und Tests
    /// sehen bereits Kontext, Haltung, Gangband und gewaehlte Angriffsvariante.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public sealed class CharacterMotionOrchestrator : MonoBehaviour
    {
        private static readonly List<CharacterMotionOrchestrator> AllList = new List<CharacterMotionOrchestrator>();
        public static IReadOnlyList<CharacterMotionOrchestrator> All => AllList;

        [Tooltip("Sekunden ohne Kampf, bis aus „Kampf“ wieder „wachsam“ wird (danach entscheidet die Klasse ueber das Wegstecken).")]
        [SerializeField] private float combatToAlertDelay = 1.5f;

        private readonly MotionContextBuilder _builder = new MotionContextBuilder();
        private readonly ContextualAttackSelector _selector = new ContextualAttackSelector();
        private MotionContext _context;
        private ResolvedMotionTuning _tuning;
        private int _resolvedHash;
        private bool _bound;
        private uint _attackCounter;
        private uint _seedBase;
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private bool _identityDirty = true;

        /// <summary>Motion-Lab/Tests: Identitaet von Hand festlegen (Rasse, Koerper, Klasse, Ruestung, Waffe – §107).</summary>
        public struct IdentityOverride
        {
            public bool Active;
            public string RaceId;
            public BodyMotionVariant Body;
            public ClassRole Role;
            public ArmorCategory Armor;
            public WeaponFamily Weapon;
        }

        private IdentityOverride _override;
        public IdentityOverride Override => _override;

        public void SetOverride(IdentityOverride o)
        {
            _override = o;
            _identityDirty = true;
        }

        public MotionContext Context => _context;
        /// <summary>Sichtbare Ausruestung (Scheiden, Nebenhand-Modell) – kann fehlen (Gegner, Lab ohne Ausruestung).</summary>
        public Ashveil.Gameplay.Visuals.EquipmentVisuals Visuals => _builder.Visuals;
        public ResolvedMotionTuning Tuning => _tuning;
        public ContextualAttackSelector Selector => _selector;

        /// <summary>Zuletzt gewaehlte Angriffsbewegung (fuer Debug/Tests/Netz).</summary>
        public AttackChoice LastAttack { get; private set; }
        public uint LastAttackSeed { get; private set; }

        /// <summary>Neue Angriffsvariante gewaehlt (spaeter: Netz-Ereignis und Abspielen).</summary>
        public event Action<AttackChoice, uint> AttackMotionChosen;
        /// <summary>Haltung gewechselt (Exploration/Alert/Combat).</summary>
        public event Action<MotionStance> StanceChanged;

        private void OnEnable()
        {
            AllList.Add(this);
            Bind();
        }

        private void OnDisable()
        {
            AllList.Remove(this);
            if (_builder.Weapons != null)
            {
                _builder.Weapons.AttackStarted -= OnAttackStarted;
                _builder.Weapons.WeaponChanged -= OnWeaponChanged;
            }
            if (_builder.Equipment != null) _builder.Equipment.Applied -= MarkIdentityDirty;
            if (_builder.Bodies != null) _builder.Bodies.BodyApplied -= OnBodyApplied;
            if (_builder.Visuals != null) _builder.Visuals.Rebuilt -= MarkIdentityDirty;
            _bound = false;
        }

        private void Bind()
        {
            if (_bound) return;
            _builder.Bind(gameObject);
            // Prozedurale Koerperschicht neben jede Standhaltung (je Koerpermodell eine)
            Transform root = _builder.Motor != null ? _builder.Motor.transform : transform;
            foreach (Ashveil.Gameplay.Characters.StancePoser sp in root.GetComponentsInChildren<Ashveil.Gameplay.Characters.StancePoser>(true))
            {
                if (sp.GetComponent<Procedural.CharacterMotionPoser>() == null) sp.gameObject.AddComponent<Procedural.CharacterMotionPoser>();
                if (sp.GetComponent<Procedural.WeaponHandPoser>() == null) sp.gameObject.AddComponent<Procedural.WeaponHandPoser>();
                // Kampfumbau: selbst gebaute Angriffe, Bogen, Zauber, Ausweichen, Schildblock, zweite Waffe
                if (sp.GetComponent<Procedural.CombatChoreographer>() == null) sp.gameObject.AddComponent<Procedural.CombatChoreographer>();
                // Letzte Schicht: automatische Traegheits-Ueberblendung – macht jeden Sprung zwischen Posen nahtlos
                if (sp.GetComponent<Procedural.AutoInertializer>() == null) sp.gameObject.AddComponent<Procedural.AutoInertializer>();
            }
            if (_builder.Weapons != null)
            {
                _builder.Weapons.AttackStarted += OnAttackStarted;
                _builder.Weapons.WeaponChanged += OnWeaponChanged;
            }
            // Identitaet nur bei Aenderungen neu lesen (§140) – nicht jeden Frame
            if (_builder.Equipment != null) _builder.Equipment.Applied += MarkIdentityDirty;
            if (_builder.Bodies != null) _builder.Bodies.BodyApplied += OnBodyApplied;
            if (_builder.Visuals != null) _builder.Visuals.Rebuilt += MarkIdentityDirty;
            _identityDirty = true;
            _seedBase = (uint)Mathf.Abs(transform.root.name.GetHashCode()) | 1u;
            _bound = true;
            _resolvedHash = int.MinValue;
        }

        private void LateUpdate()
        {
            if (!_bound) Bind();
            float dt = Time.deltaTime;
            if (_identityDirty)
            {
                _identityDirty = false;
                _builder.FillIdentity(ref _context);
                int hash = _builder.IdentityHash;
                if (_override.Active)
                {
                    if (!string.IsNullOrEmpty(_override.RaceId)) _context.RaceId = _override.RaceId;
                    _context.BodyVariant = _override.Body;
                    _context.ClassRole = _override.Role;
                    _context.Armor = _override.Armor;
                    _context.MainWeapon = _override.Weapon;
                    _context.HasShield = _override.Weapon == WeaponFamily.SwordShield;
                    hash = unchecked(hash * 31 + (_override.RaceId ?? "").GetHashCode() * 7 + (int)_override.Body * 131 + (int)_override.Role * 1031 +
                                     (int)_override.Armor * 8191 + (int)_override.Weapon * 65537 + 1);
                }
                if (hash != _resolvedHash) Resolve(hash);
            }
            _builder.FillDynamic(ref _context, dt, Time.time);
            UpdateStance();
        }

        public void MarkIdentityDirty() => _identityDirty = true;
        private void OnWeaponChanged(WeaponDefinition w) => _identityDirty = true;
        private void OnBodyApplied(int body) => _identityDirty = true;

        /// <summary>Schichten neu waehlen (Ausruestung, Klasse, Koerper, Rasse geaendert).</summary>
        public void Resolve() => Resolve(_builder.IdentityHash);

        private void Resolve(int hash)
        {
            _resolvedHash = hash;
            MotionDatabase db = MotionDatabase.Instance;
            if (db == null) { _tuning = default; return; }
            _tuning = db.Resolve(_context.RaceId, _context.BodyVariant, _context.ClassRole, _context.Armor, _context.MainWeapon);
            if (!string.IsNullOrEmpty(_tuning.fallbackNote)) WarnOnce($"Rueckfall fuer {_tuning.fallbackNote} ({_context.RaceId}, {_context.BodyVariant}, {_context.ClassRole}, {_context.Armor}, {_context.MainWeapon})");
        }

        // ---------------------------------------------------------------- Haltung (§6/§8)

        private MotionStance? _forcedStance;

        /// <summary>Motion-Lab/Tests: Haltung erzwingen (null = normal aus dem Spielzustand).</summary>
        public void ForceStance(MotionStance? stance) => _forcedStance = stance;

        private void UpdateStance()
        {
            MotionStance s;
            if (_forcedStance.HasValue) s = _forcedStance.Value;
            else if (_context.Dead) s = MotionStance.Exploration;
            else if (_context.InCombat || _context.Attacking || (_builder.Weapons != null && _builder.Weapons.IsAutoAttacking) ||
                     _context.SecondsSinceCombat < combatToAlertDelay) s = MotionStance.Combat;
            else
            {
                float alertHold = _tuning.classMotion != null ? _tuning.classMotion.autoSheatheDelay : 6f;
                s = _context.SecondsSinceCombat < combatToAlertDelay + alertHold ? MotionStance.Alert : MotionStance.Exploration;
            }
            // Waffe gilt als gezogen, solange Kampf oder Wachsamkeit laeuft (Zieh-/Wegsteck-Bewegung folgt in M5)
            _context.WeaponDrawn = s != MotionStance.Exploration;
            if (s == _context.Stance) return;
            _context.Stance = s;
            StanceChanged?.Invoke(s);
        }

        // ---------------------------------------------------------------- Angriffe (§26–§29)

        private void OnAttackStarted(AttackStep step, bool special)
        {
            WeaponUser w = _builder.Weapons;
            AttackKind kind = w != null ? w.CurrentKind : AttackKind.Combo;
            if (kind == AttackKind.Air || kind == AttackKind.Plunge) return; // eigene Bewegungen
            _attackCounter++;
            uint seed = MotionRandom.Mix(_seedBase, _attackCounter);
            AttackChoice choice = ChooseAttack(special, seed);
            LastAttack = choice;
            LastAttackSeed = seed;
            if (choice.Valid) _selector.Remember(choice.Definition);
            else if (_tuning.weapon != null) WarnOnce($"Keine Angriffsbewegung fuer {_context.MainWeapon}: {choice.Note}");
            AttackMotionChosen?.Invoke(choice, seed);
        }

        /// <summary>Angriffsvariante fuer die aktuelle Lage waehlen (auch fuer Tests/Motion-Lab).</summary>
        public AttackChoice ChooseAttack(bool ability, uint seed)
        {
            WeaponMotionProfile wp = _tuning.weapon;
            List<AttackMotionDefinition> list = wp != null ? wp.attacks : null;
            while (wp != null && (list == null || list.Count == 0) && wp.fallback != null && wp.fallback != wp)
            {
                wp = wp.fallback;
                list = wp.attacks;
                WarnOnce($"Angriffe von {_context.MainWeapon} fehlen – Rueckfall auf {wp.family}");
            }
            var q = new AttackQuery
            {
                IsAbility = ability,
                HasTarget = _context.HasTarget,
                TargetSize = _context.TargetSize,
                TargetDistance = _context.TargetDistance,
                TargetPointHeight = _context.TargetPointHeight,
                TargetSpeed = new Vector2(_context.TargetVelocity.x, _context.TargetVelocity.z).magnitude,
                BodyHeight = _context.BodyHeight,
                ClearLeft = 99f,
                ClearRight = 99f,
                Economy = _tuning.classMotion != null ? _tuning.classMotion.economyOfMotion : 0.5f,
                Seed = seed
            };
            return _selector.Select(list, q);
        }

        /// <summary>Fehlende/ersatzweise Bewegungen nie verschweigen (§143) – einmal je Fall, nur Editor/Entwickler-Build.</summary>
        private void WarnOnce(string msg)
        {
            if (!Debug.isDebugBuild && !Application.isEditor) return;
            if (!Warned.Add(msg)) return;
            Debug.LogWarning("[Ashveil] Motion: " + msg);
        }
    }
}
