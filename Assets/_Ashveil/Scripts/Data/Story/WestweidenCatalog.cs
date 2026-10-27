using System.Collections.Generic;

namespace Ashveil.Data
{
    /// <summary>
    /// Westweiden (Lückenliste Punkt 2, 12.10.2026): Kennungen der Westweiden-Aufträge W01–W28, ihrer Figuren, Gegenstände,
    /// Fundstellen und Gegner an EINER Stelle – Inhaltsaufbau (Editor), Zelle, Server und Tests lesen dieselben Konstanten.
    /// Texte: Resources/Text/de/westweiden.txt (aus `claude/texte/westweiden_auftraege_v1.md`, v1.1 einfache Sprache; Wendung,
    /// Namen und W24-Ende von Max bestätigt). Die Aufträge gelten nur in Szenen mit dem Kennzeichen <see cref="SceneTag"/>
    /// (Kareth v4 mit angeschlossener Westweiden-Zelle). W23 „Die Siegel im Kreidekeller“ und W24 „Kopf des Zöllners“ seit dem
    /// Kreidekeller (Lückenliste Punkt 5, 12.10.2026 nachts).
    /// </summary>
    public static class WestweidenCatalog
    {
        /// <summary>Szenenkennzeichen (SceneQuestFilter) = WestweidenZelle.SceneTag.</summary>
        public const string SceneTag = "westweiden";

        // ------------------------------------------------------------------ Figuren (Namen: Max 12.10. „Alle übernehmen“)
        public const string Jost = "npc.westweiden.jost";               // Wegweiser-Wehrmann am Kammsattel
        public const string Brun = "npc.westweiden.brun";               // Züchter in Hallgrund (Name aus PROG-041; eigene Kennung, Sandbox-Figur bleibt)
        public const string Stallknecht = "npc.westweiden.stallknecht"; // Hallgrund
        public const string Harm = "npc.westweiden.harm";               // Schmied, Langer Rain
        public const string Lute = "npc.westweiden.lute";               // Sattler, Langer Rain
        public const string Alwine = "npc.westweiden.alwine";           // Hauptfrau der Bürgerwehr, Langer Rain
        public const string Ruland = "npc.westweiden.ruland";           // Postenführer, Bürgerwehr-Posten
        public const string Fenna = "npc.westweiden.fenna";             // Wagenführerin am Kornwagen
        public const string Hollerbach = "npc.westweiden.hollerbach";   // Windmüller, mittlere Mühle
        public const string Marthe = "npc.westweiden.marthe";           // Bäuerin, Dreispannhof
        public const string Wilke = "npc.westweiden.wilke";             // Altbauer, Dreispannhof
        public const string Tade = "npc.westweiden.tade";               // Wirt „Zum Mühlstein“
        public const string Stromfluesterer = "npc.westweiden.stromfluesterer"; // Langer Rain
        public const string Oberhirte = "npc.westweiden.oberhirte";     // Hirtenlager
        public const string Brigge = "npc.westweiden.brigge";           // Käserin, Hirtenlager
        public const string Imke = "npc.westweiden.imke";               // Schäferin, Heckenhof
        public const string Gerlach = "npc.westweiden.gerlach";         // Händler, Langer Rain
        public const string Sanne = "npc.westweiden.sanne";             // Kräuterfrau Mutter Sanne, Langer Rain

        public static readonly string[] Npcs =
            { Jost, Brun, Stallknecht, Harm, Lute, Alwine, Ruland, Fenna, Hollerbach, Marthe, Wilke, Tade, Stromfluesterer, Oberhirte, Brigge, Imke, Gerlach, Sanne };

        // ------------------------------------------------------------------ Aufträge
        public const string W01 = "quest.westweiden.w01_wort_nach_hallgrund";
        public const string W02 = "quest.westweiden.w02_eingerissene_koppel";
        public const string W03 = "quest.westweiden.w03_eisen_fuer_die_hufe";
        public const string W04 = "quest.westweiden.w04_bezahlt_in_fell";
        public const string W05 = "quest.westweiden.w05_zaumzeug";
        public const string W06 = "quest.westweiden.w06_wagen_kommt_nicht";
        public const string W07 = "quest.westweiden.w07_befehl_an_die_rampe";
        public const string W08 = "quest.westweiden.w08_wo_der_wagen_steht";
        public const string W09 = "quest.westweiden.w09_was_vom_korn_uebrig_ist";
        public const string W10 = "quest.westweiden.w10_korn_zum_rain";
        public const string W11 = "quest.westweiden.w11_mehl_fuer_den_dreispannhof";
        public const string W12 = "quest.westweiden.w12_schlammgrundler";
        public const string W13 = "quest.westweiden.w13_mann_mit_den_voegeln";
        public const string W14 = "quest.westweiden.w14_gemiedene_traenke";
        public const string W15 = "quest.westweiden.w15_hofplage";
        public const string W16 = "quest.westweiden.w16_wolle_nach_langer_rain";
        public const string W17 = "quest.westweiden.w17_wollrechnung";
        public const string W18 = "quest.westweiden.w18_kopf_von_rotkehl";
        public const string W19 = "quest.westweiden.w19_zollbuch";
        public const string W20 = "quest.westweiden.w20_alter_mann_liest";
        public const string W21 = "quest.westweiden.w21_letzte_seite";
        public const string W22 = "quest.westweiden.w22_schuetzen_in_der_hecke";
        public const string W23 = "quest.westweiden.w23_siegel_im_kreidekeller";
        public const string W24 = "quest.westweiden.w24_kopf_des_zoellners";
        public const string W25 = "quest.westweiden.w25_kaese_fuer_den_muehlstein";
        public const string W26 = "quest.westweiden.w26_salbe_fuer_den_posten";
        public const string W27 = "quest.westweiden.w27_haeute_vom_passfuss";
        public const string W28 = "quest.westweiden.w28_heckenkoenig";

        /// <summary>Alle 28 Aufträge.</summary>
        public static readonly string[] Quests =
            { W01, W02, W03, W04, W05, W06, W07, W08, W09, W10, W11, W12, W13, W14, W15, W16, W17, W18, W19, W20, W21, W22, W23, W24, W25, W26, W27, W28 };

        /// <summary>
        /// EP (Vorschlag Claude, nach der Kareth-Reihe fortgeschrieben): voller Auftrag 1 000 EP auf Stufe 8, +150 je Stufe;
        /// Botengang 30 % (in der Hauptkette 45 %), Kopf eines seltenen Tiers ×1,3, Elite ×1,5; Kreidekeller (Gruppe) ×1,5,
        /// „Kopf des Zöllners“ (Ende der Hauptkette) ×1,3.
        /// </summary>
        public const int QuestXpTotal = 300 + 1000 + 330 + 1100 + 380 + 560 + 560 + 630 + 1400 + 630 + 630 + 1250 + 470 + 1100 + 1550 + 470 + 470
                                        + 2000 + 1700 + 770 + 830 + 1700 + 2800 + 2600 + 510 + 560 + 2000 + 3000;

        // ------------------------------------------------------------------ Gegner (EnemyBuilder.Westweiden)
        public const string EnemyOtter = "enemy.ww_bruchotter";
        public const string EnemyWeasel = "enemy.ww_heckenwiesel";
        public const string EnemySnake = "enemy.ww_kammnatter";
        public const string EnemyArcher = "enemy.ww_heckenschuetze";
        public const string EnemyRotkehl = "enemy.ww_rotkehl";
        public const string EnemyHedgeKing = "enemy.ww_heckenkoenig";
        public const string EnemyHabbo = "enemy.ww_habbo";
        public const string EnemyCellarMaster = "enemy.ww_kellermeister";
        public const string EnemyCellarGuard = "enemy.ww_kellerwache";
        public const string EnemyCellarArcher = "enemy.ww_kellerschuetze";

        // ------------------------------------------------------------------ Gegenstände
        public const string ItemPost = "item.ww_zaunpfahl";
        public const string ItemOtterPelt = "item.ww_bruchotterfell";
        public const string ItemHorseshoes = "item.ww_hufeisen";
        public const string ItemBridle = "item.ww_zaumzeug";
        public const string ItemOrder = "item.ww_befehl";
        public const string ItemGrainSack = "item.ww_kornsack";
        public const string ItemSalvagedGrain = "item.ww_geborgenes_korn";
        public const string ItemFlour = "item.ww_mehlsack";
        public const string ItemMudfish = "item.ww_schlammgrundler";
        public const string ItemWool = "item.ww_wollballen";
        public const string ItemBill = "item.ww_rechnung";
        public const string ItemRotkehlHead = "item.ww_rotkehl_kopf";
        public const string ItemTollBook = "item.ww_zollbuch";
        public const string ItemCheese = "item.ww_kaeselaib";
        public const string ItemSalve = "item.ww_salbe";
        public const string ItemSnakeSkin = "item.ww_kammnatterhaut";
        public const string ItemHedgeKingSkin = "item.ww_heckenkoenig_haut";
        public const string ItemSealRed = "item.ww_siegel_dreispannhof";
        public const string ItemSealBlue = "item.ww_siegel_hallgrund";
        public const string ItemSealMill = "item.ww_siegel_muehle";
        public const string ItemBoatList = "item.ww_bootsliste";
        public const string ItemHabboHead = "item.ww_habbo_kopf";

        // ------------------------------------------------------------------ Fundstellen (Rechtsklick, StoryAnchor = Aktion)
        public const string NodeDrain = "ww.zulauf";
        public const string NodeTollBook = "ww.zollbuch";
        public const string StoryDrainCleared = "story.ww.zulauf_frei";
        public const string NodeSealRed = "ww.siegel_rot", NodeSealBlue = "ww.siegel_blau", NodeSealMill = "ww.siegel_muehle";
        public const int PostCount = 10, GrainCount = 12, FishCount = 12;
        public static string NodePost(int i) => "ww.pfahl" + (i + 1);
        public static string NodeGrain(int i) => "ww.korn" + (i + 1);
        public static string NodeFish(int i) => "ww.grundler" + (i + 1);

        private static GlockenrastCatalog.Node[] _nodes;

        /// <summary>Fundstellen der Westweiden – gleiche Mechanik wie die Glockenrast (Server prüft Auftrag, Abstand, „genommen“).</summary>
        public static IReadOnlyList<GlockenrastCatalog.Node> Nodes
        {
            get
            {
                if (_nodes != null) return _nodes;
                var l = new List<GlockenrastCatalog.Node>
                {
                    new GlockenrastCatalog.Node { Action = NodeDrain, Quest = W14, Story = StoryDrainCleared, LabelKey = "story.westweiden.action.drain", Range = 4.5f },
                    new GlockenrastCatalog.Node { Action = NodeTollBook, Quest = W19, Item = ItemTollBook, LabelKey = "story.westweiden.action.tollbook", Range = 4f }
                };
                l.Add(new GlockenrastCatalog.Node { Action = NodeSealRed, Quest = W23, Item = ItemSealRed, LabelKey = "story.westweiden.action.seal", Range = 4f });
                l.Add(new GlockenrastCatalog.Node { Action = NodeSealBlue, Quest = W23, Item = ItemSealBlue, LabelKey = "story.westweiden.action.seal", Range = 4f });
                l.Add(new GlockenrastCatalog.Node { Action = NodeSealMill, Quest = W23, Item = ItemSealMill, LabelKey = "story.westweiden.action.seal", Range = 4f });
                for (int i = 0; i < PostCount; i++)
                    l.Add(new GlockenrastCatalog.Node { Action = NodePost(i), Quest = W02, Item = ItemPost, LabelKey = "story.westweiden.action.post", Range = 4f });
                for (int i = 0; i < GrainCount; i++)
                    l.Add(new GlockenrastCatalog.Node { Action = NodeGrain(i), Quest = W09, Item = ItemGrainSack, LabelKey = "story.westweiden.action.grain", Range = 4f });
                for (int i = 0; i < FishCount; i++)
                    l.Add(new GlockenrastCatalog.Node { Action = NodeFish(i), Quest = W12, Item = ItemMudfish, LabelKey = "story.westweiden.action.fish", Range = 4.5f });
                _nodes = l.ToArray();
                return _nodes;
            }
        }

        public static bool IsNodeAction(string action) => action != null && action.StartsWith("ww.", System.StringComparison.Ordinal);

        public static bool TryNode(string action, out GlockenrastCatalog.Node node)
        {
            if (IsNodeAction(action))
                foreach (GlockenrastCatalog.Node n in Nodes)
                    if (n.Action == action) { node = n; return true; }
            node = default;
            return false;
        }
    }
}
