using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Story
{
    /// <summary>
    /// Fester Punkt einer Geschichte in der Szene (Hofplatz, Tor, Fohlenplatz, Bedrohungsanker …). Server und Clients finden
    /// ihn ueber die Kennung – er liegt in der Zellen-Szene und ist da, sobald die Zelle geladen ist.
    /// </summary>
    public sealed class StoryAnchor : MonoBehaviour
    {
        [SerializeField] private string anchorId;
        [SerializeField] private float radius = 3f;

        private static readonly Dictionary<string, StoryAnchor> ById = new Dictionary<string, StoryAnchor>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ById.Clear();

        public string AnchorId => anchorId;
        public float Radius => radius;
        public Vector3 Position => transform.position;
        public Vector3 Forward => transform.forward;

        public void Configure(string id, float r)
        {
            anchorId = id;
            radius = r;
        }

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(anchorId)) ById[anchorId] = this;
        }

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(anchorId) && ById.TryGetValue(anchorId, out StoryAnchor a) && a == this) ById.Remove(anchorId);
        }

        public static StoryAnchor Find(string id) => id != null && ById.TryGetValue(id, out StoryAnchor a) && a != null ? a : null;

        public static bool TryGet(string id, out Vector3 pos)
        {
            StoryAnchor a = Find(id);
            pos = a != null ? a.transform.position : Vector3.zero;
            return a != null;
        }

        public bool Contains(Vector3 p, float extra = 0f)
        {
            Vector3 d = p - transform.position;
            d.y = 0f;
            return d.sqrMagnitude <= (radius + extra) * (radius + extra) && Mathf.Abs(p.y - transform.position.y) < 12f;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.9f, 0.7f, 0.3f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
