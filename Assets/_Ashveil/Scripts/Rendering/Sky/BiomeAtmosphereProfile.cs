using System;
using UnityEngine;

namespace Ashveil.Rendering.Sky
{
    /// <summary>Lokale Luft eines Gebiets (§ 49–55) – verschiebt das globale Wetter, ersetzt es nicht. (Pass 11)</summary>
    [CreateAssetMenu(menuName = "Ashveil/Himmel/Biom-Atmosphaere", fileName = "Biome_")]
    public sealed class BiomeAtmosphereProfile : ScriptableObject
    {
        public string displayName = "Standard";
        public Color fogTint = Color.white;
        [Tooltip("Faktor auf die Nebeldichte")] public float fogDensity = 1f;
        [Tooltip("Zusaetzlicher Horizontdunst")] [Range(0f, 1f)] public float hazeBias;
        [Tooltip("Verschiebt die Bewoelkung (+ = eher bedeckt)")] [Range(-0.5f, 0.5f)] public float cloudBias;
        [Tooltip("Faktor auf den Sonnendurchlass")] [Range(0.2f, 1.2f)] public float sunTransmission = 1f;
        [Tooltip("Faktor auf die Sichtweite")] [Range(0.3f, 1.5f)] public float visibility = 1f;
        [Tooltip("Blendzeit beim Betreten (Sekunden)")] public float blendSeconds = 6f;
    }
}
