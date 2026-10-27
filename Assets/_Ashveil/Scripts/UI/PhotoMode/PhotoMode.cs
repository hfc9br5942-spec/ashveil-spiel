using System.Collections.Generic;
using System.IO;
using Ashveil.Gameplay.CameraSystem;
using Ashveil.Gameplay.Controls;
using Ashveil.Rendering.PathTracing;
using UnityEngine;

namespace Ashveil.UI.Photo
{
    /// <summary>
    /// Fotomodus (Pathtracing PT6): freie Kamera, Oberflaeche aus, Welt fuer das Bild eingefroren – nur auf diesem
    /// Rechner. Der Server laeuft weiter, die eigene Figur steht still (kein Bewegen, kein Kampf).
    /// Mit Raytracing-Karte rechnet der Pathtracer das Bild fortlaufend schoener, sonst bleibt es beim Rasterbild.
    ///
    /// Kein Spielvorteil (§135): die Kamera bleibt in 40 m um die eigene Figur, und der Pathtracer kennt ohnehin nur
    /// geladene Zellen.
    ///
    /// Tasten: F11 an/aus (Esc beendet) · WASD bewegen, E/Q hoch/runter, Umschalt schneller · rechte Maus umsehen ·
    /// Mausrad Brennweite · Strg+Mausrad Blende · linke Maus Schaerfe auf Punkt · 1–4 Stufe · Tab Raster/Pathtracing ·
    /// V Ansicht · L lebendige Welt · H Tafel · Eingabe Bild speichern.
    /// </summary>
    [DefaultExecutionOrder(20000)]
    public sealed class PhotoMode : MonoBehaviour
    {
        public const string ToggleKey = "Key:F11";
        public const float MaxDistance = 40f;

        public static PhotoMode Instance { get; private set; }
        public static bool Active => Instance != null && Instance._active;

        private bool _active;
        private Camera _cam;
        private MMOCameraController _rig;
        private Transform _anchor;
        private Vector3 _pos;
        private float _yaw, _pitch, _fov;
        private Vector3 _startPos;
        private Quaternion _startRot;
        private float _startFov;
        private readonly List<(Canvas c, bool on)> _canvases = new List<(Canvas, bool)>();
        private bool _hidePanel;
        private bool _usePathTracer = true;
        private PathTracePreset _preset = PathTracePreset.PhotoHigh;
        private float _lastMove = -10f;
        private bool _movingSettingsApplied;
        private string _message = "";
        private float _messageUntil;
        private float _aperture;
        private float _focus = 10f;
        private bool _living;
        private float _ev;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            if (Instance != null) return;
            var go = new GameObject("Ashveil Fotomodus");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PhotoMode>();
        }

        private void Update()
        {
            if (!_active)
            {
                if (!UiPointer.KeyboardCaptured && !UiPointer.GameBlocked && RawInput.WasPressed(ToggleKey)) Enter();
                return;
            }
            if (RawInput.WasPressed(ToggleKey) || RawInput.EscapePressed) { Exit(); return; }
            HandleInput();
        }

        // ------------------------------------------------------------------ Ein/Aus

        public void Enter()
        {
            _cam = Camera.main;
            if (_cam == null) return;
            _rig = _cam.GetComponent<MMOCameraController>();
            _anchor = _rig != null ? _rig.Target : null;
            _startPos = _cam.transform.position;
            _startRot = _cam.transform.rotation;
            _startFov = _cam.fieldOfView;
            _pos = _startPos;
            Vector3 e = _startRot.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180f ? e.x - 360f : e.x;
            _fov = _startFov;
            if (_rig != null) _rig.enabled = false;
            UiPointer.GameBlocked = true;
            UiPointer.KeyboardCaptured = true;

            _canvases.Clear();
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas) { _canvases.Add((c, c.enabled)); c.enabled = false; }

            // Schaerfe auf die eigene Figur
            if (_anchor != null) _focus = Vector3.Distance(_startPos, _anchor.position + Vector3.up * 1.5f);
            _aperture = 0f;
            _active = true;
            _living = false;
            ApplyPathTracing(true);
            Say(AshveilPathTracer.IsRunning ? "Fotomodus – Pathtracing aktiv" : "Fotomodus (ohne Raytracing: " + PathTracingCapability.Describe() + ")");
        }

        public void Exit()
        {
            _active = false;
            AshveilPathTracer.SetEnabled(false);
            if (_cam != null)
            {
                _cam.transform.SetPositionAndRotation(_startPos, _startRot);
                _cam.fieldOfView = _startFov;
            }
            if (_rig != null) _rig.enabled = true;
            foreach (var (c, on) in _canvases) if (c != null) c.enabled = on;
            _canvases.Clear();
            UiPointer.GameBlocked = false;
            UiPointer.KeyboardCaptured = false;
        }

        private void OnDisable()
        {
            if (_active) Exit();
        }

        private void ApplyPathTracing(bool recapture)
        {
            if (!_usePathTracer || !PathTracingCapability.IsSupported)
            {
                AshveilPathTracer.SetEnabled(false);
                return;
            }
            if (!AshveilPathTracer.IsRunning) AshveilPathTracer.SetEnabled(true);
            var pt = AshveilPathTracer.Instance;
            if (pt == null) return;
            pt.targetCamera = _cam;
            pt.showHud = false;
            var s = pt.settings;
            s.ApplyPreset(_preset);
            s.freezeWorld = !_living;
            s.apertureRadius = _aperture;
            s.focusDistance = _focus;
            s.exposureEV = _ev;
            s.matchGameExposure = true;   // Kamera gleicht die Helligkeit dem Spielbild an; Bild auf/ab korrigiert
            s.environment = PathTraceEnvironment.Spielhimmel;
            _movingSettingsApplied = false;
            if (recapture) pt.Recapture();
        }

        // ------------------------------------------------------------------ Steuerung

        private void HandleInput()
        {
            float dt = Time.unscaledDeltaTime;
            bool moved = false;

            if (RawInput.RightMouseHeld)
            {
                Vector2 d = RawInput.MouseDelta * 0.12f;
                if (d.sqrMagnitude > 0f) moved = true;
                _yaw += d.x;
                _pitch = Mathf.Clamp(_pitch - d.y, -85f, 85f);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 dir = Vector3.zero;
            if (RawInput.IsHeld("Key:W")) dir += Vector3.forward;
            if (RawInput.IsHeld("Key:S")) dir += Vector3.back;
            if (RawInput.IsHeld("Key:A")) dir += Vector3.left;
            if (RawInput.IsHeld("Key:D")) dir += Vector3.right;
            if (RawInput.IsHeld("Key:E")) dir += Vector3.up;
            if (RawInput.IsHeld("Key:Q")) dir += Vector3.down;
            if (dir.sqrMagnitude > 0f)
            {
                float speed = RawInput.ShiftHeld ? 8f : 2.5f;
                _pos += rot * dir.normalized * speed * dt;
                moved = true;
            }
            // Kein Spaehen: Kamera bleibt bei der eigenen Figur
            Vector3 center = _anchor != null ? _anchor.position + Vector3.up * 1.5f : _startPos;
            Vector3 off = _pos - center;
            if (off.magnitude > MaxDistance) _pos = center + off.normalized * MaxDistance;

            float wheel = RawInput.ScrollSteps;
            if (Mathf.Abs(wheel) > 0f)
            {
                if (RawInput.CtrlHeld)
                {
                    _aperture = Mathf.Clamp(_aperture + wheel * 0.004f, 0f, 0.08f);
                    Say(_aperture > 0f ? $"Blende {_aperture * 1000f:0} mm Radius" : "Tiefenschaerfe aus");
                }
                else
                {
                    _fov = Mathf.Clamp(_fov - wheel * 2f, 12f, 90f);
                    Say($"Brennweite {FocalLength(_fov):0} mm");
                }
                moved = true;
            }

            if (RawInput.LeftMousePressed)
            {
                Ray r = _cam.ScreenPointToRay(RawInput.MousePosition);
                if (Physics.Raycast(r, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore))
                {
                    _focus = hit.distance;
                    Say($"Schaerfe auf {_focus:0.0} m");
                    moved = true;
                }
            }

            for (int i = 0; i < 4; i++)
                if (RawInput.WasPressed("Key:Digit" + (i + 1)))
                {
                    _preset = (PathTracePreset)i;
                    Say("Stufe: " + PresetName(_preset));
                    ApplyPathTracing(false);
                }
            if (RawInput.WasPressed("Key:Tab"))
            {
                _usePathTracer = !_usePathTracer;
                ApplyPathTracing(true);
                Say(_usePathTracer ? "Pathtracing" : "Rasterbild (Vergleich)");
            }
            if (RawInput.WasPressed("Key:L"))
            {
                _living = !_living;
                ApplyPathTracing(true);
                Say(_living ? "Lebendige Welt: bewegt sich weiter (rauscht staerker)" : "Welt eingefroren");
            }
            if (RawInput.WasPressed("Key:V") && AshveilPathTracer.Instance != null)
            {
                var pt = AshveilPathTracer.Instance;
                pt.settings.view = (PathTraceView)(((int)pt.settings.view + 1) % 12);
                Say("Ansicht: " + pt.settings.view);
            }
            if (RawInput.WasPressed("Key:H")) _hidePanel = !_hidePanel;
            if (RawInput.WasPressed("Key:PageUp") || RawInput.WasPressed("Key:NumpadPlus")) { _ev = Mathf.Min(_ev + 0.5f, 6f); Say($"Belichtung {_ev:+0.0;-0.0;0} EV"); }
            if (RawInput.WasPressed("Key:PageDown") || RawInput.WasPressed("Key:NumpadMinus")) { _ev = Mathf.Max(_ev - 0.5f, -6f); Say($"Belichtung {_ev:+0.0;-0.0;0} EV"); }
            if (RawInput.EnterPressed) Save();

            _cam.transform.SetPositionAndRotation(_pos, rot);
            UnderwaterView.Apply(_pos); // Unterwassersicht folgt der Fotokamera (MOV-169)
            _cam.fieldOfView = _fov;

            var tracer = AshveilPathTracer.Instance;
            if (tracer != null && AshveilPathTracer.IsRunning)
            {
                tracer.settings.apertureRadius = _aperture;
                tracer.settings.exposureEV = _ev;
                tracer.settings.focusDistance = _focus;
                // Beim Bewegen schnelle Vorschau, im Stillstand die gewaehlte Stufe
                if (moved) _lastMove = Time.unscaledTime;
                bool moving = Time.unscaledTime - _lastMove < 0.35f;
                if (moving && !_movingSettingsApplied && _preset != PathTracePreset.Preview)
                {
                    tracer.settings.ApplyPreset(PathTracePreset.Preview);
                    _movingSettingsApplied = true;
                }
                else if (!moving && _movingSettingsApplied)
                {
                    tracer.settings.ApplyPreset(_preset);
                    _movingSettingsApplied = false;
                }
            }
        }

        private void Save()
        {
            string dir = Application.isEditor ? Path.Combine("Logs", "Ashveil", "Pathtracing") : Path.Combine(Application.persistentDataPath, "Fotos");
            string path = Path.Combine(dir, "ASHVEIL_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            var pt = AshveilPathTracer.Instance;
            bool hidden = _hidePanel;
            _hidePanel = true;
            _messageUntil = 0f;
            if (pt != null && AshveilPathTracer.IsRunning)
                pt.SaveCapture(path, true, true, w => { _hidePanel = hidden; Say("Gespeichert: " + w); });
            else StartCoroutine(SaveRaster(dir, path, hidden));
        }

        private System.Collections.IEnumerator SaveRaster(string dir, string path, bool hidden)
        {
            _messageUntil = 0f;   // Tafel und Hinweis nicht mit aufs Bild
            yield return null;
            yield return new WaitForEndOfFrame();
            Directory.CreateDirectory(dir);
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path + ".png", shot.EncodeToPNG());
            Destroy(shot);
            _hidePanel = hidden;
            Say("Gespeichert: " + path + ".png");
        }

        private void Say(string msg)
        {
            _message = msg;
            _messageUntil = Time.unscaledTime + 3f;
        }

        private static float FocalLength(float fovDeg) => 12f / Mathf.Tan(0.5f * fovDeg * Mathf.Deg2Rad);   // Vollformat-Hoehe 24 mm

        private static string PresetName(PathTracePreset p) => p switch
        {
            PathTracePreset.Preview => "Vorschau",
            PathTracePreset.PhotoHigh => "Foto",
            PathTracePreset.PhotoUltra => "Foto Ultra",
            _ => "Kino"
        };

        // ------------------------------------------------------------------ Tafel

        private GUIStyle _box, _small;

        private void OnGUI()
        {
            if (!_active) return;
            if (_box == null)
            {
                _box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, richText = true, fontSize = 13, padding = new RectOffset(10, 10, 8, 8) };
                _box.normal.textColor = new Color(0.93f, 0.88f, 0.78f);
                _small = new GUIStyle(_box) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            }
            if (Time.unscaledTime < _messageUntil)
                GUI.Box(new Rect(Screen.width * 0.5f - 260f, 20f, 520f, 34f), _message, _small);
            if (_hidePanel) return;

            var pt = AshveilPathTracer.Instance;
            string status;
            if (pt != null && AshveilPathTracer.IsRunning)
            {
                var s = pt.Stats;
                string goal = pt.settings.targetSamples > 0 ? pt.settings.targetSamples.ToString() : "∞";
                status = s.Done ? $"<b>fertig</b> · {s.Samples} Proben · {s.AgeSeconds:0} s"
                                : $"rechnet … {s.Samples}/{goal} Proben · {s.AgeSeconds:0} s · aktiv {s.ActiveShare * 100f:0} %";
            }
            else status = "Rasterbild";
            string text =
                $"<b>FOTOMODUS</b> · {PresetName(_preset)} · {(_living ? "lebendige Welt" : "Welt eingefroren")}\n" +
                $"{status}\n" +
                $"Brennweite {FocalLength(_fov):0} mm · Schaerfe {_focus:0.0} m · Blende {(_aperture > 0 ? (_aperture * 1000f).ToString("0") + " mm" : "aus")} · Belichtung {(pt != null && AshveilPathTracer.IsRunning ? pt.Stats.ExposureEV : _ev):+0.0;-0.0;0} EV (Korrektur {_ev:+0.0;-0.0;0})\n" +
                "<size=11>WASD/E/Q bewegen · rechte Maus umsehen · Rad Brennweite · Strg+Rad Blende · Klick = Schaerfe\n" +
                "1–4 Stufe · Bild auf/ab Belichtung · Tab Raster/Pathtracing · V Ansicht · L lebendig · H Tafel · Eingabe speichern · F11/Esc zurueck</size>";
            GUI.Box(new Rect(16f, Screen.height - 132f, 560f, 116f), text, _box);
        }
    }
}
