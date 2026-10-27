using Ashveil.Data;
using Ashveil.Gameplay.Enemies;
using Ashveil.Gameplay.Items;
using Ashveil.Networking;
using FishNet.Component.Transforming;
using FishNet.Object;
using FishNet.Observing;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Gefaehrte des Dominators (Klassen-Identitaet Phase 4): baut aus dem Aschwolf-Gegner eine Gefaehrten-Vorlage –
    /// gleiches Modell und dieselbe Darstellung, aber ohne Gegner-KI und Beute, mit eigener Netz-Ausstattung und
    /// <see cref="NetworkCompanion"/>. Augen in blassem Geisterlicht (§78: subtile Verbindung, keine leuchtende Leine).
    /// Liegt unter Resources, damit der Server sie laden kann; wird als spawnbar eingetragen.
    /// </summary>
    public static class CompanionBuilder
    {
        public const string Folder = ProjectSetup.Root + "/Resources/Companions";
        public const string PrefabPath = Folder + "/Companion_Aschwolf.prefab";

        [MenuItem("Ashveil/Kampf/Begleiter: Aschwolf-Gefährte bauen", priority = 502)]
        public static void BuildMenu()
        {
            GameObject p = Build();
            Debug.Log(p != null ? "[Ashveil] Gefährte gebaut: " + PrefabPath + " (Aschwolf dominierbar)." : "[Ashveil] Gefährte: Aschwolf-Vorlage fehlt – erst die Sandbox-Szene erstellen.");
        }

        public static GameObject Build()
        {
            // Wilde Form als dominierbar markieren (§60)
            var def = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(EnemyBuilder.DefFolder + "/Enemy_Aschwolf.asset");
            if (def != null && !def.dominatable) { def.dominatable = true; EditorUtility.SetDirty(def); }

            var src = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyBuilder.PrefabFolder + "/Enemy_Aschwolf.prefab");
            if (src == null) return null;
            var root = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "Companion_Aschwolf";

            // Gegner-Teile und alte Netz-Ausstattung weg (in Abhaengigkeits-Reihenfolge)
            Kill<NetworkEnemy>(root);
            Kill<EnemyBrain>(root);
            Kill<LootIndicator>(root);
            Kill<CorpseLoot>(root);
            Kill<LootEligibility>(root);
            Kill<NetworkHealth>(root);
            Kill<NetworkTransform>(root);
            Kill<NetworkObserver>(root);
            Kill<NetworkObject>(root);

            // Frische Netz-Ausstattung (wie EnemyBuilder.AddCommon)
            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.SetSynchronizeScale(false);
            var so = new SerializedObject(nt);
            SerializedProperty auth = so.FindProperty("_clientAuthoritative");
            if (auth != null) auth.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<NetworkHealth>();
            InterestManagement.ApplyEnemy(root);
            root.AddComponent<NetworkCompanion>();

            // Geisterlicht in den Augen
            Material eye = EyeMaterial();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Eye") || r.name.Contains("Ember")) r.sharedMaterial = eye;

            if (!AssetDatabase.IsValidFolder(ProjectSetup.Root + "/Resources")) AssetDatabase.CreateFolder(ProjectSetup.Root, "Resources");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(ProjectSetup.Root + "/Resources", "Companions");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            var nob = prefab.GetComponent<NetworkObject>();
            if (nob != null) { nob.SetSceneId(NetworkObject.UNSET_SCENEID_VALUE); EditorUtility.SetDirty(nob); }
            EditorUtility.SetDirty(prefab);
            NetworkSetupBuilder.AddSpawnables(new[] { prefab });
            AssetDatabase.SaveAssets();
            return prefab;
        }

        private static void Kill<T>(GameObject go) where T : Component
        {
            foreach (T c in go.GetComponents<T>()) Object.DestroyImmediate(c, true);
        }

        private static Material EyeMaterial()
        {
            string path = ProjectSetup.Root + "/Resources/Companions/Companion_Geisterlicht.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            if (!AssetDatabase.IsValidFolder(ProjectSetup.Root + "/Resources")) AssetDatabase.CreateFolder(ProjectSetup.Root, "Resources");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(ProjectSetup.Root + "/Resources", "Companions");
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            m = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = "Companion_Geisterlicht" };
            Color c = new Color(0.62f, 0.8f, 1f);
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
