using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Alles, was das persoenliche Aussehen hergibt – nur Optionen, fuer die es echte Modelle/Texturen gibt –
    /// plus Farbpaletten, Materialvorlage und Ersatz-Aussehen fuer fehlende Ressourcen.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Appearance Catalog", fileName = "AppearanceCatalog")]
    public sealed class AppearanceCatalog : ScriptableObject
    {
        [Serializable]
        public class BodyEntry
        {
            public string name = "Männlich";
            public BodyVariantMask variant = BodyVariantMask.Male;
            [Tooltip("Modell fuer die Charaktervorschau (dasselbe wie am Spieler)")]
            public GameObject model;
            public List<string> skinNames = new List<string>();
            public List<Texture2D> skinTextures = new List<Texture2D>();
        }

        [Serializable]
        public class StyleEntry
        {
            public string name = "Keine";
            [Tooltip("Leer = keine (z. B. kahl)")]
            public GameObject model;
            public BodyVariantMask bodies = BodyVariantMask.All;
            [Tooltip("M7: zusaetzliche Grundformen am Kopf (Zopf, Irokese, Kinnbart …) in Haarfarbe")]
            public List<RigidPart> parts = new List<RigidPart>();
            [Tooltip("M7: gemalte Gesichtsebene je Koerper (Index wie 'bodies'), z. B. Bartstoppeln")]
            public List<Texture2D> overlays = new List<Texture2D>();
        }

        /// <summary>M7: Hautton = Grundtextur des Koerpers (hell/dunkel) mal Farbton.</summary>
        [Serializable]
        public class SkinTone
        {
            public string name = "Hell";
            [Tooltip("Index in BodyEntry.skinTextures")]
            public int baseTexture;
            public Color tint = Color.white;
            [Tooltip("Farbfeld im Editor-Fenster")]
            public Color swatch = new Color(0.8f, 0.6f, 0.45f);
        }

        /// <summary>M7: Gesicht = gemalte Ebene (Schatten, Falten …) + leichte Kopfform.</summary>
        [Serializable]
        public class FaceEntry
        {
            public string name = "Oval";
            public List<Texture2D> overlays = new List<Texture2D>();
            [Tooltip("Skalierung des Kopfknochens (Breite, Hoehe, Tiefe) – dezent halten")]
            public Vector3 headScale = Vector3.one;
        }

        [Serializable]
        public class OverlayEntry
        {
            public string name = "Keins";
            public List<Texture2D> overlays = new List<Texture2D>();
            public BodyVariantMask bodies = BodyVariantMask.All;
        }

        [Serializable]
        public class EyeEntry
        {
            public string name = "Braun";
            public Texture2D texture;
            public Color swatch = new Color(0.35f, 0.2f, 0.1f);
        }

        [Serializable]
        public class NamedColor
        {
            public string name;
            public Color color;
        }

        public List<BodyEntry> bodies = new List<BodyEntry>();
        public List<StyleEntry> hairstyles = new List<StyleEntry>();
        public List<StyleEntry> beards = new List<StyleEntry>();
        public List<NamedColor> hairColors = new List<NamedColor>();
        [Header("M7 Charakter-Editor")]
        public List<SkinTone> skinTones = new List<SkinTone>();
        public List<FaceEntry> faces = new List<FaceEntry>();
        public List<EyeEntry> eyeColors = new List<EyeEntry>();
        public List<OverlayEntry> features = new List<OverlayEntry>();
        [Tooltip("Durchsichtiges URP-Lit-Material fuer Gesichtsebenen (als Asset, damit die Shader-Variante im Build bleibt)")]
        public Material overlayTemplate;
        [Tooltip("Faerbepalette (Index 1..n; 0 = Standardfarbe des Gegenstands)")]
        public List<NamedColor> dyes = new List<NamedColor>();
        [Tooltip("URP-Lit-Vorlage fuer alle Ausruestungsmaterialien")]
        public Material litTemplate;
        [Tooltip("Animator fuer die Charaktervorschau")]
        public RuntimeAnimatorController previewController;
        [Tooltip("Ersatz, wenn ein Gegenstand kein (gueltiges) Aussehen hat – je Platzart")]
        public List<AppearanceDefinition> fallbacks = new List<AppearanceDefinition>();

        public BodyVariantMask VariantOf(int body) => body >= 0 && body < bodies.Count ? bodies[body].variant : BodyVariantMask.Male;

        public bool StyleAllowed(List<StyleEntry> list, int index, int body) =>
            index >= 0 && index < list.Count && (list[index].bodies & VariantOf(body)) != 0;

        public bool FeatureAllowed(int index, int body) =>
            index >= 0 && index < features.Count && (features[index].bodies & VariantOf(body)) != 0;

        /// <summary>Anzahl Hauttoene (neu: Farbtoene; alte Kataloge: Texturen des Koerpers).</summary>
        public int SkinCount(int body) =>
            skinTones.Count > 0 ? skinTones.Count : body >= 0 && body < bodies.Count ? Mathf.Max(1, bodies[body].skinTextures.Count) : 1;

        public string SkinName(int body, int index)
        {
            if (skinTones.Count > 0) return index >= 0 && index < skinTones.Count ? skinTones[index].name : "-";
            var names = body >= 0 && body < bodies.Count ? bodies[body].skinNames : null;
            return names != null && index >= 0 && index < names.Count ? names[index] : "-";
        }

        /// <summary>Ebene fuer diesen Koerper (null = keine).</summary>
        public static Texture2D OverlayFor(List<Texture2D> perBody, int body) =>
            perBody != null && body >= 0 && body < perBody.Count ? perBody[body] : null;

        public AppearanceDefinition Fallback(EquipType type)
        {
            foreach (AppearanceDefinition a in fallbacks) if (a != null && a.slot == type) return a;
            return null;
        }

        /// <summary>Werte auf gueltige Optionen begrenzen (Server). Gibt true zurueck, wenn etwas korrigiert wurde.</summary>
        public bool Sanitize(CharacterAppearanceData d)
        {
            bool changed = false;
            d.EnsureArrays();
            int Clamp(int v, int count, ref bool c)
            {
                int r = count <= 0 ? 0 : Mathf.Clamp(v, 0, count - 1);
                if (r != v) c = true;
                return r;
            }
            d.race = Clamp(d.race, RaceCatalog.All.Length, ref changed);
            // Nur waehlbare Voelker (Spieltest r2: nur Mensch, RaceCatalog.Playable) – sonst Mensch
            if (!RaceCatalog.IsPlayable(d.race)) { d.race = RaceCatalog.IndexOf(RaceCatalog.HumanId); changed = true; }
            d.body = Clamp(d.body, bodies.Count, ref changed);
            d.skin = Clamp(d.skin, SkinCount(d.body), ref changed);
            d.face = Clamp(d.face, Mathf.Max(1, faces.Count), ref changed);
            d.eyeColor = Clamp(d.eyeColor, Mathf.Max(1, eyeColors.Count), ref changed);
            if (d.feature != 0 && !FeatureAllowed(d.feature, d.body)) { d.feature = 0; changed = true; }
            if (!StyleAllowed(hairstyles, d.hair, d.body)) { d.hair = 0; changed = true; }
            if (!StyleAllowed(beards, d.beard, d.body)) { d.beard = 0; changed = true; }
            d.hairColor = Clamp(d.hairColor, Mathf.Max(1, hairColors.Count), ref changed);
            for (int i = 0; i < d.dyes.Length; i++)
            {
                DyeSet s = d.dyes[i];
                foreach (DyeChannel c in new[] { DyeChannel.Primary, DyeChannel.Secondary, DyeChannel.Detail })
                    if (s.Get(c) > dyes.Count) { s.Set(c, 0); changed = true; }
                d.dyes[i] = s;
            }
            return changed;
        }
    }
}
