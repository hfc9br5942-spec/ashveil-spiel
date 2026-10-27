using Ashveil.Core.Events;
using Ashveil.Data;
using Ashveil.Gameplay.Audio;
using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Prozedural animierter schwerer Zweibeiner (Platzhalter fuer Torfschrat und Glockenhirte):
    /// schwere Schritte mit Beben in der Naehe, Arme pendeln, Glocken schwingen und laeuten,
    /// Ausholen mit beiden Armen (Schlag), Bein heben und Stampfen (Flaechenangriff), Umkippen beim Tod.
    ///
    /// Combat-Masterplan (Kampf-Prototyp Grubenschrat): Angriffe aus dem Moveset bewegen die passenden Glieder
    /// (rechter/linker Arm, Schulterramme, Ansturm), Koerperteile zeigen ihren Zustand (Risse, haengender Arm,
    /// abgefallener Unterarm, Humpeln, Knien beim Niederwurf), der Gesamtzustand zeigt sich in Atmung und Haltung
    /// (Healthy/Wounded/Critical). Beim Tod fallen – je nach Einstellung „Blut und Zerteilung“ – Kopf und Arme ab.
    /// </summary>
    public sealed class EnemyColossusVisual : EnemyVisual
    {
        [SerializeField] private Transform model;
        [SerializeField] private Transform torso;
        [SerializeField] private Transform head;
        [SerializeField] private Transform legL, legR, armL, armR;
        [SerializeField] private Transform[] bells = new Transform[0];
        [SerializeField] private float strideLength = 2.5f;

        [Header("Schritte")]
        [Tooltip("Kamerabeben in diesem Umkreis (m), 0 = keins")]
        [SerializeField] private float stepTremorRadius = 12f;
        [SerializeField] private float stepTremorStrength = 0.08f;
        [SerializeField] private float stepVolume = 0.35f;

        [Header("Glocken")]
        [SerializeField] private bool ringBells;
        [Tooltip("Hoerweite der Glocken (m)")]
        [SerializeField] private float bellDistance = 160f;
        [SerializeField] private float bellInterval = 3.2f;

        private Vector3 _modelPos;
        private Quaternion _modelRot, _torsoRot, _headRot, _legLRot, _legRRot, _armLRot, _armRRot;
        private Quaternion[] _bellRot;
        private float _phase, _time, _lastStepSign;
        private float _windup, _windupTotal;
        private bool _areaWindup;
        private float _slam;   // 1 -> 0 nach dem Schlag
        private float _stomp;  // 1 -> 0 nach dem Stampfer
        private float _death;
        private float _hitShake;
        private float _bellTimer;

        // Kampf-Prototyp
        private EnemyAnatomy _anatomy;
        private bool _anatomyHooked;
        private EnemyMovePose _pose = EnemyMovePose.Default;
        private float _kneel;          // 0..1 Knien (Niederwurf)
        private float _stagger;        // > 0 Taumeln
        private float _breath;
        private bool _armRDestroyed, _deathSplit;
        private Material _woundMat, _debrisMat;
        private int _cracksShown;

        protected override void Awake()
        {
            base.Awake();
            if (model == null) model = transform;
            _modelPos = model.localPosition;
            _modelRot = model.localRotation;
            _torsoRot = torso != null ? torso.localRotation : Quaternion.identity;
            _headRot = head != null ? head.localRotation : Quaternion.identity;
            _legLRot = legL != null ? legL.localRotation : Quaternion.identity;
            _legRRot = legR != null ? legR.localRotation : Quaternion.identity;
            _armLRot = armL != null ? armL.localRotation : Quaternion.identity;
            _armRRot = armR != null ? armR.localRotation : Quaternion.identity;
            _bellRot = new Quaternion[bells.Length];
            for (int i = 0; i < bells.Length; i++) if (bells[i] != null) _bellRot[i] = bells[i].localRotation;
            _bellTimer = Random.Range(0.5f, bellInterval);
        }

        public override void PlayMove(byte moveId, float windup)
        {
            EnemyBrain br = GetComponent<EnemyBrain>();
            EnemyDefinition def = _anatomy != null ? _anatomy.Definition : br != null ? br.Definition : null;
            EnemyAttackDefinition m = def != null ? def.Move(moveId) : null;
            _pose = m != null ? m.pose : EnemyMovePose.Default;
        }

        public override void PlayAttack(Vector3 aimPoint, float windup, bool ranged)
        {
            _windupTotal = Mathf.Max(0.05f, windup);
            _windup = _windupTotal;
            _areaWindup = false;
            SfxPlayer.Play(SfxId.Moan, transform.position + Vector3.up * 2f, 0.8f, 0.05f);
        }

        public override void PlayAreaAttack(Vector3 center, float windup, float radius)
        {
            base.PlayAreaAttack(center, windup, radius);
            _windupTotal = Mathf.Max(0.05f, windup);
            _windup = _windupTotal;
            _areaWindup = true;
            SfxPlayer.Play(SfxId.Moan, transform.position + Vector3.up * 2f, 0.9f, 0.02f);
        }

        public override void PlayAttackResolved(bool hit, Vector3 point)
        {
            _windup = 0f;
            if (_areaWindup)
            {
                _stomp = 1f;
                SfxPlayer.PlayFar(SfxId.Stomp, transform.position, 1f, 90f);
                float r = Mathf.Max(20f, stepTremorRadius * 2f);
                EventBus<CameraImpulseEvent>.Raise(new CameraImpulseEvent(transform.position, 0.7f, r));
            }
            else
            {
                _slam = 1f;
                SfxPlayer.Play(SfxId.HitHeavy, point, 0.9f, 0.1f);
            }
            _areaWindup = false;
        }

        protected override void OnDied()
        {
            _death = Mathf.Max(_death, 0.0001f);
            SfxPlayer.PlayFar(SfxId.Stomp, transform.position, 0.8f, 80f);
        }

        protected override void OnDamaged(Combat.DamageInfo info)
        {
            _hitShake = 0.15f;
            // Wunde: Splitter in der Farbe des Koerpers, bei „Voll“ Flecken am Boden
            EnsureMaterials();
            if (info.Amount > 0f)
            {
                EnemyGore.Burst(info.HitPoint, info.Direction, _debrisMat, 2, 0.09f * model.lossyScale.x, 4f);
                if (Random.value < 0.35f) EnemyGore.Stain(info.HitPoint, _woundMat, 0.5f + Random.value * 0.4f);
            }
        }

        public override void PlayPartBroken(int partIndex, BodyPartState state)
        {
            HookAnatomy();
            if (_anatomy == null || partIndex < 0 || partIndex >= _anatomy.Parts.Count) return;
            EnemyAnatomy.Part p = _anatomy.Parts[partIndex];
            Vector3 at = p.Pivot != null ? p.Pivot.position : transform.position + Vector3.up * 2f;
            EnsureMaterials();
            SfxPlayer.Play(SfxId.HitHeavy, at, 1f, 0.05f);
            SfxPlayer.PlayFar(SfxId.Stomp, at, 0.6f, 60f);
            EnemyGore.Burst(at, transform.forward, _debrisMat, state == BodyPartState.Destroyed ? 10 : 6, 0.14f * model.lossyScale.x, 7f);
            EnemyGore.Stain(at, _woundMat, 1.3f);
            if (state == BodyPartState.Destroyed && p.Pivot == armR) DropForearm();
        }

        private void HookAnatomy()
        {
            if (_anatomyHooked) return;
            _anatomy = GetComponent<EnemyAnatomy>();
            if (_anatomy == null) return;
            _anatomyHooked = true;
            _anatomy.KnockedDown += _ => SfxPlayer.PlayFar(SfxId.Stomp, transform.position, 1f, 90f);
            _anatomy.Staggered += s => _stagger = Mathf.Max(_stagger, s);
            _anatomy.PartStateChanged += (i, st) =>
            {
                // Spaetkommer: ein schon zerstoerter Unterarm ist sofort weg
                if (st == BodyPartState.Destroyed && i < _anatomy.Parts.Count && _anatomy.Parts[i].Pivot == armR) DropForearm();
            };
        }

        private void EnsureMaterials()
        {
            if (_debrisMat != null) return;
            Renderer src = null;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                if (r.name == "Chest" || r.name == "Fist") { src = r; if (r.name == "Chest") break; }
            if (src == null) return;
            EnemyBrain brain = GetComponent<EnemyBrain>();
            Color glow = brain != null && brain.Definition != null ? brain.Definition.glowColor : new Color(0.6f, 0.15f, 0.1f);
            _debrisMat = src.sharedMaterial;
            _woundMat = EnemyGore.Tint(src.sharedMaterial, Color.Lerp(glow, Color.black, 0.55f));
        }

        /// <summary>Rechter Unterarm faellt ab (zerstoert) – einmal.</summary>
        private void DropForearm()
        {
            if (_armRDestroyed || armR == null) return;
            _armRDestroyed = true;
            var chunk = new GameObject("Unterarm (abgetrennt)").transform;
            chunk.SetParent(armR, false);
            chunk.localPosition = new Vector3(0f, -1.2f, 0f);
            for (int i = armR.childCount - 1; i >= 0; i--)
            {
                Transform c = armR.GetChild(i);
                if (c.name == "Fore" || c.name == "Fist") c.SetParent(chunk, true);
            }
            if (chunk.childCount == 0) return;
            if (EnemyGore.Any) EnemyGore.Detach(chunk, transform, transform.right * 2f + Vector3.up * 1.5f);
            else chunk.gameObject.SetActive(false);
        }

        /// <summary>Tod bei „Voll“: Kopf und Arme loesen sich (Zerteilung, Masterplan §33–§35).</summary>
        private void SplitOnDeath()
        {
            if (_deathSplit) return;
            _deathSplit = true;
            if (!EnemyGore.Full) return;
            EnsureMaterials();
            foreach (Transform t in new[] { head, armL, _armRDestroyed ? null : armR })
            {
                if (t == null) continue;
                Vector3 side = (t.position - transform.position);
                side.y = 0f;
                EnemyGore.Burst(t.position, side, _debrisMat, 5, 0.12f * model.lossyScale.x, 8f);
                EnemyGore.Detach(t, transform, side.normalized * 2.2f + Vector3.up * 1.2f);
            }
            EnemyGore.Stain(transform.position + transform.forward * 1.5f, _woundMat, 2.4f, 40f);
        }

        protected override void OnHealthChanged()
        {
            if (!IsDead) _death = 0f;
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            float dt = Time.deltaTime;
            _time += dt;
            float speed = IsDead || Concealment == EnemyConcealment.Submerged ? 0f : Speed;
            float moving = Mathf.Clamp01(speed / 0.8f);
            _phase += speed * dt * (2f * Mathf.PI / Mathf.Max(0.3f, strideLength));

            // ---- Schritte: Beben + dumpfer Aufprall, Glocken
            float stepSign = Mathf.Sign(Mathf.Sin(_phase));
            if (moving > 0.3f && stepSign != _lastStepSign && !IsDead)
            {
                if (stepTremorRadius > 0f)
                    EventBus<CameraImpulseEvent>.Raise(new CameraImpulseEvent(transform.position, stepTremorStrength, stepTremorRadius));
                SfxPlayer.PlayFar(SfxId.Stomp, transform.position, stepVolume, Mathf.Max(40f, stepTremorRadius * 2f));
            }
            _lastStepSign = stepSign;
            if (ringBells && !IsDead)
            {
                _bellTimer -= dt * (0.5f + moving);
                if (_bellTimer <= 0f)
                {
                    _bellTimer = bellInterval * Random.Range(0.7f, 1.3f);
                    Vector3 p = bells.Length > 0 && bells[0] != null ? bells[0].position : transform.position + Vector3.up * 8f;
                    SfxPlayer.PlayFar(SfxId.Bell, p, 1f, bellDistance, 0.06f);
                }
            }

            // ---- Beine und Arme
            float swing = 22f * moving;
            float legA = Mathf.Sin(_phase) * swing;
            float armA = -Mathf.Sin(_phase) * swing * 0.6f;
            float torsoPitch = 6f * moving, torsoRoll = Mathf.Sin(_phase) * 3f * moving;
            float armRaise = 0f, legRaise = 0f, lean = 0f;

            float twist = 0f;
            if (_windup > 0f)
            {
                _windup -= dt;
                float k = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(_windup / _windupTotal));
                if (_areaWindup) { legRaise = -55f * k; lean = -8f * k; }
                else if (_pose == EnemyMovePose.Shoulder) { twist = -35f * k; lean = -6f * k; }
                else if (_pose == EnemyMovePose.Charge) { lean = 18f * k; armRaise = 35f * k; }
                else { armRaise = -140f * k; lean = -10f * k; }
            }
            if (_slam > 0f)
            {
                _slam = Mathf.Max(0f, _slam - dt * 2.5f);
                if (_pose == EnemyMovePose.Shoulder) { twist = 30f * _slam; lean = 22f * _slam; }
                else if (_pose == EnemyMovePose.Charge) { lean = 20f * _slam; }
                else { armRaise = Mathf.Lerp(30f, -140f, 1f - _slam) * _slam; lean = 14f * _slam; }
            }
            if (_stomp > 0f)
            {
                _stomp = Mathf.Max(0f, _stomp - dt * 2f);
                legRaise = 8f * _stomp;
                lean = 6f * _stomp;
            }

            // ---- Koerperteile und Zustand (Kampf-Prototyp)
            HookAnatomy();
            BodyPartState armRState = BodyPartState.Intact, legLState = BodyPartState.Intact, headState = BodyPartState.Intact;
            EnemyDamageState dmg = EnemyDamageState.Healthy;
            bool knocked = false;
            if (_anatomy != null)
            {
                foreach (EnemyAnatomy.Part p in _anatomy.Parts)
                {
                    if (p.Pivot == armR) armRState = p.State;
                    else if (p.Pivot == legL) legLState = p.State;
                    else if (p.Pivot == head) headState = p.State;
                }
                dmg = _anatomy.DamageState;
                if (armRState == BodyPartState.Destroyed && !_armRDestroyed) DropForearm(); // Spaetkommer
                knocked = _anatomy.IsKnockedDown && !IsDead;
                UpdateCracks(dmg, headState);
            }
            _kneel = Mathf.MoveTowards(_kneel, knocked ? 1f : 0f, dt * (knocked ? 2.5f : 1.2f));
            float kneel = Mathf.SmoothStep(0f, 1f, _kneel);
            bool leftArm = _pose == EnemyMovePose.LeftArm;
            bool bothArms = _pose == EnemyMovePose.BothArms || _pose == EnemyMovePose.Default;
            float armRaiseL = leftArm || bothArms ? armRaise : armRaise * 0.25f;
            float armRaiseR = !leftArm ? armRaise : armRaise * 0.25f;
            // Gebrochener rechter Arm haengt schlaff, schwingt kaum mit und holt nicht mehr aus
            float armRHang = armRState >= BodyPartState.Broken ? 1f : 0f;
            if (armRHang > 0f) armRaiseR = 0f;
            // Humpeln: beschaedigtes/gebrochenes linkes Bein schwingt weniger, der Rumpf kippt beim Auftreten
            float limp = legLState >= BodyPartState.Broken ? 1f : legLState == BodyPartState.Damaged ? 0.5f : 0f;
            float legAL = legA * (1f - 0.6f * limp);
            torsoRoll += limp * moving * Mathf.Max(0f, Mathf.Sin(_phase)) * 7f;
            // Atmung und Haltung je Gesamtzustand
            float breathRate = dmg == EnemyDamageState.Critical ? 2.6f : dmg == EnemyDamageState.Wounded ? 1.8f : 1.1f;
            float breathAmp = dmg == EnemyDamageState.Critical ? 3.5f : dmg == EnemyDamageState.Wounded ? 2.2f : 0.8f;
            _breath += dt * breathRate;
            float hunch = dmg == EnemyDamageState.Critical ? 9f : dmg == EnemyDamageState.Wounded ? 4f : 0f;
            float stag = 0f;
            if (_stagger > 0f) { _stagger -= dt; stag = Mathf.Sin(_time * 14f) * 6f; lean -= 10f; }

            if (legL != null) legL.localRotation = _legLRot * Quaternion.Euler(Mathf.Lerp(legAL, -80f, kneel), 0f, 0f);
            if (legR != null) legR.localRotation = _legRRot * Quaternion.Euler(Mathf.Lerp(-legA + legRaise, -35f, kneel), 0f, 0f);
            if (armL != null) armL.localRotation = _armLRot * Quaternion.Euler(Mathf.Lerp(armA + armRaiseL, 25f, kneel * 0.6f), 0f, 0f);
            if (armR != null) armR.localRotation = _armRRot * Quaternion.Euler(Mathf.Lerp(-armA * (1f - 0.8f * armRHang) + armRaiseR, 22f, armRHang), 0f, -14f * armRHang);
            if (torso != null)
                torso.localRotation = _torsoRot * Quaternion.Euler(torsoPitch + lean + hunch + Mathf.Sin(_breath) * breathAmp + 24f * kneel, twist, torsoRoll + stag);
            if (head != null)
                head.localRotation = _headRot * Quaternion.Euler(Mathf.Sin(_time * 0.5f) * 4f + 16f * kneel + hunch * 0.6f, Mathf.Sin(_time * 0.3f) * 10f * (1f - moving), 0f);

            for (int i = 0; i < bells.Length; i++)
            {
                if (bells[i] == null) continue;
                float sway = Mathf.Sin(_time * 1.6f + i * 1.3f) * (6f + 10f * moving) + Mathf.Sin(_phase + i) * 6f * moving;
                bells[i].localRotation = _bellRot[i] * Quaternion.Euler(sway, 0f, sway * 0.4f);
            }

            // ---- Koerper
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.12f * moving * strideLength / 2.5f;
            Vector3 pos = _modelPos + new Vector3(0f, bob - 0.95f * kneel * model.localScale.y, 0.25f * kneel * model.localScale.y);
            Quaternion rot = _modelRot;
            if (_hitShake > 0f)
            {
                _hitShake -= dt;
                rot *= Quaternion.Euler(0f, 0f, Mathf.Sin(_time * 60f) * 2.5f * _hitShake / 0.15f);
            }
            if (IsDead)
            {
                _death = Mathf.Min(1f, _death + dt * 0.8f);
                float d = Mathf.SmoothStep(0f, 1f, _death);
                rot = _modelRot * Quaternion.Euler(80f * d, 0f, 10f * d);
                pos = _modelPos + new Vector3(0f, -0.3f * d, 0.4f * d);
                if (_death > 0.55f) SplitOnDeath();
            }
            model.localPosition = pos;
            model.localRotation = rot;
        }

        /// <summary>Risse: verwundet ein paar auf der Brust, kritisch mehr; gebrochener Kopf bekommt eigene.</summary>
        private void UpdateCracks(EnemyDamageState dmg, BodyPartState headState)
        {
            int want = (dmg == EnemyDamageState.Critical ? 7 : dmg == EnemyDamageState.Wounded ? 3 : 0) + (headState >= BodyPartState.Broken ? 3 : headState == BodyPartState.Damaged ? 1 : 0);
            if (want <= _cracksShown || torso == null) return;
            EnsureMaterials();
            if (_woundMat == null) return;
            var rnd = new System.Random(unchecked((int)(transform.position.x * 73856093f) ^ (int)(transform.position.z * 19349663f)));
            for (int i = 0; i < _cracksShown; i++) rnd.Next();
            while (_cracksShown < want)
            {
                bool onHead = _cracksShown >= (dmg == EnemyDamageState.Critical ? 7 : dmg == EnemyDamageState.Wounded ? 3 : 0) && head != null;
                Transform parent = onHead ? head : torso;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.name = "Riss";
                go.layer = gameObject.layer;
                go.transform.SetParent(parent, false);
                float rx = (float)rnd.NextDouble() - 0.5f, ry = (float)rnd.NextDouble();
                go.transform.localPosition = onHead ? new Vector3(rx * 0.4f, 0.1f + ry * 0.35f, 0.33f) : new Vector3(rx * 1.1f, 0.25f + ry * 1.1f, 0.53f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, (float)rnd.NextDouble() * 180f);
                go.transform.localScale = new Vector3(0.05f, 0.25f + (float)rnd.NextDouble() * 0.35f, 0.03f);
                go.GetComponent<Renderer>().sharedMaterial = _woundMat;
                _cracksShown++;
            }
        }
    }
}