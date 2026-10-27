using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// Gibt dem Kapsel-Platzhalter Koerpersprache, solange es noch keine Animationen gibt:
    /// Neigung in Laufrichtung, leichtes Wippen, Streckung beim Sprung, Stauchung bei der Landung.
    ///
    /// Wird entfernt bzw. deaktiviert, sobald ein animiertes Modell (Milestone 2) da ist.
    /// Arbeitet nur auf dem Kind-Transform "Visual", nie auf dem Charakter selbst.
    /// </summary>
    public sealed class PlaceholderBodyMotion : MonoBehaviour
    {
        [SerializeField] private CharacterMotor motor;
        [SerializeField] private Transform visual;

        [Header("Neigung")]
        [SerializeField] private float maxForwardLean = 9f;
        [SerializeField] private float maxSideLean = 7f;
        [SerializeField] private float leanSharpness = 10f;

        [Header("Wippen")]
        [SerializeField] private float bobHeight = 0.05f;
        [SerializeField] private float bobStepsPerMeter = 0.75f;

        [Header("Stauchen / Strecken")]
        [SerializeField] private float springStiffness = 180f;
        [SerializeField] private float springDamping = 14f;
        [SerializeField] private float jumpStretch = 0.18f;
        [SerializeField] private float landSquashPerSpeed = 0.022f;
        [SerializeField] private float maxSquash = 0.3f;

        private Vector3 _lean;
        private float _bobPhase;
        private float _squash;
        private float _squashVelocity;
        private Vector3 _baseLocalPosition;

        private void Awake()
        {
            if (motor == null) motor = GetComponentInParent<CharacterMotor>();
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual != null) _baseLocalPosition = visual.localPosition;
        }

        private void OnEnable()
        {
            if (motor == null) return;
            motor.Jumped += OnJumped;
            motor.Landed += OnLanded;
        }

        private void OnDisable()
        {
            if (motor == null) return;
            motor.Jumped -= OnJumped;
            motor.Landed -= OnLanded;
        }

        private void OnJumped()
        {
            _squashVelocity += jumpStretch * 12f;
        }

        private void OnLanded(float impactSpeed)
        {
            _squashVelocity -= Mathf.Min(maxSquash, impactSpeed * landSquashPerSpeed) * 12f;
        }

        private void LateUpdate()
        {
            if (motor == null || visual == null) return;
            float dt = Time.deltaTime;
            MotorState state = motor.State;
            CharacterMotorSettings s = motor.Settings;

            // Neigung aus lokaler Geschwindigkeit.
            Vector3 local = motor.LocalVelocity;
            float speedRef = Mathf.Max(0.01f, s.sprintSpeed);
            var targetLean = new Vector3(
                Mathf.Clamp(local.z / speedRef, -1f, 1f) * maxForwardLean,
                0f,
                -Mathf.Clamp(local.x / speedRef, -1f, 1f) * maxSideLean);
            if (!state.Grounded) targetLean *= 0.4f;
            _lean = Vector3.Lerp(_lean, targetLean, 1f - Mathf.Exp(-leanSharpness * dt));

            // Wippen proportional zur zurueckgelegten Strecke.
            float bob = 0f;
            if (state.Grounded && motor.HorizontalSpeed > 0.2f)
            {
                _bobPhase += motor.HorizontalSpeed * bobStepsPerMeter * dt * Mathf.PI * 2f;
                bob = Mathf.Abs(Mathf.Sin(_bobPhase)) * bobHeight * Mathf.Clamp01(motor.HorizontalSpeed / s.runSpeed);
            }

            // Feder fuer Stauchen/Strecken.
            float accel = -springStiffness * _squash - springDamping * _squashVelocity;
            _squashVelocity += accel * dt;
            _squash = Mathf.Clamp(_squash + _squashVelocity * dt, -maxSquash, maxSquash);

            float sy = 1f + _squash;
            float sxz = 1f / Mathf.Sqrt(Mathf.Max(0.1f, sy)); // Volumen grob erhalten
            visual.localScale = new Vector3(sxz, sy, sxz);
            visual.localRotation = Quaternion.Euler(_lean);
            visual.localPosition = _baseLocalPosition + Vector3.up * bob;
        }
    }
}
