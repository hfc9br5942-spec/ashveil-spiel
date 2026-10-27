using System.IO;
using UnityEngine;

namespace Ashveil.Core
{
    /// <summary>
    /// Einzige Stelle fuer alle Ordner AUSSERHALB von "Assets" (neben Assets im Projektordner).
    /// Aufbau und Zweck: siehe Docs/Ordnerstruktur.md.
    ///
    ///   Docs/        Dokumentation (Architektur, Konventionen, Design-Kanon)          – versioniert
    ///   Tools/       Werkzeuge ausserhalb von Unity (Blender-Skripte)                  – versioniert
    ///   SourceArt/   Quelldateien fuer Grafik (.blend, Zwischen-FBX, Fremd-Downloads) – versioniert (Downloads optional)
    ///   Builds/      erzeugte Spiel-Builds (z. B. Test-Client)                         – NICHT versioniert
    ///   Logs/Ashveil/ Testberichte, Blender-Logs, Stil- und Vorschaubilder (Logs/ selbst gehoert Unity) – NICHT versioniert
    ///   Saves/       lokale Spielstaende der Testfiguren                               – NICHT versioniert
    ///   Backups/     automatische Sicherungen vor jedem Kareth-Neuaufbau               – NICHT versioniert
    ///
    /// Im gebauten Spiel zeigt <see cref="ProjectRoot"/> auf den Ordner neben "Ashveil_Data".
    /// </summary>
    public static class ProjectPaths
    {
        public const string DocsDir = "Docs";
        public const string ToolsDir = "Tools";
        public const string SourceArtDir = "SourceArt";
        public const string BuildsDir = "Builds";
        public const string LogsDir = "Logs";
        /// <summary>Unterordner fuer Ashveil-Berichte – Logs/ direkt enthaelt Unitys eigene Protokolle.</summary>
        public const string ReportsSubDir = "Ashveil";
        public const string SavesDir = "Saves";
        public const string BackupsDir = "Backups";

        /// <summary>Projektordner (enthaelt Assets, Packages, ProjectSettings …).</summary>
        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>Absoluter Pfad relativ zum Projektordner, z. B. Get("Logs", "Riese").</summary>
        public static string Get(params string[] parts) => Path.GetFullPath(Path.Combine(ProjectRoot, Path.Combine(parts)));

        public static string Logs(params string[] parts) => Get(Prepend(LogsDir, Prepend(ReportsSubDir, parts)));
        public static string Saves(params string[] parts) => Get(Prepend(SavesDir, parts));
        public static string Backups(params string[] parts) => Get(Prepend(BackupsDir, parts));
        public static string SourceArt(params string[] parts) => Get(Prepend(SourceArtDir, parts));
        public static string Tools(params string[] parts) => Get(Prepend(ToolsDir, parts));

        /// <summary>Test-Client fuer die Mehrspieler-Tests (Ashveil &gt; Test &gt; …).</summary>
        public static string TestClientExe => Get(BuildsDir, "TestClient", "Ashveil.exe");

        private static string[] Prepend(string first, string[] rest)
        {
            var all = new string[rest.Length + 1];
            all[0] = first;
            rest.CopyTo(all, 1);
            return all;
        }
    }
}
