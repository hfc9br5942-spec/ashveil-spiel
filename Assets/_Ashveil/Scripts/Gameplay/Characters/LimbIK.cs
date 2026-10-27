using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// Zwei-Knochen-IK (Oberarm/Unterarm bzw. Oberschenkel/Schienbein) – geteilt von Haltung, Bewegungsschicht und Emotes.
    ///
    /// Ellbogen und Knie sind Scharniere: sie beugen nur um EINE Achse. Die Achse wird aus der laufenden Animation
    /// gelernt (dort ist sie anatomisch richtig) und nach dem Loesen durch Drehen des oberen Knochens um seine eigene
    /// Achse wieder auf die neue Gliedmassen-Ebene gelegt – sonst knickt das Gelenk seitlich weg (O-Bein, verdrehter Ellbogen).
    /// </summary>
    public static class LimbIK
    {
        // Gelernte Beugeachse je oberem Knochen (in dessen lokalen Achsen)
        private static readonly Dictionary<Transform, Vector3> Hinges = new Dictionary<Transform, Vector3>();

        /// <summary>Beugeachse aus der aktuellen (animierten) Pose lernen – vor eigenen Eingriffen in diesem Bild.</summary>
        public static void Learn(Transform upper, Transform lower, Transform end)
        {
            if (upper == null || lower == null || end == null) return;
            Vector3 u = lower.position - upper.position, l = end.position - lower.position;
            if (Vector3.Angle(u, l) < 10f) return;
            Vector3 axis = Vector3.Cross(u, l);
            if (axis.sqrMagnitude < 1e-10f) return;
            Vector3 m = Quaternion.Inverse(upper.rotation) * axis.normalized;
            Hinges[upper] = Hinges.TryGetValue(upper, out Vector3 old) ? Vector3.Lerp(old, m, 0.05f).normalized : m;
        }

        /// <summary>'hint' = Richtung, in die Ellbogen/Knie zeigen sollen. weight 0..1 mischt mit der Animation.</summary>
        public static void TwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint, float weight = 1f)
        {
            if (upper == null || lower == null || end == null || weight <= 0.0001f) return;
            Learn(upper, lower, end);
            if (weight < 1f) target = Vector3.Lerp(end.position, target, weight);
            Vector3 a = upper.position, b = lower.position, c = end.position;
            float lab = Vector3.Distance(a, b), lbc = Vector3.Distance(b, c);
            Vector3 d = target - a;
            if (d.sqrMagnitude < 1e-8f || lab < 1e-4f || lbc < 1e-4f) return;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(lab - lbc) + 0.01f, (lab + lbc) * 0.999f);
            Vector3 dir = d.normalized;

            Vector3 perp = Vector3.ProjectOnPlane(hint, dir);
            if (perp.sqrMagnitude < 1e-6f) perp = Vector3.ProjectOnPlane(b - a, dir);
            if (perp.sqrMagnitude < 1e-8f) return;
            perp.Normalize();

            float cosA = Mathf.Clamp((lab * lab + dist * dist - lbc * lbc) / (2f * lab * dist), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 bNew = a + lab * (cosA * dir + sinA * perp);
            Vector3 cNew = a + dir * dist;

            upper.rotation = Quaternion.FromToRotation(b - a, bNew - a) * upper.rotation;
            AlignHinge(upper, a, bNew, cNew, weight);
            Vector3 bPos = lower.position;
            lower.rotation = Quaternion.FromToRotation(end.position - bPos, cNew - bPos) * lower.rotation;
        }

        /// <summary>Oberen Knochen um seine Achse drehen, bis die gelernte Beugeachse senkrecht auf der neuen Ebene steht.</summary>
        private static void AlignHinge(Transform upper, Vector3 a, Vector3 bNew, Vector3 cNew, float weight)
        {
            if (!Hinges.TryGetValue(upper, out Vector3 local)) return;
            Vector3 ud = (bNew - a).normalized;
            Vector3 n = Vector3.Cross(ud, cNew - bNew);
            float bend = Vector3.Angle(bNew - a, cNew - bNew);
            // Fast gestreckt: Ebene unbestimmt – nicht drehen (sonst zuckt der Knochen)
            float w = Mathf.Clamp01((bend - 12f) / 30f) * weight;
            if (w <= 0f || n.sqrMagnitude < 1e-8f) return;
            Vector3 h = Vector3.ProjectOnPlane(upper.rotation * local, ud);
            if (h.sqrMagnitude < 1e-8f) return;
            float ang = Vector3.SignedAngle(h, n.normalized, ud);
            // Wuerde das Gelenk dafuer halb herum gedreht (Beugung gegen die Anatomie), hoechstens 60° nachfuehren
            ang = Mathf.Clamp(ang, -40f, 40f);
            upper.rotation = Quaternion.AngleAxis(ang * w, ud) * upper.rotation;
        }
    }
}
