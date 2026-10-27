using System;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Stellt sicher, dass die "TMP Essential Resources" (Standardschrift, SDF-Shader, Einstellungen)
    /// im Projekt liegen. Unity liefert sie mit dem uGUI-Paket aus, importiert sie aber erst auf Anfrage.
    /// </summary>
    public static class TmpResources
    {
        private static Action _pending;

        public static bool IsImported => Resources.Load<TMP_Settings>("TMP Settings") != null;

        /// <summary>
        /// true = Ressourcen vorhanden, Aufrufer kann weitermachen.
        /// false = Import gestartet; onImported wird nach Abschluss automatisch aufgerufen.
        /// </summary>
        public static bool EnsureImported(Action onImported)
        {
            if (IsImported) return true;

            _pending = onImported;
            AssetDatabase.importPackageCompleted -= OnImportCompleted;
            AssetDatabase.importPackageCompleted += OnImportCompleted;
            AssetDatabase.importPackageFailed -= OnImportFailed;
            AssetDatabase.importPackageFailed += OnImportFailed;

            Debug.Log("[Ashveil] Importiere TextMeshPro-Grundressourcen ...");
            try
            {
                TMP_PackageResourceImporter.ImportResources(true, false, false);
            }
            catch (Exception e)
            {
                Unsubscribe();
                _pending = null;
                Debug.LogError("[Ashveil] TextMeshPro-Import konnte nicht gestartet werden: " + e.Message +
                               "\nBitte manuell: Window > TextMeshPro > Import TMP Essential Resources, danach erneut ausfuehren.");
                return false;
            }

            // Falls kein Rueckruf kommt (z. B. Paket nicht gefunden), nach kurzer Zeit einen Hinweis geben.
            double deadline = EditorApplication.timeSinceStartup + 20.0;
            EditorApplication.CallbackFunction watchdog = null;
            watchdog = () =>
            {
                if (_pending == null || IsImported) { EditorApplication.update -= watchdog; return; }
                if (EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= watchdog;
                Unsubscribe();
                _pending = null;
                Debug.LogWarning("[Ashveil] TextMeshPro-Import nicht abgeschlossen. Bitte Window > TextMeshPro > " +
                                 "Import TMP Essential Resources ausfuehren und den Menuepunkt danach erneut starten.");
            };
            EditorApplication.update += watchdog;
            return false;
        }

        [MenuItem("Ashveil/Projekt/TextMeshPro-Ressourcen importieren", priority = 102)]
        private static void ImportManually()
        {
            if (IsImported)
            {
                EditorUtility.DisplayDialog("Ashveil", "Die TextMeshPro-Ressourcen sind bereits vorhanden.", "OK");
                return;
            }
            EnsureImported(null);
        }

        private static void OnImportCompleted(string packageName)
        {
            if (!packageName.Contains("TMP Essential")) return;
            Unsubscribe();
            AssetDatabase.Refresh();
            Debug.Log("[Ashveil] TextMeshPro-Grundressourcen importiert.");

            Action next = _pending;
            _pending = null;
            if (next != null) EditorApplication.delayCall += () => next();
        }

        private static void OnImportFailed(string packageName, string error)
        {
            if (!packageName.Contains("TMP Essential")) return;
            Unsubscribe();
            _pending = null;
            Debug.LogError("[Ashveil] Import der TextMeshPro-Ressourcen fehlgeschlagen: " + error +
                           "\nBitte manuell: Window > TextMeshPro > Import TMP Essential Resources.");
        }

        private static void Unsubscribe()
        {
            AssetDatabase.importPackageCompleted -= OnImportCompleted;
            AssetDatabase.importPackageFailed -= OnImportFailed;
        }
    }
}
