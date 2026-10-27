using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// 22.10.2026 (Port-7770-Leck, siehe <see cref="NetzTransport"/>): nimmt den Tugboat-Sockets jedes NetworkManagers das
    /// Vererbungs-Flag, sobald sie gebunden sind – damit kein Kindprozess (relay_win.exe des AI-Assistant-Pakets, Test-Client) den
    /// Spielport erbt und ihn nach dem Lauf festhaelt. Legt sich bei jedem Play-/Spielstart selbst an (auch ohne Domain-Reload) und
    /// braucht keine Szenen- oder Prefab-Aenderung.
    ///   22.10.2026 (Kommentar korrigiert): Start laeuft NICHT direkt nach NetworkBootstrap.Start – das Objekt entsteht erst nach dem
    ///   Szenenladen (AfterSceneLoad), dazwischen laufen andere Start-Methoden (im Spieltest-Log z. B. HudRoot.Start), die
    ///   Reihenfolge -49 ordnet es nicht unmittelbar hinter -50 ein. Es laeuft aber im selben Bild wie der automatische Host-Start und
    ///   vor FishNets „Local server is started“; der Server-Socket ist dann schon gebunden (StartConnection bindet synchron) und wird
    ///   hier entvererbt (Beleg: Smoke_2.log 22.10. 14:32). Spaetere Starts (F9-Fenster, Wiederverbinden, weitere Manager) faengt das
    ///   „Started“-Ereignis; ein zweiter Aufruf ist wirkungslos (Flag schon weg).
    ///   22.10.2026 (Runde 6, Codex-Paket A, Punkt 4): Restgrenze – NICHT atomar. Zwischen Binden und Entvererben bleibt ein Fenster
    ///   (Host-Start: Rest des Bildes; spaetere Starts: bis zum „Started“-Ereignis, beim Client bis zum Verbindungsaufbau). Ein in
    ///   diesem Fenster gestarteter vererbender Kindprozess behaelt seine Kopie; das Entfernen des Flags wirkt nur fuer kuenftige
    ///   Kindprozesse. Einzelheiten und warum keine Paketaenderung: <see cref="NetzTransport"/>.
    /// </summary>
    [DefaultExecutionOrder(-49)]
    [AddComponentMenu("")]
    public sealed class NetzSocketWaechter : MonoBehaviour
    {
        private readonly Dictionary<NetworkManager, (System.Action<ServerConnectionStateArgs> server, System.Action<ClientConnectionStateArgs> client)> _abos =
            new Dictionary<NetworkManager, (System.Action<ServerConnectionStateArgs>, System.Action<ClientConnectionStateArgs>)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Anlegen()
        {
            if (FindAnyObjectByType<NetzSocketWaechter>() != null) return;
            var go = new GameObject("[Ashveil] Netz-Socketwaechter");
            DontDestroyOnLoad(go);   // wird beim Play-Ende mit zerstoert (kein DontSave)
            go.AddComponent<NetzSocketWaechter>();
        }

        private void Start() => Abgleichen();

        private void Update()
        {
            if (Abweichung()) Abgleichen();
        }

        /// <summary>
        /// 22.10.2026 (Gegenpruefung): Instanzen statt nur Anzahl vergleichen – wird im selben Bild ein Manager zerstoert und ein neuer
        /// angelegt, bleibt die Anzahl gleich, der neue waere sonst nie abonniert worden.
        /// </summary>
        private bool Abweichung()
        {
            if (NetworkManager.Instances.Count != _abos.Count) return true;
            foreach (NetworkManager nm in NetworkManager.Instances)
                if (nm != null && !_abos.ContainsKey(nm)) return true;
            return false;
        }

        private void Abgleichen()
        {
            // Zerstoerte Manager austragen
            var weg = new List<NetworkManager>();
            foreach (var kv in _abos) if (kv.Key == null) weg.Add(kv.Key);
            foreach (NetworkManager m in weg) _abos.Remove(m);

            foreach (NetworkManager nm in NetworkManager.Instances)
            {
                if (nm == null || _abos.ContainsKey(nm) || nm.ServerManager == null || nm.ClientManager == null) continue;
                NetworkManager m = nm;
                System.Action<ServerConnectionStateArgs> s = a => { if (a.ConnectionState == LocalConnectionState.Started) NetzTransport.NichtVererben(m); };
                System.Action<ClientConnectionStateArgs> c = a => { if (a.ConnectionState == LocalConnectionState.Started) NetzTransport.NichtVererben(m); };
                m.ServerManager.OnServerConnectionState += s;
                m.ClientManager.OnClientConnectionState += c;
                _abos[m] = (s, c);
                NetzTransport.NichtVererben(m);   // schon gebunden (Host-Start in NetworkBootstrap.Start)?
            }
        }

        private void OnDestroy()
        {
            foreach (var kv in _abos)
            {
                if (kv.Key == null) continue;
                if (kv.Key.ServerManager != null) kv.Key.ServerManager.OnServerConnectionState -= kv.Value.server;
                if (kv.Key.ClientManager != null) kv.Key.ClientManager.OnClientConnectionState -= kv.Value.client;
            }
            _abos.Clear();
        }
    }
}
