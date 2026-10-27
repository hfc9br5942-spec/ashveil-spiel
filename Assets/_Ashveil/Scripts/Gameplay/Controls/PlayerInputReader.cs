using Ashveil.Gameplay.Characters;
using UnityEngine;

namespace Ashveil.Gameplay.Controls
{
    /// <summary>Kampf-Absichten eines Frames (Flanken, nicht gehalten).</summary>
    public struct CombatInput
    {
        public bool Ability1;
        public bool Ability2;
        public bool SwapWeapon;
        public bool Reload;
        public bool TargetNext;
        public bool ClearTarget;
        /// <summary>Boss-Anatomie: naechsten Schwachpunkt des Ziels waehlen (frei belegbar, Standard G).</summary>
        public bool NextWeakPoint;
        /// <summary>Kurzer Linksklick ohne Ziehen: Ziel unter dem Mauszeiger auswaehlen.</summary>
        public bool ClickSelect;
        public Vector2 ClickPosition;
        /// <summary>Kurzer Rechtsklick ohne Ziehen: benutzen/pluendern (Leiche, spaeter NPCs, Objekte).</summary>
        public bool ClickInteract;
        public Vector2 InteractPosition;
    }

    /// <summary>
    /// Uebersetzt Tastatur/Maus in MMO-Steuerung:
    ///
    ///   Linke Maustaste gehalten  -> Kamera kreist frei um den Charakter
    ///   Rechte Maustaste gehalten -> Charakter schaut in Kamerarichtung, A/D werden zu Seitschritten
    ///   Beide Maustasten          -> vorwaerts laufen
    ///   W/S, A/D, Q/E             -> vor/zurueck, drehen, seitwaerts
    ///   Num-Lock / Maus 5         -> Autorun (W oder S bricht ab)
    ///   Num-/                     -> Gehen umschalten
    ///
    /// Liefert pro Frame einen <see cref="MoveCommand"/> und die Kamerasteuerung.
    /// Enthaelt keine Bewegungslogik.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [Tooltip("Cursor waehrend Maussteuerung verstecken und fixieren")]
        [SerializeField] private bool lockCursorWhileMouseLook = true;

        private bool _autorun;
        private bool _walkMode, _jogMode, _epicMode;
        private bool _jumpPressed;
        private bool _dodgePressed;
        private bool _cursorLockedByUs;

        // Klick-Erkennung: Linksklick kurz und ohne Mausbewegung = Auswahl, sonst Kamera drehen.
        private const float ClickMaxDuration = 0.25f;
        private const float ClickMaxTravelPixels = 6f;
        private float _lmbDownTime;
        private Vector2 _lmbDownPosition;
        private float _lmbTravel;
        private bool _lmbDragging;
        private Vector2 _cursorRestorePosition;
        // Klicks, die auf einem Fenster begonnen haben, gehoeren der Oberflaeche
        private bool _lmbOnUi, _rmbOnUi;
        private float _rmbDownTime;
        private Vector2 _rmbDownPosition;
        private float _rmbTravel;

        /// <summary>Linke Maustaste allein: Kamera kreist, Charakter dreht sich nicht mit.</summary>
        public bool IsOrbiting { get; private set; }
        /// <summary>Rechte Maustaste: Charakter folgt der Kamera.</summary>
        public bool IsSteering { get; private set; }
        public bool IsMouseLookActive => IsOrbiting || IsSteering;
        public Vector2 LookDelta { get; private set; }
        public float ZoomSteps { get; private set; }
        public bool Autorun => _autorun;
        /// <summary>EINSTIEG-05 (21.10.): Im Tod den Autolauf beenden – sonst rennt die Figur nach dem Erwachen am Wachtfeuer von selbst los.</summary>
        public void AutolaufAus() => _autorun = false;
        public bool WalkMode => _walkMode;
        public CombatInput Combat { get; private set; }

        private CombatInput _injected;
        private float _injectedForward;
        private float _injectedTurn;
        private bool _injectedJump;

        /// <summary>Selbsttest: vorwaerts laufen (0..1, bis zum naechsten Aufruf) und/oder einmal springen.</summary>
        /// <summary>Selbsttest: Ausweichrolle beim naechsten Befehl.</summary>
        public void InjectDodge() => _dodgePressed = true;

        /// <summary>Automatisierung (Lasttest-Bots): drehen -1..1, bis zum naechsten Aufruf.</summary>
        public void InjectTurn(float turn) => _injectedTurn = Mathf.Clamp(turn, -1f, 1f);

        public void InjectMovement(float forward, bool jump)
        {
            _injectedForward = Mathf.Clamp(forward, -1f, 1f);
            _injectedJump |= jump;
        }

        /// <summary>
        /// Selbsttest / Automatisierung: Tasten fuer den naechsten Frame "druecken" (wird mit der echten Eingabe
        /// zusammengefuehrt und danach geloescht).
        /// </summary>
        public void InjectCombat(CombatInput input)
        {
            _injected.Ability1 |= input.Ability1;
            _injected.Ability2 |= input.Ability2;
            _injected.SwapWeapon |= input.SwapWeapon;
            _injected.Reload |= input.Reload;
            _injected.TargetNext |= input.TargetNext;
            _injected.NextWeakPoint |= input.NextWeakPoint;
            _injected.ClearTarget |= input.ClearTarget;
        }

        /// <summary>Einmal pro Frame vor der Simulation aufrufen.</summary>
        public void Poll()
        {
            // Esc-Regel (UI-204): erst Fenster, dann Ziel, dann Spielmenue. Meist hat das Spielmenue den Takt in diesem Bild
            // schon ausgefuehrt; ein zweiter Aufruf tut nichts.
            UiFensterstapel.Takt();
            if (UiPointer.GameBlocked)
            {
                Combat = default;
                _injected = default;
                IsSteering = IsOrbiting = false;
                LookDelta = Vector2.zero;
                ZoomSteps = 0f;
                _autorun = false;
                _jumpPressed = _dodgePressed = false;
                UpdateCursor(false);
                return;
            }
            bool focused = Application.isFocused;
            if (focused && RawInput.LeftMousePressed) _lmbOnUi = UiPointer.OverUi;
            if (focused && RawInput.RightMousePressed)
            {
                _rmbOnUi = UiPointer.OverUi;
                _rmbDownTime = Time.unscaledTime;
                _rmbDownPosition = RawInput.MousePosition;
                _rmbTravel = 0f;
            }
            if (!RawInput.LeftMouseHeld && !RawInput.LeftMouseReleased) _lmbOnUi = false;
            if (!RawInput.RightMouseHeld && !RawInput.RightMouseReleased) _rmbOnUi = false;
            bool lmb = focused && RawInput.LeftMouseHeld && !_lmbOnUi;
            bool rmb = focused && RawInput.RightMouseHeld && !_rmbOnUi;
            Vector2 rawDelta = RawInput.MouseDelta;
            if (rmb) _rmbTravel += rawDelta.magnitude;

            // Linke Maustaste: erst ab etwas Bewegung oder laengerem Halten wird gedreht –
            // ein kurzer Klick bleibt eine Zielauswahl und der Cursor springt nicht.
            if (focused && RawInput.LeftMousePressed && !_lmbOnUi)
            {
                _lmbDownTime = Time.unscaledTime;
                _lmbDownPosition = RawInput.MousePosition;
                _lmbTravel = 0f;
                _lmbDragging = false;
            }
            if (lmb)
            {
                _lmbTravel += rawDelta.magnitude;
                if (_lmbTravel > ClickMaxTravelPixels || Time.unscaledTime - _lmbDownTime > ClickMaxDuration)
                    _lmbDragging = true;
            }

            bool keys = !UiPointer.KeyboardCaptured;
            var combat = !keys ? new CombatInput() : new CombatInput
            {
                SwapWeapon = RawInput.WasPressed(GameKey.SwapWeapon),
                Reload = RawInput.WasPressed(GameKey.Reload),
                TargetNext = RawInput.WasPressed(GameKey.TargetNext),
                ClearTarget = UiFensterstapel.ZielAufhebenAngefordert,   // nicht die rohe Esc-Taste: offene Fenster gehen vor
                NextWeakPoint = RawInput.WasPressed(Ashveil.Core.Settings.InputBindingsStore.Current.weakPointKey)
            };
            if (focused && RawInput.LeftMouseReleased && !_lmbDragging && !rmb && !_lmbOnUi)
            {
                combat.ClickSelect = true;
                combat.ClickPosition = _lmbDownPosition;
            }
            if (focused && RawInput.RightMouseReleased && !_rmbOnUi &&
                Time.unscaledTime - _rmbDownTime <= ClickMaxDuration && _rmbTravel <= ClickMaxTravelPixels)
            {
                combat.ClickInteract = true;
                combat.InteractPosition = _rmbDownPosition;
            }
            if (!lmb) _lmbDragging = false;
            // M6.6: Angriff, Spezial, Ausweichen, Gegenstaende … kommen von der frei belegbaren Actionbar
            ActionBarInput.Poll(keys && focused, ref combat, ref _dodgePressed);
            combat.Ability1 |= _injected.Ability1;
            combat.Ability2 |= _injected.Ability2;
            combat.SwapWeapon |= _injected.SwapWeapon;
            combat.Reload |= _injected.Reload;
            combat.TargetNext |= _injected.TargetNext;
            combat.ClearTarget |= _injected.ClearTarget;
            combat.NextWeakPoint |= _injected.NextWeakPoint;
            _injected = default;
            Combat = combat;

            IsSteering = rmb;
            IsOrbiting = lmb && !rmb && _lmbDragging;
            LookDelta = IsMouseLookActive ? rawDelta : Vector2.zero;
            // Mausrad über einem Fenster, der Karte oder der Minimap gehört der Oberfläche (Karten r2, 08.10.: Minimap-Zoom)
            ZoomSteps = focused && !UiPointer.OverUi ? RawInput.ScrollSteps : 0f;

            // Tippt man im Chat (Eingabefeld hat den Fokus), ruhen alle Spieltasten (Max 06.10.2026); der Autolauf läuft weiter.
            if (keys)
            {
                if (RawInput.WasPressed(GameKey.Autorun)) _autorun = !_autorun;
                if (RawInput.WasPressed(GameKey.Forward) || RawInput.WasPressed(GameKey.Backward)) _autorun = false;
                // Gangarten schliessen sich aus: dieselbe Taste nochmal = zurueck zum Rennen
                if (RawInput.WasPressed(GameKey.WalkToggle)) { _walkMode = !_walkMode; _jogMode = false; _epicMode = false; }
                if (RawInput.WasPressed(GameKey.JogToggle)) { _jogMode = !_jogMode; _walkMode = false; _epicMode = false; }
                if (RawInput.WasPressed(GameKey.EpicWalkToggle)) { _epicMode = !_epicMode; _walkMode = false; _jogMode = false; }

                // Sprung als Flanke merken, bis der naechste Befehl gebaut wird.
                _jumpPressed |= RawInput.WasPressed(GameKey.Jump);
                _dodgePressed |= RawInput.WasPressed(GameKey.Dodge);
            }

            UpdateCursor(IsMouseLookActive);
        }

        /// <summary>Baut den Bewegungsbefehl. cameraYaw = aktuelle Kamerarichtung in Grad.</summary>
        public MoveCommand BuildCommand(float cameraYaw)
        {
            if (UiPointer.GameBlocked) return new MoveCommand { DesiredYaw = cameraYaw };
            bool bothMouse = IsSteering && RawInput.LeftMouseHeld;
            bool keys = !UiPointer.KeyboardCaptured;   // Chat: Tastatur gehört dem Eingabefeld, Maus und Autolauf bleiben

            float forward = 0f;
            if ((keys && RawInput.IsHeld(GameKey.Forward)) || _autorun || bothMouse) forward += 1f;
            if (keys && RawInput.IsHeld(GameKey.Backward)) forward -= 1f;
            forward += _injectedForward;

            float strafe = 0f;
            if (keys && RawInput.IsHeld(GameKey.StrafeLeft)) strafe -= 1f;
            if (keys && RawInput.IsHeld(GameKey.StrafeRight)) strafe += 1f;

            float turn = 0f;
            float turnKeys = !keys ? 0f : (RawInput.IsHeld(GameKey.TurnRight) ? 1f : 0f) - (RawInput.IsHeld(GameKey.TurnLeft) ? 1f : 0f);
            if (IsSteering) strafe = Mathf.Clamp(strafe + turnKeys, -1f, 1f); // A/D werden Seitschritte
            else turn = Mathf.Clamp(turnKeys + _injectedTurn, -1f, 1f);

            var cmd = new MoveCommand
            {
                Forward = Mathf.Clamp(forward, -1f, 1f),
                Strafe = strafe,
                Turn = turn,
                SteerWithCamera = IsSteering,
                DesiredYaw = cameraYaw,
                Jump = _jumpPressed || _injectedJump,
                Sprint = keys && RawInput.IsHeld(GameKey.Sprint),
                Walk = _walkMode,
                Jog = _jogMode,
                EpicWalk = _epicMode,
                Dodge = _dodgePressed,
                // Schwimmen (MOV-169): Sprungtaste gehalten = aufsteigen, X gehalten = absinken – nicht beim Tippen im Chat
                SwimUp = keys && RawInput.IsHeld(GameKey.Jump),
                SwimDown = keys && RawInput.IsHeld(GameKey.SwimDown)
            };
            _jumpPressed = false;
            _injectedJump = false;
            _dodgePressed = false;
            return cmd;
        }

        private void UpdateCursor(bool mouseLook)
        {
            if (!lockCursorWhileMouseLook) return;
            if (mouseLook && !_cursorLockedByUs)
            {
                // Position merken, damit der Mauszeiger nach dem Drehen wieder dort erscheint (wie in MMOs).
                _cursorRestorePosition = RawInput.LeftMouseHeld && !RawInput.RightMouseHeld ? _lmbDownPosition : RawInput.MousePosition;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                _cursorLockedByUs = true;
            }
            else if (!mouseLook && _cursorLockedByUs)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _cursorLockedByUs = false;
                RawInput.WarpCursor(_cursorRestorePosition);
            }
        }

        private void OnDisable()
        {
            UpdateCursor(false);
        }
    }
}
