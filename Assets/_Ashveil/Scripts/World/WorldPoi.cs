using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Fester Ort in der Welt (Point of Interest) mit stabiler Kennung. Die Kennung bleibt bei jedem Neuaufbau der
    /// Szene gleich, damit spaetere Entdeckungen, kleine Geschichten oder Auftraege sich darauf beziehen koennen.
    /// Heute nur Markierung – es haengt noch keine Spiellogik daran.
    /// </summary>
    public sealed class WorldPoi : MonoBehaviour
    {
        [SerializeField] private string id = "";
        [SerializeField] private string displayName = "";
        [SerializeField] private float radius = 20f;
        [SerializeField, TextArea] private string plannedUse = "";

        private static readonly List<WorldPoi> Registered = new List<WorldPoi>();

        public string Id => id;
        public string DisplayName => displayName;
        public float Radius => radius;
        public string PlannedUse => plannedUse;
        public static IReadOnlyList<WorldPoi> All => Registered;

        public void Configure(string poiId, string name, float poiRadius, string use)
        {
            id = poiId;
            displayName = name;
            radius = poiRadius;
            plannedUse = use;
        }

        public static WorldPoi Find(string poiId)
        {
            foreach (WorldPoi p in Registered)
                if (p != null && p.id == poiId) return p;
            return null;
        }

        private void OnEnable()
        {
            if (!Registered.Contains(this)) Registered.Add(this);
        }

        private void OnDisable() => Registered.Remove(this);

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
