using System.Collections.Generic;
using System.Text;
using Ashveil.Data;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Quests;
using Ashveil.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// Milestone 4 – alles rund um Fortschritt:
    ///   Erfahrungsleiste, Geld, Auftragsverfolgung (rechts), Meldungen (Mitte oben),
    ///   Gespraech mit NPCs (Auftraege annehmen/abgeben, Belohnung waehlen), Haendlerfenster mit Rueckkauf,
    ///   Questlog (L), Markierungen ueber NPCs (! / ?) je Spieler.
    /// Wie das Inventar: nur Bitten an den Server (<see cref="ProgressionClient"/>), Anzeige folgt seiner Antwort.
    /// </summary>
    [DefaultExecutionOrder(-490)] // nach InventoryUi: ergaenzt UiPointer.OverUi statt es zu ueberschreiben
    public sealed class QuestUi : MonoBehaviour
    {
        private sealed class Btn
        {
            public RectTransform Rt;
            public Image Back;
            public TextMeshProUGUI Label;
            public bool Visible => Rt.gameObject.activeSelf;
        }

        private sealed class Row
        {
            public RectTransform Rt;
            public Image Back, Frame, Icon;
            public TextMeshProUGUI Text, Right;
            public int Index;
            public ItemDefinition Item;
            public ItemStack Stack;
        }

        private enum DialogMode { Main, Offer, TurnIn, Progress }

        private static readonly Color ButtonColor = new Color(0.22f, 0.17f, 0.11f, 0.95f);
        private static readonly Color ButtonHover = new Color(0.36f, 0.27f, 0.16f, 0.98f);
        private static readonly Color RowColor = new Color(0f, 0f, 0f, 0.25f);
        private static readonly Color RowHover = new Color(0.36f, 0.27f, 0.16f, 0.55f);
        private static readonly Color RowSelected = new Color(0.62f, 0.48f, 0.2f, 0.7f);
        private static readonly Color XpColor = new Color(0.52f, 0.34f, 0.78f);

        private IProgressionService _svc;
        private RectTransform _canvasRt;
        private bool _built, _dirty = true;

        // HUD
        private UiKit.Bar _xpBar;
        private TextMeshProUGUI _money, _tracker, _toast, _bigToast;
        private RectTransform _trackerBack;
        private float _toastTimer, _bigToastTimer;
        private readonly Queue<string> _toastQueue = new Queue<string>();

        // Gespraech
        private RectTransform _dlgWin;
        private TextMeshProUGUI _dlgTitle, _dlgBody;
        private Btn _dlgClose, _dlgAccept, _dlgTurnIn, _dlgBack, _dlgTrade, _dlgTrain;
        private readonly List<Row> _dlgRows = new List<Row>();     // Auftragsliste
        private readonly List<Row> _rewardRows = new List<Row>();  // Belohnungen
        private DialogMode _mode;
        private QuestDefinition _modeQuest;
        private string _dlgNpcId;
        private int _choice = -1;
        private readonly List<QuestDefinition> _listed = new List<QuestDefinition>();
        private readonly List<QuestItemReward> _shownRewards = new List<QuestItemReward>();
        private int _choiceStart;
        private float _turnInWait;   // 21.10.: Doppelklick-Sperre fürs Abgeben (B2)
        private float _acceptWait;   // 21.10.: gleiche Sperre fürs Annehmen

        // Haendler
        private RectTransform _vendWin;
        private TextMeshProUGUI _vendTitle, _vendMoney, _vendHint;
        private Btn _vendClose;
        private readonly List<Row> _stockRows = new List<Row>();
        private readonly List<Row> _buybackRows = new List<Row>();
        private const int BuybackRows = 6;   // 21.10.: so viele Plätze hält der Server (NetworkProgression.buybackSlots, Prefab Player_Placeholder) (Nr. 9)
        private bool _vendorOpen;

        /// <summary>21.10. (FORTSCHRITT-01): Händlerfenster sichtbar offen. Nur dann verkauft der Rechtsklick im Rucksack
        /// (InventoryUi); im bloßen Gespräch mit einem Händler gilt wie im Hinweis „Rechtsklick: benutzen/anlegen“.</summary>
        public static bool HandelOffen { get; private set; }

        // Questlog
        private RectTransform _logWin;
        private TextMeshProUGUI _logDetail, _logEmpty;
        private Btn _logClose, _logAbandon, _logPrevious, _logNext;
        private TextMeshProUGUI _logPageLabel;
        private int _logPage;
        private const int LogPageSize = 11;
        private readonly List<Row> _logRows = new List<Row>();
        private bool _logOpen;
        private string _logSelected;

        // Tooltip
        private RectTransform _tip;
        private TextMeshProUGUI _tipText;

        private float _markerTimer;

        // ================================================================== Lebenszyklus

        private void OnEnable()
        {
            ProgressionClient.Changed += MarkDirty;
            InventoryClient.Changed += MarkDirty;
            ProgressionClient.LeveledUp += OnLevelUp;
            ProgressionClient.QuestEvent += OnQuestEvent;
            ProgressionClient.XpGained += OnXp;
            ProgressionClient.Message += Toast;
            Ashveil.Gameplay.Mastery.MasteryClient.Milestone += OnMilestone;
        }

        private void OnDisable()
        {
            ProgressionClient.Changed -= MarkDirty;
            InventoryClient.Changed -= MarkDirty;
            ProgressionClient.LeveledUp -= OnLevelUp;
            ProgressionClient.QuestEvent -= OnQuestEvent;
            ProgressionClient.XpGained -= OnXp;
            ProgressionClient.Message -= Toast;
            Ashveil.Gameplay.Mastery.MasteryClient.Milestone -= OnMilestone;
            HandelOffen = false;   // 21.10. (FORTSCHRITT-01): Flag bleibt nicht hängen, wenn die QuestUi wegfällt
        }

        private void MarkDirty() => _dirty = true;

        private void OnLevelUp(int level)
        {
            _bigToast.fontSize = 44f;
            _bigToast.text = $"Stufe {level} erreicht!";
            _bigToastTimer = 3.5f;
        }

        /// <summary>Meisterschafts-Meilenstein: kleiner als ein Stufenaufstieg, gleiche Stelle (Max 28.09.).</summary>
        private void OnMilestone(string name, int rank, string effects)
        {
            if (_bigToast == null) return;
            if (_bigToastTimer > 1f && _bigToast.fontSize > 40f) return; // Stufenaufstieg hat Vorrang
            _bigToast.fontSize = 30f;
            _bigToast.text = $"{name} – Rang {rank}\n<size=19><color=#d8d0c0>{effects}</color></size>";
            _bigToastTimer = 3f;
        }

        private void OnQuestEvent(string title, bool completed) =>
            Toast(completed ? $"Auftrag abgeschlossen: <color=#f0c850>{title}</color>" : $"Auftrag angenommen: <color=#f0c850>{title}</color>");

        private void OnXp(int amount, Vector3 where) => Toast($"<color=#b58cf0>+{amount} Erfahrung</color>");

        private void Toast(string text)
        {
            if (_toastQueue.Count > 6) _toastQueue.Dequeue();
            _toastQueue.Enqueue(text);
        }

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
            Canvas canvas = UiKit.CreateCanvas("Quest Canvas", transform, 22);
            _canvasRt = (RectTransform)canvas.transform;

            _xpBar = UiKit.CreateBar("XP", _canvasRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(760f, 14f), XpColor, 11f);
            _money = Text(_canvasRt, "Money", 17f, TextAlignmentOptions.Right, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 142f), new Vector2(320f, 24f));

            // 21.10.: Tracker auf eigener Ebene 19 unter dem Inventar (20) – lag sonst über dem Taschenfenster (B, auch beim Händler) (F2); Gespräch/Buch bleiben auf 22
            RectTransform trackerRt = (RectTransform)UiKit.CreateCanvas("Tracker Canvas", transform, 19).transform;
            _trackerBack = UiKit.CreateRect("Tracker Back", trackerRt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -142f), new Vector2(380f, 10f));
            UiKit.AddImage(_trackerBack, new Color(0f, 0f, 0f, 0.38f));
            _tracker = Text(trackerRt, "Tracker", 16f, TextAlignmentOptions.TopRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -150f), new Vector2(360f, 400f));
            _tracker.textWrappingMode = TextWrappingModes.Normal;
            _toast = Text(_canvasRt, "Toast", 20f, TextAlignmentOptions.Top, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(900f, 160f));
            _bigToast = Text(_canvasRt, "Level Toast", 44f, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 220f), new Vector2(900f, 70f));
            _bigToast.color = new Color(1f, 0.82f, 0.4f);

            // --- Gespraech
            _dlgWin = Window("Gespräch", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 30f), new Vector2(460f, 600f), out _dlgTitle, out _dlgClose);
            _dlgBody = Text(_dlgWin, "Body", 16f, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -44f), new Vector2(428f, 420f));
            _dlgBody.textWrappingMode = TextWrappingModes.Normal;
            // 21.10.: 8 statt 6 Auftragszeilen – bei Bralt sind 7 Einträge möglich, der letzte fiel still heraus (Nr. 6); Lage regelt LayoutRows
            for (int i = 0; i < 8; i++) _dlgRows.Add(MakeRow(_dlgWin, new Vector2(16f, -300f - i * 36f), new Vector2(428f, 32f), false));
            for (int i = 0; i < 6; i++) _rewardRows.Add(MakeRow(_dlgWin, new Vector2(16f, -300f - i * 40f), new Vector2(428f, 36f), true));
            _dlgAccept = MakeButton(_dlgWin, "Annehmen", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 12f), new Vector2(130f, 34f));
            _dlgTurnIn = MakeButton(_dlgWin, "Abgeben", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 12f), new Vector2(130f, 34f));
            _dlgBack = MakeButton(_dlgWin, "Zurück", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 12f), new Vector2(110f, 34f));
            _dlgTrade = MakeButton(_dlgWin, "Handeln", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 12f), new Vector2(130f, 34f));
            _dlgTrain = MakeButton(_dlgWin, "Ausbildung", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-152f, 12f), new Vector2(130f, 34f));

            // --- Haendler
            _vendWin = Window("Händler", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(520f, 30f), new Vector2(420f, 600f), out _vendTitle, out _vendClose);
            for (int i = 0; i < 9; i++) _stockRows.Add(MakeRow(_vendWin, new Vector2(14f, -44f - i * 40f), new Vector2(392f, 36f), true));
            // 21.10.: Rückkauf zeigt alle 6 Plätze des Servers statt 3 (Nr. 9) – kompakte Zeilen (20 hoch, Abstand 22), damit sie unter
            // die 9. Warenzeile (Unterkante -400) und über Hinweis/Geld (Oberkante -562) passen: Titel -403..-421, Zeilen -424..-554
            TextMeshProUGUI bb = Text(_vendWin, "Buyback Title", 15f, TextAlignmentOptions.Left, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -403f), new Vector2(392f, 18f));
            bb.text = "<color=#a8a296>Zurückkaufen</color>";
            for (int i = 0; i < BuybackRows; i++)
            {
                Row b = MakeRow(_vendWin, new Vector2(14f, -424f - i * 22f), new Vector2(392f, 20f), true);
                b.Text.fontSize = 14f;   // passend zur flachen Zeile
                b.Right.fontSize = 13f;
                _buybackRows.Add(b);
            }
            _vendMoney = Text(_vendWin, "Money", 16f, TextAlignmentOptions.Right, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 10f), new Vector2(240f, 22f));
            _vendHint = Text(_vendWin, "Hint", 12f, TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 8f), new Vector2(250f, 30f));
            _vendHint.textWrappingMode = TextWrappingModes.Normal;
            _vendHint.color = UiKit.MutedText;
            _vendHint.text = "Klick kauft · Umschalt+Klick kauft 5\nRechtsklick im Rucksack verkauft";

            // --- Questlog
            _logWin = Window("Aufträge (L)", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(760f, 500f), out _, out _logClose);
            for (int i = 0; i < QuestRules.MaxActive && i < LogPageSize; i++) _logRows.Add(MakeRow(_logWin, new Vector2(14f, -44f - i * 36f), new Vector2(250f, 32f), false));
            _logPrevious = MakeButton(_logWin, "<", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 12f), new Vector2(44f, 32f));
            _logNext = MakeButton(_logWin, ">", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(220f, 12f), new Vector2(44f, 32f));
            _logPageLabel = Text(_logWin, "Page", 14f, TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(62f, 12f), new Vector2(154f, 32f));
            _logDetail = Text(_logWin, "Detail", 16f, TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(280f, -44f), new Vector2(460f, 390f));
            _logDetail.textWrappingMode = TextWrappingModes.Normal;
            _logEmpty = Text(_logWin, "Empty", 16f, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 60f));
            _logEmpty.color = UiKit.MutedText;
            _logEmpty.text = "Keine Aufträge. NPCs mit gelbem <color=#f0c850>!</color> haben Arbeit für dich (Rechtsklick).";
            _logAbandon = MakeButton(_logWin, "Aufgeben", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 12f), new Vector2(120f, 32f));

            // --- Tooltip
            RectTransform inner = UiKit.Panel("Tooltip", _canvasRt, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), Vector2.zero, new Vector2(340f, 100f));
            _tip = (RectTransform)inner.parent;
            inner.GetComponent<Image>().color = new Color(0.04f, 0.04f, 0.06f, 0.95f);
            _tipText = UiKit.CreateText("Text", _tip, "", 16f, UiKit.TextColor);
            _tipText.textWrappingMode = TextWrappingModes.Normal;
            var tr = _tipText.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(12f, 10f);
            tr.offsetMax = new Vector2(-12f, -10f);
            _tip.gameObject.SetActive(false);

            // UI-204: Esc schließt Gespräch (mit Händler) und Auftragsbuch einzeln über den Fensterstapel
            UiFensterstapel.Anmelden(this, "Gespräch", () => _svc != null && _svc.Ready && _svc.DialogNpc != null, () => { if (_svc != null) _svc.CloseDialog(); });
            UiFensterstapel.Anmelden(this, "Auftragsbuch", () => _svc != null && _svc.Ready && _logOpen, () => { _logOpen = false; _dirty = true; });
            _built = true;
        }

        private RectTransform Window(string title, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, out TextMeshProUGUI titleText, out Btn close)
        {
            RectTransform inner = UiKit.Panel(title, _canvasRt, anchor, pivot, pos, size);
            var border = (RectTransform)inner.parent;
            titleText = UiKit.CreateText("Title", border, title, 20f, UiKit.TextColor);
            Place(titleText.rectTransform, new Vector2(14f, -8f), new Vector2(size.x - 60f, 28f));
            close = MakeButton(border, "×", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(26f, 26f));
            border.gameObject.SetActive(false);
            return border;
        }

        private static TextMeshProUGUI Text(RectTransform parent, string name, float size, TextAlignmentOptions align, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box)
        {
            TextMeshProUGUI t = UiKit.CreateText(name, parent, "", size, UiKit.TextColor, align);
            t.richText = true;
            RectTransform rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            return t;
        }

        private Btn MakeButton(RectTransform parent, string text, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            RectTransform rt = UiKit.CreateRect("Button " + text, parent, anchor, pivot, pos, size);
            UiKit.AddImage(rt, UiKit.BorderColor);
            Image back = UiKit.AddImage(UiKit.Stretch("Inner", rt, 1.5f), ButtonColor);
            TextMeshProUGUI label = UiKit.CreateText("Label", rt, text, 16f, UiKit.TextColor, TextAlignmentOptions.Center);
            var lr = label.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            return new Btn { Rt = rt, Back = back, Label = label };
        }

        private Row MakeRow(RectTransform parent, Vector2 topLeft, Vector2 size, bool icon)
        {
            RectTransform rt = UiKit.CreateRect("Row", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), topLeft, size);
            Image back = UiKit.AddImage(rt, RowColor);
            Image frame = null, img = null;
            float x = 8f;
            if (icon)
            {
                RectTransform fr = UiKit.CreateRect("Frame", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(size.y - 4f, size.y - 4f));
                frame = UiKit.AddImage(fr, UiKit.BorderColor);
                img = UiKit.AddImage(UiKit.Stretch("Icon", fr, 2f), Color.white);
                img.preserveAspect = true;
                x = size.y + 6f;
            }
            TextMeshProUGUI text = UiKit.CreateText("Text", rt, "", 15f, UiKit.TextColor, TextAlignmentOptions.Left);
            text.richText = true;
            var t = text.rectTransform;
            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.offsetMin = new Vector2(x, 0f);
            t.offsetMax = new Vector2(-8f, 0f);
            TextMeshProUGUI right = UiKit.CreateText("Right", rt, "", 14f, UiKit.TextColor, TextAlignmentOptions.Right);
            right.richText = true;
            var r = right.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(x, 0f);
            r.offsetMax = new Vector2(-8f, 0f);
            rt.gameObject.SetActive(false);
            return new Row { Rt = rt, Back = back, Frame = frame, Icon = img, Text = text, Right = right };
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
            if (!ReferenceEquals(_svc, ProgressionClient.Current))
            {
                _svc = ProgressionClient.Current;
                _logPage = 0;
                _logSelected = null;
                _dirty = true;
            }
            bool ready = _svc != null && _svc.Ready;
            IInventoryService inv = InventoryClient.Current;

            // Tastatur
            if (ready && UiPointer.HotkeysAllowed && RawInput.WasPressed(GameKey.QuestLog)) { _logOpen = !_logOpen; _dirty = true; }
            // Esc schließt über den Fensterstapel (UI-204: erst das oberste Fenster, dann das Ziel, dann das Spielmenü)

            // Gespraech wechselt -> Hauptansicht
            string npcId = ready && _svc.DialogNpc != null ? _svc.DialogNpc.Id : null;
            if (npcId != _dlgNpcId)
            {
                _dlgNpcId = npcId;
                _mode = DialogMode.Main;
                _modeQuest = null;
                _vendorOpen = false;
                _dirty = true;
                if (npcId != null) InventoryUi.RequestBag = _svc.DialogNpc.vendor;
            }

            SetActive(_dlgWin, npcId != null);
            SetActive(_vendWin, npcId != null && _vendorOpen);
            HandelOffen = npcId != null && _vendorOpen;   // 21.10. (FORTSCHRITT-01): vor dem frühen return unten
            SetActive(_logWin, ready && _logOpen);
            SetActive(_xpBar.Root, ready);
            SetActive(_money.rectTransform, ready && inv != null && inv.Ready);
            SetActive(_tracker.rectTransform, ready);
            SetActive(_trackerBack, ready && !string.IsNullOrEmpty(_tracker.text));
            TrackerPlatz();

            if (ready && _dirty) Refresh();
            _dirty = false;

            UpdateToasts(Time.unscaledDeltaTime);

            _markerTimer -= Time.unscaledDeltaTime;
            if (_markerTimer <= 0f)
            {
                _markerTimer = 0.25f;
                UpdateMarkers(ready ? _svc : null, inv != null ? inv.Class : null);
            }

            Vector2 mouse = RawInput.MousePosition;
            bool over = Over(_dlgWin, mouse) || Over(_vendWin, mouse) || Over(_logWin, mouse);
            if (over) UiPointer.OverUi = true;
            if (!ready || UiPointer.ModalOpen)   // Spielmenü offen (UI-204): keine Klicks unter dem Menü
            {
                SetActive(_tip, false);
                return;
            }
            HandleMouse(mouse);
            UpdateHover(mouse);
        }

        private static void SetActive(RectTransform rt, bool on)
        {
            if (rt != null && rt.gameObject.activeSelf != on) rt.gameObject.SetActive(on);
        }

        private static bool Over(RectTransform rt, Vector2 screen) =>
            rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);

        private void UpdateToasts(float dt)
        {
            if (_toastTimer > 0f) _toastTimer -= dt;
            if (_toastTimer <= 0f && _toastQueue.Count > 0)
            {
                var sb = new StringBuilder();
                while (_toastQueue.Count > 0) { if (sb.Length > 0) sb.Append('\n'); sb.Append(_toastQueue.Dequeue()); }
                _toast.text = sb.ToString();
                _toastTimer = 3f;
            }
            _toast.alpha = Mathf.Clamp01(_toastTimer / 0.6f);
            if (_bigToastTimer > 0f) _bigToastTimer -= dt;
            _bigToast.alpha = Mathf.Clamp01(_bigToastTimer / 0.8f);
        }

        private static void UpdateMarkers(IProgressionService p, CharacterClassDefinition cls)
        {
            foreach (NpcIdentity n in NpcIdentity.All)
            {
                if (n == null) continue;
                WorldLabel label = n.GetComponentInChildren<WorldLabel>();
                if (label == null) continue;
                QuestMarkerState s = QuestRules.MarkerFor(n.NpcId, p, cls);
                label.SetMarker(s == QuestMarkerState.Available ? WorldLabelMarker.Available
                    : s == QuestMarkerState.Complete ? WorldLabelMarker.Complete
                    : s == QuestMarkerState.InProgress ? WorldLabelMarker.InProgress : WorldLabelMarker.None);
            }
        }

        // ================================================================== Anzeige

        private void Refresh()
        {
            // Erfahrung
            int need = _svc.XpNeeded;
            _xpBar.Set(need > 0 ? (float)_svc.Xp / need : 1f);
            int rested = ProgressionClient.RestedXp;
            string rest = rested > 0 ? $"   <color=#7fb4ff>ausgeruht +{rested}</color>" : (ProgressionClient.InRestArea ? "   <color=#7fb4ff>ruht</color>" : "");
            _xpBar.Label.text = need > 0 ? $"Stufe {_svc.Level}   {_svc.Xp} / {need} Erfahrung{rest}" : $"Stufe {_svc.Level} (Höchststufe)";
            _money.text = ItemText.Money((int)Mathf.Min(_svc.Money, int.MaxValue));

            RefreshTracker();
            if (_dlgNpcId != null) RefreshDialog();
            if (_dlgNpcId != null && _vendorOpen) RefreshVendor();
            if (_logOpen) RefreshLog();
        }

        private void RefreshTracker()
        {
            // 21.10.: Höhe begrenzt (F2) – Aufträge einzeln anhängen, bis zur Oberkante der Knöpfe unten rechts
            // ("Einstellungen (O)" ab 172 über dem Rand, Canvas immer 1080 hoch); Rest als "… und X weitere (L)"
            float maxH = 1080f - _trackerTop - 16f - 180f;
            int total = 0, shown = 0;
            foreach (QuestView v in _svc.Quests) if (v.Def != null) total++;
            var sb = new StringBuilder();
            var one = new StringBuilder();
            foreach (QuestView v in _svc.Quests)
            {
                if (v.Def == null) continue;
                one.Clear();
                if (sb.Length > 0) one.Append("\n\n");
                one.Append("<color=#f0c850>").Append(v.Def.Title).Append("</color>");
                if (v.Complete)
                {
                    NpcDefinition to = GameData.Npc(v.Def.TurnInNpc);
                    one.Append("\n<color=#8fd07a>Abgeben bei ").Append(to != null ? to.Name : "?").Append("</color>");
                }
                else one.Append(ObjectiveLines(v, "\n<size=90%>", "</size>"));
                string probe = sb.ToString() + one.ToString() + (shown + 1 < total ? TrackerMore(total - shown - 1) : "");
                if (shown > 0 && _tracker.GetPreferredValues(probe, 360f, 0f).y > maxH) break;
                sb.Append(one);
                shown++;
            }
            if (shown < total) sb.Append(TrackerMore(total - shown));
            _tracker.text = sb.ToString();
            float h = sb.Length > 0 ? _tracker.GetPreferredValues(_tracker.text, 360f, 0f).y : 0f;   // zuletzt mit dem echten Text messen
            _trackerBack.sizeDelta = new Vector2(380f, Mathf.Min(h, maxH) + 16f);
        }

        private static string TrackerMore(int count) => $"\n\n<color=#a8a296>… und {count} weitere (L)</color>";

        private float _trackerTop = 142f;

        /// <summary>Karten r2 (08.10.): oben rechts sitzt die Minimap – die Auftragsliste beginnt darunter (<see cref="HudPlatz"/>).</summary>
        private void TrackerPlatz()
        {
            float top = Mathf.Max(142f, HudPlatz.ObenRechts + 16f);
            if (Mathf.Approximately(top, _trackerTop)) return;
            _trackerTop = top;
            _dirty = true;   // 21.10.: verfügbare Höhe hängt am Startpunkt (F2)
            _trackerBack.anchoredPosition = new Vector2(-14f, -top);
            _tracker.rectTransform.anchoredPosition = new Vector2(-24f, -top - 8f);
        }

        private static string ObjectiveLines(QuestView v, string prefix, string suffix)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < v.Def.objectives.Count; i++)
            {
                QuestObjective o = v.Def.objectives[i];
                int have = i < v.Progress.Length ? v.Progress[i] : 0;
                bool done = have >= o.count;
                sb.Append(prefix).Append(done ? "<color=#8a8a80>" : "<color=#e8e2d4>").Append("- ").Append(o.Label);
                if (o.count > 1 || o.type == QuestObjectiveType.Collect || o.type == QuestObjectiveType.Kill)
                    sb.Append(": ").Append(Mathf.Min(have, o.count)).Append('/').Append(o.count);
                if (done) sb.Append(" (erledigt)");
                sb.Append("</color>").Append(suffix);
            }
            return sb.ToString();
        }

        private QuestView ActiveView(string questId)
        {
            foreach (QuestView v in _svc.Quests) if (v.Def != null && v.Def.Id == questId) return v;
            return null;
        }

        /// <summary>Gruss – beim Pferdezuechter je nach eigenem Geschichtsstand (lokal, Phasing).</summary>
        private static string GreetingOf(NpcDefinition npc)
        {
            if (npc == null) return "";
            if (npc.Id == HorseStoryCatalog.BreederNpcId && StoryClient.Ready)
            {
                string key = npc.textKey + (StoryClient.Farm >= FarmPhase.Aged ? ".greeting_aged" : StoryClient.Farm == FarmPhase.Growing ? ".greeting_after" : ".greeting");
                if (Loc.Has(key)) return Loc.T(key);
            }
            // P3 Glockenrast: Gruss nach Geschichtsmarken (Anselm betet laut, Runa haelt den Schluessel …)
            return StoryClient.Ready ? npc.GreetingFor(StoryClient.HasFlag) : npc.Greeting;
        }

        private void RefreshDialog()
        {
            NpcDefinition npc = _svc.DialogNpc;
            // 21.10.: Abgabe-Ansicht erst verlassen, wenn der Server den Auftrag wirklich abgeschlossen hat (B2) –
            // bei Ablehnung (z. B. Rucksack voll) bleibt sie samt gewählter Belohnung stehen, die rote Zeile nennt den Grund
            if (_mode == DialogMode.TurnIn && _modeQuest != null && ActiveView(_modeQuest.Id) == null)
            {
                _mode = DialogMode.Main;
                _modeQuest = null;
            }
            // 21.10.: ebenso das Angebot erst verlassen, wenn der Auftrag beim Server läuft – bei Ablehnung
            // (z. B. "Zu viele Aufträge.", "Kein Platz im Inventar.") bleibt es mit Knopf "Annehmen" stehen
            if (_mode == DialogMode.Offer && _modeQuest != null && ActiveView(_modeQuest.Id) != null)
            {
                _mode = DialogMode.Main;
                _modeQuest = null;
            }
            _dlgTitle.text = npc.Name + (string.IsNullOrEmpty(npc.Title) ? "" : $"  <size=75%><color=#a8a296><noparse><</noparse>{npc.Title}<noparse>></noparse></color></size>");
            foreach (Row r in _dlgRows) SetActive(r.Rt, false);
            foreach (Row r in _rewardRows) SetActive(r.Rt, false);
            SetActive(_dlgAccept.Rt, false);
            SetActive(_dlgTurnIn.Rt, false);
            SetActive(_dlgBack.Rt, _mode != DialogMode.Main);
            SetActive(_dlgTrade.Rt, _mode == DialogMode.Main && npc.vendor);
            SetActive(_dlgTrain.Rt, _mode == DialogMode.Main && npc.IsTrainer);
            _dlgTrain.Rt.anchoredPosition = new Vector2(npc.vendor ? -152f : -14f, 12f);

            if (_mode == DialogMode.Main)
            {
                _dlgBody.text = GreetingOf(npc);
                _listed.Clear();
                // Abgeben (fertig) zuerst, dann laufende, dann neue
                foreach (QuestView v in _svc.Quests)
                    if (v.Def != null && v.Def.TurnInNpc == npc.Id && (v.Complete || QuestRules.TalkToHere(v, npc.Id))) _listed.Add(v.Def);
                foreach (QuestView v in _svc.Quests)
                    if (v.Def != null && (v.Def.TurnInNpc == npc.Id || v.Def.giverNpc == npc.Id) && !_listed.Contains(v.Def)) _listed.Add(v.Def);
                foreach (QuestDefinition q in GameData.Quests)
                    if (QuestRules.Offered(q, npc.Id, _svc.Level, _svc.IsCompleted, _svc.IsActive, QuestGate.Local(InventoryClient.Current != null ? InventoryClient.Current.Class : null)) && !_listed.Contains(q)) _listed.Add(q);
                for (int i = 0; i < _listed.Count && i < _dlgRows.Count; i++)
                {
                    QuestDefinition q = _listed[i];
                    QuestView v = ActiveView(q.Id);
                    string mark = v == null ? "<color=#f0c850>!</color>"
                        : (v.Complete || QuestRules.TalkToHere(v, npc.Id)) && q.TurnInNpc == npc.Id ? "<color=#f0c850>?</color>" : "<color=#9a978f>?</color>";
                    Row r = _dlgRows[i];
                    r.Index = i;
                    r.Text.text = $"<b>{mark}</b>  {q.Title}";
                    r.Right.text = v == null ? $"<color=#a8a296>Stufe {q.minLevel}</color>" : "";
                    SetActive(r.Rt, true);
                }
                // 21.10.: nicht bei einem laufenden Gesprächsziel mit genau diesem NPC (U2 Oswald, G10 Anselm) – das Gespräch IST hier die Aufgabe (Nr. 5)
                if (_listed.Count == 0 && !npc.vendor && !TalkTargetHere(npc.Id))
                    _dlgBody.text = GreetingOf(npc) + "\n\n<color=#a8a296>Gerade gibt es hier nichts zu tun.</color>";
                LayoutRows(_dlgRows, 36f, Mathf.Min(_listed.Count, _dlgRows.Count));
                return;
            }

            QuestDefinition qd = _modeQuest;
            QuestView view = ActiveView(qd.Id);
            var sb = new StringBuilder();
            sb.Append("<b><color=#f0c850>").Append(qd.Title).Append("</color></b>\n\n");
            if (_mode == DialogMode.Offer)
            {
                sb.Append(qd.OfferText).Append("\n\n<color=#a8a296>Ziele</color>");
                foreach (QuestObjective o in qd.objectives)
                    sb.Append("\n- ").Append(o.Label).Append(o.count > 1 ? $": {o.count}" : "");
                SetActive(_dlgAccept.Rt, view == null);
            }
            else if (_mode == DialogMode.TurnIn)
            {
                sb.Append(qd.CompleteText);
                SetActive(_dlgTurnIn.Rt, true);
            }
            else
            {
                sb.Append(string.IsNullOrEmpty(qd.ProgressText) ? qd.Summary : qd.ProgressText);
                if (view != null) sb.Append(ObjectiveLines(view, "\n", ""));
            }
            _dlgBody.text = sb.ToString();
            if (_mode != DialogMode.Progress) ShowRewards(qd);
        }

        /// <summary>Listen direkt unter den Text setzen (statt fester Hoehe).</summary>
        private void LayoutRows(List<Row> rows, float rowStep, int shown = 0)
        {
            float h = _dlgBody.GetPreferredValues(_dlgBody.text, 428f, 0f).y;
            float y = -44f - Mathf.Min(h, 420f) - 14f;
            // 21.10.: bei langem Gruss rücken die gezeigten Zeilen hoch, die unterste bleibt über den Knöpfen (Oberkante -554) (Nr. 6)
            if (shown > 0) y = Mathf.Max(y, -546f + (shown - 1) * rowStep + rows[0].Rt.sizeDelta.y);
            for (int i = 0; i < rows.Count; i++) rows[i].Rt.anchoredPosition = new Vector2(16f, y - i * rowStep);
        }

        /// <summary>21.10.: Läuft ein Auftrag mit einem Gesprächsziel bei diesem NPC (offen oder gerade erfüllt)? (Nr. 5)</summary>
        private bool TalkTargetHere(string npcId)
        {
            foreach (QuestView v in _svc.Quests)
            {
                if (v.Def == null) continue;
                foreach (QuestObjective o in v.Def.objectives)
                    if (o.type == QuestObjectiveType.TalkTo && o.targetId == npcId) return true;
            }
            return false;
        }

        private void ShowRewards(QuestDefinition q)
        {
            CharacterClassDefinition cls = InventoryClient.Current != null ? InventoryClient.Current.Class : null;
            List<QuestItemReward> fixedR = QuestRules.ForClass(q.rewardItems, cls);
            List<QuestItemReward> choices = QuestRules.ForClass(q.choiceItems, cls);
            _shownRewards.Clear();
            _shownRewards.AddRange(fixedR);
            _choiceStart = _shownRewards.Count;
            _shownRewards.AddRange(choices);
            if (choices.Count == 1) _choice = 0;
            var head = new StringBuilder("\n\n<color=#a8a296>Belohnung:</color> ");
            head.Append(q.xp).Append(" Erfahrung");
            if (q.money > 0) head.Append(" · ").Append(ItemText.Money(q.money));
            if (choices.Count > 1) head.Append("\n<color=#a8a296>Wähle eins der unteren Teile:</color>");
            _dlgBody.text += head.ToString();
            for (int i = 0; i < _shownRewards.Count && i < _rewardRows.Count; i++)
            {
                QuestItemReward r = _shownRewards[i];
                Row row = _rewardRows[i];
                row.Index = i;
                row.Item = r.item;
                row.Stack = new ItemStack(r.item.Id, r.count);
                row.Icon.sprite = ItemIcons.Get(r.item);
                row.Frame.color = ItemQualityColors.Get(r.item.quality);
                row.Text.text = $"<color={ItemQualityColors.Hex(r.item.quality)}>{r.item.DisplayName}</color>" + (r.count > 1 ? $" ×{r.count}" : "");
                bool choice = i >= _choiceStart;
                row.Right.text = choice ? (_choice == i - _choiceStart ? "<color=#f0c850>gewählt</color>" : "<color=#a8a296>zur Wahl</color>") : "";
                row.Back.color = choice && _choice == i - _choiceStart ? RowSelected : RowColor;
                SetActive(row.Rt, true);
            }
            LayoutRows(_rewardRows, 40f);
        }

        private void RefreshVendor()
        {
            NpcDefinition npc = _svc.DialogNpc;
            _vendTitle.text = npc.Name;
            for (int i = 0; i < _stockRows.Count; i++)
            {
                Row r = _stockRows[i];
                VendorEntry e = i < npc.stock.Count ? npc.stock[i] : null;
                if (e == null || e.item == null) { SetActive(r.Rt, false); continue; }
                r.Index = i;
                r.Item = e.item;
                r.Stack = new ItemStack(e.item.Id, e.count);
                r.Icon.sprite = ItemIcons.Get(e.item);
                r.Frame.color = ItemQualityColors.Get(e.item.quality);
                r.Text.text = $"<color={ItemQualityColors.Hex(e.item.quality)}>{e.item.DisplayName}</color>" + (e.count > 1 ? $" ×{e.count}" : "");
                int price = Progression.BuyPrice(e.item, e.price, e.count);   // 21.10. (FORTSCHRITT-02): wie der Server
                r.Right.text = (price > _svc.Money ? "<alpha=#88>" : "") + ItemText.Money(price);
                SetActive(r.Rt, true);
            }
            for (int i = 0; i < _buybackRows.Count; i++)
            {
                Row r = _buybackRows[i];
                if (i >= _svc.Buyback.Count) { SetActive(r.Rt, false); continue; }
                BuybackEntry b = _svc.Buyback[i];
                ItemDefinition d = GameData.Item(b.Stack.id);
                if (d == null) { SetActive(r.Rt, false); continue; }
                r.Index = i;
                r.Item = d;
                r.Stack = b.Stack;
                r.Icon.sprite = ItemIcons.Get(d);
                r.Frame.color = ItemQualityColors.Get(d.quality);
                r.Text.text = $"<color={ItemQualityColors.Hex(d.quality)}>{d.DisplayName}</color>" + (b.Stack.count > 1 ? $" ×{b.Stack.count}" : "");
                r.Right.text = ItemText.Money(b.Price);
                SetActive(r.Rt, true);
            }
            _vendMoney.text = ItemText.Money((int)Mathf.Min(_svc.Money, int.MaxValue));
        }

        private void RefreshLog()
        {
            IReadOnlyList<QuestView> qs = _svc.Quests;
            int pageCount = Mathf.Max(1, (qs.Count + LogPageSize - 1) / LogPageSize);
            if (_logSelected != null && ActiveView(_logSelected) == null) _logSelected = null;
            // 21.10.: die Seite folgt dem gewählten Auftrag – rückt er durch Abgabe/Aufgeben weiter vorn auf eine andere Seite,
            // stünde er sonst nur noch rechts im Text, links aber nicht markiert (F3, Blättern von Codex)
            for (int i = 0; _logSelected != null && i < qs.Count; i++)
                if (qs[i].Def != null && qs[i].Def.Id == _logSelected) { _logPage = i / LogPageSize; break; }
            _logPage = Mathf.Clamp(_logPage, 0, pageCount - 1);
            int first = _logPage * LogPageSize;
            SetActive(_logPrevious.Rt, _logPage > 0);
            SetActive(_logNext.Rt, _logPage + 1 < pageCount);
            SetActive(_logPageLabel.rectTransform, pageCount > 1);
            _logPageLabel.text = $"Seite {_logPage + 1} / {pageCount}";
            SetActive(_logEmpty.rectTransform, qs.Count == 0);
            if (_logSelected == null && qs.Count > first && qs[first].Def != null) _logSelected = qs[first].Def.Id;
            for (int i = 0; i < _logRows.Count; i++)
            {
                Row r = _logRows[i];
                int index = first + i;
                if (index >= qs.Count || qs[index].Def == null) { SetActive(r.Rt, false); continue; }
                r.Index = index;
                r.Text.text = qs[index].Complete ? $"{qs[index].Def.Title} <color=#8fd07a>(fertig)</color>" : qs[index].Def.Title;
                r.Back.color = qs[index].Def.Id == _logSelected ? RowSelected : RowColor;
                SetActive(r.Rt, true);
            }
            QuestView v = _logSelected != null ? ActiveView(_logSelected) : null;
            SetActive(_logAbandon.Rt, v != null);
            if (v == null)
            {
                _logDetail.text = "";
                return;
            }
            var sb = new StringBuilder();
            sb.Append("<b><color=#f0c850>").Append(v.Def.Title).Append("</color></b>\n");
            sb.Append(v.Def.Summary).Append('\n');
            sb.Append(ObjectiveLines(v, "\n", ""));
            NpcDefinition to = GameData.Npc(v.Def.TurnInNpc);
            sb.Append("\n\n<color=#a8a296>Abgabe:</color> ").Append(to != null ? to.Name : "?");
            sb.Append("\n<color=#a8a296>Belohnung:</color> ").Append(v.Def.xp).Append(" Erfahrung");
            if (v.Def.money > 0) sb.Append(" · ").Append(ItemText.Money(v.Def.money));
            sb.Append("\n\n<size=90%><color=#c8c0b0>").Append(v.Def.OfferText).Append("</color></size>");
            _logDetail.text = sb.ToString();
        }

        // ================================================================== Maus

        private static bool Clicked(Btn b, Vector2 mouse) => b != null && b.Rt.gameObject.activeInHierarchy && Over(b.Rt, mouse);

        private static Row RowAt(List<Row> rows, Vector2 mouse)
        {
            foreach (Row r in rows) if (r.Rt.gameObject.activeInHierarchy && Over(r.Rt, mouse)) return r;
            return null;
        }

        private void HandleMouse(Vector2 mouse)
        {
            foreach (Btn b in new[] { _dlgClose, _dlgAccept, _dlgTurnIn, _dlgBack, _dlgTrade, _dlgTrain, _vendClose, _logClose, _logAbandon, _logPrevious, _logNext })
                if (b.Rt.gameObject.activeInHierarchy) b.Back.color = Over(b.Rt, mouse) ? ButtonHover : ButtonColor;
            foreach (List<Row> list in new[] { _dlgRows, _stockRows, _buybackRows })
                foreach (Row r in list)
                    if (r.Rt.gameObject.activeInHierarchy) r.Back.color = Over(r.Rt, mouse) ? RowHover : RowColor;

            // 21.10.: Mausrad über dem Auftragsbuch blättert wie < > (F3); die Kamera zoomt dort nicht (UiPointer.OverUi)
            if (Over(_logWin, mouse))
            {
                int pages = Mathf.Max(1, (_svc.Quests.Count + LogPageSize - 1) / LogPageSize);
                int page = Mathf.Clamp(_logPage - (int)RawInput.ScrollSteps, 0, pages - 1);
                if (page != _logPage) { _logPage = page; _logSelected = null; _dirty = true; }
            }

            if (!RawInput.LeftMousePressed) return;

            if (Clicked(_dlgClose, mouse)) { _svc.CloseDialog(); return; }
            if (Clicked(_vendClose, mouse)) { _vendorOpen = false; _dirty = true; return; }
            if (Clicked(_logClose, mouse)) { _logOpen = false; return; }
            if (Clicked(_logPrevious, mouse) || Clicked(_logNext, mouse))
            {
                _logPage += Clicked(_logNext, mouse) ? 1 : -1;
                _logSelected = null;
                _dirty = true;
                return;
            }
            if (Clicked(_dlgBack, mouse)) { _mode = DialogMode.Main; _modeQuest = null; _dirty = true; return; }
            if (Clicked(_dlgTrade, mouse)) { _vendorOpen = !_vendorOpen; InventoryUi.RequestBag = true; _dirty = true; return; }
            if (Clicked(_dlgTrain, mouse)) { if (TrainerUi.Instance != null) TrainerUi.Instance.Toggle(_dlgNpcId); return; }
            if (Clicked(_dlgAccept, mouse) && _modeQuest != null)
            {
                // 21.10.: Ansicht bleibt bis zur Serverantwort (RefreshDialog wechselt zur Liste, sobald der Auftrag läuft) –
                // gleiche 0,5-s-Sperre wie beim Abgeben, sonst meldet die zweite Bitte rot, dass der Auftrag schon läuft
                if (Time.unscaledTime < _acceptWait) return;
                _acceptWait = Time.unscaledTime + 0.5f;
                _svc.Accept(_modeQuest.Id);
                _dirty = true;
                return;
            }
            if (Clicked(_dlgTurnIn, mouse) && _modeQuest != null)
            {
                int choices = _shownRewards.Count - _choiceStart;
                if (choices > 1 && _choice < 0)
                {
                    Ashveil.Core.Events.CombatTextEvent.Error("Bitte eine Belohnung wählen.");
                    return;
                }
                // 21.10.: Ansicht bleibt bis zur Serverantwort (RefreshDialog wechselt zur Liste, B2); kurze Sperre gegen Doppelklick,
                // sonst meldet die zweite Bitte nach erfolgreicher Abgabe rot "Auftrag läuft nicht."
                if (Time.unscaledTime < _turnInWait) return;
                _turnInWait = Time.unscaledTime + 0.5f;
                _svc.TurnIn(_modeQuest.Id, choices > 0 ? Mathf.Max(0, _choice) : -1);
                _dirty = true;
                return;
            }
            if (Clicked(_logAbandon, mouse) && _logSelected != null)
            {
                _svc.Abandon(_logSelected);
                _logSelected = null;
                _dirty = true;
                return;
            }

            Row row = RowAt(_dlgRows, mouse);
            if (row != null && _mode == DialogMode.Main && row.Index < _listed.Count)
            {
                QuestDefinition q = _listed[row.Index];
                QuestView v = ActiveView(q.Id);
                _modeQuest = q;
                _choice = -1;
                if (v == null) _mode = DialogMode.Offer;
                else if ((v.Complete || QuestRules.TalkToHere(v, _dlgNpcId)) && q.TurnInNpc == _dlgNpcId) _mode = DialogMode.TurnIn;
                else _mode = DialogMode.Progress;
                _dirty = true;
                return;
            }
            row = RowAt(_rewardRows, mouse);
            if (row != null && _mode == DialogMode.TurnIn && row.Index >= _choiceStart)
            {
                _choice = row.Index - _choiceStart;
                _dirty = true;
                return;
            }
            row = RowAt(_stockRows, mouse);
            if (row != null) { _svc.Buy(row.Index, RawInput.ShiftHeld ? 5 : 1); return; }
            row = RowAt(_buybackRows, mouse);
            if (row != null) { _svc.BuyBack(row.Index); return; }
            row = RowAt(_logRows, mouse);
            if (row != null && row.Index < _svc.Quests.Count && _svc.Quests[row.Index].Def != null)
            {
                _logSelected = _svc.Quests[row.Index].Def.Id;
                _dirty = true;
            }
        }

        private void UpdateHover(Vector2 mouse)
        {
            Row r = RowAt(_rewardRows, mouse) ?? RowAt(_stockRows, mouse) ?? RowAt(_buybackRows, mouse);
            if (r == null || r.Item == null)
            {
                SetActive(_tip, false);
                return;
            }
            _tipText.text = ItemText.Tooltip(r.Item, r.Stack, InventoryClient.Current, false, null);
            Vector2 size = _tipText.GetPreferredValues(_tipText.text, 316f, 0f);
            _tip.sizeDelta = new Vector2(340f, size.y + 22f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, mouse, null, out Vector2 local);
            Vector2 half = _canvasRt.rect.size * 0.5f;
            Vector2 pos = local + new Vector2(22f, -22f);
            if (pos.x + 340f > half.x - 8f) pos.x = local.x - 362f;
            pos.y = Mathf.Clamp(pos.y, -half.y + _tip.sizeDelta.y + 8f, half.y - 8f);
            _tip.anchoredPosition = pos;
            SetActive(_tip, true);
        }
    }
}
