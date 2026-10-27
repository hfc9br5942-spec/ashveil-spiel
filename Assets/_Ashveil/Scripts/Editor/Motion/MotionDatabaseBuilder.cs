using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Data.Motion;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Legt die Motion-Daten an (Phase M1): Profile je Schicht, Clip-Metadaten fuer die vorhandenen Quaternius-Clips,
    /// den Schwert+Schild-Angriffssatz und Eintraege fuer fehlende Clips (Status „Missing“, §114).
    /// Idempotent: vorhandene Assets werden nicht ueberschrieben (Handarbeit im Inspector bleibt), nur ergaenzt.
    /// Bericht: Logs/Ashveil/Motion-Bestand.txt.
    /// </summary>
    public static class MotionDatabaseBuilder
    {
        public const string DataDir = ProjectSetup.Root + "/Data/Motion";
        public const string ClipDir = DataDir + "/Clips";
        public const string AttackDir = DataDir + "/Attacks";
        public const string ResourcesDir = ProjectSetup.Root + "/Resources/Motion";
        public const string DatabasePath = ResourcesDir + "/MotionDatabase.asset";

        [MenuItem("Ashveil/Motion/Datenbank aktualisieren", priority = 330)]
        public static void BuildMenu()
        {
            string report = Build();
            Debug.Log("[Ashveil] Motion-Datenbank:\n" + report);
            EditorUtility.DisplayDialog("Ashveil – Motion", report.Length > 1200 ? report.Substring(0, 1200) + " …\n(Bericht: Logs/Ashveil/Motion-Bestand.txt)" : report, "OK");
        }

        public static string Build()
        {
            foreach (string d in new[] { DataDir, ClipDir, AttackDir, ResourcesDir }) ProjectSetup.EnsureFolder(d);
            Dictionary<string, AnimationClip> clips = PlayerAnimatorBuilder.LoadClips();

            MotionDatabase db = LoadOrCreate<MotionDatabase>(DatabasePath, out _);

            // ---------------- Schichten
            db.bodies = new List<BodyMotionProfile>
            {
                Body("Masculine", BodyMotionVariant.Masculine, "Etwas laengerer Schritt, breitere Schulterfuehrung, ruhigeres Becken. Keine Karikatur.",
                    stride: 1.03f, arm: 1.0f, shoulderPelvis: 2f, pelvis: 0.05f),
                Body("Feminine", BodyMotionVariant.Feminine, "Etwas kuerzerer Schritt, etwas mehr Beckenrhythmus, engere Armfuehrung. Keine Karikatur.",
                    stride: 0.97f, arm: 0.9f, shoulderPelvis: -1f, pelvis: 0f),
                Body("Neutral", BodyMotionVariant.Neutral, "Neutral – Grundlage fuer eigene Varianten.", 1f, 1f, 0f, 0f)
            };
            db.races = new List<RaceMotionProfile>
            {
                Race("Human", RaceCatalog.HumanId, false, "Ausgewogen, militaerisch nachvollziehbar, natuerlich (§20)."),
                Race("Nharim", "race.nharim", true, "Vorbereitet, noch nicht entwickelt: darf unheimlich kontrolliert wirken, nie schwebend (§20).")
            };
            db.classes = new List<ClassMotionProfile>
            {
                Class("Tank", ClassRole.Tank, 0.8f, 8f, "Geerdet, schuetzend, stabil, Gewicht, Oekonomie der Bewegung (§72).",
                    upper: 0.8f, turn: 1.25f, pelvis: 0.25f, transition: 0.9f),
                Class("Damage", ClassRole.Damage, 0.3f, 5f, "Leichter, praeziser, schnellere Uebergaenge (§72).",
                    upper: 1.05f, turn: 0.85f, pelvis: 0f, transition: 1.2f),
                Class("Healer", ClassRole.Healer, 0.5f, 6f, "Kontrolliert, ausgewogen, weniger aggressive Haltung (§72).",
                    upper: 0.95f, turn: 1f, pelvis: 0.1f, transition: 1f)
            };
            db.armors = new List<ArmorMotionProfile>
            {
                Armor("Light", ArmorCategory.Light, "Freie Armschwingung, flinke Gewichtsverlagerung.", arm: 1.05f, inertia: 0.85f, landing: 0.85f, breathing: 1f),
                Armor("Medium", ArmorCategory.Medium, "Mittelweg.", arm: 0.95f, inertia: 1f, landing: 1f, breathing: 1f),
                Armor("Heavy", ArmorCategory.Heavy, "Kuerzere freie Armschwingung, deutliche Masseuebertragung, kraeftige Bodenkontakte, laengeres Bremsen (§10/§21).",
                    arm: 0.75f, inertia: 1.35f, landing: 1.4f, breathing: 1.15f)
            };

            // ---------------- Clips
            var metas = new List<MotionClipMetadata>();
            MotionClipMetadata M(string id, string clip, MotionCategory cat, MotionTag tags, WeaponFamily fam, MotionStance st, string note = "",
                bool loop = false, Vector2 speed = default, MotionAssetStatus? status = null, PoseTag start = PoseTag.Any, PoseTag end = PoseTag.Any,
                float contact = -1f, MotionDirection dir = MotionDirection.Any, MotionPriority prio = MotionPriority.Idle)
            {
                AnimationClip c = clip != null && clips.TryGetValue(clip, out AnimationClip found) ? found : null;
                MotionAssetStatus s = status ?? (c != null ? MotionAssetStatus.Retargeted : MotionAssetStatus.Missing);
                if (c == null && s != MotionAssetStatus.Missing) s = MotionAssetStatus.Missing;
                MotionClipMetadata m = Meta(id, c, clip != null ? "Quaternius: " + clip : "", s, note, cat, tags, fam, st, loop, speed, start, end, contact, dir, prio);
                metas.Add(m);
                return m;
            }
            const WeaponFamily SS = WeaponFamily.SwordShield;
            const MotionStance EX = MotionStance.Exploration, CB = MotionStance.Combat;

            // Fortbewegung (vorhanden)
            M("loco.idle", "Idle_Loop", MotionCategory.Idle, MotionTag.Loop | MotionTag.Relaxed, WeaponFamily.None, EX, loop: true);
            M("loco.walk", "Walk_Loop", MotionCategory.Locomotion, MotionTag.Locomotion | MotionTag.Forward | MotionTag.Loop, WeaponFamily.None, EX, loop: true, speed: new Vector2(1.6f, 3.2f));
            M("loco.walk_formal", "Walk_Formal_Loop", MotionCategory.Locomotion, MotionTag.Locomotion | MotionTag.Forward | MotionTag.Loop, WeaponFamily.None, EX,
                "Heute Gehen der weiblichen Figur; Kandidat fuer Epic Walk (leicht).", loop: true, speed: new Vector2(1.4f, 2.8f));
            M("loco.jog", "Jog_Fwd_Loop", MotionCategory.Locomotion, MotionTag.Locomotion | MotionTag.Forward | MotionTag.Loop, WeaponFamily.None, EX,
                "Heute auch als Rennen (7 m/s) mit Uebertempo genutzt – ab M2 nur noch Jog.", loop: true, speed: new Vector2(3.2f, 5f));
            M("loco.sprint", "Sprint_Loop", MotionCategory.Locomotion, MotionTag.Locomotion | MotionTag.Forward | MotionTag.Loop, WeaponFamily.None, EX, loop: true, speed: new Vector2(8.5f, 11f));
            M("jump.start", "Jump_Start", MotionCategory.Jump, MotionTag.None, WeaponFamily.None, EX);
            M("jump.loop", "Jump_Loop", MotionCategory.Jump, MotionTag.Loop, WeaponFamily.None, EX, loop: true);
            M("land.normal", "Jump_Land", MotionCategory.Landing, MotionTag.None, WeaponFamily.None, EX);
            M("land.heavy", "NinjaJump_Land", MotionCategory.Landing, MotionTag.Heavy, WeaponFamily.None, EX);
            M("traversal.climb_1m", "ClimbUp_1m", MotionCategory.Traversal, MotionTag.Traversal, WeaponFamily.None, EX, "Fuer spaeteres Mantle-System (§48).");

            // Schwert + Schild (vorhanden)
            M("ss.idle.combat", "Idle_Shield_Loop", MotionCategory.Idle, MotionTag.Combat | MotionTag.Shield | MotionTag.Loop | MotionTag.Ready, SS, CB, loop: true);
            M("ss.idle.sword", "Sword_Idle", MotionCategory.Idle, MotionTag.Combat | MotionTag.Loop | MotionTag.Ready, SS, CB, "Heute Halte-Ebene der Arme.", loop: true);
            M("ss.block.idle", "Sword_Block", MotionCategory.Block, MotionTag.Combat | MotionTag.Shield, SS, CB, prio: MotionPriority.Block);
            M("ss.block.heavy", "Idle_Shield_Break", MotionCategory.Block, MotionTag.Combat | MotionTag.Shield | MotionTag.Heavy, SS, CB,
                "Als schwerer Block-Einschlag nutzbar (Schild wird weggedrueckt).", prio: MotionPriority.Block);
            M("ss.dash", "Shield_Dash", MotionCategory.Attack, MotionTag.Combat | MotionTag.Shield | MotionTag.Forward, SS, CB, "Schildansturm – Kandidat fuer Tank-Faehigkeit.", prio: MotionPriority.Ability);

            // Reaktionen / Tod (vorhanden)
            M("hit.chest", "Hit_Chest", MotionCategory.HitReaction, MotionTag.HitFront | MotionTag.UpperBodyOnly, WeaponFamily.None, CB, prio: MotionPriority.MinorReaction);
            M("hit.head", "Hit_Head", MotionCategory.HitReaction, MotionTag.HitFront | MotionTag.HighTarget | MotionTag.UpperBodyOnly, WeaponFamily.None, CB, prio: MotionPriority.MinorReaction);
            M("hit.knockback", "Hit_Knockback", MotionCategory.HitReaction, MotionTag.HitFront | MotionTag.Heavy, WeaponFamily.None, CB, prio: MotionPriority.HardControl);
            M("death.front", "Death01", MotionCategory.Death, MotionTag.Death | MotionTag.HitFront, WeaponFamily.None, CB, prio: MotionPriority.Death);

            // Angriffe Schwert + Schild (Clips vorhanden bzw. Platzhalter)
            MotionClipMetadata aDiag = M("ss.attack.diag_r", "Sword_Regular_A", MotionCategory.Attack, MotionTag.Attack | MotionTag.Combat | MotionTag.RightLead, SS, CB,
                start: PoseTag.GuardCenter, end: PoseTag.GuardLeft, contact: 0.45f, prio: MotionPriority.Ability);
            MotionClipMetadata aBack = M("ss.attack.back_l", "Sword_Regular_B", MotionCategory.Attack, MotionTag.Attack | MotionTag.Combat | MotionTag.LeftLead, SS, CB,
                start: PoseTag.GuardLeft, end: PoseTag.GuardRight, contact: 0.45f, prio: MotionPriority.Ability);
            MotionClipMetadata aHigh = M("ss.attack.high", "Sword_Regular_C", MotionCategory.Attack, MotionTag.Attack | MotionTag.Combat | MotionTag.HighTarget | MotionTag.Vertical, SS, CB,
                start: PoseTag.GuardRight, end: PoseTag.GuardCenter, contact: 0.5f, prio: MotionPriority.Ability);
            MotionClipMetadata aBash = M("ss.attack.shield_bash", "Shield_OneShot", MotionCategory.Attack, MotionTag.Attack | MotionTag.Combat | MotionTag.Shield | MotionTag.ShortRange, SS, CB,
                start: PoseTag.Any, end: PoseTag.GuardCenter, contact: 0.4f, prio: MotionPriority.Ability);
            MotionClipMetadata aLow = M("ss.attack.low_cut", "Sword_Regular_B", MotionCategory.Attack, MotionTag.Attack | MotionTag.Combat | MotionTag.LowTarget, SS, CB,
                "PLATZHALTER: Rueckhand-Clip statt echtem tiefem Schnitt – Mixamo-Clip noetig.", status: MotionAssetStatus.Placeholder,
                start: PoseTag.GuardCenter, end: PoseTag.WeaponLow, contact: 0.45f, prio: MotionPriority.Ability);
            MotionClipMetadata aThrust = M("ss.attack.thrust", "Sword_Dash", MotionCategory.Attack, MotionTag.Attack | MotionTag.Combat | MotionTag.Thrust, SS, CB,
                "PLATZHALTER: Sturmangriff statt kurzem Stoss – Mixamo-Clip noetig.", status: MotionAssetStatus.Placeholder,
                start: PoseTag.WeaponLow, end: PoseTag.GuardCenter, contact: 0.4f, prio: MotionPriority.Ability);
            MotionClipMetadata aHeavy = M("ss.ability.heavy", "Sword_Attack", MotionCategory.Attack, MotionTag.Attack | MotionTag.Combat | MotionTag.Heavy | MotionTag.Committed, SS, CB,
                "Waffen-Spezial (schwerer Hieb).", start: PoseTag.GuardCenter, end: PoseTag.GuardCenter, contact: 0.55f, prio: MotionPriority.Ability);

            // Fehlend (Abnahme §144) – Eintraege ohne Clip, damit nichts still fehlt
            string[,] missing =
            {
                { "loco.run", "Locomotion", "Eigener Renn-Clip (heute Jog mit Uebertempo)" },
                { "loco.epic_walk.heavy", "Locomotion", "Epic Walk schwer (Tank)" },
                { "loco.epic_walk.light", "Locomotion", "Epic Walk leicht (Schaden)" },
                { "loco.start.fwd", "StartStop", "Anfahren vorwaerts" }, { "loco.start.left", "StartStop", "Anfahren links" }, { "loco.start.right", "StartStop", "Anfahren rechts" },
                { "loco.stop", "StartStop", "Anhalten" }, { "loco.stop.hard", "StartStop", "Hart anhalten" },
                { "loco.pivot.left", "StartStop", "Pivot links" }, { "loco.pivot.right", "StartStop", "Pivot rechts" },
                { "turn.45", "Turn", "Drehen 45°" }, { "turn.90", "Turn", "Drehen 90°" }, { "turn.135", "Turn", "Drehen 135°" }, { "turn.180", "Turn", "Drehen 180°" },
                { "ss.draw", "DrawSheathe", "Schwert ziehen (Schild am Arm)" }, { "ss.sheathe", "DrawSheathe", "Schwert wegstecken" },
                { "ss.combat.fwd", "Locomotion", "Kampf vorwaerts mit Schild" }, { "ss.combat.back", "Locomotion", "Kampf rueckwaerts mit Schild" },
                { "ss.combat.strafe_l", "Locomotion", "Kampf seitlich links" }, { "ss.combat.strafe_r", "Locomotion", "Kampf seitlich rechts" },
                { "ss.block.impact_light", "Block", "Block-Einschlag leicht" },
                { "jump.run", "Jump", "Lauf-Sprung" }, { "land.soft", "Landing", "Weiche Landung" },
                { "traversal.step_over", "Traversal", "Step-over (Knie)" }, { "traversal.vault_low", "Traversal", "Niedriger Vault" }, { "traversal.hop", "Traversal", "Hop" },
                { "hit.back", "HitReaction", "Treffer von hinten" }, { "hit.left", "HitReaction", "Treffer von links" }, { "hit.right", "HitReaction", "Treffer von rechts" },
                { "death.back", "Death", "Tod nach hinten" }, { "death.side", "Death", "Tod seitlich" },
                { "fem.idle", "Idle", "Weibliche Variante Idle (§111)" }, { "fem.run", "Locomotion", "Weibliche Variante Laufen" }, { "fem.ss.idle.combat", "Idle", "Weibliche Variante Kampf-Idle" }
            };
            for (int i = 0; i < missing.GetLength(0); i++)
            {
                var cat = (MotionCategory)System.Enum.Parse(typeof(MotionCategory), missing[i, 1]);
                M(missing[i, 0], null, cat, MotionTag.None, missing[i, 0].StartsWith("ss.") ? SS : WeaponFamily.None, EX, missing[i, 2]);
            }
            db.clips = metas;

            // ---------------- Angriffe Schwert + Schild
            var attacks = new List<AttackMotionDefinition>
            {
                Attack("DiagRight", aDiag, "Diagonal rechts", BodyZone.Mid, new Vector2(0.35f, 0.85f), 1.8f, 90f, 0.6f, quick: true, committed: false, ability: false),
                Attack("BackLeft", aBack, "Rueckhand links", BodyZone.Mid, new Vector2(0.35f, 0.85f), 1.8f, 90f, -0.6f, quick: true, committed: false, ability: false),
                Attack("High", aHigh, "Hoher Schlag", BodyZone.High, new Vector2(0.6f, 1.05f), 1.9f, 60f, 0f, quick: false, committed: true, ability: false),
                Attack("ShieldBash", aBash, "Schildstoss", BodyZone.Mid, new Vector2(0.3f, 0.8f), 1.1f, 40f, 0f, quick: true, committed: false, ability: false),
                Attack("LowCut", aLow, "Tiefer Schnitt", BodyZone.Low, new Vector2(0.02f, 0.45f), 1.8f, 80f, 0.4f, quick: false, committed: false, ability: false),
                Attack("Thrust", aThrust, "Stoss", BodyZone.Mid, new Vector2(0.3f, 0.75f), 2.1f, 20f, 0f, quick: true, committed: false, ability: false),
                Attack("Heavy", aHeavy, "Schwerer Hieb", BodyZone.Mid, new Vector2(0.3f, 0.95f), 2.0f, 120f, 0.8f, quick: false, committed: true, ability: true)
            };

            WeaponMotionProfile ss = Weapon("SwordShield", SS, "Tank-Referenz: Schild schuetzt die Mittellinie, Schlaege effizient, nach jedem Angriff Guard (§127).",
                WeaponSheath.HipLeft, WeaponSheath.Arm, 0.55f, 0.8f, 1.1f, 2.3f, 1.2f, attacks, null, arm: 0.8f, upper: 0.9f);
            WeaponMotionProfile one = Weapon("OneHandSword", WeaponFamily.OneHandSword, "Schwert ohne Schild – vorerst Rueckfall auf Schwert+Schild.",
                WeaponSheath.HipLeft, WeaponSheath.None, 0.5f, 0.7f, 1.1f, 2.3f, 1.2f, new List<AttackMotionDefinition>(), ss, arm: 0.95f, upper: 1f);
            WeaponMotionProfile pistol = Weapon("Pistol", WeaponFamily.Pistol, "Radschlosspistole – Haltung heute ueber die Halte-Ebene.",
                WeaponSheath.HipRight, WeaponSheath.None, 0.4f, 0.5f, 6f, 20f, 0.4f, new List<AttackMotionDefinition>(), null, arm: 0.95f, upper: 1f);
            db.weapons = new List<WeaponMotionProfile> { ss, one, pistol };

            MotionIdentityProfile golden = LoadOrCreate<MotionIdentityProfile>(DataDir + "/MotionIdentity_HumanTankSwordShield.asset", out bool newGolden);
            if (newGolden) golden.intent = "Goldene Referenz (§9): Mensch · Tank · schwere Ruestung · Schwert + Schild.";
            golden.body = db.bodies[0];
            golden.race = db.races[0];
            golden.classMotion = db.classes[0];
            golden.armor = db.armors[2];
            golden.weapon = ss;
            EditorUtility.SetDirty(golden);
            db.golden = golden;
            db.identities = new List<MotionIdentityProfile> { golden };

            db.Invalidate();
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            return WriteReport(db);
        }

        // ================================================================== Bericht

        public static string WriteReport(MotionDatabase db)
        {
            var sb = new StringBuilder();
            int authored = 0, retarget = 0, placeholder = 0, missing = 0;
            foreach (MotionClipMetadata m in db.clips)
            {
                switch (m.status)
                {
                    case MotionAssetStatus.Authored: authored++; break;
                    case MotionAssetStatus.Retargeted: retarget++; break;
                    case MotionAssetStatus.Placeholder: placeholder++; break;
                    default: missing++; break;
                }
            }
            sb.AppendLine($"ASHVEIL Motion-Bestand · {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Clips: {db.clips.Count} (fertig {authored}, uebernommen {retarget}, Platzhalter {placeholder}, fehlend {missing})");
            sb.AppendLine($"Schichten: {db.bodies.Count} Koerper, {db.races.Count} Rassen, {db.classes.Count} Klassen, {db.armors.Count} Ruestungen, {db.weapons.Count} Waffen");
            foreach (WeaponMotionProfile w in db.weapons)
                sb.AppendLine($"  {w.family}: {w.attacks.Count} Angriffe{(w.fallback != null ? $" (Rueckfall {w.fallback.family})" : "")}");
            sb.AppendLine();
            foreach (MotionAssetStatus st in new[] { MotionAssetStatus.Missing, MotionAssetStatus.Placeholder, MotionAssetStatus.Retargeted, MotionAssetStatus.Authored })
            {
                var list = db.clips.Where(m => m.status == st).ToList();
                if (list.Count == 0) continue;
                sb.AppendLine($"[{st}]");
                foreach (MotionClipMetadata m in list)
                    sb.AppendLine($"  {m.motionId,-26} {m.category,-12} {(m.clip != null ? m.clip.name : "-"),-22} {m.notes}");
                sb.AppendLine();
            }
            string path = ProjectPaths.Logs("Motion-Bestand.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return sb.ToString();
        }

        // ================================================================== Helfer

        private static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
        {
            T a = AssetDatabase.LoadAssetAtPath<T>(path);
            created = a == null;
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        private static BodyMotionProfile Body(string name, BodyMotionVariant v, string intent, float stride, float arm, float shoulderPelvis, float pelvis)
        {
            var p = LoadOrCreate<BodyMotionProfile>($"{DataDir}/MotionBody_{name}.asset", out bool created);
            if (created)
            {
                p.variant = v;
                p.intent = intent;
                MotionModifiers m = MotionModifiers.Neutral;
                m.strideScale = stride; m.armSwingScale = arm; m.shoulderPelvisRhythm = shoulderPelvis; m.pelvisStability = pelvis;
                p.modifiers = m;
                EditorUtility.SetDirty(p);
            }
            return p;
        }

        private static RaceMotionProfile Race(string name, string id, bool provisional, string intent)
        {
            var p = LoadOrCreate<RaceMotionProfile>($"{DataDir}/MotionRace_{name}.asset", out bool created);
            if (created)
            {
                p.raceId = id; p.provisional = provisional; p.intent = intent; p.modifiers = MotionModifiers.Neutral;
                EditorUtility.SetDirty(p);
            }
            return p;
        }

        private static ClassMotionProfile Class(string name, ClassRole role, float economy, float sheathe, string intent,
            float upper, float turn, float pelvis, float transition)
        {
            var p = LoadOrCreate<ClassMotionProfile>($"{DataDir}/MotionClass_{name}.asset", out bool created);
            if (created)
            {
                p.role = role; p.economyOfMotion = economy; p.autoSheatheDelay = sheathe; p.intent = intent;
                p.recoveryPose = PoseTag.GuardCenter;
                MotionModifiers m = MotionModifiers.Neutral;
                m.upperBodyMotion = upper; m.turnInertia = turn; m.pelvisStability = pelvis; m.transitionSpeed = transition;
                p.modifiers = m;
                EditorUtility.SetDirty(p);
            }
            return p;
        }

        private static ArmorMotionProfile Armor(string name, ArmorCategory cat, string intent, float arm, float inertia, float landing, float breathing)
        {
            var p = LoadOrCreate<ArmorMotionProfile>($"{DataDir}/MotionArmor_{name}.asset", out bool created);
            if (created)
            {
                p.category = cat; p.intent = intent;
                MotionModifiers m = MotionModifiers.Neutral;
                m.armSwingScale = arm; m.massInertia = inertia; m.landingWeight = landing; m.breathing = breathing;
                if (cat == ArmorCategory.Heavy) { m.strideScale = 0.96f; m.centerOfMassDrop = 0.015f; m.upperBodyMotion = 0.85f; }
                p.modifiers = m;
                EditorUtility.SetDirty(p);
            }
            return p;
        }

        private static WeaponMotionProfile Weapon(string name, WeaponFamily fam, string intent, WeaponSheath main, WeaponSheath off,
            float draw, float sheathe, float rangeMin, float rangeMax, float clearance, List<AttackMotionDefinition> attacks, WeaponMotionProfile fallback,
            float arm, float upper)
        {
            var p = LoadOrCreate<WeaponMotionProfile>($"{DataDir}/MotionWeapon_{name}.asset", out bool created);
            if (created)
            {
                p.family = fam; p.intent = intent; p.mainSheathed = main; p.offhandSheathed = off;
                p.drawTime = draw; p.sheatheTime = sheathe;
                p.preferredRangeMin = rangeMin; p.preferredRangeMax = rangeMax; p.swingClearance = clearance;
                MotionModifiers m = MotionModifiers.Neutral;
                m.armSwingScale = arm; m.upperBodyMotion = upper;
                p.modifiers = m;
            }
            // Angriffsliste und Rueckfall immer aktuell halten (neue Angriffe kommen dazu, entfernte bleiben draussen)
            p.attacks = attacks;
            p.fallback = fallback;
            EditorUtility.SetDirty(p);
            return p;
        }

        private static MotionClipMetadata Meta(string id, AnimationClip clip, string source, MotionAssetStatus status, string notes,
            MotionCategory cat, MotionTag tags, WeaponFamily fam, MotionStance st, bool loop, Vector2 speed, PoseTag start, PoseTag end,
            float contact, MotionDirection dir, MotionPriority prio)
        {
            string file = id.Replace('.', '_');
            var m = LoadOrCreate<MotionClipMetadata>($"{ClipDir}/Clip_{file}.asset", out bool created);
            if (created)
            {
                m.motionId = id; m.category = cat; m.tags = tags; m.weaponFamily = fam; m.stance = st; m.loop = loop;
                m.speedRange = speed; m.startPose = start; m.endPose = end; m.contactNormalizedTime = contact;
                m.direction = dir; m.priority = prio; m.mirrorAllowed = false; m.notes = notes;
                m.rootMotion = cat == MotionCategory.Attack || cat == MotionCategory.Traversal ? RootMotionPolicy.MotorProfile : RootMotionPolicy.InPlace;
            }
            // Clip und Status spiegeln immer den echten Bestand (ein spaeter ergaenzter Clip hebt „Missing“ auf)
            if (m.clip == null || m.status <= MotionAssetStatus.Placeholder)
            {
                m.clip = clip;
                m.source = source;
                m.status = status;
                if (!string.IsNullOrEmpty(notes)) m.notes = notes;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        private static AttackMotionDefinition Attack(string name, MotionClipMetadata motion, string label, BodyZone zone, Vector2 height,
            float range, float arc, float side, bool quick, bool committed, bool ability)
        {
            var a = LoadOrCreate<AttackMotionDefinition>($"{AttackDir}/Attack_SS_{name}.asset", out bool created);
            if (created)
            {
                a.label = label; a.zone = zone; a.heightRange = height; a.preferredRange = range;
                a.minRange = Mathf.Max(0.5f, range - 1f); a.maxRange = range + 0.8f;
                a.attackArc = arc; a.swingSide = side; a.quick = quick; a.committed = committed; a.abilityOnly = ability;
                a.startPose = motion != null ? motion.startPose : PoseTag.Any;
                a.endPose = motion != null ? motion.endPose : PoseTag.Any;
                a.maxTranslationWarp = 0.35f; a.maxRotationWarp = 25f; a.forwardStep = quick ? 0.1f : 0.2f;
                a.minTargetSize = TargetSizeClass.Small;
                a.maxTargetSize = zone == BodyZone.Low ? TargetSizeClass.Titanic : TargetSizeClass.Huge;
            }
            a.motion = motion;
            EditorUtility.SetDirty(a);
            return a;
        }
    }
}
