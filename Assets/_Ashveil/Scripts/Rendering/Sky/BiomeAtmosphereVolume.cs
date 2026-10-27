using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Rendering.Sky
{
    /// <summary>
    /// Dynamic Sky Pass 11 (§ 49–55, § 87): Gebiet mit eigener Luft. Der Himmel bleibt global (Sonne, Monde, Wetterlage),
    /// das Gebiet verschiebt nur Nebel, Dunst, Bewoelkung, Sonnendurchlass und Sicht – mit breiter raeumlicher Blendzone
    /// und zusaetzlicher zeitlicher Glaettung (nie „ein Schritt = anderer Himmel“). Rein lokal fuer die eigene Kamera,
    /// kein Netzverkehr.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BiomeAtmosphereVolume : MonoBehaviour
    {
        [SerializeField] private BiomeAtmosphereProfile profile;
        [Tooltip("Volle Wirkung bis zu diesem Abstand (m, waagerecht)")]
        [SerializeField] private float radius = 150f;
        [Tooltip("Breite der Blendzone dahinter (m)")]
        [SerializeField] private float falloff = 70f;

        private static readonly List<BiomeAtmosphereVolume> Registered = new List<BiomeAtmosphereVolume>();
        public static IReadOnlyList<BiomeAtmosphereVolume> All => Registered;

        public BiomeAtmosphereProfile Profile => profile;
        public float Radius => radius;
        public float Falloff => falloff;

        public void Configure(BiomeAtmosphereProfile p, float r, float f)
        {
            profile = p;
            radius = r;
            falloff = Mathf.Max(1f, f);
        }

        /// <summary>Anteil 0..1 an einem Weltpunkt (weich ueber die Blendzone).</summary>
        public float Weight(Vector3 p)
        {
            Vector3 c = transform.position;
            float d = new Vector2(p.x - c.x, p.z - c.z).magnitude;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius, radius + falloff, d));
        }

        private void OnEnable() { if (!Registered.Contains(this)) Registered.Add(this); }
        private void OnDisable() => Registered.Remove(this);

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 0.6f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.color = new Color(0.4f, 0.8f, 0.6f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, radius + falloff);
        }
    }

    /// <summary>Gemischte lokale Luft (Ergebnis aller Gebiete an einem Punkt).</summary>
    public struct LocalAtmosphere
    {
        public Color FogTint;
        public float FogDensity;     // Faktor
        public float HazeBias;       // + Horizontdunst
        public float CloudBias;      // + Bewoelkung
        public float SunTransmission;// Faktor
        public float Visibility;     // Faktor (klein = mehr Nebel)
        public float BlendSeconds;
        public string Name;

        public static LocalAtmosphere Neutral => new LocalAtmosphere
        {
            FogTint = Color.white, FogDensity = 1f, HazeBias = 0f, CloudBias = 0f, SunTransmission = 1f, Visibility = 1f, BlendSeconds = 6f, Name = "Standard"
        };

        public static LocalAtmosphere Lerp(LocalAtmosphere a, LocalAtmosphere b, float t) => new LocalAtmosphere
        {
            FogTint = Color.Lerp(a.FogTint, b.FogTint, t),
            FogDensity = Mathf.Lerp(a.FogDensity, b.FogDensity, t),
            HazeBias = Mathf.Lerp(a.HazeBias, b.HazeBias, t),
            CloudBias = Mathf.Lerp(a.CloudBias, b.CloudBias, t),
            SunTransmission = Mathf.Lerp(a.SunTransmission, b.SunTransmission, t),
            Visibility = Mathf.Lerp(a.Visibility, b.Visibility, t),
            BlendSeconds = Mathf.Lerp(a.BlendSeconds, b.BlendSeconds, t),
            Name = t >= 0.5f ? b.Name : a.Name
        };

        public static LocalAtmosphere From(BiomeAtmosphereProfile p) => p == null ? Neutral : new LocalAtmosphere
        {
            FogTint = p.fogTint, FogDensity = p.fogDensity, HazeBias = p.hazeBias, CloudBias = p.cloudBias,
            SunTransmission = p.sunTransmission, Visibility = p.visibility, BlendSeconds = p.blendSeconds, Name = p.displayName
        };

        /// <summary>Wirkung auf die Nebeldichte insgesamt (Dichte / Sicht).</summary>
        public float FogFactor => FogDensity / Mathf.Max(0.2f, Visibility);
    }
}
