using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data.Motion
{
    /// <summary>Bewegungsidentitaet einer Waffenfamilie (§22/§23) – beeinflusst weit mehr als Angriffe.</summary>
    [CreateAssetMenu(menuName = "Ashveil/Motion/Waffe", fileName = "MotionWeapon_")]
    public sealed class WeaponMotionProfile : MotionLayerProfile
    {
        public WeaponFamily family = WeaponFamily.SwordShield;

        [Header("Sockel (§90)")]
        public WeaponSheath mainSheathed = WeaponSheath.HipLeft;
        public WeaponSheath offhandSheathed = WeaponSheath.Arm;
        [Tooltip("Schild bleibt beim Wegstecken am Arm (entspannt getragen) statt auf den Ruecken.")]
        public bool shieldStaysOnArm = true;

        [Header("Ziehen / Wegstecken (§7)")]
        [Tooltip("Sekunden bis die Waffe in der Hand ist (rein optisch – blockiert keinen Angriff).")]
        public float drawTime = 0.55f;
        public float sheatheTime = 0.8f;
        [Tooltip("Normierter Zeitpunkt im Zieh-Clip, an dem die Waffe vom Sockel in die Hand wechselt.")]
        [Range(0f, 1f)] public float drawSwapTime = 0.45f;
        [Range(0f, 1f)] public float sheatheSwapTime = 0.6f;

        [Header("Distanz und Freiraum (§68/§69)")]
        public float preferredRangeMin = 1.2f;
        public float preferredRangeMax = 2.4f;
        [Tooltip("Seitlicher Freiraum (m), den ausladende Schlaege brauchen.")]
        public float swingClearance = 1.2f;

        [Header("Angriffe")]
        public List<AttackMotionDefinition> attacks = new List<AttackMotionDefinition>();
        [Tooltip("Rueckfall, wenn fuer diese Familie noch nichts passt (§143).")]
        public WeaponMotionProfile fallback;
    }
}
