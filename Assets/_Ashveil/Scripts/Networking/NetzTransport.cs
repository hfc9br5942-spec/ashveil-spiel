using System;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// 22.10.2026 (Port-7770-Leck): Hilfen, damit der Spielport nach dem Ende eines Laufs wirklich frei ist.
    ///
    /// Ursache: Mono legt Sockets unter Windows vererbbar an (WSASocket ohne WSA_FLAG_NO_HANDLE_INHERIT). Jeder Kindprozess, den
    /// der Editor waehrend eines Laufs mit Handle-Vererbung startet (Process.Start mit UseShellExecute=false), erbt eine Kopie des
    /// Server-Sockets UDP 7770. FishNet schliesst beim Play-Ende nur das Handle des Editors; die Kopie im Kindprozess haelt den
    /// Port weiter (Get-NetUDPEndpoint nennt trotzdem den Editor als Besitzer, weil er den Socket angelegt hat), bis der
    /// Kindprozess endet. Darum half auch ein Stopp beim Play-Ende allein nicht (REP-031: StopConnection akzeptiert, Port blieb belegt).
    /// 22.10.2026 (Kommentar korrigiert): Der festgestellte Kindprozess ist relay_win.exe aus dem Paket com.unity.ai.assistant
    /// (Library/PackageCache/com.unity.ai.assistant@…/RelayApp~; gestartet von RelayService.StartDefaultRelay mit
    /// UseShellExecute=false und „--shutdown-delay 180“, er lebt also ueber den Anlass hinaus weiter). Belegt per Zeitzusammenhang
    /// (Relay waehrend eines Laufs gestartet ↔ Port danach belegt), nicht per Handle-Liste des Prozesses.
    /// Die frueher hier genannten Unity-Helfer (Shader-Compiler, Import-Worker, AutoQuitter) sind nicht belegt. Andere vererbende
    /// Kindprozesse (etwa unsere Test-Clients per Process.Start) wirken gleich – deshalb setzt der Schutz am Socket an, nicht an
    /// einem bestimmten Prozess.
    ///
    /// <see cref="NichtVererben"/> nimmt den Tugboat-Sockets direkt nach dem Binden das Vererbungs-Flag (das FishNet-Paket bleibt
    /// unveraendert; die Felder werden nur gelesen). <see cref="Stoppen"/> beendet Client, Server und Transport idempotent.
    ///
    /// 22.10.2026 (Runde 6, Codex-Paket A, Punkt 4) – RESTGRENZE, NICHT ATOMAR: Der Socket entsteht vererbbar (Mono/WSASocket) und
    /// verliert das Flag erst danach (NichtVererben, gerufen aus NetzSocketWaechter: beim Host-Start im selben Bild, bei spaeteren
    /// Starts erst mit dem „Started“-Ereignis – Server ein Bild spaeter, Client erst nach erfolgreichem Verbindungsaufbau). Startet
    /// in diesem Fenster irgendein Teil des Editors einen Kindprozess mit Handle-Vererbung, hat dieser die Kopie schon geerbt;
    /// SetHandleInformation am Elternhandle nimmt bereits geerbte Kopien NICHT zurueck. Der Schutz senkt also die Wahrscheinlichkeit
    /// eines festgehaltenen Ports, garantiert aber nichts. Eine echte Garantie gaebe es nur, wenn die Nichtvererbung schon beim
    /// Erzeugen greift (WSA_FLAG_NO_HANDLE_INHERIT in LiteNetLib/Tugboat = Paketaenderung, bewusst nicht gemacht). Kein neuer
    /// Port-Leck-Fall belegt; der beobachtete Fix bleibt.
    /// </summary>
    public static class NetzTransport
    {
        private const BindingFlags Intern = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly FieldInfo SocketManagerFeld = typeof(CommonSocket).GetField("NetManager", Intern);
        private static readonly FieldInfo V4Feld = typeof(LiteNetLib.NetManager).GetField("_udpSocketv4", Intern);
        private static readonly FieldInfo V6Feld = typeof(LiteNetLib.NetManager).GetField("_udpSocketv6", Intern);
        private static bool _feldWarnung;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private const uint HandleFlagInherit = 0x1;
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetHandleInformation(IntPtr handle, out uint flags);
#endif

        /// <summary>
        /// Macht die Sockets des Tugboat-Transports (Server und Client, IPv4 und IPv6) nicht vererbbar. Mehrfach aufrufbar,
        /// ohne Wirkung, wenn nichts laeuft oder nicht Windows. Direkt nach StartConnection aufrufen (das Binden ist synchron).
        /// </summary>
        public static void NichtVererben(NetworkManager manager)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                if (manager == null || manager.TransportManager == null) return;
                if (!(manager.TransportManager.Transport is Tugboat tugboat)) return;
                if (SocketManagerFeld == null || V4Feld == null || V6Feld == null)
                {
                    if (!_feldWarnung) Debug.LogWarning("[Ashveil] Netz: Tugboat-Felder nicht gefunden (FishNet-Version?) – Sockets bleiben vererbbar.");
                    _feldWarnung = true;
                    return;
                }
                SeiteEntvererben(tugboat.ServerSocket, "Server");
                SeiteEntvererben(tugboat.ClientSocket, "Client");
            }
            catch (Exception e) { Debug.LogWarning("[Ashveil] Netz: Sockets nicht entvererbt: " + e.Message); }
#endif
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private static void SeiteEntvererben(CommonSocket seite, string rolle)
        {
            if (seite == null) return;
            if (!(SocketManagerFeld.GetValue(seite) is LiteNetLib.NetManager nm)) return;
            Einzeln(V4Feld.GetValue(nm) as Socket, rolle, "IPv4");
            Einzeln(V6Feld.GetValue(nm) as Socket, rolle, "IPv6");
        }

        private static void Einzeln(Socket s, string rolle, string familie)
        {
            if (s == null) return;
            IntPtr h;
            try { h = s.Handle; }
            catch (ObjectDisposedException) { return; }
            if (h == IntPtr.Zero || !GetHandleInformation(h, out uint flags) || (flags & HandleFlagInherit) == 0) return;
            bool ok = SetHandleInformation(h, HandleFlagInherit, 0);
            int port = s.LocalEndPoint is System.Net.IPEndPoint ep ? ep.Port : 0;
            // Nur der Server-Port ist fuer Folgelaeufe wichtig; Client-Sockets (Zufallsport) still behandeln.
            if (rolle == "Server" || !ok)
                Debug.Log($"[Ashveil] Netz: {rolle}-Socket {familie} :{port} war vererbbar – " +
                          (ok ? "jetzt nicht mehr (Kindprozesse erben den Port nicht)." : $"Entvererben fehlgeschlagen (Fehler {Marshal.GetLastWin32Error()})."));
        }
#endif

        /// <summary>
        /// Beendet Client und Server dieses NetworkManagers und schliesst danach den Transport (Tugboat.Shutdown). Idempotent:
        /// laeuft nichts, passiert nichts. Jeder Schritt einzeln abgesichert, damit ein Fehler beim Client den Server-Stopp nicht
        /// verhindert. Rueckgabe: kurze Beschreibung, was vorher lief (fuer das Log), oder null, wenn nichts lief.
        /// </summary>
        public static string Stoppen(NetworkManager manager)
        {
            if (manager == null) return null;
            Transport t = manager.TransportManager != null ? manager.TransportManager.Transport : null;
            LocalConnectionState server = LocalConnectionState.Stopped, client = LocalConnectionState.Stopped;
            try
            {
                if (t != null) { server = t.GetConnectionState(true); client = t.GetConnectionState(false); }
            }
            catch (Exception) { /* Zustand nur fuers Log */ }
            bool lief = server != LocalConnectionState.Stopped || client != LocalConnectionState.Stopped ||
                        (manager.ServerManager != null && manager.ServerManager.Started) || (manager.ClientManager != null && manager.ClientManager.Started);
            if (!lief) return null;

            try { if (manager.ClientManager != null && manager.ClientManager.Started) manager.ClientManager.StopConnection(); }
            catch (Exception e) { Debug.LogWarning("[Ashveil] Netz: Client-Stopp: " + e.Message); }
            try { if (manager.ServerManager != null && manager.ServerManager.Started) manager.ServerManager.StopConnection(true); }
            catch (Exception e) { Debug.LogWarning("[Ashveil] Netz: Server-Stopp: " + e.Message); }
            // Auch „Starting“ (gebunden, aber noch nicht gemeldet) und Reste: Transport selbst schliessen.
            try { if (t != null) t.Shutdown(); }
            catch (Exception e) { Debug.LogWarning("[Ashveil] Netz: Transport-Shutdown: " + e.Message); }
            return $"Server {server}, Client {client}";
        }
    }
}
