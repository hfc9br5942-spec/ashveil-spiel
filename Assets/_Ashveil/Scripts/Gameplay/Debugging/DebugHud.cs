using Ashveil.Core.Events;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Controls;
using UnityEngine;

namespace Ashveil.Gameplay.Debugging
{
    /// <summary>
    /// Entwickler-Anzeige (F1): FPS, Bewegungszustand, Ausdauer, Steuerungshilfe.
    /// Nur fuer Tests; das echte HUD kommt in einem spaeteren Milestone (uGUI).
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        [SerializeField] private bool visible = false;

        private PlayerCharacter _player;
        private PlayerCharacter _bodiesOf;          // EINSTIEG-03: für diese Figur ist _bodies gesucht
        private CharacterBodySwitcher _bodies;
        private float _fpsSmoothed = 60f;
        private GUIStyle _box;
        private GUIStyle _label;
        private Texture2D _bg;
        private Texture2D _barFill;
        private float _styleScale = -1f;

        private void OnEnable()
        {
            EventBus<LocalPlayerSpawnedEvent>.Subscribe(OnPlayerSpawned);
        }

        private void OnDisable()
        {
            EventBus<LocalPlayerSpawnedEvent>.Unsubscribe(OnPlayerSpawned);
        }

        private void Start()
        {
            if (_player == null) _player = FindAnyObjectByType<PlayerCharacter>();
        }

        private void OnPlayerSpawned(LocalPlayerSpawnedEvent evt)
        {
            if (evt.Player != null) _player = evt.Player.GetComponent<PlayerCharacter>();
        }

        private void Update()
        {
            if (!UiPointer.KeyboardCaptured && RawInput.WasPressed(GameKey.ToggleDebugHud)) visible = !visible;   // nicht beim Tippen
            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            _fpsSmoothed = Mathf.Lerp(_fpsSmoothed, 1f / dt, 0.05f);
        }

        /// <summary>Skaliert mit der Bildschirmhoehe, damit der Text auf grossen Monitoren nicht winzig ist.</summary>
        private static float UiScale => Mathf.Clamp(Screen.height / 900f, 1f, 2.5f);

        private void EnsureStyles()
        {
            float scale = UiScale;
            if (_box != null && Mathf.Approximately(scale, _styleScale)) return;
            _styleScale = scale;
            if (_bg == null) _bg = MakeTex(new Color(0.05f, 0.05f, 0.07f, 0.78f));
            if (_barFill == null) _barFill = MakeTex(new Color(0.85f, 0.68f, 0.3f, 0.95f));
            int pad = Mathf.RoundToInt(10 * scale);
            _box = new GUIStyle(GUI.skin.box) { normal = { background = _bg }, padding = new RectOffset(pad, pad, pad, pad) };
            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(14 * scale),
                richText = true,
                wordWrap = false,
                normal = { textColor = new Color(0.94f, 0.92f, 0.87f) }
            };
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (!visible)
            {
                if (Ashveil.Gameplay.Controls.UiPointer.GameBlocked) return; // M7: Charakter-Editor im Vollbild
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ASHVEIL_TEST_CLIENT
                GUI.Label(new Rect(Screen.width - 220 * UiScale, 10, 210 * UiScale, 30 * UiScale), "<color=#bbb>F1: Debug-Anzeige</color>", _label);
#endif
                return;
            }

            float ui = UiScale;
            // Oben rechts, damit Spieler- und Zielrahmen oben links frei bleiben.
            GUILayout.BeginArea(new Rect(Screen.width - 390 * ui - 10, 10, 390 * ui, 620 * ui), _box);
            GUILayout.Label($"<b>ASHVEIL · Sandbox</b>   {_fpsSmoothed:0} FPS", _label);
            GUILayout.Label($"Input: {RawInput.Backend}", _label);

            if (_player != null)
            {
                CharacterMotor m = _player.Motor;
                MotorState s = m.State;
                string mode = s.Swimming ? (s.Underwater ? "Tauchen" : "Schwimmen") : !s.Grounded ? "In der Luft" : s.IsSprinting ? "Sprint" : s.IsEpicWalking ? "Epischer Gang" : s.IsWalking ? "Gehen" : s.IsJogging ? "Joggen" : m.HorizontalSpeed > 0.1f ? "Laufen" : "Stehen";
                if (s.LandingRecovery > 0f) mode += " (harte Landung)";
                if (m.IsOnSteepSlope) mode += " (zu steil)";
                GUILayout.Space(4);
                GUILayout.Label($"Zustand: <b>{mode}</b>", _label);
                GUILayout.Label($"Tempo: {m.HorizontalSpeed:0.0} m/s   Vertikal: {s.Velocity.y:0.0} m/s", _label);
                GUILayout.Label($"Position: {s.Position.x:0} / {s.Position.y:0.0} / {s.Position.z:0}", _label);
                if (!string.IsNullOrEmpty(Ashveil.Core.WorldCells.DebugInfo)) GUILayout.Label(Ashveil.Core.WorldCells.DebugInfo, _label);
                GUILayout.Label($"Autorun: {(_player.Input.Autorun ? "an" : "aus")}   Gehen: {(_player.Input.WalkMode ? "an" : "aus")}", _label);

                GUILayout.Label($"Ausdauer {s.Stamina:0}/{m.Settings.maxStamina:0}{(s.Exhausted ? "  erschoepft" : "")}", _label);
                Rect bar = GUILayoutUtility.GetRect(340 * ui, 8 * ui);
                GUI.DrawTexture(bar, _bg);
                bar.width *= Mathf.Clamp01(s.Stamina / Mathf.Max(1f, m.Settings.maxStamina));
                GUI.DrawTexture(bar, _barFill);
            }
            else
            {
                GUILayout.Label("Kein Spieler gefunden.", _label);
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>Steuerung</b>", _label);
            GUILayout.Label("W/S vor/zurueck · A/D drehen · Q/E seitwaerts", _label);
            GUILayout.Label("Rechte Maus: Blick = Laufrichtung (A/D seitwaerts)", _label);
            GUILayout.Label("Linke Maus: Kamera umkreisen · Beide: laufen", _label);
            GUILayout.Label("Mausrad Zoom · Leertaste Sprung · Shift Sprint", _label);
            GUILayout.Label("Num-Lock / Maus 5 Autorun · Num-/ Gehen", _label);
            GUILayout.Label("<b>Kampf</b>: Tab / Klick Ziel · Esc Ziel weg", _label);
            GUILayout.Label("1 Auto-Angriff · 2 Spezial · 4 Hechtrolle", _label);
            GUILayout.Label("Im Sprung: 1 Luftschlag · 2 Sturzangriff", _label);
            GUILayout.Label("F Waffe wechseln · R Nachladen", _label);
            // EINSTIEG-03 (21.10., Nachbesserung): F2 nur nennen, wo es wirkt – vernetzte Spielerfiguren haben die Taste nie
            // (auch nicht im Development-/Spieltest-Build), nur Figuren in Offline-Szenen
            if (_bodiesOf != _player)
            {
                _bodiesOf = _player;
                _bodies = _player != null ? _player.GetComponent<CharacterBodySwitcher>() : null;
            }
            GUILayout.Label(_bodies != null && _bodies.DebugSwitchAllowed ? "F2 Koerper wechseln (m/w) · F1 Anzeige ein/aus" : "F1 Anzeige ein/aus", _label);
            GUILayout.Label("F9 Netzwerk (Host / Verbinden)", _label);
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (_bg != null) Destroy(_bg);
            if (_barFill != null) Destroy(_barFill);
        }
    }
}
