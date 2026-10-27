using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;
using C = Ashveil.Data.GlockenrastCatalog;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// P3 Glockenrast (Stufe 5–9), freigegeben von Max am 30.09.2026: dreizehn Auftraege G1–G13 und die Figuren dazu.
    ///   Regionale Geschichte „Was die Glocken nicht sagen“: G1, G8, G10, G11 (Anselm, Runa)
    ///   Figurengeschichte „Harz und Bronze“: G5, G6, G10, G12, G13 (Merle, Jasko)
    ///   Weltauftraege: G2/G3 (Moorrand), G4 (Tamo), G7 (Pilger/Veit), G9 (Aschenweide)
    /// Kette: G1 → G2, G4, G5, G7, G8 · G2 → G3, G9 · G5 → G6 · G6 + G8 → G10 → G11 → G12 → G13.
    /// Texte kommen aus der Texttabelle (Resources/Text/de/glockenrast.txt, Praefix = textKey); die Assets tragen Struktur und
    /// eine Kopie der Texte (Inspector). Wie die Tank-Klassenquests werden sie bei jedem Aufbau neu geschrieben.
    /// Ende (G13): Geschichtsmarken fuer Runa (Schluessel), Anselm (betet laut) und Merle/Jasko (getrennte Enden) – sichtbar
    /// ueber den Gruss je Marke (NpcDefinition.greetingFlags) und Phasing-Requisiten in der Szene.
    /// </summary>
    public static partial class QuestContentBuilder
    {
        private const string GrKey = "quest.glockenrast.";

        public static void BuildGlockenrastQuests()
        {
            ProjectSetup.EnsureFolder(QuestFolder);
            ProjectSetup.EnsureFolder(NpcFolder);
            Loc.Reload();
            bool keep = _refresh;
            _refresh = true;
            try
            {
                // ---------------------------------------------------------- Figuren
                GrNpc("Npc_Gr_Anselm", C.Anselm, "npc.glockenrast.anselm", C.FlagConfrontation, C.FlagHomecoming);
                GrNpc("Npc_Gr_Runa", C.Runa, "npc.glockenrast.runa", C.FlagConfrontation, C.FlagHomecoming);
                GrNpc("Npc_Gr_Merle", C.Merle, "npc.glockenrast.merle", C.FlagHomecoming);
                GrNpc("Npc_Gr_Jasko", C.Jasko, "npc.glockenrast.jasko", C.FlagHomecoming);
                GrNpc("Npc_Gr_Pilger", C.Pilgrim, "npc.glockenrast.pilger");
                GrNpc("Npc_Tamo", C.Tamo, "npc.tamo");
                GrNpc("Npc_Veit", C.Veit, "npc.veit");

                // ---------------------------------------------------------- Auftraege
                QuestDefinition g1 = GrQuest("Quest_G01_DerWegNachNorden", C.G1, "g1", C.Bralt, C.Anselm, 5, 400, 0, null,
                    new List<QuestObjective> { GrObj(QuestObjectiveType.TalkTo, C.Anselm, 1, "obj.anselm") });

                QuestDefinition g2 = GrQuest("Quest_G02_NadelnImSchilf", C.G2, "g2", C.Runa, C.Runa, 5, 700, 350, new[] { g1 },
                    new List<QuestObjective> { GrObj(QuestObjectiveType.Kill, C.EnemySpitter, 8, "obj.spitters") });

                GrQuest("Quest_G03_Gegengift", C.G3, "g3", C.Runa, C.Maelis, 5, 650, 0, new[] { g2 },
                    new List<QuestObjective> { GrObj(QuestObjectiveType.Collect, C.ItemVenomGland, 6, "obj.glands") },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 3) });

                GrQuest("Quest_G04_EinRadFuerTamo", C.G4, "g4", C.Tamo, C.Tamo, 5, 700, 400, new[] { g1 },
                    new List<QuestObjective>
                    {
                        GrObj(QuestObjectiveType.Explore, C.AreaLumberCamp, 1, "obj.lumbercamp"),
                        GrObj(QuestObjectiveType.Collect, C.ItemSpareWheel, 1, "obj.wheel")
                    },
                    q => q.rewardItems = new List<QuestItemReward> { R("wool_bag") });

                QuestDefinition g5 = GrQuest("Quest_G05_DasLeereLager", C.G5, "g5", C.Merle, C.Merle, 6, 900, 0, new[] { g1 },
                    new List<QuestObjective> { GrObj(QuestObjectiveType.Kill, C.EnemyFaded, 6, "obj.faded") },
                    q => q.choiceItems = new List<QuestItemReward> { R("gr_camp_sword"), R("gr_camp_dagger"), R("gr_camp_staff"), R("gr_camp_bow") });

                QuestDefinition g6 = GrQuest("Quest_G06_HarzOhneHaende", C.G6, "g6", C.Merle, C.Merle, 6, 900, 0, new[] { g5 },
                    new List<QuestObjective>
                    {
                        GrObj(QuestObjectiveType.Explore, C.AreaResinCamp, 1, "obj.resincamp"),
                        GrObj(QuestObjectiveType.Collect, C.ItemLetter, 1, "obj.letter"),
                        GrObj(QuestObjectiveType.Collect, C.ItemLedger, 1, "obj.ledger")
                    });

                GrQuest("Quest_G07_DieSchaedeldecke", C.G7, "g7", C.Pilgrim, C.Veit, 6, 800, 0, new[] { g1 },
                    new List<QuestObjective>
                    {
                        GrObj(QuestObjectiveType.Explore, C.AreaSkullcap, 1, "obj.skullcap"),
                        GrObj(QuestObjectiveType.Collect, C.ItemRubbing, C.RubbingCount, "obj.rubbings")
                    },
                    q =>
                    {
                        q.rewardItems = new List<QuestItemReward> { R("gr_settler_ring") };
                        q.unmarked = true;   // Stufe C (Max 10.10.2026): kein „!“ über Edo – wie U-7 erzählt er nur dem, der ihn anspricht
                    });

                QuestDefinition g8 = GrQuest("Quest_G08_WasManUnterWasserFindet", C.G8, "g8", C.Anselm, C.Anselm, 7, 1000, 0, new[] { g1 },
                    new List<QuestObjective> { GrObj(QuestObjectiveType.Collect, C.ItemBellFragment, C.FragmentCount, "obj.fragments") },
                    q => q.rewardItems = new List<QuestItemReward> { R("gr_founder_cloak") });

                GrQuest("Quest_G09_TorfUndZorn", C.G9, "g9", C.Runa, C.Runa, 7, 1100, 0, new[] { g2 },
                    new List<QuestObjective> { GrObj(QuestObjectiveType.Kill, C.EnemyPeatBrute, 5, "obj.brutes") },
                    q => q.choiceItems = new List<QuestItemReward>
                    {
                        R("gr_moorwarden_gauntlets"), R("gr_moorwarden_boots"), R("gr_peatweaver_robe"), R("gr_moor_coif")
                    });

                QuestDefinition g10 = GrQuest("Quest_G10_DasZeichenImBuch", C.G10, "g10", C.Merle, C.Runa, 8, 900, 0, new[] { g6, g8 },
                    new List<QuestObjective>
                    {
                        GrObj(QuestObjectiveType.TalkTo, C.Anselm, 1, "obj.confront"),
                        GrObj(QuestObjectiveType.TalkTo, C.Runa, 1, "obj.tellruna")
                    },
                    q => q.grantsFlags = new List<string> { C.FlagConfrontation });

                QuestDefinition g11 = GrQuest("Quest_G11_DerSchweigendeTurm", C.G11, "g11", C.Runa, C.Runa, 8, 1200, 0, new[] { g10 },
                    new List<QuestObjective>
                    {
                        GrObj(QuestObjectiveType.Explore, C.AreaBellTower, 1, "obj.tower"),
                        GrObj(QuestObjectiveType.Story, C.StoryBellStruck, 1, "obj.strike")
                    });

                QuestDefinition g12 = GrQuest("Quest_G12_Nordmoor", C.G12, "g12", C.Runa, C.Jasko, 9, 1400, 600, new[] { g11 },
                    new List<QuestObjective>
                    {
                        GrObj(QuestObjectiveType.Explore, C.AreaNorthMoor, 1, "obj.northmoor"),
                        GrObj(QuestObjectiveType.Kill, C.EnemyPeatBrute, 3, "obj.moorbrutes"),
                        GrObj(QuestObjectiveType.Kill, C.EnemySpitter, 3, "obj.moorspitters"),
                        GrObj(QuestObjectiveType.Story, C.StoryJaskoFound, 1, "obj.jasko")
                    });

                GrQuest("Quest_G13_KommstDuHeuteNachtNachHause", C.G13, "g13", C.Jasko, C.Merle, 9, 1000, 0, new[] { g12 },
                    new List<QuestObjective> { GrObj(QuestObjectiveType.TalkTo, C.Merle, 1, "obj.merle") },
                    q =>
                    {
                        q.choiceItems = new List<QuestItemReward>
                        {
                            R("gr_moor_sword"), R("gr_moor_dagger"), R("gr_moor_staff"), R("gr_moor_bow"), R("gr_resin_pendant")
                        };
                        // Ende: Runa haelt den Schluessel, Anselm betet laut, Merle und Jasko an entgegengesetzten Enden
                        q.grantsFlags = new List<string> { C.FlagHomecoming, C.FlagRunaHoldsKey, C.FlagAnselmPraysAloud, C.FlagSeparateEnds };
                    });
            }
            finally
            {
                _refresh = keep;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Ashveil] Glockenrast (P3): 13 Auftraege, 7 Figuren, {C.QuestXpTotal} EP aus Auftraegen.");
        }

        // ================================================================== Hilfen

        private static void GrNpc(string file, string id, string textKey, params string[] greetingFlags)
        {
            Ensure<NpcDefinition>(NpcFolder, file, id, Loc.T(textKey + ".name"), n =>
            {
                n.textKey = textKey;
                n.title = Loc.T(textKey + ".title");
                n.greeting = Loc.T(textKey + ".greeting");
                n.vendor = false;
                n.interactRange = 5f;
                n.stock = new List<VendorEntry>();
                n.greetingFlags = new List<string>(greetingFlags);
            });
        }

        private static QuestObjective GrObj(QuestObjectiveType type, string target, int count, string key) =>
            new QuestObjective { type = type, targetId = target, count = count, label = Loc.T(GrKey + key), labelKey = GrKey + key };

        private static QuestDefinition GrQuest(string file, string id, string key, string giver, string turnIn, int minLevel, int xp, int money,
            QuestDefinition[] prerequisites, List<QuestObjective> objectives, System.Action<QuestDefinition> extra = null)
        {
            string tk = GrKey + key;
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
                if (q.prerequisites.Contains(null)) Debug.LogWarning($"[Ashveil] Glockenrast: {id} hat eine fehlende Voraussetzung.");
            });
        }
    }
}
