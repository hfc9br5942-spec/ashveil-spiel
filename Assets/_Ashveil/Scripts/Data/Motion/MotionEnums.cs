using System;

namespace Ashveil.Data.Motion
{
    // Grundbegriffe des Motion-Systems (Docs/Design/ashveil-motion-master-plan.md).
    // Neue Werte nur hinten anfuegen – Assets speichern die Zahlen.

    /// <summary>Waffenfamilie fuer Bewegung (nicht Spielwerte): bestimmt Haltung, Sockel, Clip-Saetze.</summary>
    public enum WeaponFamily : byte
    {
        None = 0,
        SwordShield = 1,
        OneHandSword = 2,
        TwoHandSword = 3,
        Greatsword = 4,
        Axe = 5,
        TwoHandAxe = 6,
        Mace = 7,
        Hammer = 8,
        Polearm = 9,
        Spear = 10,
        Daggers = 11,
        DualWield = 12,
        Bow = 13,
        Crossbow = 14,
        Staff = 15,
        CasterFocus = 16,
        Pistol = 17,
        Rifle = 18,
        Unarmed = 19
    }

    /// <summary>Bewegungscharakter des Koerpers – bewusst nicht an maennlich/weiblich gebunden.</summary>
    public enum BodyMotionVariant : byte { Neutral = 0, Masculine = 1, Feminine = 2, Custom = 3 }

    public enum ArmorCategory : byte { None = 0, Light = 1, Medium = 2, Heavy = 3 }

    /// <summary>Exploration → Alert → Combat (§6).</summary>
    public enum MotionStance : byte { Exploration = 0, Alert = 1, Combat = 2 }

    /// <summary>Gangband – kontinuierlich ueber das Tempo, das Band ist nur die Absicht.</summary>
    public enum GaitBand : byte { Idle = 0, EpicWalk = 1, Walk = 2, Jog = 3, Run = 4, Sprint = 5 }

    public enum TargetSizeClass : byte { None = 0, Tiny = 1, Small = 2, Humanoid = 3, Large = 4, Huge = 5, Titanic = 6 }

    /// <summary>Hoehenzone eines Treffpunkts relativ zum Angreifer.</summary>
    public enum BodyZone : byte { Any = 0, Low = 1, Mid = 2, High = 3 }

    public enum RootMotionPolicy : byte
    {
        /// <summary>Clip am Platz, der Motor bewegt (Fortbewegung).</summary>
        InPlace = 0,
        /// <summary>Kurzes deterministisches Motor-Profil (Angriffsschritt, Vault) + kleine Warp-Korrektur.</summary>
        MotorProfile = 1,
        /// <summary>Root Motion ueber OnAnimatorMove → MotionIntent → Motor (spaeter: Interaktionen).</summary>
        RootMotion = 2
    }

    /// <summary>Wie fertig ein Clip ist (§114). Platzhalter werden im Entwickler-Build gemeldet.</summary>
    public enum MotionAssetStatus : byte { Missing = 0, Placeholder = 1, Retargeted = 2, Authored = 3 }

    /// <summary>Vorrang von Bewegungsereignissen (§119) – hoeher gewinnt.</summary>
    public enum MotionPriority : byte
    {
        Idle = 0,
        MinorReaction = 10,
        Block = 20,
        Ability = 30,
        Traversal = 40,
        HardControl = 50,
        Death = 60
    }

    /// <summary>Anfangs-/Endhaltung eines Angriffs – fuer fliessende Folgen (§121/§122).</summary>
    public enum PoseTag : byte { Any = 0, GuardCenter = 1, GuardLeft = 2, GuardRight = 3, WeaponHigh = 4, WeaponLow = 5 }

    public enum MotionCategory : byte
    {
        Locomotion = 0,
        Idle = 1,
        Turn = 2,
        StartStop = 3,
        Attack = 4,
        Block = 5,
        DrawSheathe = 6,
        Jump = 7,
        Landing = 8,
        Traversal = 9,
        HitReaction = 10,
        Death = 11,
        Emote = 12
    }

    public enum TraversalKind : byte { None = 0, Step = 1, StepOver = 2, LowVault = 3, Hop = 4 }

    /// <summary>Richtung einer Bewegung/eines Treffers relativ zur Figur.</summary>
    public enum MotionDirection : byte { Any = 0, Forward = 1, Backward = 2, Left = 3, Right = 4, ForwardLeft = 5, ForwardRight = 6, BackLeft = 7, BackRight = 8 }

    /// <summary>Beschreibende Schlagworte als Bit-Maske (keine String-Vergleiche, §81).</summary>
    [Flags]
    public enum MotionTag : int
    {
        None = 0,
        Locomotion = 1 << 0,
        Combat = 1 << 1,
        Heavy = 1 << 2,
        Light = 1 << 3,
        Forward = 1 << 4,
        Backward = 1 << 5,
        Strafe = 1 << 6,
        Attack = 1 << 7,
        HighTarget = 1 << 8,
        LowTarget = 1 << 9,
        ShortRange = 1 << 10,
        LongRange = 1 << 11,
        LeftLead = 1 << 12,
        RightLead = 1 << 13,
        Traversal = 1 << 14,
        VaultLow = 1 << 15,
        Death = 1 << 16,
        HitLeft = 1 << 17,
        HitRight = 1 << 18,
        HitFront = 1 << 19,
        HitBack = 1 << 20,
        Shield = 1 << 21,
        Thrust = 1 << 22,
        Vertical = 1 << 23,
        Horizontal = 1 << 24,
        Committed = 1 << 25,
        Quick = 1 << 26,
        UpperBodyOnly = 1 << 27,
        Loop = 1 << 28,
        Relaxed = 1 << 29,
        Ready = 1 << 30,
        LargeTarget = 1 << 31
    }
}
