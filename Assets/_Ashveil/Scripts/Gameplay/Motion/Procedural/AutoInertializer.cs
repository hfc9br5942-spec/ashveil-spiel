using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>
    /// Automatische Traegheits-Ueberblendung (M-Nahtlos). Laeuft als LETZTE Schicht auf der fertigen Pose – nach
    /// Animator, Standhaltung, Bewegungsschicht, Waffenhand und Emotes – und macht jeden Uebergang nahtlos, egal
    /// woher der Sprung kommt (Zustandswechsel im Animator, Gewichte einer Schicht, Ziehen/Wegstecken, Haltungswechsel).
    ///
    /// Idee (Inertialisierung wie in modernen AAA-Titeln, hier aber selbst erkennend statt per Aufruf):
    ///  1. Fuer jeden Knochen wird aus den zuletzt GEZEIGTEN Posen vorhergesagt, wo er bei gleichbleibender
    ///     Bewegung jetzt waere.
    ///  2. Weicht die neue Pose sprunghaft davon ab (Beschleunigung, die kein Koerper aufbringt), wird die Differenz
    ///     als Versatz uebernommen: Die Figur macht im selben Bild genau die vorhergesagte Bewegung weiter.
    ///  3. Der Versatz klingt ueber eine kritisch gedaempfte Feder ab – ohne Nachschwingen, mit stetiger
    ///     Geschwindigkeit. Nach ~0,2 s zeigt die Figur wieder exakt die Zielpose.
    /// Normale, auch schnelle Bewegungen (Schlaege, Rennen) bleiben unberuehrt – nur echte Spruenge werden geglaettet.
    /// </summary>
    [DefaultExecutionOrder(90)]
    [DisallowMultipleComponent]
    public sealed class AutoInertializer : MonoBehaviour
    {
        public static bool GlobalEnabled = true;

        [Tooltip("Ab dieser Beschleunigung gilt eine Aenderung als Sprung (° pro Bild² bei 60 Bildern/s). Harte Schlaege liegen bei 2–5.")]
        [SerializeField] private float threshold = 6f;
        [Tooltip("Beine vertragen weniger Eingriff (Fusskontakt) – hoehere Schwelle.")]
        [SerializeField] private float legThreshold = 10f;
        [Tooltip("Halbwertszeit des Ausklingens (s).")]
        [SerializeField] private float halfLife = 0.07f;
        [Tooltip("Groessere Spruenge sind gewollt (Teleport, Wiederbeleben) und werden nicht geglaettet (°).")]
        [SerializeField] private float maxCorrection = 45f;
        [Tooltip("Nach einer Glaettung reagiert derselbe Knochen so lange nicht erneut (s) – sonst wuerde dauerndes Zittern\n" +
                 "der Eingabe zu reiner Fortschreibung der alten Bewegung.")]
        [SerializeField] private float refractory = 0.15f;
        private float[] _cool;

        private Animator _anim;
        private Transform[] _bones;
        private bool[] _leg;
        private Quaternion[] _shown1, _shown2, _in1, _in2, _written;
        private Vector3[] _x, _v;          // Versatz als Drehvektor (rad) und seine Geschwindigkeit
        private int _frames;
        private float _dtPrev;

        /// <summary>Anzahl geglaetteter Spruenge seit dem letzten Abruf und groesster Sprung vor der Glaettung (°).</summary>
        public int Smoothed { get; private set; }
        public float LargestJump { get; private set; }

        public void TakeStats(out int count, out float largest)
        {
            count = Smoothed; largest = LargestJump;
            Smoothed = 0; LargestJump = 0f;
        }

        private void Awake() => Bind();

        private void OnEnable() => _frames = 0;

        public void Bind()
        {
            _anim = GetComponent<Animator>();
            if (_anim == null || !_anim.isHuman) { _bones = null; return; }
            var list = new System.Collections.Generic.List<Transform>();
            var leg = new System.Collections.Generic.List<bool>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var b = (HumanBodyBones)i;
                Transform t = _anim.GetBoneTransform(b);
                if (t == null) continue;
                // Beine/Huefte nicht: dort haelt die Fussplatzierung den Bodenkontakt – ein Versatz liesse die Fuesse rutschen
                if (b == HumanBodyBones.Hips || b == HumanBodyBones.LeftUpperLeg || b == HumanBodyBones.RightUpperLeg ||
                    b == HumanBodyBones.LeftLowerLeg || b == HumanBodyBones.RightLowerLeg || b == HumanBodyBones.LeftFoot ||
                    b == HumanBodyBones.RightFoot || b == HumanBodyBones.LeftToes || b == HumanBodyBones.RightToes) continue;
                list.Add(t);
                leg.Add(b == HumanBodyBones.LeftUpperLeg || b == HumanBodyBones.RightUpperLeg || b == HumanBodyBones.LeftLowerLeg ||
                        b == HumanBodyBones.RightLowerLeg || b == HumanBodyBones.LeftFoot || b == HumanBodyBones.RightFoot ||
                        b == HumanBodyBones.LeftToes || b == HumanBodyBones.RightToes || b == HumanBodyBones.Hips);
            }
            _bones = list.ToArray();
            _leg = leg.ToArray();
            int n = _bones.Length;
            _shown1 = new Quaternion[n]; _shown2 = new Quaternion[n];
            _in1 = new Quaternion[n]; _in2 = new Quaternion[n]; _written = new Quaternion[n];
            _x = new Vector3[n]; _v = new Vector3[n];
            _cool = new float[n];
            _frames = 0;
        }

        /// <summary>Alles vergessen (Teleport, Figurwechsel) – die naechste Pose gilt ohne Glaettung.</summary>
        public void Snap() => _frames = 0;

        private void LateUpdate()
        {
            if (_bones == null || !GlobalEnabled) { _frames = 0; return; }
            float dt = Time.deltaTime;
            if (dt <= 1e-5f) return;
            if (_frames >= 2 && dt > 0.1f) _frames = 0; // Haenger: Vorhersage waere unsinnig
            float omega = 1.678f / Mathf.Max(0.01f, halfLife); // e^(-wt)(1+wt) = 0,5 bei t = Halbwertszeit
            float ey = Mathf.Exp(-omega * dt);
            float norm = (1f / 3600f) / (dt * dt);
            float ratio = dt / Mathf.Max(1e-4f, _dtPrev);

            for (int i = 0; i < _bones.Length; i++)
            {
                Transform t = _bones[i];
                Quaternion raw = t.localRotation;
                // Hat in diesem Bild niemand den Knochen neu gesetzt, steht dort noch unsere eigene Ausgabe –
                // dann gilt die Eingabe des letzten Bildes (sonst wuerde der Versatz sich selbst aufaddieren).
                Quaternion input = _frames > 0 && Same(raw, _written[i]) ? _in1[i] : raw;
                if (_frames < 2)
                {
                    _x[i] = _v[i] = Vector3.zero;
                    Push(i, input, input);
                    continue;
                }
                // Versatz ausklingen lassen (kritisch gedaempft)
                Vector3 j0 = _x[i], j1 = _v[i] + j0 * omega;
                _x[i] = ey * (j0 + j1 * dt);
                _v[i] = ey * (_v[i] - j1 * omega * dt);
                Quaternion shown = FromVec(_x[i]) * input;

                // Sprung in der EINGABE? (Vorhersage aus den letzten Eingaben – die eigene Glaettung zaehlt nicht)
                float errIn = Quaternion.Angle(Extrapolate(_in2[i], _in1[i], ratio), input);
                _cool[i] -= dt;
                if (_cool[i] <= 0f && errIn * norm > (_leg[i] ? legThreshold : threshold) && errIn < maxCorrection)
                {
                    // Gezeigt wird genau die Fortsetzung der bisher gezeigten Bewegung; die Differenz klingt aus
                    Quaternion pred = Extrapolate(_shown2[i], _shown1[i], ratio);
                    Quaternion d = pred * Quaternion.Inverse(shown);
                    Vector3 x = ToVec(d * FromVec(_x[i]));
                    float lim = maxCorrection * Mathf.Deg2Rad;
                    if (x.magnitude > lim) x = x.normalized * lim;
                    _x[i] = x;
                    shown = FromVec(x) * input;
                    _cool[i] = refractory;
                    Smoothed++;
                    LargestJump = Mathf.Max(LargestJump, errIn);
                }
                t.localRotation = shown;
                Push(i, input, shown);
            }

            _dtPrev = dt;
            _frames++;
        }

        private static bool Same(Quaternion a, Quaternion b) =>
            Mathf.Abs(a.x - b.x) < 1e-6f && Mathf.Abs(a.y - b.y) < 1e-6f && Mathf.Abs(a.z - b.z) < 1e-6f && Mathf.Abs(a.w - b.w) < 1e-6f;

        private static Quaternion Extrapolate(Quaternion q2, Quaternion q1, float ratio)
        {
            Quaternion step = Quaternion.Inverse(q2) * q1;
            step.ToAngleAxis(out float ang, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-8f) return q1;
            if (ang > 180f) ang -= 360f;
            return q1 * Quaternion.AngleAxis(ang * Mathf.Min(ratio, 3f), axis);
        }

        private void Push(int i, Quaternion input, Quaternion shown)
        {
            bool first = _frames == 0;
            _in2[i] = first ? input : _in1[i];
            _in1[i] = input;
            _shown2[i] = first ? shown : _shown1[i];
            _shown1[i] = shown;
            _written[i] = shown;
        }

        private static Quaternion FromVec(Vector3 v)
        {
            float a = v.magnitude;
            return a < 1e-7f ? Quaternion.identity : Quaternion.AngleAxis(a * Mathf.Rad2Deg, v / a);
        }

        private static Vector3 ToVec(Quaternion q)
        {
            q.ToAngleAxis(out float ang, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-8f) return Vector3.zero;
            if (ang > 180f) ang -= 360f;
            return axis.normalized * (ang * Mathf.Deg2Rad);
        }
    }
}
