using Ashveil.Data;
using Ashveil.Gameplay.Enemies;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// P3 Glockenrast (Stufe 5–9): zusaetzliche Spawngebiete fuer die Auftraege – nur neue Gruppen, die bestehenden bleiben.
    /// Vorhandene Gegner (Nadelspeier, Verblichener, Torfschrat mit Rolle Stark) – keine neuen Arten noetig.
    ///   G2/G3 Moorrand: je zwei Nadelspeier mehr (8 Siege, Giftdruesen ohne Wartezeit)
    ///   G5 Holzfaellerlager: drei Verblichene mehr am Suedrand (6 Siege)
    ///   G9 Aschenweide: vier Torfschrate mehr (mit dem vorhandenen fuenf, Vorgabe 5–7)
    ///   G12 Nordmoor: ein Torfschrat und drei Nadelspeier mehr (3 + 3 „Moorgegner“)
    /// </summary>
    public static partial class EnemyBuilder
    {
        private static void BuildGlockenrastSpawns(Transform enemies, EnemyDefinition husk, GameObject huskPrefab, EnemyDefinition brute, GameObject brutePrefab,
            EnemyDefinition spitter, GameObject spitterPrefab)
        {
            Vector3 lc = Ground(KarethTerrainBuilder.LumberCamp.x, KarethTerrainBuilder.LumberCamp.z);
            SetAggro(Area(enemies, "Holzfaeller Nahkampf (Glockenrast)", lc + new Vector3(-8f, 0f, -7f), husk, huskPrefab, 3, 4f, "Holzfaellerlager", 5, 7),
                AggroProfile.Aggressive);
            Area(enemies, "Nadelspeier Moorrand West (Glockenrast)", Ground(-80f, 318f), spitter, spitterPrefab, 2, 5f, "", 5, 6);
            Area(enemies, "Nadelspeier Moorrand Ost (Glockenrast)", Ground(156f, 282f), spitter, spitterPrefab, 2, 5f, "", 5, 6);
            Area(enemies, "Torfschrat Aschenweide (Glockenrast)", Ground(104f, 390f), brute, brutePrefab, 4, 9f, "", 7, 8);
            Area(enemies, "Torfschrat Nordmoor (Glockenrast)", Ground(-44f, 432f), brute, brutePrefab, 1, 4f, "", 8, 9);
            Area(enemies, "Nadelspeier Nordmoor", Ground(-52f, 404f), spitter, spitterPrefab, 3, 6f, "", 8, 9);
        }

        /// <summary>Namen der P3-Spawngebiete (zum Ersetzen beim additiven Einfuegen).</summary>
        public static readonly string[] GlockenrastSpawnNames =
        {
            "Holzfaeller Nahkampf (Glockenrast)", "Nadelspeier Moorrand West (Glockenrast)", "Nadelspeier Moorrand Ost (Glockenrast)",
            "Torfschrat Aschenweide (Glockenrast)", "Torfschrat Nordmoor (Glockenrast)", "Nadelspeier Nordmoor"
        };

        /// <summary>
        /// 12h-Block 01.10.: P3-Spawngebiete in die BESTEHENDE Szene setzen (ohne Neuaufbau, Kareth bleibt unberuehrt).
        /// Nutzt die vorhandenen Definitionen und Prefabs, ersetzt nur fruehere P3-Gebiete. Gibt die Anzahl neuer Gebiete zurueck.
        /// </summary>
        public static int InsertGlockenrastSpawns(Transform env)
        {
            Transform enemies = env.Find("Gegner");
            if (enemies == null) { Debug.LogError("[Ashveil] Glockenrast: Gruppe 'Gegner' fehlt."); return 0; }
            var husk = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefFolder + "/Enemy_Verblichener.asset");
            var brute = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefFolder + "/Enemy_Torfschrat.asset");
            var spitter = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefFolder + "/Enemy_Nadelspeier.asset");
            var huskPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Enemy_Verblichener.prefab");
            var brutePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Enemy_Torfschrat.prefab");
            var spitterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/Enemy_Nadelspeier.prefab");
            if (husk == null || brute == null || spitter == null || huskPrefab == null || brutePrefab == null || spitterPrefab == null)
            {
                Debug.LogError("[Ashveil] Glockenrast: Gegner-Definition oder -Prefab fehlt (Verblichener, Torfschrat, Nadelspeier).");
                return 0;
            }
            foreach (string n in GlockenrastSpawnNames)
                for (Transform old = enemies.Find(n); old != null; old = enemies.Find(n))
                    Object.DestroyImmediate(old.gameObject);
            BuildGlockenrastSpawns(enemies, husk, huskPrefab, brute, brutePrefab, spitter, spitterPrefab);
            return GlockenrastSpawnNames.Length;
        }
    }
}
