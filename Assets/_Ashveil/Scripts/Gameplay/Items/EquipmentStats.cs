using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Items
{
    /// <summary>Summe aller Werte der angelegten Ausruestung + daraus abgeleitete Kampfwerte.</summary>
    public struct EquipmentStats
    {
        public int Strength, Agility, Intellect, Stamina, Willpower, Armor;
        /// <summary>Blockwert aller angelegten Schilde (0 = kein Schild, kein Block).</summary>
        public int ShieldBlockValue;
        public float DamagePercent, CritPercent, LifeOnHit, ArmorPercent, HealthPercent;
        /// <summary>Talente (P1): Nahkampf-/Fernkampfschaden, Ausweichen und Block in %.</summary>
        public float MeleeDamagePercent, RangedDamagePercent, DodgePercent, BlockPercent;
        /// <summary>Charakterstufe (Grundleben und Schaden wachsen mit).</summary>
        public int Level;

        public const float BaseHealth = 100f;
        public const float HealthPerStamina = 10f;

        public float MaxHealth => (BaseHealth + Progression.HealthPerLevel * (Mathf.Max(1, Level) - 1) + Stamina * HealthPerStamina) * (1f + HealthPercent / 100f);
        public float TotalArmor => Armor * (1f + ArmorPercent / 100f);

        /// <summary>Anteil abgewehrten Schadens (0..0.75).</summary>
        public float ArmorReduction(int level) => Mathf.Clamp(TotalArmor / (TotalArmor + 200f + 40f * level), 0f, 0.75f);

        public float DamageMultiplier(bool melee) =>
            1f + (melee ? Strength : Agility) * 0.01f + (DamagePercent + (melee ? MeleeDamagePercent : RangedDamagePercent)) / 100f +
            Progression.DamagePerLevel * (Mathf.Max(1, Level) - 1);

        /// <summary>Talente einrechnen (Server und Client gleich).</summary>
        public void AddTalents(TalentBonuses t)
        {
            if (t == null) return;
            CritPercent += t.Get(TalentStat.CritPercent);
            ArmorPercent += t.Get(TalentStat.ArmorPercent);
            HealthPercent += t.Get(TalentStat.HealthPercent);
            MeleeDamagePercent += t.Get(TalentStat.MeleeDamagePercent);
            RangedDamagePercent += t.Get(TalentStat.RangedDamagePercent);
            DodgePercent += t.Get(TalentStat.DodgePercent);
            BlockPercent += t.Get(TalentStat.BlockPercent);
        }

        public float CritChance => CritPercent / 100f + Agility * 0.0005f;

        public bool HasShield => ShieldBlockValue > 0;
        /// <summary>Grund-Blockchance mit Schild (Classic: kleiner fester Anteil, keine Taste, kein Timing).</summary>
        public const float BaseBlockChance = 0.10f;
        /// <summary>Blockwert: Schild + halbe Staerke.</summary>
        public float BlockValue => HasShield ? ShieldBlockValue + Strength * 0.5f : 0f;

        public int Get(StatType t)
        {
            switch (t)
            {
                case StatType.Strength: return Strength;
                case StatType.Agility: return Agility;
                case StatType.Intellect: return Intellect;
                case StatType.Stamina: return Stamina;
                case StatType.Willpower: return Willpower;
                default: return Armor;
            }
        }

        public void Add(ItemDefinition def)
        {
            if (def == null) return;
            Armor += def.armor;
            ShieldBlockValue += def.BlockValue;
            foreach (StatModifier m in def.stats)
            {
                switch (m.stat)
                {
                    case StatType.Strength: Strength += m.value; break;
                    case StatType.Agility: Agility += m.value; break;
                    case StatType.Intellect: Intellect += m.value; break;
                    case StatType.Stamina: Stamina += m.value; break;
                    case StatType.Willpower: Willpower += m.value; break;
                    case StatType.Armor: Armor += m.value; break;
                }
            }
            foreach (ItemEffect e in def.effects)
            {
                switch (e.type)
                {
                    case ItemEffectType.DamagePercent: DamagePercent += e.value; break;
                    case ItemEffectType.CritChance: CritPercent += e.value; break;
                    case ItemEffectType.LifeOnHit: LifeOnHit += e.value; break;
                    case ItemEffectType.ArmorPercent: ArmorPercent += e.value; break;
                    case ItemEffectType.HealthPercent: HealthPercent += e.value; break;
                }
            }
        }

        public static EquipmentStats From(ItemStack[] equipment, int level = 1)
        {
            var s = new EquipmentStats { Level = Mathf.Max(1, level) };
            if (equipment == null) return s;
            foreach (ItemStack st in equipment)
                if (!st.IsEmpty) s.Add(GameData.Item(st.id));
            return s;
        }
    }
}
