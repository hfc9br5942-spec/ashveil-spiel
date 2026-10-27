using System;
using UnityEngine;

namespace Ashveil.Data.Motion
{
    /// <summary>
    /// Stellschrauben, mit denen Koerper, Rasse, Klasse, Ruestung und Waffe die Bewegungswirkung formen (§1/§21).
    /// Skalen multiplizieren sich (1 = neutral), Versaetze addieren sich (0 = neutral). Rein optisch –
    /// kein Wert hier veraendert Tempo, Reichweite oder Treffer (Gameplay bleibt getrennt, §21/§100).
    /// </summary>
    [Serializable]
    public struct MotionModifiers
    {
        [Header("Skalen (1 = neutral)")]
        [Tooltip("Schrittlaenge bei gleichem Tempo (kleiner = mehr, kuerzere Schritte).")]
        public float strideScale;
        [Tooltip("Freie Armschwingung.")]
        public float armSwingScale;
        [Tooltip("Oberkoerper-Mitbewegung (kleiner = ruhiger).")]
        public float upperBodyMotion;
        [Tooltip("Sichtbare Masse beim Anfahren/Bremsen (Vorlage/Nachschwingen).")]
        public float massInertia;
        [Tooltip("Sichtbare Traegheit beim Richtungswechsel (Oberkoerper-Nachlauf).")]
        public float turnInertia;
        [Tooltip("Wucht der Landung (Einfedern, Erholungspose).")]
        public float landingWeight;
        [Tooltip("Atemausschlag im Stand.")]
        public float breathing;
        [Tooltip("Tempo der Gewichtsverlagerungen/Uebergaenge (groesser = flinker).")]
        public float transitionSpeed;

        [Header("Versaetze (0 = neutral)")]
        [Tooltip("Schwerpunkt tiefer (m, positiv = tiefer).")]
        public float centerOfMassDrop;
        [Tooltip("Beckenstabilitaet (0..1 addiert; hoeher = ruhigere Huefte).")]
        public float pelvisStability;
        [Tooltip("Schulter-/Beckenrhythmus (Gegenrotation, Grad).")]
        public float shoulderPelvisRhythm;
        [Tooltip("Rumpfvorlage in Grad (positiv = nach vorn).")]
        public float torsoLean;

        public static MotionModifiers Neutral => new MotionModifiers
        {
            strideScale = 1f, armSwingScale = 1f, upperBodyMotion = 1f, massInertia = 1f,
            turnInertia = 1f, landingWeight = 1f, breathing = 1f, transitionSpeed = 1f
        };

        /// <summary>Unbenutzte (0-)Skalen als neutral behandeln – so bleibt ein frisch angelegtes Profil wirkungslos.</summary>
        public MotionModifiers Sanitized()
        {
            MotionModifiers m = this;
            if (m.strideScale <= 0f) m.strideScale = 1f;
            if (m.armSwingScale <= 0f) m.armSwingScale = 1f;
            if (m.upperBodyMotion <= 0f) m.upperBodyMotion = 1f;
            if (m.massInertia <= 0f) m.massInertia = 1f;
            if (m.turnInertia <= 0f) m.turnInertia = 1f;
            if (m.landingWeight <= 0f) m.landingWeight = 1f;
            if (m.breathing <= 0f) m.breathing = 1f;
            if (m.transitionSpeed <= 0f) m.transitionSpeed = 1f;
            return m;
        }

        /// <summary>Schicht b auf a anwenden (Reihenfolge Koerper → Rasse → Klasse → Ruestung → Waffe).</summary>
        public static MotionModifiers Combine(in MotionModifiers a, in MotionModifiers b)
        {
            MotionModifiers x = a.Sanitized(), y = b.Sanitized();
            return new MotionModifiers
            {
                strideScale = x.strideScale * y.strideScale,
                armSwingScale = x.armSwingScale * y.armSwingScale,
                upperBodyMotion = x.upperBodyMotion * y.upperBodyMotion,
                massInertia = x.massInertia * y.massInertia,
                turnInertia = x.turnInertia * y.turnInertia,
                landingWeight = x.landingWeight * y.landingWeight,
                breathing = x.breathing * y.breathing,
                transitionSpeed = x.transitionSpeed * y.transitionSpeed,
                centerOfMassDrop = x.centerOfMassDrop + y.centerOfMassDrop,
                pelvisStability = Mathf.Clamp01(x.pelvisStability + y.pelvisStability),
                shoulderPelvisRhythm = x.shoulderPelvisRhythm + y.shoulderPelvisRhythm,
                torsoLean = x.torsoLean + y.torsoLean
            };
        }
    }
}
