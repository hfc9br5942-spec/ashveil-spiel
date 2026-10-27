using System.Collections;
using FishNet;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>Trennen und neu verbinden (ueberlebt das Zerstoeren der Spielfigur).</summary>
    public sealed class ReconnectHelper : MonoBehaviour
    {
        public static void Run()
        {
            var go = new GameObject("Ashveil Reconnect");
            DontDestroyOnLoad(go);
            go.AddComponent<ReconnectHelper>();
        }

        private IEnumerator Start()
        {
            var cm = InstanceFinder.ClientManager;
            if (cm != null) cm.StopConnection();
            yield return new WaitForSecondsRealtime(2.5f);
            if (cm != null) cm.StartConnection();
            yield return new WaitForSecondsRealtime(1f);
            Destroy(gameObject);
        }
    }
}
