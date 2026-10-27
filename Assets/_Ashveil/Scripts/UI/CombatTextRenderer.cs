using System.Collections.Generic;
using Ashveil.Core.Events;
using TMPro;
using UnityEngine;

namespace Ashveil.UI
{
    /// <summary>
    /// Schwebende Schadenszahlen ueber dem Ziel: steigen auf, blenden aus. Kritische Treffer groesser,
    /// gelb und mit kurzem "Aufploppen". Dezent gehalten – keine Zahlenflut.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class CombatTextRenderer : MonoBehaviour
    {
        [SerializeField] private float lifetime = 1.0f;
        [SerializeField] private float riseSpeed = 70f;
        [SerializeField] private float normalSize = 30f;
        [SerializeField] private float critSize = 44f;

        private sealed class Entry
        {
            public TextMeshProUGUI Text;
            public Vector3 World;
            public Vector2 Drift;
            public float Age;
            public float BaseSize;
            public bool Active;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private RectTransform _root;
        private Camera _camera;

        private void OnEnable()
        {
            EventBus<CombatTextEvent>.Subscribe(OnCombatText);
        }

        private void OnDisable()
        {
            EventBus<CombatTextEvent>.Unsubscribe(OnCombatText);
        }

        private void Start()
        {
            if (UiKit.Font == null)
            {
                enabled = false;
                return;
            }
            _root = (RectTransform)UiKit.CreateCanvas("CombatText Canvas", transform, 5).transform;
        }

        private void OnCombatText(CombatTextEvent evt)
        {
            if (_root == null || evt.Kind == CombatTextKind.Error) return;
            // Einstellung „Schadenszahlen“: Voll / Weniger (nur kritische Treffer und erlittener Schaden) / Aus
            int mode = Ashveil.Core.Settings.GraphicsSettingsStore.Current != null ? Ashveil.Core.Settings.GraphicsSettingsStore.Current.damageNumbers : 0;
            if (mode == 2 && evt.Kind != CombatTextKind.Miss) return;
            if (mode == 1 && (evt.Kind == CombatTextKind.Damage || evt.Kind == CombatTextKind.Heal)) return;

            Entry e = null;
            foreach (Entry x in _entries)
                if (!x.Active) { e = x; break; }
            if (e == null)
            {
                e = new Entry { Text = UiKit.CreateText("Combat Text", _root, "", normalSize, Color.white, TextAlignmentOptions.Center) };
                e.Text.rectTransform.sizeDelta = new Vector2(300f, 60f);
                _entries.Add(e);
            }

            bool crit = evt.Kind == CombatTextKind.Critical;
            e.Active = true;
            e.Age = 0f;
            e.World = evt.WorldPosition;
            e.Drift = new Vector2(Random.Range(-40f, 40f), 0f);
            e.BaseSize = crit ? critSize : normalSize;
            e.Text.text = crit ? evt.Text + "!" : evt.Text;
            e.Text.color = ColorFor(evt.Kind);
            e.Text.fontStyle = crit ? FontStyles.Bold : FontStyles.Normal;
            e.Text.gameObject.SetActive(true);
        }

        private static Color ColorFor(CombatTextKind kind)
        {
            switch (kind)
            {
                case CombatTextKind.Critical: return new Color(1f, 0.85f, 0.2f);
                case CombatTextKind.DamageTaken: return new Color(1f, 0.3f, 0.25f);
                case CombatTextKind.Heal: return new Color(0.35f, 1f, 0.4f);
                case CombatTextKind.Miss: return new Color(0.75f, 0.75f, 0.75f);
                default: return new Color(1f, 1f, 1f);
            }
        }

        private void LateUpdate()
        {
            if (_root == null) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            float dt = Time.unscaledDeltaTime;
            foreach (Entry e in _entries)
            {
                if (!e.Active) continue;
                e.Age += dt;
                if (e.Age >= lifetime)
                {
                    e.Active = false;
                    e.Text.gameObject.SetActive(false);
                    continue;
                }

                Vector3 screen = _camera.WorldToScreenPoint(e.World);
                if (screen.z <= 0f)
                {
                    e.Text.alpha = 0f;
                    continue;
                }

                float t = e.Age / lifetime;
                float scale = _root.lossyScale.x > 0f ? _root.lossyScale.x : 1f;
                Vector2 offset = (e.Drift * t + new Vector2(0f, riseSpeed * e.Age)) * scale;
                e.Text.rectTransform.position = new Vector3(screen.x + offset.x, screen.y + offset.y, 0f);

                float pop = e.Age < 0.12f ? 1f + (1f - e.Age / 0.12f) * 0.5f : 1f;
                e.Text.fontSize = e.BaseSize * pop;
                e.Text.alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            }
        }
    }
}
