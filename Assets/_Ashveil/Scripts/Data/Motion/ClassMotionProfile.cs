using UnityEngine;

namespace Ashveil.Data.Motion
{
    /// <summary>Bewegungsphilosophie einer Klassenrolle (§72).</summary>
    [CreateAssetMenu(menuName = "Ashveil/Motion/Klasse", fileName = "MotionClass_")]
    public sealed class ClassMotionProfile : MotionLayerProfile
    {
        public ClassRole role = ClassRole.Damage;
        [Tooltip("Sekunden, die die Figur nach dem Kampf wachsam bleibt, bevor sie wegsteckt (§8).")]
        public float autoSheatheDelay = 6f;
        [Tooltip("Nach einem Angriff kehrt die Waffe/das Schild in diese Haltung zurueck (§74).")]
        public PoseTag recoveryPose = PoseTag.GuardCenter;
        [Tooltip("Gewichtung kontrollierter (kurzer) Angriffe gegenueber ausladenden (0..1).")]
        [Range(0f, 1f)] public float economyOfMotion = 0.5f;
    }
}
