using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ashveil.Maps
{
    /// <summary>
    /// Gebiete und Unterzonen an einer Weltposition. Reines Nachschlagen ohne Szene und ohne Oberfläche – Server und Client
    /// benutzen dieselbe Klasse (Server: Entdeckungs-EP, PROG-013; Client: Banner, Minimap, Karte).
    ///
    /// Weltraster (16 m, aus claude/welt/spielkarten.py): Zeile 0 = Süden, Spalte 0 = Westen; zonen.bytes 1 Byte je Zelle
    /// (0 = offene See), unterzonen.bytes und aufdeckung.bytes 2 Byte (0 = keine). Die beiden Unterzonen-Raster sind
    /// freiwillig: Im Spieltest r2 gibt es Unterzonen nur im Szenenraster der gebauten Szene (<see cref="LadeSzene"/>).
    /// Im Rechteck des Szenenrasters gilt die Szene: Gebiet = Gebiet der Szene, Unterzone und Aufdeckung = Szenenraster.
    /// </summary>
    public sealed class ZonenDienst
    {
        public const string Datei = "karten_unity.json";
        /// <summary>Schlüssel einer Unterzone im Speicherstand (discoveredAreas): „uz.&lt;Gebiet&gt;.&lt;Nummer&gt;“.</summary>
        public const string SchluesselPraefix = "uz.";

        public KartenDatei Daten { get; private set; }
        public string Ordner { get; private set; }
        /// <summary>Szenenraster der geladenen Szene (null = nur Weltraster).</summary>
        public SzenenRasterDatei Szene { get; private set; }
        /// <summary>Gebiet des Szenenrasters (null ohne Szenenraster).</summary>
        public ZoneDaten SzenenZone { get; private set; }

        /// <summary>Weitere Szenenraster (13.10.2026: Weltzellen wie die Westweiden neben Kareth v4) – gleiche Regeln wie das erste.</summary>
        private readonly List<(SzenenRasterDatei sz, ZoneDaten zone, ushort[] raster)> _weitere = new List<(SzenenRasterDatei, ZoneDaten, ushort[])>();
        public IEnumerable<SzenenRasterDatei> WeitereSzenen { get { foreach (var w in _weitere) yield return w.sz; } }

        private byte[] _zonen;
        private ushort[] _unter;
        private ushort[] _auf;
        private ushort[] _szene;
        private int _w, _h;
        private float _res;
        private readonly Dictionary<int, ZoneDaten> _zoneNachIndex = new Dictionary<int, ZoneDaten>();
        private readonly Dictionary<string, ZoneDaten> _zoneNachKey = new Dictionary<string, ZoneDaten>();
        private readonly Dictionary<int, UnterzoneDaten> _unterNachId = new Dictionary<int, UnterzoneDaten>();
        private readonly Dictionary<int, ZoneDaten> _zoneDerUnterzone = new Dictionary<int, ZoneDaten>();
        private readonly Dictionary<int, Vector2> _namenspunkt = new Dictionary<int, Vector2>();
        private HashSet<long> _kacheln;

        public IEnumerable<ZoneDaten> Zonen => _zoneNachIndex.Values;
        public int AnzahlUnterzonen => _unterNachId.Count;
        public bool HatWeltUnterzonen => _unter != null;

        /// <summary>Lädt Gebiete und Weltraster aus einem Ordner (z. B. StreamingAssets/Ashveil/Karten). Null mit Meldung, wenn etwas fehlt.</summary>
        public static ZonenDienst Laden(string ordner, out string fehler)
        {
            fehler = null;
            try
            {
                string json = Path.Combine(ordner, Datei);
                if (!File.Exists(json)) { fehler = "Kartendaten fehlen: " + json; return null; }
                var d = JsonUtility.FromJson<KartenDatei>(File.ReadAllText(json));
                if (d == null || d.zonen == null || d.raster_breite <= 0 || d.raster_hoehe <= 0) { fehler = "Kartendaten unvollständig: " + json; return null; }
                var s = new ZonenDienst { Daten = d, Ordner = ordner, _w = d.raster_breite, _h = d.raster_hoehe, _res = d.raster_m > 0f ? d.raster_m : 16f };
                int n = s._w * s._h;
                string zonen = Path.Combine(ordner, "zonen.bytes");
                if (!File.Exists(zonen)) { fehler = "Fehlt: " + zonen; return null; }
                s._zonen = File.ReadAllBytes(zonen);
                if (s._zonen.Length != n) { fehler = $"zonen.bytes: {s._zonen.Length} statt {n} Bytes"; return null; }
                // Unterzonen und Aufdeckung im Weltraster: freiwillig (r2: nur Szenenraster)
                s._unter = LiesU16(Path.Combine(ordner, "unterzonen.bytes"), n, ref fehler);
                s._auf = LiesU16(Path.Combine(ordner, "aufdeckung.bytes"), n, ref fehler);
                if (fehler != null) return null;
                foreach (ZoneDaten z in d.zonen)
                {
                    if (z == null) continue;
                    s._zoneNachIndex[z.index] = z;
                    if (!string.IsNullOrEmpty(z.key)) s._zoneNachKey[z.key] = z;
                    if (z.unterzonen == null) continue;
                    foreach (UnterzoneDaten u in z.unterzonen)
                    {
                        if (u == null) continue;
                        s._unterNachId[u.id] = u;
                        s._zoneDerUnterzone[u.id] = z;
                    }
                }
                return s;
            }
            catch (Exception e)
            {
                fehler = "Kartendaten nicht lesbar: " + e.Message;
                return null;
            }
        }

        /// <summary>Raster mit 2 Byte je Zelle; fehlt die Datei: null ohne Fehler. Falsche Größe: Fehler.</summary>
        private static ushort[] LiesU16(string pfad, int n, ref string fehler)
        {
            if (!File.Exists(pfad)) return null;
            byte[] b = File.ReadAllBytes(pfad);
            if (b.Length != n * 2)
            {
                fehler = $"{Path.GetFileName(pfad)}: {b.Length} statt {n * 2} Bytes";
                return null;
            }
            var r = new ushort[n];
            Buffer.BlockCopy(b, 0, r, 0, b.Length);          // little endian wie geschrieben (x86/x64, ARM)
            return r;
        }

        /// <summary>
        /// Szenenraster laden (szenen/&lt;name&gt;.json und &lt;name&gt;_unterzonen.bytes). Danach gilt im Rechteck der Szene deren Gebiet
        /// und Raster. False mit Meldung, wenn es fehlt oder nicht zu den Gebieten passt.
        /// </summary>
        public bool LadeSzene(string name, out string fehler)
        {
            fehler = null;
            try
            {
                string json = Path.Combine(Ordner, "szenen", name + ".json");
                string bytes = Path.Combine(Ordner, "szenen", name + "_unterzonen.bytes");
                if (!File.Exists(json) || !File.Exists(bytes)) { fehler = "Szenenraster fehlt: " + json; return false; }
                var sz = JsonUtility.FromJson<SzenenRasterDatei>(File.ReadAllText(json));
                if (sz == null || sz.breite <= 0 || sz.hoehe <= 0 || sz.zelle_m <= 0f) { fehler = "Szenenraster unvollständig: " + json; return false; }
                ZoneDaten zone = ZoneNachKey(sz.zone_key);
                if (zone == null) { fehler = $"Szenenraster {name}: Gebiet „{sz.zone_key}“ fehlt in {Datei}"; return false; }
                string f = null;
                ushort[] raster = LiesU16(bytes, sz.breite * sz.hoehe, ref f);
                if (raster == null) { fehler = f ?? ("Fehlt: " + bytes); return false; }
                Szene = sz;
                SzenenZone = zone;
                _szene = raster;
                _namenspunkt.Clear();
                if (sz.namen != null)
                    for (int i = 0; i + 2 < sz.namen.Length; i += 3)
                        _namenspunkt[Mathf.RoundToInt(sz.namen[i])] = new Vector2(sz.namen[i + 1], sz.namen[i + 2]);
                return true;
            }
            catch (Exception e)
            {
                fehler = "Szenenraster nicht lesbar: " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// Weiteres Szenenraster laden (szenen/&lt;name&gt;.json und _unterzonen.bytes), z. B. die Westweiden-Zelle neben Kareth v4. Im
        /// Rechteck dieses Rasters gilt dessen Gebiet und Raster; das erste Szenenraster (<see cref="Szene"/>) bleibt unverändert.
        /// </summary>
        public bool LadeWeitereSzene(string name, out string fehler)
        {
            fehler = null;
            try
            {
                string json = Path.Combine(Ordner, "szenen", name + ".json");
                string bytes = Path.Combine(Ordner, "szenen", name + "_unterzonen.bytes");
                if (!File.Exists(json) || !File.Exists(bytes)) { fehler = "Szenenraster fehlt: " + json; return false; }
                var sz = JsonUtility.FromJson<SzenenRasterDatei>(File.ReadAllText(json));
                if (sz == null || sz.breite <= 0 || sz.hoehe <= 0 || sz.zelle_m <= 0f) { fehler = "Szenenraster unvollständig: " + json; return false; }
                ZoneDaten zone = ZoneNachKey(sz.zone_key);
                if (zone == null) { fehler = $"Szenenraster {name}: Gebiet „{sz.zone_key}“ fehlt in {Datei}"; return false; }
                string f = null;
                ushort[] raster = LiesU16(bytes, sz.breite * sz.hoehe, ref f);
                if (raster == null) { fehler = f ?? ("Fehlt: " + bytes); return false; }
                _weitere.RemoveAll(w => w.sz.name == sz.name);
                _weitere.Add((sz, zone, raster));
                if (sz.namen != null)
                    for (int i = 0; i + 2 < sz.namen.Length; i += 3)
                        _namenspunkt[Mathf.RoundToInt(sz.namen[i])] = new Vector2(sz.namen[i + 1], sz.namen[i + 2]);
                return true;
            }
            catch (Exception e)
            {
                fehler = "Szenenraster nicht lesbar: " + e.Message;
                return false;
            }
        }

        /// <summary>Weiteres Szenenraster an der Stelle: Index der Zelle und das Raster; −1, wenn keins.</summary>
        private int WeitereZelle(float x, float z, out int welches)
        {
            for (int k = 0; k < _weitere.Count; k++)
            {
                SzenenRasterDatei sz = _weitere[k].sz;
                int c = Mathf.FloorToInt((x - sz.x0) / sz.zelle_m), r = Mathf.FloorToInt((z - sz.z0) / sz.zelle_m);
                if (c < 0 || r < 0 || c >= sz.breite || r >= sz.hoehe) continue;
                welches = k;
                return r * sz.breite + c;
            }
            welches = -1;
            return -1;
        }

        private int Zelle(float x, float z)
        {
            int c = Mathf.FloorToInt(x / _res), r = Mathf.FloorToInt(z / _res);
            if (c < 0 || r < 0 || c >= _w || r >= _h) return -1;
            return r * _w + c;
        }

        private int SzenenZelle(float x, float z)
        {
            if (_szene == null) return -1;
            int c = Mathf.FloorToInt((x - Szene.x0) / Szene.zelle_m), r = Mathf.FloorToInt((z - Szene.z0) / Szene.zelle_m);
            if (c < 0 || r < 0 || c >= Szene.breite || r >= Szene.hoehe) return -1;
            return r * Szene.breite + c;
        }

        /// <summary>Liegt die Stelle im Szenenraster (dort gilt die gebaute Szene)?</summary>
        public bool InSzene(float x, float z) => SzenenZelle(x, z) >= 0 || WeitereZelle(x, z, out _) >= 0;

        /// <summary>Gebietsindex an der Stelle (0 = offene See oder außerhalb).</summary>
        public int ZonenIndexBei(float x, float z)
        {
            if (SzenenZelle(x, z) >= 0) return SzenenZone.index;
            if (WeitereZelle(x, z, out int k) >= 0) return _weitere[k].zone.index;
            int i = Zelle(x, z);
            return i < 0 ? 0 : _zonen[i];
        }

        /// <summary>Gebietsindex nur aus dem Weltraster (ohne Szenenraster) – für die Weltkarte, deren Zeichnung die geplanten Grenzen zeigt.</summary>
        public int WeltZonenIndexBei(float x, float z)
        {
            int i = Zelle(x, z);
            return i < 0 ? 0 : _zonen[i];
        }

        /// <summary>Gebiet an der Stelle; null auf offener See.</summary>
        public ZoneDaten ZoneBei(float x, float z)
        {
            int idx = ZonenIndexBei(x, z);
            return idx > 0 && _zoneNachIndex.TryGetValue(idx, out ZoneDaten zd) ? zd : null;
        }

        /// <summary>Benannte Unterzone an der Stelle (Banner, Entdeckung); null, wenn keine.</summary>
        public UnterzoneDaten UnterzoneBei(float x, float z)
        {
            int id;
            int s = SzenenZelle(x, z);
            int k = -1;
            int w = s >= 0 ? -1 : WeitereZelle(x, z, out k);
            if (s >= 0) id = _szene[s];
            else if (w >= 0) id = _weitere[k].raster[w];
            else
            {
                if (_unter == null) return null;
                int i = Zelle(x, z);
                if (i < 0) return null;
                id = _unter[i];
            }
            return id > 0 && _unterNachId.TryGetValue(id, out UnterzoneDaten u) ? u : null;
        }

        /// <summary>Unterzone, deren Kartenfläche die Stelle aufdeckt; 0 = keine (dann ist die Stelle nie verhüllt).</summary>
        public int AufdeckungBei(float x, float z)
        {
            int s = SzenenZelle(x, z);
            if (s >= 0) return _szene[s];
            int w = WeitereZelle(x, z, out int k);
            if (w >= 0) return _weitere[k].raster[w];
            if (_auf == null) return 0;
            int i = Zelle(x, z);
            return i < 0 ? 0 : _auf[i];
        }

        public ZoneDaten ZoneNachIndex(int index) => _zoneNachIndex.TryGetValue(index, out ZoneDaten z) ? z : null;
        public ZoneDaten ZoneNachKey(string key) => key != null && _zoneNachKey.TryGetValue(key, out ZoneDaten z) ? z : null;
        public UnterzoneDaten UnterzoneNachId(int id) => _unterNachId.TryGetValue(id, out UnterzoneDaten u) ? u : null;
        public ZoneDaten ZoneDerUnterzone(int id) => _zoneDerUnterzone.TryGetValue(id, out ZoneDaten z) ? z : null;

        /// <summary>Wo der Name einer Unterzone auf der Gebietskarte steht (Szenenraster: innerster Punkt der Fläche, sonst Mitte).</summary>
        public Vector2 Namenspunkt(UnterzoneDaten u) =>
            u == null ? Vector2.zero : _namenspunkt.TryGetValue(u.id, out Vector2 p) ? p : new Vector2(u.x, u.z);

        /// <summary>Gibt es eine Minimap-Kachel (i nach Osten, j nach Norden)?</summary>
        public bool HatMinimapKachel(int i, int j)
        {
            if (_kacheln == null)
            {
                _kacheln = new HashSet<long>();
                int[] ij = Daten.minimap_kacheln_ij;
                if (ij != null) for (int k = 0; k + 1 < ij.Length; k += 2) _kacheln.Add(((long)ij[k] << 32) | (uint)ij[k + 1]);
            }
            return _kacheln.Contains(((long)i << 32) | (uint)j);
        }

        // ---------------------------------------------------------------- Schlüssel im Speicherstand

        /// <summary>„uz.&lt;Gebiet&gt;.&lt;Nummer&gt;“, z. B. „uz.kareth.2“ (Speicherstand: discoveredAreas, wie die Quest-Orte).</summary>
        public static string Schluessel(ZoneDaten z, UnterzoneDaten u) =>
            u == null ? null : SchluesselPraefix + (z != null && !string.IsNullOrEmpty(z.key) ? z.key : "welt") + "." + u.id;

        /// <summary>Unterzone zu einem Schlüssel; null, wenn er keine Unterzone meint oder nicht mehr zu den Daten passt.</summary>
        public UnterzoneDaten AusSchluessel(string key)
        {
            if (string.IsNullOrEmpty(key) || !key.StartsWith(SchluesselPraefix, StringComparison.Ordinal)) return null;
            int punkt = key.LastIndexOf('.');
            if (punkt <= SchluesselPraefix.Length || !int.TryParse(key.Substring(punkt + 1), out int id)) return null;
            UnterzoneDaten u = UnterzoneNachId(id);
            ZoneDaten z = ZoneDerUnterzone(id);
            string gebiet = key.Substring(SchluesselPraefix.Length, punkt - SchluesselPraefix.Length);
            return u != null && z != null && z.key == gebiet ? u : null;
        }

        /// <summary>PvP-Einordnung eines Gebiets aus Sicht einer Seite ("A" oder "B"), wie das Zonenbanner in Classic.</summary>
        public static PvpArt PvpFuer(ZoneDaten z, string spielerSeite)
        {
            if (z == null) return PvpArt.Neutral;                           // offene See: neutral, kein PvP (WORLD-227, Max 05.10.)
            switch (z.seite)
            {
                case "A":
                case "B": return z.seite == spielerSeite ? PvpArt.Freundlich : PvpArt.Feindlich;
                case "N": return PvpArt.Zuflucht;
                default: return PvpArt.Umkaempft;
            }
        }
    }

    public enum PvpArt { Freundlich, Feindlich, Umkaempft, Zuflucht, Neutral }
}
