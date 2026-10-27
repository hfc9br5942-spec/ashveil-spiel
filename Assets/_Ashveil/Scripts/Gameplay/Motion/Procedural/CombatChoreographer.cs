using Ashveil.Data;
using Ashveil.Data.Motion;
using Ashveil.Gameplay.Audio;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Visuals;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>
    /// Kampf-Choreografie (Kampfumbau 29.09.): selbst gebaute Bewegungen statt Fremd-Clips.
    ///   Angriffe   Schluesselposen je Waffenart (<see cref="MoveLibrary"/>), weich verbunden, Arme ueber die anatomische
    ///              IK, Rumpf dreht/neigt mit. Schwere Waffen bleiben in beiden Haenden, Dolche koennen umgreifen.
    ///   Beidhaendig Zweite Waffe wird mit der linken Hand gezogen, gehalten und schlaegt im Wechsel.
    ///   Stab       Zauber-Choreografien (Stab und freie Zauberhand).
    ///   Bogen      Pfeil aus dem Koecher, auflegen, heben, spannen bis zum Kinn, loesen – Sehne verformt sich.
    ///   Ausweichen je Ruestung: Rolle (Leder), Satz (Platte/Kette), Wirbelschritt (Stoff).
    ///   Schildblock Schild stoesst dem Schlag entgegen.
    /// Laeuft nach der Waffenhand (57) und vor Emotes (60) und der Traegheits-Ueberblendung (90), die jeden Uebergang
    /// zusaetzlich glaettet. Meldet laufende Aktionen an <see cref="AnimatorActions"/> – so lassen alle anderen Schichten
    /// dem Koerper Platz, auch bei fremden Spielern.
    /// </summary>
    [DefaultExecutionOrder(58)]
    [DisallowMultipleComponent]
    public sealed partial class CombatChoreographer : MonoBehaviour
    {
        public static bool GlobalEnabled = true;

        private Animator _anim;
        private Transform _model;
        private WeaponHandPoser _hands;
        private WeaponUser _weapons;
        private CharacterMotor _motor;
        private Health _health;
        private CharacterEquipment _equip;
        private CharacterMotionOrchestrator _orch;
        private CharacterLod _lod;
        private EmotePoser _emote;

        private Transform _hips, _spine, _chest, _upperChest, _neck, _head;
        private Transform _lUpLeg, _lLeg, _lFoot, _rUpLeg, _rLeg, _rFoot;
        private bool _ready;
        private Vector3 _u, _f, _rt;

        /// <summary>Laeuft gerade eine eigene Bewegung (Lab/Tests)?</summary>
        public bool Busy => _running || _dodgeT >= 0f || _blockT >= 0f || _evadeT >= 0f || _bowActive;
        /// <summary>Name der laufenden Bewegung (Lab-Bericht).</summary>
        public string CurrentMove { get; private set; } = "";

        /// <summary>Uebernimmt die Choreografie diese Waffe (sonst spielt der Animator seinen Clip)?</summary>
        public static bool Handles(WeaponDefinition w, AttackKind kind)
        {
            if (!GlobalEnabled || w == null || kind == AttackKind.Air || kind == AttackKind.Plunge) return false;
            GripStyle s = WeaponPoses.StyleOf(w.weaponClass, w.HeldLikeMelee, false);
            return s == GripStyle.OneHand || s == GripStyle.TwoHandBlade || s == GripStyle.TwoHandHeavy || s == GripStyle.Polearm ||
                   s == GripStyle.Dagger || s == GripStyle.Staff || s == GripStyle.Bow;
        }

        private void Awake()
        {
            _anim = GetComponent<Animator>();
            _model = transform;
            _hands = GetComponent<WeaponHandPoser>();
            _motor = GetComponentInParent<CharacterMotor>();
            _weapons = GetComponentInParent<WeaponUser>();
            _health = GetComponentInParent<Health>();
            _equip = GetComponentInParent<CharacterEquipment>();
            _orch = GetComponentInParent<CharacterMotionOrchestrator>();
            Transform root = _motor != null ? _motor.transform : transform.root;
            if (_orch == null && root != null) _orch = root.GetComponentInChildren<CharacterMotionOrchestrator>();
            _lod = GetComponentInParent<CharacterLod>();
            _emote = GetComponent<EmotePoser>();
        }

        private void OnEnable()
        {
            if (_weapons != null) { _weapons.AttackStarted += OnAttackStarted; _weapons.ProjectileLaunched += OnProjectileLaunched; }
            if (_motor != null) _motor.Dodged += OnDodged;
            if (_health != null) _health.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            if (_weapons != null) { _weapons.AttackStarted -= OnAttackStarted; _weapons.ProjectileLaunched -= OnProjectileLaunched; }
            if (_motor != null) _motor.Dodged -= OnDodged;
            if (_health != null) _health.Damaged -= OnDamaged;
            AnimatorActions.SetProcedural(_anim, false, false);
            ResetFlip();
            HideBowExtras();
            StopReaktion(); _evadeT = -1f; _blockT = -1f;   // 22.10. (Codex): kein alter Treffer nach Wiederaktivieren
        }

        private bool EnsureBones()
        {
            if (_ready) return true;
            if (_anim == null || !_anim.isHuman || _anim.avatar == null) return false;
            Transform B(HumanBodyBones b) => _anim.GetBoneTransform(b);
            _hips = B(HumanBodyBones.Hips); _spine = B(HumanBodyBones.Spine); _chest = B(HumanBodyBones.Chest);
            _upperChest = B(HumanBodyBones.UpperChest); _neck = B(HumanBodyBones.Neck); _head = B(HumanBodyBones.Head);
            _lUpLeg = B(HumanBodyBones.LeftUpperLeg); _lLeg = B(HumanBodyBones.LeftLowerLeg); _lFoot = B(HumanBodyBones.LeftFoot);
            _rUpLeg = B(HumanBodyBones.RightUpperLeg); _rLeg = B(HumanBodyBones.RightLowerLeg); _rFoot = B(HumanBodyBones.RightFoot);
            _ready = _hips != null && _spine != null && _chest != null && _hands != null && _lUpLeg != null && _rUpLeg != null;
            return _ready;
        }

        // Koerperraum: Modellachsen am Hueftursprung (wie WeaponPoses)
        private Vector3 M(Vector3 v) => _rt * v.x + _u * v.y + _f * v.z;
        private Vector3 FromBody(Vector3 local) => _hips.position + M(local);
        private Vector3 ToBodyDir(Vector3 w) => new Vector3(Vector3.Dot(w, _rt), Vector3.Dot(w, _u), Vector3.Dot(w, _f));
        private Vector3 ToBody(Vector3 world) => ToBodyDir(world - _hips.position);

        // ================================================================== Takt

        private void LateUpdate()
        {
            bool upper = false, full = false;
            if (!GlobalEnabled || !EnsureBones() || !_anim.isActiveAndEnabled)
            {
                AnimatorActions.SetProcedural(_anim, false, false);
                return;
            }
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _u = _model.up; _f = _model.forward; _rt = _model.right;
            bool alive = _health == null || _health.IsAlive;
            bool emoting = (_emote != null && _emote.Active) || (_motor != null && _motor.IsMounted);
            bool detailed = _lod == null || _lod.Level == 0;
            if (!alive || emoting)
            {
                _running = false; _dodgeT = -1f; _blockT = -1f; _evadeT = -1f; _tail = -1f;
                StopReaktion();   // 22.10.: kein Rest-Treffer nach Tod/Emote/Reiten
                ResetFlip();
                StopBow();
                AnimatorActions.SetProcedural(_anim, false, false);
                return;
            }

            // Reihenfolge: Ausweichen (ganzer Koerper) > Angriff > Bogen > Beidhaendig-Tragen > Schildblock
            if (_dodgeT >= 0f)
            {
                bool dodging = UpdateDodge(dt, detailed);
                full = dodging && _dodgeFull;
            }
            if (!full)
            {
                if (_running || _tail >= 0f) upper = UpdateAttack(dt, detailed) || upper;
                upper = UpdateBow(dt, detailed) || upper;
                UpdateDual(dt, detailed);
                if (_evadeT >= 0f) UpdateEvade(dt, detailed);
                if (_blockT >= 0f) UpdateBlock(dt, detailed);
                // 22.10.: Treffer einstecken zuletzt – setzt auf die fertige Pose (Angriff, Haltung, Block) auf
                UpdateWirken(dt, detailed);   // 23.10.: Rumpf/Hüfte beim Zaubern (Clip bewegt nur den linken Arm)
                if (_reaktT >= 0f) UpdateReaktion(dt, detailed);
            }
            else
            {
                UpdateDual(dt, false);
                StopReaktion();   // Ausweichrolle hat den ganzen Körper
            }
            AnimatorActions.SetProcedural(_anim, upper, full);
        }

        // ================================================================== Angriffe

        private Move _move;
        private bool _running, _mirror;
        private float _t, _wu, _ac, _re, _tail = -1f;
        private MoveKey[] _main, _second;
        private HandKey[] _free;
        private GripStyle _style;
        private float _ulnar;
        private float _curTwist, _curLean, _curSide;
        private bool _twoHanded;

        private void OnAttackStarted(AttackStep step, bool special)
        {
            if (!EnsureBones() || _weapons == null) return;
            WeaponDefinition w = _weapons.ActiveWeapon;
            AttackKind kind = _weapons.CurrentKind;
            if (!Handles(w, kind)) return;
            StartMoveFor(w, kind, _weapons.CurrentComboIndex, step);
        }

        /// <summary>
        /// P1 Klassenfaehigkeiten: eine Bewegung aus dem Satz der Waffe in der Hand abspielen (Heldenhafter Schlag, Schildhieb,
        /// Glutbolzen …), unabhaengig vom automatischen Angriff. Bogen nur, wenn er gerade in der Hand ist.
        /// </summary>
        public void PlayAbility(bool special, int combo, float windup, float active, float recovery)
        {
            if (!GlobalEnabled || !EnsureBones() || _weapons == null || _dodgeT >= 0f) return;
            WeaponDefinition w = _weapons.ActiveWeapon;
            if (w == null) return;
            AttackKind kind = special ? AttackKind.Special : AttackKind.Combo;
            if (!Handles(w, kind)) return;
            StartMoveFor(w, kind, combo, AttackStep.Create("Faehigkeit", 1f, windup, active, recovery));
        }

        private void StartMoveFor(WeaponDefinition w, AttackKind kind, int comboIndex, AttackStep step)
        {
            GripStyle style = WeaponPoses.StyleOf(w.weaponClass, w.HeldLikeMelee, false);
            _u = _model.up; _f = _model.forward; _rt = _model.right;
            if (style == GripStyle.Bow) { StartBowShot(step, kind == AttackKind.Special); return; }
            bool dual = _weapons.IsDualWielding && (style == GripStyle.OneHand || style == GripStyle.Dagger) && OffhandInHand;
            Move move = MoveLibrary.For(style, kind == AttackKind.Special, comboIndex, dual, _orch != null && _orch.Context.Offhand == OffhandRole.Shield);
            if (move == null) return;
            // Nebenhand-Hieb ohne gezogene zweite Waffe: mit der Haupthand spiegeln waere falsch – dann den Haupthand-Hieb
            if (move.Mirror && !dual) move = move.Mirrored();

            _move = move;
            _style = style;
            _mirror = move.Mirror;
            _ulnar = move.Ulnar;
            _twoHanded = style == GripStyle.TwoHandBlade || style == GripStyle.TwoHandHeavy || style == GripStyle.Polearm;
            _wu = Mathf.Max(0.05f, step.windup);
            _ac = Mathf.Max(0.04f, step.active);
            _re = Mathf.Max(0.15f, step.recovery);
            _t = 0f;
            _tail = -1f;
            CurrentMove = move.Name;

            MotionContext c = _orch != null ? _orch.Context : default;
            WeaponStance ws = WeaponPoses.Guard(style, c.Offhand);
            MoveKey guard = GuardKey(ws.right, false);

            ArmRig strike = _mirror ? _hands.LeftArm : _hands.RightArm;
            // Fremde Figur greift an, bevor ihr Arm vermessen ist (Lasttest 02.10.: NullReference im Netz-Empfang) → nur Darstellung auslassen
            if (strike == null || !strike.Valid || strike.Hand == null) { _move = null; _running = false; CurrentMove = ""; return; }
            MoveKey[] keys = move.Main;
            _main = Build(strike, _mirror ? Mirror(keys) : keys, _mirror ? guard.Mirrored() : guard, true);
            _second = move.Second != null && dual && _hands.LeftArm != null && _hands.LeftArm.Valid && _hands.LeftArm.Hand != null
                ? Build(_hands.LeftArm, move.Second, guard.Mirrored(), false) : null;
            _free = move.Free != null ? BuildFree(move.Free, ws) : null;
            _running = true;
        }

        private static MoveKey[] Mirror(MoveKey[] src)
        {
            var r = new MoveKey[src.Length];
            for (int i = 0; i < src.Length; i++) r[i] = src[i].Mirrored();
            return r;
        }

        /// <summary>Zielpose als Schluessel: Faustmitte, Knoechel, Handrichtung aus einer Haltung.</summary>
        private static MoveKey GuardKey(in HandPose hp, bool left)
        {
            Vector3 k = WeaponPoses.KnucklesOf(hp.dir, hp.palm, left);
            return new MoveKey(3f, hp.pos, k, hp.dir);
        }

        /// <summary>Schluesselfolge: aktuelle Pose (s = 0) + Bewegung + Kampfhaltung (s = 3).</summary>
        private MoveKey[] Build(ArmRig arm, MoveKey[] keys, MoveKey guard, bool torso)
        {
            var r = new MoveKey[keys.Length + 2];
            r[0] = new MoveKey(0f, ToBody(arm.Center), ToBodyDir(arm.Knuckles), ToBodyDir(arm.Direction),
                torso ? _curTwist : 0f, torso ? _curLean : 0f, torso ? _curSide : 0f, _flip);
            for (int i = 0; i < keys.Length; i++) r[i + 1] = keys[i];
            guard.s = 3f;
            r[r.Length - 1] = guard;
            return r;
        }

        private HandKey[] BuildFree(HandKey[] keys, in WeaponStance ws)
        {
            ArmRig arm = _hands.LeftArm;
            var r = new HandKey[keys.Length + 2];
            r[0] = new HandKey(0f, ToBody(arm.Center), ToBodyDir(arm.Direction), ToBodyDir(arm.Palm), HandShape.Relaxed);
            for (int i = 0; i < keys.Length; i++) r[i + 1] = keys[i];
            HandPose end = ws.left == OffhandRole.Focus ? ws.leftHand : ws.leftRest;
            r[r.Length - 1] = new HandKey(3f, end.pos, end.dir, end.palm, end.shape);
            return r;
        }

        /// <summary>
        /// Spielzeit → Bewegungszeit s. 21.10. abends (Max: „die Geschwindigkeit der Angriffe sieht nicht richtig aus“): vorher lief
        /// der ganze Schlag samt Nachlauf (s 1→2) im Trefferfenster (Hieb: 0,08 s) und die Waffe trieb danach 0,3 s gleichmaessig in
        /// die Deckung – ein Zucken mit langem Schweben. Die Spielzeiten bleiben (MOV-064 „Gameplayzeiten gewinnen“); nur die
        /// Bewegung wird verteilt, nach Messungen am Langschwert (100 fps: Beschleunigung auf den ersten 30–40°, um den Treffer fast
        /// gleichmaessig schnell, Bremsen erst im Nachlauf; SourceArt/Referenz/Angriffe/RECHERCHE_ANGRIFFE.md):
        ///   Ausholen weich (s 0→1), Schlag beschleunigt bis zum Treffer (s 1→1,45 = Mitte des Trefferfensters), Nachlauf bremst
        ///   ab (s 1,45→2, ~40 % der Erholung), Rueckkehr weich in die Deckung (s 2→3).
        /// </summary>
        /// <summary>Ende der Bewegung: nach dem Treffer Nachlauf + 0,28 s Rueckkehr, auch wenn die Spiel-Erholung kuerzer ist (ein neuer
        /// Angriff uebernimmt aus der aktuellen Pose, s = 0).</summary>
        private float VisualEnd()
        {
            float hit = _wu + 0.5f * _ac;
            float follow = Mathf.Clamp(0.4f * (_wu + _ac + _re - hit), 0.1f, 0.24f);
            return Mathf.Max(_wu + _ac + _re, hit + follow + 0.28f);
        }

        private float PhaseS(float t)
        {
            float hit = _wu + 0.5f * _ac;
            float strike = Mathf.Min(0.07f, 0.6f * hit);          // gespannt → Kontakt (2 Bilder bei 30 fps)
            float cocked = hit - strike;
            float follow = Mathf.Clamp(0.4f * (_wu + _ac + _re - hit), 0.1f, 0.24f);
            float end = VisualEnd();
            if (t < cocked) return MotionMath.SmoothStep01(t / cocked);
            if (t < hit) { float u = (t - cocked) / strike; return 1f + 0.45f * Mathf.Pow(u, 1.3f); }
            if (t < hit + follow) { float u = (t - hit) / follow; return 1.45f + 0.55f * (1f - (1f - u) * (1f - u)); }
            return 2f + MotionMath.SmoothStep01(Mathf.Clamp01((t - hit - follow) / Mathf.Max(0.05f, end - hit - follow)));
        }

        private bool UpdateAttack(float dt, bool detailed)
        {
            if (_running)
            {
                _t += dt;
                if (_t >= VisualEnd()) { _running = false; _tail = 0f; }
            }
            if (!_running && _tail < 0f) return false;
            if (!_running)
            {
                // Ausklang: die Kampfhaltung der Waffenhand uebernimmt, wir blenden gleichzeitig aus
                _tail += dt;
                float w = (1f - _hands.GuardWeight) * (1f - Mathf.Clamp01(_tail / 0.35f));
                if (_tail > 0.35f || w <= 0.001f) { _tail = -1f; _curTwist = _curLean = _curSide = 0f; ResetFlip(); CurrentMove = ""; return false; }
                if (!detailed) return false;
                ArmRig arm = _mirror ? _hands.LeftArm : _hands.RightArm;
                MoveKey g = _main[_main.Length - 1];
                PlaceGrip(arm, g, _mirror, w);
                if (_twoHanded) _hands.RefitOffhand();
                return false;
            }
            if (!detailed) return true;

            float s = PhaseS(_t);
            MoveKey m = Eval(_main, s);
            _curTwist = m.twist; _curLean = m.lean; _curSide = m.side;
            // 21.10. nachts (Max: „Angriffe realistisch mit Koerpereinsatz – aus der Huefte raus, im Moment nur mit den Armen“):
            // die Huefte fuehrt – sie nimmt die Haelfte der Drehung, verlagert das Gewicht in den Schlag (nach vorn, tiefer in die
            // Knie) und der hintere Fuss dreht auf dem Ballen mit; die Fuesse bleiben stehen (Bein-IK), der Rumpf folgt.
            float hipTwist = BodyDrive(m.twist, m.lean, m.side);
            Torso(m.twist - hipTwist, m.lean * 0.7f, m.side * 0.6f, 1f);
            if (_neck != null) Rot(_neck, -hipTwist * 0.45f, 0f, 0f); else if (_head != null) Rot(_head, -hipTwist * 0.45f, 0f, 0f);
            ArmRig strike = _mirror ? _hands.LeftArm : _hands.RightArm;
            ApplyFlip(m.flip, strike);
            PlaceGrip(strike, m, _mirror, 1f);
            if (_twoHanded) _hands.RefitOffhand();
            if (_second != null) PlaceGrip(_hands.LeftArm, Eval(_second, s), true, 1f);
            if (_mirror)
            {
                // Nebenhand schlaegt: die Haupthand bleibt in Deckung
                MotionContext c = _orch != null ? _orch.Context : default;
                PlaceGrip(_hands.RightArm, GuardKey(WeaponPoses.Guard(_style, c.Offhand).right, false), false, 1f);
            }
            if (_free != null)
            {
                HandKey h = EvalFree(_free, s);
                PlaceFree(_hands.LeftArm, h, 1f, dt);
            }
            return true;
        }

        private void PlaceGrip(ArmRig arm, in MoveKey key, bool left, float weight)
        {
            if (arm == null || !arm.Valid || weight <= 0.0001f) return;
            Vector3 k = M(key.k).normalized;
            Vector3 dir = Vector3.ProjectOnPlane(M(key.edge), k);
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            Vector3 palm = left ? Vector3.Cross(k, dir) : Vector3.Cross(dir, k);
            Vector3 p = FromBody(key.pos);
            // 22.10. nachts (Angriffsfilm Zweihänder: im Schlag lag der Griff ausserhalb der Reichweite der linken Hand – sie liess los
            // und hing als Faust vor der Brust, der Zweihänder wurde einhändig geschwungen): die Faust rückt so weit zur linken
            // Schulter, dass die zweite Hand am Griff bleibt – ein Zweihänder wird immer mit beiden Händen geführt.
            if (_twoHanded && !left && _hands.LeftArm != null && _hands.LeftArm.Valid)
            {
                MotionContext c = _orch != null ? _orch.Context : default;
                Vector3 lp = p + k * WeaponPoses.Guard(_style, c.Offhand).leftOnWeapon;
                Vector3 d = lp - _hands.LeftArm.Upper.position;
                float over = d.magnitude - _hands.LeftArm.Reach * 0.9f;
                if (over > 0f) p -= d.normalized * over;
            }
            arm.Place(p, Quaternion.LookRotation(k, palm), _ulnar, 0.5f, weight, 50f);
            arm.ClampTwist(100f);
            arm.ClampWrist(60f, 1f);
        }

        private void PlaceFree(ArmRig arm, in HandKey h, float weight, float dt)
        {
            if (arm == null || !arm.Valid || weight <= 0.0001f) return;
            Vector3 dir = M(h.dir).normalized, palm = Vector3.ProjectOnPlane(M(h.palm), dir);
            if (palm.sqrMagnitude < 1e-6f) return;
            palm.Normalize();
            Vector3 k = WeaponPoses.KnucklesOf(dir, palm, arm.Left);
            arm.Place(FromBody(h.pos), Quaternion.LookRotation(k, palm), 0f, 0.5f, weight, 50f);
            arm.ClampTwist(95f);
            arm.ClampWrist(55f, 1f);
            arm.Shape(h.shape, 1f, dt);
        }

        // ------------------------------------------------------------------ Interpolation (Catmull-Rom ueber die Schluessel)

        private static MoveKey Eval(MoveKey[] keys, float s)
        {
            int n = keys.Length;
            int i = 0;
            while (i < n - 2 && s > keys[i + 1].s) i++;
            MoveKey a = keys[Mathf.Max(0, i - 1)], b = keys[i], c = keys[i + 1], d = keys[Mathf.Min(n - 1, i + 2)];
            float u = Mathf.Clamp01((s - b.s) / Mathf.Max(1e-4f, c.s - b.s));
            var r = new MoveKey
            {
                s = s,
                pos = CR(a.pos, b.pos, c.pos, d.pos, u),
                k = CR(a.k, b.k, c.k, d.k, u).normalized,
                edge = CR(a.edge, b.edge, c.edge, d.edge, u).normalized,
                twist = CR(a.twist, b.twist, c.twist, d.twist, u),
                lean = CR(a.lean, b.lean, c.lean, d.lean, u),
                side = CR(a.side, b.side, c.side, d.side, u),
                flip = Mathf.Clamp01(Mathf.Lerp(b.flip, c.flip, MotionMath.SmoothStep01(u)))
            };
            if (r.k.sqrMagnitude < 1e-6f) r.k = c.k;
            if (r.edge.sqrMagnitude < 1e-6f) r.edge = c.edge;
            return r;
        }

        private static HandKey EvalFree(HandKey[] keys, float s)
        {
            int n = keys.Length;
            int i = 0;
            while (i < n - 2 && s > keys[i + 1].s) i++;
            HandKey a = keys[Mathf.Max(0, i - 1)], b = keys[i], c = keys[i + 1], d = keys[Mathf.Min(n - 1, i + 2)];
            float u = Mathf.Clamp01((s - b.s) / Mathf.Max(1e-4f, c.s - b.s));
            return new HandKey
            {
                s = s,
                pos = CR(a.pos, b.pos, c.pos, d.pos, u),
                dir = CR(a.dir, b.dir, c.dir, d.dir, u).normalized,
                palm = CR(a.palm, b.palm, c.palm, d.palm, u).normalized,
                shape = u < 0.5f ? b.shape : c.shape
            };
        }

        private static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static float CR(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // ------------------------------------------------------------------ Rumpf

        /// <summary>Rumpf drehen/neigen, anteilig ueber Lende, Brust, obere Brust; Kopf haelt den Blick.</summary>
        /// <summary>
        /// Ganzkoerper-Antrieb eines Schlags: Becken dreht (Anteil der Drehung), verschiebt sich in Schlagrichtung und sinkt in die
        /// Knie; beide Fuesse bleiben an ihrem Platz (Zweigelenk-IK, Knie nach vorn-aussen), der vordere Fuss behaelt seine
        /// Richtung, der hintere dreht auf dem Ballen mit der Huefte. Gibt die Beckendrehung zurueck (der Rumpf nimmt den Rest).
        /// </summary>
        private float BodyDrive(float twist, float lean, float side)
        {
            if (_hips == null || _lFoot == null || _rFoot == null || _lUpLeg == null || _rUpLeg == null) return 0f;
            float hipTwist = Mathf.Clamp(twist * 0.5f, -24f, 24f);
            Vector3 fl = _lFoot.position, fr = _rFoot.position;
            Quaternion flr = _lFoot.rotation, frr = _rFoot.rotation;
            // vorderer Fuss = der weiter vorn stehende (Kampfstand: meist links)
            bool leftFront = Vector3.Dot(fl - fr, _f) >= 0f;
            float fwd = Mathf.Clamp(lean, -12f, 20f) * 0.0045f;                  // 20° Neigung → 9 cm Gewicht nach vorn
            float drop = Mathf.Max(0f, lean) * 0.0022f + Mathf.Abs(hipTwist) * 0.0008f;   // in die Knie
            _hips.position += _f * fwd - _u * drop + _rt * (side * 0.003f);
            _hips.rotation = Quaternion.AngleAxis(hipTwist, _u) * _hips.rotation;
            Vector3 lHint = _f - _rt * 0.3f, rHint = _f + _rt * 0.3f;
            LimbIK.TwoBone(_lUpLeg, _lLeg, _lFoot, fl, lHint, 1f);
            LimbIK.TwoBone(_rUpLeg, _rLeg, _rFoot, fr, rHint, 1f);
            Quaternion pivot = Quaternion.AngleAxis(hipTwist * 0.4f, _u);   // 0,8 liess den Ballen 5–6 cm gleiten (Drehung ums Sprunggelenk)
            _lFoot.rotation = leftFront ? flr : pivot * flr;
            _rFoot.rotation = leftFront ? pivot * frr : frr;
            return hipTwist;
        }

        private void Torso(float twist, float lean, float side, float w)
        {
            if (w <= 0.0001f || (Mathf.Abs(twist) + Mathf.Abs(lean) + Mathf.Abs(side)) < 0.01f) return;
            twist *= w; lean *= w; side *= w;
            bool split = _upperChest != null && _upperChest != _chest;
            Rot(_spine, twist * 0.3f, lean * 0.4f, side * 0.35f);
            Rot(_chest, twist * (split ? 0.35f : 0.7f), lean * (split ? 0.3f : 0.6f), side * (split ? 0.35f : 0.65f));
            if (split) Rot(_upperChest, twist * 0.35f, lean * 0.3f, side * 0.3f);
            // Blick bleibt beim Ziel: Hals nimmt einen Teil der Drehung/Neigung zurueck
            if (_neck != null) Rot(_neck, -twist * 0.45f, -lean * 0.4f, -side * 0.5f);
            else if (_head != null) Rot(_head, -twist * 0.45f, -lean * 0.4f, -side * 0.5f);
        }

        private void Rot(Transform b, float twist, float lean, float side)
        {
            if (b == null) return;
            b.rotation = Quaternion.AngleAxis(twist, _u) * Quaternion.AngleAxis(lean, _rt) * Quaternion.AngleAxis(-side, _f) * b.rotation;
        }

        // ------------------------------------------------------------------ Dolch im Umgriff

        private float _flip;
        private Transform _flipPivot;
        private Vector3 _flipBasePos;
        private Quaternion _flipBaseRot;

        /// <summary>Dolch in der Faust um die Handrichtung drehen (Klinge an der Kleinfingerseite) – und zurueck.</summary>
        private void ApplyFlip(float flip, ArmRig arm)
        {
            Transform pivot = _mirror ? _offModel : _hands.Pivot;
            if (flip <= 0.001f)
            {
                if (_flipPivot != null) ResetFlip();
                _flip = 0f;
                return;
            }
            if (pivot == null || arm == null || pivot.parent != arm.Hand) { _flip = 0f; return; }
            if (_flipPivot != pivot)
            {
                ResetFlip();
                _flipPivot = pivot;
                _flipBasePos = pivot.localPosition;
                _flipBaseRot = pivot.localRotation;
            }
            float inv = 1f / Mathf.Max(1e-4f, arm.Hand.lossyScale.x);
            Vector3 c = arm.CenterLocal * inv;
            Vector3 dirLocal = arm.FrameLocal * (arm.Left ? Vector3.right : Vector3.left);
            Quaternion q = Quaternion.AngleAxis(180f * MotionMath.SmoothStep01(flip), dirLocal);
            pivot.localRotation = q * _flipBaseRot;
            pivot.localPosition = c + q * (_flipBasePos - c);
            _flip = flip;
        }

        private void ResetFlip()
        {
            if (_flipPivot != null)
            {
                _flipPivot.localPosition = _flipBasePos;
                _flipPivot.localRotation = _flipBaseRot;
            }
            _flipPivot = null;
            _flip = 0f;
        }

        /// <summary>Lab/Vorschau: passives Ausweichen zeigen (Schlag kam aus <paramref name="attackerDir"/>).</summary>
        public void PlayEvade(Vector3 attackerDir)
        {
            var info = new DamageInfo { Dodged = true, Direction = -attackerDir };
            StartEvade(info);
        }

        /// <summary>Lab/Vorschau: Schildblock zeigen.</summary>
        public void PlayBlock()
        {
            _blockT = 0f;
            SfxPlayer.Play(SfxId.ShieldBlock, transform.position + Vector3.up * 1.2f, 0.9f, 0.06f);
        }

        // ================================================================== Schildblock (automatisch mit Chance, wie WoW)

        private float _blockT = -1f;

        private void OnDamaged(DamageInfo info)
        {
            if (!GlobalEnabled) return;
            // 22.10.: genau eine ausgewählte Reaktion je Treffer – Ausweichen, Schildblock (voll/teilweise) oder Treffer einstecken
            if (info.Dodged) { StopReaktion(); _blockT = -1f; StartEvade(info); return; }
            if (_health != null && !_health.IsAlive) return;   // tödlich: Todeszustand hat Vorrang
            MotionContext c = _orch != null ? _orch.Context : default;
            if (info.Blocked > 0f && c.Offhand == OffhandRole.Shield)
            {
                _evadeT = -1f;
                _blockT = 0f;
                SfxPlayer.Play(SfxId.ShieldBlock, info.HitPoint != Vector3.zero ? info.HitPoint : transform.position + Vector3.up * 1.2f, 0.9f, 0.06f);
                // Teilblock: Restschaden als gedämpfter Körperimpuls in derselben Bewegung (eine kombinierte Pose)
                if (info.Amount > 0f) { if (EnsureBones()) { _u = _model.up; _f = _model.forward; _rt = _model.right; StartReaktion(Reaktion.Leicht, info.Direction, VarianteAus(info.HitPoint), 0.5f); } }
                else StopReaktion();
                return;
            }
            if (!EnsureBones()) return;
            _u = _model.up; _f = _model.forward; _rt = _model.right;
            TrefferReaktion(info);
        }

        /// <summary>Schild stoesst dem Schlag entgegen (schnell hoch, kurz halten, weich zurueck), Rumpf stemmt sich dagegen.</summary>
        private void UpdateBlock(float dt, bool detailed)
        {
            const float dur = 0.42f;
            _blockT += dt;
            if (_blockT >= dur) { _blockT = -1f; return; }
            float p = _blockT / dur;
            float bump = p < 0.18f ? MotionMath.SmoothStep01(p / 0.18f) : 1f - MotionMath.SmoothStep01((p - 0.35f) / 0.65f);
            bump = Mathf.Clamp01(bump);
            // 22.10. (Codex Kampfreaktionen): entfernte Figuren (LOD 1) zeigen den Stoß am Rumpf statt gar nichts
            if (!detailed) { Torso(-4f * bump, -7f * bump, 0f, 1f); return; }
            MotionContext c = _orch != null ? _orch.Context : default;
            WeaponStance ws = WeaponPoses.Guard(_hands.Style, OffhandRole.Shield);
            HandPose sp = ws.leftHand;
            // Schild nach vorn-oben gegen den Schlag, Unterarm quer, Ellbogen fest
            Vector3 pos = sp.pos + new Vector3(0.03f, 0.1f, 0.1f);
            Torso(-4f * bump, -7f * bump, 0f, 1f);
            ArmRig arm = _hands.LeftArm;
            Vector3 dir = M(sp.dir).normalized, palm = Vector3.ProjectOnPlane(M(sp.palm), dir).normalized;
            Vector3 k = WeaponPoses.KnucklesOf(dir, palm, true);
            arm.Place(FromBody(pos), Quaternion.LookRotation(k, palm), 0f, 1f, bump, 50f);
            arm.ClampTwist(95f);
            arm.ClampWrist(32f, 1f);   // 22.10. (Codex): mit 50 maß das fertige Bild 62° (Frau 68°) – Inertialisierung blendet darüber hinaus
            arm.Shape(HandShape.Grip, 1f, dt);
            if (_hands.RightArm.Valid && c.Stance == MotionStance.Combat) { /* Waffenhand bleibt in ihrer Haltung */ }
        }
    }
}
