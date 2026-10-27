using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Enemies;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;
using KC = Ashveil.Data.KarethCatalog;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// #28 Kareth v4, Welle 0: die neuen Greybox-Tiere der Ergänzungsaufträge (Max 05.10.: „zuerst als Greybox“; echte Modelle nach
    /// Spieltest r2, Max 07.10. K11) und zwei Kareth-Varianten vorhandener Gegner. Gestalt und Verhalten nach den Bausteinen 28, 29 und
    /// 32 (Bestiarium 02.10., VORSCHLAG) und dem Bauplan #28 v2; Werte Claude, Leben und Schaden aus der Balance-Tabelle (Rolle Normal).
    ///   · Furchenwühler (1–3): neutral; Schnauzenstoß, Schulterrammer mit sichtbarem Absenken (nur mehr Schaden, kein Rückstoß)
    ///   · Uferklapper (2–4): neutral, wachsam; Scherengriff, vorbereiteter Klappenschlag (nur mehr Schaden)
    ///   · Schlickwerfer (5–6): territorial; wie der Uferklapper, dazu ab und zu ein Schlickwurf auf Abstand (nur Schaden)
    ///   · Flüsterbalg (3–6): neutral am Waldrand, territorial am Nest (Gebiet); Greifhieb, kurzer Satz, Schriller Ausstoß (Fläche)
    ///   · Hungriger Deserteur (7, U-7) und Ertrunkener Matrose am Wrack (6–7, U-3, normaler Gegner statt Dungeon-Gegner)
    /// Nicht in der Greybox: Mimikry-Geräusche, Hängeplätze, Graben und Klappern als eigene Bewegungen (Bauplan Teil 3).
    /// Die Definitionen bekommen bei jedem Aufruf die Werte aus dem Code (Greybox-Stand, wie der Mähnenreißer); Beute hängt
    /// <see cref="ItemContentBuilder.AssignKarethLoot"/> an. Neue Prefabs kommen hinten an die Liste der Netz-Prefabs.
    /// Dazu: die Deserteure der Grenzgebirge-Platzhalter tragen statt Asche-Haut und Glutaugen einen Menschen-Platzhalter
    /// (U-6 nutzt sie in Kareth; WORLD-218: Deserteure sind Menschen) – Prefabs an Ort und Stelle angepasst, Kennungen bleiben.
    /// </summary>
    public static partial class EnemyBuilder
    {
        private static Material _kRooter, _kRooterDark, _kRooterSnout, _kClacker, _kClackerDark, _kClackerRim, _kClackerRust,
            _kSlinger, _kMud, _kMudTrail, _kBellows, _kBellowsDark, _kSac, _kDeserterCloth, _kDeserterEye, _kDrownedSkin;

        public const string FileRooter = "Enemy_Furchenwuehler", FileClacker = "Enemy_Uferklapper", FileSlinger = "Enemy_Schlickwerfer",
            FileBellows = "Enemy_Fluesterbalg", FileReedDeserter = "Enemy_HungrigerDeserteur", FileWreckSailor = "Enemy_ErtrunkenerMatroseWrack";

        /// <summary>Gegner-ID → Dateiname (Definition und Prefab teilen den Namen) – auch für <see cref="KarethV4Gegner"/>.</summary>
        public static readonly Dictionary<string, string> KarethFiles = new Dictionary<string, string>
        {
            { KC.EnemyRooter, FileRooter }, { KC.EnemyClacker, FileClacker }, { KC.EnemySlinger, FileSlinger },
            { KC.EnemyBellows, FileBellows }, { KC.EnemyReedDeserter, FileReedDeserter }, { KC.EnemyWreckSailor, FileWreckSailor },
        };

        /// <summary>Definitionen und Greybox-Prefabs der #28-Gegner (idempotent), Netz-Anmeldung. Schlüssel = Gegner-ID.</summary>
        public static Dictionary<string, BorderEnemy> BuildKarethCreatures()
        {
            ProjectSetup.EnsureFolder(DefFolder);
            ProjectSetup.EnsureFolder(PrefabFolder);
            CreateMaterials();
            CreateKarethMaterials();
            var r = new Dictionary<string, BorderEnemy>();

            EnemyDefinition rooter = EnsureKarethDefinition(FileRooter, KC.EnemyRooter, "Furchenwühler", SetupRooter);
            EnemyDefinition clacker = EnsureKarethDefinition(FileClacker, KC.EnemyClacker, "Uferklapper", SetupClacker);
            EnemyDefinition slinger = EnsureKarethDefinition(FileSlinger, KC.EnemySlinger, "Schlickwerfer", SetupSlinger);
            EnemyDefinition bellows = EnsureKarethDefinition(FileBellows, KC.EnemyBellows, "Flüsterbalg", SetupBellows);
            EnemyDefinition reed = EnsureKarethDefinition(FileReedDeserter, KC.EnemyReedDeserter, "Hungriger Deserteur", SetupReedDeserter);
            EnemyDefinition sailor = EnsureKarethDefinition(FileWreckSailor, KC.EnemyWreckSailor, "Ertrunkener Matrose", SetupWreckSailor);

            r[KC.EnemyRooter] = new BorderEnemy { Def = rooter, Prefab = BuildRooterPrefab(rooter) };
            r[KC.EnemyClacker] = new BorderEnemy { Def = clacker, Prefab = BuildClackerPrefab(clacker, false) };
            r[KC.EnemySlinger] = new BorderEnemy { Def = slinger, Prefab = BuildClackerPrefab(slinger, true) };
            r[KC.EnemyBellows] = new BorderEnemy { Def = bellows, Prefab = BuildBellowsPrefab(bellows) };
            r[KC.EnemyReedDeserter] = new BorderEnemy
                { Def = reed, Prefab = Tint(BuildHumanoidPrefab(reed, false), (_ashSkin, _kDeserterCloth), (_ember, _kDeserterEye)) };
            r[KC.EnemyWreckSailor] = new BorderEnemy { Def = sailor, Prefab = Tint(BuildHumanoidPrefab(sailor, false), (_ashSkin, _kDrownedSkin)) };

            var prefabs = new List<GameObject>();
            foreach (BorderEnemy e in r.Values) if (e.Prefab != null) prefabs.Add(e.Prefab);
            NetworkSetupBuilder.AddSpawnables(prefabs);
            AssetDatabase.SaveAssets();
            return r;
        }

        /// <summary>Wie <see cref="EnsureDefinition"/>, schreibt die Greybox-Werte aber bei jedem Aufruf (Werte stehen im Code).</summary>
        private static EnemyDefinition EnsureKarethDefinition(string file, string id, string displayName, System.Action<EnemyDefinition> setup)
        {
            EnemyDefinition def = EnsureDefinition(file, id, displayName, setup);
            setup(def);
            EditorUtility.SetDirty(def);
            return def;
        }

        /// <summary>
        /// Deserteure (Grenzgebirge-Platzhalter, in Kareth für U-6): Menschen-Platzhalter statt Asche-Haut und Glutaugen.
        /// Prefabs an Ort und Stelle, Kennungen und Netz-Objekte bleiben. Mehrfach aufrufbar.
        /// </summary>
        public static string ApplyDeserterLook()
        {
            CreateMaterials();
            CreateKarethMaterials();
            var log = new List<string>();
            foreach (string file in new[] { "Enemy_Deserteur", "Enemy_DeserteurSchuetzin" })
                log.Add(PatchPrefab(file, root =>
                {
                    int n = 0;
                    foreach (Renderer ren in root.GetComponentsInChildren<Renderer>(true))
                    {
                        Material[] mats = ren.sharedMaterials;
                        bool changed = false;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            if (mats[i] == _ashSkin) { mats[i] = _kDeserterCloth; changed = true; }
                            else if (mats[i] == _ember) { mats[i] = _kDeserterEye; changed = true; }
                        }
                        if (!changed) continue;
                        ren.sharedMaterials = mats;
                        n++;
                    }
                    return n > 0 ? $"{n} Teile Menschen-Platzhalter" : "schon angepasst";
                }));
            AssetDatabase.SaveAssets();
            return string.Join(" · ", log);
        }

        private static void CreateKarethMaterials()
        {
            // Bausteine 28/29/32: natürliche Tiere, keine leuchtenden Augen, keine magischen Adern
            _kRooter = K.Mat("Enemy_RooterFur", new Color(0.36f, 0.27f, 0.18f), 0.05f);
            _kRooterDark = K.Mat("Enemy_RooterDark", new Color(0.15f, 0.11f, 0.08f), 0.05f);
            _kRooterSnout = K.Mat("Enemy_RooterSnout", new Color(0.55f, 0.45f, 0.36f), 0.12f);
            _kClacker = K.Mat("Enemy_ClackerShell", new Color(0.33f, 0.34f, 0.3f), 0.45f);
            _kClackerDark = K.Mat("Enemy_ClackerDark", new Color(0.21f, 0.21f, 0.18f), 0.4f);
            _kClackerRim = K.Mat("Enemy_ClackerRim", new Color(0.6f, 0.55f, 0.44f), 0.35f);
            _kClackerRust = K.Mat("Enemy_ClackerRust", new Color(0.48f, 0.31f, 0.2f), 0.3f);
            _kSlinger = K.Mat("Enemy_SlingerShell", new Color(0.24f, 0.24f, 0.21f), 0.5f);
            _kMud = K.Mat("Enemy_Mud", new Color(0.24f, 0.19f, 0.13f), 0.6f);
            _kMudTrail = UnlitTransparent("Enemy_MudTrail", new Color(0.26f, 0.2f, 0.13f, 0.95f));
            _kBellows = K.Mat("Enemy_BellowsSkin", new Color(0.42f, 0.4f, 0.33f), 0.15f);
            _kBellowsDark = K.Mat("Enemy_BellowsDark", new Color(0.25f, 0.23f, 0.19f), 0.1f);
            _kSac = K.Mat("Enemy_BellowsSac", new Color(0.7f, 0.55f, 0.45f), 0.25f);
            _kDeserterCloth = K.Mat("Enemy_DeserterCloth", new Color(0.36f, 0.31f, 0.25f), 0.1f);
            _kDeserterEye = K.Mat("Enemy_DeserterEye", new Color(0.12f, 0.1f, 0.08f), 0.6f);
            _kDrownedSkin = K.Mat("Enemy_DrownedSkin", new Color(0.36f, 0.45f, 0.48f), 0.45f); // wie in den Salzgruben
        }

        // ================================================================== Definitionen

        private static void SetupRooter(EnemyDefinition d)
        {
            d.body = EnemyBody.Beast; d.style = EnemyStyle.Melee;
            d.minLevel = 1; d.maxLevel = 3;
            d.baseHealth = 50f; d.healthPerLevel = 12f; d.minDamage = 3f; d.maxDamage = 5f; d.damagePerLevel = 1f;
            d.attackRange = 1.9f; d.attackInterval = 1.7f; d.windup = 0.5f; d.attackArc = 110f;
            d.walkSpeed = 1.2f; d.runSpeed = 5.4f; d.wanderRadius = 5f;
            d.aggroProfile = AggroProfile.Passive; d.neutral = true; d.aggroRadius = 10f; d.territoryRadius = 5f; d.leashDistance = 35f;
            d.fleeHealthFraction = 0f; d.fleesWhenAttacked = false; d.sidestepChance = 0f; d.howlsOnEngage = false;
            d.throwRange = 0f;
            d.corpseTime = 25f; d.respawnTime = 40f; d.respawnTimeMax = 0f;
            d.bodyColor = new Color(0.36f, 0.27f, 0.18f); d.glowColor = new Color(0.55f, 0.45f, 0.36f); d.scale = 1f;
            d.moveset = new List<EnemyAttackDefinition>
            {
                new EnemyAttackDefinition
                {
                    id = 1, displayName = "Schnauzenstoß", kind = EnemyMoveKind.Strike, pose = EnemyMovePose.Default,
                    minRange = 0f, maxRange = 2.0f, windup = 0.5f, damageMultiplier = 1f, cooldown = 0f, weight = 4f, arc = 110f, knockback = 0.3f
                },
                new EnemyAttackDefinition
                {
                    // Baustein 28: senkt vorher kurz den Kopf; in der Einstiegsvariante nur mehr Schaden – kein Betäuben, kein Rückstoß
                    id = 2, displayName = "Schulterrammer", kind = EnemyMoveKind.Charge, pose = EnemyMovePose.Charge,
                    minRange = 3f, maxRange = 9f, windup = 0.95f, damageMultiplier = 1.35f, cooldown = 9f, weight = 3f, arc = 360f, knockback = 0f
                }
            };
        }

        private static void SetupClacker(EnemyDefinition d)
        {
            d.body = EnemyBody.Beast; d.style = EnemyStyle.Melee;
            d.minLevel = 2; d.maxLevel = 4;
            d.baseHealth = 60f; d.healthPerLevel = 13f; d.minDamage = 3f; d.maxDamage = 6f; d.damagePerLevel = 1f;
            d.attackRange = 2.2f; d.attackInterval = 1.8f; d.windup = 0.55f; d.attackArc = 100f;
            d.walkSpeed = 0.9f; d.runSpeed = 4.4f; d.wanderRadius = 4f;
            d.aggroProfile = AggroProfile.Passive; d.neutral = true; d.aggroRadius = 10f; d.territoryRadius = 5f; d.leashDistance = 30f;
            d.fleeHealthFraction = 0f; d.fleesWhenAttacked = false; d.sidestepChance = 0f; d.howlsOnEngage = false;
            d.throwRange = 0f;
            d.corpseTime = 25f; d.respawnTime = 45f; d.respawnTimeMax = 0f;
            d.bodyColor = new Color(0.33f, 0.34f, 0.3f); d.glowColor = new Color(0.6f, 0.55f, 0.44f); d.scale = 1.1f;
            d.moveset = new List<EnemyAttackDefinition>
            {
                new EnemyAttackDefinition
                {
                    id = 1, displayName = "Scherengriff", kind = EnemyMoveKind.Strike, pose = EnemyMovePose.RightArm,
                    minRange = 0f, maxRange = 2.2f, windup = 0.55f, damageMultiplier = 1f, cooldown = 0f, weight = 4f, arc = 100f, knockback = 0.2f
                },
                new EnemyAttackDefinition
                {
                    // Baustein 29: beide Scheren nach vorn, kurze Vorbereitung, dann zusammen – nur erhöhter Schaden, kein Festhalten
                    id = 2, displayName = "Klappenschlag", kind = EnemyMoveKind.Strike, pose = EnemyMovePose.BothArms,
                    minRange = 0f, maxRange = 2.4f, windup = 1.2f, damageMultiplier = 1.6f, cooldown = 7f, weight = 2f, arc = 150f, knockback = 0f
                }
            };
        }

        private static void SetupSlinger(EnemyDefinition d)
        {
            SetupClacker(d);
            d.minLevel = 5; d.maxLevel = 6;
            d.aggroProfile = AggroProfile.Territorial; d.neutral = false; d.territoryRadius = 7f; d.aggroRadius = 12f; d.leashDistance = 32f;
            // Baustein 29 „Spätere Fähigkeit: Schlickwurf“: klar erkennbare Vorbereitung, verständliche Flugbahn, begrenzte Reichweite, nur Schaden
            d.throwRange = 16f; d.throwMinRange = 6f; d.throwCooldown = 8f; d.throwWindup = 0.9f; d.throwDamageFactor = 0.8f; d.throwAccuracy = 0.75f;
            d.respawnTime = 50f;
            d.bodyColor = new Color(0.24f, 0.24f, 0.21f); d.scale = 1.15f;
        }

        private static void SetupBellows(EnemyDefinition d)
        {
            d.body = EnemyBody.Beast; d.style = EnemyStyle.Melee;
            d.minLevel = 3; d.maxLevel = 6;
            d.baseHealth = 65f; d.healthPerLevel = 12f; d.minDamage = 4f; d.maxDamage = 6f; d.damagePerLevel = 1.1f;
            d.attackRange = 2.0f; d.attackInterval = 1.7f; d.windup = 0.5f; d.attackArc = 110f;
            d.walkSpeed = 1.3f; d.runSpeed = 5.8f; d.wanderRadius = 3f;
            d.aggroProfile = AggroProfile.Passive; d.neutral = true; d.aggroRadius = 11f; d.territoryRadius = 7f; d.leashDistance = 32f;
            d.fleeHealthFraction = 0f; d.fleesWhenAttacked = false; d.sidestepChance = 0f; d.howlsOnEngage = false;
            d.throwRange = 0f;
            d.corpseTime = 25f; d.respawnTime = 45f; d.respawnTimeMax = 0f;
            d.bodyColor = new Color(0.42f, 0.4f, 0.33f); d.glowColor = new Color(0.7f, 0.55f, 0.45f); d.scale = 1f;
            d.moveset = new List<EnemyAttackDefinition>
            {
                new EnemyAttackDefinition
                {
                    id = 1, displayName = "Greifhieb", kind = EnemyMoveKind.Strike, pose = EnemyMovePose.LeftArm,
                    minRange = 0f, maxRange = 2.0f, windup = 0.5f, damageMultiplier = 1f, cooldown = 0f, weight = 4f, arc = 110f, knockback = 0.2f
                },
                new EnemyAttackDefinition
                {
                    // Baustein 32: kurzer Satz auf eine erreichbare Bodenposition, dann gewöhnlicher Kampf
                    id = 2, displayName = "Kurzer Satz", kind = EnemyMoveKind.Charge, pose = EnemyMovePose.Charge,
                    minRange = 3.5f, maxRange = 8f, windup = 0.7f, damageMultiplier = 1.15f, cooldown = 9f, weight = 2f, arc = 360f, knockback = 0f
                },
                new EnemyAttackDefinition
                {
                    // Baustein 32 „Schriller Ausstoß“: richtet sich auf, bläht den Kehlsack, kurzer Schallangriff – Warnring, begrenzte
                    // Reichweite, gewöhnlicher Schaden, keine Betäubung; danach eine Weile nur normale Angriffe
                    id = 3, displayName = "Schriller Ausstoß", kind = EnemyMoveKind.Area, pose = EnemyMovePose.Default,
                    minRange = 0f, maxRange = 3f, windup = 1.3f, damageMultiplier = 1.4f, cooldown = 10f, weight = 2f, arc = 360f, knockback = 0f,
                    areaRadius = 3.5f
                }
            };
        }

        private static void SetupReedDeserter(EnemyDefinition d)
        {
            SetupHusk(d);
            d.minLevel = 7; d.maxLevel = 7;
            d.walkSpeed = 1.4f; d.runSpeed = 5.4f; d.wanderRadius = 2f;
            d.aggroProfile = AggroProfile.Territorial; d.neutral = false; d.territoryRadius = 8f; d.aggroRadius = 12f; d.leashDistance = 30f;
            d.fleeHealthFraction = 0f; // er verteidigt sein Versteck
            d.throwRange = 0f;
            d.corpseTime = 30f; d.respawnTime = 60f; d.respawnTimeMax = 0f;
            d.bodyColor = new Color(0.36f, 0.31f, 0.25f); d.glowColor = new Color(0.5f, 0.42f, 0.32f);
        }

        private static void SetupWreckSailor(EnemyDefinition d)
        {
            SetupDrownedSailor(d);
            d.minLevel = 6; d.maxLevel = 7;
            d.aggroProfile = AggroProfile.Territorial; d.neutral = false; d.territoryRadius = 7f; d.aggroRadius = 12f; d.leashDistance = 30f;
            d.throwRange = 0f;
            d.corpseTime = 25f; d.respawnTime = 50f; d.respawnTimeMax = 0f;
        }

        // ================================================================== Furchenwühler (Baustein 28)

        /// <summary>
        /// Kniehohes Bodentier, deutlich länger als hoch: schwere Vorderhand, niedriger Rücken, kompakte Hinterbeine, breiter Kopf mit
        /// kurzen seitlichen Ohren, flache helle Schnauze, dunkle Grabkrallen, schmaler Borstenkamm zwischen Nacken und Schultern.
        /// </summary>
        private static GameObject BuildRooterPrefab(EnemyDefinition def)
        {
            var root = new GameObject("Enemy_" + def.name.Replace("Enemy_", ""));
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 2; col.radius = 0.3f; col.height = 1.15f; col.center = new Vector3(0f, 0.4f, 0f);

            Transform visual = K.Group("Visual", root.transform);
            Transform model = K.Group("Model", visual);
            model.localScale = Vector3.one * def.scale;

            K.Prim(PrimitiveType.Capsule, "Body", model, new Vector3(0f, 0.4f, -0.04f), new Vector3(0.46f, 0.5f, 0.42f), new Vector3(90f, 0f, 0f), _kRooter, false);
            K.Prim(PrimitiveType.Sphere, "Shoulders", model, new Vector3(0f, 0.47f, 0.24f), new Vector3(0.56f, 0.5f, 0.5f), Vector3.zero, _kRooter, false);
            K.Prim(PrimitiveType.Sphere, "Hindquarters", model, new Vector3(0f, 0.37f, -0.32f), new Vector3(0.44f, 0.38f, 0.42f), Vector3.zero, _kRooter, false);
            for (int i = 0; i < 5; i++)
                K.Box("Bristle", model, new Vector3(0f, 0.72f - i * 0.012f, 0.42f - i * 0.09f), new Vector3(0.05f, 0.13f, 0.06f), _kRooterDark,
                    new Vector3(-30f, 0f, 0f), false);

            Transform head = K.Group("Head", model, new Vector3(0f, 0.47f, 0.55f), Vector3.zero);
            K.Box("Skull", head, new Vector3(0f, 0.02f, 0.08f), new Vector3(0.32f, 0.22f, 0.28f), _kRooter, default, false);
            K.Box("Snout", head, new Vector3(0f, -0.05f, 0.27f), new Vector3(0.18f, 0.12f, 0.2f), _kRooterSnout, new Vector3(8f, 0f, 0f), false);
            K.Box("Nose Pad", head, new Vector3(0f, -0.06f, 0.38f), new Vector3(0.15f, 0.09f, 0.03f), _kRooterDark, default, false);
            K.Box("Ear L", head, new Vector3(-0.16f, 0.09f, 0.02f), new Vector3(0.08f, 0.05f, 0.09f), _kRooterDark, new Vector3(0f, 0f, 20f), false);
            K.Box("Ear R", head, new Vector3(0.16f, 0.09f, 0.02f), new Vector3(0.08f, 0.05f, 0.09f), _kRooterDark, new Vector3(0f, 0f, -20f), false);
            K.Box("Eye L", head, new Vector3(-0.09f, 0.06f, 0.21f), new Vector3(0.04f, 0.025f, 0.02f), _beastEye, default, false);
            K.Box("Eye R", head, new Vector3(0.09f, 0.06f, 0.21f), new Vector3(0.04f, 0.025f, 0.02f), _beastEye, default, false);

            var legs = new Transform[4];
            Vector3[] legPos = { new Vector3(-0.19f, 0.4f, 0.3f), new Vector3(0.19f, 0.4f, 0.3f), new Vector3(-0.17f, 0.36f, -0.3f), new Vector3(0.17f, 0.36f, -0.3f) };
            string[] legNames = { "Leg FL", "Leg FR", "Leg BL", "Leg BR" };
            for (int i = 0; i < 4; i++)
            {
                legs[i] = K.Group(legNames[i], model, legPos[i], Vector3.zero);
                if (i < 2)
                {
                    K.Prim(PrimitiveType.Capsule, "Upper", legs[i], new Vector3(0f, -0.13f, 0f), new Vector3(0.15f, 0.13f, 0.15f), Vector3.zero, _kRooter, false);
                    K.Prim(PrimitiveType.Capsule, "Lower", legs[i], new Vector3(0f, -0.29f, 0.02f), new Vector3(0.12f, 0.09f, 0.12f), Vector3.zero, _kRooterDark, false);
                    K.Box("Claws", legs[i], new Vector3(0f, -0.37f, 0.08f), new Vector3(0.17f, 0.05f, 0.19f), _kRooterDark, default, false);
                }
                else
                {
                    K.Prim(PrimitiveType.Capsule, "Upper", legs[i], new Vector3(0f, -0.12f, 0f), new Vector3(0.13f, 0.12f, 0.13f), Vector3.zero, _kRooter, false);
                    K.Box("Paw", legs[i], new Vector3(0f, -0.33f, 0.03f), new Vector3(0.12f, 0.05f, 0.14f), _kRooterDark, default, false);
                }
            }
            Transform tail = K.Group("Tail", model, new Vector3(0f, 0.42f, -0.55f), Vector3.zero);
            K.Prim(PrimitiveType.Capsule, "Tail Mesh", tail, new Vector3(0f, -0.02f, -0.08f), new Vector3(0.07f, 0.1f, 0.07f), new Vector3(-60f, 0f, 0f), _kRooterDark, false);

            var agent = AddCommon(root, visual, def, new Vector3(0f, 0.45f, 0.1f), 0.85f, 1.05f, 0.4f, 0.8f, 0.5f);
            var vis = root.AddComponent<EnemyBeastVisual>();
            SetWarningMaterials(vis);
            K.SetRef(vis, "model", model);
            K.SetRef(vis, "head", head);
            K.SetRef(vis, "tail", tail);
            SetBeastLegs(vis, legs, def.runSpeed, 0.9f);
            return SavePrefab(root, agent);
        }

        // ================================================================== Uferklapper / Schlickwerfer (Baustein 29)

        /// <summary>
        /// Kniehoch, flacher Rückenpanzer mit Mittelgrat, vier seitliche Laufbeine, zwei kräftige Scherenarme mit hellen gerippten
        /// Außenseiten (rostige Kanten), kurze Augenstiele. Schlickwerfer: dunkler, Schlickklumpen an Scheren und Panzer, Wurf.
        /// </summary>
        private static GameObject BuildClackerPrefab(EnemyDefinition def, bool slinger)
        {
            var root = new GameObject("Enemy_" + def.name.Replace("Enemy_", ""));
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 2; col.radius = 0.45f; col.height = 1.05f; col.center = new Vector3(0f, 0.45f, 0.05f);

            Transform visual = K.Group("Visual", root.transform);
            Transform model = K.Group("Model", visual);
            model.localScale = Vector3.one * def.scale;
            Material shell = slinger ? _kSlinger : _kClacker;

            K.Prim(PrimitiveType.Sphere, "Shell", model, new Vector3(0f, 0.5f, 0f), new Vector3(0.95f, 0.3f, 0.75f), Vector3.zero, shell, false);
            K.Box("Ridge", model, new Vector3(0f, 0.64f, 0f), new Vector3(0.08f, 0.06f, 0.58f), _kClackerDark, default, false);
            K.Prim(PrimitiveType.Sphere, "Underside", model, new Vector3(0f, 0.42f, 0f), new Vector3(0.8f, 0.18f, 0.62f), Vector3.zero, _kClackerRim, false);
            if (slinger)
            {
                K.Prim(PrimitiveType.Sphere, "Silt A", model, new Vector3(0.22f, 0.62f, -0.12f), new Vector3(0.18f, 0.08f, 0.16f), Vector3.zero, _kMud, false);
                K.Prim(PrimitiveType.Sphere, "Silt B", model, new Vector3(-0.18f, 0.6f, 0.15f), new Vector3(0.14f, 0.07f, 0.12f), Vector3.zero, _kMud, false);
            }

            // Vier Laufbeine seitlich (Drehpunkt am Panzerrand), Füße am Boden
            var legs = new Transform[4];
            Vector3[] legPos = { new Vector3(-0.4f, 0.5f, 0.2f), new Vector3(0.4f, 0.5f, 0.2f), new Vector3(-0.4f, 0.5f, -0.2f), new Vector3(0.4f, 0.5f, -0.2f) };
            string[] legNames = { "Leg FL", "Leg FR", "Leg BL", "Leg BR" };
            for (int i = 0; i < 4; i++)
            {
                float s = legPos[i].x < 0f ? -1f : 1f;
                legs[i] = K.Group(legNames[i], model, legPos[i], Vector3.zero);
                K.Box("Upper", legs[i], new Vector3(s * 0.18f, 0.07f, 0f), new Vector3(0.38f, 0.06f, 0.06f), _kClackerDark, new Vector3(0f, 0f, s * 22f), false);
                K.Box("Lower", legs[i], new Vector3(s * 0.38f, -0.2f, 0f), new Vector3(0.06f, 0.52f, 0.06f), _kClackerDark, new Vector3(0f, 0f, s * -12f), false);
            }

            // Scheren und Augenstiele bewegen sich mit dem „Kopf“ (Ausholen, Zuschlagen)
            Transform head = K.Group("Head", model, new Vector3(0f, 0.5f, 0.4f), Vector3.zero);
            for (int k = -1; k <= 1; k += 2)
            {
                K.Prim(PrimitiveType.Cylinder, k < 0 ? "Eye Stalk L" : "Eye Stalk R", head, new Vector3(k * 0.1f, 0.12f, 0.05f), new Vector3(0.03f, 0.07f, 0.03f), Vector3.zero, shell, false);
                K.Prim(PrimitiveType.Sphere, k < 0 ? "Eye L" : "Eye R", head, new Vector3(k * 0.1f, 0.21f, 0.06f), new Vector3(0.06f, 0.06f, 0.06f), Vector3.zero, _beastEye, false);
                string side = k < 0 ? " L" : " R";
                K.Box("Arm" + side, head, new Vector3(k * 0.3f, 0f, 0.16f), new Vector3(0.1f, 0.1f, 0.32f), _kClackerDark, new Vector3(0f, k * -20f, 0f), false);
                K.Box("Claw" + side, head, new Vector3(k * 0.5f, 0.04f, 0.4f), new Vector3(0.26f, 0.14f, 0.3f), _kClackerRim, default, false);
                K.Box("Claw Edge" + side, head, new Vector3(k * 0.5f, 0.115f, 0.4f), new Vector3(0.27f, 0.02f, 0.31f), _kClackerRust, default, false);
                K.Box("Finger" + side, head, new Vector3(k * 0.44f, -0.06f, 0.5f), new Vector3(0.08f, 0.07f, 0.22f), _kClackerDark, default, false);
                if (slinger)
                    K.Prim(PrimitiveType.Sphere, "Silt Lump" + side, head, new Vector3(k * 0.52f, 0.13f, 0.45f), new Vector3(0.14f, 0.09f, 0.14f), Vector3.zero, _kMud, false);
            }

            var agent = AddCommon(root, visual, def, new Vector3(0f, 0.5f, 0.1f), 1.0f, 1.05f, 0.5f, 0.8f, 0.6f);
            var vis = root.AddComponent<EnemyBeastVisual>();
            SetWarningMaterials(vis);
            K.SetRef(vis, "model", model);
            K.SetRef(vis, "head", head);
            if (slinger)
            {
                K.SetRef(vis, "tracerMaterial", _kMudTrail);
                K.SetBool(vis, "lobbed", true);
            }
            SetBeastLegs(vis, legs, def.runSpeed, 0.7f);
            return SavePrefab(root, agent);
        }

        // ================================================================== Flüsterbalg (Baustein 32)

        /// <summary>
        /// Oberschenkelhohes Kletterwesen: kompakter Rumpf, kleiner Kopf dicht am Körper, zwei dunkle seitliche Augen, vier Greifglieder
        /// (vorn länger) mit kurzen Fingern, großer faltiger Kehlsack bis vor den Bauch, breites Maul. Kein Schwanz. Der Kehlsack bläht
        /// sich beim Schrillen Ausstoß sichtbar auf (EnemyBeastVisual.sac).
        /// </summary>
        private static GameObject BuildBellowsPrefab(EnemyDefinition def)
        {
            var root = new GameObject("Enemy_" + def.name.Replace("Enemy_", ""));
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 1; col.radius = 0.35f; col.height = 0.9f; col.center = new Vector3(0f, 0.45f, 0f);

            Transform visual = K.Group("Visual", root.transform);
            Transform model = K.Group("Model", visual);
            model.localScale = Vector3.one * def.scale;

            K.Prim(PrimitiveType.Sphere, "Torso", model, new Vector3(0f, 0.5f, -0.05f), new Vector3(0.5f, 0.55f, 0.6f), new Vector3(-20f, 0f, 0f), _kBellows, false);
            for (int i = 0; i < 3; i++)
                K.Box("Fold", model, new Vector3(0f, 0.74f - i * 0.05f, -0.02f - i * 0.12f), new Vector3(0.36f - i * 0.04f, 0.02f, 0.03f), _kBellowsDark,
                    new Vector3(-20f, 0f, 0f), false);
            Transform head = K.Group("Head", model, new Vector3(0f, 0.72f, 0.22f), Vector3.zero);
            K.Prim(PrimitiveType.Sphere, "Skull", head, new Vector3(0f, 0f, 0.04f), new Vector3(0.3f, 0.24f, 0.28f), Vector3.zero, _kBellows, false);
            K.Box("Mouth", head, new Vector3(0f, -0.07f, 0.15f), new Vector3(0.26f, 0.035f, 0.09f), _kBellowsDark, default, false);
            K.Prim(PrimitiveType.Sphere, "Eye L", head, new Vector3(-0.13f, 0.03f, 0.1f), new Vector3(0.06f, 0.06f, 0.06f), Vector3.zero, _beastEye, false);
            K.Prim(PrimitiveType.Sphere, "Eye R", head, new Vector3(0.13f, 0.03f, 0.1f), new Vector3(0.06f, 0.06f, 0.06f), Vector3.zero, _beastEye, false);
            Transform sac = K.Group("Throat Sac", model, new Vector3(0f, 0.43f, 0.22f), Vector3.zero);
            K.Prim(PrimitiveType.Sphere, "Sac", sac, Vector3.zero, new Vector3(0.36f, 0.34f, 0.3f), Vector3.zero, _kSac, false);
            for (int i = 0; i < 3; i++)
                K.Box("Sac Fold", sac, new Vector3(0f, 0.09f - i * 0.08f, 0.14f), new Vector3(0.26f, 0.015f, 0.03f), _kBellows, default, false);

            var legs = new Transform[4];
            Vector3[] legPos = { new Vector3(-0.2f, 0.5f, 0.2f), new Vector3(0.2f, 0.5f, 0.2f), new Vector3(-0.2f, 0.42f, -0.25f), new Vector3(0.2f, 0.42f, -0.25f) };
            string[] legNames = { "Leg FL", "Leg FR", "Leg BL", "Leg BR" };
            for (int i = 0; i < 4; i++)
            {
                legs[i] = K.Group(legNames[i], model, legPos[i], Vector3.zero);
                bool front = i < 2;
                float up = front ? -0.15f : -0.12f, low = front ? -0.37f : -0.29f, foot = front ? -0.48f : -0.39f;
                K.Prim(PrimitiveType.Capsule, "Upper", legs[i], new Vector3(0f, up, 0f), new Vector3(0.1f, front ? 0.15f : 0.12f, 0.1f), Vector3.zero, _kBellows, false);
                K.Prim(PrimitiveType.Capsule, "Lower", legs[i], new Vector3(0f, low, 0.02f), new Vector3(0.08f, front ? 0.12f : 0.1f, 0.08f), Vector3.zero, _kBellows, false);
                K.Box("Hand", legs[i], new Vector3(0f, foot, 0.05f), new Vector3(front ? 0.2f : 0.15f, 0.045f, front ? 0.17f : 0.14f), _kBellowsDark, default, false);
                for (int f = -1; f <= 1; f++)
                    K.Box("Finger", legs[i], new Vector3(f * 0.06f, foot - 0.005f, front ? 0.16f : 0.13f), new Vector3(0.035f, 0.035f, 0.07f), _kBellowsDark, default, false);
            }

            var agent = AddCommon(root, visual, def, new Vector3(0f, 0.55f, 0.1f), 0.85f, 1.2f, 0.4f, 0.9f, 0.7f);
            var vis = root.AddComponent<EnemyBeastVisual>();
            SetWarningMaterials(vis);
            K.SetRef(vis, "model", model);
            K.SetRef(vis, "head", head);
            K.SetRef(vis, "sac", sac);
            SetBeastLegs(vis, legs, def.runSpeed, 0.8f);
            return SavePrefab(root, agent);
        }

        private static void SetBeastLegs(EnemyBeastVisual vis, Transform[] legs, float runSpeed, float stride)
        {
            var so = new SerializedObject(vis);
            SerializedProperty lp = so.FindProperty("legs");
            lp.arraySize = legs.Length;
            for (int i = 0; i < legs.Length; i++) lp.GetArrayElementAtIndex(i).objectReferenceValue = legs[i];
            so.FindProperty("runSpeedReference").floatValue = runSpeed;
            so.FindProperty("strideLength").floatValue = stride;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
