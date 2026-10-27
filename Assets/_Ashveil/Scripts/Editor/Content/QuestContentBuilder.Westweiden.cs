using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;
using WC = Ashveil.Data.WestweidenCatalog;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden (Lückenliste Punkt 2, 12.10.2026): die Aufträge W01–W28 als Spieldaten nach
    /// `claude/texte/westweiden_auftraege_v1.md` (v1.1 einfache Sprache; Max 12.10.: Wendung, Namen und W24-Ende bestätigt,
    /// „Die Story-Texte der Hauptkette schreibst du vorerst“). Stufen, Geber und Ziele nach dem Gebietsblatt v2 §6; EP, Geld und
    /// Belohnungen sind Vorschläge (Claude, <see cref="WestweidenCatalog.QuestXpTotal"/>).
    ///   H1/H2 Hallgrund (W01–W05) · K1/K2 Kornwagen (W06–W11) · N1 Dreispannhof (W14–W15) · N2 Heckenhof (W16–W18) ·
    ///   K3 Zollbuch (W19–W21) · Einzel W12, W13, W22, W25 · P Passfuß (W26–W28).
    /// K4 Kreidekeller (W23, Gruppe 2–3) → W24 „Kopf des Zöllners“ (Ende der Hauptkette) seit dem Kreidekeller (Punkt 5, 12.10. nachts).
    /// Alle gelten nur in Szenen mit dem Kennzeichen „westweiden“.
    /// Texte aus Resources/Text/de/westweiden.txt (Präfix = textKey). Bei jedem Aufbau neu geschrieben.
    /// </summary>
    public static partial class QuestContentBuilder
    {
        private const string WwKey = "quest.westweiden.";

        public static void BuildWestweidenQuests()
        {
            ProjectSetup.EnsureFolder(QuestFolder);
            ProjectSetup.EnsureFolder(NpcFolder);
            bool keep = _refresh;
            _refresh = true;
            try
            {
                // ---------------------------------------------------------- Figuren
                GrNpc("Npc_WW_Jost", WC.Jost, "npc.westweiden.jost");
                GrNpc("Npc_WW_Brun", WC.Brun, "npc.westweiden.brun");
                GrNpc("Npc_WW_Stallknecht", WC.Stallknecht, "npc.westweiden.stallknecht");
                GrNpc("Npc_WW_Harm", WC.Harm, "npc.westweiden.harm");
                GrNpc("Npc_WW_Lute", WC.Lute, "npc.westweiden.lute");
                GrNpc("Npc_WW_Alwine", WC.Alwine, "npc.westweiden.alwine");
                GrNpc("Npc_WW_Ruland", WC.Ruland, "npc.westweiden.ruland");
                GrNpc("Npc_WW_Fenna", WC.Fenna, "npc.westweiden.fenna");
                GrNpc("Npc_WW_Hollerbach", WC.Hollerbach, "npc.westweiden.hollerbach");
                GrNpc("Npc_WW_Marthe", WC.Marthe, "npc.westweiden.marthe");
                GrNpc("Npc_WW_Wilke", WC.Wilke, "npc.westweiden.wilke");
                GrNpc("Npc_WW_Tade", WC.Tade, "npc.westweiden.tade");
                GrNpc("Npc_WW_Stromfluesterer", WC.Stromfluesterer, "npc.westweiden.stromfluesterer");
                GrNpc("Npc_WW_Oberhirte", WC.Oberhirte, "npc.westweiden.oberhirte");
                GrNpc("Npc_WW_Brigge", WC.Brigge, "npc.westweiden.brigge");
                GrNpc("Npc_WW_Imke", WC.Imke, "npc.westweiden.imke");
                GrNpc("Npc_WW_Gerlach", WC.Gerlach, "npc.westweiden.gerlach");
                // 22.10. WESTWEIDEN-02: Gerlach („Händler“, Gruß „Gute Ware, faire Preise“) handelt – Angebot wie die Marketenderin,
                // ohne Schwarzpulver (Kolosseum-Bedarf). Erst NACH GrNpc, weil GrNpc bei jedem Aufbau vendor = false und einen leeren
                // stock schreibt. Preise 0 = Regelpreis (wie bei der Marketenderin), also keine Geldquelle.
                WwHaendler("Npc_WW_Gerlach", "healing_draught", "strong_healing_draught", "dry_bread", "ash_tea");
                GrNpc("Npc_WW_Sanne", WC.Sanne, "npc.westweiden.sanne");

                QuestObjective Talk(string npc, string key) => WwObj(QuestObjectiveType.TalkTo, npc, 1, key);
                List<QuestItemReward> Start(params string[] keys)
                {
                    var l = new List<QuestItemReward>();
                    foreach (string k in keys) l.Add(R(k));
                    return l;
                }

                // ---------------------------------------------------------- A Hallgrund-Weiden (8–10)
                QuestDefinition w01 = WwQuest("Quest_WW_W01", WC.W01, "w01", WC.Jost, WC.Brun, 8, 300, 80, null,
                    new List<QuestObjective> { Talk(WC.Brun, "obj.w01") });
                QuestDefinition w02 = WwQuest("Quest_WW_W02", WC.W02, "w02", WC.Brun, WC.Brun, 8, 1000, 300, P(w01),
                    new List<QuestObjective> { WwObj(QuestObjectiveType.Collect, WC.ItemPost, 8, "obj.w02") },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 2) });
                QuestDefinition w03 = WwQuest("Quest_WW_W03", WC.W03, "w03", WC.Brun, WC.Harm, 9, 330, 90, P(w02),
                    new List<QuestObjective> { Talk(WC.Harm, "obj.w03") });
                QuestDefinition w04 = WwQuest("Quest_WW_W04", WC.W04, "w04", WC.Harm, WC.Harm, 9, 1100, 350, P(w03),
                    new List<QuestObjective> { WwObj(QuestObjectiveType.Collect, WC.ItemOtterPelt, 8, "obj.w04") },
                    q => q.rewardItems = new List<QuestItemReward> { R("ww_hufeisen"), R("healing_draught", 2) });   // „Die Eisen liegen hier. Bring sie rauf.“
                QuestDefinition w05 = WwQuest("Quest_WW_W05", WC.W05, "w05", WC.Lute, WC.Brun, 10, 380, 120, P(w04),
                    new List<QuestObjective> { WwObj(QuestObjectiveType.Collect, WC.ItemHorseshoes, 1, "obj.w05a"), Talk(WC.Brun, "obj.w05") },
                    q =>
                    {
                        q.startItems = Start("ww_zaumzeug");   // Hufeisen: Belohnung von W04 (Harm)
                        q.rewardItems = new List<QuestItemReward> { R("healing_draught", 3), R("dry_bread", 3) };
                    });

                // ---------------------------------------------------------- K1/K2 Der Kornwagen (10–11, Hauptkette)
                QuestDefinition w06 = WwQuest("Quest_WW_W06", WC.W06, "w06", WC.Stallknecht, WC.Alwine, 10, 560, 160, P(w05),
                    new List<QuestObjective> { Talk(WC.Alwine, "obj.w06") });
                QuestDefinition w07 = WwQuest("Quest_WW_W07", WC.W07, "w07", WC.Alwine, WC.Ruland, 10, 560, 160, P(w06),
                    new List<QuestObjective> { Talk(WC.Ruland, "obj.w07") },
                    q => q.startItems = Start("ww_befehl"));
                QuestDefinition w08 = WwQuest("Quest_WW_W08", WC.W08, "w08", WC.Ruland, WC.Fenna, 11, 630, 180, P(w07),
                    new List<QuestObjective> { Talk(WC.Fenna, "obj.w08") });
                QuestDefinition w09 = WwQuest("Quest_WW_W09", WC.W09, "w09", WC.Fenna, WC.Fenna, 11, 1400, 400, P(w08),
                    new List<QuestObjective> { WwObj(QuestObjectiveType.Collect, WC.ItemGrainSack, 8, "obj.w09") },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 2) });
                QuestDefinition w10 = WwQuest("Quest_WW_W10", WC.W10, "w10", WC.Fenna, WC.Hollerbach, 11, 630, 180, P(w09),
                    new List<QuestObjective> { Talk(WC.Hollerbach, "obj.w10") },
                    q => q.startItems = Start("ww_geborgenes_korn"));
                QuestDefinition w11 = WwQuest("Quest_WW_W11", WC.W11, "w11", WC.Hollerbach, WC.Marthe, 11, 630, 180, P(w10),
                    new List<QuestObjective> { Talk(WC.Marthe, "obj.w11") },
                    q =>
                    {
                        q.startItems = Start("ww_mehlsack");
                        q.rewardItems = new List<QuestItemReward> { R("dry_bread", 5) };   // „Wir backen heute Abend.“
                    });

                // ---------------------------------------------------------- Einzelaufträge Langer Rain, Hirtenlager
                WwQuest("Quest_WW_W12", WC.W12, "w12", WC.Tade, WC.Tade, 10, 1250, 350, null,
                    new List<QuestObjective> { WwObj(QuestObjectiveType.Collect, WC.ItemMudfish, 8, "obj.w12") },
                    q => q.rewardItems = new List<QuestItemReward> { R("dry_bread", 5), R("ash_tea", 3) });
                WwQuest("Quest_WW_W13", WC.W13, "w13", WC.Stromfluesterer, WC.Oberhirte, 12, 470, 140, null,
                    new List<QuestObjective> { Talk(WC.Oberhirte, "obj.w13") });

                // ---------------------------------------------------------- N1 Dreispannhof (11–12)
                QuestDefinition w14 = WwQuest("Quest_WW_W14", WC.W14, "w14", WC.Marthe, WC.Marthe, 11, 1100, 320, P(w11),
                    new List<QuestObjective> { WwObj(QuestObjectiveType.Story, WC.StoryDrainCleared, 1, "obj.w14") },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 2) });
                WwQuest("Quest_WW_W15", WC.W15, "w15", WC.Marthe, WC.Marthe, 12, 1550, 450, P(w14),
                    new List<QuestObjective> { WwObj(QuestObjectiveType.Kill, WC.EnemyWeasel, 8, "obj.w15") },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 3) });

                // ---------------------------------------------------------- N2 Heckenhof (12)
                QuestDefinition w16 = WwQuest("Quest_WW_W16", WC.W16, "w16", WC.Imke, WC.Gerlach, 12, 470, 140, null,
                    new List<QuestObjective> { Talk(WC.Gerlach, "obj.w16") },
                    q => q.startItems = Start("ww_wollballen"));
                QuestDefinition w17 = WwQuest("Quest_WW_W17", WC.W17, "w17", WC.Gerlach, WC.Imke, 12, 470, 140, P(w16),
                    new List<QuestObjective> { Talk(WC.Imke, "obj.w17") },
                    q => q.startItems = Start("ww_rechnung"));
                WwQuest("Quest_WW_W18", WC.W18, "w18", WC.Imke, WC.Imke, 12, 2000, 600, P(w17),
                    new List<QuestObjective> { WwObj(QuestObjectiveType.Collect, WC.ItemRotkehlHead, 1, "obj.w18") },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 4) });

                // ---------------------------------------------------------- K3 Das Zollbuch (13–14, Hauptkette)
                QuestDefinition w19 = WwQuest("Quest_WW_W19", WC.W19, "w19", WC.Alwine, WC.Alwine, 13, 1700, 500, P(w11),
                    new List<QuestObjective> { WwObj(QuestObjectiveType.Collect, WC.ItemTollBook, 1, "obj.w19") },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 3) });
                QuestDefinition w20 = WwQuest("Quest_WW_W20", WC.W20, "w20", WC.Alwine, WC.Wilke, 13, 770, 220, P(w19),
                    new List<QuestObjective> { Talk(WC.Wilke, "obj.w20") },
                    q => q.startItems = Start("ww_zollbuch"));
                QuestDefinition w21 = WwQuest("Quest_WW_W21", WC.W21, "w21", WC.Wilke, WC.Ruland, 14, 830, 240, P(w20),
                    new List<QuestObjective> { Talk(WC.Ruland, "obj.w21") });

                // ---------------------------------------------------------- K4 Kreidekeller und Habbo (14–15, Ende der Hauptkette)
                QuestDefinition w23 = WwQuest("Quest_WW_W23", WC.W23, "w23", WC.Ruland, WC.Ruland, 14, 2800, 800, P(w21),
                    new List<QuestObjective>
                    {
                        WwObj(QuestObjectiveType.Collect, WC.ItemSealRed, 1, "obj.w23a"),
                        WwObj(QuestObjectiveType.Collect, WC.ItemSealBlue, 1, "obj.w23b"),
                        WwObj(QuestObjectiveType.Collect, WC.ItemSealMill, 1, "obj.w23c"),
                        WwObj(QuestObjectiveType.Collect, WC.ItemBoatList, 1, "obj.w23d")
                    },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 5) });
                WwQuest("Quest_WW_W24", WC.W24, "w24", WC.Alwine, WC.Alwine, 15, 2600, 1200, P(w23),
                    new List<QuestObjective> { WwObj(QuestObjectiveType.Collect, WC.ItemHabboHead, 1, "obj.w24") },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 5), R("dry_bread", 5) });

                // ---------------------------------------------------------- Einzel: Schützen, Käse (13)
                WwQuest("Quest_WW_W22", WC.W22, "w22", WC.Fenna, WC.Fenna, 13, 1700, 500, P(w09),
                    new List<QuestObjective> { WwObj(QuestObjectiveType.Kill, WC.EnemyArcher, 8, "obj.w22") },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 3) });
                WwQuest("Quest_WW_W25", WC.W25, "w25", WC.Brigge, WC.Tade, 13, 510, 150, null,
                    new List<QuestObjective> { Talk(WC.Tade, "obj.w25") },
                    q =>
                    {
                        q.startItems = Start("ww_kaeselaib");
                        q.rewardItems = new List<QuestItemReward> { R("dry_bread", 2) };   // „Für dich zwei.“
                    });

                // ---------------------------------------------------------- P Passfuß (14–15)
                QuestDefinition w26 = WwQuest("Quest_WW_W26", WC.W26, "w26", WC.Sanne, WC.Ruland, 14, 560, 160, null,
                    new List<QuestObjective> { Talk(WC.Ruland, "obj.w26") },
                    q => q.startItems = Start("ww_salbe"));
                QuestDefinition w27 = WwQuest("Quest_WW_W27", WC.W27, "w27", WC.Ruland, WC.Ruland, 15, 2000, 600, P(w26),
                    new List<QuestObjective> { WwObj(QuestObjectiveType.Collect, WC.ItemSnakeSkin, 8, "obj.w27") },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 3) });
                WwQuest("Quest_WW_W28", WC.W28, "w28", WC.Ruland, WC.Ruland, 15, 3000, 900, P(w27),
                    new List<QuestObjective> { WwObj(QuestObjectiveType.Collect, WC.ItemHedgeKingSkin, 1, "obj.w28") },
                    q => q.rewardItems = new List<QuestItemReward> { R("healing_draught", 5) });
            }
            finally
            {
                _refresh = keep;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Ashveil] Westweiden: {WC.Quests.Length} Aufträge, {WC.Npcs.Length} Figuren, {WC.QuestXpTotal} EP aus Aufträgen (nur in Szenen „{WC.SceneTag}“).");
        }

        /// <summary>
        /// 22.10. WESTWEIDEN-02: eine mit <see cref="GrNpc"/> angelegte Westweiden-Figur zum Händler machen (je Ware 1 Stück zum
        /// Regelpreis). Läuft nach GrNpc im selben Aufbau, damit dessen vendor = false nicht stehen bleibt.
        /// </summary>
        private static void WwHaendler(string file, params string[] waren)
        {
            var n = AssetDatabase.LoadAssetAtPath<NpcDefinition>(NpcFolder + "/" + file + ".asset");
            if (n == null)
            {
                Debug.LogWarning($"[Ashveil] Westweiden: Händler {file} fehlt.");
                return;
            }
            n.vendor = true;
            n.stock = new List<VendorEntry>();
            foreach (string k in waren)
            {
                ItemDefinition item = I(k);
                if (item != null) n.stock.Add(new VendorEntry { item = item, count = 1, price = 0 });
            }
            EditorUtility.SetDirty(n);
        }

        private static QuestObjective WwObj(QuestObjectiveType type, string target, int count, string key) =>
            new QuestObjective { type = type, targetId = target, count = count, label = Loc.T(WwKey + key), labelKey = WwKey + key };

        private static QuestDefinition WwQuest(string file, string id, string key, string giver, string turnIn, int minLevel, int xp, int money,
            QuestDefinition[] prerequisites, List<QuestObjective> objectives, System.Action<QuestDefinition> extra = null)
        {
            string tk = WwKey + key;
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
                q.requiredSceneTag = WC.SceneTag;
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
                if (q.prerequisites.Contains(null)) Debug.LogWarning($"[Ashveil] Westweiden: {id} hat eine fehlende Voraussetzung.");
            });
        }
    }
}
