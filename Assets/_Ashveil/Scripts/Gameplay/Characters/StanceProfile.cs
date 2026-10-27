using UnityEngine;

namespace Ashveil.Gameplay.Characters
{
    /// <summary>Bezugspunkt fuer eine Handposition.</summary>
    public enum StanceAnchor
    {
        /// <summary>Boden unter der Huefte (x = rechts, y = Hoehe ueber Boden, z = vorn).</summary>
        Ground,
        /// <summary>Huefte (nach Verschiebung) – fuer "Hand an der Huefte".</summary>
        Hips
    }

    /// <summary>Arm per IK an eine Stelle fuehren und die Hand ausrichten.</summary>
    [System.Serializable]
    public sealed class StanceArm
    {
        public bool enabled;
        public StanceAnchor anchor = StanceAnchor.Ground;
        [Tooltip("Hand liegt auf dem Koerper auf (z. B. an der Huefte). Dann gilt: grip.x/z = Richtung von der\n" +
                 "Koerpermitte aus, grip.y = Hoehe ueber der Huefte. Die Hand wird auf die gemessene Oberflaeche\n" +
                 "(plus Abstand fuer Ausruestung) gelegt.")]
        public bool restOnBody;
        [Tooltip("Mitte der Faust (m) im Modellraum: x rechts, y hoch, z vorn")]
        public Vector3 grip;
        [Tooltip("Knoechelachse (kleiner Finger -> Zeigefinger). Beim Schwert = Klingenrichtung")]
        public Vector3 knuckles = Vector3.forward;
        [Tooltip("Richtung, in die die Handflaeche zeigt")]
        public Vector3 palm = Vector3.left;
        [Tooltip("Wohin der Ellbogen zeigt")]
        public Vector3 elbow = Vector3.back;
        [Tooltip("Finger zusaetzlich beugen (+) oder oeffnen (-), Grad je Glied")]
        [Range(-40f, 40f)] public float fingerCurl;

        public StanceArm Clone() => (StanceArm)MemberwiseClone();
    }

    /// <summary>Schwert seitlich neben dem Koerper in den Boden gestellt, rechte Hand ruht auf dem Knauf.</summary>
    [System.Serializable]
    public sealed class StancePlantedSword
    {
        public bool enabled;
        [Tooltip("Abstand des Knaufs von der Koerperseite (m)")]
        public float sideClearance = 0.09f;
        [Tooltip("Knauf so weit vor der Huefte (m)")]
        public float forward = 0.07f;
        [Tooltip("Neigung der Klinge aus der Senkrechten (Grad)")]
        [Range(0f, 35f)] public float lean = 25f;
        [Tooltip("Richtung, in die die Spitze vom Knauf aus zeigt (x rechts, z vorn)")]
        public Vector3 heading = new Vector3(0.75f, 0f, 0.65f);
        public Vector3 knuckles = new Vector3(-1f, 0f, 0.25f);
        public Vector3 elbow = new Vector3(0.35f, -0.2f, -1f);
        [Tooltip("Rechte Schulter entspannt absenken (Grad) – sonst zieht der Arm zum Knauf die Schulter hoch")]
        [Range(0f, 20f)] public float shoulderDrop = 7f;

        public StancePlantedSword Clone() => (StancePlantedSword)MemberwiseClone();
    }

    /// <summary>Eine Koerperhaltung: Huefte, Wirbelsaeule, Kopf, Fuesse, optional Arme.</summary>
    [System.Serializable]
    public sealed class Stance
    {
        [Header("Huefte")]
        [Tooltip("Verschiebung (m): x rechts = Gewicht aufs rechte Bein")]
        public Vector3 pelvisOffset;
        [Tooltip("x = vorkippen, y = seitlich (+ = rechte Seite runter), z = drehen (+ = nach rechts)")]
        public Vector3 pelvisRotation;

        [Header("Oberkoerper")]
        [Tooltip("x = vorbeugen (- = Brust raus), y = seitlich neigen, z = drehen; verteilt auf die Wirbel")]
        public Vector3 spine;
        [Tooltip("x = Schultern heben (- = senken), y = Schultern zurueck")]
        public Vector2 shoulders;
        [Tooltip("x = nicken (+ = Kopf senken), y = neigen, z = drehen")]
        public Vector3 head;

        [Header("Beine")]
        [Tooltip("Fussstellung relativ zum Boden unter der Huefte (x rechts, z vorn)")]
        public Vector3 leftFoot = new Vector3(-0.17f, 0f, 0f);
        public Vector3 rightFoot = new Vector3(0.17f, 0f, -0.05f);
        [Tooltip("Fussspitzen nach aussen (Grad)")]
        public float leftFootOut = 12f;
        public float rightFootOut = 12f;
        [Tooltip("Wie gestreckt das laengere Bein ist (1 = ganz durchgestreckt)")]
        [Range(0.85f, 0.995f)] public float legStretch = 0.975f;

        [Header("Arme (fuer entspannte Haltungen)")]
        public StanceArm rightArm = new StanceArm();
        public StanceArm leftArm = new StanceArm();
        public StancePlantedSword plantedSword = new StancePlantedSword();

        [Header("Leben")]
        [Tooltip("Atmen: Brust hebt sich (Grad)")]
        public float breathing = 1.5f;
        [Tooltip("Langsames Gewicht-Verlagern (m)")]
        public float sway = 0.012f;

        public Stance Clone()
        {
            var s = (Stance)MemberwiseClone();
            s.rightArm = rightArm.Clone();
            s.leftArm = leftArm.Clone();
            s.plantedSword = plantedSword.Clone();
            return s;
        }
    }

    /// <summary>
    /// Haltungen eines Koerpers im Stand – pro Waffe eine entspannte und eine kampfbereite.
    /// Die entspannten Haltungen fuehren auch die Arme (Schwert aufgestuetzt, Hand an der Huefte …),
    /// die kampfbereiten lassen die Arme bei der Waffenhalte-Animation und aendern nur Stand und Rumpf.
    ///
    /// Alle Werte sind im Inspector einstellbar und wirken sofort im Play-Modus.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Haltungsprofil", fileName = "Stance_")]
    public sealed class StanceProfile : ScriptableObject
    {
        /// <summary>Hoechste Version der mitgelieferten Standardwerte (neuere ersetzen aeltere Assets).</summary>
        public const int CurrentDefaultsVersion = 9;   // 9 (21.10. abends, Max: „die Schultern sind noch etwas zu sehr zurückgezogen“, Tripo-Körper): Schultern zurück 6/10/5° → 1°, Brust raus −10/−9/−7° → −4/−4/−3°   // 8: Mann entspannt legStretch 0,998 (Knie im Stand 12,2° → Norm ≤ 12°)   // 7 (18.10. abends, Max: „Schmaler, ca. 25 cm“): Mann entspannt Spur 38 → 25 cm (Knie im Stand 13° bei Norm ≤ 12°)   // 6 (18.10.): Mann entspannt – linke Hand 7 cm tiefer (Messung: Ellbogen 70° gebeugt statt locker hängend)   // 5 (18.10.): entspannter Stand legStretch 0,995 statt 0,975/0,985 (Knie 25° → ~11°)

        [HideInInspector] public int defaultsVersion;

        public Stance swordRelaxed = new Stance();
        public Stance swordReady = new Stance();
        public Stance pistolRelaxed = new Stance();
        public Stance pistolReady = new Stance();

        [Header("Umschauen")]
        [Tooltip("Wie weit der Kopf sich beim gelegentlichen Umschauen dreht (Grad)")]
        public float lookAroundAngle = 25f;
        [Tooltip("Kopf beim Umschauen heben (Grad, gegen gesenkten Kopf)")]
        public float lookAroundLift = 8f;
        public Vector2 lookAroundInterval = new Vector2(7f, 14f);

        [Header("Ausruestung")]
        [Tooltip("Abstand zwischen Haut und Haenden/Armen (m) – Platz fuer Kleidung und Ruestung.\n" +
                 "Aufliegende Haende beruehren die Ausruestung, Arme gleiten nicht durch den Koerper.")]
        [Range(0f, 0.1f)] public float equipmentGap = 0.02f;

        [Header("Kampfbereitschaft")]
        [Tooltip("So lange nach Angriff oder Treffer bleibt die kampfbereite Haltung (s)")]
        public float readyHoldTime = 5f;

        [ContextMenu("Standardwerte: Mann")]
        private void ResetMale() => ApplyMaleDefaults();

        [ContextMenu("Standardwerte: Frau")]
        private void ResetFemale() => ApplyFemaleDefaults();

        public Stance Get(bool melee, bool ready) => melee ? (ready ? swordReady : swordRelaxed) : (ready ? pistolReady : pistolRelaxed);

        // ------------------------------------------------------------------ Standardwerte
        // Werte an den Quaternius-Base-Characters erprobt (Skelett-Simulation mit gerenderten Posen).

        /// <summary>Mann: breiter, geerdeter Stand. Schwert seitlich in den Boden gestellt, rechte Hand ruht auf dem
        /// Knauf, linker Arm haengt locker, Kopf leicht gesenkt. Mit Pistole: Waffe locker gesenkt, andere Faust an der Huefte.</summary>
        public void ApplyMaleDefaults()
        {
            swordRelaxed = new Stance
            {
                pelvisOffset = new Vector3(0.02f, 0f, 0.03f),
                spine = new Vector3(-4f, -3f, -4f), shoulders = new Vector2(-2f, 1f), head = new Vector3(10f, 0f, 6f),   // 9: war −10° / (−4, 6)
                leftFoot = new Vector3(-0.125f, 0f, 0.03f), rightFoot = new Vector3(0.125f, 0f, -0.04f),   // 7: Spur 25 statt 38 cm (Max 18.10.)
                leftFootOut = 14f, rightFootOut = 16f, legStretch = 0.998f,
                plantedSword = new StancePlantedSword { enabled = true },
                leftArm = new StanceArm { enabled = true, grip = new Vector3(-0.25f, 0.79f, 0.04f), knuckles = new Vector3(0.15f, 0.2f, 1f), palm = new Vector3(1f, 0f, 0.1f), elbow = new Vector3(-0.4f, 0f, -1f), fingerCurl = 18f },
                breathing = 1.6f, sway = 0.012f
            };
            pistolRelaxed = new Stance
            {
                pelvisOffset = new Vector3(0f, 0f, 0.03f),
                spine = new Vector3(-4f, 0f, -6f), shoulders = new Vector2(-2f, 1f), head = new Vector3(4f, 0f, -4f),   // 9: war −9° / (−4, 10)
                leftFoot = new Vector3(-0.125f, 0f, 0.05f), rightFoot = new Vector3(0.125f, 0f, -0.05f),   // 7: Spur 25 cm
                leftFootOut = 14f, rightFootOut = 16f, legStretch = 0.998f,
                rightArm = new StanceArm { enabled = true, grip = new Vector3(0.25f, 0.93f, 0.19f), knuckles = new Vector3(0f, 0.5f, 0.85f), palm = new Vector3(-1f, 0f, 0f), elbow = new Vector3(0.2f, 0f, -1f) },
                leftArm = new StanceArm { enabled = true, anchor = StanceAnchor.Hips, restOnBody = true, grip = new Vector3(-1f, 0.04f, 0.1f), knuckles = new Vector3(0f, 1f, 0.3f), palm = new Vector3(1f, 0f, 0f), elbow = new Vector3(-1f, 0f, -0.4f) },
                breathing = 1.6f, sway = 0.012f
            };
            swordReady = new Stance
            {
                spine = new Vector3(4f, 0f, 10f), head = new Vector3(-4f, 0f, -8f),
                leftFoot = new Vector3(-0.2f, 0f, 0.16f), rightFoot = new Vector3(0.2f, 0f, -0.14f),
                leftFootOut = 18f, rightFootOut = 30f, legStretch = 0.93f, breathing = 2.2f, sway = 0.008f
            };
            pistolReady = swordReady.Clone();
            lookAroundAngle = 22f;
            lookAroundLift = 10f;
            lookAroundInterval = new Vector2(8f, 15f);
            defaultsVersion = CurrentDefaultsVersion;
        }

        /// <summary>Frau: Standbein-Spielbein-Haltung (Gewicht rechts, Huefte seitlich), Kinn leicht gehoben.
        /// Schwert locker gesenkt in der Rechten, linke Hand an der Huefte. Mit Pistole: Lauf neben der Schulter
        /// nach oben gerichtet.</summary>
        public void ApplyFemaleDefaults()
        {
            swordRelaxed = new Stance
            {
                pelvisOffset = new Vector3(0.05f, 0f, 0.03f), pelvisRotation = new Vector3(0f, -6f, -8f),
                spine = new Vector3(-3f, 9f, 6f), shoulders = new Vector2(-1f, 1f), head = new Vector3(-4f, -4f, 6f),   // 9: war −7° / (−2, 5)
                leftFoot = new Vector3(-0.15f, 0f, 0.07f), rightFoot = new Vector3(0.07f, 0f, -0.04f),
                leftFootOut = 22f, rightFootOut = 8f, legStretch = 0.995f,
                rightArm = new StanceArm { enabled = true, grip = new Vector3(0.24f, 0.84f, 0.21f), knuckles = new Vector3(0.12f, -0.7f, 0.7f), palm = new Vector3(-1f, 0f, -0.2f), elbow = new Vector3(0.3f, 0f, -1f) },
                // Hand an der Taille (ueber dem Beckenkamm): Finger nach vorn-unten, Daumen hinten, Ellbogen seitlich nach
                // hinten, Finger locker gekruemmt – so bleibt das Handgelenk im natuerlichen Bereich
                leftArm = new StanceArm { enabled = true, anchor = StanceAnchor.Hips, restOnBody = true, grip = new Vector3(-1f, 0.17f, 0.2f), knuckles = new Vector3(0f, 1f, -0.45f), palm = new Vector3(1f, -0.15f, -0.2f), elbow = new Vector3(-1f, 0f, -0.6f), fingerCurl = 18f },
                breathing = 1.3f, sway = 0.01f
            };
            pistolRelaxed = swordRelaxed.Clone();
            pistolRelaxed.rightArm = new StanceArm { enabled = true, grip = new Vector3(0.2f, 1.33f, 0.19f), knuckles = new Vector3(0.1f, 0f, -1f), palm = new Vector3(-1f, 0f, 0f), elbow = new Vector3(0.4f, -1f, 0f) };
            swordReady = new Stance
            {
                spine = new Vector3(3f, 0f, 14f), head = new Vector3(-3f, 0f, -12f),
                leftFoot = new Vector3(-0.14f, 0f, 0.17f), rightFoot = new Vector3(0.15f, 0f, -0.13f),
                leftFootOut = 14f, rightFootOut = 34f, legStretch = 0.94f, breathing = 1.8f, sway = 0.008f
            };
            pistolReady = swordReady.Clone();
            lookAroundAngle = 32f;
            lookAroundLift = 0f;
            lookAroundInterval = new Vector2(6f, 12f);
            defaultsVersion = CurrentDefaultsVersion;
        }
    }
}
