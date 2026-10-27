using System;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kareth v4 – Entwurfsdaten aus dem Entwurfswerkzeug (claude/kareth_v4/export.py), gelesen mit JsonUtility.
    /// Weltmeter, Wachtfeuer = (0, 0), +z Nord. Höhen absolut (y), Lager unten = 0.
    /// </summary>
    [Serializable]
    public sealed class KarethV4Plan
    {
        public int version;
        public float x0, z0, size;
        public int res;
        public float baseY, rangeY;
        public int splatRes;
        public float seeY;
        public string[] layers;
        public KarethV4Weg[] wege;
        public float[] baeume;      // je 5: x, z, Art (0–2 Kiefer, 3 toter Baum), Größe, Drehung
        public float[] felsen;      // je 5: x, z, Art (0–2), Größe, Drehung (alt, Zufallsstreuung – seit 05.10. leer)
        /// <summary>Felsen nach Geologie (05.10.), je 12 Werte: x, z, dy über Gelände, Form (0 Block, 1 Platte, 2 Säule,
        /// 3 Kiesel, 4 Wand), Variante, sx, sy, sz, yaw, pitch, roll, Moosneigung. Gebaut mit <see cref="FelsKit"/>.</summary>
        public float[] felsenV2;
        public KarethV4Ort[] orte;
        public KarethV4Lager lager;
        public KarethV4Siedlung[] siedlungen;   // Dörfer und Höfe außerhalb des Lagers (Untersteg …)
        public KarethV4Blick[] blickpunkte;
        /// <summary>Schwellen-Bauwerke an den Ausgängen (Welle 0, Map Pass Teil 4): gebaut von <see cref="KarethV4Schwellen"/>.</summary>
        public KarethV4Schwelle[] schwellen;
    }

    /// <summary>Weg: xyz = Punktfolge (x, Höhe, z). steigung = größte geplante Steigung als Anteil (0,09 = 9 %) aus der
    /// Geländekette (Wegeklasse oder benannte Steilstelle, wege2.STEIL); 0 = ältere Daten ohne Angabe. Gelesen vom
    /// World Quality Validator (Editor/World/Quality).</summary>
    [Serializable] public sealed class KarethV4Weg { public string name, typ; public float breite; public float steigung; public float[] xyz; }
    [Serializable] public sealed class KarethV4Ort { public string id; public float x, z; }
    /// <summary>Schwelle am Ausgang: typ (dammbruch, granitstufen_steinschlag, zollturm_bergsturz, furt_wegwacht_tor,
    /// weidegatter_hangrutsch, bohlensteg_luecke), Weg, Lage, Richtung nach außen (yaw), Wegbreite, h = Dammkrone (nur Dammbruch).</summary>
    [Serializable] public sealed class KarethV4Schwelle { public string typ, weg, text; public float x, z, yaw, breite, h; }
    [Serializable] public sealed class KarethV4Blick { public string name; public float x, z, lx, ly, lz; }

    [Serializable]
    public sealed class KarethV4Lager
    {
        public KarethV4Wand[] waende;   // Wandabschnitte mit Bauart und Alter (Baugeschichte sichtbar)
        public float[] umriss;          // x, z, … (Bodenebnung, Bodenschicht)
        public KarethV4Tor[] tore;
        public KarethV4Gebaeude[] gebaeude;
        public KarethV4Bereich[] bereiche;
        public KarethV4Ausguck ausguck;
        public float[] stuetzmauer;     // x, z, …
        public float[] treppen;         // x-Positionen
        public float obenY, untenY;
        public float[] spawn;           // x, z
        public KarethV4Spur[] spuren;   // Environmental Storytelling
    }

    [Serializable] public sealed class KarethV4Siedlung { public string name; public KarethV4Gebaeude[] gebaeude; public KarethV4Bereich[] bereiche; public KarethV4Spur[] spuren; }
    [Serializable] public sealed class KarethV4Wand { public string art, alter; public float[] punkte; }
    [Serializable] public sealed class KarethV4Tor { public string name; public float x, z, yaw, breite; public bool tuerme; public string schild, art; }
    /// <summary>yaw = Blickrichtung der Vorderseite (Tür), 0 = Nord, 90 = Ost.
    /// Seit 05.10. (Fundamente statt Teller): boden = Fußbodenhöhe (Oberkante Sockel), fuss = tiefster Geländepunkt unter dem
    /// Grundriss, fundament = „eben“ | „hang“ (bergseitig eingeschnitten) | „untergeschoss“ (talseitig freies Geschoss) | „fels“.</summary>
    [Serializable] public sealed class KarethV4Gebaeude { public string id, name, typ; public float x, z, w, d, yaw; public bool oben; public float wand, sockel, boden, fuss; public string fundament; }
    /// <summary>yaw: Ausrichtung des Bereichs (Pferch: Richtung des Gatters, Markt: Blick der Stände).</summary>
    [Serializable] public sealed class KarethV4Bereich { public string id, typ; public float x, z, r, w, d, yaw; public int anzahl; }
    [Serializable] public sealed class KarethV4Ausguck { public float x, z, hoehe; }
    /// <summary>Spur (Environmental Storytelling): typ + Lage/Linie + Parameter. Jede Spur hat eine Weltlogik (Plan lager.py).</summary>
    [Serializable] public sealed class KarethV4Spur { public string typ; public float x, z, r, w, d, h, yaw, gesetzt, breite; public int anzahl, kerben; public float[] linie; }
}
