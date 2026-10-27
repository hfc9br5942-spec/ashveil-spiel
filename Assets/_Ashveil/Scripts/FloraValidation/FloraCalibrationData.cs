using System;
using System.Collections.Generic;

namespace Ashveil.FloraValidation
{
    /// <summary>
    /// Ergebnis des v3-Kalibrierlaufs (Unity/URP-Licht). Wird vom Messlauf nach SourceArt/FloraValidation_v2/calibration_v3.json
    /// geschrieben und vom Importer (Editor) auf Materialien und LOD-Ketten angewendet. Nur Testszene, kein Kanon.
    /// </summary>
    [Serializable]
    public class FloraCalibrationData
    {
        public string created;
        /// <summary>Alpha-Schaerfung je Mip fuer Blatt-/Nadelkarten je Familie (0 = aus, wie v1).</summary>
        public float mipF01, mipF04a, mipF10;
        /// <summary>F04a FAR v3 (dichtere Etagenreihen + Fernkarte mit Mindestbreite) statt FAR v2.</summary>
        public bool useFar3;
        /// <summary>Impostor v3 (seitenverhaeltnis-gerechte Rahmen) statt v2.</summary>
        public bool useVfar3;
        /// <summary>v4: F04a Windform FAR4 (positionsgebundene Cluster) und Impostor v4 (unbeleuchteter Bake + richtungsneutrales AO).</summary>
        public bool useFar4, useVfar4;
        /// <summary>Impostor-Atlas: 1024 oder 2048; BC7 (Albedo) + BC1 (Normal) statt Standardkompression.</summary>
        public int impostorMaxSize = 2048;
        public bool impostorBc;
        /// <summary>F10 LOD0-Variante: "" (v2), "a3" oder "b3".</summary>
        public string f10Lod0 = "";
        public List<ImpostorCal> impostors = new List<ImpostorCal>();
        public List<TintCal> tints = new List<TintCal>();
        public List<CalRow> rows = new List<CalRow>();
        public List<string> notes = new List<string>();
    }

    [Serializable]
    public class ImpostorCal
    {
        public string asset;
        public float cutoff = 0.42f, gain = 1f, mipAlpha;
        /// <summary>Normal-Atlas -> Blickrichtung (0 = unveraendert) und Transluzenz (-1 = Materialstandard).</summary>
        public float normalBlend, translucency = -1f;
        /// <summary>Diagnose: Achsvorzeichen des Normal-Atlas (Standard -1, 1, 1).</summary>
        public float signX = -1f, signZ = 1f;
        /// <summary>v4: AO auf Umgebungslicht / auf direktes Licht, Schaerfe der Ansichtsueberblendung.</summary>
        public float aoStrength = 1f, aoDirect, blendSharp = 1f;
        /// <summary>v4b: Glanz des Impostors (-1 = Materialstandard).</summary>
        public float smoothness = -1f;
        /// <summary>Micro-Pass: Anteil der Kronen-Grossnormale und Dicke-Daempfung der Transluzenz (0 = aus).</summary>
        public float macroBlend, thickK, selfK, sheen;
    }

    [Serializable]
    public class TintCal
    {
        public string asset, material;
        public float r = 1f, g = 1f, b = 1f, cutoff = -1f;
    }

    [Serializable]
    public class CalRow
    {
        public string step, asset, variant, view;
        public float distance, iou, dLum, dArea, value;
    }
}
