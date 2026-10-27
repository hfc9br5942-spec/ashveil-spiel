using UnityEngine;

namespace Ashveil.Core
{
    /// <summary>
    /// Zentrale Layer-Namen. Der Setup-Wizard legt diese Layer an
    /// (Menue: Ashveil > Milestone 1 > Sandbox-Szene erstellen).
    /// </summary>
    public static class GameLayers
    {
        public const string Player = "Player";
        public const string Enemy = "Enemy";
        public const string Interactable = "Interactable";

        /// <summary>Alles, woran die Kamera nicht haengen bleiben soll.</summary>
        public static int CameraIgnoreMask()
        {
            return LayerMask.GetMask(Player, Enemy, Interactable, "Ignore Raycast", "UI");
        }

        /// <summary>Layermaske fuer Kamerakollision (alles ausser Charakteren/Triggern).</summary>
        public static int CameraCollisionMask()
        {
            return ~CameraIgnoreMask();
        }
    }
}
