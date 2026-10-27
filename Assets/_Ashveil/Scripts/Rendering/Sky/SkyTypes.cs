using System;
using UnityEngine;

namespace Ashveil.Rendering.Sky
{
    /// <summary>Himmelszustaende (Vorgabe Dynamic Sky V1 § 42). Reihenfolge = Speicherformat der Profile.</summary>
    public enum SkyState
    {
        Clear = 0,
        ClearCirrus = 1,
        Scattered = 2,
        Broken = 3,
        Dramatic = 4,
        Overcast = 5,
        Haze = 6
    }

    /// <summary>Darstellungsstufe des Himmels (§ 69). Der Himmelszustand selbst ist auf allen Stufen gleich.</summary>
    public enum SkyQuality
    {
        /// <summary>Verlauf, Sonne, zwei Monde, Sterne, eine Wolkenschicht ohne Lichtprobe.</summary>
        Low = 0,
        /// <summary>Zwei Wolkenschichten mit einfacher Beleuchtung.</summary>
        Medium = 1,
        /// <summary>Zusaetzliche Wolkendetails (Erosion, feinere Lichtprobe).</summary>
        High = 2
    }

    /// <summary>
    /// Alles, was der Himmel zu einem Zeitpunkt zeigt – rein aus Weltuhr und Weltseed berechnet (§ 4, § 67).
    /// Jeder Rechner kommt zum selben Zustand, ohne dass Wolken, Monde oder Wind uebertragen werden.
    /// </summary>
    [Serializable]
    public struct SkyRuntimeState
    {
        public int WorldDay;
        public int MonthIndex;
        public float TimeOfDay;
        public double TotalHours;

        public Vector3 SunDirection;        // zur Sonne (auch unter dem Horizont)
        public float SunTransmittance;      // 0..1 – wie viel Sonnenlicht die Wolken durchlassen
        public float SunElevation;          // Grad

        public Vector3 MoonADirection, MoonBDirection;
        /// <summary>Beleuchteter Anteil 0 (Neumond) … 1 (Vollmond).</summary>
        public float MoonAIllumination, MoonBIllumination;
        /// <summary>Mondphase 0..1 (0 Neumond, 0,25 zunehmender Halbmond, 0,5 Vollmond, 0,75 abnehmender Halbmond).</summary>
        public float MoonAPhase, MoonBPhase;
        public float MoonAElevation, MoonBElevation;

        /// <summary>Drehung des Sternenhimmels (Stundenwinkel, Grad) und Achsen des Himmelsgewoelbes in Weltkoordinaten.</summary>
        public float StarRotation;
        public Vector3 CelestialX, CelestialY, CelestialZ;
        public float StarVisibility;

        public SkyState CloudType;          // vorherrschender Zustand
        public SkyState CloudFrom;          // Zustand, aus dem gerade uebergeblendet wird
        public float CloudBlend;            // 0 = CloudFrom, 1 = CloudType
        public float CloudCoverage, CloudDensity, CloudScale, CloudErosion, CloudDarkness;
        public float HighCoverage, HighDensity;
        public Vector2 CloudWindLow, CloudWindHigh;
        public float HorizonHaze;
        public float AtmosphericDensity;
        public float SkyExposure;

        /// <summary>Mondlicht (ein einziges Richtungslicht, § 20): Richtung zum Licht und Staerke 0..1.</summary>
        public Vector3 MoonLightDirection;
        public float MoonLightFactor;
        public bool MoonLightVisible;

        public string ActiveSkyEvent;
    }

    /// <summary>Farben des Himmels zu einer Stunde (Zenit, Horizont, Boden, Sonnenleuchten – Alpha = Staerke).</summary>
    [Serializable]
    public struct SkyColorKey
    {
        public float hour;
        public Color zenith, horizon, ground, glow;
        public SkyColorKey(float hour, Color zenith, Color horizon, Color ground, Color glow)
        {
            this.hour = hour; this.zenith = zenith; this.horizon = horizon; this.ground = ground; this.glow = glow;
        }
    }

    /// <summary>Ein Stern eines Sternbilds: Himmelskoordinaten (Rektaszension/Deklination in Grad) und Helligkeit.</summary>
    [Serializable]
    public struct ConstellationStar
    {
        public float ra, dec, brightness;
        public ConstellationStar(float ra, float dec, float brightness) { this.ra = ra; this.dec = dec; this.brightness = brightness; }
    }

    /// <summary>Eigenes, kuratiertes Sternbild (§ 23) – keine Linien am Himmel, nur bewusst gesetzte Sterne.</summary>
    [Serializable]
    public sealed class Constellation
    {
        public string name;
        [TextArea] public string lore;
        public ConstellationStar[] stars = Array.Empty<ConstellationStar>();
    }
}
