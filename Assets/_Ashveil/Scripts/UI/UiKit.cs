using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// Kleine Bausteine fuer die per Code erzeugte Oberflaeche (Canvas, Panels, Balken, Text).
    /// Ziel: ruhig, dunkel, gut lesbar – keine Mobile-UI.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color PanelColor = new Color(0.06f, 0.06f, 0.08f, 0.82f);
        public static readonly Color BorderColor = new Color(0.55f, 0.47f, 0.33f, 0.9f);
        public static readonly Color TextColor = new Color(0.94f, 0.92f, 0.86f);
        public static readonly Color MutedText = new Color(0.72f, 0.7f, 0.64f);
        public static readonly Color HealthColor = new Color(0.22f, 0.72f, 0.26f);
        public static readonly Color StaminaColor = new Color(0.86f, 0.68f, 0.24f);

        private static Material _textMaterial;
        private static TMP_FontAsset _font;

        public static TMP_FontAsset Font
        {
            get
            {
                if (_font == null && TMP_Settings.instance != null) _font = TMP_Settings.defaultFontAsset;
                return _font;
            }
        }

        /// <summary>Gemeinsames Textmaterial mit feiner Kontur und Schatten.</summary>
        public static Material TextMaterial
        {
            get
            {
                if (_textMaterial != null || Font == null) return _textMaterial;
                _textMaterial = new Material(Font.material) { name = "UI Text (Outline)" };
                _textMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
                _textMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.18f);
                _textMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.9f));
                if (_textMaterial.HasProperty(ShaderUtilities.ID_UnderlayColor))
                {
                    _textMaterial.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                    _textMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.55f));
                    _textMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.3f);
                    _textMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.3f);
                    _textMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.35f);
                }
                return _textMaterial;
            }
        }

        public static Canvas CreateCanvas(string name, Transform parent, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            return canvas;
        }

        public static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>Fuellt das Elternobjekt vollstaendig aus (mit optionalem Innenabstand).</summary>
        public static RectTransform Stretch(string name, Transform parent, float inset = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static Image AddImage(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Dunkles Panel mit duennem, warmem Rahmen.</summary>
        public static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            RectTransform border = CreateRect(name, parent, anchor, pivot, pos, size);
            AddImage(border, BorderColor);
            RectTransform inner = Stretch("Inner", border, 1.5f);
            AddImage(inner, PanelColor);
            return inner;
        }

        public static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (Font != null) tmp.font = Font;
            if (TextMaterial != null) tmp.fontSharedMaterial = TextMaterial;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>Balken, dessen Fuellung ueber die Ankerbreite gesteuert wird (kein Sprite noetig).</summary>
        public sealed class Bar
        {
            public RectTransform Root;
            public RectTransform Fill;
            public Image FillImage;
            public TextMeshProUGUI Label;

            public void Set(float fraction)
            {
                Fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            }
        }

        public static Bar CreateBar(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
            Color fillColor, float textSize = 15f)
        {
            RectTransform root = CreateRect(name, parent, anchor, pivot, pos, size);
            AddImage(root, new Color(0f, 0f, 0f, 0.65f));
            RectTransform fill = Stretch("Fill", root, 1f);
            fill.anchorMax = new Vector2(1f, 1f);
            fill.offsetMax = new Vector2(0f, -1f);
            Image fillImg = AddImage(fill, fillColor);
            TextMeshProUGUI label = CreateText("Value", root, "", textSize, TextColor, TextAlignmentOptions.Center);
            var lr = (RectTransform)label.transform;
            lr.anchorMin = Vector2.zero;
            lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            return new Bar { Root = root, Fill = fill, FillImage = fillImg, Label = label };
        }
    }
}
