using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// Koerperhaltung im Stand – pro Koerper (Mann/Frau) und Waffe eigene Posen aus einem <see cref="StanceProfile"/>.
    ///
    /// Laeuft nach dem Animator (LateUpdate) und legt die Haltung ueber die Idle-Animation:
    ///   Huefte verschieben/kippen · Wirbelsaeule, Schultern, Kopf · Fuesse per Bein-IK platzieren
    ///   Arme per IK (Schwert in den Boden stellen, Hand an die Huefte …) · Atmen, Gewicht verlagern, Umschauen
    ///
    /// Sobald sich die Figur bewegt, springt, ausweicht oder angreift, blendet die Haltung schnell aus und die
    /// normalen Animationen uebernehmen. Nach einem Kampf bleibt fuer kurze Zeit die kampfbereite Haltung
    /// (Arme aus der Waffenhalte-Animation, tieferer Stand).
    ///
    /// Alle Richtungen werden aus dem Modell selbst bestimmt (rechts/oben/vorn) – das funktioniert
    /// unabhaengig davon, wie die Knochenachsen im importierten Modell liegen.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public sealed class StancePoser : MonoBehaviour
    {
        [SerializeField] private StanceProfile profile;
        [SerializeField] private Animator animator;

        [Header("Ueberblenden")]
        [Tooltip("So lange still stehen, bevor die Haltung beginnt (s)")]
        [SerializeField] private float settleDelay = 0.12f;
        [SerializeField] private float blendInTime = 0.6f;
        [SerializeField] private float blendOutTime = 0.12f;
        [Tooltip("Wechsel zwischen entspannter und kampfbereiter Haltung (s)")]
        [SerializeField] private float readyBlendTime = 0.3f;

        private CharacterMotor _motor;
        private WeaponUser _weapons;
        private WeaponFx _fx;
        private Health _health;
        private EmotePoser _emote;

        private bool _bonesReady;
        private Transform _hips, _spine, _chest, _upperChest, _neck, _head;
        private readonly Limb _leftArm = new Limb(), _rightArm = new Limb();
        /// <summary>Locker hängender rechter Arm ohne Waffe in der Hand – Spiegelbild des linken Arms aus ApplyMaleDefaults.</summary>
        private static readonly StanceArm RechterArmLocker = new StanceArm
        {
            enabled = true, grip = new Vector3(0.25f, 0.79f, 0.04f), knuckles = new Vector3(-0.15f, 0.2f, 1f),
            palm = new Vector3(-1f, 0f, 0.1f), elbow = new Vector3(0.4f, 0f, -1f), fingerCurl = 18f
        };
        private readonly Limb _leftLeg = new Limb(), _rightLeg = new Limb();
        private Transform _lShoulder, _rShoulder;
        private readonly HandBones _lHand = new HandBones(), _rHand = new HandBones();

        private float _weight;
        private float _ready;
        private float _stillTime;
        private float _lastCombatTime = -999f;
        private float _time;
        private bool _lastMelee = true;

        private float _look, _lookVel, _lookTarget, _lookTimer = 4f;

        // Schwert aufstuetzen
        private Transform _sword;
        private Transform _swordParent;
        private Vector3 _swordLocalPos;
        private Quaternion _swordLocalRot;
        private bool _swordOverridden;
        private bool _swordMeasured;
        private float _swordTip = 0.925f;
        private float _swordPommel = -0.18f;

        private Vector3 _r, _u, _f;
        private Transform _model;

        /// <summary>Ruhiges Tempo: Atemzug und Gewichtsverlagerung (s).</summary>
        private const float BreathPeriod = 5.8f;
        private const float SwayPeriod = 10f;

        // Koerperoberflaeche (Rumpf) – aus dem Modell gemessen, damit Haende aufliegen statt durchzugleiten
        private bool _surfaceTried;
        private Transform[] _surfBones;
        private Vector3[] _surfLocal;
        private byte[] _surfBone;
        private Vector3[] _surfWorld;
        private float[] _surfHeight;
        private bool _surfaceValid;
        private const float SlabHalf = 0.045f;

        private sealed class Limb
        {
            public Transform upper, lower, end;
            public bool Valid => upper != null && lower != null && end != null;
        }

        private sealed class HandBones
        {
            public Transform index, little, middle, thumb;
            public Transform[] curl; // Paare: Glied, naechstes Glied
            public bool Valid => index != null && little != null;
        }

        public StanceProfile Profile { get => profile; set => profile = value; }

        /// <summary>0 = normale Animation, 1 = volle Standhaltung.</summary>
        public float Weight => _weight;

        /// <summary>Motion-System: Waffe steckt (Scheide/Holster) – dann keine Pose mit aufgestuetztem Schwert.</summary>
        public bool WeaponSheathed { get; set; }

        /// <summary>Von der Bewegungssteuerung gesetzt: Kampfhaltung (Waffe bereit) auch ohne eigenen Angriff/Treffer.</summary>
        public bool CombatReady { get; set; }

        /// <summary>So stark fuehrt die Standhaltung in diesem Bild den linken/rechten Arm (0–1) – die Bewegungsschicht
        /// laesst diese Arme dann in Ruhe (sonst zerren zwei Systeme am selben Arm).</summary>
        public float LeftArmOwn { get; private set; }
        public float RightArmOwn { get; private set; }

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            // Neuere mitgelieferte Standardwerte (anatomische Korrekturen) auch ohne neues Bauen der Figur uebernehmen
            if (profile != null && profile.defaultsVersion < StanceProfile.CurrentDefaultsVersion)
            {
                if (profile.name.Contains("Female")) profile.ApplyFemaleDefaults(); else profile.ApplyMaleDefaults();
            }
            _motor = GetComponentInParent<CharacterMotor>();
            _weapons = GetComponentInParent<WeaponUser>();
            _fx = GetComponentInParent<WeaponFx>();
            _health = GetComponentInParent<Health>();
            _emote = GetComponent<EmotePoser>();
            _lod = GetComponentInParent<Ashveil.Gameplay.Visuals.CharacterLod>();
        }

        private Ashveil.Gameplay.Visuals.CharacterLod _lod;

        private void OnEnable()
        {
            if (_weapons != null) _weapons.AttackStarted += OnAttackStarted;
            if (_health != null) _health.Damaged += OnDamaged;
            _weight = 0f;
            _stillTime = 0f;
        }

        private void OnDisable()
        {
            if (_weapons != null) _weapons.AttackStarted -= OnAttackStarted;
            if (_health != null) _health.Damaged -= OnDamaged;
            RestoreSword();
            _weight = 0f;
        }

        private void OnAttackStarted(AttackStep step, bool special) => _lastCombatTime = Time.time;
        private void OnDamaged(DamageInfo info) => _lastCombatTime = Time.time;

        // ================================================================== Ablauf

        /// <summary>18.10.2026 abends (Max: UAL pur, „alles archivieren“): Standhaltung aus – im Stand spielt der UAL-Idle-Clip
        /// unverändert. true schaltet die Haltungsschicht wieder ein.</summary>
        public static bool GlobalEnabled = false;

        private void LateUpdate()
        {
            LeftArmOwn = RightArmOwn = 0f;
            RestoreSword();
            if (!GlobalEnabled) { _weight = 0f; return; }
            if (profile == null || animator == null || !EnsureBones()) return;

            float dt = Time.deltaTime;
            _time += dt;

            WeaponDefinition active = _weapons != null ? _weapons.ActiveWeapon : null;
            bool melee = active == null || active.HeldLikeMelee;
            if (melee != _lastMelee)
            {
                // Waffe gewechselt: neue Haltung sauber neu einblenden
                _lastMelee = melee;
                _weight = 0f;
            }

            bool still = _motor == null ||
                         (_motor.State.Grounded && _motor.HorizontalSpeed < 0.25f && !_motor.IsDodging &&
                          _motor.State.LandingRecovery <= 0f);
            bool alive = _health == null || _health.IsAlive;
            bool weaponIdle = _weapons == null || _weapons.Phase == WeaponPhase.Idle;
            _stillTime = still ? _stillTime + dt : 0f;
            bool emoting = (_emote != null && _emote.Active) || (_motor != null && _motor.IsMounted); // M8: Emote / Reittier uebernimmt die Figur
            // Entfernte/vereinfachte Figuren (Figurendetail > 0) behalten die normale Animation – die Haltung kostet CPU
            bool detailed = _lod == null || _lod.Level == 0;
            // Aktion im Animator (Schlag, Treffer, Rolle, Tod – auch bei fremden Spielern) laesst der Haltung keinen Platz
            bool action = AnimatorActions.UpperBody(animator) || AnimatorActions.FullBody(animator);
            bool want = still && alive && weaponIdle && !emoting && !action && detailed && _stillTime >= settleDelay;
            _weight = Mathf.MoveTowards(_weight, want ? 1f : 0f, dt / Mathf.Max(0.01f, want ? blendInTime : blendOutTime));

            bool combat = CombatReady || (_weapons != null && (_weapons.IsAutoAttacking || !weaponIdle)) ||
                          Time.time - _lastCombatTime < profile.readyHoldTime;
            _ready = Mathf.MoveTowards(_ready, combat ? 1f : 0f, dt / Mathf.Max(0.01f, readyBlendTime));

            UpdateLookAround(dt);

            if (_weight <= 0.0001f) return;
            float w = Mathf.SmoothStep(0f, 1f, _weight);
            float r = Mathf.SmoothStep(0f, 1f, _ready);
            ApplyStance(profile.Get(melee, false), profile.Get(melee, true), r, w, melee);
        }

        private void UpdateLookAround(float dt)
        {
            _lookTimer -= dt;
            if (_lookTimer <= 0f)
            {
                if (Mathf.Approximately(_lookTarget, 0f))
                {
                    _lookTarget = (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.5f, 1f);
                    _lookTimer = Random.Range(2.5f, 4.5f);
                }
                else
                {
                    _lookTarget = 0f;
                    Vector2 iv = profile != null ? profile.lookAroundInterval : new Vector2(8f, 14f);
                    _lookTimer = Random.Range(Mathf.Min(iv.x, iv.y), Mathf.Max(iv.x, iv.y));
                }
            }
            _look = Mathf.SmoothDamp(_look, _lookTarget, ref _lookVel, 1.0f, Mathf.Infinity, dt);
        }

        // ================================================================== Haltung anwenden

        private void ApplyStance(Stance a, Stance b, float r, float w, bool melee)
        {
            Transform model = animator.transform;
            _model = model;
            _r = model.right; _u = model.up; _f = model.forward;

            // Bezugspunkt: Boden unter der animierten Huefte
            Vector3 hips0 = _hips.position;
            Vector3 anchor = hips0 - _u * Vector3.Dot(hips0 - model.position, _u);

            Vector3 lFoot0 = _leftLeg.end.position, rFoot0 = _rightLeg.end.position;
            Quaternion lFootRot0 = _leftLeg.end.rotation, rFootRot0 = _rightLeg.end.rotation;
            // Fuss relativ zum Schienbein (wie animiert) – danach Sohle wieder flach wie animiert
            Quaternion lFootRel = Quaternion.Inverse(_leftLeg.lower.rotation) * lFootRot0;
            Quaternion rFootRel = Quaternion.Inverse(_rightLeg.lower.rotation) * rFootRot0;

            float breath = Mathf.Sin(_time * (2f * Mathf.PI / BreathPeriod));
            float sway = Mathf.Sin(_time * (2f * Mathf.PI / SwayPeriod));
            float relaxed = 1f - r;

            // ---- Huefte
            Vector3 pelvisOffset = Vector3.Lerp(a.pelvisOffset, b.pelvisOffset, r);
            float swayAmt = Mathf.Lerp(a.sway, b.sway, r);
            _hips.position += (M(pelvisOffset) + _r * (swayAmt * sway)) * w;
            Vector3 pr = Vector3.Lerp(a.pelvisRotation, b.pelvisRotation, r) * w;
            _hips.rotation = Semantic(pr.x, pr.y, pr.z) * _hips.rotation;

            // ---- Wirbelsaeule (verteilt) + Atmen
            Vector3 sp = Vector3.Lerp(a.spine, b.spine, r) * w;
            float breathDeg = Mathf.Lerp(a.breathing, b.breathing, r) * breath * w;
            RotateSpine(sp, breathDeg);

            // ---- Schultern
            Vector2 sh = Vector2.Lerp(a.shoulders, b.shoulders, r) * w;
            RotateShoulder(_lShoulder, _leftArm.upper, sh.x + breathDeg * 0.4f, sh.y);
            RotateShoulder(_rShoulder, _rightArm.upper, sh.x + breathDeg * 0.4f, sh.y);

            // ---- Kopf (+ gelegentliches Umschauen in der entspannten Haltung)
            Vector3 hd = Vector3.Lerp(a.head, b.head, r) * w;
            float lookW = w * relaxed;
            hd.z += _look * profile.lookAroundAngle * lookW;
            hd.x -= Mathf.Abs(_look) * profile.lookAroundLift * lookW;
            if (_neck != null) _neck.rotation = Semantic(hd.x * 0.4f, hd.y * 0.4f, hd.z * 0.4f) * _neck.rotation;
            float headShare = _neck != null ? 0.6f : 1f;
            _head.rotation = Semantic(hd.x * headShare, hd.y * headShare, hd.z * headShare) * _head.rotation;

            // ---- Beine: Fuesse platzieren, Huefte so hoch, dass das laengere Bein fast gestreckt ist
            Vector3 lTarget = FootTarget(anchor, Vector3.Lerp(a.leftFoot, b.leftFoot, r), lFoot0, w);
            Vector3 rTarget = FootTarget(anchor, Vector3.Lerp(a.rightFoot, b.rightFoot, r), rFoot0, w);
            float stretch = Mathf.Lerp(a.legStretch, b.legStretch, r);
            float dy = Mathf.Min(PelvisLift(_leftLeg, lTarget, stretch), PelvisLift(_rightLeg, rTarget, stretch));
            _hips.position += _u * (Mathf.Clamp(dy, -0.15f, 0.08f) * w);

            TwoBone(_leftLeg, lTarget, M(new Vector3(-0.25f, 0f, 1f)));
            TwoBone(_rightLeg, rTarget, M(new Vector3(0.25f, 0f, 1f)));
            float lOut = Mathf.Lerp(a.leftFootOut, b.leftFootOut, r) * w;
            float rOut = Mathf.Lerp(a.rightFootOut, b.rightFootOut, r) * w;
            // Fussspitzen nach aussen kommen aus der HUEFTE (Aussendrehung des ganzen Beins, Knie folgt der Fussspitze) –
            // nicht aus dem Sprunggelenk, das sich kaum seitlich drehen laesst
            _leftLeg.end.rotation = FootOnShin(_leftLeg, lFootRel, lFootRot0);
            _rightLeg.end.rotation = FootOnShin(_rightLeg, rFootRel, rFootRot0);
            TurnLeg(_leftLeg, -lOut);
            TurnLeg(_rightLeg, rOut);

            // ---- Arme (nur entspannte Haltung; kampfbereit = Arme aus der Waffenhalte-Animation)
            float armW = w * relaxed;
            if (armW <= 0.0001f) return;

            UpdateSurface();
            StancePlantedSword planted = a.plantedSword;
            bool swordPlanted = melee && planted.enabled && !WeaponSheathed && PrepareSword();
            if (swordPlanted) { PlantSword(planted, anchor, armW); RightArmOwn = armW; }
            // Die Waffenhand-Pose der Standhaltung (Schwert locker in der Rechten, Pistole gesenkt) nur mit Waffe in
            // der Hand – steckt die Waffe oder ist keine da, haengt der Arm locker (sonst greift die Hand ins Leere
            // und wird vom Koerper nach hinten auf das Gesaess geschoben).
            else if (!WeaponSheathed && a.rightArm != null && a.rightArm.enabled) { PoseArm(a.rightArm, _rightArm, _rHand, anchor, armW); RightArmOwn = armW; }
            // 21.10. abends (Max: „der rechte Arm sieht oft nicht richtig aus“): ohne Waffe in der Hand blieb der Arm der Stand-
            // Animation (UAL Idle_Loop: Faust vorn an der Hüfte, Ellbogen nach hinten-außen, Schulter hochgezogen). Jetzt hängt er
            // locker wie der linke Arm des Mannes (gespiegelt) – gilt für Mann und Frau.
            else { PoseArm(RechterArmLocker, _rightArm, _rHand, anchor, armW); RightArmOwn = armW; }
            if (a.leftArm != null && a.leftArm.enabled) { PoseArm(a.leftArm, _leftArm, _lHand, anchor, armW); LeftArmOwn = armW; }
        }

        /// <summary>Fuss folgt dem Schienbein (Sprunggelenk bleibt wie animiert), die Sohle liegt so flach wie in der Animation.</summary>
        private static Quaternion FootOnShin(Limb leg, Quaternion rel, Quaternion animWorld)
        {
            Quaternion q = leg.lower.rotation * rel;
            Vector3 upLocal = Quaternion.Inverse(animWorld) * Vector3.up;
            return Quaternion.FromToRotation(q * upLocal, Vector3.up) * q;
        }

        /// <summary>Ganzes Bein um die Linie Huefte→Knoechel drehen (Fuss bleibt stehen); + = Fussspitze nach rechts.</summary>
        private void TurnLeg(Limb leg, float yaw)
        {
            if (!leg.Valid || Mathf.Abs(yaw) < 0.01f) return;
            Vector3 axis = leg.end.position - leg.upper.position;
            if (axis.sqrMagnitude < 1e-6f) return;
            // Achse zeigt nach unten: positive Gierung (vorn → rechts) = negative Drehung um die Beinachse
            leg.upper.rotation = Quaternion.AngleAxis(-yaw, axis.normalized) * leg.upper.rotation;
        }

        private void RotateSpine(Vector3 sp, float breathDeg)
        {
            Transform[] bones = { _spine, _chest, _upperChest };
            float[] share = { 0.3f, 0.3f, 0.4f };
            float total = 0f;
            for (int i = 0; i < 3; i++) if (bones[i] != null) total += share[i];
            if (total <= 0f) return;
            Transform top = _upperChest != null ? _upperChest : _chest != null ? _chest : _spine;
            for (int i = 0; i < 3; i++)
            {
                if (bones[i] == null) continue;
                float k = share[i] / total;
                float pitch = sp.x * k - (bones[i] == top ? breathDeg : 0f);
                bones[i].rotation = Semantic(pitch, sp.y * k, sp.z * k) * bones[i].rotation;
            }
        }

        private void RotateShoulder(Transform shoulder, Transform upperArm, float raise, float back)
        {
            if (shoulder == null || upperArm == null) return;
            Vector3 d = upperArm.position - shoulder.position;
            if (d.sqrMagnitude < 1e-8f) return;
            d.Normalize();
            Vector3 t = Vector3.RotateTowards(d, _u, raise * Mathf.Deg2Rad, 0f);
            t = Vector3.RotateTowards(t, -_f, back * Mathf.Deg2Rad, 0f);
            shoulder.rotation = Quaternion.FromToRotation(d, t) * shoulder.rotation;
        }

        private Vector3 FootTarget(Vector3 anchor, Vector3 stanceFoot, Vector3 animated, float w)
        {
            Vector3 t = anchor + M(stanceFoot);
            t += _u * Vector3.Dot(animated - t, _u); // Hoehe des animierten Fusses behalten
            return Vector3.Lerp(animated, t, w);
        }

        /// <summary>Wie weit die Huefte hoch (+) oder runter (-) muss, damit das Bein zu 'stretch' gestreckt ist.</summary>
        private float PelvisLift(Limb leg, Vector3 target, float stretch)
        {
            Vector3 h = leg.upper.position;
            float len = Vector3.Distance(h, leg.lower.position) + Vector3.Distance(leg.lower.position, leg.end.position);
            Vector3 v = target - h;
            float vu = Vector3.Dot(v, _u);
            float disc = vu * vu - v.sqrMagnitude + stretch * stretch * len * len;
            return vu + Mathf.Sqrt(Mathf.Max(0f, disc));
        }

        // ================================================================== Arme

        private void PoseArm(StanceArm arm, Limb limb, HandBones hand, Vector3 anchor, float weight)
        {
            if (arm == null || !arm.enabled || !limb.Valid || !hand.Valid) return;
            Vector3 grip = arm.anchor == StanceAnchor.Hips ? _hips.position + M(arm.grip) : anchor + M(arm.grip);
            bool rest = arm.restOnBody && _surfaceValid;
            if (rest)
            {
                // Auf die gemessene Oberflaeche legen: Faustmitte liegt ~1 cm vor der Handflaeche
                Vector3 dir = Vector3.ProjectOnPlane(M(new Vector3(arm.grip.x, 0f, arm.grip.z)), _u);
                if (dir.sqrMagnitude > 1e-6f)
                    grip = Contact(Height(_hips.position) + arm.grip.y, dir.normalized, profile.equipmentGap - 0.01f);
            }

            Quaternion u0 = limb.upper.localRotation, l0 = limb.lower.localRotation, h0 = limb.end.localRotation;
            Vector3 elbow0 = M(arm.elbow).normalized, elbow = elbow0;
            for (int it = 0; it < 5; it++)
            {
                limb.upper.localRotation = u0; limb.lower.localRotation = l0; limb.end.localRotation = h0;
                SolveArm(limb, hand, grip, M(arm.knuckles), M(arm.palm), elbow);
                if (!_surfaceValid) break;
                float pen = Worst(limb, hand, rest, out Vector3 push, out bool atElbow);
                if (pen < 0.004f) break;
                if (atElbow) elbow = LimitElbow(elbow0, elbow + push * 2f);   // Ellbogen etwas nach aussen
                else grip += push * pen;                                // Hand/Unterarm aus dem Koerper
            }
            Blend(limb, u0, l0, h0, weight);
            // Finger nie nach hinten ueberstrecken (negative Kruemmung war anatomisch falsch); aufliegende Hand locker gekruemmt
            float curl = arm.restOnBody ? Mathf.Clamp(arm.fingerCurl, 12f, 40f) : Mathf.Max(0f, arm.fingerCurl);
            if (curl > 0.01f) CurlFingers(hand, limb, curl * weight);
        }

        /// <summary>Ellbogen hoechstens 20 Grad von der gewuenschten Richtung wegdrehen – sonst stellt sich der
        /// Arm ab und zieht die Schulter hoch.</summary>
        private static Vector3 LimitElbow(Vector3 wanted, Vector3 candidate)
        {
            if (candidate.sqrMagnitude < 1e-6f) return wanted;
            return Vector3.RotateTowards(wanted, candidate.normalized, 20f * Mathf.Deg2Rad, 0f);
        }

        private static void Blend(Limb limb, Quaternion u0, Quaternion l0, Quaternion h0, float weight)
        {
            if (weight >= 0.999f) return;
            limb.upper.localRotation = Quaternion.Slerp(u0, limb.upper.localRotation, weight);
            limb.lower.localRotation = Quaternion.Slerp(l0, limb.lower.localRotation, weight);
            limb.end.localRotation = Quaternion.Slerp(h0, limb.end.localRotation, weight);
        }

        /// <summary>Arm so fuehren, dass die Faustmitte bei 'grip' liegt und die Hand wie vorgegeben ausgerichtet ist.</summary>
        private static void SolveArm(Limb limb, HandBones hand, Vector3 grip, Vector3 knuckles, Vector3 palm, Vector3 elbow)
        {
            knuckles.Normalize();
            palm = Vector3.ProjectOnPlane(palm, knuckles);
            if (palm.sqrMagnitude < 1e-6f || knuckles.sqrMagnitude < 1e-6f) return;
            palm.Normalize();
            Quaternion targetFrame = Quaternion.LookRotation(knuckles, palm);

            for (int pass = 0; pass < 2; pass++)
            {
                HandFrame(limb, hand, out Vector3 kn, out Vector3 pm, out Vector3 center);
                Quaternion q = targetFrame * Quaternion.Inverse(Quaternion.LookRotation(kn, pm));
                Vector3 handTarget = grip - q * (center - limb.end.position);
                TwoBone(limb, handTarget, elbow);
                HandFrame(limb, hand, out kn, out pm, out _);
                limb.end.rotation = targetFrame * Quaternion.Inverse(Quaternion.LookRotation(kn, pm)) * limb.end.rotation;
            }
            ClampWrist(limb, hand);
        }

        /// <summary>
        /// Handgelenk anatomisch begrenzen: Beugung 70°, Streckung 60°, zur Daumenseite 20°, zur Kleinfingerseite 35°
        /// (Ellipse). Eine vorgegebene Handlage, die mehr verlangt, wird zum Unterarm hin zurueckgenommen.
        /// </summary>
        public static void ClampWrist(Transform lower, Transform hand, Vector3 handDir, Vector3 palm, Vector3 radial, float scale = 1f)
        {
            if (lower == null || hand == null) return;
            Vector3 axis = hand.position - lower.position;
            if (axis.sqrMagnitude < 1e-8f || handDir.sqrMagnitude < 1e-8f) return;
            axis.Normalize();
            Vector3 dv = Vector3.ProjectOnPlane(handDir, axis);
            float theta = Vector3.Angle(axis, handDir);
            if (theta < 1f || dv.sqrMagnitude < 1e-10f) return;
            dv.Normalize();
            // Achsen im Unterarm-Raum (senkrecht zum Unterarm) – die Handachsen selbst kippen bei starker Beugung mit
            Vector3 pF = Vector3.ProjectOnPlane(palm, axis);
            if (pF.sqrMagnitude < 1e-6f) pF = Vector3.Cross(radial, axis);
            pF.Normalize();
            Vector3 rF = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(radial, axis), pF);
            if (rF.sqrMagnitude < 1e-6f) rF = Vector3.Cross(axis, pF);
            rF.Normalize();
            if (Vector3.Dot(rF, radial) < 0f) rF = -rF;
            float cf = Vector3.Dot(dv, pF), cr = Vector3.Dot(dv, rF);
            float limF = (cf > 0f ? 70f : 60f) * scale, limR = (cr > 0f ? 20f : 35f) * scale;
            float r = Mathf.Sqrt(Sq(theta * cf / limF) + Sq(theta * cr / limR));
            if (r <= 1f) return;
            Vector3 want = Vector3.RotateTowards(axis, handDir, theta / r * Mathf.Deg2Rad, 0f);
            hand.rotation = Quaternion.FromToRotation(handDir, want) * hand.rotation;
        }

        private static float Sq(float x) => x * x;

        private static void ClampWrist(Limb limb, HandBones hand)
        {
            HandFrame(limb, hand, out Vector3 kn, out Vector3 pm, out _);
            Transform fingerRef = hand.middle != null ? hand.middle : hand.index;
            Vector3 dir = fingerRef.position - limb.end.position;
            // Knoechelachse zeigt zum Zeigefinger = Daumen-/Speichenseite
            ClampWrist(limb.lower, limb.end, dir.normalized, pm, kn);
        }

        /// <summary>Handachsen wie in <see cref="CharacterBodySwitcher"/>: Knoechelachse, Handflaeche, Faustmitte.</summary>
        private static void HandFrame(Limb limb, HandBones hand, out Vector3 knuckles, out Vector3 palm, out Vector3 center)
        {
            Transform h = limb.end;
            Transform fingerRef = hand.middle != null ? hand.middle : hand.index;
            Vector3 wrist = (fingerRef.position - h.position).normalized;
            Vector3 raw = hand.index.position - hand.little.position;
            knuckles = Vector3.ProjectOnPlane(raw, wrist);
            knuckles = knuckles.sqrMagnitude > 1e-8f ? knuckles.normalized : Vector3.Cross(wrist, Vector3.up).normalized;
            palm = Vector3.Cross(wrist, knuckles).normalized;
            if (hand.thumb != null && Vector3.Dot(palm, hand.thumb.position - h.position) < 0f) palm = -palm;
            Vector3 mid = (hand.index.position + hand.little.position) * 0.5f;
            center = mid + palm * 0.025f - wrist * 0.01f;
        }

        private static void CurlFingers(HandBones hand, Limb limb, float degrees)
        {
            if (hand.curl == null) return;
            HandFrame(limb, hand, out _, out Vector3 palm, out _);
            for (int i = 0; i + 1 < hand.curl.Length; i += 2)
            {
                Transform b = hand.curl[i], next = hand.curl[i + 1];
                if (b == null || next == null) continue;
                Vector3 d = next.position - b.position;
                if (d.sqrMagnitude < 1e-8f) continue;
                b.rotation = Quaternion.FromToRotation(d, Vector3.RotateTowards(d, palm, degrees * Mathf.Deg2Rad, 0f)) * b.rotation;
            }
        }

        // ================================================================== Schwert aufstuetzen

        private bool PrepareSword()
        {
            if (_fx == null) return false;
            Transform pivot = _fx.MeleePivot;
            if (pivot == null || !pivot.gameObject.activeInHierarchy) return false;
            if (pivot != _sword)
            {
                _sword = pivot;
                _swordMeasured = false;
            }
            if (!_swordMeasured) MeasureSword();
            // Lage in der Hand merken (wird vor jedem Frame wiederhergestellt)
            _swordLocalPos = _sword.localPosition;
            _swordLocalRot = _sword.localRotation;
            _swordParent = _sword.parent;
            return true;
        }

        /// <summary>Klingenspitze und Knauf entlang der Klingenachse (+Z des Waffen-Drehpunkts) ausmessen.</summary>
        private void MeasureSword()
        {
            _swordMeasured = true;
            float min = float.MaxValue, max = float.MinValue;
            foreach (MeshFilter mf in _sword.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds bb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = bb.center + Vector3.Scale(bb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = _sword.InverseTransformPoint(mf.transform.TransformPoint(c));
                    min = Mathf.Min(min, local.z);
                    max = Mathf.Max(max, local.z);
                }
            }
            if (max > min)
            {
                float s = _sword.lossyScale.z;
                _swordTip = max * s;
                _swordPommel = min * s;
            }
        }

        private void PlantSword(StancePlantedSword planted, Vector3 anchor, float weight)
        {
            Limb limb = _rightArm;
            if (!limb.Valid || !_rHand.Valid) return;
            float length = _swordTip - _swordPommel;
            float lean = planted.lean * Mathf.Deg2Rad;
            Vector3 heading = Vector3.ProjectOnPlane(M(planted.heading), _u);
            heading = heading.sqrMagnitude > 1e-6f ? heading.normalized : _r;
            Vector3 blade = (-_u * Mathf.Cos(lean) + heading * Mathf.Sin(lean)).normalized; // Knauf -> Spitze
            float pommelHeight = length * Mathf.Cos(lean);

            // Knauf seitlich neben dem Koerper (gemessene Breite + Abstand)
            Vector3 center = anchor + _u * pommelHeight;
            float side = 0.17f;
            if (_surfaceValid)
            {
                float s = Support(pommelHeight, _r, out Vector3 c);
                if (s > 0f) { side = s; center = c; }
            }
            Vector3 pommel = center + _r * (side + planted.sideClearance + profile.equipmentGap) + _f * planted.forward;
            Vector3 tip = pommel + blade * length;

            // Unebener Boden: Schwert samt Hand auf den Boden unter der Spitze setzen
            if (Physics.Raycast(tip + _u * 0.6f, -_u, out RaycastHit hit, 1.2f, GameLayers.CameraCollisionMask(), QueryTriggerInteraction.Ignore))
            {
                Vector3 shift = _u * Vector3.Dot(hit.point - _u * 0.01f - tip, _u);
                pommel += shift;
                tip += shift;
            }

            // Schulter entspannt absenken, bevor der Arm zum Knauf gefuehrt wird
            RotateShoulder(_rShoulder, limb.upper, -planted.shoulderDrop * weight, 0f);

            Quaternion u0 = limb.upper.localRotation, l0 = limb.lower.localRotation, h0 = limb.end.localRotation;
            Vector3 elbow0 = M(planted.elbow).normalized, elbow = elbow0;
            for (int it = 0; it < 5; it++)
            {
                limb.upper.localRotation = u0; limb.lower.localRotation = l0; limb.end.localRotation = h0;
                // Hand flach auf dem Knauf (Handflaeche zeigt in Klingenrichtung = nach unten)
                SolveArm(limb, _rHand, pommel + _u * 0.012f, M(planted.knuckles), blade, elbow);
                if (!_surfaceValid) break;
                float pen = Worst(limb, _rHand, false, out Vector3 push, out bool atElbow);
                if (pen < 0.004f) break;
                if (atElbow) elbow = LimitElbow(elbow0, elbow + push * 2f);
                else { pommel += push * pen; tip += push * pen; }
            }
            Blend(limb, u0, l0, h0, weight);

            // Schwert zwischen Hand und Boden ueberblenden
            Quaternion swordRot = Quaternion.LookRotation(blade, _f);
            Vector3 swordPos = tip - blade * _swordTip;
            Vector3 heldPos = _sword.position;
            Quaternion heldRot = _sword.rotation;
            _sword.SetPositionAndRotation(Vector3.Lerp(heldPos, swordPos, weight), Quaternion.Slerp(heldRot, swordRot, weight));
            _swordOverridden = true;
        }

        private void RestoreSword()
        {
            if (!_swordOverridden) return;
            _swordOverridden = false;
            // Inzwischen in die Scheide gesteckt (anderer Elternteil)? Dann gilt die Handlage nicht mehr.
            if (_sword == null || _sword.parent != _swordParent) return;
            _sword.localPosition = _swordLocalPos;
            _sword.localRotation = _swordLocalRot;
        }

        // ================================================================== Koerperoberflaeche

        /// <summary>
        /// Misst einmalig die Rumpf-Oberflaeche (Huefte bis Brust) aus dem gehauteten Modell und merkt sich jeden
        /// Punkt relativ zu seinem Knochen. So folgt die Oberflaeche jeder Haltung, ohne das Netz jedes Bild neu
        /// zu berechnen.
        /// </summary>
        private void BuildSurface()
        {
            _surfaceTried = true;
            _surfBones = new[] { _hips, _spine, _chest, _upperChest };
            var locals = new System.Collections.Generic.List<Vector3>(2048);
            var owner = new System.Collections.Generic.List<byte>(2048);
            bool unreadable = false;

            foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh shared = smr.sharedMesh;
                if (shared == null) continue;
                if (!shared.isReadable) { unreadable = true; continue; }
                BoneWeight[] weights = shared.boneWeights;
                Transform[] bones = smr.bones;
                if (weights == null || weights.Length != shared.vertexCount || bones == null) continue;

                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                Vector3[] verts = baked.vertices;
                Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                // Leistung (M8.3): ~400 Punkte je Netz genuegen fuer die Rumpf-Ausdehnung; mit Ausruestung waren es sonst
                // viele Tausend Punkte, die jedes Bild mehrfach durchlaufen wurden (~0,6 ms je Figur).
                int step = Mathf.Max(1, verts.Length / 400);
                for (int i = 0; i < verts.Length && i < weights.Length; i += step)
                {
                    BoneWeight bw = weights[i];
                    int bi = DominantBone(bw);
                    if (bi < 0 || bi >= bones.Length || bones[bi] == null) continue;
                    int k = System.Array.IndexOf(_surfBones, bones[bi]);
                    if (k < 0) continue;
                    locals.Add(bones[bi].InverseTransformPoint(toWorld.MultiplyPoint3x4(verts[i])));
                    owner.Add((byte)k);
                }
                Destroy(baked);
            }

            _surfLocal = locals.ToArray();
            _surfBone = owner.ToArray();
            _surfWorld = new Vector3[_surfLocal.Length];
            _surfHeight = new float[_surfLocal.Length];
            if (_surfLocal.Length < 50)
                Debug.LogWarning("[Ashveil] Haltung: Koerperoberflaeche nicht messbar" +
                                 (unreadable ? " (Modell-Import ohne Read/Write – bitte Szene neu erstellen)." : ".") +
                                 " Haende koennen durch den Koerper gleiten.", this);
        }

        private static int DominantBone(BoneWeight bw)
        {
            int best = bw.boneIndex0; float w = bw.weight0;
            if (bw.weight1 > w) { best = bw.boneIndex1; w = bw.weight1; }
            if (bw.weight2 > w) { best = bw.boneIndex2; w = bw.weight2; }
            if (bw.weight3 > w) { best = bw.boneIndex3; w = bw.weight3; }
            return w > 0f ? best : -1;
        }

        /// <summary>Oberflaechenpunkte auf die aktuelle Haltung umrechnen.</summary>
        private void UpdateSurface()
        {
            if (!_surfaceTried) BuildSurface();
            _surfaceValid = _surfLocal != null && _surfLocal.Length >= 50;
            if (!_surfaceValid) return;
            Matrix4x4 m0 = L2W(0), m1 = L2W(1), m2 = L2W(2), m3 = L2W(3);
            Vector3 origin = _model.position;
            for (int i = 0; i < _surfLocal.Length; i++)
            {
                Matrix4x4 m = _surfBone[i] == 0 ? m0 : _surfBone[i] == 1 ? m1 : _surfBone[i] == 2 ? m2 : m3;
                Vector3 p = m.MultiplyPoint3x4(_surfLocal[i]);
                _surfWorld[i] = p;
                _surfHeight[i] = Vector3.Dot(p - origin, _u);
            }
        }

        private Matrix4x4 L2W(int i) => _surfBones[i] != null ? _surfBones[i].localToWorldMatrix : Matrix4x4.identity;

        private float Height(Vector3 p) => Vector3.Dot(p - _model.position, _u);

        /// <summary>Mitte des Rumpf-Querschnitts auf Hoehe h (Welt). false, wenn dort kein Rumpf ist.</summary>
        private bool SlabCenter(float h, out Vector3 center)
        {
            Vector3 sum = Vector3.zero; int n = 0;
            for (int i = 0; i < _surfWorld.Length; i++)
            {
                if (Mathf.Abs(_surfHeight[i] - h) > SlabHalf) continue;
                sum += _surfWorld[i]; n++;
            }
            if (n < 8) { center = Vector3.zero; return false; }
            center = sum / n;
            center += _u * (h - Height(center));
            return true;
        }

        /// <summary>Wie weit der Rumpf auf Hoehe h (ueber dem Modellursprung) in Richtung 'dir' reicht, gemessen von der Querschnittsmitte.</summary>
        private float Support(float h, Vector3 dir, out Vector3 center)
        {
            if (!SlabCenter(h, out center)) return 0f;
            float max = float.MinValue;
            for (int i = 0; i < _surfWorld.Length; i++)
            {
                if (Mathf.Abs(_surfHeight[i] - h) > SlabHalf) continue;
                float d = Vector3.Dot(_surfWorld[i] - center, dir);
                if (d > max) max = d;
            }
            return max;
        }

        /// <summary>Punkt auf der Oberflaeche (plus Abstand) in Richtung 'dir' auf Hoehe h.</summary>
        private Vector3 Contact(float h, Vector3 dir, float offset)
        {
            float ext = Support(h, dir, out Vector3 center);
            return center + dir * (ext + offset);
        }

        /// <summary>Wie tief steckt eine Kugel (p, radius) im Rumpf? push = Richtung nach aussen.</summary>
        private float Penetration(Vector3 p, float radius, out Vector3 push)
        {
            push = Vector3.zero;
            float h = Height(p);
            if (!SlabCenter(h, out Vector3 c)) return 0f;
            Vector3 v = Vector3.ProjectOnPlane(p - c, _u);
            float d = v.magnitude;
            push = d > 1e-4f ? v / d : _f;
            float ext = float.MinValue;
            for (int i = 0; i < _surfWorld.Length; i++)
            {
                if (Mathf.Abs(_surfHeight[i] - h) > SlabHalf) continue;
                float e = Vector3.Dot(_surfWorld[i] - c, push);
                if (e > ext) ext = e;
            }
            return ext + radius - d;
        }

        /// <summary>Staerkste Durchdringung entlang Ellbogen, Unterarm und Hand (inkl. Ausruestungsabstand).</summary>
        private float Worst(Limb limb, HandBones hand, bool skipHand, out Vector3 push, out bool atElbow)
        {
            float gap = profile.equipmentGap;
            push = Vector3.zero; atElbow = false;
            float worst = 0f;
            Vector3 e = limb.lower.position, h = limb.end.position;

            void Check(Vector3 p, float r, bool elbow, ref float best, ref Vector3 dir, ref bool isElbow)
            {
                float pen = Penetration(p, r + gap, out Vector3 d);
                if (pen > best) { best = pen; dir = d; isElbow = elbow; }
            }

            Check(e, 0.05f, true, ref worst, ref push, ref atElbow);
            for (int i = 1; i <= 4; i++) Check(Vector3.Lerp(e, h, i * 0.25f), 0.045f, false, ref worst, ref push, ref atElbow);
            if (!skipHand)
            {
                HandFrame(limb, hand, out _, out _, out Vector3 center);
                Check(center, 0.035f, false, ref worst, ref push, ref atElbow);
            }
            return worst;
        }

        // ================================================================== Hilfen

        /// <summary>Modellraum (x rechts, y oben, z vorn) -> Welt.</summary>
        private Vector3 M(Vector3 v) => _r * v.x + _u * v.y + _f * v.z;

        /// <summary>
        /// Drehung in Modellbegriffen (Grad): pitch = nach vorn kippen, roll = zur rechten Seite neigen,
        /// yaw = nach rechts drehen. Unabhaengig von Links-/Rechtshaendigkeit der Achsen.
        /// </summary>
        private Quaternion Semantic(float pitch, float roll, float yaw)
        {
            Quaternion q = Quaternion.identity;
            if (Mathf.Abs(roll) > 1e-4f) q = Quaternion.FromToRotation(_u, Vector3.RotateTowards(_u, _r, roll * Mathf.Deg2Rad, 0f)) * q;
            if (Mathf.Abs(pitch) > 1e-4f) q = Quaternion.FromToRotation(_u, Vector3.RotateTowards(_u, _f, pitch * Mathf.Deg2Rad, 0f)) * q;
            if (Mathf.Abs(yaw) > 1e-4f) q = Quaternion.FromToRotation(_f, Vector3.RotateTowards(_f, _r, yaw * Mathf.Deg2Rad, 0f)) * q;
            return q;
        }

        /// <summary>Zwei-Knochen-IK (Oberarm/Unterarm bzw. Oberschenkel/Schienbein). 'hint' = Richtung von Ellbogen/Knie.</summary>
        /// <summary>Zwei-Knochen-IK mit Scharnier (Knie/Ellbogen beugen nur in ihrer Achse, siehe <see cref="LimbIK"/>).</summary>
        private static void TwoBone(Limb limb, Vector3 target, Vector3 hint)
        {
            if (!limb.Valid) return;
            LimbIK.TwoBone(limb.upper, limb.lower, limb.end, target, hint);
        }

        private bool EnsureBones()
        {
            if (_bonesReady) return true;
            if (!animator.isHuman || animator.avatar == null) return false;
            Transform B(HumanBodyBones x) => animator.GetBoneTransform(x);

            _hips = B(HumanBodyBones.Hips);
            _spine = B(HumanBodyBones.Spine);
            _chest = B(HumanBodyBones.Chest);
            _upperChest = B(HumanBodyBones.UpperChest);
            _neck = B(HumanBodyBones.Neck);
            _head = B(HumanBodyBones.Head);
            _lShoulder = B(HumanBodyBones.LeftShoulder);
            _rShoulder = B(HumanBodyBones.RightShoulder);

            _leftArm.upper = B(HumanBodyBones.LeftUpperArm); _leftArm.lower = B(HumanBodyBones.LeftLowerArm); _leftArm.end = B(HumanBodyBones.LeftHand);
            _rightArm.upper = B(HumanBodyBones.RightUpperArm); _rightArm.lower = B(HumanBodyBones.RightLowerArm); _rightArm.end = B(HumanBodyBones.RightHand);
            _leftLeg.upper = B(HumanBodyBones.LeftUpperLeg); _leftLeg.lower = B(HumanBodyBones.LeftLowerLeg); _leftLeg.end = B(HumanBodyBones.LeftFoot);
            _rightLeg.upper = B(HumanBodyBones.RightUpperLeg); _rightLeg.lower = B(HumanBodyBones.RightLowerLeg); _rightLeg.end = B(HumanBodyBones.RightFoot);

            FillHand(_lHand, true);
            FillHand(_rHand, false);

            _bonesReady = _hips != null && _head != null && _leftLeg.Valid && _rightLeg.Valid && _leftArm.Valid && _rightArm.Valid;
            if (!_bonesReady) Debug.LogWarning("[Ashveil] Haltung: Modell hat nicht alle benoetigten Humanoid-Knochen.", this);
            return _bonesReady;
        }

        private void FillHand(HandBones hand, bool left)
        {
            Transform B(HumanBodyBones x) => animator.GetBoneTransform(x);
            hand.index = B(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            hand.little = B(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            hand.middle = B(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            hand.thumb = B(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
            HumanBodyBones[] chain = left
                ? new[]
                {
                    HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
                    HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
                    HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
                    HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal
                }
                : new[]
                {
                    HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
                    HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
                    HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
                    HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal
                };
            hand.curl = new Transform[chain.Length];
            for (int i = 0; i < chain.Length; i++) hand.curl[i] = B(chain[i]);
        }
    }
}
