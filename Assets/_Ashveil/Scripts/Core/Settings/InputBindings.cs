using System;
using System.IO;
using UnityEngine;

namespace Ashveil.Core.Settings
{
    /// <summary>
    /// Tastenbelegung dieses Rechners (Feature-Update 26.09., Abschnitte 2–4 und 10): zwoelf Actionbar-Tasten, die
    /// Wechseltaste fuer Bank B und der Modus Halten/Umschalten. Tasten sind Geraete-Einstellungen, die Belegung der
    /// Plaetze gehoert zum Charakter (Speicherstand auf dem Server).
    ///
    /// Tasten als Code: "Key:Digit1" (physische Taste des Input Systems), "Mouse:Middle", "Mouse:Back", "Mouse:Forward".
    /// Leer = nicht belegt.
    /// </summary>
    [Serializable]
    public sealed class InputBindings
    {
        public const int Version = 1;
        public const string DefaultSwap = "Key:LeftCtrl";
        /// <summary>Boss-Anatomie §4: naechsten Schwachpunkt des Ziels waehlen.</summary>
        public const string DefaultWeakPoint = "Key:G";
        /// <summary>Gangarten (Max 29.09.2026: Standard ist Rennen; Gehen, Joggen und Epischer Gang je eine eigene,
        /// umbelegbare Umschalttaste). Ohne Ziffernblock (Laptop) frei auf andere Tasten legbar.</summary>
        public const string DefaultWalk = "Key:NumpadDivide", DefaultJog = "Key:NumpadMultiply", DefaultEpicWalk = "Key:NumpadMinus";
        /// <summary>1 2 3 4 5 6 7 8 9 0 ß ´ (deutsche Tastatur; physisch Digit1 … Digit0, Minus, Equals)</summary>
        public static readonly string[] DefaultSlots =
        {
            "Key:Digit1", "Key:Digit2", "Key:Digit3", "Key:Digit4", "Key:Digit5", "Key:Digit6",
            "Key:Digit7", "Key:Digit8", "Key:Digit9", "Key:Digit0", "Key:Minus", "Key:Equals"
        };

        public int version = Version;
        public string[] slots = (string[])DefaultSlots.Clone();
        public string swapKey = DefaultSwap;
        public string weakPointKey = DefaultWeakPoint;
        public string walkKey = DefaultWalk;
        public string jogKey = DefaultJog;
        public string epicWalkKey = DefaultEpicWalk;
        /// <summary>false = Halten (Bank B nur solange gedrueckt), true = Umschalten.</summary>
        public bool toggleMode;

        public InputBindings Clone()
        {
            var c = (InputBindings)MemberwiseClone();
            c.slots = (string[])slots.Clone();
            return c;
        }

        public void Clamp()
        {
            if (slots == null || slots.Length != DefaultSlots.Length)
            {
                var s = (string[])DefaultSlots.Clone();
                if (slots != null) for (int i = 0; i < Mathf.Min(slots.Length, s.Length); i++) s[i] = slots[i];
                slots = s;
            }
            for (int i = 0; i < slots.Length; i++) slots[i] = Normalize(slots[i]);
            swapKey = Normalize(swapKey);
            // aeltere Dateien kennen die Taste noch nicht: Standard setzen (null), bewusst geleert bleibt leer ("")
            weakPointKey = weakPointKey == null ? DefaultWeakPoint : Normalize(weakPointKey);
            walkKey = walkKey == null ? DefaultWalk : Normalize(walkKey);
            jogKey = jogKey == null ? DefaultJog : Normalize(jogKey);
            epicWalkKey = epicWalkKey == null ? DefaultEpicWalk : Normalize(epicWalkKey);
            version = Version;
        }

        public static string Normalize(string code)
        {
            code = (code ?? "").Trim();
            if (code.Length == 0) return "";
            return code.StartsWith("Key:", StringComparison.Ordinal) || code.StartsWith("Mouse:", StringComparison.Ordinal) ? code : "";
        }

        /// <summary>Wo liegt dieser Code schon? -1..-12 = Platz, -100 = Wechseltaste, -101 = Schwachpunkt,
        /// -102 Gehen, -103 Joggen, -104 Epischer Gang, 0 = nirgends.</summary>
        public int UsedBy(string code)
        {
            if (string.IsNullOrEmpty(code)) return 0;
            for (int i = 0; i < slots.Length; i++) if (slots[i] == code) return -(i + 1);
            if (swapKey == code) return -100;
            if (weakPointKey == code) return -101;
            if (walkKey == code) return -102;
            if (jogKey == code) return -103;
            return epicWalkKey == code ? -104 : 0;
        }

        private void Free(int before)
        {
            if (before <= -1 && before >= -12) slots[-before - 1] = "";
            else if (before == -100) swapKey = "";
            else if (before == -101) weakPointKey = "";
            else if (before == -102) walkKey = "";
            else if (before == -103) jogKey = "";
            else if (before == -104) epicWalkKey = "";
        }

        /// <summary>Gangart-Taste belegen: 102 Gehen, 103 Joggen, 104 Epischer Gang.</summary>
        public int BindGait(int which, string code)
        {
            code = Normalize(code);
            int before = UsedBy(code);
            if (before != -which) Free(before);
            if (which == 102) walkKey = code;
            else if (which == 103) jogKey = code;
            else if (which == 104) epicWalkKey = code;
            return before;
        }

        public int BindWeakPoint(string code)
        {
            code = Normalize(code);
            int before = UsedBy(code);
            if (before != -101) Free(before);
            weakPointKey = code;
            return before;
        }

        /// <summary>
        /// Platz neu belegen. Liegt die Taste schon woanders, wird sie dort entfernt (keine Doppelbelegung).
        /// Gibt zurueck, wo sie vorher lag (siehe <see cref="UsedBy"/>).
        /// </summary>
        public int BindSlot(int slot, string code)
        {
            code = Normalize(code);
            int before = UsedBy(code);
            Free(before);
            if (slot >= 0 && slot < slots.Length) slots[slot] = code;
            return before;
        }

        public int BindSwap(string code)
        {
            code = Normalize(code);
            int before = UsedBy(code);
            if (before != -100) Free(before);
            swapKey = code;
            return before;
        }
    }

    /// <summary>Haelt und speichert die Tastenbelegung dieses Rechners ("eingabe.json" im Benutzer-Datenordner).</summary>
    public static class InputBindingsStore
    {
        private static InputBindings _current;

        public static event Action<InputBindings> Changed;

        /// <summary>Tests: Speichern abschalten bzw. auf eine eigene Datei lenken.</summary>
        public static bool PersistenceEnabled = true;
        public static string FilePathOverride;

        public static string FilePath => !string.IsNullOrEmpty(FilePathOverride) ? FilePathOverride : Path.Combine(Application.persistentDataPath, "eingabe.json");

        public static InputBindings Current
        {
            get
            {
                if (_current == null) Load();
                return _current;
            }
        }

        public static void Load()
        {
            InputBindings loaded = null;
            try
            {
                if (PersistenceEnabled && File.Exists(FilePath)) loaded = JsonUtility.FromJson<InputBindings>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ashveil] Tastenbelegung nicht lesbar, nehme Voreinstellung: " + e.Message);
            }
            if (loaded == null) loaded = new InputBindings();
            loaded.Clamp();
            _current = loaded;
            Changed?.Invoke(_current);
        }

        public static void Set(InputBindings b)
        {
            InputBindings c = b.Clone();
            c.Clamp();
            _current = c;
            Save();
            Changed?.Invoke(_current);
        }

        public static void ResetToDefaults() => Set(new InputBindings());

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
                Debug.LogWarning("[Ashveil] Tastenbelegung nicht speicherbar: " + e.Message);
            }
        }
    }
}
