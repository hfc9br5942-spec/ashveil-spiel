using Ashveil.Gameplay.Controls;
using UnityEngine;

namespace Ashveil.Gameplay.Social
{
    /// <summary>
    /// Eigener Spieler: Rechtsklick auf einen anderen Spieler oeffnet ein kleines Menue
    /// (In Gruppe einladen, Fluestern) – die Oberflaeche zeigt es an.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private float clickRange = 60f;

        private PlayerInputReader _input;
        private Camera _cam;

        private void Awake()
        {
            _input = GetComponent<PlayerInputReader>();
        }

        private void LateUpdate()
        {
            if (_input == null || !_input.isActiveAndEnabled) return;
            CombatInput c = _input.Combat;
            if (!c.ClickInteract) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            Ray ray = _cam.ScreenPointToRay(c.InteractPosition);
            PlayerIdentity best = null;
            float bestDist = float.MaxValue;
            foreach (RaycastHit h in Physics.RaycastAll(ray, clickRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                PlayerIdentity p = h.collider.GetComponentInParent<PlayerIdentity>();
                if (p == null || p.IsLocal || p.gameObject == gameObject || string.IsNullOrEmpty(p.DisplayName) || h.distance >= bestDist) continue;
                best = p;
                bestDist = h.distance;
            }
            if (best != null) SocialClient.RaisePlayerClicked(best.DisplayName, c.InteractPosition);
        }
    }
}
