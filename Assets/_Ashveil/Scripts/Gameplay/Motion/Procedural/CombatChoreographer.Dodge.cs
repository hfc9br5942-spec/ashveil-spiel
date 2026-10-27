using Ashveil.Data;
using Ashveil.Gameplay.Audio;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>
    /// Ausweichen (Max 30.09.):
    ///   Ausweichrolle  Faehigkeit des Aschenjaegers – immer dieselbe Hechtrolle ueber die Schulterlinie: Knie an die
    ///                  Brust, Haende an die Schienbeine, Wirbelsaeule rund, in einer Bewegung wieder auf die Fuesse.
    ///   Ausweichen im Kampf (passiv, WoW Classic, alle Klassen): weicht die Figur einem Nahkampfschlag aus, zeigt sie
    ///                  einen kurzen Seitschritt mit Wegdrehen des Oberkoerpers – auf jede laufende Haltung oder jeden
    ///                  Angriff aufgesetzt, die Fuesse bleiben stehen.
    /// </summary>
    public sealed partial class CombatChoreographer
    {
        private float _dodgeT = -1f, _dodgeDur = 0.5f;
        private bool _dodgeFull;
        private Vector3 _dodgeDir;
        private Quaternion _hipsRel = Quaternion.identity;
        private float _hipsHeight = 0.95f;

        /// <summary>Welche Ausweichbewegung gerade laeuft (Lab-Bericht).</summary>
        public string DodgeName => _dodgeT >= 0f ? "Ausweichrolle" : _evadeT >= 0f ? "Ausweichen (passiv)" : "";

        private void OnDodged(Vector3 dir)
        {
            if (!GlobalEnabled || !EnsureBones()) return;
            _u = _model.up; _f = _model.forward; _rt = _model.right;
            dir.y = 0f;
            _dodgeDir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.ProjectOnPlane(_f, Vector3.up).normalized;
            _dodgeDur = 0.56f;
            _dodgeFull = true;
            Vector3 face = Vector3.ProjectOnPlane(_f, Vector3.up);
            if (face.sqrMagnitude < 1e-4f) face = _dodgeDir;
            _hipsRel = Quaternion.Inverse(Quaternion.LookRotation(face.normalized, Vector3.up)) * _hips.rotation;
            _hipsHeight = Mathf.Clamp(Vector3.Dot(_hips.position - _model.position, Vector3.up), 0.6f, 1.3f);
            _dodgeT = 0f;
            // Angefangener Schlag endet mit dem Ausweichen
            _running = false; _tail = -1f; _curTwist = _curLean = _curSide = 0f;
            ResetFlip();
            SfxPlayer.Play(SfxId.Dodge, transform.position + Vector3.up, 0.7f, 0.08f);
        }

        /// <summary>true, solange die Ausweichbewegung laeuft.</summary>
        private bool UpdateDodge(float dt, bool detailed)
        {
            _dodgeT += dt;
            if (_dodgeT >= _dodgeDur) { _dodgeT = -1f; return false; }
            if (!detailed) return true;
            float p = Mathf.Clamp01(_dodgeT / _dodgeDur);
            float wIn = MotionMath.SmoothStep01(p / 0.1f) * (1f - MotionMath.SmoothStep01((p - 0.86f) / 0.14f));
            Roll(p, wIn);
            return true;
        }

        // ------------------------------------------------------------------ Rolle

        private void Roll(float p, float wIn)
        {
            Vector3 up = Vector3.up, d = _dodgeDir;
            Vector3 r = Vector3.Cross(up, d).normalized;
            Quaternion face = Quaternion.LookRotation(d, up);
            // Drehung um die Querachse: langsam abtauchen, schnell rollen, weich aufrichten
            float theta = 360f * (p * p * (3f - 2f * p));
            float c = Mathf.Pow(Mathf.Sin(Mathf.PI * p), 0.6f);
            Quaternion b = Quaternion.AngleAxis(theta, r) * face;
            Vector3 ground = _model.position;
            Vector3 center = ground + up * 0.5f;
            Vector3 stand = ground + up * _hipsHeight;
            Vector3 ball = center + b * new Vector3(0f, -0.06f, -0.14f);
            Vector3 hipsPos = Vector3.Lerp(stand, ball, c);
            _hips.SetPositionAndRotation(Vector3.Lerp(_hips.position, hipsPos, wIn), Quaternion.Slerp(_hips.rotation, b * _hipsRel, wIn));

            // Wirbelsaeule rund, Kinn zur Brust
            float curl = c * wIn;
            Curl(_spine, 20f * curl, r); Curl(_chest, 18f * curl, r);
            if (_upperChest != null && _upperChest != _chest) Curl(_upperChest, 14f * curl, r);
            Curl(_neck, 16f * curl, r); Curl(_head, 10f * curl, r);

            // Knie an die Brust (Scharnier-IK), Unterschenkel dicht an den Oberschenkeln
            float hipFlex = 118f * c, kneeFlex = 140f * c;
            Vector3 thigh = b * new Vector3(0f, -Mathf.Cos(hipFlex * Mathf.Deg2Rad), Mathf.Sin(hipFlex * Mathf.Deg2Rad));
            float sh = (hipFlex - kneeFlex) * Mathf.Deg2Rad;
            Vector3 shin = b * new Vector3(0f, -Mathf.Cos(sh), Mathf.Sin(sh));
            float lw = wIn * Mathf.Clamp01(c * 1.6f);
            Leg(_lUpLeg, _lLeg, _lFoot, thigh, shin, -r * 0.03f, thigh - r * 0.15f, lw);
            Leg(_rUpLeg, _rLeg, _rFoot, thigh, shin, r * 0.03f, thigh + r * 0.15f, lw);

            // Haende fassen die Schienbeine
            ArmRig la = _hands.LeftArm, ra = _hands.RightArm;
            if (la.Valid && _lLeg != null) la.ReachTo(_lLeg.position + shin * 0.12f - r * 0.07f, b * new Vector3(-1f, 0f, -0.6f), lw);
            if (ra.Valid && _rLeg != null) ra.ReachTo(_rLeg.position + shin * 0.12f + r * 0.07f, b * new Vector3(1f, 0f, -0.6f), lw);
        }

        private static void Curl(Transform bone, float deg, Vector3 axis)
        {
            if (bone == null || Mathf.Abs(deg) < 0.01f) return;
            bone.rotation = Quaternion.AngleAxis(deg, axis) * bone.rotation;
        }

        /// <summary>Bein ueber Oberschenkel- und Schienbeinrichtung setzen (Hueftgelenk bleibt, Knie zeigt zu <paramref name="hint"/>).</summary>
        private static void Leg(Transform upper, Transform lower, Transform foot, Vector3 thighDir, Vector3 shinDir, Vector3 spread, Vector3 hint, float w)
        {
            if (upper == null || lower == null || foot == null || w <= 0.001f) return;
            float lt = Vector3.Distance(upper.position, lower.position), ls = Vector3.Distance(lower.position, foot.position);
            Vector3 target = upper.position + thighDir.normalized * lt + shinDir.normalized * ls + spread;
            LimbIK.TwoBone(upper, lower, foot, target, hint, w);
        }

        // ------------------------------------------------------------------ Ausweichen im Kampf (passiv)

        private float _evadeT = -1f, _evadeSide = 1f;
        private Vector3 _evadeFootL, _evadeFootR;

        /// <summary>Ausgewichen: Seite weg von der Schlaghand des Angreifers (sonst zufaellig).</summary>
        private void StartEvade(in DamageInfo info)
        {
            if (!EnsureBones()) return;
            Vector3 fromAttacker = -info.Direction;
            float side = Vector3.Dot(fromAttacker, _model.right);
            _evadeSide = Mathf.Abs(side) > 0.2f ? -Mathf.Sign(side) : (Random.value < 0.5f ? -1f : 1f);
            _evadeT = 0f;
            SfxPlayer.Play(SfxId.Dodge, transform.position + Vector3.up, 0.45f, 0.1f);
        }

        /// <summary>
        /// Seitschritt: Huefte gleitet ~9 cm zur Seite und sinkt leicht, der Oberkoerper dreht und neigt sich weg vom Schlag,
        /// der Kopf zieht mit – auf die laufende Pose aufgesetzt. Die Fuesse bleiben, wo sie standen (Bein-IK).
        /// </summary>
        private void UpdateEvade(float dt, bool detailed)
        {
            const float dur = 0.4f;
            _evadeT += dt;
            if (_evadeT >= dur) { _evadeT = -1f; return; }
            float p = _evadeT / dur;
            float k = p < 0.22f ? MotionMath.SmoothStep01(p / 0.22f) : 1f - MotionMath.SmoothStep01((p - 0.3f) / 0.7f);
            k = Mathf.Clamp01(k);
            if (k <= 0.001f) return;
            // 22.10. (Codex Kampfreaktionen): entfernte Figuren (LOD 1) weichen mit dem Rumpf aus statt gar nicht
            if (!detailed) { Torso(16f * _evadeSide * k, -6f * k, 12f * _evadeSide * k, 1f); return; }
            _u = _model.up; _f = _model.forward; _rt = _model.right;
            Vector3 fl = _lFoot != null ? _lFoot.position : Vector3.zero, fr = _rFoot != null ? _rFoot.position : Vector3.zero;
            Quaternion rl = _lFoot != null ? _lFoot.rotation : Quaternion.identity, rr = _rFoot != null ? _rFoot.rotation : Quaternion.identity;   // 22.10.: Fußspitze bleibt (Gleiten 5 cm)
            _hips.position += (_rt * (0.09f * _evadeSide) - _u * 0.05f - _f * 0.03f) * k;
            _hips.rotation = Quaternion.AngleAxis(10f * _evadeSide * k, _u) * _hips.rotation;
            // Fuesse zurueck an ihren Platz, Knie nach vorn
            if (_lFoot != null) { LimbIK.TwoBone(_lUpLeg, _lLeg, _lFoot, fl, _f, 1f); _lFoot.rotation = rl; }
            if (_rFoot != null) { LimbIK.TwoBone(_rUpLeg, _rLeg, _rFoot, fr, _f, 1f); _rFoot.rotation = rr; }
            Torso(16f * _evadeSide * k, -6f * k, 12f * _evadeSide * k, 1f);
            if (_head != null) Rot(_head, 0f, 0f, 6f * _evadeSide * k);
        }
    }
}
