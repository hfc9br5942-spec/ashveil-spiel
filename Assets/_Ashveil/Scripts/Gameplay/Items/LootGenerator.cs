using System.Collections.Generic;
using Ashveil.Data;

namespace Ashveil.Gameplay.Items
{
    /// <summary>Wuerfelt eine Beutetabelle fuer genau einen Empfaenger (persoenliche Beute).</summary>
    public static class LootGenerator
    {
        /// <param name="allow">Optional (P3): darf dieser Gegenstand fuer den Empfaenger fallen? (Auftragsbeute nur mit laufendem Auftrag)</param>
        public static List<ItemStack> Roll(LootTableDefinition table, CharacterClassDefinition recipientClass, System.Random rng,
            System.Func<ItemDefinition, bool> allow = null)
        {
            var result = new List<ItemStack>();
            if (table == null) return result;

            foreach (LootTableDefinition.Entry e in table.entries)
            {
                if (e.item == null || rng.NextDouble() >= e.chance) continue;
                int amount = Count(rng, e.minCount, e.maxCount);
                if (allow != null && !allow(e.item)) continue; // erst wuerfeln, dann filtern: der Zufall der uebrigen Eintraege bleibt gleich
                AddMerged(result, e.item, amount);
            }

            var pool = new List<LootTableDefinition.Weighted>();
            foreach (LootTableDefinition.Group g in table.groups)
            {
                if (g.items == null || g.items.Count == 0) continue;
                if (rng.NextDouble() >= g.chance) continue;
                for (int p = 0; p < g.picks; p++)
                {
                    pool.Clear();
                    // Ueberwiegend fuer die eigene Klasse: mit Wahrscheinlichkeit classBias nur Tragbares ziehen
                    bool restrict = g.classAware && recipientClass != null && rng.NextDouble() < g.classBias;
                    foreach (LootTableDefinition.Weighted w in g.items)
                    {
                        if (w.item == null || w.weight <= 0f) continue;
                        if (allow != null && !allow(w.item)) continue;
                        if (restrict && !InventoryModel.UsableByClass(w.item, recipientClass)) continue;
                        pool.Add(w);
                    }
                    if (pool.Count == 0)
                        foreach (LootTableDefinition.Weighted w in g.items)
                            if (w.item != null && w.weight > 0f && (allow == null || allow(w.item))) pool.Add(w);
                    if (pool.Count == 0) continue;

                    float total = 0f;
                    foreach (LootTableDefinition.Weighted w in pool) total += w.weight;
                    double r = rng.NextDouble() * total;
                    LootTableDefinition.Weighted pick = pool[pool.Count - 1];
                    foreach (LootTableDefinition.Weighted w in pool)
                    {
                        r -= w.weight;
                        if (r <= 0) { pick = w; break; }
                    }
                    AddMerged(result, pick.item, Count(rng, pick.minCount, pick.maxCount));
                }
            }
            return result;
        }

        private static int Count(System.Random rng, int min, int max)
        {
            min = System.Math.Max(1, min);
            max = System.Math.Max(min, max);
            return rng.Next(min, max + 1);
        }

        /// <summary>Gleiche Items zusammenfassen, aber nie ueber die Stapelgroesse hinaus.</summary>
        private static void AddMerged(List<ItemStack> list, ItemDefinition def, int count)
        {
            int max = System.Math.Max(1, def.maxStack);
            for (int i = 0; i < list.Count && count > 0; i++)
            {
                if (list[i].id != def.Id || list[i].count >= max) continue;
                int put = System.Math.Min(count, max - list[i].count);
                list[i] = list[i].WithCount(list[i].count + put);
                count -= put;
            }
            while (count > 0)
            {
                int put = System.Math.Min(count, max);
                list.Add(new ItemStack(def.Id, put, false));
                count -= put;
            }
        }
    }
}
