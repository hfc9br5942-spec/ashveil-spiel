using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// P3 Glockenrast (Stufe 5–9): Auftragsgegenstaende und Belohnungen.
    ///   · 6 Auftragsgegenstaende: Giftdruese (nur als Auftragsbeute der Nadelspeier), Ersatzrad, Jaskos Brief, Kassenbuch,
    ///     Abrieb, Glockenscherbe
    ///   · Waffen zur Wahl Stufe 6 (G5) und Stufe 9 (G13): Einhandschwert, Dolch, Stab, Bogen – Schaden aus dem Waffenbudget
    ///     <see cref="Balance.WeaponAverage"/> (Faktoren je Waffenart kalibriert an den vorhandenen Waffen, siehe WeaponBudget)
    ///   · Ring (G7), Umhang (G8), vier Ruestungsteile zur Wahl (G9), Anhaenger (Schmuck-Wahl in G13)
    /// Neue Assets bekommen ihre Standardwerte beim ersten Anlegen (oder bei neuer DefaultsVersion wie alle Items).
    /// </summary>
    public static partial class ItemContentBuilder
    {
        private static readonly Color Bronze = new Color(0.62f, 0.45f, 0.24f);
        private static readonly Color Peat = new Color(0.3f, 0.23f, 0.16f);
        private static readonly Color Resin = new Color(0.85f, 0.55f, 0.18f);
        private static readonly Color Venom = new Color(0.5f, 0.8f, 0.3f);

        /// <summary>
        /// Mittlerer Schaden je Waffenart relativ zu <see cref="Balance.WeaponAverage"/> (30.09., aus den vorhandenen Waffen):
        /// Schwert (Tempo 1,0) ×1,0 · Dolch (Tempo 0,7) ×0,72 · Stab (Zauber) ×1,95 · Bogen roh ×4,25 (Bow() rechnet danach ×0,8).
        /// Spanne ±15 % um den Mittelwert.
        /// </summary>
        private static Vector2 WeaponBudget(int itemLevel, ItemQuality q, float factor)
        {
            float avg = Balance.WeaponAverage(itemLevel, q) * factor;
            return new Vector2(Mathf.Round(avg * 0.85f), Mathf.Round(avg * 1.15f));
        }

        private const float SwordFactor = 1f, DaggerFactor = 0.72f, StaffFactor = 1.95f, BowFactor = 4.25f;

        private static void BuildGlockenrastItems()
        {
            // ---------------------------------------------------------- Auftragsgegenstaende
            QuestItem("gr_venom_gland", "Pralle Giftdrüse", IconShape.Herb, Venom, GlockenrastCatalog.G3, 6,
                "Aus dem Hals eines Nadelspeiers. Nicht an der Naht anfassen.");
            QuestItem("gr_spare_wheel", "Ersatzrad", IconShape.Shield, new Color(0.45f, 0.32f, 0.2f), GlockenrastCatalog.G4, 1,
                "Ein Wagenrad. Rund, aus Holz, ungefähr passend. Eher mehr als ungefähr.");
            QuestItem("gr_jasko_letter", "Jaskos Brief", IconShape.Letter, Linen, GlockenrastCatalog.G6, 1,
                "Mit Harz versiegelt. Auf der Rückseite nur ein „M.“ – und darunter, durchgestrichen, „zwei Plätze auf dem Wagen“.");
            QuestItem("gr_resin_ledger", "Kassenbuch des Harzerlagers", IconShape.Scroll, new Color(0.55f, 0.42f, 0.3f), GlockenrastCatalog.G6, 1,
                "Die letzte Zahlung ist groß und mit einem kleinen Zeichen quittiert: einer Glocke.");
            QuestItem("gr_rubbing", "Abrieb eines alten Zeichens", IconShape.Scroll, new Color(0.3f, 0.3f, 0.32f), GlockenrastCatalog.G7, GlockenrastCatalog.RubbingCount,
                "Kohle auf Papier. Das Zeichen sieht aus wie eine Hand, die etwas hält. Oder loslässt.");
            QuestItem("gr_bell_fragment", "Glockenscherbe", IconShape.Gem, Bronze, GlockenrastCatalog.G8, GlockenrastCatalog.FragmentCount,
                "Bronze aus dem Moor. Wenn man sie anschlägt, klingt sie nicht. Sie summt.");

            // ---------------------------------------------------------- Waffen zur Wahl, Stufe 6 (G5 „Das leere Lager“)
            GlockenrastWeapons(7, 6, "camp", "Lagerwächterklinge", "Rindenmesser", "Kiefernstab", "Harzbogen",
                "Hing neben der Tür des Holzfällerlagers. Für Wölfe gedacht, nicht für Verblichene.",
                "Zum Rindenschälen geschliffen. Es schält auch anderes.",
                "Ein gerader Kiefernast, mit Harz versiegelt. Im Harz glimmt etwas.",
                "Die Sehne ist mit Harz eingerieben. Sie klebt ein wenig an den Fingern.",
                (3, 2));
            // ---------------------------------------------------------- Waffen zur Wahl, Stufe 9 (G13 „Kommst du heute Nacht nach Hause?“)
            GlockenrastWeapons(10, 9, "moor", "Moorbronze-Klinge", "Moorbronze-Dolch", "Torfeichenstab", "Nordmoorbogen",
                "Geschmiedet aus Bronze, die niemand mehr abholt.",
                "Die Schneide ist dunkel angelaufen, als hätte sie zu lange im Moor gelegen.",
                "Torfeiche ist so alt, dass sie nicht mehr brennt. Sie glimmt nur.",
                "Die Wurfarme sind mit Glockenbronze beschlagen. Er schießt leise.",
                (4, 3));

            // ---------------------------------------------------------- Schmuck und Umhang
            Armor("gr_settler_ring", "Schlichter Siedlerring", ItemQuality.Uncommon, 7, 6, EquipType.Ring, ArmorType.None, 0, IconShape.Ring, new Color(0.6f, 0.58f, 0.52f), 70,
                "Innen ein Zeichen, das auf keinem Stein der Schädeldecke fehlt.", (StatType.Stamina, 2), (StatType.Willpower, 2));
            Armor("gr_founder_cloak", "Brandfleckiger Gießermantel", ItemQuality.Uncommon, 8, 7, EquipType.Cloak, ArmorType.None, 10, IconShape.Cloak, new Color(0.34f, 0.27f, 0.2f), 90,
                "Aus einer Gießerei, die keine Glocken mehr gießt. Die Brandflecken sind echt.", (StatType.Stamina, 3), (StatType.Strength, 1));
            Armor("gr_resin_pendant", "Harztropfen-Anhänger", ItemQuality.Uncommon, 10, 9, EquipType.Neck, ArmorType.None, 0, IconShape.Amulet, Resin, 110,
                "Ein Tropfen Kiefernharz, darin eingeschlossen eine Mücke. Sie hat es auch nicht nach Hause geschafft.", (StatType.Stamina, 3), (StatType.Agility, 2), (StatType.Intellect, 2));

            // ---------------------------------------------------------- Ruestungsteil zur Wahl (G9 „Torf und Zorn“)
            Armor("gr_moorwarden_gauntlets", "Panzerhandschuhe der Moorwache", ItemQuality.Uncommon, 9, 7, EquipType.Hands, ArmorType.Plate, 26, IconShape.Gloves, Peat, 110,
                "Innen mit Torfmoos gepolstert. Riecht danach.", (StatType.Strength, 3), (StatType.Stamina, 3));
            Armor("gr_moorwarden_boots", "Stiefel der Moorwache", ItemQuality.Uncommon, 9, 7, EquipType.Feet, ArmorType.Leather, 12, IconShape.Boots, DarkLeather, 105,
                "Nicht die von Runas Vater. Die anderen.", (StatType.Agility, 3), (StatType.Stamina, 3));
            Armor("gr_peatweaver_robe", "Robe der Torfweberin", ItemQuality.Uncommon, 9, 7, EquipType.Chest, ArmorType.Cloth, 12, IconShape.Chest, new Color(0.36f, 0.3f, 0.36f), 110,
                "Der Saum ist grün vom Moor, der Kragen grau vom Rauch.", (StatType.Intellect, 4), (StatType.Willpower, 2));
            Armor("gr_moor_coif", "Moorhaube", ItemQuality.Uncommon, 9, 7, EquipType.Head, ArmorType.Mail, 22, IconShape.Helmet, Iron, 100,
                "Kettengeflecht unter einer Filzkappe. Rostet trotzdem.", (StatType.Stamina, 3), (StatType.Strength, 2));
        }

        /// <summary>Auftragsbeute: nur fuer G3 und nur solange Giftdruesen fehlen (Filter NetworkProgression.ServerWantsLoot).</summary>
        private static void AddGlockenrastLoot()
        {
            EnsureLootEntry("Loot_Nadelspeier", "gr_venom_gland", 0.6f);
        }

        private static ItemDefinition QuestItem(string key, string name, IconShape shape, Color color, string questId, int stack, string description) =>
            Item(key, name, ItemCategory.Quest, ItemQuality.Common, 5, 1, shape, color, 0, description, d =>
            {
                d.bind = BindType.OnPickup;
                d.maxStack = Mathf.Max(1, stack);
                d.unique = stack <= 1;
                d.questId = questId;
            });

        private static void GlockenrastWeapons(int ilvl, int req, string tag, string swordName, string daggerName, string staffName, string bowName,
            string swordText, string daggerText, string staffText, string bowText, (int primary, int stamina) stats)
        {
            const ItemQuality q = ItemQuality.Uncommon;
            Vector2 sw = WeaponBudget(ilvl, q, SwordFactor), dg = WeaponBudget(ilvl, q, DaggerFactor),
                st = WeaponBudget(ilvl, q, StaffFactor), bw = WeaponBudget(ilvl, q, BowFactor);
            int sell = 40 + 18 * ilvl;
            WeaponDefinition wSword = Weapon("Weapon_Gr_" + tag + "_sword", $"weapon.gr_{tag}_sword", swordName,
                w => { Melee(w, WeaponClass.Sword, sw.x, sw.y, 2.4f, 1f, "Kerbschlag"); w.quality = q; w.itemLevel = ilvl; });
            WeaponDefinition wDagger = Weapon("Weapon_Gr_" + tag + "_dagger", $"weapon.gr_{tag}_dagger", daggerName,
                w => { Melee(w, WeaponClass.Dagger, dg.x, dg.y, 2f, 0.7f, "Rindenschnitt"); w.quality = q; w.itemLevel = ilvl; });
            WeaponDefinition wStaff = Weapon("Weapon_Gr_" + tag + "_staff", $"weapon.gr_{tag}_staff", staffName,
                w => { Spell(w, st.x, st.y, 30f, "Harzfunke", Resin); w.quality = q; w.itemLevel = ilvl; });
            WeaponDefinition wBow = Weapon("Weapon_Gr_" + tag + "_bow", $"weapon.gr_{tag}_bow", bowName,
                w => { Bow(w, bw.x, bw.y, 32f, 2.8f); w.quality = q; w.itemLevel = ilvl; });
            WeaponItem($"gr_{tag}_sword", swordName, wSword, q, ilvl, req, IconShape.Sword, tag == "moor" ? Bronze : Iron, sell, swordText,
                BindType.OnPickup, (StatType.Strength, stats.primary), (StatType.Stamina, stats.stamina));
            WeaponItem($"gr_{tag}_dagger", daggerName, wDagger, q, ilvl, req, IconShape.Dagger, tag == "moor" ? Bronze : DarkLeather, sell, daggerText,
                BindType.OnPickup, (StatType.Agility, stats.primary), (StatType.Stamina, stats.stamina));
            WeaponItem($"gr_{tag}_staff", staffName, wStaff, q, ilvl, req, IconShape.Staff, tag == "moor" ? Peat : Resin, sell, staffText,
                BindType.OnPickup, (StatType.Intellect, stats.primary), (StatType.Willpower, stats.stamina));
            WeaponItem($"gr_{tag}_bow", bowName, wBow, q, ilvl, req, IconShape.Bow, tag == "moor" ? Bronze : Leather, sell, bowText,
                BindType.OnPickup, (StatType.Agility, stats.primary), (StatType.Stamina, stats.stamina));
        }

        /// <summary>Eintrag in einer vorhandenen Beutetabelle sicherstellen (auch ohne neue Standardwerte, Inspector-Werte bleiben).</summary>
        private static void EnsureLootEntry(string lootFile, string itemKey, float chance)
        {
            var loot = AssetDatabase.LoadAssetAtPath<LootTableDefinition>(LootFolder + "/" + lootFile + ".asset");
            if (loot == null || !Items.TryGetValue(itemKey, out ItemDefinition item) || item == null)
            {
                Debug.LogWarning($"[Ashveil] Beutetabelle {lootFile}: {itemKey} nicht eingetragen (fehlt).");
                return;
            }
            if (loot.entries == null) loot.entries = new List<LootTableDefinition.Entry>();
            foreach (LootTableDefinition.Entry e in loot.entries)
                if (e.item == item) return;
            loot.entries.Add(new LootTableDefinition.Entry { item = item, chance = chance, minCount = 1, maxCount = 1 });
            EditorUtility.SetDirty(loot);
        }
    }
}
