using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>Fingerhaltung einer Hand. Werte je Finger und Gelenk in <see cref="ArmRig"/>.</summary>
    public enum HandShape : byte
    {
        /// <summary>Locker haengende Hand (leicht gekruemmt, zum kleinen Finger hin mehr).</summary>
        Relaxed = 0,
        /// <summary>Fast gestreckt (greift gleich zu / laesst los).</summary>
        Open = 1,
        /// <summary>Halb geschlossen – freie Hand in Deckung.</summary>
        LooseFist = 2,
        /// <summary>Geschlossene Faust (waffenlos).</summary>
        Fist = 3,
        /// <summary>Um einen Griff von ~3,5 cm geschlossen, Daumen darueber.</summary>
        Grip = 4,
        /// <summary>Wie Griff, Zeigefinger gestreckt am Abzug.</summary>
        Trigger = 5,
        /// <summary>Stuetzhand unter Lauf oder Schaft (offen gekruemmt).</summary>
        Support = 6,
        /// <summary>Hohle Hand, Handflaeche nach oben (Fokus).</summary>
        Cupped = 7
    }

    /// <summary>
    /// Ein Arm von der Schulter bis zu den Fingerspitzen – anatomisch gefuehrt:
    ///  • Ellbogen ist ein Scharnier: der Oberarm wird so um seine Achse gedreht, dass die Beugeachse des Ellbogens
    ///    (aus der Animation gelernt) senkrecht auf der Armebene steht – kein verdrehter Oberarm, kein seitlich
    ///    abknickender Ellbogen.
    ///  • Handgelenk bleibt in seinem natuerlichen Bereich: der Ellbogen wird so gelegt, dass der Unterarm in
    ///    Verlaengerung der Hand liegt (plus gewollte Abwinkelung zur Kleinfingerseite); die Drehung um die
    ///    Unterarmachse traegt der Unterarm (Elle/Speiche), nicht das Handgelenk. Rest wird begrenzt.
    ///  • Finger: jedes Glied einzeln, absolut aus dem Handrahmen gebaut – unabhaengig davon, was die Animation
    ///    gerade mit den Fingern macht; Formwechsel werden weich ueberblendet.
    /// Rahmen der Hand wie beim Anhaengen der Waffe (<c>CharacterBodySwitcher.AttachToHand</c>):
    /// vorwaerts = Knoechelachse (kleiner Finger → Zeigefinger), oben = Handflaechen-Normale, Mitte = Faustmitte.
    /// </summary>
    public sealed class ArmRig
    {
        public readonly bool Left;
        public Transform Upper, Lower, Hand;
        /// <summary>Schluesselbein (kann fehlen) – schiebt die Schulter beim Greifen nach vorn/innen (Reichweite).</summary>
        public Transform Clavicle;
        private readonly Transform[,] _fing = new Transform[5, 3]; // 0 Daumen, 1 Zeige, 2 Mittel, 3 Ring, 4 Klein
        private readonly Vector3[] _tipLocal = new Vector3[5];
        private Quaternion _frameLocal = Quaternion.identity;
        private Vector3 _centerLocal, _dirLocal;
        private Vector3 _hingeUpperLocal;
        private bool _hingeKnown, _measured;
        public bool Valid { get; private set; }

        // Aktuelle (weich nachgefuehrte) Fingerbeugung: [Finger, Gelenk]
        private readonly float[,] _flex = new float[5, 3];
        private bool _flexInit;

        public ArmRig(bool left) { Left = left; }

        public void Bind(Animator a)
        {
            // 22.10. (Codex): Auflösungsspeicher gehören zum gebundenen Skelett – bei neuem Animator leeren
            System.Array.Clear(_swApplied, 0, 2); System.Array.Clear(_restApplied, 0, 2); System.Array.Clear(_swBase, 0, 2); System.Array.Clear(_restBase, 0, 2);
            _resFrame[0] = _resFrame[1] = -10; _resCont[0] = _resCont[1] = false;
            System.Array.Clear(_pron, 0, 2); System.Array.Clear(_swingSign, 0, 2); _pronFrame[0] = _pronFrame[1] = -10;
            Transform B(HumanBodyBones b) => a.GetBoneTransform(b);
            if (Left)
            {
                Clavicle = B(HumanBodyBones.LeftShoulder);
                Upper = B(HumanBodyBones.LeftUpperArm); Lower = B(HumanBodyBones.LeftLowerArm); Hand = B(HumanBodyBones.LeftHand);
                Set(0, B(HumanBodyBones.LeftThumbProximal), B(HumanBodyBones.LeftThumbIntermediate), B(HumanBodyBones.LeftThumbDistal));
                Set(1, B(HumanBodyBones.LeftIndexProximal), B(HumanBodyBones.LeftIndexIntermediate), B(HumanBodyBones.LeftIndexDistal));
                Set(2, B(HumanBodyBones.LeftMiddleProximal), B(HumanBodyBones.LeftMiddleIntermediate), B(HumanBodyBones.LeftMiddleDistal));
                Set(3, B(HumanBodyBones.LeftRingProximal), B(HumanBodyBones.LeftRingIntermediate), B(HumanBodyBones.LeftRingDistal));
                Set(4, B(HumanBodyBones.LeftLittleProximal), B(HumanBodyBones.LeftLittleIntermediate), B(HumanBodyBones.LeftLittleDistal));
            }
            else
            {
                Clavicle = B(HumanBodyBones.RightShoulder);
                Upper = B(HumanBodyBones.RightUpperArm); Lower = B(HumanBodyBones.RightLowerArm); Hand = B(HumanBodyBones.RightHand);
                Set(0, B(HumanBodyBones.RightThumbProximal), B(HumanBodyBones.RightThumbIntermediate), B(HumanBodyBones.RightThumbDistal));
                Set(1, B(HumanBodyBones.RightIndexProximal), B(HumanBodyBones.RightIndexIntermediate), B(HumanBodyBones.RightIndexDistal));
                Set(2, B(HumanBodyBones.RightMiddleProximal), B(HumanBodyBones.RightMiddleIntermediate), B(HumanBodyBones.RightMiddleDistal));
                Set(3, B(HumanBodyBones.RightRingProximal), B(HumanBodyBones.RightRingIntermediate), B(HumanBodyBones.RightRingDistal));
                Set(4, B(HumanBodyBones.RightLittleProximal), B(HumanBodyBones.RightLittleIntermediate), B(HumanBodyBones.RightLittleDistal));
            }
            Valid = Upper != null && Lower != null && Hand != null && _fing[1, 0] != null && _fing[4, 0] != null;
            _measured = false;
            _hingeKnown = false;
            _flexInit = false;
        }

        private void Set(int i, Transform a, Transform b, Transform c) { _fing[i, 0] = a; _fing[i, 1] = b; _fing[i, 2] = c; }

        public float UpperLength => Valid ? Vector3.Distance(Upper.position, Lower.position) : 0f;
        public float LowerLength => Valid ? Vector3.Distance(Lower.position, Hand.position) : 0f;
        public float Reach => UpperLength + LowerLength;

        // ================================================================== Messen

        /// <summary>Handrahmen in Handknochen-Koordinaten (haengt nur an den Fingeransaetzen – starr zur Hand).</summary>
        private void Measure()
        {
            if (_measured || !Valid) return;
            Transform index = _fing[1, 0], little = _fing[4, 0], middle = _fing[2, 0] != null ? _fing[2, 0] : index, thumb = _fing[0, 0];
            Vector3 wrist = (middle.position - Hand.position).normalized;
            Vector3 knuckles = Vector3.ProjectOnPlane(index.position - little.position, wrist);
            if (knuckles.sqrMagnitude < 1e-8f) return;
            knuckles.Normalize();
            Vector3 palm = Vector3.Cross(wrist, knuckles).normalized;
            if (thumb != null && Vector3.Dot(palm, thumb.position - Hand.position) < 0f) palm = -palm;
            Vector3 center = (index.position + little.position) * 0.5f + palm * 0.025f - wrist * 0.01f;
            Quaternion inv = Quaternion.Inverse(Hand.rotation);
            _frameLocal = inv * Quaternion.LookRotation(knuckles, palm);
            _centerLocal = inv * (center - Hand.position);
            _dirLocal = inv * wrist;
            for (int i = 0; i < 5; i++)
            {
                Transform j1 = _fing[i, 1], j2 = _fing[i, 2];
                if (j1 == null || j2 == null) continue;
                // Endglied: Richtung zur Spitze (falls vorhanden), sonst wie das Mittelglied in dessen Achsen
                Vector3 seg = j2.childCount > 0 ? j2.GetChild(0).position - j2.position : j2.position - j1.position;
                Quaternion basis = j2.childCount > 0 ? j2.rotation : j1.rotation;
                _tipLocal[i] = Quaternion.Inverse(basis) * seg.normalized;
            }
            _measured = true;
        }

        /// <summary>Beugeachse des Ellbogens aus der laufenden Animation lernen (vor allen eigenen Eingriffen aufrufen).</summary>
        public void Learn()
        {
            if (!Valid) return;
            Measure();
            Vector3 u = Lower.position - Upper.position, l = Hand.position - Lower.position;
            Vector3 axis = Vector3.Cross(u, l);
            if (Vector3.Angle(u, l) < 12f || axis.sqrMagnitude < 1e-10f) return;
            Vector3 m = Quaternion.Inverse(Upper.rotation) * axis.normalized;
            _hingeUpperLocal = _hingeKnown ? Vector3.Lerp(_hingeUpperLocal, m, 0.05f).normalized : m;
            _hingeKnown = true;
        }

        /// <summary>Handrahmen und Faustmitte im Handknochen (Drehung; Versatz in Metern, in Handachsen).</summary>
        public Quaternion FrameLocal { get { Measure(); return _frameLocal; } }
        public Vector3 CenterLocal { get { Measure(); return _centerLocal; } }
        /// <summary>Breite der Faust (Zeige- bis Kleinfingeransatz, m).</summary>
        public float HandWidth => Valid ? Vector3.Distance(_fing[1, 0].position, _fing[4, 0].position) : 0.07f;
        /// <summary>Dicke des gehaltenen Griffs (Radius, m) – duenne Griffe schliessen die Finger weiter.</summary>
        public float GripRadius { get; set; } = 0.017f;

        /// <summary>Aktueller Handrahmen (vorwaerts = Knoechelachse, oben = Handflaeche) und Faustmitte.</summary>
        public Quaternion Frame => Hand.rotation * _frameLocal;
        public Vector3 Center => Hand.position + Hand.rotation * _centerLocal;
        /// <summary>Handrichtung (Handgelenk → Fingeransatz).</summary>
        public Vector3 Direction => Hand.rotation * _dirLocal;
        public Vector3 Palm => Frame * Vector3.up;
        public Vector3 Knuckles => Frame * Vector3.forward;

        /// <summary>Handgelenk jeder Haltung (auch aus Animationen) auf einen natuerlichen Bereich begrenzen.</summary>
        public void ClampWrist(float maxWrist, float weight)
        {
            if (!Valid || !_measured || weight <= 0.0001f) return;
            // Anatomische Ellipse (Beugen/Strecken/Speichen-/Ellenseite getrennt), maxWrist 50 = Normalbereich
            Ashveil.Gameplay.Characters.StancePoser.ClampWrist(Lower, Hand, Direction, Palm, Knuckles, maxWrist / 50f);
        }

        /// <summary>
        /// Unterarmdrehung begrenzen (Pronation/Supination). Neutral = Handflaeche zur Innenseite der Armebene
        /// (Handschlag); mehr als ~95° verdreht sich kein Unterarm. Ueberschuss wird zurueckgedreht – je zur Haelfte im
        /// Unterarm und in der Hand (keine Haut-Verdrillung am Handgelenk). Bei fast gestrecktem Arm ist die Armebene
        /// unbestimmt (dann dreht der Oberarm mit) – keine Korrektur.
        /// </summary>
        public float ClampTwist(float limit)
        {
            if (!Valid || !_measured || !_hingeKnown) return 0f;
            Quaternion hand = Hand.rotation;
            // Eigenes Gedaechtnis (Slot 1): nicht mit dem Zielwert aus Rotate (Slot 0) vermischen
            float before = TrackedPronation(hand, 1);
            if (Mathf.Abs(before) <= limit && !Resolving(1)) { _swingSign[1] = 0f; return before; }
            Quaternion target = ResolveTarget(hand, limit, before, 1);
            // Unterarm traegt die Haelfte der Korrektur (keine Verdrillung am Handgelenk)
            float d = Mathf.DeltaAngle(PronationOf(Hand.rotation), PronationOf(target));
            Vector3 fore = (Hand.position - Lower.position).normalized;
            Lower.rotation = Quaternion.AngleAxis(d * 0.5f, fore) * Lower.rotation;
            Hand.rotation = target;
            return before;
        }

        /// <summary>Abstand der Faustmitte von einer Linie (Griff/Schaft) – Pruefwert fuers Lab.</summary>
        public float DistanceToLine(Vector3 point, Vector3 dir)
        {
            if (!Valid || !_measured) return 0f;
            Vector3 v = Center - point;
            return Vector3.ProjectOnPlane(v, dir.normalized).magnitude;
        }

        /// <summary>Winkel zwischen Unterarm und Hand (0 = gerade; natuerlich bis ~40°).</summary>
        public float WristAngle => Valid && _measured ? Vector3.Angle(Hand.position - Lower.position, Direction) : 0f;

        // ================================================================== Arm fuehren

        /// <summary>
        /// Faustmitte an <paramref name="center"/>, Handrahmen = <paramref name="frame"/>. <paramref name="ulnarDeg"/> neigt den
        /// Unterarm gegenueber der Hand zur Daumenseite (= Hand kippt zur Kleinfingerseite, wie beim Halten einer Klinge).
        /// <paramref name="twistShare"/>: Anteil der Drehung um die Unterarmachse, den der Unterarm uebernimmt (Schildarm 1).
        /// </summary>
        public void Place(Vector3 center, Quaternion frame, float ulnarDeg, float twistShare, float weight, float maxWrist = 45f)
        {
            if (!Valid || weight <= 0.0001f) return;
            Measure();
            if (!_measured) return;
            Quaternion handRot = frame * Quaternion.Inverse(_frameLocal);
            Vector3 handPos = center - handRot * _centerLocal;
            PlaceBone(handPos, handRot, ulnarDeg, twistShare, weight, maxWrist);
        }

        /// <summary>Handknochen-Drehung, die den gewuenschten Handrahmen ergibt.</summary>
        public Quaternion BoneRotation(Quaternion frame) { Measure(); return frame * Quaternion.Inverse(_frameLocal); }

        /// <summary>Handknochen-Lage fuer eine Faustmitte bei gegebener Knochendrehung.</summary>
        public Vector3 BonePosition(Vector3 center, Quaternion boneRot) { Measure(); return center - boneRot * _centerLocal; }

        /// <summary>Handknochen direkt an Lage + Drehung (Ellbogen so, dass das Handgelenk natuerlich bleibt).</summary>
        public void PlaceBone(Vector3 handPos, Quaternion handRot, float ulnarDeg, float twistShare, float weight, float maxWrist = 45f)
        {
            if (!Valid || weight <= 0.0001f) return;
            Measure();
            if (!_measured) return;
            Quaternion frame = handRot * _frameLocal;
            Vector3 dir = handRot * _dirLocal;
            Vector3 k = frame * Vector3.forward;
            Vector3 fore = dir;
            Vector3 bendAxis = Vector3.Cross(dir, k);
            if (Mathf.Abs(ulnarDeg) > 0.01f && bendAxis.sqrMagnitude > 1e-8f) fore = Quaternion.AngleAxis(ulnarDeg, bendAxis.normalized) * dir;
            Vector3 elbow = handPos - fore * LowerLength;
            Vector3 hint = elbow - (Upper.position + handPos) * 0.5f;
            Solve(handPos, hint, weight);
            Rotate(handRot, twistShare, weight, maxWrist);
        }

        /// <summary>Nur die Hand an einen Punkt fuehren (Drehung bleibt wie animiert), Ellbogen Richtung <paramref name="hint"/>.</summary>
        public void ReachTo(Vector3 handPos, Vector3 hint, float weight) { if (Valid) Solve(handPos, hint, weight); }

        /// <summary>Zweigelenk-Loesung mit Scharnier-Ellbogen.</summary>
        private void Solve(Vector3 target, Vector3 hint, float weight)
        {
            if (weight <= 0.0001f) return;
            Vector3 a = Upper.position, b = Lower.position, c = Hand.position;
            if (weight < 1f) target = Vector3.Lerp(c, target, weight);
            float lab = Vector3.Distance(a, b), lbc = Vector3.Distance(b, c);
            Vector3 d = target - a;
            if (d.sqrMagnitude < 1e-8f || lab < 1e-4f || lbc < 1e-4f) return;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(lab - lbc) + 0.01f, (lab + lbc) * 0.999f);
            Vector3 dirT = d.normalized;
            Vector3 perp = Vector3.ProjectOnPlane(hint, dirT);
            if (perp.sqrMagnitude < 1e-6f) perp = Vector3.ProjectOnPlane(b - a, dirT);
            if (perp.sqrMagnitude < 1e-8f) return;
            perp.Normalize();
            // Ellbogen-Schwenk: dorthin, wo die Beugeachse des Oberarms (aus der Animation) ohne grosse Drehung des
            // Oberarms passt – der Wunsch (hint) zaehlt zur Haelfte. Verlangt er ein Beugen gegen die Anatomie
            // (Ellbogen nach innen/oben durchgedrueckt), gilt nur die natuerliche Lage.
            if (_hingeKnown)
            {
                Vector3 h = Vector3.ProjectOnPlane(Upper.rotation * _hingeUpperLocal, dirT);
                if (h.sqrMagnitude > 1e-6f)
                {
                    Vector3 nat = Vector3.Cross(dirT, h.normalized).normalized;
                    // 15.10.: frueher harter Schalter bei Dot < -0,2 (Ellbogenebene sprang um ~50° in einem Bild, im
                    // Schwung aus der Scheide). Jetzt stetig: Anteil der natuerlichen Lage gleitet von 0,5 auf 1.
                    float natW = Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(0.1f, -0.5f, Vector3.Dot(nat, perp)));
                    perp = natW >= 0.999f ? nat : Vector3.Slerp(perp, nat, natW).normalized;
                }
            }
            float cosA = Mathf.Clamp((lab * lab + dist * dist - lbc * lbc) / (2f * lab * dist), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 bNew = a + lab * (cosA * dirT + sinA * perp);
            Vector3 cNew = a + dirT * dist;

            Upper.rotation = Quaternion.FromToRotation(b - a, bNew - a) * Upper.rotation;
            // Oberarm um die eigene Achse drehen, bis die Ellbogen-Beugeachse zur neuen Armebene passt
            Vector3 ud = (bNew - a).normalized;
            Vector3 n = Vector3.Cross(ud, cNew - bNew);
            if (_hingeKnown && n.sqrMagnitude > 1e-8f)
            {
                Vector3 h = Vector3.ProjectOnPlane(Upper.rotation * _hingeUpperLocal, ud);
                // Bei fast gestrecktem Arm ist die Armebene unbestimmt – dann nicht drehen (sonst zuckt der Oberarm)
                float bend = Vector3.Angle(bNew - a, cNew - bNew);
                float rollW = Mathf.Clamp01((bend - 6f) / 18f);
                if (h.sqrMagnitude > 1e-8f && rollW > 0f)
                {
                    // Oberarmdrehung begrenzt (Innen-/Aussendrehung der Schulter hat ~±90° Spielraum)
                    float ang = Mathf.Clamp(Vector3.SignedAngle(h, n.normalized, ud), -50f, 50f);
                    Upper.rotation = Quaternion.AngleAxis(ang * rollW * weight, ud) * Upper.rotation;
                }
            }
            Vector3 lp = Lower.position;
            Lower.rotation = Quaternion.FromToRotation(Hand.position - lp, cNew - lp) * Lower.rotation;
        }

        /// <summary>Hand drehen; Drehung um die Unterarmachse teilweise in den Unterarm, Handgelenk begrenzen.</summary>
        private void Rotate(Quaternion handRot, float twistShare, float weight, float maxWrist)
        {
            Quaternion target = Quaternion.Slerp(Hand.rotation, handRot, weight);
            // Unterarmdrehung anatomisch aufloesen: zuerst durch Schwenken des Ellbogens (Hand bleibt, wie sie ist),
            // Rest durch Zurueckdrehen der Hand
            float p0 = TrackedPronation(target, 0);
            if (Mathf.Abs(p0) <= 95f && !Resolving(0)) _swingSign[0] = 0f;
            else target = ResolveTarget(target, 95f, p0, 0);
            Vector3 axis = (Hand.position - Lower.position).normalized;
            // Anteil des Unterarms aus der Aenderung der PRONATION (anatomisch, stetig) – nicht aus der kuerzesten
            // Drehung, die bei ±180° umspringen und den Unterarm um 90° zucken lassen wuerde
            float twist = _hingeKnown ? Mathf.DeltaAngle(PronationOf(Hand.rotation), PronationOf(target))
                                      : TwistAngle(target * Quaternion.Inverse(Hand.rotation), axis);
            if (Mathf.Abs(twist) > 0.01f) Lower.rotation = Quaternion.AngleAxis(twist * Mathf.Clamp01(twistShare), axis) * Lower.rotation;
            Hand.rotation = target;
            // Handgelenk-Grenze (Beugen/Strecken/Abwinkeln, anatomisch getrennt): Rest zurueck zur Unterarmachse
            Ashveil.Gameplay.Characters.StancePoser.ClampWrist(Lower, Hand, Direction, Palm, Knuckles, maxWrist / 50f);
        }

        /// <summary>
        /// Pronation (−) / Supination (+) fuer eine Handdrehung: Handflaeche gegen die Beugeachse des Ellbogens
        /// (aus der Animation gelernt, liegt fest im Oberarm). Neutral (0) = Handflaeche zur Innenseite wie beim Handschlag.
        /// </summary>
        public float PronationOf(Quaternion handRot)
        {
            if (!_hingeKnown) return 0f;
            Vector3 fore = Hand.position - Lower.position;
            if (fore.sqrMagnitude < 1e-8f) return 0f;
            fore.Normalize();
            Vector3 neutral = Upper.rotation * _hingeUpperLocal * (Left ? -1f : 1f);
            Vector3 a = Vector3.ProjectOnPlane(neutral, fore), b = Vector3.ProjectOnPlane(handRot * _frameLocal * Vector3.up, fore);
            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(a, b, fore);
        }

        /// <summary>
        /// Verlangt die Zielhand mehr Unterarmdrehung als 'limit': Ellbogen um die Linie Schulter→Handgelenk schwenken
        /// (die Hand bleibt dabei stehen, der Oberarm dreht mit – so macht es der Mensch), bis zu 60°. Reicht das nicht,
        /// wird der Rest an der Hand zurueckgenommen.
        /// </summary>
        // Pronation des letzten Bildes (abgewickelt, kann ueber ±180° gehen). 15.10.: Beim Wegstecken an die linke Huefte
        // verlangte die Zielhand ~180° Pronation; das Vorzeichen sprang von Bild zu Bild, die Hand wurde einmal links-, einmal
        // rechtsherum zurueckgedreht – die Waffe sprang bis 116 cm (Motion-Lab). Jetzt stetig: Winkel nahe am Vorbild bleiben.
        // 15.10. (2): Gedaechtnis je Aufrufstelle getrennt – Slot 0 = Zielhand in Rotate, Slot 1 = Ist-Hand in ClampTwist.
        // Vorher teilten sich beide einen Wert; ClampTwist ueberschrieb den Zielwert mit dem schon begrenzten (~95°), das
        // Abwickelfenster lag dann bei 95° ± 180° und die Zielhand konnte trotzdem auf die andere Seite umspringen.
        private readonly float[] _pron = new float[2];
        /// <summary>Lab-Mitschrift: Pronation der Zielhand im letzten Bild (Grad, abgewickelt) und gewaehlte Schwenkrichtung.</summary>
        public float DebugPronation => _pron[0];
        public float DebugSwing => _swingSign[0];
        private readonly int[] _pronFrame = { -10, -10 };
        // Zuletzt gewaehlte Schwenkrichtung des Ellbogens je Slot (+1/−1, 0 = kein Schwenk) – Hysterese gegen Umspringen
        private readonly float[] _swingSign = new float[2];
        private const float SwingHysteresis = 10f;

        private static float Unwrapped(float p, float near) => near + Mathf.DeltaAngle(near, p);

        /// <summary>Pronation, stetig abgewickelt gegen den Wert des letzten Bildes desselben Slots.</summary>
        private float TrackedPronation(Quaternion rot, int slot)
        {
            float p = PronationOf(rot);
            int frame = Time.frameCount;
            if (frame - _pronFrame[slot] <= 2) p = Unwrapped(p, _pron[slot]);
            // 22.10. nachts (Angriffsfilm: nach bestimmten Angriffsfolgen hielt die Hand in der ganz normalen Deckung die Waffe
            // quer zur Seite; Mitschrift: abgewickelte Pronation bis −600°): das Abwickeln lief ohne Grenze weiter – ein Unterarm
            // dreht anatomisch aber nur etwa ±180°. Über ±270° hinaus gilt wieder der einfache Winkel (±180°), sonst „überdrehte“
            // jede spätere Zielhand um 250° und mehr und wurde entsprechend zurückgedreht.
            if (Mathf.Abs(p) > 270f) p = Mathf.DeltaAngle(0f, p);   // ±200 liess den Stab am Ruecken (braucht kurz ~−195°) umspringen
            _pron[slot] = p; _pronFrame[slot] = frame;
            return p;
        }

        /// <summary>Pronation der Zielhand, wenn der ganze Arm um die Linie Schulter→Handgelenk um <paramref name="ang"/> geschwenkt ist.</summary>
        private float SwingPronation(Quaternion target, Vector3 axis, Quaternion u0, float ang, float p0)
        {
            Upper.rotation = Quaternion.AngleAxis(ang, axis) * u0;
            return Unwrapped(PronationOf(target), p0);
        }

        // 22.10. (Mitschrift Ziehfilm, rechter Arm: die Auflösung schlug in einem Bild um – Abwicklung an der ±270°-Grenze
        // 266° → −90°, Wechsel der Schwenkrichtung; Ellbogen bis 120°, Zurückdrehen bis 170° auf einmal, Waffe 13–50 cm):
        // Schwenk und Zurückdrehen werden je Bild nur begrenzt verändert (8° bzw. 25°) und klingen auch nach Ende der
        // Auflösung so aus. Die Zielhand selbst bleibt ungebremst – schnelle Hiebe sind nicht betroffen (eine Grenze auf die
        // ganze Handdrehung hatte die Angriffe verschlechtert: Lab 351 → 690 cm).
        private readonly float[] _swApplied = new float[2], _restApplied = new float[2], _swBase = new float[2], _restBase = new float[2];
        private readonly int[] _resFrame = { -10, -10 };
        private readonly bool[] _resCont = new bool[2];
        // 22.10.2026 (Codex Zeitbasis): Grad je Sekunde statt je Frame – vorher dauerte ein 120°-Wechsel 0,50 s bei 30 fps, 0,104 s bei
        // 144 fps. Kalibriert auf das geprüfte Laborbild (8°/25° je Frame bei ~150 fps): 480°/s war zu langsam – die Auflösung kam bei
        // schnellen Hieben nicht hinterher und der Unterarm sprang bis 151° in einem Bild. dt gedeckelt, damit ein Ruckler nicht springt.
        private const float SwingRate = 1200f, RestRate = 3750f;
        private static float AufloesDt => Mathf.Min(Time.deltaTime, 0.05f);
        private static float SwingStep => SwingRate * AufloesDt;
        private static float RestStep => RestRate * AufloesDt;

        /// <summary>Klingt in diesem Slot noch eine Auflösung aus (Schwenk oder Zurückdrehen ungleich 0)?</summary>
        private bool Resolving(int slot) =>
            Time.frameCount - _resFrame[slot] <= 2 && (Mathf.Abs(_swApplied[slot]) > 0.01f || Mathf.Abs(_restApplied[slot]) > 0.01f);

        private Quaternion ResolveTarget(Quaternion target, float limit, float p0, int slot)
        {
            if (!_hingeKnown) return target;
            if (Mathf.Abs(p0) <= limit && !Resolving(slot)) return target;
            Vector3 axis = Hand.position - Upper.position;
            if (axis.sqrMagnitude < 1e-6f) return target;
            axis.Normalize();
            int f = Time.frameCount;
            if (_resFrame[slot] != f)
            {
                _resCont[slot] = f - _resFrame[slot] <= 2;
                _swBase[slot] = _resCont[slot] ? _swApplied[slot] : 0f;
                _restBase[slot] = _resCont[slot] ? _restApplied[slot] : 0f;
                _resFrame[slot] = f;
            }
            if (Mathf.Abs(p0) <= limit)
            {
                // Auflösung vorbei: Schwenk und Zurückdrehen weich auf 0
                _swingSign[slot] = 0f;
                Quaternion uA = Upper.rotation, hA = Hand.rotation;
                float sw0 = Mathf.MoveTowards(_swBase[slot], 0f, SwingStep);
                float rs0 = Mathf.MoveTowards(_restBase[slot], 0f, RestStep);
                _swApplied[slot] = sw0; _restApplied[slot] = rs0;
                if (Mathf.Abs(sw0) > 0.01f) Upper.rotation = Quaternion.AngleAxis(sw0, axis) * uA;
                Hand.rotation = hA;
                if (Mathf.Abs(rs0) < 0.01f) return target;
                return Quaternion.AngleAxis(rs0, (Hand.position - Lower.position).normalized) * target;
            }
            // 15.10. (2) – Hauptursache des Waffensprungs: frueher wurde nach dem Schwenk 'Lower.rotation = l0' gesetzt. Der
            // Unterarm behielt seine Weltdrehung, wurde aber mit dem Ellbogen verschoben – die Hand wanderte um den Weg des
            // Ellbogens (bis ~15 cm) vom Ziel weg, und beim Wechsel der Schwenkrichtung sprang sie auf die Gegenseite
            // (Motion-Lab: 34 cm im Schwung, 11 cm beim Einfuehren, „Hand verfehlte Griff 8,9 cm“). Jetzt schwenkt der Arm
            // starr (Unterarm-Lokaldrehung bleibt): die Hand liegt auf der Schwenkachse und bleibt exakt stehen.
            Quaternion u0 = Upper.rotation, h0 = Hand.rotation;
            float over = Mathf.Min(60f, Mathf.Abs(p0) - limit);
            float pPlus = SwingPronation(target, axis, u0, over, p0);
            float pMinus = SwingPronation(target, axis, u0, -over, p0);
            float aP = Mathf.Abs(pPlus), aM = Mathf.Abs(pMinus);
            // Stetige Wahl: die Richtung des letzten Bildes bleibt, bis die andere deutlich (> 10°) besser ist
            float prev = _swingSign[slot];
            float sgn = prev > 0f ? (aM + SwingHysteresis < aP ? -1f : 1f)
                      : prev < 0f ? (aP + SwingHysteresis < aM ? 1f : -1f)
                      : (aP <= aM ? 1f : -1f);
            float bestP = sgn > 0f ? pPlus : pMinus;
            if (Mathf.Abs(bestP) >= Mathf.Abs(p0)) { sgn = 0f; bestP = p0; } // Schwenk hilft nicht
            _swingSign[slot] = sgn;
            float sw = _resCont[slot] ? Mathf.MoveTowards(_swBase[slot], sgn * over, SwingStep) : sgn * over;
            _swApplied[slot] = sw;
            Upper.rotation = Mathf.Abs(sw) < 0.01f ? u0 : Quaternion.AngleAxis(sw, axis) * u0;
            Hand.rotation = h0; // Handdrehung wie vor dem Schwenk (setzt der Aufrufer ohnehin neu)
            // Pronation beim tatsächlich angewendeten Schwenk (weicht beim Ausblenden vom gewählten ab)
            float pNow = Mathf.Abs(sw - sgn * over) < 0.01f ? bestP : Unwrapped(PronationOf(target), p0);
            float restWant = Mathf.Abs(pNow) > limit ? -Mathf.Sign(pNow) * (Mathf.Abs(pNow) - limit) : 0f;
            float rs = _resCont[slot] ? Mathf.MoveTowards(_restBase[slot], restWant, RestStep) : restWant;
            _restApplied[slot] = rs;
            if (Mathf.Abs(rs) < 0.01f) return target;
            Vector3 fore = (Hand.position - Lower.position).normalized;
            return Quaternion.AngleAxis(rs, fore) * target;
        }

        private static float TwistAngle(Quaternion q, Vector3 axis)
        {
            Vector3 v = new Vector3(q.x, q.y, q.z);
            float proj = Vector3.Dot(v, axis);
            var t = new Quaternion(axis.x * proj, axis.y * proj, axis.z * proj, q.w);
            float mag = Mathf.Sqrt(t.x * t.x + t.y * t.y + t.z * t.z + t.w * t.w);
            if (mag < 1e-6f) return 0f;
            t = new Quaternion(t.x / mag, t.y / mag, t.z / mag, t.w / mag);
            t.ToAngleAxis(out float ang, out Vector3 ax);
            if (ang > 180f) ang -= 360f;
            return Vector3.Dot(ax, axis) < 0f ? -ang : ang;
        }

        // ================================================================== Finger

        // Beugung je Gelenk (Grund-, Mittel-, Endglied) fuer Zeige-, Mittel-, Ring-, Kleinfinger
        private static readonly float[][,] Shapes =
        {
            // 16.10.: Ruhehand staerker gekruemmt (Bildbogen: Finger standen flach und gespreizt wie eine Tablett-Hand);
            // eine lockere Hand haengt mit ~25–40° Beugung, kleiner Finger am meisten.
            /* Relaxed   */ new float[,] { { 22, 30, 14 }, { 26, 34, 16 }, { 30, 38, 18 }, { 34, 42, 20 } },
            /* Open      */ new float[,] { { 3, 5, 3 }, { 4, 6, 4 }, { 5, 8, 5 }, { 6, 10, 6 } },
            /* LooseFist */ new float[,] { { 45, 55, 35 }, { 50, 60, 38 }, { 55, 62, 40 }, { 60, 65, 42 } },
            /* Fist      */ new float[,] { { 85, 95, 60 }, { 88, 98, 62 }, { 90, 98, 62 }, { 92, 98, 62 } },
            /* Grip      */ new float[,] { { 62, 80, 45 }, { 68, 82, 45 }, { 72, 84, 45 }, { 76, 86, 45 } },
            /* Trigger   */ new float[,] { { 22, 18, 10 }, { 68, 82, 45 }, { 72, 84, 45 }, { 76, 86, 45 } },
            /* Support   */ new float[,] { { 35, 40, 25 }, { 40, 45, 28 }, { 45, 48, 30 }, { 50, 52, 32 } },
            /* Cupped    */ new float[,] { { 20, 26, 16 }, { 24, 30, 18 }, { 28, 34, 20 }, { 32, 38, 22 } },
        };

        // Daumen: Ziel (Anteil zwischen Zeigefinger-Grund und -Mittelgelenk, Abstand zur Handflaeche) und Beugung
        private static readonly float[][] Thumbs =
        {
            /* Relaxed   */ new float[] { 0.2f, 0.03f, 14, 10 },
            /* Open      */ new float[] { -0.2f, 0.05f, 2, 2 },
            /* LooseFist */ new float[] { 0.6f, 0.02f, 18, 14 },
            /* Fist      */ new float[] { 1.0f, 0.0f, 28, 22 },
            /* Grip      */ new float[] { 0.9f, 0.005f, 24, 18 },
            /* Trigger   */ new float[] { 0.9f, 0.01f, 22, 16 },
            /* Support   */ new float[] { 0.2f, 0.04f, 8, 6 },
            /* Cupped    */ new float[] { 0.15f, 0.04f, 10, 8 },
        };

        /// <summary>Fingerform setzen (absolut), weich ueberblendet (Halbwertszeit ~70 ms). weight 0 = Animation.</summary>
        public void Shape(HandShape shape, float weight, float dt)
        {
            if (!Valid || weight <= 0.0001f) return;
            Measure();
            if (!_measured) return;
            float[,] s = Shapes[(int)shape];
            float[] th = Thumbs[(int)shape];
            float k = !_flexInit ? 1f : 1f - Mathf.Pow(0.5f, dt / 0.07f);
            // Griffe: Fingerschluss nach Griffdicke (duenner Dolchgriff enger, dicker Schaft/Kolben offener)
            float close = shape == HandShape.Grip || shape == HandShape.Trigger ? Mathf.Lerp(1.1f, 0.82f, Mathf.InverseLerp(0.012f, 0.03f, GripRadius)) : 1f;
            for (int f = 1; f < 5; f++)
                for (int j = 0; j < 3; j++)
                {
                    float want = s[f - 1, j] * (shape == HandShape.Trigger && f == 1 ? 1f : close);
                    _flex[f, j] = Mathf.Lerp(_flex[f, j], want, k);
                }
            _thumbAim = _flexInit ? Mathf.Lerp(_thumbAim, th[0], k) : th[0];
            _thumbLift = _flexInit ? Mathf.Lerp(_thumbLift, th[1], k) : th[1];
            _thumbFlex1 = _flexInit ? Mathf.Lerp(_thumbFlex1, th[2], k) : th[2];
            _thumbFlex2 = _flexInit ? Mathf.Lerp(_thumbFlex2, th[3], k) : th[3];
            _flexInit = true;

            Vector3 palm = Palm;
            for (int f = 1; f < 5; f++)
            {
                Transform j0 = _fing[f, 0], j1 = _fing[f, 1], j2 = _fing[f, 2];
                if (j0 == null || j1 == null) continue;
                Vector3 baseDir = Vector3.ProjectOnPlane(j0.position - Hand.position, palm);
                if (baseDir.sqrMagnitude < 1e-8f) continue;
                baseDir.Normalize();
                Vector3 axis = Vector3.Cross(baseDir, palm).normalized;
                float a0 = _flex[f, 0], a1 = a0 + _flex[f, 1], a2 = a1 + _flex[f, 2];
                Aim(j0, j1.position - j0.position, Quaternion.AngleAxis(a0, axis) * baseDir, weight);
                if (j2 == null) continue;
                Aim(j1, j2.position - j1.position, Quaternion.AngleAxis(a1, axis) * baseDir, weight);
                Vector3 tip = j2.rotation * _tipLocal[f];
                Aim(j2, tip, Quaternion.AngleAxis(a2, axis) * baseDir, weight);
            }
            Thumb(palm, weight);
        }

        private float _thumbAim, _thumbLift, _thumbFlex1, _thumbFlex2;

        private void Thumb(Vector3 palm, float weight)
        {
            Transform t0 = _fing[0, 0], t1 = _fing[0, 1], t2 = _fing[0, 2];
            Transform i0 = _fing[1, 0], i1 = _fing[1, 1];
            if (t0 == null || t1 == null || i0 == null || i1 == null) return;
            // Grundglied zeigt auf eine Stelle ueber dem Zeigefinger (Griff: auf das Mittelglied, locker: neben den Ansatz)
            Vector3 aim = Vector3.LerpUnclamped(i0.position, i1.position, _thumbAim) + palm * _thumbLift;
            Aim(t0, t1.position - t0.position, aim - t0.position, weight);
            if (t2 == null) return;
            // 21.10. nachts (Anatomie-Pruefung: „Daumen 2. Glied seitlich“ 46–54° in jedem Griff-Bild): Mittel- und Endglied
            // sind Scharniergelenke – gebeugt wird nur in der Ebene aus Glied und Handflaechen-Normale (zur Handflaeche hin),
            // nicht auf die Handmitte zu (die lag seitlich und knickte das Glied ueber die Grenze von 40°).
            Vector3 seg1 = t2.position - t1.position;
            Aim(t1, seg1, Flex(seg1, palm, _thumbFlex1), weight);
            Vector3 seg2 = t2.rotation * _tipLocal[0];
            Aim(t2, seg2, Flex(seg2, palm, _thumbFlex2), weight);
        }

        /// <summary>Glied um die Achse quer zu Glied und Handflaechen-Normale zur Handflaeche hin beugen (Scharnier).</summary>
        private static Vector3 Flex(Vector3 seg, Vector3 palm, float deg)
        {
            Vector3 ax = Vector3.Cross(seg, palm);
            if (ax.sqrMagnitude < 1e-10f) return seg;
            return Quaternion.AngleAxis(deg, ax.normalized) * seg;
        }

        private static void Aim(Transform bone, Vector3 current, Vector3 wanted, float weight)
        {
            if (current.sqrMagnitude < 1e-10f || wanted.sqrMagnitude < 1e-10f) return;
            Quaternion q = Quaternion.FromToRotation(current, wanted);
            bone.rotation = (weight >= 0.999f ? q : Quaternion.Slerp(Quaternion.identity, q, weight)) * bone.rotation;
        }
    }
}
