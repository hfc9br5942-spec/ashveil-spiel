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
    /// Talente (Taste N, P1 – WoW Classic): drei Baeume der eigenen Klasse nebeneinander, je Reihe bis zu vier Talente.
    /// Klick = einen Punkt setzen (der Server prueft freie Punkte, Maximalrang und die 5-Punkte-Regel je Reihe).
    /// Ab Stufe 10 ein Punkt je Stufe. Zuruecksetzen beim Klassenlehrer.
    /// </summary>
    [DefaultExecutionOrder(-460)]
    public sealed class TalentUi : MonoBehaviour
    {
        private sealed class Cell
        {
            public TalentDefinition Def;
            public RectTransform Rt;
            public Image Frame, Icon;
            public TextMeshProUGUI Rank;
        }

        public static TalentUi Instance { get; private set; }
        public bool IsOpen => _open;
        public int CellCount => _cells.Count;

        private const float TreeW = 250f, CellSize = 46f, CellGap = 12f, Top = -86f;

        private RectTransform _canvasRt, _win, _close, _tip, _body;
        private TextMeshProUGUI _title, _points, _tipText;
        private readonly TextMeshProUGUI[] _treeTitles = new TextMeshProUGUI[3];
        private readonly List<Cell> _cells = new List<Cell>();
        private bool _open, _built;
        private int _shownRev = -1;
        private string _shownClass = "";

        public void Build(Transform parent)
        {
            Instance = this;
            Canvas canvas = UiKit.CreateCanvas("Talente Canvas", parent, 21);
            _canvasRt = (RectTransform)canvas.transform;
            RectTransform inner = UiKit.Panel("Talente", _canvasRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(TreeW * 3f + 40f, 340f));
            _win = (RectTransform)inner.parent;
            _title = Text(_win, 21f, new Vector2(14f, -10f), new Vector2(500f, 28f));
            _points = Text(_win, 15f, new Vector2(14f, -40f), new Vector2(TreeW * 3f, 22f));
            _close = UiKit.CreateRect("Schliessen", _win, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(26f, 26f));
            UiKit.AddImage(_close, UiKit.BorderColor);
            TextMeshProUGUI x = UiKit.CreateText("X", _close, "×", 18f, UiKit.TextColor, TextAlignmentOptions.Center);
            x.rectTransform.anchorMin = Vector2.zero; x.rectTransform.anchorMax = Vector2.one;
            x.rectTransform.offsetMin = x.rectTransform.offsetMax = Vector2.zero;
            for (int t = 0; t < 3; t++)
            {
                RectTransform col = UiKit.CreateRect("Baum " + t, _win, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f + t * (TreeW + 6f), -64f), new Vector2(TreeW, 262f));
                UiKit.AddImage(col, new Color(0f, 0f, 0f, 0.22f));
                _treeTitles[t] = Text(col, 16f, new Vector2(8f, -4f), new Vector2(TreeW - 16f, 22f));
                _treeTitles[t].color = new Color(0.91f, 0.79f, 0.54f);
            }
            _body = UiKit.CreateRect("Zellen", _win, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            RectTransform tipInner = UiKit.Panel("Tooltip", _canvasRt, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(300f, 90f));
            _tip = (RectTransform)tipInner.parent;
            _tipText = UiKit.CreateText("Text", tipInner, "", 14f, UiKit.TextColor);
            _tipText.richText = true;
            _tipText.textWrappingMode = TextWrappingModes.Normal;
            RectTransform tr = _tipText.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(8f, 6f); tr.offsetMax = new Vector2(-8f, -6f);
            _tip.gameObject.SetActive(false);
            _win.gameObject.SetActive(false);
            UiFensterstapel.Anmelden(this, "Talente", () => _open, () => _open = false);
            _built = true;
        }

        public void SetOpen(bool open) => _open = open;

        private void Rebuild(CharacterClassDefinition cls)
        {
            foreach (Cell c in _cells) if (c.Rt != null) Destroy(c.Rt.gameObject);
            _cells.Clear();
            _shownClass = cls != null ? cls.Id : "";
            for (int t = 0; t < 3; t++)
                _treeTitles[t].text = cls != null && cls.talentTrees != null && t < cls.talentTrees.Length ? cls.talentTrees[t] : "";
            if (cls == null || GameData.Database == null) return;
            foreach (GameDefinition g in GameData.Database.All)
            {
                if (!(g is TalentDefinition d) || d.classId != cls.Id) continue;
                float x = 14f + d.tree * (TreeW + 6f) + 22f + d.column * (CellSize + CellGap);
                float y = Top - 8f - d.tier * (CellSize + 34f);
                RectTransform rt = UiKit.CreateRect(d.DisplayName, _body, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(CellSize, CellSize));
                Image frame = UiKit.AddImage(rt, UiKit.BorderColor);
                Image icon = UiKit.AddImage(UiKit.Stretch("Bild", rt, 2.5f), Color.white);
                icon.sprite = ItemIcons.ForShape(d.iconShape, d.iconColor);
                icon.preserveAspect = true;
                RectTransform badge = UiKit.CreateRect("Rang", rt, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-4f, 2f), new Vector2(34f, 18f));
                UiKit.AddImage(badge, new Color(0f, 0f, 0f, 0.85f));
                TextMeshProUGUI rank = UiKit.CreateText("Zahl", badge, "", 12f, UiKit.TextColor, TextAlignmentOptions.Center);
                rank.rectTransform.anchorMin = Vector2.zero; rank.rectTransform.anchorMax = Vector2.one;
                rank.rectTransform.offsetMin = rank.rectTransform.offsetMax = Vector2.zero;
                _cells.Add(new Cell { Def = d, Rt = rt, Frame = frame, Icon = icon, Rank = rank });
            }
        }

        private int PointsInTree(int tree)
        {
            int n = 0;
            foreach (var kv in SpellClient.Talents)
            {
                TalentDefinition d = GameData.Get<TalentDefinition>(kv.Key);
                if (d != null && d.tree == tree) n += kv.Value;
            }
            return n;
        }

        private void Refresh(CharacterClassDefinition cls)
        {
            _shownRev = SpellClient.Revision;
            int level = InventoryClient.Current != null ? InventoryClient.Current.Level : 1;
            _title.text = "Talente" + (cls != null ? " – " + cls.DisplayName : "") + "  <size=70%><color=#a8a296>(N)</color></size>";
            _points.text = level < TalentDefinition.FirstTalentLevel
                ? $"<color=#a8a296>Talente gibt es ab Stufe {TalentDefinition.FirstTalentLevel} – ein Punkt je Stufe.</color>"
                : $"Freie Punkte: <color=#e8c98a>{SpellClient.TalentPointsFree}</color>   Vergeben: {SpellClient.TalentPointsSpent}   <color=#a8a296>Zurücksetzen beim Klassenlehrer.</color>";
            int[] inTree = { PointsInTree(0), PointsInTree(1), PointsInTree(2) };
            for (int t = 0; t < 3; t++)
            {
                string baseName = cls != null && cls.talentTrees != null && t < cls.talentTrees.Length ? cls.talentTrees[t] : "";
                _treeTitles[t].text = $"{baseName}  <size=80%><color=#a8a296>({inTree[t]})</color></size>";
                _treeTitles[t].richText = true;
            }
            foreach (Cell c in _cells)
            {
                int r = SpellClient.TalentRank(c.Def.Id);
                bool reachable = inTree[c.Def.tree] >= c.Def.tier * TalentDefinition.PointsPerTier;
                bool can = reachable && SpellClient.TalentPointsFree > 0 && r < c.Def.maxRank;
                c.Rank.text = $"{r}/{c.Def.maxRank}";
                c.Rank.color = r >= c.Def.maxRank ? new Color(0.95f, 0.8f, 0.35f) : r > 0 || can ? new Color(0.5f, 0.9f, 0.4f) : UiKit.MutedText;
                c.Icon.color = reachable ? Color.white : new Color(0.35f, 0.35f, 0.35f);
                c.Frame.color = r >= c.Def.maxRank ? new Color(0.95f, 0.8f, 0.35f) : can ? new Color(0.45f, 0.8f, 0.35f) : UiKit.BorderColor;
            }
        }

        private void Update()
        {
            if (!_built) return;
            if (UiPointer.HotkeysAllowed && RawInput.WasPressed(GameKey.Talents)) _open = !_open;
            // Esc schließt über den Fensterstapel (UI-204: erst das oberste Fenster, dann das Ziel, dann das Spielmenü)
            if (_win.gameObject.activeSelf != _open) _win.gameObject.SetActive(_open);
            if (!_open) { if (_tip.gameObject.activeSelf) _tip.gameObject.SetActive(false); return; }
            CharacterClassDefinition cls = InventoryClient.Current != null ? InventoryClient.Current.Class : null;
            if ((cls != null ? cls.Id : "") != _shownClass) { Rebuild(cls); _shownRev = -1; }
            if (_shownRev != SpellClient.Revision || Time.frameCount % 30 == 0) Refresh(cls);

            Vector2 mouse = RawInput.MousePosition;
            if (Over(_win, mouse)) UiPointer.OverUi = true;
            Cell hover = null;
            foreach (Cell c in _cells) if (Over(c.Rt, mouse)) { hover = c; break; }
            UpdateTip(hover, mouse);
            if (!RawInput.LeftMousePressed) return;
            if (Over(_close, mouse)) { _open = false; return; }
            if (hover != null) SpellClient.TalentRequest?.Invoke(hover.Def.Id);
        }

        private void UpdateTip(Cell c, Vector2 mouse)
        {
            bool show = c != null;
            if (_tip.gameObject.activeSelf != show) _tip.gameObject.SetActive(show);
            if (!show) return;
            int r = SpellClient.TalentRank(c.Def.Id);
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(c.Def.DisplayName).Append("</b>  <color=#a8a296>Rang ").Append(r).Append('/').Append(c.Def.maxRank).Append("</color>\n");
            if (c.Def.tier > 0) sb.Append($"<size=85%><color=#a8a296>Braucht {c.Def.tier * TalentDefinition.PointsPerTier} Punkte im Baum.</color></size>\n");
            if (r > 0) sb.Append(c.Def.DescribeRank(r));
            if (r < c.Def.maxRank) sb.Append(r > 0 ? "\n\n<color=#a8a296>Nächster Rang:</color>\n" : "").Append(c.Def.DescribeRank(r + 1));
            _tipText.text = sb.ToString();
            float h = _tipText.GetPreferredValues(_tipText.text, 284f, 0f).y + 16f;
            _tip.sizeDelta = new Vector2(300f, Mathf.Max(60f, h));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, mouse, null, out Vector2 local);
            Vector2 size = _canvasRt.rect.size;
            _tip.anchoredPosition = new Vector2(Mathf.Min(local.x + size.x * 0.5f + 18f, size.x - 310f), Mathf.Max(8f, local.y + size.y * 0.5f - h - 12f));
        }

        private static TextMeshProUGUI Text(RectTransform parent, float size, Vector2 pos, Vector2 box)
        {
            TextMeshProUGUI t = UiKit.CreateText("Text", parent, "", size, UiKit.TextColor);
            t.richText = true;
            RectTransform rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            return t;
        }

        private static bool Over(RectTransform rt, Vector2 screen) =>
            rt != null && rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);

        private void OnDisable() { if (Instance == this) Instance = null; }
    }
}
