using System.Collections.Generic;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Items
{
    /// <summary>
    /// Server: persoenliche Beute einer Leiche – pro Profil genau einmal erzeugt. Erneutes Oeffnen,
    /// Wiederverbinden oder Neuladen der Anzeige erzeugt nichts Neues. Nicht Aufgenommenes bleibt liegen,
    /// solange die Leiche existiert.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CorpseLoot : MonoBehaviour
    {
        private static readonly List<CorpseLoot> AllCorpses = new List<CorpseLoot>();

        private readonly Dictionary<string, List<ItemStack>> _loot = new Dictionary<string, List<ItemStack>>();
        private readonly Dictionary<string, int> _generations = new Dictionary<string, int>();
        private bool _generated;

        public static IReadOnlyList<CorpseLoot> All => AllCorpses;
        public bool Generated => _generated;
        public IEnumerable<string> Recipients => _loot.Keys;

        private void OnEnable() { if (!AllCorpses.Contains(this)) AllCorpses.Add(this); }
        private void OnDisable() => AllCorpses.Remove(this);

        /// <summary>Einmalig beim Tod. Weitere Aufrufe tun nichts.</summary>
        /// <param name="allow">Optional (P3): (Profil, Gegenstand) – darf er fuer dieses Profil fallen? Auftragsbeute nur mit laufendem Auftrag.</param>
        public void Generate(LootTableDefinition table, IEnumerable<string> profiles, System.Func<string, CharacterClassDefinition> classOf, int seed,
            System.Func<string, ItemDefinition, bool> allow = null)
        {
            if (_generated) return;
            _generated = true;
            int n = 0;
            foreach (string p in profiles)
            {
                if (string.IsNullOrEmpty(p) || _loot.ContainsKey(p)) continue;
                var rng = new System.Random(unchecked(seed * 486187739 + (n++ + 1) * 16777619 + p.GetHashCode()));
                string who = p;
                _loot[p] = LootGenerator.Roll(table, classOf != null ? classOf(p) : null, rng, allow != null ? item => allow(who, item) : (System.Func<ItemDefinition, bool>)null);
                _generations[p] = _generations.TryGetValue(p, out int g) ? g + 1 : 1;
            }
        }

        /// <summary>Zusatzbeute fuer alle Berechtigten (z. B. gebrochene Koerperteile), nach <see cref="Generate"/>.</summary>
        public void AddBonus(IEnumerable<string> profiles, string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId) || count <= 0) return;
            foreach (string p in profiles)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (!_loot.TryGetValue(p, out List<ItemStack> l)) { l = new List<ItemStack>(); _loot[p] = l; }
                l.Add(new ItemStack(itemId, count, false));
            }
        }

        /// <summary>Test/Debug: feste Beute fuer ein Profil (ersetzt nichts, das schon erzeugt ist).</summary>
        public void SetFixed(string profile, List<ItemStack> items)
        {
            _generated = true;
            if (_loot.ContainsKey(profile)) return;
            _loot[profile] = new List<ItemStack>(items);
            _generations[profile] = 1;
        }

        public bool IsRecipient(string profile) => profile != null && _loot.ContainsKey(profile);

        public int GenerationCount(string profile) => _generations.TryGetValue(profile, out int g) ? g : 0;

        /// <summary>Kopie der verbleibenden Beute eines Profils (leer, wenn nicht berechtigt).</summary>
        public List<ItemStack> Get(string profile)
        {
            return profile != null && _loot.TryGetValue(profile, out List<ItemStack> l) ? new List<ItemStack>(l) : new List<ItemStack>();
        }

        public bool HasLootFor(string profile) => profile != null && _loot.TryGetValue(profile, out List<ItemStack> l) && l.Count > 0;

        public bool HasAnyLoot
        {
            get
            {
                foreach (List<ItemStack> l in _loot.Values) if (l.Count > 0) return true;
                foreach (Salvage v in _salvage.Values) if (v.Items.Count > 0) return true;   // Leiche bleibt fuer die Nachlese liegen
                return false;
            }
        }

        // ------------------------------------------------------------------ Nachlese (nur fuer berechtigte Heiler)

        /// <summary>Persoenlicher Bergungsfund eines Heilers (unsichtbar fuer alle anderen, Klassen-Identitaet §34/§47).</summary>
        public sealed class Salvage
        {
            public List<ItemStack> Items = new List<ItemStack>();
            public bool Rare;
            public int EnemyLevel;
        }

        private readonly Dictionary<string, Salvage> _salvage = new Dictionary<string, Salvage>();
        private readonly HashSet<string> _salvageRolled = new HashSet<string>();

        /// <summary>Einmal je Heiler beim Tod. Auch ein leerer Fund wird vermerkt (kein zweiter Wurf).</summary>
        public void SetSalvage(string profile, List<ItemStack> items, bool rare, int enemyLevel)
        {
            if (string.IsNullOrEmpty(profile) || !_salvageRolled.Add(profile)) return;
            if (items == null || items.Count == 0) return;
            _salvage[profile] = new Salvage { Items = new List<ItemStack>(items), Rare = rare, EnemyLevel = enemyLevel };
        }

        public bool SalvageRolledFor(string profile) => profile != null && _salvageRolled.Contains(profile);
        public bool HasSalvageFor(string profile) => profile != null && _salvage.TryGetValue(profile, out Salvage v) && v.Items.Count > 0;
        public Salvage PeekSalvage(string profile) => profile != null && _salvage.TryGetValue(profile, out Salvage v) ? v : null;
        public IEnumerable<string> SalvageRecipients => _salvage.Keys;

        /// <summary>Nach dem Bergen: was nicht in den Rucksack passte, bleibt liegen.</summary>
        public void KeepSalvage(string profile, List<ItemStack> rest)
        {
            if (!_salvage.TryGetValue(profile, out Salvage v)) return;
            v.Items = rest ?? new List<ItemStack>();
        }

        /// <summary>
        /// Nimmt aus Platz <paramref name="index"/> so viel, wie <paramref name="take"/> annimmt, und gibt die
        /// aufgenommene Menge zurueck. Der Rest bleibt liegen.
        /// </summary>
        public int TakeInto(string profile, int index, System.Func<ItemStack, int> take)
        {
            if (!_loot.TryGetValue(profile, out List<ItemStack> list) || index < 0 || index >= list.Count) return 0;
            ItemStack s = list[index];
            int taken = Mathf.Clamp(take(s), 0, s.count);
            if (taken <= 0) return 0;
            if (taken >= s.count) list.RemoveAt(index);
            else list[index] = s.WithCount(s.count - taken);
            return taken;
        }

        public int ItemCount(string profile) => _loot.TryGetValue(profile, out List<ItemStack> l) ? l.Count : 0;
    }
}
