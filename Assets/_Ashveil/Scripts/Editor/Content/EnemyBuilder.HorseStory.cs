using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Networking;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Pferde-Geschichte: Quest-Raubtier „Maehnenreisser“ (Arbeitsname) als Variante des Aschwolfs – ein alter Leitruede
    /// (Max 29.09.: Aschwolf-Leitruede). Woelfe treiben Herden gezielt; genau das erzaehlen die Hinweise. Zuege: Biss,
    /// schwerer Biss (deutlich angekuendigt), Ansturm; den Sprung zur Herde steuert die Begegnung (EnemyBrain.ServerLeapAt).
    /// Dazu das Herden-Netzobjekt und die Inhalts-Liste unter Resources/Story.
    /// </summary>
    public static partial class EnemyBuilder
    {
        public const string ManeRipperId = "enemy.human_tank.maneripper";
        public const string StoryFolder = ProjectSetup.Root + "/Resources/Story";

        private static void SetupManeRipper(EnemyDefinition d)
        {
            SetupWolf(d);
            d.minLevel = 4; d.maxLevel = 6;
            d.baseHealth = 380f; d.healthPerLevel = 55f;            // Mini-Boss einer fruehen Klassenquest
            d.minDamage = 5f; d.maxDamage = 8f; d.damagePerLevel = 1.4f;
            d.attackRange = 2.2f; d.attackInterval = 1.7f; d.windup = 0.5f;
            d.walkSpeed = 1.8f; d.runSpeed = 7.2f;
            d.aggroRadius = 0f; d.leashDistance = 95f; d.aggroProfile = AggroProfile.Passive; d.howlsOnEngage = true;
            d.fleeHealthFraction = 0f;                                  // flieht nicht – er hat die Herde nicht umsonst getrieben
            d.corpseTime = 40f; d.respawnTime = 99999f;
            d.bodyColor = new Color(0.13f, 0.12f, 0.11f); d.glowColor = new Color(0.75f, 0.42f, 0.18f); d.scale = 1.6f;
            d.elite = true;
            d.moveset = new List<EnemyAttackDefinition>
            {
                new EnemyAttackDefinition
                {
                    id = 1, displayName = "Biss", kind = EnemyMoveKind.Strike, pose = EnemyMovePose.Default,
                    minRange = 0f, maxRange = 2.4f, windup = 0.55f, damageMultiplier = 1f, cooldown = 0f, weight = 4f, arc = 120f, knockback = 0.3f
                },
                new EnemyAttackDefinition
                {
                    id = 2, displayName = "Schwerer Biss", kind = EnemyMoveKind.Strike, pose = EnemyMovePose.Default,
                    minRange = 0f, maxRange = 2.6f, windup = 1.15f, damageMultiplier = 1.9f, cooldown = 7f, weight = 2f, arc = 110f, knockback = 0.9f,
                    retreatAfter = 2.5f
                },
                new EnemyAttackDefinition
                {
                    id = 3, displayName = "Ansturm", kind = EnemyMoveKind.Charge, pose = EnemyMovePose.Charge,
                    minRange = 6f, maxRange = 14f, windup = 1.2f, damageMultiplier = 1.4f, cooldown = 11f, weight = 3f, arc = 360f, knockback = 1.1f
                }
            };
        }

        /// <summary>Raubtier, Herden-Objekt und Inhalts-Liste (idempotent). Rueckgabe: die Inhalts-Liste.</summary>
        public static HorseStoryContent BuildHorseStoryContent()
        {
            ProjectSetup.EnsureFolder(DefFolder);
            ProjectSetup.EnsureFolder(PrefabFolder);
            ProjectSetup.EnsureFolder(StoryFolder);
            CreateMaterials();
            Loc.Reload();
            // Das Raubtier immer mit den Werten aus dem Code bauen (Greybox-Stand)
            EnemyDefinition def = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefFolder + "/Enemy_Maehnenreisser.asset");
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<EnemyDefinition>();
                AssetDatabase.CreateAsset(def, DefFolder + "/Enemy_Maehnenreisser.asset");
            }
            SetupManeRipper(def);
            K.SetString(def, "id", ManeRipperId);
            K.SetString(def, "displayName", Loc.T("enemy.human_tank.maneripper.name"));
            EditorUtility.SetDirty(def);
            GameObject ripper = BuildBeastPrefab(def);

            // Herden-Objekt (ein NetworkObject, Tiere rechnet der Client)
            var root = new GameObject("HerdEncounter");
            root.AddComponent<NetworkObject>();
            root.AddComponent<FishNet.Observing.NetworkObserver>();
            root.AddComponent<HerdEncounter>();
            string herdPath = StoryFolder + "/HerdEncounter.prefab";
            GameObject herd = PrefabUtility.SaveAsPrefabAsset(root, herdPath);
            Object.DestroyImmediate(root);
            var nob = herd.GetComponent<NetworkObject>();
            if (nob != null) { nob.SetSceneId(NetworkObject.UNSET_SCENEID_VALUE); EditorUtility.SetDirty(nob); }

            var content = AssetDatabase.LoadAssetAtPath<HorseStoryContent>(StoryFolder + "/HorseStoryContent.asset");
            if (content == null)
            {
                content = ScriptableObject.CreateInstance<HorseStoryContent>();
                AssetDatabase.CreateAsset(content, StoryFolder + "/HorseStoryContent.asset");
            }
            content.herdPrefab = herd;
            content.predatorPrefab = ripper;
            content.wolfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Enemy_Aschwolf.prefab");
            content.spitterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Enemy_Nadelspeier.prefab");
            content.lonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Enemy_Gratwolf.prefab"); // Grenzgebirge baut ihn
            EditorUtility.SetDirty(content);
            NetworkSetupBuilder.AddSpawnables(new[] { herd, ripper });
            AssetDatabase.SaveAssets();
            if (content.wolfPrefab == null || content.spitterPrefab == null || content.lonePrefab == null)
                Debug.LogWarning("[Ashveil] Pferde-Geschichte: eine Gegner-Vorlage fehlt (erst die Sandbox-Szene und das Grenzgebirge bauen).");
            return content;
        }
    }
}
