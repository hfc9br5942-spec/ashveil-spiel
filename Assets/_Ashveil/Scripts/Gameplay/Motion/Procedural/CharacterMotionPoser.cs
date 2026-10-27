using Ashveil.Data.Motion;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Visuals;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>
    /// Prozedurale Koerperschicht ueber den Animationen (Motion-System M2–M4/M8/M9). Laeuft nach Animator und
    /// Standhaltung, vor den Emotes. Rein optisch – Tempo, Richtung und Treffer bestimmt weiter der Motor.
    ///
    ///   Ausrichtung   Koerper folgt der Blickrichtung mit leichtem Nachlauf (Masse), Oberkoerper und Kopf fuehren;
    ///                 seitlich laufen: Beine in Laufrichtung, Brust bleibt (im Kampf) zum Gegner.
    ///   Gewicht       Rumpf neigt sich beim Anfahren vor, beim Bremsen zurueck und federt nach; in Kurven nach innen;
    ///                 Fussaufsatz sackt kurz ein – alles skaliert mit Ruestung/Klasse (ResolvedMotionTuning).
    ///   Ausschlag     Armschwung, Becken- und Oberkoerperbewegung werden um ihre Mittellage skaliert (schwer = ruhiger);
    ///                 der Kopf haelt den Blick ruhig.
    ///   Fuesse        Boden-IK (Hang, Treppe, Steine), Becken folgt dem tieferen Fuss; im Stand bleiben Fuesse stehen und
    ///                 die Figur tritt beim Drehen um (kein Drehteller), beim Anhalten kein Gleiten.
    /// </summary>
    [DefaultExecutionOrder(55)]
    [DisallowMultipleComponent]
    public sealed class CharacterMotionPoser : MonoBehaviour
    {
        /// <summary>Motion-Lab: Vorher/Nachher-Vergleich (false = nur die rohen Animationen).</summary>
        public static bool GlobalEnabled = true;
        /// <summary>Fehlersuche (Motion-Lab „haltung“, 16.10.): einzelne Teile der Schicht abschalten.</summary>
        public static bool DebugNoFeet, DebugNoArms, DebugNoDynamics;
        /// <summary>18.10. Labor: einzelne Teile der Fuss-Schicht abschaltbar (Ursachensuche „Füße schweben, Knie gebeugt“).</summary>
        public static bool DebugNoWarp, DebugNoRunLock, DebugNoCapLift, DebugNoToeGuard, DebugNoPelvis;
        /// <summary>18.10.: Schwungbein-Grenze aus – sie stammt aus der Quaternius-Zeit (Marschier-Clip). Mit den echten Aufnahmen kappte sie
        /// das Knie im Schwung (69° → 46°) und senkte die Zehen beim Aufsetzen (Gang-Prüfung).</summary>
        public static bool V2OhneHubgrenze = true;
        /// <summary>
        /// Bewegung v2 – Neuanfang 17.10.2026 (Max: „Fang neu an mit den Animationen“). Grundlinie = saubere Quaternius-Loops
        /// ohne die Korrekturschichten vom 16./17.10.: keine Schrittdehnung (Abspieltempo wie vor dem 17.10. bis 2,0), keine
        /// Schulterhaltung, keine Armfuehrung/Ellbogen-Lockerung. Fuesse am Boden (IK) und Abspieltempo bleiben. Die alten
        /// Schichten sind nicht geloescht: false schaltet sie wieder ein.
        /// </summary>
        public static bool V2Grundlinie = true;
        /// <summary>17.10. spätabends (Max: Rennen/Sprint „Vorlage“ + „Füße rutschen/trippeln“, Richtung „Mischung“): Schrittdehnung
        /// wieder an, auch in der v2-Grundlinie (Arm-Schichten bleiben aus). Tempo über Schrittlänge (MOV-037): Sprint 9 m/s auf der
        /// Rennaufnahme 4,4 m/s ≈ Abspieltempo 1,3 × Schritt 1,6.</summary>
        public static bool V2Schrittdehnung = false;   // 18.10. abends (Max: UAL pur, „alles archivieren“): aus
        public const float V2WarpMax = 1.6f, V2RateMax = 1.3f;
        /// <summary>17.10. spätabends (Max: „Füße rutschen/trippeln“): Fussmitschrift zeigte den aufgesetzten Fuss beim Gehen mit
        /// 0,3–0,4 m/s, beim Sprint bis 4 m/s weiterwandern – festgehalten wurde nur im Stand. Jetzt wird der Ballen auch im Lauf
        /// beim Aufsetzen verriegelt (Bein per IK), beim Abheben in 0,08 s gelöst.</summary>
        public static bool V2Fussverriegelung = false;   // 18.10. abends: aus (UAL pur; Füße nur am Boden halten)
        /// <summary>Greif-/Lösehöhe des Ballens über Ruhe (m). 2,5/5 cm griff kaum: CMU-Ballen stehen im Kontakt 3,5–5 cm hoch (Lab 17.10.).</summary>
        public const float BallenFlach = 0.012f;
        public const float V2LockAn = 0.02f, V2LockAus = 0.035f;   // 18.10.: vorher 0,05/0,08 – hielt den Ballen schon im End- und noch im Anfangsschwung fest (Hüfte 42° statt 25° überstreckt, Knie beim Aufsetzen 29–50°)

        /// <summary>
        /// Ein Fuss setzt auf (Klang, 14.10.2026): Figur, Fussposition (Welt), Tempo (m/s). Aus demselben Übergang „Fuss tief“
        /// wie das Einsacken erkannt – nur bei Figuren in voller Detailstufe, also in der Nähe der Kamera. Rein optisch/akustisch.
        /// </summary>
        public static event System.Action<CharacterMotionPoser, Vector3, float> Fussaufsatz;

        [Header("Ausrichtung")]
        // 16.10.: 90° (vorher 70°) – beim reinen Seitwaertslaufen blieben 20° Winkel zwischen Laufrichtung und Beinen,
        // die Fuesse glitten mit sin(20°)·Tempo (Motion-Lab: 20 cm je Schritt). Ausserhalb des Kampfes laufen die Beine
        // jetzt ganz in Laufrichtung; im Kampf (Blick zum Gegner, 0,75·) bleibt ein Rest, bis ein eigener Seitwaerts-Clip da ist.
        [SerializeField] private float maxSideTurn = 90f;
        [SerializeField] private float maxBackTurn = 40f;

        [Header("Fuesse")]
        [SerializeField] private float probeUp = 0.7f;
        [SerializeField] private float probeDown = 0.9f;
        [Tooltip("Hoechste Anpassung nach oben/unten (m).")]
        [SerializeField] private float maxFootOffset = 0.55f;
        /// <summary>16.10. spaet, Schrittdehnung (Stride Warping): Ueber diesem Abspieltempo wird nicht schneller getrippelt,
        /// sondern der Schritt verlaengert (Fuesse weiter vor/zurueck gesetzt). Rennen 6,3 m/s auf dem 4,9-m/s-Clip blieb bei
        /// Tempo 1,3 (Takt 255/min), Sprint 9 m/s bei 1,85 – hektisches Trippeln, jede Welligkeit des Clips vierfach.</summary>
        [SerializeField] private bool footYawLimit = false;
        [SerializeField] private float strideRateMax = 1.3f;
        [SerializeField] private float strideWarpMax = 1.45f;
        [SerializeField] private float maxFootTilt = 28f;
        [Tooltip("Hoechste Fusshoehe (m) im Schwung beim Gehen / ab Rennen (weich begrenzt)")]
        [SerializeField] private float liftCapWalk = 0.14f;
        [SerializeField] private float liftCapRun = 0.45f;
        [Tooltip("Hoechste Ellbogenbeugung (Grad) der freien Arme im Stand / ab Rennen")]
        [SerializeField] private float elbowFlexIdle = 14f;
        [SerializeField] private float elbowFlexRun = 55f;   // 17.10.: 80 → 55 (Ellbogen beim Laufen innen ≥ ~110°)

        private Animator _anim;
        private Transform _model, _root;
        private CharacterMotor _motor;
        private CharacterAnimatorBridge _bridge;
        private CharacterMotionOrchestrator _orch;
        private StancePoser _stance;
        private EmotePoser _emote;
        private CharacterLod _lod;
        private WeaponUser _weapons;
        private Health _health;
        private int _probeMask;
        private WeaponFx _fx;

        // Waffe und Haende fuehrt WeaponHandPoser (danach); hier nur fragen, welche Arme belegt sind
        private WeaponHandPoser _hands;

        private bool _ready;
        private Transform _hips, _spine, _chest, _upperChest, _neck, _head;
        private Transform _lUpperArm, _lLowerArm, _rUpperArm, _rLowerArm, _lShoulder, _rShoulder;
        private readonly Leg _left = new Leg(), _right = new Leg();

        private float _weight;
        private Vector3 _u, _f, _r;

        // Ausrichtung
        private SpringAngle _visYaw;
        private bool _yawInit;
        private float _chestTurn;

        // Gewicht
        private SpringFloat _pitch, _roll, _bob;
        private float _lastFwdSpeed, _accel, _lastYaw, _yawRate;
        private bool _rootInit;

        // Ausschlag (Mittellagen)
        private Quaternion _emaLUA, _emaLLA, _emaRUA, _emaRLA, _emaHips, _emaSpine, _emaChest, _emaHead;
        private bool _emaInit;

        // Fuesse
        private SpringFloat _pelvis;
        private float _lockWeight;
        private bool _locked;
        private Leg _stepping;
        private float _stepT;

        public float Weight => _weight;

        // Abspieltempo der Fortbewegung (gegen Fussgleiten, §14/§87)
        private static readonly int LocoRateId = Animator.StringToHash("LocoRate");
        private int _hasRateParam = -1;
        private float _nativeEst = -1f, _rate = 1f, _appliedRate = 1f;
        private readonly float[] _nativeBuf = new float[32], _nativeSort = new float[32];
        private int _nativeHead, _nativeCount; private float _nativeAge;
        /// <summary>Lab: geschaetzte Laufband-Geschwindigkeit des aktuellen Clips (m/s bei Tempo 1).</summary>
        public float NativeSpeedEstimate => _nativeEst;
        public float LocoRate => _rate;
        /// <summary>Aktuelle Schrittdehnung (1 = wie im Clip).</summary>
        public float StrideScale => _strideScale;
        /// <summary>18.10. Labor-Diagnose: Bodenversatz je Fuss, Becken-Absenkung, Ziel-Anhebung der Füße (m).</summary>
        public Vector4 DebugFuss => new Vector4(_left != null ? _left.ground.Value : 0f, _right != null ? _right.ground.Value : 0f, _pelvis.Value,
            _left != null ? Vector3.Dot(_left.target - _left.animPos, _u) : 0f);
        private float _strideScale = 1f;
        private Vector3 _moveDir;

        private sealed class Leg
        {
            public Transform upper, lower, foot, toe;
            public bool Valid => upper != null && lower != null && foot != null;
            public SpringFloat ground;
            public Vector3 normal = Vector3.up;
            public Vector3 animPos, target;
            public Quaternion animRot;
            public float lift, contact, restHeight = -1f;
            /// <summary>Zehe (Ballen) der Animation und ihre Ruhehöhe über dem Boden – Schutz gegen „Zehe im Boden“ (15.10.).</summary>
            public Vector3 toeAnim;
            public float toeRest = -1f;
            public float toeGroundY; public bool toeGroundOk;
            public Vector3 lockPos, stepFrom;
            /// <summary>Blickrichtung (Grad) des stehenden Fusses – haelt ihn beim Drehen im Stand fest (15.10.).</summary>
            public float lockYaw, stepYawFrom;
            public Vector3 prevLocal;
            public bool hasPrev;
            /// <summary>20.10.: tiefste Ballenhöhe der laufenden Bewegung (langsam steigend) – Bodenkontakt für die Laufband-
            /// Schätzung daran messen, nicht an der Standhöhe (MPFB-Füße: Ruhe 5,9 cm, im Lauf ~1,7 cm → Schwungphase zählte mit).</summary>
            public float ballMin = 1f;
            /// <summary>Korrektur der Fussdrehung (Grad um die Hochachse), in der Luft bestimmt, am Boden gehalten (17.10.).</summary>
            public float yawFix;
            public bool wasLow;
            /// <summary>Anhalten (15.10. (3)): Fuss steht beim Uebergang in den Stand schon am Boden → sofort voll
            /// festhalten (plantHold = 1); ist er noch in der Luft (pending), folgt er der Animation bis zum Aufsetzen.</summary>
            public float plantHold; public bool pending; public float pendingT;
            public Vector3 plantTo; public bool hasPlantTo;
            /// <summary>Fussdrehung (Welt) im Moment des Festhaltens / der geplanten Landung.</summary>
            public Quaternion plantRot = Quaternion.identity;
            /// <summary>Fussdrehung im ruhigen Stand, relativ zur Koerper-Blickrichtung (fuer flache Landungen).</summary>
            public Quaternion restRot = Quaternion.identity; public bool hasRestRot;
            /// <summary>Lauf-Verriegelung (17.10. spätabends): Ballen beim Aufsetzen festhalten, beim Abheben weich lösen.</summary>
            public bool runLock; public Vector3 runBall, runOff; public float runW;
        }

        // ================================================================== Aufbau

        private void Awake()
        {
            _anim = GetComponent<Animator>();
            _model = transform;
            _motor = GetComponentInParent<CharacterMotor>();
            _root = _motor != null ? _motor.transform : transform.root;
            _bridge = GetComponentInParent<CharacterAnimatorBridge>();
            _orch = GetComponentInParent<CharacterMotionOrchestrator>();
            if (_orch == null && _root != null) _orch = _root.GetComponentInChildren<CharacterMotionOrchestrator>();
            _stance = GetComponent<StancePoser>();
            _hands = GetComponent<WeaponHandPoser>();
            _emote = GetComponent<EmotePoser>();
            _lod = GetComponentInParent<CharacterLod>();
            _weapons = GetComponentInParent<WeaponUser>();
            _health = GetComponentInParent<Health>();
            _fx = GetComponentInParent<WeaponFx>();
            _probeMask = ~(1 << _root.gameObject.layer) & ~(1 << 2); // eigene Ebene und „Ignore Raycast“ nicht treffen
            var cc = GetComponentInParent<CharacterController>();
            _skin = cc != null ? Mathf.Clamp(cc.skinWidth, 0f, 0.1f) : 0f;
        }

        // 18.10. (Labor-Teilmessung: Knie im Gehen dauerhaft 11–30° gebeugt, Füße 5–8 cm über dem Boden): Der CharacterController
        // schwebt um seine Hautdicke (skinWidth 5 cm) über dem Boden. Die Fuss-Schicht senkte dafür das Becken ab – das Bein blieb
        // gebeugt, im Stand schwebte die Figur sichtbar. Jetzt: das ganze Modell um die Hautdicke tiefer, alle Fuss-Messungen auf
        // den echten Boden bezogen (RootFoot).
        private float _skin;
        private Vector3 _modelBase; private bool _modelBaseSet;
        private Vector3 RootFoot => _root.position - Vector3.up * _skin;
        private void ApplySkinOffset()
        {
            if (_skin <= 0f || transform.parent == null) return;
            if (!_modelBaseSet) { _modelBase = transform.localPosition; _modelBaseSet = true; }
            transform.localPosition = _modelBase + transform.parent.InverseTransformVector(Vector3.down * _skin);
        }

        private void OnEnable()
        {
            _yawInit = false;
            _emaInit = false;
            _rootInit = false;
            _weight = 0f;
            if (_bridge != null && _bridge.Animator == _anim) _bridge.ExternalOrientation = true;
        }

        private void OnDisable()
        {
            if (_bridge != null && _bridge.Animator == _anim) _bridge.ExternalOrientation = false;
        }

        private bool EnsureBones()
        {
            if (_ready) return true;
            if (_anim == null || !_anim.isHuman || _anim.avatar == null) return false;
            Transform B(HumanBodyBones b) => _anim.GetBoneTransform(b);
            _hips = B(HumanBodyBones.Hips); _spine = B(HumanBodyBones.Spine); _chest = B(HumanBodyBones.Chest);
            _upperChest = B(HumanBodyBones.UpperChest); _neck = B(HumanBodyBones.Neck); _head = B(HumanBodyBones.Head);
            _lShoulder = B(HumanBodyBones.LeftShoulder); _rShoulder = B(HumanBodyBones.RightShoulder);
            _lUpperArm = B(HumanBodyBones.LeftUpperArm); _lLowerArm = B(HumanBodyBones.LeftLowerArm);
            _rUpperArm = B(HumanBodyBones.RightUpperArm); _rLowerArm = B(HumanBodyBones.RightLowerArm);
            _left.upper = B(HumanBodyBones.LeftUpperLeg); _left.lower = B(HumanBodyBones.LeftLowerLeg); _left.foot = B(HumanBodyBones.LeftFoot); _left.toe = B(HumanBodyBones.LeftToes);
            _right.upper = B(HumanBodyBones.RightUpperLeg); _right.lower = B(HumanBodyBones.RightLowerLeg); _right.foot = B(HumanBodyBones.RightFoot); _right.toe = B(HumanBodyBones.RightToes);
            _ready = _hips != null && _spine != null && _head != null && _left.Valid && _right.Valid;
            return _ready;
        }

        // ================================================================== Ablauf

        private void LateUpdate()
        {
            if (_motor == null || !EnsureBones() || !_anim.isActiveAndEnabled) return;
            if (_bridge != null) _bridge.ExternalOrientation = _bridge.Animator == _anim;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (dt <= 0f) return;
            ApplySkinOffset();

            MotionModifiers m = _orch != null && _orch.Tuning.IsValid ? _orch.Tuning.modifiers : MotionModifiers.Neutral;
            bool combat = _orch != null && _orch.Context.Stance == MotionStance.Combat;

            MotorState s = _motor.State;
            Vector3 local = _motor.LocalVelocity;
            float speed = new Vector2(local.x, local.z).magnitude;
            { Vector3 md = Vector3.ProjectOnPlane(_model != null ? _model.TransformDirection(new Vector3(local.x, 0f, local.z)) : Vector3.zero, _u); if (md.sqrMagnitude > 0.04f) _moveDir = md.normalized; }
            bool attacking = (_weapons != null && _weapons.IsInAttackMotion) || AnimatorActions.UpperBody(_anim) || AnimatorActions.FullBody(_anim);
            bool alive = _health == null || _health.IsAlive;
            bool emoting = (_emote != null && _emote.Active) || _motor.IsMounted;
            bool detailed = _lod == null || _lod.Level == 0;

            // Ausrichtung gehoert immer uns (auch bei geringer Detailstufe – dann ohne Nachlauf)
            Orient(s, local, speed, attacking, combat, m, dt, detailed && GlobalEnabled);

            bool want = GlobalEnabled && detailed && alive && !emoting;
            _weight = Mathf.MoveTowards(_weight, want ? 1f : 0f, dt / 0.2f);
            TrackRoot(s);
            if (_weight <= 0.001f)
            {
                _emaInit = false;
                if (_hasRateParam == 1 && _appliedRate != 1f) { _anim.SetFloat(LocoRateId, 1f); _appliedRate = 1f; _rate = 1f; }
                return;
            }
            float w = MotionMath.SmoothStep01(_weight);

            _u = _model.up; _f = _model.forward; _r = _model.right;
            CaptureFeet(s.Grounded && speed < 0.2f, dt);
            MatchStride(s, local, speed, dt);
            Amplitudes(speed, attacking, m, dt, w);
            if (!DebugNoDynamics) Dynamics(s, local, speed, m, dt, w);
            if (s.IsEpicWalking) EpicPosture(w * MotionMath.SmoothStep01(speed / 0.8f), m);
            ChestAndHead(w);
            if (!DebugNoArms && !V2Grundlinie) ShoulderPosture(speed, attacking, w);
            if (!DebugNoArms && !V2Grundlinie) ArmCarry(speed, attacking, m, w);
            if (!DebugNoFeet) Feet(s, speed, attacking, m, dt, w);
        }

        // ================================================================== Ausrichtung (§17/§18/§101)

        private void Orient(in MotorState s, Vector3 local, float speed, bool attacking, bool combat, in MotionModifiers m, float dt, bool lag)
        {
            float offset = 0f;
            float freq;
            if (_motor.IsDodging)
            {
                Vector3 d = Quaternion.Euler(0f, -s.Yaw, 0f) * s.DodgeDirection;
                offset = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                freq = 9f;
            }
            else
            {
                if (speed > 0.5f && !attacking)
                {
                    float side = combat ? maxSideTurn * 0.75f : maxSideTurn;
                    offset = local.z >= -0.2f
                        ? Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -side, side)
                        : Mathf.Clamp(Mathf.Atan2(-local.x, -local.z) * Mathf.Rad2Deg, -maxBackTurn, maxBackTurn);
                }
                // Masse: im Stand dreht der Koerper traeger (die Fuesse treten um), in Bewegung schneller
                freq = (speed > 0.5f ? 5.5f : 2.6f) / Mathf.Sqrt(Mathf.Max(0.3f, m.turnInertia));
            }
            float target = s.Yaw + offset;
            if (!_yawInit || !lag) { _visYaw.Reset(target); _yawInit = true; }
            else
            {
                _visYaw.Update(target, dt, freq, 0.85f);
                // Nie sichtbar „abreissen“: begrenzter Nachlauf
                float maxLag = speed > 0.5f ? 45f : 100f;
                float lagNow = Mathf.DeltaAngle(target, _visYaw.Value);
                if (Mathf.Abs(lagNow) > maxLag) _visYaw.Value = target + Mathf.Sign(lagNow) * maxLag;
            }
            _model.localRotation = Quaternion.Euler(0f, Mathf.DeltaAngle(s.Yaw, _visYaw.Value), 0f);

            // Brust (und Kopf) zur eigentlichen Blickrichtung zurueckdrehen: im Kampf fast ganz (Gegner im Blick),
            // sonst teilweise. Beim Drehen im Stand fuehrt so der Oberkoerper die Drehung an.
            float back = Mathf.DeltaAngle(_visYaw.Value, s.Yaw);
            float share = _motor.IsDodging ? 0f : attacking ? 0.9f : combat ? 0.85f : speed > 0.5f ? 0.3f : 0.55f;
            _chestTurn = Mathf.Clamp(back * share, -60f, 60f);
        }

        // ================================================================== Ausschlag (§10/§21/§130)

        private void Amplitudes(float speed, bool attacking, in MotionModifiers m, float dt, float w)
        {
            if (!_emaInit)
            {
                _emaLUA = Local(_lUpperArm); _emaLLA = Local(_lLowerArm); _emaRUA = Local(_rUpperArm); _emaRLA = Local(_rLowerArm);
                _emaHips = Quaternion.Inverse(_model.rotation) * _hips.rotation;
                _emaSpine = Local(_spine); _emaChest = Local(_chest);
                _emaHead = Quaternion.Inverse(_model.rotation) * _head.rotation;
                _emaInit = true;
            }
            const float hl = 0.35f; // Mittellage ueber etwa einen Schrittzyklus
            _emaLUA = MotionMath.Damp(_emaLUA, Local(_lUpperArm), hl, dt);
            _emaLLA = MotionMath.Damp(_emaLLA, Local(_lLowerArm), hl, dt);
            _emaRUA = MotionMath.Damp(_emaRUA, Local(_rUpperArm), hl, dt);
            _emaRLA = MotionMath.Damp(_emaRLA, Local(_rLowerArm), hl, dt);
            _emaSpine = MotionMath.Damp(_emaSpine, Local(_spine), hl, dt);
            _emaChest = MotionMath.Damp(_emaChest, Local(_chest), hl, dt);
            Quaternion hipsModel = Quaternion.Inverse(_model.rotation) * _hips.rotation;
            _emaHips = MotionMath.Damp(_emaHips, hipsModel, hl, dt);
            Quaternion headModel = Quaternion.Inverse(_model.rotation) * _head.rotation;
            _emaHead = MotionMath.Damp(_emaHead, headModel, 0.25f, dt);

            // Nur in Bewegung – im Stand gehoert die Haltung dem StancePoser
            float move = MotionMath.SmoothStep01((speed - 0.3f) / 1.2f) * w;
            if (move <= 0.001f) return;

            if (!attacking)
            {
                float arm = Mathf.Lerp(1f, m.armSwingScale, move);
                Scale(_lUpperArm, _emaLUA, arm); Scale(_lLowerArm, _emaLLA, Mathf.Lerp(1f, arm, 0.6f));
                Scale(_rUpperArm, _emaRUA, arm); Scale(_rLowerArm, _emaRLA, Mathf.Lerp(1f, arm, 0.6f));
                float upper = Mathf.Lerp(1f, m.upperBodyMotion, move);
                Scale(_spine, _emaSpine, upper);
                Scale(_chest, _emaChest, upper);
            }
            // Becken ruhiger (schwer: Huefte stabil, §10)
            float pelvis = Mathf.Lerp(1f, 1f - 0.75f * Mathf.Clamp01(m.pelvisStability), move);
            Quaternion hipsNow = Quaternion.Inverse(_model.rotation) * _hips.rotation;
            Quaternion hipsScaled = Quaternion.SlerpUnclamped(_emaHips, hipsNow, pelvis);
            Vector3 hipsPos = _hips.position;
            _hips.rotation = _model.rotation * hipsScaled;
            _hips.position = hipsPos;

            // Kopf haelt den Blick ruhig (Menschen stabilisieren den Blick beim Laufen)
            Quaternion headNow = Quaternion.Inverse(_model.rotation) * _head.rotation;
            _head.rotation = _model.rotation * Quaternion.Slerp(headNow, _emaHead, 0.45f * move);
        }

        private static Quaternion Local(Transform t) => t != null ? t.localRotation : Quaternion.identity;

        private static void Scale(Transform t, Quaternion center, float k)
        {
            if (t == null || Mathf.Abs(k - 1f) < 0.001f) return;
            t.localRotation = Quaternion.SlerpUnclamped(center, t.localRotation, k);
        }

        // ================================================================== Gewicht (§15/§44/§100)

        private void Dynamics(in MotorState s, Vector3 local, float speed, in MotionModifiers m, float dt, float w)
        {
            float fwd = local.z;
            float rawAccel = (fwd - _lastFwdSpeed) / dt;
            _lastFwdSpeed = fwd;
            _accel = MotionMath.Damp(_accel, Mathf.Clamp(rawAccel, -40f, 40f), 0.06f, dt);
            float yawRate = Mathf.DeltaAngle(_lastYaw, s.Yaw) / dt;
            _lastYaw = s.Yaw;
            _yawRate = MotionMath.Damp(_yawRate, Mathf.Clamp(yawRate, -720f, 720f), 0.08f, dt);

            float mass = Mathf.Max(0.4f, m.massInertia);
            bool grounded = s.Grounded;

            // Vorlage: anfahren = vor, bremsen = zurueck; Nachfedern ueber die geringe Daempfung (schwer setzt sich)
            float slope = grounded ? SlopeAlongMotion(local, speed) : 0f;
            float pitchTarget = grounded ? Mathf.Clamp(_accel * 0.45f * Mathf.Sqrt(mass), -9f, 12f) + slope * 0.3f + m.torsoLean : 0f;
            float pitch = _pitch.Update(pitchTarget, dt, 2.4f / Mathf.Sqrt(mass), 0.42f + 0.25f / mass);

            // In Kurven nach innen lehnen
            float rollTarget = grounded ? Mathf.Clamp(speed * _yawRate * Mathf.Deg2Rad * 1.1f, -11f, 11f) : 0f;
            float roll = _roll.Update(rollTarget, dt, 3f, 0.75f);

            // Fussaufsatz: kurzes Einsacken (Masse), aus dem Uebergang „Fuss tief“ erkannt
            // 18.10. (Gang-Prüfung: Knie beim Aufsetzen 33–37° statt 18–24° wie im Clip, Standbein durchgehend 26°): das
            // künstliche Einsacken kam ZUSÄTZLICH zur echten Abfederung der Aufnahme – Hüfte bis 5 cm tiefer, Knie geknickt.
            // Mit Mocap-Grundlinie aus; das Ereignis (Schritt-Geräusch/Staub) bleibt.
            float kick = V2Grundlinie ? 0f : 0.22f * Mathf.Max(0.5f, m.landingWeight) * Mathf.Clamp01(speed / 7f) * mass;
            if (grounded && speed > 0.8f)
            {
                if (_left.lift < 0.04f && !_left.wasLow) { _bob.Kick(-kick); Fussaufsatz?.Invoke(this, _left.foot.position, speed); }
                if (_right.lift < 0.04f && !_right.wasLow) { _bob.Kick(-kick); Fussaufsatz?.Invoke(this, _right.foot.position, speed); }
            }
            _left.wasLow = _left.lift < 0.04f;
            _right.wasLow = _right.lift < 0.04f;
            float bob = Mathf.Clamp(_bob.Update(0f, dt, 4.5f, 0.55f), -0.05f, 0.02f);

            pitch *= w; roll *= w;
            // 18.10.: Schwerpunkt-Absenkung (schwere Rüstung, 1,5 cm) nur in Bewegung – im Stand knickte sie die Knie auf 25°
            // (Standhaltung stellt die Beine fast gestreckt, die Absenkung kam danach)
            // 18.10. (2): auch in Bewegung knickte sie mit Mocap die Knie um ~8° (Gehen Ø 37° statt 27°, wirkt wie Anschleichen) – mit
            // Mocap-Grundlinie aus; schwere Rüstung zeigt sich über Schrittlänge/Oberkörper (strideScale, upperBodyMotion).
            float comDrop = V2Grundlinie ? 0f : m.centerOfMassDrop * MotionMath.SmoothStep01((speed - 0.3f) / 1.2f);
            _hips.position += _u * (bob * w - comDrop * w);

            // 22.10. nachts (Max: „der Char hüpft ständig“; Lab: Hüfte beim Laufen je Schritt 1,02 → 0,82 m, also 20 cm auf und ab –
            // so viel hat der Lauf-Clip auf dem Tripo-Körper, echte Läufer 6–10 cm): ab Joggen das Auf und Ab der Hüfte um ihren
            // gleitenden Mittelwert auf 45 % dämpfen. Die Füsse bleiben über die Bein-IK unten am Boden (Knie federn stattdessen).
            {
                float h = Vector3.Dot(_hips.position - _root.position, _u);
                if (!_hubInit) { _hubMittel = h; _hubInit = true; }
                _hubMittel = MotionMath.Damp(_hubMittel, h, 0.22f, dt);
                float daempf = Mathf.Lerp(1f, 0.45f, MotionMath.SmoothStep01(Mathf.InverseLerp(2.4f, 4.2f, speed))) ;
                if (grounded && daempf < 0.999f) _hips.position += _u * (((_hubMittel + (h - _hubMittel) * daempf) - h) * w);
            }
            if (Mathf.Abs(pitch) > 0.01f || Mathf.Abs(roll) > 0.01f)
            {
                RotateAbout(_hips, pitch * 0.2f, roll * 0.25f);
                RotateAbout(_spine, pitch * 0.3f, roll * 0.3f);
                RotateAbout(_chest, pitch * 0.3f, roll * 0.25f);
                RotateAbout(_upperChest, pitch * 0.2f, roll * 0.2f);
            }
        }

        /// <summary>Steigung in Laufrichtung (Grad, bergauf positiv) aus zwei Bodenproben vor und hinter der Figur.</summary>
        private float SlopeAlongMotion(Vector3 local, float speed)
        {
            Vector3 dir = speed > 0.3f ? (_root.rotation * new Vector3(local.x, 0f, local.z)).normalized : _root.forward;
            Vector3 p = _root.position;
            if (!Probe(p + dir * 0.45f, out float a, out _) || !Probe(p - dir * 0.45f, out float b, out _)) return 0f;
            return Mathf.Atan2(a - b, 0.9f) * Mathf.Rad2Deg;
        }

        /// <summary>Um die Modellachsen neigen: pitch = vor (+) / zurueck, roll = rechts (+) / links.</summary>
        private void RotateAbout(Transform t, float pitchDeg, float rollDeg)
        {
            if (t == null) return;
            Quaternion q = Quaternion.identity;
            if (Mathf.Abs(pitchDeg) > 1e-4f) q = Quaternion.FromToRotation(_u, Vector3.RotateTowards(_u, _f, pitchDeg * Mathf.Deg2Rad, 0f)) * q;
            if (Mathf.Abs(rollDeg) > 1e-4f) q = Quaternion.FromToRotation(_u, Vector3.RotateTowards(_u, _r, rollDeg * Mathf.Deg2Rad, 0f)) * q;
            Vector3 p = t.position;
            t.rotation = q * t.rotation;
            t.position = p;
        }

        // ================================================================== Arme am Koerper (Max: „Skeletthaltung der Arme“)

        /// <summary>
        /// Die Bibliotheks-Clips halten die Arme deutlich vom Koerper weg (auf breite Koerper uebertragen wirkt das wie
        /// eine A-Pose). Hier werden die Oberarme in der Frontalebene an den Rumpf gefuehrt – so weit, wie Koerper und
        /// Ruestung es erlauben (schwere Ruestung: Schulterplatten halten die Arme etwas weiter ab). Vor-/Rueckschwung
        /// bleibt unberuehrt.
        /// </summary>
        /// <summary>16.10.: Die Arm-Fuehrung war fuer die Quaternius-Clips gedacht (Arme weit vom Koerper, Ellbogen im Stand
        /// angewinkelt). Mit den CMU-Aufnahmen (MOV-127) hielt sie die Ellbogen beim Joggen auf einem festen Winkel (117°) und
        /// nahm den Armschwung (Motion-Lab: Handschwung 21 cm statt 40+). Fuer Mocap-Fortbewegung aus; bei Bedarf einschaltbar.</summary>
        [SerializeField] private bool armCarry = false;

        private void ArmCarry(float speed, bool attacking, in MotionModifiers m, float w)
        {
            if (attacking || !armCarry) return;
            if (w <= 0.001f) return;
            ArmorCategory armor = _orch != null ? _orch.Context.Armor : ArmorCategory.None;
            float target = armor == ArmorCategory.Heavy ? 14f : armor == ArmorCategory.Medium ? 11f : 8f;
            // Waffe in der Hand (Haltung der Halte-Ebene) nicht zerren – nur die freie Hand voll
            if (_hands == null) _hands = GetComponent<WeaponHandPoser>();
            bool swordHeld = _hands != null && _hands.RightBusy;
            bool leftBusy = _hands != null && _hands.LeftBusy;
            // Ellbogen: Die Clips (und die Standhaltung) winkeln die Arme fast rechtwinklig an – wirkt wie
            // Posieren. Locker haengende Arme sind nur leicht gebeugt; erst mit dem Tempo mehr (Rennen braucht den Winkel).
            float maxFlex = Mathf.Lerp(elbowFlexIdle, elbowFlexRun, Mathf.InverseLerp(1.5f, 6f, speed));
            bool combat = _orch != null && _orch.Context.Stance == MotionStance.Combat;
            bool shieldUp = leftBusy;
            // Im Stand haengen die Oberarme nach hinten (Hand hinter dem Oberschenkel) – locker haengen sie
            // senkrecht bis leicht vor dem Koerper. Beim Gehen bleibt der Armschwung unberuehrt.
            float still = w * (1f - MotionMath.SmoothStep01(speed / 0.8f));
            // 16.10.: Im Stand gehoert die Armhaltung der Standhaltung (StancePoser) – vorher streckte diese Schicht die Arme
            // der Haltung wieder gerade (steife, nach vorn haengende Arme, Motion-Lab „haltung“ h0 gegen h2).
            if (_stance != null && _stance.Weight > 0.001f) w *= 1f - _stance.Weight;
            if (w <= 0.001f) return;
            // Arme, die die Standhaltung gerade fuehrt (Hand an der Huefte …), nicht zusaetzlich verbiegen
            float lFree = 1f - (_stance != null ? _stance.LeftArmOwn : 0f);
            float rFree = 1f - (_stance != null ? _stance.RightArmOwn : 0f);
            if (!shieldUp) HangArm(_lUpperArm, _lLowerArm, still * (combat ? 0f : 1f) * lFree);
            if (!swordHeld) HangArm(_rUpperArm, _rLowerArm, still * rFree);
            // 17.10. (Max: Bild – „Arme zu brav“ beim Sprint): Armschwung mit der Schrittdehnung mitskalieren (Hand vorn hoeher,
            // hinten weiter zurueck), sonst passen weite Schritte und kurze Armpendel nicht zusammen.
            // 17.10. abends (Max, ASHVEIL-Koerper: „Arme zu steif, zu stark angewinkelt“): Grundverstaerkung des Armschwungs
            // ab Joggen (Lab: Handschwung Joggen nur 11…26 cm, Ellbogen innen 43–77°), dazu die Schrittdehnung
            float basisGain = 0.35f * MotionMath.SmoothStep01(Mathf.InverseLerp(2.5f, 4.5f, speed));
            if (_strideScale > 1.001f || basisGain > 0.001f)
            {
                float gain = 1f + (basisGain + (_strideScale - 1f) * 1.6f) * w;
                if (!shieldUp) SwingArm(_lUpperArm, _lLowerArm, gain * lFree);
                if (!swordHeld) SwingArm(_rUpperArm, _rLowerArm, gain * rFree);
            }
            if (!shieldUp) RelaxElbow(_lUpperArm, _lLowerArm, _anim.GetBoneTransform(HumanBodyBones.LeftHand), maxFlex, w * lFree);
            if (!swordHeld) RelaxElbow(_rUpperArm, _rLowerArm, _anim.GetBoneTransform(HumanBodyBones.RightHand), maxFlex, w * rFree);
            Adduct(_lUpperArm, _lLowerArm, -_r, target, w * lFree);
            Adduct(_rUpperArm, _rLowerArm, _r, target, w * rFree * (swordHeld ? 0.5f : 1f));
        }

        /// <summary>Vor-/Rueckschwung des Oberarms (um die Seitachse) um den Faktor <paramref name="gain"/> verstaerken.</summary>
        private void SwingArm(Transform upper, Transform lower, float gain)
        {
            if (upper == null || lower == null || gain <= 1.001f) return;
            Vector3 d = lower.position - upper.position;
            float pitch = Mathf.Atan2(Vector3.Dot(d, _f), Vector3.Dot(d, -_u)) * Mathf.Rad2Deg; // + = nach vorn
            float extra = Mathf.Clamp(pitch * (gain - 1f), -35f, 35f);
            Vector3 axis = Vector3.Cross(-_u, _f).normalized;
            upper.rotation = Quaternion.AngleAxis(extra, axis) * upper.rotation;
        }

        private void HangArm(Transform upper, Transform lower, float k)
        {
            if (upper == null || lower == null || k <= 0.001f) return;
            Vector3 d = lower.position - upper.position;
            float pitch = Mathf.Atan2(Vector3.Dot(d, _f), Vector3.Dot(d, -_u)) * Mathf.Rad2Deg; // + = nach vorn
            float want = 5f;
            if (pitch >= want - 2f) return;
            Vector3 axis = Vector3.Cross(-_u, _f).normalized; // positive Drehung fuehrt „unten“ nach „vorn“
            upper.rotation = Quaternion.AngleAxis((want - pitch) * k, axis) * upper.rotation;
        }

        private void RelaxElbow(Transform upper, Transform lower, Transform hand, float maxFlex, float w)
        {
            if (upper == null || lower == null || hand == null) return;
            Vector3 a = lower.position - upper.position, b = hand.position - lower.position;
            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return;
            float flex = Vector3.Angle(a, b);
            if (flex <= maxFlex) return;
            // Weich: knapp ueber der Grenze kaum Eingriff, grosse Beugung wird deutlich gestreckt
            float room = 12f;
            float soft = maxFlex + room * (float)System.Math.Tanh((flex - maxFlex) / room);
            Vector3 axis = Vector3.Cross(a, b);
            if (axis.sqrMagnitude < 1e-8f) return;
            lower.rotation = Quaternion.AngleAxis(-(flex - soft) * w, axis.normalized) * lower.rotation;
        }

        private void Adduct(Transform upper, Transform lower, Vector3 outward, float targetDeg, float k)
        {
            if (upper == null || lower == null) return;
            Vector3 d = lower.position - upper.position;
            float o = Vector3.Dot(d, outward), dn = Vector3.Dot(d, -_u);
            float planar = Mathf.Sqrt(o * o + dn * dn);
            if (planar < 1e-4f) return;
            float abd = Mathf.Atan2(o, dn) * Mathf.Rad2Deg;
            float excess = abd - targetDeg;
            if (excess <= 0f) return;
            // Staerker, je mehr der Arm wirklich seitlich haengt (nicht beim weiten Vor-/Rueckschwung)
            float frontal = planar / Mathf.Max(1e-4f, d.magnitude);
            float angle = excess * k * MotionMath.SmoothStep01(frontal * 1.2f);
            // Drehachse: Vorwaertsachse – positiv dreht „nach aussen“ hin zu „nach unten“
            Vector3 axis = Vector3.Cross(outward, -_u).normalized;
            upper.rotation = Quaternion.AngleAxis(angle, axis) * upper.rotation;
        }

        /// <summary>
        /// Epischer Gang (§13): Brust leicht gehoben, Schultern ruhig, Kopf gerade – ruhig und selbstbewusst,
        /// keine Brust-raus-Parodie. Schwere Figuren tragen es geerdeter (weniger Anheben).
        /// </summary>
        private void EpicPosture(float w, in MotionModifiers m)
        {
            if (w <= 0.001f) return;
            float lift = Mathf.Lerp(4.5f, 2.5f, Mathf.Clamp01((m.massInertia - 1f) / 0.5f));
            RotateAbout(_chest, -lift * 0.5f * w, 0f);
            RotateAbout(_upperChest, -lift * 0.5f * w, 0f);
            // Kopf gleicht die Brust aus und bleibt waagerecht
            RotateAbout(_neck, lift * 0.6f * w, 0f);
            // Arme ruhiger, naeher am Koerper
            float calm = Mathf.Lerp(1f, 0.8f, w);
            Scale(_lUpperArm, _emaLUA, calm);
            Scale(_rUpperArm, _emaRUA, calm);
        }

        private void ChestAndHead(float w)
        {
            float a = _chestTurn * w;
            if (Mathf.Abs(a) < 0.05f) return;
            Yaw(_spine, a * 0.25f);
            Yaw(_chest, a * 0.3f);
            Yaw(_upperChest, a * 0.25f);
            // Kopf fuehrt die Drehung etwas mehr an (Blick zuerst)
            Yaw(_neck, a * 0.1f);
            Yaw(_head, Mathf.Clamp(a * 0.25f, -25f, 25f));
        }

        /// <summary>17.10. (Max: „schau dir auch die Schultern und die Koerperhaltung an“): Beim Joggen/Rennen/Sprint zieht die
        /// Aufnahme die Schultern hoch und nach vorn, der obere Ruecken rundet sich (Buckel, Kopf vorgeschoben). Hier: Schultern
        /// senken und zurueck, oberen Ruecken strecken, Kopf zurueck – mit dem Tempo eingeblendet (ab 2,5 m/s voll bei 4,5).</summary>
        private void ShoulderPosture(float speed, bool attacking, float w)
        {
            if (attacking) return;
            float k = w * MotionMath.SmoothStep01(Mathf.InverseLerp(2.5f, 4.5f, speed));
            if (_stance != null) k *= 1f - _stance.Weight;
            if (k <= 0.001f) return;
            // oberer Ruecken strecken (Buckel raus), Kopf dafuer zurueck – Blick bleibt waagerecht
            RotateAbout(_upperChest, -5f * k, 0f);
            RotateAbout(_neck, 1.5f * k, 0f);
            RotateAbout(_head, 1.5f * k, 0f);
            // Schultern senken (um die Vorwaertsachse) und zurueckziehen (um die Hochachse)
            ShoulderDrop(_lShoulder, -1f, 7f * k, 6f * k);
            ShoulderDrop(_rShoulder, 1f, 7f * k, 6f * k);
        }

        private void ShoulderDrop(Transform clav, float side, float dropDeg, float backDeg)
        {
            if (clav == null) return;
            // side: +1 rechts, -1 links. Senken: Schulterspitze nach unten = Drehung um die Vorwaertsachse; zurueck: um die Hochachse.
            Quaternion q = Quaternion.AngleAxis(-dropDeg * side, _f) * Quaternion.AngleAxis(backDeg * side, _u);
            Vector3 p = clav.position;
            clav.rotation = q * clav.rotation;
            clav.position = p;
        }

        private void Yaw(Transform t, float deg)
        {
            if (t == null) return;
            t.rotation = Quaternion.AngleAxis(deg, _u) * t.rotation;
        }

        // ================================================================== Fuesse (§17/§42–§46)

        /// <summary>
        /// Fussgleiten beseitigen: Wie schnell bewegt sich das Standbein in der Animation nach hinten? Genau so schnell
        /// muss der Koerper laufen. Das Verhaeltnis zum echten Tempo wird zum Abspieltempo („LocoRate“). Gemessen an der
        /// tatsaechlich abgespielten Animation (jeder Koerper, jede Mischung) – geteilt durch das zuletzt gesetzte Tempo,
        /// deshalb keine Rueckkopplung.
        /// </summary>
        private void MatchStride(in MotorState s, Vector3 local, float speed, float dt)
        {
            if (_hasRateParam < 0)
            {
                _hasRateParam = 0;
                foreach (AnimatorControllerParameter p in _anim.parameters) if (p.nameHash == LocoRateId) _hasRateParam = 1;
            }
            if (_hasRateParam == 0) return;
            float dir = local.z >= -0.2f ? 1f : -1f;
            // 15.10. (Fuss-Analyse): früher das MAXIMUM der Standbein-Geschwindigkeiten je Bild. In den CMU-Clips liegt die
            // Spitze im Stand 20–35 % über dem Laufband (Gehen Median 2,28 / Max 2,78–2,85 m/s; Rennen Knöchel 3,5–5,4 m/s),
            // die kurze Glättung (0,12 s) liess das Abspieltempo innerhalb jedes Schritts schwanken → Standfuss rutscht
            // (Sprint links Ø 6,8 cm). Jetzt: Mittel der Standballen, Ausreisser verworfen, über ~einen halben Zyklus geglättet.
            float sum = 0f; int n = 0;
            Sample(_left, dt, dir, ref sum, ref n);
            Sample(_right, dt, dir, ref sum, ref n);
            bool moving = s.Grounded && speed > 0.8f && !_motor.IsDodging;
            // 15.10. (3), Motion-Lab-Fussmitschrift: Der Standfuss lief beim Gehen mit ~Koerpertempo rueckwaerts (Takt doppelt so
            // schnell wie die Aufnahme, Abspieltempo am Anschlag 2,0). Ursache: der ERSTE Messwert setzte die Schaetzung, spaetere
            // Werte ausserhalb 0,6–1,5 × davon wurden verworfen – war der erste falsch (Anlaufen, Uebergang), blieb die
            // Schaetzung fuer immer daneben. Jetzt: Median der letzten ~0,5 s, ohne Ausschluss gegen einen Startwert.
            if (moving && n > 0 && sum / n > 0.3f)
            {
                float native = sum / n / Mathf.Max(0.1f, _appliedRate);
                _nativeBuf[_nativeHead] = native; _nativeHead = (_nativeHead + 1) % _nativeBuf.Length;
                if (_nativeCount < _nativeBuf.Length) _nativeCount++;
                _nativeAge += dt;
                if (_nativeCount >= 6)
                {
                    System.Array.Copy(_nativeBuf, _nativeSort, _nativeCount);
                    System.Array.Sort(_nativeSort, 0, _nativeCount);
                    float med = _nativeSort[_nativeCount / 2];
                    _nativeEst = _nativeEst < 0f ? med : MotionMath.Damp(_nativeEst, med, 0.15f, dt);
                }
            }
            // Obergrenze 2,0 (15.10.): Sprint 9 m/s auf der Rennaufnahme (4,66 m/s) braucht 1,93 – sonst rutschen die Füße
            // 16.10. Anfahren (Lab bis 22 cm Gleiten): Beim Stehen wurden Laufband-Schaetzung und Abspieltempo verworfen (Tempo → 1,
            // Schaetzung → unbekannt). Beim Losgehen lief der Clip darum 0,2–0,3 s mit falschem Tempo (Sprint-Clip 4,6 m/s bei
            // 9 m/s Koerpertempo = 50 % Gleiten). Jetzt: Schaetzung und Tempo im Stand behalten (der Stehen-Clip ist davon
            // unabhaengig), beim Losgehen sofort das passende Tempo; Daempfung bei grossem Sprung kuerzer.
            float warpMax = V2Grundlinie ? (V2Schrittdehnung ? V2WarpMax : 1f) : strideWarpMax;
            // 20.10.: Gehen 2,25 m/s auf dem Geh-Clip (~1,05 m/s) braucht 2,15 – bei 2,0 rutschte der Standfuss 5–10 cm
            float rateMax = V2Grundlinie && V2Schrittdehnung ? V2RateMax : warpMax > 1.001f ? Mathf.Max(1f, strideRateMax) : 2.3f;
            float want = moving && _nativeEst > 0.3f ? Mathf.Clamp(speed / _nativeEst, 0.65f, rateMax) : _rate;
            if (!moving) { _nativeCount = 0; _nativeAge = 0f; }
            float tau = Mathf.Abs(want - _rate) > 0.3f ? 0.05f : 0.15f;
            _rate = MotionMath.Damp(_rate, want, tau, dt);
            // Schrittdehnung: was das Abspieltempo nicht abdeckt, bekommt die Schrittlaenge (Fuesse laufen dann genau mit dem
            // Koerpertempo: Standfuss-Tempo = Clip × Tempo × Dehnung)
            float sWant = moving && _nativeEst > 0.3f ? Mathf.Clamp(speed / (_nativeEst * Mathf.Max(0.65f, _rate)), 1f, Mathf.Max(1f, warpMax)) : 1f;
            _strideScale = MotionMath.Damp(_strideScale, sWant, 0.15f, dt);
            _anim.SetFloat(LocoRateId, GlobalEnabled ? _rate : 1f);
            _appliedRate = GlobalEnabled ? _rate : 1f;
        }

        private void Sample(Leg l, float dt, float dir, ref float sum, ref int n)
        {
            // Ballen statt Knöchel: die Übertragung verriegelt im Stand den Ballen (er läuft genau mit dem Laufband), der
            // Knöchel hebt beim Abrollen ab und läuft ungleichmässig (Rennen 3,5–5,4 m/s statt 4,2; Joggen Mittel −6 %).
            Vector3 lp = _model.InverseTransformPoint(l.toeAnim);
            float h = Vector3.Dot(l.toeAnim - RootFoot, _u);
            l.ballMin = Mathf.Min(l.ballMin + 0.03f * dt, h);                 // folgt Bodenkontakten sofort, steigt nur langsam
            if (l.hasPrev && h < l.ballMin + 0.015f)
            {
                float v = -(lp.z - l.prevLocal.z) / dt * dir;
                if (v > 0f) { sum += v; n++; }
            }
            l.prevLocal = lp;
            l.hasPrev = true;
        }

        private float _calmT;

        private void CaptureFeet(bool idle, float dt)
        {
            _calmT = idle ? _calmT + dt : 0f;
            Capture(_left, idle);
            Capture(_right, idle);
        }

        private void Capture(Leg l, bool idle)
        {
            l.animPos = l.foot.position;
            l.animRot = l.foot.rotation;
            float h = Vector3.Dot(l.animPos - RootFoot, _u);
            // Knoechelhoehe im ruhigen Stand lernen – daraus die Anhebung ueber dem Boden
            if (l.restHeight < 0f) l.restHeight = 0.085f;
            if (idle) l.restHeight = Mathf.Lerp(l.restHeight, Mathf.Clamp(h, 0.03f, 0.16f), 0.05f);
            // Fussdrehung relativ zum Koerper im ruhigen Stand lernen. 16.10.: erst nach 0,4 s Stand, nur mit Fuss am Boden
            // und gefiltert – vorher uebernahm sie auch die Landepose nach dem Sprung (Zehen nach unten), und der festgehaltene
            // Fuss stand danach mit den Zehen 7 cm im Boden (Motion-Lab „Sprung aus dem Stand“).
            if (idle && _calmT > 0.4f && h < l.restHeight + 0.02f)
            {
                Quaternion r = Quaternion.Inverse(Quaternion.LookRotation(Vector3.ProjectOnPlane(_f, _u), _u)) * l.animRot;
                l.restRot = l.hasRestRot ? Quaternion.Slerp(l.restRot, r, 0.05f) : r;
                l.hasRestRot = true;
            }
            l.lift = Mathf.Max(0f, h - l.restHeight);
            // Zehe: Höhe in Ruhe ebenso lernen (Ballengelenk liegt je nach Figur 2–5 cm über dem Boden)
            l.toeAnim = l.toe != null ? l.toe.position : l.animPos;
            if (l.toeRest < 0f) l.toeRest = 0.03f;
            if (idle && l.toe != null) l.toeRest = Mathf.Lerp(l.toeRest, Mathf.Clamp(Vector3.Dot(l.toeAnim - RootFoot, _u), 0f, 0.08f), 0.05f);
        }

        private Vector3 _lastRootPos;

        private void TrackRoot(in MotorState s)
        {
            Vector3 p = _root.position;
            // Versetzt (Teleport, Wiederbeleben, Instanzwechsel): alles Gelernte verwerfen, sonst greifen Fuesse ins Leere
            if (_rootInit && (p - _lastRootPos).sqrMagnitude > 1.5f * 1.5f)
            {
                ResetState();
                _lastRootPos = p;
                return;
            }
            if (_rootInit && s.Grounded)
            {
                float dy = p.y - _lastRootPos.y;
                Vector3 d = p - _lastRootPos; d.y = 0f;
                // Stufen: der Controller springt, die Figur soll es nicht – Federn um den Sprung versetzen.
                // (Gleichmaessige Haenge steigen flacher als 1,5 : 1 und bleiben unberuehrt.)
                if (Mathf.Abs(dy) > 0.03f && Mathf.Abs(dy) < 0.8f && Mathf.Abs(dy) > d.magnitude * 1.5f)
                {
                    _pelvis.Value -= dy;
                    _left.ground.Value -= dy;
                    _right.ground.Value -= dy;
                }
            }
            _lastRootPos = p;
            _rootInit = true;
        }

        /// <summary>Sprung-Zustaende zuruecksetzen (nach Teleport, Neustart).</summary>
        public void ResetState()
        {
            _locked = false;
            _lockWeight = 0f;
            _stepping = null;
            _stopLatch = false;
            _pelvis.Reset(0f);
            _left.ground.Reset(0f);
            _right.ground.Reset(0f);
            _pitch.Reset(0f); _roll.Reset(0f); _bob.Reset(0f);
            _left.runLock = _right.runLock = false; _left.runW = _right.runW = 0f;
            _yawInit = false;
            _emaInit = false;
        }

        private void Feet(in MotorState s, float speed, bool attacking, in MotionModifiers m, float dt, float w)
        {
            Vector3 root = RootFoot;
            bool grounded = s.Grounded;

            // Schrittdehnung (siehe strideRateMax): Fusspositionen des Clips entlang der Laufrichtung vom Hueftpunkt aus strecken.
            // Der Standfuss laeuft dann mit Clip-Tempo × Abspieltempo × Dehnung = Koerpertempo – kein Gleiten, laengere Schritte.
            if (!DebugNoWarp && !_locked && grounded && speed > 0.5f && _strideScale > 1.001f && _moveDir.sqrMagnitude > 0.5f)
            {
                float k = (_strideScale - 1f) * w;
                // 17.10. (Max: Bild): Drehpunkt der Dehnung liegt VOR der Huefte – der Fuss landet dann nicht weit vorn (kein
                // Ueberschreiten/Bremsen), die Laenge kommt hinten beim Abdruck; der Standfuss laeuft weiter gleichmaessig.
                Vector3 c = _hips.position + _moveDir * (0.45f * (_strideScale - 1f));
                WarpStride(_left, _moveDir, c, k);
                WarpStride(_right, _moveDir, c, k);
            }

            // Bodenhoehe unter jedem Fuss
            Ground(_left, root, grounded, dt);
            Ground(_right, root, grounded, dt);

            // Stand: Fuesse bleiben stehen, beim Drehen wird umgetreten (§17)
            // 15.10. (3) Anhalten: Beim scharfen Abbremsen (Rennen → Stand in ~0,2 s) lief der Lauf-Clip weiter, waehrend der
            // Koerper fast stand – aufgesetzte Fuesse glitten mit 5–9 m/s ueber den Boden (Motion-Lab 11–24 cm). Ab starkem
            // Abbremsen unter 3,5 m/s gilt die Figur schon als „stehend“: Fuesse am Boden halten, Fuesse in der Luft setzen
            // auf und halten dann; den Rest erledigt das Umtreten. Endet, sobald wieder beschleunigt wird.
            float accel = dt > 1e-5f ? (speed - _prevSpeed) / dt : 0f;
            _prevSpeed = speed;
            // 15.10. (4), Fussmitschrift Anhalten: Der Motor bremst mit 45 m/s² (6,3 → 0 in 0,14 s). Die Bedingung „unter 3,5 m/s“
            // griff erst nach 0,06 s – da hatte der Standfuss schon abgestossen und lief weiter mit dem Clip. Jetzt zaehlt
            // starkes Bremsen (unter −20 m/s², nur der Motor-Stopp erreicht das) sofort als Stand: der Fuss am Boden haelt.
            // 16.10.: nur ohne Bewegungseingabe – mit gehaltener Taste ist starkes Bremsen kein Anhalten, sondern eine Stufe,
            // ein Hang oder ein Hindernis (Motion-Lab Treppe: Fuss 27 cm festgehalten und mitgeschleift).
            if (grounded && accel < -20f && _prevSpeedAlt > 2f && _motor.InputMagnitude < 0.1f) { _stopLatch = true; _stopMin = speed; _stopLatchT = 0f; }
            _prevSpeedAlt = speed;
            if (_stopLatch)
            {
                _stopMin = Mathf.Min(_stopMin, speed);
                _stopLatchT += dt;
                // Ende: wieder beschleunigt, in der Luft – oder die Figur laeuft nach 0,3 s immer noch (dann war es kein Anhalten,
                // sondern nur ein Tempowechsel, z. B. am Hang; der Motor-Stopp ist nach 0,15 s bei 0)
                if (!grounded || speed > _stopMin + 0.3f || (_stopLatchT > 0.3f && speed > 1f)) _stopLatch = false;
            }
            bool idle = grounded && (speed < 0.2f || _stopLatch) && !attacking && !_motor.IsDodging;
            _lockWeight = Mathf.MoveTowards(_lockWeight, idle ? 1f : 0f, dt / (idle ? 0.1f : 0.12f));
            float bodyYaw = BodyYaw();
            if (idle && !_locked)
            {
                _locked = true;
                _left.lockPos = _left.target;
                _right.lockPos = _right.target;
                _left.lockYaw = _right.lockYaw = bodyYaw;
                _stepping = null;
                // 15.10. (3), Motion-Lab „Anhalten aus dem Rennen“: Fuesse rutschten 16–24 cm, weil das Festhalten 0,1 s lang
                // einblendete, waehrend der Uebergang Rennen→Stand den Fuss noch schnell bewegte. Jetzt: ein Fuss, der schon
                // aufsteht, haelt SOFORT; ein Fuss in der Luft folgt der Animation bis er aufsetzt und haelt dann.
                BeginPlant(_left, root);
                BeginPlant(_right, root);
            }
            else if (!idle && _lockWeight <= 0f) { _locked = false; _stepping = null; }
            if (!idle) { _left.plantHold = _right.plantHold = 0f; _left.pending = _right.pending = false; _left.hasPlantTo = _right.hasPlantTo = false; }
            else if (_locked)
            {
                UpdatePlant(_left, root, bodyYaw, dt);
                UpdatePlant(_right, root, bodyYaw, dt);
            }

            if (_locked)
            {
                float mass = Mathf.Max(0.4f, m.massInertia);
                float turnRate = Mathf.Abs(_visYaw.Velocity);
                float stepDist = 0.13f * Mathf.Sqrt(mass);
                // Schnelles Drehen → kuerzere, raschere Schritte (sonst schleift der Fuss nach)
                float stepTime = 0.22f * Mathf.Sqrt(mass) / Mathf.Max(0.5f, m.transitionSpeed) / (1f + turnRate / 240f);
                float maxStride = 0.42f * Mathf.Sqrt(mass);
                if (_stepping == null)
                {
                    // Abstand vom natuerlichen Platz – auch die Verdrehung zaehlt (35° wie ein ganzer Schritt), sonst dreht der
                    // Koerper lange ueber dem stehenden Fuss weg und der Fuss muss sich am Ende verwinden
                    float el = Mathf.Max(Flat(_left.target - _left.lockPos), Mathf.Abs(Mathf.DeltaAngle(bodyYaw, _left.lockYaw)) / 35f * stepDist);
                    float er = Mathf.Max(Flat(_right.target - _right.lockPos), Mathf.Abs(Mathf.DeltaAngle(bodyYaw, _right.lockYaw)) / 35f * stepDist);
                    if (Mathf.Max(el, er) > stepDist)
                    {
                        // Zuerst der Fuss auf der Drehseite (rechts drehen → rechter Fuss oeffnet), ausser der andere hinkt weit hinterher
                        Leg turnSide = _visYaw.Velocity >= 0f ? _right : _left;
                        Leg other = turnSide == _left ? _right : _left;
                        float eTurn = turnSide == _left ? el : er, eOther = turnSide == _left ? er : el;
                        _stepping = (turnRate > 20f && eTurn > stepDist * 0.6f && eOther < eTurn * 1.6f) ? turnSide : (el >= er ? _left : _right);
                        _stepping.stepFrom = _stepping.lockPos;
                        _stepping.stepYawFrom = _stepping.lockYaw;
                        _stepT = 0f;
                    }
                }
                if (_stepping != null)
                {
                    Leg other = _stepping == _left ? _right : _left;
                    Vector3 to = _stepping.hasPlantTo ? _stepping.plantTo : _stepping.target;
                    Vector3 d = Vector3.ProjectOnPlane(to - _stepping.stepFrom, _u);
                    if (d.magnitude > maxStride) to = _stepping.stepFrom + d.normalized * maxStride + _u * Vector3.Dot(to - _stepping.stepFrom, _u);
                    _stepT += dt / stepTime;
                    float t = Mathf.Clamp01(_stepT);
                    // Erst anheben, dann versetzen, dann aufsetzen (15.10., Motion-Lab: Umtreten schleifte bis 27 cm über
                    // den Boden, weil Heben und Versetzen gleichzeitig begannen und der Bogen nur 7 cm hoch war).
                    // Waagerecht bewegt sich der Fuss nur zwischen 18 % und 85 % der Schrittzeit – dann ist er schon in der Luft.
                    float k = MotionMath.SmoothStep01(Mathf.InverseLerp(0.18f, 0.85f, t));
                    // 16.10. (3): Hoehe eigens – waagerecht ist der Fuss bei 72 % fertig, senkrecht erst bei 95 %: er setzt
                    // von oben auf statt schraeg hineinzurutschen (Lab Anhalten: Bremsfuss 20 cm mit 2–4 m/s ueber den Boden)
                    float kv = MotionMath.SmoothStep01(Mathf.InverseLerp(0.18f, 0.95f, t));
                    k = MotionMath.SmoothStep01(Mathf.InverseLerp(0.18f, 0.72f, t));
                    float arc = Mathf.Sin(t * Mathf.PI);
                    float lift = Mathf.Clamp(0.05f + 0.15f * d.magnitude, 0.065f, 0.1f);
                    // Seitlich vom Standbein weg ausweichen – Beine kreuzen sich nicht
                    Vector3 away = Vector3.ProjectOnPlane(_stepping.stepFrom - other.lockPos, _u);
                    away = away.sqrMagnitude > 1e-6f ? away.normalized : (_stepping == _left ? -_r : _r);
                    Vector3 mid = Vector3.Lerp(_stepping.stepFrom, to, 0.5f);
                    float clearance = Flat(mid - other.lockPos);
                    float push = Mathf.Max(0f, 0.2f - clearance);
                    Vector3 p = Vector3.Lerp(_stepping.stepFrom, to, k);
                    p += _u * (Vector3.Dot(to - _stepping.stepFrom, _u) * (kv - k) + arc * lift) + away * (push * arc);
                    _stepping.lockPos = p;
                    _stepping.lockYaw = Mathf.LerpAngle(_stepping.stepYawFrom, bodyYaw, k);
                    if (_stepT >= 1f)
                    {
                        _stepping.lockPos = to;
                        _stepping.lockYaw = bodyYaw;
                        if (_stepping.pending)
                        {
                            _stepping.pending = false; _stepping.plantHold = 1f;
                            // Landung flach: Fussdrehung = Ruhelage-Drehung (Blick in Laufrichtung, Sohle eben)
                            _stepping.plantRot = _stepping.hasRestRot ? _stepping.restRot : Quaternion.Inverse(BodyYawRot()) * _stepping.animRot;
                        }
                        _stepping.hasPlantTo = false;
                        _stepping = null;
                    }
                }
                // Sicherheit: nie weiter als ein grosser Schritt vom natuerlichen Platz entfernt festhalten
                if (Flat(_left.target - _left.lockPos) > 0.9f) { _left.lockPos = _left.target; _left.lockYaw = bodyYaw; }
                if (Flat(_right.target - _right.lockPos) > 0.9f) { _right.lockPos = _right.target; _right.lockYaw = bodyYaw; }
                // 16.10. (Motion-Lab „haltung“, Bild gegen Bild): Im Stand uebernimmt die Standhaltung (StancePoser) Huefte und
                // Fuesse und blendet sie nach dem Festhalten noch um – der festgehaltene Ort lag dann hoeher als der Fuss der
                // Haltung, das Becken sank darauf bis 8 cm ab: Knie gebeugt, Huefte zurueck („krumm“). Jetzt: Hoehe des
                // festgehaltenen Fusses folgt der Haltung (nur der Ort am Boden bleibt fest, damit das Umtreten weiter geht).
                // 19.10.: Die Höhe folgt IMMER dem Ziel (Animation + Boden), nicht nur mit Standhaltung – seit die Standhaltung aus
                // ist (18.10., UAL pur), blieb ein Fuss, der beim Festhalten gerade gehoben war, dauerhaft in der Luft
                // (Motion-Lab „stufen“ Stehen: linker Ballen 18,6 cm über dem Boden, nur Animator 3,3 cm).
                if (_stepping != _left && !_left.pending) _left.lockPos += _u * Vector3.Dot(_left.target - _left.lockPos, _u);
                if (_stepping != _right && !_right.pending) _right.lockPos += _u * Vector3.Dot(_right.target - _right.lockPos, _u);
                float stanceW = _stance != null ? _stance.Weight : 0f;
                if (stanceW > 0.001f)
                {
                    // 18.10. (Gang-Prüfung Stehen: Knie 25–45° nur mit Fuss-Schicht): der festgehaltene Ort stammte aus dem Clip
                    // (Füsse schmaler, weiter hinten), die Standhaltung stellt die Füsse breiter – das Bein wurde gestaucht. Ohne
                    // Drehung gleitet der Haltepunkt jetzt langsam (25 cm/s) auf den Platz der Haltung; beim Drehen bleibt er fest.
                    SettleLock(_left, bodyYaw, stanceW, dt);
                    SettleLock(_right, bodyYaw, stanceW, dt);
                }
                float lw = MotionMath.SmoothStep01(_lockWeight);
                _lockYawBlend = lw;
                _left.target = Vector3.Lerp(_left.target, _left.lockPos, Mathf.Max(lw, _left.plantHold));
                _right.target = Vector3.Lerp(_right.target, _right.lockPos, Mathf.Max(lw, _right.plantHold));
            }

            // Lauf-Verriegelung (siehe V2Fussverriegelung) – nicht im Stand (dort haelt _locked), nicht bei Angriff/Ausweichen
            if (!DebugNoRunLock && V2Fussverriegelung && grounded && !_locked && speed > 0.5f && !attacking && !_motor.IsDodging)
            {
                RunLock(_left, root, dt);
                RunLock(_right, root, dt);
            }
            else { RunRelease(_left, dt); RunRelease(_right, dt); }

            // Becken: so tief, dass der tiefere Fuss den Boden erreicht (Hang, Treppe). 15.10.: VOR der Schwungbein-Begrenzung
            // gemessen – vorher zog deren Absenkung das Becken mit (Joggen bis ~14 cm, Rennen bis ~17 cm je Schritt).
            float offL = Vector3.Dot(_left.target - _left.animPos, _u), offR = Vector3.Dot(_right.target - _right.animPos, _u);
            float pelvisTarget = Mathf.Clamp(Mathf.Min(offL, offR), -maxFootOffset, maxFootOffset * 0.5f);
            // Im Stand setzt die Standhaltung die Hueftenhoehe selbst (Beine fast gestreckt) – nicht zusaetzlich absenken
            if (_locked && _stance != null) pelvisTarget *= 1f - _stance.Weight;

            // Schwungbein: Der Gang-Clip hebt den Fuss sehr hoch (wirkt wie Marschieren/Anschleichen). Beim Gehen
            // weich auf eine natuerliche Hoehe begrenzen, mit dem Tempo freigeben (Rennen bleibt unveraendert).
            // Nicht auf Stufen/Hindernissen – dort braucht der Fuss die Hoehe.
            // 15.10.: „Rennen bleibt unveraendert“ galt mit den CMU-Clips nicht mehr (Knöchel im Schwung bis 34 cm Joggen /
            // 53 cm Rennen über der Grenze) – die Begrenzung blendet jetzt zwischen Gehen und Joggen aus.
            float capFade = 1f - MotionMath.SmoothStep01(Mathf.InverseLerp(2.3f, 4f, speed));
            if (!DebugNoCapLift && !V2OhneHubgrenze && grounded && !_locked && speed > 0.3f && !attacking && capFade > 0.001f)
            {
                float cap = Mathf.Lerp(liftCapWalk, liftCapRun, Mathf.InverseLerp(2.3f, 6f, speed));
                CapLift(_left, cap, capFade);
                CapLift(_right, cap, capFade);
            }
            _pelvis.Update(grounded && !DebugNoPelvis ? pelvisTarget : 0f, dt, 9f, 1f);
            // Schutz (NET-012, 02.10.): im Bot-Lasttest lieferten zwei Figuren NaN fuers Becken (2 816 Fehlermeldungen je Bot,
            // jede mit Stapelverfolgung). Ursache noch offen – ungueltige Werte verwerfen, Federn zuruecksetzen, einmal melden.
            if (!float.IsFinite(_pelvis.Value) || !IsFinite(_left.target) || !IsFinite(_right.target))
            {
                if (!_nanReported)
                {
                    _nanReported = true;
                    Debug.LogWarning($"[Ashveil] Haltung: ungueltige Fuss-/Beckenwerte bei '{name}' verworfen (NaN). Wird beobachtet.", this);
                }
                ResetState();
                return;
            }
            _hips.position += _u * (_pelvis.Value * w);

            // 18.10. (Gang-Prüfung Stehen: Knie 23° statt 11° nur mit Fuss-Schicht, Hüfte 1,5 cm tiefer): Im ruhigen Stand ohne
            // Drehen und ohne Umtreten stellt die Standhaltung die Füsse selbst – die Fuss-IK hier verschob nur die Hüfte.
            float stW = _stance != null ? _stance.Weight : 0f;
            float by = BodyYaw();
            // (plantHold bleibt nach dem Anhalten dauerhaft 1 – darf hier nicht zählen, sonst greift der ruhige Stand nie)
            bool ruhig = _locked && _stepping == null && stW > 0.9f
                && Mathf.Abs(Mathf.DeltaAngle(by, _left.lockYaw)) < 3f && Mathf.Abs(Mathf.DeltaAngle(by, _right.lockYaw)) < 3f;
            _ruhigW = Mathf.MoveTowards(_ruhigW, ruhig ? stW : 0f, dt / 0.25f);   // weich ein/aus, kein Ruck beim Losdrehen
            float fw = w * (1f - _ruhigW);
            Solve(_left, -1f, fw);
            Solve(_right, 1f, fw);
        }

        private bool _nanReported;
        private float _hubMittel; private bool _hubInit;
        private float _ruhigW;

        private void SettleLock(Leg lg, float bodyYaw, float stanceW, float dt)
        {
            if (_stepping == lg || Mathf.Abs(Mathf.DeltaAngle(bodyYaw, lg.lockYaw)) > 8f) return;
            Vector3 dh = Vector3.ProjectOnPlane(lg.target - lg.lockPos, _u);
            lg.lockPos += Vector3.ClampMagnitude(dh, 0.25f * dt) * stanceW;
        }

        // 16.10.: Versuch „Zyklus beim Losgehen auf 0,25 setzen“ (Play/CrossFade auf den Fortbewegungs-Zustand) verworfen –
        // Play liess beide Fuesse um 25 cm springen, CrossFade brachte nichts (Anfahren weiter 11–18 cm). Anfahren bleibt OFFEN
        // (braucht eine echte Anlauf-Aufnahme oder Schrittplanung wie beim Anhalten).
        private float _prevSpeed, _stopMin, _prevSpeedAlt, _stopLatchT;
        private bool _stopLatch;

        /// <summary>Koerper-Blickrichtung als Drehung (nur Gier, eben).</summary>
        private Quaternion BodyYawRot() { Vector3 f = Vector3.ProjectOnPlane(_f, _u); return f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f, _u) : Quaternion.identity; }

        /// <summary>Hoehe des Ballens ueber seiner Ruhehoehe auf dem Boden unter dem Fuss (m).</summary>
        private float ToeLift(Leg l, Vector3 root) => Vector3.Dot(l.toeAnim - root, _u) - l.ground.Value - l.toeRest;

        private static void WarpStride(Leg l, Vector3 dir, Vector3 c, float k)
        {
            float f = Vector3.Dot(l.animPos - c, dir);
            Vector3 hip = l.upper.position;
            float reach = Vector3.Distance(l.upper.position, l.lower.position) + Vector3.Distance(l.lower.position, l.foot.position);
            Vector3 p = l.animPos + dir * (f * k);
            // 17.10. (Ruck-Parcours: Unterschenkel 25° in einem Bild bei Clip-Zeit 0,04): die harte Reichweiten-Grenze liess
            // das Knie am Ende des Schritts einrasten. Jetzt weich: ab 86 % der Beinlaenge wird der Rest mit tanh gestaucht,
            // das Bein bleibt immer ein wenig gebeugt und die Bewegung stetig.
            // 18.10. (Gang-Prüfung: Knie im Gehen nie unter 28°, Füße 8–12 cm über dem Boden): die Grenze zog JEDEN Fuss weiter
            // als 86 % der Beinlänge zur Hüfte – auch das ganz normale, fast gestreckte Standbein der Aufnahme. Jetzt wirkt sie
            // nur auf die zusätzliche Dehnung: nie kürzer als die Animation selbst, höchstens 99,5 % der Beinlänge.
            float d0 = Vector3.Distance(l.animPos, hip);
            float r0 = Mathf.Max(reach * 0.86f, d0), r1 = Mathf.Max(reach * 0.97f, Mathf.Min(reach * 0.995f, d0 + 0.02f));
            if (r1 <= r0 + 1e-4f) r1 = r0 + 1e-4f;
            Vector3 a = p - hip; float d = a.magnitude;
            if (d > r0 && d > 1e-4f)
            {
                float dn = r0 + (r1 - r0) * (float)System.Math.Tanh((d - r0) / (r1 - r0));
                p = hip + a * (dn / d);
            }
            Vector3 delta = p - l.animPos;
            l.animPos = p;
            l.toeAnim += delta;
        }

        /// <summary>Ballen im Lauf: unter V2LockAn über Ruhehöhe aufgesetzt → Ort merken; solange er steht, wird das Bein so
        /// verschoben, dass der Ballen dort bleibt (nur waagrecht, die Höhe macht die Bodenanpassung). Über V2LockAus (Hysterese)
        /// oder mehr als 30 cm Versatz → lösen; der Versatz klingt in 0,08 s aus.</summary>
        private void RunLock(Leg l, Vector3 root, float dt)
        {
            // 18.10.: Höhe über dem FLACHEN Ballen im Lauf (MPFB 1,2 cm), nicht über toeRest aus dem Stand (2,9 cm, Füsse in
            // Standhaltung) – sonst griff die Verriegelung schon 1,7 cm früher als gedacht
            float lift = Vector3.Dot(l.toeAnim - root, _u) - l.ground.Value - BallenFlach;
            if (!l.runLock && lift < V2LockAn)
            {
                l.runLock = true;
                l.runBall = l.toeAnim + l.runOff * MotionMath.SmoothStep01(l.runW);   // wirksame Lage übernehmen, kein Sprung
            }
            else if (l.runLock && lift > V2LockAus) l.runLock = false;
            if (l.runLock)
            {
                Vector3 off = Vector3.ProjectOnPlane(l.runBall - l.toeAnim, _u);
                if (off.magnitude > 0.25f) l.runLock = false;   // 18.10.: 0,15 löste beim Sprint (Dehnung 1,38) mitten im Stand – rechts 16 cm Gleiten
                else { l.runOff = off; l.runW = 1f; }
            }
            if (!l.runLock) l.runW = Mathf.MoveTowards(l.runW, 0f, dt / 0.08f);
            if (l.runW > 0.0001f) l.target += l.runOff * MotionMath.SmoothStep01(l.runW);
        }

        private void RunRelease(Leg l, float dt)
        {
            l.runLock = false;
            l.runW = Mathf.MoveTowards(l.runW, 0f, dt / 0.08f);
            if (l.runW > 0.0001f) l.target += l.runOff * MotionMath.SmoothStep01(l.runW);
        }

        private void BeginPlant(Leg l, Vector3 root)
        {
            bool low = ToeLift(l, root) < 0.03f;
            l.plantHold = low ? 1f : 0f;
            // 16.10.: Gehaltene Drehung = Ruhelage (Sohle eben), nicht die Pose des Augenblicks – nach einer Landung griff
            // das Festhalten mehrfach neu (Bodenkontakt flackert), zuletzt mit der Zehen-nach-unten-Pose: Zehen 7 cm im Boden.
            l.plantRot = l.hasRestRot ? l.restRot : Quaternion.Inverse(BodyYawRot()) * l.animRot;
            // Bremsschritt nur beim echten Anhalten aus dem Lauf (Stopp-Sperre). 16.10.: Bei jeder Landung (Sprung aus dem
            // Stand) wurde sonst ein Schritt 25 cm nach vorn geplant – Lab 24 cm „Gleiten“ nach dem Sprung.
            l.pending = !low && _stopLatch;
            l.pendingT = 0f;
        }

        /// <summary>
        /// Fuss in der Luft beim Anhalten. 16.10.: Nicht mehr der Animation folgen (der Lauf-Clip laeuft weiter und setzt den
        /// Fuss mit Laufband-Tempo auf – 20–25 cm Rutschen beim Aufsetzen), sondern einen eigenen Schritt planen: vom jetzigen
        /// Punkt in einem Bogen auf einen Landeplatz neben dem Standfuss, leicht vor dem Koerper (Bremsschritt). Der Schritt
        /// laeuft ueber die Umtreten-Logik (Heben, Versetzen, Aufsetzen).
        /// </summary>
        private void UpdatePlant(Leg l, Vector3 root, float bodyYaw, float dt)
        {
            if (!l.pending) return;
            if (_stepping == l) { if (_stepT >= 1f) { l.pending = false; l.plantHold = 1f; } return; }
            l.pendingT += dt;
            // Anderer Fuss tritt gerade: in der Luft stehen bleiben (nicht dem Clip folgen – der setzt mit Laufband-Tempo auf)
            if (_stepping != null) { l.lockYaw = bodyYaw; return; }
            Leg other = l == _left ? _right : _left;
            // Landeplatz: seitlich wie bisher zum anderen Fuss versetzt (Beine nicht kreuzen), 0,25 m vor der Hueftmitte in
            // Laufrichtung; Hoehe = Boden unter dem Landeplatz + Ruhehoehe des Knoechels (16.10.: vorher Hoehe des anderen
            // Fusses – war der in der Luft, „landete“ der Fuss 10 cm ueber dem Boden)
            Vector3 side = Vector3.ProjectOnPlane(l.animPos - other.animPos, _u);
            float sideLen = Mathf.Clamp(Vector3.Dot(side, _r), -0.35f, 0.35f);
            if (Mathf.Abs(sideLen) < 0.08f) sideLen = (l == _left ? -1f : 1f) * 0.12f;
            Vector3 to = root + _f * 0.25f + _r * sideLen;
            float restH = l.restHeight > 0f ? l.restHeight : 0.085f;
            to += _u * (Probe(to + _u * 0.3f, out float gy, out Vector3 _) ? (gy - Vector3.Dot(to, _u)) + restH : restH);
            l.plantTo = to; l.hasPlantTo = true;
            // 16.10. (3): vom gehaltenen Punkt aus (lockPos = wo der Fuss gerade gezeigt wird), nicht vom animierten Ziel – der
            // Clip war inzwischen weiter, der wartende Fuss sprang 15 cm in einem Bild zu Boden (Lab Anhalten: 25 m/s)
            _stepping = l; l.stepFrom = l.lockPos; l.stepYawFrom = l.lockYaw; _stepT = 0f;
        }

        private static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        private void CapLift(Leg l, float cap, float k)
        {
            if (Mathf.Abs(l.ground.Value) > 0.04f || Vector3.Angle(l.normal, _u) > 12f) return;
            float knee = cap * 0.55f;
            if (l.lift <= knee) return;
            float room = cap - knee;
            float soft = knee + room * (float)System.Math.Tanh((l.lift - knee) / room);
            float drop = (l.lift - soft) * k;
            // Knöchel hoch heisst nicht Fuss in der Luft: beim Abrollen steht der Ballen noch am Boden. Höchstens so weit
            // absenken, dass die Zehe 1 cm über ihrer Ruhehöhe bleibt (Motion-Lab 15.10.: Gehen „im Boden“ 10,4 cm).
            float toeClear = Vector3.Dot(l.toeAnim - RootFoot, _u) - l.toeRest - 0.01f;
            drop = Mathf.Min(drop, Mathf.Max(0f, toeClear));
            if (drop > 0f) l.target -= _u * drop;
        }

        private void Ground(Leg l, Vector3 root, bool grounded, float dt)
        {
            float target = 0f;
            l.normal = _u;
            if (grounded && Probe(l.animPos, out float gy, out Vector3 n))
            {
                // Zehen mitpruefen: steht die Fussspitze auf einer hoeheren Stufe/Kante, kommt der ganze Fuss hinauf
                // (sonst stecken die Zehen in der Stufe)
                float ty = 0f; Vector3 tn = _u;
                l.toeGroundOk = l.toe != null && Probe(l.toe.position, out ty, out tn);
                if (l.toeGroundOk)
                {
                    l.toeGroundY = ty;
                    if (ty > gy + 0.01f) { gy = ty; n = tn; }
                }
                target = Mathf.Clamp(gy - root.y, -maxFootOffset, maxFootOffset);
                l.normal = n;
            }
            if (!grounded) l.toeGroundOk = false;
            float up = target > l.ground.Value ? 16f : 10f;
            l.ground.Update(target, dt, up, 1f);
            l.contact = 1f - MotionMath.SmoothStep01((l.lift - 0.02f) / 0.14f);
            l.target = l.animPos + _u * l.ground.Value;
        }

        private void Solve(Leg l, float side, float w)
        {
            Vector3 target = Vector3.Lerp(l.animPos, l.target, w);
            // 18.10. (Gang-Pruefung: X-Beine 10–19 cm nur mit Fuss-Schicht): Der Kniehinweis zeigte nach INNEN
            // (links side=-1 → +0,25·rechts) und das fast gestreckte Standbein lieferte keine eigene Knierichtung – die IK
            // knickte die Knie zueinander. Jetzt: Knie zeigt nach vorn und leicht nach aussen (Anatomie), die animierte
            // Knierichtung zaehlt nur, wenn das Knie wirklich gebeugt ist (Abstand zur Huefte-Fuss-Linie ueber 3 cm).
            Vector3 hint = (_f + _r * (0.08f * side)).normalized;
            Vector3 hf = l.foot.position - l.upper.position;
            Vector3 knee = Vector3.ProjectOnPlane(l.lower.position - l.upper.position, hf.sqrMagnitude > 1e-6f ? hf.normalized : _u);
            float kw = Mathf.Clamp01((knee.magnitude - 0.03f) / 0.05f);
            if (kw > 0f && Vector3.Dot(knee, _f) > 0f) hint = Vector3.Slerp(hint, knee.normalized, 0.7f * kw);
            LimbIK.TwoBone(l.upper, l.lower, l.foot, target, hint);
            // Fussstellung: animierte Drehung behalten, bei Bodenkontakt an die Flaeche anlegen
            Quaternion tilt = MotionMath.LimitedFromTo(_u, l.normal, maxFootTilt);
            // 16.10. Anhalten: Der gehaltene Standfuss behaelt auch seine DREHUNG vom Moment des Festhaltens – der Lauf-Clip
            // rollte ihn sonst weiter ab (Zehen 6 cm im Boden, Motion-Lab) und beim Aufsetzen des Bremsschritts kippte er.
            // (relativ zur Koerper-Blickrichtung gespeichert – die Blickrichtung des Fusses regelt weiter die Dreh-Sperre unten;
            //  16.10.: als Welt-Drehung gespeichert drehte sich der Fuss beim Drehen im Stand doppelt zurueck, 10–26 cm Gleiten)
            Quaternion animRot = l.plantHold > 0.001f ? Quaternion.Slerp(l.animRot, BodyYawRot() * l.plantRot, l.plantHold) : l.animRot;
            l.foot.rotation = Quaternion.Slerp(Quaternion.identity, tilt, l.contact * w) * animRot;
            // Im Stand: Fuss behaelt seine Blickrichtung, waehrend der Koerper sich darueber dreht (Zehen gleiten nicht im Kreis)
            if (_locked && _lockYawBlend > 0.001f)
            {
                float dy = Mathf.DeltaAngle(BodyYaw(), l.lockYaw) * _lockYawBlend * w;
                if (Mathf.Abs(dy) > 0.01f) l.foot.rotation = Quaternion.AngleAxis(dy, _u) * l.foot.rotation;
            }
            // 17.10. (Max, Bild Sprint von vorn): Der Schwungfuss dreht nach innen (Zehen zur Mitte). Fussdrehung um die
            // Hochachse auf 2° einwaerts … 15° auswaerts begrenzen – nur ohne Festhalten (im Stand regelt das Umtreten).
            // 17.10. 18:00: vorerst AUS – jede Variante (nur in der Luft / gehalten nach Knoechel / gehalten nach Ballen) erhoehte
            // das Fussgleiten beim Rennen (2,5 → 4–8 cm). Die Einwaertsdrehung gehoert in die Quelle (Retarget), nicht in die Laufzeit.
            if (footYawLimit && l.toe != null && l.plantHold < 0.999f && !_locked)
            {
                Vector3 fd = Vector3.ProjectOnPlane(l.toe.position - l.foot.position, _u);
                Vector3 bf = Vector3.ProjectOnPlane(_f, _u);
                if (fd.sqrMagnitude > 1e-6f && bf.sqrMagnitude > 1e-6f)
                {
                    float yaw = Vector3.SignedAngle(bf, fd, _u);      // + = nach rechts gedreht
                    float outward = yaw * side;                         // + = Zehen nach aussen
                    float wanted = Mathf.Clamp(outward, -2f, 15f);
                    // In der Luft nachfuehren, am Boden den zuletzt bestimmten Winkel HALTEN – ein wechselnder Winkel am Boden
                    // schob den Ballen seitlich (Lab 17.10.: Rennen 4 cm statt 2)
                    float delta = (wanted - outward) * side;
                    // „am Boden“ nach dem BALLEN, nicht nach dem Knoechel: beim Rennen hebt die Ferse mitten im Stand ab
                    // (contact fiel unter 0,5, der Winkel lief weiter → Rennen 8 cm Gleiten)
                    float ballenHoehe = Vector3.Dot(l.toeAnim - RootFoot, _u) - l.ground.Value - l.toeRest;
                    if (ballenHoehe > 0.04f) l.yawFix = Mathf.Lerp(l.yawFix, delta, 0.5f);
                    float apply = l.yawFix * w * (1f - l.plantHold);
                    if (Mathf.Abs(apply) > 0.05f) l.foot.rotation = Quaternion.AngleAxis(apply, _u) * l.foot.rotation;
                }
            }
            if (!DebugNoToeGuard) ToeGuard(l, target, hint, w);
        }

        /// <summary>
        /// 16.10. Motion-Lab Treppe/Hindernisse: Der Ballen stand bis 12 cm (grobe Stufen 26 cm) in der naechsten Stufe – der
        /// Knoechel wird vom IK gehalten, der Clip rollt den Fuss weiter ab, die Zehen tauchen in die Stufe. Hier zuletzt:
        /// Ballen nie unter die Flaeche, die unter ihm liegt. Erst den Fuss um den Knoechel anheben (bis 35°), reicht das
        /// nicht, den Knoechel um den Rest hoeher setzen (Bein neu loesen).
        /// </summary>
        private void ToeGuard(Leg l, Vector3 target, Vector3 hint, float w)
        {
            if (l.toe == null || !l.toeGroundOk || w <= 0.001f) return;
            // Boden unter dem Ballen, wo er JETZT steht (nach IK/Festhalten) – die Abtastung in Ground() gilt fuer die animierte
            // Stelle, die auf der Treppe bis 20 cm weiter vorn liegt (Lab: Treppe blieb bei 11/26 cm, Hindernisse wurden 0)
            if (!Probe(l.toe.position, out float gy, out _)) return;
            // 18.10. (Gang-Prüfung: Standbein durchgehend 26° gebeugt, Abdruck flach): toeRest wird im Stand gelernt (Ballen 2,9 cm),
            // beim Gehen steht der Ballen flach 1,0–1,1 cm hoch – 60 % davon (1,7 cm) lag ÜBER dem echten Ballen, der Schutz hob in
            // jedem Schritt den Knöchel an. Jetzt nur: Ballen nicht in die Fläche (6 mm Haut).
            float floor = gy + 0.006f;
            float depth = floor - l.toe.position.y;
            if (depth <= 0.008f) return;
            Vector3 ankle = l.foot.position;
            Vector3 toToe = l.toe.position - ankle;
            float flat = Flat(toToe);
            float need = Mathf.Atan2(depth, Mathf.Max(0.05f, flat)) * Mathf.Rad2Deg;
            float turn = Mathf.Min(need, 35f) * w;
            Vector3 axis = Vector3.Cross(_u, Vector3.ProjectOnPlane(toToe, _u)).normalized; // Seitachse des Fusses
            if (axis.sqrMagnitude > 0.5f) l.foot.rotation = Quaternion.AngleAxis(-turn, axis) * l.foot.rotation;
            float rest = floor - l.toe.position.y;
            if (rest > 0.008f)
            {
                Quaternion keep = l.foot.rotation;
                LimbIK.TwoBone(l.upper, l.lower, l.foot, target + _u * (rest * w), hint);
                l.foot.rotation = keep; // Unterschenkel dreht mit – Fussdrehung behalten
            }
        }

        private float _lockYawBlend;

        private float BodyYaw()
        {
            Vector3 f = Vector3.ProjectOnPlane(_f, _u);
            return f.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        private float Flat(Vector3 v) => Vector3.ProjectOnPlane(v, _u).magnitude;

        private bool Probe(Vector3 at, out float groundY, out Vector3 normal)
        {
            Vector3 origin = new Vector3(at.x, _root.position.y + probeUp, at.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeUp + probeDown, _probeMask, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                normal = hit.normal;
                return true;
            }
            groundY = _root.position.y;
            normal = Vector3.up;
            return false;
        }
    }
}
