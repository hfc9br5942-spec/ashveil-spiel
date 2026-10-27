using System.Collections.Generic;
using Ashveil.Core;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Flutwasser der Salzsee-Grotte (Salzmutter, M6.3) – reine Darstellung auf jedem Rechner. Liegt als flache
    /// Wasserflaeche am Boden der Grotte; auf das Signal „Flut kuendigt sich an“ steigt sie ein wenig und kraeuselt,
    /// bei „Flut“ steigt sie schnell bis knapp unter die Plattformen, danach sinkt sie wieder.
    /// Den Schaden rechnet der Server (Begegnung), nicht dieses Objekt.
    /// </summary>
    public sealed class FloodWater : MonoBehaviour
    {
        private static readonly List<FloodWater> All = new List<FloodWater>();

        [Tooltip("Hoehe ueber dem Ruhestand bei voller Flut (m)")]
        [SerializeField] private float floodHeight = 0.9f;
        [Tooltip("Anstieg waehrend der Warnzeit (Anteil der vollen Flut)")]
        [SerializeField] private float warnLevel = 0.18f;

        private Vector3 _restPos;
        private Renderer[] _renderers;
        private float _level;          // 0 = Ruhe, 1 = volle Flut
        private float _target;
        private float _holdUntil = -1f;
        private DungeonSlot _slot;

        /// <summary>0 = Ruhe, 1 = volle Flut (Tests).</summary>
        public float Level01 => _level;
        public bool Flooding => _target >= 0.99f;

        /// <summary>Flutwasser im angegebenen Instanz-Platz (Tests).</summary>
        public static FloodWater In(DungeonSlot slot)
        {
            foreach (FloodWater f in All)
                if (f != null && f.Slot == slot) return f;
            return null;
        }

        private DungeonSlot Slot => _slot != null ? _slot : (_slot = DungeonSlot.At(transform.position));

        private void Awake()
        {
            _restPos = transform.localPosition;
            _renderers = GetComponentsInChildren<Renderer>(true);
            Apply();
        }

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            EncounterCues.Received += OnCue;
        }

        private void OnDisable()
        {
            All.Remove(this);
            EncounterCues.Received -= OnCue;
        }

        private void OnCue(Vector3 where, string cue, float value)
        {
            if (Slot == null || DungeonSlot.At(where) != Slot) return;
            switch (cue)
            {
                case EncounterCues.Reset:
                case EncounterCues.FloodEnd:
                    _target = 0f;
                    _holdUntil = -1f;
                    if (cue == EncounterCues.Reset) { _level = 0f; Apply(); }
                    break;
                case EncounterCues.FloodWarn:
                    _target = warnLevel;
                    _holdUntil = Time.time + Mathf.Max(0.5f, value) + 1f;
                    break;
                case EncounterCues.Flood:
                    _target = 1f;
                    _holdUntil = Time.time + Mathf.Max(0.5f, value);
                    break;
            }
        }

        private void Update()
        {
            if (_holdUntil > 0f && Time.time > _holdUntil)
            {
                _holdUntil = -1f;
                _target = 0f;
            }
            if (Mathf.Approximately(_level, _target) && _level <= 0f) return;
            float speed = _target > _level ? (_target >= 0.99f ? 1.4f : 0.25f) : 0.5f;
            _level = Mathf.MoveTowards(_level, _target, speed * Time.deltaTime);
            Apply();
        }

        private void Apply()
        {
            float ripple = _level > 0f ? Mathf.Sin(Time.time * 2.3f) * 0.03f * (1f - _level * 0.5f) : 0f;
            transform.localPosition = _restPos + Vector3.up * (_level * floodHeight + ripple);
            bool visible = _level > 0.005f;
            foreach (Renderer r in _renderers)
                if (r != null && r.enabled != visible) r.enabled = visible;
        }
    }
}
