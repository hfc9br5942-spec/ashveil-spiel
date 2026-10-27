using System.Collections.Generic;
using Ashveil.Core.Settings;
using Ashveil.Gameplay.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// Einstellungsfenster (O oder Knopf "Einstellungen"): drei Grafikstufen per Klick und jede Einzeleinstellung mit
    /// &lt; &gt;. Aenderungen wirken sofort und werden pro Rechner gespeichert (<see cref="GraphicsSettingsStore"/>).
    /// Entsteht automatisch, sobald die Spieloberflaeche (Inventar-Fenster) in der Szene ist.
    /// </summary>
    [DefaultExecutionOrder(-470)] // nach Inventar/Quest/Sozial: ergaenzt UiPointer.OverUi
    public sealed class SettingsUi : MonoBehaviour
    {
        private sealed class Btn
        {
            public RectTransform Rt;
            public Image Back;
            public TextMeshProUGUI Label;
        }

        private sealed class RowView
        {
            public GraphicsCatalog.Row Row;
            public RectTransform Rt;
            public TextMeshProUGUI Value;
            public Btn Prev, Next;
        }

        private static readonly Color ButtonColor = new Color(0.22f, 0.17f, 0.11f, 0.95f);
        private static readonly Color ButtonHover = new Color(0.36f, 0.27f, 0.16f, 0.98f);
        private static readonly Color ButtonActive = new Color(0.55f, 0.38f, 0.16f, 1f);

        public static SettingsUi Instance { get; private set; }
        public bool IsOpen => _open;

        private RectTransform _canvasRt, _win;
        private TextMeshProUGUI _status, _hint;
        private Btn _openButton, _close, _keys;
        private KeybindUi _keybinds;
        private readonly Btn[] _tierButtons = new Btn[GraphicsCatalog.TierCount];
        private readonly List<RowView> _rows = new List<RowView>();
        private bool _open, _dirty = true;
        private float _fps = 60f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null || FindAnyObjectByType<InventoryUi>() == null) return;
            new GameObject("Settings UI").AddComponent<SettingsUi>();
        }

        private void Awake()
        {
            Instance = this;
            Build();
        }

        private void OnEnable() => GraphicsSettingsStore.Changed += OnChanged;

        private void OnDisable()
        {
            GraphicsSettingsStore.Changed -= OnChanged;
            if (Instance == this) Instance = null;
        }

        private void OnChanged(GraphicsOptions o) => _dirty = true;

        public void SetOpen(bool open)
        {
            _open = open;
            _dirty = true;
            if (!open && _keybinds != null) _keybinds.SetOpen(false);
        }

        // ================================================================== Aufbau

        private void Build()
        {
            Canvas canvas = UiKit.CreateCanvas("Settings Canvas", transform, 40);
            _canvasRt = (RectTransform)canvas.transform;

            _openButton = MakeButton(_canvasRt, "Einstellungen (O)", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 142f), new Vector2(288f, 30f));

            const float w = 640f, rowH = 34f;
            float h = 150f + GraphicsCatalog.Rows.Length * rowH + 60f;
            RectTransform inner = UiKit.Panel("Einstellungen", _canvasRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            _win = (RectTransform)inner.parent;
            TextMeshProUGUI title = UiKit.CreateText("Title", _win, "Einstellungen · Grafik und Kampf", 22f, UiKit.TextColor);
            Place(title.rectTransform, new Vector2(16f, -10f), new Vector2(w - 70f, 28f));
            _close = MakeButton(_win, "×", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(26f, 26f));
            _keys = MakeButton(_win, "Tastenbelegung …", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-42f, -8f), new Vector2(180f, 26f));

            TextMeshProUGUI stufe = UiKit.CreateText("TierLabel", _win, "Grafikstufe", 17f, UiKit.MutedText);
            Place(stufe.rectTransform, new Vector2(16f, -52f), new Vector2(140f, 30f));
            for (int i = 0; i < GraphicsCatalog.TierCount; i++)
                _tierButtons[i] = MakeButton(_win, GraphicsCatalog.TierNames[i], new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(160f + i * 116f, -50f), new Vector2(110f, 32f));

            _status = UiKit.CreateText("Status", _win, "", 14f, UiKit.MutedText);
            _status.richText = true;
            Place(_status.rectTransform, new Vector2(16f, -92f), new Vector2(w - 32f, 40f));
            _status.textWrappingMode = TextWrappingModes.Normal;

            float y = -140f;
            foreach (GraphicsCatalog.Row row in GraphicsCatalog.Rows)
            {
                RectTransform rt = UiKit.CreateRect("Row " + row.Label, _win, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, y), new Vector2(w - 32f, rowH - 4f));
                UiKit.AddImage(rt, new Color(1f, 1f, 1f, 0.03f));
                TextMeshProUGUI label = UiKit.CreateText("Label", rt, row.Label, 17f, UiKit.TextColor);
                Place(label.rectTransform, new Vector2(10f, -3f), new Vector2(300f, 26f));
                var view = new RowView
                {
                    Row = row,
                    Rt = rt,
                    Prev = MakeButton(rt, "<", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-236f, 0f), new Vector2(30f, 26f)),
                    Next = MakeButton(rt, ">", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(30f, 26f))
                };
                view.Value = UiKit.CreateText("Value", rt, "", 17f, UiKit.TextColor, TextAlignmentOptions.Center);
                var vr = view.Value.rectTransform;
                vr.anchorMin = vr.anchorMax = new Vector2(1f, 0.5f);
                vr.pivot = new Vector2(1f, 0.5f);
                vr.anchoredPosition = new Vector2(-38f, 0f);
                vr.sizeDelta = new Vector2(190f, 26f);
                _rows.Add(view);
                y -= rowH;
            }

            _hint = UiKit.CreateText("Hint", _win, "", 14f, UiKit.MutedText);
            _hint.textWrappingMode = TextWrappingModes.Normal;
            Place(_hint.rectTransform, new Vector2(16f, y - 8f), new Vector2(w - 32f, 44f));

            _win.gameObject.SetActive(false);
            UiFensterstapel.Anmelden(this, "Einstellungen", () => _open, () => SetOpen(false));

            // M6.6: Tastenbelegung der Actionbar als eigenes Fenster auf derselben Zeichenflaeche
            _keybinds = gameObject.AddComponent<KeybindUi>();
            _keybinds.Build(_canvasRt);
        }

        private Btn MakeButton(RectTransform parent, string text, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            RectTransform rt = UiKit.CreateRect("Button " + text, parent, anchor, pivot, pos, size);
            UiKit.AddImage(rt, UiKit.BorderColor);
            RectTransform inner = UiKit.Stretch("Inner", rt, 1.5f);
            Image back = UiKit.AddImage(inner, ButtonColor);
            TextMeshProUGUI label = UiKit.CreateText("Label", rt, text, 16f, UiKit.TextColor, TextAlignmentOptions.Center);
            var lr = label.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            return new Btn { Rt = rt, Back = back, Label = label };
        }

        private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }

        // ================================================================== Takt

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) _fps = Mathf.Lerp(_fps, 1f / dt, 0.05f);

            bool keysOpen = _keybinds != null && _keybinds.IsOpen;
            if (UiPointer.HotkeysAllowed && RawInput.WasPressed(GameKey.Settings)) SetOpen(!_open);
            // Esc schließt über den Fensterstapel (UI-204: erst das oberste Fenster, dann das Ziel, dann das Spielmenü)

            if (_win.gameObject.activeSelf != _open) _win.gameObject.SetActive(_open);
            Vector2 mouse = RawInput.MousePosition;
            bool over = Over(_openButton.Rt, mouse) || (_open && Over(_win, mouse));
            if (over) UiPointer.OverUi = true;

            Hover(_openButton, mouse, false);
            if (RawInput.LeftMousePressed && Over(_openButton.Rt, mouse))
            {
                SetOpen(!_open);
                return;
            }
            if (!_open) return;

            if (_dirty) Refresh();
            GraphicsOptions cur = GraphicsSettingsStore.Current;
            Hover(_close, mouse, false);
            Hover(_keys, mouse, keysOpen);
            for (int i = 0; i < GraphicsCatalog.TierCount; i++) Hover(_tierButtons[i], mouse, (int)cur.tier == i);
            string hint = "";
            foreach (RowView r in _rows)
            {
                Hover(r.Prev, mouse, false);
                Hover(r.Next, mouse, false);
                if (Over(r.Rt, mouse)) hint = r.Row.Hint ?? "";
            }
            _hint.text = hint;
            if (Time.frameCount % 15 == 0) UpdateStatus(cur);

            if (!RawInput.LeftMousePressed || keysOpen) return;
            if (Over(_close.Rt, mouse))
            {
                SetOpen(false);
                return;
            }
            if (Over(_keys.Rt, mouse))
            {
                _keybinds.SetOpen(true);
                return;
            }
            for (int i = 0; i < GraphicsCatalog.TierCount; i++)
                if (Over(_tierButtons[i].Rt, mouse))
                {
                    GraphicsSettingsStore.SetTier((GraphicsTier)i);
                    return;
                }
            foreach (RowView r in _rows)
            {
                int step = Over(r.Prev.Rt, mouse) ? -1 : Over(r.Next.Rt, mouse) ? 1 : 0;
                if (step == 0) continue;
                Step(r.Row, step);
                return;
            }
        }

        /// <summary>Einzelwert eine Stufe weiter (mit Umlauf) – auch fuer Tests.</summary>
        public static void Step(GraphicsCatalog.Row row, int step)
        {
            GraphicsOptions o = GraphicsSettingsStore.Current.Clone();
            int n = row.CurrentChoices.Length;
            row.Set(o, ((row.Get(o) + step) % n + n) % n);
            GraphicsSettingsStore.Set(o);
        }

        private void Refresh()
        {
            _dirty = false;
            GraphicsOptions o = GraphicsSettingsStore.Current;
            foreach (RowView r in _rows) { string[] c = r.Row.CurrentChoices; r.Value.text = c[Mathf.Clamp(r.Row.Get(o), 0, c.Length - 1)]; }
            UpdateStatus(o);
        }

        private void UpdateStatus(GraphicsOptions o)
        {
            string note = string.IsNullOrEmpty(GraphicsSettingsStore.DetectionNote) ? "" : $"   ·   Beim ersten Start: {GraphicsSettingsStore.DetectionNote}";
            _status.text = $"Aktuell: <color=#e8c98a>{GraphicsCatalog.TierNames[(int)o.tier]}</color>   ·   {_fps:0} Bilder/s   ·   " +
                           $"{SystemInfo.graphicsDeviceName}{note}";
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
