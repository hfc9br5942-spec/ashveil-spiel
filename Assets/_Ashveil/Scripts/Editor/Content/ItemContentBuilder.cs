using System;
using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Milestone 3.4 – Inhalte des Itemsystems (alles als normale Assets, spaeter frei im Inspector aenderbar):
    ///   · 3 Klassen (Eidwaechter, Aschenjaeger, Aschenweber) – vorlaeufig, bis der Klassenentwurf steht
    ///   · 6 Berufe, 3 Rezepte
    ///   · ~55 Gegenstaende aller Arten und Qualitaeten, 10 Waffen + Faeuste
    ///   · Beutetabellen fuer Aschwolf, Verblichene, Schuetzin und den Selbsttest
    /// Standardwerte werden nur beim ersten Anlegen oder bei neuer <see cref="DefaultsVersion"/> geschrieben.
    /// </summary>
    public static partial class ItemContentBuilder
    {
        private const string Root = ProjectSetup.Root + "/Data";
        private const string ItemFolder = Root + "/Items";
        private const string ClassFolder = Root + "/Classes";
        private const string ProfessionFolder = Root + "/Professions";
        private const string RecipeFolder = Root + "/Recipes";
        private const string LootFolder = Root + "/Loot";
        private const int DefaultsVersion = 9; // 6–9: P2 Balance (Boegen ×0,8, Gezielter Schuss ×1,6), 3: Milestone 4 (Schwarzpulver, Belohnungswaffen), 4: eigene Waffenwerte, 5: Kampfumbau (Bogen/Pfeile/Taschen wie WoW, Stab zaubert, Feuerwaffen ruhen)
        private const string VersionKey = "Ashveil.ItemDefaults.Version";

        private static bool _refresh;
        /// <summary>In diesem Durchlauf mit Standardwerten beschriebene Assets (nur dort Nachtraege anwenden).</summary>
        internal static readonly HashSet<GameDefinition> Written = new HashSet<GameDefinition>();
        private static readonly Dictionary<string, ItemDefinition> Items = new Dictionary<string, ItemDefinition>();

        public static CharacterClassDefinition DefaultClass { get; private set; }
        public static WeaponDefinition Fists { get; private set; }

        // Farben
        private static readonly Color Steel = new Color(0.72f, 0.74f, 0.78f);
        private static readonly Color Iron = new Color(0.5f, 0.5f, 0.52f);
        private static readonly Color Rust = new Color(0.58f, 0.36f, 0.22f);
        private static readonly Color Leather = new Color(0.52f, 0.36f, 0.22f);
        private static readonly Color DarkLeather = new Color(0.32f, 0.22f, 0.15f);
        private static readonly Color Linen = new Color(0.78f, 0.72f, 0.6f);
        private static readonly Color Ash = new Color(0.55f, 0.53f, 0.5f);
        private static readonly Color Ember = new Color(1f, 0.5f, 0.16f);
        private static readonly Color Bone = new Color(0.86f, 0.82f, 0.7f);
        private static readonly Color Blood = new Color(0.72f, 0.14f, 0.12f);
        private static readonly Color Violet = new Color(0.62f, 0.36f, 0.9f);
        private static readonly Color Moss = new Color(0.45f, 0.62f, 0.28f);
        private static readonly Color GoldC = new Color(0.9f, 0.72f, 0.3f);

        /// <summary>Legt alles an und gibt die Standardklasse zurueck.</summary>
        public static void Build()
        {
            foreach (string f in new[] { ItemFolder, ClassFolder, ProfessionFolder, RecipeFolder, LootFolder, PlayerRigBuilder.WeaponFolder })
                ProjectSetup.EnsureFolder(f);
            _refresh = EditorPrefs.GetInt(VersionKey, 0) < DefaultsVersion;
            Items.Clear();
            Written.Clear();

            // ---------------------------------------------------------- Berufe
            ProfessionDefinition skinning = Profession("Kuerschnerei", "profession.skinning", "Kürschnerei", ProfessionKind.Gathering);
            ProfessionDefinition mining = Profession("Bergbau", "profession.mining", "Bergbau", ProfessionKind.Gathering);
            ProfessionDefinition herbalism = Profession("Kraeuterkunde", "profession.herbalism", "Kräuterkunde", ProfessionKind.Gathering);
            ProfessionDefinition leatherworking = Profession("Lederverarbeitung", "profession.leatherworking", "Lederverarbeitung", ProfessionKind.Crafting);
            ProfessionDefinition smithing = Profession("Schmiedekunst", "profession.smithing", "Schmiedekunst", ProfessionKind.Crafting);
            ProfessionDefinition alchemy = Profession("Alchemie", "profession.alchemy", "Alchemie", ProfessionKind.Crafting);

            // ---------------------------------------------------------- Waffen (Kampfwerte)
            (WeaponDefinition sword, WeaponDefinition pistol) = PlayerRigBuilder.EnsureWeapons();
            // Max 30.09.: das Startschwert heisst Breitschwert (Langschwerter werden zweihaendig gefuehrt)
            if (sword != null && sword.DisplayName != "Rostiges Breitschwert") { K.SetString(sword, "displayName", "Rostiges Breitschwert"); EditorUtility.SetDirty(sword); }
            Fists = Weapon("Weapon_Fists", "weapon.fists", "Fäuste", w => Melee(w, WeaponClass.Mace, 2f, 4f, 1.9f, 0.9f, "Faustschlag"));
            WeaponDefinition greataxe = Weapon("Weapon_NotchedGreataxe", "weapon.notched_greataxe", "Schartige Zweihandaxt",
                w => { Melee(w, WeaponClass.TwoHandedAxe, 17f, 25f, 2.8f, 1.35f, "Spalthieb"); w.quality = ItemQuality.Uncommon; });
            WeaponDefinition wardenBlade = Weapon("Weapon_WardenBlade", "weapon.warden_blade", "Klinge des Eidwächters",
                w => { Melee(w, WeaponClass.Sword, 12f, 17f, 2.4f, 1f, "Eidschlag"); w.quality = ItemQuality.Uncommon; w.hasAirAttacks = true; });
            WeaponDefinition harrowBlade = Weapon("Weapon_HarrowBlade", "weapon.harrow_blade", "Harrows Übungsklinge",
                w => { Melee(w, WeaponClass.Sword, 10f, 14f, 2.4f, 1f, "Lehrstunde"); w.quality = ItemQuality.Uncommon; });
            WeaponDefinition harrowKnife = Weapon("Weapon_HarrowKnife", "weapon.harrow_knife", "Harrows Jagdmesser",
                w => { Melee(w, WeaponClass.Dagger, 7f, 10f, 2f, 0.7f, "Abhäuten"); w.quality = ItemQuality.Uncommon; });
            WeaponDefinition dagger = Weapon("Weapon_CinderDagger", "weapon.cinder_dagger", "Glutdolch",
                w => Melee(w, WeaponClass.Dagger, 6f, 9f, 2f, 0.7f, "Nierenstich"));
            WeaponDefinition staff = Weapon("Weapon_EmberStaff", "weapon.ember_staff", "Glutstab",
                w => { Spell(w, 20f, 27f, 30f, "Glutsturm", Ember); w.quality = ItemQuality.Uncommon; }); // P2: 22–30 → 20–27 (Startstab war auf Stufe 1 zu stark)
            WeaponDefinition mace = Weapon("Weapon_GravewardenMace", "weapon.gravewarden_mace", "Grabwächterkeule",
                w => { Melee(w, WeaponClass.Mace, 15f, 22f, 2.3f, 1.05f, "Schädelbrecher"); w.quality = ItemQuality.Epic; });
            WeaponDefinition bellringer = Weapon("Weapon_Bellringer", "weapon.bellringer", "Glockenläuterin",
                w => { Melee(w, WeaponClass.TwoHandedSword, 26f, 36f, 3f, 1.3f, "Totengeläut"); w.quality = ItemQuality.Legendary; w.critChance = 0.12f; w.hasAirAttacks = true; });
            WeaponDefinition blunderbuss = Weapon("Weapon_ThunderBlunderbuss", "weapon.thunder_blunderbuss", "Donnerbüchse",
                w => { Gun(w, WeaponClass.Shotgun, WeaponArchetype.Spread, 7f, 10f, 14f, 1.4f, 2, 3f, "Doppelladung"); w.pellets = 6; w.spreadAngle = 9f; w.quality = ItemQuality.Rare; });
            WeaponDefinition rifle = Weapon("Weapon_AshHunterRifle", "weapon.ash_hunter_rifle", "Aschjägerbüchse",
                w => { Gun(w, WeaponClass.Rifle, WeaponArchetype.Hitscan, 26f, 34f, 40f, 1.6f, 4, 2.6f, "Blattschuss"); w.quality = ItemQuality.Rare; });
            // Kampfumbau: Boegen wie in WoW (automatischer Schuss im Waffentempo, Pfeile als Munition, toter Winkel)
            WeaponDefinition shortbow = Weapon("Weapon_HuntersShortbow", "weapon.hunters_shortbow", "Jägerkurzbogen",
                w => Bow(w, 36f, 50f, 30f, 2.6f));
            WeaponDefinition longbow = Weapon("Weapon_AshwoodLongbow", "weapon.ashwood_longbow", "Aschweiden-Langbogen",
                w => { Bow(w, 50f, 68f, 35f, 2.9f); w.quality = ItemQuality.Uncommon; });
            WeaponDefinition recurve = Weapon("Weapon_BellbronzeRecurve", "weapon.bellbronze_recurve", "Glockenbronze-Reflexbogen",
                w => { Bow(w, 62f, 84f, 35f, 2.8f); w.quality = ItemQuality.Rare; w.critChance = 0.09f; });

            // ---------------------------------------------------------- Klassen (vorlaeufig)
            CharacterClassDefinition warden = Class("Class_Eidwaechter", "class.warden", "Eidwächter",
                "Schwer gerüstete Schildmauer mit Klinge und Schild. Hält Gegner bei sich, damit die Gruppe lebt.",
                ClassRole.Tank | ClassRole.Damage, new Color(0.55f, 0.72f, 0.9f), ArmorType.Plate, StatType.Strength, false,
                WeaponClass.Sword, WeaponClass.TwoHandedSword, WeaponClass.Axe, WeaponClass.TwoHandedAxe, WeaponClass.Mace,
                WeaponClass.Hammer, WeaponClass.Spear, WeaponClass.Bow, WeaponClass.Crossbow);
            CharacterClassDefinition stalker = Class("Class_Aschenjaeger", "class.ashstalker", "Aschenjäger",
                "Flinker Jäger in Leder: zwei Dolche oder zwei Schwerter – und der Bogen. Viel Schaden, wenig Rüstung.",
                ClassRole.Damage, new Color(0.9f, 0.6f, 0.32f), ArmorType.Leather, StatType.Agility, true,
                WeaponClass.Sword, WeaponClass.Dagger, WeaponClass.Axe, WeaponClass.Bow, WeaponClass.Crossbow);
            CharacterClassDefinition weaver = Class("Class_Aschenweber", "class.emberweaver", "Aschenweber",
                "Webt Glut und Asche zu Heilung und Feuer. Leichte Stoffe, Stab und Fokus.",
                ClassRole.Healer | ClassRole.Damage, new Color(0.78f, 0.5f, 0.98f), ArmorType.Cloth, StatType.Intellect, false,
                WeaponClass.Staff, WeaponClass.Focus, WeaponClass.Dagger, WeaponClass.Mace);
            DefaultClass = warden;

            // ---------------------------------------------------------- Gegenstaende
            // Waffen
            ItemDefinition iSword = WeaponItem("rusty_longsword", "Rostiges Breitschwert", sword, ItemQuality.Common, 2, 1, IconShape.Sword, Rust, 45,
                "Die Schneide hat mehr Kerben als Glanz – aber sie schneidet.");
            ItemDefinition iPistol = WeaponItem("wheellock_pistol", "Radschlosspistole", pistol, ItemQuality.Uncommon, 4, 1, IconShape.Pistol, Iron, 180,
                "Ein Rad, ein Funke, ein Knall.");
            WeaponItem("notched_greataxe", "Schartige Zweihandaxt", greataxe, ItemQuality.Uncommon, 5, 4, IconShape.Axe, Steel, 310,
                "Schwer genug, um Türen und Zweifel zu spalten.", BindType.OnEquip, (StatType.Strength, 4), (StatType.Stamina, 3));
            WeaponItem("warden_blade", "Klinge des Eidwächters", wardenBlade, ItemQuality.Uncommon, 6, 5, IconShape.Sword, Steel, 280,
                "Auf dem Knauf ist ein Eid eingraviert, den niemand mehr spricht.", BindType.OnEquip, (StatType.Strength, 3), (StatType.Stamina, 2));
            ItemDefinition iDagger = WeaponItem("cinder_dagger", "Glutdolch", dagger, ItemQuality.Common, 2, 1, IconShape.Dagger, Ember, 40,
                "Die Klinge ist noch warm.");
            ItemDefinition iStaff = WeaponItem("ember_staff", "Glutstab", staff, ItemQuality.Uncommon, 4, 1, IconShape.Staff, Ember, 150,
                "Im Holz glimmt eine Glut, die nie erlischt.", BindType.None, (StatType.Intellect, 4), (StatType.Willpower, 2));
            WeaponItem("gravewarden_mace", "Grabwächterkeule", mace, ItemQuality.Epic, 8, 6, IconShape.Mace, new Color(0.45f, 0.42f, 0.5f), 820,
                "Sie hat viele Gräber bewacht. Die meisten sind leer.", BindType.OnPickup, (StatType.Strength, 7), (StatType.Stamina, 6))
                .Post(d => d.effects.Add(new ItemEffect { type = ItemEffectType.LifeOnHit, value = 3f }));
            ItemDefinition iBell = WeaponItem("bellringer", "Glockenläuterin", bellringer, ItemQuality.Legendary, 10, 8, IconShape.Sword, GoldC, 2400,
                "Jeder Hieb ein Glockenschlag für die, die nicht zurückkehren.", BindType.OnPickup, (StatType.Strength, 12), (StatType.Stamina, 9));
            iBell.Post(d =>
            {
                d.effects.Add(new ItemEffect { type = ItemEffectType.DamagePercent, value = 6f });
                d.effects.Add(new ItemEffect { type = ItemEffectType.Custom, text = "Getroffene Verblichene zögern kurz (folgt)." });
                d.unique = true;
            });
            WeaponItem("thunder_blunderbuss", "Donnerbüchse", blunderbuss, ItemQuality.Rare, 7, 5, IconShape.Rifle, Rust, 640,
                "Zielen ist optional.", BindType.OnEquip, (StatType.Agility, 5), (StatType.Stamina, 3));
            WeaponItem("ash_hunter_rifle", "Aschjägerbüchse", rifle, ItemQuality.Rare, 7, 5, IconShape.Rifle, DarkLeather, 690,
                "Lauf aus Glockenbronze, Kolben aus Aschweide.", BindType.OnEquip, (StatType.Agility, 6), (StatType.Stamina, 2));
            // Milestone 4: Belohnungen der Waffenmeisterin (Tutorial-Auftrag 1)
            WeaponItem("harrow_blade", "Harrows Übungsklinge", harrowBlade, ItemQuality.Uncommon, 3, 1, IconShape.Sword, Iron, 60,
                "Stumpf genug zum Üben, scharf genug für Wölfe.", BindType.OnPickup, (StatType.Strength, 2), (StatType.Stamina, 1))
                .Post(d => d.allowedClasses = new List<CharacterClassDefinition> { warden });
            WeaponItem("harrow_knife", "Harrows Jagdmesser", harrowKnife, ItemQuality.Uncommon, 3, 1, IconShape.Dagger, DarkLeather, 55,
                "Mit dieser Klinge hat Ilse ihren ersten Wolf abgezogen.", BindType.OnPickup, (StatType.Agility, 2), (StatType.Stamina, 1))
                .Post(d => d.allowedClasses = new List<CharacterClassDefinition> { stalker });
            ItemDefinition iFocus = Item("cracked_focus", "Gesprungener Fokus", ItemCategory.Weapon, ItemQuality.Uncommon, 4, 1, IconShape.Focus, Violet, 120,
                "Durch den Sprung dringt ein leises Summen.", d =>
                {
                    d.equipType = EquipType.Weapon;
                    d.hand = WeaponHand.OffHandOnly;
                    d.allowedClasses = new List<CharacterClassDefinition> { weaver };
                    d.stats = Stats((StatType.Intellect, 3), (StatType.Willpower, 3));
                });

            // Boegen, Pfeile, Koecher, Taschen (wie WoW)
            ItemDefinition iShortbow = WeaponItem("hunters_shortbow", "Jägerkurzbogen", shortbow, ItemQuality.Common, 2, 1, IconShape.Bow, Leather, 40,
                "Eibe, Sehne aus Darm, und genug Zug für einen Wolf.");
            WeaponItem("ashwood_longbow", "Aschweiden-Langbogen", longbow, ItemQuality.Uncommon, 5, 4, IconShape.Bow, Ash, 290,
                "Aschweide biegt sich, bricht aber nie.", BindType.OnEquip, (StatType.Agility, 4), (StatType.Stamina, 2));
            WeaponItem("bellbronze_recurve", "Glockenbronze-Reflexbogen", recurve, ItemQuality.Rare, 8, 6, IconShape.Bow, GoldC, 660,
                "Die Wurfarme sind mit Glockenbronze beschlagen. Jeder Schuss klingt nach.", BindType.OnEquip, (StatType.Agility, 6), (StatType.Stamina, 3));
            ItemDefinition roughArrow = Ammo("rough_arrow", "Grobe Pfeile", ItemQuality.Common, 1, 2f, Rust, "Gerade genug.");
            Ammo("sharp_arrow", "Scharfe Pfeile", ItemQuality.Common, 8, 4.5f, Steel, "Geschliffene Eisenspitzen.");
            Ammo("jagged_arrow", "Gezackte Pfeile", ItemQuality.Uncommon, 14, 7.5f, Blood, "Was sie trifft, blutet weiter.");
            ItemDefinition lightQuiver = Container("light_quiver", "Leichter Köcher", ItemQuality.Common, 1, 6, ContainerKind.Quiver, 0.10f, IconShape.Quiver, Leather, 25,
                "Leder über einem Weidengeflecht.");
            Container("hunters_quiver", "Jägerköcher", ItemQuality.Common, 8, 10, ContainerKind.Quiver, 0.12f, IconShape.Quiver, DarkLeather, 120,
                "Mit Fell gefüttert, damit die Pfeile nicht klappern.");
            Container("heavy_quiver", "Schwerer Köcher", ItemQuality.Uncommon, 14, 14, ContainerKind.Quiver, 0.13f, IconShape.Quiver, Iron, 380,
                "Eisenbeschläge und Platz für einen langen Tag.");
            Container("linen_bag", "Leinensack", ItemQuality.Common, 1, 6, ContainerKind.General, 0f, IconShape.Bag, Linen, 20, "Grob, aber dicht.");
            Container("wool_bag", "Wollbeutel", ItemQuality.Common, 5, 8, ContainerKind.General, 0f, IconShape.Bag, Ash, 60, "Warm und geräumig.");
            Container("leather_bag", "Lederranzen", ItemQuality.Common, 10, 10, ContainerKind.General, 0f, IconShape.Bag, Leather, 150, "Doppelt genäht an allen Ecken.");

            // Ruestung – Platte/Kette
            Armor("dented_helm", "Verbeulter Helm", ItemQuality.Common, 2, 1, EquipType.Head, ArmorType.Mail, 14, IconShape.Helmet, Iron, 30, "", (StatType.Stamina, 1));
            Armor("warden_pauldrons", "Schulterstücke des Eidwächters", ItemQuality.Uncommon, 5, 4, EquipType.Shoulders, ArmorType.Plate, 30, IconShape.Shoulders, Steel, 160,
                "Mit Glockenbronze beschlagen.", (StatType.Strength, 3), (StatType.Stamina, 3));
            Armor("chainmail_shirt", "Kettenhemd", ItemQuality.Common, 3, 2, EquipType.Chest, ArmorType.Mail, 28, IconShape.Chest, Iron, 60, "", (StatType.Stamina, 2));
            ItemDefinition gauntlets = Armor("iron_gauntlets", "Eisenhandschuhe", ItemQuality.Uncommon, 5, 4, EquipType.Hands, ArmorType.Plate, 22, IconShape.Gloves, Steel, 120,
                "Grob geschmiedet, aber ehrlich.", (StatType.Strength, 2), (StatType.Stamina, 2));
            Armor("plated_greaves", "Beplattete Beinschienen", ItemQuality.Rare, 7, 6, EquipType.Legs, ArmorType.Plate, 44, IconShape.Legs, Steel, 420,
                "", (StatType.Strength, 4), (StatType.Stamina, 6)).Post(d => d.bind = BindType.OnEquip);
            // Leder
            Armor("stalker_hood", "Kapuze des Aschenjägers", ItemQuality.Uncommon, 5, 4, EquipType.Head, ArmorType.Leather, 12, IconShape.Helmet, DarkLeather, 130,
                "Riecht nach Rauch und Regen.", (StatType.Agility, 3), (StatType.Stamina, 2));
            Armor("ashen_leather_jerkin", "Aschlederwams", ItemQuality.Uncommon, 5, 4, EquipType.Chest, ArmorType.Leather, 20, IconShape.Chest, Leather, 150,
                "", (StatType.Agility, 4), (StatType.Stamina, 2));
            ItemDefinition stalkerGloves = Armor("stalker_gloves", "Jägerhandschuhe", ItemQuality.Common, 2, 1, EquipType.Hands, ArmorType.Leather, 6, IconShape.Gloves, Leather, 25, "", (StatType.Agility, 1));
            ItemDefinition stalkerBoots = Armor("stalker_boots", "Jägerstiefel", ItemQuality.Uncommon, 5, 4, EquipType.Feet, ArmorType.Leather, 10, IconShape.Boots, DarkLeather, 110,
                "Genäht aus Aschwolfleder. Leise auf jedem Boden.", (StatType.Agility, 3), (StatType.Stamina, 2));
            // Stoff
            ItemDefinition gambeson = Armor("frayed_gambeson", "Ausgefranster Gambeson", ItemQuality.Common, 1, 1, EquipType.Chest, ArmorType.Cloth, 8, IconShape.Chest, Linen, 12,
                "Gepolstert mit Stroh und Hoffnung.", (StatType.Stamina, 1));
            Armor("weaver_robe", "Robe des Aschenwebers", ItemQuality.Uncommon, 5, 4, EquipType.Chest, ArmorType.Cloth, 10, IconShape.Chest, Violet, 150,
                "Der Saum ist mit feiner Asche bestickt.", (StatType.Intellect, 4), (StatType.Willpower, 3));
            Armor("ember_sash", "Glutschärpe", ItemQuality.Uncommon, 4, 3, EquipType.Belt, ArmorType.Cloth, 4, IconShape.Belt, Ember, 90, "", (StatType.Intellect, 2), (StatType.Stamina, 2));
            ItemDefinition sandals = Armor("weaver_sandals", "Weberpantoffeln", ItemQuality.Common, 2, 1, EquipType.Feet, ArmorType.Cloth, 3, IconShape.Boots, Linen, 18, "", (StatType.Willpower, 1));
            ItemDefinition wraps = Armor("foot_wraps", "Fußlappen", ItemQuality.Common, 1, 1, EquipType.Feet, ArmorType.Cloth, 3, IconShape.Boots, Linen, 5,
                "Besser als nichts. Knapp.");
            Armor("rope_belt", "Strickgürtel", ItemQuality.Common, 1, 1, EquipType.Belt, ArmorType.Leather, 3, IconShape.Belt, Leather, 8, "");
            // Umhaenge
            Armor("grey_wool_cloak", "Grauer Wollumhang", ItemQuality.Common, 2, 1, EquipType.Cloak, ArmorType.None, 5, IconShape.Cloak, Ash, 20, "", (StatType.Stamina, 1));
            Armor("wolfhide_cloak", "Wolfsfellumhang", ItemQuality.Uncommon, 5, 4, EquipType.Cloak, ArmorType.None, 9, IconShape.Cloak, new Color(0.3f, 0.28f, 0.26f), 140,
                "Der Wolf hätte ihn lieber behalten.", (StatType.Stamina, 3), (StatType.Agility, 1));

            // Ausruestungsfamilien (sichtbar am Charakter): Reisekleidung, Jaegerleder, schwere Ruestung
            Armor("travel_hood", "Reisekapuze", ItemQuality.Common, 2, 1, EquipType.Head, ArmorType.Cloth, 4, IconShape.Helmet, new Color(0.49f, 0.43f, 0.36f), 10,
                "Hält Regen und neugierige Blicke ab.", (StatType.Stamina, 1));
            Armor("travel_mantle", "Wollüberwurf", ItemQuality.Common, 2, 1, EquipType.Shoulders, ArmorType.Cloth, 4, IconShape.Shoulders, new Color(0.42f, 0.4f, 0.36f), 12,
                "", (StatType.Stamina, 1), (StatType.Willpower, 1));
            Armor("travel_wraps", "Leinenwickel", ItemQuality.Common, 1, 1, EquipType.Hands, ArmorType.Cloth, 2, IconShape.Gloves, Linen, 4, "");
            ItemDefinition trousers = Armor("travel_trousers", "Reisehose", ItemQuality.Common, 2, 1, EquipType.Legs, ArmorType.Cloth, 6, IconShape.Legs, new Color(0.37f, 0.33f, 0.28f), 10,
                "", (StatType.Stamina, 1));
            Armor("hunter_shoulderguards", "Schulterschutz des Jägers", ItemQuality.Uncommon, 5, 4, EquipType.Shoulders, ArmorType.Leather, 10, IconShape.Shoulders, Leather, 120,
                "Links doppelt gelegt – für den Bogenarm.", (StatType.Agility, 2), (StatType.Stamina, 2));
            Armor("hunter_breeches", "Jägerhose", ItemQuality.Uncommon, 5, 4, EquipType.Legs, ArmorType.Leather, 14, IconShape.Legs, DarkLeather, 130,
                "", (StatType.Agility, 3), (StatType.Stamina, 2));
            Armor("warden_cuirass", "Harnisch des Eidwächters", ItemQuality.Uncommon, 6, 5, EquipType.Chest, ArmorType.Plate, 48, IconShape.Chest, Steel, 360,
                "Brustplatte über Kettengeflecht, an den Rändern mit Leder gefasst.", (StatType.Strength, 3), (StatType.Stamina, 4));
            Armor("iron_sabatons", "Eisenschuhe", ItemQuality.Common, 3, 2, EquipType.Feet, ArmorType.Plate, 16, IconShape.Boots, Steel, 55, "", (StatType.Stamina, 2));
            ItemDefinition iShield = Item("oak_shield", "Eichenschild", ItemCategory.Armor, ItemQuality.Uncommon, 4, 1, IconShape.Shield, new Color(0.45f, 0.31f, 0.2f), 140,
                "Eisenbeschlagen, mit einem roten Tuchstreifen.", d =>
                {
                    d.equipType = EquipType.Weapon;
                    d.hand = WeaponHand.OffHandOnly;
                    d.armor = 40;
                    d.blockValue = 6;
                    d.bind = BindType.OnEquip;
                    d.allowedClasses = new List<CharacterClassDefinition> { warden };
                    d.stats = Stats((StatType.Stamina, 3));
                });

            // Schmuck
            Armor("bone_amulet", "Knochenamulett", ItemQuality.Uncommon, 4, 3, EquipType.Neck, ArmorType.None, 0, IconShape.Amulet, Bone, 95,
                "Klappert leise, wenn Verblichene nah sind.", (StatType.Stamina, 2), (StatType.Willpower, 2));
            Armor("tarnished_ring", "Angelaufener Ring", ItemQuality.Common, 2, 1, EquipType.Ring, ArmorType.None, 0, IconShape.Ring, Ash, 20, "", (StatType.Stamina, 1));
            ItemDefinition signet = Armor("cinder_signet", "Siegel der Glut", ItemQuality.Rare, 6, 4, EquipType.Ring, ArmorType.None, 0, IconShape.Ring, Ember, 380,
                "Das Wappen ist weggebrannt.", (StatType.Stamina, 3), (StatType.Strength, 2), (StatType.Agility, 2));
            signet.Post(d =>
            {
                d.bind = BindType.OnPickup;
                d.effects = new List<ItemEffect> { new ItemEffect { type = ItemEffectType.CritChance, value = 2f } };
            });
            ItemDefinition bellCharm = Armor("shepherds_bell_charm", "Glöckchen des Hirten", ItemQuality.Rare, 6, 5, EquipType.Trinket, ArmorType.None, 0, IconShape.Trinket, GoldC, 450,
                "Es läutet, obwohl es keinen Klöppel hat.");
            bellCharm.Post(d => d.effects = new List<ItemEffect> { new ItemEffect { type = ItemEffectType.DamagePercent, value = 5f } });
            ItemDefinition emberHeart = Armor("ember_heart", "Glutherz", ItemQuality.Epic, 8, 6, EquipType.Trinket, ArmorType.None, 0, IconShape.Gem, Ember, 900,
                "Schlägt im Takt eines Herzens, das längst verbrannt ist.", (StatType.Stamina, 4));
            emberHeart.Post(d =>
            {
                d.bind = BindType.OnPickup;
                d.effects = new List<ItemEffect> { new ItemEffect { type = ItemEffectType.LifeOnHit, value = 4f }, new ItemEffect { type = ItemEffectType.HealthPercent, value = 5f } };
            });

            // Verbrauch
            ItemDefinition draught = Item("healing_draught", "Heiltrank", ItemCategory.Consumable, ItemQuality.Common, 1, 1, IconShape.Potion, Blood, 10,
                "Bitter wie Eisen.", d => { d.maxStack = 20; d.useCooldown = 10f; d.effects = new List<ItemEffect> { new ItemEffect { type = ItemEffectType.Heal, value = 60f } }; });
            Item("strong_healing_draught", "Starker Heiltrank", ItemCategory.Consumable, ItemQuality.Uncommon, 5, 4, IconShape.Potion, new Color(0.9f, 0.2f, 0.15f), 30,
                "", d => { d.maxStack = 20; d.useCooldown = 10f; d.effects = new List<ItemEffect> { new ItemEffect { type = ItemEffectType.Heal, value = 120f } }; });
            ItemDefinition bread = Item("dry_bread", "Trockenes Brot", ItemCategory.Consumable, ItemQuality.Common, 1, 1, IconShape.Food, new Color(0.78f, 0.6f, 0.36f), 3,
                "Hart genug, um es als Waffe zu führen.", d => { d.maxStack = 20; d.useCooldown = 12f; d.effects = new List<ItemEffect> { new ItemEffect { type = ItemEffectType.HealOverTime, value = 60f } }; });
            Item("ash_tea", "Aschentee", ItemCategory.Consumable, ItemQuality.Common, 3, 1, IconShape.Potion, new Color(0.5f, 0.45f, 0.38f), 8,
                "Schmeckt nach Feuerstelle.", d => { d.maxStack = 20; d.useCooldown = 12f; d.effects = new List<ItemEffect> { new ItemEffect { type = ItemEffectType.HealOverTime, value = 90f } }; });

            // Handwerksmaterial (faellt fuer alle – Handel bleibt moeglich)
            ItemDefinition pelt = Material("ash_wolf_pelt", "Aschwolfpelz", ItemQuality.Common, 2, IconShape.Hide, new Color(0.34f, 0.31f, 0.28f), 6, skinning, "Grau und steif vor Asche.");
            ItemDefinition torn = Material("torn_leather", "Zerrissenes Leder", ItemQuality.Common, 1, IconShape.Hide, Leather, 3, skinning, "");
            ItemDefinition ore = Material("coarse_iron_ore", "Grobes Eisenerz", ItemQuality.Common, 2, IconShape.Ore, Rust, 5, mining, "");
            ItemDefinition scrap = Material("rusted_iron_scrap", "Rostiger Eisenschrott", ItemQuality.Common, 1, IconShape.Ore, Rust, 2, mining, "Kann eingeschmolzen werden.");
            ItemDefinition emberMoss = Material("ember_moss", "Glutmoos", ItemQuality.Common, 2, IconShape.Herb, Ember, 4, herbalism, "Wächst nur, wo es einmal gebrannt hat.");
            const string saltLore = "Salz aus dem Grab eines Riesen – so erzählt man sich in Kareth. Bindet, was nicht ruhen will.";
            ItemDefinition salt = Material("grave_salt", "Grabsalz", ItemQuality.Uncommon, 4, IconShape.Gem, new Color(0.88f, 0.88f, 0.92f), 14, null, saltLore);
            // M6 Mythologie (Max, 26.09.): das Salz der Gruben gilt als versteinertes Fleisch der Giganten der Vorzeit
            if (salt != null && salt.Description == "Bindet, was nicht ruhen will.")
            {
                K.SetString(salt, "description", saltLore);
                EditorUtility.SetDirty(salt);
            }
            ItemDefinition linen = Material("linen_scraps", "Leinenfetzen", ItemQuality.Common, 1, IconShape.Cloth, Linen, 2, null, "");
            Material("cinder_dust", "Glutstaub", ItemQuality.Common, 1, IconShape.Ore, new Color(0.4f, 0.36f, 0.32f), 1, null, "Überall. Wirklich überall.");
            Material("ash_crow_feather", "Aschkrähenfeder", ItemQuality.Common, 2, IconShape.Arrow, new Color(0.22f, 0.21f, 0.24f), 2, null, "Grau bis in den Kiel. Fliegt trotzdem gerade.");
            Material("black_powder", "Schwarzpulver", ItemQuality.Common, 3, IconShape.Ore, new Color(0.16f, 0.15f, 0.15f), 3, null, "Klamm vom Moor, aber es brennt noch.");
            Material("wolf_fang", "Wolfsfang", ItemQuality.Common, 1, IconShape.Bone, Bone, 4, null, "Alchemisten zermahlen ihn.");
            // Milestone 5.4: Kareth – Torfschrat, Nadelspeier, Russluchs
            Material("peat_clump", "Torfklumpen", ItemQuality.Common, 6, IconShape.Ore, new Color(0.24f, 0.19f, 0.14f), 5, null, "Brennt langsam und stinkt nach Moor.");
            Material("bog_root", "Moorwurzel", ItemQuality.Common, 6, IconShape.Herb, new Color(0.4f, 0.3f, 0.2f), 7, herbalism, "Zäh wie Leder, bitter wie Galle.");
            Material("swamp_herb", "Sumpfkraut", ItemQuality.Common, 5, IconShape.Herb, Moss, 6, herbalism, "");
            Material("chitin_plate", "Chitinplatte", ItemQuality.Common, 6, IconShape.Hide, new Color(0.3f, 0.34f, 0.28f), 8, skinning, "Leicht und hart – gut für Schulterstücke.");
            Material("venom_gland", "Giftdrüse", ItemQuality.Uncommon, 7, IconShape.Herb, new Color(0.5f, 0.75f, 0.3f), 16, null, "Vorsichtig anfassen.");
            Material("spine_needle", "Stachelnadel", ItemQuality.Common, 6, IconShape.Bullet, Bone, 2, null, "Taugt als Pfeil- oder Bolzenspitze.");
            ItemDefinition lynxPelt = Material("soot_lynx_pelt", "Rußluchsfell", ItemQuality.Rare, 11, IconShape.Hide, new Color(0.18f, 0.17f, 0.17f), 90, skinning, "Grau wie das Aschenfeld – man sieht es kaum, bis es sich bewegt.");
            Material("glowing_claw", "Glühende Klaue", ItemQuality.Uncommon, 11, IconShape.Bone, Ember, 45, null, "Noch warm. Schmiede reißen sich darum.");
            RecipeDefinition rSootCloak = Recipe("Recipe_SootCloak", "recipe.soot_cloak", "Wolfsfellumhang (Rußfutter)", leatherworking, 60, Items["wolfhide_cloak"], 1, (lynxPelt, 1), (pelt, 4));
            RecipeItem("recipe_soot_cloak", "Muster: Umhang mit Rußfutter", rSootCloak, ItemQuality.Rare, 150);

            // Rezepte
            RecipeDefinition rBoots = Recipe("Recipe_StalkerBoots", "recipe.stalker_boots", "Jägerstiefel", leatherworking, 25, stalkerBoots, 1, (pelt, 4), (torn, 2));
            RecipeDefinition rDraught = Recipe("Recipe_HealingDraught", "recipe.healing_draught", "Heiltrank", alchemy, 1, draught, 2, (emberMoss, 2), (linen, 1));
            RecipeDefinition rGauntlets = Recipe("Recipe_IronGauntlets", "recipe.iron_gauntlets", "Eisenhandschuhe", smithing, 40, gauntlets, 1, (ore, 6), (scrap, 4));
            RecipeItem("recipe_stalker_boots", "Muster: Jägerstiefel", rBoots, ItemQuality.Uncommon, 60);
            RecipeItem("recipe_healing_draught", "Rezept: Heiltrank", rDraught, ItemQuality.Common, 15);
            RecipeItem("recipe_iron_gauntlets", "Plan: Eisenhandschuhe", rGauntlets, ItemQuality.Uncommon, 80);

            // Quest (fuer Milestone 4)
            Item("faded_letter", "Verblichener Brief", ItemCategory.Quest, ItemQuality.Common, 1, 1, IconShape.Letter, Linen, 0,
                "Die Tinte ist zu Asche zerfallen. Nur ein Name ist noch lesbar.", d => { d.bind = BindType.OnPickup; d.unique = true; d.questId = "quest.faded_letter"; });
            Item("bell_shard", "Glockensplitter", ItemCategory.Quest, ItemQuality.Uncommon, 5, 1, IconShape.Gem, GoldC, 0,
                "Noch immer klingt er, wenn man ihn berührt.", d => { d.bind = BindType.OnPickup; d.maxStack = 5; d.questId = "quest.bell_shepherd"; });

            // Verkaeufliche Beute
            Junk("tarnished_buttons", "Angelaufene Knöpfe", IconShape.Coin, Ash, 6, "Von einem Mantel, der niemandem mehr passt.");
            Junk("broken_pocketwatch", "Zerbrochene Taschenuhr", IconShape.Coin, GoldC, 35, "Stehen geblieben um zwölf nach drei.");
            Junk("faded_coin", "Verblasste Münze", IconShape.Coin, new Color(0.7f, 0.6f, 0.45f), 12, "Das Gesicht darauf ist weggerieben.");
            Junk("ash_caked_ribbon", "Aschverkrustetes Band", IconShape.Cloth, Blood, 4, "");
            Junk("cracked_wolf_tooth", "Gesprungener Wolfszahn", IconShape.Bone, Bone, 3, "");

            // ---------------------------------------------------------- M6.5 Salzgruben (Stufe 10–14)
            // Das Salz gilt als Fleisch der Alten (Mythologie M6) – die Beute spielt damit, bestaetigt es aber nie.
            Color saltWhite = new Color(0.9f, 0.89f, 0.86f), veinRed = new Color(0.7f, 0.2f, 0.18f), brine = new Color(0.3f, 0.42f, 0.5f);
            Material("mine_salt", "Grubensalz", ItemQuality.Common, 11, IconShape.Ore, saltWhite, 9, mining,
                "Grob, schwer und seltsam warm, wenn man es zu lange in der Hand hält.");
            Material("red_vein_salt", "Rotaderiges Salz", ItemQuality.Uncommon, 13, IconShape.Gem, veinRed, 38, mining,
                "Die Arbeiter nannten die roten Adern „das Blut der Alten“ und schlugen sie nie ohne Gebet heraus.");
            Material("salt_crab_shell", "Salzkrebsschale", ItemQuality.Common, 11, IconShape.Hide, new Color(0.92f, 0.82f, 0.78f), 11, skinning,
                "Hart wie Stein, leicht wie Holz.");
            Material("lamp_soot", "Lampenruß", ItemQuality.Common, 12, IconShape.Ore, new Color(0.12f, 0.12f, 0.14f), 6, null,
                "Was von einem Lichtfresser übrig bleibt. Er schluckt noch immer ein wenig Licht.");
            Material("heart_shard", "Herzsplitter", ItemQuality.Rare, 14, IconShape.Gem, new Color(0.95f, 0.3f, 0.28f), 160, null,
                "Ein Splitter vom Grubenherz. Wer ihn ans Ohr hält, hört einen langsamen Schlag.");
            // Nachlese des Heilers am Grubenschrat (Klassen-Identitaet Phase 3): intakt ≠ gebrochen ≠ abgetrennt (§40).
            // Keine Beruf-Zuordnung – geborgen wird nur vom Heiler, nicht gesammelt.
            Material("salt_tissue", "Salzgetränktes Gewebe", ItemQuality.Common, 12, IconShape.Hide, new Color(0.78f, 0.66f, 0.62f), 7, null,
                "Zäh und trocken. Feldheiler kochen daraus Verbände, die nicht faulen.");
            Material("salt_sinew", "Salzsehne", ItemQuality.Uncommon, 13, IconShape.Cloth, new Color(0.93f, 0.9f, 0.8f), 30, null,
                "Aus einem unversehrten Arm geborgen. Näht eine Wunde, die sonst nicht hält.");
            Material("salt_marrow", "Salzmark", ItemQuality.Rare, 13, IconShape.Potion, new Color(0.86f, 0.32f, 0.34f), 95, null,
                "Rot und warm aus dem gebrochenen Knochen. Alchemisten brauen daraus ein Gegengift – und flüstern, es sei das Mark der Alten.");
            Material("brute_claw", "Grubenschrat-Klaue", ItemQuality.Uncommon, 13, IconShape.Bone, new Color(0.84f, 0.8f, 0.72f), 45, null,
                "Vom abgetrennten Arm. Eine Trophäe – und ein Schaber, mit dem die Grubenleute früher Salz von den Wänden holten.");
            Junk("tally_stick", "Kerbholz", IconShape.Bone, Linen, 9, "Jede Kerbe eine Schicht. Die letzte Kerbe ist nur halb geschnitten.");
            Junk("salt_crusted_coin", "Salzverkrustete Münze", IconShape.Coin, saltWhite, 22, "Unter der Kruste ein Königskopf, den niemand mehr kennt.");
            Junk("drowned_logbook_page", "Aufgequollene Logbuchseite", IconShape.Letter, brine, 30, "„…das Salz trägt das Schiff nicht mehr. Es hält es fest.“");

            // Waffen der Grube
            WeaponDefinition minersPick = Weapon("Weapon_ForemansPick", "weapon.foremans_pick", "Picke des Vorarbeiters",
                w => { Melee(w, WeaponClass.Mace, 19f, 27f, 2.4f, 1.1f, "Sprengschlag"); w.quality = ItemQuality.Rare; });
            WeaponDefinition saltRifle = Weapon("Weapon_SaltLakeRifle", "weapon.salt_lake_rifle", "Salzsee-Büchse",
                w => { Gun(w, WeaponClass.Rifle, WeaponArchetype.Hitscan, 32f, 42f, 42f, 1.6f, 4, 2.6f, "Salzkorn"); w.quality = ItemQuality.Rare; });
            WeaponDefinition saltBow = Weapon("Weapon_SaltLakeBow", "weapon.salt_lake_bow", "Salzsee-Bogen",
                w => { Bow(w, 72f, 96f, 36f, 3.0f); w.quality = ItemQuality.Rare; }); // P2: 90–120 → 72–96 (Balance-Test Stufe 12: 158 %)
            WeaponDefinition drownedSabre = Weapon("Weapon_DrownedSabre", "weapon.drowned_sabre", "Säbel des Ertrunkenen",
                w => { Melee(w, WeaponClass.Sword, 17f, 24f, 2.4f, 0.95f, "Entern"); w.quality = ItemQuality.Rare; });
            WeaponDefinition lampStaff = Weapon("Weapon_LamplightStaff", "weapon.lamplight_staff", "Lampenlicht-Stab",
                w => { Spell(w, 36f, 48f, 32f, "Lichtsturm", new Color(1f, 0.8f, 0.5f)); w.quality = ItemQuality.Epic; }); // P2: 40–54 → 36–48 (134 %)
            WeaponDefinition crystalBlade = Weapon("Weapon_HeartCrystalBlade", "weapon.heart_crystal_blade", "Herzkristallklinge",
                w => { Melee(w, WeaponClass.TwoHandedSword, 32f, 44f, 3f, 1.3f, "Herzschlag"); w.quality = ItemQuality.Epic; w.critChance = 0.08f; w.hasAirAttacks = true; });

            // P2 Balance (30.09.): Waffenstufe zwischen Stufe 5 und 12 (Belohnung "Grubensalz", eine zur Wahl)
            WeaponDefinition hewerDagger = Weapon("Weapon_HewerDagger", "weapon.hewer_dagger", "Grubenhauer-Dolch",
                w => { Melee(w, WeaponClass.Dagger, 12f, 16f, 2f, 0.7f, "Keilstich"); w.quality = ItemQuality.Uncommon; });
            WeaponDefinition shaftMace = Weapon("Weapon_ShaftMace", "weapon.shaft_mace", "Stollenkeule",
                w => { Melee(w, WeaponClass.Mace, 16f, 21f, 2.3f, 1f, "Stempelschlag"); w.quality = ItemQuality.Uncommon; });
            WeaponDefinition saltwoodStaff = Weapon("Weapon_SaltwoodStaff", "weapon.saltwood_staff", "Salzholzstab",
                w => { Spell(w, 33f, 44f, 30f, "Salzfunke", new Color(0.95f, 0.85f, 0.7f)); w.quality = ItemQuality.Uncommon; });
            WeaponItem("hewer_dagger", "Grubenhauer-Dolch", hewerDagger, ItemQuality.Uncommon, 11, 9, IconShape.Dagger, Iron, 230,
                "Ein Keil mit Griff. Schneidet trotzdem.", BindType.OnPickup, (StatType.Agility, 4), (StatType.Stamina, 3));
            WeaponItem("shaft_mace", "Stollenkeule", shaftMace, ItemQuality.Uncommon, 11, 9, IconShape.Mace, Iron, 230,
                "Früher stützte sie einen Stollen. Jetzt stützt sie dich.", BindType.OnPickup, (StatType.Strength, 4), (StatType.Stamina, 3));
            WeaponItem("saltwood_staff", "Salzholzstab", saltwoodStaff, ItemQuality.Uncommon, 11, 9, IconShape.Staff, saltWhite, 230,
                "Das Holz hat Jahre in der Lake gelegen. Es knistert, wenn es warm wird.", BindType.OnPickup, (StatType.Intellect, 4), (StatType.Willpower, 3));

            // Ausruestung der Grube (Stufe 10–12, faellt von allen Gegnern)
            Armor("miner_helmet", "Grubenhelm", ItemQuality.Uncommon, 11, 10, EquipType.Head, ArmorType.Mail, 26, IconShape.Helmet, Iron, 210,
                "Die Beule vorn stammt nicht von einem Stein.", (StatType.Stamina, 3), (StatType.Strength, 2));
            Armor("hewer_gloves", "Hauerhandschuhe", ItemQuality.Uncommon, 11, 10, EquipType.Hands, ArmorType.Leather, 13, IconShape.Gloves, DarkLeather, 190,
                "Innen mit Salz verkrustet – außen mit Blut.", (StatType.Agility, 3), (StatType.Stamina, 2));
            Armor("salt_crusted_sabatons", "Salzverkrustete Eisenschuhe", ItemQuality.Uncommon, 11, 10, EquipType.Feet, ArmorType.Plate, 30, IconShape.Boots, saltWhite, 200,
                "Knirschen bei jedem Schritt.", (StatType.Strength, 3), (StatType.Stamina, 3));
            Armor("brine_robe", "Salzlaken-Robe", ItemQuality.Uncommon, 12, 10, EquipType.Chest, ArmorType.Cloth, 15, IconShape.Chest, brine, 230,
                "Trocknet nie ganz.", (StatType.Intellect, 4), (StatType.Willpower, 3));
            Armor("lamp_bearer_cloak", "Umhang des Lampenträgers", ItemQuality.Uncommon, 12, 10, EquipType.Cloak, ArmorType.None, 12, IconShape.Cloak, new Color(0.4f, 0.34f, 0.26f), 200,
                "Am Saum hängt noch der Haken für die Lampe.", (StatType.Stamina, 3), (StatType.Willpower, 2));
            Armor("shift_belt", "Schichtgürtel", ItemQuality.Uncommon, 11, 10, EquipType.Belt, ArmorType.Leather, 7, IconShape.Belt, Leather, 170,
                "Mit Schlaufen für Keile, Lunte und Brot.", (StatType.Agility, 2), (StatType.Stamina, 3));
            Armor("crab_shell_pauldrons", "Krebsschalen-Schulterstücke", ItemQuality.Uncommon, 12, 11, EquipType.Shoulders, ArmorType.Plate, 36, IconShape.Shoulders, new Color(0.9f, 0.78f, 0.74f), 240,
                "", (StatType.Strength, 3), (StatType.Stamina, 4));
            Armor("salt_ring", "Salzring", ItemQuality.Uncommon, 12, 10, EquipType.Ring, ArmorType.None, 0, IconShape.Ring, saltWhite, 220,
                "Aus einem einzigen Salzkristall geschliffen.", (StatType.Stamina, 3), (StatType.Intellect, 2));

            // Boss 1: Vorarbeiter Hask
            Armor("blastmaster_gauntlets", "Sprengmeisterhandschuhe", ItemQuality.Rare, 13, 11, EquipType.Hands, ArmorType.Plate, 34, IconShape.Gloves, Iron, 520,
                "Die Fingerkuppen sind verbrannt. Die Finger darunter auch.", (StatType.Strength, 5), (StatType.Stamina, 5)).Post(d => d.bind = BindType.OnPickup);
            Armor("fuse_cord_belt", "Luntengürtel", ItemQuality.Rare, 13, 11, EquipType.Belt, ArmorType.Leather, 10, IconShape.Belt, Ember, 480,
                "Riecht nach Pulver. Nicht rauchen.", (StatType.Agility, 5), (StatType.Stamina, 4)).Post(d => d.bind = BindType.OnPickup);
            Armor("smoulder_wraps", "Glimmschnur-Wickel", ItemQuality.Rare, 13, 11, EquipType.Hands, ArmorType.Cloth, 9, IconShape.Gloves, Ember, 480,
                "Die Schnur glimmt, aber sie brennt nie herunter.", (StatType.Intellect, 5), (StatType.Willpower, 4)).Post(d => d.bind = BindType.OnPickup);
            WeaponItem("foremans_pick", "Picke des Vorarbeiters", minersPick, ItemQuality.Rare, 13, 11, IconShape.Mace, Iron, 560,
                "Hask hat damit die Schichten eingeschlagen. Und manchmal auch Köpfe.", BindType.OnPickup, (StatType.Strength, 5), (StatType.Stamina, 4));

            // Boss 2: Die Salzmutter
            Armor("claw_shell_pauldrons", "Scherenpanzer", ItemQuality.Rare, 14, 12, EquipType.Shoulders, ArmorType.Plate, 44, IconShape.Shoulders, new Color(0.85f, 0.65f, 0.62f), 600,
                "Aus einer Schere geschnitten, die einmal einen Mann halbiert hat.", (StatType.Strength, 5), (StatType.Stamina, 6)).Post(d => d.bind = BindType.OnPickup);
            Armor("brood_mantle", "Brutmantel", ItemQuality.Rare, 14, 12, EquipType.Chest, ArmorType.Cloth, 18, IconShape.Chest, new Color(0.85f, 0.7f, 0.7f), 600,
                "Warm. Viel zu warm für etwas aus einem Salzsee.", (StatType.Intellect, 6), (StatType.Willpower, 4)).Post(d => d.bind = BindType.OnPickup);
            WeaponItem("salt_lake_rifle", "Salzsee-Büchse", saltRifle, ItemQuality.Rare, 14, 12, IconShape.Rifle, brine, 640,
                "Geladen mit Salz statt Blei – es brennt in den Wunden.", BindType.OnPickup, (StatType.Agility, 6), (StatType.Stamina, 3));
            WeaponItem("salt_lake_bow", "Salzsee-Bogen", saltBow, ItemQuality.Rare, 14, 12, IconShape.Bow, new Color(0.85f, 0.84f, 0.8f), 640,
                "Aus einem Treibholz, das zehn Jahre im Salz lag.", BindType.OnEquip, (StatType.Agility, 8), (StatType.Stamina, 5));

            // Optional: Der Ertrunkene Kapitaen
            Armor("drowned_tricorne", "Dreispitz des Ertrunkenen", ItemQuality.Rare, 13, 11, EquipType.Head, ArmorType.Leather, 16, IconShape.Helmet, brine, 520,
                "Salzränder zeigen, wie hoch das Wasser einmal stand.", (StatType.Agility, 5), (StatType.Stamina, 4)).Post(d => d.bind = BindType.OnPickup);
            WeaponItem("drowned_sabre", "Säbel des Ertrunkenen", drownedSabre, ItemQuality.Rare, 13, 11, IconShape.Sword, Steel, 560,
                "Die Klinge ist blank. Das Salz hat nur den Griff gefressen.", BindType.OnPickup, (StatType.Strength, 3), (StatType.Agility, 3), (StatType.Stamina, 3));
            Item("brine_focus", "Laken-Fokus", ItemCategory.Weapon, ItemQuality.Rare, 13, 11, IconShape.Focus, brine, 500,
                "Eine Glaskugel voller Salzwasser. Manchmal treibt ein Schatten darin.", d =>
                {
                    d.equipType = EquipType.Weapon;
                    d.hand = WeaponHand.OffHandOnly;
                    d.bind = BindType.OnPickup;
                    d.allowedClasses = new List<CharacterClassDefinition> { weaver };
                    d.stats = Stats((StatType.Intellect, 5), (StatType.Willpower, 4));
                });

            // Endboss: Das Grubenherz
            Armor("heart_of_the_old", "Herz des Alten", ItemQuality.Epic, 15, 12, EquipType.Trinket, ArmorType.None, 0, IconShape.Gem, new Color(0.95f, 0.3f, 0.28f), 1400,
                "Es schlägt. Langsam, aber es schlägt.", (StatType.Stamina, 6)).Post(d =>
            {
                d.bind = BindType.OnPickup;
                d.unique = true;
                d.effects = new List<ItemEffect> { new ItemEffect { type = ItemEffectType.HealthPercent, value = 6f }, new ItemEffect { type = ItemEffectType.LifeOnHit, value = 5f } };
            });
            Armor("salt_giant_cuirass", "Harnisch aus Riesensalz", ItemQuality.Epic, 15, 12, EquipType.Chest, ArmorType.Plate, 78, IconShape.Chest, saltWhite, 1200,
                "Salzplatten, so dick wie ein Daumen. Man sagt, sie seien einmal Haut gewesen.", (StatType.Strength, 8), (StatType.Stamina, 9)).Post(d => d.bind = BindType.OnPickup);
            Armor("shadowhide_jerkin", "Schattenlederwams", ItemQuality.Epic, 15, 12, EquipType.Chest, ArmorType.Leather, 32, IconShape.Chest, new Color(0.14f, 0.13f, 0.16f), 1200,
                "Gegerbt aus etwas, das Licht fraß. Es ist immer ein wenig dunkler als der Raum.", (StatType.Agility, 9), (StatType.Stamina, 7)).Post(d => d.bind = BindType.OnPickup);
            WeaponItem("lamplight_staff", "Lampenlicht-Stab", lampStaff, ItemQuality.Epic, 15, 12, IconShape.Staff, new Color(1f, 0.8f, 0.5f), 1300,
                "Im Knauf brennt die letzte Flamme der vier Lampen.", BindType.OnPickup, (StatType.Intellect, 9), (StatType.Willpower, 6));
            WeaponItem("heart_crystal_blade", "Herzkristallklinge", crystalBlade, ItemQuality.Epic, 15, 12, IconShape.Sword, new Color(0.95f, 0.35f, 0.3f), 1400,
                "Rot geädert wie das Herz, aus dem sie gebrochen wurde.", BindType.OnPickup, (StatType.Strength, 9), (StatType.Stamina, 7));

            // P3 Glockenrast (Stufe 5–9): Auftragsgegenstaende, Waffen zur Wahl, Schmuck, Ruestungsteile
            BuildGlockenrastItems();

            // Legendaeres Schwert „Nachhall“ Rev. E (Max 04.10.): zwei Klingen zur Wahl – in keiner Beutetabelle (Questreihe folgt)
            BuildNachhallItems();

            // #28 Kareth v4, Welle 0: Belohnungen, Auftragsgegenstaende und Tierbeute der elf Ergaenzungsauftraege
            BuildKarethItems(warden, stalker, weaver, skinning);

            // Westweiden (Lückenliste Punkt 2, 12.10.2026): Auftragsgegenstände W01–W28
            BuildWestweidenItems();
            // Hauptquest der Menschen, Akt I „Das Register“ (13.10.2026): Register und versiegelte Seite
            BuildRegisterItems();

            // ---------------------------------------------------------- Startausruestung
            // Feuerwaffen ruhen (Kampfumbau): Eidwaechter mit Schild, Aschenjaeger mit zwei Dolchen, Kurzbogen, Koecher und Pfeilen
            // 21.10.2026 (Tripo-Kleidung): Reisehose fuer alle drei Klassen – sonst starten neue Figuren mit nackten Beinen. Gilt nur
            // fuer neue Figuren (CharacterStore.CreateNew); bestehende Spielstaende bleiben, wie sie sind.
            SetStart(warden, new[] { iSword, iShield, gambeson, trousers, wraps }, (draught, 3), (bread, 2));
            SetStart(stalker, new[] { iDagger, iDagger, iShortbow, lightQuiver, gambeson, trousers, stalkerGloves, wraps }, (roughArrow, 200), (draught, 3), (bread, 2));
            SetStart(weaver, new[] { iStaff, sandals, gambeson, trousers }, (draught, 3), (bread, 2));
            // SetStart schreibt ohne Versionssprung nur leere Klassen – die Hose daher gezielt nachtragen (ohne DefaultsVersion, die alle
            // Standardwerte neu schreiben wuerde). Nur wenn die Klasse noch nichts fuer die Beine hat.
            foreach (CharacterClassDefinition klasse in new[] { warden, stalker, weaver }) StartNachtrag(klasse, trousers);

            // ---------------------------------------------------------- Beutetabellen
            string[] commonGear = { "dented_helm", "chainmail_shirt", "stalker_gloves", "rope_belt", "grey_wool_cloak", "tarnished_ring", "weaver_sandals", "cinder_dagger", "rusty_longsword",
                                    "travel_hood", "travel_mantle", "travel_wraps", "travel_trousers", "iron_sabatons" };
            string[] goodGear = { "warden_pauldrons", "iron_gauntlets", "stalker_hood", "ashen_leather_jerkin", "stalker_boots", "weaver_robe", "ember_sash",
                                  "wolfhide_cloak", "bone_amulet", "notched_greataxe", "warden_blade", "ember_staff", "cracked_focus", "ashwood_longbow", "wool_bag",
                                  "hunter_shoulderguards", "hunter_breeches", "warden_cuirass", "oak_shield" };
            string[] rareGear = { "plated_greaves", "cinder_signet", "shepherds_bell_charm", "bellbronze_recurve", "hunters_quiver" };

            LootTable("Loot_Aschwolf", "loot.ash_wolf", "Aschwolf", t =>
            {
                t.entries = Entries(("ash_wolf_pelt", 1f, 1, 2), ("wolf_fang", 0.45f, 1, 1), ("torn_leather", 0.3f, 1, 2), ("cracked_wolf_tooth", 0.35f, 1, 1));
                t.groups = new List<LootTableDefinition.Group>
                {
                    Group("Ausrüstung", 0.14f, true, Weighted(commonGear, 10f), Weighted(goodGear, 3f)),
                    Group("Selten", 0.025f, true, Weighted(rareGear, 1f), Weighted(new[] { "gravewarden_mace", "ember_heart" }, 0.25f)),
                    Group("Rezepte", 0.05f, false, Weighted(new[] { "recipe_stalker_boots", "recipe_healing_draught" }, 1f))
                };
            });
            // 21.10. (AUFTRAEGE-03/FORTSCHRITT-04): kein Verblichener Brief und keine Glockensplitter mehr in der Beute (hier, bei der Schützin
            // und beim Glockenhirten), bis ihre Aufträge (quest.faded_letter, quest.bell_shepherd) existieren – sonst unverkäufliche
            // „Questgegenstände“ ohne Auftrag, die nicht wegzuwerfen sind und Rucksackplätze für immer belegen. Die Gegenstände selbst bleiben.
            LootTable("Loot_Verblichener", "loot.faded_one", "Verblichener", t =>
            {
                t.entries = Entries(("linen_scraps", 0.5f, 1, 3), ("rusted_iron_scrap", 0.4f, 1, 2), ("faded_coin", 0.55f, 1, 2),
                    ("tarnished_buttons", 0.3f, 1, 1), ("healing_draught", 0.15f, 1, 1),
                    ("black_powder", 0.6f, 1, 2), ("ash_crow_feather", 0.55f, 1, 2));
                t.groups = new List<LootTableDefinition.Group>
                {
                    Group("Ausrüstung", 0.2f, true, Weighted(commonGear, 6f), Weighted(goodGear, 5f)),
                    Group("Selten", 0.05f, true, Weighted(rareGear, 1f), Weighted(new[] { "gravewarden_mace", "ember_heart" }, 0.4f), Weighted(new[] { "bellringer" }, 0.04f)),
                    Group("Rezepte", 0.06f, false, Weighted(new[] { "recipe_iron_gauntlets", "recipe_healing_draught" }, 1f))
                };
            });
            LootTable("Loot_VerblicheneSchuetzin", "loot.faded_gunner", "Verblichene Schützin", t =>
            {
                t.entries = Entries(("grave_salt", 0.4f, 1, 2), ("broken_pocketwatch", 0.25f, 1, 1), ("faded_coin", 0.5f, 1, 3),
                    ("ash_tea", 0.2f, 1, 2), ("black_powder", 0.75f, 1, 3), ("ash_crow_feather", 0.65f, 1, 3));   // 21.10.: ohne Glockensplitter (AUFTRAEGE-03)
                t.groups = new List<LootTableDefinition.Group>
                {
                    Group("Ausrüstung", 0.25f, true, Weighted(commonGear, 4f), Weighted(goodGear, 6f)),
                    Group("Selten", 0.08f, true, Weighted(rareGear, 1f), Weighted(new[] { "ember_heart" }, 0.3f), Weighted(new[] { "bellringer" }, 0.05f)),
                    Group("Rezepte", 0.06f, false, Weighted(new[] { "recipe_iron_gauntlets", "recipe_stalker_boots" }, 1f))
                };
            });
            // Kareth (M5.4): Tiere lassen nie Muenzen oder Waffen fallen – nur Ruestung, Material, Rezepte
            string[] commonArmor = { "dented_helm", "chainmail_shirt", "stalker_gloves", "rope_belt", "grey_wool_cloak", "tarnished_ring", "weaver_sandals",
                                     "travel_hood", "travel_mantle", "travel_wraps", "travel_trousers", "iron_sabatons" };
            string[] goodArmor = { "warden_pauldrons", "iron_gauntlets", "stalker_hood", "ashen_leather_jerkin", "stalker_boots", "weaver_robe", "ember_sash",
                                   "wolfhide_cloak", "bone_amulet", "hunter_shoulderguards", "hunter_breeches", "warden_cuirass" };
            LootTable("Loot_Torfschrat", "loot.peat_brute", "Torfschrat", t =>
            {
                t.entries = Entries(("peat_clump", 1f, 1, 3), ("bog_root", 0.6f, 1, 2), ("swamp_herb", 0.5f, 1, 2), ("ember_moss", 0.2f, 1, 2));
                t.groups = new List<LootTableDefinition.Group>
                {
                    Group("Ausrüstung", 0.18f, true, Weighted(commonArmor, 5f), Weighted(goodArmor, 6f)),
                    Group("Selten", 0.04f, true, Weighted(new[] { "plated_greaves", "cinder_signet", "shepherds_bell_charm" }, 1f))
                };
            });
            LootTable("Loot_Nadelspeier", "loot.needle_spitter", "Nadelspeier", t =>
            {
                t.entries = Entries(("chitin_plate", 0.8f, 1, 2), ("venom_gland", 0.45f, 1, 1), ("spine_needle", 0.7f, 2, 4));
                t.groups = new List<LootTableDefinition.Group>
                {
                    Group("Ausrüstung", 0.15f, true, Weighted(commonArmor, 5f), Weighted(goodArmor, 5f)),
                    Group("Selten", 0.03f, true, Weighted(new[] { "plated_greaves", "cinder_signet" }, 1f))
                };
            });
            LootTable("Loot_Russluchs", "loot.soot_lynx", "Die Graue Mutter", t =>
            {
                t.entries = Entries(("soot_lynx_pelt", 1f, 1, 1), ("glowing_claw", 1f, 1, 2), ("recipe_soot_cloak", 0.35f, 1, 1));
                t.groups = new List<LootTableDefinition.Group>
                {
                    Group("Ausrüstung", 0.8f, true, Weighted(goodArmor, 4f), Weighted(new[] { "plated_greaves", "cinder_signet", "shepherds_bell_charm" }, 2f))
                };
            });
            LootTable("Loot_Glockenhirte", "loot.bell_shepherd", "Der Glockenhirte", t =>
            {
                t.entries = Entries(("shepherds_bell_charm", 1f, 1, 1), ("ember_heart", 0.5f, 1, 1));   // 21.10.: ohne Glockensplitter (FORTSCHRITT-04)
                t.groups = new List<LootTableDefinition.Group>
                {
                    Group("Selten", 1f, true, Weighted(new[] { "plated_greaves", "cinder_signet", "bellbronze_recurve" }, 1f),
                        Weighted(new[] { "gravewarden_mace", "bellringer" }, 0.5f))
                };
            });
            // M6.5 Salzgruben: eigene Tabellen (Grubenausruestung Stufe 10–12, Bossbeute je Klasse garantiert)
            string[] mineGear = { "miner_helmet", "hewer_gloves", "salt_crusted_sabatons", "brine_robe", "lamp_bearer_cloak", "shift_belt",
                                  "crab_shell_pauldrons", "salt_ring" };
            LootTable("Loot_Grubenarbeiter", "loot.mine_worker", "Verblichener Grubenarbeiter", t =>
            {
                t.entries = Entries(("mine_salt", 0.7f, 1, 2), ("tally_stick", 0.35f, 1, 1), ("salt_crusted_coin", 0.35f, 1, 2),
                    ("black_powder", 0.4f, 1, 2), ("strong_healing_draught", 0.12f, 1, 1));
                t.groups = new List<LootTableDefinition.Group>
                {
                    Group("Ausrüstung", 0.16f, true, Weighted(mineGear, 6f), Weighted(goodArmor, 2f)),
                    Group("Selten", 0.02f, true, Weighted(new[] { "blastmaster_gauntlets", "fuse_cord_belt", "smoulder_wraps" }, 1f))
                };
            });
            LootTable("Loot_Salzkrebs", "loot.salt_crab", "Salzkrebs", t =>
            {
                t.entries = Entries(("salt_crab_shell", 0.9f, 1, 2), ("mine_salt", 0.4f, 1, 2));
                t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.08f, true, Weighted(mineGear, 1f)) };
            });
            LootTable("Loot_NadelspeierBrut", "loot.needle_brood", "Nadelspeier-Brut", t =>
            {
                t.entries = Entries(("chitin_plate", 0.6f, 1, 1), ("venom_gland", 0.3f, 1, 1), ("spine_needle", 0.5f, 1, 3), ("mine_salt", 0.2f, 1, 1));
                t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.06f, true, Weighted(mineGear, 1f)) };
            });
            LootTable("Loot_Grubenschrat", "loot.salt_brute", "Grubenschrat", t =>
            {
                t.entries = Entries(("mine_salt", 1f, 2, 4), ("red_vein_salt", 1f, 1, 2));
                t.groups = new List<LootTableDefinition.Group>
                {
                    Group("Ausrüstung", 0.4f, true, Weighted(mineGear, 1f)),
                    Group("Selten", 0.08f, true, Weighted(new[] { "claw_shell_pauldrons", "brood_mantle", "salt_lake_bow" }, 1f))
                };
            });
            LootTable("Loot_Lichtfresser", "loot.light_eater", "Lichtfresser", t =>
            {
                t.entries = Entries(("lamp_soot", 0.8f, 1, 2));
                t.groups = new List<LootTableDefinition.Group>();
            });
            LootTable("Loot_VorarbeiterHask", "loot.foreman_hask", "Vorarbeiter Hask", t =>
            {
                t.entries = Entries(("red_vein_salt", 1f, 2, 3), ("salt_crusted_coin", 1f, 2, 4), ("black_powder", 1f, 3, 5));
                t.groups = new List<LootTableDefinition.Group>
                {
                    Group("Hasks Beute", 1f, true, Weighted(new[] { "blastmaster_gauntlets", "fuse_cord_belt", "smoulder_wraps", "foremans_pick" }, 1f)),
                    Group("Ausrüstung", 0.3f, true, Weighted(mineGear, 1f))
                };
            });
            LootTable("Loot_Salzmutter", "loot.salt_mother", "Die Salzmutter", t =>
            {
                t.entries = Entries(("red_vein_salt", 1f, 2, 3), ("salt_crab_shell", 1f, 3, 5));
                t.groups = new List<LootTableDefinition.Group>
                {
                    Group("Beute der Salzmutter", 1f, true, Weighted(new[] { "claw_shell_pauldrons", "brood_mantle", "salt_lake_bow" }, 1f)),
                    Group("Ausrüstung", 0.3f, true, Weighted(mineGear, 1f))
                };
            });
            LootTable("Loot_ErtrunkenerKapitaen", "loot.drowned_captain", "Der Ertrunkene Kapitän", t =>
            {
                t.entries = Entries(("drowned_logbook_page", 1f, 1, 1), ("salt_crusted_coin", 1f, 3, 6), ("mine_salt", 1f, 2, 3));
                t.groups = new List<LootTableDefinition.Group>
                {
                    Group("Beute des Kapitäns", 1f, true, Weighted(new[] { "drowned_tricorne", "drowned_sabre", "brine_focus" }, 1f))
                };
            });
            LootTable("Loot_Grubenherz", "loot.mine_heart", "Das Grubenherz", t =>
            {
                t.entries = Entries(("heart_shard", 1f, 1, 1), ("red_vein_salt", 1f, 3, 5));
                t.groups = new List<LootTableDefinition.Group>
                {
                    Group("Beute des Herzens", 1f, true, Weighted(new[] { "salt_giant_cuirass", "shadowhide_jerkin", "lamplight_staff", "heart_crystal_blade" }, 1f)),
                    Group("Selten", 0.25f, true, Weighted(new[] { "heart_of_the_old" }, 1f))
                };
            });

            // Selbsttest: alles sicher, feste Mengen (siehe DuoTestDirector)
            LootTable("Loot_Selbsttest", "loot.selftest", "Selbsttest", t =>
            {
                t.entries = Entries(("ash_wolf_pelt", 1f, 3, 3), ("notched_greataxe", 1f, 1, 1), ("cinder_signet", 1f, 1, 1),
                    ("healing_draught", 1f, 2, 2), ("recipe_stalker_boots", 1f, 1, 1), ("broken_pocketwatch", 1f, 1, 1), ("ember_moss", 1f, 4, 4));
                t.groups = new List<LootTableDefinition.Group>();
            });

            // P3 Glockenrast: Giftdruesen als Auftragsbeute der Nadelspeier (nur mit laufendem „Gegengift“)
            AddGlockenrastLoot();
            // #28 Kareth v4: Tiere, Matrosen am Wrack, hungriger Deserteur (Auftragsbeute nur mit laufendem Auftrag)
            BuildKarethLoot();
            BuildWestweidenLoot();

            EditorPrefs.SetInt(VersionKey, DefaultsVersion);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Ashveil] Itemsystem: {Items.Count} Gegenstände, 3 Klassen, 6 Berufe, 4 Rezepte, 23 Beutetabellen.");
        }

        /// <summary>Beutetabellen an die Gegner haengen (nach <see cref="EnemyBuilder.Build"/>).</summary>
        public static void AssignLootTables()
        {
            Assign("Enemy_Aschwolf", "Loot_Aschwolf");
            Assign("Enemy_Verblichener", "Loot_Verblichener");
            Assign("Enemy_VerblicheneSchuetzin", "Loot_VerblicheneSchuetzin");
            Assign("Enemy_Torfschrat", "Loot_Torfschrat");
            Assign("Enemy_Nadelspeier", "Loot_Nadelspeier");
            Assign("Enemy_Russluchs", "Loot_Russluchs");
            Assign("Enemy_Glockenhirte", "Loot_Glockenhirte");
            // M6.5 Salzgruben – eigene Tabellen (ersetzen die Platzhalter aus M6.1–M6.4)
            string[] placeholders = { "Loot_Verblichener", "Loot_Nadelspeier", "Loot_Torfschrat", "Loot_Aschwolf" };
            Assign("Enemy_Grubenarbeiter", "Loot_Grubenarbeiter", placeholders);
            Assign("Enemy_Salzkrebs", "Loot_Salzkrebs", placeholders);
            Assign("Enemy_Krebsbrut", "Loot_Salzkrebs", placeholders);
            Assign("Enemy_NadelspeierBrut", "Loot_NadelspeierBrut", placeholders);
            Assign("Enemy_Grubenschrat", "Loot_Grubenschrat", placeholders);
            Assign("Enemy_Lichtfresser", "Loot_Lichtfresser", placeholders);
            Assign("Enemy_VorarbeiterHask", "Loot_VorarbeiterHask", placeholders);
            Assign("Enemy_Salzmutter", "Loot_Salzmutter", placeholders);
            Assign("Enemy_ErtrunkenerKapitaen", "Loot_ErtrunkenerKapitaen", placeholders);
            Assign("Enemy_ErtrunkenerMatrose", "Loot_Grubenarbeiter", placeholders);
            Assign("Enemy_Grubenherz", "Loot_Grubenherz", placeholders);
            AssignBorderLoot();
            AssignKarethLoot();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Grenzgebirge: vorlaeufig Tabellen vorhandener Gegner (eigene Beute 16–22 kommt im Inhalts-Pass).</summary>
        public static void AssignBorderLoot()
        {
            Assign("Enemy_Gratwolf", "Loot_Aschwolf");
            Assign("Enemy_Deserteur", "Loot_Grubenarbeiter");
            Assign("Enemy_DeserteurSchuetzin", "Loot_VerblicheneSchuetzin");
            Assign("Enemy_Spaltenspeier", "Loot_NadelspeierBrut");
            Assign("Enemy_Felsschrat", "Loot_Grubenschrat");
        }

        /// <summary>Beutetabelle zuweisen – nur wenn keine gesetzt ist, bei neuen Standardwerten, oder (replace) wenn die
        /// jetzige nur ein Platzhalter war. Von Hand im Inspector gewaehlte Tabellen bleiben sonst erhalten.</summary>
        private static void Assign(string enemyFile, string lootFile, string[] replace = null)
        {
            var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(EnemyBuilder.DefFolder + "/" + enemyFile + ".asset");
            var loot = AssetDatabase.LoadAssetAtPath<LootTableDefinition>(LootFolder + "/" + lootFile + ".asset");
            if (enemy == null || loot == null) return;
            bool placeholder = replace != null && enemy.lootTable != null && enemy.lootTable != loot && Array.IndexOf(replace, enemy.lootTable.name) >= 0;
            if (enemy.lootTable == null || _refresh || placeholder)
            {
                enemy.lootTable = loot;
                EditorUtility.SetDirty(enemy);
            }
        }

        // ================================================================== Hilfen: Assets

        private static T Ensure<T>(string folder, string file, string id, string displayName, string description, Action<T> setup) where T : GameDefinition
        {
            string path = folder + "/" + file + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<T>(path);
            bool fresh = def == null;
            if (fresh)
            {
                def = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(def, path);
            }
            if (fresh || _refresh)
            {
                setup?.Invoke(def);
                Written.Add(def);
                K.SetString(def, "id", id);
                K.SetString(def, "displayName", displayName);
                if (description != null) K.SetString(def, "description", description);
                EditorUtility.SetDirty(def);
            }
            return def;
        }

        private static ProfessionDefinition Profession(string file, string id, string name, ProfessionKind kind) =>
            Ensure<ProfessionDefinition>(ProfessionFolder, "Profession_" + file, id, name, null, p => { p.kind = kind; p.maxSkill = 300; });

        private static WeaponDefinition Weapon(string file, string id, string name, Action<WeaponDefinition> setup) =>
            Ensure(PlayerRigBuilder.WeaponFolder, file, id, name, null, setup);

        private static void Melee(WeaponDefinition w, WeaponClass wc, float min, float max, float range, float speed, string special)
        {
            w.weaponClass = wc;
            w.archetype = WeaponArchetype.MeleeArc;
            w.minDamage = min;
            w.maxDamage = max;
            w.critChance = 0.06f;
            w.range = range;
            float s = speed; // 1 = Schwerttempo, groesser = langsamer
            w.combo = new[]
            {
                // 21.10. nachts (AuftragNahkampfTempo): sichtbares Ausholen (0,30/0,28/0,40/0,45 s), Erholung um genau so viel kuerzer –
                // der Zyklus (Schaden pro Sekunde) bleibt wie vorher (0,42/0,42/0,65/0,75 s mal Tempo)
                AttackStep.Create("Hieb", 1f, 0.30f * s, 0.08f, 0.12f * s, 110f, 0.035f, 0f, 0.1f),
                AttackStep.Create("Rückhand", 1f, 0.28f * s, 0.08f, 0.14f * s, 120f, 0.035f, 0f, 0.1f),
                AttackStep.Create("Schwerer Hieb", 1.5f, 0.40f * s, 0.10f, 0.25f * s, 140f, 0.07f, 0.25f, 0.4f)
            };
            w.comboResetTime = 1.2f * s;
            w.specialName = special;
            w.special = AttackStep.Create(special, 2.3f, 0.45f * s, 0.10f, 0.30f * s, 160f, 0.09f, 0.4f, 0.7f);
            w.specialCooldown = 6f;
            w.resource = WeaponResource.None;
            w.magazineSize = 0;
            w.effectColor = new Color(0.95f, 0.9f, 0.8f);
            w.animationSet = "onehand_sword";
        }

        private static void Gun(WeaponDefinition w, WeaponClass wc, WeaponArchetype arch, float min, float max, float range, float interval,
            int magazine, float reload, string special)
        {
            w.weaponClass = wc;
            w.archetype = arch;
            w.minDamage = min;
            w.maxDamage = max;
            w.critChance = 0.1f;
            w.critMultiplier = 1.8f;
            w.range = range;
            w.combo = new[] { AttackStep.Create("Schuss", 1f, 0.06f, 0.05f, interval, 10f, 0.03f, 0.15f, 0.2f) };
            w.specialName = special;
            w.special = AttackStep.Create(special, 2.4f, 0.6f, 0.05f, 0.5f, 10f, 0.06f, 0.35f, 0.6f);
            w.specialCooldown = 9f;
            w.resource = WeaponResource.Ammo;
            w.magazineSize = magazine;
            w.reloadTime = reload;
            w.effectColor = new Color(1f, 0.62f, 0.25f);
            w.animationSet = "pistol";
        }

        private const float BowScale = 0.8f;

        /// <summary>Bogen wie WoW: automatischer Schuss (0,5 s Zielen, dann Waffentempo), Pfeile, toter Winkel 7,3 m (8 Yards).</summary>
        private static void Bow(WeaponDefinition w, float min, float max, float range, float speed)
        {
            w.weaponClass = WeaponClass.Bow;
            w.archetype = WeaponArchetype.Projectile;
            // P2 Balance (30.09.): Pfeile, Beweglichkeit, Kritisch ×2 und Gezielter Schuss machten den Bogen doppelt so stark
            // wie die Referenz – Schaden ×0,8 (Balance-Test)
            w.minDamage = Mathf.Round(min * BowScale);
            w.maxDamage = Mathf.Round(max * BowScale);
            w.critChance = 0.07f;
            w.critMultiplier = 2f;
            w.range = range;
            w.minRange = 7.3f;
            w.attackSpeed = speed;
            w.aimTime = 0.5f;
            w.ammoType = AmmoType.Arrow;
            w.projectileSpeed = 42f;
            w.projectileGravity = 9.81f;
            w.combo = new[] { AttackStep.Create("Automatischer Schuss", 1f, 0.5f, 0.05f, speed - 0.55f, 10f, 0.02f, 0.05f, 0.15f) };
            w.comboResetTime = 99f;
            w.specialName = "Gezielter Schuss";
            // WoW: 3 s Wirkzeit – hier 2 s, damit es im Actionkampf spielbar bleibt
            w.special = AttackStep.Create("Gezielter Schuss", 1.6f, 2.0f, 0.05f, 0.45f, 10f, 0.06f, 0.3f, 0.5f);
            w.specialCooldown = 8f;
            w.resource = WeaponResource.None;
            w.magazineSize = 0;
            w.pellets = 1;
            w.spreadAngle = 0f;
            w.hasAirAttacks = false;
            w.effectColor = new Color(0.95f, 0.92f, 0.85f);
            w.animationSet = "bow";
        }

        /// <summary>Stab = Zauberwaffe: kein Nahkampf, jeder Angriff ist ein gewirkter Zauber mit magischem Geschoss.</summary>
        private static void Spell(WeaponDefinition w, float min, float max, float range, string special, Color color)
        {
            w.weaponClass = WeaponClass.Staff;
            w.archetype = WeaponArchetype.Spell;
            w.minDamage = min;
            w.maxDamage = max;
            w.critChance = 0.06f;
            w.critMultiplier = 1.5f;
            w.range = range;
            w.minRange = 0f;
            w.ammoType = AmmoType.None;
            w.projectileSpeed = 24f;
            w.projectileGravity = 0f;
            w.aimTime = 0f;
            // Choreografie: zwei schnelle Funken, dann eine schwere Lanze
            w.combo = new[]
            {
                AttackStep.Create("Glutfunke", 1f, 0.75f, 0.05f, 0.35f, 10f, 0.02f, 0.05f, 0.1f),
                AttackStep.Create("Glutfunke", 1f, 0.65f, 0.05f, 0.35f, 10f, 0.02f, 0.05f, 0.1f),
                AttackStep.Create("Aschenlanze", 1.8f, 1.2f, 0.05f, 0.5f, 10f, 0.05f, 0.25f, 0.4f)
            };
            w.comboResetTime = 2.5f;
            w.specialName = special;
            w.special = AttackStep.Create(special, 2.8f, 1.6f, 0.05f, 0.6f, 10f, 0.08f, 0.45f, 0.7f);
            w.specialCooldown = 8f;
            w.resource = WeaponResource.None;
            w.magazineSize = 0;
            w.hasAirAttacks = false;
            w.effectColor = color;
            w.animationSet = "staff";
        }

        private static ItemDefinition Ammo(string key, string name, ItemQuality q, int req, float dps, Color color, string description) =>
            Item(key, name, ItemCategory.Ammo, q, Mathf.Max(1, req), req, IconShape.Arrow, color, 0, description, d =>
            {
                d.ammoType = AmmoType.Arrow;
                d.ammoDps = dps;
                d.maxStack = 200;
            });

        private static ItemDefinition Container(string key, string name, ItemQuality q, int req, int slots, ContainerKind kind, float haste,
            IconShape shape, Color color, int sell, string description) =>
            Item(key, name, ItemCategory.Container, q, Mathf.Max(1, req), req, shape, color, sell, description, d =>
            {
                d.equipType = EquipType.Bag;
                d.containerSlots = slots;
                d.containerKind = kind;
                d.rangedHaste = haste;
            });

        private static CharacterClassDefinition Class(string file, string id, string name, string description, ClassRole roles, Color color,
            ArmorType armor, StatType primary, bool dualWield, params WeaponClass[] weapons) =>
            Ensure<CharacterClassDefinition>(ClassFolder, file, id, name, description, c =>
            {
                c.roles = roles;
                c.classColor = color;
                c.maxArmor = armor;
                c.primaryStat = primary;
                c.dualWield = dualWield;
                c.canRoll = id == "class.ashstalker"; // Kampfumbau (Max 30.09.): Ausweichrolle ist Faehigkeit des Aschenjaegers
                c.weapons = new List<WeaponClass>(weapons);
            });

        private static void SetStart(CharacterClassDefinition c, ItemDefinition[] equip, params (ItemDefinition item, int count)[] bag)
        {
            if (!_refresh && c.startEquipment.Count > 0) return;
            c.startEquipment = new List<ItemDefinition>(equip);
            c.startBag = new List<ItemAmount>();
            foreach ((ItemDefinition item, int count) in bag) c.startBag.Add(new ItemAmount { item = item, count = count });
            EditorUtility.SetDirty(c);
        }

        /// <summary>21.10.2026: Startteil nachtragen, wenn die Klasse fuer diesen Platz noch nichts hat (idempotent).</summary>
        private static void StartNachtrag(CharacterClassDefinition c, ItemDefinition item)
        {
            if (c == null || item == null) return;
            foreach (ItemDefinition d in c.startEquipment)
                if (d != null && d.equipType == item.equipType) return;
            c.startEquipment.Add(item);
            EditorUtility.SetDirty(c);
        }

        private static ItemDefinition Item(string key, string name, ItemCategory cat, ItemQuality q, int ilvl, int req, IconShape shape, Color color,
            int sell, string description, Action<ItemDefinition> extra = null)
        {
            ItemDefinition d = Ensure<ItemDefinition>(ItemFolder, "Item_" + key, "item." + key, name, description, x =>
            {
                x.category = cat;
                x.quality = q;
                x.itemLevel = ilvl;
                x.requiredLevel = req;
                x.iconShape = shape;
                x.iconColor = color;
                x.sellPrice = sell;
                x.maxStack = 1;
                x.bind = BindType.None;
                x.unique = false;
                x.equipType = EquipType.None;
                x.armorType = ArmorType.None;
                x.armor = 0;
                x.weapon = null;
                x.hand = WeaponHand.Auto;
                x.allowedClasses = new List<CharacterClassDefinition>();
                x.stats = new List<StatModifier>();
                x.effects = new List<ItemEffect>();
                x.teachesRecipe = null;
                x.gatheredBy = null;
                x.questId = "";
                x.useCooldown = 0f;
                x.containerSlots = 0;
                x.containerKind = ContainerKind.General;
                x.rangedHaste = 0f;
                x.ammoType = AmmoType.None;
                x.ammoDps = 0f;
                extra?.Invoke(x);
            });
            Items[key] = d;
            return d;
        }

        private static ItemDefinition WeaponItem(string key, string name, WeaponDefinition w, ItemQuality q, int ilvl, int req, IconShape shape, Color color,
            int sell, string description, BindType bind = BindType.None, params (StatType, int)[] stats) =>
            Item(key, name, ItemCategory.Weapon, q, ilvl, req, shape, color, sell, description, d =>
            {
                d.equipType = EquipType.Weapon;
                d.weapon = w;
                d.bind = bind;
                d.stats = Stats(stats);
            });

        private static ItemDefinition Armor(string key, string name, ItemQuality q, int ilvl, int req, EquipType slot, ArmorType type, int armor,
            IconShape shape, Color color, int sell, string description, params (StatType, int)[] stats) =>
            Item(key, name, ItemCategory.Armor, q, ilvl, req, shape, color, sell, description, d =>
            {
                d.equipType = slot;
                d.armorType = type;
                d.armor = armor;
                d.stats = Stats(stats);
                d.bind = q >= ItemQuality.Uncommon ? BindType.OnEquip : BindType.None;
            });

        private static ItemDefinition Material(string key, string name, ItemQuality q, int ilvl, IconShape shape, Color color, int sell,
            ProfessionDefinition gatheredBy, string description) =>
            Item(key, name, ItemCategory.Material, q, ilvl, 1, shape, color, sell, description, d =>
            {
                d.maxStack = 20;
                d.gatheredBy = gatheredBy;
            });

        private static void Junk(string key, string name, IconShape shape, Color color, int sell, string description) =>
            Item(key, name, ItemCategory.Junk, ItemQuality.Common, 1, 1, shape, color, sell, description, d => d.maxStack = 10);

        private static void RecipeItem(string key, string name, RecipeDefinition r, ItemQuality q, int sell) =>
            Item(key, name, ItemCategory.Recipe, q, r.output != null ? r.output.itemLevel : 1, 1, IconShape.Scroll, new Color(0.84f, 0.76f, 0.58f), sell,
                "", d => d.teachesRecipe = r);

        private static RecipeDefinition Recipe(string file, string id, string name, ProfessionDefinition prof, int skill, ItemDefinition output, int outCount,
            params (ItemDefinition item, int count)[] reagents) =>
            Ensure<RecipeDefinition>(RecipeFolder, file, id, name, null, r =>
            {
                r.profession = prof;
                r.requiredSkill = skill;
                r.output = output;
                r.outputCount = outCount;
                r.reagents = new List<ItemAmount>();
                foreach ((ItemDefinition item, int count) in reagents) r.reagents.Add(new ItemAmount { item = item, count = count });
            });

        private static List<StatModifier> Stats(params (StatType stat, int value)[] stats)
        {
            var l = new List<StatModifier>();
            foreach ((StatType stat, int value) in stats) l.Add(new StatModifier { stat = stat, value = value });
            return l;
        }

        private static void LootTable(string file, string id, string name, Action<LootTableDefinition> setup) =>
            Ensure(LootFolder, file, id, name, null, setup);

        private static List<LootTableDefinition.Entry> Entries(params (string key, float chance, int min, int max)[] e)
        {
            var l = new List<LootTableDefinition.Entry>();
            foreach ((string key, float chance, int min, int max) in e)
                if (Items.TryGetValue(key, out ItemDefinition d))
                    l.Add(new LootTableDefinition.Entry { item = d, chance = chance, minCount = min, maxCount = max });
                else Debug.LogWarning("[Ashveil] Beutetabelle: unbekanntes Item " + key);
            return l;
        }

        private static List<LootTableDefinition.Weighted> Weighted(string[] keys, float weight)
        {
            var l = new List<LootTableDefinition.Weighted>();
            foreach (string key in keys)
                if (Items.TryGetValue(key, out ItemDefinition d))
                    l.Add(new LootTableDefinition.Weighted { item = d, weight = weight, minCount = 1, maxCount = 1 });
                else Debug.LogWarning("[Ashveil] Beutetabelle: unbekanntes Item " + key);
            return l;
        }

        private static LootTableDefinition.Group Group(string name, float chance, bool classAware, params List<LootTableDefinition.Weighted>[] parts)
        {
            var g = new LootTableDefinition.Group { name = name, chance = chance, picks = 1, classAware = classAware, classBias = 0.85f };
            foreach (List<LootTableDefinition.Weighted> p in parts) g.items.AddRange(p);
            return g;
        }
    }

    internal static class ItemContentExtensions
    {
        /// <summary>Nachtrag zu einem Item – nur, wenn es in diesem Durchlauf neu beschrieben wurde (sonst bleiben Inspector-Aenderungen).</summary>
        public static ItemDefinition Post(this ItemDefinition d, Action<ItemDefinition> change)
        {
            if (d != null && ItemContentBuilder.Written.Contains(d))
            {
                change(d);
                EditorUtility.SetDirty(d);
            }
            return d;
        }
    }
}
