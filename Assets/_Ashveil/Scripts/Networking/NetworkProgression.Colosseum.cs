using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Kolosseum Pass 4: Spieler-Aktionen im Ring (Glocke laeuten, Mitspieler wiederbeleben, Truhe oeffnen).
    /// Der Client meldet nur die Absicht, der Server (<see cref="ColosseumInstanceHandler"/>) prueft alles.
    /// </summary>
    public sealed partial class NetworkProgression
    {
        public void RequestRingBell() { if (IsOwner) ServerRingBell(); }
        public void RequestRevive(NetworkObject target) { if (IsOwner && target != null) ServerReviveAlly(target); }
        public void RequestArenaChest() { if (IsOwner) ServerOpenArenaChest(); }

        [ServerRpc]
        private void ServerRingBell()
        {
            if (Loaded) { var h = ColosseumInstanceHandler.Current; if (h != null) h.ServerRingBell(_inv); }
        }

        [ServerRpc]
        private void ServerReviveAlly(NetworkObject target)
        {
            if (!Loaded || target == null) return;
            var other = target.GetComponent<NetworkInventory>();
            if (ColosseumInstanceHandler.Current == null || other == null)
                Debug.Log($"[Ashveil] Server: Wiederbeleben von {_inv.CharacterName} ohne Ablauf/Ziel ({(ColosseumInstanceHandler.Current != null)}, {(other != null)}).");
            var hr = ColosseumInstanceHandler.Current;
            if (other != null && hr != null) hr.ServerStartRevive(_inv, other);
        }

        [ServerRpc]
        private void ServerOpenArenaChest()
        {
            if (Loaded) { var h = ColosseumInstanceHandler.Current; if (h != null) h.ServerOpenChest(_inv); }
        }
    }
}
