using Ashveil.Data;
using Ashveil.Data.Motion;
using Ashveil.Gameplay.Audio;
using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>
    /// Beidhaendig (Aschenjaeger: zwei Dolche / zwei Schwerter): die zweite Waffe steckt gespiegelt an der anderen Huefte,
    /// wird kurz nach der Haupthand-Waffe mit der linken Hand gezogen (greifen – herausziehen – in die Deckung schwingen),
    /// in der linken Faust nach vermessenem Griff gehalten und vor der Haupthand wieder weggesteckt.
    /// </summary>
    public sealed partial class CombatChoreographer
    {
        private enum OffPhase { Idle, Sheathed, Drawing, Held, Sheathing }
        private OffPhase _offPhase = OffPhase.Idle;
        private float _offT;
        private Transform _offModel;
        private Transform _offSheathParent;
        private Vector3 _offSheathPos, _offSheathScale;
        private Quaternion _offSheathRot;
        private GripInfo _offGrip;
        private Transform _offGripModel;
        private Vector3 _offStartPos, _offStartDir, _offStartPalm;
        private float _offSettle = 1f;
        private Vector3 _offSettleFromPos;
        private Quaternion _offSettleFromRot;

        /// <summary>Zweite Waffe liegt in der linken Hand (Beidhaendig-Angriffe nur dann).</summary>
        public bool OffhandInHand => _offModel != null && _offPhase == OffPhase.Held && _offModel.parent == _hands.LeftArm.Hand;

        private void UpdateDual(float dt, bool detailed)
        {
            Transform model = _orch != null && _orch.Visuals != null ? _orch.Visuals.OffhandModel : null;
            GripStyle style = _hands.Style;
            bool dual = _weapons != null && _weapons.IsDualWielding && model != null &&
                        (style == GripStyle.OneHand || style == GripStyle.Dagger) && _hands.LeftArm.Valid;
            ArmRig arm = _hands.LeftArm;

            if (model != _offModel)
            {
                _offModel = model;
                _offPhase = OffPhase.Idle;
                _offSettle = 1f;
            }
            if (!dual)
            {
                // Nicht (mehr) beidhaendig: Waffe gehoert wieder dem Aussehen (steckt, wo es sie hinlegt)
                if (_offModel != null && _offSheathParent != null && _offModel.parent == arm.Hand) SheatheOffExact();
                _offPhase = OffPhase.Idle;
                return;
            }

            // Lage an der Huefte merken, solange das Aussehen sie dort ablegt (nicht waehrend unserer Bewegung)
            if (_offModel.parent != arm.Hand && _offPhase != OffPhase.Drawing && _offPhase != OffPhase.Sheathing)
            {
                _offSheathParent = _offModel.parent;
                _offSheathPos = _offModel.localPosition;
                _offSheathRot = _offModel.localRotation;
                _offSheathScale = _offModel.localScale;
                if (_offPhase == OffPhase.Held || _offPhase == OffPhase.Idle) _offPhase = OffPhase.Sheathed;
            }
            if (_offGripModel != _offModel)
            {
                _offGripModel = _offModel;
                _offGrip = WeaponGrip.Measure(_offModel, style, arm.HandWidth);
            }

            bool wantDrawn = _hands.WeaponInHand && _hands.DrawProgress > 0.55f;
            bool anim = detailed && _offGrip.Valid;
            switch (_offPhase)
            {
                case OffPhase.Sheathed:
                    if (wantDrawn)
                    {
                        if (!anim) { AttachOffExact(); _offPhase = OffPhase.Held; break; }
                        _offPhase = OffPhase.Drawing; _offT = 0f;
                        _offStartPos = ToBody(arm.Center); _offStartDir = ToBodyDir(arm.Direction); _offStartPalm = ToBodyDir(arm.Palm);
                    }
                    break;
                case OffPhase.Held:
                    if (_offModel.parent != arm.Hand) AttachOffExact();
                    if (!wantDrawn)
                    {
                        if (!anim) { SheatheOffExact(); _offPhase = OffPhase.Sheathed; break; }
                        _offPhase = OffPhase.Sheathing; _offT = 0f;
                        _offStartPos = ToBody(arm.Center); _offStartDir = ToBodyDir(arm.Direction); _offStartPalm = ToBodyDir(arm.Palm);
                    }
                    break;
            }

            SettleOff(dt);
            if (!detailed) return;
            MotionContext c = _orch != null ? _orch.Context : default;
            WeaponStance ws = WeaponPoses.Guard(style, OffhandRole.Free);
            MoveKey guard = GuardKey(ws.right, false).Mirrored();
            bool leftUsed = (_running && (_mirror || _second != null)) || (!_running && _tail >= 0f && _mirror);

            if (_offPhase == OffPhase.Drawing || _offPhase == OffPhase.Sheathing)
            {
                OffChoreo(arm, guard, dt);
                return;
            }
            if (_offPhase == OffPhase.Held && !leftUsed)
            {
                // Deckung mit der zweiten Waffe – gleichzeitig mit der Deckung der Haupthand (im Kampf), sonst locker gesenkt
                float g = c.Stance == MotionStance.Combat ? Mathf.Max(_hands.GuardWeight, _running ? 1f : 0f) : 0f;
                if (g > 0.001f) PlaceGripUlnar(arm, guard, true, g, 18f);
                arm.Shape(HandShape.Grip, 1f, dt);
            }
            else if (_offPhase == OffPhase.Held) arm.Shape(HandShape.Grip, 1f, dt);
        }

        private void PlaceGripUlnar(ArmRig arm, in MoveKey key, bool left, float weight, float ulnar)
        {
            float old = _ulnar;
            _ulnar = ulnar;
            PlaceGrip(arm, key, left, weight);
            _ulnar = old;
        }

        /// <summary>Linke Hand greift die zweite Waffe an der Huefte, zieht sie heraus und schwingt in die Deckung (bzw. umgekehrt).</summary>
        private void OffChoreo(ArmRig arm, in MoveKey guard, float dt)
        {
            const float dur = 0.55f, grasp = 0.38f, pulled = 0.6f;
            _offT += dt / dur;
            bool drawing = _offPhase == OffPhase.Drawing;
            float t = Mathf.Clamp01(_offT);
            if (_offSheathParent == null) { if (drawing) { AttachOffExact(); _offPhase = OffPhase.Held; } else { _offPhase = OffPhase.Sheathed; } return; }

            // Scheidenlage (Welt) und Handlage, die genau diesen Griff umschliesst
            Matrix4x4 sp = _offSheathParent.localToWorldMatrix;
            Vector3 sP = sp.MultiplyPoint3x4(_offSheathPos);
            Quaternion sR = _offSheathParent.rotation * _offSheathRot;
            OffHandFor(arm, sP, sR, out Vector3 graspP, out Quaternion graspR);
            Vector3 blade = sR * Vector3.forward;
            Vector3 pulledP = graspP - blade * 0.22f;

            Vector3 guardP = FromBody(guard.pos);
            Vector3 gk = M(guard.k).normalized;
            Vector3 gdir = Vector3.ProjectOnPlane(M(guard.edge), gk).normalized;
            Quaternion guardR = Quaternion.LookRotation(gk, Vector3.Cross(gk, gdir)) * Quaternion.Inverse(arm.FrameLocal);

            Vector3 hP; Quaternion hR;
            if (drawing)
            {
                if (t < grasp)
                {
                    float u = MotionMath.SmoothStep01(t / grasp);
                    Vector3 p0 = FromBody(_offStartPos);
                    Vector3 d0 = M(_offStartDir).normalized, pl0 = M(_offStartPalm).normalized;
                    Quaternion r0 = Quaternion.LookRotation(WeaponPoses.KnucklesOf(d0, pl0, true), pl0) * Quaternion.Inverse(arm.FrameLocal);
                    Vector3 ctrl = (p0 + graspP) * 0.5f + _f * 0.14f;
                    hP = Bez(p0, ctrl, graspP, u);
                    hR = Quaternion.Slerp(r0, graspR, u);
                }
                else
                {
                    if (_offModel.parent != arm.Hand) AttachOffAt(arm);
                    if (t < pulled)
                    {
                        float v = MotionMath.SmoothStep01((t - grasp) / (pulled - grasp));
                        hP = Vector3.Lerp(graspP, pulledP, v); hR = graspR;
                    }
                    else
                    {
                        float w = MotionMath.SmoothStep01((t - pulled) / (1f - pulled));
                        Vector3 ctrl = (pulledP + guardP) * 0.5f + _u * 0.16f + _f * 0.1f;
                        hP = Bez(pulledP, ctrl, guardP, w);
                        hR = Quaternion.Slerp(graspR, guardR, w);
                    }
                }
            }
            else
            {
                float back = 0.45f, release = 0.75f;
                if (t < back)
                {
                    float w = MotionMath.SmoothStep01(t / back);
                    Vector3 p0 = FromBody(_offStartPos);
                    Vector3 d0 = M(_offStartDir).normalized, pl0 = M(_offStartPalm).normalized;
                    Quaternion r0 = Quaternion.LookRotation(WeaponPoses.KnucklesOf(d0, pl0, true), pl0) * Quaternion.Inverse(arm.FrameLocal);
                    Vector3 ctrl = (p0 + pulledP) * 0.5f + _u * 0.14f + _f * 0.1f;
                    hP = Bez(p0, ctrl, pulledP, w);
                    hR = Quaternion.Slerp(r0, graspR, w);
                }
                else if (t < release)
                {
                    float v = MotionMath.SmoothStep01((t - back) / (release - back));
                    hP = Vector3.Lerp(pulledP, graspP, v); hR = graspR;
                }
                else
                {
                    if (_offModel.parent == arm.Hand) SheatheOffAt();
                    float k = 1f - MotionMath.SmoothStep01((t - release) / (1f - release));
                    arm.PlaceBone(graspP + _u * 0.03f, graspR, 0f, 0.5f, k, 60f);
                    if (_offT >= 1f) _offPhase = OffPhase.Sheathed;
                    return;
                }
            }
            arm.PlaceBone(hP, hR, 0f, 0.5f, 1f, 60f);
            arm.ClampTwist(100f);
            arm.ClampWrist(60f, 1f);
            arm.Shape(drawing ? (t < grasp * 0.7f ? HandShape.Open : HandShape.Grip) : (t < 0.75f ? HandShape.Grip : HandShape.Relaxed), 1f, dt);
            if (drawing && _offT >= 1f) { _offPhase = OffPhase.Held; SfxPlayer.Play(SfxId.WeaponDraw, arm.Center, 0.35f); }
        }

        private static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float it = 1f - t;
            return it * it * a + 2f * it * t * b + t * t * c;
        }

        /// <summary>Handknochen-Lage, in der die linke Faust den Griff der zweiten Waffe (Weltlage) umschliesst.</summary>
        private void OffHandFor(ArmRig arm, Vector3 wP, Quaternion wR, out Vector3 hP, out Quaternion hR)
        {
            OffGripLocal(arm, out Vector3 lp, out Quaternion lr);
            float s = arm.Hand.lossyScale.x;
            hR = wR * Quaternion.Inverse(lr);
            hP = wP - hR * (lp * s);
        }

        /// <summary>Lage der Waffe relativ zur linken Hand (Handknochen-Raum, Handknochen-Einheiten):
        /// Knoechel entlang der Klinge, Schneide in Handrichtung (fuer die linke Hand um 180° um die Klinge gedreht).</summary>
        private void OffGripLocal(ArmRig arm, out Vector3 pos, out Quaternion rot)
        {
            // Rahmen im Modellraum: Knoechel +Z (Klinge), Handflaeche –X (Schneide +Y zeigt dann in die Handrichtung der linken Hand)
            Quaternion g = Quaternion.LookRotation(Vector3.forward, Vector3.left);
            rot = arm.FrameLocal * Quaternion.Inverse(g);
            float inv = 1f / Mathf.Max(1e-4f, arm.Hand.lossyScale.x);
            Vector3 grip = _offGrip.Valid ? _offGrip.Right : Vector3.zero;
            pos = arm.CenterLocal * inv - rot * grip;
        }

        private void AttachOffExact()
        {
            ArmRig arm = _hands.LeftArm;
            if (_offModel == null || !arm.Valid) return;
            OffGripLocal(arm, out Vector3 lp, out Quaternion lr);
            _offModel.SetParent(arm.Hand, false);
            _offModel.localRotation = lr;
            _offModel.localPosition = lp;
            _offModel.localScale = Vector3.one / Mathf.Max(1e-4f, arm.Hand.lossyScale.x);
            _offSettle = 1f;
        }

        /// <summary>In die Hand, Weltlage bleibt – dann in ~0,15 s in den exakten Griff.</summary>
        private void AttachOffAt(ArmRig arm)
        {
            Vector3 wp = _offModel.position; Quaternion wr = _offModel.rotation;
            AttachOffExact();
            _offSettleFromPos = arm.Hand.InverseTransformPoint(wp);
            _offSettleFromRot = Quaternion.Inverse(arm.Hand.rotation) * wr;
            _offSettleToPos = _offModel.localPosition; _offSettleToRot = _offModel.localRotation;
            _offModel.localPosition = _offSettleFromPos; _offModel.localRotation = _offSettleFromRot;
            _offSettle = 0f;
        }

        private Vector3 _offSettleToPos;
        private Quaternion _offSettleToRot;

        private void SettleOff(float dt)
        {
            if (_offSettle >= 1f || _offModel == null) return;
            _offSettle = Mathf.Min(1f, _offSettle + dt / 0.15f);
            float k = MotionMath.SmoothStep01(_offSettle);
            _offModel.localPosition = Vector3.Lerp(_offSettleFromPos, _offSettleToPos, k);
            _offModel.localRotation = Quaternion.Slerp(_offSettleFromRot, _offSettleToRot, k);
        }

        private void SheatheOffExact()
        {
            if (_offModel == null || _offSheathParent == null) return;
            _offModel.SetParent(_offSheathParent, false);
            _offModel.localPosition = _offSheathPos;
            _offModel.localRotation = _offSheathRot;
            _offModel.localScale = _offSheathScale;
            _offSettle = 1f;
        }

        private void SheatheOffAt()
        {
            Vector3 wp = _offModel.position; Quaternion wr = _offModel.rotation;
            SheatheOffExact();
            _offSettleFromPos = _offSheathParent.InverseTransformPoint(wp);
            _offSettleFromRot = Quaternion.Inverse(_offSheathParent.rotation) * wr;
            _offSettleToPos = _offSheathPos; _offSettleToRot = _offSheathRot;
            _offModel.localPosition = _offSettleFromPos; _offModel.localRotation = _offSettleFromRot;
            _offSettle = 0f;
        }
    }
}
