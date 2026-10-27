using Ashveil.Data;
using UnityEngine;

namespace Ashveil.Gameplay.Motion.Procedural
{
    /// <summary>Wie eine Waffe gehalten wird – bestimmt Kampfhaltung, Tragen, Griff beider Haende.</summary>
    public enum GripStyle : byte
    {
        None = 0,
        /// <summary>Einhandschwert, -axt, Keule (mit oder ohne Schild).</summary>
        OneHand,
        /// <summary>Zweihandschwert: rechte Hand am Parier, linke darunter am Knauf.</summary>
        TwoHandBlade,
        /// <summary>Zweihandaxt / Kriegshammer: schraeg vor dem Koerper, Haende weit auseinander am Schaft.</summary>
        TwoHandHeavy,
        /// <summary>Speer / Stangenwaffe: nach vorn, linke Hand vorn am Schaft.</summary>
        Polearm,
        /// <summary>Stab: aufrecht in der rechten Hand, linke Hand frei zum Wirken.</summary>
        Staff,
        /// <summary>Dolch: kurz und nah, linke Hand in Deckung.</summary>
        Dagger,
        /// <summary>Pistole: einhaendig, Lauf nach vorn-unten (Bereitschaft).</summary>
        Pistol,
        /// <summary>Buechse / Donnerbuechse: Kolben an der Huefte, linke Hand stuetzt den Lauf.</summary>
        LongGun,
        /// <summary>Waffenlos: beide Faeuste in Deckung.</summary>
        Unarmed,
        /// <summary>Bogen: in der LINKEN Hand (Bogenhand), die rechte zieht die Sehne.</summary>
        Bow
    }

    /// <summary>Was die linke Hand tut.</summary>
    public enum OffhandRole : byte { Free = 0, Shield = 1, OnWeapon = 2, Support = 3, Focus = 4 }

    /// <summary>Ziel einer Hand: Faustmitte relativ zur Huefte, Rahmen aus Handrichtung + Handflaeche (Modellachsen x = rechts, y = oben, z = vorn).</summary>
    public struct HandPose
    {
        public Vector3 pos;
        /// <summary>Handrichtung (Handgelenk → Finger).</summary>
        public Vector3 dir;
        /// <summary>Handflaechen-Normale.</summary>
        public Vector3 palm;
        public float ulnar;
        public HandShape shape;
    }

    public struct WeaponStance
    {
        public GripStyle style;
        public HandPose right;
        public OffhandRole left;
        public HandPose leftHand;          // Free / Shield / Focus
        public float leftOnWeapon;         // OnWeapon / Support: Abstand entlang der Waffenachse ab rechter Faust (m)
        public float slide;                // rechte Hand sitzt so weit entlang der Waffe (Stab: weiter oben)
        public HandShape rightShape;
        public HandShape leftShape;
        public float moveDrop;             // beim Gehen etwas tiefer (m)
        /// <summary>Waffe liegt in der linken Hand (Bogen): <see cref="right"/> gilt dann fuer den linken Arm,
        /// <see cref="leftHand"/>/<see cref="leftRest"/> fuer den rechten.</summary>
        public bool weaponLeft;
        public HandPose leftRest;          // linke Hand, solange sie (noch) nicht an der Waffe ist: locker haengend
    }

    /// <summary>
    /// Kampfhaltungen je Waffenart. Alles in Modellachsen relativ zur Huefte, damit Ducken, Neigen und Wippen
    /// mitgenommen werden. Rechtshaender. Werte sind so gewaehlt, dass das Handgelenk nahe der Mitte bleibt
    /// (<see cref="ArmRig.Place"/> legt den Ellbogen passend) – die Waffenrichtung ergibt sich aus Handrahmen + Griff.
    /// </summary>
    public static class WeaponPoses
    {
        public static GripStyle StyleOf(WeaponClass c, bool melee, bool unarmed)
        {
            if (unarmed) return GripStyle.Unarmed;
            switch (c)
            {
                case WeaponClass.Sword: case WeaponClass.Axe: case WeaponClass.Mace: return GripStyle.OneHand;
                case WeaponClass.TwoHandedSword: return GripStyle.TwoHandBlade;
                case WeaponClass.TwoHandedAxe: case WeaponClass.Hammer: return GripStyle.TwoHandHeavy;
                case WeaponClass.Spear: return GripStyle.Polearm;
                case WeaponClass.Staff: return GripStyle.Staff;
                case WeaponClass.Dagger: return GripStyle.Dagger;
                case WeaponClass.Pistol: case WeaponClass.Revolver: return GripStyle.Pistol;
                case WeaponClass.Rifle: case WeaponClass.Shotgun: case WeaponClass.HeavyGun: case WeaponClass.Crossbow:
                case WeaponClass.AetherGun: return GripStyle.LongGun;
                case WeaponClass.Bow: return GripStyle.Bow;
                default: return melee ? GripStyle.OneHand : GripStyle.Pistol;
            }
        }

        public static bool IsTwoHanded(GripStyle s) =>
            s == GripStyle.TwoHandBlade || s == GripStyle.TwoHandHeavy || s == GripStyle.Polearm || s == GripStyle.LongGun;

        /// <summary>Hand mit Klinge/Schaft (Knoechelachse) und Handflaeche angeben – Handrichtung wird daraus berechnet.</summary>
        private static HandPose Blade(Vector3 pos, Vector3 blade, Vector3 palmHint, bool left, float ulnar, HandShape shape)
        {
            Vector3 k = blade.normalized;
            Vector3 p = Vector3.ProjectOnPlane(palmHint, k).normalized;
            Vector3 d = left ? Vector3.Cross(p, k) : Vector3.Cross(k, p);
            return new HandPose { pos = pos, dir = d, palm = p, ulnar = ulnar, shape = shape };
        }

        /// <summary>Hand mit Handrichtung (Finger) und Handflaeche angeben.</summary>
        private static HandPose Hand(Vector3 pos, Vector3 dir, Vector3 palmHint, float ulnar, HandShape shape)
        {
            Vector3 d = dir.normalized;
            Vector3 p = Vector3.ProjectOnPlane(palmHint, d).normalized;
            return new HandPose { pos = pos, dir = d, palm = p, ulnar = ulnar, shape = shape };
        }

        /// <summary>Knoechelachse aus Handrichtung + Handflaeche (Seite beachten).</summary>
        public static Vector3 KnucklesOf(Vector3 dir, Vector3 palm, bool left) => left ? Vector3.Cross(dir, palm) : Vector3.Cross(palm, dir);

        public static WeaponStance Guard(GripStyle style, OffhandRole off)
        {
            var s = new WeaponStance { style = style, rightShape = HandShape.Grip, leftShape = HandShape.LooseFist, moveDrop = 0.04f };
            // Freie linke Hand: locker geschlossen vor dem Bauch, Handflaeche nach innen
            // (deutlich links und hoeher als die Waffenhand – darf nie nach Zweihandgriff aussehen)
            HandPose freeLeft = Hand(new Vector3(-0.23f, 0.33f, 0.28f), new Vector3(0.25f, 0.35f, 0.9f), Vector3.right, 10f, HandShape.LooseFist);
            // Locker haengende linke Hand (Pistole, Stab): Arm neben dem Koerper, Hand am Oberschenkel
            HandPose hangLeft = Hand(new Vector3(-0.21f, -0.1f, 0.07f), new Vector3(0.05f, -1f, 0.12f), new Vector3(1f, 0f, 0.1f), 0f, HandShape.Relaxed);
            s.leftRest = hangLeft;
            // Schildarm: Unterarm quer vor der Brust, Faust senkrecht am Riemen, Handflaeche zum Koerper (Schildflaeche nach vorn)
            HandPose shield = Hand(new Vector3(0.0f, 0.3f, 0.3f), new Vector3(1f, 0.08f, 0.12f), Vector3.back, 0f, HandShape.Grip);
            switch (style)
            {
                case GripStyle.OneHand:
                    s.right = Blade(new Vector3(0.21f, 0.12f, 0.31f), new Vector3(-0.08f, 0.72f, 0.69f), Vector3.left, false, 22f, HandShape.Grip);
                    break;
                case GripStyle.TwoHandBlade:
                    s.right = Blade(new Vector3(0.05f, 0.2f, 0.34f), new Vector3(-0.05f, 0.74f, 0.67f), Vector3.left, false, 22f, HandShape.Grip);
                    s.slide = 0.05f;
                    s.left = OffhandRole.OnWeapon; s.leftOnWeapon = -0.115f; s.leftShape = HandShape.Grip;
                    return s;
                case GripStyle.TwoHandHeavy:
                    s.right = Blade(new Vector3(0.13f, 0.06f, 0.28f), new Vector3(-0.42f, 0.62f, 0.66f), new Vector3(-0.8f, 0f, 0.3f), false, 15f, HandShape.Grip);
                    s.left = OffhandRole.OnWeapon; s.leftOnWeapon = 0.3f; s.leftShape = HandShape.Grip;
                    return s;
                case GripStyle.Polearm:
                    s.right = Blade(new Vector3(0.2f, 0.06f, 0.08f), new Vector3(-0.08f, 0.22f, 0.97f), Vector3.left, false, 10f, HandShape.Grip);
                    s.left = OffhandRole.OnWeapon; s.leftOnWeapon = 0.45f; s.leftShape = HandShape.Grip;
                    return s;
                case GripStyle.Staff:
                    // 23.10.: Neigung nach außen (x negativ, −0,12) nahm den Stabkopf aus dem Gesicht, verdrehte aber den linken Unterarm bei Stab-Angriff 3/Spezial
                    // (±174°) – zurückgestellt (Max: Animationsfeinheiten vorerst zurückstellen), Ausgangshaltung wie vorher
                    s.right = Blade(new Vector3(0.22f, 0.22f, 0.2f), new Vector3(0.02f, 0.97f, 0.22f), Vector3.left, false, 12f, HandShape.Grip);
                    s.slide = 0.55f;
                    s.left = off == OffhandRole.Focus ? OffhandRole.Focus : OffhandRole.Free;
                    freeLeft = hangLeft;
                    break;
                case GripStyle.Dagger:
                    s.right = Blade(new Vector3(0.19f, 0.2f, 0.32f), new Vector3(-0.05f, 0.5f, 0.86f), Vector3.left, false, 25f, HandShape.Grip);
                    freeLeft = Hand(new Vector3(-0.2f, 0.37f, 0.33f), new Vector3(0.25f, 0.45f, 0.86f), Vector3.right, 10f, HandShape.LooseFist);
                    break;
                case GripStyle.Pistol:
                    // Pistole: Knoechel entlang des (vermessenen, ~25° geneigten) Griffs – der Lauf liegt dadurch ~25° ueber
                    // der Handrichtung. Hand in Verlaengerung des Unterarms schraeg nach unten = Lauf in Bereitschaft (~-25°).
                    s.right = Hand(new Vector3(0.2f, 0.12f, 0.3f), new Vector3(-0.05f, -0.72f, 0.69f), Vector3.left, 0f, HandShape.Trigger);
                    s.rightShape = HandShape.Trigger;
                    freeLeft = hangLeft;
                    s.leftShape = HandShape.Relaxed;
                    break;
                case GripStyle.LongGun:
                    // Tiefe Bereitschaft: Faust am Kolbenhals vor dem Bauch, Unterarm zeigt nach vorn (Handschlag-Haltung = neutral),
                    // Kolben liegt unter dem Arm am Koerper, Lauf leicht nach unten; Stuetzhand unter dem Vorderschaft
                    s.right = Hand(new Vector3(0.15f, 0.2f, 0.28f), new Vector3(-0.15f, -0.2f, 0.97f), Vector3.left, 0f, HandShape.Trigger);
                    s.rightShape = HandShape.Trigger;
                    s.left = OffhandRole.Support; s.leftOnWeapon = 0.27f; s.leftShape = HandShape.Support;
                    return s;
                case GripStyle.Bow:
                    // Bereitschaft: Bogen tief und schraeg vor dem Koerper in der linken Faust (Knoechel entlang der Wurfarme,
                    // Handflaeche nach rechts), rechte Hand locker an der Huefte – bereit, zum Koecher zu greifen
                    s.weaponLeft = true;
                    s.right = Blade(new Vector3(-0.2f, 0.1f, 0.3f), new Vector3(0.38f, 0.82f, 0.42f), Vector3.right, true, 8f, HandShape.Grip);
                    s.left = OffhandRole.Free;
                    s.leftHand = Hand(new Vector3(0.2f, 0.08f, 0.16f), new Vector3(-0.1f, -0.55f, 0.83f), Vector3.left, 0f, HandShape.LooseFist);
                    s.leftShape = HandShape.LooseFist;
                    s.leftRest = Hand(new Vector3(0.21f, -0.1f, 0.07f), new Vector3(-0.05f, -1f, 0.12f), new Vector3(-1f, 0f, 0.1f), 0f, HandShape.Relaxed);
                    return s;
                case GripStyle.Unarmed:
                    s.right = Hand(new Vector3(0.14f, 0.4f, 0.36f), new Vector3(0f, 0.4f, 0.92f), Vector3.left, 0f, HandShape.Fist);
                    s.rightShape = HandShape.Fist;
                    freeLeft = Hand(new Vector3(-0.12f, 0.44f, 0.42f), new Vector3(0f, 0.4f, 0.92f), Vector3.right, 0f, HandShape.Fist);
                    s.leftShape = HandShape.Fist;
                    break;
                default:
                    return s;
            }
            if (s.left == OffhandRole.Free && off == OffhandRole.Shield) { s.left = OffhandRole.Shield; s.leftHand = shield; s.leftShape = HandShape.Grip; }
            else if (off == OffhandRole.Focus)
            {
                s.left = OffhandRole.Focus;
                s.leftHand = Hand(new Vector3(-0.12f, 0.26f, 0.33f), new Vector3(0.25f, 0.05f, 0.97f), Vector3.up, 0f, HandShape.Cupped);
                s.leftShape = HandShape.Cupped;
            }
            else if (s.left == OffhandRole.Free) { s.leftHand = freeLeft; s.leftShape = freeLeft.shape; }
            return s;
        }
    }
}
