using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>
    /// M8: spielt ein Emote auf einer humanoiden Figur ab – Spieler (vom Netz gesteuert) und NPCs (vom Tagesablauf).
    /// Rein darstellend: wer ein Emote starten/abbrechen darf, entscheidet der Server bzw. der Tagesablauf.
    ///
    ///   Sitzen   Clip "Sitting_Idle" im Layer "Emote", Huefte auf Sitzhoehe (0 = Boden), Fuesse per IK vorn
    ///   Tanzen   Clip "Dance_Loop" + Rueckwaertsgleiten und Drehung (eigener Retro-Pop-Loop, keine Choreografie-Kopie)
    ///   Lachen   Clip "Idle_Talking" + Oberkoerper wippt, Kopf nach hinten, Haende an den Bauch
    ///   Winken, Jubeln, Fistbump  prozedural ueber der Stand-Animation (Arm-IK)
    ///
    /// Laeuft nach Animator und Haltung (LateUpdate); alle Richtungen aus dem Modell selbst (rechts/oben/vorn).
    /// </summary>
    [DefaultExecutionOrder(60)]
    [DisallowMultipleComponent]
    public sealed class EmotePoser : MonoBehaviour
    {
        [SerializeField] private Animator animator;

        public const string LayerName = "Emote";
        private const float BlendIn = 0.25f, BlendOut = 0.35f;

        private int _layer = -2;
        private EmoteId _current;
        private float _elapsed, _duration;
        private bool _loop;
        private float _weight;
        private float _seat;
        private string _clipState = "Empty";

        private bool _bones;
        private Transform _hips, _spine, _chest, _neck, _head;
        private Transform _lUp, _lLo, _lHand, _rUp, _rLo, _rHand, _lThigh, _lShin, _lFoot, _rThigh, _rShin, _rFoot;
        private float _scale = 1f;
        private bool _scaleMeasured;

        private Vector3 _baseLocalPos;
        private Quaternion _baseLocalRot;
        private bool _offsetActive;
        private Vector3 _r, _u, _f, _origin;

        /// <summary>Laeuft gerade ein Emote (inkl. Ausblenden)?</summary>
        public bool Active => _current != EmoteId.None || _weight > 0.001f;
        public EmoteId Current => _current;
        public float Elapsed => _elapsed;
        public int PlayCount { get; private set; }

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void OnDisable()
        {
            RestoreOffset();
            _current = EmoteId.None;
            _weight = 0f;
        }

        /// <summary>Emote starten. elapsed = schon vergangene Zeit (Spaeter-Hinzukommende sehen dieselbe Stelle).
        /// seatHeight: nur Sitzen – Hoehe der Sitzflaeche (0 = auf dem Boden).</summary>
        public void Play(EmoteId id, float elapsed = 0f, float seatHeight = 0f) => Play(id, elapsed, seatHeight, null);

        /// <summary>Wie oben; clipState ersetzt beim Sitzen die Sitz-Animation (NPCs: "SitTalk").</summary>
        public void Play(EmoteId id, float elapsed, float seatHeight, string clipState)
        {
            if (id == EmoteId.None) { Stop(); return; }
            if (!EmoteCatalog.TryGet(id, out EmoteInfo info)) return;
            if (id == _current && Mathf.Abs(elapsed - _elapsed) < 0.3f && (clipState == null || clipState == _clipState)) return;
            _current = id;
            _elapsed = Mathf.Max(0f, elapsed);
            _loop = info.Loop;
            _duration = info.Duration;
            _seat = Mathf.Max(0f, seatHeight);
            PlayCount++;
            if (clipState != null) { SetClip(clipState, null); return; }
            SetClip(id == EmoteId.Sit ? "SitLoop" : id == EmoteId.Dance ? "Dance" : id == EmoteId.Laugh ? "Talk" : "Empty", id == EmoteId.Sit ? "SitEnter" : null);
        }

        /// <summary>
        /// NPC-Taetigkeit im Stehen (Zustand im Layer "Emote", z. B. "Chop", "FoldArms", "Talk", "Carry").
        /// Beendet ein laufendes Emote. null/"Empty" = keine Taetigkeit.
        /// </summary>
        public void PlayActivity(string state)
        {
            if (_current != EmoteId.None) _current = EmoteId.None;
            SetClip(string.IsNullOrEmpty(state) ? "Empty" : state, null);
        }

        /// <summary>Aktueller Zustand im Layer "Emote" (Tests/Anzeige).</summary>
        public string ClipState => _clipState;

        public void Stop()
        {
            if (_current == EmoteId.None) return;
            _current = EmoteId.None;
            SetClip("Empty", null);
        }

        private void SetClip(string state, string enterState)
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            if (_layer == -2) _layer = animator.GetLayerIndex(LayerName);
            if (_layer < 0) return;
            string target = enterState != null && _elapsed < 0.6f ? enterState : state;
            if (target == _clipState) return;
            _clipState = target;
            if (animator.HasState(_layer, Animator.StringToHash(target))) animator.CrossFadeInFixedTime(target, 0.3f, _layer);
        }

        // ================================================================== Ablauf

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            bool running = _current != EmoteId.None;
            if (running)
            {
                _elapsed += dt;
                if (!_loop && _elapsed >= _duration) Stop();
            }
            running = _current != EmoteId.None;
            float target = running ? 1f : 0f;
            if (running && !_loop) target = Mathf.Clamp01((_duration - _elapsed) / BlendOut);
            _weight = Mathf.MoveTowards(_weight, target, dt / (target > _weight ? BlendIn : BlendOut));

            if (animator == null || !EnsureBones()) return;
            MeasureScale();
            if (_weight <= 0.0001f && !running) { RestoreOffset(); return; }

            Transform model = animator.transform;
            if (!_offsetActive)
            {
                _baseLocalPos = model.localPosition;
                _baseLocalRot = model.localRotation;
                _offsetActive = true;
            }
            model.localPosition = _baseLocalPos;
            model.localRotation = _baseLocalRot;

            // Bezug: Modell im Ruhezustand (Fuesse = Ursprung)
            _r = model.right; _u = model.up; _f = model.forward; _origin = model.position;
            float w = Mathf.SmoothStep(0f, 1f, _weight);
            float t = _elapsed;
            EmoteId e = running ? _current : _lastPosed;
            _lastPosed = e;

            switch (e)
            {
                case EmoteId.Wave: Wave(t, w); break;
                case EmoteId.Fistbump: Fistbump(t, w); break;
                case EmoteId.Cheer: Cheer(t, w); break;
                case EmoteId.Laugh: Laugh(t, w); break;
                case EmoteId.Sit: Sit(t, w); break;
                case EmoteId.Dance: Dance(t, w, model); break;
            }
        }

        private EmoteId _lastPosed;

        // ================================================================== Emotes

        private void Wave(float t, float w)
        {
            float wave = Mathf.Sin(t * Mathf.PI * 2f * 1.6f);
            Vector3 hand = P(0.34f + 0.09f * wave, 1.78f, 0.12f);
            LimbIK.TwoBone(_rUp, _rLo, _rHand, hand, M(1f, -0.6f, -0.2f), w);
            Tilt(_head, 0f, -6f * w, 8f * w);
        }

        private void Fistbump(float t, float w)
        {
            float reach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.45f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 1.2f) / 0.45f)));
            Vector3 chest = P(0.18f, 1.28f, 0.2f);
            Vector3 out_ = P(0.12f, 1.32f, 0.62f);
            LimbIK.TwoBone(_rUp, _rLo, _rHand, Vector3.Lerp(chest, out_, reach), M(0.8f, -1f, 0f), w);
            Tilt(_spine, 6f * reach * w, 0f, -8f * reach * w);
            CurlFist(_rHand, w);
        }

        private void Cheer(float t, float w)
        {
            float bounce = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f * 1.1f));
            float lift = bounce * 0.07f * _scale * w;
            _hips.position += _u * lift;
            float sway = Mathf.Sin(t * Mathf.PI * 2f * 2.2f) * 0.05f;
            LimbIK.TwoBone(_rUp, _rLo, _rHand, P(0.26f + sway, 2.08f, 0.08f) + _u * lift, M(1f, 0f, -0.3f), w);
            LimbIK.TwoBone(_lUp, _lLo, _lHand, P(-0.26f + sway, 2.08f, 0.08f) + _u * lift, M(-1f, 0f, -0.3f), w);
            Tilt(_head, -12f * w, 0f, 0f);
        }

        private void Laugh(float t, float w)
        {
            float shake = Mathf.Sin(t * Mathf.PI * 2f * 4.5f) * Mathf.Clamp01(1.2f - Mathf.Abs(Mathf.Sin(t * 1.6f)));
            float lean = Mathf.Sin(t * Mathf.PI * 2f * 0.5f);
            Tilt(_spine, (-6f + lean * 8f + shake * 3f) * w, 0f, 0f);
            Tilt(_chest, (shake * 3f) * w, 0f, 0f);
            Tilt(_head, (-16f + shake * 4f) * w, 0f, 0f);
            LimbIK.TwoBone(_rUp, _rLo, _rHand, P(0.12f, 1.02f, 0.2f), M(1f, 0f, -0.5f), w * 0.9f);
            LimbIK.TwoBone(_lUp, _lLo, _lHand, P(-0.1f, 1.12f, 0.2f), M(-1f, 0f, -0.5f), w * 0.9f);
        }

        private void Sit(float t, float w)
        {
            // Huefte auf Sitzhoehe (Boden: knapp darueber), Fuesse vorn aufstellen
            float want = (_seat > 0.05f ? _seat : 0.14f * _scale) + 0.06f * _scale;
            float now = Vector3.Dot(_hips.position - _origin, _u);
            Transform model = animator.transform;
            float drop = (now - want) * w;
            model.position -= _u * drop;
            _origin = model.position + _u * drop; // Boden bleibt, wo er war
            if (_seat <= 0.05f)
            {
                LimbIK.TwoBone(_lThigh, _lShin, _lFoot, _origin + M(-0.16f, 0.06f, 0.5f) * _scale, M(-0.2f, 1f, 0.6f), w);
                LimbIK.TwoBone(_rThigh, _rShin, _rFoot, _origin + M(0.16f, 0.06f, 0.5f) * _scale, M(0.2f, 1f, 0.6f), w);
            }
        }

        private void Dance(float t, float w, Transform model)
        {
            // 4-s-Loop: 2,4 s rueckwaerts gleiten (Moonwalk-inspiriert), 0,8 s Drehung zurueck nach vorn, 0,8 s auf der Stelle
            const float Cycle = 4f;
            float c = Mathf.Repeat(t, Cycle);
            float back;
            float spin = 0f;
            if (c < 2.4f) back = Mathf.SmoothStep(0f, 1f, c / 2.4f);
            else if (c < 3.2f)
            {
                float k = (c - 2.4f) / 0.8f;
                back = 1f - Mathf.SmoothStep(0f, 1f, k);
                spin = Mathf.SmoothStep(0f, 360f, k);
            }
            else back = 0f;
            model.position -= _f * (0.7f * _scale * back * w);
            model.rotation = Quaternion.AngleAxis(spin * w, _u) * model.rotation;
            // Gleit-Fuesse: abwechselnd Ferse heben
            float step = Mathf.Sin(t * Mathf.PI * 2f * 2f);
            if (c < 2.4f)
            {
                Tilt(_lFoot, Mathf.Max(0f, step) * -25f * w, 0f, 0f);
                Tilt(_rFoot, Mathf.Max(0f, -step) * -25f * w, 0f, 0f);
            }
            Tilt(_head, 0f, Mathf.Sin(t * Mathf.PI * 2f) * 6f * w, 0f);
        }

        // ================================================================== Hilfen

        private Vector3 M(float x, float y, float z) => _r * x + _u * y + _f * z;
        private Vector3 P(float x, float y, float z) => _origin + M(x, y, z) * _scale;

        /// <summary>Knochen in Modellbegriffen drehen: pitch nach vorn, roll zur rechten Seite, yaw nach rechts (Grad).</summary>
        private void Tilt(Transform bone, float pitch, float roll, float yaw)
        {
            if (bone == null) return;
            Quaternion q = Quaternion.identity;
            if (Mathf.Abs(roll) > 1e-4f) q = Quaternion.AngleAxis(roll, _f) * q;
            if (Mathf.Abs(pitch) > 1e-4f) q = Quaternion.AngleAxis(pitch, _r) * q;
            if (Mathf.Abs(yaw) > 1e-4f) q = Quaternion.AngleAxis(yaw, _u) * q;
            bone.rotation = q * bone.rotation;
        }

        private void CurlFist(Transform hand, float w)
        {
            if (hand == null) return;
            foreach (Transform child in hand.GetComponentsInChildren<Transform>())
            {
                if (child == hand || child.name.ToLowerInvariant().Contains("thumb")) continue;
                child.localRotation = Quaternion.Slerp(child.localRotation, child.localRotation * Quaternion.Euler(0f, 0f, -55f), w * 0.8f);
            }
        }

        private void MeasureScale()
        {
            if (_scaleMeasured || _current != EmoteId.None) return;
            float h = Vector3.Dot(_head.position - animator.transform.position, animator.transform.up);
            if (h > 0.5f) { _scale = h / 1.62f; _scaleMeasured = true; }
        }

        private void RestoreOffset()
        {
            if (!_offsetActive || animator == null) return;
            animator.transform.localPosition = _baseLocalPos;
            animator.transform.localRotation = _baseLocalRot;
            _offsetActive = false;
        }

        private bool EnsureBones()
        {
            if (_bones) return true;
            if (!animator.isHuman || animator.avatar == null) return false;
            Transform B(HumanBodyBones x) => animator.GetBoneTransform(x);
            _hips = B(HumanBodyBones.Hips); _spine = B(HumanBodyBones.Spine); _chest = B(HumanBodyBones.Chest);
            _neck = B(HumanBodyBones.Neck); _head = B(HumanBodyBones.Head);
            _lUp = B(HumanBodyBones.LeftUpperArm); _lLo = B(HumanBodyBones.LeftLowerArm); _lHand = B(HumanBodyBones.LeftHand);
            _rUp = B(HumanBodyBones.RightUpperArm); _rLo = B(HumanBodyBones.RightLowerArm); _rHand = B(HumanBodyBones.RightHand);
            _lThigh = B(HumanBodyBones.LeftUpperLeg); _lShin = B(HumanBodyBones.LeftLowerLeg); _lFoot = B(HumanBodyBones.LeftFoot);
            _rThigh = B(HumanBodyBones.RightUpperLeg); _rShin = B(HumanBodyBones.RightLowerLeg); _rFoot = B(HumanBodyBones.RightFoot);
            _bones = _hips != null && _head != null && _rHand != null && _lHand != null && _lFoot != null && _rFoot != null;
            return _bones;
        }
    }
}
