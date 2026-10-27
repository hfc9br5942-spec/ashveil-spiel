using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Story;
using UnityEngine;

namespace Ashveil.Gameplay.Mounts
{
    /// <summary>
    /// Klassenreittier sichtbar machen (Greybox, Max 28.09.: spaeter eigenes Blender-Modell): Kriegspferd aus Grundkoerpern
    /// unter der geglaetteten Grafik der Figur, Reiter im Sattel (Fahr-Haltung aus dem Emote-Layer, Modell angehoben),
    /// Gangart aus dem Tempo (Schritt, Trab, Galopp), Kamera etwas hoeher. Auf allen Rechnern gleich; ob jemand aufsitzt,
    /// entscheidet der Server (SyncVar im Netzwerk-Teil).
    /// </summary>
    [DefaultExecutionOrder(80)]
    [DisallowMultipleComponent]
    public sealed class MountVisual : MonoBehaviour
    {
        public const float SaddleHeight = 1.02f;
        private const float CameraBoost = 0.9f;

        private CharacterMotor _motor;
        private Transform _horse;
        private Animator _riderAnim;
        private EmotePoser _riderPose;
        private Vector3 _riderBase;
        private bool _riderRaised;
        private bool _visible;
        private float _speed, _mountBlend;
        private Vector3 _lastPos;
        private MMOCameraBoostTarget _camera;

        /// <summary>Sitzt die Figur sichtbar auf (Tests: auch auf fremden Rechnern)?</summary>
        public bool Visible => _visible;
        /// <summary>Gangart fuer Tests/Anzeige: 0 Stand, 1 Schritt, 2 Trab, 3 Galopp.</summary>
        public int Gait => _speed < 0.3f ? 0 : _speed < 3.2f ? 1 : _speed < 8f ? 2 : 3;

        public static MountVisual Ensure(GameObject player)
        {
            var v = player.GetComponent<MountVisual>();
            return v != null ? v : player.AddComponent<MountVisual>();
        }

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor>();
            _lastPos = transform.position;
        }

        /// <summary>Auf-/Absitzen darstellen.</summary>
        public void SetVisible(bool on)
        {
            if (on == _visible) return;
            _visible = on;
            if (on)
            {
                if (_horse == null) Build();
                _horse.gameObject.SetActive(true);
                _mountBlend = 0f;
            }
            else
            {
                if (_horse != null) _horse.gameObject.SetActive(false);
                ReleaseRider();
            }
        }

        private void OnDisable() => ReleaseRider();

        // ================================================================== Aufbau (Greybox)

        /// <summary>
        /// Das Klassenreittier des Eidwaechters IST das Pferd aus „Wo die Herde floh“: feste Identitaet (Braunschwarz,
        /// gebrochene Blesse, Narbe rechte Schulter), mit dem alten Militaersattel vom Pferdehof.
        /// </summary>
        private void Build()
        {
            Transform parent = transform.Find("Graphics");
            if (parent == null) parent = transform;
            HorseLook look = HorseLook.FromIdentity(Ashveil.Data.HorseStoryCatalog.Warhorse, false, true);
            look.OldHarness = true;
            _rig = HorseRig.Create(parent, "Kriegspferd (Greybox)", look);
            _rig.AutoAnimate = false;
            _horse = _rig.Root;
            _horse.gameObject.SetActive(false);
        }

        private HorseRig _rig;

        // ================================================================== Ablauf

        private void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 p = transform.position;
            Vector3 d = p - _lastPos;
            d.y = 0f;
            _lastPos = p;
            float measured = d.magnitude / dt;
            if (measured > 30f) measured = 0f; // Teleport
            _speed = Mathf.Lerp(_speed, measured, 1f - Mathf.Exp(-8f * dt));

            if (!_visible || _horse == null)
            {
                ApplyCamera(0f, dt);
                return;
            }
            _mountBlend = Mathf.MoveTowards(_mountBlend, 1f, dt / 0.35f);
            Animate(dt);
            HoldRider();
            ApplyCamera(CameraBoost, dt);
        }

        private void Animate(float dt)
        {
            _rig.Speed = _speed;
            _rig.Pose = HorsePose.Stand;
            _rig.Animate(dt);
        }

        /// <summary>Reiter in den Sattel: Fahr-Haltung (Emote-Layer) und Modell auf Sattelhoehe.</summary>
        private void HoldRider()
        {
            Animator anim = ActiveAnimator();
            if (anim != _riderAnim)
            {
                ReleaseRider();
                _riderAnim = anim;
                if (anim == null) return;
                _riderPose = anim.GetComponent<EmotePoser>();
                _riderBase = anim.transform.localPosition;
            }
            if (_riderAnim == null) return;
            if (_riderPose != null && _riderPose.ClipState != "Drive" && !_riderPose.Active) _riderPose.PlayActivity("Drive");
            float bob = _horse.localPosition.y;
            _riderAnim.transform.localPosition = _riderBase + Vector3.up * ((SaddleHeight + bob) * Mathf.SmoothStep(0f, 1f, _mountBlend));
            _riderRaised = true;
        }

        private void ReleaseRider()
        {
            if (_riderAnim != null && _riderRaised)
            {
                _riderAnim.transform.localPosition = _riderBase;
                if (_riderPose != null && _riderPose.ClipState == "Drive") _riderPose.PlayActivity(null);
            }
            _riderRaised = false;
            _riderAnim = null;
            _riderPose = null;
        }

        private Animator ActiveAnimator()
        {
            var bodies = GetComponent<CharacterBodySwitcher>();
            GameObject model = bodies != null ? bodies.ActiveModel : null;
            Animator a = model != null ? model.GetComponentInChildren<Animator>() : null;
            if (a == null) a = GetComponentInChildren<Animator>();
            return a;
        }

        private void ApplyCamera(float boost, float dt)
        {
            if (_camera == null) _camera = new MMOCameraBoostTarget(this);
            _camera.Apply(boost, dt);
        }

        /// <summary>Hebt die Kamera der eigenen Figur an, solange sie im Sattel sitzt.</summary>
        private sealed class MMOCameraBoostTarget
        {
            private readonly MountVisual _owner;
            private PlayerCharacter _player;
            private float _current;

            public MMOCameraBoostTarget(MountVisual owner) => _owner = owner;

            public void Apply(float target, float dt)
            {
                if (_player == null) _player = _owner.GetComponent<PlayerCharacter>();
                if (_player == null || !_player.enabled || _player.CameraController == null) return;
                _current = Mathf.MoveTowards(_current, target, dt * 2.5f);
                _player.CameraController.PivotBoost = _current;
            }
        }
    }
}
