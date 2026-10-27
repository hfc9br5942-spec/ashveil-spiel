using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Quests
{
    /// <summary>Ort fuer Erkundungsziele (area.*). Der Server prueft, ob ein Spieler im Radius steht.</summary>
    public sealed class QuestArea : MonoBehaviour
    {
        private static readonly Dictionary<string, QuestArea> ById = new Dictionary<string, QuestArea>();

        [SerializeField] private string areaId;
        [SerializeField] private float radius = 3f;
        [Tooltip("Hoehe unterhalb des Mittelpunkts, die noch zaehlt (m) – verhindert Abkuerzung unten herum")]
        [SerializeField] private float below = 1.2f;
        [SerializeField] private float above = 4f;

        public string AreaId => areaId;
        public float Radius => radius;

        public void Configure(string id, float r, float belowTolerance)
        {
            areaId = id;
            radius = r;
            below = belowTolerance;
        }

        public static QuestArea Find(string id) =>
            id != null && ById.TryGetValue(id, out QuestArea a) && a != null ? a : null;

        public bool Contains(Vector3 p)
        {
            Vector3 c = transform.position;
            float dy = p.y - c.y;
            if (dy < -below || dy > above) return false;
            p.y = c.y;
            return (p - c).sqrMagnitude <= radius * radius;
        }

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(areaId)) ById[areaId] = this;
        }

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(areaId) && ById.TryGetValue(areaId, out QuestArea a) && a == this) ById.Remove(areaId);
        }
    }
}
