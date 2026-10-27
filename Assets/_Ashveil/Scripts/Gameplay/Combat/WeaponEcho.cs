using System.Collections.Generic;
using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Combat
{
    /// <summary>
    /// Legendaere Eigenschaft „Nachhall“ (Max 04.10.2026): Ein Treffer einer Waffe mit <see cref="WeaponDefinition.HasEcho"/>
    /// wiederholt sich gelegentlich kurz darauf als Teilschaden am selben Ziel – mit interner Abklingzeit je Traeger.
    ///
    /// Nur der Server entscheidet: <see cref="Health.AnyDamaged"/> feuert ausschliesslich dort, wo Leben wirklich abgezogen
    /// wird (Clients halten Spiegelbilder). Kein Geschoss, keine Reichweitenaenderung, keine Physikabfrage – je Treffer
    /// eine Woerterbuch-Abfrage, je Nachhall ein Listeneintrag. Der Nachhall selbst traegt <see cref="EchoId"/> als
    /// WeaponId und loest deshalb keinen weiteren Nachhall aus; er ist nicht blockbar und nicht ausweichbar (die
    /// Wiederholung eines Treffers, der bereits durchkam), Ruestung und Schilde wirken weiter.
    /// Clients sehen ihn ueber den normalen Treffer-RPC (Schadenszahl) und den Lichtpuls (<see cref="WeaponEchoVisual"/>).
    /// </summary>
    public static class WeaponEcho
    {
        public const string EchoId = "proc.nachhall";

        /// <summary>Server: ein Nachhall wurde angewendet (Angreifer, Ziel, endgueltiger Treffer) – Netz/Tests/Statistik.</summary>
        public static event System.Action<GameObject, Health, DamageInfo> Echoed;

        private struct Pending
        {
            public float Due;
            public Health Target;
            public DamageInfo Info;
        }

        private static readonly List<Pending> _pending = new List<Pending>();
        private static readonly Dictionary<GameObject, float> _ready = new Dictionary<GameObject, float>();
        private static readonly List<GameObject> _prune = new List<GameObject>();
        private static Runner _runner;

        /// <summary>Anzahl ausgeloester Nachhalle seit Spielstart (Selbsttest).</summary>
        public static int TriggeredCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _pending.Clear();
            _ready.Clear();
            _runner = null;
            Echoed = null;
            TriggeredCount = 0;
        }

        // Health setzt AnyDamaged bei SubsystemRegistration zurueck – deshalb erst danach anmelden
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            Health.AnyDamaged -= OnAnyDamaged;
            Health.AnyDamaged += OnAnyDamaged;
        }

        private static void OnAnyDamaged(Health target, DamageInfo info)
        {
            if (target == null || info.Amount <= 0f || info.Dodged) return;
            if (string.IsNullOrEmpty(info.WeaponId) || info.WeaponId == EchoId) return;
            if (info.Source == null) return;
            WeaponDefinition w = GameData.Get<WeaponDefinition>(info.WeaponId);
            if (w == null || !w.HasEcho) return;

            float now = Time.time;
            if (_ready.TryGetValue(info.Source, out float readyAt) && now < readyAt) return;
            if (Random.value > w.echoChance) return;

            if (_ready.Count > 512) Prune();
            _ready[info.Source] = now + w.echoCooldown;

            DamageInfo echo = info;
            echo.Amount = Mathf.Max(1f, Mathf.Round(info.Amount * w.echoFraction));
            echo.IsCritical = false;
            echo.WeaponId = EchoId;
            echo.Knockback = 0f;
            echo.Unblockable = true;
            echo.Dodgeable = false;
            echo.Dodged = false;
            echo.Blocked = 0f;
            echo.Absorbed = 0f;
            _pending.Add(new Pending { Due = now + w.echoDelay, Target = target, Info = echo });
            EnsureRunner();
        }

        private static void Prune()
        {
            _prune.Clear();
            foreach (KeyValuePair<GameObject, float> kv in _ready)
                if (kv.Key == null || kv.Value < Time.time) _prune.Add(kv.Key);
            foreach (GameObject g in _prune) _ready.Remove(g);
            _prune.Clear();
        }

        private static void Tick()
        {
            if (_pending.Count == 0) return;
            float now = Time.time;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                if (now < p.Due) continue;
                _pending.RemoveAt(i);
                // Ziel oder Angreifer weg/tot: der Nachhall verklingt
                if (p.Target == null || !p.Target.IsAlive || p.Info.Source == null) continue;
                TriggeredCount++;
                p.Target.ApplyDamage(p.Info);
                Echoed?.Invoke(p.Info.Source, p.Target, p.Info);
            }
        }

        private static void EnsureRunner()
        {
            if (_runner != null) return;
            var go = new GameObject("[WeaponEcho]") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
        }

        private sealed class Runner : MonoBehaviour
        {
            private void Update() => Tick();
        }
    }
}
