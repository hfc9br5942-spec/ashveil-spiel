using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Quests
{
    /// <summary>
    /// Ruheort (Gasthaus) fuer Ausgeruht-EP wie Classic (Max 10.10.2026). Der Server prueft, ob ein Spieler im Radius steht
    /// (<see cref="Ashveil.Networking.NetworkProgression"/>); der Client zeigt nur an, was der Server schickt.
    /// </summary>
    public sealed class RestArea : MonoBehaviour
    {
        private static readonly List<RestArea> All = new List<RestArea>();

        [SerializeField] private float radius = 18f;
        [Tooltip("Hoehe unter/ueber dem Mittelpunkt, die noch zaehlt (m)")]
        [SerializeField] private float below = 6f;
        [SerializeField] private float above = 10f;

        public float Radius => radius;

        public void Configure(float r, float belowTolerance = 6f, float aboveTolerance = 10f)
        {
            radius = r;
            below = belowTolerance;
            above = aboveTolerance;
        }

        /// <summary>Steht dieser Punkt in irgendeinem Ruheort?</summary>
        public static bool IsRestingAt(Vector3 p)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].Contains(p)) return true;
            return false;
        }

        public bool Contains(Vector3 p)
        {
            Vector3 c = transform.position;
            float dy = p.y - c.y;
            if (dy < -below || dy > above) return false;
            p.y = c.y;
            return (p - c).sqrMagnitude <= radius * radius;
        }

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() => All.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.5f, 0.7f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
