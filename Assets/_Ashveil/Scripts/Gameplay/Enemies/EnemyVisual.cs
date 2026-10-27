using System.Collections.Generic;
using Ashveil.Gameplay.Audio;
using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Darstellung eines Gegners – laeuft auf allen Rechnern. Die Geschwindigkeit wird aus der Bewegung
    /// des Objekts abgeleitet (auf Clients kommt die Position vom Server), Angriffe/Heulen meldet das Netz.
    /// Gemeinsam fuer alle Arten: Warnring fuer Stampfer, Tarnung/Abtauchen, Sprung.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public abstract class EnemyVisual : MonoBehaviour
    {
        [Header("Gemeinsam")]
        [Tooltip("Fuellung des Warnrings am Boden (Stampfer)")]
        [SerializeField] private Material warningFill;
        [SerializeField] private Material warningRim;

        private Vector3 _lastPos;
        private Vector3 _smoothedVelocity;
        private Renderer[] _renderers;
        private readonly List<Renderer> _eyes = new List<Renderer>();
        private EnemyConcealment _concealment;

        protected Health Health { get; private set; }

        /// <summary>Geglaettete horizontale Geschwindigkeit (m/s, Welt).</summary>
        protected Vector3 Velocity => _smoothedVelocity;
        protected float Speed => _smoothedVelocity.magnitude;
        protected bool IsDead => Health != null && !Health.IsAlive;
        public EnemyConcealment Concealment => _concealment;

        protected virtual void Awake()
        {
            Health = GetComponent<Health>();
            _lastPos = transform.position;
            _renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in _renderers)
            {
                string n = r.name.ToLowerInvariant();
                if (n.Contains("eye") && !n.Contains("brow")) _eyes.Add(r);
            }
        }

        protected virtual void OnEnable()
        {
            Health.Died += Gestorben;
            Health.Damaged += MerkeTreffer;
            Health.Changed += Geaendert;
            // 23.10. (Codex Paket C): Leichenzustand vom Netz – späte Beobachter sehen den fertigen Schnitt
            _leiche = GetComponentInParent<ILeichenzustand>();
            if (_leiche != null)
            {
                _leiche.TrennpunktGeaendert += LeicheGeaendert;
                if (_leiche.Trennpunkt != 0) LeicheGeaendert(0, _leiche.Trennpunkt);
            }
        }

        protected virtual void OnDisable()
        {
            Health.Died -= Gestorben;
            Health.Damaged -= MerkeTreffer;
            Health.Changed -= Geaendert;
            if (_leiche != null) _leiche.TrennpunktGeaendert -= LeicheGeaendert;
        }

        private ILeichenzustand _leiche;
        private byte _leicheAusstehend;

        /// <summary>
        /// Leichenzustand (Host und Clients): getrennt, aber hier noch nicht dargestellt (später Beobachter, Wiederverbinden) → ruhig trennen,
        /// ohne neuen Wurf/Blutstoß. Kommt der Wert vor dem Tod an (Reihenfolge SyncVar), wird er beim Tod nachgeholt. 0 = lebt (Respawn läuft über Geaendert).
        /// </summary>
        private void LeicheGeaendert(byte alt, byte neu)
        {
            if (neu == 0) { _leicheAusstehend = 0; return; }
            if (_getrennt) return;
            if (!_todGemeldet) { _leicheAusstehend = neu; return; }   // Tod noch nicht gemeldet (Host: Wert kommt vor „Gestorben“) → dort normal trennen
            _getrennt = true;
            Trenne(new DamageInfo { Trennpunkt = neu, Direction = -transform.forward }, true);
        }

        // CORE-005 (11.10.2026): Sichtbarkeit fuer teure Darstellung (prozedurale Beine usw.) – alle 0,2 s geprueft
        private float _visCheckAt;
        private bool _onScreen = true;

        /// <summary>Ist ein Renderer dieser Figur gerade von einer Kamera sichtbar? (ohne Grafik/Server immer false)</summary>
        protected bool OnScreen()
        {
            if (Time.unscaledTime < _visCheckAt) return _onScreen;
            _visCheckAt = Time.unscaledTime + 0.2f;
            _onScreen = false;
            if (_renderers != null)
                for (int i = 0; i < _renderers.Length; i++)
                    if (_renderers[i] != null && _renderers[i].isVisible) { _onScreen = true; break; }
            return _onScreen;
        }

        protected virtual void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 v = (transform.position - _lastPos) / dt;
            v.y = 0f;
            if (v.magnitude > 30f) v = Vector3.zero; // Teleport/Respawn/Sprung
            _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, v, 1f - Mathf.Exp(-10f * dt));
            _lastPos = transform.position;
        }

        /// <summary>Angriff wird vorbereitet (Ausholen/Zielen), danach folgt <see cref="PlayAttackResolved"/>.</summary>
        public abstract void PlayAttack(Vector3 aimPoint, float windup, bool ranged);

        /// <summary>Angriff ausgefuehrt.</summary>
        public abstract void PlayAttackResolved(bool hit, Vector3 point);

        /// <summary>Stampfer angekuendigt: Warnring am Boden, Unterklassen zeigen das Ausholen.</summary>
        public virtual void PlayAreaAttack(Vector3 center, float windup, float radius)
        {
            GroundWarning.Spawn(center, radius, windup, warningFill, warningRim);
        }

        /// <summary>Ferngezuendete Ladung explodiert: Blitz, Staubring und Knall (Boss-Faehigkeit).</summary>
        public virtual void PlayExplosion(Vector3 center, float radius)
        {
            SfxPlayer.Play(SfxId.Explosion, center + Vector3.up * 0.5f, 1f, 0.08f);
            Blast.Spawn(center, radius, warningRim);
        }

        /// <summary>Sprung auf das Ziel (die Position springt bereits vom Server).</summary>
        public virtual void PlayPounce(Vector3 from, Vector3 to)
        {
            SfxPlayer.Play(SfxId.Growl, to + Vector3.up * 0.6f, 0.9f, 0.1f);
        }

        /// <summary>Angriff aus dem Moveset beginnt (kommt vor PlayAttack/PlayAreaAttack): welche Bewegung.</summary>
        public virtual void PlayMove(byte moveId, float windup) { }

        /// <summary>Ein Koerperteil ist gebrochen oder zerstoert (Splitter, Krachen).</summary>
        public virtual void PlayPartBroken(int partIndex, Ashveil.Data.BodyPartState state) { }

        /// <summary>Rudel rufen / Kampfschrei.</summary>
        public virtual void PlayCall() { }

        /// <summary>Tarnung (nur Augen glimmen schwach) oder Abtauchen (ganz weg).</summary>
        public virtual void SetConcealment(EnemyConcealment c)
        {
            if (c == _concealment) return;
            EnemyConcealment before = _concealment;
            _concealment = c;
            foreach (Renderer r in _renderers)
            {
                if (r == null) continue;
                bool eye = _eyes.Contains(r);
                r.enabled = c == EnemyConcealment.None || (c == EnemyConcealment.Camouflaged && eye);
            }
            if (c == EnemyConcealment.Submerged || before == EnemyConcealment.Submerged)
                SfxPlayer.Play(SfxId.Splash, transform.position + Vector3.up * 0.3f, 0.7f, 0.1f);
        }

        // 22.10.2026 (Zerstückelung, COMBAT-026a; Codex-Review): letzter Treffer. Ob und wo getrennt wird, entscheidet der Server
        // am Treffer selbst (DamageInfo.Trennpunkt, kommt per ObserversDamaged). Auf dem Server/Host und im Einzelspiel läuft kein
        // Spiegel-Treffer – dort rechnet die Darstellung im selben Moment dieselbe reine Entscheidung (gleiches Ergebnis wie gesendet).
        protected DamageInfo LetzterTreffer { get; private set; }
        private float _letzterTrefferZeit = -10f;
        private bool _todGemeldet, _getrennt;

        /// <summary>23.10.: Trennpunkt des tödlichen Treffers (0 = nicht zerstückelt) – der Server würfelt daraus die Zusatzbeute (COMBAT-026b).</summary>
        public byte TodesTrennpunkt => Time.time - _letzterTrefferZeit < 0.3f ? LetzterTreffer.Trennpunkt : (byte)0;

        /// <summary>Wo dieser Gegner bei einem Treffer an <paramref name="punkt"/> getrennt würde (0 = gar nicht). Nur der Server fragt.</summary>
        public virtual byte TrennpunktFuer(Vector3 punkt, Vector3 richtung) => 0;

        /// <summary>Glied/Teil abtrennen (Todesanimation beim kritischen Todesstoß einer Hiebwaffe).</summary>
        protected virtual void Trenne(DamageInfo info, bool ruhig = false) { }

        private void MerkeTreffer(DamageInfo info)
        {
            if (!Health.IsReplica) info.Trennpunkt = Zerstueckelung.TrennpunktFuer(info, Health.Current, gameObject);
            LetzterTreffer = info;
            _letzterTrefferZeit = Time.time;
            OnDamaged(info);
            // Netz: kam der Lebenswert (SyncVar 0) vor dem tödlichen Treffer an, ist der Tod schon gemeldet – dann jetzt trennen
            if (_todGemeldet && !_getrennt && info.Trennpunkt != 0) { _getrennt = true; Trenne(info); }
        }

        private void Gestorben()
        {
            _todGemeldet = true;
            OnDied();
            if (!_getrennt && LetzterTreffer.Trennpunkt != 0 && Time.time - _letzterTrefferZeit < 0.3f) { _getrennt = true; Trenne(LetzterTreffer); }
            else if (!_getrennt && _leicheAusstehend != 0) { _getrennt = true; Trenne(new DamageInfo { Trennpunkt = _leicheAusstehend, Direction = -transform.forward }, true); }
        }

        private void Geaendert()
        {
            if (!IsDead) { _todGemeldet = false; _getrennt = false; }   // Vormerkung bleibt: nur der Leichenzustand selbst (→ 0) löscht sie (Codex: MaxHealth-Changed vor Current 0)
            OnHealthChanged();
        }

        protected abstract void OnDied();
        protected virtual void OnDamaged(DamageInfo info) { }

        /// <summary>Wiederbelebt / zurueckgesetzt.</summary>
        protected virtual void OnHealthChanged() { }
    }
}
