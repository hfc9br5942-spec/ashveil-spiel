using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;
using KC = Ashveil.Data.KarethCatalog;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// #28 Kareth v4, Welle 0 (Auftragsliste, Max 05.10.; Bauplan #28 v2, Max 07.10. abends): Gegenstände der elf
    /// Ergänzungsaufträge und Beute der neuen Greybox-Tiere. Namen, Werte und Texte sind Vorschläge (Claude).
    ///   · Belohnungen nach Klasse: Gürtel (K-A), Brustteil (U-1), Beinteil (U-6) – je Klasse genau ein Teil (allowedClasses),
    ///     Waffen zur Wahl (K-D Stufe 5, U-4 Stufe 8; Schaden aus <see cref="WeaponBudget"/> wie in der Glockenrast),
    ///     Halsschmuck für alle (U-5), ein kleiner Schmuck von Edo (U-7). Füllt die Ausrüstungslücken 1–5 und 5–9 (PROG-009).
    ///   · Auftragsgegenstände: Ebbas Krug (U-2), Logbuchseite (U-3), Pilgergaben (U-7) – fallen nur, solange der Auftrag läuft.
    ///   · Tierbeute (Bausteine 28/29/32): Borsten, Haut, Grabkralle · Greifschere, Scherenplatte · Balgfalte, Greifkralle,
    ///     Kehlhaut. Handelsware, kein Fleisch ohne Verwendung; Tiere lassen keine Münzen und Waffen fallen (wie M5.4).
    /// Neue Assets bekommen ihre Werte beim ersten Anlegen (oder bei neuer DefaultsVersion wie alle Items).
    /// </summary>
    public static partial class ItemContentBuilder
    {
        private static readonly Color Bristles = new Color(0.2f, 0.16f, 0.12f);
        private static readonly Color Shell = new Color(0.36f, 0.37f, 0.33f);
        private static readonly Color ShellRim = new Color(0.62f, 0.56f, 0.44f);
        private static readonly Color Bellows = new Color(0.45f, 0.42f, 0.35f);
        private static readonly Color Throat = new Color(0.72f, 0.57f, 0.47f);
        private static readonly Color Canvas = new Color(0.66f, 0.6f, 0.48f);
        private static readonly Color Loden = new Color(0.33f, 0.36f, 0.27f);

        private static void BuildKarethItems(CharacterClassDefinition warden, CharacterClassDefinition stalker, CharacterClassDefinition weaver,
            ProfessionDefinition skinning)
        {
            // ---------------------------------------------------------- Auftragsgegenstände
            QuestItem("k28_ebbas_jug", "Ebbas Krug", IconShape.Potion, new Color(0.55f, 0.36f, 0.24f), KC.U2, 1,
                "Steingut, braun glasiert, am Henkel ein Sprung, den jemand mit Draht geflickt hat. Riecht nach Bier und Schlick.");
            QuestItem("k28_wreck_log_page", "Seite aus dem Logbuch", IconShape.Letter, new Color(0.66f, 0.63f, 0.55f), KC.U3, 1,
                "Die Tinte ist zerlaufen. Lesbar sind ein Datum, ein Kurs und das Wort „Salz“, dreimal unterstrichen.");
            QuestItem("k28_pilgrim_gifts", "Pilgergaben", IconShape.Cloth, Linen, KC.U7, 1,
                "Ein Bündel in einem Tuch: drei Brotkanten, ein Holzlöffel, ein Band mit einem Knoten darin. Nichts davon ist viel wert. Alles davon fehlt jemandem.");
            Item("k28_front_letter", "Brief, nie abgeschickt", ItemCategory.Junk, ItemQuality.Common, 7, 1, IconShape.Letter, Linen, 1,
                "„…an der Front gibt es kein Brot mehr, und die Höfe im Süden stehen leer. Die anderen sagen, dort ist Platz für uns.“ Unterschrieben ist er nicht.",
                d => { d.maxStack = 1; d.unique = true; });

            // ---------------------------------------------------------- Tierbeute (Handelsware)
            Material("k28_coarse_bristles", "Grobe Borsten", ItemQuality.Common, 1, IconShape.Hide, Bristles, 2, skinning,
                "Steif und dunkel. Bürstenbinder zahlen ein paar Kupfer.");
            Material("k28_rooter_hide", "Furchenwühlerhaut", ItemQuality.Common, 2, IconShape.Hide, new Color(0.45f, 0.34f, 0.24f), 4, skinning,
                "Dick am Nacken, dünn am Bauch.");
            Material("k28_digging_claw", "Gut erhaltene Grabkralle", ItemQuality.Uncommon, 3, IconShape.Bone, new Color(0.2f, 0.18f, 0.15f), 12, null,
                "Breit wie eine kleine Schaufel. Die Spitze ist kaum abgenutzt.");
            Material("k28_damaged_claw", "Beschädigte Greifschere", ItemQuality.Common, 3, IconShape.Bone, Shell, 2, null,
                "Das Gelenk ist gebrochen. Die Greifkante ist noch scharf.");
            Material("k28_ribbed_claw_plate", "Gerippte Scherenplatte", ItemQuality.Uncommon, 5, IconShape.Hide, ShellRim, 14, skinning,
                "Zwei davon erzeugen ein erstaunlich lautes Klappern.");
            Material("k28_tough_fold", "Zähe Balgfalte", ItemQuality.Common, 4, IconShape.Hide, Bellows, 4, skinning,
                "Dünner, als sie aussieht, und schwer zu zerreißen.");
            Material("k28_curved_claw", "Gekrümmte Greifkralle", ItemQuality.Common, 4, IconShape.Bone, new Color(0.24f, 0.22f, 0.19f), 3, null,
                "Am inneren Rand glatt vom Klettern.");
            Material("k28_throat_skin", "Unbeschädigte Kehlhaut", ItemQuality.Uncommon, 5, IconShape.Hide, Throat, 15, skinning,
                "Die feinen Falten ziehen sich beim Loslassen wieder zusammen.");

            // ---------------------------------------------------------- K-A „Die Vorratsgruben“ (Wenna, Stufe 1): Gürtel nach Klasse
            Armor("k28_studded_girdle", "Beschlagener Gurt", ItemQuality.Common, 3, 1, EquipType.Belt, ArmorType.Plate, 7, IconShape.Belt, Iron, 16,
                "Ein Pferdegurt mit Eisennieten. Das Pferd braucht ihn nicht mehr.", (StatType.Strength, 1), (StatType.Stamina, 1))
                .Post(d => d.allowedClasses = new List<CharacterClassDefinition> { warden });
            Armor("k28_knife_loop_belt", "Gurt mit Messerschlaufe", ItemQuality.Common, 3, 1, EquipType.Belt, ArmorType.Leather, 4, IconShape.Belt, DarkLeather, 16,
                "Die Schlaufe ist für ein Küchenmesser gedacht. Ein Dolch passt auch.", (StatType.Agility, 1), (StatType.Stamina, 1))
                .Post(d => d.allowedClasses = new List<CharacterClassDefinition> { stalker });
            Armor("k28_braided_cord", "Geflochtene Kordel", ItemQuality.Common, 3, 1, EquipType.Belt, ArmorType.Cloth, 2, IconShape.Belt, Linen, 16,
                "Aus Küchengarn geflochten. Wenna sagt, es hält. Wenna hat meistens recht.", (StatType.Intellect, 1), (StatType.Willpower, 1))
                .Post(d => d.allowedClasses = new List<CharacterClassDefinition> { weaver });

            // ---------------------------------------------------------- K-D „Die ausgebliebene Holzlieferung“ (Ulf, Stufe 4): Waffe zur Wahl
            KarethWeapons(5, 4, "yard", "Zimmermannsklinge", "Spaltmesser", "Messlattenstab", "Pfahlbogen",
                "Geschmiedet aus einem Sägeblatt, das zu oft gebrochen ist. Jetzt bricht es nicht mehr.",
                "Zum Spalten von Schindeln. Ulf hat es nachgeschliffen und behauptet, es sei jetzt eine Waffe.",
                "Mit Kerben im Abstand einer Elle. Ulf misst damit noch heute, wenn du nicht hinsiehst.",
                "Aus einem Palisadenpfahl, der zu krumm für die Palisade war.",
                (2, 1));

            // ---------------------------------------------------------- U-1 „Tamos Wagen“ (Tamo, Stufe 5): Brustteil nach Klasse
            Armor("k28_wagon_plate_cuirass", "Harnisch aus Wagenblech", ItemQuality.Uncommon, 6, 5, EquipType.Chest, ArmorType.Plate, 46, IconShape.Chest, Steel, 180,
                "Tamo hat die Beschläge vom Wagenkasten abgeschlagen und zurechtgedengelt. Das Rad ist trotzdem falsch.",
                (StatType.Strength, 3), (StatType.Stamina, 3))
                .Post(d => d.allowedClasses = new List<CharacterClassDefinition> { warden });
            Armor("k28_tarp_leather_jerkin", "Wams aus Planenleder", ItemQuality.Uncommon, 6, 5, EquipType.Chest, ArmorType.Leather, 21, IconShape.Chest, Leather, 180,
                "Gewachstes Leder von der Wagenplane. Regen perlt ab, Spott auch.", (StatType.Agility, 3), (StatType.Stamina, 3))
                .Post(d => d.allowedClasses = new List<CharacterClassDefinition> { stalker });
            Armor("k28_wagon_canvas_smock", "Kittel aus Wagenplane", ItemQuality.Uncommon, 6, 5, EquipType.Chest, ArmorType.Cloth, 10, IconShape.Chest, Canvas, 180,
                "Auf dem Rücken steht noch „TAMO – UNTERSTEG“, halb ausgewaschen.", (StatType.Intellect, 3), (StatType.Willpower, 3))
                .Post(d => d.allowedClasses = new List<CharacterClassDefinition> { weaver });

            // ---------------------------------------------------------- U-4 „Arbeitszeichen“ (Veit, Stufe 7): Waffe zur Wahl
            KarethWeapons(8, 7, "workshop", "Werkstattklinge", "Kerbmesser", "Zeichenstab", "Werkstattbogen",
                "Aus den Werkstätten am Aschenfeld. In die Klinge ist ein Zeichen geschlagen, das niemand mehr liest.",
                "Für Kerben in Holz und Stein. Die Verblichenen haben damit weitergearbeitet, als niemand mehr da war.",
                "Ein Messstab aus den Werkstätten, an einem Ende mit Asche geschwärzt.",
                "Gespannt mit einer Sehne aus der Werkstatt. Sie war für etwas anderes gedacht.",
                (3, 2));

            // ---------------------------------------------------------- U-5 „Die Zeichensteine“ (Veit, Stufe 8): Halsschmuck für alle
            Armor("k28_sign_stone_pendant", "Anhänger aus Zeichenstein", ItemQuality.Uncommon, 9, 8, EquipType.Neck, ArmorType.None, 0, IconShape.Amulet, Ash, 120,
                "Ein Splitter vom fünften Stein. Veit hat ein Loch hineingebohrt. Das Zeichen hat er nicht angerührt.",
                (StatType.Stamina, 3), (StatType.Willpower, 2));

            // ---------------------------------------------------------- U-6 „Die leeren Höfe“ (Bralt, Stufe 9): Beinteil nach Klasse
            Armor("k28_watch_greaves", "Beinschienen der Wacht", ItemQuality.Uncommon, 10, 9, EquipType.Legs, ArmorType.Plate, 46, IconShape.Legs, Steel, 230,
                "Aus der Kammer der Wacht. Der Vorbesitzer ist nicht zurückgekommen, um sie abzuholen.", (StatType.Strength, 3), (StatType.Stamina, 4))
                .Post(d => d.allowedClasses = new List<CharacterClassDefinition> { warden });
            Armor("k28_scout_breeches", "Spähersbeinkleid", ItemQuality.Uncommon, 10, 9, EquipType.Legs, ArmorType.Leather, 17, IconShape.Legs, DarkLeather, 230,
                "Die Knie sind doppelt genäht. Späher knien viel.", (StatType.Agility, 4), (StatType.Stamina, 3))
                .Post(d => d.allowedClasses = new List<CharacterClassDefinition> { stalker });
            Armor("k28_loden_trousers", "Feldhose aus Loden", ItemQuality.Uncommon, 10, 9, EquipType.Legs, ArmorType.Cloth, 9, IconShape.Legs, Loden, 230,
                "Warm, kratzig und nahezu unverwüstlich. Wie die Wacht.", (StatType.Intellect, 4), (StatType.Willpower, 3))
                .Post(d => d.allowedClasses = new List<CharacterClassDefinition> { weaver });

            // ---------------------------------------------------------- U-7 „Die verschwundenen Gaben“ (Edo, Stufe 7): kleiner Gegenstand
            Armor("k28_resting_pebble", "Kiesel vom Rastplatz", ItemQuality.Uncommon, 8, 6, EquipType.Trinket, ArmorType.None, 0, IconShape.Gem,
                new Color(0.6f, 0.58f, 0.52f), 90,
                "Glatt von vielen Händen. Pilger legen ihn auf die Bank, bevor sie weitergehen. Edo meint, du gehst noch weiter.",
                (StatType.Stamina, 3), (StatType.Willpower, 1));

            // Classic-Paket (Max 11.10.2026 abends): Auftragsgegenstände der „Kopf von …“-Aufträge
            BuildKarethRareItems();
        }

        /// <summary>Waffen zur Wahl (Schwert, Dolch, Stab, Bogen) wie <see cref="GlockenrastWeapons"/>, Schlüssel k28_&lt;tag&gt;_*.</summary>
        private static void KarethWeapons(int ilvl, int req, string tag, string swordName, string daggerName, string staffName, string bowName,
            string swordText, string daggerText, string staffText, string bowText, (int primary, int stamina) stats)
        {
            const ItemQuality q = ItemQuality.Uncommon;
            Vector2 sw = WeaponBudget(ilvl, q, SwordFactor), dg = WeaponBudget(ilvl, q, DaggerFactor),
                st = WeaponBudget(ilvl, q, StaffFactor), bw = WeaponBudget(ilvl, q, BowFactor);
            int sell = 40 + 18 * ilvl;
            WeaponDefinition wSword = Weapon("Weapon_K28_" + tag + "_sword", $"weapon.k28_{tag}_sword", swordName,
                w => { Melee(w, WeaponClass.Sword, sw.x, sw.y, 2.4f, 1f, "Kerbschlag"); w.quality = q; w.itemLevel = ilvl; });
            WeaponDefinition wDagger = Weapon("Weapon_K28_" + tag + "_dagger", $"weapon.k28_{tag}_dagger", daggerName,
                w => { Melee(w, WeaponClass.Dagger, dg.x, dg.y, 2f, 0.7f, "Spaltstich"); w.quality = q; w.itemLevel = ilvl; });
            WeaponDefinition wStaff = Weapon("Weapon_K28_" + tag + "_staff", $"weapon.k28_{tag}_staff", staffName,
                w => { Spell(w, st.x, st.y, 30f, "Glutfunke", Ember); w.quality = q; w.itemLevel = ilvl; });
            WeaponDefinition wBow = Weapon("Weapon_K28_" + tag + "_bow", $"weapon.k28_{tag}_bow", bowName,
                w => { Bow(w, bw.x, bw.y, 32f, 2.8f); w.quality = q; w.itemLevel = ilvl; });
            WeaponItem($"k28_{tag}_sword", swordName, wSword, q, ilvl, req, IconShape.Sword, Iron, sell, swordText,
                BindType.OnPickup, (StatType.Strength, stats.primary), (StatType.Stamina, stats.stamina));
            WeaponItem($"k28_{tag}_dagger", daggerName, wDagger, q, ilvl, req, IconShape.Dagger, DarkLeather, sell, daggerText,
                BindType.OnPickup, (StatType.Agility, stats.primary), (StatType.Stamina, stats.stamina));
            WeaponItem($"k28_{tag}_staff", staffName, wStaff, q, ilvl, req, IconShape.Staff, tag == "yard" ? Leather : Ash, sell, staffText,
                BindType.OnPickup, (StatType.Intellect, stats.primary), (StatType.Willpower, stats.stamina));
            WeaponItem($"k28_{tag}_bow", bowName, wBow, q, ilvl, req, IconShape.Bow, Leather, sell, bowText,
                BindType.OnPickup, (StatType.Agility, stats.primary), (StatType.Stamina, stats.stamina));
        }

        /// <summary>Beutetabellen der #28-Gegner (Aufruf in <see cref="Build"/> vor dem Versionsstempel).</summary>
        private static void BuildKarethLoot()
        {
            // Tiere: nur Rüstung, Material – keine Münzen, keine Waffen (wie M5.4)
            string[] animalArmor = { "dented_helm", "chainmail_shirt", "stalker_gloves", "rope_belt", "grey_wool_cloak", "tarnished_ring", "weaver_sandals",
                                     "travel_hood", "travel_mantle", "travel_wraps", "travel_trousers", "iron_sabatons" };
            string[] betterArmor = { "warden_pauldrons", "iron_gauntlets", "stalker_hood", "ashen_leather_jerkin", "stalker_boots", "weaver_robe", "ember_sash",
                                     "wolfhide_cloak", "bone_amulet", "hunter_shoulderguards", "hunter_breeches", "warden_cuirass" };
            string[] humanGear = { "dented_helm", "chainmail_shirt", "stalker_gloves", "rope_belt", "grey_wool_cloak", "tarnished_ring", "weaver_sandals",
                                   "cinder_dagger", "rusty_longsword", "travel_hood", "travel_mantle", "travel_wraps", "travel_trousers", "iron_sabatons" };
            string[] humanGood = { "warden_pauldrons", "iron_gauntlets", "stalker_hood", "ashen_leather_jerkin", "stalker_boots", "weaver_robe", "ember_sash",
                                   "wolfhide_cloak", "bone_amulet", "notched_greataxe", "warden_blade", "ember_staff", "cracked_focus", "ashwood_longbow",
                                   "hunter_shoulderguards", "hunter_breeches", "warden_cuirass", "oak_shield" };

            LootTable("Loot_Furchenwuehler", "loot.furrow_rooter", "Furchenwühler", t =>
            {
                t.entries = Entries(("k28_coarse_bristles", 0.8f, 1, 2), ("k28_rooter_hide", 0.5f, 1, 1), ("k28_digging_claw", 0.1f, 1, 1),
                    ("torn_leather", 0.2f, 1, 1));
                t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.08f, true, Weighted(animalArmor, 1f)) };
            });
            LootTable("Loot_Uferklapper", "loot.shore_clacker", "Uferklapper", t =>
            {
                t.entries = Entries(("k28_damaged_claw", 0.7f, 1, 1), ("k28_ribbed_claw_plate", 0.15f, 1, 1));
                t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.1f, true, Weighted(animalArmor, 3f), Weighted(betterArmor, 1f)) };
            });
            LootTable("Loot_Schlickwerfer", "loot.silt_slinger", "Schlickwerfer", t =>
            {
                t.entries = Entries(("k28_damaged_claw", 0.6f, 1, 2), ("k28_ribbed_claw_plate", 0.3f, 1, 1), ("k28_ebbas_jug", 0.5f, 1, 1));
                t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.14f, true, Weighted(animalArmor, 2f), Weighted(betterArmor, 3f)) };
            });
            LootTable("Loot_Fluesterbalg", "loot.whisper_bellows", "Flüsterbalg", t =>
            {
                t.entries = Entries(("k28_tough_fold", 0.7f, 1, 2), ("k28_curved_claw", 0.45f, 1, 1), ("k28_throat_skin", 0.1f, 1, 1));
                t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.1f, true, Weighted(animalArmor, 3f), Weighted(betterArmor, 2f)) };
            });
            LootTable("Loot_ErtrunkenerMatroseWrack", "loot.wreck_sailor", "Ertrunkener Matrose (Wrack)", t =>
            {
                t.entries = Entries(("k28_wreck_log_page", 0.35f, 1, 1), ("faded_coin", 0.5f, 1, 2), ("tarnished_buttons", 0.3f, 1, 1),
                    ("linen_scraps", 0.4f, 1, 2), ("healing_draught", 0.1f, 1, 1));
                t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.18f, true, Weighted(humanGear, 4f), Weighted(humanGood, 5f)) };
            });
            LootTable("Loot_HungrigerDeserteur", "loot.reed_deserter", "Hungriger Deserteur", t =>
            {
                t.entries = Entries(("k28_pilgrim_gifts", 1f, 1, 1), ("k28_front_letter", 1f, 1, 1), ("dry_bread", 0.6f, 1, 2), ("faded_coin", 0.4f, 1, 1));
                t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.25f, true, Weighted(humanGood, 1f)) };
            });

            // Classic-Paket (Max 11.10.2026 abends): Beute der sechs seltenen Tiere
            BuildKarethRareLoot();
        }

        /// <summary>#28: Beutetabellen an die neuen Gegner hängen (nach <see cref="EnemyBuilder.BuildKarethCreatures"/>).</summary>
        public static void AssignKarethLoot()
        {
            Assign(EnemyBuilder.FileRooter, "Loot_Furchenwuehler");
            Assign(EnemyBuilder.FileClacker, "Loot_Uferklapper");
            Assign(EnemyBuilder.FileSlinger, "Loot_Schlickwerfer");
            Assign(EnemyBuilder.FileBellows, "Loot_Fluesterbalg");
            Assign(EnemyBuilder.FileWreckSailor, "Loot_ErtrunkenerMatroseWrack");
            Assign(EnemyBuilder.FileReedDeserter, "Loot_HungrigerDeserteur");
            AssetDatabase.SaveAssets();
        }
    }
}
