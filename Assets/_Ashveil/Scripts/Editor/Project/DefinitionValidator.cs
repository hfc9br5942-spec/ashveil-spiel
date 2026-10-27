using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Findet alle <see cref="GameDefinition"/>-Assets, meldet fehlende/doppelte IDs und
    /// schreibt sie in die DefinitionDatabase (wird bei Bedarf angelegt).
    /// </summary>
    public static class DefinitionValidator
    {
        private const string DatabasePath = ProjectSetup.Root + "/Data/DefinitionDatabase.asset";

        [MenuItem("Ashveil/Daten/Definitionen pruefen & Datenbank aktualisieren", priority = 200)]
        public static void ValidateMenu() => ValidateAndRebuild();

        public static DefinitionDatabase ValidateAndRebuild()
        {
            ProjectSetup.EnsureFolder(ProjectSetup.Root + "/Data");

            var all = new List<GameDefinition>();
            var seen = new Dictionary<string, GameDefinition>();
            int errors = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(GameDefinition)))
            {
                var def = AssetDatabase.LoadAssetAtPath<GameDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null) continue;

                if (string.IsNullOrWhiteSpace(def.Id))
                {
                    Debug.LogError($"[Ashveil] {def.name} hat keine ID.", def);
                    errors++;
                    continue;
                }
                if (seen.TryGetValue(def.Id, out GameDefinition other))
                {
                    Debug.LogError($"[Ashveil] Doppelte ID '{def.Id}': {def.name} und {other.name}.", def);
                    errors++;
                    continue;
                }
                seen.Add(def.Id, def);
                all.Add(def);
            }

            all.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            // Auftraege: NPCs, Ziele und Belohnungen muessen existieren
            foreach (GameDefinition d in all)
            {
                if (!(d is QuestDefinition q)) continue;
                void Need(string id, string what)
                {
                    if (string.IsNullOrEmpty(id) || !seen.ContainsKey(id))
                    {
                        Debug.LogError($"[Ashveil] Auftrag '{q.Id}': {what} '{id}' fehlt.", q);
                        errors++;
                    }
                }
                Need(q.giverNpc, "Geber");
                Need(q.TurnInNpc, "Abgabe-NPC");
                foreach (QuestObjective o in q.objectives)
                    if (o.type != QuestObjectiveType.Explore && o.type != QuestObjectiveType.Story) Need(o.targetId, "Ziel");
                foreach (QuestItemReward r in q.rewardItems) if (r.item == null) { Debug.LogError($"[Ashveil] Auftrag '{q.Id}': leere Belohnung.", q); errors++; }
                foreach (QuestItemReward r in q.choiceItems) if (r.item == null) { Debug.LogError($"[Ashveil] Auftrag '{q.Id}': leere Wahlbelohnung.", q); errors++; }
            }

            var db = AssetDatabase.LoadAssetAtPath<DefinitionDatabase>(DatabasePath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<DefinitionDatabase>();
                AssetDatabase.CreateAsset(db, DatabasePath);
            }
            db.EditorSetDefinitions(all);
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            string msg = $"[Ashveil] {all.Count} Definitionen in der Datenbank, {errors} Fehler.";
            if (errors == 0) Debug.Log(msg, db);
            else Debug.LogWarning(msg, db);
            return db;
        }
    }
}
