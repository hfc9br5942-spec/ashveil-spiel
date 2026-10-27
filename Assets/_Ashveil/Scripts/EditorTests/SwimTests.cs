using System.Collections.Generic;
using Ashveil.Core;
using Ashveil.Gameplay.Characters;
using NUnit.Framework;
using UnityEngine;

namespace Ashveil.EditorTests
{
    /// <summary>
    /// Schwimmen und Tauchen (MOV-169, Welle 0): Regeln ohne Szene – Schwimmschwelle mit Hysterese, Atem, Ertrinken,
    /// Tauchwinkel, Schwimmhöhe und die Abfrage der Wasserflächen. Der Motor selbst wird im Spieltest geprüft.
    /// </summary>
    public sealed class SwimTests
    {
        private static readonly CharacterMotorSettings S = new CharacterMotorSettings();

        [Test]
        public void Swimming_UsesHysteresisAtTheShore()
        {
            Assert.IsFalse(SwimRules.IsSwimming(false, 1.2f, S.swimEnterDepth, S.swimExitDepth), "brusttief noch nicht");
            Assert.IsTrue(SwimRules.IsSwimming(false, 1.3f, S.swimEnterDepth, S.swimExitDepth), "ab 1,3 m schwimmen");
            Assert.IsTrue(SwimRules.IsSwimming(true, 1.2f, S.swimEnterDepth, S.swimExitDepth), "beim Hinausschwimmen erst unter 1,1 m gehen");
            Assert.IsFalse(SwimRules.IsSwimming(true, 1.05f, S.swimEnterDepth, S.swimExitDepth));
            Assert.IsFalse(SwimRules.IsSwimming(true, -1f, S.swimEnterDepth, S.swimExitDepth), "außerhalb jeder Wasserfläche");
            Assert.Less(S.swimExitDepth, S.swimEnterDepth);
            Assert.Less(S.swimFloatDepth, S.swimHeadHeight, "an der Oberfläche bleibt der Kopf über Wasser");
        }

        [Test]
        public void Breath_RunsOutUnderwaterAndRefillsAtTheSurface()
        {
            float b = S.breathSeconds;
            Assert.AreEqual(90f, b, 1e-4f, "Max 06.10.: 90 s Luft (Classic: 60 s)");
            b = SwimRules.Breath(b, true, 10f, S.breathSeconds, S.breathRefillPerSecond);
            Assert.AreEqual(80f, b, 1e-4f);
            b = SwimRules.Breath(b, true, 100f, S.breathSeconds, S.breathRefillPerSecond);
            Assert.AreEqual(0f, b, 1e-4f, "nie unter 0");
            b = SwimRules.Breath(b, false, 3f, S.breathSeconds, S.breathRefillPerSecond);
            Assert.AreEqual(30f, b, 1e-4f, "an der Luft 10 s Atem je Sekunde");
            b = SwimRules.Breath(b, false, 10f, S.breathSeconds, S.breathRefillPerSecond);
            Assert.AreEqual(90f, b, 1e-4f, "nie über den Vorrat");
            Assert.IsTrue(SwimRules.HeadUnder(1.7f, S.swimHeadHeight));
            Assert.IsFalse(SwimRules.HeadUnder(S.swimFloatDepth, S.swimHeadHeight), "Schwimmhöhe: Kopf über Wasser");
        }

        [Test]
        public void Drowning_TakesAShareOfMaxHealth()
        {
            Assert.AreEqual(10f, SwimRules.DrownDamage(100f, 0.1f));
            Assert.AreEqual(1f, SwimRules.DrownDamage(4f, 0.1f), "mindestens 1");
            Assert.AreEqual(35f, SwimRules.DrownDamage(350f, S.drownFraction));
            Assert.AreEqual(2f, S.drownInterval, 1e-4f);
        }

        [Test]
        public void DrownTick_EveryIntervalOnlyWhileDrowning()
        {
            float t = 0f;
            int hits = 0;
            for (int i = 0; i < 310; i++) if (SwimRules.DrownTick(ref t, true, 0.02f, S.drownInterval)) hits++; // 6,2 s (Takte bei ≈ 2, 4, 6 s)
            Assert.AreEqual(3, hits, "alle 2 s ein Takt");
            Assert.IsFalse(SwimRules.DrownTick(ref t, false, 0.02f, S.drownInterval), "aufgetaucht: kein Schaden");
            Assert.AreEqual(0f, t, "Zähler beginnt nach dem Auftauchen von vorn");
            t = 0f;
            hits = 0;
            for (int i = 0; i < 12; i++) if (SwimRules.DrownTick(ref t, true, 0.02f, 0f)) hits++; // 0,24 s
            Assert.AreEqual(2, hits, "Abstand 0 wird zu 0,1 s – nie Schaden in jedem Takt");
            var hit = SwimRules.DrownHit(200f, 0.1f, new Vector3(1f, 2f, 3f));
            Assert.AreEqual(20f, hit.Amount);
            Assert.IsTrue(hit.Unblockable);
            Assert.IsTrue(hit.Environmental, "Umweltschaden: Rüstung zählt nicht (Max 06.10., wie Classic)");
            Assert.IsNull(hit.Source, "Umweltschaden ohne Quelle");
            Assert.AreEqual(SwimRules.DrownWeaponId, hit.WeaponId);
            Assert.AreEqual(3.6f, hit.HitPoint.y, 1e-4f, "Trefferpunkt am Kopf");
        }

        [Test]
        public void Settings_OldBreathOf60BecomesNinety()
        {
            var alt = new CharacterMotorSettings { version = 2, breathSeconds = 60f };
            alt.Migrate();
            Assert.AreEqual(90f, alt.breathSeconds, 1e-4f, "gespeicherter alter Standard 60 s wird 90 s");
            Assert.AreEqual(CharacterMotorSettings.CurrentVersion, alt.version);
            var eigen = new CharacterMotorSettings { version = 2, breathSeconds = 45f };
            eigen.Migrate();
            Assert.AreEqual(45f, eigen.breathSeconds, 1e-4f, "von Hand eingestellte Werte bleiben");
        }

        [Test]
        public void JumpOut_OnlyAtTheSurface()
        {
            Assert.IsTrue(SwimRules.CanJumpOut(S.swimFloatDepth, S.swimFloatDepth), "an der Oberfläche: Sprung aus dem Wasser");
            Assert.IsTrue(SwimRules.CanJumpOut(S.swimFloatDepth + 0.1f, S.swimFloatDepth));
            Assert.IsFalse(SwimRules.CanJumpOut(3f, S.swimFloatDepth), "getaucht: Leertaste steigt auf");
            Assert.IsFalse(SwimRules.CanJumpOut(-1f, S.swimFloatDepth), "außerhalb des Wassers nicht zuständig");
            // Nach dem Absprung (Netz-Takt 50 Hz) taucht die Figur nicht wieder ein, solange sie steigt – auch am Rand der
            // Toleranz, wo die Tiefe nach einem Takt noch über der Schwimmschwelle liegt.
            float jumpVelocity = S.JumpVelocity;
            float depthAfterOneTick = S.swimFloatDepth + 0.15f - jumpVelocity * 0.02f;
            Assert.GreaterOrEqual(depthAfterOneTick, S.swimEnterDepth, "Randfall: noch tief genug zum Schwimmen");
            Assert.IsFalse(SwimRules.ShouldSwim(false, jumpVelocity, depthAfterOneTick, S.swimEnterDepth, S.swimExitDepth), "steigt: kein Eintauchen");
            Assert.IsTrue(SwimRules.ShouldSwim(false, -1f, depthAfterOneTick, S.swimEnterDepth, S.swimExitDepth), "fällt zurück: schwimmt wieder");
            Assert.IsTrue(SwimRules.ShouldSwim(true, 2f, 2f, S.swimEnterDepth, S.swimExitDepth), "wer schwimmt, schwimmt beim Aufsteigen weiter");
        }

        [Test]
        public void DivePitch_HasDeadZoneAroundTheFollowCamera()
        {
            Assert.AreEqual(0f, SwimRules.DivePitch(14f, S.diveDeadLow, S.diveDeadHigh), "Grundneigung der Kamera: geradeaus");
            Assert.AreEqual(20f, SwimRules.DivePitch(45f, S.diveDeadLow, S.diveDeadHigh), 1e-4f, "deutlich nach unten: abtauchen");
            Assert.AreEqual(-10f, SwimRules.DivePitch(-10f, S.diveDeadLow, S.diveDeadHigh), 1e-4f, "nach oben: aufsteigen");
        }

        [Test]
        public void VerticalSpeed_FloatsAtTheSurfaceAndDivesOnCommand()
        {
            float dt = 1f / 60f;
            // an der Schwimmhöhe: ohne Eingabe bleibt man dort (kein Aufstieg über die Oberfläche)
            Assert.AreEqual(0f, SwimRules.VerticalSpeed(false, false, 0f, 0f, S.swimFloatDepth, dt, 3f, 4.25f, S.swimFloatDepth), 1e-4f);
            Assert.AreEqual(0f, SwimRules.VerticalSpeed(true, false, 0f, 0f, S.swimFloatDepth, dt, 3f, 4.25f, S.swimFloatDepth), 1e-4f, "Leertaste an der Oberfläche: nicht höher");
            Assert.AreEqual(-3f, SwimRules.VerticalSpeed(false, true, 0f, 0f, S.swimFloatDepth, dt, 3f, 4.25f, S.swimFloatDepth), 1e-4f, "X: absinken");
            // tief unten: Leertaste steigt, ohne Eingabe schwebt man
            Assert.AreEqual(3f, SwimRules.VerticalSpeed(true, false, 0f, 0f, 5f, dt, 3f, 4.25f, S.swimFloatDepth), 1e-4f);
            Assert.AreEqual(0f, SwimRules.VerticalSpeed(false, false, 0f, 0f, 5f, dt, 3f, 4.25f, S.swimFloatDepth), 1e-4f);
            // knapp unter der Schwimmhöhe treibt man auf
            Assert.Greater(SwimRules.VerticalSpeed(false, false, 0f, 0f, S.swimFloatDepth + 0.3f, dt, 3f, 4.25f, S.swimFloatDepth), 0f);
            // vorwärts mit 30° Tauchwinkel: sinkt mit sin(30°) × Tempo
            Assert.AreEqual(-4.25f * 0.5f, SwimRules.VerticalSpeed(false, false, 30f, 1f, 5f, dt, 3f, 4.25f, S.swimFloatDepth), 1e-3f);
            // zu hoch (Sprung ins Wasser): zieht auf die Schwimmhöhe
            Assert.Less(SwimRules.VerticalSpeed(false, false, 0f, 0f, 1.3f, dt, 3f, 4.25f, S.swimFloatDepth), 0f);
        }

        [Test]
        public void WaterSurface_FromAreas()
        {
            var areas = new List<WaterVolume.Area>
            {
                new WaterVolume.Area(Rect.MinMaxRect(-100f, -100f, 100f, 100f), -28f),     // See
                new WaterVolume.Area(Rect.MinMaxRect(0f, 0f, 20f, 20f), -21.7f)             // höher gelegener Tümpel
            };
            Assert.IsTrue(WaterVolume.TrySurface(areas, new Vector3(-50f, -31f, 0f), out float s1));
            Assert.AreEqual(-28f, s1, 1e-4f);
            Assert.IsTrue(WaterVolume.TrySurface(areas, new Vector3(10f, -22.3f, 10f), out float s2));
            Assert.AreEqual(-21.7f, s2, 1e-4f, "höchster Spiegel gilt");
            Assert.IsFalse(WaterVolume.TrySurface(areas, new Vector3(500f, 0f, 0f), out _), "außerhalb");
            Assert.IsFalse(WaterVolume.TrySurface(areas, new Vector3(-50f, 40f, 0f), out _, 50f), "hoch über dem Wasser: Spiegel mehr als 50 m tiefer");
        }
    }
}
