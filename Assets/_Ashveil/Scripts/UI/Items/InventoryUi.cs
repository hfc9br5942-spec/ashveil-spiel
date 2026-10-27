using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// Rucksack (I/B), Charakter mit Ausruestung (C), Beutefenster, Tooltip mit Vergleich, Aufteilen.
    ///   Ziehen            : verschieben / stapeln / anlegen / ablegen / tauschen
    ///   Rechtsklick       : anlegen, benutzen, ablegen; in der Beute: aufnehmen
    ///   Umschalt + Klick  : Stapel aufteilen
    /// Die Fenster schicken nur Bitten an den Server (<see cref="InventoryClient"/>) und zeigen dessen Antwort.
    /// Mausklicks ueber Fenstern gehoeren der Oberflaeche (<see cref="UiPointer"/>), nicht der Kamera.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class InventoryUi : MonoBehaviour
    {
        private sealed class SlotView
        {
            public RectTransform Rt;
            public Image Frame, Back, Icon;
            public TextMeshProUGUI Count, Label;
            public int Code;
        }

        private sealed class LootRow
        {
            public RectTransform Rt;
            public Image Frame, Icon;
            public TextMeshProUGUI Name, Sub;
        }

        private sealed class Button
        {
            public RectTransform Rt;
            public Image Back;
            public TextMeshProUGUI Label;
        }

        private const float SlotSize = 56f, Gap = 6f;

        /// <summary>Andere Fenster (z. B. Haendler) bitten, den Rucksack zu oeffnen.</summary>
        public static bool RequestBag;
        private static readonly Color EmptyFrame = new Color(0.32f, 0.28f, 0.22f, 0.9f);
        private static readonly Color SlotBack = new Color(0.02f, 0.02f, 0.03f, 0.72f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.17f, 0.11f, 0.95f);
        private static readonly Color ButtonHover = new Color(0.36f, 0.27f, 0.16f, 0.98f);

        private RectTransform _canvasRt;
        private RectTransform _bagWin, _charWin, _lootWin, _splitWin, _tip, _cmp;
        private TextMeshProUGUI _bagTitle, _charTitle, _stats, _tipText, _cmpText, _lootTitle, _splitText;
        private readonly List<SlotView> _bagSlots = new List<SlotView>();
        private readonly List<SlotView> _equipSlots = new List<SlotView>();
        // Taschenplaetze (wie WoW) und Munitionsplatz
        private readonly List<SlotView> _containerSlots = new List<SlotView>();
        private readonly TextMeshProUGUI[] _bagHeaders = new TextMeshProUGUI[InventoryModel.BagSlotCount + 1];
        private SlotView _ammoSlot;
        private TextMeshProUGUI _bagHint;
        private string _layoutKey = "";
        private readonly List<LootRow> _lootRows = new List<LootRow>();
        private Button _bagClose, _charClose, _lootClose, _takeAll, _splitOk, _splitCancel, _splitMinus, _splitPlus, _bagButton, _charButton;
        private Image _dragIcon;

        // Charaktervorschau, Aussehen, Faerben
        private RectTransform _previewRt;
        private RawImage _preview;
        private TextMeshProUGUI _previewHint;
        private RectTransform _lookWin;
        private Button _lookButton, _lookClose, _masteryButton;
        private sealed class OptionRow
        {
            public RectTransform Rt;
            public TextMeshProUGUI Label, Value;
            public Button Prev, Next;
            public Image Swatch;
        }
        private readonly List<OptionRow> _rows = new List<OptionRow>();
        private OptionRow _rowBody, _rowSkin, _rowHair, _rowHairColor, _rowBeard, _rowHelm, _rowCloak, _rowDyePrimary, _rowDyeSecondary, _rowDyeDetail;
        private TextMeshProUGUI _dyeTitle, _lookNote;
        private bool _lookOpen;
        private EquipmentSlot _dyeSlot = EquipmentSlot.Chest;
        private bool _previewDrag;
        private float _previewLastX;
        private int _tryOnCode = -1;
        private bool _previewDirty = true;
        private int _pendingTarget = -1;
        private int _flashCode = -1;
        private float _flashTimer;

        private bool _bagOpen, _charOpen;
        private int _pressCode = -1, _rightCode = -1, _rightLoot = -1;
        private Vector2 _pressPos;
        private bool _dragging;
        private int _splitFrom = -1, _splitAmount;
        private bool _dirty = true;
        private IInventoryService _svc;

        [System.NonSerialized] private bool _built;

        private void OnEnable()
        {
            InventoryClient.Changed += MarkDirty;
            InventoryClient.LootChanged += MarkDirty;
            InventoryClient.Changed += OnInventoryAnswer;
            AppearanceClient.Changed += MarkLook;
        }

        private void MarkLook()
        {
            _dirty = true;
            _previewDirty = true;
        }

        /// <summary>Antwort des Servers: bei abgelehntem Ausruesten das Ziel kurz rot aufleuchten lassen.</summary>
        private void OnInventoryAnswer()
        {
            _previewDirty = true;
            if (_svc != null && !string.IsNullOrEmpty(_svc.LastError) && _pendingTarget >= 0)
            {
                _flashCode = _pendingTarget;
                _flashTimer = 0.8f;
            }
            _pendingTarget = -1;
        }

        private void OnDisable()
        {
            InventoryClient.Changed -= MarkDirty;
            InventoryClient.LootChanged -= MarkDirty;
            InventoryClient.Changed -= OnInventoryAnswer;
            AppearanceClient.Changed -= MarkLook;
            CharacterPreviewStage.Existing?.Show(false);
            UiPointer.OverUi = false;
            UiPointer.Dragging = false;
        }

        private void MarkDirty() => _dirty = true;

        private void Start()
        {
            if (UiKit.Font == null)
            {
                enabled = false;
                return;
            }
            Build();
        }

        // ================================================================== Aufbau

        private void Build()
        {
            Canvas canvas = UiKit.CreateCanvas("Inventory Canvas", transform, 20);
            _canvasRt = (RectTransform)canvas.transform;

            // --- Taschen (unten rechts): Taschenplaetze oben, darunter Rucksack und jede angelegte Tasche (Layout in LayoutBags)
            float bw = 5 * SlotSize + 4 * Gap + 28f, bh = 4 * SlotSize + 3 * Gap + 76f;
            _bagWin = Window("Taschen", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 184f), new Vector2(bw, bh), out _bagTitle, out _bagClose);
            for (int b = 0; b < InventoryModel.BagSlotCount; b++)
                _containerSlots.Add(Slot(_bagWin, Vector2.zero, InventoryModel.ContainerCode(b), "Tasche"));
            for (int i = 0; i < _bagHeaders.Length; i++)
            {
                _bagHeaders[i] = UiKit.CreateText("BagHeader " + i, _bagWin, "", 13f, UiKit.MutedText);
                _bagHeaders[i].richText = true;
            }
            for (int i = 0; i < InventoryModel.BagSize; i++) _bagSlots.Add(Slot(_bagWin, Vector2.zero, i, ""));
            _bagHint = UiKit.CreateText("Hint", _bagWin, "Rechtsklick: benutzen/anlegen\nUmschalt+Klick: teilen", 12f, UiKit.MutedText);

            // --- Charakter (links)
            float cw = 580f, ch = 7 * (SlotSize + Gap) + SlotSize + 70f;
            _charWin = Window("Charakter", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 20f), new Vector2(cw, ch), out _charTitle, out _charClose);
            EquipmentSlot[] left = { EquipmentSlot.Head, EquipmentSlot.Neck, EquipmentSlot.Shoulders, EquipmentSlot.Cloak, EquipmentSlot.Chest, EquipmentSlot.Hands, EquipmentSlot.Belt };
            EquipmentSlot[] right = { EquipmentSlot.Legs, EquipmentSlot.Feet, EquipmentSlot.Ring1, EquipmentSlot.Ring2, EquipmentSlot.Trinket1, EquipmentSlot.Trinket2 };
            var byIndex = new SlotView[InventoryModel.EquipCount];
            for (int i = 0; i < left.Length; i++)
                byIndex[(int)left[i]] = Slot(_charWin, new Vector2(14f, -44f - i * (SlotSize + Gap)), InventoryModel.EquipCode(left[i]), ItemText.SlotName(left[i]));
            for (int i = 0; i < right.Length; i++)
                byIndex[(int)right[i]] = Slot(_charWin, new Vector2(cw - 14f - SlotSize, -44f - i * (SlotSize + Gap)), InventoryModel.EquipCode(right[i]), ItemText.SlotName(right[i]));
            float wy = -44f - 7 * (SlotSize + Gap);
            // Waffenreihe wie WoW: Haupthand, Nebenhand, Fernkampf – daneben der Munitionsplatz
            float wx = cw * 0.5f - 2f * (SlotSize + Gap);
            byIndex[(int)EquipmentSlot.MainHand] = Slot(_charWin, new Vector2(wx, wy), InventoryModel.EquipCode(EquipmentSlot.MainHand), "Haupthand");
            byIndex[(int)EquipmentSlot.OffHand] = Slot(_charWin, new Vector2(wx + (SlotSize + Gap), wy), InventoryModel.EquipCode(EquipmentSlot.OffHand), "Nebenhand");
            byIndex[(int)EquipmentSlot.Ranged] = Slot(_charWin, new Vector2(wx + 2f * (SlotSize + Gap), wy), InventoryModel.EquipCode(EquipmentSlot.Ranged), "Fernkampf");
            _ammoSlot = Slot(_charWin, new Vector2(wx + 3f * (SlotSize + Gap) + 10f, wy), InventoryModel.AmmoCode, "Munition");
            _equipSlots.AddRange(byIndex);
            float mid = cw - 2f * (14f + SlotSize + 14f);
            _previewRt = UiKit.CreateRect("Preview", _charWin, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f + SlotSize + 14f, -44f), new Vector2(mid, 300f));
            UiKit.AddImage(_previewRt, new Color(0f, 0f, 0f, 0.35f));
            RectTransform img = UiKit.Stretch("Image", _previewRt, 0f);
            _preview = img.gameObject.AddComponent<RawImage>();
            _preview.raycastTarget = false;
            _previewHint = UiKit.CreateText("Hint", _previewRt, "Ziehen: drehen · Alt + Zeigen: anprobieren", 11f, UiKit.MutedText, TextAlignmentOptions.Bottom);
            var hr = _previewHint.rectTransform;
            hr.anchorMin = Vector2.zero;
            hr.anchorMax = new Vector2(1f, 0f);
            hr.pivot = new Vector2(0.5f, 0f);
            hr.anchoredPosition = new Vector2(0f, 4f);
            hr.sizeDelta = new Vector2(0f, 16f);
            _stats = UiKit.CreateText("Stats", _charWin, "", 14f, UiKit.TextColor);
            _stats.textWrappingMode = TextWrappingModes.Normal;
            _stats.lineSpacing = 2f;
            Place(_stats.rectTransform, new Vector2(14f + SlotSize + 18f, -350f), new Vector2(mid - 8f, 7 * (SlotSize + Gap) - 310f));
            _lookButton = MakeButton((RectTransform)_charWin, "Aussehen", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -8f), new Vector2(96f, 26f));
            // Praxis-Meisterschaften: Fenster „Erfahrung“
            _masteryButton = MakeButton((RectTransform)_charWin, "Erfahrung", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-142f, -8f), new Vector2(96f, 26f));
            BuildLookWindow(cw);

            // --- Beute (Mitte links)
            _lootWin = Window("Beute", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-360f, 60f), new Vector2(320f, 200f), out _lootTitle, out _lootClose);
            // 21.10.: unten mittig verankert (vorher unten links mit Mittel-Drehpunkt – der Knopf ragte zur Hälfte links aus dem Fenster,
            // neben dem Gespräch/Händler lag diese Hälfte darunter)
            _takeAll = MakeButton(_lootWin, "Alles nehmen", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(160f, 32f));

            // --- Aufteilen
            _splitWin = UiKit.Panel("Split", _canvasRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(260f, 112f));
            _splitWin = (RectTransform)_splitWin.parent;
            _splitText = UiKit.CreateText("Amount", _splitWin, "", 20f, UiKit.TextColor, TextAlignmentOptions.Center);
            Place(_splitText.rectTransform, new Vector2(60f, -14f), new Vector2(140f, 30f));
            _splitMinus = MakeButton(_splitWin, "-", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -12f), new Vector2(40f, 34f));
            _splitPlus = MakeButton(_splitWin, "+", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -12f), new Vector2(40f, 34f));
            _splitOk = MakeButton(_splitWin, "Teilen", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 12f), new Vector2(110f, 34f));
            _splitCancel = MakeButton(_splitWin, "Abbrechen", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 12f), new Vector2(110f, 34f));

            // --- Schnellzugriff unten rechts
            _charButton = MakeButton(_canvasRt, "Charakter (C)", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-172f, 104f), new Vector2(140f, 32f));
            _bagButton = MakeButton(_canvasRt, "Rucksack (B)", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 104f), new Vector2(140f, 32f));

            // --- Tooltip + Vergleich
            _tip = Tooltip("Tooltip", out _tipText);
            _cmp = Tooltip("Compare", out _cmpText);

            // --- Gezogenes Symbol
            RectTransform drag = UiKit.CreateRect("Drag", _canvasRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SlotSize, SlotSize));
            _dragIcon = UiKit.AddImage(drag, Color.white);
            _dragIcon.preserveAspect = true;
            drag.gameObject.SetActive(false);

            // UI-204: Esc schließt die Fenster einzeln über den Fensterstapel (öffnen sich zwei im selben Bild, liegt das später angemeldete oben)
            UiFensterstapel.Anmelden(this, "Rucksack", () => _svc != null && _svc.Ready && _bagOpen, () =>
            {
                if (_svc != null && _svc.LootOpen) _svc.CloseLoot();   // offene Beute hält den Rucksack offen
                _bagOpen = false;
            });
            UiFensterstapel.Anmelden(this, "Charakter", () => _svc != null && _svc.Ready && _charOpen, () => _charOpen = false);
            UiFensterstapel.Anmelden(this, "Aussehen", () => _svc != null && _svc.Ready && _charOpen && _lookOpen, () => _lookOpen = false);
            UiFensterstapel.Anmelden(this, "Beute", () => _svc != null && _svc.Ready && _svc.LootOpen, () => { if (_svc != null) _svc.CloseLoot(); });
            UiFensterstapel.Anmelden(this, "Aufteilen", () => _svc != null && _svc.Ready && _splitFrom >= 0, () => _splitFrom = -1);
            _built = true;
            _dirty = true;
        }

        private RectTransform Window(string title, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, out TextMeshProUGUI titleText, out Button close)
        {
            RectTransform inner = UiKit.Panel(title, _canvasRt, anchor, pivot, pos, size);
            var border = (RectTransform)inner.parent;
            titleText = UiKit.CreateText("Title", border, title, 20f, UiKit.TextColor);
            Place(titleText.rectTransform, new Vector2(14f, -8f), new Vector2(size.x - 60f, 26f));
            close = MakeButton(border, "×", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(26f, 26f));
            border.gameObject.SetActive(false);
            return border;
        }

        private SlotView Slot(RectTransform parent, Vector2 topLeft, int code, string label)
        {
            RectTransform rt = UiKit.CreateRect("Slot " + code, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), topLeft, new Vector2(SlotSize, SlotSize));
            Image frame = UiKit.AddImage(rt, EmptyFrame);
            RectTransform backRt = UiKit.Stretch("Back", rt, 2f);
            Image back = UiKit.AddImage(backRt, SlotBack);
            RectTransform iconRt = UiKit.Stretch("Icon", rt, 4f);
            Image icon = UiKit.AddImage(iconRt, Color.white);
            icon.preserveAspect = true;
            icon.enabled = false;
            TextMeshProUGUI lbl = UiKit.CreateText("Label", rt, label, 11f, new Color(0.6f, 0.56f, 0.48f, 0.8f), TextAlignmentOptions.Center);
            lbl.textWrappingMode = TextWrappingModes.Normal;
            var lr = lbl.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(3f, 3f);
            lr.offsetMax = new Vector2(-3f, -3f);
            TextMeshProUGUI count = UiKit.CreateText("Count", rt, "", 15f, UiKit.TextColor, TextAlignmentOptions.BottomRight);
            var cr = count.rectTransform;
            cr.anchorMin = Vector2.zero;
            cr.anchorMax = Vector2.one;
            cr.offsetMin = new Vector2(3f, 2f);
            cr.offsetMax = new Vector2(-4f, -2f);
            return new SlotView { Rt = rt, Frame = frame, Back = back, Icon = icon, Count = count, Label = lbl, Code = code };
        }

        private Button MakeButton(RectTransform parent, string text, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
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
            return new Button { Rt = rt, Back = back, Label = label };
        }

        private RectTransform Tooltip(string name, out TextMeshProUGUI text)
        {
            RectTransform inner = UiKit.Panel(name, _canvasRt, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), Vector2.zero, new Vector2(340f, 100f));
            var border = (RectTransform)inner.parent;
            inner.GetComponent<Image>().color = new Color(0.04f, 0.04f, 0.06f, 0.95f);
            text = UiKit.CreateText("Text", border, "", 16f, UiKit.TextColor);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = true;
            var tr = text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(12f, 10f);
            tr.offsetMax = new Vector2(-12f, -10f);
            border.gameObject.SetActive(false);
            return border;
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
            if (!_built) return;
            if (!ReferenceEquals(_svc, InventoryClient.Current))
            {
                _svc = InventoryClient.Current;
                _dirty = true;
            }
            bool ready = _svc != null && _svc.Ready;

            if (UiPointer.HotkeysAllowed)
            {
                if (RawInput.WasPressed(GameKey.Inventory)) _bagOpen = !_bagOpen;
                if (RawInput.WasPressed(GameKey.Character)) _charOpen = !_charOpen;
            }
            // Esc schließt über den Fensterstapel (UI-204: erst das oberste Fenster, dann das Ziel, dann das Spielmenü)
            if (RequestBag && ready) _bagOpen = true;
            RequestBag = false;
            bool lootOpen = ready && _svc.LootOpen;
            if (lootOpen && !_bagOpen) _bagOpen = true; // Beute oeffnet den Rucksack mit
            SetActive(_bagWin, ready && _bagOpen);
            SetActive(_charWin, ready && _charOpen);
            SetActive(_lookWin, ready && _charOpen && _lookOpen);
            SetActive(_lootWin, lootOpen);
            if (lootOpen) LootPlatz();
            SetActive(_splitWin, ready && _splitFrom >= 0);
            SetActive(_bagButton.Rt, ready);
            SetActive(_charButton.Rt, ready);

            if (_dirty && ready) Refresh();
            _dirty = false;

            Vector2 mouse = RawInput.MousePosition;
            bool over = Over(_bagWin, mouse) || Over(_charWin, mouse) || Over(_lootWin, mouse) || Over(_splitWin, mouse) || Over(_lookWin, mouse) ||
                        Over(_bagButton.Rt, mouse) || Over(_charButton.Rt, mouse);
            UiPointer.OverUi = over || _dragging;
            UiPointer.Dragging = _dragging;

            if (!ready || UiPointer.ModalOpen)   // Spielmenü offen (UI-204): keine Klicks unter dem Menü
            {
                HideTooltip();
                return;
            }
            HandleMouse(mouse);
            UpdateHover(mouse);
            UpdatePreview(mouse);
            UpdateFlash(Time.unscaledDeltaTime);
        }

        private static void SetActive(RectTransform rt, bool on)
        {
            if (rt != null && rt.gameObject.activeSelf != on) rt.gameObject.SetActive(on);
        }

        /// <summary>
        /// 21.10.: Beute rechts neben den Gesprächsfenstern statt darunter. Gespräch (40–500), Händler (520–940) und Lehrer (960–1400)
        /// sind links verankert und liegen auf Ebene 22 über der Beute (20) – das Händlerfenster verdeckte sie samt „Alles nehmen“, und ein
        /// Klick auf eine Warenzeile nahm zugleich die Beutezeile darunter (beide Oberflächen werten denselben Klick aus; Fund aus Bild 15).
        /// Ohne Gespräch bleibt die Beute an ihrem Platz (Mitte links, wie in Build).
        /// </summary>
        private void LootPlatz()
        {
            float x = -360f;   // Mitte des Fensters (320 breit) relativ zur Bildmitte, wie in Build
            var p = Ashveil.Gameplay.Quests.ProgressionClient.Current;
            if (p != null && p.Ready && p.DialogNpc != null)
            {
                // linke Kante 20 hinter dem Gesprächsblock: ohne Handel → 520 (Lehrer erst ab 960), Händler → 960, Händler und Lehrer → 1420
                bool lehrer = TrainerUi.Instance != null && TrainerUi.Instance.IsOpen;
                float frei = !p.DialogNpc.vendor ? 520f : lehrer ? 1420f : 960f;
                float halb = _canvasRt.rect.width * 0.5f;
                x = Mathf.Max(x, frei - halb + 160f);
                x = Mathf.Min(x, halb - 8f - 160f);   // schmale Ansicht: im Bild bleiben
            }
            var pos = new Vector2(x, 60f);
            if (_lootWin.anchoredPosition != pos) _lootWin.anchoredPosition = pos;
        }

        private static bool Over(RectTransform rt, Vector2 screen) =>
            rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);

        // ================================================================== Anzeige

        /// <summary>Taschenfenster anordnen: Taschenplaetze, Rucksack, dann jede angelegte Tasche mit Kopfzeile.</summary>
        private void LayoutBags(InventoryModel m)
        {
            var key = new System.Text.StringBuilder();
            for (int b = 0; b < InventoryModel.BagSlotCount; b++) key.Append(m.Capacity(b)).Append(',');
            if (key.ToString() == _layoutKey) return;
            _layoutKey = key.ToString();

            int total = 0;
            for (int b = -1; b < InventoryModel.BagSlotCount; b++) total += m.Capacity(b);
            int cols = total > 40 ? 8 : 5;
            float step = SlotSize + Gap;
            float bw = cols * SlotSize + (cols - 1) * Gap + 28f;
            float y = -40f;
            for (int b = 0; b < _containerSlots.Count; b++)
                _containerSlots[b].Rt.anchoredPosition = new Vector2(14f + b * step, y);
            y -= step + 6f;
            foreach (SlotView v in _bagSlots) v.Rt.gameObject.SetActive(false);
            for (int b = -1; b < InventoryModel.BagSlotCount; b++)
            {
                TextMeshProUGUI header = _bagHeaders[b + 1];
                int cap = m.Capacity(b);
                header.gameObject.SetActive(cap > 0);
                if (cap <= 0) continue;
                ItemDefinition bd = b < 0 ? null : m.Def(m.Containers[b]);
                header.text = b < 0 ? "Rucksack" : $"{bd.DisplayName} <size=85%>({cap} Plätze{(bd.containerKind == ContainerKind.Quiver ? ", nur Pfeile" : bd.containerKind == ContainerKind.AmmoPouch ? ", nur Bolzen" : "")})</size>";
                Place(header.rectTransform, new Vector2(14f, y), new Vector2(bw - 28f, 18f));
                y -= 20f;
                for (int sIdx = 0; sIdx < cap; sIdx++)
                {
                    SlotView v = _bagSlots[InventoryModel.CellCode(b, sIdx)];
                    v.Rt.gameObject.SetActive(true);
                    v.Rt.anchoredPosition = new Vector2(14f + (sIdx % cols) * step, y - (sIdx / cols) * step);
                }
                y -= ((cap + cols - 1) / cols) * step + 4f;
            }
            Place(_bagHint.rectTransform, new Vector2(14f, y - 2f), new Vector2(bw - 28f, 34f));
            _bagWin.sizeDelta = new Vector2(bw, -y + 46f);
            _bagClose.Rt.anchoredPosition = new Vector2(-8f, -8f);
        }

        private void Refresh()
        {
            ItemStack[] bag = _svc.Bag, eq = _svc.Equipment;
            InventoryModel mirror = InventoryClient.Mirror(_svc);
            LayoutBags(mirror);
            for (int i = 0; i < _bagSlots.Count; i++) Show(_bagSlots[i], bag[i]);
            for (int i = 0; i < _equipSlots.Count; i++) Show(_equipSlots[i], eq[i]);
            for (int b = 0; b < _containerSlots.Count; b++) Show(_containerSlots[b], _svc.Containers[b]);
            Show(_ammoSlot, StackAt(InventoryModel.AmmoCode));
            _bagTitle.text = $"Taschen  <size=75%><color=#a8a296>{mirror.FreeBagSlots}/{mirror.GeneralCapacity} frei</color></size>";

            string cls = _svc.Class != null ? _svc.Class.DisplayName : "Wanderer";
            string clsHex = _svc.Class != null ? "#" + ColorUtility.ToHtmlStringRGB(_svc.Class.classColor) : "#e0d8c8";
            _charTitle.text = $"<color={clsHex}>{cls}</color>  <size=75%><color=#a8a296>Stufe {_svc.Level}</color></size>";
            EquipmentStats st = EquipmentStats.From(eq, _svc.Level);
            _stats.text =
                $"<color=#a8a296>Leben</color> {st.MaxHealth:0}<pos=50%><color=#a8a296>Rüstung</color> {st.TotalArmor:0} <size=80%><color=#a8a296>(-{st.ArmorReduction(_svc.Level) * 100f:0} %)</color></size>\n" +
                $"<color=#a8a296>Stärke</color> {st.Strength}<pos=50%><color=#a8a296>Geschick</color> {st.Agility}\n" +
                $"<color=#a8a296>Intellekt</color> {st.Intellect}<pos=50%><color=#a8a296>Ausdauer</color> {st.Stamina}\n" +
                $"<color=#a8a296>Willenskraft</color> {st.Willpower}<pos=50%><color=#a8a296>Kritisch</color> +{st.CritChance * 100f:0.#} %\n" +
                $"<color=#a8a296>Nahkampf</color> +{(st.DamageMultiplier(true) - 1f) * 100f:0} %<pos=50%><color=#a8a296>Fernkampf</color> +{(st.DamageMultiplier(false) - 1f) * 100f:0} %" +
                $"\n<color=#a8a296>Ausweichen</color> {CharacterEquipment.DodgeChanceFor(st) * 100f:0.#} %" + BlockLine(st);
            RefreshLook();

            // Beute
            if (_svc.LootOpen)
            {
                IReadOnlyList<ItemStack> loot = _svc.Loot;
                while (_lootRows.Count < loot.Count) _lootRows.Add(MakeLootRow(_lootRows.Count));
                for (int i = 0; i < _lootRows.Count; i++)
                {
                    bool on = i < loot.Count;
                    _lootRows[i].Rt.gameObject.SetActive(on);
                    if (!on) continue;
                    ItemDefinition d = GameData.Item(loot[i].id);
                    LootRow row = _lootRows[i];
                    Color q = d != null ? ItemQualityColors.Get(d.quality) : Color.gray;
                    row.Frame.color = q * new Color(1f, 1f, 1f, 0.9f);
                    row.Icon.sprite = ItemIcons.Get(d);
                    row.Icon.enabled = row.Icon.sprite != null;
                    string count = loot[i].count > 1 ? $"  <color=#e8e0d0>×{loot[i].count}</color>" : "";
                    row.Name.text = $"<color=#{ColorUtility.ToHtmlStringRGB(q)}>{(d != null ? d.DisplayName : loot[i].id)}</color>{count}";
                    row.Sub.text = d != null ? $"{ItemQualityColors.Name(d.quality)} · {(d.IsEquippable ? ItemText.EquipTypeName(d) : ItemText.CategoryName(d))}" : "";
                }
                float h = 44f + loot.Count * 54f + 56f;
                _lootWin.sizeDelta = new Vector2(320f, h);
                GameObject src = _svc.LootSource;
                _lootTitle.text = src != null ? "Beute" : "Beute";
            }
        }

        private LootRow MakeLootRow(int i)
        {
            RectTransform rt = UiKit.CreateRect("Loot " + i, _lootWin, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -42f - i * 54f), new Vector2(296f, 50f));
            UiKit.AddImage(rt, new Color(0f, 0f, 0f, 0.35f));
            RectTransform frameRt = UiKit.CreateRect("Frame", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(46f, 46f));
            Image frame = UiKit.AddImage(frameRt, EmptyFrame);
            UiKit.AddImage(UiKit.Stretch("Back", frameRt, 2f), SlotBack);
            Image icon = UiKit.AddImage(UiKit.Stretch("Icon", frameRt, 3f), Color.white);
            icon.preserveAspect = true;
            TextMeshProUGUI name = UiKit.CreateText("Name", rt, "", 17f, UiKit.TextColor);
            Place(name.rectTransform, new Vector2(56f, -4f), new Vector2(236f, 24f));
            TextMeshProUGUI sub = UiKit.CreateText("Sub", rt, "", 13f, UiKit.MutedText);
            Place(sub.rectTransform, new Vector2(56f, -27f), new Vector2(236f, 20f));
            return new LootRow { Rt = rt, Frame = frame, Icon = icon, Name = name, Sub = sub };
        }

        private void Show(SlotView v, ItemStack s)
        {
            ItemDefinition d = s.IsEmpty ? null : GameData.Item(s.id);
            bool dragged = _dragging && v.Code == _pressCode;
            v.Icon.enabled = d != null;
            v.Label.enabled = d == null;
            if (d != null)
            {
                v.Icon.sprite = ItemIcons.Get(d);
                v.Icon.color = dragged ? new Color(1f, 1f, 1f, 0.3f) : Color.white;
                Color q = ItemQualityColors.Get(d.quality);
                v.Frame.color = d.quality == ItemQuality.Common ? new Color(0.5f, 0.47f, 0.42f, 0.95f) : q;
                v.Count.text = s.count > 1 ? s.count.ToString() : "";
                if (_svc != null && d.IsEquippable && InventoryModel.IsBag(v.Code) &&
                    (d.requiredLevel > _svc.Level || !InventoryModel.UsableByClass(d, _svc.Class)))
                    v.Icon.color = new Color(1f, 0.45f, 0.4f, dragged ? 0.3f : 1f); // nicht tragbar: roetlich
            }
            else
            {
                v.Frame.color = EmptyFrame;
                v.Count.text = "";
            }
            if (_lookOpen && InventoryModel.IsEquip(v.Code) && InventoryModel.SlotOf(v.Code) == _dyeSlot)
                v.Frame.color = new Color(0.95f, 0.85f, 0.45f, 1f); // gewaehlter Faerbeplatz
            if (v.Code == _flashCode && _flashTimer > 0f)
                v.Frame.color = new Color(1f, 0.2f, 0.15f, 1f);  // abgelehnt
        }

        // ================================================================== Maus

        private SlotView SlotAt(Vector2 mouse)
        {
            if (Over(_bagWin, mouse))
                foreach (SlotView s in _bagSlots) if (s.Rt.gameObject.activeSelf && Over(s.Rt, mouse)) return s;
            if (Over(_bagWin, mouse))
                foreach (SlotView s in _containerSlots) if (Over(s.Rt, mouse)) return s;
            if (Over(_charWin, mouse))
            {
                foreach (SlotView s in _equipSlots) if (s != null && Over(s.Rt, mouse)) return s;
                if (_ammoSlot != null && Over(_ammoSlot.Rt, mouse)) return _ammoSlot;
            }
            return null;
        }

        private int LootAt(Vector2 mouse)
        {
            if (!Over(_lootWin, mouse)) return -1;
            for (int i = 0; i < _lootRows.Count; i++)
                if (_lootRows[i].Rt.gameObject.activeSelf && Over(_lootRows[i].Rt, mouse)) return i;
            return -1;
        }

        private ItemStack StackAt(int code)
        {
            if (_svc == null) return ItemStack.Empty;
            if (InventoryModel.IsBag(code)) return _svc.Bag[code];
            if (InventoryModel.IsEquip(code)) return _svc.Equipment[code - InventoryModel.EquipBase];
            if (InventoryModel.IsContainerSlot(code)) return _svc.Containers[code - InventoryModel.ContainerBase];
            if (code == InventoryModel.AmmoCode)
            {
                // Munitionsplatz zeigt die gewaehlte Sorte und den gesamten Vorrat (die Pfeile liegen in den Taschen)
                if (string.IsNullOrEmpty(_svc.AmmoId)) return ItemStack.Empty;
                int n = 0;
                foreach (ItemStack st in _svc.Bag) if (!st.IsEmpty && st.id == _svc.AmmoId) n += st.count;
                return new ItemStack(_svc.AmmoId, Mathf.Max(1, n));
            }
            return ItemStack.Empty;
        }

        private void HandleMouse(Vector2 mouse)
        {
            // --- Linke Taste
            if (RawInput.LeftMousePressed)
            {
                if (_splitFrom >= 0 && Over(_splitWin, mouse)) SplitButtons(mouse);
                else if (Clicked(_lookButton, mouse)) _lookOpen = !_lookOpen;
                else if (_charOpen && Clicked(_masteryButton, mouse)) MasteryUi.Ensure(transform).Toggle();
                else if (_lookOpen && Over(_lookWin, mouse)) LookButtons(mouse);
                else if (Over(_previewRt, mouse) && Over(_charWin, mouse)) { _previewDrag = true; _previewLastX = mouse.x; }
                else if (Clicked(_bagButton, mouse)) _bagOpen = !_bagOpen;
                else if (Clicked(_charButton, mouse)) _charOpen = !_charOpen;
                else if (Clicked(_bagClose, mouse)) _bagOpen = false;
                else if (Clicked(_charClose, mouse)) _charOpen = false;
                else if (Clicked(_lootClose, mouse)) _svc.CloseLoot();
                else if (Clicked(_takeAll, mouse)) _svc.TakeAllLoot();
                else
                {
                    int li = LootAt(mouse);
                    if (li >= 0) _svc.TakeLoot(li);
                    else
                    {
                        SlotView s = SlotAt(mouse);
                        if (s != null && !StackAt(s.Code).IsEmpty)
                        {
                            _pressCode = s.Code;
                            _pressPos = mouse;
                        }
                    }
                }
            }

            if (_pressCode >= 0 && RawInput.LeftMouseHeld && !_dragging && (mouse - _pressPos).sqrMagnitude > 25f)
            {
                ItemDefinition d = GameData.Item(StackAt(_pressCode).id);
                if (d != null)
                {
                    _dragging = true;
                    _dragIcon.sprite = ItemIcons.Get(d);
                    _dragIcon.gameObject.SetActive(true);
                    _dirty = true;
                }
            }
            if (_dragging)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, mouse, null, out Vector2 local);
                ((RectTransform)_dragIcon.transform).anchoredPosition = local;
            }

            if (RawInput.LeftMouseReleased && _pressCode >= 0)
            {
                if (_dragging)
                {
                    SlotView target = SlotAt(mouse);
                    if (target != null && target.Code != _pressCode)
                    {
                        _pendingTarget = target.Code;
                        _svc.Move(_pressCode, target.Code);
                    }
                    // Loslassen ausserhalb: nichts passiert (nie stillschweigend wegwerfen) – liegt dort die
                    // Actionbar, wird nur eine Verknuepfung angelegt (der Gegenstand bleibt im Rucksack)
                    else if (target == null && InventoryModel.IsBag(_pressCode))
                        UiDrag.Drop(UiDrag.Kind.Item, StackAt(_pressCode).id, mouse);
                }
                else if (InventoryModel.IsEquip(_pressCode) && !RawInput.ShiftHeld)
                {
                    _dyeSlot = InventoryModel.SlotOf(_pressCode); // Platz zum Faerben waehlen
                    _dirty = true;
                }
                else if (RawInput.ShiftHeld && InventoryModel.IsBag(_pressCode))
                {
                    ItemStack st = StackAt(_pressCode);
                    if (st.count > 1)
                    {
                        _splitFrom = _pressCode;
                        _splitAmount = Mathf.Max(1, st.count / 2);
                        UpdateSplitText();
                    }
                }
                _pressCode = -1;
                _dragging = false;
                _dragIcon.gameObject.SetActive(false);
                _dirty = true;
            }
            if (!RawInput.LeftMouseHeld && !RawInput.LeftMouseReleased && _pressCode >= 0)
            {
                _pressCode = -1;
                _dragging = false;
                _dragIcon.gameObject.SetActive(false);
            }

            // --- Rechte Taste (Klick = Druecken und Loslassen auf demselben Platz)
            if (RawInput.RightMousePressed)
            {
                SlotView s = SlotAt(mouse);
                _rightCode = s != null ? s.Code : -1;
                _rightLoot = LootAt(mouse);
            }
            if (RawInput.RightMouseReleased)
            {
                SlotView s = SlotAt(mouse);
                int li = LootAt(mouse);
                if (s != null && s.Code == _rightCode && !StackAt(s.Code).IsEmpty)
                {
                    _pendingTarget = s.Code;
                    var trade = Ashveil.Gameplay.Quests.ProgressionClient.Current;
                    // 21.10. (FORTSCHRITT-01): verkaufen nur bei OFFENEM Händlerfenster, nicht schon im Gespräch mit einem
                    // Händler (sonst war z. B. die Q01-Waffe bei Ilse trotz „Rechtsklick: anlegen“ verkauft)
                    if (InventoryModel.IsBag(s.Code) && trade != null && trade.DialogNpc != null && QuestUi.HandelOffen) trade.Sell(s.Code);
                    else if (InventoryModel.IsBag(s.Code)) _svc.Use(s.Code);
                    else if (s.Code == InventoryModel.AmmoCode) _svc.Move(InventoryModel.AmmoCode, 0); // Sorte abwaehlen
                    else if (InventoryModel.IsContainerSlot(s.Code))
                    {
                        // Leere Tasche ablegen: in die erste freie normale Zelle ausserhalb dieser Tasche
                        InventoryModel m = InventoryClient.Mirror(_svc);
                        int bag = s.Code - InventoryModel.ContainerBase, free = -1;
                        foreach (int c in m.Cells())
                            if (m.Bag[c].IsEmpty && InventoryModel.BagOf(c) != bag && m.KindOf(InventoryModel.BagOf(c)) == ContainerKind.General) { free = c; break; }
                        if (!m.BagEmpty(bag)) Ashveil.Core.Events.CombatTextEvent.Error("Leere die Tasche zuerst.");
                        else if (free < 0) Ashveil.Core.Events.CombatTextEvent.Error("Kein freier Platz.");
                        else _svc.Move(s.Code, free);
                    }
                    else _svc.Unequip(InventoryModel.SlotOf(s.Code));
                }
                else if (li >= 0 && li == _rightLoot) _svc.TakeLoot(li);
                _rightCode = -1;
                _rightLoot = -1;
            }

            // --- Mausrad im Teilen-Fenster
            if (_splitFrom >= 0 && Over(_splitWin, mouse))
            {
                float steps = RawInput.ScrollSteps;
                if (steps != 0f)
                {
                    _splitAmount += (int)steps;
                    UpdateSplitText();
                }
            }

            HoverButton(_takeAll, mouse);
            HoverButton(_splitOk, mouse);
            HoverButton(_splitCancel, mouse);
            HoverButton(_bagButton, mouse);
            HoverButton(_charButton, mouse);
            HoverButton(_lookButton, mouse);
            HoverButton(_masteryButton, mouse);
            foreach (OptionRow r in _rows)
            {
                HoverButton(r.Prev, mouse);
                HoverButton(r.Next, mouse);
            }
        }

        private static bool Clicked(Button b, Vector2 mouse) => b != null && Over(b.Rt, mouse);

        private static void HoverButton(Button b, Vector2 mouse)
        {
            if (b != null) b.Back.color = Over(b.Rt, mouse) ? ButtonHover : ButtonColor;
        }

        private void SplitButtons(Vector2 mouse)
        {
            if (Clicked(_splitMinus, mouse)) { _splitAmount--; UpdateSplitText(); }
            else if (Clicked(_splitPlus, mouse)) { _splitAmount++; UpdateSplitText(); }
            else if (Clicked(_splitCancel, mouse)) _splitFrom = -1;
            else if (Clicked(_splitOk, mouse))
            {
                InventoryModel m = InventoryClient.Mirror(_svc);
                ItemDefinition sd = GameData.Item(_svc.Bag[_splitFrom].id);
                int free = -1;
                foreach (int c in m.Cells()) if (m.Bag[c].IsEmpty && m.Accepts(c, sd)) { free = c; break; }
                if (free < 0) Ashveil.Core.Events.CombatTextEvent.Error("Kein freier Platz zum Aufteilen.");
                else _svc.Split(_splitFrom, _splitAmount, free);
                _splitFrom = -1;
            }
        }

        private void UpdateSplitText()
        {
            ItemStack st = StackAt(_splitFrom);
            if (st.IsEmpty || st.count < 2)
            {
                _splitFrom = -1;
                return;
            }
            _splitAmount = Mathf.Clamp(_splitAmount, 1, st.count - 1);
            _splitText.text = $"{_splitAmount} / {st.count}";
        }

        // ================================================================== Vorschau

        private void UpdatePreview(Vector2 mouse)
        {
            bool show = _charOpen && _charWin.gameObject.activeInHierarchy;
            CharacterPreviewStage stage = show ? CharacterPreviewStage.Ensure() : CharacterPreviewStage.Existing;
            if (stage == null) return;
            stage.Show(show);
            if (!show) return;
            if (_preview.texture != stage.Texture) _preview.texture = stage.Texture;

            if (_previewDrag)
            {
                if (RawInput.LeftMouseHeld)
                {
                    stage.Rotate(-(mouse.x - _previewLastX) * 0.6f);
                    _previewLastX = mouse.x;
                }
                else _previewDrag = false;
            }

            // Anprobe: Alt + Zeigen auf einen Gegenstand im Rucksack (nur Vorschau, aendert nichts). Strg gehoert der Actionbar.
            int tryOn = -1;
            if (RawInput.AltHeld)
            {
                SlotView sv = SlotAt(mouse);
                if (sv != null && InventoryModel.IsBag(sv.Code) && !StackAt(sv.Code).IsEmpty) tryOn = sv.Code;
            }
            if (tryOn != _tryOnCode) { _tryOnCode = tryOn; _previewDirty = true; }
            if (!_previewDirty) return;
            _previewDirty = false;

            var eq = (ItemStack[])_svc.Equipment.Clone();
            if (_tryOnCode >= 0)
            {
                ItemStack st = StackAt(_tryOnCode);
                ItemDefinition d = GameData.Item(st.id);
                if (d != null && d.IsEquippable)
                {
                    var probe = new InventoryModel { Class = _svc.Class, Level = _svc.Level };
                    probe.Load(new ItemStack[InventoryModel.BagSize], eq);
                    probe.Bag[0] = st;
                    // Anprobe folgt denselben Regeln wie echtes Anlegen (Zweihand, Nebenhand)
                    if (probe.AutoEquip(0).Ok) eq = (ItemStack[])probe.Equipment.Clone();
                }
            }
            CharacterAppearanceData look = AppearanceClient.Current != null ? AppearanceClient.Current.Current : new CharacterAppearanceData();
            stage.SetState(eq, look);
            _previewHint.text = _tryOnCode >= 0 ? "<color=#e8c070>Anprobe – nur Vorschau</color>" : "Ziehen: drehen · Alt + Zeigen: anprobieren";
        }

        private void UpdateFlash(float dt)
        {
            if (_flashTimer <= 0f) return;
            _flashTimer -= dt;
            _dirty = true;
            if (_flashTimer <= 0f) _flashCode = -1;
        }

        // ================================================================== Aussehen & Faerben

        private void BuildLookWindow(float charWidth)
        {
            float w = 330f, h = 520f;
            RectTransform inner = UiKit.Panel("Aussehen", _canvasRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f + charWidth + 10f, 20f), new Vector2(w, h));
            _lookWin = (RectTransform)inner.parent;
            TextMeshProUGUI title = UiKit.CreateText("Title", _lookWin, "Aussehen", 20f, UiKit.TextColor);
            Place(title.rectTransform, new Vector2(14f, -8f), new Vector2(w - 60f, 26f));
            float y = -44f;
            _rowBody = Row("Körper", ref y);
            _rowSkin = Row("Hautton", ref y);
            _rowHair = Row("Frisur", ref y);
            _rowHairColor = Row("Haarfarbe", ref y);
            _rowBeard = Row("Bart", ref y);
            _rowHelm = Row("Helm", ref y);
            _rowCloak = Row("Umhang", ref y);
            y -= 8f;
            _dyeTitle = UiKit.CreateText("DyeTitle", _lookWin, "Färben", 17f, UiKit.TextColor);
            Place(_dyeTitle.rectTransform, new Vector2(14f, y), new Vector2(w - 28f, 22f));
            y -= 26f;
            _rowDyePrimary = Row("Hauptfarbe", ref y, true);
            _rowDyeSecondary = Row("Nebenfarbe", ref y, true);
            _rowDyeDetail = Row("Details", ref y, true);
            _lookNote = UiKit.CreateText("Note", _lookWin,
                "Angelegten Gegenstand anklicken, um ihn zu färben.\nAussehen übertragen: folgt in einem späteren Ausbau.", 12f, UiKit.MutedText);
            _lookNote.textWrappingMode = TextWrappingModes.Normal;
            Place(_lookNote.rectTransform, new Vector2(14f, y - 4f), new Vector2(w - 28f, 40f));
            _lookWin.gameObject.SetActive(false);
        }

        private OptionRow Row(string label, ref float y, bool swatch = false)
        {
            var r = new OptionRow();
            r.Rt = UiKit.CreateRect("Row " + label, _lookWin, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, y), new Vector2(302f, 34f));
            r.Label = UiKit.CreateText("Label", r.Rt, label, 15f, UiKit.MutedText);
            Place(r.Label.rectTransform, new Vector2(0f, -6f), new Vector2(100f, 22f));
            r.Prev = MakeButton(r.Rt, "<", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(104f, -2f), new Vector2(30f, 28f));
            r.Value = UiKit.CreateText("Value", r.Rt, "", 15f, UiKit.TextColor, TextAlignmentOptions.Center);
            Place(r.Value.rectTransform, new Vector2(138f, -6f), new Vector2(126f, 22f));
            r.Next = MakeButton(r.Rt, ">", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(270f, -2f), new Vector2(30f, 28f));
            if (swatch)
            {
                RectTransform sw = UiKit.CreateRect("Swatch", r.Rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(84f, -9f), new Vector2(16f, 16f));
                r.Swatch = UiKit.AddImage(sw, Color.white);
            }
            _rows.Add(r);
            y -= 38f;
            return r;
        }

        private void RefreshLook()
        {
            if (_lookWin == null) return;
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            IAppearanceService look = AppearanceClient.Current;
            bool ok = cat != null && look != null && look.Current != null;
            _lookButton.Rt.gameObject.SetActive(ok);
            if (!ok) return;
            CharacterAppearanceData d = look.Current;
            _rowBody.Value.text = d.body < cat.bodies.Count ? cat.bodies[d.body].name : "-";
            _rowSkin.Value.text = cat.SkinName(d.body, d.skin);
            _rowSkin.Rt.gameObject.SetActive(cat.SkinCount(d.body) > 1);
            _rowHair.Value.text = d.hair < cat.hairstyles.Count ? cat.hairstyles[d.hair].name : "-";
            _rowHairColor.Value.text = d.hairColor < cat.hairColors.Count ? cat.hairColors[d.hairColor].name : "-";
            bool beards = CountStyles(cat.beards, cat, d.body) > 0;
            _rowBeard.Rt.gameObject.SetActive(beards); // nur, wenn es fuer diesen Koerper Baerte gibt
            _rowBeard.Value.text = d.beard < cat.beards.Count ? cat.beards[d.beard].name : "-";
            _rowHelm.Value.text = d.hideHelm ? "ausgeblendet" : "sichtbar";
            _rowCloak.Value.text = d.hideCloak ? "ausgeblendet" : "sichtbar";

            ItemStack worn = _svc != null ? _svc.Equipment[(int)_dyeSlot] : ItemStack.Empty;
            ItemDefinition wd = worn.IsEmpty ? null : GameData.Item(worn.id);
            bool dyeable = wd != null && wd.appearance != null && EquipmentVisuals.IsVisibleSlot(_dyeSlot);
            _dyeTitle.text = dyeable ? $"Färben: {ItemText.SlotName(_dyeSlot)} <size=80%><color=#a8a296>({wd.DisplayName})</color></size>"
                                     : $"Färben: {ItemText.SlotName(_dyeSlot)} <size=80%><color=#a8a296>(nichts Färbbares angelegt)</color></size>";
            DyeRow(_rowDyePrimary, d, DyeChannel.Primary, cat, dyeable ? wd.appearance : null);
            DyeRow(_rowDyeSecondary, d, DyeChannel.Secondary, cat, dyeable ? wd.appearance : null);
            DyeRow(_rowDyeDetail, d, DyeChannel.Detail, cat, dyeable ? wd.appearance : null);
        }

        private void DyeRow(OptionRow r, CharacterAppearanceData d, DyeChannel c, AppearanceCatalog cat, AppearanceDefinition a)
        {
            r.Rt.gameObject.SetActive(a != null);
            if (a == null) return;
            byte idx = d.dyes[(int)_dyeSlot].Get(c);
            r.Value.text = idx == 0 ? "Standard" : (idx <= cat.dyes.Count ? cat.dyes[idx - 1].name : "-");
            r.Swatch.color = idx == 0 ? a.DefaultColor(c) : cat.dyes[idx - 1].color;
        }

        private static int CountStyles(List<AppearanceCatalog.StyleEntry> list, AppearanceCatalog cat, int body)
        {
            int n = 0;
            for (int i = 1; i < list.Count; i++) if (cat.StyleAllowed(list, i, body)) n++;
            return n;
        }

        private static int Step(List<AppearanceCatalog.StyleEntry> list, AppearanceCatalog cat, int body, int current, int dir)
        {
            for (int k = 1; k <= list.Count; k++)
            {
                int i = ((current + dir * k) % list.Count + list.Count) % list.Count;
                if (cat.StyleAllowed(list, i, body)) return i;
            }
            return current;
        }

        private void LookButtons(Vector2 mouse)
        {
            AppearanceCatalog cat = GameDataBootstrap.Catalog;
            IAppearanceService look = AppearanceClient.Current;
            if (cat == null || look == null || look.Current == null) return;
            CharacterAppearanceData d = look.Current.Clone();
            int dir = 0;
            OptionRow hit = null;
            foreach (OptionRow r in _rows)
            {
                if (!r.Rt.gameObject.activeInHierarchy) continue;
                if (Clicked(r.Prev, mouse)) { hit = r; dir = -1; break; }
                if (Clicked(r.Next, mouse)) { hit = r; dir = 1; break; }
            }
            if (hit == null) return;
            int Wrap(int v, int n) => n <= 0 ? 0 : ((v % n) + n) % n;
            if (hit == _rowBody)
            {
                d.body = Wrap(d.body + dir, cat.bodies.Count);
                d.hair = AppearanceDefaults.FirstStyle(cat.hairstyles, cat, d.body);
                if (!cat.StyleAllowed(cat.beards, d.beard, d.body)) d.beard = 0;
                d.skin = Mathf.Min(d.skin, cat.SkinCount(d.body) - 1);
                if (!cat.FeatureAllowed(d.feature, d.body)) d.feature = 0;
            }
            else if (hit == _rowSkin) d.skin = Wrap(d.skin + dir, cat.SkinCount(d.body));
            else if (hit == _rowHair) d.hair = Step(cat.hairstyles, cat, d.body, d.hair, dir);
            else if (hit == _rowHairColor) d.hairColor = Wrap(d.hairColor + dir, cat.hairColors.Count);
            else if (hit == _rowBeard) d.beard = Step(cat.beards, cat, d.body, d.beard, dir);
            else if (hit == _rowHelm) d.hideHelm = !d.hideHelm;
            else if (hit == _rowCloak) d.hideCloak = !d.hideCloak;
            else
            {
                DyeChannel c = hit == _rowDyePrimary ? DyeChannel.Primary : hit == _rowDyeSecondary ? DyeChannel.Secondary : DyeChannel.Detail;
                DyeSet set = d.dyes[(int)_dyeSlot];
                set.Set(c, (byte)Wrap(set.Get(c) + dir, cat.dyes.Count + 1));
                d.dyes[(int)_dyeSlot] = set;
            }
            look.RequestChange(d);
        }

        // ================================================================== Tooltip

        private void UpdateHover(Vector2 mouse)
        {
            if (_dragging)
            {
                HideTooltip();
                return;
            }
            SlotView s = SlotAt(mouse);
            ItemStack stack = ItemStack.Empty;
            bool equipped = false;
            if (s != null)
            {
                stack = StackAt(s.Code);
                equipped = InventoryModel.IsEquip(s.Code);
            }
            else
            {
                int li = LootAt(mouse);
                if (li >= 0 && li < _svc.Loot.Count) stack = _svc.Loot[li];
            }
            ItemDefinition d = stack.IsEmpty ? null : GameData.Item(stack.id);
            if (d == null)
            {
                HideTooltip();
                return;
            }

            List<ItemDefinition> current = equipped ? null : Replaced(d);
            _tipText.text = ItemText.Tooltip(d, stack, _svc, equipped, current);
            Vector2 tipSize = Fit(_tip, _tipText);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, mouse, null, out Vector2 local);
            Vector2 half = _canvasRt.rect.size * 0.5f;
            Vector2 pos = local + new Vector2(22f, -22f);
            bool flip = pos.x + tipSize.x > half.x - 8f;
            if (flip) pos.x = local.x - 22f - tipSize.x;
            pos.y = Mathf.Clamp(pos.y, -half.y + tipSize.y + 8f, half.y - 8f);
            _tip.anchoredPosition = pos;
            SetActive(_tip, true);

            if (current != null && current.Count > 0)
            {
                var sb = new System.Text.StringBuilder("<color=#a8a296>Derzeit angelegt</color>\n");
                for (int i = 0; i < current.Count; i++)
                {
                    if (i > 0) sb.Append("\n\n");
                    sb.Append(ItemText.Tooltip(current[i], new ItemStack(current[i].Id, 1, true), _svc, true, null));
                }
                _cmpText.text = sb.ToString();
                Vector2 cmpSize = Fit(_cmp, _cmpText);
                float x = flip ? pos.x - cmpSize.x - 6f : pos.x + tipSize.x + 6f;
                if (!flip && x + cmpSize.x > half.x - 8f) x = pos.x - cmpSize.x - 6f;
                _cmp.anchoredPosition = new Vector2(x, Mathf.Clamp(pos.y, -half.y + cmpSize.y + 8f, half.y - 8f));
                SetActive(_cmp, true);
            }
            else SetActive(_cmp, false);
        }

        /// <summary>Was ein neues Teil ersetzen wuerde (fuer den Vergleich).</summary>
        private List<ItemDefinition> Replaced(ItemDefinition d)
        {
            if (!d.IsEquippable || _svc == null || d.IsContainer || d.equipType == EquipType.Bag) return null;
            var list = new List<ItemDefinition>(2);
            void Add(EquipmentSlot slot)
            {
                ItemStack s = _svc.Equipment[(int)slot];
                ItemDefinition e = s.IsEmpty ? null : GameData.Item(s.id);
                if (e != null && !list.Contains(e)) list.Add(e);
            }
            switch (d.equipType)
            {
                case EquipType.Ring: Add(EquipmentSlot.Ring1); Add(EquipmentSlot.Ring2); break;
                case EquipType.Trinket: Add(EquipmentSlot.Trinket1); Add(EquipmentSlot.Trinket2); break;
                case EquipType.Weapon:
                    if (d.IsRangedWeapon) Add(EquipmentSlot.Ranged);
                    else if (d.IsTwoHanded) { Add(EquipmentSlot.MainHand); Add(EquipmentSlot.OffHand); }
                    else if (d.Hand == WeaponHand.OffHandOnly) Add(EquipmentSlot.OffHand);
                    else Add(EquipmentSlot.MainHand);
                    break;
                default:
                    var probe = new InventoryModel();
                    Add(probe.BestSlot(d));
                    break;
            }
            return list;
        }

        private static Vector2 Fit(RectTransform box, TextMeshProUGUI text)
        {
            const float width = 340f;
            Vector2 pref = text.GetPreferredValues(text.text, width - 24f, 0f);
            var size = new Vector2(width, Mathf.Ceil(pref.y) + 22f);
            box.sizeDelta = size;
            return size;
        }

        private void HideTooltip()
        {
            SetActive(_tip, false);
            SetActive(_cmp, false);
        }

        /// <summary>Schildblock (passiv): nur mit Schild, inkl. Meisterschaft.</summary>
        private static string BlockLine(EquipmentStats st)
        {
            if (!st.HasShield) return "";
            int rank = Ashveil.Gameplay.Mastery.MasteryClient.Rank(MasteryCatalog.BlockingId);
            float chance = EquipmentStats.BaseBlockChance + MasteryCatalog.Blocking.Effect("blockChance", rank);
            float value = Mathf.Round(st.BlockValue * (1f + MasteryCatalog.Blocking.Effect("blockValue", rank)));
            return $"\n<color=#a8a296>Blocken</color> {chance * 100f:0.#} %<pos=50%><color=#a8a296>Blockwert</color> {value:0}";
        }
    }
}
