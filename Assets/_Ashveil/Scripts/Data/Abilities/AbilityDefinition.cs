using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Was eine Faehigkeit technisch ausloest (heute: die fuenf Waffen-/Bewegungsaktionen).</summary>
    public enum AbilityKind : byte
    {
        AutoAttack,
        WeaponSpecial,
        Dodge,
        SwapWeapon,
        Reload
    }

    /// <summary>
    /// Nicht-Item-Aktion fuer die Actionbar und das Faehigkeitenbuch (K). Heute die fuenf Waffen-/Bewegungsaktionen;
    /// Zauber und Klassenfaehigkeiten kommen spaeter im selben Format dazu. Abklingzeiten und Wirkung kommen weiter
    /// aus Waffe (WeaponUser) bzw. Bewegungsmotor – hier stehen nur Anzeige und Regeln.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Faehigkeit", fileName = "Ability_")]
    public sealed class AbilityDefinition : GameDefinition
    {
        protected override string IdPrefix => "ability";

        public AbilityKind kind;
        [Tooltip("Symbol (bis es eigene Bilder gibt, wird es wie ein Gegenstand gezeichnet)")]
        public IconShape iconShape = IconShape.Sword;
        public Color iconColor = new Color(0.8f, 0.78f, 0.72f);
        public Sprite icon;
        [Tooltip("Braucht ein Ziel (rote Anzeige, wenn keines oder zu weit weg)")]
        public bool requiresTarget;
        [Tooltip("Im Faehigkeitenbuch (K) anzeigen")]
        public bool showInSpellbook = true;
        [Tooltip("Reihenfolge im Faehigkeitenbuch")]
        public int sortOrder;
    }
}
