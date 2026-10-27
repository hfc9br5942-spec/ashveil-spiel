using Ashveil.Gameplay.Characters;
using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>
    /// 23.10.2026 (Max: vorhandene Magier-Clips nutzen; Ganzkörpereinsatz überall): Wirken mit den Spell_Simple-Clips bewegt im Animator
    /// nur den linken Arm (Ebene „Cast“ – die rechte Hand hält den Stab). Hier kommt der Körper dazu: beim Halten dreht der Rumpf die
    /// Wirkschulter leicht nach vorn und legt sich etwas vor, beim Auslösen schiebt die Hüfte kurz nach vorn (Füße bleiben stehen).
    /// Abbruch: Rumpf gleitet ohne Stoß zurück.
    /// </summary>
    public sealed partial class CombatChoreographer
    {
        private static readonly int CastingParam = Animator.StringToHash("Casting");
        private float _wirkK, _ausloesT = -1f;
        private bool _wirkteVorher, _hatCasting, _castGeprueft;
        private int _castEbene = -1;

        private bool WirktAnimator()
        {
            if (!_castGeprueft || _anim.runtimeAnimatorController == null)
            {
                _castGeprueft = _anim.runtimeAnimatorController != null;
                _hatCasting = false;
                if (_castGeprueft)
                    foreach (AnimatorControllerParameter p in _anim.parameters) if (p.nameHash == CastingParam) { _hatCasting = true; break; }
                _castEbene = _castGeprueft ? _anim.GetLayerIndex("Cast") : -1;
            }
            return _hatCasting && _anim.GetBool(CastingParam);
        }

        private void UpdateWirken(float dt, bool detailed)
        {
            bool wirkt = WirktAnimator();
            // Ende des Wirkens: ausgelöst (Cast-Ebene geht in CastShoot) → kurzer Stoß aus der Hüfte; abgebrochen → nur zurückgleiten
            if (_wirkteVorher && !wirkt && _castEbene >= 0)
            {
                AnimatorStateInfo n = _anim.GetNextAnimatorStateInfo(_castEbene), c = _anim.GetCurrentAnimatorStateInfo(_castEbene);
                if (n.IsName("CastShoot") || c.IsName("CastShoot")) _ausloesT = 0f;
            }
            _wirkteVorher = wirkt;
            _wirkK = MotionMath.Damp(_wirkK, wirkt ? 1f : 0f, 0.15f, dt);
            float stoss = 0f;
            if (_ausloesT >= 0f)
            {
                _ausloesT += dt;
                float p = _ausloesT / 0.4f;
                if (p >= 1f) _ausloesT = -1f;
                else stoss = Huelle(p);
            }
            if (_wirkK < 0.001f && stoss < 0.001f) return;

            // Wirkhand links: Rumpf dreht die linke Schulter nach vorn (positiv = links vor), legt sich etwas vor; Auslösen verstärkt beides
            // 14° Drehung schob den Stabkopf (rechte Hand vor der Brust) vor das Gesicht – Drehung klein, Vorlage trägt den Einsatz
            Torso(6f * _wirkK + 5f * stoss, 7f * _wirkK + 10f * stoss, 0f, 1f);
            if (detailed && stoss > 0.001f)
            {
                Vector3 fl = _lFoot != null ? _lFoot.position : Vector3.zero, fr = _rFoot != null ? _rFoot.position : Vector3.zero;
                Quaternion rl = _lFoot != null ? _lFoot.rotation : Quaternion.identity, rr = _rFoot != null ? _rFoot.rotation : Quaternion.identity;
                _hips.position += (_f * 0.04f - _u * 0.015f) * stoss;
                if (_lFoot != null) { LimbIK.TwoBone(_lUpLeg, _lLeg, _lFoot, fl, _f, 1f); _lFoot.rotation = rl; }
                if (_rFoot != null) { LimbIK.TwoBone(_rUpLeg, _rLeg, _rFoot, fr, _f, 1f); _rFoot.rotation = rr; }
            }
        }
    }
}
