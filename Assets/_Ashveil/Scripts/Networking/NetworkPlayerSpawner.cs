using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Server: erzeugt fuer jeden verbundenen Spieler eine Spielfigur – im Halbkreis am Wachtfeuer,
    /// damit man neue Spieler sofort sieht (siehe Produktionsplan, Geburtsort).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class NetworkPlayerSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkObject playerPrefab;
        [Tooltip("Mitte und Blickrichtung des Spawnbereichs")]
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private float ringRadius = 2.2f;
        [SerializeField] private int slots = 8;

        private NetworkManager _manager;
        private int _next;

        private void Start()
        {
            _manager = GetComponentInParent<NetworkManager>();
            if (_manager == null) _manager = InstanceFinder.NetworkManager;
            if (_manager == null)
            {
                Debug.LogWarning("[Ashveil] Netzwerk: kein NetworkManager gefunden – Spieler werden nicht erzeugt.", this);
                return;
            }
            _manager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
        }

        private void OnDestroy()
        {
            if (_manager != null && _manager.SceneManager != null)
                _manager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
        }

        private void OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
        {
            if (!asServer) return;
            if (playerPrefab == null)
            {
                Debug.LogWarning("[Ashveil] Netzwerk: kein Spieler-Prefab eingetragen.", this);
                return;
            }

            Vector3 center = spawnPoint != null ? spawnPoint.position : Vector3.zero;
            Quaternion rot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
            // Erster Spieler genau auf dem Spawnpunkt, weitere im Halbkreis daneben
            Vector3 pos = center;
            int i = _next++;
            if (i > 0)
            {
                int slot = (i - 1) % Mathf.Max(1, slots);
                float side = (slot % 2 == 0 ? 1f : -1f) * (1 + slot / 2);
                Vector3 right = rot * Vector3.right;
                Vector3 back = rot * Vector3.back;
                pos = center + right * (side * ringRadius * 0.6f) + back * (Mathf.Abs(side) * 0.35f);
            }

            NetworkObject nob = _manager.GetPooledInstantiated(playerPrefab, pos, rot, true);
            _manager.ServerManager.Spawn(nob, conn);
            _manager.SceneManager.AddOwnerToDefaultScene(nob);
            Debug.Log($"[Ashveil] Netzwerk: Spieler {conn.ClientId + 1} betritt die Welt.");
        }
    }
}
