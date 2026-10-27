using System.Collections.Generic;
using Ashveil.Data;
using UnityEngine;
using RC = Ashveil.Data.RegisterCatalog;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Hauptquest der Menschen, Akt I „Das Register“ (Max 12.10.2026 abends: freigegeben, Texte v1.1 in einfacher Sprache). Sechs
    /// Aufträge nach der Auftragsstruktur des Akts (§3): Veit am Nordtor gibt R1–R3, Nessa im Lazarett R4–R5, Bralt R6 – das Ende
    /// liegt bei Fenna Gaul am Kornwagen in den Westweiden (Abschiedsauftrag Kareth). Nur für Menschen. Nessa und Mutter Ansa werden
    /// Auftragsfiguren. EP nach der Kareth-Kurve, Geld klein, keine Ausrüstung (Hauptkette, VORSCHLAG Claude). Texte aus register.txt.
    /// </summary>
    public static partial class QuestContentBuilder
    {
        private const string RgKey = "quest.register.";

        public static void BuildRegisterQuests()
        {
            ProjectSetup.EnsureFolder(QuestFolder);
            ProjectSetup.EnsureFolder(NpcFolder);
            Loc.Reload();
            bool keep = _refresh;
            _refresh = true;
            try
            {
                GrNpc("Npc_Nessa", RC.Nessa, "npc.nessa");
                GrNpc("Npc_MutterAnsa", RC.Ansa, "npc.untersteg.ansa");

                QuestObjective Talk(string npc, string key) => RgObj(QuestObjectiveType.TalkTo, npc, 1, key);
                QuestObjective Step(string story, string key) => RgObj(QuestObjectiveType.Story, story, 1, key);

                QuestDefinition r1 = RgQuest("Quest_RG_R1_EinBuendelZuViel", RC.R1, "r1", RC.Veit, RC.Veit, 4, 700, 150, KarethCatalog.SceneTag, null,
                    new List<QuestObjective> { Step(RC.StoryBoard, "obj.r1a"), Step(RC.StoryBook, "obj.r1b") });
                QuestDefinition r2 = RgQuest("Quest_RG_R2_DasBuchGehtZurHalle", RC.R2, "r2", RC.Veit, RC.Bralt, 5, 400, 80, KarethCatalog.SceneTag, r1,
                    new List<QuestObjective> { Step(RC.StoryShelf, "obj.r2a"), Talk(RC.Bralt, "obj.r2b") },
                    q => q.startItems = new List<QuestItemReward> { R("rg_register") });
                QuestDefinition r3 = RgQuest("Quest_RG_R3_DieSingende", RC.R3, "r3", RC.Veit, RC.Ansa, 7, 450, 100, KarethCatalog.SceneTag, r2,
                    new List<QuestObjective> { Talk(RC.Ansa, "obj.r3") });
                QuestDefinition r4 = RgQuest("Quest_RG_R4_EineSeiteFuerNessa", RC.R4, "r4", RC.Nessa, RC.Nessa, 8, 1000, 200, KarethCatalog.SceneTag, r3,
                    new List<QuestObjective> { Step(RC.StoryChest, "obj.r4") });
                QuestDefinition r5 = RgQuest("Quest_RG_R5_UnterSeinemMesser", RC.R5, "r5", RC.Nessa, RC.Bralt, 10, 500, 120, KarethCatalog.SceneTag, r4,
                    new List<QuestObjective> { Talk(RC.Bralt, "obj.r5") },
                    q => q.grantsFlags = new List<string> { RC.FlagConfession });
                // R6 führt über die Zonengrenze: angeboten nur, wo die Westweiden zur Welt gehören (Fenna am Kornwagen)
                RgQuest("Quest_RG_R6_WemDieSeiteGehoert", RC.R6, "r6", RC.Bralt, RC.Fenna, 11, 600, 150, WestweidenCatalog.SceneTag, r5,
                    new List<QuestObjective> { Talk(RC.Fenna, "obj.r6") },
                    q =>
                    {
                        q.startItems = new List<QuestItemReward> { R("rg_versiegelte_seite") };
                        q.grantsFlags = new List<string> { RC.FlagFennasPage };
                    });
            }
            finally
            {
                _refresh = keep;
            }
            Debug.Log($"[Ashveil] Akt I „Das Register“: {RC.Quests.Length} Aufträge, {RC.QuestXpTotal} EP (nur Menschen; Nessa und Mutter Ansa als Auftragsfiguren).");
        }

        private static QuestObjective RgObj(QuestObjectiveType type, string target, int count, string key) =>
            new QuestObjective { type = type, targetId = target, count = count, label = Loc.T(RgKey + key), labelKey = RgKey + key };

        private static QuestDefinition RgQuest(string file, string id, string key, string giver, string turnIn, int minLevel, int xp, int money,
            string sceneTag, QuestDefinition prerequisite, List<QuestObjective> objectives, System.Action<QuestDefinition> extra = null)
        {
            string tk = RgKey + key;
            return Quest(file, id, Loc.T(tk + ".name"), q =>
            {
                q.textKey = tk;
                q.giverNpc = giver;
                q.turnInNpc = turnIn;
                q.minLevel = minLevel;
                q.prerequisites = prerequisite != null ? new List<QuestDefinition> { prerequisite } : new List<QuestDefinition>();
                q.requiredRace = RaceCatalog.HumanId;   // Hauptquest der Menschen
                q.requiredRole = ClassRole.None;
                q.requiredClass = "";
                q.requiredFlags = new List<string>();
                q.offeredByStory = false;
                q.unmarked = false;
                q.requiredSceneTag = sceneTag;
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
            });
        }
    }
}
