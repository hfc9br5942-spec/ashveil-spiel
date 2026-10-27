using Ashveil.Core.Events;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using UnityEngine;

namespace Ashveil.Gameplay.Items
{
    /// <summary>
    /// Eigener Spieler: Rechtsklick auf eine Leiche mit Beute oeffnet das Beutefenster; ist die Beute leer und kann der
    /// eigene Heiler dort etwas bergen (Nachlese), beginnt das Bergen.
    /// Laeuft nach dem Spieler-Update (liest die Klicks dieses Frames).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class LootInteractor : MonoBehaviour
    {
        [SerializeField] private float clickRange = 60f;
        [Tooltip("Maximale Entfernung zur Leiche")]
        [SerializeField] private float lootRange = 5f;

        private PlayerInputReader _input;
        private Health _health;
        private Camera _cam;

        public float LootRange => lootRange;

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _health = GetComponent<Health>();
        }

        private void LateUpdate()
        {
            if (_input == null || !_input.isActiveAndEnabled) return;
            // 21.10. (Nr. 8 Gefallene): tot weder pluendern noch bergen - ein offenes Beutefenster schliesst sich.
            // Nach der Wiederbelebung geht alles wieder normal.
            if (_health != null && !_health.IsAlive)
            {
                IInventoryService inv = InventoryClient.Current;
                if (inv != null && inv.LootOpen) inv.CloseLoot();
                return;
            }
            CombatInput c = _input.Combat;
            if (!c.ClickInteract) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            Ray ray = _cam.ScreenPointToRay(c.InteractPosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, clickRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            LootIndicator best = null;
            float bestDist = float.MaxValue;
            foreach (RaycastHit h in hits)
            {
                LootIndicator li = h.collider.GetComponentInParent<LootIndicator>();
                if (li == null || !(li.Available || li.SalvageAvailable) || h.distance >= bestDist) continue;
                best = li;
                bestDist = h.distance;
            }
            if (best == null) return;
            if (Vector3.Distance(best.transform.position, transform.position) > lootRange)
            {
                CombatTextEvent.Error("Zu weit entfernt.");
                return;
            }
            if (best.Available) InventoryClient.Current?.TryOpenLoot(best.gameObject);
            else SalvageClient.Request?.Invoke(best.gameObject);
        }
    }
}
