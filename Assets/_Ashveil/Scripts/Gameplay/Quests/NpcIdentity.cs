using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Quests
{
    /// <summary>
    /// Figur in der Szene, die zu einer <see cref="Ashveil.Data.NpcDefinition"/> gehoert. Server und Client haben
    /// dieselbe Szene – der Server findet den NPC ueber die ID und prueft die Entfernung.
    /// </summary>
    public sealed class NpcIdentity : MonoBehaviour
    {
        private static readonly Dictionary<string, NpcIdentity> ById = new Dictionary<string, NpcIdentity>();

        [SerializeField] private string npcId;

        public string NpcId => npcId;
        public static IEnumerable<NpcIdentity> All => ById.Values;

        public void Configure(string id) => npcId = id;

        public static NpcIdentity Find(string id) =>
            id != null && ById.TryGetValue(id, out NpcIdentity n) && n != null ? n : null;

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(npcId)) ById[npcId] = this;
        }

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(npcId) && ById.TryGetValue(npcId, out NpcIdentity n) && n == this) ById.Remove(npcId);
        }
    }
}
