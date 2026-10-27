using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;
using KC = Ashveil.Data.KarethCatalog;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// #28 Kareth v4, Welle 0: die elf Ergänzungsaufträge gegen die EP-Lücke (Auftragsliste, Max 05.10.: „Ja, so bauen“; Geber,
    /// Gasthaus „Zur Schale“, Ebba Salweg und U-7 „Die verschwundenen Gaben“: Max 07.10. abends). Stufen, Orte, Ziele und EP nach
    /// der Liste; Geld, Belohnungen und Texte sind Vorschläge (Claude, Bauplan #28 v2).
    ///   K-A Die Vorratsgruben (Wenna, 1) · K-B Die Wasserstelle (Wenna, 2) · K-C Der Waldrand (Jorn, 2) · K-D Die ausgebliebene
    ///   Holzlieferung (Ulf, 4) · U-1 Tamos Wagen (Tamo, 5, nach K-D) · U-2 Ein Krug für die Schale (Ebba → Oswald → Ebba, 5) ·
    ///   U-3 Das Wrack im Salz (Kartograf, 6, nach Q3) · U-4 Arbeitszeichen (Veit, 7) · U-5 Die Zeichensteine (Veit, 8, nach U-4) ·
    ///   U-6 Die leeren Höfe (Bralt, 9) · U-7 Die verschwundenen Gaben (Edo, 7, unmarkiert – kein „!“, nur im Gespräch).
    /// Alle elf gelten nur in Szenen mit dem Kennzeichen <see cref="KarethCatalog.SceneTag"/> (Kareth v4): Ihre Orte und Figuren
    /// stehen nur dort, in der Sandbox werden sie nicht vergeben. Texte aus Resources/Text/de/kareth.txt (Präfix = textKey); die
    /// Assets tragen Struktur und eine Kopie der Texte. Wie die Glockenrast werden sie bei jedem Aufbau neu geschrieben.
    /// </summary>
    public static partial class QuestContentBuilder
    {
        private const string KaKey = "quest.kareth.";

        public static void BuildKarethQuests(QuestDefinition q1, QuestDefinition q3)
        {
            ProjectSetup.EnsureFolder(QuestFolder);
            ProjectSetup.EnsureFolder(NpcFolder);
            Loc.Reload();
            bool keep = _refresh;
            _refresh = true;
            try
            {
                // ---------------------------------------------------------- Figuren (Wenna, Jorn, Ulf, Oswald werden Auftragsfiguren; Ebba neu)
                GrNpc("Npc_Wenna", KC.Wenna, "npc.wenna");
                GrNpc("Npc_Jorn", KC.Jorn, "npc.jorn");
                GrNpc("Npc_Ulf", KC.Ulf, "npc.ulf");
                GrNpc("Npc_Oswald", KC.Oswald, "npc.oswald", KC.FlagJugReturned);
                GrNpc("Npc_EbbaSalweg", KC.Ebba, "npc.ebba_salweg", KC.FlagJugReturned);
                // Untersteg beleben (Max 10.10. abends, Sheet v1): Krämerin Idun Vesper – Händlerin (Verbrauchsgüter, Taschen, Pfeile; kein Auftrag)
                // 21.10. (FORTSCHRITT-03): keine Leinenfetzen mehr – N04 „Leinen für Verbände“ ließ sich sonst für 16 Kupfer kaufen statt sammeln
                Npc("Npc_IdunVesper", KC.Idun, "Krämerin Idun Vesper", "Kram und Tuch",
                    "Brauchst du was? Dann sag's. Brauchst du nichts? Dann kauf trotzdem was, die Salzstraße fährt nicht mehr.", true,
                    (I("healing_draught"), 1, 0), (I("dry_bread"), 1, 0), (I("ash_tea"), 1, 0),
                    (I("rough_arrow"), 200, 20), (I("linen_bag"), 1, 0), (I("rope_belt"), 1, 0), (I("travel_hood"), 1, 0));

                // ---------------------------------------------------------- Stufe 1–5: Wachtfeuer-Mulde
                QuestDefinition ka = KaQuest("Quest_KA_DieVorratsgruben", KC.KA, "ka", KC.Wenna, KC.Wenna, 1, 250, 100, P(q1),
                    new List<QuestObjective> { KaObj(QuestObjectiveType.Kill, KC.EnemyRooter, 6, "obj.rooters") },
                    q => q.rewardItems = new List<QuestItemReward> { R("k28_studded_girdle"), R("k28_knife_loop_belt"), R("k28_braided_cord") });

                QuestDefinition kb = KaQuest("Quest_KB_DieWasserstelle", KC.KB, "kb", KC.Wenna, KC.Wenna, 2, 450, 150, P(ka),
                    new List<QuestObjective> { KaObj(QuestObjectiveType.Kill, KC.EnemyClacker, 8, "obj.clackers") },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 2) });

                KaQuest("Quest_KC_DerWaldrand", KC.KC, "kc", KC.Jorn, KC.Jorn, 2, 450, 150, P(q1),
                    new List<QuestObjective> { KaObj(QuestObjectiveType.Kill, KC.EnemyBellows, 6, "obj.bellows") },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 2) });

                QuestDefinition kd = KaQuest("Quest_KD_DieAusgebliebeneHolzlieferung", KC.KD, "kd", KC.Ulf, KC.Ulf, 4, 700, 250, P(q1),
                    new List<QuestObjective>
                    {
                        KaObj(QuestObjectiveType.Explore, KC.AreaWoodYard, 1, "obj.woodyard"),
                        KaObj(QuestObjectiveType.Kill, KC.EnemyWolf, 6, "obj.wolves")
                    },
                    q => q.choiceItems = new List<QuestItemReward> { R("k28_yard_sword"), R("k28_yard_dagger"), R("k28_yard_staff"), R("k28_yard_bow") });

                // ---------------------------------------------------------- Stufe 5–10: Untersteg und die Ringe
                KaQuest("Quest_U1_TamosWagen", KC.U1, "u1", KC.Tamo, KC.Tamo, 5, 900, 300, P(kd),
                    new List<QuestObjective>
                    {
                        KaObj(QuestObjectiveType.Kill, KC.EnemyBellows, 6, "obj.nest"),
                        KaObj(QuestObjectiveType.Explore, KC.AreaWagon, 1, "obj.wagon")
                    },
                    q => q.rewardItems = new List<QuestItemReward> { R("k28_wagon_plate_cuirass"), R("k28_tarp_leather_jerkin"), R("k28_wagon_canvas_smock") });

                KaQuest("Quest_U2_EinKrugFuerDieSchale", KC.U2, "u2", KC.Ebba, KC.Ebba, 5, 700, 250, null,
                    new List<QuestObjective>
                    {
                        KaObj(QuestObjectiveType.TalkTo, KC.Oswald, 1, "obj.oswald"),
                        KaObj(QuestObjectiveType.Kill, KC.EnemySlinger, 6, "obj.slingers"),
                        KaObj(QuestObjectiveType.Collect, KC.ItemJug, 1, "obj.jug")
                    },
                    q =>
                    {
                        q.rewardItems = new List<QuestItemReward> { R("ash_tea", 2) };   // „ein Getränk aufs Haus“ (vorhandener Verbrauchsgegenstand)
                        q.grantsFlags = new List<string> { KC.FlagJugReturned };
                    });

                KaQuest("Quest_U3_DasWrackImSalz", KC.U3, "u3", KC.Cartographer, KC.Cartographer, 6, 1000, 350, P(q3),
                    new List<QuestObjective>
                    {
                        KaObj(QuestObjectiveType.Explore, KC.AreaWreck, 1, "obj.wreck"),
                        KaObj(QuestObjectiveType.Kill, KC.EnemyWreckSailor, 6, "obj.sailors"),
                        KaObj(QuestObjectiveType.Collect, KC.ItemLogPage, 1, "obj.logpage")
                    });

                QuestDefinition u4 = KaQuest("Quest_U4_Arbeitszeichen", KC.U4, "u4", KC.Veit, KC.Veit, 7, 1000, 350, null,
                    new List<QuestObjective> { KaObj(QuestObjectiveType.Kill, KC.EnemyFaded, 8, "obj.faded") },
                    q => q.choiceItems = new List<QuestItemReward> { R("k28_workshop_sword"), R("k28_workshop_dagger"), R("k28_workshop_staff"), R("k28_workshop_bow") });

                var stones = new List<QuestObjective>();
                for (int i = 0; i < KC.SignStoneCount; i++) stones.Add(KaObj(QuestObjectiveType.Explore, KC.AreaSignStone(i), 1, "obj.stone" + (i + 1)));
                QuestDefinition u5 = KaQuest("Quest_U5_DieZeichensteine", KC.U5, "u5", KC.Veit, KC.Veit, 8, 1000, 300, P(u4), stones,
                    q => q.rewardItems = new List<QuestItemReward> { R("k28_sign_stone_pendant") });

                KaQuest("Quest_U6_DieLeerenHoefe", KC.U6, "u6", KC.Bralt, KC.Bralt, 9, 1100, 450, null,
                    new List<QuestObjective>
                    {
                        KaObj(QuestObjectiveType.Kill, KC.EnemyDeserter, 5, "obj.deserters"),
                        KaObj(QuestObjectiveType.Kill, KC.EnemyDeserterArcher, 3, "obj.archers"),
                        KaObj(QuestObjectiveType.Explore, KC.AreaTrail, 1, "obj.trail")
                    },
                    q => q.rewardItems = new List<QuestItemReward> { R("k28_watch_greaves"), R("k28_scout_breeches"), R("k28_loden_trousers") });

                KaQuest("Quest_U7_DieVerschwundenenGaben", KC.U7, "u7", KC.Pilgrim, KC.Pilgrim, 7, 900, 0, null,
                    new List<QuestObjective>
                    {
                        KaObj(QuestObjectiveType.Explore, KC.AreaRestingPlace, 1, "obj.restingplace"),
                        KaObj(QuestObjectiveType.Explore, KC.AreaHideout, 1, "obj.hideout"),
                        KaObj(QuestObjectiveType.Collect, KC.ItemGifts, 1, "obj.gifts")
                    },
                    q =>
                    {
                        q.unmarked = true;   // Quest Design Bible §4, Stufe C: kein „!“ über Edo – man erfährt davon nur im Gespräch
                        q.rewardItems = new List<QuestItemReward> { R("k28_resting_pebble") };
                    });

                (QuestDefinition n06, QuestDefinition n08) = BuildKarethNeben();
                BuildKarethClassic(kb, u5, n06, n08);
            }
            finally
            {
                _refresh = keep;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Ashveil] Kareth v4 (#28): 11 Ergänzungsaufträge, 5 Figuren, {KC.QuestXpTotal} EP aus Aufträgen (nur in Szenen „{KC.SceneTag}“).");
        }

        // ================================================================== Nebenaufträge (Max 10.10.2026 spät)

        /// <summary>
        /// Fünfzehn einfache Nebenaufträge zum Hochspielen (Max: „Classic: viele einmalige“, „etwa 15“, Texte ohne Freigabe, vorhandene Gegner
        /// und wenige neue Lager). Jeder führt zu einem Gegnergebiet aus <see cref="KarethV4Gegner"/>, dessen Stufe passt; Sammelziele sind
        /// Beute, die diese Gegner ohnehin fallen lassen (Beutetabellen in ItemContentBuilder). Neue Lager: Furchenwühler in der Feldmark,
        /// Uferklapper im Muldenbach, Aschwölfe an den Heuwiesen, Deserteure über der Mühle. EP ≈ 0,75 der Ergänzungsaufträge.
        /// Auftraggeber: Figuren, die es schon gibt (Grete und Hennek werden Auftragsfiguren, Sheets v1).
        /// </summary>
        private static (QuestDefinition n06, QuestDefinition n08) BuildKarethNeben()
        {
            GrNpc("Npc_GreteBachmann", KC.Grete, "npc.untersteg.grete");
            GrNpc("Npc_HennekDoll", KC.Hennek, "npc.untersteg.hennek");
            const string Wolf = "enemy.ash_wolf", Gunner = "enemy.faded_gunner", Brute = "enemy.peat_brute", Spitter = "enemy.needle_spitter";

            // ---------------------------------------------------------- Stufe 2–4: Mulde, Feldmark, Untersteg
            QuestDefinition n01 = KaQuest("Quest_N01_WuehlerImKorn", KC.N01, "n01", KC.Grete, KC.Grete, 2, 300, 120, null,
                new List<QuestObjective> { KaObj(QuestObjectiveType.Kill, KC.EnemyRooter, 8, "obj.n01") },
                q => q.rewardItems = new List<QuestItemReward> { R("dry_bread", 3) });
            QuestDefinition n02 = KaQuest("Quest_N02_Wolfszaehne", KC.N02, "n02", Gerrit, Gerrit, 2, 300, 120, null,
                new List<QuestObjective> { KaObj(QuestObjectiveType.Collect, "item.wolf_fang", 6, "obj.n02") },
                q => q.rewardItems = new List<QuestItemReward> { R("rough_arrow", 100) });
            QuestDefinition n03 = KaQuest("Quest_N03_KlapperAmBach", KC.N03, "n03", KC.Hennek, KC.Hennek, 3, 380, 140, null,
                new List<QuestObjective> { KaObj(QuestObjectiveType.Kill, KC.EnemyClacker, 8, "obj.n03") },
                q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 2) });
            KaQuest("Quest_N04_Leinen", KC.N04, "n04", Maelis, Maelis, 3, 380, 140, null,
                new List<QuestObjective> { KaObj(QuestObjectiveType.Collect, "item.linen_scraps", 6, "obj.n04") },
                q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 2) });
            KaQuest("Quest_N05_Schuetzinnen", KC.N05, "n05", Bralt, Bralt, 4, 450, 180, null,
                new List<QuestObjective> { KaObj(QuestObjectiveType.Kill, Gunner, 4, "obj.n05") },
                q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 2), R("ash_tea", 2) });

            // ---------------------------------------------------------- Stufe 5–7: Speiergrund, Kuppeln, Heuwiesen, Salzufer, Mühle
            QuestDefinition n06 = KaQuest("Quest_N06_Nadeln", KC.N06, "n06", Gerrit, Gerrit, 5, 520, 200, P(n02),
                new List<QuestObjective> { KaObj(QuestObjectiveType.Collect, "item.spine_needle", 12, "obj.n06") },
                q => q.rewardItems = new List<QuestItemReward> { R("rough_arrow", 200) });
            KaQuest("Quest_N07_Torf", KC.N07, "n07", KC.Ebba, KC.Ebba, 6, 600, 220, null,
                new List<QuestObjective> { KaObj(QuestObjectiveType.Collect, "item.peat_clump", 8, "obj.n07") },
                q => q.rewardItems = new List<QuestItemReward> { R("ash_tea", 3), R("dry_bread", 3) });
            QuestDefinition n08 = KaQuest("Quest_N08_Heuwiesen", KC.N08, "n08", KC.Hennek, KC.Hennek, 6, 600, 220, P(n03),
                new List<QuestObjective> { KaObj(QuestObjectiveType.Kill, Wolf, 8, "obj.n08") },
                q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 3) });
            KaQuest("Quest_N09_SchlickAmDamm", KC.N09, "n09", KC.Oswald, KC.Oswald, 6, 600, 220, null,
                new List<QuestObjective> { KaObj(QuestObjectiveType.Kill, KC.EnemySlinger, 10, "obj.n09") },
                q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 2) });
            QuestDefinition n10 = KaQuest("Quest_N10_Chitin", KC.N10, "n10", Dorn, Dorn, 6, 600, 220, null,
                new List<QuestObjective> { KaObj(QuestObjectiveType.Collect, "item.chitin_plate", 6, "obj.n10") },
                q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 2) });
            KaQuest("Quest_N11_Mehldiebe", KC.N11, "n11", KC.Grete, KC.Grete, 7, 680, 260, P(n01),
                new List<QuestObjective>
                {
                    KaObj(QuestObjectiveType.Kill, KC.EnemyDeserter, 5, "obj.n11a"),
                    KaObj(QuestObjectiveType.Kill, KC.EnemyDeserterArcher, 2, "obj.n11b")
                },
                q => q.rewardItems = new List<QuestItemReward> { R("dry_bread", 5), R("healing_draught", 2) });

            // ---------------------------------------------------------- Stufe 8–10: Werkstätten, Nordmoor
            KaQuest("Quest_N12_Grabsalz", KC.N12, "n12", KC.Veit, KC.Veit, 8, 750, 280, null,
                new List<QuestObjective> { KaObj(QuestObjectiveType.Collect, "item.grave_salt", 5, "obj.n12") },
                q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 3) });
            KaQuest("Quest_N13_SchwereSchrate", KC.N13, "n13", Bralt, Bralt, 8, 750, 280, null,
                new List<QuestObjective> { KaObj(QuestObjectiveType.Kill, Brute, 5, "obj.n13") },
                q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 3) });
            KaQuest("Quest_N14_Breitkopf", KC.N14, "n14", Maelis, Maelis, 9, 750, 280, null,
                new List<QuestObjective> { KaObj(QuestObjectiveType.Kill, Spitter, 8, "obj.n14") },
                q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 3) });
            KaQuest("Quest_N15_Altmetall", KC.N15, "n15", Dorn, Dorn, 9, 750, 300, P(n10),
                new List<QuestObjective> { KaObj(QuestObjectiveType.Collect, "item.rusted_iron_scrap", 8, "obj.n15") },
                q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 3) });
            Debug.Log($"[Ashveil] Kareth v4 Nebenaufträge: {KC.NebenQuests.Length} Aufträge, {KC.NebenXpTotal} EP (Grete, Hennek, Gerrit, Maelis, Bralt, Dorn, Ebba, Oswald, Veit).");
            return (n06, n08);
        }

        // ================================================================== Classic-Paket (Max 11.10.2026 abends)

        /// <summary>
        /// Classic-Bauplan §5 (Max 11.10.2026 abends, alle vier Punkte freigegeben): In Classic sind zwei Drittel aller Aufträge Wege –
        /// Botengänge, Hinweise auf das nächste Lager, „Hol den Kopf von …“ –, nur jeder neunte ist reines Töten (Emulator-Datenbank,
        /// nur Zahlen). Kareth hatte das Verhältnis umgekehrt. Dazu:
        ///   · acht Botengänge B1–B8 (Gespräch beim Abgabe-NPC, niedrige EP): Wachtfeuer → Brücke → Schale → Wagen, Karte → Glockenmoor (Runa),
        ///     Palisade → Waldrand, Mühle → Damm, Lazarett → Mühle; B7 verlängert die Kette U-4 → U-5
        ///   · drei „Kopf von …“-Aufträge H1–H3 auf seltene Tiere, je als letzter Schritt einer Kette (K-A → K-B → H1, N03 → N08 → H2,
        ///     N02 → N06 → H3) – damit sind die Ketten im Mittel 2–3 Glieder lang wie in Classic.
        /// Texte aus kareth.txt (VORSCHLAG Claude nach den Sheets), Geber und Abgabe sind vorhandene Figuren.
        /// </summary>
        private static void BuildKarethClassic(QuestDefinition kb, QuestDefinition u5, QuestDefinition n06, QuestDefinition n08)
        {
            QuestObjective Talk(string npc, string key) => KaObj(QuestObjectiveType.TalkTo, npc, 1, key);

            // ---------------------------------------------------------- Botengänge
            QuestDefinition b1 = KaQuest("Quest_B1_MeldungAnDieBruecke", KC.B1, "b1", Bralt, KC.Hennek, 4, 150, 40, null,
                new List<QuestObjective> { Talk(KC.Hennek, "obj.b1") });
            QuestDefinition b2 = KaQuest("Quest_B2_EinBettInDerSchale", KC.B2, "b2", KC.Hennek, KC.Ebba, 4, 120, 30, P(b1),
                new List<QuestObjective> { Talk(KC.Ebba, "obj.b2") });
            KaQuest("Quest_B3_TamosDeckel", KC.B3, "b3", KC.Ebba, KC.Tamo, 5, 150, 40, P(b2),
                new List<QuestObjective> { Talk(KC.Tamo, "obj.b3") });
            KaQuest("Quest_B4_DerBohlenwegAufDerKarte", KC.B4, "b4", KC.Cartographer, Ashveil.Data.GlockenrastCatalog.Runa, 6, 200, 60, null,
                new List<QuestObjective> { Talk(Ashveil.Data.GlockenrastCatalog.Runa, "obj.b4") });
            KaQuest("Quest_B5_PfaehleFuerUlf", KC.B5, "b5", KC.Ulf, KC.Jorn, 2, 80, 20, null,
                new List<QuestObjective> { Talk(KC.Jorn, "obj.b5") });
            KaQuest("Quest_B6_MehlFuerDenFuhrmann", KC.B6, "b6", KC.Grete, KC.Oswald, 6, 180, 50, null,
                new List<QuestObjective> { Talk(KC.Oswald, "obj.b6") },
                q => q.rewardItems = new List<QuestItemReward> { R("dry_bread", 2) });
            KaQuest("Quest_B7_AbriebFuerDenKartografen", KC.B7, "b7", KC.Veit, KC.Cartographer, 8, 300, 80, P(u5),
                new List<QuestObjective> { Talk(KC.Cartographer, "obj.b7") });
            KaQuest("Quest_B8_KraeuterGegenMehl", KC.B8, "b8", Maelis, KC.Grete, 5, 150, 40, null,
                new List<QuestObjective> { Talk(KC.Grete, "obj.b8") },
                q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 1) });

            // ---------------------------------------------------------- „Kopf von …“ (seltene Tiere)
            KaQuest("Quest_H1_Altborste", KC.H1, "h1", KC.Wenna, KC.Wenna, 4, 600, 200, P(kb),
                new List<QuestObjective> { KaObj(QuestObjectiveType.Collect, KC.ItemAltborsteTusk, 1, "obj.h1") },
                q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 3), R("dry_bread", 5) });
            KaQuest("Quest_H2_Narbenfang", KC.H2, "h2", KC.Hennek, KC.Hennek, 7, 1000, 350, P(n08),
                new List<QuestObjective> { KaObj(QuestObjectiveType.Collect, KC.ItemNarbenfangEar, 1, "obj.h2") },
                q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 4) });
            KaQuest("Quest_H3_Krummnadel", KC.H3, "h3", Gerrit, Gerrit, 9, 1300, 400, P(n06),
                new List<QuestObjective> { KaObj(QuestObjectiveType.Collect, KC.ItemKrummnadelSpine, 1, "obj.h3") },
                q => q.rewardItems = new List<QuestItemReward> { R("rough_arrow", 400) });
            Debug.Log($"[Ashveil] Kareth v4 Classic-Paket: {KC.ClassicQuests.Length} Aufträge (8 Botengänge, 3 „Kopf von …“), {KC.ClassicXpTotal} EP.");
        }

        // ================================================================== Hilfen

        private static QuestDefinition[] P(QuestDefinition q) => q != null ? new[] { q } : null;

        private static QuestObjective KaObj(QuestObjectiveType type, string target, int count, string key) =>
            new QuestObjective { type = type, targetId = target, count = count, label = Loc.T(KaKey + key), labelKey = KaKey + key };

        private static QuestDefinition KaQuest(string file, string id, string key, string giver, string turnIn, int minLevel, int xp, int money,
            QuestDefinition[] prerequisites, List<QuestObjective> objectives, System.Action<QuestDefinition> extra = null)
        {
            string tk = KaKey + key;
            return Quest(file, id, Loc.T(tk + ".name"), q =>
            {
                q.textKey = tk;
                q.giverNpc = giver;
                q.turnInNpc = turnIn;
                q.minLevel = minLevel;
                q.prerequisites = prerequisites != null ? new List<QuestDefinition>(prerequisites) : new List<QuestDefinition>();
                q.requiredRace = "";
                q.requiredRole = ClassRole.None;
                q.requiredClass = "";
                q.requiredFlags = new List<string>();
                q.offeredByStory = false;
                q.unmarked = false;
                q.requiredSceneTag = KC.SceneTag;
                q.grantsFlags = new List<string>();
                q.offerText = Loc.T(tk + ".offer");
                q.progressText = Loc.T(tk + ".progress");
                q.completeText = Loc.T(tk + ".complete");
                q.summary = Loc.T(tk + ".summary");
                q.objectives = objectives;
                q.startItems = new List<QuestItemReward>();
                q.xp = xp;
                q.money = money;
                q.rewardItems = new List<QuestItemReward>();
                q.choiceItems = new List<QuestItemReward>();
                extra?.Invoke(q);
                if (q.prerequisites.Contains(null)) Debug.LogWarning($"[Ashveil] Kareth (#28): {id} hat eine fehlende Voraussetzung.");
            });
        }
    }
}
