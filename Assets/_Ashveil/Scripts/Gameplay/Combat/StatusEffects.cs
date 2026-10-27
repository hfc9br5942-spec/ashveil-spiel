using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>
    /// Zeitlich begrenzte Zustaende einer Figur (P1 Klassenfaehigkeiten), nur auf dem Server:
    ///   Schild (Aschenschild) – faengt Schaden auf, bis er verbraucht ist oder ablaeuft,
    ///   Schadensverringerung (Ruestung staerken),
    ///   Verlangsamung (Donnerschlag, Zurechtstutzen) – bei Gegnern ueber den Tempo-Faktor der KI,
    ///   Sperre fuer einen neuen Schild ("Geschwaechte Seele" wie WoW).
    /// Wird bei Bedarf angehaengt (<see cref="Ensure"/>) und meldet sich bei <see cref="Health"/> an.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StatusEffects : MonoBehaviour, IDamageAbsorber
    {
        /// <summary>Ein laufender Zustand fuer die Anzeige (Name, verbleibende Zeit).</summary>
        public struct Entry
        {
            public string Id;
            public string Name;
            public float Until;
            public float Value;
            public bool Harmful;
        }

        private Health _health;
        private float _absorbLeft, _absorbUntil = -1f;
        private string _absorbName = "";
        private float _reduction, _reductionUntil = -1f;
        private string _reductionName = "";
        private float _slowFactor = 1f, _slowUntil = -1f;
        private string _slowName = "";
        private readonly Dictionary<string, float> _locks = new Dictionary<string, float>();

        /// <summary>Verlangsamung setzen/aufheben (Gegner-KI, Netz): Faktor 0..1.</summary>
        public Action<float> SpeedFactorSink;
        /// <summary>Etwas hat sich geaendert (Anzeige/Netz).</summary>
        public event Action Changed;
        /// <summary>Schild aufgebraucht oder abgelaufen.</summary>
        public event Action AbsorbEnded;

        public static StatusEffects Ensure(GameObject go)
        {
            if (go == null) return null;
            var s = go.GetComponent<StatusEffects>();
            if (s == null) s = go.AddComponent<StatusEffects>();
            return s;
        }

        private void Awake()
        {
            _health = GetComponent<Health>();
            if (_health != null) _health.SetAbsorber(this);
        }

        private void OnDestroy()
        {
            if (_health != null) _health.SetAbsorber(null);
        }

        // ---------------------------------------------------------------- Setzen

        public void AddAbsorb(float amount, float seconds, string name)
        {
            _absorbLeft = Mathf.Max(_absorbLeft, amount);
            _absorbUntil = Time.time + seconds;
            _absorbName = name ?? "";
            Changed?.Invoke();
        }

        public void AddDamageReduction(float fraction, float seconds, string name)
        {
            _reduction = Mathf.Clamp(fraction, 0f, 0.8f);
            _reductionUntil = Time.time + seconds;
            _reductionName = name ?? "";
            Changed?.Invoke();
        }

        /// <summary>Verlangsamen: der staerkste laufende Effekt gilt (kleinster Faktor), die Dauer wird aufgefrischt.</summary>
        public void AddSlow(float factor, float seconds, string name)
        {
            factor = Mathf.Clamp(factor, 0.2f, 1f);
            bool active = Time.time < _slowUntil;
            _slowFactor = active ? Mathf.Min(_slowFactor, factor) : factor;
            _slowUntil = Mathf.Max(active ? _slowUntil : 0f, Time.time + seconds);
            _slowName = name ?? "";
            SpeedFactorSink?.Invoke(_slowFactor);
            Changed?.Invoke();
        }

        public void Lock(string key, float seconds) => _locks[key] = Time.time + seconds;
        public bool IsLocked(string key) => _locks.TryGetValue(key, out float until) && Time.time < until;

        public void ClearAll()
        {
            bool slowed = Time.time < _slowUntil;
            _absorbLeft = 0f; _absorbUntil = -1f;
            _reduction = 0f; _reductionUntil = -1f;
            _slowFactor = 1f; _slowUntil = -1f;
            _locks.Clear();
            if (slowed) SpeedFactorSink?.Invoke(1f);
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- Abfragen

        public bool HasAbsorb => _absorbLeft > 0f && Time.time < _absorbUntil;
        public float AbsorbLeft => HasAbsorb ? _absorbLeft : 0f;
        public bool IsSlowed => Time.time < _slowUntil;
        public float SlowFactor => IsSlowed ? _slowFactor : 1f;
        public float Reduction => Time.time < _reductionUntil ? _reduction : 0f;

        public void Collect(List<Entry> into)
        {
            float now = Time.time;
            if (HasAbsorb) into.Add(new Entry { Id = "absorb", Name = _absorbName, Until = _absorbUntil, Value = _absorbLeft });
            if (now < _reductionUntil) into.Add(new Entry { Id = "reduction", Name = _reductionName, Until = _reductionUntil, Value = _reduction });
            if (now < _slowUntil) into.Add(new Entry { Id = "slow", Name = _slowName, Until = _slowUntil, Value = _slowFactor, Harmful = true });
        }

        // ---------------------------------------------------------------- IDamageAbsorber

        public float IncomingMultiplier => 1f - Reduction;

        public float Absorb(ref DamageInfo info, float amount)
        {
            if (!HasAbsorb || amount <= 0f) return amount;
            float taken = Mathf.Min(amount, _absorbLeft);
            _absorbLeft -= taken;
            info.Absorbed += taken;
            if (_absorbLeft <= 0.5f)
            {
                _absorbLeft = 0f;
                _absorbUntil = -1f;
                AbsorbEnded?.Invoke();
                Changed?.Invoke();
            }
            return amount - taken;
        }

        // ---------------------------------------------------------------- Takt

        private void Update()
        {
            float now = Time.time;
            if (_slowUntil > 0f && now >= _slowUntil)
            {
                _slowUntil = -1f;
                _slowFactor = 1f;
                SpeedFactorSink?.Invoke(1f);
                Changed?.Invoke();
            }
            if (_absorbUntil > 0f && now >= _absorbUntil)
            {
                _absorbUntil = -1f;
                _absorbLeft = 0f;
                AbsorbEnded?.Invoke();
                Changed?.Invoke();
            }
            if (_reductionUntil > 0f && now >= _reductionUntil)
            {
                _reductionUntil = -1f;
                _reduction = 0f;
                Changed?.Invoke();
            }
            if (_health != null && !_health.IsAlive && (HasAbsorb || IsSlowed)) ClearAll();
        }
    }
}
