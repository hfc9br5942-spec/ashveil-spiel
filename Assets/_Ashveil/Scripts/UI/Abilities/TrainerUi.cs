using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Abilities;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Quests;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// Klassenlehrer (P1, WoW Classic): listet je Faehigkeit der eigenen Klasse den naechsten Rang – gruen = jetzt lernbar,
    /// rot = erst ab Stufe X, grau = alles gelernt. Auswaehlen, „Erlernen“ (Geld), dazu „Talente zuruecksetzen“.
    /// Oeffnet sich ueber den Knopf „Ausbildung“ im Gespraech; der Server prueft alles.
    /// </summary>
    [DefaultExecutionOrder(-460)]
    public sealed class TrainerUi : MonoBehaviour
    {
        private sealed class Row
        {
            public RectTransform Rt;
            public Image Back, Icon;
            public TextMeshProUGUI Name, Right;
            public SpellDefinition Spell;
            public int Rank;
        }

        public static TrainerUi Instance { get; private set; }
        public bool IsOpen => _npcId != null;
        public string NpcId => _npcId;

        private const float W = 440f, RowH = 42f;
        private static readonly Color RowColor = new Color(0f, 0f, 0f, 0.25f);
        private static readonly Color Selected = new Color(0.55f, 0.42f, 0.2f, 0.45f);

        private RectTransform _win, _close, _learn, _reset;
        private TextMeshProUGUI _title, _detail, _money, _info, _learnLabel, _resetLabel;
        private readonly List<Row> _rows = new List<Row>();
        private string _npcId;
        private SpellDefinition _sel;
        private int _selRank;
        private int _shownRev = -1, _shownSpellRev = -1;
        private long _shownMoney = -1;
        private bool _built;

        public void Build(Transform parent)
        {
            Instance = this;
            Canvas canvas = UiKit.CreateCanvas("Lehrer Canvas", parent, 22);
            RectTransform inner = UiKit.Panel("Klassenlehrer", canvas.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(960f, 30f), new Vector2(W, 640f));
            _win = (RectTransform)inner.parent;
            _title = Label(_win, "Titel", 20f, new Vector2(14f, -10f), new Vector2(W - 60f, 28f));
            _close = Button(_win, "×", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(26f, 26f), out _);
            _info = Label(_win, "Info", 13f, new Vector2(14f, -40f), new Vector2(W - 28f, 20f));
            _info.color = UiKit.MutedText;
            for (int i = 0; i < 10; i++)
            {
                RectTransform rt = UiKit.CreateRect("Zeile", _win, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -66f - i * RowH), new Vector2(W - 28f, RowH - 4f));
                Image back = UiKit.AddImage(rt, RowColor);
                RectTransform fr = UiKit.CreateRect("Rahmen", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(RowH - 8f, RowH - 8f));
                UiKit.AddImage(fr, UiKit.BorderColor);
                Image icon = UiKit.AddImage(UiKit.Stretch("Bild", fr, 2f), Color.white);
                icon.preserveAspect = true;
                TextMeshProUGUI n = UiKit.CreateText("Name", rt, "", 15f, UiKit.TextColor, TextAlignmentOptions.Left);
                n.richText = true;
                Stretch(n.rectTransform, RowH, 8f);
                TextMeshProUGUI r = UiKit.CreateText("Rechts", rt, "", 13f, UiKit.TextColor, TextAlignmentOptions.Right);
                r.richText = true;
                Stretch(r.rectTransform, RowH, 8f);
                rt.gameObject.SetActive(false);
                _rows.Add(new Row { Rt = rt, Back = back, Icon = icon, Name = n, Right = r });
            }
            _detail = Label(_win, "Details", 13.5f, new Vector2(14f, -66f - 10 * RowH - 6f), new Vector2(W - 28f, 110f));
            _detail.textWrappingMode = TextWrappingModes.Normal;
            _learn = Button(_win, "Erlernen", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 12f), new Vector2(150f, 34f), out _learnLabel);
            _reset = Button(_win, "Talente zurücksetzen", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 12f), new Vector2(200f, 34f), out _resetLabel);
            _resetLabel.fontSize = 13f;
            _money = UiKit.CreateText("Geld", _win, "", 15f, UiKit.TextColor, TextAlignmentOptions.Right);
            _money.richText = true;
            RectTransform mr = _money.rectTransform;
            mr.anchorMin = mr.anchorMax = new Vector2(1f, 0f);
            mr.pivot = new Vector2(1f, 0f);
            mr.anchoredPosition = new Vector2(-14f, 52f);
            mr.sizeDelta = new Vector2(260f, 22f);
            _win.gameObject.SetActive(false);
            UiFensterstapel.Anmelden(this, "Lehrer", () => _npcId != null, () => _npcId = null);
            _built = true;
        }

        /// <summary>Knopf „Ausbildung“ im Gespraech: oeffnen bzw. schliessen.</summary>
        public void Toggle(string npcId)
        {
            _npcId = _npcId == npcId ? null : npcId;
            _sel = null;
            _shownRev = -1;
        }

        public void Close() => _npcId = null;

        // ================================================================== Takt

        private void Update()
        {
            if (!_built) return;
            IProgressionService p = ProgressionClient.Current;
            if (_npcId != null && (p == null || p.DialogNpc == null || p.DialogNpc.Id != _npcId)) _npcId = null;
            bool open = _npcId != null;
            if (_win.gameObject.activeSelf != open) _win.gameObject.SetActive(open);
            if (!open) return;
            if (_shownSpellRev != SpellClient.Revision || _shownMoney != p.Money || _shownRev != p.Level) Refresh(p);

            Vector2 mouse = RawInput.MousePosition;
            if (Over(_win, mouse)) UiPointer.OverUi = true;
            if (!RawInput.LeftMousePressed) return;
            if (Over(_close, mouse)) { _npcId = null; return; }
            if (Over(_learn, mouse) && _sel != null) { SpellClient.TrainRequest?.Invoke(_npcId, _sel.Id, _selRank); return; }
            if (Over(_reset, mouse)) { SpellClient.TalentResetRequest?.Invoke(_npcId); return; }
            foreach (Row r in _rows)
                if (r.Rt.gameObject.activeSelf && Over(r.Rt, mouse)) { _sel = r.Spell; _selRank = r.Rank; _shownRev = -1; Refresh(p); return; }
        }

        private void Refresh(IProgressionService p)
        {
            _shownSpellRev = SpellClient.Revision;
            _shownMoney = p.Money;
            _shownRev = p.Level;
            NpcDefinition npc = p.DialogNpc;
            CharacterClassDefinition cls = InventoryClient.Current != null ? InventoryClient.Current.Class : null;
            _title.text = "Ausbildung – " + (npc != null ? npc.Name : "");
            _money.text = ItemText.Money((int)Mathf.Min(p.Money, int.MaxValue));
            bool mine = npc != null && cls != null && npc.trainerClass == cls.Id;
            CharacterClassDefinition teaches = npc != null ? GameData.Class(npc.trainerClass) : null;
            if (!mine)
            {
                _info.text = $"{(npc != null ? npc.Name : "")} bildet nur {(teaches != null ? teaches.DisplayName : "eine andere Klasse")} aus.";
                foreach (Row r in _rows) r.Rt.gameObject.SetActive(false);
                _detail.text = "";
                _learn.gameObject.SetActive(false);
                _reset.gameObject.SetActive(false);
                return;
            }
            _info.text = $"{cls.DisplayName} · Stufe {p.Level} · Neue Ränge gibt es alle paar Stufen.";

            var list = new List<(SpellDefinition s, int rank, SpellRank r)>();
            var done = new List<SpellDefinition>();
            foreach (GameDefinition g in GameData.Database.All)
            {
                if (!(g is SpellDefinition s) || s.classId != cls.Id || s.RankCount == 0) continue;
                int known = SpellClient.RankOf(s.Id);
                SpellRank next = s.Rank(known + 1);
                if (next != null) list.Add((s, known + 1, next));
                else done.Add(s);
            }
            list.Sort((a, b) => a.r.level != b.r.level ? a.r.level.CompareTo(b.r.level) : a.s.sortOrder.CompareTo(b.s.sortOrder));
            int i = 0;
            foreach (var e in list)
            {
                if (i >= _rows.Count) break;
                Row row = _rows[i++];
                row.Spell = e.s;
                row.Rank = e.rank;
                bool canLevel = p.Level >= e.r.level;
                bool canPay = p.Money >= e.r.trainCost;
                string col = canLevel ? (canPay ? "#7fd36a" : "#e0c060") : "#d0604a";
                row.Name.text = $"<color={col}>{e.s.DisplayName}</color>" + (e.s.RankCount > 1 ? $" <size=85%><color=#b8b09c>Rang {e.rank}</color></size>" : "");
                row.Right.text = (canLevel ? "" : $"<color=#d0604a>ab Stufe {e.r.level}</color>   ") + (e.r.trainCost > 0 ? ItemText.Money(e.r.trainCost) : "kostenlos");
                row.Icon.sprite = ActionBarUi.SpellIcon(e.s);
                row.Icon.color = canLevel ? Color.white : new Color(0.55f, 0.55f, 0.55f);
                row.Back.color = _sel == e.s ? Selected : RowColor;
                row.Rt.gameObject.SetActive(true);
            }
            for (; i < _rows.Count; i++) _rows[i].Rt.gameObject.SetActive(false);

            if (_sel != null && (SpellClient.RankOf(_sel.Id) >= _selRank)) _sel = null; // eben gelernt
            if (_sel == null && list.Count > 0) { _sel = list[0].s; _selRank = list[0].rank; foreach (Row r in _rows) if (r.Rt.gameObject.activeSelf) r.Back.color = r.Spell == _sel ? Selected : RowColor; }
            if (_sel != null)
            {
                SpellRank r = _sel.Rank(_selRank);
                _detail.text = $"<b>{_sel.DisplayName}</b>" + (_sel.RankCount > 1 ? $" (Rang {_selRank})" : "") + "\n" + ActionBarUi.SpellTooltip(_sel, _selRank);
                bool ok = r != null && p.Level >= r.level && p.Money >= r.trainCost;
                _learn.gameObject.SetActive(true);
                _learnLabel.color = ok ? UiKit.TextColor : UiKit.MutedText;
            }
            else
            {
                _detail.text = done.Count > 0 ? "<color=#a8a296>Du hast alles gelernt, was es auf deiner Stufe gibt.</color>" : "";
                _learn.gameObject.SetActive(false);
            }
            int spent = SpellClient.TalentPointsSpent;
            _reset.gameObject.SetActive(spent > 0);
            _resetLabel.text = spent > 0 ? $"Talente zurücksetzen" : "";
        }

        // ================================================================== Bausteine

        private static TextMeshProUGUI Label(RectTransform parent, string name, float size, Vector2 pos, Vector2 box)
        {
            TextMeshProUGUI t = UiKit.CreateText(name, parent, "", size, UiKit.TextColor);
            t.richText = true;
            RectTransform rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            return t;
        }

        private static RectTransform Button(RectTransform parent, string text, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, out TextMeshProUGUI label)
        {
            RectTransform rt = UiKit.CreateRect("Knopf " + text, parent, anchor, pivot, pos, size);
            UiKit.AddImage(rt, UiKit.BorderColor);
            UiKit.AddImage(UiKit.Stretch("Inner", rt, 1.5f), new Color(0.22f, 0.17f, 0.11f, 0.95f));
            label = UiKit.CreateText("Label", rt, text, 16f, UiKit.TextColor, TextAlignmentOptions.Center);
            RectTransform lr = label.rectTransform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            return rt;
        }

        private static void Stretch(RectTransform rt, float left, float right)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(-right, 0f);
        }

        private static bool Over(RectTransform rt, Vector2 screen) =>
            rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);

        private void OnDisable() { if (Instance == this) Instance = null; }
    }
}
