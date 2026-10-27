using System;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace Ashveil.Networking
{
    /// <summary>
    /// Startet das Netzwerk und zeigt eine kleine Statusanzeige (F9).
    ///
    ///   Im Editor:                 startet automatisch als Host (Server + eigener Spieler) – Play druecken, spielen.
    ///   Test-Client:               Kommandozeile "-ashveil-join 127.0.0.1" verbindet automatisch.
    ///   Server ohne Spieler:       Kommandozeile "-ashveil-server".
    ///   Eigener Host (ohne Editor): Kommandozeile "-ashveil-host".
    ///   Sonst (Build ohne Angabe): Auswahl Host / Verbinden im Fenster.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        [SerializeField] private NetworkManager manager;
        [SerializeField] private ushort port = 7770;
        [SerializeField] private bool autoHostInEditor = true;

        private string _address = "127.0.0.1";
        private bool _panelOpen;
        private GUIStyle _box, _label, _button, _field;
        private Texture2D _bg;
        private float _styleScale = -1f;

        private void Awake()
        {
            if (manager == null) manager = GetComponent<NetworkManager>();
            Application.runInBackground = true; // Test-Client im Hintergrund darf nicht einfrieren
        }

        private void Start()
        {
            if (manager == null) return;
            Transport t = manager.TransportManager != null ? manager.TransportManager.Transport : null;
            if (t != null) t.SetPort(port);

            string join = ArgValue("-ashveil-join");
            if (join != null)
            {
                Join(string.IsNullOrEmpty(join) ? "127.0.0.1" : join);
                return;
            }
            if (HasArg("-ashveil-host"))
            {
                Host(); // eigenstaendig (z. B. Leistungstest im fertigen Spiel, ohne Editor)
                return;
            }
            if (HasArg("-ashveil-server"))
            {
                manager.ServerManager.StartConnection();
                return;
            }
            if (Application.isEditor && autoHostInEditor) Host();
            else _panelOpen = true;
        }

        private void Update()
        {
            if (Ashveil.Gameplay.Controls.RawInput.WasPressed(Ashveil.Gameplay.Controls.GameKey.ToggleNetwork)) _panelOpen = !_panelOpen;
        }

        // ================================================================== Verbindungen

        /// <summary>
        /// Host = Server + eigener Spieler. 22.10.2026 (MEHRSPIELER-05): Der eigene Client startet erst, wenn der Server wirklich
        /// läuft. Vorher startete er bedingungslos – war Port <see cref="port"/> belegt (zweiter Start von Spielen.bat, Editor im
        /// Play, altes Spiel beendet sich noch), meldete Tugboat das nur im Log, und der Client verband sich still mit dem fremden
        /// Server (eigene Figur dort abgemeldet bzw. Charakter-Editor des Editor-Testservers) oder stand ohne Figur in der Welt.
        /// Jetzt bleibt er aus, und das Netzwerk-Fenster (F9) nennt den Grund.
        /// 23.10.2026 (Runde-6-Reste b): Der eigene Client verbindet sich immer mit der lokalen Adresse (<see cref="LokaleAdresse"/>),
        /// nicht mit der zuletzt per <see cref="Join"/> gesetzten Client-Adresse des Transports.
        /// </summary>
        public void Host()
        {
            if (manager == null) return;
            _meldung = null;
            _panelOpen = false;
            if (manager.ServerManager.Started)
            {
                manager.ClientManager.StartConnection(LokaleAdresse());   // 23.10.2026: lokal, nicht die letzte Join-Adresse
                return;
            }
            if (!_hostWartet)
            {
                manager.ServerManager.OnServerConnectionState += HostServerZustand;   // vor StartConnection abonnieren
                _hostWartet = true;
            }
            manager.ServerManager.StartConnection();
        }

        private bool _hostWartet;
        private string _meldung;

        /// <summary>Tests: Grund, warum der Host nicht starten konnte (leer = keiner).</summary>
        public string Meldung => _meldung ?? "";

        private void HostServerZustand(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                HostAbmelden();
                // 23.10.2026 (Runde-6-Reste b): lokal verbinden. Läuft noch ein erfolgloses F9-Verbinden („Starting“), beendet Tugboat
                // dessen Socket in StartConnection und beginnt neu mit der lokalen Adresse.
                if (manager != null && !manager.ClientManager.Started) manager.ClientManager.StartConnection(LokaleAdresse());
            }
            else if (args.ConnectionState == LocalConnectionState.Stopping || args.ConnectionState == LocalConnectionState.Stopped)
            {
                // Stopp ohne vorheriges „Started“: der Server kam nicht hoch (meist Port belegt) – kein Client, Grund im F9-Fenster
                HostAbmelden();
                _meldung = $"Server konnte nicht starten – vermutlich ist Port {port} belegt. Läuft ASHVEIL schon (zweites Fenster, Unity-Editor)?";
                _panelOpen = true;
                Debug.LogWarning("[Ashveil] Netzwerk: Host nicht gestartet – Server kam nicht hoch (Port " + port + " belegt?), eigener Spieler verbindet sich nicht (MEHRSPIELER-05).");
            }
        }

        /// <summary>
        /// 23.10.2026 (Runde-6-Reste b): Adresse, unter der der eigene Client den eigenen Server erreicht. Vorher übernahm Host() die
        /// zuletzt per <see cref="Join"/> gesetzte Client-Adresse des Transports: Nach einem erfolglosen F9-Verbinden (Tippfehler,
        /// Freund offline) verband sich der eigene Client des neuen Hosts mit der alten, fremden Adresse und kam nie an – der Server
        /// lief, die eigene Figur fehlte. Jetzt immer lokal: die feste IPv4-Bindeadresse des Servers, wenn eine gesetzt ist (in den
        /// Szenen leer = alle Schnittstellen), sonst 127.0.0.1. Das Eingabefeld im F9-Fenster (_address) bleibt stehen.
        /// </summary>
        private string LokaleAdresse()
        {
            Transport t = manager != null && manager.TransportManager != null ? manager.TransportManager.Transport : null;
            string bind = t != null ? t.GetServerBindAddress(IPAddressType.IPv4) : null;
            return string.IsNullOrEmpty(bind) || bind == "0.0.0.0" ? "127.0.0.1" : bind;
        }

        private void HostAbmelden()
        {
            if (!_hostWartet) return;
            _hostWartet = false;
            if (manager != null && manager.ServerManager != null) manager.ServerManager.OnServerConnectionState -= HostServerZustand;
        }

        /// <summary>
        /// 22.10.2026 (Runde 6, Codex-Paket A): Ein ausdrückliches Verbinden meldet eine noch wartende Host-Absicht ZUERST ab – sonst
        /// rief ein später eintreffendes „Started“ des eigenen Servers ClientManager.StartConnection ein zweites Mal (Tugboat bricht
        /// den gerade laufenden Verbindungsaufbau dann ab und beginnt neu), bzw. ein Fehlstart des Servers setzte die Port-Meldung und
        /// öffnete das F9-Fenster über der neuen Verbindung. Der eigene Server, falls er gerade startet, läuft weiter (unverändert).
        /// </summary>
        public void Join(string address)
        {
            if (manager == null) return;
            HostAbmelden();   // 22.10.2026 (Runde 6, Codex-Paket A)
            _meldung = null;
            _address = address;
            Transport t = manager.TransportManager.Transport;
            if (t != null) t.SetClientAddress(address);
            manager.ClientManager.StartConnection();
            _panelOpen = false;
        }

        /// <summary>
        /// 22.10.2026 (Runde 6, Codex-Paket A): Trennen meldet die wartende Host-Absicht ZUERST ab und berücksichtigt auch „Starting“
        /// (wie <see cref="NetzTransport.Stoppen"/>, das hier jetzt genutzt wird). Vorher stoppte es nur Manager mit Started == true:
        /// Tugboat bindet in StartConnection synchron, meldet „Started“ aber erst im nächsten Bild (Warteschlange). Ein Trennen in
        /// diesem Fenster ließ den Server weiterlaufen, und das verspätete „Started“ startete über HostServerZustand den eigenen
        /// Client doch noch. Die Warteschlange liefert „Started“ auch nach dem Stopp noch aus (danach Stopping/Stopped) – darum muss
        /// die Abmeldung VOR dem Stopp stehen; sie verhindert zugleich die falsche „Port belegt“-Meldung beim eigenen Stopp.
        /// </summary>
        public void Disconnect()
        {
            if (manager == null) return;
            HostAbmelden();
            NetzTransport.Stoppen(manager);   // Client, Server und Transport (auch Starting), idempotent
        }

        private static bool HasArg(string name)
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Wert nach einem Argument; "" wenn ohne Wert; null wenn nicht vorhanden.</summary>
        private static string ArgValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                return i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : string.Empty;
            }
            return null;
        }

        // ================================================================== Anzeige

        private string Status()
        {
            if (manager == null) return "kein Netzwerk";
            bool server = manager.ServerManager.Started, client = manager.ClientManager.Started;
            if (server && client) return $"Host · {manager.ServerManager.Clients.Count} Spieler";
            if (server) return $"Server · {manager.ServerManager.Clients.Count} Spieler";
            if (client) return "Verbunden mit " + _address;
            return "Nicht verbunden";
        }

        private static float UiScale => Mathf.Clamp(Screen.height / 900f, 1f, 2.5f);

        private void EnsureStyles()
        {
            float s = UiScale;
            if (_box != null && Mathf.Approximately(s, _styleScale)) return;
            _styleScale = s;
            if (_bg == null)
            {
                _bg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _bg.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.07f, 0.85f));
                _bg.Apply();
            }
            int pad = Mathf.RoundToInt(10 * s);
            _box = new GUIStyle(GUI.skin.box) { normal = { background = _bg }, padding = new RectOffset(pad, pad, pad, pad) };
            _label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(14 * s), richText = true, normal = { textColor = new Color(0.94f, 0.92f, 0.87f) } };
            _button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(14 * s) };
            _field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(14 * s) };
        }

        private void OnGUI()
        {
            if (Ashveil.Gameplay.Controls.UiPointer.GameBlocked && !_panelOpen) return; // M7: Charakter-Editor im Vollbild
            EnsureStyles();
            float s = UiScale;
            if (!_panelOpen)
            {
                GUI.Label(new Rect(10 * s, Screen.height - 34 * s, 520 * s, 30 * s), $"<color=#bbb>Netz: {Status()}   ·   F9</color>", _label);
                return;
            }

            GUILayout.BeginArea(new Rect(10 * s, Screen.height - 250 * s, 360 * s, 240 * s), _box);
            GUILayout.Label("<b>Netzwerk</b>   " + Status(), _label);
            if (!string.IsNullOrEmpty(_meldung)) GUILayout.Label("<color=#e0a050>" + _meldung + "</color>", _label);   // MEHRSPIELER-05 (22.10.)
            GUILayout.Space(6 * s);
            bool connected = manager != null && (manager.ServerManager.Started || manager.ClientManager.Started);
            if (!connected)
            {
                if (GUILayout.Button("Host starten (Server + Spieler)", _button)) Host();
                GUILayout.Space(4 * s);
                GUILayout.BeginHorizontal();
                _address = GUILayout.TextField(_address, _field, GUILayout.Width(180 * s));
                if (GUILayout.Button("Verbinden", _button)) Join(_address.Trim());
                GUILayout.EndHorizontal();
            }
            else if (GUILayout.Button("Trennen", _button)) Disconnect();
            GUILayout.Space(6 * s);
            GUILayout.Label("<color=#999>F9 schliesst dieses Fenster</color>", _label);
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            HostAbmelden();
            if (_bg != null) Destroy(_bg);
        }
    }
}
