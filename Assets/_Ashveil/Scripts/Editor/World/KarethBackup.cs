using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kanon 26.09. ("Kareth niemals komplett neu aufbauen"): Vor jedem Szenenbau wird der bestehende Stand gesichert –
    /// Szene, Terrain-Daten, NavMesh, Layout und Handarbeit – nach &lt;Projekt&gt;/Backups/Kareth/&lt;Zeitstempel&gt;/.
    /// Die letzten 10 Sicherungen bleiben erhalten. Zurueckholen: Dateien an ihren Platz unter Assets kopieren.
    /// </summary>
    public static class KarethBackup
    {
        private const int Keep = 10;

        private static readonly string[] Files =
        {
            SandboxSceneBuilder.ScenePath,
            ProjectSetup.Root + "/Terrain/Kareth_TerrainData.asset",
            ProjectSetup.Root + "/Scenes/Sandbox_M1_NavMesh.asset",
            KarethLayout.AssetPath,
            ProjectSetup.Root + "/Layout/Handarbeit.prefab"
        };

        public static string Run()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            string root = Ashveil.Core.ProjectPaths.Backups("Kareth");
            string dir = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            int copied = 0;
            foreach (string rel in Files)
            {
                string src = Path.Combine(project, rel);
                if (!File.Exists(src)) continue;
                string dst = Path.Combine(dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(src, dst, true);
                if (File.Exists(src + ".meta")) File.Copy(src + ".meta", dst + ".meta", true);
                copied++;
            }
            if (copied == 0) return null;
            foreach (string old in Directory.GetDirectories(root).OrderByDescending(d => d).Skip(Keep))
            {
                try { Directory.Delete(old, true); }
                catch (Exception e) { Debug.LogWarning("[Ashveil] Alte Sicherung nicht geloescht: " + e.Message); }
            }
            Debug.Log($"[Ashveil] Kareth gesichert ({copied} Dateien) -> {dir}");
            return dir;
        }
    }
}
