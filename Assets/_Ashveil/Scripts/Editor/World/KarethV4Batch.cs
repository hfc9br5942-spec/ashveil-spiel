using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kareth v4 im Stapellauf (Unity ohne Fenster), damit Neubau, Weltprüfung und Review-Bilder auch ohne Bedienung des
    /// Editors laufen – z. B. nachts, wenn Unity geschlossen ist:
    ///   Unity.exe -batchmode -projectPath &lt;Projekt&gt; -executeMethod Ashveil.EditorTools.KarethV4Batch.BuildCheckShots -logFile &lt;Datei&gt; -quit
    /// Gleiche Schritte wie die Menüs 560 (Szene bauen), 502 (Weltqualität prüfen) und 564 (Review-Bilder mit Zeitstempel).
    /// Ändert nur, was diese Menüs auch ändern (Kareth-v4-Szene, Terrain- und NavMesh-Daten, Berichte und Bilder unter Logs).
    /// </summary>
    public static class KarethV4Batch
    {
        public static void BuildCheckShots()
        {
            string report = KarethV4Builder.Build();
            Debug.Log("[Ashveil] Kareth v4 Stapellauf – Neubau: " + report);
            CheckAndShots();
        }

        /// <summary>Ohne Neubau: Szene öffnen, Weltqualität prüfen, Review-Bilder.</summary>
        public static void CheckAndShots()
        {
            EditorSceneManager.OpenScene(KarethV4Builder.ScenePath, OpenSceneMode.Single);
            Quality.WorldQualityValidator.RunMenu();
            string dir = KarethV4Shots.Render("Lauf_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm") + "_Stapel");
            Debug.Log("[Ashveil] Kareth v4 Stapellauf – Review-Bilder: " + (dir ?? "keine"));
        }
    }
}
