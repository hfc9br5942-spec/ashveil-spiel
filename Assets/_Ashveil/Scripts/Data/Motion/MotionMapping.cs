namespace Ashveil.Data.Motion
{
    /// <summary>Uebersetzt Spieldaten (Waffenklasse, Ruestungsart, Hoehe) in Motion-Begriffe – an genau einer Stelle.</summary>
    public static class MotionMapping
    {
        public static WeaponFamily FamilyOf(WeaponClass wc, bool hasShield, bool dualWield = false)
        {
            switch (wc)
            {
                case WeaponClass.Sword: return hasShield ? WeaponFamily.SwordShield : dualWield ? WeaponFamily.DualWield : WeaponFamily.OneHandSword;
                case WeaponClass.TwoHandedSword: return WeaponFamily.TwoHandSword;
                case WeaponClass.Axe: return dualWield ? WeaponFamily.DualWield : WeaponFamily.Axe;
                case WeaponClass.TwoHandedAxe: return WeaponFamily.TwoHandAxe;
                case WeaponClass.Mace: return WeaponFamily.Mace;
                case WeaponClass.Hammer: return WeaponFamily.Hammer;
                case WeaponClass.Spear: return WeaponFamily.Spear;
                case WeaponClass.Dagger: return WeaponFamily.Daggers;
                case WeaponClass.Bow: return WeaponFamily.Bow;
                case WeaponClass.Crossbow: return WeaponFamily.Crossbow;
                case WeaponClass.Staff: return WeaponFamily.Staff;
                case WeaponClass.Focus:
                case WeaponClass.Artifact: return WeaponFamily.CasterFocus;
                case WeaponClass.Pistol:
                case WeaponClass.Revolver: return WeaponFamily.Pistol;
                default: return WeaponFamily.Rifle;
            }
        }

        public static ArmorCategory CategoryOf(ArmorType t)
        {
            switch (t)
            {
                case ArmorType.Plate: return ArmorCategory.Heavy;
                case ArmorType.Mail: return ArmorCategory.Medium;
                case ArmorType.Leather:
                case ArmorType.Cloth: return ArmorCategory.Light;
                default: return ArmorCategory.None;
            }
        }

        /// <summary>Koerperindex der Figur (0 maennlich, 1 weiblich) → Standard-Bewegungscharakter (§19, spaeter waehlbar).</summary>
        public static BodyMotionVariant VariantOfBody(int bodyIndex) => bodyIndex == 1 ? BodyMotionVariant.Feminine : BodyMotionVariant.Masculine;

        /// <summary>Groessenklasse aus der Hoehe eines Ziels (m) – bis Gegner eine eigene Angabe tragen.</summary>
        public static TargetSizeClass SizeOfHeight(float h)
        {
            if (h <= 0f) return TargetSizeClass.None;
            if (h < 0.5f) return TargetSizeClass.Tiny;
            if (h < 1.2f) return TargetSizeClass.Small;
            if (h < 2.4f) return TargetSizeClass.Humanoid;
            if (h < 4.5f) return TargetSizeClass.Large;
            if (h < 9f) return TargetSizeClass.Huge;
            return TargetSizeClass.Titanic;
        }
    }
}
