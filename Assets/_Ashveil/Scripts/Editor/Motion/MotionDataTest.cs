using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ashveil.Core;
using Ashveil.Data;
using Ashveil.Data.Motion;
using Ashveil.Gameplay.Motion;
using UnityEditor;
using UnityEngine;

namespace Ashveil.EditorTools
{
    /// <summary>
    /// Pruefung des Motion-Datenmodells und der Angriffsauswahl ohne Play-Modus (Phase M1).
    /// Bericht: Logs/Ashveil/Ashveil_MotionDatenTest.txt.
    /// </summary>
    public static class MotionDataTest
    {
        private static StringBuilder _sb;
        private static int _ok, _fail;

        [MenuItem("Ashveil/Test/Motion-Daten (ohne Play)", priority = 360)]
        public static void RunMenu()
        {
            string r = Run();
            Debug.Log("[Ashveil] " + r.Split('\n').Last(l => l.StartsWith("ERGEBNIS")));
            EditorUtility.DisplayDialog("Ashveil – Motion-Daten", r.Length > 1500 ? r.Substring(r.Length - 1500) : r, "OK");
        }

        public static string Run()
        {
            _sb = new StringBuilder();
            _ok = _fail = 0;
            Line($"ASHVEIL Motion-Daten (Phase M1, ohne Play) · {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            MotionDatabaseBuilder.Build();
            MotionDatabase db = AssetDatabase.LoadAssetAtPath<MotionDatabase>(MotionDatabaseBuilder.DatabasePath);
            Check(db != null && Resources.Load<MotionDatabase>(MotionDatabase.ResourcePath) == db, "Datenbank liegt unter Resources/Motion und ist ladbar");
            if (db == null) return Finish();
            db.Invalidate();

            Line("");
            Line("[Schichten]");
            ResolvedMotionTuning tank = db.Resolve(RaceCatalog.HumanId, BodyMotionVariant.Masculine, ClassRole.Tank, ArmorCategory.Heavy, WeaponFamily.SwordShield);
            Check(tank.IsValid && string.IsNullOrEmpty(tank.fallbackNote) && tank.weapon != null && tank.weapon.family == WeaponFamily.SwordShield,
                "Mensch · Tank · schwer · Schwert+Schild: alle Schichten ohne Rueckfall");
            Check(tank.modifiers.armSwingScale < 0.8f && tank.modifiers.massInertia > 1.2f && tank.modifiers.upperBodyMotion < 0.8f && tank.modifiers.pelvisStability > 0.2f,
                $"Tank schwer: weniger Armschwung ({tank.modifiers.armSwingScale:0.00}), mehr Masse ({tank.modifiers.massInertia:0.00}), ruhiger Oberkoerper ({tank.modifiers.upperBodyMotion:0.00}), stabiles Becken ({tank.modifiers.pelvisStability:0.00})");
            ResolvedMotionTuning tankF = db.Resolve(RaceCatalog.HumanId, BodyMotionVariant.Feminine, ClassRole.Tank, ArmorCategory.Heavy, WeaponFamily.SwordShield);
            ResolvedMotionTuning dps = db.Resolve(RaceCatalog.HumanId, BodyMotionVariant.Masculine, ClassRole.Damage, ArmorCategory.Light, WeaponFamily.SwordShield);
            float bodyDiff = Diff(tank.modifiers, tankF.modifiers), roleDiff = Diff(tank.modifiers, dps.modifiers);
            Check(bodyDiff > 0.01f && roleDiff > bodyDiff * 2f,
                $"Tank Mann/Frau unterscheiden sich ({bodyDiff:0.00}), Klasse+Ruestung staerker als Koerper ({roleDiff:0.00}) (§19/§111)");
            ResolvedMotionTuning nharim = db.Resolve("race.nharim", BodyMotionVariant.Masculine, ClassRole.Tank, ArmorCategory.Heavy, WeaponFamily.SwordShield);
            Check(nharim.race != null && nharim.race.provisional, "Nharim: Rassenprofil vorbereitet (vorlaeufig, nicht ausentwickelt)");
            ResolvedMotionTuning gun = db.Resolve(RaceCatalog.HumanId, BodyMotionVariant.Masculine, ClassRole.Damage, ArmorCategory.Light, WeaponFamily.Greatsword);
            Check(gun.weapon != null && gun.fallbackNote.Contains("Waffe"), $"Fehlende Waffenfamilie faellt zurueck und wird gemeldet („{gun.fallbackNote}“)");

            Line("");
            Line("[Zuordnung]");
            Check(MotionMapping.FamilyOf(WeaponClass.Sword, true) == WeaponFamily.SwordShield && MotionMapping.FamilyOf(WeaponClass.Sword, false) == WeaponFamily.OneHandSword &&
                  MotionMapping.FamilyOf(WeaponClass.Pistol, false) == WeaponFamily.Pistol, "Waffenklasse → Familie (Schwert mit/ohne Schild, Pistole)");
            Check(MotionMapping.CategoryOf(ArmorType.Plate) == ArmorCategory.Heavy && MotionMapping.CategoryOf(ArmorType.Cloth) == ArmorCategory.Light, "Ruestungsart → Kategorie");
            Check(MotionMapping.SizeOfHeight(0.6f) == TargetSizeClass.Small && MotionMapping.SizeOfHeight(1.8f) == TargetSizeClass.Humanoid &&
                  MotionMapping.SizeOfHeight(6f) == TargetSizeClass.Huge && MotionMapping.SizeOfHeight(20f) == TargetSizeClass.Titanic, "Zielgroesse aus Hoehe");

            Line("");
            Line("[Clips]");
            var ids = db.clips.Select(c => c.NetId).ToList();
            Check(ids.Count == ids.Distinct().Count(), $"Netz-IDs eindeutig ({ids.Count} Clips)");
            int missing = db.clips.Count(c => c.status == MotionAssetStatus.Missing), placeholder = db.clips.Count(c => c.status == MotionAssetStatus.Placeholder);
            Check(missing > 0 || placeholder > 0, $"Fehlende ({missing}) und Platzhalter-Clips ({placeholder}) sind eingetragen, nichts fehlt still (§114/§143)");
            Check(db.clips.Where(c => c.status >= MotionAssetStatus.Retargeted).All(c => c.clip != null), "Jeder als vorhanden markierte Eintrag hat einen Clip");
            Check(db.clips.Where(c => c.weaponFamily == WeaponFamily.SwordShield && c.category == MotionCategory.Attack).All(c => !c.mirrorAllowed),
                "Schwert+Schild-Angriffe duerfen nicht gespiegelt werden (§105)");

            Line("");
            Line("[Angriffsauswahl]");
            List<AttackMotionDefinition> atk = tank.weapon.attacks;
            var auto = atk.Where(a => !a.abilityOnly && a.motion != null && a.motion.clip != null).ToList();
            Check(auto.Count >= 5, $"Mindestens 5 Standardangriffe fuer Schwert+Schild ({auto.Count}: {string.Join(", ", auto.Select(a => a.label))})");

            AttackQuery human = Query(1.8f, TargetSizeClass.Humanoid, 1.1f, 1.8f, 0f, 0.8f, 1);
            string seqA = Sequence(atk, human, 20, 7), seqB = Sequence(atk, human, 20, 7);
            Check(seqA == seqB, "Deterministisch: gleiche Lage + gleiche Seeds → gleiche Folge");

            var sel = new ContextualAttackSelector();
            var picks = new List<AttackMotionDefinition>();
            for (int i = 0; i < 40; i++)
            {
                AttackQuery q = human; q.Seed = MotionRandom.Mix(99, (uint)i);
                AttackChoice c = sel.Select(atk, q);
                if (c.Valid) { picks.Add(c.Definition); sel.Remember(c.Definition); }
            }
            int repeats = 0;
            for (int i = 1; i < picks.Count; i++) if (picks[i] == picks[i - 1]) repeats++;
            int distinct = picks.Distinct().Count();
            Check(picks.Count == 40 && repeats <= 2, $"Kaum direkte Wiederholung: {repeats} in 40 Angriffen (§28)");
            Check(distinct >= 4, $"Abwechslung gegen Menschengroesse: {distinct} verschiedene Bewegungen");
            Check(picks.All(p => !p.abilityOnly), "Automatikangriff nimmt keine Faehigkeits-Bewegung");

            AttackQuery head8m = Query(1.8f, TargetSizeClass.Titanic, 8f, 2f, 0f, 0.8f, 3);
            AttackChoice none = new ContextualAttackSelector().Select(atk, head8m);
            Check(!none.Valid && none.Note == "Ziel zu hoch", $"Kopf in 8 m Hoehe: keine Nahkampfbewegung („{none.Note}“) – kein Vortaeuschen (§30)");

            float leg = Share(atk, Query(1.8f, TargetSizeClass.Huge, 0.3f, 2f, 0f, 0.8f, 4), a => a.zone == BodyZone.Low);
            Check(leg >= 0.6f, $"Grosses Wesen, Ziel Bein (0,3 m): Anteil tiefer Bewegungen {leg:0.00} (§128)");
            float high = Share(atk, Query(1.8f, TargetSizeClass.Humanoid, 1.65f, 1.9f, 0f, 0.8f, 5), a => a.zone == BodyZone.High);
            Check(high >= 0.5f, $"Hohes Ziel (1,65 m): Anteil hoher Bewegungen {high:0.00}");

            float fastQuick = (Share(atk, Query(1.8f, TargetSizeClass.Humanoid, 1.1f, 1.8f, 5f, 0.8f, 6), a => a.quick));
            float slowQuick = (Share(atk, Query(1.8f, TargetSizeClass.Large, 1.1f, 1.8f, 0f, 0.8f, 6), a => a.quick));
            Check(fastQuick > slowQuick, $"Schnelles Ziel → kurze, kontrollierte Bewegungen ({fastQuick:0.00} gegenueber {slowQuick:0.00}) (§71)");

            AttackQuery wallRight = human; wallRight.ClearRight = 0.3f;
            float rightFree = (Share(atk, human, a => a.swingSide > 0.3f));
            float rightWall = (Share(atk, wallRight, a => a.swingSide > 0.3f));
            Check(rightWall < rightFree * 0.5f, $"Wand rechts: Schlaege nach rechts seltener ({rightWall:0.00} statt {rightFree:0.00}) (§69)");

            var flow = new ContextualAttackSelector();
            AttackMotionDefinition diag = atk.First(a => a.label == "Diagonal rechts"), back = atk.First(a => a.label == "Rueckhand links");
            flow.Remember(diag);
            float wBack = flow.Score(back, human, out _), wBackCold = new ContextualAttackSelector().Score(back, human, out _);
            Check(wBack > wBackCold, $"Fluss: nach „Diagonal rechts“ passt „Rueckhand links“ besser ({wBack:0.00} statt {wBackCold:0.00}) (§121)");

            AttackQuery econ = human; econ.Economy = 0.9f;
            AttackQuery flashy = human; flashy.Economy = 0.1f;
            float cTank = (Share(atk, econ, a => a.committed)), cFlashy = (Share(atk, flashy, a => a.committed));
            Check(cTank <= cFlashy, $"Tank-Oekonomie: weniger ausladende Schlaege ({cTank:0.00} gegenueber {cFlashy:0.00}) (§127)");

            Line("");
            Line("[Architektur]");
            string asm = File.ReadAllText(Path.Combine(ProjectPaths.ProjectRoot, "Assets/_Ashveil/Scripts/Gameplay/Ashveil.Gameplay.asmdef"));
            Check(!asm.Contains("FishNet"), "Gameplay-Assembly ohne FishNet-Abhaengigkeit");
            return Finish();
        }

        // ------------------------------------------------------------------ Helfer

        private static AttackQuery Query(float bodyH, TargetSizeClass size, float pointHeight, float dist, float speed, float economy, uint seed) => new AttackQuery
        {
            HasTarget = true, BodyHeight = bodyH, TargetSize = size, TargetPointHeight = pointHeight, TargetDistance = dist,
            TargetSpeed = speed, ClearLeft = 99f, ClearRight = 99f, Economy = economy, Seed = seed
        };

        private static string Sequence(List<AttackMotionDefinition> atk, AttackQuery q, int n, uint seed)
        {
            var sel = new ContextualAttackSelector();
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                q.Seed = MotionRandom.Mix(seed, (uint)i);
                AttackChoice c = sel.Select(atk, q);
                sb.Append(c.Valid ? c.Definition.NetId.ToString() : "-").Append(',');
                if (c.Valid) sel.Remember(c.Definition);
            }
            return sb.ToString();
        }

        /// <summary>Anteil der Wahlen (ohne Verlauf, 200 Seeds), die die Bedingung erfuellen – 0..1.</summary>
        private static float Share(List<AttackMotionDefinition> atk, AttackQuery q, System.Func<AttackMotionDefinition, bool> pred)
        {
            int hit = 0, n = 0;
            for (int i = 0; i < 200; i++)
            {
                q.Seed = MotionRandom.Mix(4242, (uint)i);
                AttackChoice c = new ContextualAttackSelector().Select(atk, q);
                if (!c.Valid) continue;
                n++;
                if (pred(c.Definition)) hit++;
            }
            return n > 0 ? hit / (float)n : 0f;
        }

        private static float Diff(in MotionModifiers a, in MotionModifiers b) =>
            Mathf.Abs(a.strideScale - b.strideScale) + Mathf.Abs(a.armSwingScale - b.armSwingScale) + Mathf.Abs(a.upperBodyMotion - b.upperBodyMotion) +
            Mathf.Abs(a.massInertia - b.massInertia) + Mathf.Abs(a.turnInertia - b.turnInertia) + Mathf.Abs(a.landingWeight - b.landingWeight) +
            Mathf.Abs(a.pelvisStability - b.pelvisStability) + Mathf.Abs(a.shoulderPelvisRhythm - b.shoulderPelvisRhythm) * 0.05f;

        private static void Check(bool ok, string label)
        {
            if (ok) _ok++; else _fail++;
            Line((ok ? "  OK    " : "  FEHLER ") + label);
        }

        private static void Line(string s) => _sb.AppendLine(s);

        private static string Finish()
        {
            Line("");
            Line($"ERGEBNIS: {_ok} bestanden, {_fail} fehlgeschlagen");
            string path = ProjectPaths.Logs("Ashveil_MotionDatenTest.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, _sb.ToString(), new UTF8Encoding(false));
            return _sb.ToString();
        }
    }
}
