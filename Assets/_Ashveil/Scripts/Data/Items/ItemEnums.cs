namespace Ashveil.Data
{
    /// <summary>Grundart eines Gegenstands.</summary>
    public enum ItemCategory
    {
        Weapon,
        Armor,
        Consumable,
        /// <summary>Handwerksmaterial (Berufe).</summary>
        Material,
        Recipe,
        Quest,
        /// <summary>Verkaeufliche Beute ohne weiteren Nutzen.</summary>
        Junk,
        /// <summary>Tasche, Koecher, Munitionsbeutel (in einen Taschenplatz).</summary>
        Container,
        /// <summary>Munition (Pfeile, Bolzen) – wird beim Schuss verbraucht.</summary>
        Ammo
    }

    /// <summary>Art einer Tasche (wie WoW): Koecher nehmen nur Pfeile auf und machen den Fernkampf schneller.</summary>
    public enum ContainerKind
    {
        General,
        Quiver,
        AmmoPouch
    }

    /// <summary>Munitionsart: Bogen braucht Pfeile, Armbrust Bolzen.</summary>
    public enum AmmoType
    {
        None,
        Arrow,
        Bolt
    }

    /// <summary>Wohin ein Gegenstand angelegt wird (Waffen: siehe <see cref="WeaponHand"/>).</summary>
    public enum EquipType
    {
        None,
        Head,
        Shoulders,
        Chest,
        Hands,
        Legs,
        Feet,
        Belt,
        Cloak,
        Neck,
        Ring,
        Trinket,
        Weapon,
        /// <summary>Tasche/Koecher – in einen der 4 Taschenplaetze.</summary>
        Bag
    }

    /// <summary>Die 16 Ausruestungsplaetze eines Charakters. Reihenfolge = Speicherformat, nur hinten ergaenzen.</summary>
    public enum EquipmentSlot
    {
        Head = 0,
        Shoulders = 1,
        Chest = 2,
        Hands = 3,
        Legs = 4,
        Feet = 5,
        Belt = 6,
        Cloak = 7,
        Neck = 8,
        Ring1 = 9,
        Ring2 = 10,
        Trinket1 = 11,
        Trinket2 = 12,
        MainHand = 13,
        OffHand = 14,
        /// <summary>Fernkampf (Bogen, Armbrust …) – wie WoW eigener Platz neben den Handwaffen.</summary>
        Ranged = 15
    }

    /// <summary>Wie eine Waffe gefuehrt wird.</summary>
    public enum WeaponHand
    {
        /// <summary>Aus der Waffenklasse ableiten.</summary>
        Auto,
        /// <summary>Haupt- oder Nebenhand.</summary>
        OneHand,
        MainHandOnly,
        OffHandOnly,
        /// <summary>Beide Haende – blockiert die Nebenhand.</summary>
        TwoHand
    }

    /// <summary>Ruestungsart. Klassen tragen ihre Art und alles Leichtere.</summary>
    public enum ArmorType
    {
        None = 0,
        Cloth = 1,
        Leather = 2,
        Mail = 3,
        Plate = 4
    }

    public enum BindType
    {
        /// <summary>Frei handelbar.</summary>
        None,
        /// <summary>Wird beim Anlegen gebunden.</summary>
        OnEquip,
        /// <summary>Wird beim Aufheben gebunden.</summary>
        OnPickup
    }

    public enum StatType
    {
        Strength,
        Agility,
        Intellect,
        Stamina,
        Willpower,
        Armor
    }

    /// <summary>Besondere Wirkungen. Neue Arten nur hinten anfuegen (Speicher-/Datenformat).</summary>
    public enum ItemEffectType
    {
        None,
        /// <summary>+X % Schaden.</summary>
        DamagePercent,
        /// <summary>+X % kritische Trefferchance.</summary>
        CritChance,
        /// <summary>Heilt X Leben bei jedem Treffer.</summary>
        LifeOnHit,
        /// <summary>+X % Ruestung.</summary>
        ArmorPercent,
        /// <summary>+X % maximales Leben.</summary>
        HealthPercent,
        /// <summary>Benutzen: heilt sofort X Leben.</summary>
        Heal,
        /// <summary>Benutzen: heilt X Leben ueber 10 s.</summary>
        HealOverTime,
        /// <summary>Nur Beschreibung (Wirkung folgt spaeter), Text aus dem Effekt.</summary>
        Custom
    }

    /// <summary>Rolle einer Klasse (fuer Gruppensuche, Loot-Beteiligung, Anzeige).</summary>
    [System.Flags]
    public enum ClassRole
    {
        None = 0,
        Tank = 1,
        Healer = 2,
        Damage = 4
    }

    public enum ProfessionKind
    {
        Gathering,
        Crafting
    }

    /// <summary>Prozedurales Symbol (bis echte Icons gezeichnet sind).</summary>
    public enum IconShape
    {
        Sword, Axe, Dagger, Mace, Staff, Pistol, Rifle, Focus,
        Helmet, Shoulders, Chest, Gloves, Legs, Boots, Belt, Cloak, Amulet, Ring, Trinket,
        Potion, Food, Ore, Hide, Herb, Cloth, Bone, Scroll, Letter, Coin, Gem, Bullet, Shield,
        Bow, Arrow, Quiver, Bag
    }
}
