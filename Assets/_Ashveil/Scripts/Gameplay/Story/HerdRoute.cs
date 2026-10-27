using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Story
{
    /// <summary>
    /// Kuratierte Route (Wegpunkte mit Bogenlaenge) – die Herde folgt ihr, nicht dem freien NavMesh (Vorgabe: HerdMovementRoute).
    /// Punkte liegen auf dem Boden (vom Editor gesetzt); Server und Clients rechnen dieselben Positionen daraus.
    /// </summary>
    public sealed class HerdRoute : MonoBehaviour
    {
        [SerializeField] private string routeId;
        [SerializeField] private Vector3[] points = new Vector3[0];

        private float[] _cum;
        private static readonly Dictionary<string, HerdRoute> ById = new Dictionary<string, HerdRoute>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ById.Clear();

        public string RouteId => routeId;
        public int Count => points.Length;
        public Vector3 Point(int i) => points[i];

        public float Length
        {
            get
            {
                EnsureCum();
                return _cum.Length > 0 ? _cum[_cum.Length - 1] : 0f;
            }
        }

        public void Configure(string id, Vector3[] pts)
        {
            routeId = id;
            points = pts ?? new Vector3[0];
            _cum = null;
        }

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(routeId)) ById[routeId] = this;
        }

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(routeId) && ById.TryGetValue(routeId, out HerdRoute r) && r == this) ById.Remove(routeId);
        }

        public static HerdRoute Find(string id) => id != null && ById.TryGetValue(id, out HerdRoute r) && r != null ? r : null;

        private void EnsureCum()
        {
            if (_cum != null && _cum.Length == points.Length) return;
            _cum = new float[points.Length];
            for (int i = 1; i < points.Length; i++) _cum[i] = _cum[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        }

        /// <summary>Punkt bei Bogenlaenge s (geklemmt) und Laufrichtung (waagerecht, normiert).</summary>
        public Vector3 PointAt(float s, out Vector3 dir)
        {
            EnsureCum();
            dir = Vector3.forward;
            if (points.Length == 0) return transform.position;
            if (points.Length == 1) return points[0];
            s = Mathf.Clamp(s, 0f, _cum[_cum.Length - 1]);
            int lo = 0, hi = points.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_cum[mid] <= s) lo = mid;
                else hi = mid;
            }
            float seg = _cum[hi] - _cum[lo];
            float t = seg > 1e-4f ? (s - _cum[lo]) / seg : 0f;
            Vector3 d = points[hi] - points[lo];
            d.y = 0f;
            if (d.sqrMagnitude > 1e-6f) dir = d.normalized;
            return Vector3.Lerp(points[lo], points[hi], t);
        }

        /// <summary>Naechste Bogenlaenge zu einem Punkt (Wiederaufnahme, Tests).</summary>
        public float Project(Vector3 p)
        {
            EnsureCum();
            float best = float.MaxValue, arc = 0f;
            for (int i = 0; i + 1 < points.Length; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                Vector3 ab = b - a;
                ab.y = 0f;
                Vector3 ap = p - a;
                ap.y = 0f;
                float l2 = ab.sqrMagnitude;
                float t = l2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / l2) : 0f;
                float d = (ap - ab * t).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    arc = _cum[i] + t * (_cum[i + 1] - _cum[i]);
                }
            }
            return arc;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.95f, 0.8f, 0.3f, 0.9f);
            for (int i = 1; i < points.Length; i++) Gizmos.DrawLine(points[i - 1] + Vector3.up * 0.5f, points[i] + Vector3.up * 0.5f);
        }
    }
}
