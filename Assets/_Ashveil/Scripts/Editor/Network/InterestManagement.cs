using FishNet.Object;
using FishNet.Observing;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Large-World N3: Interessenverwaltung. Gegner werden einem Client nur geschickt, wenn eine seiner Figuren naeher als
    /// <see cref="EnemyRange"/> ist UND im selben Instanz-Platz steht (EnemyInterestCondition, 10 % Hysterese; Kolosseum
    /// Pass 2). Spieler bleiben sichtbar, wenn nah im selben Platz, die eigene Figur oder in derselben Gruppe
    /// (PlayerInterestCondition). Chat, Gruppenfenster und Fluestern laufen ueber den Server und brauchen keine Sichtbarkeit.
    /// </summary>
    public static class InterestManagement
    {
        public const float EnemyRange = 500f;
        private const string Folder = ProjectSetup.Root + "/Data/Network";
        private const string EnemyConditionPath = Folder + "/Sicht_Gegner.asset";

        public static ObserverCondition EnemyCondition()
        {
            ProjectSetup.EnsureFolder(Folder);
            var c = AssetDatabase.LoadAssetAtPath<Ashveil.Networking.EnemyInterestCondition>(EnemyConditionPath);
            if (c == null)
            {
                // Frueher: FishNet-DistanceCondition (nur Abstand) – durch die Instanz-Regel ersetzen
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(EnemyConditionPath) != null) AssetDatabase.DeleteAsset(EnemyConditionPath);
                c = ScriptableObject.CreateInstance<Ashveil.Networking.EnemyInterestCondition>();
                AssetDatabase.CreateAsset(c, EnemyConditionPath);
            }
            c.Configure(EnemyRange, 0.1f);
            EditorUtility.SetDirty(c);
            return c;
        }

        public const float PlayerRange = 500f;
        private const string PlayerConditionPath = Folder + "/Sicht_Spieler.asset";

        public static ObserverCondition PlayerCondition()
        {
            ProjectSetup.EnsureFolder(Folder);
            var c = AssetDatabase.LoadAssetAtPath<Ashveil.Networking.PlayerInterestCondition>(PlayerConditionPath);
            if (c == null)
            {
                c = ScriptableObject.CreateInstance<Ashveil.Networking.PlayerInterestCondition>();
                AssetDatabase.CreateAsset(c, PlayerConditionPath);
            }
            c.Configure(PlayerRange, 0.1f);
            EditorUtility.SetDirty(c);
            return c;
        }

        /// <summary>Spielerfigur: nah, eigene Figur oder gleiche Gruppe (PlayerInterestCondition).</summary>
        public static void ApplyPlayer(GameObject root) => Apply(root, PlayerCondition());

        /// <summary>NetworkObserver mit der Gegner-Bedingung an ein Objekt (Prefab-Wurzel) haengen.</summary>
        public static void ApplyEnemy(GameObject root) => Apply(root, EnemyCondition());

        private static void Apply(GameObject root, ObserverCondition cond)
        {
            if (root == null || root.GetComponent<NetworkObject>() == null) return;
            NetworkObserver obs = root.GetComponent<NetworkObserver>();
            if (obs == null) obs = root.AddComponent<NetworkObserver>();
            var so = new SerializedObject(obs);
            SerializedProperty type = so.FindProperty("_overrideType");
            if (type != null) type.intValue = (int)NetworkObserver.ConditionOverrideType.IgnoreManager;
            SerializedProperty list = so.FindProperty("_observerConditions");
            if (list != null)
            {
                list.ClearArray();
                list.InsertArrayElementAtIndex(0);
                list.GetArrayElementAtIndex(0).objectReferenceValue = cond;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(obs);
        }

        [MenuItem("Ashveil/Netzwerk/Interessenverwaltung anwenden (Gegner + Spieler)", priority = 230)]
        public static void ApplyToEnemyPrefabs()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { EnemyBuilder.PrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root.GetComponent<Ashveil.Gameplay.Enemies.EnemyBrain>() != null)
                {
                    ApplyEnemy(root);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    n++;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SandboxSceneBuilder.PlayerPrefabPath) != null)
            {
                GameObject pr = PrefabUtility.LoadPrefabContents(SandboxSceneBuilder.PlayerPrefabPath);
                ApplyPlayer(pr);
                PrefabUtility.SaveAsPrefabAsset(pr, SandboxSceneBuilder.PlayerPrefabPath);
                PrefabUtility.UnloadPrefabContents(pr);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Ashveil] Interessenverwaltung: {n} Gegner-Prefabs sichtbar bis {EnemyRange:0} m im selben Instanz-Platz, Spieler ebenso oder in derselben Gruppe.");
        }
    }
}
