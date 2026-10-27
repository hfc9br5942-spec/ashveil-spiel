using Ashveil.Data;
using Ashveil.Data.Motion;
using Ashveil.Gameplay.Characters;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Items;
using Ashveil.Gameplay.Targeting;
using Ashveil.Gameplay.Visuals;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Gameplay.Motion
{
    /// <summary>
    /// Fuellt den <see cref="MotionContext"/> aus dem Spielzustand (§3). Alle Referenzen werden einmal gesucht und
    /// gecacht (§140); pro Frame nur Lesen und Rechnen. Liest nie aus dem Animator.
    /// </summary>
    public sealed class MotionContextBuilder
    {
        public CharacterMotor Motor { get; private set; }
        public WeaponUser Weapons { get; private set; }
        public Health Health { get; private set; }
        public CharacterEquipment Equipment { get; private set; }
        public EquipmentVisuals Visuals { get; private set; }
        public CharacterBodySwitcher Bodies { get; private set; }
        public TargetingSystem Targeting { get; private set; }

        private Vector3 _lastVelocity;
        private float _lastYaw;
        private float _lastCombatTime = -999f;
        private bool _hasLast;

        /// <summary>Alles, was die Schichten-Auswahl beeinflusst – aendert es sich, wird neu aufgeloest.</summary>
        public int IdentityHash { get; private set; }

        public void Bind(GameObject root)
        {
            Motor = root.GetComponentInParent<CharacterMotor>();
            Weapons = root.GetComponentInParent<WeaponUser>();
            Health = root.GetComponentInParent<Health>();
            Equipment = root.GetComponentInParent<CharacterEquipment>();
            Visuals = root.GetComponentInParent<EquipmentVisuals>();
            if (Visuals == null) Visuals = root.GetComponentInChildren<EquipmentVisuals>();
            Bodies = root.GetComponentInParent<CharacterBodySwitcher>();
            if (Bodies == null) Bodies = root.GetComponentInChildren<CharacterBodySwitcher>();
            Targeting = root.GetComponentInParent<TargetingSystem>();
        }

        /// <summary>Identitaet (Rasse, Koerper, Klasse, Ruestung, Waffe) – nur bei Aenderungen noetig, aber billig.</summary>
        public void FillIdentity(ref MotionContext c)
        {
            CharacterAppearanceData look = Visuals != null ? Visuals.Look : null;
            c.RaceId = look != null ? RaceCatalog.At(look.race).id : RaceCatalog.HumanId;
            int bodyIndex = look != null ? look.body : Bodies != null ? Bodies.ActiveIndex : 0;
            c.BodyVariant = MotionMapping.VariantOfBody(bodyIndex);
            c.BodyHeight = Motor != null && Motor.Controller != null ? Motor.Controller.height : 1.8f;

            CharacterClassDefinition cls = Equipment != null ? Equipment.Class : null;
            c.ClassRole = cls != null ? cls.roles : ClassRole.Damage;
            c.HasShield = Equipment != null && Equipment.HasShield;
            c.Armor = ArmorOf(Equipment);

            WeaponDefinition w = Weapons != null ? Weapons.ActiveWeapon : null;
            c.IsMelee = w != null && w.HeldLikeMelee;
            c.Unarmed = w == null || (GameDataBootstrap.Unarmed != null && w == GameDataBootstrap.Unarmed) || w.Id == "weapon.fists";
            c.Offhand = Procedural.OffhandRole.Free;
            if (Equipment != null && Equipment.Equipped != null)
            {
                ItemStack off = Equipment.Equipped[(int)EquipmentSlot.OffHand];
                ItemDefinition od = off.IsEmpty ? null : GameData.Item(off.id);
                if (od != null && od.IsShield) c.Offhand = Procedural.OffhandRole.Shield;
                else if (od != null && !od.IsWeapon && od.Hand == WeaponHand.OffHandOnly) c.Offhand = Procedural.OffhandRole.Focus;
            }
            c.MainWeaponClass = w != null ? w.weaponClass : WeaponClass.Sword;
            c.MainWeapon = w != null ? MotionMapping.FamilyOf(w.weaponClass, c.HasShield, cls != null && cls.dualWield) : WeaponFamily.Unarmed;

            unchecked
            {
                int h = 17;
                h = h * 31 + (c.RaceId != null ? c.RaceId.GetHashCode() : 0);
                h = h * 31 + (int)c.BodyVariant;
                h = h * 31 + (int)c.ClassRole;
                h = h * 31 + (int)c.Armor;
                h = h * 31 + (int)c.MainWeapon;
                h = h * 31 + (int)c.Offhand;
                h = h * 31 + (c.Unarmed ? 1 : 0);
                IdentityHash = h;
            }
        }

        /// <summary>Bewegung, Zustand und Ziel – jeden Frame.</summary>
        public void FillDynamic(ref MotionContext c, float dt, float now)
        {
            if (Motor != null)
            {
                MotorState s = Motor.State;
                c.Grounded = s.Grounded;
                c.Velocity = s.Velocity;
                c.LocalVelocity = Motor.LocalVelocity;
                c.Speed = Motor.HorizontalSpeed;
                c.FacingYaw = s.Yaw;
                c.Sprinting = s.IsSprinting;
                c.Walking = s.IsWalking;
                c.Jogging = s.IsJogging;
                c.EpicWalking = s.IsEpicWalking;
                c.Dodging = Motor.IsDodging;
                c.Mounted = s.Mounted;
                c.InCombat = s.InCombat;
                c.StaminaNormalized = Motor.Settings != null && Motor.Settings.maxStamina > 0f ? s.Stamina / Motor.Settings.maxStamina : 1f;
                Vector3 hv = new Vector3(s.Velocity.x, 0f, s.Velocity.z);
                c.MoveDirection = hv.sqrMagnitude > 0.01f ? hv.normalized : Vector3.zero;
                if (_hasLast && dt > 0f)
                {
                    Vector3 lastH = new Vector3(_lastVelocity.x, 0f, _lastVelocity.z);
                    // Vorzeichen: positiv = schneller werden, negativ = bremsen
                    c.Acceleration = (hv.magnitude - lastH.magnitude) / dt;
                    c.TurnRate = Mathf.DeltaAngle(_lastYaw, s.Yaw) / dt;
                }
                _lastVelocity = s.Velocity;
                _lastYaw = s.Yaw;
                _hasLast = true;
                c.Gait = GaitOf(c, Motor.Settings);
            }

            if (Health != null)
            {
                c.HealthNormalized = Health.Fraction;
                c.Dead = !Health.IsAlive;
                c.Wounded = c.HealthNormalized <= 0.25f ? WoundedState.Critical : c.HealthNormalized <= 0.6f ? WoundedState.Wounded : WoundedState.Healthy;
                if (now - Health.LastDamageTime < 0.1f) _lastCombatTime = now;
            }
            c.Attacking = Weapons != null && Weapons.IsInAttackMotion;
            if (c.InCombat || c.Attacking) _lastCombatTime = now;
            c.SecondsSinceCombat = now - _lastCombatTime;

            CombatTarget t = Targeting != null ? Targeting.Current : null;
            c.HasTarget = t != null;
            if (t != null && Motor != null)
            {
                Vector3 me = Motor.transform.position;
                Vector3 aim = t.AimPoint;
                Vector3 flat = aim - me;
                flat.y = 0f;
                c.TargetPoint = aim;
                c.TargetDistance = Mathf.Max(0f, flat.magnitude - t.SelectionRadius);
                c.TargetAngle = flat.sqrMagnitude > 0.0001f ? Mathf.DeltaAngle(c.FacingYaw, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg) : 0f;
                c.TargetPointHeight = aim.y - me.y;
                c.TargetHeight = Mathf.Max(0.2f, (aim.y - t.transform.position.y) * 2f);
                c.TargetSize = MotionMapping.SizeOfHeight(c.TargetHeight);
                c.SelectedBodyPart = Targeting.CurrentPart;
            }
            else
            {
                c.TargetDistance = 0f;
                c.TargetAngle = 0f;
                c.TargetSize = TargetSizeClass.None;
                c.SelectedBodyPart = 0;
            }
        }

        /// <summary>Absicht der Gangart aus Tempo/Flags (§14). Das Tempo selbst bleibt kontinuierlich.</summary>
        public static GaitBand GaitOf(in MotionContext c, CharacterMotorSettings s)
        {
            if (c.Speed < 0.2f) return GaitBand.Idle;
            if (c.Sprinting) return GaitBand.Sprint;
            if (c.EpicWalking) return GaitBand.EpicWalk;
            if (c.Walking) return GaitBand.Walk;
            if (c.Jogging) return GaitBand.Jog;
            if (s == null) return GaitBand.Run;
            float walk = s.walkSpeed, run = s.runSpeed;
            if (c.Speed <= walk * 1.15f) return GaitBand.Walk;
            if (c.Speed < (walk + run) * 0.5f + 0.4f) return GaitBand.Jog;
            return c.Speed > run * 1.15f ? GaitBand.Sprint : GaitBand.Run;
        }

        private static ArmorCategory ArmorOf(CharacterEquipment e)
        {
            if (e == null || e.Equipped == null) return ArmorCategory.None;
            ItemStack[] eq = e.Equipped;
            int chest = (int)EquipmentSlot.Chest;
            ArmorType best = ArmorType.None;
            if (chest < eq.Length && !string.IsNullOrEmpty(eq[chest].id))
            {
                ItemDefinition d = GameData.Item(eq[chest].id);
                if (d != null) best = d.armorType;
            }
            if (best == ArmorType.None)
                for (int i = 0; i <= (int)EquipmentSlot.Feet && i < eq.Length; i++)
                {
                    if (string.IsNullOrEmpty(eq[i].id)) continue;
                    ItemDefinition d = GameData.Item(eq[i].id);
                    if (d != null && d.armorType > best) best = d.armorType;
                }
            return MotionMapping.CategoryOf(best);
        }
    }
}
