using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Eingang eines Dungeons in der Oberwelt (z. B. der Stollenmund der Salzgruben). Wer hineinlaeuft, wird vom
    /// Server in die Instanz seiner Gruppe gebracht; wer herauskommt, steht am Rueckkehrpunkt davor.
    /// </summary>
    public sealed class DungeonPortal : MonoBehaviour
    {
        [SerializeField] private string dungeonId = "salzgruben";
        [SerializeField] private string displayName = "Salzgruben";
        [SerializeField] private float radius = 2.6f;
        [Tooltip("Rueckkehrpunkt (lokal) – ausserhalb des Radius, damit man nicht sofort wieder hineinfaellt")]
        [SerializeField] private Vector3 returnPoint = new Vector3(0f, 0f, 8f);
        [SerializeField] private float returnYaw;

        private static readonly List<DungeonPortal> Registered = new List<DungeonPortal>();

        public static IReadOnlyList<DungeonPortal> All => Registered;
        public string DungeonId => dungeonId;
        public string DisplayName => displayName;
        public float Radius => radius;
        public Vector3 ReturnWorld => transform.TransformPoint(returnPoint);
        public float ReturnYaw => transform.eulerAngles.y + returnYaw;

        public void Configure(string id, string name, float triggerRadius, Vector3 returnLocal, float yaw)
        {
            dungeonId = id;
            displayName = name;
            radius = triggerRadius;
            returnPoint = returnLocal;
            returnYaw = yaw;
        }

        /// <summary>Steht der Punkt im Eingang? (waagerecht im Radius, hoechstens 4 m Hoehenunterschied)</summary>
        public bool Contains(Vector3 worldPos)
        {
            Vector3 d = worldPos - transform.position;
            return Mathf.Abs(d.y) < 4f && new Vector2(d.x, d.z).sqrMagnitude < radius * radius;
        }

        public static DungeonPortal Find(string id)
        {
            foreach (DungeonPortal p in Registered)
                if (p != null && p.dungeonId == id) return p;
            return null;
        }

        private void OnEnable()
        {
            if (!Registered.Contains(this)) Registered.Add(this);
        }

        private void OnDisable() => Registered.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.DrawLine(transform.position, ReturnWorld);
        }
    }
}
