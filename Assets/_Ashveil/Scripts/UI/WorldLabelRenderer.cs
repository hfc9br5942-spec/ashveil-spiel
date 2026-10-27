using System.Collections.Generic;
using Ashveil.Core;
using Ashveil.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ashveil.UI
{
    /// <summary>
    /// Zeichnet alle <see cref="WorldLabel"/> als scharfe Namensschilder auf den Bildschirm.
    ///
    /// - TextMeshPro (SDF) auf einem Screen-Space-Canvas: gestochen scharf in jeder Aufloesung.
    /// - Konstante Pixelgroesse, skaliert nur mit der Bildschirmaufloesung (Referenz 1080p).
    /// - Dunkle Kontur und weicher Schatten fuer Lesbarkeit vor hellem wie dunklem Hintergrund.
    /// - Ausblenden mit Entfernung, hinter der Kamera und (fuer Orte/Hinweise) hinter Waenden.
    /// - Pool: Es existieren nur so viele Textobjekte, wie gleichzeitig sichtbar sind.
    /// </summary>
    [DefaultExecutionOrder(1000)] // LateUpdate nach der Kamera, damit Schilder nicht nachhinken
    public sealed class WorldLabelRenderer : MonoBehaviour
    {
        [Header("Schrift")]
        [Tooltip("Leer = Standard-Schrift aus den TextMeshPro-Einstellungen")]
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private float nameFontSize = 21f;
        [SerializeField] private float placeFontSize = 24f;
        [SerializeField] private float infoFontSize = 18f;
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);

        [Header("Kontur & Schatten")]
        [Range(0f, 0.5f)] [SerializeField] private float outlineWidth = 0.22f;
        [SerializeField] private Color outlineColor = new Color(0f, 0f, 0f, 0.95f);
        [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.6f);

        [Header("Sichtbarkeit")]
        [Tooltip("Anteil der Maximaldistanz, ueber den ausgeblendet wird")]
        [Range(0.05f, 0.5f)] [SerializeField] private float fadeFraction = 0.2f;
        [Tooltip("Orte und Hinweise hinter Waenden ausblenden")]
        [SerializeField] private bool occludePlacesAndInfo = true;
        [SerializeField] private float occlusionFadeSpeed = 6f;

        [Header("Farben")]
        [SerializeField] private Color friendlyColor = new Color(0.42f, 0.95f, 0.42f);
        [SerializeField] private Color hostileColor = new Color(1f, 0.3f, 0.22f);
        [SerializeField] private Color neutralColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private Color placeColor = new Color(0.97f, 0.87f, 0.62f);
        [SerializeField] private Color infoColor = new Color(0.86f, 0.89f, 0.93f);
        [SerializeField] private Color questMarkerColor = new Color(1f, 0.82f, 0.1f);
        [SerializeField] private Color subtitleColor = new Color(0.82f, 0.82f, 0.78f);

        private sealed class Entry
        {
            public RectTransform Rect;
            public TextMeshProUGUI Text;
            public int Version = -1;
            public float Visibility = 1f;
            public bool InUse;
        }

        private struct Visible
        {
            public Entry Entry;
            public float Distance;
        }

        private readonly Dictionary<WorldLabel, Entry> _assigned = new Dictionary<WorldLabel, Entry>();
        private readonly List<Entry> _pool = new List<Entry>();
        private readonly List<Visible> _visible = new List<Visible>();
        private readonly List<WorldLabel> _toRelease = new List<WorldLabel>();

        private RectTransform _root;
        private Material _sharedMaterial;
        private Camera _camera;
        private int _occlusionMask;
        private bool _ready;

        private void Awake()
        {
            // TMP_Settings.defaultFontAsset wirft, wenn die TMP-Einstellungen fehlen – daher erst pruefen.
            if (font == null && TMP_Settings.instance != null) font = TMP_Settings.defaultFontAsset;
            if (font == null)
            {
                Debug.LogWarning("[WorldLabelRenderer] Keine TextMeshPro-Schrift gefunden. " +
                                 "Menue: Window > TextMeshPro > Import TMP Essential Resources.");
                enabled = false;
                return;
            }

            _sharedMaterial = new Material(font.material) { name = "WorldLabel (Outline)" };
            _sharedMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
            _sharedMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
            _sharedMaterial.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
            if (_sharedMaterial.HasProperty(ShaderUtilities.ID_UnderlayColor))
            {
                _sharedMaterial.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                _sharedMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, shadowColor);
                _sharedMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.35f);
                _sharedMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.35f);
                _sharedMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.4f);
                _sharedMaterial.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.1f);
            }

            var canvasGo = new GameObject("WorldLabel Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -10; // unter dem spaeteren HUD
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.matchWidthOrHeight = 1f; // Hoehe bestimmt die Groesse
            _root = (RectTransform)canvasGo.transform;

            _occlusionMask = GameLayers.CameraCollisionMask();
            _ready = true;
        }

        private void OnDestroy()
        {
            if (_sharedMaterial != null) Destroy(_sharedMaterial);
        }

        private void LateUpdate()
        {
            if (!_ready) return;
            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            if (_camera == null) return;

            float dt = Time.deltaTime;
            Vector3 camPos = _camera.transform.position;
            _visible.Clear();

            IReadOnlyList<WorldLabel> labels = WorldLabel.Active;
            for (int i = 0; i < labels.Count; i++)
            {
                WorldLabel label = labels[i];
                Vector3 world = label.WorldPosition;
                float distance = Vector3.Distance(camPos, world);
                Vector3 screen = _camera.WorldToScreenPoint(world);

                bool onScreen = screen.z > 0.1f &&
                                screen.x > -200f && screen.x < Screen.width + 200f &&
                                screen.y > -100f && screen.y < Screen.height + 100f;
                if (distance > label.MaxDistance || !onScreen)
                {
                    Release(label);
                    continue;
                }

                Entry e = Acquire(label);

                if (e.Version != label.Version)
                {
                    ApplyText(e, label);
                    e.Version = label.Version;
                }

                // Weich ausblenden am Rand der Sichtweite.
                float fadeStart = label.MaxDistance * (1f - fadeFraction);
                float distanceAlpha = 1f - Mathf.Clamp01((distance - fadeStart) / Mathf.Max(0.01f, label.MaxDistance - fadeStart));

                // Orte/Hinweise hinter Waenden ausblenden (NPC-Namen bleiben sichtbar, wie in MMOs ueblich).
                float targetVisibility = 1f;
                if (occludePlacesAndInfo && (label.Style == WorldLabelStyle.Place || label.Style == WorldLabelStyle.Info))
                {
                    Vector3 dir = world - camPos;
                    float len = dir.magnitude;
                    if (len > 0.5f && Physics.Raycast(camPos, dir / len, len - 0.5f, _occlusionMask, QueryTriggerInteraction.Ignore))
                        targetVisibility = 0f;
                }
                e.Visibility = Mathf.MoveTowards(e.Visibility, targetVisibility, occlusionFadeSpeed * dt);

                e.Text.alpha = distanceAlpha * e.Visibility;
                e.Rect.position = new Vector3(Mathf.Round(screen.x), Mathf.Round(screen.y), 0f);
                _visible.Add(new Visible { Entry = e, Distance = distance });
            }

            // Nahe Schilder liegen oben.
            _visible.Sort((a, b) => b.Distance.CompareTo(a.Distance));
            for (int i = 0; i < _visible.Count; i++) _visible[i].Entry.Rect.SetSiblingIndex(i);

            // Schilder zerstoerter/deaktivierter Objekte freigeben.
            _toRelease.Clear();
            foreach (KeyValuePair<WorldLabel, Entry> pair in _assigned)
                if (pair.Key == null || !pair.Key.isActiveAndEnabled) _toRelease.Add(pair.Key);
            foreach (WorldLabel l in _toRelease) Release(l);

        }

        private Entry Acquire(WorldLabel label)
        {
            if (_assigned.TryGetValue(label, out Entry existing)) return existing;

            Entry e = null;
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i].InUse) continue;
                e = _pool[i];
                break;
            }
            if (e == null)
            {
                e = CreateEntry();
                _pool.Add(e);
            }

            e.InUse = true;
            e.Version = -1;
            // Orte/Hinweise blenden weich ein (koennten verdeckt sein), NPC-Namen erscheinen sofort.
            e.Visibility = label.Style == WorldLabelStyle.Place || label.Style == WorldLabelStyle.Info ? 0f : 1f;
            e.Text.gameObject.SetActive(true);
            _assigned[label] = e;
            return e;
        }

        private void Release(WorldLabel label)
        {
            if (label is null || !_assigned.TryGetValue(label, out Entry e)) return;
            e.InUse = false;
            e.Text.gameObject.SetActive(false);
            _assigned.Remove(label);
        }

        private Entry CreateEntry()
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(600f, 120f);
            rect.pivot = new Vector2(0.5f, 0f); // Text steht auf dem Weltpunkt

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSharedMaterial = _sharedMaterial;
            text.alignment = TextAlignmentOptions.Bottom;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
            text.raycastTarget = false;

            return new Entry { Rect = rect, Text = text };
        }

        private static string Sanitize(string text)
        {
            return string.IsNullOrEmpty(text) ? "" : text.Replace("</noparse>", "");
        }

        private void ApplyText(Entry e, WorldLabel label)
        {
            float baseSize;
            Color color;
            bool bold = true;
            switch (label.Style)
            {
                case WorldLabelStyle.Hostile:
                    baseSize = nameFontSize; color = hostileColor; break;
                case WorldLabelStyle.Neutral:
                    baseSize = nameFontSize; color = neutralColor; break;
                case WorldLabelStyle.Place:
                    baseSize = placeFontSize; color = placeColor; break;
                case WorldLabelStyle.Info:
                    baseSize = infoFontSize; color = infoColor; bold = false; break;
                default:
                    baseSize = nameFontSize; color = friendlyColor; break;
            }

            var sb = new System.Text.StringBuilder(128);
            if (label.MarkerControlled)
            {
                if (label.Marker != WorldLabelMarker.None)
                {
                    Color mc = label.Marker == WorldLabelMarker.InProgress ? new Color(0.62f, 0.6f, 0.56f) : questMarkerColor;
                    char ch = label.Marker == WorldLabelMarker.Available ? '!' : '?';
                    sb.Append("<size=165%><color=#").Append(ColorUtility.ToHtmlStringRGB(mc)).Append('>').Append(ch).Append("</color></size>\n");
                }
            }
            else if (label.Style == WorldLabelStyle.QuestGiver)
                sb.Append("<size=165%><color=#").Append(ColorUtility.ToHtmlStringRGB(questMarkerColor)).Append(">!</color></size>\n");

            if (bold) sb.Append("<b>");
            sb.Append("<noparse>").Append(Sanitize(label.Title)).Append("</noparse>");
            if (bold) sb.Append("</b>");

            if (!string.IsNullOrEmpty(label.Subtitle))
            {
                bool npc = label.Style == WorldLabelStyle.Friendly || label.Style == WorldLabelStyle.QuestGiver;
                sb.Append("\n<size=78%><color=#").Append(ColorUtility.ToHtmlStringRGB(subtitleColor)).Append('>');
                sb.Append("<noparse>");
                sb.Append(npc ? "<" + Sanitize(label.Subtitle) + ">" : Sanitize(label.Subtitle));
                sb.Append("</noparse></color></size>");
            }

            e.Text.text = sb.ToString();
            e.Text.fontSize = baseSize * label.SizeMultiplier;
            e.Text.fontStyle = FontStyles.Normal;
            e.Text.color = color;
            e.Text.lineSpacing = -8f;
        }
    }
}
