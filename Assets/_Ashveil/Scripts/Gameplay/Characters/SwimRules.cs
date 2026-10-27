using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// Schwimmen und Tauchen (MOV-169, Welle 0) – reine Regeln ohne Szene, damit Motor, Server und Tests dieselben Zahlen
    /// rechnen. Tiefe = Wasserspiegel − Fußhöhe der Figur (m). Werte stehen in <see cref="CharacterMotorSettings"/>
    /// (Schwimmen); Atemdauer und Ertrinken nach WoW-Classic-Vorbild, einstellbar (Werte offen, 11 §Y).
    /// </summary>
    public static class SwimRules
    {
        /// <summary>Schwimmt die Figur? Mit Hysterese: ab <paramref name="enterDepth"/> schwimmen, erst unter
        /// <paramref name="exitDepth"/> wieder gehen (sonst Flackern an der Uferkante).</summary>
        public static bool IsSwimming(bool wasSwimming, float depth, float enterDepth, float exitDepth)
        {
            if (depth <= 0f) return false;
            return wasSwimming ? depth > exitDepth : depth >= enterDepth;
        }

        /// <summary>
        /// Schwimmt die Figur in diesem Schritt? Wie <see cref="IsSwimming"/>, aber wer gerade aufsteigt (Sprung aus dem
        /// Wasser, <paramref name="velocityY"/> über <paramref name="riseLimit"/> m/s), taucht erst beim Fallen wieder ein –
        /// sonst würde der Sprung im nächsten Takt an der Schwimmhöhe abgebremst.
        /// </summary>
        public static bool ShouldSwim(bool wasSwimming, float velocityY, float depth, float enterDepth, float exitDepth, float riseLimit = 0.5f) =>
            (wasSwimming || velocityY <= riseLimit) && IsSwimming(wasSwimming, depth, enterDepth, exitDepth);

        /// <summary>Kopf unter Wasser (Atemleiste läuft ab)?</summary>
        public static bool HeadUnder(float depth, float headHeight) => depth > headHeight;

        /// <summary>Atem in Sekunden: unter Wasser läuft er ab, an der Luft füllt er sich schnell wieder.</summary>
        public static float Breath(float breath, bool headUnder, float dt, float maxBreath, float refillPerSecond) =>
            headUnder ? Mathf.Max(0f, breath - dt) : Mathf.Min(maxBreath, breath + refillPerSecond * dt);

        /// <summary>Ertrinken: Schaden je Takt als Anteil des Höchstlebens, mindestens 1 (Classic: Anteil alle 2 s).</summary>
        public static float DrownDamage(float maxHealth, float fraction) => Mathf.Max(1f, Mathf.Round(maxHealth * Mathf.Clamp01(fraction)));

        /// <summary>Kennung des Ertrinkungsschadens (Kampflog, Statistik).</summary>
        public const string DrownWeaponId = "umwelt.ertrinken";

        /// <summary>
        /// Ertrinkungstakt: zählt, solange man ertrinkt, und meldet true, wenn ein Schadenstakt fällig ist (alle
        /// <paramref name="interval"/> s, mindestens 0,1 s). Ohne Ertrinken springt der Zähler auf 0 – nach dem Auftauchen
        /// beginnt der nächste Takt wieder von vorn.
        /// </summary>
        public static bool DrownTick(ref float timer, bool drowning, float dt, float interval)
        {
            if (!drowning)
            {
                timer = 0f;
                return false;
            }
            float iv = Mathf.Max(0.1f, interval);
            timer += dt;
            if (timer < iv) return false;
            timer -= iv;
            return true;
        }

        /// <summary>Ein Ertrinkungstreffer: ohne Quelle, nicht blockbar, Trefferpunkt am Kopf.</summary>
        public static DamageInfo DrownHit(float maxHealth, float fraction, Vector3 feetPosition) => new DamageInfo
        {
            Amount = DrownDamage(maxHealth, fraction),
            Unblockable = true,
            Environmental = true,   // Rüstung zählt nicht (Max 06.10.2026, wie Classic)
            HitPoint = feetPosition + Vector3.up * 1.6f,
            WeaponId = DrownWeaponId
        };

        /// <summary>
        /// Aus dem Wasser springen (wie in Classic): Nur an der Oberfläche – die Füße höchstens <paramref name="tolerance"/> m
        /// unter der Schwimmhöhe – wird die Sprungtaste zum Sprung, sonst steigt man auf. So kommt man auf Stege und Ufer.
        /// </summary>
        public static bool CanJumpOut(float depth, float floatDepth, float tolerance = 0.15f) => depth > 0f && depth <= floatDepth + tolerance;

        /// <summary>
        /// Tauchwinkel aus der Kamera-Neigung (Grad, positiv = nach unten). Um die Grundneigung der Verfolgerkamera liegt
        /// eine tote Zone, damit man an der Oberfläche geradeaus schwimmt; erst deutlich nach unten blicken taucht ab,
        /// nach oben blicken steigt auf (wie in WoW mit gehaltener rechter Maustaste).
        /// </summary>
        public static float DivePitch(float cameraPitch, float deadLow, float deadHigh)
        {
            if (cameraPitch > deadHigh) return cameraPitch - deadHigh;
            if (cameraPitch < deadLow) return cameraPitch - deadLow;
            return 0f;
        }

        /// <summary>
        /// Senkrechte Geschwindigkeit im Wasser (m/s, + = aufwärts). Leertaste/X steigen und sinken; vorwärts mit
        /// Kamerasteuerung folgt dem Tauchwinkel; ohne Eingabe schwebt man, knapp unter der Schwimmhöhe treibt man auf.
        /// Über die Schwimmhöhe (Füße <paramref name="floatDepth"/> unter dem Spiegel) kommt man nicht hinaus.
        /// </summary>
        public static float VerticalSpeed(bool up, bool down, float divePitchDeg, float forwardInput, float depth, float dt,
            float verticalSpeed, float swimSpeed, float floatDepth, float driftUp = 1f, float driftZone = 0.6f)
        {
            float vy;
            if (up && !down) vy = verticalSpeed;
            else if (down && !up) vy = -verticalSpeed;
            else if (Mathf.Abs(divePitchDeg) > 0.01f && forwardInput > 0.01f) vy = -Mathf.Sin(divePitchDeg * Mathf.Deg2Rad) * swimSpeed * forwardInput;
            else vy = depth - floatDepth < driftZone ? driftUp : 0f;
            // nicht über die Schwimmhöhe: höchstens bis dorthin steigen (ist man höher, zieht es auf die Schwimmhöhe)
            float maxUp = (depth - floatDepth) / Mathf.Max(dt, 1e-4f);
            return Mathf.Min(vy, maxUp);
        }
    }
}
