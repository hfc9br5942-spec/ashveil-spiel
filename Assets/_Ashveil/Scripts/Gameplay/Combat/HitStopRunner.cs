using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>Hilfsobjekt fuer <see cref="HitStop"/>: stellt die Zeit nach Ablauf wieder her.</summary>
    public sealed class HitStopRunner : MonoBehaviour
    {
        private const float SlowScale = 0.06f;

        private float _remaining;
        private bool _active;
        private float _previousScale = 1f;

        public void Begin(float duration)
        {
            _remaining = Mathf.Max(_remaining, duration);
            if (_active) return;
            _active = true;
            _previousScale = Time.timeScale;
            Time.timeScale = Mathf.Min(SlowScale, _previousScale);
        }

        private void Update()
        {
            if (!_active) return;
            _remaining -= Time.unscaledDeltaTime;
            if (_remaining > 0f) return;
            _active = false;
            _remaining = 0f;
            Time.timeScale = _previousScale;
        }

        private void OnDisable()
        {
            if (_active) Time.timeScale = _previousScale;
            _active = false;
        }
    }
}
