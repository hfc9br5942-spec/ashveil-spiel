using Ashveil.Core.Events;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Laesst ein grosses Wesen langsam eine Route abschreiten – mit schweren Schritten,
    /// die man in der Naehe als leichte Erschuetterung spuert.
    ///
    /// Milestone 1: rein atmosphaerischer Platzhalter fuer den Glockenhirten.
    /// Ab Milestone 5 uebernimmt die serverseitige Gegner-KI; dieses Script bleibt dann
    /// fuer reine Kulissen-Kreaturen (z. B. Wesen am Horizont) erhalten.
    /// </summary>
    public sealed class WaypointWalker : MonoBehaviour
    {
        [SerializeField] private Vector3[] waypoints = new Vector3[0];
        [SerializeField] private float speed = 1.6f;
        [SerializeField] private float turnDegreesPerSecond = 25f;
        [Tooltip("Pause an jedem Wegpunkt (Sekunden)")]
        [SerializeField] private float pauseAtWaypoint = 6f;

        [Header("Schritte")]
        [SerializeField] private float stepInterval = 1.5f;
        [SerializeField] private float stepBob = 0.35f;
        [SerializeField] private float tremorStrength = 0.35f;
        [SerializeField] private float tremorRadius = 70f;

        [Header("Boden folgen")]
        [SerializeField] private bool followGround = true;
        [SerializeField] private LayerMask groundMask = ~0;

        private int _index;
        private float _pauseTimer;
        private float _stepTimer;
        private float _baseY;

        private void Start()
        {
            _baseY = transform.position.y;
        }

        private void Update()
        {
            if (waypoints == null || waypoints.Length == 0) return;
            float dt = Time.deltaTime;

            if (_pauseTimer > 0f)
            {
                _pauseTimer -= dt;
                return;
            }

            Vector3 pos = transform.position;
            Vector3 goal = waypoints[_index];
            Vector3 flat = new Vector3(goal.x - pos.x, 0f, goal.z - pos.z);

            if (flat.magnitude < 1f)
            {
                _index = (_index + 1) % waypoints.Length;
                _pauseTimer = pauseAtWaypoint;
                return;
            }

            // Erst drehen, dann gehen – grosse Wesen wenden langsam.
            Quaternion look = Quaternion.LookRotation(flat.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnDegreesPerSecond * dt);
            float facing = Vector3.Angle(transform.forward, flat);
            float moveFactor = Mathf.Clamp01(1f - facing / 45f);

            pos += transform.forward * (speed * moveFactor * dt);

            // Schrittrhythmus: Heben und Senken, Erschuetterung beim Aufsetzen.
            _stepTimer += dt * moveFactor;
            float phase = _stepTimer / Mathf.Max(0.1f, stepInterval);
            if (phase >= 1f)
            {
                _stepTimer -= stepInterval;
                EventBus<CameraImpulseEvent>.Raise(new CameraImpulseEvent(pos, tremorStrength, tremorRadius));
            }

            float groundY = _baseY;
            if (followGround && Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 200f,
                    groundMask, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
            }

            pos.y = groundY + Mathf.Sin(Mathf.Clamp01(phase) * Mathf.PI) * stepBob * moveFactor;
            transform.position = pos;
        }

        /// <summary>Route vom Editor-Werkzeug setzen.</summary>
        public void SetWaypoints(Vector3[] points)
        {
            waypoints = points;
            _index = 0;
        }

        private void OnDrawGizmosSelected()
        {
            if (waypoints == null || waypoints.Length < 2) return;
            Gizmos.color = new Color(1f, 0.5f, 0.1f);
            for (int i = 0; i < waypoints.Length; i++)
                Gizmos.DrawLine(waypoints[i], waypoints[(i + 1) % waypoints.Length]);
        }
    }
}
