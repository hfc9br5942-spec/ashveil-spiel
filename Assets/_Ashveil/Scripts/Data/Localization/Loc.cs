using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Schlanke Texttabellen (Entscheidung Max 29.09.: eigene Tabelle statt Unity-Localization-Paket).
    /// Tabellen liegen als Textdateien unter <c>Resources/Text/&lt;sprache&gt;/*.txt</c> und sind mit jedem Editor bearbeitbar:
    ///
    ///   # Kommentar
    ///   quest.human_tank.foal_rescue.name = Wo die Herde floh
    ///   npc.human.horsebreeder_01.line.q1_01 = Da! Ihr – mit dem Schild!\nHabt Ihr … habt Ihr Pferde gesehen?
    ///
    /// „\n“ im Text = Zeilenumbruch. Fehlt ein Schluessel, erscheint der Schluessel selbst in eckigen Klammern (faellt in Tests auf).
    /// Neue Texte laufen ueber diese Tabelle; bestehende Texte werden nicht auf einmal umgebaut (Plan §10.8).
    /// </summary>
    public static class Loc
    {
        public const string DefaultLanguage = "de";

        private static readonly Dictionary<string, string> Table = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string _language = DefaultLanguage;
        private static bool _loaded;

        /// <summary>Anzahl geladener Eintraege (Tests).</summary>
        public static int Count { get { EnsureLoaded(); return Table.Count; } }
        public static string Language => _language;

        public static void SetLanguage(string language)
        {
            _language = string.IsNullOrEmpty(language) ? DefaultLanguage : language;
            _loaded = false;
        }

        public static bool Has(string key)
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(key) && Table.ContainsKey(key);
        }

        /// <summary>Text zum Schluessel (fehlt er: "[schluessel]").</summary>
        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            EnsureLoaded();
            return Table.TryGetValue(key, out string v) ? v : "[" + key + "]";
        }

        /// <summary>Text mit Platzhaltern {0}, {1} …</summary>
        public static string F(string key, params object[] args)
        {
            string t = T(key);
            try { return string.Format(CultureInfo.InvariantCulture, t, args); }
            catch (FormatException) { return t; }
        }

        /// <summary>Tests/Werkzeuge: Eintraege direkt hinzufuegen (ueberschreibt).</summary>
        public static void Add(string key, string text)
        {
            EnsureLoaded();
            Table[key] = text ?? "";
        }

        public static void Reload() => _loaded = false;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            Table.Clear();
            TextAsset[] files = Resources.LoadAll<TextAsset>("Text/" + _language);
            if ((files == null || files.Length == 0) && _language != DefaultLanguage) files = Resources.LoadAll<TextAsset>("Text/" + DefaultLanguage);
            if (files == null) return;
            foreach (TextAsset f in files) Parse(f.text, f.name);
        }

        /// <summary>Liest eine Tabelle ("schluessel = text"); spaetere Eintraege ueberschreiben fruehere.</summary>
        public static int Parse(string content, string source = "")
        {
            if (string.IsNullOrEmpty(content)) return 0;
            int n = 0;
            foreach (string raw in content.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                string t = line.TrimStart();
                if (t.Length == 0 || t[0] == '#') continue;
                int eq = t.IndexOf('=');
                if (eq <= 0)
                {
                    Debug.LogWarning($"[Ashveil] Texttabelle {source}: Zeile ohne '=' – {t}");
                    continue;
                }
                string key = t.Substring(0, eq).Trim();
                string val = t.Substring(eq + 1).Trim().Replace("\\n", "\n");
                Table[key] = val;
                n++;
            }
            return n;
        }
    }
}
