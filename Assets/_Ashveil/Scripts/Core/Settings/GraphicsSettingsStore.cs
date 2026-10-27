using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Ashveil.Core.Settings
{
    /// <summary>
    /// Haelt die aktuellen Grafikeinstellungen, speichert sie pro Rechner (Datei "grafik.json" im Benutzer-Datenordner
    /// von Unity, nicht im Projekt) und meldet Aenderungen. Beim allerersten Start wird die Stufe anhand der
    /// Grafikkarte gewaehlt (<see cref="DetectTier"/>).
    /// </summary>
    public static class GraphicsSettingsStore
    {
        private static GraphicsOptions _current;

        /// <summary>Nach jeder Aenderung (auch Laden). Anwender: GraphicsApplier, CharacterLod, Einstellungsfenster.</summary>
        public static event Action<GraphicsOptions> Changed;

        /// <summary>Tests koennen das Speichern abschalten, damit die Einstellung des Spielers unberuehrt bleibt.</summary>
        public static bool PersistenceEnabled = true;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "grafik.json");

        /// <summary>Warum diese Stufe beim ersten Start gewaehlt wurde (fuer Fenster und Testbericht).</summary>
        public static string DetectionNote { get; private set; } = "";

        public static GraphicsOptions Current
        {
            get
            {
                if (_current == null) Load();
                return _current;
            }
        }

        public static void Load()
        {
            GraphicsOptions loaded = null;
            try
            {
                if (PersistenceEnabled && File.Exists(FilePath)) loaded = JsonUtility.FromJson<GraphicsOptions>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ashveil] Grafikeinstellungen nicht lesbar, nehme Voreinstellung: " + e.Message);
            }
            if (loaded == null)
            {
                GraphicsTier tier = DetectTier(SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize, out string note);
                DetectionNote = note;
                loaded = GraphicsCatalog.Preset(tier);
            }
            else
            {
                // Datei-Version 1 kannte "Episch" nicht (und Benutzerdefiniert war 3): Stufe aus den Werten neu bestimmen.
                int oldVersion = loaded.version;
                // Version 3 bringt "Globale Beleuchtung": gespeicherte Stufe Hoch/Episch bekommt sie wie die Voreinstellung.
                if (oldVersion < 3)
                    loaded.globalIllumination = oldVersion >= 2 && (loaded.tier == GraphicsTier.Hoch || loaded.tier == GraphicsTier.Episch) ? 1 : 0;
                loaded.Clamp();
                loaded.tier = GraphicsCatalog.Match(loaded);
                // Wer bisher "Hoch" hatte und eine Karte der Episch-Klasse besitzt, bekommt einmalig Episch
                // (Anzeige-Werte wie Vollbild, VSync, Bildrate und Skalierung bleiben).
                if (oldVersion < 2 && loaded.tier == GraphicsTier.Hoch &&
                    DetectTier(SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize, out string up) == GraphicsTier.Episch)
                {
                    GraphicsOptions epic = GraphicsCatalog.Preset(GraphicsTier.Episch);
                    epic.CopyDisplay(loaded);
                    loaded = epic;
                    DetectionNote = up + " (einmalig von Hoch auf Episch umgestellt)";
                }
                // Version 4 (Visual Clarity Gate 02.10.): "Vollbild" wird "Randloses Vollbild" in nativer Aufloesung
                if (oldVersion < 4)
                {
                    loaded.displayMode = loaded.fullscreen ? 1 : 0;
                    loaded.resolutionWidth = loaded.resolutionHeight = 0;
                }
                if (oldVersion < 4)
                {
                    loaded.version = 4;
                    loaded.Clamp();
                    _current = loaded;
                    Save();
                }
            }
            loaded.Clamp();
            _current = loaded;
            Changed?.Invoke(_current);
        }

        /// <summary>Neue Werte uebernehmen, Stufe neu bestimmen, speichern und anwenden.</summary>
        public static void Set(GraphicsOptions options)
        {
            GraphicsOptions o = options.Clone();
            o.Clamp();
            o.tier = GraphicsCatalog.Match(o);
            _current = o;
            Save();
            Changed?.Invoke(_current);
        }

        /// <summary>Stufe waehlen – die Anzeige-Werte (Vollbild, VSync, Bildrate, Skalierung) bleiben erhalten.</summary>
        public static void SetTier(GraphicsTier tier)
        {
            if (tier == GraphicsTier.Benutzerdefiniert) return;
            GraphicsOptions keep = Current;
            GraphicsOptions p = GraphicsCatalog.Preset(tier);
            p.CopyDisplay(keep);
            p.blood = keep.blood;
            p.damageNumbers = keep.damageNumbers;
            p.enemyBars = keep.enemyBars;
            Set(p);
        }

        public static void Save()
        {
            if (!PersistenceEnabled || _current == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(_current, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ashveil] Grafikeinstellungen nicht gespeichert: " + e.Message);
            }
        }

        /// <summary>
        /// Stufe aus dem Namen der Grafikkarte (NVIDIA/AMD/Intel), sonst aus dem Grafikspeicher.
        /// RTX 4090, 5080, 5090 = Episch · RTX 50xx und 4070+ = Hoch · RTX 3060 Ti–3080, 4060/4060 Ti, RX 6700–7700 = Mittel ·
        /// darunter Niedrig. AMD RX 7900 XTX / 9070 XT = Episch.
        /// </summary>
        public static GraphicsTier DetectTier(string deviceName, int vramMb, out string note)
        {
            string n = (deviceName ?? "").ToUpperInvariant();
            Match m = Regex.Match(n, @"RTX\s*(\d{2})(\d{2})(\s*TI|\s*SUPER)?");
            if (m.Success)
            {
                int series = int.Parse(m.Groups[1].Value);
                int model = int.Parse(m.Groups[2].Value);
                bool plus = m.Groups[3].Success;
                GraphicsTier t;
                if (series >= 50) t = model >= 80 ? GraphicsTier.Episch : model >= 70 ? GraphicsTier.Hoch : GraphicsTier.Mittel;
                else if (series == 40) t = model >= 90 ? GraphicsTier.Episch : model >= 70 ? GraphicsTier.Hoch : GraphicsTier.Mittel;
                else if (series == 30) t = model >= 70 || (model == 60 && plus) ? GraphicsTier.Mittel : GraphicsTier.Niedrig;
                else t = GraphicsTier.Niedrig;
                note = $"{deviceName} erkannt → {GraphicsCatalog.TierNames[(int)t]}";
                return t;
            }
            m = Regex.Match(n, @"RX\s*(\d)(\d)(\d)0(\s*XTX|\s*XT)?");
            if (m.Success)
            {
                int gen = int.Parse(m.Groups[1].Value);
                // RDNA 4 heisst "9070": dort steht die Klasse an dritter Stelle
                int tierDigit = int.Parse(gen >= 9 ? m.Groups[3].Value : m.Groups[2].Value);
                string suffix = m.Groups[4].Success ? m.Groups[4].Value.Trim() : "";
                GraphicsTier t;
                if (gen >= 9) t = tierDigit >= 7 ? (suffix == "XT" || suffix == "XTX" ? GraphicsTier.Episch : GraphicsTier.Hoch) : GraphicsTier.Mittel;
                else if (gen == 7) t = tierDigit >= 9 && suffix == "XTX" ? GraphicsTier.Episch : tierDigit >= 8 ? GraphicsTier.Hoch : GraphicsTier.Mittel;
                else if (gen == 6) t = tierDigit >= 7 ? GraphicsTier.Mittel : GraphicsTier.Niedrig;
                else t = GraphicsTier.Niedrig;
                note = $"{deviceName} erkannt → {GraphicsCatalog.TierNames[(int)t]}";
                return t;
            }
            GraphicsTier byMemory = vramMb >= 12000 ? GraphicsTier.Hoch : vramMb >= 8000 ? GraphicsTier.Mittel : GraphicsTier.Niedrig;
            note = $"{deviceName} ({vramMb} MB) nach Grafikspeicher → {GraphicsCatalog.TierNames[(int)byMemory]}";
            return byMemory;
        }
    }
}
