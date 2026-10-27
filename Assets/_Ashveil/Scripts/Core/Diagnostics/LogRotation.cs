using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Ashveil.Core.Diagnostics
{
    /// <summary>
    /// Messprotokolle nicht ansammeln (Max 11.10.2026: „Nur letzte 5 behalten“ – im Spieltest-Ordner lagen 1,4 GB Ruckler- und
    /// Rekorder-Sitzungen). Beim ersten Lauf werden aeltere Sitzungen nicht geloescht, sondern nach Logs/Ashveil/_zu_loeschen/
    /// verschoben (Max loescht sie selbst); danach weichen aeltere Sitzungen automatisch. Fehler werden nur protokolliert.
    /// </summary>
    public static class LogRotation
    {
        private const string Marke = ".rotation_v1";
        private static readonly Regex Stempel = new Regex(@"\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}", RegexOptions.Compiled);

        /// <summary>Dateien mit Zeitstempel im Namen (eine Sitzung = ein Stempel): nur die <paramref name="behalten"/> neuesten Sitzungen bleiben.</summary>
        public static void KeepNewestStampedFiles(string ordner, int behalten)
        {
            try
            {
                if (string.IsNullOrEmpty(ordner) || !Directory.Exists(ordner)) return;
                var gruppen = new Dictionary<string, List<string>>();
                foreach (string f in Directory.GetFiles(ordner))
                {
                    Match m = Stempel.Match(Path.GetFileName(f));
                    if (!m.Success) continue;
                    if (!gruppen.TryGetValue(m.Value, out var l)) gruppen[m.Value] = l = new List<string>();
                    l.Add(f);
                }
                var alt = gruppen.Keys.OrderByDescending(k => k, StringComparer.Ordinal).Skip(Math.Max(0, behalten)).ToList();
                if (alt.Count == 0) return;
                bool erstes = !File.Exists(Path.Combine(ordner, Marke));
                string ziel = ZielOrdner(ordner);
                foreach (string k in alt)
                    foreach (string f in gruppen[k])
                        Weg(f, ziel, erstes, false);
                if (erstes) File.WriteAllText(Path.Combine(ordner, Marke), DateTime.Now.ToString("o"));
            }
            catch (Exception e) { Debug.LogWarning("[Ashveil] Protokolle aufräumen: " + e.Message); }
        }

        /// <summary>Unterordner (eine Sitzung je Ordner): nur die <paramref name="behalten"/> neuesten (nach Anlegezeit) bleiben.</summary>
        public static void KeepNewestDirectories(string ordner, int behalten)
        {
            try
            {
                if (string.IsNullOrEmpty(ordner) || !Directory.Exists(ordner)) return;
                var alt = Directory.GetDirectories(ordner)
                    .OrderByDescending(d => Directory.GetCreationTimeUtc(d))
                    .Skip(Math.Max(0, behalten)).ToList();
                if (alt.Count == 0) return;
                bool erstes = !File.Exists(Path.Combine(ordner, Marke));
                string ziel = ZielOrdner(ordner);
                foreach (string d in alt) Weg(d, ziel, erstes, true);
                if (erstes) File.WriteAllText(Path.Combine(ordner, Marke), DateTime.Now.ToString("o"));
            }
            catch (Exception e) { Debug.LogWarning("[Ashveil] Protokolle aufräumen: " + e.Message); }
        }

        // Logs/Ashveil/Ruckler → Logs/Ashveil/_zu_loeschen/Ruckler
        private static string ZielOrdner(string ordner)
        {
            string name = Path.GetFileName(Path.GetFullPath(ordner).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string eltern = Path.GetDirectoryName(Path.GetFullPath(ordner).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return Path.Combine(eltern ?? ordner, "_zu_loeschen", name);
        }

        private static void Weg(string pfad, string ziel, bool verschieben, bool ordner)
        {
            try
            {
                if (verschieben)
                {
                    Directory.CreateDirectory(ziel);
                    string nach = Path.Combine(ziel, Path.GetFileName(pfad));
                    if (ordner) { if (!Directory.Exists(nach)) Directory.Move(pfad, nach); }
                    else if (!File.Exists(nach)) File.Move(pfad, nach);
                }
                else if (ordner) Directory.Delete(pfad, true);
                else File.Delete(pfad);
            }
            catch (Exception e) { Debug.LogWarning("[Ashveil] Protokoll " + pfad + ": " + e.Message); }
        }
    }
}
