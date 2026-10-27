using System;
using UnityEngine;

namespace Ashveil.Rendering.Sky
{
    /// <summary>Mond oder anderer Himmelskoerper mit analytischer Bahn (§ 15, § 16).</summary>
    [CreateAssetMenu(menuName = "Ashveil/Himmel/Himmelskoerper", fileName = "Moon_")]
    public sealed class CelestialBodyDefinition : ScriptableObject
    {
        public string displayName = "Mond";
        [Tooltip("Umlaufzeit in Welt-Tagen (Phase und Aufgangszeit wiederholen sich danach)")]
        public float orbitalPeriodDays = 12.5f;
        [Tooltip("Bahnneigung gegen den Himmelsaequator (Grad)")]
        public float inclination = 8f;
        [Tooltip("Knotenlaenge der Bahn (Grad)")]
        public float longitude = 40f;
        [Tooltip("Startversatz der Phase (0..1)")]
        [Range(0f, 1f)] public float phaseOffset = 0.3f;
        [Tooltip("Scheinbarer Radius am Himmel (Grad)")]
        public float apparentRadius = 1.6f;
        [ColorUsage(false, true)] public Color color = new Color(0.92f, 0.9f, 0.86f);
        [Tooltip("Beitrag zum naechtlichen Mondlicht (bei Vollmond, hoch am Himmel)")]
        [Range(0f, 1f)] public float lightShare = 1f;
        [Tooltip("Oberflaechenmuster (Seed der Mare)")]
        public int surfaceSeed = 11;
    }
}
