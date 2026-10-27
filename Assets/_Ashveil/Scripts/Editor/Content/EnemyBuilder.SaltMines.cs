using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Enemies;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// M6 – Gegner der Salzgruben (Stufe 10–14). Jede Variante unterscheidet sich im Verhalten, nicht nur im Leben
    /// (Kanon 26.09.). Die Spawn-Gebiete liegen in jedem Instanz-Platz; der DungeonDirector erzeugt die Gegner erst,
    /// wenn eine Gruppe den Platz belegt, und ohne Wiederkehr.
    /// </summary>
    public static partial class EnemyBuilder
    {
        private static List<GameObject> BuildSaltMineEnemies()
        {
            EnemyDefinition worker = EnsureDefinition("Enemy_Grubenarbeiter", "enemy.mine_worker", "Verblichener Grubenarbeiter", SetupMineWorker);
            EnemyDefinition crab = EnsureDefinition("Enemy_Salzkrebs", "enemy.salt_crab", "Salzkrebs", SetupSaltCrab);
            EnemyDefinition brood = EnsureDefinition("Enemy_NadelspeierBrut", "enemy.needle_brood", "Nadelspeier-Brut", SetupNeedleBrood);
            EnemyDefinition brute = EnsureDefinition("Enemy_Grubenschrat", "enemy.salt_brute", "Grubenschrat", SetupSaltBrute);
            EnemyDefinition hask = EnsureDefinition("Enemy_VorarbeiterHask", "enemy.foreman_hask", "Vorarbeiter Hask", SetupForemanHask);
            EnemyDefinition mother = EnsureDefinition("Enemy_Salzmutter", "enemy.salt_mother", "Die Salzmutter", SetupSaltMother);
            EnemyDefinition brood2 = EnsureDefinition("Enemy_Krebsbrut", "enemy.crab_brood", "Krebsbrut", SetupCrabBrood);
            EnemyDefinition heartDef = EnsureDefinition("Enemy_Grubenherz", "enemy.mine_heart", "Das Grubenherz", SetupMineHeart);
            EnemyDefinition eater = EnsureDefinition("Enemy_Lichtfresser", "enemy.light_eater", "Lichtfresser", SetupLightEater);
            EnemyDefinition captain = EnsureDefinition("Enemy_ErtrunkenerKapitaen", "enemy.drowned_captain", "Der Ertrunkene Kapitän", SetupDrownedCaptain);
            EnemyDefinition sailor = EnsureDefinition("Enemy_ErtrunkenerMatrose", "enemy.drowned_sailor", "Ertrunkener Matrose", SetupDrownedSailor);

            Material saltSkin = K.Mat("Enemy_SaltSkin", new Color(0.7f, 0.69f, 0.66f));
            Material saltShell = K.Mat("Enemy_SaltShell", new Color(0.86f, 0.85f, 0.82f), 0.45f);
            Material saltShellDark = K.Mat("Enemy_SaltShellDark", new Color(0.62f, 0.34f, 0.3f), 0.35f);
            Material saltVein = K.Mat("Enemy_SaltVein", new Color(0.55f, 0.18f, 0.16f), 0.3f, new Color(0.35f, 0.05f, 0.04f));
            Material saltGlow = K.Mat("Enemy_SaltGlow", new Color(1f, 0.55f, 0.5f), 0.2f, new Color(1.2f, 0.35f, 0.3f));
            Material broodSkin = K.Mat("Enemy_BroodSkin", new Color(0.55f, 0.58f, 0.5f), 0.3f);
            Material haskSkin = K.Mat("Enemy_ForemanSkin", new Color(0.62f, 0.42f, 0.34f), 0.25f);
            Material motherShell = K.Mat("Enemy_MotherShell", new Color(0.78f, 0.62f, 0.6f), 0.5f);
            Material motherDark = K.Mat("Enemy_MotherShellDark", new Color(0.45f, 0.2f, 0.2f), 0.4f);
            Material broodShell = K.Mat("Enemy_BroodShell", new Color(0.93f, 0.9f, 0.86f), 0.5f);
            Material heartCrust = K.Mat("Enemy_HeartCrust", new Color(0.55f, 0.16f, 0.14f), 0.4f, new Color(0.4f, 0.04f, 0.03f));
            Material heartGlow = K.Mat("Enemy_HeartGlow", new Color(1f, 0.4f, 0.35f), 0.2f, new Color(2.2f, 0.45f, 0.35f));
            Material eaterSkin = K.Mat("Enemy_EaterSkin", new Color(0.05f, 0.05f, 0.07f), 0.1f);
            Material drownedSkin = K.Mat("Enemy_DrownedSkin", new Color(0.36f, 0.45f, 0.48f), 0.45f);
            Material captainSkin = K.Mat("Enemy_CaptainSkin", new Color(0.28f, 0.34f, 0.4f), 0.4f);

            GameObject workerPrefab = Tint(BuildHumanoidPrefab(worker, false), (_ashSkin, saltSkin));
            GameObject crabPrefab = Tint(BuildBeastPrefab(crab), (_wolfFur, saltShell), (_wolfDark, saltShellDark));
            GameObject broodPrefab = Tint(BuildNeedleSpitterPrefab(brood), (_chitin, broodSkin));
            GameObject haskPrefab = Tint(BuildHumanoidPrefab(hask, false), (_ashSkin, haskSkin));
            GameObject motherPrefab = Tint(BuildBeastPrefab(mother), (_wolfFur, motherShell), (_wolfDark, motherDark));
            GameObject broodPrefab2 = Tint(BuildBeastPrefab(brood2), (_wolfFur, broodShell), (_wolfDark, saltShellDark));
            GameObject heartPrefab = Tint(BuildColossusPrefab(heartDef, false), (_peat, heartCrust), (_root, saltShellDark), (_mossGreen, heartCrust), (_bogGlow, heartGlow));
            GameObject eaterPrefab = Tint(BuildBeastPrefab(eater), (_wolfFur, eaterSkin), (_wolfDark, eaterSkin));
            GameObject captainPrefab = Tint(BuildHumanoidPrefab(captain, false), (_ashSkin, captainSkin));
            GameObject sailorPrefab = Tint(BuildHumanoidPrefab(sailor, false), (_ashSkin, drownedSkin));
            GameObject brutePrefab = Tint(BuildColossusPrefab(brute, false), (_peat, saltShell), (_root, saltShellDark), (_mossGreen, saltVein), (_bogGlow, saltGlow));

            // Spawn-Gebiete je Instanz-Platz (lokale Punkte aus SaltMineBuilder)
            foreach (Transform slot in SaltMineBuilder.Slots)
            {
                Transform g = K.Group("Gegner", slot);
                Area(g, "Grubenarbeiter Hauptstollen 1", SaltMineBuilder.MainTunnelPack1, worker, workerPrefab, 3, 3f, "", 10, 11);
                Area(g, "Grubenarbeiter Hauptstollen 2", SaltMineBuilder.MainTunnelPack2, worker, workerPrefab, 3, 3f, "", 10, 11);
                Area(g, "Grubenarbeiter Salzhalle", SaltMineBuilder.HallCGuards, worker, workerPrefab, 2, 3f, "", 11, 12);
                Area(g, "Salzkrebse Flutstollen", SaltMineBuilder.FloodTunnel, crab, crabPrefab, 3, 3f, "", 11, 12);
                Area(g, "Salzkrebse Grotte", SaltMineBuilder.GrottoEdge, crab, crabPrefab, 2, 3f, "", 12, 13);
                Area(g, "Brut Wrackstollen", SaltMineBuilder.WreckTunnel, brood, broodPrefab, 4, 3f, "", 11, 12);
                Area(g, "Brut Wrackhoehle", SaltMineBuilder.WreckCave, brood, broodPrefab, 3, 4f, "", 12, 12);
                Area(g, "Grubenschrat Kristallstollen", SaltMineBuilder.CrystalTunnel, brute, brutePrefab, 1, 1f, "", 13, 13);
                Area(g, "Salzkrebse Herzkaverne", SaltMineBuilder.HeartEntry, crab, crabPrefab, 2, 3f, "", 13, 14);

                // Boss 1: Vorarbeiter Hask mit zwei Seitenstollen, aus denen er Arbeiter ruft (M6.2)
                EnemySpawnArea haskArea = Area(g, "Boss Vorarbeiter Hask", SaltMineBuilder.HaskSpot, hask, haskPrefab, 1, 0f, "", 14, 14);
                EnemySpawnArea callWest = Area(g, "Hask ruft (West)", SaltMineBuilder.HaskCallWest, worker, workerPrefab, 1, 1.5f, "", 11, 11);
                EnemySpawnArea callEast = Area(g, "Hask ruft (Ost)", SaltMineBuilder.HaskCallEast, worker, workerPrefab, 1, 1.5f, "", 11, 11);
                K.SetBool(callWest, "summonOnly", true);
                K.SetBool(callEast, "summonOnly", true);
                Transform enc = K.Group("Begegnung Vorarbeiter Hask", slot, SaltMineBuilder.HaskSpot, Vector3.zero);
                var foreman = enc.gameObject.AddComponent<ForemanEncounter>();
                var so = new SerializedObject(foreman);
                so.FindProperty("bossArea").objectReferenceValue = haskArea;
                so.FindProperty("bossName").stringValue = "Vorarbeiter Hask";
                SerializedProperty calls = so.FindProperty("workerAreas");
                calls.arraySize = 2;
                calls.GetArrayElementAtIndex(0).objectReferenceValue = callWest;
                calls.GetArrayElementAtIndex(1).objectReferenceValue = callEast;
                so.ApplyModifiedPropertiesWithoutUndo();

                // Boss 2: Die Salzmutter im Salzsee, Krebsbrut schluepft an drei Stellen (M6.3)
                EnemySpawnArea motherArea = Area(g, "Boss Die Salzmutter", SaltMineBuilder.SaltMotherSpot, mother, motherPrefab, 1, 0f, "", 14, 14);
                var broods = new[]
                {
                    Area(g, "Krebsbrut (West)", SaltMineBuilder.BroodWest, brood2, broodPrefab2, 1, 1.5f, "", 10, 10),
                    Area(g, "Krebsbrut (Ost)", SaltMineBuilder.BroodEast, brood2, broodPrefab2, 1, 1.5f, "", 10, 10),
                    Area(g, "Krebsbrut (Nord)", SaltMineBuilder.BroodNorth, brood2, broodPrefab2, 1, 1.5f, "", 10, 10)
                };
                foreach (EnemySpawnArea b in broods) K.SetBool(b, "summonOnly", true);
                Transform enc2 = K.Group("Begegnung Die Salzmutter", slot, SaltMineBuilder.GrottoD, Vector3.zero);
                var motherEnc = enc2.gameObject.AddComponent<SaltMotherEncounter>();
                var so2 = new SerializedObject(motherEnc);
                so2.FindProperty("bossArea").objectReferenceValue = motherArea;
                so2.FindProperty("bossName").stringValue = "Die Salzmutter";
                SerializedProperty bp = so2.FindProperty("broodAreas");
                bp.arraySize = broods.Length;
                for (int i = 0; i < broods.Length; i++) bp.GetArrayElementAtIndex(i).objectReferenceValue = broods[i];
                so2.ApplyModifiedPropertiesWithoutUndo();

                // Endboss: Das Grubenherz, Lichtfresser loeschen die vier Lampen (M6.4)
                EnemySpawnArea heartArea = Area(g, "Boss Das Grubenherz", SaltMineBuilder.HeartSpot, heartDef, heartPrefab, 1, 0f, "", 14, 14);
                EnemySpawnArea eaterArea = Area(g, "Lichtfresser", SaltMineBuilder.HeartEaters, eater, eaterPrefab, 1, 2f, "", 12, 12);
                K.SetBool(eaterArea, "summonOnly", true);
                var lampList = new List<Ashveil.World.MineLamp>(slot.GetComponentsInChildren<Ashveil.World.MineLamp>(true));
                lampList.Sort((x, y) => x.Index.CompareTo(y.Index));
                Transform enc3 = K.Group("Begegnung Das Grubenherz", slot, SaltMineBuilder.HeartF, Vector3.zero);
                var heartEnc = enc3.gameObject.AddComponent<HeartEncounter>();
                var so3 = new SerializedObject(heartEnc);
                so3.FindProperty("bossArea").objectReferenceValue = heartArea;
                so3.FindProperty("bossName").stringValue = "Das Grubenherz";
                so3.FindProperty("eaterArea").objectReferenceValue = eaterArea;
                SerializedProperty lp = so3.FindProperty("lamps");
                lp.arraySize = lampList.Count;
                for (int i = 0; i < lampList.Count; i++) lp.GetArrayElementAtIndex(i).objectReferenceValue = lampList[i].transform;
                so3.ApplyModifiedPropertiesWithoutUndo();

                // Optional: Der Ertrunkene Kapitaen in der Wrackhoehle (M6.5)
                EnemySpawnArea captainArea = Area(g, "Boss Der Ertrunkene Kapitaen", SaltMineBuilder.CaptainSpot, captain, captainPrefab, 1, 0f, "", 13, 13);
                EnemySpawnArea crewN = Area(g, "Matrosen (Nord)", SaltMineBuilder.CrewNorth, sailor, sailorPrefab, 1, 1.5f, "", 11, 11);
                EnemySpawnArea crewS = Area(g, "Matrosen (Sued)", SaltMineBuilder.CrewSouth, sailor, sailorPrefab, 1, 1.5f, "", 11, 11);
                K.SetBool(crewN, "summonOnly", true);
                K.SetBool(crewS, "summonOnly", true);
                Transform enc4 = K.Group("Begegnung Der Ertrunkene Kapitaen", slot, SaltMineBuilder.WreckE, Vector3.zero);
                var capEnc = enc4.gameObject.AddComponent<CaptainEncounter>();
                var so4 = new SerializedObject(capEnc);
                so4.FindProperty("bossArea").objectReferenceValue = captainArea;
                so4.FindProperty("bossName").stringValue = "Der Ertrunkene Kapitän";
                SerializedProperty cp = so4.FindProperty("crewAreas");
                cp.arraySize = 2;
                cp.GetArrayElementAtIndex(0).objectReferenceValue = crewN;
                cp.GetArrayElementAtIndex(1).objectReferenceValue = crewS;
                so4.ApplyModifiedPropertiesWithoutUndo();
            }
            return new List<GameObject> { workerPrefab, crabPrefab, broodPrefab, brutePrefab, haskPrefab, motherPrefab, broodPrefab2, heartPrefab, eaterPrefab,
                                          captainPrefab, sailorPrefab };
        }

        /// <summary>Anzahl der Gegner, die eine frische Instanz bekommt (Tests).</summary>
        public const int SaltMineEnemiesPerInstance = 3 + 3 + 2 + 3 + 2 + 4 + 3 + 1 + 2 + 1 + 1 + 1 + 1; // + Hask, Salzmutter, Grubenherz, Kapitaen (Gerufene nicht)

        // ================================================================== Definitionen

        /// <summary>Schwer und langsam: grosser Ausholer, jeder 4. Schlag trifft in einem kleinen Bogen (Spitzhacke).</summary>
        private static void SetupMineWorker(EnemyDefinition d)
        {
            d.body = EnemyBody.Humanoid; d.style = EnemyStyle.Melee; d.femaleModel = false;
            d.minLevel = 10; d.maxLevel = 12;
            d.baseHealth = 170f; d.healthPerLevel = 18f;
            d.minDamage = 14f; d.maxDamage = 19f; d.damagePerLevel = 1.6f;
            d.attackRange = 2.4f; d.attackInterval = 2.6f; d.windup = 0.85f; d.attackArc = 130f;
            d.areaAttackEvery = 4; d.areaRadius = 2.5f; d.areaWindup = 1f; d.areaDamageMultiplier = 1.3f;
            d.walkSpeed = 1.1f; d.runSpeed = 4.4f; d.wanderRadius = 2f;
            d.aggroProfile = AggroProfile.Territorial; d.territoryRadius = 8f; d.aggroRadius = 11f; d.leashDistance = 30f;
            d.fleeHealthFraction = 0f;
            d.corpseTime = 40f; d.respawnTime = 99999f;
            d.bodyColor = new Color(0.7f, 0.69f, 0.66f); d.glowColor = new Color(1f, 0.55f, 0.35f); d.scale = 1f;
        }

        /// <summary>Zaeh, trifft leicht – graebt sich ins Salz und taucht woanders auf, springt Ziele an.</summary>
        private static void SetupSaltCrab(EnemyDefinition d)
        {
            d.body = EnemyBody.Beast; d.style = EnemyStyle.Melee;
            d.minLevel = 11; d.maxLevel = 14;
            d.baseHealth = 230f; d.healthPerLevel = 20f;
            d.minDamage = 8f; d.maxDamage = 12f; d.damagePerLevel = 1.1f;
            d.attackRange = 1.9f; d.attackInterval = 1.4f; d.windup = 0.4f; d.attackArc = 100f;
            d.diveWhenPressedFor = 5f; d.pounceRange = 7f;
            d.walkSpeed = 1.3f; d.runSpeed = 5.8f; d.wanderRadius = 3f;
            d.aggroProfile = AggroProfile.Territorial; d.territoryRadius = 6f; d.aggroRadius = 10f; d.leashDistance = 28f;
            d.fleeHealthFraction = 0f;
            d.corpseTime = 35f; d.respawnTime = 99999f;
            d.bodyColor = new Color(0.86f, 0.85f, 0.82f); d.glowColor = new Color(1f, 0.5f, 0.4f); d.scale = 0.85f;
        }

        /// <summary>Klein und schwach, kommt zu mehreren; spuckt Gift aus der Distanz.</summary>
        private static void SetupNeedleBrood(EnemyDefinition d)
        {
            d.body = EnemyBody.Beast; d.style = EnemyStyle.Ranged;
            d.minLevel = 11; d.maxLevel = 12;
            d.baseHealth = 70f; d.healthPerLevel = 8f;
            d.minDamage = 5f; d.maxDamage = 7f; d.damagePerLevel = 0.8f;
            d.attackRange = 16f; d.attackInterval = 2f; d.windup = 0.5f; d.attackArc = 70f;
            d.preferredDistance = 11f; d.tooCloseDistance = 5f; d.rangedAccuracy = 0.7f;
            d.poisonPerSecond = 3f; d.poisonDuration = 5f;
            d.walkSpeed = 1.4f; d.runSpeed = 5.5f; d.wanderRadius = 3f;
            d.aggroProfile = AggroProfile.Aggressive; d.aggroRadius = 12f; d.leashDistance = 30f;
            d.fleeHealthFraction = 0f;
            d.corpseTime = 25f; d.respawnTime = 99999f;
            d.bodyColor = new Color(0.55f, 0.58f, 0.5f); d.glowColor = new Color(0.5f, 1f, 0.3f); d.scale = 0.6f;
        }

        /// <summary>Elite: langsam, jeder 2. Angriff ein Stampfer mit grosser Flaeche und langer Warnzeit.</summary>
        private static void SetupSaltBrute(EnemyDefinition d)
        {
            d.body = EnemyBody.Colossus; d.style = EnemyStyle.Melee;
            d.minLevel = 13; d.maxLevel = 13;
            d.baseHealth = 1300f; d.healthPerLevel = 0f;
            d.minDamage = 28f; d.maxDamage = 36f; d.damagePerLevel = 0f;
            d.attackRange = 3.4f; d.attackInterval = 3f; d.windup = 1f; d.attackArc = 130f;
            d.areaAttackEvery = 2; d.areaRadius = 6.5f; d.areaWindup = 1.8f; d.areaDamageMultiplier = 1.5f;
            d.walkSpeed = 1f; d.runSpeed = 3f; d.wanderRadius = 1f;
            d.aggroProfile = AggroProfile.Territorial; d.territoryRadius = 9f; d.aggroRadius = 12f; d.leashDistance = 26f;
            d.fleeHealthFraction = 0f; d.elite = true;
            d.corpseTime = 60f; d.respawnTime = 99999f;
            d.bodyColor = new Color(0.86f, 0.85f, 0.82f); d.glowColor = new Color(1f, 0.4f, 0.35f); d.scale = 1.15f;
            ApplySaltBruteAnatomy(d);
        }

        [MenuItem("Ashveil/Kampf/Grubenschrat: Koerperteile und Moveset anwenden", priority = 500)]
        public static void ApplySaltBruteAnatomyMenu()
        {
            var d = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefFolder + "/Enemy_Grubenschrat.asset");
            if (d == null) { Debug.LogWarning("[Ashveil] Enemy_Grubenschrat fehlt – erst die Sandbox-Szene erstellen."); return; }
            ApplySaltBruteAnatomy(d);
            EditorUtility.SetDirty(d);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Ashveil] Grubenschrat: {d.bodyParts.Count} Koerperteile, {d.moveset.Count} Angriffe angewendet.");
        }

        /// <summary>
        /// Kampf-Prototyp (Combat-Masterplan §116, Anatomie §55): Kopf (x1,5, nur erreichbar, wenn er kniet oder sich
        /// herabbeugt), rechter Keulenarm (Bruch: Doppelschlag weg, dafuer Schulterramme und linke Faust; zerstoert: Unterarm
        /// faellt ab), linkes Bein (Bruch: einmal 8 s Niederwurf, danach langsamer). Moveset mit Gewichten und Abklingzeiten.
        /// </summary>
        [MenuItem("Ashveil/Kampf/Nachlese: Stoffe und Grubenschrat-Profil anlegen", priority = 501)]
        public static void ApplySaltBruteSalvageMenu()
        {
            ItemContentBuilder.Build();   // legt nur fehlende Gegenstaende an
            var d = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefFolder + "/Enemy_Grubenschrat.asset");
            if (d == null) { Debug.LogWarning("[Ashveil] Enemy_Grubenschrat fehlt – erst die Sandbox-Szene erstellen."); return; }
            ApplySaltBruteSalvage(d);
            EditorUtility.SetDirty(d);
            AssetDatabase.SaveAssets();
            DefinitionValidator.ValidateAndRebuild();
            Debug.Log($"[Ashveil] Nachlese: Grubenschrat-Profil mit {d.salvage.entries.Count} Stoffen, {d.salvage.rolls} Wurf je Heiler.");
        }

        /// <summary>
        /// Heiler-Nachlese, Prototyp §184 (ein Gegner, ein abtrennbares Teil, ein Wurf): der rechte Arm entscheidet, was
        /// sich bergen laesst – intakt eine Sehne (Verbaende), gebrochen das seltene Mark (Gegengift), abgetrennt die Klaue
        /// (Trophaee). Gewebe geht immer. Zerstoeren ist also nicht automatisch besser (§40). Elite: ein Wurf (§42).
        /// </summary>
        public static void ApplySaltBruteSalvage(EnemyDefinition d)
        {
            d.salvage = new SalvageProfile
            {
                rolls = 1,
                chance = 1f,
                minContribution = 0.03f,
                entries = new System.Collections.Generic.List<SalvageEntry>
                {
                    new SalvageEntry { partKey = "", condition = SalvagePartCondition.Any, item = "item.salt_tissue", minCount = 1, maxCount = 2, weight = 1f },
                    new SalvageEntry { partKey = "arm_r", condition = SalvagePartCondition.Intact, item = "item.salt_sinew", weight = 1.5f },
                    new SalvageEntry { partKey = "arm_r", condition = SalvagePartCondition.Broken, item = "item.salt_marrow", weight = 0.6f, rare = true },
                    new SalvageEntry { partKey = "arm_r", condition = SalvagePartCondition.Destroyed, item = "item.brute_claw", weight = 1.5f },
                }
            };
        }

        public static void ApplySaltBruteAnatomy(EnemyDefinition d)
        {
            ApplySaltBruteSalvage(d);
            const byte Head = 1, ArmR = 2, LegL = 3;
            d.bodyParts = new System.Collections.Generic.List<BodyPartDefinition>
            {
                new BodyPartDefinition
                {
                    id = Head, key = "head", displayName = "Kopf", pivot = "Head",
                    hitboxCenter = new Vector3(0f, 0.2f, 0.05f), hitboxSize = new Vector3(0.8f, 0.85f, 0.8f),
                    reach = BodyPartReach.WhenLowered, healthShare = 0.25f, damageMultiplier = 1.5f, damagedAt = 0.5f,
                    staggerSeconds = 3f, bonusLootItem = "item.grave_salt", bonusLootCount = 1
                },
                new BodyPartDefinition
                {
                    id = ArmR, key = "arm_r", displayName = "Rechter Arm", pivot = "Arm R",
                    hitboxCenter = new Vector3(0f, -0.85f, 0.04f), hitboxSize = new Vector3(0.6f, 1.95f, 0.6f),
                    reach = BodyPartReach.Always, healthShare = 0.22f, damageMultiplier = 1f, damagedAt = 0.5f,
                    destroyOverkill = 0.4f, bonusLootItem = "item.red_vein_salt", bonusLootCount = 2
                },
                new BodyPartDefinition
                {
                    id = LegL, key = "leg_l", displayName = "Linkes Bein", pivot = "Leg L",
                    hitboxCenter = new Vector3(0f, -0.65f, 0.05f), hitboxSize = new Vector3(0.6f, 1.35f, 0.7f),
                    reach = BodyPartReach.Always, healthShare = 0.22f, damageMultiplier = 1f, damagedAt = 0.5f,
                    knockdownSeconds = 8f, speedAfterBreak = 0.7f, bonusLootItem = "item.mine_salt", bonusLootCount = 2
                }
            };
            d.moveset = new System.Collections.Generic.List<EnemyAttackDefinition>
            {
                new EnemyAttackDefinition
                {
                    id = 1, displayName = "Doppelschlag", kind = EnemyMoveKind.Strike, pose = EnemyMovePose.RightArm,
                    minRange = 0f, maxRange = 3.6f, windup = 1f, damageMultiplier = 1f, cooldown = 4f, weight = 3f, arc = 130f,
                    knockback = 0.4f, followUp = 2, followUpWindup = 0.55f, requiresIntactPart = ArmR
                },
                new EnemyAttackDefinition
                {
                    id = 2, displayName = "Nachschlag", kind = EnemyMoveKind.Strike, pose = EnemyMovePose.RightArm,
                    minRange = 0f, maxRange = 4f, windup = 0.55f, damageMultiplier = 1.2f, cooldown = 0f, weight = 0f, arc = 140f,
                    knockback = 0.6f, requiresIntactPart = ArmR, lowersHeadFor = 1.4f
                },
                new EnemyAttackDefinition
                {
                    id = 3, displayName = "Stampfen", kind = EnemyMoveKind.Area, pose = EnemyMovePose.Stomp,
                    minRange = 0f, maxRange = 5f, windup = 1.8f, damageMultiplier = 1.5f, cooldown = 9f, weight = 2f,
                    areaRadius = 6.5f, knockback = 1.2f, retreatAfter = 3f
                },
                new EnemyAttackDefinition
                {
                    id = 4, displayName = "Ansturm", kind = EnemyMoveKind.Charge, pose = EnemyMovePose.Charge,
                    minRange = 6f, maxRange = 14f, windup = 1.1f, damageMultiplier = 1.4f, cooldown = 12f, weight = 2f, arc = 360f,
                    knockback = 1.2f, requiresIntactPart = LegL
                },
                new EnemyAttackDefinition
                {
                    id = 5, displayName = "Schulterramme", kind = EnemyMoveKind.Strike, pose = EnemyMovePose.Shoulder,
                    minRange = 0f, maxRange = 3.4f, windup = 0.8f, damageMultiplier = 1.1f, cooldown = 5f, weight = 3f, arc = 120f,
                    knockback = 1.4f, onlyWhenPartBroken = ArmR, lowersHeadFor = 1.2f
                },
                new EnemyAttackDefinition
                {
                    id = 6, displayName = "Linker Faustschlag", kind = EnemyMoveKind.Strike, pose = EnemyMovePose.LeftArm,
                    minRange = 0f, maxRange = 3.4f, windup = 0.7f, damageMultiplier = 0.9f, cooldown = 3f, weight = 3f, arc = 130f,
                    knockback = 0.3f, onlyWhenPartBroken = ArmR
                }
            };
            d.woundedBelow = 0.66f;
            d.criticalBelow = 0.33f;
        }

        /// <summary>
        /// Boss 1 (M6.2): schwerer Hackenschlag, jeder 5. ein Wirbel um sich selbst. Sprengladungen und das Rufen
        /// der Arbeiter steuert die <see cref="ForemanEncounter"/> in der Szene.
        /// </summary>
        private static void SetupForemanHask(EnemyDefinition d)
        {
            d.body = EnemyBody.Humanoid; d.style = EnemyStyle.Melee; d.femaleModel = false;
            d.minLevel = 14; d.maxLevel = 14;
            d.baseHealth = 3600f; d.healthPerLevel = 0f;
            d.minDamage = 20f; d.maxDamage = 26f; d.damagePerLevel = 0f;
            d.attackRange = 2.7f; d.attackInterval = 2.4f; d.windup = 0.8f; d.attackArc = 120f;
            d.areaAttackEvery = 5; d.areaRadius = 4f; d.areaWindup = 1.4f; d.areaDamageMultiplier = 1.4f;
            d.walkSpeed = 1.2f; d.runSpeed = 4.6f; d.wanderRadius = 0f;
            d.aggroProfile = AggroProfile.Encounter; d.aggroRadius = 14f; d.leashDistance = 28f;
            d.fleeHealthFraction = 0f; d.elite = true;
            d.corpseTime = 120f; d.respawnTime = 99999f;
            d.bodyColor = new Color(0.62f, 0.42f, 0.34f); d.glowColor = new Color(1f, 0.6f, 0.3f); d.scale = 1.3f;
        }

        /// <summary>
        /// Boss 2 (M6.3): riesiger Salzkrebs. Scherenschlaege, jeder 4. ein Schlag auf den Grund (5 m). Salzspucke,
        /// Flut und Krebsbrut steuert die <see cref="SaltMotherEncounter"/>.
        /// </summary>
        private static void SetupSaltMother(EnemyDefinition d)
        {
            d.body = EnemyBody.Beast; d.style = EnemyStyle.Melee;
            d.minLevel = 14; d.maxLevel = 14;
            d.baseHealth = 4200f; d.healthPerLevel = 0f;
            d.minDamage = 18f; d.maxDamage = 24f; d.damagePerLevel = 0f;
            d.attackRange = 3.4f; d.attackInterval = 2.2f; d.windup = 0.7f; d.attackArc = 140f;
            d.areaAttackEvery = 4; d.areaRadius = 5f; d.areaWindup = 1.5f; d.areaDamageMultiplier = 1.3f;
            d.walkSpeed = 1.2f; d.runSpeed = 4.2f; d.wanderRadius = 0f;
            d.aggroProfile = AggroProfile.Encounter; d.aggroRadius = 16f; d.leashDistance = 30f;
            d.fleeHealthFraction = 0f; d.elite = true;
            d.corpseTime = 120f; d.respawnTime = 99999f;
            d.bodyColor = new Color(0.78f, 0.62f, 0.6f); d.glowColor = new Color(1f, 0.45f, 0.4f); d.scale = 2.4f;
        }

        /// <summary>Frisch geschluepfte Salzkrebse: schwach, flink, springen an – kommen immer zu dritt.</summary>
        private static void SetupCrabBrood(EnemyDefinition d)
        {
            d.body = EnemyBody.Beast; d.style = EnemyStyle.Melee;
            d.minLevel = 10; d.maxLevel = 10;
            d.baseHealth = 90f; d.healthPerLevel = 8f;
            d.minDamage = 5f; d.maxDamage = 7f; d.damagePerLevel = 0.6f;
            d.attackRange = 1.6f; d.attackInterval = 1.2f; d.windup = 0.35f; d.attackArc = 100f;
            d.pounceRange = 6f;
            d.walkSpeed = 1.5f; d.runSpeed = 6.2f; d.wanderRadius = 2f;
            d.aggroProfile = AggroProfile.Aggressive; d.aggroRadius = 14f; d.leashDistance = 30f;
            d.fleeHealthFraction = 0f;
            d.corpseTime = 20f; d.respawnTime = 99999f;
            d.bodyColor = new Color(0.93f, 0.9f, 0.86f); d.glowColor = new Color(1f, 0.55f, 0.5f); d.scale = 0.5f;
        }

        /// <summary>
        /// Endboss (M6.4): das versteinerte Herz eines der Alten, als Kristallriese aus dem Herzkranz erhoben. Bewegt sich
        /// nicht; Salzdornen im Umkreis von 8 m, jeder 3. Schlag ein Puls (10 m). Lichtfresser, Lampen und Finsternis
        /// steuert die <see cref="HeartEncounter"/>.
        /// </summary>
        private static void SetupMineHeart(EnemyDefinition d)
        {
            d.body = EnemyBody.Colossus; d.style = EnemyStyle.Melee;
            d.minLevel = 14; d.maxLevel = 14;
            d.baseHealth = 6000f; d.healthPerLevel = 0f;
            d.minDamage = 22f; d.maxDamage = 30f; d.damagePerLevel = 0f;
            d.attackRange = 8f; d.attackInterval = 2.8f; d.windup = 1f; d.attackArc = 360f;
            d.areaAttackEvery = 3; d.areaRadius = 10f; d.areaWindup = 1.8f; d.areaDamageMultiplier = 1.2f;
            d.walkSpeed = 0.1f; d.runSpeed = 0.1f; d.wanderRadius = 0f;
            d.aggroProfile = AggroProfile.Encounter; d.aggroRadius = 18f; d.leashDistance = 40f;
            d.fleeHealthFraction = 0f; d.elite = true;
            d.corpseTime = 180f; d.respawnTime = 99999f;
            d.bodyColor = new Color(0.55f, 0.16f, 0.14f); d.glowColor = new Color(1f, 0.35f, 0.3f); d.scale = 1.6f;
        }

        /// <summary>Schatten aus dem Herzen: kriechen zu einer Lampe und loeschen sie. Greifen nur an, wenn man sie stoert.</summary>
        private static void SetupLightEater(EnemyDefinition d)
        {
            d.body = EnemyBody.Beast; d.style = EnemyStyle.Melee;
            d.minLevel = 12; d.maxLevel = 12;
            d.baseHealth = 150f; d.healthPerLevel = 10f;
            d.minDamage = 9f; d.maxDamage = 12f; d.damagePerLevel = 0.8f;
            d.attackRange = 1.9f; d.attackInterval = 1.5f; d.windup = 0.45f; d.attackArc = 100f;
            d.walkSpeed = 2.4f; d.runSpeed = 2.6f; d.wanderRadius = 0f;
            d.aggroProfile = AggroProfile.Defensive; d.aggroRadius = 8f; d.leashDistance = 30f;
            d.fleeHealthFraction = 0f;
            d.corpseTime = 10f; d.respawnTime = 99999f;
            d.bodyColor = new Color(0.05f, 0.05f, 0.07f); d.glowColor = new Color(0.7f, 0.8f, 1f); d.scale = 0.9f;
        }

        /// <summary>
        /// Optionaler Boss (M6.5): Saebelhiebe, jeder 4. ein weiter Rundschlag. Breitseiten und Matrosen steuert die
        /// <see cref="CaptainEncounter"/>.
        /// </summary>
        private static void SetupDrownedCaptain(EnemyDefinition d)
        {
            d.body = EnemyBody.Humanoid; d.style = EnemyStyle.Melee; d.femaleModel = false;
            d.minLevel = 13; d.maxLevel = 13;
            d.baseHealth = 3000f; d.healthPerLevel = 0f;
            d.minDamage = 18f; d.maxDamage = 24f; d.damagePerLevel = 0f;
            d.attackRange = 2.6f; d.attackInterval = 1.9f; d.windup = 0.6f; d.attackArc = 120f;
            d.areaAttackEvery = 4; d.areaRadius = 3.5f; d.areaWindup = 1.2f; d.areaDamageMultiplier = 1.3f;
            d.walkSpeed = 1.3f; d.runSpeed = 4.8f; d.wanderRadius = 0f;
            d.aggroProfile = AggroProfile.Encounter; d.aggroRadius = 13f; d.leashDistance = 26f;
            d.fleeHealthFraction = 0f; d.elite = true;
            d.corpseTime = 120f; d.respawnTime = 99999f;
            d.bodyColor = new Color(0.28f, 0.34f, 0.4f); d.glowColor = new Color(0.6f, 0.85f, 1f); d.scale = 1.2f;
        }

        /// <summary>Die Mannschaft des Kapitaens: mittelschwer, greifen sofort an, kommen nur auf seinen Ruf.</summary>
        private static void SetupDrownedSailor(EnemyDefinition d)
        {
            d.body = EnemyBody.Humanoid; d.style = EnemyStyle.Melee; d.femaleModel = false;
            d.minLevel = 11; d.maxLevel = 11;
            d.baseHealth = 150f; d.healthPerLevel = 15f;
            d.minDamage = 10f; d.maxDamage = 14f; d.damagePerLevel = 1.2f;
            d.attackRange = 2.2f; d.attackInterval = 1.8f; d.windup = 0.55f; d.attackArc = 110f;
            d.walkSpeed = 1.3f; d.runSpeed = 5f; d.wanderRadius = 1f;
            d.aggroProfile = AggroProfile.Aggressive; d.aggroRadius = 12f; d.leashDistance = 28f;
            d.fleeHealthFraction = 0f;
            d.corpseTime = 30f; d.respawnTime = 99999f;
            d.bodyColor = new Color(0.36f, 0.45f, 0.48f); d.glowColor = new Color(0.6f, 0.85f, 1f); d.scale = 1f;
        }

        // ================================================================== Hilfen

        /// <summary>Materialien eines gespeicherten Prefabs austauschen (Varianten derselben Koerperform).</summary>
        private static GameObject Tint(GameObject prefab, params (Material from, Material to)[] map)
        {
            if (prefab == null) return null;
            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    foreach ((Material from, Material to) in map)
                        if (mats[i] == from) { mats[i] = to; changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
    }
}
