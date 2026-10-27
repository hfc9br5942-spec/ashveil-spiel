using System;
using System.Collections.Generic;
using Ashveil.Data;
using UnityEditor;
using UnityEngine;
using K = Ashveil.EditorTools.GreyboxKit;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// P1 (Max 30.09.): Klassenfaehigkeiten mit Raengen bis Stufe 20, Talente (3 Baeume je Klasse), Klassen-Ressourcen und
    /// Klassenlehrer im Startlager (Ilse → Eidwaechter, Gerrit → Aschenjaeger, Maelis → Aschenweber).
    /// Standardwerte werden geschrieben, solange ein Asset eine aeltere <see cref="BuilderVersion"/> traegt – danach im
    /// Inspector frei aenderbar (eine hoehere Version hier ueberschreibt wieder).
    /// </summary>
    public static class SpellContentBuilder
    {
        public const string SpellFolder = ProjectSetup.Root + "/Data/Abilities/Spells";
        public const string TalentFolder = ProjectSetup.Root + "/Data/Abilities/Talents";
        private const int BuilderVersion = 3; // 3: P2 Balance (Brandpfeil ab Stufe 2) // 2: Heldenhafter Schlag ohne Zielpflicht

        public const string Warden = "class.warden", Stalker = "class.ashstalker", Weaver = "class.emberweaver";

        // Farben der Symbole
        private static readonly Color Red = new Color(0.9f, 0.32f, 0.22f), Steel = new Color(0.72f, 0.76f, 0.82f), Gold = new Color(0.95f, 0.78f, 0.35f);
        private static readonly Color Ember = new Color(1f, 0.5f, 0.16f), Holy = new Color(1f, 0.9f, 0.55f), Green = new Color(0.45f, 0.8f, 0.35f), Blue = new Color(0.45f, 0.62f, 0.95f);

        public static void Build()
        {
            ProjectSetup.EnsureFolder(ProjectSetup.Root + "/Data/Abilities");
            ProjectSetup.EnsureFolder(SpellFolder);
            ProjectSetup.EnsureFolder(TalentFolder);
            Classes();
            WardenSpells();
            StalkerSpells();
            WeaverSpells();
            WardenTalents();
            StalkerTalents();
            WeaverTalents();
            Trainers();
            AssetDatabase.SaveAssets();
        }

        // ================================================================== Klassen und Lehrer

        private static void Classes()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:CharacterClassDefinition"))
            {
                var c = AssetDatabase.LoadAssetAtPath<CharacterClassDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (c == null) continue;
                switch (c.Id)
                {
                    case Warden: c.resource = ResourceType.Rage; c.talentTrees = new[] { "Waffen", "Eid", "Schutz" }; break;
                    case Stalker: c.resource = ResourceType.Energy; c.talentTrees = new[] { "Treffsicherheit", "Überleben", "Wildnis" }; break;
                    case Weaver: c.resource = ResourceType.Mana; c.talentTrees = new[] { "Glut", "Linderung", "Asche" }; break;
                    default: continue;
                }
                EditorUtility.SetDirty(c);
            }
        }

        private static void Trainers()
        {
            SetTrainer(QuestContentBuilder.Ilse, Warden);
            SetTrainer(QuestContentBuilder.Gerrit, Stalker);
            SetTrainer(QuestContentBuilder.Maelis, Weaver);
        }

        private static void SetTrainer(string npcId, string classId)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:NpcDefinition"))
            {
                var n = AssetDatabase.LoadAssetAtPath<NpcDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (n == null || n.Id != npcId) continue;
                n.trainerClass = classId;
                EditorUtility.SetDirty(n);
            }
        }

        // ================================================================== Eidwaechter (Wut)

        private static void WardenSpells()
        {
            Spell("Spell_HeroicStrike", "spell.warden_heroic_strike", "Heldenhafter Schlag", Warden, 1, IconShape.Sword, Red,
                "Dein nächster Schlag trifft mit {min} zusätzlichem Schaden. Kostet {cost} Wut, wenn er trifft. Nochmal drücken hebt ihn auf.",
                s => { s.startKnown = true; s.effect = SpellEffectKind.NextSwing; s.target = SpellTarget.Self; s.triggersGcd = false; s.requires = SpellRequirement.MeleeWeapon; s.threatMultiplier = 1.4f; },
                R(1, 0, 15, 11, 11), R(8, 200, 15, 21, 21), R(16, 1200, 15, 32, 32));
            Spell("Spell_Charge", "spell.warden_charge", "Ansturm", Warden, 2, IconShape.Boots, Gold,
                "Stürmt auf einen Gegner zu (8–25 m), betäubt ihn 1 s und erzeugt {extra} Wut. Nicht im Kampf.",
                s => { s.effect = SpellEffectKind.Charge; s.range = 25f; s.minRange = 8f; s.cooldown = 15f; s.outOfCombatOnly = true; s.stunSeconds = 1f; s.triggersGcd = false; },
                R(4, 80, 0, 0, 0, 9), R(18, 2000, 0, 0, 0, 15));
            Spell("Spell_ThunderClap", "spell.warden_thunder_clap", "Donnerschlag", Warden, 3, IconShape.Mace, Steel,
                "Erschüttert den Boden: {min}–{max} Schaden an allen Gegnern im Umkreis von 8 m und verlangsamt sie 10 s. Viel Bedrohung.",
                s => { s.effect = SpellEffectKind.SpellDamage; s.target = SpellTarget.AreaAroundSelf; s.radius = 8f; s.cooldown = 6f; s.slowFactor = 0.7f; s.slowSeconds = 10f; s.threatMultiplier = 1.75f; },
                R(6, 100, 20, 10, 13), R(12, 800, 20, 20, 25), R(18, 2000, 20, 32, 38));
            Spell("Spell_ShieldBash", "spell.warden_shield_bash", "Schildhieb", Warden, 4, IconShape.Shield, Steel,
                "Schlägt mit dem Schild zu: {min}–{max} Schaden, das Ziel ist 2 s betäubt. Braucht einen Schild.",
                s => { s.effect = SpellEffectKind.SpellDamage; s.school = SpellSchool.Physical; s.cooldown = 12f; s.stunSeconds = 2f; s.requires = SpellRequirement.Shield; s.bonusThreat = 40f; },
                R(8, 200, 10, 6, 9), R(16, 1200, 10, 14, 18));
            Spell("Spell_Taunt", "spell.warden_taunt", "Spott", Warden, 5, IconShape.Helmet, Red,
                "Zwingt das Ziel, dich {dur} s lang anzugreifen, und macht dich zu seinem gefährlichsten Gegner.",
                s => { s.effect = SpellEffectKind.None; s.range = 10f; s.cooldown = 8f; s.tauntSeconds = 3f; s.triggersGcd = false; },
                R(10, 400, 0, 0, 0, 0, 3));
            Spell("Spell_Fortify", "spell.warden_fortify", "Rüstung stärken", Warden, 6, IconShape.Chest, Steel,
                "Stemmt sich gegen alles: {extra} % weniger erlittener Schaden für {dur} s.",
                s => { s.effect = SpellEffectKind.DamageReduction; s.target = SpellTarget.Self; s.cooldown = 45f; },
                R(12, 800, 15, 0, 0, 20, 10), R(20, 2500, 15, 0, 0, 30, 10));
        }

        // ================================================================== Aschenjaeger (Energie)

        private static void StalkerSpells()
        {
            Spell("Spell_Feint", "spell.stalker_feint", "Finte", Stalker, 1, IconShape.Dagger, Red,
                "Eine Täuschung, dann der Stich: Waffenschaden plus {min}.",
                s => { s.startKnown = true; s.effect = SpellEffectKind.WeaponStrike; s.requires = SpellRequirement.MeleeWeapon; },
                R(1, 0, 40, 3, 3), R(6, 100, 40, 7, 7), R(14, 1000, 40, 12, 12));
            Spell("Spell_WingClip", "spell.stalker_wing_clip", "Zurechtstutzen", Stalker, 2, IconShape.Axe, Blue,
                "Schneidet dem Ziel in die Sehnen: Waffenschaden plus {min}, 50 % langsamer für {dur} s.",
                s => { s.effect = SpellEffectKind.WeaponStrike; s.requires = SpellRequirement.MeleeWeapon; s.slowFactor = 0.5f; s.slowSeconds = 10f; },
                R(2, 10, 25, 1, 1), R(16, 1200, 25, 6, 6));
            Spell("Spell_FireArrow", "spell.stalker_fire_arrow", "Brandpfeil", Stalker, 3, IconShape.Arrow, Ember,
                "Ein Pfeil mit brennender Spitze: Schuss plus {min}, danach brennt das Ziel {dur} s für insgesamt {extra} Schaden.",
                s => { s.effect = SpellEffectKind.RangedShot; s.school = SpellSchool.Fire; s.range = 30f; s.cooldown = 6f; s.dotSeconds = 9f; s.requires = SpellRequirement.RangedWeapon; },
                R(2, 10, 30, 4, 4, 12, 9), R(12, 800, 30, 8, 8, 28, 9), R(20, 2500, 30, 14, 14, 48, 9));
            Spell("Spell_Disengage", "spell.stalker_disengage", "Rückzug", Stalker, 4, IconShape.Boots, Green,
                "Springt mit einem Satz nach hinten – raus aus dem Nahkampf, rein in die Schussweite.",
                s => { s.effect = SpellEffectKind.LeapBack; s.target = SpellTarget.Self; s.cooldown = 20f; s.triggersGcd = false; },
                R(6, 100, 10, 0, 0));
            Spell("Spell_MultiShot", "spell.stalker_multi_shot", "Mehrfachschuss", Stalker, 5, IconShape.Quiver, Gold,
                "Drei Pfeile auf einmal: das Ziel und bis zu zwei Gegner in seiner Nähe (8 m), je Schuss plus {min}.",
                s => { s.effect = SpellEffectKind.RangedShot; s.range = 30f; s.cooldown = 10f; s.radius = 8f; s.maxTargets = 3; s.requires = SpellRequirement.RangedWeapon; },
                R(8, 200, 40, 6, 6), R(18, 2000, 40, 16, 16));
        }

        // ================================================================== Aschenweber (Mana)

        private static void WeaverSpells()
        {
            Spell("Spell_EmberBolt", "spell.weaver_ember_bolt", "Glutbolzen", Weaver, 1, IconShape.Focus, Ember,
                "Schleudert verdichtete Glut: {min}–{max} Feuerschaden.",
                s => { s.startKnown = true; s.effect = SpellEffectKind.SpellDamage; s.school = SpellSchool.Fire; s.range = 30f; s.castTime = 2f; },
                R(1, 0, 25, 18, 24), R(6, 100, 40, 32, 40), R(12, 800, 60, 50, 60), R(18, 2000, 85, 72, 86));
            Spell("Spell_Heal", "spell.weaver_heal", "Heilung", Weaver, 2, IconShape.Potion, Holy,
                "Webt Asche zu neuem Fleisch: heilt {min}–{max}. Ohne befreundetes Ziel heilst du dich selbst.",
                s => { s.startKnown = true; s.effect = SpellEffectKind.Heal; s.target = SpellTarget.Friendly; s.school = SpellSchool.Ash; s.range = 40f; s.castTime = 2.5f; },
                R(1, 0, 30, 46, 56), R(8, 200, 60, 92, 110), R(14, 1000, 100, 160, 190), R(20, 2500, 140, 250, 290));
            Spell("Spell_AshShield", "spell.weaver_ash_shield", "Aschenschild", Weaver, 3, IconShape.Shield, Gold,
                "Ein Mantel aus Asche fängt {extra} Schaden ab ({dur} s). Dasselbe Ziel kann erst nach 15 s wieder einen Schild tragen.",
                s => { s.effect = SpellEffectKind.Absorb; s.target = SpellTarget.Friendly; s.school = SpellSchool.Ash; s.range = 40f; s.cooldown = 4f; s.reapplyLockSeconds = 15f; },
                R(4, 50, 45, 0, 0, 44, 30), R(10, 400, 80, 0, 0, 90, 30), R(16, 1200, 120, 0, 0, 150, 30));
            Spell("Spell_FlashHeal", "spell.weaver_flash_heal", "Schnelle Heilung", Weaver, 4, IconShape.Herb, Holy,
                "Schnell, aber teuer: heilt {min}–{max}.",
                s => { s.effect = SpellEffectKind.Heal; s.target = SpellTarget.Friendly; s.school = SpellSchool.Ash; s.range = 40f; s.castTime = 1.5f; },
                R(10, 400, 70, 82, 96), R(18, 2000, 115, 145, 170));
            Spell("Spell_FlameWave", "spell.weaver_flame_wave", "Flammenwelle", Weaver, 5, IconShape.Gem, Ember,
                "Eine Welle aus Glut rollt von dir weg: {min}–{max} Feuerschaden an allen Gegnern im Umkreis von 8 m.",
                s => { s.effect = SpellEffectKind.SpellDamage; s.target = SpellTarget.AreaAroundSelf; s.school = SpellSchool.Fire; s.radius = 8f; s.cooldown = 8f; },
                R(12, 800, 60, 22, 28), R(20, 2500, 95, 40, 48));
            Spell("Spell_RestOfAsh", "spell.weaver_rest_of_ash", "Ruhe der Asche", Weaver, 6, IconShape.Letter, Holy,
                "Ruft einen gefallenen Mitspieler zurück – mit {extra} % seines Lebens. Nicht im Kampf.",
                s => { s.effect = SpellEffectKind.Resurrect; s.target = SpellTarget.DeadFriendly; s.school = SpellSchool.Ash; s.range = 30f; s.castTime = 5f; s.outOfCombatOnly = true; },
                R(14, 1000, 120, 0, 0, 35));
        }

        // ================================================================== Talente

        private static void WardenTalents()
        {
            string c = Warden;
            // Waffen
            Talent("Talent_W_HeavyHand", "talent.warden_heavy_hand", "Schwere Hand", c, 0, 0, 0, 5, TalentStat.MeleeDamagePercent, 2f, null, IconShape.Sword, Red, "+{v} % Nahkampfschaden.");
            Talent("Talent_W_DeepWounds", "talent.warden_deep_wounds", "Tiefe Wunden", c, 0, 0, 1, 5, TalentStat.CritPercent, 1f, null, IconShape.Axe, Red, "+{v} % Chance auf kritische Treffer.");
            Talent("Talent_W_HeroicMight", "talent.warden_heroic_might", "Heldenhafte Wucht", c, 0, 1, 0, 3, TalentStat.SpellPowerPercent, 10f, "spell.warden_heroic_strike", IconShape.Sword, Gold, "Heldenhafter Schlag trifft {v} % stärker.");
            Talent("Talent_W_Onslaught", "talent.warden_onslaught", "Wuchtiger Ansturm", c, 0, 1, 1, 2, TalentStat.EffectDurationBonus, 0.5f, "spell.warden_charge", IconShape.Boots, Gold, "Ansturm betäubt {v} s länger.");
            Talent("Talent_W_Mastery", "talent.warden_weapon_mastery", "Klingenmeister", c, 0, 2, 0, 1, TalentStat.MeleeDamagePercent, 6f, null, IconShape.Sword, Steel, "+{v} % Nahkampfschaden.");
            // Eid
            Talent("Talent_W_Wrath", "talent.warden_righteous_wrath", "Gerechter Zorn", c, 1, 0, 0, 5, TalentStat.RageGainPercent, 8f, null, IconShape.Gem, Red, "+{v} % Wut durch Schlagen und Getroffenwerden.");
            Talent("Talent_W_ImpHeroic", "talent.warden_improved_heroic", "Verbesserter Heldenhafter Schlag", c, 1, 0, 1, 3, TalentStat.SpellCostReduction, 1f, "spell.warden_heroic_strike", IconShape.Sword, Red, "Heldenhafter Schlag kostet {v} Wut weniger.");
            Talent("Talent_W_Thunder", "talent.warden_rolling_thunder", "Rollender Donner", c, 1, 1, 0, 3, TalentStat.SpellPowerPercent, 10f, "spell.warden_thunder_clap", IconShape.Mace, Steel, "Donnerschlag trifft {v} % stärker.");
            Talent("Talent_W_Endurance", "talent.warden_endurance", "Langer Atem", c, 1, 1, 1, 3, TalentStat.HealthPercent, 2f, null, IconShape.Chest, Green, "+{v} % Leben.");
            Talent("Talent_W_Oath", "talent.warden_iron_oath", "Eiserner Eid", c, 1, 2, 0, 1, TalentStat.HealthPercent, 5f, null, IconShape.Helmet, Gold, "+{v} % Leben.");
            // Schutz
            Talent("Talent_W_ShieldLore", "talent.warden_shield_lore", "Schildkunde", c, 2, 0, 0, 5, TalentStat.BlockPercent, 1f, null, IconShape.Shield, Steel, "+{v} % Blockchance mit Schild.");
            Talent("Talent_W_Toughness", "talent.warden_toughness", "Zähigkeit", c, 2, 0, 1, 5, TalentStat.ArmorPercent, 2f, null, IconShape.Chest, Steel, "+{v} % Rüstung.");
            Talent("Talent_W_Defiance", "talent.warden_defiance", "Herausforderung", c, 2, 1, 0, 3, TalentStat.ThreatPercent, 5f, null, IconShape.Helmet, Red, "+{v} % Bedrohung.");
            Talent("Talent_W_Bulwark", "talent.warden_bulwark", "Bollwerk", c, 2, 1, 1, 2, TalentStat.CooldownReduction, 10f, "spell.warden_fortify", IconShape.Shield, Gold, "Rüstung stärken ist {v} s früher wieder bereit.");
            Talent("Talent_W_LastStand", "talent.warden_last_stand", "Letztes Gefecht", c, 2, 2, 0, 1, TalentStat.HealthPercent, 8f, null, IconShape.Shield, Gold, "+{v} % Leben.");
        }

        private static void StalkerTalents()
        {
            string c = Stalker;
            // Treffsicherheit
            Talent("Talent_S_SteadyAim", "talent.stalker_steady_aim", "Ruhige Hand", c, 0, 0, 0, 5, TalentStat.RangedDamagePercent, 2f, null, IconShape.Bow, Gold, "+{v} % Fernkampfschaden.");
            Talent("Talent_S_KeenEye", "talent.stalker_keen_eye", "Scharfes Auge", c, 0, 0, 1, 5, TalentStat.CritPercent, 1f, null, IconShape.Arrow, Gold, "+{v} % Chance auf kritische Treffer.");
            Talent("Talent_S_Barrage", "talent.stalker_barrage", "Pfeilhagel", c, 0, 1, 0, 3, TalentStat.CooldownReduction, 1f, "spell.stalker_multi_shot", IconShape.Quiver, Gold, "Mehrfachschuss ist {v} s früher wieder bereit.");
            Talent("Talent_S_Kindling", "talent.stalker_kindling", "Zunder", c, 0, 1, 1, 3, TalentStat.SpellPowerPercent, 10f, "spell.stalker_fire_arrow", IconShape.Arrow, Ember, "Brandpfeil trifft und brennt {v} % stärker.");
            Talent("Talent_S_Hawkeye", "talent.stalker_hawkeye", "Falkenblick", c, 0, 2, 0, 1, TalentStat.RangedDamagePercent, 6f, null, IconShape.Bow, Gold, "+{v} % Fernkampfschaden.");
            // Ueberleben
            Talent("Talent_S_Evasion", "talent.stalker_evasion", "Ausweichen", c, 1, 0, 0, 5, TalentStat.DodgePercent, 1f, null, IconShape.Boots, Green, "+{v} % Chance, Nahkampfschlägen auszuweichen.");
            Talent("Talent_S_Blades", "talent.stalker_twin_blades", "Zwillingsklingen", c, 1, 0, 1, 5, TalentStat.MeleeDamagePercent, 2f, null, IconShape.Dagger, Red, "+{v} % Nahkampfschaden.");
            Talent("Talent_S_Hamstring", "talent.stalker_hamstring", "Sehnenschnitt", c, 1, 1, 0, 2, TalentStat.EffectDurationBonus, 2f, "spell.stalker_wing_clip", IconShape.Axe, Blue, "Zurechtstutzen verlangsamt {v} s länger.");
            Talent("Talent_S_Nimble", "talent.stalker_nimble", "Leichtfüßig", c, 1, 1, 1, 2, TalentStat.CooldownReduction, 3f, "spell.stalker_disengage", IconShape.Boots, Green, "Rückzug ist {v} s früher wieder bereit.");
            Talent("Talent_S_Catlike", "talent.stalker_catlike", "Katzenhaft", c, 1, 2, 0, 1, TalentStat.DodgePercent, 3f, null, IconShape.Boots, Gold, "+{v} % Chance, Nahkampfschlägen auszuweichen.");
            // Wildnis
            Talent("Talent_S_PackBond", "talent.stalker_pack_bond", "Rudelband", c, 2, 0, 0, 5, TalentStat.CompanionDamagePercent, 3f, null, IconShape.Hide, Green, "Dein Gefährte macht {v} % mehr Schaden.");
            Talent("Talent_S_Stamina", "talent.stalker_second_wind", "Zweiter Atem", c, 2, 0, 1, 5, TalentStat.ResourceRegenPercent, 4f, null, IconShape.Food, Gold, "Energie füllt sich {v} % schneller.");
            Talent("Talent_S_Hardened", "talent.stalker_hardened", "Abgehärtet", c, 2, 1, 0, 3, TalentStat.HealthPercent, 2f, null, IconShape.Chest, Green, "+{v} % Leben.");
            Talent("Talent_S_QuickFeint", "talent.stalker_quick_feint", "Flinke Finte", c, 2, 1, 1, 3, TalentStat.SpellCostReduction, 2f, "spell.stalker_feint", IconShape.Dagger, Red, "Finte kostet {v} Energie weniger.");
            Talent("Talent_S_PackLeader", "talent.stalker_pack_leader", "Rudelführer", c, 2, 2, 0, 1, TalentStat.CompanionDamagePercent, 10f, null, IconShape.Hide, Gold, "Dein Gefährte macht {v} % mehr Schaden.");
        }

        private static void WeaverTalents()
        {
            string c = Weaver;
            // Glut
            Talent("Talent_E_Fervor", "talent.weaver_fervor", "Glutkraft", c, 0, 0, 0, 5, TalentStat.SpellDamagePercent, 2f, null, IconShape.Focus, Ember, "+{v} % Zauberschaden.");
            Talent("Talent_E_QuickBolt", "talent.weaver_quick_bolt", "Rascher Bolzen", c, 0, 0, 1, 5, TalentStat.CastTimeReduction, 0.1f, "spell.weaver_ember_bolt", IconShape.Focus, Gold, "Glutbolzen wirkt {v} s schneller.");
            Talent("Talent_E_Wildfire", "talent.weaver_wildfire", "Flächenbrand", c, 0, 1, 0, 3, TalentStat.SpellPowerPercent, 10f, "spell.weaver_flame_wave", IconShape.Gem, Ember, "Flammenwelle trifft {v} % stärker.");
            Talent("Talent_E_Spark", "talent.weaver_spark", "Funkenflug", c, 0, 1, 1, 3, TalentStat.CritPercent, 1f, null, IconShape.Gem, Gold, "+{v} % Chance auf kritische Treffer.");
            Talent("Talent_E_EmberHeart", "talent.weaver_ember_heart", "Glutherz", c, 0, 2, 0, 1, TalentStat.SpellDamagePercent, 6f, null, IconShape.Focus, Red, "+{v} % Zauberschaden.");
            // Linderung
            Talent("Talent_E_Mending", "talent.weaver_mending", "Heilende Hände", c, 1, 0, 0, 5, TalentStat.HealingPercent, 2f, null, IconShape.Potion, Holy, "+{v} % Heilung.");
            Talent("Talent_E_QuickHeal", "talent.weaver_swift_mending", "Geübte Heilung", c, 1, 0, 1, 5, TalentStat.CastTimeReduction, 0.1f, "spell.weaver_heal", IconShape.Potion, Gold, "Heilung wirkt {v} s schneller.");
            Talent("Talent_E_Thrift", "talent.weaver_thrift", "Sparsamkeit", c, 1, 1, 0, 3, TalentStat.SpellCostReduction, 5f, "spell.weaver_flash_heal", IconShape.Herb, Green, "Schnelle Heilung kostet {v} Mana weniger.");
            Talent("Talent_E_Meditation", "talent.weaver_meditation", "Innere Ruhe", c, 1, 1, 1, 3, TalentStat.ResourceRegenPercent, 10f, null, IconShape.Herb, Blue, "Mana erholt sich {v} % schneller.");
            Talent("Talent_E_Serenity", "talent.weaver_serenity", "Stille Hände", c, 1, 2, 0, 1, TalentStat.HealingPercent, 6f, null, IconShape.Potion, Gold, "+{v} % Heilung.");
            // Asche
            Talent("Talent_E_Wellspring", "talent.weaver_wellspring", "Tiefe Quelle", c, 2, 0, 0, 5, TalentStat.ManaPercent, 2f, null, IconShape.Gem, Blue, "+{v} % Mana.");
            Talent("Talent_E_Ashward", "talent.weaver_ash_ward", "Aschenwall", c, 2, 0, 1, 5, TalentStat.SpellPowerPercent, 5f, "spell.weaver_ash_shield", IconShape.Shield, Gold, "Aschenschild fängt {v} % mehr ab.");
            Talent("Talent_E_Cinderskin", "talent.weaver_cinder_skin", "Glühende Haut", c, 2, 1, 0, 3, TalentStat.ArmorPercent, 5f, null, IconShape.Chest, Ember, "+{v} % Rüstung.");
            Talent("Talent_E_Vigor", "talent.weaver_vigor", "Lebenskraft", c, 2, 1, 1, 3, TalentStat.HealthPercent, 2f, null, IconShape.Chest, Green, "+{v} % Leben.");
            Talent("Talent_E_AshBond", "talent.weaver_ash_bond", "Aschenband", c, 2, 2, 0, 1, TalentStat.SpellPowerPercent, 15f, "spell.weaver_ash_shield", IconShape.Shield, Holy, "Aschenschild fängt {v} % mehr ab.");
        }

        // ================================================================== Hilfen

        private static SpellRank R(int level, int trainCost, int cost, float min, float max, float extra = 0f, float duration = 0f) =>
            new SpellRank { level = level, trainCost = trainCost, cost = cost, min = min, max = max, extra = extra, duration = duration };

        private static SpellDefinition Spell(string file, string id, string name, string classId, int order, IconShape shape, Color color, string description,
            Action<SpellDefinition> setup, params SpellRank[] ranks)
        {
            string path = SpellFolder + "/" + file + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<SpellDefinition>(path);
            bool fresh = def == null;
            if (fresh)
            {
                def = ScriptableObject.CreateInstance<SpellDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }
            if (fresh || def.defaultsVersion < BuilderVersion)
            {
                // Grundwerte zuruecksetzen, dann setzen
                def.startKnown = false; def.effect = SpellEffectKind.WeaponStrike; def.target = SpellTarget.Enemy; def.school = SpellSchool.Physical;
                def.castTime = 0f; def.cooldown = 0f; def.triggersGcd = true; def.range = 0f; def.minRange = 0f; def.outOfCombatOnly = false;
                def.requires = SpellRequirement.None; def.radius = 0f; def.maxTargets = 0; def.stunSeconds = 0f; def.slowFactor = 0f; def.slowSeconds = 0f;
                def.tauntSeconds = 0f; def.dotSeconds = 0f; def.threatMultiplier = 1f; def.bonusThreat = 0f; def.reapplyLockSeconds = 0f;
                def.classId = classId;
                def.sortOrder = order;
                def.iconShape = shape;
                def.iconColor = color;
                setup?.Invoke(def);
                def.ranks = new List<SpellRank>(ranks);
                def.defaultsVersion = BuilderVersion;
                K.SetString(def, "id", id);
                K.SetString(def, "displayName", name);
                K.SetString(def, "description", description);
                EditorUtility.SetDirty(def);
            }
            return def;
        }

        private static void Talent(string file, string id, string name, string classId, int tree, int tier, int column, int maxRank, TalentStat stat,
            float perRank, string spellId, IconShape shape, Color color, string description)
        {
            string path = TalentFolder + "/" + file + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<TalentDefinition>(path);
            bool fresh = def == null;
            if (fresh)
            {
                def = ScriptableObject.CreateInstance<TalentDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }
            if (!fresh && def.defaultsVersion >= BuilderVersion) return;
            def.classId = classId;
            def.tree = tree;
            def.tier = tier;
            def.column = column;
            def.maxRank = maxRank;
            def.stat = stat;
            def.perRank = perRank;
            def.spellId = spellId ?? "";
            def.iconShape = shape;
            def.iconColor = color;
            def.defaultsVersion = BuilderVersion;
            K.SetString(def, "id", id);
            K.SetString(def, "displayName", name);
            K.SetString(def, "description", description);
            EditorUtility.SetDirty(def);
        }
    }
}
