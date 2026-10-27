using Ashveil.Core.Events;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Items;
using UnityEngine;

namespace Ashveil.Gameplay.Quests
{
    /// <summary>
    /// Eigener Spieler: Rechtsklick auf einen NPC oeffnet das Gespraech (Auftraege, Handel).
    /// Der Server prueft die Entfernung erneut.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class NpcInteractor : MonoBehaviour
    {
        [SerializeField] private float clickRange = 60f;

        private PlayerInputReader _input;
        private Health _health;
        private Camera _cam;

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _health = GetComponent<Health>();
        }

        private void LateUpdate()
        {
            if (_input == null || !_input.isActiveAndEnabled) return;
            // 21.10. (Nr. 8 Gefallene): tot kein Gespraech/Handel - ein offenes Gespraechs-/Haendlerfenster schliesst sich
            // (meldet es auch dem Server). Nach der Wiederbelebung geht alles wieder normal.
            if (_health != null && !_health.IsAlive)
            {
                IProgressionService p = ProgressionClient.Current;
                if (p != null && p.DialogNpc != null) p.CloseDialog();
                return;
            }
            CombatInput c = _input.Combat;
            if (!c.ClickInteract) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            Ray ray = _cam.ScreenPointToRay(c.InteractPosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, clickRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            NpcIdentity best = null;
            float bestDist = float.MaxValue, lootDist = float.MaxValue;
            foreach (RaycastHit h in hits)
            {
                LootIndicator li = h.collider.GetComponentInParent<LootIndicator>();
                if (li != null && li.Available) lootDist = Mathf.Min(lootDist, h.distance);
                NpcIdentity n = h.collider.GetComponentInParent<NpcIdentity>();
                if (n == null || h.distance >= bestDist) continue;
                best = n;
                bestDist = h.distance;
            }
            if (best == null || lootDist < bestDist) return; // Leiche davor: Beute hat Vorrang
            NpcDefinition def = GameData.Npc(best.NpcId);
            float range = def != null ? def.interactRange : 5f;
            if (Vector3.Distance(best.transform.position, transform.position) > range)
            {
                CombatTextEvent.Error("Zu weit entfernt.");
                return;
            }
            ProgressionClient.Current?.Talk(best.NpcId);
        }
    }
}
