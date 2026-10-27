using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Prüfkörper der Begehung (#29, nur Test, wird zur Laufzeit erzeugt und wieder entfernt): merkt sich den letzten seitlichen
    /// Kontakt des CharacterControllers (Wand, Fels, Stamm, Figur), damit die Suite bei einem Hänger das Hindernis nennen kann.
    /// Bodenkontakte (Normale steiler als 45° nach oben) zählen nicht.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BegehungsKoerper : MonoBehaviour
    {
        /// <summary>Simulationszeit der Suite (für das Alter des Kontakts).</summary>
        public float SimTime { get; set; }
        public Collider LastObstacle { get; private set; }
        public Vector3 LastPoint { get; private set; }
        public Vector3 LastNormal { get; private set; }
        public float LastTime { get; private set; } = -100f;

        public void ResetContacts()
        {
            LastObstacle = null;
            LastTime = -100f;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y > 0.7f) return;   // Boden
            LastObstacle = hit.collider;
            LastPoint = hit.point;
            LastNormal = hit.normal;
            LastTime = SimTime;
        }
    }
}
