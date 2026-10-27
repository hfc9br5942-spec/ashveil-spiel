using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>
    /// Schaden ueber Zeit (Gift der Nadelspeier). Laeuft nur dort, wo Leben wirklich abgezogen wird
    /// (Server): tickt jede Sekunde, erneutes Vergiften frischt die Dauer auf (stapelt nicht).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DamageOverTime : MonoBehaviour
    {
        private Health _health;
        private GameObject _source;
        private string _id;
        private float _perSecond;
        private float _remaining;
        private float _tick;

        /// <summary>Verbleibende Giftdauer (s), 0 = nicht vergiftet.</summary>
        public float Remaining => _remaining;
        public float PerSecond => _perSecond;
        public bool IsActive => _remaining > 0f;

        /// <summary>Server: Ziel vergiften.</summary>
        public static DamageOverTime Apply(Health target, GameObject source, float perSecond, float duration, string id)
        {
            if (target == null || perSecond <= 0f || duration <= 0f || !target.IsAlive || target.IsReplica) return null;
            var dot = target.GetComponent<DamageOverTime>();
            if (dot == null) dot = target.gameObject.AddComponent<DamageOverTime>();
            dot._health = target;
            dot._source = source;
            dot._id = id;
            dot._perSecond = Mathf.Max(dot._remaining > 0f ? dot._perSecond : 0f, perSecond);
            if (dot._remaining <= 0f) dot._tick = 1f;
            dot._remaining = Mathf.Max(dot._remaining, duration);
            dot.enabled = true;
            return dot;
        }

        public static bool IsPoisoned(Health h)
        {
            var dot = h != null ? h.GetComponent<DamageOverTime>() : null;
            return dot != null && dot.IsActive;
        }

        /// <summary>Gift entfernen (Tod, Wiederbeleben, Heiltrank spaeter).</summary>
        public void Clear()
        {
            _remaining = 0f;
            enabled = false;
        }

        private void Update()
        {
            if (_health == null || !_health.IsAlive)
            {
                Clear();
                return;
            }
            float dt = Time.deltaTime;
            _remaining -= dt;
            _tick -= dt;
            if (_tick <= 0f)
            {
                _tick += 1f;
                _health.ApplyDamage(new DamageInfo
                {
                    Amount = Mathf.Max(1f, Mathf.Round(_perSecond)),
                    Source = _source,
                    HitPoint = transform.position + Vector3.up * 1.2f,
                    Direction = Vector3.zero,
                    WeaponId = _id,
                    Unblockable = true,
                    Periodisch = true   // 23.10.2026 (Kampfreaktionen): Tick-Kennung für alle Ursachen (.dot, .poison, .bleed)
                });
            }
            if (_remaining <= 0f) Clear();
        }
    }
}
