using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>
    /// Schluesselpose der Waffenhand in einer selbst gebauten Bewegung. Alles in Modellachsen relativ zur Huefte
    /// (x = rechts, y = oben, z = vorn) – wie <see cref="WeaponPoses"/>.
    ///   s     Zeit: 0..1 Ausholen, 1..2 Treffer, 2..3 Nachlauf (0 und 3 setzt die Maschine selbst: aktuelle Pose / Kampfhaltung)
    ///   pos   Faustmitte
    ///   k     Knoechelachse (bei normalem Griff = Klinge/Schaft)
    ///   edge  Handrichtung (Handgelenk → Finger) als Hinweis – dorthin zeigt die Schneide; wird senkrecht zu k gelegt
    ///   twist Oberkoerper-Drehung (° um die Hochachse, + = Schultern nach rechts), lean (+ = vor), side (+ = nach rechts)
    ///   flip  Dolch im Umgriff (0 = normal, 1 = Klinge an der Kleinfingerseite)
    /// </summary>
    public struct MoveKey
    {
        public float s;
        public Vector3 pos, k, edge;
        public float twist, lean, side, flip;

        public MoveKey(float s, Vector3 pos, Vector3 k, Vector3 edge, float twist = 0f, float lean = 0f, float side = 0f, float flip = 0f)
        {
            this.s = s; this.pos = pos; this.k = k.normalized; this.edge = edge.normalized;
            this.twist = twist; this.lean = lean; this.side = side; this.flip = flip;
        }

        /// <summary>Spiegelbild fuer die linke Hand (Nebenhand, Beidhaendig).</summary>
        public MoveKey Mirrored()
        {
            var m = this;
            m.pos.x = -pos.x; m.k.x = -k.x; m.edge.x = -edge.x;
            m.twist = -twist; m.side = -side;
            return m;
        }
    }

    /// <summary>Pose der freien Hand (Zauberhand beim Stab): Faustmitte, Handrichtung, Handflaeche, Fingerform.</summary>
    public struct HandKey
    {
        public float s;
        public Vector3 pos, dir, palm;
        public HandShape shape;

        public HandKey(float s, Vector3 pos, Vector3 dir, Vector3 palm, HandShape shape)
        {
            this.s = s; this.pos = pos; this.dir = dir.normalized; this.palm = palm.normalized; this.shape = shape;
        }
    }

    /// <summary>Eine Bewegung: Waffenhand (Pflicht), zweite Waffe (gespiegelt, beidhaendig) und/oder freie Hand.</summary>
    public sealed class Move
    {
        public string Name;
        public MoveKey[] Main;
        /// <summary>Gleichzeitiger Schlag der Nebenhand-Waffe (schon gespiegelt angegeben – gilt fuer die linke Hand).</summary>
        public MoveKey[] Second;
        /// <summary>Freie Hand (Stab: Zauberhand).</summary>
        public HandKey[] Free;
        /// <summary>Schlag mit der linken Hand ausfuehren (Nebenhand-Hieb): Main wird gespiegelt.</summary>
        public bool Mirror;
        /// <summary>Handgelenk zur Kleinfingerseite (Klinge folgt der Unterarmlinie).</summary>
        public float Ulnar = 15f;

        public Move Mirrored(string name = null)
        {
            var m = new Move { Name = name ?? Name, Main = Main, Second = Second, Free = Free, Mirror = !Mirror, Ulnar = Ulnar };
            return m;
        }
    }

    /// <summary>
    /// Selbst gebaute Angriffe je Waffenart (keine Fremd-Clips). Jede Bewegung ist eine kurze Folge von Schluesselposen
    /// der Faust + Klingenrichtung + Rumpf; die <see cref="CombatChoreographer"/>-Maschine verbindet sie mit weichen Bahnen
    /// (Catmull-Rom), fuehrt die Arme ueber die anatomische IK (<see cref="ArmRig"/>) und blendet nahtlos in die
    /// Kampfhaltung zurueck.
    ///
    /// Grundsaetze: Ausholen mit Rumpfdrehung weg vom Ziel, Schlag durch das Ziel mit Rumpf nach, die Schneide zeigt in
    /// die Schlagrichtung, schwere Waffen immer mit beiden Haenden (die linke Hand bleibt am vermessenen Griff).
    /// </summary>
    public static class MoveLibrary
    {
        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        private static MoveKey K(float s, Vector3 p, Vector3 k, Vector3 e, float tw = 0f, float le = 0f, float si = 0f, float fl = 0f) =>
            new MoveKey(s, p, k, e, tw, le, si, fl);

        // ================================================================== Einhand (Schwert, Axt, Keule)

        // 21.10. nachts (Max: „Angriffe unsauber, lieblos, Tempo falsch“; Angriffsfilm: reine Armbewegungen, Rueckhand kreuzte
        // senkrecht vor dem Gesicht, Scheitelhieb endete mit senkrecht haengender Klinge, Schulter-Aussendrehung bis 152°).
        // Neu nach der Recherche (SourceArt/Referenz/Angriffe/RECHERCHE_ANGRIFFE.md): Hiebe kommen aus Huefte und Rumpf
        // (gespannt +35° → Nachlauf −40°, CombatChoreographer.BodyDrive), der Koerper lehnt sich in den Treffer, die Klinge endet
        // VOR dem Koerper in einer Wache. Messung (Lab-Protokoll „Hieb soll/ist“): der dritte Vektor ist die UNTERARMRICHTUNG
        // (Handrichtung Handgelenk → Finger = Richtung, in die die Schneide zeigt) – als „Schlagrichtung“ angegeben verlangte er
        // >100° Unterarmdrehung, die Klemme hielt die Klinge oben (bis 88° daneben). Jetzt: Faust, Klinge, Unterarm.

        private static readonly Move OneHandSlash = new Move
        {
            Name = "Oberhau (schraeg von rechts oben)",
            Ulnar = 34f,   // Klinge naeher an der Verlaengerung des Arms (Handgelenk zur Kleinfingerseite, Grenze 40°; 38 lag mit Klemme bei 45–48°)
            Main = new[]
            {
                // gespannt: Faust ueber der rechten Schulter, Unterarm steil nach oben, Klinge ueber die Schulter nach hinten
                K(0.65f, V(0.28f, 0.62f, 0.00f), V(0.15f, 0.55f, -0.82f), V(0.10f, 0.95f, 0.25f), 36f, -6f, -4f),
                K(1.00f, V(0.29f, 0.65f, -0.03f), V(0.18f, 0.50f, -0.85f), V(0.10f, 0.95f, 0.20f), 40f, -7f, -5f),
                // Treffer: Arm lang nach vorn, Klinge schraeg nach vorn-links-unten
                K(1.45f, V(0.08f, 0.45f, 0.50f), V(-0.70f, -0.30f, 0.65f), V(-0.15f, -0.05f, 1.00f), -8f, 10f, 0f),
                // Nachlauf: Faust tief links vorn, Unterarm nach links-unten, Klinge weiter nach links-unten
                K(2.00f, V(-0.18f, 0.15f, 0.45f), V(-0.85f, -0.45f, 0.20f), V(-0.45f, -0.55f, 0.70f), -40f, 15f, 4f),
                K(2.45f, V(-0.12f, 0.20f, 0.44f), V(-0.75f, -0.35f, 0.45f), V(-0.35f, -0.45f, 0.80f), -26f, 9f, 2f),
            }
        };

        /// <summary>22.10. nachts (Angriffsfilm: mit Schild schlug der Oberhau links unten in den eigenen Schild, Keulenkopf auch bei
        /// geoeffnetem Schild): mit Schild endet der Hieb vor der Koerpermitte, die Klinge zeigt nach vorn-unten (Hurstwic: Schwerthieb
        /// vor dem Schild, der Schild deckt weiter).</summary>
        private static readonly Move OneHandSlashShield = new Move
        {
            Name = "Oberhau (mit Schild)",
            Ulnar = 34f,
            Main = new[]
            {
                OneHandSlash.Main[0], OneHandSlash.Main[1], OneHandSlash.Main[2],
                K(2.00f, V(0.02f, 0.16f, 0.46f), V(-0.45f, -0.60f, 0.65f), V(-0.20f, -0.55f, 0.80f), -30f, 15f, 3f),
                K(2.45f, V(0.05f, 0.20f, 0.45f), V(-0.40f, -0.45f, 0.80f), V(-0.15f, -0.45f, 0.88f), -20f, 9f, 2f),
            }
        };

        private static readonly Move OneHandBackhand = new Move
        {
            Name = "Roverso (schraeg von links oben)",
            Ulnar = 34f,
            Main = new[]
            {
                // gespannt: Faust vor der linken Schulter, Unterarm quer nach links-oben, Klinge ueber die linke Schulter nach hinten
                K(0.70f, V(-0.16f, 0.48f, 0.24f), V(-0.30f, 0.40f, -0.85f), V(-0.60f, 0.60f, 0.35f), -30f, -3f, 0f),   // Unterarm flacher (Supination); Faust weiter vor/aussen – Knauf ging bei der Frau durch den Kopf
                K(1.00f, V(-0.18f, 0.51f, 0.21f), V(-0.32f, 0.38f, -0.86f), V(-0.60f, 0.62f, 0.30f), -34f, -4f, 0f),
                // Treffer vorn: Arm lang, Klinge nach vorn-rechts-unten
                K(1.45f, V(0.05f, 0.42f, 0.50f), V(0.30f, 0.00f, 0.95f), V(-0.10f, -0.35f, 0.93f), 8f, 9f, 0f),   // Handruecken fuehrt, Klinge vorn (Schulter-Aussendrehung war 177°)
                K(2.00f, V(0.30f, 0.18f, 0.40f), V(0.75f, -0.40f, 0.50f), V(0.30f, -0.75f, 0.60f), 36f, 13f, -3f),
                K(2.45f, V(0.26f, 0.22f, 0.40f), V(0.75f, -0.35f, 0.45f), V(0.35f, -0.45f, 0.80f), 22f, 7f, -2f),
            }
        };

        private static readonly Move OneHandOverhead = new Move
        {
            Name = "Scheitelhau (schwer, von oben)",
            Ulnar = 32f,   // Nachlauf: Ellenneigung 48° bei 38
            Main = new[]
            {
                // Vom Tag: Faust ueber dem Kopf, Unterarm aufrecht, Klinge schraeg nach hinten-oben
                K(0.70f, V(0.12f, 0.74f, 0.00f), V(0.05f, 0.45f, -0.88f), V(0.00f, 1.00f, 0.15f), 12f, -10f, 0f),
                K(1.00f, V(0.12f, 0.78f, -0.02f), V(0.05f, 0.40f, -0.91f), V(0.00f, 1.00f, 0.10f), 14f, -12f, 0f),
                // Treffer auf Kopfhoehe des Gegners: Arm lang nach vorn, Klinge nach vorn-unten
                K(1.45f, V(0.08f, 0.50f, 0.50f), V(0.00f, -0.70f, 0.71f), V(0.00f, -0.20f, 1.00f), 4f, 11f, 0f),
                // Nachlauf: Spitze tief vorn (Alber), Oberkoerper tief im Schlag
                K(2.00f, V(0.05f, 0.22f, 0.50f), V(0.00f, -0.85f, 0.50f), V(0.00f, -0.60f, 0.80f), -4f, 18f, 0f),
                K(2.40f, V(0.06f, 0.24f, 0.48f), V(0.00f, -0.75f, 0.65f), V(0.00f, -0.50f, 0.86f), -2f, 12f, 0f),
            }
        };

        private static readonly Move OneHandSpin = new Move
        {
            Name = "Wuchtschlag (weiter Rundhieb)",
            Ulnar = 34f,
            Main = new[]
            {
                K(0.60f, V(0.4f, 0.36f, -0.06f), V(0.62f, 0.22f, -0.75f), V(-0.3f, 0f, 1f), 40f, -2f, -5f),
                K(1.00f, V(0.42f, 0.4f, -0.12f), V(0.6f, 0.26f, -0.76f), V(-0.35f, 0f, 1f), 44f, -3f, -6f),
                K(1.40f, V(0.14f, 0.34f, 0.58f), V(0.1f, 0.08f, 1f), V(-1f, 0f, 0.1f), 8f, 6f, 0f),
                K(2.00f, V(-0.4f, 0.3f, 0.12f), V(-0.86f, 0.05f, 0.5f), V(-0.3f, 0f, -0.95f), -44f, 10f, 6f),   // Rumpf ≤ 50° (Anatomie)
                K(2.40f, V(-0.34f, 0.26f, 0.16f), V(-0.8f, 0f, 0.6f), V(-0.4f, 0f, -0.9f), -36f, 6f, 4f),
            }
        };

        // ================================================================== Zweihand (immer beide Haende)
        // 21.10. nachts: wie die Einhand-Hiebe neu – dritter Vektor = Unterarm der rechten (oberen) Hand, Hiebe nach Liechtenauer
        // (Zornhau, Unterhau, Scheitelhau, Zwerchhau); Huefte und Knie ueber CombatChoreographer.BodyDrive.

        private static readonly Move TwoHandDiagonal = new Move
        {
            Name = "Zweihand: Zornhau (schraeg von rechts oben)",
            Ulnar = 26f,
            Main = new[]
            {
                K(0.65f, V(0.26f, 0.50f, 0.04f), V(0.25f, 0.65f, -0.72f), V(0.15f, 0.90f, 0.40f), 34f, -6f, -4f),   // neben der rechten Schulter, nicht vor dem Gesicht
                K(1.00f, V(0.27f, 0.52f, 0.02f), V(0.27f, 0.62f, -0.74f), V(0.15f, 0.90f, 0.35f), 38f, -7f, -5f),
                K(1.45f, V(0.02f, 0.42f, 0.46f), V(-0.55f, -0.25f, 0.80f), V(-0.10f, -0.10f, 1.00f), -6f, 10f, 0f),
                // Ende in der Wechselhut links unten (Spitze vorn-unten, nicht hinter dem Ruecken)
                K(2.00f, V(-0.18f, 0.10f, 0.48f), V(-0.75f, -0.55f, 0.35f), V(-0.40f, -0.60f, 0.70f), -36f, 18f, 5f),   // 8 cm vor: Knauf stiess in den rechten Oberschenkel
                K(2.45f, V(-0.13f, 0.14f, 0.47f), V(-0.65f, -0.50f, 0.55f), V(-0.30f, -0.55f, 0.78f), -24f, 12f, 3f),
            }
        };

        private static readonly Move TwoHandRising = new Move
        {
            Name = "Zweihand: Unterhau (von links unten nach rechts oben)",
            Ulnar = 26f,
            Main = new[]
            {
                K(0.65f, V(-0.27f, 0.08f, 0.32f), V(-0.60f, -0.55f, -0.58f), V(-0.30f, -0.70f, 0.60f), -34f, 10f, 3f),   // weiter vor/aussen: Ellbogen lagen im Rumpf
                K(1.00f, V(-0.29f, 0.06f, 0.30f), V(-0.62f, -0.57f, -0.54f), V(-0.30f, -0.72f, 0.58f), -38f, 11f, 3f),
                K(1.50f, V(0.02f, 0.30f, 0.46f), V(0.25f, 0.60f, 0.75f), V(0.10f, -0.10f, 1.00f), -4f, 4f, 0f),
                K(2.00f, V(0.20f, 0.58f, 0.22f), V(0.40f, 0.85f, -0.30f), V(0.25f, 0.80f, 0.50f), 30f, -6f, -3f),
                K(2.45f, V(0.16f, 0.52f, 0.24f), V(0.35f, 0.85f, -0.20f), V(0.20f, 0.75f, 0.60f), 22f, -4f, -2f),
            }
        };

        private static readonly Move TwoHandOverhead = new Move
        {
            Name = "Zweihand: Scheitelhau",
            Ulnar = 26f,
            Main = new[]
            {
                K(0.70f, V(0.06f, 0.72f, 0.14f), V(0.00f, 0.35f, -0.94f), V(0.00f, 1.00f, 0.15f), 6f, -14f, 0f),   // 14 cm vor: Parier/Knauf schnitt beim Ausholen in den Kopf
                K(1.00f, V(0.06f, 0.75f, 0.11f), V(0.00f, 0.30f, -0.95f), V(0.00f, 1.00f, 0.10f), 6f, -16f, 0f),
                K(1.45f, V(0.02f, 0.50f, 0.45f), V(0.00f, -0.65f, 0.76f), V(0.00f, -0.20f, 1.00f), 2f, 8f, 0f),
                // Ende im Alber: Spitze vor den Fuessen, Oberkoerper tief
                K(2.00f, V(0.00f, 0.10f, 0.52f), V(0.00f, -0.85f, 0.50f), V(0.00f, -0.60f, 0.80f), 0f, 24f, 0f),
                K(2.45f, V(0.02f, 0.10f, 0.44f), V(0.00f, -0.75f, 0.65f), V(0.00f, -0.50f, 0.86f), 0f, 18f, 0f),
            }
        };

        private static readonly Move TwoHandSweep = new Move
        {
            Name = "Zweihand: Zwerchhau (waagerecht, weit)",
            Ulnar = 26f,
            Main = new[]
            {
                K(0.60f, V(0.32f, 0.36f, -0.02f), V(0.70f, 0.15f, -0.70f), V(0.40f, -0.10f, 0.90f), 48f, -2f, -6f),
                K(1.00f, V(0.34f, 0.38f, -0.06f), V(0.72f, 0.16f, -0.67f), V(0.40f, -0.10f, 0.90f), 50f, -3f, -7f),
                K(1.45f, V(0.05f, 0.34f, 0.48f), V(-0.70f, 0.05f, 0.70f), V(0.00f, -0.05f, 1.00f), 4f, 8f, 0f),
                K(2.00f, V(-0.30f, 0.30f, 0.18f), V(-0.85f, 0.00f, -0.50f), V(-0.80f, -0.10f, 0.55f), -48f, 10f, 6f),
                K(2.45f, V(-0.25f, 0.28f, 0.22f), V(-0.80f, 0.00f, -0.30f), V(-0.70f, -0.10f, 0.70f), -38f, 7f, 4f),
            }
        };

        // ================================================================== Dolch

        private static readonly Move DaggerThrust = new Move
        {
            Name = "Dolch: Stich",
            // 22.10. (Angriffsfilm: im Stich stand die Klinge senkrecht nach oben – die Posen verlangten den Unterarm senkrecht nach
            // unten bei vorgestreckter Hand, die Handgelenk-Klemme drehte die Klinge auf): Unterarm vorn-unten, Handgelenk stark
            // zur Kleinfingerseite abgeknickt, die Spitze geht voran ins Ziel.
            Ulnar = 40f,
            Main = new[]
            {
                K(0.70f, V(0.21f, 0.16f, 0.10f), V(0f, 0.2f, 0.98f), V(-0.2f, -0.75f, 0.6f), 14f, -2f, 0f),
                K(1.00f, V(0.22f, 0.15f, 0.06f), V(0f, 0.2f, 0.98f), V(-0.2f, -0.75f, 0.6f), 16f, -3f, 0f),
                K(1.60f, V(0.14f, 0.28f, 0.6f), V(-0.05f, 0.22f, 0.97f), V(-0.15f, -0.55f, 0.82f), -10f, 10f, 0f),
                K(2.00f, V(0.12f, 0.3f, 0.62f), V(-0.05f, 0.2f, 0.98f), V(-0.15f, -0.55f, 0.82f), -12f, 11f, 0f),
                K(2.40f, V(0.16f, 0.24f, 0.44f), V(-0.05f, 0.4f, 0.92f), V(-0.15f, -0.6f, 0.78f), -4f, 6f, 0f),
            }
        };

        private static readonly Move DaggerSlash = new Move
        {
            Name = "Dolch: Rueckhandschnitt",
            Ulnar = 20f,
            Main = new[]
            {
                K(0.70f, V(-0.1f, 0.34f, 0.3f), V(-0.62f, 0.3f, 0.72f), V(0.7f, 0.1f, 0.7f), -22f, 3f, 0f),
                K(1.00f, V(-0.13f, 0.35f, 0.28f), V(-0.66f, 0.3f, 0.68f), V(0.72f, 0.1f, 0.68f), -26f, 3f, 0f),
                K(1.50f, V(0.1f, 0.33f, 0.52f), V(0.1f, 0.2f, 0.98f), V(1f, 0f, -0.1f), -4f, 6f, 0f),
                K(2.00f, V(0.36f, 0.28f, 0.28f), V(0.66f, 0.18f, 0.72f), V(0.7f, 0f, -0.7f), 22f, 4f, 0f),
                K(2.45f, V(0.3f, 0.24f, 0.3f), V(0.5f, 0.3f, 0.8f), V(0.7f, 0f, -0.7f), 14f, 3f, 0f),
            }
        };

        /// <summary>Dolch dreht in der Hand in den Umgriff, sticht von oben, dreht im Nachlauf zurueck.</summary>
        private static readonly Move DaggerIcepick = new Move
        {
            Name = "Dolch: Umgriff-Stoss",
            Ulnar = 10f,
            Main = new[]
            {
                K(0.45f, V(0.22f, 0.38f, 0.22f), V(0f, 0.8f, -0.4f), V(0f, 0.3f, 1f), 10f, -4f, 0f, 1f),
                K(1.00f, V(0.2f, 0.6f, 0.22f), V(0f, 0.78f, -0.62f), V(0.05f, 0.55f, 0.83f), 14f, -8f, 0f, 1f),
                K(1.55f, V(0.14f, 0.36f, 0.5f), V(0f, 0.75f, -0.66f), V(0f, -0.3f, 0.95f), 0f, 8f, 0f, 1f),
                K(2.00f, V(0.1f, 0.14f, 0.52f), V(0f, 0.6f, -0.8f), V(0f, -0.6f, 0.8f), -8f, 16f, 0f, 1f),
                K(2.55f, V(0.16f, 0.2f, 0.4f), V(0f, 0.45f, 0.89f), V(-0.2f, -0.9f, 0.3f), -2f, 8f, 0f, 0f),
            }
        };

        private static readonly Move DaggerKidney = new Move
        {
            Name = "Dolch: Nierenstich (Ausfall mit Drehung)",
            Ulnar = 22f,
            Main = new[]
            {
                K(0.60f, V(0.26f, 0.12f, 0.02f), V(0f, 0.3f, 0.95f), V(-0.2f, -0.95f, 0.2f), 26f, -3f, -3f),
                K(1.00f, V(0.28f, 0.1f, -0.02f), V(0f, 0.3f, 0.95f), V(-0.2f, -0.95f, 0.2f), 30f, -4f, -4f),
                K(1.60f, V(0.08f, 0.14f, 0.62f), V(-0.2f, 0f, 0.98f), V(-0.1f, -1f, 0f), -18f, 14f, 3f),
                K(2.00f, V(0.06f, 0.13f, 0.64f), V(-0.2f, 0f, 0.98f), V(-1f, 0.1f, 0.2f), -24f, 15f, 4f),
                K(2.45f, V(0.14f, 0.2f, 0.46f), V(-0.1f, 0.3f, 0.95f), V(-0.4f, -0.9f, 0f), -8f, 8f, 1f),
            }
        };

        // ================================================================== Beidhaendig: gleichzeitige Spezialangriffe

        private static MoveKey[] MirrorKeys(MoveKey[] src, float xShift)
        {
            var r = new MoveKey[src.Length];
            for (int i = 0; i < src.Length; i++) { r[i] = src[i].Mirrored(); r[i].pos.x -= xShift; }
            return r;
        }

        private static MoveKey[] Shift(MoveKey[] src, float xShift, float twistScale)
        {
            var r = new MoveKey[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                r[i] = src[i];
                r[i].pos.x += xShift;
                r[i].twist *= twistScale; r[i].side *= twistScale;
            }
            return r;
        }

        private static readonly Move DualDaggerSpecial = new Move
        {
            Name = "Zwei Dolche: Doppelstich",
            Ulnar = 22f,
            Main = Shift(DaggerThrust.Main, -0.03f, 0f),
            Second = MirrorKeys(Shift(DaggerThrust.Main, -0.03f, 0f), 0f),
        };

        private static readonly Move DualSwordSpecial = new Move
        {
            Name = "Zwei Schwerter: Kreuzschnitt",
            Main = Shift(OneHandSlash.Main, 0.02f, 0.2f),
            Second = MirrorKeys(Shift(OneHandSlash.Main, 0.02f, 0.2f), 0f),
        };

        // ================================================================== Stab (Zauber)

        private static readonly Move StaffSpark = new Move
        {
            Name = "Glutfunke (Stab heben, Handflaeche stoesst die Glut nach vorn)",
            Ulnar = 12f,
            Main = new[]
            {
                K(0.50f, V(0.24f, 0.28f, 0.26f), V(0.08f, 0.92f, 0.38f), V(-0.3f, 0f, 0.95f), 10f, -2f, 0f),
                K(1.00f, V(0.25f, 0.34f, 0.28f), V(0.1f, 0.9f, 0.42f), V(-0.35f, 0f, 0.94f), 14f, -3f, 0f),
                K(1.60f, V(0.22f, 0.3f, 0.34f), V(0.05f, 0.88f, 0.47f), V(-0.35f, 0f, 0.94f), 2f, 5f, 0f),
                K(2.40f, V(0.22f, 0.25f, 0.24f), V(0.03f, 0.95f, 0.3f), V(-0.3f, 0f, 0.95f), 0f, 2f, 0f),
            },
            Free = new[]
            {
                new HandKey(0.40f, V(-0.08f, 0.28f, 0.26f), V(0.3f, 0.2f, 0.93f), V(0.1f, 1f, 0f), HandShape.Cupped),
                new HandKey(1.00f, V(-0.04f, 0.33f, 0.24f), V(0.3f, 0.3f, 0.9f), V(0.15f, 1f, 0f), HandShape.Cupped),
                new HandKey(1.50f, V(-0.1f, 0.4f, 0.56f), V(0.05f, 0.95f, 0.3f), V(0f, 0.3f, 1f), HandShape.Open),
                new HandKey(2.10f, V(-0.12f, 0.38f, 0.5f), V(0.05f, 0.9f, 0.4f), V(0f, 0.4f, 0.9f), HandShape.Open),
            }
        };

        private static readonly Move StaffPoint = new Move
        {
            Name = "Glutfunke (Stabspitze zeigt aufs Ziel)",
            Ulnar = 10f,
            Main = new[]
            {
                K(0.55f, V(0.26f, 0.36f, 0.12f), V(0.1f, 0.95f, -0.3f), V(-0.2f, 0.3f, 0.93f), 18f, -4f, 0f),
                K(1.00f, V(0.27f, 0.4f, 0.1f), V(0.12f, 0.96f, -0.25f), V(-0.2f, 0.25f, 0.95f), 22f, -5f, 0f),
                K(1.60f, V(0.16f, 0.32f, 0.48f), V(-0.05f, 0.42f, 0.9f), V(-0.2f, -0.9f, 0.4f), -4f, 8f, 0f),
                K(2.40f, V(0.2f, 0.26f, 0.34f), V(0f, 0.8f, 0.6f), V(-0.3f, -0.4f, 0.87f), 0f, 3f, 0f),
            },
            Free = new[]
            {
                new HandKey(0.50f, V(-0.2f, 0.1f, 0.12f), V(0.1f, -0.9f, 0.4f), V(1f, 0f, 0f), HandShape.LooseFist),
                new HandKey(1.60f, V(-0.22f, 0.02f, 0.02f), V(0.05f, -1f, 0.1f), V(1f, 0f, 0.1f), HandShape.Relaxed),
            }
        };

        private static readonly Move StaffLance = new Move
        {
            Name = "Aschenlanze (beidhaendig ueber dem Kopf sammeln, dann nach vorn schleudern)",
            Ulnar = 12f,
            Main = new[]
            {
                K(0.45f, V(0.2f, 0.6f, 0.1f), V(-0.55f, 0.35f, 0.76f), V(0.1f, 0.92f, 0.3f), 12f, -8f, 0f),
                K(1.00f, V(0.18f, 0.76f, 0.02f), V(-0.62f, 0.3f, 0.73f), V(0.1f, 0.95f, 0.25f), 16f, -12f, 0f),
                K(1.55f, V(0.12f, 0.36f, 0.56f), V(-0.02f, 0.18f, 0.98f), V(-0.2f, -0.95f, 0.2f), -6f, 10f, 0f),
                K(2.00f, V(0.12f, 0.33f, 0.58f), V(-0.02f, 0.15f, 0.99f), V(-0.2f, -0.95f, 0.15f), -8f, 12f, 0f),
                K(2.50f, V(0.18f, 0.28f, 0.36f), V(0f, 0.7f, 0.7f), V(-0.3f, -0.6f, 0.7f), -2f, 5f, 0f),
            },
            Free = new[]
            {
                new HandKey(0.45f, V(-0.1f, 0.56f, 0.18f), V(0.3f, 0.8f, 0.5f), V(0.3f, -0.2f, 0.9f), HandShape.Open),
                new HandKey(1.00f, V(-0.06f, 0.72f, 0.1f), V(0.4f, 0.85f, 0.3f), V(0.3f, 0f, 0.95f), HandShape.Cupped),
                new HandKey(1.55f, V(-0.1f, 0.4f, 0.55f), V(0.1f, 0.95f, 0.3f), V(0f, 0.3f, 1f), HandShape.Open),
                new HandKey(2.20f, V(-0.12f, 0.36f, 0.48f), V(0.1f, 0.9f, 0.4f), V(0f, 0.4f, 0.9f), HandShape.Open),
            }
        };

        /// <summary>Glutsturm: Stab wirbelt einmal vor dem Koerper, Glut sammelt sich ueber der erhobenen Hand, dann
        /// stoesst die Stabspitze auf den Boden und der Sturm bricht los.</summary>
        private static readonly Move StaffStorm = new Move
        {
            Name = "Glutsturm (Stabwirbel und Aufschlag)",
            Ulnar = 12f,
            Main = new[]
            {
                K(0.20f, V(0.2f, 0.34f, 0.34f), V(0.9f, 0.4f, 0.1f), V(0f, 0.2f, 1f), 6f, 0f, 0f),
                K(0.40f, V(0.14f, 0.38f, 0.38f), V(0.1f, -0.2f, 0.97f), V(0f, 1f, 0.2f), -4f, 0f, 0f),
                K(0.60f, V(0.12f, 0.36f, 0.36f), V(-0.9f, -0.4f, -0.1f), V(0f, -0.2f, 1f), -8f, 0f, 0f),
                K(0.80f, V(0.16f, 0.46f, 0.3f), V(0f, 0.97f, 0.2f), V(0f, -0.2f, 1f), 4f, -6f, 0f),
                K(1.00f, V(0.22f, 0.62f, 0.2f), V(0.05f, 0.98f, 0.1f), V(-0.3f, 0f, 0.95f), 10f, -10f, 0f),
                K(1.60f, V(0.22f, 0.12f, 0.36f), V(0.05f, -0.9f, 0.42f), V(-0.3f, 0.3f, 0.9f), 0f, 16f, 0f),
                K(2.00f, V(0.22f, 0.1f, 0.36f), V(0.05f, -0.92f, 0.38f), V(-0.3f, 0.3f, 0.9f), 0f, 18f, 0f),
                K(2.50f, V(0.22f, 0.2f, 0.28f), V(0.03f, 0.3f, 0.95f), V(-0.3f, 0.2f, 0.93f), 0f, 6f, 0f),
            },
            Free = new[]
            {
                new HandKey(0.50f, V(-0.18f, 0.3f, 0.3f), V(0.4f, 0.3f, 0.86f), V(0.3f, 0.9f, 0f), HandShape.Cupped),
                new HandKey(1.00f, V(-0.16f, 0.72f, 0.14f), V(0.2f, 0.95f, 0.2f), V(0.2f, 0f, 0.98f), HandShape.Open),
                new HandKey(1.60f, V(-0.3f, 0.5f, 0.3f), V(-0.6f, 0.6f, 0.5f), V(0f, 0f, 1f), HandShape.Open),
                new HandKey(2.30f, V(-0.24f, 0.36f, 0.3f), V(-0.2f, 0.6f, 0.77f), V(0.3f, 0.2f, 0.9f), HandShape.Open),
            }
        };

        // ================================================================== Auswahl

        /// <summary>Bewegung fuer Waffenart + Art + Kombo-Index (null = keine eigene Bewegung, Animator-Clip).</summary>
        public static Move For(GripStyle style, bool special, int combo, bool dual, bool shield = false)
        {
            switch (style)
            {
                case GripStyle.OneHand:
                    if (dual)
                    {
                        if (special) return DualSwordSpecial;
                        switch (combo % 3)
                        {
                            case 0: return OneHandSlash;
                            case 1: return OneHandSlash.Mirrored("Nebenhand: Hieb (schraeg von links oben)");
                            default: return OneHandOverhead;
                        }
                    }
                    if (special) return OneHandSpin;
                    switch (combo % 3) { case 0: return shield ? OneHandSlashShield : OneHandSlash; case 1: return OneHandBackhand; default: return OneHandOverhead; }
                case GripStyle.TwoHandBlade:
                case GripStyle.TwoHandHeavy:
                case GripStyle.Polearm:
                    if (special) return TwoHandSweep;
                    switch (combo % 3) { case 0: return TwoHandDiagonal; case 1: return TwoHandRising; default: return TwoHandOverhead; }
                case GripStyle.Dagger:
                    if (dual)
                    {
                        if (special) return DualDaggerSpecial;
                        switch (combo % 3)
                        {
                            case 0: return DaggerThrust;
                            case 1: return DaggerSlash.Mirrored("Nebenhand: Rueckhandschnitt");
                            default: return DaggerIcepick;
                        }
                    }
                    if (special) return DaggerKidney;
                    switch (combo % 3) { case 0: return DaggerThrust; case 1: return DaggerSlash; default: return DaggerIcepick; }
                case GripStyle.Staff:
                    if (special) return StaffStorm;
                    switch (combo % 3) { case 0: return StaffSpark; case 1: return StaffPoint; default: return StaffLance; }
                default:
                    return null;
            }
        }

        /// <summary>Alle Bewegungen (Lab/Bericht).</summary>
        public static Move[] All => new[]
        {
            OneHandSlash, OneHandBackhand, OneHandOverhead, OneHandSpin, TwoHandDiagonal, TwoHandRising, TwoHandOverhead, TwoHandSweep,
            DaggerThrust, DaggerSlash, DaggerIcepick, DaggerKidney, DualDaggerSpecial, DualSwordSpecial, StaffSpark, StaffPoint, StaffLance, StaffStorm
        };
    }
}
