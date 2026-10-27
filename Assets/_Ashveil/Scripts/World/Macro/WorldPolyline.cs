using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>Abstand zu Linienzuegen in der x/z-Ebene (Gebirge, Taeler, Kerbe, Fluesse, Strassen).</summary>
    public static class WorldPolyline
    {
        /// <summary>Kleinster Abstand von p zum Linienzug; seg = Segment, t = 0…1 im Segment, along = Meter ab Anfang.</summary>
        public static float Distance(IReadOnlyList<Vector2> pts, Vector2 p, out int seg, out float t, out float along, out Vector2 closest)
        {
            seg = 0;
            t = 0f;
            along = 0f;
            closest = pts.Count > 0 ? pts[0] : Vector2.zero;
            float best = float.MaxValue, run = 0f;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                Vector2 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float tt = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
                Vector2 c = a + ab * tt;
                float d = (p - c).sqrMagnitude;
                float len = Mathf.Sqrt(len2);
                if (d < best)
                {
                    best = d;
                    seg = i;
                    t = tt;
                    closest = c;
                    along = run + len * tt;
                }
                run += len;
            }
            return Mathf.Sqrt(best);
        }

        public static float Distance(IReadOnlyList<Vector2> pts, Vector2 p) => Distance(pts, p, out _, out _, out _, out _);

        /// <summary>Vorzeichen: auf welcher Seite des Linienzugs liegt p (links +1, rechts −1, in Laufrichtung).</summary>
        public static float Side(IReadOnlyList<Vector2> pts, Vector2 p, int seg)
        {
            if (pts.Count < 2) return 1f;
            seg = Mathf.Clamp(seg, 0, pts.Count - 2);
            Vector2 a = pts[seg], b = pts[seg + 1];
            float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            return cross >= 0f ? 1f : -1f;
        }

        public static float Length(IReadOnlyList<Vector2> pts)
        {
            float l = 0f;
            for (int i = 0; i + 1 < pts.Count; i++) l += Vector2.Distance(pts[i], pts[i + 1]);
            return l;
        }

        /// <summary>Punkt nach <paramref name="meters"/> entlang des Linienzugs.</summary>
        public static Vector2 PointAt(IReadOnlyList<Vector2> pts, float meters)
        {
            if (pts.Count == 0) return Vector2.zero;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                float l = Vector2.Distance(pts[i], pts[i + 1]);
                if (meters <= l) return Vector2.Lerp(pts[i], pts[i + 1], l > 0f ? meters / l : 0f);
                meters -= l;
            }
            return pts[pts.Count - 1];
        }
    }
}
