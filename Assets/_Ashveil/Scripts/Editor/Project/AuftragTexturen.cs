using System;
using UnityEditor;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Texturen aus Max' Paket (07.10.2026; Max: „Bei den Texturen die ich dir gegeben habe sind die Details nicht mehr erkennbar.
    /// Kannst du die beibehalten?“ und „Erstelle aus den Texturen immer zum Stil und Spiel passende Versionen falls nötig“) –
    /// zwei Aufträge für den Auftragsordner:
    ///   „texturen“      „paket“ (Standard): Texturen_Paket (BaseColor = Max' Bild, passende Karten abgeleitet) einrichten und
    ///                   allen Materialien und Terrain-Layern zuweisen; „prozedural“: Sets vom 04.10. (Vergleich, Rückweg);
    ///                   „pruefen“: nur aktive Quelle, Größen, Formate, Speicher melden.
    ///   „texturbilder“  Nahbilder je Bodenschicht und Gebäudematerial aus Kareth v4 nach Logs/Ashveil/KarethV4/&lt;Parameter&gt;.
    ///   „bodenkachel“   Faktor auf die Bodenkacheln der PBR-Terrain-Layer (Terrain V2, Max 08.10.2026 „r2, Kachel 3,5 m“):
    ///                   leer oder „1.75“ = 3,5 m (Asche, Salz 1,75 m); „1“ = bisher 2 m / 1 m (umkehrbar).
    /// </summary>
    [InitializeOnLoad]
    public static class AuftragTexturen
    {
        static AuftragTexturen()
        {
            Auftragsordner.Registrieren("texturen", Texturen, true, 1800);
            Auftragsordner.Registrieren("texturbilder", Texturbilder, true, 1800);
            Auftragsordner.Registrieren("bodenkachel", Bodenkachel, true, 600);
        }

        private static void Bodenkachel(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                fertig(false, "Play läuft – nichts geändert");
                return;
            }
            string p = (a.parameter ?? "").Trim().Replace(',', '.');
            float f = WeltMaterialtest.BodenKachelStandard;
            if (p.Length > 0 && (!float.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) || f < 0.25f || f > 8f))
            {
                fertig(false, $"Parameter „{a.parameter}“ nicht verstanden – erlaubt: Faktor 0.25 … 8 (leer = {WeltMaterialtest.BodenKachelStandard})");
                return;
            }
            string m = WeltMaterialtest.BodenKachelSetzen(f);
            fertig(!m.Contains("nicht im Play") && !m.Contains(" 0 PBR"), m);
        }

        private static void Texturen(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string p = (a.parameter ?? "").Trim().ToLowerInvariant();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                fertig(false, "Play läuft – nichts geändert");
                return;
            }
            switch (p)
            {
                case "":
                case "pruefen":
                    fertig(true, WeltMaterialtest.TexturBericht());
                    return;
                case "paket":
                case "prozedural":
                    string m = WeltMaterialtest.TexturenAnwenden(p == "paket");
                    fertig(!m.Contains("fehlt") && !m.Contains("nicht im Play"), m);
                    return;
                default:
                    fertig(false, "erlaubt: paket, prozedural, pruefen");
                    return;
            }
        }

        private static void Texturbilder(Auftragsordner.Auftrag a, Action<bool, string> fertig)
        {
            string unter = string.IsNullOrWhiteSpace(a.parameter) ? null : a.parameter.Trim();
            if (unter != null)
                foreach (char c in System.IO.Path.GetInvalidFileNameChars()) unter = unter.Replace(c, '_');
            string dir = KarethV4Shots.RenderNah(unter);
            fertig(dir != null, dir != null ? "Nahbilder in " + dir : "keine Nahbilder (Kareth v4 fehlt oder Play läuft)");
        }
    }
}
