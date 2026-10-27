using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// P2 Balance (30.09.): ordnet jede Gegnerart einer Rolle der Balance-Tabelle zu (<see cref="Balance"/>). Leben und
    /// Schaden kommen dann aus Stufe + Rolle statt aus Einzelwerten. Der Glockenhirte (Weltboss) und die Maehnenreisserin
    /// (Klassenquest) behalten ihre eigenen Werte. Laeuft bei jedem Sandbox-Aufbau (idempotent).
    /// Feinabstimmung je Art: Leben- und Schadensfaktor gegenueber der Rolle.
    /// </summary>
    public static class BalanceRoleBuilder
    {
        private struct Entry
        {
            public BalanceRole Role;
            public float Health, Damage;
            public Entry(BalanceRole role, float health = 1f, float damage = 1f) { Role = role; Health = health; Damage = damage; }
        }

        private static readonly Dictionary<string, Entry> Roles = new Dictionary<string, Entry>
        {
            // Kareth
            { "enemy.ash_wolf", new Entry(BalanceRole.Normal, 0.85f, 0.95f) },       // schnell, flieht bei 15 %
            { "enemy.faded_one", new Entry(BalanceRole.Normal) },
            { "enemy.faded_gunner", new Entry(BalanceRole.Normal, 0.8f, 1f) },       // Fernkampf, haelt Abstand
            { "enemy.peat_brute", new Entry(BalanceRole.Strong) },                   // Feuer wirkt 1,5-fach
            { "enemy.needle_spitter", new Entry(BalanceRole.Normal, 0.85f, 0.7f) },  // dazu Gift
            { "enemy.soot_lynx", new Entry(BalanceRole.Elite, 0.7f, 0.9f) },         // seltene Elite, jagt Einzelgaenger
            // #28 Kareth v4, Welle 0: Greybox-Tiere und Kareth-Varianten (Werte Claude, Rolle Normal)
            { "enemy.furrow_rooter", new Entry(BalanceRole.Normal, 0.85f, 0.85f) },  // Stufe 1–3, erstes neutrales Tier
            { "enemy.shore_clacker", new Entry(BalanceRole.Normal, 1f, 0.9f) },      // Panzer, langsam
            { "enemy.silt_slinger", new Entry(BalanceRole.Normal, 1f, 0.95f) },      // dazu Schlickwurf (×0,8)
            { "enemy.whisper_bellows", new Entry(BalanceRole.Normal, 0.85f, 0.95f) },// Satz und Schallstoss (Flaeche)
            { "enemy.reed_deserter", new Entry(BalanceRole.Normal, 1.15f, 1f) },     // allein, verteidigt sein Versteck (U-7)
            { "enemy.wreck_sailor", new Entry(BalanceRole.Normal) },                 // U-3, normaler Gegner (nicht Gruppe)
            // Grenzgebirge (16–22)
            { "enemy.ridge_wolf", new Entry(BalanceRole.Normal, 0.85f, 0.95f) },
            { "enemy.deserter", new Entry(BalanceRole.Normal) },
            { "enemy.deserter_gunner", new Entry(BalanceRole.Normal, 0.8f, 1f) },
            { "enemy.cleft_spitter", new Entry(BalanceRole.Normal, 0.85f, 0.7f) },
            { "enemy.rock_brute", new Entry(BalanceRole.Elite, 0.8f, 1f) },          // Elite am Hochpass
            // Salzgruben (10–14, Gruppe)
            { "enemy.mine_worker", new Entry(BalanceRole.DungeonTrash) },
            { "enemy.salt_crab", new Entry(BalanceRole.DungeonTrash, 1.2f, 0.9f) },
            { "enemy.needle_brood", new Entry(BalanceRole.DungeonTrash, 0.5f, 0.7f) },
            { "enemy.light_eater", new Entry(BalanceRole.DungeonTrash, 0.9f, 1f) },
            { "enemy.drowned_sailor", new Entry(BalanceRole.DungeonTrash) },
            { "enemy.crab_brood", new Entry(BalanceRole.DungeonTrash, 0.35f, 0.5f) }, // Brut der Salzmutter
            { "enemy.salt_brute", new Entry(BalanceRole.Elite, 0.8f, 1f) },           // Grubenschrat (Zwischengegner, Kolosseum)
            { "enemy.foreman_hask", new Entry(BalanceRole.DungeonBoss, 0.8f, 0.9f) },
            { "enemy.salt_mother", new Entry(BalanceRole.DungeonBoss, 0.9f, 0.9f) },
            { "enemy.drowned_captain", new Entry(BalanceRole.DungeonBoss, 0.85f, 0.9f) },
            { "enemy.mine_heart", new Entry(BalanceRole.DungeonBoss, 1.15f, 1f) },
        };

        public static int Apply()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition"))
            {
                var d = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (d == null || !Roles.TryGetValue(d.Id, out Entry e)) continue;
                if (d.balanceRole == e.Role && d.healthFactor == e.Health && d.damageFactor == e.Damage) continue;
                d.balanceRole = e.Role;
                d.healthFactor = e.Health;
                d.damageFactor = e.Damage;
                EditorUtility.SetDirty(d);
                n++;
            }
            if (n > 0) AssetDatabase.SaveAssets();
            return n;
        }
    }
}
