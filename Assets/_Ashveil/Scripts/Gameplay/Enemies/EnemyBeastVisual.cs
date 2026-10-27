using System.Collections.Generic;
using Ashveil.Gameplay.Audio;
using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Prozedural animierter Vierbeiner (Platzhalter fuer den Aschwolf, bis ein echtes Modell kommt):
    /// Kreuzgang der Beine nach Geschwindigkeit, Wippen, Kopf- und Schwanzbewegung, Sprungbiss mit
    /// sichtbarem Ducken vorher, Heulen (Rudelruf) und Umfallen beim Tod.
    /// #28 (Greybox-Tiere): Wurf im Bogen statt Nadelstrich (Schlickwerfer) und Aufrichten mit Kehlsack beim
    /// Flaechenangriff (Fluesterbalg, Schallstoss). Treffer und Zeitpunkt rechnet weiter der Server – nur Darstellung.
    /// </summary>
    public sealed class EnemyBeastVisual : EnemyVisual
    {
        [SerializeField] private Transform model;
        [SerializeField] private Transform head;
        [SerializeField] private Transform tail;
        [Tooltip("Vorne links, vorne rechts, hinten links, hinten rechts")]
        [SerializeField] private Transform[] legs = new Transform[4];
        [SerializeField] private float strideLength = 1.4f;
        [SerializeField] private float runSpeedReference = 6.5f;
        [Tooltip("Fernkampf (Nadelspeier): Material der Nadelspur")]
        [SerializeField] private Material tracerMaterial;
        [Tooltip("#28 Schlickwerfer: statt der Nadelspur fliegt ein Klumpen (Material wie oben) im Bogen zum Ziel")]
        [SerializeField] private bool lobbed;
        [Tooltip("#28 Fluesterbalg: Kehlsack, der sich beim Flaechenangriff (Schallstoss) sichtbar aufblaeht")]
        [SerializeField] private Transform sac;

        private LineRenderer _tracer;
        private float _tracerTime;
        private bool _ranged;

        // Wurf im Bogen (Schlickwerfer)
        private LineRenderer _blob;
        private Vector3 _blobFrom, _blobTo;
        private float _blobT = -1f, _blobDuration;
        private bool _blobHit;

        // Aufrichten beim Flaechenangriff (Fluesterbalg)
        private Vector3 _sacScale = Vector3.one;
        private float _rear, _rearTotal;

        private Vector3 _modelPos;
        private Quaternion _modelRot, _headRot, _tailRot;
        private Quaternion[] _legRot = new Quaternion[4];
        private float _phase;
        private float _time;

        // Angriff: >0 = ducken (Ausholen), danach Sprung nach vorn
        private float _windup, _windupTotal;
        private float _lunge;       // 1 -> 0
        private float _howl;        // Sekunden
        private float _death;       // 0 -> 1
        private float _hitShake;

        protected override void Awake()
        {
            base.Awake();
            if (model == null) model = transform;
            _modelPos = model.localPosition;
            _modelRot = model.localRotation;
            if (head != null) _headRot = head.localRotation;
            if (tail != null) _tailRot = tail.localRotation;
            for (int i = 0; i < legs.Length && i < 4; i++)
                if (legs[i] != null) _legRot[i] = legs[i].localRotation;
            if (sac != null) _sacScale = sac.localScale;
            if (tracerMaterial != null && lobbed)
            {
                var go = new GameObject("Wurfklumpen");
                go.transform.SetParent(transform, false);
                _blob = go.AddComponent<LineRenderer>();
                _blob.sharedMaterial = tracerMaterial;
                _blob.positionCount = 2;
                _blob.widthMultiplier = 0.2f;
                _blob.numCapVertices = 6;
                _blob.useWorldSpace = true;
                _blob.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _blob.receiveShadows = false;
                _blob.enabled = false;
            }
            else if (tracerMaterial != null)
            {
                var go = new GameObject("Nadelspur");
                go.transform.SetParent(transform, false);
                _tracer = go.AddComponent<LineRenderer>();
                _tracer.sharedMaterial = tracerMaterial;
                _tracer.positionCount = 2;
                _tracer.widthMultiplier = 0.05f;
                _tracer.useWorldSpace = true;
                _tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _tracer.enabled = false;
            }
        }

        public override void PlayAttack(Vector3 aimPoint, float windup, bool ranged)
        {
            _windupTotal = Mathf.Max(0.05f, windup);
            _windup = _windupTotal;
            _ranged = ranged;
            if (!ranged) SfxPlayer.Play(SfxId.Growl, transform.position + Vector3.up * 0.6f, 0.7f, 0.12f);
        }

        public override void PlayAttackResolved(bool hit, Vector3 point)
        {
            _windup = 0f;
            if (_ranged && _blob != null)
            {
                // Schlickwurf: ausholen, loslassen – der Klumpen fliegt im Bogen (etwa 18 m/s) und klatscht am Ziel auf
                SfxPlayer.Play(SfxId.Swing, transform.position + Vector3.up * 0.6f, 0.7f, 0.15f);
                _blobFrom = head != null ? head.position + transform.forward * 0.3f : transform.position + Vector3.up * 0.7f;
                _blobTo = point;
                _blobHit = hit;
                _blobDuration = Mathf.Clamp(Vector3.Distance(_blobFrom, _blobTo) / 18f, 0.15f, 0.9f);
                _blobT = 0f;
                _blob.enabled = true;
                UpdateBlob(0f);
                _lunge = 0.6f;
                return;
            }
            if (_ranged)
            {
                SfxPlayer.Play(SfxId.Spit, transform.position + Vector3.up * 0.6f, 0.8f, 0.15f);
                if (_tracer != null)
                {
                    Vector3 from = head != null ? head.position : transform.position + Vector3.up * 0.6f;
                    _tracer.SetPosition(0, from);
                    _tracer.SetPosition(1, point);
                    _tracer.enabled = true;
                    _tracerTime = 0.14f;
                }
                return;
            }
            _lunge = 1f;
            SfxPlayer.Play(SfxId.Swing, transform.position + Vector3.up * 0.6f, 0.6f, 0.15f);
        }

        /// <summary>Flaechenangriff: Warnring (Basis) und sichtbares Aufrichten mit aufgeblaehtem Kehlsack (Fluesterbalg).</summary>
        public override void PlayAreaAttack(Vector3 center, float windup, float radius)
        {
            base.PlayAreaAttack(center, windup, radius);
            _rearTotal = Mathf.Max(0.05f, windup);
            _rear = _rearTotal;
            if (sac != null) SfxPlayer.Play(SfxId.Growl, transform.position + Vector3.up * 0.6f, 0.6f, 0.25f);
        }

        private void UpdateBlob(float dt)
        {
            _blobT += dt / Mathf.Max(0.01f, _blobDuration);
            float t = Mathf.Clamp01(_blobT);
            float arc = Mathf.Min(3f, Vector3.Distance(_blobFrom, _blobTo) * 0.18f);
            Vector3 p = Vector3.Lerp(_blobFrom, _blobTo, t) + Vector3.up * (4f * arc * t * (1f - t));
            Vector3 dir = (_blobTo - _blobFrom).normalized;
            _blob.SetPosition(0, p);
            _blob.SetPosition(1, p - dir * 0.06f);
            if (_blobT < 1f) return;
            _blobT = -1f;
            _blob.enabled = false;
            SfxPlayer.Play(SfxId.Splash, _blobTo, _blobHit ? 0.8f : 0.5f, 0.12f);
        }

        public override void PlayCall()
        {
            _howl = 1.6f;
            SfxPlayer.Play(SfxId.Howl, transform.position + Vector3.up * 0.8f, 0.9f, 0.06f);
        }

        protected override void OnDied()
        {
            _death = Mathf.Max(_death, 0.0001f);
            SfxPlayer.Play(SfxId.Growl, transform.position + Vector3.up * 0.5f, 0.5f, 0.3f);
        }

        // 22.10.2026 (Max, COMBAT-026a/b): kritischer Todesstoß einer Hiebwaffe trennt Kopf, Bein oder Schwanz ab. Welches Teil,
        // wählt der Server (nächstes zum Trefferpunkt) und schickt es als 1 + Teilnummer (0 Kopf, 1 Schwanz, 2.. Bein) mit dem Treffer.
        public override byte TrennpunktFuer(Vector3 punkt, Vector3 richtung)
        {
            int was = NaechstesTeil(punkt);
            return was < 0 ? (byte)0 : (byte)(was + 1);
        }

        protected override void Trenne(Combat.DamageInfo info, bool ruhig = false)
        {
            if (EnemyGore.Full) TrenneTeilNr(info.Trennpunkt - 1, info.Direction, ruhig);
        }

        protected override void OnDamaged(Combat.DamageInfo info) => _hitShake = 0.2f;

        protected override void OnHealthChanged()
        {
            if (!IsDead) { _death = 0f; TeilZuruecksetzen(); }
        }

        // ---- Zerstückelung (starre Platzhalterteile: EnemyGore.Detach) und Zurücksetzen beim Wiederbeleben
        private Transform _ab;
        private Transform _abEltern;
        private Vector3 _abPos, _abScale;
        private Quaternion _abRot;
        private int _abWas = -1;   // 0 Kopf, 1 Schwanz, 2..5 Bein
        private readonly List<(GameObject Go, int Layer)> _abLayer = new List<(GameObject, int)>();
        private readonly List<Collider> _abTreffer = new List<Collider>();
        private readonly HashSet<Collider> _abVorher = new HashSet<Collider>();

        /// <summary>Teil am nächsten zum Trefferpunkt abtrennen (Kopf, Schwanz oder Bein) – fällt mit Physik, bleibt bei der Leiche liegen.</summary>
        private void TrenneTeil(Vector3 punkt, Vector3 richtung) => TrenneTeilNr(NaechstesTeil(punkt), richtung);   // Prüfbild (AuftragZerstueckelungBild)

        /// <summary>Teilnummer am nächsten zum Punkt: 0 Kopf, 1 Schwanz, 2.. Bein; -1 = keins.</summary>
        private int NaechstesTeil(Vector3 punkt)
        {
            int was = -1; float bd = float.MaxValue;
            void Pruefe(Transform t, int w) { if (t == null) return; float d = (t.position - punkt).sqrMagnitude; if (d < bd) { bd = d; was = w; } }
            Pruefe(head, 0); Pruefe(tail, 1);
            for (int i = 0; i < legs.Length; i++) Pruefe(legs[i], 2 + i);
            return was;
        }

        private void TrenneTeilNr(int was, Vector3 richtung, bool ruhig = false)
        {
            if (_ab != null || was < 0) return;
            Transform best = was == 0 ? head : was == 1 ? tail : was - 2 < legs.Length ? legs[was - 2] : null;
            if (best == null) return;
            _ab = best; _abWas = was; _abEltern = best.parent; _abPos = best.localPosition; _abRot = best.localRotation; _abScale = best.localScale;
            _abLayer.Clear(); _abTreffer.Clear(); _abVorher.Clear();
            foreach (Collider c in best.GetComponents<Collider>()) _abVorher.Add(c);
            foreach (Transform t in best.GetComponentsInChildren<Transform>(true)) _abLayer.Add((t.gameObject, t.gameObject.layer));
            foreach (Collider c in best.GetComponentsInChildren<Collider>(true)) if (c.isTrigger && c.enabled) _abTreffer.Add(c);
            if (was == 0) head = null; else if (was == 1) tail = null; else legs[was - 2] = null;   // Animation lässt das Teil los
            Vector3 dir = richtung.sqrMagnitude > 1e-6f ? richtung.normalized : transform.forward;
            GameObject losGo = EnemyGore.Detach(best, transform, ruhig ? Vector3.zero : dir * 2.5f + Vector3.up * 1.5f);   // ruhig: Leichenzustand, das Teil liegt schon
            if (ruhig && losGo != null && losGo.TryGetComponent(out Rigidbody losRb)) losRb.angularVelocity = Vector3.zero;   // Detach setzt sonst einen Zufallsdrall (Codex)
            Renderer r = best.GetComponentInChildren<Renderer>();
            Material blut = r != null ? EnemyGore.Tint(r.sharedMaterial, new Color(0.32f, 0.02f, 0.02f)) : null;
            if (Application.isPlaying && !ruhig)   // Splitter/Flecken zerstören sich zeitversetzt – nur im Spiel (Prüfbild im Editor ohne)
            {
                EnemyGore.Burst(best.position, dir, blut, 12, 0.03f, 8f);
                EnemyGore.Stain(best.position, blut, 0.8f, Zerstueckelung.Liegezeit);
            }
            // gemeinsames Budget mit den Menschenteilen (24 Teile / 180 s): das Original wird danach nur ausgeblendet, nicht
            // zerstört – der Respawn setzt es wieder an (Codex-Review 22.10.)
            Transform ab = best;
            Zerstueckelung.Liegen(best.gameObject, () => { if (_ab == ab && ab != null) ab.gameObject.SetActive(false); });
        }

        private void TeilZuruecksetzen()
        {
            if (_ab == null) return;
            Zerstueckelung.Abmelden(_ab.gameObject);
            _ab.gameObject.SetActive(true);
            foreach (Rigidbody rb in _ab.GetComponents<Rigidbody>()) Destroy(rb);
            foreach (Collider c in _ab.GetComponents<Collider>()) if (!_abVorher.Contains(c)) Destroy(c);   // nur den von Detach angelegten
            _ab.SetParent(_abEltern, false);
            _ab.localPosition = _abPos; _ab.localRotation = _abRot; _ab.localScale = _abScale;
            foreach ((GameObject go, int layer) in _abLayer) if (go != null) go.layer = layer;
            foreach (Collider c in _abTreffer) if (c != null) c.enabled = true;
            if (_abWas == 0) head = _ab; else if (_abWas == 1) tail = _ab; else legs[_abWas - 2] = _ab;
            _ab = null; _abWas = -1;
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            float dt = Time.deltaTime;
            _time += dt;
            if (_tracer != null && _tracerTime > 0f)
            {
                _tracerTime -= dt;
                if (_tracerTime <= 0f) _tracer.enabled = false;
            }
            if (_blobT >= 0f && _blob != null) UpdateBlob(dt);
            if (!OnScreen()) return; // CORE-005: Beine, Kopf und Schwanz nur fuer sichtbare Tiere bewegen

            float speed = IsDead ? 0f : Speed;
            float run = Mathf.Clamp01(speed / Mathf.Max(0.1f, runSpeedReference));
            _phase += speed * dt * (2f * Mathf.PI / Mathf.Max(0.2f, strideLength));

            // ---- Beine im Kreuzgang
            float swing = Mathf.Lerp(0f, 38f, Mathf.Clamp01(speed / 1.2f)) * (0.6f + 0.4f * run);
            for (int i = 0; i < 4 && i < legs.Length; i++)
            {
                if (legs[i] == null) continue;
                bool diagonalA = i == 0 || i == 3;
                float a = Mathf.Sin(_phase + (diagonalA ? 0f : Mathf.PI)) * swing;
                if (IsDead) a = i < 2 ? 25f : -20f; // steif ausgestreckt
                legs[i].localRotation = _legRot[i] * Quaternion.Euler(a, 0f, 0f);
            }

            // ---- Koerper: Wippen, Atmen, Ducken, Sprung
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.06f * Mathf.Clamp01(speed / 1.5f);
            float breathe = Mathf.Sin(_time * 2.4f) * 0.008f;
            float crouch = 0f, forward = 0f, pitch = 0f;
            if (_windup > 0f)
            {
                _windup -= dt;
                float k = 1f - Mathf.Clamp01(_windup / _windupTotal);
                crouch = -0.12f * Mathf.SmoothStep(0f, 1f, k);
                forward = -0.12f * k;
                pitch = 6f * k; // Hinterteil hoch, Kopf runter
            }
            if (_lunge > 0f)
            {
                _lunge = Mathf.Max(0f, _lunge - dt * 3.2f);
                float l = Mathf.Sin(_lunge * Mathf.PI);
                forward += 0.45f * l;
                crouch += 0.12f * l;
                pitch -= 10f * l;
            }
            if (_rear > 0f)
            {
                // Aufrichten: Vorderkoerper hoch, Kehlsack blaeht sich auf (Schallstoss angekuendigt)
                _rear = Mathf.Max(0f, _rear - dt);
                float k = Mathf.SmoothStep(0f, 1f, 1f - _rear / _rearTotal);
                pitch -= 24f * k;
                crouch += 0.1f * k;
                if (sac != null) sac.localScale = _sacScale * (1f + 0.9f * k);
                if (_rear <= 0f && sac != null) sac.localScale = _sacScale;
            }

            Vector3 pos = _modelPos + new Vector3(0f, bob + breathe + crouch, forward);
            Quaternion rot = _modelRot * Quaternion.Euler(pitch + Mathf.Sin(_phase * 2f) * 1.5f * run, 0f, 0f);

            if (_hitShake > 0f)
            {
                _hitShake -= dt;
                rot *= Quaternion.Euler(0f, 0f, Mathf.Sin(_time * 70f) * 6f * _hitShake / 0.2f);
            }

            // ---- Tod: auf die Seite fallen
            if (IsDead)
            {
                _death = Mathf.Min(1f, _death + dt * 2.2f);
                float d = Mathf.SmoothStep(0f, 1f, _death);
                rot = _modelRot * Quaternion.Euler(0f, 0f, 88f * d);
                pos = _modelPos + new Vector3(0.05f * d, -0.28f * d, 0f);
            }
            model.localPosition = pos;
            model.localRotation = rot;

            // ---- Kopf: beim Rennen gesenkt, beim Heulen hoch, sonst leichtes Umsehen
            if (head != null)
            {
                float headPitch = 8f * run + (_windup > 0f ? 14f : 0f);
                float headYaw = Mathf.Sin(_time * 0.7f) * 12f * (1f - run);
                if (_howl > 0f)
                {
                    _howl -= dt;
                    float h = Mathf.Sin(Mathf.Clamp01(1f - _howl / 1.6f) * Mathf.PI);
                    headPitch = -48f * h;
                    headYaw = 0f;
                }
                if (IsDead) { headPitch = 20f; headYaw = 0f; }
                head.localRotation = _headRot * Quaternion.Euler(headPitch, headYaw, 0f);
            }

            // ---- Schwanz: pendelt, beim Rennen gestreckt
            if (tail != null)
            {
                float wag = Mathf.Sin(_time * (3f + 5f * run)) * (14f - 8f * run);
                float lift = -10f + 18f * run;
                if (IsDead) { wag = 0f; lift = -25f; }
                tail.localRotation = _tailRot * Quaternion.Euler(lift, wag, 0f);
            }
        }
    }
}
