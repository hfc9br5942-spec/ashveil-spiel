using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;
using KC = Ashveil.Data.KarethCatalog;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Classic-Paket (Max 11.10.2026 abends): Auftragsgegenstände der drei „Kopf von …“-Aufträge und Beute der sechs seltenen Tiere.
    /// Seltene Tiere lassen wie in Classic sicher ein Ausrüstungsteil (eher besser, nach Klasse), reichlich Tierbeute und je ein
    /// gutes Stück ihrer Art fallen; keine Münzen (Tiere, wie M5.4). Die Auftragsgegenstände fallen nur, solange der Auftrag läuft.
    /// Namen und Texte: VORSCHLAG (Claude).
    /// </summary>
    public static partial class ItemContentBuilder
    {
        private static void BuildKarethRareItems()
        {
            QuestItem("k28_altborste_tusk", "Hauer der Altborste", IconShape.Bone, new Color(0.86f, 0.82f, 0.7f), KC.H1, 1,
                "Gelb wie alter Käse und so lang wie Wennas Unterarm. An der Spitze klebt noch Erde aus der Vorratsgrube.");
            QuestItem("k28_narbenfang_ear", "Narbenfangs zerrissenes Ohr", IconShape.Hide, new Color(0.3f, 0.26f, 0.23f), KC.H2, 1,
                "Dreimal eingerissen und dreimal verheilt. Das vierte Mal hat nicht gereicht.");
            QuestItem("k28_krummnadel_spine", "Krummnadels Rückenstachel", IconShape.Bone, new Color(0.82f, 0.78f, 0.66f), KC.H3, 1,
                "Hohl, knochenhell und deutlich gebogen. Gerrit wird sagen, das sei Absicht.");
        }

        private static void BuildKarethRareLoot()
        {
            string[] better = { "warden_pauldrons", "iron_gauntlets", "stalker_hood", "ashen_leather_jerkin", "stalker_boots", "weaver_robe", "ember_sash",
                                "wolfhide_cloak", "bone_amulet", "hunter_shoulderguards", "hunter_breeches", "warden_cuirass" };
            LootTableDefinition.Group Gear() => Group("Ausrüstung (selten)", 1f, true, Weighted(better, 1f));

            LootTable("Loot_Altborste", "loot.rare.altborste", "Altborste", t =>
            {
                t.entries = Entries(("k28_altborste_tusk", 1f, 1, 1), ("k28_coarse_bristles", 1f, 2, 4), ("k28_rooter_hide", 1f, 1, 2),
                    ("k28_digging_claw", 1f, 1, 1));
                t.groups = new List<LootTableDefinition.Group> { Gear() };
            });
            LootTable("Loot_Rostschere", "loot.rare.rostschere", "Rostschere", t =>
            {
                t.entries = Entries(("k28_damaged_claw", 1f, 1, 2), ("k28_ribbed_claw_plate", 1f, 1, 2));
                t.groups = new List<LootTableDefinition.Group> { Gear() };
            });
            LootTable("Loot_Narbenfang", "loot.rare.narbenfang", "Narbenfang", t =>
            {
                t.entries = Entries(("k28_narbenfang_ear", 1f, 1, 1), ("wolf_fang", 1f, 2, 3), ("torn_leather", 1f, 1, 2));
                t.groups = new List<LootTableDefinition.Group> { Gear() };
            });
            LootTable("Loot_Raunebalg", "loot.rare.raunebalg", "Raunebalg", t =>
            {
                t.entries = Entries(("k28_tough_fold", 1f, 2, 3), ("k28_curved_claw", 1f, 1, 2), ("k28_throat_skin", 1f, 1, 1));
                t.groups = new List<LootTableDefinition.Group> { Gear() };
            });
            LootTable("Loot_Krummnadel", "loot.rare.krummnadel", "Krummnadel", t =>
            {
                t.entries = Entries(("k28_krummnadel_spine", 1f, 1, 1), ("spine_needle", 1f, 4, 6), ("chitin_plate", 1f, 1, 2));
                t.groups = new List<LootTableDefinition.Group> { Gear() };
            });
            LootTable("Loot_Moosbuckel", "loot.rare.moosbuckel", "Moosbuckel", t =>
            {
                t.entries = Entries(("peat_clump", 1f, 3, 5));
                t.groups = new List<LootTableDefinition.Group> { Gear() };
            });
        }

        /// <summary>Beutetabellen an die seltenen Tiere hängen (nach <see cref="EnemyBuilder.BuildKarethRares"/>).</summary>
        public static void AssignKarethRareLoot()
        {
            Assign(EnemyBuilder.FileAltborste, "Loot_Altborste");
            Assign(EnemyBuilder.FileRostschere, "Loot_Rostschere");
            Assign(EnemyBuilder.FileNarbenfang, "Loot_Narbenfang");
            Assign(EnemyBuilder.FileRaunebalg, "Loot_Raunebalg");
            Assign(EnemyBuilder.FileKrummnadel, "Loot_Krummnadel");
            Assign(EnemyBuilder.FileMoosbuckel, "Loot_Moosbuckel");
            AssetDatabase.SaveAssets();
        }
    }
}
