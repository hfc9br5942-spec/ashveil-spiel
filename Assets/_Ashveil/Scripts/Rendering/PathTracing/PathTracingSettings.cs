using System;
using UnityEngine;

namespace Ashveil.Rendering.PathTracing
{
    public enum PathTracePreset { Preview, PhotoHigh, PhotoUltra, Cinematic }

    /// <summary>Diagnose-Ansichten (Master-Plan §116). Zahlen muessen zu Resolve-Shader und Raygen passen.</summary>
    public enum PathTraceView
    {
        Bild = 0, Albedo = 1, Normale = 2, Tiefe = 3, Direkt = 4, Indirekt = 5, Emission = 6,
        Diffus = 7, Spiegelnd = 8, Probenzahl = 9, Varianz = 10, NaN = 11
    }

    /// <summary>Woher das Umgebungslicht kommt: Spielhimmel (gebacken), feste Farbe (Tests), aus, URP-Verlauf.</summary>
    public enum PathTraceEnvironment { Spielhimmel = 0, Gleichmaessig = 1, Aus = 2, Verlauf = 3 }

    public enum PathTraceDenoise { Aus, Leicht, Stark }

    [Serializable]
    public sealed class PathTraceSettings
    {
        public PathTracePreset preset = PathTracePreset.PhotoHigh;
        [Range(0, 16)] public int maxBounces = 8;
        [Range(1, 32)] public int samplesPerFrame = 4;
        [Tooltip("Ab diesem Abprall Russisches Roulette")]
        [Range(1, 16)] public int russianRouletteStart = 3;
        [Tooltip("Stopp bei dieser Probenzahl je Pixel (0 = nie)")]
        public int targetSamples = 1024;
        [Tooltip("Stopp, wenn der relative Fehler (95. Perzentil) darunter liegt (0 = aus)")]
        public float convergenceTarget = 0.01f;
        [Tooltip("Ausreisser-Grenze fuer indirektes Licht (Luminanz, 0 = aus). Erzeugt Bias – nur bewusst einsetzen.")]
        public float fireflyClamp;
        [Tooltip("Glanz-Filter (wie Cycles 'Filter Glossy'): Mindest-Rauheit nach einem rauen Abprall. Nimmt Kaustik-Gluehwuermchen, " +
                 "veraendert direkt sichtbare Materialien nicht. 0 = streng erwartungstreu (Referenz).")]
        [Range(0f, 1f)] public float filterGlossy = 0.3f;
        [Tooltip("Belichtungskorrektur in Blendenstufen (Kamera, vor der normalen Nachbearbeitung)")]
        [Range(-6f, 6f)] public float exposureEV;
        [Tooltip("Belichtung automatisch so abgleichen, dass das Bild so hell wirkt wie das Spielbild (Kamera, nicht Licht)")]
        public bool matchGameExposure;
        [Tooltip("Blendenradius in Metern (0 = alles scharf)")]
        public float apertureRadius;
        [Tooltip("Fokusabstand in Metern")]
        public float focusDistance = 10f;
        [Tooltip("Winkelradius der Sonnenscheibe (rad); 0,0095 = wie der Spielhimmel")]
        public float sunAngularRadius = 0.0095f;
        [Tooltip("Winkelradius der Mondscheibe (rad), wenn das Hauptlicht der Mond ist")]
        public float moonAngularRadius = 0.012f;
        public uint globalSeed = 1;
        public PathTraceView view = PathTraceView.Bild;
        public PathTraceEnvironment environment = PathTraceEnvironment.Spielhimmel;
        public Color uniformEnvironment = Color.white;
        public float environmentIntensity = 1f;
        [Tooltip("Adaptive Abtastung: konvergierte Kacheln bekommen keine weiteren Proben")]
        public bool adaptive = true;
        public int adaptiveMinSamples = 64;
        public int adaptiveTile = 8;
        public PathTraceDenoise denoise = PathTraceDenoise.Leicht;
        [Tooltip("Welt fuer die Aufnahme einfrieren (Lichter, Posen, Himmel). Server und Spiel laufen weiter.")]
        public bool freezeWorld = true;
        [Tooltip("Nebel/Dunst der Welt als echtes Medium (Einfachstreuung: Sonnenstrahlen, Feuerschein im Nebel)")]
        public bool fog = true;
        [Range(0f, 1f)] public float fogScatterAlbedo = 0.9f;
        [Tooltip("Vorwaertsstreuung (Henyey-Greenstein g): 0 gleichmaessig, 0,6 Dunst mit Sonnenhof")]
        [Range(-0.5f, 0.9f)] public float fogAnisotropy = 0.6f;
        [Tooltip("Nur fuer Pruefungen: 0 MIS, 1 nur Lichtproben, 2 nur BSDF-Proben")]
        [Range(0, 2)] public int misMode;
        [Tooltip("Aufnahme-Radius fuer kleine Objekte (m); grosse Objekte bleiben immer drin")]
        public float captureRadius = 600f;

        public void ApplyPreset(PathTracePreset p)
        {
            preset = p;
            switch (p)
            {
                case PathTracePreset.Preview:
                    samplesPerFrame = 1; maxBounces = 3; russianRouletteStart = 2; targetSamples = 0; adaptive = false; denoise = PathTraceDenoise.Stark; break;
                case PathTracePreset.PhotoHigh:
                    samplesPerFrame = 4; maxBounces = 8; russianRouletteStart = 3; targetSamples = 1024; adaptive = true; denoise = PathTraceDenoise.Leicht; break;
                case PathTracePreset.PhotoUltra:
                    samplesPerFrame = 8; maxBounces = 12; russianRouletteStart = 3; targetSamples = 4096; adaptive = true; denoise = PathTraceDenoise.Leicht; break;
                case PathTracePreset.Cinematic:
                    samplesPerFrame = 8; maxBounces = 12; russianRouletteStart = 4; targetSamples = 1024; adaptive = true; denoise = PathTraceDenoise.Aus; break;
            }
        }

        public PathTraceSettings Clone() => (PathTraceSettings)MemberwiseClone();
    }
}
