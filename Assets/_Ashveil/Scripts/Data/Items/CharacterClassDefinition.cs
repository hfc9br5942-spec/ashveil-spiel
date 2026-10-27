using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Spielbare Klasse: welche Waffen und Ruestungen sie fuehren darf, welche Rolle sie spielt,
    /// womit sie beginnt. Neue Klasse = neues Asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Character Class", fileName = "Class_New")]
    public sealed class CharacterClassDefinition : GameDefinition
    {
        public ClassRole roles = ClassRole.Damage;
        public Color classColor = new Color(0.8f, 0.7f, 0.5f);
        [Tooltip("Schwerste tragbare Ruestung (leichtere geht immer)")]
        public ArmorType maxArmor = ArmorType.Leather;
        public List<WeaponClass> weapons = new List<WeaponClass>();
        [Tooltip("Zwei Einhand-Nahkampfwaffen gleichzeitig")]
        public bool dualWield;
        [Tooltip("Aktive Ausweichrolle als Klassenfaehigkeit (Kampfumbau: nur der Aschenjaeger)")]
        public bool canRoll;
        [Tooltip("Hauptattribut (bestimmt Beute-Vorlieben und Schadensbonus)")]
        public StatType primaryStat = StatType.Strength;
        [Tooltip("Klassen-Ressource (WoW Classic): Wut, Energie oder Mana")]
        public ResourceType resource = ResourceType.None;
        [Tooltip("Namen der drei Talentbaeume")]
        public string[] talentTrees = new string[3];

        [Header("Start")]
        public List<ItemDefinition> startEquipment = new List<ItemDefinition>();
        public List<ItemAmount> startBag = new List<ItemAmount>();

        protected override string IdPrefix => "class";

        /// <summary>
        /// Im Charakter-Editor waehlbare Klassen in Anzeige-Reihenfolge (Max 30.09.: drei Klassen, der Magier folgt nach
        /// der Lore-Arbeit und steht bis dahin nur ausgegraut im Editor). Der Server nimmt nur diese Klassen an.
        /// </summary>
        public static readonly string[] SelectableIds = { "class.warden", "class.ashstalker", "class.emberweaver" };

        /// <summary>Ausgegrauter Platz im Editor – noch nicht spielbar.</summary>
        public const string ComingSoonName = "Magier – kommt später";

        public static bool IsSelectable(string id) => System.Array.IndexOf(SelectableIds, id) >= 0;

        public bool CanUseWeapon(WeaponClass wc) => weapons.Contains(wc);
        public bool CanWear(ArmorType a) => a <= maxArmor;
    }
}
