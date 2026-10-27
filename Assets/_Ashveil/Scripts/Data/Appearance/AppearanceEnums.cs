using System;

namespace Ashveil.Data
{
    /// <summary>Koerperbereiche des Grundmodells (aus den Knochengewichten bestimmt). Reihenfolge = Datenformat.</summary>
    [Flags]
    public enum BodyRegion
    {
        None = 0,
        Head = 1,
        Chest = 2,
        Belly = 4,
        Pelvis = 8,
        UpperArms = 16,
        Forearms = 32,
        Hands = 64,
        Thighs = 128,
        Calves = 256,
        Feet = 512
    }

    /// <summary>Oberflaeche – bestimmt Metallanteil/Glanz. Gefaerbtes Metall bleibt Metall.</summary>
    public enum SurfaceKind { Cloth, Leather, Metal, Mail, Wood, Fur, Bone, Glow }

    /// <summary>Faerbbare Bereiche eines Teils.</summary>
    public enum DyeChannel { Primary, Secondary, Detail }

    public enum ProcShape { Box, Cylinder, Dome, Sphere, Torus }

    /// <summary>Wo eine Waffe verstaut wird, wenn sie nicht in der Hand ist.</summary>
    public enum WeaponSheath { None, HipLeft, HipRight, Back, Arm, /** Bogen quer ueber dem Ruecken, Griff hinter der linken Schulter */ BackBow, /** Nebenhand-Waffe an der rechten Huefte (Beidhaendig) */ HipRightOff, /** Nebenhand-Dolch an der linken Huefte */ HipLeftOff, /** 16.10.: Schild auf dem Ruecken, solange die Waffe steckt (nur Laufzeit, kein Datenwert) */ ShieldBack }

    /// <summary>Was ein Teil am Kopf verdeckt.</summary>
    [Flags]
    public enum AppearanceHides { None = 0, Hair = 1, Beard = 2, Eyebrows = 4 }

    [Flags]
    public enum BodyVariantMask { None = 0, Male = 1, Female = 2, All = 3 }
}
