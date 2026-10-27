using Ashveil.Gameplay.Characters;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Erzeugt den Animator des Spielers aus den Quaternius-Animationen.
    ///
    /// Aufbau:
    ///   Layer "Base" (ganzer Koerper)
    ///     Locomotion (Blend: Stehen, Gehen, Laufen, Sprinten) · Rueckwaerts (gleiche Clips rueckwaerts)
    ///     Sprung (Start, Luft, Landung) · Harte Landung · Tod
    ///   Layer "WeaponHold" (Maske: nur Arme & Finger)
    ///     Leer · Pistole halten · Schwert halten
    ///   Layer "UpperBody" (Maske: Oberkoerper & Arme)
    ///     Leer · Schwert-Kombo A/B/C · Schwert-Spezial · Schuss · Gezielter Schuss · Nachladen · Treffer
    ///   Layer "FullBody": Rolle, Luftschlag, Sturzangriff
    ///   Layer "Emote" (M8, ganzer Koerper, zuoberst): Sitzen, Tanzen, Reden – und Taetigkeiten fuer NPCs
    ///     (Holz hacken, tragen, ernten, knien, essen, Arme verschraenkt, Laterne, Wagen fahren …). Gesteuert per
    ///     CrossFade von <c>EmotePoser</c> bzw. dem NPC-Tagesablauf.
    ///
    /// Die Haltung im Stand (Mann/Frau verschieden) legt danach <c>StancePoser</c> ueber die Animation.
    ///
    /// Angriffs-Clips werden auf die Zeiten der Waffendaten beschleunigt/verlangsamt.
    /// Fuer den weiblichen Koerper entsteht ein Override-Controller (eigener Gang).
    ///
    ///   Menue: Ashveil > Animation > Spieler-Animator erzeugen
    /// </summary>
    public static class PlayerAnimatorBuilder
    {
        public const string Folder = ProjectSetup.Root + "/Animations";
        public const string ControllerPath = Folder + "/Player_Humanoid.controller";
        public const string FemaleOverridePath = Folder + "/Player_Female.overrideController";
        private const string BauPath = Folder + "/_Bau_Player_Humanoid.controller";
        private const string FemaleBauPath = Folder + "/_Bau_Player_Female.overrideController";
        public const string UpperMaskPath = Folder + "/UpperBody.mask";
        public const string ArmsMaskPath = Folder + "/Arms.mask";

        // Zieldauern (s) – passend zu den Waffen-Assets aus dem Szenenaufbau
        private const float SwordLightDuration = 0.5f;
        private const float SwordHeavyDuration = 0.75f;
        private const float SwordSpecialDuration = 0.85f;
        private const float PistolShotDuration = 0.45f;
        private const float PistolAimedDuration = 1.1f;
        private const float PistolReloadDuration = 2.2f;
        private const float DodgeDuration = 0.4f;
        /// <summary>Anteil von Jump_Land, der noch Luftpose zeigt (0,07 s von 1,27 s, aus der FBX gemessen).</summary>
        private const float LandSkipFraction = 0.055f;
        private const float AirSlashDuration = 0.55f;
        private const float PlungeWindupDuration = 0.4f;

        public static bool Exists => AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null;

        [MenuItem("Ashveil/Animation/Spieler-Animator erzeugen", priority = 310)]
        public static void BuildFromMenu()
        {
            if (Build()) EditorUtility.DisplayDialog("Ashveil", "Animator erzeugt:\n" + ControllerPath, "OK");
        }

        public static bool Build()
        {
            Dictionary<string, AnimationClip> clips = LoadClips();
            if (clips.Count == 0)
            {
                EditorUtility.DisplayDialog("Ashveil", "Keine Animationen gefunden. Bitte zuerst 'Ashveil > Import > Quaternius-Animationen einlesen'.", "OK");
                return false;
            }

            ProjectSetup.EnsureFolder(Folder);
            // Neubau in einer Bau-Datei; danach wird ihr Inhalt über die vorhandene Datei kopiert. So behält der Spieler-Animator
            // seine GUID, und Szenen und Prefabs verlieren den Verweis nicht (15.10.2026; vorher: löschen + neu = neue GUID).
            AssetDatabase.DeleteAsset(BauPath);
            AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(BauPath);

            // ---------------- Parameter
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("JumpProgress", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Sprinting", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Walking", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("WeaponType", AnimatorControllerParameterType.Int);
            ctrl.AddParameter("AttackIndex", AnimatorControllerParameterType.Int);
            ctrl.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Land", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("HardLand", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Special", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Reload", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Casting", AnimatorControllerParameterType.Bool);       // 23.10.: Zauber mit Wirkzeit (Max: vorhandene Magier-Clips nutzen)
            ctrl.AddParameter("CastRelease", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Dodge", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("AirAttack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Plunge", AnimatorControllerParameterType.Trigger);
            // Motion-System: Abspieltempo der Fortbewegung (Fuesse gleiten nicht, gesetzt vom CharacterMotionPoser) und epischer Gang
            ctrl.AddParameter("LocoRate", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("EpicWalk", AnimatorControllerParameterType.Bool);

            // Grounded startet als true, sonst beginnt die Figur in der Fall-Animation.
            AnimatorControllerParameter[] ps = ctrl.parameters;
            foreach (AnimatorControllerParameter p in ps)
            {
                if (p.name == "Grounded") p.defaultBool = true;
                if (p.name == "LocoRate") p.defaultFloat = 1f;
            }
            ctrl.parameters = ps;

            BuildBaseLayer(ctrl, clips);
            BuildUpperLayer(ctrl, clips);
            BuildFullBodyLayer(ctrl, clips);
            BuildEmoteLayer(ctrl, clips);
            BuildCastLayer(ctrl, clips);   // 23.10.: zuletzt – vorhandene Ebenen behalten ihre Indizes

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            ctrl = Uebernehmen<AnimatorController>(BauPath, ControllerPath);
            BuildFemaleOverride(ctrl, clips);
            AssetDatabase.SaveAssets();
            Debug.Log("[Ashveil] Spieler-Animator erzeugt: " + ControllerPath);
            return true;
        }

        // ================================================================ Base-Layer

        private static void BuildBaseLayer(AnimatorController ctrl, Dictionary<string, AnimationClip> clips)
        {
            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;

            // Vorwaerts-Bewegung
            AnimatorState loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree fwd, 0);
            fwd.blendType = BlendTreeType.Simple1D;
            fwd.blendParameter = "Speed";
            fwd.useAutomaticThresholds = false;
            // Schwellen = Tempi des Motors (eine Quelle, keine Kopien). Rennen liegt zwischen Joggen und Sprint und mischt
            // beide Clips (Fussphasen passen: linker Fuss setzt bei beiden am Zyklusanfang auf). Das genaue Abspieltempo
            // gegen Fussgleiten regelt „LocoRate“ zur Laufzeit.
            var mot = new CharacterMotorSettings();
            AddChild(fwd, clips, "Idle_Loop", 0f);
            AddChild(fwd, clips, "Walk_Loop", mot.walkSpeed);
            AddChild(fwd, clips, "Jog_Fwd_Loop", mot.jogSpeed);
            // 18.10. (reine Aufnahmen): eigener Rennen-Clip beim Renntempo; Sprint nutzt dieselbe Aufnahme schneller (LocoRate)
            if (clips.ContainsKey("Run_Loop")) AddChild(fwd, clips, "Run_Loop", mot.runSpeed);
            AddChild(fwd, clips, "Sprint_Loop", mot.sprintSpeed);
            loco.speedParameterActive = true;
            loco.speedParameter = "LocoRate";
            // Stehen etwas ruhiger abspielen (Atmen/Gewichtsverlagerung wirken sonst hektisch)
            ChildMotion[] kids = fwd.children;
            if (kids.Length > 0 && kids[0].threshold <= 0f) { kids[0].timeScale = 0.75f; fwd.children = kids; }
            sm.defaultState = loco;

            // Rueckwaerts: Geh-/Lauf-Clips rueckwaerts abgespielt
            AnimatorState back = ctrl.CreateBlendTreeInController("Backward", out BlendTree bwd, 0);
            bwd.blendType = BlendTreeType.Simple1D;
            bwd.blendParameter = "Speed";
            bwd.useAutomaticThresholds = false;
            AddChild(bwd, clips, "Walk_Loop", mot.walkSpeed * 0.7f);
            AddChild(bwd, clips, "Jog_Fwd_Loop", mot.backwardSpeed);
            back.speed = -1f;
            back.speedParameterActive = true;
            back.speedParameter = "LocoRate";

            // Epischer Gang (§13): eigene, steuerbare Fortbewegung – ruhiger Clip, Haltung formt der Poser
            AnimatorState epic = ctrl.CreateBlendTreeInController("EpicWalk", out BlendTree ep, 0);
            ep.blendType = BlendTreeType.Simple1D;
            ep.blendParameter = "Speed";
            ep.useAutomaticThresholds = false;
            AddChild(ep, clips, "Idle_Loop", 0f);
            AddChild(ep, clips, clips.ContainsKey("Walk_Formal_Loop") ? "Walk_Formal_Loop" : "Walk_Loop", mot.epicWalkSpeed);
            epic.speedParameterActive = true;
            epic.speedParameter = "LocoRate";

            // Sprung / Fallen: EIN Luft-Zustand, dessen Abspielposition direkt aus der Vertikalgeschwindigkeit
            // kommt (Parameter "JumpProgress", gesetzt von der Bridge). Dadurch passt die Bewegung immer genau
            // zur Flugkurve – egal wie hoch, wie lang oder wie oft hintereinander gesprungen wird.
            // Jump_Start enthaelt Absprung, Arme hochschwingen, Beine anziehen und Beine zur Landung strecken.
            // 17.10. nachts (Bewegung v4): Stand- und Laufsprung getrennt. Der Laufsprung (CMU 127_25) beginnt mitten im
            // Schritt – aus dem Stand sah das wie ein Ausfallschritt in die Luft aus. Mit eigenem Standsprung (CMU 16_03)
            // mischt ein Blend Tree nach Tempo: 0 m/s Standsprung, ab SprungLaufTempo Laufsprung.
            AnimatorState air;
            if (clips.ContainsKey("Jump_Start_Stand"))
            {
                air = ctrl.CreateBlendTreeInController("Airborne", out BlendTree ab, 0);
                ab.blendType = BlendTreeType.Simple1D; ab.blendParameter = "Speed"; ab.useAutomaticThresholds = false;
                AddChild(ab, clips, "Jump_Start_Stand", 0f);
                AddChild(ab, clips, "Jump_Start", SprungLaufTempo);
            }
            else air = AddState(sm, "Airborne", clips, "Jump_Start");
            air.timeParameterActive = true;
            air.timeParameter = "JumpProgress";

            // Landung: die ersten Frames von Jump_Land zeigen noch die Luftpose – ueberspringen,
            // damit der Aufprall sofort sichtbar ist (kein "Schweben" kurz vor dem Boden).
            AnimatorState jumpLand;
            if (clips.ContainsKey("Jump_Land_Stand"))
            {
                jumpLand = ctrl.CreateBlendTreeInController("Land", out BlendTree lb, 0);
                lb.blendType = BlendTreeType.Simple1D; lb.blendParameter = "Speed"; lb.useAutomaticThresholds = false;
                AddChild(lb, clips, "Jump_Land_Stand", 0f);
                AddChild(lb, clips, "Jump_Land", SprungLaufTempo);
            }
            else jumpLand = AddState(sm, "Land", clips, "Jump_Land");
            // CMU-Landung beginnt mit dem Aufsetzen – nichts zu ueberspringen
            float landSkip = clips.TryGetValue("Jump_Land", out AnimationClip jl) && (jl.name.StartsWith("CMU_") || jl.name.StartsWith("ANM_")) ? 0f : LandSkipFraction;   // echte Aufnahmen beginnen beim Aufsetzen
            jumpLand.cycleOffset = landSkip;
            jumpLand.speed = landSkip > 0f ? 1.5f : 1f; // CMU-Landung dauert echte 0,5 s
            AnimatorState hardLand = AddState(sm, "HardLand", clips, clips.ContainsKey("NinjaJump_Land") ? "NinjaJump_Land" : "Jump_Land");
            hardLand.cycleOffset = LandSkipFraction;
            hardLand.speed = 1.2f;
            AnimatorState death = AddState(sm, "Death", clips, "Death01");

            Transition(loco, epic, 0.3f).AddCondition(AnimatorConditionMode.If, 0f, "EpicWalk");
            Transition(epic, loco, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0f, "EpicWalk");
            Transition(epic, back, 0.15f).AddCondition(AnimatorConditionMode.Less, -0.25f, "MoveZ");
            Transition(epic, air, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

            // Vor <-> Zurueck
            Transition(loco, back, 0.15f).AddCondition(AnimatorConditionMode.Less, -0.25f, "MoveZ");
            Transition(back, loco, 0.15f).AddCondition(AnimatorConditionMode.Greater, -0.15f, "MoveZ");

            // Absprung: sofort und von ueberall (auch aus der Landung heraus -> fluessiges Dauerspringen)
            AnimatorStateTransition anyJump = sm.AddAnyStateTransition(air);
            anyJump.duration = 0.06f;
            anyJump.canTransitionToSelf = true;
            anyJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            anyJump.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

            // Von einer Kante fallen
            Transition(loco, air, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            Transition(back, air, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

            // Aufsetzen
            Transition(air, jumpLand, 0.05f).AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            // Nach der Landung: im Laufen nach einem kurzen Einfedern weiter, im Stand ausklingen
            AnimatorStateTransition landRun = jumpLand.AddTransition(loco);
            landRun.hasExitTime = true;
            landRun.exitTime = landSkip + 0.12f;
            landRun.duration = 0.12f;
            landRun.AddCondition(AnimatorConditionMode.Greater, 1f, "Speed");
            AnimatorStateTransition landDone = jumpLand.AddTransition(loco);
            landDone.hasExitTime = true;
            landDone.exitTime = 0.55f;
            landDone.duration = 0.2f;
            Transition(jumpLand, air, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

            AnimatorStateTransition anyHard = sm.AddAnyStateTransition(hardLand);
            anyHard.duration = 0.05f;
            anyHard.canTransitionToSelf = false;
            anyHard.AddCondition(AnimatorConditionMode.If, 0f, "HardLand");
            anyHard.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            AnimatorStateTransition hardDone = hardLand.AddTransition(loco);
            hardDone.hasExitTime = true;
            hardDone.exitTime = 0.7f;
            hardDone.duration = 0.2f;

            AnimatorStateTransition anyDeath = sm.AddAnyStateTransition(death);
            anyDeath.duration = 0.15f;
            anyDeath.canTransitionToSelf = false;
            anyDeath.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            // Wiederbelebung: aus dem Tod zurueck in die Bewegung
            Transition(death, loco, 0.35f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
        }

        // ================================================================ Oberkoerper-Layer

        private static void BuildUpperLayer(AnimatorController ctrl, Dictionary<string, AnimationClip> clips)
        {
            // Waffe halten: NUR Arme und Finger. Ruecken und Kopf bleiben bei der Grundbewegung,
            // damit die Figur aufrecht steht (Sword_Idle allein wuerde den ganzen Oberkoerper beugen).
            AnimatorStateMachine hold = AddMaskedLayer(ctrl, "WeaponHold", CreateMask(ArmsMaskPath, false));
            AnimatorState holdEmpty = hold.AddState("Empty");
            hold.defaultState = holdEmpty;
            AnimatorState pistolIdle = AddState(hold, "PistolHold", clips, "Pistol_Idle_Loop");
            Transition(holdEmpty, pistolIdle, 0.2f).AddCondition(AnimatorConditionMode.Equals, 2f, "WeaponType");
            Transition(pistolIdle, holdEmpty, 0.2f).AddCondition(AnimatorConditionMode.NotEqual, 2f, "WeaponType");
            if (clips.ContainsKey("Sword_Idle"))
            {
                AnimatorState swordIdle = AddState(hold, "SwordHold", clips, "Sword_Idle");
                AnimatorStateTransition toHold = Transition(holdEmpty, swordIdle, 0.25f);
                toHold.AddCondition(AnimatorConditionMode.Equals, 1f, "WeaponType");
                toHold.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
                Transition(swordIdle, holdEmpty, 0.2f).AddCondition(AnimatorConditionMode.NotEqual, 1f, "WeaponType");
                // Im Sprung die Arme freigeben, damit sie mitschwingen
                Transition(swordIdle, holdEmpty, 0.12f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            }

            // Aktionen: ganzer Oberkoerper (Schlaege drehen den Rumpf mit). Danach zurueck zu "Empty" –
            // darunter haelt die WeaponHold-Ebene die Waffe weiter, die Arme sinken also nicht ab.
            AnimatorStateMachine sm = AddMaskedLayer(ctrl, "UpperBody", CreateMask(UpperMaskPath, true));
            AnimatorState empty = sm.AddState("Empty");
            sm.defaultState = empty;

            // Schwert
            AddAction(sm, empty, clips, "SwordA", "Sword_Regular_A", SwordLightDuration, "Attack", 1, 0);
            AddAction(sm, empty, clips, "SwordB", "Sword_Regular_B", SwordLightDuration, "Attack", 1, 1);
            AddAction(sm, empty, clips, "SwordC", clips.ContainsKey("Sword_Regular_C") ? "Sword_Regular_C" : "Sword_Attack", SwordHeavyDuration, "Attack", 1, 2);
            AddAction(sm, empty, clips, "SwordSpecial", "Sword_Attack", SwordSpecialDuration, "Special", 1, -1);

            // Pistole
            AddAction(sm, empty, clips, "PistolShot", "Pistol_Shoot", PistolShotDuration, "Attack", 2, -1);
            AddAction(sm, empty, clips, "PistolAimedShot", "Pistol_Shoot", PistolAimedDuration, "Special", 2, -1);
            AddAction(sm, empty, clips, "PistolReload", "Pistol_Reload", PistolReloadDuration, "Reload", 2, -1);

            // Treffer (unabhaengig von der Waffe)
            AddAction(sm, empty, clips, "Hit", "Hit_Chest", 0f, "Hit", 0, -1);
        }

        /// <summary>
        /// 23.10.2026 (Max: „war bei den animationen alles für den magier bzw stab mit dabei? dann soll claude das nehmen“): Wirkzeit-Zauber mit den
        /// vorhandenen Quaternius-Clips (UAL1, CC0) – Ansetzen (Spell_Simple_Enter), Halten solange gewirkt wird (Spell_Simple_Idle_Loop),
        /// Auslösen bei Erfolg (Spell_Simple_Shoot) bzw. Absetzen bei Abbruch (Spell_Simple_Exit). Vorher bewegte sich der Körper beim Wirken nicht.
        /// </summary>
        /// <summary>23.10.: eigene Ebene nur für den linken Arm – die rechte Hand hält den Stab (der Clip auf dem ganzen Oberkörper
        /// zog den rechten Arm nach unten, der Stab lag flach zum Boden).</summary>
        private static void BuildCastLayer(AnimatorController ctrl, Dictionary<string, AnimationClip> clips)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(CastMaskPath);
            if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, CastMaskPath); }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.LeftFingers);
            }
            EditorUtility.SetDirty(mask);
            AnimatorStateMachine sm = AddMaskedLayer(ctrl, "Cast", mask);
            AnimatorState empty = sm.AddState("Empty");
            sm.defaultState = empty;
            AddCast(sm, empty, clips);
        }

        public const string CastMaskPath = Folder + "/Cast.mask";

        private static void AddCast(AnimatorStateMachine sm, AnimatorState empty, Dictionary<string, AnimationClip> clips)
        {
            if (!clips.TryGetValue("Spell_Simple_Enter", out AnimationClip enter) || !clips.TryGetValue("Spell_Simple_Idle_Loop", out AnimationClip loop) ||
                !clips.TryGetValue("Spell_Simple_Shoot", out AnimationClip shoot) || !clips.TryGetValue("Spell_Simple_Exit", out AnimationClip exit))
            {
                Debug.LogWarning("[Ashveil] Spell_Simple-Clips fehlen – Wirken ohne Animation.");
                return;
            }
            AnimatorState sEnter = sm.AddState("CastEnter"); sEnter.motion = enter;
            AnimatorState sLoop = sm.AddState("CastLoop"); sLoop.motion = loop;
            AnimatorState sShoot = sm.AddState("CastShoot"); sShoot.motion = shoot;
            AnimatorState sExit = sm.AddState("CastExit"); sExit.motion = exit;
            // Ansetzen nur aus „Empty“ (laufende Schläge/Treffer nicht unterbrechen)
            AnimatorStateTransition t = empty.AddTransition(sEnter); t.duration = 0.12f; t.hasExitTime = false;
            t.AddCondition(AnimatorConditionMode.If, 0f, "Casting");
            t = sEnter.AddTransition(sLoop); t.hasExitTime = true; t.exitTime = 0.9f; t.duration = 0.1f;
            t = sEnter.AddTransition(sShoot); t.hasExitTime = false; t.duration = 0.08f; t.AddCondition(AnimatorConditionMode.If, 0f, "CastRelease");
            t = sEnter.AddTransition(sExit); t.hasExitTime = false; t.duration = 0.12f; t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Casting");
            t = sLoop.AddTransition(sShoot); t.hasExitTime = false; t.duration = 0.08f; t.AddCondition(AnimatorConditionMode.If, 0f, "CastRelease");
            t = sLoop.AddTransition(sExit); t.hasExitTime = false; t.duration = 0.12f; t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Casting");
            t = sShoot.AddTransition(empty); t.hasExitTime = true; t.exitTime = 0.9f; t.duration = 0.15f;
            t = sExit.AddTransition(empty); t.hasExitTime = true; t.exitTime = 0.9f; t.duration = 0.15f;
        }

        private static AnimatorStateMachine AddMaskedLayer(AnimatorController ctrl, string name, AvatarMask mask)
        {
            ctrl.AddLayer(name);
            AnimatorControllerLayer[] layers = ctrl.layers;
            int idx = layers.Length - 1;
            layers[idx].avatarMask = mask;
            layers[idx].defaultWeight = 1f;
            layers[idx].blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;
            return ctrl.layers[idx].stateMachine;
        }

        // ================================================================ Ganzkoerper-Aktionen

        /// <summary>
        /// Layer "FullBody" ohne Maske: Ausweichrolle, Luftschlag und Sturzangriff bewegen den ganzen Koerper.
        /// </summary>
        private static void BuildFullBodyLayer(AnimatorController ctrl, Dictionary<string, AnimationClip> clips)
        {
            ctrl.AddLayer("FullBody");
            AnimatorControllerLayer[] layers = ctrl.layers;
            int idx = layers.Length - 1;
            layers[idx].defaultWeight = 1f;
            layers[idx].blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;
            AnimatorStateMachine sm = ctrl.layers[idx].stateMachine;

            AnimatorState empty = sm.AddState("Empty");
            sm.defaultState = empty;

            // Ausweichrolle (Richtung macht die Bridge ueber die Modelldrehung)
            AddAction(sm, empty, clips, "Roll", "Roll", DodgeDuration, "Dodge", 0, -1);

            // Luftschlag: eigene, weit ausholende Bewegung im Sprung
            AddAction(sm, empty, clips, "AirSlash", clips.ContainsKey("Sword_Dash") ? "Sword_Dash" : "Sword_Attack", AirSlashDuration, "AirAttack", 0, -1);

            // Sturzangriff: Ueberkopf-Hieb nach unten, bleibt bis zur Landung
            string plungeClip = clips.ContainsKey("OverhandThrow") ? "OverhandThrow" : "Sword_Attack";
            if (clips.TryGetValue(plungeClip, out AnimationClip pc))
            {
                AnimatorState dive = sm.AddState("PlungeDive");
                dive.motion = pc;
                if (pc.length > 0f) dive.speed = pc.length / PlungeWindupDuration;
                AnimatorStateTransition enter = sm.AddAnyStateTransition(dive);
                enter.duration = 0.05f;
                enter.canTransitionToSelf = false;
                enter.AddCondition(AnimatorConditionMode.If, 0f, "Plunge");
                AnimatorStateTransition land = dive.AddTransition(empty);
                land.hasExitTime = false;
                land.duration = 0.1f;
                land.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            }
        }

        /// <summary>
        /// Aktion im Oberkoerper-Layer: Any State -> Aktion (Trigger + Waffentyp [+ Kombo-Index]) -> Leer.
        /// weaponType 0 = egal, attackIndex -1 = egal, duration 0 = Originaltempo.
        /// </summary>
        private static void AddAction(AnimatorStateMachine sm, AnimatorState exitTo, Dictionary<string, AnimationClip> clips,
            string stateName, string clipName, float duration, string trigger, int weaponType, int attackIndex)
        {
            if (!clips.TryGetValue(clipName, out AnimationClip clip))
            {
                Debug.LogWarning($"[Ashveil] Animation '{clipName}' fehlt – Zustand '{stateName}' wird ausgelassen.");
                return;
            }

            AnimatorState state = sm.AddState(stateName);
            state.motion = clip;
            if (duration > 0f && clip.length > 0f) state.speed = clip.length / duration;

            AnimatorStateTransition enter = sm.AddAnyStateTransition(state);
            enter.duration = 0.06f;
            enter.canTransitionToSelf = true;
            enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            if (weaponType > 0) enter.AddCondition(AnimatorConditionMode.Equals, weaponType, "WeaponType");
            if (attackIndex >= 0) enter.AddCondition(AnimatorConditionMode.Equals, attackIndex, "AttackIndex");

            AnimatorStateTransition leave = state.AddTransition(exitTo);
            leave.hasExitTime = true;
            leave.exitTime = 0.92f;
            leave.duration = 0.12f;
        }

        // ================================================================ Emote-/Taetigkeits-Layer (M8)

        /// <summary>Zustandsname → Clip. Namen sind die Schnittstelle zu EmotePoser/NpcActivity (nicht umbenennen).</summary>
        public static readonly (string state, string clip)[] EmoteStates =
        {
            ("SitEnter", "Sitting_Enter"), ("SitLoop", "Sitting_Idle_Loop"), ("SitTalk", "Sitting_Talking_Loop"),
            ("Dance", "Dance_Loop"), ("Talk", "Idle_Talking_Loop"), ("Chop", "TreeChopping_Loop"), ("Carry", "Walk_Carry_Loop"),
            ("Harvest", "Farm_Harvest"), ("Plant", "Farm_PlantSeed"), ("Water", "Farm_Watering"), ("Kneel", "Fixing_Kneeling"),
            ("Consume", "Consume"), ("FoldArms", "Idle_FoldArms_Loop"), ("Lantern", "Idle_Lantern_Loop"), ("Torch", "Idle_Torch_Loop"),
            ("Drive", "Driving_Loop"), ("Interact", "Interact"), ("PickUp", "PickUp_Table"), ("ChestOpen", "Chest_Open"),
            ("Nod", "Yes"), ("Shake", "Idle_No_Loop"), ("Call", "Idle_Rail_Call"), ("Push", "Push_Loop")
        };

        private static void BuildEmoteLayer(AnimatorController ctrl, Dictionary<string, AnimationClip> clips)
        {
            ctrl.AddLayer("Emote");
            AnimatorControllerLayer[] layers = ctrl.layers;
            int idx = layers.Length - 1;
            layers[idx].defaultWeight = 1f;
            layers[idx].blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;
            AnimatorStateMachine sm = ctrl.layers[idx].stateMachine;
            AnimatorState empty = sm.AddState("Empty");
            sm.defaultState = empty;
            var made = new Dictionary<string, AnimatorState>();
            foreach ((string state, string clip) in EmoteStates)
            {
                if (!clips.ContainsKey(clip)) { Debug.LogWarning($"[Ashveil] Emote-Animation '{clip}' fehlt – Zustand '{state}' ausgelassen."); continue; }
                made[state] = AddState(sm, state, clips, clip);
            }
            // Hinsetzen laeuft von selbst in das Sitzen ueber
            if (made.TryGetValue("SitEnter", out AnimatorState enter) && made.TryGetValue("SitLoop", out AnimatorState loop))
            {
                AnimatorStateTransition t = enter.AddTransition(loop);
                t.hasExitTime = true;
                t.exitTime = 0.95f;
                t.duration = 0.15f;
            }
        }

        // ================================================================ Weiblicher Koerper

        /// <summary>
        /// Gleiche Logik, eigene Clips: der weibliche Koerper nutzt fuer das Gehen die ruhigere
        /// "Formal"-Gangart. Weitere Unterschiede (Laufen, Idle) kommen mit weiteren Animationen dazu.
        /// </summary>
        private static void BuildFemaleOverride(AnimatorController ctrl, Dictionary<string, AnimationClip> clips)
        {
            var ov = new AnimatorOverrideController(ctrl) { name = "Player_Female" };
            // 18.10.: Frauen-Aufnahmen (gleiche CMU-Quelle, in Blender direkt auf CHR_W_MPFB01 übertragen) ersetzen die Clips des
            // Mannes 1:1 – Humanoid-Umrechnung vom Männer- auf den Frauenkörper ließ die Frau 10° zurückgelehnt stehen (Lab 18.10.).
            int ersetzt = 0;
            {
                var list = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                ov.GetOverrides(list);
                foreach (var (schluessel, datei) in NurUAL ? new (string, string)[0] : MpfbAufnahmenFrau)
                {
                    if (!clips.TryGetValue(schluessel, out AnimationClip mann) || mann == null) continue;
                    AnimationClip frau = LadeMpfb(datei);
                    if (frau == null) continue;
                    for (int i = 0; i < list.Count; i++)
                        if (list[i].Key == mann) { list[i] = new KeyValuePair<AnimationClip, AnimationClip>(mann, frau); ersetzt++; }
                }
                if (ersetzt > 0) ov.ApplyOverrides(list);
            }
            if (ersetzt == 0 && !ReineAufnahmen && clips.TryGetValue("Walk_Loop", out AnimationClip walk) && clips.TryGetValue("Walk_Formal_Loop", out AnimationClip formal))
            {
                var list = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                ov.GetOverrides(list);
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Key == walk) list[i] = new KeyValuePair<AnimationClip, AnimationClip>(walk, formal);
                ov.ApplyOverrides(list);
            }
            AssetDatabase.DeleteAsset(FemaleBauPath);
            AssetDatabase.CreateAsset(ov, FemaleBauPath);
            AssetDatabase.SaveAssets();
            Uebernehmen<AnimatorOverrideController>(FemaleBauPath, FemaleOverridePath);
        }

        /// <summary>Inhalt der Bau-Datei über das Ziel kopieren (GUID des Ziels bleibt), Bau-Datei entfernen.</summary>
        private static T Uebernehmen<T>(string bau, string ziel) where T : Object
        {
            // Hauptobjekt wie die Zieldatei benennen, sonst warnt Unity („Main Object Name … does not match filename“)
            var bauObj = AssetDatabase.LoadMainAssetAtPath(bau);
            if (bauObj != null) { bauObj.name = System.IO.Path.GetFileNameWithoutExtension(ziel); EditorUtility.SetDirty(bauObj); AssetDatabase.SaveAssets(); }
            if (System.IO.File.Exists(ziel))
            {
                System.IO.File.Copy(bau, ziel, true);
                AssetDatabase.DeleteAsset(bau);
                AssetDatabase.ImportAsset(ziel, ImportAssetOptions.ForceUpdate);
            }
            else
            {
                string fehler = AssetDatabase.MoveAsset(bau, ziel);
                if (!string.IsNullOrEmpty(fehler)) Debug.LogError("[Ashveil] " + fehler);
            }
            return AssetDatabase.LoadAssetAtPath<T>(ziel);
        }

        // ================================================================ Hilfen

        /// <summary>Maske fuer Arme + Finger; mit 'torso' zusaetzlich Rumpf und Kopf.</summary>
        private static AvatarMask CreateMask(string path, bool torso)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, path);
            }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                bool arms = part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                            part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                            part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                bool body = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head;
                mask.SetHumanoidBodyPartActive(part, arms || (torso && body));
            }
            EditorUtility.SetDirty(mask);
            return mask;
        }

        public const string MocapOrdner = ProjectSetup.Root + "/Animations/Mocap/CMU";
        /// <summary>17.10. spätabends (Bewegung v3): direkt auf das MPFB-Skelett übertragene Aufnahmen (Blender,
        /// ashveil_mpfb_retarget.py; Muybridge-Vergleich vor dem Import). Haben Vorrang vor CMU- und Quaternius-Clips.</summary>
        /// <summary>Ab diesem Tempo (m/s) zeigt der Sprung den Laufsprung, darunter gemischt bis zum Standsprung.</summary>
        public const float SprungLaufTempo = 2.5f;
        public const string MpfbOrdner = ProjectSetup.Root + "/Animations/Mocap/MPFB";
        /// <summary>18.10.: dieselben Aufnahmen auf den Frauenkörper (n5: Rumpf/Knie für den Frauenkörper nachgestellt, Lab stufen-frau) CHR_W_MPFB01 übertragen (ANM_W_*). Fehlt eine, gilt die des Mannes.</summary>
        public static readonly (string schluessel, string datei)[] MpfbAufnahmenFrau =
        {
            ("Walk_Loop", "ANM_W_Gehen_n8"), ("Jog_Fwd_Loop", "ANM_W_Joggen_n7"), ("Sprint_Loop", "ANM_W_Rennen_n7"),
            ("Idle_Loop", "ANM_W_Stehen_n7"), ("Jump_Start", "ANM_W_Laufsprung_j4:ANM_W_Laufsprung_j4_Luft"), ("Jump_Land", "ANM_W_Laufsprung_j4:ANM_W_Laufsprung_j4_Landung"),
            ("Jump_Start_Stand", "ANM_W_Sprung_j4:ANM_W_Sprung_j4_Luft"), ("Jump_Land_Stand", "ANM_W_Sprung_j4:ANM_W_Sprung_j4_Landung"),
        };
        private static AnimationClip LadeMpfb(string datei)
        {
            string fbx = datei.Split(':')[0], clipName = datei.Contains(":") ? datei.Split(':')[1] : null;
            AnimationClip c = AssetDatabase.LoadAllAssetsAtPath(MpfbOrdner + "/" + fbx + ".fbx").OfType<AnimationClip>()
                .FirstOrDefault(x => !x.name.StartsWith("__preview__") && (clipName == null || x.name == clipName));
            return c != null && c.isHumanMotion ? c : null;
        }
        public static readonly (string schluessel, string datei)[] MpfbAufnahmen =
        {
            // 18.10.: _p = Körper v3 (von Hand nach Vorschau); _n = Körper v2 (Silhouette mit Armen); _m = angepasster Körper (Silhouetten-Abgleich) + gestrecktes Standbein; _k = Clips mit dem Avatar des Körpers selbst (vorher eigener Clip-Avatar → Unity streckte die Knie, Knie 17–71° wurden
            // 0–38°). Gehen jetzt CMU 08_02 (Gang-Prüfung: Ferse zuerst, keine X-Beine, Rumpf 2,6°), Joggen 16_46, Rennen 102_10 (Rumpf −8°).
            ("Walk_Loop", "ANM_M_Gehen_n7"), ("Jog_Fwd_Loop", "ANM_M_Joggen_n5"), ("Sprint_Loop", "ANM_M_Rennen_n5"),
            ("Idle_Loop", "ANM_M_Stehen_n6"), ("Jump_Start", "ANM_M_Laufsprung_j4:ANM_M_Laufsprung_j4_Luft"), ("Jump_Land", "ANM_M_Laufsprung_j4:ANM_M_Laufsprung_j4_Landung"),
            ("Jump_Start_Stand", "ANM_M_Sprung_j4:ANM_M_Sprung_j4_Luft"), ("Jump_Land_Stand", "ANM_M_Sprung_j4:ANM_M_Sprung_j4_Landung"),
        };

        /// <summary>
        /// Schalter: CMU-Fortbewegung für die Spielerfigur. 15.10. mittags AUS (Max im Spieltest: „er läuft rückwärts“).
        /// 15.10. nachmittags wieder AN: Ursache gefunden – das CMU-Skelett blickte in Blender entgegengesetzt zur Spielfigur.
        /// Die Bewegung wird jetzt in Blender auf das Skelett der Spielfigur übertragen (ashveil_mensch_retarget.py); Messung
        /// „mocap_pruefen“ (60 Bilder/s) für Gehen, Joggen, Rennen wie Quaternius, Rundum-Bilder geprüft.
        /// Wirksam nach Neubau des Spieler-Animators (Auftrag „spieler_animator“ oder ein Szenenbau).
        /// </summary>
        /// 17.10.2026 abends (Max: „Fang neu an mit den Animationen“, Klick „Quaternius-Spiel-Loops“): AUS. Gehen, Joggen und
        /// Sprint kommen wieder aus den sauberen Quaternius-Loops (UAL1), ohne CMU-Übertragung und ohne Korrekturen. Die CMU-Clips
        /// bleiben im Projekt; true schaltet zurück.
        public static readonly bool MocapFortbewegung = false;
        /// <summary>18.10.2026 abends (Max: „kannst du die bewegungen für menschen von anfang an neu aus gratis vorlagen erstellen und
        /// alles was du bisher gemacht hast löschen?“; Klick „Quaternius UAL“, „Alles archivieren“, „Jetzt“): Bewegung nur aus den
        /// Quaternius-UAL-Clips (CC0, ART-121) – keine CMU-/MPFB-Übertragungen mehr. Die alten Clips liegen im Archiv.</summary>
        public static readonly bool NurUAL = true;
        /// <summary>18.10.2026 spätabends (Max zu UAL: „die bewegungen ansich sind nicht sauber und gefallen mir so nicht“; Klick
        /// „100STYLE“ und „Gehen langsamer“): Stehen, Gehen, Joggen und Rennen aus echten Studio-Aufnahmen, roh und ohne eigene
        /// Übertragung – Unity überträgt per Humanoid. Quelle und Tempo (Aufnahme → Spiel): Stehen 100STYLE Neutral_ID · Gehen
        /// 100STYLE Proud_FW 1,18 → 1,5 m/s · Joggen CMU 104_04 3,0 → 3,0 · Rennen CMU 127_08 4,87 → 6,3 (CMU-Aufnahmen auf das 100STYLE-Skelett umgeschrieben, Tools/Mocap/cmu_zu_s100.py) (Sprint 9 m/s auf derselben
        /// Aufnahme). Sprung und alles Übrige bleiben UAL. false schaltet auf reines UAL zurück.</summary>
        public static readonly bool ReineAufnahmen = false;   // 18.10. nachts (Max: „nimm erstmal das basis paket vom anfang ohne irgendwelche einstellungen … animationen im detail kommen später dann alle zusammen per assets“) → reines UAL
        public const string ReinOrdner = "Assets/_Ashveil/Animations/Mocap/Rein";        public static readonly (string schluessel, string datei)[] ReineClips =
        {
            ("Idle_Loop", "S100_Stehen"), ("Walk_Loop", "S100_Gehen"), ("Jog_Fwd_Loop", "CMU_Joggen"),
            ("Run_Loop", "CMU_Rennen"), ("Sprint_Loop", "CMU_Rennen"),
        };
        /// <summary>Sprung weiter aus CMU 16_01 (Quaternius-Sprung sah im Lab nicht menschlich aus, 16.10.) – wird in v2 eigens geprüft.</summary>
        public static readonly bool MocapSprung = true;
        /// <summary>17.10. spätabends (Max: „Gehen wirkt steif/künstlich“, Richtung „Mischung“ – natürliche Basis): einzelne
        /// Gangarten aus echten Aufnahmen, roh, ohne Korrekturschichten. Gehen CMU 16_21 (Max mochte es am 15.10.), Joggen CMU 16_46
        /// (4,1 m/s ≈ Spieltempo 4,0). Rennen/Sprint bleiben vorerst Quaternius.</summary>
        public static readonly HashSet<string> MocapEinzeln = new HashSet<string> { "Walk_Loop", "Jog_Fwd_Loop", "Sprint_Loop" };   // Sprint: CMU 102_10 (natürliches Rennen) statt Quaternius

        /// <summary>Fortbewegungs-Clips aus echten Aufnahmen (Schlüssel wie Quaternius → CMU-Datei ohne Endung).</summary>
        public static readonly (string schluessel, string cmu)[] Aufnahmen =
        {
            // 15.10. abends: Aufnahmen nach Tempo gewählt (Spieltempi Max 29.09.: Gehen 2,25 · Joggen 4,0 · Rennen 6,3 · Sprint 9 m/s);
            // vorher 1,1/2,7/2,7 m/s → Füße rutschten bis 15 cm.
            // 16.10.: 103_07 war mit 60 Bildern/s aufgenommen, aber als 120 gelesen – der Gang lief doppelt so schnell (320
            // Schritte/min). Echte Tempi: 103_07 = 1,17 m/s (normaler Gang), 16_21 = 1,69 m/s (zügiger Gang, Schritt 0,76 m),
            // 16_46 = 4,1 m/s, 102_10 = 4,4 m/s. Gehen (2,25 m/s) jetzt aus 16_21 mit Abspieltempo ≈ 1,3.
            ("Walk_Loop", "CMU_16_21_Gehen"), ("Jog_Fwd_Loop", "CMU_16_46_Joggen"), ("Sprint_Loop", "CMU_102_10_Rennen"),
            // 16.10. (3): Sprung aus echter Aufnahme (CMU 16_01, Sprung aus dem Stand): Absprung→kurz vor dem Aufsetzen als
            // Luft-Clip (JumpProgress), Aufsetzen→Stand als Landung. Die Quaternius-Pose (Arme hinterm Kopf, Beine an die Brust)
            // sah im Motion-Lab nicht nach Mensch aus.
            ("Jump_Start", "CMU_16_01_Sprung"), ("Jump_Land", "CMU_16_01_Landung"),
        };

        public static Dictionary<string, AnimationClip> LoadClips()
        {
            var result = new Dictionary<string, AnimationClip>();
            foreach (string path in new[] { QuaterniusImport.MaleModelPath, QuaterniusImport.Library2Path })
            {
                foreach (AnimationClip clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (clip.name.StartsWith("__preview__")) continue;
                    string key = QuaterniusModelPostprocessor.CleanClipName(clip.name);
                    if (!result.ContainsKey(key)) result.Add(key, clip);
                }
            }
            // MOV-127 (Max 14.10.: jede Bewegung aus echten Aufnahmen): Gehen, Joggen und Rennen aus der CMU-Bewegungsdatenbank
            // (Humanoid, Schleife auf der Stelle, Animations/Mocap/CMU) ersetzen die Quaternius-Clips, sobald sie im Projekt liegen.
            // Das Abspieltempo passt der CharacterMotionPoser zur Laufzeit an (LocoRate, Fußgleiten).
            if (ReineAufnahmen)
            {
                foreach (var (schluessel, datei) in ReineClips)
                {
                    AnimationClip c = AssetDatabase.LoadAllAssetsAtPath(ReinOrdner + "/" + datei + ".fbx").OfType<AnimationClip>()
                        .FirstOrDefault(x => !x.name.StartsWith("__preview__"));
                    if (c != null && c.isHumanMotion) result[schluessel] = c;
                    else Debug.LogWarning("[Ashveil] Reine Aufnahme fehlt: " + datei + " – bleibt UAL");
                }
            }
            if (NurUAL) return result;
            foreach (var (schluessel, cmu) in Aufnahmen)
            {
                bool sprung = schluessel.StartsWith("Jump");
                if (sprung ? !MocapSprung : !(MocapFortbewegung || MocapEinzeln.Contains(schluessel))) continue;
                string pfad = MocapOrdner + "/" + cmu + ".fbx";
                AnimationClip c = AssetDatabase.LoadAllAssetsAtPath(pfad).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__"));
                if (c != null && c.isHumanMotion) result[schluessel] = c;
            }
            foreach (var (schluessel, datei) in MpfbAufnahmen)
            {
                string fbx = datei.Split(':')[0], clipName = datei.Contains(":") ? datei.Split(':')[1] : null;   // „Datei:Clip“ für zerschnittene Dateien
                AnimationClip c = AssetDatabase.LoadAllAssetsAtPath(MpfbOrdner + "/" + fbx + ".fbx").OfType<AnimationClip>()
                    .FirstOrDefault(x => !x.name.StartsWith("__preview__") && (clipName == null || x.name == clipName));
                if (c != null && c.isHumanMotion) result[schluessel] = c;
            }
            return result;
        }

        private static AnimatorState AddState(AnimatorStateMachine sm, string name, Dictionary<string, AnimationClip> clips, string clipName)
        {
            AnimatorState s = sm.AddState(name);
            if (clips.TryGetValue(clipName, out AnimationClip clip)) s.motion = clip;
            else Debug.LogWarning($"[Ashveil] Animation '{clipName}' fehlt (Zustand '{name}').");
            return s;
        }

        private static void AddChild(BlendTree tree, Dictionary<string, AnimationClip> clips, string clipName, float threshold)
        {
            if (clips.TryGetValue(clipName, out AnimationClip clip)) tree.AddChild(clip, threshold);
            else Debug.LogWarning($"[Ashveil] Animation '{clipName}' fehlt im Blend Tree.");
        }

        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            return t;
        }
    }
}
