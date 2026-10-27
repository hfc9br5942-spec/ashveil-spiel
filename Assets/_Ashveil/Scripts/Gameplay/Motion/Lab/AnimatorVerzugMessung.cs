using Ashveil.Gameplay.Characters;
using UnityEngine;

namespace Ashveil.Gameplay.Motion.Lab
{
    /// <summary>
    /// 20.10. (REQ-20261020-010 Block A, REP-004): Sieht der Animator beim Auswerten den Motorzustand DESSELBEN Bildes?
    /// Läuft als erstes LateUpdate – also direkt nach der Animator-Auswertung und vor allen Posern/der Bridge – und vergleicht
    /// die ungedämpften Parameter (Grounded, Sprinting, Walking, VerticalSpeed) mit dem Motor. Ein Bild Verzug zeigt sich als
    /// falsches Grounded an jedem Absprung/jeder Landung und als VerticalSpeed-Abweichung = Änderung pro Bild.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class AnimatorVerzugMessung : MonoBehaviour
    {
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int SprintingId = Animator.StringToHash("Sprinting");
        private static readonly int WalkingId = Animator.StringToHash("Walking");
        private static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");

        public CharacterMotor Motor;
        public Animator Animator;
        public bool Aktiv;

        public int Bilder { get; private set; }
        public int BoolFalsch { get; private set; }
        public float VyMax { get; private set; }
        public float VySumme { get; private set; }

        public void Reset0() { Bilder = BoolFalsch = 0; VyMax = VySumme = 0f; }

        private void LateUpdate()
        {
            if (!Aktiv || Motor == null || Animator == null || !Animator.isActiveAndEnabled || Motor.IsProxy) return;
            MotorState s = Motor.State;
            bool falsch = false;
            if (Hat(GroundedId)) falsch |= Animator.GetBool(GroundedId) != (s.Grounded || s.Swimming);
            if (Hat(SprintingId)) falsch |= Animator.GetBool(SprintingId) != s.IsSprinting;
            if (Hat(WalkingId)) falsch |= Animator.GetBool(WalkingId) != s.IsWalking;
            if (falsch) BoolFalsch++;
            if (Hat(VerticalSpeedId))
            {
                float d = Mathf.Abs(Animator.GetFloat(VerticalSpeedId) - (s.Swimming ? 0f : s.Velocity.y));
                VyMax = Mathf.Max(VyMax, d); VySumme += d;
            }
            Bilder++;
        }

        private bool Hat(int id)
        {
            foreach (AnimatorControllerParameter p in Animator.parameters) if (p.nameHash == id) return true;
            return false;
        }
    }
}
