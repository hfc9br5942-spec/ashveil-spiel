using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Weg als Linienzug in x/z mit Bogenlänge (#29 Begehung, 07.10.2026). Reine Rechnung, ohne Szene prüfbar:
    /// nächster Wegpunkt mit Suchfenster (in Kehren springt ein Läufer nicht auf den anderen Schenkel), Punkt und Richtung nach
    /// Bogenlänge, steilste Stelle eines Höhenverlaufs über ein Fenster (wie die Weltprüfung: 10 m).
    /// Benutzt von der Begehungs-Suite (Charaktermotor läuft die Wege ab); später auch für Karte und Reisewege brauchbar.
    /// </summary>
    public sealed class Wegstrecke
    {
        private readonly Vector2[] _p;
        private readonly float[] _s;

        public int Count => _p.Length;
        public float Length => _s[_s.Length - 1];
        public Vector2 this[int i] => _p[i];
        /// <summary>Bogenlänge am Punkt i.</summary>
        public float ArcAt(int i) => _s[i];

        /// <summary>Punkte in x/z (Weltmeter). Doppelte Punkte werden übergangen.</summary>
        public Wegstrecke(IList<Vector2> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            var pts = new List<Vector2>(points.Count);
            foreach (Vector2 q in points)
                if (pts.Count == 0 || (q - pts[pts.Count - 1]).sqrMagnitude > 1e-6f) pts.Add(q);
            if (pts.Count < 2) throw new ArgumentException("Ein Weg braucht mindestens zwei verschiedene Punkte.");
            _p = pts.ToArray();
            _s = new float[_p.Length];
            for (int i = 1; i < _p.Length; i++) _s[i] = _s[i - 1] + Vector2.Distance(_p[i - 1], _p[i]);
        }

        /// <summary>Index des Teilstücks (Punkt i bis i+1), das die Bogenlänge s enthält.</summary>
        public int SegmentAt(float s)
        {
            if (s <= 0f) return 0;
            if (s >= Length) return _p.Length - 2;
            int lo = 0, hi = _s.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_s[mid] <= s) lo = mid;
                else hi = mid;
            }
            return Mathf.Min(lo, _p.Length - 2);
        }

        public Vector2 PointAt(float s)
        {
            s = Mathf.Clamp(s, 0f, Length);
            int i = SegmentAt(s);
            float len = _s[i + 1] - _s[i];
            float t = len > 1e-6f ? (s - _s[i]) / len : 0f;
            return Vector2.Lerp(_p[i], _p[i + 1], t);
        }

        /// <summary>Wegrichtung (Einheitsvektor in x/z) an der Bogenlänge s, in Richtung wachsender Bogenlänge.</summary>
        public Vector2 DirectionAt(float s)
        {
            int i = SegmentAt(Mathf.Clamp(s, 0f, Length));
            Vector2 d = _p[i + 1] - _p[i];
            return d.sqrMagnitude > 1e-9f ? d.normalized : Vector2.up;
        }

        /// <summary>
        /// Nächster Wegpunkt zu p, gesucht nur zwischen den Bogenlängen sMin und sMax. Gibt dessen Bogenlänge zurück,
        /// den seitlichen Abstand in <paramref name="lateral"/>.
        /// </summary>
        public float Project(Vector2 p, float sMin, float sMax, out float lateral)
        {
            sMin = Mathf.Clamp(sMin, 0f, Length);
            sMax = Mathf.Clamp(sMax, sMin, Length);
            int i0 = SegmentAt(sMin), i1 = SegmentAt(sMax);
            float best = float.MaxValue, bestS = sMin;
            for (int i = i0; i <= i1; i++)
            {
                Vector2 a = _p[i], ab = _p[i + 1] - a;
                float segLen = _s[i + 1] - _s[i];
                float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-9f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
                float s = _s[i] + t * segLen;
                if (s < sMin || s > sMax)
                {
                    s = Mathf.Clamp(s, sMin, sMax);
                    t = segLen > 1e-6f ? Mathf.Clamp01((s - _s[i]) / segLen) : 0f;
                }
                float d = (p - (a + ab * t)).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    bestS = s;
                }
            }
            lateral = Mathf.Sqrt(best);
            return bestS;
        }

        /// <summary>Nächster Wegpunkt zu p über den ganzen Weg.</summary>
        public float Project(Vector2 p, out float lateral) => Project(p, 0f, Length, out lateral);

        /// <summary>
        /// Steilste Stelle eines Höhenverlaufs: größte |Δy| / Δs zwischen zwei Proben, die mindestens <paramref name="window"/>
        /// Meter auseinanderliegen (Anteil: 0,3 = 30 %). s muss aufsteigend sein. at = Index der ersten Probe, −1 ohne Ergebnis.
        /// </summary>
        public static float WorstGrade(IList<float> s, IList<float> y, float window, out int at)
        {
            at = -1;
            if (s == null || y == null || s.Count != y.Count) return 0f;
            float worst = 0f;
            int j = 0;
            for (int i = 0; i < s.Count; i++)
            {
                if (j < i) j = i;
                while (j < s.Count && s[j] - s[i] < window) j++;
                if (j >= s.Count) break;
                float ds = s[j] - s[i];
                if (ds <= 1e-3f) continue;
                float g = Mathf.Abs(y[j] - y[i]) / ds;
                if (g > worst)
                {
                    worst = g;
                    at = i;
                }
            }
            return worst;
        }
    }
}
