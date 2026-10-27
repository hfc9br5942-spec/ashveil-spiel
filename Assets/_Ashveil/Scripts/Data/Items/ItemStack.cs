using System;

namespace Ashveil.Data
{
    /// <summary>
    /// Ein Stapel Gegenstaende in einem Platz – nur ID, Menge und Bindung. So klein, dass er unveraendert
    /// uebers Netz geht und im Speicherstand landet. Spaeter kommen Haltbarkeit/Affixe als weitere Felder dazu.
    /// </summary>
    [Serializable]
    public struct ItemStack : IEquatable<ItemStack>
    {
        public string id;
        public int count;
        public bool bound;

        public ItemStack(string id, int count, bool bound = false)
        {
            this.id = id;
            this.count = count;
            this.bound = bound;
        }

        public bool IsEmpty => string.IsNullOrEmpty(id) || count <= 0;

        public static readonly ItemStack Empty = default;

        /// <summary>Kann mit <paramref name="other"/> zu einem Stapel zusammengelegt werden?</summary>
        public bool SameKind(in ItemStack other) => !IsEmpty && !other.IsEmpty && id == other.id && bound == other.bound;

        public ItemStack WithCount(int n) => n <= 0 ? Empty : new ItemStack(id, n, bound);

        public bool Equals(ItemStack o) => (IsEmpty && o.IsEmpty) || (id == o.id && count == o.count && bound == o.bound);
        public override bool Equals(object obj) => obj is ItemStack o && Equals(o);
        public override int GetHashCode() => IsEmpty ? 0 : (id.GetHashCode() * 31 + count) * 2 + (bound ? 1 : 0);
        public override string ToString() => IsEmpty ? "-" : $"{id}x{count}{(bound ? "*" : "")}";
    }

    [Serializable]
    public struct ItemAmount
    {
        public ItemDefinition item;
        public int count;
    }

    [Serializable]
    public struct StatModifier
    {
        public StatType stat;
        public int value;
    }

    [Serializable]
    public struct ItemEffect
    {
        public ItemEffectType type;
        public float value;
        [UnityEngine.Tooltip("Optionaler eigener Text im Tooltip (sonst automatisch)")]
        public string text;
    }
}
