using UnityEngine;

namespace Ashveil.Gameplay.Npcs
{
    /// <summary>
    /// M8.5: Handkarren eines NPCs (z. B. des Haendlers). Wird von <see cref="NpcRoutine"/> gesetzt – beim Schieben vor dem
    /// NPC, sonst am Platz des letzten Abschnitts mit Karren. Die Position ergibt sich damit wie der Tagesablauf rein aus
    /// der Weltuhr (auf allen Rechnern gleich). Raeder drehen sich passend zur zurueckgelegten Strecke. Kein Collider
    /// (blockiert niemanden), die Hoehe folgt dem Gelaende.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcCart : MonoBehaviour
    {
        [Tooltip("Abstand Karrenmitte vor dem NPC (m) – 0 bei Fuhrwerken (der Fahrer sitzt am Ursprung)")]
        [SerializeField] private float reach = 1.35f;
        [Tooltip("Sitzhoehe des Kutschbocks (m) fuer Fahrer von Fuhrwerken")]
        [SerializeField] private float seat = 1.05f;
        [SerializeField] private Transform[] wheels = System.Array.Empty<Transform>();
        [SerializeField] private float wheelRadius = 0.35f;
        [Tooltip("M8.6: Zugtier (geht, wenn der Wagen faehrt)")]
        [SerializeField] private AnimalVisual draft;
        [Tooltip("M8.6: Aufbau, der beim Fahren leicht wippt")]
        [SerializeField] private Transform body;

        public float Reach => reach;
        public float Seat => seat;
        /// <summary>Aktuelle Geschwindigkeit (m/s, geglaettet) – Tests/Anzeige.</summary>
        public float Speed => _speed;
        public AnimalVisual Draft => draft;

        private float _speed, _bob;
        private Vector3 _bodyBase;
        private bool _bodyBaseSet;

        private Vector3 _last;
        private bool _has;
        private float _roll;
        private Quaternion[] _wheelBase;

        public void Configure(Transform[] wheelParts, float radius)
        {
            wheels = wheelParts ?? System.Array.Empty<Transform>();
            wheelRadius = Mathf.Max(0.05f, radius);
        }

        /// <summary>Fuhrwerk: Fahrer sitzt am Ursprung (reach 0) auf dem Kutschbock, Zugtier und wippender Aufbau.</summary>
        public void ConfigureWagon(float seatHeight, AnimalVisual draftAnimal, Transform wagonBody)
        {
            reach = 0f;
            seat = seatHeight;
            draft = draftAnimal;
            body = wagonBody;
        }

        public void Place(Vector3 pos, Quaternion rot)
        {
            if (Ashveil.Core.WorldGround.TrySample(pos, out float gh)) pos.y = gh;
            float step = 0f;
            if (_has)
            {
                Vector3 d = pos - _last;
                d.y = 0f;
                float moved = Vector3.Dot(d, rot * Vector3.forward);
                if (Mathf.Abs(moved) < 3f)
                {
                    _roll += moved / wheelRadius * Mathf.Rad2Deg; // Spruenge (Zeitsprung) nicht mitdrehen
                    step = Mathf.Abs(moved);
                }
            }
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            _speed = Mathf.Lerp(_speed, step / dt, 1f - Mathf.Exp(-dt * 6f));
            if (draft != null) draft.Set(_speed > 0.25f ? AnimalPose.Walk : AnimalPose.Stand, _speed);
            if (body != null)
            {
                if (!_bodyBaseSet) { _bodyBase = body.localPosition; _bodyBaseSet = true; }
                _bob += step * 2.1f;
                float amp = Mathf.Clamp01(_speed / 1.5f);
                body.localPosition = _bodyBase + Vector3.up * (Mathf.Sin(_bob) * 0.025f * amp);
                body.localRotation = Quaternion.Euler(Mathf.Sin(_bob * 0.5f) * 0.8f * amp, 0f, Mathf.Sin(_bob * 0.37f + 1.3f) * 1.2f * amp);
            }
            _last = pos;
            _has = true;
            transform.SetPositionAndRotation(pos, rot);
            if (wheels.Length == 0) return;
            if (_wheelBase == null || _wheelBase.Length != wheels.Length)
            {
                _wheelBase = new Quaternion[wheels.Length];
                for (int i = 0; i < wheels.Length; i++) _wheelBase[i] = wheels[i] != null ? wheels[i].localRotation : Quaternion.identity;
            }
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i] != null) wheels[i].localRotation = Quaternion.Euler(_roll, 0f, 0f) * _wheelBase[i];
        }
    }
}
