using Ashveil.Data;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Story;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// Geschichts-Szenen am Bildschirm: Untertitel-Leiste unten (Sprechername in seiner Farbe, Beobachtungen kursiv ohne
    /// Namen) und der Handlungs-Hinweis „Rechtsklick: Fohlen befreien“, sobald eine Handlung in Reichweite ist (auch klickbar).
    /// Kein Vollbild, keine Zeitlupe – man behaelt die Kontrolle ueber die Figur (Vorgabe §Kamera/§Ton).
    /// </summary>
    [DefaultExecutionOrder(-470)]
    public sealed class StoryUi : MonoBehaviour
    {
        private RectTransform _bar, _prompt;
        private TextMeshProUGUI _speaker, _line, _promptText;
        private CanvasGroup _barGroup;
        private DialogueLine _shownLine;

        public static StoryUi Ensure(Transform host)
        {
            var existing = host.GetComponentInChildren<StoryUi>(true);
            if (existing != null) return existing;
            var go = new GameObject("Geschichte");
            go.transform.SetParent(host, false);
            var ui = go.AddComponent<StoryUi>();
            ui.Build();
            StoryDirector.Hook();
            return ui;
        }

        private void Build()
        {
            Canvas canvas = UiKit.CreateCanvas("Geschichte Canvas", transform, 14);
            var root = (RectTransform)canvas.transform;
            _bar = UiKit.CreateRect("Untertitel", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 232f), new Vector2(980f, 92f));
            UiKit.AddImage(_bar, new Color(0.03f, 0.03f, 0.035f, 0.72f));
            _barGroup = _bar.gameObject.AddComponent<CanvasGroup>();
            _speaker = UiKit.CreateText("Sprecher", _bar, "", 17f, new Color(0.91f, 0.79f, 0.54f), TextAlignmentOptions.TopLeft);
            Place(_speaker.rectTransform, new Vector2(18f, -8f), new Vector2(944f, 22f));
            _line = UiKit.CreateText("Zeile", _bar, "", 20f, UiKit.TextColor, TextAlignmentOptions.TopLeft);
            _line.textWrappingMode = TextWrappingModes.Normal;
            _line.richText = true;
            Place(_line.rectTransform, new Vector2(18f, -32f), new Vector2(944f, 58f));
            _bar.gameObject.SetActive(false);

            _prompt = UiKit.CreateRect("Handlung", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 336f), new Vector2(360f, 38f));
            UiKit.AddImage(_prompt, UiKit.BorderColor);
            UiKit.AddImage(UiKit.Stretch("Innen", _prompt, 1.5f), new Color(0.12f, 0.09f, 0.06f, 0.94f));
            _promptText = UiKit.CreateText("Text", _prompt, "", 17f, UiKit.TextColor, TextAlignmentOptions.Center);
            _promptText.richText = true;
            _promptText.rectTransform.anchorMin = Vector2.zero;
            _promptText.rectTransform.anchorMax = Vector2.one;
            _promptText.rectTransform.offsetMin = _promptText.rectTransform.offsetMax = Vector2.zero;
            _prompt.gameObject.SetActive(false);
        }

        private void Update()
        {
            StoryDirector.Tick(Time.deltaTime);
            DialogueLine l = StoryDirector.Line;
            bool show = l != null && !string.IsNullOrEmpty(l.TextKey);
            if (_bar.gameObject.activeSelf != show) _bar.gameObject.SetActive(show);
            Vector2 mouse = RawInput.MousePosition;
            if (show)
            {
                if (l != _shownLine)
                {
                    _shownLine = l;
                    SpeakerProfile sp = DialogueLibrary.Speaker(l.Speaker);
                    bool thought = sp == null || sp.Style == "thought" || string.IsNullOrEmpty(sp.NameKey);
                    _speaker.text = thought ? "" : sp.DisplayName;
                    if (sp != null) _speaker.color = sp.Color;
                    _line.text = thought ? "<i><color=#cfc6b4>" + l.Text + "</color></i>" : l.Text;
                    _barGroup.alpha = 0f;
                }
                _barGroup.alpha = Mathf.MoveTowards(_barGroup.alpha, 1f, Time.deltaTime * 5f);
                bool over = RectTransformUtility.RectangleContainsScreenPoint(_bar, mouse, null);
                if (over)
                {
                    UiPointer.OverUi = true;
                    if (RawInput.LeftMousePressed && !UiPointer.ModalOpen) StoryDirector.Skip();   // nicht unter dem Spielmenü (UI-204)
                }
            }
            else _shownLine = null;

            StoryInteractable offered = StoryInteractor.Offered;
            bool prompt = offered != null && !StoryDirector.Active;
            if (_prompt.gameObject.activeSelf != prompt) _prompt.gameObject.SetActive(prompt);
            if (!prompt) return;
            _promptText.text = "<color=#e8c98a>Rechtsklick</color>  " + offered.Label;
            if (RectTransformUtility.RectangleContainsScreenPoint(_prompt, mouse, null))
            {
                UiPointer.OverUi = true;
                if (RawInput.LeftMousePressed && !UiPointer.ModalOpen) StoryInteractor.Use(offered);
            }
        }

        private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }
    }
}
