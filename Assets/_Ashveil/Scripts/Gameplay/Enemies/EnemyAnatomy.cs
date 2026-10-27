using System;
using System.Collections.Generic;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Koerperteile eines Gegners (Boss-Anatomie, Combat-Masterplan §74): Trefferzonen zum Anklicken, eigenes Leben je
    /// Teil, Zustaende Intakt/Beschaedigt/Gebrochen/Zerstoert, Gesamtzustand Healthy/Wounded/Critical, Niederwurf und
    /// „herabgebeugt“ (Kopf erreichbar).
    ///
    /// Server: entscheidet alles (<see cref="IDamageRouter"/> an <see cref="Health"/>). Clients: bekommen die Zustaende
    /// gespiegelt (<see cref="ApplyReplicated"/>) und zeigen sie an. Wird zur Laufzeit angehaengt, sobald die
    /// Definition Koerperteile hat – bestehende Prefabs brauchen keinen Umbau.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyAnatomy : MonoBehaviour, IDamageRouter
    {
        public const int MaxParts = 8;

        public sealed class Part
        {
            public BodyPartDefinition Def;
            public Transform Pivot;
            public BoxCollider Hitbox;
            public float Max;
            public float Current;
            public BodyPartState State;
            public bool BreakHandled;
        }

        private readonly List<Part> _parts = new List<Part>();
        private EnemyDefinition _def;
        private Health _health;
        private EnemyBrain _brain;
        private bool _server;
        private float _knockdownUntil = -1f;
        private float _loweredUntil = -1f;

        public EnemyDefinition Definition => _def;
        public IReadOnlyList<Part> Parts => _parts;
        public EnemyDamageState DamageState { get; private set; } = EnemyDamageState.Healthy;
        public bool IsKnockedDown => Time.time < _knockdownUntil;
        public float KnockdownRemaining => Mathf.Max(0f, _knockdownUntil - Time.time);
        public bool IsLowered => IsKnockedDown || Time.time < _loweredUntil;

        /// <summary>Teil hat den Zustand gewechselt: Index, neuer Zustand (Server und Clients).</summary>
        public event Action<int, BodyPartState> PartStateChanged;
        public event Action<EnemyDamageState> DamageStateChanged;
        /// <summary>Niederwurf beginnt (Sekunden) – Server und Clients.</summary>
        public event Action<float> KnockedDown;
        /// <summary>Taumeln (Sekunden) – Server und Clients.</summary>
        public event Action<float> Staggered;

        /// <summary>Anhaengen, wenn die Definition Koerperteile hat. Gibt null zurueck, wenn nicht.</summary>
        public static EnemyAnatomy Ensure(GameObject go, EnemyDefinition def, bool server)
        {
            if (go == null || def == null || !def.HasAnatomy) return null;
            var a = go.GetComponent<EnemyAnatomy>();
            if (a == null) a = go.AddComponent<EnemyAnatomy>();
            a.Setup(def, server);
            return a;
        }

        private void Setup(EnemyDefinition def, bool server)
        {
            _server |= server;
            _health = GetComponent<Health>();
            _brain = GetComponent<EnemyBrain>();
            if (_def == def && _parts.Count > 0)
            {
                if (_server && _health != null) _health.SetRouter(this);
                return;
            }
            _def = def;
            _parts.Clear();
            int layer = gameObject.layer;
            foreach (BodyPartDefinition pd in def.bodyParts)
            {
                if (pd == null || pd.id == 0 || _parts.Count >= MaxParts) continue;
                Transform pivot = FindDeep(transform, pd.pivot);
                var part = new Part { Def = pd, Pivot = pivot, State = BodyPartState.Intact };
                if (pivot != null)
                {
                    // Trefferzone: Trigger (Kampf-Abfragen ignorieren Trigger, der Mausklick nicht) auf dem Gegner-Layer
                    var go = new GameObject("Trefferzone " + pd.displayName);
                    go.layer = layer;
                    go.transform.SetParent(pivot, false);
                    var box = go.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.center = pd.hitboxCenter;
                    box.size = pd.hitboxSize;
                    var hb = go.AddComponent<BodyPartHitbox>();
                    hb.Init(this, pd.id);
                    part.Hitbox = box;
                }
                _parts.Add(part);
            }
            ResetParts();
            if (_health != null)
            {
                _health.Changed -= OnHealthChanged;
                _health.Changed += OnHealthChanged;
                if (_server) _health.SetRouter(this);
            }
        }

        private void OnDestroy()
        {
            if (_health != null) _health.Changed -= OnHealthChanged;
        }

        /// <summary>Server: Teile auf voll (Spawn, Rueckkehr mit Vollheilung).</summary>
        public void ResetParts()
        {
            float max = _health != null ? _health.Max : 100f;
            for (int i = 0; i < _parts.Count; i++)
            {
                Part p = _parts[i];
                p.Max = Mathf.Max(1f, max * p.Def.healthShare);
                p.Current = p.Max;
                p.BreakHandled = false;
                SetPartState(i, BodyPartState.Intact);
            }
            _knockdownUntil = -1f;
            _loweredUntil = -1f;
            RecomputeDamageState();
        }

        // ================================================================== Abfragen

        public int IndexOf(byte id)
        {
            for (int i = 0; i < _parts.Count; i++) if (_parts[i].Def.id == id) return i;
            return -1;
        }

        public Part Get(byte id)
        {
            int i = IndexOf(id);
            return i >= 0 ? _parts[i] : null;
        }

        public BodyPartState StateOf(byte id)
        {
            Part p = Get(id);
            return p != null ? p.State : BodyPartState.Intact;
        }

        public bool IsBroken(byte id) => StateOf(id) >= BodyPartState.Broken;

        /// <summary>Kann dieses Teil jetzt getroffen werden?</summary>
        public bool IsReachable(byte id)
        {
            Part p = Get(id);
            if (p == null) return false;
            if (p.State == BodyPartState.Destroyed) return false;
            return p.Def.reach == BodyPartReach.Always || IsLowered;
        }

        // ================================================================== Server

        public float RouteDamage(ref DamageInfo info)
        {
            float amount = info.Amount;
            int idx = IndexOf(info.PartId);
            if (idx < 0 || !IsReachable(info.PartId))
            {
                info.PartId = 0;
                return amount;
            }
            Part p = _parts[idx];
            amount *= p.Def.damageMultiplier;
            DamagePart(idx, amount);
            return amount;
        }

        /// <summary>Server/Test: Teilschaden direkt (ohne Hauptleben).</summary>
        public void DamagePart(int idx, float amount)
        {
            if (idx < 0 || idx >= _parts.Count || amount <= 0f) return;
            Part p = _parts[idx];
            if (p.State == BodyPartState.Destroyed) return;
            p.Current -= amount;
            BodyPartState next = p.State;
            if (p.Current <= 0f)
            {
                next = BodyPartState.Broken;
                if (p.Def.destroyOverkill > 0f && p.Current <= -p.Def.destroyOverkill * p.Max) next = BodyPartState.Destroyed;
            }
            else if (p.Current <= p.Def.damagedAt * p.Max) next = BodyPartState.Damaged;
            if (next <= p.State) return; // Zustaende gehen nur vorwaerts
            SetPartState(idx, next);
            if (next >= BodyPartState.Broken && !p.BreakHandled)
            {
                p.BreakHandled = true;
                OnServerBroken(p);
            }
        }

        private void OnServerBroken(Part p)
        {
            if (_health != null && !_health.IsAlive) return;
            if (p.Def.knockdownSeconds > 0f) BeginKnockdown(p.Def.knockdownSeconds);
            else if (p.Def.staggerSeconds > 0f) BeginStagger(p.Def.staggerSeconds);
            if (_brain != null && p.Def.speedAfterBreak < 0.999f) _brain.ServerSetSpeedFactor(Mathf.Min(_brain.SpeedFactor, p.Def.speedAfterBreak));
        }

        public void BeginKnockdown(float seconds)
        {
            _knockdownUntil = Time.time + seconds;
            if (_brain != null) _brain.ServerStun(seconds);
            KnockedDown?.Invoke(seconds);
        }

        public void BeginStagger(float seconds)
        {
            if (_brain != null) _brain.ServerStun(seconds);
            Staggered?.Invoke(seconds);
        }

        /// <summary>Server und Clients: beugt sich herab (Kopf erreichbar) fuer so viele Sekunden.</summary>
        public void SetLowered(float seconds)
        {
            if (seconds > 0f) _loweredUntil = Mathf.Max(_loweredUntil, Time.time + seconds);
        }

        // ================================================================== Netz (Spiegel)

        /// <summary>Zustaende aller Teile, je 2 Bit (fuer eine SyncVar).</summary>
        public uint PackStates()
        {
            uint v = 0;
            for (int i = 0; i < _parts.Count; i++) v |= (uint)_parts[i].State << (i * 2);
            return v;
        }

        /// <summary>Client: gespiegelte Zustaende uebernehmen (Spaetkommer sehen gebrochene Teile).</summary>
        public void ApplyReplicated(uint packed, EnemyDamageState damageState)
        {
            if (_server) return;
            for (int i = 0; i < _parts.Count; i++)
            {
                var s = (BodyPartState)((packed >> (i * 2)) & 3u);
                if (s != _parts[i].State) SetPartState(i, s);
            }
            if (damageState != DamageState)
            {
                DamageState = damageState;
                DamageStateChanged?.Invoke(damageState);
            }
        }

        /// <summary>Client: Niederwurf/Taumeln vom Server.</summary>
        public void ApplyReplicatedStun(float seconds, bool knockdown)
        {
            if (_server) return;
            if (knockdown)
            {
                _knockdownUntil = Time.time + seconds;
                KnockedDown?.Invoke(seconds);
            }
            else Staggered?.Invoke(seconds);
        }

        // ================================================================== Intern

        private void SetPartState(int idx, BodyPartState s)
        {
            Part p = _parts[idx];
            if (p.State == s) return;
            p.State = s;
            if (p.Hitbox != null) p.Hitbox.enabled = s != BodyPartState.Destroyed;
            PartStateChanged?.Invoke(idx, s);
        }

        private void OnHealthChanged()
        {
            if (!_server) return;
            // Vollheilung (Rueckkehr) setzt die Teile zurueck
            if (_health.IsAlive && _health.Current >= _health.Max - 0.01f && AnyPartHurt()) ResetParts();
            RecomputeDamageState();
        }

        private bool AnyPartHurt()
        {
            foreach (Part p in _parts) if (p.Current < p.Max - 0.01f || p.State != BodyPartState.Intact) return true;
            return false;
        }

        private void RecomputeDamageState()
        {
            if (_health == null || _def == null) return;
            float f = _health.Fraction;
            EnemyDamageState s = f < _def.criticalBelow ? EnemyDamageState.Critical : f < _def.woundedBelow ? EnemyDamageState.Wounded : EnemyDamageState.Healthy;
            if (s == DamageState) return;
            DamageState = s;
            DamageStateChanged?.Invoke(s);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform t = FindDeep(root.GetChild(i), name);
                if (t != null) return t;
            }
            return null;
        }
    }
}
