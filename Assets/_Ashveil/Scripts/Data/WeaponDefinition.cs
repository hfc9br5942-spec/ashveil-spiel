using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>
    /// Datengetriebene Waffe. Eine neue Waffe = ein neues Asset (Rechtsklick > Create > Ashveil > Weapon),
    /// kein neuer Code, solange sie einen vorhandenen <see cref="WeaponArchetype"/> nutzt.
    /// </summary>
    [CreateAssetMenu(menuName = "Ashveil/Weapon", fileName = "Weapon_New")]
    public sealed class WeaponDefinition : GameDefinition
    {
        [Header("Einordnung")]
        public WeaponClass weaponClass = WeaponClass.Sword;
        public WeaponArchetype archetype = WeaponArchetype.MeleeArc;
        public ItemQuality quality = ItemQuality.Common;
        [Min(1)] public int itemLevel = 1;

        [Header("Schaden")]
        [Min(0f)] public float minDamage = 8f;
        [Min(0f)] public float maxDamage = 12f;
        [Range(0f, 1f)] public float critChance = 0.05f;
        [Min(1f)] public float critMultiplier = 1.75f;
        [Tooltip("Reichweite in m (Nahkampf: Radius des Treffbogens)")]
        [Min(0.5f)] public float range = 2.6f;

        [Header("Grundangriff (Auto-Angriff, Kombo in dieser Reihenfolge)")]
        public AttackStep[] combo =
        {
            AttackStep.Create("Hieb", 1f, 0.12f, 0.08f, 0.3f)
        };
        [Tooltip("Nach dieser Pause beginnt die Kombo von vorn (s)")]
        [Min(0f)] public float comboResetTime = 1.2f;

        [Header("Spezialangriff (Taste 2)")]
        public string specialName = "Wuchtschlag";
        public AttackStep special = AttackStep.Create("Wuchtschlag", 2.2f, 0.25f, 0.1f, 0.45f, 150f, 0.09f, 0.35f, 0.6f);
        [Min(0f)] public float specialCooldown = 6f;

        [Header("Angriffe im Sprung (nur wenn aktiviert)")]
        [Tooltip("Eigene Luftangriffe statt der normalen Kombo")]
        public bool hasAirAttacks;
        public string airAttackName = "Luftschlag";
        public AttackStep airAttack = AttackStep.Create("Luftschlag", 1.3f, 0.10f, 0.10f, 0.30f, 180f, 0.05f, 0.15f, 0.3f);
        public string plungeName = "Sturzangriff";
        [Tooltip("Taste 2 in der Luft: nach unten stossen, Flaechenschaden bei der Landung")]
        public AttackStep plunge = AttackStep.Create("Sturzangriff", 2.8f, 0.12f, 0f, 0.45f, 360f, 0.1f, 0.8f, 0.9f);
        [Tooltip("Radius der Schockwelle beim Sturzangriff (m)")]
        [Min(0.5f)] public float plungeRadius = 4f;

        [Header("Ressource")]
        public WeaponResource resource = WeaponResource.None;
        [Min(0)] public int magazineSize = 0;
        [Min(0f)] public float reloadTime = 2f;

        [Header("Geschoss (Projectile / Spell)")]
        [Tooltip("Welche Munition die Waffe verschiesst (Bogen: Pfeile). None = keine (Zauber)")]
        public AmmoType ammoType = AmmoType.None;
        [Tooltip("Fluggeschwindigkeit (m/s)")]
        [Min(1f)] public float projectileSpeed = 40f;
        [Tooltip("Schwerkraft des Geschosses beim freien Zielen (m/s²)")]
        [Min(0f)] public float projectileGravity = 9.81f;
        [Tooltip("Tote Zone: naeher als das kann nicht geschossen werden (WoW: 8 Yards ≈ 7,3 m)")]
        [Min(0f)] public float minRange = 0f;
        [Tooltip("Zielen/Wirken vor dem Schuss (s) – Bewegung unterbricht")]
        [Min(0f)] public float aimTime = 0f;
        [Tooltip("Waffentempo: Sekunden zwischen automatischen Schuessen (vor Koecher-Tempo)")]
        [Min(0.3f)] public float attackSpeed = 2.8f;

        [Header("Streuung (nur Spread)")]
        [Min(1)] public int pellets = 1;
        [Range(0f, 30f)] public float spreadAngle = 0f;

        [Header("Darstellung")]
        [Tooltip("Farbe von Muendungsfeuer / Schlagspur")]
        public Color effectColor = new Color(1f, 0.75f, 0.4f);
        [Tooltip("Animationsset (Milestone 2b), z. B. 'onehand_sword', 'pistol'")]
        public string animationSet = "onehand_sword";

        [Header("Klaenge (optional – ohne Clip wird ein Platzhalter-Klang erzeugt)")]
        public AudioClip attackSound;
        public AudioClip hitSound;
        public AudioClip reloadSound;

        [Header("Legendaer: Nachhall (verzoegerte Teilwiederholung, nur Server)")]
        [Tooltip("Chance je Treffer, dass der Treffer kurz darauf als Teilschaden nachhallt (0 = aus)")]
        [Range(0f, 1f)] public float echoChance;
        [Tooltip("Anteil des endgueltigen Trefferschadens, den der Nachhall verursacht")]
        [Range(0f, 1f)] public float echoFraction = 0.3f;
        [Tooltip("Verzoegerung bis zum Nachhall (s)")]
        [Min(0f)] public float echoDelay = 0.6f;
        [Tooltip("Interne Abklingzeit je Traeger (s)")]
        [Min(0f)] public float echoCooldown = 6f;

        /// <summary>Waffe besitzt die Nachhall-Eigenschaft (Server: <c>WeaponEcho</c>).</summary>
        public bool HasEcho => echoChance > 0f && echoFraction > 0f;

        public bool IsMelee => archetype == WeaponArchetype.MeleeArc;
        public bool IsSpell => archetype == WeaponArchetype.Spell;
        /// <summary>Wird wie eine Nahkampfwaffe getragen und gefuehrt (Nahkampf, Stab) – Bogen/Feuerwaffen nicht.</summary>
        public bool HeldLikeMelee => IsMelee || IsSpell;
        public bool IsProjectile => archetype == WeaponArchetype.Projectile || archetype == WeaponArchetype.Spell;
        public bool UsesAmmo => resource == WeaponResource.Ammo && magazineSize > 0;

        protected override string IdPrefix => "weapon";

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (maxDamage < minDamage) maxDamage = minDamage;
            if (combo == null || combo.Length == 0) combo = new[] { AttackStep.Create("Angriff", 1f, 0.1f, 0.08f, 0.3f) };
        }
#endif
    }
}
