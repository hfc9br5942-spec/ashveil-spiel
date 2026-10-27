using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;
using WC = Ashveil.Data.WestweidenCatalog;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden (Lückenliste Punkt 2, 12.10.2026): Auftragsgegenstände der Aufträge W01–W28 und die Beute der Westweiden-Gegner.
    /// Beschreibungen sind Vorschläge (Claude, Erzählwerkstatt; einfache Sprache). Auftragsbeute fällt nur, solange der Auftrag läuft
    /// und noch Stücke fehlen (<c>NetworkProgression.ServerWantsLoot</c>). Tiere: Material und etwas Rüstung, keine Münzen (wie M5.4);
    /// Habbos Leute: Münzen, Brot, Leinen, Ausrüstung. Gegenstände und Tabellen werden bei jedem Aufbau neu geschrieben.
    /// </summary>
    public static partial class ItemContentBuilder
    {
        private static readonly Color WwWood = new Color(0.52f, 0.40f, 0.26f);
        private static readonly Color WwGrain = new Color(0.80f, 0.68f, 0.42f);
        private static readonly Color WwPaper = new Color(0.82f, 0.77f, 0.64f);
        private static readonly Color WwLeather = new Color(0.45f, 0.30f, 0.18f);

        private static void BuildWestweidenItems()
        {
            bool keep = _refresh;
            _refresh = true;
            try
            {
                // ---------------------------------------------------------- Hallgrund (W02–W05)
                QuestItem("ww_zaunpfahl", "Zaunpfahl", IconShape.Staff, WwWood, WC.W02, 8,
                    "Eiche, oben gespalten, unten noch gut. Jemand hat ihn aus dem Boden gerissen, nicht umgefahren.");
                QuestItem("ww_bruchotterfell", "Bruchotterfell", IconShape.Hide, new Color(0.30f, 0.22f, 0.15f), WC.W04, 8,
                    "Dicht, ölig und schwer vom Wasser. Mit Wachs hält es dicht wie nichts sonst.");
                QuestItem("ww_hufeisen", "Hufeisen für Hallgrund", IconShape.Ore, new Color(0.45f, 0.45f, 0.47f), WC.W05, 1,
                    "Zwölf Eisen auf einer Schnur, noch schwarz vom Feuer. Harm hat jedes mit einem kleinen Hammer gezeichnet.");
                QuestItem("ww_zaumzeug", "Zaumzeug", IconShape.Belt, WwLeather, WC.W05, 1,
                    "Weiches Leder, enge Nähte. Es riecht nach Fett und ein bisschen nach Lute.");

                // ---------------------------------------------------------- Hauptkette „Korn und Hecken“ (W07–W11, W19–W20)
                QuestItem("ww_befehl", "Befehl der Hauptfrau", IconShape.Letter, WwPaper, WC.W07, 1,
                    "Zwei Zeilen in harter Schrift: „Wo war der Wagen, und mit wem. – A. R.“");
                QuestItem("ww_kornsack", "Kornsack", IconShape.Bag, WwGrain, WC.W09, 8,
                    "Halb voll, an einer Ecke aufgerissen. Auf dem Leinen ein Hofzeichen.");
                QuestItem("ww_geborgenes_korn", "Geborgenes Korn", IconShape.Bag, WwGrain, WC.W10, 1,
                    "Acht Säcke, mit Seil zusammengebunden. Es ist nicht alles. Aber es ist ein Mühlentag.");
                QuestItem("ww_mehlsack", "Erster Sack Mehl", IconShape.Food, new Color(0.92f, 0.89f, 0.80f), WC.W11, 1,
                    "Frisch gemahlen und noch warm. Der erste Sack gehört dem Hof, der das Korn gab.");
                QuestItem("ww_zollbuch", "Zollbuch der Alten Zollhütte", IconShape.Scroll, new Color(0.38f, 0.26f, 0.16f), WC.W19, 1,
                    "Leder, Messing an den Ecken, die Ecke unten rechts fehlt. Die Seiten riechen nach Keller. Die Schrift ist sehr alt.");

                // ---------------------------------------------------------- Langer Rain, Höfe, Hirtenlager (W12, W16–W18, W25)
                QuestItem("ww_schlammgrundler", "Schlammgrundler", IconShape.Food, new Color(0.42f, 0.42f, 0.36f), WC.W12, 8,
                    "Grau, glatt und hässlich. In Butter gebraten ein Fest, sagt Tade.");
                QuestItem("ww_wollballen", "Wollballen", IconShape.Cloth, new Color(0.86f, 0.83f, 0.74f), WC.W16, 1,
                    "Leichter, als er aussieht. Er riecht stärker, als dir lieb ist. Feucht ist er nicht.");
                QuestItem("ww_rechnung", "Gerlachs Rechnung", IconShape.Letter, WwPaper, WC.W17, 1,
                    "Darauf steht nur ein Wort, sauber unterstrichen: „Quitt.“");
                QuestItem("ww_rotkehl_kopf", "Kopf von Rotkehl", IconShape.Bone, new Color(0.6f, 0.2f, 0.1f), WC.W18, 1,
                    "Rot an der Kehle, als hätte es sich gerade gewaschen. Zu groß für ein Wiesel.");
                QuestItem("ww_kaeselaib", "Käselaib", IconShape.Food, new Color(0.93f, 0.82f, 0.48f), WC.W25, 1,
                    "Erste Milch nach dem Kalben, vom Frühjahr. Unangebissen. Brigge hat ihn gewogen.");

                // ---------------------------------------------------------- Kreidekeller und Ende der Hauptkette (W23–W24)
                QuestItem("ww_siegel_dreispannhof", "Hofmarke vom Dreispannhof", IconShape.Cloth, new Color(0.62f, 0.18f, 0.12f), WC.W23, 1,
                    "Ein Stück Sackleinen mit rotem Hofzeichen. Marthe würde es überall erkennen.");
                QuestItem("ww_siegel_hallgrund", "Hofmarke von Hallgrund", IconShape.Cloth, new Color(0.22f, 0.32f, 0.62f), WC.W23, 1,
                    "Blaues Zeichen, ein Pferdekopf. Brun Haller hat es selbst geschnitzt.");
                QuestItem("ww_siegel_muehle", "Mühlenzeichen vom Rain", IconShape.Cloth, new Color(0.85f, 0.82f, 0.72f), WC.W23, 1,
                    "Ein Mühlrad mit vier Flügeln, mit Mehl gestempelt. Das Zeichen von Langer Rain.");
                QuestItem("ww_bootsliste", "Liste des Kellermeisters", IconShape.Letter, WwPaper, WC.W23, 1,
                    "Bootsladungen nach Westhafen, Woche für Woche. Unten ein Siegel in braunem Wachs: K.");
                QuestItem("ww_habbo_kopf", "Habbos Kopf", IconShape.Cloth, new Color(0.42f, 0.36f, 0.30f), WC.W24, 1,
                    "In ein grobes Tuch gewickelt. Alwine will ihn an der Hütte begraben, unter seinem Zoll.");

                // ---------------------------------------------------------- Passfuß (W26–W28)
                QuestItem("ww_salbe", "Salbe von Mutter Sanne", IconShape.Potion, new Color(0.45f, 0.55f, 0.32f), WC.W26, 1,
                    "Ein Tontopf mit grüner Salbe. Zweimal am Tag. Liegen, bis die Schwellung weg ist.");
                QuestItem("ww_kammnatterhaut", "Kammnatterhaut", IconShape.Hide, new Color(0.36f, 0.38f, 0.24f), WC.W27, 8,
                    "Zäh und schuppig, am Rücken ein roter Kamm. Daraus werden Stiefel, durch die kein Zahn geht.");
                QuestItem("ww_heckenkoenig_haut", "Haut des Heckenkönigs", IconShape.Hide, new Color(0.24f, 0.30f, 0.16f), WC.W28, 1,
                    "So lang wie ein Kornwagen. Sie ist schwer, und die Schuppen sind so groß wie eine Hand.");
            }
            finally
            {
                _refresh = keep;
            }
        }

        /// <summary>Beutetabellen der Westweiden-Gegner (Aufruf in <see cref="Build"/> vor dem Versionsstempel).</summary>
        private static void BuildWestweidenLoot()
        {
            string[] animalArmor = { "dented_helm", "chainmail_shirt", "stalker_gloves", "rope_belt", "grey_wool_cloak", "tarnished_ring", "weaver_sandals",
                                     "travel_hood", "travel_mantle", "travel_wraps", "travel_trousers", "iron_sabatons" };
            string[] betterArmor = { "warden_pauldrons", "iron_gauntlets", "stalker_hood", "ashen_leather_jerkin", "stalker_boots", "weaver_robe", "ember_sash",
                                     "wolfhide_cloak", "bone_amulet", "hunter_shoulderguards", "hunter_breeches", "warden_cuirass" };
            string[] humanGood = { "warden_pauldrons", "iron_gauntlets", "stalker_hood", "ashen_leather_jerkin", "stalker_boots", "weaver_robe", "ember_sash",
                                   "wolfhide_cloak", "bone_amulet", "notched_greataxe", "warden_blade", "ember_staff", "cracked_focus", "ashwood_longbow",
                                   "hunter_shoulderguards", "hunter_breeches", "warden_cuirass", "oak_shield" };
            // 22.10. WESTWEIDEN-03 / FORTSCHRITT-08: Rotkehl (selten, 12), Heckenkönig und Kellermeister (Elite 15), die Kellerleute
            // (Elite 13–14) und Habbo (15) warfen nur Kareth-Teile der Stufe 1–5. Jetzt VORHANDENE Teile der Stufe 9–12 (keine neuen
            // Gegenstände): Grubenausrüstung und Boss-/Kapitänsteile der Salzgruben – im Spieltest-Build (Kareth v4 + Westweiden) sonst
            // nicht erreichbar, also wird keine erreichbare Belohnung entwertet. Bewusst NICHT gr_moor_* / k28_*: das sind Wahlbelohnungen
            // von Glockenrast- und Kareth-Aufträgen (Gegenprüfung 22.10.). oak_shield bleibt (einziger Schild im Datenbestand).
            // Die gewöhnlichen Gegner (Tiere, Habbos Leute) behalten ihre Listen; eigene Westweiden-Ausrüstung bleibt Inhaltspunkt (Max).
            string[] wwGear = { "miner_helmet", "hewer_gloves", "salt_crusted_sabatons", "brine_robe", "lamp_bearer_cloak", "shift_belt",
                                "crab_shell_pauldrons", "salt_ring" };                                            // gut, benötigt 10–11
            string[] wwWeapons = { "hewer_dagger", "shaft_mace", "saltwood_staff", "oak_shield" };              // gut, benötigt 9 (Schild 1)
            string[] wwRareArmor = { "claw_shell_pauldrons", "brood_mantle", "drowned_tricorne" };              // selten, benötigt 11–12
            string[] wwRareWeapons = { "drowned_sabre", "foremans_pick", "salt_lake_bow", "brine_focus", "lamplight_staff" };   // selten/episch, 11–12
            bool keep = _refresh;
            _refresh = true;
            try
            {
                LootTable("Loot_WW_Bruchotter", "loot.ww_bruchotter", "Bruchotter", t =>
                {
                    t.entries = Entries(("ww_bruchotterfell", 0.65f, 1, 1), ("torn_leather", 0.35f, 1, 1));
                    t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.06f, true, Weighted(animalArmor, 1f)) };
                });
                LootTable("Loot_WW_Heckenwiesel", "loot.ww_heckenwiesel", "Heckenwiesel", t =>
                {
                    t.entries = Entries(("torn_leather", 0.4f, 1, 1));
                    t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.06f, true, Weighted(animalArmor, 1f)) };
                });
                LootTable("Loot_WW_Kammnatter", "loot.ww_kammnatter", "Kammnatter", t =>
                {
                    t.entries = Entries(("ww_kammnatterhaut", 0.55f, 1, 1), ("torn_leather", 0.25f, 1, 1));
                    t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.08f, true, Weighted(animalArmor, 2f), Weighted(betterArmor, 1f)) };
                });
                LootTable("Loot_WW_Rotkehl", "loot.ww_rotkehl", "Rotkehl", t =>
                {
                    t.entries = Entries(("ww_rotkehl_kopf", 1f, 1, 1), ("torn_leather", 1f, 1, 2));
                    t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.6f, true, Weighted(wwGear, 1f)) };   // 22.10. WESTWEIDEN-03
                });
                LootTable("Loot_WW_Heckenkoenig", "loot.ww_heckenkoenig", "Heckenkönig", t =>
                {
                    t.entries = Entries(("ww_heckenkoenig_haut", 1f, 1, 1), ("torn_leather", 1f, 2, 3));
                    // 22.10. FORTSCHRITT-08: Tier – nur Rüstung, Elite 15 mit Chance auf ein seltenes Teil
                    t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 1f, true, Weighted(wwGear, 1f), Weighted(wwRareArmor, 2f)) };
                });
                LootTable("Loot_WW_Kellerleute", "loot.ww_kellerleute", "Habbos Leute im Kreidekeller", t =>
                {
                    t.entries = Entries(("faded_coin", 0.8f, 1, 3), ("dry_bread", 0.5f, 1, 2), ("linen_scraps", 0.5f, 1, 2), ("healing_draught", 0.15f, 1, 1));
                    t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.35f, true, Weighted(wwGear, 1f), Weighted(wwWeapons, 1f)) };   // 22.10. FORTSCHRITT-08
                });
                LootTable("Loot_WW_Kellermeister", "loot.ww_kellermeister", "Kellermeister Kord", t =>
                {
                    t.entries = Entries(("ww_bootsliste", 1f, 1, 1), ("faded_coin", 1f, 3, 6), ("healing_draught", 0.5f, 1, 2));
                    // 22.10. FORTSCHRITT-08: Endgegner des Kreidekellers (Gruppe 2–3) – garantiert ein seltenes Teil der Stufe 11–12
                    t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 1f, true, Weighted(wwRareArmor, 1f), Weighted(wwRareWeapons, 1f)) };
                });
                LootTable("Loot_WW_Habbo", "loot.ww_habbo", "Habbo der Zöllner", t =>
                {
                    t.entries = Entries(("ww_habbo_kopf", 1f, 1, 1), ("faded_coin", 1f, 2, 5));
                    t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.8f, true, Weighted(wwGear, 1f), Weighted(wwWeapons, 1f)) };   // 22.10. FORTSCHRITT-08
                });
                LootTable("Loot_WW_HabbosLeute", "loot.ww_habbos_leute", "Habbos Leute", t =>
                {
                    t.entries = Entries(("faded_coin", 0.5f, 1, 2), ("dry_bread", 0.4f, 1, 2), ("linen_scraps", 0.35f, 1, 2), ("healing_draught", 0.08f, 1, 1));
                    t.groups = new List<LootTableDefinition.Group> { Group("Ausrüstung", 0.2f, true, Weighted(humanGood, 1f)) };
                });
            }
            finally
            {
                _refresh = keep;
            }
        }

        /// <summary>
        /// Westweiden: Beutetabellen an die Westweiden-Gegner hängen (nach <see cref="EnemyBuilder.BuildWestweidenCreatures"/>; immer, weil der
        /// Gegner-Aufbau die Definitionen neu schreibt). Rückgabe: Zahl der zugeordneten Gegner.
        /// </summary>
        public static int AssignWestweidenLoot()
        {
            int n = 0;
            void Set(string enemyFile, string lootFile)
            {
                var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(EnemyBuilder.DefFolder + "/" + enemyFile + ".asset");
                var loot = AssetDatabase.LoadAssetAtPath<LootTableDefinition>(LootFolder + "/" + lootFile + ".asset");
                if (enemy == null || loot == null)
                {
                    Debug.LogWarning($"[Ashveil] Westweiden-Beute: {enemyFile} oder {lootFile} fehlt.");
                    return;
                }
                if (enemy.lootTable != loot)
                {
                    enemy.lootTable = loot;
                    EditorUtility.SetDirty(enemy);
                }
                n++;
            }
            Set("Enemy_WW_Bruchotter", "Loot_WW_Bruchotter");
            Set("Enemy_WW_Heckenwiesel", "Loot_WW_Heckenwiesel");
            Set("Enemy_WW_Kammnatter", "Loot_WW_Kammnatter");
            Set("Enemy_WW_Rotkehl", "Loot_WW_Rotkehl");
            Set("Enemy_WW_Heckenkoenig", "Loot_WW_Heckenkoenig");
            Set("Enemy_WW_Heckenschlaeger", "Loot_WW_HabbosLeute");
            Set("Enemy_WW_Heckenschuetze", "Loot_WW_HabbosLeute");
            Set("Enemy_WW_Habbo", "Loot_WW_Habbo");
            Set("Enemy_WW_Kellermeister", "Loot_WW_Kellermeister");
            Set("Enemy_WW_Kellerwache", "Loot_WW_Kellerleute");
            Set("Enemy_WW_Kellerschuetze", "Loot_WW_Kellerleute");
            AssetDatabase.SaveAssets();
            return n;
        }
    }
}
