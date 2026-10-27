using System.Collections.Generic;
using Ashveil.Core.Settings;
using Ashveil.Gameplay.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// Tastenbelegung der Actionbar (aus dem Einstellungsfenster): Modus Halten/Umschalten, Wechseltaste und alle
    /// zwoelf Platz-Tasten. Klick auf eine Taste, dann die neue Taste druecken (Esc bricht ab, Entf leert).
    /// Liegt die Taste schon woanders, wird sie dort frei. Gespeichert pro Rechner (<see cref="InputBindingsStore"/>).
    /// </summary>
    [DefaultExecutionOrder(-468)]
    public sealed class KeybindUi : MonoBehaviour
    {
        private sealed class Btn
        {
            public RectTransform Rt;
            public Image Back;
            public TextMeshProUGUI Label;
        }

        private static readonly Color ButtonColor = new Color(0.22f, 0.17f, 0.11f, 0.95f);
        private static readonly Color ButtonHover = new Color(0.36f, 0.27f, 0.16f, 0.98f);
        private static readonly Color ButtonActive = new Color(0.55f, 0.38f, 0.16f, 1f);

        /// <summary>Tasten, die das Spiel schon fuer etwas anderes nutzt (Warnung, keine Sperre).</summary>
        private static readonly Dictionary<string, string> Reserved = new Dictionary<string, string>
        {
            { "Key:W", "Vorwärts" }, { "Key:S", "Rückwärts" }, { "Key:A", "Drehen links" }, { "Key:D", "Drehen rechts" },
            { "Key:Q", "Seitschritt links" }, { "Key:E", "Seitschritt rechts" }, { "Key:Space", "Springen" }, { "Key:LeftShift", "Sprinten" },
            { "Key:Tab", "Ziel wechseln" }, { "Key:F", "Waffe wechseln" }, { "Key:R", "Nachladen" }, { "Key:I", "Rucksack" },
            { "Key:B", "Rucksack" }, { "Key:C", "Charakter" }, { "Key:L", "Auftragsbuch" }, { "Key:O", "Einstellungen" },
            { "Key:K", "Fähigkeiten" }, { "Key:J", "Emotes" }, { "Key:N", "Talente" }, { "Key:M", "Karte" }, { "Key:X", "Absinken (Schwimmen)" }, { "Key:Enter", "Chat" }, { "Key:F1", "Debug-Anzeige" },
            // EINSTIEG-03 (21.10., Nachbesserung): kein „Key:F2“ mehr – F2 wechselt im vernetzten Spiel nie den Körper (auch nicht im
            // Development-/Spieltest-Build), die Taste ist frei belegbar und bekommt keine Warnung
            // REL-02 (22.10.): kein „Key:F10“ mehr – F10 startet den Selbsttest nur noch im Editor (oder bei angefordertem Test),
            // im Spiel ist die Taste frei; die Warnung kommt nur noch im Editor (Apply)
            { "Key:F9", "Netzwerk" }, { "Key:F11", "Fotomodus" }, { "Key:NumLock", "Autorun" }, { "Mouse:Forward", "Autorun" }
        };

        public static KeybindUi Instance { get; private set; }
        public bool IsOpen => _open;

        private RectTransform _win;
        private Btn _close, _hold, _toggle, _swap, _weak, _defaults;
        private readonly Btn[] _gaits = new Btn[3];
        private static readonly string[] GaitNames = { "Gehen", "Joggen", "Episch" };
        private readonly Btn[] _slots = new Btn[12];
        private TextMeshProUGUI _message;
        private bool _open;
        /// <summary>-1 = nichts, 0..11 = Platz, 100 = Wechseltaste, 101 = Schwachpunkt, 102..104 = Gangarten</summary>
        private int _capturing = -1;
        private int _captureFrame;

        public void Build(RectTransform canvasRoot)
        {
            Instance = this;
            const float w = 560f, h = 520f;
            RectTransform inner = UiKit.Panel("Tastenbelegung", canvasRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(w, h));
            _win = (RectTransform)inner.parent;
            TextMeshProUGUI title = UiKit.CreateText("Titel", _win, "Tastenbelegung · Actionbar und Gangarten", 22f, UiKit.TextColor);
            Place(title.rectTransform, new Vector2(16f, -10f), new Vector2(w - 70f, 28f));
            _close = MakeButton(_win, "×", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(26f, 26f));

            TextMeshProUGUI modeLabel = UiKit.CreateText("Modus", _win, "Bank B", 17f, UiKit.MutedText);
            Place(modeLabel.rectTransform, new Vector2(16f, -52f), new Vector2(150f, 30f));
            _hold = MakeButton(_win, "Taste halten", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(170f, -50f), new Vector2(170f, 32f));
            _toggle = MakeButton(_win, "Einmal drücken", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(350f, -50f), new Vector2(170f, 32f));

            TextMeshProUGUI swapLabel = UiKit.CreateText("Wechseltaste", _win, "Wechseltaste", 17f, UiKit.MutedText);
            Place(swapLabel.rectTransform, new Vector2(16f, -94f), new Vector2(150f, 30f));
            _swap = MakeButton(_win, "", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(170f, -92f), new Vector2(170f, 32f));
            TextMeshProUGUI weakLabel = UiKit.CreateText("Schwachpunkt", _win, "Schwachpunkt", 17f, UiKit.MutedText);
            Place(weakLabel.rectTransform, new Vector2(352f, -94f), new Vector2(110f, 30f));
            _weak = MakeButton(_win, "", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(462f, -92f), new Vector2(84f, 32f));

            for (int i = 0; i < 12; i++)
            {
                int col = i / 6, row = i % 6;
                float x = 16f + col * 270f, y = -142f - row * 40f;
                TextMeshProUGUI l = UiKit.CreateText("Platz " + (i + 1), _win, "Platz " + (i + 1), 16f, UiKit.TextColor);
                Place(l.rectTransform, new Vector2(x, y - 4f), new Vector2(90f, 28f));
                _slots[i] = MakeButton(_win, "", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x + 94f, y), new Vector2(150f, 32f));
            }

            // Gangarten (Standard ist Rennen; diese Tasten schalten um)
            TextMeshProUGUI gaitLabel = UiKit.CreateText("Gangart", _win, "Gangart", 17f, UiKit.MutedText);
            Place(gaitLabel.rectTransform, new Vector2(16f, -384f), new Vector2(80f, 30f));
            for (int i = 0; i < 3; i++)
            {
                float x = 96f + i * 152f;
                TextMeshProUGUI l = UiKit.CreateText(GaitNames[i], _win, GaitNames[i], 14f, UiKit.TextColor);
                Place(l.rectTransform, new Vector2(x, -386f), new Vector2(60f, 28f));
                _gaits[i] = MakeButton(_win, "", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x + 60f, -382f), new Vector2(84f, 32f));
            }

            _message = UiKit.CreateText("Meldung", _win, "", 14f, UiKit.MutedText);
            _message.richText = true;
            _message.textWrappingMode = TextWrappingModes.Normal;
            Place(_message.rectTransform, new Vector2(16f, -428f), new Vector2(w - 32f, 40f));
            _defaults = MakeButton(_win, "Standard wiederherstellen", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 14f), new Vector2(240f, 30f));
            _win.gameObject.SetActive(false);
            UiFensterstapel.Anmelden(this, "Tastenbelegung", () => _open, () => SetOpen(false));
            Refresh();
        }

        private Btn MakeButton(RectTransform parent, string text, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            RectTransform rt = UiKit.CreateRect("Knopf " + text, parent, anchor, pivot, pos, size);
            UiKit.AddImage(rt, UiKit.BorderColor);
            Image back = UiKit.AddImage(UiKit.Stretch("Inner", rt, 1.5f), ButtonColor);
            TextMeshProUGUI label = UiKit.CreateText("Label", rt, text, 16f, UiKit.TextColor, TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            return new Btn { Rt = rt, Back = back, Label = label };
        }

        private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }

        public void SetOpen(bool open)
        {
            _open = open;
            if (!open) StopCapture();
            Refresh();
        }

        private void OnDisable()
        {
            StopCapture();
            if (Instance == this) Instance = null;
        }

        private void Refresh()
        {
            if (_win == null) return;
            InputBindings b = InputBindingsStore.Current;
            _swap.Label.text = _capturing == 100 ? "Taste drücken …" : Show(b.swapKey);
            _weak.Label.text = _capturing == 101 ? "…" : Show(b.weakPointKey);
            for (int i = 0; i < 12; i++) _slots[i].Label.text = _capturing == i ? "Taste drücken …" : Show(b.slots[i]);
            string[] gaitKeys = { b.walkKey, b.jogKey, b.epicWalkKey };
            for (int i = 0; i < 3; i++) if (_gaits[i] != null) _gaits[i].Label.text = _capturing == 102 + i ? "…" : Show(gaitKeys[i]);
        }

        private static string Show(string code) => string.IsNullOrEmpty(code) ? "<color=#8a8478>– frei –</color>" : RawInput.Label(code);

        private void StartCapture(int what)
        {
            _capturing = what;
            _captureFrame = Time.frameCount;
            UiPointer.KeyboardCaptured = true;
            _message.text = "Neue Taste drücken – <color=#e8c98a>Esc</color> bricht ab, <color=#e8c98a>Entf</color> leert den Platz.";
            Refresh();
        }

        private void StopCapture()
        {
            if (_capturing >= 0) UiPointer.KeyboardCaptured = false;
            _capturing = -1;
        }

        private void Update()
        {
            if (_win == null) return;
            if (_win.gameObject.activeSelf != _open) _win.gameObject.SetActive(_open);
            if (!_open) return;
            Vector2 mouse = RawInput.MousePosition;
            if (Over(_win, mouse)) UiPointer.OverUi = true;

            if (_capturing >= 0)
            {
                if (Time.frameCount == _captureFrame) return; // der Klick selbst zaehlt nicht
                if (RawInput.EscapePressed) { StopCapture(); _message.text = "Abgebrochen."; Refresh(); return; }
                if (RawInput.TryCaptureAnyPress(out string code))
                {
                    if (code == "Key:Delete") code = "";
                    Apply(_capturing, code);
                    StopCapture();
                    Refresh();
                }
                return;
            }

            InputBindings cur = InputBindingsStore.Current;
            Hover(_close, mouse, false);
            Hover(_hold, mouse, !cur.toggleMode);
            Hover(_toggle, mouse, cur.toggleMode);
            Hover(_swap, mouse, false);
            Hover(_weak, mouse, false);
            Hover(_defaults, mouse, false);
            foreach (Btn s in _slots) Hover(s, mouse, false);
            foreach (Btn g in _gaits) Hover(g, mouse, false);
            if (!RawInput.LeftMousePressed) return;

            if (Over(_close.Rt, mouse)) { SetOpen(false); return; }
            if (Over(_hold.Rt, mouse) || Over(_toggle.Rt, mouse))
            {
                InputBindings b = cur.Clone();
                b.toggleMode = Over(_toggle.Rt, mouse);
                InputBindingsStore.Set(b);
                _message.text = b.toggleMode ? "Bank B: Wechseltaste einmal drücken schaltet um." : "Bank B: gilt, solange die Wechseltaste gehalten wird.";
                return;
            }
            if (Over(_defaults.Rt, mouse))
            {
                InputBindingsStore.ResetToDefaults();
                _message.text = "Standardbelegung wiederhergestellt (1 … 0, ß, ´ · Strg halten).";
                Refresh();
                return;
            }
            if (Over(_swap.Rt, mouse)) { StartCapture(100); return; }
            if (Over(_weak.Rt, mouse)) { StartCapture(101); return; }
            for (int i = 0; i < 3; i++) if (_gaits[i] != null && Over(_gaits[i].Rt, mouse)) { StartCapture(102 + i); return; }
            for (int i = 0; i < 12; i++) if (Over(_slots[i].Rt, mouse)) { StartCapture(i); return; }
        }

        /// <summary>Belegung aendern (auch fuer Tests). Gibt die Meldung zurueck.</summary>
        public static string Apply(int what, string code)
        {
            InputBindings b = InputBindingsStore.Current.Clone();
            int before = what == 100 ? b.BindSwap(code) : what == 101 ? b.BindWeakPoint(code)
                : what >= 102 && what <= 104 ? b.BindGait(what, code) : b.BindSlot(what, code);
            InputBindingsStore.Set(b);
            string msg = string.IsNullOrEmpty(code) ? "Platz geleert." : $"<color=#e8c98a>{RawInput.Label(code)}</color> belegt.";
            if (before <= -1 && before >= -12 && -before - 1 != what) msg += $" Vorher lag sie auf Platz {-before} – der ist jetzt frei.";
            else if (before == -100 && what != 100) msg += " Vorher war sie die Wechseltaste – bitte eine neue wählen.";
            else if (before == -101 && what != 101) msg += " Vorher wählte sie den Schwachpunkt – bitte eine neue Taste dafür wählen.";
            else if (before <= -102 && before >= -104 && -before != what) msg += $" Vorher schaltete sie „{GaitNames[-before - 102]}“ – bitte eine neue Taste dafür wählen.";
            if (!string.IsNullOrEmpty(code) && Reserved.TryGetValue(code, out string other)) msg += $" Achtung: auch „{other}“ – beides passiert.";
            else if (code == "Key:F10" && Application.isEditor) msg += " Achtung: im Editor auch „Selbsttest“ – beides passiert.";   // REL-02 (22.10.)
            if (Instance != null && Instance._message != null) Instance._message.text = msg;
            return msg;
        }

        private static void Hover(Btn b, Vector2 mouse, bool active)
        {
            if (b == null) return;
            b.Back.color = active ? ButtonActive : Over(b.Rt, mouse) ? ButtonHover : ButtonColor;
        }

        private static bool Over(RectTransform rt, Vector2 screen) =>
            rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);
    }
}
