using System;
using UnityEngine;

namespace Ashveil.Rendering.Sky
{
    /// <summary>Wolkenbild eines Himmelszustands (§ 42) – zwei Schichten, Lichtdurchlass, Dunst.</summary>
    [CreateAssetMenu(menuName = "Ashveil/Himmel/Wolkenprofil", fileName = "Cloud_")]
    public sealed class CloudProfile : ScriptableObject
    {
        public SkyState state;
        [Header("Untere/mittlere Schicht (Cumulus)")]
        [Range(0f, 1f)] public float lowCoverage = 0.35f;
        [Range(0f, 1f)] public float lowDensity = 0.75f;
        [Tooltip("Groesse der Formen (1 = normale Cumulus, groesser = grosse Formationen)")]
        [Range(0.3f, 3f)] public float lowScale = 1f;
        [Tooltip("0 = weiche Raender, 1 = stark zerfranst")]
        [Range(0f, 1f)] public float lowErosion = 0.5f;
        [Tooltip("Dunkle Unterseiten (schwere Wolkenbank)")]
        [Range(0f, 1f)] public float darkness = 0.2f;
        [Header("Hohe Schicht (Cirrus)")]
        [Range(0f, 1f)] public float highCoverage = 0.2f;
        [Range(0f, 1f)] public float highDensity = 0.5f;
        [Header("Licht und Luft")]
        [Tooltip("Sonnendurchlass (Minimum/Maximum) – dazwischen wandern langsame Sonnenfenster (§ 8, § 10)")]
        public Vector2 sunTransmittance = new Vector2(0.85f, 1f);
        [Range(0f, 1f)] public float horizonHaze = 0.2f;
        [Tooltip("Wie oft dieser Zustand im Monatsplan vorkommt")]
        [Range(0f, 1f)] public float weight = 0.15f;
        [Tooltip("Ordnung fuer glaubwuerdige Uebergaenge (klar 0 … bedeckt 4)")]
        public float order = 2f;
        [Tooltip("Wie lange der Zustand typischerweise bleibt (Spielstunden, min/max)")]
        public Vector2 dwellHours = new Vector2(8f, 26f);
    }
}
