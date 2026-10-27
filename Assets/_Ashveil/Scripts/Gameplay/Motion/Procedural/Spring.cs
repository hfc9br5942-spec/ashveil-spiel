using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>
    /// Gedaempfte Feder (zweiter Ordnung) fuer sichtbare Masse: Frequenz (Hz) bestimmt die Traegheit, Daempfung
    /// (0..1+) das Nachschwingen. 0,5 = deutliches Nachfedern (schwerer Koerper setzt sich), 1 = kritisch gedaempft.
    /// Stabil bei jedem Zeitschritt (exakte Loesung der Federgleichung).
    /// 19.10.2026: vorher Euler mit hoechstens 8 Teilschritten – bei einem Haenger (0,29 s nach dem Versetzen im Motion-Lab,
    /// im Spiel Zellen-Laden/Speichern) explodierten steife Federn (Boden-Feder der Fuesse bis −44 m); die Standverriegelung
    /// hielt den Fuss dort fest und das Becken blieb 55 cm im Boden, bis die Figur losging.
    /// </summary>
    public struct SpringFloat
    {
        public float Value;
        public float Velocity;

        public void Reset(float v) { Value = v; Velocity = 0f; }

        public float Update(float target, float dt, float frequency, float damping)
        {
            if (dt <= 0f) return Value;
            float w = 2f * Mathf.PI * Mathf.Max(0.01f, frequency);
            float z = Mathf.Max(0f, damping);
            float x0 = Value - target, v0 = Velocity;
            float x, v;
            if (z < 0.9999f)
            {
                // unterkritisch: gedaempfte Schwingung
                float wd = w * Mathf.Sqrt(1f - z * z);
                float e = Mathf.Exp(-z * w * dt), c = Mathf.Cos(wd * dt), s = Mathf.Sin(wd * dt);
                float c2 = (v0 + z * w * x0) / wd;
                x = e * (x0 * c + c2 * s);
                v = e * ((c2 * wd - z * w * x0) * c - (x0 * wd + z * w * c2) * s);
            }
            else if (z < 1.0001f)
            {
                // kritisch gedaempft
                float e = Mathf.Exp(-w * dt), c2 = v0 + w * x0;
                x = (x0 + c2 * dt) * e;
                v = (c2 - w * (x0 + c2 * dt)) * e;
            }
            else
            {
                // ueberkritisch
                float r = Mathf.Sqrt(z * z - 1f);
                float r1 = -w * (z - r), r2 = -w * (z + r);
                float c2 = (v0 - r1 * x0) / (r2 - r1), c1 = x0 - c2;
                float e1 = Mathf.Exp(r1 * dt), e2 = Mathf.Exp(r2 * dt);
                x = c1 * e1 + c2 * e2;
                v = r1 * c1 * e1 + r2 * c2 * e2;
            }
            Value = target + x;
            Velocity = v;
            return Value;
        }

        /// <summary>Kurzer Stoss (z. B. Fussaufsatz) – veraendert nur die Geschwindigkeit.</summary>
        public void Kick(float velocity) => Velocity += velocity;
    }

    /// <summary>Feder fuer Winkel in Grad (nimmt den kurzen Weg ueber ±180°).</summary>
    public struct SpringAngle
    {
        public float Value;
        public float Velocity;

        public void Reset(float v) { Value = v; Velocity = 0f; }

        public float Update(float target, float dt, float frequency, float damping)
        {
            float t = Value + Mathf.DeltaAngle(Value, target);
            var s = new SpringFloat { Value = Value, Velocity = Velocity };
            s.Update(t, dt, frequency, damping);
            Value = Mathf.Repeat(s.Value + 180f, 360f) - 180f;
            Velocity = s.Velocity;
            return Value;
        }
    }

    public static class MotionMath
    {
        /// <summary>Weiche Annaeherung, unabhaengig von der Bildrate (Halbwertszeit in s).</summary>
        public static float Damp(float current, float target, float halfLife, float dt) =>
            halfLife <= 0f ? target : Mathf.Lerp(current, target, 1f - Mathf.Pow(0.5f, dt / halfLife));

        public static Vector3 Damp(Vector3 current, Vector3 target, float halfLife, float dt) =>
            halfLife <= 0f ? target : Vector3.Lerp(current, target, 1f - Mathf.Pow(0.5f, dt / halfLife));

        public static Quaternion Damp(Quaternion current, Quaternion target, float halfLife, float dt) =>
            halfLife <= 0f ? target : Quaternion.Slerp(current, target, 1f - Mathf.Pow(0.5f, dt / halfLife));

        /// <summary>Drehung um eine Achse, die den Vektor 'from' Richtung 'to' dreht, begrenzt auf maxDeg.</summary>
        public static Quaternion LimitedFromTo(Vector3 from, Vector3 to, float maxDeg)
        {
            Quaternion q = Quaternion.FromToRotation(from, to);
            q.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (Mathf.Abs(angle) <= maxDeg || axis.sqrMagnitude < 1e-8f) return q;
            return Quaternion.AngleAxis(Mathf.Sign(angle) * maxDeg, axis);
        }

        public static float SmoothStep01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    }
}
