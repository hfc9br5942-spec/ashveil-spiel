using System;
using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>
    /// Lebenspunkte. Einzige Stelle, an der Leben veraendert wird.
    /// Mit dem Netzwerk-Layer laeuft ApplyDamage nur auf dem Server; Clients bekommen den Wert gespiegelt.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100f;
        [Tooltip("Stirbt nie, bleibt bei 1 stehen (Trainingspuppen)")]
        [SerializeField] private bool invulnerable;

        private float _current;

        public event Action<DamageInfo> Damaged;
        /// <summary>Geheilt: Menge, Heiler (null = von selbst / unbekannt). Nur auf dem Server.</summary>
        public event Action<float, GameObject> Healed;
        public event Action Died;
        public event Action Changed;

        public float Max => maxHealth;
        public float Current => _current;
        public float Fraction => maxHealth > 0f ? _current / maxHealth : 0f;
        public bool IsAlive => _current > 0f;
        public float LastDamageTime { get; private set; } = -999f;
        private float _invulnerableUntil = -1f;

        /// <summary>Kurz unverwundbar (Ausweichrolle).</summary>
        public void SetInvulnerableFor(float seconds)
        {
            _invulnerableUntil = Mathf.Max(_invulnerableUntil, Time.time + seconds);
        }

        public bool IsTemporarilyInvulnerable => Time.time < _invulnerableUntil;

        /// <summary>Ein Treffer wurde durch Unverwundbarkeit abgewehrt.</summary>
        public event Action<DamageInfo> Evaded;

        /// <summary>Ein Angriff hat verfehlt (z. B. viel zu starker Gegner). Nur dort, wo entschieden wird.</summary>
        public event Action<DamageInfo> Missed;

        public void RaiseMissed(in DamageInfo info)
        {
            if (IsReplica) return;
            Missed?.Invoke(info);
        }

        private IDamageMitigation _mitigation;
        private IDamageBlocker _blocker;
        private IDodger _dodger;
        private IDamageRouter _router;
        private IDamageAbsorber _absorber;

        /// <summary>Zustaende (Schilde, Schadensverringerung) – haengt der Server bei Bedarf an.</summary>
        public void SetAbsorber(IDamageAbsorber absorber) => _absorber = absorber;

        /// <summary>Koerperteile (Boss-Anatomie): wird zur Laufzeit angehaengt.</summary>
        public void SetRouter(IDamageRouter router) => _router = router;

        private void Awake()
        {
            _current = maxHealth;
            _mitigation = GetComponent<IDamageMitigation>();
            _blocker = GetComponent<IDamageBlocker>();
            _dodger = GetComponent<IDodger>();
        }

        // ---------------------------------------------------------------- Netzwerk-Spiegel

        /// <summary>
        /// Spiegelbild auf einem Client: Leben kommt vom Server, lokal wird nichts abgezogen.
        /// </summary>
        public bool IsReplica { get; private set; }

        public void SetReplica(bool replica) => IsReplica = replica;

        /// <summary>Vom Server gemeldeten Treffer darstellen (Wert + Ereignisse fuer Reaktionen/Anzeigen).</summary>
        public void ReplicateDamage(in DamageInfo info, float newCurrent)
        {
            bool wasAlive = IsAlive;
            _current = Mathf.Clamp(newCurrent, 0f, maxHealth);
            LastDamageTime = Time.time;
            Damaged?.Invoke(info);
            Changed?.Invoke();
            if (wasAlive && _current <= 0f) Died?.Invoke();
        }

        /// <summary>Maximales Leben vom Server uebernehmen (Gegnerstufen, spaeter Ausruestung).</summary>
        public void SetMaxFromNetwork(float max)
        {
            max = Mathf.Max(1f, max);
            if (Mathf.Approximately(max, maxHealth)) return;
            maxHealth = max;
            _current = Mathf.Min(_current, maxHealth);
            Changed?.Invoke();
        }

        /// <summary>Wert vom Server uebernehmen (Heilung, Zuruecksetzen, spaeter Beitretende).</summary>
        public void SetCurrentFromNetwork(float value)
        {
            bool wasAlive = IsAlive;
            float v = Mathf.Clamp(value, 0f, maxHealth);
            if (Mathf.Approximately(v, _current)) return;
            _current = v;
            Changed?.Invoke();
            if (wasAlive && _current <= 0f) Died?.Invoke();
        }

        public void ApplyDamage(in DamageInfo info)
        {
            if (IsReplica) return; // entscheidet der Server
            if (!IsAlive || info.Amount <= 0f) return;
            if (IsTemporarilyInvulnerable)
            {
                Evaded?.Invoke(info);
                return;
            }

            DamageInfo final = info;
            // Ausgewichen (vor Ruestung und Block – wie im Classic-Trefferwurf): kein Schaden, aber alle sehen es
            if (info.Dodgeable && _dodger != null && _dodger.TryDodge(info))
            {
                final.Amount = 0f;
                final.Dodged = true;
                final.Reaktion = TrefferReaktionen.Ableiten(final);   // 23.10.2026 (Kampfreaktionen): Ausgewichen
                Damaged?.Invoke(final);
                return;
            }
            if (_router != null) final.Amount = Mathf.Max(1f, Mathf.Round(_router.RouteDamage(ref final)));
            if (_absorber != null) final.Amount = Mathf.Max(1f, Mathf.Round(final.Amount * _absorber.IncomingMultiplier));
            // Rüstung – nicht bei Umweltschaden (Ertrinken, Max 06.10.2026: wie Classic)
            if (_mitigation != null && !final.Environmental) final.Amount = Mathf.Max(1f, Mathf.Round(_mitigation.Mitigate(final.Amount)));
            // Schildblock nach der Ruestung (Classic): ein voll geblockter Treffer richtet keinen Schaden an
            if (_blocker != null && !final.Unblockable) final.Amount = Mathf.Max(0f, Mathf.Round(_blocker.Block(ref final, final.Amount)));
            // Aschenschild faengt den Rest auf
            if (_absorber != null && final.Amount > 0f) final.Amount = Mathf.Max(0f, Mathf.Round(_absorber.Absorb(ref final, final.Amount)));
            _current = Mathf.Max(invulnerable ? 1f : 0f, _current - final.Amount);
            LastDamageTime = Time.time;
            // 23.10.2026 (Kampfreaktionen, Paket C): Ergebnis aus dem endgültigen Treffer – dieselbe reine Funktion, die der Client
            // aus den übertragenen Feldern nachrechnet; der Server schickt es mit (NetworkHealth.ObserversDamaged)
            final.Reaktion = TrefferReaktionen.Ableiten(final);
            Damaged?.Invoke(final);
            Changed?.Invoke();
            if (final.Amount > 0f || final.Absorbed > 0f) AnyDamaged?.Invoke(this, final);
            if (_current <= 0f) Died?.Invoke();
        }

        /// <summary>Server: irgendeine Figur hat Schaden genommen (Wut des Angreifers, Statistik). Ziel und endgueltiger Treffer.</summary>
        public static event Action<Health, DamageInfo> AnyDamaged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => AnyDamaged = null;

        public void Heal(float amount) => Heal(amount, null);

        public void Heal(float amount, GameObject healer)
        {
            if (IsReplica) return;
            if (!IsAlive || amount <= 0f) return;
            float before = _current;
            _current = Mathf.Min(maxHealth, _current + amount);
            Changed?.Invoke();
            if (_current > before) Healed?.Invoke(_current - before, healer);
        }

        /// <summary>Maximum aendern (Ausruestung), der Fuellstand bleibt anteilig erhalten.</summary>
        public void SetMaxHealth(float max)
        {
            if (IsReplica) return;
            max = Mathf.Max(1f, Mathf.Round(max));
            if (Mathf.Approximately(max, maxHealth)) return;
            float fraction = Fraction;
            maxHealth = max;
            if (IsAlive) _current = Mathf.Clamp(Mathf.Round(maxHealth * fraction), 1f, maxHealth);
            Changed?.Invoke();
        }

        /// <summary>Server: Gefallene wiederbeleben (Kolosseum: Mitspieler) – mit diesem Anteil des Lebens.</summary>
        public void Revive(float fraction)
        {
            if (IsReplica || IsAlive) return;
            _current = Mathf.Clamp(Mathf.Round(maxHealth * Mathf.Clamp01(fraction)), 1f, maxHealth);
            Changed?.Invoke();
        }

        public void ResetToFull()
        {
            if (IsReplica) return;
            _current = maxHealth;
            Changed?.Invoke();
        }

        public void Configure(float max, bool isInvulnerable)
        {
            maxHealth = Mathf.Max(1f, max);
            invulnerable = isInvulnerable;
            _current = maxHealth;
        }
    }
}
