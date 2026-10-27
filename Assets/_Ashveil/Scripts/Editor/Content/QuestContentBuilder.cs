using System;
using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Milestone 4: NPCs am Wachtfeuer (Gruss, Handel) und die ersten fuenf Auftraege inkl. Tutorialkette:
    ///   1 Eine Klinge fuer die Wacht  - Gespraech/Interaktion (Hauptmann -&gt; Waffenmeisterin)
    ///   2 Zaehe Felle                 - Kampf und Beute (Aschwolfpelze)
    ///   3 Blick ueber die Senke       - Springen und Orientierung (Aussichtsfels)
    ///   4 Die Verblichenen            - Kampf gegen staerkere Gegner
    ///   5 Pulver fuer die Wacht       - Sammeln aus Gegnerbeute
    /// Standardwerte werden nur beim ersten Anlegen oder bei neuer <see cref="DefaultsVersion"/> geschrieben.
    /// </summary>
    public static partial class QuestContentBuilder
    {
        private const string Root = ProjectSetup.Root + "/Data";
        public const string QuestFolder = Root + "/Quests";
        public const string NpcFolder = Root + "/Npcs";
        private const string ItemFolder = Root + "/Items";
        private const int DefaultsVersion = 7; // 7: Q1 führt vor das Nordtor (WORLD-240, Max 10.10.), 6: Bralt ohne Warnung, Q3 am Aussichtsfels des Muldenrand-Ausblicks (Max 07.10.), 5: Kareth v4 (Welle 0, #27): Orte von Q2–Q4, 4: P2 Waffen als Wahlbelohnung in "Grubensalz", 2: Bralt warnt vor dem Glockenhirten (seit 6 entfallen), 3: Gerrit wird Pfeilmacher (Feuerwaffen ruhen)
        private const string VersionKey = "Ashveil.QuestDefaults.Version";

        public const string Bralt = "npc.oskar_bralt";
        public const string Ilse = "npc.ilse_harrow";
        public const string Maelis = "npc.maelis";
        public const string Gerrit = "npc.gerrit";
        public const string Dorn = "npc.dorn";
        public const string Cartographer = "npc.cartographer";
        public const string LookoutArea = "area.lookout";
        // M6.5 Lager vor den Salzgruben
        public const string Vela = "npc.vela_salk";
        public const string Tobbe = "npc.tobbe";

        private static bool _refresh;

        public static void Build()
        {
            ProjectSetup.EnsureFolder(QuestFolder);
            ProjectSetup.EnsureFolder(NpcFolder);
            _refresh = EditorPrefs.GetInt(VersionKey, 0) < DefaultsVersion;

            // ---------------------------------------------------------- NPCs
            Npc("Npc_OskarBralt", Bralt, "Hauptmann Oskar Bralt", "Aufträge",
                "Die Wacht hält, solange jemand Wache hält. Und im Moment bist das du.", false);
            Npc("Npc_IlseHarrow", Ilse, "Waffenmeisterin Ilse Harrow", "Waffen & Training",
                "Arm locker, Blick auf den Gegner. Und nie auf die eigene Klinge starren.", true,
                (I("rusty_longsword"), 1, 0), (I("cinder_dagger"), 1, 0), (I("ember_staff"), 1, 0), (I("hunters_shortbow"), 1, 0));
            // 21.10.: keine Leinenfetzen mehr (FORTSCHRITT-03: N04 verlangt sie bei Maelis selbst – ein Sammelauftrag darf bei der
            // Auftraggeberin nicht käuflich sein) und kein „Rezept: Heiltrank“ (FORTSCHRITT-05: Berufe sind nicht erlernbar, das Rezept
            // war für jeden nutzlos). Rezepte aus der Beute bleiben verkäuflich.
            Npc("Npc_Maelis", Maelis, "Kräuterin Maelis", "Heiltränke & Kräuter",
                "Setz dich ans Feuer. Wer friert, kauft mehr Tee.", true,
                (I("healing_draught"), 1, 0), (I("strong_healing_draught"), 1, 0), (I("dry_bread"), 1, 0),
                (I("ash_tea"), 1, 0));
            Npc("Npc_Gerrit", Gerrit, "Pfeilmacher Gerrit", "Pfeile, Köcher & Taschen",
                "Früher hab ich Pulver verkauft. Dann ist mir das Lager um die Ohren geflogen.\n\nPfeile explodieren nicht. Ich hab's geprüft.", true,
                (I("rough_arrow"), 200, 20), (I("sharp_arrow"), 200, 100), (I("light_quiver"), 1, 0), (I("hunters_quiver"), 1, 0),
                (I("linen_bag"), 1, 0), (I("wool_bag"), 1, 0));
            Npc("Npc_Dorn", Dorn, "Schmied Dorn", "Rüstung & Reparatur",
                "Bring mir Eisen, und ich mach dir einen Menschen draus, der länger lebt.", true,
                (I("dented_helm"), 1, 0), (I("chainmail_shirt"), 1, 0), (I("iron_sabatons"), 1, 0), (I("oak_shield"), 1, 0),
                (I("travel_hood"), 1, 0), (I("travel_trousers"), 1, 0), (I("foot_wraps"), 1, 0), (I("rope_belt"), 1, 0));
            Npc("Npc_Kartograf", Cartographer, "Kartograf", "Wegweiser",
                "...", false);
            // M6.5: Lager der letzten Grubenleute vor dem Stollenmund
            Npc("Npc_VelaSalk", Vela, "Grubenmeisterin Vela Salk", "Aufträge",
                "Wir haben die Grube zugemacht, als das Salz anfing zu bluten. Seitdem sitzen wir hier und hören zu, wie sie da unten weiterarbeiten.\n\n" +
                "Tote Hände, lebendige Picken. Und ganz unten schlägt etwas.", false);
            Npc("Npc_Tobbe", Tobbe, "Bergmann Tobbe", "Grubenbedarf",
                "Lampenöl ist alle. Tränke hab ich noch. Und Stiefel, die nicht aus dem Grab kommen.", true,
                (I("healing_draught"), 1, 0), (I("strong_healing_draught"), 1, 0), (I("dry_bread"), 1, 0), (I("black_powder"), 5, 0), (I("sharp_arrow"), 200, 100), (I("leather_bag"), 1, 0),
                (I("miner_helmet"), 1, 0), (I("hewer_gloves"), 1, 0), (I("brine_robe"), 1, 0));

            // ---------------------------------------------------------- Auftraege
            QuestDefinition q1 = Quest("Quest_01_KlingeFuerDieWacht", "quest.blade_for_the_watch", "Eine Klinge für die Wacht", q =>
            {
                q.giverNpc = Bralt;
                q.turnInNpc = Ilse;
                q.minLevel = 1;
                // WORLD-240 (Max 10.10.2026): Der erste Auftrag führt durch das Nordtor nach draußen – Ilse drillt die Torwache auf
                // der Vorderwiese (Kareth v4, Tagesablauf), und ihr Schluss zeigt den Weg zum Aussichtsfels (Q3). Kein Pfeil, kein Schlauch.
                q.offerText = "Noch ein Gesicht aus der Asche. Gut - wir brauchen jeden.\n\nMit dem rostigen Ding an deinem Gürtel kommst du aber nicht weit. " +
                              "Geh zu Waffenmeisterin Ilse Harrow. Sie drillt die Torwache draußen vor dem Nordtor. Sie gibt dir etwas, das den Namen Waffe verdient.";
                q.progressText = "Ilse hat dich schon erwartet. Durch das Nordtor, dann links neben dem Weg.";
                q.completeText = "Bralt schickt dich? Dann zeig mal deine Hände. Hm. Die hier passt zu dir - pass auf sie auf, und sie passt auf dich auf.\n\n" +
                                 "Und jetzt dreh dich um. Siehst du den Fels da drüben am Muldenrand, mit dem Steinmann? Da schickt dich der Kartograf als Nächstes hin. Merk dir den Weg - hier draußen läuft dir keiner hinterher.";
                q.summary = "Sprich mit Waffenmeisterin Ilse Harrow vor dem Nordtor.";
                q.objectives = new List<QuestObjective> { Obj(QuestObjectiveType.TalkTo, Ilse, 1, "Mit Ilse Harrow gesprochen") };
                q.xp = 150;
                q.money = 50;
                q.rewardItems = new List<QuestItemReward>
                {
                    R("harrow_blade"), R("harrow_knife"), R("cracked_focus")
                };
                q.choiceItems = new List<QuestItemReward>();
            });

            QuestDefinition q2 = Quest("Quest_02_ZaeheFelle", "quest.tough_hides", "Zähe Felle", q =>
            {
                q.giverNpc = Ilse;
                q.turnInNpc = Maelis;
                q.minLevel = 1;
                q.prerequisites = new List<QuestDefinition> { q1 };
                q.offerText = "Eine Klinge lernt man nicht an Strohpuppen. Nordwestlich, hinter dem Nordtor im Wäldchen, jagen die Aschwölfe in Rudeln.\n\n" +
                              "Bring vier Pelze zu Maelis am Feuer - sie macht daraus Verbände. Und bleib in Bewegung: Wer stehen bleibt, wird gebissen.";
                q.progressText = "Vier Pelze. Nicht drei, nicht dreieinhalb.";
                q.completeText = "Oh, die sind noch warm. Danke dir - daraus werden Verbände für eine ganze Woche. Nimm dir etwas davon mit.";
                q.summary = "Sammle Aschwolfpelze im Wäldchen nordwestlich des Lagers und bring sie Kräuterin Maelis.";
                q.objectives = new List<QuestObjective> { Obj(QuestObjectiveType.Collect, "item.ash_wolf_pelt", 4, "Aschwolfpelze") };
                q.xp = 300;
                q.money = 150;
                q.rewardItems = new List<QuestItemReward> { R("healing_draught", 2) };
                q.choiceItems = new List<QuestItemReward> { R("stalker_gloves"), R("travel_wraps"), R("iron_sabatons") };
            });

            QuestDefinition q3 = Quest("Quest_03_BlickUeberDieSenke", "quest.view_over_the_hollow", "Blick über die Senke", q =>
            {
                q.giverNpc = Cartographer;
                q.turnInNpc = Cartographer;
                q.minLevel = 1;
                q.prerequisites = new List<QuestDefinition> { q1 };
                q.offerText = "(Der Kartograf spricht nicht. Er tippt auf seine Karte, dann auf den Felsen am Muldenrand nordwestlich des Lagers, " +
                              "und macht mit zwei Fingern eine springende Bewegung.)";
                q.progressText = "(Er zeigt erneut auf den Felsen. Ganz nach oben.)";
                q.completeText = "(Er nickt zufrieden und zeichnet mit Kohle ein kleines Kreuz auf deine Hand - dort, wo der Fels steht.)";
                q.summary = "Klettere auf den Aussichtsfels am Muldenrand nordwestlich des Wachtfeuers.";
                q.objectives = new List<QuestObjective> { Obj(QuestObjectiveType.Explore, LookoutArea, 1, "Aussichtsfels") };
                q.xp = 250;
                q.money = 80;
                q.rewardItems = new List<QuestItemReward> { R("grey_wool_cloak") };
                q.choiceItems = new List<QuestItemReward>();
            });

            QuestDefinition q4 = Quest("Quest_04_DieVerblichenen", "quest.the_faded", "Die Verblichenen", q =>
            {
                q.giverNpc = Bralt;
                q.turnInNpc = Bralt;
                q.minLevel = 2;
                q.prerequisites = new List<QuestDefinition> { q2 };
                q.offerText = "Im Osten, im ausgebrannten Gehöft am Bergfuß, bewegt sich wieder etwas. Menschen waren das mal - jetzt sind es Verblichene. " +
                              "Sie erinnern sich an nichts außer an Wut.\n\nDünn ihre Reihen aus. Vier sollten reichen, damit sie uns eine Weile vergessen.";
                q.progressText = "Durchs Osttor, den Bergsteig hinauf und am Bergfuß entlang. Dann immer der Asche nach.";
                q.completeText = "Du lebst noch. Das ist mehr, als ich den meisten zugetraut hätte. Such dir was aus der Kiste aus.";
                q.summary = "Besiege Verblichene im ausgebrannten Gehöft am Bergfuß östlich des Wachtfeuers.";
                q.objectives = new List<QuestObjective> { Obj(QuestObjectiveType.Kill, "enemy.faded_one", 4, "Verblichene besiegt") };
                q.xp = 500;
                q.money = 300;
                q.rewardItems = new List<QuestItemReward>();
                q.choiceItems = new List<QuestItemReward> { R("chainmail_shirt"), R("ember_sash"), R("tarnished_ring") };
            });

            Quest("Quest_05_PulverFuerDieWacht", "quest.powder_for_the_watch", "Federn für die Wacht", q =>
            {
                q.giverNpc = Gerrit;
                q.turnInNpc = Gerrit;
                q.minLevel = 2;
                q.prerequisites = new List<QuestDefinition> { q1 };
                q.offerText = "Die Wacht schießt jetzt mit Pfeilen, und mir gehen die Federn aus. Die Verblichenen tragen noch Krähenfedern an Hut und Köcher – grau, aber gerade.\n\n" +
                              "Bring mir fünf Aschkrähenfedern. Die Schützinnen haben meistens mehr dabei.";
                q.progressText = "Nicht knicken. Geknickte Federn fliegen im Kreis.";
                q.completeText = "Gute Kiele. Daraus werden ein paar hundert Pfeile. Hier – die ersten sind für dich.";
                q.summary = "Sammle Aschkrähenfedern von Verblichenen und bring sie Gerrit.";
                q.objectives = new List<QuestObjective> { Obj(QuestObjectiveType.Collect, "item.ash_crow_feather", 5, "Aschkrähenfedern") };
                q.xp = 350;
                q.money = 250;
                // 21.10.: Grobe statt Scharfe Pfeile – Q05 gibt es ab Stufe 2, Scharfe Pfeile verlangen Stufe 8 (Menge bleibt 200)
                q.rewardItems = new List<QuestItemReward> { R("healing_draught", 3), R("rough_arrow", 200) };
                q.choiceItems = new List<QuestItemReward>();
});

            // ---------------------------------------------------------- M6.5: Auftraege der Salzgruben (Stufe 10–14)
            Quest("Quest_06_SalzUndSchweigen", "quest.salt_and_silence", "Salz und Schweigen", q =>
            {
                q.giverNpc = Bralt;
                q.turnInNpc = Vela;
                q.minLevel = 9;
                q.prerequisites = new List<QuestDefinition>();
                q.offerText = "Unten am Salzsee, wo die Klippe im Südwesten aufreißt, liegen die alten Salzgruben. Die Grubenleute sitzen seit Wochen vor dem Stollen und " +
                              "wollen nicht mehr hinein. Man erzählt sich, das Salz dort sei das Fleisch der Alten.\n\n" +
                              "Ich halte nichts von Geschichten. Aber ich halte etwas von Salz – ohne Salz fault uns das Fleisch im Winter. Sprich mit Grubenmeisterin Vela Salk.";
                q.progressText = "Südwestufer des Salzsees. Folge dem Geruch nach Lake und Pulver.";
                q.completeText = "Bralt schickt jemanden? Dann glaubt er uns also doch. Setz dich. Du wirst gleich wünschen, du hättest es nicht getan.";
                q.summary = "Sprich mit Grubenmeisterin Vela Salk vor den Salzgruben am Südwestufer des Salzsees.";
                q.objectives = new List<QuestObjective> { Obj(QuestObjectiveType.TalkTo, Vela, 1, "Mit Vela Salk gesprochen") };
                q.xp = 800;
                q.money = 300;
                q.rewardItems = new List<QuestItemReward>();
                q.choiceItems = new List<QuestItemReward>();
            });
            Quest("Quest_07_Grubensalz", "quest.mine_salt", "Grubensalz", q =>
            {
                q.giverNpc = Vela;
                q.turnInNpc = Vela;
                q.minLevel = 10;
                q.prerequisites = new List<QuestDefinition>();
                q.offerText = "Die Verblichenen da unten schlagen immer noch Salz. Sie tragen es in den Taschen, als müssten sie es abliefern.\n\n" +
                              "Bring mir zehn Brocken Grubensalz. Kareth braucht es – und ich will wissen, ob es sich verändert hat.";
                q.progressText = "Zehn Brocken. Schau sie dir nicht zu lange an.";
                q.completeText = "Warm. Alle warm. Früher war es kalt wie Stein. Hier – für deine Mühe. Und wasch dir die Hände.";
                q.summary = "Sammle Grubensalz von den Grubenarbeitern in den Salzgruben und bring es Vela Salk.";
                q.objectives = new List<QuestObjective> { Obj(QuestObjectiveType.Collect, "item.mine_salt", 10, "Grubensalz") };
                q.xp = 2750;   // Salzgruben: Dungeon-Auftraege ×1,25 (Max 10.10.2026: „Dungeon-EP wie Forever“, vorher 2200)
                q.money = 900;
                q.rewardItems = new List<QuestItemReward> { R("strong_healing_draught", 3) };
                // P2 Balance: Waffenstufe zwischen 5 und 12 – je Klasse eine zur Wahl
                q.choiceItems = new List<QuestItemReward> { R("shaft_mace"), R("hewer_dagger"), R("saltwood_staff") };
            });
            QuestDefinition qShift = Quest("Quest_08_DieLetzteSchicht", "quest.the_last_shift", "Die letzte Schicht", q =>
            {
                q.giverNpc = Vela;
                q.turnInNpc = Vela;
                q.minLevel = 10;
                q.prerequisites = new List<QuestDefinition>();
                q.offerText = "Hask war mein Vorarbeiter. Als wir rausgelaufen sind, ist er drin geblieben – „bis die Schicht vorbei ist“, hat er gesagt.\n\n" +
                              "Sie ist nie vorbei gegangen. Er hält die Arbeiter da unten bei der Hacke, und er hat die Sprengladungen. Beende seine Schicht.";
                q.progressText = "Die Salzhalle, hinter dem Hauptstollen. Du hörst ihn, bevor du ihn siehst.";
                q.completeText = "Dann ist er jetzt frei. Oder zumindest still. Nimm dir etwas aus seiner Kiste – er hätte es dir sowieso nicht gegeben.";
                q.summary = "Besiege Vorarbeiter Hask in der Salzhalle der Salzgruben.";
                q.objectives = new List<QuestObjective> { Obj(QuestObjectiveType.Kill, "enemy.foreman_hask", 1, "Vorarbeiter Hask besiegt") };
                q.xp = 4400;   // ×1,25 (vorher 3500)
                q.money = 1500;
                q.rewardItems = new List<QuestItemReward>();
                q.choiceItems = new List<QuestItemReward> { R("miner_helmet"), R("hewer_gloves"), R("brine_robe") };
            });
            Quest("Quest_09_DasSchiffImBerg", "quest.ship_in_the_mountain", "Das Schiff im Berg", q =>
            {
                q.giverNpc = Vela;
                q.turnInNpc = Vela;
                q.minLevel = 11;
                q.prerequisites = new List<QuestDefinition>();
                q.offerText = "Unter der Salzhalle, westlich, liegt ein Schiff. Mitten im Berg. Mein Großvater sagte, der See habe früher bis ans Meer gereicht.\n\n" +
                              "Der Kapitän steht noch am Ruder. Niemand muss dorthin. Aber wer es tut, bekommt von mir das Beste, was ich noch habe.";
                q.progressText = "Der Wrackstollen – links in der Salzhalle. Pass auf die Brut auf.";
                q.completeText = "„Endlich Landgang“? Das hat er gesagt? … Dann hat er wenigstens am Ende gewusst, wo er war.";
                q.summary = "Optional: Besiege den Ertrunkenen Kapitän in der Wrackhöhle.";
                q.objectives = new List<QuestObjective> { Obj(QuestObjectiveType.Kill, "enemy.drowned_captain", 1, "Der Ertrunkene Kapitän besiegt") };
                q.xp = 4400;   // ×1,25 (vorher 3500)
                q.money = 1800;
                q.rewardItems = new List<QuestItemReward>();
                q.choiceItems = new List<QuestItemReward> { R("lamp_bearer_cloak"), R("shift_belt"), R("salt_ring") };
            });
            Quest("Quest_10_WasImHerzenSchlaegt", "quest.what_beats_in_the_heart", "Was im Herzen schlägt", q =>
            {
                q.giverNpc = Vela;
                q.turnInNpc = Vela;
                q.minLevel = 11;
                q.prerequisites = new List<QuestDefinition> { qShift };
                q.offerText = "Du hast es gehört, oder? Da unten. Langsam. Wie ein Herz.\n\nWir haben zu tief gegraben. Unter der Salzsee-Grotte liegt eine Kaverne, " +
                              "und darin liegt es. Es frisst unsere Lampen. Es frisst alles Licht. Wenn das Salz wirklich Fleisch ist – dann ist das hier das Herz dazu.\n\n" +
                              "Bring es zum Schweigen.";
                q.progressText = "Durch die Grotte, dann den Kristallstollen hinab. Halte die Lampen am Brennen.";
                q.completeText = "Still. Zum ersten Mal seit Monaten ist es still. Ich weiß nicht, ob wir etwas Großes getötet haben – oder nur ein Stück davon.\n\n" +
                                 "Hier. Das hat Hask für schlechte Zeiten versteckt. Ich glaube, die sind jetzt vorbei.";
                q.summary = "Besiege das Grubenherz in der Herzkaverne der Salzgruben.";
                q.objectives = new List<QuestObjective> { Obj(QuestObjectiveType.Kill, "enemy.mine_heart", 1, "Das Grubenherz besiegt") };
                q.xp = 7500;   // ×1,25 (vorher 6000)
                q.money = 4000;
                q.rewardItems = new List<QuestItemReward> { R("red_vein_salt", 2) };
                q.choiceItems = new List<QuestItemReward> { R("crab_shell_pauldrons"), R("salt_crusted_sabatons"), R("lamp_bearer_cloak") };
            });

            // Kolosseum-Vorplatz (Pass 2): Ringmeister, Marketenderin, Waffenwart
            BuildColosseumNpcs();
            // P3 Glockenrast (Stufe 5–9): 13 Auftraege, Anselm, Runa, Merle, Jasko, Pilger, Tamo, Veit
            BuildGlockenrastQuests();
            // #28 Kareth v4 (Welle 0): elf Ergaenzungsauftraege, Wenna, Jorn, Ulf, Oswald, Ebba – nur in Szenen „kareth_v4“
            BuildKarethQuests(q1, q3);
            // Westweiden (Lückenliste Punkt 2, 12.10.2026): 26 Aufträge W01–W28 (ohne W23/W24), 18 Figuren – nur in Szenen „westweiden“
            BuildWestweidenQuests();
            // Hauptquest der Menschen, Akt I „Das Register“ (13.10.2026): R1–R6, Nessa und Mutter Ansa – R1–R5 Kareth v4, R6 Westweiden
            BuildRegisterQuests();

            if (_refresh) EditorPrefs.SetInt(VersionKey, DefaultsVersion);
            AssetDatabase.SaveAssets();
            Debug.Log("[Ashveil] Auftraege und NPCs: 10 Auftraege, 8 NPCs (5 Haendler).");
        }

        // ================================================================== Hilfen

        private static ItemDefinition I(string key)
        {
            var d = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemFolder + "/Item_" + key + ".asset");
            if (d == null) Debug.LogWarning($"[Ashveil] Auftraege: Gegenstand '{key}' fehlt.");
            return d;
        }

        private static QuestItemReward R(string key, int count = 1) => new QuestItemReward { item = I(key), count = count };

        private static QuestObjective Obj(QuestObjectiveType type, string target, int count, string label) =>
            new QuestObjective { type = type, targetId = target, count = count, label = label };

        private static void Npc(string file, string id, string name, string title, string greeting, bool vendor,
            params (ItemDefinition item, int count, int price)[] stock)
        {
            Ensure<NpcDefinition>(NpcFolder, file, id, name, n =>
            {
                n.title = title;
                n.greeting = greeting;
                n.vendor = vendor;
                n.interactRange = 5f;
                n.stock = new List<VendorEntry>();
                foreach ((ItemDefinition item, int count, int price) in stock)
                    if (item != null) n.stock.Add(new VendorEntry { item = item, count = count, price = price });
            });
        }

        private static QuestDefinition Quest(string file, string id, string name, Action<QuestDefinition> setup) =>
            Ensure(QuestFolder, file, id, name, setup);

        private static T Ensure<T>(string folder, string file, string id, string displayName, Action<T> setup) where T : GameDefinition
        {
            string path = folder + "/" + file + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<T>(path);
            bool fresh = def == null;
            if (fresh)
            {
                def = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(def, path);
            }
            if (fresh || _refresh)
            {
                setup(def);
                K.SetString(def, "id", id);
                K.SetString(def, "displayName", displayName);
                EditorUtility.SetDirty(def);
            }
            return def;
        }
    }
}
