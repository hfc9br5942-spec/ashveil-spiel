namespace Ashveil.Data
{
    /// <summary>
    /// #28 Kareth v4, Welle 0 (Max 05.10.: Auftragsliste „Ja, so bauen“; 07.10. abends: Geber, Gasthaus „Zur Schale“, Ebba Salweg,
    /// U-7 „Die verschwundenen Gaben“): Kennungen der elf Ergänzungsaufträge K-A bis K-D und U-1 bis U-7, ihrer Figuren, Gegner,
    /// Gegenstände und Erkundungsziele an EINER Stelle – Inhaltsaufbau (Editor), Szene, Server und Tests lesen dieselben Konstanten.
    /// Texte stehen in der Texttabelle (Resources/Text/de/kareth.txt). Die Aufträge gibt es nur in Szenen mit dem Kennzeichen
    /// <see cref="SceneTag"/> (Kareth v4); in der Sandbox fehlen ihre Orte und Figuren.
    /// </summary>
    public static class KarethCatalog
    {
        /// <summary>Szenenkennzeichen (SceneQuestFilter) – die Aufträge stehen nur in Kareth v4.</summary>
        public const string SceneTag = "kareth_v4";

        // ------------------------------------------------------------------ Figuren
        public const string Wenna = "npc.wenna";
        public const string Jorn = "npc.jorn";
        public const string Ulf = "npc.ulf";
        public const string Oswald = "npc.oswald";
        public const string Ebba = "npc.ebba_salweg";
        /// <summary>Untersteg beleben (Max 10.10. abends, Sheets v1): Krämerin Idun Vesper, Händlerin am Laden „Kram und Tuch“.</summary>
        public const string Idun = "npc.untersteg.idun";
        /// <summary>Untersteg (Sheets v1, Max 10.10.): Müllerin Grete Bachmann und Brückenwache Hennek Doll – seit 10.10. spät Auftragsfiguren (Nebenaufträge).</summary>
        public const string Grete = "npc.untersteg.grete";
        public const string Hennek = "npc.untersteg.hennek";
        // vorhandene Figuren
        public const string Bralt = "npc.oskar_bralt";
        public const string Cartographer = "npc.cartographer";
        public const string Tamo = "npc.tamo";
        public const string Veit = "npc.veit";
        public const string Pilgrim = "npc.glockenrast.pilger";

        // ------------------------------------------------------------------ Aufträge (Reihenfolge = Auftragsliste)
        public const string KA = "quest.kareth.vorratsgruben";
        public const string KB = "quest.kareth.wasserstelle";
        public const string KC = "quest.kareth.waldrand";
        public const string KD = "quest.kareth.holzlieferung";
        public const string U1 = "quest.kareth.tamos_wagen";
        public const string U2 = "quest.kareth.krug_fuer_die_schale";
        public const string U3 = "quest.kareth.wrack_im_salz";
        public const string U4 = "quest.kareth.arbeitszeichen";
        public const string U5 = "quest.kareth.zeichensteine";
        public const string U6 = "quest.kareth.leere_hoefe";
        public const string U7 = "quest.kareth.verschwundene_gaben";

        public static readonly string[] Quests = { KA, KB, KC, KD, U1, U2, U3, U4, U5, U6, U7 };

        // ------------------------------------------------------------------ Nebenaufträge (Max 10.10.2026 spät: „Classic: viele einmalige“,
        // „etwa 15“, Texte von Claude ohne Freigabe, vorhandene Gegner und wenige neue Lager) – einfache Kampf- und Sammelaufträge zum Hochspielen
        public const string N01 = "quest.kareth.n01_wuehler_im_korn";
        public const string N02 = "quest.kareth.n02_wolfszaehne";
        public const string N03 = "quest.kareth.n03_klapper_am_bach";
        public const string N04 = "quest.kareth.n04_leinen";
        public const string N05 = "quest.kareth.n05_schuetzinnen";
        public const string N06 = "quest.kareth.n06_nadeln";
        public const string N07 = "quest.kareth.n07_torf";
        public const string N08 = "quest.kareth.n08_heuwiesen";
        public const string N09 = "quest.kareth.n09_schlick_am_damm";
        public const string N10 = "quest.kareth.n10_chitin";
        public const string N11 = "quest.kareth.n11_mehldiebe";
        public const string N12 = "quest.kareth.n12_grabsalz";
        public const string N13 = "quest.kareth.n13_schwere_schrate";
        public const string N14 = "quest.kareth.n14_breitkopf";
        public const string N15 = "quest.kareth.n15_altmetall";

        public static readonly string[] NebenQuests = { N01, N02, N03, N04, N05, N06, N07, N08, N09, N10, N11, N12, N13, N14, N15 };

        /// <summary>Summe der Erfahrung der Nebenaufträge (≈ 0,75 der Stufenwerte der Ergänzungsaufträge, damit Glockenrast nicht überholt wird).</summary>
        public const int NebenXpTotal = 300 + 300 + 380 + 380 + 450 + 520 + 600 + 600 + 600 + 600 + 680 + 750 + 750 + 750 + 750;

        // ------------------------------------------------------------------ Classic-Paket (Max 11.10.2026 abends, Classic-Bauplan §5: alle vier Punkte
        // freigegeben – Botengänge/Hinweise, seltene Gegner, „Kopf von …“-Aufträge, Ketten verlängern). Texte Claude nach den Sheets.
        // Botengänge (Gespräch beim Abgabe-NPC, Classic: 65 % aller Aufträge sind Wege): ziehen den Spieler von Lager zu Lager.
        public const string B1 = "quest.kareth.b1_meldung_an_die_bruecke";     // Bralt → Hennek (4)
        public const string B2 = "quest.kareth.b2_ein_bett_in_der_schale";     // Hennek → Ebba (4), nach B1
        public const string B3 = "quest.kareth.b3_tamos_deckel";               // Ebba → Tamo (5), nach B2
        public const string B4 = "quest.kareth.b4_bohlenweg_auf_der_karte";    // Kartograf → Runa in der Glockenrast (6)
        public const string B5 = "quest.kareth.b5_pfaehle_fuer_ulf";           // Ulf → Jorn (2)
        public const string B6 = "quest.kareth.b6_mehl_fuer_den_fuhrmann";     // Grete → Oswald (6)
        public const string B7 = "quest.kareth.b7_abrieb_fuer_den_kartografen";// Veit → Kartograf (8), nach U-5 (Kette U-4 → U-5 → B7)
        public const string B8 = "quest.kareth.b8_kraeuter_gegen_mehl";        // Maelis → Grete (5)
        // „Kopf von …“ (Classic: Einzelgegenstand eines benannten Gegners) – je der letzte Schritt einer verlängerten Kette
        public const string H1 = "quest.kareth.h1_altborste";                  // Wenna (4), K-A → K-B → H1
        public const string H2 = "quest.kareth.h2_narbenfang";                 // Hennek (7), N03 → N08 → H2
        public const string H3 = "quest.kareth.h3_krummnadel";                 // Gerrit (9), N02 → N06 → H3

        public static readonly string[] ClassicQuests = { B1, B2, B3, B4, B5, B6, B7, B8, H1, H2, H3 };
        public const int ClassicXpTotal = 150 + 120 + 150 + 200 + 80 + 180 + 300 + 150 + 600 + 1000 + 1300;

        // Seltene Gegner (Classic ≈ 1 % der Gegner): benannte, kräftigere Tiere vorhandener Arten, drei Verstecke, Wiederkehr 20–45 min
        public const string RareAltborste = "enemy.rare.altborste";     // Furchenwühler, Mulde (4)
        public const string RareRostschere = "enemy.rare.rostschere";   // Uferklapper, Salzsee-Ufer (7)
        public const string RareNarbenfang = "enemy.rare.narbenfang";   // Aschwolf, Heuwiesen und Feldmark (7)
        public const string RareRaunebalg = "enemy.rare.raunebalg";     // Flüsterbalg, Kiefernhang (7)
        public const string RareKrummnadel = "enemy.rare.krummnadel";   // Nadelspeier, Nordmoor (9)
        public const string RareMoosbuckel = "enemy.rare.moosbuckel";   // Torfschrat, Kuppeln im Glockenmoor (9)
        public static readonly string[] Rares = { RareAltborste, RareRostschere, RareNarbenfang, RareRaunebalg, RareKrummnadel, RareMoosbuckel };

        // Auftragsgegenstände der „Kopf von …“-Aufträge (fallen nur, solange der Auftrag läuft)
        public const string ItemAltborsteTusk = "item.k28_altborste_tusk";
        public const string ItemNarbenfangEar = "item.k28_narbenfang_ear";
        public const string ItemKrummnadelSpine = "item.k28_krummnadel_spine";

        /// <summary>U-2 abgegeben: Ebba hat ihren Krug wieder (Gruß von Ebba und Oswald je Spieler).</summary>
        public const string FlagJugReturned = "story.kareth.krug_zurueck";

        /// <summary>Summe der Auftrags-Erfahrung (Auftragsliste: K-A bis K-D 1 850, U-1 bis U-7 6 600).</summary>
        public const int QuestXpTotal = 250 + 450 + 450 + 700 + 900 + 700 + 1000 + 1000 + 1000 + 1100 + 900;

        // ------------------------------------------------------------------ Gegner
        public const string EnemyRooter = "enemy.furrow_rooter";       // Furchenwühler (neutral, 1–3)
        public const string EnemyClacker = "enemy.shore_clacker";      // Uferklapper (neutral, wachsam, 2–4)
        public const string EnemySlinger = "enemy.silt_slinger";       // Schlickwerfer (territorial, 5–6, Schlickwurf)
        public const string EnemyBellows = "enemy.whisper_bellows";    // Flüsterbalg (neutral; am Nest territorial)
        public const string EnemyReedDeserter = "enemy.reed_deserter"; // Hungriger Deserteur (U-7)
        public const string EnemyWreckSailor = "enemy.wreck_sailor";   // Ertrunkener Matrose am Wrack (U-3, normaler Gegner)
        // vorhandene Gegner
        public const string EnemyWolf = "enemy.ash_wolf";
        public const string EnemyFaded = "enemy.faded_one";
        public const string EnemyDeserter = "enemy.deserter";
        public const string EnemyDeserterArcher = "enemy.deserter_gunner";

        // ------------------------------------------------------------------ Gegenstände (Auftrag)
        public const string ItemJug = "item.k28_ebbas_jug";             // U-2, fällt beim Schlickwerfer
        public const string ItemLogPage = "item.k28_wreck_log_page";    // U-3, fällt bei den Matrosen am Wrack
        public const string ItemGifts = "item.k28_pilgrim_gifts";       // U-7, beim hungrigen Deserteur

        // ------------------------------------------------------------------ Erkundungsziele (QuestArea)
        public const string AreaWoodYard = "area.kareth.holzplatz";
        public const string AreaWagon = "area.kareth.tamos_wagen";
        public const string AreaWreck = "area.kareth.wrack";
        public const string AreaTrail = "area.kareth.hofspur";
        public const string AreaRestingPlace = "area.kareth.rastplatz";
        public const string AreaHideout = "area.kareth.versteck";
        public const int SignStoneCount = 5;
        public static string AreaSignStone(int i) => "area.kareth.zeichenstein" + (i + 1);

        /// <summary>Alle Erkundungsziele der elf Aufträge (Tests, Weltprüfung).</summary>
        public static string[] Areas()
        {
            var l = new System.Collections.Generic.List<string> { AreaWoodYard, AreaWagon, AreaWreck, AreaTrail, AreaRestingPlace, AreaHideout };
            for (int i = 0; i < SignStoneCount; i++) l.Add(AreaSignStone(i));
            return l.ToArray();
        }
    }
}
