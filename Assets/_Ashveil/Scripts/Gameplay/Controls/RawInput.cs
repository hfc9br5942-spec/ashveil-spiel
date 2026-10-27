using UnityEngine;
#if ASHVEIL_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ashveil.Gameplay.Controls
{
    /// <summary>Alle Spieltasten. Belegung zentral in <see cref="RawInput"/>.</summary>
    public enum GameKey
    {
        Forward,
        Backward,
        TurnLeft,
        TurnRight,
        StrafeLeft,
        StrafeRight,
        Jump,
        Sprint,
        WalkToggle,
        /// <summary>Joggen umschalten (Num *)</summary>
        JogToggle,
        /// <summary>Epischen Gang umschalten (Num -)</summary>
        EpicWalkToggle,
        Autorun,
        ToggleDebugHud,
        // Kampf
        Ability1,
        Ability2,
        SwapWeapon,
        Reload,
        TargetNext,
        ClearTarget,
        /// <summary>Entwicklung: Koerpermodell wechseln (bis zum Charaktereditor)</summary>
        SwitchBody,
        /// <summary>Ausweichrolle (Taste 4)</summary>
        Dodge,
        /// <summary>Netzwerk-Fenster (F9)</summary>
        ToggleNetwork,
        /// <summary>Selbsttest starten (F10)</summary>
        SelfTest,
        /// <summary>Inventar (I oder B)</summary>
        Inventory,
        /// <summary>Charakter/Ausruestung (C)</summary>
        Character,
        /// <summary>Questlog (L)</summary>
        QuestLog,
        /// <summary>Einstellungen (O)</summary>
        Settings,
        /// <summary>Faehigkeitenbuch (K)</summary>
        Spellbook,
        /// <summary>M8: Emote-Menue (J)</summary>
        Emotes,
        /// <summary>P1: Talente (N)</summary>
        Talents,
        /// <summary>Schwimmen: absinken (X, gehalten) – MOV-169. Aufsteigen = Sprungtaste gehalten.</summary>
        SwimDown
    }

    /// <summary>
    /// Duenne Schicht ueber der Hardware. Funktioniert mit dem neuen Input System
    /// (empfohlen, Standard in Unity 6) und faellt sonst auf den alten Input Manager zurueck.
    ///
    /// Tastenbelegung (MMO-Standard):
    ///   W/S vor/zurueck, A/D drehen (mit rechter Maustaste: seitwaerts), Q/E seitwaerts,
    ///   Leertaste springen, Shift sprinten, Num-Lock oder Maustaste 5 Autorun,
    ///   Num-/ Gehen umschalten, F1 Debug-Anzeige.
    ///   Kampf: Tab Ziel wechseln, Esc nach der Esc-Regel (UiFensterstapel: Fenster, dann Ziel, dann Spielmenue),
    ///   F Waffe wechseln, R Nachladen, Linksklick (ohne Ziehen)
    ///   waehlt ein Ziel. Die zwoelf Actionbar-Tasten und die Wechseltaste sind frei belegbar
    ///   (<see cref="Ashveil.Core.Settings.InputBindings"/>, abgefragt ueber <see cref="IsHeld(string)"/>/<see cref="WasPressed(string)"/>).
    /// </summary>
    public static class RawInput
    {
        /// <summary>Faktor, um Legacy-Mausachsen grob in Pixel umzurechnen.</summary>
        private const float LegacyMousePixelScale = 10f;

#if ASHVEIL_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        public const string Backend = "Input System";

        private static bool KeyHeld(Key key)
        {
            Keyboard kb = Keyboard.current;
            return kb != null && kb[key].isPressed;
        }

        private static bool KeyPressed(Key key)
        {
            Keyboard kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }

        public static bool IsHeld(GameKey k)
        {
            switch (k)
            {
                case GameKey.Forward: return KeyHeld(Key.W) || KeyHeld(Key.UpArrow);
                case GameKey.Backward: return KeyHeld(Key.S) || KeyHeld(Key.DownArrow);
                case GameKey.TurnLeft: return KeyHeld(Key.A) || KeyHeld(Key.LeftArrow);
                case GameKey.TurnRight: return KeyHeld(Key.D) || KeyHeld(Key.RightArrow);
                case GameKey.StrafeLeft: return KeyHeld(Key.Q);
                case GameKey.StrafeRight: return KeyHeld(Key.E);
                case GameKey.Jump: return KeyHeld(Key.Space);
                case GameKey.Sprint: return KeyHeld(Key.LeftShift);
                case GameKey.SwimDown: return KeyHeld(Key.X);
                default: return false;
            }
        }

        public static bool WasPressed(GameKey k)
        {
            switch (k)
            {
                case GameKey.Jump: return KeyPressed(Key.Space);
                // Gangarten frei belegbar (Max 29.09.: eigene, umbelegbare Umschalttasten) – Standard Num / · Num * · Num -
                case GameKey.WalkToggle: return WasPressed(Ashveil.Core.Settings.InputBindingsStore.Current.walkKey);
                case GameKey.JogToggle: return WasPressed(Ashveil.Core.Settings.InputBindingsStore.Current.jogKey);
                case GameKey.EpicWalkToggle: return WasPressed(Ashveil.Core.Settings.InputBindingsStore.Current.epicWalkKey);
                case GameKey.Autorun:
                    return KeyPressed(Key.NumLock) ||
                           (Mouse.current != null && Mouse.current.forwardButton.wasPressedThisFrame);
                case GameKey.ToggleDebugHud: return KeyPressed(Key.F1);
                case GameKey.Forward: return KeyPressed(Key.W) || KeyPressed(Key.UpArrow);
                case GameKey.Backward: return KeyPressed(Key.S) || KeyPressed(Key.DownArrow);
                // Ability1/Ability2/Dodge liegen seit M6.6 auf der Actionbar (frei belegbar, siehe ActionBarInput)
                case GameKey.SwapWeapon: return KeyPressed(Key.F);
                case GameKey.Reload: return KeyPressed(Key.R);
                case GameKey.TargetNext: return KeyPressed(Key.Tab);
                case GameKey.ClearTarget: return KeyPressed(Key.Escape);
                case GameKey.SwitchBody: return KeyPressed(Key.F2);
                case GameKey.ToggleNetwork: return KeyPressed(Key.F9);
                case GameKey.SelfTest: return KeyPressed(Key.F10);
                case GameKey.Inventory: return KeyPressed(Key.I) || KeyPressed(Key.B);
                case GameKey.Character: return KeyPressed(Key.C);
                case GameKey.QuestLog: return KeyPressed(Key.L);
                case GameKey.Settings: return KeyPressed(Key.O);
                case GameKey.Spellbook: return KeyPressed(Key.K);
                case GameKey.Emotes: return KeyPressed(Key.J);
                case GameKey.Talents: return KeyPressed(Key.N);
                default: return false;
            }
        }

        // ---- Frei belegbare Tasten (Actionbar): Codes "Key:Digit1", "Mouse:Middle" …
        private static readonly System.Collections.Generic.Dictionary<string, Key> ParsedKeys = new System.Collections.Generic.Dictionary<string, Key>();

        private static bool TryKey(string code, out Key key)
        {
            key = Key.None;
            if (string.IsNullOrEmpty(code) || !code.StartsWith("Key:", System.StringComparison.Ordinal)) return false;
            if (ParsedKeys.TryGetValue(code, out key)) return key != Key.None;
            if (!System.Enum.TryParse(code.Substring(4), out key)) key = Key.None;
            ParsedKeys[code] = key;
            return key != Key.None;
        }

        private static UnityEngine.InputSystem.Controls.ButtonControl MouseButton(string code)
        {
            Mouse m = Mouse.current;
            if (m == null) return null;
            switch (code)
            {
                case "Mouse:Middle": return m.middleButton;
                case "Mouse:Back": return m.backButton;
                case "Mouse:Forward": return m.forwardButton;
                default: return null;
            }
        }

        /// <summary>Frei belegte Taste gerade gedrueckt gehalten?</summary>
        public static bool IsHeld(string code)
        {
            if (TryKey(code, out Key k)) return KeyHeld(k);
            var b = MouseButton(code);
            return b != null && b.isPressed;
        }

        /// <summary>Frei belegte Taste in diesem Frame gedrueckt?</summary>
        public static bool WasPressed(string code)
        {
            if (TryKey(code, out Key k)) return KeyPressed(k);
            var b = MouseButton(code);
            return b != null && b.wasPressedThisFrame;
        }

        /// <summary>Kurzname fuer die Anzeige – richtet sich nach dem Tastaturlayout (deutsch: "ß", "´").</summary>
        public static string Label(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            switch (code)
            {
                case "Mouse:Middle": return "M3";
                case "Mouse:Back": return "M4";
                case "Mouse:Forward": return "M5";
            }
            if (!TryKey(code, out Key k)) return "?";
            switch (k)
            {
                case Key.LeftCtrl: return "Strg";
                case Key.RightCtrl: return "Strg R";
                case Key.LeftShift: return "Shift";
                case Key.RightShift: return "Shift R";
                case Key.LeftAlt: return "Alt";
                case Key.Space: return "Leer";
                case Key.Tab: return "Tab";
            }
            if (k >= Key.Numpad0 && k <= Key.Numpad9) return "N" + (k - Key.Numpad0);
            string name = Keyboard.current != null ? Keyboard.current[k].displayName : k.ToString();
            if (string.IsNullOrEmpty(name)) name = k.ToString();
            // Tottasten meldet Windows mit Namen („Akut“, „Zirkumflex“) – als Zeichen anzeigen
            switch (name.ToLowerInvariant())
            {
                case "akut": case "acute": return "´";
                case "gravis": case "grave": return "`";
                case "zirkumflex": case "circumflex": return "^";
            }
            return name.Length <= 4 ? name.ToUpperInvariant() : name.Substring(0, 4);
        }

        /// <summary>
        /// Tastenbelegung aendern: erste in diesem Frame gedrueckte Taste/Maustaste (ohne linke/rechte Maustaste –
        /// die steuern Kamera und Auswahl – und ohne Esc, das bricht ab).
        /// </summary>
        public static bool TryCaptureAnyPress(out string code)
        {
            code = null;
            Keyboard kb = Keyboard.current;
            if (kb != null)
                foreach (UnityEngine.InputSystem.Controls.KeyControl kc in kb.allKeys)
                    if (kc != null && kc.wasPressedThisFrame && kc.keyCode != Key.Escape)
                    {
                        code = "Key:" + kc.keyCode;
                        return true;
                    }
            foreach (string m in new[] { "Mouse:Middle", "Mouse:Back", "Mouse:Forward" })
                if (WasPressed(m)) { code = m; return true; }
            return false;
        }

        /// <summary>Setzt den Mauszeiger auf eine Bildschirmposition (nach dem Maus-Drehen).</summary>
        public static void WarpCursor(Vector2 position)
        {
            if (Mouse.current != null) Mouse.current.WarpCursorPosition(position);
        }

        public static bool LeftMousePressed => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool RightMousePressed => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        public static bool RightMouseReleased => Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame;
        public static bool ShiftHeld => Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
        public static bool CtrlHeld => Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
        public static bool AltHeld => Keyboard.current != null && (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);
        public static bool LeftMouseReleased => Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static bool LeftMouseHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;
        public static bool RightMouseHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;

        /// <summary>Mausbewegung dieses Frames in Pixeln.</summary>
        public static Vector2 MouseDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;

        /// <summary>-1, 0 oder +1 Zoomstufe (Mausrad), unabhaengig von der Treiberskalierung.</summary>
        public static float ScrollSteps
        {
            get
            {
                if (Mouse.current == null) return 0f;
                float y = Mouse.current.scroll.ReadValue().y;
                return Mathf.Abs(y) < 0.01f ? 0f : Mathf.Sign(y);
            }
        }

        // ---- Texteingabe (Chat)
        private static readonly System.Text.StringBuilder Typed = new System.Text.StringBuilder();
        private static bool _textHooked;
        private static Keyboard _textKeyboard;   // EINSTIEG-01 (21.10.): die Tastatur, an der der Puffer hängt (für den Testweg)

        private static void HookText()
        {
            if (_textHooked || Keyboard.current == null) return;
            _textHooked = true;
            _textKeyboard = Keyboard.current;
            _textKeyboard.onTextInput += c => { if (!char.IsControl(c) && Typed.Length < 256) Typed.Append(c); };
        }

        /// <summary>
        /// Tests (EINSTIEG-01, 21.10.): Text eingeben wie eine physische Tastatur – als TextEvent in die Warteschlange des
        /// Input Systems (Keyboard.onTextInput → gemeinsamer Puffer → wer im nächsten Bild zuerst ConsumeTypedText ruft).
        /// Ohne Fensterfokus schaltet das Input System die Tastatur ab und verwirft Ereignisse; dann geht der Text direkt an
        /// Keyboard.OnTextInput (gleicher Empfänger und gleiche Bildreihenfolge, nur ohne Warteschlange).
        /// Rückgabe: "warteschlange", "direkt" oder "" (keine Tastatur).
        /// </summary>
        public static string TestTextEingeben(string text)
        {
            HookText();
            Keyboard kb = _textKeyboard;
            if (kb == null || !kb.added || string.IsNullOrEmpty(text)) return "";
            bool warteschlange = kb.enabled;
            foreach (char c in text)
            {
                if (warteschlange) UnityEngine.InputSystem.InputSystem.QueueTextEvent(kb, c);
                else kb.OnTextInput(c);
            }
            return warteschlange ? "warteschlange" : "direkt";
        }

        /// <summary>Seit dem letzten Aufruf getippte Zeichen (ohne Steuerzeichen) – leert den Puffer.</summary>
        public static string ConsumeTypedText()
        {
            HookText();
            string s = Typed.ToString();
            Typed.Clear();
            return s;
        }

        public static bool EnterPressed => KeyPressed(Key.Enter) || KeyPressed(Key.NumpadEnter);
        public static bool BackspacePressed => KeyPressed(Key.Backspace);
        /// <summary>Ruecktaste gehalten (Wiederholung im Eingabefeld, UI-204).</summary>
        public static bool BackspaceHeld => KeyHeld(Key.Backspace);
        public static bool EscapePressed => KeyPressed(Key.Escape);
        public static bool SlashPressed => KeyPressed(Key.Slash);
        public static bool PastePressed => CtrlHeld && KeyPressed(Key.V);
        /// <summary>Strg+A: alles markieren (Eingabefeld, UI-204).</summary>
        public static bool SelectAllPressed => CtrlHeld && KeyPressed(Key.A);
#elif ENABLE_LEGACY_INPUT_MANAGER
        public const string Backend = "Legacy Input Manager";

        public static bool IsHeld(GameKey k)
        {
            switch (k)
            {
                case GameKey.Forward: return UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.UpArrow);
                case GameKey.Backward: return UnityEngine.Input.GetKey(KeyCode.S) || UnityEngine.Input.GetKey(KeyCode.DownArrow);
                case GameKey.TurnLeft: return UnityEngine.Input.GetKey(KeyCode.A) || UnityEngine.Input.GetKey(KeyCode.LeftArrow);
                case GameKey.TurnRight: return UnityEngine.Input.GetKey(KeyCode.D) || UnityEngine.Input.GetKey(KeyCode.RightArrow);
                case GameKey.StrafeLeft: return UnityEngine.Input.GetKey(KeyCode.Q);
                case GameKey.StrafeRight: return UnityEngine.Input.GetKey(KeyCode.E);
                case GameKey.Jump: return UnityEngine.Input.GetKey(KeyCode.Space);
                case GameKey.Sprint: return UnityEngine.Input.GetKey(KeyCode.LeftShift);
                case GameKey.SwimDown: return UnityEngine.Input.GetKey(KeyCode.X);
                default: return false;
            }
        }

        public static bool WasPressed(GameKey k)
        {
            switch (k)
            {
                case GameKey.Jump: return UnityEngine.Input.GetKeyDown(KeyCode.Space);
                case GameKey.WalkToggle: return UnityEngine.Input.GetKeyDown(KeyCode.KeypadDivide);
                case GameKey.JogToggle: return UnityEngine.Input.GetKeyDown(KeyCode.KeypadMultiply);
                case GameKey.EpicWalkToggle: return UnityEngine.Input.GetKeyDown(KeyCode.KeypadMinus);
                case GameKey.Autorun: return UnityEngine.Input.GetKeyDown(KeyCode.Numlock) || UnityEngine.Input.GetKeyDown(KeyCode.Mouse4);
                case GameKey.ToggleDebugHud: return UnityEngine.Input.GetKeyDown(KeyCode.F1);
                case GameKey.Forward: return UnityEngine.Input.GetKeyDown(KeyCode.W) || UnityEngine.Input.GetKeyDown(KeyCode.UpArrow);
                case GameKey.Backward: return UnityEngine.Input.GetKeyDown(KeyCode.S) || UnityEngine.Input.GetKeyDown(KeyCode.DownArrow);
                case GameKey.SwapWeapon: return UnityEngine.Input.GetKeyDown(KeyCode.F);
                case GameKey.Reload: return UnityEngine.Input.GetKeyDown(KeyCode.R);
                case GameKey.TargetNext: return UnityEngine.Input.GetKeyDown(KeyCode.Tab);
                case GameKey.ClearTarget: return UnityEngine.Input.GetKeyDown(KeyCode.Escape);
                case GameKey.SwitchBody: return UnityEngine.Input.GetKeyDown(KeyCode.F2);
                case GameKey.ToggleNetwork: return UnityEngine.Input.GetKeyDown(KeyCode.F9);
                case GameKey.SelfTest: return UnityEngine.Input.GetKeyDown(KeyCode.F10);
                case GameKey.Inventory: return UnityEngine.Input.GetKeyDown(KeyCode.I) || UnityEngine.Input.GetKeyDown(KeyCode.B);
                case GameKey.Character: return UnityEngine.Input.GetKeyDown(KeyCode.C);
                case GameKey.QuestLog: return UnityEngine.Input.GetKeyDown(KeyCode.L);
                case GameKey.Settings: return UnityEngine.Input.GetKeyDown(KeyCode.O);
                case GameKey.Spellbook: return UnityEngine.Input.GetKeyDown(KeyCode.K);
                case GameKey.Emotes: return UnityEngine.Input.GetKeyDown(KeyCode.J);
                case GameKey.Talents: return UnityEngine.Input.GetKeyDown(KeyCode.N);
                default: return false;
            }
        }

        // Frei belegbare Tasten brauchen das Input System – im alten Input Manager nur die Ziffern der Grundbelegung
        private static KeyCode LegacyKey(string code)
        {
            switch (code)
            {
                case "Key:Digit1": return KeyCode.Alpha1; case "Key:Digit2": return KeyCode.Alpha2; case "Key:Digit3": return KeyCode.Alpha3;
                case "Key:Digit4": return KeyCode.Alpha4; case "Key:Digit5": return KeyCode.Alpha5; case "Key:Digit6": return KeyCode.Alpha6;
                case "Key:Digit7": return KeyCode.Alpha7; case "Key:Digit8": return KeyCode.Alpha8; case "Key:Digit9": return KeyCode.Alpha9;
                case "Key:Digit0": return KeyCode.Alpha0; case "Key:LeftCtrl": return KeyCode.LeftControl;
                default: return KeyCode.None;
            }
        }
        public static bool IsHeld(string code) { KeyCode k = LegacyKey(code); return k != KeyCode.None && UnityEngine.Input.GetKey(k); }
        public static bool WasPressed(string code) { KeyCode k = LegacyKey(code); return k != KeyCode.None && UnityEngine.Input.GetKeyDown(k); }
        public static string Label(string code) => string.IsNullOrEmpty(code) ? "" : code.Replace("Key:Digit", "").Replace("Key:", "");
        public static bool TryCaptureAnyPress(out string code) { code = null; return false; }

        /// <summary>Der alte Input Manager kann den Zeiger nicht versetzen – keine Aktion.</summary>
        public static void WarpCursor(Vector2 position) { }

        public static bool LeftMousePressed => UnityEngine.Input.GetMouseButtonDown(0);
        public static bool RightMousePressed => UnityEngine.Input.GetMouseButtonDown(1);
        public static bool RightMouseReleased => UnityEngine.Input.GetMouseButtonUp(1);
        public static bool ShiftHeld => UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
        public static bool CtrlHeld => UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
        public static bool AltHeld => UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);
        public static bool LeftMouseReleased => UnityEngine.Input.GetMouseButtonUp(0);
        public static Vector2 MousePosition => UnityEngine.Input.mousePosition;
        public static bool LeftMouseHeld => UnityEngine.Input.GetMouseButton(0);
        public static bool RightMouseHeld => UnityEngine.Input.GetMouseButton(1);

        public static Vector2 MouseDelta => new Vector2(
            UnityEngine.Input.GetAxisRaw("Mouse X"),
            UnityEngine.Input.GetAxisRaw("Mouse Y")) * LegacyMousePixelScale;

        public static float ScrollSteps
        {
            get
            {
                float y = UnityEngine.Input.mouseScrollDelta.y;
                return Mathf.Abs(y) < 0.01f ? 0f : Mathf.Sign(y);
            }
        }

        // ---- Texteingabe (Chat)
        /// <summary>Tests (EINSTIEG-01): Der alte Input Manager kann keinen Text einspeisen.</summary>
        public static string TestTextEingeben(string text) => "";

        public static string ConsumeTypedText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in UnityEngine.Input.inputString) if (!char.IsControl(c)) sb.Append(c);
            return sb.ToString();
        }

        public static bool EnterPressed => UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter);
        public static bool BackspacePressed => UnityEngine.Input.GetKeyDown(KeyCode.Backspace);
        public static bool BackspaceHeld => UnityEngine.Input.GetKey(KeyCode.Backspace);
        public static bool EscapePressed => UnityEngine.Input.GetKeyDown(KeyCode.Escape);
        public static bool SlashPressed => UnityEngine.Input.GetKeyDown(KeyCode.Slash);
        public static bool PastePressed => CtrlHeld && UnityEngine.Input.GetKeyDown(KeyCode.V);
        public static bool SelectAllPressed => CtrlHeld && UnityEngine.Input.GetKeyDown(KeyCode.A);
#else
        public const string Backend = "Kein Input-Backend aktiv";

        public static bool IsHeld(GameKey k) => false;
        public static bool WasPressed(GameKey k) => false;
        public static bool IsHeld(string code) => false;
        public static bool WasPressed(string code) => false;
        public static string Label(string code) => code ?? "";
        public static bool TryCaptureAnyPress(out string code) { code = null; return false; }
        public static bool AltHeld => false;
        public static void WarpCursor(Vector2 position) { }
        public static bool LeftMousePressed => false;
        public static bool RightMousePressed => false;
        public static bool RightMouseReleased => false;
        public static bool ShiftHeld => false;
        public static bool CtrlHeld => false;
        public static bool LeftMouseReleased => false;
        public static Vector2 MousePosition => Vector2.zero;
        public static bool LeftMouseHeld => false;
        public static bool RightMouseHeld => false;
        public static Vector2 MouseDelta => Vector2.zero;
        public static float ScrollSteps => 0f;

        public static string ConsumeTypedText() => "";
        public static string TestTextEingeben(string text) => "";
        public static bool EnterPressed => false;
        public static bool BackspacePressed => false;
        public static bool BackspaceHeld => false;
        public static bool EscapePressed => false;
        public static bool SlashPressed => false;
        public static bool PastePressed => false;
        public static bool SelectAllPressed => false;
#endif
    }
}
