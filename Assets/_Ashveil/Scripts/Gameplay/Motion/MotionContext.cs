using Ashveil.Data;
using Ashveil.Data.Motion;
using UnityEngine;

namespace Ashveil.Gameplay.Motion
{
    /// <summary>Gesundheitsstufe fuer die Koerpersprache (§61) – rein optisch.</summary>
    public enum WoundedState : byte { Healthy = 0, Wounded = 1, Critical = 2 }

    /// <summary>
    /// Momentaufnahme dessen, was der Koerper gerade „weiss“ (§3). Flaches struct, jeden Frame neu befuellt,
    /// keine Allokation. Nicht alles ist schon belegt – die Felder sind die Schnittstelle fuer spaetere Phasen.
    /// </summary>
    public struct MotionContext
    {
        // Identitaet (aendert sich selten)
        public string RaceId;
        public BodyMotionVariant BodyVariant;
        public float BodyHeight;
        public ClassRole ClassRole;
        public ArmorCategory Armor;
        public WeaponFamily MainWeapon;
        public WeaponClass MainWeaponClass;
        public bool HasShield;
        public bool IsMelee;
        /// <summary>Faeuste (keine Waffe angelegt).</summary>
        public bool Unarmed;
        /// <summary>Was in der Nebenhand steckt (Schild, Fokus …) – aus der echten Ausruestung.</summary>
        public Procedural.OffhandRole Offhand;

        // Haltung
        public MotionStance Stance;
        public bool WeaponDrawn;
        public bool InCombat;
        public float SecondsSinceCombat;

        // Bewegung
        public bool Grounded;
        public Vector3 Velocity;
        public Vector3 LocalVelocity;
        public float Speed;
        public float Acceleration;
        public float TurnRate;
        public Vector3 MoveDirection;
        public float FacingYaw;
        public GaitBand Gait;
        public bool Sprinting;
        public bool Walking;
        public bool Jogging;
        public bool EpicWalking;
        public bool Dodging;
        public bool Mounted;
        public float SlopeAngle;
        public float StaminaNormalized;

        // Zustand
        public float HealthNormalized;
        public WoundedState Wounded;
        public bool Dead;
        public bool Attacking;

        // Ziel
        public bool HasTarget;
        public TargetSizeClass TargetSize;
        public float TargetHeight;
        public float TargetDistance;
        public float TargetAngle;
        public Vector3 TargetVelocity;
        public byte SelectedBodyPart;
        public float TargetPointHeight;
        public Vector3 TargetPoint;
    }
}
