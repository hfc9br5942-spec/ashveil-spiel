using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Data.Motion;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Visuals;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>
    /// Waffe und Haende (Motion-System M5/M6): laeuft nach der Koerperschicht (<see cref="CharacterMotionPoser"/>) und
    /// vor den Emotes.
    ///   Tragen      Waffe steckt ausserhalb des Kampfes dort, wo ihr Aussehen es vorgibt (Huefte links/rechts, Ruecken).
    ///   Ziehen      Choreografie statt Umschalten (§7/§8/§130): Hand greift auf einem Bogen zum Griff, schliesst die
    ///               Finger, zieht die Klinge entlang ihrer Achse heraus und schwingt sie in einem Bogen in die Kampfhaltung
    ///               (leichtes Ueberschwingen). Oberkoerper dreht mit. Die Waffe wechselt Scheide ↔ Hand genau dort, wo
    ///               Hand und Griff sich treffen – ohne Sprung. Wegstecken ist die Umkehrung.
    ///   Haltung     Je Waffenart eine eigene Kampfhaltung (<see cref="WeaponPoses"/>); im Stand und beim Gehen im Kampf.
    ///   Zwei Haende Zweihaender, Stangen, Gewehre: linke Hand am Schaft/Lauf – auch waehrend der Angriffe.
    ///   Haende      Handgelenk anatomisch (<see cref="ArmRig"/>), Finger je Zustand.
    /// </summary>
    [DefaultExecutionOrder(57)]
    [DisallowMultipleComponent]
    public sealed class WeaponHandPoser : MonoBehaviour
    {
        /// <summary>Fehlersuche (Motion-Lab „haltung“, 16.10.): Handhaltung ganz aus.</summary>
        public static bool DebugOff, DebugNoCarry, DebugNoClamp, DebugNoShape, DebugEarlyReturn;
        /// <summary>Zustand fuer das Motion-Lab (Fehlersuche).</summary>
        public string DebugState => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0} inHand={1} hold={2:0.00} guard={3:0.00} offGuard={4:0.00} zwei={5:0.00} restL={6:0.00} stil={7} eltern={8}",
            _phase, WeaponInHand, _hold, _guard, _offGuard, _twoHand, _restL, Style, _pivot != null && _pivot.parent != null ? _pivot.parent.name : "-");

        private Animator _anim;
        private Transform _model;
        private CharacterMotor _motor;
        private CharacterMotionOrchestrator _orch;
        private StancePoser _stance;
        private EmotePoser _emote;
        private CharacterLod _lod;
        private WeaponUser _weapons;
        private Health _health;
        private WeaponFx _fx;

        private readonly ArmRig _l = new ArmRig(true), _r = new ArmRig(false);
        // Waffenhand und andere Hand: normal rechts/links, beim Bogen umgekehrt (Bogenhand links, Zughand rechts)
        private bool _leftWeapon;
        private ArmRig W => _leftWeapon ? _l : _r;
        private ArmRig O => _leftWeapon ? _r : _l;
        /// <summary>Arm, der die Waffe fuehrt (Bogen: links) – fuer die Kampf-Choreografie.</summary>
        public ArmRig WeaponArm => W;
        /// <summary>Der andere Arm (Schild, Stuetzhand, Zughand beim Bogen).</summary>
        public ArmRig OtherArm => O;
        public ArmRig RightArm => _r;
        public ArmRig LeftArm => _l;
        private Transform _hips, _chest, _upperChest;
        private bool _ready;
        private Vector3 _u, _f, _rt;

        // ---- Tragen / Ziehen
        private enum Phase { Held, Sheathed, Drawing, Sheathing }
        private Phase _phase = Phase.Held;
        private float _t, _dur;
        private bool _quick;
        private Transform _pivot;
        private bool _carryInit;
        private readonly Dictionary<Transform, Pose> _grips = new Dictionary<Transform, Pose>();
        // Vermessener Griff der aktuellen Waffe (aus dem Modell) – ersetzt den festen Versatz
        private GripInfo _grip;
        private Transform _gripPivot;
        private int _gripSig;
        private GripStyle _gripStyle;
        /// <summary>Vermessener Griff (Lab/Bericht).</summary>
        public GripInfo Grip => _grip;
        private readonly Dictionary<Transform, Transform> _gripHands = new Dictionary<Transform, Transform>();
        private readonly Dictionary<int, Transform> _sheaths = new Dictionary<int, Transform>();
        private readonly HashSet<int> _fallbackSheaths = new HashSet<int>();
        private float _sheathRetry;
        private WeaponSheath _sheathKind = WeaponSheath.HipLeft;
        private Transform _sheath;
        private float _appliedSlide;
        private int _holdLayer = -2;
        private float _hold = 1f;
        // Startlagen der Choreografie (relativ zur Huefte, damit Gehen/Drehen mitgenommen wird)
        private Vector3 _startPos;
        private Quaternion _startRot;
        // Weicher Uebergang nach dem Umhaengen (Lage bleibt, dann in den exakten Griff/die Scheide)
        private float _settle = 1f;
        private Vector3 _settleFromPos;
        private Quaternion _settleFromRot;
        // Griff/Loslassen erst bei echtem Kontakt (15.10. Griff-Pass): tatsaechliche Zeitpunkte (normierte Zeit)
        private float _graspT = 0.32f, _releaseT = 0.74f;
        private const float GraspWait = 0.08f;     // laengste Wartezeit auf Kontakt nach Phase1/SheatheRelease
        private const float ContactDist = 0.012f;  // Handknochen bzw. Waffe gilt ab hier als "am Griff"/"steckt" (m)
        private const float ContactAngle = 10f;    // ... und ab dieser Restdrehung (°)
        // Reichweiten-Hilfe: Oberkoerper dreht/neigt sich, Schluesselbein schiebt vor, wenn die Hand sonst nicht hinkommt
        private float _reachAssist;
        private const float AssistYaw = 20f, AssistLean = 10f, AssistClavicle = 12f, AssistGoal = 0.96f;

        // ---- Haltung
        private float _guard, _twoHand, _offGuard, _restL;
        private Transform _focusModel;
        private Vector3 _focusLocalPos;
        private Quaternion _focusLocalRot;

        public GripStyle Style { get; private set; }
        /// <summary>Waffe, die gerade geführt wird (Drehpunkt = Griff), oder null.</summary>
        public Transform Pivot => _pivot;
        public bool WeaponInHand { get; private set; } = true;
        /// <summary>0 = steckt, 1 = in der Hand (waehrend der Bewegung dazwischen).</summary>
        public float DrawProgress => _phase == Phase.Held ? 1f : _phase == Phase.Sheathed ? 0f : _phase == Phase.Drawing ? _t : 1f - _t;
        public bool Choreographing => _phase == Phase.Drawing || _phase == Phase.Sheathing;
        /// <summary>Der Waffenarm wird von der Waffe gefuehrt (Koerperschicht laesst ihn in Ruhe).</summary>
        private bool WeaponBusy => (WeaponInHand && Style != GripStyle.None && Style != GripStyle.Unarmed) || _guard > 0.05f || Choreographing;
        private bool _otherBusy;
        /// <summary>Rechter Arm wird gefuehrt (Waffe – beim Bogen: Zughand in Haltung).</summary>
        public bool RightBusy => _leftWeapon ? _otherBusy : WeaponBusy;
        /// <summary>Linker Arm haelt Schild/Schaft/Fokus (beim Bogen: den Bogen).</summary>
        public bool LeftBusy => _leftWeapon ? WeaponBusy : _otherBusy;
        public float RightWrist => _r.Valid ? _r.WristAngle : 0f;
        public float LeftWrist => _l.Valid ? _l.WristAngle : 0f;
        /// <summary>Zweihaendig: Abstand der linken Faustmitte vom Schaft (m) – sollte unter 2 cm liegen.</summary>
        public float LeftGripError { get; private set; }
        /// <summary>Groesster Sprung der Waffe weg von ihrer gleichmaessigen Bahn (m) seit dem letzten Abruf; <paramref name="info"/> = Phase/Zeitpunkt.</summary>
        public float TakeMaxWeaponJump(out string info) { float v = _maxJump; info = _jumpInfo; _maxJump = 0f; _jumpInfo = ""; return v; }
        public float TakeMaxWeaponJump() => TakeMaxWeaponJump(out _);
        private float _maxJump;
        private string _jumpInfo = "";
        /// <summary>Lab-Mitschrift (null = aus): je Bild waehrend Ziehen/Wegstecken Zeit, Bildzeit, Waffe, Hand, Handziel,
        /// Oberkoerper-Hilfe – um Spruenge bis zur Ursache zu verfolgen (15.10. (3)).</summary>
        public System.Text.StringBuilder Trace;
        private Vector3 _traceTarget; private float _traceW;
        private Quaternion _traceTargetRot = Quaternion.identity, _traceLastHand = Quaternion.identity;
        private Quaternion _lastTargetRotBody = Quaternion.identity; private bool _hasTargetRot;
        private Quaternion _prevHandBody = Quaternion.identity; private bool _hasPrevHand;
        private Transform _lastParent;
        private float _handoffMiss;
        private Vector3 _lastWeaponLocal;
        private bool _hasLastWeapon;

        private void Awake()
        {
            _anim = GetComponent<Animator>();
            _model = transform;
            _motor = GetComponentInParent<CharacterMotor>();
            _orch = GetComponentInParent<CharacterMotionOrchestrator>();
            Transform root = _motor != null ? _motor.transform : transform.root;
            if (_orch == null && root != null) _orch = root.GetComponentInChildren<CharacterMotionOrchestrator>();
            _stance = GetComponent<StancePoser>();
            _emote = GetComponent<EmotePoser>();
            _lod = GetComponentInParent<CharacterLod>();
            _weapons = GetComponentInParent<WeaponUser>();
            _health = GetComponentInParent<Health>();
            _fx = GetComponentInParent<WeaponFx>();
        }

        private bool EnsureBones()
        {
            if (_ready) return true;
            if (_anim == null || !_anim.isHuman || _anim.avatar == null) return false;
            _hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
            _chest = _anim.GetBoneTransform(HumanBodyBones.Chest);
            _upperChest = _anim.GetBoneTransform(HumanBodyBones.UpperChest);
            if (_upperChest == null) _upperChest = _chest;
            _l.Bind(_anim);
            _r.Bind(_anim);
            _ready = _hips != null && _l.Valid && _r.Valid;
            return _ready;
        }

        private Vector3 M(Vector3 v) => _rt * v.x + _u * v.y + _f * v.z;

        // Lagen relativ zur Huefte (Modellachsen am Huefte-Ursprung)
        private Quaternion BodyRot => Quaternion.LookRotation(_f, _u);
        private Vector3 ToBody(Vector3 world) => Quaternion.Inverse(BodyRot) * (world - _hips.position);
        private Vector3 FromBody(Vector3 local) => _hips.position + BodyRot * local;
        private Quaternion ToBody(Quaternion world) => Quaternion.Inverse(BodyRot) * world;
        private Quaternion FromBody(Quaternion local) => BodyRot * local;

        // ================================================================== Ablauf

        private void LateUpdate()
        {
            if (!EnsureBones() || !_anim.isActiveAndEnabled) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (dt <= 0f) return;
            _u = _model.up; _f = _model.forward; _rt = _model.right;

            MotionContext c = _orch != null ? _orch.Context : default;
            WeaponDefinition active = _weapons != null ? _weapons.ActiveWeapon : null;
            bool melee = active == null || active.HeldLikeMelee;
            bool unarmed = _orch != null ? c.Unarmed : active == null;
            Style = WeaponPoses.StyleOf(active != null ? active.weaponClass : WeaponClass.Sword, melee, unarmed);
            _leftWeapon = Style == GripStyle.Bow;
            // Aktion im Animator (auch bei fremden Spielern, deren Waffe keinen Angriff meldet): Schlag/Schuss/Treffer
            // = Oberkoerper gehoert der Animation; Rolle/Tod = ganzer Koerper
            bool attacking = (_weapons != null && _weapons.IsInAttackMotion) || AnimatorActions.UpperBody(_anim);
            bool fullBody = AnimatorActions.FullBody(_anim);
            bool alive = _health == null || _health.IsAlive;
            bool emoting = (_emote != null && _emote.Active) || (_motor != null && _motor.IsMounted);
            bool detailed = _lod == null || _lod.Level == 0;
            bool on = CharacterMotionPoser.GlobalEnabled && !DebugOff && alive && !emoting && !fullBody;

            _l.Learn();
            _r.Learn();

            Transform pivot = _fx == null ? null : melee ? _fx.MeleePivot : _fx.RangedPivot;
            bool combat = c.Stance == MotionStance.Combat;
            WeaponStance ws = WeaponPoses.Guard(Style, c.Offhand);
            if (!DebugNoCarry) Carry(pivot, !melee, unarmed, c, attacking, dt, on && detailed);

            // 16.10. (Max: „sieht komplett verbuggt aus“; Motion-Lab „haltung“ h7/h13): Mit gesteckter Waffe ausserhalb des Kampfes
            // gibt es fuer diese Schicht nichts zu halten – trotzdem streckte sie den Arm, spreizte die Finger und zog die
            // Schultern nach vorn (Gelenkgrenzen und Fingerform auf die Stand-Animation angewandt). Jetzt: nur das Tragen
            // (Scheide) – Haende und Arme bleiben bei Animation und Standhaltung.
            bool ruhe = _phase == Phase.Sheathed && !WeaponInHand && !combat && !attacking && !Choreographing;
            if (ruhe && on && detailed && !DebugNoShape)
            {
                // Finger auch mit gesteckter Waffe: locker (Schildhand greift den Schildgriff) – sonst bleiben sie als flache
                // Ruhelage der Figur stehen (Motion-Lab: offene, steife Haende beim Laufen)
                // 16.10. (Bildbogen Joggen/Rennen: flache „Tablett“-Haende, Handflaeche nach unten): ab Joggen lockere Faust
                // wie beim echten Laufen, und die Unterarmdrehung auf Daumen-oben begrenzt (Grenze gleitet mit dem Tempo,
                // damit beim Tempowechsel nichts springt). Der Schild haengt mit gesteckter Waffe am Ruecken – die linke Hand
                // greift also nichts.
                float lauf = Mathf.InverseLerp(2.6f, 3.6f, c.Speed);
                HandShape locker = lauf > 0.5f ? HandShape.LooseFist : HandShape.Relaxed;
                W.Shape(locker, 1f, dt);
                O.Shape(locker, 1f, dt);
                if (!DebugNoClamp && lauf > 0.001f)
                {
                    float twist = Mathf.Lerp(95f, 55f, lauf);
                    W.ClampTwist(twist); O.ClampTwist(twist);
                    W.ClampWrist(50f, lauf); O.ClampWrist(50f, lauf);
                }
            }
            if (on && detailed)
            {
                // 21.10. nachts (Haut-Pruefung im Lab: die schwingende linke Hand schlug beim Gehen 1,5–1,8 cm in Knauf und Parier
                // des Hueftschwerts): wie ein echter Schwerttraeger ruht die linke Hand im Stehen und Gehen auf dem Knauf und haelt
                // die Waffe ruhig; ab Joggen laesst sie los und schwingt wieder mit (weich ein- und ausgeblendet).
                bool knaufHand = ruhe && pivot != null && pivot.parent == _sheath && _sheathKind == WeaponSheath.HipLeft &&
                                 (Style == GripStyle.OneHand || Style == GripStyle.Dagger) && c.Offhand != OffhandRole.Focus && c.Speed < 2.4f;
                _knauf = MotionMath.Damp(_knauf, knaufHand ? 1f : 0f, knaufHand || ruhe ? 0.12f : 0.05f, dt);   // beim Ziehen schnell loslassen
                if (_knauf > 0.001f && pivot != null && !DebugNoClamp)
                {
                    float kw = MotionMath.SmoothStep01(_knauf);
                    // Ziel bleibt beim Loslassen an der Scheide (die gezogene Klinge nimmt die Hand nicht mit)
                    if (knaufHand) _pommelBody = ToBody(PommelPoint(pivot));
                    // Beim Loslassen weicht das Ziel nach aussen-oben aus, damit die Hand nicht durch den Griff zurueckschwingt
                    Vector3 pos = FromBody(_pommelBody) + _u * (0.035f + 0.05f * (1f - kw)) - _rt * (0.01f + 0.09f * (1f - kw));
                    // Handflaeche schraeg nach unten-innen auf dem Knauf (Lab: flach von oben verlangte 128° Unterarmdrehung, Grenze 100°).
                    // 22.10. (Max, Screenshot: „in der Haltung ist der linke Ellenbogen falsch positioniert“ – Hühnerflügel: Finger nach
                    // vorn-unten-aussen zwangen den Ellbogen nach hinten-oben-aussen auf Brusthöhe): der Unterarm kommt vom locker neben der
                    // Hüfte hängenden Ellbogen leicht abwärts nach vorn-innen, die Hand liegt fast flach auf dem Knauf.
                    Vector3 dir = (_f * 0.9f + _rt * 0.3f - _u * 0.2f).normalized, palm = (-_u * 0.85f + _rt * 0.5f).normalized;
                    Vector3 k = WeaponPoses.KnucklesOf(dir, palm, true);
                    O.Place(pos, Quaternion.LookRotation(k, palm), 0f, 0.5f, kw, 45f);
                    O.ClampTwist(95f); O.ClampWrist(50f, 1f);
                    if (kw > 0.5f) O.Shape(HandShape.Relaxed, 1f, dt);   // LooseFist knickte den Daumen seitlich (54°)
                }

                // 22.10. nachts (Lab: beim Ziehen von der Hüfte lagen beide Hände am Griff – die linke noch auf dem Knauf, die
                // rechte griff zu): wie beim echten Schwertträger hält die linke Hand beim Ziehen und beim Einstecken die Scheide
                // unterhalb des Mundblechs fest und lässt los, sobald die Klinge frei ist bzw. steckt.
                bool scheideHalten = pivot != null && _sheathKind == WeaponSheath.HipLeft && (Style == GripStyle.OneHand || Style == GripStyle.Dagger)
                                     && c.Offhand != OffhandRole.Focus && !_fromHand
                                     && ((_phase == Phase.Drawing && _t < Phase2()) || (_phase == Phase.Sheathing && _t > SheatheSwept() - 0.2f && _t < SheatheRelease() + 0.05f));
                _scheide = MotionMath.Damp(_scheide, scheideHalten ? 1f : 0f, scheideHalten ? 0.05f : 0.07f, dt);
            }
            if (!on || !detailed || DebugEarlyReturn || ruhe) { _otherBusy = false; RestoreFocus(); ScheidenHand(dt); SettleWeapon(dt); TrackJump(); return; }

            float speed = c.Speed;
            float guardTarget = WeaponInHand && combat && !attacking ? Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(3f, 6.3f, speed)) : 0f;
            if (Choreographing) guardTarget = 0f;
            _guard = MotionMath.Damp(_guard, guardTarget, attacking ? 0.03f : 0.1f, dt);
            float drop = ws.moveDrop * Mathf.Clamp01(speed / 2f);
            bool twoHanded = ws.left == OffhandRole.OnWeapon || ws.left == OffhandRole.Support;
            Vector3 shift = twoHanded ? TwoHandShift(ws, drop) : Vector3.zero;

            // ---- rechte Hand: Choreografie oder Kampfhaltung
            if (!(Choreographing && pivot != null)) _reachAssist = 0f;
            if (Choreographing && pivot != null) Choreograph(ws, pivot, drop, shift, combat, dt);
            else if (_guard > 0.001f) PlaceHand(W, ws.right, drop, MotionMath.SmoothStep01(_guard), 0.5f, shift);
            ScheidenHand(dt);   // erst nach der Choreografie: Hüfte und Scheide sind dann schon gedreht
            SettleWeapon(dt);

            // ---- linke Hand
            _otherBusy = false;
            float twoTarget = twoHanded && _phase == Phase.Held && WeaponInHand && pivot != null ? 1f : 0f;
            _twoHand = MotionMath.Damp(_twoHand, twoTarget, 0.07f, dt);
            if (twoHanded)
            {
                // Waehrend des Ziehens/Wegsteckens haengt die linke Hand locker und greift erst am Ende zu
                float rest = combat || Choreographing ? 1f - MotionMath.SmoothStep01(_twoHand) : 0f;
                if (rest > 0.001f) PlaceHand(O, ws.leftRest, drop, rest, 0.5f);
                if (_twoHand > 0.001f && pivot != null && WeaponInHand)
                {
                    LeftOnWeapon(ws, pivot, MotionMath.SmoothStep01(_twoHand));
                    _otherBusy = true;
                }
            }
            else if (ws.left == OffhandRole.Shield || ws.left == OffhandRole.Focus || ws.left == OffhandRole.Free)
            {
                // 22.10. (Angriffsfilm Dolch/waffenlos: im Angriff hing die freie linke Hand seitlich abgespreizt – die Deckung schaltete ab und
                // die Animationspose spreizte den Arm): die freie Hand bleibt im Angriff zu drei Vierteln vor dem Körper (Deckung, Gleichgewicht).
                float offAttack = ws.left == OffhandRole.Shield ? 1f : ws.left == OffhandRole.Free ? 0.75f : 0f;
                // 23.10.: beim Wirken (Animator „Casting“, Spell_Simple-Clip auf dem linken Arm) gibt die freie Hand die Deckung frei
                bool wirkt = _anim != null && AnimatorWirkt(_anim);
                _offGuard = MotionMath.Damp(_offGuard, combat && alive && !wirkt ? Mathf.Lerp(1f, 0.4f, Mathf.InverseLerp(3f, 6.3f, speed)) * (attacking ? offAttack : 1f) : 0f, 0.12f, dt);
                // 22.10. nachts (Ziehfilm: die Deckung überschrieb den Scheidengriff – linke Hand in Brusthöhe, dann vor dem Mund):
                // solange die linke Hand die Scheide hält, wartet die Deckung.
                float gl = MotionMath.SmoothStep01(_offGuard) * (1f - MotionMath.SmoothStep01(_scheide));
                if (gl > 0.001f)
                {
                    // 22.10. nachts (Angriffsfilm: Klinge schlug beim Oberhau durch den eigenen Schild; Hurstwic: der Schild oeffnet
                    // sich nur so weit wie noetig nach links und deckt weiter den Kopf): beim Angriff 12 cm nach aussen, 6 cm hoeher.
                    bool oeffnen = attacking && ws.left == OffhandRole.Shield;
                    _schildAuf = MotionMath.Damp(_schildAuf, oeffnen ? 1f : 0f, oeffnen ? 0.05f : 0.12f, dt);
                    Vector3 auf = M(new Vector3(-0.18f, 0.08f, -0.03f)) * MotionMath.SmoothStep01(_schildAuf);   // Keulenkopf traf bei 12 cm noch den Schild
                    PlaceHand(O, ws.leftHand, drop, gl, ws.left == OffhandRole.Shield ? 1f : 0.5f, auf);
                    _otherBusy = ws.left != OffhandRole.Free;
                }
            }
            // Fernwaffen-Standhaltung spreizt den linken Arm ab – ausserhalb des Kampfes im Stand locker haengen lassen
            bool rangedStyle = Style == GripStyle.Pistol || Style == GripStyle.LongGun || Style == GripStyle.Bow;
            _restL = MotionMath.Damp(_restL, rangedStyle && !combat && !Choreographing && speed < 0.3f && c.Offhand == OffhandRole.Free ? 1f : 0f, 0.15f, dt);
            if (_restL > 0.001f) PlaceHand(O, ws.leftRest, 0f, MotionMath.SmoothStep01(_restL) * (1f - MotionMath.SmoothStep01(_offGuard)), 0.5f);
            Focus(ws.left == OffhandRole.Focus || c.Offhand == OffhandRole.Focus, MotionMath.SmoothStep01(_offGuard));

            // ---- Handgelenke und Unterarmdrehung nie ueber den natuerlichen Bereich (auch in Animationen)
            if (!DebugNoClamp)
            {
                W.ClampTwist(95f);   // 22.10.: gleiche Grenzen in Choreografie und Deckung – sonst Sprung im Übergang Ziehen → Halten
                O.ClampTwist(95f);
                W.ClampWrist(50f, 1f);
                O.ClampWrist(50f, 1f);
            }
            LeftGripError = twoHanded && pivot != null && _twoHand > 0.5f ? O.DistanceToLine(W.Center, pivot.forward) : 0f;

            // ---- Finger
            if (!DebugNoShape) W.Shape(RightShape(ws, combat, attacking), 1f, dt);
            HandShape ls;
            if (twoHanded && _twoHand > 0.3f) ls = ws.leftShape;
            else if (c.Offhand == OffhandRole.Shield) ls = HandShape.Grip;
            else if (c.Offhand == OffhandRole.Focus) ls = _offGuard > 0.3f ? HandShape.Cupped : HandShape.LooseFist;
            else if (Style == GripStyle.Unarmed && (combat || attacking)) ls = HandShape.Fist;
            else ls = combat && _offGuard > 0.3f ? ws.leftShape : HandShape.Relaxed;
            if (_scheide > 0.5f) ls = HandShape.Grip;
            if (!DebugNoShape) O.Shape(ls, 1f, dt);
            TrackJump();
        }

        /// <summary>Linke Hand hält beim Ziehen/Einstecken von der Hüfte die Scheide knapp unter dem Mundblech.</summary>
        private void ScheidenHand(float dt)
        {
            if (_scheide <= 0.001f || DebugNoClamp || _sheath == null) return;
            float sw = MotionMath.SmoothStep01(_scheide);
            Vector3 hals = _sheath.position + _sheath.forward * 0.11f - _rt * 0.035f;   // knapp unter dem Mundblech, aussen
            Vector3 dir = (_f * 0.6f - _u * 0.55f - _rt * 0.2f).normalized, palm = (_rt * 0.8f + _u * 0.2f).normalized;
            Vector3 k = WeaponPoses.KnucklesOf(dir, palm, true);
            O.Place(hals, Quaternion.LookRotation(k, palm), 0f, 0.5f, sw, 45f);
            O.ClampTwist(95f); O.ClampWrist(50f, 1f);
            if (sw > 0.5f) O.Shape(HandShape.Grip, 1f, dt);
        }

        private HandShape RightShape(in WeaponStance ws, bool combat, bool attacking)
        {
            if (Style == GripStyle.Unarmed) return combat || attacking ? HandShape.Fist : HandShape.Relaxed;
            if (_phase == Phase.Drawing)
            {
                float grasp = Phase1();
                if (_t < grasp * 0.7f) return HandShape.Open;
                // Finger schliessen erst, wenn die Waffe wirklich in der Hand ist (Griff kann bis GraspWait spaeter kommen)
                return _t < grasp || !WeaponInHand ? HandShape.LooseFist : ws.rightShape;
            }
            if (_phase == Phase.Sheathing)
            {
                // Solange die Waffe noch in der Hand ist (Einrasten abwarten), bleibt die Hand zu
                float release = WeaponInHand ? Mathf.Max(_releaseT, _t + 0.001f) : _releaseT;
                return _t < release ? ws.rightShape : _t < release + 0.12f ? HandShape.Open : HandShape.Relaxed;
            }
            return WeaponInHand ? ws.rightShape : HandShape.Relaxed;
        }

        /// <summary>Zweihaendig: Waffe so weit zur Mitte ruecken, dass die linke Hand ihren Griffpunkt sicher erreicht.</summary>
        private Vector3 TwoHandShift(in WeaponStance ws, float drop)
        {
            Vector3 rPos = _hips.position + M(ws.right.pos) - _u * drop;
            Vector3 dirW = M(ws.right.dir), palmW = M(ws.right.palm);
            Vector3 axisW = ws.left == OffhandRole.Support ? dirW : WeaponPoses.KnucklesOf(dirW, palmW, false);
            Vector3 lPos = rPos + axisW.normalized * ws.leftOnWeapon;
            Vector3 toL = lPos - O.Upper.position;
            float max = O.Reach * 0.9f;
            return toL.magnitude > max ? -toL.normalized * Mathf.Min(0.1f, toL.magnitude - max) : Vector3.zero;
        }

        // ================================================================== Haende setzen

        private void GuardHandPose(in HandPose hp, float drop, Vector3 shift, out Vector3 boneP, out Quaternion boneR)
        {
            Vector3 pos = _hips.position + M(hp.pos) - _u * drop + shift;
            Vector3 dir = M(hp.dir), palm = M(hp.palm);
            Vector3 k = WeaponPoses.KnucklesOf(dir, palm, W.Left);
            boneR = W.BoneRotation(Quaternion.LookRotation(k, palm));
            boneP = W.BonePosition(pos, boneR);
        }

        private void PlaceHand(ArmRig arm, HandPose hp, float drop, float w, float twistShare, Vector3 shift = default)
        {
            Vector3 pos = _hips.position + M(hp.pos) - _u * drop + shift;
            Vector3 dir = M(hp.dir), palm = M(hp.palm);
            Vector3 k = WeaponPoses.KnucklesOf(dir, palm, arm.Left);
            if (k.sqrMagnitude < 1e-6f || palm.sqrMagnitude < 1e-6f) return;
            arm.Place(pos, Quaternion.LookRotation(k, palm), hp.ulnar, twistShare, w);
        }

        /// <summary>Linke Hand an Schaft/Griff (Zweihaender, Stange) oder unter den Lauf (Gewehr).</summary>
        private void LeftOnWeapon(in WeaponStance ws, Transform pivot, float w)
        {
            Vector3 axis = pivot.forward;
            Vector3 rc = W.Center;
            Vector3 pos, k, palm;
            bool geo = _grip.Valid && _gripPivot == pivot;
            if (ws.left == OffhandRole.Support)
            {
                Vector3 top = pivot.up;
                palm = top;
                Vector3 dir = Vector3.ProjectOnPlane(axis * 0.45f + pivot.right * 0.9f, palm).normalized;
                k = WeaponPoses.KnucklesOf(dir, palm, true);
                // Handflaeche unter dem vermessenen Vorderschaft (statt geschaetztem Abstand)
                pos = geo && _grip.HasSupport ? pivot.TransformPoint(_grip.Support) : rc + axis * ws.leftOnWeapon - top * 0.035f;
            }
            else
            {
                k = axis;
                pos = geo && _grip.HasLeft ? pivot.TransformPoint(_grip.Left) : rc + axis * ws.leftOnWeapon;
                // Handflaeche: die Seite, mit der der linke Unterarm am wenigsten verdreht greift (Neutral = Handschlag)
                Vector3 p0 = Vector3.ProjectOnPlane(-W.Palm, axis).normalized;
                float c0 = LeftTwistCost(pos, k, p0), c1 = LeftTwistCost(pos, k, -p0);
                if (_leftFlip ? c0 + 20f < c1 : c1 + 20f < c0) _leftFlip = !_leftFlip; // Hysterese: kein Umspringen
                palm = _leftFlip ? -p0 : p0;
            }
            if (k.sqrMagnitude < 1e-6f || palm.sqrMagnitude < 1e-6f) return;
            float d = Vector3.Distance(O.Upper.position, pos);
            float fit = 1f - Mathf.InverseLerp(O.Reach * 0.97f, O.Reach * 1.12f, d);
            // 22.10. nachts (Lab: linke Hand am Zweihandgriff 8–18 cm daneben – Knöchel entlang der Waffe verlangten mehr als die
            // 45° Handgelenk, die Klemme schwenkte die Faust weg): wie die rechte Hand greift die linke mit schräg angestelltem
            // Unterarm (Ellenneigung).
            float ulnarL = ws.left == OffhandRole.OnWeapon ? 30f : 0f;
            O.Place(pos, Quaternion.LookRotation(k, palm), ulnarL, 0.5f, w * fit, 55f);
            // Die Handgelenk-Grenze drehte die Faust danach bis 18 cm vom Griff weg (Lab, Knochen selbst exakt): Arm um den Rest
            // nachführen, die Fausthaltung bleibt in den Grenzen – so fasst ein Mensch nach.
            for (int i = 0; i < 2; i++)
            {
                O.ClampTwist(95f); O.ClampWrist(50f, 1f);
                Vector3 e = pos - O.Center;
                if (e.sqrMagnitude < 0.003f * 0.003f) break;
                Quaternion keep = O.Hand.rotation;
                Vector3 ziel = O.Hand.position + e * (w * fit);
                O.ReachTo(ziel, O.Lower.position - (O.Upper.position + ziel) * 0.5f, 1f);
                O.Hand.rotation = keep;
            }
        }

        private bool _leftFlip;

        /// <summary>Wie weit die Kampfhaltung gerade greift (0..1) – die Choreografie blendet dagegen aus.</summary>
        public float GuardWeight => MotionMath.SmoothStep01(_guard);

        /// <summary>
        /// Nach einer Choreografie (Waffenhand bewegt): zweite Hand erneut an Griff/Schaft legen – schwere Waffen bleiben
        /// in beiden Haenden, egal wohin der Schlag fuehrt.
        /// </summary>
        public void RefitOffhand()
        {
            if (!_ready || _pivot == null || !WeaponInHand || _twoHand <= 0.001f) return;
            MotionContext c = _orch != null ? _orch.Context : default;
            WeaponStance ws = WeaponPoses.Guard(Style, c.Offhand);
            if (ws.left != OffhandRole.OnWeapon && ws.left != OffhandRole.Support) return;
            LeftOnWeapon(ws, _pivot, MotionMath.SmoothStep01(_twoHand));
            O.ClampTwist(95f);
            O.ClampWrist(50f, 1f);
        }

        /// <summary>Wie weit (°) der linke Unterarm fuer diese Handlage aus der Neutralstellung gedreht waere.</summary>
        private float LeftTwistCost(Vector3 pos, Vector3 k, Vector3 palm)
        {
            Vector3 dir = Vector3.Cross(palm, k);                // Handrichtung der linken Hand
            Vector3 s = O.Upper.position;
            Vector3 wrist = pos - dir * 0.08f;
            Vector3 elbow = wrist - dir * O.LowerLength;
            Vector3 n = -Vector3.Cross(elbow - s, wrist - elbow); // links: Innenseite = -Normale
            float reachErr = Mathf.Abs(Vector3.Distance(s, elbow) - O.UpperLength) * 200f;
            if (n.sqrMagnitude < 1e-6f) return reachErr;
            return Mathf.Abs(Vector3.SignedAngle(Vector3.ProjectOnPlane(n, dir), Vector3.ProjectOnPlane(palm, dir), dir)) + reachErr;
        }

        /// <summary>Fokus schwebt im Kampf ueber der hohlen Hand, sonst liegt er wie vom Aussehen gesetzt.</summary>
        private void Focus(bool hasFocus, float g)
        {
            Transform m = hasFocus && _orch != null && _orch.Visuals != null ? _orch.Visuals.OffhandModel : null;
            if (m != _focusModel)
            {
                RestoreFocus();
                _focusModel = m;
                if (m != null) { _focusLocalPos = m.localPosition; _focusLocalRot = m.localRotation; }
            }
            if (_focusModel == null) return;
            _focusModel.localPosition = _focusLocalPos;
            _focusModel.localRotation = _focusLocalRot;
            if (g <= 0.001f) return;
            Vector3 palm = O.Palm, fwd = Vector3.ProjectOnPlane(_f, palm).normalized;
            Quaternion rot = Quaternion.LookRotation(fwd, palm);
            float s = _focusModel.lossyScale.x;
            Vector3 orb = O.Center + palm * 0.1f;
            Vector3 origin = orb - rot * (new Vector3(0f, 0.09f, 0.05f) * s);
            _focusModel.SetPositionAndRotation(Vector3.Lerp(_focusModel.position, origin, g), Quaternion.Slerp(_focusModel.rotation, rot, g));
        }

        private void RestoreFocus()
        {
            if (_focusModel == null) return;
            _focusModel.localPosition = _focusLocalPos;
            _focusModel.localRotation = _focusLocalRot;
        }

        // ================================================================== Tragen: Zustand

        private void Carry(Transform pivot, bool ranged, bool unarmed, in MotionContext c, bool attacking, float dt, bool animate)
        {
            if (unarmed || pivot == null || !pivot.gameObject.activeInHierarchy)
            {
                WeaponInHand = unarmed;
                if (_phase != Phase.Held && _phase != Phase.Sheathed) _phase = Phase.Held;
                SetHold(1f, dt);
                SetStanceFlags(c, unarmed);
                return;
            }
            Transform hand = W.Hand;
            if (pivot != _pivot) { _pivot = pivot; _carryInit = false; _hasLastWeapon = false; }
            UpdateGrip(pivot);
            // Griffhaltung je Waffe und Hand einmal merken (so, wie die Ausruestung sie gesetzt hat)
            if (pivot.parent == hand && (!_gripHands.TryGetValue(pivot, out Transform gh) || gh != hand))
            {
                _grips[pivot] = new Pose(pivot.localPosition, pivot.localRotation);
                _gripHands[pivot] = hand;
                _appliedSlide = 0f;
            }

            WeaponSheath kind = _orch != null && _orch.Visuals != null ? _orch.Visuals.DrawnSheath
                : ranged ? WeaponSheath.HipRight : WeaponSheath.HipLeft;
            if (kind == WeaponSheath.Arm || kind == WeaponSheath.None) kind = WeaponSheath.HipLeft;
            if (Style == GripStyle.Bow) kind = WeaponSheath.BackBow;
            _sheathKind = kind;
            _ranged = ranged;
            _sheath = SheathAnchor(kind, ranged, Style == GripStyle.Staff, Style == GripStyle.Bow);

            bool wantDrawn = c.WeaponDrawn || attacking || _orch == null;
            // 21.10. abends (Ziehfilm Schwert+Schild: Schild kam zuerst an den Arm, dann zog die rechte Hand über ihn hinweg bis an den
            // Mund und die Klinge fuhr quer durch den Schild): Reihenfolge wie beim echten Rundschild am Riemen – erst das Schwert
            // ziehen (vorn ist nichts im Weg), sobald die Klinge frei ist den Schild über die linke Schulter an den Arm; beim
            // Wegstecken geht der Schild zuerst auf den Rücken.
            bool shieldBack = _phase == Phase.Sheathed || _phase == Phase.Sheathing || (_phase == Phase.Drawing && !_fromHand && _t < Phase2());
            CarryShield(shieldBack);
            if (!_carryInit || (pivot.parent != hand && pivot.parent != _sheath))
            {
                _carryInit = true;
                if (wantDrawn) { AttachHandExact(pivot, hand); _phase = Phase.Held; }
                else { AttachSheathExact(pivot); _phase = Phase.Sheathed; }
                _settle = 1f;
            }

            // Jemand anderes hat die Waffe umgehaengt (Koerperwechsel, Ausruestung): Zustand wieder herstellen
            if (_phase == Phase.Sheathed && pivot.parent != _sheath) AttachSheathExact(pivot);
            else if (_phase == Phase.Held && pivot.parent != hand) AttachHandExact(pivot, hand);

            if (!animate)
            {
                // Ohne Choreografie (Figur weit weg, Emote, Lab-Vergleich): sofort richtig
                if (wantDrawn && pivot.parent != hand) AttachHandExact(pivot, hand);
                if (!wantDrawn && pivot.parent != _sheath) AttachSheathExact(pivot);
                _phase = wantDrawn ? Phase.Held : Phase.Sheathed;
            }
            else
            {
                switch (_phase)
                {
                    case Phase.Sheathed:
                        if (wantDrawn) StartDraw(attacking);
                        break;
                    case Phase.Held:
                        if (!wantDrawn) StartSheathe();
                        break;
                    case Phase.Drawing:
                        // Angriff waehrend des Ziehens: Rest beschleunigen (Ziehen wird zum Ausholen)
                        if (attacking && _dur * (1f - _t) > 0.25f) _dur = 0.25f / Mathf.Max(0.05f, 1f - _t);
                        _t += dt / _dur;
                        if (!wantDrawn)
                        {
                            // Noch nicht gegriffen: bis zum Griff weiter, dann loslassen; schon gezogen: zurueckstecken
                            if (pivot.parent != hand) _abortDraw = true;
                            else { StartSheathe(); break; }
                        }
                        if (_abortDraw && _t >= Phase1()) { if (_guided && pivot.parent == _sheath) AttachSheathAt(pivot); _guided = false; _abortDraw = false; _phase = Phase.Sheathing; _t = SheatheRelease(); _releaseT = _t; _dur = SheatheTime(); break; }
                        if (_t >= 1f) { _t = 1f; _phase = Phase.Held; _guard = 1f; _fromHand = false; }
                        break;
                    case Phase.Sheathing:
                        _t += dt / _dur;
                        if (wantDrawn)
                        {
                            // Waffe noch in der Hand: von hier aus zurueck in die Haltung schwingen; sonst neu ziehen
                            if (pivot.parent == hand) StartReturn(attacking);
                            else StartDraw(attacking);
                            break;
                        }
                        if (_t >= 1f) { _t = 1f; _phase = Phase.Sheathed; }
                        break;
                }
            }

            // Rutschen entlang der Waffe (Stab weiter oben greifen) – nur in Ruhe anpassen
            if (_phase == Phase.Held && pivot.parent == hand && _settle >= 1f)
            {
                float slide = WeaponPoses.Guard(Style, OffhandRole.Free).slide;
                if (Mathf.Abs(slide - _appliedSlide) > 0.001f) BeginSettle(pivot, () => AttachHandExact(pivot, hand));
            }

            WeaponInHand = pivot.parent == hand;
            // Halte-Animation nur in Ruhe mit Waffe in der Hand (waehrend der Bewegung fuehren wir den Arm)
            SetHold(_phase == Phase.Held && WeaponInHand ? 1f : 0f, dt);
            SetStanceFlags(c, WeaponInHand && _phase == Phase.Held);
        }

        private float DrawTime(bool quick)
        {
            WeaponMotionProfile wp = _orch != null ? _orch.Tuning.weapon : null;
            float scale = wp != null ? Mathf.Clamp(wp.drawTime / 0.55f, 0.6f, 1.8f) : 1f;
            if (quick) return 0.3f;
            switch (_sheathKind)
            {
                case WeaponSheath.Back: case WeaponSheath.BackBow: return 1.0f * scale;
                case WeaponSheath.HipRight: return 0.55f * scale;
                default: return 0.8f * scale;
            }
        }

        private float SheatheTime()
        {
            WeaponMotionProfile wp = _orch != null ? _orch.Tuning.weapon : null;
            float scale = wp != null ? Mathf.Clamp(wp.sheatheTime / 0.8f, 0.6f, 1.8f) : 1f;
            switch (_sheathKind)
            {
                case WeaponSheath.Back: case WeaponSheath.BackBow: return 1.05f * scale;
                case WeaponSheath.HipRight: return 0.6f * scale;
                default: return 0.85f * scale;
            }
        }

        private bool _abortDraw, _fromHand;

        /// <summary>Waffe ist schon in der Hand (Wegstecken abgebrochen): nur noch der Schwung in die Haltung.</summary>
        private void StartReturn(bool quick)
        {
            _phase = Phase.Drawing;
            _quick = quick;
            _fromHand = true;
            _abortDraw = false;
            _t = Phase2();
            _dur = DrawTime(quick);
            if (_pivot != null) { _startPos = _shown ? _shownWeaponPos : ToBody(_pivot.position); _startRot = _shown ? _shownWeaponRot : ToBody(_pivot.rotation); }
        }

        /// <summary>Gewicht der Stuetzhand an der Waffe (0..1) – fuer die Haut-Pruefung im Lab.</summary>
        public float TwoHandWeight => _twoHand;
        private float _knauf;
        private float _schildAuf;
        private float _scheide;
        private Vector3 _pommelBody;
        private Transform _pommelOf;
        private float _pommelZ;

        /// <summary>Hinteres Ende des Griffs (Knauf) in Weltkoordinaten – aus den Modellgrenzen entlang der Klingenachse (+Z).</summary>
        private Vector3 PommelPoint(Transform pivot)
        {
            if (_pommelOf != pivot)
            {
                _pommelOf = pivot;
                float min = 0f;
                foreach (MeshFilter mf in pivot.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    Bounds b = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        min = Mathf.Min(min, pivot.InverseTransformPoint(mf.transform.TransformPoint(c)).z);
                    }
                }
                _pommelZ = min;
            }
            return pivot.TransformPoint(new Vector3(0f, 0f, _pommelZ));
        }

        /// <summary>Klinge gleitet in/aus der Scheide und haengt dabei an der Scheide (nicht an der Hand).</summary>
        private bool _guided;
        /// <summary>Klinge in einer Scheide (geführt ziehen/einstecken) – Bogen und Stab werden dagegen abgehoben/eingehängt.</summary>
        private bool Fuehrbar => _sheathKind != WeaponSheath.BackBow && Style != GripStyle.Bow;   // Stab geführt: Lab 4–6 cm statt 40–180 cm
        private Vector3 _seatFromPos;
        private Quaternion _seatFromRot = Quaternion.identity;

        private void StartDraw(bool quick)
        {
            _guided = false;
            _lastTargetRotBody = _shown ? _shownHandRot : ToBody(W.Hand.rotation); _hasTargetRot = true;
            _prevHandBody = _lastTargetRotBody; _hasPrevHand = true;
            _phase = Phase.Drawing;
            _fromHand = false;
            _abortDraw = false;
            _quick = quick;
            _t = 0f;
            _graspT = Phase1();
            _dur = DrawTime(quick);
            _startPos = _shown ? _shownHandPos : ToBody(W.Hand.position);
            _startRot = _shown ? _shownHandRot : ToBody(W.Hand.rotation);
        }

        private void StartSheathe()
        {
            _lastTargetRotBody = _shown ? _shownHandRot : ToBody(W.Hand.rotation); _hasTargetRot = true;
            _prevHandBody = _lastTargetRotBody; _hasPrevHand = true;
            _phase = Phase.Sheathing;
            _stabHandGesetzt = false;   // 23.10.: Schaftdrehung der Stab-Hand neu ab der gezeigten Lage
            _guided = false;
            _quick = false;
            _t = 0f;
            _releaseT = SheatheRelease();
            _dur = SheatheTime();
            if (_pivot != null) { _startPos = _shown ? _shownWeaponPos : ToBody(_pivot.position); _startRot = _shown ? _shownWeaponRot : ToBody(_pivot.rotation); }
        }

        // Abschnitte (normierte Zeit)
        private float Phase1() => 0.32f;                       // Griff erreicht
        private float Phase2() => 0.58f;                       // aus der Scheide gezogen
        private float SheatheSwept() => 0.42f;                 // Klinge an der Scheide
        private float SheatheRelease() => 0.74f;               // steckt – loslassen

        // ================================================================== Choreografie

        private bool _ranged;

        private float PullLength()
        {
            if (_sheathKind == WeaponSheath.BackBow) return 0.14f;
            if (_ranged) return _sheathKind == WeaponSheath.Back ? 0.1f : 0.14f;
            switch (_sheathKind)
            {
                case WeaponSheath.Back: return Style == GripStyle.Staff ? 0.2f : 0.38f;
                case WeaponSheath.HipRight: return 0.16f;
                default: return 0.32f;
            }
        }

        /// <summary>Griffhaltung als Versatz im Handknochen (Meter, Handdrehung) + Drehung der Waffe relativ zur Hand.</summary>
        private bool GripOffset(Transform pivot, out Vector3 off, out Quaternion rotLocal)
        {
            off = Vector3.zero; rotLocal = Quaternion.identity;
            if (_grip.Valid && _gripPivot == pivot)
            {
                GripPose(out Vector3 gpos, out Quaternion grot);
                rotLocal = grot; off = gpos;
                return true;
            }
            if (!_grips.TryGetValue(pivot, out Pose gp)) return false;
            float s = W.Hand.lossyScale.x;
            float slide = WeaponPoses.Guard(Style, OffhandRole.Free).slide;
            rotLocal = gp.rotation;
            off = gp.position * s + gp.rotation * new Vector3(0f, 0f, -slide);
            return true;
        }

        private void HandForWeapon(Vector3 wPos, Quaternion wRot, Vector3 off, Quaternion rotLocal, out Vector3 hPos, out Quaternion hRot)
        {
            hRot = wRot * Quaternion.Inverse(rotLocal);
            hPos = wPos - hRot * off;
        }

        private Transform _sayaAnker;
        private Quaternion _sayaBasis;

        /// <summary>Scheide an der linken Hüfte beim Ziehen/Einstecken nach hinten kippen (bis 32°), sonst in Grundlage.</summary>
        private void Scheidenzug()
        {
            if (_sheath == null || (_sheathKind != WeaponSheath.HipLeft && _sheathKind != WeaponSheath.HipLeftOff)) return;
            if (_sayaAnker != _sheath) { _sayaAnker = _sheath; _sayaBasis = _sheath.localRotation; }
            _sheath.localRotation = _sayaBasis;
            float s = 0f;
            if (_phase == Phase.Drawing && !_fromHand)
                s = _t < Phase2() ? MotionMath.SmoothStep01(Mathf.Clamp01(_t / Phase1())) : 1f - MotionMath.SmoothStep01(Mathf.Clamp01((_t - Phase2()) / 0.15f));
            else if (_phase == Phase.Sheathing)
                s = _t < SheatheRelease() ? MotionMath.SmoothStep01(Mathf.Clamp01((_t - (SheatheSwept() - 0.25f)) / 0.25f))
                                          : 1f - MotionMath.SmoothStep01(Mathf.Clamp01((_t - SheatheRelease()) / (1f - SheatheRelease())));
            if (s > 0.001f) _sheath.rotation = Quaternion.AngleAxis(32f * s, _rt) * _sheath.rotation;
        }

        private void Choreograph(in WeaponStance ws, Transform pivot, float drop, Vector3 shift, bool combat, float dt)
        {
            if (_sheath == null || !GripOffset(pivot, out Vector3 off, out Quaternion rotLocal)) { FinishInstant(pivot); return; }
            float side = _sheathKind == WeaponSheath.BackBow ? -0.8f : _sheathKind == WeaponSheath.HipLeft ? -1f : _sheathKind == WeaponSheath.HipRight ? 0.5f : 0.8f;
            // 22.10. nachts: Drehung 13°/11° → 22°/18° – davon nimmt jetzt die Hüfte die Hälfte (Körpereinsatz statt nur Arm)
            float twistAmp = _fromHand ? 0f : _phase == Phase.Drawing ? (_quick ? 10f : 22f) : 18f;
            float twist = Mathf.Sin(Mathf.Clamp01(_t) * Mathf.PI) * twistAmp * side;
            // 22.10. nachts (Max: Körpereinsatz „gilt für ALLES im Spiel“): die Hüfte dreht mit (die Hüftscheide kommt dabei nach
            // vorn zur Hand) und das Gewicht geht leicht in die Knie; die Füsse bleiben stehen (Zweigelenk-IK).
            if (!_fromHand && _hips != null && Mathf.Abs(twist) > 0.01f)
            {
                Transform lu = _anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg), ll = _anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg), lf = _anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform ru = _anim.GetBoneTransform(HumanBodyBones.RightUpperLeg), rl = _anim.GetBoneTransform(HumanBodyBones.RightLowerLeg), rf = _anim.GetBoneTransform(HumanBodyBones.RightFoot);
                if (lf != null && rf != null && lu != null && ru != null && ll != null && rl != null)
                {
                    float hipTwist = twist * 0.5f;
                    Vector3 fl = lf.position, fr = rf.position; Quaternion flr = lf.rotation, frr = rf.rotation;
                    _hips.position -= _u * (0.025f * Mathf.Sin(Mathf.Clamp01(_t) * Mathf.PI));
                    _hips.rotation = Quaternion.AngleAxis(hipTwist, _u) * _hips.rotation;
                    LimbIK.TwoBone(lu, ll, lf, fl, _f - _rt * 0.3f, 1f);
                    LimbIK.TwoBone(ru, rl, rf, fr, _f + _rt * 0.3f, 1f);
                    lf.rotation = flr; rf.rotation = frr;
                    twist -= hipTwist;
                }
            }
            // Scheide am Oberkoerper (Ruecken) dreht mit: erst drehen, dann Ziele berechnen. Hueftscheide: die Ziele haengen
            // nicht am Oberkoerper – erst Ziele, dann Oberkoerper samt Reichweiten-Hilfe (die das Ziel kennen muss).
            Transform torso = _chest != null ? _chest : _upperChest;
            bool torsoFirst = torso == null || _sheath.IsChildOf(torso);
            if (torsoFirst && Mathf.Abs(twist) > 0.01f) Twist(twist);
            // 22.10. nachts (Ziehfilm: die rechte Hand zog die Klinge eng am Körper bis vor das Kinn hoch): Saya-biki – die linke Hand
            // zieht die Scheide beim Ziehen und Einstecken nach hinten, die Mündung zeigt dadurch nach vorn und die Klinge kommt nach
            // vorn heraus bzw. hinein. Danach schwingt die Scheide in ihre Lage zurück.
            Scheidenzug();
            Vector3 sP = _sheath.position;
            Quaternion sR = _sheath.rotation;
            Vector3 blade = sR * Vector3.forward;
            // Klingen verlassen die Scheide zum Griff hin; ein Stab wird nach oben aus der Halterung gehoben
            float pullSign = Style == GripStyle.Staff ? 1f : -1f;
            // Bogen: vom Ruecken weg nach hinten-oben abheben statt entlang seiner Achse
            // 22.10. nachts (Ziehfilm Stab: 38 cm entlang der Achse über den Kopf – Arm gestreckt, Hand 10–13 cm neben dem Griff,
            // beim Übernehmen sprang der Stab 21–40 cm): der Stab in der Rückenschlaufe wird nur angehoben und vom Rücken weggenommen.
            Vector3 pullDir = Style == GripStyle.Bow ? (-_f * 0.6f + _u * 0.8f).normalized : Style == GripStyle.Staff ? (blade * 0.8f - _f * 0.6f).normalized : blade * pullSign;
            Vector3 pulledP = sP + pullDir * PullLength();
            Quaternion pulledR = sR;
            HandForWeapon(sP, sR, off, rotLocal, out Vector3 graspP, out Quaternion graspR);
            GuardHandPose(ws.right, drop, shift, out Vector3 guardHP, out Quaternion guardHR);
            Vector3 guardWP = guardHP + guardHR * off;
            Quaternion guardWR = guardHR * rotLocal;

            Vector3 hP; Quaternion hR;
            float weight = 1f;
            bool attachNow = false, releaseNow = false;
            bool grasping = false, seating = false;

            if (_phase == Phase.Drawing)
            {
                float a = Phase1(), b = Phase2();
                bool inHand = pivot.parent == W.Hand;
                // Hand greift bis Phase1 zum Griff; danach haelt sie dort, bis sie ihn wirklich erreicht (hoechstens GraspWait)
                if (!_fromHand && (_t < a || (!inHand && !_guided && _t < a + GraspWait)))
                {
                    // Hand greift auf einem Bogen zum Griff (vor dem Koerper vorbei, nicht durch ihn hindurch)
                    float u = EaseInOut(_t / a);
                    Vector3 p0 = FromBody(_startPos);
                    Vector3 ctrl = (p0 + graspP) * 0.5f + _f * 0.16f + _u * (_sheathKind == WeaponSheath.Back || _sheathKind == WeaponSheath.BackBow ? 0.22f : 0.04f);
                    hP = Bezier(p0, ctrl, graspP, u);
                    hR = Quaternion.Slerp(FromBody(_startRot), graspR, u);
                    grasping = _t >= a && !inHand && !_guided;
                }
                else
                {
                    // Kontakt nicht erreicht, Wartezeit vorbei (oder Bild uebersprungen): jetzt fuehren
                    if (!inHand && !_guided && !_fromHand)
                    {
                        if (Fuehrbar) _guided = true; else attachNow = true;   // Bogen/Stab werden abgehoben, nicht aus einer Scheide gezogen
                        _graspT = Mathf.Min(_t, b - 0.02f);
                    }
                    Vector3 wP; Quaternion wR;
                    if (_t < b && !_fromHand)
                    {
                        // Herausziehen ab dem tatsaechlichen Griff: Start ohne Ruck, Ende MIT Geschwindigkeit (v'(1) = 1),
                        // die der Schwung in die Haltung uebernimmt – kein Halt an Phase2.
                        float x = Mathf.Clamp01((_t - _graspT) / Mathf.Max(0.01f, b - _graspT));
                        float v = x * x * (2f - x);
                        wP = Vector3.Lerp(sP, pulledP, v);
                        wR = sR;
                        // 21.10. nachts: die Scheide fuehrt die Klinge (Waffe haengt noch an der Scheide), die Hand folgt dem Griff so gut
                        // das Handgelenk es erlaubt – vorher hing die Waffe schon in der Hand und kam mit der Handgelenk-Klemme (55°)
                        // schief aus der Scheide. Erst die freie Klinge wechselt in die Hand (siehe Schwung).
                        if (_guided) pivot.SetPositionAndRotation(wP, wR);
                    }
                    else if (_fromHand)
                    {
                        // Abgebrochenes Wegstecken: aus der gezeigten Lage zurueck in die Haltung (unveraendert)
                        float w = (_t - b) / (1f - b);
                        float e = SweepCurve(w, _quick ? 0.04f : 0.1f);
                        Vector3 fromP = FromBody(_startPos);
                        Vector3 ctrl = (fromP + guardWP) * 0.5f + _u * 0.06f + _f * 0.12f;
                        wP = Bezier(fromP, ctrl, guardWP, Mathf.Clamp01(e));
                        wP += (guardWP - fromP) * Mathf.Max(0f, e - 1f) * 0.35f;
                        wR = Quaternion.SlerpUnclamped(FromBody(_startRot), guardWR, e);
                    }
                    else
                    {
                        // Klinge frei: jetzt in die Hand. Die Hand steht wegen der Handgelenk-Grenze evtl. etwas neben dem Griff – der
                        // Ausgleich in den exakten Griff laeuft darum laenger (0,32 s statt 0,18 s), sonst zuckt die Klinge.
                        if (_guided) { _guided = false; attachNow = true; _longSettle = true; }
                        // Aus der Scheide in einem Bogen nach oben-vorn in die Haltung, am Ende leicht ueberschwingen.
                        // Die Bahn verlaesst die Scheide tangential entlang der Zugrichtung mit der Endgeschwindigkeit des
                        // Herausziehens (kubische Kurve, Startsteigung 1): stetige Position UND Geschwindigkeit an Phase2.
                        float w = (_t - b) / (1f - b);
                        float over = _quick ? 0.04f : 0.1f;
                        float eR = SweepCurve(w, over);
                        float iw = 1f - w;
                        float eP = eR + w * iw * iw * iw;
                        float tp = Mathf.Max(0.05f, b - _graspT);
                        float c1 = Mathf.Min(0.25f, PullLength() * (1f - b) / (3f * tp));
                        Vector3 ctrl = (pulledP + guardWP) * 0.5f + _u * (_sheathKind == WeaponSheath.Back || _sheathKind == WeaponSheath.BackBow ? 0.3f : 0.2f) + _f * 0.12f;
                        wP = Bezier3(pulledP, pulledP + pullDir * c1, ctrl, guardWP, Mathf.Clamp01(eP));
                        wP += (guardWP - pulledP) * Mathf.Max(0f, eP - 1f) * 0.35f;
                        wR = Quaternion.SlerpUnclamped(pulledR, guardWR, eR); // Drehung startet weiter ohne Ruck
                        if (_sheathKind == WeaponSheath.HipLeft || _sheathKind == WeaponSheath.HipLeftOff)
                        {
                            // 21.10. nachts (Ziehfilm Bild 8–10: Klinge schwenkte waagerecht in Schulterhoehe vor dem Gesicht durch):
                            // ueber Kreuz gezogen richtet sich die Spitze erst nach vorn-oben auf den Gegner (Ochs/Langort-Linie)
                            // und steigt dann in die Deckung – die Klinge kreuzt nie quer vor dem Kopf.
                            Vector3 tip0 = pulledR * Vector3.forward;
                            Vector3 midDir = (_f * 0.75f + _u * 0.6f + (_sheathKind == WeaponSheath.HipLeft ? -_rt : _rt) * 0.1f).normalized;
                            Quaternion midR = Quaternion.FromToRotation(tip0, midDir) * pulledR;
                            wR = eR < 0.45f ? Quaternion.Slerp(pulledR, midR, eR / 0.45f)
                                            : Quaternion.SlerpUnclamped(midR, guardWR, (eR - 0.45f) / 0.55f);
                        }
                    }
                    HandForWeapon(wP, wR, off, rotLocal, out hP, out hR);
                }
            }
            else
            {
                float a = SheatheSwept(), b = SheatheRelease();
                bool inHand = pivot.parent == W.Hand;
                // Bis die Klinge wirklich steckt (hoechstens GraspWait nach SheatheRelease) fuehrt die Hand sie
                if (_t < b || (inHand && _t < b + GraspWait))
                {
                    Vector3 wP; Quaternion wR;
                    if (_t < a)
                    {
                        float w = EaseInOut(_t / a);
                        Vector3 w0 = FromBody(_startPos);
                        Vector3 ctrl = (w0 + pulledP) * 0.5f + _u * (_sheathKind == WeaponSheath.Back || _sheathKind == WeaponSheath.BackBow ? 0.3f : 0.18f) + _f * 0.1f;
                        wP = Bezier(w0, ctrl, pulledP, w);
                        wR = Style == GripStyle.Staff ? SchaftSlerp(FromBody(_startRot), pulledR, w) : Quaternion.Slerp(FromBody(_startRot), pulledR, w);
                        if (_sheathKind == WeaponSheath.HipLeft || _sheathKind == WeaponSheath.HipLeftOff)
                        {
                            // wie beim Ziehen: Spitze erst nach vorn-oben, dann zur Muendung – kein Querschwenk vor dem Kopf
                            Vector3 tip0 = pulledR * Vector3.forward;
                            Vector3 midDir = (_f * 0.75f + _u * 0.6f + (_sheathKind == WeaponSheath.HipLeft ? -_rt : _rt) * 0.1f).normalized;
                            Quaternion midR = Quaternion.FromToRotation(tip0, midDir) * pulledR;
                            wR = w < 0.55f ? Quaternion.Slerp(FromBody(_startRot), midR, w / 0.55f)
                                           : Quaternion.Slerp(midR, pulledR, (w - 0.55f) / 0.45f);
                        }
                    }
                    else
                    {
                        float v = EaseInOut((_t - a) / (b - a));
                        if (inHand && Fuehrbar)
                        {
                            // 21.10. nachts: Spitze an der Muendung – ab jetzt fuehrt die Scheide die Klinge, die Hand folgt dem Griff
                            // (vorher steckte die Hand die Klinge mit ihrer begrenzten Handgelenk-Drehung schief hinein und die
                            // Waffe schnappte nach dem Loslassen in die Scheidenlage – Wegstecken-Film Bild 5–9).
                            // Lage aus dem zuletzt GEZEIGTEN Bild – hier steht die Hand noch in der Animationspose (IK folgt erst unten)
                            Vector3 wp0 = _shown ? FromBody(_shownWeaponPos) : pivot.position;
                            Quaternion wr0 = _shown ? FromBody(_shownWeaponRot) : pivot.rotation;
                            AttachSheathExact(pivot);
                            _seatFromPos = _sheath.InverseTransformPoint(wp0);
                            _seatFromRot = Quaternion.Inverse(_sheath.rotation) * wr0;
                            _releaseT = b; _guided = true; inHand = false;
                        }
                        if (_guided)
                        {
                            float rk = MotionMath.SmoothStep01(Mathf.Clamp01(v / 0.35f));
                            pivot.localRotation = Style == GripStyle.Staff ? SchaftSlerp(_seatFromRot, Quaternion.identity, rk) : Quaternion.Slerp(_seatFromRot, Quaternion.identity, rk);
                            pivot.localPosition = Vector3.Lerp(_seatFromPos, Vector3.zero, v);
                            wP = pivot.position; wR = pivot.rotation;
                        }
                        else
                        {
                            wP = Vector3.Lerp(pulledP, sP, v);
                            wR = sR;
                        }
                    }
                    // 23.10. (Stab-Wegstecken kippte): die Sollhand verlangte bis −196° Unterarmdrehung (Mitschrift: Abweichung bis 130°, der
                    // Stab hing an der geklemmten Hand und kippte waagerecht). Ein runder Stab darf in der Faust um den Schaft drehen.
                    if (Style == GripStyle.Staff) wR = StabRoll(wR, rotLocal);
                    HandForWeapon(wP, wR, off, rotLocal, out hP, out hR);
                    seating = _t >= b && inHand;
                }
                else
                {
                    if (_guided) { AttachSheathExact(pivot); _guided = false; }
                    releaseNow = inHand; // Wartezeit vorbei (oder Bild uebersprungen): jetzt loslassen
                    if (releaseNow) _releaseT = Mathf.Min(_t, 0.98f);
                    // Hand loest sich vom Griff und kehrt weich in die Animation zurueck. 15.10.: Anheben (3 cm) jetzt
                    // eingeblendet – vorher sprang das Ziel im Loslass-Bild um 3 cm nach oben und nahm die Waffe mit.
                    float k = EaseInOut((_t - _releaseT) / Mathf.Max(0.02f, 1f - _releaseT));
                    hP = graspP + _u * (0.03f * k);
                    hR = graspR;
                    weight = 1f - k;
                }
            }

            // Reichweite VOR dem Setzen pruefen: liegt das Handziel ausser Reichweite, gehen Oberkoerper (Drehung + Neigung)
            // und Schluesselbein so weit mit, dass der Arm leicht gebeugt ankommt (AssistGoal) – statt gestreckt am Klemmwert
            // von Solve haengen zu bleiben (Hand verfehlte den Griff, Ellbogen nahe der Streck-Singularitaet).
            if (!torsoFirst)
            {
                float need = _fromHand ? 0f : ReachAssistFor(hP, twist, torso) * weight;
                _reachAssist = MotionMath.Damp(_reachAssist, need, 0.03f, dt);
                ApplyTorso(twist, _reachAssist, hP, torso);
            }

            // 15.10. (4), Mitschrift Ziehen: Zwischen Scheide und Haltung verlangte die Zieldrehung ~200° Unterarmdrehung;
            // die anatomische Aufloesung (Schwenk/Klemme) kippte zwischen zwei Loesungen, die Hand drehte sich in einem Bild
            // um 35–40°, die Waffe in der Hand sprang 11–19 cm. Jetzt darf sich die ZIELDREHUNG der Hand hoechstens mit
            // 540°/s aendern (9° je Bild bei 60 Hz, im Koerperraum) – ein Umkippen wird zu einer kurzen Drehung ueber
            // mehrere Bilder. Der Griffkontakt wartet ohnehin bis zu GraspWait auf die Hand.
            if (_hasTargetRot)
            {
                Quaternion hRb = ToBody(hR);
                hRb = Quaternion.RotateTowards(_lastTargetRotBody, hRb, 540f * Mathf.Max(dt, 1f / 240f));
                hR = FromBody(hRb);
            }
            _lastTargetRotBody = ToBody(hR); _hasTargetRot = true;
            // 22.10. nachts (Ziehfilm: 12–15 cm Waffensprung genau beim Übergang Ziehen → Halten): die Deckung setzt die Hand mit
            // Ellenneigung (ws.right.ulnar), die Choreografie ohne – der Ellbogen sprang. Jetzt über den Schwung ein-, beim
            // Wegstecken zu Beginn ausgeblendet.
            float ulB = _phase == Phase.Drawing ? MotionMath.SmoothStep01(Mathf.Clamp01((_t - Phase2()) / (1f - Phase2())))
                      : _phase == Phase.Sheathing ? 1f - MotionMath.SmoothStep01(Mathf.Clamp01(_t / SheatheSwept())) : 0f;
            W.PlaceBone(hP, hR, ws.right.ulnar * ulB, 0.5f, weight, Mathf.Lerp(60f, 45f, ulB));   // Handgelenk-Grenze wie in der Deckung (45°)
            // 16.10.: Auch das ERGEBNIS der Handdrehung je Bild begrenzen (720°/s = 12° je Bild bei 60 Hz). Die anatomische
            // Aufloesung im Arm (Schwenk/Handgelenk-Klemme) sprang nahe der Grenze zwischen zwei Loesungen um 25–45° in einem
            // Bild – die Waffe am Handgelenk-Hebel damit um 5–11 cm. Ein kurzes Ueberschreiten der Handgelenk-Grenze ist
            // weniger sichtbar als der Sprung.
            {
                Quaternion jetzt = W.Hand.rotation;
                if (_hasPrevHand)
                {
                    Quaternion vorher = FromBody(_prevHandBody);
                    float max = 720f * Mathf.Max(dt, 1f / 240f);
                    if (Quaternion.Angle(vorher, jetzt) > max) W.Hand.rotation = Quaternion.RotateTowards(vorher, jetzt, max);
                }
                // 21.10. abends (Max: „zu keinem Zeitpunkt Körperteile unnatürlich verdrehen“; Lab: Handgelenk beim Ziehen und
                // Wegstecken 70–90° bei Grenze 55°): die Grenze lag nur VOR dieser Handsetzung – jetzt auch danach. Die Klemme
                // arbeitet auf der schon geschwindigkeitsbegrenzten Drehung, das Umkippen zwischen zwei Lösungen bleibt gedämpft.
                if (!DebugNoClamp) { W.ClampTwist(95f); W.ClampWrist(50f, 1f); }
                _prevHandBody = ToBody(W.Hand.rotation); _hasPrevHand = true;
            }
            _traceTarget = hP; _traceW = weight; _traceTargetRot = hR;
            // Griff/Loslassen erst bei echtem Kontakt (Lage UND Drehung); nach GraspWait erzwingen die Zweige oben das Umhaengen
            if (grasping && (W.Hand.position - hP).magnitude <= ContactDist && Quaternion.Angle(W.Hand.rotation, hR) <= ContactAngle)
            {
                if (Fuehrbar) _guided = true; else attachNow = true;   // Hand am Griff: herausziehen, die Scheide fuehrt (in die Hand erst mit freier Klinge)
                _graspT = _t;
            }
            if (seating && (pivot.position - sP).magnitude <= ContactDist && Quaternion.Angle(pivot.rotation, sR) <= ContactAngle)
            {
                releaseNow = true;
                _releaseT = _t;
            }
            // Messung: wie weit die Hand im Uebergabebild vom Griff entfernt blieb (Arm zu kurz, Handgelenk begrenzt)
            if (attachNow || releaseNow) _handoffMiss = (W.Hand.position - hP).magnitude;
            // Erst jetzt umhaengen: Hand steht in diesem Bild am Griff, die Waffe bleibt wo sie ist (kein Sprung)
            if (attachNow) { AttachHandAt(pivot, W.Hand); if (_longSettle) { _settleDur = 0.32f; _longSettle = false; } }
            if (releaseNow) AttachSheathAt(pivot);
        }

        /// <summary>Griff der Waffe neu vermessen, wenn Waffe, Modell oder Griffart wechseln.</summary>
        private void UpdateGrip(Transform pivot)
        {
            // 22.10. nachts: nur die Modell-Netze zaehlen (Effekt-Kinder, die an- und ausgehen, loesten Neuvermessung + Neueinsetzen aus)
            int sig = 7919;
            foreach (MeshFilter mf in pivot.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null)
                {
                    // auch die LAGE im Drehpunkt: beim Wechsel mit gezogener Waffe wird das neue Modell erst nach der Vermessung
                    // gedreht eingesetzt (AppearanceRig: Halteanker mit 90° Rollung) – sonst blieb die alte Vermessung stehen
                    Vector3 f = pivot.InverseTransformDirection(mf.transform.forward), u = pivot.InverseTransformDirection(mf.transform.up);
                    Vector3 p = pivot.InverseTransformPoint(mf.transform.position);
                    sig = sig * 31 + mf.GetHashCode() + mf.sharedMesh.GetHashCode()
                        + Mathf.RoundToInt(f.x * 50f) * 7 + Mathf.RoundToInt(f.y * 50f) * 13 + Mathf.RoundToInt(f.z * 50f) * 17
                        + Mathf.RoundToInt(u.x * 50f) * 19 + Mathf.RoundToInt(u.y * 50f) * 23 + Mathf.RoundToInt(p.magnitude * 200f) * 29;
                }
            if (pivot == _gripPivot && sig == _gripSig && Style == _gripStyle) return;
            _gripPivot = pivot; _gripSig = sig; _gripStyle = Style;
            _grip = WeaponGrip.Measure(pivot, Style, W.HandWidth);
            W.GripRadius = _grip.Valid ? Mathf.Max(0.008f, _grip.Radius) : 0.017f;
            O.GripRadius = W.GripRadius;
            if (_grip.Valid && pivot.parent == W.Hand && _phase == Phase.Held) BeginSettle(pivot, () => AttachHandExact(pivot, W.Hand));
        }

        /// <summary>Lage des Drehpunkts relativ zur Hand aus dem vermessenen Griff: Versatz (m, Handachsen) und Drehung.</summary>
        private void GripPose(out Vector3 pos, out Quaternion rot)
        {
            rot = W.FrameLocal * Quaternion.Inverse(_grip.RightFrame);
            pos = W.CenterLocal - rot * _grip.Right;
        }

        private void FinishInstant(Transform pivot)
        {
            if (_phase == Phase.Drawing) { AttachHandExact(pivot, W.Hand); _phase = Phase.Held; }
            else if (_phase == Phase.Sheathing) { AttachSheathExact(pivot); _phase = Phase.Sheathed; }
        }

        /// <summary>Oberkoerper dreht beim Ziehen zur Scheide hin und zurueck (Wirbelsaeule anteilig).</summary>
        private void Twist(float deg) => ApplyTorso(deg, 0f, Vector3.zero, null);

        // ---- Reichweiten-Hilfe (15.10. Griff-Pass)

        private bool SplitSpine => _chest != null && _upperChest != null && _chest != _upperChest;

        /// <summary>Neigeachse: kippt "oben" zur waagerechten Richtung Brust → Ziel (Oberkoerper beugt sich zum Ziel).</summary>
        private Vector3 LeanAxis(Vector3 target, Transform torso)
        {
            Vector3 h = Vector3.ProjectOnPlane(target - torso.position, _u);
            return h.sqrMagnitude < 1e-6f ? Vector3.zero : Vector3.Cross(_u, h).normalized;
        }

        /// <summary>Drehung (um _u, Grad), Neigung und Schluesselbein-Vorschub fuer Hilfe-Anteil <paramref name="s"/>.</summary>
        private void AssistAngles(float baseYaw, float s, Vector3 target, Transform torso, out float yaw, out float lean, out float clav)
        {
            // Drehrichtung aus der Geometrie (Schulter zum Ziel hin) und hoechstens so weit, wie der Winkel dorthin ist –
            // liegt das Ziel genau vor der Schulter, ist der Beitrag 0 (kein Umspringen der Richtung).
            Vector3 c = torso.position;
            float toward = Vector3.SignedAngle(Vector3.ProjectOnPlane(W.Upper.position - c, _u), Vector3.ProjectOnPlane(target - c, _u), _u);
            yaw = baseYaw + Mathf.Sign(toward) * Mathf.Min(AssistYaw, Mathf.Abs(toward)) * s;
            lean = AssistLean * s;
            clav = AssistClavicle * s;
        }

        private void TorsoRotations(float yaw, float lean, Vector3 leanAxis, out Quaternion q1, out Quaternion q2)
        {
            bool split = SplitSpine;
            float s1 = split ? 0.55f : 1f, s2 = split ? 0.45f : 0f;
            bool hasLean = Mathf.Abs(lean) > 0.001f && leanAxis.sqrMagnitude > 0.5f;
            q1 = Quaternion.AngleAxis(yaw * s1, _u);
            q2 = Quaternion.AngleAxis(yaw * s2, _u);
            if (hasLean) { q1 = Quaternion.AngleAxis(lean * s1, leanAxis) * q1; q2 = Quaternion.AngleAxis(lean * s2, leanAxis) * q2; }
        }

        /// <summary>Schulterlage (Oberarm-Ursprung), wenn Oberkoerper und Schluesselbein so gedreht waeren – rein rechnerisch.</summary>
        private Vector3 PredictShoulder(float yaw, float lean, Vector3 leanAxis, float clav, Vector3 target)
        {
            TorsoRotations(yaw, lean, leanAxis, out Quaternion q1, out Quaternion q2);
            Vector3 s = W.Upper.position;
            Transform k = W.Clavicle;
            Vector3 kp = k != null ? k.position : s;
            if (SplitSpine)
            {
                Vector3 c = _chest.position;
                Vector3 uc = c + q1 * (_upperChest.position - c);
                s = c + q1 * (s - c); kp = c + q1 * (kp - c);
                s = uc + q2 * (s - uc); kp = uc + q2 * (kp - uc);
            }
            else if (_upperChest != null)
            {
                Vector3 c = _upperChest.position;
                s = c + q1 * (s - c); kp = c + q1 * (kp - c);
            }
            if (clav > 0.01f && k != null)
            {
                Vector3 ax = Vector3.Cross(s - kp, target - kp);
                if (ax.sqrMagnitude > 1e-10f)
                    s = kp + Quaternion.AngleAxis(Mathf.Min(clav, Vector3.Angle(s - kp, target - kp)), ax.normalized) * (s - kp);
            }
            return s;
        }

        /// <summary>
        /// Kleinster Hilfe-Anteil (0..1), mit dem die Schulter nah genug ans Handziel kommt (Abstand ≤ AssistGoal · Reichweite).
        /// Stetig in den Eingaben (Halbierungssuche, 10 Schritte), keine Speicherzuteilung.
        /// </summary>
        private float ReachAssistFor(Vector3 target, float baseYaw, Transform torso)
        {
            if (torso == null || !W.Valid) return 0f;
            float goal = W.Reach * AssistGoal;
            Vector3 axis = LeanAxis(target, torso);
            AssistAngles(baseYaw, 0f, target, torso, out float y, out float l, out float c);
            if ((PredictShoulder(y, l, axis, c, target) - target).magnitude <= goal) return 0f;
            AssistAngles(baseYaw, 1f, target, torso, out y, out l, out c);
            if ((PredictShoulder(y, l, axis, c, target) - target).magnitude > goal) return 1f;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 10; i++)
            {
                float m = (lo + hi) * 0.5f;
                AssistAngles(baseYaw, m, target, torso, out y, out l, out c);
                if ((PredictShoulder(y, l, axis, c, target) - target).magnitude > goal) lo = m; else hi = m;
            }
            return hi;
        }

        /// <summary>Oberkoerper drehen/neigen (Wirbelsaeule anteilig) und Schluesselbein vorschieben – wie vorhergesagt.</summary>
        private void ApplyTorso(float baseYaw, float s, Vector3 target, Transform torso)
        {
            float yaw = baseYaw, lean = 0f, clav = 0f;
            Vector3 axis = Vector3.zero;
            if (s > 0.0001f && torso != null)
            {
                axis = LeanAxis(target, torso);
                AssistAngles(baseYaw, s, target, torso, out yaw, out lean, out clav);
            }
            if (Mathf.Abs(yaw) < 0.01f && lean < 0.001f && clav < 0.01f) return;
            TorsoRotations(yaw, lean, axis, out Quaternion q1, out Quaternion q2);
            if (SplitSpine)
            {
                _chest.rotation = q1 * _chest.rotation;
                _upperChest.rotation = q2 * _upperChest.rotation;
            }
            else if (_upperChest != null) _upperChest.rotation = q1 * _upperChest.rotation;
            Transform k = W.Clavicle;
            if (clav > 0.01f && k != null)
            {
                Vector3 kp = k.position, sh = W.Upper.position;
                Vector3 ax = Vector3.Cross(sh - kp, target - kp);
                if (ax.sqrMagnitude > 1e-10f)
                    k.rotation = Quaternion.AngleAxis(Mathf.Min(clav, Vector3.Angle(sh - kp, target - kp)), ax.normalized) * k.rotation;
            }
        }

        private Quaternion _stabHandBody;
        private bool _stabHandGesetzt;

        /// <summary>
        /// 23.10.: Drehung der Stab-Sollage um den eigenen Schaft (lokal +Z) so wählen, dass die Hand ihrer vorigen Lage am nächsten bleibt –
        /// die Schaftrichtung bleibt, nur die Faust rollt um den runden Stab (keine überdrehte Pronation, kein Kippen durch Klemmen).
        /// </summary>
        private Quaternion StabRoll(Quaternion wR, Quaternion rotLocal)
        {
            Quaternion bezug = _stabHandGesetzt ? FromBody(_stabHandBody) : FromBody(_lastTargetRotBody);
            Quaternion inv = Quaternion.Inverse(rotLocal);
            float best = 0f, bestWinkel = float.MaxValue;
            for (int i = 0; i < 36; i++)
            {
                float r = i * 10f;
                float wkl = Quaternion.Angle(wR * Quaternion.AngleAxis(r, Vector3.forward) * inv, bezug);
                if (wkl < bestWinkel) { bestWinkel = wkl; best = r; }
            }
            for (float r = best - 9f; r <= best + 9f; r += 1f)
            {
                float wkl = Quaternion.Angle(wR * Quaternion.AngleAxis(r, Vector3.forward) * inv, bezug);
                if (wkl < bestWinkel) { bestWinkel = wkl; best = r; }
            }
            Quaternion erg = wR * Quaternion.AngleAxis(best, Vector3.forward);
            _stabHandBody = ToBody(erg * inv);
            _stabHandGesetzt = true;
            return erg;
        }

        private static readonly int CastingId = Animator.StringToHash("Casting");
        /// <summary>23.10.: Wirkt die Figur gerade (Animator-Parameter „Casting“, nur wenn der Controller ihn kennt)?</summary>
        private static readonly Dictionary<RuntimeAnimatorController, bool> HatCasting = new Dictionary<RuntimeAnimatorController, bool>();
        private static bool AnimatorWirkt(Animator a)
        {
            RuntimeAnimatorController c = a.runtimeAnimatorController;
            if (c == null) return false;
            if (!HatCasting.TryGetValue(c, out bool hat))
            {
                hat = false;
                foreach (AnimatorControllerParameter p in a.parameters) if (p.nameHash == CastingId) { hat = true; break; }
                HatCasting[c] = hat;
            }
            if (!hat) return false;
            if (a.GetBool(CastingId)) return true;
            // Auslösen/Absetzen läuft noch, nachdem „Casting“ schon falsch ist
            int ebene = a.GetLayerIndex("Cast");
            return ebene >= 0 && !a.GetCurrentAnimatorStateInfo(ebene).IsName("Empty");
        }

        private static float EaseInOut(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>
        /// 22.10.2026 (Stab-Wegstecken): Drehung einer Stangenwaffe in zwei Teilen – Schaftrichtung auf kürzestem Weg, Drehung um den Schaft
        /// getrennt. Der Rückenanker des Stabs ist um den Schaft verdreht; liegen Start und Ziel ~180° auseinander, wählte Slerp eine beliebige
        /// Achse und der ganze Stab kippte waagerecht über den Kopf (Ziehfilm i_stab_3_wegstecken: Sprung 60 cm, Unterarm −165°).
        /// </summary>
        private static Quaternion SchaftSlerp(Quaternion a, Quaternion b, float t)
        {
            Vector3 da = a * Vector3.forward, db = b * Vector3.forward;
            Quaternion schwenk = Quaternion.FromToRotation(da, db);
            Quaternion ohneRoll = schwenk * a;                       // Schaft wie b, Roll wie a
            (Quaternion.Inverse(ohneRoll) * b).ToAngleAxis(out float roll, out Vector3 achse);
            if (roll > 180f) roll -= 360f;
            if (Vector3.Dot(achse, Vector3.forward) < 0f) roll = -roll;   // Rest ist (nahezu) eine Drehung um den eigenen Schaft (lokal +Z)
            return Quaternion.Slerp(Quaternion.identity, schwenk, t) * a * Quaternion.AngleAxis(roll * t, Vector3.forward);
        }

        /// <summary>
        /// Schwung in die Haltung: startet ohne Ruck (Geschwindigkeit 0), wird schnell, schiesst am Ende leicht ueber
        /// (<paramref name="over"/>) und setzt sich weich – wie eine schwere Klinge, die abgefangen wird.
        /// </summary>
        private static float SweepCurve(float x, float over)
        {
            x = Mathf.Clamp01(x);
            float s = x * x * x * (x * (x * 6f - 15f) + 10f);
            float bump = Mathf.Sin(x * Mathf.PI);
            return s + over * bump * bump * x * x;
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float it = 1f - t;
            return it * it * a + 2f * it * t * b + t * t * c;
        }

        private static Vector3 Bezier3(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float it = 1f - t;
            return it * it * it * a + 3f * it * it * t * b + 3f * it * t * t * c + t * t * t * d;
        }

        // ================================================================== Umhaengen ohne Sprung

        /// <summary>In die Hand, Lage bleibt – dann in ~0,1 s in den exakten Griff.</summary>
        private void AttachHandAt(Transform pivot, Transform hand)
        {
            BeginSettle(pivot, () => AttachHandExact(pivot, hand));
        }

        /// <summary>In die Scheide, Lage bleibt – dann in ~0,1 s exakt einrasten.</summary>
        private void AttachSheathAt(Transform pivot)
        {
            BeginSettle(pivot, () => AttachSheathExact(pivot));
        }

        private void BeginSettle(Transform pivot, System.Action attach)
        {
            Vector3 wp = pivot.position;
            Quaternion wr = pivot.rotation;
            attach();
            // Von der alten Weltlage aus weich in die neue Ruhelage
            Transform parent = pivot.parent;
            _settleFromPos = parent != null ? parent.InverseTransformPoint(wp) : wp;
            _settleFromRot = parent != null ? Quaternion.Inverse(parent.rotation) * wr : wr;
            _settleTo = new Pose(pivot.localPosition, pivot.localRotation);
            pivot.localPosition = _settleFromPos;
            pivot.localRotation = _settleFromRot;
            _settle = 0f;
            _settleDur = 0.18f;
        }

        private Pose _settleTo;
        private float _settleDur = 0.18f;
        private bool _longSettle;

        private void SettleWeapon(float dt)
        {
            if (_settle >= 1f || _pivot == null) return;
            _settle = Mathf.Min(1f, _settle + dt / _settleDur);
            float k = EaseInOut(_settle);
            _pivot.localPosition = Vector3.Lerp(_settleFromPos, _settleTo.position, k);
            _pivot.localRotation = Quaternion.Slerp(_settleFromRot, _settleTo.rotation, k);
        }

        private void AttachHandExact(Transform pivot, Transform hand)
        {
            pivot.SetParent(hand, false);
            float inv = 1f / Mathf.Max(0.0001f, hand.lossyScale.x);
            float slide = WeaponPoses.Guard(Style, OffhandRole.Free).slide;
            if (_grip.Valid && _gripPivot == pivot && hand == W.Hand)
            {
                GripPose(out Vector3 gpos, out Quaternion grot);
                pivot.localRotation = grot;
                pivot.localPosition = gpos * inv;
                pivot.localScale = Vector3.one * inv;
                _appliedSlide = slide;
                return;
            }
            if (_grips.TryGetValue(pivot, out Pose gp))
            {
                pivot.localRotation = gp.rotation;
                pivot.localPosition = gp.position + gp.rotation * new Vector3(0f, 0f, -slide) * inv;
            }
            pivot.localScale = Vector3.one * inv;
            _appliedSlide = slide;
        }

        /// <summary>
        /// 16.10.: Schild mit gesteckter Waffe auf dem Ruecken, beim Ziehen sofort am Unterarm (eigene Choreografie dafuer
        /// spaeter). Die Ausruestung haengt den Schild bei jedem Wechsel wieder an den Arm – darum jedes Bild nachsehen.
        /// </summary>
        /// <summary>Schild: 0 = auf dem Ruecken, 1 = am Unterarm; dazwischen die Choreografie (16.10./17.10.: vorher Sprung in
        /// einem Bild, Changelog (152) „offen“). Der Schild wandert in ~0,45 s ueber die linke Schulter von der einen Lage in die
        /// andere – parallel zum Ziehen/Wegstecken des Schwerts.</summary>
        private float _shieldArm = -1f;

        private void CarryShield(bool onBack)
        {
            var rig = _orch != null && _orch.Visuals != null ? _orch.Visuals.Rig : null;
            if (rig == null) return;
            var def = rig.SlotDefinition(EquipmentSlot.OffHand);
            if (def == null || def.sheath != WeaponSheath.Arm) return;
            Transform m = rig.WeaponModel(EquipmentSlot.OffHand);
            if (m == null) return;
            if (!rig.SheathPose(WeaponSheath.ShieldBack, out Transform boneB, out Vector3 lpB, out Quaternion lrB, out float lsB)) return;
            if (!rig.SheathPose(WeaponSheath.Arm, out Transform boneA, out Vector3 lpA, out Quaternion lrA, out float lsA)) return;
            float ziel = onBack ? 0f : 1f;
            if (_shieldArm < 0f) _shieldArm = ziel;
            else _shieldArm = Mathf.MoveTowards(_shieldArm, ziel, Mathf.Min(Time.deltaTime, 0.1f) / 0.45f);
            float k = MotionMath.SmoothStep01(_shieldArm);
            Transform parent = k >= 0.5f ? boneA : boneB;
            if (m.parent != parent) m.SetParent(parent, true);
            m.localScale = Vector3.one * (k >= 0.5f ? lsA : lsB);
            if (k <= 0f || k >= 1f)
            {
                // Endlage exakt (auch wenn die Ausruestung den Schild zwischendurch neu angehaengt hat)
                Transform b = k >= 1f ? boneA : boneB;
                m.localPosition = k >= 1f ? lpA : lpB; m.localRotation = k >= 1f ? lrA : lrB;
                return;
            }
            Vector3 pB = boneB.TransformPoint(lpB), pA = boneA.TransformPoint(lpA);
            Quaternion rB = boneB.rotation * lrB, rA = boneA.rotation * lrA;
            // Bogen ueber die Schulter: in der Mitte 12 cm hoeher und 8 cm nach aussen (links), damit er nicht durch den Ruecken zieht
            Vector3 up = _model != null ? _model.up : Vector3.up, left = _model != null ? -_model.right : Vector3.left;
            float bogen = Mathf.Sin(k * Mathf.PI);
            m.position = Vector3.Lerp(pB, pA, k) + up * (0.12f * bogen) + left * (0.08f * bogen);
            m.rotation = Quaternion.Slerp(rB, rA, k);
        }

        private void AttachSheathExact(Transform pivot)
        {
            if (_sheath == null) return;
            pivot.SetParent(_sheath, false);
            pivot.localPosition = Vector3.zero;
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one / Mathf.Max(0.0001f, _sheath.lossyScale.x);
        }

        // Zuletzt angezeigte Lagen (Ende des Bildes) – Start jeder Choreografie, damit nichts springt
        private Vector3 _shownHandPos, _shownWeaponPos;
        private Quaternion _shownHandRot = Quaternion.identity, _shownWeaponRot = Quaternion.identity;
        private bool _shown;
        private Vector3 _lastStep;

        private void TrackJump()
        {
            _shownHandPos = ToBody(W.Hand.position);
            _shownHandRot = ToBody(W.Hand.rotation);
            if (_pivot != null) { _shownWeaponPos = ToBody(_pivot.position); _shownWeaponRot = ToBody(_pivot.rotation); }
            _shown = true;
            if (_pivot == null || !_pivot.gameObject.activeInHierarchy) { _hasLastWeapon = false; return; }
            Vector3 local = _shownWeaponPos;
            // Sprung (15.10.): Abstand der Waffe (Griffpunkt, Koerperraum) von der Stelle, an der sie bei gleichmaessiger
            // Bewegung waere – in Zentimetern, unabhaengig von der Bildzeit (die alte m/s-Messung schlug bei sehr kurzen
            // Bildern im Lab aus). Fluessige Schwuenge bleiben unter ~2 cm, ein Umhaengen mit Versatz faellt sofort auf.
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            Transform parent = _pivot.parent;
            if (_hasLastWeapon)
            {
                Vector3 predicted = _lastWeaponLocal + _lastStep * dt;
                float raw = (local - predicted).magnitude;
                // 15.10. (3): Ein langes Bild (z. B. Bildaufnahme im Lab, 100 ms) vergroessert die Abweichung einer gekruemmten,
                // fluessigen Bahn quadratisch (a·dt²) – das ist kein Sprung, den ein Spieler bei 60 Bildern/s sieht. Darum auf
                // ein 60-Hz-Bild umrechnen. Umhaengen (Elternwechsel) wird NICHT umgerechnet: ein echter Versatz bleibt voll sichtbar.
                const float dt60 = 1f / 60f;
                bool reparent = parent != _lastParent;
                float off = !reparent && dt > dt60 * 1.5f ? raw * (dt60 / dt) * (dt60 / dt) : raw;
                if (off > _maxJump)
                {
                    _maxJump = off;
                    _jumpInfo = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} t={1:0.00}{2}{3} · Bild {4:0} ms, roh {5:0.0} cm",
                        _phase, _t, reparent ? " · umgehaengt" : "", _settle < 1f ? " · setzt sich" : "", dt * 1000f, raw * 100f)
                        + string.Format(System.Globalization.CultureInfo.InvariantCulture, " · Hand verfehlte Griff zuletzt {0:0.0} cm", _handoffMiss * 100f);
                }
                _lastStep = (local - _lastWeaponLocal) / dt;
                if (Trace != null && (Choreographing || reparent))
                {
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    Vector3 h = ToBody(W.Hand.position), g = ToBody(_traceTarget);
                    Trace.AppendLine(string.Format(ci, "{0};{1:0.000};{2:0.0};{3:0.0};{4:0.000};{5:0.000};{6:0.000};{7:0.000};{8:0.000};{9:0.000};{10:0.000};{11:0.000};{12:0.000};{13:0.00};{14:0.00};{15};{16:0.0}",
                        _phase, _t, dt * 1000f, raw * 100f, local.x, local.y, local.z, h.x, h.y, h.z, g.x, g.y, g.z, _traceW, _reachAssist,
                        parent != null ? parent.name : "-", (h - g).magnitude * 100f)
                        + string.Format(ci, ";{0:0.0};{1:0.0};{2:0.0};{3:0}", Quaternion.Angle(W.Hand.rotation, _traceTargetRot),
                            Quaternion.Angle(ToBody(W.Hand.rotation), _traceLastHand), W.DebugPronation, W.DebugSwing));
                    _traceLastHand = ToBody(W.Hand.rotation);
                }
            }
            else _lastStep = Vector3.zero;
            _lastWeaponLocal = local;
            _lastParent = parent;
            _hasLastWeapon = true;
        }

        private void SetStanceFlags(in MotionContext c, bool inHand)
        {
            if (_stance == null) return;
            // Aufstuetzen im Stand nur mit Einhandklinge (Zweihaender/Stangen/Gewehre halten beide Haende an der Waffe)
            // Nicht waehrend Ziehen/Wegstecken und nicht im Kampf – sonst verstellt das Aufstuetzen die Klinge in der Hand.
            bool plant = inHand && Style == GripStyle.OneHand && c.Stance != MotionStance.Combat && _phase == Phase.Held;
            _stance.WeaponSheathed = !plant;
            _stance.CombatReady = c.Stance == MotionStance.Combat;
        }

        private void SetHold(float target, float dt)
        {
            _hold = MotionMath.Damp(_hold, target, 0.08f, dt);
            if (_holdLayer == -2) _holdLayer = _anim.GetLayerIndex("WeaponHold");
            if (_holdLayer >= 0) _anim.SetLayerWeight(_holdLayer, _hold);
        }

        // ================================================================== Scheiden

        /// <summary>
        /// Scheide dort, wo das Aussehen die Waffe hinsteckt – um die Klingenachse so gedreht, dass die rechte Hand den
        /// Griff von aussen fassen kann (Handflaeche zum Koerper). Je Scheide getrennt fuer Nah- und Fernkampf.
        /// </summary>
        private Transform SheathAnchor(WeaponSheath kind, bool ranged, bool staff, bool bow = false)
        {
            int key = (int)kind * 2 + (ranged ? 1 : 0) + (staff ? 100 : 0) + (bow ? 200 : 0);
            bool haveRig = _orch != null && _orch.Visuals != null && _orch.Visuals.Rig != null && _orch.Visuals.Rig.Ready;
            if (_sheaths.TryGetValue(key, out Transform t) && t != null)
            {
                if (!_fallbackSheaths.Contains(key) || !haveRig || Time.time < _sheathRetry) return t;
            }
            _sheathRetry = Time.time + 1f;
            // Stab am Ruecken: Kopf nach oben ueber die rechte Schulter, wird nach oben herausgehoben
            if (staff && kind == WeaponSheath.Back)
            {
                if (t != null) return t;
                Transform sp = _upperChest != null ? _upperChest : _hips;
                t = new GameObject("Scheide (Ruecken, Stab)").transform;
                t.SetParent(sp, false);
                // 21.10. abends (Max: „Positionen im weggesteckten Zustand korrigieren“): Mitte 55 cm unter der Brust und fast
                // senkrecht ließ den 1,7-m-Stab neben dem Körper bis auf den Boden stehen → Mitte auf Rückenmitte (25 cm unter der
                // Brust), schräg über den Rücken wie der Zweihänder (Kopf über der rechten Schulter, unteres Ende links über dem Boden).
                // 23.10. geprüft und verworfen: Mitte 25 cm tiefer (Griff auf Schulterhöhe) – Wegstecken 8→29 cm, Ziehen 11→36 cm Sprung,
                // Handgelenk 58°; das Überkopf-Kippen am Anfang blieb. Ursache ist Griffstelle/Handdrehung hinter der Schulter, nicht die Höhe.
                t.position = sp.position - _model.forward * 0.22f - _model.up * 0.17f;   // 22.10.: 5 cm weiter hinten, 8 cm höher (Haut-Pruefung: Stab im rechten Unterarm)
                t.rotation = Quaternion.LookRotation((_model.up * 0.8f + _model.right * 0.55f - _model.forward * 0.1f).normalized, -_model.forward);
                OrientForGrasp(t, kind, false);
                // 22.10. nachts (Ziehfilm: der Griff an diesem Stab verlangte −157…−198° Unterarmdrehung – genau die Grenze, an der
                // jede Richtung mehrdeutig ist; die Waffe sprang beim Schwung in die Haltung 18–50 cm): der runde Stab hängt um die
                // eigene Achse gedreht, die Hand greift ihn dann fast ohne Unterarmdrehung.
                t.rotation = Quaternion.AngleAxis(180f, t.forward) * t.rotation;
                _sheaths[key] = t;
                return t;
            }
            if (haveRig && _orch.Visuals.Rig.SheathPose(kind, out Transform bone, out Vector3 lp, out Quaternion lr, out float ls) && bone.IsChildOf(_model))
            {
                Transform old = t;
                t = new GameObject("Scheide (" + kind + (ranged ? ", Fern" : "") + ")").transform;
                t.SetParent(bone, false);
                t.localPosition = lp; t.localRotation = lr; t.localScale = Vector3.one * ls;
                if (!bow) OrientForGrasp(t, kind, ranged);
                _fallbackSheaths.Remove(key);
                _sheaths[key] = t;
                if (old != null)
                {
                    if (_pivot != null && _pivot.parent == old) { _sheath = t; AttachSheathExact(_pivot); }
                    Destroy(old.gameObject);
                }
                return t;
            }
            if (t != null) return t;
            t = new GameObject("Scheide (" + kind + ", Ersatz)").transform;
            Transform parent = (kind == WeaponSheath.Back || kind == WeaponSheath.BackBow) && _upperChest != null ? _upperChest : _hips;
            t.SetParent(parent, false);
            Vector3 r = _model.right, u = _model.up, f = _model.forward;
            Vector3 bladeDir;
            switch (kind)
            {
                case WeaponSheath.HipRight:
                    t.position = _hips.position + r * 0.2f + f * 0.03f - u * 0.02f;
                    bladeDir = -u * 0.8f - f * 0.5f + r * 0.1f; break;
                case WeaponSheath.Back:
                    t.position = parent.position - f * 0.15f + r * 0.13f + u * 0.14f;
                    bladeDir = -u * 0.88f - r * 0.42f - f * 0.2f; break;
                case WeaponSheath.BackBow:
                    t.position = parent.position - f * 0.19f - r * 0.1f + u * 0.02f;
                    t.rotation = Quaternion.LookRotation((0.83f * r + 0.55f * u).normalized, (-0.55f * r + 0.83f * u).normalized);
                    _sheaths[key] = t;
                    _fallbackSheaths.Add(key);
                    return t;
                default:
                    t.position = _hips.position - r * 0.22f + f * 0.08f - u * 0.02f;
                    bladeDir = -u * 0.6f - f * 0.74f - r * 0.2f; break;
            }
            t.rotation = Quaternion.LookRotation(bladeDir.normalized, u);
            OrientForGrasp(t, kind, ranged);
            _sheaths[key] = t;
            _fallbackSheaths.Add(key);
            return t;
        }

        private void OrientForGrasp(Transform t, WeaponSheath kind, bool ranged)
        {
            Vector3 blade = t.forward;
            Vector3 center = kind == WeaponSheath.Back && _upperChest != null ? _upperChest.position : _hips.position + _model.up * 0.1f;
            // Handrahmen im Waffenraum: vermessener Griff, sonst Nahkampf (Knoechel entlang der Klinge, Handflaeche +Y)
            // bzw. Fernkampf (Knoechel = Waffen-oben, Handflaeche zur linken Waffenseite)
            Quaternion frame = _grip.Valid ? _grip.RightFrame
                : ranged ? Quaternion.LookRotation(Vector3.up, Vector3.left) : Quaternion.identity;
            // Um die Waffenachse die Lage suchen, in der die rechte Hand natuerlich greift: Hand in Verlaengerung des
            // Unterarms (von der Schulter weg zum Griff), Ellbogen erreichbar, Unterarm hoechstens ~70° aus der
            // Neutralstellung gedreht (Handflaeche zur Innenseite der Armebene, wie beim Handschlag), Handflaeche eher zum Koerper.
            Vector3 shoulder = W.Upper != null ? W.Upper.position : center;
            Vector3 reach = (t.position - shoulder).normalized;
            Vector3 toBody = Vector3.ProjectOnPlane(center - t.position, blade).normalized;
            Vector3 baseUp = Vector3.ProjectOnPlane(t.up, blade).normalized;
            float lu = W.UpperLength, ll = W.LowerLength;
            float best = float.MinValue;
            Vector3 bestUp = baseUp;
            for (int i = 0; i < 24; i++)
            {
                Vector3 up = Quaternion.AngleAxis(i * 15f, blade) * baseUp;
                Quaternion hand = Quaternion.LookRotation(blade, up) * frame;
                Vector3 k = hand * Vector3.forward, palm = hand * Vector3.up;
                Vector3 dir = W.Left ? Vector3.Cross(palm, k) : Vector3.Cross(k, palm); // Handrichtung der Waffenhand fuer diesen Griff
                float score = Vector3.Dot(dir, reach) + 0.35f * Vector3.Dot(palm, toBody);
                if (lu > 0.05f && ll > 0.05f)
                {
                    Vector3 wrist = t.position - dir * 0.08f;
                    Vector3 elbow = wrist - dir * ll;
                    score -= 2.5f * Mathf.Abs(Vector3.Distance(shoulder, elbow) - lu);
                    Vector3 n = Vector3.Cross(elbow - shoulder, wrist - elbow);
                    if (n.sqrMagnitude > 1e-6f)
                    {
                        float twist = Mathf.Abs(Vector3.SignedAngle(Vector3.ProjectOnPlane(n, dir), Vector3.ProjectOnPlane(palm, dir), dir));
                        score -= Mathf.Max(0f, twist - 70f) / 45f;
                    }
                }
                if (score > best) { best = score; bestUp = up; }
            }
            t.rotation = Quaternion.LookRotation(blade, bestUp);
        }
    }
}
