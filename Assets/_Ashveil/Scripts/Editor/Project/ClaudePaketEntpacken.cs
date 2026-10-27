using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Entpackt von Max bereitgelegte Claude-Pakete (claude/*.zip) einmalig in einen gleichnamigen Ordner daneben,
    /// damit Claude die Einzeldateien lesen kann (große ZIPs lassen sich nicht direkt übertragen). Nur Lesen und
    /// Schreiben innerhalb von claude/; keine Assets, keine Szenen. Bereits entpackte Pakete werden übersprungen.
    /// </summary>
    [InitializeOnLoad]
    public static class ClaudePaketEntpacken
    {
        private static readonly string[] Pakete = { "ASHVEIL_CLAUDE_GESAMTPAKET.zip" };

        static ClaudePaketEntpacken() => EditorApplication.delayCall += Run;

        [MenuItem("Ashveil/Projekt/Claude-Pakete entpacken", priority = 900)]
        public static void Run()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "claude"));
            foreach (string name in Pakete)
            {
                string zip = Path.Combine(root, name);
                string target = Path.Combine(root, Path.GetFileNameWithoutExtension(name));
                if (!File.Exists(zip) || Directory.Exists(target)) continue;
                try
                {
                    string full = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
                    using (ZipArchive archive = ZipFile.OpenRead(zip))
                    {
                        int n = 0;
                        foreach (ZipArchiveEntry e in archive.Entries)
                        {
                            string dest = Path.GetFullPath(Path.Combine(target, e.FullName));
                            if (!dest.StartsWith(full)) continue;          // Schutz gegen Pfade außerhalb des Zielordners
                            if (string.IsNullOrEmpty(e.Name)) { Directory.CreateDirectory(dest); continue; }
                            Directory.CreateDirectory(Path.GetDirectoryName(dest));
                            e.ExtractToFile(dest, true);
                            n++;
                        }
                        Debug.Log($"[Ashveil] Claude-Paket entpackt: {name} → {target} ({n} Dateien)");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[Ashveil] Claude-Paket {name} konnte nicht entpackt werden: {ex.Message}");
                }
            }
        }
    }
}
