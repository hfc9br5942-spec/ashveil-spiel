using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// M8: Emote-Menue (J). Klick = Emote abspielen (Dauer-Emote nochmal = beenden), Ziehen auf die Actionbar = belegen.
    /// Dieselben Emotes gibt es als Chatbefehle (/winken, /fistbump, /jubeln, /lachen, /sitzen, /tanzen).
    /// </summary>
    [DefaultExecutionOrder(-464)]
    public sealed class EmoteUi : MonoBehaviour
    {
        private sealed class Row
        {
            public EmoteInfo Info;
            public RectTransform Rt;
            public Image Back;
        }

        public static EmoteUi Instance { get; private set; }
        public bool IsOpen => _open;
        public int Count => _rows.Count;

        private RectTransform _canvasRt, _win, _close;
        private Image _dragIcon;
        private readonly List<Row> _rows = new List<Row>();
        private bool _open, _built;
        private Row _press;
        private Vector2 _pressPos;
        private bool _dragging;

        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();

        /// <summary>Symbol je Emote (Platzhalter-Formen in eigener Farbe).</summary>
        public static Sprite IconFor(string key)
        {
            if (Icons.TryGetValue(key ?? "", out Sprite s) && s != null) return s;
            IconShape shape = IconShape.Trinket;
            Color c = new Color(0.9f, 0.75f, 0.4f);
            switch (key)
            {
                case "emote.wave": shape = IconShape.Gloves; c = new Color(0.95f, 0.85f, 0.55f); break;
                case "emote.fistbump": shape = IconShape.Gloves; c = new Color(0.85f, 0.5f, 0.3f); break;
                case "emote.cheer": shape = IconShape.Trinket; c = new Color(1f, 0.85f, 0.3f); break;
                case "emote.laugh": shape = IconShape.Scroll; c = new Color(0.95f, 0.7f, 0.55f); break;
                case "emote.sit": shape = IconShape.Cloth; c = new Color(0.6f, 0.75f, 0.55f); break;
                case "emote.dance": shape = IconShape.Gem; c = new Color(0.75f, 0.55f, 0.95f); break;
            }
            s = ItemIcons.ForShape(shape, c);
            Icons[key ?? ""] = s;
            return s;
        }

        public void Build(RectTransform canvasRoot)
        {
            Instance = this;
            _canvasRt = canvasRoot;
            const float w = 380f, rowH = 54f;
            float h = 56f + EmoteCatalog.All.Length * rowH + 34f;
            RectTransform inner = UiKit.Panel("Emotes", canvasRoot, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, 40f), new Vector2(w, h));
            _win = (RectTransform)inner.parent;
            TextMeshProUGUI title = UiKit.CreateText("Titel", _win, "Emotes (J)", 21f, UiKit.TextColor);
            Place(title.rectTransform, new Vector2(14f, -10f), new Vector2(w - 60f, 28f));
            _close = UiKit.CreateRect("Schliessen", _win, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(26f, 26f));
            UiKit.AddImage(_close, UiKit.BorderColor);
            TextMeshProUGUI x = UiKit.CreateText("X", _close, "×", 18f, UiKit.TextColor, TextAlignmentOptions.Center);
            x.rectTransform.anchorMin = Vector2.zero; x.rectTransform.anchorMax = Vector2.one;
            x.rectTransform.offsetMin = x.rectTransform.offsetMax = Vector2.zero;

            float y = -48f;
            foreach (EmoteInfo e in EmoteCatalog.All)
            {
                RectTransform rt = UiKit.CreateRect(e.Name, _win, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, y), new Vector2(w - 24f, rowH - 6f));
                Image back = UiKit.AddImage(rt, new Color(1f, 1f, 1f, 0.04f));
                RectTransform ic = UiKit.CreateRect("Symbol", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(40f, 40f));
                UiKit.AddImage(ic, UiKit.BorderColor);
                Image icon = UiKit.AddImage(UiKit.Stretch("Bild", ic, 2f), Color.white);
                icon.sprite = IconFor(e.Key);
                icon.preserveAspect = true;
                TextMeshProUGUI n = UiKit.CreateText("Name", rt, $"{e.Name}  <color=#8a8478><size=80%>/{e.Command}</size></color>", 17f, UiKit.TextColor);
                n.richText = true;
                Place(n.rectTransform, new Vector2(52f, -2f), new Vector2(w - 90f, 22f));
                TextMeshProUGUI d = UiKit.CreateText("Text", rt, e.Description, 12.5f, UiKit.MutedText);
                d.textWrappingMode = TextWrappingModes.Normal;
                Place(d.rectTransform, new Vector2(52f, -22f), new Vector2(w - 90f, 26f));
                _rows.Add(new Row { Info = e, Rt = rt, Back = back });
                y -= rowH;
            }
            TextMeshProUGUI hint = UiKit.CreateText("Hinweis", _win, "Klicken: abspielen · auf die Leiste ziehen: belegen", 13f, UiKit.MutedText);
            Place(hint.rectTransform, new Vector2(14f, y - 2f), new Vector2(w - 28f, 20f));

            RectTransform drag = UiKit.CreateRect("Ziehen", canvasRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48f, 48f));
            _dragIcon = UiKit.AddImage(drag, Color.white);
            _dragIcon.preserveAspect = true;
            _dragIcon.raycastTarget = false;
            drag.gameObject.SetActive(false);
            _win.gameObject.SetActive(false);
            UiFensterstapel.Anmelden(this, "Emotes", () => _open, () => _open = false);
            _built = true;
        }

        private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }

        public void SetOpen(bool open) => _open = open;

        private void OnDisable() { if (Instance == this) Instance = null; }

        private void Update()
        {
            if (!_built) return;
            if (UiPointer.HotkeysAllowed && RawInput.WasPressed(GameKey.Emotes)) _open = !_open;
            // Esc schließt über den Fensterstapel (UI-204: erst das oberste Fenster, dann das Ziel, dann das Spielmenü)
            if (_win.gameObject.activeSelf != _open) _win.gameObject.SetActive(_open);
            if (!_open) { _press = null; _dragging = false; _dragIcon.gameObject.SetActive(false); return; }

            Vector2 mouse = RawInput.MousePosition;
            if (Over(_win, mouse) || _dragging) UiPointer.OverUi = true;
            EmoteId playing = EmoteClient.Current != null ? EmoteClient.Current.Current : EmoteId.None;
            foreach (Row r in _rows)
                r.Back.color = r.Info.Id == playing ? new Color(0.9f, 0.6f, 0.2f, 0.25f) : Over(r.Rt, mouse) ? new Color(1f, 1f, 1f, 0.1f) : new Color(1f, 1f, 1f, 0.04f);
            if (RawInput.LeftMousePressed)
            {
                if (Over(_close, mouse)) { _open = false; return; }
                _press = null;
                foreach (Row r in _rows) if (Over(r.Rt, mouse)) { _press = r; _pressPos = mouse; break; }
            }
            if (_press != null && RawInput.LeftMouseHeld && !_dragging && (mouse - _pressPos).sqrMagnitude > 25f)
            {
                _dragging = true;
                _dragIcon.sprite = IconFor(_press.Info.Key);
                _dragIcon.gameObject.SetActive(true);
            }
            if (_dragging)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, mouse, null, out Vector2 local);
                ((RectTransform)_dragIcon.transform).anchoredPosition = local;
            }
            if (RawInput.LeftMouseReleased)
            {
                if (_dragging && _press != null && !Over(_win, mouse)) UiDrag.Drop(UiDrag.Kind.Emote, _press.Info.Key, mouse);
                else if (!_dragging && _press != null && Over(_press.Rt, mouse)) EmoteClient.Play(_press.Info.Id);
                _press = null;
                _dragging = false;
                _dragIcon.gameObject.SetActive(false);
            }
        }

        private static bool Over(RectTransform rt, Vector2 screen) =>
            rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);
    }
}
