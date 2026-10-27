using System.Collections.Generic;
using System.Linq;
using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Enemies;
using Ashveil.Networking;
using Ashveil.World;
using FishNet.Component.Transforming;
using FishNet.Object;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Milestone 3.3 – Gegner:
    ///   · Definitionen: Aschwolf (Rudel), Verblichener (Nahkampf), Verblichene Schuetzin (Pistole)
    ///   · Netzwerk-Prefabs (Aschwolf als prozeduraler Vierbeiner, Verblichene als Humanoid in Asche-Haut)
    ///   · Wolfsrudel am Kiefernhang, Lager der Verblichenen an der Aschenstrasse, NavMesh-Flaeche
    /// Alles serverseitig gesteuert (EnemyBrain + EnemyDirector).
    /// </summary>
    public static partial class EnemyBuilder
    {
        public const string DefFolder = ProjectSetup.Root + "/Data/Enemies";
        public const string PrefabFolder = ProjectSetup.Root + "/Prefabs/Enemies";
        private const int DefaultsVersion = 1;
        private const string VersionKey = "Ashveil.EnemyDefaults.Version";
        // Kanon 26.09.: Aggro-Profile einmalig auf bestehende Definitionen uebertragen (andere eigene Werte bleiben)
        private const int AggroVersion = 1;
        private const string AggroKey = "Ashveil.EnemyDefaults.Aggro";

        private static readonly System.Collections.Generic.Dictionary<string, (AggroProfile profile, float territory, bool howl)> AggroDefaults =
            new System.Collections.Generic.Dictionary<string, (AggroProfile, float, bool)>
            {
                { "enemy.ash_wolf", (AggroProfile.Territorial, 7f, true) },
                { "enemy.faded_one", (AggroProfile.Territorial, 6f, false) },
                { "enemy.faded_gunner", (AggroProfile.Territorial, 10f, false) },
                { "enemy.peat_brute", (AggroProfile.Territorial, 10f, false) },
                { "enemy.needle_spitter", (AggroProfile.Aggressive, 6f, false) },
                { "enemy.soot_lynx", (AggroProfile.Aggressive, 6f, false) },
                { "enemy.bell_shepherd", (AggroProfile.Encounter, 6f, false) }
            };

        public static readonly Vector3 WolfPackA = new Vector3(-42f, 0f, 62f);
        public static readonly Vector3 WolfPackB = new Vector3(-82f, 0f, 96f);
        // Quest-Orte 27.09.: vom Osttor weg unter den Unterarm des Riesen (eine Tabelle: KarethQuestSites)
        public static readonly Vector3 AshCamp = KarethQuestSites.AshCamp;

        private static Material _wolfFur, _wolfDark, _ember, _ashSkin, _rust, _darkWood, _charred, _stone;
        private static Material _peat, _root, _mossGreen, _bogGlow, _chitin, _chitinDark, _spine, _venom, _lynxFur, _lynxDark,
            _burnt, _bronze, _lantern, _warnFill, _warnRim, _needleTrail, _beastEye;

        // ================================================================== Einstieg

        /// <summary>Definitionen + Prefabs + Szene (Lager, Spawnbereiche, NavMesh). Gibt die Netzwerk-Prefabs zurueck.</summary>
        public static List<GameObject> Build(Transform env)
        {
            ProjectSetup.EnsureFolder(DefFolder);
            ProjectSetup.EnsureFolder(PrefabFolder);
            CreateMaterials();

            EnemyDefinition wolf = EnsureDefinition("Enemy_Aschwolf", "enemy.ash_wolf", "Aschwolf", SetupWolf);
            EnemyDefinition husk = EnsureDefinition("Enemy_Verblichener", "enemy.faded_one", "Verblichener", SetupHusk);
            EnemyDefinition gunner = EnsureDefinition("Enemy_VerblicheneSchuetzin", "enemy.faded_gunner", "Verblichene Schuetzin", SetupGunner);
            // Milestone 5.4: Kareth
            EnemyDefinition brute = EnsureDefinition("Enemy_Torfschrat", "enemy.peat_brute", "Torfschrat", SetupPeatBrute);
            EnemyDefinition spitter = EnsureDefinition("Enemy_Nadelspeier", "enemy.needle_spitter", "Nadelspeier", SetupNeedleSpitter);
            EnemyDefinition lynx = EnsureDefinition("Enemy_Russluchs", "enemy.soot_lynx", "Die Graue Mutter", SetupSootLynx);
            EnemyDefinition shepherd = EnsureDefinition("Enemy_Glockenhirte", "enemy.bell_shepherd", "Der Glockenhirte", SetupBellShepherd);
            EditorPrefs.SetInt(VersionKey, DefaultsVersion);
            EditorPrefs.SetInt(AggroKey, AggroVersion);

            GameObject wolfPrefab = BuildBeastPrefab(wolf);
            GameObject huskPrefab = BuildHumanoidPrefab(husk, false);
            GameObject gunnerPrefab = BuildHumanoidPrefab(gunner, true);
            GameObject brutePrefab = BuildColossusPrefab(brute, false);
            GameObject spitterPrefab = BuildNeedleSpitterPrefab(spitter);
            GameObject lynxPrefab = BuildLynxPrefab(lynx);
            GameObject shepherdPrefab = BuildColossusPrefab(shepherd, true);

            Transform enemies = K.Group("Gegner", env);
            // Kiefernhang: Rudel (Stufe 1–4)
            // Kanon 26.09.: Startgegner direkt am Lager greifen neue Spieler nicht von sich aus an (Defensive);
            // je weiter weg, desto oefter Territorial und Aggressive. Keine Social-Aggro.
            SetAggro(Area(enemies, "Rudel Kiefernhang", WolfPackA, wolf, wolfPrefab, 3, 4f, ""), AggroProfile.Defensive);
            SetAggro(Area(enemies, "Rudel Waechterruecken", WolfPackB, wolf, wolfPrefab, 2, 3f, ""), AggroProfile.Defensive);
            Area(enemies, "Rudel Tiefer Wald", Ground(-200f, 150f), wolf, wolfPrefab, 3, 4f, "", 3, 4);
            // Verblichene: Gehoeft (3–4) und Holzfaellerlager (5–7)
            BuildAshCamp(env);
            // Q4 verlangt vier Verblichene: vier Nahkaempfer + zwei Schuetzinnen, damit niemand auf die Wiederkehr warten muss
            Area(enemies, "Verblichene Nahkampf", AshCamp + new Vector3(-2f, 0f, 2f), husk, huskPrefab, 4, 5f, "Lager der Verblichenen");
            Area(enemies, "Verblichene Schuetzin", AshCamp + new Vector3(4f, 0f, -6f), gunner, gunnerPrefab, 2, 2f, "Lager der Verblichenen");
            Vector3 lc = Ground(KarethTerrainBuilder.LumberCamp.x, KarethTerrainBuilder.LumberCamp.z);
            SetAggro(Area(enemies, "Holzfaeller Nahkampf", lc + new Vector3(-3f, 0f, 3f), husk, huskPrefab, 3, 5f, "Holzfaellerlager", 5, 7), AggroProfile.Aggressive);
            SetAggro(Area(enemies, "Holzfaeller Schuetzen", lc + new Vector3(6f, 0f, -4f), gunner, gunnerPrefab, 2, 3f, "Holzfaellerlager", 5, 7), AggroProfile.Aggressive);
            // Glockenmoor: Torfschrate (6–9)
            Area(enemies, "Torfschrat Kuppeln", Ground(20f, 300f), brute, brutePrefab, 2, 8f, "");
            Area(enemies, "Torfschrat Aschenweide", Ground(110f, 380f), brute, brutePrefab, 1, 4f, "");
            Area(enemies, "Torfschrat Nordmoor", Ground(-30f, 420f), brute, brutePrefab, 2, 8f, "");
            // Nadelspeier: Moorrand (5–7) und Kueste (8–10)
            Area(enemies, "Nadelspeier Moorrand Ost", Ground(165f, 270f), spitter, spitterPrefab, 2, 5f, "", 5, 7);
            Area(enemies, "Nadelspeier Moorrand West", Ground(-90f, 330f), spitter, spitterPrefab, 2, 5f, "", 5, 7);
            Area(enemies, "Nadelspeier Kueste", Ground(-320f, -150f), spitter, spitterPrefab, 2, 5f, "", 8, 10);
            Area(enemies, "Nadelspeier Klippen", Ground(-400f, -60f), spitter, spitterPrefab, 2, 5f, "", 8, 10);
            // P3 Glockenrast (Stufe 5–9): zusaetzliche Gruppen fuer die Auftraege G2, G3, G5, G9, G12
            BuildGlockenrastSpawns(enemies, husk, huskPrefab, brute, brutePrefab, spitter, spitterPrefab);
            // Aschenfeld: die Graue Mutter (selten, drei Verstecke) und der Glockenhirte
            EnemySpawnArea lynxArea = Area(enemies, "Die Graue Mutter (Versteck)", Ground(250f, -150f), lynx, lynxPrefab, 1, 1f, "");
            SetAlternatePoints(lynxArea, Ground(470f, 160f), Ground(480f, -190f));
            EnemyRoute route = BuildShepherdRoute(env);
            EnemySpawnArea shepherdArea = Area(enemies, "Der Glockenhirte", route.Point(0), shepherd, shepherdPrefab, 1, 0f, "");
            K.SetRef(shepherdArea, "route", route);

            BuildNavMeshSurface(env);
            var prefabs = new List<GameObject> { wolfPrefab, huskPrefab, gunnerPrefab, brutePrefab, spitterPrefab, lynxPrefab, shepherdPrefab };
            // M6: Gegner der Salzgruben (je Instanz-Platz, erscheinen erst beim Betreten)
            prefabs.AddRange(BuildSaltMineEnemies());
            // 28.09.: Grenzgebirge (die Spawngebiete liegen in den Zellen-Szenen, die Prefabs muessen registriert sein)
            prefabs.AddRange(BuildBorderEnemies().Values.Select(b => b.Prefab));
            return prefabs;
        }

        private static Vector3 Ground(float x, float z) => KarethTerrainBuilder.OnGround(x, z);

        // ================================================================== Definitionen

        private static EnemyDefinition EnsureDefinition(string file, string id, string displayName, System.Action<EnemyDefinition> setup)
        {
            string path = DefFolder + "/" + file + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
            bool fresh = def == null;
            if (fresh)
            {
                def = ScriptableObject.CreateInstance<EnemyDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }
            // Neue Standardwerte uebernehmen, eigene Anpassungen sonst behalten
            if (fresh || EditorPrefs.GetInt(VersionKey, 0) < DefaultsVersion) setup(def);
            if ((fresh || EditorPrefs.GetInt(AggroKey, 0) < AggroVersion) && AggroDefaults.TryGetValue(id, out var ag))
            {
                def.aggroProfile = ag.profile;
                def.territoryRadius = ag.territory;
                def.howlsOnEngage = ag.howl;
            }
            K.SetString(def, "id", id);
            K.SetString(def, "displayName", displayName);
            EditorUtility.SetDirty(def);
            return def;
        }

        private static void SetupWolf(EnemyDefinition d)
        {
            d.body = EnemyBody.Beast; d.style = EnemyStyle.Melee;
            d.minLevel = 1; d.maxLevel = 3;
            d.baseHealth = 55f; d.healthPerLevel = 12f;
            d.minDamage = 3f; d.maxDamage = 5f; d.damagePerLevel = 1f;
            d.attackRange = 1.9f; d.attackInterval = 1.6f; d.windup = 0.45f; d.attackArc = 110f;
            d.walkSpeed = 1.6f; d.runSpeed = 6.8f; d.wanderRadius = 6f;
            d.aggroRadius = 13f; d.leashDistance = 40f; d.howlsOnEngage = true; d.aggroProfile = AggroProfile.Territorial; d.territoryRadius = 7f;
            d.fleeHealthFraction = 0.15f; // flieht selten (1 von 10 Kämpfen, EnemyBrain; Max 07.10.)
            d.sidestepChance = WolfSidestepChance;
            d.corpseTime = 25f; d.respawnTime = 45f;
            d.bodyColor = new Color(0.2f, 0.19f, 0.18f); d.glowColor = new Color(1f, 0.45f, 0.12f); d.scale = 1f;
        }

        private static void SetupHusk(EnemyDefinition d)
        {
            d.body = EnemyBody.Humanoid; d.style = EnemyStyle.Melee; d.femaleModel = false;
            d.minLevel = 3; d.maxLevel = 4;
            d.baseHealth = 90f; d.healthPerLevel = 15f;
            d.minDamage = 6f; d.maxDamage = 9f; d.damagePerLevel = 1.5f;
            d.attackRange = 2.3f; d.attackInterval = 2.1f; d.windup = 0.6f; d.attackArc = 120f;
            d.walkSpeed = 1.3f; d.runSpeed = 5.2f; d.wanderRadius = 4f;
            d.aggroRadius = 11f; d.leashDistance = 36f; d.aggroProfile = AggroProfile.Territorial; d.territoryRadius = 6f;
            d.fleeHealthFraction = 0f;
            d.corpseTime = 25f; d.respawnTime = 50f;
            d.bodyColor = new Color(0.32f, 0.3f, 0.28f); d.glowColor = new Color(1f, 0.5f, 0.2f); d.scale = 1f;
        }

        private static void SetupGunner(EnemyDefinition d)
        {
            d.body = EnemyBody.Humanoid; d.style = EnemyStyle.Ranged; d.femaleModel = true;
            d.minLevel = 3; d.maxLevel = 4;
            d.baseHealth = 70f; d.healthPerLevel = 12f;
            d.minDamage = 8f; d.maxDamage = 12f; d.damagePerLevel = 1.5f;
            d.attackRange = 20f; d.attackInterval = 3.2f; d.windup = 0.9f; d.attackArc = 60f;
            d.preferredDistance = 11f; d.tooCloseDistance = 5f; d.rangedAccuracy = 0.75f;
            d.walkSpeed = 1.3f; d.runSpeed = 5f; d.wanderRadius = 2f;
            d.aggroRadius = 17f; d.leashDistance = 36f; d.aggroProfile = AggroProfile.Territorial; d.territoryRadius = 10f;
            d.fleeHealthFraction = 0f;
            d.corpseTime = 25f; d.respawnTime = 50f;
            d.bodyColor = new Color(0.32f, 0.3f, 0.28f); d.glowColor = new Color(1f, 0.5f, 0.2f); d.scale = 1f;
        }

        private static void SetupPeatBrute(EnemyDefinition d)
        {
            d.body = EnemyBody.Colossus; d.style = EnemyStyle.Melee;
            d.minLevel = 6; d.maxLevel = 9;
            d.baseHealth = 260f; d.healthPerLevel = 35f;
            d.minDamage = 12f; d.maxDamage = 16f; d.damagePerLevel = 2f;
            d.attackRange = 3.2f; d.attackInterval = 2.8f; d.windup = 0.9f; d.attackArc = 120f;
            d.areaAttackEvery = 2; d.areaRadius = 5f; d.areaWindup = 1.2f; d.areaDamageMultiplier = 1.4f;
            d.walkSpeed = 1.1f; d.runSpeed = 3.3f; d.wanderRadius = 5f;
            d.aggroRadius = 12f; d.leashDistance = 35f; d.aggroProfile = AggroProfile.Territorial; d.territoryRadius = 10f;
            d.fleeHealthFraction = 0f; d.fireDamageTaken = 1.5f;
            d.corpseTime = 30f; d.respawnTime = 70f;
            d.bodyColor = new Color(0.22f, 0.17f, 0.12f); d.glowColor = new Color(0.7f, 0.9f, 0.3f); d.scale = 1f;
        }

        private static void SetupNeedleSpitter(EnemyDefinition d)
        {
            d.body = EnemyBody.Beast; d.style = EnemyStyle.Ranged;
            d.minLevel = 5; d.maxLevel = 10;
            d.baseHealth = 80f; d.healthPerLevel = 13f;
            d.minDamage = 5f; d.maxDamage = 8f; d.damagePerLevel = 1.2f;
            d.attackRange = 22f; d.attackInterval = 2.4f; d.windup = 0.7f; d.attackArc = 60f;
            d.preferredDistance = 17f; d.tooCloseDistance = 9f; d.rangedAccuracy = 0.7f;
            d.poisonPerSecond = 3f; d.poisonDuration = 6f; d.diveWhenPressedFor = 2.5f;
            d.walkSpeed = 1.2f; d.runSpeed = 5f; d.wanderRadius = 4f;
            d.aggroRadius = 18f; d.leashDistance = 40f; d.aggroProfile = AggroProfile.Aggressive;
            d.fleeHealthFraction = 0f;
            d.corpseTime = 25f; d.respawnTime = 60f;
            d.bodyColor = new Color(0.3f, 0.34f, 0.28f); d.glowColor = new Color(0.5f, 1f, 0.3f); d.scale = 1f;
        }

        private static void SetupSootLynx(EnemyDefinition d)
        {
            d.body = EnemyBody.Beast; d.style = EnemyStyle.Melee;
            d.minLevel = 11; d.maxLevel = 11;
            d.baseHealth = 900f; d.healthPerLevel = 0f;
            d.minDamage = 18f; d.maxDamage = 26f; d.damagePerLevel = 0f;
            d.attackRange = 2.2f; d.attackInterval = 1.5f; d.windup = 0.4f; d.attackArc = 110f;
            d.camouflaged = true; d.revealRadius = 8f; d.pounceRange = 12f; d.huntsLoners = true; d.elite = true;
            d.walkSpeed = 1.6f; d.runSpeed = 8.5f; d.wanderRadius = 3f;
            d.aggroRadius = 22f; d.leashDistance = 60f; d.aggroProfile = AggroProfile.Aggressive;
            d.fleeHealthFraction = 0f;
            d.corpseTime = 60f; d.respawnTime = 2700f; d.respawnTimeMax = 5400f; // 45–90 min
            d.bodyColor = new Color(0.17f, 0.16f, 0.16f); d.glowColor = new Color(1f, 0.45f, 0.12f); d.scale = 1.25f;
        }

        private static void SetupBellShepherd(EnemyDefinition d)
        {
            d.body = EnemyBody.Colossus; d.style = EnemyStyle.Melee;
            d.minLevel = 34; d.maxLevel = 34;
            d.baseHealth = 2100000f; d.healthPerLevel = 0f;
            // Normaler Schlag ~70 % des Lebens eines Stufe-5-Spielers (170)
            d.minDamage = 110f; d.maxDamage = 125f; d.damagePerLevel = 0f;
            d.attackRange = 6f; d.attackInterval = 3.5f; d.windup = 1.1f; d.attackArc = 140f;
            d.areaAttackEvery = 3; d.areaRadius = 8f; d.areaWindup = 1.5f; d.areaDamageMultiplier = 1.1f;
            d.walkSpeed = 1.6f; d.runSpeed = 4.2f; d.wanderRadius = 0f;
            d.aggroRadius = 22f; d.leashDistance = 80f; d.aggroProfile = AggroProfile.Encounter;
            d.fleeHealthFraction = 0f; d.levelLabel = "??"; d.elite = true;
            d.corpseTime = 120f; d.respawnTime = 900f;
            d.bodyColor = new Color(0.1f, 0.09f, 0.08f); d.glowColor = new Color(1f, 0.6f, 0.25f); d.scale = 4.1f;
        }

        // ================================================================== Materialien

        private static void CreateMaterials()
        {
            _wolfFur = K.Mat("Enemy_WolfFur", new Color(0.19f, 0.18f, 0.17f), 0.05f);
            _wolfDark = K.Mat("Enemy_WolfDark", new Color(0.09f, 0.085f, 0.08f), 0.05f);
            _ember = K.Mat("Enemy_EmberGlow", new Color(1f, 0.45f, 0.12f), 0.3f, new Color(3.2f, 1.1f, 0.25f));
            _ashSkin = K.Mat("Enemy_AshSkin", new Color(0.3f, 0.29f, 0.27f), 0.12f);
            _rust = K.Mat("Enemy_RustyMetal", new Color(0.36f, 0.24f, 0.17f), 0.35f);
            _darkWood = K.Mat("Enemy_DarkWood", new Color(0.18f, 0.12f, 0.08f), 0.05f);
            _charred = K.Mat("Enemy_Charred", new Color(0.08f, 0.07f, 0.065f), 0.05f);
            _stone = K.Mat("Enemy_RuinStone", new Color(0.42f, 0.4f, 0.37f), 0.08f);
            _peat = K.Mat("Enemy_Peat", new Color(0.2f, 0.15f, 0.1f), 0.2f);
            _root = K.Mat("Enemy_Root", new Color(0.32f, 0.24f, 0.16f), 0.1f);
            _mossGreen = K.Mat("Enemy_Moss", new Color(0.22f, 0.32f, 0.16f), 0.05f);
            _bogGlow = K.Mat("Enemy_BogGlow", new Color(0.6f, 0.9f, 0.3f), 0.2f, new Color(1.2f, 2.2f, 0.4f));
            _chitin = K.Mat("Enemy_Chitin", new Color(0.3f, 0.35f, 0.28f), 0.55f);
            _chitinDark = K.Mat("Enemy_ChitinDark", new Color(0.14f, 0.17f, 0.14f), 0.5f);
            _spine = K.Mat("Enemy_Spine", new Color(0.82f, 0.78f, 0.62f), 0.3f);
            _venom = K.Mat("Enemy_VenomGlow", new Color(0.5f, 1f, 0.3f), 0.3f, new Color(0.8f, 2.4f, 0.4f));
            _lynxFur = K.Mat("Enemy_LynxFur", new Color(0.17f, 0.16f, 0.16f), 0.05f);
            _lynxDark = K.Mat("Enemy_LynxDark", new Color(0.07f, 0.065f, 0.065f), 0.05f);
            _burnt = K.Mat("Enemy_BurntWood", new Color(0.09f, 0.075f, 0.065f), 0.05f);
            _bronze = K.Mat("Enemy_BellBronze", new Color(0.52f, 0.37f, 0.19f), 0.6f);
            _lantern = K.Mat("Enemy_Lantern", new Color(1f, 0.6f, 0.25f), 0f, new Color(4f, 2.2f, 0.7f));
            _warnFill = UnlitTransparent("Enemy_WarningFill", new Color(1f, 0.18f, 0.08f, 0.28f));
            _warnRim = UnlitTransparent("Enemy_WarningRim", new Color(1f, 0.35f, 0.12f, 0.6f));
            _needleTrail = UnlitTransparent("Enemy_NeedleTrail", new Color(0.7f, 1f, 0.4f, 0.8f));
            // Natürliche Tieraugen ohne Eigenleuchten (Kanon 03.10.: Aschwolf mit normalen Augen)
            _beastEye = K.Mat("Enemy_BeastEye", new Color(0.24f, 0.16f, 0.07f), 0.7f);
        }

        /// <summary>Durchscheinendes, unbeleuchtetes Material (Warnring, Nadelspur).</summary>
        private static Material UnlitTransparent(string name, Color color)
        {
            string path = ProjectSetup.GreyboxMaterials + "/" + name + ".mat";
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return K.Mat(name, color, 0f, color);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = sh;
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Surface", 1f);   // Transparent
            mat.SetFloat("_Blend", 0f);     // Alpha
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Warnring-Materialien an die Darstellung haengen (gilt fuer alle Arten).</summary>
        private static void SetWarningMaterials(EnemyVisual vis)
        {
            K.SetRef(vis, "warningFill", _warnFill);
            K.SetRef(vis, "warningRim", _warnRim);
        }

        // ================================================================== Aschwolf (Vierbeiner)

        private static GameObject BuildBeastPrefab(EnemyDefinition def)
        {
            var root = new GameObject("Enemy_" + def.name.Replace("Enemy_", ""));
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 2; col.radius = 0.32f; col.height = 1.35f; col.center = new Vector3(0f, 0.6f, 0.05f);

            Transform visual = K.Group("Visual", root.transform);
            Transform model = K.Group("Model", visual);
            float s = def.scale;
            model.localScale = Vector3.one * s;

            // Rumpf und Brust
            K.Prim(PrimitiveType.Capsule, "Body", model, new Vector3(0f, 0.62f, -0.05f), new Vector3(0.44f, 0.56f, 0.44f), new Vector3(90f, 0f, 0f), _wolfFur, false);
            K.Prim(PrimitiveType.Sphere, "Chest", model, new Vector3(0f, 0.68f, 0.3f), new Vector3(0.48f, 0.52f, 0.5f), Vector3.zero, _wolfFur, false);
            K.Prim(PrimitiveType.Sphere, "Mane", model, new Vector3(0f, 0.8f, 0.36f), new Vector3(0.42f, 0.36f, 0.4f), new Vector3(-20f, 0f, 0f), _wolfDark, false);
            // Kanon 03.10.: natürliches, rußgraues Fell, normale Augen – keine Glutrisse mehr (bis 07.10. „Ember Crack A/B“, „Tail Ember“)

            // Kopf (Drehpunkt am Hals)
            Transform head = K.Group("Head", model, new Vector3(0f, 0.82f, 0.55f), Vector3.zero);
            K.Box("Skull", head, new Vector3(0f, 0.06f, 0.14f), new Vector3(0.24f, 0.24f, 0.3f), _wolfFur, default, false);
            K.Box("Snout", head, new Vector3(0f, 0.0f, 0.36f), new Vector3(0.13f, 0.12f, 0.24f), _wolfDark, new Vector3(8f, 0f, 0f), false);
            K.Box("Jaw", head, new Vector3(0f, -0.06f, 0.3f), new Vector3(0.11f, 0.05f, 0.2f), _wolfDark, new Vector3(14f, 0f, 0f), false);
            K.Box("Ear L", head, new Vector3(-0.08f, 0.24f, 0.06f), new Vector3(0.06f, 0.14f, 0.05f), _wolfDark, new Vector3(-10f, 0f, 12f), false);
            K.Box("Ear R", head, new Vector3(0.08f, 0.24f, 0.06f), new Vector3(0.06f, 0.14f, 0.05f), _wolfDark, new Vector3(-10f, 0f, -12f), false);
            K.Box("Eye L", head, new Vector3(-0.07f, 0.1f, 0.29f), new Vector3(0.05f, 0.025f, 0.02f), _beastEye, default, false);
            K.Box("Eye R", head, new Vector3(0.07f, 0.1f, 0.29f), new Vector3(0.05f, 0.025f, 0.02f), _beastEye, default, false);

            // Beine (Drehpunkt an der Huefte/Schulter)
            var legs = new Transform[4];
            Vector3[] legPos =
            {
                new Vector3(-0.13f, 0.56f, 0.36f), new Vector3(0.13f, 0.56f, 0.36f),
                new Vector3(-0.14f, 0.56f, -0.36f), new Vector3(0.14f, 0.56f, -0.36f)
            };
            string[] legNames = { "Leg FL", "Leg FR", "Leg BL", "Leg BR" };
            for (int i = 0; i < 4; i++)
            {
                legs[i] = K.Group(legNames[i], model, legPos[i], Vector3.zero);
                K.Prim(PrimitiveType.Capsule, "Upper", legs[i], new Vector3(0f, -0.18f, 0f), new Vector3(0.13f, 0.2f, 0.13f), Vector3.zero, _wolfFur, false);
                K.Prim(PrimitiveType.Capsule, "Lower", legs[i], new Vector3(0f, -0.42f, i < 2 ? 0.01f : -0.02f), new Vector3(0.09f, 0.16f, 0.09f), Vector3.zero, _wolfDark, false);
                K.Box("Paw", legs[i], new Vector3(0f, -0.54f, 0.04f), new Vector3(0.1f, 0.05f, 0.14f), _wolfDark, default, false);
            }

            // Schwanz
            Transform tail = K.Group("Tail", model, new Vector3(0f, 0.72f, -0.6f), Vector3.zero);
            K.Prim(PrimitiveType.Capsule, "Tail Mesh", tail, new Vector3(0f, -0.1f, -0.18f), new Vector3(0.1f, 0.24f, 0.1f), new Vector3(-55f, 0f, 0f), _wolfDark, false);

            var agent = AddCommon(root, visual, def, new Vector3(0f, 0.7f, 0.1f), 0.9f, 1.25f, 0.4f, 1f, 0.75f);

            var vis = root.AddComponent<EnemyBeastVisual>();
            SetWarningMaterials(vis);
            K.SetRef(vis, "model", model);
            K.SetRef(vis, "head", head);
            K.SetRef(vis, "tail", tail);
            var so = new SerializedObject(vis);
            SerializedProperty lp = so.FindProperty("legs");
            lp.arraySize = 4;
            for (int i = 0; i < 4; i++) lp.GetArrayElementAtIndex(i).objectReferenceValue = legs[i];
            so.FindProperty("runSpeedReference").floatValue = def.runSpeed;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, agent);
        }

        // ================================================================== Verblichene (Humanoid)

        private static GameObject BuildHumanoidPrefab(EnemyDefinition def, bool ranged)
        {
            var root = new GameObject("Enemy_" + def.name.Replace("Enemy_", ""));
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 1; col.radius = 0.35f; col.height = 1.8f; col.center = new Vector3(0f, 0.9f, 0f);

            Transform visual = K.Group("Visual", root.transform);
            Animator animator = null;

            bool baseChars = QuaterniusImport.BaseCharactersImported;
            string path = def.femaleModel
                ? (baseChars ? QuaterniusImport.BaseFemalePath : QuaterniusImport.FemaleModelPath)
                : (baseChars ? QuaterniusImport.BaseMalePath : QuaterniusImport.MaleModelPath);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                def.femaleModel ? PlayerAnimatorBuilder.FemaleOverridePath : PlayerAnimatorBuilder.ControllerPath);
            GameObject model = QuaterniusImport.IsImported ? PlayerRigBuilder.InstantiateModel(path, "Model", visual, controller) : null;
            if (model != null)
            {
                animator = model.GetComponent<Animator>();
                // Asche-Haut, gluehende Augen
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                {
                    bool eyes = r.name.ToLowerInvariant().Contains("eye") && !r.name.ToLowerInvariant().Contains("brow");
                    Material m = eyes ? _ember : _ashSkin;
                    r.sharedMaterials = Enumerable.Repeat(m, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
                }
            }
            else
            {
                K.Prim(PrimitiveType.Capsule, "Body (Platzhalter)", visual, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), Vector3.zero, _ashSkin, false);
            }

            // Waffe (Drehpunkt = Griff, +Z = Klinge/Lauf)
            Transform weapon;
            if (!ranged)
            {
                weapon = K.Group("Rusty Blade", visual, new Vector3(0.35f, 1f, 0.2f), Vector3.zero);
                K.Box("Grip", weapon, new Vector3(0f, 0f, -0.04f), new Vector3(0.035f, 0.035f, 0.16f), _darkWood, default, false);
                K.Box("Guard", weapon, new Vector3(0f, 0f, 0.05f), new Vector3(0.16f, 0.03f, 0.035f), _rust, default, false);
                K.Box("Blade", weapon, new Vector3(0f, 0f, 0.42f), new Vector3(0.06f, 0.012f, 0.7f), _rust, new Vector3(0f, 0f, 4f), false);
                K.Box("Notch", weapon, new Vector3(0.028f, 0f, 0.55f), new Vector3(0.02f, 0.013f, 0.06f), _charred, default, false);
            }
            else
            {
                // Max 07.10.2026: Bogen statt Pistole (Feuerwaffen ruhen, auch bei Gegnern); bis dahin „Old Wheellock“
                weapon = K.Group("Old Bow", visual, new Vector3(0.3f, 1.2f, 0.3f), Vector3.zero);
                BuildEnemyBowParts(weapon);
            }

            var agent = AddCommon(root, visual, def, new Vector3(0f, 1.3f, 0f), 0.8f, 2.15f, 0.35f, 1.8f, 1.55f);

            var vis = root.AddComponent<EnemyHumanoidVisual>();
            SetWarningMaterials(vis);
            K.SetRef(vis, "animator", animator);
            K.SetRef(vis, "weapon", weapon);
            K.SetBool(vis, "ranged", ranged);
            K.SetBool(vis, "bow", ranged);
            K.SetRef(vis, "tracerMaterial", PlayerRigBuilder.TracerMaterial());

            return SavePrefab(root, agent);
        }

        /// <summary>
        /// Einfacher Bogen (Greybox, etwa 1,25 m). Drehpunkt = Griff; die Wurfarme liegen entlang ±Z wie eine Klinge aus der Faust,
        /// zur Spitze hin zur Sehnenseite (+Y) gebogen; die Sehne läuft gerade von Spitze zu Spitze.
        /// </summary>
        private static void BuildEnemyBowParts(Transform bow)
        {
            K.Box("Grip", bow, Vector3.zero, new Vector3(0.04f, 0.05f, 0.14f), _darkWood, default, false);
            // je Wurfarm drei Glieder: (Mitte z, Mitte y, Länge, Neigung in Grad)
            (float z, float y, float len, float tilt)[] limb = { (0.17f, 0.007f, 0.21f, 4.3f), (0.365f, 0.032f, 0.2f, 10.4f), (0.54f, 0.085f, 0.17f, 23.6f) };
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < limb.Length; i++)
                    K.Box((s > 0 ? "Limb Upper " : "Limb Lower ") + (char)('A' + i), bow, new Vector3(0f, limb[i].y, s * limb[i].z),
                        new Vector3(0.034f - i * 0.006f, 0.04f - i * 0.007f, limb[i].len), _darkWood, new Vector3(-s * limb[i].tilt, 0f, 0f), false);
            K.Box("String", bow, new Vector3(0f, 0.12f, 0f), new Vector3(0.006f, 0.006f, 1.24f), _spine, default, false);
        }

        // ================================================================== Auftrag „gegner_anpassen“ (07.10.2026)

        private const float WolfSidestepChance = 0.6f;

        /// <summary>
        /// Bestehende Gegner-Prefabs an Max' Entscheidungen vom 07.10.2026 anpassen, ohne eine Szene neu zu bauen: Sandbox_M1 und
        /// Kareth v4 bleiben unverändert, die Prefabs werden an Ort und Stelle bearbeitet (Kennungen und Netz-Objekte bleiben).
        ///  · Aschwolf und Gratwolf: keine Glut im Fell, natürliche Augen (Kanon 03.10.), kurzer Seitschritt zwischen zwei Angriffen.
        ///  · Verblichene Schützin und Deserteurin: Bogen statt Radschlosspistole.
        /// Ein späterer Neubau ergibt dasselbe (BuildBeastPrefab, BuildHumanoidPrefab, SetupWolf). Mehrfach aufrufbar.
        /// </summary>
        public static string ApplyDecisions0710()
        {
            ProjectSetup.EnsureFolder(PrefabFolder);
            CreateMaterials();
            var log = new List<string>();
            foreach (string file in new[] { "Enemy_Aschwolf", "Enemy_Gratwolf" })
            {
                log.Add(PatchPrefab(file, root =>
                {
                    int removed = 0, eyes = 0;
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true).ToArray())
                        if (t != null && (t.name == "Ember Crack A" || t.name == "Ember Crack B" || t.name == "Tail Ember"))
                        {
                            Object.DestroyImmediate(t.gameObject);
                            removed++;
                        }
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                        if (r.name == "Eye L" || r.name == "Eye R")
                        {
                            r.sharedMaterial = _beastEye;
                            eyes++;
                        }
                    return $"{removed} Glutteile weg, {eyes} Augen natürlich";
                }));
                var def = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefFolder + "/" + file + ".asset");
                if (def != null && def.sidestepChance <= 0f)
                {
                    def.sidestepChance = WolfSidestepChance;
                    EditorUtility.SetDirty(def);
                    log.Add($"{file}: Seitschritt {WolfSidestepChance:0.0}");
                }
            }
            foreach (string file in new[] { "Enemy_VerblicheneSchuetzin", "Enemy_DeserteurSchuetzin" })
                log.Add(PatchPrefab(file, root =>
                {
                    var vis = root.GetComponent<EnemyHumanoidVisual>();
                    if (vis == null) return "keine Menschen-Darstellung";
                    var so = new SerializedObject(vis);
                    Transform weapon = so.FindProperty("weapon")?.objectReferenceValue as Transform;
                    SerializedProperty bow = so.FindProperty("bow");
                    if (weapon == null || bow == null) return "keine Waffe";
                    if (bow.boolValue && weapon.name == "Old Bow") return "Bogen schon da";
                    for (int i = weapon.childCount - 1; i >= 0; i--) Object.DestroyImmediate(weapon.GetChild(i).gameObject);
                    weapon.name = "Old Bow";
                    BuildEnemyBowParts(weapon);
                    K.MakeDynamic(weapon.gameObject);
                    K.SetLayerRecursively(weapon.gameObject, LayerMask.NameToLayer(GameLayers.Enemy));
                    bow.boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    return "Bogen statt Pistole";
                }));
            AssetDatabase.SaveAssets();
            return "Gegner angepasst (Max 07.10.): " + string.Join(" · ", log);
        }

        private static string PatchPrefab(string file, System.Func<GameObject, string> change)
        {
            string path = PrefabFolder + "/" + file + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return file + ": fehlt";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                string what = change(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return file + ": " + what;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ================================================================== Kolosse (Torfschrat, Glockenhirte)

        /// <summary>
        /// Schwerer Zweibeiner aus Grundformen, Masse fuer 3,4 m Hoehe, skaliert mit def.scale
        /// (Glockenhirte 4,1 = ~14 m). Glockenhirte: verkohltes Holz, Glocken am Ruecken, Laterne, Stab.
        /// </summary>
        private static GameObject BuildColossusPrefab(EnemyDefinition def, bool shepherd)
        {
            float s = def.scale;
            var root = new GameObject("Enemy_" + def.name.Replace("Enemy_", ""));
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 1; col.radius = 0.75f * s; col.height = 3.3f * s; col.center = new Vector3(0f, 1.65f * s, 0f);

            Transform visual = K.Group("Visual", root.transform);
            Transform model = K.Group("Model", visual);
            model.localScale = Vector3.one * s;
            Material body = shepherd ? _burnt : _peat;
            Material limb = shepherd ? _burnt : _root;
            Material dark = shepherd ? _charred : _mossGreen;
            Material glow = shepherd ? _ember : _bogGlow;

            // Beine (Drehpunkt Huefte)
            Transform legL = K.Group("Leg L", model, new Vector3(-0.42f, 1.3f, 0f), Vector3.zero);
            Transform legR = K.Group("Leg R", model, new Vector3(0.42f, 1.3f, 0f), Vector3.zero);
            foreach (Transform leg in new[] { legL, legR })
            {
                K.Box("Thigh", leg, new Vector3(0f, -0.45f, 0f), new Vector3(0.5f, 0.9f, 0.5f), limb, default, false);
                K.Box("Shin", leg, new Vector3(0f, -1.0f, 0.04f), new Vector3(0.42f, 0.6f, 0.42f), body, default, false);
                K.Box("Foot", leg, new Vector3(0f, -1.25f, 0.12f), new Vector3(0.5f, 0.12f, 0.75f), dark, default, false);
            }

            // Rumpf (Drehpunkt Taille), leicht gebeugt
            Transform torso = K.Group("Torso", model, new Vector3(0f, 1.3f, 0f), new Vector3(shepherd ? 18f : 8f, 0f, 0f));
            K.Box("Chest", torso, new Vector3(0f, 0.75f, 0.05f), new Vector3(1.45f, 1.35f, 0.95f), body, default, false);
            K.Box("Belly", torso, new Vector3(0f, 0.15f, 0.05f), new Vector3(1.15f, 0.5f, 0.8f), limb, default, false);
            Transform head = K.Group("Head", torso, new Vector3(0f, 1.55f, 0.3f), Vector3.zero);
            K.Prim(PrimitiveType.Sphere, "Skull", head, new Vector3(0f, 0.2f, 0.05f), new Vector3(0.65f, 0.72f, 0.65f), Vector3.zero, shepherd ? _darkWood : body, false);
            K.Box("Eye L", head, new Vector3(-0.13f, 0.25f, 0.33f), new Vector3(0.12f, 0.05f, 0.03f), glow, default, false);
            K.Box("Eye R", head, new Vector3(0.13f, 0.25f, 0.33f), new Vector3(0.12f, 0.05f, 0.03f), glow, default, false);

            Transform armL = K.Group("Arm L", torso, new Vector3(-0.92f, 1.3f, 0.05f), Vector3.zero);
            Transform armR = K.Group("Arm R", torso, new Vector3(0.92f, 1.3f, 0.05f), Vector3.zero);
            foreach (Transform arm in new[] { armL, armR })
            {
                K.Box("Upper", arm, new Vector3(0f, -0.45f, 0f), new Vector3(0.38f, 0.9f, 0.38f), limb, default, false);
                K.Box("Fore", arm, new Vector3(0f, -1.15f, 0.05f), new Vector3(0.34f, 0.7f, 0.34f), body, default, false);
                K.Prim(PrimitiveType.Sphere, "Fist", arm, new Vector3(0f, -1.6f, 0.08f), new Vector3(0.5f, 0.5f, 0.5f), Vector3.zero, dark, false);
            }

            var bells = new List<Transform>();
            if (shepherd)
            {
                // Glocken an Ketten am Ruecken
                Vector3[] bellPos = { new Vector3(-0.45f, 0.95f, -0.62f), new Vector3(0.45f, 0.85f, -0.62f), new Vector3(0f, 0.35f, -0.6f) };
                float[] bellSize = { 0.45f, 0.4f, 0.55f };
                for (int i = 0; i < bellPos.Length; i++)
                {
                    Transform b = K.Group("Bell " + (i + 1), torso, bellPos[i], Vector3.zero);
                    K.Box("Chain", b, new Vector3(0f, -0.1f, 0f), new Vector3(0.04f, 0.2f, 0.04f), _rust, default, false);
                    K.Prim(PrimitiveType.Cylinder, "Bell", b, new Vector3(0f, -0.32f, 0f), new Vector3(bellSize[i], bellSize[i] * 0.45f, bellSize[i]), Vector3.zero, _bronze, false);
                    bells.Add(b);
                }
                // Laterne in der rechten Hand, Hirtenstab in der linken
                K.Prim(PrimitiveType.Sphere, "Lantern", armR, new Vector3(0f, -1.95f, 0.15f), new Vector3(0.28f, 0.34f, 0.28f), Vector3.zero, _lantern, false);
                Light l = K.PointLight("Lantern Light", armR, new Vector3(0f, -1.95f, 0.15f), new Color(1f, 0.65f, 0.3f), 7f, 4f, true);
                if (l != null) l.range = 7f * s;
                K.Prim(PrimitiveType.Cylinder, "Staff", armL, new Vector3(0f, -1.2f, 0.25f), new Vector3(0.09f, 1.6f, 0.09f), new Vector3(8f, 0f, 0f), _darkWood, false);
            }
            else
            {
                // Torfschrat: Wurzeln, Moospolster, gluehende Sumpfpilze
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 60f;
                    Transform r = K.Group("Root", torso, new Vector3(0f, 0.9f, 0f), new Vector3(0f, a, 0f));
                    K.Prim(PrimitiveType.Cylinder, "Tendril", r, new Vector3(0f, 0.2f, 0.55f), new Vector3(0.08f, 0.45f, 0.08f), new Vector3(35f + i * 5f, 0f, 0f), _root, false);
                }
                K.Box("Moss Back", torso, new Vector3(0f, 1.2f, -0.45f), new Vector3(1.3f, 0.35f, 0.3f), _mossGreen, new Vector3(-15f, 0f, 0f), false);
                K.Box("Moss Shoulder", torso, new Vector3(-0.8f, 1.45f, 0f), new Vector3(0.6f, 0.2f, 0.6f), _mossGreen, default, false);
                K.Prim(PrimitiveType.Sphere, "Glow Cap", torso, new Vector3(0.5f, 1.35f, -0.35f), new Vector3(0.18f, 0.12f, 0.18f), Vector3.zero, _bogGlow, false);
                K.Prim(PrimitiveType.Sphere, "Glow Cap", torso, new Vector3(-0.3f, 0.6f, 0.52f), new Vector3(0.14f, 0.1f, 0.14f), Vector3.zero, _bogGlow, false);
            }

            float height = 3.4f * s;
            var agent = AddCommon(root, visual, def, new Vector3(0f, 2f * s, 0f), shepherd ? 3.5f : 1.3f, height + (shepherd ? 2.5f : 0.6f),
                Mathf.Min(1.2f, 0.6f * s), height, 2.9f * s);

            var vis = root.AddComponent<EnemyColossusVisual>();
            SetWarningMaterials(vis);
            K.SetRef(vis, "model", model);
            K.SetRef(vis, "torso", torso);
            K.SetRef(vis, "head", head);
            K.SetRef(vis, "legL", legL);
            K.SetRef(vis, "legR", legR);
            K.SetRef(vis, "armL", armL);
            K.SetRef(vis, "armR", armR);
            K.SetFloat(vis, "strideLength", 1.6f * s);
            K.SetFloat(vis, "stepTremorRadius", shepherd ? 40f : 12f);
            K.SetFloat(vis, "stepTremorStrength", shepherd ? 0.22f : 0.06f);
            K.SetFloat(vis, "stepVolume", shepherd ? 0.9f : 0.35f);
            K.SetBool(vis, "ringBells", shepherd);
            var so = new SerializedObject(vis);
            SerializedProperty bp = so.FindProperty("bells");
            bp.arraySize = bells.Count;
            for (int i = 0; i < bells.Count; i++) bp.GetArrayElementAtIndex(i).objectReferenceValue = bells[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            if (shepherd) agent.avoidancePriority = 10; // alle anderen weichen ihm aus
            return SavePrefab(root, agent);
        }

        // ================================================================== Nadelspeier (Amphibie)

        private static GameObject BuildNeedleSpitterPrefab(EnemyDefinition def)
        {
            var root = new GameObject("Enemy_" + def.name.Replace("Enemy_", ""));
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 2; col.radius = 0.38f; col.height = 1.5f; col.center = new Vector3(0f, 0.45f, 0f);

            Transform visual = K.Group("Visual", root.transform);
            Transform model = K.Group("Model", visual);
            model.localScale = Vector3.one * def.scale;

            K.Prim(PrimitiveType.Sphere, "Body", model, new Vector3(0f, 0.45f, -0.05f), new Vector3(0.85f, 0.5f, 1.25f), Vector3.zero, _chitin, false);
            K.Prim(PrimitiveType.Sphere, "Belly", model, new Vector3(0f, 0.32f, 0f), new Vector3(0.7f, 0.3f, 1f), Vector3.zero, _chitinDark, false);
            // Stacheln auf dem Ruecken
            for (int i = 0; i < 9; i++)
            {
                float z = -0.45f + i * 0.11f;
                float x = (i % 3 - 1) * 0.16f;
                K.Box("Spine", model, new Vector3(x, 0.72f + (i % 2) * 0.04f, z), new Vector3(0.03f, 0.32f, 0.03f), _spine, new Vector3(-35f, 0f, x * 60f), false);
            }
            K.Box("Venom Sac", model, new Vector3(0f, 0.55f, 0.35f), new Vector3(0.28f, 0.14f, 0.2f), _venom, default, false);

            Transform head = K.Group("Head", model, new Vector3(0f, 0.5f, 0.6f), Vector3.zero);
            K.Box("Skull", head, new Vector3(0f, 0f, 0.14f), new Vector3(0.46f, 0.24f, 0.36f), _chitin, default, false);
            K.Box("Jaw", head, new Vector3(0f, -0.1f, 0.18f), new Vector3(0.42f, 0.06f, 0.32f), _chitinDark, default, false);
            K.Prim(PrimitiveType.Sphere, "Eye L", head, new Vector3(-0.17f, 0.11f, 0.2f), new Vector3(0.1f, 0.1f, 0.1f), Vector3.zero, _venom, false);
            K.Prim(PrimitiveType.Sphere, "Eye R", head, new Vector3(0.17f, 0.11f, 0.2f), new Vector3(0.1f, 0.1f, 0.1f), Vector3.zero, _venom, false);
            K.Box("Snout Spine", head, new Vector3(0f, 0.02f, 0.38f), new Vector3(0.04f, 0.04f, 0.16f), _spine, default, false);

            var legs = new Transform[4];
            Vector3[] legPos = { new Vector3(-0.36f, 0.38f, 0.35f), new Vector3(0.36f, 0.38f, 0.35f), new Vector3(-0.38f, 0.38f, -0.35f), new Vector3(0.38f, 0.38f, -0.35f) };
            string[] legNames = { "Leg FL", "Leg FR", "Leg BL", "Leg BR" };
            for (int i = 0; i < 4; i++)
            {
                legs[i] = K.Group(legNames[i], model, legPos[i], new Vector3(0f, 0f, legPos[i].x < 0f ? 30f : -30f));
                K.Box("Upper", legs[i], new Vector3(0f, -0.14f, 0f), new Vector3(0.12f, 0.3f, 0.12f), _chitinDark, default, false);
                K.Box("Foot", legs[i], new Vector3(0f, -0.3f, 0.06f), new Vector3(0.2f, 0.05f, 0.24f), _chitinDark, default, false);
            }
            Transform tail = K.Group("Tail", model, new Vector3(0f, 0.42f, -0.65f), Vector3.zero);
            K.Box("Tail Mesh", tail, new Vector3(0f, -0.05f, -0.3f), new Vector3(0.18f, 0.1f, 0.6f), _chitin, new Vector3(10f, 0f, 0f), false);
            K.Box("Tail Fin", tail, new Vector3(0f, 0.03f, -0.45f), new Vector3(0.02f, 0.2f, 0.35f), _spine, default, false);

            var agent = AddCommon(root, visual, def, new Vector3(0f, 0.55f, 0.1f), 0.8f, 1.2f, 0.4f, 0.9f, 0.6f);

            var vis = root.AddComponent<EnemyBeastVisual>();
            SetWarningMaterials(vis);
            K.SetRef(vis, "model", model);
            K.SetRef(vis, "head", head);
            K.SetRef(vis, "tail", tail);
            K.SetRef(vis, "tracerMaterial", _needleTrail);
            var so = new SerializedObject(vis);
            SerializedProperty lp = so.FindProperty("legs");
            lp.arraySize = 4;
            for (int i = 0; i < 4; i++) lp.GetArrayElementAtIndex(i).objectReferenceValue = legs[i];
            so.FindProperty("runSpeedReference").floatValue = def.runSpeed;
            so.FindProperty("strideLength").floatValue = 0.9f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(root, agent);
        }

        // ================================================================== Russluchs (grosse Katze)

        private static GameObject BuildLynxPrefab(EnemyDefinition def)
        {
            var root = new GameObject("Enemy_" + def.name.Replace("Enemy_", ""));
            float s = def.scale;
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 2; col.radius = 0.36f * s; col.height = 1.5f * s; col.center = new Vector3(0f, 0.7f * s, 0.05f);

            Transform visual = K.Group("Visual", root.transform);
            Transform model = K.Group("Model", visual);
            model.localScale = Vector3.one * s;

            K.Prim(PrimitiveType.Capsule, "Body", model, new Vector3(0f, 0.72f, -0.02f), new Vector3(0.42f, 0.62f, 0.42f), new Vector3(90f, 0f, 0f), _lynxFur, false);
            K.Prim(PrimitiveType.Sphere, "Shoulders", model, new Vector3(0f, 0.8f, 0.35f), new Vector3(0.46f, 0.46f, 0.46f), Vector3.zero, _lynxFur, false);
            K.Box("Ember Stripe A", model, new Vector3(0.21f, 0.8f, 0.05f), new Vector3(0.02f, 0.2f, 0.05f), _ember, default, false);
            K.Box("Ember Stripe B", model, new Vector3(-0.21f, 0.76f, -0.15f), new Vector3(0.02f, 0.18f, 0.05f), _ember, default, false);

            Transform head = K.Group("Head", model, new Vector3(0f, 0.95f, 0.6f), Vector3.zero);
            K.Prim(PrimitiveType.Sphere, "Skull", head, new Vector3(0f, 0.04f, 0.1f), new Vector3(0.34f, 0.3f, 0.34f), Vector3.zero, _lynxFur, false);
            K.Box("Muzzle", head, new Vector3(0f, -0.03f, 0.27f), new Vector3(0.16f, 0.12f, 0.14f), _lynxDark, default, false);
            K.Box("Ear L", head, new Vector3(-0.11f, 0.22f, 0.05f), new Vector3(0.06f, 0.16f, 0.04f), _lynxDark, new Vector3(0f, 0f, 10f), false);
            K.Box("Ear R", head, new Vector3(0.11f, 0.22f, 0.05f), new Vector3(0.06f, 0.16f, 0.04f), _lynxDark, new Vector3(0f, 0f, -10f), false);
            K.Box("Tuft L", head, new Vector3(-0.12f, 0.33f, 0.05f), new Vector3(0.02f, 0.08f, 0.02f), _lynxDark, default, false);
            K.Box("Tuft R", head, new Vector3(0.12f, 0.33f, 0.05f), new Vector3(0.02f, 0.08f, 0.02f), _lynxDark, default, false);
            K.Box("Eye L", head, new Vector3(-0.08f, 0.08f, 0.26f), new Vector3(0.07f, 0.03f, 0.02f), _ember, default, false);
            K.Box("Eye R", head, new Vector3(0.08f, 0.08f, 0.26f), new Vector3(0.07f, 0.03f, 0.02f), _ember, default, false);

            var legs = new Transform[4];
            Vector3[] legPos = { new Vector3(-0.14f, 0.68f, 0.4f), new Vector3(0.14f, 0.68f, 0.4f), new Vector3(-0.15f, 0.68f, -0.38f), new Vector3(0.15f, 0.68f, -0.38f) };
            string[] legNames = { "Leg FL", "Leg FR", "Leg BL", "Leg BR" };
            for (int i = 0; i < 4; i++)
            {
                legs[i] = K.Group(legNames[i], model, legPos[i], Vector3.zero);
                K.Prim(PrimitiveType.Capsule, "Upper", legs[i], new Vector3(0f, -0.22f, 0f), new Vector3(0.14f, 0.24f, 0.14f), Vector3.zero, _lynxFur, false);
                K.Prim(PrimitiveType.Capsule, "Lower", legs[i], new Vector3(0f, -0.5f, 0.02f), new Vector3(0.1f, 0.18f, 0.1f), Vector3.zero, _lynxDark, false);
                K.Box("Paw", legs[i], new Vector3(0f, -0.66f, 0.05f), new Vector3(0.14f, 0.06f, 0.16f), _lynxDark, default, false);
            }
            Transform tail = K.Group("Tail", model, new Vector3(0f, 0.8f, -0.62f), Vector3.zero);
            K.Prim(PrimitiveType.Capsule, "Tail Mesh", tail, new Vector3(0f, -0.05f, -0.1f), new Vector3(0.1f, 0.14f, 0.1f), new Vector3(-60f, 0f, 0f), _lynxDark, false);

            var agent = AddCommon(root, visual, def, new Vector3(0f, 0.85f * s, 0.1f), 1.0f, 1.6f * s, 0.45f, 1.1f * s, 0.9f * s);

            var vis = root.AddComponent<EnemyBeastVisual>();
            SetWarningMaterials(vis);
            K.SetRef(vis, "model", model);
            K.SetRef(vis, "head", head);
            K.SetRef(vis, "tail", tail);
            var so = new SerializedObject(vis);
            SerializedProperty lp = so.FindProperty("legs");
            lp.arraySize = 4;
            for (int i = 0; i < 4; i++) lp.GetArrayElementAtIndex(i).objectReferenceValue = legs[i];
            so.FindProperty("runSpeedReference").floatValue = def.runSpeed;
            so.FindProperty("strideLength").floatValue = 1.8f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(root, agent);
        }

        // ================================================================== Gemeinsame Teile

        /// <summary>Leben, Zielbarkeit, Trefferreaktion, KI, NavMeshAgent und Netzwerk.</summary>
        private static NavMeshAgent AddCommon(GameObject root, Transform visual, EnemyDefinition def, Vector3 aimOffset,
            float selectionRadius, float nameplateHeight, float agentRadius, float agentHeight, float eyeHeight)
        {
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = agentRadius;
            agent.height = agentHeight;
            agent.speed = def.walkSpeed;
            agent.angularSpeed = 540f;
            agent.acceleration = 24f;
            agent.stoppingDistance = 0.2f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.enabled = false; // der Server schaltet ihn ein, sobald das NavMesh steht

            var health = root.AddComponent<Health>();
            health.Configure(def.HealthAt(def.minLevel), false);
            var ct = root.AddComponent<CombatTarget>();
            ct.Configure(def.DisplayName, def.minLevel, Faction.Hostile, aimOffset, selectionRadius);
            var reaction = root.AddComponent<HitReaction>();
            K.SetRef(reaction, "visual", visual);

            var brain = root.AddComponent<EnemyBrain>();
            K.SetRef(brain, "definition", def);
            K.SetFloat(brain, "eyeHeight", eyeHeight);

            // Netzwerk: Position vom Server, Leben gespiegelt, Angriffe angekuendigt
            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.SetSynchronizeScale(false);
            var so = new SerializedObject(nt);
            SerializedProperty auth = so.FindProperty("_clientAuthoritative");
            if (auth != null) auth.boolValue = false;
            // CORE-005 (11.10.2026, Lasttest: Netzwerktakt ≈ 10 ms je Bild): offene Welt jeden 2. Takt senden (Zwischenwerte glaettet
            // der Client), Bosse, Eliten und Routen-Gegner weiter jeden Takt
            SerializedProperty iv = so.FindProperty("_interval");
            if (iv != null) iv.intValue = def.elite || def.balanceRole == BalanceRole.DungeonBoss || def.balanceRole == BalanceRole.Elite ? 1 : 2;
            so.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<NetworkHealth>();
            // Large-World N3: nur an Clients mit einer Figur in der Naehe senden
            InterestManagement.ApplyEnemy(root);

            var plateGo = new GameObject("Nameplate");
            plateGo.transform.SetParent(root.transform, false);
            plateGo.transform.localPosition = new Vector3(0f, nameplateHeight, 0f);
            var plate = plateGo.AddComponent<WorldLabel>();
            plate.Configure(def.DisplayName, "", WorldLabelStyle.Hostile, 28f);

            var ne = root.AddComponent<NetworkEnemy>();
            K.SetRef(ne, "nameplate", plate);

            // Milestone 3.4: Beteiligung zaehlen, persoenliche Beute, Glimmen fuer den Pluenderer
            root.AddComponent<Ashveil.Gameplay.Items.LootEligibility>();
            root.AddComponent<Ashveil.Gameplay.Items.CorpseLoot>();
            var glimmer = root.AddComponent<Ashveil.Gameplay.Items.LootIndicator>();
            K.SetRef(glimmer, "moteMaterial", _ember);
            return agent;
        }

        private static GameObject SavePrefab(GameObject root, NavMeshAgent agent)
        {
            K.MakeDynamic(root);
            K.SetLayerRecursively(root, LayerMask.NameToLayer(GameLayers.Enemy));
            string path = PrefabFolder + "/" + root.name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            var nob = prefab.GetComponent<NetworkObject>();
            if (nob != null)
            {
                nob.SetSceneId(NetworkObject.UNSET_SCENEID_VALUE);
                EditorUtility.SetDirty(nob);
            }
            EditorUtility.SetDirty(prefab);
            return prefab;
        }

        private static EnemySpawnArea Area(Transform parent, string name, Vector3 pos, EnemyDefinition def, GameObject prefab, int count, float radius, string group,
            int minLevel = 0, int maxLevel = 0)
        {
            Transform t = K.Group(name, parent, pos, Vector3.zero);
            var area = t.gameObject.AddComponent<EnemySpawnArea>();
            K.SetRef(area, "definition", def);
            K.SetRef(area, "prefab", prefab);
            K.SetInt(area, "count", count);
            K.SetFloat(area, "radius", radius);
            K.SetString(area, "groupName", group);
            K.SetInt(area, "minLevel", minLevel);
            K.SetInt(area, "maxLevel", maxLevel);
            return area;
        }

        private static EnemySpawnArea SetAggro(EnemySpawnArea area, AggroProfile profile)
        {
            K.SetBool(area, "overrideAggro", true);
            K.SetInt(area, "aggroProfile", (int)profile);
            return area;
        }

        private static void SetAlternatePoints(EnemySpawnArea area, params Vector3[] points)
        {
            var so = new SerializedObject(area);
            SerializedProperty p = so.FindProperty("alternatePoints");
            p.arraySize = points.Length;
            for (int i = 0; i < points.Length; i++) p.GetArrayElementAtIndex(i).vector3Value = points[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Rundweg des Glockenhirten durch das Aschenfeld (~600 m) mit drei Huete-Plaetzen.</summary>
        private static EnemyRoute BuildShepherdRoute(Transform env)
        {
            Vector3[] pts =
            {
                Ground(297f, -59f), Ground(340f, -122f), Ground(413f, -132f), Ground(433f, -46f),
                Ground(423f, 40f), Ground(373f, 93f), Ground(314f, 73f), Ground(287f, 7f)
            };
            var go = new GameObject("Route des Glockenhirten");
            go.transform.SetParent(env, false);
            var route = go.AddComponent<EnemyRoute>();
            route.SetPoints(pts, new[] { 1, 4, 6 }, 14f);
            EditorUtility.SetDirty(route);
            // Huete-Plaetze: Kreis aus Steinen, an dem die Verblichenen knien wuerden
            foreach (int i in new[] { 1, 4, 6 })
            {
                Transform ring = K.Group("Hueteplatz", go.transform, pts[i], Vector3.zero);
                for (int k = 0; k < 7; k++)
                {
                    float a = k * Mathf.PI * 2f / 7f;
                    K.Block("Stein", ring, new Vector3(Mathf.Sin(a) * 9f, -0.3f, Mathf.Cos(a) * 9f), new Vector3(1.2f, 1.1f, 1f), _stone, k * 37f);
                }
            }
            Debug.Log($"[Ashveil] Route des Glockenhirten: {route.Length:0} m, {pts.Length} Wegpunkte.");
            return route;
        }

        // ================================================================== Lager der Verblichenen

        private static void BuildAshCamp(Transform env)
        {
            Transform camp = K.Group("Lager der Verblichenen", env, AshCamp, new Vector3(0f, 20f, 0f));
            // Eingestuerzte Mauerreste eines Gehoefts
            K.Block("Wall A", camp, new Vector3(-6f, 0f, 5f), new Vector3(7f, 2.6f, 0.6f), _stone, 0f);
            K.Block("Wall A Broken", camp, new Vector3(-1.2f, 0f, 5f), new Vector3(1.8f, 1.1f, 0.6f), _stone, 0f);
            K.Block("Wall B", camp, new Vector3(-9.4f, 0f, 0.5f), new Vector3(0.6f, 2.1f, 8f), _stone, 0f);
            K.Block("Wall C Stub", camp, new Vector3(5f, 0f, -6f), new Vector3(4f, 1.4f, 0.6f), _stone, 12f);
            K.Box("Fallen Beam", camp, new Vector3(-3f, 0.3f, 3.2f), new Vector3(0.3f, 0.3f, 4.5f), _charred, new Vector3(0f, 60f, 8f));
            // Verbranntes Zelt und Karren
            K.Box("Burnt Tent L", camp, new Vector3(4.2f, 0.9f, 3f), new Vector3(0.08f, 2.2f, 3f), _charred, new Vector3(0f, 0f, -38f));
            K.Box("Burnt Tent R", camp, new Vector3(5.6f, 0.9f, 3f), new Vector3(0.08f, 2.2f, 3f), _charred, new Vector3(0f, 0f, 38f));
            K.Block("Cart", camp, new Vector3(-5f, 0.35f, -4f), new Vector3(1.6f, 0.5f, 2.6f), _darkWood, 25f);
            K.Prim(PrimitiveType.Cylinder, "Wheel", camp, new Vector3(-4.2f, 0.45f, -3.6f), new Vector3(0.9f, 0.06f, 0.9f), new Vector3(0f, 25f, 90f), _darkWood);
            // Glutgrube – die Verblichenen sammeln sich am Rest der Waerme
            K.Prim(PrimitiveType.Cylinder, "Ember Pit", camp, new Vector3(0f, 0.08f, 0f), new Vector3(1.6f, 0.08f, 1.6f), Vector3.zero, _charred);
            K.Prim(PrimitiveType.Sphere, "Embers", camp, new Vector3(0f, 0.12f, 0f), new Vector3(1.1f, 0.12f, 1.1f), Vector3.zero, _ember, false);
            K.PointLight("Ember Light", camp, new Vector3(0f, 0.8f, 0f), new Color(1f, 0.45f, 0.15f), 9f, 2.2f, true);
            K.Particles("Ash Smoke", camp, new Vector3(0f, 0.3f, 0f), new Color(0.3f, 0.28f, 0.26f, 0.5f), 0.9f, 5f, 0.8f, 5f, 12f, 0.5f);
            K.Label(camp, new Vector3(0f, 3.5f, 0f), "Verlassenes Gehoeft", "Lager der Verblichenen", WorldLabelStyle.Place, 60f);
        }

        // ================================================================== NavMesh

        private static void BuildNavMeshSurface(Transform env)
        {
            var go = new GameObject("NavMesh Kareth (im Editor gebacken)");
            go.transform.SetParent(env, false);
            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            // 28.09.: bis x = 662, damit der Grenztunnel (Welt-Ebene, bis x = 658) ganz im Kareth-NavMesh liegt –
            // NavMeshSurface sammelt im Editor nur Objekte der eigenen Szene, die Zelle gg_west sieht den Tunnel nicht
            surface.center = KarethNavMeshCenter;
            surface.size = KarethNavMeshSize;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            int exclude = LayerMask.GetMask(GameLayers.Player, GameLayers.Enemy, GameLayers.Interactable, "Ignore Raycast", "UI");
            surface.layerMask = ~exclude;
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.25f;
            surface.overrideTileSize = true;
            surface.tileSize = 256;
            BakedSurface = surface;
        }

        public static readonly Vector3 KarethNavMeshCenter = new Vector3(31f, 45f, 250f);
        public static readonly Vector3 KarethNavMeshSize = new Vector3(1262f, 180f, KarethTerrainBuilder.Size);

        /// <summary>Flaeche, die nach dem Aufbau der ganzen Szene gebacken wird.</summary>
        public static NavMeshSurface BakedSurface { get; private set; }

        /// <summary>
        /// NavMesh im Editor backen und als Asset speichern (1,2 km zur Laufzeit zu bauen wuerde den Serverstart
        /// um viele Sekunden verzoegern). Aufruf, wenn die ganze Szene steht.
        /// </summary>
        /// <summary>NavMesh einer bestehenden Flaeche neu backen (z. B. nach dem Austauschen von Modellen).</summary>
        public static void BakeNavMesh(NavMeshSurface surface, string assetPath)
        {
            BakedSurface = surface;
            BakeNavMesh(assetPath);
        }

        public static void BakeNavMesh(string assetPath)
        {
            if (BakedSurface == null) return;
            float t0 = Time.realtimeSinceStartup;
            AssetDatabase.DeleteAsset(assetPath);
            BakedSurface.BuildNavMesh();
            if (BakedSurface.navMeshData != null)
            {
                AssetDatabase.CreateAsset(BakedSurface.navMeshData, assetPath);
                EditorUtility.SetDirty(BakedSurface);
            }
            Debug.Log($"[Ashveil] NavMesh Kareth gebacken ({(Time.realtimeSinceStartup - t0):0.0} s) -> {assetPath}");
        }
    }
}
