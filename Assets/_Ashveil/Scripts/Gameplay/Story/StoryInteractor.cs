using Ashveil.Core.Events;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Quests;
using UnityEngine;

namespace Ashveil.Gameplay.Story
{
    /// <summary>
    /// Eigene Figur: Rechtsklick auf eine Geschichts-Handlung (Fohlen, Weidetor, Pferd). Laeuft nach dem Spieler-Update wie
    /// Beute, Gespraeche und Kolosseum. Ist eine Handlung in Reichweite, zeigt der Hinweis am Bildschirm „Rechtsklick: …“
    /// (<see cref="Offered"/>); der Hinweis selbst ist ebenfalls anklickbar.
    /// </summary>
    [DefaultExecutionOrder(101)]
    public sealed class StoryInteractor : MonoBehaviour
    {
        private PlayerInputReader _input;
        private Camera _cam;

        /// <summary>Handlung, die der Hinweis gerade anbietet (Tests/Anzeige).</summary>
        public static StoryInteractable Offered { get; private set; }

        private void Awake() => _input = GetComponent<PlayerInputReader>();

        private void LateUpdate()
        {
            Offered = StoryInteractable.NearestAvailable(transform.position);
            if (_input == null || !_input.isActiveAndEnabled) return;
            CombatInput c = _input.Combat;
            if (!c.ClickInteract) return;
            if (_cam == null) _cam = Camera.main;
            StoryInteractable pick = null;
            if (_cam != null)
            {
                Ray ray = _cam.ScreenPointToRay(c.InteractPosition);
                float best = float.MaxValue;
                foreach (RaycastHit h in Physics.RaycastAll(ray, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                {
                    if (h.distance >= best) continue;
                    foreach (StoryInteractable si in h.collider.GetComponentsInParent<StoryInteractable>())
                    {
                        if (si == null || !si.Available) continue;
                        best = h.distance;
                        pick = si;
                        break;
                    }
                }
            }
            if (pick == null) return; // Rechtsklick ins Leere bleibt fuer Kamera/Ziel frei
            if (Vector3.Distance(pick.transform.position, transform.position) > pick.Range + 0.5f)
            {
                CombatTextEvent.Error("Zu weit entfernt.");
                return;
            }
            Use(pick);
        }

        public static void Use(StoryInteractable si)
        {
            if (si == null) return;
            StoryClient.Send(si.Action, "");
        }
    }
}
