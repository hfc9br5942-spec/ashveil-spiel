using System.Diagnostics;
using Ashveil.Gameplay.Npcs;
using Ashveil.Gameplay.Quests;
using Ashveil.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// 12h-Block 01.10.2026: P3 Glockenrast ADDITIV in die bestehende Sandbox_M1 einsetzen – ohne "Sandbox-Szene erstellen".
    /// Kareth (Gelaende, Handarbeit, GEN-Welt, andere Gruppen) bleibt unberuehrt. Vorgehen wie beim Kolosseum-Einbau:
    ///   1. Sicherung (KarethBackup)
    ///   2. Daten: Gegenstaende, Auftraege und NPC-Definitionen, Beutetabellen, Aussehen, Datenbank
    ///   3. Szene: Gruppe "Glockenrast (P3 Aufträge)" und die sechs P3-Spawngebiete (fruehere P3-Gruppen werden ersetzt),
    ///      Tamo und Veit bekommen ihre Quest-Kennung
    ///   4. Kareth-NavMesh neu backen (gleiche Flaeche, gleiches Verfahren), speichern
    /// Der Komplettneubau enthaelt Glockenrast weiterhin, damit ein spaeterer Neuaufbau dasselbe Ergebnis liefert.
    /// </summary>
    public static partial class SandboxSceneBuilder
    {
        public const string GlockenrastGroup = "Glockenrast (P3 Aufträge)";

        [MenuItem("Ashveil/P3/Glockenrast in Szene einfuegen (additiv)", priority = 3)]
        public static void InsertGlockenrastMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Ashveil", "Bitte zuerst den Play-Modus beenden.", "OK");
                return;
            }
            InsertGlockenrast();
        }

        /// <summary>Gibt true zurueck, wenn alles eingesetzt und gespeichert wurde.</summary>
        public static bool InsertGlockenrast()
        {
            var total = Stopwatch.StartNew();
            try
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                Scene world = SceneManager.GetSceneByPath(ScenePath);
                if (!world.IsValid() || !world.isLoaded) world = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                {
                    Scene s = SceneManager.GetSceneAt(i);
                    if (s != world) EditorSceneManager.CloseScene(s, true);
                }
                SceneManager.SetActiveScene(world);

                Transform env = null;
                foreach (GameObject go in world.GetRootGameObjects())
                    if (go.name == "Environment") env = go.transform;
                Transform camp = env != null ? env.Find("Wachtfeuer (Spawn)") : null;
                if (env == null || camp == null)
                {
                    Debug.LogError("[Ashveil] Glockenrast: 'Environment' oder 'Wachtfeuer (Spawn)' fehlt in Sandbox_M1 – nichts geaendert.");
                    return false;
                }

                EditorUtility.DisplayProgressBar("Ashveil", "Glockenrast: Sicherung ...", 0.02f);
                KarethBackup.Run();

                EditorUtility.DisplayProgressBar("Ashveil", "Glockenrast: Daten (Gegenstaende, Auftraege, Beute, Aussehen) ...", 0.1f);
                K.ResetCache();
                ItemContentBuilder.Build();
                QuestContentBuilder.Build();
                ItemContentBuilder.AssignLootTables();
                AppearanceContentBuilder.Build();
                DefinitionValidator.ValidateAndRebuild();
                SceneManager.SetActiveScene(world);

                EditorUtility.DisplayProgressBar("Ashveil", "Glockenrast: Figuren, Fundstellen, Erkundungsziele ...", 0.35f);
                CreateMaterials();
                for (Transform old = env.Find(GlockenrastGroup); old != null; old = env.Find(GlockenrastGroup))
                    Object.DestroyImmediate(old.gameObject);
                BuildGlockenrast(env);
                int ids = TagExistingNpc("Schreiber Veit", Ashveil.Data.GlockenrastCatalog.Veit) +
                          TagExistingNpc("Wagner Tamo", Ashveil.Data.GlockenrastCatalog.Tamo);

                EditorUtility.DisplayProgressBar("Ashveil", "Glockenrast: Spawngebiete ...", 0.5f);
                int spawns = EnemyBuilder.InsertGlockenrastSpawns(env);

                EditorUtility.DisplayProgressBar("Ashveil", "Glockenrast: Kareth-NavMesh neu backen ...", 0.6f);
                Physics.SyncTransforms();
                int baked = 0;
                foreach (NavMeshSurface s in env.GetComponentsInChildren<NavMeshSurface>(true))
                    if (s.name.StartsWith("NavMesh Kareth"))
                    {
                        EnemyBuilder.BakeNavMesh(s, ProjectSetup.Root + "/Scenes/Sandbox_M1_NavMesh.asset");
                        baked++;
                    }

                EditorSceneManager.MarkSceneDirty(world);
                EditorSceneManager.SaveScene(world);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Ashveil] Glockenrast (P3) additiv eingesetzt ({total.Elapsed.TotalSeconds:0} s): {spawns} Spawngebiete, " +
                          $"{ids}/2 vorhandene Figuren gekennzeichnet, NavMesh {(baked > 0 ? "neu gebacken" : "NICHT gefunden")}. Kareth unveraendert.");
                return spawns > 0 && ids == 2 && baked > 0;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Vorhandene Figur (aus dem Lager/Wegeleben) bekommt ihre Quest-Kennung, falls sie noch keine hat.</summary>
        private static int TagExistingNpc(string name, string npcId)
        {
            foreach (NpcRoutine r in Object.FindObjectsByType<NpcRoutine>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.gameObject.name != name) continue;
                NpcIdentity id = r.GetComponent<NpcIdentity>();
                if (id == null) id = r.gameObject.AddComponent<NpcIdentity>();
                id.Configure(npcId);
                EditorUtility.SetDirty(id);
                return 1;
            }
            Debug.LogWarning("[Ashveil] Glockenrast: Figur nicht gefunden – " + name);
            return 0;
        }
    }
}
