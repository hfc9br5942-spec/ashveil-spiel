using Ashveil.Core;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Gitter vor der Lorenbahn (M6.3): versperrt die Abkuerzung von der Salzsee-Grotte zur Foerderhalle, bis die
    /// Salzmutter besiegt ist. Oeffnet sich auf das Signal „lore-gate = 1“ (Kollision aus, Gitter faehrt hoch) –
    /// auf dem Server und bei allen Spielern der Instanz; neue Belegung (Signal „reset“) schliesst es wieder.
    /// </summary>
    public sealed class LoreGate : MonoBehaviour
    {
        [SerializeField] private float liftHeight = 5.5f;
        [SerializeField] private float liftSpeed = 2.5f;

        private Vector3 _closedPos;
        private Collider[] _colliders;
        private DungeonSlot _slot;
        private bool _open;

        public bool IsOpen => _open;

        /// <summary>Tor im angegebenen Instanz-Platz (Tests).</summary>
        public static LoreGate In(DungeonSlot slot)
        {
            if (slot == null) return null;
            return slot.GetComponentInChildren<LoreGate>(true);
        }

        private DungeonSlot Slot => _slot != null ? _slot : (_slot = DungeonSlot.At(transform.position));

        private void Awake()
        {
            _closedPos = transform.localPosition;
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        private void OnEnable() => EncounterCues.Received += OnCue;
        private void OnDisable() => EncounterCues.Received -= OnCue;

        private void OnCue(Vector3 where, string cue, float value)
        {
            if (Slot == null || DungeonSlot.At(where) != Slot) return;
            if (cue == EncounterCues.Reset) SetOpen(false, true);
            else if (cue == EncounterCues.LoreGate) SetOpen(value > 0.5f, false);
        }

        public void SetOpen(bool open, bool instant)
        {
            _open = open;
            foreach (Collider c in _colliders)
                if (c != null) c.enabled = !open;
            if (instant) transform.localPosition = _closedPos + (open ? Vector3.up * liftHeight : Vector3.zero);
        }

        private void Update()
        {
            Vector3 goal = _closedPos + (_open ? Vector3.up * liftHeight : Vector3.zero);
            if ((transform.localPosition - goal).sqrMagnitude > 1e-6f)
                transform.localPosition = Vector3.MoveTowards(transform.localPosition, goal, liftSpeed * Time.deltaTime);
        }
    }
}
