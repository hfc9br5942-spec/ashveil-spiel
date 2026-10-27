using System;
using UnityEngine;

namespace Ashveil.Rendering.Sky
{
    /// <summary>
    /// Klima der Welt (§ 5, § 43–47): Seed, Zustaende mit Haeufigkeiten, Monde, Farben des Tages, Sternbilder.
    /// Daraus erzeugt <see cref="SkyMonthSchedule"/> fuer jeden 30-Tage-Monat einen eigenen, reproduzierbaren Plan.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Himmel/Klima", fileName = "SkyClimate")]
    public sealed class SkyClimateProfile : ScriptableObject
    {
        [Tooltip("Weltseed – alle Rechner muessen denselben haben (steht im Build)")]
        public int worldSeed = 7719;
        [Tooltip("Tage je Himmelsmonat")] public int monthDays = 30;
        [Tooltip("Zustand zu Beginn der Welt (Tag 0) – passend zum gestalteten Kareth-Licht")]
        public SkyState firstState = SkyState.Scattered;
        public CloudProfile[] clouds = Array.Empty<CloudProfile>();
        [Tooltip("Uebergangsdauer zwischen zwei Zustaenden (Spielstunden, min/max)")]
        public Vector2 transitionHours = new Vector2(3f, 7f);

        [Header("Wind (Einheiten Wolkenraum je Sekunde; aendert sich ueber Tage)")]
        public Vector2 lowWind = new Vector2(0.0022f, 0.0009f);
        public Vector2 highWind = new Vector2(0.0055f, -0.0006f);
        [Tooltip("Drehende Windanteile: Staerke und Periode in Tagen")]
        public float windSwing = 0.7f;
        public float windSwingDays = 5.3f;
        [Tooltip("Wie schnell Wolken entstehen und zerfallen (Noise-Zeitachse je Sekunde)")]
        public float evolution = 0.0016f;

        [Header("Himmelskoerper")]
        public CelestialBodyDefinition moonA;
        public CelestialBodyDefinition moonB;
        [Tooltip("Geografische Breite des Beobachters (Grad) – neigt die Himmelsachse")]
        public float latitude = 48f;
        [Tooltip("Mondlicht nachts: Staerke ohne Mond … bei hellem Vollmond (Faktor auf das Nachtlicht)")]
        public Vector2 moonLight = new Vector2(0.55f, 1.05f);

        [Header("Farben ueber den Tag (Stuetzstellen, weich interpoliert)")]
        public SkyColorKey[] colorKeys = Array.Empty<SkyColorKey>();

        [Header("Sterne")]
        [Range(0f, 0.2f)] public float starDensity = 0.055f;
        [Range(0f, 1f)] public float milkyWay = 0.35f;
        public Constellation[] constellations = Array.Empty<Constellation>();

        [Header("Sternschnuppen und Ereignisse (Pass 12)")]
        [Tooltip("Einzelne Sternschnuppen je Spielstunde in voller Nacht (ohne Schauer)")]
        public float sporadicMeteorsPerHour = 8f;
        public SkyEventDefinition[] events = Array.Empty<SkyEventDefinition>();

        [HideInInspector] public int builderVersion;

        /// <summary>Farben zur Stunde h (weich zwischen den Stuetzstellen, ueber Mitternacht stetig).</summary>
        public SkyColorKey SampleColors(float h)
        {
            if (colorKeys == null || colorKeys.Length == 0) return default;
            int n = colorKeys.Length;
            for (int i = 0; i < n; i++)
            {
                SkyColorKey a = colorKeys[i], b = colorKeys[(i + 1) % n];
                float ha = a.hour, hb = i + 1 < n ? b.hour : b.hour + 24f;
                float hh = h < ha ? h + 24f : h;
                if (hh < ha || hh > hb) continue;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ha, hb, hh));
                return new SkyColorKey
                {
                    hour = h,
                    zenith = Color.Lerp(a.zenith, b.zenith, t),
                    horizon = Color.Lerp(a.horizon, b.horizon, t),
                    ground = Color.Lerp(a.ground, b.ground, t),
                    glow = Color.Lerp(a.glow, b.glow, t)
                };
            }
            return colorKeys[0];
        }

        public CloudProfile Profile(SkyState s)
        {
            foreach (CloudProfile p in clouds) if (p != null && p.state == s) return p;
            return clouds.Length > 0 ? clouds[0] : null;
        }
    }
}
