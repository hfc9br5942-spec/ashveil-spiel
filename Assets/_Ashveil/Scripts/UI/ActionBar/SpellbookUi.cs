using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Abilities;
using Ashveil.Gameplay.Controls;
using Ashveil.Gameplay.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// Faehigkeitenbuch (K): links die gelernten Klassenfaehigkeiten (P1, mit Rang), rechts die allgemeinen Aktionen
    /// (Angriff, Waffenfaehigkeit, Hechtrolle, Waffe wechseln, Nachladen, Reittier). Ziehen auf einen Platz der Leiste =
    /// belegen; ein Klick auf eine Klassenfaehigkeit wirkt sie (wie in WoW).
    /// </summary>
    [DefaultExecutionOrder(-465)]
    public sealed class SpellbookUi : MonoBehaviour
    {
        private sealed class Row
        {
            public AbilityDefinition Def;
            public SpellDefinition Spell;
            public RectTransform Rt;
            /// <summary>Reittier-Zeile (Def leer): ist die Leiste voll, holt man das Pferd hier heraus (Vorgabe: Leiste nie ueberschreiben).</summary>
            public bool Mount;
            public Image Icon;
        }

        public static SpellbookUi Instance { get; private set; }
        public bool IsOpen => _open;
        /// <summary>Zeilen im Buch: allgemeine Aktionen + Reittier-Zeile + gelernte Klassenfaehigkeiten.</summary>
        public int Count => _rows.Count;
        public int SpellCount { get; private set; }

        private TextMeshProUGUI _none;
        private const float ColW = 400f, RowH = 72f;

        private RectTransform _canvasRt, _win, _close, _content;
        private TextMeshProUGUI _leftTitle, _hint;
        private Image _dragIcon;
        private readonly List<Row> _rows = new List<Row>();
        private bool _open, _built;
        private Row _press, _mountRow;
        private Image _mountIcon;
        private TextMeshProUGUI _mountName;
        private Vector2 _pressPos;
        private bool _dragging;
        private int _shownRevision = -1;
        private string _shownClass = "";

        public void Build(RectTransform canvasRoot)
        {
            Instance = this;
            _canvasRt = canvasRoot;
            RectTransform inner = UiKit.Panel("Faehigkeitenbuch", canvasRoot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 40f), new Vector2(ColW * 2f + 36f, 200f));
            _win = (RectTransform)inner.parent;
            TextMeshProUGUI title = UiKit.CreateText("Titel", _win, "Fähigkeiten (K)", 21f, UiKit.TextColor);
            Place(title.rectTransform, new Vector2(14f, -10f), new Vector2(400f, 28f));
            _close = UiKit.CreateRect("Schliessen", _win, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(26f, 26f));
            UiKit.AddImage(_close, UiKit.BorderColor);
            TextMeshProUGUI x = UiKit.CreateText("X", _close, "×", 18f, UiKit.TextColor, TextAlignmentOptions.Center);
            x.rectTransform.anchorMin = Vector2.zero; x.rectTransform.anchorMax = Vector2.one;
            x.rectTransform.offsetMin = x.rectTransform.offsetMax = Vector2.zero;
            _leftTitle = UiKit.CreateText("Klasse", _win, "", 16f, new Color(0.91f, 0.79f, 0.54f));
            Place(_leftTitle.rectTransform, new Vector2(14f, -42f), new Vector2(ColW, 22f));
            TextMeshProUGUI right = UiKit.CreateText("Allgemein", _win, "Allgemein", 16f, new Color(0.91f, 0.79f, 0.54f));
            Place(right.rectTransform, new Vector2(24f + ColW, -42f), new Vector2(ColW, 22f));
            _hint = UiKit.CreateText("Hinweis", _win, "Auf die Leiste ziehen, um einen Platz zu belegen. Klick auf eine Klassenfähigkeit wirkt sie. Neue Ränge lernst du beim Klassenlehrer.", 13f, UiKit.MutedText);
            _hint.textWrappingMode = TextWrappingModes.Normal;
            _content = UiKit.CreateRect("Inhalt", _win, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);

            RectTransform drag = UiKit.CreateRect("Ziehen", canvasRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(52f, 52f));
            _dragIcon = UiKit.AddImage(drag, Color.white);
            _dragIcon.preserveAspect = true;
            _dragIcon.raycastTarget = false;
            drag.gameObject.SetActive(false);
            Rebuild();
            _win.gameObject.SetActive(false);
            UiFensterstapel.Anmelden(this, "Fähigkeiten", () => _open, () => _open = false);
            _built = true;
        }

        /// <summary>Zeilen neu anlegen (gelernte Faehigkeiten haben sich geaendert).</summary>
        private void Rebuild()
        {
            foreach (Row r in _rows) if (r.Rt != null) Destroy(r.Rt.gameObject);
            _rows.Clear();
            if (_none != null) Destroy(_none.gameObject); // sonst liegt "Noch keine…" vom ersten Aufbau unter der ersten Zeile
            _none = null;
            _shownRevision = SpellClient.Revision;

            var defs = new List<AbilityDefinition>();
            if (GameData.Database != null)
                foreach (GameDefinition d in GameData.Database.All)
                    if (d is AbilityDefinition a && a.showInSpellbook) defs.Add(a);
            defs.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));

            var spells = new List<SpellDefinition>();
            foreach (var kv in SpellClient.Known)
            {
                SpellDefinition s = GameData.Get<SpellDefinition>(kv.Key);
                if (s != null) spells.Add(s);
            }
            spells.Sort((a, b) => a.sortOrder != b.sortOrder ? a.sortOrder.CompareTo(b.sortOrder) : string.CompareOrdinal(a.Id, b.Id));
            SpellCount = spells.Count;
            CharacterClassDefinition cls = InventoryClient.Current != null ? InventoryClient.Current.Class : null;
            _shownClass = cls != null ? cls.Id : "";
            _leftTitle.text = cls != null ? cls.DisplayName : "Klasse";

            float y = -68f;
            foreach (SpellDefinition s in spells)
            {
                int rank = SpellClient.RankOf(s.Id);
                string n = s.DisplayName + (s.RankCount > 1 ? $"  <color=#b8b09c><size=80%>Rang {rank}</size></color>" : "");
                Row r = MakeRow(_content, 12f, y, ActionBarUi.SpellIcon(s), n, ShortText(s, rank));
                r.Spell = s;
                _rows.Add(r);
                y -= RowH;
            }
            if (spells.Count == 0)
            {
                _none = UiKit.CreateText("Keine", _content, "Noch keine Klassenfähigkeiten.", 14f, UiKit.MutedText);
                Place(_none.rectTransform, new Vector2(16f, y - 8f), new Vector2(ColW - 20f, 22f));
            }
            float yr = -68f;
            foreach (AbilityDefinition a in defs)
            {
                Row r = MakeRow(_content, 24f + ColW, yr, a.icon != null ? a.icon : ItemIcons.ForShape(a.iconShape, a.iconColor), a.DisplayName, a.Description);
                r.Def = a;
                _rows.Add(r);
                yr -= RowH;
            }
            // Reittier (nur sichtbar, wenn man eines hat)
            _mountRow = MakeRow(_content, 24f + ColW, yr, ItemIcons.ForShape(IconShape.Boots, new Color(0.45f, 0.3f, 0.18f)), "Reittier",
                "Aufsitzen und absitzen. Auf die Leiste ziehen, wenn dort Platz ist.");
            _mountRow.Mount = true;
            _mountIcon = _mountRow.Icon;
            _mountName = _mountRow.Rt.Find("Name").GetComponent<TextMeshProUGUI>();
            _rows.Add(_mountRow);
            yr -= RowH;

            float bottom = Mathf.Min(y, yr);
            Place(_hint.rectTransform, new Vector2(14f, bottom - 4f), new Vector2(ColW * 2f, 36f));
            _win.sizeDelta = new Vector2(ColW * 2f + 36f, -bottom + 48f);
        }

        private static string ShortText(SpellDefinition s, int rank)
        {
            string d = SpellRules.Describe(s, rank, SpellClient.Bonuses);
            return d.Length > 150 ? d.Substring(0, 147) + " …" : d;
        }

        private Row MakeRow(RectTransform parent, float x, float y, Sprite sprite, string name, string desc)
        {
            RectTransform rt = UiKit.CreateRect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(ColW - 8f, RowH - 6f));
            UiKit.AddImage(rt, new Color(1f, 1f, 1f, 0.04f));
            RectTransform ic = UiKit.CreateRect("Symbol", rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(48f, 48f));
            UiKit.AddImage(ic, UiKit.BorderColor);
            Image icon = UiKit.AddImage(UiKit.Stretch("Bild", ic, 2f), Color.white);
            icon.sprite = sprite;
            icon.preserveAspect = true;
            TextMeshProUGUI n = UiKit.CreateText("Name", rt, name, 17f, UiKit.TextColor);
            n.richText = true;
            n.alignment = TextAlignmentOptions.TopLeft;
            Place(n.rectTransform, new Vector2(60f, -2f), new Vector2(ColW - 76f, 22f));
            TextMeshProUGUI d = UiKit.CreateText("Text", rt, desc ?? "", 12.5f, UiKit.MutedText);
            d.textWrappingMode = TextWrappingModes.Normal;
            d.alignment = TextAlignmentOptions.TopLeft;         // lange Texte wachsen nach unten, nie ueber den Namen
            d.overflowMode = TextOverflowModes.Ellipsis;
            Place(d.rectTransform, new Vector2(60f, -24f), new Vector2(ColW - 76f, 42f));
            return new Row { Rt = rt, Icon = icon };
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
            if (UiPointer.HotkeysAllowed && RawInput.WasPressed(GameKey.Spellbook)) _open = !_open;
            // Esc schließt über den Fensterstapel (UI-204: erst das oberste Fenster, dann das Ziel, dann das Spielmenü)
            CharacterClassDefinition cls = InventoryClient.Current != null ? InventoryClient.Current.Class : null;
            if (_shownRevision != SpellClient.Revision || _shownClass != (cls != null ? cls.Id : "")) Rebuild();
            if (_win.gameObject.activeSelf != _open) _win.gameObject.SetActive(_open);
            if (!_open) { _press = null; _dragging = false; _dragIcon.gameObject.SetActive(false); return; }

            bool hasMount = Ashveil.Gameplay.Mounts.MountClient.HasMount;
            if (_mountRow.Rt.gameObject.activeSelf != hasMount) _mountRow.Rt.gameObject.SetActive(hasMount);
            if (hasMount)
            {
                string n = Ashveil.Gameplay.Mounts.MountClient.Name;
                if (string.IsNullOrEmpty(n) && Ashveil.Gameplay.Mounts.MountClient.Definition != null) n = Ashveil.Gameplay.Mounts.MountClient.Definition.displayName;
                _mountName.text = string.IsNullOrEmpty(n) ? "Reittier" : n;
            }
            Vector2 mouse = RawInput.MousePosition;
            if (Over(_win, mouse) || _dragging) UiPointer.OverUi = true;
            if (RawInput.LeftMousePressed)
            {
                if (Over(_close, mouse)) { _open = false; return; }
                _press = null;
                foreach (Row r in _rows) if (Over(r.Rt, mouse)) { _press = r; _pressPos = mouse; break; }
            }
            if (_press != null && RawInput.LeftMouseHeld && !_dragging && (mouse - _pressPos).sqrMagnitude > 25f)
            {
                _dragging = true;
                _dragIcon.sprite = _press.Icon != null ? _press.Icon.sprite : null;
                _dragIcon.gameObject.SetActive(true);
            }
            if (_dragging)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, mouse, null, out Vector2 local);
                ((RectTransform)_dragIcon.transform).anchoredPosition = local;
            }
            if (RawInput.LeftMouseReleased)
            {
                if (_dragging && _press != null && !Over(_win, mouse))
                {
                    if (_press.Mount) UiDrag.Drop(UiDrag.Kind.Mount, Ashveil.Gameplay.Mounts.MountClient.MountId, mouse);
                    else if (_press.Spell != null) UiDrag.Drop(UiDrag.Kind.Spell, _press.Spell.Id, mouse);
                    else if (_press.Def != null) UiDrag.Drop(UiDrag.Kind.Ability, _press.Def.Id, mouse);
                }
                else if (!_dragging && _press != null && _press.Spell != null && Over(_press.Rt, mouse))
                    SpellClient.CastRequest?.Invoke(_press.Spell.Id);
                _press = null;
                _dragging = false;
                _dragIcon.gameObject.SetActive(false);
            }
        }

        private static bool Over(RectTransform rt, Vector2 screen) =>
            rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);
    }
}
