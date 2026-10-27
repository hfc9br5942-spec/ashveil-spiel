using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// Einbahnstrasse vom Spielzustand zum Animator. Gameplay liest nie aus dem Animator.
    ///
    /// Parameter (fehlende werden still ignoriert):
    ///   float   Speed, MoveX, MoveZ, VerticalSpeed, JumpProgress
    ///   bool    Grounded, Sprinting, Walking, Dead
    ///   int     WeaponType (0 keine, 1 Nahkampf einhaendig, 2 Pistole), AttackIndex
    ///   trigger Jump, Land, HardLand, Attack, Special, Reload, Hit, Dodge, AirAttack, Plunge
    ///
    /// Zusaetzlich dreht die Bridge das Modell beim Seitwaerts- und Rueckwaertslaufen leicht in
    /// Laufrichtung, weil die Standard-Animationsbibliothek keine eigenen Strafe-Animationen hat.
    /// Der Oberkoerper-Layer (Waffen) bleibt dabei auf das Ziel gerichtet.
    /// </summary>
    public sealed class CharacterAnimatorBridge : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int MoveXId = Animator.StringToHash("MoveX");
        private static readonly int MoveZId = Animator.StringToHash("MoveZ");
        private static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");
        private static readonly int JumpProgressId = Animator.StringToHash("JumpProgress");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int SprintingId = Animator.StringToHash("Sprinting");
        private static readonly int WalkingId = Animator.StringToHash("Walking");
        private static readonly int EpicWalkId = Animator.StringToHash("EpicWalk");
        private static readonly int DeadId = Animator.StringToHash("Dead");
        private static readonly int WeaponTypeId = Animator.StringToHash("WeaponType");
        private static readonly int AttackIndexId = Animator.StringToHash("AttackIndex");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int LandId = Animator.StringToHash("Land");
        private static readonly int HardLandId = Animator.StringToHash("HardLand");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int SpecialId = Animator.StringToHash("Special");
        private static readonly int ReloadId = Animator.StringToHash("Reload");
        private static readonly int HitId = Animator.StringToHash("Hit");
        private static readonly int DodgeId = Animator.StringToHash("Dodge");
        private static readonly int AirAttackId = Animator.StringToHash("AirAttack");
        private static readonly int PlungeId = Animator.StringToHash("Plunge");
        private static readonly int SwimmingId = Animator.StringToHash("Swimming");

        [SerializeField] private CharacterMotor motor;
        [SerializeField] private Animator animator;
        [SerializeField] private float dampTime = 0.08f;

        [Header("Modell in Laufrichtung drehen (fehlende Strafe-Animationen)")]
        [SerializeField] private bool orientToMovement = true;
        [SerializeField] private float maxSideTurn = 70f;
        [SerializeField] private float maxBackTurn = 40f;
        [SerializeField] private float orientSharpness = 10f;

        private readonly HashSet<int> _parameters = new HashSet<int>();
        private RuntimeAnimatorController _cachedController;
        private WeaponUser _weapons;
        private Health _health;
        private float _modelYaw;
        // 23.10.2026 (Max: vorhandene Magier-Clips nutzen): Wirkzeit-Zauber → Spell_Simple-Clips im Animator (Casting/CastRelease).
        // Der Wirkzustand ist nur auf dem eigenen Rechner bekannt (SpellClient, TargetRpc) – andere Spieler sehen ihn erst mit Netzereignis.
        private static readonly int CastingId = Animator.StringToHash("Casting");
        private static readonly int CastReleaseId = Animator.StringToHash("CastRelease");
        private PlayerCharacter _lokal;
        private bool _wirkt;
        private float _wirkEnde;

        /// <summary>Wirken anzeigen (eigene Figur aus SpellClient; Lab/Netz später direkt).</summary>
        public void SetCasting(bool wirkt, bool ausgeloest)
        {
            if (!IsReady() || wirkt == _wirkt) return;
            _wirkt = wirkt;
            SetBool(CastingId, wirkt);
            if (!wirkt && ausgeloest) SetTrigger(CastReleaseId);
        }

        private void WirkenAktualisieren()
        {
            if (_lokal == null || !_lokal.isActiveAndEnabled) return;
            bool wirkt = Ashveil.Gameplay.Abilities.SpellClient.CastingSpell.Length > 0 && Ashveil.Gameplay.Abilities.SpellClient.CastEnd > 0f;
            if (wirkt) _wirkEnde = Ashveil.Gameplay.Abilities.SpellClient.CastEnd;
            // Ende zur geplanten Zeit = ausgelöst (Shoot); vorher = abgebrochen (Exit)
            if (wirkt != _wirkt) SetCasting(wirkt, !wirkt && Time.time >= _wirkEnde - 0.2f);
        }

        public Animator Animator => animator;

        /// <summary>Das Motion-System (CharacterMotionPoser) richtet das Modell selbst aus – dann hier nicht drehen.</summary>
        public bool ExternalOrientation { get; set; }

        private void Awake()
        {
            if (motor == null) motor = GetComponentInParent<CharacterMotor>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _weapons = GetComponentInParent<WeaponUser>();
            _health = GetComponentInParent<Health>();
            _lokal = GetComponentInParent<PlayerCharacter>();
            // Motion-System (Phase M1: liest nur mit, Ausgabe bleibt hier)
            if (GetComponent<Ashveil.Gameplay.Motion.CharacterMotionOrchestrator>() == null)
                gameObject.AddComponent<Ashveil.Gameplay.Motion.CharacterMotionOrchestrator>();
        }

        private void OnEnable()
        {
            if (motor != null)
            {
                motor.Jumped += OnJumped;
                motor.Landed += OnLanded;
                motor.Dodged += OnDodged;
            }
            if (_weapons != null)
            {
                _weapons.AttackStarted += OnAttackStarted;
                _weapons.WeaponChanged += OnWeaponChanged;
                _weapons.ReloadStarted += OnReloadStarted;
            }
            if (_health != null)
            {
                _health.Damaged += OnDamaged;
                _health.Died += OnDied;
                _health.Changed += OnHealthChanged;
            }
        }

        private void OnDisable()
        {
            if (motor != null)
            {
                motor.Jumped -= OnJumped;
                motor.Landed -= OnLanded;
                motor.Dodged -= OnDodged;
            }
            if (_weapons != null)
            {
                _weapons.AttackStarted -= OnAttackStarted;
                _weapons.WeaponChanged -= OnWeaponChanged;
                _weapons.ReloadStarted -= OnReloadStarted;
            }
            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
                _health.Died -= OnDied;
                _health.Changed -= OnHealthChanged;
            }
        }

        /// <summary>Anderes Modell (z. B. maennlich/weiblich) uebernehmen.</summary>
        public void SetAnimator(Animator newAnimator)
        {
            animator = newAnimator;
            _cachedController = null;
            _modelYaw = 0f;
            if (IsReady() && _weapons != null) SetInt(WeaponTypeId, WeaponTypeOf(_weapons.ActiveWeapon));
        }

        private void LateUpdate()
        {
            if (!IsReady()) return;
            WirkenAktualisieren();
            float dt = Time.deltaTime;
            MotorState state = motor.State;
            float runSpeed = Mathf.Max(0.01f, motor.Settings.runSpeed);
            Vector3 local = motor.LocalVelocity;

            SetFloat(SpeedId, motor.HorizontalSpeed, dt);
            SetFloat(MoveXId, local.x / runSpeed, dt);
            SetFloat(MoveZId, local.z / runSpeed, dt);
            // Schwimmen (MOV-169, Greybox ohne eigene Clips): im Wasser Fortbewegung statt Fallschleife; ein Controller
            // mit dem Parameter „Swimming“ kann eigene Schwimmzustände zeigen (Cascadeur-Clips, Animationsbedarf v3 §6)
            bool swimming = state.Swimming;
            // 22.10. nachts (Max: „der Char hüpft ständig bzw. fällt zwischendurch – ist das ein Bug?“; Lab: Becken sackte beim
            // Laufen an Bildern mit langer Bildzeit 11–20 cm ab und sprang zurück): ein einzelnes Bild ohne Bodenkontakt (Kante,
            // Ruckler, Hang) schaltete den Animator sofort auf Fallen/Landen. Jetzt Bodenkulanz 0,15 s – ein echter Sprung
            // (Steiggeschwindigkeit) oder echtes Fallen (laenger in der Luft) schaltet weiterhin sofort bzw. nach 0,15 s um.
            bool kulanz = !state.Grounded && !swimming && state.AirTime < 0.15f && state.Velocity.y <= 0.5f;
            SetFloat(VerticalSpeedId, swimming || kulanz ? 0f : state.Velocity.y, 0f);
            SetFloat(JumpProgressId, swimming || kulanz ? 0f : JumpProgress(state), 0f);
            SetBool(GroundedId, state.Grounded || swimming || kulanz);
            SetBool(SwimmingId, swimming);
            SetBool(SprintingId, state.IsSprinting);
            SetBool(WalkingId, state.IsWalking);
            SetBool(EpicWalkId, state.IsEpicWalking);

            if (!ExternalOrientation) OrientModel(local, dt);
        }

        /// <summary>
        /// Position in der Sprunganimation (0..1), abgeleitet aus der Vertikalgeschwindigkeit:
        /// Absprung = Anfang, Scheitelpunkt = Mitte, Landung = Ende. Laengere Stuerze bleiben am Ende
        /// (Beine zur Landung gestreckt). Der erste Frame (Hocke vor dem Absprung) wird uebersprungen.
        /// </summary>
        private float JumpProgress(in MotorState state)
        {
            float v0 = Mathf.Max(0.1f, motor.Settings.JumpVelocity);
            float t = Mathf.Clamp01((v0 - state.Velocity.y) / (2f * v0));
            return Mathf.Lerp(JumpStartSkip, 1f, t);
        }

        /// <summary>Anteil von Jump_Start, der die Hocke vor dem Absprung zeigt (aus der FBX gemessen).</summary>
        private const float JumpStartSkip = 0.045f;

        private void OrientModel(Vector3 local, float dt)
        {
            if (!orientToMovement || animator == null) return;
            float target = 0f;
            float speed = new Vector2(local.x, local.z).magnitude;
            bool attacking = _weapons != null && (_weapons.Phase == WeaponPhase.Windup || _weapons.Phase == WeaponPhase.Active);
            if (motor.IsDodging)
            {
                // Rolle: Modell schaut voll in Rollrichtung (auch seitlich/rueckwaerts)
                Vector3 d = Quaternion.Euler(0f, -motor.State.Yaw, 0f) * motor.State.DodgeDirection;
                target = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                _modelYaw = Mathf.LerpAngle(_modelYaw, target, 1f - Mathf.Exp(-orientSharpness * 3f * dt));
                animator.transform.localRotation = Quaternion.Euler(0f, _modelYaw, 0f);
                return;
            }
            if (speed > 0.5f && !attacking)
            {
                if (local.z >= -0.2f) target = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -maxSideTurn, maxSideTurn);
                else target = Mathf.Clamp(Mathf.Atan2(-local.x, -local.z) * Mathf.Rad2Deg, -maxBackTurn, maxBackTurn);
            }
            _modelYaw = Mathf.LerpAngle(_modelYaw, target, 1f - Mathf.Exp(-orientSharpness * dt));
            animator.transform.localRotation = Quaternion.Euler(0f, _modelYaw, 0f);
        }

        // ---------------------------------------------------------------- Ereignisse

        private void OnJumped()
        {
            if (IsReady()) SetTrigger(JumpId);
        }

        private void OnLanded(float impact)
        {
            if (!IsReady()) return;
            SetTrigger(impact >= motor.Settings.hardLandingSpeed ? HardLandId : LandId);
        }

        /// <summary>Selbst gebaute Bewegungen am Koerper vorhanden (dann keine Fremd-Clips fuer Angriffe/Ausweichen)?</summary>
        private bool Choreographed => animator != null && Ashveil.Gameplay.Motion.Procedural.CombatChoreographer.GlobalEnabled &&
                                      animator.GetComponent<Ashveil.Gameplay.Motion.Procedural.CombatChoreographer>() != null;

        private void OnAttackStarted(AttackStep step, bool special)
        {
            if (!IsReady()) return;
            SetInt(AttackIndexId, _weapons != null ? _weapons.CurrentComboIndex : 0);
            AttackKind kind = _weapons != null ? _weapons.CurrentKind : (special ? AttackKind.Special : AttackKind.Combo);
            // Kampfumbau: Nahkampf, Stab und Bogen fuehrt die Choreografie – kein Clip
            if (Choreographed && Ashveil.Gameplay.Motion.Procedural.CombatChoreographer.Handles(_weapons != null ? _weapons.ActiveWeapon : null, kind)) return;
            switch (kind)
            {
                case AttackKind.Air: SetTrigger(AirAttackId); break;
                case AttackKind.Plunge: SetTrigger(PlungeId); break;
                case AttackKind.Special: SetTrigger(SpecialId); break;
                default: SetTrigger(AttackId); break;
            }
        }

        private void OnDodged(Vector3 direction)
        {
            if (!IsReady()) return;
            // Angefangene Schlaege abbrechen, damit nach der Rolle kein "leerer" Hieb nachlaeuft.
            if (_parameters.Contains(AttackId)) animator.ResetTrigger(AttackId);
            if (_parameters.Contains(SpecialId)) animator.ResetTrigger(SpecialId);
            int upper = animator.GetLayerIndex("UpperBody");
            if (upper >= 0) animator.CrossFade("Empty", 0.1f, upper);
            // Eigene Ausweichbewegung (Rolle/Satz/Wirbelschritt) statt Rollen-Clip
            if (!Choreographed) SetTrigger(DodgeId);
        }

        private void OnReloadStarted()
        {
            if (IsReady()) SetTrigger(ReloadId);
        }

        private void OnWeaponChanged(WeaponDefinition w)
        {
            if (IsReady()) SetInt(WeaponTypeId, WeaponTypeOf(w));
        }

        private void OnDamaged(DamageInfo info)
        {
            // Mit dem Schild geblockt: die Choreografie zeigt den Schildstoss statt des Treffer-Clips.
            // 22.10.2026 (Kampfreaktionen): auch Treffer einstecken zeigt die Choreografie (gerichtete Ganzkörperreaktion) –
            // der eine Hit_Chest-Clip würde dieselben Knochen gegen sie schreiben
            if (Choreographed) return;
            if (info.Dodged) return;
            if (IsReady()) SetTrigger(HitId);
        }

        private void OnDied()
        {
            if (IsReady()) SetBool(DeadId, true);
        }

        /// <summary>Wiederbelebt: Todesanimation verlassen.</summary>
        private void OnHealthChanged()
        {
            if (_health != null && _health.IsAlive && IsReady()) SetBool(DeadId, false);
        }

        private static int WeaponTypeOf(WeaponDefinition w)
        {
            if (w == null) return 0;
            if (w.weaponClass == WeaponClass.Bow || w.weaponClass == WeaponClass.Crossbow) return 0; // Bogen fuehrt die Choreografie (keine Pistolen-Halteanimation)
            return w.HeldLikeMelee ? 1 : 2;
        }

        // ---------------------------------------------------------------- Parameter-Helfer

        private bool IsReady()
        {
            if (motor == null || animator == null || animator.runtimeAnimatorController == null) return false;
            if (_cachedController != animator.runtimeAnimatorController || (_parameters.Count == 0 && animator.isActiveAndEnabled))
            {
                _cachedController = animator.runtimeAnimatorController;
                _parameters.Clear();
                foreach (AnimatorControllerParameter p in animator.parameters) _parameters.Add(p.nameHash);
            }
            return true;
        }

        private void SetFloat(int id, float value, float dt)
        {
            if (!_parameters.Contains(id)) return;
            if (dt > 0f) animator.SetFloat(id, value, dampTime, dt);
            else animator.SetFloat(id, value);
        }

        private void SetBool(int id, bool value)
        {
            if (_parameters.Contains(id)) animator.SetBool(id, value);
        }

        private void SetInt(int id, int value)
        {
            if (_parameters.Contains(id)) animator.SetInteger(id, value);
        }

        private void SetTrigger(int id)
        {
            if (_parameters.Contains(id)) animator.SetTrigger(id);
        }
    }
}
