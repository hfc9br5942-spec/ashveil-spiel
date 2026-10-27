using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Was ein Talentpunkt veraendert (je Rang <see cref="TalentDefinition.perRank"/>).</summary>
    public enum TalentStat : byte
    {
        CritPercent = 0,
        DodgePercent = 1,
        ArmorPercent = 2,
        HealthPercent = 3,
        MeleeDamagePercent = 4,
        RangedDamagePercent = 5,
        /// <summary>Zauberschaden (Glut) in %.</summary>
        SpellDamagePercent = 6,
        HealingPercent = 7,
        /// <summary>Weniger Kosten fuer <see cref="TalentDefinition.spellId"/> (Punkte der Ressource).</summary>
        SpellCostReduction = 8,
        /// <summary>Kuerzere Wirkzeit fuer <see cref="TalentDefinition.spellId"/> (s).</summary>
        CastTimeReduction = 9,
        /// <summary>Kuerzere Abklingzeit fuer <see cref="TalentDefinition.spellId"/> (s).</summary>
        CooldownReduction = 10,
        /// <summary>Staerkere Wirkung von <see cref="TalentDefinition.spellId"/> in %.</summary>
        SpellPowerPercent = 11,
        /// <summary>Schnellere Regeneration der Klassen-Ressource in %.</summary>
        ResourceRegenPercent = 12,
        /// <summary>Mehr Wut durch Schlagen und Getroffenwerden in %.</summary>
        RageGainPercent = 13,
        /// <summary>Mehr Bedrohung in %.</summary>
        ThreatPercent = 14,
        /// <summary>Hoehere Blockchance in %.</summary>
        BlockPercent = 15,
        /// <summary>Mehr Schaden des Gefaehrten in %.</summary>
        CompanionDamagePercent = 16,
        /// <summary>Laengere Betaeubung/Verlangsamung von <see cref="TalentDefinition.spellId"/> (s).</summary>
        EffectDurationBonus = 17,
        /// <summary>Mehr maximales Mana in %.</summary>
        ManaPercent = 18
    }

    /// <summary>
    /// Talent nach WoW Classic (Max 30.09.: ab Stufe 10 ein Punkt je Stufe). Drei Baeume je Klasse; eine Reihe (tier)
    /// braucht 5 Punkte je Reihe im selben Baum. Wirkungen sind einfache Zahlen, die Server und Client gleich rechnen.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Talent", fileName = "Talent_")]
    public sealed class TalentDefinition : GameDefinition
    {
        protected override string IdPrefix => "talent";

        public const int FirstTalentLevel = 10;
        public const int PointsPerTier = 5;

        public string classId;
        [Range(0, 2)] public int tree;
        [Tooltip("Reihe 0 … (braucht Reihe × 5 Punkte im Baum)")]
        public int tier;
        [Range(0, 3)] public int column;
        [Range(1, 5)] public int maxRank = 5;
        public TalentStat stat;
        public float perRank = 1f;
        [Tooltip("Betroffene Faehigkeit (fuer Kosten/Wirkzeit/Abklingzeit/Staerke)")]
        public string spellId;
        public IconShape iconShape = IconShape.Gem;
        public Color iconColor = new Color(0.8f, 0.7f, 0.5f);
        public int defaultsVersion;

        /// <summary>Talentpunkte fuer eine Stufe: ab 10 einer je Stufe (Stufe 10 = 1, 20 = 11).</summary>
        public static int PointsForLevel(int level) => Mathf.Max(0, level - FirstTalentLevel + 1);

        /// <summary>Beschreibung mit dem Wert fuer einen Rang (Platzhalter {v} in der Beschreibung).</summary>
        public string DescribeRank(int rank)
        {
            string d = Description ?? "";
            float v = perRank * Mathf.Max(1, rank);
            string vs = Mathf.Approximately(v, Mathf.Round(v)) ? v.ToString("0") : v.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
            return d.Replace("{v}", vs);
        }
    }

    /// <summary>Summe aller gewaehlten Talente eines Charakters (Server und Client rechnen gleich).</summary>
    public sealed class TalentBonuses
    {
        private readonly Dictionary<TalentStat, float> _stats = new Dictionary<TalentStat, float>();
        private readonly Dictionary<(TalentStat, string), float> _spell = new Dictionary<(TalentStat, string), float>();

        public static readonly TalentBonuses Empty = new TalentBonuses();

        public float Get(TalentStat s) => _stats.TryGetValue(s, out float v) ? v : 0f;
        public float ForSpell(TalentStat s, string spellId) => spellId != null && _spell.TryGetValue((s, spellId), out float v) ? v : 0f;

        public void Add(TalentDefinition t, int rank)
        {
            if (t == null || rank <= 0) return;
            float v = t.perRank * Mathf.Min(rank, t.maxRank);
            if (!string.IsNullOrEmpty(t.spellId))
            {
                var key = (t.stat, t.spellId);
                _spell[key] = (_spell.TryGetValue(key, out float old) ? old : 0f) + v;
            }
            else _stats[t.stat] = Get(t.stat) + v;
        }

        public void Clear()
        {
            _stats.Clear();
            _spell.Clear();
        }

        public string Summary()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _stats) sb.Append(kv.Key).Append('=').Append(kv.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            foreach (var kv in _spell) sb.Append(kv.Key.Item1).Append(':').Append(kv.Key.Item2).Append('=').Append(kv.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            return sb.ToString();
        }
    }
}
