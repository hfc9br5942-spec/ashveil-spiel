using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Kolosseum Pass 3: Wellen-Daten (Kanon §33–35, Startwerte aus Pass 1 §5.4). Zwoelf Bloecke à 5 Minuten, Budget je Minute
    /// fuer 5 Spieler, Obergrenze gleichzeitiger Gegner, Elite-Anteil, Eingaenge je Block.
    /// PLATZHALTER-Pool: vorhandene Gegner aus Kareth und den Salzgruben auf Stufe 14–16; eigene Arena-Archetypen
    /// (Schwarm mit einfacher KI, Beschwoerer, Jaeger, Stoerer) und Bosse kommen in Pass 4/5.
    /// Werte im Inspector aenderbar – der Builder ueberschreibt nur, wenn seine Version neuer ist.
    /// </summary>
    public static class ColosseumContentBuilder
    {
        public const string Folder = ProjectSetup.Root + "/Data/Colosseum";
        public const string DefinitionPath = Folder + "/Kolosseum.asset";
        private const int Version = 1;

        public static ColosseumDefinition EnsureDefinition()
        {
            ProjectSetup.EnsureFolder(Folder);
            var def = AssetDatabase.LoadAssetAtPath<ColosseumDefinition>(DefinitionPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<ColosseumDefinition>();
                AssetDatabase.CreateAsset(def, DefinitionPath);
            }
            if (def.builderVersion >= Version && def.phases.Count > 0 && def.pool.Count > 0) return def;

            def.builderVersion = Version;
            def.runSeconds = 600f;
            def.startDelay = 10f;
            def.baseLevel = 14;
            def.maxLevel = 16;
            def.groupBase = 0.5f;
            def.groupPerPlayer = 0.25f;
            def.minAliveCap = 5;

            string[] names = { "Prüfung I", "Prüfung II", "Prüfung III", "Prüfung IV", "Prüfung V", "Prüfung VI", "Prüfung VII", "Prüfung VIII",
                               "Prüfung IX", "Prüfung X", "Prüfung XI", "Der letzte Stand" };
            float[] budget = { 10, 14, 18, 20, 23, 26, 28, 31, 34, 36, 40, 44 };
            int[] cap = { 18, 22, 26, 30, 33, 36, 38, 41, 44, 48, 48, 60 };
            float[] elite = { 0f, 0.05f, 0.08f, 0.10f, 0.12f, 0.15f, 0.15f, 0.18f, 0.20f, 0.20f, 0.25f, 0.25f };
            int[] bonus = { 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 2 };
            def.phases = new List<WavePhaseDefinition>();
            for (int i = 0; i < 12; i++)
                def.phases.Add(new WavePhaseDefinition
                {
                    displayName = names[i], startMinute = i * 5f, endMinute = (i + 1) * 5f, budgetPerMinute = budget[i], aliveCap = cap[i],
                    eliteChance = elite[i], levelBonus = bonus[i],
                    entries = i == 0 ? ArenaEntryMask.Gate : i == 1 ? ArenaEntryMask.Gate | ArenaEntryMask.FloorOpening : ArenaEntryMask.All,
                    packInterval = i < 3 ? new Vector2(7f, 12f) : i < 9 ? new Vector2(6f, 10f) : new Vector2(4f, 8f)
                });

            GameObject P(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(EnemyBuilder.PrefabFolder + "/" + name + ".prefab");
            def.pool = new List<ArenaPoolEntry>
            {
                new ArenaPoolEntry { role = "Schwarm", prefab = P("Enemy_Grubenarbeiter"), cost = 1, weight = 3f, packMin = 3, packMax = 5, entries = ArenaEntryMask.Gate },
                new ArenaPoolEntry { role = "Schwarm (schnell)", prefab = P("Enemy_Aschwolf"), cost = 1, weight = 2f, packMin = 2, packMax = 4, entries = ArenaEntryMask.Gate },
                new ArenaPoolEntry { role = "Brut aus der Tiefe", prefab = P("Enemy_Krebsbrut"), cost = 1, weight = 1.5f, packMin = 3, packMax = 5, entries = ArenaEntryMask.FloorOpening },
                new ArenaPoolEntry { role = "Fernkampf", prefab = P("Enemy_VerblicheneSchuetzin"), cost = 2, weight = 1.5f, minPhase = 1, packMin = 1, packMax = 2,
                                     entries = ArenaEntryMask.Gate | ArenaEntryMask.Gallery },
                new ArenaPoolEntry { role = "Bruiser", prefab = P("Enemy_Salzkrebs"), cost = 2, weight = 1f, minPhase = 1, packMin = 1, packMax = 2, entries = ArenaEntryMask.Gate },
                new ArenaPoolEntry { role = "Elite-Bruiser", prefab = P("Enemy_Grubenschrat"), cost = 5, weight = 1f, minPhase = 1, elite = true, packMin = 1, packMax = 1,
                                     entries = ArenaEntryMask.Gate | ArenaEntryMask.FloorOpening },
                new ArenaPoolEntry { role = "Elite-Trupp", prefab = P("Enemy_Deserteur"), cost = 5, weight = 1f, minPhase = 2, elite = true, packMin = 1, packMax = 2,
                                     entries = ArenaEntryMask.Gate },
            };
            foreach (ArenaPoolEntry e in def.pool)
                if (e.prefab == null) Debug.LogWarning($"[Ashveil] Kolosseum: Gegner-Prefab fuer '{e.role}' fehlt.");
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            return def;
        }
    }
}
