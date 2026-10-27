using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Fuer Objekte, die sich von selbst bewegen (z. B. der wandernde Riese): nur der Server steuert sie,
    /// Clients sehen die Position ueber einen server-autoritaeren NetworkTransform. So stimmen Treffer
    /// auf dem Server mit dem ueberein, was Spieler sehen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ServerDriven : NetworkBehaviour
    {
        [Tooltip("Diese Komponenten laufen nur auf dem Server")]
        [SerializeField] private Behaviour[] serverOnly = new Behaviour[0];

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsServerInitialized) return; // Host steuert selbst
            foreach (Behaviour b in serverOnly)
                if (b != null) b.enabled = false;
        }
    }
}
