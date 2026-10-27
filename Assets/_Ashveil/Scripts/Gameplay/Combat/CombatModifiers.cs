namespace Ashveil.Gameplay.Combat
{
    /// <summary>Werte des Angreifers, die ein Treffer beruecksichtigt (Ausruestung, spaeter Talente/Buffs).</summary>
    public interface ICombatModifiers
    {
        /// <summary>Multiplikator auf ausgeteilten Schaden (1 = unveraendert).</summary>
        float OutgoingDamageMultiplier(bool melee);
        /// <summary>Zusaetzliche kritische Trefferchance (0..1).</summary>
        float CritChanceBonus { get; }
        /// <summary>Leben, das jeder Treffer zurueckgibt.</summary>
        float LifeOnHit { get; }
        /// <summary>Stufe des Angreifers (Trefferchance gegen viel hoehere Gegner).</summary>
        int AttackerLevel { get; }
    }

    /// <summary>Verringert eingehenden Schaden (Ruestung).</summary>
    public interface IDamageMitigation
    {
        float Mitigate(float amount);
    }

    /// <summary>
    /// Passiver Schildblock (Max 28.09.): faengt nach der Ruestung einen festen Teil eines frontalen Treffers ab.
    /// Setzt <see cref="DamageInfo.Blocked"/> und gibt den verbleibenden Schaden zurueck (darf 0 sein).
    /// </summary>
    public interface IDamageBlocker
    {
        float Block(ref DamageInfo info, float amount);
    }

    /// <summary>Passives Ausweichen (WoW Classic): Chance aus Grundwert + Geschick, nur gegen Nahkampf von vorn.</summary>
    public interface IDodger
    {
        bool TryDodge(in DamageInfo info);
    }

    /// <summary>
    /// Zustaende auf einer Figur (P1 Faehigkeiten): weniger erlittener Schaden (Prozent) und Schilde, die Schaden
    /// auffangen. Reihenfolge in <see cref="Health.ApplyDamage"/>: Verringerung vor der Ruestung, Schild nach dem Block.
    /// </summary>
    public interface IDamageAbsorber
    {
        /// <summary>Faktor auf eingehenden Schaden (1 = unveraendert).</summary>
        float IncomingMultiplier { get; }
        /// <summary>Schild faengt auf; gibt den verbleibenden Schaden zurueck und setzt <see cref="DamageInfo.Absorbed"/>.</summary>
        float Absorb(ref DamageInfo info, float amount);
    }

    /// <summary>Bedrohungs-Faktor einer Figur (Tank-Klasse, Talente) fuer die Bedrohungstabelle der Gegner.</summary>
    public interface IThreatModifier
    {
        float ThreatMultiplier { get; }
    }
}
