using Ashveil.Gameplay.Audio;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using UnityEngine;

namespace Ashveil.Gameplay.Enemies
{
    /// <summary>
    /// Menschliche Gegner (Verblichene): Humanoid-Modell mit der Animationsbibliothek der Spieler.
    /// Treibt dieselben Animator-Parameter wie der Spieler (Speed, WeaponType, Attack, Hit, Dead)
    /// und haengt die Waffe per Handgeometrie an die rechte Hand.
    /// Pistolenschuetzen zeigen beim Zielen eine Warnung und beim Schuss Muendungsfeuer + Leuchtspur. Seit 07.10.2026 tragen die
    /// Schuetzinnen einen Bogen (Max: „Bogen statt Pistole“, Feuerwaffen ruhen auch bei Gegnern): hoerbares Spannen, sichtbarer
    /// Pfeilflug, kein Muendungsfeuer. Treffer und Zeitpunkt rechnet weiter der Server (nur Darstellung).
    /// </summary>
    public sealed class EnemyHumanoidVisual : EnemyVisual
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int WeaponTypeId = Animator.StringToHash("WeaponType");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int AttackIndexId = Animator.StringToHash("AttackIndex");
        private static readonly int HitId = Animator.StringToHash("Hit");
        private static readonly int DeadId = Animator.StringToHash("Dead");

        [SerializeField] private Animator animator;
        [Tooltip("Waffe (Drehpunkt = Griff, Klinge/Lauf zeigt nach +Z)")]
        [SerializeField] private Transform weapon;
        [SerializeField] private bool ranged;
        [Tooltip("Bogen statt Pistole (Max 07.10.2026): Spannen, Pfeilflug, kein Muendungsfeuer")]
        [SerializeField] private bool bow;
        [SerializeField] private Material tracerMaterial;

        private int _attackIndex;
        private LineRenderer _tracer;
        private float _tracerTime;
        private Light _flash;
        private bool _attached;
        private LineRenderer _arrow;
        private Vector3 _arrowFrom, _arrowTo;
        private float _arrowT = -1f, _arrowDuration;
        private bool _arrowHit;

        private void Start()
        {
            if (animator != null)
            {
                animator.SetInteger(WeaponTypeId, ranged && !bow ? 2 : 1); // Bogen: Haltung wie eine Klinge, Wurfarme aus der Faust
                animator.SetBool(GroundedId, true);
            }
            AttachWeapon();
        }

        private void AttachWeapon()
        {
            if (_attached || weapon == null || animator == null || !animator.isHuman) return;
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand == null) return;
            CharacterBodySwitcher.AttachToHand(weapon, animator, hand, Vector3.zero, Vector3.zero, !ranged || bow);
            _attached = true;
        }

        public override void PlayAttack(Vector3 aimPoint, float windup, bool isRanged)
        {
            if (animator == null) return;
            if (isRanged)
            {
                // Zielen: kurzer Moment, dann Schuss (Animation kommt mit dem Schuss)
                if (bow) SfxPlayer.Play(SfxId.BowDraw, transform.position + Vector3.up * 1.3f, 0.5f, 0.05f);
                else SfxPlayer.Play(SfxId.Reload, transform.position + Vector3.up * 1.3f, 0.35f, 0.05f);
            }
            else
            {
                animator.SetInteger(AttackIndexId, _attackIndex);
                _attackIndex = (_attackIndex + 1) % 3;
                animator.SetTrigger(AttackId);
                SfxPlayer.Play(SfxId.SwingHeavy, transform.position + Vector3.up * 1.2f, 0.6f, 0.1f);
            }
        }

        public override void PlayAttackResolved(bool hit, Vector3 point)
        {
            if (!ranged) return;
            if (bow)
            {
                Vector3 nock = weapon != null ? weapon.position : transform.position + Vector3.up * 1.4f + transform.forward * 0.4f;
                SfxPlayer.Play(SfxId.BowRelease, nock, 0.8f, 0.06f);
                ShootArrow(nock, point, hit);
                return;
            }
            if (animator != null) animator.SetTrigger(AttackId);
            Vector3 muzzle = weapon != null ? weapon.position + weapon.forward * 0.3f : transform.position + Vector3.up * 1.4f + transform.forward * 0.5f;
            SfxPlayer.Play(SfxId.Gunshot, muzzle, 0.9f, 0.06f);
            ShowTracer(muzzle, point);
        }

        public override void PlayCall()
        {
            SfxPlayer.Play(SfxId.Moan, transform.position + Vector3.up * 1.6f, 0.8f, 0.1f);
        }

        protected override void OnDied()
        {
            if (animator != null) animator.SetBool(DeadId, true);
            SfxPlayer.Play(SfxId.Moan, transform.position + Vector3.up * 1.2f, 0.6f, 0.2f);
        }

        // 22.10.2026 (Max, COMBAT-026a/b): kritischer Todesstoß einer Hiebwaffe zerstückelt – das ist dann die Todesanimation.
        // Den Trennpunkt wählt der Server aus Trefferhöhe/-seite (Kopf, Arm, Bein) und schickt ihn mit dem Treffer.
        public override byte TrennpunktFuer(Vector3 punkt, Vector3 richtung) => (byte)Zerstueckelung.Waehle(animator, punkt, richtung);

        protected override void Trenne(DamageInfo info, bool ruhig = false) =>
            Zerstueckelung.Ausfuehren(animator, (Zerstueckelung.Trennpunkt)info.Trennpunkt, info.Direction, transform, ruhig);

        protected override void OnDamaged(DamageInfo info)
        {
            if (animator != null && !IsDead) animator.SetTrigger(HitId);
        }

        protected override void OnHealthChanged()
        {
            if (animator != null && !IsDead) animator.SetBool(DeadId, false);
            if (!IsDead && animator != null) Zerstueckelung.Wiederherstellen(animator.gameObject);   // Wiederbelebung/Respawn: wieder ganz
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (animator != null) animator.SetFloat(SpeedId, IsDead ? 0f : Speed, 0.1f, Time.deltaTime);

            if (_tracer != null && _tracer.enabled)
            {
                _tracerTime -= Time.deltaTime;
                float a = Mathf.Clamp01(_tracerTime / 0.12f);
                _tracer.startColor = new Color(1f, 0.85f, 0.5f, a);
                _tracer.endColor = new Color(1f, 0.6f, 0.3f, a * 0.3f);
                if (_tracerTime <= 0f) _tracer.enabled = false;
            }
            if (_flash != null && _flash.enabled)
            {
                _flash.intensity = Mathf.Max(0f, _flash.intensity - Time.deltaTime * 60f);
                if (_flash.intensity <= 0.01f) _flash.enabled = false;
            }
            if (_arrowT >= 0f) UpdateArrow(Time.deltaTime);
        }

        /// <summary>Pfeil als kurzer Strich vom Bogen zum Ziel (etwa 45 m/s), Schaft dunkel, Befiederung hell.</summary>
        private void ShootArrow(Vector3 from, Vector3 to, bool hit)
        {
            if (_arrow == null)
            {
                var go = new GameObject("Arrow");
                go.transform.SetParent(transform, false);
                _arrow = go.AddComponent<LineRenderer>();
                _arrow.useWorldSpace = true;
                _arrow.positionCount = 2;
                _arrow.widthMultiplier = 0.03f;
                _arrow.numCapVertices = 1;
                _arrow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _arrow.receiveShadows = false;
                if (tracerMaterial != null) _arrow.sharedMaterial = tracerMaterial;
                _arrow.startColor = new Color(0.3f, 0.22f, 0.14f, 1f);
                _arrow.endColor = new Color(0.8f, 0.76f, 0.68f, 1f);
            }
            _arrowFrom = from;
            _arrowTo = to;
            _arrowHit = hit;
            _arrowDuration = Mathf.Clamp(Vector3.Distance(from, to) / 45f, 0.06f, 0.45f);
            _arrowT = 0f;
            UpdateArrow(0f);
            _arrow.enabled = true;
        }

        private void UpdateArrow(float dt)
        {
            _arrowT += dt / Mathf.Max(0.01f, _arrowDuration);
            float t = Mathf.Clamp01(_arrowT);
            Vector3 d = _arrowTo - _arrowFrom;
            float len = d.magnitude;
            Vector3 dir = len > 1e-4f ? d / len : transform.forward;
            Vector3 head = Vector3.Lerp(_arrowFrom, _arrowTo, t);
            _arrow.SetPosition(0, head);
            _arrow.SetPosition(1, head - dir * Mathf.Min(0.75f, len * t + 0.05f));
            if (_arrowT < 1f) return;
            _arrowT = -1f;
            _arrow.enabled = false;
            if (_arrowHit) SfxPlayer.Play(SfxId.ArrowHit, _arrowTo, 0.7f, 0.08f);
        }

        private void ShowTracer(Vector3 from, Vector3 to)
        {
            if (_tracer == null)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                _tracer = go.AddComponent<LineRenderer>();
                _tracer.useWorldSpace = true;
                _tracer.positionCount = 2;
                _tracer.widthMultiplier = 0.035f;
                _tracer.numCapVertices = 2;
                _tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _tracer.receiveShadows = false;
                if (tracerMaterial != null) _tracer.sharedMaterial = tracerMaterial;

                var lightGo = new GameObject("Muzzle Flash");
                lightGo.transform.SetParent(transform, false);
                _flash = lightGo.AddComponent<Light>();
                _flash.type = LightType.Point;
                _flash.range = 6f;
                _flash.color = new Color(1f, 0.7f, 0.35f);
                _flash.shadows = LightShadows.None;
                _flash.enabled = false;
            }
            _tracer.SetPosition(0, from);
            _tracer.SetPosition(1, to);
            _tracer.enabled = true;
            _tracerTime = 0.12f;
            _flash.transform.position = from;
            _flash.intensity = 5f;
            _flash.enabled = true;
        }
    }
}
