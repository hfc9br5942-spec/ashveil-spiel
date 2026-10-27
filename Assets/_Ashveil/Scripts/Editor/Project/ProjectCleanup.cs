using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Entfernt Reste der Unity-Projektvorlage und veraltete Ashveil-Dateien.
    /// Behalten werden bewusst: Assets/Settings (URP-Einstellungen) und
    /// InputSystem_Actions (projektweite Eingabeaktionen des Input Systems).
    /// </summary>
    public static class ProjectCleanup
    {
        private static readonly string[] ObsoletePaths =
        {
            // Vorlage / Reste
            "Assets/Ashveil_Milestone1.zip",
            "Assets/New Folder",
            "Assets/TutorialInfo",
            "Assets/Readme.asset",
            "Assets/Scenes/SampleScene.unity",
            // Veraltet seit den neuen Namensschildern
            ProjectSetup.Root + "/Scripts/World/FaceCamera.cs"
        };

        [MenuItem("Ashveil/Projekt/Aufraeumen (Vorlage & alte Dateien)", priority = 103)]
        private static void RunFromMenu()
        {
            Run(true);
        }

        /// <summary>Loescht vorhandene veraltete Dateien. ask = vorher nachfragen.</summary>
        public static void Run(bool ask)
        {
            string activeScene = EditorSceneManager.GetActiveScene().path;
            var toDelete = ObsoletePaths
                .Where(p => AssetDatabase.IsValidFolder(p) || AssetDatabase.LoadMainAssetAtPath(p) != null)
                .Where(p => p != activeScene)
                .Where(p => !AssetDatabase.IsValidFolder(p) || p.StartsWith(ProjectSetup.Root) ||
                            p == "Assets/TutorialInfo" || IsEmptyFolder(p)) // fremde Ordner nur, wenn leer
                .ToList();

            if (toDelete.Count == 0)
            {
                if (ask) EditorUtility.DisplayDialog("Ashveil", "Nichts aufzuraeumen – das Projekt ist sauber.", "OK");
                RemoveMissingScenesFromBuildSettings();
                return;
            }

            if (ask && !EditorUtility.DisplayDialog("Ashveil – Aufraeumen",
                    "Folgende Dateien werden in den Papierkorb verschoben:\n\n" + string.Join("\n", toDelete), "Aufraeumen", "Abbrechen"))
                return;

            // In den Papierkorb statt endgueltig loeschen – so ist nichts unwiederbringlich weg.
            var failed = new List<string>();
            AssetDatabase.MoveAssetsToTrash(toDelete.ToArray(), failed);

            // Leeren Szenen-Ordner der Vorlage entfernen.
            if (AssetDatabase.IsValidFolder("Assets/Scenes") && IsEmptyFolder("Assets/Scenes"))
                AssetDatabase.MoveAssetToTrash("Assets/Scenes");

            RemoveMissingScenesFromBuildSettings();
            AssetDatabase.Refresh();

            foreach (string path in toDelete.Except(failed)) Debug.Log("[Ashveil] In den Papierkorb verschoben: " + path);
            foreach (string path in failed) Debug.LogWarning("[Ashveil] Konnte nicht entfernt werden: " + path);
        }

        private static bool IsEmptyFolder(string folder)
        {
            return AssetDatabase.IsValidFolder(folder) &&
                   AssetDatabase.FindAssets("", new[] { folder }).Length == 0 &&
                   AssetDatabase.GetSubFolders(folder).Length == 0;
        }

        private static void RemoveMissingScenesFromBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            EditorBuildSettingsScene[] kept = scenes
                .Where(s => AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path) != null)
                .ToArray();
            if (kept.Length != scenes.Length) EditorBuildSettings.scenes = kept;
        }
    }
}
