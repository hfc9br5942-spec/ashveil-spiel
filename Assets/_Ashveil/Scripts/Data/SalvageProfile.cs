using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Welcher Zustand eines Koerperteils beim Tod einen Bergungsstoff freigibt (Klassen-Identitaet §38–§40).</summary>
    public enum SalvagePartCondition : byte
    {
        /// <summary>egal, wie es dem Teil geht</summary>
        Any,
        /// <summary>unversehrt oder nur angeschlagen</summary>
        Intact,
        /// <summary>gebrochen, aber nicht zerstoert/abgetrennt</summary>
        Broken,
        /// <summary>zerstoert bzw. abgetrennt</summary>
        Destroyed
    }

    /// <summary>Ein moeglicher Bergungsstoff.</summary>
    [Serializable]
    public sealed class SalvageEntry
    {
        [Tooltip("Schluessel des Koerperteils (z. B. arm_r). Leer = der Koerper allgemein")]
        public string partKey = "";
        public SalvagePartCondition condition = SalvagePartCondition.Any;
        [Tooltip("Gegenstand, z. B. item.salt_marrow")]
        public string item = "";
        [Min(1)] public int minCount = 1;
        [Min(1)] public int maxCount = 1;
        [Tooltip("Gewicht unter allen passenden Eintraegen")]
        [Min(0f)] public float weight = 1f;
        [Tooltip("Seltener Stoff: die Meisterschaft Nachlese erhoeht sein Gewicht und er zaehlt mehr")]
        public bool rare;
    }

    /// <summary>
    /// Nachlese des Heilers (Klassen-Identitaet §32–§50, §174 SalvageProfile): was ein Heiler nach dem Kampf zusaetzlich
    /// bergen kann – je Koerperteil und Zustand, nur Materialien (keine Ausruestung, §36). Normaler Gruppenloot bleibt
    /// unveraendert (§34). Pro Gegner und Heiler begrenzte Wuerfe (§42: Trash 0–1, Elite 1, Boss 1–3).
    /// Liegt direkt an der <see cref="EnemyDefinition"/> – der Designer markiert den Gegner im Inspector (§177).
    /// </summary>
    [Serializable]
    public sealed class SalvageProfile
    {
        [Tooltip("Wuerfe je berechtigtem Heiler (0 = keine Nachlese). Trash 0–1, Elite 1, Boss 1–3")]
        [Range(0, 3)] public int rolls;
        [Tooltip("Chance je Wurf, dass ueberhaupt etwas zu finden ist")]
        [Range(0f, 1f)] public float chance = 1f;
        [Tooltip("Mindestbeitrag (Schaden + Heilung) als Anteil am Leben des Gegners – kein AFK-Mitlaufen (§43)")]
        [Range(0f, 0.5f)] public float minContribution = 0.03f;
        public List<SalvageEntry> entries = new List<SalvageEntry>();

        public bool Active => rolls > 0 && entries != null && entries.Count > 0;
    }

    /// <summary>Das Wuerfeln der Nachlese – reine Rechnung, damit Server und Tests dasselbe nutzen.</summary>
    public static class SalvageRoller
    {
        public struct Result
        {
            public List<ItemStack> Items;
            public bool Rare;
        }

        /// <summary>Passt der Eintrag zum Zustand beim Tod? <paramref name="stateOf"/> liefert null, wenn es das Teil nicht gibt.</summary>
        public static bool Matches(SalvageEntry e, Func<string, BodyPartState?> stateOf)
        {
            if (e == null || string.IsNullOrEmpty(e.item)) return false;
            if (string.IsNullOrEmpty(e.partKey)) return e.condition == SalvagePartCondition.Any;
            BodyPartState? s = stateOf != null ? stateOf(e.partKey) : null;
            if (s == null) return false;
            switch (e.condition)
            {
                case SalvagePartCondition.Intact: return s.Value < BodyPartState.Broken;
                case SalvagePartCondition.Broken: return s.Value == BodyPartState.Broken;
                case SalvagePartCondition.Destroyed: return s.Value == BodyPartState.Destroyed;
                default: return true;
            }
        }

        /// <summary>
        /// Wuerfelt alle Wuerfe eines Heilers. <paramref name="rareBonus"/> = Anteil mehr Gewicht fuer seltene Stoffe
        /// (Meisterschaft Nachlese), <paramref name="preserved"/> = spaeter „Bewahrt“ (§46) – leicht hoeher.
        /// </summary>
        public static Result Roll(SalvageProfile p, Func<string, BodyPartState?> stateOf, float rareBonus, System.Random rng, bool preserved = false)
        {
            var r = new Result { Items = new List<ItemStack>() };
            if (p == null || !p.Active || rng == null) return r;
            var pool = new List<SalvageEntry>();
            foreach (SalvageEntry e in p.entries) if (Matches(e, stateOf) && e.weight > 0f) pool.Add(e);
            if (pool.Count == 0) return r;
            float rareMul = 1f + Mathf.Max(0f, rareBonus) + (preserved ? 0.25f : 0f);
            for (int roll = 0; roll < p.rolls; roll++)
            {
                if (rng.NextDouble() >= p.chance) continue;
                float total = 0f;
                foreach (SalvageEntry e in pool) total += e.weight * (e.rare ? rareMul : 1f);
                double pick = rng.NextDouble() * total;
                SalvageEntry chosen = pool[pool.Count - 1];
                foreach (SalvageEntry e in pool)
                {
                    pick -= e.weight * (e.rare ? rareMul : 1f);
                    if (pick <= 0) { chosen = e; break; }
                }
                int n = chosen.maxCount > chosen.minCount ? rng.Next(chosen.minCount, chosen.maxCount + 1) : Mathf.Max(1, chosen.minCount);
                Add(r.Items, chosen.item, n);
                r.Rare |= chosen.rare;
            }
            return r;
        }

        private static void Add(List<ItemStack> list, string id, int n)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].id == id) { list[i] = list[i].WithCount(list[i].count + n); return; }
            list.Add(new ItemStack(id, n, false));
        }
    }
}
