using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Ein Gegenstand. Ein neuer Gegenstand = ein neues Asset (Create &gt; Ashveil &gt; Item), kein Code.
    /// Netz und Speicherstand kennen nur die ID (z. B. "item.ash_wolf_pelt").
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Item", fileName = "Item_New")]
    public sealed class ItemDefinition : GameDefinition
    {
        [Header("Einordnung")]
        public ItemCategory category = ItemCategory.Junk;
        public ItemQuality quality = ItemQuality.Common;
        [Min(1)] public int itemLevel = 1;
        [Tooltip("Mindeststufe zum Anlegen/Benutzen")]
        [Min(1)] public int requiredLevel = 1;
        [Tooltip("1 = nicht stapelbar")]
        [Min(1)] public int maxStack = 1;
        [Tooltip("Verkaufswert beim Haendler in Kupfer")]
        [Min(0)] public int sellPrice = 1;
        public BindType bind = BindType.None;
        [Tooltip("Nur einmal im Besitz (Questgegenstaende, Einzelstuecke)")]
        public bool unique;

        [Header("Symbol")]
        [Tooltip("Optional – ohne Sprite wird ein Symbol aus Form und Farbe erzeugt")]
        public Sprite icon;
        public IconShape iconShape = IconShape.Bone;
        public Color iconColor = new Color(0.75f, 0.7f, 0.62f);

        [Header("Aussehen")]
        [Tooltip("Sichtbares Modell am Charakter (getrennt von den Spielwerten)")]
        public AppearanceDefinition appearance;

        [Header("Anlegen")]
        public EquipType equipType = EquipType.None;
        public ArmorType armorType = ArmorType.None;
        [Min(0)] public int armor;
        [Tooltip("Nur Schilde: Blockwert (abgefangener Schaden je Block). 0 = aus der Ruestung abgeleitet (15 %)")]
        [Min(0)] public int blockValue;
        [Tooltip("Nur Waffen: Kampfwerte, Angriffe, Animationen")]
        public WeaponDefinition weapon;
        public WeaponHand hand = WeaponHand.Auto;
        [Tooltip("Leer = jede Klasse, die Waffen-/Ruestungsart tragen darf")]
        public List<CharacterClassDefinition> allowedClasses = new List<CharacterClassDefinition>();

        [Header("Tasche (Kategorie Container)")]
        [Tooltip("Anzahl Plaetze der Tasche")]
        [Range(0, 20)] public int containerSlots;
        public ContainerKind containerKind = ContainerKind.General;
        [Tooltip("Nur Koecher/Munitionsbeutel: Fernkampf-Tempo +X %")]
        [Range(0f, 30f)] public float rangedHaste;

        [Header("Munition (Kategorie Ammo)")]
        public AmmoType ammoType = AmmoType.None;
        [Tooltip("Zusatzschaden pro Sekunde des Fernkampfs (wie WoW: Pfeilsorte hebt den DPS)")]
        [Min(0f)] public float ammoDps;

        [Header("Werte & Wirkungen")]
        public List<StatModifier> stats = new List<StatModifier>();
        public List<ItemEffect> effects = new List<ItemEffect>();

        [Header("Benutzen (Verbrauchsgegenstaende, Rezepte)")]
        [Min(0f)] public float useCooldown = 0f;
        [Tooltip("Rezeptrolle: lehrt dieses Rezept")]
        public RecipeDefinition teachesRecipe;

        [Header("Berufe")]
        [Tooltip("Handwerksmaterial: welcher Beruf es typischerweise sammelt (nur Anzeige – es faellt fuer alle)")]
        public ProfessionDefinition gatheredBy;

        [Header("Quest")]
        [Tooltip("ID der Quest, fuer die der Gegenstand gebraucht wird (M4)")]
        public string questId;

        protected override string IdPrefix => "item";

        public bool IsStackable => maxStack > 1;
        public bool IsContainer => category == ItemCategory.Container && containerSlots > 0;
        public bool IsAmmo => category == ItemCategory.Ammo && ammoType != AmmoType.None;
        public bool IsEquippable => equipType != EquipType.None;
        public bool IsWeapon => equipType == EquipType.Weapon && weapon != null;
        public bool IsUsable => category == ItemCategory.Consumable || teachesRecipe != null;

        /// <summary>Tatsaechliche Handhabung (Auto -&gt; aus der Waffenklasse).</summary>
        public WeaponHand Hand
        {
            get
            {
                if (hand != WeaponHand.Auto || weapon == null) return hand == WeaponHand.Auto ? WeaponHand.OneHand : hand;
                switch (weapon.weaponClass)
                {
                    case WeaponClass.TwoHandedSword:
                    case WeaponClass.TwoHandedAxe:
                    case WeaponClass.Spear:
                    case WeaponClass.Hammer:
                    case WeaponClass.Bow:
                    case WeaponClass.Crossbow:
                    case WeaponClass.Rifle:
                    case WeaponClass.Shotgun:
                    case WeaponClass.HeavyGun:
                    case WeaponClass.Staff:
                    case WeaponClass.AetherGun:
                        return WeaponHand.TwoHand;
                    case WeaponClass.Focus:
                    case WeaponClass.Artifact:
                        return WeaponHand.OffHandOnly;
                    default:
                        return WeaponHand.OneHand;
                }
            }
        }

        public bool IsTwoHanded => IsWeapon && Hand == WeaponHand.TwoHand;

        /// <summary>Schild: Nebenhand, keine Waffe, mit Ruestung oder Blockwert (Fokus/Artefakt haben beides nicht).</summary>
        public bool IsShield => equipType == EquipType.Weapon && weapon == null && Hand == WeaponHand.OffHandOnly && (armor > 0 || blockValue > 0);

        /// <summary>Blockwert des Schildes (Schildblock, Max 28.09.: passiv, nur mit Schild).</summary>
        public int BlockValue => !IsShield ? 0 : blockValue > 0 ? blockValue : Mathf.Max(1, Mathf.RoundToInt(armor * 0.15f));
        public bool IsRangedWeapon => IsWeapon && !weapon.IsMelee && !weapon.IsSpell;

        /// <summary>Passt der Gegenstand grundsaetzlich in diesen Platz (ohne Klasse/Stufe)?</summary>
        public bool FitsSlot(EquipmentSlot slot)
        {
            switch (equipType)
            {
                case EquipType.Head: return slot == EquipmentSlot.Head;
                case EquipType.Shoulders: return slot == EquipmentSlot.Shoulders;
                case EquipType.Chest: return slot == EquipmentSlot.Chest;
                case EquipType.Hands: return slot == EquipmentSlot.Hands;
                case EquipType.Legs: return slot == EquipmentSlot.Legs;
                case EquipType.Feet: return slot == EquipmentSlot.Feet;
                case EquipType.Belt: return slot == EquipmentSlot.Belt;
                case EquipType.Cloak: return slot == EquipmentSlot.Cloak;
                case EquipType.Neck: return slot == EquipmentSlot.Neck;
                case EquipType.Ring: return slot == EquipmentSlot.Ring1 || slot == EquipmentSlot.Ring2;
                case EquipType.Trinket: return slot == EquipmentSlot.Trinket1 || slot == EquipmentSlot.Trinket2;
                case EquipType.Weapon:
                    // Fernkampfwaffen (Bogen, Armbrust, Schusswaffen) haben wie in WoW ihren eigenen Platz
                    if (IsRangedWeapon) return slot == EquipmentSlot.Ranged;
                    switch (Hand)
                    {
                        case WeaponHand.OffHandOnly: return slot == EquipmentSlot.OffHand;
                        case WeaponHand.MainHandOnly:
                        case WeaponHand.TwoHand: return slot == EquipmentSlot.MainHand;
                        default: return slot == EquipmentSlot.MainHand || slot == EquipmentSlot.OffHand;
                    }
                default: return false;
            }
        }

        public int Stat(StatType type)
        {
            int sum = type == StatType.Armor ? armor : 0;
            foreach (StatModifier m in stats)
                if (m.stat == type) sum += m.value;
            return sum;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (IsEquippable) maxStack = 1;
            if (category == ItemCategory.Container && equipType == EquipType.None) equipType = EquipType.Bag;
            if (category == ItemCategory.Quest && bind == BindType.None) bind = BindType.OnPickup;
            if (requiredLevel > itemLevel + 10) requiredLevel = itemLevel;
        }
#endif
    }
}
