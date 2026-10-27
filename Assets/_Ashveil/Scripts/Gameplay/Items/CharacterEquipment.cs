using System;
using Ashveil.Data;
using Ashveil.Gameplay.Combat;
using Ashveil.Gameplay.Weapons;
using UnityEngine;

namespace Ashveil.Gameplay.Items
{
    /// <summary>
    /// Angelegte Ausruestung einer Figur (Spieler, spaeter NPCs): setzt Waffen fuer den <see cref="WeaponUser"/>,
    /// liefert Kampfwerte (Schaden, Krit, Ruestung) und – nur auf der massgeblichen Seite – das maximale Leben.
    /// Weiss nichts vom Netz: der Netzwerk-Layer ruft <see cref="Apply"/> auf, sobald sich etwas aendert.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterEquipment : MonoBehaviour, ICombatModifiers, IDamageMitigation, IDamageBlocker, IDodger, IThreatModifier
    {
        private readonly ItemStack[] _equipped = new ItemStack[InventoryModel.EquipCount];
        private WeaponUser _weapons;
        private Health _health;

        public EquipmentStats Stats { get; private set; }
        /// <summary>Gewaehlte Talente (P1) – setzt der Netz-Layer, eingerechnet bei jedem <see cref="Apply"/>.</summary>
        public TalentBonuses Talents { get; private set; } = TalentBonuses.Empty;
        public CharacterClassDefinition Class { get; private set; }
        public int Level { get; private set; } = 1;
        public int AttackerLevel => Level;
        public ItemStack[] Equipped => _equipped;

        public event Action Applied;

        private void Awake()
        {
            _weapons = GetComponent<WeaponUser>();
            _health = GetComponent<Health>();
        }

        /// <param name="authoritative">true = Server/Einzelspiel: maximales Leben wird gesetzt.</param>
        public void Apply(ItemStack[] equipment, CharacterClassDefinition cls, int level, bool authoritative)
        {
            for (int i = 0; i < _equipped.Length; i++)
                _equipped[i] = equipment != null && i < equipment.Length ? equipment[i] : ItemStack.Empty;
            Class = cls;
            Level = Mathf.Max(1, level);
            _lastAuthoritative = authoritative;
            EquipmentStats st = EquipmentStats.From(_equipped, Level);
            st.AddTalents(Talents);
            Stats = st;
            // Ausweichrolle ist eine Faehigkeit des Aschenjaegers (Max 30.09.); alle anderen weichen nur passiv aus
            var motor = GetComponent<Ashveil.Gameplay.Characters.CharacterMotor>();
            if (motor != null) motor.CanRoll = cls == null || cls.canRoll;

            if (_weapons == null) _weapons = GetComponent<WeaponUser>();
            if (_weapons != null)
            {
                ResolveWeapons(_equipped, out WeaponDefinition melee, out WeaponDefinition ranged, out WeaponDefinition offhand);
                _weapons.SetWeapons(melee, ranged, Class != null && Class.dualWield ? offhand : null);
            }
            if (authoritative)
            {
                if (_health == null) _health = GetComponent<Health>();
                if (_health != null) _health.SetMaxHealth(Stats.MaxHealth);
            }
            Applied?.Invoke();
        }

        private bool _lastAuthoritative;

        /// <summary>Talente setzen und Werte neu rechnen (gleiche Ausruestung).</summary>
        public void SetTalents(TalentBonuses talents)
        {
            Talents = talents ?? TalentBonuses.Empty;
            Apply(_equipped, Class, Level, _lastAuthoritative);
        }

        /// <summary>
        /// Wie WoW: Haupthand = Nahkampf- oder Zauberwaffe (Stab), Nebenhand = zweite Nahkampfwaffe (Beidhaendig),
        /// Fernkampfplatz = Bogen/Armbrust. F wechselt zwischen Nah- und Fernkampf. Ohne Nahkampfwaffe kaempft man mit den Faeusten.
        /// </summary>
        public static void ResolveWeapons(ItemStack[] equipment, out WeaponDefinition melee, out WeaponDefinition ranged, out WeaponDefinition offhand)
        {
            WeaponDefinition main = WeaponIn(equipment, EquipmentSlot.MainHand);
            WeaponDefinition off = WeaponIn(equipment, EquipmentSlot.OffHand);
            WeaponDefinition rng = WeaponIn(equipment, EquipmentSlot.Ranged);
            melee = main != null && (main.IsMelee || main.IsSpell) ? main : null;
            offhand = off != null && off.IsMelee ? off : null;
            if (melee == null && offhand != null) { melee = offhand; offhand = null; }
            ranged = rng != null && !rng.IsMelee && !rng.IsSpell ? rng : null;
            // Alte Staende: Fernwaffe noch in einer Hand
            if (ranged == null && main != null && !main.IsMelee && !main.IsSpell) ranged = main;
            if (ranged == null && off != null && !off.IsMelee && !off.IsSpell) ranged = off;
            if (melee == null) melee = GameDataBootstrap.Unarmed;
        }

        public static void ResolveWeapons(ItemStack[] equipment, out WeaponDefinition melee, out WeaponDefinition ranged) =>
            ResolveWeapons(equipment, out melee, out ranged, out _);

        private static WeaponDefinition WeaponIn(ItemStack[] equipment, EquipmentSlot slot)
        {
            int i = (int)slot;
            if (equipment == null || i >= equipment.Length) return null;
            ItemStack s = equipment[i];
            ItemDefinition def = s.IsEmpty ? null : GameData.Item(s.id);
            return def != null && def.IsWeapon ? def.weapon : null;
        }

        // ---------------------------------------------------------------- Kampfwerte

        public float OutgoingDamageMultiplier(bool melee) => Stats.DamageMultiplier(melee);
        public float CritChanceBonus => Stats.CritChance;
        public float LifeOnHit => Stats.LifeOnHit;

        public float Mitigate(float amount) => amount * (1f - Stats.ArmorReduction(Level));

        /// <summary>
        /// Bedrohung: Tank-Klassen (Eidwaechter) erzeugen 50 % mehr – wie die Verteidigungshaltung in Classic, nur ohne
        /// Haltungswechsel. Talente legen Prozente drauf.
        /// </summary>
        public float ThreatMultiplier =>
            (Class != null && (Class.roles & ClassRole.Tank) != 0 ? 1.5f : 1f) * (1f + Talents.Get(TalentStat.ThreatPercent) / 100f);

        // ---------------------------------------------------------------- Schildblock (passiv)

        private float _masteryBlockChance, _masteryBlockValue;

        /// <summary>Server (Tests): fester Wurf 0..1 statt Zufall; negativ = aus.</summary>
        public float BlockRollOverride { get; set; } = -1f;

        // ---------------------------------------------------------------- Ausweichen (passiv, WoW Classic)

        /// <summary>Tests: fester Wurf 0..1 statt Zufall (&lt; 0 = Zufall).</summary>
        public float DodgeRollOverride { get; set; } = -1f;
        public const float BaseDodgeChance = 0.05f;

        /// <summary>5 % Grundwert + 1 % je 20 Geschick, hoechstens 30 %.</summary>
        public float DodgeChance => DodgeChanceFor(Stats);
        public static float DodgeChanceFor(EquipmentStats st) => Mathf.Min(0.3f + st.DodgePercent / 100f, BaseDodgeChance + st.Agility * 0.0005f + st.DodgePercent / 100f);

        /// <summary>Server: weicht dieser Nahkampfschlag aus? Nur von vorn (wer von hinten schlaegt, trifft – wie WoW).</summary>
        /// <summary>Tests schalten den Zufall aus (feste Schadenspruefungen).</summary>
        public static bool DodgeEnabled = true;

        public bool TryDodge(in DamageInfo info)
        {
            if (!DodgeEnabled || !info.Dodgeable || (_health != null && !_health.IsAlive)) return false;
            Vector3 fromAttacker = -info.Direction;
            fromAttacker.y = 0f;
            if (fromAttacker.sqrMagnitude > 1e-4f && Vector3.Dot(transform.forward, fromAttacker.normalized) < 0f) return false;
            float roll = DodgeRollOverride >= 0f ? DodgeRollOverride : UnityEngine.Random.value;
            return roll < DodgeChance;
        }

        /// <summary>Server: Wirkung der Meisterschaft Schildblock (zusaetzliche Chance, +Anteil Blockwert).</summary>
        public void SetBlockMastery(float chanceBonus, float valueBonus)
        {
            _masteryBlockChance = Mathf.Clamp(chanceBonus, 0f, 0.2f);
            _masteryBlockValue = Mathf.Clamp(valueBonus, 0f, 1f);
        }

        public bool HasShield => Stats.HasShield;
        public float BlockChance => Stats.HasShield ? EquipmentStats.BaseBlockChance + _masteryBlockChance + Stats.BlockPercent / 100f : 0f;
        public float BlockValue => Mathf.Round(Stats.BlockValue * (1f + _masteryBlockValue));

        /// <summary>Nur Treffer von vorn (bis 90° zur Blickrichtung) koennen geblockt werden – Fernkampf eingeschlossen.</summary>
        public float Block(ref DamageInfo info, float amount)
        {
            if (!Stats.HasShield || amount <= 0f) return amount;
            if (!IsFrontal(info)) return amount;
            float roll = BlockRollOverride >= 0f ? BlockRollOverride : UnityEngine.Random.value;
            if (roll >= BlockChance) return amount;
            float blocked = Mathf.Min(amount, BlockValue);
            info.Blocked = blocked;
            return amount - blocked;
        }

        private bool IsFrontal(in DamageInfo info)
        {
            Vector3 toAttacker = -info.Direction;
            if (info.Source != null)
            {
                Vector3 d = info.Source.transform.position - transform.position;
                if (d.sqrMagnitude > 0.01f) toAttacker = d;
            }
            toAttacker.y = 0f;
            if (toAttacker.sqrMagnitude < 1e-4f) return true;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            return Vector3.Dot(fwd.normalized, toAttacker.normalized) >= 0f;
        }
    }
}
