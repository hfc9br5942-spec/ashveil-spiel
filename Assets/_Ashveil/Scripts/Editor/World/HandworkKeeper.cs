using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Schutz fuer Handarbeit: Alles, was unter dem Szenenobjekt <see cref="RootName"/> liegt, wird vor
    /// "Sandbox-Szene erstellen" als Prefab gesichert (_Ashveil/Layout/Handarbeit.prefab) und nach dem Neuaufbau
    /// wieder eingesetzt. So verschwinden von Hand gesetzte Felsen, Requisiten oder Lichter nicht.
    /// Grenzen: Verweise auf generierte Objekte (z. B. ein NPC) gehen verloren; Netzwerkobjekte gehoeren nicht hinein.
    /// </summary>
    public static class HandworkKeeper
    {
        public const string RootName = "Handarbeit (bleibt beim Neuaufbau erhalten)";
        private const string PrefabPath = ProjectSetup.Root + "/Layout/Handarbeit.prefab";

        public static void Capture(string scenePath)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != scenePath) return;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name != RootName) continue;
                ProjectSetup.EnsureFolder(ProjectSetup.Root + "/Layout");
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath, out bool ok);
                Debug.Log(ok
                    ? $"[Ashveil] Handarbeit gesichert ({go.transform.childCount} Objekte) -> {PrefabPath}"
                    : "[Ashveil] Handarbeit konnte nicht gesichert werden.");
                return;
            }
        }

        public static void Restore()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject root;
            if (prefab != null)
            {
                root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = RootName;
                Debug.Log($"[Ashveil] Handarbeit wiederhergestellt ({root.transform.childCount} Objekte).");
            }
            else
            {
                root = new GameObject(RootName);
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
        }
    }
}
