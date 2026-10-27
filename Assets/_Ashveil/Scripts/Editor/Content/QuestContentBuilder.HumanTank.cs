using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Tank-Klassenquests Mensch (Vorgabe Max 29.09.): „Wo die Herde floh“ und „Der letzte Weidegang“ plus der Pferdezuechter.
    /// Texte kommen aus der Texttabelle (Resources/Text/de/pferdehof.txt, Praefix = textKey); die Assets tragen nur Struktur.
    /// Meilensteine: die Einfuehrung am Wachtfeuer und das Salzgruben-Finale setzen Geschichtsmarken (grantsFlags).
    /// </summary>
    public static partial class QuestContentBuilder
    {
        public const string Breeder = HorseStoryCatalog.BreederNpcId;

        public static void BuildHumanTankQuests()
        {
            ProjectSetup.EnsureFolder(QuestFolder);
            ProjectSetup.EnsureFolder(NpcFolder);
            Loc.Reload();
            bool keep = _refresh;
            _refresh = true;
            try
            {
                Ensure<NpcDefinition>(NpcFolder, "Npc_Pferdezuechter", Breeder, Loc.T("npc.human.horsebreeder_01.name"), n =>
                {
                    n.textKey = "npc.human.horsebreeder_01";
                    n.title = Loc.T("npc.human.horsebreeder_01.title");
                    n.greeting = Loc.T("npc.human.horsebreeder_01.greeting");
                    n.vendor = false;
                    n.interactRange = 40f; // er kommt auf dich zu – die Pruefung laeuft gegen seinen Hofplatz (Hofradius)
                    n.stock = new List<VendorEntry>();
                });

                QuestDefinition q1 = Quest("Quest_HT1_WoDieHerdeFloh", HorseStoryCatalog.FoalRescueQuest, Loc.T("quest.human_tank.foal_rescue.name"), q =>
                {
                    q.textKey = "quest.human_tank.foal_rescue";
                    q.giverNpc = Breeder;
                    q.turnInNpc = Breeder;
                    q.minLevel = HorseStoryCatalog.FoalRescueMinLevel;
                    q.prerequisites = new List<QuestDefinition>();
                    q.requiredRace = RaceCatalog.HumanId;
                    q.requiredRole = ClassRole.Tank;
                    q.requiredClass = "";
                    q.requiredFlags = new List<string> { HorseStoryCatalog.MilestoneIntro };
                    q.offeredByStory = true;
                    q.grantsFlags = new List<string> { HorseStoryCatalog.FlagFoalRescueCompleted, HorseStoryCatalog.FlagStoryHorseMetPlayer };
                    q.offerText = Loc.T("quest.human_tank.foal_rescue.offer");
                    q.progressText = Loc.T("quest.human_tank.foal_rescue.progress");
                    q.completeText = Loc.T("quest.human_tank.foal_rescue.complete");
                    q.summary = Loc.T("quest.human_tank.foal_rescue.summary");
                    q.objectives = new List<QuestObjective>
                    {
                        new QuestObjective { type = QuestObjectiveType.Story, targetId = "story.horse.search", count = 6, label = Loc.T("quest.horse.obj.search"), labelKey = "quest.horse.obj.search" },
                        new QuestObjective { type = QuestObjectiveType.Story, targetId = "story.horse.foal", count = 1, label = Loc.T("quest.horse.obj.foal"), labelKey = "quest.horse.obj.foal" },
                        new QuestObjective { type = QuestObjectiveType.Story, targetId = "story.horse.predator", count = 1, label = Loc.T("quest.horse.obj.predator"), labelKey = "quest.horse.obj.predator" },
                        new QuestObjective { type = QuestObjectiveType.Story, targetId = "story.horse.return", count = 1, label = Loc.T("quest.horse.obj.return"), labelKey = "quest.horse.obj.return" },
                    };
                    q.startItems = new List<QuestItemReward>();
                    q.xp = 900;
                    q.money = 350;
                    q.rewardItems = new List<QuestItemReward>();
                    q.choiceItems = new List<QuestItemReward>();
                });

                Quest("Quest_HT2_DerLetzteWeidegang", HorseStoryCatalog.LastPastureQuest, Loc.T("quest.human_tank.last_pasture.name"), q =>
                {
                    q.textKey = "quest.human_tank.last_pasture";
                    q.giverNpc = Breeder;
                    q.turnInNpc = Breeder;
                    q.minLevel = HorseStoryCatalog.LastPastureMinLevel;
                    q.prerequisites = new List<QuestDefinition> { q1 };
                    q.requiredRace = RaceCatalog.HumanId;
                    q.requiredRole = ClassRole.Tank;
                    q.requiredFlags = new List<string> { HorseStoryCatalog.MilestoneMainStory };
                    q.offeredByStory = true;
                    q.grantsFlags = new List<string>();
                    q.offerText = Loc.T("quest.human_tank.last_pasture.offer");
                    q.progressText = Loc.T("quest.human_tank.last_pasture.progress");
                    q.completeText = Loc.T("quest.human_tank.last_pasture.complete");
                    q.summary = Loc.T("quest.human_tank.last_pasture.summary");
                    q.objectives = new List<QuestObjective>
                    {
                        new QuestObjective { type = QuestObjectiveType.Story, targetId = "story.horse.q2.wolves", count = 1, label = Loc.T("quest.horse.obj.wolves"), labelKey = "quest.horse.obj.wolves" },
                        new QuestObjective { type = QuestObjectiveType.Story, targetId = "story.horse.q2.spitters", count = 1, label = Loc.T("quest.horse.obj.spitters"), labelKey = "quest.horse.obj.spitters" },
                        new QuestObjective { type = QuestObjectiveType.Story, targetId = "story.horse.q2.lone", count = 1, label = Loc.T("quest.horse.obj.lone"), labelKey = "quest.horse.obj.lone" },
                        new QuestObjective { type = QuestObjectiveType.Story, targetId = "story.horse.q2.gate", count = 1, label = Loc.T("quest.horse.obj.gate"), labelKey = "quest.horse.obj.gate" },
                        new QuestObjective { type = QuestObjectiveType.Story, targetId = "story.horse.q2.hand", count = 1, label = Loc.T("quest.horse.obj.hand"), labelKey = "quest.horse.obj.hand" },
                    };
                    q.startItems = new List<QuestItemReward>();
                    q.xp = 9000;
                    q.money = 2500;
                    q.rewardItems = new List<QuestItemReward>();
                    q.choiceItems = new List<QuestItemReward>();
                });
            }
            finally
            {
                _refresh = keep;
            }

            // Meilensteine an bestehenden Auftraegen (Struktur, keine Texte – immer setzen)
            AddFlag("Quest_01_KlingeFuerDieWacht", HorseStoryCatalog.MilestoneIntro);
            AddFlag("Quest_10_WasImHerzenSchlaegt", HorseStoryCatalog.MilestoneMainStory);
            AssetDatabase.SaveAssets();
            Debug.Log("[Ashveil] Tank-Klassenquests Mensch: 2 Auftraege, Pferdezuechter, Meilensteine gesetzt.");
        }

        private static void AddFlag(string file, string flag)
        {
            var q = AssetDatabase.LoadAssetAtPath<QuestDefinition>(QuestFolder + "/" + file + ".asset");
            if (q == null) { Debug.LogWarning($"[Ashveil] Meilenstein: {file} fehlt."); return; }
            if (q.grantsFlags == null) q.grantsFlags = new List<string>();
            if (!q.grantsFlags.Contains(flag)) q.grantsFlags.Add(flag);
            EditorUtility.SetDirty(q);
        }
    }
}
