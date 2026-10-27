using Ashveil.Data;
using Ashveil.Gameplay.Companions;
using Ashveil.Gameplay.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// Gefaehrten-Rahmen unter dem Zielrahmen (Klassen-Identitaet §64: einfache Steuerung): Name, Stufe, Leben und die vier
    /// Befehle Folgen · Bleiben · Angriff · Passiv (der aktuelle leuchtet). „Angriff“ schickt ihn auf dein Ziel.
    /// Nur sichtbar, solange der eigene Gefaehrte gerufen ist.
    /// </summary>
    [DefaultExecutionOrder(-470)] // wie die anderen Fenster: vor dem Spieler, damit UiPointer.OverUi rechtzeitig steht
    public sealed class CompanionUi : MonoBehaviour
    {
        private sealed class Btn
        {
            public RectTransform Rt;
            public Image Back;
            public TextMeshProUGUI Label;
            public CompanionCommand Command;
        }

        private static readonly Color ButtonColor = new Color(0.22f, 0.17f, 0.11f, 0.95f);
        private static readonly Color ButtonHover = new Color(0.36f, 0.27f, 0.16f, 0.98f);
        private static readonly Color ButtonActive = new Color(0.55f, 0.4f, 0.16f, 0.98f);

        private RectTransform _win;
        private TextMeshProUGUI _title;
        private UiKit.Bar _hp;
        private readonly Btn[] _buttons = new Btn[4];

        public static CompanionUi Ensure(Transform host)
        {
            var existing = host.GetComponentInChildren<CompanionUi>(true);
            if (existing != null) return existing;
            var go = new GameObject("Gefaehrten-Rahmen");
            go.transform.SetParent(host, false);
            var ui = go.AddComponent<CompanionUi>();
            ui.Build();
            return ui;
        }

        private void Build()
        {
            Canvas canvas = UiKit.CreateCanvas("Gefaehrte Canvas", transform, 12);
            var root = (RectTransform)canvas.transform;
            RectTransform inner = UiKit.Panel("Gefaehrte", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(340f, -98f), new Vector2(300f, 70f));
            _win = (RectTransform)inner.parent;
            _title = UiKit.CreateText("Name", inner, "", 15f, new Color(0.55f, 0.9f, 0.5f));
            Place(_title.rectTransform, new Vector2(8f, -3f), new Vector2(284f, 20f));
            _hp = UiKit.CreateBar("Leben", inner, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -24f), new Vector2(282f, 14f), UiKit.HealthColor, 11f);
            string[] labels = { "Folgen", "Bleiben", "Angriff", "Passiv" };
            CompanionCommand[] cmds = { CompanionCommand.Follow, CompanionCommand.Stay, CompanionCommand.Attack, CompanionCommand.Passive };
            for (int i = 0; i < 4; i++)
            {
                RectTransform rt = UiKit.CreateRect("Befehl " + labels[i], inner, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f + i * 71f, -42f), new Vector2(67f, 22f));
                UiKit.AddImage(rt, UiKit.BorderColor);
                Image back = UiKit.AddImage(UiKit.Stretch("Innen", rt, 1.5f), ButtonColor);
                TextMeshProUGUI l = UiKit.CreateText("Text", rt, labels[i], 12.5f, UiKit.TextColor, TextAlignmentOptions.Center);
                l.rectTransform.anchorMin = Vector2.zero;
                l.rectTransform.anchorMax = Vector2.one;
                l.rectTransform.offsetMin = l.rectTransform.offsetMax = Vector2.zero;
                _buttons[i] = new Btn { Rt = rt, Back = back, Label = l, Command = cmds[i] };
            }
            _win.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_win == null) return;
            bool show = CompanionClient.Out;
            if (_win.gameObject.activeSelf != show) _win.gameObject.SetActive(show);
            if (!show) return;

            CompanionSpecies sp = CompanionCatalog.Get(CompanionClient.Species);
            string name = !string.IsNullOrEmpty(CompanionClient.Name) ? CompanionClient.Name : sp != null ? sp.displayName : "Gefährte";
            _title.text = CompanionClient.Downed
                ? $"{name}  <color=#a0968a>Stufe {CompanionClient.Level} · besiegt</color>"
                : $"{name}  <color=#a0968a>Stufe {CompanionClient.Level}</color>";
            float f = CompanionClient.PetHealth != null ? CompanionClient.PetHealth.Fraction : 0f;
            _hp.Set(f);
            _hp.Label.text = CompanionClient.PetHealth != null ? $"{Mathf.CeilToInt(CompanionClient.PetHealth.Current)} / {Mathf.CeilToInt(CompanionClient.PetHealth.Max)}" : "";

            Vector2 mouse = RawInput.MousePosition;
            bool overWin = RectTransformUtility.RectangleContainsScreenPoint(_win, mouse, null);
            if (overWin) UiPointer.OverUi = true;
            foreach (Btn b in _buttons)
            {
                bool over = RectTransformUtility.RectangleContainsScreenPoint(b.Rt, mouse, null);
                bool active = CompanionClient.Command == b.Command;
                b.Back.color = active ? ButtonActive : over ? ButtonHover : ButtonColor;
                if (over && RawInput.LeftMousePressed && !CompanionClient.Downed && !UiPointer.ModalOpen) CompanionClient.SendCommand(b.Command);
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
