using UnityEngine;

namespace Ashveil.Gameplay.Social
{
    /// <summary>Markiert eine Spielerfigur fuer Rechtsklick-Menues (Name kommt vom Netzwerk-Layer).</summary>
    public sealed class PlayerIdentity : MonoBehaviour
    {
        public string DisplayName { get; set; } = "";
        public bool IsLocal { get; set; }
    }
}
