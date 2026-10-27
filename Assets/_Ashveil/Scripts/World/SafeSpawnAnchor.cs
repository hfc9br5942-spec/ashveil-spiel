using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Sicherer Punkt einer Zelle (Masterplan §49) fuer Login, Reconnect, Sturz unter Gelaende, ungueltige gespeicherte
    /// Position nach Weltaenderungen und spaeter "Festsitzen". Jede Zelle hat mindestens einen.
    /// </summary>
    public sealed class SafeSpawnAnchor : MonoBehaviour
    {
        [SerializeField] private string anchorId = "";
        [SerializeField] private string cellId = "";

        private static readonly List<SafeSpawnAnchor> Registered = new List<SafeSpawnAnchor>();

        public string AnchorId => anchorId;
        public string CellId => cellId;
        public static IReadOnlyList<SafeSpawnAnchor> All => Registered;

        public void Configure(string id, string cell)
        {
            anchorId = id;
            cellId = cell;
        }

        /// <summary>Naechster geladener Anker (optional nur in einer Zelle).</summary>
        public static SafeSpawnAnchor Nearest(Vector3 pos, string inCell = null)
        {
            SafeSpawnAnchor best = null;
            float bestD = float.MaxValue;
            foreach (SafeSpawnAnchor a in Registered)
            {
                if (a == null || (inCell != null && a.cellId != inCell)) continue;
                float d = (a.transform.position - pos).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = a;
                }
            }
            return best;
        }

        private void OnEnable()
        {
            if (!Registered.Contains(this)) Registered.Add(this);
        }

        private void OnDisable() => Registered.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.8f);
            Gizmos.DrawWireCube(transform.position + Vector3.up, new Vector3(1f, 2f, 1f));
        }
    }
}
