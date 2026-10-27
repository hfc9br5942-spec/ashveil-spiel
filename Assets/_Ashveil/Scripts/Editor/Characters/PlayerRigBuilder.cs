using System.Linq;
using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Targeting;
using Ashveil.Gameplay.Weapons;
using Ashveil.World;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Milestone 2: Kampf- und Animationsausstattung fuer Spieler und Trainingspuppen.
    /// Wird vom Szenenaufbau (<see cref="SandboxSceneBuilder"/>) aufgerufen.
    /// </summary>
    public static class PlayerRigBuilder
    {
        public const string WeaponFolder = ProjectSetup.Root + "/Data/Weapons";
        public const string SwordPath = WeaponFolder + "/Weapon_RustyLongsword.asset";
        public const string PistolPath = WeaponFolder + "/Weapon_WheellockPistol.asset";
        private const string TracerMaterialPath = ProjectSetup.GreyboxMaterials + "/FX_Tracer.mat";
        public const string StanceFolder = ProjectSetup.Root + "/Data/Stances";
        public const string MaleStancePath = StanceFolder + "/Stance_Male.asset";
        public const string FemaleStancePath = StanceFolder + "/Stance_Female.asset";

        // ---------------------------------------------------------------- Waffen-Assets

        /// <summary>Legt die zwei Start-Waffen an (nur beim ersten Mal – spaetere Aenderungen bleiben erhalten).</summary>
        public static (WeaponDefinition sword, WeaponDefinition pistol) EnsureWeapons()
        {
            ProjectSetup.EnsureFolder(WeaponFolder);

            WeaponDefinition sword = LoadOrCreate(SwordPath, "weapon.rusty_longsword", "Rostiges Langschwert", w =>
            {
                w.weaponClass = WeaponClass.Sword;
                w.archetype = WeaponArchetype.MeleeArc;
                w.quality = ItemQuality.Common;
                w.minDamage = 9f;
                w.maxDamage = 13f;
                w.critChance = 0.06f;
                w.range = 2.4f;
                w.combo = new[]
                {
                    AttackStep.Create("Hieb", 1f, 0.30f, 0.08f, 0.12f, 110f, 0.035f, 0f, 0.1f),
                    AttackStep.Create("Rueckhand", 1f, 0.28f, 0.08f, 0.14f, 120f, 0.035f, 0f, 0.1f),
                    AttackStep.Create("Schwerer Hieb", 1.5f, 0.40f, 0.10f, 0.25f, 140f, 0.07f, 0.25f, 0.4f)
                };
                w.comboResetTime = 1.2f;
                w.specialName = "Wuchtschlag";
                w.special = AttackStep.Create("Wuchtschlag", 2.3f, 0.45f, 0.10f, 0.30f, 160f, 0.09f, 0.4f, 0.7f);   // 21.10.: sichtbares Ausholen, gleicher Zyklus
                w.specialCooldown = 6f;
                w.resource = WeaponResource.None;
                w.effectColor = new Color(0.95f, 0.9f, 0.8f);
                w.animationSet = "onehand_sword";
                w.hasAirAttacks = true;
            });

            WeaponDefinition pistol = LoadOrCreate(PistolPath, "weapon.wheellock_pistol", "Radschlosspistole", w =>
            {
                w.weaponClass = WeaponClass.Pistol;
                w.archetype = WeaponArchetype.Hitscan;
                w.quality = ItemQuality.Uncommon;
                w.minDamage = 18f;
                w.maxDamage = 24f;
                w.critChance = 0.1f;
                w.critMultiplier = 1.8f;
                w.range = 30f;
                w.combo = new[] { AttackStep.Create("Schuss", 1f, 0.05f, 0.05f, 0.90f, 10f, 0.03f, 0.12f, 0.15f) };
                w.specialName = "Gezielter Schuss";
                w.special = AttackStep.Create("Gezielter Schuss", 2.6f, 0.60f, 0.05f, 0.45f, 10f, 0.06f, 0.3f, 0.5f);
                w.specialCooldown = 8f;
                w.resource = WeaponResource.Ammo;
                w.magazineSize = 6;
                w.reloadTime = 2.2f;
                w.effectColor = new Color(1f, 0.62f, 0.25f);
                w.animationSet = "pistol";
            });

            // Bestehende Schwerter bekommen die Luftangriffe einmalig nachgeruestet
            // (spaeteres Abschalten im Inspector bleibt erhalten).
            const string migrationKey = "Ashveil.Migration.SwordAirAttacks";
            if (!EditorPrefs.GetBool(migrationKey, false))
            {
                EditorPrefs.SetBool(migrationKey, true);
                sword.hasAirAttacks = true;
                sword.airAttackName = "Luftschlag";
                sword.plungeName = "Sturzangriff";
                EditorUtility.SetDirty(sword);
            }

            return (sword, pistol);
        }

        private static WeaponDefinition LoadOrCreate(string path, string id, string displayName, System.Action<WeaponDefinition> setup)
        {
            var w = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (w != null) return w;

            w = ScriptableObject.CreateInstance<WeaponDefinition>();
            setup(w);
            AssetDatabase.CreateAsset(w, path);
            K.SetString(w, "id", id);
            K.SetString(w, "displayName", displayName);
            EditorUtility.SetDirty(w);
            return w;
        }

        // ---------------------------------------------------------------- Spieler

        /// <summary>
        /// Fuegt dem Spieler Leben, Zielwahl, Waffen, Waffenmodelle und (falls importiert) die
        /// animierten Koerpermodelle hinzu.
        /// </summary>
        public static void Equip(GameObject root, Transform visual, Transform placeholder,
            Material metal, Material wood, Material glow)
        {
            (WeaponDefinition sword, WeaponDefinition pistol) = EnsureWeapons();

            var health = root.AddComponent<Health>();
            health.Configure(100f, false);

            var weapons = root.AddComponent<WeaponUser>(); // bringt TargetingSystem mit
            K.SetRef(weapons, "meleeWeapon", sword);
            // Feuerwaffen ruhen: Fernkampf kommt nur noch aus dem Fernkampfplatz der Ausruestung (Bogen)
            K.SetRef(weapons, "rangedWeapon", (Object)null);

            var selection = root.AddComponent<SelectionIndicator>();
            K.SetRef(selection, "targeting", root.GetComponent<TargetingSystem>());

            // Platzhalter-Waffen (Griff = Drehpunkt, Klinge/Lauf zeigt nach +Z)
            Transform swordPivot = K.Group("Sword", visual, new Vector3(0.38f, 1.0f, 0.2f), new Vector3(20f, 0f, 0f));
            K.Box("Grip", swordPivot, new Vector3(0f, 0f, -0.04f), new Vector3(0.035f, 0.035f, 0.16f), wood, default, false);
            K.Box("Pommel", swordPivot, new Vector3(0f, 0f, -0.13f), new Vector3(0.05f, 0.05f, 0.05f), metal, default, false);
            K.Box("Guard", swordPivot, new Vector3(0f, 0f, 0.05f), new Vector3(0.22f, 0.035f, 0.04f), metal, default, false);
            K.Box("Blade", swordPivot, new Vector3(0f, 0f, 0.5f), new Vector3(0.055f, 0.012f, 0.85f), metal, default, false);

            Transform pistolPivot = K.Group("Pistol", visual, new Vector3(0.3f, 1.2f, 0.35f), Vector3.zero);
            K.Box("Barrel", pistolPivot, new Vector3(0f, 0.02f, 0.14f), new Vector3(0.045f, 0.05f, 0.32f), metal, default, false);
            K.Box("Lock", pistolPivot, new Vector3(0f, 0.015f, -0.03f), new Vector3(0.07f, 0.08f, 0.12f), metal, default, false);
            K.Prim(PrimitiveType.Cylinder, "Wheel", pistolPivot, new Vector3(0.04f, 0.015f, -0.03f), new Vector3(0.05f, 0.005f, 0.05f), new Vector3(0f, 0f, 90f), glow, false);
            K.Box("Grip", pistolPivot, new Vector3(0f, -0.07f, -0.08f), new Vector3(0.045f, 0.13f, 0.06f), wood, new Vector3(-15f, 0f, 0f), false);
            Transform muzzle = K.Group("Muzzle", pistolPivot, new Vector3(0f, 0.02f, 0.31f), Vector3.zero);

            var fx = root.AddComponent<WeaponFx>();
            K.SetRef(fx, "user", weapons);
            K.SetRef(fx, "meleePivot", swordPivot);
            K.SetRef(fx, "rangedPivot", pistolPivot);
            K.SetRef(fx, "muzzlePoint", muzzle);
            K.SetRef(fx, "tracerMaterial", TracerMaterial());
            K.SetRef(fx, "flashMaterial", glow);

            AddBodies(root, visual, placeholder, fx);
        }

        internal static Material TracerMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(TracerMaterialPath);
            if (mat != null) return mat;
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;
            mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, TracerMaterialPath);
            return mat;
        }

        /// <summary>
        /// Maennliches und weibliches Modell: bevorzugt die texturierten Universal Base Characters,
        /// sonst die grauen Mannequins der Animationsbibliothek.
        /// </summary>
        private static void AddBodies(GameObject root, Transform visual, Transform placeholder, WeaponFx fx)
        {
            var switcher = root.AddComponent<CharacterBodySwitcher>();
            K.SetRef(switcher, "placeholderBody", placeholder.gameObject);
            K.SetRef(switcher, "bridge", root.GetComponent<CharacterAnimatorBridge>());
            K.SetRef(switcher, "weaponFx", fx);
            K.SetRef(switcher, "placeholderMotion", root.GetComponent<PlaceholderBodyMotion>());

            if (!QuaterniusImport.IsImported) return;
            // Immer neu erzeugen, damit neue Zustaende (z. B. Rolle, Luftangriffe) sicher enthalten sind.
            PlayerAnimatorBuilder.Build();

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.ControllerPath);
            var femaleController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.FemaleOverridePath);

            // Art-Pass 3: neue Basiskoerper, falls eingeschaltet (Ashveil › Figuren), sonst Quaternius
            string malePath = CharacterBodies.MalePath;
            string femalePath = CharacterBodies.FemalePath;

            GameObject male = InstantiateModel(malePath, "Model_Male", visual, controller);
            GameObject female = InstantiateModel(femalePath, "Model_Female", visual,
                femaleController != null ? femaleController : controller);

            // Eigene Koerperhaltung im Stand fuer Mann und Frau
            if (male != null) AddStance(male, EnsureStance(MaleStancePath, false));
            if (female != null) AddStance(female, EnsureStance(FemaleStancePath, true));

            var so = new SerializedObject(switcher);
            SerializedProperty bodies = so.FindProperty("bodies");
            int count = (male != null ? 1 : 0) + (female != null ? 1 : 0);
            bodies.arraySize = count;
            int idx = 0;
            if (male != null) SetBody(bodies.GetArrayElementAtIndex(idx++), "Maennlich", male);
            if (female != null) SetBody(bodies.GetArrayElementAtIndex(idx), "Weiblich", female);
            so.FindProperty("activeIndex").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Haltungsprofil laden oder anlegen. Eigene Anpassungen im Inspector bleiben erhalten –
        /// nur wenn die mitgelieferten Standardwerte neuer sind, werden sie ersetzt.
        /// </summary>
        public static StanceProfile EnsureStance(string path, bool female)
        {
            ProjectSetup.EnsureFolder(StanceFolder);
            var profile = AssetDatabase.LoadAssetAtPath<StanceProfile>(path);
            bool created = profile == null;
            if (created)
            {
                profile = ScriptableObject.CreateInstance<StanceProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            if (created || profile.defaultsVersion < StanceProfile.CurrentDefaultsVersion)
            {
                if (female) profile.ApplyFemaleDefaults();
                else profile.ApplyMaleDefaults();
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            return profile;
        }

        internal static void AddStance(GameObject model, StanceProfile profile)
        {
            var poser = model.GetComponent<StancePoser>();
            if (poser == null) poser = model.AddComponent<StancePoser>();
            K.SetRef(poser, "profile", profile);
            K.SetRef(poser, "animator", model.GetComponent<Animator>());
            // M8: Emotes (Winken, Sitzen, Tanzen …) – laeuft nach der Haltung
            var emote = model.GetComponent<EmotePoser>();
            if (emote == null) emote = model.AddComponent<EmotePoser>();
            K.SetRef(emote, "animator", model.GetComponent<Animator>());
        }

        /// <summary>
        /// 21.10. (Tripo-Körper ins Spiel): nur die Körpermodelle eines fertigen Spieler-Prefabs gegen die aktuellen
        /// (CharacterBodies.MalePath/FemalePath) tauschen – Waffen, Netz, Komponenten und Szenen bleiben, wie sie sind.
        /// Gibt eine Kurzmeldung zurück; null = kein Körper-Umschalter im Prefab.
        /// </summary>
        public static string KoerperErneuern(GameObject root)
        {
            var switcher = root.GetComponent<CharacterBodySwitcher>();
            if (switcher == null) return null;
            var so = new SerializedObject(switcher);
            SerializedProperty bodies = so.FindProperty("bodies");
            Transform visual = null;
            for (int i = 0; i < bodies.arraySize; i++)
            {
                var alt = bodies.GetArrayElementAtIndex(i).FindPropertyRelative("model").objectReferenceValue as GameObject;
                if (alt == null) continue;
                if (visual == null) visual = alt.transform.parent;
                Object.DestroyImmediate(alt);
            }
            if (visual == null) return null;
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.ControllerPath);
            var femaleController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerAnimatorBuilder.FemaleOverridePath);
            GameObject male = InstantiateModel(CharacterBodies.MalePath, "Model_Male", visual, controller);
            GameObject female = InstantiateModel(CharacterBodies.FemalePath, "Model_Female", visual, femaleController != null ? femaleController : controller);
            if (male != null) AddStance(male, EnsureStance(MaleStancePath, false));
            if (female != null) AddStance(female, EnsureStance(FemaleStancePath, true));
            bodies.arraySize = (male != null ? 1 : 0) + (female != null ? 1 : 0);
            int idx = 0;
            if (male != null) SetBody(bodies.GetArrayElementAtIndex(idx++), "Maennlich", male);
            if (female != null) SetBody(bodies.GetArrayElementAtIndex(idx), "Weiblich", female);
            so.FindProperty("activeIndex").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            return $"Mann {System.IO.Path.GetFileNameWithoutExtension(CharacterBodies.MalePath)}, Frau {System.IO.Path.GetFileNameWithoutExtension(CharacterBodies.FemalePath)}";
        }

        private static void SetBody(SerializedProperty element, string name, GameObject model)
        {
            element.FindPropertyRelative("name").stringValue = name;
            element.FindPropertyRelative("model").objectReferenceValue = model;
        }

        internal static GameObject InstantiateModel(string fbxPath, string name, Transform parent, RuntimeAnimatorController controller)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (source == null) return null;

            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            model.name = name;
            model.transform.SetParent(parent, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            if (anim.avatar == null)
                anim.avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                r.updateWhenOffscreen = false;
            return model;
        }

        // ---------------------------------------------------------------- Trainingspuppe

        /// <summary>Macht eine Greybox-Puppe angreifbar. Die Teile muessen unter 'visual' liegen.</summary>
        public static void SetupDummy(Transform dummy, Transform visual, int level)
        {
            GameObject go = dummy.gameObject;
            var health = go.AddComponent<Health>();
            health.Configure(400f, true);
            var target = go.AddComponent<CombatTarget>();
            target.Configure("Trainingspuppe", level, Faction.Neutral, new Vector3(0f, 1.35f, 0f), 0.55f);
            go.AddComponent<TrainingDummy>();
            var reaction = go.AddComponent<HitReaction>();
            K.SetRef(reaction, "visual", visual);
            K.SetLayerRecursively(go, LayerMask.NameToLayer(GameLayers.Enemy));
            K.MakeDynamic(go);

            K.Label(dummy, new Vector3(0f, 2.35f, 0f), "Trainingspuppe", "Stufe " + level, WorldLabelStyle.Neutral, 35f, 0.9f);
        }
    }
}
