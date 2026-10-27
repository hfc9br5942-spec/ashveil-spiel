using Ashveil.Core.Events;
using Ashveil.Gameplay.Colosseum;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Controls;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Kolosseum Pass 4, eigene Figur: Rechtsklick auf die Bereit-Glocke, die Pruefpunkt-Truhe oder einen gefallenen
    /// Mitstreiter. Laeuft nach dem Spieler-Update (liest die Klicks dieses Frames), wie Beute und Gespraeche.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class ArenaInteractor : MonoBehaviour
    {
        private const float UseRange = 6f, ReviveRange = 3.5f;

        private PlayerInputReader _input;
        private NetworkProgression _progression;
        private Camera _cam;

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
            _progression = GetComponent<NetworkProgression>();
        }

        private void LateUpdate()
        {
            if (_input == null || !_input.isActiveAndEnabled || _progression == null) return;
            CombatInput c = _input.Combat;
            if (!c.ClickInteract) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            Ray ray = _cam.ScreenPointToRay(c.InteractPosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Component pick = null;
            foreach (RaycastHit h in hits)
            {
                if (h.distance >= best) continue;
                Component found = null;
                var bell = h.collider.GetComponentInParent<ArenaBell>();
                var chest = h.collider.GetComponentInParent<ArenaChest>();
                if (bell != null) found = bell;
                else if (chest != null) found = chest;
                else
                {
                    var hp = h.collider.GetComponentInParent<Health>();
                    if (hp != null && !hp.IsAlive && hp.gameObject != gameObject && hp.GetComponent<NetworkPlayer>() != null) found = hp;
                }
                if (found == null) continue;
                best = h.distance;
                pick = found;
            }
            if (pick == null) return;
            if (pick is Health fallen) { TryRevive(fallen); return; }
            if (Vector3.Distance(pick.transform.position, transform.position) > UseRange)
            {
                CombatTextEvent.Error("Zu weit entfernt.");
                return;
            }
            if (pick is ArenaBell) _progression.RequestRingBell();
            else if (pick is ArenaChest) _progression.RequestArenaChest();
        }

        /// <summary>Selbsttest / Eingabe: diesen Gefallenen wiederbeleben.</summary>
        public bool TryRevive(Health fallen)
        {
            LastReviveResult = "";
            if (fallen == null || fallen.IsAlive) { LastReviveResult = fallen == null ? "kein Ziel" : "lebt"; return false; }
            float d = Vector3.Distance(fallen.transform.position, transform.position);
            if (d > ReviveRange)
            {
                LastReviveResult = $"zu weit ({d:0.0} m)";
                CombatTextEvent.Error("Zu weit entfernt – stell dich neben den Gefallenen.");
                return false;
            }
            var nob = fallen.GetComponent<NetworkObject>();
            if (nob == null || _progression == null) { LastReviveResult = "kein Netzobjekt"; return false; }
            _progression.RequestRevive(nob);
            LastReviveResult = $"gesendet ({d:0.0} m)";
            return true;
        }

        /// <summary>Selbsttest: was beim letzten Versuch passiert ist.</summary>
        public string LastReviveResult { get; private set; } = "";
    }
}
