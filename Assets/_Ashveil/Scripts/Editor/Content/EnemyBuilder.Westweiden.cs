using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Enemies;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Westweiden spielbar (Lückenliste Punkt 1, 12.10.2026): die Tiere und Gegner der Westweiden als Greybox (wie #28 in Kareth:
    /// Formen und Verhalten zählen, Modelle folgen). Arten, Stufen und Lebensräume aus dem Gebietsblatt v2 §5 (Bestiarium B10–B13b,
    /// B18, B22, B23, B37 – VORSCHLAG), Werte über die Balance-Tabelle (Rolle Normal/Elite), Classic-Verhalten:
    ///   · Bruchotter (8–10, wehrt sich), Hornschnabel (8–11, neutral), Windläuferhirsch (9–12, Beutetier, flieht),
    ///     Heckenwiesel (10–13, wehrt sich), Felsrücken (12–15, neutral), Kammnatter (13–16, territorial, Gift),
    ///     Klippengreif (14–16, neutral), Habbos Schläger und Schützen (11–15, Lager).
    ///   · Auftrags- und Elitegegner: Habbo „der Zöllner“ (15, W24), Heckenkönig (Elite 15, W28), Horstgreif (Elite 16, ohne Auftrag),
    ///     Kellermeister Kord (Elite 15, Kreidekeller – Definition schon da, gesetzt wird er mit der Außenstation).
    ///   · Seltene: Grauschnabel (10), Rotkehl (12, W18), Der Alte vom Riegel (14), Schattenhorst (16).
    /// Keine leuchtenden Augen, keine Magie (Bestiarium-Regel). Beute folgt mit den Aufträgen als Daten (Lückenliste Punkt 2).
    /// Neue Prefabs kommen hinten an die Netz-Prefabs (IDs der alten bleiben).
    /// </summary>
    public static partial class EnemyBuilder
    {
        public const string WwOtter = "enemy.ww_bruchotter", WwRunBird = "enemy.ww_hornschnabel", WwDeer = "enemy.ww_windlaeufer",
            WwWeasel = "enemy.ww_heckenwiesel", WwRam = "enemy.ww_felsruecken", WwSnake = "enemy.ww_kammnatter", WwGriffin = "enemy.ww_klippengreif",
            WwBrawler = "enemy.ww_heckenschlaeger", WwArcher = "enemy.ww_heckenschuetze", WwHabbo = "enemy.ww_habbo",
            WwCellar = "enemy.ww_kellermeister", WwHedgeKing = "enemy.ww_heckenkoenig", WwHorst = "enemy.ww_horstgreif",
            WwRareBird = "enemy.ww_grauschnabel", WwRareWeasel = "enemy.ww_rotkehl", WwRareRam = "enemy.ww_alter_vom_riegel", WwRareGriffin = "enemy.ww_schattenhorst",
            // Kreidekeller (12.10.2026, Gebietsblatt v2 §7): Gruppengegner – Habbos Leute im Keller als Elite (2–3 Spieler)
            WwKellerwache = "enemy.ww_kellerwache", WwKellerschuetze = "enemy.ww_kellerschuetze";

        /// <summary>Gegner-ID → Dateiname (Definition und Prefab teilen den Namen) – auch für <see cref="WestweidenGegner"/>.</summary>
        public static readonly Dictionary<string, string> WestweidenFiles = new Dictionary<string, string>
        {
            { WwOtter, "Enemy_WW_Bruchotter" }, { WwRunBird, "Enemy_WW_Hornschnabel" }, { WwDeer, "Enemy_WW_Windlaeuferhirsch" },
            { WwWeasel, "Enemy_WW_Heckenwiesel" }, { WwRam, "Enemy_WW_Felsruecken" }, { WwSnake, "Enemy_WW_Kammnatter" },
            { WwGriffin, "Enemy_WW_Klippengreif" }, { WwBrawler, "Enemy_WW_Heckenschlaeger" }, { WwArcher, "Enemy_WW_Heckenschuetze" },
            { WwHabbo, "Enemy_WW_Habbo" }, { WwCellar, "Enemy_WW_Kellermeister" }, { WwHedgeKing, "Enemy_WW_Heckenkoenig" },
            { WwHorst, "Enemy_WW_Horstgreif" }, { WwRareBird, "Enemy_WW_Grauschnabel" }, { WwRareWeasel, "Enemy_WW_Rotkehl" },
            { WwRareRam, "Enemy_WW_AlterVomRiegel" }, { WwRareGriffin, "Enemy_WW_Schattenhorst" },
            { WwKellerwache, "Enemy_WW_Kellerwache" }, { WwKellerschuetze, "Enemy_WW_Kellerschuetze" },
        };

        private static Material _wOtter, _wOtterBelly, _wBird, _wBirdDark, _wHorn, _wDeer, _wDeerBelly, _wAntler, _wWeasel, _wWeaselBelly,
            _wRam, _wRamDark, _wRamHorn, _wSnake, _wSnakeBelly, _wComb, _wGriffin, _wGriffinWing, _wBeak, _wHedgeCloth, _wHedgeHood;

        private static void CreateWestweidenMaterials()
        {
            _wOtter = K.Mat("Enemy_WW_OtterFur", new Color(0.30f, 0.22f, 0.15f), 0.35f);       // nasses Fell glänzt etwas
            _wOtterBelly = K.Mat("Enemy_WW_OtterBelly", new Color(0.55f, 0.46f, 0.36f), 0.2f);
            _wBird = K.Mat("Enemy_WW_RunBird", new Color(0.52f, 0.47f, 0.38f), 0.1f);
            _wBirdDark = K.Mat("Enemy_WW_RunBirdDark", new Color(0.25f, 0.22f, 0.18f), 0.1f);
            _wHorn = K.Mat("Enemy_WW_Horn", new Color(0.78f, 0.70f, 0.52f), 0.35f);
            _wDeer = K.Mat("Enemy_WW_DeerFur", new Color(0.55f, 0.38f, 0.22f), 0.08f);
            _wDeerBelly = K.Mat("Enemy_WW_DeerBelly", new Color(0.82f, 0.74f, 0.6f), 0.08f);
            _wAntler = K.Mat("Enemy_WW_Antler", new Color(0.62f, 0.55f, 0.44f), 0.2f);
            _wWeasel = K.Mat("Enemy_WW_WeaselFur", new Color(0.55f, 0.30f, 0.14f), 0.1f);
            _wWeaselBelly = K.Mat("Enemy_WW_WeaselBelly", new Color(0.9f, 0.86f, 0.76f), 0.1f);
            _wRam = K.Mat("Enemy_WW_RamFleece", new Color(0.50f, 0.47f, 0.42f), 0.05f);
            _wRamDark = K.Mat("Enemy_WW_RamDark", new Color(0.22f, 0.20f, 0.18f), 0.05f);
            _wRamHorn = K.Mat("Enemy_WW_RamHorn", new Color(0.66f, 0.6f, 0.5f), 0.3f);
            _wSnake = K.Mat("Enemy_WW_SnakeScale", new Color(0.36f, 0.38f, 0.24f), 0.45f);
            _wSnakeBelly = K.Mat("Enemy_WW_SnakeBelly", new Color(0.66f, 0.62f, 0.45f), 0.35f);
            _wComb = K.Mat("Enemy_WW_SnakeComb", new Color(0.62f, 0.24f, 0.12f), 0.3f);           // Kamm: Warnfarbe, kein Leuchten
            _wGriffin = K.Mat("Enemy_WW_GriffinFeather", new Color(0.42f, 0.36f, 0.3f), 0.1f);
            _wGriffinWing = K.Mat("Enemy_WW_GriffinWing", new Color(0.26f, 0.23f, 0.2f), 0.1f);
            _wBeak = K.Mat("Enemy_WW_Beak", new Color(0.72f, 0.62f, 0.32f), 0.4f);
            _wHedgeCloth = K.Mat("Enemy_WW_HedgeCloth", new Color(0.30f, 0.33f, 0.22f), 0.1f);   // Heckengrün, geflickt
            _wHedgeHood = K.Mat("Enemy_WW_HedgeHood", new Color(0.18f, 0.16f, 0.13f), 0.1f);
        }

        /// <summary>Alle Westweiden-Gegner (Definitionen und Greybox-Prefabs, idempotent), Netz-Anmeldung. Schlüssel = Gegner-ID.</summary>
        public static Dictionary<string, BorderEnemy> BuildWestweidenCreatures()
        {
            ProjectSetup.EnsureFolder(DefFolder);
            ProjectSetup.EnsureFolder(PrefabFolder);
            CreateMaterials();
            CreateKarethMaterials();
            CreateWestweidenMaterials();
            var r = new Dictionary<string, BorderEnemy>();
            BorderEnemy Tier(string id, string name, System.Action<EnemyDefinition> setup, System.Func<EnemyDefinition, GameObject> build)
            {
                EnemyDefinition def = EnsureKarethDefinition(WestweidenFiles[id], id, name, setup);
                var e = new BorderEnemy { Def = def, Prefab = build(def) };
                r[id] = e;
                return e;
            }

            Tier(WwOtter, "Bruchotter", SetupWwOtter, d => BuildLongBeastPrefab(d, _wOtter, _wOtterBelly, 1f));
            Tier(WwWeasel, "Heckenwiesel", SetupWwWeasel, d => BuildLongBeastPrefab(d, _wWeasel, _wWeaselBelly, 0.72f));
            Tier(WwRunBird, "Hornschnabel", SetupWwRunBird, d => BuildRunBirdPrefab(d, _wBird));
            Tier(WwDeer, "Windläuferhirsch", SetupWwDeer, d => BuildHoofPrefab(d, false, _wDeer));
            Tier(WwRam, "Felsrücken", SetupWwRam, d => BuildHoofPrefab(d, true, _wRam));
            Tier(WwSnake, "Kammnatter", SetupWwSnake, d => BuildSnakePrefab(d, _wSnake));
            Tier(WwGriffin, "Klippengreif", SetupWwGriffin, d => BuildGriffinPrefab(d, _wGriffin));
            Tier(WwBrawler, "Habbos Schläger", d => SetupWwBandit(d, false), d => Tint(BuildHumanoidPrefab(d, false), (_ashSkin, _wHedgeCloth), (_ember, _kDeserterEye)));
            Tier(WwArcher, "Habbos Schütze", d => SetupWwBandit(d, true), d => Tint(BuildHumanoidPrefab(d, true), (_ashSkin, _wHedgeCloth), (_ember, _kDeserterEye)));
            Tier(WwHabbo, "Habbo der Zöllner", SetupWwHabbo, d => Tint(BuildHumanoidPrefab(d, false), (_ashSkin, _wHedgeHood), (_ember, _kDeserterEye)));
            Tier(WwCellar, "Kellermeister Kord", SetupWwCellar, d => Tint(BuildHumanoidPrefab(d, false), (_ashSkin, _wHedgeHood), (_ember, _kDeserterEye)));
            Tier(WwHedgeKing, "Heckenkönig", SetupWwHedgeKing, d => BuildSnakePrefab(d, K.Mat("Enemy_WW_HedgeKingScale", new Color(0.24f, 0.30f, 0.16f), 0.5f)));
            Tier(WwHorst, "Horstgreif", SetupWwHorst, d => BuildGriffinPrefab(d, K.Mat("Enemy_WW_HorstFeather", new Color(0.32f, 0.27f, 0.22f), 0.1f)));
            // Seltene (Classic-Paket wie in Kareth: Leben ×1,8, Schaden ×1,3, Gestalt ×1,3, Wiederkehr 20–45 min)
            Tier(WwRareBird, "Grauschnabel", d => { SetupWwRunBird(d); WwRare(d, 10); }, d => BuildRunBirdPrefab(d, K.Mat("Enemy_WW_GreyBird", new Color(0.55f, 0.55f, 0.53f), 0.1f)));
            Tier(WwRareWeasel, "Rotkehl", d => { SetupWwWeasel(d); WwRare(d, 12); }, d => BuildLongBeastPrefab(d, K.Mat("Enemy_WW_RedWeasel", new Color(0.6f, 0.2f, 0.1f), 0.1f), _wWeaselBelly, 0.72f));
            Tier(WwRareRam, "Der Alte vom Riegel", d => { SetupWwRam(d); WwRare(d, 14); d.aggroProfile = AggroProfile.Territorial; d.neutral = false; },
                d => BuildHoofPrefab(d, true, K.Mat("Enemy_WW_OldRam", new Color(0.72f, 0.70f, 0.64f), 0.05f)));
            Tier(WwRareGriffin, "Schattenhorst", d => { SetupWwGriffin(d); WwRare(d, 16); }, d => BuildGriffinPrefab(d, K.Mat("Enemy_WW_DarkGriffin", new Color(0.14f, 0.13f, 0.12f), 0.15f)));

            // Kreidekeller: Habbos Leute als Gruppengegner (neue Prefabs hinten an die Netz-Prefabs)
            Tier(WwKellerwache, "Kellerwache", d => SetupWwKeller(d, false), d => Tint(BuildHumanoidPrefab(d, false), (_ashSkin, _wHedgeHood), (_ember, _kDeserterEye)));
            Tier(WwKellerschuetze, "Kellerschütze", d => SetupWwKeller(d, true), d => Tint(BuildHumanoidPrefab(d, true), (_ashSkin, _wHedgeHood), (_ember, _kDeserterEye)));

            var prefabs = new List<GameObject>();
            foreach (BorderEnemy e in r.Values) if (e?.Prefab != null) prefabs.Add(e.Prefab);
            NetworkSetupBuilder.AddSpawnables(prefabs);
            AssetDatabase.SaveAssets();
            return r;
        }

        // ================================================================== Definitionen

        private static void WwBase(EnemyDefinition d, int min, int max, BalanceRole role, float hp, float dmg)
        {
            d.body = EnemyBody.Beast; d.style = EnemyStyle.Melee;
            d.minLevel = min; d.maxLevel = max;
            d.balanceRole = role; d.healthFactor = hp; d.damageFactor = dmg;
            d.elite = role == BalanceRole.Elite;
            d.throwRange = 0f; d.sidestepChance = 0f; d.howlsOnEngage = false; d.fleeHealthFraction = 0f; d.fleesWhenAttacked = false;
            d.poisonPerSecond = 0f; d.areaAttackEvery = 0;
            d.corpseTime = 25f; d.respawnTime = 150f; d.respawnTimeMax = 0f;   // Weltfülle; Classic-Streuung gibt den Faktor im Gebiet
        }

        private static EnemyAttackDefinition Move(int id, string name, EnemyMoveKind kind, EnemyMovePose pose, float maxRange, float windup,
            float dmg, float cd, float weight, float arc = 110f, float minRange = 0f, float area = 0f) =>
            new EnemyAttackDefinition
            {
                id = (byte)id, displayName = name, kind = kind, pose = pose, minRange = minRange, maxRange = maxRange, windup = windup,
                damageMultiplier = dmg, cooldown = cd, weight = weight, arc = arc, knockback = 0f, areaRadius = area
            };

        private static void SetupWwOtter(EnemyDefinition d)
        {
            WwBase(d, 8, 10, BalanceRole.Normal, 0.9f, 0.9f);
            d.attackRange = 1.9f; d.attackInterval = 1.5f; d.windup = 0.45f; d.attackArc = 110f;
            d.walkSpeed = 1.3f; d.runSpeed = 6f; d.wanderRadius = 5f;
            d.aggroProfile = AggroProfile.Defensive; d.neutral = true; d.aggroRadius = 10f; d.territoryRadius = 5f; d.leashDistance = 32f;
            d.bodyColor = new Color(0.30f, 0.22f, 0.15f); d.glowColor = new Color(0.55f, 0.46f, 0.36f); d.scale = 1f;
            d.moveset = new List<EnemyAttackDefinition>
            {
                Move(1, "Biss", EnemyMoveKind.Strike, EnemyMovePose.Default, 1.9f, 0.45f, 1f, 0f, 4f),
                Move(2, "Vorstoß", EnemyMoveKind.Charge, EnemyMovePose.Charge, 7f, 0.6f, 1.25f, 8f, 2f, 360f, 3f),   // Gebietsblatt: Vorstoß
            };
        }

        private static void SetupWwWeasel(EnemyDefinition d)
        {
            WwBase(d, 10, 13, BalanceRole.Normal, 0.8f, 0.95f);
            d.attackRange = 1.7f; d.attackInterval = 1.3f; d.windup = 0.35f; d.attackArc = 110f;
            d.walkSpeed = 1.6f; d.runSpeed = 6.6f; d.wanderRadius = 6f;
            d.aggroProfile = AggroProfile.Defensive; d.neutral = true; d.aggroRadius = 10f; d.territoryRadius = 5f; d.leashDistance = 30f;
            d.fleeHealthFraction = 0.12f;                                           // zieht sich bei wenig Leben zurück (Gebietsblatt)
            d.sidestepChance = 0.3f;
            d.bodyColor = new Color(0.55f, 0.30f, 0.14f); d.glowColor = new Color(0.9f, 0.86f, 0.76f); d.scale = 1f;
            d.moveset = new List<EnemyAttackDefinition>
            {
                Move(1, "Kehlbiss", EnemyMoveKind.Strike, EnemyMovePose.Default, 1.7f, 0.35f, 1f, 0f, 4f),
                Move(2, "Sprung", EnemyMoveKind.Charge, EnemyMovePose.Charge, 6f, 0.5f, 1.2f, 7f, 2f, 360f, 2.5f),
            };
        }

        private static void SetupWwRunBird(EnemyDefinition d)
        {
            WwBase(d, 8, 11, BalanceRole.Normal, 0.9f, 1f);
            d.attackRange = 2.2f; d.attackInterval = 1.6f; d.windup = 0.5f; d.attackArc = 100f;
            d.walkSpeed = 1.5f; d.runSpeed = 7f; d.wanderRadius = 8f;
            d.aggroProfile = AggroProfile.Passive; d.neutral = true; d.aggroRadius = 10f; d.territoryRadius = 5f; d.leashDistance = 35f;
            d.bodyColor = new Color(0.52f, 0.47f, 0.38f); d.glowColor = new Color(0.78f, 0.70f, 0.52f); d.scale = 1f;
            d.moveset = new List<EnemyAttackDefinition>
            {
                Move(1, "Hacken", EnemyMoveKind.Strike, EnemyMovePose.Default, 2.2f, 0.5f, 1f, 0f, 4f),
                Move(2, "Anlauf", EnemyMoveKind.Charge, EnemyMovePose.Charge, 10f, 0.9f, 1.4f, 9f, 2f, 360f, 4f),   // scharrt vorher (Telegraph)
            };
        }

        private static void SetupWwDeer(EnemyDefinition d)
        {
            WwBase(d, 9, 12, BalanceRole.Normal, 0.75f, 0.8f);
            d.attackRange = 2.2f; d.attackInterval = 1.8f; d.windup = 0.6f; d.attackArc = 100f;
            d.walkSpeed = 1.4f; d.runSpeed = 8f; d.wanderRadius = 9f;
            d.aggroProfile = AggroProfile.Passive; d.neutral = true; d.aggroRadius = 12f; d.territoryRadius = 5f; d.leashDistance = 30f;
            d.fleesWhenAttacked = true;                                             // Beutetier: weicht aus (Max 03.10.)
            d.bodyColor = new Color(0.55f, 0.38f, 0.22f); d.glowColor = new Color(0.82f, 0.74f, 0.6f); d.scale = 1f;
            d.moveset = new List<EnemyAttackDefinition> { Move(1, "Geweihstoß", EnemyMoveKind.Strike, EnemyMovePose.Default, 2.2f, 0.6f, 1f, 0f, 4f) };
        }

        private static void SetupWwRam(EnemyDefinition d)
        {
            WwBase(d, 12, 15, BalanceRole.Normal, 1.15f, 1f);
            d.attackRange = 2.4f; d.attackInterval = 1.9f; d.windup = 0.6f; d.attackArc = 110f;
            d.walkSpeed = 1.1f; d.runSpeed = 5.6f; d.wanderRadius = 6f;
            d.aggroProfile = AggroProfile.Passive; d.neutral = true; d.aggroRadius = 12f; d.territoryRadius = 6f; d.leashDistance = 34f;
            d.bodyColor = new Color(0.50f, 0.47f, 0.42f); d.glowColor = new Color(0.66f, 0.6f, 0.5f); d.scale = 1f;
            d.moveset = new List<EnemyAttackDefinition>
            {
                Move(1, "Hornstoß", EnemyMoveKind.Strike, EnemyMovePose.Default, 2.4f, 0.6f, 1f, 0f, 4f),
                Move(2, "Rammen", EnemyMoveKind.Charge, EnemyMovePose.Charge, 12f, 1.1f, 1.5f, 10f, 2f, 360f, 4f),   // senkt den Kopf (Telegraph)
            };
        }

        private static void SetupWwSnake(EnemyDefinition d)
        {
            WwBase(d, 13, 16, BalanceRole.Normal, 0.85f, 0.85f);
            d.attackRange = 2.4f; d.attackInterval = 1.6f; d.windup = 0.55f; d.attackArc = 90f;
            d.walkSpeed = 0.9f; d.runSpeed = 4.6f; d.wanderRadius = 3f;
            d.aggroProfile = AggroProfile.Territorial; d.neutral = false; d.aggroRadius = 11f; d.territoryRadius = 7f; d.leashDistance = 28f;
            d.poisonPerSecond = 2.5f; d.poisonDuration = 6f;                         // Biss mit Gift (W26: geschwollene Beine)
            d.bodyColor = new Color(0.36f, 0.38f, 0.24f); d.glowColor = new Color(0.62f, 0.24f, 0.12f); d.scale = 1f;
            d.moveset = new List<EnemyAttackDefinition>
            {
                Move(1, "Biss", EnemyMoveKind.Strike, EnemyMovePose.Default, 2.4f, 0.55f, 1f, 0f, 4f, 90f),
                Move(2, "Vorschnellen", EnemyMoveKind.Charge, EnemyMovePose.Charge, 5f, 0.8f, 1.35f, 8f, 2f, 360f, 2.6f),   // stellt den Kamm auf
            };
        }

        private static void SetupWwGriffin(EnemyDefinition d)
        {
            WwBase(d, 14, 16, BalanceRole.Normal, 1.1f, 1.05f);
            d.attackRange = 2.6f; d.attackInterval = 1.8f; d.windup = 0.55f; d.attackArc = 120f;
            d.walkSpeed = 1.3f; d.runSpeed = 7f; d.wanderRadius = 7f;
            d.aggroProfile = AggroProfile.Passive; d.neutral = true; d.aggroRadius = 12f; d.territoryRadius = 8f; d.leashDistance = 36f;
            d.bodyColor = new Color(0.42f, 0.36f, 0.3f); d.glowColor = new Color(0.72f, 0.62f, 0.32f); d.scale = 1f;
            d.moveset = new List<EnemyAttackDefinition>
            {
                Move(1, "Krallenhieb", EnemyMoveKind.Strike, EnemyMovePose.LeftArm, 2.6f, 0.55f, 1f, 0f, 4f, 120f),
                Move(2, "Schnabelhieb", EnemyMoveKind.Strike, EnemyMovePose.Default, 2.4f, 0.8f, 1.35f, 6f, 2f, 90f),
                Move(3, "Flügelschlag", EnemyMoveKind.Area, EnemyMovePose.Default, 3f, 1.2f, 1.3f, 11f, 1.5f, 360f, 0f, 4f),
            };
        }

        private static void SetupWwBandit(EnemyDefinition d, bool archer)
        {
            SetupHusk(d);
            d.minLevel = 11; d.maxLevel = 15;
            d.balanceRole = BalanceRole.Normal; d.healthFactor = archer ? 0.8f : 1f; d.damageFactor = 1f; d.elite = false;
            d.style = archer ? EnemyStyle.Ranged : EnemyStyle.Melee;
            if (archer)
            {
                // 21.10. (Nachbesserung KAMPF-02): Fernkampfwerte wie die Schützin (SetupGunner). Bisher blieben die Nahkampfwerte
                // aus SetupHusk stehen (Reichweite 2,3 m) – die Schützen standen zwischen 5 und 13 m und schossen nie, eingekeilt
                // nur auf höchstens 2,3 m. Abstand 11 m, Zurückweichen unter 5 m und Treffsicherheit 0,8 bleiben (Vorgaben der Klasse).
                d.attackRange = 20f; d.attackInterval = 3.2f; d.windup = 0.9f; d.attackArc = 60f;
            }
            d.walkSpeed = 1.4f; d.runSpeed = 5.6f; d.wanderRadius = 3f;
            d.aggroProfile = AggroProfile.Aggressive; d.neutral = false; d.aggroRadius = 13f; d.territoryRadius = 8f; d.leashDistance = 34f;
            d.fleeHealthFraction = archer ? 0f : 0.1f;                              // Classic: Menschen fliehen bei wenig Leben
            d.throwRange = 0f; d.areaAttackEvery = 0;
            d.corpseTime = 30f; d.respawnTime = 150f; d.respawnTimeMax = 0f;
            d.bodyColor = new Color(0.30f, 0.33f, 0.22f); d.glowColor = new Color(0.5f, 0.42f, 0.32f);
        }

        /// <summary>Kreidekeller: Elite 13–14, einzeln ziehbar (enger Aggro-Radius), Wiederkehr 6–8 min (Gebietsblatt v2 §7).</summary>
        private static void SetupWwKeller(EnemyDefinition d, bool archer)
        {
            SetupWwBandit(d, archer);
            d.minLevel = 13; d.maxLevel = 14;
            d.balanceRole = BalanceRole.Elite; d.elite = true; d.healthFactor = archer ? 0.75f : 0.85f; d.damageFactor = 0.9f;
            d.aggroRadius = 10f; d.territoryRadius = 6f; d.leashDistance = 40f;
            d.fleeHealthFraction = 0f;                                               // im Keller flieht keiner – wohin auch
            d.respawnTime = 360f; d.respawnTimeMax = 480f;
            d.bodyColor = new Color(0.26f, 0.27f, 0.22f);
        }

        private static void SetupWwHabbo(EnemyDefinition d)
        {
            SetupWwBandit(d, false);
            d.minLevel = 15; d.maxLevel = 15;
            d.healthFactor = 1.8f; d.damageFactor = 1.3f;                            // „Kopf von …“ für Solospieler (Canon 12.10.), wie die Seltenen
            d.fleeHealthFraction = 0f; d.aggroProfile = AggroProfile.Territorial; d.territoryRadius = 10f;
            d.areaAttackEvery = 5; d.areaRadius = 4f; d.areaWindup = 1.3f; d.areaDamageMultiplier = 1.5f;   // Zollstock-Schwinger, sichtbar angekündigt
            d.respawnTime = 360f; d.respawnTimeMax = 600f;                           // Auftragsziel: 6–10 min
            d.scale = 1.12f;
        }

        private static void SetupWwCellar(EnemyDefinition d)
        {
            SetupWwBandit(d, false);
            d.minLevel = 15; d.maxLevel = 15;
            d.balanceRole = BalanceRole.Elite; d.elite = true; d.healthFactor = 0.8f; d.damageFactor = 1f;
            d.fleeHealthFraction = 0f; d.aggroProfile = AggroProfile.Territorial; d.territoryRadius = 9f;
            // Gebietsblatt §7: Mehlstaub – angekündigte Wolke, aus der man heraustreten muss (Greybox: Flächenangriff mit Telegraph)
            d.areaAttackEvery = 4; d.areaRadius = 5f; d.areaWindup = 1.5f; d.areaDamageMultiplier = 1.6f;
            d.respawnTime = 480f; d.respawnTimeMax = 720f;
            d.scale = 1.15f;
        }

        private static void SetupWwHedgeKing(EnemyDefinition d)
        {
            SetupWwSnake(d);
            d.minLevel = 15; d.maxLevel = 15;
            d.balanceRole = BalanceRole.Elite; d.elite = true; d.healthFactor = 0.7f; d.damageFactor = 0.9f;   // 2 Spieler (Gebietsblatt §5.2)
            d.aggroProfile = AggroProfile.Territorial; d.territoryRadius = 12f; d.aggroRadius = 14f; d.leashDistance = 40f;
            d.respawnTime = 360f; d.respawnTimeMax = 600f;                           // Auftragselite 6–10 min (§5.2)
            d.scale = 2.4f;
        }

        private static void SetupWwHorst(EnemyDefinition d)
        {
            SetupWwGriffin(d);
            d.minLevel = 16; d.maxLevel = 16;
            d.balanceRole = BalanceRole.Elite; d.elite = true; d.healthFactor = 0.75f; d.damageFactor = 0.95f;  // 2–3 Spieler, zu zweit
            d.aggroProfile = AggroProfile.Territorial; d.neutral = false; d.territoryRadius = 14f; d.leashDistance = 42f;
            d.respawnTime = 600f; d.respawnTimeMax = 900f;                           // 10–15 min (§5.2)
            d.scale = 1.6f;
        }

        private static void WwRare(EnemyDefinition d, int level)
        {
            d.minLevel = level; d.maxLevel = level;
            d.healthFactor *= RareHealth; d.damageFactor *= RareDamage; d.scale *= RareScale;
            d.respawnTime = RareRespawnMin; d.respawnTimeMax = RareRespawnMax; d.elite = false;
        }

        // ================================================================== Greybox-Gestalten

        private static GameObject WwFinish(GameObject root, Transform visual, Transform model, Transform head, Transform tail, Transform[] legs,
            EnemyDefinition def, float height, float stride)
        {
            float s = def.scale;
            var agent = AddCommon(root, visual, def, new Vector3(0f, height * 0.6f * s, 0f), 0.6f + 0.4f * s, (height + 0.4f) * s,
                Mathf.Clamp(0.3f * s, 0.25f, 1.2f), Mathf.Max(0.5f, height * s), height * 0.8f * s);
            var vis = root.AddComponent<EnemyBeastVisual>();
            SetWarningMaterials(vis);
            K.SetRef(vis, "model", model);
            if (head != null) K.SetRef(vis, "head", head);
            if (tail != null) K.SetRef(vis, "tail", tail);
            SetBeastLegs(vis, legs, def.runSpeed, stride);
            return SavePrefab(root, agent);
        }

        private static (GameObject root, Transform visual, Transform model) WwRoot(EnemyDefinition def, float radius, float height, int dir)
        {
            var root = new GameObject("Enemy_" + def.name.Replace("Enemy_", ""));
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = dir; col.radius = radius * def.scale; col.height = height * def.scale; col.center = new Vector3(0f, (dir == 1 ? height * 0.5f : radius) * def.scale, 0f);
            Transform visual = K.Group("Visual", root.transform);
            Transform model = K.Group("Model", visual);
            model.localScale = Vector3.one * def.scale;
            return (root, visual, model);
        }

        /// <summary>Langer, niedriger Vierbeiner (Bruchotter, Heckenwiesel): gestreckter Leib, kurze Beine, langer Schwanz, kleiner Kopf.</summary>
        private static GameObject BuildLongBeastPrefab(EnemyDefinition def, Material fur, Material belly, float size)
        {
            (GameObject root, Transform visual, Transform model) = WwRoot(def, 0.25f * size, 1.3f * size, 2);
            Transform m = K.Group("Body", model); m.localScale = Vector3.one * size;
            K.Prim(PrimitiveType.Capsule, "Leib", m, new Vector3(0f, 0.34f, 0f), new Vector3(0.34f, 0.55f, 0.32f), new Vector3(90f, 0f, 0f), fur, false);
            K.Prim(PrimitiveType.Capsule, "Bauch", m, new Vector3(0f, 0.27f, 0.05f), new Vector3(0.26f, 0.42f, 0.2f), new Vector3(90f, 0f, 0f), belly, false);
            K.Prim(PrimitiveType.Capsule, "Hals", m, new Vector3(0f, 0.42f, 0.5f), new Vector3(0.2f, 0.16f, 0.2f), new Vector3(60f, 0f, 0f), fur, false);
            Transform head = K.Group("Head", m, new Vector3(0f, 0.48f, 0.66f), Vector3.zero);
            K.Prim(PrimitiveType.Sphere, "Schädel", head, new Vector3(0f, 0f, 0.04f), new Vector3(0.2f, 0.16f, 0.24f), Vector3.zero, fur, false);
            K.Box("Schnauze", head, new Vector3(0f, -0.02f, 0.17f), new Vector3(0.1f, 0.08f, 0.1f), belly, default, false);
            K.Box("Ohr L", head, new Vector3(-0.08f, 0.08f, -0.02f), new Vector3(0.05f, 0.04f, 0.03f), fur, default, false);
            K.Box("Ohr R", head, new Vector3(0.08f, 0.08f, -0.02f), new Vector3(0.05f, 0.04f, 0.03f), fur, default, false);
            K.Box("Auge L", head, new Vector3(-0.06f, 0.03f, 0.13f), new Vector3(0.03f, 0.025f, 0.02f), _beastEye, default, false);
            K.Box("Auge R", head, new Vector3(0.06f, 0.03f, 0.13f), new Vector3(0.03f, 0.025f, 0.02f), _beastEye, default, false);
            var legs = new Transform[4];
            Vector3[] lp = { new Vector3(-0.13f, 0.22f, 0.3f), new Vector3(0.13f, 0.22f, 0.3f), new Vector3(-0.13f, 0.22f, -0.3f), new Vector3(0.13f, 0.22f, -0.3f) };
            for (int i = 0; i < 4; i++)
            {
                legs[i] = K.Group("Leg " + i, m, lp[i], Vector3.zero);
                K.Prim(PrimitiveType.Capsule, "Bein", legs[i], new Vector3(0f, -0.1f, 0f), new Vector3(0.09f, 0.11f, 0.09f), Vector3.zero, fur, false);
            }
            Transform tail = K.Group("Tail", m, new Vector3(0f, 0.3f, -0.58f), Vector3.zero);
            K.Prim(PrimitiveType.Capsule, "Schwanz", tail, new Vector3(0f, -0.04f, -0.25f), new Vector3(0.1f, 0.28f, 0.1f), new Vector3(-75f, 0f, 0f), fur, false);
            return WwFinish(root, visual, model, head, tail, legs, def, 0.5f * size, 0.6f * size);
        }

        /// <summary>Hornschnabel: großer Laufvogel – zwei kräftige Beine, ovaler Rumpf, langer Hals, Kopf mit hornigem Hackschnabel.</summary>
        private static GameObject BuildRunBirdPrefab(EnemyDefinition def, Material feather)
        {
            (GameObject root, Transform visual, Transform model) = WwRoot(def, 0.35f, 1.7f, 1);
            K.Prim(PrimitiveType.Sphere, "Rumpf", model, new Vector3(0f, 1.0f, -0.05f), new Vector3(0.62f, 0.55f, 0.85f), Vector3.zero, feather, false);
            K.Prim(PrimitiveType.Capsule, "Hals", model, new Vector3(0f, 1.38f, 0.32f), new Vector3(0.16f, 0.32f, 0.16f), new Vector3(25f, 0f, 0f), feather, false);
            Transform head = K.Group("Head", model, new Vector3(0f, 1.68f, 0.48f), Vector3.zero);
            K.Prim(PrimitiveType.Sphere, "Kopf", head, Vector3.zero, new Vector3(0.2f, 0.2f, 0.24f), Vector3.zero, feather, false);
            K.Box("Hornschnabel", head, new Vector3(0f, -0.03f, 0.2f), new Vector3(0.08f, 0.1f, 0.26f), _wHorn, new Vector3(12f, 0f, 0f), false);
            K.Box("Hornplatte", head, new Vector3(0f, 0.09f, 0.05f), new Vector3(0.1f, 0.05f, 0.18f), _wHorn, default, false);
            K.Box("Auge L", head, new Vector3(-0.08f, 0.03f, 0.04f), new Vector3(0.02f, 0.03f, 0.03f), _beastEye, default, false);
            K.Box("Auge R", head, new Vector3(0.08f, 0.03f, 0.04f), new Vector3(0.02f, 0.03f, 0.03f), _beastEye, default, false);
            Transform tail = K.Group("Tail", model, new Vector3(0f, 1.05f, -0.45f), Vector3.zero);
            K.Box("Schwanzfedern", tail, new Vector3(0f, 0.05f, -0.15f), new Vector3(0.32f, 0.08f, 0.35f), _wBirdDark, new Vector3(-20f, 0f, 0f), false);
            var legs = new Transform[4];
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.15f : 0.15f;
                legs[i] = K.Group(i == 0 ? "Leg L" : "Leg R", model, new Vector3(x, 0.8f, 0f), Vector3.zero);
                K.Prim(PrimitiveType.Capsule, "Schenkel", legs[i], new Vector3(0f, -0.2f, 0f), new Vector3(0.14f, 0.2f, 0.14f), Vector3.zero, feather, false);
                K.Prim(PrimitiveType.Capsule, "Lauf", legs[i], new Vector3(0f, -0.58f, 0.02f), new Vector3(0.06f, 0.22f, 0.06f), Vector3.zero, _wHorn, false);
                K.Box("Zehen", legs[i], new Vector3(0f, -0.79f, 0.08f), new Vector3(0.14f, 0.03f, 0.2f), _wHorn, default, false);
            }
            return WwFinish(root, visual, model, head, tail, legs, def, 1.7f, 1.4f);
        }

        /// <summary>Huftier: Windläuferhirsch (schlank, hohe Läufe, Geweih) oder Felsrücken (schwer, Buckel, gedrehte Hörner, Vlies).</summary>
        private static GameObject BuildHoofPrefab(EnemyDefinition def, bool ram, Material fur)
        {
            float h = ram ? 1.25f : 1.45f;
            (GameObject root, Transform visual, Transform model) = WwRoot(def, ram ? 0.5f : 0.38f, ram ? 1.9f : 1.7f, 2);
            float bodyY = ram ? 0.85f : 1.0f;
            K.Prim(PrimitiveType.Capsule, "Rumpf", model, new Vector3(0f, bodyY, 0f), ram ? new Vector3(0.8f, 0.7f, 0.75f) : new Vector3(0.5f, 0.62f, 0.5f), new Vector3(90f, 0f, 0f), fur, false);
            if (ram) K.Prim(PrimitiveType.Sphere, "Buckel", model, new Vector3(0f, bodyY + 0.3f, 0.2f), new Vector3(0.7f, 0.45f, 0.7f), Vector3.zero, fur, false);
            else K.Prim(PrimitiveType.Capsule, "Bauch", model, new Vector3(0f, bodyY - 0.12f, 0f), new Vector3(0.4f, 0.5f, 0.38f), new Vector3(90f, 0f, 0f), _wDeerBelly, false);
            K.Prim(PrimitiveType.Capsule, "Hals", model, new Vector3(0f, bodyY + (ram ? 0.15f : 0.35f), 0.6f), ram ? new Vector3(0.4f, 0.25f, 0.4f) : new Vector3(0.2f, 0.35f, 0.2f),
                new Vector3(ram ? 70f : 35f, 0f, 0f), fur, false);
            Transform head = K.Group("Head", model, new Vector3(0f, bodyY + (ram ? 0.2f : 0.72f), ram ? 0.95f : 0.82f), Vector3.zero);
            K.Box("Schädel", head, new Vector3(0f, 0f, 0.08f), ram ? new Vector3(0.32f, 0.3f, 0.4f) : new Vector3(0.18f, 0.2f, 0.36f), ram ? _wRamDark : fur, default, false);
            K.Box("Auge L", head, new Vector3(ram ? -0.16f : -0.09f, 0.05f, 0.12f), new Vector3(0.02f, 0.03f, 0.03f), _beastEye, default, false);
            K.Box("Auge R", head, new Vector3(ram ? 0.16f : 0.09f, 0.05f, 0.12f), new Vector3(0.02f, 0.03f, 0.03f), _beastEye, default, false);
            if (ram)
                foreach (float s in new[] { -1f, 1f })
                {
                    K.Prim(PrimitiveType.Capsule, "Horn", head, new Vector3(s * 0.2f, 0.12f, -0.05f), new Vector3(0.12f, 0.22f, 0.12f), new Vector3(-30f, 0f, s * 70f), _wRamHorn, false);
                    K.Prim(PrimitiveType.Capsule, "Hornspitze", head, new Vector3(s * 0.34f, -0.06f, 0.05f), new Vector3(0.09f, 0.14f, 0.09f), new Vector3(40f, 0f, s * 20f), _wRamHorn, false);
                }
            else
                foreach (float s in new[] { -1f, 1f })
                {
                    K.Prim(PrimitiveType.Cylinder, "Stange", head, new Vector3(s * 0.12f, 0.32f, -0.02f), new Vector3(0.04f, 0.24f, 0.04f), new Vector3(-15f, 0f, s * 20f), _wAntler, false);
                    K.Prim(PrimitiveType.Cylinder, "Sprosse", head, new Vector3(s * 0.2f, 0.42f, 0.08f), new Vector3(0.03f, 0.12f, 0.03f), new Vector3(30f, 0f, s * 45f), _wAntler, false);
                    K.Prim(PrimitiveType.Cylinder, "Sprosse", head, new Vector3(s * 0.18f, 0.55f, -0.08f), new Vector3(0.03f, 0.1f, 0.03f), new Vector3(-35f, 0f, s * 30f), _wAntler, false);
                }
            var legs = new Transform[4];
            float lx = ram ? 0.25f : 0.16f, lz = ram ? 0.4f : 0.42f, legLen = bodyY - 0.1f;
            Vector3[] lp = { new Vector3(-lx, bodyY - 0.1f, lz), new Vector3(lx, bodyY - 0.1f, lz), new Vector3(-lx, bodyY - 0.1f, -lz), new Vector3(lx, bodyY - 0.1f, -lz) };
            for (int i = 0; i < 4; i++)
            {
                legs[i] = K.Group("Leg " + i, model, lp[i], Vector3.zero);
                K.Prim(PrimitiveType.Capsule, "Lauf", legs[i], new Vector3(0f, -legLen * 0.5f, 0f), new Vector3(ram ? 0.16f : 0.09f, legLen * 0.5f, ram ? 0.16f : 0.09f), Vector3.zero, fur, false);
                K.Box("Huf", legs[i], new Vector3(0f, -legLen + 0.03f, 0.02f), new Vector3(ram ? 0.16f : 0.08f, 0.06f, ram ? 0.18f : 0.1f), _wRamDark, default, false);
            }
            Transform tail = K.Group("Tail", model, new Vector3(0f, bodyY + 0.1f, ram ? -0.75f : -0.62f), Vector3.zero);
            K.Box("Wedel", tail, new Vector3(0f, -0.06f, -0.04f), new Vector3(0.1f, 0.16f, 0.06f), ram ? fur : _wDeerBelly, default, false);
            return WwFinish(root, visual, model, head, tail, legs, def, h, ram ? 1.3f : 1.8f);
        }

        /// <summary>Kammnatter: lange Schlange in flacher S-Linie, Kopf leicht erhoben, roter Kamm im Nacken (Warnfarbe, Drohhaltung).</summary>
        private static GameObject BuildSnakePrefab(EnemyDefinition def, Material scale)
        {
            (GameObject root, Transform visual, Transform model) = WwRoot(def, 0.22f, 2.2f, 2);
            const int n = 9;
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                float z = 0.55f - t * 1.9f, x = Mathf.Sin(t * Mathf.PI * 2f) * 0.22f, r = Mathf.Lerp(0.16f, 0.07f, Mathf.Abs(t - 0.25f) * 1.3f);
                K.Prim(PrimitiveType.Sphere, "Glied " + i, model, new Vector3(x, r * 0.8f, z), new Vector3(r * 2f, r * 1.5f, 0.32f), Vector3.zero, i % 3 == 1 ? _wSnakeBelly : scale, false);
            }
            Transform head = K.Group("Head", model, new Vector3(0f, 0.32f, 0.75f), Vector3.zero);
            K.Prim(PrimitiveType.Capsule, "Nacken", head, new Vector3(0f, -0.12f, -0.12f), new Vector3(0.15f, 0.2f, 0.15f), new Vector3(50f, 0f, 0f), scale, false);
            K.Box("Kopf", head, new Vector3(0f, 0f, 0.06f), new Vector3(0.16f, 0.09f, 0.22f), scale, default, false);
            for (int i = 0; i < 4; i++)
                K.Box("Kamm", head, new Vector3(0f, 0.08f + 0.02f * (i % 2), -0.06f - i * 0.07f), new Vector3(0.02f, 0.12f - i * 0.02f, 0.06f), _wComb, new Vector3(-20f, 0f, 0f), false);
            K.Box("Auge L", head, new Vector3(-0.06f, 0.03f, 0.1f), new Vector3(0.02f, 0.02f, 0.03f), _beastEye, default, false);
            K.Box("Auge R", head, new Vector3(0.06f, 0.03f, 0.1f), new Vector3(0.02f, 0.02f, 0.03f), _beastEye, default, false);
            return WwFinish(root, visual, model, head, null, new Transform[4], def, 0.4f, 1f);
        }

        /// <summary>Klippengreif: großer Felsvogel – kräftiger Rumpf, angelegte Schwingen, Hakenschnabel, zwei Fänge.</summary>
        private static GameObject BuildGriffinPrefab(EnemyDefinition def, Material feather)
        {
            (GameObject root, Transform visual, Transform model) = WwRoot(def, 0.45f, 1.6f, 1);
            K.Prim(PrimitiveType.Sphere, "Rumpf", model, new Vector3(0f, 0.95f, 0f), new Vector3(0.8f, 0.85f, 1.05f), new Vector3(-20f, 0f, 0f), feather, false);
            foreach (float s in new[] { -1f, 1f })
                K.Box("Schwinge", model, new Vector3(s * 0.42f, 1.05f, -0.15f), new Vector3(0.12f, 0.6f, 1.25f), _wGriffinWing, new Vector3(-15f, 0f, s * 12f), false);
            Transform head = K.Group("Head", model, new Vector3(0f, 1.5f, 0.42f), Vector3.zero);
            K.Prim(PrimitiveType.Sphere, "Kopf", head, Vector3.zero, new Vector3(0.36f, 0.34f, 0.4f), Vector3.zero, feather, false);
            K.Box("Hakenschnabel", head, new Vector3(0f, -0.05f, 0.25f), new Vector3(0.1f, 0.16f, 0.2f), _wBeak, new Vector3(25f, 0f, 0f), false);
            K.Box("Auge L", head, new Vector3(-0.15f, 0.05f, 0.1f), new Vector3(0.02f, 0.04f, 0.05f), _beastEye, default, false);
            K.Box("Auge R", head, new Vector3(0.15f, 0.05f, 0.1f), new Vector3(0.02f, 0.04f, 0.05f), _beastEye, default, false);
            Transform tail = K.Group("Tail", model, new Vector3(0f, 0.8f, -0.55f), Vector3.zero);
            K.Box("Stoß", tail, new Vector3(0f, -0.05f, -0.25f), new Vector3(0.45f, 0.08f, 0.55f), _wGriffinWing, new Vector3(20f, 0f, 0f), false);
            var legs = new Transform[4];
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.22f : 0.22f;
                legs[i] = K.Group(i == 0 ? "Fang L" : "Fang R", model, new Vector3(x, 0.55f, 0.05f), Vector3.zero);
                K.Prim(PrimitiveType.Capsule, "Lauf", legs[i], new Vector3(0f, -0.25f, 0f), new Vector3(0.12f, 0.25f, 0.12f), Vector3.zero, _wBeak, false);
                K.Box("Krallen", legs[i], new Vector3(0f, -0.52f, 0.1f), new Vector3(0.2f, 0.05f, 0.26f), _wRamDark, default, false);
            }
            return WwFinish(root, visual, model, head, tail, legs, def, 1.6f, 1.1f);
        }
    }
}
