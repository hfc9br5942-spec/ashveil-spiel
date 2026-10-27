using Ashveil.Data;
using Ashveil.Gameplay.Items;
using UnityEngine;

namespace Ashveil.Gameplay.Abilities
{
    /// <summary>
    /// Regeln der Klassen-Ressourcen und Zauber-Werte nach WoW Classic (P1, Max 30.09.). Server und Client rechnen damit
    /// gleich; entscheidend ist der Server.
    ///
    ///   Wut (Eidwaechter)  0–100, beginnt leer. Entsteht durch ausgeteilten Schaden (~4,5 Wut/s im Takt der Stufe) und
    ///                      erlittenen (ein Fuenftel des Lebens = 10 Wut). Ausserhalb des Kampfes verfliegt sie (1,5/s).
    ///   Energie (Aschenj.) 0–100, fuellt sich mit 10/s.
    ///   Mana (Aschenweber) Grundwert je Stufe + 15 je Intelligenz. Erholt sich nur, wenn 5 s kein Mana ausgegeben wurde
    ///                      (Fuenf-Sekunden-Regel), abhaengig von Willenskraft.
    /// </summary>
    public static class SpellRules
    {
        public const float Gcd = 1.5f;
        public const float EnergyGcd = 1.0f;
        public const float RageMax = 100f;
        public const float EnergyMax = 100f;
        public const float EnergyPerSecond = 10f;
        public const float RageDecayPerSecond = 1.5f;
        public const float FiveSecondRule = 5f;
        public const float MeleeRange = 5f;
        /// <summary>
        /// P2 Balance: Anteil des Manas je Sekunde ausser Kampf (nach der Fuenf-Sekunden-Regel). Leben erholt sich ausser
        /// Kampf mit 5 %/s – ohne diesen Wert stuende der Aschenweber nach jedem Kampf etwa doppelt so lange herum.
        /// </summary>
        public const float OutOfCombatManaPerSecond = 0.04f;

        public static float GcdFor(ResourceType t) => t == ResourceType.Energy ? EnergyGcd : Gcd;

        /// <summary>
        /// Erwarteter Schaden je Sekunde eines Nahkaempfers dieser Stufe (Richtwert fuer die Wut). Unser Kampf ist schneller
        /// als Classic (Schlagfolgen statt 2,6-s-Takt) – deshalb nicht Classics c(Stufe), sondern: wer im Takt seiner Stufe
        /// schlaegt, bekommt etwa 4,5 Wut je Sekunde; wer ein Fuenftel seines Lebens verliert, 10 Wut.
        /// </summary>
        public static float ExpectedDps(int level) => 20f + 4f * (Mathf.Max(1, level) - 1);

        public static float RageFromDealt(float damage, int level) => 4.5f * damage / ExpectedDps(level);
        public static float RageFromTaken(float damage, float maxHealth) => 50f * damage / Mathf.Max(50f, maxHealth);

        public static float MaxMana(int level, EquipmentStats st, TalentBonuses t) =>
            Mathf.Round((110f + 20f * (Mathf.Max(1, level) - 1) + st.Intellect * 15f) * (1f + (t != null ? t.Get(TalentStat.ManaPercent) : 0f) / 100f));

        public static float MaxResource(ResourceType type, int level, EquipmentStats st, TalentBonuses t)
        {
            switch (type)
            {
                case ResourceType.Mana: return MaxMana(level, st, t);
                case ResourceType.Rage: return RageMax;
                case ResourceType.Energy: return EnergyMax;
                default: return 0f;
            }
        }

        /// <summary>Mana je Sekunde ausserhalb der Fuenf-Sekunden-Regel.</summary>
        public static float ManaRegen(int level, EquipmentStats st, TalentBonuses t) =>
            (3f + st.Willpower * 0.25f + Mathf.Max(1, level) * 0.2f) * (1f + (t != null ? t.Get(TalentStat.ResourceRegenPercent) : 0f) / 100f);

        public static float EnergyRegen(TalentBonuses t) =>
            EnergyPerSecond * (1f + (t != null ? t.Get(TalentStat.ResourceRegenPercent) : 0f) / 100f);

        // ---------------------------------------------------------------- Zauber-Werte

        /// <summary>Zauberkraft: 1 % je Intelligenz, dazu Talente fuer Schaden bzw. Heilung.</summary>
        public static float SpellPowerMultiplier(EquipmentStats st, TalentBonuses t, bool healing) =>
            (1f + st.Intellect * 0.01f + Progression.DamagePerLevel * (Mathf.Max(1, st.Level) - 1) * 0.5f) *
            (1f + (t != null ? t.Get(healing ? TalentStat.HealingPercent : TalentStat.SpellDamagePercent) : 0f) / 100f);

        public static float SpellCrit(EquipmentStats st) => 0.05f + st.Intellect * 0.0005f + st.CritPercent / 100f;

        /// <summary>Kosten nach Talenten (nie unter 0).</summary>
        public static int CostOf(SpellDefinition s, SpellRank r, TalentBonuses t) =>
            r == null ? 0 : Mathf.Max(0, r.cost - Mathf.RoundToInt(t != null ? t.ForSpell(TalentStat.SpellCostReduction, s.Id) : 0f));

        public static float CastTimeOf(SpellDefinition s, TalentBonuses t) =>
            Mathf.Max(0f, s.castTime - (t != null ? t.ForSpell(TalentStat.CastTimeReduction, s.Id) : 0f));

        public static float CooldownOf(SpellDefinition s, TalentBonuses t) =>
            Mathf.Max(0f, s.cooldown - (t != null ? t.ForSpell(TalentStat.CooldownReduction, s.Id) : 0f));

        public static float PowerOf(SpellDefinition s, TalentBonuses t) =>
            1f + (t != null ? t.ForSpell(TalentStat.SpellPowerPercent, s.Id) : 0f) / 100f;

        public static float EffectBonusSeconds(SpellDefinition s, TalentBonuses t) =>
            t != null ? t.ForSpell(TalentStat.EffectDurationBonus, s.Id) : 0f;

        /// <summary>Text fuer Tooltips: Rang-Werte in die Beschreibung setzen ({min} {max} {extra} {dur} {cost}).</summary>
        public static string Describe(SpellDefinition s, int rank, TalentBonuses t = null)
        {
            SpellRank r = s != null ? s.Rank(Mathf.Max(1, rank)) : null;
            if (s == null) return "";
            string d = s.Description ?? "";
            if (r == null) return d;
            float p = PowerOf(s, t);
            var ci = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            return d.Replace("{min}", (r.min * p).ToString("0", ci)).Replace("{max}", (r.max * p).ToString("0", ci))
                    .Replace("{extra}", (r.extra * p).ToString("0", ci)).Replace("{dur}", s.EffectDuration(r, s.dotSeconds > 0 ? s.dotSeconds : s.slowSeconds).ToString("0.#", ci))
                    .Replace("{cost}", CostOf(s, r, t).ToString());
        }
    }
}
