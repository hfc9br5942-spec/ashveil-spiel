using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Feste Wanderroute (Rundweg) fuer einen Gegner, z. B. den Glockenhirten. An den Huete-Punkten
    /// bleibt er eine Weile stehen. Die Leine gilt dann nicht zum Heimatpunkt, sondern zur Route:
    /// entfernt man ihn weiter als die Leinenlaenge von seiner Route, kehrt er um.
    /// </summary>
    public sealed class EnemyRoute : MonoBehaviour
    {
        [Tooltip("Wegpunkte in Weltkoordinaten (Rundweg: nach dem letzten geht es zum ersten)")]
        [SerializeField] private Vector3[] points = new Vector3[0];
        [Tooltip("Indizes der Wegpunkte, an denen gehuetet wird (stehen bleiben)")]
        [SerializeField] private int[] pausePoints = new int[0];
        [Min(0f)] [SerializeField] private float pauseSeconds = 12f;

        public int Count => points != null ? points.Length : 0;
        public float PauseSeconds => pauseSeconds;
        public Vector3 Point(int i) => points[((i % Count) + Count) % Count];

        public bool IsPausePoint(int i)
        {
            if (pausePoints == null) return false;
            int k = ((i % Count) + Count) % Count;
            foreach (int p in pausePoints) if (p == k) return true;
            return false;
        }

        /// <summary>Gesamtlaenge des Rundwegs (m, horizontal).</summary>
        public float Length
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < Count; i++) sum += Flat(Point(i + 1) - Point(i)).magnitude;
                return sum;
            }
        }

        /// <summary>Kleinste horizontale Entfernung von p zur Route (m).</summary>
        public float DistanceTo(Vector3 p) => Flat(p - ClosestPoint(p, out _)).magnitude;

        /// <summary>Naechster Punkt auf der Route; segment = Index des Wegpunkts, zu dem man danach laeuft.</summary>
        public Vector3 ClosestPoint(Vector3 p, out int nextIndex)
        {
            nextIndex = 0;
            if (Count == 0) return p;
            if (Count == 1) return Point(0);
            float best = float.MaxValue;
            Vector3 bestPoint = Point(0);
            for (int i = 0; i < Count; i++)
            {
                Vector3 a = Point(i), b = Point(i + 1);
                Vector3 ab = Flat(b - a);
                float t = Mathf.Clamp01(Vector3.Dot(Flat(p - a), ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                Vector3 c = Vector3.Lerp(a, b, t);
                float d = Flat(p - c).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    bestPoint = c;
                    nextIndex = (i + 1) % Count;
                }
            }
            return bestPoint;
        }

        public void SetPoints(Vector3[] newPoints, int[] newPausePoints, float newPauseSeconds)
        {
            points = newPoints;
            pausePoints = newPausePoints;
            pauseSeconds = newPauseSeconds;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private void OnDrawGizmos()
        {
            if (Count < 2) return;
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
            for (int i = 0; i < Count; i++)
            {
                Gizmos.DrawLine(Point(i), Point(i + 1));
                if (IsPausePoint(i)) Gizmos.DrawWireSphere(Point(i), 4f);
            }
        }
    }
}
