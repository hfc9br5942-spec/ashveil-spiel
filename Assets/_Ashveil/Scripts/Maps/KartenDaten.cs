using System;

namespace Ashveil.Maps
{
    // Datenklassen für karten_unity.json (claude/welt/spielkarten.py, für r2 aus claude/karten_r2/karten_r2.py) und die
    // Szenenraster (szenen/<name>.json). JsonUtility-tauglich: keine verschachtelten Listen, Punktlisten sind flach
    // (x0, z0, x1, z1, …). Weltkoordinaten in Metern: x nach Osten, z nach Norden, Ursprung Südwestecke.

    [Serializable]
    public sealed class KartenDatei
    {
        public int version;
        public string stand;
        public float welt_breite_m, welt_hoehe_m;
        /// <summary>Kantenlänge einer Zelle des Weltrasters (zonen.bytes, unterzonen.bytes, aufdeckung.bytes) in Metern.</summary>
        public float raster_m;
        public int raster_breite, raster_hoehe;
        public float kueste_m;
        public string meer_name, meer_pvp;
        public int weltkarte_breite, weltkarte_hoehe;
        public float minimap_kachel_m;
        public int minimap_kachel_px;
        /// <summary>Vorhandene Minimap-Kacheln als Paare (i, j): i nach Osten, j nach Norden.</summary>
        public int[] minimap_kacheln_ij;
        public int gebietskarte_breite, gebietskarte_hoehe;
        public ZoneDaten[] zonen;
        public SchiffDaten[] schiffe;
    }

    [Serializable]
    public sealed class ZoneDaten
    {
        public string key;
        /// <summary>Index im Zonenraster (1 … 45); 0 ist die offene See.</summary>
        public int index;
        public string name;
        public int stufe_von, stufe_bis;
        /// <summary>A, B = Heimat dieser Seite; S = geteilt/umkämpft; N = neutral.</summary>
        public string seite;
        public string pvp;
        public float flaeche_km2;
        /// <summary>Kartenrahmen der Gebietskarte (x0, z0, x1, z1) in Welt-m.</summary>
        public float[] rahmen;
        public float[] umriss_xz;
        /// <summary>Namenspunkt für die Weltkarte (innerster Punkt der größten Teilfläche).</summary>
        public float name_x, name_z;
        public string farbe_haupt, farbe_zweit, farbe_akzent;
        public string wunder;
        public int fremdheit;
        public UnterzoneDaten[] unterzonen;
        public OrtDaten[] orte;
        public PunktDaten[] flugpunkte;
        public InstanzDaten[] instanzen;
        public UebergangDaten[] uebergaenge;

        public string Stufentext => stufe_bis > 0 ? (stufe_von == stufe_bis ? $"{stufe_von}" : $"{stufe_von}–{stufe_bis}") : "";
    }

    [Serializable]
    public sealed class UnterzoneDaten
    {
        /// <summary>Weltweit eindeutige Nummer (1 … n), wie in den Rastern (Welt und Szene).</summary>
        public int id;
        public string name;
        public int stufe_von, stufe_bis;
        public string typ;
        public float x, z, radius_m;
        /// <summary>Ortsteile ohne eigene Fläche (gleiche Mitte), die mit dieser Unterzone entdeckt werden.</summary>
        public int[] mit;
    }

    [Serializable]
    public sealed class OrtDaten
    {
        public string name, typ, seite;
        public float x, z;
        public string[] dienste;

        public bool HatDienst(string d)
        {
            if (dienste == null) return false;
            for (int i = 0; i < dienste.Length; i++) if (dienste[i] == d) return true;
            return false;
        }

        /// <summary>Kartenpunkt? Ruinen ohne Dienste sind Orte der Welt, keine Kartenpunkte.</summary>
        public bool Kartenpunkt => typ != "RUINE" || (dienste != null && dienste.Length > 0);

        /// <summary>Größerer Punkt für Städte, Dörfer und Lager.</summary>
        public bool Gross => typ == "CAP" || typ == "TOWN" || typ == "VIL" || typ == "CAMP";
    }

    [Serializable] public sealed class PunktDaten { public string name; public float x, z; }
    [Serializable] public sealed class InstanzDaten { public string name, stufen; public float x, z; }
    [Serializable] public sealed class UebergangDaten { public string nach, typ; public float x, z; }
    [Serializable] public sealed class SchiffDaten { public string linie, seite; public float[] punkte_xz; }

    /// <summary>
    /// Szenenraster einer gebauten Szene (r2: Kareth v4), szenen/&lt;name&gt;.json mit &lt;name&gt;_unterzonen.bytes: Unterzonen
    /// so, wie die Szene gebaut ist (die Weltdaten planen ein kleineres Kareth). Raster in Welt-m ab (x0, z0), Zeile 0 = Süden.
    /// </summary>
    [Serializable]
    public sealed class SzenenRasterDatei
    {
        public string name, zone_key, hinweis;
        public float x0, z0, zelle_m;
        public int breite, hoehe;
        /// <summary>Unterzone des Startorts – gilt beim ersten Anmelden als bekannt (ohne Entdeckungs-EP).</summary>
        public int start_unterzone;
        public float weltversatz_x, weltversatz_z;
        /// <summary>Namenspunkte der Gebietskarte: (Nummer, x, z) je Unterzone in Welt-m.</summary>
        public float[] namen;
    }

    /// <summary>
    /// Führung der Aufträge einer Szene (Quest Design Bible §4, UI-202; Max 10.10.2026: Liste A/B/C freigegeben, Kreise auch auf
    /// der Minimap): szenen/&lt;name&gt;_fuehrung.json. Stufe A = weiche Zielkreise um die Zielgebiete (Welt-m, Mitte versetzt),
    /// B = nur die Unterzone, C = nichts. Reine Anzeige am Client; der Server kennt die Datei nicht.
    /// </summary>
    [Serializable]
    public sealed class FuehrungDatei
    {
        public int version;
        public string stand, szene;
        public FuehrungAuftrag[] auftraege;
    }

    [Serializable]
    public sealed class FuehrungAuftrag
    {
        /// <summary>Kennung des Auftrags (quest.*).</summary>
        public string quest;
        /// <summary>"A", "B" oder "C".</summary>
        public string stufe;
        /// <summary>A: Kreise der offenen Ziele. ziel = Nummer des Ziels (0 …), −1 = alle Ziele.</summary>
        public FuehrungKreis[] kreise;
        /// <summary>A: Kreis um die Abgabefigur, sobald alle Ziele erfüllt sind (radius 0 = keiner).</summary>
        public FuehrungKreis abgabe;
        /// <summary>B: Nummer der Unterzone (wie im Szenenraster); 0 = keine.</summary>
        public int unterzone;
    }

    [Serializable]
    public sealed class FuehrungKreis
    {
        public int ziel = -1;
        public float x, z, radius;
    }
}
