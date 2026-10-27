using UnityEngine;

namespace Ashveil.Gameplay.Npcs
{
    /// <summary>Haltungen eines Tieres (prozedural, ohne Animationsdateien).</summary>
    public enum AnimalPose : byte { Stand, Walk, Sit, Lie, Sleep, Graze, Sniff, Groom, Look }

    /// <summary>
    /// M8.4: prozedural bewegter Vierbeiner (Katze, Hund, Ziege, Maultier – Platzhalter, bis echte Modelle kommen):
    /// Kreuzgang nach Geschwindigkeit, Wippen und Atmen, Sitzen (Hinterbeine gefaltet), Liegen/Schlafen (Beine
    /// untergeschlagen, Kopf auf dem Boden), Grasen, Schnueffeln, Putzen, Umschauen, Schwanz wedeln/pendeln.
    /// Rein darstellend; Ort und Haltung bestimmt <see cref="AnimalRoutine"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnimalVisual : MonoBehaviour
    {
        [SerializeField] private Transform model;
        [SerializeField] private Transform head;
        [SerializeField] private Transform tail;
        [Tooltip("Vorne links, vorne rechts, hinten links, hinten rechts (Drehpunkt an Schulter/Huefte)")]
        [SerializeField] private Transform[] legs = new Transform[4];
        [SerializeField] private float legLength = 0.25f;
        [SerializeField] private float strideLength = 0.5f;
        [Tooltip("Hoehe und z-Lage der Huefte (Drehpunkt beim Sitzen)")]
        [SerializeField] private Vector2 hip = new Vector2(0.25f, -0.2f);
        [SerializeField] private float sitAngle = 32f;
        [Tooltip("Schwanz wedelt (Hund) statt ruhig zu pendeln")]
        [SerializeField] private bool wagger;
        [Header("Gelenke (Tiere mit Skelett, ART-120)")]
        [Tooltip("Je Bein (VL, VR, HL, HR): Unterarm/Unterschenkel, Röhrbein, Fessel – leer = starres Bein wie bisher")]
        [SerializeField] private Transform[] mitte = new Transform[0];
        [SerializeField] private Transform[] unten = new Transform[0];
        [SerializeField] private Transform[] fessel = new Transform[0];
        [Tooltip("Schritt im Viertakt (Pferd, Rind): hinten links, vorn links, hinten rechts, vorn rechts; schneller → Trab (diagonal)")]
        [SerializeField] private bool viertakt;
        [Tooltip("Ab dieser Geschwindigkeit (m/s) ist der Gang ganz Trab")]
        [SerializeField] private float trabAb = 2.6f;
        [Header("Aufgenommene Bewegung (MOV-127)")]
        [Tooltip("Tier mit übertragenen Bewegungsclips: der Animator spielt sie, die prozedurale Bewegung ruht")]
        [SerializeField] private Animator tierAnimator;
        [Tooltip("Stand, Schritt, Galopp, Grasen, Liegen (für Prüfbilder im Editor)")]
        [SerializeField] private AnimationClip[] tierClips = new AnimationClip[0];

        /// <summary>Natürliches Tempo der Clips (m/s) und Wechsel Schritt → Galopp.</summary>
        public const float SchrittTempo = 1.5f, GaloppTempo = 4.5f, GaloppAb = 3.2f;
        private static readonly int PTempo = Animator.StringToHash("Tempo"), PTakt = Animator.StringToHash("Takt"), PHaltung = Animator.StringToHash("Haltung");
        private bool _clipStart;

        private Vector3 _modelPos;
        private Quaternion _modelRot, _headRot, _tailRot;
        private readonly Quaternion[] _legRot = new Quaternion[4];
        private readonly Quaternion[] _mRot = new Quaternion[4], _uRot = new Quaternion[4], _fRot = new Quaternion[4];
        private bool _init;
        private float _phase, _time;
        private float _speed;
        private AnimalPose _pose;

        // weich ueberblendete Anteile
        private float _sit, _lie, _sleep, _graze, _sniff, _groom, _look;

        public AnimalPose Pose => _pose;
        public float Speed => _speed;

        public void Configure(Transform m, Transform h, Transform t, Transform[] l, float legLen, float stride, Vector2 hipPoint, float sit, bool wag)
        {
            model = m; head = h; tail = t; legs = l; legLength = legLen; strideLength = stride; hip = hipPoint; sitAngle = sit; wagger = wag;
        }

        /// <summary>Gelenke unterhalb der Beindrehpunkte (Skelett-Tiere): Bein beugt sich im Schwung und beim Liegen.</summary>
        public void Gelenke(Transform[] m, Transform[] u, Transform[] f, bool vierTakt)
        {
            mitte = m ?? new Transform[0]; unten = u ?? new Transform[0]; fessel = f ?? new Transform[0]; viertakt = vierTakt;
        }

        /// <summary>Übertragene Bewegungsclips (MOV-127): Animator mit Zuständen Stand, Schritt, Galopp, Grasen, Liegen.</summary>
        public void Clips(Animator a, AnimationClip[] c)
        {
            tierAnimator = a; tierClips = c ?? new AnimationClip[0];
        }

        public bool HatClips => tierAnimator != null;

        public void Set(AnimalPose pose, float speed)
        {
            _pose = pose;
            _speed = speed;
        }

        private void Init()
        {
            _init = true;
            if (model == null) model = transform;
            _modelPos = model.localPosition;
            _modelRot = model.localRotation;
            if (head != null) _headRot = head.localRotation;
            if (tail != null) _tailRot = tail.localRotation;
            for (int i = 0; i < 4 && i < legs.Length; i++) if (legs[i] != null) _legRot[i] = legs[i].localRotation;
            for (int i = 0; i < 4; i++)
            {
                if (i < mitte.Length && mitte[i] != null) _mRot[i] = mitte[i].localRotation;
                if (i < unten.Length && unten[i] != null) _uRot[i] = unten[i].localRotation;
                if (i < fessel.Length && fessel[i] != null) _fRot[i] = fessel[i].localRotation;
            }
            _time = Random.value * 10f;
        }

        /// <summary>Gelenk um die Querachse des Tieres drehen (positiv = nach hinten), ausgehend von der Ruhelage zum Eltern-Knochen.</summary>
        private static void Beuge(Transform[] t, Quaternion[] ruhe, int i, float grad, Vector3 achse)
        {
            if (t == null || i >= t.Length || t[i] == null) return;
            Transform k = t[i];
            Quaternion basis = k.parent != null ? k.parent.rotation * ruhe[i] : ruhe[i];
            k.rotation = Quaternion.AngleAxis(grad, achse) * basis;
        }

        private static float Approach(float v, bool on, float dt, float rate = 2.5f) => Mathf.MoveTowards(v, on ? 1f : 0f, dt * rate);

        private void LateUpdate() => Tick(Time.deltaTime);

        private int Haltung() => _speed > 0.05f ? 0 : _pose == AnimalPose.Graze ? 1 : (_pose == AnimalPose.Lie || _pose == AnimalPose.Sleep) ? 2 : 0;

        private float Takt() => Mathf.Clamp(_speed < GaloppAb ? _speed / SchrittTempo : _speed / GaloppTempo, 0.5f, 1.8f);

        /// <summary>Clip-Tier: nur Parameter setzen; der Animator bewegt das Skelett. Start zufällig versetzt, damit die Herde nicht im Gleichschritt geht.</summary>
        private void TickClips()
        {
            if (!_clipStart)
            {
                _clipStart = true;
                if (tierAnimator.isActiveAndEnabled && tierAnimator.runtimeAnimatorController != null) tierAnimator.Play("Stand", 0, Random.value);
            }
            tierAnimator.SetInteger(PHaltung, Haltung());
            tierAnimator.SetFloat(PTempo, _speed);
            tierAnimator.SetFloat(PTakt, Takt());
        }

        /// <summary>Prüfbild im Editor ohne Abspielen: passenden Clip an der Stelle <paramref name="sekunden"/> auf das Skelett legen.</summary>
        private void VorschauClips(float sekunden)
        {
            int h = Haltung();
            int i = h == 1 ? 3 : h == 2 ? 4 : _speed > 0.05f ? (_speed < GaloppAb ? 1 : 2) : 0;
            if (i >= tierClips.Length || tierClips[i] == null) return;
            AnimationClip c = tierClips[i];
            float t = i == 4 ? 0f : Mathf.Repeat(sekunden * (i == 1 || i == 2 ? Takt() : 1f), Mathf.Max(0.01f, c.length));
            c.SampleAnimation(tierAnimator.gameObject, t);
        }

        /// <summary>Prüfbilder im Editor (tier_abnahme): Haltung und Tempo setzen und die Bewegung um <paramref name="sekunden"/> vorspulen.</summary>
        public void Vorschau(AnimalPose pose, float speed, float sekunden)
        {
            Set(pose, speed);
            if (tierAnimator != null) { VorschauClips(sekunden); return; }
            for (float t = 0f; t < sekunden; t += 1f / 60f) Tick(1f / 60f);
        }

        private void Tick(float dt)
        {
            if (tierAnimator != null) { TickClips(); return; }
            if (!_init) Init();
            _time += dt;
            bool walking = _speed > 0.05f;
            _sit = Approach(_sit, !walking && (_pose == AnimalPose.Sit || _pose == AnimalPose.Groom), dt);
            _lie = Approach(_lie, !walking && (_pose == AnimalPose.Lie || _pose == AnimalPose.Sleep), dt, 1.6f);
            _sleep = Approach(_sleep, !walking && _pose == AnimalPose.Sleep, dt, 0.8f);
            _graze = Approach(_graze, !walking && _pose == AnimalPose.Graze, dt, 2f);
            _sniff = Approach(_sniff, _pose == AnimalPose.Sniff, dt, 2f);
            _groom = Approach(_groom, !walking && _pose == AnimalPose.Groom, dt, 2f);
            _look = Approach(_look, !walking && _pose == AnimalPose.Look, dt, 2f);

            _phase += _speed * dt * (2f * Mathf.PI / Mathf.Max(0.1f, strideLength));
            float move = Mathf.Clamp01(_speed / 0.6f);
            float swing = 34f * move;

            // ---- Koerper
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.04f * legLength * 4f * move;
            float breathe = Mathf.Sin(_time * (_sleep > 0.5f ? 1.2f : 2.4f)) * 0.006f;
            float drop = legLength * 0.85f * _lie;
            // Sitzen: um die Huefte nach hinten kippen (Brust hoch)
            float sitA = -sitAngle * _sit * (1f - _lie);
            Quaternion sitRot = Quaternion.Euler(sitA, 0f, 0f);
            Vector3 pivot = new Vector3(0f, hip.x, hip.y);
            Vector3 pos = _modelPos + pivot - sitRot * pivot + Vector3.up * (bob + breathe - drop - 0.04f * _sit);
            float grazePitch = 5f * _graze;
            model.localPosition = pos;
            model.localRotation = _modelRot * sitRot * Quaternion.Euler(grazePitch, 0f, 0f);

            // ---- Beine: Kreuzgang, beim Sitzen hinten gefaltet, beim Liegen untergeschlagen
            for (int i = 0; i < 4 && i < legs.Length; i++)
            {
                if (legs[i] == null) continue;
                bool front = i < 2;
                bool diagonalA = i == 0 || i == 3;
                float versatz = diagonalA ? 0f : Mathf.PI;
                if (viertakt)
                {
                    // Schritt: HL 0, VL ¼, HR ½, VR ¾ Umlauf; Trab: diagonale Paare (VL+HR, VR+HL)
                    float schritt = i == 0 ? 0.5f * Mathf.PI : i == 1 ? 1.5f * Mathf.PI : i == 2 ? 0f : Mathf.PI;
                    float trab = i == 0 || i == 3 ? 0f : Mathf.PI;
                    versatz = Mathf.Lerp(schritt, trab, Mathf.Clamp01((_speed - trabAb * 0.6f) / (trabAb * 0.4f)));
                }
                float a = Mathf.Sin(_phase + versatz) * swing;
                float sitLeg = front ? sitAngle * _sit : -75f * _sit;       // vorn senkrecht halten, hinten nach vorn falten
                float lieLeg = front ? -80f * _lie : 80f * _lie;            // vorn nach vorn, hinten nach hinten
                legs[i].localRotation = _legRot[i] * Quaternion.Euler(a * (1f - _lie) + sitLeg * (1f - _lie) + lieLeg, 0f, 0f);
                // Beugen im Schwung (Fuß hebt ab, solange das Bein nach vorn schwingt) und beim Liegen (untergeschlagen)
                float hub = Mathf.Max(0f, -Mathf.Cos(_phase + versatz)) * move * (1f - _lie);
                Vector3 achse = transform.right;
                if (front)
                {
                    Beuge(mitte, _mRot, i, -12f * hub - 10f * _lie, achse);
                    Beuge(unten, _uRot, i, 75f * hub + 115f * _lie, achse);
                    Beuge(fessel, _fRot, i, 30f * hub + 20f * _lie, achse);
                }
                else
                {
                    Beuge(mitte, _mRot, i, 22f * hub + 25f * _lie, achse);
                    Beuge(unten, _uRot, i, -48f * hub - 70f * _lie, achse);
                    Beuge(fessel, _fRot, i, 30f * hub + 25f * _lie, achse);
                }
            }

            // ---- Kopf
            if (head != null)
            {
                float pitch = 55f * _graze + (35f + Mathf.Sin(_time * 7f) * 6f) * _sniff + 25f * _lie + 20f * _sleep
                              + Mathf.Sin(_phase * 2f) * 3f * move + (20f + Mathf.Sin(_time * 5f) * 10f) * _groom - 8f * _sit * (1f - _groom);
                float yaw = Mathf.Sin(_time * 0.7f) * 40f * _look + 70f * _groom + Mathf.Sin(_time * 0.9f) * 6f * (1f - _look);
                float nibble = Mathf.Sin(_time * 3.1f) * 4f * _graze;
                head.localRotation = _headRot * Quaternion.Euler(pitch + nibble, yaw, 0f);
            }

            // ---- Schwanz
            if (tail != null)
            {
                float wagSpeed = wagger ? (walking ? 9f : 6f) : 1.6f;
                float wagAmp = wagger ? 28f : 12f;
                float sway = Mathf.Sin(_time * wagSpeed) * wagAmp * (1f - _sleep);
                float droop = 30f * _sleep;
                tail.localRotation = _tailRot * Quaternion.Euler(droop, sway, 0f);
            }
        }
    }
}
