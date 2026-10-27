using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ashveil.Gameplay.Motion.Procedural;
using UnityEngine;

namespace Ashveil.Gameplay.Motion.Lab
{
    /// <summary>
    /// Anatomie-Pruefer (Motion-Lab): misst in jedem Bild die ENDGUELTIGE Pose (Animation + alle prozeduralen
    /// Schichten) gegen menschliche Gelenkgrenzen – Wirbelsaeule, Hals, Schluesselbein, Schulter, Ellbogen,
    /// Unterarmdrehung, Handgelenk, jedes Fingerglied, Huefte, Knie, Sprunggelenk, Zehen.
    ///
    /// Jedes Gelenk wird relativ zur Bezugspose des Skeletts (T-Pose des Avatars) in anatomische Achsen zerlegt:
    /// Beugung/Streckung, Seitneigung (Ab-/Adduktion, Speichen-/Ellenneigung) und Drehung um die Knochenachse.
    /// Dazu: gedehnte Knochen, Rucke (sprunghafte Winkelbeschleunigung), Haende/Ellbogen/Waffe im Koerper.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class AnatomyAuditor : MonoBehaviour
    {
        public enum Kind { Spine, Neck, Clavicle, Shoulder, Elbow, Wrist, Hip, Knee, Ankle, Toes, FingerBase, FingerMid, FingerTip, ThumbBase, ThumbMid, ThumbTip }

        private sealed class Joint
        {
            public string Name;
            public Kind Kind;
            public Transform P, C;
            // Bezugsachsen (Elternraum): Richtung des Kindknochens, Beuge- und Seitachse
            public Vector3 pD0, pA, pB;
            // Drehbezug des Kindes im Elternraum und im eigenen Raum
            public Vector3 pCA, pCB, cD, cA;
            public float aMin, aMax, bMin, bMax, tMin, tMax;
            public string aNeg, aPos, bNeg, bPos, tNeg, tPos;
            public float A, B, T;
            public Joint Twin;   // Unterarm: Drehung von Ellbogen + Handgelenk zusammen pruefen
            // Unterarmdrehung direkt: Handflaeche gegen die Beugeachse des Ellbogens (Neutral = Handflaeche parallel dazu)
            public Transform UA;
            public Vector3 uaPalm, handPalm;
            public bool Right;
        }

        public struct Finding
        {
            public string Joint, What;
            public float Value, Limit, Over, Time;
        }

        private Animator _anim;
        private readonly List<Joint> _joints = new List<Joint>();
        private readonly List<Transform> _bones = new List<Transform>();
        private readonly List<float> _refLen = new List<float>();
        private readonly List<bool> _legBones = new List<bool>();
        private bool[] _legBone;
        private Quaternion[] _q1, _q2;
        private float _dtPrev;
        private int _frames;
        private WeaponHandPoser _hands;
        private Transform _hips, _neck, _head, _lUA, _rUA, _lLA, _rLA, _lHand, _rHand, _lMid, _rMid, _lUL, _rUL, _lLL, _rLL, _lFoot, _rFoot;
        private float _halfW, _halfD;

        // Auswertung je Abschnitt
        private readonly Dictionary<string, Finding> _worst = new Dictionary<string, Finding>();
        private readonly Dictionary<string, float> _jerk = new Dictionary<string, float>();
        // 16.10.: Zeitpunkt und Groesse jedes sichtbaren Rucks (Stufe >= 8) – zum Zuordnen (Fussaufsatz? Schichtwechsel?)
        private readonly List<(float t, string bone, float rel, float err, float dt, string ctx)> _jerkEvents = new List<(float, string, float, float, float, string)>();
        public IReadOnlyList<(float t, string bone, float rel, float err, float dt, string ctx)> JerkEvents => _jerkEvents;
        /// <summary>16.10. abends: Zusatzinfo je Ruck (z. B. Clip-Zeit im Animator), vom Motion-Lab gesetzt.</summary>
        public System.Func<string> Kontext;
        private float _stepTime, _stretch, _severity, _capturedSeverity;
        private string _stretchBone;

        /// <summary>Im letzten Bild ist ein neuer schlimmster Verstoss aufgetreten (fuer ein Beweisbild).</summary>
        public bool NewWorst { get; private set; }
        public float Severity => _severity;
        public bool Ready => _joints.Count > 0;
        /// <summary>Automatisch in LateUpdate messen (aus = nur ueber <see cref="Measure"/>).</summary>
        public bool AutoMeasure { get; set; } = true;

        public void Bind(Animator anim)
        {
            _anim = anim;
            _joints.Clear(); _bones.Clear(); _refLen.Clear();
            if (_anim == null || !_anim.isHuman) return;
            _hands = _anim.GetComponent<WeaponHandPoser>();
            var reference = new ReferencePose(_anim);
            Vector3 F = Vector3.forward, U = Vector3.up, R = Vector3.right;
            Transform B(HumanBodyBones b) => _anim.GetBoneTransform(b);
            Transform chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine);
            _hips = B(HumanBodyBones.Hips); _neck = B(HumanBodyBones.Neck); _head = B(HumanBodyBones.Head);
            _lUA = B(HumanBodyBones.LeftUpperArm); _rUA = B(HumanBodyBones.RightUpperArm);
            _lLA = B(HumanBodyBones.LeftLowerArm); _rLA = B(HumanBodyBones.RightLowerArm);
            _lHand = B(HumanBodyBones.LeftHand); _rHand = B(HumanBodyBones.RightHand);
            _lMid = B(HumanBodyBones.LeftMiddleProximal); _rMid = B(HumanBodyBones.RightMiddleProximal);
            _lUL = B(HumanBodyBones.LeftUpperLeg); _rUL = B(HumanBodyBones.RightUpperLeg);
            _lLL = B(HumanBodyBones.LeftLowerLeg); _rLL = B(HumanBodyBones.RightLowerLeg);
            _lFoot = B(HumanBodyBones.LeftFoot); _rFoot = B(HumanBodyBones.RightFoot);
            float span = _lUA != null && _rUA != null ? Vector3.Distance(_lUA.position, _rUA.position) : 0.36f;
            _halfW = span * 0.4f; _halfD = span * 0.27f;

            // Rumpf, Hals
            Add(reference, "Rumpf", Kind.Spine, _hips, chest, _neck != null ? _neck : _head, U, F, R, -35, 65, -40, 40, -50, 50,
                "Hohlkreuz", "Vorbeugen", "Neigung links", "Neigung rechts", "Drehung", "Drehung");
            Add(reference, "Kopf", Kind.Neck, chest, _head, null, U, F, R, -55, 70, -45, 45, -85, 85,
                "Kopf nach hinten", "Kinn zur Brust", "Neigung links", "Neigung rechts", "Drehung", "Drehung");
            foreach (int side in new[] { -1, 1 })
            {
                bool left = side < 0;
                string s = left ? "L-" : "R-";
                Vector3 L = R * side;
                HumanBodyBones Pick(HumanBodyBones l, HumanBodyBones r) => left ? l : r;
                Transform sh = B(Pick(HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder));
                Transform ua = left ? _lUA : _rUA, la = left ? _lLA : _rLA, hand = left ? _lHand : _rHand;
                if (sh != null) Add(reference, s + "Schlüsselbein", Kind.Clavicle, chest, sh, ua, L, U, F, -20, 40, -30, 35, -40, 40,
                    "gesenkt", "hochgezogen", "zurück", "vor", "Drehung", "Drehung");
                Add(reference, s + "Schulter", Kind.Shoulder, sh != null ? sh : chest, ua, la, L, F, U, -70, 180, -80, 180, -100, 100,
                    "nach hinten", "nach vorn", "unten", "oben", "Innendrehung", "Außendrehung");
                Joint elbow = Add(reference, s + "Ellbogen", Kind.Elbow, ua, la, hand, L, F, U, -8, 150, -25, 25, -100, 100,
                    "überstreckt", "gebeugt", "seitlich", "seitlich", "Pronation", "Supination");
                Joint wrist = Add(reference, s + "Handgelenk", Kind.Wrist, la, hand, left ? _lMid : _rMid, L, -U, F, -75, 85, -40, 25, -100, 100,
                    "Streckung", "Beugung", "Ellenneigung", "Speichenneigung", "Pronation", "Supination");
                if (elbow != null && wrist != null)
                {
                    wrist.Twin = elbow;
                    // Bezug: in der T-Pose zeigt die Handflaeche nach unten und liegt parallel zur Beugeachse des Ellbogens
                    wrist.UA = ua;
                    wrist.uaPalm = Quaternion.Inverse(reference.Rot(ua)) * Vector3.down;
                    wrist.handPalm = Quaternion.Inverse(reference.Rot(hand)) * Vector3.down;
                    wrist.Right = !left;
                }
                string[] fingers = { "Daumen", "Zeigefinger", "Mittelfinger", "Ringfinger", "kleiner Finger" };
                HumanBodyBones first = left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal;
                for (int f = 0; f < 5; f++)
                {
                    Transform p0 = hand;
                    for (int g = 0; g < 3; g++)
                    {
                        Transform c = B(first + f * 3 + g);
                        if (c == null || p0 == null) break;
                        Transform tip = g < 2 ? B(first + f * 3 + g + 1) : null;
                        bool thumb = f == 0;
                        Kind k = thumb ? (Kind)((int)Kind.ThumbBase + g) : (Kind)((int)Kind.FingerBase + g);
                        float aMin, aMax, bLim, tLim;
                        if (thumb) { aMin = g == 0 ? -90 : -35; aMax = g == 0 ? 90 : g == 1 ? 85 : 95; bLim = g == 0 ? 90 : 40; tLim = g == 0 ? 50 : 30; }
                        else { aMin = g == 0 ? -35 : g == 1 ? -8 : -20; aMax = g == 0 ? 95 : g == 1 ? 115 : 95; bLim = g == 0 ? 30 : 12; tLim = g == 0 ? 25 : 15; }
                        Add(reference, s + fingers[f] + " " + (g + 1) + ". Glied", k, p0, c, tip, L, -U, F, aMin, aMax, -bLim, bLim, -tLim, tLim,
                            "überstreckt", "gebeugt", "seitlich", "seitlich", "verdreht", "verdreht");
                        p0 = c;
                    }
                }
                Transform ul = left ? _lUL : _rUL, ll = left ? _lLL : _rLL, foot = left ? _lFoot : _rFoot;
                Transform toes = B(Pick(HumanBodyBones.LeftToes, HumanBodyBones.RightToes));
                Add(reference, s + "Hüfte", Kind.Hip, _hips, ul, ll, -U, F, L, -35, 130, -30, 55, -45, 55,
                    "Bein nach hinten", "Bein nach vorn", "Bein nach innen", "Bein nach außen", "Innendrehung", "Außendrehung");
                Add(reference, s + "Knie", Kind.Knee, ul, ll, foot, -U, -F, L, -8, 155, -15, 15, -40, 40,
                    "überstreckt", "gebeugt", "X-Bein", "O-Bein", "Drehung", "Drehung");
                Add(reference, s + "Sprunggelenk", Kind.Ankle, ll, foot, toes, F, U, L, -55, 35, -30, 30, -35, 35,
                    "Spitze runter", "Spitze hoch", "nach innen", "nach außen", "Drehung", "Drehung");
                if (toes != null) Add(reference, s + "Zehen", Kind.Toes, foot, toes, null, F, U, L, -45, 75, -20, 20, -20, 20,
                    "gekrallt", "hochgezogen", "seitlich", "seitlich", "verdreht", "verdreht");
            }

            // Knochenlaengen: Abstand Kind↔Elternknochen muss bleiben (Bezug = Skelett)
            _legBones.Clear();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var hb = (HumanBodyBones)i;
                Transform t = B(hb);
                if (t == null) continue;
                _bones.Add(t);
                _legBones.Add(hb == HumanBodyBones.LeftUpperLeg || hb == HumanBodyBones.RightUpperLeg || hb == HumanBodyBones.LeftLowerLeg ||
                              hb == HumanBodyBones.RightLowerLeg || hb == HumanBodyBones.LeftFoot || hb == HumanBodyBones.RightFoot ||
                              hb == HumanBodyBones.LeftToes || hb == HumanBodyBones.RightToes);
                _refLen.Add(reference.LocalPos(t).magnitude);
            }
            _legBone = _legBones.ToArray();
            _q1 = new Quaternion[_bones.Count];
            _q2 = new Quaternion[_bones.Count];
            _frames = 0;
        }

        /// <param name="distal">Richtung des Kindknochens in der Bezugspose, falls es keinen Folgeknochen gibt.</param>
        private Joint Add(ReferencePose rp, string name, Kind kind, Transform p, Transform c, Transform tip, Vector3 distal,
            Vector3 aWorld, Vector3 bWorld, float aMin, float aMax, float bMin, float bMax, float tMin, float tMax,
            string aNeg, string aPos, string bNeg, string bPos, string tNeg, string tPos)
        {
            if (p == null || c == null) return null;
            Quaternion rpP = rp.Rot(p), rpC = rp.Rot(c);
            Vector3 d = tip != null ? rp.Pos(tip) - rp.Pos(c) : Vector3.zero;
            if (d.sqrMagnitude < 1e-8f)
            {
                // Letztes Glied: Richtung aus dem eigenen Kind (falls vorhanden), sonst die Vorgabe
                Transform child = c.childCount > 0 ? c.GetChild(0) : null;
                d = child != null ? rp.Rot(c) * child.localPosition : Vector3.zero;
                if (d.sqrMagnitude < 1e-8f) d = rp.Pos(c) - rp.Pos(p);
                if (d.sqrMagnitude < 1e-8f || kind == Kind.Neck) d = distal;
            }
            d.Normalize();
            Vector3 a = Vector3.ProjectOnPlane(aWorld, d);
            if (a.sqrMagnitude < 1e-6f) a = Vector3.ProjectOnPlane(bWorld, d);
            a.Normalize();
            Vector3 b = Vector3.Cross(d, a);
            if (Vector3.Dot(b, bWorld) < 0f) b = -b;
            Quaternion ip = Quaternion.Inverse(rpP), ic = Quaternion.Inverse(rpC);
            var j = new Joint
            {
                Name = name, Kind = kind, P = p, C = c,
                pD0 = ip * d, pA = ip * a, pB = ip * b, pCA = ip * a, pCB = ip * b, cD = ic * d, cA = ic * a,
                aMin = aMin, aMax = aMax, bMin = bMin, bMax = bMax, tMin = tMin, tMax = tMax,
                aNeg = aNeg, aPos = aPos, bNeg = bNeg, bPos = bPos, tNeg = tNeg, tPos = tPos
            };
            _joints.Add(j);
            return j;
        }

        // ------------------------------------------------------------------ Abschnitte

        public void BeginStep()
        {
            _worst.Clear(); _jerk.Clear(); _jerkEvents.Clear();
            _stepTime = 0f; _stretch = 0f; _stretchBone = null; _severity = 0f; _capturedSeverity = 0f;
            _frames = 0;
            NewWorst = false;
        }

        /// <summary>Beweisbild wurde aufgenommen – erst ein deutlich schlimmerer Verstoss loest das naechste aus.</summary>
        public void MarkCaptured() { _capturedSeverity = _severity; NewWorst = false; }

        private void LateUpdate()
        {
            if (AutoMeasure) Measure(Time.deltaTime);
        }

        /// <summary>Aktuelle Pose messen (dt = Zeit seit dem letzten Bild; 0 = ohne Ruckmessung).</summary>
        public void Measure(float dt)
        {
            if (_anim == null || _joints.Count == 0) return;
            _stepTime += dt;
            foreach (Joint j in _joints) Angles(j);
            foreach (Joint j in _joints) Check(j);
            Stretch();
            Jerk(dt);
            Penetration();
            if ((_frames & 1) == 0) MeshPenetration();
            NewWorst = _severity > _capturedSeverity + 4f;
        }

        private static void Angles(Joint j)
        {
            Quaternion pr = j.P.rotation, cr = j.C.rotation;
            Vector3 d0 = pr * j.pD0, a = pr * j.pA, b = pr * j.pB;
            Vector3 d = cr * j.cD;
            j.A = Mathf.Atan2(Vector3.Dot(d, a), Vector3.Dot(d, d0)) * Mathf.Rad2Deg;
            j.B = Mathf.Asin(Mathf.Clamp(Vector3.Dot(d, b), -1f, 1f)) * Mathf.Rad2Deg;
            // Drehung um die eigene Achse: gegen die reine Schwenkung aus der Bezugspose
            Quaternion swing = Quaternion.FromToRotation(d0, d);
            Vector3 ea = swing * (pr * j.pCA), eb = swing * (pr * j.pCB);
            Vector3 ca = cr * j.cA;
            j.T = Mathf.Atan2(Vector3.Dot(ca, eb), Vector3.Dot(ca, ea)) * Mathf.Rad2Deg;
        }

        private void Check(Joint j)
        {
            if (j.Kind == Kind.Shoulder) { CheckShoulder(j); return; }
            Over(j.Name, j.A, j.aMin, j.aMax, j.aNeg, j.aPos);
            // Daumen-Endglied: Seitlage haengt am Avatar-Bezug des Daumens (T-Pose-Korrektur) – nur Beugung pruefen
            if (j.Kind != Kind.ThumbTip) Over(j.Name, j.B, j.bMin, j.bMax, j.bNeg, j.bPos);
            if (j.Kind == Kind.Elbow) return; // Drehung des Unterarms zusammen mit dem Handgelenk
            if (j.Kind == Kind.Wrist && j.UA != null)
            {
                Over(j.Name.Replace("Handgelenk", "Unterarm"), Pronation(j), j.tMin, j.tMax, j.tNeg, j.tPos);
                return;
            }
            Over(j.Name, j.T, j.tMin, j.tMax, j.tNeg, j.tPos);
        }

        /// <summary>Pronation (−) / Supination (+) des Unterarms, unabhaengig von der Ellbogenbeugung.</summary>
        private static float Pronation(Joint j)
        {
            Vector3 fore = j.C.position - j.P.position;           // Unterarm (Ellbogen → Handgelenk)
            if (fore.sqrMagnitude < 1e-8f) return 0f;
            fore.Normalize();
            Vector3 a = Vector3.ProjectOnPlane(j.UA.rotation * j.uaPalm, fore);
            Vector3 b = Vector3.ProjectOnPlane(j.C.rotation * j.handPalm, fore);
            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(a, b, fore) * (j.Right ? -1f : 1f);
        }

        private void CheckShoulder(Joint j)
        {
            // Richtung des Oberarms in Brustachsen (Bezug: Skelett) – Bewegungsraum der Schulter statt Einzelwinkel
            Vector3 d = j.C.rotation * j.cD;
            Vector3 f = _anim.transform.forward, u = _anim.transform.up;
            if (_hips != null)
            {
                Transform chest = j.P;
                // Brustachsen: vorwaerts/oben der aktuellen Brust
                Vector3 up = (_neck != null ? _neck.position : chest.position) - (_hips != null ? _hips.position : chest.position);
                if (up.sqrMagnitude > 1e-6f) u = up.normalized;
                Vector3 across = _lUA != null && _rUA != null ? (_rUA.position - _lUA.position) : _anim.transform.right;
                f = Vector3.Cross(across, u).normalized;
            }
            bool left = j.Name.StartsWith("L");
            Vector3 lat = Vector3.Cross(u, f).normalized * (left ? -1f : 1f);
            float df = Vector3.Dot(d, f), du = Vector3.Dot(d, u), dl = Vector3.Dot(d, lat);
            // Nach hinten: Winkel hinter der Frontalebene (Oberarm unterhalb der Schulter) – Grenze ~60°
            float back = Mathf.Asin(Mathf.Clamp(-df, -1f, 1f)) * Mathf.Rad2Deg;
            if (du < 0.3f) Over(j.Name, back, -200f, 60f, "", "zu weit nach hinten");
            // Quer vor/durch den Koerper: zur Gegenseite nur vor dem Rumpf moeglich
            float medial = Mathf.Asin(Mathf.Clamp(-dl, -1f, 1f)) * Mathf.Rad2Deg;
            float allowed = df > 0.35f ? 75f : 20f;
            Over(j.Name, medial, -200f, allowed, "", df > 0.35f ? "zu weit quer vor den Körper" : "Arm in den Rumpf");
            // Drehung des Oberarms (nur wenn die Schwenkung eindeutig ist)
            float swing = Vector3.Angle(j.P.rotation * j.pD0, d);
            // 22.10. nachts: die Zerlegung Schwenk/Drehung wird schon ab ~100° Schwenk aus der T-Pose mehrdeutig (Angriffsfilm: im
            // selben Abschnitt gleichzeitig „Innendrehung −176°“ und „Aussendrehung +156°“ bei unauffaelligen Bildern) – erst
            // eine anatomische Drehachse (Ellbogenebene gegen Koerperbezug) kann dort messen. Bis dahin nur bis 100° pruefen.
            if (swing < 100f) Over(j.Name, j.T, j.tMin, j.tMax, j.tNeg, j.tPos);
        }

        private void Over(string joint, float v, float min, float max, string negName, string posName)
        {
            float over = v > max ? v - max : v < min ? min - v : 0f;
            if (over <= 3f) return; // 3° Toleranz (Messung, Hautverschiebung)
            string what = v > max ? posName : negName;
            string key = joint + "|" + what;
            if (!_worst.TryGetValue(key, out Finding f) || over > f.Over)
                _worst[key] = new Finding { Joint = joint, What = what, Value = v, Limit = v > max ? max : min, Over = over, Time = _stepTime };
            _severity = Mathf.Max(_severity, over);
        }

        private void Stretch()
        {
            for (int i = 0; i < _bones.Count; i++)
            {
                Transform t = _bones[i];
                if (t == _hips || _refLen[i] < 1e-4f) continue;
                float dev = Mathf.Abs(t.localPosition.magnitude - _refLen[i]) / _refLen[i];
                if (dev > _stretch) { _stretch = dev; _stretchBone = t.name; }
            }
            if (_stretch > 0.01f) _severity = Mathf.Max(_severity, _stretch * 100f);
        }

        /// <summary>Ruck: Abweichung von der Fortsetzung der bisherigen Drehbewegung, auf 60 Bilder/s normiert (°).</summary>
        private void Jerk(float dt)
        {
            for (int i = 0; i < _bones.Count; i++)
            {
                Quaternion q0 = _bones[i].localRotation;
                // Unregelmaessige Bilder (Aufnahme-Haenger) nicht werten – dort rechnen Federn anders als im Spiel
                float ratio = _dtPrev > 1e-4f ? dt / _dtPrev : 0f;
                if (_frames >= 2 && dt > 1e-4f && _dtPrev > 1e-4f && ratio > 0.67f && ratio < 1.5f)
                {
                    Quaternion step = Quaternion.Inverse(_q2[i]) * _q1[i];
                    step.ToAngleAxis(out float ang, out Vector3 axis);
                    if (ang > 180f) ang -= 360f;
                    Quaternion pred = _q1[i] * Quaternion.AngleAxis(ang * (dt / _dtPrev), axis);
                    float err = Quaternion.Angle(pred, q0);
                    float norm = err * (1f / 60f) * (1f / 60f) / (dt * dt);
                    string name = _bones[i].name;
                    // Beine: Fussaufsatz ist ein echter harter Stopp (Rohclips bis ~35) – erst darueber zaehlen
                    float thr = _legBone[i] ? 40f : 12f;
                    float rel = norm / thr * 8f;       // auf die gemeinsame Skala (8 = sichtbar) bringen
                    // 16.10. abends: die ersten 0,3 s eines Abschnitts nicht werten – dort liegen Schichtwechsel und die erste
                    // Bildaufnahme (Aufnahme-Haenger), Stufen bis 260 ohne Aussagekraft
                    if (_stepTime < 0.3f) rel = 0f;
                    if (!_jerk.TryGetValue(name, out float m) || rel > m) _jerk[name] = rel;
                    if (rel > 8f) { _severity = Mathf.Max(_severity, rel); if (_jerkEvents.Count < 400) _jerkEvents.Add((_stepTime, name, rel, err, dt, Kontext != null ? Kontext() : "")); }
                }
                _q2[i] = _q1[i];
                _q1[i] = q0;
            }
            _dtPrev = dt;
            _frames++;
        }

        public void ResetMotionHistory() { _frames = 0; }

        // ------------------------------------------------------------------ Durchdringung

        private void Penetration()
        {
            if (_hips == null || _neck == null) return;
            Vector3 u = (_neck.position - _hips.position).normalized;
            Vector3 across = _lUA != null && _rUA != null ? (_rUA.position - _lUA.position) : _anim.transform.right;
            Vector3 f = Vector3.Cross(across, u).normalized, r = Vector3.Cross(u, f).normalized;
            Probe("R-Hand", Palm(_rHand, _rMid), f, r, u, null);
            Probe("L-Hand", Palm(_lHand, _lMid), f, r, u, null);
            if (_rLA != null) Probe("R-Ellbogen", _rLA.position, f, r, u, null);
            if (_lLA != null) Probe("L-Ellbogen", _lLA.position, f, r, u, null);
            Transform pivot = _hands != null ? _hands.Pivot : null;
            if (pivot != null && pivot.gameObject.activeInHierarchy && ((_rHand != null && pivot.IsChildOf(_rHand)) || (_lHand != null && pivot.IsChildOf(_lHand))))
            {
                if (!WeaponAxis(pivot, out Vector3 a0, out Vector3 a1)) return;
                for (int i = 0; i <= 10; i++)
                {
                    Vector3 p = pivot.TransformPoint(Vector3.Lerp(a0, a1, i / 10f));
                    if (_rHand != null && Vector3.Distance(p, _rHand.position) < 0.12f) continue;
                    if (_lHand != null && Vector3.Distance(p, _lHand.position) < 0.12f) continue;
                    Probe("Waffe", p, f, r, u, null);
                }
            }
        }

        // 21.10. abends (Max: „Schild auf dem Rücken clippt durch den Arm – erkenne so etwas selbst“): echte Hautoberflaeche
        // (gebackener Koerper) gegen jedes starre Ausruestungsteil (Schild, Waffen, Scheiden). Teile aus ProcMesh/Prefabs
        // werden als Scheibe/Zylinder (zwei gleiche Ausdehnungen) oder Quader ihrer Modellgrenzen genaehert.
        private Mesh _bakedBody;
        private readonly List<Vector3> _skin = new List<Vector3>();
        private readonly List<MeshFilter> _rigid = new List<MeshFilter>();

        private void MeshPenetration()
        {
            Visuals.AppearanceRig rig = _anim.GetComponentInChildren<Visuals.AppearanceRig>();
            SkinnedMeshRenderer body = rig != null ? rig.Body : null;
            if (body == null || !body.gameObject.activeInHierarchy) return;
            if (_bakedBody == null) _bakedBody = new Mesh { name = "Audit Haut" };
            body.BakeMesh(_bakedBody, true);
            _bakedBody.GetVertices(_skin);
            Matrix4x4 toWorld = Matrix4x4.TRS(body.transform.position, body.transform.rotation, Vector3.one);
            int step = Mathf.Max(1, _skin.Count / 8000);

            _rigid.Clear();
            _anim.GetComponentsInChildren(false, _rigid);
            foreach (MeshFilter mf in _rigid)
            {
                if (mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
                Transform t = mf.transform;
                if (_head != null && t.IsChildOf(_head)) continue; // Haare, Augen, Helmteile
                // Greifende Hand: Teil an Hand ODER Unterarm (Schild am Mittelgriff) – Haut der Hand um die Handflaeche zaehlt nicht
                Transform holder = (_rHand != null && t.IsChildOf(_rHand)) || (_rLA != null && t.IsChildOf(_rLA)) ? _rHand
                    : (_lHand != null && t.IsChildOf(_lHand)) || (_lLA != null && t.IsChildOf(_lLA)) ? _lHand : null;
                Vector3 palm = holder == _rHand ? Palm(_rHand, _rMid) : Palm(_lHand, _lMid);
                Vector3 palm2 = _hands != null && _hands.TwoHandWeight > 0.5f ? (holder == _rHand ? Palm(_lHand, _lMid) : Palm(_rHand, _rMid)) : palm;
                Bounds mb = mf.sharedMesh.bounds;
                Vector3 e = mb.extents, sc = t.lossyScale;
                Vector3 ew = new Vector3(e.x * Mathf.Abs(sc.x), e.y * Mathf.Abs(sc.y), e.z * Mathf.Abs(sc.z));
                if (ew.x + ew.y + ew.z < 0.03f) continue; // Nieten, Kleinteile
                // Form: Achse mit abweichender Ausdehnung = Zylinderachse (Scheibe oder Stab), sonst Quader
                int ax = -1;
                if (Near(ew.x, ew.y)) ax = 2; else if (Near(ew.x, ew.z)) ax = 1; else if (Near(ew.y, ew.z)) ax = 0;
                float worst = 0f; Vector3 worstP = Vector3.zero;
                Bounds wb = mr.bounds; wb.Expand(0.01f);
                for (int i = 0; i < _skin.Count; i += step)
                {
                    Vector3 p = toWorld.MultiplyPoint3x4(_skin[i]);
                    if (!wb.Contains(p)) continue;
                    if (holder != null && (Vector3.Distance(p, palm) < 0.12f || Vector3.Distance(p, palm2) < 0.12f)) continue; // greifende Hand(e) – auch die Stuetzhand am Zweihaender
                    Vector3 q = t.InverseTransformPoint(p) - mb.center;
                    Vector3 qw = new Vector3(q.x * Mathf.Abs(sc.x), q.y * Mathf.Abs(sc.y), q.z * Mathf.Abs(sc.z));
                    float depth;
                    if (ax >= 0)
                    {
                        float axial = ew[ax] - Mathf.Abs(qw[ax]);
                        int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
                        float rx = qw[a1] / Mathf.Max(1e-4f, ew[a1]), ry = qw[a2] / Mathf.Max(1e-4f, ew[a2]);
                        float radial = (1f - Mathf.Sqrt(rx * rx + ry * ry)) * Mathf.Min(ew[a1], ew[a2]);
                        depth = Mathf.Min(axial, radial);
                    }
                    else depth = Mathf.Min(ew.x - Mathf.Abs(qw.x), Mathf.Min(ew.y - Mathf.Abs(qw.y), ew.z - Mathf.Abs(qw.z)));
                    if (depth > worst) { worst = depth; worstP = p; }
                }
                if (worst * 100f >= 0.8f) MeshHit("Haut in " + PartName(t) + " (" + NearestBone(worstP) + ")", worst * 100f);
            }

            // Teil gegen Teil (Klinge durch den Schild, Waffe durch die Scheide der anderen): Punkte des einen Teils in der Form
            // des anderen – nur zwischen verschiedenen Ausruestungsstuecken (Eltern verschieden)
            for (int a = 0; a < _rigid.Count; a++)
            for (int b = 0; b < _rigid.Count; b++)
            {
                MeshFilter A = _rigid[a], Bm = _rigid[b];
                if (a == b || A.sharedMesh == null || Bm.sharedMesh == null || A.transform.parent == Bm.transform.parent) continue;
                if ((_head != null && (A.transform.IsChildOf(_head) || Bm.transform.IsChildOf(_head)))) continue;
                MeshRenderer ra = A.GetComponent<MeshRenderer>(), rb = Bm.GetComponent<MeshRenderer>();
                if (ra == null || rb == null || !ra.enabled || !rb.enabled || !ra.bounds.Intersects(rb.bounds)) continue;
                Transform tb = Bm.transform;
                Bounds mb = Bm.sharedMesh.bounds;
                Vector3 sc = tb.lossyScale, e = mb.extents;
                Vector3 ew = new Vector3(e.x * Mathf.Abs(sc.x), e.y * Mathf.Abs(sc.y), e.z * Mathf.Abs(sc.z));
                if (ew.x + ew.y + ew.z < 0.03f) continue;
                int ax = -1;
                if (Near(ew.x, ew.y)) ax = 2; else if (Near(ew.x, ew.z)) ax = 1; else if (Near(ew.y, ew.z)) ax = 0;
                A.sharedMesh.GetVertices(_partVerts);
                int pstep = Mathf.Max(1, _partVerts.Count / 200);
                float worst = 0f;
                for (int i = 0; i < _partVerts.Count; i += pstep)
                {
                    Vector3 q = tb.InverseTransformPoint(A.transform.TransformPoint(_partVerts[i])) - mb.center;
                    Vector3 qw = new Vector3(q.x * Mathf.Abs(sc.x), q.y * Mathf.Abs(sc.y), q.z * Mathf.Abs(sc.z));
                    float depth = Depth(qw, ew, ax);
                    if (depth > worst) worst = depth;
                }
                if (worst * 100f >= 1f) MeshHit(PartName(A.transform) + " in " + PartName(tb), worst * 100f);
            }
        }

        private readonly List<Vector3> _partVerts = new List<Vector3>();

        private static float Depth(Vector3 qw, Vector3 ew, int ax)
        {
            if (ax < 0) return Mathf.Min(ew.x - Mathf.Abs(qw.x), Mathf.Min(ew.y - Mathf.Abs(qw.y), ew.z - Mathf.Abs(qw.z)));
            float axial = ew[ax] - Mathf.Abs(qw[ax]);
            int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
            float rx = qw[a1] / Mathf.Max(1e-4f, ew[a1]), ry = qw[a2] / Mathf.Max(1e-4f, ew[a2]);
            return Mathf.Min(axial, (1f - Mathf.Sqrt(rx * rx + ry * ry)) * Mathf.Min(ew[a1], ew[a2]));
        }

        /// <summary>Lesbarer Name: Teil + die naechsten Eltern bis zum Knochen/Griff („Scheide (HipLeft)/Weapon Model/Cylinder“).</summary>
        private static string PartName(Transform t)
        {
            string n = t.name.StartsWith("Equip Part") ? t.name.Substring(11) : t.name;
            Transform p = t.parent;
            for (int i = 0; i < 3 && p != null; i++, p = p.parent)
            {
                n = p.name + "/" + n;
                if (p.name.StartsWith("mixamorig:") || p.name.StartsWith("Scheide") || p.name.Contains("(")) break;
            }
            return n;
        }

        private void MeshHit(string label, float cm)
        {
            string key = "Koerper|" + label;
            if (!_worst.TryGetValue(key, out Finding f) || cm > f.Over)
                _worst[key] = new Finding { Joint = label, What = "cm", Value = cm, Limit = 0f, Over = cm, Time = _stepTime };
            _severity = Mathf.Max(_severity, cm * 2f);
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) <= 0.12f * Mathf.Max(a, b);

        private string NearestBone(Vector3 p)
        {
            string best = "?"; float bd = float.MaxValue;
            foreach (Transform b in _bones)
            {
                float d = (b.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = b.name; }
            }
            return best;
        }

        private static Vector3 Palm(Transform hand, Transform mid) => hand == null ? Vector3.zero : mid != null ? (hand.position + mid.position) * 0.5f : hand.position;

        private void Probe(string what, Vector3 p, Vector3 f, Vector3 r, Vector3 u, Transform ignore)
        {
            if (p == Vector3.zero) return;
            // Rumpf: elliptischer Zylinder Huefte → Hals
            Vector3 h = _hips.position, n = _neck.position;
            float len = Vector3.Dot(n - h, u);
            float along = Vector3.Dot(p - h, u);
            if (along > -0.08f && along < len + 0.02f)
            {
                Vector3 o = p - (h + u * Mathf.Clamp(along, 0f, len));
                float x = Vector3.Dot(o, r) / _halfW, y = Vector3.Dot(o, f) / _halfD;
                float e = Mathf.Sqrt(x * x + y * y);
                if (e < 1f) Hit(what + " im Rumpf", (1f - e) * _halfD * 100f);
            }
            // Oberschenkel
            Capsule(what + " im Oberschenkel", p, _lUL, _lLL, 0.07f);
            Capsule(what + " im Oberschenkel", p, _rUL, _rLL, 0.07f);
            if (_head != null)
            {
                float dh = Vector3.Distance(p, _head.position + u * 0.08f);
                if (dh < 0.1f) Hit(what + " im Kopf", (0.1f - dh) * 100f);
            }
        }

        private void Capsule(string label, Vector3 p, Transform a, Transform b, float radius)
        {
            if (a == null || b == null) return;
            Vector3 ab = b.position - a.position;
            float t = Mathf.Clamp01(Vector3.Dot(p - a.position, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            float d = Vector3.Distance(p, a.position + ab * t);
            if (d < radius) Hit(label, (radius - d) * 100f);
        }

        private void Hit(string label, float cm)
        {
            if (cm <= 2.5f) return; // Naeherung des Koerpers: erst deutliches Eindringen zaehlen
            string key = "Koerper|" + label;
            if (!_worst.TryGetValue(key, out Finding f) || cm > f.Over)
                _worst[key] = new Finding { Joint = label, What = "cm", Value = cm, Limit = 0f, Over = cm, Time = _stepTime };
            _severity = Mathf.Max(_severity, cm * 2f);
        }

        private readonly Dictionary<Transform, (Vector3, Vector3)> _axisCache = new Dictionary<Transform, (Vector3, Vector3)>();

        /// <summary>Laengsachse der Waffe im Raum des Drehpunkts (aus den Modellgrenzen).</summary>
        public bool WeaponAxis(Transform pivot, out Vector3 a0, out Vector3 a1)
        {
            if (_axisCache.TryGetValue(pivot, out var c)) { a0 = c.Item1; a1 = c.Item2; return true; }
            a0 = a1 = Vector3.zero;
            bool any = false;
            Bounds bb = default;
            foreach (MeshFilter mf in pivot.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 lp = pivot.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { bb = new Bounds(lp, Vector3.zero); any = true; } else bb.Encapsulate(lp);
                }
            }
            if (!any) return false;
            Vector3 s = bb.size;
            Vector3 axis = s.x >= s.y && s.x >= s.z ? Vector3.right : s.y >= s.z ? Vector3.up : Vector3.forward;
            float half = Vector3.Dot(bb.extents, axis);
            a0 = bb.center - axis * half; a1 = bb.center + axis * half;
            _axisCache[pivot] = (a0, a1);
            return true;
        }

        // ------------------------------------------------------------------ Bericht

        /// <summary>Befunde des Abschnitts (schlimmste zuerst), leer = sauber.</summary>
        public List<Finding> Findings()
        {
            var list = new List<Finding>(_worst.Values);
            list.Sort((a, b) => b.Over.CompareTo(a.Over));
            return list;
        }

        public string Report(int max = 12)
        {
            var sb = new StringBuilder();
            List<Finding> list = Findings();
            int n = 0;
            foreach (Finding f in list)
            {
                if (n++ >= max) { sb.Append($"      … und {list.Count - max} weitere\n"); break; }
                if (f.What == "cm") sb.Append(string.Format(CultureInfo.InvariantCulture, "      ✗ {0}: {1:0.0} cm tief bei {2:0.00} s\n", f.Joint, f.Value, f.Time));
                else sb.Append(string.Format(CultureInfo.InvariantCulture, "      ✗ {0} {1}: {2:0}° (Grenze {3:0}°, {4:0} zu viel) bei {5:0.00} s\n", f.Joint, f.What, f.Value, f.Limit, f.Over, f.Time));
            }
            if (_stretch > 0.01f) sb.Append(string.Format(CultureInfo.InvariantCulture, "      ✗ Knochen gedehnt: {0} {1:0.0} %\n", _stretchBone, _stretch * 100f));
            var jerks = new List<KeyValuePair<string, float>>(_jerk);
            jerks.Sort((a, b) => b.Value.CompareTo(a.Value));
            int k = 0;
            foreach (var kv in jerks)
            {
                if (kv.Value < 8f || k++ >= 4) break;
                sb.Append(string.Format(CultureInfo.InvariantCulture, "      ✗ Ruck {0}: Stufe {1:0.0}\n", kv.Key, kv.Value));
            }
            // 16.10.: die groessten Rucke mit Zeitpunkt (Sekunden ab Messbeginn), Rohfehler in Grad und Bildzeit
            if (_jerkEvents.Count > 0)
            {
                var ev = new List<(float t, string bone, float rel, float err, float dt, string ctx)>(_jerkEvents);
                ev.Sort((a, b) => b.rel.CompareTo(a.rel));
                sb.Append("      Rucke (Zeit s · Knochen · Stufe · roh ° · Bild ms · Kontext): ");
                for (int i = 0; i < ev.Count && i < 24; i++)
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.00} {1} {2:0.0} {3:0.0}° {4:0.0}ms {5} | ", ev[i].t, ev[i].bone, ev[i].rel, ev[i].err, ev[i].dt * 1000f, ev[i].ctx));
                sb.Append(string.Format(CultureInfo.InvariantCulture, "insgesamt {0}\n", _jerkEvents.Count));
            }
            return sb.ToString();
        }

        public float MaxJerk(out string bone)
        {
            bone = null; float m = 0f;
            foreach (var kv in _jerk) if (kv.Value > m) { m = kv.Value; bone = kv.Key; }
            return m;
        }

        public int Count => _worst.Count + (_stretch > 0.01f ? 1 : 0);

        /// <summary>Aktuelle Werte eines Gelenks (Beugung, Seite, Drehung) – fuer Tests.</summary>
        public bool TryGet(string joint, out Vector3 abt)
        {
            foreach (Joint j in _joints) if (j.Name == joint) { abt = new Vector3(j.A, j.B, j.T); return true; }
            abt = default; return false;
        }

        // ------------------------------------------------------------------ Bezugspose

        /// <summary>Bezugspose aus dem Avatar (Skelett, mit dem der Humanoid eingerichtet wurde = T-Pose), im Modellraum.</summary>
        private sealed class ReferencePose
        {
            private readonly Transform _root;
            private readonly Dictionary<string, SkeletonBone> _sk = new Dictionary<string, SkeletonBone>();
            private readonly Dictionary<Transform, Quaternion> _rot = new Dictionary<Transform, Quaternion>();
            private readonly Dictionary<Transform, Vector3> _pos = new Dictionary<Transform, Vector3>();

            public ReferencePose(Animator anim)
            {
                _root = anim.transform;
                if (anim.avatar != null)
                {
                    SkeletonBone[] bones = anim.avatar.humanDescription.skeleton;
                    if (bones != null) foreach (SkeletonBone b in bones) if (!_sk.ContainsKey(b.name)) _sk.Add(b.name, b);
                }
                if (_sk.Count == 0) Debug.LogWarning("[Ashveil] Anatomie: Avatar ohne Skelettbezug – nehme die aktuelle Pose als Bezug.");
            }

            public Vector3 LocalPos(Transform t) => t != _root && _sk.TryGetValue(t.name, out SkeletonBone b) ? b.position : t.localPosition;

            public Quaternion Rot(Transform t)
            {
                if (t == null || t == _root) return Quaternion.identity;
                if (_rot.TryGetValue(t, out Quaternion q)) return q;
                Quaternion local = _sk.TryGetValue(t.name, out SkeletonBone b) ? b.rotation : t.localRotation;
                q = Rot(t.parent) * local;
                _rot[t] = q;
                return q;
            }

            public Vector3 Pos(Transform t)
            {
                if (t == null || t == _root) return Vector3.zero;
                if (_pos.TryGetValue(t, out Vector3 p)) return p;
                p = Pos(t.parent) + Rot(t.parent) * LocalPos(t);
                _pos[t] = p;
                return p;
            }
        }
    }
}
