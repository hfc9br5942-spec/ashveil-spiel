using Ashveil.Core;
using Ashveil.Core.Events;
using Ashveil.Gameplay.Characters;
using UnityEngine;

namespace Ashveil.Gameplay.CameraSystem
{
    /// <summary>
    /// Klassische MMO-Verfolgerkamera.
    ///
    /// - Umkreist den Charakter (Yaw/Pitch) per Maus, Zoom per Mausrad (weich).
    /// - Laeuft der Charakter ohne Maussteuerung, schwenkt die Kamera sanft hinter ihn.
    /// - Kollision per SphereCast: rueckt sofort heran, faehrt langsam wieder heraus.
    /// - Leichte Hoehenglaettung, damit Treppen nicht ruckeln.
    /// - Erschuetterung ueber <see cref="CameraImpulseEvent"/> (sparsam).
    ///
    /// Die Eingabe kommt von aussen (<see cref="HandleLook"/>), damit dieselbe Kamera spaeter
    /// auch Zwischensequenzen, Gamepad oder Zuschauer-Modus bedienen kann.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class MMOCameraController : MonoBehaviour
    {
        [Header("Ziel")]
        [SerializeField] private Transform target;
        [SerializeField] private CharacterMotor targetMotor;
        [Tooltip("Hoehe des Drehpunkts ueber den Fuessen (etwa Schulterhoehe)")]
        [SerializeField] private float pivotHeight = 1.65f;
        [SerializeField] private float pivotVerticalSmoothTime = 0.08f;

        [Header("Drehen")]
        [Tooltip("Grad pro Pixel Mausbewegung")]
        [SerializeField] private float mouseSensitivity = 0.15f;
        [SerializeField] private bool invertY;
        [SerializeField] private float minPitch = -45f;
        [SerializeField] private float maxPitch = 80f;
        [SerializeField] private float initialPitch = 14f;

        [Header("Zoom")]
        [SerializeField] private float minDistance = 1.2f;
        [SerializeField] private float maxDistance = 24f;
        [SerializeField] private float initialDistance = 7f;
        [Tooltip("Zoomschritt relativ zur aktuellen Distanz (0.15 = 15 %)")]
        [SerializeField] private float zoomStepFraction = 0.15f;
        [SerializeField] private float zoomSharpness = 10f;

        [Header("Automatisch hinter den Charakter schwenken")]
        [SerializeField] private bool autoFollow = true;
        [SerializeField] private float autoFollowSmoothTime = 0.35f;

        [Header("Kollision")]
        [SerializeField] private LayerMask collisionMask = 0;
        [SerializeField] private float collisionRadius = 0.25f;
        [SerializeField] private float collisionPadding = 0.08f;
        [SerializeField] private float minCollisionDistance = 0.4f;
        [Tooltip("Wie schnell die Kamera nach einer Kollision wieder herausfaehrt (m/s)")]
        [SerializeField] private float collisionRecoverSpeed = 6f;

        [Header("Erschuetterung")]
        [SerializeField] private float maxShakeOffset = 0.22f;
        [SerializeField] private float maxShakeAngle = 1.2f;
        [SerializeField] private float shakeDecay = 1.6f;
        [SerializeField] private float shakeFrequency = 18f;

        private float _yaw;
        private float _pitch;
        private float _desiredDistance;
        private float _zoomDistance;
        private float _currentDistance;
        private float _yawVelocity;
        private float _pivotY;
        private float _pivotYVelocity;
        private float _lastTargetYaw;
        private bool _mouseLookThisFrame;
        private float _trauma;

        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public Transform Target => target;

        private void Awake()
        {
            if (collisionMask.value == 0) collisionMask = GameLayers.CameraCollisionMask();
            _pitch = initialPitch;
            _desiredDistance = _zoomDistance = _currentDistance = Mathf.Clamp(initialDistance, minDistance, maxDistance);
            if (target != null) SetTarget(target, targetMotor);
        }

        private void OnEnable()
        {
            EventBus<CameraImpulseEvent>.Subscribe(OnImpulse);
        }

        private void OnDisable()
        {
            EventBus<CameraImpulseEvent>.Unsubscribe(OnImpulse);
        }

        /// <summary>Zusaetzliche Hoehe des Drehpunkts (m) – z. B. im Sattel (Reittier).</summary>
        public float PivotBoost { get; set; }

        private Vector3 _framePoint;
        private float _frameUntil;

        /// <summary>
        /// Kurzer Bildausschnitt fuer Szenen (Vorgabe: nur kurz, keine Kamerafahrt): schwenkt die Kamera fuer ein paar Sekunden
        /// sanft so, dass Figur und <paramref name="worldPoint"/> im Bild sind. Maus-Blick des Spielers hat immer Vorrang.
        /// </summary>
        public void FrameTowards(Vector3 worldPoint, float seconds)
        {
            _framePoint = worldPoint;
            _frameUntil = Time.time + Mathf.Clamp(seconds, 0.5f, 4f);
        }

        public bool Framing => Time.time < _frameUntil;

        /// <summary>Neues Ziel setzen und Kamera sofort dahinter positionieren.</summary>
        public void SetTarget(Transform newTarget, CharacterMotor motor)
        {
            target = newTarget;
            targetMotor = motor;
            if (target == null) return;
            _yaw = target.eulerAngles.y;
            _lastTargetYaw = _yaw;
            _pivotY = target.position.y + pivotHeight;
            _pivotYVelocity = 0f;
            _yawVelocity = 0f;
            ApplyTransform(0f, true);
        }

        /// <summary>
        /// Mauseingabe verarbeiten. Wird VOR der Charakterbewegung aufgerufen, damit
        /// "rechte Maustaste = Charakter schaut in Kamerarichtung" im selben Frame greift.
        /// </summary>
        public void HandleLook(Vector2 deltaPixels, bool mouseLookActive, float zoomSteps)
        {
            _mouseLookThisFrame = mouseLookActive;
            if (mouseLookActive)
            {
                _yaw = Mathf.Repeat(_yaw + deltaPixels.x * mouseSensitivity, 360f);
                float dy = deltaPixels.y * mouseSensitivity * (invertY ? 1f : -1f);
                _pitch = Mathf.Clamp(_pitch + dy, minPitch, maxPitch);
                _yawVelocity = 0f;
            }

            if (Mathf.Abs(zoomSteps) > 0f)
            {
                float step = Mathf.Max(0.5f, _desiredDistance * zoomStepFraction);
                _desiredDistance = Mathf.Clamp(_desiredDistance - zoomSteps * step, minDistance, maxDistance);
            }
        }

        private void LateUpdate()
        {
            if (target == null) return;
            ApplyTransform(Time.deltaTime, false);
        }

        private void ApplyTransform(float dt, bool snap)
        {
            float targetYaw = target.eulerAngles.y;

            // Sanft hinter den Charakter schwenken, wenn er sich bewegt oder mit A/D dreht.
            if (autoFollow && !_mouseLookThisFrame && !snap)
            {
                bool moving = targetMotor != null && targetMotor.HorizontalSpeed > 0.3f;
                bool turning = Mathf.Abs(Mathf.DeltaAngle(_lastTargetYaw, targetYaw)) > 0.01f;
                bool movingBackward = targetMotor != null && targetMotor.LocalVelocity.z < -0.3f;
                if ((moving || turning) && !movingBackward)
                    _yaw = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity, autoFollowSmoothTime, Mathf.Infinity, dt);
            }
            _lastTargetYaw = targetYaw;
            if (!snap && !_mouseLookThisFrame && Time.time < _frameUntil)
            {
                Vector3 to = _framePoint - target.position;
                to.y = 0f;
                if (to.sqrMagnitude > 1f)
                {
                    // leicht seitlich versetzt hinter der Figur, damit beide zu sehen sind
                    float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - 18f;
                    _yaw = Mathf.SmoothDampAngle(_yaw, want, ref _yawVelocity, 0.7f, 120f, dt);
                }
            }

            // Drehpunkt: horizontal exakt, vertikal leicht geglaettet (Treppen, Landungen).
            float rawPivotY = target.position.y + pivotHeight + PivotBoost;
            _pivotY = snap ? rawPivotY : Mathf.SmoothDamp(_pivotY, rawPivotY, ref _pivotYVelocity, pivotVerticalSmoothTime, Mathf.Infinity, dt);
            // Nie zu weit hinterherhaengen (z. B. bei Teleport oder hohem Fall).
            if (Mathf.Abs(_pivotY - rawPivotY) > 2f) _pivotY = rawPivotY;
            Vector3 pivot = new Vector3(target.position.x, _pivotY, target.position.z);

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // Zoom glaetten.
            _zoomDistance = snap ? _desiredDistance : Mathf.Lerp(_zoomDistance, _desiredDistance, 1f - Mathf.Exp(-zoomSharpness * dt));

            // Kollision.
            float allowed = _zoomDistance;
            Vector3 back = rotation * Vector3.back;
            if (Physics.SphereCast(pivot, collisionRadius, back, out RaycastHit hit, _zoomDistance,
                    collisionMask, QueryTriggerInteraction.Ignore))
            {
                allowed = Mathf.Max(minCollisionDistance, hit.distance - collisionPadding);
            }

            if (snap || allowed < _currentDistance) _currentDistance = allowed; // sofort heran
            else _currentDistance = Mathf.MoveTowards(_currentDistance, allowed, collisionRecoverSpeed * dt);

            Vector3 position = pivot + back * _currentDistance;

            // Erschuetterung.
            if (_trauma > 0f && !snap)
            {
                float shake = _trauma * _trauma;
                float t = Time.time * shakeFrequency;
                Vector3 offset = new Vector3(
                    Mathf.PerlinNoise(t, 0.1f) - 0.5f,
                    Mathf.PerlinNoise(0.2f, t) - 0.5f,
                    0f) * (2f * maxShakeOffset * shake);
                position += rotation * offset;
                rotation *= Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(t, t) - 0.5f) * 2f * maxShakeAngle * shake);
                _trauma = Mathf.Max(0f, _trauma - shakeDecay * dt);
            }

            transform.SetPositionAndRotation(position, rotation);
            UnderwaterView.Apply(position);   // Unterwassersicht, wenn die Kamera unter einem Wasserspiegel liegt (MOV-169)
        }

        private void OnImpulse(CameraImpulseEvent evt)
        {
            if (evt.Radius <= 0f) return;
            float distance = Vector3.Distance(transform.position, evt.Origin);
            if (distance >= evt.Radius) return;
            float falloff = 1f - distance / evt.Radius;
            _trauma = Mathf.Clamp01(_trauma + evt.Strength * falloff * falloff);
        }

        private void OnValidate()
        {
            minDistance = Mathf.Max(0.3f, minDistance);
            maxDistance = Mathf.Max(minDistance, maxDistance);
            initialDistance = Mathf.Clamp(initialDistance, minDistance, maxDistance);
            maxPitch = Mathf.Clamp(maxPitch, minPitch, 89f);
        }
    }
}
