using UnityEngine;

namespace Ashveil.Gameplay.Visuals
{
    /// <summary>
    /// 19.10.2026 (ART-122, Max: „weiter ausarbeiten“): Der Zopf schwingt nach (Laufen, Drehen, Springen).
    /// Reine Darstellung, läuft auf jedem Client lokal – kein Netzverkehr, keine Physik-Engine, nur 4 Knochen je Figur
    /// (MMO-tauglich: ein paar Vektor-Rechnungen pro Frame). Ein Feder-Pendel je Knochen (Verlet), hält die Länge,
    /// zieht zur Animationslage zurück und weicht höchstens <see cref="maxWinkel"/> davon ab (kein Durchdringen des Rückens).
    /// Wird von <see cref="AppearanceRig"/> angehängt, wenn das Modell Knochen „zopf_01…“ hat (Blender: ashveil_ki3d_figur.py).
    /// </summary>
    [DefaultExecutionOrder(1000)]   // nach Animator und IK
    public sealed class ZopfPendel : MonoBehaviour
    {
        [Range(0f, 1f)] public float steifigkeit = 0.10f;   // Rückzug zur Animationslage je Frame (bei 60 fps)
        [Range(0f, 1f)] public float daempfung = 0.20f;
        public float schwerkraft = 1.5f;                     // m/s² zusätzlich (der Zopf hängt schon in der Ruhelage)
        public float maxWinkel = 35f;

        private Transform[] _k;
        private Vector3[] _p, _alt, _lokalTail;
        private float[] _len;
        private Quaternion[] _ruhe;   // Ruhe-Drehung (Humanoid-Animator schreibt Zusatzknochen nicht – sonst würde sich die Drehung aufsummieren)
        private Vector3[] _ruheDir;
        private bool _start = true;

        public void Einrichten(Transform erster)
        {
            var liste = new System.Collections.Generic.List<Transform>();
            for (Transform t = erster; t != null; )
            {
                liste.Add(t);
                Transform naechster = null;
                foreach (Transform c in t) if (c.name.StartsWith("zopf_")) { naechster = c; break; }
                t = naechster;
            }
            _k = liste.ToArray();
            int n = _k.Length;
            _p = new Vector3[n]; _alt = new Vector3[n]; _len = new float[n]; _lokalTail = new Vector3[n]; _ruhe = new Quaternion[n];
            for (int i = 0; i < n; i++) _ruhe[i] = _k[i].localRotation;
            for (int i = 0; i < n; i++)
            {
                Vector3 tail = i < n - 1 ? _k[i + 1].position
                    : _k[i].position + (n > 1 ? _k[i].position - _k[i - 1].position : Vector3.down * 0.1f);
                _lokalTail[i] = _k[i].InverseTransformPoint(tail);
                _len[i] = Mathf.Max(Vector3.Distance(_k[i].position, tail), 1e-3f);
            }
            _start = true;
        }

        private Vector3 Tail(int i) => _k[i].TransformPoint(_lokalTail[i]);

        private void OnEnable() => _start = true;

        private void LateUpdate()
        {
            if (_k == null || _k.Length == 0) return;
            float dt = Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f);
            float f60 = dt * 60f;   // Feder/Dämpfung bildratenunabhängig
            float steif = 1f - Mathf.Pow(1f - steifigkeit, f60), daem = Mathf.Pow(1f - daempfung, f60);
            for (int i = 0; i < _k.Length; i++) _k[i].localRotation = _ruhe[i];
            // Ziel-Richtungen aus der Animationslage, BEVOR Eltern-Knochen gedreht werden – so summieren sich die Ausschläge
            // entlang der Kette nicht auf (sonst schlug der Zopf über die Schulter nach vorn)
            if (_ruheDir == null || _ruheDir.Length != _k.Length) _ruheDir = new Vector3[_k.Length];
            for (int i = 0; i < _k.Length; i++) _ruheDir[i] = Tail(i) - _k[i].position;
            for (int i = 0; i < _k.Length; i++)
            {
                Vector3 kopf = _k[i].position, ziel = kopf + _ruheDir[i];
                if (_start || (_p[i] - ziel).sqrMagnitude > 1f) { _p[i] = ziel; _alt[i] = ziel; }   // Start oder Teleport
                Vector3 v = Vector3.ClampMagnitude((_p[i] - _alt[i]) * daem, 0.04f);   // Sprünge (Schnitt, Ruckler) nicht als Schleuder
                _alt[i] = _p[i];
                _p[i] += v + Vector3.down * (schwerkraft * dt * dt);
                _p[i] += (ziel - _p[i]) * steif;
                Vector3 d = _p[i] - kopf;
                if (d.sqrMagnitude < 1e-10f) d = ziel - kopf;
                d = d.normalized * _len[i];
                Vector3 zd = _ruheDir[i];
                float w = Vector3.Angle(zd, d);
                if (w > maxWinkel) d = Vector3.Slerp(zd.normalized * _len[i], d, maxWinkel / w);
                _p[i] = kopf + d;
                Vector3 jetzt = Tail(i) - kopf;   // aktuelle Richtung (Eltern schon gedreht)
                _k[i].rotation = Quaternion.FromToRotation(jetzt, d) * _k[i].rotation;
            }
            _start = false;
        }
    }
}
