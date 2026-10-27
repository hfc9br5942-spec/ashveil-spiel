using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Auftrag „nahkampf_tempo“ (21.10.2026 nachts, Max: Angriffe „zu sehr wie beim Fechten, zu wenig Ritter“, „die Geschwindigkeit
    /// der Angriffe sieht nicht richtig aus“): Das Ausholen der Nahkampfwaffen war 0,10–0,25 s – kein Mensch kann darauf reagieren
    /// (Reaktionszeit ~0,25 s), und ein Ritterhieb hat kein sichtbares Ausholen. Kanon (Animationskatalog v4 §16: Kontakt Bild 13–16 von
    /// 32 ≈ 0,43–0,53 s) und Recherche (SourceArt/Referenz/Angriffe/RECHERCHE_ANGRIFFE.md: Ausholen 0,25–0,35 s, Zweihand bis 0,6 s)
    /// verlangen mehr. Neu: leichter Hieb 0,30 s, Rueckhand 0,28 s, schwerer Hieb 0,40 s, Spezial 0,45 s (je mal Tempofaktor der Waffe)
    /// – die Erholung wird um genau so viel kuerzer, der ZYKLUS (und damit Schaden pro Sekunde) bleibt gleich.
    /// Wirkt auf alle Waffen mit MeleeArc; zweimal ausfuehren aendert nichts (nur, wo das Ausholen kuerzer ist).
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragNahkampfTempo
    {
        // Basiswerte (Tempo 1 = Schwert) vorher → nachher
        private static readonly float[] AltComboWindup = { 0.12f, 0.10f, 0.20f };
        private static readonly float[] NeuComboWindup = { 0.30f, 0.28f, 0.40f };
        private const float AltSpezial = 0.25f, NeuSpezial = 0.45f;

        static AuftragNahkampfTempo()
        {
            Auftragsordner.Registrieren("nahkampf_tempo", (a, fertig) => fertig(true, Anwenden()), false, 120);
        }

        public static string Anwenden()
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponDefinition"))
            {
                var w = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (w == null || w.archetype != WeaponArchetype.MeleeArc) continue;
                bool changed = false;
                if (w.combo != null)
                    for (int i = 0; i < w.combo.Length && i < 3; i++)
                        changed |= Strecken(ref w.combo[i], AltComboWindup[i], NeuComboWindup[i]);
                changed |= Strecken(ref w.special, AltSpezial, NeuSpezial);
                if (!changed) continue;
                EditorUtility.SetDirty(w);
                n++;
                sb.Append(w.name).Append(": ");
                if (w.combo != null) foreach (AttackStep s in w.combo) sb.Append(F(s.windup)).Append('/').Append(F(s.recovery)).Append(' ');
                sb.Append("Spezial ").Append(F(w.special.windup)).Append('/').Append(F(w.special.recovery)).Append("; ");
            }
            AssetDatabase.SaveAssets();
            return n + " Nahkampfwaffen angepasst (Ausholen/Erholung in s): " + sb;
        }

        private static bool Strecken(ref AttackStep s, float alt, float neu)
        {
            // schon angepasst: vorher war das Ausholen immer kuerzer als die Erholung (0,12/0,30 …), nachher laenger (0,30/0,12 …)
            if (s.windup <= 0f || s.windup > s.recovery) return false;
            float tempo = s.windup / alt;                 // Tempofaktor der Waffe (Dolch 0,7, Axt 1,35 …)
            float ziel = neu * tempo;
            float zyklus = s.windup + s.recovery;
            s.windup = ziel;
            s.recovery = Mathf.Max(0.1f, zyklus - ziel);
            return true;
        }

        private static string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
